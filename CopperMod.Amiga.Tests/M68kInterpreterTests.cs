using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class M68kInterpreterTests
{
	[Fact]
	public void MoveqAddqAndDbraUseDocumentedControlFlow()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x70, 0x00); // MOVEQ #0,D0
		Write(bus.Memory, 0x1002, 0x72, 0x02); // MOVEQ #2,D1
		Write(bus.Memory, 0x1004, 0x52, 0x80); // ADDQ.L #1,D0
		Write(bus.Memory, 0x1006, 0x51, 0xC9, 0xFF, 0xFC); // DBRA D1,-4
		Write(bus.Memory, 0x100A, 0x4E, 0x75); // RTS
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		bus.WriteLong(0x1FFC, 0xFFFF_FFFC);
		cpu.State.A[7] = 0x1FFC;

		for (var i = 0; i < 16 && cpu.State.ProgramCounter != 0xFFFF_FFFC; i++)
		{
			cpu.ExecuteInstruction();
		}

		Assert.Equal(0x0000_0003u, cpu.State.D[0]);
		Assert.True(cpu.State.Cycles > 0);
	}

	[Fact]
	public void NopUsesDocumentedCyclesWithoutAddingFetchTime()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x4E, 0x71); // NOP
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(4, cycles);
		Assert.Equal(24, cpu.State.Cycles);
	}

	[Fact]
	public void DbraTakenUsesDocumentedCyclesWithoutAddingFetchTime()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,*-2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 1;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(10, cycles);
		Assert.Equal(30, cpu.State.Cycles);
		Assert.Equal(0u, cpu.State.D[0] & 0xFFFF);
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void MoveWordDisplacementSourceToDataRegisterUsesDocumentedCycles()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x30, 0x28, 0x00, 0x02); // MOVE.W 2(A0),D0
		Write(bus.ChipRam, 0x2002, 0x12, 0x34);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.Cycles = 20;
		cpu.State.A[0] = 0x2000;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(12, cycles);
		Assert.Equal(0x1234u, cpu.State.D[0] & 0xFFFF);
	}

	[Fact]
	public void ImmediateBtstDataRegisterUsesDocumentedCyclesWithoutAddingExtensionFetchTime()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x08, 0x00, 0x00, 0x0E); // BTST #14,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 0x4000;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(10, cycles);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void MuluImmediateUsesSourceBitCountTiming()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0xC0, 0xFC, 0x55, 0x55); // MULU.W #$5555,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 3;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(58, cycles);
		Assert.Equal(0x0000_FFFFu, cpu.State.D[0]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void MuluRegisterSourceUsesSourceBitCountTimingWithoutEaExtension()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0xC0, 0xC1); // MULU.W D1,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 3;
		cpu.State.D[1] = 0x5555;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(54, cycles);
		Assert.Equal(0x0000_FFFFu, cpu.State.D[0]);
	}

	[Fact]
	public void MuluImmediateZeroSourceUsesMinimumTiming()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0xC0, 0xFC, 0x00, 0x00); // MULU.W #0,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.Cycles = 20;
		cpu.State.D[0] = 0x1234;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(42, cycles);
		Assert.Equal(0u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void WaitBlitPollingLoopUsesDocumentedCpuCadence()
	{
		var bus = new CycleCountingBus();
		Write(bus.Memory, 0x1000, 0x70, 0x04); // MOVEQ #4,D0
		Write(bus.Memory, 0x1002, 0x51, 0xC8, 0xFF, 0xFE); // DBRA D0,.wpre
		Write(bus.Memory, 0x1006, 0x30, 0x2E, 0x00, 0x02); // MOVE.W 2(A6),D0
		Write(bus.Memory, 0x100A, 0x08, 0x00, 0x00, 0x0E); // BTST #14,D0
		Write(bus.Memory, 0x100E, 0x66, 0xF6); // BNE .wbusy
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[6] = 0x00DFF000;
		bus.WriteWordRaw(0x00DFF002, 0x4000);

		for (var i = 0; i < 64 && bus.DataReadCycles.Count(read => (read.Address & 0x00FF_FFFE) == 0x00DFF002) < 2; i++)
		{
			cpu.ExecuteInstruction();
		}

		var dmaconrReadCycles = bus.DataReadCycles
			.Where(read => (read.Address & 0x00FF_FFFE) == 0x00DFF002)
			.Select(read => read.Cycle)
			.ToArray();
		Assert.True(
			dmaconrReadCycles.Length >= 2,
			"Expected at least two DMACONR reads; data reads were " +
			string.Join(", ", bus.DataReadCycles.Select(read => $"0x{read.Address:X8}@{read.Cycle}")));
		Assert.Equal(62, dmaconrReadCycles[0]);
		Assert.Equal(32, dmaconrReadCycles[1] - dmaconrReadCycles[0]);
	}

	[Fact]
	public void MoveaDoesNotAlterConditionCodes()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x20, 0x40); // MOVEA.L D0,A0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.StatusRegister = M68kCpuState.Supervisor |
			M68kCpuState.Extend |
			M68kCpuState.Negative |
			M68kCpuState.Zero |
			M68kCpuState.Overflow |
			M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void MovemPredecrementAndPostincrementRoundTripRegisters()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0xE7, 0xC0, 0xC0); // MOVEM.L D0-D1/A0-A1,-(A7)
		Write(bus.Memory, 0x1004, 0x4C, 0xDF, 0x03, 0x03); // MOVEM.L (A7)+,D0-D1/A0-A1
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1111_2222;
		cpu.State.D[1] = 0x3333_4444;
		cpu.State.A[0] = 0x5555_6666;
		cpu.State.A[1] = 0x7777_8888;

		cpu.ExecuteInstruction();
		cpu.State.D[0] = 0;
		cpu.State.D[1] = 0;
		cpu.State.A[0] = 0;
		cpu.State.A[1] = 0;
		cpu.ExecuteInstruction();

		Assert.Equal(0x1111_2222u, cpu.State.D[0]);
		Assert.Equal(0x3333_4444u, cpu.State.D[1]);
		Assert.Equal(0x5555_6666u, cpu.State.A[0]);
		Assert.Equal(0x7777_8888u, cpu.State.A[1]);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}

	[Fact]
	public void MovemDiagRomSaveRestoreMaskPreservesReturnAddress()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0xE7, 0x7C, 0x40); // MOVEM.L D1-D5/A1,-(A7)
		Write(bus.Memory, 0x1004, 0x4C, 0xDF, 0x02, 0x3E); // MOVEM.L (A7)+,D1-D5/A1
		Write(bus.Memory, 0x1008, 0x4E, 0x75); // RTS
		bus.WriteLong(0x2FFC, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[7] = 0x2FFC;
		cpu.State.D[1] = 0x1111_1111;
		cpu.State.D[2] = 0x2222_2222;
		cpu.State.D[3] = 0x3333_3333;
		cpu.State.D[4] = 0x4444_4444;
		cpu.State.D[5] = 0x5555_5555;
		cpu.State.A[1] = 0xAAAA_AAAA;

		cpu.ExecuteInstruction();
		Assert.Equal(0x2FE4u, cpu.State.A[7]);
		Assert.Equal(0x1111_1111u, ReadLong(bus.Memory, 0x2FE4));
		Assert.Equal(0x2222_2222u, ReadLong(bus.Memory, 0x2FE8));
		Assert.Equal(0x3333_3333u, ReadLong(bus.Memory, 0x2FEC));
		Assert.Equal(0x4444_4444u, ReadLong(bus.Memory, 0x2FF0));
		Assert.Equal(0x5555_5555u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0xAAAA_AAAAu, ReadLong(bus.Memory, 0x2FF8));
		Assert.Equal(0x0000_4000u, ReadLong(bus.Memory, 0x2FFC));

		cpu.State.D[1] = 0;
		cpu.State.D[2] = 0;
		cpu.State.D[3] = 0;
		cpu.State.D[4] = 0;
		cpu.State.D[5] = 0;
		cpu.State.A[1] = 0;
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(0x1111_1111u, cpu.State.D[1]);
		Assert.Equal(0x2222_2222u, cpu.State.D[2]);
		Assert.Equal(0x3333_3333u, cpu.State.D[3]);
		Assert.Equal(0x4444_4444u, cpu.State.D[4]);
		Assert.Equal(0x5555_5555u, cpu.State.D[5]);
		Assert.Equal(0xAAAA_AAAAu, cpu.State.A[1]);
	}

	[Fact]
	public void MoveUspUsesCorrectSupervisorDirection()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x60); // MOVE A0,USP
		Write(bus.Memory, 0x1002, 0x4E, 0x69); // MOVE USP,A1
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x1234_5678;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5678u, cpu.State.UserStackPointer);
		Assert.Equal(0x1234_5678u, cpu.State.A[1]);
		Assert.Equal(0x3000u, cpu.State.A[7]);
	}

	[Fact]
	public void ExgAddressRegistersSwapsFullLongValues()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xC5, 0x4E); // EXG A2,A6
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[2] = 0x0000_0040;
		cpu.State.A[6] = 0x00C0_0276;

		cpu.ExecuteInstruction();

		Assert.Equal(0x00C0_0276u, cpu.State.A[2]);
		Assert.Equal(0x0000_0040u, cpu.State.A[6]);
	}

	[Fact]
	public void AbcdConsumesExtendFromBinaryShiftForBcdAccumulation()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xD1, 0x80); // ADD.L D0,D0
		Write(bus.Memory, 0x1002, 0xC3, 0x01); // ABCD D1,D1
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x8000_0000;
		cpu.State.D[1] = 0x0000_0000;

		cpu.ExecuteInstruction();
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0001u, cpu.State.D[1]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void AbcdDataRegisterUsesStickyZeroAndDecimalCarry()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xC5, 0x01); // ABCD D1,D2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[1] = 0x49;
		cpu.State.D[2] = 0x50;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x00u, cpu.State.D[2] & 0xFF);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void SbcdDataRegisterSubtractsPackedDecimalWithExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x85, 0x01); // SBCD D1,D2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[1] = 0x01;
		cpu.State.D[2] = 0x20;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		cpu.ExecuteInstruction();

		Assert.Equal(0x18u, cpu.State.D[2] & 0xFF);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void AbcdPredecrementMemoryAddsPackedDecimalAndUpdatesAddresses()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xC5, 0x09); // ABCD -(A1),-(A2)
		Write(bus.Memory, 0x2000, 0x49);
		Write(bus.Memory, 0x2100, 0x50);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[1] = 0x2001;
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0x2100u, cpu.State.A[2]);
		Assert.Equal(0x00, bus.Memory[0x2100]);
		Assert.Equal(18, cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void SbcdPredecrementMemorySubtractsPackedDecimalWithExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x85, 0x09); // SBCD -(A1),-(A2)
		Write(bus.Memory, 0x2000, 0x01);
		Write(bus.Memory, 0x2100, 0x20);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[1] = 0x2001;
		cpu.State.A[2] = 0x2101;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0x2100u, cpu.State.A[2]);
		Assert.Equal(0x18, bus.Memory[0x2100]);
		Assert.Equal(18, cycles);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void NbcdDataRegisterNegatesPackedDecimalWithExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0x02); // NBCD D2
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[2] = 0x1234_5601;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5698u, cpu.State.D[2]);
		Assert.Equal(6, cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
	}

	[Fact]
	public void NbcdPostIncrementMemoryNegatesPackedDecimalAndAdvancesAddress()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x48, 0x18); // NBCD (A0)+
		Write(bus.Memory, 0x2000, 0x20);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(0x2001u, cpu.State.A[0]);
		Assert.Equal(0x80, bus.Memory[0x2000]);
		Assert.Equal(8, cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
	}

	[Fact]
	public void MovecRaisesIllegalInstructionExceptionOnM68000()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x7B); // MOVEC on a 68000 raises illegal instruction
		bus.WriteLong(0x0010, 0x2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(BigEndian.ReadUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}

	[Fact]
	public void IllegalInstructionVectorsThroughIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4A, 0xFC); // ILLEGAL
		bus.WriteLong(0x0010, 0x2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(BigEndian.ReadUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}

	[Fact]
	public void LineAAndLineFOpcodesVectorThroughEmulatorExceptions()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xA0, 0x00); // Line-A emulator exception
		Write(bus.Memory, 0x2000, 0xF0, 0x00); // Line-F emulator exception
		bus.WriteLong(10 * 4, 0x3000);
		bus.WriteLong(11 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x3000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x4FFC, "line-A stacked program counter"));

		cpu.State.ProgramCounter = 0x2000;
		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FF4u, cpu.State.A[7]);
		Assert.Equal(0x2000u, BigEndian.ReadUInt32(bus.Memory, 0x4FF6, "line-F stacked program counter"));
	}

	[Fact]
	public void RegisteredLineFHostTrapStubInvokesCallbackAndReturns()
	{
		var bus = new AmigaBus();
		var trapAddress = 0x00F0_0000u;
		var callbackCalled = false;
		bus.RegisterHostTrapStub(trapAddress, state =>
		{
			callbackCalled = true;
			state.D[0] = 0x1234_5678;
		});
		bus.WriteWord(0x1000, 0x4EB9); // JSR absolute long
		bus.WriteLong(0x1002, trapAddress);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.True(callbackCalled);
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(0xFF00, bus.ReadWord(trapAddress));
		Assert.NotEqual(0, bus.ReadWord(trapAddress + 2));
	}

	[Fact]
	public void RegisteredLineFHostTrapDoesNotReturnWhenCallbackChangesProgramCounter()
	{
		var bus = new AmigaBus();
		var trapAddress = 0x00F0_0000u;
		bus.RegisterHostTrapStub(trapAddress, state => state.ProgramCounter = 0x2000);
		bus.WriteWord(0x1000, 0x4EB9); // JSR absolute long
		bus.WriteLong(0x1002, trapAddress);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFCu, cpu.State.A[7]);
		Assert.Equal(0x1006u, bus.ReadLong(0x2FFC));
	}

	[Fact]
	public void UnregisteredLineFHostTrapOpcodeRaisesRealLineFException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xFF, 0x00, 0x12, 0x34);
		bus.WriteLong(11 * 4, 0x4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x5000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x4FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x4FFC, "line-F stacked program counter"));
		Assert.DoesNotContain(bus.Accesses, access => access.Address == 0x1002 && access.Kind == M68kBusAccessKind.CpuInstructionFetch);
	}

	[Fact]
	public void ResetInstructionSignalsExternalDevices()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x70); // RESET
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(1, bus.ExternalResetCount);
		Assert.Equal(0x1002u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void StopInstructionWaitsUntilAcceptedInterrupt()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x72, 0x20, 0x00); // STOP #$2000
		bus.WriteLong(0x0070, 0x2000); // level 4 autovector
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		var stoppedCycle = cpu.State.Cycles;
		cpu.ExecuteInstruction();

		Assert.True(cpu.State.Stopped);
		Assert.Equal(0x1004u, cpu.State.ProgramCounter);
		Assert.Equal(stoppedCycle + 1, cpu.State.Cycles);

		cpu.RequestInterrupt(4, 0x70);

		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1004u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}

	[Fact]
	public void StopInstructionRaisesPrivilegeViolationInUserMode()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x72, 0x20, 0x00); // STOP #$2000
		bus.WriteLong(0x0020, 0x2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x3000, userStackPointer: 0x4000, supervisorMode: false);

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.Stopped);
		Assert.Equal(0x2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x1000u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
	}

	[Fact]
	public void TimedWritesReachBusInProgramOrder()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x33, 0xFC, 0x80, 0x0F, 0x00, 0xDF, 0xF0, 0x96); // MOVE.W #$800F,$DFF096
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);

		cpu.ExecuteInstruction();

		Assert.True(bus.Writes.Count >= 2);
		Assert.Equal((uint)0x00DFF096, bus.Writes[^2].Address);
		Assert.Equal((uint)0x00DFF097, bus.Writes[^1].Address);
		Assert.True(bus.Writes[^1].Cycle >= bus.Writes[^2].Cycle);
	}

	[Fact]
	public void JsrPcRelativeUsesExtensionWordAsBase()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0xBA, 0x00, 0x08); // JSR 8(PC), target 0x100A
		Write(bus.Memory, 0x1004, 0x4E, 0x75); // RTS to sentinel after subroutine returns
		Write(bus.Memory, 0x100A, 0x70, 0x7F); // MOVEQ #$7F,D0
		Write(bus.Memory, 0x100C, 0x4E, 0x75); // RTS
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		bus.WriteLong(0x1FFC, 0xFFFF_FFFC);
		cpu.State.A[7] = 0x1FFC;

		for (var i = 0; i < 8 && cpu.State.ProgramCounter != 0xFFFF_FFFC; i++)
		{
			cpu.ExecuteInstruction();
		}

		Assert.Equal(0x7Fu, cpu.State.D[0]);
	}

	[Fact]
	public void ExtWordClearsStaleByteStateForJumpTableIndexes()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x04, 0x00, 0x00, 0x80); // SUBI.B #$80,D0
		Write(bus.Memory, 0x1004, 0x48, 0x80); // EXT.W D0
		Write(bus.Memory, 0x1006, 0x41, 0xFA, 0x00, 0x10); // LEA table(PC),A0
		Write(bus.Memory, 0x100A, 0xD0, 0xC0); // ADDA.W D0,A0
		Write(bus.Memory, 0x100C, 0x30, 0x10); // MOVE.W (A0),D0
		Write(bus.Memory, 0x100E, 0x4E, 0x75); // RTS
		Write(bus.Memory, 0x1018, 0x00, 0x24); // table entry selected by D0.W = 0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);
		bus.WriteLong(0x1FFC, 0xFFFF_FFFC);
		cpu.State.A[7] = 0x1FFC;
		cpu.State.D[0] = 0x1234_5680;

		for (var i = 0; i < 8 && cpu.State.ProgramCounter != 0xFFFF_FFFC; i++)
		{
			cpu.ExecuteInstruction();
		}

		Assert.Equal(0x1234_0024u, cpu.State.D[0]);
	}

	[Fact]
	public void DynamicBclrClearsMemoryBitAndSetsZeroFromPreviousValue()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x03, 0xB9, 0x00, 0x00, 0x20, 0x00); // BCLR D1,$2000.L
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[1] = 3;
		bus.Memory[0x2000] = 0xFF;

		cpu.ExecuteInstruction();

		Assert.Equal(0xF7, bus.Memory[0x2000]);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void DynamicBitOperationRejectsAddressRegisterDestination()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x01, 0xC8); // BSET D0,A0 is illegal on MC68000
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		var exception = Assert.Throws<UnsupportedM68kOpcodeException>(() => cpu.ExecuteInstruction());

		Assert.Equal(0x01C8, exception.Opcode);
		Assert.Equal(0x1000u, exception.ProgramCounter);
	}

	[Fact]
	public void ImmediateBtstByteAbsoluteLongBranchesOnCiaActiveLowFireInput()
	{
		var bus = new AmigaBus();
		bus.GamePort0FirePressed = true;
		bus.GamePort1FirePressed = true;
		Write(bus.ChipRam, 0x1000, 0x08, 0x39, 0x00, 0x06, 0x00, 0xBF, 0xE0, 0x01); // BTST #6,$BFE001.L
		Write(bus.ChipRam, 0x1008, 0x67, 0x04); // BEQ pressed
		Write(bus.ChipRam, 0x100A, 0x70, 0x01); // MOVEQ #1,D0
		Write(bus.ChipRam, 0x100C, 0x60, 0x02); // BRA done
		Write(bus.ChipRam, 0x100E, 0x70, 0x02); // pressed: MOVEQ #2,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_0002u, cpu.State.D[0]);
		Assert.Equal(0x1010u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void ImmediateBtstDisplacementAddressUsesMemoryEaTiming()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x08, 0x2A, 0x00, 0x06, 0x00, 0x02); // BTST #6,2(A2)
		bus.Memory[0x2002] = 0x40;
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[2] = 0x2000;

		cpu.ExecuteInstruction();

		Assert.Equal(16, cpu.State.Cycles);
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(0x1006u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void ImmediateBtstAbsoluteLongCiaAddsPeripheralAccessTiming()
	{
		var program = new byte[] { 0x08, 0x39, 0x00, 0x06, 0x00, 0xBF, 0xE0, 0x01 }; // BTST #6,$BFE001.L
		var bus = CreateRomProgramBus(program);
		bus.GamePort0FirePressed = true;
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x00FC0000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(20, cpu.State.Cycles);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.Equal(0x00FC0008u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void ImmediateBtstAbsoluteLongCustomRegisterKeepsAgnusBusTiming()
	{
		var program = new byte[] { 0x08, 0x39, 0x00, 0x06, 0x00, 0xDF, 0xF0, 0x02 }; // BTST #6,$DFF002.L
		var bus = CreateRomProgramBus(program);
		var expectedDataCycle = 0L;
		_ = new AmigaBus().ReadByte(0x00DFF002, ref expectedDataCycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x00FC0000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(Math.Max(20, expectedDataCycle), cpu.State.Cycles);
		Assert.Equal(0x00FC0008u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void CmpiByteAbsoluteLongBeamRegisterUsesMemoryEaAndAgnusBusTiming()
	{
		var program = new byte[] { 0x0C, 0x39, 0x00, 0xC8, 0x00, 0xDF, 0xF0, 0x06 }; // CMPI.B #$C8,$DFF006.L
		var bus = CreateRomProgramBus(program);
		var expectedDataCycle = 0L;
		_ = new AmigaBus().ReadByte(0x00DFF006, ref expectedDataCycle, AmigaBusAccessKind.CpuDataRead);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x00FC0000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(Math.Max(20, expectedDataCycle), cpu.State.Cycles);
		Assert.Equal(0x00FC0008u, cpu.State.ProgramCounter);
	}

	[Fact]
	public void AddqSubqAddressRegistersUseLongArithmeticAndDoNotChangeFlags()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x55, 0x48); // SUBQ.W #2,A0, size ignored for address registers
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x0007_B3E6;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0007_B3E4u, cpu.State.A[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void MoveImmediateToCcrPreservesSystemBits()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x44, 0xFC, 0x00, 0x15); // MOVE #$15,CCR
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = 0xA5E0;

		cpu.ExecuteInstruction();

		Assert.Equal(0xA5F5, cpu.State.StatusRegister);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void DivsAcceptsNegativeQuotientThatFitsWord()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x81, 0xD0); // DIVS.W (A0),D0
		Write(bus.Memory, 0x2000, 0x00, 0x03);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 0xFFFF_FFF6; // -10

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_FFFDu, cpu.State.D[0]); // remainder -1, quotient -3
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void DivsByZeroVectorsThroughZeroDivideException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x81, 0xFC, 0x00, 0x00); // DIVS.W #0,D0
		bus.WriteLong(5 * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_5678;

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.Halted);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor, (ushort)(BigEndian.ReadUInt16(bus.Memory, 0x2FFA, "saved status register") & M68kCpuState.Supervisor));
		Assert.Equal(0x0000_1004u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
		Assert.Equal(0x1234_5678u, cpu.State.D[0]);
	}

	[Fact]
	public void DivuByZeroVectorsThroughZeroDivideException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x80, 0xFC, 0x00, 0x00); // DIVU.W #0,D0
		bus.WriteLong(5 * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x89AB_CDEF;

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.Halted);
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(0x0000_1004u, BigEndian.ReadUInt32(bus.Memory, 0x2FFC, "saved program counter"));
		Assert.Equal(0x89AB_CDEFu, cpu.State.D[0]);
	}

	[Fact]
	public void DivsOverflowLeavesDestinationUnchanged()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x81, 0xD0); // DIVS.W (A0),D0
		Write(bus.Memory, 0x2000, 0x00, 0x01);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;
		cpu.State.D[0] = 0xFFFF_7FFF; // -32769

		cpu.ExecuteInstruction();

		Assert.Equal(0xFFFF_7FFFu, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Overflow));
	}

	[Fact]
	public void CmpaWordComparesSignExtendedOperandAgainstFullAddressRegister()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xB0, 0xC0); // CMPA.W D0,A0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x0001_0000;
		cpu.State.D[0] = 0;
		cpu.State.StatusRegister |= M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
	}

	[Fact]
	public void CmpmByteComparesPostincrementMemoryAndPreservesExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xB9, 0x0B); // CMPM.B (A3)+,(A4)+
		bus.Memory[0x2000] = (byte)'m';
		bus.Memory[0x3000] = (byte)'m';
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.State.A[3] = 0x2000;
		cpu.State.A[4] = 0x3000;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(0x2001u, cpu.State.A[3]);
		Assert.Equal(0x3001u, cpu.State.A[4]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.Equal(12, cycles);
	}

	[Fact]
	public void TrapPushesExceptionFrameAndVectorsThroughTrapTable()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x41); // TRAP #1
		bus.WriteLong((32 + 1) * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Carry;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.Supervisor | M68kCpuState.Zero | M68kCpuState.Carry, (ushort)((bus.Memory[0x2FFA] << 8) | bus.Memory[0x2FFB]));
		Assert.Equal(0x0000_1002u, ((uint)bus.Memory[0x2FFC] << 24) |
			((uint)bus.Memory[0x2FFD] << 16) |
			((uint)bus.Memory[0x2FFE] << 8) |
			bus.Memory[0x2FFF]);
		Assert.True(cpu.State.Cycles >= 34);
	}

	[Fact]
	public void StatusRegisterSupervisorBitSwitchesBetweenUserAndSupervisorStacks()
	{
		var bus = new TestBus();
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x0400);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x0400, userStackPointer: 0x2000, supervisorMode: false);
		cpu.State.SetActiveStackPointer(0x1FF0);

		cpu.State.StatusRegister |= M68kCpuState.Supervisor;

		Assert.Equal(0x1FF0u, cpu.State.UserStackPointer);
		Assert.Equal(0x0400u, cpu.State.A[7]);

		cpu.State.SetActiveStackPointer(0x03F8);
		cpu.State.StatusRegister &= unchecked((ushort)~M68kCpuState.Supervisor);

		Assert.Equal(0x03F8u, cpu.State.SupervisorStackPointer);
		Assert.Equal(0x1FF0u, cpu.State.A[7]);
	}

	[Fact]
	public void TrapFromUserModeUsesSupervisorStack()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x41); // TRAP #1
		bus.WriteLong((32 + 1) * 4, 0x0000_2000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x0400);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x0400, userStackPointer: 0x3000, supervisorMode: false);

		cpu.ExecuteInstruction();

		Assert.True(cpu.State.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x03FAu, cpu.State.A[7]);
		Assert.Equal(0x3000u, cpu.State.UserStackPointer);
		Assert.Equal(0x0000, (bus.Memory[0x03FA] << 8) | bus.Memory[0x03FB]);
		Assert.Equal(0x0000_1002u, ((uint)bus.Memory[0x03FC] << 24) |
			((uint)bus.Memory[0x03FD] << 16) |
			((uint)bus.Memory[0x03FE] << 8) |
			bus.Memory[0x03FF]);
	}

	[Fact]
	public void RteRestoresUserStackAfterReadingSupervisorExceptionFrame()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x4E, 0x73); // RTE
		Write(bus.Memory, 0x03FA, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x0400);
		cpu.State.ResetStackPointers(supervisorStackPointer: 0x03FA, userStackPointer: 0x3000, supervisorMode: true);

		cpu.ExecuteInstruction();

		Assert.False(cpu.State.GetFlag(M68kCpuState.Supervisor));
		Assert.Equal(0x0000_2000u, cpu.State.ProgramCounter);
		Assert.Equal(0x3000u, cpu.State.A[7]);
		Assert.Equal(0x0400u, cpu.State.SupervisorStackPointer);
	}

	[Fact]
	public void RoxrUsesExtendAsIncomingBitAndUpdatesCarryExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x44, 0xFC, 0x00, 0x10); // MOVE #$10,CCR
		Write(bus.Memory, 0x1004, 0xE2, 0x90); // ROXR.L #1,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x0000_0001;

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x8000_0000u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
	}

	[Fact]
	public void AddxDataRegisterUsesExtendAndPreservesUpperBits()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0xD1, 0x01); // ADDX.B D1,D0
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_56FF;
		cpu.State.D[1] = 0x0000_0001;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234_5601u, cpu.State.D[0]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void SubxPredecrementUsesMemoryOperandsAndExtend()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x91, 0x49); // SUBX.W -(A1),-(A0)
		Write(bus.Memory, 0x2000, 0x00, 0x01);
		Write(bus.Memory, 0x3000, 0x00, 0x00);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x4000);
		cpu.State.A[0] = 0x3002;
		cpu.State.A[1] = 0x2002;
		cpu.State.StatusRegister = M68kCpuState.Supervisor | M68kCpuState.Extend | M68kCpuState.Zero;

		cpu.ExecuteInstruction();

		Assert.Equal(0x3000u, cpu.State.A[0]);
		Assert.Equal(0x2000u, cpu.State.A[1]);
		Assert.Equal(0xFFFE, (bus.Memory[0x3000] << 8) | bus.Memory[0x3001]);
		Assert.True(cpu.State.GetFlag(M68kCpuState.Extend));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Carry));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Zero));
	}

	[Fact]
	public void AmigaBusSchedulesCpuWordCustomWritesAsSingleRegisterEvent()
	{
		var bus = new AmigaBus();
		Write(bus.ChipRam, 0x1000, 0x33, 0xFC, 0x80, 0x0F, 0x00, 0xDF, 0xF0, 0x96); // MOVE.W #$800F,$DFF096
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x2000);

		cpu.ExecuteInstruction();

		var write = Assert.Single(bus.CustomRegisterWrites);
		Assert.Equal(0x096, write.Address);
		Assert.Equal(0x800F, write.Value);
	}

	[Fact]
	public void CpuInstructionFetchAndLongWriteUseCycleAwareBusAccesses()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x20, 0x80); // MOVE.L D0,(A0)
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[0] = 0x2000;

		cpu.ExecuteInstruction();

		Assert.Contains(
			bus.Accesses,
			access =>
				access.Address == 0x1000 &&
				access.Kind == M68kBusAccessKind.CpuInstructionFetch &&
				access.Size == AmigaBusAccessSize.Word &&
				!access.IsWrite);
		var write = Assert.Single(bus.Accesses, access => access.Kind == M68kBusAccessKind.CpuDataWrite && access.IsWrite);
		Assert.Equal(0x2000u, write.Address);
		Assert.Equal(AmigaBusAccessSize.Long, write.Size);
		Assert.Equal(0x12, bus.Memory[0x2000]);
		Assert.Equal(0x34, bus.Memory[0x2001]);
		Assert.Equal(0x56, bus.Memory[0x2002]);
		Assert.Equal(0x78, bus.Memory[0x2003]);
	}

	[Fact]
	public void ClrMemoryReadsDestinationBeforeWritingZero()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x42, 0x90); // CLR.L (A0)
		Write(bus.Memory, 0x2000, 0x12, 0x34, 0x56, 0x78);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2000;

		cpu.ExecuteInstruction();

		var dataAccesses = bus.Accesses
			.Where(access => access.Kind is M68kBusAccessKind.CpuDataRead or M68kBusAccessKind.CpuDataWrite)
			.ToArray();
		Assert.Equal(2, dataAccesses.Length);
		Assert.Equal((uint)0x2000, dataAccesses[0].Address);
		Assert.Equal(AmigaBusAccessSize.Long, dataAccesses[0].Size);
		Assert.False(dataAccesses[0].IsWrite);
		Assert.Equal((uint)0x2000, dataAccesses[1].Address);
		Assert.Equal(AmigaBusAccessSize.Long, dataAccesses[1].Size);
		Assert.True(dataAccesses[1].IsWrite);
		Assert.True(dataAccesses[1].Cycle >= dataAccesses[0].Cycle);
		Assert.Equal(0u, BigEndian.ReadUInt32(bus.Memory, 0x2000, "cleared longword"));
		Assert.True(cpu.State.GetFlag(M68kCpuState.Zero));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Negative));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Overflow));
		Assert.False(cpu.State.GetFlag(M68kCpuState.Carry));
	}

	[Fact]
	public void OddWordDataReadRaisesAddressErrorWith68000Frame()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x30, 0x10); // MOVE.W (A0),D0
		bus.WriteLong(0x000C, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.A[0] = 0x2001;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x001D, ReadWord(bus.Memory, 0x2FF2));
		Assert.Equal(0x0000_2001u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0x3010, ReadWord(bus.Memory, 0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1002u, ReadLong(bus.Memory, 0x2FFC));
	}

	[Fact]
	public void OddWordDataWriteRaisesAddressErrorWith68000Frame()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x30, 0x80); // MOVE.W D0,(A0)
		bus.WriteLong(0x000C, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);
		cpu.State.D[0] = 0x1234;
		cpu.State.A[0] = 0x2001;

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x000D, ReadWord(bus.Memory, 0x2FF2));
		Assert.Equal(0x0000_2001u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0x3080, ReadWord(bus.Memory, 0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1002u, ReadLong(bus.Memory, 0x2FFC));
		Assert.Equal(0x00, bus.Memory[0x2001]);
	}

	[Fact]
	public void OddInstructionFetchRaisesAddressErrorWithProgramFunctionCode()
	{
		var bus = new TestBus();
		bus.WriteLong(0x000C, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1001, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FF2u, cpu.State.A[7]);
		Assert.Equal(0x0016, ReadWord(bus.Memory, 0x2FF2));
		Assert.Equal(0x0000_1001u, ReadLong(bus.Memory, 0x2FF4));
		Assert.Equal(0x0000, ReadWord(bus.Memory, 0x2FF8));
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1001u, ReadLong(bus.Memory, 0x2FFC));
	}

	[Fact]
	public void InvalidImmediateSizeRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x00, 0xF8);
		bus.WriteLong(0x0010, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1000u, ReadLong(bus.Memory, 0x2FFC));
	}

	[Fact]
	public void InvalidMode7EffectiveAddressRaisesIllegalInstructionException()
	{
		var bus = new TestBus();
		Write(bus.Memory, 0x1000, 0x30, 0xBF);
		bus.WriteLong(0x0010, 0x0000_4000);
		var cpu = new M68kInterpreter(bus);
		cpu.Reset(0x1000, 0x3000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x0000_4000u, cpu.State.ProgramCounter);
		Assert.Equal(0x2FFAu, cpu.State.A[7]);
		Assert.Equal(M68kCpuState.ResetStatusRegister, ReadWord(bus.Memory, 0x2FFA));
		Assert.Equal(0x0000_1000u, ReadLong(bus.Memory, 0x2FFC));
	}

	private static void Write(byte[] memory, int address, params byte[] data)
	{
		Array.Copy(data, 0, memory, address, data.Length);
	}

	private static ushort ReadWord(byte[] memory, int address)
		=> (ushort)((memory[address] << 8) | memory[address + 1]);

	private static uint ReadLong(byte[] memory, int address)
		=> ((uint)memory[address] << 24) |
			((uint)memory[address + 1] << 16) |
			((uint)memory[address + 2] << 8) |
			memory[address + 3];

	private static AmigaBus CreateRomProgramBus(ReadOnlySpan<byte> program)
	{
		var bus = new AmigaBus();
		var rom = new byte[0x40000];
		program.CopyTo(rom);
		bus.MapReadOnlyMemory(0x00FC0000, rom);
		return bus;
	}

	private sealed class TestBus : IM68kBus
	{
		public byte[] Memory { get; } = new byte[0x0100_0000];

		public List<(uint Address, byte Value, long Cycle)> Writes { get; } = new();

		public List<(uint Address, M68kBusAccessKind Kind, AmigaBusAccessSize Size, bool IsWrite, long Cycle)> Accesses { get; } = new();

		public int ExternalResetCount { get; private set; }

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, false, cycle));
			return Memory[address];
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, false, cycle));
			return (ushort)((Memory[address] << 8) | Memory[address + 1]);
		}

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Long, false, cycle));
			return ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, true, cycle));
			Memory[address] = value;
			Writes.Add((address, value, cycle));
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, true, cycle));
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
			Writes.Add((address, (byte)(value >> 8), cycle));
			Writes.Add((address + 1, (byte)value, cycle));
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Long, true, cycle));
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
			Writes.Add((address, (byte)(value >> 24), cycle));
			Writes.Add((address + 1, (byte)(value >> 16), cycle));
			Writes.Add((address + 2, (byte)(value >> 8), cycle));
			Writes.Add((address + 3, (byte)value, cycle));
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
			ExternalResetCount++;
		}

		public void WriteLong(uint address, uint value)
		{
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
		}
	}

	private sealed class CycleCountingBus : IM68kBus
	{
		private const int AccessCycles = 2;

		public byte[] Memory { get; } = new byte[0x0100_0000];

		public List<(uint Address, long Cycle)> DataReadCycles { get; } = new();

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			var value = Memory[address];
			cycle += AccessCycles;
			return value;
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			var value = (ushort)((Memory[address] << 8) | Memory[address + 1]);
			if (accessKind == M68kBusAccessKind.CpuDataRead)
			{
				DataReadCycles.Add((address, cycle));
			}

			cycle += AccessCycles;
			return value;
		}

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			var value = ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
			cycle += AccessCycles * 2;
			return value;
		}

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Memory[address] = value;
			cycle += AccessCycles;
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			WriteWordRaw(address, value);
			cycle += AccessCycles;
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
			cycle += AccessCycles * 2;
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

		public void WriteWordRaw(uint address, ushort value)
		{
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
		}
	}
}
