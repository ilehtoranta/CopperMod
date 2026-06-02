using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaBusTimingTests
{
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
		var scheduler = new AgnusChipSlotScheduler();
		var request = CreateChipRequest(AmigaBusRequester.Blitter, requestedCycle: 1);

		var result = scheduler.Arbitrate(request, new AmigaBusAccessResult(request, 1, 1));

		Assert.Equal(2, result.GrantedCycle);
		Assert.Equal(4, result.CompletedCycle);
	}

	[Fact]
	public void AgnusChipSlotSchedulerMakesLowerPriorityRequestsWaitBehindReservedSlots()
	{
		var scheduler = new AgnusChipSlotScheduler();
		var high = CreateChipRequest(AmigaBusRequester.Bitplane, requestedCycle: 10);
		var low = CreateChipRequest(AmigaBusRequester.Cpu, requestedCycle: 10);

		var highResult = scheduler.Arbitrate(high, new AmigaBusAccessResult(high, 10, 10));
		var lowResult = scheduler.Arbitrate(low, new AmigaBusAccessResult(low, 10, 10));

		Assert.Equal(10, highResult.GrantedCycle);
		Assert.Equal(12, lowResult.GrantedCycle);
	}

	[Fact]
	public void FixedDisplayDmaSlotsOverrideLowerPriorityButNotEqualPriorityReservations()
	{
		var scheduler = new AgnusChipSlotScheduler();
		var cpu = CreateChipRequest(AmigaBusRequester.Cpu, requestedCycle: 20);
		var bitplane = CreateChipRequest(AmigaBusRequester.Bitplane, requestedCycle: 20);
		var sprite = CreateChipRequest(AmigaBusRequester.Sprite, requestedCycle: 20);

		var cpuResult = scheduler.Arbitrate(cpu, new AmigaBusAccessResult(cpu, 20, 20));
		Assert.Equal(20, cpuResult.GrantedCycle);

		Assert.True(scheduler.TryReserveFixedDmaSlot(bitplane, out var bitplaneResult));
		Assert.Equal(20, bitplaneResult.GrantedCycle);
		Assert.False(scheduler.TryReserveFixedDmaSlot(sprite, out _));
		Assert.Equal(1, scheduler.DeniedFixedSlotCount);
		Assert.Equal(AgnusChipSlotOwner.Sprite, scheduler.LastDeniedFixedSlot?.Owner);

		var blitter = CreateChipRequest(AmigaBusRequester.Blitter, requestedCycle: 20);
		var blitterResult = scheduler.Arbitrate(blitter, new AmigaBusAccessResult(blitter, 20, 20));
		Assert.Equal(22, blitterResult.GrantedCycle);
	}

	[Fact]
	public void BitplaneFixedSlotCanOverridePreviouslyReservedSpriteSlot()
	{
		var scheduler = new AgnusChipSlotScheduler();
		var sprite = CreateChipRequest(AmigaBusRequester.Sprite, requestedCycle: 20);
		var bitplane = CreateChipRequest(AmigaBusRequester.Bitplane, requestedCycle: 20);

		Assert.True(scheduler.TryReserveFixedDmaSlot(sprite, out var spriteResult));
		Assert.Equal(20, spriteResult.GrantedCycle);
		Assert.True(scheduler.TryReserveFixedDmaSlot(bitplane, out var bitplaneResult));

		Assert.Equal(20, bitplaneResult.GrantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Bitplane, scheduler.LastGrantedSlot?.Owner);
		Assert.False(scheduler.TryReserveFixedDmaSlot(sprite, out _));
	}

	[Fact]
	public void PseudoFastUsesChipSlotSchedulerButRealFastIsZeroWait()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000,
			enableLiveAgnusDma: true,
			agnusTimingMode: AgnusTimingMode.SlotEngine);

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
		Assert.Equal(0, ciaCycle);
		Assert.True(bus.Blitter.CaptureSnapshot().Busy);
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
	public void SlotEngineOwnsPalBeamGridAndFrameRollover()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		bus.EnableLiveAgnusDma();
		var target = AmigaConstants.A500PalCpuCyclesPerFrame +
			(2 * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(226 * AmigaConstants.A500PalCpuCyclesPerColorClock);

		bus.AdvanceDmaTo(target);

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(target, snapshot.CurrentCycle);
		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerFrame, snapshot.FrameStartCycle);
		Assert.Equal(1, snapshot.FrameNumber);
		Assert.Equal(2, snapshot.BeamLine);
		Assert.Equal(226, snapshot.BeamHorizontal);
	}

	[Fact]
	public void SlotEngineCommittedCpuAccessAdvancesBusTimeline()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cycle = (long)AmigaConstants.A500PalCpuCyclesPerRasterLine +
			(10 * AgnusChipSlotScheduler.SlotCycles);

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(cycle, snapshot.CurrentCycle);
		Assert.Equal(1, snapshot.BeamLine);
		Assert.Equal(11, snapshot.BeamHorizontal);
	}

	[Fact]
	public void FastSlotPathReturnsCpuCycleAtCompletionButAppliesWriteAtGrant()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			agnusTimingMode: AgnusTimingMode.SlotEngine);
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
			enableLiveAgnusDma: true,
			agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cycle = 20L;

		bus.WriteLong(0x00001000, 0x12345678, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(20 + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Equal(cycle, bus.Agnus.CaptureSnapshot().CurrentCycle);
		Assert.Equal(0x12345678u, BigEndian.ReadUInt32(bus.ChipRam, 0x1000, "long chip write"));
		Assert.Equal(0x0000, bus.ReadChipWordForPresentation(0x1002, 21));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x1002, 22));
	}

	[Fact]
	public void CpuLongCustomWriteAppliesWordsOnSeparateSlots()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cycle = 20L;

		bus.WriteLong(0x00DFF180, 0x0F00000F, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(20 + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
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
				Assert.Equal(22, write.Cycle);
				Assert.Equal(0x182, write.Address);
				Assert.Equal(0x000F, write.Value);
			});
	}

	[Fact]
	public void TimedCpuBeamRegisterReadSamplesGrantedSlot()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true,
			agnusTimingMode: AgnusTimingMode.SlotEngine);
		var requestedCycle = (5L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(10 * AgnusChipSlotScheduler.SlotCycles);
		var cycle = requestedCycle;

		var vhposr = bus.ReadWord(0x00DFF006, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(5, (vhposr >> 8) & 0x00FF);
		Assert.Equal(10, vhposr & 0x00FF);
		Assert.Equal(requestedCycle + AgnusChipSlotScheduler.SlotCycles, cycle);
	}

	[Fact]
	public void TimedCpuLongBeamReadSamplesSecondWordAtSecondSlot()
	{
		var bus = new AmigaBus(
			enableLiveAgnusDma: true,
			agnusTimingMode: AgnusTimingMode.SlotEngine);
		var requestedCycle = (5L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(10 * AgnusChipSlotScheduler.SlotCycles);
		var cycle = requestedCycle;

		var beam = bus.ReadLong(0x00DFF004, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(5u, (beam >> 8) & 0x01FF);
		Assert.Equal(11u, beam & 0x00FF);
		Assert.Equal(requestedCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
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
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
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
	public void SlotEngineCpuGrantChecksLiveDisplayDmaBeforeCommittingFutureSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
		_ = bus.ReadChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x1002,
			fetchCycle - AgnusChipSlotScheduler.SlotCycles);

		var cycle = fetchCycle - AgnusChipSlotScheduler.SlotCycles;
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
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().DeniedFixedSlotCount);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().CpuDeniedFixedSlotBlockerCount);
	}

	[Fact]
	public void SlotEngineCpuGrantChecksLiveSpriteDmaBeforeCommittingFutureSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var fetchCycle = OutputRowStartCycle(0) + (0x14 * AgnusChipSlotScheduler.SlotCycles);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x2C40);
		bus.WriteWord(0x00DFF096, 0x8220);
		bus.WriteWord(0x00DFF120, 0x0000);
		bus.WriteWord(0x00DFF122, 0x1000);
		bus.EnableLiveAgnusDma();

		var cycle = fetchCycle;
		_ = bus.ReadWord(0x00002000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var sprite = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Sprite &&
				access.Request.Kind == AmigaBusAccessKind.Sprite &&
				access.Request.RequestedCycle == fetchCycle);
		Assert.Equal(fetchCycle, sprite.GrantedCycle);
		var cpu = bus.BusAccesses.Last(access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Target == AmigaBusAccessTarget.ChipRam);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, cpu.GrantedCycle);
		Assert.Equal(fetchCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(0, snapshot.DeniedFixedSlotCount);
		Assert.Equal(0, snapshot.SpriteDeniedFixedSlotCount);
		Assert.Equal(0, snapshot.CpuDeniedFixedSlotBlockerCount);
	}

	[Fact]
	public void SlotEngineLiveAdvanceCapturesKnownSpriteDmaSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var fetchCycle = OutputRowStartCycle(0) + (0x14 * AgnusChipSlotScheduler.SlotCycles);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x2C40);
		bus.WriteWord(0x00DFF096, 0x8220);
		bus.WriteWord(0x00DFF120, 0x0000);
		bus.WriteWord(0x00DFF122, 0x1000);
		bus.EnableLiveAgnusDma();

		bus.AdvanceDmaTo(fetchCycle);

		var sprite = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Sprite &&
				access.Request.Kind == AmigaBusAccessKind.Sprite &&
				access.Request.RequestedCycle == fetchCycle);
		Assert.Equal(fetchCycle, sprite.GrantedCycle);
		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(1, snapshot.SpriteSlotGrantCount);
		Assert.Equal(0, snapshot.DeniedFixedSlotCount);
	}

	[Fact]
	public void SlotEngineLiveCapturedFrameReusesCapturedSpriteControlWords()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var frame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var posCycle = OutputRowStartCycle(0) + (0x14 * AgnusChipSlotScheduler.SlotCycles);
		var ctlCycle = OutputRowStartCycle(0) + (0x16 * AgnusChipSlotScheduler.SlotCycles);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x2C40);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x0000);
		bus.WriteWord(0x00DFF096, 0x8220);
		bus.WriteWord(0x00DFF120, 0x0000);
		bus.WriteWord(0x00DFF122, 0x1000);
		bus.EnableLiveAgnusDma();

		bus.Display.RenderFrame(frame, 0, AmigaConstants.A500PalCpuCyclesPerFrame);

		var spriteAccesses = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Sprite)
			.ToArray();
		Assert.True(
			spriteAccesses.Length == 2,
			string.Join(
				";",
				spriteAccesses.Select(access =>
					$"{access.Request.Address:X6}@{access.Request.RequestedCycle}->{access.GrantedCycle}")));
		Assert.Contains(spriteAccesses, access => access.Request.RequestedCycle == posCycle);
		Assert.Contains(spriteAccesses, access => access.Request.RequestedCycle == ctlCycle);
		Assert.Equal(2, bus.Agnus.CaptureSnapshot().SpriteSlotGrantCount);
	}

	[Fact]
	public void SlotEngineCopperGrantChecksLiveDisplayDmaBeforeCommittingFutureSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x8000);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
		bus.AdvanceDmaTo(fetchCycle - AgnusChipSlotScheduler.SlotCycles);
		_ = bus.ReadChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x1002,
			fetchCycle - AgnusChipSlotScheduler.SlotCycles);

		var copperWord = bus.ReadLiveCopperDmaWord(
			0x2400,
			fetchCycle - AgnusChipSlotScheduler.SlotCycles,
			out var copper);

		Assert.Equal(0x1234, copperWord);
		var bitplane = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.Request.RequestedCycle == fetchCycle);
		Assert.Equal(fetchCycle, bitplane.GrantedCycle);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, copper.GrantedCycle);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().DeniedFixedSlotCount);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().CopperDeniedFixedSlotBlockerCount);
	}

	[Fact]
	public void SlotEngineCopperCustomSideEffectChecksLiveDisplayDmaBeforeCommittingFutureSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
		bus.AdvanceDmaTo(fetchCycle - AgnusChipSlotScheduler.SlotCycles);

		bus.WriteDeviceWord(
			AmigaBusRequester.Copper,
			AmigaBusAccessKind.Copper,
			0x00DFF09C,
			(ushort)(0x8000 | AmigaConstants.IntreqCopper),
			fetchCycle);
		bus.AdvanceDmaTo(fetchCycle);

		var bitplane = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.Request.RequestedCycle == fetchCycle);
		var copperWrite = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Copper &&
				access.Request.Target == AmigaBusAccessTarget.CustomRegisters &&
				access.Request.RequestedCycle == fetchCycle);
		Assert.Equal(fetchCycle, bitplane.GrantedCycle);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, copperWrite.GrantedCycle);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().DeniedFixedSlotCount);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().CopperDeniedFixedSlotBlockerCount);
	}

	[Theory]
	[InlineData((int)AmigaBusRequester.Blitter, (int)AmigaBusAccessKind.Blitter)]
	[InlineData((int)AmigaBusRequester.Paula, (int)AmigaBusAccessKind.PaulaDma)]
	[InlineData((int)AmigaBusRequester.Disk, (int)AmigaBusAccessKind.DiskDma)]
	public void SlotEngineDeviceGrantChecksLiveDisplayDmaBeforeCommittingFutureSlot(
		int requesterValue,
		int kindValue)
	{
		var requester = (AmigaBusRequester)requesterValue;
		var kind = (AmigaBusAccessKind)kindValue;
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();
		bus.AdvanceDmaTo(fetchCycle - AgnusChipSlotScheduler.SlotCycles);

		var device = RequestDeviceWordAccess(bus, requester, kind, 0x1002, fetchCycle);
		bus.AdvanceDmaTo(fetchCycle);

		var bitplane = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.Request.RequestedCycle == fetchCycle);
		Assert.Equal(fetchCycle, bitplane.GrantedCycle);
		Assert.Equal(fetchCycle + AgnusChipSlotScheduler.SlotCycles, device.GrantedCycle);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().DeniedFixedSlotCount);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
	}

	[Fact]
	public void LiveDisplayDmaStallsPseudoFastButNotRealFastRomOrCiaAccesses()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000);
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
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
		Assert.Equal(fetchCycle, ciaCycle);
	}

	[Fact]
	public void LiveDisplayDmaSlotStallsCpuCustomRegisterAccess()
	{
		var bus = new AmigaBus();
		var fetchCycle = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) +
			(0x38 * AgnusChipSlotScheduler.SlotCycles);
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
		bus.RegisterHostCallback(0x00F00000, _ => { });

		var cycle = 10L;
		_ = bus.ReadByte(0x00001000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
		cycle = 20;
		bus.WriteWord(0x00DFF096, 0x800F, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		cycle = 30;
		bus.WriteByte(0x00BFE001, 0x00, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		cycle = 40;
		_ = bus.ReadByte(0x00FC0000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var state = new M68kCpuState { Cycles = 50 };
		Assert.True(bus.TryInvokeHost(0x00F00000, state));

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
			access.Request.Kind == AmigaBusAccessKind.HostTrap &&
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
	public void StaleChipSlotRingEntriesDoNotReserveLaterFrames()
	{
		var scheduler = new AgnusChipSlotScheduler();
		var firstCycle = 20L;
		var laterCycle = firstCycle + (2L * AmigaConstants.A500PalCpuCyclesPerFrame);
		var bitplane = CreateChipRequest(AmigaBusRequester.Bitplane, firstCycle);
		var cpu = CreateChipRequest(AmigaBusRequester.Cpu, laterCycle);

		Assert.True(scheduler.TryReserveFixedDmaSlot(bitplane, out var bitplaneResult));
		var cpuResult = scheduler.Arbitrate(cpu, new AmigaBusAccessResult(cpu, laterCycle, laterCycle));

		Assert.Equal(firstCycle, bitplaneResult.GrantedCycle);
		Assert.Equal(laterCycle, cpuResult.GrantedCycle);
	}

	[Theory]
	[InlineData((int)AmigaBusRequester.Bitplane, (int)AgnusChipSlotOwner.Bitplane)]
	[InlineData((int)AmigaBusRequester.Sprite, (int)AgnusChipSlotOwner.Sprite)]
	[InlineData((int)AmigaBusRequester.Copper, (int)AgnusChipSlotOwner.Copper)]
	public void SlotEngineMakesCpuChipAccessWaitBehindCommittedDmaSlot(
		int requesterValue,
		int expectedOwnerValue)
	{
		var requester = (AmigaBusRequester)requesterValue;
		var expectedOwner = (AgnusChipSlotOwner)expectedOwnerValue;
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var requestCycle = 20L;

		Assert.True(bus.TryReserveDisplayDmaSlot(
			requester,
			CreateDmaAccessKind(requester),
			0x1000,
			requestCycle,
			out var dma));
		var dmaSnapshot = bus.Agnus.CaptureSnapshot();

		var cycle = requestCycle;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(requestCycle, dma.GrantedCycle);
		Assert.Equal(requestCycle + AgnusChipSlotScheduler.SlotCycles, bus.BusAccesses.Last().GrantedCycle);
		Assert.Equal(requestCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Equal(expectedOwner, dmaSnapshot.LastGrantedSlot?.Owner);
	}

	[Theory]
	[InlineData((int)AmigaBusRequester.Blitter, (int)AmigaBusAccessKind.Blitter, (int)AgnusChipSlotOwner.Blitter)]
	[InlineData((int)AmigaBusRequester.Paula, (int)AmigaBusAccessKind.PaulaDma, (int)AgnusChipSlotOwner.Paula)]
	[InlineData((int)AmigaBusRequester.Disk, (int)AmigaBusAccessKind.DiskDma, (int)AgnusChipSlotOwner.Disk)]
	public void SlotEngineMakesCpuChipAccessWaitBehindCommittedDeviceDmaSlot(
		int requesterValue,
		int kindValue,
		int expectedOwnerValue)
	{
		var requester = (AmigaBusRequester)requesterValue;
		var kind = (AmigaBusAccessKind)kindValue;
		var expectedOwner = (AgnusChipSlotOwner)expectedOwnerValue;
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var requestCycle = 20L;

		var device = RequestDeviceWordAccess(bus, requester, kind, 0x1000, requestCycle);
		var deviceSnapshot = bus.Agnus.CaptureSnapshot();

		var cycle = requestCycle;
		_ = bus.ReadWord(0x00001002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(requestCycle, device.GrantedCycle);
		Assert.Equal(requestCycle + AgnusChipSlotScheduler.SlotCycles, cpu.GrantedCycle);
		Assert.Equal(requestCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Equal(expectedOwner, deviceSnapshot.LastGrantedSlot?.Owner);
		Assert.Equal(0, bus.Agnus.CaptureSnapshot().DeniedFixedSlotCount);
	}

	[Fact]
	public void SlotEngineDoesNotRetroactivelyDisplaceCommittedCpuSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cycle = 20L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(20, cpu.GrantedCycle);
		Assert.False(bus.TryReserveDisplayDmaSlot(
			AmigaBusRequester.Bitplane,
			AmigaBusAccessKind.Bitplane,
			0x1000,
			20,
			out _));

		Assert.Equal(AgnusChipSlotOwner.Bitplane, bus.Agnus.CaptureSnapshot().LastDeniedFixedSlot?.Owner);
		Assert.Equal(AgnusChipSlotOwner.Cpu, bus.Agnus.CaptureSnapshot().LastDeniedFixedSlotBlocker?.Owner);
		Assert.Equal(1, bus.Agnus.CaptureSnapshot().DeniedFixedSlotCount);
		Assert.Equal(1, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
		Assert.Equal(1, bus.Agnus.CaptureSnapshot().CpuDeniedFixedSlotBlockerCount);
		Assert.Equal(22, cycle);
	}

	[Fact]
	public void SlotEngineLiveBitplaneFetchMissesCommittedCpuSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cycle = 20L;
		bus.ChipRam[0x1000] = 0x12;
		bus.ChipRam[0x1001] = 0x34;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.False(bus.TryReadLiveBitplaneDmaWord(0x1000, 20, out var value, out var grantedCycle));
		Assert.Equal(0, value);
		Assert.Equal(20, grantedCycle);
		Assert.Equal(1, bus.Agnus.CaptureSnapshot().DeniedFixedSlotCount);
		Assert.Equal(1, bus.Agnus.CaptureSnapshot().BitplaneDeniedFixedSlotCount);
		Assert.Equal(AgnusChipSlotOwner.Cpu, bus.Agnus.CaptureSnapshot().LastDeniedFixedSlotBlocker?.Owner);
		Assert.Equal(1, bus.Agnus.CaptureSnapshot().CpuDeniedFixedSlotBlockerCount);
	}

	[Fact]
	public void SlotEngineSnapshotReportsReservationsByOwner()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cycle = 20L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1002, 24);
		_ = bus.WriteChipWordForDeviceWithResult(AmigaBusRequester.Disk, AmigaBusAccessKind.DiskDma, 0x1004, 0xCAFE, 26);
		Assert.True(bus.TryReserveDisplayDmaSlot(
			AmigaBusRequester.Bitplane,
			AmigaBusAccessKind.Bitplane,
			0x1006,
			28,
			out _));
		_ = bus.ReadLiveCopperDmaWord(0x1008, 30, out _);
		_ = bus.ReadPaulaDmaWord(0x100A, 32);

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(6, snapshot.SlotReservationCount);
		Assert.Equal(1, snapshot.CpuSlotReservationCount);
		Assert.Equal(1, snapshot.BlitterSlotReservationCount);
		Assert.Equal(1, snapshot.DiskSlotReservationCount);
		Assert.Equal(1, snapshot.BitplaneSlotReservationCount);
		Assert.Equal(1, snapshot.CopperSlotReservationCount);
		Assert.Equal(1, snapshot.PaulaSlotReservationCount);
		Assert.Equal(0, snapshot.SpriteSlotReservationCount);
		Assert.Equal(6, snapshot.SlotGrantCount);
		Assert.Equal(1, snapshot.CpuSlotGrantCount);
		Assert.Equal(1, snapshot.BlitterSlotGrantCount);
		Assert.Equal(1, snapshot.DiskSlotGrantCount);
		Assert.Equal(1, snapshot.BitplaneSlotGrantCount);
		Assert.Equal(1, snapshot.CopperSlotGrantCount);
		Assert.Equal(1, snapshot.PaulaSlotGrantCount);
		Assert.Equal(0, snapshot.SpriteSlotGrantCount);
	}

	[Fact]
	public void SlotEngineSnapshotKeepsCumulativeGrantCountsAfterRingWindowRolls()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cycle = 20L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		cycle = 20L + (2L * AmigaConstants.A500PalCpuCyclesPerFrame);
		_ = bus.ReadWord(0x00001002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(1, snapshot.SlotReservationCount);
		Assert.Equal(1, snapshot.CpuSlotReservationCount);
		Assert.Equal(2, snapshot.SlotGrantCount);
		Assert.Equal(2, snapshot.CpuSlotGrantCount);
	}

	[Fact]
	public void LiveCopperDmaReadsChipPresentationAtGrantedSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var cpuCycle = 22L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		var beforeWrite = bus.ReadLiveCopperDmaWord(0x2400, 20, out var firstAccess);
		var afterWrite = bus.ReadLiveCopperDmaWord(0x2400, 24, out var secondAccess);

		Assert.Equal(20, firstAccess.GrantedCycle);
		Assert.Equal(24, secondAccess.GrantedCycle);
		Assert.Equal(0x1234, beforeWrite);
		Assert.Equal(0x5678, afterWrite);
	}

	[Fact]
	public void DeviceChipReadSamplesPresentationAtGrantedSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var cpuCycle = 22L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		var beforeWrite = bus.ReadChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x2400,
			20);
		var afterWrite = bus.ReadChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x2400,
			24);

		Assert.Equal(20, beforeWrite.BusAccess.GrantedCycle);
		Assert.Equal(24, afterWrite.BusAccess.GrantedCycle);
		Assert.Equal(0x1234, beforeWrite.Value);
		Assert.Equal(0x5678, afterWrite.Value);
	}

	[Fact]
	public void PaulaDmaReadSamplesPresentationAtGrantedSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var cpuCycle = 22L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		var beforeWrite = bus.ReadPaulaDmaWord(0x2400, 20);
		var afterWrite = bus.ReadPaulaDmaWord(0x2400, 24);

		Assert.Equal(20, beforeWrite.BusAccess.GrantedCycle);
		Assert.Equal(24, afterWrite.BusAccess.GrantedCycle);
		Assert.Equal(0x1234, beforeWrite.Value);
		Assert.Equal(0x5678, afterWrite.Value);
	}

	[Fact]
	public void ChipPresentationHistoryResolvesOutOfOrderWritesByCycle()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var laterCycle = 22L;
		bus.WriteWord(0x00002400, 0x5678, ref laterCycle, AmigaBusAccessKind.CpuDataWrite);
		var earlierCycle = 20L;
		bus.WriteWord(0x00002400, 0x9ABC, ref earlierCycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(0x1234, bus.ReadChipWordForPresentation(0x2400, 19));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, 20));
		Assert.Equal(0x9ABC, bus.ReadChipWordForPresentation(0x2400, 21));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x2400, 22));
		Assert.Equal(0x5678, bus.ReadChipWordForPresentation(0x2400, 24));
	}

	[Fact]
	public void ShadowCompareReportsDivergenceWhenLegacyWouldOverwriteCpuSlot()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.ShadowCompare);
		var cycle = 20L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var before = bus.Agnus.CaptureSnapshot().DivergenceCount;
		Assert.True(bus.TryReserveDisplayDmaSlot(
			AmigaBusRequester.Bitplane,
			AmigaBusAccessKind.Bitplane,
			0x1000,
			20,
			out _));

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.True(snapshot.DivergenceCount > before);
		var divergence = Assert.IsType<AgnusSlotDivergenceSnapshot>(snapshot.LastDivergence);
		Assert.Equal(AmigaBusRequester.Bitplane, divergence.Request.Requester);
		Assert.True(divergence.PrimaryGranted);
		Assert.False(divergence.ShadowGranted);
		Assert.Equal(20, divergence.PrimaryGrantedCycle);
		Assert.Equal(20, divergence.ShadowGrantedCycle);
	}

	[Fact]
	public void ShadowCompareReportsDataDivergenceForSlotPresentedDeviceRead()
	{
		var bus = new AmigaBus(agnusTimingMode: AgnusTimingMode.ShadowCompare);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var cpuCycle = 22L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);
		var before = bus.Agnus.CaptureSnapshot().DivergenceCount;

		var read = bus.ReadChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			0x2400,
			20);

		Assert.Equal(0x5678, read.Value);
		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.True(snapshot.DivergenceCount > before);
		var divergence = Assert.IsType<AgnusSlotDivergenceSnapshot>(snapshot.LastDivergence);
		Assert.Equal(AgnusSlotDivergenceKind.Data, divergence.Kind);
		Assert.True(divergence.HasValueComparison);
		Assert.Equal(0x5678, divergence.PrimaryValue);
		Assert.Equal(0x1234, divergence.ShadowValue);
		Assert.Equal(read.BusAccess.GrantedCycle, divergence.PrimaryGrantedCycle);
		Assert.Equal(read.BusAccess.GrantedCycle, divergence.ShadowGrantedCycle);
		Assert.Equal(AmigaBusRequester.Blitter, divergence.Request.Requester);
	}

	[Fact]
	public void SlotEngineKeepsLongCpuInternalWorkBatchedWhileDeviceDmaConsumesSlots()
	{
		var bus = new AmigaBus(audioDmaMinimumPeriod: 1, agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cpu = new M68kInterpreter(bus);
		Write(bus.ChipRam, 0x1000, 0xC0, 0xD0); // MULU.W (A0),D0
		Write(bus.ChipRam, 0x2000, 0x00, 0x03);
		Write(bus.ChipRam, 0x3000, 0x7F, 0x81, 0x40, 0xC0, 0x20, 0xE0);
		bus.WriteWord(0x00DFF0A0, 0x0000, 0);
		bus.WriteWord(0x00DFF0A2, 0x3000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0003, 0);
		bus.WriteWord(0x00DFF0A6, 0x0008, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);
		bus.Paula.AdvanceTo(20);
		cpu.Reset(0x1000, 0x4000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 7;
		var setupAccessCount = bus.BusAccesses.Count;

		cpu.ExecuteInstruction();
		bus.Paula.AdvanceTo(cpu.State.Cycles);

		Assert.Equal(21u, cpu.State.D[0]);
		Assert.True(cpu.State.Cycles >= 70);
		var cpuAccesses = bus.BusAccesses
			.Skip(setupAccessCount)
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu)
			.ToArray();
		Assert.Equal(2, cpuAccesses.Length);
		Assert.Contains(
			bus.BusAccesses.Skip(setupAccessCount),
			access => access.Request.Kind == AmigaBusAccessKind.PaulaDma &&
				access.Request.RequestedCycle > cpuAccesses[^1].CompletedCycle &&
				access.Request.RequestedCycle < cpu.State.Cycles);
	}

	[Fact]
	public void SlotEngineKeepsLongDivInternalWorkBatchedWhileDeviceDmaConsumesSlots()
	{
		var bus = new AmigaBus(audioDmaMinimumPeriod: 1, agnusTimingMode: AgnusTimingMode.SlotEngine);
		var cpu = new M68kInterpreter(bus);
		Write(bus.ChipRam, 0x1000, 0x80, 0xD0); // DIVU.W (A0),D0
		Write(bus.ChipRam, 0x2000, 0x00, 0x03);
		Write(bus.ChipRam, 0x3000, 0x7F, 0x81, 0x40, 0xC0, 0x20, 0xE0);
		bus.WriteWord(0x00DFF0A0, 0x0000, 0);
		bus.WriteWord(0x00DFF0A2, 0x3000, 0);
		bus.WriteWord(0x00DFF0A4, 0x0003, 0);
		bus.WriteWord(0x00DFF0A6, 0x0008, 0);
		bus.WriteWord(0x00DFF096, 0x8201, 0);
		bus.Paula.AdvanceTo(20);
		cpu.Reset(0x1000, 0x4000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 10;
		var setupAccessCount = bus.BusAccesses.Count;

		cpu.ExecuteInstruction();
		bus.Paula.AdvanceTo(cpu.State.Cycles);

		Assert.Equal(0x0001_0003u, cpu.State.D[0]);
		Assert.True(cpu.State.Cycles >= 140);
		var cpuAccesses = bus.BusAccesses
			.Skip(setupAccessCount)
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu)
			.ToArray();
		Assert.Equal(2, cpuAccesses.Length);
		Assert.Contains(
			bus.BusAccesses.Skip(setupAccessCount),
			access => access.Request.Kind == AmigaBusAccessKind.PaulaDma &&
				access.Request.RequestedCycle > cpuAccesses[^1].CompletedCycle &&
				access.Request.RequestedCycle < cpu.State.Cycles);
	}

	[Fact]
	public void SlotEngineMovemVisibleAccessesInterleaveWithDeviceDmaSlots()
	{
		var bus = new AmigaBus(audioDmaMinimumPeriod: 1, agnusTimingMode: AgnusTimingMode.SlotEngine);
		Write(bus.ChipRam, 0x1000, 0x48, 0xE7, 0xC0, 0xC0); // MOVEM.L D0-D1/A0-A1,-(A7)
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x6000);
		cpu.State.D[0] = 0x1111_2222;
		cpu.State.D[1] = 0x3333_4444;
		cpu.State.A[0] = 0x5555_6666;
		cpu.State.A[1] = 0x7777_8888;
		var setupAccessCount = StartFastPaulaDma(bus);

		cpu.ExecuteInstruction();
		bus.Paula.AdvanceTo(cpu.State.Cycles);

		Assert.Equal(0x5FF0u, cpu.State.A[7]);
		AssertPaulaDmaInterleavedWithCpuBusWindow(bus, setupAccessCount, minCpuAccesses: 6);
	}

	[Fact]
	public void SlotEngineJsrAndRtsStackAccessesInterleaveWithDeviceDmaSlots()
	{
		var bus = new AmigaBus(audioDmaMinimumPeriod: 1, agnusTimingMode: AgnusTimingMode.SlotEngine);
		Write(bus.ChipRam, 0x1000, 0x4E, 0xB9, 0x00, 0x00, 0x10, 0x10); // JSR $00001010
		Write(bus.ChipRam, 0x1010, 0x4E, 0x75); // RTS
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x6000);
		var setupAccessCount = StartFastPaulaDma(bus);

		cpu.ExecuteInstruction();
		bus.Paula.AdvanceTo(cpu.State.Cycles);
		cpu.ExecuteInstruction();
		bus.Paula.AdvanceTo(cpu.State.Cycles);

		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
		Assert.Equal(0x6000u, cpu.State.A[7]);
		Assert.Contains(
			bus.BusAccesses.Skip(setupAccessCount),
			access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
				access.Request.Size == AmigaBusAccessSize.Long);
		Assert.Contains(
			bus.BusAccesses.Skip(setupAccessCount),
			access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
				access.Request.Size == AmigaBusAccessSize.Long);
		AssertPaulaDmaInterleavedWithCpuBusWindow(bus, setupAccessCount, minCpuAccesses: 6);
	}

	[Fact]
	public void SlotEngineExceptionEntryStackAccessesInterleaveWithDeviceDmaSlots()
	{
		var bus = new AmigaBus(audioDmaMinimumPeriod: 1, agnusTimingMode: AgnusTimingMode.SlotEngine);
		Write(bus.ChipRam, 0x1000, 0x4A, 0xFC); // ILLEGAL
		BigEndian.WriteUInt32(bus.ChipRam, 4 * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x6000);
		var setupAccessCount = StartFastPaulaDma(bus);

		cpu.ExecuteInstruction();
		bus.Paula.AdvanceTo(cpu.State.Cycles);

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x5FFAu, cpu.State.A[7]);
		Assert.Contains(
			bus.BusAccesses.Skip(setupAccessCount),
			access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
				access.Request.Size == AmigaBusAccessSize.Long);
		Assert.Contains(
			bus.BusAccesses.Skip(setupAccessCount),
			access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
				access.Request.Size == AmigaBusAccessSize.Word);
		AssertPaulaDmaInterleavedWithCpuBusWindow(bus, setupAccessCount, minCpuAccesses: 4);
	}

	[Fact]
	public void SlotEngineMatchesLegacyForUncontendedChipBusAccesses()
	{
		var legacy = new AmigaBus(agnusTimingMode: AgnusTimingMode.LegacyReservation);
		var slot = new AmigaBus(agnusTimingMode: AgnusTimingMode.SlotEngine);
		Write(legacy.ChipRam, 0x1000, 0x12, 0x34);
		Write(slot.ChipRam, 0x1000, 0x12, 0x34);

		RunUncontendedChipAccessSequence(legacy);
		RunUncontendedChipAccessSequence(slot);

		Assert.Equal(
			BuildAccessSignature(legacy.BusAccesses),
			BuildAccessSignature(slot.BusAccesses));
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesNextVblank()
	{
		var bus = new AmigaBus();
		var frameCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
		var currentCycle = frameCycle - 2;
		bus.AdvanceRasterTo(currentCycle);

		var candidate = bus.GetNextStoppedCpuWakeCandidateCycle(currentCycle, frameCycle + 100);

		Assert.Equal(frameCycle, candidate);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesCiaBTodHsyncAlarm()
	{
		var bus = new AmigaBus();
		var events = new List<AmigaCiaInterruptEvent>();
		bus.CiaB.WriteRegister(0x0F, 0x80, 0, events);
		bus.CiaB.WriteRegister(0x08, 0x01, 0, events);
		bus.CiaB.WriteRegister(0x0D, 0x84, 0, events);

		var candidate = bus.GetNextStoppedCpuWakeCandidateCycle(0, 1000);

		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerRasterLine, candidate);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesNextCiaTimerUnderflow()
	{
		var bus = new AmigaBus();
		var events = new List<AmigaCiaInterruptEvent>();
		bus.CiaA.WriteRegister(0x04, 0x02, 0, events);
		bus.CiaA.WriteRegister(0x05, 0x00, 0, events);
		bus.CiaA.WriteRegister(0x0E, 0x11, 0, events);

		var candidate = bus.GetNextStoppedCpuWakeCandidateCycle(0, 100);

		Assert.Equal(20, candidate);
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

		var candidate = bus.GetNextStoppedCpuWakeCandidateCycle(0, 100);

		Assert.Equal(readyCycle, candidate);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesPaulaPendingWrite()
	{
		var bus = new AmigaBus();
		bus.Paula.ScheduleWrite(20, 0x0A6, 0x0003);

		var candidate = bus.GetNextStoppedCpuWakeCandidateCycle(0, 100);

		Assert.Equal(20, candidate);
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

		Assert.Equal(6, candidate);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIncludesBlitterCompletion()
	{
		var bus = new AmigaBus();
		StartNastyBlit(bus);
		bus.AdvanceDmaTo(100);
		var completionCycle = bus.Blitter.GetPredictedCompletionCycle();

		var candidate = bus.GetNextStoppedCpuWakeCandidateCycle(100, 1000);

		Assert.Equal(completionCycle, candidate);
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

	private static AmigaDiskImage CreateSingleWordDisk(ushort word)
	{
		var data = new byte[2];
		BigEndian.WriteUInt16(data, 0, word);
		var track = AmigaEncodedTrack.FromBytes(data);
		var tracks = new AmigaEncodedTrack[AmigaDiskImage.TrackCount];
		Array.Fill(tracks, track);
		return AmigaDiskImage.FromEncodedTracks(tracks);
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
			access.Request.Target != AmigaBusAccessTarget.CustomRegisters)
		{
			return false;
		}

		var slotCount = access.Request.Size == AmigaBusAccessSize.Long ? 2 : 1;
		for (var slot = 0; slot < slotCount; slot++)
		{
			if (access.GrantedCycle + (slot * AgnusChipSlotScheduler.SlotCycles) == slotCycle)
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
