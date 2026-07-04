using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kBcdTests
{
	[Fact]
	public void AbcdPredecrementMatchesSingleStepInvalidDigitOverflow()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xCB0B);
		bus.WriteWord(0x966E, 0x2E6F);
		bus.WriteWord(0x2FA4, 0xB6DB);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.A[3] = 0x9670;
		cpu.State.A[5] = 0x2FA6;
		cpu.State.StatusRegister = 0x070A;

		cpu.ExecuteInstruction();

		Assert.Equal(0x966Fu, cpu.State.A[3]);
		Assert.Equal(0x2FA5u, cpu.State.A[5]);
		Assert.Equal(0xB6B0, bus.ReadWord(0x2FA4));
		Assert.Equal(0x071B, cpu.State.StatusRegister);
	}

	[Fact]
	public void AbcdPredecrementSameAddressRegisterReadsSourceBeforeDestinationDecrement()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xCB0D);
		bus.WriteWord(0xCB74, 0x3A4E);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.A[5] = 0xCB76;
		cpu.State.StatusRegister = 0x8308;

		cpu.ExecuteInstruction();

		Assert.Equal(0xCB74u, cpu.State.A[5]);
		Assert.Equal(0x8E4E, bus.ReadWord(0xCB74));
		Assert.Equal(0x8308, cpu.State.StatusRegister);
	}

	[Fact]
	public void AbcdPredecrementDoesNotCarryFromLowNibbleCorrectionAlone()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xCD09);
		bus.WriteWord(0x70CC, 0x36C8);
		bus.WriteWord(0x7686, 0x3B5D);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x8000);
		cpu.State.A[1] = 0x70CD;
		cpu.State.A[6] = 0x7688;
		cpu.State.StatusRegister = 0x271B;

		cpu.ExecuteInstruction();

		Assert.Equal(0x70CCu, cpu.State.A[1]);
		Assert.Equal(0x7687u, cpu.State.A[6]);
		Assert.Equal(0x3B9A, bus.ReadWord(0x7686));
		Assert.Equal(0x2708, cpu.State.StatusRegister);
	}

	[Fact]
	public void NbcdPredecrementSetsOverflowFromDecimalCorrection()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x4821); // NBCD -(A1)
		bus.WriteWord(0x2100, 0x4E00);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2100u, cpu.State.A[1]);
		Assert.Equal(0x4B00, bus.ReadWord(0x2100));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry, cpu.State.StatusRegister);
	}
}
