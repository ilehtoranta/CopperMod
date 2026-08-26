using System;
using System.Collections.Generic;
using System.Linq;
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
    private const ushort CmdClear = 5;
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
    private const byte IoErrBadLength = 0xFB;
    private const uint MemfPublicClear = 0x0001_0001;
    private const int RequestBytes = 0x30;
    private const int EventOffset = RequestBytes;
    private const int EventBytes = 0x18;
    private const uint InputContinuationAddress = 0x00F0_8600;
    private const uint ResetHandlerContinuationAddress = 0x00F0_8610;
    private const long ResetTimeoutSeconds = 10;

    private readonly AmigaBus _bus;
    private readonly ExecMemoryOperations _memory;
    private readonly Action<uint> _replyMessage;
    private readonly Action<string> _diagnostic;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Queue<RawKeyboardEvent> _readEvents = new();
    private readonly Queue<PendingInputEvent> _inputEvents = new();
    private readonly HashSet<byte> _pressed = new();
    private readonly List<ResetHandler> _resetHandlers = new();
    private readonly Func<uint> _getCurrentTask;
    private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
    private readonly Action _requestSystemReset;
    private readonly List<PendingRead> _pendingReads = new();
    private uint _nativeInputTask;
    private uint _scratch;
    private bool _inputDispatchActive;
    private readonly record struct PendingRead(uint Request, uint Task);
    private readonly record struct PendingInputEvent(byte Raw, ushort Qualifier, uint Seconds, uint Microseconds);
    private readonly record struct RawKeyboardEvent(byte Raw, ushort Qualifier, uint Seconds, uint Microseconds);
    private readonly record struct ResetHandler(uint Address, uint Code, uint Data, sbyte Priority, long Sequence);
    private byte? _repeatKey;
    private long _nextRepeatCycle = long.MaxValue;
    private long _repeatThresholdCycles;
    private long _repeatPeriodCycles;
    private long _resetSequence;
    private long _resetDeadlineCycle = long.MaxValue;
    private bool _resetRequested;
    private bool _resetHandlerActive;
    private readonly Queue<ResetHandler> _pendingResetHandlers = new();
    private readonly HashSet<uint> _resetOutstanding = new();

    public KeyboardDeviceServices(
        AmigaBus bus,
        ExecMemoryOperations memory,
        Action<uint> replyMessage,
        Action<string> diagnostic,
        Func<uint> getCurrentTask,
        Action<M68kCpuState, uint, uint>? startGuestSubroutine = null,
        Action? requestSystemReset = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
        _diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        _getCurrentTask = getCurrentTask ?? throw new ArgumentNullException(nameof(getCurrentTask));
        _startGuestSubroutine = startGuestSubroutine ?? ((_, _, _) => { });
        _requestSystemReset = requestSystemReset ?? (() => { });
        _repeatThresholdCycles = _bus.RasterTiming.CpuClockHz / 2;
        _repeatPeriodCycles = Math.Max(1, _bus.RasterTiming.CpuClockHz / 25);
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
        RegisterAddress(ResetHandlerContinuationAddress, ContinueResetHandler);
        return true;
    }

    public bool QueueKeyDown(AmigaRawKey key, long cycle = 0)
    {
        if (!IsInstalled) return false;
        var raw = (byte)key;
        if (raw >= 0x80 || !_pressed.Add(raw)) return true;
        Enqueue(raw, cycle);
        if (IsRepeatable(raw)) { _repeatKey = raw; _nextRepeatCycle = cycle + _repeatThresholdCycles; }
        if (IsKeyboardResetChord()) BeginReset(cycle);
        return true;
    }

    public bool QueueKeyUp(AmigaRawKey key, long cycle = 0)
    {
        if (!IsInstalled) return false;
        var raw = (byte)key;
        if (raw >= 0x80 || !_pressed.Remove(raw)) return true;
        Enqueue((byte)(raw | 0x80), cycle);
        if (_repeatKey == raw) { _repeatKey = null; _nextRepeatCycle = long.MaxValue; }
        return true;
    }

    public void ConfigureKeyRepeat(uint seconds, uint microseconds, bool period)
    {
        var micros = (Int128)seconds * 1_000_000 + Math.Min(microseconds, 999_999u);
        var cycles = (long)Int128.Clamp((micros * _bus.RasterTiming.CpuClockHz) / 1_000_000, 1, long.MaxValue);
        if (period) _repeatPeriodCycles = cycles;
        else _repeatThresholdCycles = cycles;
    }

    public long GetNextDeadline(long currentCycle, long targetCycle)
    {
        var next = targetCycle;
        if (_repeatKey.HasValue && _nextRepeatCycle > currentCycle && _nextRepeatCycle < next) next = _nextRepeatCycle;
        if (_resetRequested && _resetDeadlineCycle > currentCycle && _resetDeadlineCycle < next) next = _resetDeadlineCycle;
        return next;
    }

    public void ProcessPending(M68kCpuState state)
    {
        if (!IsInstalled) return;
        if (_resetRequested)
        {
            ProcessReset(state);
            return;
        }
        CompletePendingRead(state.Cycles);
        if (_repeatKey is { } repeat && state.Cycles >= _nextRepeatCycle)
        {
            _inputEvents.Enqueue(CreateInputEvent(repeat, repeat: true, state.Cycles));
            _nextRepeatCycle = state.Cycles + _repeatPeriodCycles;
        }
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
        _pendingReads.Clear(); _readEvents.Clear(); _inputEvents.Clear(); _pressed.Clear(); _resetHandlers.Clear(); _pendingResetHandlers.Clear(); _resetOutstanding.Clear();
        _repeatKey = null; _nextRepeatCycle = long.MaxValue;
        _resetRequested = false; _resetHandlerActive = false; _resetDeadlineCycle = long.MaxValue; _resetSequence = 0;
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
            case CmdClear: ClearInputBuffer(request, state.Cycles); break;
            case KbdReadEvent:
                ReadEvent(request, state.Cycles);
                break;
            case KbdReadMatrix: WriteMatrix(request, state.Cycles); break;
            case KbdAddResetHandler: AddResetHandler(request, state.Cycles); break;
            case KbdRemResetHandler: RemoveResetHandler(request, state.Cycles); break;
            case KbdResetHandlerDone: ResetHandlerDone(request, state.Cycles); break;
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

    private void Enqueue(byte raw, long cycle = 0)
    {
        var input = CreateInputEvent(raw, repeat: false, cycle);
        _readEvents.Enqueue(new RawKeyboardEvent(input.Raw, input.Qualifier, input.Seconds, input.Microseconds));
        _inputEvents.Enqueue(input);
    }

    private PendingInputEvent CreateInputEvent(byte raw, bool repeat, long cycle)
    {
        var micros = ((Int128)Math.Max(0, cycle) * 1_000_000) / _bus.RasterTiming.CpuClockHz;
        var qualifier = GetQualifiers(raw);
        if (repeat) qualifier |= 0x0200;
        return new PendingInputEvent(raw, qualifier, unchecked((uint)(micros / 1_000_000)), unchecked((uint)(micros % 1_000_000)));
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
        if (!HasWholeInputEventBuffer(request)) { Complete(request, IoErrBadLength, 0, cycle, reply: true); return; }
        if (_readEvents.Count != 0) WriteReadEvents(request, cycle);
        else _pendingReads.Add(new PendingRead(request, task));
    }

    private void CompletePendingRead(long cycle)
    {
        for (var index = 0; index < _pendingReads.Count && _readEvents.Count != 0;)
        {
            var pending = _pendingReads[index];
            if (_nativeInputTask != 0 && pending.Task == _nativeInputTask) { index++; continue; }
            _pendingReads.RemoveAt(index); WriteReadEvents(pending.Request, cycle);
        }
    }

    private bool HasWholeInputEventBuffer(uint request)
    {
        var destination = _bus.ReadLong(request + IoDataOffset);
        var length = _bus.ReadLong(request + IoLengthOffset);
        return destination != 0 && length >= EventBytes && length % EventBytes == 0 &&
            length <= int.MaxValue && _bus.IsMappedMemoryRange(destination, checked((int)length));
    }

    private void WriteReadEvents(uint request, long cycle)
    {
        if (!HasWholeInputEventBuffer(request)) { Complete(request, IoErrBadLength, 0, cycle, reply: true); return; }
        var destination = _bus.ReadLong(request + IoDataOffset);
        var capacity = _bus.ReadLong(request + IoLengthOffset) / EventBytes;
        var actual = 0u;
        while (actual < capacity && _readEvents.TryDequeue(out var input))
        {
            var address = destination + actual * EventBytes;
            _bus.ClearMemory(address, EventBytes);
            _bus.WriteByte(address + 4, 1, cycle);
            _bus.WriteWord(address + 6, input.Raw, cycle);
            _bus.WriteWord(address + 8, input.Qualifier, cycle);
            _bus.WriteLong(address + 0x10, input.Seconds, cycle);
            _bus.WriteLong(address + 0x14, input.Microseconds, cycle);
            actual++;
        }
        Complete(request, 0, actual * EventBytes, cycle, reply: true);
    }

    private void WriteMatrix(uint request, long cycle)
    {
        var destination = _bus.ReadLong(request + IoDataOffset); var length = _bus.ReadLong(request + IoLengthOffset);
        var actual = Math.Min(length, 16u);
        if ((actual != 0 && (destination == 0 || !_bus.IsMappedMemoryRange(destination, checked((int)actual)))) || actual > int.MaxValue) { Complete(request, IoErrBadAddress, 0, cycle, reply: true); return; }
        if (actual != 0) _bus.ClearMemory(destination, checked((int)actual));
        foreach (var raw in _pressed) { var offset = raw >> 3; if (offset < actual) { var address = destination + (uint)offset; _bus.WriteByte(address, (byte)(_bus.ReadByte(address) | (1 << (raw & 7))), cycle); } }
        Complete(request, 0, actual, cycle, reply: true);
    }

    private void ClearInputBuffer(uint request, long cycle)
    {
        _readEvents.Clear();
        Complete(request, 0, 0, cycle, reply: true);
    }

    private void AddResetHandler(uint request, long cycle)
    {
        var address = _bus.ReadLong(request + IoDataOffset);
        if (address == 0 || !_bus.IsMappedMemoryRange(address, 0x16)) { Complete(request, IoErrBadAddress, 0, cycle, true); return; }
        var priority = unchecked((sbyte)_bus.ReadByte(address + 9));
        var data = _bus.ReadLong(address + 0x0E);
        var code = _bus.ReadLong(address + 0x12);
        if (priority is not (-32 or -16 or 0 or 16 or 32) || code == 0 || !_bus.IsCpuPhysicalAddressMapped(code, 2, AmigaBusAccessKind.CpuInstructionFetch)) { Complete(request, IoErrBadAddress, 0, cycle, true); return; }
        if (!_resetHandlers.Exists(handler => handler.Address == address)) _resetHandlers.Add(new ResetHandler(address, code, data, priority, _resetSequence++));
        Complete(request, 0, 0, cycle, true);
    }

    private void RemoveResetHandler(uint request, long cycle)
    {
        var address = _bus.ReadLong(request + IoDataOffset);
        var index = _resetHandlers.FindIndex(handler => handler.Address == address);
        if (index < 0) { Complete(request, IoErrBadAddress, 0, cycle, true); return; }
        _resetHandlers.RemoveAt(index);
        Complete(request, 0, 0, cycle, true);
    }

    private void ResetHandlerDone(uint request, long cycle)
    {
        var address = _bus.ReadLong(request + IoDataOffset);
        if (!_resetRequested || !_resetOutstanding.Remove(address)) { Complete(request, IoErrBadAddress, 0, cycle, true); return; }
        Complete(request, 0, 0, cycle, true);
    }

    private bool IsKeyboardResetChord()
        => _pressed.Contains((byte)AmigaRawKey.Control) &&
            _pressed.Contains((byte)AmigaRawKey.LeftAmiga) &&
            _pressed.Contains((byte)AmigaRawKey.RightAmiga);

    private void BeginReset(long cycle)
    {
        if (_resetRequested) return;
        _resetRequested = true;
        _resetHandlerActive = false;
        _resetOutstanding.Clear();
        _pendingResetHandlers.Clear();
        foreach (var handler in _resetHandlers.OrderByDescending(handler => handler.Priority).ThenBy(handler => handler.Sequence))
        {
            _pendingResetHandlers.Enqueue(handler);
            _resetOutstanding.Add(handler.Address);
        }
        _resetDeadlineCycle = cycle + ResetTimeoutSeconds * _bus.RasterTiming.CpuClockHz;
    }

    private void ProcessReset(M68kCpuState state)
    {
        if (state.Cycles >= _resetDeadlineCycle)
        {
            _diagnostic("keyboard.device reset handler deadline elapsed; resetting the machine.");
            _resetRequested = false;
            _requestSystemReset();
            return;
        }

        if (_resetHandlerActive) return;
        if (_pendingResetHandlers.TryDequeue(out var handler))
        {
            _resetHandlerActive = true;
            state.A[1] = handler.Data;
            _startGuestSubroutine(state, handler.Code, ResetHandlerContinuationAddress);
            return;
        }

        if (_resetOutstanding.Count == 0)
        {
            _resetRequested = false;
            _requestSystemReset();
        }
    }

    private void ContinueResetHandler(M68kCpuState state)
    {
        _resetHandlerActive = false;
        ProcessReset(state);
    }

    private void StartNativeInput(M68kCpuState state, PendingInputEvent input)
    {
        var request = _scratch; var inputEvent = _scratch + EventOffset;
        _bus.ClearMemory(_scratch, RequestBytes + EventBytes);
        _bus.WriteLong(request + IoDeviceOffset, InputDeviceBase, state.Cycles); _bus.WriteWord(request + IoCommandOffset, IndWriteEvent, state.Cycles); _bus.WriteLong(request + IoDataOffset, inputEvent, state.Cycles);
        _bus.WriteByte(request + IoFlagsOffset, IoQuick, state.Cycles);
        _bus.WriteByte(inputEvent + 4, 1, state.Cycles); // IECLASS_RAWKEY
        _bus.WriteWord(inputEvent + 6, input.Raw, state.Cycles); _bus.WriteWord(inputEvent + 8, input.Qualifier, state.Cycles);
        _bus.WriteLong(inputEvent + 0x10, input.Seconds, state.Cycles); _bus.WriteLong(inputEvent + 0x14, input.Microseconds, state.Cycles);
        var resume = state.ProgramCounter; state.A[7] -= 4; _bus.WriteLong(state.A[7], resume, state.Cycles); state.A[7] -= 4; _bus.WriteLong(state.A[7], InputContinuationAddress, state.Cycles);
        state.A[1] = request; state.ProgramCounter = InputDeviceBase - 30; _inputDispatchActive = true;
    }

    private void ContinueNativeInput(M68kCpuState state) => _inputDispatchActive = false;

    private ushort GetQualifiers(byte raw = 0xFF)
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
        if (IsNumericPadKey((byte)(raw & 0x7F))) result |= 0x0100;
        return result;
    }

    private static bool IsNumericPadKey(byte raw)
        => raw is 0x0F or 0x1D or 0x1E or 0x1F or 0x2D or 0x2E or 0x2F or 0x3C or 0x3D or 0x3E or 0x3F or 0x43 or 0x4A or >= 0x5A and <= 0x5E;

    private static bool IsRepeatable(byte raw)
        => raw is not ((byte)AmigaRawKey.LeftShift) and not ((byte)AmigaRawKey.RightShift) and not ((byte)AmigaRawKey.CapsLock) and
           not ((byte)AmigaRawKey.Control) and not ((byte)AmigaRawKey.LeftAlt) and not ((byte)AmigaRawKey.RightAlt) and
           not ((byte)AmigaRawKey.LeftAmiga) and not ((byte)AmigaRawKey.RightAmiga);

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
