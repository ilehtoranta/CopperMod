using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kChkTests
{
	[Fact]
	public void ChkUpperBoundTrapClearsConditionCodesExceptExtend()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4D82); // CHK D2,D6
		bus.WriteLong(6 * 4, 0x2000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[2] = 3;
		cpu.State.D[6] = 5;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | 0x0200 |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor | 0x0200 | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(M68kCpuState.Supervisor | 0x0200 | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void ChkNegativeTrapSetsNegativeAndClearsOtherConditionCodesExceptExtend()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4D82); // CHK D2,D6
		bus.WriteLong(6 * 4, 0x2000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[2] = 3;
		cpu.State.D[6] = 0xFFFF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | 0x0200 |
			M68kCpuState.Extend | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | 0x0200 |
			M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(expectedStatus, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void ChkInRangeClearsConditionCodesExceptExtend()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4D82); // CHK D2,D6

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[2] = 3;
		cpu.State.D[6] = 2;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | 0x0200 |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor | 0x0200 | M68kCpuState.Extend, cpu.State.StatusRegister);
	}
}
