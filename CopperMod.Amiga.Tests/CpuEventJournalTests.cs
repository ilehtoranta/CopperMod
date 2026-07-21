using CopperMod.Amiga.Bus;
using Copper68k;

namespace CopperMod.Amiga.Tests;

public sealed class CpuEventJournalTests
{
	[Fact]
	public void JournalPreservesChronologicalMetadataAndCommitOrder()
	{
		var journal = new CpuEventJournal(capacity: 2);

		Assert.True(journal.TryEnqueue(
			20,
			CpuJournalInstructionPhase.Operand,
			AmigaBusAccessTarget.ChipRam,
			0x1000,
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessSize.Word,
			isWrite: true,
			0x1234,
			CpuJournalDependencyFlags.MemoryWrite,
			out var firstSequence));
		Assert.True(journal.TryEnqueue(
			24,
			CpuJournalInstructionPhase.Retirement,
			AmigaBusAccessTarget.ChipRam,
			0x1002,
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessSize.Word,
			isWrite: true,
			0x5678,
			CpuJournalDependencyFlags.MemoryWrite,
			out var secondSequence));

		Assert.True(secondSequence > firstSequence);
		ref var first = ref journal.Peek();
		Assert.Equal(firstSequence, first.Sequence);
		Assert.Equal(20, first.RequestedCycle);
		Assert.Equal(0x1000u, first.Address);
		Assert.Equal(0x1234u, first.Value);
		Assert.False(first.Committed);
		journal.CommitHead(22, 24);
		Assert.Equal(secondSequence, journal.Peek().Sequence);
		journal.CommitHead(26, 28);

		Assert.Equal(0, journal.Count);
		Assert.Equal(2, journal.CommittedEvents);
	}

	[Fact]
	public void FullJournalReturnsHardBarrierWithoutAllocating()
	{
		var journal = new CpuEventJournal(capacity: 2);
		for (var i = 0; i < journal.Capacity; i++)
		{
			Assert.True(TryEnqueueWord(journal, 20 + (i * 4), 0x1000u + (uint)(i * 2)));
		}

		var before = GC.GetAllocatedBytesForCurrentThread();
		var accepted = TryEnqueueWord(journal, 40, 0x1010);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.False(accepted);
		Assert.Equal(0, allocated);
		Assert.Equal(1, journal.FullBarriers);
		Assert.Equal(journal.Capacity, journal.Count);
	}

	[Fact]
	public void ResetReusesBackingStorageWithoutClearingOrAllocating()
	{
		var journal = new CpuEventJournal(capacity: 4);
		Assert.True(TryEnqueueWord(journal, 20, 0x1000));
		var before = GC.GetAllocatedBytesForCurrentThread();

		journal.Reset();
		Assert.True(TryEnqueueWord(journal, 24, 0x1002));
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(1, journal.Count);
		Assert.Equal(1UL, journal.Peek().Sequence);
		Assert.Equal(1, journal.Resets);
	}

	[Fact]
	public void JournalRejectsOutOfOrderRequestedCycles()
	{
		var journal = new CpuEventJournal(capacity: 2);
		Assert.True(TryEnqueueWord(journal, 24, 0x1000));

		Assert.Throws<InvalidOperationException>(() => TryEnqueueWord(journal, 20, 0x1002));
		Assert.Equal(1, journal.Count);
	}

	[Fact]
	public void JournalDetectsByteWordAndLongOverlap()
	{
		var journal = new CpuEventJournal(capacity: 4);
		Assert.True(TryEnqueueWord(journal, 20, 0x1002));

		Assert.True(journal.HasPendingOverlap(0x1001, 2));
		Assert.True(journal.HasPendingOverlap(0x1002, 1));
		Assert.True(journal.HasPendingOverlap(0x1000, 4));
		Assert.False(journal.HasPendingOverlap(0x1004, 4));
	}

