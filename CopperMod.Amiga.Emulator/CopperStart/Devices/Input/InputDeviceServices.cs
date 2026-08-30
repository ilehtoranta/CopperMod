using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using PortableDevices = CopperStart.Devices;
using PortableExec = CopperStart.Exec;

namespace CopperMod.Amiga.CopperStart.Devices.Input;

/// <summary>Original-vector continuation and guest-call adapter for portable input.device.</summary>
internal sealed class InputDeviceServices : IDisposable
{
	private const uint NativeBeginIoContinuationAddress = 0x00F0_8700, HostHandlerContinuationAddress = 0x00F0_8710;
	private readonly AmigaBus _bus; private readonly ExecMemoryContext _memory; private readonly Action<APTR> _replyMessage;
	private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine; private readonly Action<uint, uint, bool> _configureKeyRepeat;
	private readonly List<(uint Address, uint Token)> _gateways = new(); private readonly List<ObservedInputEvent> _observedEvents = new();
	private APTR _state; private uint _beginIoAddress, _beginIoToken; private bool _forwardingBeginIo; private M68kCpuState? _activeState;

	public InputDeviceServices(AmigaBus bus, ExecMemoryContext memory, Action<uint> replyMessage,
		Action<M68kCpuState, uint, uint> startGuestSubroutine, Action<uint, uint, bool> configureKeyRepeat)
	{
		_bus = bus ?? throw new ArgumentNullException(nameof(bus)); _memory = memory ?? throw new ArgumentNullException(nameof(memory)); ArgumentNullException.ThrowIfNull(replyMessage); _replyMessage = request => replyMessage(request.Raw);
		_startGuestSubroutine = startGuestSubroutine ?? throw new ArgumentNullException(nameof(startGuestSubroutine)); _configureKeyRepeat = configureKeyRepeat ?? throw new ArgumentNullException(nameof(configureKeyRepeat));
	}

	// Retained for focused host consumers that do not bootstrap Exec allocation.
	public InputDeviceServices(AmigaBus bus, Action<uint> replyMessage,
		Action<M68kCpuState, uint, uint> startGuestSubroutine, Action<uint, uint, bool> configureKeyRepeat)
		: this(bus, CreateCompatibilityMemory(bus), replyMessage, startGuestSubroutine, configureKeyRepeat) { }

	public uint DeviceBase { get; private set; }
	public bool IsInstalled => _beginIoToken != 0;
	public IReadOnlyCollection<uint> KnownNativeHandlers
	{
		get
		{
			var result = new List<uint>(); if (_state.IsNull) return result; var platform = CreatePlatform(); var list = PortableDevices.InputDeviceCore.HandlerListAddress(ref platform, _state); if (list.IsNull) return result;
			var tail = list.Raw + ExecLayout.List.Tail; for (var current = _bus.ReadLong(list.Raw); current != 0 && current != tail && result.Count < 256; current = _bus.ReadLong(current)) result.Add(current); return result;
		}
	}
	public IReadOnlyList<ObservedInputEvent> ObservedWriteEvents => _observedEvents;
	internal int GatewayRegistrationCount => _gateways.Count;
	internal event Action<ObservedInputEvent>? InputEventObserved;
	internal readonly record struct ObservedInputEvent(uint Address, byte Class, byte SubClass, ushort Code, ushort Qualifier, short X, short Y, uint Seconds, uint Microseconds, uint Next);

