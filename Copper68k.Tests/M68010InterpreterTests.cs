using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68010InterpreterTests
{
	[Fact]
	public void FactoryCreatesM68010CoreWithoutM68020StackMode()
	{
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68010, new Copper68kTestBus());
		var interpreter = Assert.IsType<M68010Interpreter>(cpu);
		Assert.False(interpreter.State.M68020StackModeEnabled);
	}

	[Fact]
	public void MovecTransfersVectorBaseRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E7B, 0x0801, // MOVEC D0,VBR
			0x4E7A, 0x1801); // MOVEC VBR,D1
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0400;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		Assert.Equal(0x0000_0400u, cpu.State.VectorBaseRegister);
		Assert.Equal(0x0000_0400u, cpu.State.D[1]);
		Assert.False(cpu.State.M68020StackModeEnabled);
	}

	[Theory]
	[InlineData((ushort)0x000, 0xFFFF_FFFEu, 0x0000_0006u)]
	[InlineData((ushort)0x001, 0xFFFF_FFFDu, 0x0000_0005u)]
	[InlineData((ushort)0x801, 0x1234_5678u, 0x1234_5678u)]
	public void MovecTransfersSupportedControlRegisters(
		ushort controlRegister,
		uint sourceValue,
		uint expectedValue)
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(
			bus,
			CodeBase,
			0x4E7B, controlRegister, // MOVEC D0,control
			0x4E7A, (ushort)(0x1000 | controlRegister)); // MOVEC control,D1
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = sourceValue;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(expectedValue, cpu.State.D[1]);
		Assert.Equal(CodeBase + 8u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void MovecVectorBaseToA7UpdatesActiveSupervisorStackPointer()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E7A, 0xF801); // MOVEC VBR,A7
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0000_4800;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4800u, cpu.State.A[7]);
		Assert.Equal(0x0000_4800u, cpu.State.SupervisorStackPointer);
	}

	[Fact]
	public void MovecInUserModeRaisesPrivilegeViolationWithoutChangingControlRegister()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E7B, 0x0801); // MOVEC D0,VBR
		bus.WriteLong(0x0400 + (8u * 4), 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.ResetStackPointers(0x3000, 0x4000, supervisorMode: false);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0400u, cpu.State.VectorBaseRegister);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(0, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(8 * 4, bus.ReadWord(0x2FFE));
	}

	[Fact]
	public void MovecUnsupportedControlRegisterRaisesIllegalWithoutChangingDestination()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x4E7A, 0x1808); // MOVEC PCR,D1
		bus.WriteLong(0x0400 + (4u * 4), 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.State.D[1] = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.D[1]);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(4 * 4, bus.ReadWord(0x2FFE));
	}

	[Fact]
	public void ExceptionUsesVectorBaseAndFormatZeroFrameAndRteRestoresState()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0xA000);
		bus.WriteLong(0x0400 + (10u * 4), 0x0000_2000);
		WriteWords(bus, 0x2000, 0x4E73); // RTE
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(10 * 4, bus.ReadWord(0x2FFE));

		cpu.ExecuteInstruction();

		Assert.Equal(CodeBase, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, cpu.State.StatusRegister);
	}

	[Fact]
	public void InterruptUsesVectorBaseAndFormatZeroFrame()
	{
		var bus = new ZeroWaitCodeBus();
		bus.WriteLong(0x0400 + (27u * 4), 0x0000_2400);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.VectorBaseRegister = 0x0400;
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.RequestInterrupt(3, 27u * 4);

		Assert.Equal(0x2400u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(27 * 4, bus.ReadWord(0x2FFE));
		Assert.Equal(3, (cpu.State.StatusRegister >> 8) & 7);
	}

	[Fact]
	public void SharedInstructionsMatchM68000ExecutionModel()
	{
		var m68000Bus = new ZeroWaitCodeBus();
		var m68010Bus = new ZeroWaitCodeBus();
		var program = new ushort[]
		{
			0x7001, // MOVEQ #1,D0
			0x5280, // ADDQ.L #1,D0
			0x1080, // MOVE.B D0,(A0)
			0x4E71 // NOP
		};
		WriteWords(m68000Bus, CodeBase, program);
		WriteWords(m68010Bus, CodeBase, program);
		var m68000 = new M68kInterpreter(m68000Bus);
		var m68010 = new M68010Interpreter(m68010Bus);
		m68000.Reset(CodeBase, 0x3000);
		m68010.Reset(CodeBase, 0x3000);
		m68000.State.A[0] = 0x2000;
		m68010.State.A[0] = 0x2000;

		for (var i = 0; i < program.Length; i++)
		{
			m68000.ExecuteInstruction();
			m68010.ExecuteInstruction();
		}

		Assert.Equal(m68000.State.ProgramCounter, m68010.State.ProgramCounter);
		Assert.Equal(m68000.State.StatusRegister, m68010.State.StatusRegister);
		Assert.Equal(m68000.State.Cycles, m68010.State.Cycles);
		Assert.Equal(m68000.State.NativeCycles, m68010.State.NativeCycles);
		Assert.Equal(m68000.State.D, m68010.State.D);
		Assert.Equal(m68000.State.A, m68010.State.A);
		Assert.Equal(ReadByte(m68000Bus, 0x2000), ReadByte(m68010Bus, 0x2000));
		Assert.Equal(m68000Bus.InstructionFetchWords, m68010Bus.InstructionFetchWords);
	}

	[Fact]
	public void OddWordReadRaisesFormat8AddressErrorFrame()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x3211); // MOVE.W (A1),D1
		bus.WriteLong(3u * 4u, 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.A[1] = 1;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FF8));
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(0x800Cu, bus.ReadWord(0x2FFE));
	}

	[Fact]
	public void RejectsM68020OnlyExtbLong()
	{
		var bus = new ZeroWaitCodeBus();
		WriteWords(bus, CodeBase, 0x49C0); // EXTB.L D0
		bus.WriteLong(4u * 4u, 0x0000_2000);
		var cpu = new M68010Interpreter(bus);
		cpu.Reset(CodeBase, 0x3000);
		cpu.State.D[0] = 0x0000_0080;
		cpu.ExecuteInstruction();
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x0000_0080u, cpu.State.D[0]);
		Assert.Equal(0x2FF8u, cpu.State.A[7]);
		Assert.Equal(CodeBase, bus.ReadLong(0x2FFA));
		Assert.Equal(4 * 4, bus.ReadWord(0x2FFE));
	}

	private const uint CodeBase = 0x1000;
}
