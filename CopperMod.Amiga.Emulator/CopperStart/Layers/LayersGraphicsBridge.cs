using Amiga;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.CopperStart.Layers;

internal sealed partial class LayersHostServices
{
    private readonly GraphicsBlitScratch _pixelBlitScratch = new();
    private readonly GraphicsRasterOperations.MintermScratch _pixelMintermScratch = new();
    private readonly byte[] _bitmapHeaderScratch = new byte[checked((int)BitMap.Size)];
    private readonly uint[] _bitmapPlaneScratch = new uint[8];
    private bool _bitmapAllocationScratchInUse;
    private uint _rasterPrimaryRastPort;
    private uint _rasterSecondaryRastPort;

    private bool CopyRectangle(
        APTR sourceBitMap,
        APTR destinationBitMap,
        int sourceX,
        int sourceY,
        int destinationX,
        int destinationY,
        int width,
        int height,
        byte minterm,
        APTR mask)
    {
        if (mask.IsNotNull ||
            !TryShort(sourceX, out var sx) || !TryShort(sourceY, out var sy) ||
            !TryShort(destinationX, out var dx) || !TryShort(destinationY, out var dy) ||
            !TryShort(width, out var w) || !TryShort(height, out var h) ||
            w <= 0 || h <= 0)
        {
            return false;
        }

        var result = GraphicsBlitOperations.BltBitMap(
            _graphicsMemory,
            sourceBitMap.Raw,
            sx,
            sy,
            destinationBitMap.Raw,
            dx,
            dy,
            w,
            h,
            minterm,
            0xFF,
            0,
            _graphicsAllocator,
            _pixelBlitScratch);
        if (result != GraphicsRasterOperations.Failure)
            return true;

        var providerOwned = _graphics.IsRtgBitMap?.Invoke(sourceBitMap.Raw) == true ||
            _graphics.IsRtgBitMap?.Invoke(destinationBitMap.Raw) == true;
        if (!providerOwned)
            return false;
        var state = ResetProviderGraphicsState();
        state.A[0] = sourceBitMap.Raw;
        state.A[1] = destinationBitMap.Raw;
        state.D[0] = unchecked((uint)sx);
        state.D[1] = unchecked((uint)sy);
        state.D[2] = unchecked((uint)dx);
        state.D[3] = unchecked((uint)dy);
        state.D[4] = unchecked((uint)w);
        state.D[5] = unchecked((uint)h);
        state.D[6] = minterm;
        state.D[7] = 0xFF;
        return _graphics.BltBitMap(state) != 0;
    }

    private bool BackfillRectangle(
        APTR rastPort,
        APTR bitMap,
        APTR hook,
        int minX,
        int minY,
        int maxX,
        int maxY,
        int offsetX,
        int offsetY)
    {
        if (hook.Raw != LayerBackfillHook.Backfill ||
            !_memory.IsMapped(rastPort.Raw, checked((int)RastPort.Size)) ||
            !_memory.IsMapped(bitMap.Raw, checked((int)BitMap.Size)) ||
            maxX < minX || maxY < minY)
        {
            return false;
        }
        var width = (long)maxX - minX + 1;
        var height = (long)maxY - minY + 1;
        if (width <= 0 || height <= 0 ||
            !TryShort(offsetX, out var x0) || !TryShort(offsetY, out var y0) ||
            !TryShort(offsetX + width - 1, out var x1) ||
            !TryShort(offsetY + height - 1, out var y1))
        {
            return false;
        }

        var memory = CreatePlatform();
        var originalLayer = LayersRastPortCodec.ReadLayer(ref memory, rastPort);
        var originalBitMap = LayersRastPortCodec.ReadBitMap(ref memory, rastPort);
        try
        {
            LayersRastPortCodec.WriteLayer(ref memory, rastPort, APTR.Null);
            LayersRastPortCodec.WriteBitMap(ref memory, rastPort, bitMap);
            return GraphicsRasterOperations.EraseRect(
                _graphicsMemory,
                rastPort.Raw,
                x0,
                y0,
                x1,
                y1,
                null,
                _providerSnapshotAddresses,
                _providerSnapshotValues,
                _providerNestedSnapshotAddresses,
                _providerNestedSnapshotValues,
                _pixelMintermScratch) != GraphicsRasterOperations.Failure;
        }
        finally
        {
            LayersRastPortCodec.WriteBitMap(
                ref memory,
                rastPort,
                originalBitMap);
            LayersRastPortCodec.WriteLayer(
                ref memory,
                rastPort,
                originalLayer);
        }
    }

