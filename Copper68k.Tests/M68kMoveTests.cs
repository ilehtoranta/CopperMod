using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kMoveTests
{
	[Fact]
	public void MoveLongDestinationAddressErrorKeepsSourceConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x28AD, 0x0004); // MOVE.L 4(A5),(A4)
		bus.WriteLong(0x2004, 0x9234_5678);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[4] = 0x2101;
		cpu.State.A[5] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x28A5u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x2FF4));
		Assert.Equal(0x28AD, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void MoveLongStackDisplacementDestinationAddressErrorClearsZeroBeforeNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x20AF, 0xFFE0); // MOVE.L -32(A7),(A0)
		bus.WriteLong(0x4FE0, 0xEC21_09E5);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2101u, cpu.State.A[0]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x20A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x20AF, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongStackDisplacementSuccessfulWriteUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x24AF, 0xFFE0); // MOVE.L -32(A7),(A2)
		bus.WriteLong(0x4FE0, 0xB2CE_8647);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative, cpu.State.StatusRegister);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		Assert.Equal(0xB2CE_8647u, bus.ReadLong(0x2100));
	}

	[Fact]
	public void MoveLongStackIndexedDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2F90, 0x0001); // MOVE.L (A0),1(A7,D0.W)
		bus.WriteLong(0x2000, 0x3124_4FFE);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2F85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x5001u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2F90, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongIndexedDestinationAddressErrorPreservesOverflowUntilWrite()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2387, 0x0001); // MOVE.L D7,1(A1,D0.W)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2100;
		cpu.State.D[7] = 0x5A48_CA3C;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2385u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2387, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongIndexedDestinationAddressErrorPreservesCarryUntilWrite()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2B84, 0x0001); // MOVE.L D4,1(A5,D0.W)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2100;
		cpu.State.D[4] = 0xC685_D9E8;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2B85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2B84, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDataRegisterStackIndexedDestinationAddressErrorPreservesCarryUntilWrite()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2F82, 0x0001); // MOVE.L D2,1(A7,D0.W)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.D[2] = 0x9746_E023;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2F85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x5001u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2F82, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAddressRegisterStackIndexedDestinationAddressErrorPreservesCarryUntilWrite()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2F8E, 0x0001); // MOVE.L A6,1(A7,D0.W)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x9DEB_E7C8;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2F85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x5001u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2F8E, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPostincrementSourceIndexedDestinationAddressErrorClearsOverflowCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2D9B, 0x0001); // MOVE.L (A3)+,1(A6,D0.W)
		bus.WriteLong(0x2000, 0x0C70_A3A3);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2000;
		cpu.State.A[6] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2004u, cpu.State.A[3]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2D85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2D9B, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPredecrementSourceDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2567, 0x0001); // MOVE.L -(A7),1(A2)
		bus.WriteLong(0x4FFC, 0x13B5_EA89);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FEEu, cpu.State.A[7]);
		Assert.Equal(0x2565u, bus.ReadWord(0x4FEE));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF0));
		Assert.Equal(0x2567, bus.ReadWord(0x4FF4));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FF6));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FF8));
	}

	[Fact]
	public void MoveLongPredecrementSourceIndexedDestinationAddressErrorClearsOverflowCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x25A0, 0x0001); // MOVE.L -(A0),1(A2,D0.W)
		bus.WriteLong(0x1FFC, 0x1234_5678);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[2] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1FFCu, cpu.State.A[0]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x25A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x25A0, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongImmediatePostincrementDestinationAddressErrorPreservesConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x26FC, 0x0802, 0x8AAB); // MOVE.L #$08028AAB,(A3)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Carry;
		Assert.Equal(0x2101u, cpu.State.A[3]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x26E5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x26FC, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1008u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAbsoluteDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x21F8, 0x2000, 0x2101); // MOVE.L ($2000).W,($2101).W
		bus.WriteLong(0x2000, 0x115C_3914);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x21E5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x21F8, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAbsoluteLongDestinationAddressErrorStacksSecondExtensionPc()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x23EE, 0x0004, 0x0000, 0x2101); // MOVE.L 4(A6),($2101).L
		bus.WriteLong(0x2004, 0xA915_B8AD);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x23E5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x23EE, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPostincrementSourceAbsoluteLongDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x23DD, 0x0000, 0x2101); // MOVE.L (A5)+,($2101).L
		bus.WriteLong(0x2000, 0x1122_8899);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(0x2004u, cpu.State.A[5]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x23C5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x23DD, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDataRegisterDestinationAddressErrorPreservesConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2283); // MOVE.L D3,(A1)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.D[3] = 0x5CF2_BEEE;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2285u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2283, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDataRegisterDisplacementDestinationAddressErrorPreservesConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2947, 0x0001); // MOVE.L D7,1(A4)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[4] = 0x2100;
		cpu.State.D[7] = 0xED74_2064;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2945u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2947, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDataRegisterDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2D44, 0x0001); // MOVE.L D4,1(A6)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2100;
		cpu.State.D[4] = 0x8816_1A6E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2D45u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2D44, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAddressRegisterDestinationAddressErrorPreservesConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x228D); // MOVE.L A5,(A1)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.A[5] = 0xE217_F41E;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2285u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x228D, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAddressRegisterDisplacementDestinationAddressErrorPreservesOverflowUntilWrite()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2549, 0x0001); // MOVE.L A1,1(A2)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0xBD54_4401;
		cpu.State.A[2] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2545u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2549, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAddressRegisterDisplacementDestinationAddressErrorPreservesCarryUntilWrite()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x254F, 0x0001); // MOVE.L A7,1(A2)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2545u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x254F, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDataRegisterPostincrementDestinationAddressErrorPreservesConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x24C5); // MOVE.L D5,(A2)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2101;
		cpu.State.D[5] = 0x0A8B_FCEF;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Carry;
		Assert.Equal(0x2101u, cpu.State.A[2]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x24C5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x24C5, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDisplacementDestinationAddressErrorStacksPostExtensionPc()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2B6A, 0x0004, 0x0001); // MOVE.L 4(A2),1(A5)
		bus.WriteLong(0x2004, 0x2838_DFA8);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2000;
		cpu.State.A[5] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2B65u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2B6A, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongMemorySourceDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2B53, 0x0001); // MOVE.L (A3),1(A5)
		bus.WriteLong(0x2000, 0x6EF5_DE26);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2000;
		cpu.State.A[5] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2B45u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2B53, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDisplacementSourceDirectDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x22AB, 0x0004); // MOVE.L 4(A3),(A1)
		bus.WriteLong(0x2004, 0xA441_A8FF);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.A[3] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x22A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x22AB, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongIndexedSourcePostincrementDestinationAddressErrorClearsConditionCodesBeforeNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x28F1, 0x0000); // MOVE.L 0(A1,D0.W),(A4)+
		bus.WriteLong(0x2000, 0xFD65_30A9);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2000;
		cpu.State.A[4] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2101u, cpu.State.A[4]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x28E5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x28F1, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongIndexedSourcePostincrementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x26F1, 0x0000); // MOVE.L 0(A1,D0.W),(A3)+
		bus.WriteLong(0x2000, 0xF1C5_F78C);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2000;
		cpu.State.A[3] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative;
		Assert.Equal(0x2101u, cpu.State.A[3]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x26E5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x26F1, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPcDisplacementSourcePostincrementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2CFA, 0x0004); // MOVE.L 4(PC),(A6)+
		bus.WriteLong(0x1006, 0x6BA0_BBA4);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(0x2101u, cpu.State.A[6]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2CE5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2CFA, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPcIndexedSourceDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x217B, 0x0004, 0x0001); // MOVE.L 4(PC,D0.W),1(A0)
		bus.WriteLong(0x1006, 0x1B36_64E4);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2165u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x217B, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPcIndexedSourceIndexedDestinationAddressErrorClearsOverflowCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2BBB, 0x0004, 0x0001); // MOVE.L 4(PC,D0.W),1(A5,D0.W)
		bus.WriteLong(0x1006, 0x1234_5678);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2BA5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2BBB, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongMemorySourcePostincrementDestinationAddressErrorClearsConditionCodesBeforeNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x22D5); // MOVE.L (A5),(A1)+
		bus.WriteLong(0x2000, 0xABB8_4FAF);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.A[5] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2101u, cpu.State.A[1]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x22C5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x22D5, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongMemorySourcePostincrementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x26D5); // MOVE.L (A5),(A3)+
		bus.WriteLong(0x2000, 0x7BE2_9DDF);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2101;
		cpu.State.A[5] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative;
		Assert.Equal(0x2101u, cpu.State.A[3]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x26C5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x26D5, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDisplacementSourcePostincrementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x24EC, 0x0004); // MOVE.L 4(A4),(A2)+
		bus.WriteLong(0x2004, 0x9855_953B);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2101;
		cpu.State.A[4] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(0x2101u, cpu.State.A[2]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x24E5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x24EC, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongIndexedSourceDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x24B0, 0x0000); // MOVE.L 0(A0,D0.W),(A2)
		bus.WriteLong(0x2000, 0x1740_B265);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x24A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x24B0, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongIndexedSourceIndexedDestinationAddressErrorClearsOverflowCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x29B2, 0x0000, 0x0001); // MOVE.L 0(A2,D0.W),1(A4,D0.W)
		bus.WriteLong(0x2000, 0x9F99_C80F);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2000;
		cpu.State.A[4] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x29A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x29B2, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongDisplacementSourceIndexedDestinationAddressErrorClearsCarry()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x29AA, 0x0004, 0x0001); // MOVE.L 4(A2),1(A4,D0.W)
		bus.WriteLong(0x2004, 0x2A3F_6941);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2000;
		cpu.State.A[4] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x29A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x29AA, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPredecrementDestinationAddressErrorLeavesAddressRegisterUnchanged()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2936, 0x0000); // MOVE.L (d8,A6,D0.W),-(A4)
		bus.WriteLong(0x2000, 0x1234_5678);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[4] = 0x2103;
		cpu.State.A[6] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[4]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x2925u, bus.ReadWord(0x2FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x2FF4));
		Assert.Equal(0x2936, bus.ReadWord(0x2FF8));
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, bus.ReadWord(0x2FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void MoveLongPostincrementSourceDestinationAddressErrorKeepsSourceFlags()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2298); // MOVE.L (A0)+,(A1)
		bus.WriteLong(0x2000, 0x9234_8878);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[1] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2004u, cpu.State.A[0]);
		Assert.Equal(0x2101u, cpu.State.A[1]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void MoveLongPostincrementSourceDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2298); // MOVE.L (A0)+,(A1)
		bus.WriteLong(0x2000, 0xCB3C_0BB3);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[1] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2004u, cpu.State.A[0]);
		Assert.Equal(0x2101u, cpu.State.A[1]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void MoveLongPostincrementSourcePostincrementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2ADC); // MOVE.L (A4)+,(A5)+
		bus.WriteLong(0x2000, 0x1122_8899);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[4] = 0x2000;
		cpu.State.A[5] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2004u, cpu.State.A[4]);
		Assert.Equal(0x2101u, cpu.State.A[5]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Negative, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
	}
}
