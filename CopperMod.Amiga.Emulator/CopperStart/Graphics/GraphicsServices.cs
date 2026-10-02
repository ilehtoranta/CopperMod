using System;
using System.Collections.Generic;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.CopperStart.Graphics;

internal interface IGraphicsLayerGatewayBackend
{
    bool TryInvokeGraphicsRaster(
        M68kCpuState state,
        GraphicsLvo lvo,
        out M68kHostGatewayResult result);

    bool TryInvokeGraphicsCompanion(
        M68kCpuState state,
        GraphicsLvo lvo,
        out M68kHostGatewayResult result);
}

/// <summary>
/// CopperStart's graphics.library contribution point.
///
/// This intentionally owns no broad fallback table.  ROM mode will register
/// only LVOs added here after their native behavior has been migrated and
/// tested; all remaining graphics.library vectors continue through Kickstart.
/// </summary>
internal sealed class GraphicsServices : IDisposable
{
    private readonly CopperStartGraphicsContext _context;
    private readonly CopperStartGraphicsDisplayAdapter _displayAdapter;
    private readonly GraphicsLibraryCore _core;
    private readonly CopperStartGraphicsRegisterAdapter _portableAdapter;
    private readonly IGraphicsLayerGatewayBackend? _layerGateway;
    private readonly bool _hasLayerLockProvider;
    private HostLibraryGatewayRegistry? _romOverlayRegistry;
    private uint _romOverlayBase;
    private IGraphicsFontListBackend? _reboundFontList;
    private uint _reboundFontListPreviousBase;
    private bool _reboundGraphicsBase;
    private uint _reboundGraphicsBasePreviousBase;

    /// <summary>
    /// Vectors whose portable implementation is a bounded guest-memory
    /// operation.  ROM mode may opt into these individually; every other
    /// vector remains owned by the native Kickstart image until it has its own
    /// native-parity proof.
    /// </summary>
    internal static IReadOnlyList<GraphicsLvo> RomSafeOverlayLvos { get; } =
    [
        GraphicsLvo.InitRastPort,
        GraphicsLvo.InitVPort,
        GraphicsLvo.InitView,
        GraphicsLvo.InitBitMap,
        GraphicsLvo.InitArea,
        GraphicsLvo.InitTmpRas,
        GraphicsLvo.DoCollision,
        GraphicsLvo.DrawGList,
        GraphicsLvo.LockLayerRom,
        GraphicsLvo.AttemptLockLayerRom,
        GraphicsLvo.UnlockLayerRom,
        GraphicsLvo.SetAPen,
        GraphicsLvo.SetBPen,
        GraphicsLvo.SetDrMd,
        GraphicsLvo.SetABPenDrMd,
        GraphicsLvo.SetWriteMask,
        GraphicsLvo.SetMaxPen,
        GraphicsLvo.Move,
        GraphicsLvo.Draw,
        GraphicsLvo.PolyDraw,
        GraphicsLvo.RectFill,
        GraphicsLvo.SetRast,
        GraphicsLvo.ClearEOL,
        GraphicsLvo.ClearScreen,
        GraphicsLvo.EraseRect,
        GraphicsLvo.DrawEllipse,
        GraphicsLvo.ScrollRaster,
        GraphicsLvo.ScrollRasterBF,
        GraphicsLvo.AreaMove,
        GraphicsLvo.AreaDraw,
        GraphicsLvo.AreaEllipse,
        GraphicsLvo.AreaEnd,
        GraphicsLvo.Flood,
        GraphicsLvo.GetAPen,
        GraphicsLvo.GetBPen,
        GraphicsLvo.GetDrMd,
        GraphicsLvo.GetOutlinePen,
        GraphicsLvo.SetOutlinePen,
        GraphicsLvo.Text,
        GraphicsLvo.TextLength,
        GraphicsLvo.TextExtent,
        GraphicsLvo.FontExtent,
        GraphicsLvo.TextFit,
        GraphicsLvo.WeighTAMatch,
        GraphicsLvo.AskFont,
        GraphicsLvo.OpenFont,
        GraphicsLvo.CloseFont,
        GraphicsLvo.SetFont,
        GraphicsLvo.AddFont,
        GraphicsLvo.RemFont,
        GraphicsLvo.ExtendFont,
        GraphicsLvo.StripFont,
        GraphicsLvo.AllocRaster,
        GraphicsLvo.FreeRaster,
        GraphicsLvo.AskSoftStyle,
        GraphicsLvo.SetSoftStyle,
        GraphicsLvo.SetRPAttrsA,
        GraphicsLvo.GetRPAttrsA,
        GraphicsLvo.OwnBlitter,
        GraphicsLvo.DisownBlitter,
        GraphicsLvo.WaitBlit,
        GraphicsLvo.QBlit,
        GraphicsLvo.QBSBlit,
        GraphicsLvo.SyncSBitMap,
        GraphicsLvo.CopySBitMap,
        GraphicsLvo.BltBitMap,
        GraphicsLvo.ClipBlit,
        GraphicsLvo.BltBitMapRastPort,
        GraphicsLvo.BltMaskBitMapRastPort,
        GraphicsLvo.BltClear,
        GraphicsLvo.BltPattern,
        GraphicsLvo.BltTemplate,
        GraphicsLvo.ReadPixel,
        GraphicsLvo.WritePixel,
        GraphicsLvo.ReadPixelLine8,
        GraphicsLvo.WritePixelLine8,
        GraphicsLvo.ReadPixelArray8,
        GraphicsLvo.WritePixelArray8,
        GraphicsLvo.WriteChunkyPixels,
        GraphicsLvo.GetColorMap,
        GraphicsLvo.FreeColorMap,
        GraphicsLvo.AttachPalExtra,
        GraphicsLvo.GetRGB4,
        GraphicsLvo.SetRGB4CM,
        GraphicsLvo.SetRGB32CM,
        GraphicsLvo.ObtainPen,
        GraphicsLvo.ObtainBestPenA,
        GraphicsLvo.ReleasePen,
        GraphicsLvo.FindColor,
        GraphicsLvo.GetRGB32,
        GraphicsLvo.VideoControl,
        GraphicsLvo.LoadRGB4,
        GraphicsLvo.LoadRGB32,
        GraphicsLvo.SetRGB4,
        GraphicsLvo.SetRGB32,
        GraphicsLvo.AllocBitMap,
        GraphicsLvo.FreeBitMap,
        GraphicsLvo.GetBitMapAttr,
        GraphicsLvo.BitMapScale,
        GraphicsLvo.GfxNew,
        GraphicsLvo.GfxFree,
        GraphicsLvo.GfxAssociate,
        GraphicsLvo.GfxLookUp,
        GraphicsLvo.GetSprite,
        GraphicsLvo.FreeSprite,
        GraphicsLvo.MoveSprite,
        GraphicsLvo.ChangeSprite,
        GraphicsLvo.GetExtSpriteA,
        GraphicsLvo.AllocSpriteDataA,
        GraphicsLvo.ChangeExtSpriteA,
        GraphicsLvo.FreeSpriteData,
        GraphicsLvo.InitGels,
        GraphicsLvo.SetCollision,
        GraphicsLvo.InitMasks,
        GraphicsLvo.AddVSprite,
        GraphicsLvo.RemVSprite,
        GraphicsLvo.SortGList,
        GraphicsLvo.AddBob,
        GraphicsLvo.RemIBob,
        GraphicsLvo.AddAnimOb,
        GraphicsLvo.Animate,
        GraphicsLvo.GetGBuffers,
        GraphicsLvo.InitGMasks,
        GraphicsLvo.FreeGBuffers,
        GraphicsLvo.ScalerDiv,
        GraphicsLvo.LoadView,
        GraphicsLvo.CalcIVG,
        GraphicsLvo.SetChipRev,
        GraphicsLvo.OpenMonitor,
        GraphicsLvo.CloseMonitor,
        GraphicsLvo.FindDisplayInfo,
        GraphicsLvo.NextDisplayInfo,
        GraphicsLvo.ModeNotAvailable,
        GraphicsLvo.GetDisplayInfoData,
        GraphicsLvo.BestModeIDA,
        GraphicsLvo.CoerceMode,
        GraphicsLvo.GetVPModeID,
        GraphicsLvo.WaitTOF,
        GraphicsLvo.WaitBOVP,
        GraphicsLvo.VBeamPos,
        GraphicsLvo.ScrollVPort,
        GraphicsLvo.ChangeVPBitMap,
        GraphicsLvo.AllocDBufInfo,
        GraphicsLvo.FreeDBufInfo,
        GraphicsLvo.MakeVPort,
        GraphicsLvo.MrgCop,
        GraphicsLvo.FreeVPortCopLists,
        GraphicsLvo.FreeCprList,
        GraphicsLvo.FreeCopList,
        GraphicsLvo.NewRegion,
        GraphicsLvo.DisposeRegion,
        GraphicsLvo.AndRectRegion,
        GraphicsLvo.OrRectRegion,
        GraphicsLvo.ClearRectRegion,
        GraphicsLvo.ClearRegion,
        GraphicsLvo.XorRectRegion,
        GraphicsLvo.OrRegionRegion,
        GraphicsLvo.XorRegionRegion,
        GraphicsLvo.AndRegionRegion,
        GraphicsLvo.UCopperListInit,
        GraphicsLvo.CMove,
        GraphicsLvo.CWait,
        GraphicsLvo.CBump
    ];

