using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kMovemTests
{
	[Fact]
	public void MovemLongRegistersToPredecrementAddressErrorStacksPostfetchProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x48E5, 0x0DE0); // MOVEM.L D5-D7/A0-A2,-(A5)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Supervisor |
			M68kCpuState.Extend | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x48E5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x20FFu, bus.ReadLong(0x4FF4));
		Assert.Equal(0x48E5, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MovemLongMemoryToRegistersAddressErrorStacksPostfetchProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4CD0, 0x3813); // MOVEM.L (A0),D0-D1,D4,A3-A5
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x4CD5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x4CD0, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MovemLongPcRelativeAddressErrorUsesInstructionReadStatus()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4CFA, 0x3128, 0x8A13); // MOVEM.L d16(PC),D3,D5,A0,A4-A5
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x4CF2, bus.ReadWord(0x4FF2));
		Assert.Equal(0xFFFF9A17u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x4CFA, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1008u, bus.ReadLong(0x4FFC));
	}
}
