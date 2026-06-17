using CopperMod.Amiga;
using CopperDisk;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBusTimingTests
{
	private const int HrmLowResPlane1FetchSlot = 7;

	[Fact]
	public void ZeroWaitArbiterGrantsImmediatelyAndUsesConfiguredAccessCycles()
	{
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Cpu,
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			123,
			isWrite: false);

		var result = new ZeroWaitBusArbiter(baseAccessCycles: 4).Arbitrate(request);

		Assert.Equal(123, result.RequestedCycle);
		Assert.Equal(123, result.GrantedCycle);
		Assert.Equal(127, result.CompletedCycle);
		Assert.Equal(0, result.WaitCycles);
		Assert.Equal(4, result.AccessCycles);
	}

	[Fact]
	public void AgnusChipSlotSchedulerAlignsRequestsToChipClockGrid()
	{
		Assert.Equal(0, AgnusChipSlotScheduler.AlignToSlot(0));
		Assert.Equal(2, AgnusChipSlotScheduler.AlignToSlot(1));
		Assert.Equal(2, AgnusChipSlotScheduler.AlignToSlot(2));
	}

	[Fact]
	public void A500PalFrameUsesLong313LineCadenceByDefault()
	{
		Assert.Equal(313, AmigaConstants.A500PalRasterLines);
		Assert.Equal(
			AmigaConstants.A500PalCpuCyclesPerRasterLine * 313,
			AmigaConstants.A500PalCpuCyclesPerFrame);
	}

	[Fact]
	public void CopperSlotsStayEvenRelativeToEachRasterLine()
	{
		var engine = new AgnusHrmSlotEngine();
		var oddLineDiskSlot = AmigaConstants.A500PalCpuCyclesPerRasterLine +
			(0x08 * AgnusChipSlotScheduler.SlotCycles);

		var access = engine.ReserveCopperDmaSlot(0x1000, oddLineDiskSlot);

		Assert.Equal(oddLineDiskSlot, access.GrantedCycle);
		Assert.True(AgnusHrmOcsSlotTable.IsCopperAccessSlot(access.GrantedCycle));
		Assert.False(AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(access.GrantedCycle));
	}

	[Fact]
	public void PseudoFastUsesChipSlotSchedulerButRealFastIsZeroWait()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000,
			enableLiveAgnusDma: true);

		var pseudoFastCycle = 20L;
		_ = bus.ReadWord(AmigaConstants.A500BootPseudoFastRamBase, ref pseudoFastCycle, AmigaBusAccessKind.CpuDataRead);
		var realFastCycle = 20L;
		_ = bus.ReadWord(AmigaConstants.A500RealFastRamBase, ref realFastCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(20 + AgnusChipSlotScheduler.SlotCycles, pseudoFastCycle);
		Assert.Equal(20, realFastCycle);
		Assert.Contains(bus.BusAccesses, access => access.Request.Target == AmigaBusAccessTarget.ExpansionRam);
		Assert.Contains(bus.BusAccesses, access => access.Request.Target == AmigaBusAccessTarget.RealFastRam);
	}

	[Fact]
	public void RealTimeClockUsesChipSlotScheduler()
	{
		var now = new DateTimeOffset(2026, 6, 14, 21, 45, 37, TimeSpan.Zero);
		var bus = new AmigaBus(
			realTimeClockEnabled: true,
			realTimeClockNowProvider: () => now,
			enableLiveAgnusDma: true);

		var cycle = 20L;
		_ = bus.ReadByte(AmigaRealTimeClock.BaseAddress, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(20 + AgnusChipSlotScheduler.SlotCycles, cycle);
		Assert.Contains(bus.BusAccesses, access => access.Request.Target == AmigaBusAccessTarget.RealTimeClock);
	}

	[Fact]
	public void BlitterNastyModeStallsPseudoFastButNotRealFastRomOrCiaAccesses()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000);
		bus.MapReadOnlyMemory(0x00FC0000, new byte[] { 0x12, 0x34 });
		StartNastyBlit(bus);

		var expansionCycle = 0L;
		_ = bus.ReadWord(AmigaConstants.A500BootPseudoFastRamBase, ref expansionCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.True(expansionCycle > 0);

		var realFastCycle = 0L;
		_ = bus.ReadWord(AmigaConstants.A500RealFastRamBase, ref realFastCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, realFastCycle);

		var romCycle = 0L;
		Assert.Equal(0x12, bus.ReadByte(0x00FC0000, ref romCycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(0, romCycle);

		var ciaCycle = 0L;
		_ = bus.ReadByte(0x00BFE001, ref ciaCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(ExpectedCiaAccessCycle(0), ciaCycle);
		Assert.True(bus.Blitter.CaptureSnapshot().Busy);
	}

	[Theory]
	[InlineData(0, 10)]
	[InlineData(1, 10)]
	[InlineData(9, 10)]
	[InlineData(10, 20)]
	[InlineData(11, 20)]
	public void CpuCiaByteReadWaitsForPeripheralEClockCycle(long requestedCycle, long expectedCycle)
	{
		var bus = new AmigaBus();
		var cycle = requestedCycle;

		_ = bus.ReadByte(0x00BFE001, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(expectedCycle, cycle);
		var access = Assert.Single(bus.BusAccesses, access =>
			access.Request.Target == AmigaBusAccessTarget.Cia &&
			access.Request.Kind == AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(requestedCycle, access.RequestedCycle);
		Assert.Equal(expectedCycle, access.GrantedCycle);
		Assert.Equal(expectedCycle, access.CompletedCycle);
	}

	[Fact]
	public void CpuCiaReadAdvancesTodToGrantedCycle()
	{
		var bus = new AmigaBus();
		var hsyncCycle = (long)AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var beforeRequest = hsyncCycle - (2 * AmigaConstants.A500PalCpuCyclesPerCiaTick);
		Assert.True(ExpectedCiaAccessCycle(beforeRequest) < hsyncCycle);

		var cycle = beforeRequest;
		Assert.Equal(0x00, bus.ReadByte(0x00BFD800, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(ExpectedCiaAccessCycle(beforeRequest), cycle);

		cycle = hsyncCycle;
		Assert.Equal(0x01, bus.ReadByte(0x00BFD800, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(ExpectedCiaAccessCycle(hsyncCycle), cycle);
	}

	[Theory]
	[InlineData(0, 10)]
	[InlineData(1, 10)]
	[InlineData(9, 10)]
	[InlineData(10, 20)]
	[InlineData(11, 20)]
	public void CpuCiaByteWriteWaitsForPeripheralEClockCycle(long requestedCycle, long expectedCycle)
	{
		var bus = new AmigaBus();
		var cycle = requestedCycle;

		bus.WriteByte(0x00BFE001, 0x00, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(expectedCycle, cycle);
		var access = Assert.Single(bus.BusAccesses, access =>
			access.Request.Target == AmigaBusAccessTarget.Cia &&
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite);
		Assert.Equal(requestedCycle, access.RequestedCycle);
		Assert.Equal(expectedCycle, access.GrantedCycle);
		Assert.Equal(expectedCycle, access.CompletedCycle);
	}

	[Fact]
	public void NonNastyBlitterStallsCpuByAtMostTwoSlots()
	{
		// Without BLTPRI: Blitter takes its DMA slots; CPU is restricted to even-phase slots.
		// If Blitter occupies an even slot, CPU bumps to the next even slot (2 slots later).
		var bus = new AmigaBus();
		var blitterCycle = 20L; // even absolute slot
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1000, blitterCycle);

		var cycle = blitterCycle;
		_ = bus.ReadWord(0x00001002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		// Slot 20 (even) is taken by Blitter. Slot 22 is odd (not CPU-accessible). Next even slot is 24.
		Assert.Equal(blitterCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cpu.GrantedCycle);
		Assert.Equal(blitterCycle + (3 * AgnusChipSlotScheduler.SlotCycles), cycle);
	}

	[Fact]
	public void LiveAgnusBeamSchedulerUsesIntegerBeamGrid()
	{
		var bus = new AmigaBus();
		bus.EnableLiveAgnusDma();
		var target = (5 * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(17 * AmigaConstants.A500PalCpuCyclesPerColorClock);

		bus.AdvanceDmaTo(target);

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(target, snapshot.CurrentCycle);
		Assert.Equal(5, snapshot.BeamLine);
		Assert.Equal(17, snapshot.BeamHorizontal);
		Assert.Equal(0, snapshot.FrameStartCycle);
	}

	[Fact]
	public void FastSlotPathReturnsCpuCycleAtCompletionButAppliesWriteAtGrant()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true);
		var cycle = 20L;

		bus.WriteWord(0x00DFF180, 0x0F00, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(20, bus.CustomRegisterWrites.Single().Cycle);
		Assert.Equal(20 + AgnusChipSlotScheduler.SlotCycles, cycle);
		Assert.Equal(cycle, bus.Agnus.CaptureSnapshot().CurrentCycle);
	}

	[Fact]
	public void FastSlotPathConsumesBothSlotsForCpuLongChipAccess()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true);
		var cycle = 20L;

		bus.WriteLong(0x00001000, 0x12345678, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(20 + (3 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Equal(cycle, bus.Agnus.CaptureSnapshot().CurrentCycle);
		Assert.Equal(0x12345678u, BigEndian.ReadUInt32(bus.ChipRam, 0x1000, "long chip write"));
		Assert.Equal(0x0000, bus.ReadChipWordForPresentation(0x1002, 23));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x1002, 24));
	}

	[Fact]
	public void CpuLongCustomWriteAppliesWordsOnSeparateSlots()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true);
		var cycle = 20L;

		bus.WriteLong(0x00DFF180, 0x0F00000F, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(20 + (3 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Collection(
			bus.CustomRegisterWrites,
			write =>
			{
				Assert.Equal(20, write.Cycle);
				Assert.Equal(0x180, write.Address);
				Assert.Equal(0x0F00, write.Value);
			},
			write =>
			{
				Assert.Equal(24, write.Cycle);
				Assert.Equal(0x182, write.Address);
				Assert.Equal(0x000F, write.Value);
			});
	}

	[Fact]
	public void TimedCpuBeamRegisterReadSamplesGrantedSlot()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true);
		var requestedCycle = (5L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(10 * AgnusChipSlotScheduler.SlotCycles);
		var cycle = requestedCycle;

		var vhposr = bus.ReadWord(0x00DFF006, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(5, (vhposr >> 8) & 0x00FF);
		Assert.Equal(11, vhposr & 0x00FF);
		Assert.Equal(requestedCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
	}

	[Fact]
	public void TimedCpuLongBeamReadSamplesSecondWordAtSecondSlot()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true);
		var requestedCycle = (5L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(10 * AgnusChipSlotScheduler.SlotCycles);
		var cycle = requestedCycle;

		var beam = bus.ReadLong(0x00DFF004, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(5u, (beam >> 8) & 0x01FF);
		Assert.Equal(13u, beam & 0x00FF);
		Assert.Equal(requestedCycle + (4 * AgnusChipSlotScheduler.SlotCycles), cycle);
	}

	[Fact]
	public void UntimedBeamReadKeepsLastRasterPositionForInspection()
	{
		var bus = new AmigaBus();
		var targetCycle = (300L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(7 * AgnusChipSlotScheduler.SlotCycles);
		bus.AdvanceRasterTo(targetCycle);

		var beam = bus.ReadLong(0x00DFF004);

		Assert.Equal(300u, (beam >> 8) & 0x01FF);
		Assert.Equal(7u, beam & 0x00FF);
	}

	[Fact]
	public void LiveDisplayDmaSlotStallsCpuChipAccess()
	{
		var bus = new AmigaBus();
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		bus.WriteWord(0x00DFF180, 0x0000);
		bus.WriteWord(0x00DFF182, 0x0F00);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();

		var cycle = fetchCycle;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var bitplane = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.Request.RequestedCycle == fetchCycle);
		Assert.Equal(fetchCycle, bitplane.GrantedCycle);
		var cpu = bus.BusAccesses.Last(access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.ChipRam);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, cpu.GrantedCycle);
		Assert.Equal(fetchCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.True(bus.Agnus.CaptureSnapshot().CpuChipStallCycles >= AgnusChipSlotScheduler.SlotCycles);
	}

	[Fact]
	public void BlitterSearchSkipsFutureLiveBitplaneSlot()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		var requestedCycle = fetchCycle - AgnusChipSlotScheduler.SlotCycles;
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0000);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();

		var blocker = bus.ReadChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x2000,
			requestedCycle);
		var write = bus.WriteChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x1000,
			0xFFFF,
			requestedCycle);

		Assert.Equal(requestedCycle, blocker.BusAccess.GrantedCycle);
		Assert.True(write.GrantedCycle > fetchCycle, $"write.GrantedCycle={write.GrantedCycle}, fetchCycle={fetchCycle}");
		var bitplane = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.Request.RequestedCycle == fetchCycle);
		Assert.Equal(fetchCycle, bitplane.GrantedCycle);
		Assert.Equal(0x0000, bus.ReadChipWordForPresentation(0x1000, fetchCycle));
	}

	[Fact]
	public void LiveCaptureInvalidationClearsStaleBitplaneSlotReservations()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xFFFF);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();

		bus.Display.CaptureLiveDisplayDmaBeforeHrmGrant(fetchCycle);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);

		bus.Display.ScheduleWrite(fetchCycle, 0x0102, 0x0001);
		bus.AdvanceDmaTo(fetchCycle + (4 * AgnusChipSlotScheduler.SlotCycles));

		Assert.Equal(0, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
		Assert.Contains(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.Request.RequestedCycle == fetchCycle &&
				access.CompletedCycle > access.GrantedCycle);
	}

	[Fact]
	public void LiveDisplayPreGrantDoesNotReenterCopperWhileBlitterSatisfiesWait()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		const uint copperList = 0x2400;
		StartLongBlit(bus);
		var expectedReadyCycle = bus.Blitter.GetPredictedCompletionCycle();
		WriteCopperList(
			bus,
			copperList,
			(0x0001, 0x00FE),
			(0x009C, (ushort)(0x8000 | AmigaConstants.IntreqCopper)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);

		bus.AdvanceDmaTo(expectedReadyCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			(write.Value & AmigaConstants.IntreqCopper) != 0);
		Assert.True(
			intreqWrite.Cycle >= expectedReadyCycle,
			$"Copper resumed before blitter completion: intreq={intreqWrite.Cycle}, blitter={expectedReadyCycle}");
	}

	[Fact]
	public void LiveCopperIntreqMoveIsVisibleBeforePaulaConsumesTargetCycle()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		const uint copperList = 0x2400;
		WriteCopperList(
			bus,
			copperList,
			(0x0001, 0x00FE),
			(0x009C, (ushort)(0x8000 | AmigaConstants.IntreqCopper)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqCopper));
		bus.WriteWord(0x00DFF096, 0x8280);

		bus.AdvanceDmaTo(AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			(write.Value & AmigaConstants.IntreqCopper) != 0);
		var cpuVisibleCycle = intreqWrite.Cycle +
			AmigaConstants.A500CopperIntreqDelayCpuCycles +
			AmigaConstants.A500InterruptRecognitionDelayCpuCycles;
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqCopper);
		Assert.Equal(3, bus.Paula.GetHighestPendingInterruptLevel());
		Assert.Equal(0, bus.Paula.GetHighestCpuVisibleInterruptLevel(cpuVisibleCycle - 1));
		Assert.Equal(3, bus.Paula.GetHighestCpuVisibleInterruptLevel(cpuVisibleCycle));
	}

	[Fact]
	public void LiveDisplayDmaStallsPseudoFastButNotRealFastRomOrCiaAccesses()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		bus.MapReadOnlyMemory(0x00FC0000, new byte[] { 0x12, 0x34 });
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
		bus.AdvanceDmaTo(fetchCycle);

		var expansionCycle = fetchCycle;
		_ = bus.ReadWord(AmigaConstants.A500BootPseudoFastRamBase, ref expansionCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.True(expansionCycle > fetchCycle);

		var realFastCycle = fetchCycle;
		_ = bus.ReadWord(AmigaConstants.A500RealFastRamBase, ref realFastCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(fetchCycle, realFastCycle);

		var romCycle = fetchCycle;
		Assert.Equal(0x12, bus.ReadByte(0x00FC0000, ref romCycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(fetchCycle, romCycle);

		var ciaCycle = fetchCycle;
		_ = bus.ReadByte(0x00BFE001, ref ciaCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(ExpectedCiaAccessCycle(fetchCycle), ciaCycle);
	}

	[Fact]
	public void LiveDisplayDmaSlotStallsCpuCustomRegisterAccess()
	{
		var bus = new AmigaBus();
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
		bus.AdvanceDmaTo(fetchCycle);

		var cycle = fetchCycle;
		bus.WriteWord(0x00DFF180, 0x0F00, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var cpuCustom = bus.BusAccesses.Last(access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, cpuCustom.GrantedCycle);
		Assert.Equal(fetchCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.True(bus.Agnus.CaptureSnapshot().CpuChipStallCycles >= AgnusChipSlotScheduler.SlotCycles);
	}

	[Fact]
	public void IntreqPollingPreparesLiveBitplaneSlotsWithoutCapturingFetchWords()
	{
		var bus = new AmigaBus();
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();

		var initialFetchWords = bus.Display.LiveFetchBatchWordCount;
		var cycle = fetchCycle;
		_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var cpuCustom = bus.BusAccesses.Last(access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, cpuCustom.GrantedCycle);
		Assert.Equal(fetchCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Equal(initialFetchWords, bus.Display.LiveFetchBatchWordCount);
		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.True(snapshot.BitplaneSlotGrantCount > 0);
		Assert.True(snapshot.CpuChipStallCycles >= AgnusChipSlotScheduler.SlotCycles);

		cycle = fetchCycle + (4 * AgnusChipSlotScheduler.SlotCycles);
		_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(initialFetchWords, bus.Display.LiveFetchBatchWordCount);
	}

	[Fact]
	public void IntreqPollingDoesNotPreventLaterLiveBitplaneCapture()
	{
		var bus = new AmigaBus();
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		var frameStopCycle = AmigaConstants.A500PalCpuCyclesPerFrame - 1;
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();

		var initialFetchWords = bus.Display.LiveFetchBatchWordCount;
		for (var requestedCycle = fetchCycle; requestedCycle <= frameStopCycle + 64; requestedCycle += 32)
		{
			var cycle = requestedCycle;
			_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		}

		Assert.Equal(initialFetchWords, bus.Display.LiveFetchBatchWordCount);

		bus.AdvanceDmaTo(frameStopCycle);

		Assert.True(
			bus.Display.LiveFetchBatchWordCount > initialFetchWords,
			$"Expected the later full DMA advance to capture bitplane words after INTREQ polling, liveWords={bus.Display.LiveFetchBatchWordCount}");
	}

	[Fact]
	public void OnlyDiskDataRegistersRequirePassiveDiskInputAdvance()
	{
		Assert.False(AmigaDiskController.RequiresPassiveInputAdvance(0x00DFF01E));
		Assert.True(AmigaDiskController.RequiresPassiveInputAdvance(0x00DFF008));
		Assert.True(AmigaDiskController.RequiresPassiveInputAdvance(0x00DFF01A));
	}

	[Fact]
	public void BeamAndInputRegisterReadsDoNotAdvanceAudioOrPassiveDiskInput()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.Paula.ScheduleWrite(0, 0x0A6, 0x0004);
		bus.Paula.ScheduleWrite(0, 0x0AA, 0x1234);
		bus.Paula.AdvanceTo(0);
		var audioBefore = bus.Paula.GetChannelSnapshot(0);
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);

		var beamCycle = targetCycle;
		_ = bus.ReadWord(0x00DFF006, ref beamCycle, AmigaBusAccessKind.CpuDataRead);
		var joyCycle = targetCycle;
		_ = bus.ReadWord(0x00DFF00A, ref joyCycle, AmigaBusAccessKind.CpuDataRead);
		var potCycle = targetCycle;
		_ = bus.ReadWord(0x00DFF016, ref potCycle, AmigaBusAccessKind.CpuDataRead);

		var audioAfter = bus.Paula.GetChannelSnapshot(0);
		Assert.Equal(audioBefore.NextSampleCycle, audioAfter.NextSampleCycle);
		Assert.Equal(audioBefore.CurrentSample, audioAfter.CurrentSample);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);
	}

	[Fact]
	public void RegisterLatchReadsOnlyFlushDueCustomWrites()
	{
		var bus = new AmigaBus();
		bus.Paula.ScheduleWrite(20, 0x09E, 0x8001);

		var beforeCycle = 10L;
		Assert.Equal(0, bus.ReadWord(0x00DFF010, ref beforeCycle, AmigaBusAccessKind.CpuDataRead));

		var atCycle = 20L;
		Assert.Equal(1, bus.ReadWord(0x00DFF010, ref atCycle, AmigaBusAccessKind.CpuDataRead));
	}

	[Fact]
	public void RegisterLatchReadsDoNotAdvanceAudioOrPassiveDiskInput()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.Paula.ScheduleWrite(0, 0x0A6, 0x0004);
		bus.Paula.ScheduleWrite(0, 0x0AA, 0x1234);
		bus.Paula.AdvanceTo(0);
		var audioBefore = bus.Paula.GetChannelSnapshot(0);
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);

		var cycle = targetCycle;
		_ = bus.ReadWord(0x00DFF010, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var audioAfter = bus.Paula.GetChannelSnapshot(0);
		Assert.Equal(audioBefore.NextSampleCycle, audioAfter.NextSampleCycle);
		Assert.Equal(audioBefore.CurrentSample, audioAfter.CurrentSample);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);
	}

	[Fact]
	public void DiskStatusRegisterReadsAdvanceEventsWithoutPassiveInput()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);

		var cycle = targetCycle;
		_ = bus.ReadWord(0x00DFF024, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);

		var dskCycle = cycle;
		var dskbytr = bus.ReadWord(0x00DFF01A, ref dskCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.NotEqual(0, dskbytr & 0x8000);
		Assert.Equal(0x12, dskbytr & 0x00FF);
	}

	[Fact]
	public void CiaPollingAdvancesEventsWithoutPassiveDiskInput()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.WriteByte(0x00BFD400, 0x03, 0);
		bus.WriteByte(0x00BFD500, 0x00, 0);
		bus.WriteByte(0x00BFDD00, 0x81, 0);
		bus.WriteByte(0x00BFDE00, 0x11, 0);
		Assert.Empty(bus.DrainCiaInterrupts());

		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);
		var initialDskbytr = bus.Disk.CaptureSnapshot().Dskbytr;
		var cycle = targetCycle;
		_ = bus.ReadByte(0x00BFDE00, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var ciaGrantCycle = ExpectedCiaAccessCycle(targetCycle);
		Assert.Equal(ciaGrantCycle, cycle);
		var timerEvents = bus.DrainCiaInterrupts()
			.Where(e => e.Cia == AmigaCiaId.B && e.IcrBits == AmigaCia.TimerAInterruptMask)
			.ToArray();
		var timerEvent = Assert.Single(timerEvents);
		Assert.Equal(40, timerEvent.Cycle);
		Assert.Equal(initialDskbytr, bus.Disk.CaptureSnapshot().Dskbytr);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);

		cycle = targetCycle;
		_ = bus.ReadByte(0x00BFE001, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);

		cycle = ciaGrantCycle;
		var dskbytr = bus.ReadWord(0x00DFF01A, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.NotEqual(0, dskbytr & 0x8000);
		Assert.Equal(0x12, dskbytr & 0x00FF);
	}

	[Fact]
	public void CiaBDriveControlWriteAdvancesEventsWithoutPassiveDiskInput()
	{
		var bus = new AmigaBus();
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1212));
		bus.WriteByte(0x00BFD100, 0xFF, 0);
		bus.WriteByte(0x00BFD300, 0xFF, 0);
		bus.WriteByte(0x00BFD100, 0x77, 0);

		var readyCycle = ExpectedCiaAccessCycle(0) + MotorReadyDelayCycles();
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);
		var initialDskbytr = bus.Disk.CaptureSnapshot().Dskbytr;
		var cycle = targetCycle;
		bus.WriteByte(0x00BFD100, 0x76, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var ciaGrantCycle = ExpectedCiaAccessCycle(targetCycle);
		Assert.Equal(ciaGrantCycle, cycle);
		Assert.Equal(initialDskbytr, bus.Disk.CaptureSnapshot().Dskbytr);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);

		var dskCycle = ciaGrantCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);
		var dskbytr = bus.ReadWord(0x00DFF01A, ref dskCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.NotEqual(0, dskbytr & 0x8000);
		Assert.Equal(0x12, dskbytr & 0x00FF);
	}

	[Fact]
	public void DmaconrPollingAdvancesBlitterWithoutAdvancingAudioPlayback()
	{
		var bus = new AmigaBus();
		bus.Paula.ScheduleWrite(0, 0x0A6, 0x0004);
		bus.Paula.ScheduleWrite(0, 0x0AA, 0x1234);
		bus.Paula.AdvanceTo(0);
		StartLongBlit(bus);
		var audioBefore = bus.Paula.GetChannelSnapshot(0);
		var completionCycle = bus.Blitter.GetPredictedCompletionCycle();

		var cycle = Math.Max(1, completionCycle - 10);
		var busy = bus.ReadByte(0x00DFF002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, busy & 0x40);
		var audioAfterBusyPoll = bus.Paula.GetChannelSnapshot(0);
		Assert.Equal(audioBefore.NextSampleCycle, audioAfterBusyPoll.NextSampleCycle);
		Assert.Equal(audioBefore.CurrentSample, audioAfterBusyPoll.CurrentSample);

		cycle = completionCycle;
		var complete = bus.ReadByte(0x00DFF002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0, complete & 0x40);
	}

	[Fact]
	public void CiaPollingDoesNotAdvanceDiskDmaCompletion()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.Paula.ScheduleWrite(0, 0x096, 0x8210);
		bus.Paula.AdvanceTo(0);
		bus.Disk.WriteRegister(0x024, 0x8001, 0);
		bus.Disk.WriteRegister(0x024, 0x8001, 0);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);

		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 2);
		var cycle = targetCycle;
		_ = bus.ReadByte(0x00BFDE00, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(ExpectedCiaAccessCycle(targetCycle), cycle);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);

		bus.AdvanceDmaTo(cycle);

		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
	}

	[Fact]
	public void CpuCyclesIncludeDelayedInstructionFetchCompletion()
	{
		var arbiter = new FixedDelayArbiter(waitCycles: 5, accessCycles: 3);
		var bus = new AmigaBus(arbiter: arbiter);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);

		cpu.ExecuteInstruction();

		var fetch = Assert.Single(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch);
		Assert.Equal(0, fetch.RequestedCycle);
		Assert.Equal(5, fetch.GrantedCycle);
		Assert.Equal(8, fetch.CompletedCycle);
		Assert.Equal(12, cpu.State.Cycles);
	}

	[Fact]
	public void DelayedCustomRegisterWritesAreStampedAtGrantedCycle()
	{
		var arbiter = new FixedDelayArbiter(waitCycles: 7, accessCycles: 2);
		var bus = new AmigaBus(arbiter: arbiter);
		var cycle = 100L;

		bus.WriteWord(0x00DFF096, 0x800F, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteWord(0x00DFF0AA, 0x7F81, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteByte(0x00DFF0A8, 0x20, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(127, cycle);
		Assert.Equal(3, bus.CustomRegisterWrites.Count);
		Assert.Equal(107, bus.CustomRegisterWrites[0].Cycle);
		Assert.Equal(116, bus.CustomRegisterWrites[1].Cycle);
		Assert.Equal(125, bus.CustomRegisterWrites[2].Cycle);
		var cpuCustomWrites = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu && access.Request.Target == AmigaBusAccessTarget.CustomRegisters)
			.ToArray();
		Assert.All(
			bus.CustomRegisterWrites.Zip(cpuCustomWrites),
			pair => Assert.Equal(pair.Second.GrantedCycle, pair.First.Cycle));
	}

	[Fact]
	public void AmigaBusClassifiesCpuAccessTargetsForArbitration()
	{
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(0x00FC0000, new byte[] { 0x12, 0x34 });
		bus.RegisterHostTrapStub(0x00F00000, _ => { });

		var cycle = 10L;
		_ = bus.ReadByte(0x00001000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
		cycle = 20;
		bus.WriteWord(0x00DFF096, 0x800F, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		cycle = 30;
		bus.WriteByte(0x00BFE001, 0x00, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		cycle = 40;
		_ = bus.ReadByte(0x00FC0000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		cycle = 50;
		Assert.Equal(0xFF00, bus.ReadWord(0x00F00000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch));

		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch &&
			access.Request.Target == AmigaBusAccessTarget.ChipRam);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters &&
			access.Request.Size == AmigaBusAccessSize.Word);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Target == AmigaBusAccessTarget.Cia &&
			access.Request.Size == AmigaBusAccessSize.Byte);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
			access.Request.Target == AmigaBusAccessTarget.Rom);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch &&
			access.Request.Target == AmigaBusAccessTarget.HostTrap);
	}

	[Fact]
	public void PaulaDmaFetchesUseNamedBusRequestPath()
	{
		var bus = new AmigaBus();
		bus.ChipRam[0x1000] = 0x7F;
		bus.ChipRam[0x1001] = 0x81;
		bus.WriteWord(0x00DFF0A2, 0x1000);
		bus.WriteWord(0x00DFF0A4, 0x0001);
		bus.WriteWord(0x00DFF0A6, 0x0002);
		bus.WriteWord(0x00DFF096, 0x8201);

		bus.Paula.AdvanceTo(0);

		var dma = Assert.Single(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.PaulaDma);
		Assert.Equal(AmigaBusRequester.Paula, dma.Request.Requester);
		Assert.Equal(AmigaBusAccessTarget.ChipRam, dma.Request.Target);
		Assert.Equal(AmigaBusAccessSize.Word, dma.Request.Size);
		Assert.Equal(0x1000u, dma.Request.Address);
		Assert.False(dma.Request.IsWrite);
	}

	[Fact]
	public void HrmSlotTableMatchesPalOcsFixedOwnerCounts()
	{
		var fixedOwners = Enumerable
			.Range(0, AmigaConstants.A500PalColorClocksPerRasterLine)
			.Select(AgnusHrmOcsSlotTable.GetFixedOwner)
			.ToArray();

		Assert.Equal(AgnusHrmOcsSlotTable.RefreshSlotsPerLine, fixedOwners.Count(owner => owner == AgnusChipSlotOwner.Refresh));
		Assert.Equal(AgnusHrmOcsSlotTable.DiskSlotsPerLine, fixedOwners.Count(owner => owner == AgnusChipSlotOwner.Disk));
		Assert.Equal(AgnusHrmOcsSlotTable.AudioSlotsPerLine, fixedOwners.Count(owner => owner == AgnusChipSlotOwner.Paula));
		Assert.Equal(AgnusHrmOcsSlotTable.SpriteSlotsPerLine, fixedOwners.Count(owner => owner == AgnusChipSlotOwner.Sprite));
		Assert.Equal(AgnusChipSlotOwner.Refresh, AgnusHrmOcsSlotTable.GetFixedOwner(0x00));
		Assert.Equal(AgnusChipSlotOwner.Refresh, AgnusHrmOcsSlotTable.GetFixedOwner(0x06));
		Assert.Equal(AgnusChipSlotOwner.Disk, AgnusHrmOcsSlotTable.GetFixedOwner(0x08));
		Assert.Equal(AgnusChipSlotOwner.Paula, AgnusHrmOcsSlotTable.GetFixedOwner(0x10));
		Assert.Equal(AgnusChipSlotOwner.Sprite, AgnusHrmOcsSlotTable.GetFixedOwner(0x18));
		Assert.Equal(AgnusChipSlotOwner.Sprite, AgnusHrmOcsSlotTable.GetFixedOwner(0x36));
		Assert.Equal(AgnusChipSlotOwner.Free, AgnusHrmOcsSlotTable.GetFixedOwner(0x38));
	}

	[Fact]
	public void HrmSlotEngineCommitsMandatoryRefreshSlotsPerLine()
	{
		var bus = new AmigaBus();

		bus.AdvanceDmaTo(AmigaConstants.A500PalCpuCyclesPerRasterLine - 1);

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(AgnusHrmOcsSlotTable.RefreshSlotsPerLine, snapshot.RefreshSlotReservationCount);
		Assert.Equal(AgnusHrmOcsSlotTable.RefreshSlotsPerLine, snapshot.RefreshSlotGrantCount);
		Assert.Equal(0, snapshot.DiskSlotReservationCount);
		Assert.Equal(0, snapshot.PaulaSlotReservationCount);
		Assert.Equal(0, snapshot.SpriteSlotReservationCount);
	}

	[Fact]
	public void HrmSlotEngineGrantsCpuOnlyOnEvenAvailableMemorySlots()
	{
		var bus = new AmigaBus();
		var cycle = 0x41L * AgnusChipSlotScheduler.SlotCycles;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var access = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(0x42L * AgnusChipSlotScheduler.SlotCycles, access.GrantedCycle);
		Assert.True(AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(access.GrantedCycle));
		Assert.Equal(access.CompletedCycle, cycle);
	}

	[Theory]
	[InlineData(0x08)]
	[InlineData(0x10)]
	[InlineData(0x18)]
	public void HrmSlotEngineGrantsCopperOnIdleDeviceDmaWindows(int horizontal)
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var requestedCycle = horizontal * AgnusChipSlotScheduler.SlotCycles;

		var value = bus.ReadLiveCopperDmaWord(0x2400, requestedCycle, out var access);

		Assert.Equal(0x1234, value);
		Assert.Equal(requestedCycle, access.GrantedCycle);
		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(AgnusChipSlotOwner.Copper, snapshot.LastGrantedSlot?.Owner);
		Assert.Equal(0, snapshot.DiskSlotReservationCount);
		Assert.Equal(0, snapshot.PaulaSlotReservationCount);
		Assert.Equal(0, snapshot.SpriteSlotReservationCount);
	}

	[Fact]
	public void HrmSlotEngineMakesCopperWaitForReservedSpriteSlot()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var spriteCycle = AgnusHrmOcsSlotTable.FirstSpriteHorizontal * AgnusChipSlotScheduler.SlotCycles;
		Assert.True(bus.TryReserveDisplayDmaSlot(
			AmigaBusRequester.Sprite,
			AmigaBusAccessKind.Sprite,
			0x1000,
			spriteCycle,
			out var spriteAccess));

		_ = bus.ReadLiveCopperDmaWord(0x2400, spriteCycle, out var copperAccess);

		Assert.Equal(spriteCycle, spriteAccess.GrantedCycle);
		Assert.Equal(spriteCycle + (2 * AgnusChipSlotScheduler.SlotCycles), copperAccess.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Copper, bus.Agnus.CaptureSnapshot().LastGrantedSlot?.Owner);
	}

	[Theory]
	[InlineData((int)AmigaBusRequester.Disk, (int)AmigaBusAccessKind.DiskDma, 0x08)]
	[InlineData((int)AmigaBusRequester.Paula, (int)AmigaBusAccessKind.PaulaDma, 0x10)]
	public void HrmSlotEngineLegalizesFixedDeviceDmaToHrmWindows(
		int requesterValue,
		int kindValue,
		int expectedHorizontal)
	{
		var requester = (AmigaBusRequester)requesterValue;
		var kind = (AmigaBusAccessKind)kindValue;
		var bus = new AmigaBus();

		var access = requester == AmigaBusRequester.Sprite
			? ReserveDisplaySlot(bus, requester, kind, 0)
			: RequestDeviceWordAccess(bus, requester, kind, 0x1000, 0);

		Assert.Equal(expectedHorizontal * AgnusChipSlotScheduler.SlotCycles, access.GrantedCycle);
		Assert.Equal(requester == AmigaBusRequester.Paula ? AgnusChipSlotOwner.Paula : requester == AmigaBusRequester.Disk ? AgnusChipSlotOwner.Disk : AgnusChipSlotOwner.Sprite, bus.Agnus.CaptureSnapshot().LastGrantedSlot?.Owner);
	}

	[Fact]
	public void HrmExactDisplayDmaRejectsWrongFixedWindow()
	{
		var bus = new AmigaBus();

		Assert.False(bus.TryReserveDisplayDmaSlot(
			AmigaBusRequester.Sprite,
			AmigaBusAccessKind.Sprite,
			0x1000,
			0,
			out var access));

		Assert.Equal(0, access.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Sprite, bus.Agnus.CaptureSnapshot().LastDeniedFixedSlot?.Owner);
	}

	[Fact]
	public void HrmBitplaneDmaStealsSpriteSlotAndRefreshBlocksTooEarlyBitplane()
	{
		var bus = new AmigaBus();
		var spriteCycle = AgnusHrmOcsSlotTable.FirstSpriteHorizontal * AgnusChipSlotScheduler.SlotCycles;

		var sprite = ReserveDisplaySlot(bus, AmigaBusRequester.Sprite, AmigaBusAccessKind.Sprite, spriteCycle);
		var bitplane = ReserveDisplaySlot(bus, AmigaBusRequester.Bitplane, AmigaBusAccessKind.Bitplane, spriteCycle);
		var spriteRetry = bus.TryReserveDisplayDmaSlot(
			AmigaBusRequester.Sprite,
			AmigaBusAccessKind.Sprite,
			0x1000,
			spriteCycle,
			out _);

		Assert.Equal(spriteCycle, sprite.GrantedCycle);
		Assert.Equal(spriteCycle, bitplane.GrantedCycle);
		Assert.False(spriteRetry);
		Assert.Equal(AgnusChipSlotOwner.Sprite, bus.Agnus.CaptureSnapshot().LastDeniedFixedSlot?.Owner);
		Assert.Equal(AgnusChipSlotOwner.Bitplane, bus.Agnus.CaptureSnapshot().LastDeniedFixedSlotBlocker?.Owner);

		var refreshBus = new AmigaBus();
		Assert.False(refreshBus.TryReadLiveBitplaneDmaWord(0x1000, 0, out var value, out var grantedCycle));
		Assert.Equal(0, value);
		Assert.Equal(0, grantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Bitplane, refreshBus.Agnus.CaptureSnapshot().LastDeniedFixedSlot?.Owner);
		Assert.Equal(AgnusChipSlotOwner.Refresh, refreshBus.Agnus.CaptureSnapshot().LastDeniedFixedSlotBlocker?.Owner);
		Assert.Equal(1, refreshBus.Agnus.CaptureSnapshot().RefreshDeniedFixedSlotBlockerCount);
	}

	[Fact]
	public void LiveCopperDmaReadsChipPresentationAtGrantedSlot()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var cpuCycle = 32L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		var beforeWrite = bus.ReadLiveCopperDmaWord(0x2400, 20, out var firstAccess);
		var afterWrite = bus.ReadLiveCopperDmaWord(0x2400, 56, out var secondAccess);

		Assert.Equal(20, firstAccess.GrantedCycle);
		Assert.Equal(56, secondAccess.GrantedCycle);
		Assert.Equal(0x1234, beforeWrite);
		Assert.Equal(0x5678, afterWrite);
	}

	[Fact]
	public void PaulaDmaReadSamplesPresentationAtGrantedSlot()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var cpuCycle = 34L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		var beforeWrite = bus.ReadPaulaDmaWord(0x2400, 20);
		var afterWrite = bus.ReadPaulaDmaWord(0x2400, 36);

		Assert.Equal(32, beforeWrite.BusAccess.GrantedCycle);
		Assert.Equal(36, afterWrite.BusAccess.GrantedCycle);
		Assert.Equal(0x1234, beforeWrite.Value);
		Assert.Equal(0x5678, afterWrite.Value);
	}

	[Fact]
	public void ChipPresentationHistoryResolvesOutOfOrderWritesByCycle()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var laterCycle = 22L;
		bus.WriteWord(0x00002400, 0x5678, ref laterCycle, AmigaBusAccessKind.CpuDataWrite);
		var earlierCycle = 20L;
		bus.WriteWord(0x00002400, 0x9ABC, ref earlierCycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x2400, 19));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, 20));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, 21));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, 22));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, 23));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x2400, 24));
	}

	[Fact]
	public void ChipPresentationHistoryKeepsWritesBeyondInitialCaptureCapacity()
	{
		var bus = new AmigaBus();
		const int baseAddress = 0x1000;
		const int wordCount = 40_000;

		for (var i = 0; i < wordCount; i++)
		{
			var cycle = 1_000L + i * 4L;
			var value = (ushort)(0x8000 | (i & 0x7FFF));
			bus.WriteWord((uint)(baseAddress + i * 2), value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		}

		var lastAddress = baseAddress + (wordCount - 1) * 2;
		var lastValue = (ushort)(0x8000 | ((wordCount - 1) & 0x7FFF));

		Assert.Equal(0x0000, bus.ReadChipWordForPresentation((uint)lastAddress, 0));
		Assert.Equal(lastValue, bus.ReadChipWordForPresentation((uint)lastAddress, long.MaxValue));

		bus.ClearPresentationWriteHistory();

		var rewriteRequestCycle = 1_000_000L;
		var rewriteCycle = rewriteRequestCycle;
		bus.WriteWord((uint)lastAddress, 0x55AA, ref rewriteCycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(lastValue, bus.ReadChipWordForPresentation((uint)lastAddress, rewriteRequestCycle - 1));
		Assert.Equal(0x55AA, bus.ReadChipWordForPresentation((uint)lastAddress, rewriteCycle));
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesNextVblank()
	{
		var bus = new AmigaBus();
		var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
		var currentCycle = frameCycle - 2;
		bus.AdvanceRasterTo(currentCycle);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(
			currentCycle,
			frameCycle + 100,
			out var wakeSource);

		Assert.Equal(frameCycle, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.VerticalBlank, wakeSource);
	}

	[Fact]
	public void CpuBatchWakeSourceKeepsTargetCycleWhenVblankIsOnlyTargetTie()
	{
		var bus = new AmigaBus();
		var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
		var currentCycle = frameCycle - 2;
		bus.AdvanceRasterTo(currentCycle);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(
			currentCycle,
			frameCycle,
			out var wakeSource);

		Assert.Equal(frameCycle, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, wakeSource);
	}

	[Fact]
	public void CpuBatchWakeCandidateIgnoresMaskedPaulaInterruptLevel()
	{
		var bus = new AmigaBus();
		bus.Paula.ScheduleWrite(
			0,
			0x09A,
			(ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqVerticalBlank));
		bus.Paula.ScheduleWrite(
			0,
			0x09C,
			(ushort)(0x8000 | AmigaConstants.IntreqVerticalBlank));
		bus.Paula.AdvanceTo(0);

		var releaseCycle = AmigaConstants.A500InterruptRecognitionDelayCpuCycles;
		var delayedCandidate = bus.GetNextCpuBatchWakeCandidateCycle(10, 200, out var delayedWakeSource);
		var pendingCandidate = bus.GetNextCpuBatchWakeCandidateCycle(releaseCycle, releaseCycle + 100, out var pendingWakeSource);
		var maskedCandidate = bus.GetNextCpuBatchWakeCandidateCycle(releaseCycle, releaseCycle + 100, 3, out var maskedWakeSource);
		var unmaskedCandidate = bus.GetNextCpuBatchWakeCandidateCycle(releaseCycle, releaseCycle + 100, 2, out var unmaskedWakeSource);

		Assert.Equal(releaseCycle, delayedCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, delayedWakeSource);
		Assert.Equal(releaseCycle + 1, pendingCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, pendingWakeSource);
		Assert.Equal(releaseCycle + 100, maskedCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, maskedWakeSource);
		Assert.Equal(releaseCycle + 1, unmaskedCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, unmaskedWakeSource);
	}

	[Fact]
	public void CpuBatchWakeCandidateDoesNotClampToHsyncTodTickWithoutAlarm()
	{
		var bus = new AmigaBus();

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 1000, out var wakeSource);

		Assert.Equal(1000, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, wakeSource);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesCiaBTodHsyncAlarm()
	{
		var bus = new AmigaBus();
		var events = new List<AmigaCiaInterruptEvent>();
		bus.CiaB.WriteRegister(0x0F, 0x80, 0, events);
		bus.CiaB.WriteRegister(0x08, 0x01, 0, events);
		bus.CiaB.WriteRegister(0x0D, 0x84, 0, events);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 1000, out var wakeSource);

		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerRasterLine, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.HorizontalSyncTod, wakeSource);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesNextCiaTimerUnderflow()
	{
		var bus = new AmigaBus();
		var events = new List<AmigaCiaInterruptEvent>();
		bus.CiaA.WriteRegister(0x04, 0x02, 0, events);
		bus.CiaA.WriteRegister(0x05, 0x00, 0, events);
		bus.CiaA.WriteRegister(0x0E, 0x11, 0, events);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 100, out var wakeSource);

		Assert.Equal(20, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.CiaTimer, wakeSource);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesPendingDiskReadDma()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.Paula.ScheduleWrite(0, 0x096, 0x8210);
		bus.Paula.AdvanceTo(0);
		bus.Disk.WriteRegister(0x024, 0x8001, 0);
		bus.Disk.WriteRegister(0x024, 0x8001, 0);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 100, out var wakeSource);

		Assert.Equal(readyCycle, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.Disk, wakeSource);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesPaulaPendingWrite()
	{
		var bus = new AmigaBus();
		bus.Paula.ScheduleWrite(20, 0x0A6, 0x0003);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 100, out var wakeSource);

		Assert.Equal(20, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.Paula, wakeSource);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesLiveCopperWork()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		const uint copperList = 0x2400;
		WriteCopperList(
			bus,
			copperList,
			(0x0180, 0x0F00),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);
		bus.AdvanceDmaTo(0);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(
			0,
			AmigaConstants.A500PalCpuCyclesPerRasterLine,
			out var wakeSource);

		Assert.InRange(candidate, 1, AmigaConstants.A500PalCpuCyclesPerRasterLine);
		Assert.Equal(M68kTraceBatchWakeSource.Copper, wakeSource);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesPaulaAudioSampleEvent()
	{
		var bus = new AmigaBus(audioDmaMinimumPeriod: 1);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
		bus.Paula.ScheduleWrite(0, 0x09A, 0xC080);
		bus.Paula.ScheduleWrite(0, 0x0A0, 0x0000);
		bus.Paula.ScheduleWrite(0, 0x0A2, 0x1000);
		bus.Paula.ScheduleWrite(0, 0x0A4, 0x0001);
		bus.Paula.ScheduleWrite(0, 0x0A6, 0x0003);
		bus.Paula.ScheduleWrite(0, 0x096, 0x8201);
		bus.Paula.ScheduleWrite(0, 0x09C, 0x0080);
		bus.Paula.AdvanceTo(0);

		var candidate = bus.GetNextStoppedCpuWakeCandidateCycle(0, 100);

		Assert.Equal(34, candidate);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesBlitterCompletion()
	{
		var bus = new AmigaBus();
		StartNastyBlit(bus);
		bus.AdvanceDmaTo(100);
		var completionCycle = bus.Blitter.GetPredictedCompletionCycle();

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(100, 1000, out var wakeSource);

		Assert.Equal(completionCycle, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.Blitter, wakeSource);
	}

	private static void Write(byte[] memory, int address, params byte[] data)
	{
		Array.Copy(data, 0, memory, address, data.Length);
	}

	private static long OutputRowStartCycle(int row)
	{
		var line = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
		return (long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
	}

	private static long LowResPlane1FetchCycle(int row)
		=> OutputRowStartCycle(row) +
			((0x38 + HrmLowResPlane1FetchSlot) * AgnusChipSlotScheduler.SlotCycles);

	private static AmigaDiskImage CreateSingleWordDisk(ushort word)
	{
		var data = new byte[2];
		BigEndian.WriteUInt16(data, 0, word);
		var track = AmigaEncodedTrack.FromBytes(data);
		var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
		Array.Fill(tracks, track);
		return AmigaDiskImage.FromEncodedTracks(tracks);
	}

	private static long DiskByteCycleCount(int trackByteLength, int byteCount)
	{
		return (long)Math.Ceiling(
			AmigaConstants.A500PalCpuClockHz / (trackByteLength * 5.0) * byteCount);
	}

	private static long MotorReadyDelayCycles()
	{
		return Math.Max(1, (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));
	}

	private static AmigaBusAccessRequest CreateChipRequest(AmigaBusRequester requester, long requestedCycle)
	{
		return new AmigaBusAccessRequest(
			requester,
			requester switch
			{
				AmigaBusRequester.Cpu => AmigaBusAccessKind.CpuDataRead,
				AmigaBusRequester.Sprite => AmigaBusAccessKind.Sprite,
				AmigaBusRequester.Blitter => AmigaBusAccessKind.Blitter,
				_ => AmigaBusAccessKind.Bitplane
			},
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			requestedCycle,
			isWrite: false);
	}

	private static AmigaBusAccessKind CreateDmaAccessKind(AmigaBusRequester requester)
	{
		return requester switch
		{
			AmigaBusRequester.Sprite => AmigaBusAccessKind.Sprite,
			AmigaBusRequester.Copper => AmigaBusAccessKind.Copper,
			_ => AmigaBusAccessKind.Bitplane
		};
	}

	private static AmigaBusAccessResult RequestDeviceWordAccess(
		AmigaBus bus,
		AmigaBusRequester requester,
		AmigaBusAccessKind kind,
		uint address,
		long requestedCycle)
	{
		return requester switch
		{
			AmigaBusRequester.Paula => bus.ReadPaulaDmaWord(address, requestedCycle).BusAccess,
			AmigaBusRequester.Disk => bus.WriteChipWordForDeviceWithResult(requester, kind, address, 0xCAFE, requestedCycle),
			_ => bus.ReadChipWordForDeviceWithResult(requester, kind, address, requestedCycle).BusAccess
		};
	}

	private static AmigaBusAccessResult ReserveDisplaySlot(
		AmigaBus bus,
		AmigaBusRequester requester,
		AmigaBusAccessKind kind,
		long requestedCycle)
	{
		Assert.True(bus.TryReserveDisplayDmaSlot(requester, kind, 0x1000, requestedCycle, out var access));
		return access;
	}

	private static void WriteCopperList(AmigaBus bus, uint address, params (ushort First, ushort Second)[] instructions)
	{
		for (var i = 0; i < instructions.Length; i++)
		{
			BigEndian.WriteUInt16(bus.ChipRam, (int)address + (i * 4), instructions[i].First);
			BigEndian.WriteUInt16(bus.ChipRam, (int)address + (i * 4) + 2, instructions[i].Second);
		}
	}

	private static void SetCopperPointer(AmigaBus bus, int list, uint address)
	{
		var highOffset = list == 1 ? 0x00DFF080u : 0x00DFF084u;
		bus.WriteWord(highOffset, (ushort)(address >> 16));
		bus.WriteWord(highOffset + 2, (ushort)address);
	}

	private static void StartLongBlit(AmigaBus bus)
	{
		bus.WriteWord(0x00DFF040, 0x09F0, 0);
		bus.WriteWord(0x00DFF042, 0x0000, 0);
		bus.WriteWord(0x00DFF050, 0x0000, 0);
		bus.WriteWord(0x00DFF052, 0x3000, 0);
		bus.WriteWord(0x00DFF054, 0x0000, 0);
		bus.WriteWord(0x00DFF056, 0x4000, 0);
		bus.WriteWord(0x00DFF096, 0x8240, 0);
		bus.WriteWord(0x00DFF058, (ushort)((8 << 6) | 8), 0);
	}

	private static int StartFastPaulaDma(AmigaBus bus)
	{
		for (var i = 0; i < 64; i += 2)
		{
			BigEndian.WriteUInt16(bus.ChipRam, 0x7000 + i, (ushort)(0x4000 + i));
		}

		bus.WriteWord(0x00DFF0A0, 0x0000, 0);
		bus.WriteWord(0x00DFF0A2, 0x7000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0020, 0);
		bus.WriteWord(0x00DFF0A6, 0x0002, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);
		bus.Paula.AdvanceTo(0);
		return bus.BusAccesses.Count;
	}

	private static void AssertPaulaDmaInterleavedWithCpuBusWindow(
		AmigaBus bus,
		int setupAccessCount,
		int minCpuAccesses)
	{
		var window = bus.BusAccesses.Skip(setupAccessCount).ToArray();
		var cpuAccesses = window
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu)
			.ToArray();
		var paulaAccesses = window
			.Where(access => access.Request.Kind == AmigaBusAccessKind.PaulaDma)
			.ToArray();

		Assert.True(
			cpuAccesses.Length >= minCpuAccesses,
			BuildAccessSignature(cpuAccesses));
		Assert.NotEmpty(paulaAccesses);
		Assert.Contains(
			paulaAccesses,
			paula => paula.GrantedCycle > cpuAccesses[0].GrantedCycle &&
				paula.GrantedCycle < cpuAccesses[^1].GrantedCycle);
		foreach (var paula in paulaAccesses)
		{
			Assert.DoesNotContain(
				cpuAccesses,
				cpu => BusAccessUsesSlot(cpu, paula.GrantedCycle));
		}
	}

	private static bool BusAccessUsesSlot(AmigaBusAccessResult access, long slotCycle)
	{
		if (access.Request.Target != AmigaBusAccessTarget.ChipRam &&
			access.Request.Target != AmigaBusAccessTarget.ExpansionRam &&
			access.Request.Target != AmigaBusAccessTarget.RealTimeClock &&
			access.Request.Target != AmigaBusAccessTarget.CustomRegisters)
		{
			return false;
		}

		var slotCount = access.Request.Size == AmigaBusAccessSize.Long ? 2 : 1;
		var slotStride = access.Request.Requester == AmigaBusRequester.Cpu
			? 2 * AgnusChipSlotScheduler.SlotCycles
			: AgnusChipSlotScheduler.SlotCycles;
		for (var slot = 0; slot < slotCount; slot++)
		{
			if (access.GrantedCycle + (slot * slotStride) == slotCycle)
			{
				return true;
			}
		}

		return false;
	}

	private static void RunUncontendedChipAccessSequence(AmigaBus bus)
	{
		var cpuCycle = 0L;
		_ = bus.ReadWord(0x00001000, ref cpuCycle, AmigaBusAccessKind.CpuInstructionFetch);
		cpuCycle = 8;
		bus.WriteWord(0x00001002, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1004, 20);
		_ = bus.WriteChipWordForDeviceWithResult(AmigaBusRequester.Disk, AmigaBusAccessKind.DiskDma, 0x1006, 0x9ABC, 30);
	}

	private static string BuildAccessSignature(IReadOnlyList<AmigaBusAccessResult> accesses)
	{
		return string.Join(
			";",
			accesses.Select(access =>
				$"{access.Request.Requester}:{access.Request.Kind}:{access.Request.Target}:{access.Request.Address:X6}:{access.Request.Size}:{access.GrantedCycle}:{access.CompletedCycle}"));
	}

	private static long ExpectedCiaAccessCycle(long requestedCycle)
	{
		var cycle = Math.Max(0, requestedCycle + 1);
		var remainder = cycle % AmigaConstants.A500PalCpuCyclesPerCiaTick;
		return remainder == 0
			? cycle
			: cycle + AmigaConstants.A500PalCpuCyclesPerCiaTick - remainder;
	}

	private static void StartNastyBlit(AmigaBus bus)
	{
		BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);
		bus.WriteWord(0x00DFF040, 0x09F0);
		bus.WriteWord(0x00DFF050, 0x0000);
		bus.WriteWord(0x00DFF052, 0x3000);
		bus.WriteWord(0x00DFF054, 0x0000);
		bus.WriteWord(0x00DFF056, 0x4000);
		bus.WriteWord(0x00DFF096, 0x8640);
		bus.AdvanceDmaTo(100);
		bus.WriteWord(0x00DFF058, 0x0044);
	}

	private sealed class FixedDelayArbiter : IAmigaBusArbiter
	{
		private readonly long _waitCycles;
		private readonly long _accessCycles;

		public FixedDelayArbiter(long waitCycles, long accessCycles)
		{
			_waitCycles = waitCycles;
			_accessCycles = accessCycles;
		}

		public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request)
		{
			var granted = request.RequestedCycle + _waitCycles;
			return new AmigaBusAccessResult(request, granted, granted + _accessCycles);
		}
	}
}
