using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Portable planar implementations of the V36 chunky/planar pixel helpers.
///
/// The Kickstart routines describe the source array as a byte-per-pen buffer
/// whose rows are padded to a 16-pixel boundary.  The portable path keeps that
/// guest ABI, but performs the conversion directly against the guest BitMap;
/// the temporary RastPort is therefore an explicit compatibility parameter,
/// not a host object that is dereferenced here.  Layer and RTG ownership is
/// decided by the register adapter before these pure operations are entered.
/// </summary>
internal static class GraphicsPixelArrayOperations
{
    private const int PixelsPerArrayWord = 16;
    // The portable helpers preflight the padded guest array, stage caller
    // bytes, snapshot planar state, and then publish the conversion. Keep
    // those potentially full-rectangle passes inside the same bounded host
    // work contract used by other transactional planar primitives. A native
    // or provider owner can still handle a larger valid guest request after
    // the portable path declines it.
    private const ulong PortableWorkLimit = int.MaxValue;
    private const ulong PixelArrayPassesPerPixel = 5;
    private const ulong PixelArrayPassesPerArrayByte = 2;

    /// <summary>
    /// Status-aware callers use the same padded row geometry as the portable
    /// conversion bodies when deciding whether an explicit temporary
    /// RastPort belongs to this implementation.  Invalid/empty geometry is
    /// left to the normal vector result path; only a valid positive width
    /// produces a workspace stride here.
    /// </summary>
    internal static bool TryGetPaddedStrideForStatus(int width, out uint stride)
    {
        stride = 0;
        return width > 0 && TryGetPaddedStride((uint)width, out stride);
    }

    /// <summary>
    /// Keeps malformed optional temporary workspaces behind the native/
    /// provider boundary without changing the classic -1 result for an
    /// otherwise valid RastPort whose source array or geometry is invalid.
    /// </summary>
    internal static bool TryCanAddressArrayForStatus(
        IGraphicsMemory memory,
        uint array,
        uint stride,
        uint rows,
        uint logicalWidth)
        => CanAddressArray(memory, array, stride, rows, logicalWidth);

    /// <summary>
    /// V40 WriteChunkyPixels advances by the caller's explicit row stride but
    /// consumes only the logical pixel bytes in each row.  Keep its status
    /// admission distinct from the V36 RASSIZE helpers: padding contributes to
    /// address arithmetic and the portable-work bound, but unreadable padding
    /// is not itself a reason to decline a request whose pixel bytes are
    /// readable.
    /// </summary>
    internal static bool TryCanAddressLogicalArrayForStatus(
        IGraphicsMemory memory,
        uint array,
        uint stride,
        uint rows,
        uint logicalWidth)
        => CanAddressLogicalArray(memory, array, stride, rows, logicalWidth);

