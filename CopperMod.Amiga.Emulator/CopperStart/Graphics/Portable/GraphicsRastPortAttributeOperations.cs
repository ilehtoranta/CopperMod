using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Portable V39 RastPort tag-list operations.  The tag parser follows the
/// Exec <c>TagItem</c> ABI and deliberately bounds both ordinary items and
/// TAG_MORE chains so malformed guest lists cannot hang a native caller.
/// Unknown/private tags are ignored, matching the forward-compatible graphics
/// library contract; CyberGraphX-specific attributes remain outside this
/// portable path.
/// </summary>
internal static class GraphicsRastPortAttributeOperations
{
    internal const uint TagDone = 0;
    internal const uint TagIgnore = 1;
    internal const uint TagMore = 2;
    internal const uint TagSkip = 3;

    internal const uint RptagFont = 0x8000_0000;
    internal const uint RptagSoftStyle = 0x8000_0001;
    internal const uint RptagAPen = 0x8000_0002;
    internal const uint RptagBPen = 0x8000_0003;
    internal const uint RptagDrMd = 0x8000_0004;
    internal const uint RptagOutlinePen = 0x8000_0005;
    internal const uint RptagWriteMask = 0x8000_0006;
    internal const uint RptagMaxPen = 0x8000_0007;
    internal const uint RptagDrawBounds = 0x8000_0008;

    private const int MaximumTagItems = 256;

    internal sealed class GetScratch
    {
        private readonly List<TagItem> _items = new(MaximumTagItems);
        private readonly List<(uint Address, byte Value)> _original = new();
        private readonly HashSet<uint> _addresses = new();

        internal List<TagItem> Items => _items;
        internal List<(uint Address, byte Value)> Original => _original;
        internal HashSet<uint> Addresses => _addresses;
    }

    internal static bool Set(
        IGraphicsMemory memory,
        uint rastPort,
        uint tags,
        IGraphicsFontBackend fonts)
        => Set(memory, rastPort, tags, fonts, 0);

