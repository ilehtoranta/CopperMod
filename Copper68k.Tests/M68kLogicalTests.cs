using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kLogicalTests
{
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
