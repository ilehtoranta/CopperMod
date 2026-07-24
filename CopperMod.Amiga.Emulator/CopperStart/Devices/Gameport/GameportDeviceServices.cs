using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Devices.Gameport;

/// <summary>
/// Host wrapper for the ROM-created gameport.device.  It observes the normal
/// emulated JOYDAT counters; it never writes the custom-chip input registers.
/// </summary>
internal sealed class GameportDeviceServices : IDisposable
{
    private const int DeviceListOffset = 0x15E, NodeNameOffset = 0x0A, LibraryOpenCountOffset = 0x20;
    private const int IoDeviceOffset = 0x14, IoUnitOffset = 0x18, IoCommandOffset = 0x1C, IoFlagsOffset = 0x1E, IoErrorOffset = 0x1F, IoActualOffset = 0x20, IoLengthOffset = 0x24, IoDataOffset = 0x28;
    private const ushort GpdReadEvent = 9, GpdAskCType = 10, GpdSetCType = 11, GpdAskTrigger = 12, GpdSetTrigger = 13;
    private const byte IoQuick = 0x01, IoErrOpenFail = 0xFF, IoErrAborted = 0xFE, IoErrBadLength = 0xFB, IeClassRawMouse = 2;
    private const int InputEventBytes = 0x18;
    private const uint NativeBeginIoContinuationAddress = 0x00F0_8800;
    private readonly AmigaBus _bus;
    private readonly Action<uint> _reply;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Dictionary<uint, int> _units = new();
    private readonly Dictionary<uint, PendingRead> _pending = new();
    private readonly byte[] _controllerTypes = new byte[2];
    private readonly byte[][] _triggers = { new byte[8], new byte[8] };
    private readonly ushort[] _lastJoy = new ushort[2];
    private readonly record struct PendingRead(uint Request, int Unit);
    private uint _beginIoAddress, _beginIoToken;
    private bool _forwardingBeginIo;