    internal static bool Set(
        IGraphicsMemory memory,
        uint rastPort,
        uint tags,
        IGraphicsFontBackend fonts,
        uint defaultFont)
    {
        // Every public RastPort field envelope contains WORD/LONG members.
        // A host byte-addressable mapping must not turn an odd guest base into
        // a claimed compatibility call: a native 68000 would take an address
        // error before the tag list is consumed.
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (tags == 0)
            return true;

        if (!TryCollect(memory, tags, out var items) ||
            !TrySnapshotSetState(memory, rastPort, items, out var original))
        {
            return false;
        }

        foreach (var item in items)
        {
            if (!SetOne(memory, rastPort, item.Tag, item.Data, fonts, defaultFont))
            {
                RestoreSetState(memory, rastPort, original);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reports whether a validated tag list contains the classic
    /// <c>RPTAG_Font=0</c> form.  The register adapter uses this narrow probe
    /// to resolve <c>GfxBase-&gt;DefaultFont</c> only when the tag list actually
    /// requests it; pen-only lists therefore do not trigger compatibility
    /// font creation or a native font-list read.
    /// </summary>
    internal static bool TryRequiresDefaultFont(
        IGraphicsMemory memory,
        uint tags,
        out bool requiresDefaultFont)
    {
        requiresDefaultFont = false;
        if (tags == 0)
            return true;

        if (!TryCollect(memory, tags, out var items))
            return false;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Tag == RptagFont && item.Data == 0)
            {
                requiresDefaultFont = true;
                break;
            }
        }

        return true;
    }

    internal static bool Get(
        IGraphicsMemory memory,
        uint rastPort,
        uint tags,
        IGraphicsFontBackend fonts,
        IGraphicsLayerRasterBackend? layerRaster = null)
        => GetCore(memory, rastPort, tags, fonts, layerRaster, null);

    internal static bool Get(
        IGraphicsMemory memory,
        uint rastPort,
        uint tags,
        IGraphicsFontBackend fonts,
        IGraphicsLayerRasterBackend? layerRaster,
        GetScratch scratch)
        => GetCore(memory, rastPort, tags, fonts, layerRaster, scratch);

    private static bool GetCore(
        IGraphicsMemory memory,
        uint rastPort,
        uint tags,
        IGraphicsFontBackend fonts,
        IGraphicsLayerRasterBackend? layerRaster,
        GetScratch? scratch)
    {
        // Keep the same guest alignment boundary for queries.  This guard is
        // intentionally before tag-list collection and output journaling so
        // malformed native-owned RastPorts remain completely untouched.
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (tags == 0)
            return true;

        List<TagItem> items;
        List<(uint Address, byte Value)> original;
        if (scratch is null)
        {
            if (!TryCollect(memory, tags, out items) ||
                !TrySnapshotGetOutputs(memory, items, out original))
            {
                return false;
            }
        }
        else
        {
            items = scratch.Items;
            original = scratch.Original;
            if (!TryCollect(memory, tags, items) ||
                !TrySnapshotGetOutputs(
                    memory,
                    items,
                    original,
                    scratch.Addresses))
            {
                return false;
            }
        }

        foreach (var item in items)
        {
            if (!GetOne(memory, rastPort, item.Tag, item.Data, fonts, layerRaster))
            {
                RestoreGetOutputs(memory, original);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Restricts the reversible native ROM overlay to the tag subset whose
    /// behavior is a bounded RastPort byte/word update or query. Draw-bounds
    /// is admitted because the core validates non-layered bitmap state and
    /// can hand layered state to an explicit provider; unknown/private tags
    /// retain the normal native boundary.
    /// </summary>
    internal static bool IsNativeSafeOverlayTagList(
        IGraphicsMemory memory,
        uint tags)
    {
        if (tags == 0)
            return true;

        if (!TryCollect(memory, tags, out var items))
            return false;

        // Keep the overlay admission test stronger than the normal portable
        // dispatch.  A malformed DrawBounds destination is still a valid
        // TagItem chain, but it is not a safe native-overlay operation: the
        // resident vector must retain the address-error/wrap boundary instead
        // of first entering the compatibility adapter.  Scalar results keep
        // their existing MaxPen-null exception; GetRPAttrsA performs the
        // complete scalar preflight when it actually claims the call.
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Tag is not
                (RptagFont or
                 RptagSoftStyle or
                 RptagAPen or
                 RptagBPen or
                 RptagDrMd or
                 RptagOutlinePen or
                 RptagWriteMask or
                 RptagMaxPen or
                 RptagDrawBounds))
            {
                return false;
            }

            if (item.Tag == RptagDrawBounds &&
                !TryValidateDrawBoundsDestination(item.Data))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the exact RastPort byte ranges that a native-safe setter tag
    /// list can publish.  The host overlay uses these ranges to admit sparse
    /// or provider-backed RastPorts without requiring unrelated trailing
    /// fields to be mapped.  Unknown tags and malformed lists remain on the
    /// resident/provider boundary through the existing safe-tag predicate.
    /// </summary>
    internal static bool TryGetNativeSetWriteSpans(
        IGraphicsMemory memory,
        uint tags,
        out List<(int Offset, int Count)> spans)
    {
        var resultSpans = new List<(int Offset, int Count)>();
        spans = resultSpans;
        if (tags == 0)
            return true;

        if (!TryCollect(memory, tags, out var items))
            return false;

        void Add(int offset, int count)
            => resultSpans.Add((offset, count));

        foreach (var item in items)
        {
            switch (item.Tag)
            {
                case RptagFont:
                    Add(GraphicsLayouts.RastPortFont, sizeof(uint));
                    Add(GraphicsLayouts.RastPortAlgoStyle, sizeof(byte));
                    Add(GraphicsLayouts.RastPortTextHeight, sizeof(ushort));
                    Add(GraphicsLayouts.RastPortTextWidth, sizeof(ushort));
                    Add(GraphicsLayouts.RastPortTextBaseline, sizeof(ushort));
                    break;
                case RptagSoftStyle:
                    Add(GraphicsLayouts.RastPortAlgoStyle, sizeof(byte));
                    break;
                case RptagAPen:
                    Add(GraphicsLayouts.RastPortFgPen, sizeof(byte));
                    Add(GraphicsLayouts.RastPortLinePatternCount, sizeof(byte));
                    Add(GraphicsLayouts.RastPortMinterms, 8);
                    Add(GraphicsLayouts.RastPortFlags, sizeof(ushort));
                    break;
                case RptagBPen:
                    Add(GraphicsLayouts.RastPortBgPen, sizeof(byte));
                    Add(GraphicsLayouts.RastPortLinePatternCount, sizeof(byte));
                    Add(GraphicsLayouts.RastPortMinterms, 8);
                    Add(GraphicsLayouts.RastPortFlags, sizeof(ushort));
                    break;
                case RptagDrMd:
                    Add(GraphicsLayouts.RastPortDrawMode, sizeof(byte));
                    Add(GraphicsLayouts.RastPortLinePatternCount, sizeof(byte));
                    Add(GraphicsLayouts.RastPortMinterms, 8);
                    Add(GraphicsLayouts.RastPortFlags, sizeof(ushort));
                    break;
                case RptagOutlinePen:
                    Add(GraphicsLayouts.RastPortOutlinePen, sizeof(byte));
                    Add(GraphicsLayouts.RastPortFlags, sizeof(ushort));
                    break;
                case RptagWriteMask:
                    Add(GraphicsLayouts.RastPortMask, sizeof(byte));
                    break;
                case RptagMaxPen:
                    if (item.Data != 0)
                        Add(GraphicsLayouts.RastPortMask, sizeof(byte));
                    break;
                case RptagDrawBounds:
                    // SetRPAttrsA ignores DrawBounds; it is a GetRPAttrsA
                    // query tag and therefore has no RastPort write range.
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the guest destinations written by the native-safe GetRPAttrsA
    /// subset.  Alignment, NULL, and 32-bit wrapping remain validation
    /// failures here so the resident/provider owner retains malformed calls.
    /// </summary>
    internal static bool TryGetNativeGetOutputSpans(
        IGraphicsMemory memory,
        uint tags,
        out List<(uint Address, int Count)> spans)
    {
        spans = new List<(uint Address, int Count)>();
        if (tags == 0 || !TryCollect(memory, tags, out var items))
            return tags == 0;

        foreach (var item in items)
        {
            if (item.Data == 0)
            {
                if (item.Tag is
                    RptagFont or
                    RptagSoftStyle or
                    RptagAPen or
                    RptagBPen or
                    RptagDrMd or
                    RptagOutlinePen or
                    RptagWriteMask or
                    RptagDrawBounds)
                {
                    return false;
                }

                // RPTAG_MaxPen has a documented NULL-result no-op. Unknown
                // tags are ignored by the portable query implementation.
                continue;
            }

            var count = item.Tag == RptagDrawBounds
                ? GraphicsLayouts.RectangleSize
                : item.Tag is
                    (RptagFont or
                     RptagSoftStyle or
                     RptagAPen or
                     RptagBPen or
                     RptagDrMd or
                     RptagOutlinePen or
                     RptagWriteMask or
                     RptagMaxPen)
                    ? sizeof(uint)
                    : 0;
            if (count == 0)
                continue;

            if ((item.Data & 1u) != 0 ||
                (ulong)item.Data + (uint)count - 1UL > uint.MaxValue)
            {
                return false;
            }

            spans.Add((item.Data, count));
        }

        return true;
    }

    private static bool TryValidateDrawBoundsDestination(uint rectangle)
        => rectangle != 0 &&
           (rectangle & 1u) == 0 &&
           rectangle <= uint.MaxValue -
               (uint)(GraphicsLayouts.RectangleSize - 1);

    private static bool SetOne(
        IGraphicsMemory memory,
        uint rastPort,
        uint tag,
        uint data,
        IGraphicsFontBackend fonts,
        uint defaultFont)
    {
        switch (tag)
        {
            case RptagFont:
                // RPTAG_Font=0 is the tag-list spelling of SetFont(NULL).
                // A mapped host may pass an already-resolved compatibility
                // default, but the pure/core path must also let the font
                // backend resolve GfxBase->DefaultFont so direct and mapped
                // calls share the same guest contract.
                if (data == 0 && defaultFont != 0)
                    data = defaultFont;

                return GraphicsTextOperations.SetFont(
                        memory,
                        rastPort,
                        data,
                        fonts) == GraphicsRasterOperations.Success;
            case RptagSoftStyle:
                return GraphicsTextOperations.SetSoftStyle(
                    memory,
                    rastPort,
                    data,
                    uint.MaxValue,
                    fonts) != GraphicsRasterOperations.Failure;
            case RptagAPen:
                return GraphicsRasterOperations.SetAPen(memory, rastPort, data) != GraphicsRasterOperations.Failure;
            case RptagBPen:
                return GraphicsRasterOperations.SetBPen(memory, rastPort, data) != GraphicsRasterOperations.Failure;
            case RptagDrMd:
                return GraphicsRasterOperations.SetDrawMode(memory, rastPort, data) != GraphicsRasterOperations.Failure;
            case RptagOutlinePen:
                return GraphicsRasterOperations.SetOutlinePen(memory, rastPort, data) != GraphicsRasterOperations.Failure;
            case RptagWriteMask:
                return GraphicsRasterOperations.SetWriteMask(memory, rastPort, data) != GraphicsRasterOperations.Failure;
            case RptagMaxPen:
                return GraphicsRasterOperations.SetMaxPen(memory, rastPort, data) != GraphicsRasterOperations.Failure;
            default:
                return true;
        }
    }

    private static bool GetOne(
        IGraphicsMemory memory,
        uint rastPort,
        uint tag,
        uint data,
        IGraphicsFontBackend fonts,
        IGraphicsLayerRasterBackend? layerRaster)
    {
        // A zero output pointer is invalid for every recognised output tag
        // except RPTAG_MaxPen, whose query is intentionally a no-op when no
        // destination is supplied.  Unknown/private tags remain
        // forward-compatible: GetRPAttrsA must ignore them even when their
        // data word is zero rather than turning an otherwise valid tag list
        // into a portable failure and stealing the call from a provider.
        if (data == 0)
        {
            return tag switch
            {
                RptagMaxPen => true,
                RptagFont or
                RptagSoftStyle or
                RptagAPen or
                RptagBPen or
                RptagDrMd or
                RptagOutlinePen or
                RptagWriteMask or
                RptagDrawBounds => false,
                _ => true
            };
        }

        switch (tag)
        {
            case RptagFont:
                return GraphicsRasterOperations.TryReadRastPortLong(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortFont,
                        out var font) &&
                    memory.TryWriteLong(data, font);
            case RptagSoftStyle:
            {
                return GraphicsTextOperations.TryAskSoftStyle(
                        memory,
                        rastPort,
                        fonts,
                        out var style) &&
                    memory.TryWriteLong(data, style);
            }
            case RptagAPen:
            {
                var value = GraphicsRasterOperations.GetAPen(memory, rastPort);
                return value != GraphicsRasterOperations.Failure && memory.TryWriteLong(data, unchecked((uint)value));
            }
            case RptagBPen:
            {
                var value = GraphicsRasterOperations.GetBPen(memory, rastPort);
                return value != GraphicsRasterOperations.Failure && memory.TryWriteLong(data, unchecked((uint)value));
            }
            case RptagDrMd:
            {
                var value = GraphicsRasterOperations.GetDrawMode(memory, rastPort);
                return value != GraphicsRasterOperations.Failure && memory.TryWriteLong(data, unchecked((uint)value));
            }
            case RptagOutlinePen:
            {
                var value = GraphicsRasterOperations.GetOutlinePen(memory, rastPort);
                return value != GraphicsRasterOperations.Failure && memory.TryWriteLong(data, unchecked((uint)value));
            }
            case RptagWriteMask:
                return GraphicsRasterOperations.TryReadRastPortByte(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortMask,
                        out var writeMask) &&
                    memory.TryWriteLong(data, writeMask);
            case RptagMaxPen:
            {
                var value = GraphicsRasterOperations.GetMaxPen(memory, rastPort);
                return value != GraphicsRasterOperations.Failure &&
                    memory.TryWriteLong(data, unchecked((uint)value));
            }
            case RptagDrawBounds:
                return TryWriteDrawBounds(memory, rastPort, data, layerRaster);
            default:
                return true;
        }
    }

    private static bool TryWriteDrawBounds(
        IGraphicsMemory memory,
        uint rastPort,
        uint rectangle,
        IGraphicsLayerRasterBackend? layerRaster)
    {
        // Rectangle contains four public WORD fields.  A byte-readable odd
        // address is not a valid 68k envelope; reject it before either a
        // layer-provider callback or the non-layered bounds publication.
        if (!TryValidateDrawBoundsDestination(rectangle))
        {
            return false;
        }

        // ClipRect traversal belongs to layers.library.  A layered RastPort
        // must fall back to that owner instead of exposing the backing bitmap
        // bounds as if the window were unobscured.
        if (!GraphicsRasterOperations.TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer))
        {
            return false;
        }

        if (layer != 0)
            return layerRaster?.TryGetDrawBounds(rastPort, rectangle) == true;

        if (!GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
            return false;

        // Rectangle.Min*/Max* are signed guest WORDs.  A malformed bitmap
        // geometry whose right or bottom edge cannot be represented in that
        // result envelope must remain available to the native/provider path;
        // unchecked narrowing would otherwise publish a wrapped negative
        // bound and make a valid GetRPAttrsA call observe corrupted state.
        if (bitmap.Width == 0 ||
            bitmap.Rows == 0 ||
            bitmap.Width - 1u > short.MaxValue ||
            bitmap.Rows - 1u > short.MaxValue)
        {
            return false;
        }

        return memory.TryWriteWord(
                rectangle + (uint)GraphicsLayouts.RectangleMinX,
                0) &&
            memory.TryWriteWord(
                rectangle + (uint)GraphicsLayouts.RectangleMinY,
                0) &&
            memory.TryWriteWord(
                rectangle + (uint)GraphicsLayouts.RectangleMaxX,
                unchecked((ushort)(bitmap.Width - 1))) &&
            memory.TryWriteWord(
                rectangle + (uint)GraphicsLayouts.RectangleMaxY,
                unchecked((ushort)(bitmap.Rows - 1)));
    }

    private static bool Walk(
        IGraphicsMemory memory,
        uint initialTags,
        Func<uint, uint, bool> apply)
    {
        var cursor = initialTags;
        for (var item = 0; item < MaximumTagItems; item++)
        {
            if (cursor == 0 ||
                (cursor & 1u) != 0 ||
                // A TagItem occupies bytes [cursor..cursor+7].  The final
                // aligned guest start, $FFFF_FFF8, is therefore valid; only
                // starts above max-7 cross the 32-bit address boundary.
                cursor > uint.MaxValue - 7u ||
                !memory.TryReadLong(cursor, out var tag) ||
                !memory.TryReadLong(cursor + 4u, out var data))
            {
                return false;
            }

            var nextCursor = cursor + 8u;
            switch (tag)
            {
                case TagDone:
                    return true;
                case TagIgnore:
                    if (nextCursor < cursor)
                        return false;

                    cursor = nextCursor;
                    continue;
                case TagMore:
                    if (data == 0)
                        return true;

                    if ((data & 1u) != 0)
                        return false;

                    cursor = data;
                    continue;
                case TagSkip:
                    if (!TryAdvanceTagItems(nextCursor, data, cursor, out var skippedCursor))
                        return false;

                    cursor = skippedCursor;
                    continue;
                default:
                    if (nextCursor < cursor)
                        return false;

                    if (!apply(tag, data))
                        return false;

                    cursor = nextCursor;
                    continue;
            }
        }

        return false;
    }

    private static bool TryCollect(
        IGraphicsMemory memory,
        uint initialTags,
        out List<TagItem> items)
    {
        items = new List<TagItem>();
        return TryCollect(memory, initialTags, items);
    }

    private static bool TryCollect(
        IGraphicsMemory memory,
        uint initialTags,
        List<TagItem> items)
    {
        items.Clear();
        var cursor = initialTags;
        for (var item = 0; item < MaximumTagItems; item++)
        {
            if (cursor == 0 ||
                (cursor & 1u) != 0 ||
                cursor > uint.MaxValue - 7u ||
                !memory.TryReadLong(cursor, out var tag) ||
                !memory.TryReadLong(cursor + 4u, out var data))
            {
                return false;
            }

            var nextCursor = cursor + 8u;
            switch (tag)
            {
                case TagDone:
                    return true;
                case TagIgnore:
                    if (nextCursor < cursor)
                        return false;

                    cursor = nextCursor;
                    continue;
                case TagMore:
                    if (data == 0)
                        return true;

                    if ((data & 1u) != 0)
                        return false;

                    cursor = data;
                    continue;
                case TagSkip:
                    if (!TryAdvanceTagItems(nextCursor, data, cursor, out var skippedCursor))
                        return false;

                    cursor = skippedCursor;
                    continue;
                default:
                    if (nextCursor < cursor)
                        return false;

                    items.Add(new TagItem(tag, data));
                    cursor = nextCursor;
                    continue;
            }
        }

        return false;
    }

    private static bool TryAdvanceTagItems(
        uint nextCursor,
        uint itemCount,
        uint currentCursor,
        out uint cursor)
    {
        cursor = 0;
        if (nextCursor < currentCursor)
            return false;

        var skipBytes = (ulong)itemCount * 8ul;
        if (skipBytes > uint.MaxValue ||
            nextCursor > uint.MaxValue - (uint)skipBytes)
        {
            return false;
        }

        cursor = nextCursor + (uint)skipBytes;
        return true;
    }

    private static bool TrySnapshotSetState(
        IGraphicsMemory memory,
        uint rastPort,
        IReadOnlyList<TagItem> items,
        out List<(int Offset, byte Value)> original)
    {
        original = new List<(int Offset, byte Value)>();
        var offsets = new HashSet<int>();

        void Add(int offset)
            => offsets.Add(offset);

        void AddRange(int first, int count)
        {
            for (var offset = 0; offset < count; offset++)
                offsets.Add(first + offset);
        }

        foreach (var item in items)
        {
            switch (item.Tag)
            {
                case RptagFont:
                    // SetFont consumes and publishes the selected font, the
                    // three cached metric words, and the soft-style reset in
                    // AlgoStyle.  TextFlags and TxSpacing remain caller
                    // state, so do not make a sparse/provider-owned trailing
                    // field prevent an otherwise valid font update.
                    AddRange(GraphicsLayouts.RastPortFont, sizeof(uint));
                    Add(GraphicsLayouts.RastPortAlgoStyle);
                    AddRange(GraphicsLayouts.RastPortTextHeight, sizeof(ushort));
                    AddRange(GraphicsLayouts.RastPortTextWidth, sizeof(ushort));
                    AddRange(GraphicsLayouts.RastPortTextBaseline, sizeof(ushort));
                    break;
                case RptagSoftStyle:
                    Add(GraphicsLayouts.RastPortAlgoStyle);
                    break;
                case RptagAPen:
                case RptagBPen:
                case RptagDrMd:
                    Add(item.Tag == RptagAPen
                        ? GraphicsLayouts.RastPortFgPen
                        : item.Tag == RptagBPen
                            ? GraphicsLayouts.RastPortBgPen
                            : GraphicsLayouts.RastPortDrawMode);
                    Add(GraphicsLayouts.RastPortLinePatternCount);
                    // SetAPen/SetBPen/SetDrMd also regenerate the public
                    // RastPort minterms[8].  Keep those derived bytes in the
                    // same transaction as the source pen/mode, pattern phase,
                    // and private pen-enable flag so a later tag failure
                    // cannot expose a mixed old/new draw state.
                    AddRange(GraphicsLayouts.RastPortMinterms, 8);
                    Add(GraphicsLayouts.RastPortFlags);
                    Add(GraphicsLayouts.RastPortFlags + 1);
                    break;
                case RptagOutlinePen:
                    Add(GraphicsLayouts.RastPortOutlinePen);
                    Add(GraphicsLayouts.RastPortFlags);
                    Add(GraphicsLayouts.RastPortFlags + 1);
                    break;
                case RptagWriteMask:
                case RptagMaxPen:
                    Add(GraphicsLayouts.RastPortMask);
                    break;
            }
        }

        foreach (var offset in offsets)
        {
            if (!GraphicsRasterOperations.TryReadRastPortByte(
                    memory,
                    rastPort,
                    offset,
                    out var value))
                return false;

            original.Add((offset, value));
        }

        return true;
    }

    private static void RestoreSetState(
        IGraphicsMemory memory,
        uint rastPort,
        IReadOnlyList<(int Offset, byte Value)> original)
    {
        foreach (var (offset, value) in original)
            _ = GraphicsRasterOperations.TryWriteRastPortByte(
                memory,
                rastPort,
                offset,
                value);
    }

    private static bool TrySnapshotGetOutputs(
        IGraphicsMemory memory,
        IReadOnlyList<TagItem> items,
        out List<(uint Address, byte Value)> original)
    {
        original = new List<(uint Address, byte Value)>();
        var addresses = new HashSet<uint>();
        return TrySnapshotGetOutputs(memory, items, original, addresses);
    }

    private static bool TrySnapshotGetOutputs(
        IGraphicsMemory memory,
        IReadOnlyList<TagItem> items,
        List<(uint Address, byte Value)> original,
        HashSet<uint> addresses)
    {
        original.Clear();
        addresses.Clear();

        // IReadOnlyList enumeration boxes List<T>.Enumerator on the hot Get
        // path. Indexing keeps the caller-supplied scratch fully allocation
        // free after warm-up.
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            if (item.Data == 0)
            {
                // Unknown/private output tags are ignored by GetRPAttrsA,
                // including a zero data word.  Only recognised tags need a
                // destination check; RPTAG_MaxPen deliberately keeps its
                // existing null-output no-op form.
                if (item.Tag is
                    RptagFont or
                    RptagSoftStyle or
                    RptagAPen or
                    RptagBPen or
                    RptagDrMd or
                    RptagOutlinePen or
                    RptagWriteMask or
                    RptagDrawBounds)
                    return false;

                continue;
            }

            switch (item.Tag)
            {
                case RptagFont:
                case RptagSoftStyle:
                case RptagAPen:
                case RptagBPen:
                case RptagDrMd:
                case RptagOutlinePen:
                case RptagWriteMask:
                case RptagMaxPen:
                    // Every scalar result is a guest ULONG destination.  A
                    // byte-addressable host could write an odd address, but
                    // the 68000 TagItem ABI cannot issue an unaligned long
                    // store.  Decline before journaling so a native/provider
                    // owner can handle the malformed request without any
                    // partial output publication.
                    if ((item.Data & 1u) != 0)
                        return false;

                    if (!AddAddressRange(addresses, item.Data, 4))
                        return false;
                    break;
                case RptagDrawBounds:
                    if ((item.Data & 1u) != 0)
                        return false;

                    if (!AddAddressRange(
                            addresses,
                            item.Data,
                            GraphicsLayouts.RectangleSize))
                        return false;
                    break;
            }
        }

        foreach (var address in addresses)
        {
            if (!memory.TryReadByte(address, out var value))
                return false;

            original.Add((address, value));
        }

        return true;
    }

    private static bool AddAddressRange(
        HashSet<uint> addresses,
        uint address,
        int count)
    {
        if (count <= 0 ||
            (ulong)address + (uint)count - 1u > uint.MaxValue)
        {
            return false;
        }

        for (var offset = 0; offset < count; offset++)
            addresses.Add(address + (uint)offset);
        return true;
    }

    private static void RestoreGetOutputs(
        IGraphicsMemory memory,
        IReadOnlyList<(uint Address, byte Value)> original)
    {
        foreach (var (address, value) in original)
            _ = memory.TryWriteByte(address, value);
    }

    internal readonly record struct TagItem(uint Tag, uint Data);
}
