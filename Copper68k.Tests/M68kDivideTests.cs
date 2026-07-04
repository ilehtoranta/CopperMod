using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kDivideTests
{
	[Fact]
	public void DivsPostincrementSourceAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x8DDD); // DIVS (A5)+,D6
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2101;
		cpu.State.D[6] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[5]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}

	[Fact]
	public void DivsOverflowSetsNegativeFromQuotientSignAndClearsZeroCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x87D0); // DIVS (A0),D3
		bus.WriteWords(0x2000, 0x8B97);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[3] = 0x57CF_E913;
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x57CF_E913u, cpu.State.D[3]);
		Assert.Equal(M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow, cpu.State.StatusRegister);
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void DivuOverflowSetsNegativeOverflowAndClearsZeroCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x8AD5); // DIVU (A5),D5
		bus.WriteWords(0x2000, 0xC66B);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[5] = 0x2000;
		cpu.State.D[5] = 0xD83B_1E26;
		cpu.State.StatusRegister = M68kCpuState.Trace | 0x0100 |
			M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0xD83B_1E26u, cpu.State.D[5]);
		Assert.Equal(M68kCpuState.Trace | 0x0100 |
			M68kCpuState.Negative | M68kCpuState.Overflow, cpu.State.StatusRegister);
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
	}
}
