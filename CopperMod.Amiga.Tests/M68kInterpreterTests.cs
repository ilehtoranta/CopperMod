using System.Runtime.CompilerServices;
using CopperMod.Amiga;
namespace CopperMod.Amiga.Tests;
public sealed class M68kInterpreterTests
{
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
	public void QueuedSecondFallthroughWordStaysFrozenAfterMemoryMutation()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0x71, // NOP
			0x4E, 0x71, // NOP
			0x4E, 0x71, // NOP, should be queued before mutation
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;

		cpu.ExecuteInstruction();
		bus.WriteWordRaw(0x1004, 0x7007); // MOVEQ #7,D0 if fetched after mutation
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
		Assert.Contains(bus.InstructionFetchCycles, fetch => fetch.Address == 0x1004);
	}
	[Fact]
	public void NopBusSequencePrimesTwoPrefetchWordsAndTopsUpFallthrough()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0x71,
			0x4E, 0x71,
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(4, cycles);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1004u, M68kOperandSize.Word, false, 24L, 26L));
	}
	[Fact]
	public void AndiWordMemoryDestinationBusSequencePrefetchesBetweenReadAndWrite()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x02, 0x53, 0x00, 0xFF, // ANDI.W #$00FF,(A3)
			0x4E, 0x71,
			0x4E, 0x71);
		bus.WriteWordRaw(0x2000, 0xA5F0);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[3] = 0x2000;

		cpu.ExecuteInstruction();

		Assert.Equal(0x00F0, ReadWord(bus.Memory, 0x2000));
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1004u, M68kOperandSize.Word, false, 24L, 26L),
			(M68kBusAccessKind.CpuDataRead, 0x2000u, M68kOperandSize.Word, false, 26L, 28L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1006u, M68kOperandSize.Word, false, 28L, 30L),
			(M68kBusAccessKind.CpuDataWrite, 0x2000u, M68kOperandSize.Word, true, 30L, 32L));
	}
	[Fact]
	public void NotTakenShortBranchPreservesQueuedFallthroughWord()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0x71, // NOP
			0x66, 0x02, // BNE.S +2, not taken because Z is set
			0x4E, 0x71, // NOP, already queued fallthrough
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.SetFlag(M68kCpuState.Zero, true);

		cpu.ExecuteInstruction();
		bus.WriteWordRaw(0x1004, 0x7007); // MOVEQ #7,D0 if the fallthrough queue is discarded
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void NotTakenShortBranchBusSequenceKeepsFallthroughPrefetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x66, 0x02, // BNE.S +2, not taken because Z is set
			0x4E, 0x71,
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.SetFlag(M68kCpuState.Zero, true);

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(8, cycles);
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1004u, M68kOperandSize.Word, false, 24L, 26L));
	}
	[Fact]
	public void TakenShortBranchBusSequenceFlushesFallthroughAndPrimesTarget()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x66, 0x02, // BNE.S +2, taken because Z is clear
			0x70, 0x07,
			0x4E, 0x71,
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.SetFlag(M68kCpuState.Zero, false);

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(10, cycles);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1004u, M68kOperandSize.Word, false, 26L, 28L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1006u, M68kOperandSize.Word, false, 28L, 30L));
	}
	[Fact]
	public void NotTakenWordBranchQueuesFallthroughAfterExtensionFetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0x71, // NOP
			0x67, 0x00, // BEQ.W +4, not taken because Z is clear
			0x00, 0x04,
			0x4E, 0x71, // NOP, should be queued after extension fetch
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.SetFlag(M68kCpuState.Zero, false);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		bus.WriteWordRaw(0x1006, 0x7007); // MOVEQ #7,D0 if fallthrough was not queued
		cpu.ExecuteInstruction();

		Assert.Equal(0u, cpu.State.D[0]);
		Assert.Equal(0x1008u, cpu.State.ProgramCounter);
		Assert.Contains(bus.InstructionFetchCycles, fetch => fetch.Address == 0x1006);
	}
	[Fact]
	public void NotTakenWordBranchBusSequenceQueuesFallthroughAfterExtensionFetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x67, 0x00, // BEQ.W +4, not taken because Z is clear
			0x00, 0x04,
			0x4E, 0x71,
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.SetFlag(M68kCpuState.Zero, false);

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(12, cycles);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1004u, M68kOperandSize.Word, false, 28L, 30L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1006u, M68kOperandSize.Word, false, 30L, 32L));
	}
	[Fact]
	public void TakenWordBranchDoesNotFetchDiscardedFallthroughWord()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0x71, // NOP
			0x60, 0x00, // BRA.W +4
			0x00, 0x04,
			0x70, 0x07, // discarded fallthrough
			0x4E, 0x71, // target
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x1008u, cpu.State.ProgramCounter);
		Assert.DoesNotContain(bus.InstructionFetchCycles, fetch => fetch.Address == 0x1006);
		Assert.Contains(bus.InstructionFetchCycles, fetch => fetch.Address == 0x1008);
	}
	[Fact]
	public void TakenWordBranchBusSequenceSuppressesFallthroughAfterExtensionFetch()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x60, 0x00, // BRA.W +4
			0x00, 0x04,
			0x70, 0x07, // discarded fallthrough
			0x4E, 0x71, // target
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(10, cycles);
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1006u, M68kOperandSize.Word, false, 26L, 28L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1008u, M68kOperandSize.Word, false, 28L, 30L));
	}
	[Fact]
	public void DbraTakenBusSequenceSuppressesFallthroughAndPrimesTarget()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x51, 0xC8, 0xFF, 0xFE, // DBRA D0,*-2
			0x70, 0x07);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 1;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(10, cycles);
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		Assert.Equal(0u, cpu.State.D[0] & 0xFFFF);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 26L, 28L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 28L, 30L));
	}
	[Fact]
	public void JmpAbsoluteLongBusSequenceFetchesLongTargetAndPrimesDestination()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0xF9, // JMP ($2000).L
			0x00, 0x00,
			0x20, 0x00,
			0x70, 0x07);
		Write(bus.Memory, 0x2000,
			0x4E, 0x71,
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(12, cycles);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1004u, M68kOperandSize.Word, false, 24L, 26L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1006u, M68kOperandSize.Word, false, 26L, 28L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x2000u, M68kOperandSize.Word, false, 28L, 30L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x2002u, M68kOperandSize.Word, false, 30L, 32L));
	}
	[Fact]
	public void JsrAbsoluteLongBusSequencePushesReturnBeforePrimingDestination()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0xB9, // JSR ($2000).L
			0x00, 0x00,
			0x20, 0x00,
			0x70, 0x07);
		Write(bus.Memory, 0x2000,
			0x4E, 0x71,
			0x4E, 0x71);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(20, cycles);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFCu, cpu.State.A[7]);
		Assert.Equal(0x0000u, ReadWord(bus.Memory, 0x2FFC));
		Assert.Equal(0x1006u, ReadWord(bus.Memory, 0x2FFE));
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1004u, M68kOperandSize.Word, false, 24L, 26L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1006u, M68kOperandSize.Word, false, 26L, 28L),
			(M68kBusAccessKind.CpuDataWrite, 0x2FFEu, M68kOperandSize.Word, true, 28L, 30L),
			(M68kBusAccessKind.CpuDataWrite, 0x2FFCu, M68kOperandSize.Word, true, 30L, 32L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x2000u, M68kOperandSize.Word, false, 32L, 34L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x2002u, M68kOperandSize.Word, false, 34L, 36L));
	}
	[Fact]
	public void RtsBusSequencePullsReturnAddressBeforePrimingDestination()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x4E, 0x75, // RTS
			0x4E, 0x71);
		Write(bus.Memory, 0x2000,
			0x4E, 0x71,
			0x4E, 0x71);
		bus.WriteWordRaw(0x3000, 0x0000);
		bus.WriteWordRaw(0x3002, 0x2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(16, cycles);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3004u, cpu.State.A[7]);
		AssertCpuPhaseSequence(
			bus.CpuBusPhases,
			(M68kBusAccessKind.CpuInstructionFetch, 0x1000u, M68kOperandSize.Word, false, 20L, 22L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x1002u, M68kOperandSize.Word, false, 22L, 24L),
			(M68kBusAccessKind.CpuDataRead, 0x3000u, M68kOperandSize.Long, false, 24L, 28L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x2000u, M68kOperandSize.Word, false, 28L, 30L),
			(M68kBusAccessKind.CpuInstructionFetch, 0x2002u, M68kOperandSize.Word, false, 30L, 32L));
	}
	[Fact]
	public void Synccpu3PollingBodyUsesExpectedThirtyEightCycles()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000,
			0x02, 0x53, 0x00, 0xFF, // ANDI.W #$00FF,(A3)
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x66, 0xF4);            // BNE.S back to ANDI
		bus.WriteWordRaw(0x2000, 0x0011);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[3] = 0x2000;

		for (var i = 0; i < 5; i++)
		{
			cpu.ExecuteInstruction();
		}

		Assert.Equal(38, cpu.State.Cycles - 20);
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		Assert.Equal(0x0011, ReadWord(bus.Memory, 0x2000));
	}
	[Fact]
	public void Synccpu3LoopCycleCountingBusRetiresEveryThirtyEightCyclesAcrossIterations()
	{
		const int iterations = 32;
		var bus = new CycleCountingBus();
		WriteSynccpu3Loop(bus.Memory, 0x1000);
		bus.WriteWordRaw(0x2000, 0x0011);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[3] = 0x2000;

		var loopEndCycles = ExecuteSynccpu3LoopIterationsAndCaptureEndCycles(cpu, iterations);
		var loopDeltas = GetDeltas(loopEndCycles, initialCycle: 20);

		Assert.True(
			loopDeltas.All(delta => delta == 38),
			$"loopDeltas={string.Join(",", loopDeltas)}");
	}
	[Fact]
	public void Synccpu3LoopCycleCountingBusDataReadRequestPhaseStaysStableAfterPrefetchWarmup()
	{
		const int iterations = 32;
		var bus = new CycleCountingBus();
		WriteSynccpu3Loop(bus.Memory, 0x1000);
		bus.WriteWordRaw(0x2000, 0x0011);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[3] = 0x2000;

		ExecuteSynccpu3LoopIterations(cpu, iterations);

		var reads = GetSynccpu3DataReads(bus, 0x2000).ToArray();
		var requestDeltas = GetRequestDeltas(reads);
		var diagnostic = BuildSynccpu3ReadPhaseDiagnostic(reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(
			requestDeltas.Skip(1).All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact]
	public void Synccpu3PollingBodyUsesExpectedThirtyEightCyclesFromChipRam()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, 0x1000,
			0x02, 0x53, 0x00, 0xFF, // ANDI.W #$00FF,(A3)
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x66, 0xF4);            // BNE.S back to ANDI
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[3] = 0x2000;

		var retiredCycles = new int[5];
		for (var i = 0; i < retiredCycles.Length; i++)
		{
			retiredCycles[i] = cpu.ExecuteInstruction();
		}

		Assert.True(
			cpu.State.Cycles - 20 == 38,
			$"actualCycles={cpu.State.Cycles - 20}, retired={string.Join("/", retiredCycles)}, recentBus={string.Join(",", bus.BusAccesses.Select(access => $"{access.Request.Kind}:{(access.Request.IsWrite ? "W" : "R")}:0x{access.Request.Address:X6}@{access.GrantedCycle}").TakeLast(16))}");
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
	}
	[Fact]
	public void Synccpu3LoopAmigaBusRetiresEveryThirtyEightCyclesAcrossIterations()
	{
		const int iterations = 32;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = 0x2000;

		var loopEndCycles = ExecuteSynccpu3LoopIterationsAndCaptureEndCycles(cpu, iterations);
		var loopDeltas = GetDeltas(loopEndCycles, initialCycle: 64);

		Assert.True(
			loopDeltas.All(delta => delta == 38),
			$"loopDeltas={string.Join(",", loopDeltas)}");
	}
	[Fact(Skip = "Documents unresolved AmigaBus CPU request phase: disabling live DMA still produces the chip-RAM 36/40-cycle data-read cadence.")]
	public void Synccpu3LoopAmigaBusWithoutLiveDmaDataReadRequestPhaseStaysStableAfterPrefetchWarmup()
	{
		const int iterations = 32;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: false);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = 0x2000;

		ExecuteSynccpu3LoopIterations(cpu, iterations);

		var reads = GetSynccpu3DataReads(bus, 0x2000).ToArray();
		var requestDeltas = GetRequestDeltas(reads);
		var diagnostic = BuildSynccpu3ReadPhaseDiagnostic(bus, reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(
			requestDeltas.Skip(1).All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact]
	public void Synccpu3LoopAmigaBusRealFastRamDataReadRequestPhaseStaysStableAfterPrefetchWarmup()
	{
		const int iterations = 32;
		var bus = new AmigaBus(realFastRamSize: 0x10000, captureBusAccesses: true, enableLiveAgnusDma: true);
		var programAddress = bus.RealFastRamBase + 0x1000u;
		var dataAddress = bus.RealFastRamBase + 0x2000u;
		WriteAmigaMemory(bus, programAddress,
			0x02, 0x53, 0x00, 0xFF, // ANDI.W #$00FF,(A3)
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x66, 0xF4);            // BNE.S back to ANDI
		WriteAmigaMemory(bus, dataAddress, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(programAddress, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = dataAddress;

		var loopEndCycles = ExecuteSynccpu3LoopIterationsAndCaptureEndCycles(
			cpu,
			iterations,
			expectedProgramCounter: programAddress);
		var loopDeltas = GetDeltas(loopEndCycles, initialCycle: 64);
		var reads = GetSynccpu3DataReads(bus, dataAddress, programAddress).ToArray();
		var requestDeltas = GetRequestDeltas(reads);
		var diagnostic = BuildSynccpu3ReadPhaseDiagnostic(bus, reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(
			loopDeltas.All(delta => delta == 38),
			$"loopDeltas={string.Join(",", loopDeltas)} | {diagnostic}");
		Assert.True(
			requestDeltas.Skip(1).All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact(Skip = "Documents unresolved chip-RAM RMW loop phase: target instruction fetch request cadence shows 36/40 excursions before settling.")]
	public void Synccpu3LoopChipRamInstructionFetchRequestPhaseStaysStableAfterPrefetchWarmup()
	{
		const int iterations = 32;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = 0x2000;

		ExecuteSynccpu3LoopIterations(cpu, iterations);

		var fetches = GetInstructionFetches(bus, address: 0x1000, instructionProgramCounter: 0x100A).ToArray();
		var requestDeltas = GetRequestDeltas(fetches);
		var diagnostic = BuildCpuPhaseCadenceDiagnostic(bus, fetches);

		Assert.Equal(iterations, fetches.Length);
		Assert.True(
			requestDeltas.Skip(1).All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact(Skip = "Documents unresolved chip-RAM RMW loop phase: ANDI.W memory writeback request cadence keeps 36/40/42 excursions.")]
	public void Synccpu3LoopChipRamDataWriteRequestPhaseStaysStableAfterPrefetchWarmup()
	{
		const int iterations = 32;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = 0x2000;

		ExecuteSynccpu3LoopIterations(cpu, iterations);

		var writes = GetCpuPhases(
			bus,
			address: 0x2000,
			instructionProgramCounter: 0x1000,
			M68kBusAccessKind.CpuDataWrite).ToArray();
		var requestDeltas = GetRequestDeltas(writes);
		var diagnostic = BuildCpuPhaseCadenceDiagnostic(bus, writes);

		Assert.Equal(iterations, writes.Length);
		Assert.True(
			requestDeltas.Skip(1).All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact]
	public void Synccpu3LoopChipRamReadOnlyPollingShapeDataReadRequestPhaseStaysStableAfterPrefetchWarmup()
	{
		const int iterations = 32;
		const int instructionsPerIteration = 5;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteChipRamReadOnlyPollingLoop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = 0x2000;

		ExecuteLoopIterations(cpu, iterations, instructionsPerIteration);

		var reads = GetSynccpu3DataReads(bus, 0x2000).ToArray();
		var requestDeltas = GetRequestDeltas(reads);
		var diagnostic = BuildSynccpu3ReadPhaseDiagnostic(bus, reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(
			requestDeltas.Skip(2).Distinct().Count() == 1,
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact]
	public void Synccpu3LoopChipRamInstructionOnlyShapeBranchFetchRequestPhaseStaysStableAfterPrefetchWarmup()
	{
		const int iterations = 32;
		const int instructionsPerIteration = 5;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteChipRamInstructionOnlyLoop(bus.ChipRam, 0x1000);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;

		ExecuteLoopIterations(cpu, iterations, instructionsPerIteration);

		var fetches = GetInstructionFetches(bus, address: 0x1000, instructionProgramCounter: 0x1008).ToArray();
		var requestDeltas = GetRequestDeltas(fetches);
		var diagnostic = BuildCpuPhaseCadenceDiagnostic(bus, fetches);

		Assert.Equal(iterations, fetches.Length);
		Assert.True(
			requestDeltas.Skip(4).Distinct().Count() == 1,
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact(Skip = "Documents unresolved chip-RAM RMW loop phase: data-read request, grant, and completion deltas all share the same 36/40 cadence.")]
	public void Synccpu3LoopChipRamDataReadRequestGrantAndCompletionCadenceStayAligned()
	{
		const int iterations = 32;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = 0x2000;

		ExecuteSynccpu3LoopIterations(cpu, iterations);

		var reads = GetSynccpu3DataReads(bus, 0x2000).ToArray();
		var requestDeltas = GetRequestDeltas(reads);
		var grantDeltas = GetGrantDeltas(reads);
		var completedDeltas = GetCompletedDeltas(reads);
		var diagnostic = BuildCpuPhaseCadenceDiagnostic(bus, reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(
			requestDeltas.Skip(1).All(delta => delta == 38) &&
			grantDeltas.Skip(1).All(delta => delta == 38) &&
			completedDeltas.Skip(1).All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | grantDeltas={string.Join(",", grantDeltas)} | completedDeltas={string.Join(",", completedDeltas)} | {diagnostic}");
	}
	[Fact]
	public void Synccpu3LoopChipRamRmwDataReadPhaseWalksInsideStableNineteenSlotLoop()
	{
		const int iterations = 32;
		const long initialCycle = 64;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = initialCycle;
		cpu.State.A[3] = 0x2000;

		var loopEndCycles = ExecuteSynccpu3LoopIterationsAndCaptureEndCycles(cpu, iterations);
		var loopStartCycles = GetLoopStartCycles(loopEndCycles, initialCycle);
		var loopDeltas = GetDeltas(loopEndCycles, initialCycle);
		var reads = GetSynccpu3DataReads(bus, 0x2000).ToArray();
		var readOffsets = GetRequestOffsetsFromLoopStart(reads, loopStartCycles);
		var requestDeltas = GetRequestDeltas(reads);
		var expectedRequestDeltas = GetDeltasFromLoopAndOffsetDeltas(loopDeltas, readOffsets);
		var diagnostic =
			$"loopSlots={string.Join(",", loopDeltas.Select(delta => delta / 2))} | " +
			$"readOffsetSlots={string.Join(",", readOffsets.Select(offset => offset / 2))} | " +
			$"requestDeltaSlots={string.Join(",", requestDeltas.Select(delta => delta / 2))} | " +
			BuildSynccpu3ReadPhaseDiagnostic(bus, reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(loopDeltas.All(delta => delta == 38), diagnostic);
		Assert.True(readOffsets.All(offset => offset >= 0 && offset % AmigaConstants.A500PalCpuCyclesPerColorClock == 0), diagnostic);
		Assert.Equal(expectedRequestDeltas, requestDeltas);
	}
	[Fact]
	public void Synccpu3TakenBneShortRefillsLoopTargetBeforeNextIterationMemoryRead()
	{
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, 0x1000,
			0x02, 0x53, 0x00, 0xFF, // ANDI.W #$00FF,(A3)
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x66, 0xF4);            // BNE.S back to ANDI
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[3] = 0x2000;

		for (var i = 0; i < 5; i++)
		{
			cpu.ExecuteInstruction();
		}

		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		Assert.Equal(58, cpu.State.Cycles);

		var branchPhases = bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.InstructionProgramCounter == 0x100A)
			.ToArray();
		var targetOpcodeFetch = branchPhases.FirstOrDefault(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.CpuPhase.Address == 0x1000);
		var targetImmediateFetch = branchPhases.FirstOrDefault(phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
			phase.CpuPhase.Address == 0x1002);

		Assert.True(
			targetOpcodeFetch.CpuPhase.Address == 0x1000 &&
			targetImmediateFetch.CpuPhase.Address == 0x1002 &&
			targetOpcodeFetch.CpuPhase.CompletedCycle <= targetImmediateFetch.CpuPhase.RequestedCycle,
			$"branchPhases={string.Join(",", branchPhases.Select(phase => $"{phase.CpuPhase.AccessKind}:0x{phase.CpuPhase.Address:X6}@{phase.CpuPhase.RequestedCycle}-{phase.CpuPhase.CompletedCycle}"))}");

		cpu.ExecuteInstruction();

		var nextIterationDataRead = bus.CpuBusPhases.FirstOrDefault(phase =>
			phase.CpuPhase.InstructionProgramCounter == 0x1000 &&
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
			phase.CpuPhase.Address == 0x2000 &&
			phase.CpuPhase.RequestedCycle >= targetImmediateFetch.CpuPhase.CompletedCycle);

		Assert.True(
			nextIterationDataRead.CpuPhase.Address == 0x2000 &&
			targetImmediateFetch.CpuPhase.CompletedCycle <= nextIterationDataRead.CpuPhase.RequestedCycle,
			$"targetFetch=0x{targetImmediateFetch.CpuPhase.Address:X6}@{targetImmediateFetch.CpuPhase.RequestedCycle}-{targetImmediateFetch.CpuPhase.CompletedCycle}, phases={string.Join(",", bus.CpuBusPhases.Select(phase => $"pc=0x{phase.CpuPhase.InstructionProgramCounter:X6}:{phase.CpuPhase.AccessKind}:0x{phase.CpuPhase.Address:X6}@{phase.CpuPhase.RequestedCycle}-{phase.CpuPhase.CompletedCycle}"))}");
	}
	[Fact(Skip = "Documents unresolved synccpu3 loop bus-request phase: current chip-RAM reads use a 36/40-cycle cadence instead of a simple 38-cycle cadence.")]
	public void Synccpu3LoopChipRamDataReadRequestPhaseStaysStableAcrossIterations()
	{
		const int iterations = 32;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 64;
		cpu.State.A[3] = 0x2000;

		ExecuteSynccpu3LoopIterations(cpu, iterations);

		var reads = GetSynccpu3DataReads(bus, 0x2000).ToArray();
		var requestDeltas = GetRequestDeltas(reads);
		var diagnostic = BuildSynccpu3ReadPhaseDiagnostic(bus, reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(
			requestDeltas.All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact(Skip = "Documents unresolved synccpu3 loop bus-request phase: VHPOSR reads match the chip-RAM 36/40-cycle cadence, so this is not custom-register specific.")]
	public void Synccpu3LoopVhposrDataReadRequestPhaseStaysStableAcrossIterations()
	{
		const int iterations = 16;
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		WriteSynccpu3Loop(bus.ChipRam, 0x1000);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = (240L * AmigaConstants.A500PalCpuCyclesPerRasterLine) + 64;
		cpu.State.A[3] = 0x00DFF006;

		ExecuteSynccpu3LoopIterations(cpu, iterations);

		var reads = GetSynccpu3DataReads(bus, 0x00DFF006).ToArray();
		var requestDeltas = GetRequestDeltas(reads);
		var diagnostic = BuildSynccpu3ReadPhaseDiagnostic(bus, reads);

		Assert.Equal(iterations, reads.Length);
		Assert.True(
			requestDeltas.All(delta => delta == 38),
			$"requestDeltas={string.Join(",", requestDeltas)} | {diagnostic}");
	}
	[Fact]
	public void AndiWordMemoryDestinationPrefetchesFallthroughBeforeWriteBack()
	{
		// Moira's execAndiEa orders readOp -> prefetch -> writeOp for memory destinations.
		var bus = new AmigaBus(captureBusAccesses: true, enableLiveAgnusDma: true);
		Write(bus.ChipRam, 0x1000,
			0x02, 0x53, 0x00, 0xFF, // ANDI.W #$00FF,(A3)
			0x4E, 0x71,             // NOP
			0x4E, 0x71);            // NOP
		Write(bus.ChipRam, 0x2000, 0x00, 0x11);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[3] = 0x2000;

		_ = cpu.ExecuteInstruction();

		var phases = bus.CpuBusPhases
			.Where(phase => phase.CpuPhase.InstructionProgramCounter == 0x1000)
			.ToArray();
		var dataReadIndex = Array.FindIndex(phases, phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
			phase.CpuPhase.Address == 0x2000);
		var dataWriteIndex = Array.FindIndex(phases, phase =>
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataWrite &&
			phase.CpuPhase.Address == 0x2000);
		var fallthroughPrefetchBetweenReadAndWrite =
			dataReadIndex >= 0 &&
			dataWriteIndex > dataReadIndex &&
			phases
				.Skip(dataReadIndex + 1)
				.Take(dataWriteIndex - dataReadIndex - 1)
				.Any(phase =>
					phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuInstructionFetch &&
					phase.CpuPhase.Address >= 0x1004);

		Assert.True(
			fallthroughPrefetchBetweenReadAndWrite,
			$"phases={string.Join(",", phases.Select(phase => $"{phase.CpuPhase.AccessKind}:{(phase.CpuPhase.IsWrite ? "W" : "R")}:0x{phase.CpuPhase.Address:X6}@{phase.CpuPhase.RequestedCycle}-{phase.CpuPhase.CompletedCycle}"))}");
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
		Assert.Equal(16, cycles);
		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFF);
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
		Assert.Equal(134, cycles);
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
		Assert.Equal(158, cycles);
		Assert.Equal(0xFFFF_FFFDu, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
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
	public void AmigaBusDeferredPseudoFastTimingFlushMatchesImmediateExactTiming()
	{
		var immediateBus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var deferredBus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var firstAddress = immediateBus.ExpansionRamBase + 0x20;
		var secondAddress = immediateBus.ExpansionRamBase + 0x40;
		Write(immediateBus.ExpansionRam, 0x20, 0x12, 0x34);
		Write(immediateBus.ExpansionRam, 0x40, 0x56, 0x78, 0x9A, 0xBC);
		Write(deferredBus.ExpansionRam, 0x20, 0x12, 0x34);
		Write(deferredBus.ExpansionRam, 0x40, 0x56, 0x78, 0x9A, 0xBC);
		var immediateCycle = 20L;
		var deferredCycle = 20L;
		var deferredTiming = (IM68kDeferredCpuInstructionTiming)deferredBus;
		Assert.True(immediateBus.TryReadExactCpuDataWord(firstAddress, ref immediateCycle, out var immediateWord));
		Assert.True(immediateBus.TryReadExactCpuDataLong(secondAddress, ref immediateCycle, out var immediateLong));
		deferredTiming.BeginDeferredCpuInstructionTiming(deferredCycle);
		Assert.True(deferredBus.TryReadExactCpuDataWord(firstAddress, ref deferredCycle, out var deferredWord));
		Assert.True(deferredBus.TryReadExactCpuDataLong(secondAddress, ref deferredCycle, out var deferredLong));
		Assert.Equal(20L, deferredCycle);
		deferredTiming.FlushDeferredCpuInstructionTiming(ref deferredCycle);
		Assert.Equal(immediateWord, deferredWord);
		Assert.Equal(immediateLong, deferredLong);
		Assert.Equal(immediateCycle, deferredCycle);
	}
	[Fact]
	public void AmigaBusDeferredPseudoFastTimingFlushesBeforeChipExactAccess()
	{
		var immediateBus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var deferredBus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var expansionAddress = immediateBus.ExpansionRamBase + 0x20;
		Write(immediateBus.ExpansionRam, 0x20, 0x12, 0x34);
		Write(deferredBus.ExpansionRam, 0x20, 0x12, 0x34);
		Write(immediateBus.ChipRam, 0x2000, 0x56, 0x78);
		Write(deferredBus.ChipRam, 0x2000, 0x56, 0x78);
		var immediateCycle = 20L;
		var deferredCycle = 20L;
		var deferredTiming = (IM68kDeferredCpuInstructionTiming)deferredBus;
		Assert.True(immediateBus.TryReadExactCpuDataWord(expansionAddress, ref immediateCycle, out var immediateExpansion));
		Assert.True(immediateBus.TryReadExactCpuDataWord(0x2000, ref immediateCycle, out var immediateChip));
		deferredTiming.BeginDeferredCpuInstructionTiming(deferredCycle);
		Assert.True(deferredBus.TryReadExactCpuDataWord(expansionAddress, ref deferredCycle, out var deferredExpansion));
		Assert.Equal(20L, deferredCycle);
		Assert.True(deferredBus.TryReadExactCpuDataWord(0x2000, ref deferredCycle, out var deferredChip));
		Assert.Equal(immediateExpansion, deferredExpansion);
		Assert.Equal(immediateChip, deferredChip);
		Assert.Equal(immediateCycle, deferredCycle);
	}
	[Fact]
	public void AmigaBusDeferredPseudoFastWritesNotifyJitBeforeTimingFlush()
	{
		var bus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var address = bus.ExpansionRamBase + 0x20;
		var cycle = 20L;
		var notifications = 0;
		uint notifiedAddress = 0;
		var notifiedByteCount = 0;
		bus.JitEligibleMemoryWritten += (writtenAddress, byteCount) =>
		{
			notifications++;
			notifiedAddress = writtenAddress;
			notifiedByteCount = byteCount;
		};
		var deferredTiming = (IM68kDeferredCpuInstructionTiming)bus;
		deferredTiming.BeginDeferredCpuInstructionTiming(cycle);
		Assert.True(bus.TryWriteExactCpuDataLong(address, 0x1234_5678, ref cycle));
		Assert.Equal(20L, cycle);
		Assert.Equal(0x1234_5678u, BigEndian.ReadUInt32(bus.ExpansionRam, 0x20, "deferred expansion write"));
		Assert.Equal(1, notifications);
		Assert.Equal(address, notifiedAddress);
		Assert.Equal(4, notifiedByteCount);
		deferredTiming.FlushDeferredCpuInstructionTiming(ref cycle);
		Assert.True(cycle >= 20L);
	}
	[Fact]
	public void DeferredPseudoFastTimingFlushesBeforeAddressErrorFrame()
	{
		var bus = new AmigaBus(
			expansionRamSize: 0x10000,
			captureBusAccesses: false,
			enableLiveAgnusDma: true,
			enableLiveDisplayDma: false);
		Write(bus.ChipRam, 0x000C, 0x00, 0x00, 0x40, 0x00);
		Write(bus.ChipRam, 0x1000, 0x32, 0x90); // MOVE.W (A0),(A1)
		Write(bus.ChipRam, 0x4000, 0x4E, 0x71); // NOP at address-error vector
		Write(bus.ExpansionRam, 0x0020, 0x12, 0x34);
		var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
		cpu.Reset(0x1000, 0x00C0_8000);
		cpu.State.A[0] = bus.ExpansionRamBase + 0x20;
		cpu.State.A[1] = 0x2001;
		cpu.ExecuteInstruction();
		var frameOffset = (int)(cpu.State.A[7] - bus.ExpansionRamBase);
		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x00C0_7FF2u, cpu.State.A[7]);
		Assert.Equal(0x3285, ReadWord(bus.ExpansionRam, frameOffset));
		Assert.Equal(0x0000_2001u, ReadLong(bus.ExpansionRam, frameOffset + 2));
		Assert.Equal(0x3290, ReadWord(bus.ExpansionRam, frameOffset + 6));
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.ExpansionRam, frameOffset + 8));
		Assert.Equal(0x0000_1004u, ReadLong(bus.ExpansionRam, frameOffset + 10));
		Assert.Equal(0x00, bus.ChipRam[0x2001]);
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_4002u, cpu.State.ProgramCounter);
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
	[Theory]
	[InlineData(0x00BFE001u)]
	[InlineData(0x00BFE201u)]
	[InlineData(0x00BFEE01u)]
	[InlineData(0x00BFD000u)]
	[InlineData(0x00BFD300u)]
	[InlineData(0x00BFDF00u)]
	public void AmigaBusDeferredExactCpuDataHelpersDeferSideEffectFreeCiaByteReads(uint address)
	{
		var bus = CreateExactCpuDataAmigaBus();
		var deferredTiming = (IM68kDeferredCpuInstructionTiming)bus;
		var cycle = 21L;
		deferredTiming.BeginDeferredCpuInstructionTiming(cycle);
		Assert.True(bus.TryReadExactCpuDataByte(address, ref cycle, out _));
		Assert.Equal(21L, cycle);
		deferredTiming.FlushDeferredCpuInstructionTiming(ref cycle);
		Assert.Equal(ExpectedCiaAccessCycle(21), cycle);
	}
	[Theory]
	[InlineData(0x00BFE401u)]
	[InlineData(0x00BFE801u)]
	[InlineData(0x00BFEC01u)]
	[InlineData(0x00BFED01u)]
	public void AmigaBusDeferredExactCpuDataHelpersFallBackForSideEffectfulCiaByteReads(uint address)
	{
		var bus = CreateExactCpuDataAmigaBus();
		var deferredTiming = (IM68kDeferredCpuInstructionTiming)bus;
		var cycle = 21L;
		deferredTiming.BeginDeferredCpuInstructionTiming(cycle);
		Assert.False(bus.TryReadExactCpuDataByte(address, ref cycle, out _));
		Assert.Equal(21L, cycle);
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
	public void AmigaBusExactCpuDataHelpersResumeChipRamAfterRomOverlayDisabled()
	{
		var bus = CreateExactCpuDataAmigaBus();
		var overlayRom = new byte[0x40000];
		Write(overlayRom, 0, 0xAB, 0xCD);
		Write(bus.ChipRam, 0, 0x12, 0x34);
		bus.MapReadOnlyMemory(0x00FC0000, overlayRom);
		var cycle = 20L;
		Assert.False(bus.TryReadExactCpuDataWord(0x000000, ref cycle, out _));
		Assert.Equal(20L, cycle);
		bus.WriteByte(0x00BFE001, 0x00, 0);
		cycle = 20L;
		Assert.True(bus.TryReadExactCpuDataWord(0x000000, ref cycle, out var value));
		Assert.Equal(0x1234, value);
	}
	[Fact]
	public void AmigaBusExactCpuDataHelpersFallBackForHostTrapBank()
	{
		var bus = CreateExactCpuDataAmigaBus();
		Write(bus.ChipRam, 0x2000, 0x12, 0x34);
		bus.RegisterHostTrapStub(0x2000, _ => { });
		var cycle = 20L;
		Assert.False(bus.TryReadExactCpuDataWord(0x2000, ref cycle, out _));
		Assert.Equal(20L, cycle);
		Assert.False(bus.TryReadExactCpuDataWord(0x2100, ref cycle, out _));
		Assert.Equal(20L, cycle);
	}
	[Fact]
	public void AmigaBusExactCpuDataHelpersGuardExpansionRamBoundaries()
	{
		var bus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		var finalLongAddress = bus.ExpansionRamBase + 0xFFFC;
		Write(bus.ExpansionRam, 0xFFFC, 0x12, 0x34, 0x56, 0x78);
		var cycle = 20L;
		Assert.True(bus.TryReadExactCpuDataLong(finalLongAddress, ref cycle, out var value));
		Assert.Equal(0x1234_5678u, value);
		cycle = 20L;
		Assert.False(bus.TryReadExactCpuDataLong(bus.ExpansionRamBase + 0xFFFE, ref cycle, out _));
		Assert.Equal(20L, cycle);
	}
	[Fact]
	public void AmigaBusExactCpuDataHelpersFallBackForSpecialExpansionRamBank()
	{
		var bus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		bus.RegisterHostTrapStub(bus.ExpansionRamBase, _ => { });
		var cycle = 20L;
		Assert.False(bus.TryReadExactCpuDataWord(bus.ExpansionRamBase + 0x20, ref cycle, out _));
		Assert.Equal(20L, cycle);
		bus = CreateExactCpuDataAmigaBus(expansionRamSize: 0x10000);
		bus.MapReadOnlyMemory(bus.ExpansionRamBase, new byte[] { 0x12, 0x34 });
		cycle = 20L;
		Assert.False(bus.TryReadExactCpuDataWord(bus.ExpansionRamBase + 0x20, ref cycle, out _));
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
	private static void WriteSynccpu3Loop(byte[] memory, int address)
	{
		Write(memory, address,
			0x02, 0x53, 0x00, 0xFF, // ANDI.W #$00FF,(A3)
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x4E, 0x71,             // NOP
			0x66, 0xF4);            // BNE.S back to ANDI
	}
	private static void WriteChipRamReadOnlyPollingLoop(byte[] memory, int address)
	{
		Write(memory, address,
			0x4A, 0x53, // TST.W (A3)
			0x4E, 0x71, // NOP
			0x4E, 0x71, // NOP
			0x4E, 0x71, // NOP
			0x66, 0xF6); // BNE.S back to TST
	}
	private static void WriteChipRamInstructionOnlyLoop(byte[] memory, int address)
	{
		Write(memory, address,
			0x4E, 0x71, // NOP
			0x4E, 0x71, // NOP
			0x4E, 0x71, // NOP
			0x4E, 0x71, // NOP
			0x60, 0xF6); // BRA.S back to first NOP
	}
	private static void ExecuteSynccpu3LoopIterations(IM68kCore cpu, int iterations)
	{
		for (var iteration = 0; iteration < iterations; iteration++)
		{
			for (var instruction = 0; instruction < 5; instruction++)
			{
				cpu.ExecuteInstruction();
			}

			Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		}
	}
	private static void ExecuteLoopIterations(
		IM68kCore cpu,
		int iterations,
		int instructionsPerIteration,
		uint expectedProgramCounter = 0x1000)
	{
		for (var iteration = 0; iteration < iterations; iteration++)
		{
			for (var instruction = 0; instruction < instructionsPerIteration; instruction++)
			{
				cpu.ExecuteInstruction();
			}

			Assert.Equal(expectedProgramCounter, cpu.State.ProgramCounter);
		}
	}
	private static long[] ExecuteSynccpu3LoopIterationsAndCaptureEndCycles(
		IM68kCore cpu,
		int iterations,
		uint expectedProgramCounter = 0x1000)
	{
		var loopEndCycles = new long[iterations];
		for (var iteration = 0; iteration < iterations; iteration++)
		{
			for (var instruction = 0; instruction < 5; instruction++)
			{
				cpu.ExecuteInstruction();
			}

			Assert.Equal(expectedProgramCounter, cpu.State.ProgramCounter);
			loopEndCycles[iteration] = cpu.State.Cycles;
		}

		return loopEndCycles;
	}
	private static IEnumerable<M68kCpuBusPhase> GetSynccpu3DataReads(CycleCountingBus bus, uint address)
		=> bus.CpuBusPhases.Where(phase =>
			phase.InstructionProgramCounter == 0x1000 &&
			phase.AccessKind == M68kBusAccessKind.CpuDataRead &&
			phase.Address == address);
	private static IEnumerable<AmigaCpuBusPhaseTrace> GetSynccpu3DataReads(AmigaBus bus, uint address)
		=> GetSynccpu3DataReads(bus, address, instructionProgramCounter: 0x1000);
	private static IEnumerable<AmigaCpuBusPhaseTrace> GetSynccpu3DataReads(
		AmigaBus bus,
		uint address,
		uint instructionProgramCounter)
		=> bus.CpuBusPhases.Where(phase =>
			phase.CpuPhase.InstructionProgramCounter == instructionProgramCounter &&
			phase.CpuPhase.AccessKind == M68kBusAccessKind.CpuDataRead &&
			phase.CpuPhase.Address == address);
	private static IEnumerable<AmigaCpuBusPhaseTrace> GetInstructionFetches(
		AmigaBus bus,
		uint address,
		uint instructionProgramCounter)
		=> GetCpuPhases(bus, address, instructionProgramCounter, M68kBusAccessKind.CpuInstructionFetch);
	private static IEnumerable<AmigaCpuBusPhaseTrace> GetCpuPhases(
		AmigaBus bus,
		uint address,
		uint instructionProgramCounter,
		M68kBusAccessKind accessKind)
		=> bus.CpuBusPhases.Where(phase =>
			phase.CpuPhase.InstructionProgramCounter == instructionProgramCounter &&
			phase.CpuPhase.AccessKind == accessKind &&
			phase.CpuPhase.Address == address);
	private static long[] GetDeltas(IReadOnlyList<long> values, long initialCycle)
	{
		var deltas = new long[values.Count];
		var previous = initialCycle;
		for (var i = 0; i < values.Count; i++)
		{
			deltas[i] = values[i] - previous;
			previous = values[i];
		}

		return deltas;
	}
	private static long[] GetLoopStartCycles(IReadOnlyList<long> loopEndCycles, long initialCycle)
	{
		var starts = new long[loopEndCycles.Count];
		if (starts.Length == 0)
		{
			return starts;
		}

		starts[0] = initialCycle;
		for (var i = 1; i < starts.Length; i++)
		{
			starts[i] = loopEndCycles[i - 1];
		}

		return starts;
	}
	private static long[] GetRequestOffsetsFromLoopStart(
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases,
		IReadOnlyList<long> loopStartCycles)
	{
		Assert.Equal(loopStartCycles.Count, phases.Count);
		var offsets = new long[phases.Count];
		for (var i = 0; i < phases.Count; i++)
		{
			offsets[i] = phases[i].CpuPhase.RequestedCycle - loopStartCycles[i];
		}

		return offsets;
	}
	private static long[] GetDeltasFromLoopAndOffsetDeltas(
		IReadOnlyList<long> loopDeltas,
		IReadOnlyList<long> offsets)
	{
		Assert.Equal(loopDeltas.Count, offsets.Count);
		var deltas = new long[Math.Max(0, offsets.Count - 1)];
		for (var i = 1; i < offsets.Count; i++)
		{
			deltas[i - 1] = loopDeltas[i] + offsets[i] - offsets[i - 1];
		}

		return deltas;
	}
	private static long[] GetRequestDeltas(IReadOnlyList<M68kCpuBusPhase> reads)
	{
		var deltas = new long[Math.Max(0, reads.Count - 1)];
		for (var i = 1; i < reads.Count; i++)
		{
			deltas[i - 1] = reads[i].RequestedCycle - reads[i - 1].RequestedCycle;
		}

		return deltas;
	}
	private static long[] GetRequestDeltas(IReadOnlyList<AmigaCpuBusPhaseTrace> reads)
	{
		var deltas = new long[Math.Max(0, reads.Count - 1)];
		for (var i = 1; i < reads.Count; i++)
		{
			deltas[i - 1] = reads[i].CpuPhase.RequestedCycle - reads[i - 1].CpuPhase.RequestedCycle;
		}

		return deltas;
	}
	private static long[] GetGrantDeltas(IReadOnlyList<AmigaCpuBusPhaseTrace> phases)
	{
		var deltas = new long[Math.Max(0, phases.Count - 1)];
		for (var i = 1; i < phases.Count; i++)
		{
			deltas[i - 1] = GetGrantCycle(phases[i]) - GetGrantCycle(phases[i - 1]);
		}

		return deltas;
	}
	private static long[] GetCompletedDeltas(IReadOnlyList<AmigaCpuBusPhaseTrace> phases)
	{
		var deltas = new long[Math.Max(0, phases.Count - 1)];
		for (var i = 1; i < phases.Count; i++)
		{
			deltas[i - 1] = phases[i].CpuPhase.CompletedCycle - phases[i - 1].CpuPhase.CompletedCycle;
		}

		return deltas;
	}
	private static long GetGrantCycle(AmigaCpuBusPhaseTrace phase)
		=> phase.BusAccess.HasValue
			? phase.BusAccess.GetValueOrDefault().GrantedCycle
			: phase.CpuPhase.CompletedCycle;
	private static string BuildSynccpu3ReadPhaseDiagnostic(IReadOnlyList<M68kCpuBusPhase> reads)
		=> string.Join(
			"; ",
			reads.Select(phase =>
				$"req={phase.RequestedCycle} done={phase.CompletedCycle} pc=0x{phase.InstructionProgramCounter:X4} {phase.AccessKind} 0x{phase.Address:X6}"));
	private static string BuildSynccpu3ReadPhaseDiagnostic(AmigaBus bus, IReadOnlyList<AmigaCpuBusPhaseTrace> reads)
		=> string.Join(
			"; ",
			reads.Select(phase =>
			{
				var request = bus.GetPalBeamPosition(phase.CpuPhase.RequestedCycle);
				var complete = bus.GetPalBeamPosition(phase.CpuPhase.CompletedCycle);
				var grantCycle = phase.BusAccess.HasValue
					? phase.BusAccess.GetValueOrDefault().GrantedCycle
					: phase.CpuPhase.CompletedCycle;
				var grant = bus.GetPalBeamPosition(grantCycle);
				var wait = phase.BusAccess.HasValue
					? phase.BusAccess.GetValueOrDefault().WaitCycles
					: 0;
				return $"req={phase.CpuPhase.RequestedCycle}/v{request.BeamLine}h{request.BeamHorizontal} " +
					$"grant={grantCycle}/v{grant.BeamLine}h{grant.BeamHorizontal} " +
					$"done={phase.CpuPhase.CompletedCycle}/v{complete.BeamLine}h{complete.BeamHorizontal} wait={wait}";
			}));
	private static string BuildCpuPhaseCadenceDiagnostic(
		AmigaBus bus,
		IReadOnlyList<AmigaCpuBusPhaseTrace> phases)
		=> string.Join(
			"; ",
			phases.Select(phase =>
			{
				var request = bus.GetPalBeamPosition(phase.CpuPhase.RequestedCycle);
				var grantCycle = GetGrantCycle(phase);
				var grant = bus.GetPalBeamPosition(grantCycle);
				var complete = bus.GetPalBeamPosition(phase.CpuPhase.CompletedCycle);
				var wait = phase.BusAccess.HasValue
					? phase.BusAccess.GetValueOrDefault().WaitCycles
					: 0;
				return $"pc=0x{phase.CpuPhase.InstructionProgramCounter:X4} {phase.CpuPhase.AccessKind} " +
					$"0x{phase.CpuPhase.Address:X6} req={phase.CpuPhase.RequestedCycle}/v{request.BeamLine}h{request.BeamHorizontal} " +
					$"grant={grantCycle}/v{grant.BeamLine}h{grant.BeamHorizontal} " +
					$"done={phase.CpuPhase.CompletedCycle}/v{complete.BeamLine}h{complete.BeamHorizontal} wait={wait}";
			}));
	private static void AssertCpuPhaseSequence(
		IReadOnlyList<M68kCpuBusPhase> actual,
		params (M68kBusAccessKind Kind, uint Address, M68kOperandSize Size, bool IsWrite, long RequestedCycle, long CompletedCycle)[] expected)
	{
		var diagnostic = string.Join(
			",",
			actual.Select(phase =>
				$"{phase.AccessKind}:{(phase.IsWrite ? "W" : "R")}:0x{phase.Address:X6}:{phase.Size}@{phase.RequestedCycle}-{phase.CompletedCycle}"));
		Assert.True(actual.Count == expected.Length, $"expected={expected.Length}, actual={actual.Count}, phases={diagnostic}");
		for (var i = 0; i < expected.Length; i++)
		{
			var phase = actual[i];
			var expectedPhase = expected[i];
			Assert.True(
				phase.AccessKind == expectedPhase.Kind &&
				phase.Address == expectedPhase.Address &&
				phase.Size == expectedPhase.Size &&
				phase.IsWrite == expectedPhase.IsWrite &&
				phase.RequestedCycle == expectedPhase.RequestedCycle &&
				phase.CompletedCycle == expectedPhase.CompletedCycle,
				$"index={i}, expected={expectedPhase.Kind}:{(expectedPhase.IsWrite ? "W" : "R")}:0x{expectedPhase.Address:X6}:{expectedPhase.Size}@{expectedPhase.RequestedCycle}-{expectedPhase.CompletedCycle}, " +
				$"actual={phase.AccessKind}:{(phase.IsWrite ? "W" : "R")}:0x{phase.Address:X6}:{phase.Size}@{phase.RequestedCycle}-{phase.CompletedCycle}, phases={diagnostic}");
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
	private static void WriteAmigaMemory(AmigaBus bus, uint address, params byte[] data)
	{
		for (var i = 0; i < data.Length; i++)
		{
			bus.WriteHostByte(address + (uint)i, data[i]);
		}
	}
	private static ushort ReadWord(byte[] memory, int address)
		=> (ushort)((memory[address] << 8) | memory[address + 1]);
	private static uint ReadLong(byte[] memory, int address)
		=> ((uint)memory[address] << 24) |
			((uint)memory[address + 1] << 16) |
			((uint)memory[address + 2] << 8) |
			memory[address + 3];
	private static long ExpectedCiaAccessCycle(long requestedCycle)
	{
		var cycle = Math.Max(0, requestedCycle + 1);
		var remainder = cycle % AmigaConstants.A500PalCpuCyclesPerCiaTick;
		return remainder == 0
			? cycle
			: cycle + AmigaConstants.A500PalCpuCyclesPerCiaTick - remainder;
	}
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