    private APTR AllocateBitMap(
        int width,
        int height,
        byte depth,
        uint flags,
        APTR friendBitMap)
    {
        if (width <= 0 || height <= 0)
            return APTR.Null;
        if (ShouldFailPortableLayersBitMapAllocation())
            return APTR.Null;
        // Provider-owned friends must produce provider-owned backing.  A
        // planar fallback would silently cross ownership domains and make
        // later atomic Sync/Copy/Scroll/Swap impossible to journal.
        if (_graphics.IsRtgBitMap?.Invoke(friendBitMap.Raw) == true)
        {
            var providerState = ResetProviderGraphicsState();
            providerState.A[0] = friendBitMap.Raw;
            providerState.D[0] = checked((uint)width);
            providerState.D[1] = checked((uint)height);
            providerState.D[2] = depth;
            providerState.D[3] = flags;
            return APTR.FromPointer(_graphics.AllocBitMap(providerState));
        }

        // Layers planar backing BitMaps are compact rasters whose width
        // already includes the classic BackingX word offset. A display friend
        // must not widen that private raster to the display's full row stride.
        if (_bitmapAllocationScratchInUse)
            return APTR.Null;
        _bitmapAllocationScratchInUse = true;
        uint result;
        try
        {
            result = GraphicsRasterOperations.AllocBitMap(
                _graphicsMemory,
                _graphicsAllocator,
                checked((uint)width),
                checked((uint)height),
                depth,
                flags,
                0,
                _bitmapHeaderScratch,
                _bitmapPlaneScratch);
        }
        finally
        {
            _bitmapAllocationScratchInUse = false;
        }
        if (result != 0)
        {
            TraceProviderOperationForTest("AllocBitMap");
            return APTR.FromPointer(result);
        }

        // A non-provider allocator may still supply a compatible bitmap when
        // the portable planar allocator cannot satisfy the request.
        var state = ResetProviderGraphicsState();
        state.A[0] = friendBitMap.Raw;
        state.D[0] = checked((uint)width);
        state.D[1] = checked((uint)height);
        state.D[2] = depth;
        state.D[3] = flags;
        var fallback = _graphics.AllocBitMap(state);
        if (fallback != 0)
            TraceProviderOperationForTest("AllocBitMap");
        return APTR.FromPointer(fallback);
    }

    private bool ReleaseBitMap(APTR bitMap)
    {
        if (bitMap.IsNull)
            return false;
        if (_bitmapAllocationScratchInUse)
            return false;
        _bitmapAllocationScratchInUse = true;
        int result;
        try
        {
            result = GraphicsRasterOperations.FreeBitMap(
                _graphicsMemory,
                _graphicsAllocator,
                bitMap.Raw,
                _bitmapPlaneScratch);
        }
        finally
        {
            _bitmapAllocationScratchInUse = false;
        }
        if (result != GraphicsRasterOperations.Failure)
        {
            TraceProviderOperationForTest("FreeBitMap");
            return true;
        }
        if (_graphics.IsRtgBitMap?.Invoke(bitMap.Raw) != true)
            return false;
        _graphics.FreeBitMap(bitMap.Raw);
        TraceProviderOperationForTest("FreeBitMap");
        return true;
    }

    private bool RenderLayerInfo(
        APTR layerInfo,
        APTR destinationRastPort,
        APTR destinationBitMap,
        APTR destinationBounds,
        APTR layerInfoBounds,
        APTR renderList,
        APTR ignoreList,
        bool erase,
        bool applyOpacityMultiplier)
    {
        _ = layerInfo;
        _ = destinationRastPort;
        _ = destinationBitMap;
        _ = destinationBounds;
        _ = layerInfoBounds;
        _ = renderList;
        _ = ignoreList;
        _ = erase;
        _ = applyOpacityMultiplier;
        TraceProviderOperationForTest("RenderLayerInfoDeclined");
        return false;
    }

