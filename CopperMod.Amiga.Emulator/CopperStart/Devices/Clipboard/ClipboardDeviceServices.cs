using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.CopperStart.Devices.Clipboard;

/// <summary>
/// Guest-visible, host-owned clipboard.device. The device deliberately stores
/// opaque IFF bytes: interpretation and host text conversion belong to the
/// bridge layered on top of this core.
/// </summary>
internal sealed class ClipboardDeviceServices : IDisposable
{
    private const int DeviceListOffset = 0x15E;
    private const int NodeNameOffset = 0x0A;
    private const int LibraryOpenCountOffset = 0x20;
    private const int IoDeviceOffset = 0x14;
    private const int IoUnitOffset = 0x18;
    private const int IoCommandOffset = 0x1C;
    private const int IoFlagsOffset = 0x1E;
    private const int IoErrorOffset = 0x1F;
    private const int IoActualOffset = 0x20;
    private const int IoLengthOffset = 0x24;
    private const int IoDataOffset = 0x28;
    private const int IoOffsetOffset = 0x2C;
    private const int IoClipIdOffset = 0x30;
    private const byte IoQuick = 0x01;
    private const byte IoErrOpenFail = 0xFF;
    private const byte IoErrAborted = 0xFE;
    private const byte IoErrNoCommand = 0xFD;
    private const byte IoErrBadAddress = 0xFC;
    private const byte CbErrObsoleteId = 1;
    private const ushort CmdReset = 1;
    private const ushort CmdRead = 2;
    private const ushort CmdWrite = 3;
    private const ushort CmdUpdate = 4;
    private const ushort CmdClear = 5;
    private const ushort CmdStop = 6;
    private const ushort CmdStart = 7;
    private const ushort CmdFlush = 8;
    private const ushort CbdPost = 9;
    private const ushort CbdCurrentReadId = 10;
    private const ushort CbdCurrentWriteId = 11;
    private const ushort CbdChangeHook = 12;
    private const int AllocationBytes = 0x100;
    private const int UnitBytes = 0x20;
    private const int VectorBytes = 42;
    private const int NameOffset = 0x60;

    private readonly AmigaBus _bus;
    private readonly ExecMemoryOperations _memory;
    private readonly Action<uint> _replyMessage;
    private readonly Action<uint, uint, M68kCpuState> _putMessage;
    private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
    private readonly uint _hookContinuation;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Dictionary<uint, ClipboardUnit> _units = new();
    private readonly Dictionary<uint, ClipboardUnit> _unitsByAddress = new();
    private readonly ConcurrentQueue<HostClipboardPayload> _pendingHostPayloads = new();
    private uint _allocation;
    private uint _execBase;
    private string? _primaryTextForHost;
    private ClipboardImage? _primaryImageForHost;
    private readonly Queue<HookNotification> _pendingHooks = new();
    private uint _activeHookMessage;

    private sealed class ClipboardUnit
    {
        public uint GuestAddress;
        public uint PostPort;
        public uint PostId;
        public uint SatisfyMessage;
        public bool SatisfySent;
        public readonly List<uint> PendingReads = new();
        public readonly List<uint> DeferredRequests = new();
        public readonly HashSet<uint> ChangeHooks = new();
        public uint ReadId = 1;
        public uint WriteId = 1;
        public byte[] Committed = Array.Empty<byte>();
        public byte[] Pending = Array.Empty<byte>();
        public bool HasPendingWrite;
        public bool Stopped;
    }
    private readonly record struct HookNotification(uint Hook, uint ChangeCommand, uint ClipId);
    private readonly record struct HostClipboardPayload(string? Text, ClipboardImage? Image);

