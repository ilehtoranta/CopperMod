namespace CopperMod.Sid.Tests;

public sealed class Mos6510Tests
{
	[Fact]
	public void ExecutesOfficialLoadStoreAndCycleCounts()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0xA9; // LDA #$42
		bus.Memory[0x1001] = 0x42;
		bus.Memory[0x1002] = 0x8D; // STA $D400
		bus.Memory[0x1003] = 0x00;
		bus.Memory[0x1004] = 0xD4;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);

		Assert.Equal(2, cpu.ExecuteInstruction());
		Assert.Equal(4, cpu.ExecuteInstruction());

		Assert.Equal(0x42, cpu.A);
		Assert.Equal(6, cpu.Cycles);
		Assert.Equal((ushort)0xD400, bus.LastWriteAddress);
		Assert.Equal(0x42, bus.LastWriteValue);
		Assert.Equal(3, bus.LastWriteCycle);
	}

	[Fact]
	public void BranchAddsPageCrossingPenalty()
	{
		var bus = new TestBus();
		bus.Memory[0x10FD] = 0xD0; // BNE +2
		bus.Memory[0x10FE] = 0x02;
		bus.Memory[0x1101] = 0xEA;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x10FD);
		cpu.Status &= 0xFD;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(4, cycles);
		Assert.Equal(0x1101, cpu.ProgramCounter);
	}

	[Fact]
	public void ExecutesCommonIllegalSlo()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0xA9; // LDA #$01
		bus.Memory[0x1001] = 0x01;
		bus.Memory[0x1002] = 0x07; // SLO $20
		bus.Memory[0x1003] = 0x20;
		bus.Memory[0x0020] = 0x40;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);

		cpu.ExecuteInstruction();
		cpu.ExecuteInstruction();

		Assert.Equal(0x80, bus.Memory[0x0020]);
		Assert.Equal(0x81, cpu.A);
	}

	[Fact]
	public void ReadModifyWriteInstructionsPerformDummyWriteBeforeFinalWrite()
	{
		var bus = new TestBus();
		bus.Memory[0x1000] = 0x0E; // ASL $D019
		bus.Memory[0x1001] = 0x19;
		bus.Memory[0x1002] = 0xD0;
		bus.Memory[0xD019] = 0x81;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);

		cpu.ExecuteInstruction();

		Assert.Equal(2, bus.Writes.Count);
		Assert.Equal((0xD019, (byte)0x81, 4), bus.Writes[0]);
		Assert.Equal((0xD019, (byte)0x02, 5), bus.Writes[1]);
	}

	[Theory]
	[MemberData(nameof(StoreWriteCases))]
	public void StoreInstructionsWriteOnFinalBusCycle(StoreWriteCase testCase)
	{
		var bus = new TestBus();
		LoadProgram(bus, testCase.Program);
		if (testCase.PointerLocation.HasValue && testCase.PointerTarget.HasValue)
		{
			WriteZeroPagePointer(bus, testCase.PointerLocation.Value, testCase.PointerTarget.Value);
		}

		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);
		cpu.A = StoreA;
		cpu.X = StoreX;
		cpu.Y = StoreY;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(testCase.TotalCycles, cycles);
		var write = Assert.Single(bus.Writes);
		Assert.Equal(testCase.TargetAddress, write.Address);
		Assert.Equal(testCase.ExpectedValue, write.Value);
		Assert.Equal(testCase.TotalCycles - 1, write.CycleOffset);
	}

	[Theory]
	[MemberData(nameof(ReadModifyWriteCases))]
	public void ReadModifyWriteInstructionsPerformDummyAndFinalWritesOnLastTwoCycles(ReadModifyWriteCase testCase)
	{
		var bus = new TestBus();
		LoadProgram(bus, testCase.Program);
		bus.Memory[testCase.TargetAddress] = testCase.OriginalValue;
		if (testCase.PointerLocation.HasValue && testCase.PointerTarget.HasValue)
		{
			WriteZeroPagePointer(bus, testCase.PointerLocation.Value, testCase.PointerTarget.Value);
		}

		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);
		cpu.A = 0x22;
		cpu.X = 0x0C;
		cpu.Y = 0x03;
		if (testCase.CarryIn)
		{
			cpu.Status |= 0x01;
		}

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(testCase.TotalCycles, cycles);
		Assert.Equal(2, bus.Writes.Count);
		Assert.Equal((testCase.TargetAddress, testCase.OriginalValue, testCase.TotalCycles - 2), bus.Writes[0]);
		Assert.Equal((testCase.TargetAddress, testCase.FinalValue, testCase.TotalCycles - 1), bus.Writes[1]);
	}

	public static IEnumerable<object[]> StoreWriteCases()
	{
		yield return Store("STA zpg", [0x85, 0x18], 3, 0x0018, StoreA);
		yield return Store("STA zpg,X", [0x95, ZeroPageOperandForX(0x18)], 4, 0x0018, StoreA);
		yield return Store("STA abs", Absolute(0x8D, 0xD418), 4, 0xD418, StoreA);
		yield return Store("STA abs,X", AbsoluteX(0x9D, 0xD418, StoreX), 5, 0xD418, StoreA);
		yield return Store("STA abs,Y", AbsoluteY(0x99, 0xD418, StoreY), 5, 0xD418, StoreA);
		yield return Store("STA (zpg,X)", [0x81, 0x20], 6, 0xD418, StoreA, PointerForIndirectX(0x20, StoreX), 0xD418);
		yield return Store("STA (zpg),Y", [0x91, 0x30], 6, 0xD418, StoreA, 0x30, unchecked((ushort)(0xD418 - StoreY)));
		yield return Store("STX zpg", [0x86, 0x18], 3, 0x0018, StoreX);
		yield return Store("STX zpg,Y", [0x96, ZeroPageOperandForY(0x18)], 4, 0x0018, StoreX);
		yield return Store("STX abs", Absolute(0x8E, 0xD418), 4, 0xD418, StoreX);
		yield return Store("STY zpg", [0x84, 0x18], 3, 0x0018, StoreY);
		yield return Store("STY zpg,X", [0x94, ZeroPageOperandForX(0x18)], 4, 0x0018, StoreY);
		yield return Store("STY abs", Absolute(0x8C, 0xD418), 4, 0xD418, StoreY);
		yield return Store("SAX zpg", [0x87, 0x18], 3, 0x0018, StoreAAndX);
		yield return Store("SAX zpg,Y", [0x97, ZeroPageOperandForY(0x18)], 4, 0x0018, StoreAAndX);
		yield return Store("SAX abs", Absolute(0x8F, 0xD418), 4, 0xD418, StoreAAndX);
		yield return Store("SAX (zpg,X)", [0x83, 0x20], 6, 0xD418, StoreAAndX, PointerForIndirectX(0x20, StoreX), 0xD418);
		yield return Store("AHX (zpg),Y", [0x93, 0x30], 6, 0xD418, StoreHighMasked(StoreAAndX, 0xD418), 0x30, unchecked((ushort)(0xD418 - StoreY)));
		yield return Store("AHX abs,Y", AbsoluteY(0x9F, 0xD418, StoreY), 5, 0xD418, StoreHighMasked(StoreAAndX, 0xD418));
		yield return Store("SHX abs,Y", AbsoluteY(0x9E, 0xD418, StoreY), 5, 0xD418, StoreHighMasked(StoreX, 0xD418));
		yield return Store("SHY abs,X", AbsoluteX(0x9C, 0xD418, StoreX), 5, 0xD418, StoreHighMasked(StoreY, 0xD418));
		yield return Store("TAS abs,Y", AbsoluteY(0x9B, 0xD418, StoreY), 5, 0xD418, StoreHighMasked(StoreAAndX, 0xD418));
	}

	public static IEnumerable<object[]> ReadModifyWriteCases()
	{
		yield return Rmw("ASL abs", Absolute(0x0E, 0xD418), 6, 0xD418, 0x81, 0x02);
		yield return Rmw("LSR abs,X", AbsoluteX(0x5E, 0xD418, 0x0C), 7, 0xD418, 0x81, 0x40);
		yield return Rmw("ROL abs", Absolute(0x2E, 0xD418), 6, 0xD418, 0x41, 0x82);
		yield return Rmw("ROR abs,X", AbsoluteX(0x7E, 0xD418, 0x0C), 7, 0xD418, 0x02, 0x81, carryIn: true);
		yield return Rmw("INC abs", Absolute(0xEE, 0xD418), 6, 0xD418, 0x7F, 0x80);
		yield return Rmw("DEC abs,X", AbsoluteX(0xDE, 0xD418, 0x0C), 7, 0xD418, 0x80, 0x7F);
		yield return Rmw("SLO abs", Absolute(0x0F, 0xD418), 6, 0xD418, 0x81, 0x02);
		yield return Rmw("RLA abs,X", AbsoluteX(0x3F, 0xD418, 0x0C), 7, 0xD418, 0x41, 0x82);
		yield return Rmw("SRE abs,Y", AbsoluteY(0x5B, 0xD418, 0x03), 7, 0xD418, 0x81, 0x40);
		yield return Rmw("RRA (zpg,X)", [0x63, 0x20], 8, 0xD418, 0x02, 0x81, carryIn: true, PointerForIndirectX(0x20, 0x0C), 0xD418);
		yield return Rmw("DCP (zpg),Y", [0xD3, 0x30], 8, 0xD418, 0x80, 0x7F, carryIn: false, 0x30, unchecked((ushort)(0xD418 - 0x03)));
		yield return Rmw("ISC abs", Absolute(0xEF, 0xD418), 6, 0xD418, 0x7F, 0x80);
	}

	private sealed class TestBus : ICpuBus
	{
		public byte[] Memory { get; } = new byte[65536];

		public List<(ushort Address, byte Value, int CycleOffset)> Writes { get; } = new();

		public ushort LastWriteAddress { get; private set; }

		public byte LastWriteValue { get; private set; }

		public long LastWriteCycle { get; private set; }

		public byte Read(ushort address, int cycleOffset = 0)
		{
			_ = cycleOffset;
			return Memory[address];
		}

		public void Write(ushort address, byte value, int cycleOffset)
		{
			Memory[address] = value;
			Writes.Add((address, value, cycleOffset));
			LastWriteAddress = address;
			LastWriteValue = value;
			LastWriteCycle = cycleOffset;
		}
	}

	private const byte StoreA = 0xF7;
	private const byte StoreX = 0xD5;
	private const byte StoreY = 0x03;
	private const byte StoreAAndX = StoreA & StoreX;

	private static object[] Store(
		string name,
		byte[] program,
		int totalCycles,
		ushort targetAddress,
		byte expectedValue,
		byte? pointerLocation = null,
		ushort? pointerTarget = null)
	{
		return [new StoreWriteCase(name, program, totalCycles, targetAddress, expectedValue, pointerLocation, pointerTarget)];
	}

	private static object[] Rmw(
		string name,
		byte[] program,
		int totalCycles,
		ushort targetAddress,
		byte originalValue,
		byte finalValue,
		bool carryIn = false,
		byte? pointerLocation = null,
		ushort? pointerTarget = null)
	{
		return [new ReadModifyWriteCase(name, program, totalCycles, targetAddress, originalValue, finalValue, carryIn, pointerLocation, pointerTarget)];
	}

	private static void LoadProgram(TestBus bus, IReadOnlyList<byte> program)
	{
		for (var i = 0; i < program.Count; i++)
		{
			bus.Memory[0x1000 + i] = program[i];
		}
	}

	private static void WriteZeroPagePointer(TestBus bus, byte location, ushort target)
	{
		bus.Memory[location] = (byte)(target & 0xFF);
		bus.Memory[(byte)(location + 1)] = (byte)(target >> 8);
	}

	private static byte[] Absolute(byte opcode, ushort address)
	{
		return [opcode, (byte)(address & 0xFF), (byte)(address >> 8)];
	}

	private static byte[] AbsoluteX(byte opcode, ushort targetAddress, byte x)
	{
		return Absolute(opcode, unchecked((ushort)(targetAddress - x)));
	}

	private static byte[] AbsoluteY(byte opcode, ushort targetAddress, byte y)
	{
		return Absolute(opcode, unchecked((ushort)(targetAddress - y)));
	}

	private static byte ZeroPageOperandForX(byte target)
	{
		return unchecked((byte)(target - StoreX));
	}

	private static byte ZeroPageOperandForY(byte target)
	{
		return unchecked((byte)(target - StoreY));
	}

	private static byte PointerForIndirectX(byte operand, byte x)
	{
		return unchecked((byte)(operand + x));
	}

	private static byte StoreHighMasked(byte value, ushort targetAddress)
	{
		return (byte)(value & (((targetAddress >> 8) + 1) & 0xFF));
	}

	public sealed record StoreWriteCase(
		string Name,
		byte[] Program,
		int TotalCycles,
		ushort TargetAddress,
		byte ExpectedValue,
		byte? PointerLocation,
		ushort? PointerTarget)
	{
		public override string ToString() => Name;
	}

	public sealed record ReadModifyWriteCase(
		string Name,
		byte[] Program,
		int TotalCycles,
		ushort TargetAddress,
		byte OriginalValue,
		byte FinalValue,
		bool CarryIn,
		byte? PointerLocation,
		ushort? PointerTarget)
	{
		public override string ToString() => Name;
	}
}
