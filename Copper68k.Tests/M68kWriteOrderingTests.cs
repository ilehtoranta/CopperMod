using Copper68k;

namespace Copper68k.Tests;

/// <summary>
/// Verifies that the 68000 interpreter writes long words in the correct
/// bus access order for predecrement and stack-push operations.
/// The real MC68000 writes the low word (at addr+2) first, then the high
/// word (at addr) for these cases, unlike normal ascending long writes.
/// </summary>
public sealed class M68kWriteOrderingTests
{
	private const uint CodeBase = 0x1000;
	private const uint StackBase = 0x4000;

	[Fact]
	public void PushLongWritesLowWordFirst()
	{
		// JSR $00002000 — pushes the return address (PC after JSR) to stack
		// Return address will be $1006 (after the 3-word JSR instruction)
		var bus = new WriteRecordingBus();
		bus.WriteWords(CodeBase,
			0x4EB9, 0x0000, 0x2000); // JSR $00002000
		bus.WriteWords(0x2000, 0x4E71); // NOP at target (so CPU doesn't crash)

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(CodeBase, StackBase);
		cpu.ExecuteInstructions(1, long.MaxValue, NullBoundary.Instance);

		// JSR pushes the return address (0x00001006) as a long word.
		// Real 68000: low word ($1006) written to SP-2 first, then high word ($0000) to SP-4.
		var writes = bus.WordWrites;
		var pushWrites = writes.Where(w => w.Address >= StackBase - 4 && w.Address < StackBase).ToList();
		Assert.Equal(2, pushWrites.Count);
		// First write should be the low word at the higher address (SP-2 = $3FFE)
		Assert.Equal(StackBase - 2, pushWrites[0].Address);
		Assert.Equal(0x1006, pushWrites[0].Value); // low word of return address
		// Second write should be the high word at the lower address (SP-4 = $3FFC)
		Assert.Equal(StackBase - 4, pushWrites[1].Address);
		Assert.Equal(0x0000, pushWrites[1].Value); // high word of return address
	}

	[Fact]
	public void MoveLongToPredecrementWritesLowWordFirst()
	{
		// MOVE.L D0,-(A1)
		var bus = new WriteRecordingBus();
		bus.WriteWords(CodeBase, 0x2300); // MOVE.L D0,-(A1)

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[0] = 0xAABB_CCDD;
		cpu.State.A[1] = 0x3000;
		cpu.ExecuteInstructions(1, long.MaxValue, NullBoundary.Instance);

		Assert.Equal(0x2FFCu, cpu.State.A[1]); // Decremented by 4
		var writes = bus.WordWrites.Where(w => w.Address >= 0x2FFC && w.Address <= 0x2FFE).ToList();
		Assert.Equal(2, writes.Count);
		// First write: low word at addr+2 ($2FFE)
		Assert.Equal(0x2FFEu, writes[0].Address);
		Assert.Equal(0xCCDD, writes[0].Value);
		// Second write: high word at addr ($2FFC)
		Assert.Equal(0x2FFCu, writes[1].Address);
		Assert.Equal(0xAABB, writes[1].Value);
	}

	[Fact]
	public void AddxLongMemoryWritebackWritesLowWordFirst()
	{
		// ADDX.L -(A0),-(A1) — opcode $D388
		var bus = new WriteRecordingBus();
		bus.WriteWords(CodeBase, 0xD388); // ADDX.L -(A0),-(A1)
		// Source at A0-4 = $2FFC: $0000_0001
		bus.WriteLong(0x2FFC, 0x0000_0001);
		// Destination at A1-4 = $3FFC: $0000_0002
		bus.WriteLong(0x3FFC, 0x0000_0002);

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.A[0] = 0x3000;
		cpu.State.A[1] = 0x4000;
		cpu.State.SetFlag(M68kCpuState.Extend, false);
		cpu.ExecuteInstructions(1, long.MaxValue, NullBoundary.Instance);

		// Result: 1 + 2 = 3 → $0000_0003 at destination $3FFC
		Assert.Equal(0x0000_0003u, bus.ReadLong(0x3FFC));
		var writes = bus.WordWrites.Where(w => w.Address >= 0x3FFC && w.Address <= 0x3FFE).ToList();
		Assert.Equal(2, writes.Count);
		// First write: low word at $3FFE
		Assert.Equal(0x3FFEu, writes[0].Address);
		Assert.Equal(0x0003, writes[0].Value);
		// Second write: high word at $3FFC
		Assert.Equal(0x3FFCu, writes[1].Address);
		Assert.Equal(0x0000, writes[1].Value);
	}

