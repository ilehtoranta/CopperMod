using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Copper68k;
namespace Copper68k.Tests;
public sealed class M68kInterpreterCoreBehaviorTests
{
	[Fact]
	public void MoveqAddqAndDbraUseDocumentedControlFlow()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x70, 0x00); // MOVEQ #0,D0
		Write(bus.Memory, 0x1002, 0x72, 0x02); // MOVEQ #2,D1
		Write(bus.Memory, 0x1004, 0x52, 0x80); // ADDQ.L #1,D0
		Write(bus.Memory, 0x1006, 0x51, 0xC9, 0xFF, 0xFC); // DBRA D1,-4
		Write(bus.Memory, 0x100A, 0x4E, 0x75); // RTS
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		bus.WriteLong(0x1FFC, 0xFFFF_FFFC);
		cpu.State.A[7] = 0x1FFC;
		for (var i = 0; i < 16 && cpu.State.ProgramCounter != 0xFFFF_FFFC; i++)
		{
			cpu.ExecuteInstruction();
		}
		Assert.Equal(0x0000_0003u, cpu.State.D[0]);
		Assert.True(cpu.State.Cycles > 0);
	}
	[Fact]
	public void PrefetchRequestsInstructionWordsSerially()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(4, cycles);
		Assert.Equal(
			new[] { (Address: 0x1000u, Cycle: 0L), (Address: 0x1002u, Cycle: 2L) },
			bus.InstructionFetchCycles.Take(2).ToArray());
	}
	[Fact]
	public void CpuBusPhaseTraceRecordsSerialPrefetchWords()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.ExecuteInstruction();
		var fetches = bus.CpuBusPhases
			.Where(phase => phase.AccessKind == M68kBusAccessKind.CpuInstructionFetch)
			.Take(2)
			.ToArray();
		Assert.Equal(2, fetches.Length);
		Assert.Equal(0x1000u, fetches[0].InstructionProgramCounter);
		Assert.Equal(0x1000u, fetches[0].Address);
		Assert.Equal(M68kOperandSize.Word, fetches[0].Size);
		Assert.Equal(0, fetches[0].RequestedCycle);
		Assert.Equal(2, fetches[0].CompletedCycle);
		Assert.False(fetches[0].IsWrite);
		Assert.Equal(0x1000u, fetches[1].InstructionProgramCounter);
		Assert.Equal(0x1002u, fetches[1].Address);
		Assert.Equal(2, fetches[1].RequestedCycle);
		Assert.Equal(4, fetches[1].CompletedCycle);
	}
	[Fact]
	public void ImmediateExtensionConsumesQueuedPrefetchBeforeNextLookahead()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x08, 0x00, 0x00, 0x0E); // BTST #14,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 0x4000;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(
			new[] { (Address: 0x1000u, Cycle: 0L), (Address: 0x1002u, Cycle: 2L), (Address: 0x1004u, Cycle: 4L) },
			bus.InstructionFetchCycles.Take(3).ToArray());
	}
	[Fact]
	public void TakenBranchFlushesQueuedPrefetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x60, 0x04); // BRA.S target
		Write(bus.Memory, 0x1002, 0x70, 0x01); // stale queued word if branch fails to flush
		Write(bus.Memory, 0x1006, 0x70, 0x02); // target: MOVEQ #2,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(2u, cpu.State.D[0]);
		Assert.Equal(0x1008u, cpu.State.ProgramCounter);
		Assert.Contains(bus.InstructionFetchCycles, fetch => fetch.Address == 0x1006u);
	}
	[Fact]
	public void TakenDbraTargetExtensionWaitBlocksRetirement()
	{
		var bus = new CycleCountingBus
		{
			DelayedInstructionFetchAddress = 0x1002,
			DelayedInstructionFetchOccurrence = 2,
			DelayedInstructionFetchCycles = 4
		};
		Write(bus.Memory, 0x1000, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,$1000
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 1;

		var elapsed = cpu.ExecuteInstruction();
		var fetches = bus.InstructionFetchCycles.ToArray();

		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(new uint[] { 0x1000, 0x1002, 0x1000, 0x1002 }, fetches.Select(fetch => fetch.Address));
		Assert.Equal(12, elapsed);
	}
	[Fact]
	public void TakenDbraCommittedTargetExtensionBlocksRetirementAndInterruptEntry()
	{
		var bus = new CycleCountingBus
		{
			DelayedInstructionFetchAddress = 0x1002,
			DelayedInstructionFetchOccurrence = 2,
			DelayedInstructionFetchCycles = 16
		};
		Write(bus.Memory, 0x0064, 0x00, 0x00, 0x20, 0x00);
		Write(bus.Memory, 0x1000, 0x51, 0xC8, 0xFF, 0xFE);
		Write(bus.Memory, 0x2000, 0x4E, 0x71, 0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		cpu.State.D[0] = 1;

		var elapsed = cpu.ExecuteInstruction();
		var transition = ((IM68000PipelineStateTransfer)cpu).ExportM68000PipelineState();
		var extensionReadyCycle = transition.ReadyCycle1;

		Assert.Equal(extensionReadyCycle, elapsed);
		Assert.Equal(24, elapsed);
		Assert.Equal(2, transition.PrefetchCount);
		Assert.Equal(extensionReadyCycle + 6, transition.ExceptionEntryNotBeforeCycle);

		var interruptPhaseStart = bus.CpuBusPhases.Count;
		cpu.RequestInterrupt(1, 0x0064);
		var lowPcWrite = bus.CpuBusPhases
			.Skip(interruptPhaseStart)
			.First(phase => phase.AccessKind == M68kBusAccessKind.CpuDataWrite);

		Assert.Equal(extensionReadyCycle + 12, lowPcWrite.RequestedCycle);
	}

	[Fact]
	public void TakenDbraIplAtEightCyclesBeforePollIsRecognizedAtThatPoll()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,$1000
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 2;
		var recognition = Assert.IsAssignableFrom<IM68000InterruptRecognition>(cpu);

		cpu.ExecuteInstruction();
		var firstPoll = recognition.LastInterruptSampleCycle;
		var pinAssertCycle = firstPoll - 8;

		Assert.True(recognition.HasRecognizedInterrupt(pinAssertCycle));
	}
	[Theory]
	[InlineData(1, 2L, 12)]
	[InlineData(2, 6L, 10)]
	public void TakenDbraKeepsImportedPhysicalBusTailAcrossQueueReplacement(
		int entryQueueCount,
		long secondWordReadyCycle,
		int expectedElapsed)
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,$1000
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 1;

		var pipeline = (IM68000PipelineStateTransfer)cpu;
		pipeline.ImportM68000PipelineState(new M68000PipelineState(
			PrefetchAddress: 0x1000,
			Word0: 0x51C8,
			Word1: 0xFFFE,
			ReadyCycle0: 0,
			ReadyCycle1: secondWordReadyCycle,
			DeferredBatchEligible0: false,
			DeferredBatchEligible1: false,
			PrefetchCount: entryQueueCount,
			ConsumeWithoutPrefetch: false,
			SkipRetirePrefetchTopUp: false,
			NextBusTransferCycle: 6,
			LastBusReadyCycle: 6,
			RetireBusCycle: 0,
			PendingInternalCycles: 0));

		var elapsed = cpu.ExecuteInstruction();
		var after = pipeline.ExportM68000PipelineState();

		Assert.Equal(expectedElapsed, elapsed);
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.True(after.NextBusTransferCycle >= 6, $"tail moved backwards: {after.NextBusTransferCycle}");
		Assert.True(after.LastBusReadyCycle >= 6, $"ready moved backwards: {after.LastBusReadyCycle}");
		Assert.True(after.PrefetchCount >= 1, "taken DBRA must leave its target opcode available");
		Assert.Equal(0x1000u, after.PrefetchAddress);
	}
	[Fact]
	public void TakenDbraCancelsOnlyCancellablePendingPrefetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,$1000
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 1;

		var pipeline = (IM68000PipelineStateTransfer)cpu;
		pipeline.ImportM68000PipelineState(new M68000PipelineState(
			PrefetchAddress: 0x1000,
			Word0: 0x51C8,
			Word1: 0xFFFE,
			ReadyCycle0: 0,
			ReadyCycle1: 2,
			DeferredBatchEligible0: false,
			DeferredBatchEligible1: false,
			PrefetchCount: 2,
			ConsumeWithoutPrefetch: false,
			SkipRetirePrefetchTopUp: false,
			NextBusTransferCycle: 6,
			LastBusReadyCycle: 6,
			RetireBusCycle: 0,
			PendingInternalCycles: 0,
			HasPendingPrefetch: true,
			PendingPrefetchAddress: 0x1004,
			PendingPrefetchEarliestCycle: 10));

		cpu.ExecuteInstruction();
		var after = pipeline.ExportM68000PipelineState();

		Assert.False(after.HasPendingPrefetch);
		Assert.Equal(0x1000u, after.PrefetchAddress);
		Assert.True(after.NextBusTransferCycle >= 6, $"tail moved backwards: {after.NextBusTransferCycle}");
	}
	[Fact]
	public void ExpiredDbraReadsAbandonedTargetBeforeFallthroughRefill()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x51, 0xC8, 0x00, 0x0E); // DBRA D0,$1010
		Write(bus.Memory, 0x1004, 0x4E, 0x71, 0x4E, 0x71); // fallthrough
		Write(bus.Memory, 0x1010, 0x7E, 0xAD);             // abandoned target
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 0;

		var elapsed = cpu.ExecuteInstruction();

		Assert.Equal(14, elapsed);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		Assert.Equal(0xFFFFu, cpu.State.D[0]);
		Assert.Equal(
			new uint[] { 0x1000, 0x1002, 0x1010, 0x1004, 0x1006 },
			bus.InstructionFetchCycles.Select(fetch => fetch.Address));
	}
	[Fact]
	public void ExpiredDbraWaitsForCommittedFallthroughExtensionRead()
	{
		var bus = new CycleCountingBus
		{
			DelayedInstructionFetchAddress = 0x1006,
			DelayedInstructionFetchCycles = 8
		};
		Write(bus.Memory, 0x1000, 0x51, 0xC8, 0x00, 0x0E); // DBRA D0,$1010
		Write(bus.Memory, 0x1004, 0x4E, 0x71, 0x4E, 0x71); // fallthrough
		Write(bus.Memory, 0x1010, 0x7E, 0xAD);             // abandoned target
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 0;

		var elapsed = cpu.ExecuteInstruction();

		Assert.Equal(18, elapsed);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		Assert.Equal(0x1006u, bus.InstructionFetchCycles[^1].Address);
	}
	[Theory]
	[InlineData((int)M68kOpcodePlanDispatch.Scalar)]
	[InlineData((int)M68kOpcodePlanDispatch.KindTable)]
	[InlineData((int)M68kOpcodePlanDispatch.PackedPlan)]
	public void MoveWordPredecrementAddressErrorFramesPrefetchedFallthroughWord(int dispatchValue)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0x312E, 0x0000, 0x7EC5)); // MOVE.W 0(A6),-(A0)
		bus.WriteLong(0x000C, 0x0000_4000);
		Write(bus.Memory, 0x2000, 0xA1, 0xCA);
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var cpu = new M68kInterpreter(
			bus,
			new M68kCpuState(),
			instructionFrequency: null,
			enableInstructionFetchWindow: true,
			enableCpuBusPhaseTrace: true,
			enableOpcodePlan: dispatch != M68kOpcodePlanDispatch.Scalar,
			opcodePlanDispatch: dispatch);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.A[0] = 0x3003;
		cpu.State.A[6] = 0x2000;

		cpu.ExecuteInstruction();

		Assert.Equal(3, cpu.State.LastExceptionVector);
		Assert.Equal(0x3001u, cpu.State.A[0]);
		Assert.Equal(0x7EC5, ReadWord(bus.Memory, 0x7FF8));
	}

	[Fact]
	public void CpuDataReadWaitsBehindPendingPrefetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x30, 0x10); // MOVE.W (A0),D0
		Write(bus.Memory, 0x2000, 0x12, 0x34);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFF);
		Assert.Contains((Address: 0x2000u, Cycle: 4L), bus.DataReadCycles);
	}
	[Fact]
	public void CpuBusPhaseTraceRecordsDataReadBehindPendingPrefetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x30, 0x10); // MOVE.W (A0),D0
		Write(bus.Memory, 0x2000, 0x12, 0x34);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.ExecuteInstruction();
		var dataRead = Assert.Single(bus.CpuBusPhases, phase => phase.AccessKind == M68kBusAccessKind.CpuDataRead);
		Assert.Equal(0x1000u, dataRead.InstructionProgramCounter);
		Assert.Equal(0x2000u, dataRead.Address);
		Assert.Equal(M68kOperandSize.Word, dataRead.Size);
		Assert.Equal(4, dataRead.RequestedCycle);
		Assert.Equal(6, dataRead.CompletedCycle);
		Assert.False(dataRead.IsWrite);
	}
	[Fact]
	public void CpuBusPhaseTraceRecordsLongWriteSpan()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x20, 0x80); // MOVE.L D0,(A0)
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[0] = 0x2000;
		cpu.ExecuteInstruction();
		var dataWrite = Assert.Single(bus.CpuBusPhases, phase => phase.AccessKind == M68kBusAccessKind.CpuDataWrite);
		Assert.Equal(0x1000u, dataWrite.InstructionProgramCounter);
		Assert.Equal(0x2000u, dataWrite.Address);
		Assert.Equal(M68kOperandSize.Long, dataWrite.Size);
		Assert.Equal(4, dataWrite.RequestedCycle);
		Assert.Equal(8, dataWrite.CompletedCycle);
		Assert.True(dataWrite.IsWrite);
		Assert.Equal(0x1234_5678u, ReadBigEndianUInt32(bus.Memory, 0x2000, "long write"));
	}
	[Fact]
	public void FetchLongConsumesSerializedPrefetchWords()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x20, 0x3C, 0x12, 0x34, 0x56, 0x78); // MOVE.L #$12345678,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(
			new[] { (Address: 0x1000u, Cycle: 0L), (Address: 0x1002u, Cycle: 2L), (Address: 0x1004u, Cycle: 4L), (Address: 0x1006u, Cycle: 6L) },
			bus.InstructionFetchCycles.Take(4).ToArray());
	}
	[Fact]
	public void PlannedInterpreterMatchesScalarForFullContactTransformLoop()
	{
		var program = Words(
			0x2018, // MOVE.L (A0)+,D0
			0x221A, // MOVE.L (A2)+,D1
			0xB183, // EOR.L D0,D3
			0xB383, // EOR.L D1,D3
			0xC087, // AND.L D7,D0
			0xC287, // AND.L D7,D1
			0xD080, // ADD.L D0,D0
			0x8081, // OR.L D1,D0
			0x22C0, // MOVE.L D0,(A1)+
			0x51CA, 0xFFEC, // DBRA D2,loop
			0x60E8); // BRA.S loop
		var scalar = CreateParityCpu(program, enableOpcodePlan: false);
		var planned = CreateParityCpu(program, enableOpcodePlan: true);
		SetupTransformParityState(scalar.Cpu.State, scalar.Bus);
		SetupTransformParityState(planned.Cpu.State, planned.Bus);
		planned.Cpu.PlannedInterpreterCountersEnabled = true;
		ExecuteBoth(scalar.Cpu, planned.Cpu, 64);
		AssertParity(scalar, planned);
		var counters = planned.Cpu.CapturePlannedInterpreterCounters();
		Assert.True(counters.FastInstructions > 0);
		Assert.True(counters.MoveInstructions > 0);
		Assert.True(counters.RegisterArithmeticInstructions > 0);
		Assert.True(counters.DbccInstructions > 0);
	}
	[Fact]
	public void PlannedInterpreterUsesExactHotFullContactShapePlans()
	{
		Assert.Equal(
			M68kOpcodePlanKind.MoveLongPostincrementToData,
			M68kOpcodePlanTable.Kinds[0x2018]);
		Assert.Equal(
			M68kOpcodePlanKind.MoveLongPostincrementToData,
			M68kOpcodePlanTable.Kinds[0x221A]);
		Assert.Equal(
			M68kOpcodePlanKind.MoveLongDataToPostincrement,
			M68kOpcodePlanTable.Kinds[0x22C0]);
		Assert.Equal(
			M68kOpcodePlanKind.MoveLongDataToAbsoluteLong,
			M68kOpcodePlanTable.Kinds[0x23C0]);
		Assert.Equal(
			M68kOpcodePlanKind.ShortUnconditionalBranch,
			M68kOpcodePlanTable.Kinds[0x60FE]);
		Assert.Equal(
			M68kOpcodePlanKind.QuickLongDataRegister,
			M68kOpcodePlanTable.Kinds[0x5482]);
		Assert.Equal(
			M68kOpcodePlanKind.DataRegisterLongEorToDestination,
			M68kOpcodePlanTable.Kinds[0xB183]);
		Assert.Equal(
			M68kOpcodePlanKind.DataRegisterLongAndToRegister,
			M68kOpcodePlanTable.Kinds[0xC087]);
		Assert.Equal(
			M68kOpcodePlanKind.DataRegisterLongAddToRegister,
			M68kOpcodePlanTable.Kinds[0xD080]);
		Assert.Equal(
			M68kOpcodePlanKind.DataRegisterLongOrToRegister,
			M68kOpcodePlanTable.Kinds[0x8081]);
	}
	[Fact]
	public void PlannedPackedPlansMatchKindTableForEveryOpcode()
	{
		for (var opcode = 0; opcode <= 0xFFFF; opcode++)
		{
			var word = (ushort)opcode;
			var kind = M68kOpcodePlanTable.Kinds[word];
			Assert.Equal(kind, M68kOpcodePlanTable.PackedPlans[word].Kind);
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedDispatchVariantMatchesScalarForFullContactTransformLoop(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var program = Words(
			0x2018, // MOVE.L (A0)+,D0
			0x221A, // MOVE.L (A2)+,D1
			0xB183, // EOR.L D0,D3
			0xB383, // EOR.L D1,D3
			0xC087, // AND.L D7,D0
			0xC287, // AND.L D7,D1
			0xD080, // ADD.L D0,D0
			0x8081, // OR.L D1,D0
			0x22C0, // MOVE.L D0,(A1)+
			0x51CA, 0xFFEC, // DBRA D2,loop
			0x60E8); // BRA.S loop
		var scalar = CreateParityCpu(program, enableOpcodePlan: false);
		var planned = CreateParityCpu(program, enableOpcodePlan: true, dispatch);
		SetupTransformParityState(scalar.Cpu.State, scalar.Bus);
		SetupTransformParityState(planned.Cpu.State, planned.Bus);
		planned.Cpu.PlannedInterpreterCountersEnabled = true;
		ExecuteBoth(scalar.Cpu, planned.Cpu, 64);
		AssertParity(scalar, planned);
		Assert.True(planned.Cpu.CapturePlannedInterpreterCounters().FastInstructions > 0);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedMovePostincrementFastShapesMatchScalar(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var opcodes = new ushort[]
		{
			0x12D8, // MOVE.B (A0)+,(A1)+
			0x32D8, // MOVE.W (A0)+,(A1)+
			0x22D8, // MOVE.L (A0)+,(A1)+
			0x1018, // MOVE.B (A0)+,D0
			0x3018, // MOVE.W (A0)+,D0
			0x2018, // MOVE.L (A0)+,D0
			0x12C0, // MOVE.B D0,(A1)+
			0x32C0, // MOVE.W D0,(A1)+
			0x22C0  // MOVE.L D0,(A1)+
		};
		foreach (var opcode in opcodes)
		{
			var scalar = CreateParityCpu(Words(opcode), enableOpcodePlan: false);
			var planned = CreateParityCpu(Words(opcode), enableOpcodePlan: true, dispatch);
			SetupMoveFastShapeParityState(scalar.Cpu.State, scalar.Bus);
			SetupMoveFastShapeParityState(planned.Cpu.State, planned.Bus);
			ExecuteBoth(scalar.Cpu, planned.Cpu, 1);
			AssertParity(scalar, planned);
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedMovePostincrementFastShapesPreserveSpecialIncrements(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var a7Source = CreateParityCpu(Words(0x101F), enableOpcodePlan: true, dispatch); // MOVE.B (A7)+,D0
		a7Source.Cpu.State.A[7] = 0x4000;
		a7Source.Bus.Memory[0x4000] = 0x5A;
		a7Source.Cpu.ExecuteInstruction();
		Assert.Equal(0x4002u, a7Source.Cpu.State.A[7]);
		Assert.Equal(0x0000_005Au, a7Source.Cpu.State.D[0]);
		var sameRegister = CreateParityCpu(Words(0x20D8), enableOpcodePlan: true, dispatch); // MOVE.L (A0)+,(A0)+
		sameRegister.Cpu.State.A[0] = 0x2000;
		sameRegister.Bus.WriteLong(0x2000, 0x1122_3344);
		sameRegister.Cpu.ExecuteInstruction();
		Assert.Equal(0x2008u, sameRegister.Cpu.State.A[0]);
		Assert.Equal(0x1122_3344u, ReadLong(sameRegister.Bus.Memory, 0x2004));
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedMovePostincrementFastShapesPreserveAddressErrorOrdering(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var oddSource = CreateParityCpu(Words(0x3018), enableOpcodePlan: true, dispatch); // MOVE.W (A0)+,D0
		oddSource.Bus.WriteLong(0x000C, 0x0000_4000);
		oddSource.Cpu.State.A[0] = 0x2001;
		oddSource.Cpu.State.D[0] = 0xAAAA_5555;
		oddSource.Cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		oddSource.Cpu.ExecuteInstruction();
		Assert.Equal(3, oddSource.Cpu.State.LastExceptionVector);
		Assert.Equal(0x0000_4000u, oddSource.Cpu.State.ProgramCounter);
		Assert.Equal(0x2003u, oddSource.Cpu.State.A[0]);
		Assert.Equal(0xAAAA_5555u, oddSource.Cpu.State.D[0]);
		Assert.Equal(0x1002u, oddSource.Cpu.State.LastExceptionStackedProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry,
			oddSource.Cpu.State.LastExceptionStatusRegister);
		var oddWordDestination = CreateParityCpu(Words(0x32C0), enableOpcodePlan: true, dispatch); // MOVE.W D0,(A1)+
		oddWordDestination.Bus.WriteLong(0x000C, 0x0000_4000);
		oddWordDestination.Cpu.State.A[1] = 0x3001;
		oddWordDestination.Cpu.State.D[0] = 0x0000_8001;
		oddWordDestination.Cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		oddWordDestination.Cpu.ExecuteInstruction();
		Assert.Equal(3, oddWordDestination.Cpu.State.LastExceptionVector);
		Assert.Equal(0x3001u, oddWordDestination.Cpu.State.A[1]);
		Assert.Equal(0x1004u, oddWordDestination.Cpu.State.LastExceptionStackedProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative,
			oddWordDestination.Cpu.State.LastExceptionStatusRegister);
		var oddLongDestination = CreateParityCpu(Words(0x22D8), enableOpcodePlan: true, dispatch); // MOVE.L (A0)+,(A1)+
		oddLongDestination.Bus.WriteLong(0x000C, 0x0000_4000);
		oddLongDestination.Bus.WriteLong(0x2000, 0x0000_8000);
		oddLongDestination.Cpu.State.A[0] = 0x2000;
		oddLongDestination.Cpu.State.A[1] = 0x3001;
		oddLongDestination.Cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		oddLongDestination.Cpu.ExecuteInstruction();
		Assert.Equal(3, oddLongDestination.Cpu.State.LastExceptionVector);
		Assert.Equal(0x2004u, oddLongDestination.Cpu.State.A[0]);
		Assert.Equal(0x3001u, oddLongDestination.Cpu.State.A[1]);
		Assert.Equal(0x1004u, oddLongDestination.Cpu.State.LastExceptionStackedProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative,
			oddLongDestination.Cpu.State.LastExceptionStatusRegister);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedMoveDisplacementToDataFastShapeMatchesScalar(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var programs = new[]
		{
			Words(0x1228, 0x0003), // MOVE.B 3(A0),D1
			Words(0x3228, 0x0002), // MOVE.W 2(A0),D1
			Words(0x2228, 0x0000), // MOVE.L 0(A0),D1
			Words(0x322E, 0x0002)  // MOVE.W 2(A6),D1
		};
		foreach (var program in programs)
		{
			var scalar = CreateParityCpu(program, enableOpcodePlan: false);
			var planned = CreateParityCpu(program, enableOpcodePlan: true, dispatch);
			SetupMoveDisplacementToDataParityState(scalar.Cpu.State, scalar.Bus);
			SetupMoveDisplacementToDataParityState(planned.Cpu.State, planned.Bus);
			ExecuteBoth(scalar.Cpu, planned.Cpu, 1);
			AssertParity(scalar, planned);
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedMoveDisplacementToDataFastShapePreservesSourceAddressErrorOrdering(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var cpu = CreateParityCpu(Words(0x3028, 0x0001), enableOpcodePlan: true, dispatch); // MOVE.W 1(A0),D0
		cpu.Bus.WriteLong(0x000C, 0x0000_4000);
		cpu.Cpu.State.A[0] = 0x2000;
		cpu.Cpu.State.D[0] = 0xAAAA_5555;
		cpu.Cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.Cpu.ExecuteInstruction();
		Assert.Equal(3, cpu.Cpu.State.LastExceptionVector);
		Assert.Equal(0x0000_4000u, cpu.Cpu.State.ProgramCounter);
		Assert.Equal(0x2000u, cpu.Cpu.State.A[0]);
		Assert.Equal(0xAAAA_5555u, cpu.Cpu.State.D[0]);
		Assert.Equal(0x1002u, cpu.Cpu.State.LastExceptionStackedProgramCounter);
		Assert.Equal(
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry,
			cpu.Cpu.State.LastExceptionStatusRegister);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedRegisterArithmeticEaToDataFastShapesMatchScalar(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var lines = new[] { 0x8, 0x9, 0xB, 0xC, 0xD };
		var sourceModes = new[] { 2, 3, 5 };
		for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			for (var opmode = 0; opmode <= 2; opmode++)
			{
				foreach (var mode in sourceModes)
				{
					var opcode = RegisterArithmeticOpcode(lines[lineIndex], register: 1, opmode, mode, eaRegister: 0);
					var program = mode == 5 ? Words(opcode, 0x0002) : Words(opcode);
					var scalar = CreateParityCpu(program, enableOpcodePlan: false);
					var planned = CreateParityCpu(program, enableOpcodePlan: true, dispatch);
					SetupRegisterArithmeticEaParityState(scalar.Cpu.State, scalar.Bus);
					SetupRegisterArithmeticEaParityState(planned.Cpu.State, planned.Bus);
					ExecuteBoth(scalar.Cpu, planned.Cpu, 1);
					AssertParity(scalar, planned);
				}
			}
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedRegisterArithmeticEaToDataFastShapesPreserveSourceAddressErrorOrdering(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var initialStatus = (ushort)(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry);
		var oddIndirectWord = CreateParityCpu(
			Words(RegisterArithmeticOpcode(0xD, register: 0, opmode: 1, mode: 2, eaRegister: 0)),
			enableOpcodePlan: true,
			dispatch);
		SetupFaultingArithmeticSource(oddIndirectWord, initialStatus, 0x2001);
		oddIndirectWord.Cpu.ExecuteInstruction();
		AssertFaultingArithmeticSource(oddIndirectWord, initialStatus, expectedA0: 0x2001, expectedStackedPc: 0x1002);
		var oddIndirectLong = CreateParityCpu(
			Words(RegisterArithmeticOpcode(0xD, register: 0, opmode: 2, mode: 2, eaRegister: 0)),
			enableOpcodePlan: true,
			dispatch);
		SetupFaultingArithmeticSource(oddIndirectLong, initialStatus, 0x2001);
		oddIndirectLong.Cpu.ExecuteInstruction();
		AssertFaultingArithmeticSource(oddIndirectLong, initialStatus, expectedA0: 0x2001, expectedStackedPc: 0x1002);
		var oddPostincrementWord = CreateParityCpu(
			Words(RegisterArithmeticOpcode(0xD, register: 0, opmode: 1, mode: 3, eaRegister: 0)),
			enableOpcodePlan: true,
			dispatch);
		SetupFaultingArithmeticSource(oddPostincrementWord, initialStatus, 0x2001);
		oddPostincrementWord.Cpu.ExecuteInstruction();
		AssertFaultingArithmeticSource(oddPostincrementWord, initialStatus, expectedA0: 0x2003, expectedStackedPc: 0x1002);
		var oddPostincrementLong = CreateParityCpu(
			Words(RegisterArithmeticOpcode(0xD, register: 0, opmode: 2, mode: 3, eaRegister: 0)),
			enableOpcodePlan: true,
			dispatch);
		SetupFaultingArithmeticSource(oddPostincrementLong, initialStatus, 0x2001);
		oddPostincrementLong.Cpu.ExecuteInstruction();
		AssertFaultingArithmeticSource(oddPostincrementLong, initialStatus, expectedA0: 0x2001, expectedStackedPc: 0x1002);
		var oddDisplacementWord = CreateParityCpu(
			Words(RegisterArithmeticOpcode(0xD, register: 0, opmode: 1, mode: 5, eaRegister: 0), 0x0001),
			enableOpcodePlan: true,
			dispatch);
		SetupFaultingArithmeticSource(oddDisplacementWord, initialStatus, 0x2000);
		oddDisplacementWord.Cpu.ExecuteInstruction();
		AssertFaultingArithmeticSource(oddDisplacementWord, initialStatus, expectedA0: 0x2000, expectedStackedPc: 0x1002);
	}
	[Fact]
	public void LeaDisplacementToAddressUsesFastAddressOnlyPath()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(
			0x43E8, 0x0008, // LEA 8(A0),A1
			0x4FE8, 0xFFFC)); // LEA -4(A0),A7
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.A[0] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		bus.Accesses.Clear();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x2008u, cpu.State.A[1]);
		Assert.Equal(0x1FFCu, cpu.State.A[7]);
		Assert.Equal(
			M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry,
			cpu.State.StatusRegister);
		Assert.DoesNotContain(bus.Accesses, access => access.Kind == M68kBusAccessKind.CpuDataRead);
	}

	[Theory]
	[InlineData(0x4E71, (int)M68000MicrosequenceClass.SequentialFinalPrefetch)]
	[InlineData(0x7001, (int)M68000MicrosequenceClass.SequentialFinalPrefetch)]
	[InlineData(0x60FE, (int)M68000MicrosequenceClass.TakenShortBranchFullRefill)]
	[InlineData(0x3A13, (int)M68000MicrosequenceClass.MoveSourceReadThenFinalPrefetch)]
	[InlineData(0x34C5, (int)M68000MicrosequenceClass.MoveWriteThenFinalPrefetch)]
	[InlineData(0x3545, (int)M68000MicrosequenceClass.MoveWriteThenFinalPrefetch)]
	[InlineData(0x3290, (int)M68000MicrosequenceClass.MoveSourceReadWriteThenFinalPrefetch)]
	[InlineData(0x1010, (int)M68000MicrosequenceClass.MoveSourceReadThenFinalPrefetch)]
	[InlineData(0x12C0, (int)M68000MicrosequenceClass.MoveWriteThenFinalPrefetch)]
	[InlineData(0x1290, (int)M68000MicrosequenceClass.MoveSourceReadWriteThenFinalPrefetch)]
	[InlineData(0x1228, (int)M68000MicrosequenceClass.MoveSourceReadThenFinalPrefetch)]
	[InlineData(0x1340, (int)M68000MicrosequenceClass.MoveWriteThenFinalPrefetch)]
	[InlineData(0x3218, (int)M68000MicrosequenceClass.MoveSourceReadThenFinalPrefetch)]
	[InlineData(0x1218, (int)M68000MicrosequenceClass.MoveSourceReadThenFinalPrefetch)]
	[InlineData(0x3298, (int)M68000MicrosequenceClass.MoveSourceReadWriteThenFinalPrefetch)]
	[InlineData(0x12D8, (int)M68000MicrosequenceClass.MoveSourceReadWriteThenFinalPrefetch)]
	[InlineData(0x3300, (int)M68000MicrosequenceClass.MovePrefetchThenPredecrementWrite)]
	[InlineData(0x1300, (int)M68000MicrosequenceClass.MovePrefetchThenPredecrementWrite)]
	[InlineData(0x3310, (int)M68000MicrosequenceClass.MovePrefetchThenPredecrementWrite)]
	[InlineData(0x1318, (int)M68000MicrosequenceClass.MovePrefetchThenPredecrementWrite)]
	[InlineData(0x3328, (int)M68000MicrosequenceClass.MovePrefetchThenPredecrementWrite)]
	[InlineData(0x1328, (int)M68000MicrosequenceClass.MovePrefetchThenPredecrementWrite)]
	[InlineData(0x0253, (int)M68000MicrosequenceClass.ImmediateMemoryReadPrefetchWrite)]
	public void PackedPlansClassifyProvenMicrosequences(int opcode, int expectedClass)
	{
		Assert.Equal(
			(M68000MicrosequenceClass)expectedClass,
			M68kOpcodePlanTable.PackedPlans[opcode].Microsequence);
	}

	[Theory]
	[InlineData(0x0C40, 0x0001)]
	[InlineData(0x0640, 0x0001)]
	[InlineData(0x0440, 0x0001)]
	[InlineData(0x0240, 0x00FF)]
	[InlineData(0x0040, 0x0100)]
	[InlineData(0x0A40, 0x0001)]
	[InlineData(0x0800, 0x0000)]
	public void OneExtensionRegisterOperationsUseTypedExactDescriptor(int opcode, int extension)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words((ushort)opcode, (ushort)extension));

		Assert.True(M68kDecoder.TryDecode(bus, 0x1000, out var instruction, out var reason));
		Assert.Equal(M68kJitBailoutReason.None, reason);
		var descriptor = M68kJitCore.GetClassicM68000Microsequence(in instruction);

		Assert.Equal(M68000CompiledMicrosequenceKind.SequentialOneExtension, descriptor.Kind);
		Assert.Equal(1, descriptor.ExtensionWordsToConsume);
		Assert.True(descriptor.CanCompileExactly);
		Assert.NotEqual(M68kInstructionFamily.Unknown, descriptor.Family);
	}

	[Theory]
	[InlineData(0x303C, 0x0001, (int)M68000CompiledMicrosequenceKind.MoveWordImmediateToData)]
	[InlineData(0x103C, 0x0001, (int)M68000CompiledMicrosequenceKind.MoveByteImmediateToData)]
	public void ImmediateToDataMoveUsesDedicatedTwoPrefetchDescriptor(int opcode, int extension, int expectedKind)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words((ushort)opcode, (ushort)extension));

		Assert.True(M68kDecoder.TryDecode(bus, 0x1000, out var instruction, out _));
		var descriptor = M68kJitCore.GetClassicM68000Microsequence(in instruction);

		Assert.Equal((M68000CompiledMicrosequenceKind)expectedKind, descriptor.Kind);
		Assert.Equal(1, descriptor.ExtensionWordsToConsume);
		Assert.Equal(2, descriptor.MoveFinalPrefetchCount);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedSequentialRetireMatchesScalarPrefetchBusOrder(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x5480, // ADDQ.L #2,D0
			0xD081, // ADD.L D1,D0
			0x4E71); // NOP
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);

		for (var instruction = 0; instruction < 5; instruction++)
		{
			scalar.ExecuteInstruction();
			planned.ExecuteInstruction();
			AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedSequentialRetireFallsBackToGeneralForOneWordQueue(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, Words(0x7001, 0x7202, 0x4E71));
		Write(plannedBus.Memory, 0x1000, Words(0x7001, 0x7202, 0x4E71));
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		var scalarPipeline = (IM68000PipelineStateTransfer)scalar;
		var plannedPipeline = (IM68000PipelineStateTransfer)planned;
		var entry = scalarPipeline.ExportM68000PipelineState() with
		{
			PrefetchCount = 1,
			DeferredBatchEligible1 = false
		};
		scalarPipeline.ImportM68000PipelineState(in entry);
		plannedPipeline.ImportM68000PipelineState(in entry);

		scalar.ExecuteInstruction();
		planned.ExecuteInstruction();

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedSequentialRetireConsumesPendingPrefetchLikeScalar(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, Words(0x7001, 0x7202, 0x4E71));
		Write(plannedBus.Memory, 0x1000, Words(0x7001, 0x7202, 0x4E71));
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		var scalarPipeline = (IM68000PipelineStateTransfer)scalar;
		var plannedPipeline = (IM68000PipelineStateTransfer)planned;
		var entry = scalarPipeline.ExportM68000PipelineState() with
		{
			HasPendingPrefetch = true,
			PendingPrefetchAddress = 0x1004,
			PendingPrefetchEarliestCycle = 10
		};
		scalarPipeline.ImportM68000PipelineState(in entry);
		plannedPipeline.ImportM68000PipelineState(in entry);

		scalar.ExecuteInstruction();
		planned.ExecuteInstruction();

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
		Assert.Equal(
			((IM68000InterruptRecognition)scalar).LastInterruptSampleCycle,
			((IM68000InterruptRecognition)planned).LastInterruptSampleCycle);
	}
	[Theory]
	[InlineData(false, (int)M68kOpcodePlanDispatch.KindTable)]
	[InlineData(true, (int)M68kOpcodePlanDispatch.KindTable)]
	[InlineData(true, (int)M68kOpcodePlanDispatch.PackedPlan)]
	public void ImmediateBtstDataRegisterPrefetchIsIndependentOfTestedValue(
		bool enableOpcodePlan,
		int dispatchValue)
	{
		var clearBus = new CycleCountingBus();
		var setBus = new CycleCountingBus();
		var program = Words(0x0800, 0x0002, 0x6702, 0x4E71); // BTST #2,D0; BEQ.S
		Write(clearBus.Memory, 0x1000, program);
		Write(setBus.Memory, 0x1000, program);
		var clear = CreateCycleParityCpu(
			clearBus,
			enableOpcodePlan,
			(M68kOpcodePlanDispatch)dispatchValue);
		var set = CreateCycleParityCpu(
			setBus,
			enableOpcodePlan,
			(M68kOpcodePlanDispatch)dispatchValue);
		clear.State.D[0] = 0;
		set.State.D[0] = 4;

		clear.ExecuteInstruction();
		set.ExecuteInstruction();

		Assert.Equal(
			clearBus.CpuBusPhases.Select(phase => new
			{
				phase.InstructionProgramCounter,
				phase.Address,
				phase.RequestedCycle,
				phase.CompletedCycle,
				phase.AccessKind,
				phase.IsWrite
			}),
			setBus.CpuBusPhases.Select(phase => new
			{
				phase.InstructionProgramCounter,
				phase.Address,
				phase.RequestedCycle,
				phase.CompletedCycle,
				phase.AccessKind,
				phase.IsWrite
			}));
		var fallthroughFetch = Assert.Single(clearBus.CpuBusPhases, phase =>
			phase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.Address == 0x1004);
		Assert.Equal(4, fallthroughFetch.RequestedCycle);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void ImmediateBtstPhysicalPrefetchIsIndependentOfFollowingBatchedBranch(int dispatchValue)
	{
		var clearBus = new CycleCountingBus();
		var setBus = new CycleCountingBus();
		var program = Words(0x0800, 0x0002, 0x6702, 0x3085, 0x4E71);
		Write(clearBus.Memory, 0x1000, program);
		Write(setBus.Memory, 0x1000, program);
		var clear = CreateCycleParityCpu(clearBus, true, (M68kOpcodePlanDispatch)dispatchValue);
		var set = CreateCycleParityCpu(setBus, true, (M68kOpcodePlanDispatch)dispatchValue);
		clear.State.D[0] = 0;
		set.State.D[0] = 4;

		Assert.Equal(2, ((IM68kBatchCore)clear).ExecuteInstructions(2, long.MaxValue, new BusAccessBatchBoundary()));
		Assert.Equal(2, ((IM68kBatchCore)set).ExecuteInstructions(2, long.MaxValue, new BusAccessBatchBoundary()));

		Assert.Equal(
			clearBus.CpuBusPhases.Where(phase => phase.InstructionProgramCounter == 0x1000).Select(phase =>
				(phase.Address, phase.RequestedCycle, phase.CompletedCycle, phase.AccessKind)),
			setBus.CpuBusPhases.Where(phase => phase.InstructionProgramCounter == 0x1000).Select(phase =>
				(phase.Address, phase.RequestedCycle, phase.CompletedCycle, phase.AccessKind)));
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedShortBranchRetireMatchesScalarPrefetchBusOrder(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, Words(0x60FE)); // BRA.S self
		Write(plannedBus.Memory, 0x1000, Words(0x60FE));
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);

		for (var instruction = 0; instruction < 4; instruction++)
		{
			scalar.ExecuteInstruction();
			planned.ExecuteInstruction();
			AssertCycleParity(scalar, scalarBus, planned, plannedBus);
			Assert.Equal(
				((IM68000InterruptRecognition)scalar).LastInterruptSampleCycle,
				((IM68000InterruptRecognition)planned).LastInterruptSampleCycle);
		}
	}
	[Theory]
	[InlineData((int)M68kOpcodePlanDispatch.KindTable)]
	[InlineData((int)M68kOpcodePlanDispatch.PackedPlan)]
	public void ExecuteInstructionDoesNotEnterDeferredBatch(int dispatchValue)
	{
		var bus = new DeferredRetirementBus();
		Write(bus.Memory, 0x1000, Words(0x7001, 0x7202, 0x4E71));
		var cpu = new M68kInterpreter(
			bus,
			new M68kCpuState(),
			instructionFrequency: null,
			enableInstructionFetchWindow: true,
			enableCpuBusPhaseTrace: false,
			enableOpcodePlan: true,
			opcodePlanDispatch: (M68kOpcodePlanDispatch)dispatchValue);
		cpu.Reset(0x1000, 0x8000);
		var pipeline = (IM68000PipelineStateTransfer)cpu;
		var queued = pipeline.ExportM68000PipelineState() with
		{
			PrefetchAddress = 0x1000,
			Word0 = 0x7001,
			Word1 = 0x7202,
			DeferredBatchEligible0 = true,
			DeferredBatchEligible1 = true,
			PrefetchCount = 2
		};
		pipeline.ImportM68000PipelineState(in queued);

		cpu.ExecuteInstruction();

		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(0u, cpu.State.D[1]);
		Assert.Equal(0, bus.BeginInstructionTimingCalls);
		Assert.Equal(0, bus.FlushInstructionTimingCalls);
		Assert.Equal(0, bus.CompletedBatchInstructions);
		Assert.False(bus.IsDeferredCpuBusBatchActive);
	}
	[Theory]
	[InlineData(false, (int)M68kOpcodePlanDispatch.KindTable)]
	[InlineData(true, (int)M68kOpcodePlanDispatch.KindTable)]
	[InlineData(true, (int)M68kOpcodePlanDispatch.PackedPlan)]
	public void ShortConditionalBranchNotTakenCommitsFallthroughFetchAfterInternalProgress(
		bool enableOpcodePlan,
		int dispatchValue)
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, Words(0x6702, 0x4E71, 0x4E71)); // BEQ.S not taken
		var cpu = CreateCycleParityCpu(
			bus,
			enableOpcodePlan,
			(M68kOpcodePlanDispatch)dispatchValue);
		cpu.State.StatusRegister &= unchecked((ushort)~M68kCpuState.Zero);

		var elapsed = cpu.ExecuteInstruction();
		var committedLookahead = Assert.Single(bus.CpuBusPhases, phase =>
			phase.InstructionProgramCounter == 0x1000 &&
			phase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.Address == 0x1004);

		Assert.Equal(8, elapsed);
		Assert.Equal(6, committedLookahead.RequestedCycle);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedShortSelfBranchBatchMatchesScalarQueueAndBusOrder(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, Words(0x60FE)); // BRA.S self
		Write(plannedBus.Memory, 0x1000, Words(0x60FE));
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		planned.PlannedInterpreterCountersEnabled = true;
		var scalarBoundary = new BusAccessBatchBoundary();
		var plannedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(32, ((IM68kBatchCore)scalar).ExecuteInstructions(32, long.MaxValue, scalarBoundary));
		Assert.Equal(32, ((IM68kBatchCore)planned).ExecuteInstructions(32, long.MaxValue, plannedBoundary));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
		Assert.Equal(32, scalarBoundary.BeforeInstructionCalls);
		Assert.Equal(32, scalarBoundary.AfterInstructionCalls);
		Assert.Equal(0, scalarBoundary.BusAccessBatchCalls);
		Assert.Equal(32, plannedBoundary.BeforeInstructionCalls);
		Assert.Equal(32, plannedBoundary.AfterInstructionCalls);
		Assert.Equal(0, plannedBoundary.BusAccessBatchCalls);
		Assert.Equal(0, plannedBoundary.BusAccessBatchInstructions);
		var counters = planned.CapturePlannedInterpreterCounters();
		Assert.Equal(32, counters.FastInstructions);
		Assert.Equal(32, counters.BranchInstructions);
		Assert.Equal(0, counters.ScalarFallbackInstructions);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedShortSelfBranchBatchPreservesQueuedOpcodeAfterExternalWrite(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, Words(0x60FE)); // BRA.S self
		Write(plannedBus.Memory, 0x1000, Words(0x60FE));
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		scalar.ExecuteInstruction();
		planned.ExecuteInstruction();
		Write(scalarBus.Memory, 0x1000, Words(0x4E71)); // NOP, old BRA remains queued
		Write(plannedBus.Memory, 0x1000, Words(0x4E71));
		var scalarBoundary = new BusAccessBatchBoundary();
		var plannedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(2, ((IM68kBatchCore)scalar).ExecuteInstructions(2, long.MaxValue, scalarBoundary));
		Assert.Equal(2, ((IM68kBatchCore)planned).ExecuteInstructions(2, long.MaxValue, plannedBoundary));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
		Assert.Equal(0x4E71, planned.State.LastOpcode);
		Assert.Equal(0x1002u, planned.State.ProgramCounter);
		Assert.Equal(0, plannedBoundary.BusAccessBatchCalls);
		Assert.Equal(0, plannedBoundary.BusAccessBatchInstructions);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchMatchesScalarQueueAndBusOrder(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7400, // MOVEQ #0,D2
			0xD081, // ADD.L D1,D0
			0x5482, // ADDQ.L #2,D2
			0x4E71, // NOP
			0x60F8); // BRA.S arithmetic loop
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		planned.PlannedInterpreterCountersEnabled = true;
		var scalarBoundary = new BusAccessBatchBoundary();
		var plannedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(70, ((IM68kBatchCore)scalar).ExecuteInstructions(70, long.MaxValue, scalarBoundary));
		Assert.Equal(70, ((IM68kBatchCore)planned).ExecuteInstructions(70, long.MaxValue, plannedBoundary));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
		Assert.Equal(70, scalarBoundary.BeforeInstructionCalls);
		Assert.Equal(70, scalarBoundary.AfterInstructionCalls);
		Assert.Equal(70, plannedBoundary.BeforeInstructionCalls);
		Assert.Equal(70, plannedBoundary.AfterInstructionCalls);
		Assert.Equal(0, plannedBoundary.BusAccessBatchCalls);
		Assert.Equal(0, plannedBoundary.BusAccessBatchInstructions);
		var counters = planned.CapturePlannedInterpreterCounters();
		Assert.Equal(70, counters.FastInstructions);
		Assert.Equal(0, counters.ScalarFallbackInstructions);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchExecutesEveryAdmittedKindDirectly(int dispatchValue)
	{
		var opcodes = new ushort[]
		{
			0x7001, // MOVEQ #1,D0
			0x5240, // ADDQ.W #1,D0
			0x5280, // ADDQ.L #1,D0
			0x8081, // OR.L D1,D0
			0xB183, // EOR.L D0,D3
			0xC087, // AND.L D7,D0
			0xD080, // ADD.L D0,D0
			0x4E71, // NOP
			0x60EE  // BRA.S loop
		};
		var expectedKinds = new[]
		{
			M68kOpcodePlanKind.Moveq,
			M68kOpcodePlanKind.QuickRegister,
			M68kOpcodePlanKind.QuickLongDataRegister,
			M68kOpcodePlanKind.DataRegisterLongOrToRegister,
			M68kOpcodePlanKind.DataRegisterLongEorToDestination,
			M68kOpcodePlanKind.DataRegisterLongAndToRegister,
			M68kOpcodePlanKind.DataRegisterLongAddToRegister,
			M68kOpcodePlanKind.Nop,
			M68kOpcodePlanKind.ShortUnconditionalBranch
		};
		Assert.Equal(expectedKinds, opcodes.Select(opcode => M68kOpcodePlanTable.Kinds[opcode]));

		var program = Words(opcodes);
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		scalar.State.D[1] = planned.State.D[1] = 0x0101_0101;
		scalar.State.D[3] = planned.State.D[3] = 0xA5A5_5A5A;
		scalar.State.D[7] = planned.State.D[7] = 0x0FFF_FFFF;
		planned.PlannedInterpreterCountersEnabled = true;
		var scalarBoundary = new BusAccessBatchBoundary();
		var plannedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(96, ((IM68kBatchCore)scalar).ExecuteInstructions(96, long.MaxValue, scalarBoundary));
		Assert.Equal(96, ((IM68kBatchCore)planned).ExecuteInstructions(96, long.MaxValue, plannedBoundary));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
		Assert.Equal(0, plannedBoundary.BusAccessBatchCalls);
		Assert.Equal(0, plannedBoundary.BusAccessBatchInstructions);
		var counters = planned.CapturePlannedInterpreterCounters();
		Assert.Equal(96, counters.FastInstructions);
		Assert.Equal(0, counters.ScalarFallbackInstructions);
		Assert.True(counters.NopInstructions > 0);
		Assert.True(counters.MoveqInstructions > 0);
		Assert.True(counters.BranchInstructions > 0);
		Assert.True(counters.QuickRegisterInstructions > 0);
		Assert.True(counters.RegisterArithmeticInstructions > 0);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchExitsToScalarAtUnsupportedOpcode(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x4280, // CLR.L D0
			0x4E71); // NOP
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		planned.PlannedInterpreterCountersEnabled = true;
		var scalarBoundary = new BusAccessBatchBoundary();
		var plannedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(5, ((IM68kBatchCore)scalar).ExecuteInstructions(5, long.MaxValue, scalarBoundary));
		Assert.Equal(5, ((IM68kBatchCore)planned).ExecuteInstructions(5, long.MaxValue, plannedBoundary));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(0x4280, planned.State.LastOpcode);
		Assert.Equal(0, plannedBoundary.BusAccessBatchInstructions);
		Assert.Equal(1, planned.CapturePlannedInterpreterCounters().ScalarFallbackInstructions);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchHonorsTargetCycleAndInstructionLimit(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x5280, // ADDQ.L #1,D0
			0x60FA); // BRA.S MOVEQ D2
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		var targetCycle = scalar.State.Cycles + 80;
		var scalarTargetBoundary = new BusAccessBatchBoundary();
		var plannedTargetBoundary = new BusAccessBatchBoundary();

		var scalarToTarget = ((IM68kBatchCore)scalar).ExecuteInstructions(100, targetCycle, scalarTargetBoundary);
		var plannedToTarget = ((IM68kBatchCore)planned).ExecuteInstructions(100, targetCycle, plannedTargetBoundary);

		Assert.Equal(scalarToTarget, plannedToTarget);
		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.True(planned.State.Cycles >= targetCycle);
		Assert.Equal(0, plannedTargetBoundary.BusAccessBatchInstructions);

		var scalarLimitBoundary = new BusAccessBatchBoundary();
		var plannedLimitBoundary = new BusAccessBatchBoundary();
		Assert.Equal(5, ((IM68kBatchCore)scalar).ExecuteInstructions(5, long.MaxValue, scalarLimitBoundary));
		Assert.Equal(5, ((IM68kBatchCore)planned).ExecuteInstructions(5, long.MaxValue, plannedLimitBoundary));
		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchCommitsStateBeforeAfterBatchBoundary(int dispatchValue)
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x4E71)); // NOP
		var cpu = CreateCycleParityCpu(
			bus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		var boundary = new BusAccessBatchBoundary();

		Assert.Equal(4, ((IM68kBatchCore)cpu).ExecuteInstructions(4, long.MaxValue, boundary));

		Assert.Equal(0, boundary.BusAccessBatchCalls);
		Assert.Equal(4, boundary.BeforeInstructionCalls);
		Assert.Equal(4, boundary.AfterInstructionCalls);
		Assert.Equal(0u, boundary.AfterBatchProgramCounter);
		Assert.Equal(0, boundary.AfterBatchCycles);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchCommitsPartialStateWhenInstructionFetchThrows(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus
		{
			ThrowingInstructionFetchAddress = 0x1008
		};
		var plannedBus = new CycleCountingBus
		{
			ThrowingInstructionFetchAddress = 0x1008
		};
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x7805); // MOVEQ #5,D4
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);

		Assert.Throws<InvalidOperationException>(() =>
			((IM68kBatchCore)scalar).ExecuteInstructions(8, long.MaxValue, new BusAccessBatchBoundary()));
		Assert.Throws<InvalidOperationException>(() =>
			((IM68kBatchCore)planned).ExecuteInstructions(8, long.MaxValue, new BusAccessBatchBoundary()));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchCommitsBeforeScalarStopInstruction(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x4E72, 0x2700); // STOP #$2700
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);

		Assert.Equal(5, ((IM68kBatchCore)scalar).ExecuteInstructions(5, long.MaxValue, new BusAccessBatchBoundary()));
		Assert.Equal(5, ((IM68kBatchCore)planned).ExecuteInstructions(5, long.MaxValue, new BusAccessBatchBoundary()));

		Assert.True(planned.State.Stopped);
		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchMatchesScalarWithDelayedRetirementPrefetch(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0xD081, // ADD.L D1,D0
			0x4E71, // NOP
			0x60F8); // BRA.S ADD
		var scalarBus = new CycleCountingBus
		{
			DelayedInstructionFetchAddress = 0x1008,
			DelayedInstructionFetchOccurrence = 2,
			DelayedInstructionFetchCycles = 12
		};
		var plannedBus = new CycleCountingBus
		{
			DelayedInstructionFetchAddress = 0x1008,
			DelayedInstructionFetchOccurrence = 2,
			DelayedInstructionFetchCycles = 12
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		var scalarBoundary = new BusAccessBatchBoundary();
		var plannedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(80, ((IM68kBatchCore)scalar).ExecuteInstructions(80, long.MaxValue, scalarBoundary));
		Assert.Equal(80, ((IM68kBatchCore)planned).ExecuteInstructions(80, long.MaxValue, plannedBoundary));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)planned).CapturePrefetchDiagnosticState());
		Assert.Contains(plannedBus.CpuBusPhases, phase =>
			phase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.Address == 0x1008 &&
			phase.CompletedCycle - phase.RequestedCycle >= 14);
		Assert.Equal(0, plannedBoundary.BusAccessBatchInstructions);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedFixedPlanBatchRejectsOddTargetBranchPair(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x4E71, // NOP
			0x6001); // BRA.S to odd address
		var scalarBus = new CycleCountingBus();
		var plannedBus = new CycleCountingBus();
		Write(scalarBus.Memory, 0x000C, 0x00, 0x00, 0x40, 0x00);
		Write(plannedBus.Memory, 0x000C, 0x00, 0x00, 0x40, 0x00);
		Write(scalarBus.Memory, 0x1000, program);
		Write(plannedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false);
		var planned = CreateCycleParityCpu(
			plannedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue);
		var scalarBoundary = new BusAccessBatchBoundary();
		var plannedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(3, ((IM68kBatchCore)scalar).ExecuteInstructions(3, long.MaxValue, scalarBoundary));
		Assert.Equal(3, ((IM68kBatchCore)planned).ExecuteInstructions(3, long.MaxValue, plannedBoundary));

		AssertCycleParity(scalar, scalarBus, planned, plannedBus);
		Assert.Equal(3, planned.State.LastExceptionVector);
		Assert.Equal(0, plannedBoundary.BusAccessBatchCalls);
		Assert.Equal(0, plannedBoundary.BusAccessBatchInstructions);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunMatchesScalarAcrossLoopAndLimits(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7400, // MOVEQ #0,D2
			0xD081, // ADD.L D1,D0
			0x5482, // ADDQ.L #2,D2
			0x4E71, // NOP
			0x60F8); // BRA.S arithmetic loop
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, enableOpcodePlan: false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(73, ((IM68kBatchCore)scalar).ExecuteInstructions(73, long.MaxValue, scalarBoundary));
		Assert.Equal(73, ((IM68kBatchCore)cached).ExecuteInstructions(73, long.MaxValue, cachedBoundary));
		AssertCachedRunParity(scalar, cached);
		Assert.True(cachedBus.FixedPlanWindowRequests > 0);
		Assert.True(cachedBoundary.PureCpuBatchCalls > 0);
		Assert.Equal(cached.State.ProgramCounter, cachedBoundary.AfterBatchProgramCounter);
		Assert.Equal(cached.State.Cycles, cachedBoundary.AfterBatchCycles);
		Assert.Equal(cached.State.LastOpcode, cachedBoundary.AfterBatchLastOpcode);

		var target = scalar.State.Cycles + 83;
		var scalarToTarget = ((IM68kBatchCore)scalar).ExecuteInstructions(100, target, scalarBoundary);
		var cachedToTarget = ((IM68kBatchCore)cached).ExecuteInstructions(100, target, cachedBoundary);
		Assert.Equal(scalarToTarget, cachedToTarget);
		AssertCachedRunParity(scalar, cached);
		Assert.True(cached.State.Cycles >= target);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunFollowsSafeForwardShortBranch(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x6004, // BRA.S target
			0x76FF, // skipped
			0x4E71, // skipped
			0x7403, // target: MOVEQ #3,D2
			0x5480, // ADDQ.L #2,D0
			0x60FA); // BRA.S target
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(41, ((IM68kBatchCore)scalar).ExecuteInstructions(41, long.MaxValue, scalarBoundary));
		Assert.Equal(41, ((IM68kBatchCore)cached).ExecuteInstructions(41, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(0u, cached.State.D[3]);
		Assert.True(cachedBoundary.PureCpuBatchCalls > 0);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunPreservesPlannedCounters(int dispatchValue)
	{
		var bus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(bus.Memory, 0x1000, Words(
			0x7001, // MOVEQ #1,D0
			0x5280, // ADDQ.L #1,D0
			0x8080, // OR.L D0,D0
			0x4E71, // NOP
			0x60F8)); // BRA.S ADDQ
		var cpu = CreateCycleParityCpu(
			bus,
			enableOpcodePlan: true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		cpu.PlannedInterpreterCountersEnabled = true;

		Assert.Equal(96, ((IM68kBatchCore)cpu).ExecuteInstructions(
			96,
			long.MaxValue,
			new BusAccessBatchBoundary()));

		var counters = cpu.CapturePlannedInterpreterCounters();
		Assert.Equal(96, counters.FastInstructions);
		Assert.Equal(0, counters.ScalarFallbackInstructions);
		Assert.True(counters.MoveqInstructions > 0);
		Assert.True(counters.QuickRegisterInstructions > 0);
		Assert.True(counters.RegisterArithmeticInstructions > 0);
		Assert.True(counters.NopInstructions > 0);
		Assert.True(counters.BranchInstructions > 0);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunAcceleratedLoopMatchesEveryAdmittedRegisterKind(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x5240, // ADDQ.W #1,D0
			0x5280, // ADDQ.L #1,D0
			0x5288, // ADDQ.L #1,A0
			0x8081, // OR.L D1,D0
			0xB183, // EOR.L D0,D3
			0xC087, // AND.L D7,D0
			0xD080, // ADD.L D0,D0
			0x4E71, // NOP
			0x60EC); // BRA.S loop
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		scalar.State.D[1] = cached.State.D[1] = 0x0101_0101;
		scalar.State.D[3] = cached.State.D[3] = 0xA5A5_5A5A;
		scalar.State.D[7] = cached.State.D[7] = 0x0FFF_FFFF;
		scalar.State.A[0] = cached.State.A[0] = 0x2000;

		Assert.Equal(1003, ((IM68kBatchCore)scalar).ExecuteInstructions(
			1003,
			long.MaxValue,
			new BusAccessBatchBoundary()));
		Assert.Equal(1003, ((IM68kBatchCore)cached).ExecuteInstructions(
			1003,
			long.MaxValue,
			new BusAccessBatchBoundary()));

		AssertCachedRunParity(scalar, cached);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunGuardsChangedOpcodeBeyondQueuedWords(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x7805, // MOVEQ #5,D4
			0x7A06, // MOVEQ #6,D5 -- changed after cache construction
			0x60F4); // BRA.S D1
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		// One prologue instruction plus the complete cached loop returns to $1002
		// with the cache key and queued IR/IRC unchanged.
		Assert.Equal(7, ((IM68kBatchCore)scalar).ExecuteInstructions(7, long.MaxValue, scalarBoundary));
		Assert.Equal(7, ((IM68kBatchCore)cached).ExecuteInstructions(7, long.MaxValue, cachedBoundary));
		AssertCachedRunParity(scalar, cached);
		Assert.True(cachedBoundary.PureCpuBatchCalls > 0);

		// Keep the window generation unchanged to exercise the per-entry opcode guard.
		scalarBus.WriteWordRaw(0x100A, 0x4285); // CLR.L D5, unsupported by fixed runs
		cachedBus.WriteWordRaw(0x100A, 0x4285);
		Assert.Equal(6, ((IM68kBatchCore)scalar).ExecuteInstructions(6, long.MaxValue, scalarBoundary));
		Assert.Equal(6, ((IM68kBatchCore)cached).ExecuteInstructions(6, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(0u, cached.State.D[5]);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunPreservesStaleQueuedOpcodeAtModifiedLoopTarget(int dispatchValue)
	{
		var program = Words(
			0x7001, // prologue
			0x7202, // MOVEQ #2,D1 -- loop target changed while queued
			0x5480, // ADDQ.L #2,D0
			0x4E71, // NOP
			0x60F8); // BRA.S loop target
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		// Prologue plus one loop leaves the original target opcode in IR.
		Assert.Equal(5, ((IM68kBatchCore)scalar).ExecuteInstructions(5, long.MaxValue, scalarBoundary));
		Assert.Equal(5, ((IM68kBatchCore)cached).ExecuteInstructions(5, long.MaxValue, cachedBoundary));
		AssertCachedRunParity(scalar, cached);
		scalarBus.WriteWordRaw(0x1002, 0x7207); // MOVEQ #7,D1, generation deliberately unchanged
		cachedBus.WriteWordRaw(0x1002, 0x7207);

		Assert.Equal(8, ((IM68kBatchCore)scalar).ExecuteInstructions(8, long.MaxValue, scalarBoundary));
		Assert.Equal(8, ((IM68kBatchCore)cached).ExecuteInstructions(8, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(7u, cached.State.D[1]);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunRebuildsAfterWindowGenerationChange(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x60F8); // BRA.S D1
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(11, ((IM68kBatchCore)scalar).ExecuteInstructions(11, long.MaxValue, scalarBoundary));
		Assert.Equal(11, ((IM68kBatchCore)cached).ExecuteInstructions(11, long.MaxValue, cachedBoundary));
		AssertCachedRunParity(scalar, cached);
		var requestsBeforeInvalidation = cachedBus.FixedPlanWindowRequests;

		cachedBus.AdvanceFixedPlanWindowGeneration();
		Assert.Equal(17, ((IM68kBatchCore)scalar).ExecuteInstructions(17, long.MaxValue, scalarBoundary));
		Assert.Equal(17, ((IM68kBatchCore)cached).ExecuteInstructions(17, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.True(cachedBus.FixedPlanWindowRequests > requestsBeforeInvalidation);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunDirectMappedCollisionReplacesAndRebuilds(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1300
		};
		var loop = Words(0x7001, 0x5280, 0x4E71, 0x60FA);
		Write(scalarBus.Memory, 0x1000, loop);
		Write(scalarBus.Memory, 0x1200, loop);
		Write(cachedBus.Memory, 0x1000, loop);
		Write(cachedBus.Memory, 0x1200, loop);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		foreach (var programCounter in new uint[] { 0x1000, 0x1200, 0x1000 })
		{
			scalar.Reset(programCounter, 0x8000);
			cached.Reset(programCounter, 0x8000);
			Assert.Equal(19, ((IM68kBatchCore)scalar).ExecuteInstructions(
				19,
				long.MaxValue,
				scalarBoundary));
			Assert.Equal(19, ((IM68kBatchCore)cached).ExecuteInstructions(
				19,
				long.MaxValue,
				cachedBoundary));
			AssertCachedRunParity(scalar, cached);
		}

		Assert.True(cachedBoundary.PureCpuBatchCalls >= 3);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunExitsBeforeUnsafeWindowFetch(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x7805, // MOVEQ #5,D4
			0x7A06); // MOVEQ #6,D5, outside direct window
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x100A
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(6, ((IM68kBatchCore)scalar).ExecuteInstructions(6, long.MaxValue, scalarBoundary));
		Assert.Equal(6, ((IM68kBatchCore)cached).ExecuteInstructions(6, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.Contains(cachedBus.InstructionFetchCycles, fetch => fetch.Address == 0x100A);
		Assert.Equal(0, cachedBoundary.PureCpuBatchCalls);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CpuBusPhaseTracingDisablesCachedFixedPlanRuns(int dispatchValue)
	{
		var program = Words(
			0x7001, // MOVEQ #1,D0
			0x7202, // MOVEQ #2,D1
			0x7403, // MOVEQ #3,D2
			0x7604, // MOVEQ #4,D3
			0x60F8); // BRA.S D1
		var scalarBus = new CycleCountingBus();
		var tracedBus = new CycleCountingBus
		{
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x1000, program);
		Write(tracedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false);
		var traced = CreateCycleParityCpu(
			tracedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue);
		var scalarBoundary = new BusAccessBatchBoundary();
		var tracedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(48, ((IM68kBatchCore)scalar).ExecuteInstructions(48, long.MaxValue, scalarBoundary));
		Assert.Equal(48, ((IM68kBatchCore)traced).ExecuteInstructions(48, long.MaxValue, tracedBoundary));

		AssertCycleParity(scalar, scalarBus, traced, tracedBus);
		Assert.Equal(0, tracedBoundary.PureCpuBatchCalls);
		Assert.Equal(0, tracedBus.FixedPlanWindowRequests);
		Assert.NotEmpty(tracedBus.CpuBusPhases);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void InstructionFrequencyTracingDisablesCachedFixedPlanRuns(int dispatchValue)
	{
		var bus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(bus.Memory, 0x1000, Words(0x7001, 0x7202, 0x7403, 0x7604, 0x60F8));
		var cpu = CreateCycleParityCpu(
			bus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		((IM68kInstructionFrequencyProvider)cpu).InstructionFrequencyEnabled = true;
		var boundary = new BusAccessBatchBoundary();

		Assert.Equal(32, ((IM68kBatchCore)cpu).ExecuteInstructions(32, long.MaxValue, boundary));

		Assert.Equal(0, boundary.PureCpuBatchCalls);
		Assert.Equal(0, bus.FixedPlanWindowRequests);
		Assert.True(boundary.BeforeInstructionCalls > 0);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunRejectsPendingPrefetchAdmission(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		var program = Words(0x7001, 0x7202, 0x7403, 0x7604, 0x4E71);
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarPipeline = (IM68000PipelineStateTransfer)scalar;
		var cachedPipeline = (IM68000PipelineStateTransfer)cached;
		var entry = scalarPipeline.ExportM68000PipelineState() with
		{
			PrefetchAddress = 0x1000,
			Word0 = 0x7001,
			Word1 = 0x7202,
			PrefetchCount = 2,
			HasPendingPrefetch = true,
			PendingPrefetchAddress = 0x1004,
			PendingPrefetchEarliestCycle = 8
		};
		scalarPipeline.ImportM68000PipelineState(in entry);
		cachedPipeline.ImportM68000PipelineState(in entry);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(1, ((IM68kBatchCore)scalar).ExecuteInstructions(1, long.MaxValue, scalarBoundary));
		Assert.Equal(1, ((IM68kBatchCore)cached).ExecuteInstructions(1, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(0, cachedBoundary.PureCpuBatchCalls);
		Assert.Equal(0, cachedBus.FixedPlanWindowRequests);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunCommitsPrefixBeforeUnsafeFetchThrows(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			ThrowingInstructionFetchAddress = 0x100A
		};
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			ThrowingInstructionFetchAddress = 0x100A,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x100A
		};
		var program = Words(0x7001, 0x7202, 0x7403, 0x7604, 0x7805, 0x7A06);
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Throws<InvalidOperationException>(() =>
			((IM68kBatchCore)scalar).ExecuteInstructions(8, long.MaxValue, scalarBoundary));
		Assert.Throws<InvalidOperationException>(() =>
			((IM68kBatchCore)cached).ExecuteInstructions(8, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(0, cachedBoundary.PureCpuBatchCalls);
		Assert.Equal(0x1006u, cached.State.LastInstructionProgramCounter);
		Assert.Equal(0x7604, cached.State.LastOpcode);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanRunStopsBeforeOddShortBranchTarget(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		Write(scalarBus.Memory, 0x000C, 0x00, 0x00, 0x40, 0x00);
		Write(cachedBus.Memory, 0x000C, 0x00, 0x00, 0x40, 0x00);
		var program = Words(0x7001, 0x7202, 0x7403, 0x6001);
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);

		Assert.Equal(4, ((IM68kBatchCore)scalar).ExecuteInstructions(
			4,
			long.MaxValue,
			new BusAccessBatchBoundary()));
		Assert.Equal(4, ((IM68kBatchCore)cached).ExecuteInstructions(
			4,
			long.MaxValue,
			new BusAccessBatchBoundary()));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(3, cached.State.LastExceptionVector);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedShortSelfBranchMatchesScalarAcrossInstructionAndCycleLimits(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = new CycleCountingBus
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
		var program = Words(0x60FE, 0x4E71, 0x4E71);
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(37, ((IM68kBatchCore)scalar).ExecuteInstructions(37, long.MaxValue, scalarBoundary));
		Assert.Equal(37, ((IM68kBatchCore)cached).ExecuteInstructions(37, long.MaxValue, cachedBoundary));
		AssertCachedRunParity(scalar, cached);
		Assert.True(cachedBoundary.PureCpuBatchCalls > 0);

		var targetCycle = scalar.State.Cycles + 45;
		var scalarCount = ((IM68kBatchCore)scalar).ExecuteInstructions(100, targetCycle, scalarBoundary);
		var cachedCount = ((IM68kBatchCore)cached).ExecuteInstructions(100, targetCycle, cachedBoundary);
		Assert.Equal(scalarCount, cachedCount);
		AssertCachedRunParity(scalar, cached);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFixedPlanGraphMatchesAllShortConditionalBranchConditions(int dispatchValue)
	{
		for (var condition = 2; condition <= 15; condition++)
		{
			foreach (var expectedTaken in new[] { false, true })
			{
				var conditionFlags = Enumerable.Range(0, 16)
					.Select(value => (ushort)value)
					.First(flags => M68kIntegerSemantics.EvaluateCondition(
						flags,
						condition) == expectedTaken);
				var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
				var cachedBus = new CycleCountingBus
				{
					ZeroWaitInstructionFetch = true,
					FixedPlanRunEnabled = true,
					FixedPlanWindowStart = 0x1000,
					FixedPlanWindowEndExclusive = 0x1100
				};
				var program = Words(
					(ushort)(0x6004 | (condition << 8)), // Bcc.S $1006
					0x7001, // fallthrough: MOVEQ #1,D0
					0x6002, // BRA.S join
					0x7002, // taken: MOVEQ #2,D0
					0x60F6); // join: BRA.S $1000
				Write(scalarBus.Memory, 0x1000, program);
				Write(cachedBus.Memory, 0x1000, program);
				var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
				var cached = CreateCycleParityCpu(
					cachedBus,
					true,
					(M68kOpcodePlanDispatch)dispatchValue,
					enableCpuBusPhaseTrace: false);
			scalar.State.StatusRegister = cached.State.StatusRegister = (ushort)(
				M68kCpuState.Supervisor | conditionFlags);
				var scalarBoundary = new BusAccessBatchBoundary();
				var cachedBoundary = new BusAccessBatchBoundary();

				Assert.Equal(17, ((IM68kBatchCore)scalar).ExecuteInstructions(
					17,
					long.MaxValue,
					scalarBoundary));
				Assert.Equal(17, ((IM68kBatchCore)cached).ExecuteInstructions(
					17,
					long.MaxValue,
					cachedBoundary));

				AssertCachedRunParity(scalar, cached);
				Assert.True(cachedBoundary.PureCpuBatchCalls > 0);
			}
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFastMemoryRunMatchesScalarThroughDbraExpiry(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = CreateFastMemoryRunBus();
		var program = Words(
			0x2018,             // MOVE.L (A0)+,D0
			0xD081,             // ADD.L D1,D0
			0x23C0, 0x0000, 0x3000, // MOVE.L D0,$3000.L
			0x51CA, 0xFFF4);    // DBRA D2,$1000
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		for (var i = 0; i < 3; i++)
		{
			var value = unchecked(0x1020_3040u + (uint)i);
			WriteLongRaw(scalarBus.Memory, 0x2000u + ((uint)i * 4), value);
			WriteLongRaw(cachedBus.Memory, 0x2000u + ((uint)i * 4), value);
		}
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		scalar.State.A[0] = cached.State.A[0] = 0x2000;
		scalar.State.D[1] = cached.State.D[1] = 0x0101_0101;
		scalar.State.D[2] = cached.State.D[2] = 2;
		var scalarBoundary = new BusAccessBatchBoundary();
		var cachedBoundary = new BusAccessBatchBoundary();

		Assert.Equal(12, ((IM68kBatchCore)scalar).ExecuteInstructions(12, long.MaxValue, scalarBoundary));
		Assert.Equal(12, ((IM68kBatchCore)cached).ExecuteInstructions(12, long.MaxValue, cachedBoundary));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(ReadLongRaw(scalarBus.Memory, 0x3000), ReadLongRaw(cachedBus.Memory, 0x3000));
		Assert.True(cachedBus.FastLongReadCalls > 0);
		Assert.True(cachedBus.FastLongWriteCalls > 0);
		Assert.True(cachedBoundary.BusAccessBatchCalls > 0);
		Assert.Equal(
			new[]
			{
				(0x3000u, true),
				(0x2004u, false), (0x3000u, true),
				(0x2008u, false), (0x3000u, true)
			},
			cachedBus.FastLongAccesses);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFastMemoryGraphAdmitsDifferentLoopShapes(int dispatchValue)
	{
		var cases = new[]
		{
			(Program: Words(0x2018, 0x51CA, 0xFFFC), Instructions: 6, Counter: 2u),
			(Program: Words(0x2018, 0x5382, 0x66FA), Instructions: 6, Counter: 2u),
			(Program: Words(
				0xD081,
				0x2018,
				0x23C0, 0x0000, 0x3000,
				0x51CA, 0xFFF4), Instructions: 12, Counter: 2u)
		};

		foreach (var testCase in cases)
		{
			var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
			var cachedBus = CreateFastMemoryRunBus();
			Write(scalarBus.Memory, 0x1000, testCase.Program);
			Write(cachedBus.Memory, 0x1000, testCase.Program);
			for (var index = 0; index < 4; index++)
			{
				var value = 0x1020_3040u + (uint)index;
				WriteLongRaw(scalarBus.Memory, 0x2000u + ((uint)index * 4), value);
				WriteLongRaw(cachedBus.Memory, 0x2000u + ((uint)index * 4), value);
			}

			var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
			var cached = CreateCycleParityCpu(
				cachedBus,
				true,
				(M68kOpcodePlanDispatch)dispatchValue,
				enableCpuBusPhaseTrace: false);
			scalar.State.A[0] = cached.State.A[0] = 0x2000;
			scalar.State.D[1] = cached.State.D[1] = 0x0101_0101;
			scalar.State.D[2] = cached.State.D[2] = testCase.Counter;
			var scalarBoundary = new BusAccessBatchBoundary();
			var cachedBoundary = new BusAccessBatchBoundary();

			Assert.Equal(testCase.Instructions, ((IM68kBatchCore)scalar).ExecuteInstructions(
				testCase.Instructions,
				long.MaxValue,
				scalarBoundary));
			Assert.Equal(testCase.Instructions, ((IM68kBatchCore)cached).ExecuteInstructions(
				testCase.Instructions,
				long.MaxValue,
				cachedBoundary));

			AssertCachedRunParity(scalar, cached);
			Assert.True(cachedBus.FastLongReadCalls > 0);
			Assert.True(cachedBoundary.BusAccessBatchCalls > 0);
			Assert.True(scalarBus.Memory.AsSpan(0x3000, 4).SequenceEqual(
				cachedBus.Memory.AsSpan(0x3000, 4)));
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFastMemoryRunRestoresStoreEntryWhenFastWriteIsRejected(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = CreateFastMemoryRunBus();
		cachedBus.RejectedFastWriteAddress = 0x3000;
		var program = Words(
			0x2018,
			0xD081,
			0x23C0, 0x0000, 0x3000,
			0x51CA, 0xFFF4);
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		WriteLongRaw(scalarBus.Memory, 0x2000, 0x7FFF_FFFF);
		WriteLongRaw(cachedBus.Memory, 0x2000, 0x7FFF_FFFF);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		scalar.State.A[0] = cached.State.A[0] = 0x2000;
		scalar.State.D[1] = cached.State.D[1] = 1;
		scalar.State.D[2] = cached.State.D[2] = 1;

		Assert.Equal(8, ((IM68kBatchCore)scalar).ExecuteInstructions(8, long.MaxValue, new BusAccessBatchBoundary()));
		Assert.Equal(8, ((IM68kBatchCore)cached).ExecuteInstructions(8, long.MaxValue, new BusAccessBatchBoundary()));

		AssertCachedRunParity(scalar, cached);
		Assert.Equal(ReadLongRaw(scalarBus.Memory, 0x3000), ReadLongRaw(cachedBus.Memory, 0x3000));
		Assert.True(cachedBus.FastLongWriteCalls > 0);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFastMemoryRunFallsBackForRejectedAndOddReads(int dispatchValue)
	{
		foreach (var (sourceAddress, rejectedAddress) in new[]
		{
			(0x2000u, (uint?)0x2000u),
			(0x2001u, (uint?)null)
		})
		{
			var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
			var cachedBus = CreateFastMemoryRunBus();
			Write(scalarBus.Memory, 0x000C, 0x00, 0x00, 0x40, 0x00);
			Write(cachedBus.Memory, 0x000C, 0x00, 0x00, 0x40, 0x00);
			var program = Words(
				0x2018,
				0xD081,
				0x23C0, 0x0000, 0x3000,
				0x51CA, 0xFFF4);
			Write(scalarBus.Memory, 0x1000, program);
			Write(cachedBus.Memory, 0x1000, program);
			WriteLongRaw(scalarBus.Memory, 0x2000, 0x0102_0304);
			WriteLongRaw(cachedBus.Memory, 0x2000, 0x0102_0304);
			var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
			var cached = CreateCycleParityCpu(
				cachedBus,
				true,
				(M68kOpcodePlanDispatch)dispatchValue,
				enableCpuBusPhaseTrace: false);
			scalar.State.A[0] = cached.State.A[0] = 0x2000;
			scalar.State.D[2] = cached.State.D[2] = 1;

			// The reset queue contains one word, so execute the first iteration
			// scalarly and return with the two-word loop-entry queue populated.
			Assert.Equal(4, ((IM68kBatchCore)scalar).ExecuteInstructions(4, long.MaxValue, new BusAccessBatchBoundary()));
			Assert.Equal(4, ((IM68kBatchCore)cached).ExecuteInstructions(4, long.MaxValue, new BusAccessBatchBoundary()));
			AssertCachedRunParity(scalar, cached);
			scalar.State.A[0] = cached.State.A[0] = sourceAddress;
			cachedBus.RejectedFastReadAddress = rejectedAddress;

			Assert.Equal(2, ((IM68kBatchCore)scalar).ExecuteInstructions(2, long.MaxValue, new BusAccessBatchBoundary()));
			Assert.Equal(2, ((IM68kBatchCore)cached).ExecuteInstructions(2, long.MaxValue, new BusAccessBatchBoundary()));

			AssertCachedRunParity(scalar, cached);
			if ((sourceAddress & 1) != 0)
			{
				Assert.Equal(3, cached.State.LastExceptionVector);
			}
			else
			{
				Assert.True(cachedBus.FastLongReadCalls > 0);
			}
		}
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void CachedFastMemoryRunRevalidatesExtensionsAndSelfModifiedCode(int dispatchValue)
	{
		var scalarBus = new CycleCountingBus { ZeroWaitInstructionFetch = true };
		var cachedBus = CreateFastMemoryRunBus();
		var program = Words(
			0x2018,
			0xD081,
			0x23C0, 0x0000, 0x3000,
			0x51CA, 0xFFF4);
		Write(scalarBus.Memory, 0x1000, program);
		Write(cachedBus.Memory, 0x1000, program);
		WriteLongRaw(scalarBus.Memory, 0x2000, 0x1111_1111);
		WriteLongRaw(cachedBus.Memory, 0x2000, 0x1111_1111);
		WriteLongRaw(scalarBus.Memory, 0x2004, 0x4E71_4E71);
		WriteLongRaw(cachedBus.Memory, 0x2004, 0x4E71_4E71);
		var scalar = CreateCycleParityCpu(scalarBus, false, enableCpuBusPhaseTrace: false);
		var cached = CreateCycleParityCpu(
			cachedBus,
			true,
			(M68kOpcodePlanDispatch)dispatchValue,
			enableCpuBusPhaseTrace: false);
		scalar.State.A[0] = cached.State.A[0] = 0x2000;
		scalar.State.D[2] = cached.State.D[2] = 2;

		Assert.Equal(4, ((IM68kBatchCore)scalar).ExecuteInstructions(4, long.MaxValue, new BusAccessBatchBoundary()));
		Assert.Equal(4, ((IM68kBatchCore)cached).ExecuteInstructions(4, long.MaxValue, new BusAccessBatchBoundary()));
		AssertCachedRunParity(scalar, cached);

		// Change an unqueued extension word without changing the window generation.
		scalarBus.WriteWordRaw(0x1008, 0x1000);
		cachedBus.WriteWordRaw(0x1008, 0x1000);
		Assert.Equal(4, ((IM68kBatchCore)scalar).ExecuteInstructions(4, long.MaxValue, new BusAccessBatchBoundary()));
		Assert.Equal(4, ((IM68kBatchCore)cached).ExecuteInstructions(4, long.MaxValue, new BusAccessBatchBoundary()));
		AssertCachedRunParity(scalar, cached);
		Assert.True(cachedBus.Memory.AsSpan(0x1000, 4).SequenceEqual(scalarBus.Memory.AsSpan(0x1000, 4)));
		Assert.Equal(0x4E71, ReadWord(cachedBus.Memory, 0x1000));
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedMoveLongDataToAbsoluteLongMatchesScalar(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var program = Words(0x23C0, 0x0000, 0x3000); // MOVE.L D0,$3000.L
		var scalar = CreateParityCpu(program, enableOpcodePlan: false);
		var planned = CreateParityCpu(program, enableOpcodePlan: true, dispatch);
		SetupMoveFastShapeParityState(scalar.Cpu.State, scalar.Bus);
		SetupMoveFastShapeParityState(planned.Cpu.State, planned.Bus);
		scalar.Cpu.ExecuteInstruction();
		planned.Cpu.ExecuteInstruction();
		AssertParity(scalar, planned);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedQuickLongDataRegisterMatchesScalar(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var opcodes = new ushort[] { 0x5080, 0x5482, 0x5183, 0x5F84 };
		foreach (var opcode in opcodes)
		{
			var scalar = CreateParityCpu(Words(opcode), enableOpcodePlan: false);
			var planned = CreateParityCpu(Words(opcode), enableOpcodePlan: true, dispatch);
			scalar.Cpu.State.D[opcode & 7] = 0x7FFF_FFFE;
			planned.Cpu.State.D[opcode & 7] = 0x7FFF_FFFE;
			scalar.Cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Carry;
			planned.Cpu.State.StatusRegister = scalar.Cpu.State.StatusRegister;
			scalar.Cpu.ExecuteInstruction();
			planned.Cpu.ExecuteInstruction();
			AssertParity(scalar, planned);
		}
	}
	[Theory]
	[InlineData(0x41C0)] // LEA D0,A0
	[InlineData(0x4848)] // PEA A0
	[InlineData(0x4E80)] // JSR D0
	[InlineData(0x4EC0)] // JMP D0
	public void InvalidControlEffectiveAddressRaisesIllegalInstructionException(ushort opcode)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(opcode));
		bus.WriteLong(0x0010, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadBigEndianUInt32(bus.Memory, 0x2FFC, "stacked program counter"));
	}
	[Fact]
	public void PlannedInterpreterMatchesScalarForBranchBtstAndImmediateLoop()
	{
		var program = Words(
			0x322E, 0x0002, // MOVE.W 2(A6),D1
			0x0201, 0x00FF, // ANDI.B #$FF,D1
			0x6702, // BEQ.S skip
			0x5380, // SUBQ.L #1,D0
			0x0814, 0x000E, // BTST #14,(A4)
			0x66F2, // BNE.S start
			0x4E71); // NOP
		var scalar = CreateParityCpu(program, enableOpcodePlan: false);
		var planned = CreateParityCpu(program, enableOpcodePlan: true);
		SetupBranchParityState(scalar.Cpu.State, scalar.Bus);
		SetupBranchParityState(planned.Cpu.State, planned.Bus);
		planned.Cpu.PlannedInterpreterCountersEnabled = true;
		ExecuteBoth(scalar.Cpu, planned.Cpu, 48);
		AssertParity(scalar, planned);
		var counters = planned.Cpu.CapturePlannedInterpreterCounters();
		Assert.True(counters.BranchInstructions > 0);
		Assert.True(counters.ImmediateInstructions > 0);
		Assert.True(counters.ImmediateBtstInstructions > 0);
		Assert.True(counters.QuickRegisterInstructions > 0);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedDispatchVariantMatchesScalarForDbraD0Loop(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var program = Words(
			0x51C8, 0xFFFE, // DBRA D0,loop
			0x4E71); // NOP
		var scalar = CreateParityCpu(program, enableOpcodePlan: false);
		var planned = CreateParityCpu(program, enableOpcodePlan: true, dispatch);
		scalar.Cpu.State.D[0] = 3;
		planned.Cpu.State.D[0] = 3;
		ExecuteBoth(scalar.Cpu, planned.Cpu, 5);
		AssertParity(scalar, planned);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedDispatchVariantMatchesScalarForBranchBtstAndImmediateLoop(int dispatchValue)
	{
		var dispatch = (M68kOpcodePlanDispatch)dispatchValue;
		var program = Words(
			0x322E, 0x0002, // MOVE.W 2(A6),D1
			0x0201, 0x00FF, // ANDI.B #$FF,D1
			0x6702, // BEQ.S skip
			0x5380, // SUBQ.L #1,D0
			0x0814, 0x000E, // BTST #14,(A4)
			0x66F2, // BNE.S start
			0x4E71); // NOP
		var scalar = CreateParityCpu(program, enableOpcodePlan: false);
		var planned = CreateParityCpu(program, enableOpcodePlan: true, dispatch);
		SetupBranchParityState(scalar.Cpu.State, scalar.Bus);
		SetupBranchParityState(planned.Cpu.State, planned.Bus);
		planned.Cpu.PlannedInterpreterCountersEnabled = true;
		ExecuteBoth(scalar.Cpu, planned.Cpu, 48);
		AssertParity(scalar, planned);
		Assert.True(planned.Cpu.CapturePlannedInterpreterCounters().FastInstructions > 0);
	}
	[Fact]
	public void PlannedInterpreterUsesFetchedOpcodeNotProgramCounterCache()
	{
		var scalar = CreateParityCpu(Words(0x4E71), enableOpcodePlan: false);
		var planned = CreateParityCpu(Words(0x4E71), enableOpcodePlan: true);
		scalar.Cpu.ExecuteInstruction();
		planned.Cpu.ExecuteInstruction();
		Write(scalar.Bus.Memory, 0x1000, 0x70, 0x05); // MOVEQ #5,D0
		Write(planned.Bus.Memory, 0x1000, 0x70, 0x05);
		scalar.Cpu.State.ProgramCounter = 0x1000;
		planned.Cpu.State.ProgramCounter = 0x1000;
		scalar.Cpu.ExecuteInstruction();
		planned.Cpu.ExecuteInstruction();
		AssertParity(scalar, planned);
		Assert.Equal(5u, planned.Cpu.State.D[0]);
	}
	[Fact]
	public void WaitBlitPollingLoopUsesDocumentedCpuCadence()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x70, 0x04); // MOVEQ #4,D0
		Write(bus.Memory, 0x1002, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,.wpre
		Write(bus.Memory, 0x1006, 0x30, 0x2E, 0x00, 0x02); // MOVE.W 2(A6),D0
		Write(bus.Memory, 0x100A, 0x08, 0x00, 0x00, 0x0E); // BTST #14,D0
		Write(bus.Memory, 0x100E, 0x66, 0xF6); // BNE .wbusy
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[6] = 0x00DFF000;
		bus.WriteWordRaw(0x00DFF002, 0x4000);
		for (var i = 0; i < 64 && bus.DataReadCycles.Count(read => (read.Address & 0x00FF_FFFE) == 0x00DFF002) < 2; i++)
		{
			cpu.ExecuteInstruction();
		}
		var dmaconrReadCycles = bus.DataReadCycles
			.Where(read => (read.Address & 0x00FF_FFFE) == 0x00DFF002)
			.Select(read => read.Cycle)
			.ToArray();
		Assert.True(
			dmaconrReadCycles.Length >= 2,
			"Expected at least two DMACONR reads; data reads were " +
			string.Join(", ", bus.DataReadCycles.Select(read => $"0x{read.Address:X8}@{read.Cycle}")));
		Assert.Equal(60, dmaconrReadCycles[0]);
		// MOVE.W d16(An),Dn (12), BTST #imm,Dn (10), BNE taken (10).
		Assert.Equal(32, dmaconrReadCycles[1] - dmaconrReadCycles[0]);
	}
	[Fact]
	public void MoveaDoesNotAlterConditionCodes()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x20, 0x40); // MOVEA.L D0,A0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5678u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
	}
	[Fact]
	public void MovemPredecrementAndPostincrementRoundTripRegisters()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0xE7, 0xC0, 0xC0); // MOVEM.L D0-D1/A0-A1,-(A7)
		Write(bus.Memory, 0x1004, 0x4C, 0xDF, 0x03, 0x03); // MOVEM.L (A7)+,D0-D1/A0-A1
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1111_2222;
		cpu.State.D[1] = 0x3333_4444;
		cpu.State.A[0] = 0x5555_6666;
		cpu.State.A[1] = 0x7777_8888;
		cpu.ExecuteInstruction();
		cpu.State.D[0] = 0;
		cpu.State.D[1] = 0;
		cpu.State.A[0] = 0;
		cpu.State.A[1] = 0;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1111_2222u, cpu.State.D[0]);
		Assert.Equal(0x3333_4444u, cpu.State.D[1]);
		Assert.Equal(0x5555_6666u, cpu.State.A[0]);
		Assert.Equal(0x7777_8888u, cpu.State.A[1]);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}
	[Fact]
	public void MovemDiagRomSaveRestoreMaskPreservesReturnAddress()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0xE7, 0x7C, 0x40); // MOVEM.L D1-D5/A1,-(A7)
		Write(bus.Memory, 0x1004, 0x4C, 0xDF, 0x02, 0x3E); // MOVEM.L (A7)+,D1-D5/A1
		Write(bus.Memory, 0x1008, 0x4E, 0x75); // RTS
		bus.WriteLong(0x2FFC, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[7] = 0x2FFC;
		cpu.State.D[1] = 0x1111_1111;
		cpu.State.D[2] = 0x2222_2222;
		cpu.State.D[3] = 0x3333_3333;
		cpu.State.D[4] = 0x4444_4444;
		cpu.State.D[5] = 0x5555_5555;
		cpu.State.A[1] = 0xAAAA_AAAA;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2FE4u, cpu.State.A[7]);
		Assert.Equal(0x1111_1111u, ReadLong(bus.Memory, 0x2FE4));
		Assert.Equal(0x2222_2222u, ReadLong(bus.Memory, 0x2FE8));
		Assert.Equal(0x3333_3333u, ReadLong(bus.Memory, 0x2FEC));
		Assert.Equal(0x4444_4444u, ReadLong(bus.Memory, 0x2FF0));
		Assert.Equal(0x5555_5555u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0xAAAA_AAAAu, ReadLong(bus.Memory, 0x2FF8));
		Assert.Equal(0x0000_4000u, ReadLong(bus.Memory, 0x2FFC));
		cpu.State.D[1] = 0;
		cpu.State.D[2] = 0;
		cpu.State.D[3] = 0;
		cpu.State.D[4] = 0;
		cpu.State.D[5] = 0;
		cpu.State.A[1] = 0;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(0x1111_1111u, cpu.State.D[1]);
		Assert.Equal(0x2222_2222u, cpu.State.D[2]);
		Assert.Equal(0x3333_3333u, cpu.State.D[3]);
		Assert.Equal(0x4444_4444u, cpu.State.D[4]);
		Assert.Equal(0x5555_5555u, cpu.State.D[5]);
		Assert.Equal(0xAAAA_AAAAu, cpu.State.A[1]);
	}
	[Fact]
	public void MoveUspUsesCorrectSupervisorDirection()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x60); // MOVE A0,USP
		Write(bus.Memory, 0x1002, 0x4E, 0x69); // MOVE USP,A1
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x1234_5678;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5678u, cpu.State.UserStackPointer);
		Assert.Equal(0x1234_5678u, cpu.State.A[1]);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}
	[Fact]
	public void ExgAddressRegistersSwapsFullLongValues()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xC5, 0x4E); // EXG A2,A6
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[2] = 0x0000_0040;
		cpu.State.A[6] = 0x00C0_0276;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00C0_0276u, cpu.State.A[2]);
		Assert.Equal(0x0000_0040u, cpu.State.A[6]);
	}
	[Fact]
	public void AbcdDataRegisterUsesStickyZeroAndDecimalCarry()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xC5, 0x01); // ABCD D1,D2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[1] = 0x49;
		cpu.State.D[2] = 0x50;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x00u, cpu.State.D[2] & 0xFF);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void SbcdDataRegisterSubtractsPackedDecimalWithExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x85, 0x01); // SBCD D1,D2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[1] = 0x01;
		cpu.State.D[2] = 0x20;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		cpu.ExecuteInstruction();
		Assert.Equal(0x18u, cpu.State.D[2] & 0xFF);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void AbcdPredecrementMemoryAddsPackedDecimalAndUpdatesAddresses()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xC5, 0x09); // ABCD -(A1),-(A2)
		Write(bus.Memory, 0x2000, 0x49);
		Write(bus.Memory, 0x2100, 0x50);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[1] = 0x2001;
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0x2100u, cpu.State.A[2]);
		Assert.Equal(0x00, bus.Memory[0x2100]);
		Assert.Equal(18, cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void SbcdPredecrementMemorySubtractsPackedDecimalWithExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x85, 0x09); // SBCD -(A1),-(A2)
		Write(bus.Memory, 0x2000, 0x01);
		Write(bus.Memory, 0x2100, 0x20);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[1] = 0x2001;
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0x2100u, cpu.State.A[2]);
		Assert.Equal(0x18, bus.Memory[0x2100]);
		Assert.Equal(18, cycles);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void NbcdDataRegisterNegatesPackedDecimalWithExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0x02); // NBCD D2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[2] = 0x1234_5601;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5698u, cpu.State.D[2]);
		Assert.Equal(6, cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
	}
	[Fact]
	public void NbcdPostIncrementMemoryNegatesPackedDecimalAndAdvancesAddress()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0x18); // NBCD (A0)+
		Write(bus.Memory, 0x2000, 0x20);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero;
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(0x2001u, cpu.State.A[0]);
		Assert.Equal(0x80, bus.Memory[0x2000]);
		Assert.Equal(12, cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
	}
	[Fact]
	public void NegxByteDataRegisterUsesExtendAndClearsZeroForNonZeroResult()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x40, 0x00); // NEGX.B D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_5601;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(0x1234_56FEu, cpu.State.D[0]);
		Assert.Equal(4, cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
	}
	[Fact]
	public void NegxByteZeroResultPreservesClearedZeroFlag()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x40, 0x00); // NEGX.B D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(4, cycles);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
	}
	[Fact]
	public void MovecRaisesIllegalInstructionExceptionOnM68000()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x7B); // MOVEC on a 68000 raises illegal instruction
		bus.WriteLong(0x0010, 0x2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(ReadBigEndianUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x1000u, ReadBigEndianUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}
	[Fact]
	public void IllegalInstructionVectorsThroughIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4A, 0xFC); // ILLEGAL
		bus.WriteLong(0x0010, 0x2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(ReadBigEndianUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x1000u, ReadBigEndianUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}
	[Fact]
	public void LineAAndLineFOpcodesVectorThroughEmulatorExceptions()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xA0, 0x00); // Line-A emulator exception
		Write(bus.Memory, 0x2000, 0xF0, 0x00); // Line-F emulator exception
		bus.WriteLong(10 * 4, 0x3000);
		bus.WriteLong(11 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x3000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadBigEndianUInt32(bus.Memory, 0x4FFC, "line-A stacked program counter"));
		cpu.State.ProgramCounter = 0x2000;
		cpu.ExecuteInstruction();
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF4u, cpu.State.A[7]);
		Assert.Equal(0x2000u, ReadBigEndianUInt32(bus.Memory, 0x4FF6, "line-F stacked program counter"));
	}
	[Fact]
	public void UnregisteredLineFHostTrapOpcodeRaisesRealLineFException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xFF, 0x00, 0x12, 0x34);
		bus.WriteLong(11 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadBigEndianUInt32(bus.Memory, 0x4FFC, "line-F stacked program counter"));
		Assert.Contains(bus.Accesses, access => access.Address == 0x1002 && access.Kind == M68kBusAccessKind.CpuInstructionFetch);
	}
	[Fact]
	public void ResetInstructionSignalsExternalDevices()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x70); // RESET
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(1, bus.ExternalResetCount);
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void StopInstructionWaitsUntilAcceptedInterrupt()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x72, 0x20, 0x00); // STOP #$2000
		bus.WriteLong(0x0070, 0x2000); // level 4 autovector
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		var stoppedCycle = cpu.State.Cycles;
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.Stopped);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		Assert.Equal(stoppedCycle + 1, cpu.State.Cycles);
		cpu.RequestInterrupt(4, 0x70);
		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1004u, ReadBigEndianUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}
	[Fact]
	public void StopInstructionRaisesPrivilegeViolationInUserMode()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x72, 0x20, 0x00); // STOP #$2000
		bus.WriteLong(0x0020, 0x2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x3000, userStackPointer: 0x4000, supervisorMode: false);
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadBigEndianUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}
	[Fact]
	public void TimedWritesReachBusInProgramOrder()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x33, 0xFC, 0x80, 0x0F, 0x00, 0xDF, 0xF0, 0x96); // MOVE.W #$800F,$DFF096
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.ExecuteInstruction();
		Assert.True(bus.Writes.Count >= 2);
		Assert.Equal((uint)0x00DFF096, bus.Writes[^2].Address);
		Assert.Equal((uint)0x00DFF097, bus.Writes[^1].Address);
		Assert.True(bus.Writes[^1].Cycle >= bus.Writes[^2].Cycle);
	}
	[Fact]
	public void JsrPcRelativeUsesExtensionWordAsBase()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0xBA, 0x00, 0x08); // JSR 8(PC), target 0x100A
		Write(bus.Memory, 0x1004, 0x4E, 0x75); // RTS to sentinel after subroutine returns
		Write(bus.Memory, 0x100A, 0x70, 0x7F); // MOVEQ #$7F,D0
		Write(bus.Memory, 0x100C, 0x4E, 0x75); // RTS
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		bus.WriteLong(0x1FFC, 0xFFFF_FFFC);
		cpu.State.A[7] = 0x1FFC;
		for (var i = 0; i < 8 && cpu.State.ProgramCounter != 0xFFFF_FFFC; i++)
		{
			cpu.ExecuteInstruction();
		}
		Assert.Equal(0x7Fu, cpu.State.D[0]);
	}
	[Fact]
	public void ExtWordClearsStaleByteStateForJumpTableIndexes()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x04, 0x00, 0x00, 0x80); // SUBI.B #$80,D0
		Write(bus.Memory, 0x1004, 0x48, 0x80); // EXT.W D0
		Write(bus.Memory, 0x1006, 0x41, 0xFA, 0x00, 0x10); // LEA table(PC),A0
		Write(bus.Memory, 0x100A, 0xD0, 0xC0); // ADDA.W D0,A0
		Write(bus.Memory, 0x100C, 0x30, 0x10); // MOVE.W (A0),D0
		Write(bus.Memory, 0x100E, 0x4E, 0x75); // RTS
		Write(bus.Memory, 0x1018, 0x00, 0x24); // table entry selected by D0.W = 0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		bus.WriteLong(0x1FFC, 0xFFFF_FFFC);
		cpu.State.A[7] = 0x1FFC;
		cpu.State.D[0] = 0x1234_5680;
		for (var i = 0; i < 8 && cpu.State.ProgramCounter != 0xFFFF_FFFC; i++)
		{
			cpu.ExecuteInstruction();
		}
		Assert.Equal(0x1234_0024u, cpu.State.D[0]);
	}
	[Fact]
	public void DynamicBclrClearsMemoryBitAndSetsZeroFromPreviousValue()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x03, 0xB9, 0x00, 0x00, 0x20, 0x00); // BCLR D1,$2000.L
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[1] = 3;
		bus.Memory[0x2000] = 0xFF;
		cpu.ExecuteInstruction();
		Assert.Equal(0xF7, bus.Memory[0x2000]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void ImmediateBitOperationRejectsAddressRegisterDestination()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x08, 0xC8, 0x00, 0x00); // BSET #0,A0 is illegal on MC68000
		Write(bus.Memory, 4 * 4, 0x00, 0x00, 0x40, 0x00);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.SupervisorStackPointer);
		Assert.Equal((ushort)M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void DynamicBtstAllowsPcRelativeSource()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(
			0x013A, 0x0002, // BTST D0,2(PC)
			0x0100, 0x0000));
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void MoveCcrToAddressRegisterRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0x44C8)); // MOVE.B A0,CCR is illegal
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void MoveSrToAddressRegisterRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0x46C8)); // MOVE SR,A0 is illegal
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = 0;

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void RegisterToDataRegisterArithmeticEncodingRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0xC180)); // AND.L D0,D0 in the register-to-EA form is illegal
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Theory]
	[InlineData(0x4888)] // EXT.W A0 is illegal
	[InlineData(0x48C8)] // EXT.L A0 is illegal
	public void ExtAddressRegisterEncodingRaisesIllegalInstructionException(ushort opcode)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(opcode));
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void TasAddressRegisterEncodingRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0x4AC8)); // TAS A0 is illegal
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void ByteQuickArithmeticToAddressRegisterRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0x5008)); // ADDQ.B #8,A0 is illegal
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void QuickArithmeticRejectsPcRelativeDestination()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0x503A, 0x0002)); // ADDQ.B #8,2(PC) is illegal
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void ByteArithmeticRejectsAddressRegisterSource()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(0x8008)); // OR.B A0,D0 is illegal
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Theory]
	[InlineData(0x80C8)] // DIVU.W A0,D0 is illegal
	[InlineData(0xC0C8)] // MULU.W A0,D0 is illegal
	public void MultiplyAndDivideRejectAddressRegisterSource(ushort opcode)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, Words(opcode));
		bus.WriteLong(4 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void MovepExecutesOn68000Interpreter()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x05, 0x49, 0x00, 0x11); // MOVEP.L $11(A1),D2
		Write(bus.Memory, 0x1004, 0x01, 0x89, 0x00, 0x19); // MOVEP.W D0,$19(A1)
		bus.Memory[0x3011] = 0x12;
		bus.Memory[0x3013] = 0x34;
		bus.Memory[0x3015] = 0x56;
		bus.Memory[0x3017] = 0x78;
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[1] = 0x3000;
		cpu.State.D[0] = 0xAABB_CDEF;
		cpu.State.D[2] = 0xFFFF_FFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5678u, cpu.State.D[2]);
		Assert.Equal(0xCD, bus.Memory[0x3019]);
		Assert.Equal(0xEF, bus.Memory[0x301B]);
		Assert.Equal(0x1008u, cpu.State.ProgramCounter);
		Assert.Equal(M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry,
			cpu.State.StatusRegister);
	}
	[Fact]
	public void ImmediateBtstDisplacementAddressUsesMemoryEaTiming()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x08, 0x2A, 0x00, 0x06, 0x00, 0x02); // BTST #6,2(A2)
		bus.Memory[0x2002] = 0x40;
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[2] = 0x2000;
		cpu.ExecuteInstruction();
		Assert.Equal(16, cpu.State.Cycles);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void AddqSubqAddressRegistersUseLongArithmeticAndDoNotChangeFlags()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x55, 0x48); // SUBQ.W #2,A0, size ignored for address registers
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x0007_B3E6;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0007_B3E4u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
	}
	[Fact]
	public void DivsAcceptsNegativeQuotientThatFitsWord()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x81, 0xD0); // DIVS.W (A0),D0
		Write(bus.Memory, 0x2000, 0x00, 0x03);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 0xFFFF_FFF6; // -10
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_FFFDu, cpu.State.D[0]); // remainder -1, quotient -3
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void DivsByZeroVectorsThroughZeroDivideException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x81, 0xFC, 0x00, 0x00); // DIVS.W #0,D0
		bus.WriteLong(5 * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.Halted);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(ReadBigEndianUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x0000_1004u, ReadBigEndianUInt32(bus.Memory, 0x2FFC, "saved program counter"));
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
	}
	[Fact]
	public void DivuByZeroVectorsThroughZeroDivideException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x80, 0xFC, 0x00, 0x00); // DIVU.W #0,D0
		bus.WriteLong(5 * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x89AB_CDEF;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.Halted);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x0000_1004u, ReadBigEndianUInt32(bus.Memory, 0x2FFC, "saved program counter"));
		Assert.Equal(0x89AB_CDEFu, cpu.State.D[0]);
	}
	[Fact]
	public void DivsOverflowLeavesDestinationUnchanged()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x81, 0xD0); // DIVS.W (A0),D0
		Write(bus.Memory, 0x2000, 0x00, 0x01);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 0xFFFF_7FFF; // -32769
		cpu.ExecuteInstruction();
		Assert.Equal(0xFFFF_7FFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
	}
	[Fact]
	public void CmpaWordComparesSignExtendedOperandAgainstFullAddressRegister()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xB0, 0xC0); // CMPA.W D0,A0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x0001_0000;
		cpu.State.D[0] = 0;
		cpu.State.StatusRegister |= M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
	}
	[Fact]
	public void CmpmByteComparesPostincrementMemoryAndPreservesExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xB9, 0x0B); // CMPM.B (A3)+,(A4)+
		bus.Memory[0x2000] = (byte)'m';
		bus.Memory[0x3000] = (byte)'m';
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.State.A[3] = 0x2000;
		cpu.State.A[4] = 0x3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;
		var cycles = cpu.ExecuteInstruction();
		Assert.Equal(0x2001u, cpu.State.A[3]);
		Assert.Equal(0x3001u, cpu.State.A[4]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(12, cycles);
	}
	[Fact]
	public void TrapPushesExceptionFrameAndVectorsThroughTrapTable()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x41); // TRAP #1
		bus.WriteLong((32 + 1) * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Carry;
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Carry, (ushort)((bus.Memory[0x2FFA] << 8) | bus.Memory[0x2FFB]));
		Assert.Equal(0x0000_1002u, ((uint)bus.Memory[0x2FFC] << 24) |
			((uint)bus.Memory[0x2FFD] << 16) |
			((uint)bus.Memory[0x2FFE] << 8) |
			bus.Memory[0x2FFF]);
		Assert.True(cpu.State.Cycles >= 34);
	}
	[Fact]
	public void StatusRegisterSupervisorBitSwitchesBetweenUserAndSupervisorStacks()
	{
		var bus = new TestBus();
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x0400);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x0400, userStackPointer: 0x2000, supervisorMode: false);
		cpu.State.SetActiveStackPointer(0x1FF0);
		cpu.State.StatusRegister |= M68kCpuState.Supervisor;
		Assert.Equal(0x1FF0u, cpu.State.UserStackPointer);
		Assert.Equal(0x0400u, cpu.State.A[7]);
		cpu.State.SetActiveStackPointer(0x03F8);
		cpu.State.StatusRegister &= unchecked((ushort)~M68kCpuState.Supervisor);
		Assert.Equal(0x03F8u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x1FF0u, cpu.State.A[7]);
	}
	[Fact]
	public void TrapFromUserModeUsesSupervisorStack()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x41); // TRAP #1
		bus.WriteLong((32 + 1) * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x0400);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x0400, userStackPointer: 0x3000, supervisorMode: false);
		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x03FAu, cpu.State.A[7]);
		Assert.Equal(0x3000u, cpu.State.UserStackPointer);
		Assert.Equal(0x0000, (bus.Memory[0x03FA] << 8) | bus.Memory[0x03FB]);
		Assert.Equal(0x0000_1002u, ((uint)bus.Memory[0x03FC] << 24) |
			((uint)bus.Memory[0x03FD] << 16) |
			((uint)bus.Memory[0x03FE] << 8) |
			bus.Memory[0x03FF]);
	}
	[Fact]
	public void RteRestoresUserStackAfterReadingSupervisorExceptionFrame()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x73); // RTE
		Write(bus.Memory, 0x03FA, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x0400);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x03FA, userStackPointer: 0x3000, supervisorMode: true);
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(0x0400u, cpu.State.SupervisorStackPointer);
	}
	[Fact]
	public void RteCommittedTargetExtensionBlocksRetirementAndImmediateInterruptEntry()
	{
		var bus = new CycleCountingBus
		{
			DelayedInstructionFetchAddress = 0x2002,
			DelayedInstructionFetchCycles = 24
		};
		Write(bus.Memory, 0x0064, 0x00, 0x00, 0x30, 0x00);
		Write(bus.Memory, 0x1000, 0x4E, 0x73); // RTE
		Write(bus.Memory, 0x03FA, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00);
		Write(bus.Memory, 0x2000, 0x4E, 0x71, 0x4E, 0x71);
		Write(bus.Memory, 0x3000, 0x4E, 0x71, 0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x0400);
		cpu.State.ResetStackPointers(
			supervisorStackPointer: 0x03FA,
			userStackPointer: 0x4000,
			supervisorMode: true);

		var elapsed = cpu.ExecuteInstruction();
		var transition = ((IM68000PipelineStateTransfer)cpu).ExportM68000PipelineState();
		var extensionReadyCycle = transition.ReadyCycle1;

		Assert.Equal(extensionReadyCycle, elapsed);
		Assert.Equal(extensionReadyCycle, transition.ExceptionEntryNotBeforeCycle);

		var interruptPhaseStart = bus.CpuBusPhases.Count;
		cpu.RequestInterrupt(1, 0x0064);
		var firstInterruptPhase = bus.CpuBusPhases
			.Skip(interruptPhaseStart)
			.First();

		Assert.True(firstInterruptPhase.RequestedCycle >= extensionReadyCycle);
	}
	[Fact]
	public void RoxrUsesExtendAsIncomingBitAndUpdatesCarryExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x44, 0xFC, 0x00, 0x10); // MOVE #$10,CCR
		Write(bus.Memory, 0x1004, 0xE2, 0x90); // ROXR.L #1,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x0000_0001;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x8000_0000u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
	}
	[Fact]
	public void AddxDataRegisterUsesExtendAndPreservesUpperBits()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xD1, 0x01); // ADDX.B D1,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_56FF;
		cpu.State.D[1] = 0x0000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5601u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void SubxPredecrementUsesMemoryOperandsAndExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x91, 0x49); // SUBX.W -(A1),-(A0)
		Write(bus.Memory, 0x2000, 0x00, 0x01);
		Write(bus.Memory, 0x3000, 0x00, 0x00);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.State.A[0] = 0x3002;
		cpu.State.A[1] = 0x2002;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;
		cpu.ExecuteInstruction();
		Assert.Equal(0x3000u, cpu.State.A[0]);
		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0xFFFE, (bus.Memory[0x3000] << 8) | bus.Memory[0x3001]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}
	[Fact]
	public void CpuInstructionFetchAndLongWriteUseCycleAwareBusAccesses()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x20, 0x80); // MOVE.L D0,(A0)
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[0] = 0x2000;
		cpu.ExecuteInstruction();
		Assert.Contains(
			bus.Accesses,
			access =>
				access.Address == 0x1000 &&
				access.Kind == M68kBusAccessKind.CpuInstructionFetch &&
				access.Size == TestAccessSize.Word &&
				!access.IsWrite);
		var write = Assert.Single(bus.Accesses, access => access.Kind == M68kBusAccessKind.CpuDataWrite && access.IsWrite);
		Assert.Equal(0x2000u, write.Address);
		Assert.Equal(TestAccessSize.Long, write.Size);
		Assert.Equal(0x12, bus.Memory[0x2000]);
		Assert.Equal(0x34, bus.Memory[0x2001]);
		Assert.Equal(0x56, bus.Memory[0x2002]);
		Assert.Equal(0x78, bus.Memory[0x2003]);
	}
	[Fact]
	public void InterpreterUsesInstructionFetchWindowForSequentialOpcodeWords()
	{
		var bus = new InstructionFetchWindowBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x71); // NOP
		Write(bus.Memory, 0x1002, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(1, bus.WindowRequests);
		Assert.Equal(4, bus.WindowCommits);
		Assert.Equal(0, bus.GenericInstructionFetchWordReads);
		Assert.Equal(8, cpu.State.Cycles);
	}
	[Fact]
	public void PrefetchedSequentialOpcodeSurvivesSelfModifyingWrite()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x21, 0xFC, 0x70, 0x05, 0x4E, 0x71, 0x10, 0x08); // MOVE.L #$70054E71,$1008.W
		Write(bus.Memory, 0x1008, 0x70, 0x01); // MOVEQ #1,D0, already prefetched before the write
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x7005, ReadWord(bus.Memory, 0x1008));
		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(0x7001, cpu.State.LastOpcode);
	}
	[Theory]
	[MemberData(nameof(OpcodePlanDispatchVariants))]
	public void PlannedMoveLongDataToAbsoluteLongPreservesPrefetchedSequentialOpcode(int dispatchValue)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x23, 0xC0, 0x00, 0x00, 0x10, 0x06); // MOVE.L D0,$1006.L
		Write(bus.Memory, 0x1006, 0x70, 0x01); // MOVEQ #1,D0, prefetched before the write
		var cpu = new M68kInterpreter(
			bus,
			new M68kCpuState(),
			instructionFrequency: null,
			enableInstructionFetchWindow: true,
			enableOpcodePlan: true,
			opcodePlanDispatch: (M68kOpcodePlanDispatch)dispatchValue);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x7005_4E71;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x7005, ReadWord(bus.Memory, 0x1006));
		Assert.Equal(1u, cpu.State.D[0]);
		Assert.Equal(0x7001, cpu.State.LastOpcode);
	}
	[Fact]
	public void TakenJumpFlushesSelfModifiedPrefetchTarget()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x21, 0xFC, 0x70, 0x05, 0x4E, 0x71, 0x10, 0x08); // MOVE.L #$70054E71,$1008.W
		Write(bus.Memory, 0x1008, 0x4E, 0xD0); // JMP (A0), already prefetched before the write
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x1008;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x1008u, cpu.State.ProgramCounter);
		cpu.ExecuteInstruction();
		Assert.Equal(5u, cpu.State.D[0]);
		Assert.Equal(0x7005, cpu.State.LastOpcode);
		Assert.Equal(0x100Au, cpu.State.ProgramCounter);
	}
	[Fact]
	public void ClrMemoryReadsDestinationBeforeWritingZero()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x42, 0x90); // CLR.L (A0)
		Write(bus.Memory, 0x2000, 0x12, 0x34, 0x56, 0x78);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.ExecuteInstruction();
		var dataAccesses = bus.Accesses
			.Where(access => access.Kind is M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite)
			.ToArray();
		Assert.Equal(2, dataAccesses.Length);
		Assert.Equal((uint)0x2000, dataAccesses[0].Address);
		Assert.Equal(TestAccessSize.Long, dataAccesses[0].Size);
		Assert.False(dataAccesses[0].IsWrite);
		Assert.Equal((uint)0x2000, dataAccesses[1].Address);
		Assert.Equal(TestAccessSize.Long, dataAccesses[1].Size);
		Assert.True(dataAccesses[1].IsWrite);
		Assert.True(dataAccesses[1].Cycle >= dataAccesses[0].Cycle);
		Assert.Equal(0u, ReadBigEndianUInt32(bus.Memory, 0x2000, "cleared longword"));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}
	[Fact]
	public void InterpreterUsesExactCpuDataBusForMemoryOperands()
	{
		var bus = new ExactCpuDataTestBus();
		Write(bus.Memory, 0x1000, 0x20, 0x10); // MOVE.L (A0),D0
		Write(bus.Memory, 0x1002, 0x22, 0x80); // MOVE.L D0,(A1)
		Write(bus.Memory, 0x2000, 0x12, 0x34, 0x56, 0x78);
		var cpu = M68kCoreFactory.CreateM68000Core(bus, default(ExactCpuDataTestAccess));
		cpu.Reset(0x1000, 0x4000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[1] = 0x3000;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(0x1234_5678u, ReadLong(bus.Memory, 0x3000));
		Assert.Equal(1, bus.ExactReadLongCount);
		Assert.Equal(1, bus.ExactWriteLongCount);
		Assert.Equal(0, bus.GenericDataReadCount);
		Assert.Equal(0, bus.GenericDataWriteCount);
	}
	[Fact]
	public void OddInstructionFetchRaisesAddressErrorWithProgramFunctionCode()
	{
		var bus = new TestBus();
		bus.WriteLong(0x000C, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1001, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x0016, ReadWord(bus.Memory, 0x2FF2));
		Assert.Equal(0x0000_1001u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0x0000, ReadWord(bus.Memory, 0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1001u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void InvalidImmediateSizeRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x00, 0xF8);
		bus.WriteLong(0x0010, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	[Fact]
	public void InvalidMode7EffectiveAddressRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x30, 0xBF);
		bus.WriteLong(0x0010, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1000u, ReadLong(bus.Memory, 0x2FFC));
	}
	public static IEnumerable<object[]> OpcodePlanDispatchVariants()
	{
		yield return new object[] { (int)M68kOpcodePlanDispatch.KindTable };
		yield return new object[] { (int)M68kOpcodePlanDispatch.PackedPlan };
	}
	private static byte[] Words(params ushort[] words)
	{
		var data = new byte[words.Length * 2];
		for (var i = 0; i < words.Length; i++)
		{
			data[i * 2] = (byte)(words[i] >> 8);
			data[(i * 2) + 1] = (byte)words[i];
		}
		return data;
	}
	private static CycleCountingBus CreateFastMemoryRunBus()
		=> new()
		{
			ZeroWaitInstructionFetch = true,
			FixedPlanRunEnabled = true,
			FastMemoryEnabled = true,
			FixedPlanWindowStart = 0x1000,
			FixedPlanWindowEndExclusive = 0x1100
		};
	private static void WriteLongRaw(byte[] memory, uint address, uint value)
	{
		memory[address] = (byte)(value >> 24);
		memory[address + 1] = (byte)(value >> 16);
		memory[address + 2] = (byte)(value >> 8);
		memory[address + 3] = (byte)value;
	}
	private static uint ReadLongRaw(byte[] memory, uint address)
		=> ((uint)memory[address] << 24) |
			((uint)memory[address + 1] << 16) |
			((uint)memory[address + 2] << 8) |
			memory[address + 3];
	private static ParityRun CreateParityCpu(
		byte[] program,
		bool enableOpcodePlan,
		M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable)
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, program);
		var cpu = new M68kInterpreter(
			bus,
			new M68kCpuState(),
			instructionFrequency: null,
			enableInstructionFetchWindow: true,
			enableOpcodePlan: enableOpcodePlan,
			opcodePlanDispatch: opcodePlanDispatch);
		cpu.Reset(0x1000, 0x8000);
		return new ParityRun(cpu, bus);
	}
	private static M68kInterpreter CreateCycleParityCpu(
		CycleCountingBus bus,
		bool enableOpcodePlan,
		M68kOpcodePlanDispatch opcodePlanDispatch = M68kOpcodePlanDispatch.KindTable,
		bool enableCpuBusPhaseTrace = true)
	{
		var cpu = new M68kInterpreter(
			bus,
			new M68kCpuState(),
			instructionFrequency: null,
			enableInstructionFetchWindow: true,
			enableCpuBusPhaseTrace: enableCpuBusPhaseTrace,
			enableOpcodePlan: enableOpcodePlan,
			opcodePlanDispatch: opcodePlanDispatch);
		cpu.Reset(0x1000, 0x8000);
		return cpu;
	}
	private static void AssertCycleParity(
		M68kInterpreter scalar,
		CycleCountingBus scalarBus,
		M68kInterpreter planned,
		CycleCountingBus plannedBus)
	{
		Assert.Equal(scalar.State.ProgramCounter, planned.State.ProgramCounter);
		Assert.Equal(scalar.State.Cycles, planned.State.Cycles);
		Assert.Equal(scalar.State.StatusRegister, planned.State.StatusRegister);
		Assert.Equal(scalar.State.LastOpcode, planned.State.LastOpcode);
		Assert.Equal(scalar.State.LastInstructionProgramCounter, planned.State.LastInstructionProgramCounter);
		Assert.Equal(scalar.State.D, planned.State.D);
		Assert.Equal(scalar.State.A, planned.State.A);
		Assert.Equal(scalarBus.InstructionFetchCycles.ToArray(), plannedBus.InstructionFetchCycles.ToArray());
		Assert.Equal(
			scalarBus.CpuBusPhases.Select(ToComparableBusPhase).ToArray(),
			plannedBus.CpuBusPhases.Select(ToComparableBusPhase).ToArray());
		Assert.True(
			scalarBus.Memory.AsSpan(0x1000, 0x20).SequenceEqual(plannedBus.Memory.AsSpan(0x1000, 0x20)),
			"planned execution changed code memory differently from scalar execution");
	}
	private static void AssertCachedRunParity(M68kInterpreter scalar, M68kInterpreter cached)
	{
		Assert.Equal(scalar.State.ProgramCounter, cached.State.ProgramCounter);
		Assert.Equal(scalar.State.Cycles, cached.State.Cycles);
		Assert.Equal(scalar.State.StatusRegister, cached.State.StatusRegister);
		Assert.Equal(scalar.State.LastOpcode, cached.State.LastOpcode);
		Assert.Equal(
			scalar.State.LastInstructionProgramCounter,
			cached.State.LastInstructionProgramCounter);
		Assert.Equal(scalar.State.D, cached.State.D);
		Assert.Equal(scalar.State.A, cached.State.A);
		Assert.Equal(
			((IM68000PrefetchDiagnostics)scalar).CapturePrefetchDiagnosticState(),
			((IM68000PrefetchDiagnostics)cached).CapturePrefetchDiagnosticState());
		Assert.Equal(
			((IM68000InterruptRecognition)scalar).LastInterruptSampleCycle,
			((IM68000InterruptRecognition)cached).LastInterruptSampleCycle);
	}
	private static object ToComparableBusPhase(M68kCpuBusPhase phase)
		=> new
		{
			phase.InstructionProgramCounter,
			phase.Address,
			phase.Size,
			phase.RequestedCycle,
			phase.CompletedCycle,
			phase.AccessKind,
			phase.IsWrite,
			phase.StatusRegister
		};
	private static void SetupTransformParityState(M68kCpuState state, TestBus bus)
	{
		state.A[0] = 0x2000;
		state.A[1] = 0x3000;
		state.A[2] = 0x2400;
		state.D[2] = 3;
		state.D[3] = 0x5555_5555;
		state.D[7] = 0x0F0F_0F0F;
		for (var offset = 0; offset < 0x80; offset += 4)
		{
			bus.WriteLong(0x2000u + (uint)offset, 0x0102_0304u + (uint)offset);
			bus.WriteLong(0x2400u + (uint)offset, 0x1020_3040u + (uint)offset);
		}
	}
	private static ushort RegisterArithmeticOpcode(int line, int register, int opmode, int mode, int eaRegister)
		=> (ushort)((line << 12) | (register << 9) | (opmode << 6) | (mode << 3) | eaRegister);
	private static void SetupRegisterArithmeticEaParityState(M68kCpuState state, TestBus bus)
	{
		state.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		state.A[0] = 0x2000;
		state.D[1] = 0x1234_5678;
		bus.WriteLong(0x2000, 0x8000_1234);
		bus.WriteLong(0x2004, 0x0102_0304);
	}
	private static void SetupFaultingArithmeticSource(ParityRun run, ushort initialStatus, uint a0)
	{
		run.Bus.WriteLong(0x000C, 0x0000_4000);
		run.Cpu.State.A[0] = a0;
		run.Cpu.State.D[0] = 0xAAAA_5555;
		run.Cpu.State.StatusRegister = initialStatus;
	}
	private static void AssertFaultingArithmeticSource(
		ParityRun run,
		ushort initialStatus,
		uint expectedA0,
		uint expectedStackedPc)
	{
		Assert.Equal(0x0000_4000u, run.Cpu.State.ProgramCounter);
		Assert.Equal(0x7FF2u, run.Cpu.State.A[7]);
		Assert.Equal(0x0000_2001u, ReadLong(run.Bus.Memory, 0x7FF4));
		Assert.Equal(initialStatus, ReadWord(run.Bus.Memory, 0x7FFA));
		Assert.Equal(expectedStackedPc, ReadLong(run.Bus.Memory, 0x7FFC));
		Assert.Equal(expectedA0, run.Cpu.State.A[0]);
		Assert.Equal(0xAAAA_5555u, run.Cpu.State.D[0]);
	}
	private static void SetupMoveFastShapeParityState(M68kCpuState state, TestBus bus)
	{
		state.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		state.A[0] = 0x2000;
		state.A[1] = 0x3000;
		state.D[0] = 0x89AB_CDEF;
		bus.WriteLong(0x2000, 0x8000_1234);
		bus.WriteLong(0x2004, 0x0000_0000);
		bus.WriteLong(0x3000, 0x5555_5555);
		bus.WriteLong(0x3004, 0x5555_5555);
	}
	private static void SetupMoveDisplacementToDataParityState(M68kCpuState state, TestBus bus)
	{
		state.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;
		state.A[0] = 0x2000;
		state.A[6] = 0x2000;
		state.D[1] = 0x89AB_CDEF;
		bus.WriteLong(0x2000, 0x8000_1234);
		bus.WriteLong(0x2004, 0x0000_0000);
	}
	private static void SetupBranchParityState(M68kCpuState state, TestBus bus)
	{
		state.D[0] = 4;
		state.A[4] = 0x2400;
		state.A[6] = 0x2000;
		Write(bus.Memory, 0x2002, 0x00, 0x7F);
		Write(bus.Memory, 0x2400, 0x40, 0x00);
	}
	private static void ExecuteBoth(M68kInterpreter scalar, M68kInterpreter planned, int instructions)
	{
		for (var i = 0; i < instructions; i++)
		{
			scalar.ExecuteInstruction();
			planned.ExecuteInstruction();
		}
	}
	private static void AssertParity(ParityRun scalar, ParityRun planned)
	{
		Assert.Equal(scalar.Cpu.State.ProgramCounter, planned.Cpu.State.ProgramCounter);
		Assert.Equal(scalar.Cpu.State.Cycles, planned.Cpu.State.Cycles);
		Assert.Equal(scalar.Cpu.State.StatusRegister, planned.Cpu.State.StatusRegister);
		Assert.Equal(scalar.Cpu.State.LastOpcode, planned.Cpu.State.LastOpcode);
		Assert.Equal(scalar.Cpu.State.LastInstructionProgramCounter, planned.Cpu.State.LastInstructionProgramCounter);
		Assert.Equal(scalar.Cpu.State.D, planned.Cpu.State.D);
		Assert.Equal(scalar.Cpu.State.A, planned.Cpu.State.A);
		for (var address = 0x1000; address < 0x3100; address++)
		{
			Assert.Equal(scalar.Bus.Memory[address], planned.Bus.Memory[address]);
		}
	}
	private static void Write(byte[] memory, int address, params byte[] data)
	{
		Array.Copy(data, 0, memory, address, data.Length);
	}
	private static ushort ReadWord(byte[] memory, int address)
		=> (ushort)((memory[address] << 8) | memory[address + 1]);
	private static uint ReadLong(byte[] memory, int address)
		=> ((uint)memory[address] << 24) |
			((uint)memory[address + 1] << 16) |
			((uint)memory[address + 2] << 8) |
			memory[address + 3];
	private static ushort ReadBigEndianUInt16(byte[] data, int offset, string description)
	{
	_ = description;
	return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
	}
	private static uint ReadBigEndianUInt32(byte[] data, int offset, string description)
	{
	_ = description;
	return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
	}
	private enum TestAccessSize
	{
		Byte,
		Word,
		Long
	}
	private sealed record ParityRun(M68kInterpreter Cpu, TestBus Bus);
	private readonly struct ExactCpuDataTestAccess : IM68kCpuDataAccess<ExactCpuDataTestBus, ExactCpuDataTestAccess>
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static byte ReadByte(ExactCpuDataTestBus bus, uint address, ref long cycle)
				=> bus.TryReadExactCpuDataByte(address, ref cycle, out var value)
					? value
					: ReadByteFallback(bus, address, ref cycle);
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static ushort ReadWord(ExactCpuDataTestBus bus, uint address, ref long cycle)
				=> bus.TryReadExactCpuDataWord(address, ref cycle, out var value)
					? value
					: ReadWordFallback(bus, address, ref cycle);
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static uint ReadLong(ExactCpuDataTestBus bus, uint address, ref long cycle)
				=> bus.TryReadExactCpuDataLong(address, ref cycle, out var value)
					? value
					: ReadLongFallback(bus, address, ref cycle);
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static void WriteByte(ExactCpuDataTestBus bus, uint address, byte value, ref long cycle)
			{
				if (!bus.TryWriteExactCpuDataByte(address, value, ref cycle))
				{
					WriteByteFallback(bus, address, value, ref cycle);
				}
			}
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static void WriteTasByte(ExactCpuDataTestBus bus, uint address, byte value, ref long cycle)
				=> WriteByte(bus, address, value, ref cycle);
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static void WriteWord(ExactCpuDataTestBus bus, uint address, ushort value, ref long cycle)
			{
				if (!bus.TryWriteExactCpuDataWord(address, value, ref cycle))
				{
					WriteWordFallback(bus, address, value, ref cycle);
				}
			}
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public static void WriteLong(ExactCpuDataTestBus bus, uint address, uint value, ref long cycle)
			{
				if (!bus.TryWriteExactCpuDataLong(address, value, ref cycle))
				{
					WriteLongFallback(bus, address, value, ref cycle);
				}
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			private static byte ReadByteFallback(ExactCpuDataTestBus bus, uint address, ref long cycle)
				=> bus.ReadByte(address, ref cycle, M68kBusAccessKind.CpuDataRead);
			[MethodImpl(MethodImplOptions.NoInlining)]
			private static ushort ReadWordFallback(ExactCpuDataTestBus bus, uint address, ref long cycle)
				=> bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuDataRead);
			[MethodImpl(MethodImplOptions.NoInlining)]
			private static uint ReadLongFallback(ExactCpuDataTestBus bus, uint address, ref long cycle)
				=> bus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead);
			[MethodImpl(MethodImplOptions.NoInlining)]
			private static void WriteByteFallback(ExactCpuDataTestBus bus, uint address, byte value, ref long cycle)
				=> bus.WriteByte(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
			[MethodImpl(MethodImplOptions.NoInlining)]
			private static void WriteWordFallback(ExactCpuDataTestBus bus, uint address, ushort value, ref long cycle)
				=> bus.WriteWord(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
			[MethodImpl(MethodImplOptions.NoInlining)]
			private static void WriteLongFallback(ExactCpuDataTestBus bus, uint address, uint value, ref long cycle)
				=> bus.WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
		}
	private sealed class ExactCpuDataTestBus : IM68kBus
		{
			public byte[] Memory { get; } = new byte[0x0100_0000];
			public int ExactReadLongCount { get; private set; }
			public int ExactWriteLongCount { get; private set; }
			public int GenericDataReadCount { get; private set; }
			public int GenericDataWriteCount { get; private set; }
			public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
			{
				RecordGenericAccess(accessKind, isWrite: false);
				cycle += 2;
				return Memory[address];
			}
			public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
			{
				RecordGenericAccess(accessKind, isWrite: false);
				cycle += 4;
				return (ushort)((Memory[address] << 8) | Memory[address + 1]);
			}
			public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
			{
				RecordGenericAccess(accessKind, isWrite: false);
				cycle += 8;
				return ReadLongValue(address);
			}
			public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
			{
				RecordGenericAccess(accessKind, isWrite: true);
				Memory[address] = value;
				cycle += 2;
			}
			public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
			{
				RecordGenericAccess(accessKind, isWrite: true);
				Memory[address] = (byte)(value >> 8);
				Memory[address + 1] = (byte)value;
				cycle += 4;
			}
			public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
			{
				RecordGenericAccess(accessKind, isWrite: true);
				WriteLongValue(address, value);
				cycle += 8;
			}
			public bool HasHostTrapStub(uint address)
			{
				_ = address;
				return false;
			}
			public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
			{
				_ = instructionProgramCounter;
				_ = trapId;
				_ = state;
				return false;
			}
			public void ResetExternalDevices(long cycle)
			{
				_ = cycle;
			}
			public bool TryReadExactCpuDataByte(uint address, ref long cycle, out byte value)
			{
				cycle += 2;
				value = Memory[address];
				return true;
			}
			public bool TryReadExactCpuDataWord(uint address, ref long cycle, out ushort value)
			{
				cycle += 4;
				value = (ushort)((Memory[address] << 8) | Memory[address + 1]);
				return true;
			}
			public bool TryReadExactCpuDataLong(uint address, ref long cycle, out uint value)
			{
				ExactReadLongCount++;
				cycle += 8;
				value = ReadLongValue(address);
				return true;
			}
			public bool TryWriteExactCpuDataByte(uint address, byte value, ref long cycle)
			{
				Memory[address] = value;
				cycle += 2;
				return true;
			}
			public bool TryWriteExactCpuDataWord(uint address, ushort value, ref long cycle)
			{
				Memory[address] = (byte)(value >> 8);
				Memory[address + 1] = (byte)value;
				cycle += 4;
				return true;
			}
			public bool TryWriteExactCpuDataLong(uint address, uint value, ref long cycle)
			{
				ExactWriteLongCount++;
				WriteLongValue(address, value);
				cycle += 8;
				return true;
			}
			private void RecordGenericAccess(M68kBusAccessKind accessKind, bool isWrite)
			{
				if (accessKind == M68kBusAccessKind.CpuDataRead)
				{
					GenericDataReadCount++;
				}
				else if (accessKind == M68kBusAccessKind.CpuDataWrite)
				{
					GenericDataWriteCount++;
				}
				_ = isWrite;
			}
			private uint ReadLongValue(uint address)
				=> ((uint)Memory[address] << 24) |
					((uint)Memory[address + 1] << 16) |
					((uint)Memory[address + 2] << 8) |
					Memory[address + 3];
			private void WriteLongValue(uint address, uint value)
			{
				Memory[address] = (byte)(value >> 24);
				Memory[address + 1] = (byte)(value >> 16);
				Memory[address + 2] = (byte)(value >> 8);
				Memory[address + 3] = (byte)value;
			}
		}
	private sealed class TestBus : IM68kBus, IM68kCodeReader
	{
		public byte[] Memory { get; } = new byte[0x0100_0000];
		public List<(uint Address, byte Value, long Cycle)> Writes { get; } = new();
		public List<(uint Address, M68kBusAccessKind Kind, TestAccessSize Size, bool IsWrite, long Cycle)> Accesses { get; } = new();
		public int ExternalResetCount { get; private set; }
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, TestAccessSize.Byte, false, cycle));
			return Memory[address];
		}
		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, TestAccessSize.Word, false, cycle));
			return (ushort)((Memory[address] << 8) | Memory[address + 1]);
		}
		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, TestAccessSize.Long, false, cycle));
			return ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
		}
		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, TestAccessSize.Byte, true, cycle));
			Memory[address] = value;
			Writes.Add((address, value, cycle));
		}
		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, TestAccessSize.Word, true, cycle));
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
			Writes.Add((address, (byte)(value >> 8), cycle));
			Writes.Add((address + 1, (byte)value, cycle));
		}
		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, TestAccessSize.Long, true, cycle));
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
			Writes.Add((address, (byte)(value >> 24), cycle));
			Writes.Add((address + 1, (byte)(value >> 16), cycle));
			Writes.Add((address + 2, (byte)(value >> 8), cycle));
			Writes.Add((address + 3, (byte)value, cycle));
		}
		public void WriteLongDescending(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
			=> WriteLong(address, value, ref cycle, accessKind);
		public uint ReadLongDescending(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> ReadLong(address, ref cycle, accessKind);
		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}
		public ushort ReadHostWord(uint address)
			=> (ushort)((Memory[address] << 8) | Memory[address + 1]);
		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}
		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
			ExternalResetCount++;
		}
		public void WriteLong(uint address, uint value)
		{
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
		}
	}
	private sealed class InstructionFetchWindowBus : IM68kBus, IM68kInstructionFetchWindowBus
	{
		private readonly uint[] _generation = { 1u };
		public byte[] Memory { get; } = new byte[0x0100_0000];
		public int WindowRequests { get; private set; }
		public int WindowCommits { get; private set; }
		public int GenericInstructionFetchWordReads { get; private set; }
		public bool TryGetInstructionFetchWindow(uint address, out M68kInstructionFetchWindow window)
		{
			WindowRequests++;
			window = new M68kInstructionFetchWindow(
				Memory,
				(int)address,
				address,
				address + 0x100,
				0xFFFF_FFFF,
				0,
				_generation,
				_generation[0]);
			return true;
		}
		public void CommitInstructionFetchWindowWord(in M68kInstructionFetchWindow window, uint address, ref long cycle)
		{
			_ = window;
			_ = address;
			WindowCommits++;
			cycle += 2;
		}
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			cycle += 2;
			return Memory[address];
		}
		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			if (accessKind == M68kBusAccessKind.CpuInstructionFetch)
			{
				GenericInstructionFetchWordReads++;
			}
			cycle += 2;
			return (ushort)((Memory[address] << 8) | Memory[address + 1]);
		}
		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			cycle += 4;
			return ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
		}
		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			Memory[address] = value;
			cycle += 2;
		}
		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
			cycle += 2;
		}
		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
			cycle += 4;
		}
		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}
		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}
		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
		}
	}
	private sealed class DeferredRetirementBus :
		IM68kBus,
		IM68kInstructionFetchWindowBus,
		IM68kDeferredCpuInstructionTiming
	{
		private readonly uint[] _generation = { 1u };
		private bool _batchExecutionStarted;
		public byte[] Memory { get; } = new byte[0x0100_0000];
		public int BeginInstructionTimingCalls { get; private set; }
		public int FlushInstructionTimingCalls { get; private set; }
		public int CompletedBatchInstructions { get; private set; }
		public bool IsDeferredCpuBusBatchActive { get; private set; }
		public bool IsInternalNoBusWindowEnabled => false;
		public bool TryGetInstructionFetchWindow(uint address, out M68kInstructionFetchWindow window)
		{
			window = new M68kInstructionFetchWindow(
				Memory,
				(int)address,
				address,
				address + 0x100,
				0xFFFF_FFFF,
				0,
				_generation,
				_generation[0]);
			return true;
		}
		public void CommitInstructionFetchWindowWord(
			in M68kInstructionFetchWindow window,
			uint address,
			ref long cycle)
		{
			_ = window;
			_ = address;
			cycle += 2;
			if (_batchExecutionStarted)
			{
				IsDeferredCpuBusBatchActive = false;
			}
		}
		public void BeginDeferredCpuInstructionTiming(long cycle)
		{
			_ = cycle;
			BeginInstructionTimingCalls++;
		}
		public void FlushDeferredCpuInstructionTiming(ref long cycle)
		{
			FlushInstructionTimingCalls++;
			cycle += 3;
		}
		public bool IsDeferredCpuBusBatchEligibleInstructionFetchWindow(
			in M68kInstructionFetchWindow window)
		{
			_ = window;
			return true;
		}
		public bool TryBeginDeferredCpuBusBatch(
			M68kCpuState state,
			long currentCycle,
			long? targetCycle,
			out long batchTargetCycle,
			out M68kTraceBatchWakeSource wakeSource)
		{
			_ = state;
			_ = targetCycle;
			batchTargetCycle = currentCycle + 100;
			wakeSource = M68kTraceBatchWakeSource.Unknown;
			IsDeferredCpuBusBatchActive = true;
			return true;
		}
		public void BeginDeferredCpuBusBatchExecution()
		{
			_batchExecutionStarted = true;
		}
		public void CompleteDeferredCpuBusBatchExecution(
			int instructionCount,
			int skippedInstructionFlushCount)
		{
			_ = skippedInstructionFlushCount;
			CompletedBatchInstructions += instructionCount;
		}
		public void EndDeferredCpuBusBatch(
			ref long cycle,
			M68kDeferredCpuBusBatchExitReason reason)
		{
			_ = cycle;
			_ = reason;
			IsDeferredCpuBusBatchActive = false;
		}
		public bool TryAdvanceInternalNoBusWindow(
			M68kCpuState state,
			long currentCycle,
			int cycles,
			M68kInternalNoBusWindowKind kind)
		{
			_ = state;
			_ = currentCycle;
			_ = cycles;
			_ = kind;
			return false;
		}
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			cycle += 2;
			return Memory[address];
		}
		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			cycle += 2;
			return (ushort)((Memory[address] << 8) | Memory[address + 1]);
		}
		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			cycle += 4;
			return ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
		}
		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			Memory[address] = value;
			cycle += 2;
		}
		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
			cycle += 2;
		}
		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = accessKind;
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
			cycle += 4;
		}
		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}
		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}
		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
		}
	}
	private sealed class BusAccessBatchBoundary :
		IM68kInstructionBoundary,
		IM68kBusAccessTraceBatchBoundary,
		IM68kPureCpuTraceBatchBoundary
	{
		private M68kCpuState? _state;
		public int BeforeInstructionCalls { get; private set; }
		public int AfterInstructionCalls { get; private set; }
		public int BusAccessBatchCalls { get; private set; }
		public int BusAccessBatchInstructions { get; private set; }
		public int PureCpuBatchCalls { get; private set; }
		public int PureCpuBatchInstructions { get; private set; }
		public uint AfterBatchProgramCounter { get; private set; }
		public long AfterBatchCycles { get; private set; }
		public ushort AfterBatchLastOpcode { get; private set; }
		public uint AfterBatchLastInstructionProgramCounter { get; private set; }
		public bool BeforeInstruction()
		{
			BeforeInstructionCalls++;
			return true;
		}
		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			_ = currentCycle;
			AfterInstructionCalls++;
		}
		public bool TryBeginBusAccessTraceBatch(
			M68kCpuState state,
			long targetCycle,
			out long batchTargetCycle)
		{
			_state = state;
			batchTargetCycle = targetCycle;
			return BeforeInstruction();
		}
		public void AfterBusAccessTraceBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = currentCycle;
			BusAccessBatchCalls++;
			BusAccessBatchInstructions += instructionCount;
			var state = Assert.IsType<M68kCpuState>(_state);
			AfterBatchProgramCounter = state.ProgramCounter;
			AfterBatchCycles = state.Cycles;
			AfterBatchLastOpcode = state.LastOpcode;
			AfterBatchLastInstructionProgramCounter = state.LastInstructionProgramCounter;
		}
		public bool TryBeginPureCpuTraceBatch(
			M68kCpuState state,
			long targetCycle,
			out long batchTargetCycle)
		{
			_state = state;
			batchTargetCycle = targetCycle;
			return BeforeInstruction();
		}
		public void AfterPureCpuTraceBatch(long previousCycle, long currentCycle, int instructionCount)
		{
			_ = previousCycle;
			_ = currentCycle;
			PureCpuBatchCalls++;
			PureCpuBatchInstructions += instructionCount;
			var state = Assert.IsType<M68kCpuState>(_state);
			AfterBatchProgramCounter = state.ProgramCounter;
			AfterBatchCycles = state.Cycles;
			AfterBatchLastOpcode = state.LastOpcode;
			AfterBatchLastInstructionProgramCounter = state.LastInstructionProgramCounter;
		}
	}
	private sealed class CycleCountingBus :
		IM68kBus,
		IM68kCpuBusPhaseTrace,
		IM68kFixedPlanRunBus,
		IM68kFastMemoryBus
	{
		private const int AccessCycles = 2;
		public byte[] Memory { get; } = new byte[0x0100_0000];
		public List<(uint Address, long Cycle)> InstructionFetchCycles { get; } = new();
		public List<(uint Address, long Cycle)> DataReadCycles { get; } = new();
		public List<M68kCpuBusPhase> CpuBusPhases { get; } = new();
		public uint? DelayedInstructionFetchAddress { get; init; }
		public int DelayedInstructionFetchOccurrence { get; init; } = 1;
		public int DelayedInstructionFetchCycles { get; init; }
		public uint? ThrowingInstructionFetchAddress { get; init; }
		public bool ZeroWaitInstructionFetch { get; init; }
		public bool FixedPlanRunEnabled { get; init; }
		public bool FastMemoryEnabled { get; init; }
		public uint? RejectedFastReadAddress { get; set; }
		public uint? RejectedFastWriteAddress { get; set; }
		public int FastLongReadCalls { get; private set; }
		public int FastLongWriteCalls { get; private set; }
		public List<(uint Address, bool IsWrite)> FastLongAccesses { get; } = new();
		public uint FixedPlanWindowStart { get; init; }
		public uint FixedPlanWindowEndExclusive { get; init; }
		public int FixedPlanWindowRequests { get; private set; }
		private readonly uint[] _fixedPlanWindowGeneration = [1];
		private int _matchingInstructionFetchCount;
		public bool CpuBusPhaseTracingEnabled => true;
		public void RecordCpuBusPhase(in M68kCpuBusPhase phase)
		{
			CpuBusPhases.Add(phase);
		}
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			var value = Memory[address];
			cycle += accessKind == M68kBusAccessKind.CpuInstructionFetch && ZeroWaitInstructionFetch
				? 0
				: AccessCycles;
			return value;
		}
		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			if (accessKind == M68kBusAccessKind.CpuInstructionFetch &&
				ThrowingInstructionFetchAddress == address)
			{
				throw new InvalidOperationException($"Injected instruction-fetch failure at 0x{address:X8}.");
			}

			var value = (ushort)((Memory[address] << 8) | Memory[address + 1]);
			if (accessKind == M68kBusAccessKind.CpuInstructionFetch)
			{
				if (DelayedInstructionFetchAddress == address &&
					++_matchingInstructionFetchCount == DelayedInstructionFetchOccurrence)
				{
					cycle += DelayedInstructionFetchCycles;
				}
				InstructionFetchCycles.Add((address, cycle));
			}
			else if (accessKind == M68kBusAccessKind.CpuDataRead)
			{
				DataReadCycles.Add((address, cycle));
			}
			cycle += accessKind == M68kBusAccessKind.CpuInstructionFetch && ZeroWaitInstructionFetch
				? 0
				: AccessCycles;
			return value;
		}
		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			var value = ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
			cycle += AccessCycles * 2;
			return value;
		}
		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Memory[address] = value;
			cycle += AccessCycles;
		}
		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			WriteWordRaw(address, value);
			cycle += AccessCycles;
		}
		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
			cycle += AccessCycles * 2;
		}
		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}
		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}
		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
		}
		public bool TryReadFastByte(uint address, M68kBusAccessKind accessKind, out byte value)
		{
			if (!FastMemoryEnabled || accessKind != M68kBusAccessKind.CpuDataRead)
			{
				value = 0;
				return false;
			}

			value = Memory[address];
			return true;
		}
		public bool TryReadFastWord(uint address, M68kBusAccessKind accessKind, out ushort value)
		{
			if (!FastMemoryEnabled || accessKind != M68kBusAccessKind.CpuDataRead || (address & 1) != 0)
			{
				value = 0;
				return false;
			}

			value = (ushort)((Memory[address] << 8) | Memory[address + 1]);
			return true;
		}
		public bool TryReadFastLong(uint address, M68kBusAccessKind accessKind, out uint value)
		{
			FastLongReadCalls++;
			if (!FastMemoryEnabled || accessKind != M68kBusAccessKind.CpuDataRead ||
				(address & 1) != 0 || RejectedFastReadAddress == address)
			{
				value = 0;
				return false;
			}

			value = ReadLongRaw(Memory, address);
			FastLongAccesses.Add((address, false));
			return true;
		}
		public bool TryWriteFastByte(uint address, byte value, M68kBusAccessKind accessKind)
		{
			if (!FastMemoryEnabled || accessKind != M68kBusAccessKind.CpuDataWrite)
			{
				return false;
			}

			Memory[address] = value;
			return true;
		}
		public bool TryWriteFastWord(uint address, ushort value, M68kBusAccessKind accessKind)
		{
			if (!FastMemoryEnabled || accessKind != M68kBusAccessKind.CpuDataWrite || (address & 1) != 0)
			{
				return false;
			}

			WriteWordRaw(address, value);
			return true;
		}
		public bool TryWriteFastLong(uint address, uint value, M68kBusAccessKind accessKind)
		{
			FastLongWriteCalls++;
			if (!FastMemoryEnabled || accessKind != M68kBusAccessKind.CpuDataWrite ||
				(address & 1) != 0 || RejectedFastWriteAddress == address)
			{
				return false;
			}

			WriteLongRaw(Memory, address, value);
			FastLongAccesses.Add((address, true));
			return true;
		}
		public bool TryGetFixedPlanRunWindow(uint address, out M68kFixedPlanRunWindow window)
		{
			if (!FixedPlanRunEnabled ||
				address < FixedPlanWindowStart ||
				address >= FixedPlanWindowEndExclusive)
			{
				window = default;
				return false;
			}

			FixedPlanWindowRequests++;
			var fetchWindow = new M68kInstructionFetchWindow(
				Memory,
				checked((int)FixedPlanWindowStart),
				FixedPlanWindowStart,
				FixedPlanWindowEndExclusive,
				0x00FF_FFFF,
				busTag: 1,
				_fixedPlanWindowGeneration,
				_fixedPlanWindowGeneration[0]);
			window = new M68kFixedPlanRunWindow(
				in fetchWindow,
				readyCycleOffset: ZeroWaitInstructionFetch ? 0 : AccessCycles,
				nextBusCycleOffset: ZeroWaitInstructionFetch ? 0 : AccessCycles,
				deferredBatchEligible: false);
			return true;
		}
		public void AdvanceFixedPlanWindowGeneration()
		{
			_fixedPlanWindowGeneration[0]++;
		}
		public void WriteWordRaw(uint address, ushort value)
		{
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
		}
	}
}