    public GameportDeviceServices(AmigaBus bus, Action<uint> reply)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _reply = reply ?? throw new ArgumentNullException(nameof(reply));
    }

    public uint DeviceBase { get; private set; }
    public bool IsInstalled => _gateways.Count != 0;
    internal byte ControllerType(int unit) => _controllerTypes[unit];

    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14)) return IsInstalled;
        var device = FindDevice(execBase + DeviceListOffset, "gameport.device");
        if (device < 36 || !_bus.IsMappedMemoryRange(device - 36, 36)) return false;
        DeviceBase = device;
        _lastJoy[0] = _bus.ReadWord(0x00DFF00A); _lastJoy[1] = _bus.ReadWord(0x00DFF00C);
        Register(-6, Open); Register(-12, Close); Register(-18, Expunge); Register(-24, ExtFunc); Register(-36, AbortIo);
        _beginIoAddress = DeviceBase - 30; RegisterBeginIo(); RegisterAddress(NativeBeginIoContinuationAddress, ContinueNativeBeginIo);
        return true;
    }

    public void ProcessPending(M68kCpuState state)
    {
        if (!IsInstalled) return;
        for (var unit = 0; unit < 2; unit++)
        {
            var joy = _bus.ReadWord(unit == 0 ? 0x00DFF00Au : 0x00DFF00Cu);
            var x = unchecked((sbyte)((byte)joy - (byte)_lastJoy[unit]));
            var y = unchecked((sbyte)((byte)(joy >> 8) - (byte)(_lastJoy[unit] >> 8)));
            _lastJoy[unit] = joy;
            if (x == 0 && y == 0) continue;
            foreach (var entry in new List<PendingRead>(_pending.Values))
            {
                if (entry.Unit != unit) continue;
                if (WriteMouseEvent(entry.Request, unit, x, y, state.Cycles)) _pending.Remove(entry.Request);
            }
        }
    }

    public void Reset() => Dispose();
    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        _gateways.Clear(); _units.Clear(); _pending.Clear(); Array.Clear(_controllerTypes); Array.Clear(_lastJoy); _beginIoAddress = 0; _beginIoToken = 0; _forwardingBeginIo = false; DeviceBase = 0;
    }

    private void Open(M68kCpuState state)
    {
        var request = state.A[1]; var unit = unchecked((int)state.D[0]);
        if (request == 0 || unit is < 0 or > 1 || !_bus.IsMappedMemoryRange(request, IoDataOffset + 4)) { Complete(request, IoErrOpenFail, state.Cycles, reply: false); state.D[0] = IoErrOpenFail; return; }
        _units[request] = unit; _bus.WriteLong(request + IoDeviceOffset, DeviceBase, state.Cycles); _bus.WriteLong(request + IoUnitOffset, unchecked((uint)unit), state.Cycles);
        var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(count + 1)), state.Cycles); Complete(request, 0, state.Cycles, reply: false); state.D[0] = 0;
    }
    private void Close(M68kCpuState state) { _units.Remove(state.A[1]); if (DeviceBase != 0) { var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); if (count != 0) _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(count - 1)), state.Cycles); } state.D[0] = 0; }
    private static void Expunge(M68kCpuState state) => state.D[0] = 0;
    private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;

    private void BeginIo(M68kCpuState state)
    {
        if (_forwardingBeginIo || _beginIoToken == 0) { state.D[0] = uint.MaxValue; return; }
        var request = state.A[1];
        if (request == 0 || !_bus.IsMappedMemoryRange(request, IoDataOffset + 4) || _bus.ReadLong(request + IoDeviceOffset) != DeviceBase) return;
        var unit = GetUnit(request); var command = _bus.ReadWord(request + IoCommandOffset); var data = _bus.ReadLong(request + IoDataOffset); var length = _bus.ReadLong(request + IoLengthOffset);
        switch (command)
        {
            case GpdAskCType:
                if (data == 0 || length < 1 || !_bus.IsMappedMemoryRange(data, 1)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                _bus.WriteByte(data, _controllerTypes[unit], state.Cycles); Complete(request, 0, state.Cycles, true); break;
            case GpdSetCType:
                if (data == 0 || length != 1 || !_bus.IsMappedMemoryRange(data, 1)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                _controllerTypes[unit] = _bus.ReadByte(data); Complete(request, 0, state.Cycles, true); break;
            case GpdAskTrigger:
                if (data == 0 || length != 8 || !_bus.IsMappedMemoryRange(data, 8)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                for (var index = 0; index < 8; index++) _bus.WriteByte(data + (uint)index, _triggers[unit][index], state.Cycles); Complete(request, 0, state.Cycles, true); break;
            case GpdSetTrigger:
                if (data == 0 || length != 8 || !_bus.IsMappedMemoryRange(data, 8)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                for (var index = 0; index < 8; index++) _triggers[unit][index] = _bus.ReadByte(data + (uint)index); Complete(request, 0, state.Cycles, true); break;
            case GpdReadEvent:
                if (data == 0 || length < InputEventBytes || !_bus.IsMappedMemoryRange(data, InputEventBytes)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                _bus.WriteByte(request + IoFlagsOffset, (byte)(_bus.ReadByte(request + IoFlagsOffset) & ~IoQuick), state.Cycles); _pending[request] = new PendingRead(request, unit); break;
            default:
                ForwardNativeBeginIo(state); break;
        }
    }

    private void ForwardNativeBeginIo(M68kCpuState state)
    {
        if (state.A[7] < 4) { state.D[0] = uint.MaxValue; return; }
        RemoveBeginIoRegistration();
        state.A[7] -= 4; _bus.WriteLong(state.A[7], NativeBeginIoContinuationAddress, state.Cycles);
        state.ProgramCounter = _beginIoAddress; _forwardingBeginIo = true;
    }

    private void ContinueNativeBeginIo(M68kCpuState state)
    {
        if (!_forwardingBeginIo) return;
        _forwardingBeginIo = false; RegisterBeginIo();
    }

    private void AbortIo(M68kCpuState state)
    {
        var request = state.A[1];
        if (!_pending.Remove(request)) { state.D[0] = uint.MaxValue; return; }
        Complete(request, IoErrAborted, state.Cycles, true); state.D[0] = 0;
    }

    private bool WriteMouseEvent(uint request, int unit, int x, int y, long cycle)
    {
        var data = _bus.ReadLong(request + IoDataOffset);
        if (data == 0 || !_bus.IsMappedMemoryRange(data, InputEventBytes)) { Complete(request, IoErrBadLength, cycle, true); return true; }
        _bus.ClearMemory(data, InputEventBytes); _bus.WriteByte(data + 4, IeClassRawMouse, cycle); _bus.WriteByte(data + 5, (byte)unit, cycle);
        _bus.WriteWord(data + 0x0A, unchecked((ushort)(short)x), cycle); _bus.WriteWord(data + 0x0C, unchecked((ushort)(short)y), cycle);
        var micros = ((Int128)Math.Max(0, cycle) * 1_000_000) / _bus.RasterTiming.CpuClockHz;
        _bus.WriteLong(data + 0x10, unchecked((uint)(micros / 1_000_000)), cycle); _bus.WriteLong(data + 0x14, unchecked((uint)(micros % 1_000_000)), cycle);
        Complete(request, 0, cycle, true); return true;
    }

    private void Complete(uint request, byte error, long cycle, bool reply)
    {
        if (request == 0 || !_bus.IsMappedMemoryRange(request + IoErrorOffset, 1)) return;
        _bus.WriteByte(request + IoErrorOffset, error, cycle); _bus.WriteLong(request + IoActualOffset, error == 0 ? 1u : 0u, cycle); _bus.WriteByte(request + 8, 7, cycle);
        if (reply && (_bus.ReadByte(request + IoFlagsOffset) & IoQuick) == 0) _reply(request);
    }
    private int GetUnit(uint request) => _units.TryGetValue(request, out var unit) ? unit : 0;
    private void Register(int lvo, Action<M68kCpuState> callback) { var address = unchecked((uint)((int)DeviceBase + lvo)); _gateways.Add((address, _bus.RegisterHostGateway(address, callback))); }
    private void RegisterAddress(uint address, Action<M68kCpuState> callback) => _gateways.Add((address, _bus.RegisterHostGateway(address, callback)));
    private void RegisterBeginIo()
    {
        if (_beginIoAddress == 0 || _beginIoToken != 0) return;
        _beginIoToken = _bus.RegisterHostGateway(_beginIoAddress, BeginIo); _gateways.Add((_beginIoAddress, _beginIoToken));
    }
    private void RemoveBeginIoRegistration()
    {
        if (_beginIoToken == 0) return;
        _bus.RemoveHostGateway(_beginIoAddress, _beginIoToken);
        for (var index = _gateways.Count - 1; index >= 0; index--) if (_gateways[index].Address == _beginIoAddress && _gateways[index].Token == _beginIoToken) { _gateways.RemoveAt(index); break; }
        _beginIoToken = 0;
    }
    private uint FindDevice(uint list, string name)
    {
        for (var node = _bus.ReadLong(list); node != 0 && node != list + 4 && _bus.IsMappedMemoryRange(node, NodeNameOffset + 4); node = _bus.ReadLong(node)) if (string.Equals(ReadName(_bus.ReadLong(node + NodeNameOffset)), name, StringComparison.OrdinalIgnoreCase)) return node;
        return 0;
    }
    private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var character = _bus.ReadByte(address + (uint)length); if (character == 0) break; value[length++] = (char)character; } return new string(value[..length]); }
}
