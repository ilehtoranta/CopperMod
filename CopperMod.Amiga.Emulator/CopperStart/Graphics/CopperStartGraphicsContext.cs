using System;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.CopperStart.Graphics;

/// <summary>
/// Reset-scoped concrete bridge for CopperStart graphics services.
///
/// Ordinary graphics structures (View, ViewPort, RastPort and BitMap) may use
/// <see cref="Memory"/>.  Any operation that reaches the custom chips or changes
/// display scheduling must use its explicit callback instead, retaining normal
/// bus side effects and ordering.
/// </summary>
internal sealed class CopperStartGraphicsContext
{
    public CopperStartGraphicsContext(
        HostGuestMemory memory,
        Action<M68kCpuState>? waitTof,
        Action<uint, ushort, long> writeCustomRegister,
        Action<uint> selectFrontViewPort,
        Action? requestDisplayRebuild,
        Action<uint> initializeCompatibilityViewPort,
        Func<uint, bool> isMappedRastPort,
        Func<uint> ensureCompatibilityFont,
        Action<string, int> logCall,
        Func<M68kCpuState, uint> bltBitMap,
        Func<M68kCpuState, uint> clipBlit,
        Func<M68kCpuState, uint> bltBitMapRastPort,
        Func<M68kCpuState, uint> allocBitMap,
        Action<uint> freeBitMap,
        Func<uint, uint, uint> getBitMapAttr,
        Func<uint, uint, uint> changeViewPortBitMap,
        Func<M68kCpuState, uint> mergeCopperLists,
        Func<M68kCpuState, uint> makeViewPort,
        Action<M68kCpuState>? loadView,
        Action<M68kCpuState> loadRgb4,
        Action<M68kCpuState> setRgb4,
        Func<uint> ensureCompatibilityHostObject,
        Action<M68kCpuState> draw,
        Action<M68kCpuState> text,
        Action<M68kCpuState> setRast,
        Action<M68kCpuState> rectFill,
        Func<long, ushort>? getBeamPosition = null,
        Func<uint, long, long>? waitForViewportBottom = null,
        Func<int, uint, uint>? allocateMemory = null,
        Action<uint, int>? freeMemory = null,
        Func<uint, uint>? getViewPortModeId = null,
        Func<uint, bool>? isRtgBitMap = null,
        Func<uint, bool>? isRtgRastPort = null,
        Action<uint>? freeCprList = null,
        Action<uint>? freeVPortCopLists = null,
        IGraphicsFontListBackend? fontList = null,
        Func<uint, bool>? syncSBitMap = null,
        Func<uint, bool>? copySBitMap = null,
        Func<uint, uint, ushort>? calcIvg = null,
        Func<uint, uint>? setChipRev = null,
        Action<uint, uint, uint, uint, long>? scheduleDoubleBufferMessages = null,
        Action<uint, uint>? drawGList = null,
        Action<uint>? doCollision = null,
        Action<uint, uint, uint>? remIBob = null,
        Action<uint, uint, uint>? dispatchCollision = null,
        Action<uint, ushort, uint>? dispatchBoundary = null,
        Action<uint>? waitForBeginningOfVerticalBlank = null,
        Action<uint, uint, short, short>? moveSprite = null,
        Action<uint, uint, uint>? changeSprite = null,
        Action? ownBlitter = null,
        Action? disownBlitter = null,
        Action? waitBlit = null,
        Action<uint>? submitBlit = null,
        Action<uint, bool, short>? submitQueuedBlit = null,
        Action<uint>? cancelDoubleBufferMessages = null,
        IGraphicsLayerRasterBackend? layerRaster = null,
        Func<M68kCpuState, bool>? tryLoadView = null,
        bool defaultMonitorNtsc = false,
        bool supportsEcsDisplay = true,
        Action<uint>? requestViewportDisplayRebuild = null,
        uint graphicsLibraryBase = 0,
        Action<uint>? lockLayerRom = null,
        Func<uint, bool>? attemptLockLayerRom = null,
        Action<uint>? unlockLayerRom = null,
        Func<uint, bool>? tryFreeCprList = null,
        Func<uint, bool>? tryFreeVPortCopLists = null,
        Action<long>? requestDisplayRebuildTimed = null,
        Action<uint, long>? requestViewportDisplayRebuildTimed = null,
        IGraphicsTransactionalBitMapBackend? transactionalBitMaps = null,
        IGraphicsValidatedLayerRasterBackend? validatedLayerRaster = null,
        Func<bool>? preferProviderBitMapAllocations = null,
        Func<bool>? tryOwnBlitter = null,
        Func<bool>? tryDisownBlitter = null,
        Func<bool>? tryWaitBlit = null,
        Func<uint, bool, short, bool>? trySubmitQueuedBlit = null,
        IGraphicsTimedBlitterBackend? timedBlitter = null,
        Func<uint, uint, short, uint, bool, bool, (bool Handled, int SpriteNumber)>? tryGetExtSprite = null,
        Func<uint, uint, uint, uint, uint, bool, bool, (bool Handled, bool Success)>? tryChangeExtSprite = null,
        Func<uint>? currentTask = null,
        Func<uint, uint, bool>? isDisplayDmaRange = null,
        bool supportsMonitorNameAllocation = false,
        Func<uint, bool>? isCompatibilityLoadView = null,
        Action<M68kCpuState>? setRgb32 = null,
        bool supportsAgaDisplay = false)
    {
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
        WaitTof = waitTof;
        WriteCustomRegister = writeCustomRegister ?? throw new ArgumentNullException(nameof(writeCustomRegister));
        SelectFrontViewPort = selectFrontViewPort ?? throw new ArgumentNullException(nameof(selectFrontViewPort));
        RequestDisplayRebuild = requestDisplayRebuild;
        InitializeCompatibilityViewPort = initializeCompatibilityViewPort ?? throw new ArgumentNullException(nameof(initializeCompatibilityViewPort));
        IsMappedRastPort = isMappedRastPort ?? throw new ArgumentNullException(nameof(isMappedRastPort));
        EnsureCompatibilityFont = ensureCompatibilityFont ?? throw new ArgumentNullException(nameof(ensureCompatibilityFont));
        LogCall = logCall ?? throw new ArgumentNullException(nameof(logCall));
        BltBitMap = bltBitMap ?? throw new ArgumentNullException(nameof(bltBitMap));
        ClipBlit = clipBlit ?? throw new ArgumentNullException(nameof(clipBlit));
        BltBitMapRastPort = bltBitMapRastPort ?? throw new ArgumentNullException(nameof(bltBitMapRastPort));
        AllocBitMap = allocBitMap ?? throw new ArgumentNullException(nameof(allocBitMap));
        FreeBitMap = freeBitMap ?? throw new ArgumentNullException(nameof(freeBitMap));
        GetBitMapAttr = getBitMapAttr ?? throw new ArgumentNullException(nameof(getBitMapAttr));
        ChangeViewPortBitMap = changeViewPortBitMap ?? throw new ArgumentNullException(nameof(changeViewPortBitMap));
        MergeCopperLists = mergeCopperLists ?? throw new ArgumentNullException(nameof(mergeCopperLists));
        MakeViewPort = makeViewPort ?? throw new ArgumentNullException(nameof(makeViewPort));
        LoadView = loadView;
        LoadRgb4 = loadRgb4 ?? throw new ArgumentNullException(nameof(loadRgb4));
        SetRgb4 = setRgb4 ?? throw new ArgumentNullException(nameof(setRgb4));
        EnsureCompatibilityHostObject = ensureCompatibilityHostObject ?? throw new ArgumentNullException(nameof(ensureCompatibilityHostObject));
        Draw = draw ?? throw new ArgumentNullException(nameof(draw));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        SetRast = setRast ?? throw new ArgumentNullException(nameof(setRast));
        RectFill = rectFill ?? throw new ArgumentNullException(nameof(rectFill));
        GetBeamPosition = getBeamPosition;
        WaitForViewportBottom = waitForViewportBottom;
        AllocateMemory = allocateMemory;
        FreeMemory = freeMemory;
        GetViewPortModeId = getViewPortModeId;
        IsRtgBitMap = isRtgBitMap;
        IsRtgRastPort = isRtgRastPort;
        FreeCprList = freeCprList;
        FreeVPortCopLists = freeVPortCopLists;
        FontList = fontList;
        SyncSBitMap = syncSBitMap;
        CopySBitMap = copySBitMap;
        CalcIvg = calcIvg;
        SetChipRev = setChipRev;
        ScheduleDoubleBufferMessages = scheduleDoubleBufferMessages;
        DrawGList = drawGList;
        DoCollision = doCollision;
        RemIBob = remIBob;
        DispatchCollision = dispatchCollision;
        DispatchBoundary = dispatchBoundary;
        WaitForBeginningOfVerticalBlank = waitForBeginningOfVerticalBlank;
        MoveSprite = moveSprite;
        ChangeSprite = changeSprite;
        TryGetExtSprite = tryGetExtSprite;
        TryChangeExtSprite = tryChangeExtSprite;
        OwnBlitter = ownBlitter;
        DisownBlitter = disownBlitter;
        WaitBlit = waitBlit;
        SubmitBlit = submitBlit;
        SubmitQueuedBlit = submitQueuedBlit;
        TryOwnBlitter = tryOwnBlitter;
        TryDisownBlitter = tryDisownBlitter;
        TryWaitBlit = tryWaitBlit;
        TrySubmitQueuedBlit = trySubmitQueuedBlit;
        TimedBlitter = timedBlitter;
        CancelDoubleBufferMessages = cancelDoubleBufferMessages;
        LayerRaster = layerRaster;
        TryLoadView = tryLoadView;
        DefaultMonitorNtsc = defaultMonitorNtsc;
        SupportsEcsDisplay = supportsEcsDisplay;
        RequestViewportDisplayRebuild = requestViewportDisplayRebuild;
        GraphicsLibraryBase = graphicsLibraryBase;
        LockLayerRom = lockLayerRom;
        AttemptLockLayerRom = attemptLockLayerRom;
        UnlockLayerRom = unlockLayerRom;
        TryFreeCprList = tryFreeCprList;
        TryFreeVPortCopLists = tryFreeVPortCopLists;
        RequestDisplayRebuildTimed = requestDisplayRebuildTimed;
        RequestViewportDisplayRebuildTimed = requestViewportDisplayRebuildTimed;
        TransactionalBitMaps = transactionalBitMaps;
        ValidatedLayerRaster = validatedLayerRaster;
        PreferProviderBitMapAllocations = preferProviderBitMapAllocations;
        CurrentTask = currentTask;
        IsDisplayDmaRange = isDisplayDmaRange;
        SupportsMonitorNameAllocation = supportsMonitorNameAllocation;
        IsCompatibilityLoadView = isCompatibilityLoadView;
        SetRgb32 = setRgb32;
        SupportsAgaDisplay = supportsAgaDisplay;
    }

