using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Non-escaping workspace for a host that serializes BltBitMap calls. The
/// owner must keep one instance per independently re-entrant execution path.
/// Buffers grow during warm-up and are then reused without managed allocation.
/// </summary>
internal sealed class GraphicsBlitScratch
{
    private int[] _sourcePixels = Array.Empty<int>();
    private int[] _destinationPixels = Array.Empty<int>();
    private bool[] _templateBits = Array.Empty<bool>();
    private readonly List<uint> _snapshotAddresses = new();
    private readonly List<byte> _snapshotValues = new();
    internal GraphicsRasterOperations.VisibilityScratch Visibility { get; } = new();
    private bool _acquired;

    internal bool TryAcquire(
        int cellCount,
        out int[] sourcePixels,
        out int[] destinationPixels,
        out List<uint> snapshotAddresses,
        out List<byte> snapshotValues)
    {
        sourcePixels = Array.Empty<int>();
        destinationPixels = Array.Empty<int>();
        snapshotAddresses = _snapshotAddresses;
        snapshotValues = _snapshotValues;
        if (_acquired || cellCount <= 0)
            return false;

        _acquired = true;
        if (_sourcePixels.Length < cellCount)
            Array.Resize(ref _sourcePixels, cellCount);
        if (_destinationPixels.Length < cellCount)
            Array.Resize(ref _destinationPixels, cellCount);
        _snapshotAddresses.Clear();
        _snapshotValues.Clear();
        sourcePixels = _sourcePixels;
        destinationPixels = _destinationPixels;
        return true;
    }

    internal void Release()
    {
        _snapshotAddresses.Clear();
        _snapshotValues.Clear();
        _acquired = false;
    }

    internal bool TryAcquireTemplate(
        int cellCount,
        out bool[] templateBits,
        out List<uint> snapshotAddresses,
        out List<byte> snapshotValues)
    {
        templateBits = Array.Empty<bool>();
        snapshotAddresses = _snapshotAddresses;
        snapshotValues = _snapshotValues;
        if (_acquired || cellCount <= 0)
            return false;

        _acquired = true;
        if (_templateBits.Length < cellCount)
            Array.Resize(ref _templateBits, cellCount);
        _snapshotAddresses.Clear();
        _snapshotValues.Clear();
        templateBits = _templateBits;
        return true;
    }

    internal void Reset()
    {
        if (_acquired)
            return;
        _sourcePixels = Array.Empty<int>();
        _destinationPixels = Array.Empty<int>();
        _templateBits = Array.Empty<bool>();
        _snapshotAddresses.Clear();
        _snapshotValues.Clear();
        _snapshotAddresses.TrimExcess();
        _snapshotValues.TrimExcess();
    }
}

/// <summary>
/// Guarded planar bitmap blits for the native graphics.library path.
/// CyberGraphX and layered ClipBlit paths remain separate patch providers.
/// </summary>
internal static class GraphicsBlitOperations
{
    // BltBitMap stages source and destination pixels, snapshots the
    // destination, and then publishes the minterm result. Keep that
    // aggregate logical-cell work bounded before any TempA overlap probe,
    // scratch growth, or guest plane validation can turn a valid UWORD
    // rectangle into an unbounded host allocation/traversal.
    private const ulong PortableWorkLimit = int.MaxValue;
    // BltClear probes the complete byte span, snapshots it for rollback, and
    // then publishes the fill. Keep those three passes inside the same host
    // work contract before the first destination read or managed allocation.
    private const ulong BltClearPassesPerByte = 3;
    private const ulong BltBitMapPassesPerCell = 4;
    private const ulong BltMaskBitMapRastPortPassesPerCell = 5;
    private const ulong BltTemplatePassesPerCell = 3;

