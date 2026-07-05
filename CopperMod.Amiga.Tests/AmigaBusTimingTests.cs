using CopperMod.Amiga;
using CopperDisk;
using Copper68k;

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
	public void DmaWordReservationSamplesChipRamAtCommitGrantCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const uint address = 0x1000;

		bus.WriteChipDmaWordAtGrantedSlot(address, 0x1111, 0);
		var reservation = bus.ReserveChipWordForDevice(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			address,
			20);
		Assert.True(reservation.GrantedCycle > 0);

		bus.WriteChipDmaWordAtGrantedSlot(address, 0x2222, reservation.GrantedCycle - 1);

		Assert.Equal(0x2222, bus.CommitDmaWordRead(in reservation));
	}

	[Fact]
	public void DmaWordWriteReservationCommitsChipRamOnlyWhenConsumed()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const uint address = 0x1000;

		bus.WriteChipDmaWordAtGrantedSlot(address, 0x1111, 0);
		var reservation = bus.ReserveChipWordWriteForDevice(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			address,
			20);

		Assert.Equal(0x1111, bus.ReadChipDmaWordAtGrantedSlot(address, reservation.GrantedCycle));

		bus.CommitDmaWordWrite(in reservation, 0x3333);

		Assert.Equal(0x3333, bus.ReadChipDmaWordAtGrantedSlot(address, reservation.GrantedCycle + 1));
	}

	[Fact]
	public void ExactCpuChipSlotFastHelperMatchesGenericSingleSlotGrant()
	{
		var genericEngine = new AgnusHrmSlotEngine();
		var fastEngine = new AgnusHrmSlotEngine();
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Cpu,
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			20,
			isWrite: false);

		var generic = genericEngine.Arbitrate(request, new AmigaBusAccessResult(request, 20, 20));
		fastEngine.GrantCpuDataSingleSlot(
			request.Kind,
			request.Target,
			request.Address,
			request.Size,
			request.RequestedCycle,
			request.IsWrite,
			out var granted,
			out var completed);

		Assert.Equal(generic.GrantedCycle, granted);
		Assert.Equal(generic.CompletedCycle, completed);
		Assert.Equal(genericEngine.LastReservation?.GrantedCycle, fastEngine.LastReservation?.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Cpu, fastEngine.LastGrantedSlot?.Owner);
	}

	[Fact]
	public void ExactCpuChipSlotFastHelperMatchesGenericLongGrant()
	{
		var genericEngine = new AgnusHrmSlotEngine();
		var fastEngine = new AgnusHrmSlotEngine();
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Cpu,
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Long,
			20,
			isWrite: true);

		var generic = genericEngine.Arbitrate(request, new AmigaBusAccessResult(request, 20, 20));
		fastEngine.GrantCpuDataLongSlots(
			request.Kind,
			request.Target,
			request.Address,
			request.RequestedCycle,
			request.IsWrite,
			out var firstWordCycle,
			out var secondWordCycle,
			out var completed);

		Assert.Equal(generic.GrantedCycle, firstWordCycle);
		Assert.Equal(generic.GrantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles), secondWordCycle);
		Assert.Equal(generic.CompletedCycle, completed);
		Assert.Equal(genericEngine.LastReservation?.CompletedCycle, fastEngine.LastReservation?.CompletedCycle);
	}

	[Fact]
	public void ExactCpuChipSlotFastHelperMatchesGenericRefreshContention()
	{
		var genericEngine = new AgnusHrmSlotEngine();
		var fastEngine = new AgnusHrmSlotEngine();
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Cpu,
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			0,
			isWrite: false);

		var generic = genericEngine.Arbitrate(request, new AmigaBusAccessResult(request, 0, 0));
		fastEngine.GrantCpuDataSingleSlot(
			request.Kind,
			request.Target,
			request.Address,
			request.Size,
			request.RequestedCycle,
			request.IsWrite,
			out var granted,
			out var completed);

		Assert.Equal(generic.GrantedCycle, granted);
		Assert.Equal(generic.CompletedCycle, completed);
		Assert.True(granted > request.RequestedCycle);
	}

	[Fact]
	public void ExactCpuChipSlotFastHelperMatchesGenericDisplaySlotContention()
	{
		var genericEngine = new AgnusHrmSlotEngine();
		var fastEngine = new AgnusHrmSlotEngine();
		const long requestedCycle = 20;
		genericEngine.ReserveBitplaneDmaSlot(0x2000, requestedCycle);
		fastEngine.ReserveBitplaneDmaSlot(0x2000, requestedCycle);
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Cpu,
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			requestedCycle,
			isWrite: false);

		var generic = genericEngine.Arbitrate(request, new AmigaBusAccessResult(request, requestedCycle, requestedCycle));
		fastEngine.GrantCpuDataSingleSlot(
			request.Kind,
			request.Target,
			request.Address,
			request.Size,
			request.RequestedCycle,
			request.IsWrite,
			out var granted,
			out var completed);

		Assert.Equal(generic.GrantedCycle, granted);
		Assert.Equal(generic.CompletedCycle, completed);
		Assert.True(granted > requestedCycle);
	}

	[Fact]
	public void ExactCpuChipSlotFastHelperMatchesGenericNiceBlitterMisses()
	{
		var genericEngine = new AgnusHrmSlotEngine();
		var fastEngine = new AgnusHrmSlotEngine();
		genericEngine.BlitterPriorityEnabled = false;
		fastEngine.BlitterPriorityEnabled = false;
		ReserveBlitterWordSlot(genericEngine, 20);
		ReserveBlitterWordSlot(genericEngine, 24);
		ReserveBlitterWordSlot(genericEngine, 28);
		ReserveBlitterWordSlot(fastEngine, 20);
		ReserveBlitterWordSlot(fastEngine, 24);
		ReserveBlitterWordSlot(fastEngine, 28);
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Cpu,
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			20,
			isWrite: false);

		var generic = genericEngine.Arbitrate(request, new AmigaBusAccessResult(request, 20, 20));
		fastEngine.GrantCpuDataSingleSlot(
			request.Kind,
			request.Target,
			request.Address,
			request.Size,
			request.RequestedCycle,
			request.IsWrite,
			out var granted,
			out var completed);

		Assert.Equal(generic.GrantedCycle, granted);
		Assert.Equal(generic.CompletedCycle, completed);
		Assert.Equal(28, granted);
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
	public void ExactCpuChipFastPathReadsChipRamAndRecordsScalarReservation()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		Write(bus.ChipRam, 0x1000, 0x12, 0x34);
		var cycle = 20L;

		var value = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0x1234, value);
		var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(AmigaBusAccessTarget.ChipRam, reservation.Request.Target);
		Assert.Equal(AmigaBusAccessKind.CpuDataRead, reservation.Request.Kind);
		Assert.Equal(reservation.CompletedCycle, cycle);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void ExactCpuChipFastPathWritePreservesPresentationGrantCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		Write(bus.ChipRam, 0x2400, 0x12, 0x34);
		var cycle = 20L;

		bus.WriteWord(0x00002400, 0x9ABC, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(reservation.CompletedCycle, cycle);
		Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x2400, reservation.GrantedCycle - 1));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, reservation.GrantedCycle));
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void ExactCpuChipFastPathLongWriteUsesSecondCpuWordCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		Write(bus.ChipRam, 0x2400, 0x12, 0x34, 0x56, 0x78);
		var cycle = 20L;

		bus.WriteLong(0x00002400, 0x9ABCDEF0, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		var secondWordCycle = reservation.GrantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles);
		Assert.Equal(reservation.CompletedCycle, cycle);
		Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x2400, reservation.GrantedCycle - 1));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, reservation.GrantedCycle));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x2402, secondWordCycle - 1));
		Assert.Equal(0xDEF0, bus.ReadChipWordForPresentation(0x2402, secondWordCycle));
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void ExactCpuChipFastPathUsesChipSlotTimingForExpansionRam()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			captureBusAccesses: false,
			enableLiveAgnusDma: true);
		Write(bus.ExpansionRam, 0, 0xCA, 0xFE);
		var cycle = 20L;

		var value = bus.ReadWord(AmigaConstants.A500BootPseudoFastRamBase, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0xCAFE, value);
		var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(AmigaBusAccessTarget.ExpansionRam, reservation.Request.Target);
		Assert.Equal(reservation.CompletedCycle, cycle);
		Assert.True(cycle > 20);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void JitSlotAwareChipRamUsesExactCpuSlotFastPath()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		Write(bus.ChipRam, 0x2400, 0x12, 0x34, 0x56, 0x78);
		var readCycle = 20L;

		var value = bus.ReadJitSlotAwareMemory(ref readCycle, 0x00002400, M68kOperandSize.Long);

		Assert.Equal(0x12345678u, value);
		var readReservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(AmigaBusAccessKind.CpuDataRead, readReservation.Request.Kind);
		Assert.Equal(AmigaBusAccessTarget.ChipRam, readReservation.Request.Target);
		Assert.Equal(readReservation.CompletedCycle, readCycle);
		Assert.Empty(bus.BusAccesses);

		var writeCycle = 40L;
		bus.WriteJitSlotAwareMemory(ref writeCycle, 0x00002400, 0x9ABCDEF0, M68kOperandSize.Long);

		var writeReservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		var secondWordCycle = writeReservation.GrantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles);
		Assert.Equal(AmigaBusAccessKind.CpuDataWrite, writeReservation.Request.Kind);
		Assert.Equal(writeReservation.CompletedCycle, writeCycle);
		Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x2400, writeReservation.GrantedCycle - 1));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, writeReservation.GrantedCycle));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x2402, secondWordCycle - 1));
		Assert.Equal(0xDEF0, bus.ReadChipWordForPresentation(0x2402, secondWordCycle));
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void InstructionFetchWindowUsesExactCpuSlotFastPath()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		Write(bus.ChipRam, 0x2400, 0x4E, 0x71);
		Assert.True(bus.TryGetInstructionFetchWindow(0x00002400, out var window));
		var cycle = 20L;

		bus.CommitInstructionFetchWindowWord(in window, 0x00002400, ref cycle);

		var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(AmigaBusAccessKind.CpuInstructionFetch, reservation.Request.Kind);
		Assert.Equal(AmigaBusAccessTarget.ChipRam, reservation.Request.Target);
		Assert.Equal(reservation.CompletedCycle, cycle);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void ExactCpuChipFastPathFallsBackWhenBusAccessCaptureIsEnabled()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		Write(bus.ChipRam, 0x1000, 0x12, 0x34);
		var cycle = 20L;

		var value = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0x1234, value);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.ChipRam &&
			access.Request.Kind == AmigaBusAccessKind.CpuDataRead);
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
	public void CpuCiaReadObservesPublishedTodOnly()
	{
		var bus = new AmigaBus();
		var hsyncCycle = (long)AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var beforeRequest = hsyncCycle - (2 * AmigaConstants.A500PalCpuCyclesPerCiaTick);
		Assert.True(ExpectedCiaAccessCycle(beforeRequest) < hsyncCycle);

		var cycle = beforeRequest;
		Assert.Equal(0x00, bus.ReadByte(0x00BFD800, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(ExpectedCiaAccessCycle(beforeRequest), cycle);

		cycle = hsyncCycle;
		Assert.Equal(0x00, bus.ReadByte(0x00BFD800, ref cycle, AmigaBusAccessKind.CpuDataRead));
		Assert.Equal(ExpectedCiaAccessCycle(hsyncCycle), cycle);

		bus.AdvanceRasterTo(hsyncCycle);
		cycle = hsyncCycle;
		Assert.Equal(0x01, bus.ReadByte(0x00BFD800, ref cycle, AmigaBusAccessKind.CpuDataRead));
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
	public void NonNastyBlitterYieldsThirdConsecutiveCpuMiss()
	{
		var bus = new AmigaBus();
		var slotCycles = AgnusChipSlotScheduler.SlotCycles;
		var firstSlot = 20L;
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1000, firstSlot);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1002, firstSlot + (2 * slotCycles));
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1004, firstSlot + (4 * slotCycles));

		var cycle = firstSlot;
		_ = bus.ReadWord(0x00001006, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(firstSlot + (4 * slotCycles), cpu.GrantedCycle);
		Assert.Equal(firstSlot + (5 * slotCycles), cycle);
	}

	[Fact]
	public void NastyBlitterDoesNotYieldThirdConsecutiveCpuMiss()
	{
		var bus = new AmigaBus();
		var slotCycles = AgnusChipSlotScheduler.SlotCycles;
		var firstSlot = 20L;
		bus.WriteWord(0x00DFF096, 0x8640);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1000, firstSlot);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1002, firstSlot + (2 * slotCycles));
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1004, firstSlot + (4 * slotCycles));

		var cycle = firstSlot;
		_ = bus.ReadWord(0x00001006, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(firstSlot + (6 * slotCycles), cpu.GrantedCycle);
		Assert.Equal(firstSlot + (7 * slotCycles), cycle);
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
	public void LiveDisplayWakeCandidateIncludesPendingCustomWrite()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.EnableLiveAgnusDma();
		var writeCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) + 20;
		bus.AdvanceDmaTo(writeCycle - 2);
		bus.Display.ScheduleWrite(writeCycle, 0x0100, 0x0000);

		var candidate = bus.Display.GetNextLiveDisplayWakeCandidateCycle(writeCycle - 2, writeCycle);

		Assert.Equal(writeCycle, candidate);
	}

	[Fact]
	public void LiveDisplayWakeCandidateIncludesLineStateCapture()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart - 2);

		var candidate = bus.Display.GetNextLiveDisplayWakeCandidateCycle(lineStart - 2, lineStart);

		Assert.Equal(lineStart, candidate);
	}

	[Fact]
	public void LiveDisplayWakeCandidateSkipsIdleLineStateWhenNoDisplayWork()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.EnableLiveAgnusDma();
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		bus.AdvanceDmaTo(lineStart - 2);

		var candidate = bus.Display.GetNextLiveDisplayWakeCandidateCycle(lineStart - 2, lineStart);

		Assert.Null(candidate);
	}

	[Fact]
	public void LiveDisplayWakeCandidateIncludesBitplaneFetch()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(fetchCycle - 2);

		var candidate = bus.Display.GetNextLiveDisplayWakeCandidateCycle(fetchCycle - 2, fetchCycle);

		Assert.Equal(fetchCycle, candidate);
	}

	[Fact]
	public void LiveDisplayWakeCandidateUsesRecordedRasterlineTapeForCapturedLine()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveOneBitplaneDma(bus);

		bus.AdvanceDmaTo(fetchCycle + AgnusChipSlotScheduler.SlotCycles);

		var candidate = bus.Display.GetNextLiveDisplayWakeCandidateCycle(lineStart, fetchCycle);

		Assert.Equal(fetchCycle, candidate);
	}

	[Fact]
	public void LiveDisplayWakeCandidateIncludesSpriteFetch()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var lineStop = lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine;
		ConfigureLiveSpriteDma(bus);
		bus.AdvanceDmaTo(lineStart - 2);

		var current = lineStart - 2;
		for (var i = 0; i < 32; i++)
		{
			var candidate = bus.Display.GetNextLiveDisplayWakeCandidateCycle(current, lineStop);
			Assert.NotNull(candidate);
			bus.AdvanceDmaTo(candidate.Value);
			if (bus.Display.CaptureSnapshot().LastSpriteDmaFetches > 0)
			{
				return;
			}

			current = Math.Max(current + 1, candidate.Value);
		}

		Assert.Fail("Expected a live display wake candidate to reach a sprite DMA fetch on the configured line.");
	}

	[Fact]
	public void HardwareSchedulerSkipsLiveDisplayAdvanceWhenDisplayHasNoLiveWork()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.EnableLiveAgnusDma();

		bus.AdvanceHardwareTo(OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY));

		var display = bus.Display.CaptureSnapshot();
		Assert.Equal(0, bus.Display.LiveDisplayEventCount);
		Assert.Equal(0, display.LastRasterlinePlanEvents);
		Assert.True(bus.Agnus.CaptureSnapshot().RefreshSlotReservationCount > 0);
	}

	[Fact]
	public void CpuBoundaryHardwareDrainDoesNotReservePureRefreshSlots()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var targetCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);

		bus.AdvanceHardwareEventsTo(targetCycle);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().RefreshSlotReservationCount);

		bus.AdvanceHardwareTo(targetCycle);
		Assert.True(bus.Agnus.CaptureSnapshot().RefreshSlotReservationCount > 0);
	}

	[Fact]
	public void LiveRasterlinePlanRecordsLineStateEvent()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveOneBitplaneDma(bus);

		bus.AdvanceDmaTo(lineStart);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastRasterlinePlanLineStateEvents > 0);
		Assert.True(snapshot.LastRasterlinePlanValidLines > 0);
		Assert.Equal(0, snapshot.LastRasterlinePlanInvalidLines);
	}

	[Fact]
	public void LiveRasterlinePlanRecordsPendingWriteEvent()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.EnableLiveAgnusDma();
		var writeCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) + 20;
		bus.AdvanceDmaTo(writeCycle - 2);
		bus.Display.ScheduleWrite(writeCycle, 0x0180, 0x0123);

		bus.AdvanceDmaTo(writeCycle);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastRasterlinePlanPendingWriteOrCopperEvents > 0);
		Assert.True(snapshot.LastRasterlinePlanValidLines > 0);
		Assert.Equal(0, snapshot.LastRasterlinePlanInvalidLines);
	}

	[Fact]
	public void LiveRasterlinePlanRecordsBitplaneFetchBatch()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveOneBitplaneDma(bus);

		bus.AdvanceDmaTo(fetchCycle);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastRasterlinePlanBitplaneFetchEvents > 0);
		Assert.True(snapshot.LastRasterlinePlanValidLines > 0);
		Assert.Equal(0, snapshot.LastRasterlinePlanInvalidLines);
		Assert.True(snapshot.LastRowDmaPlansBuilt > 0);
		Assert.True(snapshot.LastRowDmaBitplaneEntriesExecuted > 0);
	}

	[Fact]
	public void PredictedRasterlinePlanMatchesSimpleBitplaneLine()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var validationStop = lineStart + (3 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		ConfigureLiveOneBitplaneDma(bus);

		bus.AdvanceDmaTo(validationStop);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(
			snapshot.LastPredictedRasterlinePlanLines > 0,
			$"recorded={snapshot.LastRasterlinePlanEvents}, lineState={snapshot.LastRasterlinePlanLineStateEvents}, bitplane={snapshot.LastRasterlinePlanBitplaneFetchEvents}, lines={snapshot.LastPredictedRasterlinePlanLines}, matched={snapshot.LastPredictedRasterlinePlanMatchedLines}, mismatched={snapshot.LastPredictedRasterlinePlanMismatchedLines}, unsupported={snapshot.LastPredictedRasterlinePlanUnsupportedLines}, copper={snapshot.LastPredictedRasterlinePlanUnsupportedCopperLines}, pending={snapshot.LastPredictedRasterlinePlanUnsupportedPendingWriteLines}, sprite={snapshot.LastPredictedRasterlinePlanUnsupportedSpriteLines}, invalid={snapshot.LastPredictedRasterlinePlanUnsupportedInvalidStateLines}, overflow={snapshot.LastPredictedRasterlinePlanUnsupportedOverflowLines}, descriptorBuilds={snapshot.LastRasterlineDescriptorBuilds}, descriptorReplayed={snapshot.LastRasterlineDescriptorReplayedRows}, descriptorMismatches={snapshot.LastRasterlineDescriptorMismatches}");
		Assert.True(
			snapshot.LastPredictedRasterlinePlanMatchedLines > 0,
			$"lines={snapshot.LastPredictedRasterlinePlanLines}, matched={snapshot.LastPredictedRasterlinePlanMatchedLines}, mismatched={snapshot.LastPredictedRasterlinePlanMismatchedLines}");
		Assert.Equal(0, snapshot.LastPredictedRasterlinePlanMismatchedLines);
		Assert.True(snapshot.LastRasterlineDescriptorBuilds > 0);
		Assert.True(snapshot.LastRasterlineDescriptorBitplaneRows > 0);
		Assert.True(snapshot.LastRasterlineDescriptorReplayAttempts > 0);
		Assert.True(snapshot.LastRasterlineDescriptorReplayedRows > 0);
		Assert.Equal(0, snapshot.LastRasterlineDescriptorMismatches);
		Assert.True(snapshot.LastRowDmaPlansBuilt > 0);
		Assert.True(snapshot.LastRowDmaBitplaneEntriesExecuted > 0);
	}

	[Fact]
	public void RasterlineDescriptorReplayReadsChipRamAtFetchTime()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var fetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0000);

		bus.AdvanceDmaTo(lineStart);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xA55A);
		bus.AdvanceDmaTo(fetchCycle);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastRasterlineDescriptorReplayedRows > 0);
		Assert.Equal(0, snapshot.LastRasterlineDescriptorMismatches);
		Assert.True(snapshot.LastRowDmaBitplaneEntriesExecuted > 0);
		Assert.Equal(0xA55A, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
	}

	[Fact]
	public void RasterlineDescriptorReplaysSpriteRows()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var validationStop = lineStart + (3 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		ConfigureLiveSpriteDma(bus);

		bus.AdvanceDmaTo(validationStop);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastRasterlineDescriptorSpriteRows > 0);
		Assert.True(snapshot.LastRasterlineDescriptorReplayAttempts > 0);
		Assert.True(snapshot.LastRasterlineDescriptorReplayedRows > 0);
		Assert.Equal(0, snapshot.LastPredictedRasterlinePlanUnsupportedSpriteLines);
		Assert.Equal(0, snapshot.LastPredictedRasterlinePlanMismatchedLines);
		Assert.Equal(0, snapshot.LastRasterlineDescriptorMismatches);
		Assert.True(snapshot.LastRowDmaPlansBuilt > 0);
		Assert.True(snapshot.LastRowDmaSpriteEntriesExecuted > 0);
	}

	[Fact]
	public void PredictedRasterlinePlanMarksPendingWriteLineUnsupported()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.EnableLiveAgnusDma();
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var validationStop = lineStart + (3 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		bus.Display.ScheduleWrite(lineStart + 20, 0x0180, 0x0123);

		bus.AdvanceDmaTo(validationStop);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastPredictedRasterlinePlanUnsupportedPendingWriteLines > 0);
		Assert.Equal(0, snapshot.LastPredictedRasterlinePlanMismatchedLines);
	}

	[Fact]
	public void LiveRasterlinePlanRecordsSpriteFetchBatch()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var lineStop = lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine;
		ConfigureLiveSpriteDma(bus);

		for (var cycle = lineStart; cycle < lineStop; cycle += AgnusChipSlotScheduler.SlotCycles)
		{
			bus.AdvanceDmaTo(cycle);
			if (bus.Display.CaptureSnapshot().LastSpriteDmaFetches > 0)
			{
				var snapshot = bus.Display.CaptureSnapshot();
				Assert.True(snapshot.LastRasterlinePlanSpriteFetchEvents > 0);
				Assert.True(snapshot.LastRasterlinePlanValidLines > 0);
				Assert.Equal(0, snapshot.LastRasterlinePlanInvalidLines);
				return;
			}
		}

		Assert.Fail("Expected live sprite DMA to produce a shadow rasterline plan sprite event.");
	}

	[Fact]
	public void LiveRasterlinePlanOverflowMarksLineInvalid()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.EnableLiveAgnusDma();
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		for (var i = 0; i < 80; i++)
		{
			bus.Display.ScheduleWrite(lineStart + 20 + i, 0x0180, (ushort)i);
		}

		bus.AdvanceDmaTo(lineStart + 120);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.True(snapshot.LastRasterlinePlanPendingWriteOrCopperEvents >= 80);
		Assert.True(snapshot.LastRasterlinePlanOverflowLines > 0);
		Assert.True(snapshot.LastRasterlinePlanInvalidLines > 0);
		Assert.True(snapshot.LastRasterlinePlanMaxEventsPerLine > 64);
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
	public void PreparedBitplaneSlotAllowsLaterReadFromResolvedAddress()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1111);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0xA55A);

		Assert.True(bus.TryReserveDisplayDmaSlot(
			AmigaBusRequester.Bitplane,
			AmigaBusAccessKind.Bitplane,
			0x1000,
			fetchCycle,
			out var prepared));
		Assert.Equal(fetchCycle, prepared.GrantedCycle);

		Assert.True(bus.TryReadLiveBitplaneDmaWord(0x2000, fetchCycle, out var value, out var grantedCycle));
		Assert.Equal(fetchCycle, grantedCycle);
		Assert.Equal(0xA55A, value);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
	}

	[Fact]
	public void RowBitplaneDmaBatchMatchesScalarRead()
	{
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		var scalarBus = new AmigaBus();
		var batchBus = new AmigaBus();
		BigEndian.WriteUInt16(scalarBus.ChipRam, 0x2000, 0xA55A);
		BigEndian.WriteUInt16(batchBus.ChipRam, 0x2000, 0xA55A);
		var lineStartCycle = RowLineStartCycle(fetchCycle);
		var entries = new[]
		{
			new RowDmaBitplaneEntry(RowCycleOffset(fetchCycle), plane: 0, word: 0, slot: HrmLowResPlane1FetchSlot, address: 0x2000, rowPresent: true)
		};
		var values = new ushort[entries.Length];
		var granted = new bool[entries.Length];

		Assert.True(scalarBus.TryReadLiveBitplaneDmaWord(0x2000, fetchCycle, out var scalarValue, out var scalarGrantedCycle));
		batchBus.ReadRowBitplaneDmaFetchesForPresentation(
			entries,
			lineStartCycle,
			values,
			granted,
			out var grantedCount,
			out var firstGrantedCycle,
			out var lastGrantedCycle);

		Assert.True(granted[0]);
		Assert.Equal(1, grantedCount);
		Assert.Equal(scalarValue, values[0]);
		Assert.Equal(scalarGrantedCycle, firstGrantedCycle);
		Assert.Equal(scalarGrantedCycle, lastGrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Bitplane, batchBus.Agnus.CaptureSnapshot().LastGrantedSlot?.Owner);
	}

	[Fact]
	public void RowBitplaneDmaBatchDeniedByRefreshMatchesScalarRead()
	{
		var scalarBus = new AmigaBus();
		var batchBus = new AmigaBus();
		var entries = new[]
		{
			new RowDmaBitplaneEntry(0, plane: 0, word: 0, slot: 0, address: 0x1000, rowPresent: true)
		};
		var values = new ushort[entries.Length];
		var granted = new bool[entries.Length];

		Assert.False(scalarBus.TryReadLiveBitplaneDmaWord(0x1000, 0, out var scalarValue, out var scalarGrantedCycle));
		batchBus.ReadRowBitplaneDmaFetchesForPresentation(
			entries,
			lineStartCycle: 0,
			values,
			granted,
			out var grantedCount,
			out var firstGrantedCycle,
			out var lastGrantedCycle);

		Assert.False(granted[0]);
		Assert.Equal(scalarValue, values[0]);
		Assert.Equal(0, scalarGrantedCycle);
		Assert.Equal(0, grantedCount);
		Assert.Equal(-1, firstGrantedCycle);
		Assert.Equal(-1, lastGrantedCycle);
		var snapshot = batchBus.Agnus.CaptureSnapshot();
		Assert.Equal(AgnusChipSlotOwner.Bitplane, snapshot.LastDeniedFixedSlot?.Owner);
		Assert.Equal(AgnusChipSlotOwner.Refresh, snapshot.LastDeniedFixedSlotBlocker?.Owner);
		Assert.Equal(1, snapshot.RefreshDeniedFixedSlotBlockerCount);
	}

	[Fact]
	public void RowBitplaneDmaBatchSkipsMissingRowsWithoutBusAccess()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		var lineStartCycle = RowLineStartCycle(fetchCycle);
		var entries = new[]
		{
			new RowDmaBitplaneEntry(RowCycleOffset(fetchCycle), plane: 0, word: 0, slot: HrmLowResPlane1FetchSlot, address: 0x2000, rowPresent: false)
		};
		var values = new ushort[entries.Length];
		var granted = new bool[entries.Length];

		bus.ReadRowBitplaneDmaFetchesForPresentation(
			entries,
			lineStartCycle,
			values,
			granted,
			out var grantedCount,
			out var firstGrantedCycle,
			out var lastGrantedCycle);

		Assert.False(granted[0]);
		Assert.Equal(0, values[0]);
		Assert.Equal(0, grantedCount);
		Assert.Equal(-1, firstGrantedCycle);
		Assert.Equal(-1, lastGrantedCycle);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void RowBitplaneDmaBatchCapturesPresentBusAccessesOnly()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		var lineStartCycle = RowLineStartCycle(fetchCycle);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0x1111);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2002, 0x2222);
		var entries = new[]
		{
			new RowDmaBitplaneEntry(RowCycleOffset(fetchCycle), plane: 0, word: 0, slot: HrmLowResPlane1FetchSlot, address: 0x2000, rowPresent: true),
			new RowDmaBitplaneEntry(RowCycleOffset(fetchCycle + 2), plane: 1, word: 0, slot: 3, address: 0, rowPresent: false),
			new RowDmaBitplaneEntry(RowCycleOffset(fetchCycle + 4), plane: 2, word: 0, slot: 5, address: 0x2002, rowPresent: true)
		};
		var values = new ushort[entries.Length];
		var granted = new bool[entries.Length];

		bus.ReadRowBitplaneDmaFetchesForPresentation(
			entries,
			lineStartCycle,
			values,
			granted,
			out var grantedCount,
			out var firstGrantedCycle,
			out var lastGrantedCycle);

		Assert.Equal(2, grantedCount);
		Assert.Equal(fetchCycle, firstGrantedCycle);
		Assert.Equal(fetchCycle + 4, lastGrantedCycle);
		Assert.True(granted[0]);
		Assert.False(granted[1]);
		Assert.True(granted[2]);
		Assert.Equal(0x1111, values[0]);
		Assert.Equal(0, values[1]);
		Assert.Equal(0x2222, values[2]);
		Assert.Equal(2, bus.BusAccesses.Count);
	}

	[Fact]
	public void RowBitplaneDmaBatchSamplesPresentationHistoryAtGrantedCycle()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var cpuCycle = 32L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);
		var entries = new[]
		{
			new RowDmaBitplaneEntry(20, plane: 0, word: 0, slot: 0, address: 0x2400, rowPresent: true),
			new RowDmaBitplaneEntry(56, plane: 0, word: 1, slot: 0, address: 0x2400, rowPresent: true)
		};
		var values = new ushort[entries.Length];
		var granted = new bool[entries.Length];

		bus.ReadRowBitplaneDmaFetchesForPresentation(
			entries,
			lineStartCycle: 0,
			values,
			granted,
			out var grantedCount,
			out var firstGrantedCycle,
			out var lastGrantedCycle);

		Assert.Equal(2, grantedCount);
		Assert.Equal(20, firstGrantedCycle);
		Assert.Equal(56, lastGrantedCycle);
		Assert.True(granted[0]);
		Assert.True(granted[1]);
		Assert.Equal(0x1234, values[0]);
		Assert.Equal(0x5678, values[1]);
	}

	[Fact]
	public void PreparedBitplaneSlotAllowsLaterFixedReservationFromResolvedAddress()
	{
		var engine = new AgnusHrmSlotEngine();
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		var preparedRequest = new AmigaBusAccessRequest(
			AmigaBusRequester.Bitplane,
			AmigaBusAccessKind.Bitplane,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			fetchCycle,
			isWrite: false);
		var resolvedRequest = new AmigaBusAccessRequest(
			AmigaBusRequester.Bitplane,
			AmigaBusAccessKind.Bitplane,
			AmigaBusAccessTarget.ChipRam,
			0x2000,
			AmigaBusAccessSize.Word,
			fetchCycle,
			isWrite: false);

		Assert.True(engine.TryReserveExactFixedDmaSlot(preparedRequest, out var prepared));
		Assert.Equal(fetchCycle, prepared.GrantedCycle);

		Assert.True(engine.TryReserveFixedDmaSlot(resolvedRequest, out var resolved));
		Assert.Equal(fetchCycle, resolved.GrantedCycle);
		Assert.Equal(0, engine.DeniedFixedSlotCount);
	}

	[Fact]
	public void HardwareSchedulerDrainToSameCycleDoesNotDuplicateCiaTimerInterrupt()
	{
		var bus = new AmigaBus();
		var setupEvents = new List<AmigaCiaInterruptEvent>();
		bus.CiaA.WriteRegister(0x04, 0x02, 0, setupEvents);
		bus.CiaA.WriteRegister(0x05, 0x00, 0, setupEvents);
		bus.CiaA.WriteRegister(0x0D, 0x81, 0, setupEvents);
		bus.CiaA.WriteRegister(0x0E, 0x11, 0, setupEvents);
		Assert.Empty(bus.DrainCiaInterrupts());

		bus.AdvanceHardwareTo(20);
		var first = bus.DrainCiaInterrupts();
		bus.AdvanceHardwareTo(20);
		var second = bus.DrainCiaInterrupts();

		var interrupt = Assert.Single(first);
		Assert.Equal(AmigaCiaId.A, interrupt.Cia);
		Assert.Equal(AmigaCia.TimerAInterruptMask, interrupt.IcrBits);
		Assert.Equal(20, interrupt.Cycle);
		Assert.Empty(second);
	}

	[Fact]
	public void CpuCustomWritePublishesSameCyclePaulaRegisterLatch()
	{
		var bus = new AmigaBus();
		const long cycle = 40;
		bus.AdvanceHardwareTo(cycle);
		var writeCycle = cycle;
		bus.WriteWord(0x00DFF09C, (ushort)(0x8000 | AmigaConstants.IntreqBlitter), ref writeCycle, AmigaBusAccessKind.CpuDataWrite);

		var readCycle = writeCycle;
		var value = bus.ReadWord(0x00DFF01E, ref readCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, value & AmigaConstants.IntreqBlitter);
	}

	[Fact]
	public void RepeatedIntreqPollingDoesNotPublishHardwareEvents()
	{
		var bus = new AmigaBus();
		var cycle = 4L;
		_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var before = bus.CaptureHardwareSchedulerSnapshot();

		for (var i = 0; i < 16; i++)
		{
			cycle += 4;
			_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		}

		var after = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(before.RasterEvents, after.RasterEvents);
		Assert.Equal(before.CiaEvents, after.CiaEvents);
		Assert.Equal(before.PaulaEvents, after.PaulaEvents);
		Assert.Equal(before.DiskEvents, after.DiskEvents);
		Assert.Equal(before.BlitterEvents, after.BlitterEvents);
	}

	[Fact]
	public void SlotContendedCleanThroughSkipsRepeatedReadsBeforeNextHardwareEvent()
	{
		var bus = new AmigaBus();
		var cycle = 4L;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var before = bus.CaptureHardwareSchedulerSnapshot();

		for (var i = 0; i < 16; i++)
		{
			cycle += 4;
			_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		}

		var after = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(
			after.BusAccessDrainCount > before.BusAccessDrainCount,
			$"Expected repeated slot-contended reads to use the bus-access drain entry point, before={before.BusAccessDrainCount}, after={after.BusAccessDrainCount}");
		Assert.Equal(before.DrainCount, after.DrainCount);
		Assert.Equal(before.RasterEvents, after.RasterEvents);
		Assert.Equal(before.CiaEvents, after.CiaEvents);
		Assert.Equal(before.PaulaEvents, after.PaulaEvents);
		Assert.Equal(before.DiskEvents, after.DiskEvents);
		Assert.Equal(before.AgnusEvents, after.AgnusEvents);
		Assert.Equal(before.BlitterEvents, after.BlitterEvents);
	}

	[Fact]
	public void WakeAgendaSkipsSlotContendedReadsWithFutureLiveDisplayWork()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.Display.ScheduleWrite(AmigaConstants.A500PalCpuCyclesPerRasterLine, 0x180, 0x0123);
		var cycle = 20L;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var before = bus.CaptureHardwareSchedulerSnapshot();

		for (var i = 0; i < 16; i++)
		{
			cycle += 4;
			_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		}

		var after = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(
			after.WakeAgendaDrainSkips > before.WakeAgendaDrainSkips,
			$"Expected future live display work not to block slot-contended wake agenda skips, before={before.WakeAgendaDrainSkips}, after={after.WakeAgendaDrainSkips}");
		Assert.Equal(before.AgnusEvents, after.AgnusEvents);
	}

	[Fact]
	public void SlotContendedCleanThroughDoesNotHideSameCycleHardwareInterruptAfterCachedSlotRead()
	{
		var bus = new AmigaBus();
		var cycle = 20L;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		cycle += 4;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var writeCycle = cycle + 4;
		bus.RequestHardwareInterrupt(AmigaConstants.IntreqBlitter, writeCycle);
		var readCycle = writeCycle;
		_ = bus.ReadWord(0x00001000, ref readCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, bus.Paula.Intreq & AmigaConstants.IntreqBlitter);
	}

	[Fact]
	public void WakeAgendaInvalidatesWhenCustomWriteSchedulesHardwareWork()
	{
		var bus = new AmigaBus();
		var cycle = 20L;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		cycle += 4;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var before = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(before.WakeAgendaCacheHits > 0);

		cycle += 4;
		bus.WriteWord(0x00DFF09C, (ushort)(0x8000 | AmigaConstants.IntreqCopper), ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var after = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(
			after.WakeAgendaInvalidations > before.WakeAgendaInvalidations,
			$"Expected a custom write to invalidate the wake agenda, before={before.WakeAgendaInvalidations}, after={after.WakeAgendaInvalidations}");
	}

	[Fact]
	public void IntreqTimedReadObservesPublishedVblankOnly()
	{
		var bus = new AmigaBus();
		var frameCycle = (long)AmigaConstants.A500PalCpuCyclesPerFrame;

		var initialCycle = 0L;
		var initial = bus.ReadWord(0x00DFF01E, ref initialCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, initial & AmigaConstants.IntreqVerticalBlank);

		var cycle = frameCycle;
		var value = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0, value & AmigaConstants.IntreqVerticalBlank);

		bus.AdvanceRasterTo(frameCycle);
		cycle = frameCycle;
		value = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, value & AmigaConstants.IntreqVerticalBlank);
	}

	[Fact]
	public void IntreqReadAfterSameCyclePseudoFastAccessObservesPublishedVblankOnly()
	{
		var bus = new AmigaBus(expansionRamSize: 0x10000);
		var frameCycle = (long)AmigaConstants.A500PalCpuCyclesPerFrame;

		var memoryCycle = frameCycle;
		_ = bus.ReadWord(
			AmigaConstants.A500BootPseudoFastRamBase,
			ref memoryCycle,
			AmigaBusAccessKind.CpuDataRead);

		var intreqCycle = frameCycle;
		var value = bus.ReadWord(0x00DFF01E, ref intreqCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0, value & AmigaConstants.IntreqVerticalBlank);

		bus.AdvanceRasterTo(frameCycle);
		intreqCycle = frameCycle;
		value = bus.ReadWord(0x00DFF01E, ref intreqCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, value & AmigaConstants.IntreqVerticalBlank);
	}

	[Fact]
	public void RepeatedIntreqPollingDoesNotPublishCiaTimerInterrupt()
	{
		var bus = new AmigaBus();
		var setupEvents = new List<AmigaCiaInterruptEvent>();
		bus.CiaA.WriteRegister(0x04, 0x02, 0, setupEvents);
		bus.CiaA.WriteRegister(0x05, 0x00, 0, setupEvents);
		bus.CiaA.WriteRegister(0x0D, 0x81, 0, setupEvents);
		bus.CiaA.WriteRegister(0x0E, 0x11, 0, setupEvents);
		Assert.Empty(bus.DrainCiaInterrupts());

		for (var requestedCycle = 0L; requestedCycle <= 20; requestedCycle += 4)
		{
			var cycle = requestedCycle;
			_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		}

		Assert.Empty(bus.DrainCiaInterrupts());
		bus.AdvanceCiaTimersTo(20);
		var interrupt = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.A, interrupt.Cia);
		Assert.Equal(AmigaCia.TimerAInterruptMask, interrupt.IcrBits);
		Assert.Equal(20, interrupt.Cycle);
	}

	[Fact]
	public void RepeatedIntreqPollingDoesNotPublishDiskBlockInterrupt()
	{
		const ushort DskBlkInterrupt = 0x0002;
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.Paula.ScheduleWrite(0, 0x096, 0x8210);
		bus.Paula.AdvanceTo(32);
		bus.Disk.WriteRegister(0x020, 0x0000, 0);
		bus.Disk.WriteRegister(0x022, 0x1000, 0);
		bus.Disk.WriteRegister(0x024, 0x8002, 0);
		bus.Disk.WriteRegister(0x024, 0x8002, 0);

		foreach (var requestedCycle in new[] { 0L, Math.Max(0, readyCycle - 4) })
		{
			var cycle = requestedCycle;
			_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		}

		var startReadCycle = readyCycle;
		_ = bus.ReadWord(0x00DFF01E, ref startReadCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().TransferCount);
		Assert.Equal(0, bus.Paula.Intreq & DskBlkInterrupt);

		bus.AdvanceDmaTo(readyCycle);
		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		Assert.True(completionCycle > readyCycle);
		bus.AdvanceDmaTo(completionCycle);
		var completionReadCycle = completionCycle;
		var value = bus.ReadWord(0x00DFF01E, ref completionReadCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, value & DskBlkInterrupt);
		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
	}

	[Fact]
	public void RepeatedIntreqPollingDoesNotPublishBlitterInterrupt()
	{
		var bus = new AmigaBus();
		StartLongBlit(bus);
		var completionCycle = bus.Blitter.GetPredictedCompletionCycle();
		Assert.True(completionCycle > 0);

		var beforeCycle = Math.Max(0, completionCycle - 16);
		_ = bus.ReadWord(0x00DFF01E, ref beforeCycle, AmigaBusAccessKind.CpuDataRead);

		var cycle = completionCycle;
		var value = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0, value & AmigaConstants.IntreqBlitter);

		var publishedCycle = completionCycle + 10_000;
		bus.Blitter.AdvanceTo(publishedCycle);
		cycle = publishedCycle;
		value = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, value & AmigaConstants.IntreqBlitter);
	}

	[Fact]
	public void SameCycleCpuPaulaRegisterWritesRemainVisibleToSameCycleIntreqReads()
	{
		var bus = new AmigaBus();
		var cycle = 40L;
		bus.WriteWord(0x00DFF09C, (ushort)(0x8000 | AmigaConstants.IntreqVerticalBlank), ref cycle, AmigaBusAccessKind.CpuDataWrite);
		Assert.NotEqual(0, bus.Paula.Intreq & AmigaConstants.IntreqVerticalBlank);

		bus.WriteWord(0x00DFF09C, AmigaConstants.IntreqVerticalBlank, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		var value = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0, value & AmigaConstants.IntreqVerticalBlank);
		Assert.Equal(0, bus.Paula.Intreq & AmigaConstants.IntreqVerticalBlank);
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
		bus.Paula.AdvanceTo(32);
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
	public void DskbytrReadObservesPublishedPassiveInputAndClearsReadyOnce()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);

		var firstCycle = targetCycle;
		var first = bus.ReadWord(0x00DFF01A, ref firstCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, first & 0x8000);

		bus.AdvanceDmaTo(targetCycle);
		firstCycle = targetCycle;
		first = bus.ReadWord(0x00DFF01A, ref firstCycle, AmigaBusAccessKind.CpuDataRead);
		var secondCycle = firstCycle;
		var second = bus.ReadWord(0x00DFF01A, ref secondCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, first & 0x8000);
		Assert.Equal(0x12, first & 0x00FF);
		Assert.Equal(0, second & 0x8000);
		Assert.Equal(0x12, second & 0x00FF);
	}

	[Fact]
	public void SlotContendedChipRamReadDoesNotAdvancePassiveDiskSyncInput()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x5AA5);
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x4489));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 2);

		var chipCycle = targetCycle;
		var chipValue = bus.ReadWord(0x00001000, ref chipCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0x5AA5, chipValue);
		Assert.Equal(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x9000);

		var dskCycle = chipCycle;
		var dskbytr = bus.ReadWord(0x00DFF01A, ref dskCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, dskbytr & 0x1000);

		bus.AdvanceDmaTo(targetCycle);
		dskCycle = chipCycle;
		dskbytr = bus.ReadWord(0x00DFF01A, ref dskCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.NotEqual(0, dskbytr & 0x1000);
	}

	[Fact]
	public void RegisterLatchReadsObservePublishedWritesOnly()
	{
		var bus = new AmigaBus();
		bus.Paula.ScheduleWrite(20, 0x09E, 0x8001);

		var beforeCycle = 10L;
		Assert.Equal(0, bus.ReadWord(0x00DFF010, ref beforeCycle, AmigaBusAccessKind.CpuDataRead));

		var atCycle = 20L;
		Assert.Equal(0, bus.ReadWord(0x00DFF010, ref atCycle, AmigaBusAccessKind.CpuDataRead));

		bus.Paula.AdvanceRegisterObservableTo(20);
		atCycle = 20L;
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
	public void DiskStatusRegisterReadsDoNotPublishPassiveInput()
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
		Assert.Equal(0, dskbytr & 0x8000);

		bus.AdvanceDmaTo(targetCycle);
		dskCycle = cycle;
		dskbytr = bus.ReadWord(0x00DFF01A, ref dskCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.NotEqual(0, dskbytr & 0x8000);
		Assert.Equal(0x12, dskbytr & 0x00FF);
	}

	[Fact]
	public void CiaPollingDoesNotPublishEventsOrPassiveDiskInput()
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
		Assert.Empty(timerEvents);
		bus.AdvanceCiaTimersTo(ciaGrantCycle);
		timerEvents = bus.DrainCiaInterrupts()
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
		Assert.Equal(0, dskbytr & 0x8000);

		bus.AdvanceDmaTo(targetCycle);
		cycle = ciaGrantCycle;
		dskbytr = bus.ReadWord(0x00DFF01A, ref cycle, AmigaBusAccessKind.CpuDataRead);
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
		Assert.Equal(0, dskbytr & 0x8000);

		bus.AdvanceDmaTo(dskCycle);
		dskCycle = ciaGrantCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);
		dskbytr = bus.ReadWord(0x00DFF01A, ref dskCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.NotEqual(0, dskbytr & 0x8000);
		Assert.Equal(0x12, dskbytr & 0x00FF);
	}

	[Fact]
	public void DmaconrPollingDoesNotAdvanceBlitterOrAudioPlayback()
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

		Assert.NotEqual(0, complete & 0x40);

		var publishedCycle = completionCycle + 10_000;
		bus.Blitter.AdvanceTo(publishedCycle);
		cycle = publishedCycle;
		complete = bus.ReadByte(0x00DFF002, ref cycle, AmigaBusAccessKind.CpuDataRead);

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
	public void CpuCyclesUseDocumentedInstructionFloorAndDelayedFetchCompletion()
	{
		var arbiter = new FixedDelayArbiter(waitCycles: 5, accessCycles: 3);
		var bus = new AmigaBus(arbiter: arbiter);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);

		cpu.ExecuteInstruction();

		var fetches = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch)
			.OrderBy(access => access.Request.Address)
			.ToArray();
		Assert.Equal(2, fetches.Length);
		Assert.Equal(0x1000u, fetches[0].Request.Address);
		Assert.Equal(0x1002u, fetches[1].Request.Address);
		Assert.Equal(0, fetches[0].RequestedCycle);
		Assert.Equal(5, fetches[0].GrantedCycle);
		Assert.Equal(8, fetches[0].CompletedCycle);
		Assert.Equal(fetches[0].CompletedCycle, fetches[1].RequestedCycle);
		Assert.Equal(13, fetches[1].GrantedCycle);
		Assert.Equal(16, fetches[1].CompletedCycle);
		Assert.NotEqual(fetches[0].RequestedCycle, fetches[1].RequestedCycle);
		Assert.Equal(8, cpu.State.Cycles);
	}

	[Fact]
	public void InstructionFetchWindowMatchesGenericChipFetchTimingAndValue()
	{
		var genericBus = new AmigaBus(arbiter: new FixedDelayArbiter(waitCycles: 5, accessCycles: 3));
		var windowBus = new AmigaBus(arbiter: new FixedDelayArbiter(waitCycles: 5, accessCycles: 3));
		Write(genericBus.ChipRam, 0x1000, 0x12, 0x34);
		Write(windowBus.ChipRam, 0x1000, 0x12, 0x34);
		var genericCycle = 11L;
		var windowCycle = 11L;

		var genericValue = genericBus.ReadWord(0x00001000, ref genericCycle, AmigaBusAccessKind.CpuInstructionFetch);
		Assert.True(windowBus.TryGetInstructionFetchWindow(0x00001000, out var window));
		windowBus.CommitInstructionFetchWindowWord(in window, 0x00001000, ref windowCycle);
		var windowValue = window.ReadWord(0x00001000);

		Assert.Equal(genericValue, windowValue);
		Assert.Equal(genericCycle, windowCycle);
		var genericAccess = Assert.Single(genericBus.BusAccesses);
		var windowAccess = Assert.Single(windowBus.BusAccesses);
		Assert.Equal(genericAccess.Request.Requester, windowAccess.Request.Requester);
		Assert.Equal(genericAccess.Request.Kind, windowAccess.Request.Kind);
		Assert.Equal(genericAccess.Request.Target, windowAccess.Request.Target);
		Assert.Equal(genericAccess.Request.Address, windowAccess.Request.Address);
		Assert.Equal(genericAccess.Request.Size, windowAccess.Request.Size);
		Assert.Equal(genericAccess.Request.RequestedCycle, windowAccess.Request.RequestedCycle);
		Assert.Equal(genericAccess.Request.IsWrite, windowAccess.Request.IsWrite);
		Assert.Equal(genericAccess.GrantedCycle, windowAccess.GrantedCycle);
		Assert.Equal(genericAccess.CompletedCycle, windowAccess.CompletedCycle);
	}

	[Fact]
	public void AccurateM68000CpuBusPhasesMirrorInstructionFetchWindowGrants()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71); // NOP
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x2000);

		cpu.ExecuteInstruction();

		var fetchPhases = bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch)
			.Take(2)
			.ToArray();
		var fetchAccesses = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch)
			.Take(2)
			.ToArray();
		Assert.Equal(2, fetchPhases.Length);
		Assert.Equal(fetchAccesses.Length, fetchPhases.Length);
		for (var i = 0; i < fetchPhases.Length; i++)
		{
			var phase = fetchPhases[i];
			var access = fetchAccesses[i];
			Assert.True(phase.BusAccess.HasValue);
			Assert.Equal(access.Request.Address, phase.CpuPhase.Address);
			Assert.Equal(access.Request.RequestedCycle, phase.CpuPhase.RequestedCycle);
			Assert.Equal(access.CompletedCycle, phase.CpuPhase.CompletedCycle);
			var phaseAccess = phase.BusAccess.GetValueOrDefault();
			Assert.Equal(access.GrantedCycle, phaseAccess.GrantedCycle);
			Assert.Equal(access.CompletedCycle, phaseAccess.CompletedCycle);
			Assert.Equal(access.GrantedCycle, phase.SecondWordCycle);
		}
	}

	[Fact]
	public void AccurateM68000CpuBusPhaseRecordsChipSlotWait()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71); // NOP
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 1;

		cpu.ExecuteInstruction();

		var waitedPhase = bus.CpuBusPhases.First(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.BusAccess.HasValue &&
			phase.BusAccess.GetValueOrDefault().WaitCycles > 0);
		Assert.Equal(0x1000u, waitedPhase.CpuPhase.InstructionProgramCounter);
		Assert.Equal(0x1000u, waitedPhase.CpuPhase.Address);
		Assert.Equal(1, waitedPhase.CpuPhase.RequestedCycle);
		var waitedAccess = waitedPhase.BusAccess.GetValueOrDefault();
		Assert.True(waitedAccess.GrantedCycle > waitedAccess.Request.RequestedCycle);
		Assert.Equal(waitedAccess.GrantedCycle - waitedAccess.Request.RequestedCycle, waitedAccess.WaitCycles);
		Assert.Equal(waitedAccess.CompletedCycle, waitedPhase.CpuPhase.CompletedCycle);
		Assert.True(waitedPhase.GrantedSlot.HasValue);
		var waitedSlot = waitedPhase.GrantedSlot.GetValueOrDefault();
		Assert.Equal(AgnusChipSlotOwner.Cpu, waitedSlot.Owner);
	}

	[Fact]
	public void AccurateM68000CpuBusPhaseLongWriteExposesSecondWordCycle()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x20, 0x80); // MOVE.L D0,(A0)
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[0] = 0x2400;

		cpu.ExecuteInstruction();

		var writePhase = Assert.Single(
			bus.CpuBusPhases,
			phase => phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite);
		Assert.True(writePhase.BusAccess.HasValue);
		Assert.Equal(0x2400u, writePhase.CpuPhase.Address);
		Assert.Equal(M68kOperandSize.Long, writePhase.CpuPhase.Size);
		var writeAccess = writePhase.BusAccess.GetValueOrDefault();
		Assert.Equal(AmigaBusAccessSize.Long, writeAccess.Request.Size);
		Assert.Equal(
			writeAccess.GrantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles),
			writePhase.SecondWordCycle);
		Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x2400, writeAccess.GrantedCycle));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x2402, writePhase.SecondWordCycle));
	}

	[Fact]
	public void AccurateM68000SelfModifiedQueuedPrefetchStaysDeterministic()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x21, 0xFC, 0x70, 0x05, 0x4E, 0x71, 0x10, 0x08); // MOVE.L #$70054E71,$1008.W
		Write(bus.ChipRam, 0x1008, 0x70, 0x01); // MOVEQ #1,D0, already queued before the write
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x7005, BigEndian.ReadUInt16(bus.ChipRam, 0x1008, "self-modified word"));
		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Contains(
			bus.CpuBusPhases,
			phase => phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
				phase.CpuPhase.Address == 0x1008u);
		Assert.Contains(
			bus.CpuBusPhases,
			phase => phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
				phase.CpuPhase.Address == 0x1008u);
	}

	[Fact]
	public void InstructionFetchWindowInvalidatesWhenKickstartOverlayChanges()
	{
		var bus = new AmigaBus();
		var rom = new byte[0x40000];
		Write(rom, 0x0000, 0x12, 0x34);
		bus.MapReadOnlyMemory(0x00FC0000, rom);

		Assert.True(bus.TryGetInstructionFetchWindow(0x00000000, out var window));
		Assert.True(window.ContainsWord(0x00000000));

		var cycle = 0L;
		bus.WriteByte(0x00BFE001, 0x00, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.False(window.ContainsWord(0x00000000));
	}

	[Fact]
	public void InstructionFetchWindowStopsBeforeHostTrapStub()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71);
		bus.RegisterHostTrapStub(0x00001004, _ => { });

		Assert.True(bus.TryGetInstructionFetchWindow(0x00001000, out var window));
		Assert.True(window.ContainsWord(0x00001000));
		Assert.True(window.ContainsWord(0x00001002));
		Assert.False(window.ContainsWord(0x00001004));
		Assert.False(bus.TryGetInstructionFetchWindow(0x00001004, out _));
	}

	[Fact]
	public void InstructionFetchWindowStopsAtChipRamBackingBoundary()
	{
		var bus = new AmigaBus(chipRamSize: 0x80000);
		Write(bus.ChipRam, 0x7FFC, 0x12, 0x34, 0x56, 0x78);

		Assert.True(bus.TryGetInstructionFetchWindow(0x0007FFFC, out var window));
		Assert.True(window.ContainsWord(0x0007FFFC));
		Assert.True(window.ContainsWord(0x0007FFFE));
		Assert.False(window.ContainsWord(0x00080000));

		Assert.True(bus.TryGetInstructionFetchWindow(0x00080000, out var mirrorWindow));
		Assert.True(mirrorWindow.ContainsWord(0x00080000));
		Assert.Equal(0, mirrorWindow.MemoryOffset);
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

		bus.Paula.AdvanceTo(32);

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

	[Fact]
	public void SpriteDmaWordExactSlotReadsOnlyFixedSpriteSlot()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x5AA5);
		var spriteCycle = AgnusHrmOcsSlotTable.FirstSpriteHorizontal * AgnusChipSlotScheduler.SlotCycles;

		Assert.True(bus.TryReadRowSpriteDmaWordForPresentation(0x1000, spriteCycle, out var value, out var grantedCycle));

		Assert.Equal(0x5AA5, value);
		Assert.Equal(spriteCycle, grantedCycle);
		var access = Assert.Single(bus.BusAccesses, access => access.Request.Kind == AmigaBusAccessKind.Sprite);
		Assert.Equal(spriteCycle, access.RequestedCycle);
		Assert.Equal(spriteCycle, access.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Sprite, bus.Agnus.CaptureSnapshot().LastGrantedSlot?.Owner);
	}

	[Fact]
	public void SpriteDmaWordExactSlotDoesNotSearchForwardFromWrongSlot()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x5AA5);
		var wrongCycle = (AgnusHrmOcsSlotTable.FirstSpriteHorizontal - 1) * AgnusChipSlotScheduler.SlotCycles;

		Assert.False(bus.TryReadRowSpriteDmaWordForPresentation(0x1000, wrongCycle, out var value, out var grantedCycle));

		Assert.Equal(0, value);
		Assert.Equal(wrongCycle, grantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Sprite, bus.Agnus.CaptureSnapshot().LastDeniedFixedSlot?.Owner);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().SpriteSlotGrantCount);
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
	public void PaulaDmaChannelReadsUseHrmChannelSlotOrder()
	{
		var bus = new AmigaBus();

		var accesses = Enumerable
			.Range(0, AmigaConstants.PaulaChannelCount)
			.Select(channel => bus.ReadPaulaDmaWord(channel, 0x2400u + ((uint)channel * 2u), 0).BusAccess)
			.ToArray();

		Assert.Equal(
			new long[]
			{
				AgnusHrmOcsSlotTable.FirstPaulaHorizontal * AgnusChipSlotScheduler.SlotCycles,
				(AgnusHrmOcsSlotTable.FirstPaulaHorizontal + 2) * AgnusChipSlotScheduler.SlotCycles,
				(AgnusHrmOcsSlotTable.FirstPaulaHorizontal + 4) * AgnusChipSlotScheduler.SlotCycles,
				(AgnusHrmOcsSlotTable.FirstPaulaHorizontal + 6) * AgnusChipSlotScheduler.SlotCycles
			},
			accesses.Select(access => access.GrantedCycle).ToArray());
		Assert.Equal(new[] { 0, 1, 2, 3 }, accesses.Select(access => access.Request.Channel).ToArray());
		Assert.Equal(AgnusChipSlotOwner.Paula, bus.Agnus.CaptureSnapshot().LastGrantedSlot?.Owner);
	}

	[Fact]
	public void PaulaDmaSameChannelRetryPreservesDeniedSlotAccounting()
	{
		var bus = new AmigaBus();

		var first = bus.ReadPaulaDmaWord(0, 0x2400, 0).BusAccess;
		var second = bus.ReadPaulaDmaWord(0, 0x2402, 0).BusAccess;
		var snapshot = bus.Agnus.CaptureSnapshot();

		Assert.Equal(AgnusHrmOcsSlotTable.FirstPaulaHorizontal * AgnusChipSlotScheduler.SlotCycles, first.GrantedCycle);
		Assert.Equal(first.GrantedCycle + AmigaConstants.A500PalCpuCyclesPerRasterLine, second.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Paula, snapshot.LastDeniedFixedSlot?.Owner);
		Assert.Equal(AgnusChipSlotOwner.Paula, snapshot.LastDeniedFixedSlotBlocker?.Owner);
		Assert.Equal(1, snapshot.PaulaDeniedFixedSlotCount);
		Assert.Equal(1, snapshot.PaulaDeniedFixedSlotBlockerCount);
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
	public void StoppedCpuWakeCandidateIgnoresPendingDiskReadDmaWhenDskblkDisabled()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.WriteWord(0x00DFF096, 0x8210, 0);
		bus.Paula.AdvanceTo(0);
		bus.Disk.WriteRegister(0x024, 0x8001, 0);
		bus.Disk.WriteRegister(0x024, 0x8001, 0);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 100, out var wakeSource);

		Assert.Equal(100, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, wakeSource);
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
	public void StoppedCpuWakeCandidateIncludesBenignLiveCopperControlBoundary()
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
	public void CopperQuiescentWindowStartsWhenCopperWaitsPastFrameEnd()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentDiagnostics: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		Assert.True(bus.Display.TryGetCopperQuiescentWindow(cycle, out var start, out var end));
		Assert.Equal(cycle, start);
		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerFrame, end);
		var snapshot = bus.Display.CaptureSnapshot();
		Assert.Equal(1, snapshot.CopperQuiescentWindowCount);
		Assert.True(snapshot.CopperQuiescentTotalCycles > 0);
	}

	[Fact]
	public void CustomRegisterScheduleClassifierTreatsKnownBusScheduleBenignWritesAsBenign()
	{
		Assert.False(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x180));
		Assert.False(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x1BE));
		Assert.False(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x181));
		Assert.False(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x120));
		Assert.False(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x146));
		Assert.False(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x0A8));
		Assert.False(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x0D8));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x09A));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x09C));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x096));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x09E));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x092));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x100));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x088));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x0A4));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x0A6));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x0AA));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x024));
		Assert.True(CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(0x058));
	}

	[Fact]
	public void CopperQuiescentCpuScheduleAffectingCustomWriteIsCountedAsInvalidationRisk()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentDiagnostics: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF09C, 0x8004, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentCustomRegisterWrites >= 1);
		Assert.True(scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentCpuBenignCustomWrites);
		Assert.True(scheduler.CopperQuiescentSchedulerDrains >= 1);
	}

	[Fact]
	public void CopperQuiescentCpuColorWriteIsCountedAsBenignCustomWrite()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF180, 0x0F00, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentCustomRegisterWrites >= 1);
		Assert.True(scheduler.CopperQuiescentCpuBenignCustomWrites >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathRejectedInvalidated);
	}

	[Fact]
	public void CopperQuiescentCpuSpriteWriteIsCountedAsBenignCustomWrite()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF146, 0x1234, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentCustomRegisterWrites >= 1);
		Assert.True(scheduler.CopperQuiescentCpuBenignCustomWrites >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathRejectedInvalidated);
	}

	[Fact]
	public void CopperQuiescentCpuAudioVolumeWriteIsCountedAsBenignCustomWrite()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF0A8, 0x0040, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentCustomRegisterWrites >= 1);
		Assert.True(scheduler.CopperQuiescentCpuBenignCustomWrites >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathRejectedInvalidated);
	}

	[Fact]
	public void CopperQuiescentShadowPredictorMatchesStaticCpuChipGrant()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentDiagnostics: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentSlotContendedAccesses >= 1);
		Assert.True(scheduler.CopperQuiescentShadowPredictions >= 1);
		Assert.Equal(scheduler.CopperQuiescentShadowPredictions, scheduler.CopperQuiescentShadowMatches);
		Assert.Equal(0, scheduler.CopperQuiescentShadowMismatches);
	}

	[Fact]
	public void CopperQuiescentShadowPredictorMatchesPreparedBitplaneSlotGrant()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentDiagnostics: true);
		ConfigureLiveOneBitplaneDma(bus);
		StartCopperEndOfFrameWait(bus);
		var cycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentShadowPredictions >= 1);
		Assert.Equal(scheduler.CopperQuiescentShadowPredictions, scheduler.CopperQuiescentShadowMatches);
		Assert.Equal(0, scheduler.CopperQuiescentShadowMismatches);
	}

	[Fact]
	public void CopperQuiescentShadowPredictorReportsUnsupportedWhenPaulaDmaIsDue()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentDiagnostics: true);
		StartCopperEndOfFrameWait(bus);
		var lineStart = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		for (var i = 0; i < 64; i += 2)
		{
			BigEndian.WriteUInt16(bus.ChipRam, 0x7000 + i, (ushort)(0x4000 + i));
		}

		bus.Paula.ScheduleWrite(lineStart, 0x0A0, 0x0000);
		bus.Paula.ScheduleWrite(lineStart, 0x0A2, 0x7000);
		bus.Paula.ScheduleWrite(lineStart, 0x0A4, 0x0020);
		bus.Paula.ScheduleWrite(lineStart, 0x0A6, 0x0002);
		bus.Paula.ScheduleWrite(lineStart, 0x096, 0x8201);
		bus.Paula.AdvanceTo(lineStart);
		var cycle = lineStart + (AgnusHrmOcsSlotTable.FirstPaulaHorizontal * AgnusChipSlotScheduler.SlotCycles) - 2;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentShadowUnsupported >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentShadowMismatches);
	}

	[Fact]
	public void CopperQuiescentFastPathIsDisabledByDefault()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.CopperQuiescentFastPathAttempts);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathSkippedDrains);
	}

	[Fact]
	public void CopperQuiescentFastPathSkipsSupportedCpuChipDrainWithoutChangingGrant()
	{
		var baseline = new AmigaBus(enableLiveAgnusDma: true);
		var fast = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		StartCopperEndOfFrameWait(baseline);
		StartCopperEndOfFrameWait(fast);
		var baselineCycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var fastCycle = baselineCycle;

		var baselineValue = baseline.ReadWord(0x00001000, ref baselineCycle, AmigaBusAccessKind.CpuDataRead);
		var fastValue = fast.ReadWord(0x00001000, ref fastCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(baselineValue, fastValue);
		Assert.Equal(baselineCycle, fastCycle);
		var scheduler = fast.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentFastPathAttempts >= 1);
		Assert.True(scheduler.CopperQuiescentFastPathUsed >= 1);
		Assert.True(scheduler.CopperQuiescentFastPathSkippedDrains >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathVerificationMismatches);
	}

	[Fact]
	public void CopperQuiescentFastPathPreservesPreparedBitplaneContentionGrant()
	{
		var baseline = new AmigaBus(enableLiveAgnusDma: true);
		var fast = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		ConfigureLiveOneBitplaneDma(baseline);
		ConfigureLiveOneBitplaneDma(fast);
		StartCopperEndOfFrameWait(baseline);
		StartCopperEndOfFrameWait(fast);
		var baselineCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		var fastCycle = baselineCycle;

		_ = baseline.ReadWord(0x00001000, ref baselineCycle, AmigaBusAccessKind.CpuDataRead);
		_ = fast.ReadWord(0x00001000, ref fastCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(baselineCycle, fastCycle);
		Assert.True(fast.CaptureHardwareSchedulerSnapshot().CopperQuiescentFastPathSkippedDrains >= 1);
	}

	[Fact]
	public void CopperQuiescentFastPathCustomWriteInvalidatesRemainderOfFrame()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		bus.WriteWord(0x00DFF09C, 0x8004, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentCustomRegisterWrites >= 1);
		Assert.True(scheduler.CopperQuiescentCpuScheduleAffectingCustomWrites >= 1);
		Assert.True(scheduler.CopperQuiescentFastPathRejectedInvalidated >= 1);
	}

	[Fact]
	public void CopperQuiescentFastPathRejectsLongAccessAsUnsupported()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		_ = bus.ReadLong(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentFastPathRejectedUnsupported >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathVerificationMismatches);
	}

	[Fact]
	public void CopperQuiescentFastPathDoesNotSkipAfterBlitterSetupInvalidatesWindow()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: false,
			enableCopperQuiescentFastPath: true);
		StartLongBlit(bus);
		bus.EnableLiveAgnusDma();
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CopperQuiescentFastPathRejectedInvalidated >= 1);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathSkippedDrains);
		Assert.Equal(0, scheduler.CopperQuiescentFastPathVerificationMismatches);
	}

	[Fact]
	public void CpuBatchWakeCandidateIgnoresDisabledPaulaAudioDmaEvent()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
		bus.Paula.ScheduleWrite(0, 0x0A0, 0x0000);
		bus.Paula.ScheduleWrite(0, 0x0A2, 0x1000);
		bus.Paula.ScheduleWrite(0, 0x0A4, 0x0001);
		bus.Paula.ScheduleWrite(0, 0x0A6, 0x0001);
		bus.Paula.ScheduleWrite(0, 0x096, 0x8201);
		bus.Paula.AdvanceTo(0);

		var internalCandidate = bus.Paula.GetNextWakeCandidateCycle(0, 100);
		var cpuCandidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 100, out var wakeSource);

		Assert.Equal(32, internalCandidate);
		Assert.Equal(100, cpuCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, wakeSource);
	}

	[Fact]
	public void CpuBatchWakeCandidateStopsAtCopperWaitWake()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		StartCopperListAtFrameStart(
			bus,
			(0x0201, 0xFFFE),
			(0x0180, 0x0F00),
			(0xFFFF, 0xFFFE));
		var currentCycle = AmigaConstants.A500PalCpuCyclesPerRasterLine;
		bus.AdvanceDmaTo(currentCycle);

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(
			currentCycle,
			3 * AmigaConstants.A500PalCpuCyclesPerRasterLine,
			out var wakeSource);

		Assert.InRange(
			candidate,
			currentCycle + 1,
			3 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		Assert.Equal(M68kTraceBatchWakeSource.Copper, wakeSource);
	}

	[Fact]
	public void CpuBatchWakeCandidateStopsAtCopperSkipDecision()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		StartCopperListAtFrameStart(
			bus,
			(0x0001, 0x0001),
			(0x0180, 0x0F00),
			(0xFFFF, 0xFFFE));

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 100, out var wakeSource);

		Assert.InRange(candidate, 1, 100);
		Assert.Equal(M68kTraceBatchWakeSource.Copper, wakeSource);
	}

	[Fact]
	public void CpuBatchWakeCandidateStopsAtNextCopperInstructionWithoutScanningForHazardMove()
	{
		var immediateHazard = new AmigaBus(enableLiveAgnusDma: true);
		StartCopperListAtFrameStart(
			immediateHazard,
			(0x009C, 0x8004),
			(0xFFFF, 0xFFFE));
		var immediateCandidate = immediateHazard.GetNextCpuBatchWakeCandidateCycle(0, 100, out var immediateWakeSource);

		var delayedHazard = new AmigaBus(enableLiveAgnusDma: true);
		StartCopperListAtFrameStart(
			delayedHazard,
			(0x0180, 0x0F00),
			(0x009C, 0x8004),
			(0xFFFF, 0xFFFE));
		var delayedCandidate = delayedHazard.GetNextCpuBatchWakeCandidateCycle(0, 100, out var delayedWakeSource);

		Assert.True(immediateCandidate > 0);
		Assert.Equal(immediateCandidate, delayedCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.Copper, immediateWakeSource);
		Assert.Equal(M68kTraceBatchWakeSource.Copper, delayedWakeSource);
	}

	[Fact]
	public void DeferredCpuBusBatchDisabledExecutesSingleRomInstruction()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(64));
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(romBase + 2, cpu.State.ProgramCounter);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuBusBatchAttempts);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchUsed);
	}

	[Fact]
	public void DeferredCpuBusBatchRunsMultipleRomInstructionsWhenEnabled()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(1024));
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.ProgramCounter > romBase + 2);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchAttempts >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchUsed >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchInstructions > 1);
		Assert.Equal(scheduler.DeferredCpuBusBatchInstructions, scheduler.DeferredCpuBusBatchSkippedInstructionFlushes);
	}

	[Fact]
	public void DeferredCpuBusBatchIgnoresPassiveDiskByteReadiness()
	{
		const uint romBase = 0x00FC0000;
		const long readyCycle = 20;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(1024));
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstruction();

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuBusBatchWakeDisk);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakePassiveByteReady);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakePendingDma);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakeActiveDmaCompletion);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakeUnknown);
	}

	[Fact]
	public void CpuBatchWakeCandidateCachesNoEventTarget()
	{
		var bus = new AmigaBus();
		const long targetCycle = 1000;

		var first = bus.GetNextCpuBatchWakeCandidateCycle(0, targetCycle, 0, out var firstWakeSource);
		var second = bus.GetNextCpuBatchWakeCandidateCycle(10, targetCycle, 0, out var secondWakeSource);

		Assert.Equal(targetCycle, first);
		Assert.Equal(targetCycle, second);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, firstWakeSource);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, secondWakeSource);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CpuVisibleNoEventCacheHits >= 1);
		Assert.True(scheduler.CpuVisibleNoEventCacheMisses >= 1);
	}

	[Fact]
	public void CpuBatchNoEventCacheDoesNotHidePendingInterrupt()
	{
		var bus = new AmigaBus();
		const long targetCycle = 1000;
		_ = bus.GetNextCpuBatchWakeCandidateCycle(0, targetCycle, 0, out _);

		bus.AbleCiaInterrupts(AmigaCiaId.A, 0x81, 0);
		bus.SetCiaInterrupts(AmigaCiaId.A, 0x81, 0);
		var wake = bus.GetNextCpuBatchWakeCandidateCycle(100, targetCycle, 0, out var wakeSource);

		Assert.Equal(101, wake);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, wakeSource);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.CpuVisibleNoEventCacheInvalidations >= 1);
	}

	[Fact]
	public void DeferredCpuBusBatchDoesNotAttemptChipFetchedCode()
	{
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x4E71);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x4E71);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuBusBatchAttempts);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchUsed);
	}

	[Fact]
	public void DeferredCpuBusBatchExitsBeforeChipFetchedInstruction()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x4E, 0xF9, 0x00, 0x00, 0x10, 0x00,
			0x4E, 0x71
		});
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(1, scheduler.DeferredCpuBusBatchExitPcLeftFastWindow);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchExitChipVisibleAccess);
	}

	[Fact]
	public void DeferredCpuBusBatchFlushesForChipDataAccess()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		});
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		cpu.State.A[0] = 0x1000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFFu);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchExitChipVisibleAccess >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchInstructions >= 1);
	}

	[Fact]
	public void DeferredCpuBusBatchKeepsBatchForSideEffectFreeCiaRead()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		var rom = CreateNopRom(1024);
		rom[0] = 0x10;
		rom[1] = 0x39;
		rom[2] = 0x00;
		rom[3] = 0xBF;
		rom[4] = 0xE2;
		rom[5] = 0x01; // MOVE.B $BFE201.L,D0 (CIAA DDRA)
		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x03u, cpu.State.D[0] & 0xFFu);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchInstructions > 1);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchExitChipVisibleAccess);
	}

	[Fact]
	public void DeferredCpuBusBatchFlushesForSideEffectfulCiaRead()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x10, 0x39, 0x00, 0xBF, 0xED, 0x01,
			0x4E, 0x71
		}); // MOVE.B $BFED01.L,D0 (CIAA ICR), then NOP
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstruction();

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchExitChipVisibleAccess >= 1);
	}

	[Fact]
	public void DeferredCpuBusBatchChipsetCatchupSkipsDrainForChipRamWordRead()
	{
		const uint romBase = 0x00FC0000;
		var baseline = new AmigaBus(captureBusAccesses: false);
		var batched = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		var program = new byte[]
		{
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		};
		baseline.MapReadOnlyMemory(romBase, program);
		batched.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x1234);
		BigEndian.WriteUInt16(batched.ChipRam, 0x1000, 0x1234);
		var baselineCpu = M68kCoreFactory.CreateM68000Core(baseline, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(batched, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(romBase, 0x3000);
		batchedCpu.Reset(romBase, 0x3000);
		baselineCpu.State.A[0] = 0x1000;
		batchedCpu.State.A[0] = 0x1000;

		baselineCpu.ExecuteInstruction();
		baselineCpu.ExecuteInstruction();
		batchedCpu.ExecuteInstruction();

		Assert.Equal(baselineCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.D[0], batchedCpu.State.D[0]);
		Assert.Equal(baselineCpu.State.Cycles, batchedCpu.State.Cycles);
		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchChipsetCatchupAttempts >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchChipsetCatchupUsed >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchChipsetCatchupSkippedDrains >= 1);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchChipsetCatchupVerificationMismatches);
	}

	[Fact]
	public void DeferredCpuBusBatchChipsetCatchupRejectsLongAccess()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x20, 0x10,
			0x4E, 0x71
		});
		BigEndian.WriteUInt32(bus.ChipRam, 0x1000, 0x12345678);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		cpu.State.A[0] = 0x1000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x12345678u, cpu.State.D[0]);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchChipsetCatchupAttempts >= 1);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchChipsetCatchupUsed);
		Assert.True(scheduler.DeferredCpuBusBatchChipsetCatchupRejectedUnsupported >= 1);
	}

	[Fact]
	public void DeferredCpuBusBatchChipsetCatchupRejectsActiveBlitter()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		});
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		StartLongBlit(bus);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		cpu.State.A[0] = 0x1000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFFu);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchChipsetCatchupAttempts >= 1);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchChipsetCatchupUsed);
		Assert.True(scheduler.DeferredCpuBusBatchChipsetCatchupRejectedDynamicDma >= 1);
	}

	[Fact]
	public void CpuBoundaryHardwareAdvanceDefersDisabledPaulaAudioDmaEvent()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x7F81);
		bus.Paula.ScheduleWrite(0, 0x0A0, 0x0000);
		bus.Paula.ScheduleWrite(0, 0x0A2, 0x1000);
		bus.Paula.ScheduleWrite(0, 0x0A4, 0x0001);
		bus.Paula.ScheduleWrite(0, 0x0A6, 0x0001);
		bus.Paula.ScheduleWrite(0, 0x096, 0x8201);
		bus.Paula.AdvanceTo(0);

		bus.AdvanceHardwareEventsTo(100, cpuInterruptMask: 0);
		var deferredCandidate = bus.Paula.GetNextWakeCandidateCycle(0, 100);
		bus.AdvanceDmaTo(100);

		Assert.Equal(32, deferredCandidate);
		Assert.Null(bus.Paula.GetNextWakeCandidateCycle(0, 100));
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

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 100, out var wakeSource);

		Assert.Equal(32, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.Paula, wakeSource);
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

	private static void ReserveBlitterWordSlot(AgnusHrmSlotEngine engine, long cycle)
	{
		var request = new AmigaBusAccessRequest(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			AmigaBusAccessTarget.ChipRam,
			0x3000,
			AmigaBusAccessSize.Word,
			cycle,
			isWrite: false);
		_ = engine.Arbitrate(request, new AmigaBusAccessResult(request, cycle, cycle));
	}

	private static long OutputRowStartCycle(int row)
	{
		var line = (0x2C - AmigaConstants.PalLowResOverscanBorderY) + row;
		return (long)line * AmigaConstants.A500PalCpuCyclesPerRasterLine;
	}

	private static long RowLineStartCycle(long cycle)
		=> cycle - RowCycleOffset(cycle);

	private static int RowCycleOffset(long cycle)
		=> checked((int)(cycle % AmigaConstants.A500PalCpuCyclesPerRasterLine));

	private static long LowResPlane1FetchCycle(int row)
		=> OutputRowStartCycle(row) +
			((0x38 + HrmLowResPlane1FetchSlot) * AgnusChipSlotScheduler.SlotCycles);

	private static ushort ReadLiveBitplaneWord(AmigaBus bus, int row, int plane, int word)
	{
		var field = typeof(OcsDisplay).GetField(
			"_liveBitplaneWords",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(field);
		var words = Assert.IsType<ushort[]>(field.GetValue(bus.Display));
		return words[(row * 6 * 64) + (plane * 64) + word];
	}

	private static void ConfigureLiveOneBitplaneDma(AmigaBus bus)
	{
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
	}

	private static void ConfigureLiveSpriteDma(AmigaBus bus)
	{
		const uint spriteList = 0x3000;
		WriteSpriteDmaBlock(
			bus,
			spriteList,
			AmigaConstants.PalLowResOverscanBorderX,
			AmigaConstants.PalLowResOverscanBorderY,
			1,
			0x8000,
			0x0000);
		SetSpritePointer(bus, sprite: 0, spriteList);
		bus.WriteWord(0x00DFF096, 0x8220);
		bus.EnableLiveAgnusDma();
	}

	private static void WriteSpriteDmaBlock(
		AmigaBus bus,
		uint address,
		int x,
		int y,
		int height,
		ushort dataA,
		ushort dataB)
	{
		var (pos, ctl) = EncodeSpritePosition(x, y, height);
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address), pos);
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address + 2), ctl);
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address + 4), dataA);
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address + 6), dataB);
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address + 8), 0);
		BigEndian.WriteUInt16(bus.ChipRam, checked((int)address + 10), 0);
	}

	private static void SetSpritePointer(AmigaBus bus, int sprite, uint address)
	{
		var register = 0x00DFF120u + (uint)(sprite * 4);
		bus.WriteWord(register, (ushort)(address >> 16));
		bus.WriteWord(register + 2, (ushort)address);
	}

	private static (ushort Pos, ushort Ctl) EncodeSpritePosition(int x, int y, int height)
	{
		var hStart = x + 128 - AmigaConstants.PalLowResOverscanBorderX;
		var vStart = y + (0x2C - AmigaConstants.PalLowResOverscanBorderY);
		var vStop = vStart + height;
		var pos = (ushort)(((vStart & 0xFF) << 8) | ((hStart >> 1) & 0xFF));
		var ctl = (ushort)(((vStop & 0xFF) << 8) |
			(hStart & 0x0001) |
			((vStop & 0x100) != 0 ? 0x0002 : 0) |
			((vStart & 0x100) != 0 ? 0x0004 : 0));
		return (pos, ctl);
	}

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

	private static void StartCopperEndOfFrameWait(AmigaBus bus)
	{
		const uint copperList = 0x2400;
		WriteCopperList(bus, copperList, (0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);
		bus.AdvanceDmaTo(AmigaConstants.A500PalCpuCyclesPerRasterLine);
	}

	private static void StartCopperEndOfFrameWaitAtFrameStart(AmigaBus bus)
	{
		const uint copperList = 0x2400;
		WriteCopperList(bus, copperList, (0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);
		bus.AdvanceDmaTo(0);
	}

	private static void StartCopperListAtFrameStart(
		AmigaBus bus,
		params (ushort First, ushort Second)[] instructions)
	{
		const uint copperList = 0x2400;
		WriteCopperList(bus, copperList, instructions);
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);
		bus.AdvanceDmaTo(0);
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

	private static byte[] CreateNopRom(int bytes)
	{
		var rom = new byte[bytes];
		for (var i = 0; i + 1 < rom.Length; i += 2)
		{
			rom[i] = 0x4E;
			rom[i + 1] = 0x71;
		}

		return rom;
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