    /// <summary>
    /// Private monitor-driver vectors are overlaid separately so the public
    /// LVO catalog remains unchanged.  The host gateway still preserves the
    /// original ROM target whenever the portable driver declines ownership.
    /// </summary>
    internal static IReadOnlyList<int> RomPrivateOverlayLvos { get; } =
    [ GraphicsPrivateLvo.SetDisplayInfoData ];

    public GraphicsServices(
        CopperStartGraphicsContext context,
        bool allowGuestOwnedDbufInfo = false,
        IGraphicsLayerBackend? layers = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        var layerAdapter = new CopperStartGraphicsLayerAdapter(context, layers);
        _layerGateway = layers as IGraphicsLayerGatewayBackend;
        _hasLayerLockProvider = layerAdapter.HasLockProvider;
        _displayAdapter = new CopperStartGraphicsDisplayAdapter(context);
        _core = new GraphicsLibraryCore(
            new CopperStartGraphicsMemoryAdapter(
                context.Memory,
                context.IsDisplayDmaRange),
            new CopperStartGraphicsAllocator(context),
            new CopperStartGraphicsBlitterAdapter(context),
            _displayAdapter,
            layerAdapter,
            context.DrawGList is not null &&
            context.DoCollision is not null &&
            context.RemIBob is not null
                ? new CopperStartGraphicsGelsAdapter(context)
                : null,
            context.MoveSprite is not null || context.ChangeSprite is not null
            || context.TryGetExtSprite is not null || context.TryChangeExtSprite is not null
                ? new CopperStartGraphicsSpriteAdapter(context)
                : null,
            allowGuestOwnedDbufInfo: allowGuestOwnedDbufInfo,
            graphicsLibraryBase: context.GraphicsLibraryBase,
            currentTask: context.CurrentTask);
        _portableAdapter = new CopperStartGraphicsRegisterAdapter(
            _core,
            _context.EnsureCompatibilityFont,
            _context.IsRtgBitMap,
            _context.IsRtgRastPort,
            _context.FontList,
            layerAdapter,
            _context.PreferProviderBitMapAllocations,
            (address, byteCount) =>
                byteCount <= int.MaxValue &&
                _context.Memory.IsMapped(address, (int)byteCount) &&
                _context.Memory.Bus.IsWritableMemoryRange(address, (int)byteCount),
            hasLayerLockProvider: layerAdapter.HasLockProvider);
    }

    // Kept internal so the host-side timed publication seam can be exercised
    // without widening the public GraphicsServices surface. Native ROM code
    // continues to reach the same adapter through GraphicsLibraryCore.
    internal IGraphicsTimedDisplayBackend TimedDisplayBackend => _displayAdapter;

    /// <summary>
    /// Keeps native ViewExtra monitor/top-line sidecars aligned when the
    /// Intuition rethink path publishes a View outside the graphics LVO
    /// adapter.  No display callback is invoked here; RethinkDisplay already
    /// owns the copper publication boundary.
    /// </summary>
    internal bool TryRefreshNativeViewSidecars(uint view)
        => _core.TryRefreshNativeViewSidecars(view);

    internal bool TryClearNativeViewSidecars()
        => _core.TryClearNativeViewSidecars();

    /// <summary>
    /// Shares the graphics provider/rebind lifetime gate with host-side
    /// Intuition display reconstruction. This keeps a RethinkDisplay copper
    /// publication from racing native GfxBase rebinding between admission and
    /// ActiView/sidecar commit.
    /// </summary>
    internal bool WithMonitorStateLock(Func<bool> operation)
        => _portableAdapter.WithMonitorStateLock(operation);

    /// <summary>
    /// Exposes the shared graphics-side screen-resource handoff to an
    /// Intuition/native owner.  The owner chooses whether the plane is
    /// caller/provider-owned or allocated by the explicit chip-plane path;
    /// this facade does not claim a Screen by itself.
    /// </summary>
    internal bool TryAllocateScreenResourceChain(
        byte depth,
        ushort width,
        ushort height,
        uint plane0,
        out ScreenResourceChainAllocation allocation)
        => _core.TryAllocateScreenResourceChain(
            depth,
            width,
            height,
            plane0,
            out allocation);

    internal bool TryAllocateScreenResourceChainWithPlane(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlane,
        bool requireDisplayDma,
        out ScreenResourceChainAllocation allocation)
        => _core.TryAllocateScreenResourceChainWithPlane(
            depth,
            width,
            height,
            clearPlane,
            requireDisplayDma,
            out allocation);

    internal bool TryAllocateScreenResourceChainWithPlanes(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlanes,
        bool requireDisplayDma,
        out ScreenResourceChainAllocation allocation)
        => _core.TryAllocateScreenResourceChainWithPlanes(
            depth,
            width,
            height,
            clearPlanes,
            requireDisplayDma,
            out allocation);

    internal int FreeScreenResourceChain(uint view)
        => _core.FreeScreenResourceChain(view);

    /// <summary>
    /// Exposes the classic Screen-prefix owner to a host Intuition or native
    /// CopperSharp68k handoff. The embedded ViewPort/RastPort/BitMap remain
    /// inside the allocated Screen envelope; CyberGraphX/provider surfaces
    /// stay outside this standard-planar boundary.
    /// </summary>
    internal bool TryAllocateScreenPrefix(
        byte depth,
        ushort width,
        ushort height,
        uint plane0,
        out ScreenPrefixAllocation allocation)
        => _core.TryAllocateScreenPrefix(
            depth,
            width,
            height,
            plane0,
            out allocation);

    internal bool TryAllocateScreenPrefixWithPlane(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlane,
        bool requireDisplayDma,
        out ScreenPrefixAllocation allocation)
        => _core.TryAllocateScreenPrefixWithPlane(
            depth,
            width,
            height,
            clearPlane,
            requireDisplayDma,
            out allocation);

    internal bool TryAllocateScreenPrefixWithPlanes(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlanes,
        bool requireDisplayDma,
        out ScreenPrefixAllocation allocation)
        => _core.TryAllocateScreenPrefixWithPlanes(
            depth,
            width,
            height,
            clearPlanes,
            requireDisplayDma,
            out allocation);

    internal int FreeScreenPrefix(uint screen)
        => _core.FreeScreenPrefix(screen);

    internal bool IsOwnedScreenPrefix(uint screen)
        => _core.IsOwnedScreenPrefix(screen);

