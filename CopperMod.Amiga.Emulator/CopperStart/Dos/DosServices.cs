using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga;

namespace CopperMod.Amiga.CopperStart.Dos;

/// <summary>Isolated dos.library operations that do not require host file-system state.</summary>
internal sealed class DosServices
{
    private readonly CopperStartDosContext _context;
    private readonly Dictionary<uint, DosHandle> _handles = new();
    private uint _nextHandle = 0x0000_5000;
    private uint _lastError;
    private uint _nextLock = 0x0000_7000;
    private readonly Dictionary<uint, AmigaDosDirectoryEntry> _locks = new();
    private int _openDiagnostics, _readDiagnostics, _writeDiagnostics, _genericDiagnostics, _readArgsDiagnostics;
    public DosServices(CopperStartDosContext context) => _context = context ?? throw new ArgumentNullException(nameof(context));

    public void CurrentDir(M68kCpuState state) => state.D[0] = _context.CurrentDirectoryLock;

    public void Reset() { _handles.Clear(); _locks.Clear(); _nextHandle = 0x0000_5000; _nextLock = 0x0000_7000; _lastError = 0; _openDiagnostics = _readDiagnostics = _writeDiagnostics = _genericDiagnostics = _readArgsDiagnostics = 0; }
    public void Open(M68kCpuState state)
    {
        var path = _context.ReadPath(state.D[1]);
        if (path.StartsWith("con:", StringComparison.OrdinalIgnoreCase)) { state.D[0] = CreateHandle(path, Array.Empty<byte>(), true); return; }
        var data = string.IsNullOrWhiteSpace(path) ? null : _context.ReadFile(path);
        if (data is null) { state.D[0] = 0; _lastError = 205; Diagnose(ref _openDiagnostics, "AMIGA_BOOT_DOS_OPEN_MISSING", $"Open failed for '{path}'.", 16); return; }
        var handle = CreateHandle(path, data, false); state.D[0] = handle; _lastError = 0; Diagnose(ref _openDiagnostics, "AMIGA_BOOT_DOS_OPEN", $"Opened '{path}' as 0x{handle:X8}.", 16);
    }
    public void Close(M68kCpuState state) { _handles.Remove(state.D[1]); state.D[0] = 0; }
    public void Input(M68kCpuState state) => Output(state);
    public void Output(M68kCpuState state)
    {
        foreach (var pair in _handles) if (pair.Value.IsConsole) { state.D[0] = pair.Key; return; }
        state.D[0] = CreateHandle("con:", Array.Empty<byte>(), true);
    }
    public void Write(M68kCpuState state)
    {
        if (!_handles.TryGetValue(state.D[1], out var handle) || !handle.IsConsole) { state.D[0] = 0xFFFF_FFFF; return; }
        Diagnose(ref _writeDiagnostics, "AMIGA_BOOT_DOS_WRITE", _context.ReadText(state.D[2], checked((int)Math.Min(state.D[3], 160))), 16); state.D[0] = state.D[3];
    }
    public void Read(M68kCpuState state)
    {
        if (!_handles.TryGetValue(state.D[1], out var handle)) { state.D[0] = 0xFFFF_FFFF; return; }
        var requested = (int)Math.Min(state.D[3], int.MaxValue); var count = Math.Min(requested, Math.Max(0, handle.Data.Length - handle.Position));
        if (count > 0 && _context.Memory.IsMapped(state.D[2], count)) { for (var i = 0; i < count; i++) _context.Memory.WriteByte(state.D[2] + (uint)i, handle.Data[handle.Position + i]); handle.Position += count; }
        Diagnose(ref _readDiagnostics, "AMIGA_BOOT_DOS_READ", $"Read {count}/0x{requested:X} bytes from '{handle.Path}' into 0x{state.D[2]:X8}.", 24); state.D[0] = (uint)count;
    }
    public void Seek(M68kCpuState state)
    {
        if (!_handles.TryGetValue(state.D[1], out var handle)) { state.D[0] = 0xFFFF_FFFF; return; }
        var old = handle.Position; var origin = unchecked((int)state.D[3]) switch { 1 => handle.Data.Length, -1 => 0, _ => handle.Position }; handle.Position = Math.Clamp(origin + unchecked((int)state.D[2]), 0, handle.Data.Length); state.D[0] = (uint)old;
    }
    public void IoErr(M68kCpuState state) => state.D[0] = _lastError;
    public void Lock(M68kCpuState state)
    {
        var entry = _context.ReadPath(state.D[1]) is var path && !string.IsNullOrWhiteSpace(path) ? _context.FindEntry(path) : null;
        if (entry is null) { state.D[0] = 0; _lastError = 205; return; }
        var handle = _nextLock; _nextLock += 4; _locks[handle] = entry.Value; state.D[0] = handle; _lastError = 0;
    }
    public void UnLock(M68kCpuState state) { _locks.Remove(state.D[1]); state.D[0] = 0; _lastError = 0; }
    public void Examine(M68kCpuState state)
    {
        if (!_locks.TryGetValue(state.D[1], out var entry)) { state.D[0] = 0; _lastError = 205; return; }
        _context.WriteFileInfoBlock(state.D[2], entry); state.D[0] = 1; _lastError = 0;
    }
    public void DupLock(M68kCpuState state)
    {
        var source = state.D[1]; if (source == 0 || source == _context.CurrentDirectoryLock) { state.D[0] = _context.CurrentDirectoryLock; _lastError = 0; return; }
        if (!_locks.TryGetValue(source, out var entry)) { state.D[0] = _context.CurrentDirectoryLock; _lastError = 0; return; }
        var handle = _nextLock; _nextLock += 4; _locks[handle] = entry; state.D[0] = handle; _lastError = 0;
    }
    public void InvokeGeneric(M68kCpuState state, int lvo)
    {
        switch (lvo)
        {
            case -798: ReadArgs(state); return;
            case -96: DupLock(state); return;
            case -192: DateStamp(state); return;
            case -198: Delay(state); return;
            case -858 or -474: state.D[0] = 0; return;
        }

        Diagnose(ref _genericDiagnostics, "AMIGA_BOOT_DOS_GENERIC", $"DOS library call {lvo} returned a host-bridge default value.", 24);
        state.D[0] = lvo switch { -78 => 0xFFFF_FFFF, -300 => 1, _ => 0 };
    }

