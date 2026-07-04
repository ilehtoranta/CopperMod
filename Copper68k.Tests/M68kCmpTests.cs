using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kCmpTests
{
	[Fact]
	public void CmpiBytePostincrementA7AdvancesStackPointerByWord()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x0C1F, 0x0012); // CMPI.B #$12,(A7)+
		bus.Memory[0x2000] = 0x34;

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2002u, cpu.State.A[7]);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		Assert.Equal(0x34, bus.Memory[0x2000]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void CmpmLongOddPostincrementSourceAdvancesSourceByWordBeforeAddressError()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xB38C); // CMPM.L (A4)+,(A1)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[1] = 0x2100;
		cpu.State.A[4] = 0x2001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2003u, cpu.State.A[4]);
		Assert.Equal(0x2100u, cpu.State.A[1]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0xB395u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2001u, bus.ReadLong(0x2FF4));
		Assert.Equal(0xB38C, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void CmpmWordOddPostincrementSourceAdvancesSourceByWordBeforeAddressError()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xB14C); // CMPM.W (A4)+,(A0)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2100;
		cpu.State.A[4] = 0x2001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2003u, cpu.State.A[4]);
		Assert.Equal(0x2100u, cpu.State.A[0]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0xB155u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2001u, bus.ReadLong(0x2FF4));
		Assert.Equal(0xB14C, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void CmpLongOddPostincrementSourceDoesNotAdvanceBeforeAddressError()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xB49D); // CMP.L (A5)+,D2
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[5] = 0x2001;
		cpu.State.D[2] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2001u, cpu.State.A[5]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0xB495u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2001u, bus.ReadLong(0x2FF4));
		Assert.Equal(0xB49D, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void CmpmLongOddPostincrementDestinationDoesNotAdvanceBeforeAddressError()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xB58C); // CMPM.L (A4)+,(A2)+
		bus.WriteLong(3 * 4, 0x4000);
		bus.WriteLong(0x2000, 0x1234_5678);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[2] = 0x2101;
		cpu.State.A[4] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2004u, cpu.State.A[4]);
		Assert.Equal(0x2101u, cpu.State.A[2]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0xB595u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x2FF4));
		Assert.Equal(0xB58C, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x2FFC));
	}
}
