namespace AmigaTracker.Sid.Tests;

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

	private sealed class TestBus : ICpuBus
	{
		public byte[] Memory { get; } = new byte[65536];

		public List<(ushort Address, byte Value, int CycleOffset)> Writes { get; } = new();

		public ushort LastWriteAddress { get; private set; }

		public byte LastWriteValue { get; private set; }

		public long LastWriteCycle { get; private set; }

		public byte Read(ushort address)
		{
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
}