    public void Delay(M68kCpuState state)
    {
        var ticks = Math.Clamp(unchecked((int)state.D[1]), 0, 60 * 50);
        state.Cycles += (long)ticks * AmigaConstants.A500PalCpuCyclesPerFrame;
        state.D[0] = 0;
    }

    public void DateStamp(M68kCpuState state)
    {
        var stamp = state.D[1];
        if (stamp != 0 && _context.Memory.IsMapped(stamp, 12))
        {
            var ticks = Math.Max(0, state.Cycles / AmigaConstants.A500PalCpuCyclesPerFrame);
            var minutes = ticks / (50 * 60);
            _context.Memory.WriteLong(stamp, (uint)(minutes / (24 * 60)));
            _context.Memory.WriteLong(stamp + 4, (uint)(minutes % (24 * 60)));
            _context.Memory.WriteLong(stamp + 8, (uint)(ticks % (50 * 60)));
        }
        state.D[0] = stamp;
    }
    public void ReadArgs(M68kCpuState state)
    {
        var template = state.D[1] != 0 && _context.Memory.IsMapped(state.D[1], 1) ? _context.ReadText(state.D[1], 160) : string.Empty;
        var count = CountTemplateEntries(template);
        if (state.D[2] != 0 && count > 0 && _context.Memory.IsMapped(state.D[2], checked(count * 4))) for (var i = 0; i < count; i++) _context.Memory.WriteLong(state.D[2] + (uint)(i * 4), 0);
        var rdArgs = state.D[3] != 0 ? state.D[3] : _context.AllocateMemory(0x400, 0x0001_0001); state.D[0] = rdArgs; _lastError = rdArgs != 0 ? 0u : 103u;
        Diagnose(ref _readArgsDiagnostics, "AMIGA_BOOT_DOS_READ_ARGS", $"ReadArgs parsed an empty host command line for template '{template}'.", 8);
    }
    private static int CountTemplateEntries(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return 0; var count = 0; var text = false;
        foreach (var character in template) { if (character == ',') { if (text) { count++; text = false; } } else text |= !char.IsWhiteSpace(character); }
        return text ? count + 1 : count;
    }
    private uint CreateHandle(string path, byte[] data, bool console) { var handle = _nextHandle; _nextHandle += 4; _handles.Add(handle, new DosHandle(path, data, console)); return handle; }
    private void Diagnose(ref int count, string code, string message, int limit) { if (count++ < limit) _context.AddDiagnostic(code, message); }
    private sealed class DosHandle(string path, byte[] data, bool isConsole) { public string Path { get; } = path; public byte[] Data { get; } = data; public bool IsConsole { get; } = isConsole; public int Position { get; set; } }
}
