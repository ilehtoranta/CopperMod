using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using PortableDevices = CopperStart.Devices;

namespace CopperMod.Amiga.CopperStart.Devices.Gameport;

/// <summary>Host discovery, gateway, and hardware adapter for the portable gameport.device core.</summary>
internal sealed class GameportDeviceServices : IDisposable
{
	private const uint NativeBeginIoContinuationAddress = 0x00F0_8800;
	private readonly AmigaBus _bus;
	private readonly ExecMemoryContext _memory;
	private readonly Action<APTR> _replyMessage;
	private readonly List<(uint Address, uint Token)> _gateways = new();
	private APTR _state;
	private uint _beginIoAddress, _beginIoToken;
	private bool _forwardingBeginIo;

	public GameportDeviceServices(AmigaBus bus, ExecMemoryContext memory, Action<uint> reply)
	{
		_bus = bus ?? throw new ArgumentNullException(nameof(bus));
		_memory = memory ?? throw new ArgumentNullException(nameof(memory));
		ArgumentNullException.ThrowIfNull(reply);
		_replyMessage = request => reply(request.Raw);
	}

	public uint DeviceBase { get; private set; }
	public bool IsInstalled => _gateways.Count != 0;
	internal byte ControllerType(int unit)
	{
		var platform = _memory.CreateGamePortPlatform(_replyMessage);
		return _state.IsNull ? (byte)0 : PortableDevices.GamePortDeviceCore.ControllerType(ref platform, _state, (byte)unit);
	}

	public long GetNextDeadline(long currentCycle, long targetCycle)
	{
		if (_state.IsNull || currentCycle > uint.MaxValue) return targetCycle;
		var platform = _memory.CreateGamePortPlatform(_replyMessage);
		var target = targetCycle < 0 ? 0u : targetCycle > uint.MaxValue ? uint.MaxValue : (uint)targetCycle;
		return PortableDevices.GamePortDeviceCore.GetNextDeadline(ref platform, _state, (uint)Math.Max(0, currentCycle), target);
	}