    /// <summary>
    /// Installs the explicitly proven guest-memory subset over a native
    /// graphics.library jump table.  The operation is reversible and refuses
    /// to install if any selected vector is outside the mapped ROM image.
    /// </summary>
    public bool InstallKickstartRomOverlay(uint nativeGraphicsBase)
    {
        // The negative vector table and the positive GfxBase image are
        // consumed through native 68000 WORD/LONG accesses.  A byte-readable
        // odd base is therefore an address-error envelope, not a valid ROM
        // overlay target; reject it before probing or replacing any vector.
        if ((nativeGraphicsBase & 1u) != 0 ||
            nativeGraphicsBase < 0x0000_0420u ||
            nativeGraphicsBase > 0xFFFF_F000u)
            return false;

        if (_romOverlayRegistry is not null)
        {
            if (!_romOverlayRegistry.IsInstalled || _romOverlayBase != nativeGraphicsBase)
                return false;

            // A previous install may have declined sidecar allocation because
            // the guest allocator or native database span was not ready yet.
            // Repeated idempotent installation is allowed to retry that
            // bounded publication without rebuilding the ROM gateway table.
            // The full native GfxBase envelope may also become visible after
            // the first install (for example, when Exec finishes resident
            // initialization).  Rebind the portable registries at that
            // boundary before publishing any sidecars; otherwise later GfxNew
            // and MonitorList operations would continue to link through the
            // compact compatibility base even though the native overlay is
            // already active.
            var hasNativeEnvelope = HasNativeGfxBaseEnvelope(nativeGraphicsBase);
            if (!_reboundGraphicsBase && hasNativeEnvelope)
            {
                _reboundGraphicsBasePreviousBase =
                    _portableAdapter.RebindGraphicsLibraryBase(nativeGraphicsBase);
                if (!_portableAdapter.IsBoundToGraphicsLibraryBase(nativeGraphicsBase))
                {
                    RestoreReboundFontList();
                    return false;
                }

                _reboundGraphicsBase = true;
            }

            if (_reboundGraphicsBase && hasNativeEnvelope)
                _ = _portableAdapter.TryPublishNativeDisplayDatabase();

            return true;
        }

        var gateways = new List<HostLibraryGateway>(
            RomSafeOverlayLvos.Count + RomPrivateOverlayLvos.Count);
        foreach (var lvo in RomSafeOverlayLvos)
        {
            if (!TryAddOverlayGateway((int)lvo))
                return false;
        }
        foreach (var lvo in RomPrivateOverlayLvos)
        {
            if (!TryAddOverlayGateway(lvo))
                return false;
        }

        bool TryAddOverlayGateway(int captured)
        {
            var address64 = (long)nativeGraphicsBase + captured;
            if (address64 < 0 || address64 > uint.MaxValue)
                return false;

            var address = (uint)address64;
            if (!_context.Memory.IsMapped(address, 6))
                return false;

            // Keep the original Kickstart target available before the ROM
            // bytes are replaced by the host gateway.  A declined portable
            // operation must tail-chain to that JMP.L target; otherwise a
            // safe-vector overlay would silently swallow malformed or
            // provider-owned calls that still belong to native Kickstart.
            var originalTarget = TryReadNativeVectorTarget(address, out var target)
                ? target
                : 0u;

            gateways.Add(new HostLibraryGateway(
                captured,
                state => InvokeNativeOverlayGateway(state, captured, originalTarget)));
            return true;
        }

        // The host boot path initially publishes the synthetic font list at
        // the compatibility graphics.library base.  Once a native resident
        // is discovered, lifecycle publication must follow that resident's
        // GfxBase instead of silently mutating the host shim's TextFonts list.
        // Rebinding is intentionally non-destructive; the native image owns
        // initialization of its list envelope and existing nodes.
        if (_context.FontList is { } fontList &&
            fontList.BaseAddress != nativeGraphicsBase)
        {
            var previousBase = fontList.BaseAddress;
            if (!fontList.TryRebind(nativeGraphicsBase))
                return false;

            _reboundFontList = fontList;
            _reboundFontListPreviousBase = previousBase;
        }

        // A compact CopperScreen image may still cover the numeric tail
        // offsets with zero-filled backing storage.  Rebind the portable
        // registries only after the full native envelope proves its Exec
        // MonitorList sentinels (empty or already linked); otherwise a
        // compact overlay would decline OpenMonitor instead of retaining
        // the compatibility owner.
        if (!_reboundGraphicsBase && HasNativeGfxBaseEnvelope(nativeGraphicsBase))
        {
            _reboundGraphicsBasePreviousBase =
                _portableAdapter.RebindGraphicsLibraryBase(nativeGraphicsBase);
            if (!_portableAdapter.IsBoundToGraphicsLibraryBase(nativeGraphicsBase))
            {
                // The font list is rebound before the portable registries so
                // native OpenLibrary can publish its DefaultFont immediately.
                // If the graphics-base transition is refused (for example,
                // because an owned sidecar could not be detached), restore
                // that list claim before leaving the overlay uninstalled.
                RestoreReboundFontList();
                return false;
            }

            _reboundGraphicsBase = true;

            // GfxBase.DisplayInfoDataBase is an APTR to a private database
            // object, not an embedded Exec list. Publish the portable
            // sidecar only after the native monitor-list envelope has been
            // validated; a native/provider pointer remains untouched and an
            // allocation/write failure leaves the field at its prior value.
            _ = _portableAdapter.TryPublishNativeDisplayDatabase();
        }

        var registry = new HostLibraryGatewayRegistry(_context.Memory.Bus);
        registry.AddLibrary(nativeGraphicsBase, gateways);
        registry.InstallRomOverlays();
        _portableAdapter.SetNativeOverlayGraphicsBase(nativeGraphicsBase);
        _romOverlayRegistry = registry;
        _romOverlayBase = nativeGraphicsBase;
        return true;
    }

    private bool HasNativeGfxBaseEnvelope(uint graphicsBase)
    {
        if ((graphicsBase & 1u) != 0 ||
            graphicsBase > uint.MaxValue - (uint)(GraphicsLayouts.GfxBaseNativeSize - 1) ||
            !_context.Memory.IsMapped(graphicsBase, GraphicsLayouts.GfxBaseNativeSize))
        {
            return false;
        }

        var list = graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorList;
        var head = _context.Memory.ReadLong(
            graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListHead);
        var tail = _context.Memory.ReadLong(
            graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListTail);
        var tailPred = _context.Memory.ReadLong(
            graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListTailPred);
        var type = _context.Memory.ReadByte(
            graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListType);
        var pad = _context.Memory.ReadByte(
            graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListPad);
        return type == 0 &&
               pad == 0 &&
               (head == list + 4u && tail == 0 && tailPred == list ||
                // A resident/provider may already have linked one or more
                // MonitorSpec nodes before the host overlay is installed.
                // That is still a valid native GfxBase envelope; the
                // portable monitor registry can append to it without
                // claiming or rewriting the existing nodes.
                head != 0 && head != list + 4u && tail == 0 && tailPred != list);
    }

    internal bool IsKickstartRomOverlayInstalled => _romOverlayRegistry?.IsInstalled == true;

    private bool RestoreReboundFontList()
    {
        if (_reboundFontList is not { } fontList)
            return true;

        if (!fontList.TryRebind(_reboundFontListPreviousBase))
            return false;

        _reboundFontList = null;
        _reboundFontListPreviousBase = 0;
        return true;
    }

    private bool TryReadNativeVectorTarget(uint address, out uint target)
    {
        target = 0;
        if (!_context.Memory.IsMapped(address, 6) ||
            _context.Memory.ReadWord(address) != 0x4EF9)
        {
            return false;
        }

        target = _context.Memory.ReadLong(address + 2u);
        return target != 0;
    }

    private static void TailChainNativeVector(M68kCpuState state, uint target)
    {
        if (target != 0)
            state.ProgramCounter = target;
    }

    private M68kHostGatewayResult InvokeNativeOverlayGateway(
        M68kCpuState state,
        int captured,
        uint originalTarget)
    {
        var lvo = (GraphicsLvo)captured;

        // The resident Layers owner has a scheduler-aware raster boundary in
        // addition to its lock/super-bitmap companions.  Consult it before
        // the ordinary portable adapter so a mapped ROM call can preserve a
        // parked-task result and the exact guest frame just like
        // InvokeGateway.  The backend itself rejects unsupported/foreign
        // endpoints; those remain transparent to native Kickstart below.
        if (_layerGateway is not null &&
            _layerGateway.TryInvokeGraphicsRaster(state, lvo, out var rasterResult))
        {
            return rasterResult;
        }

        // Companion calls need the same topology authority.  Do not let the
        // compatibility layer adapter reduce a live Layers call to a direct
        // helper invocation, which would bypass blocking/continuation state.
        if (IsLayersGraphicsCompanion(captured) && _layerGateway is not null)
        {
            if (_layerGateway.TryInvokeGraphicsCompanion(state, lvo, out var companionResult))
                return companionResult;

            // A configured Layers owner declined this layer.  It is foreign
            // to that owner, so preserve the captured native vector instead
            // of falling through to the direct portable layer adapter.
            TailChainNativeVector(state, originalTarget);
            return M68kHostGatewayResult.Completed;
        }

        // Layer locks are scheduler-sensitive.  A live Layers owner must be
        // allowed to park the call and return its exact gateway result; a
        // foreign layer, or a standalone host with no lock provider, remains
        // transparent to the native Kickstart vector.
        if (IsLayerLockVector(lvo))
        {
            if (_layerGateway is not null &&
                _layerGateway.TryInvokeGraphicsCompanion(state, lvo, out var layerResult))
            {
                return layerResult;
            }

            if (_layerGateway is not null || !HasLayerLockProvider)
            {
                TailChainNativeVector(state, originalTarget);
                return M68kHostGatewayResult.Completed;
            }
        }

        // Safe vectors are deliberately bounded to this adapter.  A malformed
        // guest pointer is handled as the portable routine's normal no-op/
        // failure result; no host object is fabricated.
        if (captured == (int)GraphicsLvo.InitVPort)
        {
            // InitVPort is only claimed after the guarded guest
            // initialization succeeds.  Keep D0/cycles untouched on
            // malformed or unmapped input so the native/provider boundary can
            // retain ownership, matching the normal GraphicsServices path.
            if (!_portableAdapter.InitializeViewPort(state.A[0]))
            {
                TailChainNativeVector(state, originalTarget);
                return M68kHostGatewayResult.Completed;
            }

            _context.InitializeCompatibilityViewPort(state.A[0]);

            state.D[0] = 0;
            return M68kHostGatewayResult.Completed;
        }

        if (_portableAdapter.TryInvoke(state, captured, nativeOverlay: true))
            return M68kHostGatewayResult.Completed;

        // CopperStart's host builder owns generated raw DspIns streams.  They
        // are valid scheduler inputs, but they are intentionally not public
        // CopList descriptors and therefore cannot pass the portable
        // preallocated-resource validator.  Admit only the explicit host
        // classification; malformed/native/provider Views remain transparent
        // to their original vectors.
        if (captured == (int)GraphicsLvo.LoadView &&
            _context.IsCompatibilityLoadView?.Invoke(state.A[1]) == true &&
            TryForwardProviderLoadView(state))
        {
            return M68kHostGatewayResult.Completed;
        }

        // GetBitMapAttr is the one safe overlay vector whose ownership may
        // intentionally belong to a separate surface provider.  Preserve that
        // boundary even though the native jump-table slot is overlaid: a false
        // portable result must reach the provider callback instead of becoming
        // a silent compatibility no-op.
        if (captured == (int)GraphicsLvo.GetBitMapAttr &&
            _portableAdapter.IsProviderBitMap(state.A[0]))
        {
            state.D[0] = _context.GetBitMapAttr(state.A[0], state.D[1]);
            return M68kHostGatewayResult.Completed;
        }

        // LoadView is also admitted only for a validated standard display
        // chain.  When the linked ViewPort is explicitly RTG-owned, keep the
        // mapped slot transparent by forwarding to the display/provider
        // callback instead of fabricating a portable success or swallowing the
        // provider handoff.
        if (captured == (int)GraphicsLvo.LoadView &&
            _portableAdapter.IsRtgViewForProvider(state.A[1]) &&
            TryForwardProviderLoadView(state))
        {
            return M68kHostGatewayResult.Completed;
        }

        TailChainNativeVector(state, originalTarget);
        return M68kHostGatewayResult.Completed;
    }

