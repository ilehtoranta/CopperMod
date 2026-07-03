using System.Runtime.CompilerServices;
using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68kInterpreterTests
{
	public static IEnumerable<object[]> OpcodePlanDispatchVariants()
	{
		yield return new object[] { (int)M68kOpcodePlanDispatch.KindTable };
		yield return new object[] { (int)M68kOpcodePlanDispatch.ComputedKind };
		yield return new object[] { (int)M68kOpcodePlanDispatch.PackedPlan };
		yield return new object[] { (int)M68kOpcodePlanDispatch.DelegateTable };
	}

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
	public void NopUsesDocumentedCyclesWithoutAddingFetchTime()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(4, cycles);
		Assert.Equal(24, cpu.State.Cycles);
	}

	[Fact]
	public void AmigaAccurateM68000TasChipRamLosesWriteBack()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x4A, 0xD0); // TAS (A0)
		bus.ChipRam[0x2000] = 0x01;
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x01, bus.ChipRam[0x2000]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Contains(
			bus.BusAccesses,
			access => access.Request.Kind == AmigaBusAccessKind.CpuDataWrite &&
				access.Request.Target == AmigaBusAccessTarget.ChipRam &&
				access.Request.Address == 0x2000u);
	}

	[Fact]
	public void AmigaAccurateM68000TasRealFastRamWritesBack()
	{
		var bus = new AmigaBus(realFastRamSize: 0x10000);
		var targetAddress = AmigaConstants.A500RealFastRamBase + 0x2000u;
		Write(bus.ChipRam, 0x1000, 0x4A, 0xD0); // TAS (A0)
		bus.WriteHostByte(targetAddress, 0x01);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = targetAddress;

		cpu.ExecuteInstruction();

		Assert.Equal(0x81, bus.ReadHostByte(targetAddress));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
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
	public void DbraTakenUsesDocumentedCyclesWithoutAddingFetchTime()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,*-2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 1;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(10, cycles);
		Assert.Equal(30, cpu.State.Cycles);
		Assert.Equal(0u, cpu.State.D[0] & 0xFFFF);
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
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
	public void MoveWordDisplacementSourceToDataRegisterIncludesPrefetchContention()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x30, 0x28, 0x00, 0x02); // MOVE.W 2(A0),D0
		Write(bus.ChipRam, 0x2002, 0x12, 0x34);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[0] = 0x2000;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(14, cycles);
		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFF);
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
		Assert.Equal(0x1234_5678u, BigEndian.ReadUInt32(bus.Memory, 0x2000, "long write"));
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
	public void ImmediateBtstDataRegisterUsesDocumentedCyclesWithoutAddingExtensionFetchTime()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x08, 0x00, 0x00, 0x0E); // BTST #14,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 0x4000;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(10, cycles);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void MuluImmediateUsesSourceBitCountTiming()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0xC0, 0xFC, 0x55, 0x55); // MULU.W #$5555,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 3;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(58, cycles);
		Assert.Equal(0x0000_FFFFu, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void MuluRegisterSourceUsesSourceBitCountTimingWithoutEaExtension()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0xC0, 0xC1); // MULU.W D1,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 3;
		cpu.State.D[1] = 0x5555;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(54, cycles);
		Assert.Equal(0x0000_FFFFu, cpu.State.D[0]);
	}

	[Fact]
	public void MuluImmediateZeroSourceUsesMinimumTiming()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0xC0, 0xFC, 0x00, 0x00); // MULU.W #0,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 0x1234;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(42, cycles);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void DivuRegisterSourceUsesDataDependentTimingWithoutEaExtension()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x80, 0xC1); // DIVU.W D1,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 6;
		cpu.State.D[1] = 3;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(130, cycles);
		Assert.Equal(2u, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void DivsMemorySourceUsesDataDependentTimingWithEaCycles()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x81, 0xD0); // DIVS.W (A0),D0
		Write(bus.ChipRam, 0x2000, 0x00, 0x03);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 0xFFFF_FFF6; // -10

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(154, cycles);
		Assert.Equal(0xFFFF_FFFDu, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
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
	public void PlannedDispatchTablesMatchKindTableForEveryOpcode()
	{
		for (var opcode = 0; opcode <= 0xFFFF; opcode++)
		{
			var word = (ushort)opcode;
			var kind = M68kOpcodePlanTable.Kinds[word];
			Assert.Equal(kind, M68kOpcodePlanTable.ComputeKind(word));
			Assert.Equal(kind, M68kOpcodePlanTable.PackedPlans[word].Kind);
			Assert.Equal(kind != M68kOpcodePlanKind.Unsupported, M68kInterpreter.HasDelegatePlanForOpcode(word));
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
		Assert.Equal(62, dmaconrReadCycles[0]);
		Assert.Equal(34, dmaconrReadCycles[1] - dmaconrReadCycles[0]);
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
	public void AbcdConsumesExtendFromBinaryShiftForBcdAccumulation()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xD1, 0x80); // ADD.L D0,D0
		Write(bus.Memory, 0x1002, 0xC3, 0x01); // ABCD D1,D1
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x8000_0000;
		cpu.State.D[1] = 0x0000_0000;

		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0001u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
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
		Assert.Equal(8, cycles);
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
		Assert.Equal(8, cycles);
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
		Assert.Equal(8, cycles);
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
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(BigEndian.ReadUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
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
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(BigEndian.ReadUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
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
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x4FFC, "line-A stacked program counter"));

		cpu.State.ProgramCounter = 0x2000;
		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF4u, cpu.State.A[7]);
		Assert.Equal(0x2000u, BigEndian.ReadUInt32(bus.Memory, 0x4FF6, "line-F stacked program counter"));
	}

	[Fact]
	public void RegisteredLineFHostTrapStubInvokesCallbackAndReturns()
	{
		var bus = new AmigaBus();
		var trapAddress = 0x00F0_0000u;
		var callbackCalled = false;
		bus.RegisterHostTrapStub(trapAddress, state =>
		{
			callbackCalled = true;
			state.D[0] = 0x1234_5678;
		});
		bus.WriteWord(0x1000, 0x4EB9); // JSR absolute long
		bus.WriteLong(0x1002, trapAddress);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.True(callbackCalled);
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(0xFF00, bus.ReadWord(trapAddress));
		Assert.NotEqual(0, bus.ReadWord(trapAddress + 2));
	}

	[Fact]
	public void RegisteredLineFHostTrapDoesNotReturnWhenCallbackChangesProgramCounter()
	{
		var bus = new AmigaBus();
		var trapAddress = 0x00F0_0000u;
		bus.RegisterHostTrapStub(trapAddress, state => state.ProgramCounter = 0x2000);
		bus.WriteWord(0x1000, 0x4EB9); // JSR absolute long
		bus.WriteLong(0x1002, trapAddress);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFCu, cpu.State.A[7]);
		Assert.Equal(0x1006u, bus.ReadLong(0x2FFC));
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
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x4FFC, "line-F stacked program counter"));
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
		Assert.Equal(0x1004u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
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
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
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
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		var exception = Assert.Throws<UnsupportedM68kOpcodeException>(() => cpu.ExecuteInstruction());

		Assert.Equal(0x08C8, exception.Opcode);
		Assert.Equal(0x1000u, exception.ProgramCounter);
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
	public void ImmediateBtstByteAbsoluteLongBranchesOnCiaActiveLowFireInput()
	{
		var bus = new AmigaBus();
		bus.GamePort0FirePressed = true;
		bus.GamePort1FirePressed = true;
		Write(bus.ChipRam, 0x1000, 0x08, 0x39, 0x00, 0x06, 0x00, 0xBF, 0xE0, 0x01); // BTST #6,$BFE001.L
		Write(bus.ChipRam, 0x1008, 0x67, 0x04); // BEQ pressed
		Write(bus.ChipRam, 0x100A, 0x70, 0x01); // MOVEQ #1,D0
		Write(bus.ChipRam, 0x100C, 0x60, 0x02); // BRA done
		Write(bus.ChipRam, 0x100E, 0x70, 0x02); // pressed: MOVEQ #2,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0002u, cpu.State.D[0]);
		Assert.Equal(0x1010u, cpu.State.ProgramCounter);
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
	public void ImmediateBtstAbsoluteLongCiaAddsPeripheralAccessTiming()
	{
		var program = new byte[] { 0x08, 0x39, 0x00, 0x06, 0x00, 0xBF, 0xE0, 0x01 }; // BTST #6,$BFE001.L
		var bus = CreateRomProgramBus(program);
		bus.GamePort0FirePressed = true;
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x00FC0000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(20, cpu.State.Cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(0x00FC0008u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void ImmediateBtstAbsoluteLongCustomRegisterKeepsAgnusBusTiming()
	{
		var program = new byte[] { 0x08, 0x39, 0x00, 0x06, 0x00, 0xDF, 0xF0, 0x02 }; // BTST #6,$DFF002.L
		var bus = CreateRomProgramBus(program);
		var expectedDataCycle = 0L;
		_ = new AmigaBus().ReadByte(0x00DFF002, ref expectedDataCycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x00FC0000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(Math.Max(20, expectedDataCycle), cpu.State.Cycles);
		Assert.Equal(0x00FC0008u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void CmpiByteAbsoluteLongBeamRegisterUsesMemoryEaAndAgnusBusTiming()
	{
		var program = new byte[] { 0x0C, 0x39, 0x00, 0xC8, 0x00, 0xDF, 0xF0, 0x06 }; // CMPI.B #$C8,$DFF006.L
		var bus = CreateRomProgramBus(program);
		var expectedDataCycle = 0L;
		_ = new AmigaBus().ReadByte(0x00DFF006, ref expectedDataCycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x00FC0000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(Math.Max(20, expectedDataCycle), cpu.State.Cycles);
		Assert.Equal(0x00FC0008u, cpu.State.ProgramCounter);
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
	public void MoveImmediateToCcrPreservesSystemBits()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x44, 0xFC, 0x00, 0x15); // MOVE #$15,CCR
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = 0xA5E0;

		cpu.ExecuteInstruction();

		Assert.Equal(0xA5F5, cpu.State.StatusRegister);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
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
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(BigEndian.ReadUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x0000_1004u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
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
		Assert.Equal(0x0000_1004u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
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
	public void AmigaBusSchedulesCpuWordCustomWritesAsSingleRegisterEvent()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x33, 0xFC, 0x80, 0x0F, 0x00, 0xDF, 0xF0, 0x96); // MOVE.W #$800F,$DFF096
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);

		cpu.ExecuteInstruction();

		var write = Assert.Single(bus.CustomRegisterWrites);
		Assert.Equal(0x096, write.Address);
		Assert.Equal(0x800F, write.Value);
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
				access.Size == AmigaBusAccessSize.Word &&
				!access.IsWrite);
		var write = Assert.Single(bus.Accesses, access => access.Kind == M68kBusAccessKind.CpuDataWrite && access.IsWrite);
		Assert.Equal(0x2000u, write.Address);
		Assert.Equal(AmigaBusAccessSize.Long, write.Size);
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
		Assert.Equal(3, bus.WindowCommits);
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
		Assert.Equal(AmigaBusAccessSize.Long, dataAccesses[0].Size);
		Assert.False(dataAccesses[0].IsWrite);
		Assert.Equal((uint)0x2000, dataAccesses[1].Address);
		Assert.Equal(AmigaBusAccessSize.Long, dataAccesses[1].Size);
		Assert.True(dataAccesses[1].IsWrite);
		Assert.True(dataAccesses[1].Cycle >= dataAccesses[0].Cycle);
		Assert.Equal(0u, BigEndian.ReadUInt32(bus.Memory, 0x2000, "cleared longword"));
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
	public void AmigaBusExactCpuDataHelpersMatchGenericChipRamAccess()
	{
		var genericBus = CreateExactCpuDataAmigaBus();
		var exactBus = CreateExactCpuDataAmigaBus();
		Write(genericBus.ChipRam, 0x2000, 0x12, 0x34, 0x56, 0x78);
		Write(exactBus.ChipRam, 0x2000, 0x12, 0x34, 0x56, 0x78);
		IM68kBus generic = genericBus;
		var genericReadCycle = 100L;
		var exactReadCycle = 100L;

		var genericValue = generic.ReadLong(0x2000, ref genericReadCycle, M68kBusAccessKind.CpuDataRead);
		var exactGranted = exactBus.TryReadExactCpuDataLong(0x2000, ref exactReadCycle, out var exactValue);

		Assert.True(exactGranted);
		Assert.Equal(genericValue, exactValue);
		Assert.Equal(genericReadCycle, exactReadCycle);

		var genericWriteCycle = 120L;
		var exactWriteCycle = 120L;
		generic.WriteLong(0x2010, 0x89AB_CDEF, ref genericWriteCycle, M68kBusAccessKind.CpuDataWrite);
		var exactWrote = exactBus.TryWriteExactCpuDataLong(0x2010, 0x89AB_CDEF, ref exactWriteCycle);

		Assert.True(exactWrote);
		Assert.Equal(genericWriteCycle, exactWriteCycle);
		Assert.Equal(
			BigEndian.ReadUInt32(genericBus.ChipRam, 0x2010, "generic chip write"),
			BigEndian.ReadUInt32(exactBus.ChipRam, 0x2010, "exact chip write"));
	}

	[Fact]
	public void AmigaBusExactCpuDataHelpersMatchGenericExpansionRamAccess()
	{
		var genericBus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var exactBus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var readAddress = genericBus.ExpansionRamBase + 0x20;
		var writeAddress = genericBus.ExpansionRamBase + 0x40;
		Write(genericBus.ExpansionRam, 0x20, 0x12, 0x34, 0x56, 0x78);
		Write(exactBus.ExpansionRam, 0x20, 0x12, 0x34, 0x56, 0x78);
		IM68kBus generic = genericBus;
		var genericReadCycle = 100L;
		var exactReadCycle = 100L;

		var genericValue = generic.ReadLong(readAddress, ref genericReadCycle, M68kBusAccessKind.CpuDataRead);
		var exactGranted = exactBus.TryReadExactCpuDataLong(readAddress, ref exactReadCycle, out var exactValue);

		Assert.True(exactGranted);
		Assert.Equal(genericValue, exactValue);
		Assert.Equal(genericReadCycle, exactReadCycle);

		var genericWriteCycle = 120L;
		var exactWriteCycle = 120L;
		generic.WriteLong(writeAddress, 0x89AB_CDEF, ref genericWriteCycle, M68kBusAccessKind.CpuDataWrite);
		var exactWrote = exactBus.TryWriteExactCpuDataLong(writeAddress, 0x89AB_CDEF, ref exactWriteCycle);

		Assert.True(exactWrote);
		Assert.Equal(genericWriteCycle, exactWriteCycle);
		Assert.Equal(
			BigEndian.ReadUInt32(genericBus.ExpansionRam, 0x40, "generic expansion write"),
			BigEndian.ReadUInt32(exactBus.ExpansionRam, 0x40, "exact expansion write"));
	}

	[Fact]
	public void AmigaBusExactCpuDataHelpersMatchGenericRealFastRamAccess()
	{
		var genericBus = CreateExactCpuDataAmigaBus(realFastRamSize: 0x10000);
		var exactBus = CreateExactCpuDataAmigaBus(realFastRamSize: 0x10000);
		var readAddress = genericBus.RealFastRamBase + 0x20;
		var writeAddress = genericBus.RealFastRamBase + 0x40;
		Write(genericBus.RealFastRam, 0x20, 0x12, 0x34, 0x56, 0x78);
		Write(exactBus.RealFastRam, 0x20, 0x12, 0x34, 0x56, 0x78);
		IM68kBus generic = genericBus;
		var genericReadCycle = 100L;
		var exactReadCycle = 100L;

		var genericValue = generic.ReadLong(readAddress, ref genericReadCycle, M68kBusAccessKind.CpuDataRead);
		var exactGranted = exactBus.TryReadExactCpuDataLong(readAddress, ref exactReadCycle, out var exactValue);

		Assert.True(exactGranted);
		Assert.Equal(genericValue, exactValue);
		Assert.Equal(genericReadCycle, exactReadCycle);

		var genericWriteCycle = 120L;
		var exactWriteCycle = 120L;
		generic.WriteLong(writeAddress, 0x89AB_CDEF, ref genericWriteCycle, M68kBusAccessKind.CpuDataWrite);
		var exactWrote = exactBus.TryWriteExactCpuDataLong(writeAddress, 0x89AB_CDEF, ref exactWriteCycle);

		Assert.True(exactWrote);
		Assert.Equal(genericWriteCycle, exactWriteCycle);
		Assert.Equal(
			BigEndian.ReadUInt32(genericBus.RealFastRam, 0x40, "generic real fast write"),
			BigEndian.ReadUInt32(exactBus.RealFastRam, 0x40, "exact real fast write"));
	}

	[Fact]
	public void AmigaBusExactCpuDataHelpersFallBackForDiagnosticsAndDevices()
	{
		var captured = new AmigaBus(captureBusAccesses: true);
		var cycle = 20L;
		Assert.False(captured.TryReadExactCpuDataWord(0x2000, ref cycle, out _));
		Assert.Equal(20L, cycle);

		var devices = CreateExactCpuDataAmigaBus();
		Assert.False(devices.TryReadExactCpuDataWord(0x00DFF002, ref cycle, out _));
		Assert.Equal(20L, cycle);
		Assert.False(devices.TryReadExactCpuDataByte(0x00BFE001, ref cycle, out _));
		Assert.Equal(20L, cycle);
	}

	[Fact]
	public void AmigaBusExactCpuDataHelpersFallBackForRom()
	{
		var bus = CreateExactCpuDataAmigaBus();
		var overlayRom = new byte[0x40000];
		Write(overlayRom, 0, 0x12, 0x34, 0x56, 0x78);
		bus.MapReadOnlyMemory(0x00FC0000, overlayRom);
		var cycle = 20L;

		Assert.False(bus.TryReadExactCpuDataWord(0x000000, ref cycle, out _));
		Assert.Equal(20L, cycle);

		bus = CreateExactCpuDataAmigaBus();
		bus.MapReadOnlyMemory(0x00E00000, new byte[] { 0x12, 0x34, 0x56, 0x78 });

		Assert.False(bus.TryReadExactCpuDataWord(0x00E00000, ref cycle, out _));
		Assert.Equal(20L, cycle);
	}

	[Fact]
	public void AmigaBusMemoryCopyHelpersUseRamBackends()
	{
		var bus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000, realFastRamSize: 0x10000);

		AssertCopyAndClear(bus, 0x2000);
		AssertCopyAndClear(bus, bus.ExpansionRamBase + 0x40);
		AssertCopyAndClear(bus, bus.RealFastRamBase + 0x80);

		static void AssertCopyAndClear(AmigaBus bus, uint address)
		{
			var source = new byte[] { 0x12, 0x34, 0x56, 0x78 };
			var destination = new byte[source.Length];

			bus.CopyToMemory(address, source);
			bus.CopyFromMemory(address, destination);
			Assert.Equal(source, destination);

			bus.ClearMemory(address, source.Length);
			Array.Fill(destination, (byte)0xFF);
			bus.CopyFromMemory(address, destination);
			Assert.Equal(new byte[source.Length], destination);
		}
	}

	[Fact]
	public void OddWordDataReadRaisesAddressErrorWith68000Frame()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x30, 0x10); // MOVE.W (A0),D0
		bus.WriteLong(0x000C, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2001;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x3015, ReadWord(bus.Memory, 0x2FF2)); // status word: (opcode & $FFE0) | function-code
		Assert.Equal(0x0000_2001u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0x3010, ReadWord(bus.Memory, 0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1002u, ReadLong(bus.Memory, 0x2FFC));
	}

	[Fact]
	public void OddWordDataWriteRaisesAddressErrorWith68000Frame()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x30, 0x80); // MOVE.W D0,(A0)
		bus.WriteLong(0x000C, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234;
		cpu.State.A[0] = 0x2001;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x3085, ReadWord(bus.Memory, 0x2FF2)); // status word: (opcode & $FFE0) | function-code
		Assert.Equal(0x0000_2001u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0x3080, ReadWord(bus.Memory, 0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1002u, ReadLong(bus.Memory, 0x2FFC));
		Assert.Equal(0x00, bus.Memory[0x2001]);
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

	private static AmigaBus CreateRomProgramBus(ReadOnlySpan<byte> program)
	{
		var bus = new AmigaBus();
		var rom = new byte[0x40000];
		program.CopyTo(rom);
		bus.MapReadOnlyMemory(0x00FC0000, rom);
		return bus;
	}

	private static AmigaBus CreateExactCpuDataAmigaBus(int expansionRamSize = 0, int realFastRamSize = 0)
		=> new AmigaBus(
			expansionRamSize: expansionRamSize,
			captureBusAccesses: false,
			enableLiveAgnusDma: false,
			enableLiveDisplayDma: false,
			realFastRamSize: realFastRamSize);

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

	private sealed class TestBus : IM68kBus
	{
		public byte[] Memory { get; } = new byte[0x0100_0000];

		public List<(uint Address, byte Value, long Cycle)> Writes { get; } = new();

		public List<(uint Address, M68kBusAccessKind Kind, AmigaBusAccessSize Size, bool IsWrite, long Cycle)> Accesses { get; } = new();

		public int ExternalResetCount { get; private set; }

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, false, cycle));
			return Memory[address];
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, false, cycle));
			return (ushort)((Memory[address] << 8) | Memory[address + 1]);
		}

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Long, false, cycle));
			return ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, true, cycle));
			Memory[address] = value;
			Writes.Add((address, value, cycle));
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, true, cycle));
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
			Writes.Add((address, (byte)(value >> 8), cycle));
			Writes.Add((address + 1, (byte)value, cycle));
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Long, true, cycle));
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

	private sealed class CycleCountingBus : IM68kBus, IM68kCpuBusPhaseTrace
	{
		private const int AccessCycles = 2;

		public byte[] Memory { get; } = new byte[0x0100_0000];

		public List<(uint Address, long Cycle)> InstructionFetchCycles { get; } = new();

		public List<(uint Address, long Cycle)> DataReadCycles { get; } = new();

		public List<M68kCpuBusPhase> CpuBusPhases { get; } = new();

		public bool CpuBusPhaseTracingEnabled => true;

		public void RecordCpuBusPhase(in M68kCpuBusPhase phase)
		{
			CpuBusPhases.Add(phase);
		}

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			var value = Memory[address];
			cycle += AccessCycles;
			return value;
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			var value = (ushort)((Memory[address] << 8) | Memory[address + 1]);
			if (accessKind == M68kBusAccessKind.CpuInstructionFetch)
			{
				InstructionFetchCycles.Add((address, cycle));
			}
			else if (accessKind == M68kBusAccessKind.CpuDataRead)
			{
				DataReadCycles.Add((address, cycle));
			}

			cycle += AccessCycles;
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

		public void WriteWordRaw(uint address, ushort value)
		{
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
		}
	}
}
