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

		Assert.Equal(0x8610, bus.ReadWord(0x7FFA)); // post-BTST SR saved by trace
		Assert.Equal(0x1004u, bus.ReadLong(0x7FFC));
		Assert.Equal(9, cpu.State.LastExceptionVector);
	}

	[Fact]
	public void ImmediateBtstCannotTestImmediateOperand()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x083C, 0x0007, 0x3FC2);
		bus.WriteLong(16, 0x5000); // Illegal-instruction vector

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.StatusRegister = 0x271B;

		cpu.ExecuteInstruction();

		// M68000PM 4-63 excludes #data for static BTST. Dynamic BTST's
		// different legal table (4-62) remains covered separately above.
		Assert.Equal(4, cpu.State.LastExceptionVector);
		Assert.Equal(0x5000u, cpu.State.ProgramCounter);
		Assert.Equal(0x271B, bus.ReadWord(0x7FFA));
		Assert.Equal(0x1000u, bus.ReadLong(0x7FFC));
	}
}
