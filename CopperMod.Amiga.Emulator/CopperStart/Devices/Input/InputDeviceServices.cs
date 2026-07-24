using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Devices.Input;

/// <summary>
/// Incremental ROM input.device takeover. Until a command has a complete host
/// implementation, BeginIO is a gateway trampoline into the original ROM LVO.
/// This preserves the live ROM handler chain while providing one controlled
/// interception point for later host commands.
/// </summary>
internal sealed class InputDeviceServices : IDisposable
{
    private const int DeviceListOffset = 0x15E;
    private const int NodeNameOffset = 0x0A;
    private const uint NativeBeginIoContinuationAddress = 0x00F0_8700;
    private const uint HostHandlerContinuationAddress = 0x00F0_8710;
    private const ushort IndAddHandler = 9;
    private const ushort IndRemHandler = 10;
    private const ushort IndWriteEvent = 11;
    private const ushort IndAddEvent = 24;
    private const ushort IndSetThreshold = 12;
    private const ushort IndSetPeriod = 13;
    private const int IoCommandOffset = 0x1C;
    private const int IoFlagsOffset = 0x1E;
    private const int IoErrorOffset = 0x1F;
    private const int IoActualOffset = 0x20;
    private const int IoLengthOffset = 0x24;
    private const int NodeTypeOffset = 0x08;
    private const int IoDataOffset = 0x28;
    private const int TimeSecondsOffset = 0x20;
    private const int TimeMicrosecondsOffset = 0x24;
    private const int InterruptDataOffset = 0x0E;
    private const int InterruptCodeOffset = 0x12;
    private const byte IoQuick = 0x01;
    private const byte IeClassRawKey = 1;
    private const byte IeClassRawMouse = 2;
    private const int InputEventBytes = 0x18;

    private readonly AmigaBus _bus;
    private readonly Action<uint> _replyMessage;
    private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
    private readonly Action<uint, uint, bool> _configureKeyRepeat;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly HashSet<uint> _nativeHandlers = new();
    private readonly List<ObservedInputEvent> _observedEvents = new();
    private readonly List<ForwardedOperation> _pendingHandlerOperations = new();
    private uint _beginIoAddress;
    private uint _beginIoToken;
    private bool _forwardingBeginIo;
    private ForwardedOperation? _forwardedOperation;
    private uint _handlerListAddress;
    private HostWriteOperation? _hostWrite;

    private readonly record struct ForwardedOperation(uint Request, ushort Command, uint Data);
    private sealed class HostWriteOperation
    {
        public HostWriteOperation(uint request, uint events, uint handler) { Request = request; Events = events; Handler = handler; }
        public uint Request { get; }
        public uint Events { get; set; }
        public uint Handler { get; set; }
    }
    internal readonly record struct ObservedInputEvent(uint Address, byte Class, byte SubClass, ushort Code, ushort Qualifier, uint Next);

