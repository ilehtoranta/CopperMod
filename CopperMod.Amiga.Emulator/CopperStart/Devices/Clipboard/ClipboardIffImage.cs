using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Devices.Clipboard;

/// <summary>Host-neutral 32-bit BGRA clipboard image.</summary>
public sealed class ClipboardImage
{
    /// <summary>Creates an image from row-major BGRA pixels.</summary>
    public ClipboardImage(int width, int height, uint[] bgra32)
    {
        if (width <= 0 || height <= 0 || bgra32 is null || bgra32.Length != checked(width * height))
            throw new ArgumentException("Clipboard image dimensions and pixel buffer do not agree.");
        Width = width;
        Height = height;
        Bgra32 = bgra32;
    }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }
    /// <summary>Image height in pixels.</summary>
    public int Height { get; }
    /// <summary>Row-major pixels in BGRA byte order packed into 32-bit words.</summary>
    public uint[] Bgra32 { get; }
}

/// <summary>
/// Bounded conversion for the common indexed ILBM clipboard representation.
/// Unsupported display modes deliberately remain opaque IFF in clipboard.device.
/// </summary>
internal static class ClipboardIffImage
{
    private const uint Form = 0x464F524D, Ilbm = 0x494C424D, Bmhd = 0x424D4844, Cmap = 0x434D4150, Body = 0x424F4459, Camg = 0x43414D47;
    private const int MaxDimension = 2048, MaxPixels = 2_000_000;

    public static bool TryDecode(ReadOnlySpan<byte> data, out ClipboardImage? image)
    {
        image = null;
        if (data.Length < 12 || ReadLong(data, 0) != Form || ReadLong(data, 8) != Ilbm) return false;
        var formEnd = (long)ReadLong(data, 4) + 8;
        if (formEnd > data.Length || formEnd < 12) return false;
        ReadOnlySpan<byte> bitmapHeader = default, colorMap = default, body = default;
        uint mode = 0;
        for (var offset = 12; offset + 8 <= formEnd;)
        {
            var type = ReadLong(data, offset); var length = ReadLong(data, offset + 4);
            var content = offset + 8; var next = (long)content + length;
            if (next > formEnd || next > int.MaxValue) return false;
            var chunk = data.Slice(content, (int)length);
            if (type == Bmhd) bitmapHeader = chunk;
            else if (type == Cmap) colorMap = chunk;
            else if (type == Body) body = chunk;
            else if (type == Camg && chunk.Length >= 4) mode = ReadLong(chunk, 0);
            offset = checked((int)(next + (length & 1)));
        }
        if (bitmapHeader.Length < 20 || colorMap.IsEmpty || body.IsEmpty) return false;
        var width = ReadWord(bitmapHeader, 0); var height = ReadWord(bitmapHeader, 2); var depth = bitmapHeader[8];
        var masking = bitmapHeader[9]; var compression = bitmapHeader[10];
        // HAM/EHB has palette semantics beyond a plain indexed host bitmap.
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels ||
            depth is 0 or > 8 || masking > 1 || compression > 1 || (mode & 0x0000_0800) != 0 || (mode & 0x0000_0080) != 0) return false;
        var colors = new uint[1 << depth];
        for (var index = 0; index < colors.Length; index++)
        {
            var offset = index * 3;
            if (offset + 3 > colorMap.Length) break;
            colors[index] = 0xFF00_0000u | ((uint)colorMap[offset] << 16) | ((uint)colorMap[offset + 1] << 8) | colorMap[offset + 2];
        }
        var rowBytes = ((width + 15) >> 4) << 1;
        var planes = depth + masking;
        var bytesNeeded = checked(rowBytes * planes * height);
        byte[] decoded;
        if (compression == 0)
        {
            if (body.Length < bytesNeeded) return false;
            decoded = body[..bytesNeeded].ToArray();
        }
        else if (!TryDecodeByteRun1(body, bytesNeeded, out decoded)) return false;