    public HostGuestMemory Memory { get; }
    public Action<M68kCpuState>? WaitTof { get; }
    public Action<uint, ushort, long> WriteCustomRegister { get; }
    public Action<uint> SelectFrontViewPort { get; }
    public Action? RequestDisplayRebuild { get; }
    public Action<uint> InitializeCompatibilityViewPort { get; }
    public Func<uint, bool> IsMappedRastPort { get; }
    public Func<uint> EnsureCompatibilityFont { get; }
    public Action<string, int> LogCall { get; }
    public Func<M68kCpuState, uint> BltBitMap { get; }
    public Func<M68kCpuState, uint> ClipBlit { get; }
    public Func<M68kCpuState, uint> BltBitMapRastPort { get; }
    public Func<M68kCpuState, uint> AllocBitMap { get; }
    public Action<uint> FreeBitMap { get; }
    public Func<uint, uint, uint> GetBitMapAttr { get; }
    public Func<uint, uint, uint> ChangeViewPortBitMap { get; }
    public Func<M68kCpuState, uint> MergeCopperLists { get; }
    public Func<M68kCpuState, uint> MakeViewPort { get; }
    public Action<M68kCpuState>? LoadView { get; }
    public Action<M68kCpuState> LoadRgb4 { get; }
    public Action<M68kCpuState> SetRgb4 { get; }
    /// <summary>
    /// Optional host path that consumes the eight significant component bits
    /// supplied by RGB32 graphics vectors. A missing callback deliberately
    /// retains the RGB4 fallback for OCS/ECS-compatible hosts.
    /// </summary>
    public Action<M68kCpuState>? SetRgb32 { get; }
    public Func<uint> EnsureCompatibilityHostObject { get; }
    public Action<M68kCpuState> Draw { get; }
    public Action<M68kCpuState> Text { get; }
    public Action<M68kCpuState> SetRast { get; }
    public Action<M68kCpuState> RectFill { get; }
    public Func<long, ushort>? GetBeamPosition { get; }
    public Func<uint, long, long>? WaitForViewportBottom { get; }
    public Func<int, uint, uint>? AllocateMemory { get; }
    public Action<uint, int>? FreeMemory { get; }
    public Func<uint, uint>? GetViewPortModeId { get; }
    public Func<uint, bool>? IsRtgBitMap { get; }
    public Func<uint, bool>? IsRtgRastPort { get; }
    public Action<uint>? FreeCprList { get; }
    public Action<uint>? FreeVPortCopLists { get; }
    public IGraphicsFontListBackend? FontList { get; }
    public Func<uint, bool>? SyncSBitMap { get; }
    public Func<uint, bool>? CopySBitMap { get; }
    public Func<uint, uint, ushort>? CalcIvg { get; }
    public Func<uint, uint>? SetChipRev { get; }
    public Action<uint, uint, uint, uint, long>? ScheduleDoubleBufferMessages { get; }
    public Action<uint, uint>? DrawGList { get; }
    public Action<uint>? DoCollision { get; }
    public Action<uint, uint, uint>? RemIBob { get; }
    public Action<uint, uint, uint>? DispatchCollision { get; }
    public Action<uint, ushort, uint>? DispatchBoundary { get; }
    public Action<uint>? WaitForBeginningOfVerticalBlank { get; }
    public Action<uint, uint, short, short>? MoveSprite { get; }
    public Action<uint, uint, uint>? ChangeSprite { get; }
    public Func<uint, uint, short, uint, bool, bool, (bool Handled, int SpriteNumber)>? TryGetExtSprite { get; }
    public Func<uint, uint, uint, uint, uint, bool, bool, (bool Handled, bool Success)>? TryChangeExtSprite { get; }
    public Action? OwnBlitter { get; }
    public Action? DisownBlitter { get; }
    public Action? WaitBlit { get; }
    public Action<uint>? SubmitBlit { get; }
    public Action<uint, bool, short>? SubmitQueuedBlit { get; }
    /// <summary>
    /// Optional status-aware blitter callbacks.  A false result leaves the
    /// graphics vector unclaimed so a native/provider owner can handle
    /// contention instead of observing a fabricated success.
    /// </summary>
    public Func<bool>? TryOwnBlitter { get; }
    public Func<bool>? TryDisownBlitter { get; }
    public Func<bool>? TryWaitBlit { get; }
    public Func<uint, bool, short, bool>? TrySubmitQueuedBlit { get; }
    /// <summary>
    /// Optional scheduler-owned FIFO. It is kept as an explicit sidecar so a
    /// future CopperSharp68k host can provide queue timing without changing
    /// the classic callback-shaped construction path.
    /// </summary>
    public IGraphicsTimedBlitterBackend? TimedBlitter { get; }
    public Action<uint>? CancelDoubleBufferMessages { get; }
    public IGraphicsLayerRasterBackend? LayerRaster { get; }
    public Func<M68kCpuState, bool>? TryLoadView { get; }
    public bool DefaultMonitorNtsc { get; }
    public bool SupportsEcsDisplay { get; }
    public bool SupportsAgaDisplay { get; }
    public Func<uint>? CurrentTask { get; }
    /// <summary>
    /// Optional native display-DMA address classifier. A missing callback
    /// keeps the memory-only CopperSharp68k compatibility path on its
    /// portable mapping/alignment rule.
    /// </summary>
    public Func<uint, uint, bool>? IsDisplayDmaRange { get; }
    public bool SupportsMonitorNameAllocation { get; }
    /// <summary>
    /// Optional host-owner classifier for generated compatibility copper.
    /// Native/provider Views remain outside this callback and continue through
    /// their original vector or explicit RTG owner.
    /// </summary>
    public Func<uint, bool>? IsCompatibilityLoadView { get; }
    public uint GraphicsLibraryBase { get; }
    public Action<uint>? LockLayerRom { get; }
    public Func<uint, bool>? AttemptLockLayerRom { get; }
    public Action<uint>? UnlockLayerRom { get; }
    public Func<uint, bool>? TryFreeCprList { get; }
    public Func<uint, bool>? TryFreeVPortCopLists { get; }

