using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Mirrors the public graphics.library TextFonts list into a guest GfxBase.
/// This is deliberately a memory-only implementation: it does not own font
/// allocations and it never decides which fonts are eligible for matching.
/// </summary>
internal sealed class GraphicsGuestFontListBackend :
    IGraphicsFontListBackend,
    IGraphicsNativeFontListPublicationBackend
{
    private const int MaximumFontNodes = 4096;
    private const byte FontRemovedFlag = 0x80;
    private readonly IGraphicsMemory _memory;
    private uint _gfxBase;

    internal GraphicsGuestFontListBackend(IGraphicsMemory memory, uint gfxBase)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _gfxBase = gfxBase;
    }

    public uint BaseAddress => _gfxBase;

    /// <summary>
    /// Rebinds list publication to a discovered native graphics.library base.
    /// The operation deliberately does not initialize or clear the target
    /// list: a native resident may already have published font nodes there.
    /// Every subsequent AddFont/RemFont operation validates that target list
    /// in place and fails closed if its envelope is not readable.
    /// </summary>
    public bool TryRebind(uint gfxBase)
    {
        if (gfxBase == 0 || (gfxBase & 1u) != 0 ||
            !TryAddress(gfxBase, (uint)GraphicsLayouts.GfxBaseTextFonts, out var listAddress) ||
            !TryAddress(gfxBase, (uint)GraphicsLayouts.GfxBaseDefaultFont, out var defaultAddress) ||
            !_memory.TryReadLong(listAddress, out _) ||
            !_memory.TryReadLong(defaultAddress, out _))
        {
            return false;
        }

        _gfxBase = gfxBase;
        return true;
    }

    /// <summary>
    /// Resolves the exact public fields that <c>AddFont</c> will rewrite.
    /// The plan is side-effect free so native-overlay admission can reject a
    /// read-only resident/provider list before publishing the new node links
    /// or clearing its removed flag.
    /// </summary>
    public bool TryGetAddFontPublicationSpans(
        uint fontAddress,
        out IReadOnlyList<(uint Address, int ByteCount)> spans)
    {
        spans = Array.Empty<(uint Address, int ByteCount)>();
        if ((_gfxBase & 1u) != 0 || !IsWordAddress(fontAddress) ||
            fontAddress == 0 ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsHead, out var head) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTailPred, out var tailPred) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTail, out var tail) ||
            tail != 0 || tailPred == 0 ||
            !IsWordAddress(head) || !IsWordAddress(tailPred))
        {
            return false;
        }

        // Admission must validate the complete resident chain, not only its
        // first predecessor and last successor.  A provider may leave an
        // interior cycle that would otherwise let AddFont publish a new tail
        // while the public list remains unreachable/corrupt.
        if (!TryEnumerate(out _))
            return false;

        var list = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts;
        var tailSentinel = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail;
        var empty = head == tailSentinel && tailPred == list;
        if ((!empty && (head == 0 || head == tailSentinel || tailPred == list)) ||
            (empty && head != tailSentinel) ||
            (!empty &&
                (!TryReadGuestLong(head, 4, out var firstPred) || firstPred != list ||
                 !TryReadGuestLong(tailPred, 0, out var lastSucc) ||
                 lastSucc != tailSentinel)) ||
            !TryReadGuestByte(fontAddress, (uint)GraphicsLayouts.TextFontFlags, out _) ||
            !TryReadGuestLong(fontAddress, 0, out var oldSuccessor) ||
            !TryReadGuestLong(fontAddress, 4, out var oldPredecessor) ||
            !IsOptionalWordAddress(oldSuccessor) ||
            !IsOptionalWordAddress(oldPredecessor) ||
            oldSuccessor != 0 || oldPredecessor != 0)
        {
            return false;
        }

        if (!TryAddress(fontAddress, 0, out var fontLinksAddress) ||
            !TryAddress(tailPred, 0, out var tailPredLinkAddress) ||
            !TryAddress(
                _gfxBase,
                (uint)GraphicsLayouts.GfxBaseTextFontsTailPred,
                out var listTailPredAddress) ||
            !TryAddress(
                fontAddress,
                (uint)GraphicsLayouts.TextFontFlags,
                out var fontFlagsAddress))
        {
            return false;
        }

        var result = new List<(uint Address, int ByteCount)>(5)
        {
            (fontLinksAddress, sizeof(uint) * 2),
            (tailPredLinkAddress, sizeof(uint)),
            (listTailPredAddress, sizeof(uint)),
            (fontFlagsAddress, sizeof(byte))
        };

        var listHeadAddress = 0u;
        if (empty &&
            !TryAddress(
                _gfxBase,
                (uint)GraphicsLayouts.GfxBaseTextFontsHead,
                out listHeadAddress))
        {
            return false;
        }

        if (empty)
            result.Add((listHeadAddress, sizeof(uint)));

        spans = result;
        return true;
    }

    /// <summary>
    /// Reads the current Exec-style TextFonts list without mutating it.
    /// Every predecessor/successor link is checked, including the list's
    /// head/tail sentinels, and bounded traversal rejects cycles or a list
    /// that never reaches its tail.  This lets OpenFont inspect resident
    /// native fonts while keeping malformed guest lists available to the
    /// captured native/provider vector.
    /// </summary>
    public bool TryEnumerate(out IReadOnlyList<uint> fonts)
    {
        fonts = Array.Empty<uint>();
        if ((_gfxBase & 1u) != 0 ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsHead, out var head) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTailPred, out var tailPred) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTail, out var tail) ||
            tail != 0 ||
            !IsOptionalWordAddress(head) ||
            !IsOptionalWordAddress(tailPred))
        {
            return false;
        }

        var list = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts;
        var tailSentinel = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail;
        if (head == tailSentinel)
            return tailPred == list;

        if (head == 0 || tailPred == list)
            return false;

        var result = new List<uint>();
        var visited = new HashSet<uint>();
        var predecessor = list;
        var current = head;
        for (var index = 0; index < MaximumFontNodes; index++)
        {
            if (current == 0 || current == list || current == tailSentinel ||
                !IsWordAddress(current) ||
                !visited.Add(current) ||
                !TryReadGuestLong(current, 4, out var actualPredecessor) ||
                !IsWordAddress(actualPredecessor) ||
                actualPredecessor != predecessor ||
                !TryReadGuestLong(current, 0, out var successor))
            {
                return false;
            }

            result.Add(current);
            if (successor == tailSentinel)
            {
                if (current != tailPred)
                    return false;

                fonts = result;
                return true;
            }

            if (successor == 0 || !IsWordAddress(successor))
                return false;

            predecessor = current;
            current = successor;
        }

        return false;
    }

    /// <summary>Reads the selected GfxBase's guest DefaultFont pointer.</summary>
    public bool TryGetDefaultFont(out uint fontAddress)
    {
        if (!TryReadLong(GraphicsLayouts.GfxBaseDefaultFont, out fontAddress) ||
            !IsOptionalWordAddress(fontAddress))
        {
            fontAddress = 0;
            return false;
        }

        return true;
    }

    internal bool Initialize(uint defaultFont)
    {
        if ((_gfxBase & 1u) != 0 || !IsOptionalWordAddress(defaultFont))
            return false;

        if (!TrySnapshotRange(
                _gfxBase,
                GraphicsLayouts.GfxBaseTextFonts,
                GraphicsLayouts.GfxBaseDefaultFont + 4 - GraphicsLayouts.GfxBaseTextFonts,
                out var original))
        {
            return false;
        }

        var list = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts;
        var tail = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail;
        if (TryWriteLong(GraphicsLayouts.GfxBaseTextFontsHead, tail) &&
            TryWriteLong(GraphicsLayouts.GfxBaseTextFontsTail, 0) &&
            TryWriteLong(GraphicsLayouts.GfxBaseTextFontsTailPred, list) &&
            TryWriteByte(GraphicsLayouts.GfxBaseTextFontsType, 0) &&
            TryWriteByte(GraphicsLayouts.GfxBaseTextFontsPad, 0) &&
            SetDefaultFont(defaultFont))
        {
            return true;
        }

        RestoreRange(_gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts, original);
        return false;
    }

    public bool SetDefaultFont(uint fontAddress)
    {
        if ((_gfxBase & 1u) != 0 || !IsOptionalWordAddress(fontAddress))
            return false;

        if (!TrySnapshotRange(
                _gfxBase,
                GraphicsLayouts.GfxBaseDefaultFont,
                4,
                out var original))
        {
            return false;
        }

        if (TryWriteLong(GraphicsLayouts.GfxBaseDefaultFont, fontAddress))
        {
            return true;
        }

        RestoreRange(_gfxBase + (uint)GraphicsLayouts.GfxBaseDefaultFont, original);
        return false;
    }

    public bool TryAdd(uint fontAddress)
    {
        if ((_gfxBase & 1u) != 0 || !IsWordAddress(fontAddress) ||
            fontAddress == 0 ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsHead, out var head) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTailPred, out var tailPred) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTail, out var tail) ||
            tail != 0 ||
            tailPred == 0 ||
            !IsWordAddress(head) ||
            !IsWordAddress(tailPred))
        {
            return false;
        }

        if (!TryEnumerate(out _))
            return false;

        var list = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts;
        var tailSentinel = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail;
        var empty = head == tailSentinel && tailPred == list;
        if ((!empty && (head == 0 || head == tailSentinel || tailPred == list)) ||
            (empty && head != tailSentinel) ||
            (!empty &&
                (!TryReadGuestLong(head, 4, out var firstPred) || firstPred != list ||
             !TryReadGuestLong(tailPred, 0, out var lastSucc) || lastSucc != tailSentinel)) ||
            !TryReadGuestByte(fontAddress, (uint)GraphicsLayouts.TextFontFlags, out _) ||
            !TryReadGuestLong(fontAddress, 0, out var oldSucc) ||
            !TryReadGuestLong(fontAddress, 4, out var oldPred) ||
            !IsOptionalWordAddress(oldSucc) ||
            !IsOptionalWordAddress(oldPred) ||
            oldSucc != 0 ||
            oldPred != 0)
        {
            return false;
        }

        if (!TrySnapshotRange(
                _gfxBase,
                GraphicsLayouts.GfxBaseTextFonts,
                GraphicsLayouts.GfxBaseDefaultFont - GraphicsLayouts.GfxBaseTextFonts,
                out var originalHeader) ||
            !TrySnapshotRange(
                fontAddress,
                0,
                GraphicsLayouts.TextFontFlags + 1,
                out var originalFont))
        {
            return false;
        }

        // AddFont appends to the Exec-style list.  The list header itself is
        // also the predecessor of the first node and the tail sentinel is
        // the successor of the last node.
        if (!TryWriteGuestLong(fontAddress, 0, tailSentinel) ||
            !TryWriteGuestLong(fontAddress, 4, tailPred) ||
            !TryWriteGuestLong(tailPred, 0, fontAddress) ||
            !TryWriteLong(GraphicsLayouts.GfxBaseTextFontsTailPred, fontAddress) ||
            (empty && !TryWriteLong(GraphicsLayouts.GfxBaseTextFontsHead, fontAddress)) ||
            !TryUpdateFontFlags(fontAddress, clearRemoved: true))
        {
            RestoreRange(
                _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts,
                originalHeader);
            RestoreRange(fontAddress, originalFont);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the exact public fields that <c>RemFont</c> will rewrite.
    /// This is deliberately side-effect free: native-overlay admission must
    /// be able to reject a read-only resident/provider node before list links
    /// or the detached font's removed flag are touched.
    /// </summary>
    public bool TryGetRemoveFontPublicationSpans(
        uint fontAddress,
        out IReadOnlyList<(uint Address, int ByteCount)> spans)
    {
        spans = Array.Empty<(uint Address, int ByteCount)>();
        if ((_gfxBase & 1u) != 0 || !IsWordAddress(fontAddress) ||
            fontAddress == 0 ||
            !TryEnumerate(out var fonts) ||
            !ContainsFont(fonts, fontAddress) ||
            !TryReadGuestLong(fontAddress, 0, out var successor) ||
            !TryReadGuestLong(fontAddress, 4, out var predecessor) ||
            successor == 0 || predecessor == 0 ||
            !IsWordAddress(successor) || !IsWordAddress(predecessor))
        {
            return false;
        }

        var list = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts;
        var tailSentinel = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail;
        if (!TryReadLong(GraphicsLayouts.GfxBaseTextFontsHead, out var head) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTailPred, out var tailPred) ||
            !IsOptionalWordAddress(head) ||
            !IsOptionalWordAddress(tailPred) ||
            !TryReadGuestLong(predecessor, 0, out var predecessorSuccessor) ||
            predecessorSuccessor != fontAddress ||
            !TryReadGuestLong(successor, 4, out var successorPredecessor) ||
            successorPredecessor != fontAddress ||
            (predecessor == list && successor == 0) ||
            (head != fontAddress && predecessor == list) ||
            (tailPred != fontAddress && successor == tailSentinel))
        {
            return false;
        }

        var result = new List<(uint Address, int ByteCount)>(7);
        if (!TryAddress(predecessor, 0, out var predecessorAddress) ||
            !TryAddress(successor, 4, out var successorAddress) ||
            !TryAddress(fontAddress, 0, out var fontLinksAddress) ||
            !TryAddress(
                fontAddress,
                (uint)GraphicsLayouts.TextFontFlags,
                out var fontFlagsAddress))
        {
            return false;
        }

        result.Add((predecessorAddress, sizeof(uint)));
        result.Add((successorAddress, sizeof(uint)));
        result.Add((fontLinksAddress, sizeof(uint) * 2));
        result.Add((fontFlagsAddress, sizeof(byte)));

        var headAddress = 0u;
        if (head == fontAddress &&
            !TryAddress(
                _gfxBase,
                (uint)GraphicsLayouts.GfxBaseTextFontsHead,
                out headAddress))
        {
            return false;
        }

        if (head == fontAddress)
            result.Add((headAddress, sizeof(uint)));

        var tailPredAddress = 0u;
        if (tailPred == fontAddress &&
            !TryAddress(
                _gfxBase,
                (uint)GraphicsLayouts.GfxBaseTextFontsTailPred,
                out tailPredAddress))
        {
            return false;
        }

        if (tailPred == fontAddress)
            result.Add((tailPredAddress, sizeof(uint)));

        // The one-node form republishes the canonical empty-list head after
        // the unlink. Keep that second write in the admission set as well;
        // duplicate spans are harmless and make the plan mirror the body.
        var emptyHeadAddress = 0u;
        if (successor == tailSentinel && predecessor == list &&
            !TryAddress(
                _gfxBase,
                (uint)GraphicsLayouts.GfxBaseTextFontsHead,
                out emptyHeadAddress))
        {
            return false;
        }

        if (successor == tailSentinel && predecessor == list)
            result.Add((emptyHeadAddress, sizeof(uint)));

        spans = result;
        return true;
    }

    public bool TryRemove(uint fontAddress)
    {
        if ((_gfxBase & 1u) != 0 || !IsWordAddress(fontAddress) ||
            fontAddress == 0 ||
            !TryEnumerate(out var fonts) ||
            !ContainsFont(fonts, fontAddress) ||
            !TryReadGuestLong(fontAddress, 0, out var successor) ||
            !TryReadGuestLong(fontAddress, 4, out var predecessor) ||
            successor == 0 ||
            predecessor == 0 ||
            !IsWordAddress(successor) ||
            !IsWordAddress(predecessor))
        {
            return false;
        }

        var list = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts;
        var tailSentinel = _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFontsTail;
        if (!TryReadGuestLong(predecessor, 0, out var predecessorSuccessor) ||
            predecessorSuccessor != fontAddress ||
            !TryReadGuestLong(successor, 4, out var successorPredecessor) ||
            successorPredecessor != fontAddress)
        {
            return false;
        }

        if (predecessor == list && successor == 0)
        {
            return false;
        }

        if (!TryReadLong(GraphicsLayouts.GfxBaseTextFontsHead, out var head) ||
            !TryReadLong(GraphicsLayouts.GfxBaseTextFontsTailPred, out var tailPred) ||
            !IsOptionalWordAddress(head) ||
            !IsOptionalWordAddress(tailPred) ||
            (head != fontAddress && predecessor == list) ||
            (tailPred != fontAddress && successor == tailSentinel))
        {
            return false;
        }

        if (!TrySnapshotRange(
                _gfxBase,
                GraphicsLayouts.GfxBaseTextFonts,
                GraphicsLayouts.GfxBaseDefaultFont - GraphicsLayouts.GfxBaseTextFonts,
                out var originalHeader) ||
            !TrySnapshotRange(
                fontAddress,
                0,
                GraphicsLayouts.TextFontFlags + 1,
                out var originalFont) ||
            !TrySnapshotRange(predecessor, 0, 4, out var originalPredecessor) ||
            !TrySnapshotRange(successor, 4, 4, out var originalSuccessor))
        {
            return false;
        }

        if (!TryWriteGuestLong(predecessor, 0, successor) ||
            !TryWriteGuestLong(successor, 4, predecessor) ||
            (head == fontAddress && !TryWriteLong(GraphicsLayouts.GfxBaseTextFontsHead, successor)) ||
            (tailPred == fontAddress && !TryWriteLong(GraphicsLayouts.GfxBaseTextFontsTailPred, predecessor)) ||
            !TryWriteGuestLong(fontAddress, 0, 0) ||
            !TryWriteGuestLong(fontAddress, 4, 0) ||
            !TryUpdateFontFlags(fontAddress, clearRemoved: false))
        {
            RestoreRange(
                _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts,
                originalHeader);
            RestoreRange(fontAddress, originalFont);
            RestoreRange(predecessor, originalPredecessor);
            RestoreRangeAt(successor, 4, originalSuccessor);
            return false;
        }

        // When the last node was removed, restore the canonical empty-list
        // sentinel pair.  The main unlink above already writes the head and
        // tail-predecessor fields, but keep the explicit final head
        // publication for native-list parity.  It is a real write boundary:
        // a byte-level failure here must roll the whole unlink back rather
        // than expose an empty list with a half-removed font.
        if (successor == tailSentinel && predecessor == list &&
            !TryWriteLong(GraphicsLayouts.GfxBaseTextFontsHead, tailSentinel))
        {
            RestoreRange(
                _gfxBase + (uint)GraphicsLayouts.GfxBaseTextFonts,
                originalHeader);
            RestoreRange(fontAddress, originalFont);
            RestoreRange(predecessor, originalPredecessor);
            RestoreRangeAt(successor, 4, originalSuccessor);
            return false;
        }

        return true;
    }

    private bool TryUpdateFontFlags(uint fontAddress, bool clearRemoved)
    {
        if (!TryReadGuestByte(fontAddress, (uint)GraphicsLayouts.TextFontFlags, out var flags))
        {
            return false;
        }

        var updated = clearRemoved
            ? (byte)(flags & ~FontRemovedFlag)
            : (byte)(flags | FontRemovedFlag);
        return TryWriteGuestByte(fontAddress, (uint)GraphicsLayouts.TextFontFlags, updated);
    }

    private static bool ContainsFont(
        IReadOnlyList<uint> fonts,
        uint fontAddress)
    {
        foreach (var font in fonts)
        {
            if (font == fontAddress)
                return true;
        }

        return false;
    }

    private bool TryReadGuestLong(uint baseAddress, uint offset, out uint value)
    {
        value = 0;
        return TryAddress(baseAddress, offset, out var address) &&
            _memory.TryReadLong(address, out value);
    }

    private bool TryWriteGuestLong(uint baseAddress, uint offset, uint value)
        => TryAddress(baseAddress, offset, out var address) &&
            _memory.TryWriteLong(address, value);

    private bool TryReadGuestByte(uint baseAddress, uint offset, out byte value)
    {
        value = 0;
        return TryAddress(baseAddress, offset, out var address) &&
            _memory.TryReadByte(address, out value);
    }

    private bool TryWriteGuestByte(uint baseAddress, uint offset, byte value)
        => TryAddress(baseAddress, offset, out var address) &&
            _memory.TryWriteByte(address, value);

    private bool TryReadLong(int offset, out uint value)
        => _memory.TryReadLong(_gfxBase + (uint)offset, out value);

    private bool TryWriteLong(int offset, uint value)
        => _memory.TryWriteLong(_gfxBase + (uint)offset, value);

    private bool TryWriteByte(int offset, byte value)
        => _memory.TryWriteByte(_gfxBase + (uint)offset, value);

    private bool TrySnapshotRange(
        uint baseAddress,
        int offset,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (offset < 0 || byteCount <= 0 ||
            !TryAddress(baseAddress, (uint)offset, out var start))
        {
            return false;
        }

        original = new byte[byteCount];
        for (var index = 0; index < byteCount; index++)
        {
            if (!TryAddress(start, (uint)index, out var address) ||
                !_memory.TryReadByte(address, out original[index]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private void RestoreRange(uint baseAddress, byte[] original)
    {
        for (var index = original.Length - 1; index >= 0; index--)
        {
            if (TryAddress(baseAddress, (uint)index, out var address))
                _ = _memory.TryWriteByte(address, original[index]);
        }
    }

    private void RestoreRangeAt(uint baseAddress, int offset, byte[] original)
    {
        if (offset >= 0 && TryAddress(baseAddress, (uint)offset, out var start))
            RestoreRange(start, original);
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

    private static bool IsWordAddress(uint address) => (address & 1u) == 0;

    private static bool IsOptionalWordAddress(uint address)
        => address == 0 || IsWordAddress(address);
}