    public ClipboardDeviceServices(AmigaBus bus, ExecMemoryOperations memory, Action<uint> replyMessage, Action<uint, uint, M68kCpuState> putMessage,
        Action<M68kCpuState, uint, uint> startGuestSubroutine, uint hookContinuation)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
        _putMessage = putMessage ?? throw new ArgumentNullException(nameof(putMessage));
        _startGuestSubroutine = startGuestSubroutine ?? throw new ArgumentNullException(nameof(startGuestSubroutine));
        _hookContinuation = hookContinuation;
    }

    public uint DeviceBase { get; private set; }
    public bool IsInstalled => DeviceBase != 0;

    /// <summary>Queues text supplied by the host UI; guest state changes only at a boundary.</summary>
    public void QueuePrimaryTextFromHost(string text) => _pendingHostPayloads.Enqueue(new HostClipboardPayload(text ?? string.Empty, null));

    /// <summary>Queues a primary-unit image supplied by the host UI.</summary>
    public void QueuePrimaryImageFromHost(ClipboardImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _pendingHostPayloads.Enqueue(new HostClipboardPayload(null, image));
    }

    /// <summary>Returns one primary-unit update to be published by the host UI.</summary>
    public bool TryTakePrimaryTextForHost(out string text)
    {
        var available = _primaryTextForHost is not null;
        text = _primaryTextForHost ?? string.Empty;
        _primaryTextForHost = null;
        return available;
    }

    /// <summary>Returns one primary-unit ILBM update decoded for the host UI.</summary>
    public bool TryTakePrimaryImageForHost(out ClipboardImage? image)
    {
        image = _primaryImageForHost;
        _primaryImageForHost = null;
        return image is not null;
    }

    /// <summary>Applies host input at an outer emulator boundary.</summary>
    public void ProcessPending(M68kCpuState state)
    {
        while (_pendingHostPayloads.TryDequeue(out var payload))
        {
            var unit = GetUnit(0);
            unit.Committed = payload.Image is { } image ? ClipboardIffImage.Encode(image) : ClipboardIffText.Encode(payload.Text ?? string.Empty);
            unit.Pending = Array.Empty<byte>(); unit.HasPendingWrite = false;
            AdvanceClipIds(unit);
            QueueChangeHooks(unit, CmdUpdate);
        }
        foreach (var unit in _units.Values)
            if (!unit.Stopped) ProcessDeferredRequests(unit, state);
        StartNextHook(state);
    }

    public void ContinueHook(M68kCpuState state)
    {
        if (_activeHookMessage != 0) _memory.Free(_activeHookMessage, 12);
        _activeHookMessage = 0;
    }

    /// <summary>Creates and links the one CopperStart-owned device once live Exec exists.</summary>
    public bool TryInstall(uint execBase)
    {
        if (IsInstalled) return true;
        if (execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14)) return false;

        _allocation = _memory.Allocate(AllocationBytes, 0);
        if (_allocation == 0 || !_bus.IsMappedMemoryRange(_allocation, AllocationBytes))
        {
            _allocation = 0;
            return false;
        }

        _memory.Clear(_allocation, AllocationBytes);
        _execBase = execBase;
        DeviceBase = _allocation + VectorBytes;
        var name = _allocation + NameOffset;
        WriteAscii(name, "clipboard.device");
        _bus.WriteByte(DeviceBase + 8, 3, 0); // NT_DEVICE
        _bus.WriteLong(DeviceBase + NodeNameOffset, name, 0);
        _bus.WriteWord(DeviceBase + 0x14, 39, 0); // KS 3.1-compatible version
        _bus.WriteWord(DeviceBase + 0x1A, VectorBytes, 0);
        _bus.WriteWord(DeviceBase + 0x1C, AllocationBytes - VectorBytes, 0);
        AddTail(execBase + DeviceListOffset, DeviceBase);
        Register(-6, Open); Register(-12, Close); Register(-18, Expunge);
        Register(-24, ExtFunc); Register(-30, BeginIo); Register(-36, AbortIo);
        return true;
    }

    public void Reset() => Dispose();

    public void Dispose()
    {
        RemoveFromDeviceList();
        for (var index = _gateways.Count - 1; index >= 0; index--)
            _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        _gateways.Clear();
        if (_allocation != 0)
            _memory.Free(_allocation, AllocationBytes);
        _allocation = 0;
        _execBase = 0;
        DeviceBase = 0;
        foreach (var unit in _units.Values)
        {
            if (unit.GuestAddress != 0) _memory.Free(unit.GuestAddress, UnitBytes);
            if (unit.SatisfyMessage != 0) _memory.Free(unit.SatisfyMessage, 0x1C);
        }
        _units.Clear();
        _unitsByAddress.Clear();
        while (_pendingHostPayloads.TryDequeue(out _)) { }
        _primaryTextForHost = null;
        _primaryImageForHost = null;
        while (_pendingHooks.Count != 0) _pendingHooks.Dequeue();
        _activeHookMessage = 0;
    }

    private void Open(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request, IoClipIdOffset + 4))
        {
            Complete(request, IoErrOpenFail, state.Cycles, false); state.D[0] = IoErrOpenFail; return;
        }
        var unit = GetOrCreateUnit(state.D[0]);
        if (unit is null)
        {
            Complete(request, IoErrOpenFail, state.Cycles, false); state.D[0] = IoErrOpenFail; return;
        }
        _bus.WriteLong(request + IoDeviceOffset, DeviceBase, state.Cycles);
        _bus.WriteLong(request + IoUnitOffset, unit.GuestAddress, state.Cycles);
        _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(_bus.ReadWord(DeviceBase + LibraryOpenCountOffset) + 1)), state.Cycles);
        Complete(request, 0, state.Cycles, false); state.D[0] = 0;
    }

    private void Close(M68kCpuState state)
    {
        if (DeviceBase != 0)
        {
            var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset);
            if (count != 0) _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(count - 1)), state.Cycles);
        }
        state.D[0] = 0;
    }

    private static void Expunge(M68kCpuState state) => state.D[0] = 0;
    private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;
    private void AbortIo(M68kCpuState state)
    {
        var request = state.A[1];
        foreach (var unit in _units.Values)
        {
            if (unit.PendingReads.Remove(request))
            {
                Complete(request, IoErrAborted, state.Cycles, true);
                state.D[0] = 0;
                return;
            }
            if (unit.DeferredRequests.Remove(request))
            {
                Complete(request, IoErrAborted, state.Cycles, true);
                state.D[0] = 0;
                return;
            }
        }
        state.D[0] = 0;
    }

    private void BeginIo(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request, IoClipIdOffset + 4) || _bus.ReadLong(request + IoDeviceOffset) != DeviceBase) return;
        if (!_unitsByAddress.TryGetValue(_bus.ReadLong(request + IoUnitOffset), out var unit))
        {
            Complete(request, IoErrBadAddress, state.Cycles, (_bus.ReadByte(request + IoFlagsOffset) & IoQuick) == 0);
            return;
        }
        ProcessRequest(request, unit, state);
    }

    private void ProcessRequest(uint request, ClipboardUnit unit, M68kCpuState state)
    {
        var command = _bus.ReadWord(request + IoCommandOffset);
        if (unit.Stopped && command is not CmdStart and not CmdReset and not CmdFlush)
        {
            if (!unit.DeferredRequests.Contains(request)) unit.DeferredRequests.Add(request);
            return;
        }
        byte error;
        switch (command)
        {
            case CmdReset:
                FlushPendingRequests(unit, state, request);
                unit.Stopped = false; unit.Pending = Array.Empty<byte>(); unit.HasPendingWrite = false;
                unit.PostPort = 0; unit.PostId = 0; unit.SatisfySent = false;
                error = 0;
                break;
            case CmdRead:
                error = Read(request, unit, state);
                if (unit.PendingReads.Contains(request)) return;
                break;
            case CmdWrite: error = Write(request, unit, state.Cycles); break;
            case CmdUpdate: error = Update(request, unit, state.Cycles); break;
            case CmdClear: error = Clear(unit, state); break;
            case CmdStop: unit.Stopped = true; error = 0; break;
            case CmdStart:
                unit.Stopped = false;
                error = 0;
                break;
            case CmdFlush:
                FlushPendingRequests(unit, state, request);
                error = 0;
                break;
            case CbdCurrentReadId: _bus.WriteLong(request + IoClipIdOffset, unit.ReadId, state.Cycles); error = 0; break;
            case CbdCurrentWriteId: _bus.WriteLong(request + IoClipIdOffset, unit.WriteId, state.Cycles); error = 0; break;
            case CbdPost: error = Post(request, unit, state.Cycles); break;
            case CbdChangeHook: error = ChangeHook(request, unit); break;
            default: error = IoErrNoCommand; break;
        }
        Complete(request, error, state.Cycles, (_bus.ReadByte(request + IoFlagsOffset) & IoQuick) == 0);
        if (command == CmdStart) ProcessDeferredRequests(unit, state);
    }

    private byte Clear(ClipboardUnit unit, M68kCpuState state)
    {
        unit.Committed = Array.Empty<byte>(); unit.Pending = Array.Empty<byte>(); unit.HasPendingWrite = false;
        unit.PostPort = 0; unit.PostId = 0; unit.SatisfySent = false;
        AdvanceClipIds(unit);
        QueuePrimaryTextForHost(unit); QueueChangeHooks(unit, CmdUpdate);
        CompletePendingReads(unit, state.Cycles);
        return 0;
    }

    private void FlushPendingRequests(ClipboardUnit unit, M68kCpuState state, uint exclude)
    {
        for (var index = unit.PendingReads.Count - 1; index >= 0; index--)
        {
            var request = unit.PendingReads[index];
            if (request == exclude) continue;
            unit.PendingReads.RemoveAt(index);
            Complete(request, IoErrAborted, state.Cycles, true);
        }
        for (var index = unit.DeferredRequests.Count - 1; index >= 0; index--)
        {
            var request = unit.DeferredRequests[index];
            if (request == exclude) continue;
            unit.DeferredRequests.RemoveAt(index);
            Complete(request, IoErrAborted, state.Cycles, true);
        }
    }

    private void ProcessDeferredRequests(ClipboardUnit unit, M68kCpuState state)
    {
        while (!unit.Stopped && unit.DeferredRequests.Count != 0)
        {
            var request = unit.DeferredRequests[0];
            unit.DeferredRequests.RemoveAt(0);
            if (!_bus.IsMappedMemoryRange(request, IoClipIdOffset + 4)) continue;
            ProcessRequest(request, unit, state);
        }
    }

    private byte Read(uint request, ClipboardUnit unit, M68kCpuState state)
    {
        var id = _bus.ReadLong(request + IoClipIdOffset);
        if (id != 0 && id != unit.ReadId) return CbErrObsoleteId;
        _bus.WriteLong(request + IoClipIdOffset, unit.ReadId, state.Cycles);
        if (unit.PostPort != 0)
        {
            SendSatisfyMessage(unit, state);
            if (!unit.PendingReads.Contains(request)) unit.PendingReads.Add(request);
            return 0;
        }
        return TransferToGuest(request, unit.Committed, state.Cycles);
    }

    private byte Write(uint request, ClipboardUnit unit, long cycle)
    {
        var id = _bus.ReadLong(request + IoClipIdOffset);
        if (id == 0)
        {
            AdvanceWriteId(unit);
            unit.Pending = Array.Empty<byte>(); unit.HasPendingWrite = true;
            id = unit.WriteId;
            _bus.WriteLong(request + IoClipIdOffset, id, cycle);
        }
        if (!unit.HasPendingWrite || id != unit.WriteId) return CbErrObsoleteId;
        var length = _bus.ReadLong(request + IoLengthOffset); var data = _bus.ReadLong(request + IoDataOffset); var offset = _bus.ReadLong(request + IoOffsetOffset);
        if (length != 0 && (data == 0 || !_bus.IsMappedMemoryRange(data, unchecked((int)length))) || offset > int.MaxValue || length > int.MaxValue) return IoErrBadAddress;
        var end = (long)offset + length; if (end > int.MaxValue) return IoErrBadAddress;
        if (unit.Pending.Length < end) Array.Resize(ref unit.Pending, (int)end);
        for (var index = 0u; index < length; index++) unit.Pending[(int)(offset + index)] = _bus.ReadByte(data + index);
        _bus.WriteLong(request + IoActualOffset, length, cycle); return 0;
    }

    private byte Update(uint request, ClipboardUnit unit, long cycle)
    {
        var id = _bus.ReadLong(request + IoClipIdOffset);
        if (!unit.HasPendingWrite || (id != 0 && id != unit.WriteId)) return CbErrObsoleteId;
        unit.Committed = unit.Pending; unit.Pending = Array.Empty<byte>(); unit.HasPendingWrite = false;
        unit.ReadId = unit.WriteId; _bus.WriteLong(request + IoClipIdOffset, unit.ReadId, cycle);
        QueuePrimaryTextForHost(unit);
        QueueChangeHooks(unit, CmdUpdate);
        unit.PostPort = 0; unit.PostId = 0; unit.SatisfySent = false;
        CompletePendingReads(unit, cycle);
        return 0;
    }

    private byte Post(uint request, ClipboardUnit unit, long cycle)
    {
        var requestedId = _bus.ReadLong(request + IoClipIdOffset);
        var port = _bus.ReadLong(request + IoDataOffset);
        if (port == 0 || !_bus.IsMappedMemoryRange(port, 0x20)) return IoErrBadAddress;
        if (requestedId != 0 && requestedId != unit.WriteId) return CbErrObsoleteId;
        AdvanceWriteId(unit);
        unit.ReadId = unit.WriteId; unit.Pending = Array.Empty<byte>(); unit.HasPendingWrite = true;
        unit.PostPort = port; unit.PostId = unit.WriteId; unit.SatisfySent = false;
        _bus.WriteLong(request + IoClipIdOffset, unit.PostId, cycle);
        QueueChangeHooks(unit, CbdPost);
        return 0;
    }

    private byte ChangeHook(uint request, ClipboardUnit unit)
    {
        var hook = _bus.ReadLong(request + IoDataOffset);
        if (hook == 0 || !_bus.IsMappedMemoryRange(hook, 12)) return IoErrBadAddress;
        if (_bus.ReadLong(request + IoLengthOffset) != 0) unit.ChangeHooks.Add(hook);
        else unit.ChangeHooks.Remove(hook);
        return 0;
    }

    private void SendSatisfyMessage(ClipboardUnit unit, M68kCpuState state)
    {
        if (unit.SatisfySent || unit.PostPort == 0) return;
        if (unit.SatisfyMessage == 0)
        {
            unit.SatisfyMessage = _memory.Allocate(0x1C, 0);
            if (unit.SatisfyMessage == 0 || !_bus.IsMappedMemoryRange(unit.SatisfyMessage, 0x1C)) { unit.SatisfyMessage = 0; return; }
            _memory.Clear(unit.SatisfyMessage, 0x1C);
        }
        var message = unit.SatisfyMessage;
        _bus.WriteByte(message + 8, 5, state.Cycles); // NT_MESSAGE
        _bus.WriteWord(message + 0x12, 6, state.Cycles);
        _bus.WriteWord(message + 0x14, unchecked((ushort)_bus.ReadLong(unit.GuestAddress + 0x0E)), state.Cycles);
        _bus.WriteLong(message + 0x18, unit.PostId, state.Cycles);
        _putMessage(unit.PostPort, message, state); unit.SatisfySent = true;
    }

    private void CompletePendingReads(ClipboardUnit unit, long cycle)
    {
        foreach (var request in unit.PendingReads)
        {
            _bus.WriteLong(request + IoClipIdOffset, unit.ReadId, cycle);
            Complete(request, TransferToGuest(request, unit.Committed, cycle), cycle, true);
        }
        unit.PendingReads.Clear();
    }

    private void QueueChangeHooks(ClipboardUnit unit, uint command)
    {
        foreach (var hook in unit.ChangeHooks) _pendingHooks.Enqueue(new HookNotification(hook, command, unit.ReadId));
    }

    private void StartNextHook(M68kCpuState state)
    {
        if (_activeHookMessage != 0 || _pendingHooks.Count == 0) return;
        var notification = _pendingHooks.Dequeue();
        if (!_bus.IsMappedMemoryRange(notification.Hook, 12)) return;
        // CBD_CHANGEHOOK receives a utility.library struct Hook. Its
        // h_Entry follows the two-pointer MinNode at offset +8.
        var entry = _bus.ReadLong(notification.Hook + 8);
        if (entry == 0 || !_bus.IsCpuPhysicalAddressMapped(entry, 2, AmigaBusAccessKind.CpuInstructionFetch)) return;
        var message = _memory.Allocate(12, 0);
        if (message == 0 || !_bus.IsMappedMemoryRange(message, 12)) return;
        _memory.Clear(message, 12);
        _bus.WriteLong(message + 4, notification.ChangeCommand, state.Cycles);
        _bus.WriteLong(message + 8, notification.ClipId, state.Cycles);
        _activeHookMessage = message;
        state.A[0] = notification.Hook; state.A[1] = message; state.A[2] = 0;
        _startGuestSubroutine(state, entry, _hookContinuation);
    }

    private byte TransferToGuest(uint request, byte[] source, long cycle)
    {
        var length = _bus.ReadLong(request + IoLengthOffset); var data = _bus.ReadLong(request + IoDataOffset); var offset = _bus.ReadLong(request + IoOffsetOffset);
        if (length != 0 && (data == 0 || !_bus.IsMappedMemoryRange(data, unchecked((int)length))) || offset > int.MaxValue || length > int.MaxValue) return IoErrBadAddress;
        var available = offset >= source.Length ? 0 : Math.Min((uint)(source.Length - offset), length);
        for (var index = 0u; index < available; index++) _bus.WriteByte(data + index, source[(int)(offset + index)], cycle);
        _bus.WriteLong(request + IoActualOffset, available, cycle); return 0;
    }

    private ClipboardUnit? GetOrCreateUnit(uint number)
    {
        if (_units.TryGetValue(number, out var unit)) return unit;
        var address = _memory.Allocate(UnitBytes, 0);
        if (address == 0 || !_bus.IsMappedMemoryRange(address, UnitBytes)) return null;
        _memory.Clear(address, UnitBytes);
        _bus.WriteLong(address + 0x0E, number, 0);
        unit = new ClipboardUnit { GuestAddress = address };
        _units.Add(number, unit); _unitsByAddress.Add(address, unit);
        return unit;
    }
    private ClipboardUnit GetUnit(uint number) => GetOrCreateUnit(number) ?? throw new InvalidOperationException("Unable to allocate clipboard unit.");
    private static void AdvanceWriteId(ClipboardUnit unit) { unit.WriteId++; if (unit.WriteId == 0) unit.WriteId = 1; }
    private static void AdvanceClipIds(ClipboardUnit unit) { AdvanceWriteId(unit); unit.ReadId = unit.WriteId; }
    private void QueuePrimaryTextForHost(ClipboardUnit unit)
    {
        if (!_units.TryGetValue(0, out var primary) || !ReferenceEquals(primary, unit)) return;
        _primaryTextForHost = null; _primaryImageForHost = null;
        if (ClipboardIffImage.TryDecode(unit.Committed, out var image)) _primaryImageForHost = image;
        else if (ClipboardIffText.TryDecode(unit.Committed, out var text)) _primaryTextForHost = text;
    }
    private void Complete(uint request, byte error, long cycle, bool reply)
    {
        if (request == 0 || !_bus.IsMappedMemoryRange(request, IoErrorOffset + 1)) return;
        _bus.WriteByte(request + IoErrorOffset, error, cycle);
        if (reply) _replyMessage(request);
    }
    private void Register(int lvo, Action<M68kCpuState> callback)
    {
        var address = unchecked((uint)((int)DeviceBase + lvo)); _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
    }
    private void AddTail(uint list, uint node)
    {
        var tail = list + 4; var predecessor = _bus.ReadLong(list + 8);
        if (predecessor == 0 || !_bus.IsMappedMemoryRange(predecessor, 8)) predecessor = list;
        _bus.WriteLong(node, tail, 0); _bus.WriteLong(node + 4, predecessor, 0);
        _bus.WriteLong(predecessor, node, 0); _bus.WriteLong(list + 8, node, 0);
    }
    private void RemoveFromDeviceList()
    {
        if (DeviceBase == 0 || _execBase == 0 || !_bus.IsMappedMemoryRange(DeviceBase, 8)) return;
        var predecessor = _bus.ReadLong(DeviceBase + 4); var successor = _bus.ReadLong(DeviceBase);
        if (predecessor == 0 || successor == 0 || !_bus.IsMappedMemoryRange(predecessor, 8) || !_bus.IsMappedMemoryRange(successor, 8)) return;
        _bus.WriteLong(predecessor, successor, 0); _bus.WriteLong(successor + 4, predecessor, 0);
        if (successor == _execBase + DeviceListOffset + 4)
            _bus.WriteLong(_execBase + DeviceListOffset + 8, predecessor, 0);
        _bus.WriteLong(DeviceBase, 0, 0); _bus.WriteLong(DeviceBase + 4, 0, 0);
    }
    private void WriteAscii(uint address, string text)
    {
        for (var index = 0; index < text.Length; index++) _bus.WriteByte(address + (uint)index, (byte)text[index], 0);
        _bus.WriteByte(address + (uint)text.Length, 0, 0);
    }
}