    private bool HasLayerLockProvider => _hasLayerLockProvider;

    private static bool IsLayerLockVector(GraphicsLvo lvo)
        => lvo is GraphicsLvo.LockLayerRom or
            GraphicsLvo.AttemptLockLayerRom or GraphicsLvo.UnlockLayerRom;

    /// <summary>
    /// Forwards a declined RTG LoadView to the provider's status-aware host
    /// boundary.  The legacy callback is void-shaped and therefore always
    /// claims the request; a TryLoadView provider must be able to decline so
    /// the native vector remains reachable without fabricating a D0 result.
    /// </summary>
    private bool TryForwardProviderLoadView(M68kCpuState state)
    {
        if (_context.TryLoadView is not null)
        {
            var originalD0 = state.D[0];
            var originalCycles = state.Cycles;
            if (!_context.TryLoadView(state))
            {
                // A status-aware decline is a transparent provider boundary:
                // it must not consume the classic void-call registers before
                // the native vector gets a chance to handle the request.
                state.D[0] = originalD0;
                state.Cycles = originalCycles;
                return false;
            }

            state.D[0] = 0;
            return true;
        }

        if (_context.LoadView is null)
            return false;

        _context.LoadView(state);
        state.D[0] = 0;
        return true;
    }

    public void Dispose()
    {
        _romOverlayRegistry?.Dispose();
        _romOverlayRegistry = null;
        _romOverlayBase = 0;
        _portableAdapter.SetNativeOverlayGraphicsBase(0);

        if (_reboundGraphicsBase)
        {
            // Release must prove that the private sidecar is no longer
            // reachable before the registry binding moves away from the
            // native GfxBase.  A sparse/provider memory bridge may decline a
            // read, descriptor invalidation, or APTR clear.  Keep the
            // transition pending in that case so a later Dispose retry can
            // finish the handoff without leaking or freeing a reachable
            // database.
            if (!_portableAdapter.ReleaseNativeDisplayDatabase())
                return;

            _ = _portableAdapter.RebindGraphicsLibraryBase(
                _reboundGraphicsBasePreviousBase);
            if (!_portableAdapter.IsBoundToGraphicsLibraryBase(
                    _reboundGraphicsBasePreviousBase))
            {
                // The old native binding remains authoritative until the
                // monitor-list transaction succeeds.  Preserve the saved
                // base and ownership marker for a later retry.
                return;
            }

            _reboundGraphicsBase = false;
            _reboundGraphicsBasePreviousBase = 0;
        }

        if (_reboundFontList is not null)
            _ = RestoreReboundFontList();
    }

    /// <summary>Dispatches the CopperStart graphics.library compatibility table.</summary>
    public void Invoke(M68kCpuState state, int displacement)
    {
        _context.LogCall("graphics.library", displacement);
        if (displacement == (int)GraphicsLvo.InitVPort)
        {
            // InitVPort itself is a void routine, but the host-side display
            // projection must not observe an invalid guest pointer.  Keep the
            // portable clear and the host projection as one guarded boundary.
            if (!_portableAdapter.InitializeViewPort(state.A[0]))
                return;

            _context.InitializeCompatibilityViewPort(state.A[0]);
            state.D[0] = 0;
            return;
        }

        if (_portableAdapter.TryInvoke(state, displacement))
            return;

        // The host compatibility builder owns generated raw DspIns streams.
        // They are valid scheduler inputs, but intentionally not public
        // CopList descriptors. Admit only the explicit host classification;
        // malformed/native/provider Views remain unclaimed.
        if (displacement == (int)GraphicsLvo.LoadView &&
            _context.IsCompatibilityLoadView?.Invoke(state.A[1]) == true)
        {
            _ = TryForwardProviderLoadView(state);
            return;
        }

        switch (displacement)
        {
            case -30:
                // Only an explicitly RTG-owned bitmap may cross this
                // declined portable path.  Malformed standard bitmaps stay
                // available to native Kickstart.
                if (_portableAdapter.IsProviderBitMap(state.A[0]) ||
                    _portableAdapter.IsProviderBitMap(state.A[1]))
                    state.D[0] = _context.BltBitMap(state);

                return;
            case -552:
                // Layer clipping and RTG RastPorts are provider-owned; an
                // invalid standard RastPort must not become a synthetic
                // compatibility callback. A Layers provider decline is
                // transparent here; only an explicitly RTG-owned endpoint
                // may reach the CyberGraphX/provider callback.
                if ((_layerGateway is null &&
                        (_portableAdapter.IsProviderRastPort(state.A[0]) ||
                         _portableAdapter.IsProviderRastPort(state.A[1]))) ||
                    _portableAdapter.IsRtgRastPortForProvider(state.A[0]) ||
                    _portableAdapter.IsRtgRastPortForProvider(state.A[1]))
                    state.D[0] = _context.ClipBlit(state);

                return;
            case -606:
                if ((_layerGateway is null &&
                        (_portableAdapter.IsProviderBitMap(state.A[0]) ||
                         _portableAdapter.IsProviderRastPort(state.A[1]))) ||
                    _portableAdapter.IsProviderBitMap(state.A[0]) ||
                    _portableAdapter.IsRtgRastPortForProvider(state.A[1]))
                    state.D[0] = _context.BltBitMapRastPort(state);

                return;
            case -918: state.D[0] = _context.AllocBitMap(state); return;
            case -924:
                // A portable decline can mean a foreign/malformed standard
                // bitmap or an explicitly RTG-owned surface.  Only the
                // latter crosses to the provider callback; foreign standard
                // ownership remains available to native Kickstart.
                if (_portableAdapter.IsProviderBitMap(state.A[0]))
                {
                    _context.FreeBitMap(state.A[0]);
                    state.D[0] = 0;
                }

                return;
            case -942:
                // A declined ChangeVPBitMap may be a malformed standard
                // swap or an RTG-owned surface.  Forward only the latter to
                // the provider callback; malformed standard state remains
                // available to native Kickstart with D0/cycles untouched.
                if (_portableAdapter.IsRtgChangeViewPortForProvider(state.A[0], state.A[1]))
                    state.D[0] = _context.ChangeViewPortBitMap(state.A[0], state.A[1]);

                return;
            // GetBitMapAttr(struct BitMap *bm, ULONG attrNum) receives the
            // attribute selector in D1. Keep the provider fallback on the
            // same classic ABI as the portable adapter and ROM overlay.
            case -960:
                // Keep malformed/foreign standard BitMaps unclaimed.  A
                // registered RTG surface retains its provider ABI instead.
                if (_portableAdapter.IsProviderBitMap(state.A[0]))
                    state.D[0] = _context.GetBitMapAttr(state.A[0], state.D[1]);

                return;
            case -0xD2:
                // MrgCop may be declined by the portable validator for a
                // malformed standard View, or because the View belongs to an
                // RTG provider.  Only the latter may reach the compatibility
                // callback; publishing copper state for a foreign/malformed
                // View would steal the native/provider ownership boundary.
                if (_portableAdapter.IsRtgViewForProvider(state.A[1]))
                    state.D[0] = _context.MergeCopperLists(state);

                return;
            case -0xD8:
                // MakeVPort has the same boundary for its viewport argument.
                // Keep malformed standard state available to native Kickstart
                // while allowing an explicitly RTG-owned viewport provider to
                // handle its own copper setup.
                if (_portableAdapter.IsRtgViewPortForProvider(state.A[1]))
                    state.D[0] = _context.MakeViewPort(state);

                return;
            case -0xDE:
                // LoadView is void on success, but the portable adapter
                // declines malformed standard Views as well as RTG-owned
                // Views.  Only the latter may cross into the CyberGraphX/
                // native provider callback; otherwise a legacy host helper
                // could publish a malformed View and consume the call.
                if (_portableAdapter.IsRtgViewForProvider(state.A[1]))
                    _ = TryForwardProviderLoadView(state);

                return;
            case -0x3C:
                // Text is claimed only by the portable decoded-font path or
                // the explicit layer provider above.  A declined call may
                // carry malformed guest font/text state; do not turn it into
                // a fabricated legacy success here.
                return;
            case -0x42:
                // SetFont has the same ownership boundary.  The old host
                // helper could write a partial cache for an invalid font or
                // RastPort after the portable adapter had declined it.
                return;
            case -0x48:
                // Null OpenFont requests retain the compatibility Topaz
                // bridge.  A malformed non-null TextAttr must remain
                // unclaimed once the portable/list matcher has declined it;
                // otherwise this switch would fabricate a host font and
                // swallow native/provider ownership.
                if (state.A[0] == 0)
                    state.D[0] = _context.EnsureCompatibilityFont();

                return;
            case -0x4E:
                // CloseFont has no host-only compatibility side effect.  If
                // the portable registry declined the guest font envelope,
                // leave the call unclaimed so native Kickstart or a font
                // provider can own the failure instead of fabricating D0=0.
                return;
            case -0xC0:
                // A declined LoadRGB4 may be malformed standard state or an
                // RTG viewport owned by a provider.  Preserve the native
                // boundary for the former; only the latter reaches the
                // legacy host callback.
                if (_portableAdapter.IsRtgViewPortForProvider(state.A[0]))
                {
                    _context.LoadRgb4(state);
                    state.D[0] = 0;
                }

                return;
            case -0x120:
                // Keep SetRGB4's provider/native handoff symmetric with
                // LoadRGB4 instead of fabricating success for a malformed
                // viewport or ColorMap.
                if (_portableAdapter.IsRtgViewPortForProvider(state.A[0]))
                {
                    _context.SetRgb4(state);
                    state.D[0] = 0;
                }

                return;
            case (int)GraphicsLvo.OwnBlitter:
            case (int)GraphicsLvo.DisownBlitter:
            case (int)GraphicsLvo.WaitBlit:
            case (int)GraphicsLvo.QBlit:
            case (int)GraphicsLvo.QBSBlit:
                // A status-aware blitter adapter declines when no
                // scheduler/custom-chip owner is connected. Preserve the
                // guest register state so native Kickstart or another
                // provider retains these calls.
                return;
            case (int)GraphicsLvo.WaitBOVP:
            case (int)GraphicsLvo.ScrollVPort:
                // The portable adapter already validated the complete
                // viewport/RasInfo chain.  If it declined the call, leave
                // the register/cycle state untouched for the native or
                // provider-owned implementation instead of manufacturing a
                // compatibility host object through the generic fallback.
                return;
            case -0x10E:
                if (_context.WaitTof is not null)
                    state.D[0] = WaitTof(state);

                return;
            default:
                // A vector that has not been claimed by the portable core or
                // an explicit provider must remain available to the native
                // Kickstart/layers owner.  Returning a fabricated host object
                // here used to make declined layered and unimplemented calls
                // look successful, corrupting the classic D0/cycle boundary
                // and preventing the provider from taking over.
                return;
        }
    }

