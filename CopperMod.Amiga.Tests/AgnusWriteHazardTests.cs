using CopperMod.Amiga.Bus;
using Copper68k;

namespace CopperMod.Amiga.Tests;

public sealed class AgnusWriteHazardTests
{
	[Fact]
	public void QuiescentExecutorHasNoChipRamWriteHazard()
	{
		var bus = new AmigaBus(captureBusAccesses: false);

		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(10_000));
		var refreshes = bus.CausalBusExecutor.ChipRamWriteHazardRefreshes;
		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(20_000));
		Assert.Equal(refreshes, bus.CausalBusExecutor.ChipRamWriteHazardRefreshes);
	}

	[Fact]
	public void JournalHeadBecomesWriteHazardAtItsRequestedCycle()
	{
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		Assert.True(executor.TryEnqueueCpuChipWordWrite(
			0x1000,
			0x1234,
			20,
			CpuJournalInstructionPhase.Operand,
			CpuJournalDependencyFlags.None,
			out _));

		Assert.False(executor.MayWriteChipRamBefore(19));
		Assert.True(executor.MayWriteChipRamBefore(20));
		executor.FlushCpuEventJournal();
		Assert.False(executor.MayWriteChipRamBefore(10_000));
	}

	[Theory]
	[InlineData((int)AgnusBusAgendaSource.Cpu)]
	[InlineData((int)AgnusBusAgendaSource.Disk)]
	[InlineData((int)AgnusBusAgendaSource.Blitter)]
	public void PersistentWriteIntentContributesItsEarliestCycle(int sourceValue)
	{
		var source = (AgnusBusAgendaSource)sourceValue;
		var bus = new AmigaBus(captureBusAccesses: false);
		var executor = bus.CausalBusExecutor;
		var intent = new AgnusBusIntent
		{
			Requester = source == AgnusBusAgendaSource.Cpu
				? AmigaBusRequester.Cpu
				: source == AgnusBusAgendaSource.Disk
					? AmigaBusRequester.Disk
					: AmigaBusRequester.Blitter,
			Kind = AmigaBusAccessKind.CpuDataWrite,
			Target = AmigaBusAccessTarget.ChipRam,
			Size = AmigaBusAccessSize.Word,
			Flags = AgnusBusIntentFlags.Pending | AgnusBusIntentFlags.Write,
			Address = 0x2000,
			EarliestCycle = 40
		};
		executor.SetIntent(source, intent);

		Assert.False(executor.MayWriteChipRamBefore(39));
		Assert.True(executor.MayWriteChipRamBefore(40));
	}

	[Theory]
	[InlineData((int)AgnusBusAgendaSource.Disk)]
	[InlineData((int)AgnusBusAgendaSource.Blitter)]
	public void ReadOnlyIntentDoesNotCreateWriteHazard(int sourceValue)
	{
		var source = (AgnusBusAgendaSource)sourceValue;
		var bus = new AmigaBus(captureBusAccesses: false);
		var intent = new AgnusBusIntent
		{
			Requester = source == AgnusBusAgendaSource.Disk
				? AmigaBusRequester.Disk
				: AmigaBusRequester.Blitter,
			Kind = AmigaBusAccessKind.DiskDma,
			Target = AmigaBusAccessTarget.ChipRam,
			Size = AmigaBusAccessSize.Word,
			Flags = AgnusBusIntentFlags.Pending,
			Address = 0x2000,
			EarliestCycle = 40
		};
		bus.CausalBusExecutor.SetIntent(source, intent);

		Assert.False(bus.CausalBusExecutor.MayWriteChipRamBefore(100));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(12)]
	public void ReadOnlyChipRamSegmentKeepsRomBatchActiveAcrossCpuReads(int readCount)
	{
		const uint romBase = 0x00FC0000;
		var program = CreateChipReadProgram(readCount);
		var scalar = new AmigaBus(captureBusAccesses: false, enableDeferredCpuBusBatch: false);
		var segmented = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuChipReadSegments: true);
		scalar.MapReadOnlyMemory(romBase, program);
		segmented.MapReadOnlyMemory(romBase, program);
		for (var word = 0; word < readCount; word++)
		{
			var value = (ushort)(0x4000 + word);
			scalar.WriteWord((uint)(0x1000 + (word * 2)), value);
			segmented.WriteWord((uint)(0x1000 + (word * 2)), value);
		}

		var scalarCpu = M68kCoreFactory.CreateM68000Core(
			scalar, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		var segmentedCpu = M68kCoreFactory.CreateM68000Core(
			segmented, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		scalarCpu.Reset(romBase, 0x4000);
		segmentedCpu.Reset(romBase, 0x4000);
		scalarCpu.State.A[0] = 0x1000;
		segmentedCpu.State.A[0] = 0x1000;

		scalarCpu.ExecuteInstructions(readCount + 2, null, TestBoundary.Instance);
		segmentedCpu.ExecuteInstructions(readCount + 2, null, TestBoundary.Instance);

		Assert.Equal(scalarCpu.State.D[0], segmentedCpu.State.D[0]);
		Assert.Equal(scalarCpu.State.A[0], segmentedCpu.State.A[0]);
		Assert.Equal(scalarCpu.State.ProgramCounter, segmentedCpu.State.ProgramCounter);
		var scalarPipeline = ((IM68000PipelineStateTransfer)scalarCpu).ExportM68000PipelineState();
		var segmentedPipeline = ((IM68000PipelineStateTransfer)segmentedCpu).ExportM68000PipelineState();
		Assert.True(
			scalarCpu.State.Cycles == segmentedCpu.State.Cycles,
			$"reads={readCount},scalar={scalarCpu.State.Cycles},segmented={segmentedCpu.State.Cycles}," +
			$"next={scalarPipeline.NextBusTransferCycle}/{segmentedPipeline.NextBusTransferCycle}," +
			$"ready={scalarPipeline.LastBusReadyCycle}/{segmentedPipeline.LastBusReadyCycle}," +
			$"retire={scalarPipeline.RetireBusCycle}/{segmentedPipeline.RetireBusCycle}," +
			$"count={scalarPipeline.PrefetchCount}/{segmentedPipeline.PrefetchCount}," +
			$"pending={scalarPipeline.HasPendingPrefetch}/{segmentedPipeline.HasPendingPrefetch}," +
			$"prefetch={scalarPipeline.ReadyCycle0},{scalarPipeline.ReadyCycle1}/" +
			$"{segmentedPipeline.ReadyCycle0},{segmentedPipeline.ReadyCycle1}");
		var segmentedScheduler = segmented.CaptureHardwareSchedulerSnapshot();
		Assert.Equal(0, segmentedScheduler.DeferredCpuBusBatchExitChipVisibleAccess);
		Assert.True(segmentedScheduler.DeferredCpuBusBatchInstructions > 1);
	}

	[Theory]
	[InlineData((int)AgnusBusAgendaSource.Disk)]
	[InlineData((int)AgnusBusAgendaSource.Blitter)]
	public void PendingDmaWriterBlocksChipReadSegment(int sourceValue)
	{
		const uint romBase = 0x00FC0000;
		var source = (AgnusBusAgendaSource)sourceValue;
		var bus = new AmigaBus(
			captureBusAccesses: false,
			enableDeferredCpuBusBatch: true,
			enableDeferredCpuChipReadSegments: true);
		bus.MapReadOnlyMemory(romBase, CreateChipReadProgram(1));
		bus.WriteWord(0x1000, 0x6789);
		bus.CausalBusExecutor.SetIntent(source, new AgnusBusIntent
		{
			Requester = source == AgnusBusAgendaSource.Disk
				? AmigaBusRequester.Disk
				: AmigaBusRequester.Blitter,
			Kind = source == AgnusBusAgendaSource.Disk
				? AmigaBusAccessKind.DiskDma
				: AmigaBusAccessKind.Blitter,
			Target = AmigaBusAccessTarget.ChipRam,
			Size = AmigaBusAccessSize.Word,
			Flags = AgnusBusIntentFlags.Pending | AgnusBusIntentFlags.Write,
			Address = 0x2000,
			EarliestCycle = 40
		});
		var cpu = M68kCoreFactory.CreateM68000Core(
			bus, default(AmigaCpuDataAccess), enableCpuBusPhaseTrace: false);
		cpu.Reset(romBase, 0x4000);
		cpu.State.A[0] = 0x1000;

		cpu.ExecuteInstructions(3, null, TestBoundary.Instance);

		Assert.Equal(0x6789u, cpu.State.D[0] & 0xFFFFu);
		var scheduler = bus.CaptureHardwareSchedulerSnapshot();
		Assert.True(scheduler.DeferredCpuBusBatchExitChipVisibleAccess > 0);
	}

	private static byte[] CreateChipReadProgram(int reads)
	{
		var program = new byte[(reads + 16) * 2];
		for (var offset = 0; offset < program.Length; offset += 2)
		{
			program[offset] = 0x4E;
			program[offset + 1] = 0x71;
		}

		for (var read = 0; read < reads; read++)
		{
			var offset = 4 + (read * 2);
			program[offset] = 0x30;
			program[offset + 1] = 0x18; // MOVE.W (A0)+,D0
		}

		return program;
	}

	private sealed class TestBoundary :
		IM68kInstructionBoundary,
		IM68kDeferredCpuBusBatchBoundary
	{
		public static TestBoundary Instance { get; } = new();
		public bool BeforeInstruction() => true;
		public bool TryPrepareDeferredCpuBusBatch(M68kCpuState state) => true;
		public long ClampDeferredCpuBusBatchTarget(M68kCpuState state, long targetCycle) => targetCycle;
		public void AfterDeferredCpuBusBatch(long previousCycle, long currentCycle, int instructionCount) { }
		public void AfterInstruction(long previousCycle, long currentCycle) { }
	}
}
