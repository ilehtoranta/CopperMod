using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68kInstructionFrequencyTests
{
	private const uint FastCodeBase = AmigaConstants.A500BootPseudoFastRamBase;
	private const int FastCodeSize = 64 * 1024;

	[Fact]
	public void InterpreterFrequencyCapturesFamilyAndOpcodeCounts()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			0x1000,
			0x7001, // MOVEQ #1,D0
			0xB308); // CMPM.B (A0)+,(A1)+
		bus.ChipRam[0x2000] = 0x42;
		bus.ChipRam[0x3000] = 0x42;
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.InstructionFrequencyEnabled = true;
		cpu.State.A[0] = 0x2000;
		cpu.State.A[1] = 0x3000;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		var snapshot = cpu.CaptureInstructionFrequency();
		Assert.Equal(2, snapshot.TotalInstructions);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.Move && family.Count == 1);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.CompareMemory && family.Count == 1);
		Assert.Contains(snapshot.Opcodes, opcode => opcode.Opcode == 0xB308 && opcode.Mnemonic == "CMPM" && opcode.Count == 1);
		Assert.Contains(snapshot.HotPcs, pc => pc.ProgramCounter == 0x1000 && pc.Opcode == 0x7001 && pc.Count == 1);
		Assert.Contains(snapshot.HotPcs, pc => pc.ProgramCounter == 0x1002 && pc.Opcode == 0xB308 && pc.Count == 1);
	}

	[Fact]
	public void InstructionFrequencyCapturesNextJitTargets()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			0x1000,
			0x7001, // MOVEQ #1,D0
			0x4440, // NEG.W D0
			0xC0C0); // MULU.W D0,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.InstructionFrequencyEnabled = true;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		var snapshot = cpu.CaptureInstructionFrequency();
		Assert.Equal(3, snapshot.TotalInstructions);
		Assert.Contains(snapshot.JitTargets, target => target.Target == M68kJitTarget.NegNot && target.Count == 1);
		Assert.Contains(snapshot.JitTargets, target => target.Target == M68kJitTarget.MultiplyDivide && target.Count == 1);
		Assert.Contains(snapshot.Opcodes, opcode => opcode.Opcode == 0x4440 && opcode.JitTarget == M68kJitTarget.NegNot);
		Assert.Contains(snapshot.Opcodes, opcode => opcode.Opcode == 0xC0C0 && opcode.JitTarget == M68kJitTarget.MultiplyDivide);
		Assert.DoesNotContain(snapshot.Opcodes, opcode => opcode.Opcode == 0x7001 && opcode.JitTarget != M68kJitTarget.None);
	}

	[Fact]
	public void JitTargetClassifierDoesNotTreatSupportedUnaryOrStatusControlAsNextTargets()
	{
		Assert.Equal(M68kJitTarget.NegNot, M68kInstructionClassifier.GetJitTarget(0x4000));
		Assert.Equal(M68kJitTarget.NegNot, M68kInstructionClassifier.GetJitTarget(0x4604));
		Assert.Equal(M68kJitTarget.NegNot, M68kInstructionClassifier.GetJitTarget(0x4442));
		Assert.Equal(M68kJitTarget.MultiplyDivide, M68kInstructionClassifier.GetJitTarget(0xC1F5));
		Assert.Equal(M68kJitTarget.None, M68kInstructionClassifier.GetJitTarget(0x4242));
		Assert.Equal(M68kJitTarget.None, M68kInstructionClassifier.GetJitTarget(0x4A43));
		Assert.Equal(M68kJitTarget.None, M68kInstructionClassifier.GetJitTarget(0x44FC));
	}

	[Fact]
	public void InstructionFrequencyCanBeResetBetweenBenchmarkPhases()
	{
		var bus = new AmigaBus();
		WriteWords(bus, 0x1000, 0x7001, 0x7202);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.InstructionFrequencyEnabled = true;
		cpu.ExecuteInstruction();

		cpu.ResetInstructionFrequency();
		cpu.ExecuteInstruction();

		var snapshot = cpu.CaptureInstructionFrequency();
		Assert.Equal(1, snapshot.TotalInstructions);
		Assert.Contains(snapshot.Opcodes, opcode => opcode.Opcode == 0x7202 && opcode.Count == 1);
		Assert.DoesNotContain(snapshot.Opcodes, opcode => opcode.Opcode == 0x7001);
		Assert.Contains(snapshot.HotPcs, pc => pc.ProgramCounter == 0x1002 && pc.Opcode == 0x7202 && pc.Count == 1);
		Assert.DoesNotContain(snapshot.HotPcs, pc => pc.ProgramCounter == 0x1000 && pc.Opcode == 0x7001);
	}

	[Fact]
	public void InterpreterFrequencyCapturesBackwardBranchHotLoopBlocks()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			0x1000,
			0x7002, // MOVEQ #2,D0
			0x51C8, // DBRA D0,loop
			0xFFFE, // loop target is the DBRA opcode
			0x4E71); // NOP after expiry
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.InstructionFrequencyEnabled = true;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		var snapshot = cpu.CaptureInstructionFrequency();
		Assert.Equal(4, snapshot.TotalInstructions);
		Assert.Contains(snapshot.HotPcs, pc => pc.ProgramCounter == 0x1002 && pc.Opcode == 0x51C8 && pc.Count == 3);
		var loop = Assert.Single(snapshot.HotLoops);
		Assert.Equal(0x1002u, loop.StartProgramCounter);
		Assert.Equal(0x1006u, loop.EndProgramCounter);
		Assert.Equal(0x1002u, loop.BranchProgramCounter);
		Assert.Equal(0x1002u, loop.TargetProgramCounter);
		Assert.Equal(0x51C8, loop.BranchOpcode);
		Assert.Equal("DBcc", loop.BranchMnemonic);
		Assert.Equal(4, loop.ByteLength);
		Assert.Equal(2, loop.Count);
	}

	[Fact]
	public void TimedInterpreterFrequencyCapturesBackwardBranchHotLoopBlocks()
	{
		var bus = new AmigaBus();
		WriteWords(
			bus,
			0x1000,
			0x7002, // MOVEQ #2,D0
			0x51C8, // DBRA D0,loop
			0xFFFE, // loop target is the DBRA opcode
			0x4E71); // NOP after expiry
		var cpu = new M68020Interpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.InstructionFrequencyEnabled = true;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		var snapshot = cpu.CaptureInstructionFrequency();
		Assert.Equal(4, snapshot.TotalInstructions);
		Assert.Contains(snapshot.HotPcs, pc => pc.ProgramCounter == 0x1002 && pc.Opcode == 0x51C8 && pc.Count == 3);
		var loop = Assert.Single(snapshot.HotLoops);
		Assert.Equal(0x1002u, loop.StartProgramCounter);
		Assert.Equal(0x1006u, loop.EndProgramCounter);
		Assert.Equal(0x1002u, loop.BranchProgramCounter);
		Assert.Equal(0x1002u, loop.TargetProgramCounter);
		Assert.Equal(0x51C8, loop.BranchOpcode);
		Assert.Equal("DBcc", loop.BranchMnemonic);
		Assert.Equal(4, loop.ByteLength);
		Assert.Equal(2, loop.Count);
	}

	[Fact]
	public void JitFrequencyIncludesCompiledTraceInstructions()
	{
		var bus = new AmigaBus(expansionRamSize: FastCodeSize);
		WriteWords(
			bus,
			FastCodeBase,
			0x7001, // MOVEQ #1,D0
			0x5280, // ADDQ.L #1,D0
			0x60FA); // BRA.S loop
		var cpu = new M68kJitCore(bus);
		cpu.Reset(FastCodeBase, 0x4000);
		cpu.InstructionFrequencyEnabled = true;

		var executed = cpu.ExecuteInstructions(240, null, new CountingBoundary());

		var snapshot = cpu.CaptureInstructionFrequency();
		Assert.Equal((long)executed, snapshot.TotalInstructions);
		Assert.True(cpu.Counters.CompiledTraces > 0);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.Move);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.QuickArithmetic);
		Assert.Contains(snapshot.Families, family => family.Family == M68kInstructionFamily.ControlFlow);
	}

	private static void WriteWords(AmigaBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}

	private sealed class CountingBoundary : IM68kInstructionBoundary
	{
		public bool BeforeInstruction()
			=> true;

		public void AfterInstruction(long previousCycle, long currentCycle)
		{
			_ = previousCycle;
			_ = currentCycle;
		}
	}
}