    /// <summary>
    /// Scheduler-aware host-shim entry.  The ordinary compatibility API stays
    /// void-shaped, while the injected Layers owner can suspend a contended
    /// LockLayerRom call without losing its portable continuation token.
    /// </summary>
    public M68kHostGatewayResult InvokeGateway(M68kCpuState state, int displacement)
    {
        if (_layerGateway is not null &&
            PortableLayers.LayersRasterCore.IsSupported(
                checked((short)displacement)) &&
            _layerGateway.TryInvokeGraphicsRaster(
                state,
                (GraphicsLvo)displacement,
                out var rasterResult))
        {
            // A CopperStart-owned layered endpoint remains inside RasterCore
            // until it has either completed/declined with its exact frame
            // restored or parked the outer scheduler.  In particular, a
            // blocked call must never run the ordinary compatibility path.
            return rasterResult;
        }

        // A mapped Layers endpoint may have been admitted by the topology
        // owner and then declined by its validated provider (for example,
        // after a complete destination preflight failure). The native overlay
        // path will tail-chain this decline; the ordinary host path has no
        // resident vector to call, so keep it a side-effect-free no-op rather
        // than re-entering the legacy per-operation bridge. That bridge is
        // deliberately a diagnostic fallback and must not manufacture a
        // second clipping/damage owner.
        if (_layerGateway is not null &&
            PortableLayers.LayersRasterCore.IsSupported(
                checked((short)displacement)) &&
            IsLayerProviderRasterEndpoint(state, (GraphicsLvo)displacement))
        {
            return M68kHostGatewayResult.Completed;
        }

        if (_layerGateway is not null &&
            IsLayersGraphicsCompanion(displacement) &&
            _layerGateway.TryInvokeGraphicsCompanion(
                state,
                (GraphicsLvo)displacement,
                out var result))
        {
            return result;
        }

        Invoke(state, displacement);
        return M68kHostGatewayResult.Completed;
    }

    private bool IsLayerProviderRasterEndpoint(
        M68kCpuState state,
        GraphicsLvo lvo)
    {
        var primary = lvo == GraphicsLvo.ClipBlit
            ? state.A[1]
            : lvo is GraphicsLvo.ReadPixelLine8 or
                GraphicsLvo.WritePixelLine8 or
                GraphicsLvo.ReadPixelArray8 or
                GraphicsLvo.WritePixelArray8 or
                GraphicsLvo.WriteChunkyPixels
                ? state.A[0]
                : state.A[1];
        if (_portableAdapter.IsProviderRastPort(primary))
            return true;

        return lvo == GraphicsLvo.ClipBlit &&
            _portableAdapter.IsProviderRastPort(state.A[0]);
    }

    private static bool IsLayersGraphicsCompanion(int displacement)
        => (GraphicsLvo)displacement is GraphicsLvo.LockLayerRom or
            GraphicsLvo.AttemptLockLayerRom or GraphicsLvo.UnlockLayerRom or
            GraphicsLvo.SyncSBitMap or GraphicsLvo.CopySBitMap;

    /// <summary>Executes the migrated WaitTOF service using the scheduler path.</summary>
    public uint WaitTof(M68kCpuState state)
    {
        _context.WaitTof?.Invoke(state);
        return 0;
    }

    /// <summary>
    /// Writes a custom register through the context's emulated-bus callback.
    /// It is deliberately not implemented with HostGuestMemory.
    /// </summary>
    public void WriteCustomRegister(uint address, ushort value, long cycles)
        => _context.WriteCustomRegister(address, value, cycles);

