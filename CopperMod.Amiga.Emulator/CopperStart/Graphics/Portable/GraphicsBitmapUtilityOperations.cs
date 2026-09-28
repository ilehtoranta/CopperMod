using System;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static class GraphicsBitmapUtilityOperations
{
    internal const int Success = 0;
    internal const int Failure = -1;

    // BitMapScale stages the sampled source cells, snapshots the selected
    // destination planes, and then publishes the scaled cells.  Keep those
    // three potentially full-rectangle passes inside the same bounded host
    // work contract used by the other transactional planar primitives.  The
    // guard is deliberately applied before DDA arrays or the staged colour
    // buffer are allocated; a near-Int32 destination must decline rather than
    // turn a valid 16-bit guest rectangle into a multi-gigabyte host request.
    private const ulong PortableWorkLimit = int.MaxValue;
    private const ulong BitMapScalePassesPerCell = 3;

    /// <summary>
    /// Computes the destination extent used by BitMapScale.  The public ABI
    /// constrains all factors to 14 bits; invalid zero denominators are
    /// rejected rather than allowing a host divide-by-zero path.
    /// </summary>
    internal static ushort ScalerDiv(ushort factor, ushort numerator, ushort denominator)
    {
        if (denominator == 0 || numerator == 0 ||
            factor > 0x3FFF || numerator > 0x3FFF || denominator > 0x3FFF)
        {
            return 0;
        }

        if (factor == 0)
            return 0;

        var product = (ulong)factor * numerator;
        var result = product / denominator;
        // The classic helper never reports a zero-sized destination for a
        // non-zero scale request.  A product smaller than the denominator is
        // promoted to one before the half-up remainder rule is applied.
        if (result == 0)
            return 1;

        var remainder = product % denominator;
        // Kickstart's scaler uses nearest-integer arithmetic, rounding a
        // half (and anything larger) upward.  BitMapScale uses this same
        // helper for the published destination extent.
        if (remainder >= ((uint)denominator + 1u) / 2u)
            result++;

        // The public ABI returns UWORD.  Kickstart computes the product in a
        // wider temporary, then returns the word-sized result; it does not
        // saturate a valid 14-bit-factor product at $FFFF.  Keep the cast at
        // the ABI boundary so callers observe the native low-word wrap.
        return (ushort)result;
    }

    /// <summary>
    /// Applies the portable OCS/ECS BitMapScale contract to two standard
    /// planar guest BitMaps.  The classic structure contains no destination
    /// dimensions on input: they are derived from the source extent and the
    /// two factor ratios, then written back before the pixels are copied.
    /// CyberGraphX/RTG surfaces are deliberately left to the host patch
    /// boundary; this operation only accepts native planar memory.
    /// </summary>
    internal static int BitMapScale(IGraphicsMemory memory, uint bitScaleArgs)
    {
        if (!TryReadArgs(memory, bitScaleArgs, out var args) ||
            args.Flags != 0 ||
            // A zero scale factor is not a zero-sized success: the classic
            // ScalerDiv contract cannot form a destination ratio from it.
            // Keep this rejection before bitmap probing or extent publication
            // so the portable transaction leaves the caller's BitScaleArgs
            // untouched and the native/provider owner can retry the request.
            args.XSrcFactor == 0 || args.XSrcFactor > 0x3FFF ||
            args.XDestFactor == 0 || args.XDestFactor > 0x3FFF ||
            args.YSrcFactor == 0 || args.YSrcFactor > 0x3FFF ||
            args.YDestFactor == 0 || args.YDestFactor > 0x3FFF ||
            args.SrcWidth == 0 || args.SrcHeight == 0 ||
            args.SrcBitMap == 0 || args.DestBitMap == 0 ||
            // BitMapScale consumes an ordinary planar BitMap, not the
            // V36 BltBitMap constant-plane form. Every declared source
            // plane therefore needs a readable non-null link before the
            // scaler claims the call; treating a missing link as an
            // implicit zero plane would publish a result that a native
            // planar owner could not produce safely.
            !TryReadBitmap(memory, args.SrcBitMap, requirePlanes: true, out var source) ||
            !TryReadBitmap(memory, args.DestBitMap, requirePlanes: true, out var destination))
        {
            return Failure;
        }

        var destinationWidth = ScaleExtent(args.SrcWidth, args.XDestFactor, args.XSrcFactor);
        var destinationHeight = ScaleExtent(args.SrcHeight, args.YDestFactor, args.YSrcFactor);
        if (destinationWidth == 0 || destinationHeight == 0 ||
            !Fits(source, args.SrcX, args.SrcY, args.SrcWidth, args.SrcHeight) ||
            !Fits(destination, args.DestX, args.DestY, destinationWidth, destinationHeight) ||
            // The classic vector rejects a shared BitMap header even when
            // the source and destination rectangles happen not to overlap.
            // Keeping that ownership rule explicit avoids turning a
            // disjoint same-bitmap copy into an in-place scaling operation
            // whose source sampling and destination writes can diverge.
            args.SrcBitMap == args.DestBitMap ||
            (args.SrcBitMap != args.DestBitMap &&
             HasAliasedPlaneStorage(memory, source, destination)))
        {
            return Failure;
        }

        var writeMask = (byte)((1 << Math.Min(source.Depth, destination.Depth)) - 1);
        var destinationCells = (ulong)destinationWidth * destinationHeight;
        if (destinationCells == 0 ||
            destinationCells > PortableWorkLimit / BitMapScalePassesPerCell)
            return Failure;

        // BitMapScale is one guest operation.  Read every source sample and
        // probe every selected destination plane before publishing either the
        // derived extent fields or the first scaled pixel; otherwise a late
        // malformed source/destination row can expose a partial scale.
        // Native planar BitMaps are limited to eight planes, so every sampled
        // pen value fits in one guest byte.  Keep the transactional staging
        // buffer byte-sized as well; using an int[] here needlessly multiplied
        // the host allocation and made large-but-valid guest extents much more
        // likely to fail before the first guest write.
        // BitMapScale uses the classic centered DDA schedule, driven by the
        // requested source/destination factors rather than by the rounded
        // extents.  The two schedules happen to agree for common 2:3
        // expansion, but diverge for ratios such as 1:2; using the extents
        // would move the duplicated source pixel by one destination column.
        var xSamples = BuildDdaSamples(
            args.SrcWidth,
            args.XSrcFactor,
            args.XDestFactor,
            destinationWidth);
        var ySamples = BuildDdaSamples(
            args.SrcHeight,
            args.YSrcFactor,
            args.YDestFactor,
            destinationHeight);
        var scaledColors = new byte[(int)destinationCells];
        for (var destinationY = 0; destinationY < destinationHeight; destinationY++)
        {
            var sourceY = args.SrcY + ySamples[destinationY];
            // The guest dimensions are WORDs, but their product is not.  Keep
            // the row-base calculation wide until after the total cell-count
            // guard above, then use the proven int range for indexing.
            var destinationRowBase = (int)((long)destinationY * destinationWidth);
            for (var destinationX = 0; destinationX < destinationWidth; destinationX++)
            {
                var sourceX = args.SrcX + xSamples[destinationX];
                if (!ReadBitmapPixel(
                        memory,
                        source,
                        sourceX,
                        sourceY,
                        out var color))
                {
                    return Failure;
                }

                scaledColors[destinationRowBase + destinationX] = (byte)color;
                if (!GraphicsRasterOperations.TryProbeBitmapWrite(
                        memory,
                        destination,
                        args.DestX + destinationX,
                        args.DestY + destinationY,
                        writeMask))
                {
                    return Failure;
                }
            }
        }

        if (!memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestWidth,
                out var originalDestinationWidth) ||
            !memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestHeight,
                out var originalDestinationHeight) ||
            !GraphicsRasterOperations.TrySnapshotBitmapRegion(
                memory,
                destination,
                args.DestX,
                args.DestY,
                args.DestX + destinationWidth - 1,
                args.DestY + destinationHeight - 1,
                writeMask,
                out var destinationSnapshotAddresses,
                out var destinationSnapshotValues))
        {
            return Failure;
        }

        void RestoreTransaction()
        {
            RestoreWord(
                memory,
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestWidth,
                originalDestinationWidth);
            RestoreWord(
                memory,
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestHeight,
                originalDestinationHeight);
            GraphicsRasterOperations.RestoreBitmapSnapshot(
                memory,
                destinationSnapshotAddresses,
                destinationSnapshotValues);
        }

        if (!memory.TryWriteWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestWidth,
                destinationWidth) ||
            !memory.TryWriteWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestHeight,
                destinationHeight))
        {
            RestoreTransaction();
            return Failure;
        }

        // Planar blits only have source data for the common plane range.  A
        // destination with extra planes keeps those planes untouched, just
        // as the classic min-depth blitter contract does.
        for (var destinationY = 0; destinationY < destinationHeight; destinationY++)
        {
            var destinationRowBase = (int)((long)destinationY * destinationWidth);
            for (var destinationX = 0; destinationX < destinationWidth; destinationX++)
            {
                if (!GraphicsRasterOperations.SetBitmapPixel(
                        memory,
                        destination,
                        args.DestX + destinationX,
                        args.DestY + destinationY,
                        scaledColors[destinationRowBase + destinationX],
                        drawMode: 0,
                        writeMask: writeMask))
                {
                    RestoreTransaction();
                    return Failure;
                }
            }
        }

        return Success;
    }

    private static void RestoreWord(
        IGraphicsMemory memory,
        uint address,
        ushort value)
    {
        _ = memory.TryWriteByte(address, (byte)(value >> 8));
        _ = memory.TryWriteByte(address + 1u, (byte)value);
    }

    private static bool TryReadArgs(
        IGraphicsMemory memory,
        uint address,
        out BitScaleArgs args)
    {
        args = default;
        if (address == 0 || (address & 1u) != 0 ||
            address > uint.MaxValue - (uint)(GraphicsLayouts.BitScaleArgsSize - 1) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcX, out var srcX) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcY, out var srcY) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcWidth, out var srcWidth) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsSrcHeight, out var srcHeight) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsXSrcFactor, out var xSrcFactor) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsYSrcFactor, out var ySrcFactor) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsDestX, out var destX) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsDestY, out var destY) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsXDestFactor, out var xDestFactor) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitScaleArgsYDestFactor, out var yDestFactor) ||
            !memory.TryReadLong(address + (uint)GraphicsLayouts.BitScaleArgsSrcBitMap, out var srcBitMap) ||
            !memory.TryReadLong(address + (uint)GraphicsLayouts.BitScaleArgsDestBitMap, out var destBitMap) ||
            !memory.TryReadLong(address + (uint)GraphicsLayouts.BitScaleArgsFlags, out var flags))
        {
            return false;
        }

        args = new BitScaleArgs(
            srcX,
            srcY,
            srcWidth,
            srcHeight,
            xSrcFactor,
            ySrcFactor,
            destX,
            destY,
            xDestFactor,
            yDestFactor,
            srcBitMap,
            destBitMap,
            flags);
        return true;
    }

    private static bool TryReadBitmap(
        IGraphicsMemory memory,
        uint address,
        bool requirePlanes,
        out GraphicsRasterOperations.BitmapInfo bitmap)
    {
        bitmap = default;
        if (address == 0 || (address & 1u) != 0 ||
            address > uint.MaxValue - (uint)(GraphicsLayouts.BitMapDepth + sizeof(byte) - 1) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitMapBytesPerRow, out var bytesPerRow) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.BitMapRows, out var rows) ||
            !memory.TryReadByte(address + (uint)GraphicsLayouts.BitMapFlags, out var flags) ||
            !memory.TryReadByte(address + (uint)GraphicsLayouts.BitMapDepth, out var depth) ||
            bytesPerRow == 0 || (bytesPerRow & 1) != 0 || rows == 0 || depth == 0 || depth > 8)
        {
            return false;
        }

        // BitMap headers allocated with BMF_MINPLANES carry only the
        // declared number of plane links.  BitMapScale consumes those links
        // directly, so its probe must stop at the depth-sized envelope rather
        // than requiring the unused five-to-eight-plane tail.
        var structureBytes = GraphicsLayouts.BitMapPlanes + (depth * sizeof(uint));
        if (address > uint.MaxValue - (uint)(structureBytes - 1))
            return false;

        if (!GraphicsRasterOperations.TryGetPlaneBytesPerRow(
                bytesPerRow,
                depth,
                flags,
                out var planeBytesPerRow))
        {
            return false;
        }

        bitmap = new GraphicsRasterOperations.BitmapInfo(
            address,
            bytesPerRow,
            planeBytesPerRow,
            rows,
            depth,
            flags);
        uint firstPlaneAddress = 0;
        var haveFirstPlaneAddress = false;
        for (var plane = 0; plane < depth; plane++)
        {
            // The plane pointer is a complete guest LONG.  Its final byte
            // may legally be the last byte of the 32-bit address space;
            // reject only a start whose inclusive end crosses into zero.
            if (address > uint.MaxValue -
                (uint)(GraphicsLayouts.BitMapPlanes + (plane * 4) + sizeof(uint) - 1))
                return false;

            if (!memory.TryReadLong(
                    address + (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4),
                    out var planeAddress))
            {
                return false;
            }

            if (planeAddress == 0)
            {
                if (requirePlanes)
                    return false;
                continue;
            }

            if ((flags & (1 << 2)) != 0)
            {
                if (plane == 0)
                {
                    firstPlaneAddress = planeAddress;
                    haveFirstPlaneAddress = true;
                }
                else if (haveFirstPlaneAddress)
                {
                    var expected = (ulong)firstPlaneAddress +
                        ((ulong)plane * planeBytesPerRow);
                    if (expected > uint.MaxValue || planeAddress != (uint)expected)
                        return false;
                }
            }

            // BitMapScale is a native planar operation. Its word-oriented
            // source/destination envelopes must not cross an odd 68000
            // address, even when the host memory adapter can read individual
            // bytes there. A null source plane is rejected above; an odd
            // non-null plane is likewise rejected before any extent or
            // destination write is published.
            if ((planeAddress & 1u) != 0)
                return false;

            if (!TryProbePlane(
                    memory,
                    planeAddress,
                    bytesPerRow,
                    planeBytesPerRow,
                    rows,
                    flags))
                return false;
        }

        return true;
    }

    private static bool TryProbePlane(
        IGraphicsMemory memory,
        uint address,
        ushort guestBytesPerRow,
        ushort bytesPerRow,
        ushort rows,
        byte flags)
    {
        var rowStride = (flags & (1 << 2)) != 0 ? guestBytesPerRow : bytesPerRow;
        var byteCount = ((ulong)(rows - 1) * rowStride) + bytesPerRow;
        if (rows == 0 || bytesPerRow == 0 || byteCount == 0 || byteCount > uint.MaxValue ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        // A valid UWORD bitmap can describe a plane larger than the host's
        // transactional span limit. Keep this geometry probe bounded; every
        // source sample and destination write is still checked individually
        // by the scaler before publication.
        if (byteCount > int.MaxValue)
        {
            var last = address + (uint)(byteCount - 1);
            return memory.TryReadByte(address, out _) &&
                memory.TryReadByte(last, out _);
        }

        for (var row = 0; row < rows; row++)
        {
            var rowAddress = address + (uint)((ulong)row * rowStride);
            for (var offset = 0; offset < bytesPerRow; offset++)
            {
                if (!memory.TryReadByte(rowAddress + (uint)offset, out _))
                    return false;
            }
        }

        return true;
    }

    private static bool ReadBitmapPixel(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int x,
        int y,
        out int color)
    {
        color = 0;
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return false;

        var bitMask = (byte)(0x80 >> (x & 7));
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            // A null source plane is a zero plane in the same spirit as the
            // classic blitter's V36 source semantics.
            if (!memory.TryReadLong(
                    bitmap.Address + (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4),
                    out var planeAddress))
                return false;

            if (planeAddress == 0)
                continue;

            if (!GraphicsRasterOperations.TryGetPlaneByteAddress(
                    memory,
                    bitmap,
                    plane,
                    x,
                    y,
                    out var byteAddress) ||
                !memory.TryReadByte(byteAddress, out var value))
                return false;
            if ((value & bitMask) != 0)
                color |= 1 << plane;
        }

        return true;
    }

    private static bool Fits(
        GraphicsRasterOperations.BitmapInfo bitmap,
        int x,
        int y,
        int width,
        int height)
        => x >= 0 && y >= 0 && width > 0 && height > 0 &&
           (long)x + width <= bitmap.Width &&
           (long)y + height <= bitmap.Rows;

    private static ushort ScaleExtent(ushort extent, ushort numerator, ushort denominator)
        => ScalerDiv(extent, numerator, denominator);

    private static int[] BuildDdaSamples(
        ushort sourceExtent,
        ushort sourceFactor,
        ushort destinationFactor,
        ushort destinationExtent)
    {
        var samples = new int[destinationExtent];
        var sourceLast = sourceExtent - 1;
        // This is the native bms_srcpos() phase: enlargements use
        // (sourceFactor - 1) / 2 while reductions use destinationFactor / 2.
        // Keep the arithmetic wide even though the public factors are UWORDs.
        var phase = sourceFactor < destinationFactor
            ? ((long)sourceFactor - 1) / 2
            : (long)destinationFactor / 2;

        for (var destinationIndex = 0;
             destinationIndex < destinationExtent;
             destinationIndex++)
        {
            var sourceIndex = ((long)destinationIndex * sourceFactor + phase) /
                              destinationFactor;
            samples[destinationIndex] = (int)Math.Min(sourceIndex, sourceLast);
        }

        return samples;
    }

    private static bool HasAliasedPlaneStorage(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo source,
        GraphicsRasterOperations.BitmapInfo destination)
    {
        var sourceSpan = GraphicsRasterOperations.GetBitmapPlaneTouchedSpan(source);
        var destinationSpan = GraphicsRasterOperations.GetBitmapPlaneTouchedSpan(destination);
        if (sourceSpan == 0 || destinationSpan == 0)
            return true;

        for (var sourcePlane = 0; sourcePlane < source.Depth; sourcePlane++)
        {
            if (!TryReadPlaneAddress(memory, source, sourcePlane, out var sourceAddress))
                return true;

            if (sourceAddress == 0)
            {
                continue;
            }

            for (var destinationPlane = 0; destinationPlane < destination.Depth; destinationPlane++)
            {
                if (!TryReadPlaneAddress(memory, destination, destinationPlane, out var destinationAddress))
                    return true;

                if (destinationAddress == 0)
                {
                    continue;
                }

                if (PlaneRangesOverlap(
                        source,
                        sourceAddress,
                        destination,
                        destinationAddress,
                        sourceSpan,
                        destinationSpan))
                    return true;
            }
        }

        return false;
    }

    private static bool TryReadPlaneAddress(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int plane,
        out uint address)
    {
        address = 0;
        var planeOffset = GraphicsLayouts.BitMapPlanes + (plane * 4);
        if (plane < 0 || plane >= bitmap.Depth ||
            planeOffset < 0 ||
            bitmap.Address > uint.MaxValue -
                (uint)(planeOffset + sizeof(uint) - 1) ||
            !memory.TryReadLong(
                bitmap.Address + (uint)planeOffset,
                out address))
        {
            return false;
        }

        return true;
    }

    private static bool RangesOverlap(
        uint firstAddress,
        ulong firstLength,
        uint secondAddress,
        ulong secondLength)
    {
        var firstEnd = (ulong)firstAddress + firstLength;
        var secondEnd = (ulong)secondAddress + secondLength;
        return (ulong)firstAddress < secondEnd &&
               (ulong)secondAddress < firstEnd;
    }

    private static bool PlaneRangesOverlap(
        GraphicsRasterOperations.BitmapInfo first,
        uint firstAddress,
        GraphicsRasterOperations.BitmapInfo second,
        uint secondAddress,
        ulong firstSpan,
        ulong secondSpan)
    {
        if (!RangesOverlap(firstAddress, firstSpan, secondAddress, secondSpan))
            return false;

        // A bounding span is exact for ordinary planar storage.  For two
        // row-interleaved bitmaps it can over-report overlap because the
        // bytes belonging to a plane are separated by the aggregate stride;
        // compare the touched row intervals instead.
        if ((first.Flags & (1 << 2)) == 0 || (second.Flags & (1 << 2)) == 0)
            return true;

        var firstStride = GraphicsRasterOperations.GetBitmapPlaneRowStride(first);
        var secondStride = GraphicsRasterOperations.GetBitmapPlaneRowStride(second);
        var firstRow = 0;
        var secondRow = 0;
        while (firstRow < first.Rows && secondRow < second.Rows)
        {
            var firstStart = (ulong)firstAddress +
                ((ulong)(uint)firstRow * (uint)firstStride);
            var secondStart = (ulong)secondAddress +
                ((ulong)(uint)secondRow * (uint)secondStride);
            var firstEnd = firstStart + (uint)first.PlaneBytesPerRow;
            var secondEnd = secondStart + (uint)second.PlaneBytesPerRow;
            if (firstStart < secondEnd && secondStart < firstEnd)
                return true;

            if (firstEnd <= secondStart)
                firstRow++;
            else
                secondRow++;
        }

        return false;
    }

    private readonly struct BitScaleArgs
    {
        internal BitScaleArgs(
            ushort srcX,
            ushort srcY,
            ushort srcWidth,
            ushort srcHeight,
            ushort xSrcFactor,
            ushort ySrcFactor,
            ushort destX,
            ushort destY,
            ushort xDestFactor,
            ushort yDestFactor,
            uint srcBitMap,
            uint destBitMap,
            uint flags)
        {
            SrcX = srcX;
            SrcY = srcY;
            SrcWidth = srcWidth;
            SrcHeight = srcHeight;
            XSrcFactor = xSrcFactor;
            YSrcFactor = ySrcFactor;
            DestX = destX;
            DestY = destY;
            XDestFactor = xDestFactor;
            YDestFactor = yDestFactor;
            SrcBitMap = srcBitMap;
            DestBitMap = destBitMap;
            Flags = flags;
        }

        internal ushort SrcX { get; }
        internal ushort SrcY { get; }
        internal ushort SrcWidth { get; }
        internal ushort SrcHeight { get; }
        internal ushort XSrcFactor { get; }
        internal ushort YSrcFactor { get; }
        internal ushort DestX { get; }
        internal ushort DestY { get; }
        internal ushort XDestFactor { get; }
        internal ushort YDestFactor { get; }
        internal uint SrcBitMap { get; }
        internal uint DestBitMap { get; }
        internal uint Flags { get; }
    }
}