    /// <summary>
    /// Optional cycle-aware complete display rebuild boundary.  Timed
    /// graphics vectors must publish against the guest call cycle rather than
    /// the host CPU state's last independently observed cycle.
    /// </summary>
    public Action<long>? RequestDisplayRebuildTimed { get; }

    /// <summary>
    /// Optional viewport-aware display rebuild boundary.  ScrollVPort changes
    /// the copper interpretation of one viewport's RasInfo offsets; a host
    /// implementation may need that address to rebuild only the affected
    /// display chain.  The legacy parameterless callback remains the fallback
    /// for callers that rebuild the complete active view.
    /// </summary>
    public Action<uint>? RequestViewportDisplayRebuild { get; }

    /// <summary>Cycle-aware counterpart to <see cref="RequestViewportDisplayRebuild"/>.</summary>
    public Action<uint, long>? RequestViewportDisplayRebuildTimed { get; }

    public IGraphicsTransactionalBitMapBackend? TransactionalBitMaps { get; }

    public IGraphicsValidatedLayerRasterBackend? ValidatedLayerRaster { get; }

    /// <summary>
    /// Reports whether the active graphics provider must see friendless
    /// AllocBitMap calls before the portable planar allocator.  RTG-enabled
    /// CopperStart boots use this boundary so display-class surfaces retain
    /// their provider-owned storage and format contract even when A0 is null.
    /// </summary>
    public Func<bool>? PreferProviderBitMapAllocations { get; }
}