    public uint InitView(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitView);
        return state.D[0];
    }

    /// <summary>
    /// Initializes a guest RastPort through the same guarded portable
    /// structure path used by LVO dispatch and the native overlay.
    /// </summary>
    public uint InitRastPort(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitRastPort);
        return state.D[0];
    }

    /// <summary>Initializes a guest standard-planar BitMap envelope.</summary>
    public uint InitBitMap(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitBitMap);
        return state.D[0];
    }

    /// <summary>Initializes a caller-owned AreaInfo collector envelope.</summary>
    public uint InitArea(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitArea);
        return state.D[0];
    }

    /// <summary>Initializes a caller-owned temporary-raster descriptor.</summary>
    public uint InitTmpRas(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitTmpRas);
        return state.D[0];
    }

    /// <summary>Allocates a guest raster through the portable allocator boundary.</summary>
    public uint AllocRaster(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AllocRaster);
        return state.D[0];
    }

    /// <summary>Frees a guest raster through the portable allocator boundary.</summary>
    public uint FreeRaster(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeRaster);
        return state.D[0];
    }

    /// <summary>Scales a guest planar bitmap through the portable boundary.</summary>
    public uint BitMapScale(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.BitMapScale);
        return state.D[0];
    }

    /// <summary>Computes the classic bitmap scale quotient through the portable boundary.</summary>
    public uint ScalerDiv(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ScalerDiv);
        return state.D[0];
    }

    /// <summary>Allocates a standard planar BitMap through the portable boundary.</summary>
    public uint AllocBitMap(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AllocBitMap);
        return state.D[0];
    }

    /// <summary>Frees a standard planar BitMap through the portable boundary.</summary>
    public uint FreeBitMap(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeBitMap);
        return state.D[0];
    }

    /// <summary>Queries a standard planar BitMap attribute through the portable boundary.</summary>
    public uint GetBitMapAttr(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetBitMapAttr);
        return state.D[0];
    }

    /// <summary>Allocates a standard graphics extended node through the portable boundary.</summary>
    public uint GfxNew(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GfxNew);
        return state.D[0];
    }

    /// <summary>Frees an instance-owned graphics extended node through the portable boundary.</summary>
    public uint GfxFree(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GfxFree);
        return state.D[0];
    }

    /// <summary>Associates a graphics extended node with a guest owner pointer.</summary>
    public uint GfxAssociate(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GfxAssociate);
        return state.D[0];
    }

    /// <summary>Looks up the extended node associated with a guest owner pointer.</summary>
    public uint GfxLookUp(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GfxLookUp);
        return state.D[0];
    }

    /// <summary>Allocates an instance-owned guest Region envelope.</summary>
    public uint NewRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.NewRegion);
        return state.D[0];
    }

    /// <summary>Disposes an instance-owned guest Region and its rectangles.</summary>
    public uint DisposeRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.DisposeRegion);
        return state.D[0];
    }

    /// <summary>Unions a rectangle into a guest Region.</summary>
    public uint OrRectRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.OrRectRegion);
        return state.D[0];
    }

    /// <summary>Intersects a guest Region with a rectangle.</summary>
    public uint AndRectRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AndRectRegion);
        return state.D[0];
    }

    /// <summary>Exclusive-ors a rectangle into a guest Region.</summary>
    public uint XorRectRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.XorRectRegion);
        return state.D[0];
    }

    /// <summary>Subtracts a rectangle from a guest Region.</summary>
    public uint ClearRectRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ClearRectRegion);
        return state.D[0];
    }

    /// <summary>Clears all rectangles from a guest Region.</summary>
    public uint ClearRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ClearRegion);
        return state.D[0];
    }

    /// <summary>Unions one guest Region into another.</summary>
    public uint OrRegionRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.OrRegionRegion);
        return state.D[0];
    }

    /// <summary>Exclusive-ors one guest Region into another.</summary>
    public uint XorRegionRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.XorRegionRegion);
        return state.D[0];
    }

    /// <summary>Intersects one guest Region with another.</summary>
    public uint AndRegionRegion(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AndRegionRegion);
        return state.D[0];
    }

    /// <summary>Allocates an instance-owned guest ColorMap and its color tables.</summary>
    public uint GetColorMap(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetColorMap);
        return state.D[0];
    }

    /// <summary>Releases an instance-owned guest ColorMap and its color tables.</summary>
    public uint FreeColorMap(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeColorMap);
        return state.D[0];
    }

    /// <summary>Sets one guest ColorMap entry using 4-bit RGB components.</summary>
    public uint SetRGB4CM(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetRGB4CM);
        return state.D[0];
    }

    /// <summary>Sets one guest ColorMap entry using 32-bit RGB components.</summary>
    public uint SetRGB32CM(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetRGB32CM);
        return state.D[0];
    }

    /// <summary>Attaches the instance-owned PaletteExtra to a ColorMap.</summary>
    public uint AttachPalExtra(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AttachPalExtra);
        return state.D[0];
    }

    /// <summary>Obtains a shared or exclusive pen from an instance-owned ColorMap.</summary>
    public uint ObtainPen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ObtainPen);
        return state.D[0];
    }

    /// <summary>Obtains the best matching pen using a V39 tag list.</summary>
    public uint ObtainBestPenA(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ObtainBestPenA);
        return state.D[0];
    }

    /// <summary>Releases a pen owned by an instance ColorMap.</summary>
    public uint ReleasePen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ReleasePen);
        return state.D[0];
    }

    /// <summary>Finds the nearest color in an instance-owned ColorMap.</summary>
    public uint FindColor(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FindColor);
        return state.D[0];
    }

    /// <summary>Applies the portable scalar/in-place subset of VideoControl.</summary>
    public uint VideoControl(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.VideoControl);
        return state.D[0];
    }

    /// <summary>Reads one guest ColorMap entry as a packed RGB4 value.</summary>
    public uint GetRGB4(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetRGB4);
        return state.D[0];
    }

    /// <summary>Reads a guest ColorMap range as an RGB32 result table.</summary>
    public uint GetRGB32(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetRGB32);
        return state.D[0];
    }

    /// <summary>Loads a standard viewport palette from an RGB4 table.</summary>
    public uint LoadRGB4(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.LoadRGB4))
            return state.D[0];

        // CyberGraphX/native providers own explicitly RTG-backed viewports.
        // Keep the callback boundary identical to normal LVO dispatch and do
        // not claim malformed standard viewport state here.
        if (_portableAdapter.IsRtgViewPortForProvider(state.A[0]))
        {
            _context.LoadRgb4(state);
            state.D[0] = 0;
        }

        return state.D[0];
    }

    /// <summary>Loads a standard viewport palette from an RGB32 table.</summary>
    public uint LoadRGB32(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.LoadRGB32);
        return state.D[0];
    }

    /// <summary>Sets one standard viewport palette entry using RGB4 components.</summary>
    public uint SetRGB4(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetRGB4))
            return state.D[0];

        if (_portableAdapter.IsRtgViewPortForProvider(state.A[0]))
        {
            _context.SetRgb4(state);
            state.D[0] = 0;
        }

        return state.D[0];
    }

    /// <summary>Sets one standard viewport palette entry using RGB32 components.</summary>
    public uint SetRGB32(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetRGB32);
        return state.D[0];
    }

    /// <summary>Obtains one of the instance-owned standard simple sprites.</summary>
    public uint GetSprite(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetSprite);
        return state.D[0];
    }

    /// <summary>Releases an instance-owned standard simple sprite number.</summary>
    public uint FreeSprite(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeSprite);
        return state.D[0];
    }

    /// <summary>Moves a standard sprite through the planar/native projection seam.</summary>
    public uint MoveSprite(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.MoveSprite);
        return state.D[0];
    }

    /// <summary>Rebinds a standard sprite image through the planar/native seam.</summary>
    public uint ChangeSprite(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ChangeSprite);
        return state.D[0];
    }

    /// <summary>Converts an extended sprite request into guest-compatible storage.</summary>
    public uint GetExtSpriteA(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetExtSpriteA);
        return state.D[0];
    }

    /// <summary>Allocates guest image data for an extended sprite.</summary>
    public uint AllocSpriteDataA(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AllocSpriteDataA);
        return state.D[0];
    }

    /// <summary>Changes an extended sprite and returns its classic BOOL result.</summary>
    public uint ChangeExtSpriteA(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ChangeExtSpriteA);
        return state.D[0];
    }

    /// <summary>Releases instance-owned extended sprite image data.</summary>
    public uint FreeSpriteData(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeSpriteData);
        return state.D[0];
    }

    /// <summary>Initializes a guest GELS/VSprite sentinel chain.</summary>
    public uint InitGels(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitGels);
        return state.D[0];
    }

    /// <summary>Publishes one guest GELS collision handler entry.</summary>
    public uint SetCollision(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetCollision);
        return state.D[0];
    }

    /// <summary>Builds the masks for a guest VSprite or Bob.</summary>
    public uint InitMasks(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitMasks);
        return state.D[0];
    }

    /// <summary>Adds a guest VSprite to its RastPort GELS chain.</summary>
    public uint AddVSprite(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AddVSprite);
        return state.D[0];
    }

    /// <summary>Removes a guest VSprite from its GELS chain.</summary>
    public uint RemVSprite(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.RemVSprite);
        return state.D[0];
    }

    /// <summary>Sorts a guest GELS chain by its documented Y/X ordering.</summary>
    public uint SortGList(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SortGList);
        return state.D[0];
    }

    /// <summary>Adds a guest Bob and its active VSprite to a GELS chain.</summary>
    public uint AddBob(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AddBob);
        return state.D[0];
    }

    /// <summary>Removes a guest Bob from a GELS chain.</summary>
    public uint RemIBob(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.RemIBob);
        return state.D[0];
    }

    /// <summary>Adds an animation object and its active Bob chain.</summary>
    public uint AddAnimOb(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AddAnimOb);
        return state.D[0];
    }

    /// <summary>Advances guest animation objects using their fixed-point state.</summary>
    public uint Animate(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.Animate);
        return state.D[0];
    }

    /// <summary>Allocates or publishes guest GELS animation buffers.</summary>
    public uint GetGBuffers(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetGBuffers);
        return state.D[0];
    }

    /// <summary>Builds animation collision masks across all active frames.</summary>
    public uint InitGMasks(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.InitGMasks);
        return state.D[0];
    }

    /// <summary>Releases instance-owned guest GELS animation buffers.</summary>
    public uint FreeGBuffers(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeGBuffers);
        return state.D[0];
    }

    /// <summary>Runs provider-owned GELS collision dispatch when explicitly attached.</summary>
    public uint DoCollision(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.DoCollision);
        return state.D[0];
    }

    /// <summary>Runs provider-owned GELS drawing dispatch when explicitly attached.</summary>
    public uint DrawGList(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.DrawGList);
        return state.D[0];
    }

    /// <summary>Claims the explicit scheduler/custom-chip blitter owner.</summary>
    public uint OwnBlitter(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.OwnBlitter);
        return state.D[0];
    }

    /// <summary>Releases the explicit scheduler/custom-chip blitter owner.</summary>
    public uint DisownBlitter(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.DisownBlitter);
        return state.D[0];
    }

    /// <summary>Waits for the explicit scheduler/custom-chip blitter owner.</summary>
    public uint WaitBlit(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WaitBlit);
        return state.D[0];
    }

    /// <summary>Queues a blitter node through the explicit host/custom-chip seam.</summary>
    public uint QBlit(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.QBlit);
        return state.D[0];
    }

    /// <summary>Queues a beam-synchronized blitter node through the host seam.</summary>
    public uint QBSBlit(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.QBSBlit);
        return state.D[0];
    }

    /// <summary>Locks a layer through the explicit layers/native backend seam.</summary>
    public uint LockLayerRom(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.LockLayerRom);
        return state.D[0];
    }

    /// <summary>Attempts a layer lock and returns the classic BOOL result.</summary>
    public uint AttemptLockLayerRom(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AttemptLockLayerRom);
        return state.D[0];
    }

    /// <summary>Releases a layer lock through the explicit backend seam.</summary>
    public uint UnlockLayerRom(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.UnlockLayerRom);
        return state.D[0];
    }

    /// <summary>Synchronizes a layer super-bitmap when a backend claims it.</summary>
    public uint SyncSBitMap(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SyncSBitMap);
        return state.D[0];
    }

    /// <summary>Copies a layer super-bitmap when a backend claims it.</summary>
    public uint CopySBitMap(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CopySBitMap);
        return state.D[0];
    }

    /// <summary>Finds a native OCS/ECS display-database record by ModeID.</summary>
    public uint FindDisplayInfo(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FindDisplayInfo);
        return state.D[0];
    }

    /// <summary>Enumerates the native OCS/ECS display-database records.</summary>
    public uint NextDisplayInfo(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.NextDisplayInfo);
        return state.D[0];
    }

    /// <summary>Queries native monitor/chip availability for a ModeID.</summary>
    public uint ModeNotAvailable(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ModeNotAvailable);
        return state.D[0];
    }

    /// <summary>Copies a native display-database data chunk into guest memory.</summary>
    public uint GetDisplayInfoData(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetDisplayInfoData);
        return state.D[0];
    }

    /// <summary>Opens an instance-owned native PAL/NTSC MonitorSpec.</summary>
    public uint OpenMonitor(M68kCpuState state)
    {
        // The adapter preserves inputs when declining to a patched/native
        // provider. This direct library call has no next provider to handle
        // that decline, so expose the documented NULL failure result.
        if (!_portableAdapter.TryInvoke(state, (int)GraphicsLvo.OpenMonitor))
            state.D[0] = 0;
        return state.D[0];
    }

    /// <summary>Closes an instance-owned native PAL/NTSC MonitorSpec.</summary>
    public uint CloseMonitor(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CloseMonitor);
        return state.D[0];
    }

    /// <summary>Selects the best native OCS/ECS ModeID for a guest tag list.</summary>
    public uint BestModeIDA(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.BestModeIDA);
        return state.D[0];
    }

    /// <summary>Coerces a standard viewport request to a native monitor mode.</summary>
    public uint CoerceMode(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CoerceMode);
        return state.D[0];
    }

    /// <summary>Returns the native ModeID represented by a standard viewport.</summary>
    public uint GetVPModeID(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetVPModeID);
        return state.D[0];
    }

    /// <summary>Allocates an instance-owned standard DBufInfo envelope.</summary>
    public uint AllocDBufInfo(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AllocDBufInfo);
        return state.D[0];
    }

    /// <summary>Frees an instance-owned standard DBufInfo envelope.</summary>
    public uint FreeDBufInfo(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeDBufInfo);
        return state.D[0];
    }

    /// <summary>Changes a standard viewport bitmap through the portable/provider boundary.</summary>
    public uint ChangeVPBitMap(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.ChangeVPBitMap))
            return state.D[0];

        if (_portableAdapter.IsRtgChangeViewPortForProvider(state.A[0], state.A[1]))
            state.D[0] = _context.ChangeViewPortBitMap(state.A[0], state.A[1]);

        return state.D[0];
    }

    /// <summary>Builds a standard viewport copper list through the portable/provider boundary.</summary>
    public uint MakeVPort(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.MakeVPort))
            return state.D[0];

        if (_portableAdapter.IsRtgViewPortForProvider(state.A[1]))
            state.D[0] = _context.MakeViewPort(state);

        return state.D[0];
    }

    /// <summary>Merges a standard View's copper lists through the portable/provider boundary.</summary>
    public uint MrgCop(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.MrgCop))
            return state.D[0];

        if (_portableAdapter.IsRtgViewForProvider(state.A[1]))
            state.D[0] = _context.MergeCopperLists(state);

        return state.D[0];
    }

    /// <summary>Releases standard viewport copper resources through the portable boundary.</summary>
    public uint FreeVPortCopLists(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeVPortCopLists);
        return state.D[0];
    }

    /// <summary>Releases a display-owned CopperList envelope through the portable/provider boundary.</summary>
    public uint FreeCprList(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeCprList);
        return state.D[0];
    }

    /// <summary>Releases an instance-owned user copper list through the portable boundary.</summary>
    public uint FreeCopList(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FreeCopList);
        return state.D[0];
    }

    /// <summary>Initializes a caller-owned user copper-list envelope.</summary>
    public uint UCopperListInit(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.UCopperListInit);
        return state.D[0];
    }

    /// <summary>Appends a MOVE instruction to a user copper list.</summary>
    public uint CMove(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CMove);
        return state.D[0];
    }

    /// <summary>Appends a WAIT instruction to a user copper list.</summary>
    public uint CWait(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CWait);
        return state.D[0];
    }

    /// <summary>Advances a user copper list to its next instruction block.</summary>
    public uint CBump(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CBump);
        return state.D[0];
    }

    /// <summary>Waits for the next top-of-frame boundary through the scheduler-aware path.</summary>
    public uint WaitTOF(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WaitTOF);
        return state.D[0];
    }

    /// <summary>Waits for the bottom of a standard viewport through the scheduler-aware path.</summary>
    public uint WaitBOVP(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WaitBOVP);
        return state.D[0];
    }

    /// <summary>Reads the current vertical beam position through the display boundary.</summary>
    public uint VBeamPos(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.VBeamPos);
        return state.D[0];
    }

    /// <summary>Requests a standard viewport presentation rebuild.</summary>
    public uint ScrollVPort(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ScrollVPort);
        return state.D[0];
    }

    /// <summary>Calculates the display scan-line count through the display boundary.</summary>
    public uint CalcIVG(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CalcIVG);
        return state.D[0];
    }

    /// <summary>Queries the active chipset revision bits through the display boundary.</summary>
    public uint SetChipRev(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetChipRev);
        return state.D[0];
    }

    /// <summary>
    /// Initializes a guest ViewPort for the direct CopperScreen/console host
    /// path.  Keep the guest clear and the compatibility display projection
    /// in one guarded boundary, matching normal LVO dispatch and the native
    /// overlay.  A malformed envelope remains available to the native or
    /// provider implementation with its D0/cycle state unchanged.
    /// </summary>
    public uint InitVPort(M68kCpuState state)
    {
        if (!_portableAdapter.InitializeViewPort(state.A[0]))
            return state.D[0];

        _context.InitializeCompatibilityViewPort(state.A[0]);
        state.D[0] = 0;
        return state.D[0];
    }

    /// <summary>
    /// Performs the direct host-side View activation boundary.  Standard
    /// Views use the same scheduler-aware portable path as LVO dispatch; a
    /// declined RTG View may reach the explicit provider callback, while
    /// malformed standard state remains unclaimed with D0 and cycles intact.
    /// </summary>
    public uint LoadView(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.LoadView))
            return state.D[0];

        if (_portableAdapter.IsRtgViewForProvider(state.A[1]))
            _ = TryForwardProviderLoadView(state);

        return state.D[0];
    }

    public uint SetAPen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetAPen);
        return state.D[0];
    }

    public uint SetBPen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetBPen);
        return state.D[0];
    }

    public uint SetDrMd(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetDrMd);
        return state.D[0];
    }

    /// <summary>Sets both pens and the draw mode through the portable boundary.</summary>
    public uint SetABPenDrMd(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetABPenDrMd);
        return state.D[0];
    }

    /// <summary>Sets the RastPort plane write mask through the portable boundary.</summary>
    public uint SetWriteMask(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetWriteMask);
        return state.D[0];
    }

    /// <summary>Sets the RastPort maximum pen through the portable boundary.</summary>
    public uint SetMaxPen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetMaxPen);
        return state.D[0];
    }

    /// <summary>Gets the RastPort foreground pen through the portable boundary.</summary>
    public uint GetAPen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetAPen);
        return state.D[0];
    }

    /// <summary>Gets the RastPort background pen through the portable boundary.</summary>
    public uint GetBPen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetBPen);
        return state.D[0];
    }

    /// <summary>Gets the RastPort draw mode through the portable boundary.</summary>
    public uint GetDrMd(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetDrMd);
        return state.D[0];
    }

    /// <summary>Gets the RastPort outline pen through the portable boundary.</summary>
    public uint GetOutlinePen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetOutlinePen);
        return state.D[0];
    }

    /// <summary>Sets the RastPort outline pen and returns its previous value.</summary>
    public uint SetOutlinePen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetOutlinePen);
        return state.D[0];
    }

    /// <summary>Applies a bounded V39 RastPort attribute tag list.</summary>
    public uint SetRPAttrsA(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetRPAttrsA);
        return state.D[0];
    }

    /// <summary>Queries a bounded V39 RastPort attribute tag list.</summary>
    public uint GetRPAttrsA(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.GetRPAttrsA);
        return state.D[0];
    }

    public uint TextLength(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.TextLength);
        return state.D[0];
    }

    /// <summary>Measures a guest string through the portable text-metric boundary.</summary>
    public uint TextExtent(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.TextExtent);
        return state.D[0];
    }

    /// <summary>Fits a guest string through the portable text-metric boundary.</summary>
    public uint TextFit(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.TextFit);
        return state.D[0];
    }

    /// <summary>Publishes a guest font extent through the portable metric boundary.</summary>
    public uint FontExtent(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.FontExtent);
        return state.D[0];
    }

    /// <summary>Scores two guest TextAttr records through the portable matcher.</summary>
    public uint WeighTAMatch(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WeighTAMatch);
        return state.D[0];
    }

    /// <summary>Queries the non-intrinsic font style through the portable boundary.</summary>
    public uint AskSoftStyle(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AskSoftStyle);
        return state.D[0];
    }

    /// <summary>Updates the non-intrinsic font style through the portable boundary.</summary>
    public uint SetSoftStyle(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetSoftStyle);
        return state.D[0];
    }

    public uint SetFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetFont);
        return state.D[0];
    }

    /// <summary>Opens a guest TextFont through the portable lifecycle boundary.</summary>
    public uint OpenFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.OpenFont);
        return state.D[0];
    }

    /// <summary>Closes a guest TextFont through the portable lifecycle boundary.</summary>
    public uint CloseFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.CloseFont);
        return state.D[0];
    }

    /// <summary>Copies the current guest font attributes through the portable boundary.</summary>
    public uint AskFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AskFont);
        return state.D[0];
    }

    /// <summary>Adds a guest TextFont through the portable lifecycle boundary.</summary>
    public uint AddFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AddFont);
        return state.D[0];
    }

    /// <summary>Removes a guest TextFont through the portable lifecycle boundary.</summary>
    public uint RemFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.RemFont);
        return state.D[0];
    }

    /// <summary>Extends a guest TextFont through the portable lifecycle boundary.</summary>
    public uint ExtendFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ExtendFont);
        return state.D[0];
    }

    /// <summary>Strips a guest TextFont through the portable lifecycle boundary.</summary>
    public uint StripFont(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.StripFont);
        return state.D[0];
    }

    public uint Move(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.Move);
        return state.D[0];
    }

    public uint Draw(M68kCpuState state)
    {
        if (!_portableAdapter.TryInvoke(state, (int)GraphicsLvo.Draw))
        {
            if (IsDeclinedLayerGatewayRaster(state, GraphicsLvo.Draw))
                return state.D[0];
            _context.Draw(state);
        }

        return 0;
    }

    public uint Text(M68kCpuState state)
    {
        if (!_portableAdapter.TryInvoke(state, (int)GraphicsLvo.Text))
        {
            if (IsDeclinedLayerGatewayRaster(state, GraphicsLvo.Text))
                return state.D[0];
            _context.Text(state);
        }

        return 0;
    }

    public uint SetRast(M68kCpuState state)
    {
        if (!_portableAdapter.TryInvoke(state, (int)GraphicsLvo.SetRast))
        {
            if (IsDeclinedLayerGatewayRaster(state, GraphicsLvo.SetRast))
                return state.D[0];
            _context.SetRast(state);
        }

        return 0;
    }

    public uint RectFill(M68kCpuState state)
    {
        if (!_portableAdapter.TryInvoke(state, (int)GraphicsLvo.RectFill))
        {
            if (IsDeclinedLayerGatewayRaster(state, GraphicsLvo.RectFill))
                return state.D[0];
            _context.RectFill(state);
        }

        return 0;
    }

    /// <summary>Reads a planar pixel through the portable RastPort boundary.</summary>
    public uint ReadPixel(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ReadPixel);
        return state.D[0];
    }

    /// <summary>Writes a planar pixel through the portable RastPort boundary.</summary>
    public uint WritePixel(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WritePixel);
        return state.D[0];
    }

    /// <summary>Reads a byte-per-pen RastPort line through the portable boundary.</summary>
    public uint ReadPixelLine8(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ReadPixelLine8);
        return state.D[0];
    }

    /// <summary>Writes a byte-per-pen RastPort line through the portable boundary.</summary>
    public uint WritePixelLine8(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WritePixelLine8);
        return state.D[0];
    }

    /// <summary>Reads a byte-per-pen RastPort rectangle through the portable boundary.</summary>
    public uint ReadPixelArray8(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ReadPixelArray8);
        return state.D[0];
    }

    /// <summary>Writes a byte-per-pen RastPort rectangle through the portable boundary.</summary>
    public uint WritePixelArray8(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WritePixelArray8);
        return state.D[0];
    }

    /// <summary>Writes chunky pixels through the portable planar conversion boundary.</summary>
    public uint WriteChunkyPixels(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.WriteChunkyPixels);
        return state.D[0];
    }

    /// <summary>Draws a connected point chain through the portable RastPort boundary.</summary>
    public uint PolyDraw(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.PolyDraw);
        return state.D[0];
    }

    /// <summary>Draws an ellipse through the portable RastPort boundary.</summary>
    public uint DrawEllipse(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.DrawEllipse);
        return state.D[0];
    }

    /// <summary>Scrolls a RastPort through the portable drawing boundary.</summary>
    public uint ScrollRaster(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ScrollRaster);
        return state.D[0];
    }

    /// <summary>Scrolls and clears a RastPort through the portable drawing boundary.</summary>
    public uint ScrollRasterBF(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ScrollRasterBF);
        return state.D[0];
    }

    /// <summary>Clears from the current RastPort position to the line end.</summary>
    public uint ClearEOL(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ClearEOL);
        return state.D[0];
    }

    /// <summary>Clears the current RastPort screen through the portable boundary.</summary>
    public uint ClearScreen(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.ClearScreen);
        return state.D[0];
    }

    /// <summary>Erases an inclusive RastPort rectangle through the portable boundary.</summary>
    public uint EraseRect(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.EraseRect);
        return state.D[0];
    }

    /// <summary>Starts or closes an area polygon through the portable boundary.</summary>
    public uint AreaMove(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AreaMove);
        return state.D[0];
    }

    /// <summary>Adds a vertex to the current area polygon through the portable boundary.</summary>
    public uint AreaDraw(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AreaDraw);
        return state.D[0];
    }

    /// <summary>Adds an ellipse item to the current area collector.</summary>
    public uint AreaEllipse(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AreaEllipse);
        return state.D[0];
    }

    /// <summary>Fills and closes the current area polygon through the portable boundary.</summary>
    public uint AreaEnd(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.AreaEnd);
        return state.D[0];
    }

    /// <summary>Flood-fills a RastPort through the portable boundary.</summary>
    public uint Flood(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.Flood);
        return state.D[0];
    }

    /// <summary>Applies a planar stencil/pattern through the portable boundary.</summary>
    public uint BltPattern(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.BltPattern);
        return state.D[0];
    }

    /// <summary>Applies a template blit through the portable RastPort boundary.</summary>
    public uint BltTemplate(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.BltTemplate);
        return state.D[0];
    }

    /// <summary>Clears a guest blit span through the portable memory boundary.</summary>
    public uint BltClear(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.BltClear);
        return state.D[0];
    }

    /// <summary>Copies planar BitMaps through the portable/provider boundary.</summary>
    public uint BltBitMap(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.BltBitMap))
            return state.D[0];

        if (_portableAdapter.IsProviderBitMap(state.A[0]) ||
            _portableAdapter.IsProviderBitMap(state.A[1]))
        {
            state.D[0] = _context.BltBitMap(state);
        }

        return state.D[0];
    }

    /// <summary>Copies between RastPorts through the portable/provider boundary.</summary>
    public uint ClipBlit(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.ClipBlit))
            return state.D[0];

        if (IsDeclinedLayerGatewayRaster(state, GraphicsLvo.ClipBlit))
            return state.D[0];

        if (_portableAdapter.IsProviderRastPort(state.A[0]) ||
            _portableAdapter.IsProviderRastPort(state.A[1]))
        {
            state.D[0] = _context.ClipBlit(state);
        }

        return state.D[0];
    }

    /// <summary>Copies a planar BitMap into a RastPort through the provider boundary.</summary>
    public uint BltBitMapRastPort(M68kCpuState state)
    {
        if (_portableAdapter.TryInvoke(state, (int)GraphicsLvo.BltBitMapRastPort))
            return state.D[0];

        if (IsDeclinedLayerGatewayRaster(state, GraphicsLvo.BltBitMapRastPort))
            return state.D[0];

        if (_portableAdapter.IsProviderBltBitMapRastPort(state.A[0], state.A[1]))
        {
            state.D[0] = _context.BltBitMapRastPort(state);
        }

        return state.D[0];
    }

    private bool IsDeclinedLayerGatewayRaster(
        M68kCpuState state,
        GraphicsLvo lvo)
    {
        if (_layerGateway is null)
            return false;

        var primary = state.A[1];
        if (_portableAdapter.IsLayeredRastPortForProvider(primary))
            return true;

        return lvo == GraphicsLvo.ClipBlit &&
            _portableAdapter.IsLayeredRastPortForProvider(state.A[0]);
    }

    /// <summary>Performs a masked planar bitmap-to-RastPort blit.</summary>
    public uint BltMaskBitMapRastPort(M68kCpuState state)
    {
        _ = _portableAdapter.TryInvoke(state, (int)GraphicsLvo.BltMaskBitMapRastPort);
        return state.D[0];
    }
}
