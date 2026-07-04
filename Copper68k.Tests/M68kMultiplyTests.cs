using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kMultiplyTests
{
	[Fact]
	public void MuluPostincrementSourceAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xC6D8); // MULU (A0)+,D3
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2101;
		cpu.State.D[3] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[0]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}
}