	[Fact]
	public void ExecutorFlushesChipWordWritesChronologically()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		var executor = bus.CausalBusExecutor;
		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x1000, 0x1234, 20, CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.None, out var firstSequence));
		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x1002, 0x5678, 24, CpuJournalInstructionPhase.Retirement,
			CpuJournalDependencyFlags.None, out var secondSequence));
		Assert.Equal(20, executor.CpuJournalDeadlineCycle);

		Assert.Equal((ushort)0, bus.ReadWord(0x1000));
		Assert.Equal((ushort)0, bus.ReadWord(0x1002));
		var completedCycle = executor.FlushCpuEventJournal();

		Assert.True(secondSequence > firstSequence);
		Assert.True(completedCycle >= 24);
		Assert.Equal((ushort)0x1234, bus.ReadWord(0x1000));
		Assert.Equal((ushort)0x5678, bus.ReadWord(0x1002));
		Assert.Equal(0, executor.CpuEventJournal.Count);
		Assert.Equal(long.MaxValue, executor.CpuJournalDeadlineCycle);
		Assert.Equal(2, executor.CpuEventJournal.CommittedEvents);
		var writes = bus.BusAccesses
			.Where(access => access.Request.Kind == AmigaBusAccessKind.CpuDataWrite)
			.ToArray();
		Assert.Equal(2, writes.Length);
		Assert.True(writes[0].GrantedCycle <= writes[1].GrantedCycle);
	}

	[Fact]
	public void ExecutorCommitsLongWordHalvesAsIndependentWordEvents()
	{
		var bus = new AmigaBus(captureBusAccesses: true);
		var executor = bus.CausalBusExecutor;
		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x2000, 0xA55A, 20, CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.LongWordFirstHalf, out _));
		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x2002, 0x5AA5, 24, CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.LongWordSecondHalf, out _));

		executor.FlushCpuEventJournal();

		Assert.Equal((ushort)0xA55A, bus.ReadWord(0x2000));
		Assert.Equal((ushort)0x5AA5, bus.ReadWord(0x2002));
		Assert.Equal(2, executor.CpuEventJournal.CommittedEvents);
	}

	[Fact]
	public void DeferredRomBatchJournalsChipWordWriteUntilChronologicalFlush()
	{
		const uint romBase = 0x00FC0000;
		var rom = new byte[256];
		for (var offset = 0; offset < rom.Length; offset += 2)
		{
			rom[offset] = 0x4E;
			rom[offset + 1] = 0x71;
		}

		// NOP; NOP; two writes; then a dependent Chip RAM read that forces the barrier.
		new byte[] { 0x33, 0xFC, 0xA5, 0x5A, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		new byte[]
		{
			0x23, 0xFC, 0x12, 0x34, 0x56, 0x78, 0x00, 0x00, 0x10, 0x04
		}.CopyTo(rom, 12);
		new byte[] { 0x30, 0x39, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 22);
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x3000);

		cpu.ExecuteInstructions(10, null, JournalTestBoundary.Instance);

		Assert.Equal((ushort)0xA55A, bus.ReadWord(0x1000));
		Assert.Equal(0x12345678u, bus.ReadLong(0x1004));
		Assert.Equal(0xA55Au, cpu.State.D[0] & 0xFFFFu);
		Assert.True(bus.CausalBusExecutor.CpuEventJournal.EnqueuedEvents >= 3);
		Assert.Equal(
			bus.CausalBusExecutor.CpuEventJournal.EnqueuedEvents,
			bus.CausalBusExecutor.CpuEventJournal.CommittedEvents);
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
	}

	[Fact]
	public void ExecutorJournalEnqueueAndFlushAllocateNothingAfterWarmup()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x3000, 0x1111, 20, CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.None, out _));
		executor.FlushCpuEventJournal();
		executor.CpuEventJournal.Reset();

		var before = GC.GetAllocatedBytesForCurrentThread();
		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x3002, 0x2222, 40, CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.None, out _));
		executor.FlushCpuEventJournal();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal((ushort)0x2222, bus.ReadWord(0x3002));
	}

	[Fact]
	public void ExceptionBoundaryFlushesJournaledWriteExactlyOnce()
	{
		const uint romBase = 0x00FC0000;
		const uint handler = romBase + 0x40;
		var rom = new byte[256];
		for (var offset = 0; offset < rom.Length; offset += 2)
		{
			rom[offset] = 0x4E;
			rom[offset + 1] = 0x71;
		}

		new byte[] { 0x33, 0xFC, 0xCA, 0xFE, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		rom[12] = 0x4A;
		rom[13] = 0xFC; // ILLEGAL
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		bus.WriteLong(4u * 4u, handler); // vector 4
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus,
			default(AmigaCpuDataAccess),
			enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);

		cpu.ExecuteInstructions(4, null, JournalTestBoundary.Instance);

		Assert.Equal((ushort)0xCAFE, bus.ReadWord(0x1000));
		Assert.Equal(1, bus.CausalBusExecutor.CpuEventJournal.EnqueuedEvents);
		Assert.Equal(1, bus.CausalBusExecutor.CpuEventJournal.CommittedEvents);
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
	}

	[Fact]
	public void OverlappingSecondWriteFlushesFirstBeforeWritingScalar()
	{
		const uint romBase = 0x00FC0000;
		var rom = new byte[256];
		for (var offset = 0; offset < rom.Length; offset += 2)
		{
			rom[offset] = 0x4E;
			rom[offset + 1] = 0x71;
		}

		new byte[] { 0x33, 0xFC, 0x11, 0x11, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		new byte[] { 0x33, 0xFC, 0x22, 0x22, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 12);
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);

		cpu.ExecuteInstructions(8, null, JournalTestBoundary.Instance);

		Assert.Equal((ushort)0x2222, bus.ReadWord(0x1000));
		Assert.Equal(1, bus.CausalBusExecutor.CpuEventJournal.EnqueuedEvents);
		Assert.Equal(1, bus.CausalBusExecutor.CpuEventJournal.CommittedEvents);
	}

	[Fact]
	public void StopBoundaryFlushesJournalBeforePublishingStoppedState()
	{
		const uint romBase = 0x00FC0000;
		var rom = new byte[256];
		for (var offset = 0; offset < rom.Length; offset += 2)
		{
			rom[offset] = 0x4E;
			rom[offset + 1] = 0x71;
		}

		new byte[] { 0x33, 0xFC, 0xBE, 0xEF, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		new byte[] { 0x4E, 0x72, 0x27, 0x00 }.CopyTo(rom, 12);
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);

		cpu.ExecuteInstructions(4, null, JournalTestBoundary.Instance);

		Assert.True(cpu.State.Stopped);
		Assert.Equal((ushort)0xBEEF, bus.ReadWord(0x1000));
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
		Assert.Equal(
			bus.CausalBusExecutor.CpuEventJournal.EnqueuedEvents,
			bus.CausalBusExecutor.CpuEventJournal.CommittedEvents);
	}

	[Theory]
	[InlineData(0x1039, 0x000000A5u)] // MOVE.B $1000,D0
	[InlineData(0x3039, 0x0000A55Au)] // MOVE.W $1000,D0
	[InlineData(0x2039, 0xA55A5AA5u)] // MOVE.L $1000,D0
	public void OverlappingByteWordAndLongReadsFlushPendingWrite(ushort readOpcode, uint expected)
	{
		const uint romBase = 0x00FC0000;
		var rom = CreateNopRom(256);
		new byte[] { 0x33, 0xFC, 0xA5, 0x5A, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		rom[12] = (byte)(readOpcode >> 8);
		rom[13] = (byte)readOpcode;
		new byte[] { 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 14);
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		bus.WriteWord(0x1002, 0x5AA5);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);

		cpu.ExecuteInstructions(6, null, JournalTestBoundary.Instance);

		Assert.Equal(expected, cpu.State.D[0]);
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
		Assert.Equal(
			bus.CausalBusExecutor.CpuEventJournal.EnqueuedEvents,
			bus.CausalBusExecutor.CpuEventJournal.CommittedEvents);
	}

	[Fact]
	public void ResetInstructionFlushesJournalBeforeExternalResetCallback()
	{
		const uint romBase = 0x00FC0000;
		var rom = CreateNopRom(256);
		new byte[] { 0x33, 0xFC, 0xD0, 0x0D, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		new byte[] { 0x4E, 0x70 }.CopyTo(rom, 12); // RESET
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);

		cpu.ExecuteInstructions(4, null, JournalTestBoundary.Instance);

		Assert.Equal((ushort)0xD00D, bus.ReadWord(0x1000));
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
	}

	[Fact]
	public void IntegratedRingFullBarrierFlushesThenFallsBackWithoutAllocation()
	{
		const uint romBase = 0x00FC0000;
		var rom = CreateNopRom(256);
		new byte[] { 0x33, 0xFC, 0x11, 0x11, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		new byte[] { 0x33, 0xFC, 0x22, 0x22, 0x00, 0x00, 0x10, 0x02 }.CopyTo(rom, 12);
		new byte[] { 0x33, 0xFC, 0x33, 0x33, 0x00, 0x00, 0x10, 0x04 }.CopyTo(rom, 20);
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			cpuEventJournalCapacity: 2);
		bus.MapReadOnlyMemory(romBase, rom);
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);
		cpu.ExecuteInstructions(8, null, JournalTestBoundary.Instance);
		cpu.Reset(romBase, 0x4000);

		var before = GC.GetAllocatedBytesForCurrentThread();
		cpu.ExecuteInstructions(8, null, JournalTestBoundary.Instance);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal((ushort)0x1111, bus.ReadWord(0x1000));
		Assert.Equal((ushort)0x2222, bus.ReadWord(0x1002));
		Assert.Equal((ushort)0x3333, bus.ReadWord(0x1004));
		Assert.True(bus.CausalBusExecutor.CpuEventJournal.FullBarriers >= 2);
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
	}

	[Fact]
	public void InstructionFetchFromModifiedChipPageFlushesBeforeFetch()
	{
		const uint romBase = 0x00FC0000;
		var rom = CreateNopRom(256);
		new byte[] { 0x33, 0xFC, 0x4E, 0x71, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		new byte[] { 0x4E, 0xF9, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 12); // JMP $1000
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		bus.WriteWord(0x1000, 0x4AFC); // ILLEGAL until the journal commits NOP.
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);

		cpu.ExecuteInstructions(4, null, JournalTestBoundary.Instance);

		Assert.Equal((ushort)0x4E71, bus.ReadWord(0x1000));
		Assert.True(cpu.State.ProgramCounter >= 0x1000 && cpu.State.ProgramCounter < 0x1010);
		Assert.Equal(-1, cpu.State.LastExceptionVector);
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
	}

	[Fact]
	public void HostGatewayCallbackObservesFlushedJournalWrites()
	{
		const uint romBase = 0x00FC0000;
		var rom = CreateNopRom(256);
		new byte[] { 0x33, 0xFC, 0xC0, 0xDE, 0x00, 0x00, 0x10, 0x00 }.CopyTo(rom, 4);
		var bus = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: true);
		bus.MapReadOnlyMemory(romBase, rom);
		ushort observed = 0;
		var invoked = false;
		bus.RegisterHostGateway(romBase + 12, _ =>
		{
			invoked = true;
			observed = bus.ReadWord(0x1000);
		});
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);

		cpu.ExecuteInstructions(4, null, JournalTestBoundary.Instance);

		Assert.True(invoked);
		Assert.Equal((ushort)0xC0DE, observed);
		Assert.Equal(0, bus.CausalBusExecutor.CpuEventJournal.Count);
	}

	private static byte[] CreateNopRom(int byteCount)
	{
		var rom = new byte[byteCount];
		for (var offset = 0; offset < rom.Length; offset += 2)
		{
			rom[offset] = 0x4E;
			rom[offset + 1] = 0x71;
		}

		return rom;
	}

	private static bool TryEnqueueWord(CpuEventJournal journal, long cycle, uint address)
		=> journal.TryEnqueue(
			cycle,
			CpuJournalInstructionPhase.Operand,
			AmigaBusAccessTarget.ChipRam,
			address,
			AmigaBusAccessKind.CpuDataWrite,
			AmigaBusAccessSize.Word,
			isWrite: true,
			0x1234,
			CpuJournalDependencyFlags.MemoryWrite,
			out _);

	private sealed class JournalTestBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		public static JournalTestBoundary Instance { get; } = new();
		public bool BeforeInstruction() => true;
		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;
		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle) => targetCycle;
		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount) { }
		public void AfterInstruction(long previousCycle, long currentCycle) { }
	}
}