    private bool ExecuteLayeredRaster(
        short graphicsLvo,
        ref PortableLayers.LayersRegisterFrame registers,
        APTR layer,
        APTR firstClipRect,
        APTR firstSuperClipRect,
        APTR secondaryLayer,
        APTR secondaryFirstClipRect,
        APTR secondaryFirstSuperClipRect)
    {
        var provider = _graphics.ValidatedLayerRaster;
        if (provider is null)
            return false;
        if (_rasterPrimaryRastPort == 0 ||
            (secondaryLayer.IsNotNull && _rasterSecondaryRastPort == 0) ||
            (secondaryLayer.IsNull && _rasterSecondaryRastPort != 0) ||
            !TryReadRasterLayerGuardDepth(layer, out var primaryGuardDepth))
        {
            return false;
        }


        var secondaryGuardDepth = (ushort)0;
        if (secondaryLayer.IsNotNull && secondaryLayer.Raw != layer.Raw &&
            !TryReadRasterLayerGuardDepth(
                secondaryLayer,
                out secondaryGuardDepth))
        {
            return false;
        }
        _lastRasterProviderGuardCountForTest = secondaryLayer.IsNotNull &&
            secondaryLayer.Raw != layer.Raw ? 2 : 1;
        _lastRasterProviderPrimaryGuardDepthForTest = primaryGuardDepth;
        _lastRasterProviderSecondaryGuardDepthForTest = secondaryGuardDepth;

        var executed = provider.TryExecuteLayeredRaster(
            graphicsLvo,
            ref registers,
            new GraphicsValidatedLayerEndpoint(
                _rasterPrimaryRastPort,
                layer.Raw,
                firstClipRect.Raw,
                firstSuperClipRect.Raw),
            secondaryLayer.IsNull
                ? default
                : new GraphicsValidatedLayerEndpoint(
                    _rasterSecondaryRastPort,
                    secondaryLayer.Raw,
                    secondaryFirstClipRect.Raw,
                    secondaryFirstSuperClipRect.Raw));
        if (executed)
        {
            ArmRasterRetireLinkReadFaultForTest(layer);
            TraceLayeredRasterProviderOperationForTest(graphicsLvo);
        }
        return executed;
    }

    private bool TryReadRasterLayerGuardDepth(
        APTR layer,
        out ushort depth)
    {
        depth = 0;
        var task = _getCurrentTask();
        if (task == 0 || layer.IsNull)
        {
            return false;
        }

        var memory = CreatePlatform();
        var semaphore = LayersLayerCodec.LockAddress(layer);
        if (!_memory.IsMapped(
                semaphore.Raw,
                checked((int)SignalSemaphore.Size)))
        {
            return false;
        }
        if (LayersSignalSemaphoreCodec.ReadOwner(
                ref memory,
                semaphore).Raw != task)
        {
            return false;
        }

        var publicNest = unchecked((ushort)LayersSignalSemaphoreCodec.ReadNestCount(
            ref memory,
            semaphore));
        if (publicNest == ushort.MaxValue)
            return false;
        depth = checked((ushort)(publicNest + 1));
        return true;
    }

    public bool TryDraw(uint rastPortAddress, short x, short y)
    {
        var frame = FrameA1(rastPortAddress);
        frame.D0 = unchecked((uint)x);
        frame.D1 = unchecked((uint)y);
        return DispatchRaster(GraphicsLvo.Draw, rastPortAddress, 0, ref frame);
    }

    public bool TryText(uint rastPortAddress, uint textAddress, short length)
    {
        var frame = FrameA1(rastPortAddress);
        frame.A0 = textAddress;
        frame.D0 = unchecked((uint)length);
        return DispatchRaster(GraphicsLvo.Text, rastPortAddress, 0, ref frame);
    }

    public bool TryGetDrawBounds(uint rastPortAddress, uint rectangleAddress)
    {
        var memory = CreatePlatform();
        var rastPort = APTR.FromPointer(rastPortAddress);
        var rectangle = APTR.FromPointer(rectangleAddress);
        if (!_active || !LayersRastPortCodec.IsMapped(ref memory, rastPort) ||
            !LayersRectangleCodec.IsMapped(ref memory, rectangle))
            return false;
        var layer = LayersRastPortCodec.ReadLayer(ref memory, rastPort);
        if (!PortableLayers.LayersLayerCore.TryGetDescriptor(ref memory, APTR.FromPointer(_root), layer, out _))
            return false;
        LayersRectangleCodec.Write(
            ref memory,
            rectangle,
            LayersLayerCodec.ReadBounds(ref memory, layer));
        return true;
    }

