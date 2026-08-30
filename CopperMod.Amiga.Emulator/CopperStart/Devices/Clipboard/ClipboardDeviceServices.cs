using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using PortableDevices = CopperStart.Devices;

namespace CopperMod.Amiga.CopperStart.Devices.Clipboard;

/// <summary>Host clipboard conversion and guest-callback adapter for the portable clipboard core.</summary>
internal sealed class ClipboardDeviceServices : IDisposable
{
	private const int AllocationBytes = 0x100, VectorBytes = 42, NameOffset = 0x60;
	private const uint StreamCapacity = 16 * 1024;
	private const int StateOffset = 0xA0;
	private const int UnitAllocationBytes = (int)PortableDevices.ClipboardDeviceCore.UnitStateSize + (int)(StreamCapacity * 2);
	private readonly AmigaBus _bus; private readonly ExecMemoryOperations _memory; private readonly Action<uint> _replyMessage;
	private readonly Action<uint, uint, M68kCpuState> _putMessage; private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine; private readonly uint _hookContinuation;
	private readonly List<(uint Address, uint Token)> _gateways = new(); private readonly List<UnitAllocation> _unitAllocations = new();
	private readonly List<uint> _satisfyMessages = new();
	private readonly ConcurrentQueue<HostClipboardPayload> _pendingHostPayloads = new(); private readonly Queue<HookNotification> _pendingHooks = new();
	private uint _allocation, _execBase, _state, _activeHookMessage; private string? _primaryTextForHost; private ClipboardImage? _primaryImageForHost; private M68kCpuState? _activeState;
	private readonly record struct UnitAllocation(uint State);
	private readonly record struct HookNotification(uint Hook, uint ChangeCommand, uint ClipId);
	private readonly record struct HostClipboardPayload(string? Text, ClipboardImage? Image);