	[Fact]
	public void NormalWriteLongUsesHighWordFirst()
	{
		// MOVE.L D0,(A1) — normal (non-predecrement) long write
		var bus = new WriteRecordingBus();
		bus.WriteWords(CodeBase, 0x2280); // MOVE.L D0,(A1)

		var cpu = new M68kInterpreter(bus);
		cpu.Reset(CodeBase, StackBase);
		cpu.State.D[0] = 0x1234_5678;
		cpu.State.A[1] = 0x3000;
		cpu.ExecuteInstructions(1, long.MaxValue, NullBoundary.Instance);

		var writes = bus.WordWrites.Where(w => w.Address >= 0x3000 && w.Address <= 0x3002).ToList();
		Assert.Equal(2, writes.Count);
		// Normal order: high word at addr first, then low word at addr+2
		Assert.Equal(0x3000u, writes[0].Address);
		Assert.Equal(0x1234, writes[0].Value);
		Assert.Equal(0x3002u, writes[1].Address);
		Assert.Equal(0x5678, writes[1].Value);
	}

	/// <summary>
	/// A test bus that records the order of word-level writes while still
	/// providing normal memory read/write behavior.
	/// </summary>
	private sealed class WriteRecordingBus : IM68kBus, IM68kCodeReader
	{
		private readonly byte[] _memory = new byte[0x0100_0000];
		private readonly List<WordWrite> _writes = new();

		public IReadOnlyList<WordWrite> WordWrites => _writes;

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> _memory[address];

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> (ushort)((_memory[address] << 8) | _memory[address + 1]);

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> ((uint)_memory[address] << 24) | ((uint)_memory[address + 1] << 16) |
			   ((uint)_memory[address + 2] << 8) | _memory[address + 3];

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
			=> _memory[address] = value;

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_writes.Add(new WordWrite(address, value));
			_memory[address] = (byte)(value >> 8);
			_memory[address + 1] = (byte)value;
		}

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			// Default WriteLong: decompose into two word writes (high first)
			// This is only called for non-descending paths.
			WriteWord(address, (ushort)(value >> 16), ref cycle, accessKind);
			WriteWord(address + 2, (ushort)value, ref cycle, accessKind);
		}

		public void WriteLongDescending(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
		{
			// Descending: low word at addr+2 first, then high word at addr.
			WriteWord(address + 2, (ushort)value, ref cycle, accessKind);
			WriteWord(address, (ushort)(value >> 16), ref cycle, accessKind);
		}

		public bool HasHostTrapStub(uint address) => false;

		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state) => false;

		public void ResetExternalDevices(long cycle) { }

		public ushort ReadHostWord(uint address)
			=> (ushort)((_memory[address] << 8) | _memory[address + 1]);

		// Helper methods for test setup (not bus interface)
		public void WriteWords(uint address, params ushort[] words)
		{
			for (var i = 0; i < words.Length; i++)
			{
				var a = address + (uint)(i * 2);
				_memory[a] = (byte)(words[i] >> 8);
				_memory[a + 1] = (byte)words[i];
			}
		}

		public void WriteLong(uint address, uint value)
		{
			_memory[address] = (byte)(value >> 24);
			_memory[address + 1] = (byte)(value >> 16);
			_memory[address + 2] = (byte)(value >> 8);
			_memory[address + 3] = (byte)value;
		}

		public uint ReadLong(uint address)
			=> ((uint)_memory[address] << 24) | ((uint)_memory[address + 1] << 16) |
			   ((uint)_memory[address + 2] << 8) | _memory[address + 3];
	}

	internal readonly record struct WordWrite(uint Address, ushort Value);

	private sealed class NullBoundary : IM68kInstructionBoundary
	{
		public static readonly NullBoundary Instance = new();
		public bool BeforeInstruction() => true;
		public void AfterInstruction(long previousCycle, long currentCycle) { }
	}
}
