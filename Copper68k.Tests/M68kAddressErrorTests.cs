using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kAddressErrorTests
{
	[Fact]
	public void OddWordDataReadRaisesAddressErrorWith68000Frame()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x3010); // MOVE.W (A0),D0
		bus.WriteLong(3 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2001;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x3015, bus.ReadWord(0x2FF2)); // status word: (opcode & $FFE0) | function-code
		Assert.Equal(0x2001u, bus.ReadLong(0x2FF4));
		Assert.Equal(0x3010, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void OddWordDataWriteRaisesAddressErrorWith68000Frame()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x3080); // MOVE.W D0,(A0)
		bus.WriteLong(3 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234;
		cpu.State.A[0] = 0x2001;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x3085, bus.ReadWord(0x2FF2)); // status word: (opcode & $FFE0) | function-code
		Assert.Equal(0x2001u, bus.ReadLong(0x2FF4));
		Assert.Equal(0x3080, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x2FFC));
		Assert.Equal(0x00, bus.Memory[0x2001]);
	}

	[Fact]
	public void DataAddressErrorStacksExtensionWordProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x2EC604, 0xDBB3, 0x0C4F, 0x06B7);
		bus.WriteLong(0x00000C, 0xE010_E916);
		bus.WriteWords(0x10E916, 0x0A3A, 0xDCEE);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x2EC604, 0x4542AC);
		cpu.State.D[0] = 0xA1E9_40B7;
		cpu.State.D[5] = 0x014F_BCC8;
		cpu.State.A[3] = 0x3E4D_4A5B;
		cpu.State.StatusRegister = 0xA313;

		cpu.ExecuteInstruction();

		Assert.Equal(0x45429Eu, cpu.State.SupervisorStackPointer);
		Assert.Equal(0xDBB5, bus.ReadWord(0x45429E));
		Assert.Equal(0xE036_8B61u, bus.ReadLong(0x4542A0));
		Assert.Equal(0xDBB3, bus.ReadWord(0x4542A4));
		Assert.Equal(0xA313, bus.ReadWord(0x4542A6));
		Assert.Equal(0x002E_C606u, bus.ReadLong(0x4542A8));
		Assert.Equal(0x2313, cpu.State.StatusRegister);
		Assert.Equal(0xE010_E916u, cpu.State.ProgramCounter);
	}
}