	public ClipboardDeviceServices(AmigaBus bus, ExecMemoryOperations memory, Action<uint> replyMessage,
		Action<uint, uint, M68kCpuState> putMessage, Action<M68kCpuState, uint, uint> startGuestSubroutine, uint hookContinuation)
	{ _bus = bus ?? throw new ArgumentNullException(nameof(bus)); _memory = memory ?? throw new ArgumentNullException(nameof(memory)); _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage)); _putMessage = putMessage ?? throw new ArgumentNullException(nameof(putMessage)); _startGuestSubroutine = startGuestSubroutine ?? throw new ArgumentNullException(nameof(startGuestSubroutine)); _hookContinuation = hookContinuation; }

	public uint DeviceBase { get; private set; } public bool IsInstalled => DeviceBase != 0;
	public void QueuePrimaryTextFromHost(string text) => _pendingHostPayloads.Enqueue(new HostClipboardPayload(text ?? string.Empty, null));
	public void QueuePrimaryImageFromHost(ClipboardImage image) { ArgumentNullException.ThrowIfNull(image); _pendingHostPayloads.Enqueue(new HostClipboardPayload(null, image)); }
	public bool TryTakePrimaryTextForHost(out string text) { var available = _primaryTextForHost is not null; text = _primaryTextForHost ?? string.Empty; _primaryTextForHost = null; return available; }
	public bool TryTakePrimaryImageForHost(out ClipboardImage? image) { image = _primaryImageForHost; _primaryImageForHost = null; return image is not null; }

	public bool TryInstall(uint execBase)
	{
		if (IsInstalled) return true; if (execBase == 0 || !_bus.IsMappedMemoryRange(execBase + (uint)ExecLayout.ExecBase.DeviceList, checked((int)global::Amiga.List.Size))) return false;
		_allocation = _memory.Allocate(AllocationBytes, 0); if (_allocation == 0 || !_bus.IsMappedMemoryRange(_allocation, AllocationBytes)) { Dispose(); return false; } _state = _allocation + StateOffset;
		_memory.Clear(_allocation, AllocationBytes); _execBase = execBase; DeviceBase = _allocation + VectorBytes; var name = _allocation + NameOffset; WriteAscii(name, ClipboardDevice.Name); _bus.WriteByte(DeviceBase + ExecLayout.Node.Type, (byte)NodeType.Device, 0); _bus.WriteLong(DeviceBase + ExecLayout.Node.Name, name, 0); _bus.WriteWord(DeviceBase + ExecLayout.Library.Version, 39, 0); _bus.WriteWord(DeviceBase + ExecLayout.Library.NegativeSize, VectorBytes, 0); _bus.WriteWord(DeviceBase + ExecLayout.Library.PositiveSize, AllocationBytes - VectorBytes, 0); AddTail(execBase + (uint)ExecLayout.ExecBase.DeviceList, DeviceBase);
		var platform = CreatePlatform(0); if (!PortableDevices.ClipboardDeviceCore.Initialize(ref platform, APTR.FromPointer(_state))) { Dispose(); return false; }
		Register(ClipboardDevice.Open, Open); Register(ClipboardDevice.Close, Close); Register(ClipboardDevice.Expunge, Expunge); Register(ClipboardDevice.ExtFunc, ExtFunc); Register(ClipboardDevice.BeginIO, BeginIo); Register(ClipboardDevice.AbortIO, AbortIo); return true;
	}

	public void ProcessPending(M68kCpuState state)
	{
		_activeState = state; try { while (_pendingHostPayloads.TryDequeue(out var payload)) { var unit = GetOrCreateUnit(0); if (unit.IsNull) continue; var bytes = payload.Image is { } image ? ClipboardIffImage.Encode(image) : ClipboardIffText.Encode(payload.Text ?? string.Empty); if (bytes.Length > StreamCapacity) continue; var platform = CreatePlatform(state.Cycles); var staging = PortableDevices.ClipboardDeviceCore.StagingAddress(ref platform, unit); _bus.CopyToMemory(staging.Raw, bytes); PortableDevices.ClipboardDeviceCore.ReplaceCommitted(ref platform, unit, staging, (uint)bytes.Length, (uint)ClipboardCommand.Update); } StartNextHook(state); } finally { _activeState = null; }
	}
	public void ContinueHook(M68kCpuState state) { if (_activeHookMessage != 0) _memory.Free(_activeHookMessage, (int)ClipHookMessage.Size); _activeHookMessage = 0; }
	public void Reset() => Dispose();
	public void Dispose()
	{
		if (_activeHookMessage != 0) _memory.Free(_activeHookMessage, (int)ClipHookMessage.Size); RemoveFromDeviceList(); for (var i = _gateways.Count - 1; i >= 0; i--) _bus.RemoveHostGateway(_gateways[i].Address, _gateways[i].Token); _gateways.Clear(); foreach (var message in _satisfyMessages) _memory.Free(message, (int)SatisfyMessage.Size); _satisfyMessages.Clear(); foreach (var unit in _unitAllocations) _memory.Free(unit.State, UnitAllocationBytes); _unitAllocations.Clear(); if (_allocation != 0) _memory.Free(_allocation, AllocationBytes); _state = _allocation = _execBase = _activeHookMessage = 0; DeviceBase = 0; while (_pendingHostPayloads.TryDequeue(out _)) { } _pendingHooks.Clear(); _primaryTextForHost = null; _primaryImageForHost = null;
	}

	private void Open(M68kCpuState state) { var request = APTR.FromPointer(state.A[1]); var unit = GetOrCreateUnit(state.D[0]); if (unit.IsNull) { state.D[0] = unchecked((uint)(sbyte)IoError.OpenFail); return; } var platform = CreatePlatform(state.Cycles); state.D[0] = PortableDevices.ClipboardDeviceCore.Open(ref platform, APTR.FromPointer(_state), APTR.FromPointer(DeviceBase), request, state.D[0]); }
	private void Close(M68kCpuState state) { var platform = CreatePlatform(state.Cycles); state.D[0] = PortableDevices.ClipboardDeviceCore.Close(ref platform, APTR.FromPointer(DeviceBase), APTR.FromPointer(state.A[1])); }
	private static void Expunge(M68kCpuState state) => state.D[0] = 0; private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;
	private void BeginIo(M68kCpuState state) { _activeState = state; try { var platform = CreatePlatform(state.Cycles); var request = APTR.FromPointer(state.A[1]); var command = (ClipboardCommand)_bus.ReadWord(state.A[1] + (uint)ExecLayout.IORequest.Command); var unit = APTR.FromPointer(_bus.ReadLong(state.A[1] + (uint)ExecLayout.IORequest.Unit)); if (command == ClipboardCommand.Flush) state.D[0] = PortableDevices.ClipboardDeviceCore.FlushCommand(ref platform, unit, request); else if (command == ClipboardCommand.Reset) state.D[0] = PortableDevices.ClipboardDeviceCore.ResetCommand(ref platform, unit, request); else state.D[0] = PortableDevices.ClipboardDeviceCore.BeginIo(ref platform, APTR.FromPointer(DeviceBase), request); } finally { _activeState = null; } }
	private void AbortIo(M68kCpuState state) { var platform = CreatePlatform(state.Cycles); state.D[0] = PortableDevices.ClipboardDeviceCore.AbortIo(ref platform, APTR.FromPointer(state.A[1])); }
	private APTR GetOrCreateUnit(uint number)
	{
		var platform = CreatePlatform(0); var found = PortableDevices.ClipboardDeviceCore.FindUnit(ref platform, APTR.FromPointer(_state), number); if (found.IsNotNull) return found;
		var unit = _memory.Allocate(UnitAllocationBytes, 0); if (unit == 0 || !_bus.IsMappedMemoryRange(unit, UnitAllocationBytes)) return APTR.Null; var committed = unit + PortableDevices.ClipboardDeviceCore.UnitStateSize; var pending = committed + StreamCapacity;
		var pointer = APTR.FromPointer(unit); if (!PortableDevices.ClipboardDeviceCore.InitializeUnit(ref platform, pointer, number, APTR.FromPointer(committed), APTR.FromPointer(pending), StreamCapacity) || !PortableDevices.ClipboardDeviceCore.RegisterUnit(ref platform, APTR.FromPointer(_state), pointer)) { _memory.Free(unit, UnitAllocationBytes); return APTR.Null; } _unitAllocations.Add(new UnitAllocation(unit)); return pointer;
	}
	private AmigaBusClipboardPlatform CreatePlatform(long cycle) => new(_bus, cycle, _replyMessage, SendSatisfy, QueueHook, PublishClipboard);
	private void SendSatisfy(APTR unit, APTR port, uint clipId)
	{
		if (_activeState is null) return; var message = _memory.Allocate((int)SatisfyMessage.Size, 0); if (message == 0 || !_bus.IsMappedMemoryRange(message, (int)SatisfyMessage.Size)) return; _satisfyMessages.Add(message); _memory.Clear(message, (int)SatisfyMessage.Size); _bus.WriteByte(message + ExecLayout.Node.Type, (byte)NodeType.Message, _activeState.Cycles); _bus.WriteWord(message + ExecLayout.Message.Length, 6, _activeState.Cycles); _bus.WriteWord(message + SatisfyMessageLayout.Unit, unchecked((ushort)_bus.ReadLong(unit.Raw + 14)), _activeState.Cycles); _bus.WriteLong(message + SatisfyMessageLayout.ClipId, clipId, _activeState.Cycles); _putMessage(port.Raw, message, _activeState);
	}
	private void QueueHook(APTR hook, uint command, uint clipId) => _pendingHooks.Enqueue(new HookNotification(hook.Raw, command, clipId));
	private void PublishClipboard(APTR unit, APTR data, uint length)
	{
		var platform = CreatePlatform(0); if (PortableDevices.ClipboardDeviceCore.UnitNumber(ref platform, unit) != 0) return; _primaryTextForHost = null; _primaryImageForHost = null; if (length == 0) { _primaryTextForHost = string.Empty; return; } var bytes = new byte[length]; _bus.CopyFromMemory(data.Raw, bytes); if (ClipboardIffImage.TryDecode(bytes, out var image)) _primaryImageForHost = image; else if (ClipboardIffText.TryDecode(bytes, out var text)) _primaryTextForHost = text;
	}
	private void StartNextHook(M68kCpuState state)
	{
		if (_activeHookMessage != 0 || _pendingHooks.Count == 0) return; var notification = _pendingHooks.Dequeue(); if (!_bus.IsMappedMemoryRange(notification.Hook, 12)) return; var entry = _bus.ReadLong(notification.Hook + 8); if (entry == 0 || !_bus.IsCpuPhysicalAddressMapped(entry, 2, AmigaBusAccessKind.CpuInstructionFetch)) return; var message = _memory.Allocate((int)ClipHookMessage.Size, 0); if (message == 0) return; _memory.Clear(message, (int)ClipHookMessage.Size); _bus.WriteLong(message + ClipHookMessageLayout.ChangeCommand, notification.ChangeCommand, state.Cycles); _bus.WriteLong(message + ClipHookMessageLayout.ClipId, notification.ClipId, state.Cycles); _activeHookMessage = message; state.A[0] = notification.Hook; state.A[1] = message; state.A[2] = 0; _startGuestSubroutine(state, entry, _hookContinuation);
	}
	private void Register(int lvo, Action<M68kCpuState> callback) { var address = unchecked((uint)((int)DeviceBase + lvo)); _gateways.Add((address, _bus.RegisterHostGateway(address, callback))); }
	private void AddTail(uint list, uint node) { var previous = _bus.ReadLong(list + (uint)ExecLayout.List.TailPred); _bus.WriteLong(node + (uint)ExecLayout.Node.Successor, list + (uint)ExecLayout.List.Tail, 0); _bus.WriteLong(node + (uint)ExecLayout.Node.Predecessor, previous, 0); _bus.WriteLong(previous + (uint)ExecLayout.Node.Successor, node, 0); _bus.WriteLong(list + (uint)ExecLayout.List.TailPred, node, 0); }
	private void RemoveFromDeviceList() { if (DeviceBase == 0 || !_bus.IsMappedMemoryRange(DeviceBase, 8)) return; var next = _bus.ReadLong(DeviceBase); var previous = _bus.ReadLong(DeviceBase + 4); if (next != 0 && previous != 0) { _bus.WriteLong(previous, next, 0); _bus.WriteLong(next + 4, previous, 0); } }
	private void WriteAscii(uint address, string text) { for (var i = 0; i < text.Length; i++) _bus.WriteByte(address + (uint)i, (byte)text[i], 0); _bus.WriteByte(address + (uint)text.Length, 0, 0); }
}

