using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.CopperStart.Graphics;

/// <summary>
/// Host-only binding for the portable graphics memory contract. It deliberately
/// refuses hardware ranges and converts guarded HostGuestMemory access into the
/// portable Try* shape.
/// </summary>
internal sealed class CopperStartGraphicsMemoryAdapter : IGraphicsMemory, IGraphicsDisplayMemory
{
    private readonly HostGuestMemory _memory;
    private readonly Func<uint, uint, bool>? _isDisplayDmaRange;

    internal CopperStartGraphicsMemoryAdapter(
        HostGuestMemory memory,
        Func<uint, uint, bool>? isDisplayDmaRange = null)
    {
        _memory = memory;
        _isDisplayDmaRange = isDisplayDmaRange;
    }

    public bool TryReadByte(uint address, out byte value)
    {
        if (!_memory.IsMapped(address, 1))
        {
            value = 0;
            return false;
        }

        value = _memory.ReadByte(address);
        return true;
    }

    public bool TryReadWord(uint address, out ushort value)
    {
        if (!_memory.IsMapped(address, 2))
        {
            value = 0;
            return false;
        }

        value = _memory.ReadWord(address);
        return true;
    }

    public bool TryReadLong(uint address, out uint value)
    {
        if (!_memory.IsMapped(address, 4))
        {
            value = 0;
            return false;
        }

        value = _memory.ReadLong(address);
        return true;
    }

    public bool TryWriteByte(uint address, byte value)
    {
        if (!_memory.IsMapped(address, 1) ||
            !_memory.Bus.IsWritableMemoryRange(address, 1))
            return false;

        _memory.WriteByte(address, value);
        return true;
    }

    public bool TryWriteWord(uint address, ushort value)
    {
        if (!_memory.IsMapped(address, 2) ||
            !_memory.Bus.IsWritableMemoryRange(address, 2))
            return false;

        _memory.WriteWord(address, value);
        return true;
    }

    public bool TryWriteLong(uint address, uint value)
    {
        if (!_memory.IsMapped(address, 4) ||
            !_memory.Bus.IsWritableMemoryRange(address, 4))
            return false;

        _memory.WriteLong(address, value);
        return true;
    }

    public bool IsDisplayDmaRange(uint address, uint byteCount)
        => _isDisplayDmaRange is null
            ? true
            : _isDisplayDmaRange(address, byteCount);
}

internal sealed class CopperStartGraphicsUnboundAllocator : IGraphicsAllocatorBackend
{
    public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
    {
        address = 0;
        return false;
    }

    public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass) { }
}

/// <summary>
/// Maps portable raster allocations to the reset-scoped Exec memory boundary.
/// Graphics does not own the guest allocator; it only supplies the classic
/// MEMF_CHIP/MEMF_PUBLIC class requested by the portable operation.
/// </summary>
internal sealed class CopperStartGraphicsAllocator :
    IGraphicsAllocatorBackend,
    IGraphicsMonitorNameAllocator
{
    private const uint MemfPublic = 0x0000_0001;
    private const uint MemfChip = 0x0000_0002;

    private readonly CopperStartGraphicsContext _context;

    internal CopperStartGraphicsAllocator(CopperStartGraphicsContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public bool SupportsMonitorNameAllocation
        => _context.SupportsMonitorNameAllocation;

    public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
    {
        address = 0;
        if (byteCount == 0 || byteCount > int.MaxValue || _context.AllocateMemory is null)
            return false;

        var flags = memoryClass switch
        {
            GraphicsMemoryClass.Chip => MemfChip,
            GraphicsMemoryClass.Public => MemfPublic,
            _ => 0u
        };
        address = _context.AllocateMemory((int)byteCount, flags);
        return address != 0;
    }

    public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
    {
        if (address == 0 || byteCount == 0 || byteCount > int.MaxValue || _context.FreeMemory is null)
            return;

        _context.FreeMemory(address, (int)byteCount);
    }
}

internal sealed class CopperStartGraphicsBlitterAdapter :
    IGraphicsBlitterBackend,
    IGraphicsQueuedBlitterBackend,
    IGraphicsQueuedBlitterLinkBackend,
    IGraphicsBlitterOwnershipStatusBackend,
    IGraphicsQueuedBlitterStatusBackend,
    IGraphicsTimedBlitterBackend
{
    private readonly CopperStartGraphicsContext _context;

    internal CopperStartGraphicsBlitterAdapter(CopperStartGraphicsContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public bool PublishesGuestLinks
        => _context.TimedBlitter is IGraphicsQueuedBlitterLinkBackend linked &&
            linked.PublishesGuestLinks;

    public void Own() => _context.OwnBlitter?.Invoke();
    public void Disown() => _context.DisownBlitter?.Invoke();
    public void Wait() => _context.WaitBlit?.Invoke();
    public void Submit(uint operationAddress) => _context.SubmitBlit?.Invoke(operationAddress);
    public void SubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync)
        => _context.SubmitQueuedBlit?.Invoke(operationAddress, beamSynchronized, beamSync);

    public bool TryOwn()
    {
        if (_context.TryOwnBlitter is not null)
            return _context.TryOwnBlitter();

        if (_context.OwnBlitter is null)
            return false;

        _context.OwnBlitter();
        return true;
    }

    public bool TryDisown()
    {
        if (_context.TryDisownBlitter is not null)
            return _context.TryDisownBlitter();

        if (_context.DisownBlitter is null)
            return false;

        _context.DisownBlitter();
        return true;
    }

    public bool TryWait()
    {
        if (_context.TryWaitBlit is not null)
            return _context.TryWaitBlit();

        if (_context.WaitBlit is null)
            return false;

        _context.WaitBlit();
        return true;
    }

    public bool TrySubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync)
    {
        if (_context.TrySubmitQueuedBlit is not null)
        {
            return _context.TrySubmitQueuedBlit(
                operationAddress,
                beamSynchronized,
                beamSync);
        }

        if (_context.SubmitQueuedBlit is not null)
        {
            _context.SubmitQueuedBlit(operationAddress, beamSynchronized, beamSync);
            return true;
        }

        if (_context.SubmitBlit is not null)
        {
            _context.SubmitBlit(operationAddress);
            return true;
        }

        return false;
    }

    public bool TryOwn(long cycle)
        => _context.TimedBlitter?.TryOwn(cycle) ?? TryOwn();

    public bool TryDisown(long cycle)
        => _context.TimedBlitter?.TryDisown(cycle) ?? TryDisown();

    public bool TryWait(long cycle, out long completionCycle)
    {
        if (_context.TimedBlitter is not null)
            return _context.TimedBlitter.TryWait(cycle, out completionCycle);

        completionCycle = cycle;
        return TryWait();
    }

    public bool TrySubmitQueued(
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle)
        => _context.TimedBlitter?.TrySubmitQueued(
            operationAddress,
            beamSynchronized,
            beamSync,
            cycle) ?? TrySubmitQueued(operationAddress, beamSynchronized, beamSync);

    public bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync)
    {
        if (_context.TimedBlitter is IGraphicsQueuedBlitterLinkBackend linked &&
            linked.PublishesGuestLinks)
        {
            return linked.TrySubmitQueued(
                memory,
                operationAddress,
                beamSynchronized,
                beamSync);
        }

        return TrySubmitQueued(operationAddress, beamSynchronized, beamSync);
    }

    public bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle)
    {
        if (_context.TimedBlitter is IGraphicsQueuedBlitterLinkBackend linked &&
            linked.PublishesGuestLinks)
        {
            return linked.TrySubmitQueued(
                memory,
                operationAddress,
                beamSynchronized,
                beamSync,
                cycle);
        }

        return _context.TimedBlitter?.TrySubmitQueued(
            operationAddress,
            beamSynchronized,
            beamSync,
            cycle) ?? TrySubmitQueued(operationAddress, beamSynchronized, beamSync);
    }
}

internal sealed class CopperStartGraphicsUnboundBlitter :
    IGraphicsBlitterBackend,
    IGraphicsQueuedBlitterBackend,
    IGraphicsBlitterOwnershipStatusBackend,
    IGraphicsQueuedBlitterStatusBackend
{
    public void Own() { }
    public void Disown() { }
    public void Wait() { }
    public void Submit(uint operationAddress) { }
    public void SubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync) { }
    public bool TryOwn() => false;
    public bool TryDisown() => false;
    public bool TryWait() => false;
    public bool TrySubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync) => false;
}

internal sealed class CopperStartGraphicsUnboundDisplay : IGraphicsDisplayBackend
{
    public void PublishView(uint viewAddress) { }
    public void WaitForTopOfFrame() { }
    public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) { }
    public ushort GetBeamPosition() => 0;
}

/// <summary>
/// Graphics-side view of the single Layers owner.  CopperStart boot injects
/// its <c>LayersHostServices</c> instance; standalone graphics tests may still
/// supply the legacy callback bundle, but there is no independent lock table
/// or topology fallback here.
/// </summary>
internal sealed class CopperStartGraphicsLayerAdapter : IGraphicsLayerBackend, IGraphicsLayerRasterBackend
{
    private readonly CopperStartGraphicsContext _context;
    private readonly IGraphicsLayerBackend? _layers;
    private readonly IGraphicsLayerRasterBackend? _raster;

    internal bool HasLockProvider
        => _layers is not null ||
           (_context.LockLayerRom is not null &&
           _context.AttemptLockLayerRom is not null &&
           _context.UnlockLayerRom is not null);

    internal CopperStartGraphicsLayerAdapter(
        CopperStartGraphicsContext context,
        IGraphicsLayerBackend? layers = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _layers = layers;
        _raster = layers as IGraphicsLayerRasterBackend ??
            (layers is null ? context.LayerRaster : null);
    }

    public void Lock(uint layerAddress)
    {
        if (_layers is not null)
        {
            _layers.Lock(layerAddress);
            return;
        }
        if (HasLockProvider && _context.LockLayerRom is { } lockLayer)
        {
            lockLayer(layerAddress);
        }
    }

    public bool TryLock(uint layerAddress)
        => _layers?.TryLock(layerAddress) ??
            (HasLockProvider && _context.AttemptLockLayerRom is { } attempt
            ? attempt(layerAddress)
            : false);

    public void Unlock(uint layerAddress)
    {
        if (_layers is not null)
        {
            _layers.Unlock(layerAddress);
            return;
        }
        if (HasLockProvider && _context.UnlockLayerRom is { } unlockLayer)
        {
            unlockLayer(layerAddress);
        }
    }
    public bool SyncSuperBitMap(uint layerAddress)
        => _layers?.SyncSuperBitMap(layerAddress) ??
            (_context.SyncSBitMap?.Invoke(layerAddress) ?? false);
    public bool CopySuperBitMap(uint layerAddress)
        => _layers?.CopySuperBitMap(layerAddress) ??
            (_context.CopySBitMap?.Invoke(layerAddress) ?? false);

    public bool TryDraw(uint rastPortAddress, short x, short y)
        => _raster?.TryDraw(rastPortAddress, x, y) == true;

    public bool TryText(uint rastPortAddress, uint textAddress, short length)
        => _raster?.TryText(rastPortAddress, textAddress, length) == true;

    public bool TryGetDrawBounds(uint rastPortAddress, uint rectangleAddress)
        => _raster?.TryGetDrawBounds(rastPortAddress, rectangleAddress) == true;

    public bool TryRectFill(
        uint rastPortAddress,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => _raster?.TryRectFill(
            rastPortAddress,
            xMin,
            yMin,
            xMax,
            yMax) == true;

    public bool TryDrawEllipse(
        uint rastPortAddress,
        short centerX,
        short centerY,
        short radiusX,
        short radiusY)
        => _raster?.TryDrawEllipse(
            rastPortAddress,
            centerX,
            centerY,
            radiusX,
            radiusY) == true;

    public bool TrySetRast(uint rastPortAddress, uint pen)
        => _raster?.TrySetRast(rastPortAddress, pen) == true;

    public bool TryReadPixel(
        uint rastPortAddress,
        short x,
        short y,
        out int color)
    {
        if (_raster is not null)
            return _raster.TryReadPixel(rastPortAddress, x, y, out color);

        color = GraphicsRasterOperations.Failure;
        return false;
    }

    public bool TryWritePixel(uint rastPortAddress, short x, short y)
        => _raster?.TryWritePixel(rastPortAddress, x, y) == true;

    public bool TryScrollRaster(
        uint rastPortAddress,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => _raster?.TryScrollRaster(
            rastPortAddress,
            deltaX,
            deltaY,
            xMin,
            yMin,
            xMax,
            yMax) == true;

    public bool TryScrollRasterBF(
        uint rastPortAddress,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => _raster?.TryScrollRasterBF(
            rastPortAddress,
            deltaX,
            deltaY,
            xMin,
            yMin,
            xMax,
            yMax) == true;

    public bool TryClearEOL(uint rastPortAddress)
        => _raster?.TryClearEOL(rastPortAddress) == true;

    public bool TryClearScreen(uint rastPortAddress)
        => _raster?.TryClearScreen(rastPortAddress) == true;

    public bool TryEraseRect(
        uint rastPortAddress,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => _raster?.TryEraseRect(
            rastPortAddress,
            xMin,
            yMin,
            xMax,
            yMax) == true;

    public bool TryPolyDraw(uint rastPortAddress, ushort count, uint pointsAddress)
        => _raster?.TryPolyDraw(rastPortAddress, count, pointsAddress) == true;

    public bool TryAreaMove(uint rastPortAddress, short x, short y)
        => _raster?.TryAreaMove(rastPortAddress, x, y) == true;

    public bool TryAreaDraw(uint rastPortAddress, short x, short y)
        => _raster?.TryAreaDraw(rastPortAddress, x, y) == true;

    public bool TryAreaEllipse(
        uint rastPortAddress,
        short centerX,
        short centerY,
        short radiusX,
        short radiusY)
        => _raster?.TryAreaEllipse(
            rastPortAddress,
            centerX,
            centerY,
            radiusX,
            radiusY) == true;

    public bool TryAreaEnd(uint rastPortAddress)
        => _raster?.TryAreaEnd(rastPortAddress) == true;

    public bool TryFlood(
        uint rastPortAddress,
        uint mode,
        short x,
        short y,
        out int result)
    {
        if (_raster is not null)
            return _raster.TryFlood(rastPortAddress, mode, x, y, out result);

        result = GraphicsRasterOperations.Failure;
        return false;
    }

    public bool TryReadPixelLine8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort width,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        if (_raster is not null)
        {
            return _raster.TryReadPixelLine8(
                rastPortAddress,
                xStart,
                yStart,
                width,
                arrayAddress,
                temporaryRastPortAddress,
                out result);
        }

        result = GraphicsRasterOperations.Failure;
        return false;
    }

    public bool TryWritePixelLine8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort width,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        if (_raster is not null)
        {
            return _raster.TryWritePixelLine8(
                rastPortAddress,
                xStart,
                yStart,
                width,
                arrayAddress,
                temporaryRastPortAddress,
                out result);
        }

        result = GraphicsRasterOperations.Failure;
        return false;
    }

    public bool TryReadPixelArray8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort xStop,
        ushort yStop,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        if (_raster is not null)
        {
            return _raster.TryReadPixelArray8(
                rastPortAddress,
                xStart,
                yStart,
                xStop,
                yStop,
                arrayAddress,
                temporaryRastPortAddress,
                out result);
        }

        result = GraphicsRasterOperations.Failure;
        return false;
    }

    public bool TryWritePixelArray8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort xStop,
        ushort yStop,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        if (_raster is not null)
        {
            return _raster.TryWritePixelArray8(
                rastPortAddress,
                xStart,
                yStart,
                xStop,
                yStop,
                arrayAddress,
                temporaryRastPortAddress,
                out result);
        }

        result = GraphicsRasterOperations.Failure;
        return false;
    }

    public bool TryWriteChunkyPixels(
        uint rastPortAddress,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint sourceArrayAddress,
        int bytesPerRow)
        => _raster?.TryWriteChunkyPixels(
            rastPortAddress,
            xStart,
            yStart,
            xStop,
            yStop,
            sourceArrayAddress,
            bytesPerRow) == true;

    public bool TryClipBlit(
        uint sourceRastPortAddress,
        short sourceX,
        short sourceY,
        uint destinationRastPortAddress,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm)
        => _raster?.TryClipBlit(
            sourceRastPortAddress,
            sourceX,
            sourceY,
            destinationRastPortAddress,
            destinationX,
            destinationY,
            width,
            height,
            minterm) == true;

    public bool TryBltBitMapRastPort(
        uint sourceBitMapAddress,
        short sourceX,
        short sourceY,
        uint destinationRastPortAddress,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        out int result)
    {
        if (_raster is not null)
        {
            return _raster.TryBltBitMapRastPort(
                sourceBitMapAddress,
                sourceX,
                sourceY,
                destinationRastPortAddress,
                destinationX,
                destinationY,
                width,
                height,
                minterm,
                out result);
        }

        result = 0;
        return false;
    }

    public bool TryBltMaskBitMapRastPort(
        uint sourceBitMapAddress,
        short sourceX,
        short sourceY,
        uint destinationRastPortAddress,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        uint maskAddress)
        => _raster?.TryBltMaskBitMapRastPort(
            sourceBitMapAddress,
            sourceX,
            sourceY,
            destinationRastPortAddress,
            destinationX,
            destinationY,
            width,
            height,
            minterm,
            maskAddress) == true;

    public bool TryBltPattern(
        uint rastPortAddress,
        uint maskAddress,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        short bytesPerRow)
        => _raster?.TryBltPattern(
            rastPortAddress,
            maskAddress,
            xMin,
            yMin,
            xMax,
            yMax,
            bytesPerRow) == true;

    public bool TryBltTemplate(
        uint templateAddress,
        short sourceX,
        short sourceModulo,
        uint rastPortAddress,
        short destinationX,
        short destinationY,
        short width,
        short height)
        => _raster?.TryBltTemplate(
            templateAddress,
            sourceX,
            sourceModulo,
            rastPortAddress,
            destinationX,
            destinationY,
            width,
            height) == true;
}

/// <summary>
/// Optional host projection for the GELS display family.  The portable core
/// validates guest Bob/VSprite chains first; this adapter only forwards the
/// already-decoded addresses to a scheduler/display owner supplied by the
/// active CopperStart profile.
/// </summary>
internal sealed class CopperStartGraphicsGelsAdapter : IGraphicsGelsBackend
{
    private readonly CopperStartGraphicsContext _context;

    internal CopperStartGraphicsGelsAdapter(CopperStartGraphicsContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public void DrawGList(uint rastPort, uint viewPort)
        => _context.DrawGList?.Invoke(rastPort, viewPort);

    public void DoCollision(uint rastPort)
        => _context.DoCollision?.Invoke(rastPort);

    public void RemIBob(uint bob, uint rastPort, uint viewPort)
        => _context.RemIBob?.Invoke(bob, rastPort, viewPort);

    public void DispatchCollision(uint firstVSprite, uint secondVSprite, uint routineAddress)
        => _context.DispatchCollision?.Invoke(firstVSprite, secondVSprite, routineAddress);

    public void DispatchBoundary(uint vSprite, ushort boundaryFlags, uint routineAddress)
        => _context.DispatchBoundary?.Invoke(vSprite, boundaryFlags, routineAddress);
}

/// <summary>
/// Optional host projection for hardware sprites.  Guest SimpleSprite and
/// ExtSprite envelopes are committed by the portable core first; this bridge
/// only forwards the validated position/image transition to the active
/// CopperStart display owner.
/// </summary>
internal sealed class CopperStartGraphicsSpriteAdapter : IGraphicsSpriteBackend
{
    private readonly CopperStartGraphicsContext _context;

