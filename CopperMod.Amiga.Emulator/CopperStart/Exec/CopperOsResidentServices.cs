using System;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>
/// Publishes CopperStart's identity through Exec's normal resident-module
/// chain.  The ROM resident table remains intact: ExecBase is redirected to a
/// reset-scoped copy with one additional, inert CopperOS resident.
/// </summary>
internal sealed class CopperOsResidentServices
{
    private const int ResModulesOffset = 0x12C;
    private const int ResidentBytes = 0x60;
    private const uint MemfPublicClear = 0x0001_0001;
    private const ushort MatchWord = 0x4AFC;
    private const uint EndMarker = 0xFFFF_FFFF;
    private readonly CopperStartExecContext _context;
    private uint _execBase;
    private uint _originalModuleTable;
    private uint _moduleTable;
    private int _moduleTableBytes;
    private uint _resident;

    public CopperOsResidentServices(CopperStartExecContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public uint ResidentAddress => _resident;

    public bool IsInstalled => _resident != 0;

    public bool Install()
    {
        if (IsInstalled) return true;

        var execBase = _context.GetExecBase();
        if (execBase == 0 || !_context.Memory.IsMapped(execBase + ResModulesOffset, 4)) return false;

        var original = _context.Memory.ReadLong(execBase + ResModulesOffset);
        if (!TryReadModuleTable(original, out var count)) return false;

        var resident = _context.MemoryOperations.Allocate(ResidentBytes, MemfPublicClear);
        var tableBytes = checked((count + 2) * 4);
        var table = resident == 0 ? 0 : _context.MemoryOperations.Allocate(tableBytes, MemfPublicClear);
        if (resident == 0 || table == 0)
        {
            if (table != 0) _context.MemoryOperations.Free(table, tableBytes);
            if (resident != 0) _context.MemoryOperations.Free(resident, ResidentBytes);
            return false;
        }

        try
        {
            for (var index = 0; index < count; index++)
                _context.Memory.WriteLong(table + (uint)(index * 4), _context.Memory.ReadLong(original + (uint)(index * 4)));
            _context.Memory.WriteLong(table + (uint)(count * 4), resident);
            _context.Memory.WriteLong(table + (uint)((count + 1) * 4), EndMarker);
            WriteResident(resident);
            _context.Memory.WriteLong(execBase + ResModulesOffset, table);
        }
        catch
        {
            _context.MemoryOperations.Free(table, tableBytes);
            _context.MemoryOperations.Free(resident, ResidentBytes);
            return false;
        }

        _execBase = execBase;
        _originalModuleTable = original;
        _moduleTable = table;
        _moduleTableBytes = tableBytes;
        _resident = resident;
        return true;
    }

    /// <summary>
    /// AddResident(resident=A1) through the owned module-table copy.  It never
    /// writes the ROM's original table and ignores duplicate/invalid tags.
    /// </summary>
    public uint AddResident(Copper68k.M68kCpuState state)
    {
        var resident = state.A[1];
        if (!Install() || !IsValidResident(resident) || ContainsResident(resident)) return 0;

        var count = 0;
        while (_context.Memory.ReadLong(_moduleTable + (uint)(count * 4)) != EndMarker) count++;
        var replacementBytes = checked((count + 2) * 4);
        var replacement = _context.MemoryOperations.Allocate(replacementBytes, MemfPublicClear);
        if (replacement == 0) return 0;

        try
        {
            for (var index = 0; index < count; index++)
                _context.Memory.WriteLong(replacement + (uint)(index * 4), _context.Memory.ReadLong(_moduleTable + (uint)(index * 4)));
            _context.Memory.WriteLong(replacement + (uint)(count * 4), resident);
            _context.Memory.WriteLong(replacement + (uint)((count + 1) * 4), EndMarker);
            _context.Memory.WriteLong(_execBase + ResModulesOffset, replacement);
        }
        catch
        {
            _context.MemoryOperations.Free(replacement, replacementBytes);
            return 0;
        }

        _context.MemoryOperations.Free(_moduleTable, _moduleTableBytes);
        _moduleTable = replacement;
        _moduleTableBytes = replacementBytes;
        return 0;
    }

    public void Reset()
    {
        if (_moduleTable != 0 && _execBase != 0 && _context.Memory.IsMapped(_execBase + ResModulesOffset, 4) &&
            _context.Memory.ReadLong(_execBase + ResModulesOffset) == _moduleTable)
            _context.Memory.WriteLong(_execBase + ResModulesOffset, _originalModuleTable);

        if (_moduleTable != 0) _context.MemoryOperations.Free(_moduleTable, _moduleTableBytes);
        if (_resident != 0) _context.MemoryOperations.Free(_resident, ResidentBytes);
        _execBase = 0;
        _originalModuleTable = 0;
        _moduleTable = 0;
        _moduleTableBytes = 0;
        _resident = 0;
    }

    private bool TryReadModuleTable(uint table, out int count)
    {
        count = 0;
        if (table == 0) return true;
        for (; count < 2048; count++)
        {
            var entry = table + (uint)(count * 4);
            if (!_context.Memory.IsMapped(entry, 4)) return false;
            if (_context.Memory.ReadLong(entry) == EndMarker) return true;
        }

        return false;
    }

    private bool ContainsResident(uint resident)
    {
        for (var index = 0; _context.Memory.ReadLong(_moduleTable + (uint)(index * 4)) != EndMarker; index++)
            if (_context.Memory.ReadLong(_moduleTable + (uint)(index * 4)) == resident) return true;
        return false;
    }

    private bool IsValidResident(uint resident)
        => resident != 0 && _context.Memory.IsMapped(resident, 0x1A) &&
           _context.Memory.ReadWord(resident) == MatchWord &&
           _context.Memory.ReadLong(resident + 2) == resident &&
           _context.Memory.ReadLong(resident + 6) > resident;

    private void WriteResident(uint resident)
    {
        const uint nameOffset = 0x20;
        const uint idOffset = 0x30;
        _context.Memory.WriteWord(resident, MatchWord);
        _context.Memory.WriteLong(resident + 0x02, resident);
        _context.Memory.WriteLong(resident + 0x06, resident + ResidentBytes);
        _context.Memory.WriteByte(resident + 0x0A, 0); // inert; it identifies CopperOS but is never boot-initialized.
        _context.Memory.WriteByte(resident + 0x0B, 1);
        _context.Memory.WriteByte(resident + 0x0C, 0);
        _context.Memory.WriteByte(resident + 0x0D, 0);
        _context.Memory.WriteLong(resident + 0x0E, resident + nameOffset);
        _context.Memory.WriteLong(resident + 0x12, resident + idOffset);
        _context.Memory.WriteLong(resident + 0x16, 0);
        WriteAscii(resident + nameOffset, "CopperOS");
        WriteAscii(resident + idOffset, "CopperOS 1.0");
    }

    private void WriteAscii(uint address, string value)
    {
        for (var index = 0; index < value.Length; index++) _context.Memory.WriteByte(address + (uint)index, (byte)value[index]);
        _context.Memory.WriteByte(address + (uint)value.Length, 0);
    }
}
