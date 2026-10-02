using System;
using System.Collections.Generic;
using System.Numerics;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Decodes the classic graphics.library <c>TextFont</c> arrays directly from
/// guest memory.  The decoder deliberately does not own allocation or host
/// font objects, so the same contract can be used by the future native 68k
/// implementation.
/// </summary>
internal sealed class GraphicsMemoryFontBackend :
    IGraphicsFontBackend,
    IGraphicsColorFontBackend,
    IGraphicsFontLifecycleBackend,
    IGraphicsFontMatchBackend
{
    private const ushort ExtensionMatchWord = 0xF00D;
    private const byte ExtensionFlagNoRemove = 1 << 0;
    private const int MaxFontMatchWeight = 32767;
    private const int DeviceDpiMismatchPenaltyBit = 12;
    private const int MaximumFontTagItems = 256;
    private const uint TagDone = 0;
    private const uint TagIgnore = 1;
    private const uint TagMore = 2;
    private const uint TagSkip = 3;
    private const uint TagUser = 0x8000_0000;
    private const uint DeviceDpiTag = TagUser | 1;
    private const int TextAttrTagsOffset = GraphicsLayouts.TextAttrSize;
    private const byte StyleUnderlined = 1 << 0;
    private const byte StyleBold = 1 << 1;
    private const byte StyleItalic = 1 << 2;
    private const byte StyleTagged = 1 << 7;
    private const byte FontStyleColorFont = 1 << 6;
    private const byte FontFlagDisk = 1 << 1;
    private const byte FontFlagRevPath = 1 << 2;
    private const byte FontFlagDesigned = 1 << 6;
    private const byte FontFlagRemoved = 1 << 7;
    private const byte FontFlagRomFont = 1 << 0;
    private const ushort ColorFontFlagColor = 1 << 0;
    private const ushort ColorFontFlagGrey = 1 << 1;
    private const ushort ColorFontFlagAntiAlias = 1 << 2;
    private readonly IGraphicsMemory _memory;
    private readonly IGraphicsAllocatorBackend? _allocator;
    private readonly Func<uint>? _defaultFont;
    private readonly IGraphicsFontListBackend? _fontList;
    private readonly List<uint> _registeredFonts = new();
    private readonly HashSet<uint> _openedFonts = new();
    private readonly HashSet<uint> _removedFonts = new();
    private readonly Dictionary<uint, uint> _ownedExtensions = new();
    private readonly Dictionary<uint, uint> _ownedExtensionAllocations = new();
    private readonly Dictionary<uint, uint> _originalExtensions = new();
    private readonly Dictionary<uint, byte> _originalTaggedStyles = new();

    internal GraphicsMemoryFontBackend(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend? allocator = null,
        Func<uint>? defaultFont = null,
        IGraphicsFontListBackend? fontList = null)
    {
        _memory = memory;
        _allocator = allocator;
        _defaultFont = defaultFont;
        _fontList = fontList;
    }

    internal bool HasMetrics(uint fontAddress)
    {
        if (!TryReadFontHeader(
                fontAddress,
                out var ySize,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out var charData,
                out var modulo,
                out var charLoc,
                out _,
                out _))
        {
            return false;
        }

        return TryValidateStrikeEnvelope(charData, ySize, modulo, charLoc != 0) &&
            TryValidateGlyphTables(fontAddress);
    }

    /// <summary>
    /// Reports whether this graphics-library instance has an outstanding
    /// <c>OpenFont</c> accessor for the guest font.  A resident native font
    /// may be visible through the rebound TextFonts list without being owned
    /// by the compatibility overlay; native CloseFont must remain available
    /// for that case.
    /// </summary>
    internal bool IsOwnedOpenFont(uint fontAddress)
        => fontAddress != 0 && _openedFonts.Contains(fontAddress);

    /// <summary>
    /// Reports whether the current guest extension pointer still names the
    /// envelope allocated by this graphics-library instance.  StripFont must
    /// not preflight or detach a foreign/provider extension, even when the
    /// same font address was previously extended by this backend and later
    /// repointed by its owner.
    /// </summary>
    internal bool IsOwnedExtension(uint fontAddress)
    {
        if (fontAddress == 0 ||
            !_ownedExtensions.TryGetValue(fontAddress, out var ownedExtension))
        {
            return false;
        }

        return TryReadAt(
                fontAddress,
                GraphicsLayouts.TextFontExtension,
                out uint currentExtension) &&
            currentExtension == ownedExtension;
    }

    /// <summary>
    /// CloseFont may remove a non-ROM resident when its final accessor closes.
    /// Report the narrow case in which that chained removal will reach this
    /// backend's owned extension, without performing any publication.
    /// </summary>
    internal bool WillCloseStripOwnedExtension(uint fontAddress)
    {
        if (!IsOwnedExtension(fontAddress) ||
            !TryReadAccessor(fontAddress, out var accessors) ||
            accessors != 1)
        {
            return false;
        }

        var wasRegistered = _registeredFonts.Contains(fontAddress);
        var wasGuestListed = false;
        if (!wasRegistered && _fontList is not null &&
            !TryIsGuestListed(fontAddress, out wasGuestListed))
        {
            return false;
        }

        if (!wasRegistered && !wasGuestListed ||
            !TryReadByteAt(
                fontAddress,
                GraphicsLayouts.TextFontFlags,
                out var flags) ||
            (flags & FontFlagRomFont) != 0 ||
            IsDefaultFont(fontAddress) ||
            !TryHasNoRemoveFlag(fontAddress, out var noRemove))
        {
            return false;
        }

        return !noRemove;
    }

    /// <summary>
    /// RemFont performs the same owned-extension strip after it has removed
    /// the font from the guest list.  Keep that chained publication query
    /// side-effect free so native-overlay admission can include the pointer
    /// and style fields in its preflight.
    /// </summary>
    internal bool WillRemoveStripOwnedExtension(uint fontAddress)
        => IsOwnedExtension(fontAddress) &&
           TryHasNoRemoveFlag(fontAddress, out var noRemove) &&
           !noRemove;

    public bool TryGetDefaultFont(out uint fontAddress)
    {
        if (_fontList is not null)
            return _fontList.TryGetDefaultFont(out fontAddress);

        if (_defaultFont is not null)
        {
            fontAddress = _defaultFont();
            return fontAddress != 0;
        }

        fontAddress = 0;
        return false;
    }

    public bool TryWeighTAMatch(
        uint requestedTextAttr,
        uint targetTextAttr,
        uint targetTags,
        out short weight)
    {
        weight = 0;
        // The public matcher deliberately does not use either ta_Name field.
        // Keep its read set separate from OpenFont's full TextAttr decoder so
        // an unreadable name pointer cannot steal a valid metric call from a
        // native/provider implementation. The scored fields and the tagged
        // suffix remain fully word-aligned and bounded below.
        if (!TryReadMatchAttributes(requestedTextAttr, out var requested) ||
            !TryReadMatchAttributes(targetTextAttr, out var target))
        {
            return false;
        }

        var matchWeight = WeighTextAttributes(requested, target);
        if ((requested.Style & StyleTagged) != 0)
        {
            // The public ABI receives a TTextAttr in A0 when FSF_TAGGED is
            // set.  The first eight bytes are still the TextAttr prefix; the
            // tta_Tags pointer follows it.  Validate both lists before using
            // their optional TA_DeviceDPI value so malformed guest chains
            // fail closed instead of partially influencing a score.
            var requestedHasDpi = false;
            var requestedDpi = 0u;
            if (!TryReadAt(requestedTextAttr, TextAttrTagsOffset, out uint requestedTags) ||
                !TryReadDeviceDpiTags(targetTags, out var targetHasDpi, out var targetDpi) ||
                !TryReadDeviceDpiTags(requestedTags, out requestedHasDpi, out requestedDpi))
            {
                return false;
            }

            // A missing TA_DeviceDPI tag means that the font has no known
            // aspect ratio and therefore does not participate in the ratio
            // comparison.  When both values are present, compare their
            // normalized Y/X ratio (not their raw DPI magnitudes).
            if (requestedTags == 0)
            {
                requestedHasDpi = false;
                requestedDpi = 0;
            }

            matchWeight = ApplyDeviceDpiWeight(
                matchWeight,
                requestedHasDpi,
                requestedDpi,
                targetHasDpi,
                targetDpi);
        }

        weight = unchecked((short)matchWeight);
        return true;
    }

    /// <summary>
    /// Validates the request envelope used by the public OpenFont gateway.
    /// OpenFont may legitimately return zero for a readable request with no
    /// matching resident font, so the register adapter needs this separate
    /// status to distinguish that result from malformed tagged suffixes that
    /// must remain available to native/provider ownership.
    /// </summary>
    internal bool TryValidateTextAttrRequest(uint textAttrAddress)
    {
        if (!GraphicsFontOperations.TryReadTextAttributes(
                _memory,
                textAttrAddress,
                out var requested))
        {
            return false;
        }

        if ((requested.Style & StyleTagged) == 0)
            return true;

        return TryReadAt(textAttrAddress, TextAttrTagsOffset, out uint requestedTags) &&
            TryReadDeviceDpiTags(requestedTags, out _, out _);
    }

    /// <summary>
    /// Resolves the same resident candidate that <see cref="TryOpen"/> would
    /// select, without publishing an accessor increment.  The native overlay
    /// uses this read-only selection pass to preflight the exact WORD it will
    /// mutate before claiming a provider-owned font.
    /// </summary>
    internal bool TryResolveOpenFont(uint textAttrAddress, out uint fontAddress)
    {
        fontAddress = 0;
        if (!GraphicsFontOperations.TryReadTextAttributes(_memory, textAttrAddress, out var requested))
            return false;

        var requestedHasDpi = false;
        var requestedDpi = 0u;
        if ((requested.Style & StyleTagged) != 0)
        {
            if (!TryReadAt(textAttrAddress, TextAttrTagsOffset, out uint requestedTags) ||
                !TryReadDeviceDpiTags(requestedTags, out requestedHasDpi, out requestedDpi))
            {
                return false;
            }
        }

        IReadOnlyList<uint> candidates;
        if (_fontList is not null)
        {
            // The guest list is authoritative when a graphics-library base
            // backend is attached.  Native overlay calls must see resident
            // nodes that were not inserted by this host instance, and a
            // malformed list must decline rather than silently falling back
            // to the private compatibility registry.
            if (!_fontList.TryEnumerate(out candidates))
            {
                fontAddress = 0;
                return false;
            }
        }
        else
        {
            candidates = _registeredFonts;
        }

        var bestScore = 0;
        foreach (var candidate in candidates)
        {
            if (TryScoreCandidate(
                    candidate,
                    requested,
                    false,
                    requestedHasDpi,
                    requestedDpi,
                    out var score) &&
                score > bestScore)
            {
                bestScore = score;
                fontAddress = candidate;
            }
        }

        uint defaultFont;
        if (_fontList is not null)
        {
            if (!_fontList.TryGetDefaultFont(out defaultFont))
            {
                fontAddress = 0;
                return false;
            }
        }
        else
        {
            defaultFont = _defaultFont?.Invoke() ?? 0;
        }

        if (defaultFont != 0 &&
            TryScoreCandidate(
                defaultFont,
                requested,
                true,
                requestedHasDpi,
                requestedDpi,
                out var defaultScore) &&
            defaultScore > bestScore)
        {
            fontAddress = defaultFont;
        }

        return true;
    }

    public bool TryOpen(uint textAttrAddress, out uint fontAddress)
    {
        if (!TryResolveOpenFont(textAttrAddress, out fontAddress) ||
            fontAddress == 0 ||
            !TryIncrementAccessors(fontAddress))
        {
            fontAddress = 0;
            return false;
        }

        _openedFonts.Add(fontAddress);
        return true;
    }

    public bool TryClose(uint fontAddress)
    {
        // tf_Accessors is a native WORD field.  Do not let a byte-addressable
        // host adapter turn an odd TextFont pointer into a partial CloseFont
        // mutation; that envelope belongs to the 68000/native-provider
        // boundary.
        if (fontAddress == 0 || (fontAddress & 1u) != 0)
            return false;

        var wasRegistered = _registeredFonts.Contains(fontAddress);
        var wasOpened = _openedFonts.Contains(fontAddress);
        var wasGuestListed = false;
        if (!wasRegistered && _fontList is not null &&
            !TryIsGuestListed(fontAddress, out wasGuestListed))
        {
            // A font opened from a rebound native list must not be closed
            // through a partial or malformed list snapshot. Leave the
            // accessor word untouched so the resident/provider owner can
            // retry the call with its own list semantics.
            return false;
        }

        if ((!wasRegistered &&
             !wasOpened &&
             !wasGuestListed &&
             !_removedFonts.Contains(fontAddress) &&
             !IsDefaultFont(fontAddress)) ||
            !TryReadAccessor(fontAddress, out var accessors))
        {
            return false;
        }

        if (accessors == 0)
        {
            _removedFonts.Remove(fontAddress);
            _openedFonts.Remove(fontAddress);
            return true;
        }

        byte flags = 0;
        if (accessors == 1 && (wasRegistered || wasGuestListed) &&
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out flags))
        {
            return false;
        }

        var nextAccessors = unchecked((ushort)(accessors - 1));
        if (!TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontAccessors,
                out var accessorAddress) ||
            !TrySnapshotRange(
                accessorAddress,
                sizeof(ushort),
                out var originalAccessorBytes))
        {
            return false;
        }

        var closed = TryWriteAt(
            fontAddress,
            GraphicsLayouts.TextFontAccessors,
            nextAccessors);
        if (!closed)
        {
            RestoreRange(_memory, accessorAddress, originalAccessorBytes);
            return false;
        }

        if (closed && accessors == 1)
        {
            if ((wasRegistered || wasGuestListed) &&
                (flags & FontFlagRomFont) == 0 &&
                !IsDefaultFont(fontAddress))
            {
                // Kickstart CloseFont removes a non-ROM font when its last
                // accessor closes, including resident nodes obtained from a
                // rebound guest TextFonts list.  The reset-scoped
                // compatibility default is the one resident exception even
                // when its synthetic TextFont does not advertise
                // FPF_ROMFONT: screen teardown must be able to reopen that
                // shared default on the next screen.  A canonical
                // TE0F_NOREMFONT extension is the other intentional veto;
                // malformed extension state is a failed close so the
                // accessor write remains retryable.
                if (!TryHasNoRemoveFlag(fontAddress, out var noRemove))
                {
                    RestoreRange(_memory, accessorAddress, originalAccessorBytes);
                    return false;
                }

                if (!noRemove && !TryRemove(fontAddress))
                {
                    RestoreRange(_memory, accessorAddress, originalAccessorBytes);
                    return false;
                }
            }
            else if (!wasRegistered)
            {
                _removedFonts.Remove(fontAddress);
            }

            _openedFonts.Remove(fontAddress);
        }

        return closed;
    }

    public bool TryAdd(uint fontAddress)
    {
        if (fontAddress == 0 ||
            !GraphicsFontOperations.TryReadTextFontAttributes(_memory, fontAddress, out _) ||
            !HasMetrics(fontAddress))
        {
            return false;
        }

        // AddFont is a list insertion, not an idempotent registration.  A
        // duplicate node would corrupt the Exec list and is rejected by the
        // classic library as an invalid public TextFont.
        if (_registeredFonts.Contains(fontAddress))
            return false;

        if (!TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontAccessors,
                out var accessorAddress) ||
            !TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontNodeType,
                out var nodeTypeAddress) ||
            !TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontFlags,
                out var flagsAddress) ||
            !TrySnapshotRange(
                accessorAddress,
                sizeof(ushort),
                out var originalAccessorBytes) ||
            !TrySnapshotRange(
                nodeTypeAddress,
                sizeof(byte),
                out var originalNodeTypeBytes) ||
            !TrySnapshotRange(
                flagsAddress,
                sizeof(byte),
                out var originalFlagsBytes) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out var flags))
        {
            return false;
        }

        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontNodeType, out var originalNodeType) ||
            !TryReadAccessor(fontAddress, out var originalAccessors) ||
            !TryWriteAt(fontAddress, GraphicsLayouts.TextFontAccessors, 0) ||
            !TryWriteByteAt(
                fontAddress,
                GraphicsLayouts.TextFontNodeType,
                GraphicsLayouts.TextFontNodeTypeFont))
        {
            RestoreRange(_memory, accessorAddress, originalAccessorBytes);
            RestoreRange(_memory, nodeTypeAddress, originalNodeTypeBytes);
            RestoreRange(_memory, flagsAddress, originalFlagsBytes);
            return false;
        }

        if (_fontList is not null)
        {
            if (!_fontList.TryAdd(fontAddress))
            {
                RestoreRange(_memory, accessorAddress, originalAccessorBytes);
                RestoreRange(_memory, nodeTypeAddress, originalNodeTypeBytes);
                RestoreRange(_memory, flagsAddress, originalFlagsBytes);
                return false;
            }
        }
        else if (!TryWriteByteAt(
                     fontAddress,
                     GraphicsLayouts.TextFontFlags,
                     (byte)(flags & 0x7F)))
        {
            RestoreRange(_memory, accessorAddress, originalAccessorBytes);
            RestoreRange(_memory, nodeTypeAddress, originalNodeTypeBytes);
            RestoreRange(_memory, flagsAddress, originalFlagsBytes);
            return false;
        }

        _registeredFonts.Add(fontAddress);
        _removedFonts.Remove(fontAddress);
        return true;
    }

    public bool TryRemove(uint fontAddress)
    {
        // TextFont has WORD/LONG members.  Do not let the compatibility
        // backend decode an odd, byte-readable address when no guest list is
        // present to perform the native-list alignment admission first.
        if (fontAddress == 0 || (fontAddress & 1u) != 0)
            return false;

        var wasRegistered = _registeredFonts.Contains(fontAddress);
        var wasGuestListed = false;
        if (!wasRegistered && _fontList is not null &&
            !TryIsGuestListed(fontAddress, out wasGuestListed))
        {
            return false;
        }

        if (!wasRegistered && !wasGuestListed)
        {
            return false;
        }

        // RemFont may be called while an already-open pointer still exists;
        // retain that accessor count so a late StripFont failure can restore
        // the complete public state before the caller retries.
        if (!TryReadAccessor(fontAddress, out _))
        {
            return false;
        }

        if (!TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontAccessors,
                out var accessorAddress) ||
            !TrySnapshotRange(
                accessorAddress,
                sizeof(ushort),
                out var originalAccessorBytes) ||
            !TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontFlags,
                out var flagsAddress) ||
            !TrySnapshotRange(
                flagsAddress,
                sizeof(byte),
                out var originalFlagsBytes))
        {
            return false;
        }

        // RemFont must honor a valid TextFontExtension carrying
        // TE0F_NOREMFONT.  Check the complete extension identity before
        // touching either the guest list or FPF_REMOVED so a veto leaves the
        // font fully available and retryable for the caller.
        if (!TryCanRemoveFont(fontAddress))
        {
            return false;
        }

        if (_fontList is not null)
        {
            if (!_fontList.TryRemove(fontAddress))
                return false;
        }
        else if (!TryWriteByteAt(
                     fontAddress,
                     GraphicsLayouts.TextFontFlags,
                     (byte)(originalFlagsBytes[0] | 0x80)))
        {
            return false;
        }

        if (wasRegistered && !_registeredFonts.Remove(fontAddress))
            return false;

        _removedFonts.Add(fontAddress);
        if (TryStrip(fontAddress))
        {
            return true;
        }

        // StripFont owns the extension teardown, so a failed detach must not
        // leave the font half-removed.  Reinsert the node through the same
        // AddFont boundary and restore the accessor count that RemFont did
        // not own.
        if (TryAdd(fontAddress))
        {
            if (!wasRegistered)
                _registeredFonts.Remove(fontAddress);

            // AddFont resets the public accessor word before re-publication.
            // Restore the caller-visible count byte-wise: a bridge may reject
            // the repeated WORD used by the ordinary rollback path.
            RestoreRange(_memory, accessorAddress, originalAccessorBytes);
        }
        else
        {
            // AddFont has its own transaction, but it may decline before it
            // can reinsert the node (for example, when a bridge rejects the
            // fallback accessor WORD). Restore the availability boundary
            // directly so a failed RemFont cannot strand a removed node.
            RestoreRange(_memory, accessorAddress, originalAccessorBytes);
            var restored = _fontList is null;
            if (_fontList is not null)
                restored = _fontList.TryAdd(fontAddress);
            else
                RestoreRange(_memory, flagsAddress, originalFlagsBytes);

            if (restored)
            {
                if (wasRegistered)
                    _registeredFonts.Add(fontAddress);
                _removedFonts.Remove(fontAddress);
            }
        }

        return false;
    }

    private bool TryCanRemoveFont(uint fontAddress)
        => TryHasNoRemoveFlag(fontAddress, out var noRemove) && !noRemove;

    private bool TryIsGuestListed(uint fontAddress, out bool listed)
    {
        listed = false;
        if (_fontList is null)
            return true;

        if (!_fontList.TryEnumerate(out var fonts))
            return false;

        foreach (var font in fonts)
        {
            if (font == fontAddress)
            {
                listed = true;
                break;
            }
        }

        return true;
    }

    private bool TryHasNoRemoveFlag(uint fontAddress, out bool noRemove)
    {
        noRemove = false;
        if (!TryReadAt(
                fontAddress,
                GraphicsLayouts.TextFontExtension,
                out uint extensionAddress))
        {
            return false;
        }

        if (extensionAddress == 0)
        {
            return true;
        }

        // TextFontExtension contains WORD/LONG fields.  A byte-readable odd
        // pointer is not a valid native 68000 extension envelope; leave the
        // removal decision to the resident/provider owner instead of
        // decoding a byte-shifted match word or back pointer here.
        if ((extensionAddress & 1u) != 0)
        {
            return false;
        }

        if (!TryReadAt(
                extensionAddress,
                GraphicsLayouts.TextFontExtensionMatchWord,
                out ushort matchWord) ||
            !TryReadByteAt(
                extensionAddress,
                GraphicsLayouts.TextFontExtensionFlags0,
                out var flags0) ||
            !TryReadAt(
                extensionAddress,
                GraphicsLayouts.TextFontExtensionBackPtr,
                out uint backPointer))
        {
            return false;
        }

        // Foreign extension records are not governed by TE0F_NOREMFONT;
        // only the canonical match word/back-pointer pair identifies the
        // TextFontExtension contract described by RemFont.
        noRemove = matchWord == ExtensionMatchWord &&
            backPointer == fontAddress &&
            (flags0 & ExtensionFlagNoRemove) != 0;
        return true;
    }

    public bool TryExtend(uint fontAddress, uint fontTags)
    {
        if (fontAddress == 0 ||
            !GraphicsFontOperations.TryReadTextFontAttributes(_memory, fontAddress, out var fontAttributes))
        {
            return false;
        }

        if (!TryReadAt(fontAddress, GraphicsLayouts.TextFontExtension, out uint existingExtension))
            return false;

        if (existingExtension != 0)
            return (existingExtension & 1u) == 0;

        // ExtendFont clones the caller's tag list before publishing the
        // system-owned TextFontExtension.  Validate and materialize the
        // complete bounded guest chain first so an unreadable or cyclic list
        // cannot become a tagged font that later OpenFont/WeighTAMatch calls
        // would dereference only after the extension was already exposed.
        if (!TryCloneFontTags(fontTags, out var clonedTags))
        {
            return false;
        }

        var tagBytes = checked((uint)clonedTags.Count * 8u);
        var allocationBytes = (uint)GraphicsLayouts.TextFontExtensionSize + tagBytes;
        if (_allocator is null)
        {
            return false;
        }

        var allocationSucceeded = _allocator.TryAllocate(
            allocationBytes,
            GraphicsMemoryClass.Public,
            out var extensionAddress);
        if (!allocationSucceeded || extensionAddress == 0 || (extensionAddress & 1u) != 0)
        {
            // Allocator implementations normally clear the out address on
            // failure, but the portable boundary must also be safe for a
            // backend that returns a provisional address together with a
            // false status.  No extension has been published yet, so the
            // whole provisional span is still owned by this operation.
            if (extensionAddress != 0)
            {
                _allocator.Free(
                    extensionAddress,
                    allocationBytes,
                    GraphicsMemoryClass.Public);
            }

            return false;
        }

        if (!TryAdd(extensionAddress, (uint)GraphicsLayouts.TextFontExtensionSize, out var clonedTagAddress))
        {
            _allocator.Free(
                extensionAddress,
                allocationBytes,
                GraphicsMemoryClass.Public);
            return false;
        }

        // ExtendFont owns a guest-visible public envelope until the font
        // backlink commits.  Snapshot the complete recycled span before any
        // metadata write so a late tag/pointer failure cannot leave a partial
        // extension available to a native/provider retry.
        if (!TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontExtension,
                out var fontExtensionAddress) ||
            !TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontStyle,
                out var fontStyleAddress) ||
            !TrySnapshotRange(
                extensionAddress,
                allocationBytes,
                out var originalExtension) ||
            !TrySnapshotRange(
                fontExtensionAddress,
                sizeof(uint),
                out var originalFontExtension) ||
            !TrySnapshotRange(
                fontStyleAddress,
                sizeof(byte),
                out var originalFontStyle))
        {
            _allocator.Free(
                extensionAddress,
                allocationBytes,
                GraphicsMemoryClass.Public);
            return false;
        }

        var initialized =
            TryWriteAt(extensionAddress, GraphicsLayouts.TextFontExtensionMatchWord, ExtensionMatchWord) &&
            TryWriteByteAt(extensionAddress, GraphicsLayouts.TextFontExtensionFlags0, 0) &&
            TryWriteByteAt(extensionAddress, GraphicsLayouts.TextFontExtensionFlags1, 0) &&
            TryWriteAt(extensionAddress, GraphicsLayouts.TextFontExtensionBackPtr, fontAddress) &&
            TryWriteAt(extensionAddress, GraphicsLayouts.TextFontExtensionOrigReplyPort, existingExtension) &&
            TryWriteAt(extensionAddress, GraphicsLayouts.TextFontExtensionTags, clonedTagAddress) &&
            TryWriteAt(extensionAddress, 0x10, 0u) &&
            TryWriteAt(extensionAddress, 0x14, 0u);

        if (initialized)
        {
            for (var index = 0; index < clonedTags.Count; index++)
            {
                var itemAddress = clonedTagAddress + (uint)(index * 8);
                initialized =
                    TryWriteAt(itemAddress, 0, clonedTags[index].Tag) &&
                    TryWriteAt(itemAddress, 4, clonedTags[index].Data);
                if (!initialized)
                    break;
            }
        }

        if (!initialized)
        {
            RestoreRange(_memory, extensionAddress, originalExtension);
            _allocator.Free(
                extensionAddress,
                allocationBytes,
                GraphicsMemoryClass.Public);
            return false;
        }

        // FSF_TAGGED advertises that tf_Extension points at a system-owned
        // TextFontExtension.  Publish the pointer and style bit only after
        // the extension is fully initialized; if either guest write fails,
        // leave the font exactly as it was and release the allocation.
        if (!TryWriteAt(fontAddress, GraphicsLayouts.TextFontExtension, extensionAddress) ||
            !TryWriteByteAt(
                fontAddress,
                GraphicsLayouts.TextFontStyle,
                (byte)(fontAttributes.Style | StyleTagged)))
        {
            RestoreRange(
                _memory,
                fontExtensionAddress,
                originalFontExtension);
            RestoreRange(
                _memory,
                fontStyleAddress,
                originalFontStyle);
            RestoreRange(_memory, extensionAddress, originalExtension);
            _allocator.Free(
                extensionAddress,
                allocationBytes,
                GraphicsMemoryClass.Public);
            return false;
        }

        _ownedExtensions[fontAddress] = extensionAddress;
        _ownedExtensionAllocations[fontAddress] = allocationBytes;
        _originalExtensions[fontAddress] = existingExtension;
        _originalTaggedStyles[fontAddress] = (byte)(fontAttributes.Style & StyleTagged);
        return true;
    }

    public bool TryStrip(uint fontAddress)
    {
        // tf_Extension is a native LONG field.  Keep an odd TextFont pointer
        // at the 68000/native-provider boundary even when a host adapter can
        // byte-read the shifted field.
        if (fontAddress == 0 || (fontAddress & 1u) != 0 ||
            !TryReadAt(fontAddress, GraphicsLayouts.TextFontExtension, out uint extensionAddress))
        {
            return false;
        }

        if (extensionAddress == 0)
            return true;

        // StripFont must not byte-decode a malformed extension pointer.  A
        // native WORD/LONG access would raise the 68000 address-error trap,
        // so keep the call available to the resident/provider boundary.
        if ((extensionAddress & 1u) != 0)
            return false;

        // Only extensions allocated by this backend may be detached.  An
        // existing extension can belong to diskfont.library or an application;
        // StripFont must not free it or clear a pointer it does not own.
        if (!_ownedExtensions.TryGetValue(fontAddress, out var ownedExtension) ||
            ownedExtension != extensionAddress)
        {
            return true;
        }

        var originalExtension = _originalExtensions.TryGetValue(fontAddress, out var savedExtension)
            ? savedExtension
            : 0u;
        var originalTaggedStyle = _originalTaggedStyles.TryGetValue(fontAddress, out var savedTaggedStyle)
            ? savedTaggedStyle
            : (byte)0;

        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontStyle, out var style))
            return false;

        var nextStyle = (byte)((style & ~StyleTagged) | originalTaggedStyle);
        if (!TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontExtension,
                out var fontExtensionAddress) ||
            !TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontStyle,
                out var fontStyleAddress) ||
            !TrySnapshotRange(
                fontExtensionAddress,
                sizeof(uint),
                out var originalExtensionField) ||
            !TrySnapshotRange(
                fontStyleAddress,
                sizeof(byte),
                out var originalStyleField))
        {
            return false;
        }

        if (!TryWriteAt(fontAddress, GraphicsLayouts.TextFontExtension, originalExtension) ||
            !TryWriteByteAt(fontAddress, GraphicsLayouts.TextFontStyle, nextStyle))
        {
            // The first LONG may have accepted one to three bytes and the
            // adapter may reject every repeated LONG. Restore the complete
            // public fields byte-wise so StripFont remains retryable.
            RestoreRange(_memory, fontExtensionAddress, originalExtensionField);
            RestoreRange(_memory, fontStyleAddress, originalStyleField);
            return false;
        }

        _ownedExtensions.Remove(fontAddress);
        var allocationBytes = _ownedExtensionAllocations.TryGetValue(fontAddress, out var ownedBytes)
            ? ownedBytes
            : (uint)GraphicsLayouts.TextFontExtensionSize;
        _ownedExtensionAllocations.Remove(fontAddress);
        _originalExtensions.Remove(fontAddress);
        _originalTaggedStyles.Remove(fontAddress);
        _allocator?.Free(
            ownedExtension,
            allocationBytes,
            GraphicsMemoryClass.Public);

        return true;
    }

    public bool TryGetMetrics(uint fontAddress, out GraphicsFontMetrics metrics)
    {
        if (!TryReadFontHeader(
                fontAddress,
                out var ySize,
                out var xSize,
                out var baseline,
                out var style,
                out var boldSmear,
                out var loChar,
                out var hiChar,
                out _,
                out _,
                out _,
                out var charSpace,
                out var charKern))
        {
            metrics = default;
            return false;
        }

        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out var flags))
        {
            metrics = default;
            return false;
        }

        metrics = new GraphicsFontMetrics(
            ySize,
            xSize,
            baseline,
            0,
            style,
            boldSmear,
            flags,
            loChar,
            hiChar,
            hasCharSpace: charSpace != 0,
            hasCharKern: charKern != 0);
        return true;
    }

    public bool TryGetMetricMetrics(uint fontAddress, out GraphicsFontMetrics metrics)
    {
        // The resident metric vectors read TextFont scalar fields and their
        // optional metric tables, but they do not require a mapped strike
        // envelope.  Keep this query decoder distinct from TryGetMetrics,
        // which remains the admission path for Text/SetFont and lifecycle
        // operations that may later dereference glyph rows.
        if (!TryReadMetricScalars(
                fontAddress,
                out var ySize,
                out var xSize,
                out var baseline,
                out var style,
                out var boldSmear,
                out var loChar,
                out var hiChar,
                out var charSpace,
                out var charKern) ||
            hiChar < loChar)
        {
            metrics = default;
            return false;
        }

        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out var flags))
        {
            metrics = default;
            return false;
        }

        metrics = new GraphicsFontMetrics(
            ySize,
            xSize,
            baseline,
            0,
            style,
            boldSmear,
            flags,
            loChar,
            hiChar,
            hasCharSpace: charSpace != 0,
            hasCharKern: charKern != 0);
        return true;
    }

    public bool TryGetMetricGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph)
    {
        glyph = default;
        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out var flags) ||
            !TryReadMetricHeader(
                fontAddress,
                out var ySize,
                out var xSize,
                out _,
                out _,
                out _,
                out var loChar,
                out var hiChar,
            out var charLoc,
                out var charSpace,
                out var charKern) ||
            hiChar < loChar)
        {
            return false;
        }

        var index = character >= loChar && character <= hiChar
            ? (uint)(character - loChar)
            : (uint)(hiChar - loChar + 1);
        return TryGetGlyphAtIndex(
            flags,
            ySize,
            xSize,
            0,
            0,
            charLoc,
            charSpace,
            charKern,
            index,
            requireStrike: false,
            out glyph);
    }

    public bool TryGetMetricDefaultGlyph(uint fontAddress, out GraphicsGlyph glyph)
    {
        glyph = default;
        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out var flags) ||
            !TryReadMetricHeader(
                fontAddress,
                out var ySize,
                out var xSize,
                out _,
                out _,
                out _,
                out var loChar,
                out var hiChar,
            out var charLoc,
                out var charSpace,
                out var charKern) ||
            hiChar < loChar)
        {
            return false;
        }

        var index = (uint)(hiChar - loChar + 1);
        return TryGetGlyphAtIndex(
            flags,
            ySize,
            xSize,
            0,
            0,
            charLoc,
            charSpace,
            charKern,
            index,
            requireStrike: false,
            out glyph);
    }

    public bool TryGetStyle(uint fontAddress, out byte style)
    {
        style = 0;
        if (fontAddress == 0 || (fontAddress & 1u) != 0)
            return false;

        // The resident AskSoftStyle/SetSoftStyle bodies read only the
        // intrinsic TextFont style byte.  Do not require the strike, glyph
        // tables, or cached metric fields that Text/metrics consume.
        return TryReadByteAt(fontAddress, GraphicsLayouts.TextFontStyle, out style);
    }

    public bool TryGetColorFont(uint fontAddress, out GraphicsColorFontInfo colorFont)
    {
        colorFont = default;
        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontStyle, out var style) ||
            (style & FontStyleColorFont) == 0 ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.ColorTextFontFlags, out var flags) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.ColorTextFontDepth, out var depth) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.ColorTextFontForegroundColor, out var foregroundColor) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.ColorTextFontLowColor, out var lowColor) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.ColorTextFontHighColor, out var highColor) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.ColorTextFontPlanePick, out var planePick) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.ColorTextFontPlaneOnOff, out var planeOnOff) ||
            depth > 8 ||
            lowColor > highColor ||
            (flags & (ColorFontFlagColor | ColorFontFlagGrey)) == 0 ||
            (flags & ColorFontFlagAntiAlias) != 0)
        {
            return false;
        }

        // The depth is the number of source planes, while PlanePick names the
        // destination planes that receive them. A malformed font that asks
        // for more source planes than destination slots cannot be composed by
        // the classic Image-style mapping and remains native/provider-owned.
        var picked = BitOperations.PopCount(planePick);
        if (picked < depth)
            return false;

        var planeData = new uint[depth];
        for (var plane = 0; plane < depth; plane++)
        {
            if (!TryReadLongAt(
                    fontAddress,
                    GraphicsLayouts.ColorTextFontCharacterData + plane * 4,
                    out planeData[plane]) ||
                planeData[plane] == 0 ||
                (planeData[plane] & 1u) != 0)
            {
                return false;
            }
        }

        colorFont = new GraphicsColorFontInfo(
            flags,
            depth,
            foregroundColor,
            lowColor,
            highColor,
            planePick,
            planeOnOff,
            planeData);
        return true;
    }

    public bool TryGetColorGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph)
    {
        glyph = default;
        if (!TryGetColorFont(fontAddress, out var colorFont) ||
            !TryGetGlyph(fontAddress, character, out var baseGlyph) ||
            colorFont.Depth == 0)
        {
            // A depth-zero ColorTextFont is still a valid constant-plane
            // image, so retain the ordinary glyph geometry when there are no
            // source plane pointers to substitute.
            return colorFont.Depth == 0 && TryGetGlyph(fontAddress, character, out glyph);
        }

        glyph = new GraphicsGlyph(
            baseGlyph.Width,
            baseGlyph.Height,
            baseGlyph.Advance,
            baseGlyph.Kerning,
            colorFont.PlaneData[0],
            baseGlyph.Modulo,
            baseGlyph.BitOffset);
        return true;
    }

    public bool TryGetGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph)
    {
        glyph = default;
        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out var flags))
        {
            return false;
        }

        if (!TryReadFontHeader(
                fontAddress,
                out var ySize,
                out var xSize,
                out _,
                out _,
                out _,
                out var loChar,
                out var hiChar,
                out var charData,
                out var modulo,
                out var charLoc,
                out var charSpace,
                out var charKern))
        {
            return false;
        }

        // TextFont stores one extra CharLoc/CharSpace/CharKern entry after the
        // requested character range.  Kickstart uses that final entry as the
        // default glyph for characters outside LoChar..HiChar; rejecting those
        // bytes would make text metrics and rendering diverge from ROM fonts.
        var index = character >= loChar && character <= hiChar
            ? (uint)(character - loChar)
            : (uint)(hiChar - loChar + 1);
        return TryGetGlyphAtIndex(
            flags,
            ySize,
            xSize,
            charData,
            modulo,
            charLoc,
            charSpace,
            charKern,
            index,
            requireStrike: true,
            out glyph);
    }

    public bool TryGetDefaultGlyph(uint fontAddress, out GraphicsGlyph glyph)
    {
        glyph = default;
        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontFlags, out var flags) ||
            !TryReadFontHeader(
                fontAddress,
                out var ySize,
                out var xSize,
                out _,
                out _,
                out _,
                out var loChar,
                out var hiChar,
                out var charData,
                out var modulo,
                out var charLoc,
                out var charSpace,
                out var charKern))
        {
            return false;
        }

        // The default slot is index (HiChar-LoChar)+1, including the full
        // 0..255 declaration for which no byte value can name an out-of-range
        // character. Read the table index directly instead of routing through
        // TryGetGlyph's byte-shaped fallback selector.
        var index = (uint)(hiChar - loChar + 1);
        return TryGetGlyphAtIndex(
            flags,
            ySize,
            xSize,
            charData,
            modulo,
            charLoc,
            charSpace,
            charKern,
            index,
            requireStrike: true,
            out glyph);
    }

    private bool TryGetGlyphAtIndex(
        byte flags,
        ushort ySize,
        ushort xSize,
        uint charData,
        ushort modulo,
        uint charLoc,
        uint charSpace,
        uint charKern,
        uint index,
        bool requireStrike,
        out GraphicsGlyph glyph)
    {
        glyph = default;
        ushort bitOffset;
        ushort width;
        if (charLoc != 0)
        {
            if (!TryAdd(charLoc, index * 4, out var locationAddress) ||
                !TryReadWord(locationAddress, out bitOffset) ||
                !TryAdd(locationAddress, 2, out var widthAddress) ||
                !TryReadWord(widthAddress, out width))
            {
                return false;
            }
        }
        else
        {
            // Fixed-width fonts may omit CharLoc entirely.  Their strike is
            // laid out as XSize-bit cells, with Modulo bytes between rows.
            // The final cell is still the default glyph for out-of-range
            // characters, just as it is for proportional fonts.
            if (xSize == 0)
            {
                if (requireStrike)
                    return false;

                bitOffset = 0;
                width = 0;
            }
            else if (index > ushort.MaxValue / xSize)
            {
                return false;
            }
            else
            {
                bitOffset = (ushort)(index * xSize);
                width = xSize;
            }
        }

        if (requireStrike && (charData == 0 || modulo == 0))
            return false;

        // CharLoc is expressed as a bit offset plus a bit width within each
        // modulo-sized strike row.  Reject a glyph whose packed byte span
        // crosses the declared row stride: letting it through would make the
        // renderer read the next row (or an unrelated guest object) as part
        // of the current glyph.  Fixed-cell fonts use the same check because
        // their synthesized bit offset is derived from XSize.
        var rowByteOffset = (uint)(bitOffset >> 3);
        var rowByteCount = ((uint)(bitOffset & 7) + width + 7u) / 8u;
        if (requireStrike && (ulong)rowByteOffset + rowByteCount > modulo)
            return false;

        // CharSpace is optional.  Without it, the nominal TextFont width is
        // the cell advance (including fixed-cell fonts and proportional fonts
        // whose bitmap locations carry only glyph bounds).
        var advance = (flags & FontFlagRevPath) != 0
            ? -(int)xSize
            : (int)xSize;
        if (charSpace != 0)
        {
            if (!TryAdd(charSpace, (uint)index * 2, out var spaceAddress) ||
                !TryReadWord(spaceAddress, out var rawSpace))
            {
                return false;
            }

            advance = unchecked((short)rawSpace);
        }

        var kerning = 0;
        if (charKern != 0)
        {
            if (!TryAdd(charKern, (uint)index * 2, out var kernAddress) ||
                !TryReadWord(kernAddress, out var rawKern))
            {
                return false;
            }

            kerning = unchecked((short)rawKern);
        }

        glyph = new GraphicsGlyph(
            width,
            ySize,
            advance,
            (short)kerning,
            requireStrike ? charData : 0,
            requireStrike ? modulo : (ushort)0,
            bitOffset);
        return true;
    }

    private bool TryScoreCandidate(
        uint fontAddress,
        GraphicsTextAttributes requested,
        bool defaultCandidate,
        bool requestedHasDpi,
        uint requestedDpi,
        out int score)
    {
        score = 0;
        if (defaultCandidate)
        {
            if (!TryReadString(requested.Name, out var requestedName) ||
                !IsTopazName(requestedName))
            {
                return false;
            }
        }

        if (!GraphicsFontOperations.TryReadTextFontAttributes(
                _memory,
                fontAddress,
                out var candidate) ||
            // AddFont validates the strike envelope before publishing a
            // node, but a native graphics.library list may already contain a
            // resident node that was not admitted by this backend.  Do not
            // let such a node win OpenFont and only fail later when Text
            // dereferences its metrics; the portable boundary must select
            // only a font that can actually supply the common TextFont
            // header/strike contract.
            !HasMetrics(fontAddress) ||
            (candidate.Flags & FontFlagRemoved) != 0 ||
            !TryReadString(requested.Name, out var requestedFontName) ||
            !TryReadString(candidate.Name, out var candidateName) ||
            (defaultCandidate
                ? !IsTopazName(candidateName)
                : !NamesEqual(requestedFontName, candidateName)))
        {
            return false;
        }

        score = WeighTextAttributes(requested, candidate);
        if (score == 0 || (requested.Style & StyleTagged) == 0)
            return score != 0;

        // A tagged TextAttr participates in OpenFont selection through the
        // candidate TextFontExtension's tag list.  Missing tags are a valid
        // “unknown aspect” candidate; malformed owned/foreign chains are not
        // allowed to influence a positive match.
        var candidateHasDpi = false;
        var candidateDpi = 0u;
        if (!TryReadAt(fontAddress, GraphicsLayouts.TextFontExtension, out uint extensionAddress))
            return false;

        if (extensionAddress != 0 &&
            ((extensionAddress & 1u) != 0 ||
             !TryReadAt(extensionAddress, GraphicsLayouts.TextFontExtensionTags, out uint candidateTags) ||
             !TryReadDeviceDpiTags(candidateTags, out candidateHasDpi, out candidateDpi)))
        {
            return false;
        }

        score = ApplyDeviceDpiWeight(
            score,
            requestedHasDpi,
            requestedDpi,
            candidateHasDpi,
            candidateDpi);
        return true;
    }

    /// <summary>
    /// Kickstart's OpenFont/OpenDiskFont matcher is based on the private
    /// WeighTAMatch metric, not a weighted Hamming distance.  In particular,
    /// the ROM/DISK source bits are not ordinary style preferences: designed
    /// requests may use designed or disk fonts, REVPATH must match exactly,
    /// and size is penalized asymmetrically before the style penalties are
    /// applied.  Keeping this as a pure metric makes it reusable by a future
    /// native 68k implementation without changing the guest TextFont layout.
    /// </summary>
    private static int WeighTextAttributes(
        GraphicsTextAttributes requested,
        GraphicsTextAttributes candidate)
    {
        var matchWeight = MaxFontMatchWeight;

        // A designed request must not silently fall back to a constructed
        // ROM/host font.  A disk-backed candidate is still eligible even
        // when its size was constructed: Kickstart treats FPF_DISKFONT as a
        // source-qualified match for this gate, while an in-memory candidate
        // must carry FPF_DESIGNED explicitly.
        if ((requested.Flags & FontFlagDesigned) != 0 &&
            (candidate.Flags & (FontFlagDesigned | FontFlagDisk)) == 0)
        {
            return 0;
        }

        // A right-to-left font is not a usable substitute for a left-to-right
        // request (or vice versa).  Other preference bits are intentionally
        // not compared bit-for-bit by WeighTAMatch.
        if (((requested.Flags ^ candidate.Flags) & FontFlagRevPath) != 0)
        {
            return 0;
        }

        // Style penalties mirror the V36-V40 WeighTAMatch bit clearing.  The
        // metric is positive for suitable matches and MAXFONTMATCHWEIGHT for
        // an exact match; OpenFont selects the highest positive weight.
        if ((requested.Style & StyleUnderlined) != 0 &&
            (candidate.Style & StyleUnderlined) == 0)
        {
            matchWeight &= ~(1 << 2);
        }
        if ((requested.Style & StyleUnderlined) == 0 &&
            (candidate.Style & StyleUnderlined) != 0)
        {
            matchWeight &= ~(1 << 11);
        }

        if ((requested.Style & StyleBold) != 0 &&
            (candidate.Style & StyleBold) == 0)
        {
            matchWeight &= ~(1 << 3);
        }
        if ((requested.Style & StyleBold) == 0 &&
            (candidate.Style & StyleBold) != 0)
        {
            matchWeight &= ~(1 << 9);
        }

        if ((requested.Style & StyleItalic) != 0 &&
            (candidate.Style & StyleItalic) == 0)
        {
            matchWeight &= ~(1 << 4);
        }
        if ((requested.Style & StyleItalic) == 0 &&
            (candidate.Style & StyleItalic) != 0)
        {
            matchWeight &= ~(1 << 10);
        }

        // The ROM implementation sign-extends each UWORD through a WORD
        // cast before taking the difference.  This is observable for
        // high-bit sizes: $FFFF and $0001 are two signed units apart in the
        // guest comparison, not 65534 units apart as an unsigned host
        // subtraction would report.
        var sizeDifference = Math.Abs(
            (int)(short)requested.YSize - (int)(short)candidate.YSize);
        if (sizeDifference > 511)
        {
            return 0;
        }

        // Kickstart keeps the shifted penalty in a signed WORD temporary.
        // Preserve that width before the comparison: for an upward delta of
        // 256..511 the 7-bit shift wraps negative on the 68000, and the ROM
        // consequently returns the same wrapped WORD result rather than
        // declining the match as an unbounded host integer would.
        var sizePenalty = unchecked((short)(requested.YSize < candidate.YSize
            ? sizeDifference << 7
            : sizeDifference << 5));
        return sizePenalty > matchWeight ? 0 : matchWeight - sizePenalty;
    }

    private bool TryReadMatchAttributes(
        uint textAttrAddress,
        out GraphicsTextAttributes attributes)
    {
        attributes = default;
        if (textAttrAddress == 0 ||
            (textAttrAddress & 1u) != 0 ||
            !TryReadAt(
                textAttrAddress,
                GraphicsLayouts.TextAttrYSize,
                out ushort ySize) ||
            !TryReadByteAt(
                textAttrAddress,
                GraphicsLayouts.TextAttrStyle,
                out var style) ||
            !TryReadByteAt(
                textAttrAddress,
                GraphicsLayouts.TextAttrFlags,
                out var flags))
        {
            return false;
        }

        // Name is intentionally zero: WeighTAMatch's metric never compares
        // or dereferences it, and the field is not needed by its callers.
        // A zero ta_YSize is still a readable metric input.  The native
        // matcher scores the fields it receives without imposing the
        // nonzero-font-height validation used by OpenFont's font envelope.
        attributes = new GraphicsTextAttributes(0, ySize, style, flags);
        return true;
    }

    private static int ApplyDeviceDpiWeight(
        int matchWeight,
        bool requestedHasDpi,
        uint requestedDpi,
        bool targetHasDpi,
        uint targetDpi)
    {
        if (!requestedHasDpi || !targetHasDpi || matchWeight == 0)
            return matchWeight;

        var requestedX = (ushort)(requestedDpi >> 16);
        var requestedY = (ushort)requestedDpi;
        var targetX = (ushort)(targetDpi >> 16);
        var targetY = (ushort)targetDpi;

        // TA_DeviceDPI stores the reciprocal pixel aspect components.  The
        // values themselves are not physical DPI readings; only Y/X matters.
        // Preserve exact matches even when the two lists use scaled values
        // such as 75:50 and 150:100.
        var exactRatio = requestedX != 0 && requestedY != 0 &&
            targetX != 0 && targetY != 0 &&
            (ulong)requestedY * targetX == (ulong)targetY * requestedX;
        if (exactRatio ||
            (requestedX == targetX && requestedY == targetY &&
             requestedX == 0 && requestedY == 0))
        {
            return matchWeight;
        }

        // The existing metric clears one ranking bit for each non-fatal
        // mismatch (style bits use 2..4 and 9..11).  Keep the same positive,
        // monotonic shape for the optional aspect-ratio mismatch and reserve
        // the next bit for a future native-ROM exact weighting implementation.
        return matchWeight & ~(1 << DeviceDpiMismatchPenaltyBit);
    }

    private bool TryCloneFontTags(
        uint tagList,
        out List<FontTagItem> clonedTags)
    {
        clonedTags = new List<FontTagItem>();
        if (tagList == 0)
        {
            // ExtendFont supplies a private TAG_DONE list when the caller
            // passes NULL.  Keep that list guest-visible too, so the owned
            // extension never retains a borrowed pointer.
            clonedTags.Add(new FontTagItem(TagDone, 0));
            return true;
        }

        // TagItem is a pair of 32-bit guest words.  The 68k ABI requires the
        // list head (and every TAG_MORE continuation) to be word aligned;
        // accepting an odd address here would let the portable decoder read
        // a byte-shifted list that native Kickstart would reject with a bus
        // error.  Keep the extension boundary transparent for that owner.
        if ((tagList & 1u) != 0)
        {
            return false;
        }

        var cursor = tagList;
        var visited = new HashSet<uint>();
        for (var item = 0; item < MaximumFontTagItems; item++)
        {
            if (cursor == 0 ||
                (cursor & 1u) != 0 ||
                // TagItem occupies bytes [cursor..cursor+7].  The final
                // aligned guest start, $FFFF_FFF8, is complete and valid;
                // only a start above max-7 crosses the address space.
                cursor > uint.MaxValue - 7u ||
                !visited.Add(cursor) ||
                !_memory.TryReadLong(cursor, out var tag) ||
                !_memory.TryReadLong(cursor + 4u, out var data))
            {
                clonedTags.Clear();
                return false;
            }

            var nextCursor = cursor + 8u;
            switch (tag)
            {
                case TagDone:
                    clonedTags.Add(new FontTagItem(TagDone, 0));
                    return true;
                case TagIgnore:
                    // CloneTagItems preserves the effective tag stream.  An
                    // ignored item and its payload are not observable by the
                    // later font-extension consumers.
                    if (nextCursor < cursor)
                    {
                        clonedTags.Clear();
                        return false;
                    }

                    cursor = nextCursor;
                    continue;
                case TagMore:
                    if (data == 0)
                    {
                        clonedTags.Add(new FontTagItem(TagDone, 0));
                        return true;
                    }

                    if ((data & 1u) != 0)
                    {
                        clonedTags.Clear();
                        return false;
                    }

                    cursor = data;
                    continue;
                case TagSkip:
                    if (!TryAdvanceTagItems(nextCursor, data, cursor, out var skippedCursor))
                    {
                        clonedTags.Clear();
                        return false;
                    }

                    cursor = skippedCursor;
                    continue;
                default:
                    if (nextCursor < cursor)
                    {
                        clonedTags.Clear();
                        return false;
                    }

                    clonedTags.Add(new FontTagItem(tag, data));
                    if (clonedTags.Count >= MaximumFontTagItems)
                    {
                        clonedTags.Clear();
                        return false;
                    }

                    cursor = nextCursor;
                    continue;
            }
        }

        clonedTags.Clear();
        return false;
    }

    private bool TryReadDeviceDpiTags(
        uint tagList,
        out bool hasDpi,
        out uint dpi)
    {
        hasDpi = false;
        dpi = 0;
        if (tagList == 0)
            return true;

        // The suffix is an Exec TagItem list, not a byte string.  Reject an
        // odd head/continuation before reading any fields so tagged
        // WeighTAMatch/OpenFont calls cannot observe a host-only, unaligned
        // interpretation of malformed guest memory.
        if ((tagList & 1u) != 0)
        {
            return false;
        }

        var cursor = tagList;
        var visited = new HashSet<uint>();
        for (var item = 0; item < MaximumFontTagItems; item++)
        {
            if (cursor == 0 ||
                (cursor & 1u) != 0 ||
                cursor > uint.MaxValue - 7u ||
                !visited.Add(cursor) ||
                !_memory.TryReadLong(cursor, out var tag) ||
                !_memory.TryReadLong(cursor + 4u, out var data))
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

                    if (tag == DeviceDpiTag && !hasDpi)
                    {
                        hasDpi = true;
                        dpi = data;
                    }

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

    private bool IsDefaultFont(uint fontAddress)
        => fontAddress != 0 && _defaultFont?.Invoke() == fontAddress;

    private readonly struct FontTagItem
    {
        internal FontTagItem(uint tag, uint data)
        {
            Tag = tag;
            Data = data;
        }

        internal uint Tag { get; }
        internal uint Data { get; }
    }

    private bool TryIncrementAccessors(uint fontAddress)
    {
        if (!TryReadAccessor(fontAddress, out var accessors) || accessors == ushort.MaxValue)
            return false;

        if (!TryAdd(
                fontAddress,
                (uint)GraphicsLayouts.TextFontAccessors,
                out var accessorAddress) ||
            !TrySnapshotRange(
                accessorAddress,
                sizeof(ushort),
                out var originalAccessorBytes))
        {
            return false;
        }

        if (TryWriteAt(
            fontAddress,
            GraphicsLayouts.TextFontAccessors,
            unchecked((ushort)(accessors + 1))))
        {
            return true;
        }

        // OpenFont owns the increment transaction. Restore both bytes when
        // an adapter accepts only part of the WORD, otherwise a failed open
        // can leak an accessor count that later CloseFont will consume.
        RestoreRange(_memory, accessorAddress, originalAccessorBytes);
        return false;
    }

    private bool TryReadAccessor(uint fontAddress, out ushort accessors)
        => TryReadAt(fontAddress, GraphicsLayouts.TextFontAccessors, out accessors);

    private bool TryReadAt(uint baseAddress, int offset, out ushort value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) &&
            _memory.TryReadWord(address, out value);
    }

    private bool TryReadAt(uint baseAddress, int offset, out uint value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) &&
            _memory.TryReadLong(address, out value);
    }

    private bool TryWriteAt(uint baseAddress, int offset, ushort value)
        => TryAdd(baseAddress, (uint)offset, out var address) &&
            _memory.TryWriteWord(address, value);

    private bool TryWriteAt(uint baseAddress, int offset, uint value)
        => TryAdd(baseAddress, (uint)offset, out var address) &&
            _memory.TryWriteLong(address, value);

    private bool TryWriteByteAt(uint baseAddress, int offset, byte value)
        => TryAdd(baseAddress, (uint)offset, out var address) &&
            _memory.TryWriteByte(address, value);

    private bool TryReadByteAt(uint baseAddress, int offset, out byte value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) &&
            _memory.TryReadByte(address, out value);
    }

    private bool TryReadString(uint address, out string value)
    {
        value = string.Empty;
        if (address == 0)
            return false;

        var chars = new char[256];
        for (var index = 0; index < chars.Length; index++)
        {
            if (!TryAdd(address, (uint)index, out var characterAddress) ||
                !_memory.TryReadByte(characterAddress, out var character))
            {
                return false;
            }

            if (character == 0)
            {
                value = new string(chars, 0, index);
                return true;
            }

            chars[index] = (char)character;
        }

        return false;
    }

    private static bool NamesEqual(string requested, string candidate)
        // OpenFont walks the resident TextFonts list and compares each node
        // name with ta_Name as stored.  Path stripping belongs to
        // OpenDiskFont's file lookup, not this resident-list ABI; otherwise
        // a request for "match.font" could incorrectly open a resident node
        // named "FONTS:match.font".
        => string.Equals(requested, candidate, StringComparison.Ordinal);

    private static string NormalizeName(string name)
    {
        var separator = Math.Max(name.LastIndexOf(':'), Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\')));
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    private static bool IsTopazName(string name)
    {
        var normalized = NormalizeName(name);
        return string.Equals(normalized, "topaz", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "topaz.font", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryReadMetricScalars(
        uint fontAddress,
        out ushort ySize,
        out ushort xSize,
        out ushort baseline,
        out byte style,
        out ushort boldSmear,
        out byte loChar,
        out byte hiChar,
        out uint charSpace,
        out uint charKern)
    {
        ySize = 0;
        xSize = 0;
        baseline = 0;
        style = 0;
        boldSmear = 0;
        loChar = 0;
        hiChar = 0;
        charSpace = 0;
        charKern = 0;

        if (fontAddress == 0 ||
            (fontAddress & 1u) != 0 ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontYSize, out ySize) ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontXSize, out xSize) ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontBaseline, out baseline) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontStyle, out style) ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontBoldSmear, out boldSmear) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontLoChar, out loChar) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontHiChar, out hiChar) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharSpace, out charSpace) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharKern, out charKern))
        {
            return false;
        }

        return (charSpace == 0 || (charSpace & 1u) == 0) &&
            (charKern == 0 || (charKern & 1u) == 0);
    }

    private bool TryReadMetricHeader(
        uint fontAddress,
        out ushort ySize,
        out ushort xSize,
        out ushort baseline,
        out byte style,
        out ushort boldSmear,
        out byte loChar,
        out byte hiChar,
        out uint charLoc,
        out uint charSpace,
        out uint charKern)
    {
        charLoc = 0;
        if (!TryReadMetricScalars(
                fontAddress,
                out ySize,
                out xSize,
                out baseline,
                out style,
                out boldSmear,
                out loChar,
                out hiChar,
                out charSpace,
                out charKern) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharLoc, out charLoc))
        {
            return false;
        }

        return charLoc == 0 || (charLoc & 1u) == 0;
    }

    private bool TryReadFontHeader(
        uint fontAddress,
        out ushort ySize,
        out ushort xSize,
        out ushort baseline,
        out byte style,
        out ushort boldSmear,
        out byte loChar,
        out byte hiChar,
        out uint charData,
        out ushort modulo,
        out uint charLoc,
        out uint charSpace,
        out uint charKern)
    {
        if (!TryReadFontHeaderRaw(
                fontAddress,
                out ySize,
                out xSize,
                out baseline,
                out style,
                out boldSmear,
                out loChar,
                out hiChar,
                out charData,
                out modulo,
                out charLoc,
                out charSpace,
                out charKern))
        {
            return false;
        }

        // CharLoc, CharSpace and CharKern are word-oriented guest tables.
        // A host byte adapter can technically read an odd pointer, but a
        // native 68000 dereference would raise an address error. Reject the
        // complete font before metrics or glyph selection can publish a
        // byte-shifted interpretation. CharData is bitmap byte storage and
        // intentionally remains legal at either parity.
        if ((charLoc != 0 && (charLoc & 1u) != 0) ||
            (charSpace != 0 && (charSpace & 1u) != 0) ||
            (charKern != 0 && (charKern & 1u) != 0))
        {
            return false;
        }

        // A TextFont is not usable until its strike storage is present.  A
        // non-null CharLoc alone is insufficient: accepting that shape would
        // publish a font which can be opened and selected, but whose first
        // glyph dereference necessarily fails.  Keep the guest/native
        // boundary fail-closed and require the common strike envelope before
        // AddFont/OpenFont can expose the node.
        return ySize != 0 &&
            hiChar >= loChar &&
            charData != 0 &&
            modulo != 0 &&
            (charLoc != 0 || xSize != 0);
    }

    private bool TryReadFontHeaderRaw(
        uint fontAddress,
        out ushort ySize,
        out ushort xSize,
        out ushort baseline,
        out byte style,
        out ushort boldSmear,
        out byte loChar,
        out byte hiChar,
        out uint charData,
        out ushort modulo,
        out uint charLoc,
        out uint charSpace,
        out uint charKern)
    {
        ySize = 0;
        xSize = 0;
        baseline = 0;
        style = 0;
        boldSmear = 0;
        loChar = 0;
        hiChar = 0;
        charData = 0;
        modulo = 0;
        charLoc = 0;
        charSpace = 0;
        charKern = 0;

        if (fontAddress == 0 ||
            (fontAddress & 1u) != 0 ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontYSize, out ySize) ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontXSize, out xSize) ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontBaseline, out baseline) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontStyle, out style) ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontBoldSmear, out boldSmear) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontLoChar, out loChar) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontHiChar, out hiChar) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharData, out charData) ||
            !TryReadWordAt(fontAddress, GraphicsLayouts.TextFontModulo, out modulo) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharLoc, out charLoc) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharSpace, out charSpace) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharKern, out charKern))
        {
            return false;
        }

        // CharLoc, CharSpace, and CharKern are WORD-oriented guest tables.
        // A query path may omit strike storage, but it still cannot decode a
        // byte-shifted table through a native 68000 word read.
        return (charLoc == 0 || (charLoc & 1u) == 0) &&
            (charSpace == 0 || (charSpace & 1u) == 0) &&
            (charKern == 0 || (charKern & 1u) == 0);
    }

    private bool TryValidateStrikeEnvelope(
        uint charData,
        ushort ySize,
        ushort modulo,
        bool validateFinalRow)
    {
        // Native AddFont publishes the caller's TextFont without traversing
        // CharLoc/CharSpace/CharKern. Keep that lifecycle contract while
        // protecting the portable/native boundary from a strike whose first
        // byte is readable but whose final scanline wraps out of guest
        // memory. Detailed glyph widths, table entries, and every row used by
        // a particular character remain the responsibility of Text/metrics
        // preflight, so lifecycle admission stays bounded.
        if (charData == 0 || ySize == 0 || modulo == 0)
            return false;

        if (!validateFinalRow)
        {
            // Fixed-cell resident nodes may be published with an opaque
            // modulo envelope whose rows are only touched after SetFont/Text
            // selects the node. Preserve that native lifecycle contract; the
            // renderer's per-glyph preflight remains authoritative for it.
            return _memory.TryReadByte(charData, out _);
        }

        var finalRowOffset = (ulong)(ySize - 1) * modulo;
        if (finalRowOffset > uint.MaxValue - charData)
            return false;

        return _memory.TryReadByte(charData, out _) &&
            _memory.TryReadByte(charData + (uint)finalRowOffset, out _);
    }

    private bool TryValidateGlyphTables(uint fontAddress)
    {
        if (!TryReadByteAt(fontAddress, GraphicsLayouts.TextFontLoChar, out var loChar) ||
            !TryReadByteAt(fontAddress, GraphicsLayouts.TextFontHiChar, out var hiChar) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharLoc, out var charLoc) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharSpace, out var charSpace) ||
            !TryReadLongAt(fontAddress, GraphicsLayouts.TextFontCharKern, out var charKern))
        {
            return false;
        }

        // Each table carries one extra entry for the default glyph used when
        // a byte falls outside LoChar..HiChar.  Validate the whole declared
        // span before a proportional node enters the selectable list; the
        // renderer still validates each selected glyph's packed row bounds.
        if ((charLoc != 0 && (charLoc & 1u) != 0) ||
            (charSpace != 0 && (charSpace & 1u) != 0) ||
            (charKern != 0 && (charKern & 1u) != 0))
        {
            return false;
        }

        var entryCount = (uint)hiChar - loChar + 2u;
        if (charLoc != 0 && !TryProbeTable(charLoc, entryCount, sizeof(uint)))
            return false;
        if (charSpace != 0 && !TryProbeTable(charSpace, entryCount, sizeof(ushort)))
            return false;
        if (charKern != 0 && !TryProbeTable(charKern, entryCount, sizeof(ushort)))
            return false;

        return true;
    }

    private bool TryProbeTable(uint address, uint entries, int entryBytes)
    {
        if (address == 0 || entries == 0 || entryBytes <= 0)
            return false;

        var byteCount = (ulong)entries * (uint)entryBytes;
        if (byteCount > uint.MaxValue || address > uint.MaxValue - (uint)(byteCount - 1))
            return false;

        for (var offset = 0u; offset < (uint)byteCount; offset++)
        {
            if (!_memory.TryReadByte(address + offset, out _))
                return false;
        }

        return true;
    }

    private bool TryReadWord(uint address, out ushort value)
        => _memory.TryReadWord(address, out value);

    private bool TryReadWordAt(uint baseAddress, int offset, out ushort value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) && _memory.TryReadWord(address, out value);
    }

    private bool TryReadLongAt(uint baseAddress, int offset, out uint value)
    {
        value = 0;
        return TryAdd(baseAddress, (uint)offset, out var address) && _memory.TryReadLong(address, out value);
    }

    private bool TrySnapshotRange(
        uint address,
        uint byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (address == 0 ||
            byteCount == 0 ||
            byteCount > int.MaxValue ||
            address > uint.MaxValue - (byteCount - 1u))
        {
            return false;
        }

        original = new byte[(int)byteCount];
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!_memory.TryReadByte(address + offset, out original[(int)offset]))
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
            if (address <= uint.MaxValue - (uint)offset)
                _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
        }
    }

    private static bool TryAdd(uint address, uint offset, out uint result)
    {
        if (offset > uint.MaxValue - address)
        {
            result = 0;
            return false;
        }

        result = address + offset;
        return true;
    }
}
