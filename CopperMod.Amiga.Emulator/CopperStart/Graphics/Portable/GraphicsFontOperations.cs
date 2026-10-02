using System;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Portable guest-memory side of the graphics.library font lifecycle.
/// System-list ownership and allocation are deliberately delegated to the
/// explicit lifecycle backend so this code remains usable by a native 68k
/// implementation as well as the CopperStart host bridge.
/// </summary>
internal static class GraphicsFontOperations
{
    internal static uint OpenFont(
        IGraphicsFontLifecycleBackend lifecycle,
        uint textAttrAddress)
        => lifecycle.TryOpen(textAttrAddress, out var fontAddress) ? fontAddress : 0;

    internal static int CloseFont(
        IGraphicsFontLifecycleBackend lifecycle,
        uint fontAddress)
    {
        // Kickstart treats a NULL TextFont pointer as a harmless no-op. Keep
        // the lifecycle backend's registry contract strict (it still rejects
        // NULL when called directly), but close the public ABI at this seam so
        // a null A1 does not require a readable guest font envelope.
        if (fontAddress == 0)
            return GraphicsRasterOperations.Success;

        return lifecycle.TryClose(fontAddress)
            ? GraphicsRasterOperations.Success
            : GraphicsRasterOperations.Failure;
    }

    internal static int AddFont(
        IGraphicsFontLifecycleBackend lifecycle,
        uint fontAddress)
        => lifecycle.TryAdd(fontAddress) ? GraphicsRasterOperations.Success : GraphicsRasterOperations.Failure;

    internal static int RemFont(
        IGraphicsFontLifecycleBackend lifecycle,
        uint fontAddress)
        => lifecycle.TryRemove(fontAddress) ? GraphicsRasterOperations.Success : GraphicsRasterOperations.Failure;

    internal static bool ExtendFont(
        IGraphicsFontLifecycleBackend lifecycle,
        uint fontAddress,
        uint fontTags)
        => lifecycle.TryExtend(fontAddress, fontTags);

    internal static int StripFont(
        IGraphicsFontLifecycleBackend lifecycle,
        uint fontAddress)
    {
        // StripFont(NULL) is the same idempotent teardown form as
        // CloseFont(NULL).  Keep the lifecycle backend strict for direct
        // ownership operations, but make the shared portable ABI safe for
        // CopperSharp68k callers that bypass the register adapter.
        if (fontAddress == 0)
            return GraphicsRasterOperations.Success;

        return lifecycle.TryStrip(fontAddress)
            ? GraphicsRasterOperations.Success
            : GraphicsRasterOperations.Failure;
    }

    internal static int AskFont(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAttrAddress)
    {
        if (rastPort == 0 || (rastPort & 1u) != 0 ||
            textAttrAddress == 0 || (textAttrAddress & 1u) != 0 ||
            !TryReadLongAt(memory, rastPort, GraphicsLayouts.RastPortFont, out var fontAddress) ||
            fontAddress == 0 ||
            !TryReadAskFontAttributes(memory, fontAddress, out var attributes) ||
            !TryProbeRange(memory, textAttrAddress, GraphicsLayouts.TextAttrSize))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!TrySnapshotRange(memory, textAttrAddress, GraphicsLayouts.TextAttrSize, out var original))
            return GraphicsRasterOperations.Failure;

        if (TryWriteLongAt(memory, textAttrAddress, GraphicsLayouts.TextAttrName, attributes.Name) &&
            TryWriteWordAt(memory, textAttrAddress, GraphicsLayouts.TextAttrYSize, attributes.YSize) &&
            TryWriteByteAt(memory, textAttrAddress, GraphicsLayouts.TextAttrStyle, attributes.Style) &&
            TryWriteByteAt(memory, textAttrAddress, GraphicsLayouts.TextAttrFlags, attributes.Flags))
        {
            return GraphicsRasterOperations.Success;
        }

