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
	[MemberData(nameof(LoadReadCases))]
	public void LoadInstructionsReadDataOnFinalBusCycle(LoadReadCase testCase)
	{
		var bus = new TestBus();
		LoadProgram(bus, testCase.Program);
		bus.Memory[testCase.TargetAddress] = 0x5A;
		if (testCase.PointerLocation.HasValue && testCase.PointerTarget.HasValue)
		{
			WriteZeroPagePointer(bus, testCase.PointerLocation.Value, testCase.PointerTarget.Value);
		}

		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);
		cpu.X = testCase.X;
		cpu.Y = testCase.Y;

		var cycles = cpu.ExecuteInstruction();

		Assert.Equal(testCase.TotalCycles, cycles);
		Assert.Equal(0x5A, cpu.A);
		var targetRead = Assert.Single(bus.Reads.Where(read => read.Address == testCase.TargetAddress));
		Assert.Equal(testCase.TotalCycles - 1, targetRead.CycleOffset);
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
		var targetRead = Assert.Single(bus.BusFrames.Where(frame =>
			frame.Address == testCase.TargetAddress &&
			frame.Kind == CpuBusAccessKind.Read));
		Assert.Equal(testCase.TotalCycles - 3, targetRead.CycleOffset);
		Assert.Equal(2, bus.Writes.Count);
		Assert.Equal((testCase.TargetAddress, testCase.OriginalValue, testCase.TotalCycles - 2), bus.Writes[0]);
		Assert.Equal((testCase.TargetAddress, testCase.FinalValue, testCase.TotalCycles - 1), bus.Writes[1]);
	}

	[Fact]
	public void BusTraceRecordsOpcodeOperandAndIdleCycles()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x18]); // CLC
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);

		cpu.ExecuteInstruction();

		Assert.Collection(
			bus.BusFrames,
			frame =>
			{
				Assert.Equal(CpuBusAccessKind.OpcodeFetch, frame.Kind);
				Assert.Equal(0, frame.CycleOffset);
				Assert.Equal(0x1000, frame.Address);
				Assert.Equal((byte)0x18, frame.Value.GetValueOrDefault());
			},
			frame =>
			{
				Assert.Equal(CpuBusAccessKind.Idle, frame.Kind);
				Assert.Equal(1, frame.CycleOffset);
			});
	}

	[Fact]
	public void AbsoluteIndexedPageCrossLoadEmitsDummyReadBeforeFinalRead()
	{
		var bus = new TestBus();
		LoadProgram(bus, Absolute(0xBD, 0xD3FF)); // LDA $D3FF,X
		bus.Memory[0xD41C] = 0x5A;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);
		cpu.X = 0x1D;

		cpu.ExecuteInstruction();

		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == CpuBusAccessKind.DummyRead &&
			frame.CycleOffset == 3 &&
			frame.Address == 0xD31C);
		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == CpuBusAccessKind.Read &&
			frame.CycleOffset == 4 &&
			frame.Address == 0xD41C &&
			frame.Value == 0x5A);
	}

	[Fact]
	public void AbsoluteIndexedStoreEmitsDummyReadBeforeFinalWrite()
	{
		var bus = new TestBus();
		LoadProgram(bus, AbsoluteX(0x9D, 0xD418, 0x0C)); // STA $D40C,X
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);
		cpu.A = 0x77;
		cpu.X = 0x0C;

		cpu.ExecuteInstruction();

		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == CpuBusAccessKind.DummyRead &&
			frame.CycleOffset == 3 &&
			frame.Address == 0xD418);
		Assert.Contains(bus.BusFrames, frame =>
			frame.Kind == CpuBusAccessKind.Write &&
			frame.CycleOffset == 4 &&
			frame.Address == 0xD418 &&
			frame.Value == 0x77);
	}

	[Fact]
	public void BranchPageCrossTraceIncludesTwoIdleCycles()
	{
		var bus = new TestBus();
		bus.Memory[0x10FD] = 0xD0; // BNE +2
		bus.Memory[0x10FE] = 0x02;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x10FD);
		cpu.Status &= 0xFD;

		cpu.ExecuteInstruction();

		Assert.Equal(0x1101, cpu.ProgramCounter);
		Assert.Contains(bus.BusFrames, frame => frame.Kind == CpuBusAccessKind.OperandFetch && frame.CycleOffset == 1);
		Assert.Contains(bus.BusFrames, frame => frame.Kind == CpuBusAccessKind.Idle && frame.CycleOffset == 2);
		Assert.Contains(bus.BusFrames, frame => frame.Kind == CpuBusAccessKind.Idle && frame.CycleOffset == 3);
	}

	[Fact]
	public void JsrTraceOrdersOperandIdleStackWritesAndHighOperandFetch()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x20, 0x56, 0x34]); // JSR $3456
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x3456, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames.Select(frame => (frame.Kind, frame.CycleOffset)).ToArray(),
			item => Assert.Equal((CpuBusAccessKind.OpcodeFetch, 0), item),
			item => Assert.Equal((CpuBusAccessKind.OperandFetch, 1), item),
			item => Assert.Equal((CpuBusAccessKind.Idle, 2), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 3), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 4), item),
			item => Assert.Equal((CpuBusAccessKind.OperandFetch, 5), item));
		Assert.Equal(0x10, bus.Memory[0x01FD]);
		Assert.Equal(0x02, bus.Memory[0x01FC]);
	}

	[Fact]
	public void BrkTraceOrdersSignatureStackWritesAndVectorReads()
	{
		var bus = new TestBus();
		LoadProgram(bus, [0x00, 0xEA]);
		bus.Memory[0xFFFE] = 0x34;
		bus.Memory[0xFFFF] = 0x12;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x1000);

		cpu.ExecuteInstruction();

		Assert.Equal(0x1234, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames.Select(frame => (frame.Kind, frame.CycleOffset)).ToArray(),
			item => Assert.Equal((CpuBusAccessKind.OpcodeFetch, 0), item),
			item => Assert.Equal((CpuBusAccessKind.OperandFetch, 1), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 2), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 3), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 4), item),
			item => Assert.Equal((CpuBusAccessKind.VectorRead, 5), item),
			item => Assert.Equal((CpuBusAccessKind.VectorRead, 6), item));
	}

	[Fact]
	public void IrqTraceOrdersIdleStackWritesAndVectorReads()
	{
		var bus = new TestBus();
		bus.Memory[0xFFFE] = 0x78;
		bus.Memory[0xFFFF] = 0x56;
		var cpu = new Mos6510(bus);
		cpu.Reset(0x2345);
		cpu.Status &= 0xFB;

		Assert.True(cpu.TryRequestIrq());

		Assert.Equal(0x5678, cpu.ProgramCounter);
		Assert.Collection(
			bus.BusFrames.Select(frame => (frame.Kind, frame.CycleOffset)).ToArray(),
			item => Assert.Equal((CpuBusAccessKind.Idle, 0), item),
			item => Assert.Equal((CpuBusAccessKind.Idle, 1), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 2), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 3), item),
			item => Assert.Equal((CpuBusAccessKind.StackWrite, 4), item),
			item => Assert.Equal((CpuBusAccessKind.VectorRead, 5), item),
			item => Assert.Equal((CpuBusAccessKind.VectorRead, 6), item));
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

	public static IEnumerable<object[]> LoadReadCases()
	{
		yield return Load("LDA zpg", [0xA5, 0x20], 3, 0x0020);
		yield return Load("LDA zpg,X", [0xB5, 0x20], 4, 0x0023, x: 0x03);
		yield return Load("LDA abs", Absolute(0xAD, 0xD41C), 4, 0xD41C);
		yield return Load("LDA abs,X", Absolute(0xBD, 0xD419), 4, 0xD41C, x: 0x03);
		yield return Load("LDA abs,X page cross", Absolute(0xBD, 0xD3FF), 5, 0xD41C, x: 0x1D);
		yield return Load("LDA (zpg),Y", [0xB1, 0x30], 5, 0xD41C, y: 0x03, pointerLocation: 0x30, pointerTarget: 0xD419);
		yield return Load("LDA (zpg),Y page cross", [0xB1, 0x30], 6, 0xD41C, y: 0x1D, pointerLocation: 0x30, pointerTarget: 0xD3FF);
		yield return Load("LAX abs", Absolute(0xAF, 0xD41C), 4, 0xD41C);
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

		public List<(ushort Address, int CycleOffset)> Reads { get; } = new();

		public List<CpuBusTraceFrame> BusFrames { get; } = new();

		public ushort LastWriteAddress { get; private set; }

		public byte LastWriteValue { get; private set; }

		public long LastWriteCycle { get; private set; }

		public byte Read(ushort address, int cycleOffset = 0, CpuBusAccessKind kind = CpuBusAccessKind.Read)
		{
			Reads.Add((address, cycleOffset));
			var value = Memory[address];
			BusFrames.Add(new CpuBusTraceFrame(cycleOffset, cycleOffset, cycleOffset, value, address, value, kind, delayedByVic: false));
			return value;
		}

		public void Write(ushort address, byte value, int cycleOffset, CpuBusAccessKind kind = CpuBusAccessKind.Write)
		{
			Memory[address] = value;
			Writes.Add((address, value, cycleOffset));
			BusFrames.Add(new CpuBusTraceFrame(cycleOffset, cycleOffset, cycleOffset, 0, address, value, kind, delayedByVic: false));
			LastWriteAddress = address;
			LastWriteValue = value;
			LastWriteCycle = cycleOffset;
		}

		public void Idle(ushort address, int cycleOffset, CpuBusAccessKind kind = CpuBusAccessKind.Idle)
		{
			BusFrames.Add(new CpuBusTraceFrame(cycleOffset, cycleOffset, cycleOffset, 0, address, null, kind, delayedByVic: false));
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

	private static object[] Load(
		string name,
		byte[] program,
		int totalCycles,
		ushort targetAddress,
		byte x = 0,
		byte y = 0,
		byte? pointerLocation = null,
		ushort? pointerTarget = null)
	{
		return [new LoadReadCase(name, program, totalCycles, targetAddress, x, y, pointerLocation, pointerTarget)];
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

	public sealed record LoadReadCase(
		string Name,
		byte[] Program,
		int TotalCycles,
		ushort TargetAddress,
		byte X,
		byte Y,
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
