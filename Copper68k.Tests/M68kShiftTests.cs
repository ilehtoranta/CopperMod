using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kShiftTests
{
	[Fact]
	public void RoxlByteRegisterZeroCountCopiesExtendToCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xE533); // ROXL.B D2,D3

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.D[2] = 0x80;
		cpu.State.D[3] = 0x7A4F57BF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x7A4F57BFu, cpu.State.D[3]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry, cpu.State.StatusRegister);
	}

	[Fact]
	public void AslWordPostincrementMemoryAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xE1DC); // ASL.W (A4)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[4] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[4]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}

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
