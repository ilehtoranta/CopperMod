using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kShiftTests
{
	[Fact]
	public void AslByteSetsOverflowWhenSignChanges()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xE302);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.D[2] = 0x6891_C884;
		cpu.State.StatusRegister = 0x0700;

		cpu.ExecuteInstruction();

		Assert.Equal(0x6891_C808u, cpu.State.D[2]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}
}
