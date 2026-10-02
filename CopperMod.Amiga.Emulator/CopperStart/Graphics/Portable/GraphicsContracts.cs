using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal enum GraphicsMemoryClass
{
    Any = 0,
    Chip = 1,
    Public = 2
}

/// <summary>Big-endian guest memory boundary. Implementations own address validation.</summary>
internal interface IGraphicsMemory
{
    bool TryReadByte(uint address, out byte value);
    bool TryReadWord(uint address, out ushort value);
    bool TryReadLong(uint address, out uint value);
    bool TryWriteByte(uint address, byte value);
    bool TryWriteWord(uint address, ushort value);
    bool TryWriteLong(uint address, uint value);
}

/// <summary>
/// Optional address-class capability for native display DMA.  The portable
/// graphics layer can validate a bitmap's guest envelope and word alignment
/// without this sidecar; a CopperStart/native memory adapter may additionally
/// state whether the complete plane span belongs to the chipset's display
/// fetch class.  Keeping this separate from <see cref="IGraphicsMemory"/>
/// preserves the CopperSharp68k memory-only layout and leaves provider-owned
/// (for example CyberGraphX) surfaces outside the portable OCS/ECS rule.
/// </summary>
internal interface IGraphicsDisplayMemory
{
    bool IsDisplayDmaRange(uint address, uint byteCount);
}

internal interface IGraphicsAllocatorBackend
{
    bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address);
    void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass);
}

/// <summary>
/// Optional allocator capability for the small public C strings attached to
/// resident native MonitorSpec nodes. Some host test allocators intentionally
/// admit only fixed-size graphics envelopes; those allocators leave the
/// optional xln_Name publication null rather than receiving an unexpected
/// variable-size request.
/// </summary>
internal interface IGraphicsMonitorNameAllocator
{
    bool SupportsMonitorNameAllocation { get; }
}

/// <summary>
/// Boundary for operations whose effects are visible through the custom-chip bus.
/// Implementations must preserve the emulator scheduler's event ordering.
/// </summary>
internal interface IGraphicsBlitterBackend
{
    void Own();
    void Disown();
    void Wait();
    void Submit(uint operationAddress);
}

/// <summary>
/// Optional extension for the V40 QBlit/QBSBlit queue boundary.  The legacy
/// <see cref="IGraphicsBlitterBackend.Submit(uint)"/> method remains the
/// compatibility fallback for hosts that only expose an operation address;
/// a native/CopperSharp68k backend can implement this sidecar to preserve the
/// queue class and the guest VBEAM value without widening the base contract.
/// </summary>
internal interface IGraphicsQueuedBlitterBackend
{
    void SubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync);
}

/// <summary>
/// Optional guest-linked queue boundary for the classic <c>bltnode.n</c>
/// field.  The portable FIFO owns the system link publication transaction;
/// address-only/provider queues deliberately omit this sidecar so a native
/// or CyberGraphX owner can retain the guest node unchanged.
/// </summary>
internal interface IGraphicsQueuedBlitterLinkBackend
{
    /// <summary>
    /// True only when this boundary owns publication of the guest
    /// <c>bltnode.n</c> link. Adapters may implement the memory-shaped
    /// overloads for compatibility while retaining an address-only provider
    /// queue when this capability is false.
    /// </summary>
    bool PublishesGuestLinks { get; }

    bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync);

    bool TrySubmitQueued(
        IGraphicsMemory memory,
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle);
}

/// <summary>
/// Optional status-aware ownership boundary used by host/native adapters.
/// The classic portable backend remains void-shaped, but an adapter that has
/// no scheduler/custom-chip owner can decline the vector so Kickstart or a
/// provider retains the call instead of observing a fabricated no-op.
/// </summary>
internal interface IGraphicsBlitterOwnershipStatusBackend
{
    bool TryOwn();
    bool TryDisown();
    bool TryWait();
}

/// <summary>
/// Optional status-aware queue boundary. A provider may decline QBlit/QBSBlit
/// when it cannot execute the guest node at the scheduler boundary.
/// </summary>
internal interface IGraphicsQueuedBlitterStatusBackend
{
    bool TrySubmitQueued(uint operationAddress, bool beamSynchronized, short beamSync);
}

/// <summary>
/// Optional cycle-aware blitter boundary.  CopperStart/native schedulers use
/// this sidecar when a queued node must retain the guest submission cycle and
/// when WaitBlit has to publish the cycle at which the custom-chip operation
/// actually completed.  Providers that do not own a scheduler continue to use
/// the untimed interfaces above.
/// </summary>
internal interface IGraphicsTimedBlitterBackend
{
    bool TryOwn(long cycle);
    bool TryDisown(long cycle);
    bool TryWait(long cycle, out long completionCycle);
    bool TrySubmitQueued(
        uint operationAddress,
        bool beamSynchronized,
        short beamSync,
        long cycle);
}

/// <summary>
/// Boundary for the graphics.library layer-ROM lock vectors. Layer clipping,
/// backing-store synchronization, and CyberGraphX ownership remain outside
/// this contract so a native/layers implementation can replace it later.
/// </summary>
internal interface IGraphicsLayerBackend
{
    void Lock(uint layerAddress);
    bool TryLock(uint layerAddress);
    void Unlock(uint layerAddress);
    bool SyncSuperBitMap(uint layerAddress);
    bool CopySuperBitMap(uint layerAddress);
}

/// <summary>
/// Atomic pixel snapshot boundary for provider-owned (for example RTG)
/// bitmaps participating in a Layers topology transaction.
/// </summary>
internal interface IGraphicsTransactionalBitMapBackend
{
    object? CaptureBitMap(uint bitMapAddress);

    bool RestoreBitMap(uint bitMapAddress, object snapshot);

    object? CaptureRectangle(
        uint bitMapAddress,
        int x,
        int y,
        int width,
        int height,
        int maximumSnapshotBytes,
        out int snapshotBytes);

    bool RestoreRectangle(uint bitMapAddress, object snapshot);

    /// <summary>Returns one provider snapshot after commit/cancel.</summary>
    void ReleaseSnapshot(object snapshot);

    /// <summary>Drops reset-scoped provider snapshot retention.</summary>
    void ResetSnapshotPool();

    bool CopyFromSnapshot(
        object sourceSnapshot,
        uint destinationBitMap,
        int sourceX,
        int sourceY,
        int destinationX,
        int destinationY,
        int width,
        int height,
        byte minterm,
        uint maskPlane);