    public InputDeviceServices(AmigaBus bus, Action<uint> replyMessage, Action<M68kCpuState, uint, uint> startGuestSubroutine, Action<uint, uint, bool> configureKeyRepeat)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
        _startGuestSubroutine = startGuestSubroutine ?? throw new ArgumentNullException(nameof(startGuestSubroutine));
        _configureKeyRepeat = configureKeyRepeat ?? throw new ArgumentNullException(nameof(configureKeyRepeat));
    }

    public uint DeviceBase { get; private set; }
    public bool IsInstalled => _beginIoToken != 0;
    public IReadOnlyCollection<uint> KnownNativeHandlers => _nativeHandlers;
    public IReadOnlyList<ObservedInputEvent> ObservedWriteEvents => _observedEvents;
    internal int GatewayRegistrationCount => _gateways.Count;

    /// <summary>Observes completion of ROM-owned asynchronous handler commands.</summary>
    public void ProcessPending()
    {
        for (var index = _pendingHandlerOperations.Count - 1; index >= 0; index--)
        {
            var operation = _pendingHandlerOperations[index];
            if (!_bus.IsMappedMemoryRange(operation.Request + NodeTypeOffset, 1) || _bus.ReadByte(operation.Request + NodeTypeOffset) != 7) continue;
            _pendingHandlerOperations.RemoveAt(index);
            if (_bus.ReadByte(operation.Request + IoErrorOffset) != 0) continue;
            if (operation.Command == IndAddHandler) ImportLiveHandlerList(operation.Data);
            else { _nativeHandlers.Remove(operation.Data); if (_handlerListAddress != 0) ImportLiveHandlerList(_handlerListAddress); }
        }
    }

    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14)) return IsInstalled;
        var device = FindDevice(execBase + DeviceListOffset, "input.device");
        if (device < 30 || !_bus.IsMappedMemoryRange(device - 30, 30)) return false;
        DeviceBase = device;
        _beginIoAddress = device - 30;
        RegisterBeginIo();
        RegisterAddress(NativeBeginIoContinuationAddress, ContinueNativeBeginIo);
        RegisterAddress(HostHandlerContinuationAddress, ContinueHostHandler);
        return true;
    }

    public void Reset() => Dispose();

    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        _gateways.Clear(); _nativeHandlers.Clear(); _observedEvents.Clear(); _pendingHandlerOperations.Clear(); _forwardedOperation = null; _hostWrite = null; _handlerListAddress = 0; _beginIoAddress = 0; _beginIoToken = 0; _forwardingBeginIo = false; DeviceBase = 0;
    }

    private void BeginIo(M68kCpuState state)
    {
        // The 68k core has already consumed FF00+token. Remove only this
        // registration, place a continuation above the guest caller return,
        // and execute the original ROM vector without recursive CPU entry.
        if (_forwardingBeginIo || _beginIoToken == 0 || state.A[7] < 4) { state.D[0] = uint.MaxValue; return; }
        var request = state.A[1];
        if (TryHandleKeyRepeatConfiguration(state, request)) return;
        if (TryStartHostedWrite(state, request)) return;
        if (TryHandleHostedHandlerChange(state, request)) return;
        RemoveBeginIoRegistration();
        if (request != 0 && _bus.IsMappedMemoryRange(request + IoDataOffset, 4))
        {
            var command = _bus.ReadWord(request + IoCommandOffset);
            var data = _bus.ReadLong(request + IoDataOffset);
            if (command == IndWriteEvent) CaptureInputEventChain(data);
            if (command is IndAddHandler or IndRemHandler) _forwardedOperation = new ForwardedOperation(request, command, data);
        }
        state.A[7] -= 4;
        _bus.WriteLong(state.A[7], NativeBeginIoContinuationAddress, state.Cycles);
        state.ProgramCounter = _beginIoAddress;
        _forwardingBeginIo = true;
    }

    private void ContinueNativeBeginIo(M68kCpuState state)
    {
        if (_forwardedOperation is { } operation) _pendingHandlerOperations.Add(operation);
        _forwardedOperation = null;
        if (_forwardingBeginIo) { _forwardingBeginIo = false; RegisterBeginIo(); }
    }

    private bool TryStartHostedWrite(M68kCpuState state, uint request)
    {
        if (_hostWrite is not null || _handlerListAddress == 0 || request == 0 ||
            !_bus.IsMappedMemoryRange(request + IoDataOffset, 4) ||
            _bus.ReadWord(request + IoCommandOffset) is not (IndWriteEvent or IndAddEvent))
        {
            return false;
        }

        var command = _bus.ReadWord(request + IoCommandOffset);
        var events = command == IndAddEvent ? BuildAddedEventChain(request, state.Cycles) : _bus.ReadLong(request + IoDataOffset);
        if (events == 0 || !_bus.IsMappedMemoryRange(events, 16)) return false;
        CaptureInputEventChain(events);
        var firstHandler = _bus.ReadLong(_handlerListAddress);
        if (firstHandler == _handlerListAddress + 4 || firstHandler == 0) return false;
        _hostWrite = new HostWriteOperation(request, events, firstHandler);
        MarkAsynchronous(request, state.Cycles);
        StartCurrentHandler(state);
        return true;
    }

    private bool TryHandleKeyRepeatConfiguration(M68kCpuState state, uint request)
    {
        if (request == 0 || !_bus.IsMappedMemoryRange(request + TimeMicrosecondsOffset, 4)) return false;
        var command = _bus.ReadWord(request + IoCommandOffset);
        if (command is not (IndSetThreshold or IndSetPeriod)) return false;
        _configureKeyRepeat(_bus.ReadLong(request + TimeSecondsOffset), _bus.ReadLong(request + TimeMicrosecondsOffset), command == IndSetPeriod);
        MarkAsynchronous(request, state.Cycles);
        CompleteHostedRequest(request, state.Cycles);
        return true;
    }

    private uint BuildAddedEventChain(uint request, long cycle)
    {
        var first = _bus.ReadLong(request + IoDataOffset);
        var bytes = _bus.ReadLong(request + IoLengthOffset);
        if (first == 0 || bytes < InputEventBytes || bytes % InputEventBytes != 0 || bytes > 0x1800 || !_bus.IsMappedMemoryRange(first, (int)bytes)) return 0;

        uint head = 0, previous = 0;
        var count = Math.Min(bytes / InputEventBytes, 256u);
        for (uint index = 0; index < count; index++)
        {
            var current = first + index * InputEventBytes;
            var @class = _bus.ReadByte(current + 4);
            if (@class is not (IeClassRawKey or IeClassRawMouse)) continue;
            _bus.WriteLong(current, 0, cycle);
            WriteTimestamp(current, cycle);
            if (previous == 0) head = current;
            else _bus.WriteLong(previous, current, cycle);
            previous = current;
        }
        return head;
    }

    private void WriteTimestamp(uint inputEvent, long cycle)
    {
        var micros = ((Int128)Math.Max(0, cycle) * 1_000_000) / _bus.RasterTiming.CpuClockHz;
        _bus.WriteLong(inputEvent + 0x10, unchecked((uint)(micros / 1_000_000)), cycle);
        _bus.WriteLong(inputEvent + 0x14, unchecked((uint)(micros % 1_000_000)), cycle);
    }

    private bool TryHandleHostedHandlerChange(M68kCpuState state, uint request)
    {
        if (_handlerListAddress == 0 || request == 0 || !_bus.IsMappedMemoryRange(request + IoDataOffset, 4)) return false;
        var command = _bus.ReadWord(request + IoCommandOffset);
        if (command is not (IndAddHandler or IndRemHandler)) return false;
        var handler = _bus.ReadLong(request + IoDataOffset);
        if (handler == 0 || !_bus.IsMappedMemoryRange(handler, InterruptCodeOffset + 4)) return false;

        if (command == IndAddHandler) InsertHandler(handler);
        else RemoveHandler(handler);
        ImportLiveHandlerList(_handlerListAddress);
        MarkAsynchronous(request, state.Cycles);
        CompleteHostedRequest(request, state.Cycles);
        return true;
    }

    private void ContinueHostHandler(M68kCpuState state)
    {
        if (_hostWrite is null) return;
        _hostWrite.Events = state.D[0];
        if (_hostWrite.Events == 0) { CompleteHostedWrite(state); return; }
        _hostWrite.Handler = _bus.IsMappedMemoryRange(_hostWrite.Handler, 4) ? _bus.ReadLong(_hostWrite.Handler) : 0;
        StartCurrentHandler(state);
    }

    private void StartCurrentHandler(M68kCpuState state)
    {
        while (_hostWrite is not null)
        {
            var handler = _hostWrite.Handler;
            if (handler == 0 || handler == _handlerListAddress + 4) { CompleteHostedWrite(state); return; }
            if (!_bus.IsMappedMemoryRange(handler + InterruptCodeOffset, 4)) { _hostWrite.Handler = 0; continue; }
            var code = _bus.ReadLong(handler + InterruptCodeOffset);
            if (code == 0 || !_bus.IsCpuPhysicalAddressMapped(code, 2, AmigaBusAccessKind.CpuInstructionFetch))
            {
                _hostWrite.Handler = _bus.ReadLong(handler);
                continue;
            }
            state.A[0] = _hostWrite.Events;
            state.A[1] = _bus.ReadLong(handler + InterruptDataOffset);
            state.A[2] = code;
            _startGuestSubroutine(state, code, HostHandlerContinuationAddress);
            return;
        }
    }

    private void CompleteHostedWrite(M68kCpuState state)
    {
        var operation = _hostWrite;
        _hostWrite = null;
        if (operation is null || !_bus.IsMappedMemoryRange(operation.Request + IoErrorOffset, 1)) return;
        CompleteHostedRequest(operation.Request, state.Cycles);
    }

    private void MarkAsynchronous(uint request, long cycle)
    {
        if (_bus.IsMappedMemoryRange(request + IoFlagsOffset, 1)) _bus.WriteByte(request + IoFlagsOffset, (byte)(_bus.ReadByte(request + IoFlagsOffset) & ~IoQuick), cycle);
    }

    private void CompleteHostedRequest(uint request, long cycle)
    {
        if (!_bus.IsMappedMemoryRange(request + IoErrorOffset, 1)) return;
        _bus.WriteByte(request + IoErrorOffset, 0, cycle);
        _bus.WriteByte(request + NodeTypeOffset, 7, cycle);
        _replyMessage(request);
    }

    private void InsertHandler(uint handler)
    {
        var tail = _handlerListAddress + 4;
        var current = _bus.ReadLong(_handlerListAddress);
        var priority = unchecked((sbyte)_bus.ReadByte(handler + 9));
        while (current != 0 && current != tail && _bus.IsMappedMemoryRange(current + 10, 1) &&
               priority <= unchecked((sbyte)_bus.ReadByte(current + 9))) current = _bus.ReadLong(current);
        if (current == 0 || !_bus.IsMappedMemoryRange(current + 4, 4)) return;
        var previous = _bus.ReadLong(current + 4);
        if (previous == 0 || !_bus.IsMappedMemoryRange(previous, 4)) return;
        _bus.WriteLong(handler, current);
        _bus.WriteLong(handler + 4, previous);
        _bus.WriteLong(previous, handler);
        _bus.WriteLong(current + 4, handler);
    }

    private void RemoveHandler(uint handler)
    {
        if (!_bus.IsMappedMemoryRange(handler + 4, 4)) return;
        var next = _bus.ReadLong(handler); var previous = _bus.ReadLong(handler + 4);
        if (next == 0 || previous == 0 || !_bus.IsMappedMemoryRange(next + 4, 4) || !_bus.IsMappedMemoryRange(previous, 4)) return;
        _bus.WriteLong(previous, next);
        _bus.WriteLong(next + 4, previous);
        _bus.WriteLong(handler, 0);
        _bus.WriteLong(handler + 4, 0);
    }

    private void RegisterBeginIo()
    {
        if (_beginIoAddress != 0 && _beginIoToken == 0)
        {
            _beginIoToken = _bus.RegisterHostGateway(_beginIoAddress, BeginIo);
            _gateways.Add((_beginIoAddress, _beginIoToken));
        }
    }

    private void RemoveBeginIoRegistration()
    {
        if (_beginIoToken == 0) return;
        _bus.RemoveHostGateway(_beginIoAddress, _beginIoToken);
        for (var index = _gateways.Count - 1; index >= 0; index--)
        {
            if (_gateways[index].Address == _beginIoAddress && _gateways[index].Token == _beginIoToken)
            {
                _gateways.RemoveAt(index);
                break;
            }
        }
        _beginIoToken = 0;
    }

    private void CaptureInputEventChain(uint eventAddress)
    {
        for (var count = 0; eventAddress != 0 && count < 256; count++)
        {
            if (!_bus.IsMappedMemoryRange(eventAddress, 16)) break;
            var next = _bus.ReadLong(eventAddress);
            _observedEvents.Add(new ObservedInputEvent(eventAddress,
                _bus.ReadByte(eventAddress + 4), _bus.ReadByte(eventAddress + 5),
                _bus.ReadWord(eventAddress + 6), _bus.ReadWord(eventAddress + 8), next));
            if (next == eventAddress) break;
            eventAddress = next;
        }
    }

    private void ImportLiveHandlerList(uint node)
    {
        if (node == 0 || !_bus.IsMappedMemoryRange(node + 4, 4)) return;
        var candidate = node;
        for (var count = 0; count < 256 && _bus.IsMappedMemoryRange(candidate + 4, 4); count++)
        {
            if (_bus.ReadLong(candidate + 4) == 0) { _handlerListAddress = candidate; break; }
            candidate = _bus.ReadLong(candidate + 4);
        }
        if (_handlerListAddress == 0 || !_bus.IsMappedMemoryRange(_handlerListAddress, 12)) return;
        _nativeHandlers.Clear();
        var tail = _handlerListAddress + 4;
        for (var current = _bus.ReadLong(_handlerListAddress); current != 0 && current != tail && _nativeHandlers.Count < 256; current = _bus.ReadLong(current))
        {
            if (!_bus.IsMappedMemoryRange(current + 8, 4)) break;
            _nativeHandlers.Add(current);
        }
    }

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
