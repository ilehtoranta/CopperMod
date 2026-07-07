using Copper68k;

namespace Copper68k.Tests;

public sealed class M68kAddressBusWidthTests
{
	private const uint CodeAddress = 0x00001000;
	private const uint StackAddress = 0x00002000;
	private const uint WrappedAddress = 0x01000000;

	[Theory]
	[InlineData(M68kCpuModel.M68000, 0x00000000u, 0x5Au)]
	[InlineData(M68kCpuModel.M68010, 0x00000000u, 0x5Au)]
	[InlineData(M68kCpuModel.M68020, WrappedAddress, 0xA5u)]
	[InlineData(M68kCpuModel.M68030, WrappedAddress, 0xA5u)]
	[InlineData(M68kCpuModel.M68040, WrappedAddress, 0xA5u)]
	public void CpuAddressBusWidthControlsAbsoluteLongDataAccess(
		M68kCpuModel model,
		uint expectedDataAddress,
		uint expectedValue)
	{
		var bus = new RecordingBus();
		WriteMoveByteAbsoluteLongToD0(bus, CodeAddress, WrappedAddress);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(CodeAddress, StackAddress);

		cpu.ExecuteInstruction();

		Assert.Equal(expectedDataAddress, bus.LastDataReadAddress);
		Assert.Equal(expectedValue, cpu.State.D[0] & 0xFF);
	}

	private static void WriteMoveByteAbsoluteLongToD0(RecordingBus bus, uint address, uint source)
	{
		bus.WriteWord(address, 0x1039); // MOVE.B (xxx).L,D0
		bus.WriteLong(address + 2, source);
		bus.WriteWord(address + 6, 0x4E71); // NOP guard
	}

	private sealed class RecordingBus : IM68kBus, IM68kCodeReader
	{
		private readonly Dictionary<uint, byte> _memory = new();

		public uint LastDataReadAddress { get; private set; } = uint.MaxValue;

		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			if (accessKind == M68kBusAccessKind.CpuDataRead)
			{
				LastDataReadAddress = address;
				return address switch
				{
					0x00000000 => 0x5A,
					WrappedAddress => 0xA5,
					_ => 0
				};
			}

			return _memory.TryGetValue(address, out var value) ? value : (byte)0;
		}

		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> (ushort)((ReadByte(address, ref cycle, accessKind) << 8) |
				ReadByte(address + 1, ref cycle, accessKind));

		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind accessKind)
			=> ((uint)ReadWord(address, ref cycle, accessKind) << 16) |
				ReadWord(address + 2, ref cycle, accessKind);

		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind accessKind)
		{
			_ = cycle;
			_ = accessKind;
			_memory[address] = value;
		}

		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
			=> WriteWord(address, value);

		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
			=> WriteLong(address, value);

		public bool HasHostTrapStub(uint address)
		{
			_ = address;
			return false;
		}

		public ushort ReadHostWord(uint address)
		{
			var high = _memory.TryGetValue(address, out var highValue) ? highValue : (byte)0;
			var low = _memory.TryGetValue(address + 1, out var lowValue) ? lowValue : (byte)0;
			return (ushort)((high << 8) | low);
		}

		public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
		{
			_ = instructionProgramCounter;
			_ = trapId;
			_ = state;
			return false;
		}

		public void ResetExternalDevices(long cycle)
			=> _ = cycle;

		public void WriteWord(uint address, ushort value)
		{
			_memory[address] = (byte)(value >> 8);
			_memory[address + 1] = (byte)value;
		}

		public void WriteLong(uint address, uint value)
		{
			WriteWord(address, (ushort)(value >> 16));
			WriteWord(address + 2, (ushort)value);
		}
	}
}
