using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Devices.Input;
using CopperMod.Amiga.CopperStart.Exec;

namespace CopperMod.Amiga.CopperStart.Devices.Console;

/// <summary>
/// Host implementation of the ROM-created console.device.  The device base
/// and the caller supplied Intuition Window remain guest owned; only the
/// console unit state and its I/O queues live here.
/// </summary>
internal sealed class ConsoleDeviceServices : IDisposable
{
    private const int DeviceListOffset = 0x15E, NodeNameOffset = 0x0A, LibraryOpenCountOffset = 0x20;
    private const int IoDeviceOffset = 0x14, IoUnitOffset = 0x18, IoCommandOffset = 0x1C, IoFlagsOffset = 0x1E, IoErrorOffset = 0x1F, IoActualOffset = 0x20, IoLengthOffset = 0x24, IoDataOffset = 0x28;
    private const int WindowRPortOffset = 0x32;
    private const ushort CmdRead = 2, CmdWrite = 3, CmdClear = 5;
    private const byte IoQuick = 1, IoErrOpenFail = 0xFF, IoErrAborted = 0xFE, IoErrNoCommand = 0xFD, IoErrBadAddress = 0xFC, IeClassRawKey = 1;
    private const uint MemfPublicClear = 0x0001_0001;
    private readonly AmigaBus _bus;
    private readonly ExecMemoryOperations _memory;
    private readonly InputDeviceServices _input;
    private readonly Action<uint> _reply;
    private readonly Action<M68kCpuState, uint, uint, uint> _drawText;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Dictionary<uint, ConsoleUnit> _units = new();
    private readonly List<PendingRead> _pendingReads = new();
    private readonly record struct PendingRead(uint Request, uint Unit);

    private sealed class ConsoleUnit
    {
        public ConsoleUnit(uint request, int number, uint window, uint rastPort, uint scratch) { Request = request; Number = number; Window = window; RastPort = rastPort; Scratch = scratch; }
        public uint Request { get; } public int Number { get; } public uint Window { get; } public uint RastPort { get; } public uint Scratch { get; }
        public Queue<byte> Input { get; } = new(); public List<byte> History { get; } = new();
        public int CursorX { get; set; } public int CursorY { get; set; }
        public bool Escape { get; set; } public bool CsiActive { get; set; } public List<byte> Csi { get; } = new(); public bool RawKeys { get; set; }
    }

    public ConsoleDeviceServices(AmigaBus bus, ExecMemoryOperations memory, InputDeviceServices input, Action<uint> reply, Action<M68kCpuState, uint, uint, uint> drawText)
    { _bus = bus; _memory = memory; _input = input; _reply = reply; _drawText = drawText; _input.InputEventObserved += ObserveInput; }

    public uint DeviceBase { get; private set; }
    public bool IsInstalled => _gateways.Count != 0;

    public bool TryInstall(uint execBase)
    {
        if (IsInstalled || execBase == 0 || !_bus.IsMappedMemoryRange(execBase + DeviceListOffset, 14)) return IsInstalled;
        var device = FindDevice(execBase + DeviceListOffset, "console.device");
        if (device < 48 || !_bus.IsMappedMemoryRange(device - 48, 48)) return false;
        DeviceBase = device;
        Register(-6, Open); Register(-12, Close); Register(-18, Expunge); Register(-24, ExtFunc); Register(-30, BeginIo); Register(-36, AbortIo);
        Register(-42, CdInputHandler); Register(-48, RawKeyConvert);
        return true;
    }