    public bool TryRectFill(uint rp, short x0, short y0, short x1, short y1)
        => DispatchFour(GraphicsLvo.RectFill, rp, x0, y0, x1, y1);
    public bool TryDrawEllipse(uint rp, short x, short y, short rx, short ry)
        => DispatchFour(GraphicsLvo.DrawEllipse, rp, x, y, rx, ry);
    public bool TrySetRast(uint rp, uint pen)
    {
        var frame = FrameA1(rp); frame.D0 = pen;
        return DispatchRaster(GraphicsLvo.SetRast, rp, 0, ref frame);
    }
    public bool TryReadPixel(uint rp, short x, short y, out int color)
    {
        var frame = FrameA1(rp); frame.D0 = unchecked((uint)x); frame.D1 = unchecked((uint)y);
        var claimed = DispatchRaster(GraphicsLvo.ReadPixel, rp, 0, ref frame);
        color = claimed ? unchecked((int)frame.D0) : GraphicsRasterOperations.Failure;
        return claimed;
    }
    public bool TryWritePixel(uint rp, short x, short y)
    {
        var frame = FrameA1(rp); frame.D0 = unchecked((uint)x); frame.D1 = unchecked((uint)y);
        return DispatchRaster(GraphicsLvo.WritePixel, rp, 0, ref frame);
    }
    public bool TryScrollRaster(uint rp, short dx, short dy, short x0, short y0, short x1, short y1)
        => DispatchSix(GraphicsLvo.ScrollRaster, rp, dx, dy, x0, y0, x1, y1);
    public bool TryScrollRasterBF(uint rp, short dx, short dy, short x0, short y0, short x1, short y1)
        => DispatchSix(GraphicsLvo.ScrollRasterBF, rp, dx, dy, x0, y0, x1, y1);
    public bool TryClearEOL(uint rp) => DispatchNoData(GraphicsLvo.ClearEOL, rp);
    public bool TryClearScreen(uint rp) => DispatchNoData(GraphicsLvo.ClearScreen, rp);
    public bool TryEraseRect(uint rp, short x0, short y0, short x1, short y1)
        => DispatchFour(GraphicsLvo.EraseRect, rp, x0, y0, x1, y1);
    public bool TryPolyDraw(uint rp, ushort count, uint points)
    {
        var frame = FrameA1(rp); frame.A0 = points; frame.D0 = count;
        return DispatchRaster(GraphicsLvo.PolyDraw, rp, 0, ref frame);
    }
    public bool TryAreaMove(uint rp, short x, short y) => DispatchTwo(GraphicsLvo.AreaMove, rp, x, y);
    public bool TryAreaDraw(uint rp, short x, short y) => DispatchTwo(GraphicsLvo.AreaDraw, rp, x, y);
    public bool TryAreaEllipse(uint rp, short x, short y, short rx, short ry)
        => DispatchFour(GraphicsLvo.AreaEllipse, rp, x, y, rx, ry);
    public bool TryAreaEnd(uint rp) => DispatchNoData(GraphicsLvo.AreaEnd, rp);
    public bool TryFlood(uint rp, uint mode, short x, short y, out int result)
    {
        var frame = FrameA1(rp); frame.D0 = unchecked((uint)x); frame.D1 = unchecked((uint)y); frame.D2 = mode;
        var claimed = DispatchRaster(GraphicsLvo.Flood, rp, 0, ref frame);
        result = claimed ? unchecked((int)frame.D0) : GraphicsRasterOperations.Failure;
        return claimed;
    }
    public bool TryReadPixelLine8(uint rp, ushort x, ushort y, ushort width, uint array, uint temp, out int result)
        => DispatchPixelSpan(GraphicsLvo.ReadPixelLine8, rp, x, y, width, 0, array, temp, out result);
    public bool TryWritePixelLine8(uint rp, ushort x, ushort y, ushort width, uint array, uint temp, out int result)
        => DispatchPixelSpan(GraphicsLvo.WritePixelLine8, rp, x, y, width, 0, array, temp, out result);
    public bool TryReadPixelArray8(uint rp, ushort x0, ushort y0, ushort x1, ushort y1, uint array, uint temp, out int result)
        => DispatchPixelSpan(GraphicsLvo.ReadPixelArray8, rp, x0, y0, x1, y1, array, temp, out result);
    public bool TryWritePixelArray8(uint rp, ushort x0, ushort y0, ushort x1, ushort y1, uint array, uint temp, out int result)
        => DispatchPixelSpan(GraphicsLvo.WritePixelArray8, rp, x0, y0, x1, y1, array, temp, out result);
    public bool TryWriteChunkyPixels(uint rp, int x0, int y0, int x1, int y1, uint source, int bytesPerRow)
    {
        var frame = FrameA0(rp); frame.A2 = source; frame.D0 = unchecked((uint)x0); frame.D1 = unchecked((uint)y0); frame.D2 = unchecked((uint)x1); frame.D3 = unchecked((uint)y1); frame.D4 = unchecked((uint)bytesPerRow);
        return DispatchRaster(GraphicsLvo.WriteChunkyPixels, rp, 0, ref frame);
    }
    public bool TryClipBlit(uint sourceRp, short sx, short sy, uint destinationRp, short dx, short dy, short width, short height, byte minterm)
    {
        var sourceLayered = OwnsLayeredRastPort(sourceRp);
        var destinationLayered = OwnsLayeredRastPort(destinationRp);
        if (!sourceLayered && !destinationLayered) return false;
        var frame = new PortableLayers.LayersRegisterFrame { A0 = sourceRp, A1 = destinationRp, D0 = unchecked((uint)sx), D1 = unchecked((uint)sy), D2 = unchecked((uint)dx), D3 = unchecked((uint)dy), D4 = unchecked((uint)width), D5 = unchecked((uint)height), D6 = minterm };
        var primary = destinationLayered ? destinationRp : sourceRp;
        // A self ClipBlit has one unique Layers lock/partition endpoint even
        // though the ABI continues to carry the same RastPort in A0 and A1.
        // Supplying it twice would acquire the same non-recursive task lock
        // twice once RasterCore owns internal locking.
        var secondary = destinationLayered && sourceLayered &&
            sourceRp != destinationRp
                ? sourceRp
                : 0u;
        return DispatchRaster(GraphicsLvo.ClipBlit, primary, secondary, ref frame);
    }
    public bool TryBltBitMapRastPort(uint source, short sx, short sy, uint rp, short dx, short dy, short width, short height, byte minterm, out int result)
    {
        var frame = FrameA1(rp); frame.A0 = source; SetBlitData(ref frame, sx, sy, dx, dy, width, height, minterm);
        var claimed = DispatchRaster(GraphicsLvo.BltBitMapRastPort, rp, 0, ref frame);
        result = claimed ? unchecked((int)frame.D0) : 0;
        return claimed;
    }
    public bool TryBltMaskBitMapRastPort(uint source, short sx, short sy, uint rp, short dx, short dy, short width, short height, byte minterm, uint mask)
    {
        var frame = FrameA1(rp); frame.A0 = source; frame.A2 = mask; SetBlitData(ref frame, sx, sy, dx, dy, width, height, minterm);
        return DispatchRaster(GraphicsLvo.BltMaskBitMapRastPort, rp, 0, ref frame);
    }
    public bool TryBltPattern(uint rp, uint mask, short x0, short y0, short x1, short y1, short bytesPerRow)
    {
        var frame = FrameA1(rp); frame.A0 = mask; frame.D0 = unchecked((uint)x0); frame.D1 = unchecked((uint)y0); frame.D2 = unchecked((uint)x1); frame.D3 = unchecked((uint)y1); frame.D4 = unchecked((uint)bytesPerRow);
        return DispatchRaster(GraphicsLvo.BltPattern, rp, 0, ref frame);
    }
    public bool TryBltTemplate(uint template, short sx, short modulo, uint rp, short dx, short dy, short width, short height)
    {
        var frame = FrameA1(rp); frame.A0 = template; frame.D0 = unchecked((uint)sx); frame.D1 = unchecked((uint)modulo); frame.D2 = unchecked((uint)dx); frame.D3 = unchecked((uint)dy); frame.D4 = unchecked((uint)width); frame.D5 = unchecked((uint)height);
        return DispatchRaster(GraphicsLvo.BltTemplate, rp, 0, ref frame);
    }

