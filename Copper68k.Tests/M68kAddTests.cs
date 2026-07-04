using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kAddTests
{
	[Fact]
	public void AddLongDataRegisterToIndexedDestinationAddressErrorStacksExtensionProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xDBB3, 0x0001); // ADD.L D5,1(A3,D0.W)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2100;
		cpu.State.D[5] = 0x014F_BCC8;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0xDBB5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0xDBB3, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void AddLongDataRegisterToDisplacementDestinationAddressErrorStacksExtensionProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xD9A8, 0x0001); // ADD.L D4,1(A0)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2100;
		cpu.State.D[4] = 0x014F_BCC8;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0xD9B5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0xD9A8, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void AddqLongToDisplacementDestinationAddressErrorStacksExtensionProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x5EAA, 0x0001); // ADDQ.L #7,1(A2)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x5EB5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x5EAA, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void AddqLongToPredecrementDestinationAddressErrorKeepsDecrementedAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x5EA6); // ADDQ.L #7,-(A6)
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
		Assert.Equal(0x5EB5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x5EA6, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void AddiLongToPredecrementDestinationAddressErrorKeepsDecrementedAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x06A6, 0x1111, 0x1111); // ADDI.L #$11111111,-(A6)
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
		Assert.Equal(0x06B5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x06A6, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void AddiLongToDisplacementDestinationAddressErrorStacksExtensionProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x06A8, 0x1111, 0x1111, 0x0001); // ADDI.L #$11111111,1(A0)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x06B5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x06A8, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void AddqWordToPostincrementDestinationAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x5458); // ADDQ.W #2,(A0)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[0]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}

	[Fact]
	public void AddiWordToPostincrementDestinationAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x065C, 0x1234); // ADDI.W #$1234,(A4)+
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
	public void AddWordDataRegisterToPostincrementDestinationAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xD55E); // ADD.W D2,(A6)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2101;
		cpu.State.D[2] = 0x1234;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[6]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0xD555u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0xD55E, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void AddWordPostincrementSourceAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xDA5B); // ADD.W (A3)+,D5
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2101;
		cpu.State.D[5] = 0x1234;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[3]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}

	[Fact]
	public void AddaWordPostincrementSourceAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xDEDA); // ADDA.W (A2)+,A7
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[2]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}

	[Fact]
	public void AddLongDataRegisterToPredecrementDestinationAddressErrorKeepsDecrementedAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0xD1A6); // ADD.L D0,-(A6)
		bus.WriteLong(3 * 4, 0x4000);
		bus.WriteLong(0x2101, 0x1234_5678);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2105;
		cpu.State.D[0] = 0x1111_1111;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2101u, cpu.State.A[6]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0xD1B5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0xD1A6, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}
}
