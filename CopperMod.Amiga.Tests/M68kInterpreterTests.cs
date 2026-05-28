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
				access.Kind == AmigaBusAccessKind.CpuInstructionFetch &&
				access.Size == AmigaBusAccessSize.Word &&
				!access.IsWrite);
		var write = Assert.Single(bus.Accesses, access => access.Kind == AmigaBusAccessKind.CpuDataWrite && access.IsWrite);
		Assert.Equal(0x2000u, write.Address);
		Assert.Equal(AmigaBusAccessSize.Long, write.Size);
		Assert.Equal(0x12, bus.Memory[0x2000]);
		Assert.Equal(0x34, bus.Memory[0x2001]);
		Assert.Equal(0x56, bus.Memory[0x2002]);
		Assert.Equal(0x78, bus.Memory[0x2003]);
	}

	private static void Write(byte[] memory, int address, params byte[] data)
	{
		Array.Copy(data, 0, memory, address, data.Length);
	}

	private sealed class TestBus : IM68kBus
	{
		public byte[] Memory { get; } = new byte[0x0100_0000];

		public List<(uint Address, byte Value, long Cycle)> Writes { get; } = new();

		public List<(uint Address, AmigaBusAccessKind Kind, AmigaBusAccessSize Size, bool IsWrite, long Cycle)> Accesses { get; } = new();

		public byte ReadByte(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, false, cycle));
			return Memory[address];
		}

		public ushort ReadWord(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, false, cycle));
			return (ushort)((Memory[address] << 8) | Memory[address + 1]);
		}

		public uint ReadLong(uint address, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Long, false, cycle));
			return ((uint)Memory[address] << 24) |
				((uint)Memory[address + 1] << 16) |
				((uint)Memory[address + 2] << 8) |
				Memory[address + 3];
		}

		public void WriteByte(uint address, byte value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Byte, true, cycle));
			Memory[address] = value;
			Writes.Add((address, value, cycle));
		}

		public void WriteWord(uint address, ushort value, ref long cycle, AmigaBusAccessKind accessKind)
		{
			Accesses.Add((address, accessKind, AmigaBusAccessSize.Word, true, cycle));
			Memory[address] = (byte)(value >> 8);
			Memory[address + 1] = (byte)value;
			Writes.Add((address, (byte)(value >> 8), cycle));
			Writes.Add((address + 1, (byte)value, cycle));
		}

		public void WriteLong(uint address, uint value, ref long cycle, AmigaBusAccessKind accessKind)
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

		public bool TryInvokeHost(uint address, M68kCpuState state)
		{
			_ = address;
			_ = state;
			return false;
		}

		public void WriteLong(uint address, uint value)
		{
			Memory[address] = (byte)(value >> 24);
			Memory[address + 1] = (byte)(value >> 16);
			Memory[address + 2] = (byte)(value >> 8);
			Memory[address + 3] = (byte)value;
		}
	}
}
