using Copper68k;

namespace Copper68k.Tests;

internal sealed class Copper68kTestBus : IM68kBus, IM68kCodeReader
{
	private readonly Dictionary<uint, Action<M68kCpuState>> _hostTrapStubs = new();

	public Copper68kTestBus(int memorySize = 0x0100_0000)
	{
		Memory = new byte[memorySize];
	}

	public byte[] Memory { get; }

	public int ExternalResetCount { get; private set; }

	public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		return Memory[Offset(address, 1)];
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
		Memory[Offset(address, 1)] = value;
	}

	public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		WriteWord(address, value);
	}

	public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		WriteLong(address, value);
	}

	public bool HasHostTrapStub(uint address)
		=> _hostTrapStubs.ContainsKey(address);

	public ushort ReadHostWord(uint address)
		=> ReadWord(address);

	public bool TryInvokeHostTrap(uint instructionProgramCounter, ushort trapId, M68kCpuState state)
	{
		_ = trapId;
		if (!_hostTrapStubs.TryGetValue(instructionProgramCounter, out var handler))
		{
			return false;
		}

		handler(state);
		return true;
	}

	public void ResetExternalDevices(long cycle)
	{
		_ = cycle;
		ExternalResetCount++;
	}

	public void RegisterHostTrapStub(uint address, Action<M68kCpuState> handler)
		=> _hostTrapStubs[address] = handler;

	public void WriteWords(uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			WriteWord(address + (uint)(i * 2), words[i]);
		}
	}

	public ushort ReadWord(uint address)
	{
		var offset = Offset(address, 2);
		return (ushort)((Memory[offset] << 8) | Memory[offset + 1]);
	}

	public uint ReadLong(uint address)
	{
		var offset = Offset(address, 4);
		return ((uint)Memory[offset] << 24) |
			((uint)Memory[offset + 1] << 16) |
			((uint)Memory[offset + 2] << 8) |
			Memory[offset + 3];
	}

	public void WriteWord(uint address, ushort value)
	{
		var offset = Offset(address, 2);
		Memory[offset] = (byte)(value >> 8);
		Memory[offset + 1] = (byte)value;
	}

	public void WriteLong(uint address, uint value)
	{
		var offset = Offset(address, 4);
		Memory[offset] = (byte)(value >> 24);
		Memory[offset + 1] = (byte)(value >> 16);
		Memory[offset + 2] = (byte)(value >> 8);
		Memory[offset + 3] = (byte)value;
	}

	private int Offset(uint address, int byteCount)
	{
		if (address > int.MaxValue || address + byteCount > Memory.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(address), address, "Address is outside test bus memory.");
		}

		return (int)address;
	}
}
