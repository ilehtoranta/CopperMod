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
	public void AgnusBusDeadlineAgendaUpdatesMinimumWithoutAllocationOrRescan()
	{
		var agenda = new AgnusBusDeadlineAgenda();

		agenda.Set(AgnusBusAgendaSource.Blitter, 40);
		agenda.Set(AgnusBusAgendaSource.Display, 20);
		agenda.Set(AgnusBusAgendaSource.Paula, 30);
		Assert.Equal(20, agenda.NextCycle);

		agenda.Set(AgnusBusAgendaSource.Display, 60);
		Assert.Equal(30, agenda.NextCycle);

		agenda.Set(AgnusBusAgendaSource.Paula, long.MaxValue);
		Assert.Equal(40, agenda.NextCycle);

		agenda.Reset();
		Assert.Equal(long.MaxValue, agenda.NextCycle);
	}

	[Fact]
	public void AgnusRasterlinePlanRingHasThreeLinesAndEightPlaneCapacity()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var plans = bus.CausalBusExecutor.RasterlinePlans;

		Assert.Equal(3, plans.Plans.Length);
		Assert.Equal(3 * 8 * 128, plans.BitplaneEntries.Length);
		Assert.Equal(3 * 8 * 2, plans.SpriteEntries.Length);
		Assert.Equal(3, plans.ExecutedMasks.Length);
		Assert.Equal(3, plans.BitplaneCursorIndices.Length);
		Assert.Equal(3, plans.SpriteCursorIndices.Length);
	}

	[Fact]
	public void AgnusRasterlinePlanOwnsIncrementalSpriteSelection()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var plans = bus.CausalBusExecutor.RasterlinePlans;
		var plan = new RowDmaPlan(
			generation: 1,
			row: 4,
			lineStartCycle: 100,
			dmacon: 0x0220,
			bplcon0: 0x1000,
			dmaPlanVersion: 1,
			signature: 0,
			bitplaneStart: 0,
			bitplaneCount: 0,
			spriteStart: 16,
			spriteCount: 3,
			valid: true);
		plans.SetSpriteEntry(16, new RowDmaSpriteEntry(100, 0, 0));
		plans.SetSpriteEntry(17, new RowDmaSpriteEntry(104, 0, 1));
		plans.SetSpriteEntry(18, new RowDmaSpriteEntry(108, 1, 0));
		plans.Commit(1, in plan);
		Assert.True(plans.TryGetFixedOwnerAt(104, out var owner, out var fixedEntry));
		Assert.Equal(AgnusChipSlotOwner.Sprite, owner);
		Assert.Equal(17, fixedEntry);
		Assert.Equal(AgnusChipSlotOwner.Sprite, bus.CausalBusExecutor.GetPlannedFixedOwnerAt(104, out fixedEntry));
		Assert.Equal(17, fixedEntry);

		Assert.True(plans.TryGetNextSpriteEntry(in plan, 0, 0, out var first));
		Assert.Equal(16, first);
		plans.MarkSpriteEntryConsumed(in plan, first);
		Assert.True(plans.TryGetNextSpriteEntry(in plan, 0, 1, out var second));
		Assert.Equal(17, second);
		plans.MarkSpriteEntryConsumed(in plan, second);
		Assert.True(plans.TryGetNextSpriteEntry(in plan, 1, 0, out var third));
		Assert.Equal(18, third);
		plans.MarkSpriteEntryConsumed(in plan, third);
		Assert.False(plans.TryGetNextSpriteEntry(in plan, 1, 1, out _));
	}

	[Fact]
	public void AgnusRasterlinePlanMaterializesOcsSpriteSlotsWithoutAllocation()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var plans = bus.CausalBusExecutor.RasterlinePlans;
		const long lineStart = 1000;

		var count = plans.MaterializeOcsSpriteEntries(
			ringSlot: 2,
			lineStart,
			AmigaConstants.A500PalCpuCyclesPerColorClock,
			dmaEnabled: true);

		Assert.Equal(16, count);
		var first = plans.SpriteEntries[32];
		var last = plans.SpriteEntries[47];
		Assert.Equal(0, first.SpriteIndex);
		Assert.Equal(0, first.Word);
		Assert.Equal(7, last.SpriteIndex);
		Assert.Equal(1, last.Word);
		Assert.Equal(
			lineStart + ((long)AgnusHrmOcsSlotTable.FirstSpriteHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock),
			first.Cycle);
		Assert.Equal(
			first.Cycle + (30L * AmigaConstants.A500PalCpuCyclesPerColorClock),
			last.Cycle);
		var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
		_ = plans.MaterializeOcsSpriteEntries(
			2,
			lineStart,
			AmigaConstants.A500PalCpuCyclesPerColorClock,
			dmaEnabled: true);
		var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
		Assert.Equal(allocatedBefore, allocatedAfter);
		Assert.Equal(0, plans.MaterializeOcsSpriteEntries(
			0,
			lineStart,
			AmigaConstants.A500PalCpuCyclesPerColorClock,
			dmaEnabled: false));
	}

	[Fact]
	public void AgnusDisplayControlMirrorTracksSetClearAndEightPlanePointers()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		bus.WriteWord(0xDFF096, 0x8380);
		bus.WriteWord(0xDFF096, 0x0080);
		bus.WriteWord(0xDFF0FC, 0x0012);
		bus.WriteWord(0xDFF0FE, 0x3456);
		bus.WriteWord(0xDFF100, 0x7210);
		bus.WriteWord(0xDFF108, 0xFFFC);

		var state = bus.CausalBusExecutor.DisplayControlState;
		Assert.Equal((ushort)0x0300, state.Dmacon);
		Assert.Equal(bus.WriteChipDmaPointerLow(bus.WriteChipDmaPointerHigh(0, 0x0012), 0x3456),
			state.BitplanePointers[7]);
		Assert.Equal((ushort)0x7210, state.Bplcon0);
		Assert.Equal((short)-4, state.Bpl1Mod);
		Assert.Equal(6, state.AppliedWriteCount);
		Assert.Equal(0, state.IgnoredHistoricalWriteCount);
		bus.CausalBusExecutor.CompareDisplayLineControlState(
			state.LastWriteCycle, state.Dmacon, 0, 0, 0, 0, 0, state.Bplcon0, 0, 0, 0, state.Bpl1Mod, 0);
		Assert.Equal(1, state.LineStateMatches);
		bus.CausalBusExecutor.CompareDisplayLineControlState(
			state.LastWriteCycle, state.Dmacon, 0, 0, 0, 0, 0, 0, 0, 0, 0, state.Bpl1Mod, 0);
		Assert.Equal(1, state.LineStateMismatches);
		Assert.Contains("BPLCON0", state.FirstLineStateMismatch);
		var allocatedBeforeRepeatedMismatch = GC.GetAllocatedBytesForCurrentThread();
		bus.CausalBusExecutor.CompareDisplayLineControlState(
			state.LastWriteCycle, state.Dmacon, 0, 0, 0, 0, 0, 0, 0, 0, 0, state.Bpl1Mod, 0);
		Assert.Equal(allocatedBeforeRepeatedMismatch, GC.GetAllocatedBytesForCurrentThread());
		Assert.Equal(2, state.LineStateMismatches);

		bus.CausalBusExecutor.MarkAdvancedThrough(100);
		Assert.Throws<InvalidOperationException>(() =>
			bus.CausalBusExecutor.ObserveDisplayControlWrite(
				AmigaBusRequester.Host, 0x100, 0, 99));
		Assert.Equal((ushort)0x7210, state.Bplcon0);
		Assert.Equal(0, state.IgnoredHistoricalWriteCount);
	}

	[Fact]
	public void AgnusBusExecutorRejectsIntentBehindMonotonicHorizon()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		executor.MarkAdvancedThrough(100);
		var intent = new AgnusBusIntent
		{
			Requester = AmigaBusRequester.Blitter,
			Kind = AmigaBusAccessKind.Blitter,
			Target = AmigaBusAccessTarget.ChipRam,
			Size = AmigaBusAccessSize.Word,
			Flags = AgnusBusIntentFlags.Pending,
			Address = 0x1000,
			EarliestCycle = 98
		};

		Assert.Throws<InvalidOperationException>(
			(Action)(() => executor.SetIntent(AgnusBusAgendaSource.Blitter, intent)));
		Assert.Throws<InvalidOperationException>(
			(Action)(() => executor.ExecuteEligibleAt(
				98,
				useCpuWaitBlitterMicroOps: false,
				processBlitter: true)));
	}

	[Fact]
	public void AgnusBusExecutorNeverReturnsAgendaWorkBehindExecutedHorizon()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		executor.MarkAdvancedThrough(100);

		var next = executor.GetNextSlotContendedCycle(98, 120);

		Assert.True(next == long.MaxValue || next >= 100);
	}

	[Fact]
	public void AgnusBusExecutorExecutesCpuWordsThroughCausalBusPath()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		var executor = bus.CausalBusExecutor;
		var write = new CpuWordRequest(
			0x1000,
			20,
			AmigaBusAccessKind.CpuDataWrite,
			isWrite: true,
			value: 0xA55A);

		var written = executor.ExecuteCpuWord(write);
		var read = executor.ExecuteCpuWord(new CpuWordRequest(
			0x1000,
			written.CompletedCycle,
			AmigaBusAccessKind.CpuDataRead,
			isWrite: false));

		Assert.Equal((ushort)0xA55A, read.Value);
		Assert.True(written.CompletedCycle >= write.RequestedCycle);
		Assert.True(read.CompletedCycle >= written.CompletedCycle);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
			access.Request.Address == 0x1000);
		Assert.Contains(bus.BusAccesses, access =>
			access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
			access.Request.Address == 0x1000);
		Assert.Equal(2, executor.CpuGrantedWords);
		Assert.Equal(0, executor.CpuDeniedWords);
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Cpu).Pending);
		Assert.Equal(0x1000u, executor.LastCpuAddress);
	}

	[Fact]
	public void CpuJournalWriteBeforeDisplayFetchSuppliesNewWord()
	{
		var bus = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(bus);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1357);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		bus.AdvanceDmaTo(OutputRowStartCycle(row));

		Assert.True(bus.CausalBusExecutor.TryEnqueueCpuChipWordWrite(
			0x1000, 0xA55A, fetchCycle - (2 * AgnusChipSlotScheduler.SlotCycles),
			CpuJournalInstructionPhase.Operand, CpuJournalDependencyFlags.None, out _));
		bus.CausalBusExecutor.FlushCpuEventJournal();
		bus.AdvanceDmaTo(fetchCycle);

		Assert.Equal(0xA55A, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
	}

	[Fact]
	public void CpuJournalWriteDeniedByDisplayFetchLeavesFetchedOldWord()
	{
		var bus = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(bus);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1357);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		bus.AdvanceDmaTo(OutputRowStartCycle(row));

		Assert.True(bus.CausalBusExecutor.TryEnqueueCpuChipWordWrite(
			0x1000, 0xA55A, fetchCycle,
			CpuJournalInstructionPhase.Operand, CpuJournalDependencyFlags.None, out _));
		bus.CausalBusExecutor.FlushCpuEventJournal();

		Assert.Equal(0x1357, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
		Assert.Equal(0xA55A, BigEndian.ReadUInt16(bus.ChipRam, 0x1000, "journaled CPU write"));
	}

	[Fact]
	public void CpuJournalLongWordHalvesRemainSeparatedByDisplayFetch()
	{
		var bus = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(bus);
		BigEndian.WriteUInt16(bus.ChipRam, 0x0FFE, 0x1111);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x2222);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		bus.AdvanceDmaTo(OutputRowStartCycle(row));

		Assert.True(bus.CausalBusExecutor.TryEnqueueCpuChipLongWrite(
			0x0FFE, 0xA55A5AA5, fetchCycle - AgnusChipSlotScheduler.SlotCycles,
			CpuJournalInstructionPhase.Operand, out _, out _));
		bus.CausalBusExecutor.FlushCpuEventJournal();

		Assert.Equal(0xA55A, BigEndian.ReadUInt16(bus.ChipRam, 0x0FFE, "first longword half"));
		Assert.Equal(0x2222, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
		Assert.Equal(0x5AA5, BigEndian.ReadUInt16(bus.ChipRam, 0x1000, "second longword half"));
	}

	[Fact]
	public void CpuJournalAndScalarDisplayContentionProduceIdenticalBusTrace()
	{
		var scalar = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var journaled = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(scalar);
		ConfigureLiveOneBitplaneDma(journaled);
		BigEndian.WriteUInt16(scalar.ChipRam, 0x1000, 0x1357);
		BigEndian.WriteUInt16(journaled.ChipRam, 0x1000, 0x1357);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var fetchCycle = LowResPlane1FetchCycle(row);
		scalar.AdvanceDmaTo(lineStart);
		journaled.AdvanceDmaTo(lineStart);

		var scalarCycle = fetchCycle;
		AmigaCpuDataAccess.WriteWord(scalar, 0x1000, 0xA55A, ref scalarCycle);
		AmigaCpuDataAccess.WriteWord(scalar, 0x1002, 0x5AA5, ref scalarCycle);
		Assert.True(journaled.CausalBusExecutor.TryEnqueueCpuChipWordWrite(
			0x1000, 0xA55A, fetchCycle, CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.None, out _));
		Assert.True(journaled.CausalBusExecutor.TryEnqueueCpuChipWordWrite(
			0x1002, 0x5AA5, fetchCycle + AgnusChipSlotScheduler.SlotCycles,
			CpuJournalInstructionPhase.Retirement,
			CpuJournalDependencyFlags.None, out _));
		var journalCompletedCycle = journaled.CausalBusExecutor.FlushCpuEventJournal();

		var scalarTrace = scalar.BusAccesses
			.Where(access => access.GrantedCycle >= lineStart)
			.Select(access => (
				access.Request.Requester,
				access.Request.Kind,
				access.Request.Address,
				access.Request.IsWrite,
				access.GrantedCycle,
				access.CompletedCycle))
			.ToArray();
		var journalTrace = journaled.BusAccesses
			.Where(access => access.GrantedCycle >= lineStart)
			.Select(access => (
				access.Request.Requester,
				access.Request.Kind,
				access.Request.Address,
				access.Request.IsWrite,
				access.GrantedCycle,
				access.CompletedCycle))
			.ToArray();

		Assert.Equal(scalarCycle, journalCompletedCycle);
		Assert.Equal(scalarTrace, journalTrace);
		Assert.Equal(
			ReadLiveBitplaneWord(scalar, row, plane: 0, word: 0),
			ReadLiveBitplaneWord(journaled, row, plane: 0, word: 0));
		Assert.Equal(
			scalar.CausalBusExecutor.DisplayControlState.BitplaneLastFetchAddresses[0],
			journaled.CausalBusExecutor.DisplayControlState.BitplaneLastFetchAddresses[0]);
		Assert.Equal(
			BigEndian.ReadUInt16(scalar.ChipRam, 0x1000, "scalar CPU write"),
			BigEndian.ReadUInt16(journaled.ChipRam, 0x1000, "journaled CPU write"));
		Assert.Equal(
			BigEndian.ReadUInt16(scalar.ChipRam, 0x1002, "second scalar CPU write"),
			BigEndian.ReadUInt16(journaled.ChipRam, 0x1002, "second journaled CPU write"));
		Assert.Equal(scalar.ReadWord(0x00DFF01E), journaled.ReadWord(0x00DFF01E));
	}

	[Fact]
	public void ActiveDestinationBlitterContributesChipRamWriteHazard()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(long.MaxValue - 1));
		var refreshesBefore = bus.CausalBusExecutor.ChipRamWriteHazardRefreshes;
		StartLongBlit(bus);
		var eligibility = bus.Blitter.GetRawBusEligibilityCycle();

		Assert.NotEqual(long.MaxValue, eligibility);
		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(eligibility - 1));
		Assert.True(bus.CausalBusExecutor.MayWriteChipRamBefore(eligibility));
		Assert.True(bus.CausalBusExecutor.ChipRamWriteHazardRefreshes > refreshesBefore);
	}

	[Fact]
	public void SourceOnlyBlitterRemainsReadOnlyForChipRamHazard()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		bus.WriteWord(0x00DFF040, 0x08F0);
		bus.WriteWord(0x00DFF042, 0x0000);
		bus.WriteWord(0x00DFF050, 0x0000);
		bus.WriteWord(0x00DFF052, 0x3000);
		bus.WriteWord(0x00DFF096, 0x8240);
		bus.WriteWord(0x00DFF058, (ushort)((2 << 6) | 2));

		Assert.NotEqual(long.MaxValue, bus.Blitter.GetRawBusEligibilityCycle());
		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(long.MaxValue - 1));
	}

	[Fact]
	public void DiskToMemoryDmaContributesChipRamWriteHazard()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.Paula.ScheduleWrite(0, 0x096, 0x8210);
		bus.Paula.AdvanceTo(0);
		bus.Disk.WriteRegister(0x020, 0x0000, 0);
		bus.Disk.WriteRegister(0x022, 0x1000, 0);
		bus.Disk.WriteRegister(0x024, 0x8002, 0);
		bus.Disk.WriteRegister(0x024, 0x8002, 0);
		var eligibility = bus.Disk.GetRawSlotDmaEligibilityCycle();

		Assert.NotEqual(long.MaxValue, eligibility);
		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(eligibility - 1));
		Assert.True(bus.CausalBusExecutor.MayWriteChipRamBefore(eligibility));
	}

	[Fact]
	public void MemoryToDiskDmaRemainsReadOnlyForChipRamHazard()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		bus.Paula.ScheduleWrite(0, 0x096, 0x8210);
		bus.Paula.AdvanceTo(0);
		bus.Disk.WriteRegister(0x020, 0x0000, 0);
		bus.Disk.WriteRegister(0x022, 0x1000, 0);
		bus.Disk.WriteRegister(0x024, 0xC002, 0);
		bus.Disk.WriteRegister(0x024, 0xC002, 0);

		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(long.MaxValue - 1));
	}

	[Fact]
	public void CpuChipReadsCrossDisplayOnlyDmaAndFollowingWriteFlushesInOrder()
	{
		const uint romBase = 0x00FC0000;
		var program = CreateNopRom(128);
		for (var read = 0; read < 8; read++)
		{
			program[4 + (read * 2)] = 0x30;
			program[5 + (read * 2)] = 0x18; // MOVE.W (A0)+,D0
		}
		new byte[] { 0x33, 0xFC, 0x5A, 0xA5, 0x00, 0x00, 0x10, 0x00 }.CopyTo(program, 20);

		var scalar = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var segmented = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuChipWriteJournal: true,
			enableDeferredCpuChipReadSegments: true);
		ConfigureLiveOneBitplaneDma(scalar);
		ConfigureLiveOneBitplaneDma(segmented);
		scalar.MapReadOnlyMemory(romBase, program);
		segmented.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(scalar.ChipRam, 0x1000, 0xA55A);
		BigEndian.WriteUInt16(segmented.ChipRam, 0x1000, 0xA55A);
		for (var word = 0; word < 8; word++)
		{
			BigEndian.WriteUInt16(scalar.ChipRam, 0x2000 + (word * 2), (ushort)(0x5000 + word));
			BigEndian.WriteUInt16(segmented.ChipRam, 0x2000 + (word * 2), (ushort)(0x5000 + word));
		}

		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var segmentedCpu = M68kCoreFactory.CreateM68000Core(
			segmented, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x4000);
		segmentedCpu.Reset(romBase, 0x4000);
		scalarCpu.State.A[0] = 0x2000;
		segmentedCpu.State.A[0] = 0x2000;
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		scalarCpu.State.Cycles = fetchCycle - 8;
		segmentedCpu.State.Cycles = fetchCycle - 8;

		scalarCpu.ExecuteInstructions(11, null, NoOpBoundary.Instance);
		segmentedCpu.ExecuteInstructions(11, null, NoOpBoundary.Instance);

		Assert.Equal(scalarCpu.State.Cycles, segmentedCpu.State.Cycles);
		Assert.Equal(scalarCpu.State.ProgramCounter, segmentedCpu.State.ProgramCounter);
		Assert.Equal(scalarCpu.State.A[0], segmentedCpu.State.A[0]);
		Assert.Equal(scalarCpu.State.D[0], segmentedCpu.State.D[0]);
		Assert.Equal(
			ReadLiveBitplaneWord(scalar, row, plane: 0, word: 0),
			ReadLiveBitplaneWord(segmented, row, plane: 0, word: 0));
		Assert.Equal(0xA55A, ReadLiveBitplaneWord(segmented, row, plane: 0, word: 0));
		Assert.Equal((ushort)0x5AA5, segmented.ReadWord(0x1000));
		var scheduler = segmented.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchInstructions > 1);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchExitChipVisibleAccess);
	}

	[Fact]
	public void CpuChipReadsCrossPaulaPlaybackWithLatchAndPointerParity()
	{
		const uint romBase = 0x00FC0000;
		var program = CreateNopRom(128);
		for (var read = 0; read < 8; read++)
		{
			program[4 + (read * 2)] = 0x30;
			program[5 + (read * 2)] = 0x18;
		}

		var scalar = new AmigaBus(captureBusAccesses: false);
		var segmented = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuChipReadSegments: true);
		scalar.MapReadOnlyMemory(romBase, program);
		segmented.MapReadOnlyMemory(romBase, program);
		for (var word = 0; word < 8; word++)
		{
			BigEndian.WriteUInt16(scalar.ChipRam, 0x2000 + (word * 2), (ushort)(0x6000 + word));
			BigEndian.WriteUInt16(segmented.ChipRam, 0x2000 + (word * 2), (ushort)(0x6000 + word));
		}
		StartFastPaulaDma(scalar);
		StartFastPaulaDma(segmented);
		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var segmentedCpu = M68kCoreFactory.CreateM68000Core(
			segmented, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x4000);
		segmentedCpu.Reset(romBase, 0x4000);
		scalarCpu.State.A[0] = 0x2000;
		segmentedCpu.State.A[0] = 0x2000;

		scalarCpu.ExecuteInstructions(10, null, NoOpBoundary.Instance);
		segmentedCpu.ExecuteInstructions(10, null, NoOpBoundary.Instance);

		var scalarAudio = scalar.Paula.GetChannelSnapshot(0);
		var segmentedAudio = segmented.Paula.GetChannelSnapshot(0);
		var scalarPipeline = ((IM68000PipelineStateTransfer)scalarCpu).ExportM68000PipelineState();
		var segmentedPipeline = ((IM68000PipelineStateTransfer)segmentedCpu).ExportM68000PipelineState();
		var segmentedScheduler = segmented.CaptureHardwareSchedulerSnapshot();
		Assert.True(
			scalarCpu.State.Cycles == segmentedCpu.State.Cycles,
			$"cycles={scalarCpu.State.Cycles}/{segmentedCpu.State.Cycles}," +
			$"next={scalarPipeline.NextBusTransferCycle}/{segmentedPipeline.NextBusTransferCycle}," +
			$"ready={scalarPipeline.LastBusReadyCycle}/{segmentedPipeline.LastBusReadyCycle}," +
			$"retire={scalarPipeline.RetireBusCycle}/{segmentedPipeline.RetireBusCycle}," +
			$"count={scalarPipeline.PrefetchCount}/{segmentedPipeline.PrefetchCount}," +
			$"pending={scalarPipeline.HasPendingPrefetch}/{segmentedPipeline.HasPendingPrefetch}," +
			$"cpuGrant={scalar.CausalBusExecutor.LastCpuGrantedCycle}/" +
			$"{segmented.CausalBusExecutor.LastCpuGrantedCycle}," +
			$"paulaGrant={scalar.CausalBusExecutor.GetLastPaulaGrantedCycle(0)}/" +
			$"{segmented.CausalBusExecutor.GetLastPaulaGrantedCycle(0)}," +
			$"batch={segmentedScheduler.DeferredCpuBusBatchUsed}/" +
			$"{segmentedScheduler.DeferredCpuBusBatchInstructions}," +
			$"exit={segmentedScheduler.DeferredCpuBusBatchExitTargetCycle}/" +
			$"{segmentedScheduler.DeferredCpuBusBatchExitMaxInstructions}/" +
			$"{segmentedScheduler.DeferredCpuBusBatchExitChipVisibleAccess}");
		Assert.Equal(scalarCpu.State.D[0], segmentedCpu.State.D[0]);
		Assert.Equal(scalarAudio.CurrentAddress, segmentedAudio.CurrentAddress);
		Assert.Equal(scalarAudio.RemainingWords, segmentedAudio.RemainingWords);
		Assert.Equal(scalarAudio.DataWord, segmentedAudio.DataWord);
		Assert.Equal(scalarAudio.DataLatch, segmentedAudio.DataLatch);
		Assert.Equal(
			scalar.CausalBusExecutor.GetLastPaulaAddress(0),
			segmented.CausalBusExecutor.GetLastPaulaAddress(0));
		Assert.Equal(
			scalar.CausalBusExecutor.GetLastPaulaValue(0),
			segmented.CausalBusExecutor.GetLastPaulaValue(0));
		Assert.True(segmentedScheduler.DeferredCpuBusBatchInstructions > 1);
	}

	[Fact]
	public void CpuChipReadsCrossSpriteFetchWithValueAndPointerParity()
	{
		const uint romBase = 0x00FC0000;
		var program = CreateNopRom(128);
		for (var read = 0; read < 8; read++)
		{
			program[4 + (read * 2)] = 0x30;
			program[5 + (read * 2)] = 0x18;
		}

		var scalar = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var segmented = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuChipReadSegments: true);
		ConfigureLiveSpriteDma(scalar);
		ConfigureLiveSpriteDma(segmented);
		scalar.MapReadOnlyMemory(romBase, program);
		segmented.MapReadOnlyMemory(romBase, program);
		for (var word = 0; word < 8; word++)
		{
			BigEndian.WriteUInt16(scalar.ChipRam, 0x2000 + (word * 2), (ushort)(0x7000 + word));
			BigEndian.WriteUInt16(segmented.ChipRam, 0x2000 + (word * 2), (ushort)(0x7000 + word));
		}

		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var segmentedCpu = M68kCoreFactory.CreateM68000Core(
			segmented, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x4000);
		segmentedCpu.Reset(romBase, 0x4000);
		scalarCpu.State.A[0] = 0x2000;
		segmentedCpu.State.A[0] = 0x2000;
		var firstVisibleLine = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var spriteSlot = firstVisibleLine +
			((long)AgnusHrmOcsSlotTable.FirstSpriteHorizontal *
				AmigaConstants.A500PalCpuCyclesPerColorClock);
		scalarCpu.State.Cycles = spriteSlot - 8;
		segmentedCpu.State.Cycles = spriteSlot - 8;

		scalarCpu.ExecuteInstructions(10, null, NoOpBoundary.Instance);
		segmentedCpu.ExecuteInstructions(10, null, NoOpBoundary.Instance);

		var scalarSprite = scalar.CausalBusExecutor.DisplayControlState;
		var segmentedSprite = segmented.CausalBusExecutor.DisplayControlState;
		Assert.Equal(scalarCpu.State.Cycles, segmentedCpu.State.Cycles);
		Assert.Equal(scalarCpu.State.D[0], segmentedCpu.State.D[0]);
		Assert.Equal(scalarCpu.State.A[0], segmentedCpu.State.A[0]);
		Assert.Equal(scalarSprite.SpriteGrantCounts[0], segmentedSprite.SpriteGrantCounts[0]);
		Assert.Equal(scalarSprite.SpriteLastFetchCycles[0], segmentedSprite.SpriteLastFetchCycles[0]);
		Assert.Equal(scalarSprite.SpriteLastFetchAddresses[0], segmentedSprite.SpriteLastFetchAddresses[0]);
		Assert.Equal(scalarSprite.SpriteNextFetchAddresses[0], segmentedSprite.SpriteNextFetchAddresses[0]);
		Assert.True(segmentedSprite.SpriteGrantCounts[0] > 0);
		var scheduler = segmented.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchInstructions > 1);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchExitChipVisibleAccess);
	}

	[Fact]
	public void CpuChipReadSegmentPreservesCopperFetchBeforeSelfModification()
	{
		const uint romBase = 0x00FC0000;
		const uint copperList = 0x2400;
		var program = CreateNopRom(128);
		for (var read = 0; read < 8; read++)
		{
			program[4 + (read * 2)] = 0x30;
			program[5 + (read * 2)] = 0x18;
		}
		new byte[] { 0x33, 0xFC, 0x45, 0x67, 0x00, 0x00, 0x24, 0x02 }.CopyTo(program, 20);

		var scalar = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var segmented = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuChipReadSegments: true);
		foreach (var bus in new[] { scalar, segmented })
		{
			WriteCopperList(bus, copperList, (0x0180, 0x0123), (0xFFFF, 0xFFFE));
			SetCopperPointer(bus, list: 1, copperList);
			bus.WriteWord(0x00DFF096, 0x8280);
			bus.EnableLiveAgnusDma();
			bus.MapReadOnlyMemory(romBase, program);
			for (var word = 0; word < 8; word++)
			{
				BigEndian.WriteUInt16(bus.ChipRam, 0x2000 + (word * 2), (ushort)(0x7100 + word));
			}
		}

		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var segmentedCpu = M68kCoreFactory.CreateM68000Core(
			segmented, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x4000);
		segmentedCpu.Reset(romBase, 0x4000);
		scalarCpu.State.A[0] = 0x2000;
		segmentedCpu.State.A[0] = 0x2000;

		scalarCpu.ExecuteInstructions(11, null, NoOpBoundary.Instance);
		segmentedCpu.ExecuteInstructions(11, null, NoOpBoundary.Instance);

		Assert.Equal(scalarCpu.State.Cycles, segmentedCpu.State.Cycles);
		Assert.Equal((ushort)0x4567, BigEndian.ReadUInt16(
			segmented.ChipRam,
			(int)copperList + 2,
			"self-modified Copper word"));
		Assert.Equal(scalar.CausalBusExecutor.CopperGrantedWords, segmented.CausalBusExecutor.CopperGrantedWords);
		Assert.Equal(scalar.CausalBusExecutor.LastCopperAddress, segmented.CausalBusExecutor.LastCopperAddress);
		Assert.Equal(scalar.CausalBusExecutor.LastCopperValue, segmented.CausalBusExecutor.LastCopperValue);
		Assert.Equal(scalar.CausalBusExecutor.LastCopperMoveValue, segmented.CausalBusExecutor.LastCopperMoveValue);
		Assert.Equal((ushort)0x0123, segmented.CausalBusExecutor.LastCopperMoveValue);
		Assert.True(segmented.CausalBusExecutor.LastCopperGrantedCycle < segmented.CausalBusExecutor.LastCpuGrantedCycle);
	}

	[Fact]
	public void DeferredBitplanePointerWritePatchesOnlyUnexecutedDisplayDmaSuffix()
	{
		const uint romBase = 0x00FC0000;
		var program = CreateNopRom(128);
		new byte[] { 0x33, 0xFC, 0x30, 0x00, 0x00, 0xDF, 0xF0, 0xE2 }.CopyTo(program, 4);
		var scalar = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true);
		var journaled = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuCustomPointerWrites: true);
		ConfigureLiveOneBitplaneDma(scalar);
		ConfigureLiveOneBitplaneDma(journaled);
		scalar.MapReadOnlyMemory(romBase, program);
		journaled.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(scalar.ChipRam, 0x1000, 0x1111);
		BigEndian.WriteUInt16(journaled.ChipRam, 0x1000, 0x1111);
		BigEndian.WriteUInt16(scalar.ChipRam, 0x3000, 0xA55A);
		BigEndian.WriteUInt16(journaled.ChipRam, 0x3000, 0xA55A);
		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var journaledCpu = M68kCoreFactory.CreateM68000Core(
			journaled, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x4000);
		journaledCpu.Reset(romBase, 0x4000);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		scalarCpu.State.Cycles = fetchCycle - 40;
		journaledCpu.State.Cycles = fetchCycle - 40;

		scalarCpu.ExecuteInstructions(3, null, NoOpBoundary.Instance);
		journaledCpu.ExecuteInstructions(3, null, NoOpBoundary.Instance);
		scalar.AdvanceDmaTo(fetchCycle);
		journaled.AdvanceDmaTo(fetchCycle);

		Assert.Equal(scalarCpu.State.Cycles, journaledCpu.State.Cycles);
		Assert.Equal(
			ReadLiveBitplaneWord(scalar, row, plane: 0, word: 0),
			ReadLiveBitplaneWord(journaled, row, plane: 0, word: 0));
		Assert.Equal(0xA55A, ReadLiveBitplaneWord(journaled, row, plane: 0, word: 0));
		Assert.True(journaled.CausalBusExecutor.CpuEventJournal.CommittedEvents > 0);
		Assert.Equal(0, journaled.CausalBusExecutor.CpuEventJournal.Count);
	}

	[Fact]
	public void DeferredBitplanePointerWriteCannotChangeAlreadyFetchedWord()
	{
		const uint romBase = 0x00FC0000;
		var program = CreateNopRom(128);
		new byte[] { 0x33, 0xFC, 0x30, 0x00, 0x00, 0xDF, 0xF0, 0xE2 }.CopyTo(program, 4);
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuCustomPointerWrites: true);
		ConfigureLiveOneBitplaneDma(bus);
		bus.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1357);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x2468);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		bus.AdvanceDmaTo(fetchCycle);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);
		cpu.State.Cycles = fetchCycle + 4;

		cpu.ExecuteInstructions(3, null, NoOpBoundary.Instance);

		Assert.Equal(0x1357, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
		Assert.Equal((ushort)0x3000, bus.CaptureCustomRegisterFileSnapshot().Get(0x0E2).LastWriteValue);
	}

	[Fact]
	public void DeferredSpritePointerWritePatchesOnlyUnexecutedSpriteDmaSuffix()
	{
		const uint romBase = 0x00FC0000;
		var program = CreateNopRom(128);
		new byte[] { 0x33, 0xFC, 0x40, 0x00, 0x00, 0xDF, 0xF1, 0x22 }.CopyTo(program, 4);
		var scalar = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true);
		var journaled = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuCustomPointerWrites: true);
		foreach (var bus in new[] { scalar, journaled })
		{
			ConfigureLiveSpriteDma(bus);
			WriteSpriteDmaBlock(
				bus,
				0x4000,
				AmigaConstants.PalLowResOverscanBorderX,
				AmigaConstants.PalLowResOverscanBorderY,
				1,
				0x4000,
				0x0000);
			bus.MapReadOnlyMemory(romBase, program);
		}
		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var journaledCpu = M68kCoreFactory.CreateM68000Core(
			journaled, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x5000);
		journaledCpu.Reset(romBase, 0x5000);
		var lineStart = OutputRowStartCycle(0);
		var spriteSlot = lineStart +
			((long)AgnusHrmOcsSlotTable.FirstSpriteHorizontal *
				AmigaConstants.A500PalCpuCyclesPerColorClock);
		scalarCpu.State.Cycles = spriteSlot - 40;
		journaledCpu.State.Cycles = spriteSlot - 40;

		scalarCpu.ExecuteInstructions(3, null, NoOpBoundary.Instance);
		journaledCpu.ExecuteInstructions(3, null, NoOpBoundary.Instance);
		scalar.AdvanceDmaTo(spriteSlot);
		journaled.AdvanceDmaTo(spriteSlot);

		Assert.Equal(scalarCpu.State.Cycles, journaledCpu.State.Cycles);
		Assert.Equal(
			scalar.CausalBusExecutor.DisplayControlState.SpriteLastFetchAddresses[0],
			journaled.CausalBusExecutor.DisplayControlState.SpriteLastFetchAddresses[0]);
		Assert.InRange(
			journaled.CausalBusExecutor.DisplayControlState.SpriteLastFetchAddresses[0],
			0x4000u,
			0x4002u);
		Assert.True(journaled.CausalBusExecutor.CpuEventJournal.CommittedEvents > 0);
		Assert.Equal(0, journaled.CausalBusExecutor.CpuEventJournal.Count);
	}

	[Fact]
	public void DeferredPaletteWritesPreserveSameLineCompositionAndGrantCycles()
	{
		const uint romBase = 0x00FC0000;
		var program = CreateNopRom(128);
		new byte[] { 0x33, 0xFC, 0x0F, 0x00, 0x00, 0xDF, 0xF1, 0x80 }.CopyTo(program, 4);
		new byte[] { 0x33, 0xFC, 0x00, 0x00, 0x00, 0xDF, 0xF1, 0x80 }.CopyTo(program, 16);
		var scalar = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true);
		var journaled = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuCustomCompositionWrites: true);
		scalar.MapReadOnlyMemory(romBase, program);
		journaled.MapReadOnlyMemory(romBase, program);
		var scalarFrame = new uint[AmigaConstants.PalLowResWidth * AmigaConstants.PalLowResHeight];
		var journaledFrame = new uint[scalarFrame.Length];
		var frameStop = AmigaConstants.A500PalCpuCyclesPerFrame;
		scalar.Display.BeginPresentationFrame(new PresentationFrameTarget(scalarFrame), 0, frameStop);
		journaled.Display.BeginPresentationFrame(new PresentationFrameTarget(journaledFrame), 0, frameStop);
		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var journaledCpu = M68kCoreFactory.CreateM68000Core(
			journaled, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x4000);
		journaledCpu.Reset(romBase, 0x4000);
		var writeArea = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY) + 100;
		scalarCpu.State.Cycles = writeArea;
		journaledCpu.State.Cycles = writeArea;

		scalarCpu.ExecuteInstructions(7, null, NoOpBoundary.Instance);
		journaledCpu.ExecuteInstructions(7, null, NoOpBoundary.Instance);
		scalar.AdvanceDmaTo(frameStop);
		journaled.AdvanceDmaTo(frameStop);
		scalar.Display.CompletePresentationFrame(frameStop);
		journaled.Display.CompletePresentationFrame(frameStop);
		var diagnosticScalarColors = scalar.CustomRegisterWrites.Where(write => write.Address == 0x180).ToArray();
		var diagnosticJournaledColors = journaled.CustomRegisterWrites.Where(write => write.Address == 0x180).ToArray();

		Assert.True(
			scalarCpu.State.Cycles == journaledCpu.State.Cycles,
			$"cycles={scalarCpu.State.Cycles}/{journaledCpu.State.Cycles}," +
			$"cpuGrant={scalar.CausalBusExecutor.LastCpuGrantedCycle}/" +
			$"{journaled.CausalBusExecutor.LastCpuGrantedCycle}," +
			$"horizon={scalar.CausalBusExecutor.ExecutedThroughCycle}/" +
			$"{journaled.CausalBusExecutor.ExecutedThroughCycle}," +
			$"colors={string.Join(',', diagnosticScalarColors.Select(write => write.Cycle))}/" +
			$"{string.Join(',', diagnosticJournaledColors.Select(write => write.Cycle))}");
		Assert.Equal(scalarFrame, journaledFrame);
		var scalarColors = diagnosticScalarColors;
		var journaledColors = diagnosticJournaledColors;
		Assert.Equal(scalarColors.Select(write => write.Cycle), journaledColors.Select(write => write.Cycle));
		Assert.Equal(scalarColors.Select(write => write.Value), journaledColors.Select(write => write.Value));
		Assert.Equal(2, journaledColors.Length);
		Assert.True(journaled.CausalBusExecutor.CpuEventJournal.CommittedEvents >= 2);
		Assert.Equal(0, journaled.CausalBusExecutor.CpuEventJournal.Count);
		Assert.Equal(0, journaled.CaptureHardwareSchedulerSnapshot().DeferredCpuBusBatchExitChipVisibleAccess);
		Assert.True(scalar.CaptureHardwareSchedulerSnapshot().DeferredCpuBusBatchExitChipVisibleAccess > 0);
	}

	[Fact]
	public void DeniedCpuWordRemainsPendingUntilAgnusGrantsRetry()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		var refreshCycle = (long)AgnusHrmOcsSlotTable.FirstRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;

		Assert.False(executor.TryGrantCpuDataSingleExactSlot(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			refreshCycle,
			refreshCycle,
			isWrite: false,
			allowNiceBlitterSteal: false,
			out _));
		ref readonly var pending = ref executor.GetIntent(AgnusBusAgendaSource.Cpu);
		Assert.True(pending.Pending);
		Assert.Equal(AmigaBusRequester.Cpu, pending.Requester);
		Assert.Equal(0x1000u, pending.Address);
		Assert.True(pending.EarliestCycle > refreshCycle);
		Assert.Equal(1, executor.CpuDeniedWords);
		var retryCycle = pending.EarliestCycle;

		executor.GrantCpuDataSingleSlot(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			retryCycle,
			isWrite: false,
			out var grantedCycle,
			out _);
		Assert.True(grantedCycle >= retryCycle);
		Assert.Equal(1, executor.CpuGrantedWords);
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Cpu).Pending);
	}

	[Fact]
	public void KnownQuietCpuSlotCommitsBeforeRefreshAndRejectsRefreshBoundary()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		var refreshCycle = (long)AgnusHrmOcsSlotTable.LastRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;
		Assert.False(executor.TryGrantCpuDataSingleExactSlot(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			AmigaBusAccessSize.Word,
			refreshCycle,
			refreshCycle,
			isWrite: false,
			allowNiceBlitterSteal: false,
			out _));
		executor.ClearPendingCpuSlotRequest();
		var quietCycle = (AgnusHrmOcsSlotTable.LastRefreshHorizontal + 1L) *
			AgnusChipSlotScheduler.SlotCycles;

		Assert.True(executor.TryCommitCpuDataKnownQuietSlot(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			quietCycle,
			quietCycle,
			isWrite: false,
			out var completedCycle));
		Assert.Equal(quietCycle + AgnusChipSlotScheduler.SlotCycles, completedCycle);
		Assert.Equal(1, executor.CpuGrantedWords);
		Assert.Equal(quietCycle, executor.LastCpuGrantedCycle);

		var nextRefresh = bus.GetNextLineStartCycle(quietCycle);
		Assert.False(executor.TryCommitCpuDataKnownQuietSlot(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			nextRefresh,
			nextRefresh,
			isWrite: false,
			out _));
		Assert.Equal(1, executor.CpuGrantedWords);
	}

	[Fact]
	public void CpuTimingQuietSequenceMatchesScalarExpansionWordGrants()
	{
		const int words = 8;
		var scalarBus = new AmigaBus(captureBusAccesses: false);
		var sequenceBus = new AmigaBus(captureBusAccesses: false);
		var scalar = scalarBus.CausalBusExecutor;
		var sequence = sequenceBus.CausalBusExecutor;
		var firstCycle = (AgnusHrmOcsSlotTable.LastRefreshHorizontal + 1L) *
			AgnusChipSlotScheduler.SlotCycles;
		var scalarCycle = firstCycle;
		var scalarLastGrant = -1L;
		for (var i = 0; i < words; i++)
		{
			scalar.GrantCpuDataSingleSlot(
				AmigaBusAccessKind.CpuDataRead,
				AmigaBusAccessTarget.ExpansionRam,
				AmigaConstants.A500BootPseudoFastRamBase,
				AmigaBusAccessSize.Word,
				scalarCycle,
				isWrite: false,
				out scalarLastGrant,
				out scalarCycle);
		}

		var request = new CpuTimingSequenceRequest(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			firstCycle,
			words,
			isWrite: false);
		Assert.True(sequence.TryExecuteCpuTimingSequence(request, out var result));

		Assert.Equal(words, result.CompletedWords);
		Assert.Equal(scalarLastGrant, result.LastGrantedCycle);
		Assert.Equal(scalarCycle, result.CompletedCycle);
		Assert.Equal(scalar.CpuGrantedWords, sequence.CpuGrantedWords);
		Assert.Equal(1, sequence.CpuTimingSequenceRuns);
		Assert.Equal(words, sequence.CpuTimingSequenceWords);
		Assert.False(sequence.GetIntent(AgnusBusAgendaSource.Cpu).Pending);
		for (var cycle = firstCycle;
			cycle <= scalarLastGrant;
			cycle += AgnusChipSlotScheduler.SlotCycles)
		{
			Assert.True(sequenceBus.TryGetCommittedAgnusSlotOwner(cycle, out var owner));
			Assert.Equal(AgnusChipSlotOwner.Cpu, owner);
		}
	}

	[Fact]
	public void CpuTimingSequenceMatchesScalarAcrossMandatoryRefreshSlots()
	{
		const int words = 16;
		var scalarBus = new AmigaBus(captureBusAccesses: false);
		var sequenceBus = new AmigaBus(captureBusAccesses: false);
		var scalar = scalarBus.CausalBusExecutor;
		var sequence = sequenceBus.CausalBusExecutor;
		var firstCycle = scalarBus.GetNextLineStartCycle(0) - 8;
		var scalarCycle = firstCycle;
		var scalarLastGrant = -1L;
		for (var index = 0; index < words; index++)
		{
			scalar.GrantCpuDataSingleSlot(
				AmigaBusAccessKind.CpuInstructionFetch,
				AmigaBusAccessTarget.ExpansionRam,
				AmigaConstants.A500BootPseudoFastRamBase,
				AmigaBusAccessSize.Word,
				scalarCycle,
				isWrite: false,
				out scalarLastGrant,
				out scalarCycle);
		}

		var request = new CpuTimingSequenceRequest(
			AmigaBusAccessKind.CpuInstructionFetch,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			firstCycle,
			words,
			isWrite: false,
			instructionFetchShapeBits: (1UL << words) - 1);
		Assert.True(sequence.TryExecuteCpuTimingSequence(request, out var result));

		Assert.Equal(scalarLastGrant, result.LastGrantedCycle);
		Assert.Equal(scalarCycle, result.CompletedCycle);
		for (var cycle = firstCycle; cycle <= scalarLastGrant; cycle += AgnusChipSlotScheduler.SlotCycles)
		{
			var scalarCommitted = scalarBus.TryGetCommittedAgnusSlotOwner(cycle, out var scalarOwner);
			var sequenceCommitted = sequenceBus.TryGetCommittedAgnusSlotOwner(cycle, out var sequenceOwner);
			Assert.True(
				scalarCommitted == sequenceCommitted && scalarOwner == sequenceOwner,
				$"cycle={cycle},scalar={scalarCommitted}/{scalarOwner},sequence={sequenceCommitted}/{sequenceOwner}");
		}
	}

	[Fact]
	public void CpuTimingSequenceRejectsRequestBehindExecutedHorizon()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		executor.MarkAdvancedThrough(100);
		var request = new CpuTimingSequenceRequest(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			98,
			1,
			isWrite: false);

		Assert.Throws<InvalidOperationException>(() =>
			executor.TryExecuteCpuTimingSequence(request, out _));
	}

	[Fact]
	public void UnresolvedCpuTimingFenceStopsPhysicalAgnusCommitment()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;

		executor.PublishUnresolvedCpuTiming(10);
		var advancedThrough = executor.AdvanceThrough(100);

		Assert.Equal(9, advancedThrough);
		Assert.Equal(9, executor.ExecutedThroughCycle);
		Assert.Equal(10, executor.UnresolvedCpuTimingFenceCycle);
		Assert.False(bus.TryGetCommittedAgnusSlotOwner(10, out var owner));
		Assert.Equal(AgnusChipSlotOwner.Free, owner);
		Assert.Throws<InvalidOperationException>(() => executor.MarkAdvancedThrough(10));
		Assert.Throws<InvalidOperationException>(() =>
			executor.ExecuteEligibleAt(10, useCpuWaitBlitterMicroOps: false, processBlitter: true));

		executor.ClearUnresolvedCpuTiming();
		Assert.True(executor.AdvanceThrough(100) >= 100);
	}

	[Fact]
	public void CpuValueJournalFenceStopsUntilWriteIsCommitted()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;

		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x100,
			0xCAFE,
			10,
			CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.None,
			out _));
		Assert.Equal(10, executor.UnresolvedCpuEventFenceCycle);
		Assert.Equal(9, executor.AdvanceThrough(100));

		var cpuCycle = 10L;
		executor.FlushCpuEventJournal(ref cpuCycle);

		Assert.Equal(long.MaxValue, executor.UnresolvedCpuEventFenceCycle);
		Assert.True(bus.TryGetCommittedAgnusSlotOwner(10, out var owner));
		Assert.Equal(AgnusChipSlotOwner.Cpu, owner);
		Assert.Equal(0xCAFE, BigEndian.ReadUInt16(bus.ChipRam, 0x100, "journaled CPU write"));
	}

	[Fact]
	public void CpuTimingSequenceCrossingRefreshMatchesScalarGrantCycles()
	{
		const int words = 6;
		var scalarBus = new AmigaBus(captureBusAccesses: false);
		var sequenceBus = new AmigaBus(captureBusAccesses: false);
		var scalar = scalarBus.CausalBusExecutor;
		var sequence = sequenceBus.CausalBusExecutor;
		var nextLine = scalarBus.GetNextLineStartCycle(0);
		var firstCycle = nextLine - (2L * AgnusChipSlotScheduler.SlotCycles);
		var scalarCycle = firstCycle;
		var scalarLastGrant = -1L;
		for (var i = 0; i < words; i++)
		{
			scalar.GrantCpuDataSingleSlot(
				AmigaBusAccessKind.CpuDataRead,
				AmigaBusAccessTarget.ExpansionRam,
				AmigaConstants.A500BootPseudoFastRamBase,
				AmigaBusAccessSize.Word,
				scalarCycle,
				isWrite: false,
				out scalarLastGrant,
				out scalarCycle);
		}

		var request = new CpuTimingSequenceRequest(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			firstCycle,
			words,
			isWrite: false);
		Assert.True(sequence.TryExecuteCpuTimingSequence(request, out var result));

		Assert.Equal(scalarLastGrant, result.LastGrantedCycle);
		Assert.Equal(scalarCycle, result.CompletedCycle);
		Assert.True(sequence.CpuTimingSequenceBarrierRejects > 0);
		Assert.True(sequence.CpuTimingSequenceSlotRejects > 0);
		Assert.True(sequenceBus.TryGetCommittedAgnusSlotOwner(nextLine, out var owner));
		Assert.Equal(AgnusChipSlotOwner.Refresh, owner);
	}

	[Fact]
	public void CpuTimingSequenceCrossingBitplaneSlotMatchesScalarGrantCycles()
	{
		const int words = 6;
		var scalarBus = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var sequenceBus = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(scalarBus);
		ConfigureLiveOneBitplaneDma(sequenceBus);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var fetchCycle = LowResPlane1FetchCycle(row);
		scalarBus.AdvanceDmaTo(lineStart);
		sequenceBus.AdvanceDmaTo(lineStart);
		scalarBus.Display.PrepareLiveDisplaySlotsBeforeHrmGrant(fetchCycle);
		sequenceBus.Display.PrepareLiveDisplaySlotsBeforeHrmGrant(fetchCycle);
		var firstCycle = fetchCycle;
		var scalar = scalarBus.CausalBusExecutor;
		var sequence = sequenceBus.CausalBusExecutor;
		var scalarCycle = firstCycle;
		var scalarLastGrant = -1L;
		for (var i = 0; i < words; i++)
		{
			scalar.GrantCpuDataSingleSlot(
				AmigaBusAccessKind.CpuDataRead,
				AmigaBusAccessTarget.ExpansionRam,
				AmigaConstants.A500BootPseudoFastRamBase,
				AmigaBusAccessSize.Word,
				scalarCycle,
				isWrite: false,
				out scalarLastGrant,
				out scalarCycle);
		}

		var request = new CpuTimingSequenceRequest(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			firstCycle,
			words,
			isWrite: false);
		Assert.True(sequence.TryExecuteCpuTimingSequence(request, out var result));

		Assert.Equal(scalarLastGrant, result.LastGrantedCycle);
		Assert.Equal(scalarCycle, result.CompletedCycle);
		Assert.True(sequence.CpuTimingSequenceSlotRejects > 0);
		Assert.True(sequenceBus.TryGetCommittedAgnusSlotOwner(fetchCycle, out var owner));
		Assert.Equal(AgnusChipSlotOwner.Bitplane, owner);
	}

	[Fact]
	public void CpuTimingSequenceCrossingCopperPairMatchesScalarGrantCycles()
	{
		const int words = 5;
		var scalarBus = new AmigaBus(captureBusAccesses: false);
		var sequenceBus = new AmigaBus(captureBusAccesses: false);
		var copperRequest = 0x20L * AgnusChipSlotScheduler.SlotCycles;
		var scalarCopper = scalarBus.CausalBusExecutor.ReserveCopperDmaSlot(0x1000, copperRequest);
		var sequenceCopper = sequenceBus.CausalBusExecutor.ReserveCopperDmaSlot(0x1000, copperRequest);
		Assert.Equal(scalarCopper.GrantedCycle, sequenceCopper.GrantedCycle);
		var firstCycle = scalarCopper.GrantedCycle;
		var scalar = scalarBus.CausalBusExecutor;
		var sequence = sequenceBus.CausalBusExecutor;
		var scalarCycle = firstCycle;
		var scalarLastGrant = -1L;
		for (var i = 0; i < words; i++)
		{
			scalar.GrantCpuDataSingleSlot(
				AmigaBusAccessKind.CpuDataRead,
				AmigaBusAccessTarget.ExpansionRam,
				AmigaConstants.A500BootPseudoFastRamBase,
				AmigaBusAccessSize.Word,
				scalarCycle,
				isWrite: false,
				out scalarLastGrant,
				out scalarCycle);
		}

		var request = new CpuTimingSequenceRequest(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ExpansionRam,
			AmigaConstants.A500BootPseudoFastRamBase,
			firstCycle,
			words,
			isWrite: false);
		Assert.True(sequence.TryExecuteCpuTimingSequence(request, out var result));

		Assert.Equal(scalarLastGrant, result.LastGrantedCycle);
		Assert.Equal(scalarCycle, result.CompletedCycle);
		Assert.True(sequence.CpuTimingSequenceSlotRejects > 0);
		Assert.True(sequenceBus.TryGetCommittedAgnusSlotOwner(firstCycle, out var owner));
		Assert.Equal(AgnusChipSlotOwner.Copper, owner);
	}

	[Fact]
	public void VposwForcedLongPalFrameWrapsAfterLine313()
	{
		var bus = new AmigaBus();
		var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var longFrameCycles = AmigaConstants.A500PalLongRasterLines * lineCycles;

		bus.WriteWord(0x00DFF02A, 0x8001);

		Assert.Equal(0x138, ReadBeamLineAt(bus, longFrameCycles - 1));
		Assert.Equal(0x000, ReadBeamLineAt(bus, longFrameCycles));
	}

	[Fact]
	public void VposwForcedShortPalFrameWrapsAfterLine312()
	{
		var bus = new AmigaBus();
		var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var shortFrameCycles = AmigaConstants.A500PalShortRasterLines * lineCycles;

		bus.WriteWord(0x00DFF02A, 0x0001);

		Assert.Equal(0x137, ReadBeamLineAt(bus, shortFrameCycles - 1));
		Assert.Equal(0x000, ReadBeamLineAt(bus, shortFrameCycles));
		Assert.Equal(0x000, ReadBeamLineAt(bus, shortFrameCycles * 2L));
	}

	[Fact]
	public void VposrLongFrameBitFollowsVposwFrameLength()
	{
		var bus = new AmigaBus();

		bus.WriteWord(0x00DFF02A, 0x8001);
		Assert.NotEqual(0, ReadVposrAt(bus, 0) & 0x8000);

		bus.WriteWord(0x00DFF02A, 0x0001);
		Assert.Equal(0, ReadVposrAt(bus, 0) & 0x8000);
	}

	[Fact]
	public void VposwForcedShortPalFrameRequestsVerticalBlankAfterLine312()
	{
		var bus = new AmigaBus();
		var shortFrameCycles = AmigaConstants.A500PalShortRasterLines *
			AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF02A, 0x0000);

		bus.AdvanceRasterTo(shortFrameCycles - 1);
		bus.Paula.AdvanceTo(shortFrameCycles - 1);
		Assert.Equal(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);

		bus.AdvanceRasterTo(shortFrameCycles);
		bus.Paula.AdvanceTo(shortFrameCycles);
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqVerticalBlank);
	}

	[Fact]
	public void CiaATodUsesVposwForcedShortPalFrameCadence()
	{
		var bus = new AmigaBus();
		var shortFrameCycles = AmigaConstants.A500PalShortRasterLines *
			AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF02A, 0x0000);
		Assert.Equal(0x00, bus.ReadByte(0x00BFE801));

		bus.AdvanceRasterTo(shortFrameCycles);

		Assert.Equal(0x01, bus.ReadByte(0x00BFE801));
	}

	[Fact]
	public void CiaBTodKeepsHorizontalLineCadenceWhenVposwForcesShortFrames()
	{
		var bus = new AmigaBus();
		var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF02A, 0x0000);
		Assert.Equal(0x00, bus.ReadByte(0x00BFD800));

		bus.AdvanceRasterTo(lineCycles - 1);
		Assert.Equal(0x00, bus.ReadByte(0x00BFD800));

		bus.AdvanceRasterTo(lineCycles);
		Assert.Equal(0x01, bus.ReadByte(0x00BFD800));
	}

	[Fact]
	public void LiveDisplayDmaStartsNextFrameAtVposwForcedShortFrameStop()
	{
		var bus = new AmigaBus(enableLiveDisplayDma: true);
		var shortFrameCycles = AmigaConstants.A500PalShortRasterLines *
			AmigaConstants.A500PalCpuCyclesPerRasterLine;

		bus.WriteWord(0x00DFF02A, 0x0000);
		bus.Display.ScheduleWrite(shortFrameCycles - 2, 0x0180, 0x0123);
		bus.AdvanceDmaTo(shortFrameCycles);

		Assert.Equal(shortFrameCycles, GetPrivateField<long>(bus.Display, "_liveFrameStartCycle"));
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
	public void CpuRequestUsesNextPairedOpportunityAfterCopperCollision()
	{
		var bus = new AmigaBus();
		const long collisionCycle = 20;
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		_ = bus.ReadLiveCopperDmaWord(0x1000, collisionCycle, out var copper);

		var completedCycle = collisionCycle;
		_ = bus.ReadWord(0x00002000, ref completedCycle, AmigaBusAccessKind.CpuInstructionFetch);
		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);

		Assert.Equal(collisionCycle, copper.GrantedCycle);
		// Copper's fixed half of the HRM pair is handled as one paired arbitration
		// opportunity. This does not impose a universal parity rule on retries after
		// other DMA owners.
		Assert.Equal(collisionCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cpu.GrantedCycle);
		Assert.Equal(cpu.GrantedCycle + AgnusChipSlotScheduler.SlotCycles, completedCycle);
	}

	[Fact]
	public void CausalCustomReadSkipsAdjacentCpuHalfAfterCopperCollision()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		const long collisionCycle = 20;
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		_ = bus.ReadLiveCopperDmaWord(0x1000, collisionCycle, out var copper);

		var completedCycle = collisionCycle;
		_ = bus.ReadWord(0x00DFF004, ref completedCycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = bus.BusAccesses.Last(access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);

		Assert.Equal(collisionCycle, copper.GrantedCycle);
		Assert.Equal(collisionCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cpu.GrantedCycle);
		Assert.Equal(cpu.GrantedCycle + AgnusChipSlotScheduler.SlotCycles, completedCycle);
	}

	[Fact]
	public void DmaWordReadSamplesChipRamAtomicallyAtItsGrantedCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const uint address = 0x1000;

		bus.WriteChipDmaWordAtGrantedSlot(address, 0x1111, 0);
		bus.WriteChipDmaWordAtGrantedSlot(address, 0x2222, 19);
		var execution = bus.ExecuteChipWordForDeviceRead(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			address,
			20);

		Assert.True(execution.Granted);
		Assert.True(execution.GrantedCycle >= 20);
		Assert.Equal(0x2222, execution.Value);
	}

	[Fact]
	public void DmaWordWriteCommitsChipRamAtomicallyAtItsGrantedCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const uint address = 0x1000;

		bus.WriteChipDmaWordAtGrantedSlot(address, 0x1111, 0);
		var access = bus.WriteChipWordForDeviceWithResult(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			address,
			0x3333,
			20);

		Assert.Equal(0x3333, bus.ReadChipDmaWordAtGrantedSlot(address, access.GrantedCycle + 1));
	}

	[Fact]
	public void PaulaDmaExecutionRetainsTheValueSampledAtItsGrantedCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const uint address = 0x1000;

		bus.WriteChipDmaWordAtGrantedSlot(address, 0x1111, 0);
		bus.WriteChipDmaWordAtGrantedSlot(address, 0x2222, 19);
		var read = bus.ReadPaulaDmaWord(channel: 0, address: address, requestedCycle: 20);
		bus.WriteChipDmaWordAtGrantedSlot(address, 0x3333, read.BusAccess.GrantedCycle + 1);

		Assert.Equal(0x2222, read.Value);
		Assert.Equal(0x3333, bus.ReadCurrentChipDmaWord(address));
	}

	[Fact]
	public void ChipRamExecutionBehindCommittedBusHorizonIsRejected()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		const uint address = 0x1000;

		bus.WriteChipDmaWordAtGrantedSlot(address, 0x1234, 20);

		var error = Assert.Throws<InvalidOperationException>(
			() => bus.WriteChipDmaWordAtGrantedSlot(address, 0x5678, 18));

		Assert.Contains("executed bus horizon 20", error.Message, StringComparison.Ordinal);
		Assert.Equal(0x1234, bus.ReadWord(address));
	}

	[Fact]
	public void BlitterWordSlotHelperMatchesGenericGrant()
	{
		var genericEngine = new AgnusHrmSlotEngine();
		var fastEngine = new AgnusHrmSlotEngine();
		var readRequest = new AmigaBusAccessRequest(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessSize.Word,
			20,
			isWrite: false);
		var writeRequest = new AmigaBusAccessRequest(
			AmigaBusRequester.Blitter,
			AmigaBusAccessKind.Blitter,
			AmigaBusAccessTarget.ChipRam,
			0x1002,
			AmigaBusAccessSize.Word,
			24,
			isWrite: true);

		var genericRead = genericEngine.Arbitrate(readRequest, new AmigaBusAccessResult(readRequest, 20, 20));
		var fastRead = fastEngine.ReserveBlitterDmaWordSlot(0x1000, 20, isWrite: false);
		var genericWrite = genericEngine.Arbitrate(writeRequest, new AmigaBusAccessResult(writeRequest, 24, 24));
		var fastWrite = fastEngine.ReserveBlitterDmaWordSlot(0x1002, 24, isWrite: true);

		Assert.Equal(genericRead.GrantedCycle, fastRead.GrantedCycle);
		Assert.Equal(genericRead.CompletedCycle, fastRead.CompletedCycle);
		Assert.Equal(genericWrite.GrantedCycle, fastWrite.GrantedCycle);
		Assert.Equal(genericWrite.CompletedCycle, fastWrite.CompletedCycle);
		Assert.Equal(genericEngine.GetSlotGrantCount(AgnusChipSlotOwner.Blitter), fastEngine.GetSlotGrantCount(AgnusChipSlotOwner.Blitter));
		Assert.Equal(AgnusChipSlotOwner.Blitter, fastEngine.LastGrantedSlot?.Owner);
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
		Assert.Equal(22, granted);
	}

	[Fact]
	public void PseudoFastUsesChipSlotSchedulerButRealFastIsZeroWait()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000,
			enableLiveAgnusDma: true);
		bus.ConfigureAutoconfigFastRamForHost();

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
		Assert.Equal(0x1234, bus.ReadCurrentChipDmaWord(0x2400));
		var cycle = 20L;

		bus.WriteWord(0x00002400, 0x9ABC, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(reservation.CompletedCycle, cycle);
		Assert.Equal(0x9ABC, bus.ReadCurrentChipDmaWord(0x2400));
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void CausalCpuLongWriteCompletesAfterSecondWordCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		Write(bus.ChipRam, 0x2400, 0x12, 0x34, 0x56, 0x78);
		Assert.Equal(0x1234, bus.ReadCurrentChipDmaWord(0x2400));
		Assert.Equal(0x5678, bus.ReadCurrentChipDmaWord(0x2402));
		var cycle = 20L;

		bus.WriteLong(0x00002400, 0x9ABCDEF0, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var reservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(reservation.CompletedCycle, cycle);
		Assert.Equal(0x9ABC, bus.ReadCurrentChipDmaWord(0x2400));
		Assert.Equal(0xDEF0, bus.ReadCurrentChipDmaWord(0x2402));
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
		Assert.Equal(readReservation.CompletedCycle + AgnusChipSlotScheduler.SlotCycles, readCycle);
		Assert.Empty(bus.BusAccesses);

		var writeCycle = 40L;
		bus.WriteJitSlotAwareMemory(ref writeCycle, 0x00002400, 0x9ABCDEF0, M68kOperandSize.Long);

		var writeReservation = Assert.NotNull(bus.Agnus.CaptureSnapshot().LastFixedDmaReservation);
		Assert.Equal(AmigaBusAccessKind.CpuDataWrite, writeReservation.Request.Kind);
		Assert.Equal(writeReservation.CompletedCycle + AgnusChipSlotScheduler.SlotCycles, writeCycle);
		Assert.Equal(0x9ABC, bus.ReadCurrentChipDmaWord(0x2400));
		Assert.Equal(0xDEF0, bus.ReadCurrentChipDmaWord(0x2402));
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
	public void ProductionExecutorCommitsCpuWordWhenLegacyExactFastPathIsDisabled()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableHardwareSpecialization: true,
			enableLiveAgnusDma: true);
		Assert.True(bus.CausalBusExecutor.ProductionEnabled);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71);
		bus.CausalBusExecutor.MarkAdvancedThrough(100);
		var cycle = 104L;

		var value = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);

		Assert.Equal(0x4E71, value);
		Assert.True(cycle > 104);
		Assert.Equal(cycle - AgnusChipSlotScheduler.SlotCycles, bus.CausalBusExecutor.LastCpuGrantedCycle);
		Assert.True(bus.CausalBusExecutor.ExecutedThroughCycle >= bus.CausalBusExecutor.LastCpuGrantedCycle);
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
		bus.ConfigureAutoconfigFastRamForHost();
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
	public void PendingCpuRequestRetriesInSlotImmediatelyAfterBlitterCollision()
	{
		// The HRM phase diagram describes normal bus allocation. Once the CPU request is
		// pending, Agnus retries it in the immediately following slot rather than forcing
		// the eventual grant back onto the request's original phase.
		var bus = new AmigaBus();
		var blitterCycle = 20L;
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1000, blitterCycle);

		var cycle = blitterCycle;
		_ = bus.ReadWord(0x00001002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(blitterCycle + AgnusChipSlotScheduler.SlotCycles, cpu.GrantedCycle);
		Assert.Equal(blitterCycle + (2 * AgnusChipSlotScheduler.SlotCycles), cycle);
	}

	[Fact]
	public void NastyBlitterDoesNotYieldThirdConsecutivePendingCpuRetry()
	{
		var bus = new AmigaBus();
		var slotCycles = AgnusChipSlotScheduler.SlotCycles;
		var firstSlot = 20L;
		bus.WriteWord(0x00DFF096, 0x8640);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1000, firstSlot);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1002, firstSlot + slotCycles);
		_ = bus.ReadChipWordForDeviceWithResult(AmigaBusRequester.Blitter, AmigaBusAccessKind.Blitter, 0x1004, firstSlot + (2 * slotCycles));

		var cycle = firstSlot;
		_ = bus.ReadWord(0x00001006, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var cpu = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(firstSlot + (3 * slotCycles), cpu.GrantedCycle);
		Assert.Equal(firstSlot + (4 * slotCycles), cycle);
	}

	[Fact]
	public void NastyBlitterOwnsEligibleSlotBeforeSimultaneousCpuRequest()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		StartNastyBlit(bus, startCycle: 596);
		var blitterDueCycle = bus.Blitter.GetRawBusEligibilityCycle();
		Assert.False(bus.IsMandatoryRefreshSlot(blitterDueCycle));
		var cycle = blitterDueCycle;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);

		var firstBlitter = bus.BusAccesses.First(access =>
			access.Request.Requester == AmigaBusRequester.Blitter);
		var cpu = bus.BusAccesses.Last(access =>
			access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(blitterDueCycle, firstBlitter.GrantedCycle);
		Assert.True(cpu.GrantedCycle > firstBlitter.GrantedCycle);
	}

	[Fact]
	public void NastyDestinationOnlyBlitterKeepsFourCycleCadenceAcrossPendingCpuFetches()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		const long startCycle = 596;
		bus.WriteWord(0x00DFF040, 0x0100); // D-only clear.
		bus.WriteWord(0x00DFF054, 0x0000);
		bus.WriteWord(0x00DFF056, 0x4000);
		bus.WriteWord(0x00DFF096, 0x8640); // DMAEN, BLTEN and BLTPRI.
		bus.AdvanceDmaTo(100);
		bus.WriteWord(0x00DFF058, 0x0048, startCycle); // One row, eight words.
		var firstDueCycle = bus.Blitter.GetRawBusEligibilityCycle();
		Assert.False(bus.IsMandatoryRefreshSlot(firstDueCycle));
		var cycle = firstDueCycle;

		for (var word = 0; word < 4; word++)
		{
			_ = bus.ReadWord(
				(uint)(0x00001000 + (word * 2)),
				ref cycle,
				AmigaBusAccessKind.CpuInstructionFetch);
		}

		var blitterCycles = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Blitter)
			.Select(access => access.GrantedCycle)
			.Take(4)
			.ToArray();
		Assert.InRange(
			blitterCycles[0],
			firstDueCycle,
			firstDueCycle + (2 * AgnusChipSlotScheduler.SlotCycles));
		var firstBlitterCycle = blitterCycles[0];
		Assert.Equal(
			new[]
			{
				firstBlitterCycle,
				firstBlitterCycle + 4,
				firstBlitterCycle + 8,
				firstBlitterCycle + 12
			},
			blitterCycles);
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
	public void CausalCpuLongChipAccessCommitsBothWordSlots()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true);
		var cycle = 20L;

		bus.WriteLong(0x00001000, 0x12345678, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(20 + (3 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Equal(cycle, bus.Agnus.CaptureSnapshot().CurrentCycle);
		Assert.Equal(0x12345678u, BigEndian.ReadUInt32(bus.ChipRam, 0x1000, "long chip write"));
		Assert.Equal(0x5678, bus.ReadCurrentChipDmaWord(0x1002));
	}

	[Fact]
	public void CpuLongCustomWriteAppliesWordsOnSeparateSlots()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true);
		var cycle = 20L;

		bus.WriteLong(0x00DFF180, 0x0F00000F, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(20 + (4 * AgnusChipSlotScheduler.SlotCycles), cycle);
		Assert.Collection(
			bus.CustomRegisterWrites,
			write =>
			{
				Assert.Equal(22, write.Cycle);
				Assert.Equal(0x180, write.Address);
				Assert.Equal(0x0F00, write.Value);
			},
			write =>
			{
				Assert.Equal(26, write.Cycle);
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
		var read = Assert.Single(bus.CustomRegisterReads);

		Assert.Equal(5, (vhposr >> 8) & 0x00FF);
		Assert.Equal(EncodeVhposr(bus.GetBeamPosition(read.GrantedCycle)), vhposr);
		Assert.Equal(read.CompletedCycle, cycle);
		Assert.Equal(read.GrantedCycle + AgnusChipSlotScheduler.SlotCycles, cycle);
	}

	[Fact]
	public void TimedCpuVhposrReadDoesNotExposeRefreshOwnedHorizontalValues()
	{
		var observed = new[]
		{
			ReadTimedVhposrHorizontal(0x00),
			ReadTimedVhposrHorizontal(0x02),
			ReadTimedVhposrHorizontal(0x04),
			ReadTimedVhposrHorizontal(0x06)
		};

		Assert.DoesNotContain(4, observed);
		Assert.DoesNotContain(6, observed);
		Assert.DoesNotContain(8, observed);
		Assert.DoesNotContain(10, observed);
		Assert.Equal(new[] { 5, 7, 9, 11 }, observed);

		static int ReadTimedVhposrHorizontal(int physicalHorizontal)
		{
			var bus = new AmigaBus(enableLiveAgnusDma: true);
			var cycle = (long)physicalHorizontal * AgnusChipSlotScheduler.SlotCycles;
			var vhposr = bus.ReadWord(0x00DFF006, ref cycle, AmigaBusAccessKind.CpuDataRead);
			return vhposr & 0x00FF;
		}
	}

	[Fact]
	public void TimedCpuVhposrHorizontalZeroStillReportsCurrentRasterLine()
	{
		var bus = new AmigaBus();
		var requestedCycle = (5L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(223 * AgnusChipSlotScheduler.SlotCycles);
		var cycle = requestedCycle;

		var vhposr = bus.ReadWord(0x00DFF006, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(5, (vhposr >> 8) & 0x00FF);
		Assert.Equal(0, vhposr & 0x00FF);
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
		Assert.Equal(17u, beam & 0x00FF);
		Assert.Equal(requestedCycle + (4 * AgnusChipSlotScheduler.SlotCycles), cycle);
	}

	[Fact]
	public void TimedCpuBeamRegisterReadsEncodeGrantedBeamPosition()
	{
		var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var longFrameCycles = AmigaConstants.A500PalLongRasterLines * lineCycles;
		var requestedCycles = new[]
		{
			0L,
			1L,
			(5L * lineCycles) + (222L * AgnusChipSlotScheduler.SlotCycles),
			(5L * lineCycles) + (223L * AgnusChipSlotScheduler.SlotCycles),
			(5L * lineCycles) + (226L * AgnusChipSlotScheduler.SlotCycles),
			(312L * lineCycles) + (190L * AgnusChipSlotScheduler.SlotCycles),
			(312L * lineCycles) + (200L * AgnusChipSlotScheduler.SlotCycles),
			(312L * lineCycles) + (210L * AgnusChipSlotScheduler.SlotCycles),
			(312L * lineCycles) + (220L * AgnusChipSlotScheduler.SlotCycles),
			longFrameCycles + (4L * AgnusChipSlotScheduler.SlotCycles),
			longFrameCycles + (14L * AgnusChipSlotScheduler.SlotCycles),
			longFrameCycles + (24L * AgnusChipSlotScheduler.SlotCycles),
			longFrameCycles - 4,
			longFrameCycles - 2,
		};

		foreach (var requestedCycle in requestedCycles)
		{
			var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
			var cycle = requestedCycle;

			var vhposr = bus.ReadWord(0x00DFF006, ref cycle, AmigaBusAccessKind.CpuDataRead);

			var read = Assert.Single(bus.CustomRegisterReads);
			var expected = EncodeVhposr(bus.GetBeamPosition(read.GrantedCycle));
			Assert.Equal(0x006, read.Address);
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(expected, vhposr);
			Assert.Equal(expected, read.Value);
		}
	}

	[Fact]
	public void TimedCpuVposrReadEncodesGrantedFrameLengthAndLineHighBit()
	{
		var lineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var cases = new[]
		{
			(ForcedVposw: (ushort)0x8001, RequestedCycle: 0L),
			(ForcedVposw: (ushort)0x8001, RequestedCycle: 0x100L * lineCycles),
			(ForcedVposw: (ushort)0x0001, RequestedCycle: 0L),
			(ForcedVposw: (ushort)0x0001, RequestedCycle: 0x100L * lineCycles),
		};

		foreach (var testCase in cases)
		{
			var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
			bus.WriteWord(0x00DFF02A, testCase.ForcedVposw);
			var cycle = testCase.RequestedCycle;

			var vposr = bus.ReadWord(0x00DFF004, ref cycle, AmigaBusAccessKind.CpuDataRead);

			var read = Assert.Single(bus.CustomRegisterReads);
			var expected = EncodeVposr(bus.GetBeamPosition(read.GrantedCycle));
			Assert.Equal(0x004, read.Address);
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(expected, vposr);
			Assert.Equal(expected, read.Value);
		}
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
		Assert.Equal(11u, beam & 0x00FF);
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
	public void AgnusDisplayControlMirrorMatchesCapturedLiveLineState()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveOneBitplaneDma(bus);

		bus.AdvanceDmaTo(lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var state = bus.CausalBusExecutor.DisplayControlState;
		Assert.True(state.LineStateMatches > 0);
		Assert.Equal(0, state.LineStateMismatches);
		Assert.Equal(string.Empty, state.FirstLineStateMismatch);
		Assert.True(state.BitplaneGrantCounts[0] > 0);
		Assert.Equal(0, state.BitplanePointerMismatches);
		Assert.True(state.BitplanePointerMatches > 0);
		Assert.Equal(string.Empty, state.FirstBitplanePointerMismatch);
	}

	[Fact]
	public void AgnusDisplayControlMirrorRecordsGrantedSpriteFetchesCausally()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var firstVisibleLine = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveSpriteDma(bus);

		bus.AdvanceDmaTo(firstVisibleLine + AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var state = bus.CausalBusExecutor.DisplayControlState;
		Assert.True(state.SpriteGrantCounts[0] > 0);
		Assert.InRange(state.SpriteLastFetchAddresses[0], 0x3000u, 0x300Au);
		Assert.InRange(state.SpriteLastFetchCycles[0], 0, firstVisibleLine + AmigaConstants.A500PalCpuCyclesPerRasterLine);
		Assert.True(state.SpriteAddressMatches > 0);
		Assert.Equal(0, state.SpriteAddressMismatches);
		Assert.Equal(string.Empty, state.FirstSpriteAddressMismatch);
		Assert.Equal(
			bus.AddChipDmaPointerOffset(state.SpriteLastFetchAddresses[0], 2),
			state.SpriteNextFetchAddresses[0]);
	}

	[Fact]
	public void AgnusSpriteAddressAdvancesOnlyAfterGrantedFetch()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		bus.WriteWord(0xDFF120, 0x0000);
		bus.WriteWord(0xDFF122, 0x3000);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1111);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3002, 0x2222);
		var state = bus.CausalBusExecutor.DisplayControlState;
		var firstSlot = OutputRowStartCycle(0) +
			((long)AgnusHrmOcsSlotTable.FirstSpriteHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);
		var secondSlot = firstSlot + (2L * AmigaConstants.A500PalCpuCyclesPerColorClock);

		Assert.Equal(0x3000u, state.SpriteNextFetchAddresses[0]);
		Assert.True(bus.CausalBusExecutor.TryExecuteSpriteWord(
			0, 0x3000, firstSlot, out var firstValue, out _));
		Assert.Equal((ushort)0x1111, firstValue);
		Assert.Equal(0x3002u, state.SpriteNextFetchAddresses[0]);
		Assert.Equal(1, state.SpriteAddressMatches);

		// Re-executing an already committed slot cannot advance the causal address.
		Assert.False(bus.CausalBusExecutor.TryExecuteSpriteWord(
			0, 0x3002, firstSlot, out _, out _));
		Assert.Equal(0x3002u, state.SpriteNextFetchAddresses[0]);
		Assert.Equal(1, state.SpriteGrantCounts[0]);

		Assert.True(bus.CausalBusExecutor.TryExecuteSpriteWord(
			0, 0x3002, secondSlot, out var secondValue, out _));
		Assert.Equal((ushort)0x2222, secondValue);
		Assert.Equal(0x3004u, state.SpriteNextFetchAddresses[0]);
		Assert.Equal(2, state.SpriteAddressMatches);
		Assert.Equal(0, state.SpriteAddressMismatches);
	}

	[Fact]
	public void AgnusExecutorSamplesSpriteWordAndCommitsAddressAtomically()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		bus.WriteWord(0xDFF120, 0x0000);
		bus.WriteWord(0xDFF122, 0x3000);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0xA55A);
		var spriteSlot = OutputRowStartCycle(0) +
			((long)AgnusHrmOcsSlotTable.FirstSpriteHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);

		var granted = bus.CausalBusExecutor.TryExecuteSpriteWord(
			channel: 0,
			address: 0x3000,
			requestedCycle: spriteSlot,
			out var value,
			out var grantedCycle);

		Assert.True(granted);
		Assert.Equal((ushort)0xA55A, value);
		Assert.Equal(spriteSlot, grantedCycle);
		Assert.Equal(grantedCycle, bus.CausalBusExecutor.DisplayControlState.SpriteLastFetchCycles[0]);
		Assert.Equal(0x3002u, bus.CausalBusExecutor.DisplayControlState.SpriteNextFetchAddresses[0]);
		Assert.True(bus.CausalBusExecutor.ExecutedThroughCycle >= grantedCycle);

		var denied = bus.CausalBusExecutor.TryExecuteSpriteWord(
			channel: 0,
			address: 0x3002,
			requestedCycle: spriteSlot,
			out _,
			out _);
		Assert.False(denied);
		Assert.Equal(0x3002u, bus.CausalBusExecutor.DisplayControlState.SpriteNextFetchAddresses[0]);
		Assert.Equal(1, bus.CausalBusExecutor.DisplayControlState.SpriteGrantCounts[0]);
	}

	[Fact]
	public void LiveDisplayCpuVisibleWakeAgendaSurvivesHorizonOnlyQueriesAndInvalidatesOnScheduleChange()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart - 2);

		var rebuildsBefore = bus.Display.LiveCpuVisibleWorkCycleRebuilds;
		var hitsBefore = bus.Display.LiveCpuVisibleWorkCycleCacheHits;
		var first = bus.Display.GetNextLiveDisplayWakeCandidateCycle(lineStart - 2, lineStart);
		var second = bus.Display.GetNextLiveDisplayWakeCandidateCycle(lineStart - 2, lineStart + 2);

		Assert.Equal(lineStart, first);
		Assert.Equal(lineStart, second);
		Assert.Equal(rebuildsBefore, bus.Display.LiveCpuVisibleWorkCycleRebuilds);
		Assert.Equal(hitsBefore + 2, bus.Display.LiveCpuVisibleWorkCycleCacheHits);

		var writeCycle = lineStart - 1;
		bus.Display.ScheduleWrite(writeCycle, 0x0100, 0x0000);
		var afterWrite = bus.Display.GetNextLiveDisplayWakeCandidateCycle(
			lineStart - 2,
			lineStart + 4);

		Assert.Equal(writeCycle, afterWrite);
		Assert.Equal(rebuildsBefore + 1, bus.Display.LiveCpuVisibleWorkCycleRebuilds);
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
	public void LiveDmaSlotWorkExcludesLineStateAndIncludesFirstBitplaneFetch()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var fetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart - AgnusChipSlotScheduler.SlotCycles);

		Assert.False(bus.Display.HasLiveDmaSlotWorkThrough(lineStart));
		bus.AdvanceDmaTo(lineStart);
		Assert.True(bus.Display.HasLiveDmaSlotWorkThrough(fetchCycle));
	}

	[Fact]
	public void DmaconBitplaneDisableBeforeFirstFetchSuppressesSameLineDmaSlots()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var firstFetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);

		var disableRequestCycle = firstFetchCycle - (4 * AgnusChipSlotScheduler.SlotCycles);
		bus.WriteWord(0x00DFF096, 0x0100, disableRequestCycle);
		bus.AdvanceDmaTo(lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var bitplaneFetches = bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.GrantedCycle >= firstFetchCycle &&
				access.GrantedCycle < lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine)
			.ToArray();
		var dmaconWrites = bus.CustomRegisterWrites
			.Where(write => write.Address == 0x096)
			.ToArray();
		var diagnostic = string.Join(
			"; ",
			bitplaneFetches.Select(access =>
			$"fetch 0x{access.Request.Address:X6}@{access.Request.RequestedCycle}->{access.GrantedCycle}")) +
			$" | firstFetch={firstFetchCycle}, disableRequest={disableRequestCycle}, " +
			$"dmacon=[{string.Join(",", dmaconWrites.Select(write => $"0x{write.Value:X4}@{write.Cycle}"))}]";

		Assert.True(bitplaneFetches.Length == 0, diagnostic);
	}

	[Fact]
	public void CpuLongDmaconHighWordDisablesBitplaneDmaBeforeSecondWordCompletes()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var firstFetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);

		var disableRequestCycle = firstFetchCycle - (2 * AgnusChipSlotScheduler.SlotCycles);
		bus.WriteLong(0x00DFF096, 0x01000000, disableRequestCycle);
		bus.AdvanceDmaTo(lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var dmaconDisable = Assert.Single(
			bus.CustomRegisterWrites,
			write => write.Address == 0x096 && write.Value == 0x0100);
		var bitplaneFetches = bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.GrantedCycle >= firstFetchCycle &&
				access.GrantedCycle < lineStart + AmigaConstants.A500PalCpuCyclesPerRasterLine)
			.ToArray();
		var diagnostic =
			$"firstFetch={firstFetchCycle}, disableRequest={disableRequestCycle}, " +
			$"disableLanded={dmaconDisable.Cycle}, " +
			$"fetches=[{string.Join(",", bitplaneFetches.Select(access => $"{access.Request.RequestedCycle}->{access.GrantedCycle}"))}]";

		Assert.True(dmaconDisable.Cycle < firstFetchCycle, diagnostic);
		Assert.True(bitplaneFetches.Length == 0, diagnostic);
	}

	[Fact]
	public void LiveDmaScratchCustomLongDmaconHighWordSuppressesBitplaneFetchBeforeSecondWord()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var firstFetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		var scratchSlots = new AgnusHrmSlotEngine(captureSlotDebug: true);
		var requestCycle = firstFetchCycle - (2 * AgnusChipSlotScheduler.SlotCycles);

		var supported = bus.Display.TryRunCpuWaitLiveDmaScratch(
			scratchSlots,
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessTarget.CustomRegisters,
			0x00DFF096,
			AmigaBusAccessSize.Long,
			requestCycle,
			isWrite: true,
			OcsLiveDmaScratchCpuWrite.Long(
				AmigaBusAccessTarget.CustomRegisters,
				0x00DFF096,
				0x01000000),
			out var result);

		var diagnostic =
			$"{result.ToDetailString()}, firstFetch={firstFetchCycle}, request={requestCycle}, " +
			$"grant={result.GrantedCycle}, second={result.SecondWordCycle}, completion={result.CompletedCycle}";
		Assert.True(supported, diagnostic);
		Assert.True(result.GrantedCycle < firstFetchCycle, diagnostic);
		Assert.True(result.SecondWordCycle >= firstFetchCycle, diagnostic);
		Assert.Equal(0, result.BitplaneFetches);
	}

	[Fact]
	public void LiveDmaScratchSingleSlotExecutorMatchesReferenceBitplaneCollision()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);
		var scratchSlots = new AgnusHrmSlotEngine(captureSlotDebug: true);

		var supported = bus.Display.TryRunCpuWaitLiveDmaScratch(
			scratchSlots,
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x00001000,
			AmigaBusAccessSize.Word,
			requestCycle,
			isWrite: false,
			OcsLiveDmaScratchCpuWrite.None,
			out var result);
		var referenceBus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(referenceBus);
		referenceBus.AdvanceDmaTo(lineStart);
		var wakeBeforeReference = referenceBus.Display.GetNextLiveDisplayWakeCandidateCycle(
			requestCycle - 1,
			requestCycle);
		var schedulerBeforeReference = referenceBus.CaptureHardwareSchedulerSnapshot();
		var agnusBeforeReference = referenceBus.Agnus.CurrentCycle;
		var hasLiveBeforeReference = referenceBus.Display.HasLiveDisplayWork();
		var agnusWakeBeforeReference = referenceBus.Agnus.GetNextWakeCandidateCycle(requestCycle - 1, requestCycle, true);
		var referenceCycle = requestCycle;
		_ = referenceBus.ReadWord(0x00001000, ref referenceCycle, AmigaBusAccessKind.CpuDataRead);
		var schedulerAfterReference = referenceBus.CaptureHardwareSchedulerSnapshot();
		var agnusAfterReference = referenceBus.Agnus.CurrentCycle;
		var reference = Assert.Single(
			referenceBus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
				access.Request.Address == 0x00001000);

		var nearbyAccesses = string.Join(
			" | ",
			referenceBus.BusAccesses
				.Where(access => Math.Abs(access.GrantedCycle - requestCycle) <= 8)
				.Select(access => $"{access.Request.Requester}/{access.Request.Kind}/{access.Request.Target}@{access.GrantedCycle}->{access.CompletedCycle}"));
		var diagnostic = $"scratch={result.ToDetailString()}, reference={reference.GrantedCycle}->{reference.CompletedCycle}, " +
			$"wake={wakeBeforeReference?.ToString() ?? "none"}, live={referenceBus.Display.LiveExecutionCycle}/" +
			$"{referenceBus.Display.LiveCapturedThroughCycle}, drains={schedulerBeforeReference.DrainCount}->" +
			$"{schedulerAfterReference.DrainCount}, busDrains={schedulerBeforeReference.BusAccessDrainCount}->" +
			$"{schedulerAfterReference.BusAccessDrainCount}, wakeSkips={schedulerBeforeReference.WakeAgendaDrainSkips}->" +
			$"{schedulerAfterReference.WakeAgendaDrainSkips}, agnus={schedulerBeforeReference.AgnusEvents}->" +
			$"{schedulerAfterReference.AgnusEvents}/cycle:{agnusBeforeReference}->{agnusAfterReference}/live:{hasLiveBeforeReference}/wake:{agnusWakeBeforeReference}, fast={schedulerBeforeReference.CopperQuiescentFastPathUsed}->" +
			$"{schedulerAfterReference.CopperQuiescentFastPathUsed}, accesses={nearbyAccesses}";
		Assert.True(supported, diagnostic);
		Assert.True(result.BitplaneFetches > 0, diagnostic);
		Assert.True(reference.GrantedCycle == result.GrantedCycle, diagnostic);
		Assert.True(reference.CompletedCycle == result.CompletedCycle, diagnostic);
	}

	[Fact]
	public void CpuWaitFixedSlotImageReportsBitplaneOwnerWithoutMutation()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var fetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);
		var before = bus.Display.CaptureSnapshot();

		var supported = bus.Display.TryGetCpuWaitFixedSlotOwner(fetchCycle, out var owner, out var unsupported);
		var after = bus.Display.CaptureSnapshot();

		Assert.True(supported, unsupported.ToString());
		Assert.Equal(CpuWaitFixedSlotOwner.BitplaneRead, owner);
		Assert.Equal(before.LastBitplaneDmaFetches, after.LastBitplaneDmaFetches);
		Assert.Equal(before.LastSpriteDmaFetches, after.LastSpriteDmaFetches);
		Assert.Equal(before.LastFirstDisplayDmaCycle, after.LastFirstDisplayDmaCycle);
		Assert.Equal(before.LastLastDisplayDmaCycle, after.LastLastDisplayDmaCycle);
	}

	[Fact]
	public void CpuWaitFixedSlotImageKeepsPreparedButUncapturedBitplaneSlotOwned()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var firstFetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);
		bus.Display.PrepareLiveDisplaySlotsBeforeHrmGrant(firstFetchCycle + 16);
		var before = bus.Display.CaptureSnapshot();

		var supported = bus.Display.TryGetCpuWaitFixedSlotOwner(
			firstFetchCycle,
			out var owner,
			out var unsupported);
		var after = bus.Display.CaptureSnapshot();

		Assert.True(supported, unsupported.ToString());
		Assert.Equal(CpuWaitFixedSlotOwner.BitplaneRead, owner);
		Assert.Equal(before.LastBitplaneDmaFetches, after.LastBitplaneDmaFetches);
		Assert.Equal(before.LastFirstDisplayDmaCycle, after.LastFirstDisplayDmaCycle);
		Assert.Equal(before.LastLastDisplayDmaCycle, after.LastLastDisplayDmaCycle);
	}

	[Theory]
	[InlineData(0x096, 0x0100)]
	[InlineData(0x100, 0x0200)]
	public void CommittedDisplayDmaRejectsRegisterWritesBehindItsCausalHorizon(
		ushort registerOffset,
		ushort value)
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var firstFetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);
		bus.Display.PrepareLiveDisplaySlotsBeforeHrmGrant(firstFetchCycle + 16);
		Assert.True(bus.TryGetCommittedAgnusSlotOwner(firstFetchCycle, out var beforeOwner));
		Assert.Equal(AgnusChipSlotOwner.Bitplane, beforeOwner);
		Assert.True(bus.Display.LiveCapturedThroughCycle >= firstFetchCycle);
		Assert.True(bus.Display.LiveCausalDisplayStateThroughCycle >= firstFetchCycle);

		var writeCycle = firstFetchCycle - (4 * AgnusChipSlotScheduler.SlotCycles);
		var error = Assert.Throws<InvalidOperationException>(() =>
			bus.Display.ScheduleWrite(writeCycle, registerOffset, value));

		Assert.Contains("behind the causal display horizon", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CpuWaitFixedSlotImageCacheHitAllocatesNothing()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var fetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);
		Assert.True(bus.Display.TryGetCpuWaitFixedSlotOwner(fetchCycle, out _, out _));
		// Let tiered compilation settle before measuring the steady-state cache
		// path. The exact threshold is runtime- and suite-order-dependent.
		for (var i = 0; i < 4096; i++)
		{
			_ = bus.Display.TryGetCpuWaitFixedSlotOwner(fetchCycle, out _, out _);
		}

		var before = GC.GetAllocatedBytesForCurrentThread();
		var allHits = true;

		for (var i = 0; i < 1000; i++)
		{
			allHits &= bus.Display.TryGetCpuWaitFixedSlotOwner(fetchCycle, out _, out _);
		}

		var after = GC.GetAllocatedBytesForCurrentThread();
		Assert.True(allHits);
		Assert.Equal(before, after);
	}

	[Fact]
	public void CpuWaitFixedSlotImagePredictionMatchesReferenceBitplaneCollision()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);

		var supported = bus.TryPredictCpuWaitFixedSlotGrant(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x00001000,
			AmigaBusAccessSize.Word,
			requestCycle,
			isWrite: false,
			out var predictedGrant,
			out var predictedCompletion,
			out var unsupported);
		var referenceCycle = requestCycle;
		_ = bus.ReadWord(0x00001000, ref referenceCycle, AmigaBusAccessKind.CpuDataRead);
		var reference = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
				access.Request.Address == 0x00001000);

		Assert.True(supported, unsupported.ToString());
		Assert.Equal(reference.GrantedCycle, predictedGrant);
		Assert.Equal(reference.CompletedCycle, predictedCompletion);
	}

	[Fact]
	public void CpuWaitFixedSlotImagePredictionMatchesReferenceRefreshDuringVerticalBlank()
	{
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		bus.EnableLiveAgnusDma();
		var requestCycle = Enumerable.Range(
				0,
				AmigaConstants.A500PalCpuCyclesPerRasterLine /
					AgnusChipSlotScheduler.SlotCycles)
			.Select(slot => (long)slot * AgnusChipSlotScheduler.SlotCycles)
			.First(bus.IsMandatoryRefreshSlot);

		var supported = bus.TryPredictCpuWaitFixedSlotGrantAcrossPhysicalFrame(
			AmigaBusAccessKind.CpuInstructionFetch,
			AmigaBusAccessTarget.ExpansionRam,
			bus.ExpansionRamBase,
			AmigaBusAccessSize.Word,
			requestCycle,
			isWrite: false,
			out var predictedGrant,
			out var predictedCompletion,
			out var unsupported);

		var referenceCycle = requestCycle;
		_ = bus.ReadWord(
			bus.ExpansionRamBase,
			ref referenceCycle,
			AmigaBusAccessKind.CpuInstructionFetch);
		var reference = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuInstructionFetch &&
				access.Request.Address == bus.ExpansionRamBase);

		Assert.True(supported, unsupported.ToString());
		Assert.Equal(reference.GrantedCycle, predictedGrant);
		Assert.Equal(reference.CompletedCycle, predictedCompletion);
		Assert.True(bus.IsMandatoryRefreshSlot(requestCycle));
		Assert.False(bus.IsMandatoryRefreshSlot(predictedGrant));
	}

	[Fact]
	public void CpuWaitFixedSlotImageFindsFreeSlotAfterDenseBitplaneRun()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		for (uint plane = 0; plane < 5; plane++)
		{
			bus.WriteLong(0x00DFF0E0 + (plane * 4), 0x00001000 + (plane * 0x1000));
		}

		bus.WriteWord(0x00DFF100, 0x5200);
		bus.EnableLiveAgnusDma();
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		bus.AdvanceDmaTo(lineStart);
		var requestedCycle = lineStart + (0x38 * AgnusChipSlotScheduler.SlotCycles);

		var supported = bus.TryPredictCpuWaitFixedSlotGrant(
			AmigaBusAccessKind.CpuInstructionFetch,
			AmigaBusAccessTarget.ChipRam,
			0x00001000,
			AmigaBusAccessSize.Word,
			requestedCycle,
			isWrite: false,
			out var grantedCycle,
			out var completedCycle,
			out var unsupported);

		Assert.True(supported, unsupported.ToString());
		Assert.True(grantedCycle >= requestedCycle);
		Assert.Equal(grantedCycle + AgnusChipSlotScheduler.SlotCycles, completedCycle);
		Assert.False(AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(grantedCycle));
		Assert.True(bus.Display.TryGetCpuWaitFixedSlotOwner(
			grantedCycle,
			out var owner,
			out var ownerUnsupported), ownerUnsupported.ToString());
		Assert.Equal(CpuWaitFixedSlotOwner.Free, owner);
	}

	[Fact]
	public void CpuWaitFixedSlotImageReportsRefreshAndFreeOwners()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);

		var refreshCycle = Enumerable.Range(0, AmigaConstants.A500PalCpuCyclesPerRasterLine / 2)
			.Select(slot => lineStart + (slot * AgnusChipSlotScheduler.SlotCycles))
			.First(AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot);
		Assert.True(bus.Display.TryGetCpuWaitFixedSlotOwner(refreshCycle, out var refresh, out var refreshUnsupported));
		Assert.Equal(CpuWaitFixedSlotImageUnsupported.None, refreshUnsupported);
		Assert.Equal(CpuWaitFixedSlotOwner.Refresh, refresh);

		var freeCycle = Enumerable.Range(0, AmigaConstants.A500PalCpuCyclesPerRasterLine / 2)
			.Select(slot => lineStart + (slot * AgnusChipSlotScheduler.SlotCycles))
			.First(cycle =>
				!AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(cycle) &&
				bus.Display.TryGetCpuWaitFixedSlotOwner(cycle, out var owner, out _) &&
				owner == CpuWaitFixedSlotOwner.Free);
		Assert.True(bus.Display.TryGetCpuWaitFixedSlotOwner(freeCycle, out var free, out var freeUnsupported));
		Assert.Equal(CpuWaitFixedSlotImageUnsupported.None, freeUnsupported);
		Assert.Equal(CpuWaitFixedSlotOwner.Free, free);
	}

	[Fact]
	public void CpuWaitFixedSlotImageReportsSpriteOwnerWithoutMutation()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		ConfigureLiveSpriteDma(bus);
		bus.AdvanceDmaTo(lineStart);
		var before = bus.Display.CaptureSnapshot();

		var spriteCycle = Enumerable.Range(0, AmigaConstants.A500PalCpuCyclesPerRasterLine / 2)
			.Select(slot => lineStart + (slot * AgnusChipSlotScheduler.SlotCycles))
			.First(cycle =>
				bus.Display.TryGetCpuWaitFixedSlotOwner(cycle, out var owner, out _) &&
				owner == CpuWaitFixedSlotOwner.SpriteRead);
		var after = bus.Display.CaptureSnapshot();

		Assert.True(bus.Display.TryGetCpuWaitFixedSlotOwner(spriteCycle, out var owner, out var unsupported));
		Assert.Equal(CpuWaitFixedSlotImageUnsupported.None, unsupported);
		Assert.Equal(CpuWaitFixedSlotOwner.SpriteRead, owner);
		Assert.Equal(before.LastSpriteDmaFetches, after.LastSpriteDmaFetches);
		Assert.Equal(before.LastFirstDisplayDmaCycle, after.LastFirstDisplayDmaCycle);
		Assert.Equal(before.LastLastDisplayDmaCycle, after.LastLastDisplayDmaCycle);
	}

	[Fact]
	public void CpuWaitFixedSlotImageBuildAllocatesNothing()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);
		_ = bus.Display.TryGetCpuWaitFixedSlotOwner(LowResPlane1FetchCycle(row), out _, out _);
		bus.AdvanceDmaTo(OutputRowStartCycle(row + 1));
		var fetchCycle = LowResPlane1FetchCycle(row + 1);
		var before = GC.GetAllocatedBytesForCurrentThread();

		var supported = bus.Display.TryGetCpuWaitFixedSlotOwner(fetchCycle, out var owner, out var unsupported);

		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
		Assert.True(supported, unsupported.ToString());
		Assert.Equal(CpuWaitFixedSlotOwner.BitplaneRead, owner);
	}

	[Fact]
	public void CpuWaitFixedSlotImageRejectsPendingWriteBeforeMutation()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		bus.AdvanceDmaTo(lineStart);
		bus.Display.ScheduleWrite(lineStart + 20, 0x0180, 0x0123);

		var supported = bus.Display.TryGetCpuWaitFixedSlotOwner(
			LowResPlane1FetchCycle(row),
			out _,
			out var unsupported);

		Assert.False(supported);
		Assert.Equal(CpuWaitFixedSlotImageUnsupported.PendingWrite, unsupported);
	}

	[Fact]
	public void CpuWaitFixedSlotImageRejectsReachableCopperBeforeMutation()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		StartCopperListAtFrameStart(
			bus,
			(0x2C01, 0xFFFE),
			(0x0180, 0x0123),
			(0xFFFF, 0xFFFE));
		bus.AdvanceDmaTo(lineStart);

		var supported = bus.Display.TryGetCpuWaitFixedSlotOwner(
			lineStart + 16,
			out _,
			out var unsupported);

		Assert.False(supported);
		Assert.Equal(CpuWaitFixedSlotImageUnsupported.Copper, unsupported);
	}

	[Fact]
	public void CpuWaitFixedSlotImagePredictionPreservesDisplayFetchTimeMemoryVisibility()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1357);
		bus.AdvanceDmaTo(lineStart);
		Assert.True(bus.TryPredictCpuWaitFixedSlotGrant(
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessTarget.ChipRam,
			0x00001000,
			AmigaBusAccessSize.Word,
			requestCycle,
			isWrite: true,
			out var predictedGrant,
			out _,
			out var unsupported), unsupported.ToString());

		var cpuCycle = requestCycle;
		bus.WriteWord(0x00001000, 0xA55A, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.True(predictedGrant > requestCycle);
		Assert.Equal(predictedGrant + AgnusChipSlotScheduler.SlotCycles, cpuCycle);
		Assert.Equal(0x1357, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
		Assert.Equal(0xA55A, BigEndian.ReadUInt16(bus.ChipRam, 0x1000, "CPU write after display fetch"));
	}

	[Fact]
	public void DeferredCpuBatchExitReadUsesCausalExecutorAcrossLiveBitplaneCollision()
	{
		var baseline = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var deferred = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		ConfigureLiveOneBitplaneDma(baseline);
		ConfigureLiveOneBitplaneDma(deferred);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x1357);
		BigEndian.WriteUInt16(deferred.ChipRam, 0x1000, 0x1357);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		baseline.AdvanceDmaTo(lineStart);
		deferred.AdvanceDmaTo(lineStart);
		deferred.SetCpuWaitSlotContendedCleanThroughForTest(requestCycle - 1);
		deferred.ArmDeferredCpuBatchExitForTest(requestCycle);
		var baselineCycle = requestCycle;
		var deferredCycle = requestCycle;

		var baselineValue = AmigaCpuDataAccess.ReadWord(baseline, 0x00001000, ref baselineCycle);
		var deferredValue = AmigaCpuDataAccess.ReadWord(deferred, 0x00001000, ref deferredCycle);

		Assert.Equal(baselineValue, deferredValue);
		Assert.Equal(baselineCycle, deferredCycle);
		Assert.Equal(
			baseline.Display.CaptureSnapshot().LastBitplaneDmaFetches,
			deferred.Display.CaptureSnapshot().LastBitplaneDmaFetches);
		Assert.True(deferred.CausalBusExecutor.ProductionEnabled);
		Assert.True(deferred.TryGetCommittedAgnusSlotOwner(requestCycle, out var owner));
		Assert.Equal(AgnusChipSlotOwner.Bitplane, owner);
	}

	[Fact]
	public void DeferredCpuBatchExitWritePreservesCausalBitplaneFetchBeforeCpuWrite()
	{
		var baseline = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var deferred = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		ConfigureLiveOneBitplaneDma(baseline);
		ConfigureLiveOneBitplaneDma(deferred);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x1357);
		BigEndian.WriteUInt16(deferred.ChipRam, 0x1000, 0x1357);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		baseline.AdvanceDmaTo(lineStart);
		deferred.AdvanceDmaTo(lineStart);
		deferred.SetCpuWaitSlotContendedCleanThroughForTest(requestCycle - 1);
		deferred.ArmDeferredCpuBatchExitForTest(requestCycle);
		var baselineCycle = requestCycle;
		var deferredCycle = requestCycle;

		AmigaCpuDataAccess.WriteWord(baseline, 0x00001000, 0xA55A, ref baselineCycle);
		AmigaCpuDataAccess.WriteWord(deferred, 0x00001000, 0xA55A, ref deferredCycle);

		Assert.Equal(baselineCycle, deferredCycle);
		Assert.Equal(0x1357, ReadLiveBitplaneWord(baseline, row, plane: 0, word: 0));
		Assert.Equal(0x1357, ReadLiveBitplaneWord(deferred, row, plane: 0, word: 0));
		Assert.Equal(0xA55A, BigEndian.ReadUInt16(deferred.ChipRam, 0x1000, "deferred CPU write"));
		Assert.True(deferred.CausalBusExecutor.ProductionEnabled);
		Assert.True(deferred.TryGetCommittedAgnusSlotOwner(requestCycle, out var owner));
		Assert.Equal(AgnusChipSlotOwner.Bitplane, owner);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void DeferredBatchExitMatchesCausalExecutorWithLiveBitplaneAndActiveBlitter(bool nasty)
	{
		var baseline = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var deferred = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		ConfigureLiveOneBitplaneDma(baseline);
		ConfigureLiveOneBitplaneDma(deferred);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x2468);
		BigEndian.WriteUInt16(deferred.ChipRam, 0x1000, 0x2468);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		baseline.AdvanceDmaTo(lineStart);
		deferred.AdvanceDmaTo(lineStart);
		StartLongBlit(baseline, lineStart);
		StartLongBlit(deferred, lineStart);
		if (nasty)
		{
			baseline.WriteWord(0x00DFF096, 0x8400, lineStart);
			deferred.WriteWord(0x00DFF096, 0x8400, lineStart);
		}
		baseline.AdvanceDmaTo(requestCycle - 1);
		deferred.AdvanceDmaTo(requestCycle - 1);
		var baselineSlots = new List<AgnusSlotScheduleAuditEntry>();
		var deferredSlots = new List<AgnusSlotScheduleAuditEntry>();
		baseline.SetSlotScheduleAuditSink(baselineSlots.Add);
		deferred.SetSlotScheduleAuditSink(deferredSlots.Add);
		deferred.SetCpuWaitSlotContendedCleanThroughForTest(requestCycle - 1);
		deferred.ArmDeferredCpuBatchExitForTest(requestCycle);
		var baselineCycle = requestCycle;
		var deferredCycle = requestCycle;

		var baselineValue = AmigaCpuDataAccess.ReadWord(baseline, 0x00001000, ref baselineCycle);
		var deferredValue = AmigaCpuDataAccess.ReadWord(deferred, 0x00001000, ref deferredCycle);
		var baselineBlitter = baseline.Blitter.CaptureSnapshot();
		var deferredBlitter = deferred.Blitter.CaptureSnapshot();
		var diagnostic =
			$"cpu={baselineCycle}/{deferredCycle}; " +
			$"reference=[{string.Join(',', baselineSlots.Select(FormatSlotAuditEntry))}]; " +
			$"deferred=[{string.Join(',', deferredSlots.Select(FormatSlotAuditEntry))}]";

		Assert.True(baselineValue == deferredValue, diagnostic);
		Assert.True(baselineCycle == deferredCycle, diagnostic);
		Assert.Equal(ReadLiveBitplaneWord(baseline, row, 0, 0), ReadLiveBitplaneWord(deferred, row, 0, 0));
		Assert.Equal(baselineBlitter.CurrentCycle, deferredBlitter.CurrentCycle);
		Assert.Equal(baselineBlitter.WordX, deferredBlitter.WordX);
		Assert.Equal(baselineBlitter.RowY, deferredBlitter.RowY);
		Assert.Equal(baselineBlitter.SourceA, deferredBlitter.SourceA);
		Assert.Equal(baselineBlitter.DestinationD, deferredBlitter.DestinationD);
		Assert.True(baseline.CausalBusExecutor.ProductionEnabled);
		Assert.True(deferred.CausalBusExecutor.ProductionEnabled);
	}

	private static string FormatSlotAuditEntry(AgnusSlotScheduleAuditEntry entry)
		=> $"{entry.SlotCycle}:{entry.Owner}:{entry.Source}";

	[Fact]
	public void RetiredFixedSlotImageMismatchDoesNotDisableCausalExecutor()
	{
		var baseline = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		var deferred = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		ConfigureLiveOneBitplaneDma(baseline);
		ConfigureLiveOneBitplaneDma(deferred);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		baseline.AdvanceDmaTo(lineStart);
		deferred.AdvanceDmaTo(lineStart);
		deferred.VerifyProductionCpuWaitFixedSlotImageForTest(
			default,
			new CpuWaitFixedSlotTimelineSignature(
				1,
				1,
				requestCycle,
				requestCycle,
				AgnusChipSlotOwner.Cpu,
				AgnusChipSlotOwner.Cpu));
		deferred.SetCpuWaitSlotContendedCleanThroughForTest(requestCycle - 1);
		deferred.ArmDeferredCpuBatchExitForTest(requestCycle);
		var baselineCycle = requestCycle;
		var deferredCycle = requestCycle;

		var baselineValue = AmigaCpuDataAccess.ReadWord(baseline, 0x00001000, ref baselineCycle);
		var deferredValue = AmigaCpuDataAccess.ReadWord(deferred, 0x00001000, ref deferredCycle);
		var scheduler = deferred.CaptureHardwareSchedulerSnapshot();

		Assert.Equal(baselineValue, deferredValue);
		Assert.Equal(baselineCycle, deferredCycle);
		Assert.Equal(1, scheduler.DeferredCpuWaitFixedImageProductionVerificationMismatches);
		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionAttempts);
		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionUsed);
		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionFallbackUnsupported);
		Assert.True(deferred.CausalBusExecutor.ProductionEnabled);
	}

	[Fact]
	public void CausalExecutorFixedSlotAccessAllocatesNothingWithoutLegacyImage()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true);
		ConfigureLiveOneBitplaneDma(bus);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var firstLineStart = OutputRowStartCycle(row);
		var firstRequest = LowResPlane1FetchCycle(row);
		bus.AdvanceDmaTo(firstLineStart);
		bus.SetCpuWaitSlotContendedCleanThroughForTest(firstRequest - 1);
		bus.ArmDeferredCpuBatchExitForTest(firstRequest);
		var warmCycle = firstRequest;
		_ = AmigaCpuDataAccess.ReadWord(bus, 0x00001000, ref warmCycle);
		var measuredRow = row + 1;
		var measuredLineStart = OutputRowStartCycle(measuredRow);
		var measuredRequest = LowResPlane1FetchCycle(measuredRow);
		bus.AdvanceDmaTo(measuredLineStart);
		bus.SetCpuWaitSlotContendedCleanThroughForTest(measuredRequest - 1);
		bus.ArmDeferredCpuBatchExitForTest(measuredRequest);
		var measuredCycle = measuredRequest;
		var before = GC.GetAllocatedBytesForCurrentThread();

		_ = AmigaCpuDataAccess.ReadWord(bus, 0x00001000, ref measuredCycle);

		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
		Assert.Equal(0, bus.CaptureHardwareSchedulerSnapshot().DeferredCpuWaitFixedImageProductionUsed);
		Assert.True(bus.CausalBusExecutor.ProductionEnabled);
	}

	[Fact]
	public void CausalExecutorSplitsLiveLongAccessWithoutLegacyFallback()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableDeferredCpuBusBatch: true);
		ConfigureLiveOneBitplaneDma(bus);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		var requestCycle = LowResPlane1FetchCycle(row);
		bus.AdvanceDmaTo(lineStart);
		bus.SetCpuWaitSlotContendedCleanThroughForTest(requestCycle - 1);
		bus.ArmDeferredCpuBatchExitForTest(requestCycle);
		var cycle = requestCycle;

		_ = AmigaCpuDataAccess.ReadLong(bus, 0x00001000, ref cycle);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();

		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionAttempts);
		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionUsed);
		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionFallbackUnsupported);
		Assert.True(bus.CausalBusExecutor.ProductionEnabled);
	}

	[Fact]
	public void DeferredCpuLiveSlotFallbackMatchesReferenceAcrossBitplaneLine()
	{
		var baseline = new AmigaBus(enableLiveAgnusDma: true);
		var deferred = new AmigaBus(enableLiveAgnusDma: true, enableDeferredCpuBusBatch: true);
		ConfigureLiveOneBitplaneDma(baseline);
		ConfigureLiveOneBitplaneDma(deferred);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		baseline.AdvanceDmaTo(lineStart);
		deferred.AdvanceDmaTo(lineStart);
		var baselineCycle = lineStart;
		var deferredCycle = lineStart;

		for (var access = 0; access < 128; access++)
		{
			var baselineValue = baseline.ReadWord(0x00001000, ref baselineCycle, AmigaBusAccessKind.CpuDataRead);
			var deferredValue = deferred.ReadWord(0x00001000, ref deferredCycle, AmigaBusAccessKind.CpuDataRead);
			var diagnostic = $"access={access},cycles={baselineCycle}/{deferredCycle}," +
				$"values=0x{baselineValue:X4}/0x{deferredValue:X4}," +
				$"baseline={baseline.Display.CaptureSnapshot().LastBitplaneDmaFetches}," +
				$"deferred={deferred.Display.CaptureSnapshot().LastBitplaneDmaFetches}";
			Assert.True(baselineValue == deferredValue, diagnostic);
			Assert.True(baselineCycle == deferredCycle, diagnostic);
		}
	}

	[Fact]
	public void DeferredCpuLiveSlotExecutorMatchesReferenceAcrossPaletteCopper()
	{
		var baseline = new AmigaBus(enableLiveAgnusDma: true);
		var deferred = new AmigaBus(enableLiveAgnusDma: true, enableDeferredCpuBusBatch: true);
		StartCopperListAtFrameStart(
			baseline,
			(0x0180, 0x0111),
			(0x0182, 0x0222),
			(0x0184, 0x0333),
			(0xFFFF, 0xFFFE));
		StartCopperListAtFrameStart(
			deferred,
			(0x0180, 0x0111),
			(0x0182, 0x0222),
			(0x0184, 0x0333),
			(0xFFFF, 0xFFFE));
		var baselineCycle = 0L;
		var deferredCycle = 0L;

		for (var access = 0; access < 32; access++)
		{
			var baselineValue = baseline.ReadWord(0x00001000, ref baselineCycle, AmigaBusAccessKind.CpuDataRead);
			var deferredValue = deferred.ReadWord(0x00001000, ref deferredCycle, AmigaBusAccessKind.CpuDataRead);
			var baselineSnapshot = baseline.Display.CaptureSnapshot();
			var deferredSnapshot = deferred.Display.CaptureSnapshot();
			var diagnostic = $"access={access},cycles={baselineCycle}/{deferredCycle}," +
				$"values=0x{baselineValue:X4}/0x{deferredValue:X4}," +
				$"colors=0x{baselineSnapshot.Colors[0]:X4}/0x{deferredSnapshot.Colors[0]:X4}";
			Assert.True(baselineValue == deferredValue, diagnostic);
			Assert.True(baselineCycle == deferredCycle, diagnostic);
			Assert.True(baselineSnapshot.Colors.SequenceEqual(deferredSnapshot.Colors), diagnostic);
		}
	}

	[Fact]
	public void CpuDmaconBitplaneDisableRequestedBeforeFetchLandsAfterContendedFetch()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var firstFetchCycle = LowResPlane1FetchCycle(row);
		ConfigureLiveOneBitplaneDma(bus);

		var disableRequestCycle = firstFetchCycle;
		bus.WriteWord(0x00DFF096, 0x0100, disableRequestCycle);
		bus.AdvanceDmaTo(firstFetchCycle + 16);

		var dmaconDisable = Assert.Single(
			bus.CustomRegisterWrites,
			write => write.Address == 0x096 && write.Value == 0x0100);
		var firstFetch = Assert.Single(
			bus.BusAccesses,
			access => access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.Request.Kind == AmigaBusAccessKind.Bitplane &&
				access.GrantedCycle == firstFetchCycle);
		var diagnostic =
			$"disableRequest={disableRequestCycle}, disableLanded={dmaconDisable.Cycle}, " +
			$"fetch={firstFetch.Request.RequestedCycle}->{firstFetch.GrantedCycle}";

		Assert.True(dmaconDisable.Cycle > firstFetch.GrantedCycle, diagnostic);
	}

	[Fact]
	public void ConsecutiveCpuDmaconWritesDoNotRetryAnExecutorCommittedGrantPastRefresh()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(bus);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var lineStart = OutputRowStartCycle(row);
		bus.AdvanceDmaTo(lineStart);
		var refreshCycle = Enumerable.Range(0, AmigaConstants.A500PalCpuCyclesPerRasterLine / 2)
			.Select(slot => lineStart + (slot * AgnusChipSlotScheduler.SlotCycles))
			.First(cycle =>
				cycle >= lineStart + 8 &&
				AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(cycle));
		var cycle = refreshCycle - (4 * AgnusChipSlotScheduler.SlotCycles);

		bus.WriteWord(0x00DFF096, 0x0001, ref cycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteWord(0x00DFF096, 0x0002, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var writes = bus.CustomRegisterWrites
			.Where(write => write.Address == 0x096 && write.Value is 0x0001 or 0x0002)
			.ToArray();
		Assert.Equal(2, writes.Length);
		Assert.True(writes[0].Cycle < writes[1].Cycle);
		Assert.True(writes[1].Cycle <= refreshCycle);
	}

	[Fact]
	public void RefreshOnlyDisplayCoverageDoesNotAdvanceCausalCommitHorizon()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(bus);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var refreshCycle = Enumerable.Range(0, AmigaConstants.A500PalCpuCyclesPerRasterLine / 2)
			.Select(slot => lineStart + (slot * AgnusChipSlotScheduler.SlotCycles))
			.First(cycle =>
				cycle >= lineStart + 8 &&
				AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(cycle));

		bus.Display.AdvanceLiveDmaTo(refreshCycle);

		Assert.True(bus.Display.LiveCapturedThroughCycle >= refreshCycle);
		Assert.True(bus.Display.LiveCausalDisplayStateThroughCycle < refreshCycle);
		bus.Display.ScheduleWrite(
			refreshCycle - AgnusChipSlotScheduler.SlotCycles,
			0x0180,
			0x0123);
	}

	[Fact]
	public void FinalizedPresentationRowRejectsLateRegisterWrite()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		ConfigureLiveOneBitplaneDma(bus);
		var frameStop = bus.GetFrameStopCycle(0);
		var framebuffer = new uint[bus.Display.Width * bus.Display.Height];
		bus.Display.BeginPresentationFrame(
			new PresentationFrameTarget(framebuffer),
			0,
			frameStop);

		var thirdRowStart = OutputRowStartCycle(3);
		bus.AdvanceDmaTo(thirdRowStart);

		var firstRowStop = OutputRowStartCycle(1) - 1;
		Assert.True(bus.Display.LiveFinalizedPresentationThroughCycle >= firstRowStop);
		var exception = Assert.Throws<InvalidOperationException>(() =>
			bus.Display.ScheduleWrite(OutputRowStartCycle(0), 0x0180, 0x0123));
		Assert.Contains("finalized presentation horizon", exception.Message);
		bus.Display.AbortPresentationFrame();
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
			$"recorded={snapshot.LastRasterlinePlanEvents}, lineState={snapshot.LastRasterlinePlanLineStateEvents}, bitplane={snapshot.LastRasterlinePlanBitplaneFetchEvents}, lines={snapshot.LastPredictedRasterlinePlanLines}, matched={snapshot.LastPredictedRasterlinePlanMatchedLines}, mismatched={snapshot.LastPredictedRasterlinePlanMismatchedLines}, unsupported={snapshot.LastPredictedRasterlinePlanUnsupportedLines}, copper={snapshot.LastPredictedRasterlinePlanUnsupportedCopperLines}, pending={snapshot.LastPredictedRasterlinePlanUnsupportedPendingWriteLines}, sprite={snapshot.LastPredictedRasterlinePlanUnsupportedSpriteLines}, invalid={snapshot.LastPredictedRasterlinePlanUnsupportedInvalidStateLines}, overflow={snapshot.LastPredictedRasterlinePlanUnsupportedOverflowLines}");
		Assert.True(
			snapshot.LastPredictedRasterlinePlanMatchedLines > 0,
			$"lines={snapshot.LastPredictedRasterlinePlanLines}, matched={snapshot.LastPredictedRasterlinePlanMatchedLines}, mismatched={snapshot.LastPredictedRasterlinePlanMismatchedLines}");
		Assert.Equal(0, snapshot.LastPredictedRasterlinePlanMismatchedLines);
		Assert.True(snapshot.LastRowDmaPlansBuilt > 0);
		Assert.True(snapshot.LastRowDmaBitplaneEntriesExecuted > 0);
	}

	[Fact]
	public void RowDmaPlanSamplesChipRamAtFetchTime()
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
		Assert.True(snapshot.LastRowDmaBitplaneEntriesExecuted > 0);
		Assert.Equal(0xA55A, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
	}

	[Fact]
	public void RowDmaPlanExecutesSpriteRows()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		var lineStart = OutputRowStartCycle(AmigaConstants.PalLowResOverscanBorderY);
		var validationStop = lineStart + (3 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		ConfigureLiveSpriteDma(bus);

		bus.AdvanceDmaTo(validationStop);

		var snapshot = bus.Display.CaptureSnapshot();
		Assert.Equal(0, snapshot.LastPredictedRasterlinePlanUnsupportedSpriteLines);
		Assert.Equal(0, snapshot.LastPredictedRasterlinePlanMismatchedLines);
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
		Assert.True(bus.Display.TryGetCapturedBitplaneWord(
			AmigaConstants.PalLowResOverscanBorderY,
			plane: 0,
			word: 0,
			out var captured));
		Assert.Equal(0x0000, captured);
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
		var finalBlitterDma = bus.BusAccesses.Last(access =>
			access.Request.Requester == AmigaBusRequester.Blitter &&
			access.Request.Kind == AmigaBusAccessKind.Blitter);
		Assert.True(
			intreqWrite.Cycle >= expectedReadyCycle,
			$"Copper resumed before blitter completion: intreq={intreqWrite.Cycle}, blitter={expectedReadyCycle}");
		Assert.True(
			intreqWrite.Cycle >= finalBlitterDma.CompletedCycle,
			$"Copper resumed before the final physical blitter transfer: intreq={intreqWrite.Cycle}, dma={finalBlitterDma.CompletedCycle}");
	}

	[Fact]
	public void ConsecutiveSatisfiedCopperWaitsPreservePhysicalColorWriteIntervals()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true,
			enableLiveDisplayDma: true);
		const uint copperList = 0x2400;
		WriteCopperList(
			bus,
			copperList,
			(0x6241, 0xFFFE),
			(0x0180, 0x0F00),
			(0x6241, 0xFFFE),
			(0x6241, 0xFFFE),
			(0x0180, 0x0FF0),
			(0x6241, 0xFFFE),
			(0x6241, 0xFFFE),
			(0x6241, 0xFFFE),
			(0x0180, 0x00FF),
			(0x6241, 0xFFFE),
			(0x6241, 0xFFFE),
			(0x6241, 0xFFFE),
			(0x6241, 0xFFFE),
			(0x0180, 0x0000),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF08E, 0x2C71);
		bus.WriteWord(0x00DFF090, 0x2CD1);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF100, 0x5200);
		for (var plane = 0; plane < 5; plane++)
		{
			var pointerRegister = 0x00DFF0E0u + ((uint)plane * 4u);
			var pointer = 0x4000u + ((uint)plane * 0x1000u);
			bus.WriteWord(pointerRegister, (ushort)(pointer >> 16));
			bus.WriteWord(pointerRegister + 2, (ushort)pointer);
		}

		bus.WriteWord(0x00DFF096, 0x8380);

		bus.AdvanceDmaTo(101L * AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var writes = bus.Display.CopperDisplayWrites
			.Where(write => write.Address == 0x180)
			.Take(4)
			.ToArray();
		Assert.True(
			writes.Length == 4,
			$"writes={writes.Length}, copperAccesses={bus.BusAccesses.Count(access => access.Request.Requester == AmigaBusRequester.Copper)}, " +
			$"allWrites=[{string.Join(",", bus.Display.CopperDisplayWrites.Select(write => $"0x{write.Address:X3}=0x{write.Value:X4}@{write.Cycle}"))}]");
		Assert.Equal(new ushort[] { 0x0F00, 0x0FF0, 0x00FF, 0x0000 }, writes
			.Select(write => write.Value)
			.ToArray());
		Assert.Equal(44640, writes[0].Cycle);
		var intervals = writes
			.Zip(writes.Skip(1), (first, second) => second.Cycle - first.Cycle)
			.ToArray();
		var intervalAccesses = bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Copper &&
				access.GrantedCycle >= writes[0].Cycle &&
				access.GrantedCycle <= writes[3].Cycle)
			.Select(access =>
				$"{access.Request.Requester}/{access.Request.Kind}/0x{access.Request.Address:X6}:" +
				$"{access.Request.RequestedCycle}->{access.GrantedCycle}->{access.CompletedCycle}")
			.ToArray();
		Assert.True(
			new long[] { 56, 72, 96 }.SequenceEqual(intervals),
			$"writes=[{string.Join(",", writes.Select(write => write.Cycle))}], intervals=[{string.Join(",", intervals)}], " +
			$"accesses=[{string.Join(";", intervalAccesses)}]");
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
			AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles;
		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & AmigaConstants.IntreqCopper);
		Assert.Equal(3, bus.Paula.GetHighestPendingInterruptLevel());
		Assert.Equal(0, bus.Paula.GetHighestCpuVisibleInterruptLevel(cpuVisibleCycle - 1));
		Assert.Equal(3, bus.Paula.GetHighestCpuVisibleInterruptLevel(cpuVisibleCycle));
	}

	[Fact]
	public void CopperWaitFf81Level1IntreqWriteDocumentsBeamPosition()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		const uint copperList = 0x2400;
		const ushort intreqLevel1 = 0x0004;
		WriteCopperList(
			bus,
			copperList,
			(0x1001, 0xFFFE),
			(0x09C, 0x8008),
			(0x4001, 0xFFFE),
			(0x100, 0x2200),
			(0xFF81, 0xFFFE),
			(0x09C, (ushort)(0x8000 | intreqLevel1)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | intreqLevel1));
		bus.WriteWord(0x00DFF096, 0x8280);

		var line256Start = 256L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		bus.AdvanceDmaTo(line256Start + AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			write.Value == (0x8000 | intreqLevel1));
		var beam = bus.GetBeamPosition(intreqWrite.Cycle);
		var copperAccesses = bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Copper &&
				access.GrantedCycle >= intreqWrite.Cycle - 64 &&
				access.GrantedCycle <= intreqWrite.Cycle + 32)
			.ToArray();
		var accessText = string.Join(
			"; ",
			copperAccesses.Select(access =>
			{
				var grant = bus.GetBeamPosition(access.GrantedCycle);
				return $"{access.Request.Kind}/0x{access.Request.Address:X6} " +
					$"req={access.Request.RequestedCycle - line256Start},grant={access.GrantedCycle - line256Start}/" +
					$"v{grant.BeamLine:X3}h{grant.BeamHorizontal:X2}";
			}));
		var diagnostic =
			$"intreq={intreqWrite.Cycle}/deltaToLine256={intreqWrite.Cycle - line256Start}/" +
			$"v{beam.BeamLine:X3}h{beam.BeamHorizontal:X2}, " +
			$"accesses=[{accessText}]";

		Assert.Equal(line256Start - 182, intreqWrite.Cycle);
		Assert.Equal(0x0FF, beam.BeamLine);
		Assert.Equal(0x88, beam.BeamHorizontal);
		Assert.Equal(4, copperAccesses.Length);
		Assert.Equal(new long[] { -186, -182, -178, -174 }, copperAccesses
			.Select(access => access.GrantedCycle - line256Start)
			.ToArray());
		Assert.Contains("0x002416", diagnostic);
	}

	[Fact]
	public void CopperWaitFf81Level1IntreqBecomesCpuVisibleAfterSoftwareRegisterDelay()
	{
		var bus = new AmigaBus(
			captureBusAccesses: true,
			enableLiveAgnusDma: true);
		const uint copperList = 0x2400;
		const ushort intreqLevel1 = 0x0004;
		WriteCopperList(
			bus,
			copperList,
			(0x1001, 0xFFFE),
			(0x09C, 0x8008),
			(0x4001, 0xFFFE),
			(0x100, 0x2200),
			(0xFF81, 0xFFFE),
			(0x09C, (ushort)(0x8000 | intreqLevel1)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | intreqLevel1));
		bus.WriteWord(0x00DFF096, 0x8280);

		var line256Start = 256L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		bus.AdvanceDmaTo(line256Start + AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			write.Value == (0x8000 | intreqLevel1));
		var cpuVisibleCycle = intreqWrite.Cycle +
			AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles;
		var intreqBeam = bus.GetBeamPosition(intreqWrite.Cycle);
		var visibleBeam = bus.GetBeamPosition(cpuVisibleCycle);
		var diagnostic =
			$"intreq={intreqWrite.Cycle}/delta={intreqWrite.Cycle - line256Start}/" +
			$"v{intreqBeam.BeamLine:X3}h{intreqBeam.BeamHorizontal:X2}, " +
			$"visible={cpuVisibleCycle}/delta={cpuVisibleCycle - line256Start}/" +
			$"v{visibleBeam.BeamLine:X3}h{visibleBeam.BeamHorizontal:X2}, " +
			$"delay={cpuVisibleCycle - intreqWrite.Cycle}";

		Assert.NotEqual(0, bus.ReadWord(0x00DFF01E) & intreqLevel1);
		Assert.Equal(1, bus.Paula.GetHighestPendingInterruptLevel());
		Assert.Equal(line256Start - 182, intreqWrite.Cycle);
		Assert.Equal(line256Start - 180, cpuVisibleCycle);
		Assert.Equal(
			AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles,
			cpuVisibleCycle - intreqWrite.Cycle);
		Assert.Equal(0, bus.Paula.GetHighestCpuVisibleInterruptLevel(cpuVisibleCycle - 1));
		Assert.Equal(1, bus.Paula.GetHighestCpuVisibleInterruptLevel(cpuVisibleCycle));
		Assert.Equal(0x0FF, intreqBeam.BeamLine);
		Assert.Equal(0x88, intreqBeam.BeamHorizontal);
		Assert.Equal(0x0FF, visibleBeam.BeamLine);
		Assert.Equal(0x89, visibleBeam.BeamHorizontal);
		Assert.Contains("delay=2", diagnostic);
	}

	[Fact]
	public void LiveCopperIntreqDispatchesAtCpuBoundaryAfterRecognitionDelay()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const uint copperList = 0x2400;
		const uint handlerAddress = 0x2000;
		WriteCopperList(
			bus,
			copperList,
			(0x0001, 0x00FE),
			(0x009C, (ushort)(0x8000 | AmigaConstants.IntreqCopper)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71);
		Write(bus.ChipRam, (int)handlerAddress, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71);
		bus.WriteLong((24u + 3u) * 4u, handlerAddress);
		machine.Cpu.Reset(0x1000, 0x3000);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqCopper));
		bus.WriteWord(0x00DFF096, 0x8280);

		for (var cycle = 0L;
			cycle <= AmigaConstants.A500PalCpuCyclesPerRasterLine &&
			!bus.CustomRegisterWrites.Any(write =>
				write.Address == 0x09C &&
				(write.Value & AmigaConstants.IntreqCopper) != 0);
			cycle += 2)
		{
			bus.AdvanceDmaTo(cycle);
		}

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			(write.Value & AmigaConstants.IntreqCopper) != 0);
		var cpuVisibleCycle = intreqWrite.Cycle +
			AmigaConstants.A500CopperIntreqDelayCpuCycles +
			AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles;
		machine.Cpu.State.Cycles = cpuVisibleCycle - 1;
		Assert.False(machine.DispatchPendingHardwareInterrupt());
		Assert.Equal(0x1000u, machine.Cpu.State.ProgramCounter);

		machine.Cpu.State.Cycles = cpuVisibleCycle;
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(machine.Cpu);
		machine.Cpu.ExecuteInstruction();
		var acceptanceCycle = machine.Cpu.State.Cycles;
		Assert.True(recognition.LastInterruptSampleCycle >= cpuVisibleCycle);
		var phaseCountBeforeDispatch = bus.CpuBusPhases.Count;
		Assert.True(machine.DispatchPendingHardwareInterrupt());

		Assert.Equal(3, (machine.Cpu.State.StatusRegister >> 8) & 7);
		Assert.Equal(handlerAddress, machine.Cpu.State.ProgramCounter);
		Assert.Equal(acceptanceCycle + 52, machine.Cpu.State.Cycles);
		Assert.Equal(3, bus.Paula.GetHighestCpuVisibleInterruptLevel(cpuVisibleCycle));
		var interruptPhases = bus.CpuBusPhases
			.Skip(phaseCountBeforeDispatch)
			.ToArray();
		var timeline = BuildCpuBusPhaseTimeline(interruptPhases, acceptanceCycle);
		var expected =
			"pc=0x1000,CpuDataWrite,0x002FFE,r+14..+18,g+16 | " +
			"pc=0x1000,CpuInterruptAcknowledge,0xFFFFF7,r+18..+24 | " +
			"pc=0x1000,CpuDataWrite,0x002FFA,r+28..+32,g+30 | " +
			"pc=0x1000,CpuDataWrite,0x002FFC,r+32..+36,g+34 | " +
			"pc=0x1000,CpuDataRead,0x00006C,r+36..+44,g+42 | " +
			"pc=0x1000,CpuInstructionFetch,0x002000,r+44..+48,g+46 | " +
			"pc=0x1000,CpuInstructionFetch,0x002002,r+48..+52,g+50";
		Assert.True(expected == timeline, timeline);
	}

	[Fact]
	public void VAmigaTsProbe10Irq1PrologueSamplesAndStoresVhposrCausally()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const uint copperList = 0x2400;
		const uint handlerAddress = 0x2000;
		const int valuesAddress = 0x3000;
		const ushort intreqLevel1 = 0x0004;
		WriteCopperList(
			bus,
			copperList,
			(0x0001, 0x00FE),
			(0x009C, (ushort)(0x8000 | intreqLevel1)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71);
		Write(bus.ChipRam, (int)handlerAddress, CreateVAmigaTsProbe10Irq1Handler(valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(0x1000, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		machine.Cpu.State.A[3] = 0x00DFF006;
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | intreqLevel1));
		bus.WriteWord(0x00DFF096, 0x8280);

		bus.AdvanceDmaTo(AmigaConstants.A500PalCpuCyclesPerRasterLine);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			write.Value == (0x8000 | intreqLevel1));
		var cpuVisibleCycle = intreqWrite.Cycle + AmigaConstants.A500IntreqToIplDelayCpuCycles;
		machine.Cpu.State.Cycles = cpuVisibleCycle;
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(machine.Cpu);
		machine.Cpu.ExecuteInstruction();
		Assert.True(recognition.LastInterruptSampleCycle >= cpuVisibleCycle);
		Assert.True(machine.DispatchPendingHardwareInterrupt());

		for (var i = 0; i < 40; i++)
		{
			machine.Cpu.ExecuteInstruction();
		}

		var reads = bus.CustomRegisterReads
			.Where(read => read.Address == 0x006 && read.Kind == AmigaBusAccessKind.CpuDataRead)
			.Take(16)
			.ToArray();
		var actual = reads.Select(read => read.Value).ToArray();
		var stored = ReadChipWords(bus.ChipRam, valuesAddress, reads.Length);
		var deltas = reads.Skip(1)
			.Zip(reads, (current, previous) => current.SampleCycle - previous.SampleCycle)
			.ToArray();
		var colorWrites = bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180)
			.ToArray();
		var intreqClears = bus.CustomRegisterWrites
			.Where(write => write.Address == 0x09C && write.Value == 0x3FFF)
			.ToArray();
		var phaseOrigin = cpuVisibleCycle;
		var phases = bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.RequestedCycle >= phaseOrigin &&
				phase.CpuPhase.RequestedCycle <= (reads.Length == 0 ? phaseOrigin + 200 : reads[0].CompletedCycle + 80))
			.ToArray();
		var diagnostic =
			$"intreqWrite={intreqWrite.Cycle}, visible={cpuVisibleCycle}, " +
			$"colorWrites=[{string.Join(",", colorWrites.Select(write => $"0x{write.Value:X4}@{write.Cycle}"))}], " +
			$"intreqClears=[{string.Join(",", intreqClears.Select(write => $"0x{write.Value:X4}@{write.Cycle}"))}], " +
			$"values=[{string.Join(",", actual.Select(value => $"0x{value:X4}"))}], " +
			$"deltas=[{string.Join(",", deltas)}], " +
			$"phases={BuildCpuBusPhaseTimeline(phases, phaseOrigin)}";

		Assert.Equal(16, reads.Length);
		Assert.Equal(actual, stored);
		Assert.All(reads, read =>
		{
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(EncodeVhposr(bus.GetBeamPosition(read.SampleCycle)), read.Value);
		});
		Assert.All(deltas, delta => Assert.True(delta >= 20, diagnostic));
		Assert.True(colorWrites.Any(write => write.Value == 0x0300), diagnostic);
		Assert.True(intreqClears.Length >= 1, diagnostic);
	}

	[Fact]
	public void VAmigaTsProbe10Irq1AtRasterLine38PreservesDisplayDmaPriority()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const uint copperList = 0x2400;
		const uint handlerAddress = 0x2000;
		const int valuesAddress = 0x3000;
		const ushort intreqLevel1 = 0x0004;
		var slotAudit = new List<AgnusSlotScheduleAuditEntry>();
		bus.SetSlotScheduleAuditSink(slotAudit.Add);
		WriteCopperList(
			bus,
			copperList,
			(0x1001, 0x00FE),
			(0x02A, 0x8001),
			(0x4001, 0x00FE),
			(0x100, 0x2200),
			(0x3885, 0xFFFE),
			(0x09C, (ushort)(0x8000 | intreqLevel1)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71);
		Write(bus.ChipRam, (int)handlerAddress, CreateVAmigaTsProbe10Irq1Handler(valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(0x1000, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		machine.Cpu.State.A[3] = 0x00DFF006;
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | intreqLevel1));
		bus.WriteWord(0x00DFF096, 0x8380);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x4000);
		bus.WriteWord(0x00DFF0E4, 0x0000);
		bus.WriteWord(0x00DFF0E6, 0x5000);

		var targetLineCycle = AmigaConstants.A500PalCpuCyclesPerFrame +
			(0x39L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		bus.AdvanceDmaTo(targetLineCycle);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			write.Value == (0x8000 | intreqLevel1));
		var cpuVisibleCycle = intreqWrite.Cycle + AmigaConstants.A500IntreqToIplDelayCpuCycles;
		machine.Cpu.State.Cycles = cpuVisibleCycle;
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(machine.Cpu);
		machine.Cpu.ExecuteInstruction();
		var acceptanceCycle = machine.Cpu.State.Cycles;
		Assert.True(recognition.LastInterruptSampleCycle >= cpuVisibleCycle);
		Assert.True(machine.DispatchPendingHardwareInterrupt());
		Assert.Equal(acceptanceCycle + 52, machine.Cpu.State.Cycles);

		for (var i = 0; i < 40; i++)
		{
			machine.Cpu.ExecuteInstruction();
		}

		var reads = bus.CustomRegisterReads
			.Where(read => read.Address == 0x006 && read.Kind == AmigaBusAccessKind.CpuDataRead)
			.Take(16)
			.ToArray();
		var actual = reads.Select(read => read.Value).ToArray();
		var deltas = reads.Skip(1)
			.Zip(reads, (current, previous) => current.SampleCycle - previous.SampleCycle)
			.ToArray();
		var bitplaneSlots = bus.BusAccesses.Count(access =>
			access.Request.Requester == AmigaBusRequester.Bitplane &&
			access.GrantedCycle >= intreqWrite.Cycle - 128 &&
			(reads.Length == 0 || access.GrantedCycle <= reads[^1].CompletedCycle + 64));
		var bitplaneGrants = bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.GrantedCycle >= cpuVisibleCycle &&
				access.GrantedCycle <= cpuVisibleCycle + 220)
			.Select(access => AgnusHrmOcsSlotTable.GetHorizontal(access.GrantedCycle))
			.Take(48)
			.ToArray();
		var auditSlots = slotAudit
			.Where(entry =>
				entry.SlotCycle >= cpuVisibleCycle &&
				entry.SlotCycle <= cpuVisibleCycle + 180)
			.Select(entry =>
				$"+{entry.SlotCycle - cpuVisibleCycle}/0x{AgnusHrmOcsSlotTable.GetHorizontal(entry.SlotCycle):X2}:{entry.Owner}/{entry.Requester}" +
				(entry.ReplacedExisting ? $"> {entry.ReplacedOwner}" : string.Empty))
			.ToArray();
		var phaseOrigin = cpuVisibleCycle;
		var phases = bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.RequestedCycle >= phaseOrigin &&
				phase.CpuPhase.RequestedCycle <= (reads.Length == 0 ? phaseOrigin + 240 : reads[1].CompletedCycle + 80))
			.ToArray();
		var diagnostic =
			$"intreqWrite={intreqWrite.Cycle}, visible={cpuVisibleCycle}, bitplaneSlots={bitplaneSlots}, " +
			$"bitplaneH=[{string.Join(",", bitplaneGrants.Select(horizontal => $"0x{horizontal:X2}"))}], " +
			$"audit=[{string.Join(",", auditSlots)}], " +
			$"values=[{string.Join(",", actual.Select(value => $"0x{value:X4}"))}], " +
			$"deltas=[{string.Join(",", deltas)}], " +
			$"phases={BuildCpuBusPhaseTimeline(phases, phaseOrigin)}";

		Assert.Equal(16, reads.Length);
		Assert.True(bitplaneSlots > 0, diagnostic);
		Assert.All(reads, read =>
		{
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(EncodeVhposr(bus.GetBeamPosition(read.SampleCycle)), read.Value);
		});
		Assert.All(deltas, delta => Assert.True(delta > 0, diagnostic));
		Assert.True(phases.Any(phase =>
			phase.CpuPhase.CompletedCycle - phase.CpuPhase.RequestedCycle > (2 * AgnusChipSlotScheduler.SlotCycles)), diagnostic);
		Assert.Contains(auditSlots, slot => slot.Contains(":Bitplane/Bitplane", StringComparison.Ordinal));
		Assert.DoesNotContain("> Cpu", diagnostic);
	}

	[Fact]
	public void VAmigaTsProbe10Irq1AtRasterLine38StoresSampledVhposrLatches()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const uint copperList = 0x2400;
		const uint handlerAddress = 0x2000;
		const int valuesAddress = 0x3000;
		const ushort intreqLevel1 = 0x0004;
		WriteCopperList(
			bus,
			copperList,
			(0x1001, 0x00FE),
			(0x02A, 0x8001),
			(0x4001, 0x00FE),
			(0x100, 0x2200),
			(0x3885, 0xFFFE),
			(0x09C, (ushort)(0x8000 | intreqLevel1)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71);
		Write(bus.ChipRam, (int)handlerAddress, CreateVAmigaTsProbe10Irq1Handler(valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(0x1000, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		machine.Cpu.State.A[3] = 0x00DFF006;
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | intreqLevel1));
		bus.WriteWord(0x00DFF096, 0x8380);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x4000);
		bus.WriteWord(0x00DFF0E4, 0x0000);
		bus.WriteWord(0x00DFF0E6, 0x5000);

		var targetLineCycle = AmigaConstants.A500PalCpuCyclesPerFrame +
			(0x39L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		bus.AdvanceDmaTo(targetLineCycle);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			(write.Value & intreqLevel1) != 0);
		var cpuVisibleCycle = intreqWrite.Cycle + AmigaConstants.A500IntreqToIplDelayCpuCycles;
		machine.Cpu.State.Cycles = cpuVisibleCycle;
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(machine.Cpu);
		machine.Cpu.ExecuteInstruction();
		Assert.True(recognition.LastInterruptSampleCycle >= cpuVisibleCycle);
		Assert.True(machine.DispatchPendingHardwareInterrupt());

		for (var i = 0; i < 40; i++)
		{
			machine.Cpu.ExecuteInstruction();
		}

		const int valueCount = 16;
		var stored = ReadChipWords(bus.ChipRam, valuesAddress, valueCount);
		var reads = bus.CustomRegisterReads
			.Where(read => read.Address == 0x006 && read.Kind == AmigaBusAccessKind.CpuDataRead)
			.Take(valueCount)
			.ToArray();
		var readValues = reads.Select(read => read.Value).ToArray();
		var storeWrites = bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
				phase.CpuPhase.Address >= valuesAddress &&
				phase.CpuPhase.Address < valuesAddress + (valueCount * 2))
			.ToArray();
		var diagnostic =
			$"intreqWrite={intreqWrite.Cycle}, visible={cpuVisibleCycle}, " +
			$"stored=[{string.Join(",", stored.Select(value => $"0x{value:X4}"))}], " +
			$"reads=[{string.Join(",", readValues.Select(value => $"0x{value:X4}"))}], " +
			$"stores={BuildCpuBusPhaseTimeline(storeWrites, cpuVisibleCycle)}";

		Assert.Equal(valueCount, reads.Length);
		Assert.Equal(readValues, stored);
		Assert.Equal(valueCount, storeWrites.Length);
		for (var i = 0; i < valueCount; i++)
		{
			Assert.Equal(reads[i].GrantedCycle, reads[i].SampleCycle);
			Assert.Equal(EncodeVhposr(bus.GetBeamPosition(reads[i].SampleCycle)), reads[i].Value);
			Assert.Equal((uint)(valuesAddress + (i * 2)), storeWrites[i].CpuPhase.Address);
			Assert.True(storeWrites[i].CpuPhase.RequestedCycle >= reads[i].CompletedCycle, diagnostic);
		}
	}

	[Fact]
	public void VAmigaTsProbe10TwoPlaneHiresLeavesCopperSlotBeforeIrqVisibility()
	{
		static (
			long IntreqCycle,
			long VisibleCycle,
			long EntryCycles,
			long SampleAfterVisible,
			long Bplcon0DisableCycle,
			ushort Value,
			int SampleLine,
			int SampleHorizontal,
			int PreInterruptBitplaneGrants,
			int BitplaneGrants,
			string Boundaries,
			string Phases) Run(bool enableBitplaneDma)
		{
			using var machine = new Machine(MachineOptions
				.ForProfile(MachineProfile.A500Pal512KBoot)
				.WithLiveAgnusDma(true)
				.WithBusAccessLogging(true));
			var bus = machine.Bus;
			const uint copperList = 0x2400;
			const uint handlerAddress = 0x2000;
			const int valuesAddress = 0x3000;
			const ushort intreqLevel1 = 0x0004;
			WriteCopperList(
				bus,
				copperList,
				(0x3885, 0xFFFE),
				(0x09C, (ushort)(0x8000 | intreqLevel1)),
				(0x100, 0x0200),
				(0xFFFF, 0xFFFE));
			SetCopperPointer(bus, list: 1, copperList);
			Write(bus.ChipRam, 0x1000, 0x60, 0xFE);
			Write(bus.ChipRam, (int)handlerAddress, CreateVAmigaTsProbe10Irq1Handler(valuesAddress));
			bus.WriteLong((24u + 1u) * 4u, handlerAddress);
			machine.Cpu.Reset(0x1000, 0x3F00);
			machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
			machine.Cpu.State.A[1] = 0x00DFF000;
			bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | intreqLevel1));
			bus.WriteWord(0x00DFF096, enableBitplaneDma ? (ushort)0x8380 : (ushort)0x8280);
			bus.WriteWord(0x00DFF092, 0x0038);
			bus.WriteWord(0x00DFF094, 0x00D0);
			bus.WriteWord(0x00DFF0E0, 0x0000);
			bus.WriteWord(0x00DFF0E2, 0x4000);
			bus.WriteWord(0x00DFF0E4, 0x0000);
			bus.WriteWord(0x00DFF0E6, 0x5000);
			// Establish the display state causally before the measured line. The Copper's
			// later BPLCON0 write is part of the probe sequence, not reliable setup for
			// comparing DMACON's bitplane enable against the disabled control run.
			bus.WriteWord(0x00DFF100, 0x2200);

			var targetLineCycle = 0x39L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
			var startCycle = targetLineCycle - 512;
			bus.AdvanceDmaTo(startCycle);
			machine.Cpu.State.Cycles = startCycle;
			long interruptEntryStartCycle = -1;
			long entryStopCycle = -1;
			var boundaries = new List<(uint Pc, long Start, long Stop)>();

			for (var i = 0; i < 400 && bus.CustomRegisterReads.All(read => read.Address != 0x006); i++)
			{
				bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
				var beforeDispatch = machine.Cpu.State.Cycles;
				if (machine.DispatchPendingHardwareInterrupt())
				{
					interruptEntryStartCycle = beforeDispatch;
					entryStopCycle = machine.Cpu.State.Cycles;
				}

				var pc = machine.Cpu.State.ProgramCounter;
				var start = machine.Cpu.State.Cycles;
				machine.Cpu.ExecuteInstruction();
				if (pc >= handlerAddress && pc < handlerAddress + 0x40)
				{
					boundaries.Add((pc, start, machine.Cpu.State.Cycles));
				}
			}

			var intreqWrite = bus.CustomRegisterWrites.Last(write =>
				write.Address == 0x09C && write.Value == (0x8000 | intreqLevel1));
			var visibleCycle = intreqWrite.Cycle + AmigaConstants.A500IntreqToIplDelayCpuCycles;
			var firstRead = bus.CustomRegisterReads.First(read => read.Address == 0x006);
			var bplcon0Disable = bus.CustomRegisterWrites.LastOrDefault(write =>
				write.Address == 0x100 && write.Value == 0x0200);
			var sampleBeam = bus.GetBeamPosition(firstRead.SampleCycle);
			var phaseStart = visibleCycle;
			var phases = bus.CpuBusPhases
				.Where(phase =>
					phase.CpuPhase.RequestedCycle >= phaseStart &&
					phase.CpuPhase.RequestedCycle <= firstRead.CompletedCycle)
				.ToArray();
			return (
				intreqWrite.Cycle,
				visibleCycle,
				entryStopCycle - interruptEntryStartCycle,
				firstRead.SampleCycle - visibleCycle,
				bplcon0Disable.Cycle,
				firstRead.Value,
				sampleBeam.BeamLine,
				sampleBeam.BeamHorizontal,
				bus.BusAccesses.Count(access =>
					access.Request.Requester == AmigaBusRequester.Bitplane &&
					access.GrantedCycle >= intreqWrite.Cycle - 256 &&
					access.GrantedCycle < intreqWrite.Cycle),
				bus.BusAccesses.Count(access =>
					access.Request.Requester == AmigaBusRequester.Bitplane &&
					access.GrantedCycle >= visibleCycle &&
					access.GrantedCycle <= firstRead.CompletedCycle),
				string.Join(",", boundaries.Select(boundary =>
					$"0x{boundary.Pc:X4}:{boundary.Start - visibleCycle}-{boundary.Stop - visibleCycle}")),
				BuildCpuBusPhaseTimeline(phases, visibleCycle));
		}

		var noBitplanes = Run(enableBitplaneDma: false);
		var bitplanes = Run(enableBitplaneDma: true);
		var diagnostic =
			$"noBpl intreq={noBitplanes.IntreqCycle}, visible={noBitplanes.VisibleCycle}, " +
			$"entry={noBitplanes.EntryCycles}, sample=+{noBitplanes.SampleAfterVisible}/" +
			$"bploff={noBitplanes.Bplcon0DisableCycle}," +
			$"0x{noBitplanes.Value:X4}@v{noBitplanes.SampleLine}h{noBitplanes.SampleHorizontal}, preBpl={noBitplanes.PreInterruptBitplaneGrants}, bplgrants={noBitplanes.BitplaneGrants}, " +
			$"boundaries=[{noBitplanes.Boundaries}], phases=[{noBitplanes.Phases}] | " +
			$"bpl intreq={bitplanes.IntreqCycle}, visible={bitplanes.VisibleCycle}, " +
			$"entry={bitplanes.EntryCycles}, sample=+{bitplanes.SampleAfterVisible}/" +
			$"bploff={bitplanes.Bplcon0DisableCycle}," +
			$"0x{bitplanes.Value:X4}@v{bitplanes.SampleLine}h{bitplanes.SampleHorizontal}, preBpl={bitplanes.PreInterruptBitplaneGrants}, bplgrants={bitplanes.BitplaneGrants}, " +
			$"boundaries=[{bitplanes.Boundaries}], phases=[{bitplanes.Phases}]";

		Assert.Equal(noBitplanes.IntreqCycle, bitplanes.IntreqCycle);
		Assert.Equal(noBitplanes.VisibleCycle, bitplanes.VisibleCycle);
		Assert.Equal(0, noBitplanes.PreInterruptBitplaneGrants);
		Assert.True(bitplanes.PreInterruptBitplaneGrants > 0, diagnostic);
		Assert.Equal(noBitplanes.EntryCycles, bitplanes.EntryCycles);
		Assert.Equal(noBitplanes.SampleAfterVisible, bitplanes.SampleAfterVisible);
		Assert.Equal(0, noBitplanes.BitplaneGrants);
		Assert.Equal(0, bitplanes.BitplaneGrants);
		Assert.Equal(0, noBitplanes.Bplcon0DisableCycle);
		Assert.Equal(0, bitplanes.Bplcon0DisableCycle);
	}

	[Theory(Skip = "Synthetic late-start IRQ harness dispatches outside the source CPU/Copper phase; retained only as a diagnostic fixture.")]
	[InlineData(0x002000, 0x003000)]
	[InlineData(0x07035E, 0x07060C)]
	public void VAmigaTsProbe10IdleLoopCopperIrq1BoundaryIsIndependentOfHandlerAddress(int handlerAddress, int valuesAddress)
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		var slotAudit = new List<AgnusSlotScheduleAuditEntry>();
		bus.SetSlotScheduleAuditSink(slotAudit.Add);
		const uint copperList = 0x2400;
		const int interruptedLoopAddress = 0x070352;
		const ushort intreqLevel1 = 0x0004;
		WriteCopperList(
			bus,
			copperList,
			(0x1001, 0xFFFE),
			(0x09C, 0x8008),
			(0x4001, 0xFFFE),
			(0x100, 0x2200),
			(0xFFDF, 0xFFFE),
			(0x1001, 0xFFFE),
			(0x02A, 0x8001),
			(0x3885, 0xFFFE),
			(0x09C, (ushort)(0x8000 | intreqLevel1)),
			(0x100, 0x0200),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		Write(bus.ChipRam, interruptedLoopAddress, new byte[] { 0x60, 0xFE }); // done: BRA.S done
		Write(bus.ChipRam, handlerAddress, CreateVAmigaTsProbe10SourceIrq1Handler(handlerAddress, valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, (uint)handlerAddress);
		machine.Cpu.Reset(interruptedLoopAddress, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		machine.Cpu.State.A[3] = 0x00DFF006;
		bus.WriteWord(0x00DFF09A, 0xC004);
		bus.WriteWord(0x00DFF096, 0x8080);
		bus.WriteWord(0x00DFF096, 0x8100);
		bus.WriteWord(0x00DFF096, 0x8200);
		bus.WriteWord(0x00DFF096, 0x8400);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF08E, 0x2C81);
		bus.WriteWord(0x00DFF090, 0xF4C1);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x4000);
		bus.WriteWord(0x00DFF0E4, 0x0000);
		bus.WriteWord(0x00DFF0E6, 0x5000);

		var targetCycle = AmigaConstants.A500PalCpuCyclesPerFrame;
		bus.AdvanceDmaTo(targetCycle - 512);
		machine.Cpu.State.Cycles = targetCycle - 512;

		var prefetch = Assert.IsAssignableFrom<IM68000PrefetchDiagnostics>(machine.Cpu);
		var interruptBoundaries = new List<(long BeforeCycle, long AfterCycle, M68000PrefetchDiagnosticState Before, M68000PrefetchDiagnosticState After)>();
		var handlerBoundaries = new List<(uint Pc, long Start, long Stop, M68000PrefetchDiagnosticState Before, M68000PrefetchDiagnosticState After)>();
		var executed = 0;
		while (executed < 400 &&
			ReadChipWords(bus.ChipRam, valuesAddress, 16).All(value => value == 0))
		{
			bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
			var beforeInterrupt = prefetch.CapturePrefetchDiagnosticState();
			var beforeInterruptCycle = machine.Cpu.State.Cycles;
			if (machine.DispatchPendingHardwareInterrupt())
			{
				interruptBoundaries.Add((
					beforeInterruptCycle,
					machine.Cpu.State.Cycles,
					beforeInterrupt,
					prefetch.CapturePrefetchDiagnosticState()));
			}

			var pc = machine.Cpu.State.ProgramCounter;
			var instructionStart = machine.Cpu.State.Cycles;
			var beforeInstruction = prefetch.CapturePrefetchDiagnosticState();
			machine.Cpu.ExecuteInstruction();
			if (pc >= (uint)handlerAddress && pc < (uint)handlerAddress + 0x40)
			{
				handlerBoundaries.Add((
					pc,
					instructionStart,
					machine.Cpu.State.Cycles,
					beforeInstruction,
					prefetch.CapturePrefetchDiagnosticState()));
			}
			executed++;
		}
		for (var i = 0; i < 40; i++)
		{
			bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
			_ = machine.DispatchPendingHardwareInterrupt();
			machine.Cpu.ExecuteInstruction();
		}

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			write.Value == (0x8000 | intreqLevel1));
		var cpuVisibleCycle = intreqWrite.Cycle + AmigaConstants.A500IntreqToIplDelayCpuCycles;

		var reference = VAmigaTsProbe10ExpectedValues();
		var interrupt = Assert.Single(interruptBoundaries);
		var intreqBeam = bus.GetBeamPosition(intreqWrite.Cycle);
		var intreqFetches = bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Copper &&
				(access.Request.Address == copperList + 32 || access.Request.Address == copperList + 34))
			.OrderBy(access => access.Request.Address)
			.ToArray();
		var intreqFetchBeams = intreqFetches
			.Select(access => bus.GetBeamPosition(access.GrantedCycle))
			.ToArray();
		var visibleBeam = bus.GetBeamPosition(cpuVisibleCycle);
		var acceptanceBeam = bus.GetBeamPosition(interrupt.BeforeCycle);
		var entryBeam = bus.GetBeamPosition(interrupt.AfterCycle);
		var stored = ReadChipWords(bus.ChipRam, valuesAddress, reference.Length);
		var reads = bus.CustomRegisterReads
			.Where(read =>
				read.Address == 0x006 &&
				read.Kind == AmigaBusAccessKind.CpuDataRead &&
				read.SampleCycle >= interrupt.AfterCycle)
			.Take(reference.Length)
			.ToArray();
		var bitplaneGrants = bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.GrantedCycle >= cpuVisibleCycle &&
				access.GrantedCycle <= cpuVisibleCycle + 260)
			.Select(access => AgnusHrmOcsSlotTable.GetHorizontal(access.GrantedCycle))
			.ToArray();
		var auditSlots = slotAudit
			.Where(entry =>
				entry.SlotCycle >= cpuVisibleCycle &&
				entry.SlotCycle <= cpuVisibleCycle + 220)
			.Select(entry =>
				$"+{entry.SlotCycle - cpuVisibleCycle}/0x{AgnusHrmOcsSlotTable.GetHorizontal(entry.SlotCycle):X2}:{entry.Owner}/{entry.Requester}/{entry.Target}/{entry.Size}" +
				(entry.ReplacedExisting ? $"> {entry.ReplacedOwner}" : string.Empty))
			.ToArray();
		var diagnostic =
			$"handler=0x{handlerAddress:X6}, values=0x{valuesAddress:X6}, " +
			$"intreqWrite={intreqWrite.Cycle}/v{intreqBeam.BeamLine:X3}h{intreqBeam.BeamHorizontal:X2}, " +
			$"visible={cpuVisibleCycle}/v{visibleBeam.BeamLine:X3}h{visibleBeam.BeamHorizontal:X2}, " +
			$"accept=v{acceptanceBeam.BeamLine:X3}h{acceptanceBeam.BeamHorizontal:X2}, " +
			$"entry=v{entryBeam.BeamLine:X3}h{entryBeam.BeamHorizontal:X2}, " +
			$"reference=[{string.Join(",", reference.Select(value => $"0x{value:X4}"))}], " +
			$"stored=[{string.Join(",", stored.Select(value => $"0x{value:X4}"))}], " +
			$"reads=[{string.Join(",", reads.Select(read => $"0x{read.Value:X4}@{read.SampleCycle}"))}], " +
			$"bitplaneH=[{string.Join(",", bitplaneGrants.Select(horizontal => $"0x{horizontal:X2}"))}], " +
			$"audit=[{string.Join(",", auditSlots)}], " +
			$"irq=[{string.Join(";", interruptBoundaries.Select(boundary => $"{boundary.BeforeCycle - cpuVisibleCycle}->{boundary.AfterCycle - cpuVisibleCycle}:{FormatPrefetchDiagnostic(boundary.Before, cpuVisibleCycle)}=>{FormatPrefetchDiagnostic(boundary.After, cpuVisibleCycle)}"))}], " +
			$"handlerBounds=[{string.Join(";", handlerBoundaries.Take(5).Select(boundary => $"0x{boundary.Pc:X6}:{boundary.Start - cpuVisibleCycle}->{boundary.Stop - cpuVisibleCycle}:{FormatPrefetchDiagnostic(boundary.Before, cpuVisibleCycle)}=>{FormatPrefetchDiagnostic(boundary.After, cpuVisibleCycle)}"))}], " +
			$"phases={BuildCpuBusPhaseTimeline(bus.CpuBusPhases.Where(phase => phase.CpuPhase.RequestedCycle >= cpuVisibleCycle && phase.CpuPhase.RequestedCycle <= reads[1].CompletedCycle + 12).ToArray(), cpuVisibleCycle)}";

		Assert.True(reference.Length == reads.Length, diagnostic);
		Assert.True(reference.SequenceEqual(stored), diagnostic);
		Assert.True(interrupt.BeforeCycle - cpuVisibleCycle == 8, diagnostic);
		Assert.True(interrupt.AfterCycle - cpuVisibleCycle == 52, diagnostic);
		Assert.True(reads[0].SampleCycle - cpuVisibleCycle == 98, diagnostic);
		Assert.Equal(2, intreqFetches.Length);
		Assert.Equal(new[] { 0x88, 0x8A }, intreqFetchBeams.Select(beam => beam.BeamHorizontal).ToArray());
		Assert.Equal(intreqFetches[1].GrantedCycle, intreqWrite.Cycle);
		Assert.True((intreqBeam.BeamLine, intreqBeam.BeamHorizontal) == (0x138, 0x8A), diagnostic);
		Assert.True((visibleBeam.BeamLine, visibleBeam.BeamHorizontal) == (0x138, 0x8E), diagnostic);
		Assert.True((acceptanceBeam.BeamLine, acceptanceBeam.BeamHorizontal) == (0x138, 0x92), diagnostic);
		Assert.True((entryBeam.BeamLine, entryBeam.BeamHorizontal) == (0x138, 0xA8), diagnostic);
		Assert.True(
			handlerBoundaries.Take(4).Select(boundary => boundary.Stop - cpuVisibleCycle)
				.SequenceEqual(new long[] { 68, 84, 92, 104 }),
			diagnostic);
	}

	[Fact]
	public void VAmigaTsProbe10SourceReadStoreTimingIsIndependentOfChipRamAddress()
	{
		var low = RunVAmigaTsProbe10SourceVhposrReadMacro(0x2000, 0x3000, 0x38C2);
		var high = RunVAmigaTsProbe10SourceVhposrReadMacro(0x07035E, 0x07060C, 0x38C2);
		var lowOffsets = low.Reads.Select(read => read.SampleCycle - low.StartCycle).ToArray();
		var highOffsets = high.Reads.Select(read => read.SampleCycle - high.StartCycle).ToArray();

		Assert.Equal(lowOffsets, highOffsets);
		Assert.Equal(low.Reads.Select(read => read.Value), high.Reads.Select(read => read.Value));
		Assert.Equal(
			ReadChipWords(low.Bus.ChipRam, 0x3000, low.Reads.Length),
			ReadChipWords(high.Bus.ChipRam, 0x07060C, high.Reads.Length));
	}

	[Fact]
	public void AccurateM68000LongRunningBraIrqAtRetirementWaitsForNextIplSample()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const int loopAddress = 0x070352;
		const int handlerAddress = 0x07035E;
		const int valuesAddress = 0x07060C;
		const ushort intreqLevel1 = 0x0004;
		Write(bus.ChipRam, loopAddress, new byte[] { 0x60, 0xFE });
		Write(bus.ChipRam, handlerAddress, CreateVAmigaTsProbe10SourceIrq1Handler(handlerAddress, valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(loopAddress, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		var probe10VisibleCycle =
			(312L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(144L * AmigaConstants.A500PalCpuCyclesPerColorClock);
		machine.Cpu.State.Cycles = probe10VisibleCycle - 1280;
		bus.WriteWord(0x00DFF09A, 0xC004);

		for (var i = 0; i < 128; i++)
		{
			bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
			machine.Cpu.ExecuteInstruction();
		}

		var visibleCycle = machine.Cpu.State.Cycles;
		Assert.Equal(probe10VisibleCycle, visibleCycle);
		bus.RequestHardwareInterrupt(intreqLevel1, visibleCycle - AmigaConstants.A500IntreqToIplDelayCpuCycles);
		bus.AdvanceDmaTo(visibleCycle);
		var before = Assert.IsAssignableFrom<IM68000PrefetchDiagnostics>(machine.Cpu)
			.CapturePrefetchDiagnosticState();
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(machine.Cpu);
		Assert.True(recognition.LastInterruptSampleCycle < visibleCycle);
		Assert.False(machine.DispatchPendingHardwareInterrupt());
		machine.Cpu.ExecuteInstruction();
		bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
		Assert.True(recognition.LastInterruptSampleCycle >= visibleCycle);
		Assert.True(machine.DispatchPendingHardwareInterrupt());
		var entryCycle = machine.Cpu.State.Cycles;
		while (bus.CustomRegisterReads.All(read => read.Address != 0x006))
		{
			machine.Cpu.ExecuteInstruction();
		}

		var firstRead = Assert.Single(bus.CustomRegisterReads.Where(read => read.Address == 0x006));
		var phases = bus.CpuBusPhases.Where(phase =>
			phase.CpuPhase.RequestedCycle >= visibleCycle &&
			phase.CpuPhase.RequestedCycle <= firstRead.CompletedCycle);
		var diagnostic =
			$"visible={visibleCycle}, entry=+{entryCycle - visibleCycle}, " +
			$"sample=+{firstRead.SampleCycle - visibleCycle}/0x{firstRead.Value:X4}, " +
			$"before={FormatPrefetchDiagnostic(before, visibleCycle)}, " +
			$"phases=[{BuildCpuBusPhaseTimeline(phases.ToArray(), visibleCycle)}]";

		Assert.True(before.ProgramCounter == loopAddress, diagnostic);
		Assert.True(entryCycle - visibleCycle == 56, diagnostic);
		// The handler presents the read at +98; the causal Agnus executor samples
		// the custom register on the next physical chip-bus slot at +102 after
		// the required post-IPL-sample interrupt setup interval.
		Assert.True(firstRead.SampleCycle - visibleCycle == 102, diagnostic);
	}

	[Fact]
	public void AccurateM68000Probe10IntreqAtEmulatedPhaseEntersHandlerAtHaa()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const int loopAddress = 0x070352;
		const int handlerAddress = 0x07035E;
		const ushort intreqLevel1 = 0x0004;
		Write(bus.ChipRam, loopAddress, new byte[] { 0x60, 0xFE });
		Write(bus.ChipRam, handlerAddress, new byte[] { 0x4E, 0x71 });
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(loopAddress, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		bus.WriteWord(0x00DFF09A, 0xC004);

		var requestCycle =
			(0x138L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(0x8AL * AmigaConstants.A500PalCpuCyclesPerColorClock);
		machine.Cpu.State.Cycles = requestCycle - 1280;
		for (var i = 0; i < 128; i++)
		{
			bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
			machine.Cpu.ExecuteInstruction();
		}

		Assert.Equal(requestCycle, machine.Cpu.State.Cycles);
		bus.RequestHardwareInterrupt(intreqLevel1, requestCycle);
		var visibleCycle = requestCycle + AmigaConstants.A500IntreqToIplDelayCpuCycles;
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(machine.Cpu);

		machine.Cpu.ExecuteInstruction();
		bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
		var firstRetireCycle = machine.Cpu.State.Cycles;
		var firstSampleCycle = recognition.LastInterruptSampleCycle;
		Assert.False(machine.DispatchPendingHardwareInterrupt());

		machine.Cpu.ExecuteInstruction();
		bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
		var secondRetireCycle = machine.Cpu.State.Cycles;
		var secondSampleCycle = recognition.LastInterruptSampleCycle;
		Assert.True(machine.DispatchPendingHardwareInterrupt());
		var entryCycle = machine.Cpu.State.Cycles;

		var requestBeam = bus.GetBeamPosition(requestCycle);
		var visibleBeam = bus.GetBeamPosition(visibleCycle);
		var firstSampleBeam = bus.GetBeamPosition(firstSampleCycle);
		var firstRetireBeam = bus.GetBeamPosition(firstRetireCycle);
		var secondSampleBeam = bus.GetBeamPosition(secondSampleCycle);
		var secondRetireBeam = bus.GetBeamPosition(secondRetireCycle);
		var entryBeam = bus.GetBeamPosition(entryCycle);
		var diagnostic =
			$"request=v{requestBeam.BeamLine:X3}h{requestBeam.BeamHorizontal:X2}, " +
			$"visible=v{visibleBeam.BeamLine:X3}h{visibleBeam.BeamHorizontal:X2}, " +
			$"first=sample:v{firstSampleBeam.BeamLine:X3}h{firstSampleBeam.BeamHorizontal:X2}/retire:v{firstRetireBeam.BeamLine:X3}h{firstRetireBeam.BeamHorizontal:X2}, " +
			$"second=sample:v{secondSampleBeam.BeamLine:X3}h{secondSampleBeam.BeamHorizontal:X2}/retire:v{secondRetireBeam.BeamLine:X3}h{secondRetireBeam.BeamHorizontal:X2}, " +
			$"entry=v{entryBeam.BeamLine:X3}h{entryBeam.BeamHorizontal:X2}";

		Assert.True((requestBeam.BeamLine, requestBeam.BeamHorizontal) == (0x138, 0x8A), diagnostic);
		Assert.True((visibleBeam.BeamLine, visibleBeam.BeamHorizontal) == (0x138, 0x8E), diagnostic);
		Assert.Equal(visibleCycle, firstSampleCycle);
		Assert.True(firstRetireCycle > visibleCycle, diagnostic);
		Assert.True(secondSampleCycle >= visibleCycle, diagnostic);
		Assert.True((entryBeam.BeamLine, entryBeam.BeamHorizontal) == (0x138, 0xAB), diagnostic);
	}

	[Fact]
	public void AccurateM68000Probe10Irq1MicrosequenceDocumentsEmulatedEcsDifference()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const int loopAddress = 0x070352;
		const int handlerAddress = 0x07035E;
		const int valuesAddress = 0x07060C;
		const ushort intreqLevel1 = 0x0004;
		Write(bus.ChipRam, loopAddress, new byte[] { 0x60, 0xFE });
		Write(bus.ChipRam, handlerAddress, CreateVAmigaTsProbe10SourceIrq1Handler(handlerAddress, valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(loopAddress, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;

		var visibleCycle =
			(312L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(144L * AmigaConstants.A500PalCpuCyclesPerColorClock);
		machine.Cpu.State.Cycles = visibleCycle - 1280;
		bus.WriteWord(0x00DFF09A, 0xC004);
		for (var i = 0; i < 128; i++)
		{
			bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
			machine.Cpu.ExecuteInstruction();
		}

		Assert.Equal(visibleCycle, machine.Cpu.State.Cycles);
		bus.RequestHardwareInterrupt(intreqLevel1, visibleCycle - AmigaConstants.A500IntreqToIplDelayCpuCycles);
		bus.AdvanceDmaTo(visibleCycle);
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(machine.Cpu);
		Assert.True(recognition.LastInterruptSampleCycle < visibleCycle);
		Assert.False(machine.DispatchPendingHardwareInterrupt());
		machine.Cpu.ExecuteInstruction();
		bus.AdvanceDmaTo(machine.Cpu.State.Cycles);
		Assert.True(recognition.LastInterruptSampleCycle >= visibleCycle);
		Assert.True(machine.DispatchPendingHardwareInterrupt());

		var entryCycle = machine.Cpu.State.Cycles;
		var boundaries = new List<(uint Pc, long Start, long Stop)>();
		for (var i = 0; i < 5; i++)
		{
			var pc = machine.Cpu.State.ProgramCounter;
			var start = machine.Cpu.State.Cycles;
			machine.Cpu.ExecuteInstruction();
			boundaries.Add((pc, start, machine.Cpu.State.Cycles));
		}

		var firstRead = Assert.Single(bus.CustomRegisterReads.Where(read =>
			read.Address == 0x006 && read.Kind == AmigaBusAccessKind.CpuDataRead));
		var entryBeam = bus.GetBeamPosition(entryCycle);
		var stopBeams = boundaries.Select(boundary => bus.GetBeamPosition(boundary.Stop)).ToArray();
		var requestBeam = bus.GetBeamPosition(firstRead.RequestedCycle);
		var grantBeam = bus.GetBeamPosition(firstRead.GrantedCycle);
		var sampleBeam = bus.GetBeamPosition(firstRead.SampleCycle);
		var completeBeam = bus.GetBeamPosition(firstRead.CompletedCycle);
		var actualStops = stopBeams.Select(beam => beam.BeamHorizontal).ToArray();
		var emulatorStops = new[] { 0xB2, 0xBA, 0xBE, 0xC4, 0xC8 };
		var differences = actualStops.Zip(emulatorStops, (actual, expected) => actual - expected).ToArray();
		var diagnostic =
			$"entry=v{entryBeam.BeamLine:X3}h{entryBeam.BeamHorizontal:X2}, " +
			$"boundaries=[{string.Join(",", boundaries.Select((boundary, index) => $"0x{boundary.Pc:X6}:v{stopBeams[index].BeamLine:X3}h{stopBeams[index].BeamHorizontal:X2}/+{boundary.Stop - boundary.Start}"))}], " +
			$"read=0x{firstRead.Value:X4}:req=v{requestBeam.BeamLine:X3}h{requestBeam.BeamHorizontal:X2}," +
			$"grant=v{grantBeam.BeamLine:X3}h{grantBeam.BeamHorizontal:X2}," +
			$"sample=v{sampleBeam.BeamLine:X3}h{sampleBeam.BeamHorizontal:X2}," +
			$"done=v{completeBeam.BeamLine:X3}h{completeBeam.BeamHorizontal:X2}, " +
			$"referenceEntry=v138hAA, referenceStops=[B2,BA,BE,C4,C8], differences=[{string.Join(",", differences)}]";

		Assert.True((entryBeam.BeamLine, entryBeam.BeamHorizontal) == (0x138, 0xAC), diagnostic);
		Assert.True(actualStops.SequenceEqual(new[] { 0xB4, 0xBC, 0xC0, 0xC6, 0xCA }), diagnostic);
		Assert.True(differences.SequenceEqual(Enumerable.Repeat(2, 5)), diagnostic);
		Assert.True(firstRead.Value == 0x38C7, diagnostic);
		Assert.True((sampleBeam.BeamLine, sampleBeam.BeamHorizontal) == (0x138, 0xC3), diagnostic);
	}

	[Fact]
	public void VAmigaTsStyleCopperBplconEnableDocumentsCurrentBitplaneOrigin()
	{
		var bounds = RenderVAmigaTsStyleCopperEnabledBitplaneBounds();
		var diagnostic =
			$"bounds=({bounds.MinX},{bounds.FirstLowResRow})-({bounds.MaxX},{bounds.LastLowResRow})";

		Assert.True(bounds.FirstLowResRow >= 0, diagnostic);
		Assert.Equal(38, bounds.FirstLowResRow);
	}

	[Fact]
	public void CopperColor00BandsDocumentCurrentRawOrigin()
	{
		var bands = RenderCopperColor00BandRows(
			(0x20, 0x0F00),
			(0x40, 0x00F0),
			(0x60, 0x000F));
		var diagnostic = string.Join(
			"; ",
			bands.Select(band => $"line=0x{band.Line:X2}/color=0x{band.Color:X4}/row={band.FirstLowResRow}"));

		Assert.Equal(6, bands.Single(band => band.Line == 0x20).FirstLowResRow);
		Assert.Equal(38, bands.Single(band => band.Line == 0x40).FirstLowResRow);
		Assert.Equal(70, bands.Single(band => band.Line == 0x60).FirstLowResRow);
		Assert.DoesNotContain("row=-1", diagnostic);
	}

	[Fact]
	public void DedicatedBeamReadTraceDoesNotRequireGeneralBusCapture()
	{
		var bus = new AmigaBus(captureBusAccesses: false, enableLiveAgnusDma: true);
		bus.CaptureCustomRegisterReadTrace(0x006, 2, 16);
		var cycle = 1234L;

		_ = bus.ReadWord(0x00DFF006, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var read = Assert.Single(bus.CustomRegisterReadTrace);
		Assert.Equal(0x006, read.Address);
		Assert.Empty(bus.CustomRegisterReads);
		Assert.Empty(bus.BusAccesses);
	}

	[Fact]
	public void CopperBplconEnableLineSweepDocumentsCurrentBitplaneOrigin()
	{
		var samples = new[]
		{
			(Line: 0x2C, ExpectedRow: 18),
			(Line: 0x30, ExpectedRow: 22),
			(Line: 0x38, ExpectedRow: 30),
			(Line: 0x40, ExpectedRow: 38),
			(Line: 0x48, ExpectedRow: 46)
		};
		var actual = samples
			.Select(sample => (sample.Line, Bounds: RenderVAmigaTsStyleCopperEnabledBitplaneBounds(sample.Line)))
			.ToArray();
		var diagnostic = string.Join(
			"; ",
			actual.Select(sample =>
				$"line=0x{sample.Line:X2}/bounds=({sample.Bounds.MinX},{sample.Bounds.FirstLowResRow})-({sample.Bounds.MaxX},{sample.Bounds.LastLowResRow})"));

		foreach (var sample in samples)
		{
			var bounds = actual.Single(entry => entry.Line == sample.Line).Bounds;
			Assert.Equal(sample.ExpectedRow, bounds.FirstLowResRow);
		}

		Assert.DoesNotContain(",-1", diagnostic);
	}

	[Fact]
	public void CopperBplconEnableDocumentsFirstBitplaneFetchAndRenderRow()
	{
		var probe = RenderVAmigaTsStyleCopperEnabledBitplaneProbe(0x40);
		var diagnostic =
			$"firstFetch=0x{probe.FirstFetchAddress:X6}@{probe.FirstFetchCycle}/" +
			$"v{probe.FirstFetchLine:X3}h{probe.FirstFetchHorizontal:X2}, " +
			$"bounds=({probe.Bounds.MinX},{probe.Bounds.FirstLowResRow})-({probe.Bounds.MaxX},{probe.Bounds.LastLowResRow})";

		Assert.Equal(0x40, probe.FirstFetchLine);
		Assert.Equal(0x3F, probe.FirstFetchHorizontal);
		Assert.Equal(38, probe.Bounds.FirstLowResRow);
		Assert.Equal(0x4000u, probe.FirstFetchAddress);
		Assert.DoesNotContain("-1", diagnostic);
	}

	[Fact]
	public void VAmigaTsStyleBitplaneSetupDocumentsPointerDmaAndFirstGlyphOrdering()
	{
		var probe = RenderVAmigaTsStyleCpuPointerBitplaneProbe();
		var diagnostic =
			$"pointerHigh={probe.PointerHighCycle}/v{probe.PointerHighLine:X3}h{probe.PointerHighHorizontal:X2}, " +
			$"pointerLow={probe.PointerLowCycle}/v{probe.PointerLowLine:X3}h{probe.PointerLowHorizontal:X2}, " +
			$"diwStart=v{probe.DiwStartLine:X3}, " +
			$"bplcon={probe.BplconCycle}/v{probe.BplconLine:X3}h{probe.BplconHorizontal:X2}, " +
			$"firstFetch=0x{probe.FirstFetchAddress:X6}@{probe.FirstFetchCycle}/v{probe.FirstFetchLine:X3}h{probe.FirstFetchHorizontal:X2}, " +
			$"firstGlyphRow={probe.Bounds.FirstLowResRow}";

		Assert.Equal(0x10, probe.PointerHighLine);
		Assert.Equal(0x10, probe.PointerLowLine);
		Assert.True(probe.PointerLowCycle < probe.DiwStartCycle, diagnostic);
		Assert.Equal(0x2C, probe.DiwStartLine);
		Assert.Equal(0x40, probe.BplconLine);
		Assert.Equal(0x40, probe.FirstFetchLine);
		Assert.Equal(0x3F, probe.FirstFetchHorizontal);
		Assert.Equal(0x4000u, probe.FirstFetchAddress);
		Assert.Equal(38, probe.Bounds.FirstLowResRow);
		Assert.True(probe.PointerLowCycle < probe.BplconCycle, diagnostic);
		Assert.True(probe.BplconCycle <= probe.FirstFetchCycle, diagnostic);
		Assert.DoesNotContain("-1", diagnostic);
	}

	[Fact(Skip = "vAmigaTS probe10 raw reference contract; current display renderer places this playfield row at low-res row 38.")]
	public void VAmigaTsStyleCopperBplconEnableShouldMapBitplaneRowZeroToRawRow24()
	{
		var bounds = RenderVAmigaTsStyleCopperEnabledBitplaneBounds();
		var diagnostic =
			$"bounds=({bounds.MinX},{bounds.FirstLowResRow})-({bounds.MaxX},{bounds.LastLowResRow})";

		Assert.True(bounds.FirstLowResRow >= 0, diagnostic);
		Assert.Equal(24, bounds.FirstLowResRow);
	}

	[Fact(Skip = "Hardware reference contract from vAmigaTS probe10; current IRQ1 result buffer is late relative to expected values.")]
	public void VAmigaTsProbe10Irq1AtRasterLine38ResultBufferMatchesReference()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const uint copperList = 0x2400;
		const uint handlerAddress = 0x2000;
		const int valuesAddress = 0x3000;
		const ushort intreqLevel1 = 0x0004;
		WriteCopperList(
			bus,
			copperList,
			(0x1001, 0x00FE),
			(0x02A, 0x8001),
			(0x4001, 0x00FE),
			(0x100, 0x2200),
			(0x3885, 0xFFFE),
			(0x09C, (ushort)(0x8000 | intreqLevel1)),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71, 0x4E, 0x71, 0x4E, 0x71);
		Write(bus.ChipRam, (int)handlerAddress, CreateVAmigaTsProbe10Irq1Handler(valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(0x1000, 0x3F00);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		machine.Cpu.State.A[3] = 0x00DFF006;
		bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | intreqLevel1));
		bus.WriteWord(0x00DFF096, 0x8380);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x4000);
		bus.WriteWord(0x00DFF0E4, 0x0000);
		bus.WriteWord(0x00DFF0E6, 0x5000);

		var targetLineCycle = AmigaConstants.A500PalCpuCyclesPerFrame +
			(0x39L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		bus.AdvanceDmaTo(targetLineCycle);

		var intreqWrite = bus.CustomRegisterWrites.Last(write =>
			write.Address == 0x09C &&
			(write.Value & intreqLevel1) != 0);
		var cpuVisibleCycle = intreqWrite.Cycle + AmigaConstants.A500IntreqToIplDelayCpuCycles;
		machine.Cpu.State.Cycles = cpuVisibleCycle;
		Assert.True(machine.DispatchPendingHardwareInterrupt());

		for (var i = 0; i < 40; i++)
		{
			machine.Cpu.ExecuteInstruction();
		}

		var expected = VAmigaTsProbe10ExpectedValues();
		var stored = ReadChipWords(bus.ChipRam, valuesAddress, expected.Length);
		var diagnostic =
			$"intreqWrite={intreqWrite.Cycle}, visible={cpuVisibleCycle}, " +
			$"expected=[{string.Join(",", expected.Select(value => $"0x{value:X4}"))}], " +
			$"stored=[{string.Join(",", stored.Select(value => $"0x{value:X4}"))}]";

		Assert.True(expected.SequenceEqual(stored), diagnostic);
	}

	private static ushort[] VAmigaTsProbe10ExpectedValues()
		=>
		[
			0x38C2, 0x38CC, 0x38D6, 0x38E0,
			0x000D, 0x0017, 0x0021, 0x002B,
			0x0035, 0x003F, 0x0049, 0x0053,
			0x005D, 0x0067, 0x0071, 0x007B
		];

	private static ushort[] CurrentVAmigaTsProbe10Irq1ExpectedValues()
		=>
		[
			0x3800, 0x390F, 0x3919, 0x3923,
			0x392D, 0x3937, 0x3941, 0x3955,
			0x3969, 0x397D, 0x3991, 0x39A5,
			0x39B9, 0x39C8, 0x39DC, 0x3A09
		];

	private static ushort[] CurrentVAmigaTsProbe10SourceCopperExpectedValues()
		=>
		[
			0x381B, 0x3825, 0x382F, 0x3839,
			0x3843, 0x384D, 0x3857, 0x3861,
			0x386B, 0x3875, 0x387F, 0x3889,
			0x3893, 0x389D, 0x38A7, 0x38B1
		];

	private static (int FirstLowResRow, int LastLowResRow, int MinX, int MaxX) RenderVAmigaTsStyleCopperEnabledBitplaneBounds()
		=> RenderVAmigaTsStyleCopperEnabledBitplaneBounds(0x40);

	private static (int FirstLowResRow, int LastLowResRow, int MinX, int MaxX) RenderVAmigaTsStyleCopperEnabledBitplaneBounds(int enableLine)
		=> RenderVAmigaTsStyleCopperEnabledBitplaneProbe(enableLine).Bounds;

	private static (
		(int FirstLowResRow, int LastLowResRow, int MinX, int MaxX) Bounds,
		long FirstFetchCycle,
		int FirstFetchLine,
		int FirstFetchHorizontal,
		uint FirstFetchAddress) RenderVAmigaTsStyleCopperEnabledBitplaneProbe(int enableLine)
	{
		var bus = new AmigaBus(enableLiveDisplayDma: true);
		const uint copperList = 0x2400;
		const uint bitplane = 0x4000;

		BigEndian.WriteUInt16(bus.ChipRam, (int)bitplane, 0xFFFF);
		bus.WriteWord(0x00DFF180, 0x0000);
		bus.WriteWord(0x00DFF182, 0x0F00);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF08E, 0x2C81);
		bus.WriteWord(0x00DFF090, 0xF4C1);
		bus.WriteWord(0x00DFF0E0, (ushort)(bitplane >> 16));
		bus.WriteWord(0x00DFF0E2, (ushort)bitplane);
		bus.WriteWord(0x00DFF108, 0x0000);
		bus.WriteWord(0x00DFF10A, 0x0000);
		WriteCopperList(
			bus,
			copperList,
			((ushort)((enableLine << 8) | 0x0001), 0xFFFE),
			(0x0100, 0x1200),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8380);

		var frameStop = bus.GetFrameStopCycle(0);
		var framebuffer = new uint[bus.Display.Width * bus.Display.Height];
		bus.Display.BeginPresentationFrame(new PresentationFrameTarget(framebuffer), 0, frameStop);
		bus.AdvanceDmaTo(frameStop);
		bus.Display.CompletePresentationFrame(frameStop);

		var bounds = FindNonBackgroundLowResBounds(framebuffer, bus.Display.Width, bus.Display.Height, framebuffer[0]);
		var firstFetch = bus.BusAccesses.First(access =>
			access.Request.Requester == AmigaBusRequester.Bitplane &&
			access.Request.Kind == AmigaBusAccessKind.Bitplane);
		var firstFetchBeam = bus.GetBeamPosition(firstFetch.GrantedCycle);
		return (
			bounds,
			firstFetch.GrantedCycle,
			firstFetchBeam.BeamLine,
			firstFetchBeam.BeamHorizontal,
			firstFetch.Request.Address);
	}

	private static (
		(int FirstLowResRow, int LastLowResRow, int MinX, int MaxX) Bounds,
		long PointerHighCycle,
		int PointerHighLine,
		int PointerHighHorizontal,
		long PointerLowCycle,
		int PointerLowLine,
		int PointerLowHorizontal,
		long DiwStartCycle,
		int DiwStartLine,
		long BplconCycle,
		int BplconLine,
		int BplconHorizontal,
		long FirstFetchCycle,
		int FirstFetchLine,
		int FirstFetchHorizontal,
		uint FirstFetchAddress) RenderVAmigaTsStyleCpuPointerBitplaneProbe()
	{
		var bus = new AmigaBus(enableLiveDisplayDma: true);
		const uint copperList = 0x2400;
		const uint bitplane = 0x4000;
		const int diwStartLine = 0x2C;
		const int bplconEnableLine = 0x40;

		BigEndian.WriteUInt16(bus.ChipRam, (int)bitplane, 0xFFFF);
		bus.WriteWord(0x00DFF180, 0x0000);
		bus.WriteWord(0x00DFF182, 0x0F00);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteWord(0x00DFF08E, 0x2C81);
		bus.WriteWord(0x00DFF090, 0xF4C1);
		bus.WriteWord(0x00DFF108, 0x0000);
		bus.WriteWord(0x00DFF10A, 0x0000);
		WriteCopperList(
			bus,
			copperList,
			((ushort)((bplconEnableLine << 8) | 0x0001), 0xFFFE),
			(0x0100, 0x1200),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8380);

		var pointerCycle = 0x10L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		bus.WriteWord(0x00DFF0E0, (ushort)(bitplane >> 16), ref pointerCycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteWord(0x00DFF0E2, (ushort)bitplane, ref pointerCycle, AmigaBusAccessKind.CpuDataWrite);

		var frameStop = bus.GetFrameStopCycle(0);
		var framebuffer = new uint[bus.Display.Width * bus.Display.Height];
		bus.Display.BeginPresentationFrame(new PresentationFrameTarget(framebuffer), 0, frameStop);
		bus.AdvanceDmaTo(frameStop);
		bus.Display.CompletePresentationFrame(frameStop);

		var pointerHigh = bus.CustomRegisterWrites.Last(write => write.Address == 0x0E0);
		var pointerLow = bus.CustomRegisterWrites.Last(write => write.Address == 0x0E2);
		var bplcon = bus.Display.CopperDisplayWrites.Last(write => write.Address == 0x100 && write.Value == 0x1200);
		var firstFetch = bus.BusAccesses.First(access =>
			access.Request.Requester == AmigaBusRequester.Bitplane &&
			access.Request.Kind == AmigaBusAccessKind.Bitplane);
		var pointerHighBeam = bus.GetBeamPosition(pointerHigh.Cycle);
		var pointerLowBeam = bus.GetBeamPosition(pointerLow.Cycle);
		var bplconBeam = bus.GetBeamPosition(bplcon.Cycle);
		var firstFetchBeam = bus.GetBeamPosition(firstFetch.GrantedCycle);
		var diwStartCycle = diwStartLine * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var bounds = FindNonBackgroundLowResBounds(framebuffer, bus.Display.Width, bus.Display.Height, framebuffer[0]);
		return (
			bounds,
			pointerHigh.Cycle,
			pointerHighBeam.BeamLine,
			pointerHighBeam.BeamHorizontal,
			pointerLow.Cycle,
			pointerLowBeam.BeamLine,
			pointerLowBeam.BeamHorizontal,
			diwStartCycle,
			diwStartLine,
			bplcon.Cycle,
			bplconBeam.BeamLine,
			bplconBeam.BeamHorizontal,
			firstFetch.GrantedCycle,
			firstFetchBeam.BeamLine,
			firstFetchBeam.BeamHorizontal,
			firstFetch.Request.Address);
	}

	private static (int Line, ushort Color, int FirstLowResRow)[] RenderCopperColor00BandRows(
		params (int Line, ushort Color)[] writes)
	{
		var bus = new AmigaBus(enableLiveDisplayDma: true);
		const uint copperList = 0x2400;
		var instructions = new List<(ushort First, ushort Second)>(writes.Length * 2 + 1);
		foreach (var write in writes)
		{
			instructions.Add(((ushort)((write.Line << 8) | 0x0001), 0xFFFE));
			instructions.Add((0x0180, write.Color));
		}

		instructions.Add((0xFFFF, 0xFFFE));
		WriteCopperList(bus, copperList, instructions.ToArray());
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);

		var frameStop = bus.GetFrameStopCycle(0);
		var framebuffer = new uint[bus.Display.Width * bus.Display.Height];
		bus.Display.BeginPresentationFrame(new PresentationFrameTarget(framebuffer), 0, frameStop);
		try
		{
			bus.AdvanceDmaTo(frameStop);
			bus.Display.CompletePresentationFrame(frameStop);
		}
		catch
		{
			bus.Display.AbortPresentationFrame();
			throw;
		}

		return writes
			.Zip(FindColorTransitionRows(framebuffer, bus.Display.Width, bus.Display.Height), (write, row) =>
				(write.Line, write.Color, row))
			.ToArray();
	}

	private static int[] FindColorTransitionRows(
		uint[] framebuffer,
		int width,
		int height)
	{
		var rows = new List<int>();
		var previous = SampleLowResRowPixel(framebuffer, width, 0);
		for (var lowY = 1; lowY < height / 2; lowY++)
		{
			var current = SampleLowResRowPixel(framebuffer, width, lowY);
			if (current == previous)
			{
				continue;
			}

			rows.Add(lowY);
			previous = current;
		}

		return rows.ToArray();
	}

	private static uint SampleLowResRowPixel(uint[] framebuffer, int width, int lowY)
		=> framebuffer[(lowY * 2 * width) + (width / 2)];

	private static (int FirstLowResRow, int LastLowResRow, int MinX, int MaxX) FindNonBackgroundLowResBounds(
		uint[] framebuffer,
		int width,
		int height,
		uint background)
	{
		var firstRow = -1;
		var lastRow = -1;
		var minX = width;
		var maxX = -1;
		var lowHeight = height / 2;

		for (var lowY = 0; lowY < lowHeight; lowY++)
		{
			var rowHasPixel = false;
			for (var highY = lowY * 2; highY < Math.Min(height, (lowY * 2) + 2); highY++)
			{
				var rowOffset = highY * width;
				for (var x = 0; x < width; x++)
				{
					if (framebuffer[rowOffset + x] == background)
					{
						continue;
					}

					rowHasPixel = true;
					minX = Math.Min(minX, x);
					maxX = Math.Max(maxX, x);
				}
			}

			if (!rowHasPixel)
			{
				continue;
			}

			if (firstRow < 0)
			{
				firstRow = lowY;
			}

			lastRow = lowY;
		}

		return (firstRow, lastRow, minX == width ? -1 : minX, maxX);
	}

	[Fact]
	public void CpuLongReadCanUseGappedSlotsBetweenTwoPlaneLowResDma()
	{
		AssertCpuLongReadCanUseGappedSlotsBetweenTwoPlaneLowResDma(lineStart: 0);
		AssertCpuLongReadCanUseGappedSlotsBetweenTwoPlaneLowResDma(lineStart: 167526);
	}

	private static void AssertCpuLongReadCanUseGappedSlotsBetweenTwoPlaneLowResDma(long lineStart)
	{
		var engine = new AgnusHrmSlotEngine();
		long Cycle(int hpos) => lineStart + (hpos * AgnusChipSlotScheduler.SlotCycles);
		for (var hpos = 0x93; hpos <= 0xD7; hpos += 4)
		{
			_ = engine.ReserveBitplaneDmaSlot(0x4000, Cycle(hpos));
		}

		engine.GrantCpuDataSingleSlot(
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessTarget.ChipRam,
			0x3EFE,
			AmigaBusAccessSize.Word,
			Cycle(0x90),
			isWrite: true,
			out _,
			out _);
		engine.GrantCpuDataSingleSlot(
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessTarget.ChipRam,
			0x3EFC,
			AmigaBusAccessSize.Word,
			Cycle(0x92),
			isWrite: true,
			out _,
			out _);
		engine.GrantCpuDataSingleSlot(
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessTarget.ChipRam,
			0x3EFA,
			AmigaBusAccessSize.Word,
			Cycle(0x96),
			isWrite: true,
			out _,
			out _);

		engine.GrantCpuDataLongSlots(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x64,
			Cycle(0x9A),
			isWrite: false,
			out var firstWordCycle,
			out var secondWordCycle,
			out var completedCycle);

		Assert.Equal(Cycle(0x9A), firstWordCycle);
		Assert.Equal(Cycle(0x9C), secondWordCycle);
		Assert.Equal(Cycle(0x9D), completedCycle);
	}

	[Fact]
	public void AmigaBusCpuLongReadCanUseGappedSlotsBetweenTwoPlaneLowResDma()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		bus.WriteByte(0x00BFE001, 0x00, 0);
		const long lineStart = 167526;
		long Cycle(int hpos) => lineStart + (hpos * AgnusChipSlotScheduler.SlotCycles);
		var slotAudit = new List<AgnusSlotScheduleAuditEntry>();
		bus.SetSlotScheduleAuditSink(slotAudit.Add);
		for (var hpos = 0x93; hpos <= 0xD7; hpos += 4)
		{
			Assert.True(bus.TryReserveRowBitplaneDmaSlot(0x4000, Cycle(hpos), out var grantedCycle));
			Assert.Equal(Cycle(hpos), grantedCycle);
		}

		Write(bus.ChipRam, 0x64, 0x00, 0x00, 0x20, 0x00);
		var writeCycle = Cycle(0x90);
		bus.WriteWord(0x3EFE, 0x1000, ref writeCycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteWord(0x3EFC, 0x0000, ref writeCycle, AmigaBusAccessKind.CpuDataWrite);
		bus.WriteWord(0x3EFA, 0x2000, ref writeCycle, AmigaBusAccessKind.CpuDataWrite);
		var readCycle = Cycle(0x9A);
		var engine = GetPrivateField<AgnusHrmSlotEngine>(bus, "_hrmSlotEngine");
		Assert.True(engine.TryPredictCpuDataLongSlots(
			AmigaBusAccessKind.CpuDataRead,
			AmigaBusAccessTarget.ChipRam,
			0x64,
			readCycle,
			isWrite: false,
			out var predictedFirst,
			out var predictedSecond,
			out var predictedCompleted));
		Assert.Equal(Cycle(0x9A), predictedFirst);
		Assert.Equal(Cycle(0x9C), predictedSecond);
		Assert.Equal(Cycle(0x9D), predictedCompleted);

		var value = bus.ReadLong(0x64, ref readCycle, AmigaBusAccessKind.CpuDataRead);

		var auditSlots = slotAudit
			.Where(entry => entry.SlotCycle >= Cycle(0x90) && entry.SlotCycle <= Cycle(0xE2))
			.Select(entry =>
				$"0x{AgnusHrmOcsSlotTable.GetHorizontal(entry.SlotCycle):X2}:{entry.Owner}/{entry.Requester}/{entry.Target}/{entry.Size}")
			.ToArray();
		var diagnostic = string.Join(",", auditSlots);
		Assert.Equal(0x00002000u, value);
		Assert.True(readCycle == Cycle(0x9D), diagnostic);
		Assert.Contains("0x9A:Cpu/Cpu/ChipRam/Word", auditSlots);
		Assert.Contains("0x9C:Cpu/Cpu/ChipRam/Word", auditSlots);
		Assert.DoesNotContain("0xD9:Cpu/Cpu/ChipRam/Word", diagnostic);
	}

	[Fact]
	public void CpuLongWriteCommitsWordHalvesAroundInterveningBitplaneDma()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x0000);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x0000);
		ConfigureLiveOneBitplaneDma(bus);

		var cpuCycle = fetchCycle - AgnusChipSlotScheduler.SlotCycles;
		bus.WriteLong(0x00001000, 0x8000FFFF, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.True(bus.Display.TryGetCapturedBitplaneWord(row, plane: 0, word: 0, out var captured));
		Assert.Equal(0x8000, captured);
		var ordered = bus.BusAccesses
			.Where(access => access.GrantedCycle >= fetchCycle - AgnusChipSlotScheduler.SlotCycles &&
				access.GrantedCycle <= cpuCycle &&
				(access.Request.Requester == AmigaBusRequester.Bitplane ||
					access.Request.Requester == AmigaBusRequester.Cpu &&
					access.Request.Kind == AmigaBusAccessKind.CpuDataWrite))
			.OrderBy(access => access.GrantedCycle)
			.ToArray();

		Assert.Equal(3, ordered.Length);
		Assert.Equal(AmigaBusRequester.Cpu, ordered[0].Request.Requester);
		Assert.Equal(0x1000u, ordered[0].Request.Address);
		Assert.Equal(AmigaBusRequester.Bitplane, ordered[1].Request.Requester);
		Assert.Equal(fetchCycle, ordered[1].GrantedCycle);
		Assert.Equal(AmigaBusRequester.Cpu, ordered[2].Request.Requester);
		Assert.Equal(0x1002u, ordered[2].Request.Address);
		Assert.All(ordered, access => Assert.Equal(AmigaBusAccessSize.Word, access.Request.Size));
	}

	[Fact]
	public void LiveDisplayDmaStallsPseudoFastButNotRealFastRomOrCiaAccesses()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			realFastRamSize: 0x10000);
		bus.ConfigureAutoconfigFastRamForHost();
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
	public void IntreqPollingAdvancesLiveBitplaneDmaThroughReadBarrier()
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
		Assert.True(bus.Display.LiveFetchBatchWordCount > initialFetchWords);
		var fetchWordsAfterFirstRead = bus.Display.LiveFetchBatchWordCount;
		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.True(snapshot.BitplaneSlotGrantCount > 0);
		Assert.True(snapshot.CpuChipStallCycles >= AgnusChipSlotScheduler.SlotCycles);

		cycle = fetchCycle + (4 * AgnusChipSlotScheduler.SlotCycles);
		_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.True(bus.Display.LiveFetchBatchWordCount >= fetchWordsAfterFirstRead);
	}

	[Fact]
	public void CustomRegisterReadAfterBitplaneSlotMaterializesPreviousFixedFetch()
	{
		var bus = new AmigaBus();
		var row = AmigaConstants.PalLowResOverscanBorderY;
		var fetchCycle = LowResPlane1FetchCycle(row);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xA55A);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x1000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();

		var cycle = fetchCycle + AgnusChipSlotScheduler.SlotCycles;
		_ = bus.ReadWord(0x00DFF004, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.Equal(0xA55A, ReadLiveBitplaneWord(bus, row, plane: 0, word: 0));
		Assert.True(bus.Agnus.CaptureSnapshot().BitplaneSlotGrantCount > 0);
	}

	[Fact]
	public void IntreqPollingCapturesLiveBitplanesWhileCrossingTheFrame()
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
		var maximumFetchWords = initialFetchWords;
		for (var requestedCycle = fetchCycle; requestedCycle <= frameStopCycle + 64; requestedCycle += 32)
		{
			var cycle = requestedCycle;
			_ = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
			maximumFetchWords = Math.Max(maximumFetchWords, bus.Display.LiveFetchBatchWordCount);
		}

		Assert.True(
			maximumFetchWords > initialFetchWords,
			$"Expected INTREQ polling barriers to capture bitplane words, maxLiveWords={maximumFetchWords}");
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
		batchBus.ExecuteRowBitplaneDmaFetches(
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
	public void AgnusExecutorSamplesBitplaneWordAndCommitsHorizonAtomically()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var fetchCycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0xA55A);

		var granted = bus.CausalBusExecutor.TryExecuteBitplaneWord(
			plane: 0,
			address: 0x2000,
			requestedCycle: fetchCycle,
			out var value,
			out var grantedCycle);

		Assert.True(granted);
		Assert.Equal((ushort)0xA55A, value);
		Assert.Equal(fetchCycle, grantedCycle);
		var state = bus.CausalBusExecutor.DisplayControlState;
		Assert.Equal(1, state.BitplaneGrantCounts[0]);
		Assert.Equal(0x2000u, state.BitplaneLastFetchAddresses[0]);
		Assert.Equal(grantedCycle, state.BitplaneLastFetchCycles[0]);
		Assert.True(bus.CausalBusExecutor.ExecutedThroughCycle >= grantedCycle);

		Assert.False(bus.CausalBusExecutor.TryExecuteBitplaneWord(
			plane: 0,
			address: 0x2002,
			requestedCycle: fetchCycle,
			out _,
			out _));
		Assert.Equal(1, state.BitplaneGrantCounts[0]);
	}

	[Fact]
	public void AgnusExecutorSamplesCopperWordAtPredictedExactSlot()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0x0180);
		var slotCycle = bus.CausalBusExecutor.PredictCopperWordCycle(0x2000, 20);

		var granted = bus.CausalBusExecutor.TryExecuteCopperWordExact(
			address: 0x2000,
			requestedCycle: 20,
			slotCycle,
			out var value,
			out var access);

		Assert.True(granted);
		Assert.Equal((ushort)0x0180, value);
		Assert.Equal(slotCycle, access.GrantedCycle);
		Assert.Equal(1, bus.CausalBusExecutor.CopperGrantedWords);
		Assert.Equal(0, bus.CausalBusExecutor.CopperDeniedWords);
		Assert.Equal(0x2000u, bus.CausalBusExecutor.LastCopperAddress);
		Assert.Equal(value, bus.CausalBusExecutor.LastCopperValue);
		Assert.Equal(slotCycle, bus.CausalBusExecutor.LastCopperGrantedCycle);
		Assert.True(bus.CausalBusExecutor.ExecutedThroughCycle >= slotCycle);

		Assert.False(bus.CausalBusExecutor.TryExecuteCopperWordExact(
			address: 0x2002,
			requestedCycle: 20,
			slotCycle,
			out _,
			out _));
		Assert.Equal(1, bus.CausalBusExecutor.CopperGrantedWords);
		Assert.Equal(1, bus.CausalBusExecutor.CopperDeniedWords);
	}

	[Fact]
	public void DeniedCopperFetchRemainsPendingUntilGranted()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 0x0180);
		var refreshCycle = (long)AgnusHrmOcsSlotTable.FirstRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;

		Assert.False(bus.CausalBusExecutor.TryExecuteCopperWordExact(
			0x2000,
			refreshCycle,
			refreshCycle,
			out _,
			out _));

		ref readonly var pending = ref bus.CausalBusExecutor.GetIntent(AgnusBusAgendaSource.Copper);
		Assert.True(pending.Pending);
		Assert.Equal(AmigaBusRequester.Copper, pending.Requester);
		Assert.Equal(0x2000u, pending.Address);
		Assert.True(pending.EarliestCycle > refreshCycle);

		var retryCycle = pending.EarliestCycle;
		Assert.True(bus.CausalBusExecutor.TryExecuteCopperWordExact(
			0x2000,
			retryCycle,
			retryCycle,
			out var value,
			out var access));
		Assert.Equal((ushort)0x0180, value);
		Assert.Equal(retryCycle, access.GrantedCycle);
		Assert.False(bus.CausalBusExecutor.GetIntent(AgnusBusAgendaSource.Copper).Pending);
	}

	[Fact]
	public void CopperMoveControlIntentCommitsOnlyAtItsDataCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		executor.MarkAdvancedThrough(100);
		executor.ScheduleCopperMoveControl(register: 0x180, value: 0x0F00, cycle: 120);

		Assert.True(executor.HasPendingCopperMove);
		Assert.Equal(120, executor.PendingCopperMoveCycle);
		ref readonly var pending = ref executor.GetIntent(AgnusBusAgendaSource.Control);
		Assert.True(pending.Pending);
		Assert.True((pending.Flags & AgnusBusIntentFlags.Write) != 0);
		Assert.Equal(AmigaBusAccessTarget.CustomRegisters, pending.Target);
		Assert.Equal(0x00DFF180u, pending.Address);
		Assert.Equal(1, executor.CopperMoveEventsScheduled);
		Assert.Equal(0, executor.CopperMoveEventsCommitted);

		Assert.Throws<InvalidOperationException>(
			() => executor.CommitCopperMoveControl(0x180, 0x0F00, 119));
		Assert.True(executor.HasPendingCopperMove);

		executor.CommitCopperMoveControl(0x180, 0x0F00, 120);
		Assert.False(executor.HasPendingCopperMove);
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Control).Pending);
		Assert.Equal(1, executor.CopperMoveEventsCommitted);
		Assert.Equal(120, executor.ExecutedThroughCycle);
	}

	[Fact]
	public void LiveCopperMoveCommitsAtomicallyThroughAgnus()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		const uint copperList = 0x2400;
		WriteCopperList(
			bus,
			copperList,
			(0x0180, 0x0F00),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);
		bus.WriteWord(0x00DFF088, 0x0000);
		bus.EnableLiveAgnusDma();

		bus.AdvanceDmaTo(128);

		var executor = bus.CausalBusExecutor;
		Assert.Equal(0, executor.CopperMoveEventsScheduled);
		Assert.Equal(1, executor.CopperMoveEventsCommitted);
		Assert.False(executor.HasPendingCopperMove);
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Control).Pending);
		Assert.Equal((ushort)0x180, executor.LastCopperMoveRegister);
		Assert.Equal((ushort)0x0F00, executor.LastCopperMoveValue);
		Assert.True(executor.LastCopperMoveCycle >= 0);
	}

	[Fact]
	public void LiveCopperDmaconDisableCommitsBeforeCopperStopsFetching()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		const uint copperList = 0x2400;
		WriteCopperList(
			bus,
			copperList,
			(0x0096, 0x0080),
			(0x0180, 0x0F00),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, copperList);
		bus.WriteWord(0x00DFF096, 0x8280);
		bus.WriteWord(0x00DFF088, 0x0000);
		bus.EnableLiveAgnusDma();

		bus.AdvanceDmaTo(256);

		var executor = bus.CausalBusExecutor;
		Assert.Equal(1, executor.CopperDmaconEventsCommitted);
		Assert.Equal((ushort)0x096, executor.LastCopperMoveRegister);
		Assert.Equal((ushort)0x0080, executor.LastCopperMoveValue);
		Assert.Equal(0, executor.DisplayControlState.Dmacon & 0x0080);
		Assert.False(executor.HasPendingCopperMove);
	}

	[Fact]
	public void LiveCopperCopjmp2CommitsThroughAgnusBeforeChangingFetchList()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		const uint firstList = 0x2400;
		const uint secondList = 0x2500;
		WriteCopperList(
			bus,
			firstList,
			(0x008A, 0x0000),
			(0xFFFF, 0xFFFE));
		WriteCopperList(
			bus,
			secondList,
			(0x0180, 0x0F00),
			(0xFFFF, 0xFFFE));
		SetCopperPointer(bus, list: 1, firstList);
		SetCopperPointer(bus, list: 2, secondList);
		bus.WriteWord(0x00DFF096, 0x8280);
		bus.WriteWord(0x00DFF088, 0x0000);
		bus.EnableLiveAgnusDma();

		bus.AdvanceDmaTo(256);

		var executor = bus.CausalBusExecutor;
		Assert.Equal(1, executor.CopperCopjmpEventsCommitted);
		Assert.True(executor.CopperMoveEventsCommitted >= 2);
		Assert.Equal((ushort)0x180, executor.LastCopperMoveRegister);
		Assert.Equal((ushort)0x0F00, executor.LastCopperMoveValue);
		Assert.False(executor.HasPendingCopperMove);
	}

	[Fact]
	public void AgnusExecutorCommitsBlitterReadAndWriteWordsCausally()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);
		var executor = bus.CausalBusExecutor;

		var read = executor.ExecuteBlitterWord(
			0x3000,
			requestedCycle: 20,
			isWrite: false,
			writeValue: 0);
		Assert.True(read.Granted);
		Assert.Equal((ushort)0x1234, read.Value);
		Assert.Equal(1, executor.BlitterGrantedReads);
		Assert.Equal(0, executor.BlitterGrantedWrites);
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Blitter).Pending);

		var write = executor.ExecuteBlitterWord(
			0x3000,
			requestedCycle: read.Access.CompletedCycle,
			isWrite: true,
			writeValue: 0xA55A);
		Assert.True(write.Granted);
		Assert.Equal((ushort)0xA55A, bus.ReadCurrentChipDmaWord(0x3000));
		Assert.Equal(1, executor.BlitterGrantedWrites);
		Assert.Equal(0x3000u, executor.LastBlitterAddress);
		Assert.Equal((ushort)0xA55A, executor.LastBlitterValue);
		Assert.Equal(write.Access.GrantedCycle, executor.LastBlitterGrantedCycle);
		Assert.True(executor.ExecutedThroughCycle >= write.Access.GrantedCycle);
	}

	[Fact]
	public void AgnusExecutorCommitsPaulaWordsIntoIndependentChannelLatches()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3200, 0x1234);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3400, 0xA55A);
		var executor = bus.CausalBusExecutor;

		var channel0 = executor.ExecutePaulaWord(0, 0x3200, requestedCycle: 0);
		var channel3 = executor.ExecutePaulaWord(3, 0x3400, channel0.Access.CompletedCycle);

		Assert.Equal((ushort)0x1234, channel0.Value);
		Assert.Equal((ushort)0xA55A, channel3.Value);
		Assert.Equal(2, executor.PaulaGrantedWords);
		Assert.Equal(1, executor.GetPaulaChannelGrantedWords(0));
		Assert.Equal(1, executor.GetPaulaChannelGrantedWords(3));
		Assert.Equal(0x3200u, executor.GetLastPaulaAddress(0));
		Assert.Equal(0x3400u, executor.GetLastPaulaAddress(3));
		Assert.Equal((ushort)0x1234, executor.GetLastPaulaValue(0));
		Assert.Equal((ushort)0xA55A, executor.GetLastPaulaValue(3));
		Assert.False(executor.GetPaulaIntent(0).Pending);
		Assert.False(executor.GetPaulaIntent(3).Pending);
	}

	[Fact]
	public void DeniedPaulaWordRemainsPendingForItsChannelUntilGranted()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3200, 0x5678);
		var executor = bus.CausalBusExecutor;
		var refreshCycle = (long)AgnusHrmOcsSlotTable.FirstRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;

		Assert.False(executor.TryExecutePaulaWordExact(0, 0x3200, refreshCycle, out _));
		ref readonly var pending = ref executor.GetPaulaIntent(0);
		Assert.True(pending.Pending);
		Assert.Equal(AmigaBusRequester.Paula, pending.Requester);
		Assert.Equal((byte)0, pending.Channel);
		Assert.Equal(0x3200u, pending.Address);
		Assert.True(pending.EarliestCycle > refreshCycle);
		Assert.Equal(1, executor.PaulaDeniedWords);

		var retry = executor.ExecutePaulaWord(0, 0x3200, pending.EarliestCycle);
		Assert.Equal((ushort)0x5678, retry.Value);
		Assert.Equal(1, executor.PaulaGrantedWords);
		Assert.False(executor.GetPaulaIntent(0).Pending);
	}

	[Fact]
	public void AgnusExecutorCommitsDiskReadAndWriteWordsAtFixedSlots()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3600, 0x5AA5);
		var executor = bus.CausalBusExecutor;
		var firstDiskSlot = bus.PredictDiskDmaGrantCycle(0);

		Assert.True(executor.TryExecuteDiskWordExact(
			0x3600,
			writeMode: true,
			diskWordValue: 0,
			firstDiskSlot,
			out var chipRead));
		Assert.Equal((ushort)0x5AA5, chipRead.Value);
		Assert.Equal(1, executor.DiskGrantedReads);

		var secondDiskSlot = bus.PredictDiskDmaGrantCycle(chipRead.CompletedCycle);
		Assert.True(executor.TryExecuteDiskWordExact(
			0x3800,
			writeMode: false,
			diskWordValue: 0xA55A,
			secondDiskSlot,
			out var chipWrite));
		Assert.Equal((ushort)0xA55A, bus.ReadCurrentChipDmaWord(0x3800));
		Assert.Equal(1, executor.DiskGrantedWrites);
		Assert.Equal(0x3800u, executor.LastDiskAddress);
		Assert.Equal((ushort)0xA55A, executor.LastDiskValue);
		Assert.Equal(chipWrite.GrantedCycle, executor.LastDiskGrantedCycle);
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Disk).Pending);
	}

	[Fact]
	public void DeniedDiskWordRemainsPendingUntilNextFixedDiskSlot()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		var refreshCycle = (long)AgnusHrmOcsSlotTable.FirstRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;

		Assert.False(executor.TryExecuteDiskWordExact(
			0x3800,
			writeMode: false,
			diskWordValue: 0x1357,
			refreshCycle,
			out _));
		ref readonly var pending = ref executor.GetIntent(AgnusBusAgendaSource.Disk);
		Assert.True(pending.Pending);
		Assert.Equal(AmigaBusRequester.Disk, pending.Requester);
		Assert.True((pending.Flags & AgnusBusIntentFlags.Write) != 0);
		Assert.Equal(0x3800u, pending.Address);
		Assert.Equal(bus.PredictDiskDmaGrantCycle(refreshCycle + AgnusChipSlotScheduler.SlotCycles), pending.EarliestCycle);
		Assert.Equal(1, executor.DiskDeniedWords);

		Assert.True(executor.TryExecuteDiskWordExact(
			0x3800,
			writeMode: false,
			diskWordValue: 0x1357,
			pending.EarliestCycle,
			out _));
		Assert.Equal((ushort)0x1357, bus.ReadCurrentChipDmaWord(0x3800));
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Disk).Pending);
	}

	[Fact]
	public void DeniedBlitterWordRemainsPendingUntilAgnusGrantsRetry()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);
		var executor = bus.CausalBusExecutor;
		var refreshCycle = (long)AgnusHrmOcsSlotTable.FirstRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;

		Assert.False(executor.TryExecuteBlitterWordExact(
			0x3000,
			refreshCycle,
			refreshCycle,
			isWrite: false,
			writeValue: 0,
			displayPrepared: false,
			out _));
		ref readonly var pending = ref executor.GetIntent(AgnusBusAgendaSource.Blitter);
		Assert.True(pending.Pending);
		Assert.Equal(AmigaBusRequester.Blitter, pending.Requester);
		Assert.Equal(0x3000u, pending.Address);
		Assert.True(pending.EarliestCycle > refreshCycle);
		Assert.Equal(1, executor.BlitterDeniedWords);

		var retry = executor.ExecuteBlitterWord(
			0x3000,
			pending.EarliestCycle,
			isWrite: false,
			writeValue: 0);
		Assert.True(retry.Granted);
		Assert.Equal((ushort)0x1234, retry.Value);
		Assert.False(executor.GetIntent(AgnusBusAgendaSource.Blitter).Pending);
	}

	[Fact]
	public void RowBitplaneDmaBatchDeniedByRefreshMatchesScalarRead()
	{
		var scalarBus = new AmigaBus();
		var batchBus = new AmigaBus();
		var refreshCycle = (long)AgnusHrmOcsSlotTable.FirstRefreshHorizontal *
			AgnusChipSlotScheduler.SlotCycles;
		var entries = new[]
		{
			new RowDmaBitplaneEntry((int)refreshCycle, plane: 0, word: 0, slot: 0, address: 0x1000, rowPresent: true)
		};
		var values = new ushort[entries.Length];
		var granted = new bool[entries.Length];

		Assert.False(scalarBus.TryReadLiveBitplaneDmaWord(0x1000, refreshCycle, out var scalarValue, out var scalarGrantedCycle));
		batchBus.ExecuteRowBitplaneDmaFetches(
			entries,
			lineStartCycle: 0,
			values,
			granted,
			out var grantedCount,
			out var firstGrantedCycle,
			out var lastGrantedCycle);

		Assert.False(granted[0]);
		Assert.Equal(scalarValue, values[0]);
		Assert.Equal(refreshCycle, scalarGrantedCycle);
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

		bus.ExecuteRowBitplaneDmaFetches(
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

		bus.ExecuteRowBitplaneDmaFetches(
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
	public void RowBitplaneDmaBatchesSampleChipRamCausally()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var firstEntry = new[]
		{
			new RowDmaBitplaneEntry(20, plane: 0, word: 0, slot: 0, address: 0x2400, rowPresent: true)
		};
		var firstValues = new ushort[1];
		var firstGranted = new bool[1];

		bus.ExecuteRowBitplaneDmaFetches(
			firstEntry,
			lineStartCycle: 0,
			firstValues,
			firstGranted,
			out var firstGrantedCount,
			out var firstGrantedCycle,
			out _);

		var cpuCycle = 32L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);
		var secondEntry = new[]
		{
			new RowDmaBitplaneEntry(56, plane: 0, word: 1, slot: 0, address: 0x2400, rowPresent: true)
		};
		var secondValues = new ushort[1];
		var secondGranted = new bool[1];
		bus.ExecuteRowBitplaneDmaFetches(
			secondEntry,
			lineStartCycle: 0,
			secondValues,
			secondGranted,
			out var secondGrantedCount,
			out var secondGrantedCycle,
			out _);

		Assert.Equal(1, firstGrantedCount);
		Assert.Equal(1, secondGrantedCount);
		Assert.Equal(20, firstGrantedCycle);
		Assert.Equal(56, secondGrantedCycle);
		Assert.True(firstGranted[0]);
		Assert.True(secondGranted[0]);
		Assert.Equal(0x1234, firstValues[0]);
		Assert.Equal(0x5678, secondValues[0]);
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
	public void RepeatedIntreqPollingSkipsWhenNoHardwareEventIsDue()
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

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void DeferredDmaReadOwnershipShadowIsSafeAcrossOcsEcsPalNtscProfiles(int profile)
	{
		var chipset = profile switch
		{
			0 => AmigaChipset.OcsPal,
			1 => AmigaChipset.OcsNtsc,
			2 => AmigaChipset.EcsPal,
			_ => AmigaChipset.EcsNtsc
		};
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableHardwareSpecialization: true,
			chipset: chipset,
			verifyDeferredDmaReads: true);
		bus.WriteWord(0x00DFF096, 0x8300);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x0038);
		bus.WriteWord(0x00DFF0E0, 0x0000);
		bus.WriteWord(0x00DFF0E2, 0x4000);
		bus.WriteWord(0x00DFF100, 0x1000);
		bus.EnableLiveAgnusDma();

		var cycle = LowResPlane1FetchCycle(AmigaConstants.PalLowResOverscanBorderY);
		for (var i = 0; i < 96; i++)
		{
			_ = bus.ReadWord(0x2000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
		}

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionVerificationMismatches);
		Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionUsed);
		if (chipset.VideoStandard == VideoStandard.Pal)
		{
			Assert.True(scheduler.DeferredCpuWaitFixedImageProductionVerificationMatches > 0);
		}
		else
		{
			Assert.Equal(0, scheduler.DeferredCpuWaitFixedImageProductionVerificationMatches);
			Assert.True(scheduler.DeferredCpuWaitFixedImageProductionFallbackFrame > 0);
		}
	}

	[Fact]
	public void CpuCustomRegisterReadExecutesDueCopperMovesBeforeSampling()
	{
		var bus = new AmigaBus();
		StartCopperListAtFrameStart(
			bus,
			(0x009A, (ushort)(0x8000 | AmigaConstants.IntreqCopper)),
			(0xFFFF, 0xFFFE));

		var cycle = 64L;
		var intenar = bus.ReadWord(0x00DFF01C, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, intenar & AmigaConstants.IntreqCopper);
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
		Assert.True(after.DrainCount - before.DrainCount <= 1);
		Assert.Equal(before.RasterEvents, after.RasterEvents);
		Assert.Equal(before.CiaEvents, after.CiaEvents);
		Assert.Equal(before.PaulaEvents, after.PaulaEvents);
		Assert.Equal(before.DiskEvents, after.DiskEvents);
		Assert.Equal(before.AgnusEvents + 1, after.AgnusEvents);
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
		Assert.Equal(0, after.AgnusExecutorShadowMismatches);
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
	public void CpuInterruptWriteLeavesUnrelatedAgnusAgendaLeavesUntouched()
	{
		var bus = new AmigaBus();
		var cycle = 20L;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		cycle += 4;
		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var before = bus.CaptureHardwareSchedulerSnapshot();

		cycle += 4;
		bus.WriteWord(0x00DFF09C, (ushort)(0x8000 | AmigaConstants.IntreqCopper), ref cycle, AmigaBusAccessKind.CpuDataWrite);

		var after = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(before.AgnusExecutorAgendaUpdates, after.AgnusExecutorAgendaUpdates);
	}

	[Fact]
	public void IntreqTimedReadPublishesVblankThroughItsSampleCycle()
	{
		var bus = new AmigaBus();
		var frameCycle = (long)AmigaConstants.A500PalCpuCyclesPerFrame;

		var initialCycle = 0L;
		var initial = bus.ReadWord(0x00DFF01E, ref initialCycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(0, initial & AmigaConstants.IntreqVerticalBlank);

		var cycle = frameCycle;
		var value = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, value & AmigaConstants.IntreqVerticalBlank);
	}

	[Fact]
	public void IntreqReadAfterSameCyclePseudoFastAccessPublishesVblank()
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

		Assert.NotEqual(0, value & AmigaConstants.IntreqVerticalBlank);
	}

	[Fact]
	public void RepeatedIntreqPollingAdvancesCiaTimersThroughTheReadBarrier()
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

		var interrupt = Assert.Single(bus.DrainCiaInterrupts());
		Assert.Equal(AmigaCiaId.A, interrupt.Cia);
		Assert.Equal(AmigaCia.TimerAInterruptMask, interrupt.IcrBits);
		Assert.Equal(20, interrupt.Cycle);
	}

	[Fact]
	public void IntreqPollingAdvancesDiskDmaAndPublishesBlockInterrupt()
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
		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
		Assert.Equal(0, bus.Paula.Intreq & DskBlkInterrupt);

		var completionCycle = bus.Disk.CaptureSnapshot().ActiveDmaCompletionCycle;
		Assert.True(completionCycle > readyCycle);
		var completionReadCycle = completionCycle;
		var value = bus.ReadWord(0x00DFF01E, ref completionReadCycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, value & DskBlkInterrupt);
		Assert.Equal(1, bus.Disk.CaptureSnapshot().TransferCount);
	}

	[Fact]
	public void FreshIntreqPollAtNiceBlitterFinalSlotWaitsForBlitterCompletion()
	{
		var bus = new AmigaBus();
		StartLongBlit(bus);
		var completionCycle = bus.Blitter.GetPredictedCompletionCycle();
		Assert.True(completionCycle > 0);

		var beforeCycle = Math.Max(0, completionCycle - 16);
		_ = bus.ReadWord(0x00DFF01E, ref beforeCycle, AmigaBusAccessKind.CpuDataRead);

		var cycle = completionCycle;
		var value = bus.ReadWord(0x00DFF01E, ref cycle, AmigaBusAccessKind.CpuDataRead);
		var atNominalCompletion = bus.Blitter.CaptureSnapshot();
		Assert.NotEqual(0, value & AmigaConstants.IntreqBlitter);
		Assert.False(atNominalCompletion.Busy);
		Assert.Equal(completionCycle, atNominalCompletion.LastDmaCycle);
		Assert.Equal(
			completionCycle + (2 * AgnusChipSlotScheduler.SlotCycles),
			cycle);
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
	public void BeamAndInputRegisterReadsAdvanceTheSharedCustomChipBarrier()
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
		Assert.True(audioAfter.NextSampleCycle >= audioBefore.NextSampleCycle);
		Assert.NotEqual(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);
	}

	[Fact]
	public void DskbytrReadPublishesPassiveInputAndClearsReadyOnce()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);

		var firstCycle = targetCycle;
		var first = bus.ReadWord(0x00DFF01A, ref firstCycle, AmigaBusAccessKind.CpuDataRead);
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
		Assert.NotEqual(0, dskbytr & 0x1000);
	}

	[Fact]
	public void RegisterLatchReadsPublishScheduledWritesThroughTheSampleCycle()
	{
		var bus = new AmigaBus();
		bus.Paula.ScheduleWrite(20, 0x09E, 0x8001);

		var beforeCycle = 10L;
		Assert.Equal(0, bus.ReadWord(0x00DFF010, ref beforeCycle, AmigaBusAccessKind.CpuDataRead));

		var atCycle = 20L;
		Assert.Equal(1, bus.ReadWord(0x00DFF010, ref atCycle, AmigaBusAccessKind.CpuDataRead));
	}

	[Fact]
	public void RegisterLatchReadsAdvanceAudioAndPassiveDiskInput()
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
		Assert.True(audioAfter.NextSampleCycle >= audioBefore.NextSampleCycle);
		Assert.NotEqual(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);
	}

	[Fact]
	public void DiskStatusRegisterReadsPublishPassiveInput()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		var targetCycle = readyCycle + DiskByteCycleCount(trackByteLength: 2, byteCount: 1);

		var cycle = targetCycle;
		_ = bus.ReadWord(0x00DFF008, ref cycle, AmigaBusAccessKind.CpuDataRead);

		Assert.NotEqual(0, bus.Disk.CaptureSnapshot().Dskbytr & 0x8000);

		var dskCycle = cycle;
		var dskbytr = bus.ReadWord(0x00DFF01A, ref dskCycle, AmigaBusAccessKind.CpuDataRead);
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
	public void DmaconrPollingAdvancesBlitterWithoutCouplingHostAudioPlayback()
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
		Assert.Equal(2, fetches[0].RequestedCycle);
		Assert.Equal(7, fetches[0].GrantedCycle);
		Assert.Equal(10, fetches[0].CompletedCycle);
		Assert.Equal(14, fetches[1].RequestedCycle);
		Assert.True(fetches[1].RequestedCycle >= fetches[0].CompletedCycle);
		Assert.Equal(19, fetches[1].GrantedCycle);
		Assert.Equal(22, fetches[1].CompletedCycle);
		Assert.NotEqual(fetches[0].RequestedCycle, fetches[1].RequestedCycle);
		Assert.Equal(10, cpu.State.Cycles);
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
	public void FixedPlanRunWindowAdmitsOnlyUntracedZeroWaitInstructionTargets()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			captureBusAccesses: false,
			realFastRamSize: 0x10000);
		bus.ConfigureAutoconfigFastRamForHost();
		const uint romAddress = 0x00F8_0000;
		bus.MapReadOnlyMemory(romAddress, new byte[0x1000]);
		var runBus = (IM68kFixedPlanRunBus)bus;

		Assert.False(runBus.TryGetFixedPlanRunWindow(0x1000, out _));
		Assert.False(runBus.TryGetFixedPlanRunWindow(bus.ExpansionRamBase, out _));
		Assert.False(runBus.TryGetFixedPlanRunWindow(0x00DF_F000, out _));
		Assert.False(runBus.TryGetFixedPlanRunWindow(0x00BF_E001, out _));
		Assert.True(runBus.TryGetFixedPlanRunWindow(AmigaConstants.A500RealFastRamBase, out _));
		Assert.True(runBus.TryGetFixedPlanRunWindow(romAddress, out _));

		var tracedBus = new AmigaBus(
			captureBusAccesses: true,
			realFastRamSize: 0x10000);
		tracedBus.ConfigureAutoconfigFastRamForHost();
		Assert.False(((IM68kFixedPlanRunBus)tracedBus).TryGetFixedPlanRunWindow(
			AmigaConstants.A500RealFastRamBase,
			out _));
	}

	[Fact]
	public void InterpreterFastMemoryAdmitsOnlyZeroWaitMemoryTargets()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			captureBusAccesses: false,
			realFastRamSize: 0x10000,
			rtgVramSize: 0x100000);
		bus.ConfigureAutoconfigFastRamForHost();
		bus.ConfigureAutoconfigRtgForHost();
		var rtgAddress = bus.AllocateRtgVram(0x1000);
		const uint romAddress = 0x00F8_0000;
		bus.MapReadOnlyMemory(romAddress, new byte[0x1000]);
		var fastBus = (IM68kFastMemoryBus)bus;

		Assert.True(fastBus.TryReadFastLong(
			AmigaConstants.A500RealFastRamBase,
			M68kBusAccessKind.CpuDataRead,
			out _));
		Assert.True(fastBus.TryReadFastLong(
			romAddress,
			M68kBusAccessKind.CpuDataRead,
			out _));
		Assert.True(fastBus.TryReadFastLong(
			rtgAddress,
			M68kBusAccessKind.CpuDataRead,
			out _));
		Assert.False(fastBus.TryReadFastLong(0x1000, M68kBusAccessKind.CpuDataRead, out _));
		Assert.False(fastBus.TryReadFastLong(bus.ExpansionRamBase, M68kBusAccessKind.CpuDataRead, out _));
		Assert.False(fastBus.TryReadFastLong(0x00DF_F000, M68kBusAccessKind.CpuDataRead, out _));
		Assert.False(fastBus.TryReadFastLong(0x00BF_E001, M68kBusAccessKind.CpuDataRead, out _));

		Assert.True(fastBus.TryWriteFastLong(
			AmigaConstants.A500RealFastRamBase,
			0x1234_5678,
			M68kBusAccessKind.CpuDataWrite));
		Assert.True(fastBus.TryWriteFastLong(
			rtgAddress,
			0x89AB_CDEF,
			M68kBusAccessKind.CpuDataWrite));
		Assert.False(fastBus.TryWriteFastLong(
			romAddress,
			0,
			M68kBusAccessKind.CpuDataWrite));
		Assert.False(fastBus.TryWriteFastLong(
			bus.ExpansionRamBase,
			0,
			M68kBusAccessKind.CpuDataWrite));
	}

	[Fact]
	public void AccurateM68000CpuBusPhasesPreservePhysicalRequestAndMirrorBusGrant()
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
			var phaseAccess = phase.BusAccess.GetValueOrDefault();
			Assert.Equal(access.Request.Address, phase.CpuPhase.Address);
			Assert.True(phase.CpuPhase.RequestedCycle <= access.Request.RequestedCycle);
			Assert.Equal(access.CompletedCycle, phase.CpuPhase.CompletedCycle);
			Assert.Equal(access.Request.RequestedCycle, phaseAccess.Request.RequestedCycle);
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
		Assert.Equal(AmigaBusAccessSize.Word, writeAccess.Request.Size);
		var wordWrites = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu &&
				access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
				access.Request.Address is 0x2400 or 0x2402)
			.ToArray();
		Assert.Equal(2, wordWrites.Length);
		Assert.All(wordWrites, access => Assert.Equal(AmigaBusAccessSize.Word, access.Request.Size));
		Assert.Equal(0x2400u, wordWrites[0].Request.Address);
		Assert.Equal(0x2402u, wordWrites[1].Request.Address);
		Assert.True(wordWrites[1].GrantedCycle > wordWrites[0].GrantedCycle);
		Assert.Equal(wordWrites[1].GrantedCycle, writePhase.SecondWordCycle);
		Assert.Equal(0x1234, bus.ReadCurrentChipDmaWord(0x2400));
		Assert.Equal(0x5678, bus.ReadCurrentChipDmaWord(0x2402));
	}

	[Fact]
	public void AccurateM68000SelfModifiedQueuedPrefetchStaysDeterministic()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x21, 0xFC, 0x70, 0x05, 0x4E, 0x71, 0x10, 0x08); // MOVE.L #$70054E71,$1008.W
		Write(bus.ChipRam, 0x1008, 0x70, 0x01); // MOVEQ #1,D0 queued before the destination write.
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
		var queuedFetch = bus.CpuBusPhases.First(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.CpuPhase.Address == 0x1008u);
		var modifyingWrite = bus.CpuBusPhases.First(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
			phase.CpuPhase.Address == 0x1008u);
		Assert.True(queuedFetch.CpuPhase.CompletedCycle <= modifyingWrite.CpuPhase.RequestedCycle);
	}

	[Theory]
	[InlineData(false, 64)]
	[InlineData(true, 64)]
	[InlineData(true, 120)]
	[InlineData(true, 136)]
	[InlineData(true, 152)]
	public void AccurateM68000VerticalSyncLoopKeepsPalRasterPhase(bool captureBusAccesses, int startOffset)
	{
		const int programAddress = 0x1000;
		var bus = new AmigaBus(captureBusAccesses: captureBusAccesses, enableLiveAgnusDma: true);
		Write(bus.ChipRam, programAddress, CreateVerticalSyncPhaseLoopProgram());
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(programAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (240L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + startOffset;
		var stopCycle = cpu.State.Cycles + (30L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var exitProgramCounter = (uint)(programAddress + 226);

		while (cpu.State.ProgramCounter != exitProgramCounter && cpu.State.Cycles < stopCycle)
		{
			cpu.ExecuteInstruction();
		}

		var markers = bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180 && write.Value == 0x0404)
			.ToArray();
		var diagnostic = BuildVerticalSyncPhaseDiagnostic(bus, markers);

		Assert.True(cpu.State.ProgramCounter == exitProgramCounter, diagnostic);
		Assert.True(markers.Length >= 8, diagnostic);
		for (var i = 1; i < markers.Length; i++)
		{
			var delta = markers[i].Cycle - markers[i - 1].Cycle;
			Assert.True(
				Math.Abs(delta - AmigaConstants.A500PalCpuCyclesPerRasterLine) <= 2,
				diagnostic);
		}
	}

	[Fact]
	public void AccurateM68000Probe10CompleteFrameColorLedgerAndCausalPresentationMatchOptimizedExecutionModes()
	{
		var scalar = RunProbe10SynccpuMode(
			"romScalarComplete",
			programInRom: true,
			captureBusAccesses: true,
			enableHardwareSpecialization: false,
			enableDeferredCpuBusBatch: false,
			stopOnFirstPurpleWrite: false,
			configureDisplayForTimeline: true);
		var fast = RunProbe10SynccpuMode(
			"romFastComplete",
			programInRom: true,
			captureBusAccesses: false,
			enableHardwareSpecialization: false,
			enableDeferredCpuBusBatch: false,
			stopOnFirstPurpleWrite: false,
			configureDisplayForTimeline: true);
		var specializedDeferred = RunProbe10SynccpuMode(
			"romSpecializedDeferredComplete",
			programInRom: true,
			captureBusAccesses: false,
			enableHardwareSpecialization: true,
			enableDeferredCpuBusBatch: true,
			stopOnFirstPurpleWrite: false,
			configureDisplayForTimeline: true);
		var specializedDeferredDiagnosticTrace = RunProbe10SynccpuMode(
			"romSpecializedDeferredDiagnosticTraceComplete",
			programInRom: true,
			captureBusAccesses: false,
			enableHardwareSpecialization: true,
			enableDeferredCpuBusBatch: true,
			enableDiagnosticTrace: true,
			stopOnFirstPurpleWrite: false,
			configureDisplayForTimeline: true);

		Assert.True(scalar.ColorWrites.Length > 3, FormatProbe10ModeRun(scalar));
		Assert.Contains(scalar.PresentationPixels, pixel => pixel != 0xFF000000u);
		AssertProbe10CompleteModeParity(scalar, fast);
		AssertProbe10CompleteModeParity(scalar, specializedDeferred);
		AssertProbe10CompleteModeParity(specializedDeferred, specializedDeferredDiagnosticTrace);
	}

	[Fact]
	public void AccurateM68000Probe10RepeatedIrq2LifecycleMatchesCapturedAndFastBusPaths()
	{
		var captured = RunProbe10RepeatedIrq2Lifecycle(captureBusAccesses: true);
		var fast = RunProbe10RepeatedIrq2Lifecycle(captureBusAccesses: false);
		var diagnostic = $"captured=[{string.Join(';', captured)}], fast=[{string.Join(';', fast)}]";

		Assert.True(captured.Length == 3, diagnostic);
		Assert.True(fast.Length == 3, diagnostic);
		Assert.True(captured.SequenceEqual(fast), diagnostic);
	}

	[Fact]
	public void AccurateM68000Probe10RuntimeBatchingMatchesCapturedAndFastBusPaths()
	{
		var captured = RunProbe10RuntimeBatchedIrq2(captureBusAccesses: true);
		var fast = RunProbe10RuntimeBatchedIrq2(captureBusAccesses: false);
		var diagnostic = BuildProbe10RuntimeBatchDivergence(captured, fast);

		Assert.True(captured.ColorWrites.SequenceEqual(fast.ColorWrites), diagnostic);
		Assert.True(captured.VhposrReads.SequenceEqual(fast.VhposrReads), diagnostic);
		Assert.True(captured.CpuCycle == fast.CpuCycle, diagnostic);
		Assert.True(captured.ProgramCounter == fast.ProgramCounter, diagnostic);
		Assert.True(captured.Intreq == fast.Intreq, diagnostic);
	}

	[Fact]
	public void AccurateM68000CycleProbeVposrSampleDocumentsCycle01vAndCycleD9vDifference()
	{
		var cycle01v = RunCycleVposrProbeSample(probeNops: 68);
		var cycleD9v = RunCycleVposrProbeSample(probeNops: 66);
		var diagnostic = BuildCycleVposrProbeSampleDiagnostic(cycle01v, cycleD9v);

		Assert.Equal(0x8001, cycle01v.Read.Value);
		Assert.Equal(0x8001u, cycle01v.D0);
		Assert.Equal(0x8000, cycleD9v.Read.Value);
		Assert.Equal(0x8000u, cycleD9v.D0);
		Assert.Equal(10, cycle01v.Read.SampleCycle - cycleD9v.Read.SampleCycle);
		Assert.Equal((256, 1), (cycle01v.SampleBeam.BeamLine, cycle01v.SampleBeam.BeamHorizontal));
		Assert.Equal((255, 223), (cycleD9v.SampleBeam.BeamLine, cycleD9v.SampleBeam.BeamHorizontal));
		_ = diagnostic;
	}

	[Fact]
	public void AccurateM68000CycleD9vRepeatedMainLoopKeepsProbeSamplePhase()
	{
		var result = RunRepeatedCycleVposrProbeSamples(probeNops: 66, sampleCount: 2);
		var diagnostic = string.Join(
			"; ",
			result.Reads.Select((read, index) =>
			{
				var request = result.Bus.GetBeamPosition(read.RequestedCycle);
				var grant = result.Bus.GetBeamPosition(read.GrantedCycle);
				var sample = result.Bus.GetBeamPosition(read.SampleCycle);
				return $"#{index}:value=0x{read.Value:X4}," +
					$"request=v{request.BeamLine}h{request.BeamHorizontal}," +
					$"grant=v{grant.BeamLine}h{grant.BeamHorizontal}," +
					$"sample=v{sample.BeamLine}h{sample.BeamHorizontal}";
			}));

		Assert.Equal(2, result.Reads.Length);
		Assert.All(result.Reads, read => Assert.Equal((ushort)0x8000, read.Value));
		_ = diagnostic;
	}

	[Fact]
	public void AccurateM68000CycleD9vRepeatedMainLoopDocumentsStartupConvergenceAcrossVblankIrq()
	{
		var dmaAdvanceOnly = RunRepeatedCycleVposrProbeSamples(
			probeNops: 66,
			sampleCount: 2,
			advanceDmaPerInstruction: true);
		var copperOnly = RunRepeatedCycleVposrProbeSamples(
			probeNops: 66,
			sampleCount: 2,
			enableCycleCopper: true,
			advanceDmaPerInstruction: true);
		var irqOnly = RunRepeatedCycleVposrProbeSamples(
			probeNops: 66,
			sampleCount: 8,
			enableVblankIrq: true);
		var copperAndIrq = RunRepeatedCycleVposrProbeSamples(
			probeNops: 66,
			sampleCount: 8,
			enableCycleCopper: true,
			enableVblankIrq: true);
		var firstIrqPhases = BuildCpuBusPhaseTimeline(
			copperAndIrq.FirstInterruptPhases,
			copperAndIrq.FirstInterruptPhases.Length == 0
				? 0
				: copperAndIrq.FirstInterruptPhases[0].CpuPhase.RequestedCycle);
		var diagnostic = string.Join(
			" | ",
			new[]
			{
				FormatRepeatedCycleVposrProbeSamples("dma", dmaAdvanceOnly),
				FormatRepeatedCycleVposrProbeSamples("copper", copperOnly),
				FormatRepeatedCycleVposrProbeSamples("irq", irqOnly),
				FormatRepeatedCycleVposrProbeSamples("both", copperAndIrq),
			}) + $" | firstIrqPhases={firstIrqPhases}";

		Assert.True(irqOnly.InterruptDispatches > 0, diagnostic);
		Assert.True(copperAndIrq.InterruptDispatches > 0, diagnostic);
		Assert.Equal(new ushort[] { 0x8000, 0x8000 }, dmaAdvanceOnly.Reads.Select(read => read.Value));
		Assert.Equal(new ushort[] { 0x8000, 0x8000 }, copperOnly.Reads.Select(read => read.Value));
		// The exact cycleD9v hardware image keeps the VPOSR sample at $8000
		// across VBLANK IRQ/RTE. The former alternating $8001/$8000
		// expectation encoded compensation for an overlong DBRA exception tail.
		var expectedIrqValues = Enumerable.Repeat((ushort)0x8000, 8).ToArray();
		Assert.True(expectedIrqValues.SequenceEqual(irqOnly.Reads.Select(read => read.Value)), diagnostic);
		Assert.True(expectedIrqValues.SequenceEqual(copperAndIrq.Reads.Select(read => read.Value)), diagnostic);
		_ = diagnostic;
	}

	[Fact]
	public void AccurateM68000Cycle01vFullIrqDelayPathMatchesHardwareSpecializationAndDeferredBatching()
	{
		var scalar = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true);
		var fastScalar = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			captureBusAccesses: false);
		var specialized = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			enableHardwareSpecialization: true,
			captureBusAccesses: false);
		var deferred = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			enableDeferredCpuBusBatch: true,
			captureBusAccesses: false);
		var specializedDeferred = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			enableHardwareSpecialization: true,
			enableDeferredCpuBusBatch: true,
			captureBusAccesses: false);
		var runs = new[]
		{
			(Name: "scalar", Result: scalar),
			(Name: "fastScalar", Result: fastScalar),
			(Name: "specialized", Result: specialized),
			(Name: "deferred", Result: deferred),
			(Name: "specializedDeferred", Result: specializedDeferred)
		};
		var names = new[]
		{
			"afterVposrRead",
			"irqEntry",
			"irqRteComplete",
			"afterDelayLoop",
			"loop2Entry"
		};
		var expected = GetCycle01vBoundaryCycles(scalar.Boundaries, names);
		var diagnostic = string.Join(
			" | ",
			runs.Select(run =>
			{
				var scheduler = run.Result.Bus.CaptureHardwareSchedulerSnapshot();
				return $"{run.Name}:boundaries={FormatCycle01vBoundaryCycles(run.Result.Boundaries, names)}, " +
					$"loop2={run.Result.Loop2EntryCycle}, irq={run.Result.InterruptDispatches}, " +
					$"batch={scheduler.DeferredCpuBusBatchAttempts}/{scheduler.DeferredCpuBusBatchUsed}/" +
					$"{scheduler.DeferredCpuBusBatchInstructions}, wakeVblank={scheduler.DeferredCpuBusBatchWakeVerticalBlank}";
			}));

		Assert.Equal(1, scalar.InterruptDispatches);
		Assert.True(expected.All(cycle => cycle >= 0), diagnostic);
		Assert.All(runs, run => Assert.Equal(expected, GetCycle01vBoundaryCycles(run.Result.Boundaries, names)));
		Assert.All(runs, run => Assert.Equal(scalar.Loop2EntryCycle, run.Result.Loop2EntryCycle));
		Assert.Equal(0, deferred.Bus.CaptureHardwareSchedulerSnapshot().DeferredCpuBusBatchUsed);
		Assert.Equal(0, specializedDeferred.Bus.CaptureHardwareSchedulerSnapshot().DeferredCpuBusBatchUsed);
		_ = diagnostic;
	}

	[Fact]
	public void AccurateM68000Cycle01vFullPathAtAdfAddressDoesNotSelfBlockSynccpu4Prefetch()
	{
		const uint targetInstructionPc = 0x070358;
		const uint targetFetchAddress = 0x07035C;
		var result = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			enableCycleCopper: true,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			requestSyntheticVblankAfterProbe: true,
			synccpuAddress: 0x0702C0);
		var phases = result.Bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.InstructionProgramCounter == targetInstructionPc &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
				phase.CpuPhase.Address == targetFetchAddress)
			.ToArray();
		var waits = phases.Select(phase => phase.BusAccess?.WaitCycles ?? 0).ToArray();
		var diagnostic = string.Join(";", phases.Select(phase =>
		{
			var grant = phase.BusAccess?.GrantedCycle ?? phase.CpuPhase.RequestedCycle;
			var requestBeam = result.Bus.GetBeamPosition(phase.CpuPhase.RequestedCycle);
			var slots = new List<string>();
			for (var cycle = AgnusChipSlotScheduler.AlignToSlot(phase.CpuPhase.RequestedCycle);
				cycle <= grant;
				cycle += AgnusChipSlotScheduler.SlotCycles)
			{
				result.Bus.TryGetCommittedAgnusSlotOwner(cycle, out var owner);
				slots.Add($"+{cycle - phase.CpuPhase.RequestedCycle}/" +
					$"h{AgnusHrmOcsSlotTable.GetHorizontal(cycle)}:{owner}");
			}

			return $"v{requestBeam.BeamLine}h{requestBeam.BeamHorizontal}:" +
				$"wait={grant - phase.CpuPhase.RequestedCycle}[{string.Join(',', slots)}]";
		}));

		Assert.NotEmpty(phases);
		Assert.True(waits.All(wait => wait <= 2), diagnostic);
	}

	[Fact]
	public void AccurateM68000Cycle01vDbraInterruptEntryPreservesCommittedPhysicalBusSequence()
	{
		var result = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			enableCycleCopper: true,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			requestSyntheticVblankAfterProbe: true,
			syntheticVblankIrqOffset: 26,
			cycle01vPostRteD3Override: 0x24DB);
		var irqAccept = result.Boundaries.Single(boundary => boundary.Name == "irqAccept").Cycle;
		var irqEntry = result.Boundaries.Single(boundary => boundary.Name == "irqEntry").Cycle;
		var phases = result.Bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.CompletedCycle >= irqAccept - 40 &&
				phase.CpuPhase.RequestedCycle <= irqEntry)
			.Select(phase => (
				phase.CpuPhase.AccessKind,
				phase.CpuPhase.Address,
				Request: phase.CpuPhase.RequestedCycle - irqAccept,
				Grant: (phase.BusAccess?.GrantedCycle ?? phase.CpuPhase.RequestedCycle) - irqAccept,
				Complete: phase.CpuPhase.CompletedCycle - irqAccept))
			.ToArray();
		var diagnostic = string.Join(",", phases);
		var committedTailIndex = Array.FindLastIndex(
			phases,
			phase => phase.AccessKind == M68kBusAccessKind.CpuInstructionFetch && phase.Address == 0x001098u);
		Assert.True(committedTailIndex >= 0, diagnostic);
		var committedTail = phases[committedTailIndex];
		var firstStackWrite = phases[committedTailIndex + 1];
		// The committed DBRA tail plus exception setup produces this
		// observable request gap in the synthetic physical-bus sequence.
		Assert.Equal(12, firstStackWrite.Request - committedTail.Complete);

		// The beam phase depends on preceding refresh/Copper ownership, but these
		// transfers are one indivisible physical sequence. In particular, the
		// committed DBRA extension survives interrupt recognition.
		Assert.Equal(
			new[]
			{
				(M68kBusAccessKind.CpuInstructionFetch, 0x001098u),
				(M68kBusAccessKind.CpuDataWrite, 0x002FFEu),
				(M68kBusAccessKind.CpuInterruptAcknowledge, 0xFFFFF7u),
				(M68kBusAccessKind.CpuDataWrite, 0x002FFAu),
				(M68kBusAccessKind.CpuDataWrite, 0x002FFCu),
				(M68kBusAccessKind.CpuDataRead, 0x00006Cu),
				(M68kBusAccessKind.CpuInstructionFetch, 0x001500u),
				(M68kBusAccessKind.CpuInstructionFetch, 0x001502u)
			},
			phases.Skip(committedTailIndex).Take(8).Select(phase => (phase.AccessKind, phase.Address)));
		_ = diagnostic;
	}

	[Fact]
	public void AccurateM68000Cycle01vHandlerTimingIsIndependentOfEqualPopulationD0Bits()
	{
		var results = new[] { 0x8001u, 0x0005u }
			.Select(d0 => (D0: d0, Run: RunCycle01vDelayLoopProbe(
				startLine: 232,
				startOffset: 56,
				markerCount: 1,
				enableCycleCopper: true,
				inlineLoop2: true,
				enableSyntheticVblankIrq: true,
				useCycle01vIrq3Handler: true,
				cycle01vIrq3InitialD0: d0,
				requestSyntheticVblankAfterProbe: true,
				syntheticVblankIrqOffset: 26,
				cycle01vPostRteD3Override: 0x24DB)))
			.ToArray();
		var durations = results.Select(result =>
		{
			var entry = result.Run.Boundaries.Single(boundary => boundary.Name == "irqEntry").Cycle;
			var rte = result.Run.Boundaries.Single(boundary => boundary.Name == "rte").Cycle;
			return rte - entry;
		}).ToArray();

		Assert.Equal(durations[0], durations[1]);
	}

	[Fact]
	public void AccurateM68000Cycle01vSteadyDbraLineMatchesPhysicalBusSlots()
	{
		var result = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			enableCycleCopper: true,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			requestSyntheticVblankAfterProbe: true,
			syntheticVblankIrqOffset: 26,
			cycle01vPostRteD3Override: 0x24DB);
		var actual = result.Bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.InstructionProgramCounter == 0x1096 &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch)
			.Select(phase =>
			{
				var grant = phase.BusAccess?.GrantedCycle ?? phase.CpuPhase.RequestedCycle;
				var beam = result.Bus.GetBeamPosition(grant);
				return (beam.BeamLine, beam.BeamHorizontal, phase.CpuPhase.Address);
			})
			.Where(phase => phase.BeamLine == 100 && phase.BeamHorizontal < 48)
			.Select(phase => $"h{phase.BeamHorizontal:X}:{phase.Address - 0x1096:X}")
			.ToArray();

		var expected = new[]
			{
				"h4:0", "h6:2", "hA:0", "hC:2", "hF:0", "h11:2",
				"h14:0", "h16:2", "h19:0", "h1B:2", "h1E:0", "h20:2",
				"h23:0", "h25:2", "h28:0", "h2A:2", "h2D:0", "h2F:2"
			};
		var normalizedActual = actual
			.Take(expected.Length)
			.Select(value =>
			{
				var separator = value.IndexOf(':');
				var horizontal = Convert.ToInt32(value.Substring(1, separator - 1), 16) + 3;
				return $"h{horizontal:X}{value[separator..]}";
			})
			.ToArray();

		Assert.True(
			expected.SequenceEqual(normalizedActual),
			$"expected=[{string.Join(',', expected)}], normalized=[{string.Join(',', normalizedActual)}], actual=[{string.Join(',', actual)}]");
	}

	[Fact]
	public void AccurateM68000Cycle01vPostRteBoundaryMatchesEmulatorReference()
	{
		var result = RunCycle01vDelayLoopProbe(
			startLine: 232,
			startOffset: 56,
			markerCount: 1,
			enableCycleCopper: true,
			inlineLoop2: true,
			enableSyntheticVblankIrq: true,
			useCycle01vIrq3Handler: true,
			requestSyntheticVblankAfterProbe: true,
			syntheticVblankIrqOffset: 26,
			cycle01vPostRteD3Override: 0x24DB);
		var postRte = result.Boundaries.Single(boundary => boundary.Name == "irqRteComplete");
		var postRteBeam = result.Bus.GetBeamPosition(postRte.Cycle);

		Assert.Equal((2, 70), (postRteBeam.BeamLine, postRteBeam.BeamHorizontal));
	}

	[Fact]
	public void AccurateM68000Vhpos2Irq3PrefixSamplesVhposrAtGrantedCycle()
	{
		const uint mainAddress = 0x1000;
		const uint handlerAddress = 0x1500;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, (int)mainAddress, 0x60, 0xFE); // BRA.S main
		Write(bus.ChipRam, (int)handlerAddress,
			0x48, 0xE7, 0xFF, 0xFE,             // MOVEM.L D0-A6,-(SP)
			0x33, 0x7C, 0x00, 0x20, 0x00, 0x9C, // MOVE.W #$0020,INTREQ(A1)
			0x30, 0x39, 0x00, 0xDF, 0xF0, 0x06, // MOVE.W $DFF006,D0
			0x60, 0xFE);                         // BRA.S here
		bus.WriteLong((24u + 3u) * 4u, handlerAddress);
		bus.WriteWord(0x00DFF09A, (ushort)(0xC000 | AmigaConstants.IntreqVerticalBlank));

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(mainAddress, 0x8000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.Cycles =
			(255L * AmigaConstants.A500PalCpuCyclesPerRasterLine) +
			(200L * AgnusChipSlotScheduler.SlotCycles);
		var frameStartCycle = bus.GetNextFrameStartCycle(cpu.State.Cycles);
		bus.RequestHardwareInterrupt(
			AmigaConstants.IntreqVerticalBlank,
			frameStartCycle);
		var interruptAcceptCycle = -1L;
		var interruptEntryCycle = -1L;
		var boundaries = new List<(uint Pc, long Start, long Stop)>();

		for (var instruction = 0; instruction < 100_000 && bus.CustomRegisterReads.Count == 0; instruction++)
		{
			var acceptCycle = cpu.State.Cycles;
			if (DispatchPendingHardwareInterruptIfNeeded(bus, cpu) && interruptAcceptCycle < 0)
			{
				interruptAcceptCycle = acceptCycle;
				interruptEntryCycle = cpu.State.Cycles;
			}

			var pc = cpu.State.ProgramCounter;
			var start = cpu.State.Cycles;
			cpu.ExecuteInstruction();
			if (pc >= handlerAddress && pc < handlerAddress + 16)
			{
				boundaries.Add((pc, start, cpu.State.Cycles));
			}
		}

		var read = Assert.Single(bus.CustomRegisterReads.Where(read => read.Address == 0x006));
		var beam = bus.GetBeamPosition(read.SampleCycle);
		var moveBoundary = Assert.Single(boundaries.Where(boundary => boundary.Pc == handlerAddress + 10));
		var movePhases = bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.InstructionProgramCounter == handlerAddress + 10)
			.Select(phase => (phase.CpuPhase.AccessKind, phase.CpuPhase.Address))
			.ToArray();
		var diagnostic =
			$"value=0x{read.Value:X4}, sample=v{beam.BeamLine}h{beam.BeamHorizontal}, " +
			$"request={read.RequestedCycle}, grant={read.GrantedCycle}, done={read.CompletedCycle}, " +
			$"frameStart={frameStartCycle}, accept={interruptAcceptCycle - frameStartCycle}, " +
			$"entry={interruptEntryCycle - frameStartCycle}, boundaries=" +
			string.Join(",", boundaries.Select(boundary =>
				$"0x{boundary.Pc:X4}:{boundary.Start - frameStartCycle}-{boundary.Stop - frameStartCycle}")) +
			$", movePhases={string.Join(";", bus.CpuBusPhases
				.Where(phase => phase.CpuPhase.InstructionProgramCounter == handlerAddress + 10)
				.Select(phase => $"{phase.CpuPhase.AccessKind}:0x{phase.CpuPhase.Address:X6}@" +
					$"{phase.CpuPhase.RequestedCycle - frameStartCycle}-{phase.CpuPhase.CompletedCycle - frameStartCycle}"))}";

		Assert.Equal(16, moveBoundary.Stop - moveBoundary.Start);
		Assert.Contains(bus.CpuBusPhases, phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.CpuPhase.Address == handlerAddress + 12 &&
			phase.CpuPhase.CompletedCycle <= moveBoundary.Start);
		Assert.Equal(
			new[]
			{
				(M68kBusAccessKind.CpuInstructionFetch, handlerAddress + 14),
				(M68kBusAccessKind.CpuInstructionFetch, handlerAddress + 16),
				(M68kBusAccessKind.CpuInstructionFetch, handlerAddress + 18),
				(M68kBusAccessKind.CpuDataRead, 0x00DFF006u)
			},
			movePhases);
		Assert.Equal(read.GrantedCycle, read.SampleCycle);
		Assert.Equal(EncodeVhposr(beam), read.Value);
		Assert.True(interruptAcceptCycle >= frameStartCycle, diagnostic);
		Assert.True(interruptEntryCycle > interruptAcceptCycle, diagnostic);
		Assert.True(moveBoundary.Start >= interruptEntryCycle, diagnostic);
	}

	[Fact]
	public void AccurateM68000Cycle01vIrq3HandlerExpectedCostMatchesCurrentTiming()
	{
		const int registerCount = 15; // D0-A6
		const int testedBits = 16;
		const int setBitsInD0 = 2; // D0=$8001 in cycle01v, so bit 0 and bit 15 are set.
		const int clearBitsInD0 = testedBits - setBitsInD0;

		var interruptEntry = 44;
		var movemSave = 8 + (8 * registerCount);
		var acknowledge = 16; // MOVE.W #$0020,INTREQ(A1)
		var loadMarker = 8; // MOVE.W #$0CCC,D5
		var clearBitPath =
			8 +  // LEA bitN+2(PC),A0
			12 + // MOVE.W #$0333,(A0)
			10 + // BTST #n,D0
			10;  // BEQ.S taken
		var setBitPath =
			8 +  // LEA bitN+2(PC),A0
			12 + // MOVE.W #$0333,(A0)
			10 + // BTST #n,D0
			8 +  // BEQ.S not taken
			8;   // MOVE.W D5,(A0)
		var bitVisualization = (clearBitsInD0 * clearBitPath) + (setBitsInD0 * setBitPath);
		var movemRestore = 12 + (8 * registerCount);
		var rte = 20;
		var expectedTotal =
			interruptEntry +
			movemSave +
			acknowledge +
			loadMarker +
			bitVisualization +
			movemRestore +
			rte;
		var expectedBoundaries = new[]
		{
			interruptEntry,
			interruptEntry,
			interruptEntry + movemSave,
			interruptEntry + movemSave + acknowledge,
			interruptEntry + movemSave + acknowledge + loadMarker,
			interruptEntry + movemSave + acknowledge + loadMarker + bitVisualization,
			interruptEntry + movemSave + acknowledge + loadMarker + bitVisualization + movemRestore,
			expectedTotal
		};

		Assert.Equal("44,44,172,188,196,848,980,1000", string.Join(",", expectedBoundaries));
		Assert.Equal(1000, expectedTotal);
	}

	[Fact]
	public void AccurateM68000Cycle01vIrq3HandlerPatchesVisualizationTableFromD0()
	{
		var clearLowBit = RunCycle01vIrq3HandlerPatchProbe(0x8000);
		var setLowBit = RunCycle01vIrq3HandlerPatchProbe(0x8001);
		var diagnostic =
			$"clear=[{string.Join(",", clearLowBit.Values.Select(value => $"0x{value:X4}"))}], " +
			$"set=[{string.Join(",", setLowBit.Values.Select(value => $"0x{value:X4}"))}]";

		Assert.Equal(0x0333, clearLowBit.Bit0Value);
		Assert.True(clearLowBit.Bit15Value == 0x0CCC, diagnostic);
		Assert.Equal(0x0CCC, setLowBit.Bit0Value);
		Assert.True(setLowBit.Bit15Value == 0x0CCC, diagnostic);
		Assert.Equal(0x0180, clearLowBit.Bit0Register);
		Assert.Equal(0x0180, clearLowBit.Bit15Register);
		Assert.Equal(clearLowBit.RtePc, clearLowBit.EndPc);
		Assert.Equal(setLowBit.RtePc, setLowBit.EndPc);
	}

	[Fact]
	public void AccurateM68000CycleD9vIrq3HandlerPatchesOnlyBit15ForD0HighBit()
	{
		var result = RunCycle01vIrq3HandlerPatchProbe(0x8000);
		var diagnostic = string.Join(",", result.Values.Select((value, bit) => $"b{bit}=0x{value:X4}"));

		Assert.Equal(0x0333, result.Values[14]);
		Assert.Equal(0x0CCC, result.Values[15]);
		Assert.Equal(15, result.Values.Count(value => value == 0x0333));
		Assert.Equal(1, result.Values.Count(value => value == 0x0CCC));
		_ = diagnostic;
	}

	[Fact]
	public void WaitLineLoopTakenBranchCadenceDocumentsVhposrSamples()
	{
		var result = RunWaitLineLoopProbe(
			CreateWaitLineLoopProgram(vposrMask: 0x0001, appendRts: false),
			startLine: 239,
			startOffset: 64,
			stopOnColorWrite: true);

		var diagnostic = BuildWaitLineLoopDiagnostic(result.Bus, result.Reads, result.ColorWrites, result.Cpu.State.Cycles);
		var expectedReads = new ushort[]
		{
			0xEF29, 0xEF3A, 0xEF4B, 0xEF5C,
			0xEF6D, 0xEF7E, 0xEF8F, 0xEFA0,
			0xEFB1, 0xEFC2, 0xEFD3, 0xEF01,
			0xF012, 0x8000
		};
		Assert.True(expectedReads.SequenceEqual(result.Reads.Select(read => read.Value)), diagnostic);
		Assert.Equal(
			new long[] { 34, 34, 34, 34, 34, 34, 34, 34, 34, 34, 34, 34, 40 },
			result.Reads.Skip(1)
				.Zip(result.Reads, (current, previous) => current.SampleCycle - previous.SampleCycle)
				.ToArray());
		var color = Assert.Single(result.ColorWrites);
		var beam = result.Bus.GetBeamPosition(color.Cycle);
		Assert.True(color.Value == 0x0F0F && beam.BeamLine == 240 && beam.BeamHorizontal == 48, diagnostic);
	}

	[Fact]
	public void WaitLineLoopTakenIterationsDoNotAccumulateCycleError()
	{
		var result = RunWaitLineLoopProbe(
			CreateWaitLineLoopProgram(vposrMask: 0x0001, appendRts: false),
			startLine: 239,
			startOffset: 64,
			stopOnColorWrite: true);
		var vhposrReads = result.Reads
			.TakeWhile(read => read.Address == 0x006)
			.ToArray();
		var deltas = vhposrReads
			.Skip(2)
			.Zip(vhposrReads.Skip(1), (current, previous) => current.SampleCycle - previous.SampleCycle)
			.ToArray();
		var pairedCycles = deltas
			.Take(deltas.Length - (deltas.Length & 1))
			.Chunk(2)
			.Select(pair => pair.Sum())
			.ToArray();
		var diagnostic = BuildWaitLineLoopDiagnostic(
			result.Bus,
			result.Reads,
			result.ColorWrites,
			result.Cpu.State.Cycles);

		Assert.True(deltas.Length >= 8, diagnostic);
		Assert.All(pairedCycles, pair => Assert.Equal(68, pair));
	}

	[Fact]
	public void WaitLineLoopExitToSync1DocumentsExactPhaseHandoff()
	{
		const int programAddress = 0x1000;
		const uint firstBnePc = programAddress + 0x0A;
		const uint vposrAndiPc = programAddress + 0x0C;
		const uint secondBnePc = programAddress + 0x12;
		const uint colorMovePc = programAddress + 0x14;
		const uint sync1AndiPc = programAddress + 0x1A;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, programAddress, CreateSynccpuBeamPollingProgram());
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(programAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (239L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + 64;
		var ledger = new List<(uint Pc, long Start, long Stop)>();
		CustomRegisterWrite? color = null;
		CustomRegisterRead? sync1Read = null;

		for (var i = 0; i < 512 && !sync1Read.HasValue; i++)
		{
			var pc = cpu.State.ProgramCounter;
			var start = cpu.State.Cycles;
			cpu.ExecuteInstruction();
			ledger.Add((pc, start, cpu.State.Cycles));
			if (!color.HasValue)
			{
				var candidate = bus.CustomRegisterWrites.FirstOrDefault(write =>
					write.Address == 0x180 && write.Value == 0x0F0F);
				color = candidate.Cycle != 0 ? candidate : null;
			}

			if (color.HasValue)
			{
				var candidate = bus.CustomRegisterReads.FirstOrDefault(read =>
					read.Address == 0x006 && read.RequestedCycle > color.Value.Cycle);
				sync1Read = candidate.RequestedCycle != 0 ? candidate : null;
			}
		}

		var finalFirstBne = ledger.Last(entry => entry.Pc == firstBnePc);
		var vposrAndi = ledger.Last(entry => entry.Pc == vposrAndiPc);
		var secondBne = ledger.Last(entry => entry.Pc == secondBnePc);
		var colorMove = ledger.Last(entry => entry.Pc == colorMovePc);
		var sync1Andi = ledger.Last(entry => entry.Pc == sync1AndiPc);
		var colorValue = color.GetValueOrDefault();
		var sync1ReadValue = sync1Read.GetValueOrDefault();
		var origin = finalFirstBne.Start;
		var diagnostic =
			$"firstBne={finalFirstBne.Start - origin}-{finalFirstBne.Stop - origin}," +
			$"vposrAndi={vposrAndi.Start - origin}-{vposrAndi.Stop - origin}," +
			$"secondBne={secondBne.Start - origin}-{secondBne.Stop - origin}," +
			$"colorMove={colorMove.Start - origin}-{colorMove.Stop - origin}," +
			$"colorWrite={colorValue.Cycle - origin}," +
			$"sync1Andi={sync1Andi.Start - origin}-{sync1Andi.Stop - origin}," +
			$"sync1Read={sync1ReadValue.RequestedCycle - origin}/{sync1ReadValue.Value:X4}";

		Assert.True(color.HasValue, diagnostic);
		Assert.True(sync1Read.HasValue, diagnostic);
		Assert.Equal(finalFirstBne.Stop, vposrAndi.Start);
		Assert.Equal(vposrAndi.Stop, secondBne.Start);
		Assert.Equal(secondBne.Stop, colorMove.Start);
		Assert.Equal(colorMove.Stop, sync1Andi.Start);
		Assert.True(colorValue.Cycle >= colorMove.Start && colorValue.Cycle <= colorMove.Stop, diagnostic);
		Assert.True(sync1ReadValue.RequestedCycle >= sync1Andi.Start && sync1ReadValue.RequestedCycle < sync1Andi.Stop, diagnostic);
		Assert.Equal(8, finalFirstBne.Stop - finalFirstBne.Start);
		Assert.Equal(20, vposrAndi.Stop - vposrAndi.Start);
		Assert.Equal(8, secondBne.Stop - secondBne.Start);
		Assert.Equal(16, colorMove.Stop - colorMove.Start);
	}

	[Fact]
	public void Probe10Sync1FinalIterationDocumentsLine32BusTimelineOutsideBitplaneWindow()
	{
		var result = RunProbe10Sync1ExitAtBeam(expectedReadHorizontal: 204, expectedColorHorizontal: 218);
		var origin = result.FinalRead.RequestedCycle;
		var phases = result.Bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= origin - 16 &&
				phase.CpuPhase.RequestedCycle <= result.ColorWrite.Cycle + 16)
			.ToArray();
		var bitplaneAccesses = result.Bus.BusAccesses
			.Where(access =>
				access.Request.Requester == AmigaBusRequester.Bitplane &&
				access.GrantedCycle >= origin - 16 &&
				access.GrantedCycle <= result.ColorWrite.Cycle + 16)
			.ToArray();
		var readBeam = result.Bus.GetBeamPosition(result.FinalRead.SampleCycle);
		var colorBeam = result.Bus.GetBeamPosition(result.ColorWrite.Cycle);
		var diagnostic =
			$"start={result.StartCycle}, read=0x{result.FinalRead.Value:X4}@v{readBeam.BeamLine}h{readBeam.BeamHorizontal}, " +
			$"color=v{colorBeam.BeamLine}h{colorBeam.BeamHorizontal}, " +
			$"phases=[{BuildCpuBusPhaseTimeline(phases, origin)}], " +
			$"bitplane=[{string.Join(';', bitplaneAccesses.Select(access => $"{access.GrantedCycle - origin}:{access.Request.Address:X6}"))}]";
		var phaseTimeline = BuildCpuBusPhaseTimeline(phases, origin);

		Assert.True(readBeam.BeamLine == 32 && readBeam.BeamHorizontal == 204, diagnostic);
		Assert.True((result.FinalRead.Value & 0x000F) == 0, diagnostic);
		Assert.True(colorBeam.BeamLine == 32 && colorBeam.BeamHorizontal == 218, diagnostic);
		Assert.Empty(bitplaneAccesses);
		Assert.Contains(phases, phase =>
			phase.CpuPhase.InstructionProgramCounter == 0x1000 &&
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
			phase.CpuPhase.Address == 0x00DFF006);
		Assert.Contains(phases, phase =>
			phase.CpuPhase.InstructionProgramCounter == 0x1004 &&
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch);
		Assert.Contains(phases, phase =>
			phase.CpuPhase.InstructionProgramCounter == 0x1006 &&
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
			phase.CpuPhase.Address == 0x00DFF180);
		Assert.Equal(
			"pc=0x1004,CpuInstructionFetch,0x001000,r+-14..+-10,g+-12 | " +
			"pc=0x1004,CpuInstructionFetch,0x001002,r+-10..+-6,g+-8 | " +
			"pc=0x1000,CpuInstructionFetch,0x001004,r+-6..+-2,g+-4 | " +
			"pc=0x1000,CpuDataRead,0xDFF006,r+-2..+2,g+0 | " +
			"pc=0x1000,CpuInstructionFetch,0x001006,r+2..+6,g+4 | " +
			"pc=0x1000,CpuDataWrite,0xDFF006,r+6..+10,g+8 | " +
			"pc=0x1004,CpuInstructionFetch,0x001008,r+14..+18,g+16 | " +
			"pc=0x1006,CpuInstructionFetch,0x00100A,r+18..+22,g+20 | " +
			"pc=0x1006,CpuInstructionFetch,0x00100C,r+22..+26,g+24 | " +
			"pc=0x1006,CpuDataWrite,0xDFF180,r+26..+30,g+28 | " +
			"pc=0x1006,CpuInstructionFetch,0x00100E,r+30..+34,g+32",
			phaseTimeline);
	}

	[Fact]
	public void WaitLineLoopFirstBneNotTakenPreservesFallthroughPrefetch()
	{
		var result = RunWaitLineLoopProbe(
			CreateFirstBneNotTakenProbeProgram(),
			startLine: 240,
			startOffset: 4,
			stopOnColorWrite: true);

		var diagnostic = BuildWaitLineLoopDiagnostic(result.Bus, result.Reads, result.ColorWrites, result.Cpu.State.Cycles);
		var read = Assert.Single(result.Reads);
		var color = Assert.Single(result.ColorWrites);
		var colorBeam = result.Bus.GetBeamPosition(color.Cycle);
		Assert.Equal(0xF0, read.Value >> 8);
		Assert.True(color.Value == 0x1111 && colorBeam.BeamLine == 240 && colorBeam.BeamHorizontal == 27, diagnostic);
		Assert.Contains(
			result.Bus.CpuBusPhases,
			phase => phase.CpuPhase.InstructionProgramCounter == 0x100A &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
				phase.CpuPhase.Address == 0x00100E);
	}

	[Fact]
	public void WaitLineLoopSecondBneTakenFlushesToLoopStart()
	{
		var result = RunWaitLineLoopProbe(
			CreateWaitLineLoopProgram(vposrMask: 0x8000, appendRts: false),
			startLine: 240,
			startOffset: 4,
			stopOnColorWrite: false,
			maxInstructions: 24);

		var diagnostic = BuildWaitLineLoopDiagnostic(result.Bus, result.Reads, result.ColorWrites, result.Cpu.State.Cycles);
		Assert.Empty(result.ColorWrites);
		Assert.True(
			new ushort[] { 0xF00B, 0x8000, 0xF02A, 0x8000, 0xF049, 0x8000, 0xF068, 0x8000 }
				.SequenceEqual(result.Reads.Select(read => read.Value)),
			diagnostic);
		Assert.True(
			result.Bus.CpuBusPhases.Count(phase =>
				phase.CpuPhase.InstructionProgramCounter == 0x1012 &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
				phase.CpuPhase.Address == 0x001000) >= 4,
			diagnostic);
	}

	[Fact]
	public void WaitLineLoopSecondBneNotTakenHandsExpectedPhaseToColorWrite()
	{
		var result = RunWaitLineLoopProbe(
			CreateWaitLineLoopProgram(vposrMask: 0x0001, appendRts: true),
			startLine: 240,
			startOffset: 4,
			stopOnColorWrite: true);

		var diagnostic = BuildWaitLineLoopDiagnostic(result.Bus, result.Reads, result.ColorWrites, result.Cpu.State.Cycles);
		Assert.True(
			new ushort[] { 0xF00B, 0x8000 }.SequenceEqual(result.Reads.Select(read => read.Value)),
			diagnostic);
		var color = Assert.Single(result.ColorWrites);
		var beam = result.Bus.GetBeamPosition(color.Cycle);
		Assert.True(color.Value == 0x0F0F && beam.BeamLine == 240 && beam.BeamHorizontal == 41, diagnostic);
		Assert.Contains(
			result.Bus.CpuBusPhases,
			phase => phase.CpuPhase.InstructionProgramCounter == 0x1014 &&
				phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
				phase.CpuPhase.Address == 0x00DFF180);
	}

	[Fact]
	public void AccurateM68000SynccpuPrefixSamplesBeamRegistersAtGrantCycles()
	{
		const int programAddress = 0x1000;
		var program = CreateSynccpuBeamPollingProgram();
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, programAddress, program);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(programAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (240L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + 64;
		var stopCycle = cpu.State.Cycles + AmigaConstants.A500PalCpuCyclesPerRasterLine;

		while (bus.CustomRegisterWrites.All(write => write.Address != 0x180 || write.Value != 0x0F0F) &&
			cpu.State.Cycles < stopCycle)
		{
			cpu.ExecuteInstruction();
		}

		var colorWrite = Assert.Single(bus.CustomRegisterWrites.Where(write =>
			write.Address == 0x180 &&
			write.Value == 0x0F0F));
		var beamReads = bus.CustomRegisterReads
			.Where(read => read.Address is 0x004 or 0x006)
			.ToArray();
		var diagnostic = BuildBeamReadDiagnostic(bus, beamReads, colorWrite.Cycle);

		Assert.True(beamReads.Length >= 2, diagnostic);
		Assert.All(beamReads, read =>
		{
			var beam = bus.GetBeamPosition(read.GrantedCycle);
			var expected = read.Address == 0x004 ? EncodeVposr(beam) : EncodeVhposr(beam);
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(expected, read.Value);
		});
		Assert.Equal(0x006, beamReads[0].Address);
		Assert.Equal(0xF0, beamReads[0].Value >> 8);
		Assert.Equal(0x004, beamReads[1].Address);
		Assert.Equal(0, beamReads[1].Value & 0x0001);
		Assert.True(colorWrite.Cycle > beamReads[1].CompletedCycle, diagnostic);
	}

	[Fact]
	public void AccurateM68000NoExtensionVhposrReadMacroSamplesCausallyAtSteadyCadence()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var result = RunVAmigaTsProbe10VhposrReadMacro(programAddress, valuesAddress, 0x38C1);
		var actual = result.Reads.Select(read => read.Value).ToArray();
		var stored = ReadChipWords(result.Bus.ChipRam, valuesAddress, actual.Length);
		var diagnostic = BuildVhposrProbeDiagnostic(result.StartCycle, result.Reads, actual, actual);

		Assert.Equal(16, actual.Length);
		Assert.Equal(actual, stored);
		Assert.All(result.Reads, read =>
		{
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(EncodeVhposr(result.Bus.GetBeamPosition(read.SampleCycle)), read.Value);
			Assert.True(read.RequestedCycle <= read.GrantedCycle, diagnostic);
			Assert.True(read.GrantedCycle < read.CompletedCycle, diagnostic);
		});
		for (var i = 1; i < result.Reads.Length; i++)
		{
			var deltaCycles = result.Reads[i].SampleCycle - result.Reads[i - 1].SampleCycle;
			Assert.True(deltaCycles is 16 or 18, diagnostic);
		}
	}

	[Fact]
	public void AccurateM68000NoExtensionVhposrReadMacroCrossesRasterBoundaryMonotonically()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var result = RunVAmigaTsProbe10VhposrReadMacro(programAddress, valuesAddress, 0x38C1);
		var actual = result.Reads.Select(read => read.Value).ToArray();
		var beams = result.Reads.Select(read => result.Bus.GetBeamPosition(read.SampleCycle)).ToArray();
		var diagnostic = BuildVhposrProbeDiagnostic(result.StartCycle, result.Reads, actual, actual);

		Assert.Equal(16, actual.Length);
		Assert.All(result.Reads, read =>
		{
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(EncodeVhposr(result.Bus.GetBeamPosition(read.SampleCycle)), read.Value);
		});
		Assert.Single(beams.Zip(beams.Skip(1)).Where(pair => pair.First.BeamLine != pair.Second.BeamLine));
		for (var i = 1; i < result.Reads.Length; i++)
		{
			Assert.True(result.Reads[i].SampleCycle > result.Reads[i - 1].CompletedCycle, diagnostic);
		}
	}

	[Fact]
	public void AccurateM68000VAmigaTsProbe10VhposrReadMacroDocumentsCurrentBusTimeline()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var result = RunVAmigaTsProbe10VhposrReadMacro(programAddress, valuesAddress, 0x38C1);
		var phases = result.Bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.RequestedCycle >= result.StartCycle &&
				phase.CpuPhase.RequestedCycle <= result.Reads[1].CompletedCycle + 12)
			.ToArray();
		var actual = BuildCpuBusPhaseTimeline(phases, result.StartCycle);
		var expected =
			"pc=0x1000,CpuInstructionFetch,0x001000,r+0..+5,g+3 | " +
			"pc=0x1000,CpuInstructionFetch,0x001002,r+5..+9,g+7 | " +
			"pc=0x1000,CpuDataRead,0xDFF006,r+9..+13,g+11 | " +
			"pc=0x1000,CpuInstructionFetch,0x001004,r+13..+17,g+15 | " +
			"pc=0x1002,CpuDataWrite,0x002000,r+17..+21,g+19 | " +
			"pc=0x1002,CpuInstructionFetch,0x001006,r+21..+25,g+23 | " +
			"pc=0x1004,CpuDataRead,0xDFF006,r+25..+29,g+27 | " +
			"pc=0x1004,CpuInstructionFetch,0x001008,r+29..+33,g+31 | " +
			"pc=0x1006,CpuDataWrite,0x002002,r+33..+37,g+35 | " +
			"pc=0x1006,CpuInstructionFetch,0x00100A,r+37..+41,g+39 | " +
			"pc=0x1008,CpuDataRead,0xDFF006,r+41..+45,g+43";

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void AccurateM68000NoExtensionVposrReadMacroSamplesVerticalTransitionCausally()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var line256Start = 256L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var result = RunVAmigaTsVprobeVposrReadMacro(programAddress, valuesAddress, line256Start - 111);
		var actual = result.Reads.Select(read => read.Value).ToArray();
		var stored = ReadChipWords(result.Bus.ChipRam, valuesAddress, 16);
		var diagnostic = BuildVhposrProbeDiagnostic(result.StartCycle, result.Bus, result.Reads, actual, actual);

		Assert.Equal(16, actual.Length);
		Assert.Equal(actual, stored);
		Assert.All(result.Reads, read =>
		{
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(EncodeVposr(result.Bus.GetBeamPosition(read.SampleCycle)), read.Value);
		});
		var transition = Assert.Single(result.Reads.Zip(result.Reads.Skip(1))
			.Where(pair => pair.First.Value != pair.Second.Value));
		Assert.True(transition.First.SampleCycle < line256Start, diagnostic);
		Assert.True(transition.Second.SampleCycle >= line256Start, diagnostic);
	}

	[Fact]
	public void AccurateM68000VAmigaTsVprobe2CpuVisibleIrqEntryMatchesSourceCopperPhase()
	{
		using var machine = new Machine(MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithLiveAgnusDma(true)
			.WithBusAccessLogging(true));
		var bus = machine.Bus;
		const uint handlerAddress = 0x2000;
		const int valuesAddress = 0x3000;
		const int stackPointer = 0x3F00;
		var line256Start = 256L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var cpuVisibleCycle = line256Start - 174;
		Write(bus.ChipRam, 0x1000, 0x60, 0xFE, 0x4E, 0x71);
		Write(bus.ChipRam, (int)handlerAddress, CreateVAmigaTsVprobe2Irq1Handler(valuesAddress));
		bus.WriteLong((24u + 1u) * 4u, handlerAddress);
		machine.Cpu.Reset(0x1000, stackPointer);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		machine.Cpu.State.Cycles = cpuVisibleCycle;

		machine.Cpu.RequestInterrupt(1, (24u + 1u) * 4u);
		for (var i = 0; i < 48; i++)
		{
			machine.Cpu.ExecuteInstruction();
			if (ReadChipWords(bus.ChipRam, valuesAddress, 16).All(value => value != 0))
			{
				break;
			}
		}

		var reads = bus.CustomRegisterReads
			.Where(read => read.Address == 0x004 && read.Kind == AmigaBusAccessKind.CpuDataRead)
			.Take(16)
			.ToArray();
		var stored = ReadChipWords(bus.ChipRam, valuesAddress, 16);
		var entryPhases = bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.RequestedCycle >= cpuVisibleCycle &&
				(reads.Length == 0 || phase.CpuPhase.RequestedCycle <= reads[0].CompletedCycle + 48))
			.ToArray();
		var diagnostic =
			$"visible={cpuVisibleCycle}/line256StartDelta={cpuVisibleCycle - line256Start}, " +
			$"cpuAfterInterrupt={machine.Cpu.State.Cycles}, " +
			$"reads=[{string.Join(",", reads.Select(read => $"0x{read.Value:X4}@{read.SampleCycle - line256Start}/v{bus.GetBeamPosition(read.SampleCycle).BeamLine:X3}h{bus.GetBeamPosition(read.SampleCycle).BeamHorizontal:X2}"))}], " +
			$"stored=[{string.Join(",", stored.Select(value => $"0x{value:X4}"))}], " +
			$"phases=[{BuildCpuBusPhaseTimeline(entryPhases, cpuVisibleCycle)}]";
		var current = new ushort[]
		{
			0x8000, 0x8000, 0x8000, 0x8000,
			0x8000, 0x8000, 0x8001, 0x8001,
			0x8001, 0x8001, 0x8001, 0x8001,
			0x8001, 0x8001, 0x8001, 0x8001
		};

		Assert.Equal(16, reads.Length);
		Assert.True(current.SequenceEqual(reads.Select(read => read.Value)), diagnostic);
		Assert.Equal(current, stored);
		Assert.Equal(line256Start - 112, reads[0].SampleCycle);
		Assert.Equal(line256Start + 10, reads[6].SampleCycle);
		Assert.Equal(20, reads[1].SampleCycle - reads[0].SampleCycle);
		Assert.Equal(22, reads[6].SampleCycle - reads[5].SampleCycle);
		Assert.True(entryPhases.Length >= 7, diagnostic);
		Assert.Equal(M68kBusAccessKind.CpuDataWrite, entryPhases[0].CpuPhase.AccessKind);
		Assert.Equal((uint)(stackPointer - 2), entryPhases[0].CpuPhase.Address);
		Assert.Equal(M68kBusAccessKind.CpuInterruptAcknowledge, entryPhases[1].CpuPhase.AccessKind);
		Assert.Equal(0xFFFFF3u, entryPhases[1].CpuPhase.Address);
		Assert.Equal(M68kBusAccessKind.CpuDataWrite, entryPhases[2].CpuPhase.AccessKind);
		Assert.Equal((uint)(stackPointer - 6), entryPhases[2].CpuPhase.Address);
		Assert.Equal(M68kBusAccessKind.CpuDataWrite, entryPhases[3].CpuPhase.AccessKind);
		Assert.Equal((uint)(stackPointer - 4), entryPhases[3].CpuPhase.Address);
		Assert.Equal(M68kBusAccessKind.CpuDataRead, entryPhases[4].CpuPhase.AccessKind);
		Assert.Equal(0x000064u, entryPhases[4].CpuPhase.Address);
		Assert.Equal(M68kBusAccessKind.CpuInstructionFetch, entryPhases[5].CpuPhase.AccessKind);
		Assert.Equal(handlerAddress, entryPhases[5].CpuPhase.Address);
		Assert.Equal(M68kBusAccessKind.CpuInstructionFetch, entryPhases[6].CpuPhase.AccessKind);
		Assert.Equal(handlerAddress + 2, entryPhases[6].CpuPhase.Address);
	}

	[Fact]
	public void AccurateM68000NoExtensionMoveReadStorePairKeepsHardwareObservedReadCadence()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var result = RunVAmigaTsProbe10VhposrReadMacro(programAddress, valuesAddress, 0x38C1);
		var firstRead = result.Reads[0];
		var secondRead = result.Reads[1];
		var phases = result.Bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.RequestedCycle >= firstRead.RequestedCycle &&
				phase.CpuPhase.RequestedCycle <= secondRead.CompletedCycle)
			.ToArray();
		var diagnostic =
			$"read0=0x{firstRead.Value:X4}@+{firstRead.SampleCycle - result.StartCycle}, " +
			$"read1=0x{secondRead.Value:X4}@+{secondRead.SampleCycle - result.StartCycle}, " +
			$"delta={secondRead.SampleCycle - firstRead.SampleCycle}, " +
			$"expectedDelta=16, phases=[{BuildCpuBusPhaseTimeline(phases, result.StartCycle)}], " +
			$"classification={ClassifyMoveReadStorePairEarlyPhase(firstRead, secondRead, phases, expectedDelta: 16)}";

		Assert.True(secondRead.SampleCycle - firstRead.SampleCycle == 16, diagnostic);
		Assert.Contains(
			phases,
			phase => phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
				phase.CpuPhase.RequestedCycle >= firstRead.CompletedCycle &&
				phase.CpuPhase.CompletedCycle <= secondRead.RequestedCycle);
	}

	[Fact]
	public void AccurateM68000VAmigaTsProbe10SourceReadStoreMacroSamplesAndStoresCausally()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var result = RunVAmigaTsProbe10SourceVhposrReadMacro(programAddress, valuesAddress, 0x38C2);
		var actual = result.Reads.Select(read => read.Value).ToArray();
		var stored = ReadChipWords(result.Bus.ChipRam, valuesAddress, actual.Length);
		var diagnostic = BuildVhposrProbeDiagnostic(result.StartCycle, result.Bus, result.Reads, actual, actual);

		Assert.Equal(16, actual.Length);
		Assert.Equal(actual, stored);
		Assert.All(result.Reads, read =>
		{
			Assert.Equal(read.GrantedCycle, read.SampleCycle);
			Assert.Equal(EncodeVhposr(result.Bus.GetBeamPosition(read.SampleCycle)), read.Value);
		});
		Assert.All(result.Reads.Zip(result.Reads.Skip(1)), pair =>
			Assert.Equal(20, pair.Second.SampleCycle - pair.First.SampleCycle));
	}

	[Fact]
	public void AccurateM68000VAmigaTsProbe9Through13MapEndOfLineCausally()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var cases = new[]
		{
			(Name: "probe9", IsLongFrame: true, First: (ushort)0x38BC),
			(Name: "probe10", IsLongFrame: true, First: (ushort)0x38C2),
			(Name: "probe11", IsLongFrame: false, First: (ushort)0x37BC),
			(Name: "probe12", IsLongFrame: false, First: (ushort)0x37BC),
			(Name: "probe13", IsLongFrame: false, First: (ushort)0x37C2)
		};

		foreach (var testCase in cases)
		{
			var result = RunVAmigaTsProbe10SourceVhposrReadMacro(
				programAddress,
				valuesAddress,
				testCase.First,
				testCase.IsLongFrame);
			var actual = result.Reads.Select(read => read.Value).ToArray();
			var beams = result.Reads.Select(read => result.Bus.GetBeamPosition(read.SampleCycle)).ToArray();
			var diagnostic = $"{testCase.Name}: " +
				BuildVhposrProbeDiagnostic(result.StartCycle, result.Bus, result.Reads, actual, actual);

			Assert.Equal(16, actual.Length);
			Assert.All(result.Reads, read =>
			{
				var beam = result.Bus.GetBeamPosition(read.SampleCycle);
				Assert.Equal(read.GrantedCycle, read.SampleCycle);
				Assert.Equal(EncodeVhposr(beam), read.Value);
			});
			Assert.All(result.Reads.Zip(result.Reads.Skip(1)), pair =>
				Assert.Equal(20, pair.Second.SampleCycle - pair.First.SampleCycle));
			var wrap = Assert.Single(beams.Zip(beams.Skip(1))
				.Where(pair => pair.First.BeamLine != pair.Second.BeamLine));
			Assert.Equal(testCase.IsLongFrame ? 0x138 : 0x137, wrap.First.BeamLine);
			Assert.Equal(0, wrap.Second.BeamLine);
		}
	}

	[Fact]
	public void AccurateM68000VhposrReadStorePairDocumentsAmigaBusTiming()
	{
		const int programAddress = 0x1000;
		const int valuesAddress = 0x2000;
		var result = RunVhposrReadStorePair(programAddress, valuesAddress, firstExpectedValue: 0x38C1);
		var read = Assert.Single(result.Reads);
		var phases = result.Bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.RequestedCycle >= result.StartCycle &&
				phase.CpuPhase.RequestedCycle <= read.CompletedCycle + 16)
			.ToArray();
		var actual = BuildCpuBusPhaseTimeline(phases, result.StartCycle);
		var expected =
			"pc=0x1000,CpuInstructionFetch,0x001000,r+0..+5,g+3 | " +
			"pc=0x1000,CpuInstructionFetch,0x001002,r+5..+9,g+7 | " +
			"pc=0x1000,CpuDataRead,0xDFF006,r+9..+13,g+11 | " +
			"pc=0x1000,CpuInstructionFetch,0x001004,r+13..+17,g+15 | " +
			"pc=0x1002,CpuDataWrite,0x002000,r+17..+21,g+19 | " +
			"pc=0x1002,CpuInstructionFetch,0x001006,r+21..+25,g+23";
		var stored = (ushort)((result.Bus.ChipRam[valuesAddress] << 8) | result.Bus.ChipRam[valuesAddress + 1]);

		Assert.Equal(0x006, read.Address);
		Assert.Equal(0x38C1, read.Value);
		Assert.Equal(read.GrantedCycle, read.SampleCycle);
		Assert.Equal(0x38C1, stored);
		Assert.Equal(0x38C1, EncodeVhposr(result.Bus.GetBeamPosition(read.SampleCycle)));
		Assert.Equal(expected, actual);
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
		bus.RegisterHostGateway(0x00001004, _ => { });

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
		Assert.Equal(0x2020, bus.CustomRegisterWrites[2].Value);
		var cpuCustomWrites = bus.BusAccesses
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu && access.Request.Target == AmigaBusAccessTarget.CustomRegisters)
			.ToArray();
		Assert.All(
			bus.CustomRegisterWrites.Zip(cpuCustomWrites),
			pair => Assert.Equal(pair.Second.GrantedCycle, pair.First.Cycle));
	}

	[Fact]
	public void EvenCustomByteWriteMirrorsValueOntoBothWordBytes()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00DFF0A8, 0x20, 10);

		var write = Assert.Single(bus.CustomRegisterWrites);
		var access = Assert.Single(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);
		Assert.Equal(0x0A8, write.Address);
		Assert.Equal(0x2020, write.Value);
		Assert.Equal(access.GrantedCycle, write.Cycle);
	}

	[Fact]
	public void OddCustomByteWriteMirrorsValueOntoBothWordBytes()
	{
		var bus = new AmigaBus();

		bus.WriteByte(0x00DFF0A9, 0x20, 10);

		var write = Assert.Single(bus.CustomRegisterWrites);
		var access = Assert.Single(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);
		Assert.Equal(0x0A8, write.Address);
		Assert.Equal(0x2020, write.Value);
		Assert.Equal(access.GrantedCycle, write.Cycle);
	}

	[Fact]
	public void OddCustomSpaceWordWriteFallsBackToByteSplit()
	{
		var bus = new AmigaBus();

		bus.WriteWord(0x00DFF181, 0x1234, 10);
		var access = Assert.Single(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);

		Assert.Equal(2, bus.CustomRegisterWrites.Count);
		Assert.Equal(0x180, bus.CustomRegisterWrites[0].Address);
		Assert.Equal(0x1212, bus.CustomRegisterWrites[0].Value);
		Assert.Equal(access.GrantedCycle, bus.CustomRegisterWrites[0].Cycle);
		Assert.Equal(0x182, bus.CustomRegisterWrites[1].Address);
		Assert.Equal(0x3434, bus.CustomRegisterWrites[1].Value);
		Assert.Equal(access.GrantedCycle, bus.CustomRegisterWrites[1].Cycle);
	}

	[Fact]
	public void LastAlignedCustomWordAddressRoutesAsSingleCustomRegisterWrite()
	{
		var bus = new AmigaBus();

		bus.WriteWord(0x00DFF1FE, 0x1234, 10);

		var write = Assert.Single(bus.CustomRegisterWrites);
		var access = Assert.Single(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);
		Assert.Equal(0x1FE, write.Address);
		Assert.Equal(0x1234, write.Value);
		Assert.Equal(access.GrantedCycle, write.Cycle);
	}

	[Fact]
	public void UnalignedCustomSpaceEndWordWriteFallsBackToByteSplit()
	{
		var bus = new AmigaBus();

		bus.WriteWord(0x00DFF1FF, 0x1234, 10);

		var write = Assert.Single(bus.CustomRegisterWrites);
		var customByte = Assert.Single(bus.BusAccesses, access =>
			access.Request.Requester == AmigaBusRequester.Cpu &&
			access.Request.Target == AmigaBusAccessTarget.CustomRegisters);
		Assert.Equal(0x1FE, write.Address);
		Assert.Equal(0x1212, write.Value);
		Assert.Equal(customByte.GrantedCycle, write.Cycle);
	}

	[Fact]
	public void AmigaBusClassifiesCpuAccessTargetsForArbitration()
	{
		var bus = new AmigaBus();
		bus.MapReadOnlyMemory(0x00FC0000, new byte[] { 0x12, 0x34 });
		bus.RegisterHostGateway(0x00F00000, _ => { });

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
		Assert.Equal(AgnusChipSlotOwner.Free, AgnusHrmOcsSlotTable.GetFixedOwner(0x03));
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
		var before = bus.Agnus.CaptureSnapshot();

		bus.AdvanceDmaTo(AgnusHrmOcsSlotTable.LastRefreshHorizontal * AgnusChipSlotScheduler.SlotCycles);

		var snapshot = bus.Agnus.CaptureSnapshot();
		Assert.Equal(
			AgnusHrmOcsSlotTable.RefreshSlotsPerLine,
			snapshot.RefreshSlotReservationCount - before.RefreshSlotReservationCount);
		Assert.Equal(
			AgnusHrmOcsSlotTable.RefreshSlotsPerLine,
			snapshot.RefreshSlotGrantCount - before.RefreshSlotGrantCount);
		Assert.Equal(0, snapshot.DiskSlotReservationCount);
		Assert.Equal(0, snapshot.PaulaSlotReservationCount);
		Assert.Equal(0, snapshot.SpriteSlotReservationCount);
	}

	[Theory]
	[InlineData(0x41)]
	[InlineData(0x42)]
	public void HrmSlotEngineGrantsCpuOnEitherFreeMemorySlotPhase(int horizontal)
	{
		var bus = new AmigaBus();
		var requestedCycle = (long)horizontal * AgnusChipSlotScheduler.SlotCycles;
		var cycle = requestedCycle;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var access = bus.BusAccesses.Last(access => access.Request.Requester == AmigaBusRequester.Cpu);
		Assert.Equal(requestedCycle, access.GrantedCycle);
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

		Assert.True(bus.TryReadRowSpriteDmaWord(0x1000, spriteCycle, out var value, out var grantedCycle));

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

		Assert.False(bus.TryReadRowSpriteDmaWord(0x1000, wrongCycle, out var value, out var grantedCycle));

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
		var refreshCycle = AgnusHrmOcsSlotTable.FirstRefreshHorizontal * AgnusChipSlotScheduler.SlotCycles;
		Assert.False(refreshBus.TryReadLiveBitplaneDmaWord(0x1000, refreshCycle, out var value, out var grantedCycle));
		Assert.Equal(0, value);
		Assert.Equal(refreshCycle, grantedCycle);
		Assert.Equal(AgnusChipSlotOwner.Bitplane, refreshBus.Agnus.CaptureSnapshot().LastDeniedFixedSlot?.Owner);
		Assert.Equal(AgnusChipSlotOwner.Refresh, refreshBus.Agnus.CaptureSnapshot().LastDeniedFixedSlotBlocker?.Owner);
		Assert.Equal(1, refreshBus.Agnus.CaptureSnapshot().RefreshDeniedFixedSlotBlockerCount);
	}

	[Fact]
	public void LiveCopperDmaSamplesChipRamAtGrantedSlot()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var beforeWrite = bus.ReadLiveCopperDmaWord(0x2400, 20, out var firstAccess);
		var cpuCycle = 32L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

		var afterWrite = bus.ReadLiveCopperDmaWord(0x2400, 56, out var secondAccess);

		Assert.Equal(20, firstAccess.GrantedCycle);
		Assert.Equal(56, secondAccess.GrantedCycle);
		Assert.Equal(0x1234, beforeWrite);
		Assert.Equal(0x5678, afterWrite);
	}

	[Fact]
	public void PaulaDmaReadSamplesChipRamAtGrantedSlot()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		var beforeWrite = bus.ReadPaulaDmaWord(0x2400, 20);
		var cpuCycle = 34L;
		bus.WriteWord(0x00002400, 0x5678, ref cpuCycle, AmigaBusAccessKind.CpuDataWrite);

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
	public void ChipRamWritesExposeOnlyCommittedCausalState()
	{
		var bus = new AmigaBus();
		BigEndian.WriteUInt16(bus.ChipRam, 0x2400, 0x1234);
		Assert.Equal(0x1234, bus.ReadCurrentChipDmaWord(0x2400));

		var firstCycle = 20L;
		bus.WriteWord(0x00002400, 0x9ABC, ref firstCycle, AmigaBusAccessKind.CpuDataWrite);
		Assert.Equal(0x9ABC, bus.ReadCurrentChipDmaWord(0x2400));

		var secondCycle = firstCycle;
		bus.WriteWord(0x00002400, 0x5678, ref secondCycle, AmigaBusAccessKind.CpuDataWrite);
		Assert.True(secondCycle >= firstCycle);
		Assert.Equal(0x5678, bus.ReadCurrentChipDmaWord(0x2400));
	}

	[Fact]
	public void ChipRamBackendDoesNotContainPresentationHistoryStorage()
	{
		var fields = typeof(AmigaChipRamBackend).GetFields(
			System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.NonPublic |
			System.Reflection.BindingFlags.Public);

		Assert.DoesNotContain(
			fields,
			field => field.Name.Contains("presentation", StringComparison.OrdinalIgnoreCase) ||
				field.FieldType.Name.Contains("PresentationWriteHistory", StringComparison.Ordinal));
		Assert.Null(typeof(AmigaBus).Assembly.GetType("CopperMod.Amiga.ChipPresentationWriteHistory"));
		Assert.Null(typeof(AmigaBus).Assembly.GetType("CopperMod.Amiga.CustomChips.Agnus.AmigaDmaWordReservation"));
		Assert.DoesNotContain(
			typeof(AmigaBus).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
			method => method.Name.StartsWith("CommitDmaWord", StringComparison.Ordinal));
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
		var horizon = bus.GetNextCpuVisibilityHorizon(currentCycle, frameCycle + 100);

		Assert.Equal(frameCycle, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.VerticalBlank, wakeSource);
		Assert.Equal(frameCycle, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.VerticalBlank, horizon.Reason);
	}

	[Fact]
	public void CpuVisibilityHorizonReturnsTargetWithoutMutatingExecution()
	{
		var bus = new AmigaBus();
		var executedBefore = bus.ExecutedChipBusHorizon;
		var paulaBefore = bus.Paula.GetChannelSnapshot(0);
		var diskBefore = bus.Disk.CaptureSnapshot();

		var horizon = bus.GetNextCpuVisibilityHorizon(0, 100, cpuInterruptMask: 0);
		var updatesAfterWarmup = bus.CausalBusExecutor.CpuVisibilityLeafUpdates;
		var readsAfterWarmup = bus.CausalBusExecutor.CpuVisibilityRootReads;
		var repeated = bus.GetNextCpuVisibilityHorizon(1, 100, cpuInterruptMask: 0);

		Assert.Equal(100, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.TargetCycle, horizon.Reason);
		Assert.Equal(AmigaDiskController.SchedulerWakeReason.None, horizon.DiskReason);
		Assert.True(horizon.ReachesTarget);
		Assert.Equal(horizon, repeated);
		Assert.Equal(updatesAfterWarmup, bus.CausalBusExecutor.CpuVisibilityLeafUpdates);
		Assert.Equal(readsAfterWarmup + 1, bus.CausalBusExecutor.CpuVisibilityRootReads);
		Assert.Equal(executedBefore, bus.ExecutedChipBusHorizon);
		Assert.Equal(paulaBefore, bus.Paula.GetChannelSnapshot(0));
		var diskAfter = bus.Disk.CaptureSnapshot();
		Assert.Equal(diskBefore.DiskPointer, diskAfter.DiskPointer);
		Assert.Equal(diskBefore.Dsklen, diskAfter.Dsklen);
		Assert.Equal(diskBefore.Dsksync, diskAfter.Dsksync);
		Assert.Equal(diskBefore.Dskbytr, diskAfter.Dskbytr);
		Assert.Equal(diskBefore.ActiveDma, diskAfter.ActiveDma);
		Assert.Equal(diskBefore.ActiveDmaCompletionCycle, diskAfter.ActiveDmaCompletionCycle);
	}

	[Fact]
	public void CpuVisibilityHorizonHonorsExternalBoundaryAndTargetTie()
	{
		var bus = new AmigaBus();

		var earlier = bus.GetNextCpuVisibilityHorizon(
			0, 100, cpuInterruptMask: 0, externalBoundaryCycle: 40);
		var tied = bus.GetNextCpuVisibilityHorizon(
			0, 100, cpuInterruptMask: 0, externalBoundaryCycle: 100);

		Assert.Equal(40, earlier.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.ExternalBoundary, earlier.Reason);
		Assert.Equal(100, tied.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.TargetCycle, tied.Reason);
	}

	[Fact]
	public void CpuVisibilityHorizonWarmRootQueriesAllocateNothing()
	{
		var bus = new AmigaBus();
		for (var i = 0; i < 100; i++)
		{
			_ = bus.GetNextCpuVisibilityHorizon(0, 1000, cpuInterruptMask: 0);
		}

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1000; i++)
		{
			_ = bus.GetNextCpuVisibilityHorizon(0, 1000, cpuInterruptMask: 0);
		}
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void CpuVisibilityAgendaUpdatesOnlyPaulaLeavesForPaulaWrite()
	{
		var bus = new AmigaBus();
		_ = bus.GetNextCpuVisibilityHorizon(0, 100, cpuInterruptMask: 0);
		var updatesBefore = bus.CausalBusExecutor.CpuVisibilityLeafUpdates;

		bus.Paula.ScheduleWrite(20, 0x0A6, 0x0003);
		var horizon = bus.GetNextCpuVisibilityHorizon(0, 100, cpuInterruptMask: 0);

		Assert.Equal(20, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.Paula, horizon.Reason);
		Assert.Equal(8, bus.CausalBusExecutor.CpuVisibilityLeafUpdates - updatesBefore);
	}

	[Fact]
	public void CpuVisibilityHorizonIncludesCiaTimerDeadline()
	{
		var bus = new AmigaBus();
		var events = new List<AmigaCiaInterruptEvent>();
		bus.CiaA.WriteRegister(0x04, 0x02, 0, events);
		bus.CiaA.WriteRegister(0x05, 0x00, 0, events);
		bus.CiaA.WriteRegister(0x0E, 0x11, 0, events);

		var horizon = bus.GetNextCpuVisibilityHorizon(0, 100);

		Assert.Equal(20, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.CiaTimer, horizon.Reason);
	}

	[Fact]
	public void CpuVisibilityHorizonIncludesPendingAgnusControlEvent()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true);
		bus.CausalBusExecutor.ScheduleCopperMoveControl(0x180, 0x0123, cycle: 24);
		var executedBefore = bus.CausalBusExecutor.ExecutedThroughCycle;

		var horizon = bus.GetNextCpuVisibilityHorizon(0, 100);

		Assert.Equal(24, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.ControlEvent, horizon.Reason);
		Assert.Equal(executedBefore, bus.CausalBusExecutor.ExecutedThroughCycle);
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

		var releaseCycle = AmigaConstants.A500SoftwareInterruptRegisterToIplDelayCpuCycles;
		var delayedCandidate = bus.GetNextCpuBatchWakeCandidateCycle(0, 200, out var delayedWakeSource);
		var visibleCandidate = bus.GetNextCpuBatchWakeCandidateCycle(10, 200, out var visibleWakeSource);
		var pendingCandidate = bus.GetNextCpuBatchWakeCandidateCycle(releaseCycle, releaseCycle + 100, out var pendingWakeSource);
		var maskedCandidate = bus.GetNextCpuBatchWakeCandidateCycle(releaseCycle, releaseCycle + 100, 3, out var maskedWakeSource);
		var unmaskedCandidate = bus.GetNextCpuBatchWakeCandidateCycle(releaseCycle, releaseCycle + 100, 2, out var unmaskedWakeSource);
		var maskedHorizon = bus.GetNextCpuVisibilityHorizon(releaseCycle, releaseCycle + 100, 3);
		var unmaskedHorizon = bus.GetNextCpuVisibilityHorizon(releaseCycle, releaseCycle + 100, 2);

		Assert.Equal(releaseCycle, delayedCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, delayedWakeSource);
		Assert.Equal(11, visibleCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, visibleWakeSource);
		Assert.Equal(releaseCycle + 1, pendingCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, pendingWakeSource);
		Assert.Equal(releaseCycle + 100, maskedCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, maskedWakeSource);
		Assert.Equal(releaseCycle + 1, unmaskedCandidate);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, unmaskedWakeSource);
		Assert.Equal(releaseCycle + 100, maskedHorizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.TargetCycle, maskedHorizon.Reason);
		Assert.Equal(releaseCycle + 1, unmaskedHorizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.PendingInterrupt, unmaskedHorizon.Reason);
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
		var horizon = bus.GetNextCpuVisibilityHorizon(0, 1000);

		Assert.Equal(AmigaConstants.A500PalCpuCyclesPerRasterLine, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.HorizontalSyncTod, wakeSource);
		Assert.Equal(candidate, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.HorizontalSyncTod, horizon.Reason);
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
		var horizon = bus.GetNextCpuVisibilityHorizon(0, 100);

		Assert.Equal(20, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.CiaTimer, wakeSource);
		Assert.Equal(20, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.CiaTimer, horizon.Reason);
	}

	[Fact]
	public void StoppedCpuWakeCandidateIgnoresPendingDiskReadDmaWhenDskblkDisabled()
	{
		var bus = new AmigaBus();
		const long readyCycle = 20;
		bus.Disk.Drive0.Insert(CreateSingleWordDisk(0x1234));
		bus.Disk.Drive0.SetSelected(true);
		bus.Disk.Drive0.SetMotorOn(true, readyCycle - MotorReadyDelayCycles());
		var writeCycle = 0L;
		bus.WriteWord(0x00DFF096, 0x8210, ref writeCycle, AmigaBusAccessKind.CpuDataWrite);
		bus.Paula.AdvanceTo(writeCycle);
		bus.Disk.WriteRegister(0x024, 0x8001, writeCycle);
		bus.Disk.WriteRegister(0x024, 0x8001, writeCycle);

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
		var horizon = bus.GetNextCpuVisibilityHorizon(0, 100);

		Assert.Equal(20, candidate);
		Assert.Equal(M68kTraceBatchWakeSource.Paula, wakeSource);
		Assert.Equal(candidate, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.Paula, horizon.Reason);
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
		static bool AffectsBusSchedule(ushort offset) =>
			CustomRegisterScheduleClassifier.AffectsEventSchedule(
				CustomRegisterScheduleClassifier.GetPotentialImpact(AmigaChipset.OcsPal, offset));

		Assert.False(AffectsBusSchedule(0x180));
		Assert.False(AffectsBusSchedule(0x1BE));
		Assert.False(AffectsBusSchedule(0x181));
		Assert.False(AffectsBusSchedule(0x120));
		Assert.False(AffectsBusSchedule(0x146));
		Assert.False(AffectsBusSchedule(0x0A8));
		Assert.False(AffectsBusSchedule(0x0D8));
		Assert.True(AffectsBusSchedule(0x09A));
		Assert.True(AffectsBusSchedule(0x09C));
		Assert.True(AffectsBusSchedule(0x096));
		Assert.True(AffectsBusSchedule(0x09E));
		Assert.True(AffectsBusSchedule(0x092));
		Assert.True(AffectsBusSchedule(0x08E));
		Assert.True(AffectsBusSchedule(0x090));
		Assert.True(AffectsBusSchedule(0x100));
		Assert.True(AffectsBusSchedule(0x088));
		Assert.True(AffectsBusSchedule(0x0A4));
		Assert.True(AffectsBusSchedule(0x0A6));
		Assert.True(AffectsBusSchedule(0x0AA));
		Assert.True(AffectsBusSchedule(0x024));
		Assert.True(AffectsBusSchedule(0x058));
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
	public void CpuColorPresentationUsesDeniseVisiblePhase()
	{
		const long grantCycle = 100;
		var deniseVisibleCycle = grantCycle + (6 * AgnusChipSlotScheduler.SlotCycles);

		Assert.Equal(
			deniseVisibleCycle,
			AmigaBus.GetDisplayWriteCycle(AmigaBusRequester.Cpu, 0x180, grantCycle));
		Assert.Equal(
			grantCycle,
			AmigaBus.GetDisplayWriteCycle(AmigaBusRequester.Copper, 0x180, grantCycle));
		Assert.Equal(
			grantCycle,
			AmigaBus.GetDisplayWriteCycle(AmigaBusRequester.Cpu, 0x100, grantCycle));
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
		Assert.True(
			scheduler.CopperQuiescentShadowPredictions == scheduler.CopperQuiescentShadowMatches,
			scheduler.CopperQuiescentFirstShadowMismatch);
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
		Assert.True(
			scheduler.CopperQuiescentShadowPredictions == scheduler.CopperQuiescentShadowMatches,
			scheduler.CopperQuiescentFirstShadowMismatch);
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
	public void CopperQuiescentFastPathExecutesLongAccessAsCausalWords()
	{
		var bus = new AmigaBus(enableLiveAgnusDma: true, enableCopperQuiescentFastPath: true);
		StartCopperEndOfFrameWait(bus);
		var cycle = 2L * AmigaConstants.A500PalCpuCyclesPerRasterLine;

		_ = bus.ReadLong(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.CopperQuiescentFastPathRejectedUnsupported);
		Assert.True(scheduler.CopperQuiescentFastPathSkippedDrains >= 1);
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
		var horizon = bus.GetNextCpuVisibilityHorizon(
			currentCycle,
			3 * AmigaConstants.A500PalCpuCyclesPerRasterLine);

		Assert.InRange(
			candidate,
			currentCycle + 1,
			3 * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		Assert.Equal(M68kTraceBatchWakeSource.Copper, wakeSource);
		Assert.Equal(candidate, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.Copper, horizon.Reason);
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
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(1024));
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		cpu.ExecuteInstructions(64, null, NoOpBoundary.Instance);

		Assert.True(cpu.State.ProgramCounter > romBase + 2);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchAttempts >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchUsed >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchInstructions > 1);
		Assert.Equal(scheduler.DeferredCpuBusBatchInstructions, scheduler.DeferredCpuBusBatchSkippedInstructionFlushes);
	}

	[Fact]
	public void DeferredCpuSynchronizationObservesReadyAndPhysicalTailSeparately()
	{
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true);
		CreateNopRom(128).CopyTo(bus.ExpansionRam, 0);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(bus.ExpansionRamBase, 0x3000);

		cpu.ExecuteInstructions(16, cpu.State.Cycles + 1_000, NoOpBoundary.Instance);

		var checkpoint = bus.LastDeferredCpuBusCheckpoint;
		Assert.True(checkpoint.LastReadyCycle >= 0);
		Assert.Equal(
			0,
			checkpoint.PhysicalNextTransferCycle - checkpoint.LastReadyCycle);
		Assert.True(checkpoint.ArchitecturalRetireCycle <= checkpoint.PhysicalNextTransferCycle);
	}

	[Fact]
	public void ScalarInstructionEntryTimestampPrecedesPendingIrMaterialization()
	{
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false);
		CreateNopRom(128).CopyTo(bus.ExpansionRam, 0);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: false);
		cpu.Reset(bus.ExpansionRamBase, 0x3000);
		cpu.ExecuteInstructions(28, cpu.State.Cycles + 10_000, NoOpBoundary.Instance);
		var before = ((IM68000PrefetchDiagnostics)cpu).CapturePrefetchDiagnosticState();

		cpu.ExecuteInstruction();

		var after = ((IM68000PrefetchDiagnostics)cpu).CapturePrefetchDiagnosticState();
		Assert.Equal(before.Cycles, after.InstructionStartCycle);
		Assert.True(before.HasPendingPrefetch);
		Assert.Equal(0, before.PrefetchCount);
	}

	[Fact(Skip = "Stage 6 continuation: preserve refresh slip through the later required publication at instruction 29.")]
	public void DeferredCpuPublicationShadowConsumesSlipAlreadyAbsorbedBySuccessor()
	{
		const int instructionCount = 29;
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: false);
		CreateNopRom(128).CopyTo(bus.ExpansionRam, 0);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: false);
		cpu.Reset(bus.ExpansionRamBase, 0x3000);

		cpu.ExecuteInstructions(
			instructionCount,
			cpu.State.Cycles + 10_000,
			NoOpBoundary.Instance);

		Assert.True(bus.DeferredCpuRetirementPublicationShadowEntries > 0);
		Assert.Equal(
			M68kInstructionFetchPublicationPhase.CancellableSuccessor,
			bus.DeferredCpuRetirementPublicationShadowLastPhase);
		Assert.True(
			bus.DeferredCpuRetirementPublicationShadowLastNominalCycle == 118,
			$"shadow={bus.DeferredCpuRetirementPublicationShadowLastNominalCycle}/" +
			$"{bus.DeferredCpuRetirementPublicationShadowLastTranslatedCycle}; " +
			$"checkpoint={bus.PreviousDeferredCpuBusCheckpoint} -> " +
			$"{bus.LastDeferredCpuBusCheckpoint}; pipeline=" +
			$"{((IM68000PrefetchDiagnostics)cpu).CapturePrefetchDiagnosticState()}");
		Assert.Equal(116, bus.DeferredCpuRetirementPublicationShadowLastFloor);
		Assert.Equal(0, bus.DeferredCpuRetirementPublicationShadowLastInheritedDelay);
		Assert.Equal(118, bus.DeferredCpuRetirementPublicationShadowLastLegacyReadyCycle);
		Assert.Equal(120, bus.DeferredCpuRetirementPublicationShadowLastTranslatedCycle);
		Assert.Equal(
			bus.DeferredCpuInstructionPublicationPhaseSlipGroup,
			bus.DeferredCpuRetirementPublicationShadowLastGroup);
		Assert.Equal(18, bus.DeferredCpuRetirementPublicationShadowFirstNominalCycle);
		Assert.Equal(14, bus.DeferredCpuRetirementPublicationShadowFirstFloor);
		Assert.Equal(2, bus.DeferredCpuRetirementPublicationShadowFirstInheritedDelay);
		Assert.Equal(18, bus.DeferredCpuRetirementPublicationShadowFirstTranslatedCycle);
		Assert.Equal(0, bus.DeferredCpuRetirementPublicationShadowFirstContext.EntryPrefetchCount);
		Assert.Equal(10, bus.DeferredCpuRetirementPublicationShadowFirstContext.InstructionEntryCycle);
		Assert.Equal(12, bus.DeferredCpuRetirementPublicationShadowFirstContext.EntryBusCycle);
		Assert.Equal(0, bus.DeferredCpuRetirementPublicationShadowLastContext.EntryPrefetchCount);
		Assert.Equal(112, bus.DeferredCpuRetirementPublicationShadowLastContext.InstructionEntryCycle);
		Assert.Equal(114, bus.DeferredCpuRetirementPublicationShadowLastContext.EntryBusCycle);
		Assert.True(bus.DeferredCpuRetirementPublicationShadowTrimEntries > 0);
		Assert.True(bus.DeferredCpuRetirementPublicationShadowReplayEntries > 0);
		Assert.True(bus.DeferredCpuRetirementPublicationShadowLastWasReplay);
	}

	[Fact]
	public void DeferredCpuFullPrefetchPreservesIdentityBoundIrIrcGap()
	{
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: false);
		CreateNopRom(64).CopyTo(bus.ExpansionRam, 0);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: false);
		cpu.Reset(bus.ExpansionRamBase, 0x3000);
		cpu.State.A[0] = bus.ExpansionRamBase + 0x20;
		((IM68000PipelineStateTransfer)cpu).ImportM68000PipelineState(
			new M68000PipelineState(
				bus.ExpansionRamBase,
				0x4ED0,
				0,
				0,
				0,
				true,
				false,
				1,
				false,
				false,
				12,
				10,
				0,
				0));

		((IM68kBatchCore)cpu).ExecuteInstructions(2, 16, NoOpBoundary.Instance);

		var checkpoint = bus.LastDeferredCpuBusCheckpoint;
		var pipeline = ((IM68000PrefetchDiagnostics)cpu).CapturePrefetchDiagnosticState();
		Assert.True(
			checkpoint.ResolvedQueueMask == 3,
			$"selected={checkpoint}; previous={bus.PreviousDeferredCpuBusCheckpoint}; " +
			$"last={bus.LastDeferredCpuBusCheckpoint}; pipeline={pipeline}; shadow=" +
			$"{bus.DeferredCpuIrcPairShadowEntries}/" +
			$"{bus.DeferredCpuIrcPairShadowPredecessorReadyCycle}/" +
			$"{bus.DeferredCpuIrcPairShadowPredictedReadyCycle}");
		Assert.Equal(16, checkpoint.QueueReadyCycle0);
		Assert.Equal(20, checkpoint.QueueReadyCycle1);
		Assert.Equal(20, checkpoint.LastReadyCycle);
		Assert.Equal(20, checkpoint.PhysicalNextTransferCycle);
		Assert.True(bus.TryGetCommittedAgnusSlotOwner(14, out var owner14));
		Assert.Equal(AgnusChipSlotOwner.Cpu, owner14);
		var committed16 = bus.TryGetCommittedAgnusSlotOwner(16, out var owner16);
		Assert.True(!committed16 || owner16 != AgnusChipSlotOwner.Cpu);
		Assert.True(bus.TryGetCommittedAgnusSlotOwner(18, out var owner18));
		Assert.Equal(AgnusChipSlotOwner.Cpu, owner18);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(26)]
	[InlineData(27)]
	[InlineData(28)]
	[InlineData(29)]
	public void DeferredCpuRequestedCycleJournalMatchesScalarExpansionFetchesAcrossRefresh(int instructionCount)
	{
		var scalar = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false);
		var batched = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: false);
		CreateNopRom(2048).CopyTo(scalar.ExpansionRam, 0);
		CreateNopRom(2048).CopyTo(batched.ExpansionRam, 0);
		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(
			batched,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: false);
		scalarCpu.Reset(scalar.ExpansionRamBase, 0x3000);
		batchedCpu.Reset(batched.ExpansionRamBase, 0x3000);
		Assert.Equal(scalarCpu.State.Cycles, batchedCpu.State.Cycles);
		var batchStartCycle = scalarCpu.State.Cycles;
		var targetCycle = batchStartCycle + 10_000;

		var scalarExecuted = scalarCpu.ExecuteInstructions(instructionCount, targetCycle, NoOpBoundary.Instance);
		var batchedExecuted = batchedCpu.ExecuteInstructions(instructionCount, targetCycle, NoOpBoundary.Instance);
		Assert.True(
			scalarExecuted == batchedExecuted,
			$"executed={scalarExecuted}/{batchedExecuted},cycles={scalarCpu.State.Cycles}/{batchedCpu.State.Cycles}," +
			$"pc=0x{scalarCpu.State.ProgramCounter:X8}/0x{batchedCpu.State.ProgramCounter:X8}");

		var scalarPrefetch = ((IM68000PrefetchDiagnostics)scalarCpu).CapturePrefetchDiagnosticState();
		var batchedPrefetch = ((IM68000PrefetchDiagnostics)batchedCpu).CapturePrefetchDiagnosticState();
		Assert.True(
			scalarCpu.State.Cycles == batchedCpu.State.Cycles,
			$"scalar={scalarPrefetch}; batched={batchedPrefetch}; " +
			$"shadow={batched.DeferredCpuRetirementPublicationShadowEntries}/" +
			$"{batched.DeferredCpuRetirementPublicationShadowLastNominalCycle}/" +
			$"{batched.DeferredCpuRetirementPublicationShadowLastFloor}/" +
			$"{batched.DeferredCpuRetirementPublicationShadowLastInheritedDelay}/" +
			$"{batched.DeferredCpuRetirementPublicationShadowLastLegacyReadyCycle}/" +
			$"{batched.DeferredCpuRetirementPublicationShadowLastTranslatedCycle}/" +
			$"{batched.DeferredCpuRetirementPublicationShadowLastPhase}; " +
			$"slip={batched.DeferredCpuInstructionPublicationPhaseSlipGroup}/" +
			$"{batched.DeferredCpuInstructionPublicationPhaseSlipToken}/" +
			$"{batched.DeferredCpuInstructionPublicationPhaseSlipVirtualReadyCycle}/" +
			$"{batched.DeferredCpuInstructionPublicationPhaseSlipActualReadyCycle}/" +
			$"{batched.DeferredCpuInstructionPublicationPhaseSlipContext}; " +
			$"shadowContext={batched.DeferredCpuRetirementPublicationShadowLastContext}; " +
			$"maxProjection={batched.DeferredCpuProjectedMaxRetireDelay}/" +
			$"{batched.DeferredCpuProjectedMaxRetireVirtualCycle}/" +
			$"{batched.DeferredCpuProjectedMaxRetireDependencyCompletedCycle}/" +
			$"{batched.DeferredCpuProjectedMaxRetireDependencyToken}/" +
			$"{batched.DeferredCpuProjectedMaxRetireJournalCount}; " +
			$"lastProjection={batched.DeferredCpuProjectedLastVirtualCycle}/" +
			$"{batched.DeferredCpuProjectedLastDependencyCompletedCycle}/" +
			$"{batched.DeferredCpuProjectedLastDependencyToken}/" +
			$"{batched.DeferredCpuProjectedLastJournalCount}/" +
			$"{batched.DeferredCpuProjectedLastEntryCount}/" +
			$"{batched.DeferredCpuProjectedLastRejectReason}/" +
			$"{batched.DeferredCpuProjectedLastCpuEventCount}/" +
			$"{batched.DeferredCpuProjectedLastFailedIndex}/" +
			$"{batched.DeferredCpuProjectedLastFailedRequestedCycle}/" +
			$"{batched.DeferredCpuProjectedLastUnsupported}; " +
			$"checkpoint={batched.PreviousDeferredCpuBusCheckpoint} -> " +
			$"{batched.LastDeferredCpuBusCheckpoint}");
		Assert.Equal(scalarCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		var stopCycle = Math.Max(scalarCpu.State.Cycles, batchedCpu.State.Cycles);
		for (var slot = batchStartCycle; slot <= stopCycle; slot += AgnusChipSlotScheduler.SlotCycles)
		{
			var scalarCommitted = scalar.TryGetCommittedAgnusSlotOwner(slot, out var scalarOwner);
			var batchedCommitted = batched.TryGetCommittedAgnusSlotOwner(slot, out var batchedOwner);
			Assert.True(
				scalarCommitted == batchedCommitted && scalarOwner == batchedOwner,
				$"slot={slot},scalar={scalarCommitted}/{scalarOwner},batched={batchedCommitted}/{batchedOwner}; " +
				$"checkpoint={batched.PreviousDeferredCpuBusCheckpoint} -> " +
				$"{batched.LastDeferredCpuBusCheckpoint}; prefetch={batchedPrefetch}");
		}
		Assert.True(
			scalarPrefetch == batchedPrefetch,
			$"scalar={scalarPrefetch}; batched={batchedPrefetch}; checkpoint=" +
			$"{batched.PreviousDeferredCpuBusCheckpoint} -> {batched.LastDeferredCpuBusCheckpoint}");

		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		Assert.True(
			instructionCount < 8 ||
			scheduler.DeferredCpuBusBatchSkippedInstructionFlushes > 1);
		Assert.Equal(0, scheduler.DeferredCpuTimingProjectionMismatches);
		Assert.True(
			instructionCount < 1_000 ||
			stopCycle > batched.GetNextLineStartCycle(0));
	}

	[Fact]
	public void DeferredCpuBusBatchCollapsesRomInstructionBoundaryCallbacks()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(1024));
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		var boundary = new CountingDeferredBatchBoundary(bus);

		var executed = cpu.ExecuteInstructions(64, null, boundary);

		Assert.Equal(64, executed);
		Assert.True(boundary.BatchCallbacks > 0);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(boundary.BatchedInstructions > boundary.BatchCallbacks,
			$"batch={boundary.BatchCallbacks}/{boundary.BatchedInstructions},scalar={boundary.ScalarCallbacks}," +
			$"exits={scheduler.DeferredCpuBusBatchExitTargetCycle}/{scheduler.DeferredCpuBusBatchExitMaxInstructions}/" +
			$"{scheduler.DeferredCpuBusBatchExitChipVisibleAccess}/{scheduler.DeferredCpuBusBatchExitPcLeftFastWindow}");
		Assert.Equal(64, boundary.ScalarCallbacks + boundary.BatchedInstructions);
		Assert.True(boundary.ScalarCallbacks + boundary.BatchCallbacks < executed);
	}

	[Fact]
	public void DeferredCpuBusBatchCollapsesFastRamInstructionBoundaryCallbacks()
	{
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true);
		CreateNopRom(1024).CopyTo(bus.ExpansionRam, 0);
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(bus.ExpansionRamBase, 0x3000);
		var boundary = new CountingDeferredBatchBoundary(bus);

		var executed = cpu.ExecuteInstructions(64, null, boundary);

		Assert.Equal(64, executed);
		Assert.True(boundary.BatchCallbacks > 0);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(boundary.BatchedInstructions > boundary.BatchCallbacks,
			$"batch={boundary.BatchCallbacks}/{boundary.BatchedInstructions},scalar={boundary.ScalarCallbacks}," +
			$"exits={scheduler.DeferredCpuBusBatchExitTargetCycle}/{scheduler.DeferredCpuBusBatchExitMaxInstructions}/" +
			$"{scheduler.DeferredCpuBusBatchExitChipVisibleAccess}/{scheduler.DeferredCpuBusBatchExitPcLeftFastWindow}");
		Assert.Equal(64, boundary.ScalarCallbacks + boundary.BatchedInstructions);
		Assert.True(boundary.ScalarCallbacks + boundary.BatchCallbacks < executed);
	}

	[Fact]
	public void DeferredCpuBusBatchSteadyStateAllocatesNothing()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(4096));
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		_ = cpu.ExecuteInstructions(64, null, NoOpBoundary.Instance);

		var before = GC.GetAllocatedBytesForCurrentThread();
		_ = cpu.ExecuteInstructions(64, null, NoOpBoundary.Instance);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void DeferredCpuBusBatchRejectsPendingCpuVisibleInterrupt()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(64));
		bus.AbleCiaInterrupts(AmigaCiaId.A, 0x81, 0);
		bus.SetCiaInterrupts(AmigaCiaId.A, 0x81, 0);
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		var executed = cpu.ExecuteInstructions(8, null, NoOpBoundary.Instance);

		Assert.Equal(8, executed);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchAttempts > 0);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchUsed);
	}

	[Fact]
	public void DeferredCpuBusBatchFastRamSelfModificationInvalidatesSuccessorFetch()
	{
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true);
		var baseAddress = bus.ExpansionRamBase;
		var targetAddress = baseAddress + 0x20;
		var program = bus.ExpansionRam;
		BigEndian.WriteUInt16(program, 0x00, 0x4E71); // NOP primes batch admission.
		BigEndian.WriteUInt16(program, 0x02, 0x3080); // MOVE.W D0,(A0)
		BigEndian.WriteUInt16(program, 0x04, 0x4ED0); // JMP (A0)
		BigEndian.WriteUInt16(program, 0x20, 0x4AFC); // ILLEGAL, replaced before fetch.
		BigEndian.WriteUInt16(program, 0x22, 0x4E71);
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(baseAddress, 0x3000);
		cpu.State.A[0] = targetAddress;
		cpu.State.D[0] = 0x4E71;

		var executed = cpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(4, executed);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
		Assert.Equal(targetAddress + 2, cpu.State.ProgramCounter);
		Assert.Equal(0x4E71, BigEndian.ReadUInt16(program, 0x20, "modified opcode"));
		Assert.True(bus.CaptureHardwareSchedulerSnapshot().DeferredCpuBusBatchUsed > 0);
	}

	[Fact]
	public void DeferredCpuBusBatchBoundaryRejectsBeforeBusBatchSetup()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(64));
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		var executed = cpu.ExecuteInstructions(8, null, RejectDeferredBatchBoundary.Instance);

		Assert.Equal(8, executed);
		Assert.Equal(romBase + 16, cpu.State.ProgramCounter);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuBusBatchAttempts);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchUsed);
	}

	[Fact]
	public void DeferredCpuBusBatchScheduledBoundaryClampsExecutedBatch()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(256));
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		var boundary = new ScheduledDeferredBatchBoundary(cpu.State.Cycles + 32);

		var executed = cpu.ExecuteInstructions(32, cpu.State.Cycles + 128, boundary);

		Assert.True(executed > 0);
		Assert.True(boundary.Fired);
		Assert.True(boundary.FirstAdvanceCycle >= boundary.ScheduledCycle);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed > 0);
		Assert.True(scheduler.DeferredCpuBusBatchInstructions >= scheduler.DeferredCpuBusBatchUsed);
	}

	[Fact]
	public void DeferredCpuBusBatchMultipleBoundariesUseEarliestCycle()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(256));
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		var startCycle = cpu.State.Cycles;
		var boundary = new MultipleScheduledDeferredBatchBoundary(
			startCycle + 24,
			startCycle + 64);

		var executed = cpu.ExecuteInstructions(32, startCycle + 128, boundary);

		Assert.True(executed > 0);
		Assert.Equal(startCycle + 24, boundary.FirstScheduledCycle);
		Assert.InRange(boundary.FirstAdvanceCycle, startCycle + 24, startCycle + 27);
	}

	[Fact]
	public void DeferredCpuBusBatchBoundaryExceptionDoesNotLeakActiveBatch()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, CreateNopRom(256));
		var cpu = (IM68kBatchCore)M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		Assert.Throws<InvalidOperationException>(() =>
			cpu.ExecuteInstructions(8, null, ThrowingDeferredBatchBoundary.Instance));

		var afterException = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(1, afterException.DeferredCpuBusBatchAttempts);
		Assert.Equal(0, afterException.DeferredCpuBusBatchUsed);
		Assert.Equal(0, afterException.DeferredCpuBusBatchInstructions);

		var executed = cpu.ExecuteInstructions(8, null, NoOpBoundary.Instance);
		Assert.Equal(8, executed);
		Assert.True(bus.CaptureHardwareSchedulerSnapshot().DeferredCpuBusBatchUsed > 0);
	}

	[Fact]
	public void DeferredCpuBusBatchMatchesScalarMoveWordReadStorePairInExpansionRam()
	{
		const uint romBase = 0x00FC0000;
		const int pairs = 16;
		const int sourceOffset = 0x0000;
		const int destinationOffset = 0x0200;
		var program = CreateMoveWordReadStorePairProgram(pairs);
		var baseline = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false);
		var batched = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		baseline.MapReadOnlyMemory(romBase, program);
		batched.MapReadOnlyMemory(romBase, program);
		for (var i = 0; i < pairs; i++)
		{
			var value = (ushort)(0x4000 + i);
			BigEndian.WriteUInt16(baseline.ExpansionRam, sourceOffset + (i * 2), value);
			BigEndian.WriteUInt16(batched.ExpansionRam, sourceOffset + (i * 2), value);
		}

		var baselineCpu = M68kCoreFactory.CreateM68000Core(
			baseline,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(
			batched,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(romBase, 0x3000);
		batchedCpu.Reset(romBase, 0x3000);
		baselineCpu.State.A[0] = AmigaConstants.A500BootPseudoFastRamBase + sourceOffset;
		baselineCpu.State.A[1] = AmigaConstants.A500BootPseudoFastRamBase + destinationOffset;
		batchedCpu.State.A[0] = AmigaConstants.A500BootPseudoFastRamBase + sourceOffset;
		batchedCpu.State.A[1] = AmigaConstants.A500BootPseudoFastRamBase + destinationOffset;

		var baselineExecuted = baselineCpu.ExecuteInstructions(pairs * 2, null, NoOpBoundary.Instance);
		var batchedExecuted = batchedCpu.ExecuteInstructions(pairs * 2, null, NoOpBoundary.Instance);

		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		var baselineDestination = baseline.ExpansionRam
			.Skip(destinationOffset)
			.Take(pairs * 2)
			.ToArray();
		var batchedDestination = batched.ExpansionRam
			.Skip(destinationOffset)
			.Take(pairs * 2)
			.ToArray();
		var diagnostic =
			$"baseline cycles={baselineCpu.State.Cycles}, batched cycles={batchedCpu.State.Cycles}, " +
			$"baseline pc=0x{baselineCpu.State.ProgramCounter:X8}, batched pc=0x{batchedCpu.State.ProgramCounter:X8}, " +
			$"batch used={scheduler.DeferredCpuBusBatchUsed}, instructions={scheduler.DeferredCpuBusBatchInstructions}, " +
			$"verification={scheduler.DeferredCpuBusBatchVerificationMismatches}, firstMismatch={scheduler.DeferredCpuBusBatchFirstMismatch}";

		Assert.Equal(pairs * 2, baselineExecuted);
		Assert.Equal(pairs * 2, batchedExecuted);
		Assert.Equal(baselineCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.Cycles, batchedCpu.State.Cycles);
		Assert.Equal(baselineCpu.State.A[0], batchedCpu.State.A[0]);
		Assert.Equal(baselineCpu.State.A[1], batchedCpu.State.A[1]);
		Assert.Equal(baselineCpu.State.D[5], batchedCpu.State.D[5]);
		Assert.True(baselineDestination.SequenceEqual(batchedDestination), diagnostic);
		Assert.True(scheduler.DeferredCpuBusBatchUsed >= 1, diagnostic);
		Assert.True(scheduler.DeferredCpuBusBatchInstructions >= pairs, diagnostic);
		Assert.True(scheduler.DeferredCpuBusBatchUsed < pairs, diagnostic);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchVerificationMismatches);
	}

	[Fact]
	public void DeferredExpansionReadJournalUsesExecutorOwnedQuietSequence()
	{
		const uint romBase = 0x00FC0000;
		const int reads = 12;
		var program = new byte[(reads * 2) + 8];
		for (var i = 0; i < reads; i++)
		{
			program[i * 2] = 0x30;
			program[(i * 2) + 1] = 0x18; // MOVE.W (A0)+,D0
		}
		for (var i = reads * 2; i < program.Length; i += 2)
		{
			program[i] = 0x4E;
			program[i + 1] = 0x71;
		}

		var scalar = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: false);
		var batched = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: false,
			enableDeferredCpuChipReadSegments: true);
		scalar.MapReadOnlyMemory(romBase, program);
		batched.MapReadOnlyMemory(romBase, program);
		for (var i = 0; i < reads; i++)
		{
			BigEndian.WriteUInt16(scalar.ExpansionRam, i * 2, (ushort)(0x5000 + i));
			BigEndian.WriteUInt16(batched.ExpansionRam, i * 2, (ushort)(0x5000 + i));
		}

		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(
			batched, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x3000);
		batchedCpu.Reset(romBase, 0x3000);
		scalarCpu.State.A[0] = AmigaConstants.A500BootPseudoFastRamBase;
		batchedCpu.State.A[0] = AmigaConstants.A500BootPseudoFastRamBase;

		Assert.Equal(reads, scalarCpu.ExecuteInstructions(reads, null, NoOpBoundary.Instance));
		Assert.Equal(reads, batchedCpu.ExecuteInstructions(reads, null, NoOpBoundary.Instance));
		Assert.Equal(scalarCpu.State.Cycles, batchedCpu.State.Cycles);
		Assert.Equal(scalarCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(scalarCpu.State.A[0], batchedCpu.State.A[0]);
		Assert.Equal(scalarCpu.State.D[0], batchedCpu.State.D[0]);
		Assert.True(batched.CausalBusExecutor.CpuTimingSequenceRuns > 0);
		Assert.True(batched.CausalBusExecutor.CpuTimingSequenceWords >= 2);
	}

	[Fact]
	public void DeferredCpuBusBatchMatchesScalarExpansionRamAccessesAtFiniteTargetCycle()
	{
		const uint romBase = 0x00FC0000;
		const int pairs = 16;
		const int sourceOffset = 0x0000;
		const int destinationOffset = 0x0200;
		var program = CreateMoveWordReadStorePairProgram(pairs);
		var baseline = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false);
		var batched = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		baseline.MapReadOnlyMemory(romBase, program);
		batched.MapReadOnlyMemory(romBase, program);
		for (var i = 0; i < pairs; i++)
		{
			var value = (ushort)(0x5000 + i);
			BigEndian.WriteUInt16(baseline.ExpansionRam, sourceOffset + (i * 2), value);
			BigEndian.WriteUInt16(batched.ExpansionRam, sourceOffset + (i * 2), value);
		}

		var baselineCpu = M68kCoreFactory.CreateM68000Core(
			baseline,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(
			batched,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(romBase, 0x3000);
		batchedCpu.Reset(romBase, 0x3000);
		baselineCpu.State.A[0] = AmigaConstants.A500BootPseudoFastRamBase + sourceOffset;
		baselineCpu.State.A[1] = AmigaConstants.A500BootPseudoFastRamBase + destinationOffset;
		batchedCpu.State.A[0] = AmigaConstants.A500BootPseudoFastRamBase + sourceOffset;
		batchedCpu.State.A[1] = AmigaConstants.A500BootPseudoFastRamBase + destinationOffset;
		var targetCycle = baselineCpu.State.Cycles + 128;

		var baselineExecuted = baselineCpu.ExecuteInstructions(pairs * 2, targetCycle, NoOpBoundary.Instance);
		var batchedExecuted = batchedCpu.ExecuteInstructions(pairs * 2, targetCycle, NoOpBoundary.Instance);

		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		var diagnostic =
			$"baseline executed/cycles/pc={baselineExecuted}/{baselineCpu.State.Cycles}/0x{baselineCpu.State.ProgramCounter:X8}, " +
			$"batched={batchedExecuted}/{batchedCpu.State.Cycles}/0x{batchedCpu.State.ProgramCounter:X8}, " +
			$"batch={scheduler.DeferredCpuBusBatchUsed}/{scheduler.DeferredCpuBusBatchInstructions}, " +
			$"exit={scheduler.DeferredCpuBusBatchExitTargetCycle}/{scheduler.DeferredCpuBusBatchExitMaxInstructions}/" +
			$"{scheduler.DeferredCpuBusBatchExitChipVisibleAccess}/{scheduler.DeferredCpuBusBatchExitPcLeftFastWindow}, " +
			$"mismatch={scheduler.DeferredCpuBusBatchVerificationMismatches}:{scheduler.DeferredCpuBusBatchFirstMismatch}";

		Assert.True(baselineExecuted == batchedExecuted, diagnostic);
		Assert.True(baselineCpu.State.Cycles == batchedCpu.State.Cycles, diagnostic);
		Assert.Equal(baselineCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.A[0], batchedCpu.State.A[0]);
		Assert.Equal(baselineCpu.State.A[1], batchedCpu.State.A[1]);
		Assert.True(baseline.ExpansionRam.SequenceEqual(batched.ExpansionRam), diagnostic);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchVerificationMismatches);
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

		cpu.ExecuteInstructions(64, null, NoOpBoundary.Instance);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuBusBatchWakeDisk);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakePassiveByteReady);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakePendingDma);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakeActiveDmaCompletion);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchDiskWakeUnknown);
	}

	[Fact]
	public void CpuBatchWakeCandidateReusesExecutorAgendaForNoEventTarget()
	{
		var bus = new AmigaBus();
		const long targetCycle = 1000;

		var first = bus.GetNextCpuBatchWakeCandidateCycle(0, targetCycle, 0, out var firstWakeSource);
		var refreshesAfterFirst = bus.CausalBusExecutor.CpuVisibilitySourceRefreshes;
		var rootReadsAfterFirst = bus.CausalBusExecutor.CpuVisibilityRootReads;
		var second = bus.GetNextCpuBatchWakeCandidateCycle(10, targetCycle, 0, out var secondWakeSource);

		Assert.Equal(targetCycle, first);
		Assert.Equal(targetCycle, second);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, firstWakeSource);
		Assert.Equal(M68kTraceBatchWakeSource.TargetCycle, secondWakeSource);
		Assert.Equal(refreshesAfterFirst, bus.CausalBusExecutor.CpuVisibilitySourceRefreshes);
		Assert.Equal(rootReadsAfterFirst + 1, bus.CausalBusExecutor.CpuVisibilityRootReads);
	}

	[Fact]
	public void CpuBatchNoEventCacheDoesNotHidePendingInterrupt()
	{
		var bus = new AmigaBus();
		const long targetCycle = 1000;
		_ = bus.GetNextCpuBatchWakeCandidateCycle(0, targetCycle, 0, out _);
		var queriesBeforeInterrupt = bus.CausalBusExecutor.CpuVisibilityQueries;

		bus.AbleCiaInterrupts(AmigaCiaId.A, 0x81, 0);
		bus.SetCiaInterrupts(AmigaCiaId.A, 0x81, 0);
		var wake = bus.GetNextCpuBatchWakeCandidateCycle(100, targetCycle, 0, out var wakeSource);
		var horizon = bus.GetNextCpuVisibilityHorizon(100, targetCycle, 0);

		Assert.Equal(101, wake);
		Assert.Equal(M68kTraceBatchWakeSource.PendingInterrupt, wakeSource);
		Assert.Equal(wake, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.PendingInterrupt, horizon.Reason);
		Assert.Equal(queriesBeforeInterrupt + 2, bus.CausalBusExecutor.CpuVisibilityQueries);
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
	public void DeferredCpuWaitWindowDisabledIsInert()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var cycle = 20L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
		_ = bus.ReadWord(0x00001002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowAttempts);
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowEligible);
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowTotalCycles);
	}

	[Fact]
	public void DeferredCpuWaitWindowNormalModeSkipsDiagnosticsWithoutBatchExit()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true);
		var cycle = 20L;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowAttempts);
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowFastPathAttempts);
	}

	[Fact]
	public void CausalExecutorRecordsChipFetchReadAndWriteWords()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		var cycle = 20L;
		var before = bus.CausalBusExecutor.CpuGrantedWords;

		var fetched = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
		var read = bus.ReadWord(0x00001002, ref cycle, AmigaBusAccessKind.CpuDataRead);
		bus.WriteWord(0x00001004, 0x1234, ref cycle, AmigaBusAccessKind.CpuDataWrite);

		Assert.Equal(0, fetched);
		Assert.Equal(0, read);
		Assert.Equal(0x1234, BigEndian.ReadUInt16(bus.ChipRam, 0x1004, "causal CPU write"));
		Assert.Equal(before + 3, bus.CausalBusExecutor.CpuGrantedWords);
		Assert.Equal(cycle - AgnusChipSlotScheduler.SlotCycles, bus.CausalBusExecutor.LastCpuGrantedCycle);
	}

	[Fact]
	public void CausalExecutorRecordsSplitLongExpansionAndCustomWords()
	{
		var bus = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		var cycle = 22L;
		var beforeWords = bus.CausalBusExecutor.CpuGrantedWords;

		_ = bus.ReadWord(0x00001000, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(beforeWords + 1, bus.CausalBusExecutor.CpuGrantedWords);
		_ = bus.ReadLong(0x00001002, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(beforeWords + 3, bus.CausalBusExecutor.CpuGrantedWords);
		_ = bus.ReadWord(AmigaConstants.A500BootPseudoFastRamBase, ref cycle, AmigaBusAccessKind.CpuDataRead);
		Assert.Equal(beforeWords + 3, bus.CausalBusExecutor.CpuGrantedWords);
		_ = bus.ReadWord(0x00DFF002, ref cycle, AmigaBusAccessKind.CpuDataRead);

		// Real expansion RAM bypasses Agnus; Chip RAM, the split long read,
		// and the custom-register word remain executor-owned.
		Assert.Equal(beforeWords + 4, bus.CausalBusExecutor.CpuGrantedWords);
		Assert.Equal(0x00DFF002u, bus.CausalBusExecutor.LastCpuAddress);
		Assert.True(bus.CausalBusExecutor.ExecutedThroughCycle >= bus.CausalBusExecutor.LastCpuGrantedCycle);
	}

	[Fact]
	public void DeferredCpuInternalNoBusWindowDisabledIsInertForChipFetchedMultiply()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC0C1); // MULU.W D1,D0
		BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x4E71);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 7;
		cpu.State.D[1] = 9;

		cpu.ExecuteInstruction();

		Assert.Equal(63u, cpu.State.D[0]);
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuInternalNoBusWindowAttempts);
		Assert.Equal(0, scheduler.DeferredCpuInternalNoBusWindowUsed);
	}

	[Fact]
	public void DeferredCpuInternalNoBusWindowRecordsChipFetchedMultiplyAndDivide()
	{
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuInternalNoBusWindow: true);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0xC0D0); // MULU.W (A0),D0
		BigEndian.WriteUInt16(bus.ChipRam, 0x1002, 0x80C1); // DIVU.W D1,D0
		BigEndian.WriteUInt16(bus.ChipRam, 0x1004, 0x4E71);
		BigEndian.WriteUInt16(bus.ChipRam, 0x2000, 6);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 7;
		cpu.State.D[1] = 3;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_000Eu, cpu.State.D[0]);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuInternalNoBusWindowAttempts >= 2);
		Assert.True(scheduler.DeferredCpuInternalNoBusWindowUsed >= 2);
		Assert.True(scheduler.DeferredCpuInternalNoBusWindowTotalCycles > 0);
		Assert.True(scheduler.DeferredCpuInternalNoBusWindowAdvancedCycles > 0);
		Assert.Equal(1, scheduler.DeferredCpuInternalNoBusWindowMultiply);
		Assert.Equal(1, scheduler.DeferredCpuInternalNoBusWindowDivide);
		Assert.Equal(0, scheduler.DeferredCpuInternalNoBusWindowVerificationMismatches);
	}

	[Fact]
	public void DeferredCpuInternalNoBusWindowDoesNotRecordDivideByZero()
	{
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x80C1); // DIVU.W D1,D0
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 42;
		cpu.State.D[1] = 0;

		cpu.ExecuteInstruction();

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuInternalNoBusWindowAttempts);
		Assert.Equal(0, scheduler.DeferredCpuInternalNoBusWindowUsed);
		Assert.Equal(0, scheduler.DeferredCpuInternalNoBusWindowDivide);
	}

	[Fact]
	public void DeferredCpuBusBatchExitsBeforeChipFetchedInstruction()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x4E, 0x71,
			0x4E, 0x71,
			0x4E, 0xF9, 0x00, 0x00, 0x10, 0x00,
			0x4E, 0x71
		});
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstructions(5, null, NoOpBoundary.Instance);

		// The target is executed only after the ROM batch exits; ORI.B #0,D0
		// consumes the first two zero words in Chip RAM and retires at $1004.
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed > 0);
		Assert.Equal(
			1,
			scheduler.DeferredCpuBusBatchExitPcLeftFastWindow +
				scheduler.DeferredCpuBusBatchExitChipVisibleAccess);
	}

	[Fact]
	public void DeferredCpuBusBatchFlushesForChipDataAccess()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		});
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		cpu.State.A[0] = 0x1000;

		cpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFFu);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		var diagnostic = $"batch={scheduler.DeferredCpuBusBatchAttempts}/{scheduler.DeferredCpuBusBatchUsed}/" +
			$"{scheduler.DeferredCpuBusBatchInstructions}, exit={scheduler.DeferredCpuBusBatchExitChipVisibleAccess}/" +
			$"{scheduler.DeferredCpuBusBatchExitPcLeftFastWindow}, pc=0x{cpu.State.ProgramCounter:X8}, cycles={cpu.State.Cycles}";
		Assert.True(scheduler.DeferredCpuBusBatchExitChipVisibleAccess >= 1, diagnostic);
		Assert.True(scheduler.DeferredCpuBusBatchInstructions >= 1);
	}

	[Fact]
	public void DeferredCpuBusBatchAccountsFirstInstructionChipDataExit()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		}); // Scalar prefetch warmup; then MOVE.W (A0),D0 is the first batched instruction.
		BigEndian.WriteUInt16(bus.ChipRam, 0x1000, 0x1234);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		cpu.State.A[0] = 0x1000;

		cpu.ExecuteInstructions(3, null, NoOpBoundary.Instance);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFFu);
		Assert.Equal(1, scheduler.DeferredCpuBusBatchUsed);
		Assert.Equal(1, scheduler.DeferredCpuBusBatchInstructions);
		Assert.Equal(1, scheduler.DeferredCpuBusBatchFlushes);
		Assert.Equal(1, scheduler.DeferredCpuBusBatchExitChipVisibleAccess);
	}

	[Fact]
	public void DeferredCpuBusBatchStopsBeforeArchitecturalTrapHandlerInChipRam()
	{
		const uint romBase = 0x00FC0000;
		const uint handler = 0x00001000;
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		var rom = CreateNopRom(64);
		rom[0] = 0x4E;
		rom[1] = 0x71; // NOP
		rom[2] = 0x4E;
		rom[3] = 0x71; // NOP
		rom[4] = 0x4E;
		rom[5] = 0x40; // TRAP #0
		bus.MapReadOnlyMemory(romBase, rom);
		BigEndian.WriteUInt32(bus.ChipRam, 32 * 4, handler);
		BigEndian.WriteUInt16(bus.ChipRam, (int)handler, 0x4E71);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstructions(3, null, NoOpBoundary.Instance);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(32, cpu.State.LastExceptionVector);
		Assert.Equal(handler, cpu.State.ProgramCounter);
		Assert.Equal(1, scheduler.DeferredCpuBusBatchUsed);
		Assert.Equal(2, scheduler.DeferredCpuBusBatchInstructions);
		Assert.Equal(1, scheduler.DeferredCpuBusBatchFlushes);
		Assert.Equal(
			1,
			scheduler.DeferredCpuBusBatchExitPcLeftFastWindow +
				scheduler.DeferredCpuBusBatchExitChipVisibleAccess);
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

		cpu.ExecuteInstructions(64, null, NoOpBoundary.Instance);

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
			0x4E, 0x71,
			0x4E, 0x71,
			0x10, 0x39, 0x00, 0xBF, 0xED, 0x01,
			0x4E, 0x71
		}); // Two scalar warmups; then side-effectful CIAA ICR read.
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed >= 1);
		Assert.True(scheduler.DeferredCpuBusBatchExitChipVisibleAccess >= 1);
	}

	[Fact]
	public void DeferredCpuBusBatchAdvanceUntilCpuGrantHandlesChipRamWordRead()
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

		baselineCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);
		batchedCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(baselineCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.D[0], batchedCpu.State.D[0]);
		Assert.Equal(baselineCpu.State.Cycles, batchedCpu.State.Cycles);
		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed > 0);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchVerificationMismatches);
	}

	[Fact]
	public void DeferredCpuBusBatchAdvanceUntilCpuGrantHandlesLongAccess()
	{
		const uint romBase = 0x00FC0000;
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, new byte[]
		{
			0x4E, 0x71,
			0x4E, 0x71,
			0x20, 0x10,
			0x4E, 0x71
		});
		BigEndian.WriteUInt32(bus.ChipRam, 0x1000, 0x12345678);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);
		cpu.State.A[0] = 0x1000;

		cpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(0x12345678u, cpu.State.D[0]);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed > 0);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchVerificationMismatches);
	}

	[Fact]
	public void DeferredCpuBusBatchReferencePathDoesNotEnterRetiredWaitSlotExecutor()
	{
		const uint romBase = 0x00FC0000;
		var baseline = new AmigaBus(captureBusAccesses: false);
		var reference = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true,
			forceCpuWaitSlotReference: true);
		var program = new byte[]
		{
			0x4E, 0x71,
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		};
		baseline.MapReadOnlyMemory(romBase, program);
		reference.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x5678);
		BigEndian.WriteUInt16(reference.ChipRam, 0x1000, 0x5678);
		var baselineCpu = M68kCoreFactory.CreateM68000Core(baseline, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var referenceCpu = M68kCoreFactory.CreateM68000Core(reference, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(romBase, 0x3000);
		referenceCpu.Reset(romBase, 0x3000);
		baselineCpu.State.A[0] = 0x1000;
		referenceCpu.State.A[0] = 0x1000;

		baselineCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);
		referenceCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(baselineCpu.State.ProgramCounter, referenceCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.D[0], referenceCpu.State.D[0]);
		Assert.Equal(baselineCpu.State.Cycles, referenceCpu.State.Cycles);
		var scheduler = reference.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowFastPathAttempts);
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowFastPathUsed);
		Assert.Equal(0, scheduler.DeferredCpuWaitSlotShadowAttempts);
		Assert.Equal(0, scheduler.DeferredCpuWaitWindowAttempts);
	}

	[Fact]
	public void DeferredCpuBusBatchAdvanceUntilCpuGrantHandlesActiveBlitter()
	{
		const uint romBase = 0x00FC0000;
		var program = new byte[]
		{
			0x4E, 0x71,
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		};
		var baseline = new AmigaBus(captureBusAccesses: false);
		var batched = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		baseline.MapReadOnlyMemory(romBase, program);
		batched.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x1234);
		BigEndian.WriteUInt16(batched.ChipRam, 0x1000, 0x1234);
		StartLongBlit(baseline);
		StartLongBlit(batched);
		var baselineCpu = M68kCoreFactory.CreateM68000Core(baseline, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(batched, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(romBase, 0x3000);
		batchedCpu.Reset(romBase, 0x3000);
		baselineCpu.State.A[0] = 0x1000;
		batchedCpu.State.A[0] = 0x1000;

		baselineCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);
		batchedCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(baselineCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.D[0], batchedCpu.State.D[0]);
		Assert.Equal(baselineCpu.State.Cycles, batchedCpu.State.Cycles);
		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed > 0);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchVerificationMismatches);
		Assert.Equal(baseline.Blitter.CaptureSnapshot(), batched.Blitter.CaptureSnapshot());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void DeferredCpuSyntheticRomOrExpansionWorkloadFallsBackAroundActiveBlitter(bool executeFromExpansion)
	{
		const uint romBase = 0x00FC0000;
		const int repetitions = 8;
		const int readsPerBlit = 16;
		var program = CreateSyntheticRomBlitterWorkload(repetitions, readsPerBlit);
		var baseline = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false);
		var batched = new AmigaBus(
			expansionRamSize: AmigaConstants.A500BootPseudoFastRamSize,
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		var programAddress = executeFromExpansion
			? baseline.ExpansionRamBase + 0x8000u
			: romBase;
		if (executeFromExpansion)
		{
			program.AsSpan().CopyTo(baseline.ExpansionRam.AsSpan(0x8000));
			program.AsSpan().CopyTo(batched.ExpansionRam.AsSpan(0x8000));
		}
		else
		{
			baseline.MapReadOnlyMemory(programAddress, program);
			batched.MapReadOnlyMemory(programAddress, program);
		}
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x1234);
		BigEndian.WriteUInt16(batched.ChipRam, 0x1000, 0x1234);
		StartLongBlit(baseline);
		StartLongBlit(batched);
		var baselineCpu = M68kCoreFactory.CreateM68000Core(baseline, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(batched, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(programAddress, 0x3000);
		batchedCpu.Reset(programAddress, 0x3000);
		baselineCpu.State.A[0] = 0x1000;
		batchedCpu.State.A[0] = 0x1000;

		var instructionCount = repetitions * (1 + readsPerBlit);
		var baselineExecuted = baselineCpu.ExecuteInstructions(instructionCount, null, NoOpBoundary.Instance);
		var batchedExecuted = batchedCpu.ExecuteInstructions(instructionCount, null, NoOpBoundary.Instance);

		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		var diagnostic =
			$"source={(executeFromExpansion ? "expansion" : "rom")}," +
			$"executed={baselineExecuted}/{batchedExecuted}," +
			$"baselinePc={baselineCpu.State.ProgramCounter:X8},batchedPc={batchedCpu.State.ProgramCounter:X8}," +
			$"baselineA0={baselineCpu.State.A[0]:X8},batchedA0={batchedCpu.State.A[0]:X8}," +
			$"baselineCycles={baselineCpu.State.Cycles},batchedCycles={batchedCpu.State.Cycles}," +
			$"overlap={scheduler.DeferredCpuWaitBlitterOverlapAttempts}/" +
			$"{scheduler.DeferredCpuWaitBlitterOverlapSupported}/" +
			$"{scheduler.DeferredCpuWaitBlitterOverlapUnsupported}/" +
			$"{scheduler.DeferredCpuWaitBlitterOverlapNasty}," +
			$"batch={scheduler.DeferredCpuBusBatchAttempts}/{scheduler.DeferredCpuBusBatchUsed}," +
			$"waitfast={scheduler.DeferredCpuWaitWindowFastPathAttempts}/" +
			$"{scheduler.DeferredCpuWaitWindowFastPathUsed}," +
			$"shadow={scheduler.DeferredCpuWaitSlotShadowAttempts}/" +
			$"{scheduler.DeferredCpuWaitSlotShadowMatches}/" +
			$"{scheduler.DeferredCpuWaitSlotShadowMismatches}/" +
			$"{scheduler.DeferredCpuWaitSlotShadowUnsupported}," +
			$"shadowBlit={scheduler.DeferredCpuWaitSlotShadowBlitterScratchAttempts}/" +
			$"{scheduler.DeferredCpuWaitSlotShadowBlitterScratchSupported}/" +
			$"{scheduler.DeferredCpuWaitSlotShadowBlitterScratchUnsupported}/" +
			$"{scheduler.DeferredCpuWaitSlotShadowBlitterScratchMatches}/" +
			$"{scheduler.DeferredCpuWaitSlotShadowBlitterScratchMismatches}," +
			$"shadowFirst={scheduler.DeferredCpuWaitSlotShadowFirstMismatch}," +
			$"baselineBlit={baseline.Blitter.CaptureSnapshot().Busy}/" +
			$"{baseline.Blitter.CaptureSnapshot().CurrentCycle}," +
			$"batchedBlit={batched.Blitter.CaptureSnapshot().Busy}/" +
			$"{batched.Blitter.CaptureSnapshot().CurrentCycle}";
		Assert.True(baselineCpu.State.ProgramCounter == batchedCpu.State.ProgramCounter, diagnostic);
		Assert.True(baselineCpu.State.A[0] == batchedCpu.State.A[0], diagnostic);
		Assert.True(baselineCpu.State.Cycles == batchedCpu.State.Cycles, diagnostic);
		Assert.True(scheduler.DeferredCpuBusBatchUsed > 0);
		Assert.True(batched.CausalBusExecutor.ProductionEnabled);
	}

	[Fact]
	public void DeferredCpuBusBatchBlitterScratchHandlesPartialAreaMicroOp()
	{
		const uint romBase = 0x00FC0000;
		var program = new byte[]
		{
			0x4E, 0x71,
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		};
		var baseline = new AmigaBus(captureBusAccesses: false);
		var batched = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		baseline.MapReadOnlyMemory(romBase, program);
		batched.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x1234);
		BigEndian.WriteUInt16(batched.ChipRam, 0x1000, 0x1234);
		StartLongBlit(baseline);
		StartLongBlit(batched);
		Assert.True(baseline.Blitter.AdvanceCpuWaitAreaMicroOpTo(baseline.Blitter.CaptureSnapshot().CurrentCycle));
		Assert.True(batched.Blitter.AdvanceCpuWaitAreaMicroOpTo(batched.Blitter.CaptureSnapshot().CurrentCycle));
		var baselineCpu = M68kCoreFactory.CreateM68000Core(baseline, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(batched, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(romBase, 0x3000);
		batchedCpu.Reset(romBase, 0x3000);
		baselineCpu.State.A[0] = 0x1000;
		batchedCpu.State.A[0] = 0x1000;

		baselineCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);
		batchedCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(baselineCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.D[0], batchedCpu.State.D[0]);
		Assert.Equal(baselineCpu.State.Cycles, batchedCpu.State.Cycles);
		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchUsed > 0);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchVerificationMismatches);
		Assert.Equal(baseline.Blitter.CaptureSnapshot(), batched.Blitter.CaptureSnapshot());
	}

	[Fact]
	public void DeferredCpuBusBatchAdvanceUntilCpuGrantHandlesNastyBlitter()
	{
		const uint romBase = 0x00FC0000;
		var program = new byte[]
		{
			0x4E, 0x71,
			0x4E, 0x71,
			0x30, 0x10,
			0x4E, 0x71
		};
		var baseline = new AmigaBus(captureBusAccesses: false);
		var batched = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			verifyDeferredCpuBusBatch: true);
		baseline.MapReadOnlyMemory(romBase, program);
		batched.MapReadOnlyMemory(romBase, program);
		BigEndian.WriteUInt16(baseline.ChipRam, 0x1000, 0x1234);
		BigEndian.WriteUInt16(batched.ChipRam, 0x1000, 0x1234);
		StartNastyBlit(baseline);
		StartNastyBlit(batched);
		var baselineCpu = M68kCoreFactory.CreateM68000Core(baseline, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var batchedCpu = M68kCoreFactory.CreateM68000Core(batched, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		baselineCpu.Reset(romBase, 0x3000);
		batchedCpu.Reset(romBase, 0x3000);
		baselineCpu.State.A[0] = 0x1000;
		batchedCpu.State.A[0] = 0x1000;

		baselineCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);
		batchedCpu.ExecuteInstructions(4, null, NoOpBoundary.Instance);

		Assert.Equal(baselineCpu.State.ProgramCounter, batchedCpu.State.ProgramCounter);
		Assert.Equal(baselineCpu.State.D[0], batchedCpu.State.D[0]);
		Assert.Equal(baselineCpu.State.Cycles, batchedCpu.State.Cycles);
		var scheduler = batched.CaptureHardwareSchedulerSnapshot();
		// Nasty mode is a CPU-visible contention boundary, so the conservative
		// horizon rejects speculation and keeps this access on the scalar path.
		Assert.Equal(0, scheduler.DeferredCpuBusBatchUsed);
		Assert.Equal(0, scheduler.DeferredCpuBusBatchVerificationMismatches);
		Assert.Equal(baseline.Blitter.CaptureSnapshot(), batched.Blitter.CaptureSnapshot());
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
	public void StoppedCpuWakeCandidateIncludesFirstCausalBlitterTransfer()
	{
		var bus = new AmigaBus();
		StartNastyBlit(bus, startCycle: 100);
		bus.AdvanceDmaTo(100);
		var completionCycle = bus.Blitter.GetPredictedCompletionCycle();

		var candidate = bus.GetNextCpuBatchWakeCandidateCycle(100, 1000, out var wakeSource);
		var horizon = bus.GetNextCpuVisibilityHorizon(100, 1000);

		Assert.Equal(102, candidate);
		Assert.True(candidate < completionCycle);
		Assert.Equal(M68kTraceBatchWakeSource.Blitter, wakeSource);
		Assert.Equal(candidate, horizon.Cycle);
		Assert.Equal(CpuVisibilityHorizonReason.Blitter, horizon.Reason);
	}

	private static void Write(byte[] memory, int address, params byte[] data)
	{
		Array.Copy(data, 0, memory, address, data.Length);
	}

	private static byte[] CreateVerticalSyncPhaseLoopProgram()
	{
		const int syncNops = 96;
		var program = new List<byte>(226);
		var loopStart = program.Count;
		EmitWord(program, 0x4E71); // NOP
		EmitWord(program, 0x337C); // MOVE.W #$0404,$0180(A1)
		EmitWord(program, 0x0404);
		EmitWord(program, 0x0180);
		for (var i = 0; i < syncNops; i++)
		{
			EmitWord(program, 0x4E71);
		}

		EmitWord(program, 0x3413); // MOVE.W (A3),D2
		EmitWord(program, 0x337C); // MOVE.W #$0F0F,$0180(A1)
		EmitWord(program, 0x0F0F);
		EmitWord(program, 0x0180);
		EmitWord(program, 0x0242); // ANDI.W #$FF00,D2
		EmitWord(program, 0xFF00);
		EmitWord(program, 0x0C42); // CMPI.W #$FF00,D2
		EmitWord(program, 0xFF00);
		EmitWord(program, 0x6600); // BNE.W back to loop start
		EmitWord(program, unchecked((ushort)(loopStart - program.Count)));
		EmitWord(program, 0x337C); // MOVE.W #$0000,$0180(A1)
		EmitWord(program, 0x0000);
		EmitWord(program, 0x0180);

		return program.ToArray();
	}

	private static byte[] CreateSynccpuBeamPollingProgram(
		ushort waitLineValue = 0xF000,
		ushort verticalStopValue = 0xFF00)
	{
		var program = new List<byte>(320);

		var waitLineLoop = program.Count;
		EmitWord(program, 0x3413); // MOVE.W (A3),D2
		EmitWord(program, 0x0242); // ANDI.W #$FF00,D2
		EmitWord(program, 0xFF00);
		EmitWord(program, 0x0C42); // CMPI.W #$F000,D2
		EmitWord(program, waitLineValue);
		EmitBneShort(program, waitLineLoop);
		EmitWord(program, 0x0269); // ANDI.W #$0001,$0004(A1)
		EmitWord(program, 0x0001);
		EmitWord(program, 0x0004);
		EmitBneShort(program, waitLineLoop);

		EmitMoveColor00(program, 0x0F0F);
		var sync1Loop = program.Count;
		EmitWord(program, 0x0253); // ANDI.W #$000F,(A3)
		EmitWord(program, 0x000F);
		EmitBneShort(program, sync1Loop);

		EmitMoveColor00(program, 0x0606);
		var sync2Loop = program.Count;
		EmitWord(program, 0x0253); // ANDI.W #$001F,(A3)
		EmitWord(program, 0x001F);
		EmitBneShort(program, sync2Loop);

		EmitMoveColor00(program, 0x0A0A);
		var sync3Loop = program.Count;
		EmitWord(program, 0x0253); // ANDI.W #$00FF,(A3)
		EmitWord(program, 0x00FF);
		EmitWord(program, 0x4E71);
		EmitWord(program, 0x4E71);
		EmitWord(program, 0x4E71);
		EmitBneShort(program, sync3Loop);

		EmitWord(program, 0x740A); // MOVEQ #10,D2
		var adjustLoop = program.Count;
		EmitWord(program, 0x51CA); // DBRA D2,.adjust
		EmitBranchDisplacement(program, adjustLoop);

		var verticalLoop = program.Count;
		EmitWord(program, 0x4E71);
		EmitMoveColor00(program, 0x0404);
		for (var i = 0; i < 96; i++)
		{
			EmitWord(program, 0x4E71);
		}

		EmitWord(program, 0x3413); // MOVE.W (A3),D2
		EmitMoveColor00(program, 0x0F0F);
		EmitWord(program, 0x0242); // ANDI.W #$FF00,D2
		EmitWord(program, 0xFF00);
		EmitWord(program, 0x0C42); // CMPI.W #$FF00,D2
		EmitWord(program, verticalStopValue);
		EmitBneWord(program, verticalLoop);

		EmitMoveColor00(program, 0x0000);

		return program.ToArray();
	}

	private static Probe10Sync1ExitRun RunProbe10Sync1ExitAtBeam(
		int expectedReadHorizontal,
		int expectedColorHorizontal)
	{
		const int programAddress = 0x1000;
		var program = new List<byte>();
		var loop = program.Count;
		EmitWord(program, 0x0253); // ANDI.W #$000F,(A3)
		EmitWord(program, 0x000F);
		EmitBneShort(program, loop);
		EmitMoveColor00(program, 0x0606);
		EmitWord(program, 0x60FE); // BRA.S self
		var observedAlignments = new HashSet<string>();

		for (var startOffset = 0; startOffset < AmigaConstants.A500PalCpuCyclesPerRasterLine; startOffset += 2)
		{
			var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
			Write(bus.ChipRam, programAddress, program.ToArray());
			ConfigureProbe10BitplaneDma(bus);
			bus.WriteWord(0x00DFF100, 0x2200);
			var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
			cpu.Reset(programAddress, 0x8000);
			cpu.State.A[1] = 0x00DFF000;
			cpu.State.A[3] = 0x00DFF006;
			cpu.State.Cycles =
				(32L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + startOffset;
			var startCycle = cpu.State.Cycles;

			for (var instruction = 0; instruction < 128; instruction++)
			{
				cpu.ExecuteInstruction();
				var color = bus.CustomRegisterWrites.FirstOrDefault(write =>
					write.Address == 0x180 && write.Value == 0x0606);
				if (color.Address != 0x180)
				{
					continue;
				}

				var finalRead = bus.CustomRegisterReads.Last(read => read.Address == 0x006);
				var readBeam = bus.GetBeamPosition(finalRead.SampleCycle);
				var colorBeam = bus.GetBeamPosition(color.Cycle);
				observedAlignments.Add($"v{readBeam.BeamLine}h{readBeam.BeamHorizontal}->v{colorBeam.BeamLine}h{colorBeam.BeamHorizontal}");
				if (readBeam.BeamLine == 32 &&
					readBeam.BeamHorizontal == expectedReadHorizontal &&
					colorBeam.BeamLine == 32 &&
					colorBeam.BeamHorizontal == expectedColorHorizontal)
				{
					return new Probe10Sync1ExitRun(bus, startCycle, finalRead, color);
				}

				break;
			}
		}

		throw new InvalidOperationException(
			$"Unable to align sync1 exit to read h{expectedReadHorizontal} and COLOR00 h{expectedColorHorizontal}; " +
			$"observed=[{string.Join(",", observedAlignments)}].");
	}

	private static Probe10Irq2Lifecycle[] RunProbe10RepeatedIrq2Lifecycle(bool captureBusAccesses)
	{
		const int mainAddress = 0x1000;
		const int irq2Address = 0x1200;
		const int synccpuAddress = 0x1400;
		const uint purpleMovePc = synccpuAddress + 20;
		var bus = new AmigaBus(
			captureBusAccesses: captureBusAccesses,
			enableLiveAgnusDma: true,
			enableHardwareSpecialization: true);
		bus.CaptureCustomRegisterReadTrace(0x006, 2, 65536);
		StartCopperListAtFrameStart(
			bus,
			(0x1001, 0xFFFE),
			(0x09C, 0x8008),
			(0x4001, 0xFFFE),
			(0x100, 0x2200),
			(0xFFDF, 0xFFFE),
			(0x1001, 0xFFFE),
			(0x02A, 0x8001),
			(0x3885, 0xFFFE),
			(0x09C, 0x8004),
			(0xFFFF, 0xFFFE));
		ConfigureProbe10BitplaneDma(bus);

		Write(bus.ChipRam, mainAddress,
		[
			0x4E, 0x71,       // NOP
			0x60, 0xFC        // BRA.S main
		]);
		var irq2 = new List<byte>();
		EmitWord(irq2, 0x337C); // MOVE.W #IntreqPorts,$009C(A1)
		EmitWord(irq2, AmigaConstants.IntreqPorts);
		EmitWord(irq2, 0x009C);
		EmitWord(irq2, 0x48E7); // MOVEM.L D0-A6,-(SP)
		EmitWord(irq2, 0xFFFE);
		EmitWord(irq2, 0x4EB9); // JSR synccpu
		EmitWord(irq2, (ushort)(synccpuAddress >> 16));
		EmitWord(irq2, (ushort)synccpuAddress);
		EmitWord(irq2, 0x303C); // MOVE.W #9500,D0 (source-shaped reporting workload)
		EmitWord(irq2, 9500);
		var reportDelayLoop = irq2.Count;
		EmitWord(irq2, 0x51C8); // DBRA D0,.reportDelay
		EmitBranchDisplacement(irq2, reportDelayLoop);
		EmitWord(irq2, 0x4CDF); // MOVEM.L (SP)+,D0-A6
		EmitWord(irq2, 0x7FFF);
		EmitWord(irq2, 0x4E73); // RTE
		Write(bus.ChipRam, irq2Address, irq2.ToArray());
		var synccpu = new List<byte>(CreateSynccpuBeamPollingProgram(0x2000, 0x3000));
		EmitWord(synccpu, 0x4E75); // RTS
		Write(bus.ChipRam, synccpuAddress, synccpu.ToArray());
		bus.WriteLong((24u + 2u) * 4u, irq2Address);
		bus.WriteWord(0x00DFF09A, (ushort)(0xC000 | AmigaConstants.IntreqPorts));

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(mainAddress, 0x8000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		var lifecycles = new List<Probe10Irq2Lifecycle>(3);
		var irqAccept = -1L;
		var irqEntry = -1L;
		var waitLineExit = -1L;
		var purpleWrite = -1L;
		var greenWrite = -1L;
		var magentaWrite = -1L;
		var firstVerticalWrite = -1L;
		var firstSync1Sample = -1L;
		var reportStart = -1L;
		var reportD0_8192 = -1L;
		var reportD0_4096 = -1L;
		var reportD0_0 = -1L;
		var readIndex = 0;
		var writeIndex = 0;
		var stopCycle = 8L * AmigaConstants.A500PalCpuCyclesPerFrame;

		while (cpu.State.Cycles < stopCycle && lifecycles.Count < 3)
		{
			var acceptCycle = cpu.State.Cycles;
			if (DispatchPendingHardwareInterruptIfNeeded(bus, cpu))
			{
				irqAccept = acceptCycle;
				irqEntry = cpu.State.Cycles;
			}

			if (irqEntry >= 0 && waitLineExit < 0 && cpu.State.ProgramCounter == purpleMovePc)
			{
				waitLineExit = cpu.State.Cycles;
			}

			var instructionPc = cpu.State.ProgramCounter;
			if (instructionPc == irq2Address + 20)
			{
				var d0 = (ushort)cpu.State.D[0];
				if (reportStart < 0)
				{
					reportStart = cpu.State.Cycles;
				}
				if (d0 == 8192 && reportD0_8192 < 0)
				{
					reportD0_8192 = cpu.State.Cycles;
				}
				if (d0 == 4096 && reportD0_4096 < 0)
				{
					reportD0_4096 = cpu.State.Cycles;
				}
				if (d0 == 0 && reportD0_0 < 0)
				{
					reportD0_0 = cpu.State.Cycles;
				}
			}
			cpu.ExecuteInstruction();
			for (; writeIndex < bus.CustomRegisterWrites.Count; writeIndex++)
			{
				var write = bus.CustomRegisterWrites[writeIndex];
				if (purpleWrite < 0 && write.Address == 0x180 && write.Value == 0x0F0F)
				{
					purpleWrite = write.Cycle;
				}
				else if (purpleWrite >= 0 && greenWrite < 0 && write.Address == 0x180 && write.Value == 0x0606)
				{
					greenWrite = write.Cycle;
				}
				else if (greenWrite >= 0 && magentaWrite < 0 && write.Address == 0x180 && write.Value == 0x0A0A)
				{
					magentaWrite = write.Cycle;
				}
				else if (magentaWrite >= 0 && firstVerticalWrite < 0 && write.Address == 0x180 && write.Value == 0x0404)
				{
					firstVerticalWrite = write.Cycle;
				}
			}

			var reads = bus.CustomRegisterReadTrace;
			for (; readIndex < reads.Count; readIndex++)
			{
				var read = reads[readIndex];
				if (purpleWrite >= 0 && firstSync1Sample < 0 && read.SampleCycle > purpleWrite)
				{
					firstSync1Sample = read.SampleCycle;
				}
			}

			if (instructionPc == irq2Address + 28)
			{
				lifecycles.Add(new Probe10Irq2Lifecycle(
					irqAccept,
					irqEntry,
					waitLineExit,
					purpleWrite,
					greenWrite,
					magentaWrite,
					firstVerticalWrite,
					firstSync1Sample,
					reportStart,
					reportD0_8192,
					reportD0_4096,
					reportD0_0,
					cpu.State.Cycles));
				irqAccept = irqEntry = waitLineExit = purpleWrite = greenWrite = magentaWrite = firstVerticalWrite = firstSync1Sample = -1;
				reportStart = reportD0_8192 = reportD0_4096 = reportD0_0 = -1;
			}
		}

		return lifecycles.ToArray();
	}

	private static Probe10RuntimeBatchRun RunProbe10RuntimeBatchedIrq2(bool captureBusAccesses)
	{
		const int mainAddress = 0x1000;
		const int irq2Address = 0x1200;
		const int synccpuAddress = 0x1400;
		var options = MachineOptions
			.ForProfile(MachineProfile.A500Pal512KBoot)
			.WithBusAccessLogging(captureBusAccesses)
			.WithHardwareSpecialization(true);
		using var machine = new Machine(options);
		var bus = machine.Bus;
		bus.CaptureCustomRegisterReadTrace(0x006, 2, 65536);
		StartCopperListAtFrameStart(
			bus,
			(0x1001, 0xFFFE),
			(0x09C, 0x8008),
			(0x4001, 0xFFFE),
			(0x100, 0x2200),
			(0xFFDF, 0xFFFE),
			(0x1001, 0xFFFE),
			(0x02A, 0x8001),
			(0x3885, 0xFFFE),
			(0x09C, 0x8004),
			(0xFFFF, 0xFFFE));
		ConfigureProbe10BitplaneDma(bus);

		Write(bus.ChipRam, mainAddress,
		[
			0x4E, 0x71,
			0x60, 0xFC
		]);
		var irq2 = new List<byte>();
		EmitWord(irq2, 0x337C);
		EmitWord(irq2, AmigaConstants.IntreqPorts);
		EmitWord(irq2, 0x009C);
		EmitWord(irq2, 0x48E7);
		EmitWord(irq2, 0xFFFE);
		EmitWord(irq2, 0x4EB9);
		EmitWord(irq2, (ushort)(synccpuAddress >> 16));
		EmitWord(irq2, (ushort)synccpuAddress);
		EmitWord(irq2, 0x303C);
		EmitWord(irq2, 9500);
		var reportDelayLoop = irq2.Count;
		EmitWord(irq2, 0x51C8);
		EmitBranchDisplacement(irq2, reportDelayLoop);
		EmitWord(irq2, 0x4CDF);
		EmitWord(irq2, 0x7FFF);
		EmitWord(irq2, 0x4E73);
		Write(bus.ChipRam, irq2Address, irq2.ToArray());
		var synccpu = new List<byte>(CreateSynccpuBeamPollingProgram(0x2000, 0x3000));
		EmitWord(synccpu, 0x4E75);
		Write(bus.ChipRam, synccpuAddress, synccpu.ToArray());
		bus.WriteLong((24u + 2u) * 4u, irq2Address);
		bus.WriteWord(0x00DFF09A, (ushort)(0xC000 | AmigaConstants.IntreqPorts));

		machine.Cpu.Reset(mainAddress, 0x8000);
		machine.Cpu.State.StatusRegister = M68kCpuState.Supervisor;
		machine.Cpu.State.A[1] = 0x00DFF000;
		machine.Cpu.State.A[3] = 0x00DFF006;
		var boot = new AmigaBootController(machine);
		var frameStart = 0L;
		for (var frame = 0; frame < 8; frame++)
		{
			var frameStop = bus.GetFrameStopCycle(frameStart);
			boot.ContinueCopperStartRuntimeUntilCycle(frameStop, 100_000);
			bus.AdvanceHardwareTo(frameStop);
			frameStart = frameStop;
		}

		var writes = bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180)
			.Select(write => new Probe10RuntimeColorWrite(write.Value, write.Cycle))
			.ToArray();
		var reads = bus.CustomRegisterReadTrace
			.Select(read => new Probe10RuntimeVhposrRead(
				read.Value,
				read.RequestedCycle,
				read.GrantedCycle,
				read.SampleCycle,
				read.CompletedCycle))
			.ToArray();
		return new Probe10RuntimeBatchRun(
			writes,
			reads,
			machine.Cpu.State.Cycles,
			machine.Cpu.State.ProgramCounter,
			bus.ReadWord(0x00DFF01E));
	}

	private static string BuildProbe10RuntimeBatchDivergence(
		Probe10RuntimeBatchRun expected,
		Probe10RuntimeBatchRun actual)
	{
		var writeCount = Math.Min(expected.ColorWrites.Length, actual.ColorWrites.Length);
		for (var i = 0; i < writeCount; i++)
		{
			if (expected.ColorWrites[i] != actual.ColorWrites[i])
			{
				return $"COLOR00 #{i}: captured={expected.ColorWrites[i]}, fast={actual.ColorWrites[i]}";
			}
		}

		var readCount = Math.Min(expected.VhposrReads.Length, actual.VhposrReads.Length);
		for (var i = 0; i < readCount; i++)
		{
			if (expected.VhposrReads[i] != actual.VhposrReads[i])
			{
				return $"VHPOSR #{i}: captured={expected.VhposrReads[i]}, fast={actual.VhposrReads[i]}";
			}
		}

		return $"captured=w{expected.ColorWrites.Length}/r{expected.VhposrReads.Length}/" +
			$"c{expected.CpuCycle}/pc0x{expected.ProgramCounter:X6}/irq0x{expected.Intreq:X4}, " +
			$"fast=w{actual.ColorWrites.Length}/r{actual.VhposrReads.Length}/" +
			$"c{actual.CpuCycle}/pc0x{actual.ProgramCounter:X6}/irq0x{actual.Intreq:X4}";
	}

	private static (ushort Value, int BeamLine, int BeamHorizontal)[] RunSynccpuBeamPollingMarkers(
		int startOffset,
		int markerCount)
	{
		const int programAddress = 0x1000;
		var program = CreateSynccpuBeamPollingProgram();
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, programAddress, program);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(programAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (239L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + startOffset;
		var stopCycle = cpu.State.Cycles + (24L * AmigaConstants.A500PalCpuCyclesPerRasterLine);

		while (cpu.State.Cycles < stopCycle &&
			bus.CustomRegisterWrites.Count(write => write.Address == 0x180) < markerCount)
		{
			cpu.ExecuteInstruction();
		}

		return bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180)
			.Take(markerCount)
			.Select(write =>
			{
				var beam = bus.GetBeamPosition(write.Cycle);
				return (write.Value, beam.BeamLine, beam.BeamHorizontal);
			})
			.ToArray();
	}

	private static Probe10ModeRun RunProbe10SynccpuMode(
		string name,
		bool programInRom,
		bool captureBusAccesses,
		bool enableHardwareSpecialization,
		bool enableDeferredCpuBusBatch,
		bool enableDiagnosticTrace = false,
		bool stopOnFirstPurpleWrite = true,
		bool configureDisplayForTimeline = false)
	{
		const int chipProgramAddress = 0x1000;
		const int romProgramAddress = 0xFC0000;
		var programAddress = programInRom ? romProgramAddress : chipProgramAddress;
		var bus = new AmigaBus(
			captureBusAccesses: captureBusAccesses,
			enableLiveAgnusDma: true,
			enableHardwareSpecialization: enableHardwareSpecialization,
			enableDeferredCpuBusBatch: enableDeferredCpuBusBatch);
		if (enableDiagnosticTrace)
		{
			bus.CaptureCpuChipRamWriteTrace(0x00070000, 0x10000, 131072);
			bus.CaptureCustomRegisterReadTrace(0x006, 2, 1048576);
		}

		var program = CreateSynccpuBeamPollingProgram();
		if (programInRom)
		{
			bus.MapReadOnlyMemory((uint)programAddress, program);
		}
		else
		{
			Write(bus.ChipRam, programAddress, program);
		}
		if (configureDisplayForTimeline)
		{
			ConfigureLiveOneBitplaneDma(bus);
		}

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset((uint)programAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (239L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + 64;
		var stopCycle = cpu.State.Cycles + (400L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var presentationPixels = Array.Empty<uint>();
		var presentationStopCycle = 0L;
		var presentationActive = false;
		if (configureDisplayForTimeline)
		{
			var beam = bus.GetBeamPosition(cpu.State.Cycles);
			presentationStopCycle = beam.FrameStartCycle + beam.FrameCycles;
			presentationPixels = new uint[bus.Display.Width * bus.Display.Height];
			bus.Display.BeginPresentationFrame(
				new PresentationFrameTarget(presentationPixels),
				beam.FrameStartCycle,
				presentationStopCycle);
			presentationActive = true;
		}

		var instructions = new List<Probe10InstructionLedgerEntry>();
		var boundary = new Probe10InstructionLedgerBoundary(bus, cpu, instructions, stopOnFirstPurpleWrite);
		var batchCpu = Assert.IsAssignableFrom<IM68kBatchCore>(cpu);
		try
		{
			while (cpu.State.Cycles < stopCycle && (!stopOnFirstPurpleWrite || !boundary.ReachedFirstPurpleWrite))
			{
				var executionStop = presentationActive
					? Math.Min(stopCycle, presentationStopCycle)
					: stopCycle;
				var executed = batchCpu.ExecuteInstructions(256, executionStop, boundary);
				if (presentationActive && cpu.State.Cycles >= presentationStopCycle)
				{
					bus.Display.CompletePresentationFrame(presentationStopCycle);
					presentationActive = false;
				}

				if (executed == 0)
				{
					break;
				}
			}

			if (presentationActive)
			{
				bus.Display.CompletePresentationFrame(presentationStopCycle);
				presentationActive = false;
			}
		}
		finally
		{
			if (presentationActive)
			{
				bus.Display.AbortPresentationFrame();
			}
		}

		var writes = bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180)
			.Select(write =>
			{
				var beam = bus.GetBeamPosition(write.Cycle);
				return new Probe10ColorWriteLedgerEntry(write.Value, write.Cycle, beam.BeamLine, beam.BeamHorizontal);
			})
			.ToArray();
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		return new Probe10ModeRun(
			name,
			instructions.ToArray(),
			writes,
			presentationPixels,
			scheduler.DeferredCpuBusBatchAttempts,
			scheduler.DeferredCpuBusBatchUsed,
			scheduler.DeferredCpuBusBatchInstructions);
	}

	private static void AssertProbe10ModeParity(Probe10ModeRun expected, Probe10ModeRun actual)
	{
		var diagnostic = BuildProbe10ModeDivergence(expected, actual);
		Assert.True(expected.Instructions.SequenceEqual(actual.Instructions), diagnostic);
		Assert.True(expected.ColorWrites.SequenceEqual(actual.ColorWrites), diagnostic);
	}

	private static void AssertProbe10CompleteModeParity(Probe10ModeRun expected, Probe10ModeRun actual)
	{
		AssertProbe10ModeParity(expected, actual);

		var diagnostic = BuildProbe10ModeDivergence(expected, actual);
		Assert.True(expected.PresentationPixels.SequenceEqual(actual.PresentationPixels), diagnostic);
	}

	private static string BuildProbe10ModeDivergence(Probe10ModeRun expected, Probe10ModeRun actual)
	{
		var commonInstructions = Math.Min(expected.Instructions.Length, actual.Instructions.Length);
		for (var i = 0; i < commonInstructions; i++)
		{
			if (expected.Instructions[i] != actual.Instructions[i])
			{
				return $"instruction divergence #{i}: expected={expected.Instructions[i]}, actual={actual.Instructions[i]}; " +
					$"expectedRun={FormatProbe10ModeRun(expected)}; actualRun={FormatProbe10ModeRun(actual)}";
			}
		}

		var commonWrites = Math.Min(expected.ColorWrites.Length, actual.ColorWrites.Length);
		for (var i = 0; i < commonWrites; i++)
		{
			if (expected.ColorWrites[i] != actual.ColorWrites[i])
			{
				return $"COLOR00 divergence #{i}: expected={expected.ColorWrites[i]}, actual={actual.ColorWrites[i]}; " +
					$"expectedRun={FormatProbe10ModeRun(expected)}; actualRun={FormatProbe10ModeRun(actual)}";
			}
		}

		return $"ledger length divergence: expected={FormatProbe10ModeRun(expected)}; actual={FormatProbe10ModeRun(actual)}";
	}

	private static string FormatProbe10ModeRun(Probe10ModeRun run)
		=> $"{run.Name}: instructions={run.Instructions.Length}, " +
			$"writes=[{string.Join(",", run.ColorWrites)}], " +
			$"presentationPixels={run.PresentationPixels.Length}, " +
			$"batch={run.DeferredBatchAttempts}/{run.DeferredBatchUsed}/{run.DeferredBatchInstructions}";

	private static int ExtractNamedScore(string entry, string name)
	{
		var prefix = name + "=";
		var start = entry.IndexOf(prefix, StringComparison.Ordinal);
		Assert.True(start >= 0, entry);
		start += prefix.Length;
		var end = entry.IndexOf(',', start);
		if (end < 0)
		{
			end = entry.Length;
		}

		return int.Parse(entry[start..end]);
	}

	private static (ushort Value, int BeamLine, int BeamHorizontal)[] RunJsrSynccpuBeamPollingMarkers(
		byte[] jsrInstruction,
		int startOffset,
		int markerCount)
	{
		const int mainAddress = 0x1000;
		const int synccpuAddress = 0x1100;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, mainAddress, jsrInstruction);
		Write(bus.ChipRam, synccpuAddress, CreateSynccpuBeamPollingProgram());
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(mainAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (239L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + startOffset;
		var stopCycle = cpu.State.Cycles + (24L * AmigaConstants.A500PalCpuCyclesPerRasterLine);

		while (cpu.State.Cycles < stopCycle &&
			bus.CustomRegisterWrites.Count(write => write.Address == 0x180) < markerCount)
		{
			cpu.ExecuteInstruction();
		}

		return bus.CustomRegisterWrites
			.Where(write => write.Address == 0x180)
			.Take(markerCount)
			.Select(write =>
			{
				var beam = bus.GetBeamPosition(write.Cycle);
				return (write.Value, beam.BeamLine, beam.BeamHorizontal);
			})
			.ToArray();
	}

	private static (AmigaBus Bus, long Loop2EntryCycle, uint ProgramCounter, long Cycle, int InterruptDispatches, Cycle01vBoundary[] Boundaries, CustomRegisterWrite[] Writes) RunCycle01vDelayLoopProbe(
		int startLine,
		int startOffset,
		int markerCount,
		bool enableCycleCopper = false,
		bool inlineLoop2 = false,
		bool enableSyntheticVblankIrq = false,
		int syntheticIrqNops = 0,
		bool useCycle01vIrq3Handler = false,
		uint cycle01vIrq3InitialD0 = 0x8001,
		long syntheticVblankIrqOffset = 0,
		bool advanceLiveAgnusForInterruptPolling = true,
		int cycle01vIrq3PaddingNops = 0,
		bool enableHardwareSpecialization = false,
		bool enableDeferredCpuBusBatch = false,
		bool captureBusAccesses = true,
		bool requestSyntheticVblankAfterProbe = false,
		ushort? cycle01vPostRteD3Override = null,
		int synccpuAddress = 0x1100)
	{
		const int mainAddress = 0x1000;
		const int irq3Address = 0x1500;
		var loop2Address = inlineLoop2 ? 0x10A6 : 0x1400;

		var bus = new AmigaBus(
			captureBusAccesses: captureBusAccesses,
			enableLiveAgnusDma: true,
			enableHardwareSpecialization: enableHardwareSpecialization,
			enableDeferredCpuBusBatch: enableDeferredCpuBusBatch);
		if (enableCycleCopper)
		{
			StartCopperListAtFrameStart(bus, CreateCycle01vCopperList());
		}

		Write(
			bus.ChipRam,
			mainAddress,
			inlineLoop2
				? CreateCycle01vInlineDelayLoopMainProgram(synccpuAddress, branchBackToMain: false)
				: CreateCycle01vDelayLoopMainProgram(synccpuAddress, loop2Address));
		var synccpu = new List<byte>(CreateSynccpuBeamPollingProgram());
		EmitWord(synccpu, 0x4E75); // RTS
		Write(bus.ChipRam, synccpuAddress, synccpu.ToArray());
		if (!inlineLoop2)
		{
			Write(bus.ChipRam, loop2Address, CreateCycle01vLoop2Program());
		}
		Cycle01vIrq3HandlerLayout? irq3Handler = null;
		if (enableSyntheticVblankIrq)
		{
			irq3Handler = useCycle01vIrq3Handler
				? CreateCycle01vIrq3Handler(irq3Address, cycle01vIrq3PaddingNops)
				: null;
			var irq3Program = irq3Handler?.Program ?? CreateSyntheticVblankIrq3Handler(syntheticIrqNops);
			Write(bus.ChipRam, irq3Address, irq3Program);
			bus.WriteLong((24u + 3u) * 4u, irq3Address);
			bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqVerticalBlank));
		}

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(mainAddress, 0x3000);
		if (enableSyntheticVblankIrq)
		{
			cpu.State.StatusRegister = requestSyntheticVblankAfterProbe
				? (ushort)(M68kCpuState.Supervisor | 0x0700)
				: M68kCpuState.Supervisor;
		}

		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		if (useCycle01vIrq3Handler)
		{
			cpu.State.D[0] = cycle01vIrq3InitialD0;
		}

		cpu.State.Cycles = (startLine * (long)AmigaConstants.A500PalCpuCyclesPerRasterLine) + startOffset;
		var syntheticVblankRequested = false;
		if (enableSyntheticVblankIrq && !requestSyntheticVblankAfterProbe)
		{
			bus.RequestHardwareInterrupt(
				AmigaConstants.IntreqVerticalBlank,
				bus.GetNextFrameStartCycle(cpu.State.Cycles) + syntheticVblankIrqOffset);
			syntheticVblankRequested = true;
		}

		var stopCycle = cpu.State.Cycles + (2000L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var loop2EntryCycle = -1L;
		var interruptDispatches = 0;
		var boundaryCycles = new Dictionary<string, long>(StringComparer.Ordinal)
		{
			["start"] = cpu.State.Cycles
		};
		var boundaryD3 = new Dictionary<string, ushort>(StringComparer.Ordinal)
		{
			["start"] = (ushort)cpu.State.D[3]
		};

		while (cpu.State.Cycles < stopCycle &&
			(loop2EntryCycle < 0 ||
				bus.CustomRegisterWrites.Count(write => write.Address == 0x180 && write.Cycle >= loop2EntryCycle) < markerCount))
		{
			var interruptAcceptCycle = cpu.State.Cycles;
			var interruptAcceptD3 = (ushort)cpu.State.D[3];
			if (DispatchPendingHardwareInterruptIfNeeded(bus, cpu, advanceLiveAgnusForInterruptPolling))
			{
				interruptDispatches++;
				if (irq3Handler != null)
				{
					boundaryCycles.TryAdd("irqAccept", interruptAcceptCycle);
					boundaryD3.TryAdd("irqAccept", interruptAcceptD3);
					boundaryCycles.TryAdd("irqEntry", cpu.State.Cycles);
					boundaryD3.TryAdd("irqEntry", (ushort)cpu.State.D[3]);
				}
			}

			RecordCycle01vDelayLoopBoundary(cpu, boundaryCycles, boundaryD3);
			if (enableSyntheticVblankIrq &&
				requestSyntheticVblankAfterProbe &&
				!syntheticVblankRequested &&
				boundaryCycles.ContainsKey("afterVposrRead"))
			{
				bus.WriteWord(
					0x00DFF09C,
					AmigaConstants.IntreqVerticalBlank,
					cpu.State.Cycles);
				bus.RequestHardwareInterrupt(
					AmigaConstants.IntreqVerticalBlank,
					bus.GetNextFrameStartCycle(cpu.State.Cycles) + syntheticVblankIrqOffset);
				cpu.State.StatusRegister = M68kCpuState.Supervisor;
				syntheticVblankRequested = true;
			}
			if (irq3Handler != null)
			{
				RecordCycle01vIrq3Boundary(cpu, irq3Handler, boundaryCycles, boundaryD3);
			}

			var instructionPc = cpu.State.ProgramCounter;
			if (loop2EntryCycle < 0 && cpu.State.ProgramCounter == loop2Address)
			{
				loop2EntryCycle = cpu.State.Cycles;
				boundaryCycles.TryAdd("loop2Entry", loop2EntryCycle);
				boundaryD3.TryAdd("loop2Entry", (ushort)cpu.State.D[3]);
			}

			cpu.ExecuteInstruction();
			if (irq3Handler != null && instructionPc == irq3Handler.RtePc)
			{
				if (cycle01vPostRteD3Override.HasValue)
				{
					cpu.State.D[3] = (cpu.State.D[3] & 0xFFFF_0000) | cycle01vPostRteD3Override.Value;
				}

				boundaryCycles.TryAdd("irqRteComplete", cpu.State.Cycles);
				boundaryD3.TryAdd("irqRteComplete", (ushort)cpu.State.D[3]);
			}
		}
		RecordCycle01vDelayLoopBoundary(cpu, boundaryCycles, boundaryD3);

		return (
			bus,
			loop2EntryCycle,
			cpu.State.ProgramCounter,
			cpu.State.Cycles,
			interruptDispatches,
			boundaryCycles
				.Select(pair => new Cycle01vBoundary(pair.Key, pair.Value, boundaryD3.GetValueOrDefault(pair.Key)))
				.ToArray(),
			bus.CustomRegisterWrites
				.Where(write => write.Address == 0x180)
				.ToArray());
	}

	private static bool DispatchPendingHardwareInterruptIfNeeded(AmigaBus bus, IM68kCore cpu, bool advanceLiveAgnus = true)
	{
		bus.AdvanceDmaTo(cpu.State.Cycles, advanceLiveAgnus);
		var level = bus.Paula.GetHighestCpuVisibleInterruptLevel(cpu.State.Cycles);
		if (level > 0)
		{
			var interruptMask = (cpu.State.StatusRegister >> 8) & 0x07;
			if (level <= interruptMask)
			{
				return false;
			}

			cpu.RequestInterrupt(level, (uint)((24 + level) * 4));
			return true;
		}

		return false;
	}

	private static (AmigaBus Bus, long Loop2EntryCycle, uint ProgramCounter, long Cycle, int InterruptDispatches, Cycle01vBoundary[] Boundaries, CustomRegisterWrite[] Writes) RunCycle01vDelayLoopProbe(
		int startOffset,
		int markerCount,
		bool enableCycleCopper = false,
		bool inlineLoop2 = false)
		=> RunCycle01vDelayLoopProbe(
			startLine: 239,
			startOffset,
			markerCount,
			enableCycleCopper,
			inlineLoop2);

	private static (AmigaBus Bus, CustomRegisterWrite[] BurstWrites) RunCycle01vInlineMainLoopProbe(
		int startOffset,
		int burstCount)
	{
		const int mainAddress = 0x1000;
		const int synccpuAddress = 0x1100;
		const int loop2Address = 0x10A6;

		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, mainAddress, CreateCycle01vInlineDelayLoopMainProgram(synccpuAddress, branchBackToMain: true));
		var synccpu = new List<byte>(CreateSynccpuBeamPollingProgram());
		EmitWord(synccpu, 0x4E75); // RTS
		Write(bus.ChipRam, synccpuAddress, synccpu.ToArray());

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(mainAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (239L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + startOffset;
		var stopCycle = cpu.State.Cycles + (4000L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var burstWrites = new List<CustomRegisterWrite>(burstCount);
		var pendingBurstWriteIndex = -1;
		var armedForBurst = true;

		while (cpu.State.Cycles < stopCycle && burstWrites.Count < burstCount)
		{
			if (!armedForBurst && cpu.State.ProgramCounter == mainAddress)
			{
				armedForBurst = true;
			}

			if (armedForBurst &&
				pendingBurstWriteIndex < 0 &&
				cpu.State.ProgramCounter == loop2Address &&
				(ushort)cpu.State.D[3] == 300)
			{
				pendingBurstWriteIndex = bus.CustomRegisterWrites.Count;
				armedForBurst = false;
			}

			cpu.ExecuteInstruction();
			if (pendingBurstWriteIndex >= 0)
			{
				var burstWrite = bus.CustomRegisterWrites
					.Skip(pendingBurstWriteIndex)
					.FirstOrDefault(write => write.Address == 0x180 && write.Value == 0x0F0F);
				if (burstWrite.Address == 0x180)
				{
					burstWrites.Add(burstWrite);
					pendingBurstWriteIndex = -1;
				}
			}
		}

		return (bus, burstWrites.ToArray());
	}

	private static CycleVposrProbeSample RunCycleVposrProbeSample(int probeNops)
	{
		const int mainAddress = 0x1000;
		const int synccpuAddress = 0x1100;
		var afterProbeReadPc = (uint)(mainAddress + 6 + (probeNops * 2) + 4);
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, mainAddress, CreateCycleVposrProbeMainProgram(synccpuAddress, probeNops));
		var synccpu = new List<byte>(CreateSynccpuBeamPollingProgram());
		EmitWord(synccpu, 0x4E75); // RTS
		Write(bus.ChipRam, synccpuAddress, synccpu.ToArray());

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(mainAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (239L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + 56;
		var startCycle = cpu.State.Cycles;
		var stopCycle = cpu.State.Cycles + (2000L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var afterSynccpuCycle = -1L;
		var beforeProbeReadCycle = -1L;

		while (cpu.State.Cycles < stopCycle && cpu.State.ProgramCounter != afterProbeReadPc)
		{
			if (cpu.State.ProgramCounter == mainAddress + 6 && afterSynccpuCycle < 0)
			{
				afterSynccpuCycle = cpu.State.Cycles;
			}

			if (cpu.State.ProgramCounter == afterProbeReadPc - 4 && beforeProbeReadCycle < 0)
			{
				beforeProbeReadCycle = cpu.State.Cycles;
			}

			cpu.ExecuteInstruction();
		}

		Assert.Equal(afterProbeReadPc, cpu.State.ProgramCounter);
		var read = bus.CustomRegisterReads.Last(customRead =>
			customRead.Address == 0x004 &&
			customRead.Kind == AmigaBusAccessKind.CpuDataRead);
		var sampleBeam = bus.GetBeamPosition(read.SampleCycle);
		var requestBeam = bus.GetBeamPosition(read.RequestedCycle);
		var grantBeam = bus.GetBeamPosition(read.GrantedCycle);
		return new CycleVposrProbeSample(
			probeNops,
			startCycle,
			afterSynccpuCycle,
			beforeProbeReadCycle,
			read,
			requestBeam,
			grantBeam,
			sampleBeam,
			cpu.State.D[0],
			cpu.State.Cycles);
	}

	private static (AmigaBus Bus, CustomRegisterRead[] Reads, int InterruptDispatches, AmigaCpuBusPhaseTrace[] FirstInterruptPhases) RunRepeatedCycleVposrProbeSamples(
		int probeNops,
		int sampleCount,
		bool enableCycleCopper = false,
		bool enableVblankIrq = false,
		bool advanceDmaPerInstruction = false)
	{
		const int mainAddress = 0x1000;
		const int synccpuAddress = 0x1100;
		const int irq3Address = 0x1500;
		var afterProbeReadPc = (uint)(mainAddress + 6 + (probeNops * 2) + 4);
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		if (enableCycleCopper)
		{
			StartCopperListAtFrameStart(bus, CreateCycle01vCopperList());
		}

		Write(
			bus.ChipRam,
			mainAddress,
			CreateCycle01vInlineDelayLoopMainProgram(
				synccpuAddress,
				branchBackToMain: true,
				probeNops));
		var synccpu = new List<byte>(CreateSynccpuBeamPollingProgram());
		EmitWord(synccpu, 0x4E75); // RTS
		Write(bus.ChipRam, synccpuAddress, synccpu.ToArray());
		if (enableVblankIrq)
		{
			var handler = CreateCycle01vIrq3Handler(irq3Address);
			Write(bus.ChipRam, irq3Address, handler.Program);
			bus.WriteLong((24u + 3u) * 4u, irq3Address);
			bus.WriteWord(0x00DFF09A, (ushort)(0x8000 | 0x4000 | AmigaConstants.IntreqVerticalBlank));
		}

		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(mainAddress, 0x3000);
		if (enableVblankIrq)
		{
			cpu.State.StatusRegister = M68kCpuState.Supervisor;
		}

		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (239L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + 56;
		var stopCycle = cpu.State.Cycles + (4000L * AmigaConstants.A500PalCpuCyclesPerRasterLine);
		var reads = new List<CustomRegisterRead>(sampleCount);
		var interruptDispatches = 0;
		var firstInterruptPhases = Array.Empty<AmigaCpuBusPhaseTrace>();

		while (cpu.State.Cycles < stopCycle && reads.Count < sampleCount)
		{
			var interruptStartCycle = cpu.State.Cycles;
			if (enableVblankIrq && DispatchPendingHardwareInterruptIfNeeded(bus, cpu))
			{
				interruptDispatches++;
				if (firstInterruptPhases.Length == 0)
				{
					firstInterruptPhases = bus.CpuBusPhases.Where(phase =>
						phase.CpuPhase.RequestedCycle >= interruptStartCycle &&
						phase.CpuPhase.RequestedCycle <= cpu.State.Cycles).ToArray();
				}
			}
			else if (advanceDmaPerInstruction)
			{
				bus.AdvanceDmaTo(cpu.State.Cycles);
			}

			cpu.ExecuteInstruction();
			if (cpu.State.ProgramCounter == afterProbeReadPc)
			{
				reads.Add(bus.CustomRegisterReads.Last(read =>
					read.Address == 0x004 &&
					read.Kind == AmigaBusAccessKind.CpuDataRead));
			}
		}

		return (bus, reads.ToArray(), interruptDispatches, firstInterruptPhases);
	}

	private static string FormatRepeatedCycleVposrProbeSamples(
		string name,
		(AmigaBus Bus, CustomRegisterRead[] Reads, int InterruptDispatches, AmigaCpuBusPhaseTrace[] FirstInterruptPhases) result)
		=> $"{name}/irq={result.InterruptDispatches}:" + string.Join(
			",",
			result.Reads.Select(read =>
			{
				var sample = result.Bus.GetBeamPosition(read.SampleCycle);
				return $"0x{read.Value:X4}@{read.SampleCycle}/v{sample.BeamLine}h{sample.BeamHorizontal}";
			}));

	private static byte[] CreateCycleVposrProbeMainProgram(int synccpuAddress, int probeNops)
	{
		var program = new List<byte>(16 + (probeNops * 2));
		EmitWord(program, 0x4EB9); // JSR synccpu
		EmitWord(program, (ushort)(synccpuAddress >> 16));
		EmitWord(program, (ushort)synccpuAddress);
		for (var i = 0; i < probeNops; i++)
		{
			EmitWord(program, 0x4E71);
		}

		EmitWord(program, 0x3029); // MOVE.W VPOSR(A1),D0
		EmitWord(program, 0x0004);
		EmitWord(program, 0x4E75); // RTS, not reached by the probe
		return program.ToArray();
	}

	private static void RecordCycle01vDelayLoopBoundary(
		IM68kCore cpu,
		IDictionary<string, long> boundaries,
		IDictionary<string, ushort>? boundaryD3 = null)
	{
		var name = cpu.State.ProgramCounter switch
		{
			0x1006 => "afterSynccpu",
			0x108E => "afterProbeNops",
			0x1092 => "afterVposrRead",
			0x1096 => "delayLoopStart",
			0x109A => "afterDelayLoop",
			0x109E => "afterMove300",
			0x10A2 => "afterMoveF0f",
			0x10A6 => "afterMoveZero",
			0x1400 => "loop2Entry",
			_ => null
		};
		if (name != null && boundaries.TryAdd(name, cpu.State.Cycles))
		{
			boundaryD3?.TryAdd(name, (ushort)cpu.State.D[3]);
		}
	}

	private static byte[] CreateCycle01vDelayLoopMainProgram(int synccpuAddress, int loop2Address)
	{
		var program = new List<byte>(170);
		EmitWord(program, 0x4EB9); // JSR synccpu
		EmitWord(program, (ushort)(synccpuAddress >> 16));
		EmitWord(program, (ushort)synccpuAddress);
		for (var i = 0; i < 68; i++)
		{
			EmitWord(program, 0x4E71);
		}

		EmitWord(program, 0x3029); // MOVE.W VPOSR(A1),D0
		EmitWord(program, 0x0004);
		EmitWord(program, 0x363C); // MOVE.W #12000,D3
		EmitWord(program, 0x2EE0);
		var delayLoop = program.Count;
		EmitWord(program, 0x51CB); // DBRA D3,.loop1
		EmitBranchDisplacement(program, delayLoop);
		EmitWord(program, 0x363C); // MOVE.W #300,D3
		EmitWord(program, 0x012C);
		EmitWord(program, 0x383C); // MOVE.W #$0F0F,D4
		EmitWord(program, 0x0F0F);
		EmitWord(program, 0x3A3C); // MOVE.W #$0000,D5
		EmitWord(program, 0x0000);
		EmitWord(program, 0x4EF9); // JMP loop2
		EmitWord(program, (ushort)(loop2Address >> 16));
		EmitWord(program, (ushort)loop2Address);
		return program.ToArray();
	}

	private static byte[] CreateCycle01vInlineDelayLoopMainProgram(
		int synccpuAddress,
		bool branchBackToMain,
		int probeNops = 68)
	{
		var program = new List<byte>(180);
		EmitWord(program, 0x4EB9); // JSR synccpu
		EmitWord(program, (ushort)(synccpuAddress >> 16));
		EmitWord(program, (ushort)synccpuAddress);
		for (var i = 0; i < probeNops; i++)
		{
			EmitWord(program, 0x4E71);
		}

		EmitWord(program, 0x3029); // MOVE.W VPOSR(A1),D0
		EmitWord(program, 0x0004);
		EmitWord(program, 0x363C); // MOVE.W #12000,D3
		EmitWord(program, 0x2EE0);
		var delayLoop = program.Count;
		EmitWord(program, 0x51CB); // DBRA D3,.loop1
		EmitBranchDisplacement(program, delayLoop);
		EmitWord(program, 0x363C); // MOVE.W #300,D3
		EmitWord(program, 0x012C);
		EmitWord(program, 0x383C); // MOVE.W #$0F0F,D4
		EmitWord(program, 0x0F0F);
		EmitWord(program, 0x3A3C); // MOVE.W #$0000,D5
		EmitWord(program, 0x0000);
		var loop2 = program.Count;
		EmitWord(program, 0x3344); // MOVE.W D4,COLOR00(A1)
		EmitWord(program, 0x0180);
		EmitWord(program, 0x3345); // MOVE.W D5,COLOR00(A1)
		EmitWord(program, 0x0180);
		EmitWord(program, 0x51CB); // DBRA D3,.loop2
		EmitBranchDisplacement(program, loop2);
		if (branchBackToMain)
		{
			EmitWord(program, 0x6000); // BRA.W mainloop
			EmitBranchDisplacement(program, 0);
		}
		else
		{
			EmitWord(program, 0x4E75);
		}

		return program.ToArray();
	}

	private static byte[] CreateCycle01vLoop2Program()
	{
		var program = new List<byte>(12);
		var loop = program.Count;
		EmitWord(program, 0x3344); // MOVE.W D4,COLOR00(A1)
		EmitWord(program, 0x0180);
		EmitWord(program, 0x3345); // MOVE.W D5,COLOR00(A1)
		EmitWord(program, 0x0180);
		EmitWord(program, 0x51CB); // DBRA D3,.loop2
		EmitBranchDisplacement(program, loop);
		EmitWord(program, 0x4E75);
		return program.ToArray();
	}

	private static byte[] CreateSyntheticVblankIrq3Handler(int nopCount)
	{
		var program = new List<byte>(8 + (nopCount * 2));
		EmitWord(program, 0x337C); // MOVE.W #$0020,INTREQ(A1)
		EmitWord(program, AmigaConstants.IntreqVerticalBlank);
		EmitWord(program, 0x009C);
		for (var i = 0; i < nopCount; i++)
		{
			EmitWord(program, 0x4E71);
		}

		EmitWord(program, 0x4E73); // RTE
		return program.ToArray();
	}

	private static Cycle01vIrq3HandlerLayout CreateCycle01vIrq3Handler(int handlerAddress, int paddingNops = 0)
	{
		var program = new List<byte>(256);
		var leaPatches = new List<(int ExtensionOffset, int Bit)>(16);
		var movemSaveOffset = program.Count;
		EmitWord(program, 0x48E7); // MOVEM.L D0-A6,-(SP)
		EmitWord(program, 0xFFFE);
		var ackOffset = program.Count;
		EmitWord(program, 0x337C); // MOVE.W #$0020,INTREQ(A1)
		EmitWord(program, AmigaConstants.IntreqVerticalBlank);
		EmitWord(program, 0x009C);
		var moveD5Offset = program.Count;
		EmitWord(program, 0x3A3C); // MOVE.W #$0CCC,D5
		EmitWord(program, 0x0CCC);
		var firstTestOffset = program.Count;

		for (var bit = 0; bit < 16; bit++)
		{
			EmitWord(program, 0x41FA); // LEA (bitN+2)(PC),A0
			var extensionOffset = program.Count;
			EmitWord(program, 0);
			leaPatches.Add((extensionOffset, bit));
			EmitWord(program, 0x30BC); // MOVE.W #$0333,(A0)
			EmitWord(program, 0x0333);
			EmitWord(program, 0x0800); // BTST #bit,D0
			EmitWord(program, (ushort)bit);
			var branchOffset = program.Count;
			EmitWord(program, 0x6700); // BEQ.S next
			EmitWord(program, 0x3085); // MOVE.W D5,(A0)
			PatchShortBranch(program, branchOffset, program.Count);
		}

		for (var i = 0; i < paddingNops; i++)
		{
			EmitWord(program, 0x4E71);
		}

		var restoreOffset = program.Count;
		EmitWord(program, 0x4CDF); // MOVEM.L (SP)+,D0-A6
		EmitWord(program, 0x7FFF);
		var rteOffset = program.Count;
		EmitWord(program, 0x4E73); // RTE
		var dataOffset = program.Count;
		for (var bit = 0; bit < 16; bit++)
		{
			EmitWord(program, 0x0180);
			EmitWord(program, 0x0000);
		}

		foreach (var patch in leaPatches)
		{
			var targetOffset = dataOffset + (patch.Bit * 4) + 2;
			var displacement = (handlerAddress + targetOffset) - (handlerAddress + patch.ExtensionOffset);
			var displacementWord = unchecked((ushort)displacement);
			program[patch.ExtensionOffset] = (byte)(displacementWord >> 8);
			program[patch.ExtensionOffset + 1] = (byte)displacementWord;
		}

		return new Cycle01vIrq3HandlerLayout(
			program.ToArray(),
			(uint)(handlerAddress + movemSaveOffset),
			(uint)(handlerAddress + ackOffset),
			(uint)(handlerAddress + moveD5Offset),
			(uint)(handlerAddress + firstTestOffset),
			(uint)(handlerAddress + restoreOffset),
			(uint)(handlerAddress + rteOffset),
			(uint)(handlerAddress + dataOffset));
	}

	private static Cycle01vIrq3HandlerPatchProbeResult RunCycle01vIrq3HandlerPatchProbe(uint d0)
	{
		const int handlerAddress = 0x1500;
		var handler = CreateCycle01vIrq3Handler(handlerAddress);
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, handlerAddress, handler.Program);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(handlerAddress, 0x3F00);
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.D[0] = d0;

		for (var i = 0; i < 512 && cpu.State.ProgramCounter != handler.RtePc; i++)
		{
			cpu.ExecuteInstruction();
		}

		var dataOffset = checked((int)handler.DataPc);
		var bit0Offset = dataOffset + 2;
		var bit15Offset = dataOffset + (15 * 4) + 2;
		var values = Enumerable.Range(0, 16)
			.Select(bit => BigEndian.ReadUInt16(bus.ChipRam, dataOffset + (bit * 4) + 2, $"bit{bit} value"))
			.ToArray();
		return new Cycle01vIrq3HandlerPatchProbeResult(
			BigEndian.ReadUInt16(bus.ChipRam, dataOffset, "bit0 register"),
			BigEndian.ReadUInt16(bus.ChipRam, bit0Offset, "bit0 value"),
			BigEndian.ReadUInt16(bus.ChipRam, dataOffset + (15 * 4), "bit15 register"),
			BigEndian.ReadUInt16(bus.ChipRam, bit15Offset, "bit15 value"),
			handler.RtePc,
			cpu.State.ProgramCounter,
			values);
	}

	private static void RecordCycle01vIrq3Boundary(
		IM68kCore cpu,
		Cycle01vIrq3HandlerLayout handler,
		IDictionary<string, long> boundaries,
		IDictionary<string, ushort>? boundaryD3 = null)
	{
		var name = cpu.State.ProgramCounter switch
		{
			var pc when pc == handler.MovemSavePc => "movemSave",
			var pc when pc == handler.AckPc => "ack",
			var pc when pc == handler.MoveD5Pc => "moveD5",
			var pc when pc == handler.FirstTestPc => "firstTest",
			var pc when pc == handler.RestorePc => "restore",
			var pc when pc == handler.RtePc => "rte",
			_ => null
		};
		if (name != null && boundaries.TryAdd(name, cpu.State.Cycles))
		{
			boundaryD3?.TryAdd(name, (ushort)cpu.State.D[3]);
		}
	}

	private static void PatchShortBranch(List<byte> program, int branchOffset, int targetOffset)
	{
		var displacement = targetOffset - (branchOffset + 2);
		if (displacement < sbyte.MinValue || displacement > sbyte.MaxValue)
		{
			throw new InvalidOperationException($"Short branch displacement out of range: {displacement}.");
		}

		program[branchOffset + 1] = unchecked((byte)(sbyte)displacement);
	}

	private sealed class Probe10InstructionLedgerBoundary : IM68kInstructionBoundary
	{
		private readonly AmigaBus _bus;
		private readonly IM68kCore _cpu;
		private readonly List<Probe10InstructionLedgerEntry> _instructions;
		private readonly bool _stopOnFirstPurpleWrite;
		private uint _instructionProgramCounter;

		public Probe10InstructionLedgerBoundary(
			AmigaBus bus,
			IM68kCore cpu,
			List<Probe10InstructionLedgerEntry> instructions,
			bool stopOnFirstPurpleWrite = true)
		{
			_bus = bus;
			_cpu = cpu;
			_instructions = instructions;
			_stopOnFirstPurpleWrite = stopOnFirstPurpleWrite;
		}

		public bool ReachedFirstPurpleWrite => _bus.CustomRegisterWrites.Any(write =>
			write.Address == 0x180 && write.Value == 0x0A0A);

		public bool BeforeInstruction()
		{
			if (_stopOnFirstPurpleWrite && ReachedFirstPurpleWrite)
			{
				return false;
			}

			_instructionProgramCounter = _cpu.State.ProgramCounter;
			return true;
		}

		public void AfterInstruction(long previousCycle, long currentCycle)
			=> _instructions.Add(new Probe10InstructionLedgerEntry(
				_instructionProgramCounter,
				_cpu.State.LastOpcode,
				previousCycle,
				currentCycle));
	}

	private sealed record Probe10ModeRun(
		string Name,
		Probe10InstructionLedgerEntry[] Instructions,
		Probe10ColorWriteLedgerEntry[] ColorWrites,
		uint[] PresentationPixels,
		long DeferredBatchAttempts,
		long DeferredBatchUsed,
		long DeferredBatchInstructions);

	private readonly record struct Probe10InstructionLedgerEntry(
		uint ProgramCounter,
		ushort Opcode,
		long StartCycle,
		long RetireCycle);

	private readonly record struct Probe10ColorWriteLedgerEntry(
		ushort Value,
		long Cycle,
		int BeamLine,
		int BeamHorizontal);

	private readonly record struct Probe10Irq2Lifecycle(
		long IrqAcceptCycle,
		long IrqEntryCycle,
		long WaitLineExitCycle,
		long PurpleWriteCycle,
		long GreenWriteCycle,
		long MagentaWriteCycle,
		long FirstVerticalWriteCycle,
		long FirstSync1SampleCycle,
		long ReportStartCycle,
		long ReportD0_8192Cycle,
		long ReportD0_4096Cycle,
		long ReportD0_0Cycle,
		long RteCompleteCycle);

	private sealed record Probe10RuntimeBatchRun(
		Probe10RuntimeColorWrite[] ColorWrites,
		Probe10RuntimeVhposrRead[] VhposrReads,
		long CpuCycle,
		uint ProgramCounter,
		ushort Intreq);

	private readonly record struct Probe10RuntimeColorWrite(ushort Value, long Cycle);

	private readonly record struct Probe10RuntimeVhposrRead(
		ushort Value,
		long RequestedCycle,
		long GrantedCycle,
		long SampleCycle,
		long CompletedCycle);

	private sealed record Probe10Sync1ExitRun(
		AmigaBus Bus,
		long StartCycle,
		CustomRegisterRead FinalRead,
		CustomRegisterWrite ColorWrite);

	private sealed record Cycle01vIrq3HandlerLayout(
		byte[] Program,
		uint MovemSavePc,
		uint AckPc,
		uint MoveD5Pc,
		uint FirstTestPc,
		uint RestorePc,
		uint RtePc,
		uint DataPc);

	private sealed record Cycle01vIrq3HandlerPatchProbeResult(
		ushort Bit0Register,
		ushort Bit0Value,
		ushort Bit15Register,
		ushort Bit15Value,
		uint RtePc,
		uint EndPc,
		ushort[] Values);

	private static (ushort First, ushort Second)[] CreateCycle01vCopperList()
	{
		var instructions = new List<(ushort First, ushort Second)>();
		for (var line = 0x40; line <= 0xC0; line += 0x08)
		{
			instructions.Add(((ushort)((line << 8) | 0x01), 0xFFFE));
			instructions.Add((0x0180, 0x0F00));
			instructions.Add(((ushort)((line << 8) | 0xD9), 0xFFFE));
			instructions.Add((0x0180, 0x0000));
		}

		instructions.Add((0xFFFF, 0xFFFE));
		return instructions.ToArray();
	}

	private static string BuildColorWriteBeamDiagnostic(AmigaBus bus, IReadOnlyList<CustomRegisterWrite> writes)
		=> string.Join(
			"; ",
			writes
				.Where(write => write.Address == 0x180)
				.Take(16)
				.Select(write =>
				{
					var beam = bus.GetBeamPosition(write.Cycle);
					return $"0x{write.Value:X4}@v{beam.BeamLine}h{beam.BeamHorizontal}/c{write.Cycle}";
				}));

	private static string BuildCycle01vBoundaryDiagnostic(AmigaBus bus, IReadOnlyList<Cycle01vBoundary> boundaries)
	{
		var origin = boundaries.First(boundary => boundary.Name == "start").Cycle;
		return string.Join(
			"; ",
			boundaries.Select(boundary =>
			{
				var beam = bus.GetBeamPosition(boundary.Cycle);
				return $"{boundary.Name}=+{boundary.Cycle - origin}/v{beam.BeamLine}h{beam.BeamHorizontal}";
			}));
	}

	private static long[] GetCycle01vBoundaryCycles(
		IReadOnlyList<Cycle01vBoundary> boundaries,
		IReadOnlyList<string> names)
	{
		var map = boundaries.ToDictionary(boundary => boundary.Name, boundary => boundary.Cycle, StringComparer.Ordinal);
		return names.Select(name => map.TryGetValue(name, out var cycle) ? cycle : -1L).ToArray();
	}

	private static string FormatCycle01vBoundaryCycles(
		IReadOnlyList<Cycle01vBoundary> boundaries,
		IReadOnlyList<string> names)
		=> string.Join(
			",",
			names.Zip(GetCycle01vBoundaryCycles(boundaries, names), (name, cycle) => $"{name}={cycle}"));

	private static string BuildCycle01vAccessDiagnostic(AmigaBus bus, IReadOnlyList<Cycle01vBoundary> boundaries)
	{
		var start = boundaries.First(boundary => boundary.Name == "start").Cycle;
		var end = boundaries.FirstOrDefault(boundary => boundary.Name == "loop2Entry")?.Cycle ?? long.MaxValue;
		var window = bus.BusAccesses
			.Where(access => access.Request.RequestedCycle >= start && access.Request.RequestedCycle <= end)
			.ToArray();
		var copper = window
			.Where(access => access.Request.Requester == AmigaBusRequester.Copper)
			.ToArray();
		var cpuWait = window
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu && access.WaitCycles > 0)
			.ToArray();
		var firstCopper = copper.FirstOrDefault();
		var firstCpuWait = cpuWait.FirstOrDefault();
		return
			$"all={window.Length},copper={copper.Length},cpuWait={cpuWait.Length}," +
			$"firstCopper={FormatAccess(bus, firstCopper)}," +
			$"firstCpuWait={FormatAccess(bus, firstCpuWait)}";
	}

	private static string BuildCycle01vProbeSummary(
		(AmigaBus Bus, long Loop2EntryCycle, uint ProgramCounter, long Cycle, int InterruptDispatches, Cycle01vBoundary[] Boundaries, CustomRegisterWrite[] Writes) result)
	{
		var firstLoopWrite = result.Writes.First(write => write.Cycle >= result.Loop2EntryCycle && write.Value == 0x0F0F);
		var beam = result.Bus.GetBeamPosition(firstLoopWrite.Cycle);
		var start = result.Boundaries.First(boundary => boundary.Name == "start").Cycle;
		var end = result.Boundaries.First(boundary => boundary.Name == "loop2Entry").Cycle;
		var window = result.Bus.BusAccesses
			.Where(access => access.Request.RequestedCycle >= start && access.Request.RequestedCycle <= end)
			.ToArray();
		var copperCount = window.Count(access => access.Request.Requester == AmigaBusRequester.Copper);
		var cpuWaitCount = window.Count(access => access.Request.Requester == AmigaBusRequester.Cpu && access.WaitCycles > 0);
		var cpuWaitCycles = window
			.Where(access => access.Request.Requester == AmigaBusRequester.Cpu)
			.Sum(access => access.WaitCycles);
		return $"v{beam.BeamLine}h{beam.BeamHorizontal}/irq{result.InterruptDispatches}/cu{copperCount}/cw{cpuWaitCount}/ws{cpuWaitCycles}";
	}

	private static string BuildCycleVposrProbeSampleDiagnostic(params CycleVposrProbeSample[] samples)
		=> string.Join(
			" | ",
			samples.Select(sample =>
				$"nops={sample.ProbeNops},value=0x{sample.Read.Value:X4},d0=0x{sample.D0:X4}," +
				$"bit0={sample.Read.Value & 1},start=+0," +
				$"afterSynccpu=+{sample.AfterSynccpuCycle - sample.StartCycle}," +
				$"beforeRead=+{sample.BeforeProbeReadCycle - sample.StartCycle}," +
				$"read=req+{sample.Read.RequestedCycle - sample.StartCycle}/v{sample.RequestBeam.BeamLine}h{sample.RequestBeam.BeamHorizontal}," +
				$"grant+{sample.Read.GrantedCycle - sample.StartCycle}/v{sample.GrantBeam.BeamLine}h{sample.GrantBeam.BeamHorizontal}," +
				$"sample+{sample.Read.SampleCycle - sample.StartCycle}/v{sample.SampleBeam.BeamLine}h{sample.SampleBeam.BeamHorizontal}," +
				$"done+{sample.Read.CompletedCycle - sample.StartCycle}," +
				$"cpu+{sample.CompletedCycle - sample.StartCycle}"));

	private static string FormatAccess(AmigaBus bus, AmigaBusAccessResult access)
	{
		if (access.Request.RequestedCycle == 0 &&
			access.GrantedCycle == 0 &&
			access.CompletedCycle == 0)
		{
			return "none";
		}

		var beam = bus.GetBeamPosition(access.GrantedCycle);
		return
			$"{access.Request.Requester}/{access.Request.Kind}/0x{access.Request.Address:X6}" +
			$"@r{access.Request.RequestedCycle}/g{access.GrantedCycle}/w{access.WaitCycles}/v{beam.BeamLine}h{beam.BeamHorizontal}";
	}

	private sealed record Cycle01vBoundary(string Name, long Cycle, ushort D3 = 0);

	private sealed record CycleVposrProbeSample(
		int ProbeNops,
		long StartCycle,
		long AfterSynccpuCycle,
		long BeforeProbeReadCycle,
		CustomRegisterRead Read,
		AgnusBeamPosition RequestBeam,
		AgnusBeamPosition GrantBeam,
		AgnusBeamPosition SampleBeam,
		uint D0,
		long CompletedCycle);

	private static (AmigaBus Bus, IM68kCore Cpu, CustomRegisterRead[] Reads, CustomRegisterWrite[] ColorWrites)
		RunWaitLineLoopProbe(
			byte[] program,
			int startLine,
			int startOffset,
			bool stopOnColorWrite,
			int maxInstructions = 256)
	{
		const int programAddress = 0x1000;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, programAddress, program);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(programAddress, 0x3000);
		cpu.State.A[1] = 0x00DFF000;
		cpu.State.A[3] = 0x00DFF006;
		cpu.State.Cycles = (startLine * (long)AmigaConstants.A500PalCpuCyclesPerRasterLine) + startOffset;
		var stopCycle = cpu.State.Cycles + (4L * AmigaConstants.A500PalCpuCyclesPerRasterLine);

		for (var i = 0; i < maxInstructions && cpu.State.Cycles < stopCycle; i++)
		{
			cpu.ExecuteInstruction();
			if (stopOnColorWrite && bus.CustomRegisterWrites.Any(write => write.Address == 0x180))
			{
				break;
			}
		}

		return (
			bus,
			cpu,
			bus.CustomRegisterReads
				.Where(read => read.Address is 0x004 or 0x006)
				.ToArray(),
			bus.CustomRegisterWrites
				.Where(write => write.Address == 0x180)
				.ToArray());
	}

	private static byte[] CreateFirstBneNotTakenProbeProgram()
	{
		var program = new List<byte>(16);
		var loop = program.Count;
		EmitWord(program, 0x3413); // MOVE.W (A3),D2
		EmitWord(program, 0x0242); // ANDI.W #$FF00,D2
		EmitWord(program, 0xFF00);
		EmitWord(program, 0x0C42); // CMPI.W #$F000,D2
		EmitWord(program, 0xF000);
		EmitBneShort(program, loop);
		EmitMoveColor00(program, 0x1111);
		EmitWord(program, 0x4E75);
		return program.ToArray();
	}

	private static byte[] CreateWaitLineLoopProgram(ushort vposrMask, bool appendRts)
	{
		var program = new List<byte>(32);
		var waitLineLoop = program.Count;
		EmitWord(program, 0x3413); // MOVE.W (A3),D2
		EmitWord(program, 0x0242); // ANDI.W #$FF00,D2
		EmitWord(program, 0xFF00);
		EmitWord(program, 0x0C42); // CMPI.W #$F000,D2
		EmitWord(program, 0xF000);
		EmitBneShort(program, waitLineLoop);
		EmitWord(program, 0x0269); // ANDI.W #mask,$0004(A1)
		EmitWord(program, vposrMask);
		EmitWord(program, 0x0004);
		EmitBneShort(program, waitLineLoop);
		EmitMoveColor00(program, 0x0F0F);
		if (appendRts)
		{
			EmitWord(program, 0x4E75);
		}

		return program.ToArray();
	}

	private static string BuildWaitLineLoopDiagnostic(
		AmigaBus bus,
		IReadOnlyList<CustomRegisterRead> reads,
		IReadOnlyList<CustomRegisterWrite> colorWrites,
		long cpuCycle)
	{
		var readText = string.Join(
			"; ",
			reads.Select((read, index) =>
			{
				var request = bus.GetBeamPosition(read.RequestedCycle);
				var grant = bus.GetBeamPosition(read.GrantedCycle);
				var delta = index == 0 ? 0 : read.SampleCycle - reads[index - 1].SampleCycle;
				return $"#{index}:reg=0x{read.Address:X3},value=0x{read.Value:X4}," +
					$"req=v{request.BeamLine}h{request.BeamHorizontal}," +
					$"grant=v{grant.BeamLine}h{grant.BeamHorizontal},delta={delta}";
			}));
		var colorText = string.Join(
			"; ",
			colorWrites.Select(write =>
			{
				var beam = bus.GetBeamPosition(write.Cycle);
				return $"0x{write.Value:X4}@v{beam.BeamLine}h{beam.BeamHorizontal}";
			}));
		var phaseStart = reads.Count == 0
			? cpuCycle - 128
			: reads[0].RequestedCycle - 16;
		var phaseEnd = colorWrites.Count == 0
			? cpuCycle
			: colorWrites[^1].Cycle + 16;
		var phaseText = BuildCpuBusPhaseTimeline(
			bus.CpuBusPhases
				.Where(phase => phase.CpuPhase.RequestedCycle >= phaseStart &&
					phase.CpuPhase.RequestedCycle <= phaseEnd)
				.ToArray(),
			phaseStart);
		return $"reads=[{readText}] colors=[{colorText}] phases=[{phaseText}]";
	}

	private static string FormatPrefetchDiagnostic(M68000PrefetchDiagnosticState state, long originCycle)
		=> $"pc={state.ProgramCounter:X6},cy={state.Cycles - originCycle}," +
			$"q={state.PrefetchCount}@{state.PrefetchAddress:X6}," +
			$"w={state.Word0:X4}/{state.Word1:X4}," +
			$"ready={state.ReadyCycle0 - originCycle}/{state.ReadyCycle1 - originCycle}," +
			$"bus={state.BusCycle - originCycle},ret={state.RetireBusCycle - originCycle}";

	private static void EmitMoveColor00(List<byte> program, ushort value)
	{
		EmitWord(program, 0x337C); // MOVE.W #value,$0180(A1)
		EmitWord(program, value);
		EmitWord(program, 0x0180);
	}

	private static void EmitBneWord(List<byte> program, int targetOffset)
	{
		EmitWord(program, 0x6600);
		EmitBranchDisplacement(program, targetOffset);
	}

	private static void EmitBneShort(List<byte> program, int targetOffset)
	{
		var displacement = targetOffset - (program.Count + 2);
		Assert.InRange(displacement, -128, 127);
		EmitWord(program, (ushort)(0x6600 | ((byte)displacement)));
	}

	private static void EmitBranchDisplacement(List<byte> program, int targetOffset)
	{
		EmitWord(program, unchecked((ushort)(targetOffset - program.Count)));
	}

	private static void EmitWord(List<byte> program, ushort word)
	{
		program.Add((byte)(word >> 8));
		program.Add((byte)word);
	}

	private static string BuildVerticalSyncPhaseDiagnostic(
		AmigaBus bus,
		IReadOnlyList<CustomRegisterWrite> markers)
	{
		if (markers.Count == 0)
		{
			return "No $0404 COLOR00 markers were written by the synthetic vertical sync loop.";
		}

		var entries = markers
			.Take(24)
			.Select((write, index) =>
			{
				var beam = bus.GetBeamPosition(write.Cycle);
				var delta = index == 0
					? 0
					: write.Cycle - markers[index - 1].Cycle;
				return $"{index}:cycle={write.Cycle},v={beam.BeamLine:X3},h={beam.BeamHorizontal:X2},delta={delta}";
			});
		var markerSummary = string.Join("; ", entries);
		if (markers.Count < 2)
		{
			return markerSummary;
		}

		var startCycle = markers[0].Cycle - 32;
		var endCycle = markers[1].Cycle + 64;
		var phaseWindow = bus.CpuBusPhases
			.Where(phase =>
				phase.CpuPhase.RequestedCycle >= startCycle &&
				phase.CpuPhase.RequestedCycle <= endCycle)
			.ToArray();
		var phaseSummary = string.Join(
			"; ",
			phaseWindow
				.Take(24)
				.Concat(phaseWindow.Skip(Math.Max(24, phaseWindow.Length - 48)))
				.Select(phase =>
				{
					var access = phase.BusAccess.GetValueOrDefault();
					var wait = phase.BusAccess.HasValue
						? access.WaitCycles
						: 0;
					return $"pc={phase.CpuPhase.InstructionProgramCounter:X6},addr={phase.CpuPhase.Address:X6},{phase.CpuPhase.AccessKind},req={phase.CpuPhase.RequestedCycle},done={phase.CpuPhase.CompletedCycle},wait={wait}";
				}));
		return $"{markerSummary} | phases: {phaseSummary}";
	}

	private static string BuildSynccpuBeamPollingDiagnostic(
		AmigaBus bus,
		IReadOnlyList<CustomRegisterWrite> markers)
	{
		var markerSummary = string.Join(
			"; ",
			markers
				.Where(write => write.Value is 0x0606 or 0x0A0A or 0x0404 or 0x0000)
				.Take(32)
				.Select(write =>
				{
					var beam = bus.GetBeamPosition(write.Cycle);
					return $"0x{write.Value:X3}@cycle={write.Cycle},v={beam.BeamLine},h={beam.BeamHorizontal}";
				}));
		var phaseSummary = string.Join(
			"; ",
			bus.CpuBusPhases
				.Where(phase => phase.CpuPhase.Address == 0x00DFF006)
				.Take(40)
				.Concat(bus.CpuBusPhases
					.Where(phase => phase.CpuPhase.Address == 0x00DFF006)
					.TakeLast(80))
				.Select(phase =>
				{
					var request = bus.GetBeamPosition(phase.CpuPhase.RequestedCycle);
					var complete = bus.GetBeamPosition(phase.CpuPhase.CompletedCycle);
					var grant = phase.BusAccess.HasValue
						? bus.GetBeamPosition(phase.BusAccess.GetValueOrDefault().GrantedCycle)
						: complete;
					var value = (ushort)((grant.BeamLine << 8) |
						EncodeVhposrHorizontal(grant.BeamHorizontal));
					var wait = phase.BusAccess.HasValue
						? phase.BusAccess.GetValueOrDefault().WaitCycles
						: 0;
					return $"pc=0x{phase.CpuPhase.InstructionProgramCounter:X4} {phase.CpuPhase.AccessKind} " +
						$"{(phase.CpuPhase.IsWrite ? "W" : "R")} req=v{request.BeamLine}h{request.BeamHorizontal} " +
						$"grant=v{grant.BeamLine}h{grant.BeamHorizontal} done=v{complete.BeamLine}h{complete.BeamHorizontal} " +
						$"wait={wait} vhpos=0x{value:X4}";
				}));
		return $"markers={markerSummary} | vhposrPhases={phaseSummary}";
	}

	private static string BuildBeamReadDiagnostic(
		AmigaBus bus,
		IReadOnlyList<CustomRegisterRead> reads,
		long colorWriteCycle)
	{
		return string.Join(
			"; ",
			reads.Select(read =>
			{
				var request = bus.GetBeamPosition(read.RequestedCycle);
				var grant = bus.GetBeamPosition(read.GrantedCycle);
				var complete = bus.GetBeamPosition(read.CompletedCycle);
				return $"reg=0x{read.Address:X3} value=0x{read.Value:X4} " +
					$"req={read.RequestedCycle}/v{request.BeamLine:X3}h{request.BeamHorizontal:X2} " +
					$"grant={read.GrantedCycle}/v{grant.BeamLine:X3}h{grant.BeamHorizontal:X2} " +
					$"done={read.CompletedCycle}/v{complete.BeamLine:X3}h{complete.BeamHorizontal:X2} " +
					$"colorDelta={colorWriteCycle - read.CompletedCycle}";
			}));
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
		const int rasterlineRingSize = 3;
		const int planesPerLine = 8;
		const int wordsPerPlane = 128;
		var ringSlot = Math.Abs(row % rasterlineRingSize);
		return words[(ringSlot * planesPerLine * wordsPerPlane) + (plane * wordsPerPlane) + word];
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

	private static void ConfigureProbe10BitplaneDma(AmigaBus bus)
	{
		bus.WriteWord(0x00DFF096, 0x8100);
		bus.WriteWord(0x00DFF092, 0x0038);
		bus.WriteWord(0x00DFF094, 0x00D0);
		bus.WriteLong(0x00DFF0E0, 0x00006000);
		bus.WriteLong(0x00DFF0E4, 0x00007000);
		bus.WriteWord(0x00DFF08E, 0x2C81);
		bus.WriteWord(0x00DFF090, 0xF4C1);
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
		var hStart = x + 129 - AmigaConstants.PalLowResOverscanBorderX;
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
		=> StartLongBlit(bus, 0);

	private static byte[] CreateSyntheticRomBlitterWorkload(int repetitions, int readsPerBlit)
	{
		var program = new byte[repetitions * (8 + (readsPerBlit * 2))];
		var offset = 0;
		for (var repetition = 0; repetition < repetitions; repetition++)
		{
			BigEndian.WriteUInt16(program, offset, 0x33FC); // MOVE.W #imm,(xxx).L
			BigEndian.WriteUInt16(program, offset + 2, 0x0044); // start a four-word blit
			BigEndian.WriteUInt16(program, offset + 4, 0x00DF);
			BigEndian.WriteUInt16(program, offset + 6, 0xF058); // BLTSIZE
			offset += 8;
			for (var read = 0; read < readsPerBlit; read++)
			{
				BigEndian.WriteUInt16(program, offset, 0x3018); // MOVE.W (A0)+,D0
				offset += 2;
			}
		}

		return program;
	}

	private static void StartLongBlit(AmigaBus bus, long cycle)
	{
		bus.WriteWord(0x00DFF040, 0x09F0, cycle);
		bus.WriteWord(0x00DFF042, 0x0000, cycle);
		bus.WriteWord(0x00DFF050, 0x0000, cycle);
		bus.WriteWord(0x00DFF052, 0x3000, cycle);
		bus.WriteWord(0x00DFF054, 0x0000, cycle);
		bus.WriteWord(0x00DFF056, 0x4000, cycle);
		bus.WriteWord(0x00DFF096, 0x8240, cycle);
		bus.WriteWord(0x00DFF058, (ushort)((8 << 6) | 8), cycle);
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

	private static int ReadBeamLineAt(AmigaBus bus, long cycle)
	{
		bus.AdvanceRasterTo(cycle);
		return (int)((bus.ReadLong(0x00DFF004) >> 8) & 0x01FF);
	}

	private static ushort ReadVposrAt(AmigaBus bus, long cycle)
	{
		bus.AdvanceRasterTo(cycle);
		return bus.ReadWord(0x00DFF004);
	}

	private static ushort EncodeVposr(AgnusBeamPosition beam)
		=> (ushort)(((beam.IsLongFrame ? 1 : 0) << 15) | ((beam.BeamLine >> 8) & 0x0001));

	private static ushort EncodeVhposr(AgnusBeamPosition beam)
	{
		var horizontal = EncodeVhposrHorizontal(beam.BeamHorizontal);
		return (ushort)(((beam.BeamLine & 0x00FF) << 8) | horizontal);
	}

	private static int EncodeVhposrHorizontal(int beamHorizontal)
	{
		var physicalHorizontal = Math.Clamp(beamHorizontal, 0, 0xE2);
		return (physicalHorizontal + 4) % AmigaConstants.A500PalColorClocksPerRasterLine;
	}

	private static (long StartCycle, AmigaBus Bus, CustomRegisterRead[] Reads) RunVAmigaTsProbe10VhposrReadMacro(
		int programAddress,
		int valuesAddress,
		ushort firstExpectedValue)
	{
		var program = CreateVAmigaTsProbeVhposrReadMacroProgram();
		var lineStart = 312L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var firstExpectedPhysicalHorizontal = ((firstExpectedValue & 0x00FF) -
			4 + AmigaConstants.A500PalColorClocksPerRasterLine) %
			AmigaConstants.A500PalColorClocksPerRasterLine;
		var searchCenter = lineStart + (firstExpectedPhysicalHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);

		var candidates = new List<string>();
		for (var startCycle = searchCenter - 512; startCycle <= searchCenter + 512; startCycle++)
		{
			var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
			Write(bus.ChipRam, programAddress, program);
			var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
			cpu.Reset((uint)programAddress, 0x3000);
			cpu.State.A[2] = (uint)valuesAddress;
			cpu.State.A[3] = 0x00DFF006;
			cpu.State.Cycles = startCycle;

			for (var i = 0; i < 32; i++)
			{
				cpu.ExecuteInstruction();
			}

			var reads = bus.CustomRegisterReads
				.Where(read => read.Address == 0x006 && read.Kind == AmigaBusAccessKind.CpuDataRead)
				.ToArray();
			if (reads.Length == 16 && reads[0].Value == firstExpectedValue)
			{
				return (startCycle, bus, reads);
			}

			if (reads.Length == 16 && candidates.Count < 48)
			{
				candidates.Add($"start={startCycle}:first=0x{reads[0].Value:X4}@{reads[0].SampleCycle}");
			}
		}

		throw new InvalidOperationException(
			$"Unable to align probe macro first VHPOSR read to 0x{firstExpectedValue:X4} near cycle {searchCenter}. " +
			$"Candidates: {string.Join("; ", candidates)}");
	}

	private static (long StartCycle, AmigaBus Bus, CustomRegisterRead[] Reads) RunVAmigaTsVprobeVposrReadMacro(
		int programAddress,
		int valuesAddress,
		long startCycle)
	{
		var program = CreateVAmigaTsProbeVhposrReadMacroProgram();
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, programAddress, program);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset((uint)programAddress, 0x3000);
		cpu.State.A[2] = (uint)valuesAddress;
		cpu.State.A[3] = 0x00DFF004;
		cpu.State.Cycles = startCycle;

		for (var i = 0; i < 32; i++)
		{
			cpu.ExecuteInstruction();
		}

		var reads = bus.CustomRegisterReads
			.Where(read => read.Address == 0x004 && read.Kind == AmigaBusAccessKind.CpuDataRead)
			.ToArray();
		return (startCycle, bus, reads);
	}

	private static (long StartCycle, AmigaBus Bus, CustomRegisterRead[] Reads) RunVAmigaTsProbe10SourceVhposrReadMacro(
		int programAddress,
		int valuesAddress,
		ushort firstExpectedValue,
		bool isLongFrame = true)
	{
		var program = CreateVAmigaTsProbe10SourceVhposrReadMacroProgram();
		var lineStart = (isLongFrame ? 312L : 311L) * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var firstExpectedPhysicalHorizontal = ((firstExpectedValue & 0x00FF) -
			4 + AmigaConstants.A500PalColorClocksPerRasterLine) %
			AmigaConstants.A500PalColorClocksPerRasterLine;
		var searchCenter = lineStart + (firstExpectedPhysicalHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);

		var candidates = new List<string>();
		for (var startCycle = searchCenter - 512; startCycle <= searchCenter + 512; startCycle++)
		{
			var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
			bus.WriteWord(0x00DFF02A, isLongFrame ? (ushort)0x8001 : (ushort)0x0001);
			Write(bus.ChipRam, programAddress, program);
			var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
			cpu.Reset((uint)programAddress, 0x3000);
			cpu.State.A[1] = 0x00DFF000;
			cpu.State.A[2] = (uint)valuesAddress;
			cpu.State.Cycles = startCycle;

			for (var i = 0; i < 32; i++)
			{
				cpu.ExecuteInstruction();
			}

			var reads = bus.CustomRegisterReads
				.Where(read => read.Address == 0x006 && read.Kind == AmigaBusAccessKind.CpuDataRead)
				.ToArray();
			if (reads.Length == 16 && reads[0].Value == firstExpectedValue)
			{
				return (startCycle, bus, reads);
			}

			if (reads.Length == 16 && candidates.Count < 48)
			{
				candidates.Add($"start={startCycle}:first=0x{reads[0].Value:X4}@{reads[0].SampleCycle}");
			}
		}

		throw new InvalidOperationException(
			$"Unable to align source-shaped probe10 macro first VHPOSR read to 0x{firstExpectedValue:X4} near cycle {searchCenter}. " +
			$"Candidates: {string.Join("; ", candidates)}");
	}

	private static (long StartCycle, AmigaBus Bus, CustomRegisterRead[] Reads) RunVhposrReadStorePair(
		int programAddress,
		int valuesAddress,
		ushort firstExpectedValue)
	{
		var program = new byte[]
		{
			0x3A, 0x13, // MOVE.W (A3),D5
			0x34, 0xC5, // MOVE.W D5,(A2)+
			0x4E, 0x71, // NOP, lets the store instruction top up fallthrough prefetch.
			0x4E, 0x71
		};
		var lineStart = 312L * AmigaConstants.A500PalCpuCyclesPerRasterLine;
		var firstExpectedPhysicalHorizontal = ((firstExpectedValue & 0x00FF) -
			4 + AmigaConstants.A500PalColorClocksPerRasterLine) %
			AmigaConstants.A500PalColorClocksPerRasterLine;
		var searchCenter = lineStart + (firstExpectedPhysicalHorizontal * AmigaConstants.A500PalCpuCyclesPerColorClock);

		var candidates = new List<string>();
		for (var startCycle = searchCenter - 512; startCycle <= searchCenter + 512; startCycle++)
		{
			var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
			Write(bus.ChipRam, programAddress, program);
			var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
			cpu.Reset((uint)programAddress, 0x3000);
			cpu.State.A[2] = (uint)valuesAddress;
			cpu.State.A[3] = 0x00DFF006;
			cpu.State.Cycles = startCycle;

			cpu.ExecuteInstruction();
			cpu.ExecuteInstruction();

			var reads = bus.CustomRegisterReads
				.Where(read => read.Address == 0x006 && read.Kind == AmigaBusAccessKind.CpuDataRead)
				.ToArray();
			if (reads.Length == 1 && reads[0].Value == firstExpectedValue)
			{
				return (startCycle, bus, reads);
			}

			if (reads.Length == 1 && candidates.Count < 48)
			{
				candidates.Add($"start={startCycle}:first=0x{reads[0].Value:X4}@{reads[0].SampleCycle}");
			}
		}

		throw new InvalidOperationException(
			$"Unable to align VHPOSR read/store pair first read to 0x{firstExpectedValue:X4} near cycle {searchCenter}. " +
			$"Candidates: {string.Join("; ", candidates)}");
	}

	private static byte[] CreateVAmigaTsProbeVhposrReadMacroProgram()
	{
		var program = new byte[16 * 4];
		for (var offset = 0; offset < program.Length; offset += 4)
		{
			program[offset] = 0x3A;
			program[offset + 1] = 0x13; // MOVE.W (A3),D5
			program[offset + 2] = 0x34;
			program[offset + 3] = 0xC5; // MOVE.W D5,(A2)+
		}

		return program;
	}

	private static byte[] CreateVAmigaTsProbe10SourceVhposrReadMacroProgram()
	{
		var program = new byte[16 * 6];
		for (var offset = 0; offset < program.Length; offset += 6)
		{
			program[offset] = 0x3A;
			program[offset + 1] = 0x29; // MOVE.W $0006(A1),D5
			program[offset + 2] = 0x00;
			program[offset + 3] = 0x06;
			program[offset + 4] = 0x34;
			program[offset + 5] = 0xC5; // MOVE.W D5,(A2)+
		}

		return program;
	}

	private static byte[] CreateVAmigaTsVprobe2SourceVposrReadMacroProgram()
	{
		var program = new byte[16 * 6];
		for (var offset = 0; offset < program.Length; offset += 6)
		{
			program[offset] = 0x3A;
			program[offset + 1] = 0x29; // MOVE.W $0004(A1),D5
			program[offset + 2] = 0x00;
			program[offset + 3] = 0x04;
			program[offset + 4] = 0x34;
			program[offset + 5] = 0xC5; // MOVE.W D5,(A2)+
		}

		return program;
	}

	private static byte[] CreateVAmigaTsProbe10Irq1Handler(int valuesAddress)
	{
		var program = new List<byte>(84);
		EmitWord(program, 0x337C); // MOVE.W #$0300,COLOR00(A1)
		EmitWord(program, 0x0300);
		EmitWord(program, 0x0180);
		EmitWord(program, 0x337C); // MOVE.W #$3FFF,INTREQ(A1)
		EmitWord(program, 0x3FFF);
		EmitWord(program, 0x009C);
		EmitWord(program, 0x45F9); // LEA values,A2
		EmitWord(program, (ushort)(valuesAddress >> 16));
		EmitWord(program, (ushort)valuesAddress);
		// probe10.s uses MOVE.W VHPOSR(A1),D5, including the displacement word.
		program.AddRange(CreateVAmigaTsProbe10SourceVhposrReadMacroProgram());
		EmitWord(program, 0x4E73); // RTE
		return program.ToArray();
	}

	private static byte[] CreateVAmigaTsProbe10SourceIrq1Handler(int handlerAddress, int valuesAddress)
	{
		var program = new List<byte>(82);
		EmitWord(program, 0x337C); // MOVE.W #$0300,COLOR00(A1)
		EmitWord(program, 0x0300);
		EmitWord(program, 0x0180);
		EmitWord(program, 0x337C); // MOVE.W #$3FFF,INTREQ(A1)
		EmitWord(program, 0x3FFF);
		EmitWord(program, 0x009C);
		EmitWord(program, 0x45FA); // LEA values(PC),A2
		// 68000 d16(PC) uses the extension-word address as its PC base.
		EmitWord(program, checked((ushort)(valuesAddress - (handlerAddress + 14))));
		program.AddRange(CreateVAmigaTsProbe10SourceVhposrReadMacroProgram());
		EmitWord(program, 0x4E73); // RTE
		return program.ToArray();
	}

	private static byte[] CreateVAmigaTsProbe10InterruptedWaitLoop()
	{
		var program = new List<byte>(24);
		var waitLineLoop = program.Count;
		EmitWord(program, 0x3413); // MOVE.W (A3),D2
		EmitWord(program, 0xC47C); // AND.W #$FF00,D2
		EmitWord(program, 0xFF00);
		EmitWord(program, 0xB47C); // CMP.W #$2000,D2
		EmitWord(program, 0x2000);
		EmitBneShort(program, waitLineLoop);
		EmitWord(program, 0x0269); // ANDI.W #$0001,$0004(A1)
		EmitWord(program, 0x0001);
		EmitWord(program, 0x0004);
		EmitBneShort(program, waitLineLoop);
		return program.ToArray();
	}

	private static byte[] CreateVAmigaTsVprobe2Irq1Handler(int valuesAddress)
	{
		var program = new List<byte>(104);
		EmitWord(program, 0x45F9); // LEA values,A2
		EmitWord(program, (ushort)(valuesAddress >> 16));
		EmitWord(program, (ushort)valuesAddress);
		program.AddRange(CreateVAmigaTsVprobe2SourceVposrReadMacroProgram());
		EmitWord(program, 0x337C); // MOVE.W #$3FFF,INTREQ(A1)
		EmitWord(program, 0x3FFF);
		EmitWord(program, 0x009C);
		EmitWord(program, 0x4E73); // RTE
		return program.ToArray();
	}

	private static string BuildVhposrProbeDiagnostic(
		long startCycle,
		IReadOnlyList<CustomRegisterRead> reads,
		IReadOnlyList<ushort> actual,
		IReadOnlyList<ushort> expected)
		=> BuildVhposrProbeDiagnostic(startCycle, bus: null, reads, actual, expected);

	private static string BuildVhposrProbeDiagnostic(
		long startCycle,
		AmigaBus? bus,
		IReadOnlyList<CustomRegisterRead> reads,
		IReadOnlyList<ushort> actual,
		IReadOnlyList<ushort> expected)
	{
		var actualText = string.Join(",", actual.Select(value => $"0x{value:X4}"));
		var expectedText = string.Join(",", expected.Select(value => $"0x{value:X4}"));
		var readsText = string.Join("; ", reads.Select((read, index) =>
		{
			var delta = index == 0 ? 0 : read.SampleCycle - reads[index - 1].SampleCycle;
			var expectedValue = index < expected.Count ? expected[index] : (ushort)0;
			var expectedDelta = index == 0 ? 0 : 20;
			var beam = bus?.GetBeamPosition(read.SampleCycle);
			var beamText = beam.HasValue ? $"/v{beam.Value.BeamLine:X3}h{beam.Value.BeamHorizontal:X2}" : string.Empty;
			return
				$"#{index}: actual=0x{read.Value:X4}, expected=0x{expectedValue:X4}, " +
				$"sample={read.SampleCycle}(+{read.SampleCycle - startCycle}){beamText}, " +
				$"delta={delta}/expectedDelta={expectedDelta}, " +
				$"request={read.RequestedCycle - startCycle}, grant={read.GrantedCycle - startCycle}, complete={read.CompletedCycle - startCycle}";
		}));
		var timeline = string.Empty;
		if (bus != null && reads.Count > 0)
		{
			var endCycle = reads[^1].CompletedCycle + 12;
			var phases = bus.CpuBusPhases
				.Where(phase => phase.CpuPhase.RequestedCycle >= startCycle &&
					phase.CpuPhase.RequestedCycle <= endCycle)
				.ToArray();
			timeline = $", phases=[{BuildCpuBusPhaseTimeline(phases, startCycle)}]";
		}

		return
			$"startCycle={startCycle}, expected=[{expectedText}], actual=[{actualText}], " +
			$"reads=[{readsText}]{timeline}";
	}

	private static string BuildCpuBusPhaseTimeline(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		long originCycle)
		=> string.Join(
			" | ",
			phases.Select(phase =>
			{
				var cpu = phase.CpuPhase;
				var access = phase.BusAccess;
				var grant = access.HasValue
					? $",g+{access.Value.GrantedCycle - originCycle}"
					: string.Empty;
				return $"pc=0x{cpu.InstructionProgramCounter:X4},{cpu.AccessKind},0x{cpu.Address:X6}," +
					$"r+{cpu.RequestedCycle - originCycle}..+{cpu.CompletedCycle - originCycle}{grant}";
			}));

	private static string ClassifyMoveReadStorePairEarlyPhase(
		CustomRegisterRead firstRead,
		CustomRegisterRead secondRead,
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		int expectedDelta)
	{
		var dataWrite = phases.FirstOrDefault(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
			phase.CpuPhase.RequestedCycle >= firstRead.CompletedCycle &&
			phase.CpuPhase.RequestedCycle < secondRead.RequestedCycle);
		var finalPrefetch = phases.LastOrDefault(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.CpuPhase.RequestedCycle >= firstRead.CompletedCycle &&
			phase.CpuPhase.RequestedCycle < secondRead.RequestedCycle);
		var actualDelta = secondRead.SampleCycle - firstRead.SampleCycle;
		var missingCycles = expectedDelta - actualDelta;
		if (missingCycles <= 0)
		{
			return "cadence is not early";
		}

		return
			$"next read is {missingCycles} cycles early; " +
			$"write={FormatOptionalPhase(dataWrite, firstRead.SampleCycle)}, " +
			$"finalPrefetch={FormatOptionalPhase(finalPrefetch, firstRead.SampleCycle)}";
	}

	private static string FormatOptionalPhase(AmigaCpuBusPhaseTrace phase, long originCycle)
	{
		if (phase.CpuPhase.AccessKind == default &&
			phase.CpuPhase.Address == default &&
			phase.CpuPhase.RequestedCycle == default &&
			phase.CpuPhase.CompletedCycle == default)
		{
			return "missing";
		}

		return
			$"{phase.CpuPhase.AccessKind}@r+{phase.CpuPhase.RequestedCycle - originCycle}" +
			$"..+{phase.CpuPhase.CompletedCycle - originCycle}";
	}

	private static ushort[] ReadChipWords(byte[] memory, int address, int count)
	{
		var values = new ushort[count];
		for (var i = 0; i < values.Length; i++)
		{
			var offset = address + (i * 2);
			values[i] = (ushort)((memory[offset] << 8) | memory[offset + 1]);
		}

		return values;
	}

	private static T GetPrivateField<T>(object target, string name)
	{
		return Assert.IsType<T>(GetPrivateFieldValue(target, name));
	}

	private static object GetPrivateFieldValue(object target, string name)
	{
		var field = target.GetType().GetField(
			name,
			System.Reflection.BindingFlags.Instance |
				System.Reflection.BindingFlags.Public |
				System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(field);
		return field.GetValue(target)!;
	}

	private static T GetMemberValue<T>(object target, string name)
	{
		return Assert.IsType<T>(GetMemberRawValue(target, name));
	}

	private static object GetMemberRawValue(object target, string name)
	{
		var flags = System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic;
		var field = target.GetType().GetField(name, flags);
		if (field != null)
		{
			return field.GetValue(target)!;
		}

		var property = target.GetType().GetProperty(name, flags);
		Assert.NotNull(property);
		return property.GetValue(target)!;
	}

	private static object GetPrivateMemberValue(object target, string name)
		=> GetMemberRawValue(target, name);

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

	private static byte[] CreateMoveWordReadStorePairProgram(int pairs)
	{
		var program = new byte[pairs * 4];
		for (var offset = 0; offset < program.Length; offset += 4)
		{
			program[offset] = 0x3A;
			program[offset + 1] = 0x18; // MOVE.W (A0)+,D5
			program[offset + 2] = 0x32;
			program[offset + 3] = 0xC5; // MOVE.W D5,(A1)+
		}

		return program;
	}

	private static void StartNastyBlit(AmigaBus bus, long startCycle = 0)
	{
		BigEndian.WriteUInt16(bus.ChipRam, 0x3000, 0x1234);
		bus.WriteWord(0x00DFF040, 0x09F0);
		bus.WriteWord(0x00DFF050, 0x0000);
		bus.WriteWord(0x00DFF052, 0x3000);
		bus.WriteWord(0x00DFF054, 0x0000);
		bus.WriteWord(0x00DFF056, 0x4000);
		bus.WriteWord(0x00DFF096, 0x8640);
		bus.AdvanceDmaTo(100);
		bus.WriteWord(0x00DFF058, 0x0044, startCycle);
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

	private sealed class NoOpBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		public static NoOpBoundary Instance { get; } = new();

		public bool BeforeInstruction()
			=> true;

		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;

		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle)
			=> targetCycle;

		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = currentCycle;
			_ = instructionCount;
		}

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			_ = currentCycle;
		}
	}



	private sealed class RejectDeferredBatchBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		public static RejectDeferredBatchBoundary Instance { get; } = new();

		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => false;

		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle)
			=> targetCycle;

		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = currentCycle;
			_ = instructionCount;
		}

		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			_ = currentCycle;
		}
	}

	private sealed class CountingDeferredBatchBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		private readonly AmigaBus _bus;

		public CountingDeferredBatchBoundary(AmigaBus bus)
		{
			_bus = bus;
		}

		public int ScalarCallbacks { get; private set; }

		public int BatchCallbacks { get; private set; }

		public int BatchedInstructions { get; private set; }

		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;

		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle)
			=> targetCycle;

		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = currentCycle;
			BatchCallbacks++;
			BatchedInstructions += instructionCount;
			_bus.AdvanceHardwareEventsTo(currentCycle, cpuInterruptMask: 0);
		}

		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			ScalarCallbacks++;
			_bus.AdvanceHardwareEventsTo(currentCycle, cpuInterruptMask: 0);
		}
	}

	private sealed class ThrowingDeferredBatchBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		public static ThrowingDeferredBatchBoundary Instance { get; } = new();

		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;

		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle)
			=> throw new InvalidOperationException("Synthetic boundary failure.");

		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount)
		{
		}

		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
		}
	}

	private sealed class ScheduledDeferredBatchBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		public ScheduledDeferredBatchBoundary(long scheduledCycle)
		{
			ScheduledCycle = scheduledCycle;
		}

		public long ScheduledCycle { get; }

		public bool Fired { get; private set; }

		public long FirstAdvanceCycle { get; private set; } = -1;

		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;

		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle)
			=> Fired ? targetCycle : Math.Min(targetCycle, ScheduledCycle);

		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = instructionCount;
			Advance(currentCycle);
		}

		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			Advance(currentCycle);
		}

		private void Advance(long currentCycle)
		{
			if (Fired || currentCycle < ScheduledCycle)
			{
				return;
			}

			Fired = true;
			FirstAdvanceCycle = currentCycle;
		}
	}

	private sealed class MultipleScheduledDeferredBatchBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		private readonly long _firstCycle;
		private readonly long _secondCycle;

		public MultipleScheduledDeferredBatchBoundary(long firstCycle, long secondCycle)
		{
			_firstCycle = firstCycle;
			_secondCycle = secondCycle;
		}

		public long FirstScheduledCycle { get; private set; } = -1;

		public long FirstAdvanceCycle { get; private set; } = -1;

		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;

		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle)
		{
			var nextCycle = FirstScheduledCycle < 0 ? _firstCycle : _secondCycle;
			return Math.Min(targetCycle, nextCycle);
		}

		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = instructionCount;
			Advance(currentCycle);
		}

		public bool BeforeInstruction() => true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			Advance(currentCycle);
		}

		private void Advance(long currentCycle)
		{
			if (FirstScheduledCycle >= 0 || currentCycle < _firstCycle)
			{
				return;
			}

			FirstScheduledCycle = _firstCycle;
			FirstAdvanceCycle = currentCycle;
		}
	}
}
