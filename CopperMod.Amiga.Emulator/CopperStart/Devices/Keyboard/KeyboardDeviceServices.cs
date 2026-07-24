using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.Input;

namespace CopperMod.Amiga.CopperStart.Devices.Keyboard;

/// <summary>
/// Host-keyboard front end for the ROM-created keyboard.device. Host keys are
/// mapped by CopperScreen before reaching this service; this class deals only
/// in Amiga raw key codes and forwards them to the native input.device chain.
/// </summary>
internal sealed class KeyboardDeviceServices : IDisposable
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
    private const ushort KbdReadEvent = 9;
    private const ushort KbdReadMatrix = 10;
    private const ushort KbdAddResetHandler = 11;
    private const ushort KbdRemResetHandler = 12;
    private const ushort KbdResetHandlerDone = 13;
    private const ushort IndWriteEvent = 11;
    private const byte IoQuick = 0x01;
    private const byte IoErrOpenFail = 0xFF;
    private const byte IoErrAborted = 0xFE;
    private const byte IoErrNoCommand = 0xFD;
    private const byte IoErrBadAddress = 0xFC;
    private const uint MemfPublicClear = 0x0001_0001;
    private const int RequestBytes = 0x30;
    private const int EventOffset = RequestBytes;
    private const int EventBytes = 0x18;
    private const uint InputContinuationAddress = 0x00F0_8600;

    private readonly AmigaBus _bus;
    private readonly ExecMemoryOperations _memory;
    private readonly Action<uint> _replyMessage;
    private readonly Action<string> _diagnostic;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Queue<byte> _readEvents = new();
    private readonly Queue<byte> _inputEvents = new();
    private readonly HashSet<byte> _pressed = new();
    private readonly HashSet<uint> _resetHandlers = new();
    private readonly Func<uint> _getCurrentTask;
    private readonly List<PendingRead> _pendingReads = new();
    private uint _nativeInputTask;
    private uint _scratch;
    private bool _inputDispatchActive;
    private readonly record struct PendingRead(uint Request, uint Task);

    public KeyboardDeviceServices(AmigaBus bus, ExecMemoryOperations memory, Action<uint> replyMessage, Action<string> diagnostic, Func<uint> getCurrentTask)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        _getCurrentTask = getCurrentTask ?? throw new ArgumentNullException(nameof(getCurrentTask));
    }

    public uint DeviceBase { get; private set; }
    public uint InputDeviceBase { get; private set; }
    public bool IsInstalled => _gateways.Count != 0;

    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14)) return IsInstalled;
        var keyboard = FindDevice(execBase + DeviceListOffset, "keyboard.device");
        var input = FindDevice(execBase + DeviceListOffset, "input.device");
        if (keyboard < 36 || input < 30 || !_bus.IsMappedMemoryRange(keyboard - 36, 36) || !_bus.IsMappedMemoryRange(input - 30, 30)) return false;
        var scratch = _memory.Allocate(RequestBytes + EventBytes, MemfPublicClear);
        if (scratch == 0) return false;

        DeviceBase = keyboard;
        InputDeviceBase = input;
        _scratch = scratch;
        Register(-6, Open); Register(-12, Close); Register(-18, Expunge); Register(-24, ExtFunc);
        Register(-30, BeginIo); Register(-36, AbortIo);
        RegisterAddress(InputContinuationAddress, ContinueNativeInput);
        return true;
    }

    public bool QueueKeyDown(AmigaRawKey key)
    {
        if (!IsInstalled) return false;
        var raw = (byte)key;
        if (raw >= 0x80 || !_pressed.Add(raw)) return true;
        Enqueue(raw);
        return true;
    }

    public bool QueueKeyUp(AmigaRawKey key)
    {
        if (!IsInstalled) return false;
        var raw = (byte)key;
        if (raw >= 0x80 || !_pressed.Remove(raw)) return true;
        Enqueue((byte)(raw | 0x80));
        return true;
    }

    public void ProcessPending(M68kCpuState state)
    {
        if (!IsInstalled) return;
        CompletePendingRead(state.Cycles);
        if (_inputDispatchActive || _inputEvents.Count == 0) return;
        StartNativeInput(state, _inputEvents.Dequeue());
    }

    public void Reset() => Dispose();

    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        _gateways.Clear();
        if (_scratch != 0) _memory.Free(_scratch, RequestBytes + EventBytes);
        _scratch = 0; DeviceBase = 0; InputDeviceBase = 0; _nativeInputTask = 0; _inputDispatchActive = false;
        _pendingReads.Clear(); _readEvents.Clear(); _inputEvents.Clear(); _pressed.Clear(); _resetHandlers.Clear();
    }

    private void Open(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || state.D[0] != 0 || !_bus.IsMappedMemoryRange(request, IoDataOffset + 4)) { Complete(request, IoErrOpenFail, 0, state.Cycles, reply: false); state.D[0] = IoErrOpenFail; return; }
        _bus.WriteLong(request + IoDeviceOffset, DeviceBase, state.Cycles); _bus.WriteLong(request + IoUnitOffset, 0, state.Cycles);
        var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(count + 1)), state.Cycles);
        Complete(request, 0, 0, state.Cycles, reply: false); state.D[0] = 0;
    }

    private void Close(M68kCpuState state)
    {
        if (DeviceBase != 0) { var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); if (count != 0) _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(count - 1)), state.Cycles); }
        state.D[0] = 0;
    }
    private static void Expunge(M68kCpuState state) => state.D[0] = 0;
    private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;

    private void BeginIo(M68kCpuState state)
    {
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request, IoDataOffset + 4) || _bus.ReadLong(request + IoDeviceOffset) != DeviceBase) return;
        var command = _bus.ReadWord(request + IoCommandOffset);
        switch (command)
        {
            case KbdReadEvent:
                ReadEvent(request, state.Cycles);
                break;
            case KbdReadMatrix: WriteMatrix(request, state.Cycles); break;
            case KbdAddResetHandler: AddResetHandler(request, state.Cycles); break;
            case KbdRemResetHandler: RemoveResetHandler(request, state.Cycles); break;
            case KbdResetHandlerDone: Complete(request, 0, 0, state.Cycles, reply: true); break;
            default: _diagnostic($"keyboard.device unsupported command {command} at IORequest 0x{request:X8}."); Complete(request, IoErrNoCommand, 0, state.Cycles, reply: true); break;
        }
    }

    private void AbortIo(M68kCpuState state)
    {
        var request = state.A[1];
        for (var index = 0; index < _pendingReads.Count; index++)
        {
            if (_pendingReads[index].Request != request) continue;
            _pendingReads.RemoveAt(index); Complete(request, IoErrAborted, 0, state.Cycles, reply: true); state.D[0] = 0; return;
        }
        state.D[0] = uint.MaxValue;
    }

    private void Enqueue(byte raw)
    {
        _readEvents.Enqueue(raw); _inputEvents.Enqueue(raw);
    }

    private void ReadEvent(uint request, long cycle)
    {
        var task = _getCurrentTask();
        // The ROM input task keeps one KBD_READEVENT outstanding.  Host keys
        // reach it through our IND_WRITEEVENT bridge, so completing this
        // request too would duplicate every event through the ROM keyboard
        // queue.  Other callers retain normal keyboard.device behavior.
        if (_nativeInputTask == 0 && task != 0)
        {
            _nativeInputTask = task;
            _readEvents.Clear();
        }
        if (_nativeInputTask != 0 && task == _nativeInputTask)
        {
            _pendingReads.Add(new PendingRead(request, task));
            return;
        }
        if (_readEvents.Count != 0) WriteReadEvent(request, _readEvents.Dequeue(), cycle);
        else _pendingReads.Add(new PendingRead(request, task));
    }

    private void CompletePendingRead(long cycle)
    {
        for (var index = 0; index < _pendingReads.Count && _readEvents.Count != 0;)
        {
            var pending = _pendingReads[index];
            if (_nativeInputTask != 0 && pending.Task == _nativeInputTask) { index++; continue; }
            _pendingReads.RemoveAt(index); WriteReadEvent(pending.Request, _readEvents.Dequeue(), cycle);
        }
    }

    private void WriteReadEvent(uint request, byte raw, long cycle)
    {
        var destination = _bus.ReadLong(request + IoDataOffset); var length = _bus.ReadLong(request + IoLengthOffset);
        if (destination == 0 || length == 0 || !_bus.IsMappedMemoryRange(destination, 1)) { Complete(request, IoErrBadAddress, 0, cycle, reply: true); return; }
        _bus.WriteByte(destination, raw, cycle); Complete(request, 0, 1, cycle, reply: true);
    }

    private void WriteMatrix(uint request, long cycle)
    {
        var destination = _bus.ReadLong(request + IoDataOffset); var length = _bus.ReadLong(request + IoLengthOffset);
        if (destination == 0 || length < 16 || !_bus.IsMappedMemoryRange(destination, 16)) { Complete(request, IoErrBadAddress, 0, cycle, reply: true); return; }
        _bus.ClearMemory(destination, 16);
        foreach (var raw in _pressed) { var address = destination + (uint)(raw >> 3); _bus.WriteByte(address, (byte)(_bus.ReadByte(address) | (1 << (raw & 7))), cycle); }
        Complete(request, 0, 16, cycle, reply: true);
    }

    private void AddResetHandler(uint request, long cycle) { var handler = _bus.ReadLong(request + IoDataOffset); if (handler == 0) Complete(request, IoErrBadAddress, 0, cycle, true); else { _resetHandlers.Add(handler); Complete(request, 0, 0, cycle, true); } }
    private void RemoveResetHandler(uint request, long cycle) { var handler = _bus.ReadLong(request + IoDataOffset); Complete(request, _resetHandlers.Remove(handler) ? (byte)0 : IoErrBadAddress, 0, cycle, true); }

    private void StartNativeInput(M68kCpuState state, byte raw)
    {
        var request = _scratch; var inputEvent = _scratch + EventOffset;
        _bus.ClearMemory(_scratch, RequestBytes + EventBytes);
        _bus.WriteLong(request + IoDeviceOffset, InputDeviceBase, state.Cycles); _bus.WriteWord(request + IoCommandOffset, IndWriteEvent, state.Cycles); _bus.WriteLong(request + IoDataOffset, inputEvent, state.Cycles);
        _bus.WriteByte(request + IoFlagsOffset, IoQuick, state.Cycles);
        _bus.WriteByte(inputEvent + 4, 1, state.Cycles); // IECLASS_RAWKEY
        _bus.WriteWord(inputEvent + 6, raw, state.Cycles); _bus.WriteWord(inputEvent + 8, GetQualifiers(), state.Cycles);
        var micros = ((Int128)Math.Max(0, state.Cycles) * 1_000_000) / _bus.RasterTiming.CpuClockHz;
        _bus.WriteLong(inputEvent + 0x10, unchecked((uint)(micros / 1_000_000)), state.Cycles); _bus.WriteLong(inputEvent + 0x14, unchecked((uint)(micros % 1_000_000)), state.Cycles);
        var resume = state.ProgramCounter; state.A[7] -= 4; _bus.WriteLong(state.A[7], resume, state.Cycles); state.A[7] -= 4; _bus.WriteLong(state.A[7], InputContinuationAddress, state.Cycles);
        state.A[1] = request; state.ProgramCounter = InputDeviceBase - 30; _inputDispatchActive = true;
    }

    private void ContinueNativeInput(M68kCpuState state) => _inputDispatchActive = false;

    private ushort GetQualifiers()
    {
        ushort result = 0;
        if (_pressed.Contains((byte)AmigaRawKey.LeftShift)) result |= 0x0001;
        if (_pressed.Contains((byte)AmigaRawKey.RightShift)) result |= 0x0002;
        if (_pressed.Contains((byte)AmigaRawKey.CapsLock)) result |= 0x0004;
        if (_pressed.Contains((byte)AmigaRawKey.Control)) result |= 0x0008;
        if (_pressed.Contains((byte)AmigaRawKey.LeftAlt)) result |= 0x0010;
        if (_pressed.Contains((byte)AmigaRawKey.RightAlt)) result |= 0x0020;
        if (_pressed.Contains((byte)AmigaRawKey.LeftAmiga)) result |= 0x0040;
        if (_pressed.Contains((byte)AmigaRawKey.RightAmiga)) result |= 0x0080;
        return result;
    }

    private void Complete(uint request, byte error, uint actual, long cycle, bool reply)
    {
        if (request == 0 || !_bus.IsMappedMemoryRange(request + IoErrorOffset, 9)) return;
        _bus.WriteByte(request + IoErrorOffset, error, cycle); _bus.WriteLong(request + IoActualOffset, actual, cycle);
        if (reply && (_bus.ReadByte(request + IoFlagsOffset) & IoQuick) == 0) _replyMessage(request);
    }

    private void Register(int lvo, Action<M68kCpuState> callback) => RegisterAddress(unchecked((uint)((int)DeviceBase + lvo)), callback);
    private void RegisterAddress(uint address, Action<M68kCpuState> callback) => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
    private uint FindDevice(uint list, string name)
    {
        var node = _bus.ReadLong(list);
        for (var count = 0; node != 0 && node != list + 4 && count < 256; count++)
        {
            if (!_bus.IsMappedMemoryRange(node, NodeNameOffset + 4)) return 0;
            if (string.Equals(ReadName(_bus.ReadLong(node + NodeNameOffset)), name, StringComparison.OrdinalIgnoreCase)) return node;
            node = _bus.ReadLong(node);
        }
        return 0;
    }
    private string ReadName(uint address)
    {
        Span<char> value = stackalloc char[64]; var length = 0;
        while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var character = _bus.ReadByte(address + (uint)length); if (character == 0) break; value[length++] = (char)character; }
        return new string(value[..length]);
    }
}
