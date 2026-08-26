using System;
using System.Collections.Generic;
using System.Text;

namespace CopperMod.Amiga.CopperStart.Devices.Clipboard;

/// <summary>Small, allocation-bounded FTXT codec for the primary clipboard unit.</summary>
internal static class ClipboardIffText
{
    private const uint Form = 0x464F524D; // FORM
    private const uint Ftxt = 0x46545854; // FTXT
    private const uint Chrs = 0x43485253; // CHRS
    private const uint Utf8 = 0x55544638; // UTF8

    public static byte[] Encode(string text)
    {
        text ??= string.Empty;
        var legacy = Encoding.Latin1.GetBytes(text);
        var unicode = Encoding.UTF8.GetBytes(text);
        var bytes = new List<byte>(24 + legacy.Length + unicode.Length + 2);
        WriteFourCc(bytes, Form); WriteLong(bytes, 0); WriteFourCc(bytes, Ftxt);
        WriteChunk(bytes, Chrs, legacy); WriteChunk(bytes, Utf8, unicode);
        WriteLongAt(bytes, 4, checked((uint)(bytes.Count - 8)));
        return bytes.ToArray();
    }

    /// <summary>Reads all text chunks, preferring UTF8 when a producer supplies both.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out string text)
    {
        text = string.Empty;
        if (data.Length < 12 || ReadLong(data, 0) != Form || ReadLong(data, 8) != Ftxt) return false;
        var formLength = ReadLong(data, 4);
        var end = (int)Math.Min(data.Length, (long)formLength + 8);
        var legacy = new List<byte>(); var unicode = new List<byte>();
        for (var offset = 12; offset + 8 <= end;)
        {
            var type = ReadLong(data, offset); var length = ReadLong(data, offset + 4);
            var content = offset + 8; var next = (long)content + length;
            if (next > end) return false;
            if (type == Chrs) legacy.AddRange(data.Slice(content, checked((int)length)).ToArray());
            else if (type == Utf8) unicode.AddRange(data.Slice(content, checked((int)length)).ToArray());
            offset = checked((int)(next + (length & 1)));
        }
        if (unicode.Count != 0) { text = Encoding.UTF8.GetString(unicode.ToArray()); return true; }
        if (legacy.Count != 0) { text = Encoding.Latin1.GetString(legacy.ToArray()); return true; }
        return true;
    }

    private static uint ReadLong(ReadOnlySpan<byte> data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    private static void WriteFourCc(List<byte> bytes, uint value) => WriteLong(bytes, value);
    private static void WriteLong(List<byte> bytes, uint value)
    {
        bytes.Add((byte)(value >> 24)); bytes.Add((byte)(value >> 16)); bytes.Add((byte)(value >> 8)); bytes.Add((byte)value);
    }
    private static void WriteLongAt(List<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value;
    }
    private static void WriteChunk(List<byte> bytes, uint type, byte[] content)
    {
        WriteFourCc(bytes, type); WriteLong(bytes, (uint)content.Length); bytes.AddRange(content); if ((content.Length & 1) != 0) bytes.Add(0);
    }
}
