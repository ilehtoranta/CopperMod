using Copper68k;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>KS 3.x InitStruct table decoder for ordinary guest RAM structures.</summary>
internal sealed class ExecInitStructServices
{
    private readonly CopperStartExecContext _context;
    public ExecInitStructServices(CopperStartExecContext context) => _context = context;

    public uint InitStruct(M68kCpuState state)
    {
        var table = state.A[1]; var memory = state.A[2]; var size = state.D[0];
        if (table == 0 || memory == 0 || !_context.Memory.IsMapped(table, 1)) return 0;
        if (size != 0)
        {
            if (size > int.MaxValue || !_context.Memory.IsMapped(memory, (int)size)) return 0;
            for (uint offset = 0; offset < size; offset++) _context.Memory.WriteByte(memory + offset, 0);
        }

        uint cursor = table, destination = 0;
        for (var commands = 0; commands < 4096; commands++)
        {
            cursor = (cursor + 1) & ~1u;
            if (!_context.Memory.IsMapped(cursor, 1)) break;
            var command = _context.Memory.ReadByte(cursor++);
            if (command == 0) break;
            var destinationMode = command >> 6; var sourceMode = (command >> 4) & 3; var count = (command & 15) + 1;
            if (sourceMode == 3) break;
            if (destinationMode == 2)
            {
                if (!_context.Memory.IsMapped(cursor, 1)) break;
                destination = _context.Memory.ReadByte(cursor++);
            }
            else if (destinationMode == 3)
            {
                if (!_context.Memory.IsMapped(cursor, 3)) break;
                destination = ((uint)_context.Memory.ReadByte(cursor) << 16) | ((uint)_context.Memory.ReadByte(cursor + 1) << 8) | _context.Memory.ReadByte(cursor + 2); cursor += 3;
            }

            var width = sourceMode switch { 0 => 4, 1 => 2, _ => 1 };
            var repeated = destinationMode == 1;
            uint value = 0;
            for (var index = 0; index < count; index++)
            {
                if (!repeated || index == 0)
                {
                    if (!TryReadSource(ref cursor, sourceMode, out value)) return 0;
                }
                if (size != 0 && destination + (uint)width > size) return 0;
                if (!_context.Memory.IsMapped(memory + destination, width)) return 0;
                if (width == 4) _context.Memory.WriteLong(memory + destination, value);
                else if (width == 2) _context.Memory.WriteWord(memory + destination, (ushort)value);
                else _context.Memory.WriteByte(memory + destination, (byte)value);
                destination += (uint)width;
            }
        }
        return 0;
    }

    private bool TryReadSource(ref uint cursor, int mode, out uint value)
    {
        if (mode == 2) { if (!_context.Memory.IsMapped(cursor, 1)) { value = 0; return false; } value = _context.Memory.ReadByte(cursor++); return true; }
        cursor = (cursor + 1) & ~1u;
        var width = mode == 0 ? 4 : 2;
        if (!_context.Memory.IsMapped(cursor, width)) { value = 0; return false; }
        value = width == 4 ? _context.Memory.ReadLong(cursor) : _context.Memory.ReadWord(cursor); cursor += (uint)width; return true;
    }
}
