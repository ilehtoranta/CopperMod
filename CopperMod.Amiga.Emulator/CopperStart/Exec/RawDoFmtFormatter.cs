using System;
using System.Text;
using CopperMod.Amiga.Bus;

namespace CopperMod.Amiga.CopperStart.Exec;

/// <summary>Guest-memory formatter used by Exec RawDoFmt. Bare integers are 16-bit.</summary>
internal static class RawDoFmtFormatter
{
    public static string Format(AmigaBus bus, uint format, uint data, Func<uint, string> readString, out uint nextData)
    {
        var output = new StringBuilder();
        for (var index = 0; index < 1024 && bus.IsMappedMemoryRange(format + (uint)index, 1); index++)
        {
            var character = bus.ReadByte(format + (uint)index);
            if (character == 0) break;
            if (character != '%') { output.Append((char)character); continue; }
            character = bus.ReadByte(format + (uint)++index);
            if (character == '%') { output.Append('%'); continue; }
            var longValue = false;
            while (character is (byte)'l' or (byte)'L') { longValue = true; character = bus.ReadByte(format + (uint)++index); }
            var value = longValue ? bus.ReadLong(data) : bus.ReadWord(data);
            data += longValue ? 4u : 2u;
            output.Append(character switch
            {
                (byte)'d' or (byte)'i' => longValue ? unchecked((int)value).ToString() : unchecked((short)value).ToString(),
                (byte)'u' => longValue ? value.ToString() : ((ushort)value).ToString(),
                (byte)'x' or (byte)'X' => longValue ? value.ToString(character == 'X' ? "X" : "x") : ((ushort)value).ToString(character == 'X' ? "X" : "x"),
                (byte)'c' => ((char)(value & 0xff)).ToString(),
                (byte)'s' => readString(value),
                _ => (longValue ? unchecked((int)value) : unchecked((short)value)).ToString()
            });
        }
        nextData = data;
        return output.ToString();
    }
}