    internal CopperStartGraphicsSpriteAdapter(CopperStartGraphicsContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public void MoveSprite(uint viewPort, uint sprite, short x, short y)
        => _context.MoveSprite?.Invoke(viewPort, sprite, x, y);

    public void ChangeSprite(uint viewPort, uint sprite, uint imageData)
        => _context.ChangeSprite?.Invoke(viewPort, sprite, imageData);

    public bool TryGetExtSprite(
        uint extSprite,
        uint tags,
        short requested,
        uint attachedSprite,
        bool softSprite,
        bool scanDoubled,
        out int spriteNumber)
    {
        var result = _context.TryGetExtSprite?.Invoke(
            extSprite,
            tags,
            requested,
            attachedSprite,
            softSprite,
            scanDoubled);
        if (result is { Handled: true })
        {
            spriteNumber = result.Value.SpriteNumber;
            return true;
        }

        spriteNumber = GraphicsRasterOperations.Failure;
        return false;
    }

    public bool TryChangeExtSprite(
        uint viewPort,
        uint oldSprite,
        uint newSprite,
        uint tags,
        uint attachedSprite,
        bool softSprite,
        bool scanDoubled,
        out bool success)
    {
        var result = _context.TryChangeExtSprite?.Invoke(
            viewPort,
            oldSprite,
            newSprite,
            tags,
            attachedSprite,
            softSprite,
            scanDoubled);
        if (result is { Handled: true })
        {
            success = result.Value.Success;
            return true;
        }

        success = false;
        return false;
    }
}

/// <summary>
/// Scheduler-aware display bridge. It is deliberately outside the portable
/// namespace so View activation remains an explicit host/custom-chip boundary.
/// </summary>
internal sealed class CopperStartGraphicsDisplayAdapter :
    IGraphicsDisplayBackend,
    IGraphicsDisplayProfileBackend,
    IGraphicsDisplayChipsetBackend,
    IGraphicsPaletteBackend,
    IGraphicsTimedPaletteBackend,
    IGraphicsRgb32PaletteBackend,
    IGraphicsTimedRgb32PaletteBackend,
    IGraphicsTimedDisplayBackend,
    IGraphicsViewportDisplayBackend,
    IGraphicsTimedViewportDisplayBackend,
    IGraphicsViewportBitmapDisplayBackend,
    IGraphicsTimedViewportBitmapDisplayBackend,
    IGraphicsDoubleBufferCapabilityBackend,
    IGraphicsCopperBackend,
    IGraphicsCopperBuildStatusBackend,
    IGraphicsCopperBuildResourceStatusBackend,
    IGraphicsCopperMergeStatusBackend,
    IGraphicsCopperResourceBackend,
    IGraphicsCopperResourceStatusBackend,
    IGraphicsCopperNativeResourceStatusBackend,
    IGraphicsViewModeBackend,
    IGraphicsCalcIvgBackend,
    IGraphicsChipRevisionBackend,
    IGraphicsDoubleBufferMessageBackend,
    IGraphicsDoubleBufferMessageCancellationBackend,
    IGraphicsBeamPositionStatusBackend,
    IGraphicsViewPublicationStatusBackend,
    IGraphicsFrameWaitStatusBackend,
    IGraphicsViewportWaitStatusBackend,
    IGraphicsViewportPresentationStatusBackend
{
    private readonly CopperStartGraphicsContext _context;

    internal CopperStartGraphicsDisplayAdapter(CopperStartGraphicsContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    public bool IsNtsc => _context.DefaultMonitorNtsc;
    public bool SupportsEcsDisplay => _context.SupportsEcsDisplay;
    public bool SupportsAgaDisplay => _context.SupportsAgaDisplay;
    public bool HasExplicitVPortCopListOwner
        => _context.TryFreeVPortCopLists is not null;
    public bool HasExplicitCprListOwner
        => _context.TryFreeCprList is not null;
    public bool HasExplicitMakeVPortOwner
        => _context.MakeViewPort is not null;
    public bool CanPublishView
        => _context.TryLoadView is not null || _context.LoadView is not null;

    public void PublishView(uint viewAddress)
        => PublishView(viewAddress, 0);

    public void PublishView(uint viewAddress, long cycle)
    {
        var state = CreateState(cycle);
        state.A[1] = viewAddress;
        if (_context.LoadView is not null)
        {
            _context.LoadView(state);
            return;
        }

        // A status-aware host may expose only TryLoadView.  Keep the legacy
        // void-shaped timed boundary useful for callers that do not consume
        // a boolean result, while TryPublishView remains the result-bearing
        // path used by GraphicsLibraryCore.LoadView.
        _ = _context.TryLoadView?.Invoke(state);
    }

    public bool TryPublishView(uint viewAddress, long cycle)
    {
        if (_context.TryLoadView is not null)
        {
            var state = CreateState(cycle);
            state.A[1] = viewAddress;
            return _context.TryLoadView(state);
        }

        if (_context.LoadView is null)
            return false;

        PublishView(viewAddress, cycle);
        return true;
    }

    public void WaitForTopOfFrame()
        => _ = WaitForTopOfFrame(0);

    public long WaitForTopOfFrame(long cycle)
    {
        if (_context.WaitTof is null)
            return cycle;

        var state = CreateState(cycle);
        _context.WaitTof(state);
        return state.Cycles;
    }

    public bool TryWaitForTopOfFrame(long cycle, out long nextCycle)
    {
        if (_context.WaitTof is null)
        {
            nextCycle = cycle;
            return false;
        }

        nextCycle = WaitForTopOfFrame(cycle);
        return true;
    }

    public void WaitForBeginningOfVerticalBlank(uint viewPortAddress)
    {
        if (_context.WaitForBeginningOfVerticalBlank is not null)
        {
            _context.WaitForBeginningOfVerticalBlank(viewPortAddress);
            return;
        }

        // CopperStart normally supplies the scheduler-aware callback below
        // rather than a separate untimed action.  Keep the untimed graphics
        // vector observable on that path instead of silently dropping the
        // vertical-blank boundary.
        if (_context.WaitForViewportBottom is not null)
            _ = _context.WaitForViewportBottom(viewPortAddress, 0);
    }
    public long WaitForViewportBottom(uint viewPortAddress, long cycle)
    {
        if (_context.WaitForViewportBottom is not null)
            return _context.WaitForViewportBottom(viewPortAddress, cycle);

        _context.WaitForBeginningOfVerticalBlank?.Invoke(viewPortAddress);
        return cycle;
    }
    public bool TryWaitForViewportBottom(
        uint viewPortAddress,
        long cycle,
        out long nextCycle)
    {
        if (_context.WaitForViewportBottom is null &&
            _context.WaitForBeginningOfVerticalBlank is null)
        {
            nextCycle = cycle;
            return false;
        }

        nextCycle = WaitForViewportBottom(viewPortAddress, cycle);
        return true;
    }
    public void ScrollViewPort(uint viewPortAddress)
    {
        if (_context.RequestViewportDisplayRebuild is not null)
        {
            _context.RequestViewportDisplayRebuild(viewPortAddress);
            return;
        }

        _context.RequestDisplayRebuild?.Invoke();
    }
    public long ScrollViewPort(uint viewPortAddress, long cycle)
    {
        if (_context.RequestViewportDisplayRebuildTimed is not null)
        {
            _context.RequestViewportDisplayRebuildTimed(viewPortAddress, cycle);
            return cycle;
        }

        if (_context.RequestDisplayRebuildTimed is not null)
        {
            _context.RequestDisplayRebuildTimed(cycle);
            return cycle;
        }

        ScrollViewPort(viewPortAddress);
        return cycle;
    }
    public bool CanScrollViewPort =>
        _context.RequestDisplayRebuild is not null ||
        _context.RequestViewportDisplayRebuild is not null ||
        _context.RequestDisplayRebuildTimed is not null ||
        _context.RequestViewportDisplayRebuildTimed is not null;
    public bool CanChangeViewPortBitMap(uint dbufInfoAddress)
        => _context.RequestViewportDisplayRebuild is not null ||
            _context.RequestViewportDisplayRebuildTimed is not null ||
            _context.RequestDisplayRebuild is not null ||
            _context.RequestDisplayRebuildTimed is not null ||
            (dbufInfoAddress != 0 && _context.ScheduleDoubleBufferMessages is not null);
    public void ChangeViewPortBitMap(
        uint viewPortAddress,
        uint bitMapAddress,
        uint dbufInfoAddress)
    {
        _ = bitMapAddress;
        _ = dbufInfoAddress;
        if (_context.RequestViewportDisplayRebuild is not null)
        {
            _context.RequestViewportDisplayRebuild(viewPortAddress);
            return;
        }

        _context.RequestDisplayRebuild?.Invoke();
    }

    public long ChangeViewPortBitMap(
        uint viewPortAddress,
        uint bitMapAddress,
        uint dbufInfoAddress,
        long cycle)
    {
        _ = bitMapAddress;
        _ = dbufInfoAddress;
        if (_context.RequestViewportDisplayRebuildTimed is not null)
        {
            _context.RequestViewportDisplayRebuildTimed(viewPortAddress, cycle);
            return cycle;
        }

        if (_context.RequestDisplayRebuildTimed is not null)
            _context.RequestDisplayRebuildTimed(cycle);
        else if (_context.RequestViewportDisplayRebuild is not null)
            _context.RequestViewportDisplayRebuild(viewPortAddress);
        else
            _context.RequestDisplayRebuild?.Invoke();
        return cycle;
    }
    public void ScheduleDoubleBufferMessages(
        uint viewPortAddress,
        uint previousBitMapAddress,
        uint bitMapAddress,
        uint dbufInfoAddress,
        long cycle)
        => _context.ScheduleDoubleBufferMessages?.Invoke(
            viewPortAddress,
            previousBitMapAddress,
            bitMapAddress,
            dbufInfoAddress,
            cycle);

    public void CancelDoubleBufferMessages(uint dbufInfoAddress)
        => _context.CancelDoubleBufferMessages?.Invoke(dbufInfoAddress);
    public bool SupportsDoubleBuffer(uint viewPortAddress)
    {
        if (_context.GetViewPortModeId is null)
            return true;

        return GraphicsDisplayDatabase.SupportsDoubleBuffering(
            _context.GetViewPortModeId(viewPortAddress),
            _context.DefaultMonitorNtsc,
            _context.SupportsEcsDisplay);
    }
    public void MakeViewPort(uint viewAddress, uint viewPortAddress)
    {
        var state = CreateState(0);
        state.A[0] = viewAddress;
        state.A[1] = viewPortAddress;
        _ = _context.MakeViewPort(state);
    }
    public int MakeViewPortStatus(uint viewAddress, uint viewPortAddress)
    {
        var state = CreateState(0);
        state.A[0] = viewAddress;
        state.A[1] = viewPortAddress;
        return unchecked((int)_context.MakeViewPort(state));
    }

    public void MergeCopperLists(uint viewAddress)
    {
        var state = CreateState(0);
        state.A[1] = viewAddress;
        _ = _context.MergeCopperLists(state);
    }
    public int MergeCopperListsStatus(uint viewAddress)
    {
        var state = CreateState(0);
        state.A[1] = viewAddress;
        return unchecked((int)_context.MergeCopperLists(state));
    }
    public void FreeCprList(uint cprListAddress)
        => _context.FreeCprList?.Invoke(cprListAddress);
    public void FreeVPortCopLists(uint viewPortAddress)
        => _context.FreeVPortCopLists?.Invoke(viewPortAddress);
    public bool TryFreeCprList(uint cprListAddress)
    {
        if (_context.TryFreeCprList is not null)
            return _context.TryFreeCprList(cprListAddress);

        if (_context.FreeCprList is null)
            return false;

        _context.FreeCprList(cprListAddress);
        return true;
    }
    public bool TryFreeVPortCopLists(uint viewPortAddress)
    {
        if (_context.TryFreeVPortCopLists is not null)
            return _context.TryFreeVPortCopLists(viewPortAddress);

        if (_context.FreeVPortCopLists is null)
            return false;

        _context.FreeVPortCopLists(viewPortAddress);
        return true;
    }
    public bool TryGetModeId(uint viewPortAddress, out uint modeId)
    {
        if (_context.GetViewPortModeId is not null)
        {
            modeId = _context.GetViewPortModeId(viewPortAddress);
            return modeId != GraphicsModeIds.Invalid;
        }

        modeId = GraphicsModeIds.Invalid;
        return false;
    }

    public bool TryCalcIvg(uint viewAddress, uint viewPortAddress, out ushort scanLines)
    {
        if (_context.CalcIvg is null)
        {
            scanLines = 0;
            return false;
        }

        scanLines = _context.CalcIvg(viewAddress, viewPortAddress);
        return true;
    }

    public bool TrySetChipRev(uint requestedBits, out uint actualBits)
    {
        if (_context.SetChipRev is null)
        {
            actualBits = 0;
            return false;
        }

        actualBits = _context.SetChipRev(requestedBits);
        return true;
    }

    public ushort GetBeamPosition() => 0;
    public ushort GetBeamPosition(long cycle) => _context.GetBeamPosition?.Invoke(cycle) ?? 0;
    public bool TryGetBeamPosition(long cycle, out ushort beamPosition)
    {
        if (_context.GetBeamPosition is null)
        {
            beamPosition = 0;
            return false;
        }

        beamPosition = _context.GetBeamPosition(cycle);
        return true;
    }

    public void LoadRgb4(uint viewPortAddress, uint colorsAddress, short count)
        => LoadRgb4(viewPortAddress, colorsAddress, count, 0);

    public void LoadRgb4(uint viewPortAddress, uint colorsAddress, short count, long cycle)
    {
        var state = CreateState(cycle);
        state.A[0] = viewPortAddress;
        state.A[1] = colorsAddress;
        state.D[0] = unchecked((uint)(ushort)count);
        _context.LoadRgb4(state);
    }

    public void SetRgb4(uint viewPortAddress, short index, byte red, byte green, byte blue)
        => SetRgb4(viewPortAddress, index, red, green, blue, 0);

    public void SetRgb4(uint viewPortAddress, short index, byte red, byte green, byte blue, long cycle)
    {
        var state = CreateState(cycle);
        state.A[0] = viewPortAddress;
        state.D[0] = unchecked((uint)(ushort)index);
        state.D[1] = red;
        state.D[2] = green;
        state.D[3] = blue;
        _context.SetRgb4(state);
    }

    public void SetRgb32(uint viewPortAddress, uint index, byte red, byte green, byte blue)
        => SetRgb32(viewPortAddress, index, red, green, blue, 0);

    public void SetRgb32(uint viewPortAddress, uint index, byte red, byte green, byte blue, long cycle)
    {
        var state = CreateState(cycle);
        state.A[0] = viewPortAddress;
        state.D[0] = index;
        state.D[1] = red;
        state.D[2] = green;
        state.D[3] = blue;

        // The RGB8 callback is optional so standalone CopperSharp68k hosts
        // retain the existing OCS/ECS COLOR-register projection unchanged.
        if (_context.SetRgb32 is not null)
            _context.SetRgb32(state);
        else
        {
            state.D[1] = (uint)(red >> 4);
            state.D[2] = (uint)(green >> 4);
            state.D[3] = (uint)(blue >> 4);
            _context.SetRgb4(state);
        }
    }

    private static M68kCpuState CreateState(long cycle)
        => new() { Cycles = cycle };
}

/// <summary>
/// Register adapter for the first pure-memory graphics.library vectors,
/// allocator boundary, and scheduler-aware display calls.
/// </summary>
internal sealed class CopperStartGraphicsRegisterAdapter
{
    private readonly GraphicsLibraryCore _core;
    private readonly GraphicsMemoryFontBackend _fonts;
    private readonly Func<uint>? _ensureCompatibilityFont;
    private readonly Func<uint, bool>? _isRtgBitMap;
    private readonly Func<uint, bool>? _isRtgRastPort;
    private readonly Func<bool>? _preferProviderBitMapAllocations;
    private readonly Func<uint, uint, bool>? _isWritableMemoryRange;
    private uint _nativeOverlayGraphicsBase;
    private readonly IGraphicsFontListBackend? _fontList;
    private readonly IGraphicsLayerRasterBackend? _layerRaster;
    private readonly bool _hasLayerLockProvider;

    internal CopperStartGraphicsRegisterAdapter(
        GraphicsLibraryCore core,
        Func<uint>? ensureCompatibilityFont = null,
        Func<uint, bool>? isRtgBitMap = null,
        Func<uint, bool>? isRtgRastPort = null,
        IGraphicsFontListBackend? fontList = null,
        IGraphicsLayerRasterBackend? layerRaster = null,
        Func<bool>? preferProviderBitMapAllocations = null,
        Func<uint, uint, bool>? isWritableMemoryRange = null,
        bool hasLayerLockProvider = false)
    {
        _core = core;
        _ensureCompatibilityFont = ensureCompatibilityFont;
        _isRtgBitMap = isRtgBitMap;
        _isRtgRastPort = isRtgRastPort;
        _preferProviderBitMapAllocations = preferProviderBitMapAllocations;
        _isWritableMemoryRange = isWritableMemoryRange;
        _fontList = fontList;
        _layerRaster = layerRaster;
        _hasLayerLockProvider = hasLayerLockProvider;
        _fonts = new GraphicsMemoryFontBackend(
            core.Memory,
            core.Allocator,
            ensureCompatibilityFont,
            fontList);
    }

    internal bool InitializeViewPort(uint viewPort)
        => _core.InitializeViewPort(viewPort);

    internal bool InitializeRasInfo(uint rasInfo)
        => _core.InitializeRasInfo(rasInfo);

    internal uint RebindGraphicsLibraryBase(uint graphicsLibraryBase)
        => _core.RebindGraphicsLibraryBase(graphicsLibraryBase);

    internal bool IsBoundToGraphicsLibraryBase(uint graphicsLibraryBase)
        => _core.IsBoundToGraphicsLibraryBase(graphicsLibraryBase);

    /// <summary>
    /// Runs a host-side display/lifetime transaction under the same provider
    /// gate used by native-overlay publication and graphics-base rebind. The
    /// Intuition rethink path uses this because it rebuilds the View outside a
    /// graphics LVO but still commits ActiView and copper ownership as one
    /// lifecycle boundary.
    /// </summary>
    internal bool WithMonitorStateLock(Func<bool> operation)
        => _core.WithMonitorStateLock(operation);

    internal void SetNativeOverlayGraphicsBase(uint graphicsLibraryBase)
        => _nativeOverlayGraphicsBase = graphicsLibraryBase;

    /// <summary>
    /// Native-overlay SetChipRev may publish the capability byte only when a
    /// mapped native GfxBase field is writable.  An absent optional tail is
    /// deliberately not a decline: compact CopperScreen images retain the
    /// compatibility provider until a full native GfxBase is present.
    /// </summary>
    private bool CanPublishNativeChipRevision()
    {
        var graphicsBase = _nativeOverlayGraphicsBase != 0
            ? _nativeOverlayGraphicsBase
            : _core.GraphicsLibraryBase;
        if (!_core.TryGetNativeLoadViewPublicationSpan(
                graphicsBase,
                out var publicationAddress,
                out _))
        {
            // Odd, wrapping, and fully mapped malformed native envelopes
            // remain with the resident/provider vector. A zero base or a
            // compact compatibility image still has no native sidecar to
            // preflight and remains claimable.
            return false;
        }

        if (publicationAddress == 0)
            return true;

        var fieldAddress64 = (ulong)graphicsBase +
            (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        if (fieldAddress64 > uint.MaxValue)
            return false;

        var fieldAddress = (uint)fieldAddress64;
        if (!_core.Memory.TryReadByte(fieldAddress, out _))
            return true;

        return _isWritableMemoryRange?.Invoke(fieldAddress, 1) ?? true;
    }

    /// <summary>
    /// Native-overlay InitRastPort publishes a complete classic structure,
    /// including its cleared prefix and optional default-font fields. Admit
    /// that output only when the visible guest span is writable, so a
    /// provider/image overlay cannot trigger host font resolution before
    /// handing the vector back to its owner.
    /// </summary>
    private bool CanPublishNativeRastPort(uint rastPort, int byteCount)
    {
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return true;

        return CanPublishNativeSpan(
            rastPort,
            byteCount);
    }

    /// <summary>
    /// Native-overlay structure initializers clear and republish every byte of
    /// their public envelope.  A visible writable-span check lets malformed or
    /// read-only provider mappings tail-chain before the transactional core
    /// has to snapshot guest memory.
    /// </summary>
    private bool CanPublishNativeSpan(uint address, int byteCount)
        => CanPublishNativeSpan(address, (ulong)byteCount);

    private bool CanPublishNativeSpan(uint address, ulong byteCount)
    {
        if (address == 0 || (address & 1u) != 0 || byteCount == 0 ||
            byteCount > uint.MaxValue)
            return true;

        return _isWritableMemoryRange?.Invoke(address, (uint)byteCount) ?? true;
    }

    /// <summary>
    /// GfxFree clears an owned ViewExtra/ViewPortExtra association LONG before
    /// releasing the extended-node allocation. Preflight that mutable field
    /// (and the registry's metadata/backlink reads) so a native/provider
    /// overlay cannot observe a partial guest write from a failed free.
    /// MonitorSpec and SpecialMonitor nodes return no backlink and therefore
    /// need only the registry validation performed by the core planner.
    /// </summary>
    private bool CanPublishNativeGfxFree(uint node)
    {
        if (!_core.TryGetNativeGfxFreePublication(
                node,
                out var backlink))
        {
            return false;
        }

        return backlink == 0 ||
            CanPublishNativeSpan(backlink, sizeof(uint));
    }

    /// <summary>
    /// GfxAssociate clears VPXF_FREE_ME on a temporary ViewPortExtra, then
    /// publishes the new node backlink before clearing a displaced node
    /// backlink. Check every complete envelope before entering the registry
    /// transaction so a provider/native mapping cannot observe a partial
    /// association.
    /// </summary>
    private bool CanPublishNativeGfxAssociate(uint pointer, uint node)
    {
        if (!_core.TryGetNativeGfxAssociatePublication(
                pointer,
                node,
                out var temporaryFlags,
                out var hasTemporaryFlags,
                out var first,
                out var second,
                out var count))
        {
            return false;
        }

        return (!hasTemporaryFlags ||
                CanPublishNativeSpan(temporaryFlags, sizeof(ushort))) &&
            (count < 1 || CanPublishNativeSpan(first, sizeof(uint))) &&
            (count < 2 || CanPublishNativeSpan(second, sizeof(uint)));
    }

    /// <summary>
    /// A CopperSharp68k/FIFO queue may publish the classic <c>bltnode.n</c>
    /// link before submitting a queued operation. The address-only/provider
    /// queues deliberately do not claim this write. For a linked owner,
    /// preflight the node's own link word so a read-only native node reaches
    /// its resident vector before the queue transaction is attempted. A
    /// malformed or unmapped node remains a core validation failure rather
    /// than becoming an additional overlay ownership rule.
    /// </summary>
    private bool CanPublishNativeQueuedBlitterNode(uint node)
    {
        if (!_core.HasQueuedBlitterLinkBackend ||
            node == 0 ||
            (node & 1u) != 0 ||
            !CanAddress(node, GraphicsLayouts.BltNodeNext, sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                node + (uint)GraphicsLayouts.BltNodeNext,
                out _))
        {
            return true;
        }

        return CanPublishNativeSpan(
            node + (uint)GraphicsLayouts.BltNodeNext,
            sizeof(uint));
    }

    /// <summary>
    /// LoadView publishes ActiView and, for extended Views, the optional
    /// CurrentMonitor/TopLine sidecars in the native GfxBase.  Admit the
    /// complete validated resident envelope before the display handoff so a
    /// read-only native/provider base remains available to its owner.
    /// </summary>
    private bool CanPublishNativeLoadView()
    {
        var graphicsBase = _nativeOverlayGraphicsBase != 0
            ? _nativeOverlayGraphicsBase
            : _core.GraphicsLibraryBase;
        if (!_core.TryGetNativeLoadViewPublicationSpan(
                graphicsBase,
                out var address,
                out var bytes))
            return false;

        return address == 0 || CanPublishNativeSpan(address, bytes);
    }

    /// <summary>
    /// Native-overlay pixel-array results are byte-per-pen guest buffers, not
    /// word-aligned public structures.  Keep odd array addresses in the
    /// writable-range probe while leaving NULL, empty, malformed, and
    /// wrapping requests to the portable/provider validation boundary.
    /// </summary>
    private bool CanPublishNativePixelArrayOutput(
        uint array,
        long width,
        long height)
    {
        if (array == 0 || width <= 0 || height <= 0 ||
            width > int.MaxValue || height > int.MaxValue ||
            !GraphicsPixelArrayOperations.TryGetPaddedStrideForStatus(
                (int)width,
                out var stride))
        {
            return true;
        }

        var byteCount = (ulong)stride * (ulong)height;
        if (byteCount == 0 || byteCount > uint.MaxValue)
            return true;

        return CanPublishNativeByteSpan(array, byteCount);
    }

    private bool CanPublishNativeByteSpan(uint address, ulong byteCount)
    {
        if (address == 0 || byteCount == 0 || byteCount > uint.MaxValue)
            return true;

        return _isWritableMemoryRange?.Invoke(address, (uint)byteCount) ?? true;
    }

    private bool CanPublishNativeBltClear(
        uint address,
        uint byteCount,
        uint flags)
    {
        var rowsMode = (flags & 0x2u) != 0;
        var bytesPerRow = rowsMode ? byteCount & 0xFFFFu : byteCount;
        var rows = rowsMode ? byteCount >> 16 : 1u;

        // Preserve BltClear's bounded no-write forms and leave malformed or
        // unstageable requests to the existing portable/native ownership seam.
        if ((!rowsMode && bytesPerRow == 0) ||
            (rowsMode && (bytesPerRow == 0 || rows == 0)) ||
            address == 0 || (address & 1u) != 0 ||
            (bytesPerRow & 1u) != 0)
        {
            return true;
        }

        var totalBytes = (ulong)bytesPerRow * rows;
        if (totalBytes == 0 || totalBytes > uint.MaxValue ||
            address > uint.MaxValue - (uint)(totalBytes - 1) ||
            totalBytes > int.MaxValue ||
            totalBytes > (uint)(int.MaxValue / 3))
        {
            return true;
        }

        return CanPublishNativeByteSpan(address, totalBytes);
    }

    /// <summary>
    /// Native-overlay palette setters publish into the ColorMap's guest
    /// ColorTable (and, for V36/V39 maps, its LowColorBits companion).  The
    /// portable transaction already rolls back a failed WORD, but admission
    /// must reject a read-only native/provider table before taking the
    /// graphics-owned color-map lock or attempting a partial write.
    /// </summary>
    private bool CanPublishNativeColorMapEntry(uint colorMap, uint index)
    {
        if (colorMap == 0 || (colorMap & 1u) != 0 ||
            !CanAddress(colorMap, GraphicsLayouts.ColorMapCount, sizeof(ushort)) ||
            !_core.Memory.TryReadWord(
                colorMap + (uint)GraphicsLayouts.ColorMapCount,
                out var count) ||
            index >= count)
        {
            return true;
        }

        if (!CanAddress(
                colorMap,
                GraphicsLayouts.ColorMapColorTable,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                colorMap + (uint)GraphicsLayouts.ColorMapColorTable,
                out var colorTable) ||
            colorTable == 0 ||
            (colorTable & 1u) != 0)
        {
            return true;
        }

        var entryOffset = (ulong)index * sizeof(ushort);
        if (entryOffset > uint.MaxValue ||
            !CanAddress(colorTable, (int)entryOffset, sizeof(ushort)))
        {
            return true;
        }

        if (!CanPublishNativeByteSpan(
                colorTable + (uint)entryOffset,
                sizeof(ushort)))
        {
            return false;
        }

        if (!CanAddress(colorMap, GraphicsLayouts.ColorMapType, sizeof(byte)) ||
            !_core.Memory.TryReadByte(
                colorMap + (uint)GraphicsLayouts.ColorMapType,
                out var mapType) ||
            mapType == 0)
        {
            return true;
        }

        if (!CanAddress(
                colorMap,
                GraphicsLayouts.ColorMapLowColorBits,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                colorMap + (uint)GraphicsLayouts.ColorMapLowColorBits,
                out var lowColorBits) ||
            lowColorBits == 0 ||
            (lowColorBits & 1u) != 0 ||
            !CanAddress(lowColorBits, (int)entryOffset, sizeof(ushort)))
        {
            return true;
        }

        return CanPublishNativeByteSpan(
            lowColorBits + (uint)entryOffset,
            sizeof(ushort));
    }

    private bool CanPublishNativeColorMapTable(uint colorMap)
    {
        if (colorMap == 0 || (colorMap & 1u) != 0 ||
            !CanAddress(colorMap, GraphicsLayouts.ColorMapCount, sizeof(ushort)) ||
            !_core.Memory.TryReadWord(
                colorMap + (uint)GraphicsLayouts.ColorMapCount,
                out var count) ||
            count == 0)
        {
            return true;
        }

        if (!CanAddress(
                colorMap,
                GraphicsLayouts.ColorMapColorTable,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                colorMap + (uint)GraphicsLayouts.ColorMapColorTable,
                out var colorTable) ||
            colorTable == 0 ||
            (colorTable & 1u) != 0)
        {
            return true;
        }

        var tableBytes = (ulong)count * sizeof(ushort);
        if (tableBytes > uint.MaxValue ||
            !CanAddress(colorTable, 0, (int)tableBytes) ||
            !CanPublishNativeByteSpan(colorTable, tableBytes))
        {
            return false;
        }

        if (!CanAddress(colorMap, GraphicsLayouts.ColorMapType, sizeof(byte)) ||
            !_core.Memory.TryReadByte(
                colorMap + (uint)GraphicsLayouts.ColorMapType,
                out var mapType) ||
            mapType == 0)
        {
            return true;
        }

        if (!CanAddress(
                colorMap,
                GraphicsLayouts.ColorMapLowColorBits,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                colorMap + (uint)GraphicsLayouts.ColorMapLowColorBits,
                out var lowColorBits) ||
            lowColorBits == 0 ||
            (lowColorBits & 1u) != 0 ||
            !CanAddress(lowColorBits, 0, (int)tableBytes))
        {
            return true;
        }

        return CanPublishNativeByteSpan(lowColorBits, tableBytes);
    }

    private bool CanPublishNativeViewPortColorMapEntry(
        uint viewPort,
        uint index)
    {
        if (viewPort == 0 || (viewPort & 1u) != 0 ||
            !CanAddress(
                viewPort,
                GraphicsLayouts.ViewPortColorMap,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortColorMap,
                out var colorMap))
        {
            return true;
        }

        return CanPublishNativeColorMapEntry(colorMap, index);
    }

    private bool CanPublishNativeViewPortColorMapTable(uint viewPort)
    {
        if (viewPort == 0 || (viewPort & 1u) != 0 ||
            !CanAddress(
                viewPort,
                GraphicsLayouts.ViewPortColorMap,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortColorMap,
                out var colorMap))
        {
            return true;
        }

        return CanPublishNativeColorMapTable(colorMap);
    }

    /// <summary>
    /// Region boolean vectors republish the destination Region header and,
    /// for allocation-free AndRectRegion, rewrite each existing
    /// RegionRectangle node in place.  New nodes are allocated through the
    /// portable allocator and are therefore covered by its own rollback
    /// boundary; the native overlay only needs to admit the already-visible
    /// guest spans before claiming the call.
    /// </summary>
    private bool CanPublishNativeRegion(uint region, bool includeExistingNodes)
    {
        if (region == 0 || (region & 1u) != 0)
            return true;

        if (!CanPublishNativeSpan(region, GraphicsLayouts.RegionSize))
            return false;

        if (!includeExistingNodes ||
            !CanAddress(region, GraphicsLayouts.RegionRectangle, sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                region + (uint)GraphicsLayouts.RegionRectangle,
                out var node))
        {
            return true;
        }

        var visited = new HashSet<uint>();
        for (var count = 0; node != 0 && count < 4096; count++)
        {
            if ((node & 1u) != 0 || !visited.Add(node))
                return true;

            if (!CanPublishNativeSpan(
                    node,
                    GraphicsLayouts.RegionRectangleSize))
            {
                return false;
            }

            if (!CanAddress(
                    node,
                    GraphicsLayouts.RegionRectangleNext,
                    sizeof(uint)) ||
                !_core.Memory.TryReadLong(
                    node + (uint)GraphicsLayouts.RegionRectangleNext,
                    out node))
            {
                return true;
            }
        }

        // A malformed/cyclic chain remains available to the native/provider
        // owner.  The portable core will perform its bounded chain check if
        // the native mapping is otherwise writable.
        return node == 0;
    }

    /// <summary>
    /// BitMapScale writes both destination planes and the two destination
    /// extent words in BitScaleArgs. The argument-record span is admitted by
    /// the caller, while this helper checks the scaled destination rectangle
    /// against the common source/destination plane mask before the portable
    /// scaler stages samples or publishes either result.
    /// </summary>
    private bool CanPublishNativeBitMapScaleDestination(
        uint bitScaleArgs,
        uint sourceBitMap,
        uint destinationBitMap)
    {
        if (bitScaleArgs == 0 || (bitScaleArgs & 1u) != 0 ||
            !CanAddress(
                bitScaleArgs,
                GraphicsLayouts.BitScaleArgsSrcWidth,
                sizeof(ushort)) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsSrcWidth,
                out var sourceWidth) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsSrcHeight,
                out var sourceHeight) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsXSrcFactor,
                out var xSourceFactor) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsYSrcFactor,
                out var ySourceFactor) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestX,
                out var destinationX) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsDestY,
                out var destinationY) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsXDestFactor,
                out var xDestinationFactor) ||
            !_core.Memory.TryReadWord(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsYDestFactor,
                out var yDestinationFactor) ||
            !_core.Memory.TryReadLong(
                bitScaleArgs + (uint)GraphicsLayouts.BitScaleArgsFlags,
                out var flags))
        {
            return true;
        }

        if (flags != 0 || sourceWidth == 0 || sourceHeight == 0 ||
            xSourceFactor == 0 || xSourceFactor > 0x3FFF ||
            ySourceFactor == 0 || ySourceFactor > 0x3FFF ||
            xDestinationFactor == 0 || xDestinationFactor > 0x3FFF ||
            yDestinationFactor == 0 || yDestinationFactor > 0x3FFF ||
            !TryReadNativeBitmapHeader(sourceBitMap, out var source) ||
            !TryReadNativeBitmapHeader(destinationBitMap, out var destination))
        {
            return true;
        }

        var destinationWidth = GraphicsBitmapUtilityOperations.ScalerDiv(
            sourceWidth,
            xDestinationFactor,
            xSourceFactor);
        var destinationHeight = GraphicsBitmapUtilityOperations.ScalerDiv(
            sourceHeight,
            yDestinationFactor,
            ySourceFactor);
        if (destinationWidth == 0 || destinationHeight == 0)
            return true;

        var commonDepth = Math.Min(source.Depth, destination.Depth);
        if (commonDepth == 0)
            return true;

        var planeMask = (byte)((1 << commonDepth) - 1);
        return CanPublishNativeBitmapDestinationRegion(
            destination,
            destinationX,
            destinationY,
            (int)destinationX + destinationWidth - 1,
            (int)destinationY + destinationHeight - 1,
            planeMask);
    }

    /// <summary>
    /// ChangeVPBitMap publishes the selected bitmap through the viewport's
    /// RasInfo link.  The replacement bitmap and DBufInfo are read-only
    /// inputs at this pure-memory boundary; admit a native overlay only when
    /// the one guest LONG that changes association is writable.
    /// </summary>
    private bool CanPublishNativeChangeViewPortBitMap(uint viewPort)
    {
        if (viewPort == 0 || (viewPort & 1u) != 0 ||
            !CanAddress(
                viewPort,
                GraphicsLayouts.ViewPortRasInfo,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var rasInfo) ||
            rasInfo == 0 || (rasInfo & 1u) != 0 ||
            !CanAddress(
                rasInfo,
                GraphicsLayouts.RasInfoBitMap,
                sizeof(uint)))
        {
            return true;
        }

        return CanPublishNativeSpan(
            rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
            sizeof(uint));
    }

    /// <summary>
    /// MakeVPort publishes the four public copper-list links in the target
    /// ViewPort.  The copper builder journals those links transactionally,
    /// but a native overlay must decide ownership before it enters the
    /// builder: a read-only resident ViewPort belongs to Kickstart/provider
    /// code and must not be claimed merely because its readable geometry is
    /// otherwise suitable for the portable path.
    /// </summary>
    private bool CanPublishNativeViewPortCopperLinks(uint viewPort)
    {
        if (viewPort == 0 || (viewPort & 1u) != 0)
            return true;

        var offsets = new[]
        {
            GraphicsLayouts.ViewPortDspIns,
            GraphicsLayouts.ViewPortSprIns,
            GraphicsLayouts.ViewPortClrIns,
            GraphicsLayouts.ViewPortUCopIns
        };

        foreach (var offset in offsets)
        {
            // An unaddressable field remains the portable validator's
            // malformed/provider boundary.  Only an addressable field that
            // is explicitly read-only is an ownership refusal here.
            if (CanAddress(viewPort, offset, sizeof(uint)) &&
                !CanPublishNativeSpan(
                    viewPort + (uint)offset,
                    sizeof(uint)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// MakeVPort replays an owned ColorMap's static VideoControl batch before
    /// crossing the copper boundary. That replay is transactional, but its
    /// commit still changes the guest ColorMap envelope; admit the complete
    /// span alongside the four public copper links.
    /// </summary>
    private bool CanPublishNativeMakeViewPort(uint viewPort)
    {
        if (!CanPublishNativeViewPortCopperLinks(viewPort) ||
            !_core.CanClaimNativeMakeViewPortResources(viewPort) ||
            !_core.TryGetNativeMakeViewPortPublication(
                viewPort,
                out var colorMap,
                out var bytes))
        {
            return false;
        }

        return colorMap == 0 ||
            (CanPublishNativeSpan(colorMap, bytes) &&
             _core.IsNativeMakeViewPortBatchPublicationSafe(
                 viewPort,
                 CanPublishNativeSpan));
    }

    /// <summary>
    /// FreeVPortCopLists may chain from the four copper links into an owned
    /// temporary ViewPortExtra.  The extended-node free clears its public
    /// ViewPort backlink immediately before releasing the allocation, so a
    /// native overlay must admit that final LONG before clearing any links.
    /// </summary>
    private bool CanPublishNativeFreeViewPortCopLists(uint viewPort)
    {
        if (!_core.TryGetNativeFreeVPortCopListsPublication(
                viewPort,
                out var backlink))
        {
            return false;
        }

        return backlink == 0 ||
            CanPublishNativeSpan(backlink, sizeof(uint));
    }

    /// <summary>
    /// MrgCop publishes the same four links for every visible ViewPort in a
    /// View chain. Hidden ViewPorts are skipped by the resident merge loop,
    /// so their sparse/read-only copper fields must not steal ownership from
    /// an otherwise valid visible merge.
    /// </summary>
    private bool CanPublishNativeVisibleCopperLinks(uint view)
    {
        if (view == 0 || (view & 1u) != 0 ||
            !CanAddress(view, GraphicsLayouts.ViewViewPort, sizeof(uint)))
        {
            return true;
        }

        if (!_core.Memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                out var viewPort))
        {
            return true;
        }

        for (var index = 0; index < 64 && viewPort != 0; index++)
        {
            var current = viewPort;
            if ((current & 1u) != 0 ||
                !CanAddress(current, GraphicsLayouts.ViewPortModes, sizeof(ushort)) ||
                !CanAddress(current, GraphicsLayouts.ViewPortNext, sizeof(uint)) ||
                !_core.Memory.TryReadWord(
                    current + (uint)GraphicsLayouts.ViewPortModes,
                    out var modes) ||
                !_core.Memory.TryReadLong(
                    current + (uint)GraphicsLayouts.ViewPortNext,
                    out viewPort))
            {
                return true;
            }

            if ((modes & GraphicsModeIds.ViewPortHidden) != 0)
                continue;

            if (!CanPublishNativeViewPortCopperLinks(current))
                return false;
        }

        // A malformed/overlong chain remains the core validator's boundary;
        // this helper only refuses an explicitly read-only output span.
        return true;
    }

    private bool CanPublishNativeCopperField(
        uint address,
        int offset,
        int byteCount)
    {
        if (!CanAddress(address, offset, byteCount))
            return true;

        return CanPublishNativeSpan(
            address + (uint)offset,
            byteCount);
    }

    private bool CanPublishNativeCopperBump(uint userList)
    {
        if (!_core.TryGetCopperBumpPublicationState(
                userList,
                out var copList,
                out var count,
                out var maxCount,
                out var nextCopList))
        {
            return false;
        }

        if (count < maxCount)
        {
            return CanPublishNativeCopperField(
                       copList,
                       GraphicsLayouts.CopListCopPtr,
                       sizeof(uint)) &&
                   CanPublishNativeCopperField(
                       copList,
                       GraphicsLayouts.CopListCount,
                       sizeof(ushort));
        }

        if (!CanPublishNativeCopperField(
                userList,
                GraphicsLayouts.UCopListCopList,
                sizeof(uint)))
        {
            return false;
        }

        return nextCopList != 0 ||
            CanPublishNativeCopperField(
                copList,
                GraphicsLayouts.CopListNext,
                sizeof(uint));
    }

    /// <summary>
    /// MoveSprite publishes the SimpleSprite X/Y words and, when present,
    /// the two hardware position/control words referenced by PosCtlData.
    /// Refuse an explicitly read-only native overlay before the portable
    /// transaction can partially move the guest sprite.
    /// </summary>
    private bool CanPublishNativeMoveSprite(uint sprite)
    {
        if (sprite == 0 || (sprite & 1u) != 0)
            return true;

        if (CanAddress(
                sprite,
                GraphicsLayouts.SimpleSpriteX,
                sizeof(ushort)) &&
            !CanPublishNativeSpan(
                sprite + (uint)GraphicsLayouts.SimpleSpriteX,
                sizeof(ushort)))
        {
            return false;
        }

        if (CanAddress(
                sprite,
                GraphicsLayouts.SimpleSpriteY,
                sizeof(ushort)) &&
            !CanPublishNativeSpan(
                sprite + (uint)GraphicsLayouts.SimpleSpriteY,
                sizeof(ushort)))
        {
            return false;
        }

        if (!CanAddress(
                sprite,
                GraphicsLayouts.SimpleSpritePosCtlData,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
                out var posCtlData) ||
            posCtlData == 0 ||
            (posCtlData & 1u) != 0)
        {
            return true;
        }

        return !CanAddress(posCtlData, 0, GraphicsLayouts.SpriteImagePosCtlSize) ||
               CanPublishNativeSpan(
                   posCtlData,
                   GraphicsLayouts.SpriteImagePosCtlSize);
    }

    /// <summary>
    /// GetSprite publishes only the caller-owned SimpleSprite <c>num</c>
    /// word.  Probe that output span through the native-overlay writability
    /// boundary before the portable pool selector can claim a resident or
    /// provider sprite.  The complete envelope remains the core validator's
    /// responsibility, so malformed/unaddressable structures are left to the
    /// native/provider path rather than being rejected by this admission
    /// helper.
    /// </summary>
    private bool CanPublishNativeGetSprite(uint sprite)
    {
        if (sprite == 0 || (sprite & 1u) != 0 ||
            !CanAddress(
                sprite,
                GraphicsLayouts.SimpleSpriteNum,
                sizeof(ushort)))
        {
            return true;
        }

        return CanPublishNativeSpan(
            sprite + (uint)GraphicsLayouts.SimpleSpriteNum,
            sizeof(ushort));
    }

    /// <summary>
    /// InitGels clears both sentinel VSprite envelopes and republishes the
    /// GelsInfo links/flags plus an optional collision-handler LONG.  Resolve
    /// those exact destinations before the native overlay can claim the
    /// transaction; malformed envelopes remain with the core/native validator.
    /// </summary>
    private bool CanPublishNativeInitGels(
        uint head,
        uint tail,
        uint gelsInfo)
    {
        if (!_core.TryGetInitGelsPublicationSpans(
                head,
                tail,
                gelsInfo,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// SetCollision republishes one collision-handler table LONG selected by
    /// the guest number.  Probe that exact entry before native-overlay
    /// dispatch so a resident/provider table remains available when read-only.
    /// </summary>
    private bool CanPublishNativeSetCollision(
        uint number,
        uint gelsInfo)
    {
        if (!_core.TryGetSetCollisionPublicationSpans(
                number,
                gelsInfo,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// InitMasks writes the caller-owned border and collision-mask buffers
    /// after reading the source image.  Admit only when both output spans are
    /// writable in a native overlay; malformed source/image envelopes remain
    /// with the core/native validator.
    /// </summary>
    private bool CanPublishNativeInitMasks(uint sprite)
    {
        if (!_core.TryGetInitMasksPublicationSpans(
                sprite,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// AddVSprite publishes four reciprocal links and the user/system flag
    /// word.  Resolve that exact list insertion before claiming a native
    /// overlay vector so a provider-owned list cannot be partially relinked.
    /// </summary>
    private bool CanPublishNativeAddVSprite(uint sprite, uint rastPort)
    {
        if (!_core.TryGetAddVSpritePublicationSpans(
                sprite,
                rastPort,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>RemVSprite clears two reciprocal and two self links.</summary>
    private bool CanPublishNativeRemVSprite(uint sprite)
    {
        if (!_core.TryGetRemVSpritePublicationSpans(
                sprite,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// SortGList rewrites every link in the bounded chain.  Preflight the
    /// complete derived span set before native-overlay list publication.
    /// </summary>
    private bool CanPublishNativeSortGList(uint rastPort)
    {
        if (!_core.TryGetSortGListPublicationSpans(
                rastPort,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// AddBob publishes Bob flags, VSprite ownership/flags, and then the
    /// delegated VSprite insertion links.  Admit only after all destinations
    /// are known writable so a provider-owned Bob cannot be half-registered.
    /// </summary>
    private bool CanPublishNativeAddBob(uint bob, uint rastPort)
    {
        if (!_core.TryGetAddBobPublicationSpans(
                bob,
                rastPort,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// RemIBob may restore SAVEBOB rows before unlinking and clearing Bob
    /// ownership.  Preflight the complete teardown span set before the native
    /// overlay claims a guest-only path; provider-backed GELS is rejected by
    /// the vector boundary below.
    /// </summary>
    private bool CanPublishNativeRemIBob(
        uint bob,
        uint rastPort,
        uint viewPort)
    {
        if (!_core.TryGetRemIBobPublicationSpans(
                bob,
                rastPort,
                viewPort,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// AddAnimOb links an AnimOb, initializes active component state, and may
    /// admit several Bobs into the current GELS chain.  Resolve the complete
    /// bounded publication envelope before native-overlay dispatch.
    /// </summary>
    private bool CanPublishNativeAddAnimOb(
        uint animOb,
        uint animKeyAddress,
        uint rastPort)
    {
        if (!_core.TryGetAddAnimObPublicationSpans(
                animOb,
                animKeyAddress,
                rastPort,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Animate may update multiple AnimObs/components and switch Bobs, with a
    /// possible SAVEBOB restore on the old frame.  Admit only after the full
    /// traversal-derived guest span set is writable.
    /// </summary>
    private bool CanPublishNativeAnimate(
        uint animKeyAddress,
        uint rastPort)
    {
        if (!_core.TryGetAnimatePublicationSpans(
                animKeyAddress,
                rastPort,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// GetGBuffers publishes five LONG pointers per discovered Bob/VSprite.
    /// Resolve those guest-owned output spans before the allocator-backed
    /// transaction so a read-only provider envelope cannot cause temporary
    /// storage allocation or a transient partial publication.
    /// </summary>
    private bool CanPublishNativeGetGBuffers(
        uint animOb,
        uint rastPort)
    {
        if (!_core.TryGetGBuffersPublicationSpans(
                animOb,
                rastPort,
                out var addresses))
        {
            return false;
        }

        foreach (var address in addresses)
        {
            if (!CanPublishNativeSpan(address, sizeof(uint)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// InitGMasks republishes the VSprite Bob owner and writes the caller's
    /// border/collision mask spans.  Resolve those guest-owned destinations
    /// before the native overlay can claim the mask transaction; malformed
    /// envelopes remain with the core/native validator.
    /// </summary>
    private bool CanPublishNativeInitGMasks(uint animOb)
    {
        if (!_core.TryGetInitGMasksPublicationSpans(
                animOb,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// FreeGBuffers may restore saved raster rows and clear Bob/VSprite state
    /// before releasing its registry-owned storage.  Admit only when every
    /// destination selected by the live buffer set is writable; this keeps a
    /// read-only provider envelope available to the captured native vector.
    /// </summary>
    private bool CanPublishNativeFreeGBuffers(
        uint animOb,
        uint rastPort,
        uint db)
    {
        if (!_core.TryGetFreeGBuffersPublicationSpans(
                animOb,
                rastPort,
                db,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// DrawGList restores and captures SAVEBOB rows and republishes Bob/VSprite
    /// state before entering the host GELS callback.  Resolve those exact guest
    /// spans before a native-overlay dispatch so a read-only provider/raster
    /// mapping remains available to its owner.
    /// </summary>
    private bool CanPublishNativeDrawGList(
        uint rastPort,
        uint viewPort)
    {
        if (!_core.TryGetDrawGListPublicationSpans(
                rastPort,
                viewPort,
                out var spans))
        {
            return false;
        }

        foreach (var (address, bytes) in spans)
        {
            if (!CanPublishNativeSpan(address, bytes))
                return false;
        }

        return true;
    }

    /// <summary>
    /// ChangeSprite publishes only the SimpleSprite PosCtlData LONG; the new
    /// image stream is an input owned by the caller/display backend.  Refuse
    /// an explicitly read-only association field before the native overlay
    /// can claim the rebinding.
    /// </summary>
    private bool CanPublishNativeChangeSprite(uint sprite)
    {
        if (sprite == 0 || (sprite & 1u) != 0 ||
            !CanAddress(
                sprite,
                GraphicsLayouts.SimpleSpritePosCtlData,
                sizeof(uint)))
        {
            return true;
        }

        return CanPublishNativeSpan(
            sprite + (uint)GraphicsLayouts.SimpleSpritePosCtlData,
            sizeof(uint));
    }

    /// <summary>
    /// Standard ChangeExtSpriteA republishes the old ExtSprite's complete
    /// public state from the validated replacement.  Admit only the fields
    /// that the portable path actually writes, leaving sparse/malformed
    /// envelopes to its own validator.
    /// </summary>
    private bool CanPublishNativeChangeExtSprite(uint sprite)
    {
        if (sprite == 0 || (sprite & 1u) != 0)
            return true;

        var spans = new[]
        {
            (GraphicsLayouts.SimpleSpritePosCtlData, sizeof(uint)),
            (GraphicsLayouts.SimpleSpriteHeight, sizeof(ushort)),
            (GraphicsLayouts.SimpleSpriteX, sizeof(ushort)),
            (GraphicsLayouts.SimpleSpriteY, sizeof(ushort)),
            (GraphicsLayouts.SimpleSpriteNum, sizeof(ushort)),
            (GraphicsLayouts.ExtSpriteWordWidth, sizeof(ushort)),
            (GraphicsLayouts.ExtSpriteFlags, sizeof(ushort))
        };

        foreach (var (offset, byteCount) in spans)
        {
            if (CanAddress(sprite, offset, byteCount) &&
                !CanPublishNativeSpan(
                    sprite + (uint)offset,
                    byteCount))
            {
                return false;
            }
        }

        return true;
    }

    private bool CanPublishNativeCloseMonitor(uint monitorSpec)
    {
        if (monitorSpec == 0)
            return true;

        if (!_core.IsOwnedMonitorSpec(monitorSpec) ||
            !CanAddress(
                monitorSpec,
                GraphicsLayouts.MonitorSpecOpenCount,
                sizeof(ushort)))
        {
            return false;
        }

        return CanPublishNativeSpan(
            monitorSpec + (uint)GraphicsLayouts.MonitorSpecOpenCount,
            sizeof(ushort));
    }

    /// <summary>
    /// OpenMonitor initializes a new resident MonitorSpec and splices it into
    /// the native GfxBase MonitorList.  Admit the native-overlay call only
    /// when the complete native base is writable; otherwise the portable
    /// allocator would create and then tear down a compatibility node before
    /// discovering that the resident/provider list owns the publication.
    /// Compact compatibility images intentionally bypass this check.
    /// </summary>
    private bool CanPublishNativeOpenMonitor()
    {
        var graphicsBase = _nativeOverlayGraphicsBase != 0
            ? _nativeOverlayGraphicsBase
            : _core.GraphicsLibraryBase;
        if (!_core.HasNativeGfxBaseEnvelope(graphicsBase))
        {
            // A compact image has no mapped private MonitorList suffix and
            // may use the compatibility registry.  A malformed list whose
            // complete public fields are mapped is different: it is a
            // resident/provider ownership boundary, so do not allocate a
            // temporary MonitorSpec merely to discover that publication
            // cannot repair the list.
            return !_core.HasMappedNativeGfxBaseMonitorList(graphicsBase);
        }

        if (!_core.HasMappedNativeGfxBaseMonitorList(graphicsBase))
            return true;

        return CanPublishNativeSpan(
            graphicsBase,
            GraphicsLayouts.GfxBaseNativeSize);
    }

    private bool CanPublishNativeCloseFont(uint fontAddress)
    {
        if (fontAddress == 0)
            return true;

        // A native resident font can be present in the rebound TextFonts
        // list without having been opened through this compatibility
        // instance.  Do not consume that native/provider CloseFont request;
        // only an outstanding local OpenFont accessor is ours to decrement.
        if (!_fonts.IsOwnedOpenFont(fontAddress) ||
            !CanAddress(
                fontAddress,
                GraphicsLayouts.TextFontAccessors,
                sizeof(ushort)))
        {
            return false;
        }

        if (!CanPublishNativeSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontAccessors,
                sizeof(ushort)))
        {
            return false;
        }

        // The final accessor can chain through RemFont and StripFont.  When
        // this backend owns that extension, admit the close only if the two
        // additional public fields are writable before the chained free.
        if (!_fonts.WillCloseStripOwnedExtension(fontAddress))
            return true;

        // The chained RemFont also unlinks the rebound guest TextFonts list.
        // Include its exact publication plan before any accessor/list write
        // can make the native fallback observe a partial close.
        if (_fontList is IGraphicsNativeFontListPublicationBackend planner &&
            (!planner.TryGetRemoveFontPublicationSpans(
                fontAddress,
                out var spans) ||
             !CanPublishNativeFontListSpans(spans)))
        {
            return false;
        }

        return CanPublishNativeSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontExtension,
                sizeof(uint)) &&
            CanPublishNativeByteSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontStyle,
                sizeof(byte));
    }

    private bool CanPublishNativeRemFont(uint fontAddress)
    {
        // Only the memory-backed native list planner can describe the exact
        // link/flag writes.  Other lifecycle implementations retain their
        // existing ownership contract and are not widened by this optional
        // admission seam.
        if (_fontList is not IGraphicsNativeFontListPublicationBackend planner)
            return true;

        if (!planner.TryGetRemoveFontPublicationSpans(
                fontAddress,
                out var spans) ||
            !CanPublishNativeFontListSpans(spans))
        {
            return false;
        }

        // RemFont tears down an owned extension after the list transaction.
        // Admit the complete chained publication before either the list
        // writes or the allocator free can begin.
        if (!_fonts.WillRemoveStripOwnedExtension(fontAddress))
            return true;

        return CanPublishNativeSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontExtension,
                sizeof(uint)) &&
            CanPublishNativeByteSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontStyle,
                sizeof(byte));
    }

    private bool CanPublishNativeAddFont(uint fontAddress)
    {
        if (_fontList is not IGraphicsNativeFontListPublicationBackend planner)
            return true;

        return planner.TryGetAddFontPublicationSpans(
                fontAddress,
                out var spans) &&
            CanPublishNativeFontListSpans(spans);
    }

    /// <summary>
    /// ExtendFont publishes the allocated extension pointer and may update
    /// the caller-owned style byte while preserving the rest of the TextFont
    /// prefix.  Admit a native-overlay call only when both exact fields are
    /// writable, so allocation and extension initialization cannot run before
    /// a provider/native font regains ownership of a read-only envelope.
    /// Malformed/odd font pointers remain with the normal core validation.
    /// </summary>
    private bool CanPublishNativeExtendFont(uint fontAddress)
    {
        if (fontAddress == 0 || (fontAddress & 1u) != 0 ||
            !CanAddress(
                fontAddress,
                GraphicsLayouts.TextFontExtension,
                sizeof(uint)) ||
            !CanAddress(
                fontAddress,
                GraphicsLayouts.TextFontStyle,
                sizeof(byte)))
        {
            return true;
        }

        return CanPublishNativeSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontExtension,
                sizeof(uint)) &&
            CanPublishNativeByteSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontStyle,
                sizeof(byte));
    }

    /// <summary>
    /// StripFont clears the public extension pointer/style pair only for an
    /// extension previously allocated by this compatibility backend.  Keep a
    /// foreign/provider extension transparent, but admit the owned detach
    /// only when both public fields are writable so no field write or allocator
    /// free can precede a native-owner fallback.
    /// </summary>
    private bool CanPublishNativeStripFont(uint fontAddress)
    {
        if (fontAddress == 0 ||
            !_fonts.IsOwnedExtension(fontAddress) ||
            (fontAddress & 1u) != 0 ||
            !CanAddress(
                fontAddress,
                GraphicsLayouts.TextFontExtension,
                sizeof(uint)) ||
            !CanAddress(
                fontAddress,
                GraphicsLayouts.TextFontStyle,
                sizeof(byte)))
        {
            return true;
        }

        return CanPublishNativeSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontExtension,
                sizeof(uint)) &&
            CanPublishNativeByteSpan(
                fontAddress + (uint)GraphicsLayouts.TextFontStyle,
                sizeof(byte));
    }

    private bool CanPublishNativeFontListSpans(
        IReadOnlyList<(uint Address, int ByteCount)> spans)
    {
        foreach (var span in spans)
        {
            // tf_Flags is a byte at an odd offset in the TextFont prefix;
            // use the byte-span probe for that field instead of the public
            // WORD/LONG alignment admission used by the general helper.
            var writable = (span.Address & 1u) != 0 || span.ByteCount == 1
                ? CanPublishNativeByteSpan(
                    span.Address,
                    (ulong)span.ByteCount)
                : CanPublishNativeSpan(span.Address, span.ByteCount);
            if (!writable)
                return false;
        }

        return true;
    }

    /// <summary>
    /// OpenFont increments the selected resident TextFont's accessor WORD.
    /// Resolve the candidate without mutating it, then admit the mapped
    /// overlay only when that exact public field is writable. A valid request
    /// with no matching font has no guest publication and remains claimable;
    /// malformed requests are still rejected by the normal lifecycle path.
    /// </summary>
    private bool CanPublishNativeOpenFont(uint textAttr)
    {
        if (!_fonts.TryResolveOpenFont(textAttr, out var font) || font == 0)
            return true;

        if (!CanAddress(
                font,
                GraphicsLayouts.TextFontAccessors,
                sizeof(ushort)))
        {
            return false;
        }

        return CanPublishNativeSpan(
            font + (uint)GraphicsLayouts.TextFontAccessors,
            sizeof(ushort));
    }

    private bool CanPublishNativeRastPortBytes(
        uint rastPort,
        int offset,
        int byteCount)
    {
        if (rastPort == 0 || (rastPort & 1u) != 0 ||
            offset < 0 || byteCount <= 0)
        {
            return true;
        }

        var address = (ulong)rastPort + (uint)offset;
        if (address > uint.MaxValue ||
            (ulong)byteCount > uint.MaxValue ||
            address + (uint)byteCount - 1UL > uint.MaxValue)
        {
            return true;
        }

        return _isWritableMemoryRange?.Invoke(
            (uint)address,
            (uint)byteCount) ?? true;
    }

    private bool CanPublishNativeRastPortSpanSet(
        uint rastPort,
        params (int Offset, int Count)[] spans)
    {
        foreach (var span in spans)
        {
            if (!CanPublishNativeRastPortBytes(
                    rastPort,
                    span.Offset,
                    span.Count))
            {
                return false;
            }
        }

        return true;
    }

    private bool CanPublishNativePenModeState(
        uint rastPort,
        int primaryOffset)
        => CanPublishNativeRastPortSpanSet(
            rastPort,
            (primaryOffset, sizeof(byte)),
            (GraphicsLayouts.RastPortLinePatternCount, sizeof(byte)),
            (GraphicsLayouts.RastPortMinterms, 8),
            (GraphicsLayouts.RastPortFlags, sizeof(ushort)));

    private bool CanPublishNativeMoveState(uint rastPort)
        => CanPublishNativeRastPortSpanSet(
            rastPort,
            (GraphicsLayouts.RastPortCurrentX, sizeof(ushort)),
            (GraphicsLayouts.RastPortCurrentY, sizeof(ushort)),
            (GraphicsLayouts.RastPortLinePatternCount, sizeof(byte)),
            (GraphicsLayouts.RastPortFlags, sizeof(ushort)));

    // Text publishes only the final cursor X, the fresh line-pattern phase,
    // and FRST_DOT in the RastPort state. Keep this write envelope separate
    // from Move/Draw: Text does not consume or rewrite CurrentY, and checking
    // unrelated state would steal a sparse/provider-owned call from the
    // resident vector. In a native overlay, a read-only state byte must be
    // declined before glyph decoding or planar writes begin.
    private bool CanPublishNativeText(uint rastPort)
        => CanPublishNativeRastPortSpanSet(
            rastPort,
            (GraphicsLayouts.RastPortCurrentX, sizeof(ushort)),
            (GraphicsLayouts.RastPortLinePatternCount, sizeof(byte)),
            (GraphicsLayouts.RastPortFlags, sizeof(ushort)));

    private bool CanPublishNativeSetRast(uint rastPort)
    {
        if (!GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            // Malformed or unreadable bitmap state remains available to the
            // portable/provider validation boundary. The core will decline
            // without publishing a partial raster when this probe cannot
            // establish a valid planar envelope.
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
            return true;

        var planeSpan = GraphicsRasterOperations.GetBitmapPlaneTouchedSpan(bitmap);
        if (planeSpan == 0 || planeSpan > uint.MaxValue)
            return true;

        var planesAddress = (ulong)bitmap.Address +
            (uint)GraphicsLayouts.BitMapPlanes;
        if (planesAddress > uint.MaxValue)
            return true;

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((effectiveMask & (1 << plane)) == 0)
                continue;

            var planePointerAddress = planesAddress + (uint)(plane * sizeof(uint));
            if (planePointerAddress > uint.MaxValue ||
                !_core.Memory.TryReadLong(
                    (uint)planePointerAddress,
                    out var planeAddress) ||
                planeAddress == 0 ||
                (ulong)planeAddress + planeSpan - 1UL > uint.MaxValue)
            {
                return true;
            }

            if (!CanPublishNativeByteSpan(planeAddress, planeSpan))
                return false;
        }

        return true;
    }

    private bool CanPublishNativeClear(uint rastPort)
    {
        if (!CanPublishNativeRastPortBytes(
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte)))
        {
            return false;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
            return true;

        return CanPublishNativePenModeState(
                rastPort,
                GraphicsLayouts.RastPortFgPen) &&
            CanPublishNativeSetRast(rastPort);
    }

    private bool CanPublishNativeWritePixel(
        uint rastPort,
        short x,
        short y)
    {
        if (!GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0 ||
            x < 0 || y < 0 ||
            x >= bitmap.Width || y >= bitmap.Rows)
        {
            return true;
        }

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((effectiveMask & (1 << plane)) == 0)
                continue;

            if (!GraphicsRasterOperations.TryGetPlaneByteAddress(
                    _core.Memory,
                    bitmap,
                    plane,
                    x,
                    y,
                    out var byteAddress))
            {
                return true;
            }

            if (!CanPublishNativeByteSpan(byteAddress, 1))
                return false;
        }

        return true;
    }

    private bool CanPublishNativeRasterRegion(
        uint rastPort,
        int xMin,
        int yMin,
        int xMax,
        int yMax)
    {
        if (xMax < xMin || yMax < yMin ||
            !GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
            return true;

        var left = Math.Max(0, xMin);
        var top = Math.Max(0, yMin);
        var right = Math.Min(bitmap.Width - 1, xMax);
        var bottom = Math.Min(bitmap.Rows - 1, yMax);
        if (left > right || top > bottom)
            return true;

        var firstByte = left >> 3;
        var bytesPerRow = (right >> 3) - firstByte + 1;
        if (bytesPerRow <= 0)
            return true;

        var rowStride = GraphicsRasterOperations.GetBitmapPlaneRowStride(bitmap);
        var planesAddress = (ulong)bitmap.Address +
            (uint)GraphicsLayouts.BitMapPlanes;
        if (planesAddress > uint.MaxValue)
            return true;

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((effectiveMask & (1 << plane)) == 0)
                continue;

            var planePointerAddress = planesAddress + (uint)(plane * sizeof(uint));
            if (planePointerAddress > uint.MaxValue ||
                !_core.Memory.TryReadLong(
                    (uint)planePointerAddress,
                    out var planeAddress) ||
                planeAddress == 0 ||
                (planeAddress & 1u) != 0)
            {
                return true;
            }

            for (var y = top; y <= bottom; y++)
            {
                var rowAddress = (ulong)planeAddress +
                    ((ulong)(uint)y * (uint)rowStride) +
                    (uint)firstByte;
                if (rowAddress > uint.MaxValue ||
                    (ulong)(uint)(bytesPerRow - 1) >
                        (ulong)uint.MaxValue - rowAddress)
                {
                    return true;
                }

                if (!CanPublishNativeByteSpan(
                        (uint)rowAddress,
                        (ulong)(uint)bytesPerRow))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool CanPublishNativeLinePixels(
        GraphicsRasterOperations.BitmapInfo bitmap,
        byte effectiveMask,
        int startX,
        int startY,
        int endX,
        int endY)
    {
        var x = startX;
        var y = startY;
        var dx = Math.Abs(endX - x);
        var sx = x < endX ? 1 : -1;
        var dy = -Math.Abs(endY - y);
        var sy = y < endY ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            if (x >= 0 && y >= 0 &&
                x < bitmap.Width && y < bitmap.Rows)
            {
                for (var plane = 0; plane < bitmap.Depth; plane++)
                {
                    if ((effectiveMask & (1 << plane)) == 0)
                        continue;

                    if (!GraphicsRasterOperations.TryGetPlaneByteAddress(
                            _core.Memory,
                            bitmap,
                            plane,
                            x,
                            y,
                            out var byteAddress))
                    {
                        return true;
                    }

                    if (!CanPublishNativeByteSpan(byteAddress, 1))
                        return false;
                }
            }

            if (x == endX && y == endY)
                break;

            var doubleError = error * 2;
            if (doubleError >= dy)
            {
                error += dy;
                x += sx;
            }

            if (doubleError <= dx)
            {
                error += dx;
                y += sy;
            }
        }

        return true;
    }

    private bool CanPublishNativeDraw(
        uint rastPort,
        short targetX,
        short targetY)
    {
        if (!CanPublishNativeRastPortSpanSet(
                rastPort,
                (GraphicsLayouts.RastPortCurrentX, sizeof(ushort)),
                (GraphicsLayouts.RastPortCurrentY, sizeof(ushort)),
                (GraphicsLayouts.RastPortLinePatternCount, sizeof(byte)),
                (GraphicsLayouts.RastPortFlags, sizeof(ushort))))
        {
            return false;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask) ||
            !_core.Memory.TryReadWord(
                rastPort + (uint)GraphicsLayouts.RastPortCurrentX,
                out var currentXWord) ||
            !_core.Memory.TryReadWord(
                rastPort + (uint)GraphicsLayouts.RastPortCurrentY,
                out var currentYWord))
        {
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
            return true;

        return CanPublishNativeLinePixels(
            bitmap,
            effectiveMask,
            (short)currentXWord,
            (short)currentYWord,
            targetX,
            targetY);
    }

    private bool CanPublishNativePolyDraw(
        uint rastPort,
        ushort count,
        uint pointsAddress)
    {
        if (count == 0)
            return true;

        if (!CanPublishNativeRastPortSpanSet(
                rastPort,
                (GraphicsLayouts.RastPortCurrentX, sizeof(ushort)),
                (GraphicsLayouts.RastPortCurrentY, sizeof(ushort)),
                (GraphicsLayouts.RastPortLinePatternCount, sizeof(byte)),
                (GraphicsLayouts.RastPortFlags, sizeof(ushort))))
        {
            return false;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask) ||
            !_core.Memory.TryReadWord(
                rastPort + (uint)GraphicsLayouts.RastPortCurrentX,
                out var currentXWord) ||
            !_core.Memory.TryReadWord(
                rastPort + (uint)GraphicsLayouts.RastPortCurrentY,
                out var currentYWord))
        {
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
            return true;

        var currentX = (short)currentXWord;
        var currentY = (short)currentYWord;
        for (var index = 0; index < count; index++)
        {
            var pointAddress = (ulong)pointsAddress +
                (uint)(index * 4);
            if (pointAddress > uint.MaxValue ||
                !_core.Memory.TryReadWord(
                    (uint)pointAddress,
                    out var pointXWord) ||
                !_core.Memory.TryReadWord(
                    (uint)pointAddress + 2u,
                    out var pointYWord))
            {
                return true;
            }

            var pointX = (short)pointXWord;
            var pointY = (short)pointYWord;
            if (!CanPublishNativeLinePixels(
                    bitmap,
                    effectiveMask,
                    currentX,
                    currentY,
                    pointX,
                    pointY))
            {
                return false;
            }

            currentX = pointX;
            currentY = pointY;
        }

        return true;
    }

    private bool CanPublishNativeScrollRaster(
        uint rastPort,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        bool useBackgroundPen)
    {
        // Reversed bounds are the documented empty non-layered operation;
        // the portable core returns before touching any public state.
        if (xMax < xMin || yMax < yMin ||
            !GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        var left = Math.Max(0, (int)xMin);
        var top = Math.Max(0, (int)yMin);
        var right = Math.Min(bitmap.Width - 1, (int)xMax);
        var bottom = Math.Min(bitmap.Rows - 1, (int)yMax);
        if (left > right || top > bottom)
            return true;

        var logicalWidth = (long)right - xMin + 1L;
        var logicalHeight = (long)bottom - yMin + 1L;
        if (logicalWidth <= 0 || logicalHeight <= 0)
            return true;

        // A zero displacement is a read/probe-only request.  Ordinary
        // ScrollRaster also leaves a rectangle untouched when its delta
        // reaches either logical dimension; BF keeps that full-displacement
        // boundary for its EraseRect backfill path.
        if (deltaX == 0 && deltaY == 0)
            return true;

        var absoluteDeltaX = Math.Abs((int)deltaX);
        var absoluteDeltaY = Math.Abs((int)deltaY);
        var ordinaryFullDisplacement =
            !useBackgroundPen &&
            (absoluteDeltaX >= logicalWidth || absoluteDeltaY >= logicalHeight);
        if (ordinaryFullDisplacement)
            return true;

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
        {
            // With no selected destination planes, both variants only reset
            // the public line-pattern phase for a real, non-empty request.
            return CanPublishNativeRastPortBytes(
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte));
        }

        if (!CanPublishNativeRasterRegion(
                rastPort,
                xMin,
                yMin,
                xMax,
                yMax))
        {
            return false;
        }

        // ScrollRasterBF delegates vacated strips to EraseRect, whose
        // transactional SetAPen/restore sequence publishes FgPen, Minterms,
        // Flags, and the pattern phase in addition to the selected planes.
        return !useBackgroundPen ||
            CanPublishNativePenModeState(
                rastPort,
                GraphicsLayouts.RastPortFgPen);
    }

    private bool CanPublishNativeFlood(
        uint rastPort,
        uint mode,
        short x,
        short y)
    {
        // Invalid modes and out-of-raster seeds are rejected by the core
        // before any destination or TmpRas write. Leave those requests on
        // the existing native/provider validation boundary.
        if (mode > 1 ||
            !GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return true;

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
            return true;

        // Flood snapshots and may publish any connected pixel in the
        // selected planes. A complete plane-span admission is conservative
        // but keeps a late connected-region discovery from partially writing
        // a mapped native overlay before it can tail-chain.
        if (!CanPublishNativeSetRast(rastPort))
            return false;

        var requiredBytes = (ulong)(uint)bitmap.PlaneBytesPerRow *
            (uint)bitmap.Rows;
        if (requiredBytes == 0 || requiredBytes > uint.MaxValue)
            return true;

        // A NULL TmpRas uses the portable managed visited set or allocator
        // boundary and has no guest scratch output to admit here. An
        // attached TmpRas is cleared and marked during the flood walk, so
        // reject a read-only scratch span before claiming the operation.
        if (!GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                rastPort,
                GraphicsLayouts.RastPortTmpRas,
                out var tmpRas) ||
            tmpRas == 0)
        {
            return true;
        }

        if ((tmpRas & 1u) != 0 ||
            !GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                tmpRas,
                GraphicsLayouts.TmpRasRasPtr,
                out var rasBuffer) ||
            !GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                tmpRas,
                GraphicsLayouts.TmpRasByteCount,
                out var byteCount) ||
            rasBuffer == 0 ||
            byteCount < requiredBytes)
        {
            return true;
        }

        return CanPublishNativeByteSpan(rasBuffer, requiredBytes);
    }

    private bool CanPublishNativeBltPattern(
        uint rastPort,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
    {
        if (xMax < xMin || yMax < yMin ||
            !GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        if (GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask) == 0)
            return true;

        // BltPattern/RectFill publish only selected planar destination bytes;
        // source masks and area patterns are read-only inputs. Keep the
        // clipped region admission conservative for draw-mode source no-ops,
        // because the native vector may still enter its patterned writer.
        return CanPublishNativeRasterRegion(
            rastPort,
            xMin,
            yMin,
            xMax,
            yMax);
    }

    private bool CanPublishNativeBltTemplate(
        uint rastPort,
        short destinationX,
        short destinationY,
        short width,
        short height)
    {
        if (width <= 0 || height <= 0 ||
            !GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        if (GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask) == 0)
            return true;

        var left = Math.Max(0, -(int)destinationX);
        var top = Math.Max(0, -(int)destinationY);
        var right = Math.Min((int)width, bitmap.Width - (int)destinationX);
        var bottom = Math.Min((int)height, bitmap.Rows - (int)destinationY);
        if (left >= right || top >= bottom)
            return true;

        return CanPublishNativeRasterRegion(
            rastPort,
            destinationX + left,
            destinationY + top,
            destinationX + right - 1,
            destinationY + bottom - 1);
    }

    private bool CanPublishNativePixelWriterRegion(
        uint rastPort,
        long xMin,
        long yMin,
        long xMax,
        long yMax)
    {
        if (xMax < xMin || yMax < yMin ||
            xMin < int.MinValue || xMin > int.MaxValue ||
            yMin < int.MinValue || yMin > int.MaxValue ||
            xMax < int.MinValue || xMax > int.MaxValue ||
            yMax < int.MinValue || yMax > int.MaxValue ||
            !GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        if (GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask) == 0)
            return true;

        // The pixel-array and chunky writers consume caller-owned source
        // bytes but publish only the clipped selected-plane destination.
        return CanPublishNativeRasterRegion(
            rastPort,
            (int)xMin,
            (int)yMin,
            (int)xMax,
            (int)yMax);
    }

    private bool CanPublishNativeBitmapDestinationRegion(
        GraphicsRasterOperations.BitmapInfo bitmap,
        int xMin,
        int yMin,
        int xMax,
        int yMax,
        byte planeMask)
    {
        if (xMax < xMin || yMax < yMin)
        {
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            planeMask);
        if (effectiveMask == 0)
            return true;

        var left = Math.Max(0, xMin);
        var top = Math.Max(0, yMin);
        var right = Math.Min(bitmap.Width - 1, xMax);
        var bottom = Math.Min(bitmap.Rows - 1, yMax);
        if (left > right || top > bottom)
            return true;

        var firstByte = left >> 3;
        var bytesPerRow = (right >> 3) - firstByte + 1;
        if (bytesPerRow <= 0)
            return true;

        var rowStride = GraphicsRasterOperations.GetBitmapPlaneRowStride(bitmap);
        var planesAddress = (ulong)bitmap.Address +
            (uint)GraphicsLayouts.BitMapPlanes;
        if (planesAddress > uint.MaxValue)
            return true;

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((effectiveMask & (1 << plane)) == 0)
                continue;

            var planePointerAddress = planesAddress +
                (uint)(plane * sizeof(uint));
            if (planePointerAddress > uint.MaxValue ||
                !_core.Memory.TryReadLong(
                    (uint)planePointerAddress,
                out var planeAddress) ||
                planeAddress == 0 ||
                (planeAddress & 1u) != 0)
            {
                return true;
            }

            for (var y = top; y <= bottom; y++)
            {
                var rowAddress = (ulong)planeAddress +
                    ((ulong)(uint)y * (uint)rowStride) +
                    (uint)firstByte;
                if (rowAddress > uint.MaxValue ||
                    (ulong)(uint)(bytesPerRow - 1) >
                        (ulong)uint.MaxValue - rowAddress)
                {
                    return true;
                }

                if (!CanPublishNativeByteSpan(
                        (uint)rowAddress,
                        (ulong)(uint)bytesPerRow))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryReadNativeBitmapHeader(
        uint address,
        out GraphicsRasterOperations.BitmapInfo bitmap)
    {
        bitmap = default;
        // BltBitMap-style vectors receive a BitMap pointer rather than a
        // RastPort. Keep this direct header read separate from TryReadBitmap,
        // whose ABI intentionally starts at rp_BitMap.
        if (address == 0 || (address & 1u) != 0 ||
            address > uint.MaxValue -
                (uint)(GraphicsLayouts.BitMapDepth + sizeof(byte) - 1) ||
            !_core.Memory.TryReadWord(
                address + (uint)GraphicsLayouts.BitMapBytesPerRow,
                out var bytesPerRow) ||
            !_core.Memory.TryReadWord(
                address + (uint)GraphicsLayouts.BitMapRows,
                out var rows) ||
            !_core.Memory.TryReadByte(
                address + (uint)GraphicsLayouts.BitMapFlags,
                out var flags) ||
            !_core.Memory.TryReadByte(
                address + (uint)GraphicsLayouts.BitMapDepth,
                out var depth) ||
            bytesPerRow == 0 || (bytesPerRow & 1) != 0 ||
            rows == 0 || depth == 0 || depth > 8 ||
            !GraphicsRasterOperations.TryGetPlaneBytesPerRow(
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

    private bool CanPublishNativeAreaCollector(uint rastPort)
    {
        if (!GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                rastPort,
                GraphicsLayouts.RastPortAreaInfo,
                out var areaInfo) ||
            areaInfo == 0 ||
            (areaInfo & 1u) != 0)
        {
            return true;
        }

        if (!_core.Memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable,
                out var vectorTable) ||
            !_core.Memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
                out var vectorPointer) ||
            !_core.Memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFlagTable,
                out var flagTable) ||
            !_core.Memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer,
                out var flagPointer) ||
            !_core.Memory.TryReadWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                out var count) ||
            !_core.Memory.TryReadWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoMaxCount,
                out var maxCount))
        {
            return true;
        }

        // A full collector cannot accept another record when its capacity is
        // exhausted. Let the core/native owner decide that failure without
        // turning an output-admission probe into a new ownership rule.
        if (maxCount == 0 || count >= maxCount)
            return true;

        var vectorBytes = (ulong)maxCount * 4UL;
        var flagBytes = maxCount;
        if (vectorTable == 0 || flagTable == 0 ||
            vectorBytes == 0 || vectorBytes > uint.MaxValue ||
            (ulong)vectorTable + vectorBytes - 1UL > uint.MaxValue ||
            (ulong)flagTable + flagBytes - 1UL > uint.MaxValue)
        {
            return true;
        }

        var expectedVectorPointer = (ulong)vectorTable +
            (ulong)count * 4UL;
        var expectedFlagPointer = (ulong)flagTable + count;
        if (expectedVectorPointer > uint.MaxValue ||
            expectedFlagPointer > uint.MaxValue ||
            vectorPointer != (uint)expectedVectorPointer ||
            flagPointer != (uint)expectedFlagPointer)
        {
            return true;
        }

        if (!CanPublishNativeByteSpan(
                areaInfo,
                (ulong)GraphicsLayouts.AreaInfoSize) ||
            !CanPublishNativeByteSpan(vectorTable, vectorBytes) ||
            !CanPublishNativeByteSpan(flagTable, flagBytes))
        {
            return false;
        }

        return true;
    }

    private bool CanPublishNativeAreaEnd(uint rastPort)
    {
        if (!GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                rastPort,
                GraphicsLayouts.RastPortAreaInfo,
                out var areaInfo) ||
            areaInfo == 0 ||
            (areaInfo & 1u) != 0 ||
            !_core.Memory.TryReadWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                out var count))
        {
            return true;
        }

        // An empty collector is an observable no-op and must not be turned
        // into a writable-descriptor requirement merely because the caller
        // supplied a sparse or read-only AreaInfo envelope.
        if (count == 0)
            return true;

        // AreaEnd always resets the collector state after a successful
        // non-empty fill, but its vector/flag tables are read-only inputs at
        // this boundary. Only the AreaInfo descriptor itself is output here.
        if (!CanPublishNativeByteSpan(
                areaInfo,
                (ulong)GraphicsLayouts.AreaInfoSize))
        {
            return false;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return true;
        }

        var effectiveMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (effectiveMask == 0)
            return true;

        if (!CanPublishNativeSetRast(rastPort))
            return false;

        if (_core.Memory.TryReadWord(
                rastPort + (uint)GraphicsLayouts.RastPortFlags,
                out var flags) &&
            (flags & GraphicsLayouts.RastPortAreaOutline) != 0 &&
            !CanPublishNativeRastPortSpanSet(
                rastPort,
                (GraphicsLayouts.RastPortFgPen, sizeof(byte)),
                (GraphicsLayouts.RastPortLinePatternCount, sizeof(byte)),
                (GraphicsLayouts.RastPortCurrentX, sizeof(ushort)),
                (GraphicsLayouts.RastPortCurrentY, sizeof(ushort)),
                (GraphicsLayouts.RastPortFlags, sizeof(ushort))))
        {
            return false;
        }

        var requiredBytes = (ulong)(uint)bitmap.PlaneBytesPerRow *
            (uint)bitmap.Rows;
        if (requiredBytes == 0 || requiredBytes > uint.MaxValue ||
            !GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                rastPort,
                GraphicsLayouts.RastPortTmpRas,
                out var tmpRas) ||
            tmpRas == 0)
        {
            return true;
        }

        // AreaEnd sizes a caller-owned TmpRas from the largest decoded shape,
        // not necessarily the complete bitmap. If the descriptor is smaller
        // than the conservative full-raster envelope, leave ownership to the
        // portable/native validator rather than over-declining a valid small
        // shape; a full-sized attached scratch span must be writable because
        // it is cleared and marked during the fill.
        if ((tmpRas & 1u) != 0 ||
            !GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                tmpRas,
                GraphicsLayouts.TmpRasRasPtr,
                out var rasBuffer) ||
            !GraphicsRasterOperations.TryReadRastPortLong(
                _core.Memory,
                tmpRas,
                GraphicsLayouts.TmpRasByteCount,
                out var byteCount) ||
            rasBuffer == 0 ||
            byteCount < requiredBytes)
        {
            return true;
        }

        return CanPublishNativeByteSpan(rasBuffer, requiredBytes);
    }

    private bool CanPublishNativeRastPortTagOutputs(
        uint rastPort,
        uint tags)
    {
        if (!GraphicsRastPortAttributeOperations.TryGetNativeSetWriteSpans(
                _core.Memory,
                tags,
                out var spans))
        {
            return false;
        }

        foreach (var span in spans)
        {
            if (!CanPublishNativeRastPortBytes(
                    rastPort,
                    span.Offset,
                    span.Count))
            {
                return false;
            }
        }

        return true;
    }

    private bool CanPublishNativeGetOutputSpans(uint tags)
    {
        if (!GraphicsRastPortAttributeOperations.TryGetNativeGetOutputSpans(
                _core.Memory,
                tags,
                out var spans))
        {
            return false;
        }

        foreach (var span in spans)
        {
            if (!CanPublishNativeByteSpan(
                    span.Address,
                    (ulong)span.Count))
            {
                return false;
            }
        }

        return true;
    }

    internal bool TryPublishNativeDisplayDatabase()
        => _core.TryPublishNativeDisplayDatabase();

    internal bool ReleaseNativeDisplayDatabase()
        => _core.ReleaseNativeDisplayDatabase();

    /// <summary>
    /// Reports whether a declined <c>LoadView</c> call belongs to the RTG
    /// provider boundary.  The portable planar validator deliberately
    /// declines both malformed standard Views and CyberGraphX-owned Views;
    /// GraphicsServices needs this narrow discriminator so only the latter
    /// can reach the provider callback.
    /// </summary>
    internal bool IsRtgViewForProvider(uint view)
        => IsRtgView(view) ||
           (view == 0 && IsRtgActiveView());

    /// <summary>
    /// Reports whether a declined viewport palette call belongs to the RTG
    /// provider boundary.  A malformed standard viewport must not be routed
    /// through the legacy host palette callback.
    /// </summary>
    internal bool IsRtgViewPortForProvider(uint viewPort)
        => IsRtgViewPort(viewPort);

    /// <summary>
    /// Reports whether a declined <c>ChangeVPBitMap</c> call targets an RTG
    /// surface either through the replacement bitmap or the viewport's
    /// currently attached bitmap.
    /// </summary>
    internal bool IsRtgChangeViewPortForProvider(uint viewPort, uint bitMap)
        => (_isRtgBitMap?.Invoke(bitMap) ?? false) || IsRtgViewPort(viewPort);

    /// <summary>
    /// Identifies a RastPort whose clipping/damage semantics belong to a
    /// layer or RTG provider rather than the non-layered portable core.
    /// </summary>
    internal bool IsProviderRastPort(uint rastPort)
    {
        // Keep provider fallback on the same native object boundary as the
        // layered/standard classifier below.  A byte-addressable odd base
        // must not be read merely to decide that a provider owns it.
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (IsRtgRastPort(rastPort))
            return true;

        if (!CanAddress(
                rastPort,
                GraphicsLayouts.RastPortLayer,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortLayer,
                out var layer))
        {
            return false;
        }

        return layer != 0;
    }

    /// <summary>
    /// Identifies an explicitly RTG-owned RastPort for the narrow provider
    /// callbacks in <see cref="GraphicsServices"/>. Layer ownership by
    /// itself must not be forwarded to those callbacks when a validated
    /// Layers raster provider declines an operation.
    /// </summary>
    internal bool IsRtgRastPortForProvider(uint rastPort)
        => IsRtgRastPort(rastPort);

    /// <summary>
    /// Identifies a RastPort whose layer link is owned by the explicit
    /// Layers gateway.  Keep this separate from <see cref="IsProviderRastPort"/>
    /// so a direct graphics helper can distinguish a declined layered call
    /// from an RTG/CyberGraphX-owned RastPort before invoking its legacy
    /// callback.
    /// </summary>
    internal bool IsLayeredRastPortForProvider(uint rastPort)
        => IsLayeredRastPort(rastPort);

    internal bool IsProviderBltBitMapRastPort(uint bitMap, uint rastPort)
        => (_isRtgBitMap?.Invoke(bitMap) ?? false) || IsProviderRastPort(rastPort);

    internal bool IsProviderBitMap(uint bitMap)
        => _isRtgBitMap?.Invoke(bitMap) == true;

    internal bool TryInvoke(
        M68kCpuState state,
        int displacement,
        bool nativeOverlay = false)
    {
        switch (displacement)
        {
            case (int)GraphicsLvo.InitRastPort:
                // InitRastPort is specified in terms of SetFont(GfxBase-
                // >DefaultFont), not a fontless zeroed structure.  Prefer
                // the rebound guest font-list owner when one is installed;
                // the compatibility callback remains the host-shim owner
                // for sessions without a mapped native GfxBase.
                // A native overlay must establish the complete caller-owned
                // RastPort output envelope before resolving that default
                // font. Otherwise a provider-owned read-only RastPort could
                // still consume the compatibility font callback before the
                // portable clear discovers that no guest byte is writable.
                if (nativeOverlay &&
                    !CanPublishNativeRastPort(state.A[1], GraphicsLayouts.RastPortSize))
                    return false;

                // A native overlay without that rebound list must not use
                // the host compatibility callback: it would publish the
                // host font pointer and cached metrics into a native
                // RastPort and steal the resident vector's ownership.
                var compatibilityDefaultResolved = false;
                var compatibilityDefault = 0u;
                if (nativeOverlay &&
                    _fontList is null &&
                    _ensureCompatibilityFont is not null)
                {
                    // Probe the host callback once. A zero result means the
                    // compatibility session has no font owner and retains
                    // the existing zero-font overlay path; a nonzero result
                    // is host state and must not be written into a native
                    // RastPort.
                    compatibilityDefault = _ensureCompatibilityFont();
                    compatibilityDefaultResolved = true;
                    if (compatibilityDefault != 0)
                        return false;
                }

                if (_fontList is not null)
                {
                    if (!_fontList.TryGetDefaultFont(out var guestDefaultFont) ||
                        guestDefaultFont == 0 ||
                        !_fonts.TryGetMetrics(guestDefaultFont, out var guestDefaultMetrics))
                    {
                        return false;
                    }

                    if (!_core.InitializeRastPort(
                            state.A[1],
                            guestDefaultFont,
                            guestDefaultMetrics))
                    {
                        return false;
                    }

                    state.D[0] = 0;
                    return true;
                }

                if (_ensureCompatibilityFont is not null)
                {
                    var defaultFont = compatibilityDefaultResolved
                        ? compatibilityDefault
                        : _ensureCompatibilityFont();
                    if (defaultFont != 0)
                    {
                        // A present compatibility default is an ownership
                        // decision, not an optional hint. Keep InitRastPort
                        // aligned with SetFont: if the selected guest font
                        // cannot be decoded, do not claim a fontless success
                        // and leave the native/provider vector available.
                        if (!_fonts.TryGetMetrics(defaultFont, out var defaultMetrics))
                            return false;

                        // If the provider-supplied metric path was selected,
                        // a failed guest write must remain unclaimed.  Do not
                        // turn a malformed RastPort into a successful
                        // no-font initialization by silently retrying.
                        if (!_core.InitializeRastPort(
                                state.A[1],
                                defaultFont,
                                defaultMetrics))
                        {
                            return false;
                        }

                        state.D[0] = 0;
                        return true;
                    }
                }

                if (!_core.InitializeRastPort(state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.InitView:
                // InitView is a void vector, but the portable adapter must
                // only claim it after the complete guest View envelope has
                // been validated and cleared.  A declined call leaves D0,
                // cycles, and guest memory untouched for native/provider
                // ownership.
                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[1], GraphicsLayouts.ViewSize))
                    return false;

                if (!_core.InitializeView(state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.InitVPort:
                // InitVPort is a void vector.  The host GraphicsServices
                // wrapper adds its projection callback only after this same
                // guarded portable operation succeeds; the register adapter
                // claims the guest structure initialization itself.
                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[0], GraphicsLayouts.ViewPortSize))
                    return false;

                if (!_core.InitializeViewPort(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.InitBitMap:
                // Keep the void ABI, but only claim a bitmap whose complete
                // guest envelope was preflighted and initialized.  A
                // malformed/truncated object remains available to native
                // Kickstart or another provider, with D0/cycles untouched.
                // InitBitMap only publishes the eight-byte geometry/header
                // prefix; the caller-owned Planes[] links must not be read or
                // changed by this constructor.  In a native overlay, reject
                // a mapped read-only prefix before the portable transaction
                // can probe or partially publish it.
                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[0], GraphicsLayouts.BitMapPlanes))
                {
                    return false;
                }

                if (!_core.InitializeBitMap(
                        state.A[0],
                        (byte)state.D[0],
                        (ushort)state.D[1],
                        (ushort)state.D[2]))
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.InitArea:
                // InitArea publishes the complete AreaInfo collector
                // descriptor but does not consume the caller-owned vector or
                // flag storage.  Keep a mapped read-only descriptor in the
                // native/provider path before the portable snapshot/write
                // transaction begins.
                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[0], GraphicsLayouts.AreaInfoSize))
                {
                    return false;
                }

                if (!_core.InitializeArea(
                        state.A[0],
                        state.A[1],
                        unchecked((short)state.D[0])))
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.InitTmpRas:
                // InitTmpRas publishes only its public descriptor pair; the
                // caller-owned backing span is intentionally not read here.
                // A native overlay must nevertheless see a writable
                // descriptor before the portable transaction can claim it.
                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[0], GraphicsLayouts.TmpRasSize))
                {
                    return false;
                }

                if (!_core.InitializeTmpRas(state.A[0], state.A[1], state.D[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GetColorMap:
            {
                var colorMap = _core.GetColorMap(state.D[0]);
                if (colorMap == 0)
                    return false;

                state.D[0] = colorMap;
                return true;
            }
            case (int)GraphicsLvo.FreeColorMap:
                if (nativeOverlay && state.A[0] != 0 && !_core.IsOwnedColorMap(state.A[0]))
                    return false;

                if (!_core.FreeColorMap(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AttachPalExtra:
                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !_core.IsNativeOverlayPaletteStateSafe(state.A[0])))
                    return false;

                if (IsRtgViewPort(state.A[1]))
                    return false;

                var attachResult = _core.AttachPalExtra(state.A[0], state.A[1]);
                if (nativeOverlay && attachResult != 0)
                    return false;

                state.D[0] = unchecked((uint)attachResult);
                return true;
            case (int)GraphicsLvo.GetRGB4:
                if (nativeOverlay && !_core.IsOwnedColorMap(state.A[0]))
                    return false;

                // GetRGB4 returns the 12-bit palette value in D0.  Keep the
                // value produced by the ColorMap operation intact at the
                // register boundary; unrelated RastPort getters must not
                // participate in this vector's result.
                var rgb4 = _core.GetRGB4(state.A[0], unchecked((int)state.D[0]));
                if (rgb4 == uint.MaxValue)
                    return false;

                state.D[0] = rgb4;
                return true;
            case (int)GraphicsLvo.SetRGB4CM:
                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !CanPublishNativeColorMapEntry(
                         state.A[0],
                         unchecked((uint)(short)state.D[0]))))
                    return false;

                if (!_core.SetRGB4CM(
                        state.A[0],
                        unchecked((uint)(short)state.D[0]),
                        (byte)state.D[1],
                        (byte)state.D[2],
                        (byte)state.D[3]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetRGB32CM:
                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !CanPublishNativeColorMapEntry(state.A[0], state.D[0])))
                    return false;

                if (!_core.SetRGB32CM(
                        state.A[0],
                        state.D[0],
                        state.D[1],
                        state.D[2],
                        state.D[3]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ObtainPen:
                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !_core.IsNativeOverlayPaletteStateSafe(state.A[0])))
                    return false;

                state.D[0] = unchecked((uint)_core.ObtainPen(
                    state.A[0],
                    state.D[0],
                    state.D[1],
                    state.D[2],
                    state.D[3],
                    state.D[4]));
                return true;
            case (int)GraphicsLvo.ObtainBestPenA:
                if (!GraphicsColorOperations.IsBestPenTagListSafe(
                        _core.Memory,
                        state.A[1]))
                    return false;

                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !_core.IsNativeOverlayPaletteStateSafe(state.A[0])))
                    return false;

                if (!_core.TryObtainBestPenA(
                        state.A[0],
                        state.D[1],
                        state.D[2],
                        state.D[3],
                        state.A[1],
                        out var bestPen))
                    return false;

                state.D[0] = unchecked((uint)bestPen);
                return true;
            case (int)GraphicsLvo.ReleasePen:
                // ReleasePen documents the all-ones pen as an idempotent
                // no-op.  It does not inspect or mutate the ColorMap, so the
                // native overlay may claim this form even when A0 is NULL or
                // belongs to another palette provider.  Real releases still
                // pass through the instance-ownership gate below.
                if (state.D[0] == uint.MaxValue)
                {
                    state.D[0] = 0;
                    return true;
                }

                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !_core.IsNativeOverlayPaletteStateSafe(state.A[0])))
                    return false;

                _core.ReleasePen(state.A[0], state.D[0]);
                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.FindColor:
                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !_core.IsNativeOverlayPaletteStateSafe(state.A[0])))
                    return false;

                state.D[0] = unchecked((uint)_core.FindColor(
                    state.A[0],
                    state.D[1],
                    state.D[2],
                    state.D[3],
                    unchecked((int)state.D[4])));
                return true;
            case (int)GraphicsLvo.VideoControl:
                if (nativeOverlay &&
                    (!_core.IsOwnedColorMap(state.A[0]) ||
                     !CanPublishNativeSpan(
                         state.A[0],
                         GraphicsLayouts.ColorMapSize) ||
                     !_core.IsNativeOverlayVideoControlSafe(
                         state.A[1],
                         CanPublishNativeSpan)))
                {
                    return false;
                }

                state.D[0] = _core.VideoControl(state.A[0], state.A[1]) ? 0u : 1u;
                return true;
            case (int)GraphicsLvo.GetRGB32:
                // GetRGB32 publishes three guest LONGs per requested color.
                // Zero-count queries intentionally remain no-read successes;
                // malformed/odd/wrapping tables stay with the portable
                // validation boundary.  For a valid native-overlay result,
                // require the complete table to be bus-visible writable before
                // decoding ColorMap entries or entering the rollback writer.
                if (nativeOverlay && state.D[1] != 0)
                {
                    var rgb32OutputBytes = (ulong)state.D[1] * 12UL;
                    if (!CanPublishNativeSpan(state.A[1], rgb32OutputBytes))
                        return false;
                }

                if (nativeOverlay && !_core.IsOwnedColorMap(state.A[0]))
                    return false;

                if (!_core.GetRGB32(state.A[0], state.D[0], state.D[1], state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.LoadRGB4:
                if (IsRtgViewPort(state.A[0]))
                    return false;

                if (nativeOverlay &&
                    (!_core.IsOwnedViewPortColorMap(state.A[0]) ||
                     !CanPublishNativeViewPortColorMapTable(state.A[0])))
                    return false;

                if (!_core.LoadRGB4(
                        state.A[0],
                        state.A[1],
                        unchecked((short)state.D[0]),
                        state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.LoadRGB32:
                if (IsRtgViewPort(state.A[0]))
                    return false;

                if (nativeOverlay &&
                    (!_core.IsOwnedViewPortColorMap(state.A[0]) ||
                     !CanPublishNativeViewPortColorMapTable(state.A[0])))
                    return false;

                if (!_core.LoadRGB32(state.A[0], state.A[1], state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetRGB4:
                if (IsRtgViewPort(state.A[0]))
                    return false;

                if (nativeOverlay &&
                    (!_core.IsOwnedViewPortColorMap(state.A[0]) ||
                     !CanPublishNativeViewPortColorMapEntry(
                         state.A[0],
                         unchecked((uint)(short)state.D[0]))))
                    return false;

                if (!_core.SetRGB4(
                        state.A[0],
                        unchecked((short)state.D[0]),
                        (byte)state.D[1],
                        (byte)state.D[2],
                        (byte)state.D[3],
                        state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetRGB32:
                if (IsRtgViewPort(state.A[0]))
                    return false;

                if (nativeOverlay &&
                    (!_core.IsOwnedViewPortColorMap(state.A[0]) ||
                     !CanPublishNativeViewPortColorMapEntry(
                         state.A[0],
                         state.D[0])))
                    return false;

                if (!_core.SetRGB32(
                        state.A[0],
                        state.D[0],
                        state.D[1],
                        state.D[2],
                        state.D[3],
                        state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.BltBitMap:
            {
                // CyberGraphX owns any registered linear-surface endpoint.
                if ((_isRtgBitMap?.Invoke(state.A[0]) ?? false) ||
                    (_isRtgBitMap?.Invoke(state.A[1]) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    (!TryReadNativeBitmapHeader(
                         state.A[1],
                         out var destinationBitmap) ||
                     !CanPublishNativeBitmapDestinationRegion(
                         destinationBitmap,
                         unchecked((short)state.D[2]),
                         unchecked((short)state.D[3]),
                         unchecked((short)state.D[2]) +
                             unchecked((short)state.D[4]) - 1,
                         unchecked((short)state.D[3]) +
                             unchecked((short)state.D[5]) - 1,
                         unchecked((byte)state.D[7]))))
                {
                    return false;
                }

                // D6/D7 carry the documented UBYTE minterm/mask values in
                // their low register bytes.  Normalize the 68k register
                // frame before entering the portable/provider seam.
                var result = _core.BltBitMap(
                    state.A[0],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    state.A[1],
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3]),
                    unchecked((short)state.D[4]),
                    unchecked((short)state.D[5]),
                    unchecked((byte)state.D[6]),
                    unchecked((byte)state.D[7]),
                    state.A[2]);
                // BltBitMap's public result is the participating plane count,
                // not a portable error channel.  A guarded malformed endpoint
                // therefore remains unclaimed so native Kickstart or a
                // surface provider can apply its own ownership policy.
                if (result == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)result);
                return true;
            }
            case (int)GraphicsLvo.ClipBlit:
            {
                if (!(_isRtgRastPort?.Invoke(state.A[0]) ?? false) &&
                    !(_isRtgRastPort?.Invoke(state.A[1]) ?? false) &&
                    (IsLayeredRastPort(state.A[0]) || IsLayeredRastPort(state.A[1])))
                {
                    if (_layerRaster?.TryClipBlit(
                            state.A[0],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            state.A[1],
                            unchecked((short)state.D[2]),
                            unchecked((short)state.D[3]),
                            unchecked((short)state.D[4]),
                            unchecked((short)state.D[5]),
                            unchecked((byte)state.D[6])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                // Preserve CyberGraphX and layer-aware clipping for RTG ports.
                if (!IsNonLayeredRastPort(state.A[0]) ||
                    !IsNonLayeredRastPort(state.A[1]) ||
                    (_isRtgRastPort?.Invoke(state.A[0]) ?? false) ||
                    (_isRtgRastPort?.Invoke(state.A[1]) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !CanPublishNativeRasterRegion(
                        state.A[1],
                        unchecked((short)state.D[2]),
                        unchecked((short)state.D[3]),
                        unchecked((short)state.D[2]) +
                            unchecked((short)state.D[4]) - 1,
                        unchecked((short)state.D[3]) +
                            unchecked((short)state.D[5]) - 1))
                {
                    return false;
                }

                var result = _core.ClipBlit(
                    state.A[0],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    state.A[1],
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3]),
                    unchecked((short)state.D[4]),
                    unchecked((short)state.D[5]),
                    unchecked((byte)state.D[6]));
                // ClipBlit is a documented void vector.  A portable
                // validation failure is an ownership decline, not a
                // synthetic -1 result; leave the classic register state for
                // layers.library or the native vector.
                if (result == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.BltClear:
            {
                // BltClear is void.  Keep malformed/unaligned memory
                // available to the native boundary instead of claiming the
                // call with the portable status sentinel.
                if (nativeOverlay &&
                    !CanPublishNativeBltClear(
                        state.A[1],
                        state.D[0],
                        state.D[1]))
                {
                    return false;
                }

                if (_core.BltClear(state.A[1], state.D[0], state.D[1]) ==
                    GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.BltBitMapRastPort:
            {
                if (!(_isRtgBitMap?.Invoke(state.A[0]) ?? false) &&
                    IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster is not null &&
                        _layerRaster.TryBltBitMapRastPort(
                            state.A[0],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            state.A[1],
                            unchecked((short)state.D[2]),
                            unchecked((short)state.D[3]),
                            unchecked((short)state.D[4]),
                            unchecked((short)state.D[5]),
                            unchecked((byte)state.D[6]),
                            out var providerResult))
                    {
                        state.D[0] = unchecked((uint)providerResult);
                        return true;
                    }

                    return false;
                }

                // RTG bitmap/surface ownership remains with the CyberGraphX
                // patch layer. Return false so GraphicsServices chains to the
                // legacy callback rather than treating linear VRAM as planar.
                if (!IsNonLayeredRastPort(state.A[1]) ||
                    (_isRtgBitMap?.Invoke(state.A[0]) ?? false) ||
                    (_isRtgRastPort?.Invoke(state.A[1]) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !CanPublishNativeRasterRegion(
                        state.A[1],
                        unchecked((short)state.D[2]),
                        unchecked((short)state.D[3]),
                        unchecked((short)state.D[2]) +
                            unchecked((short)state.D[4]) - 1,
                        unchecked((short)state.D[3]) +
                            unchecked((short)state.D[5]) - 1))
                {
                    return false;
                }

                var result = _core.BltBitMapRastPort(
                    state.A[0],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    state.A[1],
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3]),
                    unchecked((short)state.D[4]),
                    unchecked((short)state.D[5]),
                    unchecked((byte)state.D[6]));
                // The public BOOL is TRUE on success.  The portable core
                // uses its internal 0/-1 status convention, so translate it
                // only after a successful operation; malformed standard
                // endpoints remain unclaimed.
                if (result == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 1;
                return true;
            }
            case (int)GraphicsLvo.BltMaskBitMapRastPort:
            {
                if (!(_isRtgBitMap?.Invoke(state.A[0]) ?? false) &&
                    IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryBltMaskBitMapRastPort(
                            state.A[0],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            state.A[1],
                            unchecked((short)state.D[2]),
                            unchecked((short)state.D[3]),
                            unchecked((short)state.D[4]),
                            unchecked((short)state.D[5]),
                            unchecked((byte)state.D[6]),
                            state.A[2]) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                // CyberGraphX owns any RTG endpoint; the separate patch layer
                // handles its surface and layer-aware clipping semantics.
                if (!IsNonLayeredRastPort(state.A[1]) ||
                    (_isRtgBitMap?.Invoke(state.A[0]) ?? false) ||
                    (_isRtgRastPort?.Invoke(state.A[1]) ?? false))
                {
                    return false;
                }

                var maskDestinationReadable = GraphicsRasterOperations.TryReadBitmap(
                    _core.Memory,
                    state.A[1],
                    out var maskDestinationBitmap);
                var maskSourceReadable = TryReadNativeBitmapHeader(
                    state.A[0],
                    out var maskSourceBitMap);
                var maskDestinationWritable = maskDestinationReadable &&
                    maskSourceReadable &&
                    CanPublishNativeBitmapDestinationRegion(
                        maskDestinationBitmap,
                        unchecked((short)state.D[2]),
                        unchecked((short)state.D[3]),
                        unchecked((short)state.D[2]) +
                            unchecked((short)state.D[4]) - 1,
                        unchecked((short)state.D[3]) +
                            unchecked((short)state.D[5]) - 1,
                        (byte)((1 << Math.Min(
                            maskSourceBitMap.Depth,
                            8)) - 1));
                if (nativeOverlay && !maskDestinationWritable)
                {
                    return false;
                }

                var result = _core.BltMaskBitMapRastPort(
                    state.A[0],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    state.A[1],
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3]),
                    unchecked((short)state.D[4]),
                    unchecked((short)state.D[5]),
                    unchecked((byte)state.D[6]),
                    state.A[2]);
                // BltMaskBitMapRastPort is a documented void vector.  A
                // failed portable preflight must not become a compatibility
                // success or overwrite D0 for the native/provider path.
                if (result == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.BltPattern:
            {
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryBltPattern(
                            state.A[1],
                            state.A[0],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            unchecked((short)state.D[2]),
                            unchecked((short)state.D[3]),
                            unchecked((short)state.D[4])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeBltPattern(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]),
                        unchecked((short)state.D[2]),
                        unchecked((short)state.D[3])))
                {
                    return false;
                }

                var result = _core.BltPattern(
                    state.A[1],
                    state.A[0],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3]),
                    // byteCount is the documented signed WORD in D4.  Keep
                    // only the low register word at the portable/provider
                    // seam; upper-register residue is not part of the ABI.
                    unchecked((short)state.D[4]));
                if (result == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.BltTemplate:
            {
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryBltTemplate(
                            state.A[0],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            state.A[1],
                            unchecked((short)state.D[2]),
                            unchecked((short)state.D[3]),
                            unchecked((short)state.D[4]),
                            unchecked((short)state.D[5])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeBltTemplate(
                        state.A[1],
                        unchecked((short)state.D[2]),
                        unchecked((short)state.D[3]),
                        unchecked((short)state.D[4]),
                        unchecked((short)state.D[5])))
                {
                    return false;
                }

                var result = _core.BltTemplate(
                    state.A[0],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    state.A[1],
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3]),
                    unchecked((short)state.D[4]),
                    unchecked((short)state.D[5]));
                if (result == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.AllocRaster:
            {
                // AllocRaster's public ABI takes UWORD width/height in
                // D0/D1.  The CPU register file retains the caller's full
                // 32-bit values, so consume only the low words just as the
                // resident 68k vector does.  Passing stale high bits through
                // to the portable allocator would reject an otherwise valid
                // guest request and steal it from the native/provider path.
                var raster = _core.AllocRaster(
                    (ushort)state.D[0],
                    (ushort)state.D[1]);
                if (raster == 0)
                    return false;

                state.D[0] = raster;
                return true;
            }
            case (int)GraphicsLvo.FreeRaster:
                // FreeRaster's width/height arguments are UWORDs in the
                // Kickstart ABI (D0:16/D1:16), like AllocRaster's UWORD
                // inputs.  The CPU register file stores the full 32-bit
                // values, so consume only the low words before ownership
                // checks and byte-count reconstruction.  Otherwise stale
                // high bits make a valid raster look foreign and leave it
                // allocated on the native-overlay path.
                var freeRasterWidth = (ushort)state.D[0];
                var freeRasterHeight = (ushort)state.D[1];
                if (nativeOverlay &&
                    !_core.IsOwnedRaster(
                        state.A[0],
                        freeRasterWidth,
                        freeRasterHeight))
                {
                    return false;
                }

                if (_core.FreeRaster(
                        state.A[0],
                        freeRasterWidth,
                        freeRasterHeight) !=
                    GraphicsRasterOperations.Success)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AllocBitMap:
            {
                // AllocBitMap uses ULONG width/height/depth/flags in D0..D3.
                // Preserve the complete dimensions for portable validation;
                // truncating them could allocate an unrelated smaller bitmap.
                // An active display-class provider owns even friendless
                // allocations: A0 == NULL does not make an RTG request
                // planar.  An explicit RTG friend likewise carries its own
                // allocation, stride, and format contract.  In either case
                // leave the complete call available to the provider (or to
                // native Kickstart when this adapter is installed as an
                // overlay) without first creating a portable bitmap.
                if (_preferProviderBitMapAllocations?.Invoke() == true ||
                    _isRtgBitMap?.Invoke(state.A[0]) == true)
                    return false;

                var bitMap = _core.AllocBitMap(
                    state.D[0],
                    state.D[1],
                    state.D[2],
                    state.D[3],
                    state.A[0]);
                if (bitMap == 0)
                    return false;

                state.D[0] = bitMap;
                return true;
            }
            case (int)GraphicsLvo.FreeBitMap:
                if (state.A[0] != 0 && (_isRtgBitMap?.Invoke(state.A[0]) ?? false))
                    return false;

                if (nativeOverlay &&
                    state.A[0] != 0 &&
                    !_core.IsOwnedBitMap(state.A[0]))
                    return false;

                if (_core.FreeBitMap(state.A[0]) != GraphicsRasterOperations.Success)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GetBitMapAttr:
                if (_isRtgBitMap?.Invoke(state.A[0]) ?? false)
                    return false;

                if (!_core.TryGetBitMapAttr(state.A[0], state.D[1], out var bitMapAttribute))
                    return false;

                state.D[0] = bitMapAttribute;
                return true;
            case (int)GraphicsLvo.GfxNew:
            {
                var node = _core.GfxNew(state.D[0]);
                if (node == 0)
                    return false;

                state.D[0] = node;
                return true;
            }
            case (int)GraphicsLvo.GfxFree:
                if (nativeOverlay)
                {
                    return _core.WithMonitorStateLock(() =>
                    {
                        if (!_core.IsOwnedExtendedNode(state.A[0]) ||
                            !CanPublishNativeGfxFree(state.A[0]) ||
                            !_core.GfxFree(state.A[0]))
                        {
                            return false;
                        }

                        state.D[0] = 0;
                        return true;
                    });
                }

                if (!_core.GfxFree(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GfxAssociate:
                if (nativeOverlay)
                {
                    return _core.WithMonitorStateLock(() =>
                    {
                        if (!_core.IsOwnedExtendedNode(state.A[1]) ||
                            !CanPublishNativeGfxAssociate(
                                state.A[0],
                                state.A[1]) ||
                            !_core.GfxAssociate(state.A[0], state.A[1]))
                        {
                            return false;
                        }

                        state.D[0] = 0;
                        return true;
                    });
                }

                if (!_core.GfxAssociate(state.A[0], state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GfxLookUp:
                var lookedUpNode = _core.GfxLookUp(state.A[0]);
                // A null key is the documented empty lookup and can be
                // claimed as a zero result.  For any non-null key, however,
                // a zero portable result is not proof that the key is
                // unassociated: the resident/native owner or a provider may
                // have its own association table.  Keep that request
                // available to the captured vector in native-overlay mode.
                if (nativeOverlay && state.A[0] != 0 && lookedUpNode == 0)
                    return false;

                state.D[0] = lookedUpNode;
                return true;
            case (int)GraphicsLvo.LoadView:
                // A NULL View is the blank-display form, but it can still be
                // operating on an RTG view currently published through a
                // native GfxBase.  Preserve that provider-owned handoff just
                // like a non-null RTG View instead of clearing ActiView or
                // asking the planar display owner to blank it.
                if (IsRtgView(state.A[1]) ||
                    (state.A[1] == 0 && IsRtgActiveView()))
                    return false;

                // CopperStart's compatibility builder owns generated raw
                // DspIns links, not public CopList descriptors.  A merged
                // linked View therefore cannot pass the resident
                // preallocated-singleton validator below, even though the
                // host has already proved the complete chain and CPR spans.
                // Hand that specific host shape to the scheduler-aware
                // publication callback; native-overlay calls still decline
                // so a resident/provider owner retains the vector.
                if (nativeOverlay)
                {
                    return _core.WithMonitorStateLock(() =>
                    {
                        if (!CanPublishNativeLoadView() ||
                            _core.LoadView(
                                state.A[1],
                                state.Cycles) == GraphicsRasterOperations.Failure)
                        {
                            return false;
                        }

                        state.D[0] = 0;
                        return true;
                    });
                }

                // LoadView is a documented void vector.  The portable core
                // retains a status internally so malformed guest Views can
                // fail closed without publishing them.  A successful call
                // still follows the void ABI; a failed validation leaves the
                // register state untouched for native/provider fallback.
                if (_core.LoadView(state.A[1], state.Cycles) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.CalcIVG:
                if (IsRtgView(state.A[0]) || IsRtgViewPort(state.A[1]))
                    return false;

                if (!_core.TryCalcIvg(state.A[0], state.A[1], out var scanLines))
                    return false;

                state.D[0] = scanLines;
                return true;
            case (int)GraphicsLvo.SetChipRev:
                // ChipRevBits0 is a public native GfxBase byte.  A mapped
                // read-only provider/image overlay owns that publication;
                // leave the original vector available before invoking the
                // host capability callback.  Compact images with no mapped
                // native tail keep the historical compatibility claim.
                if (nativeOverlay && !CanPublishNativeChipRevision())
                    return false;

                if (!_core.TrySetChipRev(state.D[0], out var actualChipRevBits))
                    return false;

                state.D[0] = actualChipRevBits;
                return true;
            case (int)GraphicsLvo.WaitTOF:
                if (!_core.TryWaitTOF(state.Cycles, out var topOfFrameCycle))
                    return false;

                state.Cycles = topOfFrameCycle;
                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.VBeamPos:
                if (!_core.TryVBeamPos(state.Cycles, out var beamPosition))
                    return false;

                state.D[0] = beamPosition;
                return true;
            case (int)GraphicsLvo.WaitBOVP:
                // CyberGraphX owns RTG viewport presentation and its frame
                // boundary.  Do not turn a readable RTG RasInfo envelope
                // into a planar wait; a provider patch must own the vector.
                if (IsRtgViewPort(state.A[0]))
                    return false;

                if (!_core.TryWaitBOVP(
                        state.A[0],
                        state.Cycles,
                        out var waitCycle))
                {
                    return false;
                }

                state.Cycles = waitCycle;
                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ScrollVPort:
            {
                // CyberGraphX owns RTG viewport presentation.  The planar
                // implementation may validate the RasInfo chain, but it
                // must not claim the vector merely because that structure is
                // readable; leave RTG viewports to their provider boundary.
                if (IsRtgViewPort(state.A[0]))
                    return false;

                bool TryScrollViewPort()
                {
                    if (!_core.TryScrollViewPort(
                            state.A[0],
                            state.Cycles,
                            out var scrollCycle))
                    {
                        return false;
                    }

                    state.Cycles = scrollCycle;
                    state.D[0] = 0;
                    return true;
                }

                // ScrollVPort enters the display provider and scheduler.
                // Keep that viewport mutation ordered with a graphics-base
                // handoff, just like LoadView publication.
                return nativeOverlay
                    ? _core.WithMonitorStateLock(TryScrollViewPort)
                    : TryScrollViewPort();
            }
            case (int)GraphicsLvo.ChangeVPBitMap:
            {
                // CyberGraphX owns RTG display surfaces.  Check both the
                // bitmap being installed and the currently attached bitmap
                // before the portable planar association can claim the
                // vector; a false result lets GraphicsServices forward the
                // call to the RTG provider boundary.
                if ((_isRtgBitMap?.Invoke(state.A[1]) ?? false) ||
                    IsRtgViewPort(state.A[0]))
                {
                    return false;
                }

                bool TryChangeViewPortBitMap()
                {
                    if (nativeOverlay &&
                        !CanPublishNativeChangeViewPortBitMap(state.A[0]))
                    {
                        return false;
                    }

                    if (!_core.TryChangeViewPortBitMap(
                            state.A[0],
                            state.A[1],
                            state.A[2],
                            state.Cycles,
                            out var nextCycle))
                    {
                        return false;
                    }

                    state.Cycles = nextCycle;
                    state.D[0] = 0;
                    return true;
                }

                // The guest RasInfo association and the display scheduler
                // form one lifecycle transaction. Keep native-overlay
                // writability admission paired with publication so a GfxBase
                // provider rebind cannot split the two phases.
                return nativeOverlay
                    ? _core.WithMonitorStateLock(TryChangeViewPortBitMap)
                    : TryChangeViewPortBitMap();
            }
            case (int)GraphicsLvo.MakeVPort:
            {
                if (IsRtgViewPort(state.A[1]))
                    return false;

                bool TryMakeViewPort()
                {
                    if (nativeOverlay &&
                        !CanPublishNativeMakeViewPort(state.A[1]))
                    {
                        return false;
                    }

                    state.D[0] = unchecked((uint)_core.MakeViewPort(
                        state.A[0],
                        state.A[1]));
                    return true;
                }

                // MakeVPort may create or reuse a ViewPortExtra association.
                // Keep its ownership admission and publication paired with
                // graphics-base rebinding of the shared extended-node table.
                return nativeOverlay
                    ? _core.WithMonitorStateLock(TryMakeViewPort)
                    : TryMakeViewPort();
            }
            case (int)GraphicsLvo.MrgCop:
            {
                if (IsRtgView(state.A[1]))
                    return false;

                bool TryMergeCopperLists()
                {
                    if (nativeOverlay &&
                        !CanPublishNativeVisibleCopperLinks(state.A[1]))
                    {
                        return false;
                    }

                    var mergeResult = _core.MergeCopperLists(state.A[1]);
                    if (mergeResult == GraphicsRasterOperations.Failure)
                    {
                        // A malformed guest View is not a completed legacy
                        // operation. Preserve the caller's registers and let
                        // the captured/native provider or tail-chain decide
                        // whether it can handle the vector.
                        return false;
                    }

                    state.D[0] = unchecked((uint)mergeResult);
                    return true;
                }

                // Copper-list admission, provider merge, and public-link
                // publication are one screen/view lifecycle transaction.
                return nativeOverlay
                    ? _core.WithMonitorStateLock(TryMergeCopperLists)
                    : TryMergeCopperLists();
            }
            case (int)GraphicsLvo.FreeVPortCopLists:
            {
                if (IsRtgViewPort(state.A[0]))
                    return false;

                bool TryFreeViewPortCopLists()
                {
                    // A legacy void copper-resource callback has no way to
                    // decline a valid resident/provider ViewPort. In a native
                    // overlay, leave non-null ViewPorts to that captured owner
                    // unless the display boundary exposes an explicit
                    // result-bearing ownership check. The direct compatibility
                    // path remains unchanged, and NULL keeps its native no-op.
                    if (nativeOverlay &&
                        !_core.CanClaimNativeVPortCopLists(state.A[0]))
                    {
                        return false;
                    }

                    // FreeVPortCopLists clears the same four public copper
                    // links that MakeVPort/MrgCop publish. A native overlay
                    // must admit those output LONGs before entering the
                    // transactional teardown so a read-only resident ViewPort
                    // remains available to Kickstart/provider ownership.
                    if (nativeOverlay &&
                        !CanPublishNativeViewPortCopperLinks(state.A[0]))
                    {
                        return false;
                    }

                    // A temporary ViewPortExtra is released after the copper
                    // links. Admit its guest backlink before entering the
                    // chained teardown so a read-only node cannot leave the
                    // ViewPort half-cleared for a native/provider owner.
                    if (nativeOverlay &&
                        !CanPublishNativeFreeViewPortCopLists(state.A[0]))
                    {
                        return false;
                    }

                    var result = _core.FreeViewPortCopLists(state.A[0]);
                    if (result != GraphicsRasterOperations.Success)
                        return false;

                    state.D[0] = 0;
                    return true;
                }

                // Teardown removes ViewPortExtra registry state as well as
                // guest links, so rebind must wait for the whole transaction.
                return nativeOverlay
                    ? _core.WithMonitorStateLock(TryFreeViewPortCopLists)
                    : TryFreeViewPortCopLists();
            }
            case (int)GraphicsLvo.FreeCopList:
                // FreeCopList clears both public links in the caller-owned
                // UCopList before releasing the registry-owned intermediate
                // buffers.  A native overlay must admit that complete owner
                // envelope first so a read-only provider mapping cannot enter
                // a partial teardown transaction.
                if (nativeOverlay &&
                    (!_core.TryGetNativeFreeCopListPublication(
                        state.A[0],
                        out var freeCopUserList) ||
                     !CanPublishNativeSpan(
                        freeCopUserList,
                        GraphicsLayouts.UCopListSize)))
                {
                    return false;
                }

                if (!_core.FreeCopList(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.FreeCprList:
                // A legacy void copper-resource callback has no way to
                // decline a valid resident/provider CprList.  In a native
                // overlay, leave non-null lists to that captured owner unless
                // the portable display boundary exposes an explicit
                // result-bearing ownership check.  The direct compatibility
                // path remains unchanged, and NULL keeps its native no-op.
                if (nativeOverlay &&
                    !_core.CanClaimNativeCprList(state.A[0]))
                {
                    return false;
                }

                if (!_core.FreeCprList(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.UCopperListInit:
            {
                // UCopperListInit clears and republishes the caller-owned
                // UCopList before allocating its internal CopList and
                // instruction buffers.  A native overlay must admit that
                // public record first so a readonly provider/image mapping
                // cannot consume the portable allocator on a call that must
                // remain available to resident Kickstart ownership.
                if (nativeOverlay &&
                    !CanPublishNativeSpan(
                        state.A[0],
                        GraphicsLayouts.UCopListSize))
                {
                    return false;
                }

                var copList = _core.UCopperListInit(
                    state.A[0],
                    (ushort)state.D[0]);
                if (copList == 0)
                    return false;

                state.D[0] = copList;
                return true;
            }
            case (int)GraphicsLvo.CMove:
                if (nativeOverlay &&
                    (!_core.TryGetCurrentCopperInstructionAddress(
                        state.A[1],
                        out var moveInstruction) ||
                     !CanPublishNativeSpan(
                         moveInstruction,
                         GraphicsLayouts.CopInsSize)))
                {
                    return false;
                }

                if (!_core.CMove(
                        state.A[1],
                        state.D[0],
                        (ushort)state.D[1]))
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.CWait:
                if (nativeOverlay &&
                    (!_core.TryGetCurrentCopperInstructionAddress(
                        state.A[1],
                        out var waitInstruction) ||
                     !CanPublishNativeSpan(
                         waitInstruction,
                         GraphicsLayouts.CopInsSize)))
                {
                    return false;
                }

                if (!_core.CWait(
                        state.A[1],
                        (ushort)state.D[0],
                        (ushort)state.D[1]))
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.CBump:
                if (nativeOverlay &&
                    !CanPublishNativeCopperBump(state.A[1]))
                {
                    return false;
                }

                if (!_core.CBump(state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GetVPModeID:
                if (IsRtgViewPort(state.A[0]))
                    return false;

                // A malformed standard ViewPort is not a portable result.
                // Leave the original registers untouched so Kickstart or a
                // monitor/provider patch can own the request.  A valid
                // viewport may still publish INVALID_ID when its native
                // mode cannot be derived.
                if (!_core.TryGetViewPortModeId(state.A[0], out var modeId))
                    return false;

                state.D[0] = modeId;
                return true;
            case (int)GraphicsLvo.FindDisplayInfo:
                if (!_core.IsNativeDisplayMode(state.D[0]))
                    return false;

                state.D[0] = _core.FindDisplayInfo(state.D[0]);
                return true;
            case (int)GraphicsLvo.NextDisplayInfo:
                if (state.D[0] != GraphicsModeIds.Invalid &&
                    !_core.IsNativeDisplayInfoHandle(state.D[0]))
                {
                    return false;
                }

                state.D[0] = _core.NextDisplayInfo(state.D[0]);
                return true;
            case (int)GraphicsLvo.ModeNotAvailable:
                if (!_core.IsNativeDisplayModeQuery(state.D[0]))
                    return false;

                state.D[0] = _core.ModeNotAvailable(state.D[0]);
                return true;
            case GraphicsPrivateLvo.SetDisplayInfoData:
                if (!_core.TrySetDisplayInfoData(
                        state.A[0],
                        state.A[1],
                        state.D[0],
                        state.D[1],
                        state.D[2],
                        out var setDisplayInfoDataResult))
                    return false;

                state.D[0] = setDisplayInfoDataResult;
                return true;
            case (int)GraphicsLvo.GetDisplayInfoData:
            {
                bool InvokeDisplayInfoData()
                {
                // GetDisplayInfoData copies a bounded public chunk into the
                // caller's A1 buffer.  Resolve only the fixed chunk length
                // here—without invoking the monitor-spec provider—so a
                // native-overlay image/provider mapping that is read-only is
                // declined before the portable database enters its
                // read/rollback writer.  NULL/empty buffers and unknown
                // public tags retain their valid zero-byte result boundary;
                // DTAG_VEC remains a monitor-driver handoff.
                if (nativeOverlay &&
                    state.A[1] != 0 &&
                    state.D[0] != 0)
                {
                    if (!_core.TryGetDisplayInfoDataOutputLength(
                            state.A[0],
                            state.D[0],
                            state.D[1],
                            state.D[2],
                            out var displayInfoOutputBytes) ||
                        (displayInfoOutputBytes != 0 &&
                         ((ulong)state.A[1] + displayInfoOutputBytes > 0x1_0000_0000UL ||
                          !CanPublishNativeByteSpan(
                             state.A[1],
                             displayInfoOutputBytes))))
                    {
                        return false;
                    }

                    // Only the compact host backend can lazily publish a node.
                    // CMDB readback is an opaque, read-only query even when its
                    // Mspc is null, malformed as a node, or reference-saturated.
                    if (displayInfoOutputBytes != 0 &&
                        state.D[1] == GraphicsDisplayDatabase.DtagMntr &&
                        _core.MonitorInfoNeedsHostPublication(state.A[0], state.D[2], displayInfoOutputBytes) &&
                        !CanPublishNativeOpenMonitor())
                    {
                        return false;
                    }
                }

                var result = _core.GetDisplayInfoData(
                    state.A[0],
                    state.A[1],
                    state.D[0],
                    state.D[1],
                    state.D[2]);
                if (result == GraphicsDisplayDatabase.Failure)
                    return false;

                // A zero result is valid for an unknown data tag or an empty
                // caller buffer.  For a recognized chunk with a non-empty
                // buffer request, zero means the guest span could not be
                // read/written and the native/provider path must retain
                // ownership instead of observing a fabricated success.
                if (result == 0 &&
                    state.A[1] != 0 &&
                    state.D[0] != 0 &&
                    !(state.A[0] == 0 &&
                      state.D[2] == GraphicsModeIds.Invalid) &&
                    (state.D[1] == GraphicsDisplayDatabase.DtagDisp ||
                     state.D[1] == GraphicsDisplayDatabase.DtagDims ||
                     state.D[1] == GraphicsDisplayDatabase.DtagMntr ||
                     state.D[1] == GraphicsDisplayDatabase.DtagName))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)result);
                return true;
                }

                return nativeOverlay
                    ? _core.WithMonitorStateLock(InvokeDisplayInfoData)
                    : InvokeDisplayInfoData();
            }
            case (int)GraphicsLvo.BestModeIDA:
            {
                // A valid native request with no matching record must still
                // publish INVALID_ID.  Only malformed tag/viewport data or
                // a foreign monitor/provider request remains unclaimed.
                if (!_core.TryBestModeIDA(state.A[0], out var result))
                    return false;

                state.D[0] = result;
                return true;
            }
            case (int)GraphicsLvo.CoerceMode:
            {
                if (IsRtgViewPort(state.A[0]))
                    return false;

                if (!_core.TryCoerceMode(
                    state.A[0],
                    state.D[0],
                    state.D[1],
                    out var result))
                    return false;

                state.D[0] = result;
                return true;
            }
            case (int)GraphicsLvo.OpenMonitor:
            {
                if (nativeOverlay)
                {
                    return _core.WithMonitorStateLock(() =>
                    {
                        if (!CanPublishNativeOpenMonitor() ||
                            !_core.TryOpenMonitor(
                                state.A[1],
                                state.D[0],
                                out var monitor))
                        {
                            return false;
                        }

                        state.D[0] = monitor;
                        return true;
                    });
                }

                if (!_core.TryOpenMonitor(state.A[1], state.D[0], out var compatibilityMonitor))
                    return false;

                state.D[0] = compatibilityMonitor;
                return true;
            }
            case (int)GraphicsLvo.CloseMonitor:
            {
                if (nativeOverlay)
                {
                    return _core.WithMonitorStateLock(() =>
                    {
                        if (!CanPublishNativeCloseMonitor(state.A[0]))
                            return false;

                        var nativeResult = _core.CloseMonitor(state.A[0]);
                        if (nativeResult == GraphicsMonitorOperations.CloseFailure)
                            return false;

                        state.D[0] = unchecked((uint)nativeResult);
                        return true;
                    });
                }

                var result = _core.CloseMonitor(state.A[0]);
                if (result == GraphicsMonitorOperations.CloseFailure)
                    return false;

                state.D[0] = unchecked((uint)result);
                return true;
            }
            case (int)GraphicsLvo.GetSprite:
                if (nativeOverlay &&
                    !CanPublishNativeGetSprite(state.A[0]))
                {
                    return false;
                }

                if (nativeOverlay && !_core.IsValidSimpleSprite(state.A[0]))
                    return false;

                state.D[0] = unchecked((uint)_core.GetSprite(
                    state.A[0],
                    unchecked((short)state.D[0])));
                return true;
            case (int)GraphicsLvo.FreeSprite:
                if (nativeOverlay &&
                    !_core.IsOwnedSprite(unchecked((short)state.D[0])))
                {
                    return false;
                }

                if (!_core.FreeSprite(unchecked((short)state.D[0])))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.MoveSprite:
            {
                if (nativeOverlay && _core.HasSpriteBackend)
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeMoveSprite(state.A[1]))
                {
                    return false;
                }

                var success = _core.MoveSprite(
                    state.A[0],
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]));
                if (!success)
                    return false;

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.ChangeSprite:
            {
                if (nativeOverlay && _core.HasSpriteBackend)
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeChangeSprite(state.A[1]))
                {
                    return false;
                }

                var success = _core.ChangeSprite(
                    state.A[0],
                    state.A[1],
                    state.A[2]);
                if (!success)
                    return false;

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.GetExtSpriteA:
                if (nativeOverlay && !_core.IsValidGetExtSpriteRequest(state.A[0], state.A[1]))
                    return false;

                if (_core.TryGetExtSpriteA(
                        state.A[0],
                        state.A[1],
                        out var spriteNumber))
                {
                    state.D[0] = unchecked((uint)spriteNumber);
                    return true;
                }

                // A host-side compatibility session historically claimed an
                // unsupported form with the normal -1 result.  Keep that
                // behavior outside a native overlay; in ROM mode a provider
                // decline must remain transparent to Kickstart.
                if (nativeOverlay)
                    return false;

                state.D[0] = unchecked((uint)GraphicsRasterOperations.Failure);
                return true;
            case (int)GraphicsLvo.AllocSpriteDataA:
            {
                // CyberGraphX owns sprite conversion from an RTG bitmap.
                // Do not allocate a portable ExtSprite/image pair merely
                // because the provider surface happens to expose a readable
                // standard-looking header.
                if (_isRtgBitMap?.Invoke(state.A[2]) == true)
                    return false;

                var extSprite = _core.AllocSpriteDataA(state.A[2], state.A[1]);
                if (nativeOverlay && extSprite == 0)
                    return false;

                state.D[0] = extSprite;
                return true;
            }
            case (int)GraphicsLvo.ChangeExtSpriteA:
            {
                if (nativeOverlay &&
                    !CanPublishNativeChangeExtSprite(state.A[1]))
                {
                    return false;
                }

                if (!_core.TryChangeExtSpriteA(
                        state.A[0],
                        state.A[1],
                        state.A[2],
                        state.A[3],
                        out var success))
                {
                    if (nativeOverlay)
                        return false;

                    // Preserve the legacy host-shaped failure result when a
                    // provider declines an unsupported display form.
                    state.D[0] = 0;
                    return true;
                }

                if (nativeOverlay && !success)
                    return false;

                state.D[0] = success ? 1u : 0u;
                return true;
            }
            case (int)GraphicsLvo.FreeSpriteData:
                // V40 documents a NULL ExtSprite as an idempotent no-op.  It
                // is therefore claimed even by the native overlay; only a
                // non-null foreign handle must tail-chain to the provider.
                if (nativeOverlay && state.A[2] != 0 && !_core.IsOwnedSpriteData(state.A[2]))
                    return false;

                _core.FreeSpriteData(state.A[2]);
                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.InitGels:
                if (nativeOverlay &&
                    !CanPublishNativeInitGels(
                        state.A[0],
                        state.A[1],
                        state.A[2]))
                {
                    return false;
                }

                if (!_core.InitGels(state.A[0], state.A[1], state.A[2]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetCollision:
                if (nativeOverlay &&
                    !CanPublishNativeSetCollision(
                        state.D[0],
                        state.A[1]))
                {
                    return false;
                }

                if (!_core.SetCollision(state.D[0], state.A[0], state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.InitMasks:
                if (nativeOverlay &&
                    !CanPublishNativeInitMasks(state.A[0]))
                {
                    return false;
                }

                if (!_core.InitMasks(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AddVSprite:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeAddVSprite(
                        state.A[0],
                        state.A[1]))
                {
                    return false;
                }

                if (!_core.AddVSprite(state.A[0], state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.RemVSprite:
                if (nativeOverlay && !CanPublishNativeRemVSprite(state.A[0]))
                {
                    return false;
                }

                if (!_core.RemVSprite(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SortGList:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeSortGList(state.A[1]))
                {
                    return false;
                }

                if (!_core.SortGList(state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AddBob:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeAddBob(
                        state.A[0],
                        state.A[1]))
                {
                    return false;
                }

                if (!_core.AddBob(state.A[0], state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.DoCollision:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (!_core.DoCollision(state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.DrawGList:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeDrawGList(
                        state.A[1],
                        state.A[0]))
                {
                    return false;
                }

                if (!_core.DrawGList(state.A[1], state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.RemIBob:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && _core.HasGelsBackend)
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeRemIBob(
                        state.A[0],
                        state.A[1],
                        state.A[2]))
                {
                    return false;
                }

                if (!_core.RemIBob(state.A[0], state.A[1], state.A[2]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AddAnimOb:
                if (IsRtgRastPort(state.A[2]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeAddAnimOb(
                        state.A[0],
                        state.A[1],
                        state.A[2]))
                {
                    return false;
                }

                if (!_core.AddAnimOb(state.A[0], state.A[1], state.A[2]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.Animate:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeAnimate(
                        state.A[0],
                        state.A[1]))
                {
                    return false;
                }

                if (!_core.Animate(state.A[0], state.A[1]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GetGBuffers:
            {
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeGetGBuffers(
                        state.A[0],
                        state.A[1]))
                {
                    return false;
                }

                var success = _core.GetGBuffers(state.A[0], state.A[1], state.D[0]);
                if (nativeOverlay && !success)
                    return false;

                state.D[0] = success ? 1u : 0u;
                return true;
            }
            case (int)GraphicsLvo.InitGMasks:
                if (nativeOverlay &&
                    !CanPublishNativeInitGMasks(state.A[0]))
                {
                    return false;
                }

                if (!_core.InitGMasks(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.FreeGBuffers:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeFreeGBuffers(
                        state.A[0],
                        state.A[1],
                        state.D[0]))
                {
                    return false;
                }

                if (!_core.FreeGBuffers(state.A[0], state.A[1], state.D[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.LockLayerRom:
                if ((state.A[5] & 1u) != 0)
                    return false;

                if (nativeOverlay && !_hasLayerLockProvider)
                    return false;

                _core.LockLayerRom(state.A[5]);
                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AttemptLockLayerRom:
                if ((state.A[5] & 1u) != 0)
                    return false;

                if (nativeOverlay && !_hasLayerLockProvider)
                    return false;

                state.D[0] = _core.AttemptLockLayerRom(state.A[5]) ? 1u : 0u;
                return true;
            case (int)GraphicsLvo.UnlockLayerRom:
                if ((state.A[5] & 1u) != 0)
                    return false;

                if (nativeOverlay && !_hasLayerLockProvider)
                    return false;

                _core.UnlockLayerRom(state.A[5]);
                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SyncSBitMap:
                if (!_core.SyncSBitMap(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.CopySBitMap:
                if (!_core.CopySBitMap(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.BitMapScale:
            {
                // BitMapScale publishes DestWidth/DestHeight back into the
                // caller-owned BitScaleArgs record.  Admit the complete
                // native envelope before decoding bitmap endpoints or
                // staging the planar transaction; a read-only provider/image
                // record must remain available to its owner.
                if (nativeOverlay &&
                    !CanPublishNativeSpan(
                        state.A[0],
                        GraphicsLayouts.BitScaleArgsSize))
                {
                    return false;
                }

                // The BitScaleArgs record names both endpoints.  Keep RTG
                // ownership with CyberGraphX instead of letting the planar
                // path touch a linear surface.
                if (state.A[0] > uint.MaxValue -
                    (uint)(GraphicsLayouts.BitScaleArgsDestBitMap + sizeof(uint) - 1) ||
                    !_core.Memory.TryReadLong(
                        state.A[0] + (uint)GraphicsLayouts.BitScaleArgsSrcBitMap,
                        out var sourceBitMap) ||
                    !_core.Memory.TryReadLong(
                        state.A[0] + (uint)GraphicsLayouts.BitScaleArgsDestBitMap,
                        out var destinationBitMap) ||
                    (_isRtgBitMap?.Invoke(sourceBitMap) ?? false) ||
                    (_isRtgBitMap?.Invoke(destinationBitMap) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    (!_core.IsOwnedBitMap(sourceBitMap) ||
                     !_core.IsOwnedBitMap(destinationBitMap)))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !CanPublishNativeBitMapScaleDestination(
                        state.A[0],
                        sourceBitMap,
                        destinationBitMap))
                {
                    return false;
                }

                var result = _core.BitMapScale(state.A[0]);
                if (result == GraphicsBitmapUtilityOperations.Failure)
                    return false;

                // BitMapScale is VOID; the guest-visible result is the two
                // destination extent words written into BitScaleArgs.
                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.ScalerDiv:
                state.D[0] = _core.ScalerDiv(
                    (ushort)state.D[0],
                    (ushort)state.D[1],
                    (ushort)state.D[2]);
                return true;
            case (int)GraphicsLvo.AllocDBufInfo:
            {
                // CyberGraphX owns double-buffer message envelopes for an
                // RTG viewport.  AllocDBufInfo is a viewport-bitmap
                // lifecycle vector, so keep its admission symmetric with
                // ChangeVPBitMap/ScrollVPort: do not allocate a portable
                // public DBufInfo merely because the viewport header is
                // readable.  The native/provider vector must retain the
                // original register frame for that surface.
                if (IsRtgViewPort(state.A[0]))
                    return false;

                var dbufInfo = _core.AllocDBufInfo(state.A[0]);
                if (dbufInfo == 0)
                    return false;

                state.D[0] = dbufInfo;
                return true;
            }
            case (int)GraphicsLvo.FreeDBufInfo:
            {
                // FreeDBufInfo takes its handle in A1, unlike AllocDBufInfo's
                // A0 ViewPort argument. A0 may name an unrelated live object.
                var result = _core.FreeDBufInfo(state.A[1]);
                if (result != GraphicsRasterOperations.Success)
                    return false;

                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.NewRegion:
            {
                var region = _core.NewRegion();
                if (region == 0)
                    return false;

                state.D[0] = region;
                return true;
            }
            case (int)GraphicsLvo.DisposeRegion:
                if (nativeOverlay && !_core.IsOwnedRegion(state.A[0]))
                    return false;

                if (_core.DisposeRegion(state.A[0]) != GraphicsRegionOperations.Success)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.OrRectRegion:
            {
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !CanPublishNativeRegion(state.A[0], includeExistingNodes: false)))
                    return false;

                var success = _core.OrRectRegion(state.A[0], state.A[1]);
                if (!success)
                    return false;

                state.D[0] = 1;
                return true;
            }
            case (int)GraphicsLvo.AndRectRegion:
            {
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !CanPublishNativeRegion(state.A[0], includeExistingNodes: true)))
                    return false;

                var success = _core.AndRectRegion(state.A[0], state.A[1]);
                if (!success)
                    return false;

                // AndRectRegion is the native void vector.  The portable
                // helper retains a success bit for guarded guest-memory
                // failure handling, but that status is not part of the 68k
                // ABI and must not leak through D0.
                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.XorRectRegion:
            {
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !CanPublishNativeRegion(state.A[0], includeExistingNodes: false)))
                    return false;

                var success = _core.XorRectRegion(state.A[0], state.A[1]);
                if (!success)
                    return false;

                state.D[0] = 1;
                return true;
            }
            case (int)GraphicsLvo.ClearRectRegion:
            {
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !CanPublishNativeRegion(state.A[0], includeExistingNodes: false)))
                    return false;

                var success = _core.ClearRectRegion(state.A[0], state.A[1]);
                if (!success)
                    return false;

                state.D[0] = 1;
                return true;
            }
            case (int)GraphicsLvo.ClearRegion:
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !CanPublishNativeRegion(state.A[0], includeExistingNodes: false)))
                    return false;

                if (!_core.ClearRegion(state.A[0]))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.OrRegionRegion:
            {
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !_core.IsOwnedRegion(state.A[1]) ||
                     !CanPublishNativeRegion(state.A[1], includeExistingNodes: false)))
                {
                    return false;
                }

                var success = _core.OrRegionRegion(state.A[0], state.A[1]);
                if (!success)
                    return false;

                state.D[0] = 1;
                return true;
            }
            case (int)GraphicsLvo.XorRegionRegion:
            {
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !_core.IsOwnedRegion(state.A[1]) ||
                     !CanPublishNativeRegion(state.A[1], includeExistingNodes: false)))
                {
                    return false;
                }

                var success = _core.XorRegionRegion(state.A[0], state.A[1]);
                if (!success)
                    return false;

                state.D[0] = 1;
                return true;
            }
            case (int)GraphicsLvo.AndRegionRegion:
            {
                if (nativeOverlay &&
                    (!_core.IsOwnedRegion(state.A[0]) ||
                     !_core.IsOwnedRegion(state.A[1]) ||
                     !CanPublishNativeRegion(state.A[1], includeExistingNodes: false)))
                {
                    return false;
                }

                var success = _core.AndRegionRegion(state.A[0], state.A[1]);
                if (!success)
                    return false;

                state.D[0] = 1;
                return true;
            }
            case (int)GraphicsLvo.OwnBlitter:
                if (!_core.OwnBlitter(state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.DisownBlitter:
                if (!_core.DisownBlitter(state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.WaitBlit:
                if (!_core.WaitBlit(state.Cycles, out var completedCycle))
                    return false;

                if (completedCycle > state.Cycles)
                    state.Cycles = completedCycle;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.QBlit:
                if (nativeOverlay &&
                    !CanPublishNativeQueuedBlitterNode(state.A[1]))
                {
                    return false;
                }

                if (!_core.QBlit(state.A[1], state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.QBSBlit:
                if (nativeOverlay &&
                    !CanPublishNativeQueuedBlitterNode(state.A[1]))
                {
                    return false;
                }

                if (!_core.QBSBlit(state.A[1], state.Cycles))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetAPen:
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativePenModeState(
                        state.A[1],
                        GraphicsLayouts.RastPortFgPen))
                {
                    return false;
                }

                if (_core.SetAPen(state.A[1], state.D[0]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetBPen:
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativePenModeState(
                        state.A[1],
                        GraphicsLayouts.RastPortBgPen))
                {
                    return false;
                }

                if (_core.SetBPen(state.A[1], state.D[0]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetDrMd:
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativePenModeState(
                        state.A[1],
                        GraphicsLayouts.RastPortDrawMode))
                {
                    return false;
                }

                if (_core.SetDrawMode(state.A[1], state.D[0]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GetAPen:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                var foregroundPen = _core.GetAPen(state.A[0]);
                if (foregroundPen == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)foregroundPen);
                return true;
            case (int)GraphicsLvo.GetBPen:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                var backgroundPen = _core.GetBPen(state.A[0]);
                if (backgroundPen == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)backgroundPen);
                return true;
            case (int)GraphicsLvo.GetDrMd:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                var drawMode = _core.GetDrawMode(state.A[0]);
                if (drawMode == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)drawMode);
                return true;
            case (int)GraphicsLvo.SetABPenDrMd:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                if (nativeOverlay &&
                    (!CanPublishNativePenModeState(
                         state.A[0],
                         GraphicsLayouts.RastPortFgPen) ||
                     !CanPublishNativePenModeState(
                         state.A[0],
                         GraphicsLayouts.RastPortBgPen) ||
                     !CanPublishNativePenModeState(
                         state.A[0],
                         GraphicsLayouts.RastPortDrawMode)))
                {
                    return false;
                }

                if (_core.SetABPenDrMd(
                        state.A[0],
                        state.D[0],
                        state.D[1],
                        state.D[2]) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetWriteMask:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeRastPortBytes(
                        state.A[0],
                        GraphicsLayouts.RastPortMask,
                        sizeof(byte)))
                {
                    return false;
                }

                if (_core.SetWriteMask(state.A[0], state.D[0]) == GraphicsRasterOperations.Failure)
                    return false;

                // Public ULONG success is nonzero; the helper's zero is internal status.
                state.D[0] = uint.MaxValue;
                return true;
            case (int)GraphicsLvo.SetMaxPen:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                if (nativeOverlay && state.D[0] != 0 &&
                    !CanPublishNativeRastPortBytes(
                        state.A[0],
                        GraphicsLayouts.RastPortMask,
                        sizeof(byte)))
                {
                    return false;
                }

                if (_core.SetMaxPen(state.A[0], state.D[0]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GetOutlinePen:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                var outlinePen = _core.GetOutlinePen(state.A[0]);
                if (outlinePen == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)outlinePen);
                return true;
            case (int)GraphicsLvo.SetOutlinePen:
                if (IsRtgRastPort(state.A[0]) &&
                    !HasLayeredRastPortLink(state.A[0]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeRastPortSpanSet(
                        state.A[0],
                        (GraphicsLayouts.RastPortOutlinePen, sizeof(byte)),
                        (GraphicsLayouts.RastPortFlags, sizeof(ushort))))
                {
                    return false;
                }

                var previousOutlinePen = _core.SetOutlinePen(state.A[0], state.D[0]);
                if (previousOutlinePen == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)previousOutlinePen);
                return true;
            case (int)GraphicsLvo.Move:
                // CyberGraphX owns cursor movement for RTG-backed RastPorts
                // together with the other raster vectors.  Leave the call
                // transparent before decoding coordinates or mutating the
                // guest cursor so the provider sees the original frame.
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeMoveState(state.A[1]))
                    return false;

                if (_core.Move(
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1])) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.Draw:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryDraw(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeDraw(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1])))
                {
                    return false;
                }

                if (_core.Draw(
                        state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1])) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AreaMove:
            case (int)GraphicsLvo.AreaDraw:
                if (IsLayeredRastPort(state.A[1]))
                {
                    var claimed = displacement == (int)GraphicsLvo.AreaMove
                        ? _layerRaster?.TryAreaMove(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1])) == true
                        : _layerRaster?.TryAreaDraw(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1])) == true;
                    if (!claimed)
                        return false;

                    state.D[0] = 0;
                    return true;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeAreaCollector(state.A[1]))
                    return false;

                var areaResult = displacement == (int)GraphicsLvo.AreaMove
                    ? _core.AreaMove(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]))
                    : _core.AreaDraw(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]));
                if (areaResult == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AreaEllipse:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryAreaEllipse(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            unchecked((short)state.D[2]),
                            unchecked((short)state.D[3])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeAreaCollector(state.A[1]))
                    return false;

                if (_core.AreaEllipse(
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3])) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AreaEnd:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryAreaEnd(state.A[1]) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeAreaEnd(state.A[1]))
                    return false;

                if (_core.AreaEnd(state.A[1]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ReadPixel:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster is not null &&
                        _layerRaster.TryReadPixel(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            out var providerColor))
                    {
                        state.D[0] = unchecked((uint)providerColor);
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (!_core.TryReadPixel(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]),
                        out var readPixelResult))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)readPixelResult);
                return true;
            case (int)GraphicsLvo.WritePixel:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryWritePixel(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeWritePixel(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1])))
                {
                    return false;
                }

                if (!_core.TryWritePixel(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]),
                        out var writePixelResult))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)writePixelResult);
                return true;
            case (int)GraphicsLvo.ReadPixelLine8:
            {
                if (!(_isRtgRastPort?.Invoke(state.A[0]) ?? false) &&
                    IsLayeredRastPort(state.A[0]))
                {
                    if (_layerRaster is not null &&
                        _layerRaster.TryReadPixelLine8(
                            state.A[0],
                            (ushort)state.D[0],
                            (ushort)state.D[1],
                            (ushort)state.D[2],
                            state.A[2],
                            state.A[1],
                            out var providerResult))
                    {
                        state.D[0] = unchecked((uint)providerResult);
                        return true;
                    }

                    return false;
                }

                // The V36 pixel-array ABI uses A0 for the RastPort and A1 for
                // the caller-owned temporary RastPort.  Layered and RTG
                // surfaces remain explicit fallback boundaries.
                if (!IsNonLayeredRastPort(state.A[0]) ||
                    (_isRtgRastPort?.Invoke(state.A[0]) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !CanPublishNativePixelArrayOutput(
                        state.A[2],
                        (ushort)state.D[2],
                        1))
                {
                    return false;
                }

                if (!_core.TryReadPixelLine8(
                    state.A[0],
                    (ushort)state.D[0],
                    (ushort)state.D[1],
                    (ushort)state.D[2],
                    state.A[2],
                    state.A[1],
                    out var result))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)result);
                return true;
            }
            case (int)GraphicsLvo.WritePixelLine8:
            {
                if (!(_isRtgRastPort?.Invoke(state.A[0]) ?? false) &&
                    IsLayeredRastPort(state.A[0]))
                {
                    if (_layerRaster is not null &&
                        _layerRaster.TryWritePixelLine8(
                            state.A[0],
                            (ushort)state.D[0],
                            (ushort)state.D[1],
                            (ushort)state.D[2],
                            state.A[2],
                            state.A[1],
                            out var providerResult))
                    {
                        state.D[0] = unchecked((uint)providerResult);
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[0]) ||
                    (_isRtgRastPort?.Invoke(state.A[0]) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !CanPublishNativePixelWriterRegion(
                        state.A[0],
                        (ushort)state.D[0],
                        (ushort)state.D[1],
                        (long)(ushort)state.D[0] + (ushort)state.D[2] - 1L,
                        (ushort)state.D[1]))
                {
                    return false;
                }

                if (!_core.TryWritePixelLine8(
                    state.A[0],
                    (ushort)state.D[0],
                    (ushort)state.D[1],
                    (ushort)state.D[2],
                    state.A[2],
                    state.A[1],
                    out var result))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)result);
                return true;
            }
            case (int)GraphicsLvo.ReadPixelArray8:
            {
                if (!(_isRtgRastPort?.Invoke(state.A[0]) ?? false) &&
                    IsLayeredRastPort(state.A[0]))
                {
                    if (_layerRaster is not null &&
                        _layerRaster.TryReadPixelArray8(
                            state.A[0],
                            (ushort)state.D[0],
                            (ushort)state.D[1],
                            (ushort)state.D[2],
                            (ushort)state.D[3],
                            state.A[2],
                            state.A[1],
                            out var providerResult))
                    {
                        state.D[0] = unchecked((uint)providerResult);
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[0]) ||
                    (_isRtgRastPort?.Invoke(state.A[0]) ?? false))
                {
                    return false;
                }

                var readArrayWidth = (long)(ushort)state.D[2] -
                    (ushort)state.D[0] + 1L;
                var readArrayHeight = (long)(ushort)state.D[3] -
                    (ushort)state.D[1] + 1L;
                if (nativeOverlay &&
                    !CanPublishNativePixelArrayOutput(
                        state.A[2],
                        readArrayWidth,
                        readArrayHeight))
                {
                    return false;
                }

                if (!_core.TryReadPixelArray8(
                    state.A[0],
                    (ushort)state.D[0],
                    (ushort)state.D[1],
                    (ushort)state.D[2],
                    (ushort)state.D[3],
                    state.A[2],
                    state.A[1],
                    out var result))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)result);
                return true;
            }
            case (int)GraphicsLvo.WritePixelArray8:
            {
                if (!(_isRtgRastPort?.Invoke(state.A[0]) ?? false) &&
                    IsLayeredRastPort(state.A[0]))
                {
                    if (_layerRaster is not null &&
                        _layerRaster.TryWritePixelArray8(
                            state.A[0],
                            (ushort)state.D[0],
                            (ushort)state.D[1],
                            (ushort)state.D[2],
                            (ushort)state.D[3],
                            state.A[2],
                            state.A[1],
                            out var providerResult))
                    {
                        state.D[0] = unchecked((uint)providerResult);
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[0]) ||
                    (_isRtgRastPort?.Invoke(state.A[0]) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !CanPublishNativePixelWriterRegion(
                        state.A[0],
                        (ushort)state.D[0],
                        (ushort)state.D[1],
                        (ushort)state.D[2],
                        (ushort)state.D[3]))
                {
                    return false;
                }

                if (!_core.TryWritePixelArray8(
                    state.A[0],
                    (ushort)state.D[0],
                    (ushort)state.D[1],
                    (ushort)state.D[2],
                    (ushort)state.D[3],
                    state.A[2],
                    state.A[1],
                    out var result))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)result);
                return true;
            }
            case (int)GraphicsLvo.WriteChunkyPixels:
            {
                if (!(_isRtgRastPort?.Invoke(state.A[0]) ?? false) &&
                    IsLayeredRastPort(state.A[0]))
                {
                    if (_layerRaster?.TryWriteChunkyPixels(
                            state.A[0],
                            unchecked((int)state.D[0]),
                            unchecked((int)state.D[1]),
                            unchecked((int)state.D[2]),
                            unchecked((int)state.D[3]),
                            state.A[2],
                            unchecked((int)state.D[4])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[0]) ||
                    (_isRtgRastPort?.Invoke(state.A[0]) ?? false))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !CanPublishNativePixelWriterRegion(
                        state.A[0],
                        unchecked((int)state.D[0]),
                        unchecked((int)state.D[1]),
                        unchecked((int)state.D[2]),
                        unchecked((int)state.D[3])))
                {
                    return false;
                }

                if (!_core.TryWriteChunkyPixels(
                    state.A[0],
                    unchecked((int)state.D[0]),
                    unchecked((int)state.D[1]),
                    unchecked((int)state.D[2]),
                    unchecked((int)state.D[3]),
                    state.A[2],
                    unchecked((int)state.D[4]),
                    out _))
                {
                    return false;
                }

                // The V40 entry point is VOID; clear D0 so the host/native
                // adapters do not leak an implementation-specific count.
                state.D[0] = 0;
                return true;
            }
            case (int)GraphicsLvo.RectFill:
                // RectFill's inverted geometry is a documented void no-op.
                // Keep the RTG/provider ownership probe first. A readable
                // layered RastPort must still reach its layer provider before
                // the standard-planar no-op; an unreadable layer endpoint is
                // instead claimable without a bitmap envelope.
                if (IsRtgRastPort(state.A[1]))
                    return false;

                var rectFillXMin = unchecked((short)state.D[0]);
                var rectFillYMin = unchecked((short)state.D[1]);
                var rectFillXMax = unchecked((short)state.D[2]);
                var rectFillYMax = unchecked((short)state.D[3]);
                if ((rectFillXMax < rectFillXMin || rectFillYMax < rectFillYMin) &&
                    !IsLayeredRastPort(state.A[1]))
                {
                    state.D[0] = 0;
                    return true;
                }

                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryRectFill(
                            state.A[1],
                            rectFillXMin,
                            rectFillYMin,
                            rectFillXMax,
                            rectFillYMax) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeRasterRegion(
                        state.A[1],
                        rectFillXMin,
                        rectFillYMin,
                        rectFillXMax,
                        rectFillYMax))
                {
                    return false;
                }

                if (_core.RectFill(
                        state.A[1],
                    rectFillXMin,
                    rectFillYMin,
                    rectFillXMax,
                    rectFillYMax) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.DrawEllipse:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryDrawEllipse(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            unchecked((short)state.D[2]),
                            unchecked((short)state.D[3])) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeRasterRegion(
                        state.A[1],
                        unchecked((short)state.D[0]) -
                            unchecked((short)state.D[2]),
                        unchecked((short)state.D[1]) -
                            unchecked((short)state.D[3]),
                        unchecked((short)state.D[0]) +
                            unchecked((short)state.D[2]),
                        unchecked((short)state.D[1]) +
                            unchecked((short)state.D[3])))
                {
                    return false;
                }

                if (_core.DrawEllipse(
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    unchecked((short)state.D[2]),
                    unchecked((short)state.D[3])) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ScrollRaster:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                var scrollXMin = unchecked((short)state.D[2]);
                var scrollYMin = unchecked((short)state.D[3]);
                var scrollXMax = unchecked((short)state.D[4]);
                var scrollYMax = unchecked((short)state.D[5]);
                if ((scrollXMax < scrollXMin || scrollYMax < scrollYMin) &&
                    !IsLayeredRastPort(state.A[1]))
                {
                    state.D[0] = 0;
                    return true;
                }

                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryScrollRaster(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            scrollXMin,
                            scrollYMin,
                            scrollXMax,
                            scrollYMax) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeScrollRaster(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]),
                        scrollXMin,
                        scrollYMin,
                        scrollXMax,
                        scrollYMax,
                        useBackgroundPen: false))
                {
                    return false;
                }

                if (_core.ScrollRaster(
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    scrollXMin,
                    scrollYMin,
                    scrollXMax,
                    scrollYMax) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ScrollRasterBF:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                var scrollBfXMin = unchecked((short)state.D[2]);
                var scrollBfYMin = unchecked((short)state.D[3]);
                var scrollBfXMax = unchecked((short)state.D[4]);
                var scrollBfYMax = unchecked((short)state.D[5]);
                if ((scrollBfXMax < scrollBfXMin || scrollBfYMax < scrollBfYMin) &&
                    !IsLayeredRastPort(state.A[1]))
                {
                    state.D[0] = 0;
                    return true;
                }

                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryScrollRasterBF(
                            state.A[1],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            scrollBfXMin,
                            scrollBfYMin,
                            scrollBfXMax,
                            scrollBfYMax) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeScrollRaster(
                        state.A[1],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1]),
                        scrollBfXMin,
                        scrollBfYMin,
                        scrollBfXMax,
                        scrollBfYMax,
                        useBackgroundPen: true))
                {
                    return false;
                }

                if (_core.ScrollRasterBF(
                    state.A[1],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]),
                    scrollBfXMin,
                    scrollBfYMin,
                    scrollBfXMax,
                    scrollBfYMax) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.Flood:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster is not null &&
                        _layerRaster.TryFlood(
                            state.A[1],
                            state.D[2],
                            unchecked((short)state.D[0]),
                            unchecked((short)state.D[1]),
                            out var providerResult))
                    {
                        state.D[0] = unchecked((uint)providerResult);
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeFlood(
                        state.A[1],
                        state.D[2],
                        unchecked((short)state.D[0]),
                        unchecked((short)state.D[1])))
                {
                    return false;
                }

                var floodResult = _core.Flood(
                    state.A[1],
                    state.D[2],
                    unchecked((short)state.D[0]),
                    unchecked((short)state.D[1]));
                // Flood returns the portable success/failure status.  A
                // malformed or otherwise unclaimable RastPort must remain
                // available to native Kickstart or a provider, rather than
                // being claimed with the internal -1 sentinel in D0.
                if (floodResult == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)floodResult);
                return true;
            case (int)GraphicsLvo.SetRast:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TrySetRast(
                            state.A[1],
                            state.D[0]) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeSetRast(state.A[1]))
                    return false;

                if (_core.SetRast(state.A[1], state.D[0]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.PolyDraw:
                // CyberGraphX still owns an RTG PolyDraw call even when the
                // low-word count is zero.  The portable zero-segment form is
                // a valid no-read no-op, but provider ownership must be
                // decided before that shortcut so the incoming register and
                // cycle frame remains available to the native/provider path.
                if (IsRtgRastPort(state.A[1]))
                    return false;

                // Apply the same provider-first rule to a readable layered
                // RastPort.  The provider may use an empty PolyDraw as an
                // ordering boundary; a portable no-op must not steal that
                // call.  IsLayeredRastPort is deliberately opportunistic, so
                // a malformed/unreadable endpoint still reaches the classic
                // standard-planar empty no-read success below.
                if ((ushort)state.D[0] == 0 && IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryPolyDraw(
                            state.A[1],
                            0,
                            state.A[0]) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                // PolyDraw's public prototype is LONG, but Kickstart consumes
                // only the low UWORD and performs no RastPort/point-table
                // dereference when that value is zero.  Keep this empty
                // vector on the same no-op boundary as the portable core so
                // an unreadable or provider-owned endpoint cannot be claimed
                // merely because the request has no line segments.
                if ((ushort)state.D[0] == 0)
                {
                    state.D[0] = 0;
                    return true;
                }

                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryPolyDraw(
                            state.A[1],
                            unchecked((ushort)state.D[0]),
                            state.A[0]) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativePolyDraw(
                        state.A[1],
                        unchecked((ushort)state.D[0]),
                        state.A[0]))
                {
                    return false;
                }

                if (_core.PolyDraw(
                        state.A[1],
                    unchecked((ushort)state.D[0]),
                    state.A[0]) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ClearEOL:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryClearEOL(state.A[1]) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeClear(state.A[1]))
                    return false;

                if (_core.ClearEOL(state.A[1]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ClearScreen:
                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryClearScreen(state.A[1]) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay && !CanPublishNativeClear(state.A[1]))
                    return false;

                if (_core.ClearScreen(state.A[1]) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.EraseRect:
                if (IsRtgRastPort(state.A[1]))
                    return false;

                var eraseXMin = unchecked((short)state.D[0]);
                var eraseYMin = unchecked((short)state.D[1]);
                var eraseXMax = unchecked((short)state.D[2]);
                var eraseYMax = unchecked((short)state.D[3]);
                if ((eraseXMax < eraseXMin || eraseYMax < eraseYMin) &&
                    !IsLayeredRastPort(state.A[1]))
                {
                    state.D[0] = 0;
                    return true;
                }

                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryEraseRect(
                            state.A[1],
                            eraseXMin,
                            eraseYMin,
                            eraseXMax,
                            eraseYMax) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    (!CanPublishNativePenModeState(
                         state.A[1],
                         GraphicsLayouts.RastPortFgPen) ||
                     !CanPublishNativeRasterRegion(
                         state.A[1],
                         eraseXMin,
                         eraseYMin,
                         eraseXMax,
                         eraseYMax)))
                {
                    return false;
                }

                if (_core.EraseRect(
                    state.A[1],
                    eraseXMin,
                    eraseYMin,
                    eraseXMax,
                    eraseYMax) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.TextLength:
            {
                // CyberGraphX owns metric queries for RTG-backed RastPorts
                // together with the drawing vectors.  Do this ownership
                // check before decoding D0 or the guest Font/cache fields so
                // the provider sees the original register/cycle frame.
                if (IsRtgRastPort(state.A[1]))
                    return false;

                // Kickstart takes a signed WORD count in D0.  Ignore any
                // unrelated high-word residue, but leave a negative low word
                // available to the native/provider implementation rather than
                // turning it into a huge host walk.
                var countWord = unchecked((short)state.D[0]);
                if (countWord < 0)
                    return false;

                var count = (uint)countWord;
                if (!CanAddress(
                        state.A[1],
                        GraphicsLayouts.RastPortFont,
                        sizeof(uint)) ||
                    !_core.Memory.TryReadLong(
                        state.A[1] + (uint)GraphicsLayouts.RastPortFont,
                        out var textLengthFont))
                {
                    return false;
                }

                // A NULL guest Font is the compatibility fixed-metric state;
                // a non-NULL but undecodable Font is malformed ownership and
                // must remain available to native Kickstart/provider code.
                var hasDecodedFont = textLengthFont != 0 &&
                    _fonts.TryGetMetricMetrics(textLengthFont, out _);
                if (textLengthFont != 0 && !hasDecodedFont)
                    return false;

                if (!hasDecodedFont &&
                    (!CanAddress(
                        state.A[1],
                        GraphicsLayouts.RastPortTextWidth,
                        sizeof(ushort)) ||
                     !CanAddress(
                        state.A[1],
                        GraphicsLayouts.RastPortTextSpacing,
                        sizeof(ushort))))
                {
                    return false;
                }

                var measured = hasDecodedFont
                    ? _core.TryTextLength(state.A[1], state.A[0], count, _fonts, out var textLength)
                    : _core.TryTextLength(state.A[1], count, out textLength);
                if (!measured)
                    return false;

                // TextLength returns a signed WORD and also takes a signed
                // WORD input count. Keep the core's wider accumulation for
                // overflow detection, then expose the classic sign-extended
                // 16-bit result in D0.
                state.D[0] = unchecked((uint)(short)textLength);
                return true;
            }
            case (int)GraphicsLvo.OpenFont:
                // Native overlay lookup must use the rebound guest list and
                // its resident DefaultFont.  The compatibility callback is
                // deliberately host-owned and therefore cannot claim a
                // native call when the list backend is absent or A0 is null.
                if (nativeOverlay && (_fontList is null || state.A[0] == 0))
                    return false;

                if (nativeOverlay && !CanPublishNativeOpenFont(state.A[0]))
                    return false;

                if (state.A[0] == 0 && _ensureCompatibilityFont is not null)
                {
                    state.D[0] = _ensureCompatibilityFont();
                    return true;
                }

                // OpenFont returns a valid zero when no candidate matches,
                // but malformed TextAttr/list state must remain unclaimed so
                // native Kickstart or a provider can observe the failure.
                if (!_fonts.TryValidateTextAttrRequest(state.A[0]) ||
                    (_fontList is not null && !_fontList.TryEnumerate(out _)))
                {
                    return false;
                }

                state.D[0] = _core.OpenFont(state.A[0], _fonts);
                return true;
            case (int)GraphicsLvo.WeighTAMatch:
                // A zero weight is a valid metric result for an unsuitable
                // but readable pair.  Preserve the matcher status separately
                // so an unreadable or malformed guest TextAttr/tag chain is
                // left available to the native/provider vector instead of
                // being claimed as a successful zero score.
                if (!_core.TryWeighTAMatch(
                        state.A[0],
                        state.A[1],
                        state.A[2],
                        _fonts,
                        out var matchWeight))
                {
                    return false;
                }

                state.D[0] = unchecked((uint)matchWeight);
                return true;
            case (int)GraphicsLvo.CloseFont:
                // CloseFont(NULL) is a documented idempotent no-op.  It does
                // not need the rebound guest TextFonts list, so keep this
                // safe ABI form claimable even when a native overlay has no
                // portable font-list backend and all non-null handles must
                // remain available to Kickstart/provider ownership.
                if (state.A[1] == 0)
                {
                    state.D[0] = 0;
                    return true;
                }

                if (nativeOverlay &&
                    !CanPublishNativeCloseFont(state.A[1]))
                    return false;

                if (_core.CloseFont(state.A[1], _fonts) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AskFont:
                // AskFont reads the selected font from the RastPort and
                // publishes provider-specific attributes into the caller's
                // TextAttr.  A layer-backed RTG RastPort still exposes this
                // guest metadata to the portable Layers text contract; an
                // unlinked RTG RastPort remains provider-owned. Keep the
                // ownership decision ahead of all guest reads/writes.
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[0], GraphicsLayouts.TextAttrSize))
                    return false;

                if (_core.AskFont(state.A[1], state.A[0]) != GraphicsRasterOperations.Success)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AddFont:
                // A native overlay may claim list mutation only when the
                // caller supplied the explicit guest TextFonts backend.  A
                // memory-only registry without that backend cannot see the
                // resident list owned by native graphics.library, so keep
                // the mapped vector transparent in that configuration.
                if (nativeOverlay && _fontList is null)
                    return false;

                if (nativeOverlay && !CanPublishNativeAddFont(state.A[1]))
                    return false;

                if (_core.AddFont(state.A[1], _fonts) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.RemFont:
                if (nativeOverlay && _fontList is null)
                    return false;

                if (nativeOverlay && !CanPublishNativeRemFont(state.A[1]))
                    return false;

                if (_core.RemFont(state.A[1], _fonts) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.ExtendFont:
                // ExtendFont allocates and publishes a TextFontExtension.
                // A native overlay may claim that ownership only when the
                // rebound guest TextFonts backend is present; otherwise the
                // host allocator would mutate a native font while the
                // original Kickstart routine still owns its extension ABI.
                if (nativeOverlay && _fontList is null)
                    return false;

                // A malformed native list must remain transparent just like
                // OpenFont/AddFont/RemFont.  Do this before allocating so a
                // failed overlay call cannot leave a host extension behind.
                if (_fontList is not null && !_fontList.TryEnumerate(out _))
                    return false;

                if (nativeOverlay && !CanPublishNativeExtendFont(state.A[0]))
                    return false;

                if (!_core.ExtendFont(state.A[0], state.A[1], _fonts))
                    return false;

                state.D[0] = 1;
                return true;
            case (int)GraphicsLvo.StripFont:
                // StripFont(NULL) is an idempotent no-op.  It is safe to
                // claim without a guest TextFonts backend; only a non-null
                // font asks this overlay to detach extension state that may
                // belong to native graphics.library or a provider.
                if (state.A[0] == 0)
                {
                    state.D[0] = 0;
                    return true;
                }

                if (nativeOverlay && _fontList is null)
                    return false;

                if (_fontList is not null && !_fontList.TryEnumerate(out _))
                    return false;

                if (nativeOverlay && !CanPublishNativeStripFont(state.A[0]))
                    return false;

                if (_core.StripFont(state.A[0], _fonts) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.SetFont:
                // A layered RastPort may point at a CyberGraphX/RTG bitmap.
                // The layer gateway still consumes the portable text vectors,
                // so its guest font/cache fields must be initialised before
                // the provider sees ClearEOL, ClearScreen, or Text.  Keep a
                // standalone RTG RastPort available to CyberGraphX while
                // allowing this metadata-only update on a layer-backed port.
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                // SetFont publishes the selected TextFont pointer and its
                // cached metrics/style fields into the caller-owned RastPort.
                // Admit the complete envelope before resolving a guest font,
                // so a read-only provider/image RastPort cannot consume host
                // font state and then fail during publication.
                if (nativeOverlay &&
                    !CanPublishNativeRastPort(state.A[1], GraphicsLayouts.RastPortSize))
                    return false;

                // A native overlay may resolve NULL A0 only through the
                // rebound guest GfxBase.DefaultFont.  Without that list, the
                // host compatibility provider must not claim the mapped
                // vector and mutate a native RastPort on its behalf.
                if (nativeOverlay && _fontList is null && state.A[0] == 0)
                    return false;

                var fontAddress = state.A[0];
                if (fontAddress == 0)
                {
                    // The classic ABI treats a NULL TextFont pointer as a
                    // request for GfxBase->DefaultFont.  Native-overlay
                    // calls must read the rebound guest list, while the
                    // compatibility host uses its explicit default-font
                    // provider.  If neither owner can supply a valid
                    // default, keep the vector unclaimed for Kickstart or a
                    // provider rather than publishing a partial cache.
                    if (_fontList is not null)
                    {
                        if (!_fontList.TryGetDefaultFont(out fontAddress))
                            return false;
                    }
                    else
                    {
                        fontAddress = _ensureCompatibilityFont?.Invoke() ?? 0;
                    }
                }

                if (fontAddress == 0 || !_fonts.HasMetrics(fontAddress))
                    return false;

                if (_core.SetFont(state.A[1], fontAddress, _fonts) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.AskSoftStyle:
                // A layer-backed RastPort may carry an RTG bitmap while its
                // font/style bytes remain the guest metadata consumed by the
                // portable Layers text path.  Keep standalone RTG ports
                // provider-owned; only admit the metadata query for a valid
                // layer link.
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                // AskSoftStyle's ULONG result may legitimately be
                // 0xffffffff, so retain admission separately from the value.
                // The portable query also guards exactly the RP.Font LONG
                // envelope while accepting a readable null Font as zero.
                if (!GraphicsTextOperations.TryAskSoftStyle(
                        _core.Memory,
                        state.A[1],
                        _fonts,
                        out var softStyle))
                    return false;

                state.D[0] = softStyle;
                return true;
            case (int)GraphicsLvo.SetSoftStyle:
                // Match SetFont/AskSoftStyle: style metadata on a
                // layer-linked RTG RastPort belongs to the guest-facing
                // layer contract, while an unlinked RTG RastPort remains a
                // CyberGraphX/provider vector.
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeRastPortBytes(
                        state.A[1],
                        GraphicsLayouts.RastPortAlgoStyle,
                        sizeof(byte)))
                {
                    return false;
                }

                var previousSoftStyle = _core.SetSoftStyle(
                    state.A[1],
                    state.D[0],
                    state.D[1],
                    _fonts);
                if (previousSoftStyle == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)previousSoftStyle);
                return true;
            case (int)GraphicsLvo.SetRPAttrsA:
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeRastPortTagOutputs(
                        state.A[1],
                        state.A[0]))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !GraphicsRastPortAttributeOperations.IsNativeSafeOverlayTagList(
                        _core.Memory,
                        state.A[0]))
                {
                    return false;
                }

                if (!GraphicsRastPortAttributeOperations.TryRequiresDefaultFont(
                        _core.Memory,
                        state.A[0],
                        out var requiresDefaultFont))
                {
                    return false;
                }

                var tagDefaultFont = 0u;
                if (requiresDefaultFont)
                {
                    // RPTAG_Font=0 follows SetFont(NULL): resolve the
                    // selected GfxBase default only for a tag list that
                    // actually asks for it.  Native-overlay calls require
                    // the rebound guest list; the compatibility host uses
                    // its explicit default-font provider.
                    if (nativeOverlay && _fontList is null)
                    {
                        return false;
                    }

                    if (_fontList is not null)
                    {
                        if (!_fontList.TryGetDefaultFont(out tagDefaultFont))
                            return false;
                    }
                    else
                    {
                        tagDefaultFont = _ensureCompatibilityFont?.Invoke() ?? 0;
                    }

                    if (tagDefaultFont == 0 || !_fonts.HasMetrics(tagDefaultFont))
                        return false;
                }

                if (!_core.SetRPAttrs(
                        state.A[1],
                        state.A[0],
                        _fonts,
                        tagDefaultFont))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.GetRPAttrsA:
                if (IsRtgRastPort(state.A[1]) &&
                    !HasLayeredRastPortLink(state.A[1]))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeGetOutputSpans(state.A[0]))
                {
                    return false;
                }

                if (nativeOverlay &&
                    !GraphicsRastPortAttributeOperations.IsNativeSafeOverlayTagList(
                        _core.Memory,
                        state.A[0]))
                {
                    return false;
                }

                if (!_core.GetRPAttrs(state.A[1], state.A[0], _fonts))
                    return false;

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.Text:
                // Keep RTG/CyberGraphX ownership ahead of the documented
                // empty-string no-op.  Text(count == 0) does not consume
                // guest text or RastPort state on the planar path, but an
                // RTG provider still owns the vector and may use that call as
                // an ordering boundary.  Do not let the compatibility
                // adapter claim it merely because there is no glyph work.
                if (IsRtgRastPort(state.A[1]))
                    return false;

                var textCountWord = unchecked((short)state.D[0]);
                if (textCountWord < 0)
                    return false;

                var textCount = (uint)textCountWord;
                // Text(count == 0) is the documented empty-string no-op. The
                // portable core returns success before reading the text
                // pointer or font. A readable layered RastPort is instead
                // handed to the explicit layer provider first, matching the
                // non-empty Text ownership split while preserving the
                // standard-planar no-read path for null/unreadable ports.
                if (textCount == 0)
                {
                    if (IsLayeredRastPort(state.A[1]))
                    {
                        if (_layerRaster?.TryText(
                                state.A[1],
                                state.A[0],
                                0) == true)
                        {
                            state.D[0] = 0;
                            return true;
                        }

                        return false;
                    }

                    state.D[0] = 0;
                    return true;
                }

                if (IsLayeredRastPort(state.A[1]))
                {
                    if (_layerRaster?.TryText(
                            state.A[1],
                            state.A[0],
                            textCountWord) == true)
                    {
                        state.D[0] = 0;
                        return true;
                    }

                    return false;
                }

                if (!IsNonLayeredRastPort(state.A[1]) ||
                    (!TryReadFontMetricsAddress(state.A[1], out _) &&
                     !TryReadMetricOnlyTextAddress(state.A[1], out _)))
                {
                    return false;
                }

                if (nativeOverlay && !CanPublishNativeText(state.A[1]))
                    return false;

                if (_core.Text(
                        state.A[1],
                        state.A[0],
                        textCount,
                        _fonts) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.TextExtent:
                // TextExtent reads the RastPort's selected font and spacing;
                // an RTG RastPort may carry a provider-specific metric model.
                // Keep the entire query transparent to that provider before
                // touching the caller's count or result envelope.
                if (IsRtgRastPort(state.A[1]))
                    return false;

                var extentCountWord = unchecked((short)state.D[0]);
                if (extentCountWord < 0)
                    return false;

                var extentCount = (uint)extentCountWord;
                if ((state.A[0] == 0 && extentCount != 0) ||
                    state.A[2] == 0 ||
                    (state.A[2] & 1u) != 0 ||
                    !CanAddress(state.A[2], 0, GraphicsLayouts.TextExtentSize))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[2], GraphicsLayouts.TextExtentSize))
                    return false;

                if (!TryReadMetricQueryAddress(state.A[1], out _))
                    return false;

                if (_core.TextExtent(
                    state.A[1],
                    state.A[0],
                    extentCount,
                    state.A[2],
                    _fonts) == GraphicsRasterOperations.Failure)
                {
                    return false;
                }

                state.D[0] = 0;
                return true;
            case (int)GraphicsLvo.TextFit:
                // TextFit is likewise part of the RTG text contract.  Its
                // result extent and alternate constraints must remain owned
                // by the provider when the RastPort is RTG-backed.
                if (IsRtgRastPort(state.A[1]))
                    return false;

                var fitCount = (uint)(ushort)state.D[0];
                var fitDirection = unchecked((short)state.D[1]);
                var fitWidth = (uint)(ushort)state.D[2];
                var fitHeight = (uint)(ushort)state.D[3];
                if ((state.A[0] == 0 && fitCount != 0) ||
                    state.A[2] == 0 ||
                    (state.A[2] & 1u) != 0 ||
                    !CanAddress(state.A[2], 0, GraphicsLayouts.TextExtentSize) ||
                    (fitCount != 0 && !TryReadMetricQueryAddress(state.A[1], out _)) ||
                    (fitCount != 0 &&
                     state.A[3] != 0 &&
                     !CanAddress(state.A[3], 0, GraphicsLayouts.TextExtentSize)))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[2], GraphicsLayouts.TextExtentSize))
                    return false;

                // TextFit takes UWORD strLen/width/height and a signed WORD
                // direction.  The upper halves of the 68k data registers are
                // not part of this ABI.
                var fittedCharacters = _core.TextFit(
                    state.A[1],
                    state.A[0],
                    fitCount,
                    state.A[2],
                    state.A[3],
                    fitDirection,
                    fitWidth,
                    fitHeight,
                    _fonts);
                if (fittedCharacters == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = unchecked((uint)fittedCharacters);
                return true;
            case (int)GraphicsLvo.FontExtent:
                if (state.A[0] == 0 ||
                    state.A[1] == 0 ||
                    (state.A[1] & 1u) != 0 ||
                    !CanAddress(state.A[1], 0, GraphicsLayouts.TextExtentSize))
                    return false;

                if (nativeOverlay &&
                    !CanPublishNativeSpan(state.A[1], GraphicsLayouts.TextExtentSize))
                    return false;

                if (_core.FontExtent(state.A[0], state.A[1], _fonts) == GraphicsRasterOperations.Failure)
                    return false;

                state.D[0] = 0;
                return true;
            default:
                return false;
        }

    }

    private bool IsRtgViewPort(uint viewPort)
    {
        if (_isRtgBitMap is null ||
            !CanAddress(
                viewPort,
                GraphicsLayouts.ViewPortRasInfo,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var rasInfo) ||
            rasInfo == 0)
        {
            return false;
        }

        // A viewport may carry more than one RasInfo node (for example a
        // dual-playfield or an interleaved display chain). CyberGraphX owns
        // the viewport when any linked bitmap is provider-owned, not only
        // when the first node happens to carry the RTG surface. Keep this
        // probe bounded and fail closed on a malformed/cyclic chain so a
        // native/provider owner can still claim the undecodable envelope.
        var visited = new HashSet<uint>();
        for (var node = 0; node < 64 && rasInfo != 0; node++)
        {
            if (!visited.Add(rasInfo) ||
                !CanAddress(
                    rasInfo,
                    GraphicsLayouts.RasInfoBitMap,
                    sizeof(uint)) ||
                !_core.Memory.TryReadLong(
                    rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                    out var bitMap))
            {
                return false;
            }

            if (bitMap != 0 && _isRtgBitMap(bitMap))
                return true;

            if (!CanAddress(
                    rasInfo,
                    GraphicsLayouts.RasInfoNext,
                    sizeof(uint)) ||
                !_core.Memory.TryReadLong(
                    rasInfo + (uint)GraphicsLayouts.RasInfoNext,
                    out var next))
            {
                return false;
            }

            rasInfo = next;
        }

        return false;
    }

    private bool IsRtgView(uint view)
    {
        if (_isRtgBitMap is null || view == 0 ||
            !CanAddress(
                view,
                GraphicsLayouts.ViewViewPort,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                out var viewPort))
        {
            return false;
        }

        for (var index = 0; index < 64 && viewPort != 0; index++)
        {
            if (IsRtgViewPort(viewPort))
                return true;

            if (!CanAddress(
                    viewPort,
                    GraphicsLayouts.ViewPortNext,
                    sizeof(uint)))
            {
                return false;
            }

            if (!_core.Memory.TryReadLong(
                    viewPort + (uint)GraphicsLayouts.ViewPortNext,
                    out viewPort))
            {
                return false;
            }
        }

        return false;
    }

    private bool IsRtgActiveView()
    {
        var graphicsBase = _core.GraphicsLibraryBase;
        if (_isRtgBitMap is null ||
            graphicsBase == 0 ||
            (graphicsBase & 1u) != 0 ||
            !CanAddress(
                graphicsBase,
                GraphicsLayouts.GfxBaseActiView,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                graphicsBase + (uint)GraphicsLayouts.GfxBaseActiView,
                out var activeView) ||
            activeView == 0)
        {
            return false;
        }

        return IsRtgView(activeView);
    }

    private bool IsRtgRastPort(uint rastPort)
    {
        // RastPort contains WORD/LONG fields.  A native 68000 takes an
        // address-error trap before an odd base can be classified as an RTG
        // or layer-owned object; keep the host ownership probe from turning
        // that boundary into a provider claim.
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (_isRtgRastPort?.Invoke(rastPort) == true)
            return true;

        // A CyberGraphX provider commonly registers ownership on the bitmap
        // object, not on every RastPort that happens to reference it.  The
        // classic RastPort contract exposes that bitmap at rp_BitMap, so
        // resolve the link before any planar vector claims the port.  An
        // unreadable link is deliberately not classified as RTG here; the
        // normal guarded decoder will then leave malformed state unclaimed.
        return _isRtgBitMap is not null &&
               CanAddress(
                   rastPort,
                   GraphicsLayouts.RastPortBitMap,
                   sizeof(uint)) &&
               _core.Memory.TryReadLong(
                   rastPort + (uint)GraphicsLayouts.RastPortBitMap,
                   out var bitMap) &&
               bitMap != 0 &&
               _isRtgBitMap(bitMap);
    }

    private bool IsNonLayeredRastPort(uint rastPort)
        => rastPort != 0 && (rastPort & 1u) == 0 &&
           !IsRtgRastPort(rastPort) &&
           CanAddress(rastPort, GraphicsLayouts.RastPortLayer, sizeof(uint)) &&
           _core.Memory.TryReadLong(
               rastPort + (uint)GraphicsLayouts.RastPortLayer,
               out var layer) && layer == 0;

    private bool TryReadFontMetricsAddress(uint rastPort, out uint font)
    {
        font = 0;
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (!CanAddress(
                rastPort,
                GraphicsLayouts.RastPortFont,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortFont,
                out font) ||
            font == 0)
        {
            return false;
        }

        return _fonts.HasMetrics(font);
    }

    private bool TryReadMetricOnlyTextAddress(uint rastPort, out uint font)
    {
        font = 0;
        if (rastPort == 0 || (rastPort & 1u) != 0 ||
            !CanAddress(
                rastPort,
                GraphicsLayouts.RastPortFont,
                sizeof(uint)) ||
            !CanAddress(
                rastPort,
                GraphicsLayouts.RastPortMask,
                sizeof(byte)) ||
            !_core.Memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortFont,
                out font) ||
            font == 0 ||
            !_core.Memory.TryReadByte(
                rastPort + (uint)GraphicsLayouts.RastPortMask,
                out var writeMask) ||
            !GraphicsRasterOperations.TryReadBitmap(
                _core.Memory,
                rastPort,
                out var bitmap) ||
            GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask) != 0)
        {
            return false;
        }

        // A zero effective write mask cannot publish a bitmap bit. Admit only
        // the metric/glyph-table envelope here; GraphicsTextOperations will
        // still validate the RastPort/bitmap and use TryGetMetricGlyph for
        // cursor movement, while nonzero effective masks retain strict strike
        // checks.
        return _fonts.TryGetMetricMetrics(font, out _);
    }

    private bool TryReadMetricQueryAddress(uint rastPort, out uint font)
    {
        font = 0;
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (!CanAddress(
                rastPort,
                GraphicsLayouts.RastPortFont,
                sizeof(uint)) ||
            !_core.Memory.TryReadLong(
                rastPort + (uint)GraphicsLayouts.RastPortFont,
                out font) ||
            font == 0)
        {
            return false;
        }

        return _fonts.TryGetMetricMetrics(font, out _);
    }

    private bool IsLayeredRastPort(uint rastPort)
        => rastPort != 0 && (rastPort & 1u) == 0 &&
           !IsRtgRastPort(rastPort) &&
           CanAddress(rastPort, GraphicsLayouts.RastPortLayer, sizeof(uint)) &&
           _core.Memory.TryReadLong(
               rastPort + (uint)GraphicsLayouts.RastPortLayer,
                out var layer) && layer != 0;

    private bool HasLayeredRastPortLink(uint rastPort)
        => rastPort != 0 && (rastPort & 1u) == 0 &&
           CanAddress(rastPort, GraphicsLayouts.RastPortLayer, sizeof(uint)) &&
           _core.Memory.TryReadLong(
               rastPort + (uint)GraphicsLayouts.RastPortLayer,
               out var layer) && layer != 0;

    private static bool CanAddress(uint address, int offset, int byteCount)
    {
        if (offset < 0 || byteCount <= 0)
            return false;

        var endOffset = (ulong)(uint)offset + (uint)byteCount - 1UL;
        return endOffset <= uint.MaxValue &&
               (ulong)address + endOffset <= uint.MaxValue;
    }
}
