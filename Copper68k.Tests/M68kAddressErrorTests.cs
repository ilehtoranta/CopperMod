using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kAddressErrorTests
{
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