    private bool DispatchRaster(GraphicsLvo lvo, uint primary, uint secondary, ref PortableLayers.LayersRegisterFrame frame)
    {
        _ = lvo;
        _ = primary;
        _ = secondary;
        _ = frame;
        TraceCompatibilityRasterDispatchForTest();

        // This legacy interface is called only by the ordinary portable
        // graphics adapter. A CopperStart-owned RastPort must have been
        // intercepted by GraphicsServices.InvokeGateway and dispatched with
        // the caller's real CPU state so RasterCore can suspend/resume the
        // outer scheduler. Executing here would silently lose Block/token
        // results (including cleanup continuations), so fail closed. Provider
        // reentrancy reaches this same boundary and is declined as well.
        return false;
    }
    internal bool OwnsLayeredRastPort(uint rp)
    {
        var platform = CreatePlatform();
        var rastPort = APTR.FromPointer(rp);
        if (!_active || !LayersRastPortCodec.IsMapped(ref platform, rastPort)) return false;
        var layer = LayersRastPortCodec.ReadLayer(ref platform, rastPort);
        return PortableLayers.LayersLayerCore.TryGetDescriptor(ref platform, APTR.FromPointer(_root), layer, out _);
    }
    private bool DispatchNoData(GraphicsLvo lvo, uint rp) { var frame = FrameA1(rp); return DispatchRaster(lvo, rp, 0, ref frame); }
    private bool DispatchTwo(GraphicsLvo lvo, uint rp, short a, short b) { var frame = FrameA1(rp); frame.D0 = unchecked((uint)a); frame.D1 = unchecked((uint)b); return DispatchRaster(lvo, rp, 0, ref frame); }
    private bool DispatchFour(GraphicsLvo lvo, uint rp, short a, short b, short c, short d) { var frame = FrameA1(rp); frame.D0 = unchecked((uint)a); frame.D1 = unchecked((uint)b); frame.D2 = unchecked((uint)c); frame.D3 = unchecked((uint)d); return DispatchRaster(lvo, rp, 0, ref frame); }
    private bool DispatchSix(GraphicsLvo lvo, uint rp, short a, short b, short c, short d, short e, short f) { var frame = FrameA1(rp); frame.D0 = unchecked((uint)a); frame.D1 = unchecked((uint)b); frame.D2 = unchecked((uint)c); frame.D3 = unchecked((uint)d); frame.D4 = unchecked((uint)e); frame.D5 = unchecked((uint)f); return DispatchRaster(lvo, rp, 0, ref frame); }
    private bool DispatchPixelSpan(GraphicsLvo lvo, uint rp, ushort a, ushort b, ushort c, ushort d, uint array, uint temp, out int result) { var frame = FrameA0(rp); frame.A1 = temp; frame.A2 = array; frame.D0 = a; frame.D1 = b; frame.D2 = c; frame.D3 = d; var claimed = DispatchRaster(lvo, rp, 0, ref frame); result = claimed ? unchecked((int)frame.D0) : GraphicsRasterOperations.Failure; return claimed; }
    private static PortableLayers.LayersRegisterFrame FrameA1(uint rp) => new() { A1 = rp };
    private static PortableLayers.LayersRegisterFrame FrameA0(uint rp) => new() { A0 = rp };
    private static short S(uint value) => unchecked((short)value);
    private static bool TryShort(long value, out short result) { if (value < short.MinValue || value > short.MaxValue) { result = 0; return false; } result = (short)value; return true; }
    private static void SetBlitData(ref PortableLayers.LayersRegisterFrame frame, short sx, short sy, short dx, short dy, short width, short height, byte minterm) { frame.D0 = unchecked((uint)sx); frame.D1 = unchecked((uint)sy); frame.D2 = unchecked((uint)dx); frame.D3 = unchecked((uint)dy); frame.D4 = unchecked((uint)width); frame.D5 = unchecked((uint)height); frame.D6 = minterm; }
}