    bool Backfill(
        uint rastPort,
        uint destinationBitMap,
        int destinationX,
        int destinationY,
        int width,
        int height);
}

internal readonly record struct GraphicsValidatedLayerEndpoint(
    uint RastPort,
    uint Layer,
    uint FirstClipRect,
    uint FirstSuperClipRect = 0);

/// <summary>
/// Single-call provider boundary for a Layers-validated raster operation.
/// Providers must consume these exact public and Super endpoint heads and must
/// not rediscover Layer.ClipRect/SuperClipRect through the RastPort. The
/// secondary endpoint is non-zero only for a dual-layer ClipBlit.
/// </summary>
internal interface IGraphicsValidatedLayerRasterBackend
{
    bool TryExecuteLayeredRaster(
        short graphicsLvo,
        ref global::CopperStart.Layers.LayersRegisterFrame registers,
        GraphicsValidatedLayerEndpoint primary,
        GraphicsValidatedLayerEndpoint secondary);
}

/// <summary>
/// Legacy per-operation layered raster boundary retained for non-Layers test
/// providers. It is not a topology authority: a CopperStart Layers-owned
/// RastPort must first pass through <see cref="IGraphicsValidatedLayerRasterBackend"/>.
/// </summary>
internal interface IGraphicsLayerRasterBackend
{
    bool TryDraw(uint rastPortAddress, short x, short y);
    bool TryText(uint rastPortAddress, uint textAddress, short length);

    /// <summary>
    /// Claims a layered <c>RPTAG_DrawBounds</c> query for a legacy provider.
    /// </summary>
    bool TryGetDrawBounds(uint rastPortAddress, uint rectangleAddress)
        => false;