    internal static int BltClear(
        IGraphicsMemory memory,
        uint address,
        uint byteCount,
        uint flags)
    {
        var rowsMode = (flags & 0x2) != 0;
        var fillMode = (flags & 0x4) != 0;
        var bytesPerRow = rowsMode ? byteCount & 0xFFFFu : byteCount;
        var rows = rowsMode ? byteCount >> 16 : 1u;
        // The classic clear loop has no memory access when scalar bytecount is
        // zero.  Preserve that bounded no-op before validating the guest
        // pointer.  Row mode still validates its word-addressed memBlock
        // envelope before accepting a zero row/stride request, so a malformed
        // odd pointer remains available to native/provider ownership.
        if (!rowsMode && bytesPerRow == 0)
            return GraphicsRasterOperations.Success;
        if (rowsMode && (bytesPerRow == 0 || rows == 0))
            return address != 0 && (address & 1u) == 0
                ? GraphicsRasterOperations.Success
                : GraphicsRasterOperations.Failure;

        // The blitter consumes words in both forms.  In row mode the low
        // word is the bytes-per-row value, so it carries the same even-byte
        // contract as the scalar byte-count form; accepting an odd stride
        // would make the second row begin on an unaligned word boundary.
        if (address == 0 || (address & 1u) != 0 ||
            (bytesPerRow & 1u) != 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        var totalBytes = (ulong)bytesPerRow * rows;
        if (totalBytes == 0 || totalBytes > uint.MaxValue ||
            address > uint.MaxValue - (uint)(totalBytes - 1))
        {
            return GraphicsRasterOperations.Failure;
        }

        // Kickstart documents memBlock as local, blitter-accessible memory.
        // Keep this address-class admission optional for memory-only
        // CopperSharp68k adapters, while CopperStart/native adapters can leave
        // a readable fast-memory span available to their own implementation.
        if (memory is IGraphicsDisplayMemory displayMemory &&
            !displayMemory.IsDisplayDmaRange(address, (uint)totalBytes))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (totalBytes > PortableWorkLimit / BltClearPassesPerByte)
            return GraphicsRasterOperations.Failure;

        // The transactional clear snapshots the complete span into a
        // managed byte array. Reject a guest geometry that cannot be staged
        // before the preflight loops; otherwise a maximal 16-bit row request
        // would walk billions of host reads merely to reach this guard.
        if (totalBytes > int.MaxValue)
            return GraphicsRasterOperations.Failure;

        // BltClear is one guest operation.  Probe the complete destination
        // span before writing any byte so a truncated late row cannot expose
        // a partially cleared buffer.
        for (var row = 0u; row < rows; row++)
        {
            var rowAddress = address + (row * bytesPerRow);
            for (var offset = 0u; offset < bytesPerRow; offset++)
            {
                if (!memory.TryReadByte(rowAddress + offset, out _))
                    return GraphicsRasterOperations.Failure;
            }
        }

        if (!TrySnapshot(memory, address, (int)totalBytes, out var original))
            return GraphicsRasterOperations.Failure;

        var fillWord = fillMode ? (ushort)(flags >> 16) : (ushort)0;
        for (var row = 0u; row < rows; row++)
        {
            var rowAddress = address + (row * bytesPerRow);
            for (var offset = 0u; offset < bytesPerRow; offset++)
            {
                var value = fillMode
                        ? (byte)(((((row * bytesPerRow) + offset) & 1) == 0)
                        ? fillWord >> 8
                        : fillWord)
                    : (byte)0;
                if (!memory.TryWriteByte(rowAddress + offset, value))
                {
                    Restore(memory, address, original);
                    return GraphicsRasterOperations.Failure;
                }
            }
        }

        return GraphicsRasterOperations.Success;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        int size,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (size <= 0)
            return false;

        original = new byte[size];
        for (var offset = 0; offset < size; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out original[offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static void Restore(
        IGraphicsMemory memory,
        uint address,
        byte[] original)
    {
        for (var offset = 0; offset < original.Length; offset++)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
    }

    internal static int BltBitMap(
        IGraphicsMemory memory,
        uint sourceAddress,
        short sourceX,
        short sourceY,
        uint destinationAddress,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        byte mask,
        uint tempA,
        IGraphicsAllocatorBackend? allocator = null,
        GraphicsBlitScratch? scratch = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (pixelVisible is null)
        {
            return BltBitMapCore(
                memory, sourceAddress, sourceX, sourceY,
                destinationAddress, destinationX, destinationY,
                width, height, minterm, mask, tempA,
                allocator, scratch, null);
        }

        var visibility = scratch?.Visibility ??
            new GraphicsRasterOperations.VisibilityScratch();
        if (!visibility.TryAcquire(pixelVisible, out var stablePixelVisible))
            return GraphicsRasterOperations.Failure;

        try
        {
            return BltBitMapCore(
                memory, sourceAddress, sourceX, sourceY,
                destinationAddress, destinationX, destinationY,
                width, height, minterm, mask, tempA,
                allocator, scratch, stablePixelVisible);
        }
        finally
        {
            visibility.Release();
        }
    }

    private static int BltBitMapCore(
        IGraphicsMemory memory,
        uint sourceAddress,
        short sourceX,
        short sourceY,
        uint destinationAddress,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        byte mask,
        uint tempA,
        IGraphicsAllocatorBackend? allocator,
        GraphicsBlitScratch? scratch,
        Func<int, int, bool>? pixelVisible)
    {
        if (!TryReadBitmapHeader(memory, sourceAddress, out var source) ||
            !TryReadBitmapHeader(memory, destinationAddress, out var destination) ||
            width <= 0 || height <= 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        var planeLimit = Math.Min(source.Depth, destination.Depth);
        var participatingMask = (byte)(mask & (uint)((1 << planeLimit) - 1));
        var planeCount = CountBits(participatingMask);
        if (planeCount == 0)
            return 0;

        if (!TryClipRectangle(
                source,
                destination,
                sourceX,
                sourceY,
                destinationX,
                destinationY,
                width,
                height,
                out var clippedSourceX,
                out var clippedSourceY,
                out var clippedDestinationX,
                out var clippedDestinationY,
                out var clippedWidth,
                out var clippedHeight))
        {
            return 0;
        }

        if (!TryFitPortableWork(
                clippedWidth,
                clippedHeight,
                BltBitMapPassesPerCell))
        {
            return GraphicsRasterOperations.Failure;
        }

        // Layer/provider callers may own only a visible fragment of the
        // clipped destination. Establish that ownership before touching
        // source planes, destination planes, overlap workspace, or caller
        // TempA. A fully hidden blit is still a successful plane-count
        // result, but it must not claim any hidden planar storage.
        var hasVisibleDestination = false;
        if (pixelVisible is not null &&
            !TryHasVisibleDestination(
                clippedDestinationX,
                clippedDestinationY,
                clippedWidth,
                clippedHeight,
                pixelVisible,
                out hasVisibleDestination))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (pixelVisible is not null && !hasVisibleDestination)
            return planeCount;

        // The native blitter consumes TempA only when the participating
        // source and destination raster spans overlap.  A null TempA asks
        // graphics.library to obtain one word-aligned Chip scan line; a
        // caller-supplied workspace remains subject to the guest address and
        // readability checks below.  Non-overlapping blits intentionally do
        // not allocate or probe TempA because the hardware would not touch it.
        var spansOverlap = BlitSpansOverlap(
            memory,
            source,
            clippedSourceX,
            clippedSourceY,
            destination,
            clippedDestinationX,
            clippedDestinationY,
            clippedWidth,
            clippedHeight,
            participatingMask);
        var ownsTempA = false;
        var tempABytes = (uint)source.BytesPerRow;
        if (spansOverlap && tempA == 0 && allocator is not null && scratch is null)
        {
            if (!allocator.TryAllocate(
                    tempABytes,
                    GraphicsMemoryClass.Chip,
                    out tempA))
            {
                // Allocators may return a provisional address alongside a
                // false status.  Release it before exposing the decline so a
                // failed blit cannot leak a guest workspace.
                if (tempA != 0)
                    allocator.Free(tempA, tempABytes, GraphicsMemoryClass.Chip);

                return GraphicsRasterOperations.Failure;
            }

            if (tempA == 0 ||
                (tempA & 1u) != 0 ||
                tempABytes == 0 ||
                tempA > uint.MaxValue - (tempABytes - 1u))
            {
                if (tempA != 0)
                    allocator.Free(tempA, tempABytes, GraphicsMemoryClass.Chip);

                return GraphicsRasterOperations.Failure;
            }

            ownsTempA = true;
        }

        try
        {
            // A caller-provided workspace must be readable and aligned.  An
            // address returned by the allocator is already owned by this
            // operation, so probing it through the synthetic memory map would
            // incorrectly reject valid native/host allocator implementations.
            if (spansOverlap &&
                tempA != 0 &&
                !ownsTempA &&
                ((tempA & 1u) != 0 ||
                 !TryProbePlane(memory, tempA, source.BytesPerRow, 1)))
            {
                return GraphicsRasterOperations.Failure;
            }

            // Kickstart documents an overlapping caller-supplied TempA as a
            // chip-accessible scan-line workspace.  Keep the address-class
            // check optional so a memory-only CopperSharp68k adapter retains
            // its ordinary guest-range contract; an allocator-owned TempA is
            // checked too when CopperStart exposes the display-DMA sidecar.
            if (spansOverlap &&
                tempA != 0 &&
                memory is IGraphicsDisplayMemory displayMemory &&
                !displayMemory.IsDisplayDmaRange(tempA, tempABytes))
            {
                return GraphicsRasterOperations.Failure;
            }

            if (pixelVisible is null &&
                !TryValidatePlanes(memory, source, destination, participatingMask))
                return GraphicsRasterOperations.Failure;

        var cellCount = (long)clippedWidth * clippedHeight;
        if (cellCount <= 0 || cellCount > int.MaxValue)
            return GraphicsRasterOperations.Failure;

        int[] sourcePixels;
        int[] destinationPixels;
        List<uint>? snapshotAddressBuffer = null;
        List<byte>? snapshotValueBuffer = null;
        var scratchAcquired = false;
        if (scratch is null)
        {
            sourcePixels = new int[(int)cellCount];
            destinationPixels = new int[(int)cellCount];
        }
        else if (!scratch.TryAcquire(
                     (int)cellCount,
                     out sourcePixels,
                     out destinationPixels,
                     out var scratchAddresses,
                     out var scratchValues))
        {
            return GraphicsRasterOperations.Failure;
        }
        else
        {
            scratchAcquired = true;
            snapshotAddressBuffer = scratchAddresses;
            snapshotValueBuffer = scratchValues;
        }

        try
        {
            // Snapshot both inputs.  This preserves non-destructive source and
            // destination semantics when the bitmaps overlap or alias.
            for (var row = 0; row < clippedHeight; row++)
            {
                for (var column = 0; column < clippedWidth; column++)
                {
                    var index = (row * clippedWidth) + column;
                    if (pixelVisible is not null &&
                        !pixelVisible(
                            clippedDestinationX + column,
                            clippedDestinationY + row))
                    {
                        sourcePixels[index] = 0;
                        destinationPixels[index] = 0;
                        continue;
                    }

                    if (!TryReadPixel(memory, source, clippedSourceX + column, clippedSourceY + row, planeLimit, true, out sourcePixels[index]) ||
                        !TryReadPixel(memory, destination, clippedDestinationX + column, clippedDestinationY + row, planeLimit, false, out destinationPixels[index]))
                    {
                        return GraphicsRasterOperations.Failure;
                    }
                }
            }

            if (!GraphicsRasterOperations.TrySnapshotBitmapRegion(
                    memory,
                    destination,
                    clippedDestinationX,
                    clippedDestinationY,
                    clippedDestinationX + clippedWidth - 1,
                    clippedDestinationY + clippedHeight - 1,
                    participatingMask,
                    out var destinationSnapshotAddresses,
                    out var destinationSnapshotValues,
                    snapshotAddressBuffer,
                    snapshotValueBuffer,
                    pixelVisible))
            {
                return GraphicsRasterOperations.Failure;
            }

            for (var row = 0; row < clippedHeight; row++)
            {
                for (var column = 0; column < clippedWidth; column++)
                {
                    if (pixelVisible is not null &&
                        !pixelVisible(
                            clippedDestinationX + column,
                            clippedDestinationY + row))
                    {
                        continue;
                    }

                    var index = (row * clippedWidth) + column;
                    var sourcePixel = sourcePixels[index];
                    var destinationPixel = destinationPixels[index];
                    var output = destinationPixel;
                    for (var plane = 0; plane < planeLimit; plane++)
                    {
                        if ((participatingMask & (1 << plane)) == 0)
                            continue;

                        var sourceBit = (sourcePixel & (1 << plane)) != 0;
                        var destinationBit = (destinationPixel & (1 << plane)) != 0;
                        var outputBit = EvaluateMinterm(minterm, sourceBit, destinationBit);
                        if (outputBit)
                            output |= 1 << plane;
                        else
                            output &= ~(1 << plane);
                    }

                    if (!TryWritePixel(
                            memory,
                            destination,
                            clippedDestinationX + column,
                            clippedDestinationY + row,
                            output,
                            participatingMask))
                    {
                        GraphicsRasterOperations.RestoreBitmapSnapshot(
                            memory,
                            destinationSnapshotAddresses,
                            destinationSnapshotValues);
                        return GraphicsRasterOperations.Failure;
                    }
                }
            }

            return planeCount;
        }
        finally
        {
            if (scratchAcquired)
                scratch!.Release();
        }
        }
        finally
        {
            if (ownsTempA)
                allocator!.Free(tempA, tempABytes, GraphicsMemoryClass.Chip);
        }
    }

    internal static int ClipBlit(
        IGraphicsMemory memory,
        uint sourceRastPort,
        short sourceX,
        short sourceY,
        uint destinationRastPort,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        IGraphicsAllocatorBackend? allocator = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (!TryReadLayer(memory, sourceRastPort, out var sourceLayer) ||
            !TryReadLayer(memory, destinationRastPort, out var destinationLayer) ||
            sourceLayer != 0 || destinationLayer != 0 ||
            !GraphicsRasterOperations.TryReadBitmap(memory, sourceRastPort, out var source) ||
            !GraphicsRasterOperations.TryReadBitmap(memory, destinationRastPort, out var destination) ||
            !TryReadRastPortByte(
                memory,
                destinationRastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return GraphicsRasterOperations.Failure;
        }

        return BltBitMap(
            memory,
            source.Address,
            sourceX,
            sourceY,
            destination.Address,
            destinationX,
            destinationY,
            width,
            height,
            minterm,
            writeMask,
            0,
            allocator,
            pixelVisible: pixelVisible);
    }

    internal static int BltBitMapRastPort(
        IGraphicsMemory memory,
        uint sourceBitMap,
        short sourceX,
        short sourceY,
        uint destinationRastPort,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        IGraphicsAllocatorBackend? allocator = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (!TryReadLayer(memory, destinationRastPort, out var layer) ||
            layer != 0 ||
            !TryReadRastPortLong(
                memory,
                destinationRastPort,
                GraphicsLayouts.RastPortBitMap,
                out var destinationBitMap) ||
            !TryReadRastPortByte(
                memory,
                destinationRastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask) ||
            destinationBitMap == 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        return BltBitMap(
            memory,
            sourceBitMap,
            sourceX,
            sourceY,
            destinationBitMap,
            destinationX,
            destinationY,
            width,
            height,
            minterm,
            writeMask,
            0,
            allocator,
            pixelVisible: pixelVisible) == GraphicsRasterOperations.Failure
            ? GraphicsRasterOperations.Failure
            : GraphicsRasterOperations.Success;
    }

    internal static int BltMaskBitMapRastPort(
        IGraphicsMemory memory,
        uint sourceBitMap,
        short sourceX,
        short sourceY,
        uint destinationRastPort,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        uint blitMask,
        Func<int, int, bool>? pixelVisible = null)
    {
        // Resolve layer ownership before geometry.  For a non-layered void
        // vector, Kickstart returns successfully for an empty request before
        // admitting the external mask or either bitmap.  This keeps an empty
        // call source-free while still allowing a validated layer provider to
        // own the nonzero-layer form.
        if (!TryReadLayer(memory, destinationRastPort, out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return GraphicsRasterOperations.Failure;
        }

        // A null external mask is an invalid call even when the requested
        // rectangle is empty; preserve the existing ownership boundary for
        // that sentinel before taking the source-free geometry shortcut.
        if (blitMask == 0)
            return GraphicsRasterOperations.Failure;

        if (width <= 0 || height <= 0)
            return GraphicsRasterOperations.Success;

        if ((blitMask & 1u) != 0 ||
            layer != 0 ||
            !TryReadRastPortLong(
                memory,
                destinationRastPort,
                GraphicsLayouts.RastPortBitMap,
                out var destinationBitMap) ||
            !TryReadRastPortByte(
                memory,
                destinationRastPort,
                GraphicsLayouts.RastPortMask,
                out var rastPortWriteMask) ||
            destinationBitMap == 0 ||
            !TryReadBitmapHeader(memory, sourceBitMap, out var source) ||
            !TryReadBitmapHeader(memory, destinationBitMap, out var destination))
        {
            return GraphicsRasterOperations.Failure;
        }

        // Kickstart 3.1/V40 builds the temporary mask BitMap from the
        // aggregate source BytesPerRow when the source is interleaved.  Its
        // source width is therefore eight pixels per aggregate byte, rather
        // than eight pixels per linked plane row.  Preserve that historical
        // compatibility quirk here without changing the normal planar or
        // later graphics-library behavior.
        var maskBytesPerRow = source.BytesPerRow;
        var maskSource = source;
        if ((source.Flags & GraphicsRasterOperations.BmfInterleaved) != 0)
        {
            if (source.GuestBytesPerRow <= 0 ||
                source.GuestBytesPerRow > int.MaxValue / 8)
            {
                return GraphicsRasterOperations.Failure;
            }

            maskBytesPerRow = source.GuestBytesPerRow;
            maskSource = source.WithWidth(source.GuestBytesPerRow * 8);
        }

        var planeLimit = Math.Min(maskSource.Depth, destination.Depth);
        var commonPlaneMask = (byte)((1 << planeLimit) - 1);
        var participatingMask = (byte)(
            GraphicsRasterOperations.EffectiveWriteMask(destination, rastPortWriteMask) &
            commonPlaneMask);
        // BltMaskBitMapRastPort's documented cookie-copy minterm is $E0,
        // and its inverse-source form is $20. The portable path applies the
        // external mask as an explicit per-pixel gate, so reduce those
        // three-input forms to the equivalent two-input source/destination
        // minterms before evaluating each admitted pixel.
        var rasterMinterm = minterm switch
        {
            0xE0 => 0xC0u,
            0x20 => 0x30u,
            _ => minterm
        };
        if (!TryClipRectangle(
                maskSource,
                destination,
                sourceX,
                sourceY,
                destinationX,
                destinationY,
                width,
                height,
                out var clippedSourceX,
                out var clippedSourceY,
                out var clippedDestinationX,
                out var clippedDestinationY,
                out var clippedWidth,
                out var clippedHeight))
        {
            return 0;
        }

        if (!TryFitPortableWork(
                clippedWidth,
                clippedHeight,
                BltMaskBitMapRastPortPassesPerCell))
        {
            return GraphicsRasterOperations.Failure;
        }

        // The mask form shares the same layered destination transaction as
        // BltBitMap. Cache visibility by logical destination coordinate so
        // admission, staging, rollback snapshot, and publication cannot
        // disagree when a provider is stateful.
        Dictionary<long, bool>? visibilityCache =
            pixelVisible is null ? null : new Dictionary<long, bool>();
        bool IsProviderVisible(int pixelX, int pixelY)
        {
            if (pixelVisible is null)
                return true;

            var key = ((long)pixelX << 32) | (uint)pixelY;
            if (visibilityCache!.TryGetValue(key, out var visible))
                return visible;

            visible = pixelVisible(pixelX, pixelY);
            visibilityCache[key] = visible;
            return visible;
        }

        var hasVisibleDestination = false;
        if (pixelVisible is not null &&
            !TryHasVisibleDestination(
                clippedDestinationX,
                clippedDestinationY,
                clippedWidth,
                clippedHeight,
                IsProviderVisible,
                out hasVisibleDestination))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (pixelVisible is not null && !hasVisibleDestination)
            return GraphicsRasterOperations.Success;

        // The external one-bit blitMask gates pixels, while the destination
        // RastPort.Mask selects which common source/destination planes the
        // minterm may publish, as in Kickstart 3.1.
        //
        // Kickstart 3.1 documents this external plane as a chip-memory
        // resource.  A CopperStart memory adapter may know that address
        // class; keep the check optional so a memory-only CopperSharp68k
        // implementation retains its ordinary guest-range contract.
        var maskSpan = (ulong)(uint)maskBytesPerRow * (uint)maskSource.Rows;
        if (maskSpan == 0 ||
            maskSpan > uint.MaxValue ||
            blitMask > uint.MaxValue - (uint)(maskSpan - 1) ||
            (memory is IGraphicsDisplayMemory displayMemory &&
             !displayMemory.IsDisplayDmaRange(blitMask, (uint)maskSpan)))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (pixelVisible is null &&
            !TryProbeMask(memory, blitMask, maskBytesPerRow, maskSource.Rows))
            return GraphicsRasterOperations.Failure;

        if (pixelVisible is null &&
            !TryValidatePlanes(memory, maskSource, destination, participatingMask))
            return GraphicsRasterOperations.Failure;

        var cellCount = (long)clippedWidth * clippedHeight;
        if (cellCount <= 0 || cellCount > int.MaxValue)
            return GraphicsRasterOperations.Failure;

        var sourcePixels = new int[(int)cellCount];
        var destinationPixels = new int[(int)cellCount];
        var maskPixels = new bool[(int)cellCount];
        for (var row = 0; row < clippedHeight; row++)
        {
            for (var column = 0; column < clippedWidth; column++)
            {
                var index = (row * clippedWidth) + column;
                if (pixelVisible is not null &&
                    !IsProviderVisible(
                        clippedDestinationX + column,
                        clippedDestinationY + row))
                {
                    sourcePixels[index] = 0;
                    destinationPixels[index] = 0;
                    maskPixels[index] = false;
                    continue;
                }

                if (!TryReadPixelMasked(
                    memory,
                    maskSource,
                    clippedSourceX + column,
                        clippedSourceY + row,
                        planeLimit,
                        participatingMask,
                        true,
                        out sourcePixels[index]) ||
                    !TryReadPixelMasked(
                        memory,
                        destination,
                        clippedDestinationX + column,
                        clippedDestinationY + row,
                        planeLimit,
                        participatingMask,
                        false,
                        out destinationPixels[index]) ||
                    !TryReadMaskPixel(
                        memory,
                        blitMask,
                        maskBytesPerRow,
                        clippedSourceX + column,
                        clippedSourceY + row,
                        out maskPixels[index]))
                {
                    return GraphicsRasterOperations.Failure;
                }
            }
        }

        if (!GraphicsRasterOperations.TrySnapshotBitmapRegion(
            memory,
            destination,
            clippedDestinationX,
            clippedDestinationY,
            clippedDestinationX + clippedWidth - 1,
            clippedDestinationY + clippedHeight - 1,
            participatingMask,
            out var destinationSnapshotAddresses,
            out var destinationSnapshotValues,
            pixelVisible: pixelVisible is null ? null : IsProviderVisible))
        {
            return GraphicsRasterOperations.Failure;
        }

        for (var row = 0; row < clippedHeight; row++)
        {
            for (var column = 0; column < clippedWidth; column++)
            {
                var index = (row * clippedWidth) + column;
                if (pixelVisible is not null &&
                    !IsProviderVisible(
                        clippedDestinationX + column,
                        clippedDestinationY + row))
                {
                    continue;
                }

                if (!maskPixels[index])
                    continue;

                var sourcePixel = sourcePixels[index];
                var destinationPixel = destinationPixels[index];
                var output = destinationPixel;
                for (var plane = 0; plane < planeLimit; plane++)
                {
                    var sourceBit = (sourcePixel & (1 << plane)) != 0;
                    var destinationBit = (destinationPixel & (1 << plane)) != 0;
                    var outputBit = EvaluateMinterm(rasterMinterm, sourceBit, destinationBit);
                    if (outputBit)
                        output |= 1 << plane;
                    else
                        output &= ~(1 << plane);
                }

                if (!TryWritePixel(
                    memory,
                    destination,
                    clippedDestinationX + column,
                    clippedDestinationY + row,
                    output,
                    participatingMask))
                {
                    GraphicsRasterOperations.RestoreBitmapSnapshot(
                        memory,
                        destinationSnapshotAddresses,
                        destinationSnapshotValues);
                    return GraphicsRasterOperations.Failure;
                }
            }
        }

        return GraphicsRasterOperations.Success;
    }

    internal static int BltTemplate(
        IGraphicsMemory memory,
        uint templateAddress,
        short sourceX,
        short sourceModulo,
        uint rastPort,
        short destinationX,
        short destinationY,
        short width,
        short height,
        GraphicsBlitScratch? scratch = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        // Layer ownership is resolved before geometry, matching the other
        // clipped void blitters: a validated nonzero layer remains a provider
        // boundary even when the requested rectangle is empty.  Once that
        // boundary is known, an empty SizeX/SizeY rectangle is a successful
        // no-op and must not admit or probe the source template at all.
        if (!TryReadLayer(memory, rastPort, out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (width <= 0 || height <= 0)
            return GraphicsRasterOperations.Success;

        // RPF_NO_PENS is an optional provider/native colour-ownership hint.
        // Honor it when the Flags word is readable, but keep sparse planar
        // ports (which omit the hint) on the portable path.
        if (GraphicsRasterOperations.TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags) &&
            (flags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryReadRastPortByte(memory, rastPort, GraphicsLayouts.RastPortMask, out var writeMask))
        {
            return GraphicsRasterOperations.Failure;
        }

        writeMask = GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask);

        // With no destination planes selected, BltTemplate is a successful
        // void no-op after the public RastPort/BitMap envelope is valid.  Do
        // not allocate or decode a potentially enormous template rectangle;
        // nonzero-mask requests retain complete source/destination preflight.
        if (writeMask == 0)
            return GraphicsRasterOperations.Success;

        var drawMode = GraphicsRasterOperations.GetDrawMode(memory, rastPort);
        if (drawMode < 0)
            return GraphicsRasterOperations.Failure;

        // Native BltTemplate renders through the provider graphics context,
        // whose foreground/background colours replace the public RastPort
        // pens when RPF_NO_PENS is set.  The portable planar path has no such
        // private sidecar.  Treat Flags as an opportunistic provider hint so
        // sparse guests that do not expose this internal word retain the
        // resident public-pen envelope, but decline before FgPen/BgPen reads
        // when the ownership bit is readable and set.
        var left = Math.Max(0, -(int)destinationX);
        var top = Math.Max(0, -(int)destinationY);
        var right = Math.Min((int)width, bitmap.Width - (int)destinationX);
        var bottom = Math.Min((int)height, bitmap.Rows - (int)destinationY);
        if (left >= right || top >= bottom)
            return GraphicsRasterOperations.Success;

        // The source template is word addressed by the classic blitter.  An
        // odd modulo would make the next row start on an odd address, which
        // is an address-error boundary on native 68k hardware rather than a
        // valid portable byte stride.  Admit these source fields only after
        // clipping proves that at least one destination cell will be used;
        // a fully clipped call is a source-free successful no-op.
        if (templateAddress == 0 || (templateAddress & 1u) != 0 ||
            sourceX < 0 || sourceX > 15 ||
            (sourceModulo & 1) != 0)
        {
            return GraphicsRasterOperations.Failure;
        }

        // SetBitmapPixel ignores colour in COMPLEMENT mode. Keep FgPen out
        // of the template admission envelope for that inversion-only form;
        // ordinary JAM1/JAM2 and inverse-video non-complement templates keep
        // their foreground-pen dependency. Resolve the public fields lazily
        // after provider visibility is known so a fully hidden clipped
        // template cannot claim sparse/provider-owned colour storage.
        var color = 0;
        var foregroundLoaded = (drawMode & 2) != 0;

        // SetBitmapPixel ignores the supplied colour in COMPLEMENT mode:
        // every selected destination plane is toggled from its current bit.
        // A JAM2|COMPLEMENT template therefore does not consume BgPen, even
        // though its zero-template cells still participate in the inversion.
        // Keep that guest field outside the portable read envelope so an
        // unrelated/provider-owned pen cannot steal a valid native request.
        var background = 0;
        var backgroundLoaded = (drawMode & 1) == 0 || (drawMode & 2) != 0;

        if (!TryFitPortableWork(
                right - left,
                bottom - top,
                BltTemplatePassesPerCell))
        {
            return GraphicsRasterOperations.Failure;
        }

        var cellCount = (long)(right - left) * (bottom - top);
        if (cellCount <= 0 || cellCount > int.MaxValue)
            return GraphicsRasterOperations.Failure;

        // The blitter's A source is a word-addressed template in chip
        // memory.  The optional display-DMA sidecar is the CopperStart/native
        // address-class boundary; a memory-only CopperSharp68k adapter keeps
        // the ordinary guest-readable source contract.  Preflight every
        // clipped source row before reading any template byte so a later
        // non-chip row cannot expose a partially claimed source operation.
        var displayMemory = memory as IGraphicsDisplayMemory;
        var firstTemplateBit = sourceX + left;
        var lastTemplateBit = sourceX + right - 1;
        var firstTemplateByteOffset = firstTemplateBit >> 3;
        var lastTemplateByteOffset = lastTemplateBit >> 3;
        var templateRowByteCount = lastTemplateByteOffset -
            firstTemplateByteOffset + 1;
        for (var y = top; y < bottom; y++)
        {
            var templateRowSigned = (long)templateAddress +
                ((long)y * sourceModulo);
            if (templateRowSigned < 0 || templateRowSigned > uint.MaxValue)
                return GraphicsRasterOperations.Failure;

            var rowStartSigned = templateRowSigned + firstTemplateByteOffset;
            if (rowStartSigned < 0 ||
                rowStartSigned > uint.MaxValue -
                    (uint)(templateRowByteCount - 1))
            {
                return GraphicsRasterOperations.Failure;
            }

            if (displayMemory is not null &&
                !displayMemory.IsDisplayDmaRange(
                    (uint)rowStartSigned,
                    (uint)templateRowByteCount))
            {
                return GraphicsRasterOperations.Failure;
            }
        }

        // BltTemplate is a single guest operation.  Decode the complete
        // clipped template and probe every destination pixel before exposing
        // any write.  A late malformed template row or destination plane must
        // not leave the earlier rows partially rendered.
        bool[] templateBits;
        List<uint>? snapshotAddressBuffer = null;
        List<byte>? snapshotValueBuffer = null;
        var scratchAcquired = false;
        if (scratch is null)
        {
            templateBits = new bool[(int)cellCount];
        }
        else if (!scratch.TryAcquireTemplate(
                     (int)cellCount,
                     out templateBits,
                     out var scratchAddresses,
                     out var scratchValues))
        {
            return GraphicsRasterOperations.Failure;
        }
        else
        {
            scratchAcquired = true;
            snapshotAddressBuffer = scratchAddresses;
            snapshotValueBuffer = scratchValues;
        }

        GraphicsRasterOperations.VisibilityScratch? acquiredVisibility = null;
        var stablePixelVisible = pixelVisible;
        if (pixelVisible is not null)
        {
            acquiredVisibility = scratch?.Visibility ??
                new GraphicsRasterOperations.VisibilityScratch();
            if (!acquiredVisibility.TryAcquire(
                    pixelVisible,
                    out stablePixelVisible))
            {
                if (scratchAcquired)
                    scratch!.Release();
                return GraphicsRasterOperations.Failure;
            }
        }

        try
        {
        for (var y = top; y < bottom; y++)
        {
            // BLTxMOD is a signed 16-bit byte offset.  Zero repeats the
            // source row; a negative even modulo walks the source backwards.
            // Keep the intermediate signed address wide so neither mode can
            // wrap through guest address zero.
            var templateRowSigned = (long)templateAddress +
                ((long)y * sourceModulo);
            if (templateRowSigned < 0 || templateRowSigned > uint.MaxValue)
            {
                return GraphicsRasterOperations.Failure;
            }

            var templateRow = (uint)templateRowSigned;
            for (var x = left; x < right; x++)
            {
                var templateBit = sourceX + x;
                var byteOffset = (uint)(templateBit >> 3);
                if (templateRow > uint.MaxValue - byteOffset)
                    return GraphicsRasterOperations.Failure;

                var byteAddress = templateRow + byteOffset;
                if (!memory.TryReadByte(byteAddress, out var value))
                    return GraphicsRasterOperations.Failure;

                var index = ((y - top) * (right - left)) + (x - left);
                var selected = (value & (0x80 >> (templateBit & 7))) != 0;
                if ((drawMode & 4) != 0)
                    selected = !selected;

                templateBits[index] = selected;
                // JAM2 writes both phases of the template.  JAM1 only
                // touches selected foreground bits, so do not require a
                // destination read for an untouched zero-template cell.
                if ((selected || (drawMode & 1) != 0) &&
                    (stablePixelVisible is null ||
                     stablePixelVisible(destinationX + x, destinationY + y)) &&
                    !GraphicsRasterOperations.TryProbeBitmapWrite(
                        memory,
                        bitmap,
                        destinationX + x,
                        destinationY + y,
                        writeMask))
                {
                    return GraphicsRasterOperations.Failure;
                }
            }
        }

        if (!GraphicsRasterOperations.TrySnapshotBitmapRegion(
                memory,
                bitmap,
                destinationX + left,
                destinationY + top,
                destinationX + right - 1,
                destinationY + bottom - 1,
                writeMask,
                out var destinationSnapshotAddresses,
                out var destinationSnapshotValues,
                snapshotAddressBuffer,
                snapshotValueBuffer,
                stablePixelVisible))
        {
            return GraphicsRasterOperations.Failure;
        }

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = ((y - top) * (right - left)) + (x - left);
                var selected = templateBits[index];
                if (stablePixelVisible is not null &&
                    !stablePixelVisible(destinationX + x, destinationY + y))
                {
                    continue;
                }
                if (selected)
                {
                    if (!TryEnsureTemplatePen(
                            memory,
                            rastPort,
                            true,
                            ref color,
                            ref foregroundLoaded) ||
                        !GraphicsRasterOperations.SetBitmapPixel(
                            memory,
                            bitmap,
                            destinationX + x,
                            destinationY + y,
                            color,
                            drawMode,
                            writeMask))
                    {
                        GraphicsRasterOperations.RestoreBitmapSnapshot(
                            memory,
                            destinationSnapshotAddresses,
                            destinationSnapshotValues);
                        return GraphicsRasterOperations.Failure;
                    }
                }
                else if ((drawMode & 1) != 0 &&
                         (!TryEnsureTemplatePen(
                              memory,
                              rastPort,
                              false,
                              ref background,
                              ref backgroundLoaded) ||
                          !GraphicsRasterOperations.SetBitmapPixel(
                              memory,
                              bitmap,
                              destinationX + x,
                              destinationY + y,
                              background,
                              drawMode,
                              writeMask)))
                {
                    GraphicsRasterOperations.RestoreBitmapSnapshot(
                        memory,
                        destinationSnapshotAddresses,
                        destinationSnapshotValues);
                    return GraphicsRasterOperations.Failure;
                }
            }
        }

        return GraphicsRasterOperations.Success;
        }
        finally
        {
            acquiredVisibility?.Release();
            if (scratchAcquired)
                scratch!.Release();
        }
    }

    private static bool TryEnsureTemplatePen(
        IGraphicsMemory memory,
        uint rastPort,
        bool foreground,
        ref int value,
        ref bool loaded)
    {
        if (loaded)
            return true;
        value = foreground
            ? GraphicsRasterOperations.GetAPen(memory, rastPort)
            : GraphicsRasterOperations.GetBPen(memory, rastPort);
        if (value < 0)
            return false;
        loaded = true;
        return true;
    }

    private static bool TryReadBitmapHeader(
        IGraphicsMemory memory,
        uint address,
        out GraphicsRasterOperations.BitmapInfo bitmap)
    {
        bitmap = default;
        // BitMap contains word fields and is addressed directly by 68k
        // blitter code.  An odd guest pointer would raise an address error on
        // native hardware, so keep it outside the portable/provider claim
        // rather than letting a host byte store reinterpret the structure.
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

        // AllocBitMap(BMF_MINPLANES) and compatible native callers may expose
        // only the plane-pointer entries declared by Depth.  The blitter
        // needs no bytes in the unused tail of the historical eight-plane
        // envelope, so keep the guest/provider boundary depth-sized.
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
        return true;
    }

    private static bool TryFitPortableWork(
        int width,
        int height,
        ulong passesPerCell)
    {
        if (width <= 0 || height <= 0 || passesPerCell == 0)
            return false;

        var cells = (ulong)(uint)width * (uint)height;
        return cells <= PortableWorkLimit / passesPerCell;
    }

    private static bool TryHasVisibleDestination(
        int destinationX,
        int destinationY,
        int width,
        int height,
        Func<int, int, bool> pixelVisible,
        out bool hasVisible)
    {
        hasVisible = false;
        if (width <= 0 || height <= 0)
            return true;

        var cells = (ulong)(uint)width * (uint)height;
        if (cells > PortableWorkLimit || cells > int.MaxValue)
            return false;

        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (pixelVisible(destinationX + column, destinationY + row))
                {
                    hasVisible = true;
                    return true;
                }
            }
        }

        return true;
    }

    private static bool TryReadLayer(IGraphicsMemory memory, uint rastPort, out uint layer)
        => TryReadRastPortLong(memory, rastPort, GraphicsLayouts.RastPortLayer, out layer);

    private static bool TryReadRastPortByte(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out byte value)
    {
        value = 0;
        return TryRastPortAddress(rastPort, offset, sizeof(byte), out var address) &&
               memory.TryReadByte(address, out value);
    }

    private static bool TryReadRastPortLong(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out uint value)
    {
        value = 0;
        return TryRastPortAddress(rastPort, offset, sizeof(uint), out var address) &&
               memory.TryReadLong(address, out value);
    }

    private static bool TryRastPortAddress(
        uint rastPort,
        int offset,
        int byteCount,
        out uint address)
    {
        address = 0;
        if (rastPort == 0 || offset < 0 || byteCount <= 0)
            return false;

        var end = (ulong)(uint)offset + (uint)byteCount - 1UL;
        if (end > uint.MaxValue || (ulong)rastPort + end > uint.MaxValue)
            return false;

        address = rastPort + (uint)offset;
        return true;
    }

    private static bool TryClipRectangle(
        GraphicsRasterOperations.BitmapInfo source,
        GraphicsRasterOperations.BitmapInfo destination,
        short sourceX,
        short sourceY,
        short destinationX,
        short destinationY,
        short width,
        short height,
        out int clippedSourceX,
        out int clippedSourceY,
        out int clippedDestinationX,
        out int clippedDestinationY,
        out int clippedWidth,
        out int clippedHeight)
    {
        var sx = (long)sourceX;
        var sy = (long)sourceY;
        var dx = (long)destinationX;
        var dy = (long)destinationY;
        var w = (long)width;
        var h = (long)height;

        if (sx < 0)
        {
            var delta = -sx;
            sx = 0;
            dx += delta;
            w -= delta;
        }

        if (sy < 0)
        {
            var delta = -sy;
            sy = 0;
            dy += delta;
            h -= delta;
        }

        if (dx < 0)
        {
            var delta = -dx;
            dx = 0;
            sx += delta;
            w -= delta;
        }

        if (dy < 0)
        {
            var delta = -dy;
            dy = 0;
            sy += delta;
            h -= delta;
        }

        w = Math.Min(w, source.Width - sx);
        w = Math.Min(w, destination.Width - dx);
        h = Math.Min(h, source.Rows - sy);
        h = Math.Min(h, destination.Rows - dy);

        if (w <= 0 || h <= 0 || sx < 0 || sy < 0 || dx < 0 || dy < 0)
        {
            clippedSourceX = clippedSourceY = clippedDestinationX = clippedDestinationY = 0;
            clippedWidth = clippedHeight = 0;
            return false;
        }

        clippedSourceX = checked((int)sx);
        clippedSourceY = checked((int)sy);
        clippedDestinationX = checked((int)dx);
        clippedDestinationY = checked((int)dy);
        clippedWidth = checked((int)w);
        clippedHeight = checked((int)h);
        return true;
    }

    private static bool TryValidatePlanes(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo source,
        GraphicsRasterOperations.BitmapInfo destination,
        byte mask)
    {
        var displayMemory = memory as IGraphicsDisplayMemory;
        var planeLimit = Math.Min(source.Depth, destination.Depth);
        for (var plane = 0; plane < planeLimit; plane++)
        {
            if ((mask & (1 << plane)) == 0)
                continue;

            if (!TryReadPlaneAddress(memory, source, plane, out var sourcePlane) ||
                !TryReadPlaneAddress(memory, destination, plane, out var destinationPlane) ||
                destinationPlane == 0 || destinationPlane == uint.MaxValue)
            {
                return false;
            }

            // Native planar blitter streams are word addressed.  Zero and
            // $FFFFFFFF retain their documented source-plane constants, but
            // an ordinary source or any destination plane must be word
            // aligned before the portable path claims the operation.
            if ((!IsSpecialPlane(sourcePlane) && (sourcePlane & 1u) != 0) ||
                (destinationPlane & 1u) != 0)
            {
                return false;
            }

            if (!IsSpecialPlane(sourcePlane) &&
                !TryProbePlane(memory, sourcePlane, source))
            {
                return false;
            }

            if (!TryProbePlane(memory, destinationPlane, destination))
                return false;

            // The blitter consumes actual planar storage through the chipset
            // DMA path. Keep this address-class admission optional for
            // memory-only CopperSharp68k adapters, but when CopperStart/native
            // memory exposes the classifier require each complete participating
            // plane envelope to be display-DMA accessible. V36 constant source
            // planes remain synthetic values and are intentionally exempt.
            if (displayMemory is not null)
            {
                var sourceSpan = GraphicsRasterOperations.GetBitmapPlaneTouchedSpan(source);
                var destinationSpan = GraphicsRasterOperations.GetBitmapPlaneTouchedSpan(destination);
                if ((!IsSpecialPlane(sourcePlane) &&
                     (sourceSpan == 0 || sourceSpan > uint.MaxValue ||
                      !displayMemory.IsDisplayDmaRange(sourcePlane, (uint)sourceSpan))) ||
                    destinationSpan == 0 || destinationSpan > uint.MaxValue ||
                    !displayMemory.IsDisplayDmaRange(destinationPlane, (uint)destinationSpan))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool BlitSpansOverlap(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo source,
        int sourceX,
        int sourceY,
        GraphicsRasterOperations.BitmapInfo destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        byte participatingMask)
    {
        // Plane pointers are guest addresses, not ownership tokens: distinct
        // planes may intentionally point into one shared allocation. Compare
        // every participating source/destination byte envelope rather than
        // relying on pointer equality, so shifted and cross-plane aliases take
        // the same TempA path as an exact self-blit.
        var planeLimit = Math.Min(source.Depth, destination.Depth);
        for (var sourcePlaneIndex = 0; sourcePlaneIndex < planeLimit; sourcePlaneIndex++)
        {
            if ((participatingMask & (1 << sourcePlaneIndex)) == 0 ||
                !TryReadPlaneAddress(memory, source, sourcePlaneIndex, out var sourcePlane) ||
                sourcePlane == 0 || sourcePlane == uint.MaxValue)
            {
                continue;
            }

            if (!TryGetByteEnvelope(
                    sourcePlane,
                    GraphicsRasterOperations.GetBitmapPlaneRowStride(source),
                    sourceX,
                    sourceY,
                    width,
                    height,
                    out var sourceStart,
                    out var sourceEnd))
            {
                // The normal plane preflight owns malformed spans. Treat an
                // unrepresentable alias envelope as overlapping here so an
                // invalid caller TempA cannot bypass the guarded boundary.
                return true;
            }

            for (var destinationPlaneIndex = 0;
                 destinationPlaneIndex < planeLimit;
                 destinationPlaneIndex++)
            {
                if ((participatingMask & (1 << destinationPlaneIndex)) == 0 ||
                    !TryReadPlaneAddress(
                        memory,
                        destination,
                        destinationPlaneIndex,
                        out var destinationPlane) ||
                    destinationPlane == 0 || destinationPlane == uint.MaxValue)
                {
                    continue;
                }

                if (!TryGetByteEnvelope(
                        destinationPlane,
                        GraphicsRasterOperations.GetBitmapPlaneRowStride(destination),
                        destinationX,
                        destinationY,
                        width,
                        height,
                        out var destinationStart,
                        out var destinationEnd))
                {
                    // The normal plane preflight owns malformed spans. Treat
                    // an unrepresentable alias envelope as overlapping here
                    // so an invalid caller TempA cannot bypass the guarded
                    // boundary.
                    return true;
                }

                if (sourceStart <= destinationEnd && destinationStart <= sourceEnd)
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetByteEnvelope(
        uint plane,
        int bytesPerRow,
        int x,
        int y,
        int width,
        int height,
        out ulong start,
        out ulong end)
    {
        start = end = 0;
        if (plane == 0 || bytesPerRow <= 0 || x < 0 || y < 0 || width <= 0 || height <= 0)
            return false;

        var firstOffset = ((ulong)(uint)y * (uint)bytesPerRow) + (uint)(x >> 3);
        var lastX = (ulong)(uint)(x + width - 1);
        var lastY = (ulong)(uint)(y + height - 1);
        var lastOffset = (lastY * (uint)bytesPerRow) + (uint)(lastX >> 3);
        start = (ulong)plane + firstOffset;
        end = (ulong)plane + lastOffset;
        return end >= start && end <= uint.MaxValue;
    }

    private static bool TryProbeMask(
        IGraphicsMemory memory,
        uint mask,
        int bytesPerRow,
        int rows)
    {
        if (bytesPerRow <= 0 || rows <= 0 ||
            (bytesPerRow & 1) != 0 ||
            !TryProbePlane(memory, mask, bytesPerRow, rows))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadMaskPixel(
        IGraphicsMemory memory,
        uint mask,
        int bytesPerRow,
        int x,
        int y,
        out bool set)
    {
        set = false;
        if (x < 0 || y < 0 || bytesPerRow <= 0)
            return false;

        var offset = ((ulong)(uint)y * (uint)bytesPerRow) + (uint)(x >> 3);
        if (offset > uint.MaxValue || mask > uint.MaxValue - (uint)offset ||
            !memory.TryReadByte(mask + (uint)offset, out var value))
        {
            return false;
        }

        set = (value & (0x80 >> (x & 7))) != 0;
        return true;
    }

    private static bool TryReadPlaneAddress(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int plane,
        out uint address)
        => memory.TryReadLong(
            bitmap.Address + (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4),
            out address);

    private static bool TryProbePlane(
        IGraphicsMemory memory,
        uint address,
        int bytesPerRow,
        int rows)
    {
        var byteCount = (ulong)(uint)bytesPerRow * (uint)rows;
        if (address == 0 || byteCount == 0 || byteCount > uint.MaxValue ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        return memory.TryReadByte(address, out _) &&
            memory.TryReadByte(address + (uint)(byteCount - 1), out _);
    }

    private static bool TryProbePlane(
        IGraphicsMemory memory,
        uint address,
        GraphicsRasterOperations.BitmapInfo bitmap)
    {
        var rowStride = GraphicsRasterOperations.GetBitmapPlaneRowStride(bitmap);
        var byteCount = GraphicsRasterOperations.GetBitmapPlaneTouchedSpan(bitmap);
        if (address == 0 || byteCount == 0 || byteCount > uint.MaxValue ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        // Keep the large-UWORD geometry probe bounded.  The per-pixel walk
        // below still validates every byte it will publish; the full plane
        // envelope only needs endpoint evidence when representing it as a
        // managed span would exceed the host's transactional limit.
        if (byteCount > int.MaxValue)
        {
            var last = address + (uint)(byteCount - 1);
            return memory.TryReadByte(address, out _) &&
                memory.TryReadByte(last, out _);
        }

        for (var row = 0; row < bitmap.Rows; row++)
        {
            var rowAddress = address + (uint)((ulong)row * (uint)rowStride);
            for (var offset = 0; offset < bitmap.PlaneBytesPerRow; offset++)
            {
                if (!memory.TryReadByte(rowAddress + (uint)offset, out _))
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadPixel(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int x,
        int y,
        int planeCount,
        bool sourcePlaneSpecials,
        out int color)
    {
        color = 0;
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return false;

        var byteOffset = ((ulong)(uint)y * (uint)GraphicsRasterOperations.GetBitmapPlaneRowStride(bitmap)) +
                         (uint)(x >> 3);
        var bitMask = (byte)(0x80 >> (x & 7));
        for (var plane = 0; plane < planeCount; plane++)
        {
            if (!TryReadPlaneAddress(memory, bitmap, plane, out var planeAddress))
                return false;

            bool set;
            if (sourcePlaneSpecials && planeAddress == 0)
            {
                set = false;
            }
            else if (sourcePlaneSpecials && planeAddress == uint.MaxValue)
            {
                set = true;
            }
            else if (byteOffset > uint.MaxValue - planeAddress ||
                     !memory.TryReadByte(planeAddress + (uint)byteOffset, out var value))
            {
                return false;
            }
            else
            {
                set = (value & bitMask) != 0;
            }

            if (set)
                color |= 1 << plane;
        }

        return true;
    }

    private static bool TryReadPixelMasked(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int x,
        int y,
        int planeCount,
        byte planeMask,
        bool sourcePlaneSpecials,
        out int color)
    {
        color = 0;
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return false;

        var byteOffset = ((ulong)(uint)y * (uint)GraphicsRasterOperations.GetBitmapPlaneRowStride(bitmap)) +
                         (uint)(x >> 3);
        var bitMask = (byte)(0x80 >> (x & 7));
        for (var plane = 0; plane < planeCount; plane++)
        {
            if ((planeMask & (1 << plane)) == 0)
                continue;

            if (!TryReadPlaneAddress(memory, bitmap, plane, out var planeAddress))
                return false;

            bool set;
            if (sourcePlaneSpecials && planeAddress == 0)
            {
                set = false;
            }
            else if (sourcePlaneSpecials && planeAddress == uint.MaxValue)
            {
                set = true;
            }
            else if (byteOffset > uint.MaxValue - planeAddress ||
                     !memory.TryReadByte(planeAddress + (uint)byteOffset, out var value))
            {
                return false;
            }
            else
            {
                set = (value & bitMask) != 0;
            }

            if (set)
                color |= 1 << plane;
        }

        return true;
    }

    private static bool TryWritePixel(
        IGraphicsMemory memory,
        GraphicsRasterOperations.BitmapInfo bitmap,
        int x,
        int y,
        int color,
        byte mask)
    {
        var byteOffset = ((ulong)(uint)y * (uint)GraphicsRasterOperations.GetBitmapPlaneRowStride(bitmap)) +
                         (uint)(x >> 3);
        var bitMask = (byte)(0x80 >> (x & 7));
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((mask & (1 << plane)) == 0)
                continue;

            if (!TryReadPlaneAddress(memory, bitmap, plane, out var planeAddress) ||
                byteOffset > uint.MaxValue - planeAddress ||
                !memory.TryReadByte(planeAddress + (uint)byteOffset, out var value))
            {
                return false;
            }

            var next = (color & (1 << plane)) != 0
                ? (byte)(value | bitMask)
                : (byte)(value & ~bitMask);
            if (byteOffset > uint.MaxValue - planeAddress ||
                !memory.TryWriteByte(planeAddress + (uint)byteOffset, next))
                return false;
        }

        return true;
    }

    private static bool IsSpecialPlane(uint address)
        => address == 0 || address == uint.MaxValue;

    private static int CountBits(byte value)
    {
        var count = 0;
        for (var bit = value; bit != 0; bit = (byte)(bit & (bit - 1)))
            count++;
        return count;
    }

    private static bool EvaluateMinterm(uint minterm, bool source, bool destination)
    {
        var index = 4 | (source ? 2 : 0) | (destination ? 1 : 0);
        return (minterm & (1 << index)) != 0;
    }
}