    public void Reset() => Dispose();
    public void Dispose()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--) _bus.RemoveHostGateway(_gateways[index].Address, _gateways[index].Token);
        foreach (var unit in _units.Values) if (unit.Scratch != 0) _memory.Free(unit.Scratch, 256);
        _gateways.Clear(); _units.Clear(); _pendingReads.Clear(); DeviceBase = 0;
    }

    private void Open(M68kCpuState state)
    {
        var request = state.A[1]; var number = unchecked((int)state.D[0]);
        var window = request != 0 && _bus.IsMappedMemoryRange(request + IoDataOffset, 4) ? _bus.ReadLong(request + IoDataOffset) : 0;
        var libraryOnly = number == -1;
        var rastPort = window != 0 && _bus.IsMappedMemoryRange(window + WindowRPortOffset, 4) ? _bus.ReadLong(window + WindowRPortOffset) : 0;
        var scratch = _memory.Allocate(256, MemfPublicClear);
        if (request == 0 || scratch == 0 || (!libraryOnly && (window == 0 || rastPort == 0))) { if (scratch != 0) _memory.Free(scratch, 256); Complete(request, IoErrOpenFail, 0, state.Cycles, false); state.D[0] = IoErrOpenFail; return; }
        _units[request] = new ConsoleUnit(request, number, window, rastPort, scratch);
        _bus.WriteLong(request + IoDeviceOffset, DeviceBase, state.Cycles); _bus.WriteLong(request + IoUnitOffset, request, state.Cycles);
        var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(count + 1)), state.Cycles);
        Complete(request, 0, 0, state.Cycles, false); state.D[0] = 0;
    }

    private void Close(M68kCpuState state)
    {
        var request = state.A[1]; CancelReads(request, state.Cycles); if (_units.Remove(request, out var unit)) _memory.Free(unit.Scratch, 256);
        var count = _bus.ReadWord(DeviceBase + LibraryOpenCountOffset); if (count != 0) _bus.WriteWord(DeviceBase + LibraryOpenCountOffset, unchecked((ushort)(count - 1)), state.Cycles); state.D[0] = 0;
    }
    private static void Expunge(M68kCpuState state) => state.D[0] = 0;
    private static void ExtFunc(M68kCpuState state) => state.D[0] = 0;

    private void BeginIo(M68kCpuState state)
    {
        var request = state.A[1]; if (!_units.TryGetValue(request, out var unit) || _bus.ReadLong(request + IoDeviceOffset) != DeviceBase) return;
        switch (_bus.ReadWord(request + IoCommandOffset))
        {
            case CmdRead: StartRead(request, unit, state.Cycles); break;
            case CmdWrite: Write(request, unit, state); break;
            case CmdClear: unit.Input.Clear(); unit.History.Clear(); unit.CursorX = unit.CursorY = 0; Complete(request, 0, 0, state.Cycles, true); break;
            default: Complete(request, IoErrNoCommand, 0, state.Cycles, true); break;
        }
    }

    private void AbortIo(M68kCpuState state)
    {
        var request = state.A[1]; for (var index = 0; index < _pendingReads.Count; index++) if (_pendingReads[index].Request == request) { _pendingReads.RemoveAt(index); Complete(request, IoErrAborted, 0, state.Cycles, true); state.D[0] = 0; return; } state.D[0] = uint.MaxValue;
    }

    private void StartRead(uint request, ConsoleUnit unit, long cycle)
    {
        if (unit.Input.Count != 0) CompleteRead(request, unit, cycle);
        else { _bus.WriteByte(request + IoFlagsOffset, (byte)(_bus.ReadByte(request + IoFlagsOffset) & ~IoQuick), cycle); _pendingReads.Add(new PendingRead(request, unit.Request)); }
    }

    private void Write(uint request, ConsoleUnit unit, M68kCpuState state)
    {
        var source = _bus.ReadLong(request + IoDataOffset); var length = Math.Min(_bus.ReadLong(request + IoLengthOffset), 0x10000u);
        if (source == 0 || !_bus.IsMappedMemoryRange(source, checked((int)length))) { Complete(request, IoErrBadAddress, 0, state.Cycles, true); return; }
        var run = new List<byte>();
        for (uint index = 0; index < length; index++) ParseOutput(unit, _bus.ReadByte(source + index), run);
        FlushRun(unit, run, state); Complete(request, 0, length, state.Cycles, true);
    }

    private void ParseOutput(ConsoleUnit unit, byte value, List<byte> run)
    {
        if (unit.CsiActive)
        {
            unit.Csi.Add(value); if (value is >= 0x40 and <= 0x7E) { ApplyCsi(unit, value); unit.Csi.Clear(); unit.CsiActive = false; unit.Escape = false; } return;
        }
        if (unit.Escape) { unit.Escape = false; if (value == (byte)'[') { unit.Csi.Clear(); unit.CsiActive = true; return; } if (value is (byte)'7' or (byte)'8') return; return; }
        if (value == 0x1B) { FlushRun(unit, run, null); unit.Escape = true; return; }
        if (value == '\r') { FlushRun(unit, run, null); unit.CursorX = 0; return; }
        if (value == '\n') { FlushRun(unit, run, null); unit.CursorY++; return; }
        if (value == '\b') { FlushRun(unit, run, null); unit.CursorX = Math.Max(0, unit.CursorX - 1); return; }
        if (value == '\t') { FlushRun(unit, run, null); unit.CursorX = (unit.CursorX + 8) & ~7; return; }
        if (value >= 0x20) { run.Add(value); unit.History.Add(value); unit.CursorX++; }
    }

    private static void ApplyCsi(ConsoleUnit unit, byte final)
    {
        var text = System.Text.Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(unit.Csi));
        var parts = text[..^1].Split(';'); var first = parts.Length == 0 || !int.TryParse(parts[0], out var value) ? 1 : Math.Max(1, value);
        switch ((char)final) { case 'A': unit.CursorY = Math.Max(0, unit.CursorY - first); break; case 'B': unit.CursorY += first; break; case 'C': unit.CursorX += first; break; case 'D': unit.CursorX = Math.Max(0, unit.CursorX - first); break; case 'H': case 'f': unit.CursorX = unit.CursorY = 0; break; }
    }

    private void FlushRun(ConsoleUnit unit, List<byte> run, M68kCpuState? state)
    {
        if (run.Count == 0 || state is null || unit.RastPort == 0) { run.Clear(); return; }
        var count = Math.Min(run.Count, 255); for (var index = 0; index < count; index++) _bus.WriteByte(unit.Scratch + (uint)index, run[index], state.Cycles);
        _drawText(state, unit.RastPort, unit.Scratch, (uint)count); run.Clear();
    }

    private void ObserveInput(InputDeviceServices.ObservedInputEvent input)
    {
        if (input.Class != IeClassRawKey || (input.Code & 0x80) != 0) return;
        var character = Translate((byte)input.Code, input.Qualifier);
        if (character == 0) return;
        foreach (var unit in _units.Values) { unit.Input.Enqueue(character); CompleteUnitReads(unit, 0); }
    }
    private void CdInputHandler(M68kCpuState state) { var address = state.A[0]; for (var count = 0; address != 0 && count < 256 && _bus.IsMappedMemoryRange(address, 16); count++) { ObserveInput(new InputDeviceServices.ObservedInputEvent(address, _bus.ReadByte(address + 4), _bus.ReadByte(address + 5), _bus.ReadWord(address + 6), _bus.ReadWord(address + 8), _bus.ReadLong(address))); address = _bus.ReadLong(address); } state.D[0] = state.A[0]; }
    private void RawKeyConvert(M68kCpuState state) { var value = Translate((byte)state.D[0], 0); if (state.A[1] != 0 && state.D[1] != 0 && value != 0) { _bus.WriteByte(state.A[1], value, state.Cycles); state.D[0] = 1; } else state.D[0] = 0; }
    private static byte Translate(byte raw, ushort qualifier) => raw switch { 0x00 => (byte)'`', >= 0x01 and <= 0x0A => (byte)("1234567890"[raw - 1]), 0x10 => (byte)'q', 0x11 => (byte)'w', 0x12 => (byte)'e', 0x13 => (byte)'r', 0x14 => (byte)'t', 0x15 => (byte)'y', 0x16 => (byte)'u', 0x17 => (byte)'i', 0x18 => (byte)'o', 0x19 => (byte)'p', 0x20 => (byte)'a', 0x21 => (byte)'s', 0x22 => (byte)'d', 0x23 => (byte)'f', 0x24 => (byte)'g', 0x25 => (byte)'h', 0x26 => (byte)'j', 0x27 => (byte)'k', 0x28 => (byte)'l', 0x31 => (byte)'z', 0x32 => (byte)'x', 0x33 => (byte)'c', 0x34 => (byte)'v', 0x35 => (byte)'b', 0x36 => (byte)'n', 0x37 => (byte)'m', 0x40 => (byte)' ', 0x44 => (byte)'\r', _ => 0 };

    private void CompleteUnitReads(ConsoleUnit unit, long cycle) { for (var index = 0; index < _pendingReads.Count && unit.Input.Count != 0;) { var pending = _pendingReads[index]; if (pending.Unit != unit.Request) { index++; continue; } _pendingReads.RemoveAt(index); CompleteRead(pending.Request, unit, cycle); } }
    private void CompleteRead(uint request, ConsoleUnit unit, long cycle) { var data = _bus.ReadLong(request + IoDataOffset); var length = _bus.ReadLong(request + IoLengthOffset); if (data == 0 || length == 0 || !_bus.IsMappedMemoryRange(data, 1)) { Complete(request, IoErrBadAddress, 0, cycle, true); return; } var actual = 0u; while (actual < length && unit.Input.Count != 0 && _bus.IsMappedMemoryRange(data + actual, 1)) { _bus.WriteByte(data + actual++, unit.Input.Dequeue(), cycle); } Complete(request, 0, actual, cycle, true); }
    private void CancelReads(uint unit, long cycle) { for (var index = _pendingReads.Count - 1; index >= 0; index--) if (_pendingReads[index].Unit == unit) { var request = _pendingReads[index].Request; _pendingReads.RemoveAt(index); Complete(request, IoErrAborted, 0, cycle, true); } }
    private void Complete(uint request, byte error, uint actual, long cycle, bool reply) { if (request == 0 || !_bus.IsMappedMemoryRange(request + IoErrorOffset, 9)) return; _bus.WriteByte(request + IoErrorOffset, error, cycle); _bus.WriteLong(request + IoActualOffset, error == 0 ? actual : 0, cycle); if (reply && (_bus.ReadByte(request + IoFlagsOffset) & IoQuick) == 0) _reply(request); }
    private void Register(int lvo, Action<M68kCpuState> callback) { var address = unchecked((uint)((int)DeviceBase + lvo)); _gateways.Add((address, _bus.RegisterHostGateway(address, callback))); }
    private uint FindDevice(uint list, string name) { for (var node = _bus.ReadLong(list); node != 0 && node != list + 4 && _bus.IsMappedMemoryRange(node, NodeNameOffset + 4); node = _bus.ReadLong(node)) if (string.Equals(ReadName(_bus.ReadLong(node + NodeNameOffset)), name, StringComparison.OrdinalIgnoreCase)) return node; return 0; }
    private string ReadName(uint address) { Span<char> value = stackalloc char[64]; var length = 0; while (address != 0 && length < value.Length && _bus.IsMappedMemoryRange(address + (uint)length, 1)) { var c = _bus.ReadByte(address + (uint)length); if (c == 0) break; value[length++] = (char)c; } return new string(value[..length]); }
}