	public bool TryInstall(uint execBase)
	{
		if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + (uint)ExecLayout.ExecBase.DeviceList, checked((int)global::Amiga.List.Size))) return IsInstalled;
		var device = FindDevice(execBase + (uint)ExecLayout.ExecBase.DeviceList, GamePortDevice.Name);
		if (device < 36 || !_bus.IsMappedMemoryRange(device - 36, 36)) return false;
		var state = _memory.Allocate((int)PortableDevices.GamePortDeviceCore.StateSize,
			(uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear | global::Amiga.Exec.MemoryFlags.NoExpunge));
		if (state == 0) return false;
		_state = APTR.FromPointer(state);
		var platform = _memory.CreateGamePortPlatform(_replyMessage);
		if (!PortableDevices.GamePortDeviceCore.Initialize(ref platform, _state))
		{
			_memory.Free(state, (int)PortableDevices.GamePortDeviceCore.StateSize); _state = APTR.Null; return false;
		}
		DeviceBase = device;
		Register(GamePortDevice.Open, Open); Register(GamePortDevice.Close, Close); Register(GamePortDevice.Expunge, Expunge);
		Register(GamePortDevice.ExtFunc, ExtFunc); Register(GamePortDevice.AbortIO, AbortIo);
		_beginIoAddress = DeviceBase + unchecked((uint)GamePortDevice.BeginIO); RegisterBeginIo(); RegisterAddress(NativeBeginIoContinuationAddress, ContinueNativeBeginIo);
		return true;
	}

	public void ProcessPending(M68kCpuState state)
	{
		if (_state.IsNull || state.Cycles < 0 || state.Cycles > uint.MaxValue) return;
		var platform = _memory.CreateGamePortPlatform(_replyMessage, state);
		PortableDevices.GamePortDeviceCore.ProcessPending(ref platform, _state, (uint)state.Cycles);
	}

	public void Reset() => Dispose();
	public void Dispose()
	{
		for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
		_gateways.Clear();
		if (_state.IsNotNull) _memory.Free(_state.Raw, (int)PortableDevices.GamePortDeviceCore.StateSize);
		_state = APTR.Null; _beginIoAddress = 0; _beginIoToken = 0; _forwardingBeginIo = false; DeviceBase = 0;
	}

	private void Open(M68kCpuState state)
	{
		var platform = _memory.CreateGamePortPlatform(_replyMessage, state);
		state.D[0] = PortableDevices.GamePortDeviceCore.Open(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]), unchecked((int)state.D[0]));
	}
	private void Close(M68kCpuState state) { var platform = _memory.CreateGamePortPlatform(_replyMessage, state); state.D[0] = PortableDevices.GamePortDeviceCore.Close(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1])); }
	private static void Expunge(M68kCpuState state) => state.D[0] = 0;
	private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;

	private void BeginIo(M68kCpuState state)
	{
		if (_forwardingBeginIo || _beginIoToken == 0 || state.Cycles < 0 || state.Cycles > uint.MaxValue) { state.D[0] = uint.MaxValue; return; }
		var platform = _memory.CreateGamePortPlatform(_replyMessage, state);
		if (!PortableDevices.GamePortDeviceCore.BeginIo(ref platform, _state, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1]), (uint)state.Cycles)) ForwardNativeBeginIo(state);
	}

	private void AbortIo(M68kCpuState state) { var platform = _memory.CreateGamePortPlatform(_replyMessage, state); state.D[0] = PortableDevices.GamePortDeviceCore.AbortIo(ref platform, _state, APTR.FromPointer(state.A[1])); }
	private void ForwardNativeBeginIo(M68kCpuState state) { if (state.A[7] < 4) { state.D[0] = uint.MaxValue; return; } RemoveBeginIoRegistration(); state.A[7] -= 4; _bus.WriteLong(state.A[7], NativeBeginIoContinuationAddress, state.Cycles); state.ProgramCounter = _beginIoAddress; _forwardingBeginIo = true; }
	private void ContinueNativeBeginIo(M68kCpuState state) { if (!_forwardingBeginIo) return; _forwardingBeginIo = false; RegisterBeginIo(); }

	private void Register(int lvo, Action<M68kCpuState> callback) { var address = unchecked((uint)((int)DeviceBase + lvo)); _gateways.Add((address, _bus.RegisterHostGateway(address, callback))); }
	private void RegisterAddress(uint address, Action<M68kCpuState> callback) => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
	private void RegisterBeginIo() { if (_beginIoAddress == 0 || _beginIoToken != 0) return; _beginIoToken = _bus.RegisterHostGateway(_beginIoAddress, BeginIo); _gateways.Add((_beginIoAddress, _beginIoToken)); }
	private void RemoveBeginIoRegistration() { if (_beginIoToken == 0) return; _bus.RemoveHostGateway(_beginIoAddress, _beginIoToken); for (var index = _gateways.Count - 1; index >= 0; index--) if (_gateways[index].Address == _beginIoAddress && _gateways[index].Token == _beginIoToken) { _gateways.RemoveAt(index); break; } _beginIoToken = 0; }

	private uint FindDevice(uint list, string name)
	{
		var node = _bus.ReadLong(list + (uint)ExecLayout.Node.Successor);
		for (var count = 0; node != 0 && node != list + ExecLayout.List.Tail && count < 256; count++)
		{
			if (!_bus.IsMappedMemoryRange(node, ExecLayout.Node.Name + 4)) return 0;
			if (string.Equals(ReadName(_bus.ReadLong(node + ExecLayout.Node.Name)), name, StringComparison.OrdinalIgnoreCase)) return node;
			node = _bus.ReadLong(node + (uint)ExecLayout.Node.Successor);
		}
		return 0;
	}
	private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var character = _bus.ReadByte(address + (uint)length); if (character == 0) break; value[length++] = (char)character; } return new string(value[..length]); }
}
