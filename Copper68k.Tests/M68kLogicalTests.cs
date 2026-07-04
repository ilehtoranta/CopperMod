using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kLogicalTests
{
	[Fact]
	public void ClrWordPostincrementAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x425B); // CLR.W (A3)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[3]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}

	[Fact]
	public void ClrLongPredecrementAddressErrorKeepsDecrementedAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x42A6); // CLR.L -(A6)
		bus.WriteLong(3 * 4, 0x4000);
		bus.WriteLong(0x2101, 0x1234_5678);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2105;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2101u, cpu.State.A[6]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x42B5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x42A6, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void EorBytePostincrementDestinationAdvancesOnce()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xBF1B); // EOR.B D7,(A3)+
		bus.Memory[0x2000] = 0xBB;

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[3] = 0x2000;
		cpu.State.D[7] = 0x6A34_196D;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2001u, cpu.State.A[3]);
		Assert.Equal(0xD6, bus.Memory[0x2000]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
	}

	[Fact]
	public void EoriToSrClearsUndefinedStatusRegisterBits()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x0A7C, 0xAA30);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = 0xA40B;

		cpu.ExecuteInstruction();

		Assert.Equal(0x061B, cpu.State.StatusRegister);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
	}
}
