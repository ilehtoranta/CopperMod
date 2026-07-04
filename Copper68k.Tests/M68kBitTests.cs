using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kBitTests
{
	[Fact]
	public void DynamicBtstCanTestImmediateOperand()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x033C, 0x3FC2);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.D[1] = 0x98C0_FED7;
		cpu.State.StatusRegister = 0x8610;

		cpu.ExecuteInstruction();

		Assert.Equal(0x8610, cpu.State.StatusRegister);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void ImmediateBtstCanTestImmediateOperand()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x083C, 0x0007, 0x3FC2);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.StatusRegister = 0x2700;

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
	}
}
