using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using PortableDevices = CopperStart.Devices;

namespace CopperMod.Amiga.CopperStart.Devices.Timer;

/// <summary>Host discovery, gateway, and cycle adapter for the portable timer.device core.</summary>
internal sealed class TimerDeviceServices : IDisposable
{
	private readonly AmigaBus _bus;
	private readonly ExecMemoryContext _memory;
	private readonly Action<APTR> _replyMessage;
	private readonly List<(uint Address, uint Token)> _gateways = new();
	private APTR _state;

	public TimerDeviceServices(AmigaBus bus, ExecMemoryContext memory,
		Action<uint> replyMessage, Action<string> diagnostic)
	{
		_bus = bus ?? throw new ArgumentNullException(nameof(bus));
		_memory = memory ?? throw new ArgumentNullException(nameof(memory));
		ArgumentNullException.ThrowIfNull(replyMessage);
		ArgumentNullException.ThrowIfNull(diagnostic);
		_replyMessage = request => replyMessage(request.Raw);
	}

	public uint DeviceBase { get; private set; }
	public bool IsInstalled => _gateways.Count != 0;

	public bool TryInstall(uint execBase)
	{
		if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(
			execBase + (uint)ExecLayout.ExecBase.DeviceList, checked((int)global::Amiga.List.Size))) return IsInstalled;
		var device = FindDevice(execBase + (uint)ExecLayout.ExecBase.DeviceList, TimerDevice.Name);
		if (device == 0 || device < 60 || !_bus.IsMappedMemoryRange(device - 60, 60)) return false;
		var state = _memory.Allocate((int)PortableDevices.TimerDeviceCore.StateSize,
			(uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear |
			global::Amiga.Exec.MemoryFlags.NoExpunge));
		if (state == 0) return false;
		_state = APTR.FromPointer(state);
		var platform = _memory.CreateTimerPlatform(_replyMessage);
		if (!PortableDevices.TimerDeviceCore.Initialize(ref platform, _state))
		{
			_memory.Free(state, (int)PortableDevices.TimerDeviceCore.StateSize);
			_state = APTR.Null;
			return false;
		}
		DeviceBase = device;
		Register(TimerDevice.Open, Open);
		Register(TimerDevice.Close, Close);
		Register(TimerDevice.Expunge, Expunge);
		Register(TimerDevice.ExtFunc, ExtFunc);
		Register(TimerDevice.BeginIO, BeginIo);
		Register(TimerDevice.AbortIO, AbortIo);
		Register(TimerDevice.ReadEClockLvo, ReadEClock);
		return true;
	}

	public long GetNextDeadline(long currentCycle, long targetCycle)
	{
		if (_state.IsNull) return targetCycle;
		var platform = _memory.CreateTimerPlatform(_replyMessage);
		var deadline = PortableDevices.TimerDeviceCore.GetNextDeadline(ref platform, _state,
			ToCycle(currentCycle), ToCycle(targetCycle));
		return deadline >= long.MaxValue ? long.MaxValue : (long)deadline;
	}

	public void ProcessPending(M68kCpuState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		if (_state.IsNull) return;
		var platform = _memory.CreateTimerPlatform(_replyMessage, state);
		PortableDevices.TimerDeviceCore.ProcessPending(ref platform, _state, ToCycle(state.Cycles));
	}

	public void Reset() => Dispose();

	public void Dispose()
	{
		for (var index = _gateways.Count - 1; index >= 0; index--)
			_bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
		_gateways.Clear();
		if (_state.IsNotNull)
			_memory.Free(_state.Raw, (int)PortableDevices.TimerDeviceCore.StateSize);
		_state = APTR.Null;
		DeviceBase = 0;
	}

	private void Open(M68kCpuState state)
	{
		var platform = _memory.CreateTimerPlatform(_replyMessage, state);
		state.D[0] = PortableDevices.TimerDeviceCore.Open(ref platform, _state,
			APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]), (TimerUnit)state.D[0], ToCycle(state.Cycles));
	}

	private void Close(M68kCpuState state)
	{
		var platform = _memory.CreateTimerPlatform(_replyMessage, state);
		state.D[0] = PortableDevices.TimerDeviceCore.Close(ref platform, _state,
			APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]));
	}

	private static void Expunge(M68kCpuState state) => state.D[0] = 0;
	private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;

	private void BeginIo(M68kCpuState state)
	{
		var platform = _memory.CreateTimerPlatform(_replyMessage, state);
		PortableDevices.TimerDeviceCore.BeginIo(ref platform, _state, APTR.FromPointer(DeviceBase),
			APTR.FromPointer(state.A[1]), ToCycle(state.Cycles));
	}

	private void AbortIo(M68kCpuState state)
	{
		var platform = _memory.CreateTimerPlatform(_replyMessage, state);
		state.D[0] = PortableDevices.TimerDeviceCore.AbortIo(ref platform, _state, APTR.FromPointer(state.A[1]));
	}

	private void ReadEClock(M68kCpuState state)
	{
		var platform = _memory.CreateTimerPlatform(_replyMessage, state);
		state.D[0] = PortableDevices.TimerDeviceCore.ReadEClock(ref platform,
			APTR.FromPointer(state.A[0]), ToCycle(state.Cycles));
	}

	private void Register(int lvo, Action<M68kCpuState> callback)
	{
		var address = unchecked((uint)((int)DeviceBase + lvo));
		_gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
	}

	private uint FindDevice(uint list, string name)
	{
		var node = _bus.ReadLong(list + (uint)ExecLayout.Node.Successor);
		for (var count = 0; node != 0 && node != list + ExecLayout.List.Tail && count < 256; count++)
		{
			if (!_bus.IsMappedMemoryRange(node, ExecLayout.Node.Name + 4)) return 0;
			if (string.Equals(ReadName(_bus.ReadLong(node + ExecLayout.Node.Name)), name,
				StringComparison.OrdinalIgnoreCase)) return node;
			node = _bus.ReadLong(node + (uint)ExecLayout.Node.Successor);
		}
		return 0;
	}

	private string ReadName(uint address)
	{
		Span<char> characters = stackalloc char[64];
		var length = 0;
		while (address != 0 && length < characters.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1))
		{
			var value = _bus.ReadByte(address + (uint)length);
			if (value == 0) break;
			characters[length++] = (char)value;
		}
		return new string(characters[..length]);
	}

	private static ulong ToCycle(long cycle) => cycle <= 0 ? 0 : (ulong)cycle;
}
