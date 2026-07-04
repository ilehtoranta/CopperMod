using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kMoveTests
{
	[Fact]
	public void MoveToCcrPostincrementSourceAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x44DD); // MOVE (A5)+,CCR
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Supervisor |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[5]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x44D5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x44DD, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveToSrPostincrementSourceAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x46DD); // MOVE (A5)+,SR
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Supervisor |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[5]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x46D5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x46DD, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveImmediateToSrInUserModeRaisesPrivilegeViolation()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x46FC, 0x2700); // MOVE #$2700,SR
		bus.WriteLong(8 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.ResetStackPointers(0x5000, 0x3000, supervisorMode: false);
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FFAu, cpu.State.A[7]);
		Assert.Equal(0x4FFAu, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x3000u, cpu.State.UserStackPointer);
		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry), cpu.State.StatusRegister);
		Assert.Equal((ushort)(M68kCpuState.Trace | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry), bus.ReadWord(0x4FFA));
		Assert.Equal(0x1000u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveFromSrPredecrementDestinationAddressErrorUsesReadStatusWord()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x40E6); // MOVE SR,-(A6)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2103;
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Supervisor |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(0x2101u, cpu.State.A[6]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x40F5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x40E6, bus.ReadWord(0x4FF8));
		Assert.Equal((ushort)(M68kCpuState.Trace | M68kCpuState.Supervisor |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Carry), bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveFromSrPostincrementDestinationAddressErrorAdvancesAddressRegister()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x40DE); // MOVE SR,(A6)+
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Trace | M68kCpuState.Supervisor |
			M68kCpuState.Extend | M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2103u, cpu.State.A[6]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x40D5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x40DE, bus.ReadWord(0x4FF8));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveWordAddressRegisterToPostincrementDestinationAdvancesDestination()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x34C9); // MOVE.W A1,(A2)+

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0xCAFE_BEEF;
		cpu.State.A[2] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2102u, cpu.State.A[2]);
		Assert.Equal(0xBEEF, bus.ReadWord(0x2100));
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative, cpu.State.StatusRegister);
	}

	[Fact]
	public void MoveWordStackPostincrementToPredecrementDestinationAddressErrorUpdatesConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x371F, 0x5767); // MOVE.W (A7)+,-(A3)
		bus.WriteWord(0x5000, 0x5BEE);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2103;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | 0x0300 |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | 0x0300, cpu.State.StatusRegister);
		Assert.Equal(0x2101u, cpu.State.A[3]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF4u, cpu.State.A[7]);
		Assert.Equal(0x5765u, bus.ReadWord(0x4FF4));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF6));
		Assert.Equal(0x5767, bus.ReadWord(0x4FFA));
		Assert.Equal(M68kCpuState.Supervisor | 0x0300, bus.ReadWord(0x4FFC));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFE));
	}

	[Fact]
	public void MoveWordDataRegisterToAddressIndirectDestinationAddressErrorStacksPostfetchProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x3A83, 0xE9CD); // MOVE.W D3,(A5)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2101;
		cpu.State.D[3] = 0x1234;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x3A85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x3A83, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveWordStackPredecrementToIndexedDestinationAddressErrorClearsCarryAndPreservesOverflow()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x37A7, 0x0001); // MOVE.W -(A7),1(A3,D0.W)
		bus.WriteWord(0x3000, 0xDAA9);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x5000, userStackPointer: 0x3002, supervisorMode: false);
		cpu.State.A[3] = 0x2100;
		cpu.State.D[0] = 0;
		cpu.State.StatusRegister = 0x0200 | M68kCpuState.Negative | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)(M68kCpuState.Supervisor | 0x0200 | M68kCpuState.Negative), cpu.State.StatusRegister);
		Assert.Equal(0x3000u, cpu.State.UserStackPointer);
		Assert.Equal(0x4FF2u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveWordPostincrementSourceToIndexedDestinationAddressErrorAdvancesSource()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x3F9B, 0x0001); // MOVE.W (A3)+,1(A7,D0.W)
		bus.WriteWord(0x2000, 0x1234);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2000;
		cpu.State.D[0] = 0;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2002u, cpu.State.A[3]);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
	}

	[Fact]
	public void MoveWordMemoryToAbsoluteLongDestinationAddressErrorStacksSecondExtensionProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x33D1, 0x5B5C, 0x909F); // MOVE.W (A1),$5B5C909F.L
		bus.WriteWord(0x2000, 0x1AE6);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | 0x0700 |
			M68kCpuState.Negative | M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor | 0x0700, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x33C5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x5B5C_909Fu, bus.ReadLong(0x4FF4));
		Assert.Equal(0x33D1, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor | 0x0700, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveWordAddressRegisterToAbsoluteLongDestinationAddressErrorStacksPostAbsoluteProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x33CA, 0xBFA2, 0x01F5); // MOVE.W A2,$BFA201F5.L
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Extend | M68kCpuState.Negative |
			M68kCpuState.Zero | M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal((ushort)(M68kCpuState.Supervisor | M68kCpuState.Extend), cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x33C1u, bus.ReadWord(0x4FF2));
		Assert.Equal(0xBFA2_01F5u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x33CA, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Extend, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveWordDisplacementA6ToDataRegisterAddressErrorStacksDisplacementProgramCounter()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x3A2E, 0x1436); // MOVE.W $1436(A6),D5
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x0CCB;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | 0x0700 |
			M68kCpuState.Extend | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | 0x0700 |
			M68kCpuState.Extend | M68kCpuState.Zero;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x3A35u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x3A2E, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1002u, bus.ReadLong(0x4FFC));
	}

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
	public void MoveLongMemorySourceIndexedDestinationAddressErrorClearsOverflow()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2B90, 0x0001); // MOVE.L (A0),1(A5,D0.W)
		bus.WriteLong(0x2000, 0x19B4_8EAA);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[5] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2B85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2B90, bus.ReadWord(0x4FF8));
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
	public void MoveLongPostincrementSourceDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2D5B, 0x0001); // MOVE.L (A3)+,1(A6)
		bus.WriteLong(0x2000, 0x0377_9F11);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2000;
		cpu.State.A[6] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero | M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x2004u, cpu.State.A[3]);
		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2D45u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2D5B, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FFA));
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
	public void MoveLongPredecrementSourceDirectDestinationAddressErrorClearsZeroAndPreservesClearNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2AA3); // MOVE.L -(A3),(A5)
		bus.WriteLong(0x1FFC, 0xC240_3640);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2000;
		cpu.State.A[5] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend;
		Assert.Equal(0x1FFCu, cpu.State.A[3]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2AA5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2AA3, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPredecrementSourceDirectDestinationAddressErrorClearsZeroAndPreservesSetNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2AA0); // MOVE.L -(A0),(A5)
		bus.WriteLong(0x1FFC, 0xC672_8278);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[5] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Negative |
			M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative;
		Assert.Equal(0x1FFCu, cpu.State.A[0]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2AA5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2AA0, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPredecrementSourceA1DestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x22A2); // MOVE.L -(A2),(A1)
		bus.WriteLong(0x1FFC, 0x5F81_4205);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.A[2] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Negative |
			M68kCpuState.Zero | M68kCpuState.Overflow;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1FFCu, cpu.State.A[2]);
		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x22A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x22A2, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPredecrementSourceA0DestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x20A2); // MOVE.L -(A2),(A0)
		bus.WriteLong(0x1FFC, 0x1234_7A68);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2101;
		cpu.State.A[2] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Negative |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1FFCu, cpu.State.A[2]);
		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x20A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x20A2, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPredecrementSourceA0DestinationAddressErrorUpdatesNegativeZeroWithTraceState()
	{
		var bus = new WrappingMoveTestBus();
		bus.WriteWords(0x000F_CB52, 0x20A2); // MOVE.L -(A2),(A0)
		bus.WriteLong(0x372072, 0x5F81_4205);
		bus.WriteLong(3 * 4, 0x4CDE_143A);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x000F_CB52, 0x00FB_490C);
		cpu.State.A[0] = 0xEDC7_D993;
		cpu.State.A[2] = 0x0437_2076;
		cpu.State.StatusRegister = 0xA20C;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0437_2072u, cpu.State.A[2]);
		Assert.Equal(0x2200, cpu.State.StatusRegister);
		Assert.Equal(0x4CDE_143Au, cpu.State.ProgramCounter);
		Assert.Equal(0x00FB_48FEu, cpu.State.A[7]);
		Assert.Equal(0x20A5u, bus.ReadWord(0x00FB_48FE));
		Assert.Equal(0xEDC7_D993u, bus.ReadLong(0x00FB_4900));
		Assert.Equal(0x20A2, bus.ReadWord(0x00FB_4904));
		Assert.Equal(0xA200, bus.ReadWord(0x00FB_4906));
		Assert.Equal(0x000F_CB56u, bus.ReadLong(0x00FB_4908));
	}

	[Fact]
	public void MoveLongPredecrementSourceA6DestinationAddressErrorClearsNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2CA0); // MOVE.L -(A0),(A6)
		bus.WriteLong(0x1FFC, 0x6893_2C8C);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2000;
		cpu.State.A[6] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend;
		Assert.Equal(0x1FFCu, cpu.State.A[0]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2CA5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2CA0, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPredecrementSourceA6DestinationAddressErrorPreservesNegativeForOtherSources()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2CA3); // MOVE.L -(A3),(A6)
		bus.WriteLong(0x1FFC, 0xD8DD_C8DF);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2000;
		cpu.State.A[6] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(0x1FFCu, cpu.State.A[3]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2CA5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2CA3, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongStackPredecrementSourceDirectDestinationAddressErrorClearsZeroAndPreservesClearNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x26A7); // MOVE.L -(A7),(A3)
		bus.WriteLong(0x4FFC, 0xEF6B_CFB0);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FEEu, cpu.State.A[7]);
		Assert.Equal(0x26A5u, bus.ReadWord(0x4FEE));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF0));
		Assert.Equal(0x26A7, bus.ReadWord(0x4FF4));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FF6));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FF8));
	}

	[Fact]
	public void MoveLongStackPredecrementSourceDirectDestinationAddressErrorPreservesExtendOnlyStatus()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x28A7); // MOVE.L -(A7),(A4)
		bus.WriteLong(0x4FFC, 0xB401_4241);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[4] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FEEu, cpu.State.A[7]);
		Assert.Equal(0x28A5u, bus.ReadWord(0x4FEE));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF0));
		Assert.Equal(0x28A7, bus.ReadWord(0x4FF4));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FF6));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FF8));
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
	public void MoveLongImmediateDirectDestinationAddressErrorPreservesConditionCodes()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x22BC, 0xAB82, 0xC02F); // MOVE.L #$AB82C02F,(A1)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Carry;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x22A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x22BC, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1008u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongImmediateDisplacementDestinationAddressErrorUpdatesNegativeZeroAndPreservesOverflow()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x217C, 0x7B25, 0xD3AB, 0x0001); // MOVE.L #$7B25D3AB,1(A0)
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Negative | M68kCpuState.Zero | M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Overflow;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2165u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x217C, bus.ReadWord(0x4FF8));
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
	public void MoveLongAbsoluteLongSourceDirectDestinationAddressErrorClearsZeroAndPreservesNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x28B9, 0x0000, 0x2000); // MOVE.L ($2000).L,(A4)
		bus.WriteLong(0x2000, 0xA00C_3A2B);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[4] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x28A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x28B9, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1008u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAbsoluteLongSourceDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2379, 0x0000, 0x2000, 0x0001); // MOVE.L ($2000).L,1(A1)
		bus.WriteLong(0x2000, 0xA8B0_7F7B);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2365u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2379, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1008u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongAbsoluteWordSourcePostincrementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x24F8, 0x2000); // MOVE.L ($2000).W,(A2)+
		bus.WriteLong(0x2000, 0xDE5B_C808);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2101;
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
		Assert.Equal(0x24F8, bus.ReadWord(0x4FF8));
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
	public void MoveLongStackPostincrementSourceAbsoluteWordDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x21DF, 0x2101); // MOVE.L (A7)+,($2101).W
		bus.WriteLong(0x5000, 0x9B19_6F99);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF6u, cpu.State.A[7]);
		Assert.Equal(0x21C5u, bus.ReadWord(0x4FF6));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF8));
		Assert.Equal(0x21DF, bus.ReadWord(0x4FFC));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFE));
		Assert.Equal(0x1004u, bus.ReadLong(0x5000));
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
	public void MoveLongDisplacementSourceDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x216B, 0x0004, 0x0001); // MOVE.L 4(A3),1(A0)
		bus.WriteLong(0x2004, 0xC96E_7889);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[0] = 0x2100;
		cpu.State.A[3] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2165u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x216B, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
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
	public void MoveLongStackSourceDirectDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2697); // MOVE.L (A7),(A3)
		bus.WriteLong(0x5000, 0x7DCD_87EB);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2685u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2697, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongMemorySourceDirectDestinationAddressErrorClearsNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2C94); // MOVE.L (A4),(A6)
		bus.WriteLong(0x2000, 0x9234_5678);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[4] = 0x2000;
		cpu.State.A[6] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Negative |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2C85u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2C94, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongMemorySourceA2DestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2495); // MOVE.L (A5),(A2)
		bus.WriteLong(0x2000, 0x0317_CCED);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2101;
		cpu.State.A[5] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2485u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2495, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
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
	public void MoveLongPcDisplacementSourceDisplacementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2D7A, 0x0004, 0x0001); // MOVE.L 4(PC),1(A6)
		bus.WriteLong(0x1006, 0x99C8_843E);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2D65u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2D7A, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPcDisplacementSourceDirectDestinationAddressErrorClearsNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x22BA, 0x0004); // MOVE.L 4(PC),(A1)
		bus.WriteLong(0x1006, 0x92FF_20F1);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[1] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Negative |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x22A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x22BA, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1006u, bus.ReadLong(0x4FFC));
	}

	[Fact]
	public void MoveLongPcDisplacementSourceA6DestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2CBA, 0x0004); // MOVE.L 4(PC),(A6)
		bus.WriteLong(0x1006, 0xC226_EE41);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[6] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2CA5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2CBA, bus.ReadWord(0x4FF8));
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
	public void MoveLongPcIndexedSourceDirectDestinationAddressErrorClearsNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x26BB, 0x0004); // MOVE.L 4(PC,D0.W),(A3)
		bus.WriteLong(0x1006, 0x2F95_0191);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[3] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Negative |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(M68kCpuState.Supervisor, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x26A5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x26BB, bus.ReadWord(0x4FF8));
		Assert.Equal(M68kCpuState.Supervisor, bus.ReadWord(0x4FFA));
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
	public void MoveLongPredecrementSourcePostincrementDestinationAddressErrorUsesLowWordNegative()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2AE6); // MOVE.L -(A6),(A5)+
		bus.WriteLong(0x1FFC, 0x43B4_8C76);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[5] = 0x2101;
		cpu.State.A[6] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Zero | M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative;
		Assert.Equal(0x2101u, cpu.State.A[5]);
		Assert.Equal(0x1FFCu, cpu.State.A[6]);
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2AE5u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2AE6, bus.ReadWord(0x4FF8));
		Assert.Equal(expectedStatus, bus.ReadWord(0x4FFA));
		Assert.Equal(0x1004u, bus.ReadLong(0x4FFC));
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
	public void MoveLongIndexedSourceDisplacementDestinationAddressErrorUpdatesNegativeZero()
	{
		var bus = new Copper68kTestBus();
		bus.WriteWords(0x1000, 0x2772, 0x0000, 0x0001); // MOVE.L 0(A2,D0.W),1(A3)
		bus.WriteLong(0x2000, 0xF06A_410F);
		bus.WriteLong(3 * 4, 0x4000);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);
		cpu.State.A[2] = 0x2000;
		cpu.State.A[3] = 0x2100;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero |
			M68kCpuState.Overflow | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		var expectedStatus = M68kCpuState.Supervisor | M68kCpuState.Negative;
		Assert.Equal(expectedStatus, cpu.State.StatusRegister);
		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF2u, cpu.State.A[7]);
		Assert.Equal(0x2765u, bus.ReadWord(0x4FF2));
		Assert.Equal(0x2101u, bus.ReadLong(0x4FF4));
		Assert.Equal(0x2772, bus.ReadWord(0x4FF8));
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

	private sealed class WrappingMoveTestBus : IM68kBus, IM68kCodeReader
	{
		private readonly byte[] _memory = new byte[0x0100_0000];

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return _memory[Offset(address)];
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadWord(address);
		}

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			return ReadLong(address);
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			_memory[Offset(address)] = value;
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			WriteWord(address, value);
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			WriteWord(address, (ushort)(value >> 16), ref cycle, accessKind);
			WriteWord(address + 2, (ushort)value, ref cycle, accessKind);
		}

		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}

		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}

		public void ResetExternalDevices(long cycle)
		{
			_ = cycle;
		}

		public ushort ReadHostWord(uint address)
			=> ReadWord(address);

		public ushort ReadWord(uint address)
			=> (ushort)((_memory[Offset(address)] << 8) | _memory[Offset(address + 1)]);

		public uint ReadLong(uint address)
			=> ((uint)ReadWord(address) << 16) | ReadWord(address + 2);

		public void WriteWords(uint address, params ushort[] values)
		{
			foreach (var value in values)
			{
				WriteWord(address, value);
				address += 2;
			}
		}

		public void WriteWord(uint address, ushort value)
		{
			_memory[Offset(address)] = (byte)(value >> 8);
			_memory[Offset(address + 1)] = (byte)value;
		}

		public void WriteLong(uint address, uint value)
		{
			WriteWord(address, (ushort)(value >> 16));
			WriteWord(address + 2, (ushort)value);
		}

		private static int Offset(uint address)
			=> (int)(address & 0x00FF_FFFF);
	}
}
