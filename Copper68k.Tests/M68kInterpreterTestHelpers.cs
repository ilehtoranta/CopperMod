using Copper68k;

namespace Copper68k.Tests;

internal static class M68kInterpreterTestHelpers
{
	public static void WriteWords(ZeroWaitCodeBus bus, uint address, params ushort[] words)
	{
		for (var i = 0; i < words.Length; i++)
		{
			bus.WriteWord(address + (uint)(i * 2), words[i]);
		}
	}

	public static byte ReadByte(ZeroWaitCodeBus bus, uint address)
	{
		long cycle = 0;
		return bus.ReadByte(address, ref cycle, M68kBusAccessKind.CpuDataRead);
	}
}

internal sealed class ZeroWaitCodeBus : IM68kBus, IM68kCodeReader
{
	private readonly byte[] _memory = new byte[0x0100_0000];

	public int InstructionFetchWords { get; private set; }

	public int WriteMachineDelay { get; init; }

	public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = cycle;
		_ = accessKind;
		return _memory[Normalize(address)];
	}

	public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind accessKind)
	{
		if (accessKind == M68kBusAccessKind.CpuInstructionFetch)
		{
			InstructionFetchWords++;
		}

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
		_ = accessKind;
		_memory[Normalize(address)] = value;
		cycle += WriteMachineDelay;
	}

	public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		WriteWord(address, value);
		cycle += WriteMachineDelay;
	}

	public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind accessKind)
	{
		_ = accessKind;
		WriteLong(address, value);
		cycle += WriteMachineDelay;
	}

	public bool HasHostGateway(uint address)
	{
		_ = address;
		return false;
	}

	public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state)
	{
		_ = instructionProgramCounter;
		_ = token;
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
	{
		var offset = Normalize(address);
		return (ushort)((_memory[offset] << 8) | _memory[Normalize(address + 1)]);
	}

	public uint ReadLong(uint address)
		=> ((uint)ReadWord(address) << 16) | ReadWord(address + 2);

	public void WriteWord(uint address, ushort value)
	{
		var offset = Normalize(address);
		_memory[offset] = (byte)(value >> 8);
		_memory[Normalize(address + 1)] = (byte)value;
	}

	public void WriteLong(uint address, uint value)
	{
		WriteWord(address, (ushort)(value >> 16));
		WriteWord(address + 2, (ushort)value);
	}

	private static int Normalize(uint address)
		=> (int)(address & 0x00FF_FFFF);
}
