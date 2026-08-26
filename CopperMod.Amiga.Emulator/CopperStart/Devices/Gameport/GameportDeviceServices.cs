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
    private const ushort CmdClear = 5, GpdReadEvent = 9, GpdAskCType = 10, GpdSetCType = 11, GpdAskTrigger = 12, GpdSetTrigger = 13;
    private const byte IoQuick = 0x01, IoErrOpenFail = 0xFF, IoErrAborted = 0xFE, IoErrBadLength = 0xFB, IeClassRawMouse = 2, IeCodeNoButton = 0xFF, IeCodeLeftButton = 0x68, IeCodeRightButton = 0x69, IeCodeUpPrefix = 0x80;
    private const byte GpctMouse = 1, GpctRelativeJoystick = 2, GpctAbsoluteJoystick = 3;
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
    private readonly sbyte[] _lastJoystickX = new sbyte[2], _lastJoystickY = new sbyte[2];
    private readonly bool[] _lastPrimary = new bool[2], _lastSecondary = new bool[2];
    private readonly ushort[] _qualifiers = new ushort[2];
    private readonly Queue<RawGamePortEvent>[] _events = { new(), new() };
    private readonly long[] _nextTimeout = { long.MaxValue, long.MaxValue };
    private readonly long[] _nextJoystickRepeat = { long.MaxValue, long.MaxValue };
    private readonly long[] _lastReportCycle = new long[2];
    private readonly record struct PendingRead(uint Request, int Unit);
    private readonly record struct RawGamePortEvent(byte Code, short X, short Y, ushort Qualifiers);
    private uint _beginIoAddress, _beginIoToken;
    private bool _forwardingBeginIo;

    public GameportDeviceServices(AmigaBus bus, Action<uint> reply)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _reply = reply ?? throw new ArgumentNullException(nameof(reply));
        _triggers[0][1] = 3;
        _triggers[1][1] = 3;
    }

    public uint DeviceBase { get; private set; }
    public bool IsInstalled => _gateways.Count != 0;
    internal byte ControllerType(int unit) => _controllerTypes[unit];
    public long GetNextDeadline(long currentCycle, long targetCycle)
    {
        var result = targetCycle;
        for (var unit = 0; unit < 2; unit++)
        {
            if (_nextTimeout[unit] > currentCycle && _nextTimeout[unit] < result) result = _nextTimeout[unit];
            if (_nextJoystickRepeat[unit] > currentCycle && _nextJoystickRepeat[unit] < result) result = _nextJoystickRepeat[unit];
        }
        return result;
    }

    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14)) return IsInstalled;
        var device = FindDevice(execBase + DeviceListOffset, "gameport.device");
        if (device < 36 || !_bus.IsMappedMemoryRange(device - 36, 36)) return false;
        DeviceBase = device;
        _lastJoy[0] = _bus.ReadWord(0x00DFF00A); _lastJoy[1] = _bus.ReadWord(0x00DFF00C);
        for (var unit = 0; unit < 2; unit++) (_lastJoystickX[unit], _lastJoystickY[unit]) = DecodeJoystick(_lastJoy[unit]);
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
            if (IsJoystickController(unit)) ProcessJoystick(unit, joy, state.Cycles);
            else
            {
                var x = unchecked((sbyte)((byte)joy - (byte)_lastJoy[unit]));
                var y = unchecked((sbyte)((byte)(joy >> 8) - (byte)(_lastJoy[unit] >> 8)));
                _lastJoy[unit] = joy;
                if (x != 0 || y != 0) EnqueueMotion(unit, x, y);
            }
            var primary = unit == 0 ? _bus.GamePort0FirePressed : _bus.GamePort1FirePressed;
            var secondary = unit == 0 ? _bus.GamePort0SecondFirePressed : _bus.GamePort1SecondFirePressed;
            if (primary != _lastPrimary[unit]) { _lastPrimary[unit] = primary; EnqueueButton(unit, IeCodeLeftButton, 0x4000, primary); }
            if (secondary != _lastSecondary[unit]) { _lastSecondary[unit] = secondary; EnqueueButton(unit, IeCodeRightButton, 0x2000, secondary); }
            if (state.Cycles >= _nextTimeout[unit])
            {
                _events[unit].Enqueue(new RawGamePortEvent(IeCodeNoButton, 0, 0, _qualifiers[unit]));
                ScheduleTimeout(unit, state.Cycles);
            }
            if (_events[unit].Count == 0) continue;
            foreach (var entry in new List<PendingRead>(_pending.Values))
            {
                if (entry.Unit != unit) continue;
                if (_events[unit].Count == 0) break;
                if (WriteGamePortEvent(entry.Request, unit, state.Cycles)) _pending.Remove(entry.Request);
            }
        }
    }

    public void Reset() => Dispose();
    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        _gateways.Clear(); _units.Clear(); _pending.Clear(); Array.Clear(_controllerTypes); Array.Clear(_lastJoy); Array.Clear(_lastJoystickX); Array.Clear(_lastJoystickY); Array.Clear(_lastPrimary); Array.Clear(_lastSecondary); Array.Clear(_qualifiers); Array.Clear(_lastReportCycle); foreach (var events in _events) events.Clear(); _nextTimeout[0] = _nextTimeout[1] = long.MaxValue; _nextJoystickRepeat[0] = _nextJoystickRepeat[1] = long.MaxValue; _beginIoAddress = 0; _beginIoToken = 0; _forwardingBeginIo = false; DeviceBase = 0;
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
            case CmdClear:
                _events[unit].Clear(); Complete(request, 0, state.Cycles, true); break;
            case GpdAskCType:
                if (data == 0 || length < 1 || !_bus.IsMappedMemoryRange(data, 1)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                _bus.WriteByte(data, _controllerTypes[unit], state.Cycles); Complete(request, 0, state.Cycles, true); break;
            case GpdSetCType:
                if (data == 0 || length != 1 || !_bus.IsMappedMemoryRange(data, 1)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                _controllerTypes[unit] = _bus.ReadByte(data); ResetControllerSample(unit, state.Cycles); Complete(request, 0, state.Cycles, true); break;
            case GpdAskTrigger:
                if (data == 0 || length != 8 || !_bus.IsMappedMemoryRange(data, 8)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                for (var index = 0; index < 8; index++) _bus.WriteByte(data + (uint)index, _triggers[unit][index], state.Cycles); Complete(request, 0, state.Cycles, true); break;
            case GpdSetTrigger:
                if (data == 0 || length != 8 || !_bus.IsMappedMemoryRange(data, 8)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                for (var index = 0; index < 8; index++) _triggers[unit][index] = _bus.ReadByte(data + (uint)index); ScheduleTimeout(unit, state.Cycles); Complete(request, 0, state.Cycles, true); break;
            case GpdReadEvent:
                if (data == 0 || length < InputEventBytes || !_bus.IsMappedMemoryRange(data, InputEventBytes)) { Complete(request, IoErrBadLength, state.Cycles, true); break; }
                _bus.WriteByte(request + IoFlagsOffset, (byte)(_bus.ReadByte(request + IoFlagsOffset) & ~IoQuick), state.Cycles);
                if (_events[unit].Count != 0) WriteGamePortEvent(request, unit, state.Cycles); else { _pending[request] = new PendingRead(request, unit); ScheduleTimeout(unit, state.Cycles); }
                break;
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

    private void EnqueueMotion(int unit, int x, int y)
    {
        var xDelta = (_triggers[unit][4] << 8) | _triggers[unit][5];
        var yDelta = (_triggers[unit][6] << 8) | _triggers[unit][7];
        if ((xDelta != 0 && Math.Abs(x) < xDelta) || (yDelta != 0 && Math.Abs(y) < yDelta)) return;
        _events[unit].Enqueue(new RawGamePortEvent(IeCodeNoButton, (short)x, (short)y, (ushort)(_qualifiers[unit] | 0x8000)));
    }

    private void ScheduleTimeout(int unit, long cycle)
    {
        var ticks = (_triggers[unit][2] << 8) | _triggers[unit][3];
        _nextTimeout[unit] = ticks == 0 ? long.MaxValue : cycle + (long)ticks * FrameCycles;
    }

    private void EnqueueButton(int unit, byte code, ushort qualifier, bool down)
    {
        if (down) _qualifiers[unit] |= qualifier; else _qualifiers[unit] &= unchecked((ushort)~qualifier);
        var keys = (_triggers[unit][0] << 8) | _triggers[unit][1];
        if ((down && (keys & 1) == 0) || (!down && (keys & 2) == 0)) return;
        _events[unit].Enqueue(new RawGamePortEvent((byte)(down ? code : code | IeCodeUpPrefix), 0, 0, _qualifiers[unit]));
    }

    private bool WriteGamePortEvent(uint request, int unit, long cycle)
    {
        var data = _bus.ReadLong(request + IoDataOffset);
        if (data == 0 || !_bus.IsMappedMemoryRange(data, InputEventBytes)) { Complete(request, IoErrBadLength, cycle, true); return true; }
        var input = _events[unit].Dequeue();
        _bus.ClearMemory(data, InputEventBytes); _bus.WriteByte(data + 4, IeClassRawMouse, cycle); _bus.WriteByte(data + 5, (byte)unit, cycle);
        _bus.WriteWord(data + 6, input.Code, cycle); _bus.WriteWord(data + 8, input.Qualifiers, cycle); _bus.WriteWord(data + 0x0A, unchecked((ushort)input.X), cycle); _bus.WriteWord(data + 0x0C, unchecked((ushort)input.Y), cycle);
        var elapsedFrames = Math.Max(0, cycle - _lastReportCycle[unit]) / FrameCycles;
        _lastReportCycle[unit] = cycle;
        _bus.WriteLong(data + 0x10, unchecked((uint)elapsedFrames), cycle); _bus.WriteLong(data + 0x14, 0, cycle);
        Complete(request, 0, cycle, true, InputEventBytes); return true;
    }

    private long FrameCycles => _bus.RasterTiming.GetFrameCycles(_bus.RasterTiming.LongFrameLines);
    private bool IsJoystickController(int unit) => _controllerTypes[unit] is GpctRelativeJoystick or GpctAbsoluteJoystick;

    private void ResetControllerSample(int unit, long cycle)
    {
        var joy = _bus.ReadWord(unit == 0 ? 0x00DFF00Au : 0x00DFF00Cu);
        _lastJoy[unit] = joy;
        (_lastJoystickX[unit], _lastJoystickY[unit]) = DecodeJoystick(joy);
        _nextJoystickRepeat[unit] = _controllerTypes[unit] == GpctRelativeJoystick && (_lastJoystickX[unit] != 0 || _lastJoystickY[unit] != 0) ? cycle + FrameCycles : long.MaxValue;
    }

    private void ProcessJoystick(int unit, ushort joy, long cycle)
    {
        var (x, y) = DecodeJoystick(joy);
        var previousX = _lastJoystickX[unit]; var previousY = _lastJoystickY[unit];
        _lastJoy[unit] = joy; _lastJoystickX[unit] = x; _lastJoystickY[unit] = y;
        var changed = x != previousX || y != previousY;
        if (_controllerTypes[unit] == GpctAbsoluteJoystick)
        {
            if (changed && MeetsJoystickDelta(unit, previousX, previousY, x, y)) _events[unit].Enqueue(new RawGamePortEvent(IeCodeNoButton, x, y, _qualifiers[unit]));
            return;
        }
        if (x == 0 && y == 0) { _nextJoystickRepeat[unit] = long.MaxValue; return; }
        if ((changed && MeetsJoystickDelta(unit, previousX, previousY, x, y)) || cycle >= _nextJoystickRepeat[unit])
        {
            _events[unit].Enqueue(new RawGamePortEvent(IeCodeNoButton, x, y, _qualifiers[unit]));
            _nextJoystickRepeat[unit] = cycle + FrameCycles;
        }
    }

    private bool MeetsJoystickDelta(int unit, sbyte previousX, sbyte previousY, sbyte x, sbyte y)
    {
        var xDelta = (_triggers[unit][4] << 8) | _triggers[unit][5];
        var yDelta = (_triggers[unit][6] << 8) | _triggers[unit][7];
        return (xDelta != 0 && Math.Abs(x - previousX) >= xDelta) || (yDelta != 0 && Math.Abs(y - previousY) >= yDelta);
    }

    private static (sbyte X, sbyte Y) DecodeJoystick(ushort joy)
    {
        var right = (joy & 0x0002) != 0; var down = ((joy & 0x0001) != 0) ^ right;
        var left = (joy & 0x0200) != 0; var up = ((joy & 0x0100) != 0) ^ left;
        return ((sbyte)((right ? 1 : 0) - (left ? 1 : 0)), (sbyte)((down ? 1 : 0) - (up ? 1 : 0)));
    }

    private void Complete(uint request, byte error, long cycle, bool reply, uint actual = 0)
    {
        if (request == 0 || !_bus.IsMappedMemoryRange(request + IoErrorOffset, 1)) return;
        _bus.WriteByte(request + IoErrorOffset, error, cycle); _bus.WriteLong(request + IoActualOffset, error == 0 ? actual : 0u, cycle); _bus.WriteByte(request + 8, 7, cycle);
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