internal struct AmigaBusClipboardPlatform : PortableDevices.IClipboardDevicePlatform
{
	private readonly AmigaBus _bus; private readonly long _cycle; private readonly Action<uint> _reply; private readonly Action<APTR, APTR, uint> _satisfy; private readonly Action<APTR, uint, uint> _hook; private readonly Action<APTR, APTR, uint> _publish;
	public AmigaBusClipboardPlatform(AmigaBus bus, long cycle, Action<uint> reply, Action<APTR, APTR, uint> satisfy, Action<APTR, uint, uint> hook, Action<APTR, APTR, uint> publish) { _bus = bus; _cycle = cycle; _reply = reply; _satisfy = satisfy; _hook = hook; _publish = publish; }
	public byte ReadUInt8(APTR a, int o = 0) => _bus.ReadByte(a.Raw + (uint)o); public ushort ReadUInt16(APTR a, int o = 0) => _bus.ReadWord(a.Raw + (uint)o); public uint ReadUInt32(APTR a, int o = 0) => _bus.ReadLong(a.Raw + (uint)o); public void WriteUInt8(APTR a, int o, byte v) => _bus.WriteByte(a.Raw + (uint)o, v, _cycle); public void WriteUInt16(APTR a, int o, ushort v) => _bus.WriteWord(a.Raw + (uint)o, v, _cycle); public void WriteUInt32(APTR a, int o, uint v) => _bus.WriteLong(a.Raw + (uint)o, v, _cycle); public void Clear(APTR a, uint n) => _bus.ClearMemory(a.Raw, checked((int)n)); public bool IsMapped(APTR a, uint n) => n <= int.MaxValue && _bus.IsMappedMemoryRange(a.Raw, (int)n); public void Copy(APTR source, APTR destination, uint n) { for (var i = 0u; i < n; i++) _bus.WriteByte(destination.Raw + i, _bus.ReadByte(source.Raw + i), _cycle); } public void ReplyMessage(APTR r) => _reply(r.Raw); public void SendClipboardSatisfy(APTR u, APTR port, uint id) => _satisfy(u, port, id); public void QueueClipboardChangeHook(APTR hook, uint command, uint id) => _hook(hook, command, id); public void PublishClipboard(APTR unit, APTR data, uint length) => _publish(unit, data, length);
}