    private static bool CanAddressLogicalArray(
        IGraphicsMemory memory,
        uint array,
        uint stride,
        uint rows,
        uint logicalWidth)
    {
        if (stride < logicalWidth || stride == 0 || rows == 0 ||
            logicalWidth == 0)
        {
            return false;
        }

        var logicalPixels = (ulong)logicalWidth * rows;
        if (logicalPixels == 0 || logicalPixels > int.MaxValue)
            return false;

        var lastLogicalOffset = (ulong)(rows - 1u) * stride +
            logicalWidth - 1u;
        if (lastLogicalOffset > uint.MaxValue ||
            array > uint.MaxValue - (uint)lastLogicalOffset)
        {
            return false;
        }

        for (var row = 0u; row < rows; row++)
        {
            for (var column = 0u; column < logicalWidth; column++)
            {
                if (!TryArrayAddress(array, row, stride, column, out var address) ||
                    !memory.TryReadByte(address, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool TryCanCompletePortableWorkForStatus(
        uint width,
        uint height,
        uint stride)
        => FitsPortableWork(width, height, stride);

    internal static bool TryValidateTemporaryRastPortForStatus(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo sourceBitmap,
        uint temporaryRastPort,
        uint chunkyStride)
    {
        if (temporaryRastPort == 0)
            return true;

        return TryValidateTemporaryRastPort(memory, temporaryRastPort, sourceBitmap, chunkyStride);
    }

    internal static int ReadPixelLine8(
        IGraphicsMemory memory,
        uint rastPort,
        int xStart,
        int yStart,
        int width,
        uint array,
        uint temporaryRastPort)
    {
        _ = temporaryRastPort;
        if (!GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
            return GraphicsRasterOperations.Failure;

        if (width < 0)
            return GraphicsRasterOperations.Failure;

        if (!TryGetPaddedStride((uint)width, out var stride))
            return GraphicsRasterOperations.Failure;
        if (width == 0)
            return GraphicsRasterOperations.Success;

        if (!FitsPortableWork((uint)width, 1, stride))
            return GraphicsRasterOperations.Failure;

        if (array == 0 || !CanAddressArray(memory, array, stride, 1, (uint)width))
            return GraphicsRasterOperations.Failure;

        if (!TryValidateTemporaryRastPort(memory, temporaryRastPort, bitmap, stride))
            return GraphicsRasterOperations.Failure;

        return ReadRows(
            memory,
            bitmap,
            (int)xStart,
            (int)yStart,
            (uint)width,
            1,
            stride,
            array);
    }

    internal static int WritePixelLine8(
        IGraphicsMemory memory,
        uint rastPort,
        int xStart,
        int yStart,
        int width,
        uint array,
        uint temporaryRastPort,
        Func<int, int, bool>? pixelVisible = null)
    {
        _ = temporaryRastPort;
        if (!GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
            return GraphicsRasterOperations.Failure;

        if (width < 0)
            return GraphicsRasterOperations.Failure;

        if (!TryGetPaddedStride((uint)width, out var stride))
            return GraphicsRasterOperations.Failure;
        if (width == 0)
            return GraphicsRasterOperations.Success;

        if (!GraphicsRasterOperations.TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return GraphicsRasterOperations.Failure;
        }

        // Only planes declared by the linked BitMap can be selected.  A
        // mask containing exclusively higher bits is therefore the same
        // bounded successful no-op as an explicit zero mask; normalize it
        // before staging the caller-owned source array or probing planar
        // destination rows.
        writeMask = GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask);

        // No destination planes are selected.  Keep the bounded successful
        // no-op (do not validate/stage a potentially enormous source array or
        // walk destination rows that cannot change).  Since no destination
        // pixel can be plotted, the V36 line result is zero rather than the
        // requested width.
        if (writeMask == 0)
            return 0;

        if (!FitsPortableWork((uint)width, 1, stride))
            return GraphicsRasterOperations.Failure;

        var hasVisibleDestination = false;
        if (pixelVisible is not null &&
            !TryHasVisibleDestination(
                bitmap,
                xStart,
                yStart,
                (uint)width,
                1,
                pixelVisible,
                out hasVisibleDestination))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (pixelVisible is not null && !hasVisibleDestination)
            return GraphicsRasterOperations.Success;

        if (array == 0 || !CanAddressArray(memory, array, stride, 1, (uint)width))
            return GraphicsRasterOperations.Failure;

        if (!TryValidateTemporaryRastPort(memory, temporaryRastPort, bitmap, stride))
            return GraphicsRasterOperations.Failure;

        return WriteRows(
            memory,
            bitmap,
            (int)xStart,
            (int)yStart,
            (uint)width,
            1,
            stride,
            array,
            writeMask,
            pixelVisible);
    }

    internal static int ReadPixelArray8(
        IGraphicsMemory memory,
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        uint temporaryRastPort)
    {
        _ = temporaryRastPort;
        if (xStop < xStart || yStop < yStart ||
            !GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return GraphicsRasterOperations.Failure;
        }

        var width64 = (long)xStop - xStart + 1L;
        var height64 = (long)yStop - yStart + 1L;
        if (width64 <= 0 || height64 <= 0 ||
            width64 > uint.MaxValue || height64 > uint.MaxValue)
            return GraphicsRasterOperations.Failure;

        var width = (uint)width64;
        var height = (uint)height64;
        if (!TryGetPaddedStride(width, out var stride))
            return GraphicsRasterOperations.Failure;
        if (width == 0 || height == 0)
            return GraphicsRasterOperations.Success;

        if (!FitsPortableWork(width, height, stride))
            return GraphicsRasterOperations.Failure;

        if (array == 0 || !CanAddressArray(memory, array, stride, height, width))
            return GraphicsRasterOperations.Failure;

        if (!TryValidateTemporaryRastPort(memory, temporaryRastPort, bitmap, stride))
            return GraphicsRasterOperations.Failure;

        return ReadRows(
            memory,
            bitmap,
            xStart,
            yStart,
            width,
            height,
            stride,
            array);
    }

    internal static int WritePixelArray8(
        IGraphicsMemory memory,
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        uint temporaryRastPort,
        Func<int, int, bool>? pixelVisible = null)
    {
        _ = temporaryRastPort;
        if (xStop < xStart || yStop < yStart ||
            !GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!GraphicsRasterOperations.TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return GraphicsRasterOperations.Failure;
        }

        // Keep the V36 array writer on the same effective-mask contract as
        // the scalar/line drawing primitives.  Bits above bm_Depth cannot
        // publish a planar byte, so they must not force source staging or
        // destination validation for a request that is observationally a
        // successful no-op.
        writeMask = GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask);

        var width64 = (long)xStop - xStart + 1L;
        var height64 = (long)yStop - yStart + 1L;
        if (width64 <= 0 || height64 <= 0 ||
            width64 > uint.MaxValue || height64 > uint.MaxValue)
            return GraphicsRasterOperations.Failure;

        var width = (uint)width64;
        var height = (uint)height64;
        if (!TryGetPaddedStride(width, out var stride))
            return GraphicsRasterOperations.Failure;
        if (width == 0 || height == 0)
            return GraphicsRasterOperations.Success;

        // A zero write mask cannot publish any planar destination byte.  Keep
        // the public operation successful with its zero plotted-count result,
        // while avoiding source-array staging for a large masked request.
        if (writeMask == 0)
            return GraphicsRasterOperations.Success;

        if (!FitsPortableWork(width, height, stride))
            return GraphicsRasterOperations.Failure;

        var hasVisibleDestination = false;
        if (pixelVisible is not null &&
            !TryHasVisibleDestination(
                bitmap,
                xStart,
                yStart,
                width,
                height,
                pixelVisible,
                out hasVisibleDestination))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (pixelVisible is not null && !hasVisibleDestination)
            return GraphicsRasterOperations.Success;

        if (array == 0 || !CanAddressArray(memory, array, stride, height, width))
            return GraphicsRasterOperations.Failure;

        if (!TryValidateTemporaryRastPort(memory, temporaryRastPort, bitmap, stride))
            return GraphicsRasterOperations.Failure;

        return WriteRows(
            memory,
            bitmap,
            xStart,
            yStart,
            width,
            height,
            stride,
            array,
            writeMask,
            pixelVisible);
    }

    internal static int WriteChunkyPixels(
        IGraphicsMemory memory,
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        int bytesPerRow,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (xStop < xStart || yStop < yStart ||
            !GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!GraphicsRasterOperations.TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return GraphicsRasterOperations.Failure;
        }

        // The V40 chunky writer shares the same effective destination-mask
        // rule.  Normalize before the no-op fast path so a high-only mask
        // cannot turn an otherwise bounded call into a multi-row source or
        // plane walk.
        writeMask = GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask);

        var width64 = (long)xStop - xStart + 1L;
        var height64 = (long)yStop - yStart + 1L;
        if (width64 <= 0 || height64 <= 0 ||
            width64 > uint.MaxValue || height64 > uint.MaxValue ||
            bytesPerRow <= 0 ||
            (ulong)(uint)bytesPerRow < (ulong)width64)
        {
            return GraphicsRasterOperations.Failure;
        }

        var width = (uint)width64;
        var height = (uint)height64;
        // Validate the caller's row geometry before applying the mask fast
        // path; no source bytes or destination planes are needed once the
        // request is known to be a successful zero-mask no-op.
        if (writeMask == 0)
            return GraphicsRasterOperations.Success;

        if (!FitsPortableWork(width, height, (uint)bytesPerRow))
            return GraphicsRasterOperations.Failure;

        var hasVisibleDestination = false;
        if (pixelVisible is not null &&
            !TryHasVisibleDestination(
                bitmap,
                xStart,
                yStart,
                width,
                height,
                pixelVisible,
                out hasVisibleDestination))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (pixelVisible is not null && !hasVisibleDestination)
            return GraphicsRasterOperations.Success;

        if (array == 0 ||
            !CanAddressLogicalArray(memory, array, (uint)bytesPerRow, height, width))
            return GraphicsRasterOperations.Failure;

        return WriteRows(
            memory,
            bitmap,
            xStart,
            yStart,
            width,
            height,
            (uint)bytesPerRow,
            array,
            writeMask,
            pixelVisible);
    }

    private static int ReadRows(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int xStart,
        int yStart,
        uint width,
        uint height,
        uint stride,
        uint array)
    {
        // Complete the guest-array and planar-source probes before exposing
        // any output byte.  A truncated array or a malformed later plane
        // must not leave a caller with a prefix that looks valid.
        if (!PreflightArray(memory, array, stride, width, height) ||
            !PreflightBitmapRows(memory, bitmap, xStart, yStart, width, height))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!TrySnapshotArray(
                memory,
                array,
                stride,
                width,
                height,
                out var arraySnapshotAddresses,
                out var arraySnapshotValues))
            return GraphicsRasterOperations.Failure;

        // The caller-owned chunky array is allowed to alias the source
        // bitmap envelope.  Do not publish the first output byte while later
        // samples still depend on the same planar source byte: a write to an
        // aliased array can otherwise change a future bit and make the result
        // depend on traversal order.  Stage the complete logical sample set
        // first, then perform the output pass transactionally.
        var logicalPixels = (ulong)width * height;
        if (logicalPixels == 0 || logicalPixels > int.MaxValue)
            return GraphicsRasterOperations.Failure;

        var colors = new byte[(int)logicalPixels];
        // The array result is the number of samples that were actually read
        // from the clipped RastPort.  ReadPixelLine8 uses this same V36
        // count contract: its result is not the requested width when the
        // horizontal span extends outside the bitmap.  The source array
        // still describes the complete logical request, so out-of-raster
        // samples are consumed and made deterministic either way.
        var count = 0;
        for (var row = 0u; row < height; row++)
        {
            var y = (long)yStart + row;
            for (var column = 0u; column < width; column++)
            {
                var index = checked((int)(row * width + column));
                var x = (long)xStart + column;
                if (!TryArrayAddress(array, row, stride, column, out _))
                    return GraphicsRasterOperations.Failure;

                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
                {
                    colors[index] = 0;
                    continue;
                }

                if (!GraphicsRasterOperations.TryReadBitmapPixel(
                        memory,
                        bitmap,
                        (int)x,
                        (int)y,
                        out var color))
                    return GraphicsRasterOperations.Failure;

                colors[index] = unchecked((byte)color);
                count++;
            }
        }

        for (var row = 0u; row < height; row++)
        {
            for (var column = 0u; column < width; column++)
            {
                var index = checked((int)(row * width + column));
                if (!TryArrayAddress(array, row, stride, column, out var pixelAddress) ||
                    !memory.TryWriteByte(pixelAddress, (byte)colors[index]))
                {
                    RestoreSnapshot(
                        memory,
                        arraySnapshotAddresses,
                        arraySnapshotValues);
                    return GraphicsRasterOperations.Failure;
                }
            }
        }

        return count;
    }

    private static int WriteRows(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int xStart,
        int yStart,
        uint width,
        uint height,
        uint stride,
        uint array,
        byte writeMask,
        Func<int, int, bool>? pixelVisible = null)
    {
        // Read all source bytes and validate every destination pixel before
        // the first planar write.  This keeps malformed guest input from
        // partially changing a multi-plane raster.
        if (!TrySnapshotArray(
                memory,
                array,
                stride,
                width,
                height,
                out _,
                out var sourcePixels) ||
            !PreflightBitmapRows(
                memory,
                bitmap,
                xStart,
                yStart,
                width,
                height,
                pixelVisible))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!TrySnapshotBitmapDestination(
                memory,
                bitmap,
                xStart,
                yStart,
                width,
                height,
                writeMask,
                pixelVisible,
                out var destinationSnapshotAddresses,
                out var destinationSnapshotValues))
        {
            return GraphicsRasterOperations.Failure;
        }

        // Array, chunky, and WritePixelLine8 writers report the number of
        // pixels actually plotted after the destination raster clips the
        // requested rectangle. Source bytes for clipped samples are always
        // consumed from the caller's padded array.
        var count = 0;
        for (var row = 0u; row < height; row++)
        {
            var y = (long)yStart + row;
            for (var column = 0u; column < width; column++)
            {
                var x = (long)xStart + column;
                var color = sourcePixels[(int)(row * width + column)];

                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
                {
                    continue;
                }

                if (pixelVisible is not null &&
                    !pixelVisible((int)x, (int)y))
                {
                    continue;
                }

                if (!GraphicsRasterOperations.SetBitmapPixel(memory, bitmap, (int)x, (int)y, color, 0, writeMask))
                {
                    RestoreSnapshot(
                        memory,
                        destinationSnapshotAddresses,
                        destinationSnapshotValues);
                    return GraphicsRasterOperations.Failure;
                }

                count++;
            }
        }

        return count;
    }

    private static bool TrySnapshotBitmapDestination(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int xStart,
        int yStart,
        uint width,
        uint height,
        byte writeMask,
        Func<int, int, bool>? pixelVisible,
        out List<uint> snapshotAddresses,
        out List<byte> snapshotValues)
    {
        snapshotAddresses = new List<uint>();
        snapshotValues = new List<byte>();
        if (width == 0 || height == 0 || writeMask == 0)
            return true;

        var right = (long)xStart + width - 1L;
        var bottom = (long)yStart + height - 1L;
        var left = Math.Max(0L, xStart);
        var top = Math.Max(0L, yStart);
        var clippedRight = Math.Min((long)bitmap.Width - 1L, right);
        var clippedBottom = Math.Min((long)bitmap.Rows - 1L, bottom);
        if (left > clippedRight || top > clippedBottom)
            return true;

        return GraphicsRasterOperations.TrySnapshotBitmapRegion(
            memory,
            bitmap,
            (int)left,
            (int)top,
            (int)clippedRight,
            (int)clippedBottom,
            writeMask,
            out snapshotAddresses,
            out snapshotValues,
            pixelVisible: pixelVisible);
    }

    private static bool TrySnapshotArray(
        IGraphicsMemory memory,
        uint array,
        uint stride,
        uint width,
        uint height,
        out List<uint> snapshotAddresses,
        out List<byte> snapshotValues)
    {
        snapshotAddresses = new List<uint>();
        snapshotValues = new List<byte>();
        for (var row = 0u; row < height; row++)
        {
            for (var column = 0u; column < width; column++)
            {
                if (!TryArrayAddress(array, row, stride, column, out var address) ||
                    !memory.TryReadByte(address, out var value))
                {
                    return false;
                }

                snapshotAddresses.Add(address);
                snapshotValues.Add(value);
            }
        }

        return true;
    }

    private static void RestoreSnapshot(
        IGraphicsMemory memory,
        IReadOnlyList<uint> addresses,
        IReadOnlyList<byte> values)
    {
        for (var index = addresses.Count - 1; index >= 0; index--)
            _ = memory.TryWriteByte(addresses[index], values[index]);
    }

    private static bool PreflightArray(
        IGraphicsMemory memory,
        uint array,
        uint stride,
        uint width,
        uint height)
    {
        for (var row = 0u; row < height; row++)
        {
            for (var column = 0u; column < width; column++)
            {
                if (!TryArrayAddress(array, row, stride, column, out var address) ||
                    !memory.TryReadByte(address, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool PreflightBitmapRows(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int xStart,
        int yStart,
        uint width,
        uint height,
        Func<int, int, bool>? pixelVisible = null)
    {
        for (var row = 0u; row < height; row++)
        {
            var y = (long)yStart + row;
            for (var column = 0u; column < width; column++)
            {
                var x = (long)xStart + column;
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
                    continue;

                if (pixelVisible is not null &&
                    !pixelVisible((int)x, (int)y))
                {
                    continue;
                }

                if (!GraphicsRasterOperations.TryProbeBitmapPixel(
                        memory,
                        bitmap,
                        (int)x,
                        (int)y))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryHasVisibleDestination(
        GraphicsRasterOperations.BitmapInfo bitmap,
        int xStart,
        int yStart,
        uint width,
        uint height,
        Func<int, int, bool> pixelVisible,
        out bool hasVisible)
    {
        hasVisible = false;
        if (width == 0 || height == 0)
            return true;

        var right = (long)xStart + width - 1L;
        var bottom = (long)yStart + height - 1L;
        var left = Math.Max(0L, xStart);
        var top = Math.Max(0L, yStart);
        var clippedRight = Math.Min((long)bitmap.Width - 1L, right);
        var clippedBottom = Math.Min((long)bitmap.Rows - 1L, bottom);
        if (left > clippedRight || top > clippedBottom)
            return true;

        var cells = (ulong)(clippedRight - left + 1L) *
            (ulong)(clippedBottom - top + 1L);
        if (cells > PortableWorkLimit || cells > int.MaxValue)
            return false;

        for (var y = (int)top; y <= clippedBottom; y++)
        {
            for (var x = (int)left; x <= clippedRight; x++)
            {
                if (pixelVisible(x, y))
                {
                    hasVisible = true;
                    return true;
                }
            }
        }

        return true;
    }

    private static bool TryGetPaddedStride(uint width, out uint stride)
    {
        stride = 0;
        // The public array contract rounds each row up to a 16-pixel
        // boundary.  Keep the addition widened so a maximal guest rectangle
        // cannot wrap to a small stride and accidentally pass the later
        // address/envelope checks.
        var padded = (ulong)width + (PixelsPerArrayWord - 1u);
        var rounded = padded & ~((ulong)PixelsPerArrayWord - 1UL);
        if (rounded == 0 || rounded > uint.MaxValue)
            return width == 0;

        stride = (uint)rounded;
        return stride >= width;
    }

    private static bool FitsPortableWork(uint width, uint height, uint stride)
    {
        if (width == 0 || height == 0 || stride < width || stride == 0)
            return false;

        var logicalPixels = (ulong)width * height;
        var arrayBytes = (ulong)stride * height;
        if (logicalPixels == 0 || arrayBytes == 0 ||
            logicalPixels > PortableWorkLimit / PixelArrayPassesPerPixel)
        {
            return false;
        }

        var pixelWork = logicalPixels * PixelArrayPassesPerPixel;
        return arrayBytes <=
            (PortableWorkLimit - pixelWork) / PixelArrayPassesPerArrayByte;
    }

    private static bool CanAddressArray(
        IGraphicsMemory memory,
        uint array,
        uint stride,
        uint rows,
        uint logicalWidth)
    {
        // Kickstart's RASSIZE contract reserves the complete padded row, not
        // just logicalWidth bytes.  Validate that full guest envelope before
        // conversion so a native/temp-RastPort implementation cannot walk
        // into an unmapped padding byte after the portable path has claimed
        // the vector.
        if (stride < logicalWidth || stride == 0 || rows == 0)
            return false;

        var pixels = (ulong)logicalWidth * rows;
        if (pixels == 0 || pixels > int.MaxValue)
            return false;

        var envelopeBytes = (ulong)stride * rows;
        if (envelopeBytes == 0 ||
            envelopeBytes > int.MaxValue ||
            envelopeBytes > uint.MaxValue ||
            array > uint.MaxValue - (uint)(envelopeBytes - 1))
        {
            return false;
        }

        for (var row = 0u; row < rows; row++)
        {
            if (!TryArrayAddress(array, row, stride, stride - 1u, out var rowEnd))
                return false;

            var rowStart = rowEnd - (stride - 1u);
            for (var offset = 0u; offset < stride; offset++)
            {
                if (!memory.TryReadByte(rowStart + offset, out _))
                    return false;
            }
        }

        return true;
    }

    private static bool TryValidateTemporaryRastPort(
        IGraphicsMemory memory,
        uint temporaryRastPort,
        GraphicsRasterOperations.BitmapInfo sourceBitmap,
        uint chunkyStride)
    {
        // The host path historically accepted a null temporary RastPort and
        // performed the conversion directly against the destination bitmap.
        // Keep that transitional form, but when the caller supplies the
        // documented helper RastPort, validate its complete one-row planar
        // workspace before claiming the vector.
        if (temporaryRastPort == 0)
            return true;

        // The optional workspace is still a native RastPort structure.  Its
        // Layer and BitMap links are consumed with 68k longword accesses, so
        // an odd guest base must remain available to the resident/provider
        // owner even when the host memory adapter exposes unaligned bytes.
        if ((temporaryRastPort & 1u) != 0 ||
            chunkyStride == 0 || (chunkyStride & 0x0Fu) != 0 ||
            temporaryRastPort > uint.MaxValue -
                (uint)(GraphicsLayouts.RastPortBitMap + sizeof(uint) - 1) ||
            !memory.TryReadLong(
                temporaryRastPort + (uint)GraphicsLayouts.RastPortLayer,
                out var layer) ||
            layer != 0 ||
            !memory.TryReadLong(
                temporaryRastPort + (uint)GraphicsLayouts.RastPortBitMap,
                out var bitMap) ||
            bitMap == 0 ||
            (bitMap & 1u) != 0 ||
            bitMap > uint.MaxValue - (uint)GraphicsLayouts.BitMapDepth ||
            !memory.TryReadWord(
                bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow,
                out var bytesPerRow) ||
            !memory.TryReadWord(
                bitMap + (uint)GraphicsLayouts.BitMapRows,
                out var rows) ||
            !memory.TryReadByte(
                bitMap + (uint)GraphicsLayouts.BitMapDepth,
                out var depth))
        {
            return false;
        }

        // The classic helper uses one temporary planar row.  chunkyStride is
        // measured in pixels/bytes, while BitMap.BytesPerRow is measured in
        // 16-bit planar words: one byte per eight pixels.
        var expectedBytesPerRow = chunkyStride / 8u;
        if (expectedBytesPerRow == 0 ||
            bytesPerRow != expectedBytesPerRow ||
            rows != 1 ||
            depth != sourceBitmap.Depth)
        {
            return false;
        }

        for (var plane = 0; plane < depth; plane++)
        {
            var planeField = (ulong)bitMap +
                (uint)GraphicsLayouts.BitMapPlanes +
                (uint)(plane * 4);
            if (planeField > uint.MaxValue - 4u ||
                !memory.TryReadLong((uint)planeField, out var planeAddress) ||
                planeAddress == 0 ||
                (planeAddress & 1u) != 0 ||
                !TryProbeTemporaryPlane(memory, planeAddress, bytesPerRow))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryProbeTemporaryPlane(
        IGraphicsMemory memory,
        uint address,
        ushort bytesPerRow)
    {
        if (bytesPerRow == 0 ||
            address > uint.MaxValue - ((uint)bytesPerRow - 1u))
        {
            return false;
        }

        for (var offset = 0u; offset < bytesPerRow; offset++)
        {
            if (!memory.TryReadByte(address + offset, out _))
                return false;
        }

        return true;
    }

    private static bool TryArrayAddress(
        uint array,
        uint row,
        uint stride,
        uint column,
        out uint address)
    {
        address = 0;
        var offset = ((ulong)row * stride) + column;
        if (array == 0 || offset > uint.MaxValue || array > uint.MaxValue - (uint)offset)
            return false;

        address = array + (uint)offset;
        return true;
    }
}
