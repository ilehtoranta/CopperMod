using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static class GraphicsTextOperations
{
    // Kickstart's algorithmic styling supports underline, bold and italic.
    // FSF_EXTENDED is a font-design attribute rather than a software transform.
    private const byte SupportedStyleBits = 0x07;
    private const byte StyleUnderlined = 0x01;
    private const byte StyleBold = 0x02;
    private const byte StyleItalic = 0x04;
    // FSF_COLORFONT selects the ColorTextFont extension.  Its tf_CharData
    // is a multi-plane color strike, not the single planar strike consumed by
    // this portable template path.  Keep that representation explicitly on
    // the native/provider side instead of silently rendering only plane zero.
    private const byte FontStyleColorFont = 0x40;
    private const byte FontFlagRevPath = 0x04;
    private const byte FontFlagProportional = 0x20;
    // ColorTextFont's low flag nibble selects the strike representation.  The
    // planar replacement only owns designer-colour and grey strikes; the
    // antialias form is a chunky/provider path (CyberGraphX in the classic
    // implementation) and must remain available to that owner.
    private const ushort ColorFontFlagColor = 1 << 0;
    private const ushort ColorFontFlagGrey = 1 << 1;
    private const ushort ColorFontFlagAntiAlias = 1 << 2;
    private const ulong PortableWorkLimit = int.MaxValue;

    internal static int SetFont(
        IGraphicsMemory memory,
        uint rastPort,
        uint fontAddress,
        IGraphicsFontBackend fonts)
    {
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return GraphicsRasterOperations.Failure;

        // The resident SetFont vector treats a NULL TextFont as a request for
        // GfxBase->DefaultFont.  Resolve that through the font backend so pure
        // host calls and the register adapter share the same guest contract;
        // a backend without a default owner remains available for native
        // provider fallback.
        if (fontAddress == 0 && !fonts.TryGetDefaultFont(out fontAddress))
            return GraphicsRasterOperations.Failure;

        if (fontAddress == 0 || (fontAddress & 1u) != 0 ||
            !fonts.TryGetMetrics(fontAddress, out var metrics))
            return GraphicsRasterOperations.Failure;

        // SetFont publishes the selected font and cached metrics, and clears
        // the effect of any previous soft styles.  TextFlags and TxSpacing
        // remain caller-owned; AlgoStyle is the one intervening RastPort byte
        // that this vector deliberately resets.
        if (!TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFont,
                sizeof(uint)) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAlgoStyle,
                sizeof(byte)) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextHeight,
                sizeof(ushort)) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextWidth,
                sizeof(ushort)) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextBaseline,
                sizeof(ushort)))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!TrySnapshotRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFont,
                sizeof(uint),
                out var originalFont) ||
            !TrySnapshotRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAlgoStyle,
                sizeof(byte),
                out var originalAlgorithmStyle) ||
            !TrySnapshotRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextHeight,
                sizeof(ushort),
                out var originalHeight) ||
            !TrySnapshotRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextWidth,
                sizeof(ushort),
                out var originalWidth) ||
            !TrySnapshotRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextBaseline,
                sizeof(ushort),
                out var originalBaseline))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!TryWriteRastPortLong(memory, rastPort, GraphicsLayouts.RastPortFont, fontAddress) ||
            !TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortAlgoStyle, 0) ||
            !TryWriteRastPortWord(memory, rastPort, GraphicsLayouts.RastPortTextHeight, metrics.Height) ||
            !TryWriteRastPortWord(memory, rastPort, GraphicsLayouts.RastPortTextWidth, metrics.Width) ||
            !TryWriteRastPortWord(memory, rastPort, GraphicsLayouts.RastPortTextBaseline, metrics.Baseline))
        {
            RestoreRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFont,
                originalFont);
            RestoreRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAlgoStyle,
                originalAlgorithmStyle);
            RestoreRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextHeight,
                originalHeight);
            RestoreRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextWidth,
                originalWidth);
            RestoreRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextBaseline,
                originalBaseline);
            return GraphicsRasterOperations.Failure;
        }

        return GraphicsRasterOperations.Success;
    }

    internal static int AskSoftStyle(
        IGraphicsMemory memory,
        uint rastPort,
        IGraphicsFontBackend fonts)
    {
        return TryAskSoftStyle(memory, rastPort, fonts, out var style)
            ? unchecked((int)style)
            : GraphicsRasterOperations.Failure;
    }

    /// <summary>
    /// Queries the style bits that are not intrinsic to the selected font.
    /// AskSoftStyle is a ULONG-returning vector, so <c>0xffffffff</c> is a
    /// valid result when a font has no intrinsic style.  Keep the success
    /// status separate from the legacy signed helper's -1 sentinel so callers
    /// such as GetRPAttrsA do not reject that valid value.
    /// </summary>
    internal static bool TryAskSoftStyle(
        IGraphicsMemory memory,
        uint rastPort,
        IGraphicsFontBackend fonts,
        out uint style)
    {
        style = 0;
        if (rastPort == 0 || (rastPort & 1u) != 0 ||
            !TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFont,
                out var fontAddress))
        {
            return false;
        }

        // A readable null Font is a valid empty RastPort state.  The classic
        // query returns no available algorithmic styles in that case; only a
        // non-null pointer that cannot be decoded remains unclaimed for a
        // native/provider implementation.
        if (fontAddress == 0)
            return true;

        if ((fontAddress & 1u) != 0 ||
            !fonts.TryGetStyle(fontAddress, out var intrinsicStyle))
        {
            return false;
        }

        // The classic result is the complement of the intrinsic TextFont
        // style byte.  That deliberately leaves undefined/future bits set;
        // SetSoftStyle will still publish only the byte-sized AlgoStyle field
        // and Text will apply the three transforms it knows how to render.
        style = unchecked((uint)~intrinsicStyle);
        return true;
    }

    internal static int SetSoftStyle(
        IGraphicsMemory memory,
        uint rastPort,
        uint style,
        uint enable,
        IGraphicsFontBackend fonts)
    {
        if (!TryReadFontAddress(memory, rastPort, out var fontAddress) ||
            !fonts.TryGetStyle(fontAddress, out var intrinsicStyle) ||
            !TryReadRastPortByte(memory, rastPort, GraphicsLayouts.RastPortAlgoStyle, out var algorithmStyle))
        {
            return GraphicsRasterOperations.Failure;
        }

        // AskSoftStyle/SetSoftStyle operate on the complete intrinsic style
        // byte, not just the three transforms currently rendered by Text.
        // Intrinsic bits cannot be changed; all other bits in the enable mask
        // may be requested, while the guest AlgoStyle storage remains one
        // byte wide.  Return the resulting byte state together with the
        // intrinsic style exactly as the classic vector does.
        var intrinsic = (uint)intrinsicStyle;
        var realEnable = enable & ~intrinsic;
        var nextAlgorithmStyle = (byte)(
            ((~realEnable & algorithmStyle) | (realEnable & style)) & byte.MaxValue);
        if (!TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortAlgoStyle, nextAlgorithmStyle))
            return GraphicsRasterOperations.Failure;

        return unchecked((int)((byte)(nextAlgorithmStyle | intrinsicStyle)));
    }

    internal static int Text(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAddress,
        uint count,
        IGraphicsFontBackend fonts,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        GraphicsRasterOperations.VisibilityScratch? visibilityScratch = null)
    {
        if (pixelVisible is null)
        {
            return TextCore(
                memory,
                rastPort,
                textAddress,
                count,
                fonts,
                null,
                snapshotAddressBuffer,
                snapshotValueBuffer);
        }

        var visibility = visibilityScratch ?? new GraphicsRasterOperations.VisibilityScratch();
        if (!visibility.TryAcquire(pixelVisible, out var stablePixelVisible))
            return GraphicsRasterOperations.Failure;

        try
        {
            return TextCore(
                memory,
                rastPort,
                textAddress,
                count,
                fonts,
                stablePixelVisible,
                snapshotAddressBuffer,
                snapshotValueBuffer);
        }
        finally
        {
            visibility.Release();
        }
    }

    private static int TextCore(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAddress,
        uint count,
        IGraphicsFontBackend fonts,
        Func<int, int, bool>? pixelVisible,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer)
    {
        if (count == 0)
        {
            // An empty standard-planar Text call is otherwise a no-read
            // success.  A readable nonzero Layer is the one exception: the
            // pure/core entry has no clipping or damage predicate, so leave
            // even an empty layered request behind the explicit provider or
            // native boundary.  An unreadable layer field deliberately keeps
            // the classic no-read success available to the register owner.
            if (TryReadRastPortLong(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortLayer,
                    out var emptyLayer) &&
                emptyLayer != 0 &&
                pixelVisible is null)
            {
                return GraphicsRasterOperations.Failure;
            }

            return GraphicsRasterOperations.Success;
        }

        // The portable path still decodes every character to advance the
        // guest cursor when the effective write mask is zero.  Keep that
        // cursor-only pass on the same bounded host-work contract as the
        // metric queries and destination preflight; otherwise an internal
        // widened count could make a no-op Text call walk an unbounded number
        // of guest bytes before it reaches the first raster write.
        if ((ulong)count > PortableWorkLimit)
            return GraphicsRasterOperations.Failure;

        // Text is a raster operation, so a direct/core call must respect the
        // same explicit layer-provider boundary as Draw and the planar pixel
        // primitives.  CopperStart normally routes layered Text before this
        // method; the guard prevents a future CopperSharp68k caller from
        // painting a layer's backing bitmap without clipping/damage policy.
        if (!GraphicsRasterOperations.TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return GraphicsRasterOperations.Failure;
        }

        // RPF_NO_PENS transfers colour ownership to the native/provider
        // renderer. Reject that boundary before reading the public draw mode,
        // mask, font, or caller text stream; those fields are not portable
        // inputs for a private-colour Text operation and may be unavailable
        // while the provider owns the RastPort.
        if (!GraphicsRasterOperations.TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var oldFlags) ||
            (oldFlags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        if (textAddress == 0 ||
            !TryValidateTextSpan(textAddress, count, direction: 1) ||
            !TryReadRastPortLong(memory, rastPort, GraphicsLayouts.RastPortFont, out var fontAddress) ||
            fontAddress == 0 || (fontAddress & 1u) != 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        // The public mask plus bitmap metadata are the only draw-state bytes
        // needed to choose the metric-only cursor path. Keep
        // ColorTextFont/provider admission ahead of the remaining draw-state
        // reads for ordinary (nonzero-mask) text, preserving the classic
        // boundary when those bytes are owned by a native/provider renderer.
        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var requestedWriteMask))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
            return GraphicsRasterOperations.Failure;

        var admissionMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            requestedWriteMask);

        GraphicsFontMetrics metrics;
        if ((admissionMask == 0
                ? !fonts.TryGetMetricMetrics(fontAddress, out metrics)
                : !fonts.TryGetMetrics(fontAddress, out metrics)))
        {
            return GraphicsRasterOperations.Failure;
        }

        var isColorFont = (metrics.Style & FontStyleColorFont) != 0;
        IGraphicsColorFontBackend? colorFonts = null;
        var colorFont = default(GraphicsColorFontInfo);
        if (isColorFont && admissionMask != 0 && pixelVisible is null)
        {
            // Preserve the native/provider decision ordering for direct
            // ColorTextFont calls: an unsupported strike must decline before
            // unrelated public draw-state bytes are claimed.
            colorFonts = fonts as IGraphicsColorFontBackend;
            if (colorFonts is null ||
                !colorFonts.TryGetColorFont(fontAddress, out colorFont))
            {
                return GraphicsRasterOperations.Failure;
            }

            var colorStyle = (ushort)(colorFont.Flags & 0x000F);
            if ((colorStyle & ColorFontFlagAntiAlias) != 0 ||
                (colorStyle != ColorFontFlagColor &&
                 colorStyle != ColorFontFlagGrey))
            {
                return GraphicsRasterOperations.Failure;
            }
        }
        if (!TryReadRastPortByte(memory, rastPort, GraphicsLayouts.RastPortDrawMode, out var drawMode) ||
            !TryReadRastPortByte(memory, rastPort, GraphicsLayouts.RastPortMask, out var writeMask) ||
            !TryReadRastPortByte(memory, rastPort, GraphicsLayouts.RastPortAlgoStyle, out var algorithmStyle) ||
            !TryReadRastPortSignedWord(memory, rastPort, GraphicsLayouts.RastPortCurrentX, out var x) ||
            !TryReadRastPortSignedWord(memory, rastPort, GraphicsLayouts.RastPortCurrentY, out var baseline) ||
            !TryReadRastPortWord(memory, rastPort, GraphicsLayouts.RastPortTextBaseline, out var textBaseline) ||
            !TryReadRastPortSignedWord(memory, rastPort, GraphicsLayouts.RastPortTextSpacing, out var textSpacing) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out var oldPatternCount))
        {
            return GraphicsRasterOperations.Failure;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask);

        // The planar writer ignores its colour argument in COMPLEMENT mode:
        // every selected destination plane is toggled from its current bit.
        // Resolve public pens lazily at the first visible template cell that
        // needs each source. This keeps an all-zero JAM1 glyph, an entirely
        // clipped run, and a zero-effective-mask cursor update from claiming
        // unrelated provider-owned pen bytes. Layered ColorTextFont mapping
        // is admitted only after a visible destination survives geometry
        // classification; direct calls retain the native ordering.
        var complementWholeText = (drawMode & 3) == 3;
        byte foreground = 0;
        var foregroundLoaded = effectiveMask == 0 ||
            (drawMode & 2) != 0;
        byte background = 0;
        var backgroundLoaded = effectiveMask == 0 ||
            (drawMode & 1) == 0 ||
            (drawMode & 2) != 0;

        bool TryEnsureForeground()
        {
            if (foregroundLoaded)
                return true;

            if (!TryReadRastPortByte(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortFgPen,
                    out foreground))
            {
                return false;
            }

            foregroundLoaded = true;
            return true;
        }

        bool TryEnsureBackground()
        {
            if (backgroundLoaded)
                return true;

            if (!TryReadRastPortByte(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortBgPen,
                    out background))
            {
                return false;
            }

            backgroundLoaded = true;
            return true;
        }

        writeMask = effectiveMask;

        // Text is a single guest operation.  Validate the complete character
        // stream and every decoded glyph strike before touching the
        // destination bitmap; otherwise a malformed final character could
        // leave earlier glyphs visible even though the vector reports failure.
        // A zero RastPort mask still consumes the text stream and advances
        // the cursor, but cannot publish any glyph/background pixels.  Keep
        // the source/font decode contract while skipping strike destination
        // planning below; the mask-aware destination preflight already
        // treats this as a successful no-op.
        var reversePath = (metrics.Flags & FontFlagRevPath) != 0;
        var variableWidthFont = UsesVariableWidthMetrics(metrics);
        var usesGlyphKerning = metrics.HasCharKern;
        var softStyle = (byte)(algorithmStyle & SupportedStyleBits);
        // The guest TextFont owns the exact smear width. A zero
        // tf_BoldSmear is a valid (albeit visually inert) bold request and
        // must not be widened by the host renderer; TextExtent uses the same
        // declared field when publishing the styled envelope.
        var boldSmear = (softStyle & StyleBold) != 0
            ? (int)metrics.BoldSmear
            : 0;
        var italic = (softStyle & StyleItalic) != 0;
        var underline = (softStyle & StyleUnderlined) != 0;
        var underlineStart = 0;
        var underlineEnd = -1;
        var underlineY = 0;
        var underlineVisible = false;
        if (underline && writeMask != 0 && !complementWholeText && !TryGetUnderlineRange(
                memory,
                textAddress,
                count,
                fontAddress,
                x,
                textSpacing,
                reversePath,
                baseline,
                textBaseline,
                metrics,
                algorithmStyle,
                fonts,
                usesGlyphKerning,
                !metrics.HasCharSpace,
                out underlineStart,
                out underlineEnd,
                out underlineY,
                out underlineVisible))
            return GraphicsRasterOperations.Failure;

        // Text is implemented as a template blit on the classic planar
        // path.  In JAM2 that template's zero bits cover the complete
        // TextExtent rectangle, including inter-character spacing and the
        // font rows that contain no glyph strike.  Keep the same envelope in
        // the portable renderer so BPen is visible in those cells as well.
        var fillTextBackground = writeMask != 0 && (drawMode & 1) != 0;
        var backgroundStartX = 0;
        var backgroundEndX = -1;
        var backgroundTopY = 0;
        var backgroundBottomY = -1;
        if (fillTextBackground)
        {
            if (!TryMeasureText(memory, rastPort, textAddress, count, fonts, out var textExtent))
            {
                return GraphicsRasterOperations.Failure;
            }

            // A zero-width/zero-advance glyph has no template rectangle.  It
            // is still a successful Text call; simply leave the background
            // envelope empty while preserving the cursor update below.
            if (textExtent.MaxX >= textExtent.MinX &&
                (!TryAddInt(x, textExtent.MinX, out backgroundStartX) ||
                 !TryAddInt(x, textExtent.MaxX, out backgroundEndX)))
            {
                return GraphicsRasterOperations.Failure;
            }

            if (metrics.Height != 0 &&
                (!TryAddInt(baseline, -(int)textBaseline, out backgroundTopY) ||
                 !TryAddInt(backgroundTopY, metrics.Height - 1, out backgroundBottomY)))
            {
                return GraphicsRasterOperations.Failure;
            }
        }

        // A provider can hide the complete destination while the guest Text
        // operation still has to consume its characters and publish the
        // cursor.  Classify only the geometric destination envelope first so
        // a hidden ColorTextFont does not claim its provider-owned strike
        // metadata, pens, or planar storage.
        var skipColorRaster = false;
        if (isColorFont && pixelVisible is not null)
        {
            if (!TryPreflightTextDestination(
                    memory,
                    bitmap,
                    textAddress,
                    count,
                    fontAddress,
                    x,
                    baseline,
                    textBaseline,
                    textSpacing,
                    drawMode,
                    writeMask,
                    boldSmear,
                    italic,
                    underlineVisible,
                    reversePath,
                    variableWidthFont,
                    metrics.Baseline,
                    metrics.Height,
                    underlineStart,
                    underlineEnd,
                    underlineY,
                    fillTextBackground,
                    backgroundStartX,
                    backgroundEndX,
                    backgroundTopY,
                    backgroundBottomY,
                    fonts,
                    colorFonts: null,
                    colorFont: default,
                    isColorFont: true,
                    foregroundPen: 0,
                    usesGlyphKerning,
                    !metrics.HasCharSpace,
                    metrics.Width,
                    pixelVisible,
                    sourceAdmissionOnly: true,
                    out var hasVisibleDestination))
            {
                return GraphicsRasterOperations.Failure;
            }

            skipColorRaster = !hasVisibleDestination;
        }

        if (isColorFont && !skipColorRaster && writeMask != 0 && colorFonts is null)
        {
            // Resolve the color-font representation only after a visible
            // destination survives provider admission.  Antialias and
            // unsupported strikes remain with the native/provider owner.
            colorFonts = fonts as IGraphicsColorFontBackend;
            if (colorFonts is null ||
                !colorFonts.TryGetColorFont(fontAddress, out colorFont))
            {
                return GraphicsRasterOperations.Failure;
            }

            var colorStyle = (ushort)(colorFont.Flags & 0x000F);
            if ((colorStyle & ColorFontFlagAntiAlias) != 0 ||
                (colorStyle != ColorFontFlagColor &&
                 colorStyle != ColorFontFlagGrey))
            {
                return GraphicsRasterOperations.Failure;
            }

        }

        if (isColorFont && !skipColorRaster && writeMask != 0 &&
            !complementWholeText && !TryEnsureForeground())
        {
            return GraphicsRasterOperations.Failure;
        }

        // Text is a single guest operation. Validate every decoded glyph
        // strike before touching the destination bitmap; a malformed final
        // character must not leave earlier glyphs visible. A zero mask, or a
        // fully hidden ColorTextFont run, still consumes the stream below.
        if (writeMask != 0 && !skipColorRaster && !complementWholeText &&
            ((isColorFont && pixelVisible is null)
                ? !TryPreflightColorText(
                    memory,
                    textAddress,
                    count,
                    fontAddress,
                    fonts,
                    colorFont)
                : !TryPreflightText(memory, textAddress, count, fontAddress, fonts)))
        {
            return GraphicsRasterOperations.Failure;
        }

        // SetBitmapPixel guards one destination pixel at a time.  Text spans
        // several rows and glyphs, so a later row can still cross out of a
        // malformed plane after earlier rows were published.  Probe the full
        // destination plan (including the final current-X store) before the
        // first raster write to preserve the vector's all-or-nothing failure
        // behavior for guest memory faults.
        var textSnapshotAddresses = snapshotAddressBuffer ?? new List<uint>();
        var textSnapshotValues = snapshotValueBuffer ?? new List<byte>();
        textSnapshotAddresses.Clear();
        textSnapshotValues.Clear();
        if (!TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                2) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                2) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                1) ||
             (!skipColorRaster && !TryPreflightTextDestination(
                memory,
                bitmap,
                textAddress,
                count,
                fontAddress,
                x,
                baseline,
                textBaseline,
                textSpacing,
                drawMode,
                writeMask,
                boldSmear,
                italic,
                underlineVisible,
                reversePath,
                variableWidthFont,
                metrics.Baseline,
                metrics.Height,
                underlineStart,
                underlineEnd,
                underlineY,
                fillTextBackground,
                backgroundStartX,
                backgroundEndX,
                backgroundTopY,
                backgroundBottomY,
                fonts,
                colorFonts,
                colorFont,
                isColorFont,
                foreground,
                usesGlyphKerning,
                !metrics.HasCharSpace,
                metrics.Width,
                 pixelVisible,
                 sourceAdmissionOnly: false,
                 out _,
                 textSnapshotAddresses,
                 textSnapshotValues)))
        {
            return GraphicsRasterOperations.Failure;
        }

        bool TrySetVisiblePixel(
            int pixelX,
            int pixelY,
            int color,
            bool visibilityChecked = false)
        {
            // Match Draw's bitmap-first admission: an off-raster glyph,
            // smear, or underline sample remains part of text traversal but
            // cannot ask a layer provider to classify an impossible
            // destination.
            if (pixelX < 0 || pixelY < 0 ||
                pixelX >= bitmap.Width || pixelY >= bitmap.Rows)
            {
                return true;
            }

            if (!visibilityChecked &&
                pixelVisible is not null &&
                !pixelVisible(pixelX, pixelY))
                return true;

            return writeMask == 0 || GraphicsRasterOperations.SetBitmapPixel(
                memory,
                bitmap,
                pixelX,
                pixelY,
                color,
                drawMode,
                writeMask);
        }

        bool IsVisibleDestination(int pixelX, int pixelY)
        {
            return pixelX >= 0 && pixelY >= 0 &&
                pixelX < bitmap.Width && pixelY < bitmap.Rows &&
                (pixelVisible?.Invoke(pixelX, pixelY) ?? true);
        }

        bool TryPaintWithPen(
            int pixelX,
            int pixelY,
            bool useForegroundPen,
            int? explicitColor = null)
        {
            if (!IsVisibleDestination(pixelX, pixelY))
                return true;

            var penReady = useForegroundPen
                ? TryEnsureForeground()
                : TryEnsureBackground();
            var color = explicitColor ??
                (useForegroundPen ? foreground : background);
            return penReady &&
                TrySetVisiblePixel(
                    pixelX,
                    pixelY,
                    color,
                    visibilityChecked: true);
        }

        int FailText()
        {
            // The final cursor WORD and FRST_DOT Flags WORD may each accept
            // only a prefix before a guest-memory bridge rejects the
            // publication. Restore both fields byte-wise so the same failing
            // WORD operation cannot strand a half-advanced Text state.
            RestoreRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                new[] { (byte)((ushort)x >> 8), (byte)(ushort)x });
            _ = TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                oldPatternCount);
            RestoreRastPortField(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                new[] { (byte)(oldFlags >> 8), (byte)oldFlags });
            for (var snapshot = textSnapshotAddresses.Count - 1; snapshot >= 0; snapshot--)
                _ = memory.TryWriteByte(
                    textSnapshotAddresses[snapshot],
                    textSnapshotValues[snapshot]);
            return GraphicsRasterOperations.Failure;
        }

        if (fillTextBackground)
        {
            // INVERSVID swaps the template phases, so a zero source cell in
            // the untouched spacing envelope receives APen instead of BPen.
            var inverseVideoBackground = (drawMode & 4) != 0;
            for (var y = backgroundTopY; y <= backgroundBottomY; y++)
            {
                for (var pixelX = backgroundStartX; pixelX <= backgroundEndX; pixelX++)
                {
                    var painted = complementWholeText
                        ? TrySetVisiblePixel(pixelX, y, 0)
                        : TryPaintWithPen(
                            pixelX,
                            y,
                            inverseVideoBackground);
                    if (!painted)
                    {
                        return FailText();
                    }

                    if (pixelX == int.MaxValue)
                        return FailText();
                }

                if (y == int.MaxValue)
                    return FailText();
            }
        }

        var cursorX = (int)x;
        var cursorY = baseline - textBaseline;
        var leadingNegativeKerning = 0;
        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !(writeMask == 0 || skipColorRaster || complementWholeText
                    ? fonts.TryGetMetricGlyph(fontAddress, character, out var glyph)
                    : fonts.TryGetGlyph(fontAddress, character, out glyph)))
            {
                return FailText();
            }

            if (!TryGetCellWidth(
                    glyph,
                    !metrics.HasCharSpace,
                    metrics.Width,
                    out var cellWidth))
                return FailText();

            var glyphKerning = usesGlyphKerning ? glyph.Kerning : 0;
            if (!TryAddInt(ref cursorX, glyphKerning))
                return FailText();
            if (index == 0 && glyphKerning < 0)
                leadingNegativeKerning = glyphKerning;
            if (writeMask != 0 && !complementWholeText && !skipColorRaster)
            {
                // The template raster is the complete font-height cell, not
                // only the rows occupied by this glyph's strike.  Empty rows
                // are still source-zero rows: JAM2 paints their background,
                // JAM1|INVERSVID paints them with APen, and
                // COMPLEMENT|INVERSVID complements them.  The full JAM2
                // background pass above already covers those rows, so this
                // loop only adds observable work for the inverse/complement
                // modes while retaining the same cell geometry as the
                // native BltTemplate path.
                for (var row = 0; row < metrics.Height; row++)
                {
                    var italicShift = italic
                        ? GetItalicShift(metrics.Baseline, row)
                        : 0;
                    for (var column = 0; column < cellWidth; column++)
                    {
                        var glyphColumn = GetGlyphColumnForCell(glyph, column, variableWidthFont);
                        if (!TryGetCellPixelX(
                                cursorX,
                                glyph,
                                column,
                                italicShift,
                                reversePath,
                                out var pixelX))
                        {
                            return FailText();
                        }

                        var pixelVisibleForSource = IsVisibleDestination(
                            pixelX,
                            cursorY + row);
                        if (pixelVisible is not null && !pixelVisibleForSource)
                            continue;

                        var set = false;
                        var sourceColor = foreground;
                        if (glyphColumn >= 0 && row < glyph.Height)
                        {
                            if (isColorFont)
                            {
                                if (!colorFont.TryGetPixel(
                                        memory,
                                        glyph,
                                        glyphColumn,
                                        row,
                                        foreground,
                                        out sourceColor,
                                        out set))
                                {
                                    return FailText();
                                }
                            }
                            else if (!glyph.TryIsSet(memory, glyphColumn, row, out set))
                            {
                                return FailText();
                            }
                        }

                        var patternBit = set;
                        if ((drawMode & 4) != 0)
                            patternBit = !patternBit;

                        if (!patternBit && (drawMode & 1) != 0)
                        {
                            if (!TryPaintWithPen(
                                    pixelX,
                                    cursorY + row,
                                    useForegroundPen: false))
                            {
                                return FailText();
                            }

                        }
                    }

                    // Draw foreground after JAM2 background so bold smearing
                    // cannot be erased by the background of the next column.
                    for (var column = 0; column < cellWidth; column++)
                    {
                        var glyphColumn = GetGlyphColumnForCell(glyph, column, variableWidthFont);
                        if (!TryGetCellPixelX(
                                cursorX,
                                glyph,
                                column,
                                italicShift,
                                reversePath,
                                out var pixelX))
                        {
                            return FailText();
                        }

                        var smearLimit = glyphColumn >= 0 ? boldSmear : 0;
                        var visibleSmear = false;
                        for (var smear = 0; smear <= smearLimit; smear++)
                        {
                            if (!TryAddInt(pixelX, smear, out var smearX))
                                return FailText();

                            if (IsVisibleDestination(smearX, cursorY + row))
                                visibleSmear = true;
                        }

                        if (pixelVisible is not null && !visibleSmear)
                            continue;

                        var set = false;
                        var sourceColor = foreground;
                        if (glyphColumn >= 0 && row < glyph.Height)
                        {
                            if (isColorFont)
                            {
                                if (!colorFont.TryGetPixel(
                                        memory,
                                        glyph,
                                        glyphColumn,
                                        row,
                                        foreground,
                                        out sourceColor,
                                        out set))
                                {
                                    return FailText();
                                }
                            }
                            else if (!glyph.TryIsSet(memory, glyphColumn, row, out set))
                            {
                                return FailText();
                            }
                        }

                        var patternBit = set;
                        if ((drawMode & 4) != 0)
                            patternBit = !patternBit;
                        if (!patternBit)
                            continue;

                        for (var smear = 0; smear <= smearLimit; smear++)
                        {
                            if (!TryAddInt(pixelX, smear, out var smearX))
                                return FailText();

                            if (!TryPaintWithPen(
                                    smearX,
                                    cursorY + row,
                                    useForegroundPen: true,
                                    explicitColor: isColorFont && (drawMode & 4) == 0
                                        ? sourceColor
                                        : null))
                            {
                                return FailText();
                            }

                        }
                    }
                }
            }

            if (!TryAddInt(ref cursorX, glyph.Advance))
                return FailText();
            if (!TryAddInt(ref cursorX, textSpacing))
                return FailText();
        }

        if (writeMask != 0 && underlineVisible && !complementWholeText)
        {
            for (var pixelX = underlineStart; pixelX <= underlineEnd; pixelX++)
            {
                if (!TryPaintWithPen(
                        pixelX,
                        underlineY,
                        useForegroundPen: true))
                {
                    return FailText();
                }

                if (pixelX == int.MaxValue)
                    return FailText();
            }
        }

        // TextLength/TextExtent deliberately omit a negative kerning offset
        // before the first glyph.  Text still uses that offset to place the
        // first strike, but the public cursor advance must match the metric
        // vectors so callers can chain the two operations without a drift.
        if (leadingNegativeKerning != 0 &&
            !TryAddInt(ref cursorX, -leadingNegativeKerning))
        {
            return FailText();
        }

        // The native Text vector clips the public current position to the
        // RastPort drawing boundary when a string runs past it.  Planar
        // RastPorts are rooted at x=0, so a run that finishes left of the
        // drawable area publishes zero rather than leaking a negative pen
        // position.  Retain the 16-bit coordinate contract even for a very
        // wide planar allocation.
        var maxCursorX = Math.Min(bitmap.Width, short.MaxValue);
        if (cursorX > maxCursorX)
            cursorX = maxCursorX;
        else if (cursorX < 0)
            cursorX = 0;

        if (!TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                unchecked((ushort)(short)cursorX)) ||
            !TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                15) ||
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                (ushort)(oldFlags | GraphicsLayouts.RastPortFirstDot)))
        {
            return FailText();
        }

        return GraphicsRasterOperations.Success;
    }

    internal static int TextLength(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAddress,
        uint count,
        IGraphicsFontBackend fonts)
    {
        return TryTextLength(memory, rastPort, textAddress, count, fonts, out var length)
            ? length
            : GraphicsRasterOperations.Failure;
    }

    internal static bool TryTextLength(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAddress,
        uint count,
        IGraphicsFontBackend fonts,
        out int length)
    {
        length = 0;
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        // The internal count is widened for host-side callers, but the
        // portable metric loop is a bounded walk.  Decline an aggregate count that
        // cannot be represented by the bounded work contract before any
        // text bytes or glyphs are decoded.
        if ((ulong)count > PortableWorkLimit)
            return false;

        if (count == 0)
        {
            // The decoded TextFont path has no characters to inspect and
            // therefore no reason to consume the cached TxWidth/TxSpacing
            // words in the RastPort.  Native TextLength still reads the
            // selected TextFont header, so distinguish a readable decoded
            // font from the compatibility NULL-font cache path before
            // returning the empty result.  This keeps a stale or faulted
            // cache from stealing an otherwise valid zero-length query.
            if (!TryReadRastPortLong(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortFont,
                    out var emptyFontAddress))
            {
                return false;
            }

            if (emptyFontAddress != 0)
            {
                if ((emptyFontAddress & 1u) != 0 ||
                !fonts.TryGetMetricMetrics(emptyFontAddress, out _))
                {
                    return false;
                }

                length = 0;
                return true;
            }

            return GraphicsRasterOperations.TryTextLength(memory, rastPort, count, out length);
        }

        if (textAddress == 0)
            return false;

        // The guest address space is still a 32-bit interval. Reject a string
        // whose final byte would wrap
        // before asking the selected font backend to decode any glyph.
        if (!TryValidateTextSpan(textAddress, count, direction: 1))
            return false;

        if (!TryReadRastPortLong(memory, rastPort, GraphicsLayouts.RastPortFont, out var fontAddress) ||
            fontAddress == 0 || (fontAddress & 1u) != 0 ||
            !TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextSpacing,
                out var textSpacing))
        {
            return false;
        }

        if (!fonts.TryGetMetricMetrics(fontAddress, out var metrics))
            return false;

        var usesGlyphKerning = metrics.HasCharKern;
        long total = 0;
        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !fonts.TryGetMetricGlyph(fontAddress, character, out var glyph))
            {
                return false;
            }

            // Fixed-cell fonts without CharSpace/CharKern use their nominal
            // cell advance.  Variable-width fonts retain the per-glyph
            // kerning and advance movement, excluding only a leading
            // negative kern from the public TextLength result.
            if (usesGlyphKerning && (index != 0 || glyph.Kerning >= 0))
                total += glyph.Kerning;

            if (!TryGetMetricAdvance(metrics, glyph, out var glyphAdvance))
                return false;

            total += glyphAdvance;
            total += textSpacing;
            if (total < int.MinValue || total > int.MaxValue)
                return false;
        }

        length = (int)total;
        return true;
    }

    internal static int TextExtent(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAddress,
        uint count,
        uint extentAddress,
        IGraphicsFontBackend fonts)
    {
        if (!TryValidateTextExtentDestination(memory, extentAddress) ||
            !TryMeasureText(memory, rastPort, textAddress, count, fonts, out var extent) ||
            !WriteTextExtent(memory, extentAddress, extent))
        {
            return GraphicsRasterOperations.Failure;
        }

        return GraphicsRasterOperations.Success;
    }

    internal static int FontExtent(
        IGraphicsMemory memory,
        uint fontAddress,
        uint extentAddress,
        IGraphicsFontBackend fonts)
    {
        // FontExtent publishes the same packed 12-byte TextExtent envelope as
        // TextExtent/TextFit.  Claim the result destination before asking a
        // font provider for metrics; a truncated or odd guest result must stay
        // available to the native/provider implementation without decoding or
        // walking the selected font first.
        if (fontAddress == 0 || (fontAddress & 1u) != 0 ||
            !TryValidateTextExtentDestination(memory, extentAddress) ||
            !fonts.TryGetMetricMetrics(fontAddress, out var metrics))
        {
            return GraphicsRasterOperations.Failure;
        }

        var reversePath = (metrics.Flags & FontFlagRevPath) != 0;
        // The classic routine derives width from the per-character
        // CharSpace+CharKern movements.  Fixed-cell fonts reach the same
        // nominal width through their glyph advances; seeding with tf_XSize
        // would incorrectly retain a width larger than every actual entry.
        var width = 0;
        var sawGlyph = false;
        var minX = 0;
        var maxX = -1;

        bool AccumulateGlyph(GraphicsGlyph glyph)
        {
            var glyphKerning = metrics.HasCharKern ? glyph.Kerning : 0;
            var glyphMinX = glyphKerning;
            var glyphMaxX = glyphKerning + glyph.Width - 1;
            if (!sawGlyph || glyphMinX < minX)
                minX = glyphMinX;
            if (!sawGlyph || glyphMaxX > maxX)
                maxX = glyphMaxX;

            // FontExtent's width uses the complete one-character movement
            // (CharSpace plus CharKern), then ignores only movement in the
            // wrong overall direction. The kerning still contributes to the
            // selected-direction total before the zero clamp.
            if (!TryGetMetricAdvance(metrics, glyph, out var glyphAdvance))
                return false;

            var candidateMovement = (long)glyphAdvance + glyphKerning;
            if (candidateMovement < int.MinValue || candidateMovement > int.MaxValue)
                return false;

            var candidateWidth = (int)candidateMovement;
            candidateWidth = reversePath
                ? Math.Min(0, candidateWidth)
                : Math.Max(0, candidateWidth);
            width = reversePath
                ? Math.Min(width, candidateWidth)
                : Math.Max(width, candidateWidth);
            sawGlyph = true;
            return true;
        }

        // FontExtent describes the declared LoChar..HiChar range.  The extra
        // CharLoc/CharSpace/CharKern slot is only the fallback used by Text
        // for bytes outside that range; it is not one of the font's declared
        // characters and must not widen the font-wide envelope.
        for (var character = (int)metrics.LoChar; character <= metrics.HiChar; character++)
        {
            if (!fonts.TryGetMetricGlyph(fontAddress, (byte)character, out var glyph))
            {
                // FontExtent describes the complete declared TextFont
                // range. A missing glyph inside that range is therefore a
                // malformed/undecodable font, not an optional sparse entry;
                // silently skipping it could publish a width derived from a
                // different subset and steal the call from native/provider
                // ownership.
                return GraphicsRasterOperations.Failure;
            }

            if (!AccumulateGlyph(glyph))
                return GraphicsRasterOperations.Failure;
        }

        if (!sawGlyph)
            return GraphicsRasterOperations.Failure;

        var extent = new TextMeasure(
            width,
            metrics.Height,
            minX,
            -metrics.Baseline,
            maxX,
            metrics.Height - metrics.Baseline - 1);
        return WriteTextExtent(memory, extentAddress, extent)
            ? GraphicsRasterOperations.Success
            : GraphicsRasterOperations.Failure;
    }

    internal static int TextFit(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAddress,
        uint count,
        uint extentAddress,
        uint constrainingExtentAddress,
        int direction,
        uint constrainingWidth,
        uint constrainingHeight,
        IGraphicsFontBackend fonts)
    {
        if (!TryValidateTextExtentDestination(memory, extentAddress))
            return GraphicsRasterOperations.Failure;

        short textSpacing = 0;
        byte algorithmStyle = 0;
        if (extentAddress == 0 || (direction != 1 && direction != -1) ||
            (count != 0 && textAddress == 0) ||
            !TryValidateTextSpan(textAddress, count, direction) ||
            rastPort == 0 || (rastPort & 1u) != 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        // The classic zero-length path only publishes an empty TextExtent.
        // It does not dereference rp->Font (nor spacing/style or an optional
        // constraining extent), so an otherwise valid request remains
        // claimable even when the selected font belongs to a provider or is
        // temporarily unreadable.  Keep this boundary before the decoded
        // font lookup; non-empty calls still require the complete font
        // metrics contract below.
        if (count == 0)
        {
            return WriteTextExtent(memory, extentAddress, default)
                ? GraphicsRasterOperations.Success
                : GraphicsRasterOperations.Failure;
        }

        if (!TryReadFontAddress(memory, rastPort, out var fontAddress) ||
            !fonts.TryGetMetricMetrics(fontAddress, out var metrics))
        {
            return GraphicsRasterOperations.Failure;
        }

        // The alternate dimensions are used only when no explicit
        // TextExtent is supplied.  Kickstart treats the two forms as
        // alternative constraint descriptions; when an extent is present,
        // its origin-relative rectangle is the complete constraint and the
        // width/height arguments are ignored.  Zero is still a valid
        // zero-sized alternate constraint when the extent pointer is NULL.
        var constraint = default(TextMeasure);
        if (count != 0 &&
            constrainingExtentAddress != 0 &&
            !TryReadTextExtent(memory, constrainingExtentAddress, out constraint))
        {
            return GraphicsRasterOperations.Failure;
        }

        // The native TextFit path rejects an explicit box whose vertical
        // envelope cannot contain the selected font before it walks the
        // string.  Keep that short-circuit ahead of glyph decoding: a
        // malformed or provider-owned character stream must not turn a
        // valid zero-fit result into an unclaimed call when the box is
        // already too short by construction.
        if (count != 0 &&
            constrainingExtentAddress != 0 &&
            (constraint.MinY > -metrics.Baseline ||
             constraint.MaxY < metrics.Height - metrics.Baseline - 1 ||
             constraint.Height < metrics.Height))
        {
            return WriteTextExtent(memory, extentAddress, default)
                ? GraphicsRasterOperations.Success
                : GraphicsRasterOperations.Failure;
        }

        // The alternate rendering-box dimensions are ULONGs at the 68k
        // vector boundary, but Kickstart defines their usable range as
        // 0..32767. The range applies only when the alternate form is in
        // use; values in these registers are ignored with an explicit
        // TextExtent.
        if (count != 0 && constrainingExtentAddress == 0 &&
            (constrainingWidth > short.MaxValue ||
             constrainingHeight > short.MaxValue))
        {
            return GraphicsRasterOperations.Failure;
        }

        // Kickstart rejects a rendering box shorter than the selected font
        // before it walks the character stream. Keep this early no-fit
        // boundary for either active constraint form: a malformed glyph or
        // unreadable text byte cannot turn an otherwise valid zero-fit into
        // an unclaimed call.
        if (count != 0 &&
            ((constrainingExtentAddress == 0 &&
              constrainingHeight < metrics.Height) ||
             (constrainingExtentAddress != 0 &&
              constraint.Height < metrics.Height)))
        {
            return WriteTextExtent(memory, extentAddress, default)
                ? GraphicsRasterOperations.Success
                : GraphicsRasterOperations.Failure;
        }

        // TextFit's no-fit height shortcut above intentionally remains
        // authoritative.  Every other non-empty query must fit the same
        // bounded character-walk contract as TextLength/TextExtent.
        if ((ulong)count > PortableWorkLimit)
            return GraphicsRasterOperations.Failure;

        if (!TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextSpacing,
                out textSpacing) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAlgoStyle,
                out algorithmStyle))
        {
            return GraphicsRasterOperations.Failure;
        }

        long cursor = 0;
        var width = 0;
        long minX = 0;
        long maxX = -1;
        var minY = -metrics.Baseline;
        var maxY = metrics.Height - metrics.Baseline - 1;
        var fitted = 0u;
        var first = true;
        var leadingKerning = 0;
        var pathDirection = 0;
        var variableWidthFont = UsesVariableWidthMetrics(metrics);
        var reversePath = (metrics.Flags & FontFlagRevPath) != 0;
        var usesGlyphKerning = metrics.HasCharKern;

        for (var index = 0u; index < count; index++)
        {
            if (!TryGetDirectedAddress(textAddress, index, direction, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !fonts.TryGetMetricGlyph(fontAddress, character, out var glyph))
            {
                return GraphicsRasterOperations.Failure;
            }

            // TextFit cannot describe a string whose inter-character motion
            // stalls or reverses.  Kickstart reports an empty fit in that
            // case, even if an intermediate prefix would fit the supplied
            // rectangle.  Check the effective step for every boundary so
            // proportional glyphs cannot change the path direction halfway
            // through the string.
            // The path guard follows the same cursor movement that the
            // renderer and TextExtent use: character kerning is applied
            // before the glyph advance and TxSpacing. Ignoring CharKern
            // here could claim a fit when kerning stalls or reverses the
            // actual cursor path.
            if (!TryGetMetricAdvance(metrics, glyph, out var glyphAdvance))
            {
                return GraphicsRasterOperations.Failure;
            }

            var step = (usesGlyphKerning ? (long)glyph.Kerning : 0L) +
                       glyphAdvance +
                       textSpacing;
            var stepDirection = Math.Sign(step);
            if (stepDirection == 0 ||
                (pathDirection != 0 && stepDirection != pathDirection))
            {
                return WriteTextExtent(memory, extentAddress, default)
                    ? GraphicsRasterOperations.Success
                    : GraphicsRasterOperations.Failure;
            }

            pathDirection = stepDirection;

            // The candidate envelope is accumulated speculatively.  If this
            // character is the first one that does not fit, TextFit must
            // publish the accepted prefix, not the rejected glyph's bounds.
            // Keep the prior prefix snapshot beside the cursor/width state;
            // the latter are only committed after Fits succeeds, while the
            // horizontal bounds are updated incrementally below.
            var prefixMinX = minX;
            var prefixMaxX = maxX;

            if (usesGlyphKerning)
                cursor += glyph.Kerning;
            if (!FitsInt(cursor))
                return GraphicsRasterOperations.Failure;
            if (usesGlyphKerning && first && glyph.Kerning < 0)
                leadingKerning = glyph.Kerning;
            if (!TryGetTextCellEnvelope(
                    glyph,
                    cursor,
                    reversePath,
                    variableWidthFont,
                    out var glyphMinX,
                    out var glyphMaxX))
                return GraphicsRasterOperations.Failure;
            // TextExtent/TextFit start with the current pen origin in the
            // horizontal envelope.  A positive first-character kern moves
            // the strike to the right, but it must not erase that origin
            // from the published rectangle.
            if (glyphMinX < minX)
                minX = glyphMinX;
            if (glyphMaxX > maxX)
                maxX = glyphMaxX;

            var advanceCursor = cursor + glyphAdvance;
            if (!FitsInt(advanceCursor))
                return GraphicsRasterOperations.Failure;
            if (advanceCursor < minX)
                minX = advanceCursor;
            if (advanceCursor > maxX)
                maxX = advanceCursor;

            var candidateCursor = advanceCursor + textSpacing;
            if (!FitsInt(candidateCursor))
                return GraphicsRasterOperations.Failure;
            // Keep TextFit's candidate envelope identical to the
            // TextExtent one-character accumulation before testing the
            // constraining rectangle.
            if (candidateCursor < minX)
                minX = candidateCursor;
            if (candidateCursor > maxX)
                maxX = candidateCursor;

            var candidateWidth = candidateCursor;
            candidateWidth -= leadingKerning;
            var candidateMaxX = maxX - 1;
            var candidateMinX = minX;
            if (!TryApplyStyleExtent(
                    metrics,
                    algorithmStyle,
                    ref candidateMinX,
                    ref candidateMaxX) ||
                !FitsInt(candidateWidth))
                return GraphicsRasterOperations.Failure;

            var candidate = new TextMeasure(
                (int)candidateWidth,
                metrics.Height,
                (int)candidateMinX,
                minY,
                (int)candidateMaxX,
                maxY);
            if (!Fits(
                    candidate,
                    constraint,
                    constrainingExtentAddress != 0,
                    constrainingWidth,
                    constrainingHeight))
            {
                minX = prefixMinX;
                maxX = prefixMaxX;
                break;
            }

            cursor = candidateCursor;
            width = candidate.Width;
            fitted++;
            first = false;
        }

        if (fitted == 0)
        {
            if (!WriteTextExtent(memory, extentAddress, default))
                return GraphicsRasterOperations.Failure;

            return GraphicsRasterOperations.Success;
        }

        if (!FitsInt(maxX - 1))
            return GraphicsRasterOperations.Failure;

        var resultMinX = minX;
        var resultMaxX = maxX - 1;
        if (!TryApplyStyleExtent(metrics, algorithmStyle, ref resultMinX, ref resultMaxX))
            return GraphicsRasterOperations.Failure;

        var result = new TextMeasure(
            width,
            metrics.Height,
            (int)resultMinX,
            minY,
            (int)resultMaxX,
            maxY);
        return WriteTextExtent(memory, extentAddress, result)
            ? unchecked((int)fitted)
            : GraphicsRasterOperations.Failure;
    }

    private static bool TryMeasureText(
        IGraphicsMemory memory,
        uint rastPort,
        uint textAddress,
        uint count,
        IGraphicsFontBackend fonts,
        out TextMeasure extent)
    {
        extent = default;
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        // Keep the metric aggregation bounded before validating or walking
        // a potentially enormous character span.  Zero-count TextExtent
        // remains on its established empty-string path below.
        if ((ulong)count > PortableWorkLimit)
            return false;

        if (
            (count != 0 && textAddress == 0) ||
            !TryValidateTextSpan(textAddress, count, direction: 1) ||
            !TryReadFontAddress(memory, rastPort, out var fontAddress) ||
            !fonts.TryGetMetricMetrics(fontAddress, out var metrics) ||
            !TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextSpacing,
                out var textSpacing) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAlgoStyle,
                out var algorithmStyle))
        {
            return false;
        }

        // The classic metric path has two empty-string envelopes.  A fixed
        // cell font has no movement-cell samples, so its horizontal rectangle
        // is empty (MaxX = -1).  A proportional/kerning font initializes the
        // zero-width movement cell at x=0, then applies the same algorithmic
        // bold/italic expansion used for non-empty text.  Keep that split so
        // TextExtent remains compatible with the native TextFont tables
        // instead of silently treating every font as fixed-cell.
        if (count == 0)
        {
            var emptyVariableWidthFont = UsesVariableWidthMetrics(metrics);
            long emptyMinX = 0;
            long emptyMaxX = emptyVariableWidthFont ? 0 : -1;
            if (emptyVariableWidthFont &&
                !TryApplyStyleExtent(metrics, algorithmStyle, ref emptyMinX, ref emptyMaxX))
                return false;

            extent = new TextMeasure(
                0,
                metrics.Height,
                (int)emptyMinX,
                -metrics.Baseline,
                (int)emptyMaxX,
                metrics.Height - metrics.Baseline - 1);
            return true;
        }

        long cursor = 0;
        var width = 0;
        long minX = 0;
        long maxX = -1;
        var first = true;
        var leadingKerning = 0;
        var variableWidthFont = UsesVariableWidthMetrics(metrics);
        var reversePath = (metrics.Flags & FontFlagRevPath) != 0;
        var usesGlyphKerning = metrics.HasCharKern;
        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !fonts.TryGetMetricGlyph(fontAddress, character, out var glyph))
            {
                return false;
            }

            if (usesGlyphKerning)
                cursor += glyph.Kerning;
            if (!FitsInt(cursor))
                return false;
            if (usesGlyphKerning && first && glyph.Kerning < 0)
                leadingKerning = glyph.Kerning;
            if (!TryGetTextCellEnvelope(
                    glyph,
                    cursor,
                    reversePath,
                    variableWidthFont,
                    out var glyphMinX,
                    out var glyphMaxX))
                return false;
            // The proportional TextExtent envelope starts at the current
            // pen origin. Preserve that zero bound when the first glyph's
            // positive kerning shifts its strike to the right.
            if (glyphMinX < minX)
                minX = glyphMinX;
            if (glyphMaxX > maxX)
                maxX = glyphMaxX;

            if (!TryGetMetricAdvance(metrics, glyph, out var glyphAdvance))
                return false;

            cursor += glyphAdvance;
            if (!FitsInt(cursor))
                return false;
            // TextExtent includes the consumed movement cell as well as
            // the glyph strike. Signed CharSpace/TxSpacing can move that
            // cell back to the left, so maintain both bounds for fixed and
            // proportional fonts alike.
            if (cursor < minX)
                minX = cursor;
            if (cursor > maxX)
                maxX = cursor;
            cursor += textSpacing;
            if (!FitsInt(cursor))
                return false;
            if (cursor < minX)
                minX = cursor;
            if (cursor > maxX)
                maxX = cursor;
            width = (int)cursor;
            first = false;
        }

        var adjustedWidth = (long)width - leadingKerning;
        if (!FitsInt(adjustedWidth))
            return false;

        if (!FitsInt(maxX - 1))
            return false;

        var resultMinX = minX;
        var resultMaxX = maxX - 1;
        if (!TryApplyStyleExtent(metrics, algorithmStyle, ref resultMinX, ref resultMaxX))
            return false;

        extent = new TextMeasure(
            (int)adjustedWidth,
            metrics.Height,
            (int)resultMinX,
            -metrics.Baseline,
            (int)resultMaxX,
            metrics.Height - metrics.Baseline - 1);
        return true;
    }

    private static bool TryReadFontAddress(IGraphicsMemory memory, uint rastPort, out uint fontAddress)
    {
        fontAddress = 0;
        return rastPort != 0 && (rastPort & 1u) == 0 &&
            TryReadRastPortLong(memory, rastPort, GraphicsLayouts.RastPortFont, out fontAddress) &&
            fontAddress != 0 &&
            (fontAddress & 1u) == 0;
    }

    private static bool UsesVariableWidthMetrics(GraphicsFontMetrics metrics)
        => (metrics.Flags & FontFlagProportional) != 0 ||
            metrics.HasCharSpace ||
            metrics.HasCharKern;

    private static bool TryRastPortRange(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        int byteCount)
        => TryAddress(rastPort, offset, byteCount, out var address) &&
           TryProbeRange(memory, address, byteCount);

    private static bool TryReadRastPortByte(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out byte value)
    {
        value = 0;
        return TryAddress(rastPort, offset, sizeof(byte), out var address) &&
               memory.TryReadByte(address, out value);
    }

    private static bool TryReadRastPortWord(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out ushort value)
    {
        value = 0;
        return TryAddress(rastPort, offset, sizeof(ushort), out var address) &&
               memory.TryReadWord(address, out value);
    }

    private static bool TryReadRastPortSignedWord(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out short value)
    {
        value = 0;
        if (!TryReadRastPortWord(memory, rastPort, offset, out var raw))
            return false;

        value = unchecked((short)raw);
        return true;
    }

    private static bool TryReadRastPortLong(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out uint value)
    {
        value = 0;
        return TryAddress(rastPort, offset, sizeof(uint), out var address) &&
               memory.TryReadLong(address, out value);
    }

    private static bool TryWriteRastPortByte(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        byte value)
        => TryAddress(rastPort, offset, sizeof(byte), out var address) &&
           memory.TryWriteByte(address, value);

    private static bool TryWriteRastPortWord(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        ushort value)
        => TryAddress(rastPort, offset, sizeof(ushort), out var address) &&
           memory.TryWriteWord(address, value);

    private static bool TryWriteRastPortLong(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        uint value)
        => TryAddress(rastPort, offset, sizeof(uint), out var address) &&
           memory.TryWriteLong(address, value);

    private static bool TryAddress(uint baseAddress, int offset, int byteCount, out uint address)
    {
        address = 0;
        if (baseAddress == 0 || offset < 0 || byteCount <= 0)
            return false;

        var end = (ulong)(uint)offset + (uint)byteCount - 1UL;
        if (end > uint.MaxValue || (ulong)baseAddress + end > uint.MaxValue)
            return false;

        address = baseAddress + (uint)offset;
        return true;
    }

    private static bool TryProbeRange(IGraphicsMemory memory, uint address, int byteCount)
    {
        if (address == 0 || byteCount <= 0 || address > uint.MaxValue - (uint)(byteCount - 1))
            return false;

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }

        return true;
    }

    private static bool TryPreflightText(
        IGraphicsMemory memory,
        uint textAddress,
        uint count,
        uint fontAddress,
        IGraphicsFontBackend fonts)
    {
        if ((ulong)count > PortableWorkLimit)
            return false;

        ulong strikeBytes = 0;
        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !fonts.TryGetGlyph(fontAddress, character, out var glyph))
            {
                return false;
            }

            if (glyph.CharData == 0 || glyph.Width == 0 || glyph.Height == 0)
                continue;

            var bytesPerRow = ((ulong)(glyph.BitOffset & 7) + glyph.Width + 7UL) / 8UL;
            var glyphBytes = bytesPerRow * glyph.Height;
            if (bytesPerRow == 0 ||
                glyphBytes > PortableWorkLimit ||
                strikeBytes > PortableWorkLimit - glyphBytes)
            {
                return false;
            }

            strikeBytes += glyphBytes;
            if (!TryPreflightGlyph(memory, glyph))
                return false;
        }

        return true;
    }

    private static bool TryPreflightColorText(
        IGraphicsMemory memory,
        uint textAddress,
        uint count,
        uint fontAddress,
        IGraphicsFontBackend fonts,
        GraphicsColorFontInfo colorFont)
    {
        if ((ulong)count > PortableWorkLimit)
            return false;

        ulong strikeBytes = 0;
        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !fonts.TryGetGlyph(fontAddress, character, out var glyph))
            {
                return false;
            }

            if (glyph.Width == 0 || glyph.Height == 0)
                continue;

            var bytesPerRow = ((ulong)(glyph.BitOffset & 7) + glyph.Width + 7UL) / 8UL;
            var glyphBytes = bytesPerRow * glyph.Height;
            if (bytesPerRow == 0 ||
                glyphBytes > PortableWorkLimit ||
                strikeBytes > PortableWorkLimit - glyphBytes)
            {
                return false;
            }

            strikeBytes += glyphBytes;
            if (!colorFont.TryPreflightGlyph(memory, glyph))
                return false;
        }

        return true;
    }

    private static bool TryPreflightGlyph(IGraphicsMemory memory, GraphicsGlyph glyph)
    {
        if (glyph.CharData == 0 || glyph.Width == 0 || glyph.Height == 0)
            return true;

        // A bit-packed glyph row may start at an arbitrary bit offset.  Probe
        // only the bytes used by each row rather than assuming that Modulo is
        // large enough for a contiguous height-wide strike.
        var bytesPerRow = ((ulong)(glyph.BitOffset & 7) + glyph.Width + 7UL) / 8UL;
        if (bytesPerRow == 0 || bytesPerRow > int.MaxValue)
            return false;

        for (var row = 0u; row < glyph.Height; row++)
        {
            var rowOffset = (ulong)row * glyph.Modulo + (uint)(glyph.BitOffset >> 3);
            if (rowOffset > uint.MaxValue - glyph.CharData ||
                !TryProbeRange(memory, glyph.CharData + (uint)rowOffset, (int)bytesPerRow))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryPreflightTextDestination(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        uint textAddress,
        uint count,
        uint fontAddress,
        short startX,
        short baseline,
        ushort textBaseline,
        short textSpacing,
        byte drawMode,
        byte writeMask,
        int boldSmear,
        bool italic,
        bool underline,
        bool reversePath,
        bool variableWidthFont,
        ushort italicBaseline,
        ushort textHeight,
        int underlineStart,
        int underlineEnd,
        int underlineY,
        bool fillTextBackground,
        int backgroundStartX,
        int backgroundEndX,
        int backgroundTopY,
        int backgroundBottomY,
        IGraphicsFontBackend memoryFonts,
        IGraphicsColorFontBackend? colorFonts,
        GraphicsColorFontInfo colorFont,
        bool isColorFont,
        byte foregroundPen,
        bool usesGlyphKerning,
        bool allowUnsignedCellAdvance,
        ushort nominalCellWidth,
        Func<int, int, bool>? pixelVisible,
        bool sourceAdmissionOnly,
        out bool hasVisibleDestination,
        List<uint>? snapshotAddresses = null,
        List<byte>? snapshotValues = null)
    {
        hasVisibleDestination = false;
        var visibleDestinationFound = false;
        if (isColorFont && !sourceAdmissionOnly && colorFonts is null)
            return false;

        writeMask = GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
            return true;

        // The projected layer predicate owns only logical cells that can
        // reach the selected bitmap.  Keep the bitmap envelope ahead of the
        // provider callback for every text source (cell background, glyph,
        // bold smear, and underline).  Off-raster text still advances the
        // cursor and participates in the complete preflight, but it cannot
        // trigger provider classification or destination probes.
        bool IsVisibleDestination(int pixelX, int pixelY)
        {
            var visible = pixelX >= 0 && pixelY >= 0 &&
                pixelX < bitmap.Width && pixelY < bitmap.Rows &&
                (pixelVisible?.Invoke(pixelX, pixelY) ?? true);
            if (visible)
                visibleDestinationFound = true;
            return visible;
        }

        if (!TryPreflightTextWorkBudget(
                memory,
                textAddress,
                count,
                fontAddress,
                bitmap.Depth,
                writeMask,
                textHeight,
                boldSmear,
                fillTextBackground,
                backgroundStartX,
                backgroundEndX,
                backgroundTopY,
                backgroundBottomY,
                underline,
                underlineStart,
                underlineEnd,
                memoryFonts,
                allowUnsignedCellAdvance,
                nominalCellWidth,
                sourceAdmissionOnly,
                metricOnly: sourceAdmissionOnly || (drawMode & 3) == 3))
        {
            return false;
        }

        if (fillTextBackground)
        {
            for (var y = backgroundTopY; y <= backgroundBottomY; y++)
            {
                for (var pixelX = backgroundStartX; pixelX <= backgroundEndX; pixelX++)
                {
                    if (IsVisibleDestination(pixelX, y) &&
                        !sourceAdmissionOnly &&
                        !GraphicsRasterOperations.TryProbeBitmapWrite(
                            memory,
                            bitmap,
                            pixelX,
                            y,
                            writeMask,
                            snapshotAddresses,
                            snapshotValues))
                    {
                        return false;
                    }

                    if (pixelX == int.MaxValue)
                        return false;
                }

                if (y == int.MaxValue)
                    return false;
            }
        }

        // JAM2|COMPLEMENT applies the complement minterm once to the whole
        // TextExtent template.  The source strike is not part of that
        // operation: metric glyphs are sufficient for cursor geometry and
        // the background envelope above already covers every template cell.
        // Returning before TryReadGlyph keeps malformed/unmapped strike
        // storage with the provider/native owner while preserving the normal
        // destination preflight and rollback contract.
        if ((drawMode & 3) == 3)
        {
            hasVisibleDestination = visibleDestinationFound;
            return true;
        }

        bool TryReadGlyph(
            byte character,
            out GraphicsGlyph glyph)
        {
            glyph = default;
            return sourceAdmissionOnly
                ? memoryFonts.TryGetMetricGlyph(fontAddress, character, out glyph)
                : memoryFonts.TryGetGlyph(fontAddress, character, out glyph);
        }

        bool TryReadSource(
            GraphicsGlyph glyph,
            int glyphColumn,
            int row,
            out bool set,
            out byte color)
        {
            set = false;
            color = foregroundPen;
            if (glyphColumn < 0 || row < 0 || row >= glyph.Height)
                return true;

            if (isColorFont)
            {
                return colorFont.TryGetPixel(
                    memory,
                    glyph,
                    glyphColumn,
                    row,
                    foregroundPen,
                    out color,
                    out set);
            }

            return glyph.TryIsSet(memory, glyphColumn, row, out set);
        }

        var cursorX = (int)startX;
        var cursorY = baseline - textBaseline;
        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !TryReadGlyph(character, out var glyph))
            {
                return false;
            }

            if (!TryGetCellWidth(
                    glyph,
                    allowUnsignedCellAdvance,
                    nominalCellWidth,
                    out var cellWidth))
                return false;

            var glyphKerning = usesGlyphKerning ? glyph.Kerning : 0;
            if (!TryAddInt(ref cursorX, glyphKerning))
                return false;
            for (var row = 0; row < textHeight; row++)
            {
                var italicShift = italic
                    ? GetItalicShift(italicBaseline, row)
                    : 0;
                if (sourceAdmissionOnly)
                {
                    for (var column = 0; column < cellWidth; column++)
                    {
                        var glyphColumn = GetGlyphColumnForCell(glyph, column, variableWidthFont);
                        if (!TryGetCellPixelX(
                                cursorX,
                                glyph,
                                column,
                                italicShift,
                                reversePath,
                                out var pixelX))
                        {
                            return false;
                        }

                        var smearLimit = glyphColumn >= 0 ? boldSmear : 0;
                        for (var smear = 0; smear <= smearLimit; smear++)
                        {
                            if (!TryAddInt(pixelX, smear, out var smearX))
                                return false;

                            _ = IsVisibleDestination(smearX, cursorY + row);
                        }
                    }

                    continue;
                }

                for (var column = 0; column < cellWidth; column++)
                {
                    var glyphColumn = GetGlyphColumnForCell(glyph, column, variableWidthFont);
                    if (!TryGetCellPixelX(
                            cursorX,
                            glyph,
                            column,
                            italicShift,
                            reversePath,
                            out var pixelX))
                    {
                        return false;
                    }

                    var pixelVisibleForSource = IsVisibleDestination(
                        pixelX,
                        cursorY + row);
                    if (pixelVisible is not null && !pixelVisibleForSource)
                        continue;

                    if (!TryReadSource(
                            glyph,
                            glyphColumn,
                            row,
                            out var set,
                            out _))
                    {
                        return false;
                    }

                    var patternBit = (drawMode & 4) != 0 ? !set : set;
                    if (!patternBit && (drawMode & 1) != 0)
                    {
                        if (pixelVisibleForSource &&
                            !GraphicsRasterOperations.TryProbeBitmapWrite(
                                memory,
                                bitmap,
                                pixelX,
                                cursorY + row,
                                writeMask,
                                snapshotAddresses,
                                snapshotValues))
                        {
                            return false;
                        }
                    }
                }

                for (var column = 0; column < cellWidth; column++)
                {
                    var glyphColumn = GetGlyphColumnForCell(glyph, column, variableWidthFont);
                    if (!TryGetCellPixelX(
                            cursorX,
                            glyph,
                            column,
                            italicShift,
                            reversePath,
                            out var pixelX))
                    {
                        return false;
                    }

                    var smearLimit = glyphColumn >= 0 ? boldSmear : 0;
                    var visibleSmear = false;
                    for (var smear = 0; smear <= smearLimit; smear++)
                    {
                        if (!TryAddInt(pixelX, smear, out var smearX))
                            return false;

                        if (IsVisibleDestination(smearX, cursorY + row))
                            visibleSmear = true;
                    }

                    if (pixelVisible is not null && !visibleSmear)
                        continue;

                    if (!TryReadSource(
                            glyph,
                            glyphColumn,
                            row,
                            out var set,
                            out _))
                    {
                        return false;
                    }

                    var patternBit = (drawMode & 4) != 0 ? !set : set;
                    if (!patternBit)
                        continue;

                    for (var smear = 0; smear <= smearLimit; smear++)
                    {
                        if (!TryAddInt(pixelX, smear, out var smearX))
                            return false;

                        if (IsVisibleDestination(smearX, cursorY + row) &&
                            !GraphicsRasterOperations.TryProbeBitmapWrite(
                                memory,
                                bitmap,
                                smearX,
                                cursorY + row,
                                writeMask,
                                snapshotAddresses,
                                snapshotValues))
                        {
                            return false;
                        }
                    }
                }
            }

            if (!TryAddInt(ref cursorX, glyph.Advance))
                return false;
            if (!TryAddInt(ref cursorX, textSpacing))
                return false;
        }

        if (underline)
        {
            for (var pixelX = underlineStart; pixelX <= underlineEnd; pixelX++)
            {
                if (IsVisibleDestination(pixelX, underlineY) &&
                    !sourceAdmissionOnly &&
                    !GraphicsRasterOperations.TryProbeBitmapWrite(
                        memory,
                        bitmap,
                        pixelX,
                        underlineY,
                        writeMask,
                        snapshotAddresses,
                        snapshotValues))
                {
                    return false;
                }

                if (pixelX == int.MaxValue)
                    return false;
            }
        }

        hasVisibleDestination = visibleDestinationFound;
        return true;
    }

    private static bool TryPreflightTextWorkBudget(
        IGraphicsMemory memory,
        uint textAddress,
        uint count,
        uint fontAddress,
        int bitmapDepth,
        byte writeMask,
        ushort textHeight,
        int boldSmear,
        bool fillTextBackground,
        int backgroundStartX,
        int backgroundEndX,
        int backgroundTopY,
        int backgroundBottomY,
        bool underline,
        int underlineStart,
        int underlineEnd,
        IGraphicsFontBackend fonts,
        bool allowUnsignedCellAdvance,
        ushort nominalCellWidth,
        bool sourceAdmissionOnly,
        bool metricOnly)
    {
        if ((ulong)count > PortableWorkLimit || boldSmear < 0)
            return false;

        // Text's rollback journal stores one byte per selected destination
        // plane for every published sample.  The older cell-only budget
        // bounded the nested glyph loops but could still admit a valid
        // multi-plane request whose journal/publication span exceeded the
        // host contract.  Count the selected planes before any destination
        // preflight so the complete transaction remains available to a
        // native/provider owner instead of growing a multi-billion-entry
        // managed list.
        var selectedPlanes = 0u;
        for (var plane = 0; plane < bitmapDepth; plane++)
        {
            if ((writeMask & (1 << plane)) != 0)
                selectedPlanes++;
        }

        if (selectedPlanes == 0)
            return true;

        ulong work = 0;
        if (fillTextBackground &&
            !TryAddTextWork(
                ref work,
                TextRectangleCells(
                    backgroundStartX,
                    backgroundEndX,
                    backgroundTopY,
                    backgroundBottomY),
                selectedPlanes))
        {
            return false;
        }

        if (underline &&
            !TryAddTextWork(
                ref work,
                TextSpanCells(underlineStart, underlineEnd),
                selectedPlanes))
        {
            return false;
        }

        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character))
            {
                return false;
            }

            GraphicsGlyph glyph;
            var glyphRead = sourceAdmissionOnly || metricOnly
                ? fonts.TryGetMetricGlyph(fontAddress, character, out glyph)
                : fonts.TryGetGlyph(fontAddress, character, out glyph);
            if (!glyphRead ||
                !TryGetCellWidth(
                    glyph,
                    allowUnsignedCellAdvance,
                    nominalCellWidth,
                    out var cellWidth))
            {
                return false;
            }

            var cells = (ulong)textHeight * (uint)cellWidth;
            var perCell = (3UL + (uint)boldSmear) * selectedPlanes;
            if (cells != 0 &&
                (cells > PortableWorkLimit / perCell ||
                 !TryAddTextWork(ref work, cells, perCell)))
            {
                return false;
            }
        }

        return true;
    }

    private static ulong TextRectangleCells(
        int startX,
        int endX,
        int startY,
        int endY)
    {
        if (endX < startX || endY < startY)
            return 0;

        var width = (ulong)((long)endX - startX + 1L);
        var height = (ulong)((long)endY - startY + 1L);
        return width > ulong.MaxValue / height ? ulong.MaxValue : width * height;
    }

    private static ulong TextSpanCells(int start, int end)
        => end < start ? 0 : (ulong)((long)end - start + 1L);

    private static bool TryAddTextWork(
        ref ulong work,
        ulong cells,
        ulong passesPerCell)
    {
        if (passesPerCell != 0 &&
            cells > PortableWorkLimit / passesPerCell)
        {
            return false;
        }

        var amount = cells * passesPerCell;
        if (amount > PortableWorkLimit - work)
            return false;

        work += amount;
        return true;
    }

    private static bool TryGetCellPixelX(
        int cursorX,
        GraphicsGlyph glyph,
        int column,
        int italicShift,
        bool reversePath,
        out int pixelX)
    {
        // FPF_REVPATH moves the pen toward decreasing X.  The strike remains
        // in its stored left-to-right bit order; its cell is anchored at the
        // movement endpoint so the glyph occupies the pixels immediately to
        // the left of the original pen position.
        var origin = reversePath
            ? (long)cursorX + Math.Min(0, glyph.Advance)
            : cursorX;
        var value = origin + italicShift + column;
        if (value < int.MinValue || value > int.MaxValue)
        {
            pixelX = 0;
            return false;
        }

        pixelX = (int)value;
        return true;
    }

    private static int GetGlyphColumnForCell(
        GraphicsGlyph glyph,
        int cellColumn,
        bool variableWidthFont)
    {
        if (variableWidthFont)
            return cellColumn < glyph.Width ? cellColumn : -1;

        // Fixed-cell fonts advance by tf_XSize even when a CharLoc strike is
        // narrower.  The strike is centered in that nominal movement cell;
        // padding columns remain source-zero cells at their original cell
        // positions for JAM/INVERSVID/complement handling.
        var centeredColumn = cellColumn - GetFixedCellCenterOffset(glyph);
        return centeredColumn >= 0 && centeredColumn < glyph.Width
            ? centeredColumn
            : -1;
    }

    private static int GetFixedCellCenterOffset(GraphicsGlyph glyph)
    {
        var centering = Math.Abs(glyph.Advance) - glyph.Width;
        return centering > 0 ? centering / 2 : 0;
    }

    private static int GetItalicShift(ushort baseline, int row)
    {
        // Kickstart's smear starts at floor(tf_Baseline / 2) and moves one
        // pixel left on every other scan line while the baseline countdown
        // remains odd.  Floor division is important below the baseline:
        // C# truncates negative integer division toward zero, whereas the
        // 68k routine's countdown produces -1 for a remaining odd row.
        var delta = (long)baseline - row;
        if (delta >= 0)
            return (int)(delta / 2);

        return (int)(-((-delta + 1) / 2));
    }

    private static bool TryGetUnderlineRange(
        IGraphicsMemory memory,
        uint textAddress,
        uint count,
        uint fontAddress,
        int startX,
        short textSpacing,
        bool reversePath,
        short baseline,
        ushort textBaseline,
        GraphicsFontMetrics metrics,
        byte algorithmStyle,
        IGraphicsFontBackend fonts,
        bool usesGlyphKerning,
        bool allowUnsignedCellAdvance,
        out int underlineStart,
        out int underlineEnd,
        out int underlineY,
        out bool underlineVisible)
    {
        underlineStart = 0;
        underlineEnd = -1;
        underlineY = 0;
        underlineVisible = false;

        var row = (long)textBaseline + 1;
        if (row < metrics.Height - 1)
            row++;
        if (row >= metrics.Height)
            return true;

        var cursorX = (long)startX;
        var minX = long.MaxValue;
        var maxX = long.MinValue;
        for (var index = 0u; index < count; index++)
        {
            if (!TryAdd(textAddress, index, out var characterAddress) ||
                !memory.TryReadByte(characterAddress, out var character) ||
                !fonts.TryGetGlyph(fontAddress, character, out var glyph))
            {
                return false;
            }

            if (!TryGetCellWidth(
                    glyph,
                    allowUnsignedCellAdvance,
                    metrics.Width,
                    out _))
                return false;

            if (usesGlyphKerning)
                cursorX += glyph.Kerning;
            if (!FitsInt(cursorX))
                return false;

            var advanceEnd = cursorX + glyph.Advance;
            if (!FitsInt(advanceEnd))
                return false;
            var movementEnd = advanceEnd + textSpacing;
            if (!FitsInt(movementEnd))
                return false;

            if (reversePath)
            {
                var reverseOrigin = cursorX + Math.Min(0, glyph.Advance);
                var reverseWidth = Math.Max(glyph.Width, Math.Abs(glyph.Advance));
                if (!IncludeUnderlineRange(
                        reverseOrigin,
                        reverseOrigin + reverseWidth - 1L,
                        ref minX,
                        ref maxX))
                {
                    return false;
                }
            }
            else if (!IncludeUnderlineRange(
                         cursorX,
                         cursorX + glyph.Width - 1L,
                         ref minX,
                         ref maxX))
            {
                return false;
            }

            // Underline is a run-wide operation. Include the complete
            // consumed movement cell in either direction so negative
            // CharSpace/TxSpacing does not leave a gap to the left of the
            // glyph envelope.
            if (advanceEnd != cursorX &&
                !IncludeUnderlineRange(
                    Math.Min(cursorX, advanceEnd),
                    Math.Max(cursorX, advanceEnd) - 1L,
                    ref minX,
                    ref maxX))
            {
                return false;
            }

            if (movementEnd != advanceEnd &&
                !IncludeUnderlineRange(
                    Math.Min(advanceEnd, movementEnd),
                    Math.Max(advanceEnd, movementEnd) - 1L,
                    ref minX,
                    ref maxX))
            {
                return false;
            }

            cursorX = movementEnd;
        }

        if (minX == long.MaxValue ||
            !FitsInt(minX) ||
            !FitsInt(maxX) ||
            maxX < minX ||
            row < int.MinValue ||
            row > int.MaxValue)
        {
            return false;
        }

        // Underline is generated after the styled template has been built.
        // Keep its run-wide line aligned with the same algorithmic bold and
        // italic envelope reported by TextExtent; otherwise a combined
        // style can leave its right smear or italic padding un-underlined.
        if (!TryApplyStyleExtent(metrics,
                algorithmStyle,
                ref minX,
                ref maxX))
        {
            return false;
        }

        var y = (long)baseline - textBaseline + row;
        if (!FitsInt(y))
            return false;

        underlineStart = (int)minX;
        underlineEnd = (int)maxX;
        underlineY = (int)y;
        underlineVisible = true;
        return true;
    }

    private static bool IncludeUnderlineRange(
        long candidateMin,
        long candidateMax,
        ref long min,
        ref long max)
    {
        if (!FitsInt(candidateMin) || !FitsInt(candidateMax))
            return false;

        if (candidateMin < min)
            min = candidateMin;
        if (candidateMax > max)
            max = candidateMax;
        return true;
    }

    private static bool TryAddInt(ref int value, int delta)
    {
        var result = (long)value + delta;
        if (result < int.MinValue || result > int.MaxValue)
            return false;

        value = (int)result;
        return true;
    }

    private static bool TryAddInt(int value, int delta, out int result)
    {
        var sum = (long)value + delta;
        if (sum < int.MinValue || sum > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)sum;
        return true;
    }

    private static bool TryGetTextCellEnvelope(
        GraphicsGlyph glyph,
        long cursor,
        bool reversePath,
        bool variableWidthFont,
        out long minX,
        out long maxX)
    {
        minX = 0;
        maxX = -1;

        if (variableWidthFont)
        {
            // The renderer places reverse-path strikes at the cell's left
            // edge, while normal-path strikes begin at the current pen.
            var origin = reversePath
                ? cursor + Math.Min(0, glyph.Advance)
                : cursor;
            var glyphMax = (long)origin + glyph.Width;
            if (!FitsInt(origin) || !FitsInt(glyphMax))
                return false;

            minX = origin;
            maxX = glyphMax;
        }
        else
        {
            // Fixed-cell metrics publish the nominal movement cell rather
            // than the strike width.  Keep both endpoints for signed
            // advances so reverse-path and negative-spacing text has a
            // rectangle on the same side of the pen as the renderer.
            var cellEnd = cursor + glyph.Advance;
            if (!FitsInt(cellEnd))
                return false;

            if (glyph.Advance < 0)
            {
                minX = cellEnd;
                maxX = cursor;
            }
            else
            {
                minX = cursor;
                maxX = cellEnd;
            }
        }

        return FitsInt(minX) && FitsInt(maxX);
    }

    private static bool TryGetMetricAdvance(
        GraphicsFontMetrics metrics,
        GraphicsGlyph glyph,
        out int advance)
    {
        advance = glyph.Advance;

        // TextFont.tf_CharSpace is a signed WORD.  A decoded font without a
        // CharSpace table uses the nominal tf_XSize cell, which is an
        // unsigned WORD (and becomes negative only for REVPATH).  Keep the
        // provider boundary faithful to those guest field widths instead of
        // allowing an arbitrary host int to become a metric movement.
        var minimum = metrics.HasCharSpace ? (int)short.MinValue : -ushort.MaxValue;
        var maximum = metrics.HasCharSpace ? (int)short.MaxValue : ushort.MaxValue;
        if (advance < minimum || advance > maximum)
            return false;

        if (!metrics.HasCharSpace &&
            (advance < short.MinValue || advance > short.MaxValue) &&
            (metrics.Width == 0 || Math.Abs((long)advance) != metrics.Width))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetCellWidth(
        GraphicsGlyph glyph,
        bool allowUnsignedCellAdvance,
        ushort nominalCellWidth,
        out int cellWidth)
    {
        // A glyph backed by tf_CharSpace uses a signed WORD movement.  A
        // fixed-cell glyph without that table instead uses the unsigned
        // tf_XSize WORD; FPF_REVPATH negates that value, so the valid range
        // is -65535..65535 rather than the signed-WORD interval.
        var minimumAdvance = allowUnsignedCellAdvance
            ? -(int)ushort.MaxValue
            : (int)short.MinValue;
        var maximumAdvance = allowUnsignedCellAdvance
            ? (int)ushort.MaxValue
            : (int)short.MaxValue;
        if (glyph.Advance < minimumAdvance || glyph.Advance > maximumAdvance)
        {
            // A no-CharSpace TextFont derives movement from the unsigned
            // tf_XSize field. Host/provider glyphs must still identify that
            // nominal cell explicitly; an arbitrary out-of-range advance is
            // a signed CharSpace value in disguise and stays native-owned.
            if (!allowUnsignedCellAdvance ||
                nominalCellWidth == 0 ||
                Math.Abs((long)glyph.Advance) != nominalCellWidth)
            {
                cellWidth = 0;
                return false;
            }
        }

        cellWidth = Math.Max(glyph.Width, Math.Abs(glyph.Advance));
        return true;
    }

    private static bool TryReadTextExtent(IGraphicsMemory memory, uint address, out TextMeasure extent)
    {
        extent = default;
        // TextExtent is a packed sequence of 16-bit fields.  Keep the
        // result envelope on the same 68k word-alignment boundary as the
        // native vector; an odd guest address would raise an address error
        // instead of being byte-processed by the host bridge.
        if ((address & 1u) != 0 ||
            !TryProbeRange(memory, address, GraphicsLayouts.TextExtentSize) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.TextExtentWidth, out var width) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.TextExtentHeight, out var height) ||
            !TryReadSignedWord(memory, address + (uint)GraphicsLayouts.TextExtentMinX, out var minX) ||
            !TryReadSignedWord(memory, address + (uint)GraphicsLayouts.TextExtentMinY, out var minY) ||
            !TryReadSignedWord(memory, address + (uint)GraphicsLayouts.TextExtentMaxX, out var maxX) ||
            !TryReadSignedWord(memory, address + (uint)GraphicsLayouts.TextExtentMaxY, out var maxY))
        {
            return false;
        }

        extent = new TextMeasure(width, height, minX, minY, maxX, maxY);
        return true;
    }

    private static bool TryValidateTextExtentDestination(
        IGraphicsMemory memory,
        uint address)
        => address != 0 &&
           (address & 1u) == 0 &&
           TryProbeRange(memory, address, GraphicsLayouts.TextExtentSize);

    private static bool TrySnapshotRastPortField(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (!TryAddress(rastPort, offset, byteCount, out var address) ||
            !TryProbeRange(memory, address, byteCount))
        {
            return false;
        }

        original = new byte[byteCount];
        for (var index = 0; index < byteCount; index++)
        {
            if (!memory.TryReadByte(address + (uint)index, out original[index]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static void RestoreRastPortField(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        byte[] original)
    {
        if (!TryAddress(rastPort, offset, original.Length, out var address))
            return;

        for (var index = original.Length - 1; index >= 0; index--)
            _ = memory.TryWriteByte(address + (uint)index, original[index]);
    }

    private static bool WriteTextExtent(IGraphicsMemory memory, uint address, TextMeasure extent)
    {
        // TextExtent/TextFit/FontExtent all publish one 12-byte guest result.
        // Probe the complete structure first so a truncated pointer cannot
        // leave only width/height (or a partial rectangle) visible.
        // Width/height are UWORDs at the public ABI (the width may carry a
        // negative REVPATH value in its two's-complement representation),
        // while the Rectangle members are signed WORDs.  Do not let a
        // checked 32-bit accumulation silently wrap either class of field
        // during the final guest publication; a native/provider owner can
        // still implement the ROM's legacy overflow behavior.
        if (!CanPublishTextExtent(extent) ||
            (address & 1u) != 0 ||
            !TryProbeRange(memory, address, GraphicsLayouts.TextExtentSize))
            return false;

        var original = new byte[GraphicsLayouts.TextExtentSize];
        for (var index = 0; index < original.Length; index++)
        {
            if (!memory.TryReadByte(address + (uint)index, out original[index]))
                return false;
        }

        if (memory.TryWriteWord(address + (uint)GraphicsLayouts.TextExtentWidth, unchecked((ushort)extent.Width)) &&
            memory.TryWriteWord(address + (uint)GraphicsLayouts.TextExtentHeight, unchecked((ushort)extent.Height)) &&
            memory.TryWriteWord(address + (uint)GraphicsLayouts.TextExtentMinX, unchecked((ushort)(short)extent.MinX)) &&
            memory.TryWriteWord(address + (uint)GraphicsLayouts.TextExtentMinY, unchecked((ushort)(short)extent.MinY)) &&
            memory.TryWriteWord(address + (uint)GraphicsLayouts.TextExtentMaxX, unchecked((ushort)(short)extent.MaxX)) &&
            memory.TryWriteWord(address + (uint)GraphicsLayouts.TextExtentMaxY, unchecked((ushort)(short)extent.MaxY)))
        {
            return true;
        }

        for (var index = 0; index < original.Length; index++)
            _ = memory.TryWriteByte(address + (uint)index, original[index]);
        return false;
    }

    private static bool CanPublishTextExtent(TextMeasure extent)
        // te_Width is a UWORD.  Keep the signed internal movement available
        // through the complete 16-bit bit-pattern range so reverse-path
        // fixed cells below -32768 publish their native wrapped value.
        => extent.Width >= -ushort.MaxValue &&
           extent.Width <= ushort.MaxValue &&
           extent.Height >= 0 &&
           extent.Height <= ushort.MaxValue &&
           // A reverse-path fixed cell derives MinX from the unsigned
           // tf_XSize movement word before its signed TextExtent fields are
           // published.  Preserve that guest word pattern for negative
           // MinX values (the width field already follows the same UWORD
           // contract), while MaxX and all positive rectangle members remain
           // signed WORD bounded.
           extent.MinX >= -ushort.MaxValue &&
           extent.MinX <= short.MaxValue &&
           extent.MinY >= short.MinValue &&
           extent.MinY <= short.MaxValue &&
           extent.MaxX >= short.MinValue &&
           extent.MaxX <= short.MaxValue &&
           extent.MaxY >= short.MinValue &&
           extent.MaxY <= short.MaxValue;

    private static bool FitsInt(long value)
        => value >= int.MinValue && value <= int.MaxValue;

    private static bool TryApplyStyleExtent(
        GraphicsFontMetrics metrics,
        byte algorithmStyle,
        ref long minX,
        ref long maxX)
    {
        if ((algorithmStyle & StyleBold) != 0)
            maxX += metrics.BoldSmear;

        if ((algorithmStyle & StyleItalic) != 0)
        {
            maxX += metrics.Baseline / 2;
            minX -= ((long)metrics.Height - metrics.Baseline) / 2;
        }

        return FitsInt(minX) && FitsInt(maxX);
    }

    private static bool Fits(
        TextMeasure candidate,
        TextMeasure constraint,
        bool hasExtent,
        uint width,
        uint height)
    {
        // The alternate rendering-box dimensions are independent of the
        // rendering origin, but are ignored when an explicit TextExtent is
        // supplied. Kickstart requires the candidate's actual envelope (not
        // only its signed cursor advance) to fit the active constraint.
        if (!hasExtent)
        {
            var envelopeWidth = (long)candidate.MaxX - candidate.MinX + 1L;
            if (envelopeWidth > width || candidate.Height > height)
                return false;
        }

        // An explicit TextExtent additionally requires the current pen
        // origin to be included on the X axis. A rectangle wholly to either
        // side of x=0 is invalid even when the rendered glyph envelope would
        // fit inside that rectangle.
        if (hasExtent &&
            (constraint.MinX > 0 || constraint.MaxX < 0 ||
             candidate.Width > constraint.Width ||
             candidate.Height > constraint.Height ||
             candidate.MinX < constraint.MinX || candidate.MaxX > constraint.MaxX ||
             candidate.MinY < constraint.MinY || candidate.MaxY > constraint.MaxY))
        {
            return false;
        }
        return true;
    }

    private static bool TryGetDirectedAddress(uint baseAddress, uint index, int direction, out uint address)
    {
        if (direction == 1)
            return TryAdd(baseAddress, index, out address);

        if (index > baseAddress)
        {
            address = 0;
            return false;
        }

        address = baseAddress - index;
        return true;
    }

    private static bool TryValidateTextSpan(uint textAddress, uint count, int direction)
    {
        if (count == 0)
            return true;

        var lastOffset = count - 1;
        return direction switch
        {
            1 => lastOffset <= uint.MaxValue - textAddress,
            -1 => lastOffset <= textAddress,
            _ => false
        };
    }

    private readonly struct TextMeasure
    {
        internal TextMeasure(int width, int height, int minX, int minY, int maxX, int maxY)
        {
            Width = width;
            Height = height;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int MinX { get; }
        internal int MinY { get; }
        internal int MaxX { get; }
        internal int MaxY { get; }
    }

    private static bool TryReadSignedWord(IGraphicsMemory memory, uint address, out short value)
    {
        if (!memory.TryReadWord(address, out var raw))
        {
            value = 0;
            return false;
        }

        value = unchecked((short)raw);
        return true;
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
