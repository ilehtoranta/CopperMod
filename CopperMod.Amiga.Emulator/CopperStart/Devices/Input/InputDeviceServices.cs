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
    private const ushort IndAddHandler = 9;
    private const ushort IndRemHandler = 10;
    private const ushort IndWriteEvent = 11;
    private const int IoCommandOffset = 0x1C;
    private const int IoErrorOffset = 0x1F;
    private const int NodeTypeOffset = 0x08;
    private const int IoDataOffset = 0x28;

    private readonly AmigaBus _bus;
    private readonly Action<uint> _replyMessage;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly HashSet<uint> _nativeHandlers = new();
    private readonly List<ObservedInputEvent> _observedEvents = new();
    private readonly List<ForwardedOperation> _pendingHandlerOperations = new();
    private uint _beginIoAddress;
    private uint _beginIoToken;
    private bool _forwardingBeginIo;
    private ForwardedOperation? _forwardedOperation;
    private uint _handlerListAddress;

    private readonly record struct ForwardedOperation(uint Request, ushort Command, uint Data);
    internal readonly record struct ObservedInputEvent(uint Address, byte Class, byte SubClass, ushort Code, ushort Qualifier, uint Next);

    public InputDeviceServices(AmigaBus bus, Action<uint> replyMessage)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _replyMessage = replyMessage ?? throw new ArgumentNullException(nameof(replyMessage));
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
        return true;
    }

    public void Reset() => Dispose();

    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        _gateways.Clear(); _nativeHandlers.Clear(); _observedEvents.Clear(); _pendingHandlerOperations.Clear(); _forwardedOperation = null; _handlerListAddress = 0; _beginIoAddress = 0; _beginIoToken = 0; _forwardingBeginIo = false; DeviceBase = 0;
    }

    private void BeginIo(M68kCpuState state)
    {
        // The 68k core has already consumed FF00+token. Remove only this
        // registration, place a continuation above the guest caller return,
        // and execute the original ROM vector without recursive CPU entry.
        if (_forwardingBeginIo || _beginIoToken == 0 || state.A[7] < 4) { state.D[0] = uint.MaxValue; return; }
        var request = state.A[1];
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