    /// <summary>
    /// Claims a classic RectFill operation for a legacy provider.
    /// </summary>
    bool TryRectFill(
        uint rastPortAddress,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => false;

    /// <summary>Claims a classic DrawEllipse operation on a layered surface.</summary>
    bool TryDrawEllipse(
        uint rastPortAddress,
        short centerX,
        short centerY,
        short radiusX,
        short radiusY)
        => false;

    /// <summary>Claims a complete SetRast fill on a layered surface.</summary>
    bool TrySetRast(uint rastPortAddress, uint pen)
        => false;

    /// <summary>Reads a pixel through the provider-owned layer surface.</summary>
    bool TryReadPixel(
        uint rastPortAddress,
        short x,
        short y,
        out int color)
    {
        color = GraphicsRasterOperations.Failure;
        return false;
    }

    /// <summary>Writes a pixel through the provider-owned layer surface.</summary>
    bool TryWritePixel(uint rastPortAddress, short x, short y)
        => false;

    /// <summary>Claims a layered ScrollRaster operation.</summary>
    bool TryScrollRaster(
        uint rastPortAddress,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => false;

    /// <summary>Claims the backfill variant of a layered ScrollRaster operation.</summary>
    bool TryScrollRasterBF(
        uint rastPortAddress,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => false;

    /// <summary>Claims a layered ClearEOL operation.</summary>
    bool TryClearEOL(uint rastPortAddress)
        => false;

    /// <summary>Claims a layered ClearScreen operation.</summary>
    bool TryClearScreen(uint rastPortAddress)
        => false;

    /// <summary>Claims a layered EraseRect operation.</summary>
    bool TryEraseRect(
        uint rastPortAddress,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => false;

    /// <summary>Claims a layered PolyDraw point stream.</summary>
    bool TryPolyDraw(uint rastPortAddress, ushort count, uint pointsAddress)
        => false;

    /// <summary>Claims a layered AreaMove collector operation.</summary>
    bool TryAreaMove(uint rastPortAddress, short x, short y)
        => false;

    /// <summary>Claims a layered AreaDraw collector operation.</summary>
    bool TryAreaDraw(uint rastPortAddress, short x, short y)
        => false;

    /// <summary>Claims a layered AreaEllipse collector operation.</summary>
    bool TryAreaEllipse(
        uint rastPortAddress,
        short centerX,
        short centerY,
        short radiusX,
        short radiusY)
        => false;

    /// <summary>Claims completion of a layered area collector.</summary>
    bool TryAreaEnd(uint rastPortAddress)
        => false;

    /// <summary>Claims a layered Flood operation and publishes its BOOL result.</summary>
    bool TryFlood(
        uint rastPortAddress,
        uint mode,
        short x,
        short y,
        out int result)
    {
        result = GraphicsRasterOperations.Failure;
        return false;
    }

    /// <summary>Claims a V36 horizontal pixel-line read and publishes its count. Coordinates and width are UWORD values.</summary>
    bool TryReadPixelLine8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort width,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        result = GraphicsRasterOperations.Failure;
        return false;
    }

    /// <summary>Claims a V36 horizontal pixel-line write and publishes its count. Coordinates and width are UWORD values.</summary>
    bool TryWritePixelLine8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort width,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        result = GraphicsRasterOperations.Failure;
        return false;
    }

    /// <summary>Claims a V36 pixel-array read and publishes its count. Coordinates are UWORD values.</summary>
    bool TryReadPixelArray8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort xStop,
        ushort yStop,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        result = GraphicsRasterOperations.Failure;
        return false;
    }

    /// <summary>Claims a V36 pixel-array write and publishes its count. Coordinates are UWORD values.</summary>
    bool TryWritePixelArray8(
        uint rastPortAddress,
        ushort xStart,
        ushort yStart,
        ushort xStop,
        ushort yStop,
        uint arrayAddress,
        uint temporaryRastPortAddress,
        out int result)
    {
        result = GraphicsRasterOperations.Failure;
        return false;
    }

    /// <summary>Claims the V40 chunky-pixel write vector.</summary>
    bool TryWriteChunkyPixels(
        uint rastPortAddress,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint sourceArrayAddress,
        int bytesPerRow)
        => false;

    /// <summary>
    /// Claims a layer-aware ClipBlit between two RastPorts.  D6 carries the
    /// documented UBYTE minterm in the low register byte.
    /// </summary>
    bool TryClipBlit(
        uint sourceRastPortAddress,
        short sourceX,
        short sourceY,
        uint destinationRastPortAddress,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm)
        => false;

    /// <summary>
    /// Claims a bitmap-to-layered-RastPort blit and publishes BOOL.  D6
    /// carries the documented UBYTE minterm in the low register byte.
    /// </summary>
    bool TryBltBitMapRastPort(
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
        result = 0;
        return false;
    }

    /// <summary>
    /// Claims a masked bitmap-to-layered-RastPort blit.  D6 carries the
    /// documented UBYTE minterm in the low register byte.
    /// </summary>
    bool TryBltMaskBitMapRastPort(
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
        => false;

    /// <summary>Claims a layer-aware patterned blit.</summary>
    bool TryBltPattern(
        uint rastPortAddress,
        uint maskAddress,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        short bytesPerRow)
        => false;

    /// <summary>Claims a layer-aware template blit.</summary>
    bool TryBltTemplate(
        uint templateAddress,
        short sourceX,
        short sourceModulo,
        uint rastPortAddress,
        short destinationX,
        short destinationY,
        short width,
        short height)
        => false;
}

/// <summary>Boundary for View publication, beam queries, and frame synchronization.</summary>
internal interface IGraphicsDisplayBackend
{
    void PublishView(uint viewAddress);
    void WaitForTopOfFrame();
    void WaitForBeginningOfVerticalBlank(uint viewPortAddress);
    ushort GetBeamPosition();
}

/// <summary>
/// Optional active-machine video profile exposed by a display adapter.  The
/// portable database keeps explicit PAL/NTSC records, while zero-monitor-part
/// ModeIDs resolve through this boundary to the machine's jumper-selected
/// timing profile.  Hosts without a profile retain the classic PAL fallback.
/// </summary>
internal interface IGraphicsDisplayProfileBackend
{
    bool IsNtsc { get; }
}

/// <summary>
/// Optional chipset capability exposed beside the active video profile. ECS
/// SuperHires records remain in the portable database for unprofiled callers,
/// but an active OCS display must not advertise or select those modes.
/// </summary>
internal interface IGraphicsDisplayChipsetBackend
{
    bool SupportsEcsDisplay { get; }

    /// <summary>
    /// True only when the active planar display has both AGA Alice and Lisa
    /// capabilities. Hosts compiled against the earlier ECS-only capability
    /// contract retain the conservative false default.
    /// </summary>
    bool SupportsAgaDisplay => false;
}

/// <summary>
/// Optional hardware-facing GELS boundary.  The portable layer owns guest
/// list validation and animation state; a CopperStart/native backend owns
/// copper projection, collision callback dispatch, and raster erase/restore.
/// </summary>
internal interface IGraphicsGelsBackend
{
    void DrawGList(uint rastPort, uint viewPort);
    void DoCollision(uint rastPort);
    void RemIBob(uint bob, uint rastPort, uint viewPort);

    /// <summary>Receives a decoded GEL-to-GEL collision without executing the guest routine pointer.</summary>
    void DispatchCollision(uint firstVSprite, uint secondVSprite, uint routineAddress) { }

    /// <summary>Receives a decoded boundary collision without executing the guest routine pointer.</summary>
    void DispatchBoundary(uint vSprite, ushort boundaryFlags, uint routineAddress) { }
}

/// <summary>
/// Optional hardware-facing sprite projection.  The portable layer owns
/// SimpleSprite/ExtSprite guest state and validates image spans; a
/// CopperStart/native backend regenerates hardware position/control words and
/// publishes the image through the active viewport/DMA scheduler.
/// </summary>
internal interface IGraphicsSpriteBackend
{
    void MoveSprite(uint viewPort, uint sprite, short x, short y);
    void ChangeSprite(uint viewPort, uint sprite, uint imageData);

    /// <summary>
    /// Optional provider-owned GetExtSpriteA path.  The portable core still
    /// validates the ExtSprite envelope and tag list before this callback is
    /// reached; the provider owns the exact soft-sprite/scan-doubled display
    /// semantics and returns a sprite number when it claims the request.
    /// </summary>
    bool TryGetExtSprite(
        uint extSprite,
        uint tags,
        short requested,
        uint attachedSprite,
        bool softSprite,
        bool scanDoubled,
        out int spriteNumber)
    {
        spriteNumber = GraphicsRasterOperations.Failure;
        return false;
    }

    /// <summary>
    /// Optional provider-owned ChangeExtSpriteA path for display forms that
    /// are not representable by the portable OCS/ECS envelope.  The boolean
    /// result reports the operation result after the provider claims it;
    /// returning false leaves native Kickstart/provider ownership untouched.
    /// </summary>
    bool TryChangeExtSprite(
        uint viewPort,
        uint oldSprite,
        uint newSprite,
        uint tags,
        uint attachedSprite,
        bool softSprite,
        bool scanDoubled,
        out bool success)
    {
        success = false;
        return false;
    }
}

/// <summary>
/// Optional host/display boundary for palette calls whose visible effect is
/// outside guest ColorMap storage.  The portable core always updates the
/// guest map first; a native backend may omit this boundary entirely.
/// </summary>
internal interface IGraphicsPaletteBackend
{
    void LoadRgb4(uint viewPortAddress, uint colorsAddress, short count);
    void SetRgb4(uint viewPortAddress, short index, byte red, byte green, byte blue);
}

/// <summary>
/// Optional scheduler-aware palette boundary.  The portable ColorMap update
/// is committed first; a CopperStart/native implementation may then publish
/// the visible palette change at the guest call's canonical CPU cycle.
/// </summary>
internal interface IGraphicsTimedPaletteBackend
{
    void LoadRgb4(uint viewPortAddress, uint colorsAddress, short count, long cycle);
    void SetRgb4(uint viewPortAddress, short index, byte red, byte green, byte blue, long cycle);
}

/// <summary>
/// Optional full-precision palette publication boundary. RGB32 graphics calls
/// specify the most-significant eight bits of each ULONG component; AGA and
/// host-native displays can retain those bits rather than reducing every
/// update to OCS/ECS COLOR-register precision.
/// </summary>
internal interface IGraphicsRgb32PaletteBackend
{
    void SetRgb32(uint viewPortAddress, uint index, byte red, byte green, byte blue);
}

/// <summary>
/// Cycle-aware counterpart to <see cref="IGraphicsRgb32PaletteBackend"/>.
/// </summary>
internal interface IGraphicsTimedRgb32PaletteBackend
{
    void SetRgb32(uint viewPortAddress, uint index, byte red, byte green, byte blue, long cycle);
}

/// <summary>
/// Optional host boundary for display calls that must preserve the CPU's
/// current scheduler cycle. Portable tests can continue to use the untimed
/// display contract; CopperStart supplies this timed adapter explicitly.
/// </summary>
internal interface IGraphicsTimedDisplayBackend
{
    void PublishView(uint viewAddress, long cycle);
    long WaitForTopOfFrame(long cycle);
    long WaitForViewportBottom(uint viewPortAddress, long cycle);
    ushort GetBeamPosition(long cycle);
}

/// <summary>
/// Optional status-aware View publication boundary. A host without a native
/// display scheduler must decline rather than claim a validated View with a
/// fabricated no-op publication, leaving the vector for Kickstart/provider
/// code.
/// </summary>
internal interface IGraphicsViewPublicationStatusBackend
{
    /// <summary>
    /// Indicates whether a real publication owner is connected.  A status
    /// backend may be present as a routing adapter even when its provider has
    /// no callback; idempotent LoadView must not turn that unbound case into a
    /// fabricated success.
    /// </summary>
    bool CanPublishView => true;

    bool TryPublishView(uint viewAddress, long cycle);
}

/// <summary>
/// Optional status-aware frame-boundary wait. A host without a scheduler
/// owner declines `WaitTOF` so the native/provider implementation retains the
/// blocking vector and its guest cycle/register state.
/// </summary>
internal interface IGraphicsFrameWaitStatusBackend
{
    bool TryWaitForTopOfFrame(long cycle, out long nextCycle);
}

/// <summary>
/// Optional status-aware viewport-bottom wait. A validated viewport still
/// needs an actual vertical-blank owner before `WaitBOVP` can claim the call.
/// </summary>
internal interface IGraphicsViewportWaitStatusBackend
{
    bool TryWaitForViewportBottom(uint viewPortAddress, long cycle, out long nextCycle);
}

/// <summary>
/// Optional status-aware presentation/rebuild boundary for viewport changes.
/// Guest associations are only claimed when a host/native display owner can
/// observe the resulting scroll or bitmap transition.
/// </summary>
internal interface IGraphicsViewportPresentationStatusBackend
{
    bool CanScrollViewPort { get; }
    bool CanChangeViewPortBitMap(uint dbufInfoAddress);
}

/// <summary>
/// Optional status-aware beam-position boundary. A host display that cannot
/// provide a canonical beam sample must decline VBeamPos so a native/provider
/// implementation can own the vector instead of claiming a fabricated zero.
/// </summary>
internal interface IGraphicsBeamPositionStatusBackend
{
    bool TryGetBeamPosition(long cycle, out ushort beamPosition);
}

/// <summary>
/// Optional display boundary for ScrollVPort.  The portable operation only
/// validates guest RasInfo state; rebuilding or publishing copper state stays
/// on this explicit host/native boundary.
/// </summary>
internal interface IGraphicsViewportDisplayBackend
{
    void ScrollViewPort(uint viewPortAddress);
}

/// <summary>Timed ScrollVPort boundary used by CopperStart's scheduler path.</summary>
internal interface IGraphicsTimedViewportDisplayBackend
{
    long ScrollViewPort(uint viewPortAddress, long cycle);
}

/// <summary>
/// Host boundary for a validated ChangeVPBitMap transition. The portable
/// operation updates the guest RasInfo pointer first; the display backend then
/// schedules the presentation/rebuild without exposing host state to guest
/// memory code.
/// </summary>
internal interface IGraphicsViewportBitmapDisplayBackend
{
    void ChangeViewPortBitMap(uint viewPortAddress, uint bitMapAddress, uint dbufInfoAddress);
}

/// <summary>Timed ChangeVPBitMap boundary used by CopperStart.</summary>
internal interface IGraphicsTimedViewportBitmapDisplayBackend
{
    long ChangeViewPortBitMap(
        uint viewPortAddress,
        uint bitMapAddress,
        uint dbufInfoAddress,
        long cycle);
}

/// <summary>
/// Optional scheduler boundary for DBufInfo synchronization. The portable
/// raster operation supplies the previous and new bitmap identities only
/// after the guest RasInfo association has been committed; the host/native
/// side owns the frame timing and Exec message replies.
/// </summary>
internal interface IGraphicsDoubleBufferMessageBackend
{
    void ScheduleDoubleBufferMessages(
        uint viewPortAddress,
        uint previousBitMapAddress,
        uint bitMapAddress,
        uint dbufInfoAddress,
        long cycle);
}

/// <summary>
/// Optional teardown boundary for scheduled DBufInfo replies.  A display
/// scheduler must cancel pending frame-boundary messages before the portable
/// allocator releases the guest envelope, otherwise a later boundary could
/// dereference a stale message port.
/// </summary>
internal interface IGraphicsDoubleBufferMessageCancellationBackend
{
    void CancelDoubleBufferMessages(uint dbufInfoAddress);
}

/// <summary>
/// Optional display capability query used by AllocDBufInfo.  The portable
/// allocator owns the public DBufInfo envelope, while the active display
/// backend decides whether the selected monitor/mode can present a second
/// bitmap.  CyberGraphX may provide its own implementation.
/// </summary>
internal interface IGraphicsDoubleBufferCapabilityBackend
{
    bool SupportsDoubleBuffer(uint viewPortAddress);
}

/// <summary>
/// Boundary for MakeVPort/MrgCop's hardware-facing copper construction. The
/// portable core validates guest structure ranges first; a CopperStart or
/// native backend owns the actual copper instruction allocation and merge.
/// </summary>
internal interface IGraphicsCopperBackend
{
    void MakeViewPort(uint viewAddress, uint viewPortAddress);
    void MergeCopperLists(uint viewAddress);
}

/// <summary>
/// Optional status-aware MrgCop boundary.  Legacy/native adapters may retain
/// the void <see cref="IGraphicsCopperBackend.MergeCopperLists"/> contract;
/// adapters that can report allocation/merge outcomes implement this sidecar.
/// </summary>
internal interface IGraphicsCopperMergeStatusBackend
{
    int MergeCopperListsStatus(uint viewAddress);
}

/// <summary>Optional status-aware MakeVPort boundary for MVP_* results.</summary>
internal interface IGraphicsCopperBuildStatusBackend
{
    int MakeViewPortStatus(uint viewAddress, uint viewPortAddress);
}

/// <summary>
/// Explicit ownership seam for resource-bearing MakeVPort calls. A native
/// overlay may only claim a non-NULL RasInfo viewport when the display bridge
/// can build and own its private CPR/raw copper resources; CopperSharp68k and
/// CopperScreen provide their own implementations at this boundary.
/// </summary>
internal interface IGraphicsCopperBuildResourceStatusBackend
{
    bool HasExplicitMakeVPortOwner { get; }
}

/// <summary>
/// Optional owner of the hardware-facing <c>struct cprlist</c> allocation
/// produced by MrgCop.  The portable core validates the public envelope, but
/// deliberately does not infer private allocation sizes or custom-chip state.
/// </summary>
internal interface IGraphicsCopperResourceBackend
{
    void FreeVPortCopLists(uint viewPortAddress);
    void FreeCprList(uint cprListAddress);
}

/// <summary>
/// Optional result-bearing copper teardown owner. A native/CopperSharp68k
/// implementation can decline a structurally valid cprlist when the wrapper
/// or backing copper allocation is not owned by that graphics instance. The
/// legacy void boundary remains available for hosts whose ownership is already
/// established by the callback itself.
/// </summary>
internal interface IGraphicsCopperResourceStatusBackend
{
    bool TryFreeVPortCopLists(uint viewPortAddress);
    bool TryFreeCprList(uint cprListAddress);
}

/// <summary>
/// Marks a status-aware display bridge whose copper teardown callbacks are
/// explicit ownership boundaries rather than compatibility wrappers around
/// legacy void callbacks. Native overlays use these sidecars before claiming
/// non-null hardware-facing resources.
/// </summary>
internal interface IGraphicsCopperNativeResourceStatusBackend
{
    bool HasExplicitVPortCopListOwner { get; }
    bool HasExplicitCprListOwner { get; }
}

/// <summary>
/// Optional display/copper boundary for CalcIVG.  The portable layer guards
/// the guest View/ViewPort envelopes first; a CopperStart/native implementation
/// then counts the scan lines required by the actual copper instruction stream.
/// No fallback value is claimed when a backend is absent.
/// </summary>
internal interface IGraphicsCalcIvgBackend
{
    bool TryCalcIvg(uint viewAddress, uint viewPortAddress, out ushort scanLines);
}

/// <summary>
/// Optional chipset-capability boundary for SetChipRev.  The portable
/// graphics layer does not infer custom-chip revisions; a CopperStart/native
/// provider returns the actual bits enabled in GfxBase->ChipRevBits0.
/// </summary>
internal interface IGraphicsChipRevisionBackend
{
    bool TrySetChipRev(uint requestedBits, out uint actualBits);
}

internal static class GraphicsChipRevision
{
    internal const uint BigBlits = 1u << 0;
    internal const uint HrAgnus = 1u << 0;
    internal const uint HrDenise = 1u << 1;
    internal const uint AaAlice = 1u << 2;
    internal const uint AaLisa = 1u << 3;
    internal const uint AaMLisa = 1u << 4;

    internal const uint SetA = HrAgnus;
    internal const uint SetEcs = HrAgnus | HrDenise;
    internal const uint SetAa = AaAlice | AaLisa | SetEcs;
    internal const uint SetBest = 0xFFFF_FFFFu;

    internal static uint RequestedBits(uint requestedBits, uint availableBits)
        => (requestedBits == SetBest ? availableBits : requestedBits) & availableBits;
}

internal static class GraphicsViewportStatuses
{
    internal const int Ok = 0;
    internal const int NoMemory = 1;
    internal const int NoViewPortExtra = 2;
    internal const int NoDisplayInstructions = 3;
    internal const int NoDisplay = 4;
    internal const int OffBottom = 5;
}

/// <summary>
/// Explicit source for a ViewPort's private 32-bit display database handle.
/// The handle is intentionally not stored in the public ViewPort layout.
/// </summary>
internal interface IGraphicsViewModeBackend
{
    bool TryGetModeId(uint viewPortAddress, out uint modeId);
}

internal static class GraphicsModeIds
{
    internal const uint Invalid = 0xFFFF_FFFFu;
    // graphics/modeid.h: the zero monitor part denotes the machine's
    // jumper-selected default monitor.  The portable database exposes the
    // default records explicitly; host/native display adapters may still
    // resolve the same keys to their active PAL/NTSC profile.
    internal const uint DefaultMonitor = 0x0000_0000u;
    internal const uint NtscMonitor = 0x0001_1000u;
    internal const uint PalMonitor = 0x0002_1000u;
    // Foreign/extended monitor families from graphics/modeid.h remain
    // provider-owned. Naming their canonical monitor parts makes the
    // handoff explicit without adding them to the portable OCS/ECS database.
    internal const uint VgaMonitor = 0x0003_1000u;
    internal const uint A2024Monitor = 0x0004_1000u;
    internal const uint ProtoMonitor = 0x0005_1000u;
    internal const uint Euro72Monitor = 0x0006_1000u;
    internal const uint Euro36Monitor = 0x0007_1000u;
    internal const uint Super72Monitor = 0x0008_1000u;
    internal const uint DblNtscMonitor = 0x0009_1000u;
    internal const uint DblPalMonitor = 0x000A_1000u;
    internal const ushort GenlockVideoMode = 0x0002;
    internal const ushort HiresMode = 0x8000;
    internal const ushort InterlaceMode = 0x0004;
    internal const ushort DoubleScanMode = 0x0008;
    // Feature bit carried by the documented SUPER_KEY composite.  Callers
    // should use SuperHiresKey/SuperHiresLaceKey for complete ModeIDs.
    internal const ushort SuperHiresMode = 0x0020;
    internal const ushort PlayfieldBitAssignment = 0x0040;
    internal const ushort ExtraHalfBriteMode = 0x0080;
    internal const ushort GenlockAudioMode = 0x0100;
    internal const ushort DualPlayfieldMode = 0x0400;
    internal const ushort HamMode = 0x0800;
    internal const ushort ExtendedMode = 0x1000;
    internal const ushort ViewPortHidden = 0x2000;
    internal const ushort SpritesMode = 0x4000;

    internal const ushort LoresKey = 0x0000;
    internal const ushort HiresKey = HiresMode;
    // graphics/modeid.h: SUPER_KEY = HIRES_KEY | 0x20 (0x00008020).
    internal const ushort SuperHiresKey = HiresMode | SuperHiresMode;
    internal const ushort HamKey = HamMode;
    internal const ushort LoresLaceKey = InterlaceMode;
    internal const ushort HiresLaceKey = HiresMode | InterlaceMode;
    internal const ushort SuperHiresLaceKey = HiresMode | SuperHiresMode | InterlaceMode;
    internal const ushort HamLaceKey = HamMode | InterlaceMode;
    internal const ushort LoresDualPlayfieldKey = DualPlayfieldMode;
    internal const ushort HiresDualPlayfieldKey = HiresMode | DualPlayfieldMode;
    internal const ushort SuperHiresDualPlayfieldKey = HiresMode | SuperHiresMode | DualPlayfieldMode;
    internal const ushort LoresDualPlayfieldLaceKey = DualPlayfieldMode | InterlaceMode;
    internal const ushort HiresDualPlayfieldLaceKey = HiresMode | DualPlayfieldMode | InterlaceMode;
    internal const ushort SuperHiresDualPlayfieldLaceKey = HiresMode | SuperHiresMode | DualPlayfieldMode | InterlaceMode;
    internal const ushort LoresDualPlayfieldTwoKey = DualPlayfieldMode | PlayfieldBitAssignment;
    internal const ushort HiresDualPlayfieldTwoKey = HiresMode | DualPlayfieldMode | PlayfieldBitAssignment;
    internal const ushort SuperHiresDualPlayfieldTwoKey = HiresMode | SuperHiresMode | DualPlayfieldMode | PlayfieldBitAssignment;
    internal const ushort LoresDualPlayfieldTwoLaceKey = DualPlayfieldMode | PlayfieldBitAssignment | InterlaceMode;
    internal const ushort HiresDualPlayfieldTwoLaceKey = HiresMode | DualPlayfieldMode | PlayfieldBitAssignment | InterlaceMode;
    internal const ushort SuperHiresDualPlayfieldTwoLaceKey = HiresMode | SuperHiresMode | DualPlayfieldMode | PlayfieldBitAssignment | InterlaceMode;
    internal const ushort ExtraHalfBriteKey = ExtraHalfBriteMode;
    internal const ushort ExtraHalfBriteLaceKey = ExtraHalfBriteMode | InterlaceMode;

    // V40 scan-doubled keys are part of the native monitor query namespace,
    // even though this portable OCS/ECS database does not synthesize their
    // display records.  Keeping them here lets ModeNotAvailable report the
    // documented no-chip result without claiming unrelated provider modes.
    internal const ushort LoresScanDoubledKey = DoubleScanMode;
    internal const ushort LoresHamScanDoubledKey = HamMode | DoubleScanMode;
    internal const ushort LoresEhbScanDoubledKey = ExtraHalfBriteMode | DoubleScanMode;
    internal const ushort HiresHamScanDoubledKey = HiresMode | HamMode | DoubleScanMode;

    internal static bool TryGetNativeModeId(ushort viewModes, bool ntsc, out uint modeId)
    {
        // View.Modes also carries run-time controls (sprite enable, genlock,
        // and viewport hiding) which are not part of a ModeID key.  Strip
        // those controls, then accept only the canonical OCS/ECS composite
        // keys from graphics/modeid.h.  Double-scan, extended modes, and RTG
        // monitor keys remain separate provider paths; SuperHires is an ECS
        // native mode and is represented in the portable display database.
        const ushort ignoredControls =
            GenlockVideoMode | GenlockAudioMode | ViewPortHidden | SpritesMode;
        var modeKey = (ushort)(viewModes & ~ignoredControls);
        if (!IsNativeOcsEcsKey(modeKey))
        {
            modeId = Invalid;
            return false;
        }

        modeId = (ntsc ? NtscMonitor : PalMonitor) | modeKey;
        return true;
    }

    internal static bool IsNativeOcsEcsKey(ushort modeKey)
        => modeKey == LoresKey ||
           modeKey == HiresKey ||
           modeKey == SuperHiresKey ||
           modeKey == HamKey ||
           modeKey == LoresLaceKey ||
           modeKey == HiresLaceKey ||
           modeKey == SuperHiresLaceKey ||
           modeKey == HamLaceKey ||
           modeKey == LoresDualPlayfieldKey ||
           modeKey == HiresDualPlayfieldKey ||
           modeKey == SuperHiresDualPlayfieldKey ||
           modeKey == LoresDualPlayfieldLaceKey ||
           modeKey == HiresDualPlayfieldLaceKey ||
           modeKey == SuperHiresDualPlayfieldLaceKey ||
           modeKey == LoresDualPlayfieldTwoKey ||
           modeKey == HiresDualPlayfieldTwoKey ||
           modeKey == SuperHiresDualPlayfieldTwoKey ||
           modeKey == LoresDualPlayfieldTwoLaceKey ||
           modeKey == HiresDualPlayfieldTwoLaceKey ||
           modeKey == SuperHiresDualPlayfieldTwoLaceKey ||
           modeKey == ExtraHalfBriteKey ||
           modeKey == ExtraHalfBriteLaceKey;

    internal static bool IsNativeModeQueryKey(ushort modeKey)
        => IsNativeOcsEcsKey(modeKey) ||
           modeKey == LoresScanDoubledKey ||
           modeKey == LoresHamScanDoubledKey ||
           modeKey == LoresEhbScanDoubledKey ||
           modeKey == HiresHamScanDoubledKey;
}

internal readonly struct GraphicsFontMetrics
{
    internal GraphicsFontMetrics(
        ushort height,
        ushort width,
        ushort baseline,
        ushort spacing,
        byte style = 0,
        ushort boldSmear = 1,
        byte flags = 0,
        byte loChar = 0,
        byte hiChar = byte.MaxValue,
        bool hasCharSpace = false,
        bool hasCharKern = false)
    {
        Height = height;
        Width = width;
        Baseline = baseline;
        Spacing = spacing;
        Style = style;
        BoldSmear = boldSmear;
        Flags = flags;
        LoChar = loChar;
        HiChar = hiChar;
        HasCharSpace = hasCharSpace;
        HasCharKern = hasCharKern;
    }

    internal ushort Height { get; }
    internal ushort Width { get; }
    internal ushort Baseline { get; }
    internal ushort Spacing { get; }
    internal byte Style { get; }
    internal ushort BoldSmear { get; }
    internal byte Flags { get; }
    internal byte LoChar { get; }
    internal byte HiChar { get; }
    internal bool HasCharSpace { get; }
    internal bool HasCharKern { get; }
}

internal readonly struct GraphicsGlyph
{
    internal GraphicsGlyph(byte width, byte height, byte advance, ulong rows)
        : this(width, height, advance, 0, rows)
    {
    }

    internal GraphicsGlyph(
        ushort width,
        ushort height,
        int advance,
        short kerning,
        uint charData,
        ushort modulo,
        ushort bitOffset)
    {
        Width = width;
        Height = height;
        Advance = advance;
        Kerning = kerning;
        Rows = 0;
        CharData = charData;
        Modulo = modulo;
        BitOffset = bitOffset;
    }

    private GraphicsGlyph(ushort width, ushort height, int advance, short kerning, ulong rows)
    {
        Width = width;
        Height = height;
        Advance = advance;
        Kerning = kerning;
        Rows = rows;
        CharData = 0;
        Modulo = 0;
        BitOffset = 0;
    }

    internal ushort Width { get; }
    internal ushort Height { get; }
    internal int Advance { get; }
    internal short Kerning { get; }
    internal ulong Rows { get; }
    internal uint CharData { get; }
    internal ushort Modulo { get; }
    internal ushort BitOffset { get; }

    internal bool IsSet(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height &&
            (Rows & (1UL << (63 - ((y * 8) + x)))) != 0;

    internal bool TryIsSet(IGraphicsMemory memory, int x, int y, out bool set)
    {
        set = false;
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return false;

        if (CharData == 0)
        {
            set = IsSet(x, y);
            return true;
        }

        var bit = (uint)BitOffset + (uint)x;
        var rowOffset = (ulong)(uint)y * Modulo + (bit >> 3);
        if (rowOffset > uint.MaxValue - CharData ||
            !memory.TryReadByte(CharData + (uint)rowOffset, out var row))
        {
            return false;
        }

        set = (row & (0x80 >> (int)(bit & 7))) != 0;
        return true;
    }
}

/// <summary>
/// The guest-side color composition metadata carried after a ColorTextFont's
/// TextFont prefix.  Source planes use the same CharLoc/Modulo geometry as a
/// normal glyph; PlanePick/PlaneOnOff then map those source bits to the
/// destination planar color index.
/// </summary>
internal readonly struct GraphicsColorFontInfo
{
    private const ushort ColorFontFlagMapColor = 1 << 0;
    private const byte NoForegroundRemap = byte.MaxValue;

    internal GraphicsColorFontInfo(
        ushort flags,
        byte depth,
        byte foregroundColor,
        byte lowColor,
        byte highColor,
        byte planePick,
        byte planeOnOff,
        uint[] planeData)
    {
        Flags = flags;
        Depth = depth;
        ForegroundColor = foregroundColor;
        LowColor = lowColor;
        HighColor = highColor;
        PlanePick = planePick;
        PlaneOnOff = planeOnOff;
        PlaneData = planeData;
    }

    internal ushort Flags { get; }
    internal byte Depth { get; }
    internal byte ForegroundColor { get; }
    internal byte LowColor { get; }
    internal byte HighColor { get; }
    internal byte PlanePick { get; }
    internal byte PlaneOnOff { get; }
    internal uint[] PlaneData { get; }

    internal bool TryGetPixel(
        IGraphicsMemory memory,
        GraphicsGlyph glyph,
        int x,
        int y,
        byte foregroundPen,
        out byte color,
        out bool source)
    {
        color = 0;
        source = false;
        if (x < 0 || y < 0 || x >= glyph.Width || y >= glyph.Height ||
            Depth > ColorTextFontMaximumDepth || PlaneData.Length < Depth)
        {
            return false;
        }

        var sourceColor = 0;
        for (var plane = 0; plane < Depth; plane++)
        {
            var planeData = PlaneData[plane];
            if (planeData == 0)
                return false;

            var bit = (uint)glyph.BitOffset + (uint)x;
            var rowOffset = (ulong)(uint)y * glyph.Modulo + (bit >> 3);
            if (rowOffset > uint.MaxValue - planeData ||
                !memory.TryReadByte(planeData + (uint)rowOffset, out var row))
            {
                return false;
            }

            if ((row & (0x80 >> (int)(bit & 7))) != 0)
                sourceColor |= 1 << plane;
        }

        // ColorTextFont follows the Image PlanePick/PlaneOnOff contract:
        // source plane zero is assigned to the first set destination bit,
        // and unpicked destination planes receive their constant OnOff bit.
        var mapped = 0;
        var sourcePlane = 0;
        for (var destinationPlane = 0;
             destinationPlane < ColorTextFontMaximumDepth;
             destinationPlane++)
        {
            var destinationBit = 1 << destinationPlane;
            var set = (PlanePick & destinationBit) != 0
                ? sourcePlane < Depth && (sourceColor & (1 << sourcePlane++)) != 0
                : (PlaneOnOff & destinationBit) != 0;
            if (set)
                mapped |= destinationBit;
        }

        // CTF_MAPCOLOR is the low bit of ctf_Flags.  It is distinct from the
        // font's colour-style choice in the grey-font case: only a valid
        // mapped foreground color in the declared Low..High range may be
        // replaced by the RastPort APen.  A zero source index remains the
        // transparent background of a planar ColorTextFont strike.
        var mapForeground =
            (Flags & ColorFontFlagMapColor) != 0 &&
            ForegroundColor != NoForegroundRemap &&
            ForegroundColor >= LowColor &&
            ForegroundColor <= HighColor;
        source = sourceColor != 0;
        color = source && mapForeground && sourceColor == ForegroundColor
            ? foregroundPen
            : (byte)mapped;
        return true;
    }

    internal bool TryPreflightGlyph(IGraphicsMemory memory, GraphicsGlyph glyph)
    {
        if (Depth > ColorTextFontMaximumDepth || PlaneData.Length < Depth)
            return false;
        if (glyph.Width == 0 || glyph.Height == 0)
            return true;

        var bytesPerRow = ((ulong)(glyph.BitOffset & 7) + glyph.Width + 7UL) / 8UL;
        if (bytesPerRow == 0 || bytesPerRow > int.MaxValue)
            return false;

        for (var plane = 0; plane < Depth; plane++)
        {
            var planeData = PlaneData[plane];
            if (planeData == 0)
                return false;
            for (var row = 0u; row < glyph.Height; row++)
            {
                var rowOffset = (ulong)row * glyph.Modulo + (uint)(glyph.BitOffset >> 3);
                if (rowOffset > uint.MaxValue - planeData)
                    return false;
                for (var offset = 0u; offset < (uint)bytesPerRow; offset++)
                {
                    if (!memory.TryReadByte(planeData + (uint)rowOffset + offset, out _))
                        return false;
                }
            }
        }

        return true;
    }

    private const int ColorTextFontMaximumDepth = 8;
}

/// <summary>Font lookup boundary; implementations read TextFont data from guest memory.</summary>
internal interface IGraphicsFontBackend
{
    bool TryGetMetrics(uint fontAddress, out GraphicsFontMetrics metrics);
    bool TryGetGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph);

    // TextLength/TextExtent/TextFit/FontExtent consume only the metric tables
    // and TextFont scalar fields.  Keep that query envelope separate from
    // Text rendering and lifecycle admission: a resident TextFont can be
    // measurable even when its strike storage is not mapped (or tf_YSize is
    // zero), because the resident metric vectors do not dereference glyph
    // rows.  Lightweight providers may fall back to the stricter rendering
    // path when they do not expose a separate query decoder.
    bool TryGetMetricMetrics(uint fontAddress, out GraphicsFontMetrics metrics)
    {
        return TryGetMetrics(fontAddress, out metrics);
    }

    bool TryGetMetricGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph)
    {
        return TryGetGlyph(fontAddress, character, out glyph);
    }

    bool TryGetMetricDefaultGlyph(uint fontAddress, out GraphicsGlyph glyph)
    {
        return TryGetDefaultGlyph(fontAddress, out glyph);
    }

    // AskSoftStyle and SetSoftStyle consume only TextFont->tf_Style.  Keep
    // that scalar read separate from the complete metric/glyph envelope so a
    // sparse resident TextFont remains available to the native/provider path
    // for style queries without becoming claimable for Text rendering.
    bool TryGetStyle(uint fontAddress, out byte style)
    {
        style = 0;
        if (!TryGetMetrics(fontAddress, out var metrics))
            return false;

        style = metrics.Style;
        return true;
    }

    // SetFont(NULL) selects GfxBase->DefaultFont.  Lightweight providers may
    // omit a default-font owner and leave the call available to native/provider
    // fallback; the memory-backed graphics backend supplies this from its guest
    // font-list or compatibility callback.
    bool TryGetDefaultFont(out uint fontAddress)
    {
        fontAddress = 0;
        return false;
    }

    // TextFont tables carry one extra entry after HiChar for characters that
    // fall outside the declared range.  Lightweight host providers may not
    // expose that optional slot; the memory-backed provider does so when it
    // can decode the guest table directly.
    bool TryGetDefaultGlyph(uint fontAddress, out GraphicsGlyph glyph)
    {
        glyph = default;
        return false;
    }
}

/// <summary>
/// Optional standard-planar ColorTextFont decoder.  Backends that do not own
/// the classic multi-plane color-font ABI simply omit this interface; the
/// portable Text vector then declines before mutating guest state so a native
/// or provider implementation can claim it.
/// </summary>
internal interface IGraphicsColorFontBackend
{
    bool TryGetColorFont(uint fontAddress, out GraphicsColorFontInfo colorFont);
    bool TryGetColorGlyph(uint fontAddress, byte character, out GraphicsGlyph glyph);
}

/// <summary>
/// Pure guest-memory boundary for the V36 <c>WeighTAMatch</c> metric.  It is
/// separate from glyph rendering so a future native 68k font provider can
/// implement the matcher without exposing host font objects to the ROM path.
/// </summary>
internal interface IGraphicsFontMatchBackend
{
    bool TryWeighTAMatch(
        uint requestedTextAttr,
        uint targetTextAttr,
        uint targetTags,
        out short weight);
}

/// <summary>
/// Guest-facing font ownership boundary. Implementations own system-font-list
/// state and any allocator-backed TextFontExtension objects; portable code only
/// supplies the guest addresses and preserves the classic ABI layout.
/// </summary>
internal interface IGraphicsFontLifecycleBackend
{
    bool TryOpen(uint textAttrAddress, out uint fontAddress);
    bool TryClose(uint fontAddress);
    bool TryAdd(uint fontAddress);
    bool TryRemove(uint fontAddress);
    bool TryExtend(uint fontAddress, uint fontTags);
    bool TryStrip(uint fontAddress);
}

/// <summary>
/// Guest-side publication boundary for the graphics.library TextFonts list.
/// The portable font matcher keeps its own lookup state, while this optional
/// backend mirrors AddFont/RemFont/DefaultFont into a native-compatible
/// <c>GfxBase</c> list.  Keeping the list mutation behind a separate boundary
/// lets a future native 68k resident use the same TextFont node layout.
/// </summary>
internal interface IGraphicsFontListBackend
{
    /// <summary>
    /// Guest address of the GfxBase whose TextFonts list this backend mutates.
    /// Native-overlay installation may rebind the same memory-only backend to
    /// the discovered library base before lifecycle vectors are claimed.
    /// </summary>
    uint BaseAddress { get; }
    bool TryRebind(uint gfxBase);
    bool TryEnumerate(out IReadOnlyList<uint> fonts);
    bool TryGetDefaultFont(out uint fontAddress);
    bool TryAdd(uint fontAddress);
    bool TryRemove(uint fontAddress);
    bool SetDefaultFont(uint fontAddress);
}

/// <summary>
/// Optional native-overlay publication planning for a guest TextFonts list.
/// The list owner resolves the exact link/flag fields that a lifecycle
/// operation will rewrite without performing any writes.  A mapped native
/// adapter can then apply its writable-range admission before the portable
/// lifecycle enters its transactional teardown.
/// </summary>
internal interface IGraphicsNativeFontListPublicationBackend
{
    bool TryGetAddFontPublicationSpans(
        uint fontAddress,
        out IReadOnlyList<(uint Address, int ByteCount)> spans);
    bool TryGetRemoveFontPublicationSpans(
        uint fontAddress,
        out IReadOnlyList<(uint Address, int ByteCount)> spans);
}

internal readonly struct GraphicsTextAttributes
{
    internal GraphicsTextAttributes(uint name, ushort ySize, byte style, byte flags)
    {
        Name = name;
        YSize = ySize;
        Style = style;
        Flags = flags;
    }

    internal uint Name { get; }
    internal ushort YSize { get; }
    internal byte Style { get; }
    internal byte Flags { get; }
}