	public void ProcessPending() { if (_state.IsNull) return; var platform = CreatePlatform(); PortableDevices.InputDeviceCore.ProcessPending(ref platform, _state); }
	public bool TryInstall(uint execBase)
	{
		if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + (uint)ExecLayout.ExecBase.DeviceList, checked((int)global::Amiga.List.Size))) return IsInstalled;
		var device = FindDevice(execBase + (uint)ExecLayout.ExecBase.DeviceList, InputDevice.Name); if (device < 30 || !_bus.IsMappedMemoryRange(device - 30, 30)) return false;
		var allocation = _memory.Allocate((int)PortableDevices.InputDeviceCore.StateSize, (uint)(global::Amiga.Exec.MemoryFlags.Public | global::Amiga.Exec.MemoryFlags.Clear | global::Amiga.Exec.MemoryFlags.NoExpunge)); if (allocation == 0) return false;
		_state = APTR.FromPointer(allocation); var platform = CreatePlatform(); if (!PortableDevices.InputDeviceCore.Initialize(ref platform, _state)) { _memory.Free(allocation, (int)PortableDevices.InputDeviceCore.StateSize); _state = APTR.Null; return false; }
		DeviceBase = device; _beginIoAddress = device + unchecked((uint)InputDevice.BeginIO); RegisterBeginIo(); RegisterAddress(NativeBeginIoContinuationAddress, ContinueNativeBeginIo); RegisterAddress(HostHandlerContinuationAddress, ContinueHostHandler); return true;
	}

	public void Reset() => Dispose();
	public void Dispose()
	{
		for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token); _gateways.Clear();
		if (_state.IsNotNull) _memory.Free(_state.Raw, (int)PortableDevices.InputDeviceCore.StateSize); _state = APTR.Null; _observedEvents.Clear(); _beginIoAddress = _beginIoToken = 0; _forwardingBeginIo = false; _activeState = null; DeviceBase = 0;
	}

	private AmigaBusExecMemoryPlatform CreatePlatform(M68kCpuState? state = null) => _memory.CreateInputPlatform(_replyMessage, _configureKeyRepeat, ObserveEvent, StartHandler, state);
	private void BeginIo(M68kCpuState state)
	{
		if (_forwardingBeginIo || _beginIoToken == 0 || state.A[7] < 4 || state.Cycles < 0 || state.Cycles > uint.MaxValue) { state.D[0] = uint.MaxValue; return; }
		_activeState = state; try { var platform = CreatePlatform(state); if (PortableDevices.InputDeviceCore.BeginIo(ref platform, _state, APTR.FromPointer(state.A[1]), (uint)state.Cycles)) return; } finally { _activeState = null; }
		RemoveBeginIoRegistration(); state.A[7] -= 4; _bus.WriteLong(state.A[7], NativeBeginIoContinuationAddress, state.Cycles); state.ProgramCounter = _beginIoAddress; _forwardingBeginIo = true;
	}
	private void ContinueNativeBeginIo(M68kCpuState state) { var platform = CreatePlatform(state); PortableDevices.InputDeviceCore.ContinueNative(ref platform, _state); if (_forwardingBeginIo) { _forwardingBeginIo = false; RegisterBeginIo(); } }
	private void ContinueHostHandler(M68kCpuState state) { _activeState = state; try { var platform = CreatePlatform(state); PortableDevices.InputDeviceCore.ContinueHandler(ref platform, _state, APTR.FromPointer(state.D[0])); } finally { _activeState = null; } }
	private void StartHandler(APTR code, APTR events, APTR data) { if (_activeState is null) return; _activeState.A[0] = events.Raw; _activeState.A[1] = data.Raw; _activeState.A[2] = code.Raw; _startGuestSubroutine(_activeState, code.Raw, HostHandlerContinuationAddress); }
	private void ObserveEvent(APTR address)
	{
		if (!_bus.IsMappedMemoryRange(address.Raw, checked((int)InputEvent.Size))) return; var observed = new ObservedInputEvent(address.Raw, _bus.ReadByte(address.Raw + InputEventLayout.Class), _bus.ReadByte(address.Raw + InputEventLayout.SubClass), _bus.ReadWord(address.Raw + InputEventLayout.Code), _bus.ReadWord(address.Raw + InputEventLayout.Qualifier), unchecked((short)_bus.ReadWord(address.Raw + InputEventLayout.X)), unchecked((short)_bus.ReadWord(address.Raw + InputEventLayout.Y)), _bus.ReadLong(address.Raw + InputEventLayout.Seconds), _bus.ReadLong(address.Raw + InputEventLayout.Microseconds), _bus.ReadLong(address.Raw + InputEventLayout.NextEvent)); _observedEvents.Add(observed); InputEventObserved?.Invoke(observed);
	}

	private void RegisterBeginIo() { if (_beginIoAddress == 0 || _beginIoToken != 0) return; _beginIoToken = _bus.RegisterHostGateway(_beginIoAddress, BeginIo); _gateways.Add((_beginIoAddress, _beginIoToken)); }
	private void RemoveBeginIoRegistration() { if (_beginIoToken == 0) return; _bus.RemoveHostGateway(_beginIoAddress, _beginIoToken); for (var index = _gateways.Count - 1; index >= 0; index--) if (_gateways[index].Address == _beginIoAddress && _gateways[index].Token == _beginIoToken) { _gateways.RemoveAt(index); break; } _beginIoToken = 0; }
	private void RegisterAddress(uint address, Action<M68kCpuState> callback) => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
	private uint FindDevice(uint list, string name) { var node = _bus.ReadLong(list); for (var count = 0; node != 0 && node != list + ExecLayout.List.Tail && count < 256; count++) { if (!_bus.IsMappedMemoryRange(node, ExecLayout.Node.Name + 4)) return 0; if (string.Equals(ReadName(_bus.ReadLong(node + ExecLayout.Node.Name)), name, StringComparison.OrdinalIgnoreCase)) return node; node = _bus.ReadLong(node); } return 0; }
	private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var character = _bus.ReadByte(address + (uint)length); if (character == 0) break; value[length++] = (char)character; } return new string(value[..length]); }
	private static ExecMemoryContext CreateCompatibilityMemory(AmigaBus bus) => new(
		bus, (_, _) => 0x0007_F000, (_, _) => 0, (_, _) => { }, _ => 0, (_, _, _) => 0, (_, _, _) => { }, _ => 0,
		(_, _, _) => { }, (_, _, _) => { }, (_, _) => { }, () => 0, PortableExec.ExecMemoryAllocatorKind.Classic,
		() => 0, _ => { }, (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { }, _ => { });
}
