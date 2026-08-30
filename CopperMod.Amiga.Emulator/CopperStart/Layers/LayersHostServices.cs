using System;
using System.Collections.Generic;
using Amiga;
using Copper68k;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using PortableLayers = CopperStart.Layers;

namespace CopperMod.Amiga.CopperStart.Layers;

/// <summary>
/// Reset-scoped MedPlayer owner for the portable CopperStart Layers state.
/// The service is deliberately dormant until <see cref="TryInstall"/> is
/// called by the CopperStart host-shim boot path.
/// </summary>
internal sealed partial class LayersHostServices :
    IGraphicsLayerBackend,
    IGraphicsLayerRasterBackend,
    IGraphicsLayerGatewayBackend,
    IDisposable
{
    internal const uint HookContinuationAddress = 0x00F0_8C00;
    internal const uint BlockContinuationAddress = 0x00F0_8C10;

    private const short OpenLvo = -6;
    private const short CloseLvo = -12;
    private const short ExpungeLvo = -18;
    private const short ReservedLvo = -24;
    private const sbyte LibraryPriority = 0;
    private const uint OpaqueAllocationMagic = 0x4353_4F50; // "CSOP"
    private const uint OpaqueAllocationHeaderSize = 16;
    // Matches the portable/native resource bound. A maximum 4,096-layer
    // topology can retain one owned Damage Region per layer while a mutation
    // simultaneously owns replacement regions, a partition transaction, a
    // pixel plan, and four workspaces.
    private const int MaximumOpaqueAllocations = 16_384;

    private static readonly short[] ClassicLvos =
    [
        LayersLvo.InitLayers,
        LayersLvo.CreateUpfrontLayer,
        LayersLvo.CreateBehindLayer,
        LayersLvo.UpfrontLayer,
        LayersLvo.BehindLayer,
        LayersLvo.MoveLayer,
        LayersLvo.SizeLayer,
        LayersLvo.ScrollLayer,
        LayersLvo.BeginUpdate,
        LayersLvo.EndUpdate,
        LayersLvo.DeleteLayer,
        LayersLvo.LockLayer,
        LayersLvo.UnlockLayer,
        LayersLvo.LockLayers,
        LayersLvo.UnlockLayers,
        LayersLvo.LockLayerInfo,
        LayersLvo.SwapBitsRastPortClipRect,
        LayersLvo.WhichLayer,
        LayersLvo.UnlockLayerInfo,
        LayersLvo.NewLayerInfo,
        LayersLvo.DisposeLayerInfo,
        LayersLvo.FattenLayerInfo,
        LayersLvo.ThinLayerInfo,
        LayersLvo.MoveLayerInFrontOf,
        LayersLvo.InstallClipRegion,
        LayersLvo.MoveSizeLayer,
        LayersLvo.CreateUpfrontHookLayer,
        LayersLvo.CreateBehindHookLayer,
        LayersLvo.InstallLayerHook,
        LayersLvo.InstallLayerInfoHook,
        LayersLvo.SortLayerCR,
        LayersLvo.DoHookClipRects
    ];

    private static readonly short[] MorphOsLvos =
    [
        LayersLvo.CreateUpfrontLayerTagList,
        LayersLvo.CreateBehindLayerTagList,
        LayersLvo.WhichLayerBehindLayer,
        LayersLvo.IsLayerVisible,
        LayersLvo.RenderLayerInfoTagList,
        LayersLvo.LockLayerUpdates,
        LayersLvo.UnlockLayerUpdates,
        LayersLvo.IsVisibleInLayer,
        LayersLvo.IsLayerHitable
    ];

    private readonly HostGuestMemory _memory;
    private readonly CopperStartGraphicsContext _graphics;
    private readonly CopperStartGraphicsMemoryAdapter _graphicsMemory;
    private readonly CopperStartGraphicsAllocator _graphicsAllocator;
    private readonly List<uint> _providerSnapshotAddresses = new();
    private readonly List<byte> _providerSnapshotValues = new();
    private readonly List<uint> _providerNestedSnapshotAddresses = new();
    private readonly List<byte> _providerNestedSnapshotValues = new();
    // Provider calls are synchronous and the Layers host is single-dispatch.
    // Reusing one reset-scoped register frame avoids allocating D/A arrays on
    // RTG allocation and fallback copy paths without letting the frame escape.
    private readonly M68kCpuState _providerGraphicsState = new();
    private readonly Func<int, uint, uint> _allocate;
    private readonly Action<uint, int> _free;
    private readonly Func<uint> _getExecBase;
    private readonly Func<uint> _getCurrentTask;
    private readonly Func<M68kCpuState, uint, bool> _suspendTask;
    private readonly Action<uint> _wakeTask;
    private readonly Action<M68kCpuState, uint, uint> _startGuestSubroutine;
    private readonly List<(uint Address, uint Token)> _gateways = new();
    private readonly Dictionary<uint, PendingWait> _pendingWaits = new();
    // Opaque retained plans are guest allocations, but their release extent
    // must never be trusted back to guest-writable metadata. Keep one bounded
    // reset-scoped host ledger: its backing array is allocated at controller
    // construction and every steady Allocate/Is/Free operation is allocation
    // free. The guest prefix below is diagnostic only.
    private readonly OpaqueAllocationEntry[] _opaqueAllocations =
        new OpaqueAllocationEntry[MaximumOpaqueAllocations];
    private readonly int[] _opaqueFreeSlots =
        new int[MaximumOpaqueAllocations];
    private readonly int[] _opaqueHashSlots =
        new int[MaximumOpaqueAllocations * 2];
    private int _opaqueAllocationCount;
    private int _opaqueAllocationHighWater;
    private int _opaqueFreeHead = -1;

    private uint _allocation;
    private uint _allocationSize;
    private uint _root;
    private uint _libraryBase;
    private bool _linked;
    private bool _active;
    private M68kCpuState? _dispatchState;
    private short _dispatchLvo;
    private PortableLayers.LayersRegisterFrame _dispatchOriginalFrame;
    private bool _parkAccepted;
    private PendingCallback _pendingCallback;

    private enum PendingWaitKind : byte
    {
        LayersVector,
        GraphicsCompanion,
        Raster
    }

    private readonly record struct PendingWait(
        short Lvo,
        uint Token,
        PortableLayers.LayersRegisterFrame Frame,
        PendingWaitKind Kind,
        uint PrimaryRastPort,
        uint SecondaryRastPort);

    private readonly record struct PendingCallback(
        short Lvo,
        uint Token,
        PortableLayers.LayersRegisterFrame Frame)
    {
        public bool IsActive => Token != 0;
    }

    private readonly record struct OpaqueAllocationEntry(
        uint Allocation,
        uint Payload,
        uint PayloadSize,
        uint TotalSize)
    {
        public bool IsActive => Payload != 0;
    }

    internal LayersHostServices(
        HostGuestMemory memory,
        CopperStartGraphicsContext graphics,
        Func<int, uint, uint> allocate,
        Action<uint, int> free,
        Func<uint> getExecBase,
        Func<uint> getCurrentTask,
        Func<M68kCpuState, uint, bool> suspendTask,
        Action<uint> wakeTask,
        Action<M68kCpuState, uint, uint> startGuestSubroutine)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _graphicsMemory = new CopperStartGraphicsMemoryAdapter(_memory);
        _graphicsAllocator = new CopperStartGraphicsAllocator(_graphics);
        _allocate = allocate ?? throw new ArgumentNullException(nameof(allocate));
        _free = free ?? throw new ArgumentNullException(nameof(free));
        _getExecBase = getExecBase ?? throw new ArgumentNullException(nameof(getExecBase));
        _getCurrentTask = getCurrentTask ?? throw new ArgumentNullException(nameof(getCurrentTask));
        _suspendTask = suspendTask ?? throw new ArgumentNullException(nameof(suspendTask));
        _wakeTask = wakeTask ?? throw new ArgumentNullException(nameof(wakeTask));
        _startGuestSubroutine = startGuestSubroutine ?? throw new ArgumentNullException(nameof(startGuestSubroutine));
    }

    private M68kCpuState ResetProviderGraphicsState()
    {
        Array.Clear(_providerGraphicsState.D);
        Array.Clear(_providerGraphicsState.A);
        _providerGraphicsState.ProgramCounter = 0;
        _providerGraphicsState.StatusRegister = M68kCpuState.Supervisor;
        _providerGraphicsState.Cycles = 0;
        _providerGraphicsState.NativeCycles = 0;
        _providerGraphicsState.Halted = false;
        _providerGraphicsState.Stopped = false;
        return _providerGraphicsState;
    }

    internal bool IsInstalled => _active;
    internal uint LibraryBase => _active ? _libraryBase : 0;
    internal uint RootAddress => _active ? _root : 0;
    internal int GatewayRegistrationCount => _gateways.Count;
    internal int OpaqueAllocationCountForTest => _opaqueAllocationCount;
    internal int OpaqueAllocationCapacityForTest => MaximumOpaqueAllocations;
    internal int PendingWaitCountForTest => _pendingWaits.Count;
    internal PortableLayers.LayersAbiProfile Profile { get; private set; }

    internal uint AllocateOpaqueForTest(uint byteSize)
    {
        var platform = CreatePlatform();
        return platform.AllocateOpaque(
            byteSize,
            global::Amiga.Exec.MemoryFlags.Public |
                global::Amiga.Exec.MemoryFlags.Clear).Raw;
    }

    internal bool IsOpaqueAllocationForTest(uint address, uint expectedSize)
    {
        var platform = CreatePlatform();
        return platform.IsOpaqueAllocation(
            APTR.FromPointer(address),
            expectedSize);
    }

    internal void FreeOpaqueForTest(uint address)
    {
        var platform = CreatePlatform();
        platform.FreeOpaque(APTR.FromPointer(address));
    }

    /// <summary>
    /// Installs a complete selected-profile gateway table and publishes its
    /// real guest Library node. Publication is last, after both allocations,
    /// the private root, vector handlers, and list links are valid. Normal
    /// CopperStart callers omit <paramref name="profile"/> and therefore get
    /// the additive MorphOS m68k surface; Unified is retained for
    /// differential qualification only.
    /// </summary>
    internal bool TryInstall(
        uint execBase,
        PortableLayers.LayersAbiProfile profile =
            PortableLayers.LayersAbiProfile.Unified)
    {
        if (_active)
            return execBase == _getExecBase() && profile == Profile;
        if (_allocation != 0 || _root != 0 || _gateways.Count != 0 ||
            execBase == 0 || execBase != _getExecBase() ||
            profile is not PortableLayers.LayersAbiProfile.Unified and
                not PortableLayers.LayersAbiProfile.Unified)
        {
            return false;
        }

        var negativeSize = profile == PortableLayers.LayersAbiProfile.Unified
            ? checked((uint)-LayersLvo.IsLayerHitable)
            : checked((uint)-LayersLvo.DoHookClipRects);
        const string name = "layers.library";
        var id = profile == PortableLayers.LayersAbiProfile.Unified
            ? "CopperStart layers.library 52.0 (MorphOS m68k ABI)"
            : "CopperStart layers.library 40.0";
        var positiveSize = checked(global::Amiga.Library.Size +
            (uint)name.Length + 1u + (uint)id.Length + 1u);
        var allocationSize = checked(negativeSize + positiveSize);
        var allocation = _allocate(checked((int)allocationSize),
            (uint)(global::Amiga.Exec.MemoryFlags.Public |
                global::Amiga.Exec.MemoryFlags.Clear));
        if (allocation == 0)
            return false;
        var root = _allocate(checked((int)PortableLayers.LayersPrivateRootCore.Size),
            (uint)(global::Amiga.Exec.MemoryFlags.Public |
                global::Amiga.Exec.MemoryFlags.Clear));
        if (root == 0)
        {
            _free(allocation, checked((int)allocationSize));
            return false;
        }

        _allocation = allocation;
        _allocationSize = allocationSize;
        _libraryBase = checked(allocation + negativeSize);
        _root = root;
        Profile = profile;

        try
        {
            var platform = CreatePlatform();
            if (!PortableLayers.LayersPrivateRootCore.Initialize(
                    ref platform,
                    APTR.FromPointer(_root),
                    APTR.FromPointer(_libraryBase),
                    APTR.FromPointer(execBase),
                    profile) ||
                !InitializeLibrary(negativeSize, positiveSize, name, id) ||
                !RegisterSelectedProfile(profile) ||
                !LinkLibrary(execBase))
            {
                ResetPartialInstallation();
                return false;
            }

            _active = true;
            return true;
        }
        catch
        {
            ResetPartialInstallation();
            return false;
        }
    }

    internal M68kHostGatewayResult Invoke(M68kCpuState state, short lvo)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_active || !PortableLayers.LayersVectorManifest.TryDescribe(lvo, Profile, out _))
            return M68kHostGatewayResult.Completed;

        var frame = Capture(state);
        var original = frame;
        var platform = CreatePlatform(state);
        _dispatchState = state;
        _dispatchLvo = lvo;
        _dispatchOriginalFrame = original;
        _parkAccepted = false;
        var result = PortableLayers.LayersVectorRouter.Dispatch(
            ref platform,
            APTR.FromPointer(_root),
            lvo,
            ref frame);
        _dispatchState = null;

        return CompletePortableDispatch(
            state,
            lvo,
            original,
            frame,
            result,
            PendingWaitKind.LayersVector);
    }

    public bool TryInvokeGraphicsRaster(
        M68kCpuState state,
        GraphicsLvo lvo,
        out M68kHostGatewayResult gatewayResult)
    {
        ArgumentNullException.ThrowIfNull(state);
        gatewayResult = M68kHostGatewayResult.Completed;

        // Text with a zero ULONG count is the portable graphics contract's
        // empty-string no-op.  Claim it before resolving a layer endpoint or
        // entering RasterCore: the call has no guest-memory or layer state
        // dependency, so malformed text/RastPort pointers must not force a
        // provider traversal or a native fallback.  Keep D0 normalized at
        // this scheduler-aware boundary just as GraphicsServices does for
        // the ordinary register adapter.
        if (lvo == GraphicsLvo.Text && state.D[0] == 0)
        {
            state.D[0] = 0;
            return true;
        }

        if (!_active ||
            !PortableLayers.LayersRasterCore.IsSupported((short)lvo) ||
            _rasterPrimaryRastPort != 0 ||
            !TryResolveGatewayRasterEndpoints(
                state,
                lvo,
                out var primaryRastPort,
                out var secondaryRastPort))
        {
            return false;
        }

        var frame = Capture(state);
        var original = frame;
        var platform = CreatePlatform(state);
        PortableLayers.LayersGatewayResult result;
        _dispatchState = state;
        _dispatchLvo = (short)lvo;
        _dispatchOriginalFrame = original;
        _parkAccepted = false;
        _rasterPrimaryRastPort = primaryRastPort;
        _rasterSecondaryRastPort = secondaryRastPort;
        try
        {
            result = secondaryRastPort == 0
                ? PortableLayers.LayersRasterCore.Dispatch(
                    ref platform,
                    APTR.FromPointer(_root),
                    (short)lvo,
                    APTR.FromPointer(primaryRastPort),
                    ref frame)
                : PortableLayers.LayersRasterCore.Dispatch(
                    ref platform,
                    APTR.FromPointer(_root),
                    (short)lvo,
                    APTR.FromPointer(primaryRastPort),
                    APTR.FromPointer(secondaryRastPort),
                    ref frame);
        }
        finally
        {
            _rasterPrimaryRastPort = 0;
            _rasterSecondaryRastPort = 0;
            _dispatchState = null;
        }

        gatewayResult = CompletePortableDispatch(
            state,
            (short)lvo,
            original,
            frame,
            result,
            PendingWaitKind.Raster,
            primaryRastPort,
            secondaryRastPort);
        return true;
    }

    private bool TryResolveGatewayRasterEndpoints(
        M68kCpuState state,
        GraphicsLvo lvo,
        out uint primaryRastPort,
        out uint secondaryRastPort)
    {
        primaryRastPort = 0;
        secondaryRastPort = 0;
        if (lvo == GraphicsLvo.ClipBlit)
        {
            var source = state.A[0];
            var destination = state.A[1];
            var sourceLayered = OwnsLayeredRastPort(source);
            var destinationLayered = OwnsLayeredRastPort(destination);
            if (!sourceLayered && !destinationLayered)
                return false;

            primaryRastPort = destinationLayered ? destination : source;
            if (sourceLayered && destinationLayered && source != destination)
                secondaryRastPort = source;
            return true;
        }

        var rastPort = lvo is GraphicsLvo.ReadPixelLine8 or
            GraphicsLvo.WritePixelLine8 or GraphicsLvo.ReadPixelArray8 or
            GraphicsLvo.WritePixelArray8 or GraphicsLvo.WriteChunkyPixels
                ? state.A[0]
                : state.A[1];
        if (!OwnsLayeredRastPort(rastPort))
            return false;
        primaryRastPort = rastPort;
        return true;
    }

    public bool TryInvokeGraphicsCompanion(
        M68kCpuState state,
        GraphicsLvo lvo,
        out M68kHostGatewayResult gatewayResult)
    {
        ArgumentNullException.ThrowIfNull(state);
        gatewayResult = M68kHostGatewayResult.Completed;
        if (!_active)
            return false;

        var layer = APTR.FromPointer(lvo is GraphicsLvo.LockLayerRom or
            GraphicsLvo.AttemptLockLayerRom or GraphicsLvo.UnlockLayerRom
                ? state.A[5]
                : state.A[0]);
        var platform = CreatePlatform(state);
        if (!PortableLayers.LayersLayerCore.TryGetDescriptor(
                ref platform,
                APTR.FromPointer(_root),
                layer,
                out _))
        {
            // A live CopperStart Layers service owns only records published
            // below its private root.  Foreign/native layers must remain
            // available to their original graphics or ROM provider.
            return false;
        }
        switch (lvo)
        {
            case GraphicsLvo.LockLayerRom:
            {
                var original = Capture(state);
                _dispatchState = state;
                _dispatchLvo = (short)lvo;
                _dispatchOriginalFrame = original;
                _parkAccepted = false;
                var result = PortableLayers.LayersLayerCore.Lock(
                    ref platform,
                    APTR.FromPointer(_root),
                    layer);
                _dispatchState = null;
                gatewayResult = CompletePortableDispatch(
                    state,
                    (short)lvo,
                    original,
                    original,
                    result,
                    PendingWaitKind.GraphicsCompanion);
                return true;
            }
            case GraphicsLvo.AttemptLockLayerRom:
                state.D[0] = PortableLayers.LayersLayerCore.AttemptLock(
                    ref platform,
                    APTR.FromPointer(_root),
                    layer) ? 1u : 0u;
                return true;
            case GraphicsLvo.UnlockLayerRom:
                _ = PortableLayers.LayersLayerCore.Unlock(
                    ref platform,
                    APTR.FromPointer(_root),
                    layer);
                state.D[0] = 0;
                return true;
            case GraphicsLvo.SyncSBitMap:
                _ = PortableLayers.LayersBitmapCore.SyncSBitMap(
                    ref platform,
                    APTR.FromPointer(_root),
                    layer);
                state.D[0] = 0;
                return true;
            case GraphicsLvo.CopySBitMap:
                _ = PortableLayers.LayersBitmapCore.CopySBitMap(
                    ref platform,
                    APTR.FromPointer(_root),
                    layer);
                state.D[0] = 0;
                return true;
            default:
                return false;
        }
    }

    internal M68kHostGatewayResult ContinueBlocked(M68kCpuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var task = _getCurrentTask();
        if (task == 0 || !_pendingWaits.Remove(task, out var pending) || !_active)
            return M68kHostGatewayResult.Completed;

        // The blocked-task gateway is a second host call layered on top of
        // the original graphics/layers vector.  A completed resume must
        // return through this gateway's own post-token PC so the CPU performs
        // the normal RTS and consumes the original caller's link. Applying
        // the saved vector frame's PC here would leave A7 pointing at that
        // link while execution continues at the original vector stub,
        // producing a subtly truncated guest frame.
        var continuationReturnProgramCounter = state.ProgramCounter;
        var frame = pending.Frame;
        var platform = CreatePlatform(state);
        PortableLayers.LayersGatewayResult result;
        _dispatchState = state;
        _dispatchLvo = pending.Lvo;
        _dispatchOriginalFrame = pending.Frame;
        _parkAccepted = false;
        if (pending.Kind == PendingWaitKind.GraphicsCompanion)
        {
            result = PortableLayers.LayersLayerCore.Lock(
                ref platform,
                APTR.FromPointer(_root),
                APTR.FromPointer(pending.Frame.A5),
                pending.Token);
        }
        else if (pending.Kind == PendingWaitKind.Raster)
        {
            _rasterPrimaryRastPort = pending.PrimaryRastPort;
            _rasterSecondaryRastPort = pending.SecondaryRastPort;
            try
            {
                result = pending.SecondaryRastPort == 0
                    ? PortableLayers.LayersRasterCore.Dispatch(
                        ref platform,
                        APTR.FromPointer(_root),
                        pending.Lvo,
                        APTR.FromPointer(pending.PrimaryRastPort),
                        ref frame,
                        pending.Token)
                    : PortableLayers.LayersRasterCore.Dispatch(
                        ref platform,
                        APTR.FromPointer(_root),
                        pending.Lvo,
                        APTR.FromPointer(pending.PrimaryRastPort),
                        APTR.FromPointer(pending.SecondaryRastPort),
                        ref frame,
                        pending.Token);
            }
            finally
            {
                _rasterPrimaryRastPort = 0;
                _rasterSecondaryRastPort = 0;
            }
        }
        else
        {
            result = PortableLayers.LayersVectorRouter.Dispatch(
                ref platform,
                APTR.FromPointer(_root),
                pending.Lvo,
                ref frame,
                pending.Token);
        }
        _dispatchState = null;
        var completed = CompletePortableDispatch(
            state,
            pending.Lvo,
            pending.Frame,
            frame,
            result,
            pending.Kind,
            pending.PrimaryRastPort,
            pending.SecondaryRastPort);
        if (completed == M68kHostGatewayResult.Completed &&
            result.Disposition ==
                PortableLayers.LayersGatewayDisposition.Completed &&
            result.ContinuationToken == 0)
        {
            state.ProgramCounter = continuationReturnProgramCounter;
        }

        return completed;
    }

    internal M68kHostGatewayResult ContinueHook(M68kCpuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_active || !_pendingCallback.IsActive)
            return M68kHostGatewayResult.Completed;

        // ContinueHook is itself executing inside the six-byte continuation
        // gateway.  A completed portable resume must leave this gateway's
        // post-token PC intact so the CPU performs its normal stack return.
        // Applying the original Layers vector PC here would fall through into
        // the neighboring vector after the last callback.
        var continuationReturnProgramCounter = state.ProgramCounter;
        var pending = _pendingCallback;
        _pendingCallback = default;
        var frame = pending.Frame;
        var platform = CreatePlatform(state);
        _dispatchState = state;
        _dispatchLvo = pending.Lvo;
        _dispatchOriginalFrame = pending.Frame;
        var result = PortableLayers.LayersVectorRouter.Dispatch(
            ref platform,
            APTR.FromPointer(_root),
            pending.Lvo,
            ref frame,
            pending.Token,
            callbackSucceeded: true);
        _dispatchState = null;
        var completed = CompletePortableDispatch(
            state,
            pending.Lvo,
            pending.Frame,
            frame,
            result,
            PendingWaitKind.LayersVector);
        if (result.Disposition ==
            PortableLayers.LayersGatewayDisposition.Completed)
        {
            state.ProgramCounter = continuationReturnProgramCounter;
        }
        return completed;
    }

    public void Reset()
    {
        var lifecycleComplete = _root == 0;
        if (_root != 0 && _memory.IsMapped(_root,
                checked((int)PortableLayers.LayersPrivateRootCore.Size)))
        {
            var platform = CreatePlatform();
            lifecycleComplete = PortableLayers.LayersLifecycleCore.Reset(
                ref platform,
                APTR.FromPointer(_root));
        }
        _active = false;
        ResetPixelTransactions();
        _pixelBlitScratch.Reset();
        _providerSnapshotAddresses.Clear();
        _providerSnapshotValues.Clear();
        _providerNestedSnapshotAddresses.Clear();
        _providerNestedSnapshotValues.Clear();
        _providerSnapshotAddresses.TrimExcess();
        _providerSnapshotValues.TrimExcess();
        _providerNestedSnapshotAddresses.TrimExcess();
        _providerNestedSnapshotValues.TrimExcess();
        _pendingCallback = default;
        ResetRasterProviderEvidenceForTest();

        foreach (var task in _pendingWaits.Keys)
            _wakeTask(task);
        _pendingWaits.Clear();

        if (lifecycleComplete)
        {
            ReleaseAllPortableLayersOpaqueAllocations();
            ResetPartialInstallation();
        }
        else
        {
            // Preserve the complete allocation/root pair for a later
            // lifecycle retry.  Freeing either one while an owned record or
            // provider resource remains would turn a teardown failure into a
            // guest use-after-free.
            UnlinkLibrary();
            RemoveGatewayRegistrations();
            _dispatchState = null;
            _dispatchLvo = 0;
            _dispatchOriginalFrame = default;
            _parkAccepted = false;
        }
        Profile = PortableLayers.LayersAbiProfile.Unified;
    }

    internal bool TaskDied(uint task)
    {
        if (task == 0)
            return false;

        _pendingWaits.Remove(task);
        if (!_active || _root == 0 || !_memory.IsMapped(
                _root,
                checked((int)PortableLayers.LayersPrivateRootCore.Size)))
        {
            return true;
        }

        var platform = CreatePlatform();
        return PortableLayers.LayersLifecycleCore.TaskDied(
            ref platform,
            APTR.FromPointer(_root),
            APTR.FromPointer(task));
    }

    public void Dispose() => Reset();

    public void Lock(uint layerAddress)
    {
        if (!_active)
            return;
        var platform = CreatePlatform();
        _ = PortableLayers.LayersLayerCore.Lock(
            ref platform,
            APTR.FromPointer(_root),
            APTR.FromPointer(layerAddress));
    }

    public bool TryLock(uint layerAddress)
    {
        if (!_active)
            return false;
        var platform = CreatePlatform();
        return PortableLayers.LayersLayerCore.AttemptLock(
            ref platform,
            APTR.FromPointer(_root),
            APTR.FromPointer(layerAddress));
    }

    public void Unlock(uint layerAddress)
    {
        if (!_active)
            return;
        var platform = CreatePlatform();
        _ = PortableLayers.LayersLayerCore.Unlock(
            ref platform,
            APTR.FromPointer(_root),
            APTR.FromPointer(layerAddress));
    }

    public bool SyncSuperBitMap(uint layerAddress)
    {
        if (!_active)
            return false;
        var platform = CreatePlatform();
        return PortableLayers.LayersBitmapCore.SyncSBitMap(
            ref platform,
            APTR.FromPointer(_root),
            APTR.FromPointer(layerAddress));
    }

    public bool CopySuperBitMap(uint layerAddress)
    {
        if (!_active)
            return false;
        var platform = CreatePlatform();
        return PortableLayers.LayersBitmapCore.CopySBitMap(
            ref platform,
            APTR.FromPointer(_root),
            APTR.FromPointer(layerAddress));
    }

    private M68kHostGatewayResult CompletePortableDispatch(
        M68kCpuState state,
        short lvo,
        PortableLayers.LayersRegisterFrame original,
        PortableLayers.LayersRegisterFrame frame,
        PortableLayers.LayersGatewayResult result,
        PendingWaitKind kind,
        uint primaryRastPort = 0,
        uint secondaryRastPort = 0)
    {
        var graphicsCompanion = kind == PendingWaitKind.GraphicsCompanion;
        switch (result.Disposition)
        {
            case PortableLayers.LayersGatewayDisposition.Completed:
                if (result.ContinuationToken != 0)
                {
                    var task = _getCurrentTask();
                    if (task != 0 && _suspendTask(
                            state,
                            BlockContinuationAddress))
                    {
                        _pendingWaits[task] = new PendingWait(
                            lvo,
                            result.ContinuationToken,
                            original,
                            kind,
                            primaryRastPort,
                            secondaryRastPort);
                        // Cleanup retry has no external lock owner which will
                        // wake it. Make the suspended task ready immediately;
                        // the actual re-entry still occurs only at the outer
                        // instruction/scheduler boundary, never recursively.
                        _wakeTask(task);
                        return M68kHostGatewayResult.BlockCurrentTask;
                    }

                    // Returning through the guest vector while a portable
                    // operation still owns guards would strand them forever.
                    // Preserve the incoming frame and keep the call at the
                    // host boundary if the scheduler cannot accept the retry.
                    Apply(state, original);
                    return M68kHostGatewayResult.BlockCurrentTask;
                }
                Apply(state, graphicsCompanion ? original : frame);
                // ClipBlit is a documented void graphics.library vector.
                // Its validated Layer provider uses D0 internally to report
                // whether it copied any pixels, but that implementation
                // detail must not cross the host/68k ABI boundary.
                if (graphicsCompanion ||
                    (kind == PendingWaitKind.Raster &&
                        lvo == (short)GraphicsLvo.ClipBlit))
                    state.D[0] = 0;
                return M68kHostGatewayResult.Completed;
            case PortableLayers.LayersGatewayDisposition.BlockCurrentTask:
            {
                var task = _getCurrentTask();
                if (!_parkAccepted || task == 0 || result.ContinuationToken == 0)
                {
                    Apply(state, original);
                    return M68kHostGatewayResult.Completed;
                }
                _pendingWaits[task] = new PendingWait(
                    lvo,
                    result.ContinuationToken,
                    original,
                    kind,
                    primaryRastPort,
                    secondaryRastPort);
                return M68kHostGatewayResult.BlockCurrentTask;
            }
            case PortableLayers.LayersGatewayDisposition.InvokeGuestCallback:
                return M68kHostGatewayResult.Completed;
            default:
                Apply(state, original);
                return M68kHostGatewayResult.Completed;
        }
    }

    private bool RegisterSelectedProfile(PortableLayers.LayersAbiProfile profile)
    {
        Register(OpenLvo, Open);
        Register(CloseLvo, Close);
        Register(ExpungeLvo, Expunge);
        Register(ReservedLvo, Reserved);
        for (var index = 0; index < ClassicLvos.Length; index++)
        {
            var captured = ClassicLvos[index];
            Register(captured, state => Invoke(state, captured));
        }
        if (profile == PortableLayers.LayersAbiProfile.Unified)
        {
            for (var index = 0; index < MorphOsLvos.Length; index++)
            {
                var captured = MorphOsLvos[index];
                Register(captured, state => Invoke(state, captured));
            }
        }
        RegisterAddress(HookContinuationAddress, ContinueHook);
        RegisterAddress(BlockContinuationAddress, ContinueBlocked);
        return true;
    }

    private bool InitializeLibrary(
        uint negativeSize,
        uint positiveSize,
        string name,
        string id)
    {
        if (!_memory.IsMapped(_allocation, checked((int)_allocationSize)) ||
            !_memory.IsMapped(_libraryBase, checked((int)global::Amiga.Library.Size)))
            return false;
        var nameAddress = checked(_libraryBase + global::Amiga.Library.Size);
        var idAddress = checked(nameAddress + (uint)name.Length + 1u);
        WriteAscii(nameAddress, name);
        WriteAscii(idAddress, id);
        var memory = CreatePlatform();
        var library = APTR.FromPointer(_libraryBase);
        LayersExecNodeCodec.WriteType(ref memory, library, NodeType.Library);
        LayersExecNodeCodec.WritePriority(
            ref memory,
            library,
            LibraryPriority);
        LayersExecNodeCodec.WriteName(
            ref memory,
            library,
            APTR.FromPointer(nameAddress));
        LayersLibraryCodec.WriteFlags(
            ref memory,
            library,
            LibraryFlags.Changed | LibraryFlags.SumUsed);
        LayersLibraryCodec.WriteNegativeSize(
            ref memory,
            library,
            checked((ushort)negativeSize));
        LayersLibraryCodec.WritePositiveSize(
            ref memory,
            library,
            checked((ushort)positiveSize));
        LayersLibraryCodec.WriteVersion(
            ref memory,
            library,
            LayersAbiConstants.MorphOsV52);
        LayersLibraryCodec.WriteRevision(ref memory, library, 0);
        LayersLibraryCodec.WriteIdString(
            ref memory,
            library,
            APTR.FromPointer(idAddress));
        LayersLibraryCodec.WriteOpenCount(ref memory, library, 0);
        return true;
    }

    private bool LinkLibrary(uint execBase)
    {
        var memory = CreatePlatform();
        var list = LayersExecBaseCodec.LibraryListAddress(
            APTR.FromPointer(execBase));
        if (!_memory.IsMapped(list.Raw, checked((int)global::Amiga.List.Size)))
            return false;
        if (LayersExecListCodec.ReadHead(ref memory, list).IsNull &&
            LayersExecListCodec.ReadTail(ref memory, list).IsNull &&
            LayersExecListCodec.ReadTailPred(ref memory, list).IsNull)
        {
            LayersExecListCodec.Initialize(ref memory, list);
            LayersExecListCodec.WriteType(ref memory, list, NodeType.Library);
        }
        var predecessor = LayersExecListCodec.ReadTailPred(ref memory, list);
        var tail = LayersExecListCodec.TailAddress(list);
        if (predecessor.IsNull || !_memory.IsMapped(predecessor.Raw, 4) ||
            !_memory.IsMapped(tail.Raw, 4))
            return false;
        var library = APTR.FromPointer(_libraryBase);
        LayersExecNodeCodec.WritePrevious(ref memory, library, predecessor);
        LayersExecNodeCodec.WriteNext(ref memory, library, tail);
        LayersExecNodeCodec.WriteNext(ref memory, predecessor, library);
        LayersExecNodeCodec.WritePrevious(ref memory, tail, library);
        _linked = true;
        return true;
    }

    private void UnlinkLibrary()
    {
        if (!_linked || _libraryBase == 0 ||
            !_memory.IsMapped(_libraryBase, checked((int)global::Amiga.Library.Size)))
        {
            _linked = false;
            return;
        }
        var memory = CreatePlatform();
        var library = APTR.FromPointer(_libraryBase);
        var successor = LayersExecNodeCodec.ReadNext(ref memory, library);
        var predecessor = LayersExecNodeCodec.ReadPrevious(ref memory, library);
        if (successor.IsNotNull && predecessor.IsNotNull &&
            _memory.IsMapped(successor.Raw, 4) &&
            _memory.IsMapped(predecessor.Raw, 4) &&
            LayersExecNodeCodec.ReadNext(ref memory, predecessor) == library &&
            LayersExecNodeCodec.ReadPrevious(ref memory, successor) == library)
        {
            LayersExecNodeCodec.WriteNext(ref memory, predecessor, successor);
            LayersExecNodeCodec.WritePrevious(ref memory, successor, predecessor);
        }
        _linked = false;
    }

    private void ResetPartialInstallation()
    {
        _active = false;
        ReleaseAllPortableLayersOpaqueAllocations();
        UnlinkLibrary();
        RemoveGatewayRegistrations();
        if (_root != 0)
            _free(_root, checked((int)PortableLayers.LayersPrivateRootCore.Size));
        if (_allocation != 0)
            _free(_allocation, checked((int)_allocationSize));
        _root = 0;
        _allocation = 0;
        _allocationSize = 0;
        _libraryBase = 0;
        _dispatchState = null;
        _dispatchLvo = 0;
        _dispatchOriginalFrame = default;
        _parkAccepted = false;
        ResetRasterProviderEvidenceForTest();
    }

    private void RemoveGatewayRegistrations()
    {
        for (var index = _gateways.Count - 1; index >= 0; index--)
        {
            _memory.Bus.RemoveHostGateway(
                _gateways[index].Address,
                _gateways[index].Token);
        }
        _gateways.Clear();
    }

    private void Open(M68kCpuState state)
    {
        if (!_active)
        {
            state.D[0] = 0;
            return;
        }
        var memory = CreatePlatform();
        var library = APTR.FromPointer(_libraryBase);
        var count = LayersLibraryCodec.ReadOpenCount(ref memory, library);
        if (count != ushort.MaxValue)
            LayersLibraryCodec.WriteOpenCount(
                ref memory,
                library,
                (ushort)(count + 1));
        state.D[0] = _libraryBase;
    }

    private void Close(M68kCpuState state)
    {
        if (_active)
        {
            var memory = CreatePlatform();
            var library = APTR.FromPointer(_libraryBase);
            var count = LayersLibraryCodec.ReadOpenCount(ref memory, library);
            if (count != 0)
                LayersLibraryCodec.WriteOpenCount(
                    ref memory,
                    library,
                    (ushort)(count - 1));
        }
        state.D[0] = 0;
    }

    private static void Expunge(M68kCpuState state) => state.D[0] = 0;
    private static void Reserved(M68kCpuState state) => state.D[0] = 0;

    private void Register(short lvo, Func<M68kCpuState, M68kHostGatewayResult> handler)
        => RegisterAddress(unchecked((uint)((int)_libraryBase + lvo)), handler);

    private void Register(short lvo, Action<M68kCpuState> handler)
        => Register(lvo, state =>
        {
            handler(state);
            return M68kHostGatewayResult.Completed;
        });

    private void RegisterAddress(uint address, Func<M68kCpuState, M68kHostGatewayResult> handler)
        => _gateways.Add((address, _memory.Bus.RegisterHostGateway(address, handler)));

    private void WriteAscii(uint address, string value)
    {
        for (var index = 0; index < value.Length; index++)
            _memory.WriteByte(address + (uint)index, checked((byte)value[index]));
        _memory.WriteByte(address + (uint)value.Length, 0);
    }

    private LayersHostPlatform CreatePlatform(M68kCpuState? state = null)
        => new(this, state);

    private static PortableLayers.LayersRegisterFrame Capture(M68kCpuState state) => new()
    {
        D0 = state.D[0],
        D1 = state.D[1],
        D2 = state.D[2],
        D3 = state.D[3],
        D4 = state.D[4],
        D5 = state.D[5],
        D6 = state.D[6],
        D7 = state.D[7],
        A0 = state.A[0],
        A1 = state.A[1],
        A2 = state.A[2],
        A3 = state.A[3],
        A4 = state.A[4],
        A5 = state.A[5],
        A6 = state.A[6],
        ProgramCounter = state.ProgramCounter,
        StatusRegister = state.StatusRegister
    };

    private static void Apply(M68kCpuState state, PortableLayers.LayersRegisterFrame frame)
    {
        state.D[0] = frame.D0;
        state.D[1] = frame.D1;
        state.D[2] = frame.D2;
        state.D[3] = frame.D3;
        state.D[4] = frame.D4;
        state.D[5] = frame.D5;
        state.D[6] = frame.D6;
        state.D[7] = frame.D7;
        state.A[0] = frame.A0;
        state.A[1] = frame.A1;
        state.A[2] = frame.A2;
        state.A[3] = frame.A3;
        state.A[4] = frame.A4;
        state.A[5] = frame.A5;
        state.A[6] = frame.A6;
        state.ProgramCounter = frame.ProgramCounter;
        state.StatusRegister = frame.StatusRegister;
    }

    private static uint Add(APTR address, int offset)
        => unchecked(address.Raw + (uint)offset);

    private struct LayersHostPlatform :
        PortableLayers.ILayersMemoryPlatform,
        PortableLayers.ILayersResourcePlatform,
        PortableLayers.ILayersGraphicsPlatform,
        PortableLayers.ILayersTransactionalGraphicsPlatform,
        PortableLayers.ILayersCallbackPlatform
    {
        private readonly LayersHostServices _owner;
        private readonly M68kCpuState? _state;

        internal LayersHostPlatform(LayersHostServices owner, M68kCpuState? state)
        {
            _owner = owner;
            _state = state;
        }

        public byte ReadUInt8(APTR address, int offset = 0)
            => _owner._memory.ReadByte(Add(address, offset));
        public ushort ReadUInt16(APTR address, int offset = 0)
            => _owner._memory.ReadWord(Add(address, offset));
        public uint ReadUInt32(APTR address, int offset = 0)
            => _owner.ShouldFailRasterRetireLinkReadForTest(address, offset)
                ? 1u
                : _owner._memory.ReadLong(Add(address, offset));
        public void WriteUInt8(APTR address, int offset, byte value)
            => _owner._memory.WriteByte(Add(address, offset), value);
        public void WriteUInt16(APTR address, int offset, ushort value)
            => _owner._memory.WriteWord(Add(address, offset), value);
        public void WriteUInt32(APTR address, int offset, uint value)
            => _owner._memory.WriteLong(Add(address, offset), value);

        public void Clear(APTR address, uint byteCount)
        {
            for (var offset = 0u; offset < byteCount; offset++)
                _owner._memory.WriteByte(address.Raw + offset, 0);
        }

        public void Copy(APTR source, APTR destination, uint byteCount)
        {
            if (destination.Raw > source.Raw && destination.Raw < source.Raw + byteCount)
            {
                for (var offset = byteCount; offset != 0; offset--)
                    _owner._memory.WriteByte(
                        destination.Raw + offset - 1,
                        _owner._memory.ReadByte(source.Raw + offset - 1));
                return;
            }
            for (var offset = 0u; offset < byteCount; offset++)
                _owner._memory.WriteByte(
                    destination.Raw + offset,
                    _owner._memory.ReadByte(source.Raw + offset));
        }

        public bool IsMapped(APTR address, uint byteSize)
            => byteSize <= int.MaxValue &&
                _owner._memory.IsMapped(address.Raw, checked((int)byteSize));

        public APTR Allocate(uint byteSize, global::Amiga.Exec.MemoryFlags requirements)
            => APTR.FromPointer(_owner.AllocatePortableLayersMemory(
                byteSize,
                requirements));

        public void Free(APTR address, uint byteSize)
        {
            if (address.IsNotNull && byteSize <= int.MaxValue)
                _owner._free(address.Raw, checked((int)byteSize));
        }

        public APTR AllocateOpaque(
            uint byteSize,
            global::Amiga.Exec.MemoryFlags requirements)
        {
            if (byteSize == 0 ||
                byteSize > int.MaxValue - OpaqueAllocationHeaderSize)
            {
                return APTR.Null;
            }

            var totalSize = checked(byteSize + OpaqueAllocationHeaderSize);
            var ledgerIndex = _owner.FindFreePortableLayersOpaqueSlot();
            if (ledgerIndex < 0)
                return APTR.Null;
            var allocation = _owner.AllocatePortableLayersMemory(
                totalSize,
                requirements);
            if (allocation == 0 ||
                allocation > uint.MaxValue - OpaqueAllocationHeaderSize)
            {
                if (allocation != 0)
                    _owner._free(allocation, checked((int)totalSize));
                _owner.ReturnPortableLayersOpaqueSlot(ledgerIndex);
                return APTR.Null;
            }

            var payload = allocation + OpaqueAllocationHeaderSize;
            for (var offset = 0u; offset < totalSize; offset++)
                _owner._memory.WriteByte(allocation + offset, 0);
            _owner._memory.WriteLong(allocation, OpaqueAllocationMagic);
            _owner._memory.WriteLong(allocation + 4, byteSize);
            _owner._memory.WriteLong(allocation + 8, payload);
            _owner._memory.WriteLong(allocation + 12, totalSize);
            _owner._opaqueAllocations[ledgerIndex] = new OpaqueAllocationEntry(
                allocation,
                payload,
                byteSize,
                totalSize);
            if (!_owner.RegisterPortableLayersOpaqueAllocation(
                    payload,
                    ledgerIndex))
            {
                _owner._opaqueAllocations[ledgerIndex] = default;
                _owner.ReturnPortableLayersOpaqueSlot(ledgerIndex);
                for (var offset = 0u; offset < totalSize; offset++)
                    _owner._memory.WriteByte(allocation + offset, 0);
                _owner._free(allocation, checked((int)totalSize));
                return APTR.Null;
            }
            _owner._opaqueAllocationCount++;
            return APTR.FromPointer(payload);
        }

        public bool IsOpaqueAllocation(APTR address, uint expectedSize)
            => _owner.FindPortableLayersOpaqueAllocation(
                address.Raw,
                expectedSize) >= 0;

        public bool FreeOpaque(APTR address)
        {
            var ledgerIndex = _owner.FindPortableLayersOpaqueAllocation(
                address.Raw,
                expectedSize: 0);
            if (ledgerIndex < 0)
                return false;

            if (_owner.ShouldFailOpaqueFreeForTest())
                return false;

            return _owner.ReleasePortableLayersOpaqueAllocation(ledgerIndex);
        }

        public APTR GetCurrentTask(APTR execBase)
            => execBase.Raw == _owner._getExecBase()
                ? APTR.FromPointer(_owner._getCurrentTask())
                : APTR.Null;

        public bool ParkCurrentTask(APTR task, APTR waitObject, uint continuationToken)
        {
            _ = waitObject;
            if (_state is null || task.Raw == 0 || task.Raw != _owner._getCurrentTask() ||
                continuationToken == 0)
                return false;
            _owner._parkAccepted = _owner._suspendTask(
                _state,
                BlockContinuationAddress);
            return _owner._parkAccepted;
        }

        public void WakeTask(APTR task, uint continuationToken)
        {
            _ = continuationToken;
            if (task.IsNotNull)
                _owner._wakeTask(task.Raw);
        }

        public bool InitializeLayerRastPort(APTR rastPort, APTR bitMap, APTR layer)
        {
            if (!IsMapped(rastPort, RastPort.Size) ||
                !IsMapped(bitMap, BitMap.Size) || layer.IsNull)
                return false;
            // InitRastPort's classic public defaults are part of the Layer
            // envelope, not optional host state.  Layers allocates the SDK's
            // 100-byte RastPort, so initialize only fields contained in that
            // ABI rather than calling the portable extended-envelope helper.
            var memory = this;
            LayersRastPortCodec.InitializeLayerDefaults(
                ref memory,
                rastPort,
                bitMap,
                layer);
            _owner.TraceProviderOperationForTest("InitRastPort");
            return true;
        }

        public bool CopyRectangle(
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
            => _owner.CopyRectangle(
                sourceBitMap,
                destinationBitMap,
                sourceX,
                sourceY,
                destinationX,
                destinationY,
                width,
                height,
                minterm,
                mask);

        public bool BackfillRectangle(
            APTR rastPort,
            APTR bitMap,
            APTR hook,
            int minX,
            int minY,
            int maxX,
            int maxY,
            int offsetX,
            int offsetY)
            => _owner.BackfillRectangle(
                rastPort,
                bitMap,
                hook,
                minX,
                minY,
                maxX,
                maxY,
                offsetX,
                offsetY);

        public APTR BeginLayersPixelTransaction(int operationCapacity)
            => _owner.BeginLayersPixelTransaction(operationCapacity);

        public bool StageLayersPixelCopy(
            APTR transaction,
            APTR sourceBitMap,
            APTR destinationBitMap,
            int sourceX,
            int sourceY,
            int destinationX,
            int destinationY,
            int width,
            int height,
            byte minterm,
            APTR mask,
            PortableLayers.LayersPixelCopySource sourceMode)
            => _owner.StageLayersPixelCopy(
                transaction,
                sourceBitMap,
                destinationBitMap,
                sourceX,
                sourceY,
                destinationX,
                destinationY,
                width,
                height,
                minterm,
                mask,
                sourceMode);

        public bool StageLayersPixelBackfill(
            APTR transaction,
            APTR rastPort,
            APTR destinationBitMap,
            APTR hook,
            int destinationX,
            int destinationY,
            int width,
            int height,
            int offsetX,
            int offsetY)
            => _owner.StageLayersPixelBackfill(
                transaction,
                rastPort,
                destinationBitMap,
                hook,
                destinationX,
                destinationY,
                width,
                height,
                offsetX,
                offsetY);

        public bool TryApplyLayersPixelTransaction(APTR transaction)
            => _owner.TryApplyLayersPixelTransaction(transaction);

        public void CancelLayersPixelTransaction(APTR transaction)
            => _owner.CancelLayersPixelTransaction(transaction);

        public void CompleteLayersPixelTransaction(APTR transaction)
            => _owner.CompleteLayersPixelTransaction(transaction);

        public APTR AllocateBitMap(
            int width,
            int height,
            byte depth,
            uint flags,
            APTR friendBitMap)
            => _owner.AllocateBitMap(width, height, depth, flags, friendBitMap);

        public bool ReleaseBitMap(APTR bitMap) => _owner.ReleaseBitMap(bitMap);

        public bool ExecuteLayeredRaster(
            short graphicsLvo,
            ref PortableLayers.LayersRegisterFrame registers,
            APTR layer,
            APTR firstClipRect,
            APTR firstSuperClipRect,
            APTR secondaryLayer,
            APTR secondaryFirstClipRect,
            APTR secondaryFirstSuperClipRect)
            => _owner.ExecuteLayeredRaster(
                graphicsLvo,
                ref registers,
                layer,
                firstClipRect,
                firstSuperClipRect,
                secondaryLayer,
                secondaryFirstClipRect,
                secondaryFirstSuperClipRect);

        public bool RenderLayerInfo(
            APTR layerInfo,
            APTR destinationRastPort,
            APTR destinationBitMap,
            APTR destinationBounds,
            APTR layerInfoBounds,
            APTR renderList,
            APTR ignoreList,
            bool erase,
            bool applyOpacityMultiplier)
            => _owner.RenderLayerInfo(
                layerInfo,
                destinationRastPort,
                destinationBitMap,
                destinationBounds,
                layerInfoBounds,
                renderList,
                ignoreList,
                erase,
                applyOpacityMultiplier);

        public bool BeginGuestHook(
            APTR hook,
            APTR target,
            APTR message,
            uint continuationToken)
        {
            if (_state is null || continuationToken == 0 ||
                !IsMapped(hook, Hook.Size))
                return false;
            var entry = ReadUInt32(hook, checked((int)MinNode.Size));
            if (entry == 0 || _owner._pendingCallback.IsActive)
                return false;
            _owner._pendingCallback = new PendingCallback(
                _owner._dispatchLvo,
                continuationToken,
                _owner._dispatchOriginalFrame);
            _state.A[0] = hook.Raw;
            _state.A[1] = message.Raw;
            _state.A[2] = target.Raw;
            _owner._startGuestSubroutine(_state, entry, HookContinuationAddress);
            return _state.ProgramCounter == entry ||
                _state.ProgramCounter == entry + 6 ||
                _state.ProgramCounter == HookContinuationAddress;
        }

        public void CancelGuestHook(uint continuationToken)
        {
            if (_owner._pendingCallback.Token == continuationToken)
                _owner._pendingCallback = default;
        }
    }
}