        var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pen = 0;
            var bit = 7 - (x & 7);
            for (var plane = 0; plane < depth; plane++)
                if ((decoded[(y * planes + plane) * rowBytes + (x >> 3)] & (1 << bit)) != 0) pen |= 1 << plane;
            pixels[y * width + x] = colors[pen];
        }
        image = new ClipboardImage(width, height, pixels);
        return true;
    }

    public static byte[] Encode(ClipboardImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width > MaxDimension || image.Height > MaxDimension || (long)image.Width * image.Height > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(image));
        var palette = BuildPalette(image.Bgra32, out var indices);
        var depth = DepthForPalette(palette.Count);
        var rowBytes = ((image.Width + 15) >> 4) << 1;
        var body = new byte[checked(rowBytes * depth * image.Height)];
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var pen = indices[y * image.Width + x]; var bit = 7 - (x & 7);
            for (var plane = 0; plane < depth; plane++)
                if ((pen & (1 << plane)) != 0) body[(y * depth + plane) * rowBytes + (x >> 3)] |= (byte)(1 << bit);
        }
        var result = new List<byte>(12 + 28 + palette.Count * 3 + body.Length + 2);
        WriteLong(result, Form); WriteLong(result, 0); WriteLong(result, Ilbm);
        var header = new byte[20]; WriteWord(header, 0, (ushort)image.Width); WriteWord(header, 2, (ushort)image.Height); header[8] = (byte)depth; header[10] = 0; header[14] = 10; WriteWord(header, 16, (ushort)image.Width); WriteWord(header, 18, (ushort)image.Height);
        WriteChunk(result, Bmhd, header);
        var map = new byte[palette.Count * 3];
        for (var index = 0; index < palette.Count; index++) { map[index * 3] = (byte)(palette[index] >> 16); map[index * 3 + 1] = (byte)(palette[index] >> 8); map[index * 3 + 2] = (byte)palette[index]; }
        WriteChunk(result, Cmap, map); WriteChunk(result, Body, body); WriteLongAt(result, 4, checked((uint)(result.Count - 8)));
        return result.ToArray();
    }

    private static List<uint> BuildPalette(uint[] pixels, out byte[] indices)
    {
        var map = new Dictionary<uint, byte>(); var palette = new List<uint>(); var quantized = false; indices = new byte[pixels.Length];
        for (var index = 0; index < pixels.Length; index++)
        {
            var color = pixels[index] & 0x00FF_FFFFu;
            if (!map.TryGetValue(color, out var pen))
            {
                if (palette.Count < 256) { pen = (byte)palette.Count; map.Add(color, pen); palette.Add(color); }
                else { pen = Quantized(color); quantized = true; }
            }
            indices[index] = pen;
        }
        if (!quantized) return palette;
        palette.Clear();
        for (var index = 0; index < pixels.Length; index++) indices[index] = Quantized(pixels[index] & 0x00FF_FFFFu);
        for (var index = 0; index < 256; index++)
        {
            var red = (uint)((index & 0xE0) | ((index & 0xE0) >> 3) | ((index & 0xE0) >> 6));
            var green = (uint)(((index & 0x1C) << 3) | (index & 0x1C) | ((index & 0x1C) >> 3));
            var blue = (uint)((index & 3) * 85);
            palette.Add((red << 16) | (green << 8) | blue);
        }
        return palette;
    }

    private static byte Quantized(uint color)
        => (byte)(((color >> 21) & 0xE0) | ((color >> 13) & 0x1C) | ((color >> 6) & 0x03));

    private static int DepthForPalette(int count) { var depth = 1; while ((1 << depth) < Math.Max(2, count)) depth++; return depth; }
    private static bool TryDecodeByteRun1(ReadOnlySpan<byte> source, int outputLength, out byte[] output)
    {
        output = new byte[outputLength]; var sourceOffset = 0; var targetOffset = 0;
        while (sourceOffset < source.Length && targetOffset < outputLength)
        {
            var control = unchecked((sbyte)source[sourceOffset++]);
            if (control >= 0) { var count = control + 1; if (sourceOffset + count > source.Length || targetOffset + count > outputLength) return false; source.Slice(sourceOffset, count).CopyTo(output.AsSpan(targetOffset)); sourceOffset += count; targetOffset += count; }
            else if (control != -128) { var count = 1 - control; if (sourceOffset >= source.Length || targetOffset + count > outputLength) return false; output.AsSpan(targetOffset, count).Fill(source[sourceOffset++]); targetOffset += count; }
        }
        return targetOffset == outputLength;
    }
    private static ushort ReadWord(ReadOnlySpan<byte> bytes, int offset) => (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    private static uint ReadLong(ReadOnlySpan<byte> bytes, int offset) => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    private static void WriteWord(byte[] bytes, int offset, ushort value) { bytes[offset] = (byte)(value >> 8); bytes[offset + 1] = (byte)value; }
    private static void WriteLong(List<byte> bytes, uint value) { bytes.Add((byte)(value >> 24)); bytes.Add((byte)(value >> 16)); bytes.Add((byte)(value >> 8)); bytes.Add((byte)value); }
    private static void WriteLongAt(List<byte> bytes, int offset, uint value) { bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value; }
    private static void WriteChunk(List<byte> bytes, uint type, byte[] payload) { WriteLong(bytes, type); WriteLong(bytes, (uint)payload.Length); bytes.AddRange(payload); if ((payload.Length & 1) != 0) bytes.Add(0); }
}