        RestoreRange(memory, textAttrAddress, original);
        return GraphicsRasterOperations.Failure;
    }

    internal static bool TryReadTextAttributes(
        IGraphicsMemory memory,
        uint textAttrAddress,
        out GraphicsTextAttributes attributes)
    {
        attributes = default;
        if (textAttrAddress == 0 || (textAttrAddress & 1u) != 0 ||
            !TryReadLongAt(memory, textAttrAddress, GraphicsLayouts.TextAttrName, out var name) ||
            !TryReadWordAt(memory, textAttrAddress, GraphicsLayouts.TextAttrYSize, out var ySize) ||
            !TryReadByteAt(memory, textAttrAddress, GraphicsLayouts.TextAttrStyle, out var style) ||
            !TryReadByteAt(memory, textAttrAddress, GraphicsLayouts.TextAttrFlags, out var flags))
        {
            return false;
        }

        // OpenFont's native matcher accepts a readable zero ta_YSize and
        // lets WeighTAMatch rank it against resident fonts.  The selected
        // TextFont still requires a valid nonzero strike envelope; that
        // separate candidate check remains in GraphicsMemoryFontBackend.
        attributes = new GraphicsTextAttributes(name, ySize, style, flags);
        return true;
    }

    internal static bool TryReadTextFontAttributes(
        IGraphicsMemory memory,
        uint fontAddress,
        out GraphicsTextAttributes attributes)
    {
        attributes = default;
        if (fontAddress == 0 || (fontAddress & 1u) != 0 ||
            !TryReadLongAt(memory, fontAddress, GraphicsLayouts.TextFontName, out var name) ||
            !TryReadWordAt(memory, fontAddress, GraphicsLayouts.TextFontYSize, out var ySize) ||
            !TryReadByteAt(memory, fontAddress, GraphicsLayouts.TextFontStyle, out var style) ||
            !TryReadByteAt(memory, fontAddress, GraphicsLayouts.TextFontFlags, out var flags) ||
            ySize == 0)
        {
            return false;
        }

        attributes = new GraphicsTextAttributes(name, ySize, style, flags);
        return true;
    }

    /// <summary>
    /// Reads only the fields consumed by the resident AskFont vector.  A
    /// readable TextFont with a zero YSize is still queryable: AskFont copies
    /// the value and does not apply the nonzero strike admission used by
    /// OpenFont/AddFont/ExtendFont.
    /// </summary>
    internal static bool TryReadAskFontAttributes(
        IGraphicsMemory memory,
        uint fontAddress,
        out GraphicsTextAttributes attributes)
    {
        attributes = default;
        if (fontAddress == 0 || (fontAddress & 1u) != 0 ||
            !TryReadLongAt(memory, fontAddress, GraphicsLayouts.TextFontName, out var name) ||
            !TryReadWordAt(memory, fontAddress, GraphicsLayouts.TextFontYSize, out var ySize) ||
            !TryReadByteAt(memory, fontAddress, GraphicsLayouts.TextFontStyle, out var style) ||
            !TryReadByteAt(memory, fontAddress, GraphicsLayouts.TextFontFlags, out var flags))
        {
            return false;
        }

        attributes = new GraphicsTextAttributes(name, ySize, style, flags);
        return true;
    }

    private static bool TryReadByteAt(IGraphicsMemory memory, uint baseAddress, int offset, out byte value)
    {
        value = 0;
        return TryAddress(baseAddress, (uint)offset, out var address) &&
            memory.TryReadByte(address, out value);
    }

    private static bool TryReadWordAt(IGraphicsMemory memory, uint baseAddress, int offset, out ushort value)
    {
        value = 0;
        return TryAddress(baseAddress, (uint)offset, out var address) &&
            memory.TryReadWord(address, out value);
    }

    private static bool TryReadLongAt(IGraphicsMemory memory, uint baseAddress, int offset, out uint value)
    {
        value = 0;
        return TryAddress(baseAddress, (uint)offset, out var address) &&
            memory.TryReadLong(address, out value);
    }

    private static bool TryWriteByteAt(IGraphicsMemory memory, uint baseAddress, int offset, byte value)
        => TryAddress(baseAddress, (uint)offset, out var address) &&
            memory.TryWriteByte(address, value);

    private static bool TryWriteWordAt(IGraphicsMemory memory, uint baseAddress, int offset, ushort value)
        => TryAddress(baseAddress, (uint)offset, out var address) &&
            memory.TryWriteWord(address, value);

    private static bool TryWriteLongAt(IGraphicsMemory memory, uint baseAddress, int offset, uint value)
        => TryAddress(baseAddress, (uint)offset, out var address) &&
            memory.TryWriteLong(address, value);

    private static bool TryProbeRange(IGraphicsMemory memory, uint address, int byteCount)
    {
        if (address == 0 || byteCount <= 0 ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }

        return true;
    }

    private static bool TrySnapshotRange(
        IGraphicsMemory memory,
        uint address,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (address == 0 || byteCount <= 0 ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        original = new byte[byteCount];
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!TryAddress(address, (uint)offset, out var current) ||
                !memory.TryReadByte(current, out original[offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static void RestoreRange(
        IGraphicsMemory memory,
        uint address,
        byte[] original)
    {
        for (var offset = original.Length - 1; offset >= 0; offset--)
        {
            if (TryAddress(address, (uint)offset, out var current))
                _ = memory.TryWriteByte(current, original[offset]);
        }
    }

    private static bool TryAddress(uint baseAddress, uint offset, out uint address)
    {
        if (offset > uint.MaxValue - baseAddress)
        {
            address = 0;
            return false;
        }

        address = baseAddress + offset;
        return true;
    }
}
