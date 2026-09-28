using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Typed home for graphics.library semantics.
///
/// This foundation type intentionally exposes no LVO dispatcher. Function
/// methods are added goal-by-goal after their guest ABI and behavior are tested.
/// </summary>
internal sealed class GraphicsLibraryCore
{
    private readonly GraphicsColorOperations.Registry _colorMaps = new();
    private readonly GraphicsSpriteOperations.Registry _sprites = new();
    private readonly GraphicsMonitorOperations.Registry _monitors;
    private readonly GraphicsMonitorPositionState _monitorPositions = new();
    private readonly GraphicsExtendedNodeOperations.Registry _extendedNodes;
    private readonly GraphicsCopperOperations.Registry _copperLists = new();
    private readonly GraphicsDoubleBufferOperations.Registry _dbufInfos = new();
    private readonly GraphicsAnimationOperations.Registry _animationBuffers = new();
    private readonly GraphicsRasterOperations.Registry _bitMaps = new();
    private readonly GraphicsScreenOperations.Registry _screenChains = new();
    private readonly GraphicsScreenPrefixOperations.Registry _screenPrefixes = new();
    private readonly GraphicsRegionOperations.Registry _regions = new();
    private readonly Dictionary<uint, (uint Width, uint Height)> _rasters = new();
    private readonly Dictionary<uint, uint> _temporaryViewPortExtras = new();
    private readonly IGraphicsLayerBackend _layers;
    private readonly IGraphicsLayerRasterBackend? _layerRaster;
    private readonly IGraphicsGelsBackend? _gels;
    private readonly IGraphicsSpriteBackend? _spriteBackend;
    private readonly bool _allowGuestOwnedDbufInfo;
    private readonly Func<uint>? _currentTask;
    // Optional full guest GfxBase envelope.  Host-shim sessions leave this
    // zero; native/CopperSharp68k sessions can supply the mapped base so the
    // portable LoadView path mirrors GfxBase->ActiView publication.
    private uint _graphicsLibraryBase;
    // A full native GfxBase may own an opaque DisplayInfoDataBase pointer.
    // Track only a sidecar that this instance allocated; a non-zero pointer
    // supplied by Kickstart or a display provider is never reinterpreted or
    // freed by the portable bridge.
    private uint _nativeDisplayDatabaseAddress;
    private uint _nativeDisplayDatabaseBase;
    private bool _ownsNativeDisplayDatabase;
    // Captured guest registration is authoritative across owned-table rebuilds,
    // including deliberately empty/opaque slots. It grants no node ownership.
    private NativeMonitorRegistrationState? _savedNativeMonitorRegistration;
    // SetDisplayInfoData mutates the family record, not the MonitorSpec node.
    // Keep the complete transferred MNTR image as a per-library sidecar so
    // opaque padding and driver-owned fields survive later readback without
    // changing the public CMDB3 layout.
    private readonly Dictionary<uint, byte[]> _monitorDriverRecords = new();
    // Monitor registration, the mutable driver records, and the optional
    // native CMDB publication form one ownership domain.  The native overlay
    // can enter these seams from a host callback while another task is
    // rebinding or releasing the provider; serialize the domain so a reader
    // never observes a half-published pointer/record pair.  This is host-side
    // lifetime serialization only; it does not claim to emulate an Exec
    // semaphore or inter-task signal.
    private readonly object _monitorStateSync = new();
    private readonly record struct NativeMonitorRegistrationState(
        uint DefaultFamily, uint NtscMonitor, uint PalMonitor,
        GraphicsMonitorViewPosition NtscOriginal, GraphicsMonitorViewPosition PalOriginal);
    // Graphics.library keeps the active View pointer and the last published
    // guest-chain fingerprint as part of the display hand-off state.  A
    // repeated request for an unchanged View must not rebuild/requeue the same
    // copper stream, but a caller may legally edit the View's CPR or ViewPort
    // links before loading it again.  Keep this sidecar independent of the
    // host backend so portable and CopperStart paths share the same lifecycle.
    private uint _activeViewAddress;
    private ulong _activeViewSignature;
    private bool _hasPublishedView;
    // CurrentMonitor and TopLine are optional native GfxBase tail fields.
    // Remember only fields this instance actually populated so LoadView(NULL)
    // can relinquish its own publication without clearing a provider-owned
    // value that was present before the portable bridge took over.
    private uint _ownedNativeCurrentMonitorAddress;
    private uint _ownedNativeCurrentMonitorValue;
    private uint _ownedNativeTopLineAddress;
    private ushort _ownedNativeTopLineValue;

    internal GraphicsLibraryCore(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        IGraphicsBlitterBackend blitter,
        IGraphicsDisplayBackend display,
        IGraphicsLayerBackend? layers = null,
        IGraphicsGelsBackend? gels = null,
        IGraphicsSpriteBackend? spriteBackend = null,
        bool allowGuestOwnedDbufInfo = false,
        uint graphicsLibraryBase = 0,
        Func<uint>? currentTask = null)
    {
        Memory = memory;
        Allocator = allocator;
        Blitter = blitter;
        Display = display;
        _layers = layers ?? GraphicsUnboundLayerBackend.Instance;
        _layerRaster = layers as IGraphicsLayerRasterBackend;
        _gels = gels;
        _spriteBackend = spriteBackend;
        _allowGuestOwnedDbufInfo = allowGuestOwnedDbufInfo;
        _currentTask = currentTask;
        _graphicsLibraryBase = graphicsLibraryBase;
        _extendedNodes = new GraphicsExtendedNodeOperations.Registry(graphicsLibraryBase);
        _monitors = new GraphicsMonitorOperations.Registry(
            (display as IGraphicsDisplayProfileBackend)?.IsNtsc == true,
            (display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false,
            graphicsLibraryBase);
    }

    internal IGraphicsMemory Memory { get; }
    internal IGraphicsAllocatorBackend Allocator { get; }
    internal IGraphicsBlitterBackend Blitter { get; }
    internal IGraphicsDisplayBackend Display { get; }

    /// <summary>
    /// Guest graphics.library base currently bound to this instance.  The
    /// native-overlay register adapter uses the rebound base to classify
    /// optional GfxBase field publication without reconstructing a host-only
    /// address from the original compatibility base.
    /// </summary>
    internal uint GraphicsLibraryBase
    {
        get
        {
            lock (_monitorStateSync)
                return _graphicsLibraryBase;
        }
    }

    /// <summary>
    /// Runs a native-overlay publication transaction while the monitor/provider
    /// lifetime gate is held.  The register adapter uses this for operations
    /// whose writable-span preflight must remain paired with the subsequent
    /// registry and guest-memory commit; a graphics-base rebind cannot enter
    /// between those two phases.
    /// </summary>
    internal bool WithMonitorStateLock(Func<bool> operation)
    {
        lock (_monitorStateSync)
            return operation();
    }

    /// <summary>
    /// Reports whether the rebound base exposes the native GfxBase monitor
    /// envelope and structurally valid MonitorList sentinels. Compact
    /// CopperScreen images deliberately do not satisfy this predicate; native
    /// overlay admission can therefore keep using the compatibility monitor
    /// path until a real CopperSharp68k-shaped base is present.
    /// </summary>
    internal bool HasNativeGfxBaseEnvelope()
    {
        lock (_monitorStateSync)
            return HasNativeGfxBaseEnvelope(_graphicsLibraryBase);
    }

    /// <summary>
    /// Reports whether the mapped guest exposes all public MonitorList
    /// fields, regardless of whether their sentinel values form a valid
    /// Exec list.  Native-overlay admission uses this distinction to keep a
    /// malformed-but-fully-mapped resident list with its native/provider
    /// owner before the portable OpenMonitor path allocates a MonitorSpec.
    /// Compact compatibility images do not map this private suffix and
    /// therefore return <see langword="false"/>.
    /// </summary>
    internal bool HasMappedNativeGfxBaseMonitorList()
    {
        lock (_monitorStateSync)
            return HasMappedNativeGfxBaseMonitorList(_graphicsLibraryBase);
    }

    internal bool HasMappedNativeGfxBaseMonitorList(uint graphicsBase)
    {
        if ((graphicsBase & 1u) != 0 ||
            graphicsBase > uint.MaxValue -
                (uint)(GraphicsLayouts.GfxBaseNativeSize - 1))
        {
            return false;
        }

        var list = graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorList;
        var head = graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListHead;
        var tail = graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListTail;
        var tailPred = graphicsBase +
                       (uint)GraphicsLayouts.GfxBaseMonitorListTailPred;
        var type = graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListType;
        var pad = graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorListPad;
        var defaultMonitor = graphicsBase +
                             (uint)GraphicsLayouts.GfxBaseDefaultMonitor;

        if (!Memory.TryReadLong(head, out var headValue) ||
            !Memory.TryReadLong(tail, out var tailValue) ||
            !Memory.TryReadLong(tailPred, out var tailPredValue) ||
            !Memory.TryReadByte(type, out _) ||
            !Memory.TryReadByte(pad, out _) ||
            !Memory.TryReadLong(defaultMonitor, out var defaultMonitorValue) ||
            list > uint.MaxValue - 12u)
        {
            return false;
        }

        // A compact compatibility mapping commonly exposes zero-filled host
        // bytes at the private suffix without actually publishing a native
        // MonitorList. Treat that all-zero shape as absent; any nonzero list
        // pointer/default-monitor value means a resident/provider envelope is
        // present and should be admitted only by its native owner.
        return headValue != 0 || tailValue != 0 ||
               tailPredValue != 0 || defaultMonitorValue != 0;
    }

    internal bool IsBoundToGraphicsLibraryBase(uint graphicsLibraryBase)
    {
        lock (_monitorStateSync)
            return _graphicsLibraryBase == graphicsLibraryBase;
    }

    internal bool HasGelsBackend
        => _gels is not null;

    internal bool HasQueuedBlitterLinkBackend
        => Blitter is IGraphicsQueuedBlitterLinkBackend linked &&
            linked.PublishesGuestLinks;

    internal bool HasSpriteBackend
        => _spriteBackend is not null;

    /// <summary>
    /// Moves guest-owned native-library publication to the discovered
    /// graphics.library base.  CopperStart constructs its host adapters before
    /// Exec discovers a resident graphics.library, so keeping this base
    /// immutable would publish GfxNew library links, MonitorList nodes, and
    /// ActiView through the compatibility image after native takeover.
    /// </summary>
    internal uint RebindGraphicsLibraryBase(uint graphicsLibraryBase)
    {
        lock (_monitorStateSync)
        {
            var previous = _graphicsLibraryBase;
        // GfxBase is a 68000 structure whose public LONG fields are consumed
        // at word-aligned offsets.  Keep an odd discovered base available to
        // the resident/provider owner instead of rebinding the portable
        // registries to an address-error envelope.
        if ((graphicsLibraryBase & 1u) != 0)
            return previous;

        if (graphicsLibraryBase != previous)
        {
            // Existing GfxNew/MonitorSpec envelopes carry their own
            // xln_Lib backlink.  Move those guest fields as one transaction
            // before changing the registry default; otherwise a native
            // takeover would expose a mixed compatibility/native ownership
            // graph.  The monitor registry is applied second so a refusal can
            // roll the extended-node writes back to the old base.
            if (!_extendedNodes.TryRebindGraphicsLibraryBase(
                    Memory,
                    graphicsLibraryBase))
            {
                return previous;
            }

            if (!_monitors.TryRebindGraphicsLibraryBase(
                    Memory,
                    graphicsLibraryBase))
            {
                _ = _extendedNodes.TryRebindGraphicsLibraryBase(Memory, previous);
                return previous;
            }

            // A native overlay may have published a private display-database
            // sidecar through the old GfxBase. Moving the registries without
            // first detaching that sidecar would leave an owned allocation
            // unreachable, and a later publication on the new base would
            // create a second object. If detaching fails, restore both
            // registry backlink sets and keep the old binding authoritative.
            if (_ownsNativeDisplayDatabase &&
                _nativeDisplayDatabaseBase != 0 &&
                _nativeDisplayDatabaseBase != graphicsLibraryBase &&
                !TryDetachNativeDisplayDatabaseForRebind())
            {
                _ = _monitors.TryRebindGraphicsLibraryBase(Memory, previous);
                _ = _extendedNodes.TryRebindGraphicsLibraryBase(Memory, previous);
                return previous;
            }

            _graphicsLibraryBase = graphicsLibraryBase;
            // A rebinding changes the guest structure that owns these
            // offsets. Do not later clear an address belonging to the
            // previous native overlay; the new provider must explicitly
            // publish its own fields.
            ForgetOwnedNativeViewSidecars();
        }
            return previous;
        }
    }

    /// <summary>
    /// Publishes the private display-database sidecar used by the native
    /// CopperScreen/CopperSharp68k handoff.  The APTR is written last and only
    /// for a structurally valid full GfxBase envelope.  A non-zero pointer is
    /// provider/native-owned and is deliberately preserved byte-for-byte.
    /// </summary>
    internal bool TryPublishNativeDisplayDatabase()
    {
        lock (_monitorStateSync)
        {
            if (!HasNativeGfxBaseEnvelope(_graphicsLibraryBase))
                return false;

        var field = _graphicsLibraryBase +
                    (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase;
        if (!Memory.TryReadLong(field, out var current))
            return false;

        if (current != 0)
        {
            // Keep an existing native/provider object untouched. If a
            // provider replaced a sidecar after publication, stop claiming
            // ownership so Dispose cannot free memory it now owns.
            if (_ownsNativeDisplayDatabase &&
                current != _nativeDisplayDatabaseAddress)
            {
                if (!TryInvalidateNativeRuntimeDescriptor())
                    return false;
                _ownsNativeDisplayDatabase = false;
                _nativeDisplayDatabaseAddress = 0;
                _nativeDisplayDatabaseBase = 0;
                _savedNativeMonitorRegistration = null;
                _monitorDriverRecords.Clear();
            }

            return !_ownsNativeDisplayDatabase || TryPublishNativeRuntimeDescriptor();
        }

        if (_ownsNativeDisplayDatabase &&
            _nativeDisplayDatabaseAddress != 0 &&
            _nativeDisplayDatabaseBase == _graphicsLibraryBase)
        {
            if (Memory.TryWriteLong(field, _nativeDisplayDatabaseAddress))
                return TryPublishNativeRuntimeDescriptor();

            // A sparse/native bridge may publish part of the APTR before
            // declining the LONG. Restore the previously cleared field so a
            // retry cannot observe a stale or half-written sidecar pointer.
            _ = RestoreGuestLong(field, 0);
            return false;
        }

        if (!TryGetMonitorRegistrationForPublication(out var registration))
            return false;
        var publicationBase = _graphicsLibraryBase;
        if (!GraphicsDisplayDatabase.TryCreateNativeDatabase(
                Memory,
                Allocator,
                (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false,
                out var database,
                id => _monitorPositions.Get(id, false),
                registration.DefaultFamily == GraphicsModeIds.NtscMonitor,
                registration.NtscMonitor,
                registration.PalMonitor,
                id => id == GraphicsModeIds.NtscMonitor ? registration.NtscOriginal : registration.PalOriginal))
        {
            return false;
        }

        // Allocation may yield to a host callback. Do not publish an old
        // snapshot after a rebind, resident change, or provider publication.
        // The candidate is still private and has never become guest-reachable.
        if (_graphicsLibraryBase != publicationBase || _ownsNativeDisplayDatabase ||
            !Memory.TryReadLong(field, out var latest) || latest != current ||
            !TryGetMonitorRegistrationForPublication(out var currentRegistration) ||
            currentRegistration != registration)
        {
            Allocator.Free(database, (uint)GraphicsDisplayDatabase.NativeDatabaseSize, GraphicsMemoryClass.Public);
            return false;
        }

        if (!Memory.TryWriteLong(field, database))
        {
            // The sidecar is not reachable until this APTR commits. Restore
            // the original zero value byte-wise before releasing the block so
            // a partial pointer publication cannot outlive its allocation.
            _ = RestoreGuestLong(field, current);
            Allocator.Free(
                database,
                (uint)GraphicsDisplayDatabase.NativeDatabaseSize,
                GraphicsMemoryClass.Public);
            return false;
        }

        _nativeDisplayDatabaseAddress = database;
        _nativeDisplayDatabaseBase = _graphicsLibraryBase;
        _ownsNativeDisplayDatabase = true;
            return TryPublishNativeRuntimeDescriptor();
        }
    }

    /// <summary>
    /// Reverses a sidecar publication when a native overlay is disposed. The
    /// allocation is freed when the native GfxBase still points at the object
    /// we own or has already been cleared; a provider replacement remains
    /// untouched.
    /// </summary>
    internal bool ReleaseNativeDisplayDatabase()
    {
        lock (_monitorStateSync)
        {
            if (!_ownsNativeDisplayDatabase ||
                _nativeDisplayDatabaseAddress == 0 ||
                _nativeDisplayDatabaseBase == 0)
            {
                return true;
            }

            // The native/provider owner may have replaced the GfxBase
            // envelope after publication.  Do not clear or free a database
            // through a malformed MonitorList; the pointer remains reachable
            // until the provider restores a valid native handoff boundary.
            if (!HasNativeGfxBaseEnvelope(_nativeDisplayDatabaseBase))
                return false;

            var field = _nativeDisplayDatabaseBase +
                        (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase;
            if (!Memory.TryReadLong(field, out var current))
            {
                // The guest field is unreadable, so reachability cannot be
                // proven. Keep the ownership claim for a later retry rather than
                // freeing a block that might still be reachable by a provider.
                return false;
            }

            if (current != 0 && current != _nativeDisplayDatabaseAddress)
            {
                if (!TryInvalidateNativeRuntimeDescriptor())
                    return false;
                // A provider/native owner replaced the pointer. Relinquish only
                // our sidecar claim; never clear or free memory reachable through
                // the replacement object.
                _nativeDisplayDatabaseAddress = 0;
                _nativeDisplayDatabaseBase = 0;
                _ownsNativeDisplayDatabase = false;
                _savedNativeMonitorRegistration = null;
                _monitorDriverRecords.Clear();
                return true;
            }

            if (!TryCaptureOwnedMonitorState())
                return false;
            if (!TryInvalidateNativeRuntimeDescriptor())
                return false;

            if (current == _nativeDisplayDatabaseAddress &&
                !TryClearOwnedNativeDisplayDatabasePointer(field))
            {
                // Do not free a block that remains reachable when the guest
                // boundary declines the clear.
                return false;
            }

            // If the field was already cleared by the guest/provider, the owned
            // sidecar is no longer reachable and can be reclaimed just like the
            // ordinary clear-and-free path above.

            Allocator.Free(
                _nativeDisplayDatabaseAddress,
                (uint)GraphicsDisplayDatabase.NativeDatabaseSize,
                GraphicsMemoryClass.Public);
            _nativeDisplayDatabaseAddress = 0;
            _nativeDisplayDatabaseBase = 0;
            _ownsNativeDisplayDatabase = false;
            return true;
        }
    }

    private bool TryDetachNativeDisplayDatabaseForRebind()
    {
        var address = _nativeDisplayDatabaseAddress;
        var baseAddress = _nativeDisplayDatabaseBase;
        if (!_ownsNativeDisplayDatabase || address == 0 || baseAddress == 0)
            return true;

        // Rebinding has the same ownership proof as ordinary release.  A
        // malformed native envelope cannot authorize clearing or freeing a
        // still-reachable display database sidecar.
        if (!HasNativeGfxBaseEnvelope(baseAddress))
            return false;

        var field = baseAddress +
                    (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase;
        if (!Memory.TryReadLong(field, out var current))
            return false;

        if ((current == address || current == 0) && !TryCaptureOwnedMonitorState())
            return false;
        if (!TryInvalidateNativeRuntimeDescriptor())
            return false;

        if (current == address)
        {
            if (!TryClearOwnedNativeDisplayDatabasePointer(field))
                return false;

            Allocator.Free(
                address,
                (uint)GraphicsDisplayDatabase.NativeDatabaseSize,
                GraphicsMemoryClass.Public);
        }
        else if (current != 0)
        {
            // A provider/native owner replaced the pointer. Its object is
            // no longer ours; relinquish only the sidecar claim and never
            // free memory reachable through the replacement pointer.
            _nativeDisplayDatabaseAddress = 0;
            _nativeDisplayDatabaseBase = 0;
            _ownsNativeDisplayDatabase = false;
            _savedNativeMonitorRegistration = null;
            _monitorDriverRecords.Clear();
            return true;
        }
        else
        {
            // The field was cleared externally. The owned block is no
            // longer guest-reachable, so it can be reclaimed before moving
            // the registry binding.
            Allocator.Free(
                address,
                (uint)GraphicsDisplayDatabase.NativeDatabaseSize,
                GraphicsMemoryClass.Public);
        }

        _nativeDisplayDatabaseAddress = 0;
        _nativeDisplayDatabaseBase = 0;
        _ownsNativeDisplayDatabase = false;
        return true;
    }

    private bool TryClearOwnedNativeDisplayDatabasePointer(uint field)
    {
        // Clearing the APTR is a guest-visible ownership transition. A
        // native/mapped bridge may accept a prefix of the LONG and then
        // reject it; retain the sidecar until the original pointer is restored
        // byte-wise, otherwise teardown could free an object still reachable
        // through a half-cleared GfxBase field.
        if (!TrySnapshotNativeBytes(field, sizeof(uint), out var original))
            return false;

        if (Memory.TryWriteLong(field, 0))
            return true;

        _ = RestoreNativeBytes(field, original);
        return false;
    }

    internal bool HasNativeGfxBaseEnvelope(uint graphicsBase)
    {
        if ((graphicsBase & 1u) != 0 ||
            graphicsBase > uint.MaxValue -
                (uint)(GraphicsLayouts.GfxBaseNativeSize - 1))
        {
            return false;
        }

        var list = graphicsBase + (uint)GraphicsLayouts.GfxBaseMonitorList;
        var headAddress = graphicsBase +
                           (uint)GraphicsLayouts.GfxBaseMonitorListHead;
        var tailAddress = graphicsBase +
                           (uint)GraphicsLayouts.GfxBaseMonitorListTail;
        var tailPredAddress = graphicsBase +
                              (uint)GraphicsLayouts.GfxBaseMonitorListTailPred;
        var typeAddress = graphicsBase +
                          (uint)GraphicsLayouts.GfxBaseMonitorListType;
        var padAddress = graphicsBase +
                         (uint)GraphicsLayouts.GfxBaseMonitorListPad;

        return Memory.TryReadLong(headAddress, out var head) &&
               Memory.TryReadLong(tailAddress, out var tail) &&
               Memory.TryReadLong(tailPredAddress, out var tailPred) &&
               Memory.TryReadByte(typeAddress, out var type) &&
               Memory.TryReadByte(padAddress, out var pad) &&
               type == 0 &&
               pad == 0 &&
               ((head == list + 4u && tail == 0 && tailPred == list) ||
                // A resident OpenMonitor may already have linked one or
                // more MonitorSpec nodes. The native GfxBase remains a valid
                // full envelope once the list has moved beyond its empty
                // sentinel; only the empty-list form is required during
                // initial takeover.
                (head != 0 && head != list + 4u && tail == 0 && tailPred != list));
    }

    internal void LockLayerRom(uint layerAddress)
    {
        // LockLayerRom consumes the Layer * in A5.  Keep the portable
        // compatibility registry/provider from claiming a null or odd
        // envelope that would raise an address error on a 68000 caller.
        if (layerAddress == 0 || (layerAddress & 1u) != 0)
            return;

        _layers.Lock(layerAddress);
    }

    internal bool AttemptLockLayerRom(uint layerAddress)
        => layerAddress != 0 &&
           (layerAddress & 1u) == 0 &&
           _layers.TryLock(layerAddress);

    internal void UnlockLayerRom(uint layerAddress)
    {
        if (layerAddress == 0 || (layerAddress & 1u) != 0)
            return;

        _layers.Unlock(layerAddress);
    }

    /// <summary>
    /// Synchronizes a layer-owned SuperBitMap only for a non-null Layer
    /// handle.  The private Layer/ClipRect envelope belongs to layers.library
    /// or a native provider; a null or odd A0 is a malformed public call and
    /// must not be forwarded to a callback-backed host that could otherwise
    /// claim it.
    /// </summary>
    internal bool SyncSBitMap(uint layerAddress)
        => layerAddress != 0 &&
           (layerAddress & 1u) == 0 &&
           _layers.SyncSuperBitMap(layerAddress);

    /// <summary>
    /// Copies a layer-owned SuperBitMap only for a non-null Layer handle.  Do
    /// the ownership/alignment check before entering the optional provider so
    /// the portable register boundary preserves D0/cycles for malformed input.
    /// </summary>
    internal bool CopySBitMap(uint layerAddress)
        => layerAddress != 0 &&
           (layerAddress & 1u) == 0 &&
           _layers.CopySuperBitMap(layerAddress);

    internal uint GetColorMap(uint entries)
        => GraphicsColorOperations.GetColorMap(Memory, Allocator, _colorMaps, entries);

    internal bool FreeColorMap(uint colorMap)
        => colorMap == 0 ||
           WithColorMapLock(
               colorMap,
               false,
               () => GraphicsColorOperations.FreeColorMap(Allocator, _colorMaps, colorMap));

    /// <summary>
    /// Reports whether the ColorMap envelope belongs to this graphics
    /// instance. Native-overlay palette calls use this gate before reading or
    /// mutating ColorTable storage, so a resident/native or RTG map remains
    /// available to its original owner.
    /// </summary>
    internal bool IsOwnedColorMap(uint colorMap)
        => _colorMaps.Maps.ContainsKey(colorMap);

    /// <summary>
    /// Reports whether an owned ColorMap's private PaletteExtra sidecar is
    /// structurally safe for a native-overlay pen operation. A narrowed
    /// sharable span is allowed and will be normalized by the next portable
    /// call; mismatched sidecar pointers or list storage remain available to
    /// the resident/provider implementation.
    /// </summary>
    internal bool IsNativeOverlayPaletteStateSafe(uint colorMap)
        => GraphicsColorOperations.IsPaletteExtraStateSafe(Memory, _colorMaps, colorMap);

    /// <summary>
    /// Returns true only when a readable ViewPort points at a ColorMap owned
    /// by this graphics instance. Bulk palette loads may publish through the
    /// viewport callback, so native-overlay admission must not infer
    /// ownership from a merely readable viewport envelope.
    /// </summary>
    internal bool IsOwnedViewPortColorMap(uint viewPort)
    {
        // ViewPort contains word/long fields.  A byte-addressable host could
        // otherwise discover an owned ColorMap through an odd base and let a
        // native-overlay palette call claim an address-error request.
        // The inclusive longword envelope still admits the one valid base
        // whose ColorMap field ends exactly at $FFFFFFFF.
        if (viewPort == 0 ||
            (viewPort & 1u) != 0 ||
            viewPort > uint.MaxValue -
                (uint)(GraphicsLayouts.ViewPortColorMap + sizeof(uint) - 1) ||
            !Memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortColorMap,
                out var colorMap))
        {
            return false;
        }

        return IsOwnedColorMap(colorMap);
    }

    /// <summary>
    /// Reports whether an extended-node envelope belongs to this graphics
    /// instance.  The native overlay uses this gate for GfxFree so a resident
    /// or provider-owned node remains available to its original library.
    /// </summary>
    internal bool IsOwnedExtendedNode(uint node)
    {
        lock (_monitorStateSync)
            return _extendedNodes.IsOwned(node);
    }

    /// <summary>
    /// Describes the guest publication performed by GfxFree for an owned
    /// extended node. ViewExtra and ViewPortExtra clear their association
    /// backlink before releasing the allocation; monitor nodes have no such
    /// public LONG. Native-overlay dispatch uses this read-only plan before
    /// entering the fail-closed registry transaction.
    /// </summary>
    internal bool TryGetNativeGfxFreePublication(
        uint node,
        out uint backlink)
    {
        lock (_monitorStateSync)
            return _extendedNodes.TryGetFreePublicationSpan(
                Memory,
                node,
                out backlink);
    }

    /// <summary>
    /// Describes the complete guest publication performed by GfxAssociate.
    /// Native-overlay dispatch uses this side-effect-free plan to preflight a
    /// temporary ViewPortExtra's VPXF_FREE_ME WORD plus both the incoming and
    /// any displaced node backlink LONGs before the registry transaction
    /// changes public state.
    /// </summary>
    internal bool TryGetNativeGfxAssociatePublication(
        uint pointer,
        uint node,
        out uint temporaryFlags,
        out bool hasTemporaryFlags,
        out uint first,
        out uint second,
        out int count)
    {
        lock (_monitorStateSync)
        {
            temporaryFlags = 0;
            hasTemporaryFlags = _temporaryViewPortExtras.ContainsKey(node);
            if (hasTemporaryFlags)
            {
                if (node > uint.MaxValue -
                        (uint)(GraphicsLayouts.ViewPortExtraFlags + sizeof(ushort) - 1) ||
                    !Memory.TryReadWord(
                        node + (uint)GraphicsLayouts.ViewPortExtraFlags,
                        out _))
                {
                    first = 0;
                    second = 0;
                    count = 0;
                    return false;
                }

                temporaryFlags = node + (uint)GraphicsLayouts.ViewPortExtraFlags;
            }

            return _extendedNodes.TryGetAssociatePublicationSpans(
                Memory,
                pointer,
                node,
                out first,
                out second,
                out count);
        }
    }

    internal int AttachPalExtra(uint colorMap, uint viewPort)
        => WithColorMapLock(
            colorMap,
            1,
            () => GraphicsColorOperations.AttachPalExtra(
                Allocator,
                Memory,
                _colorMaps,
                colorMap,
                viewPort));

    internal uint GetRGB4(uint colorMap, int entry)
        => WithColorMapLock(
            colorMap,
            uint.MaxValue,
            () => GraphicsColorOperations.GetRgb4(Memory, _colorMaps, colorMap, entry));

    internal bool SetRGB4CM(uint colorMap, uint entry, byte red, byte green, byte blue)
        => WithColorMapLock(
            colorMap,
            false,
            () => GraphicsColorOperations.SetRgb4Cm(Memory, _colorMaps, colorMap, entry, red, green, blue));

    internal bool SetRGB32CM(uint colorMap, uint entry, uint red, uint green, uint blue)
        => WithColorMapLock(
            colorMap,
            false,
            () => GraphicsColorOperations.SetRgb32Cm(Memory, _colorMaps, colorMap, entry, red, green, blue));

    internal int ObtainPen(uint colorMap, uint requested, uint red, uint green, uint blue, uint flags)
        => WithColorMapLock(
            colorMap,
            -1,
            () => GraphicsColorOperations.ObtainPen(
                Memory,
                _colorMaps,
                colorMap,
                requested,
                red,
                green,
                blue,
                flags,
                Display as IGraphicsPaletteBackend));

    internal bool ReleasePen(uint colorMap, uint pen)
        => WithColorMapLock(
            colorMap,
            false,
            () => GraphicsColorOperations.ReleasePen(Memory, _colorMaps, colorMap, pen));

    internal int FindColor(uint colorMap, uint red, uint green, uint blue, int maxPen)
        => WithColorMapLock(
            colorMap,
            -1,
            () => GraphicsColorOperations.FindColor(Memory, _colorMaps, colorMap, red, green, blue, maxPen));

    internal int ObtainBestPenA(uint colorMap, uint red, uint green, uint blue, uint tags)
    {
        _ = TryObtainBestPenA(colorMap, red, green, blue, tags, out var result);
        return result;
    }

    internal bool TryObtainBestPenA(
        uint colorMap,
        uint red,
        uint green,
        uint blue,
        uint tags,
        out int result)
    {
        var outcome = WithColorMapLock(
            colorMap,
            (Claimed: false, Result: -1),
            () =>
            {
                var claimed = GraphicsColorOperations.TryObtainBestPenA(
                    Memory,
                    _colorMaps,
                    colorMap,
                    red,
                    green,
                    blue,
                    tags,
                    out var pen,
                    Display as IGraphicsPaletteBackend);
                return (Claimed: claimed, Result: pen);
            });
        result = outcome.Result;
        return outcome.Claimed;
    }

    internal bool VideoControl(uint colorMap, uint tags)
        => WithColorMapLock(
            colorMap,
            false,
            () => GraphicsColorOperations.VideoControl(Memory, _colorMaps, _extendedNodes, colorMap, tags));

    internal bool IsNativeOverlayVideoControlSafe(
        uint tags,
        Func<uint, ulong, bool>? canPublish = null)
        => GraphicsColorOperations.IsNativeOverlayVideoControlSafe(
            Memory,
            tags,
            canPublish);

    internal bool GetRGB32(uint colorMap, uint first, uint count, uint table)
        => WithColorMapLock(
            colorMap,
            false,
            () => GraphicsColorOperations.GetRgb32(Memory, _colorMaps, colorMap, first, count, table));

    internal bool LoadRGB4(uint viewPort, uint colors, short count)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.LoadRgb4(
                Memory,
                _colorMaps,
                viewPort,
                colors,
                count,
                Display as IGraphicsPaletteBackend));

    internal bool LoadRGB4(uint viewPort, uint colors, short count, long cycle)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.LoadRgb4(
                Memory,
                _colorMaps,
                viewPort,
                colors,
                count,
                Display as IGraphicsPaletteBackend,
                Display as IGraphicsTimedPaletteBackend,
                cycle));

    internal bool LoadRGB32(uint viewPort, uint table)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.LoadRgb32(
                Memory,
                _colorMaps,
                viewPort,
                table,
                Display as IGraphicsPaletteBackend,
                rgb32Palette: Display as IGraphicsRgb32PaletteBackend));

    internal bool LoadRGB32(uint viewPort, uint table, long cycle)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.LoadRgb32(
                Memory,
                _colorMaps,
                viewPort,
                table,
                Display as IGraphicsPaletteBackend,
                Display as IGraphicsTimedPaletteBackend,
                Display as IGraphicsRgb32PaletteBackend,
                Display as IGraphicsTimedRgb32PaletteBackend,
                cycle));

    internal bool SetRGB4(uint viewPort, int entry, byte red, byte green, byte blue)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.SetRgb4(
                Memory,
                _colorMaps,
                viewPort,
                entry,
                red,
                green,
                blue,
                Display as IGraphicsPaletteBackend));

    internal bool SetRGB4(uint viewPort, int entry, byte red, byte green, byte blue, long cycle)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.SetRgb4(
                Memory,
                _colorMaps,
                viewPort,
                entry,
                red,
                green,
                blue,
                Display as IGraphicsPaletteBackend,
                Display as IGraphicsTimedPaletteBackend,
                cycle));

    internal bool SetRGB32(uint viewPort, uint entry, uint red, uint green, uint blue)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.SetRgb32(
                Memory,
                _colorMaps,
                viewPort,
                entry,
                red,
                green,
                blue,
                Display as IGraphicsPaletteBackend,
                rgb32Palette: Display as IGraphicsRgb32PaletteBackend));

    internal bool SetRGB32(uint viewPort, uint entry, uint red, uint green, uint blue, long cycle)
        => WithViewportColorMapLock(
            viewPort,
            false,
            () => GraphicsColorOperations.SetRgb32(
                Memory,
                _colorMaps,
                viewPort,
                entry,
                red,
                green,
                blue,
                Display as IGraphicsPaletteBackend,
                Display as IGraphicsTimedPaletteBackend,
                Display as IGraphicsRgb32PaletteBackend,
                Display as IGraphicsTimedRgb32PaletteBackend,
                cycle));

    private T WithColorMapLock<T>(uint colorMap, T missing, Func<T> operation)
    {
        if (!GraphicsColorOperations.TryGetAllocationSyncRoot(_colorMaps, colorMap, out var syncRoot))
            return missing;

        lock (syncRoot)
        {
            var currentTask = _currentTask?.Invoke() ?? 0;
            if (!GraphicsColorOperations.TryEnterPaletteSemaphore(
                    Memory,
                    _colorMaps,
                    colorMap,
                    currentTask))
                return missing;

            try
            {
                return operation();
            }
            finally
            {
                GraphicsColorOperations.ExitPaletteSemaphore(
                    Memory,
                    _colorMaps,
                    colorMap,
                    currentTask);
            }
        }
    }

    private T WithViewportColorMapLock<T>(uint viewPort, T missing, Func<T> operation)
    {
        // Palette vectors consume the ViewPort through aligned guest LONG
        // accesses before they can inspect or lock its ColorMap.  Preserve
        // the native/provider boundary for an odd base instead of allowing a
        // permissive host memory adapter to lock or mutate an owned map.
        if (viewPort != 0 && (viewPort & 1u) != 0)
            return missing;

        if (viewPort == 0 ||
            viewPort > uint.MaxValue -
                (uint)(GraphicsLayouts.ViewPortColorMap + sizeof(uint) - 1) ||
            !Memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortColorMap,
                out var colorMap) ||
            colorMap == 0)
        {
            // A viewport without an attached ColorMap is still a valid
            // palette-provider call; the operation owns its own no-map
            // behavior.  Only a present map is required to use the host lock.
            return operation();
        }

        return WithColorMapLock(colorMap, missing, operation);
    }

    internal bool InitializeRastPort(uint rastPort)
        => GraphicsRasterOperations.InitializeRastPort(Memory, rastPort);

    internal bool InitializeRastPort(
        uint rastPort,
        uint defaultFont,
        GraphicsFontMetrics defaultMetrics)
        => GraphicsRasterOperations.InitializeRastPort(
            Memory,
            rastPort,
            defaultFont,
            defaultMetrics);

    internal bool InitializeView(uint view)
        => GraphicsRasterOperations.InitializeView(Memory, view);

    internal bool InitializeViewPort(uint viewPort)
        => GraphicsRasterOperations.InitializeViewPort(Memory, viewPort);

    internal bool InitializeRasInfo(uint rasInfo)
        => GraphicsRasterOperations.InitializeRasInfo(Memory, rasInfo);

    internal bool InitializeTmpRas(uint tmpRas, uint buffer, uint byteCount)
        => GraphicsRasterOperations.InitializeTmpRas(Memory, tmpRas, buffer, byteCount);

    internal bool InitializeBitMap(uint bitMap, byte depth, ushort width, ushort height)
        => GraphicsRasterOperations.InitializeBitMap(Memory, bitMap, depth, width, height);

    internal bool InitializeScreenResourceChain(
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        byte depth,
        ushort width,
        ushort height,
        uint plane0)
        => GraphicsRasterOperations.InitializeScreenResourceChain(
            Memory,
            view,
            viewPort,
            rasInfo,
            bitMap,
            depth,
            width,
            height,
            plane0);

    /// <summary>
    /// Publishes a caller-owned standard-planar chain with one explicit
    /// plane pointer per BitMap depth.  The existing scalar overload remains
    /// the compatibility path for depth one.
    /// </summary>
    internal bool InitializeScreenResourceChainPlanes(
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        byte depth,
        ushort width,
        ushort height,
        IReadOnlyList<uint> planes)
        => GraphicsRasterOperations.InitializeScreenResourceChainPlanes(
            Memory,
            view,
            viewPort,
            rasInfo,
            bitMap,
            depth,
            width,
            height,
            planes);

    internal bool TeardownScreenResourceChain(
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap)
        => GraphicsRasterOperations.TeardownScreenResourceChain(
            Memory,
            view,
            viewPort,
            rasInfo,
            bitMap);

    internal bool TeardownScreenResourceChainPlanes(
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        IReadOnlyList<uint> planes)
        => GraphicsRasterOperations.TeardownScreenResourceChainPlanes(
            Memory,
            view,
            viewPort,
            rasInfo,
            bitMap,
            planes);

    /// <summary>
    /// Allocates the four public envelopes for one portable screen chain.
    /// The plane pointer is supplied by the caller/provider and is never
    /// allocated or released by this ownership boundary.
    /// </summary>
    internal bool TryAllocateScreenResourceChain(
        byte depth,
        ushort width,
        ushort height,
        uint plane0,
        out ScreenResourceChainAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenOperations.TryAllocate(
                Memory,
                Allocator,
                _screenChains,
                depth,
                width,
                height,
                plane0,
                out allocation);
    }

    /// <summary>
    /// Allocates the public screen envelopes around a caller-owned,
    /// depth-sized standard-planar table. Plane storage remains outside this
    /// allocator transaction and is never cleared or released here.
    /// </summary>
    internal bool TryAllocateScreenResourceChainPlanes(
        byte depth,
        ushort width,
        ushort height,
        IReadOnlyList<uint> planes,
        out ScreenResourceChainAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenOperations.TryAllocatePlanes(
                Memory,
                Allocator,
                _screenChains,
                depth,
                width,
                height,
                planes,
                out allocation);
    }

    /// <summary>
    /// Allocates a standard-planar chip plane together with the public screen
    /// envelopes. Set <paramref name="requireDisplayDma"/> only when this
    /// owner has an explicit display-DMA capability; the caller-owned-plane
    /// form remains available through <see cref="TryAllocateScreenResourceChain"/>.
    /// </summary>
    internal bool TryAllocateScreenResourceChainWithPlane(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlane,
        bool requireDisplayDma,
        out ScreenResourceChainAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenOperations.TryAllocateWithPlane(
                Memory,
                Allocator,
                _screenChains,
                depth,
                width,
                height,
                clearPlane,
                requireDisplayDma,
                out allocation);
    }

    /// <summary>
    /// Allocates one contiguous chip span for all standard-planar planes and
    /// publishes the derived depth-sized plane table with the public chain.
    /// The span is released as one owner resource on exact close.
    /// </summary>
    internal bool TryAllocateScreenResourceChainWithPlanes(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlanes,
        bool requireDisplayDma,
        out ScreenResourceChainAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenOperations.TryAllocateWithPlanes(
                Memory,
                Allocator,
                _screenChains,
                depth,
                width,
                height,
                clearPlanes,
                requireDisplayDma,
                out allocation);
    }

    /// <summary>
    /// Closes and releases an allocator-owned screen chain.  A foreign,
    /// replaced, or already-closed view remains available to its owner.
    /// </summary>
    internal int FreeScreenResourceChain(uint view)
    {
        lock (_monitorStateSync)
            return GraphicsScreenOperations.Free(
                Memory,
                Allocator,
                _screenChains,
                view);
    }

    internal bool IsOwnedScreenResourceChain(uint view)
    {
        lock (_monitorStateSync)
            return _screenChains.Contains(view);
    }

    /// <summary>
    /// Allocates one classic Intuition Screen envelope with its embedded
    /// ViewPort/RastPort/BitMap prefix, plus a separate View and RasInfo.
    /// The screen owner supplies the plane unless the explicit chip-plane
    /// form is selected; this boundary does not claim CyberGraphX surfaces.
    /// </summary>
    internal bool TryAllocateScreenPrefix(
        byte depth,
        ushort width,
        ushort height,
        uint plane0,
        out ScreenPrefixAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenPrefixOperations.TryAllocate(
                Memory,
                Allocator,
                _screenPrefixes,
                depth,
                width,
                height,
                plane0,
                out allocation);
    }

    /// <summary>
    /// Allocates the classic Screen prefix around a caller-owned,
    /// depth-sized standard-planar table. The Screen/View/RasInfo envelopes
    /// are owned here; the supplied plane spans remain external.
    /// </summary>
    internal bool TryAllocateScreenPrefixPlanes(
        byte depth,
        ushort width,
        ushort height,
        IReadOnlyList<uint> planes,
        out ScreenPrefixAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenPrefixOperations.TryAllocatePlanes(
                Memory,
                Allocator,
                _screenPrefixes,
                depth,
                width,
                height,
                planes,
                out allocation);
    }

    internal bool TryAllocateScreenPrefixWithPlane(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlane,
        bool requireDisplayDma,
        out ScreenPrefixAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenPrefixOperations.TryAllocateWithPlane(
                Memory,
                Allocator,
                _screenPrefixes,
                depth,
                width,
                height,
                clearPlane,
                requireDisplayDma,
                out allocation);
    }

    /// <summary>
    /// Allocates one contiguous chip span for all standard-planar planes in
    /// the embedded Screen prefix. The span is released as one owner resource
    /// on exact close.
    /// </summary>
    internal bool TryAllocateScreenPrefixWithPlanes(
        byte depth,
        ushort width,
        ushort height,
        bool clearPlanes,
        bool requireDisplayDma,
        out ScreenPrefixAllocation allocation)
    {
        lock (_monitorStateSync)
            return GraphicsScreenPrefixOperations.TryAllocateWithPlanes(
                Memory,
                Allocator,
                _screenPrefixes,
                depth,
                width,
                height,
                clearPlanes,
                requireDisplayDma,
                out allocation);
    }

    internal int FreeScreenPrefix(uint screen)
    {
        lock (_monitorStateSync)
            return GraphicsScreenPrefixOperations.Free(
                Memory,
                Allocator,
                _screenPrefixes,
                screen);
    }

    internal bool IsOwnedScreenPrefix(uint screen)
    {
        lock (_monitorStateSync)
            return _screenPrefixes.Contains(screen);
    }

    internal bool InitializeArea(uint areaInfo, uint buffer, short maxVectors)
        => GraphicsRasterOperations.InitializeArea(Memory, areaInfo, buffer, maxVectors);

    internal int ChangeViewPortBitMap(uint viewPort, uint bitMap)
        => GraphicsRasterOperations.ChangeViewPortBitMap(Memory, viewPort, bitMap);

    internal int BltBitMap(
        uint sourceBitMap,
        short sourceX,
        short sourceY,
        uint destinationBitMap,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        byte mask,
        uint tempA,
        Func<int, int, bool>? pixelVisible = null)
        => GraphicsBlitOperations.BltBitMap(
            Memory,
            sourceBitMap,
            sourceX,
            sourceY,
            destinationBitMap,
            destinationX,
            destinationY,
            width,
            height,
            minterm,
            mask,
            tempA,
            Allocator,
            pixelVisible: pixelVisible);

    internal int BltClear(uint address, uint byteCount, uint flags)
        => GraphicsBlitOperations.BltClear(Memory, address, byteCount, flags);

    internal int ClipBlit(
        uint sourceRastPort,
        short sourceX,
        short sourceY,
        uint destinationRastPort,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        Func<int, int, bool>? pixelVisible = null)
        => GraphicsBlitOperations.ClipBlit(
            Memory,
            sourceRastPort,
            sourceX,
            sourceY,
            destinationRastPort,
            destinationX,
            destinationY,
            width,
            height,
            minterm,
            Allocator,
            pixelVisible: pixelVisible);

    internal int BltBitMapRastPort(
        uint sourceBitMap,
        short sourceX,
        short sourceY,
        uint destinationRastPort,
        short destinationX,
        short destinationY,
        short width,
        short height,
        byte minterm,
        Func<int, int, bool>? pixelVisible = null)
        => GraphicsBlitOperations.BltBitMapRastPort(
            Memory,
            sourceBitMap,
            sourceX,
            sourceY,
            destinationRastPort,
            destinationX,
            destinationY,
            width,
            height,
            minterm,
            Allocator,
            pixelVisible: pixelVisible);

    internal int BltMaskBitMapRastPort(
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
        => GraphicsBlitOperations.BltMaskBitMapRastPort(
            Memory,
            sourceBitMap,
            sourceX,
            sourceY,
            destinationRastPort,
            destinationX,
            destinationY,
            width,
            height,
            minterm,
            blitMask,
            pixelVisible: pixelVisible);

    internal int BltPattern(
        uint rastPort,
        uint mask,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        short byteCount)
    {
        return GraphicsRasterOperations.BltPattern(
            Memory,
            rastPort,
            mask,
            xMin,
            yMin,
            xMax,
            yMax,
            unchecked((uint)byteCount),
            rejectNegativeByteCount: byteCount < 0);
    }

    internal int BltTemplate(
        uint templateAddress,
        short sourceX,
        short sourceModulo,
        uint rastPort,
        short destinationX,
        short destinationY,
        short width,
        short height)
        => GraphicsBlitOperations.BltTemplate(
            Memory,
            templateAddress,
            sourceX,
            sourceModulo,
            rastPort,
            destinationX,
            destinationY,
            width,
            height);

    internal int ChangeViewPortBitMap(uint viewPort, uint bitMap, uint dbufInfo)
    {
        // Validate the complete swap envelope before asking a presentation
        // owner about capability. A malformed native request must remain an
        // unclaimed provider/native request and must not trigger even a
        // capability probe with provider-visible side effects.
        if (!GraphicsRasterOperations.TryValidateViewPortBitMapChange(
                Memory,
                viewPort,
                bitMap,
                dbufInfo,
                _allowGuestOwnedDbufInfo ? null : _dbufInfos,
                out _,
                out _))
            return GraphicsRasterOperations.Failure;

        // Keep the direct host entry on the same ownership boundary as the
        // scheduler-aware TryChangeViewPortBitMap path.  A status adapter can
        // be present while its display/rebuild provider is unbound; do not
        // publish a guest bitmap association that no presentation owner can
        // observe, or the native/provider vector would be swallowed.
        if (Display is IGraphicsViewportPresentationStatusBackend presentationStatus &&
            !presentationStatus.CanChangeViewPortBitMap(dbufInfo))
        {
            return GraphicsRasterOperations.Failure;
        }

        var result = GraphicsRasterOperations.ChangeViewPortBitMap(
            Memory,
            viewPort,
            bitMap,
            dbufInfo,
            out var previousBitMap,
            _allowGuestOwnedDbufInfo ? null : _dbufInfos);
        if (result == GraphicsRasterOperations.Success &&
            Display is IGraphicsViewportBitmapDisplayBackend bitmapDisplay)
        {
            bitmapDisplay.ChangeViewPortBitMap(viewPort, bitMap, dbufInfo);
        }

        if (result == GraphicsRasterOperations.Success && dbufInfo != 0 &&
            Display is IGraphicsDoubleBufferMessageBackend messageBackend)
        {
            messageBackend.ScheduleDoubleBufferMessages(
                viewPort,
                previousBitMap,
                bitMap,
                dbufInfo,
                0);
        }

        return result;
    }

    internal int MakeViewPort(uint view, uint viewPort)
    {
        if (view == 0 || !GraphicsRasterOperations.ValidateView(Memory, view))
            return GraphicsViewportStatuses.NoViewPortExtra;

        if (viewPort == 0 ||
            !GraphicsRasterOperations.ValidateViewPortForScroll(Memory, viewPort))
        {
            return GraphicsViewportStatuses.NoViewPortExtra;
        }

        if (!GraphicsRasterOperations.ValidateViewPortForMake(
                Memory,
                view,
                viewPort))
        {
            return GraphicsViewportStatuses.NoDisplay;
        }

        // MakeVPort is a single guest operation.  A status-aware copper
        // builder may publish its public DspIns/SprIns/ClrIns/UCopIns links
        // before discovering that it cannot finish the stream.  Snapshot the
        // exact four links before any batch replay, temporary ViewPortExtra
        // allocation, or provider callback so a failed handoff cannot expose
        // a partial copper list to a later LoadView/FreeVPortCopLists call.
        // The complete MakeVPort envelope above guarantees these link fields
        // are part of the validated guest ViewPort, but keep the explicit
        // status boundary if a sparse provider-backed memory adapter still
        // refuses one of them.
        var originalCopperLinks = SnapshotViewPortCopperLinks(viewPort);
        if (originalCopperLinks is null)
        {
            return GraphicsViewportStatuses.NoDisplay;
        }

        // A ColorMap with VIDEOCONTROL_BATCH owns a static VideoControl list
        // that must be replayed at every MakeVPort.  Apply that list only
        // after the public ViewPort/RasInfo envelope is valid and before a
        // temporary ViewPortExtra or copper backend can publish state.
        if (!GraphicsColorOperations.TryApplyEnabledVideoControlBatch(
                Memory,
                _colorMaps,
                _extendedNodes,
                viewPort,
                out _))
        {
            return GraphicsViewportStatuses.NoDisplay;
        }

        // Kickstart's MakeVPort requires a ViewPortExtra association for the
        // display-specific state it derives while building the intermediate
        // copper list.  Callers commonly create that node explicitly through
        // GfxNew/GfxAssociate, but the library also owns a temporary node when
        // no association exists.  Keep a pre-existing association untouched;
        // in particular, do not replace a foreign extended-node type merely
        // because MakeVPort was called.
        var associatedNode = _extendedNodes.LookUp(viewPort);
        var ownsTemporaryExtra = false;
        if (associatedNode != 0)
        {
            if (!_extendedNodes.IsViewPortExtra(associatedNode))
                return GraphicsViewportStatuses.NoViewPortExtra;
        }
        else
        {
            associatedNode = _extendedNodes.New(
                Memory,
                Allocator,
                GraphicsExtendedNodeOperations.ViewPortExtraType);
            if (associatedNode == 0 ||
                !_extendedNodes.AssociateViewPortExtra(Memory, viewPort, associatedNode))
            {
                if (associatedNode != 0)
                    _ = _extendedNodes.Free(Memory, Allocator, associatedNode);

                return GraphicsViewportStatuses.NoViewPortExtra;
            }

            // VPXF_FREE_ME is the guest-visible ownership marker used by
            // FreeVPortCopLists to distinguish this temporary node from a
            // caller-owned GfxNew/ViewPortExtra object.
            if (!Memory.TryWriteWord(
                    associatedNode + (uint)GraphicsLayouts.ViewPortExtraFlags,
                    1))
            {
                _ = _extendedNodes.Free(Memory, Allocator, associatedNode);
                return GraphicsViewportStatuses.NoViewPortExtra;
            }

            ownsTemporaryExtra = true;
            _temporaryViewPortExtras[associatedNode] = viewPort;
        }

        int result;
        if (Display is IGraphicsCopperBuildStatusBackend statusBackend)
        {
            result = statusBackend.MakeViewPortStatus(view, viewPort);
        }
        else if (Display is not IGraphicsCopperBackend copperBackend)
        {
            result = GraphicsViewportStatuses.NoDisplay;
        }
        else
        {
            copperBackend.MakeViewPort(view, viewPort);
            result = GraphicsViewportStatuses.Ok;
        }

        if (result != GraphicsViewportStatuses.Ok)
        {
            // The provider boundary is allowed to mutate the guest links
            // while constructing its private stream.  Restore the snapshot
            // before releasing a temporary association so the failed call is
            // fully retryable and native/provider ownership remains clear.
            RestoreViewPortCopperLinks(viewPort, originalCopperLinks);

            if (ownsTemporaryExtra)
            {
                _temporaryViewPortExtras.Remove(associatedNode);
                _ = _extendedNodes.Free(Memory, Allocator, associatedNode);
            }
        }

        return result;
    }

    /// <summary>
    /// Reports the owned ColorMap envelope that MakeVPort may mutate while
    /// replaying a static VideoControl batch. Copper links and allocation of
    /// a temporary ViewPortExtra are handled by their own boundaries; this
    /// helper keeps the batch guest state separate for native admission.
    /// </summary>
    internal bool TryGetNativeMakeViewPortPublication(
        uint viewPort,
        out uint colorMap,
        out ulong bytes)
    {
        colorMap = 0;
        bytes = 0;
        if (!GraphicsColorOperations.TryGetEnabledVideoControlBatchColorMap(
                Memory,
                _colorMaps,
                viewPort,
                out var batchMap,
                out var hasBatch))
        {
            return false;
        }

        if (!hasBatch)
            return true;

        colorMap = batchMap;
        bytes = (uint)GraphicsLayouts.ColorMapSize;
        return true;
    }

    /// <summary>
    /// Native-overlay MakeVPort may inspect a resource-bearing viewport, but
    /// it must not claim the vector unless the display bridge explicitly owns
    /// the CPR/raw copper construction that a non-NULL RasInfo requires.
    /// NULL RasInfo is the assembling form and remains a pure portable
    /// admission; unreadable envelopes stay on the captured owner.
    /// </summary>
    internal bool CanClaimNativeMakeViewPortResources(uint viewPort)
    {
        if (viewPort == 0 || (viewPort & 1u) != 0)
            return true;

        if (viewPort > uint.MaxValue -
            (uint)(GraphicsLayouts.ViewPortRasInfo + sizeof(uint) - 1) ||
            !Memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var rasInfo))
        {
            return false;
        }

        return rasInfo == 0 ||
            (Display is IGraphicsCopperBuildResourceStatusBackend resourceOwner &&
             resourceOwner.HasExplicitMakeVPortOwner);
    }

    internal bool IsNativeMakeViewPortBatchPublicationSafe(
        uint viewPort,
        Func<uint, ulong, bool> canPublish)
        => GraphicsColorOperations.IsNativeOverlayVideoControlBatchSafe(
            Memory,
            _colorMaps,
            viewPort,
            canPublish);

    internal int MergeCopperLists(uint view)
    {
        if (!GraphicsRasterOperations.ValidateViewForCopperMerge(
                Memory,
                view,
                out var hasVisibleViewPort))
            return GraphicsRasterOperations.Failure;

        if (!Memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                out var firstViewPort) ||
            firstViewPort == 0)
        {
            return GraphicsCopperOperations.MergeNoOp;
        }

        // Resident MrgCop reports MCOP_NOP when every linked ViewPort is
        // hidden.  Do not ask a display provider to rebuild a stream that the
        // native loop would never inspect or publish.
        if (!hasVisibleViewPort)
            return GraphicsCopperOperations.MergeNoOp;

        // MrgCop is a single display-chain handoff.  A status-aware copper
        // owner may relink one or more public ViewPort copper fields while
        // constructing its merged stream and then report MCOP_NOMEM (or
        // another failure).  Keep the validated visible chain retryable by
        // journaling those four public links before crossing that boundary.
        // Hidden ViewPorts are intentionally excluded: the resident MrgCop
        // loop skips their payload entirely, so a sparse hidden node must not
        // make an otherwise valid merge unclaimed.
        var originalCopperLinks = SnapshotVisibleViewPortCopperLinks(view);
        if (originalCopperLinks is null)
            return GraphicsRasterOperations.Failure;

        if (Display is IGraphicsCopperMergeStatusBackend statusBackend)
        {
            var result = statusBackend.MergeCopperListsStatus(view);
            if (result != GraphicsCopperOperations.MergeOk)
                RestoreVisibleViewPortCopperLinks(originalCopperLinks);

            return result;
        }

        if (Display is not IGraphicsCopperBackend copperBackend)
        {
            return GraphicsRasterOperations.Failure;
        }

        copperBackend.MergeCopperLists(view);
        return GraphicsCopperOperations.MergeOk;
    }

    private List<(uint ViewPort, uint[] Links)>? SnapshotVisibleViewPortCopperLinks(
        uint view)
    {
        if (!TryReadLongAt(
                view,
                GraphicsLayouts.ViewViewPort,
                out var viewPort))
        {
            return null;
        }

        var snapshots = new List<(uint ViewPort, uint[] Links)>();
        for (var index = 0; index < 64 && viewPort != 0; index++)
        {
            var currentViewPort = viewPort;
            if (!TryReadWordAt(
                    currentViewPort,
                    GraphicsLayouts.ViewPortModes,
                    out var modes) ||
                !TryReadLongAt(
                    currentViewPort,
                    GraphicsLayouts.ViewPortNext,
                    out viewPort))
            {
                return null;
            }

            if ((modes & GraphicsModeIds.ViewPortHidden) != 0)
                continue;

            var links = SnapshotViewPortCopperLinks(currentViewPort);
            if (links is null)
                return null;

            snapshots.Add((currentViewPort, links));
        }

        return viewPort == 0 ? snapshots : null;
    }

    private void RestoreVisibleViewPortCopperLinks(
        IReadOnlyList<(uint ViewPort, uint[] Links)> snapshots)
    {
        foreach (var snapshot in snapshots)
            RestoreViewPortCopperLinks(snapshot.ViewPort, snapshot.Links);
    }

    /// <summary>
    /// Validates the public View/ViewPort envelopes before asking a native
    /// copper/display backend to count the viewport's instruction scan lines.
    /// CalcIVG is not a portable arithmetic operation: the result depends on
    /// the actual merged copper stream, so an absent backend leaves the vector
    /// unclaimed rather than manufacturing a plausible count.
    /// </summary>
    internal bool TryCalcIvg(uint view, uint viewPort, out ushort scanLines)
    {
        scanLines = 0;
        if (!GraphicsRasterOperations.ValidateViewForCalcIvg(
                Memory,
                view,
                viewPort))
        {
            return false;
        }

        return Display is IGraphicsCalcIvgBackend calcBackend &&
            calcBackend.TryCalcIvg(view, viewPort, out scanLines);
    }

    /// <summary>
    /// SetChipRev is a chipset capability query/update, not a portable
    /// raster operation.  Leave the vector unclaimed when no native provider
    /// is installed so a ROM or later host layer can supply the policy.
    /// </summary>
    internal bool TrySetChipRev(uint requestedBits, out uint actualBits)
    {
        actualBits = 0;
        if (Display is not IGraphicsChipRevisionBackend chipBackend ||
            !chipBackend.TrySetChipRev(requestedBits, out actualBits))
        {
            actualBits = 0;
            return false;
        }

        return true;
    }

    internal int FreeViewPortCopLists(uint viewPort)
    {
        // The native vector's GfxLookUp(NULL) form is a successful void
        // no-op.  Keep it ahead of the optional owner snapshot, whose
        // null-address result otherwise looks like an unowned malformed
        // envelope and would incorrectly decline the call.
        if (viewPort == 0)
            return GraphicsRasterOperations.Success;

        var hasStatusOwner = Display is IGraphicsCopperResourceStatusBackend;
        var hasLegacyOwner = Display is IGraphicsCopperResourceBackend;
        var originalLinks = hasStatusOwner || !hasLegacyOwner
            ? SnapshotViewPortCopperLinks(viewPort)
            : null;
        var result = GraphicsRasterOperations.FreeViewPortCopLists(Memory, viewPort);
        if (result == GraphicsRasterOperations.Success)
        {
            if (Display is IGraphicsCopperResourceStatusBackend statusBackend)
            {
                if (!statusBackend.TryFreeVPortCopLists(viewPort))
                {
                    RestoreViewPortCopperLinks(viewPort, originalLinks);
                    return GraphicsRasterOperations.Failure;
                }
            }
            else if (Display is IGraphicsCopperResourceBackend resourceBackend)
            {
                resourceBackend.FreeVPortCopLists(viewPort);
            }
            else
            {
                // A clean ViewPort has no display-resource ownership left to
                // hand off.  Kickstart's FreeVPortCopLists therefore reduces
                // this form to a successful void no-op even when no copper
                // owner is attached.  Keep non-empty links transparent so a
                // native/provider owner can still release its private lists.
                var hadOwnedLinks = originalLinks is null;
                if (originalLinks is not null)
                {
                    foreach (var link in originalLinks)
                    {
                        if (link != 0)
                        {
                            hadOwnedLinks = true;
                            break;
                        }
                    }
                }

                if (hadOwnedLinks)
                {
                    RestoreViewPortCopperLinks(viewPort, originalLinks);
                    return GraphicsRasterOperations.Failure;
                }
            }
        }

        if (result == GraphicsRasterOperations.Success)
        {
            var associatedNode = _extendedNodes.LookUp(viewPort);
            var freeTemporary = false;
            if (associatedNode != 0 && _extendedNodes.IsViewPortExtra(associatedNode))
            {
                freeTemporary = Memory.TryReadWord(
                    associatedNode + (uint)GraphicsLayouts.ViewPortExtraFlags,
                    out var flags) &&
                    (flags & 1) != 0;
            }

            if (!freeTemporary)
            {
                // A caller-owned ViewPortExtra may have replaced a temporary
                // node while that node's immediate cleanup was declined by a
                // sparse guest bridge.  The displaced node is no longer the
                // active lookup result, so find pending temporary ownership
                // by viewport rather than requiring its key to equal the
                // current caller-owned association.
                foreach (var pair in _temporaryViewPortExtras)
                {
                    if (pair.Value == viewPort)
                    {
                        associatedNode = pair.Key;
                        freeTemporary = true;
                        break;
                    }
                }
            }

            if (freeTemporary && associatedNode != 0)
            {
                if (_extendedNodes.Free(Memory, Allocator, associatedNode))
                    _temporaryViewPortExtras.Remove(associatedNode);
            }
        }

        return result;
    }

    private uint[]? SnapshotViewPortCopperLinks(uint viewPort)
    {
        // FreeVPortCopLists owns only the four public copper links.  Do not
        // require the unrelated ViewPort header to fit: native/provider
        // callers may expose a sparse tail where the links are readable but
        // the geometry fields (or the full struct envelope) are not.
        if (viewPort == 0 ||
            viewPort > uint.MaxValue -
                (uint)(GraphicsLayouts.ViewPortUCopIns + sizeof(uint) - 1))
        {
            return null;
        }

        var offsets = new[]
        {
            GraphicsLayouts.ViewPortDspIns,
            GraphicsLayouts.ViewPortSprIns,
            GraphicsLayouts.ViewPortClrIns,
            GraphicsLayouts.ViewPortUCopIns
        };
        var links = new uint[offsets.Length];
        for (var index = 0; index < offsets.Length; index++)
        {
            if (!Memory.TryReadLong(viewPort + (uint)offsets[index], out links[index]))
                return null;
        }

        return links;
    }

    private void RestoreViewPortCopperLinks(uint viewPort, uint[]? links)
    {
        // Match SnapshotViewPortCopperLinks' narrow ownership envelope so a
        // failed owner handoff can restore the links even when the rest of
        // the guest ViewPort lies beyond the 32-bit address boundary.
        if (links is null ||
            links.Length != 4 ||
            viewPort == 0 ||
            viewPort > uint.MaxValue -
                (uint)(GraphicsLayouts.ViewPortUCopIns + sizeof(uint) - 1))
            return;

        var offsets = new[]
        {
            GraphicsLayouts.ViewPortDspIns,
            GraphicsLayouts.ViewPortSprIns,
            GraphicsLayouts.ViewPortClrIns,
            GraphicsLayouts.ViewPortUCopIns
        };
        for (var index = 0; index < offsets.Length; index++)
            RestoreGuestLong(viewPort + (uint)offsets[index], links[index]);
    }

    private bool RestoreGuestLong(uint address, uint value)
    {
        var success = true;
        success &= Memory.TryWriteByte(address, (byte)(value >> 24));
        success &= Memory.TryWriteByte(address + 1u, (byte)(value >> 16));
        success &= Memory.TryWriteByte(address + 2u, (byte)(value >> 8));
        success &= Memory.TryWriteByte(address + 3u, (byte)value);
        return success;
    }

    private bool RestoreGuestWord(uint address, ushort value)
    {
        var success = true;
        success &= Memory.TryWriteByte(address, (byte)(value >> 8));
        success &= Memory.TryWriteByte(address + 1u, (byte)value);
        return success;
    }

    internal uint UCopperListInit(uint userList, ushort instructionCount)
        => GraphicsCopperOperations.UCopperListInit(
            Memory,
            Allocator,
            _copperLists,
            userList,
            instructionCount);

    internal bool CMove(uint userList, uint hardwareRegister, ushort value)
        => GraphicsCopperOperations.CMove(
            Memory,
            _copperLists,
            userList,
            hardwareRegister,
            value);

    internal bool CWait(uint userList, ushort vertical, ushort horizontal)
        => GraphicsCopperOperations.CWait(
            Memory,
            _copperLists,
            userList,
            vertical,
            horizontal);

    internal bool TryGetCurrentCopperInstructionAddress(
        uint userList,
        out uint instruction)
        => GraphicsCopperOperations.TryGetCurrentInstructionAddress(
            Memory,
            _copperLists,
            userList,
            out instruction);

    internal bool TryGetCopperBumpPublicationState(
        uint userList,
        out uint copList,
        out ushort count,
        out ushort maxCount,
        out uint nextCopList)
        => GraphicsCopperOperations.TryGetCBumpPublicationState(
            Memory,
            _copperLists,
            userList,
            out copList,
            out count,
            out maxCount,
            out nextCopList);

    internal bool CBump(uint userList)
        => GraphicsCopperOperations.CBump(
            Memory,
            _copperLists,
            Allocator,
            userList);

    internal bool CEnd(uint userList)
        => GraphicsCopperOperations.CEnd(
            Memory,
            _copperLists,
            Allocator,
            userList);

    internal bool FreeCopList(uint copList)
        => GraphicsCopperOperations.FreeCopList(
            Memory,
            Allocator,
            _copperLists,
            copList);

    /// <summary>
    /// Resolves the public <c>UCopList</c> envelope that a registry-owned
    /// intermediate copper list will clear during native-overlay teardown.
    /// A NULL list is the native no-op; a non-owned list remains available to
    /// the captured resident/provider vector.
    /// </summary>
    internal bool TryGetNativeFreeCopListPublication(
        uint copList,
        out uint userList)
        => GraphicsCopperOperations.TryGetUserListForCopList(
            _copperLists,
            copList,
            out userList);

    internal bool FreeCprList(uint cprList)
    {
        // The native teardown walks a NULL cprlist head as an empty list.
        // Keep that successful void no-op before probing a public envelope
        // or asking an optional provider to release a private allocation.
        if (cprList == 0)
            return true;

        // CprList is a native word-aligned structure. Reject an odd guest
        // address before probing the public envelope or invoking a provider;
        // the 68000 ABI cannot legally dereference this layout at an odd
        // boundary.
        if ((cprList & 1u) != 0)
            return false;

        if (cprList > uint.MaxValue - (uint)(GraphicsLayouts.CprListSize - 1))
        {
            return false;
        }

        for (var offset = 0; offset < GraphicsLayouts.CprListSize; offset++)
        {
            if (!Memory.TryReadByte(cprList + (uint)offset, out _))
                return false;
        }

        if (Display is IGraphicsCopperResourceStatusBackend statusBackend)
            return statusBackend.TryFreeCprList(cprList);

        if (Display is IGraphicsCopperResourceBackend resourceBackend)
        {
            resourceBackend.FreeCprList(cprList);
            return true;
        }

        // A readable envelope is not, by itself, proof that this graphics
        // instance owns the private wrapper/raw allocation. Leave the call
        // available to the native ROM or provider when no owner is attached.
        return false;
    }

    /// <summary>
    /// Reports whether the native-overlay adapter has a result-bearing owner
    /// boundary for a non-null hardware-facing CprList.  The legacy void
    /// resource callback cannot distinguish a portable allocation from a
    /// resident/provider list, so a mapped native vector must leave that form
    /// to the captured owner.  A status-aware owner performs the final
    /// address-specific admission in <see cref="FreeCprList"/>.
    /// </summary>
    internal bool CanClaimNativeCprList(uint cprList)
        => cprList == 0 ||
           (Display is IGraphicsCopperNativeResourceStatusBackend nativeOwner &&
            nativeOwner.HasExplicitCprListOwner);

    /// <summary>
    /// Reports whether a native-overlay FreeVPortCopLists call has an
    /// explicit result-bearing owner for a non-null ViewPort. A legacy void
    /// callback cannot distinguish a resident/provider copper envelope from
    /// one created by this portable graphics instance.
    /// </summary>
    internal bool CanClaimNativeVPortCopLists(uint viewPort)
        => viewPort == 0 ||
           (Display is IGraphicsCopperNativeResourceStatusBackend nativeOwner &&
            nativeOwner.HasExplicitVPortCopListOwner);

    /// <summary>
    /// Reports the optional guest backlink that FreeVPortCopLists will clear
    /// after releasing the four public copper links.  The native-overlay
    /// adapter uses this side-effect-free plan to admit the complete chained
    /// teardown: a temporary ViewPortExtra with an unreadable or read-only
    /// backlink must not let the preceding copper-link writes escape to a
    /// resident/provider owner.
    /// </summary>
    internal bool TryGetNativeFreeVPortCopListsPublication(
        uint viewPort,
        out uint backlink)
    {
        backlink = 0;
        if (viewPort == 0)
            return true;

        var associatedNode = _extendedNodes.LookUp(viewPort);
        var freeTemporary = false;
        if (associatedNode != 0 && _extendedNodes.IsViewPortExtra(associatedNode))
        {
            freeTemporary = Memory.TryReadWord(
                    associatedNode + (uint)GraphicsLayouts.ViewPortExtraFlags,
                    out var flags) &&
                (flags & 1) != 0;
        }

        if (!freeTemporary)
        {
            // A caller-owned node may have replaced a temporary association;
            // the displaced temporary remains pending in this map until a
            // later FreeVPortCopLists retry can release it.
            foreach (var pair in _temporaryViewPortExtras)
            {
                if (pair.Value == viewPort)
                {
                    associatedNode = pair.Key;
                    freeTemporary = true;
                    break;
                }
            }
        }

        if (!freeTemporary || associatedNode == 0)
            return true;

        return _extendedNodes.TryGetFreePublicationSpan(
            Memory,
            associatedNode,
            out backlink);
    }

    internal long ChangeViewPortBitMap(
        uint viewPort,
        uint bitMap,
        uint dbufInfo,
        long cycle)
    {
        _ = TryChangeViewPortBitMap(
            viewPort,
            bitMap,
            dbufInfo,
            cycle,
            out var nextCycle);
        return nextCycle;
    }

    /// <summary>
    /// Performs the timed ChangeVPBitMap transaction and preserves the
    /// portable validation result for the register/provider boundary.  The
    /// legacy timed wrapper above intentionally keeps its cycle-only ABI for
    /// existing display backends; register dispatch uses this status-aware
    /// form so malformed guest DBufInfo or bitmap envelopes are not claimed as
    /// successful void calls.
    /// </summary>
    internal bool TryChangeViewPortBitMap(
        uint viewPort,
        uint bitMap,
        uint dbufInfo,
        long cycle,
        out long nextCycle)
    {
        nextCycle = cycle;
        // Keep the complete malformed guest swap transparent before
        // consulting the status-aware presentation owner. Capability probes
        // belong after the portable envelope gate so native/provider fallback
        // sees no callback for an invalid viewport, bitmap, or DBufInfo.
        if (!GraphicsRasterOperations.TryValidateViewPortBitMapChange(
                Memory,
                viewPort,
                bitMap,
                dbufInfo,
                _allowGuestOwnedDbufInfo ? null : _dbufInfos,
                out _,
                out _))
            return false;

        if (Display is IGraphicsViewportPresentationStatusBackend presentationStatus &&
            !presentationStatus.CanChangeViewPortBitMap(dbufInfo))
        {
            return false;
        }

        var result = GraphicsRasterOperations.ChangeViewPortBitMap(
            Memory,
            viewPort,
            bitMap,
            dbufInfo,
            out var previousBitMap,
            _allowGuestOwnedDbufInfo ? null : _dbufInfos);
        if (result != GraphicsRasterOperations.Success)
            return false;

        if (Display is IGraphicsTimedViewportBitmapDisplayBackend timedBitmapDisplay)
        {
            nextCycle = timedBitmapDisplay.ChangeViewPortBitMap(
                viewPort,
                bitMap,
                dbufInfo,
                cycle);
        }
        else if (Display is IGraphicsViewportBitmapDisplayBackend bitmapDisplay)
            bitmapDisplay.ChangeViewPortBitMap(viewPort, bitMap, dbufInfo);

        if (dbufInfo != 0 && Display is IGraphicsDoubleBufferMessageBackend messageBackend)
        {
            messageBackend.ScheduleDoubleBufferMessages(
                viewPort,
                previousBitMap,
                bitMap,
                dbufInfo,
                nextCycle);
        }

        return true;
    }

    internal uint AllocDBufInfo(uint viewPort)
    {
        // Validate the guest viewport before asking a display owner about
        // capability.  A malformed native envelope must remain an
        // unclaimed/provider request and must not trigger a capability probe
        // with provider-visible side effects.
        if (!GraphicsRasterOperations.ValidateViewPortForDoubleBuffer(Memory, viewPort))
            return 0;

        if (Display is IGraphicsDoubleBufferCapabilityBackend capabilityBackend &&
            !capabilityBackend.SupportsDoubleBuffer(viewPort))
        {
            return 0;
        }

        return GraphicsDoubleBufferOperations.AllocDBufInfo(
            Memory,
            Allocator,
            _dbufInfos,
            viewPort);
    }

    internal int FreeDBufInfo(uint address)
    {
        var wasOwned = _dbufInfos.Contains(address);
        if (wasOwned &&
            Display is IGraphicsDoubleBufferMessageCancellationBackend cancellationBackend)
        {
            // Cancel before the allocator releases the guest envelope.  The
            // scheduler may otherwise observe a freed message port between
            // the ownership check and the next frame boundary.
            cancellationBackend.CancelDoubleBufferMessages(address);
        }

        var result = GraphicsDoubleBufferOperations.FreeDBufInfo(Allocator, _dbufInfos, address);
        return result;
    }

    internal bool ScrollViewPort(uint viewPort)
    {
        if (!GraphicsRasterOperations.ValidateViewPortForScrollOperation(Memory, viewPort))
            return false;

        if (Display is IGraphicsViewportPresentationStatusBackend presentationStatus &&
            !presentationStatus.CanScrollViewPort)
        {
            return false;
        }

        if (Display is IGraphicsViewportDisplayBackend viewportDisplay)
            viewportDisplay.ScrollViewPort(viewPort);

        return true;
    }

    internal long ScrollViewPort(uint viewPort, long cycle)
    {
        _ = TryScrollViewPort(viewPort, cycle, out var nextCycle);
        return nextCycle;
    }

    /// <summary>
    /// Timed ScrollVPort transaction with an explicit validation result for
    /// register dispatch.  The cycle-only wrapper remains for existing
    /// display backends, while malformed viewport chains must be allowed to
    /// fall through to the native/provider owner.
    /// </summary>
    internal bool TryScrollViewPort(uint viewPort, long cycle, out long nextCycle)
    {
        nextCycle = cycle;
        if (!GraphicsRasterOperations.ValidateViewPortForScrollOperation(Memory, viewPort))
            return false;

        if (Display is IGraphicsViewportPresentationStatusBackend presentationStatus &&
            !presentationStatus.CanScrollViewPort)
        {
            return false;
        }

        if (Display is IGraphicsTimedViewportDisplayBackend timedViewportDisplay)
        {
            nextCycle = timedViewportDisplay.ScrollViewPort(viewPort, cycle);
            return true;
        }

        if (Display is IGraphicsViewportDisplayBackend viewportDisplay)
            viewportDisplay.ScrollViewPort(viewPort);

        return true;
    }

    internal bool TryGetViewPortModeId(uint viewPort, out uint modeId)
    {
        modeId = GraphicsModeIds.Invalid;
        var ntsc = (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true;
        if (!GraphicsRasterOperations.TryReadViewPortColorMap(
                Memory,
                viewPort,
                out var colorMap))
        {
            return false;
        }

        // GetVPModeID reads vp_ColorMap first.  Once that link names an
        // instance-owned ColorMap, the resident path resolves the mode from
        // the map and does not consume the unrelated ViewPort geometry or
        // RasInfo envelope.  Keep the full assembling-chain validator only
        // for the null-map case, where the portable fallback derives the
        // native mode from vp_Modes.
        if (colorMap == 0 &&
            !GraphicsRasterOperations.ValidateViewPortForModeQuery(Memory, viewPort))
        {
            return false;
        }

        if (Display is IGraphicsViewModeBackend modeBackend &&
            modeBackend.TryGetModeId(viewPort, out modeId))
        {
            return true;
        }

        // An assembled viewport may have no ColorMap yet, but a non-null
        // foreign association is provider-owned. Do not turn that state into
        // a native INVALID_ID result: leave the vector available to a
        // CyberGraphX/native display provider instead.
        if (!GraphicsColorOperations.ValidateNativeViewPortColorMap(
                Memory,
                _colorMaps,
                viewPort))
        {
            return false;
        }

        modeId = GraphicsColorOperations.GetNativeViewPortModeId(
            Memory,
            _colorMaps,
            viewPort,
            ntsc);
        return true;
    }

    internal uint GetViewPortModeId(uint viewPort)
        => TryGetViewPortModeId(viewPort, out var modeId)
            ? modeId
            : GraphicsModeIds.Invalid;

    internal uint FindDisplayInfo(uint modeId)
        => GraphicsDisplayDatabase.FindDisplayInfo(modeId);

    internal bool IsNativeDisplayMode(uint modeId)
        => GraphicsDisplayDatabase.IsNativeModeId(modeId);

    internal bool IsNativeDisplayInfoHandle(uint handle)
        => GraphicsDisplayDatabase.IsNativeDisplayInfoHandle(handle);

    internal bool IsNativeDisplayModeQuery(uint modeId)
        => GraphicsDisplayDatabase.IsNativeModeQuery(modeId);

    internal uint NextDisplayInfo(uint lastModeId)
        => GraphicsDisplayDatabase.NextDisplayInfo(lastModeId);

    internal uint ModeNotAvailable(uint modeId)
        => GraphicsDisplayDatabase.ModeNotAvailable(
            modeId,
            (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false);

    internal int GetDisplayInfoData(
        uint handle,
        uint buffer,
        uint bufferSize,
        uint tag,
        uint modeId)
    {
        lock (_monitorStateSync)
        {
        if (tag == GraphicsDisplayDatabase.DtagMntr)
        {
                var selectedId = handle == GraphicsDisplayDatabase.DefaultModeHandle ? 0 : handle != 0 ? handle : modeId;
                if (handle == 0 && modeId == GraphicsModeIds.Invalid) return 0;
                if (!GraphicsMonitorSelectors.TryGetFamily(selectedId, out var family))
                    return GraphicsDisplayDatabase.Failure;
                var count = Math.Min(bufferSize, (uint)GraphicsMonitorInfoImage.TransferSize);
                if (buffer == 0 || count == 0 || buffer > uint.MaxValue - (count - 1)) return 0;
                var admission = GraphicsMonitorInfoReadback.Read(Memory, _graphicsLibraryBase, family, count, out var snapshot);
                if (admission == GraphicsMonitorInfoReadback.Admission.Invalid) return GraphicsDisplayDatabase.Failure;
                if (admission == GraphicsMonitorInfoReadback.Admission.Registered)
                {
                    // The family owns MNTR, so use its canonical base record rather
                    // than opening the selected node or consulting managed ownership.
                    // A registered CMDB snapshot is field-granular: the
                    // registration and position cells are only admitted when
                    // the requested transfer reaches those fields.  Keep
                    // read-only queries out of the mutation sidecar entirely;
                    // otherwise a compact query (or a later guest edit to a
                    // CMDB cell) would permanently turn subsequent pointer or
                    // position fields into stale zeroes.  The sidecar is
                    // created only by SetDisplayInfoData and remains
                    // authoritative for those explicit mutations.
                    var record = GetOrCreateMonitorDriverRecord(
                        snapshot.Family,
                        snapshot.MonitorSpec,
                        snapshot.Current,
                        snapshot.Original,
                        cache: false);
                    return GraphicsDisplayDatabase.GetDisplayInfoData(Memory, 0, buffer, bufferSize,
                        tag, snapshot.Family, _ => snapshot.MonitorSpec,
                        monitorPositionProvider: _ => snapshot.Current,
                        monitorOriginalPositionProvider: _ => snapshot.Original,
                        monitorInfoImageProvider: _ => record);
                }
            }
        var existingFamily = tag == GraphicsDisplayDatabase.DtagMntr &&
                TryResolveMonitorMutationFamily(handle, modeId, out var resolvedFamily)
                ? resolvedFamily
                : 0u;
        var existingRecord = existingFamily != 0 &&
                _monitorDriverRecords.TryGetValue(existingFamily, out var mutableRecord)
                ? mutableRecord
                : null;
        var result = GraphicsDisplayDatabase.GetDisplayInfoData(
                Memory,
                handle,
                buffer,
                bufferSize,
                tag,
                modeId,
                displayId => _monitors.FindOrCreateForDisplay(
                    Memory,
                    Allocator,
                    displayId),
                (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true,
                (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false,
                (Display as IGraphicsDisplayChipsetBackend)?.SupportsAgaDisplay == true,
                GetMonitorViewPosition,
                GetMonitorOriginalPosition,
                monitorInfoImageProvider: existingRecord is null ? null : _ => existingRecord);
        // Read-only host queries must not become mutation-sidecar state.  The
        // guest CMDB/position cells remain the authoritative source and may
        // be edited by a provider between calls; only SetDisplayInfoData is
        // allowed to seed _monitorDriverRecords.
        return result;
        }
    }

    /// <summary>Implements the private V39 monitor-driver record setter.</summary>
    internal bool TrySetDisplayInfoData(
        uint handle,
        uint buffer,
        uint bufferSize,
        uint tag,
        uint modeId,
        out uint result)
    {
        lock (_monitorStateSync)
        {
            result = 0;
            if (tag != GraphicsDisplayDatabase.DtagMntr ||
                !TryResolveMonitorMutationFamily(handle, modeId, out var family))
                return false;

        var transfer = Math.Min(bufferSize, (uint)GraphicsMonitorInfoImage.TransferSize);
        if (transfer < 20)
            return true;
        if (buffer == 0 || buffer > uint.MaxValue - (transfer - 1))
            return false;
        if (!TryReadGuestBytes(buffer, transfer, out var source))
            return false;

        if (!_monitorDriverRecords.TryGetValue(family, out var record))
        {
            if (GraphicsMonitorInfoReadback.Read(
                    Memory,
                    _graphicsLibraryBase,
                    family,
                    GraphicsMonitorInfoImage.TransferSize,
                    out var snapshot) == GraphicsMonitorInfoReadback.Admission.Registered)
            {
                record = GetOrCreateMonitorDriverRecord(
                    snapshot.Family,
                    snapshot.MonitorSpec,
                    snapshot.Current,
                    snapshot.Original);
            }
            else
            {
                var monitorSpec = _monitors.FindOpenForDisplay(family);
                var current = _monitorPositions.Get(family, false);
                var original = GetMonitorOriginalPosition(family);
                record = GetOrCreateMonitorDriverRecord(family, monitorSpec, current, original);
            }
        }

        var next = (byte[])record.Clone();
        source.AsSpan().CopyTo(next);
            _monitorDriverRecords[family] = next;
            result = transfer - 20;
            return true;
        }
    }

    private bool TryResolveMonitorMutationFamily(uint handle, uint modeId, out uint family)
    {
        var selectedId = handle == GraphicsDisplayDatabase.DefaultModeHandle
            ? 0u
            : handle != 0 ? handle : modeId;
        if (!GraphicsMonitorSelectors.TryGetFamily(selectedId, out family))
            return false;
        if (family == 0)
            family = (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true
                ? GraphicsModeIds.NtscMonitor
                : GraphicsModeIds.PalMonitor;
        return true;
    }

    private byte[] GetOrCreateMonitorDriverRecord(
        uint family,
        uint monitorSpec,
        GraphicsMonitorViewPosition current,
        GraphicsMonitorViewPosition original,
        bool cache = true)
    {
        if (_monitorDriverRecords.TryGetValue(family, out var existing))
            return existing;
        var image = GraphicsMonitorInfoImage.Create(
            family == GraphicsModeIds.PalMonitor,
            monitorSpec,
            current,
            original);
        var record = image.AsSpan(0, GraphicsMonitorInfoImage.TransferSize).ToArray();
        if (cache)
            _monitorDriverRecords[family] = record;
        return record;
    }

    private bool TryReadGuestBytes(uint address, uint length, out byte[] bytes)
    {
        bytes = new byte[(int)length];
        for (var offset = 0u; offset < length; offset++)
        {
            if (Memory.TryReadByte(address + offset, out bytes[(int)offset]))
                continue;
            bytes = Array.Empty<byte>();
            return false;
        }
        return true;
    }

    internal bool MonitorInfoNeedsHostPublication(uint handle, uint modeId, uint byteCount)
    {
        lock (_monitorStateSync)
        {
            if (byteCount <= 16) return false;
            var selectedId = handle == GraphicsDisplayDatabase.DefaultModeHandle ? 0 : handle != 0 ? handle : modeId;
            if (!GraphicsMonitorSelectors.TryGetFamily(selectedId, out var family)) return false;
            return GraphicsMonitorInfoReadback.Read(Memory, _graphicsLibraryBase, family, byteCount, out _) ==
                GraphicsMonitorInfoReadback.Admission.HostBackend;
        }
    }

    // Explicit selected-monitor boundary for the future Intuition/resident
    // mutation bridge. This is not an implementation of SetPrefs itself.
    internal bool TrySetMonitorViewPosition(uint monitorId, short? x, short? y)
    {
        lock (_monitorStateSync)
        {
            if (monitorId != 0x11000u && monitorId != 0x21000u)
                return false;
            if (_graphicsLibraryBase != 0)
            {
                if (!TryPublishNativeDisplayDatabase() ||
                    !TryGetOwnedMonitorPositionCell(monitorId, out var cell) ||
                    !Memory.TryReadLong(cell, out var previous))
                    return false;
                var next = new GraphicsMonitorViewPosition(
                    x ?? unchecked((short)(previous >> 16)),
                    y ?? unchecked((short)previous));
                if (!Memory.TryWriteLong(cell, next.WordPair))
                {
                    _ = RestoreGuestLong(cell, previous);
                    return false;
                }
                var updated = _monitorPositions.TrySet(monitorId, next.X, next.Y);
                UpdateMonitorDriverRecordCurrent(monitorId, next.WordPair);
                return updated;
            }
            var changed = _monitorPositions.TrySet(monitorId, x, y);
            if (changed)
                UpdateMonitorDriverRecordCurrent(monitorId, _monitorPositions.Get(monitorId, false).WordPair);
            return changed;
        }
    }

    private void UpdateMonitorDriverRecordCurrent(uint monitorId, uint wordPair)
    {
        if (!_monitorDriverRecords.TryGetValue(monitorId, out var record) ||
            record.Length < GraphicsMonitorInfoImage.TransferSize)
            return;
        record[0x14] = (byte)(wordPair >> 24);
        record[0x15] = (byte)(wordPair >> 16);
        record[0x16] = (byte)(wordPair >> 8);
        record[0x17] = (byte)wordPair;
    }

    private GraphicsMonitorViewPosition GetMonitorViewPosition(uint modeId)
    {
        var ntsc = (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true;
        var monitorId = modeId & 0xFFFF1000u;
        if (monitorId == 0)
            monitorId = ntsc ? 0x11000u : 0x21000u;
        if (TryGetOwnedMonitorPositionCell(monitorId, out var cell) &&
            Memory.TryReadLong(cell, out var current))
            return new(unchecked((short)(current >> 16)), unchecked((short)current));
        return _monitorPositions.Get(modeId, ntsc);
    }

    private GraphicsMonitorViewPosition GetMonitorOriginalPosition(uint modeId)
    {
        var monitorId = modeId & 0xFFFF1000u;
        if (monitorId == 0)
            monitorId = (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true ? 0x11000u : 0x21000u;
        // The ownership check proves the complete12-byte record. Original is
        // a separate guest LONG after current; preferences never overwrite it.
        if (TryGetOwnedMonitorPositionCell(monitorId, out var currentCell) &&
            Memory.TryReadLong(currentCell + 4, out var original))
            return new(unchecked((short)(original >> 16)), unchecked((short)original));
        return GraphicsMonitorViewPosition.BootDefault;
    }

    private bool TryGetNativeRuntimeDescriptor(out uint descriptor, out bool enabled)
    {
        descriptor = 0;
        enabled = false;
        if (_graphicsLibraryBase == 0 ||
            !Memory.TryReadWord(_graphicsLibraryBase + 0x12, out var positiveSize))
            return false;
        if (positiveSize < GraphicsLibraryImageLayout.NativeRuntimeImageSize)
            return true;
        if (_graphicsLibraryBase > uint.MaxValue - (GraphicsLibraryImageLayout.NativeRuntimeImageSize - 1u))
            return false;
        descriptor = _graphicsLibraryBase + GraphicsLibraryImageLayout.NativeRuntimeDescriptorTag;
        if (!Memory.TryReadLong(descriptor + 4, out var version))
            return false;
        enabled = version == GraphicsLibraryImageLayout.NativeRuntimeDescriptorCurrentVersion;
        return true;
    }

    private bool TryPublishNativeRuntimeDescriptor()
    {
        if (!TryGetNativeRuntimeDescriptor(out var descriptor, out var enabled))
            return false;
        if (!enabled)
            return true;
        var prior = new uint[5];
        for (var index = 0; index < prior.Length; index++)
            if (!Memory.TryReadLong(descriptor + (uint)index * 4, out prior[index]))
                return false;
        var valid = GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag;
        if (prior[0] != 0 && (prior[0] != valid || prior[2] != _graphicsLibraryBase ||
            prior[3] != _nativeDisplayDatabaseAddress))
            return false;
        if (Memory.TryWriteLong(descriptor, 0) &&
            Memory.TryWriteLong(descriptor + 8, _graphicsLibraryBase) &&
            Memory.TryWriteLong(descriptor + 12, _nativeDisplayDatabaseAddress) &&
            Memory.TryWriteLong(descriptor + 16, (uint)GraphicsDisplayDatabase.NativeDatabaseSize) &&
            Memory.TryWriteLong(descriptor, valid))
            return true;
        // Restore payload before restoring validity. Keep the owned table
        // allocated on failure; even failed rollback cannot create a dangling
        // descriptor pointing into a freed allocation.
        _ = RestoreGuestLong(descriptor, 0);
        for (var index = 1; index < prior.Length; index++)
            _ = RestoreGuestLong(descriptor + (uint)index * 4, prior[index]);
        _ = RestoreGuestLong(descriptor, prior[0]);
        return false;
    }

    private bool TryInvalidateNativeRuntimeDescriptor()
    {
        if (!TryGetNativeRuntimeDescriptor(out var descriptor, out var enabled))
            return false;
        if (!enabled)
            return true;
        if (!Memory.TryReadLong(descriptor, out var tag))
            return false;
        if (tag != GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag)
            return true;
        if (!Memory.TryReadLong(descriptor + 8, out var owner) ||
            !Memory.TryReadLong(descriptor + 12, out var database))
            return false;
        if (owner != _graphicsLibraryBase || database != _nativeDisplayDatabaseAddress)
            return true;
        if (Memory.TryWriteLong(descriptor, 0))
            return true;
        _ = RestoreGuestLong(descriptor, tag);
        return false;
    }

    private bool TryGetMonitorRegistrationForPublication(out NativeMonitorRegistrationState state)
    {
        if (_savedNativeMonitorRegistration is { } saved)
        {
            state = saved;
            return true;
        }
        state = default;
        if (!_monitors.TryGetResidentRegistrations(Memory, out var ntsc, out var pal)) return false;
        state = new((Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true
                ? GraphicsModeIds.NtscMonitor : GraphicsModeIds.PalMonitor,
            ntsc, pal, GraphicsMonitorViewPosition.BootDefault, GraphicsMonitorViewPosition.BootDefault);
        return true;
    }

    private bool TryCaptureOwnedMonitorState()
    {
        // Capture the complete mutable trailer before committing any host copy.
        // Mappings are raw data: do not open, validate, adopt or free their nodes.
        // An unavailable field retains the owned allocation for a later retry.
        if (!TryGetOwnedMonitorPositionCell(0x11000, out var ntscCell, allowClearedPointer: true) ||
            !TryGetOwnedMonitorPositionCell(0x21000, out var palCell, allowClearedPointer: true) ||
            !Memory.TryReadLong(ntscCell, out var ntsc) ||
            !Memory.TryReadLong(palCell, out var pal) ||
            !Memory.TryReadLong(ntscCell + 4, out var ntscOriginal) ||
            !Memory.TryReadLong(palCell + 4, out var palOriginal) ||
            !Memory.TryReadLong(_nativeDisplayDatabaseAddress + (uint)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, out var family) ||
            family is not GraphicsModeIds.NtscMonitor and not GraphicsModeIds.PalMonitor ||
            !Memory.TryReadLong(_nativeDisplayDatabaseAddress + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset, out var ntscMonitor) ||
            !Memory.TryReadLong(_nativeDisplayDatabaseAddress + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + 4, out var palMonitor))
            return false;
        _savedNativeMonitorRegistration = new(family, ntscMonitor, palMonitor,
            new(unchecked((short)(ntscOriginal >> 16)), unchecked((short)ntscOriginal)),
            new(unchecked((short)(palOriginal >> 16)), unchecked((short)palOriginal)));
        _monitorPositions.TrySet(0x11000, unchecked((short)(ntsc >> 16)), unchecked((short)ntsc));
        _monitorPositions.TrySet(0x21000, unchecked((short)(pal >> 16)), unchecked((short)pal));
        return true;
    }

    private bool TryGetOwnedMonitorPositionCell(uint monitorId, out uint cell, bool allowClearedPointer = false)
    {
        cell = 0;
        if (!_ownsNativeDisplayDatabase || _nativeDisplayDatabaseBase != _graphicsLibraryBase ||
            (monitorId != 0x11000u && monitorId != 0x21000u) ||
            !Memory.TryReadLong(_graphicsLibraryBase + (uint)GraphicsLayouts.GfxBaseDisplayInfoDataBase, out var published) ||
            (published != _nativeDisplayDatabaseAddress && !(allowClearedPointer && published == 0)))
            return false;
        var address = _nativeDisplayDatabaseAddress;
        if (
            !Memory.TryReadLong(address, out var magic) || magic != GraphicsDisplayDatabase.NativeDatabaseMagic ||
            !Memory.TryReadLong(address + 4, out var version) || version != GraphicsDisplayDatabase.NativeDatabaseVersion ||
            !Memory.TryReadLong(address + 8, out var size) || size != GraphicsDisplayDatabase.NativeDatabaseSize)
            return false;
        var record = address + (uint)(GraphicsDisplayDatabase.NativeMonitorPositionsOffset +
            (monitorId == 0x11000u ? 0 : GraphicsDisplayDatabase.NativeMonitorPositionRecordSize));
        if (!Memory.TryReadLong(record, out var storedId) || storedId != monitorId)
            return false;
        cell = record + 4;
        return true;
    }

    /// <summary>
    /// Returns the side-effect-free guest output length for a public display
    /// query.  The native-overlay adapter uses this before entering the
    /// chunk writer so a read-only provider/image buffer is declined before
    /// monitor-spec discovery or transactional writes.
    /// </summary>
    internal bool TryGetDisplayInfoDataOutputLength(
        uint handle,
        uint bufferSize,
        uint tag,
        uint modeId,
        out uint byteCount)
        => GraphicsDisplayDatabase.TryGetDisplayInfoDataOutputLength(
            handle,
            bufferSize,
            tag,
            modeId,
            (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true,
            (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false,
            out byteCount);

    internal uint BestModeIDA(uint tagList)
    {
        if (Display is IGraphicsViewModeBackend modeBackend)
        {
            return GraphicsDisplayDatabase.BestModeIDA(
                Memory,
                tagList,
                viewPort => modeBackend.TryGetModeId(viewPort, out var modeId)
                    ? modeId
                    : GraphicsModeIds.Invalid,
                defaultMonitorNtsc: (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true,
                supportsEcsDisplay: (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false,
                supportsAgaDisplay: (Display as IGraphicsDisplayChipsetBackend)?.SupportsAgaDisplay == true);
        }

        return GraphicsDisplayDatabase.BestModeIDA(
            Memory,
            tagList,
            viewPort => GraphicsColorOperations.GetNativeViewPortModeId(
                Memory,
                _colorMaps,
                viewPort,
                (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true),
            rejectInvalidViewportMode: false,
            defaultMonitorNtsc: (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true,
            supportsEcsDisplay: (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false,
            supportsAgaDisplay: (Display as IGraphicsDisplayChipsetBackend)?.SupportsAgaDisplay == true);
    }

    internal bool TryBestModeIDA(uint tagList, out uint modeId)
    {
        var defaultMonitorNtsc = (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true;
        var supportsEcsDisplay = (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false;
        var supportsAgaDisplay = (Display as IGraphicsDisplayChipsetBackend)?.SupportsAgaDisplay == true;
        var viewportModeCache = new Dictionary<uint, uint>();
        Func<uint, uint>? viewportModeProvider;
        var rejectInvalidViewportMode = true;

        if (Display is IGraphicsViewModeBackend modeBackend)
        {
            viewportModeProvider = viewPort =>
            {
                if (viewportModeCache.TryGetValue(viewPort, out var cached))
                    return cached;

                var value = modeBackend.TryGetModeId(viewPort, out var mode)
                    ? mode
                    : GraphicsModeIds.Invalid;
                viewportModeCache[viewPort] = value;
                return value;
            };
        }
        else
        {
            viewportModeProvider = viewPort =>
            {
                if (viewportModeCache.TryGetValue(viewPort, out var cached))
                    return cached;

                var value = GraphicsColorOperations.GetNativeViewPortModeId(
                    Memory,
                    _colorMaps,
                    viewPort,
                    defaultMonitorNtsc);
                viewportModeCache[viewPort] = value;
                return value;
            };
            rejectInvalidViewportMode = false;
        }

        modeId = GraphicsDisplayDatabase.BestModeIDA(
            Memory,
            tagList,
            viewportModeProvider,
            rejectInvalidViewportMode,
            defaultMonitorNtsc,
            supportsEcsDisplay,
            supportsAgaDisplay);
        if (modeId != GraphicsModeIds.Invalid)
            return true;

        return GraphicsDisplayDatabase.IsBestModeRequestOwned(
            Memory,
            tagList,
            viewportModeProvider,
            rejectInvalidViewportMode,
            defaultMonitorNtsc,
            supportsEcsDisplay,
            supportsAgaDisplay);
    }

    internal uint CoerceMode(uint viewPort, uint monitorId, uint flags)
        => GraphicsDisplayDatabase.CoerceMode(
            Memory,
            viewPort,
            monitorId,
            flags,
            (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true,
            (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false,
            (Display as IGraphicsDisplayChipsetBackend)?.SupportsAgaDisplay == true,
            GraphicsColorOperations.GetNativeViewPortModeId(Memory, _colorMaps, viewPort,
                (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true, requireAssociation: true));

    internal bool TryCoerceMode(
        uint viewPort,
        uint monitorId,
        uint flags,
        out uint modeId)
    {
        var defaultMonitorNtsc = (Display as IGraphicsDisplayProfileBackend)?.IsNtsc == true;
        var supportsEcsDisplay = (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false;
        var supportsAgaDisplay = (Display as IGraphicsDisplayChipsetBackend)?.SupportsAgaDisplay == true;
        modeId = GraphicsDisplayDatabase.CoerceMode(
            Memory,
            viewPort,
            monitorId,
            flags,
            defaultMonitorNtsc,
            supportsEcsDisplay,
            supportsAgaDisplay,
            GraphicsColorOperations.GetNativeViewPortModeId(Memory, _colorMaps, viewPort,
                defaultMonitorNtsc, requireAssociation: true));
        if (modeId != GraphicsModeIds.Invalid)
            return true;

        return GraphicsDisplayDatabase.IsCoerceRequestOwned(
            Memory,
            viewPort,
            monitorId,
            flags,
            defaultMonitorNtsc);
    }

    internal uint OpenMonitor(uint monitorName, uint displayId)
    {
        lock (_monitorStateSync)
            return _monitors.Open(Memory, Allocator, monitorName, displayId);
    }

    internal bool TryOpenMonitor(uint monitorName, uint displayId, out uint monitor)
    {
        lock (_monitorStateSync)
            return _monitors.TryOpen(Memory, Allocator, monitorName, displayId, out monitor);
    }

    internal int CloseMonitor(uint monitorSpec)
    {
        lock (_monitorStateSync)
            return _monitors.Close(Memory, Allocator, monitorSpec);
    }

    internal bool IsOwnedMonitorSpec(uint monitorSpec)
    {
        lock (_monitorStateSync)
            return _monitors.IsOwned(monitorSpec);
    }

    internal uint GfxNew(uint nodeType)
    {
        lock (_monitorStateSync)
            return _extendedNodes.New(Memory, Allocator, nodeType);
    }

    internal bool GfxFree(uint node)
    {
        lock (_monitorStateSync)
        {
            var result = _extendedNodes.Free(Memory, Allocator, node);
            if (result)
                _temporaryViewPortExtras.Remove(node);

            return result;
        }
    }

    internal bool GfxAssociate(uint pointer, uint node)
    {
        lock (_monitorStateSync)
        {
            var previousNode = _extendedNodes.LookUp(pointer);
            var transfersTemporaryNode = _temporaryViewPortExtras.ContainsKey(node);
            var temporaryFlagsAddress = node + (uint)GraphicsLayouts.ViewPortExtraFlags;
            ushort originalTemporaryFlags = 0;
            if (transfersTemporaryNode &&
                !Memory.TryReadWord(temporaryFlagsAddress, out originalTemporaryFlags))
            {
                // A failed read is not permission to publish a synthetic zero.
                // Keep the temporary marker untouched so a native/provider or a
                // recovered guest bridge can retry the ownership transfer.
                return false;
            }

            if (transfersTemporaryNode &&
                !Memory.TryWriteWord(temporaryFlagsAddress, 0))
            {
                RestoreGuestWord(temporaryFlagsAddress, originalTemporaryFlags);
                return false;
            }

            var result = _extendedNodes.Associate(Memory, pointer, node);
            if (!result)
            {
                if (transfersTemporaryNode)
                    RestoreGuestWord(temporaryFlagsAddress, originalTemporaryFlags);

                return false;
            }

            if (transfersTemporaryNode)
            {
                // An explicit GfxAssociate transfers a temporary MakeVPort node
                // to caller ownership, including an idempotent re-associate with
                // the same viewport.  Clear the private free-me marker as part of
                // that transfer so a later FreeVPortCopLists cannot reclaim it.
                _temporaryViewPortExtras.Remove(node);
            }

            if (previousNode != 0 &&
                previousNode != node &&
                _temporaryViewPortExtras.ContainsKey(previousNode))
            {
                // Replacing a temporary association with a caller-owned node
                // transfers the viewport's ownership without leaking the old
                // MakeVPort allocation.  Keep the pending marker when an
                // immediate guest-memory cleanup is declined; FreeVPortCopLists
                // can retry the displaced node after the bridge recovers.
                if (_extendedNodes.Free(Memory, Allocator, previousNode))
                    _temporaryViewPortExtras.Remove(previousNode);
            }

            return true;
        }
    }

    internal uint GfxLookUp(uint pointer)
    {
        lock (_monitorStateSync)
            return _extendedNodes.LookUp(pointer);
    }

    internal int GetSprite(uint sprite, short requested)
        => GraphicsSpriteOperations.GetSprite(Memory, _sprites, sprite, requested);

    internal bool IsValidSimpleSprite(uint sprite)
        => GraphicsSpriteOperations.IsValidSimpleSprite(Memory, sprite);

    internal bool FreeSprite(short sprite)
        => GraphicsSpriteOperations.FreeSprite(_sprites, sprite);

    /// <summary>
    /// Native-overlay FreeSprite may release only a hardware slot acquired
    /// through this graphics instance. An untracked slot can belong to the
    /// resident library or a display provider, so the mapped vector must
    /// remain available to that owner instead of claiming an idempotent
    /// host-side success.
    /// </summary>
    internal bool IsOwnedSprite(short sprite)
        => GraphicsSpriteOperations.IsAllocated(_sprites, sprite);

    internal bool MoveSprite(uint viewPort, uint sprite, short x, short y)
        => GraphicsSpriteOperations.MoveSprite(
            Memory,
            viewPort,
            sprite,
            x,
            y,
            _spriteBackend);

    internal bool ChangeSprite(uint viewPort, uint sprite, uint newData)
        => GraphicsSpriteOperations.ChangeSprite(
            Memory,
            viewPort,
            sprite,
            newData,
            _spriteBackend);

    internal uint AllocSpriteDataA(uint bitMap, uint tags)
        => GraphicsSpriteOperations.AllocSpriteDataA(
            Memory,
            Allocator,
            _sprites,
            bitMap,
            tags);

    internal int GetExtSpriteA(uint extSprite, uint tags)
        => GraphicsSpriteOperations.TryGetExtSpriteA(
                Memory,
                _sprites,
                extSprite,
                tags,
                _spriteBackend,
                out var result)
            ? result
            : GraphicsRasterOperations.Failure;

    /// <summary>
    /// Runs GetExtSpriteA through the optional display/provider boundary when
    /// the request asks for a software or scan-doubled form.  Ordinary OCS/
    /// ECS requests always remain owned by the portable implementation.
    /// </summary>
    internal bool TryGetExtSpriteA(uint extSprite, uint tags, out int result)
        => GraphicsSpriteOperations.TryGetExtSpriteA(
            Memory,
            _sprites,
            extSprite,
            tags,
            _spriteBackend,
            out result);

    internal bool IsValidGetExtSpriteRequest(uint extSprite, uint tags)
        => GraphicsSpriteOperations.IsValidGetExtSpriteRequest(Memory, extSprite, tags);

    internal bool IsOwnedSpriteData(uint extSprite)
        => _sprites.OwnsExtended(extSprite);

    internal bool ChangeExtSpriteA(
        uint viewPort,
        uint oldSprite,
        uint newSprite,
        uint tags)
        => GraphicsSpriteOperations.TryChangeExtSpriteA(
                Memory,
                viewPort,
                oldSprite,
                newSprite,
                tags,
                _spriteBackend,
                out var success) &&
            success;

    /// <summary>
    /// Runs ChangeExtSpriteA through the optional display/provider boundary
    /// for soft/scan-doubled forms.  The returned status distinguishes a
    /// provider that declined ownership from a provider that claimed and
    /// failed the operation.
    /// </summary>
    internal bool TryChangeExtSpriteA(
        uint viewPort,
        uint oldSprite,
        uint newSprite,
        uint tags,
        out bool success)
        => GraphicsSpriteOperations.TryChangeExtSpriteA(
            Memory,
            viewPort,
            oldSprite,
            newSprite,
            tags,
            _spriteBackend,
            out success);

    internal void FreeSpriteData(uint extSprite)
        => GraphicsSpriteOperations.FreeSpriteData(Allocator, _sprites, extSprite);

    internal bool InitGels(uint head, uint tail, uint gelsInfo)
        => GraphicsGelsOperations.InitGels(Memory, head, tail, gelsInfo);

    internal bool TryGetInitGelsPublicationSpans(
        uint head,
        uint tail,
        uint gelsInfo,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsGelsOperations.TryGetInitGelsPublicationSpans(
            Memory,
            head,
            tail,
            gelsInfo,
            out spans);

    internal bool SetCollision(uint number, uint routine, uint gelsInfo)
        => GraphicsGelsOperations.SetCollision(Memory, number, routine, gelsInfo);

    internal bool TryGetSetCollisionPublicationSpans(
        uint number,
        uint gelsInfo,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsGelsOperations.TryGetSetCollisionPublicationSpans(
            Memory,
            number,
            gelsInfo,
            out spans);

    internal bool InitMasks(uint sprite)
        => GraphicsGelsOperations.InitMasks(Memory, sprite);

    internal bool TryGetInitMasksPublicationSpans(
        uint sprite,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsGelsOperations.TryGetInitMasksPublicationSpans(
            Memory,
            sprite,
            out spans);

    internal bool TryGetAddVSpritePublicationSpans(
        uint sprite,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsGelsOperations.TryGetAddVSpritePublicationSpans(
            Memory,
            sprite,
            rastPort,
            out spans);

    internal bool TryGetRemVSpritePublicationSpans(
        uint sprite,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsGelsOperations.TryGetRemVSpritePublicationSpans(
            Memory,
            sprite,
            out spans);

    internal bool TryGetSortGListPublicationSpans(
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsGelsOperations.TryGetSortGListPublicationSpans(
            Memory,
            rastPort,
            out spans);

    internal bool AddVSprite(uint sprite, uint rastPort)
        => GraphicsGelsOperations.AddVSprite(Memory, sprite, rastPort);

    internal bool RemVSprite(uint sprite)
        => GraphicsGelsOperations.RemVSprite(Memory, sprite);

    internal bool SortGList(uint rastPort)
        => GraphicsGelsOperations.SortGList(Memory, rastPort);

    internal bool AddBob(uint bob, uint rastPort)
        => GraphicsAnimationOperations.AddBob(Memory, bob, rastPort);

    internal bool TryGetAddBobPublicationSpans(
        uint bob,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsAnimationOperations.TryGetAddBobPublicationSpans(
            Memory,
            bob,
            rastPort,
            out spans);

    internal bool TryGetRemIBobPublicationSpans(
        uint bob,
        uint rastPort,
        uint viewPort,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsAnimationOperations.TryGetRemIBobPublicationSpans(
            Memory,
            bob,
            rastPort,
            viewPort,
            out spans);

    internal bool DoCollision(uint rastPort)
        => GraphicsAnimationOperations.DoCollision(Memory, rastPort, _gels);

    internal bool DrawGList(uint rastPort, uint viewPort)
        => GraphicsAnimationOperations.DrawGList(Memory, rastPort, viewPort, _gels);

    internal bool TryGetDrawGListPublicationSpans(
        uint rastPort,
        uint viewPort,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsAnimationOperations.TryGetDrawGListPublicationSpans(
            Memory,
            rastPort,
            viewPort,
            out spans);

    internal bool RemIBob(uint bob, uint rastPort, uint viewPort)
        => GraphicsAnimationOperations.RemIBob(Memory, bob, rastPort, viewPort, _gels);

    internal bool AddAnimOb(uint animOb, uint animKeyAddress, uint rastPort)
        => GraphicsAnimationOperations.AddAnimOb(Memory, animOb, animKeyAddress, rastPort);

    internal bool TryGetAddAnimObPublicationSpans(
        uint animOb,
        uint animKeyAddress,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsAnimationOperations.TryGetAddAnimObPublicationSpans(
            Memory,
            animOb,
            animKeyAddress,
            rastPort,
            out spans);

    internal bool Animate(uint animKeyAddress, uint rastPort)
        => GraphicsAnimationOperations.Animate(Memory, animKeyAddress, rastPort);

    internal bool TryGetAnimatePublicationSpans(
        uint animKeyAddress,
        uint rastPort,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsAnimationOperations.TryGetAnimatePublicationSpans(
            Memory,
            animKeyAddress,
            rastPort,
            out spans);

    internal bool GetGBuffers(uint animOb, uint rastPort, uint db)
        => GraphicsAnimationOperations.GetGBuffers(
            Memory,
            Allocator,
            _animationBuffers,
            animOb,
            rastPort,
            db);

    internal bool TryGetGBuffersPublicationSpans(
        uint animOb,
        uint rastPort,
        out List<uint> addresses)
        => GraphicsAnimationOperations.TryGetGBuffersPublicationSpans(
            Memory,
            _animationBuffers,
            animOb,
            rastPort,
            out addresses);

    internal bool TryGetInitGMasksPublicationSpans(
        uint animOb,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsAnimationOperations.TryGetInitGMasksPublicationSpans(
            Memory,
            animOb,
            out spans);

    internal bool InitGMasks(uint animOb)
        => GraphicsAnimationOperations.InitGMasks(Memory, animOb);

    internal bool FreeGBuffers(uint animOb, uint rastPort, uint db)
        => GraphicsAnimationOperations.FreeGBuffers(
            Memory,
            Allocator,
            _animationBuffers,
            animOb,
            rastPort,
            db);

    internal bool TryGetFreeGBuffersPublicationSpans(
        uint animOb,
        uint rastPort,
        uint db,
        out List<(uint Address, ulong Bytes)> spans)
        => GraphicsAnimationOperations.TryGetFreeGBuffersPublicationSpans(
            Memory,
            _animationBuffers,
            animOb,
            rastPort,
            db,
            out spans);

    internal ushort ScalerDiv(ushort factor, ushort numerator, ushort denominator)
        => GraphicsBitmapUtilityOperations.ScalerDiv(factor, numerator, denominator);

    // RASSIZE is the classic graphics/gfx.h raster-size macro. Keep its
    // guest ULONG arithmetic available beside the raster allocator while
    // leaving allocation ownership to AllocRaster.
    internal uint RasSize(uint width, uint height)
        => GraphicsRasterOperations.RasSize(width, height);

    internal int BitMapScale(uint bitScaleArgs)
        => GraphicsBitmapUtilityOperations.BitMapScale(Memory, bitScaleArgs);

    internal uint AllocRaster(uint width, uint height)
    {
        var raster = GraphicsRasterOperations.AllocRaster(Allocator, width, height);
        if (raster != 0)
            _rasters[raster] = (width, height);

        return raster;
    }

    internal int FreeRaster(uint address, uint width, uint height)
    {
        // Keep the public ownership lookup aligned with the native
        // word-addressed PLANEPTR contract.  This early guard prevents an odd
        // caller pointer from being interpreted as a portable allocator
        // request before the registry or chip-memory backend is touched.
        if ((address & 1u) != 0)
            return GraphicsRasterOperations.Failure;

        if (!_rasters.TryGetValue(address, out var dimensions) ||
            dimensions.Width != width ||
            dimensions.Height != height)
        {
            return GraphicsRasterOperations.Failure;
        }

        var result = GraphicsRasterOperations.FreeRaster(
            Allocator,
            address,
            width,
            height);
        if (result == GraphicsRasterOperations.Success)
            _rasters.Remove(address);

        return result;
    }

    /// <summary>
    /// Reports whether the raster address and dimensions were allocated by
    /// this graphics instance. Native-overlay FreeRaster must not release a
    /// foreign chip-memory range merely because its caller supplied a valid
    /// RASSIZE pair.
    /// </summary>
    internal bool IsOwnedRaster(uint address, uint width, uint height)
        => _rasters.TryGetValue(address, out var dimensions) &&
            dimensions.Width == width &&
            dimensions.Height == height;

    internal uint GetBitMapAttr(uint bitMap, uint attribute)
        => GraphicsRasterOperations.GetBitMapAttr(Memory, bitMap, attribute);

    internal bool TryGetBitMapAttr(uint bitMap, uint attribute, out uint value)
        => GraphicsRasterOperations.TryGetBitMapAttr(Memory, bitMap, attribute, out value);

    /// <summary>
    /// Reports whether a BitMap envelope was allocated by this graphics
    /// instance.  Native-overlay FreeBitMap must never reinterpret a
    /// foreign/native or RTG surface as portable planar storage.
    /// </summary>
    internal bool IsOwnedBitMap(uint bitMap)
        => _bitMaps.Contains(bitMap);

    internal uint AllocBitMap(
        uint width,
        uint height,
        uint depth,
        uint flags,
        uint friendBitMap)
    {
        var bitMap = GraphicsRasterOperations.AllocBitMap(
            Memory,
            Allocator,
            width,
            height,
            depth,
            flags,
            friendBitMap,
            supportsInterleaved: (Display as IGraphicsDisplayChipsetBackend)?.SupportsEcsDisplay != false);
        if (bitMap != 0 && !_bitMaps.TryRegister(Memory, bitMap))
        {
            // AllocBitMap only reports a pointer after it has published a
            // complete standard-planar envelope. If that envelope can no
            // longer be captured for this instance's ownership registry,
            // roll it back rather than expose a bitmap that FreeBitMap could
            // never safely reclaim.
            _ = GraphicsRasterOperations.FreeBitMap(Memory, Allocator, bitMap);
            return 0;
        }

        return bitMap;
    }

    internal int FreeBitMap(uint bitMap)
    {
        // FreeBitMap(NULL) is the native empty teardown form.  Claim it
        // before consulting the instance registry or probing a guest header;
        // non-null foreign, stale, and malformed bitmaps remain strict.
        if (bitMap == 0)
            return GraphicsRasterOperations.Success;

        if (!_bitMaps.TryGet(bitMap, out var allocation))
            return GraphicsRasterOperations.Failure;

        var result = GraphicsRasterOperations.FreeBitMap(
            Memory,
            Allocator,
            bitMap,
            expectedAllocation: allocation);
        if (result == GraphicsRasterOperations.Success)
            _bitMaps.Remove(bitMap);

        return result;
    }

    internal bool OwnBlitter()
    {
        if (Blitter is IGraphicsBlitterOwnershipStatusBackend status)
            return status.TryOwn();

        Blitter.Own();
        return true;
    }

    internal bool OwnBlitter(long cycle)
    {
        if (Blitter is IGraphicsTimedBlitterBackend timed)
            return timed.TryOwn(cycle);

        return OwnBlitter();
    }

    internal bool DisownBlitter()
    {
        if (Blitter is IGraphicsBlitterOwnershipStatusBackend status)
            return status.TryDisown();

        Blitter.Disown();
        return true;
    }

    internal bool DisownBlitter(long cycle)
    {
        if (Blitter is IGraphicsTimedBlitterBackend timed)
            return timed.TryDisown(cycle);

        return DisownBlitter();
    }

    internal bool WaitBlit()
    {
        if (Blitter is IGraphicsBlitterOwnershipStatusBackend status)
            return status.TryWait();

        Blitter.Wait();
        return true;
    }

    internal bool WaitBlit(long cycle, out long completionCycle)
    {
        if (Blitter is IGraphicsTimedBlitterBackend timed)
            return timed.TryWait(cycle, out completionCycle);

        completionCycle = cycle;
        return WaitBlit();
    }

    internal bool QBlit(uint nodeAddress)
        => GraphicsBlitterQueueOperations.Queue(
            Memory,
            Blitter,
            nodeAddress,
            beamSynchronized: false);

    internal bool QBlit(uint nodeAddress, long cycle)
        => GraphicsBlitterQueueOperations.Queue(
            Memory,
            Blitter,
            nodeAddress,
            beamSynchronized: false,
            cycle);

    internal bool QBSBlit(uint nodeAddress)
        => GraphicsBlitterQueueOperations.Queue(
            Memory,
            Blitter,
            nodeAddress,
            beamSynchronized: true);

    internal bool QBSBlit(uint nodeAddress, long cycle)
        => GraphicsBlitterQueueOperations.Queue(
            Memory,
            Blitter,
            nodeAddress,
            beamSynchronized: true,
            cycle);

    internal uint NewRegion()
    {
        var region = GraphicsRegionOperations.NewRegion(Memory, Allocator);
        if (region != 0)
            _regions.Register(region);

        return region;
    }

    internal int DisposeRegion(uint region)
    {
        if (!_regions.Contains(region))
            return GraphicsRegionOperations.Failure;

        var result = GraphicsRegionOperations.DisposeRegion(Memory, Allocator, region);
        if (result == GraphicsRegionOperations.Success)
            _regions.Remove(region);

        return result;
    }

    /// <summary>
    /// Reports whether this graphics instance allocated the guest Region
    /// envelope.  Native-overlay region operations may mutate and reclaim
    /// rectangle nodes only for regions owned by the same allocator; a
    /// foreign/native Region must remain available to its original owner.
    /// </summary>
    internal bool IsOwnedRegion(uint region)
        => _regions.Contains(region);

    internal bool AndRectRegion(uint region, uint rectangle)
        => GraphicsRegionOperations.AndRectRegion(Memory, Allocator, region, rectangle);

    internal bool OrRectRegion(uint region, uint rectangle)
        => GraphicsRegionOperations.OrRectRegion(Memory, Allocator, region, rectangle);

    internal bool XorRectRegion(uint region, uint rectangle)
        => GraphicsRegionOperations.XorRectRegion(Memory, Allocator, region, rectangle);

    internal bool ClearRectRegion(uint region, uint rectangle)
        => GraphicsRegionOperations.ClearRectRegion(Memory, Allocator, region, rectangle);

    internal bool ClearRegion(uint region)
        => GraphicsRegionOperations.ClearRegion(Memory, Allocator, region);

    internal bool OrRegionRegion(uint sourceRegion, uint destinationRegion)
        => GraphicsRegionOperations.OrRegionRegion(
            Memory,
            Allocator,
            sourceRegion,
            destinationRegion);

    internal bool XorRegionRegion(uint sourceRegion, uint destinationRegion)
        => GraphicsRegionOperations.XorRegionRegion(
            Memory,
            Allocator,
            sourceRegion,
            destinationRegion);

    internal bool AndRegionRegion(uint sourceRegion, uint destinationRegion)
        => GraphicsRegionOperations.AndRegionRegion(
            Memory,
            Allocator,
            sourceRegion,
            destinationRegion);

    private bool ValidateNativeLoadViewOwnership(uint view)
    {
        if (view == 0 || Display is IGraphicsViewPublicationStatusBackend)
            return true;

        return GraphicsColorOperations.ValidateNativeViewColorMaps(
            Memory,
            _colorMaps,
            view);
    }

    private bool ValidateNativeLoadViewPublicationSeparation(uint view)
    {
        if (view == 0 ||
            !TryAddress(
                view,
                0,
                GraphicsLayouts.ViewSize,
                out var viewAddress))
        {
            return view == 0;
        }

        // LoadView commits ActiView and optional monitor/top-line sidecars
        // after the public View graph has been validated.  Keep that native
        // publication envelope disjoint from the caller's View so a crafted
        // alias cannot turn the sidecar commit into an overwrite of the
        // structure being loaded.
        if (!TryGetNativeLoadViewPublicationSpan(
                _graphicsLibraryBase,
                out var publicationAddress,
                out var publicationBytes))
        {
            return false;
        }

        if (publicationAddress == 0 || publicationBytes == 0)
            return true;

        var viewEnd = (ulong)viewAddress + (uint)GraphicsLayouts.ViewSize;
        var publicationEnd =
            (ulong)publicationAddress + publicationBytes;
        return (ulong)viewAddress >= publicationEnd ||
               (ulong)publicationAddress >= viewEnd;
    }

    internal int LoadView(uint view)
    {
        lock (_monitorStateSync)
        {
        if (view != 0 &&
            (!GraphicsRasterOperations.ValidateViewForLoad(Memory, view) ||
             !ValidateNativeLoadViewOwnership(view) ||
             !ValidateNativeLoadViewPublicationSeparation(view)))
            return GraphicsRasterOperations.Failure;

        if (!TryGetViewSignature(view, out var viewSignature))
            return GraphicsRasterOperations.Failure;

        if (_hasPublishedView &&
            view == _activeViewAddress &&
            (Display is not IGraphicsViewPublicationStatusBackend status ||
             status.CanPublishView) &&
            viewSignature == _activeViewSignature)
        {
            // The display stream is already current, but the optional full
            // guest GfxBase envelope may have been edited by a native caller
            // between requests.  Repair only that sidecar; keep the native
            // idempotent LoadView rule of avoiding a second display handoff.
            if (!TryPrepareNativeActiveView(
                    out var idempotentNativeActiveView,
                    out var idempotentOriginalNativeActiveView) ||
                !TryPrepareNativeViewSidecarsForLoad(
                    view,
                    out var idempotentNativeCurrentMonitor,
                    out var idempotentOriginalNativeCurrentMonitor,
                    out var idempotentRequestedCurrentMonitor,
                    out var idempotentNativeTopLine,
                    out var idempotentOriginalNativeTopLine,
                    out var idempotentRequestedTopLine) ||
                !CommitNativeViewState(
                    idempotentNativeActiveView,
                    view,
                    idempotentOriginalNativeActiveView,
                    idempotentNativeCurrentMonitor,
                    idempotentRequestedCurrentMonitor,
                    idempotentOriginalNativeCurrentMonitor,
                    idempotentNativeTopLine,
                    idempotentRequestedTopLine,
                    idempotentOriginalNativeTopLine))
            {
                return GraphicsRasterOperations.Failure;
            }

            RecordNativeViewSidecarsAfterCommit(
                view,
                idempotentNativeCurrentMonitor,
                idempotentRequestedCurrentMonitor,
                idempotentNativeTopLine,
                idempotentRequestedTopLine);

            return GraphicsRasterOperations.Success;
        }

        if (!TryPrepareNativeActiveView(
                out var nativeActiveView,
                out var originalNativeActiveView) ||
            !TryPrepareNativeViewSidecarsForLoad(
                view,
                out var nativeCurrentMonitor,
                out var originalNativeCurrentMonitor,
                out var requestedCurrentMonitor,
                out var nativeTopLine,
                out var originalTopLine,
                out var requestedTopLine))
            return GraphicsRasterOperations.Failure;

        if (Display is IGraphicsViewPublicationStatusBackend statusBackend)
        {
            // The guest GfxBase sidecar is the authoritative ownership
            // publication.  Publish it before the scheduler/display handoff
            // so a native callback observing the request sees the same
            // ActiView that the queued hardware stream will activate.
            if (!CommitNativeViewState(
                    nativeActiveView,
                    view,
                    originalNativeActiveView,
                    nativeCurrentMonitor,
                    requestedCurrentMonitor,
                    originalNativeCurrentMonitor,
                    nativeTopLine,
                    requestedTopLine,
                    originalTopLine))
                return GraphicsRasterOperations.Failure;

            if (!statusBackend.TryPublishView(view, 0))
            {
                // A status-aware owner can decline after the sidecar has
                // been made visible.  Restore the previous guest pointer so
                // native/provider fallback sees one coherent publication.
                if (!RestoreNativeViewState(
                        nativeActiveView,
                        originalNativeActiveView,
                        nativeCurrentMonitor,
                        originalNativeCurrentMonitor,
                        nativeTopLine,
                        originalTopLine))
                {
                    return GraphicsRasterOperations.Failure;
                }

                return GraphicsRasterOperations.Failure;
            }

            _activeViewAddress = view;
            _ = TryGetViewSignature(view, out _activeViewSignature);
            _hasPublishedView = true;
            RecordNativeViewSidecarsAfterCommit(
                view,
                nativeCurrentMonitor,
                requestedCurrentMonitor,
                nativeTopLine,
                requestedTopLine);
            return GraphicsRasterOperations.Success;
        }

        if (!CommitNativeViewState(
                nativeActiveView,
                view,
            originalNativeActiveView,
            nativeCurrentMonitor,
            requestedCurrentMonitor,
            originalNativeCurrentMonitor,
            nativeTopLine,
            requestedTopLine,
            originalTopLine))
            return GraphicsRasterOperations.Failure;

        Display.PublishView(view);
        _activeViewAddress = view;
        _ = TryGetViewSignature(view, out _activeViewSignature);
        _hasPublishedView = true;
        RecordNativeViewSidecarsAfterCommit(
            view,
            nativeCurrentMonitor,
            requestedCurrentMonitor,
            nativeTopLine,
            requestedTopLine);
        return GraphicsRasterOperations.Success;
        }
    }

    internal int LoadView(uint view, long cycle)
    {
        lock (_monitorStateSync)
        {
        if (view != 0 &&
            (!GraphicsRasterOperations.ValidateViewForLoad(Memory, view) ||
             !ValidateNativeLoadViewOwnership(view) ||
             !ValidateNativeLoadViewPublicationSeparation(view)))
            return GraphicsRasterOperations.Failure;

        if (!TryGetViewSignature(view, out var viewSignature))
            return GraphicsRasterOperations.Failure;

        if (_hasPublishedView &&
            view == _activeViewAddress &&
            (Display is not IGraphicsViewPublicationStatusBackend status ||
             status.CanPublishView) &&
            viewSignature == _activeViewSignature)
        {
            // Timed calls share the same idempotent display rule.  Keep the
            // guest active-view pointer authoritative without enqueueing a
            // duplicate timed publication.
            if (!TryPrepareNativeActiveView(
                    out var idempotentNativeActiveView,
                    out var idempotentOriginalNativeActiveView) ||
                !TryPrepareNativeViewSidecarsForLoad(
                    view,
                    out var idempotentNativeCurrentMonitor,
                    out var idempotentOriginalNativeCurrentMonitor,
                    out var idempotentRequestedCurrentMonitor,
                    out var idempotentNativeTopLine,
                    out var idempotentOriginalTopLine,
                    out var idempotentRequestedTopLine) ||
                !CommitNativeViewState(
                    idempotentNativeActiveView,
                    view,
                    idempotentOriginalNativeActiveView,
                    idempotentNativeCurrentMonitor,
                    idempotentRequestedCurrentMonitor,
                    idempotentOriginalNativeCurrentMonitor,
                    idempotentNativeTopLine,
                    idempotentRequestedTopLine,
                    idempotentOriginalTopLine))
            {
                return GraphicsRasterOperations.Failure;
            }

            RecordNativeViewSidecarsAfterCommit(
                view,
                idempotentNativeCurrentMonitor,
                idempotentRequestedCurrentMonitor,
                idempotentNativeTopLine,
                idempotentRequestedTopLine);

            return GraphicsRasterOperations.Success;
        }

        if (!TryPrepareNativeActiveView(
                out var nativeActiveView,
                out var originalNativeActiveView) ||
            !TryPrepareNativeViewSidecarsForLoad(
                view,
                out var nativeCurrentMonitor,
                out var originalNativeCurrentMonitor,
                out var requestedCurrentMonitor,
                out var nativeTopLine,
                out var originalTopLine,
                out var requestedTopLine))
            return GraphicsRasterOperations.Failure;

        if (Display is IGraphicsViewPublicationStatusBackend statusBackend)
        {
            // Keep timed and untimed LoadView on the same guest publication
            // boundary: the active View pointer becomes visible before the
            // frame-scheduled display owner is invoked.
            if (!CommitNativeViewState(
                    nativeActiveView,
                    view,
                    originalNativeActiveView,
                    nativeCurrentMonitor,
                    requestedCurrentMonitor,
                    originalNativeCurrentMonitor,
                    nativeTopLine,
                    requestedTopLine,
                    originalTopLine))
                return GraphicsRasterOperations.Failure;

            if (!statusBackend.TryPublishView(view, cycle))
            {
                if (!RestoreNativeViewState(
                        nativeActiveView,
                        originalNativeActiveView,
                        nativeCurrentMonitor,
                        originalNativeCurrentMonitor,
                        nativeTopLine,
                        originalTopLine))
                {
                    return GraphicsRasterOperations.Failure;
                }

                return GraphicsRasterOperations.Failure;
            }

            _activeViewAddress = view;
            _ = TryGetViewSignature(view, out _activeViewSignature);
            _hasPublishedView = true;
            RecordNativeViewSidecarsAfterCommit(
                view,
                nativeCurrentMonitor,
                requestedCurrentMonitor,
                nativeTopLine,
                requestedTopLine);
            return GraphicsRasterOperations.Success;
        }

        if (!CommitNativeViewState(
                nativeActiveView,
                view,
            originalNativeActiveView,
            nativeCurrentMonitor,
            requestedCurrentMonitor,
            originalNativeCurrentMonitor,
            nativeTopLine,
            requestedTopLine,
            originalTopLine))
            return GraphicsRasterOperations.Failure;

        if (Display is IGraphicsTimedDisplayBackend timedDisplay)
            timedDisplay.PublishView(view, cycle);
        else
            Display.PublishView(view);

        _activeViewAddress = view;
        _ = TryGetViewSignature(view, out _activeViewSignature);
        _hasPublishedView = true;
        RecordNativeViewSidecarsAfterCommit(
            view,
            nativeCurrentMonitor,
            requestedCurrentMonitor,
            nativeTopLine,
            requestedTopLine);
        return GraphicsRasterOperations.Success;
        }
    }

    /// <summary>
    /// Refreshes the optional native ViewExtra sidecars after an Intuition
    /// rethink has rebuilt the active copper stream outside the graphics
    /// register gateway.  The host RethinkDisplay path already owns the
    /// active-view publication; this bounded helper keeps CurrentMonitor and
    /// TopLine on the same guest-layout/ownership boundary without invoking
    /// the display backend again.
    /// </summary>
    internal bool TryRefreshNativeViewSidecars(uint view)
    {
        lock (_monitorStateSync)
        {
        // RethinkDisplay reaches this seam after its own projection work, but
        // keep the helper fail-closed when a native/provider caller supplies a
        // malformed or odd View directly.  Sidecar refresh must never clear a
        // value owned by the previously committed View merely because the
        // shifted bytes of an invalid pointer happen to look like a plain View.
        if (view != 0 && !GraphicsRasterOperations.ValidateView(Memory, view))
        {
            return false;
        }

        if (!TryPrepareNativeViewSidecarsForLoad(
                view,
                out var nativeCurrentMonitor,
                out var originalCurrentMonitor,
                out var requestedCurrentMonitor,
                out var nativeTopLine,
                out var originalTopLine,
                out var requestedTopLine))
        {
            return false;
        }

        var committed = CommitNativeViewState(
            0,
            view,
            0,
            nativeCurrentMonitor,
            requestedCurrentMonitor,
            originalCurrentMonitor,
            nativeTopLine,
            requestedTopLine,
            originalTopLine);
        if (committed)
        {
            RecordNativeViewSidecarsAfterCommit(
                view,
                nativeCurrentMonitor,
                requestedCurrentMonitor,
                nativeTopLine,
                requestedTopLine);
        }

        return committed;
        }
    }

    /// <summary>
    /// Reports the optional native GfxBase envelope that LoadView may update
    /// (ActiView plus the monitor/top-line tail).  A compact compatibility
    /// image has no validated native envelope and therefore has no guest span
    /// to preflight; a full resident/provider envelope is returned as one
    /// bounded publication range so the mapped native gateway can leave it to
    /// its owner when the range is read-only.
    /// </summary>
    internal bool TryGetNativeLoadViewPublicationSpan(
        out uint address,
        out ulong bytes)
    {
        lock (_monitorStateSync)
        {
            return TryGetNativeLoadViewPublicationSpan(
                _graphicsLibraryBase,
                out address,
                out bytes);
        }
    }

    internal bool TryGetNativeLoadViewPublicationSpan(
        uint graphicsBase,
        out uint address,
        out ulong bytes)
    {
        address = 0;
        bytes = 0;
        if (graphicsBase == 0)
            return true;

        if ((graphicsBase & 1u) != 0 ||
            graphicsBase > uint.MaxValue -
                (uint)(GraphicsLayouts.GfxBaseNativeSize - 1))
        {
            // Odd or wrapping bases are address-error envelopes, not compact
            // images. Keep them available to the resident/provider vector.
            return false;
        }

        if (!HasNativeGfxBaseEnvelope(graphicsBase))
        {
            // A compact image has no mapped native MonitorList suffix. A
            // fully mapped but malformed list is provider-owned and must not
            // be claimed as a portable publication target.
            return !HasMappedNativeGfxBaseMonitorList(graphicsBase);
        }

        address = graphicsBase;
        bytes = (uint)GraphicsLayouts.GfxBaseNativeSize;
        return true;
    }

    /// <summary>
    /// Clears the optional native ViewExtra sidecars without invoking the
    /// display backend.  AmigaBoot's scheduler owns its LoadView handoff, so
    /// that host path uses this seam to keep a queued blank and the native
    /// GfxBase tail coherent without publishing a second display request.
    /// </summary>
    internal bool TryClearNativeViewSidecars()
    {
        lock (_monitorStateSync)
        {
        if (!TryPrepareNativeViewSidecarsForLoad(
                0,
                out var nativeCurrentMonitor,
                out var originalCurrentMonitor,
                out var requestedCurrentMonitor,
                out var nativeTopLine,
                out var originalTopLine,
                out var requestedTopLine))
        {
            return false;
        }

        var committed = CommitNativeViewState(
            0,
            0,
            0,
            nativeCurrentMonitor,
            requestedCurrentMonitor,
            originalCurrentMonitor,
            nativeTopLine,
            requestedTopLine,
            originalTopLine);
        if (committed)
        {
            RecordNativeViewSidecarsAfterCommit(
                0,
                nativeCurrentMonitor,
                requestedCurrentMonitor,
                nativeTopLine,
                requestedTopLine);
        }

        return committed;
        }
    }

    private bool TryPrepareNativeViewSidecarsForLoad(
        uint view,
        out uint nativeCurrentMonitor,
        out uint originalCurrentMonitor,
        out uint requestedCurrentMonitor,
        out uint nativeTopLine,
        out ushort originalTopLine,
        out ushort requestedTopLine)
    {
        nativeCurrentMonitor = 0;
        originalCurrentMonitor = 0;
        requestedCurrentMonitor = 0;
        nativeTopLine = 0;
        originalTopLine = 0;
        requestedTopLine = 0;
        if (view == 0)
        {
            return TryPrepareOwnedNativeViewSidecarsForClear(
                out nativeCurrentMonitor,
                out originalCurrentMonitor,
                out requestedCurrentMonitor,
                out nativeTopLine,
                out originalTopLine,
                out requestedTopLine);
        }

        if (!TryPrepareNativeCurrentMonitor(
                view,
                out nativeCurrentMonitor,
                out originalCurrentMonitor,
                out requestedCurrentMonitor))
        {
            return false;
        }

        // An ordinary View has no ViewExtra monitor/timing sidecar.  If this
        // graphics instance published one for the superseded extended View,
        // retire that owned value as part of the same LoadView transaction.
        // Do not touch a value a native/provider owner has since replaced.
        if (nativeCurrentMonitor == 0 &&
            !TryPrepareOwnedNativeCurrentMonitorForClear(
                out nativeCurrentMonitor,
                out originalCurrentMonitor))
        {
            return false;
        }

        if (!TryPrepareNativeTopLine(
                view,
                out nativeTopLine,
                out originalTopLine,
                out requestedTopLine))
        {
            return false;
        }

        if (nativeTopLine == 0 &&
            !TryPrepareOwnedNativeTopLineForClear(
                out nativeTopLine,
                out originalTopLine))
        {
            return false;
        }

        return true;
    }

    private bool TryPrepareOwnedNativeViewSidecarsForClear(
        out uint nativeCurrentMonitor,
        out uint originalCurrentMonitor,
        out uint requestedCurrentMonitor,
        out uint nativeTopLine,
        out ushort originalTopLine,
        out ushort requestedTopLine)
    {
        nativeCurrentMonitor = 0;
        originalCurrentMonitor = 0;
        nativeTopLine = 0;
        originalTopLine = 0;
        requestedCurrentMonitor = 0;
        requestedTopLine = 0;
        if (!TryPrepareOwnedNativeCurrentMonitorForClear(
                out nativeCurrentMonitor,
                out originalCurrentMonitor) ||
            !TryPrepareOwnedNativeTopLineForClear(
                out nativeTopLine,
                out originalTopLine))
        {
            return false;
        }

        return true;
    }

    private bool TryPrepareOwnedNativeCurrentMonitorForClear(
        out uint nativeCurrentMonitor,
        out uint originalValue)
    {
        nativeCurrentMonitor = 0;
        originalValue = 0;
        if (_ownedNativeCurrentMonitorAddress == 0 ||
            _graphicsLibraryBase == 0)
        {
            return true;
        }

        if ((_graphicsLibraryBase & 1u) != 0)
        {
            return false;
        }

        // The native/provider owner may have replaced the GfxBase envelope
        // after this graphics instance published the sidecar.  Once the
        // MonitorList no longer proves a valid native envelope, the cached
        // address is no longer ours to clear—even if the field still happens
        // to contain the value we wrote previously.
        if (!HasNativeGfxBaseEnvelope(_graphicsLibraryBase))
        {
            _ownedNativeCurrentMonitorAddress = 0;
            _ownedNativeCurrentMonitorValue = 0;
            return true;
        }

        if (!TryAddress(
                _graphicsLibraryBase,
                GraphicsLayouts.GfxBaseCurrentMonitor,
                sizeof(uint),
                out var address) ||
            address != _ownedNativeCurrentMonitorAddress ||
            !Memory.TryReadLong(address, out originalValue))
        {
            // The field may have moved to a provider-owned overlay or may be
            // outside a compact host image.  Preserve it and relinquish our
            // claim after a successful blank publication.
            return true;
        }

        if (originalValue != _ownedNativeCurrentMonitorValue)
        {
            // A native/provider caller replaced the value after we published
            // it.  Do not erase that replacement during teardown.
            originalValue = 0;
            return true;
        }

        if (!TryProbeWritableLong(address, originalValue))
        {
            originalValue = 0;
            return false;
        }

        nativeCurrentMonitor = address;
        return true;
    }

    private bool TryPrepareOwnedNativeTopLineForClear(
        out uint nativeTopLine,
        out ushort originalValue)
    {
        nativeTopLine = 0;
        originalValue = 0;
        if (_ownedNativeTopLineAddress == 0 ||
            _graphicsLibraryBase == 0)
        {
            return true;
        }

        if ((_graphicsLibraryBase & 1u) != 0)
        {
            return false;
        }

        // Match CurrentMonitor ownership: a malformed/provider-replaced
        // native envelope relinquishes the cached TopLine sidecar rather
        // than erasing a value that now belongs to the resident owner.
        if (!HasNativeGfxBaseEnvelope(_graphicsLibraryBase))
        {
            _ownedNativeTopLineAddress = 0;
            _ownedNativeTopLineValue = 0;
            return true;
        }

        if (!TryAddress(
            _graphicsLibraryBase,
                GraphicsLayouts.GfxBaseTopLine,
                sizeof(ushort),
                out var address) ||
            address != _ownedNativeTopLineAddress ||
            !Memory.TryReadWord(address, out originalValue))
        {
            return true;
        }

        if (originalValue != _ownedNativeTopLineValue)
        {
            originalValue = 0;
            return true;
        }

        if (!TryProbeWritableWord(address, originalValue))
        {
            originalValue = 0;
            return false;
        }

        nativeTopLine = address;
        return true;
    }

    private void RecordNativeViewSidecarsAfterCommit(
        uint view,
        uint nativeCurrentMonitor,
        uint requestedCurrentMonitor,
        uint nativeTopLine,
        ushort requestedTopLine)
    {
        if (view == 0)
        {
            ForgetOwnedNativeViewSidecars();
            return;
        }

        if (nativeCurrentMonitor != 0)
        {
            _ownedNativeCurrentMonitorAddress = nativeCurrentMonitor;
            _ownedNativeCurrentMonitorValue = requestedCurrentMonitor;
        }
        else
        {
            // A plain View, a compact image, or a provider replacement may
            // leave no portable CurrentMonitor publication in this commit.
            // Retire the old claim so a later rebind/teardown cannot act on
            // an address that is no longer part of the active transaction.
            _ownedNativeCurrentMonitorAddress = 0;
            _ownedNativeCurrentMonitorValue = 0;
        }

        if (nativeTopLine != 0)
        {
            _ownedNativeTopLineAddress = nativeTopLine;
            _ownedNativeTopLineValue = requestedTopLine;
        }
        else
        {
            // Keep TopLine ownership in lockstep with CurrentMonitor when
            // the active View no longer supplies a portable sidecar.
            _ownedNativeTopLineAddress = 0;
            _ownedNativeTopLineValue = 0;
        }
    }

    private void ForgetOwnedNativeViewSidecars()
    {
        _ownedNativeCurrentMonitorAddress = 0;
        _ownedNativeCurrentMonitorValue = 0;
        _ownedNativeTopLineAddress = 0;
        _ownedNativeTopLineValue = 0;
    }

    private bool TryPrepareNativeCurrentMonitor(
        uint view,
        out uint nativeCurrentMonitor,
        out uint originalValue,
        out uint requestedValue)
    {
        nativeCurrentMonitor = 0;
        originalValue = 0;
        requestedValue = 0;
        if (_graphicsLibraryBase == 0)
            return true;

        if ((_graphicsLibraryBase & 1u) != 0)
            return false;

        if (!HasNativeGfxBaseEnvelope(_graphicsLibraryBase))
            return true;

        if (!TryAddress(
                _graphicsLibraryBase,
                GraphicsLayouts.GfxBaseCurrentMonitor,
                sizeof(uint),
                out var address) ||
            !Memory.TryReadLong(address, out originalValue))
        {
            // The compact host image ends before the native monitor tail.
            // Keep that optional sidecar boundary consistent with ActiView.
            return true;
        }

        if (!TryGetAssociatedViewMonitor(
                view,
                out requestedValue,
                out _,
                out var hasMonitor))
        {
            return false;
        }

        if (!hasMonitor)
            return true;

        if (!TryProbeWritableLong(address, originalValue))
        {
            originalValue = 0;
            requestedValue = 0;
            return false;
        }

        nativeCurrentMonitor = address;
        return true;
    }

    private bool TryPrepareNativeTopLine(
        uint view,
        out uint nativeTopLine,
        out ushort originalValue,
        out ushort requestedValue)
    {
        nativeTopLine = 0;
        originalValue = 0;
        requestedValue = 0;
        if (_graphicsLibraryBase == 0)
            return true;

        if ((_graphicsLibraryBase & 1u) != 0)
            return false;

        if (!HasNativeGfxBaseEnvelope(_graphicsLibraryBase))
            return true;

        if (!TryAddress(
                _graphicsLibraryBase,
                GraphicsLayouts.GfxBaseTopLine,
                sizeof(ushort),
                out var address) ||
            !Memory.TryReadWord(address, out originalValue))
        {
            // The compact host image ends before the native TopLine field.
            // Keep this optional tail outside the portable claim boundary.
            return true;
        }

        if (!TryGetAssociatedViewTopLine(
                view,
                out requestedValue,
                out var hasTopLine))
        {
            return false;
        }

        if (!hasTopLine)
            return true;

        // Probe writability without changing the resident value until the
        // display owner accepts the complete View publication.
        if (!TryProbeWritableWord(address, originalValue))
        {
            originalValue = 0;
            requestedValue = 0;
            return false;
        }

        nativeTopLine = address;
        return true;
    }

    private bool TryGetAssociatedViewMonitor(
        uint view,
        out uint monitor,
        out uint monitorField,
        out bool hasMonitor)
    {
        monitor = 0;
        monitorField = 0;
        hasMonitor = false;
        if (view == 0 ||
            !TryReadWordAt(view, GraphicsLayouts.ViewModes, out var viewModes) ||
            (viewModes & GraphicsModeIds.ExtendedMode) == 0 ||
            !_extendedNodes.TryGetAssociatedViewExtra(view, out var viewExtra))
        {
            return true;
        }

        hasMonitor = true;
        if (!TryReadLongAt(viewExtra, GraphicsLayouts.ViewExtraView, out var associatedView) ||
            associatedView != view ||
            !TryAddress(
                viewExtra,
                GraphicsLayouts.ViewExtraMonitor,
                sizeof(uint),
                out monitorField) ||
            !Memory.TryReadLong(monitorField, out monitor) ||
            monitor == 0 ||
            (monitor & 1u) != 0)
        {
            monitor = 0;
            monitorField = 0;
            return false;
        }

        return true;
    }

    private bool TryGetAssociatedViewTopLine(
        uint view,
        out ushort topLine,
        out bool hasTopLine)
    {
        topLine = 0;
        hasTopLine = false;
        if (view == 0 ||
            !TryReadWordAt(view, GraphicsLayouts.ViewModes, out var viewModes) ||
            (viewModes & GraphicsModeIds.ExtendedMode) == 0 ||
            !_extendedNodes.TryGetAssociatedViewExtra(view, out var viewExtra))
        {
            return true;
        }

        if (!TryReadLongAt(viewExtra, GraphicsLayouts.ViewExtraView, out var associatedView) ||
            associatedView != view ||
            !TryReadWordAt(viewExtra, GraphicsLayouts.ViewExtraTopLine, out topLine))
        {
            topLine = 0;
            return false;
        }

        hasTopLine = true;
        return true;
    }

    private bool CommitNativeViewState(
        uint nativeActiveView,
        uint view,
        uint originalActiveView,
        uint nativeCurrentMonitor,
        uint requestedCurrentMonitor,
        uint originalCurrentMonitor,
        uint nativeTopLine,
        ushort requestedTopLine,
        ushort originalTopLine)
    {
        if (!CommitNativeActiveView(nativeActiveView, view, originalActiveView))
            return false;

        if ((nativeCurrentMonitor == 0 ||
             Memory.TryWriteLong(nativeCurrentMonitor, requestedCurrentMonitor)) &&
            (nativeTopLine == 0 ||
             Memory.TryWriteWord(nativeTopLine, requestedTopLine)))
        {
            return true;
        }

        if (nativeActiveView != 0)
            _ = RestoreNativeLong(nativeActiveView, originalActiveView);
        if (nativeCurrentMonitor != 0)
            _ = RestoreNativeLong(nativeCurrentMonitor, originalCurrentMonitor);
        if (nativeTopLine != 0)
            _ = RestoreNativeWord(nativeTopLine, originalTopLine);
        return false;
    }

    private bool RestoreNativeViewState(
        uint nativeActiveView,
        uint originalActiveView,
        uint nativeCurrentMonitor,
        uint originalCurrentMonitor,
        uint nativeTopLine,
        ushort originalTopLine)
    {
        var activeRestored = nativeActiveView == 0 ||
                             RestoreNativeLong(nativeActiveView, originalActiveView);
        var monitorRestored = nativeCurrentMonitor == 0 ||
                              RestoreNativeLong(
                                  nativeCurrentMonitor,
                                  originalCurrentMonitor);
        var topLineRestored = nativeTopLine == 0 ||
                              RestoreNativeWord(nativeTopLine, originalTopLine);
        return activeRestored && monitorRestored && topLineRestored;
    }

    private bool TryPrepareNativeActiveView(
        out uint nativeActiveView,
        out uint originalValue)
    {
        nativeActiveView = 0;
        originalValue = 0;
        if (_graphicsLibraryBase == 0)
        {
            return true;
        }

        // ActiView is a guest LONG in GfxBase.  A byte-readable odd base is
        // still an address-error envelope on a native 68000 and must remain
        // available to the native/provider implementation rather than being
        // claimed by the host adapter.
        if ((_graphicsLibraryBase & 1u) != 0)
        {
            return false;
        }

        if (!TryGetNativeLoadViewPublicationSpan(
                _graphicsLibraryBase,
                out _,
                out _))
        {
            return false;
        }

        if (!TryAddress(
                _graphicsLibraryBase,
                GraphicsLayouts.GfxBaseActiView,
                sizeof(uint),
                out var address) ||
            // The compact host image ends before the public GfxBase field.
            // An unreadable optional field therefore means “no native sidecar”
            // rather than a failed portable LoadView request.
            !Memory.TryReadLong(address, out originalValue))
        {
            return true;
        }

        // A same-value write is a guarded writability probe.  It keeps the
        // native sidecar unchanged until the display backend has accepted the
        // requested publication, while still rejecting a mapped read-only or
        // fault-injected field before the host handoff.
        if (!TryProbeWritableLong(address, originalValue))
        {
            originalValue = 0;
            return false;
        }

        nativeActiveView = address;
        return true;
    }

    private bool CommitNativeActiveView(uint address, uint view, uint originalValue)
    {
        if (address == 0 || Memory.TryWriteLong(address, view))
            return true;

        _ = RestoreNativeLong(address, originalValue);
        return false;
    }

    private bool TryProbeWritableLong(uint address, uint originalValue)
    {
        if (!TrySnapshotNativeBytes(address, sizeof(uint), out var original))
            return false;

        if (Memory.TryWriteLong(address, originalValue))
            return true;

        RestoreNativeBytes(address, original);
        return false;
    }

    private bool TryProbeWritableWord(uint address, ushort originalValue)
    {
        if (!TrySnapshotNativeBytes(address, sizeof(ushort), out var original))
            return false;

        if (Memory.TryWriteWord(address, originalValue))
            return true;

        RestoreNativeBytes(address, original);
        return false;
    }

    private bool RestoreNativeLong(uint address, uint value)
    {
        var success = true;
        success &= Memory.TryWriteByte(address, (byte)(value >> 24));
        success &= Memory.TryWriteByte(address + 1u, (byte)(value >> 16));
        success &= Memory.TryWriteByte(address + 2u, (byte)(value >> 8));
        success &= Memory.TryWriteByte(address + 3u, (byte)value);
        return success;
    }

    private bool RestoreNativeWord(uint address, ushort value)
    {
        var success = true;
        success &= Memory.TryWriteByte(address, (byte)(value >> 8));
        success &= Memory.TryWriteByte(address + 1u, (byte)value);
        return success;
    }

    private bool TrySnapshotNativeBytes(
        uint address,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (!TryAddress(address, 0, byteCount, out _))
            return false;

        original = new byte[byteCount];
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (Memory.TryReadByte(address + (uint)offset, out original[offset]))
                continue;

            original = Array.Empty<byte>();
            return false;
        }

        return true;
    }

    private bool RestoreNativeBytes(uint address, byte[] original)
    {
        var success = true;
        for (var offset = 0; offset < original.Length; offset++)
            success &= Memory.TryWriteByte(address + (uint)offset, original[offset]);
        return success;
    }

    private bool TryGetViewSignature(uint view, out ulong signature)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        signature = offsetBasis;
        if (view == 0)
            return true;

        var current = view;
        var visited = new HashSet<uint>();
        for (var node = 0; node < 64 && current != 0; node++)
        {
            if (!visited.Add(current))
                return false;
            if (node == 0)
            {
                if (!TryHashRange(current, GraphicsLayouts.ViewSize, prime, ref signature))
                    return false;

                if (!TryGetAssociatedViewMonitor(
                        current,
                        out _,
                        out var monitorField,
                        out var hasMonitor) ||
                    (hasMonitor &&
                     !TryHashRange(monitorField, sizeof(uint), prime, ref signature)) ||
                    !TryGetAssociatedViewTopLine(
                        current,
                        out _,
                        out var hasTopLine))
                {
                    return false;
                }

                if (hasTopLine)
                {
                    if (!_extendedNodes.TryGetAssociatedViewExtra(current, out var viewExtra) ||
                        !TryHashRange(
                            viewExtra + (uint)GraphicsLayouts.ViewExtraTopLine,
                            sizeof(ushort),
                            prime,
                            ref signature))
                    {
                        return false;
                    }
                }

                if (!TryReadLongAt(current, GraphicsLayouts.ViewViewPort, out current))
                {
                    return false;
                }
            }
            else
            {
                if (!TryAddress(
                        current,
                        GraphicsLayouts.ViewPortModes,
                        sizeof(ushort),
                        out var modesAddress) ||
                    !TryAddress(
                        current,
                        GraphicsLayouts.ViewPortNext,
                        sizeof(uint),
                        out var nextAddress) ||
                    !Memory.TryReadWord(modesAddress, out var modes) ||
                    !Memory.TryReadLong(nextAddress, out var next))
                {
                    return false;
                }

                if ((modes & GraphicsModeIds.ViewPortHidden) != 0)
                {
                    // Resident display traversal consumes only Modes/Next
                    // for hidden nodes. Keep the signature on that same
                    // ownership boundary so malformed hidden RasInfo or
                    // BitMap payloads cannot reject an otherwise valid view.
                    if (!TryHashRange(modesAddress, sizeof(ushort), prime, ref signature) ||
                        !TryHashRange(nextAddress, sizeof(uint), prime, ref signature))
                    {
                        return false;
                    }

                    current = next;
                    continue;
                }

                if (!TryHashRange(current, GraphicsLayouts.ViewPortSize, prime, ref signature) ||
                    !TryReadLongAt(current, GraphicsLayouts.ViewPortRasInfo, out var rasInfo) ||
                    !TryHashRasInfoChain(rasInfo, prime, ref signature))
                {
                    return false;
                }

                current = next;
            }
        }

        return current == 0;
    }

    private bool TryHashRasInfoChain(
        uint rasInfo,
        ulong prime,
        ref ulong signature)
    {
        var visited = new HashSet<uint>();
        for (var node = 0; node < 64 && rasInfo != 0; node++)
        {
            if (!visited.Add(rasInfo) ||
                !TryHashRange(rasInfo, GraphicsLayouts.RasInfoSize, prime, ref signature) ||
                !TryReadLongAt(rasInfo, GraphicsLayouts.RasInfoNext, out var next) ||
                !TryReadLongAt(rasInfo, GraphicsLayouts.RasInfoBitMap, out var bitMap) ||
                !TryHashBitmapSignature(bitMap, prime, ref signature))
            {
                return false;
            }

            rasInfo = next;
        }

        return rasInfo == 0;
    }

    private bool TryHashBitmapSignature(
        uint bitMap,
        ulong prime,
        ref ulong signature)
    {
        if (bitMap == 0)
            return true;

        // ValidateViewForLoad has already checked this envelope.  Hash only
        // the fields that affect display projection instead of requiring all
        // reserved BitMap bytes to be mapped in a sparse guest address space.
        if ((bitMap & 1u) != 0 ||
            !TryHashRange(bitMap, 6, prime, ref signature) ||
            !TryReadByteAt(bitMap, GraphicsLayouts.BitMapDepth, out var depth) ||
            depth == 0 || depth > 8)
        {
            return false;
        }

        for (var plane = 0; plane < depth; plane++)
        {
            // The active-view signature tracks the guest BitMap plane links,
            // not pixel bytes at the linked storage.  Hashing the plane
            // storage itself made a valid one-row/two-byte raster require an
            // unrelated four-byte read and caused LoadView publication to
            // depend on pixel contents rather than display topology.
            if (!TryAddress(
                    bitMap,
                    GraphicsLayouts.BitMapPlanes + (plane * sizeof(uint)),
                    sizeof(uint),
                    out var planePointerAddress) ||
                !TryHashRange(planePointerAddress, sizeof(uint), prime, ref signature))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryHashRange(
        uint address,
        int byteCount,
        ulong prime,
        ref ulong signature)
    {
        if (address == 0 || byteCount <= 0 ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!Memory.TryReadByte(address + (uint)offset, out var value))
                return false;

            signature ^= value;
            signature *= prime;
        }

        return true;
    }

    private bool TryReadByteAt(uint baseAddress, int offset, out byte value)
    {
        value = 0;
        return TryAddress(baseAddress, offset, sizeof(byte), out var address) &&
               Memory.TryReadByte(address, out value);
    }

    private bool TryReadWordAt(uint baseAddress, int offset, out ushort value)
    {
        value = 0;
        return TryAddress(baseAddress, offset, sizeof(ushort), out var address) &&
               Memory.TryReadWord(address, out value);
    }

    private bool TryReadLongAt(uint baseAddress, int offset, out uint value)
    {
        value = 0;
        return TryAddress(baseAddress, offset, sizeof(uint), out var address) &&
               Memory.TryReadLong(address, out value);
    }

    private static bool TryAddress(
        uint baseAddress,
        int offset,
        int byteCount,
        out uint address)
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

    internal int WaitTOF()
    {
        // Keep the direct compatibility entry on the same scheduler
        // ownership boundary as the timed/register path.  A status-aware
        // display adapter may be present only as a routing seam; when its
        // real frame owner declines, claiming a successful void wait here
        // would fabricate synchronization and prevent the native/provider
        // vector from handling the call.
        if (Display is IGraphicsFrameWaitStatusBackend statusBackend)
        {
            return statusBackend.TryWaitForTopOfFrame(0, out _)
                ? GraphicsRasterOperations.Success
                : GraphicsRasterOperations.Failure;
        }

        Display.WaitForTopOfFrame();
        return GraphicsRasterOperations.Success;
    }

    internal long WaitTOF(long cycle)
        => TryWaitTOF(cycle, out var nextCycle) ? nextCycle : cycle;

    internal bool TryWaitTOF(long cycle, out long nextCycle)
    {
        if (Display is IGraphicsFrameWaitStatusBackend statusBackend)
            return statusBackend.TryWaitForTopOfFrame(cycle, out nextCycle);

        nextCycle = Display is IGraphicsTimedDisplayBackend timedDisplay
            ? timedDisplay.WaitForTopOfFrame(cycle)
            : WaitForTopOfFrameWithoutTiming(cycle);
        return true;
    }

    internal int WaitBOVP(uint viewPort)
    {
        if (!GraphicsRasterOperations.ValidateViewPortForWaitBovp(Memory, viewPort))
            return GraphicsRasterOperations.Failure;

        if (Display is IGraphicsViewportWaitStatusBackend statusBackend)
        {
            return statusBackend.TryWaitForViewportBottom(viewPort, 0, out _)
                ? GraphicsRasterOperations.Success
                : GraphicsRasterOperations.Failure;
        }

        Display.WaitForBeginningOfVerticalBlank(viewPort);
        return GraphicsRasterOperations.Success;
    }

    internal long WaitBOVP(uint viewPort, long cycle)
    {
        _ = TryWaitBOVP(viewPort, cycle, out var nextCycle);
        return nextCycle;
    }

    /// <summary>
    /// Timed WaitBOVP transaction with an explicit validation result for
    /// register dispatch.  A malformed viewport/RasInfo chain must not be
    /// claimed as a successful void wait: the native/provider boundary owns
    /// that case, while a valid request retains the scheduler-aware cycle.
    /// </summary>
    internal bool TryWaitBOVP(uint viewPort, long cycle, out long nextCycle)
    {
        nextCycle = cycle;
        if (!GraphicsRasterOperations.ValidateViewPortForWaitBovp(Memory, viewPort))
            return false;

        if (Display is IGraphicsViewportWaitStatusBackend statusBackend)
            return statusBackend.TryWaitForViewportBottom(viewPort, cycle, out nextCycle);

        nextCycle = Display is IGraphicsTimedDisplayBackend timedDisplay
            ? timedDisplay.WaitForViewportBottom(viewPort, cycle)
            : WaitForViewportBottomWithoutTiming(viewPort, cycle);
        return true;
    }

    internal ushort VBeamPos()
        => Display.GetBeamPosition();

    internal ushort VBeamPos(long cycle)
        => Display is IGraphicsTimedDisplayBackend timedDisplay
            ? timedDisplay.GetBeamPosition(cycle)
            : Display.GetBeamPosition();

    internal bool TryVBeamPos(long cycle, out ushort beamPosition)
    {
        if (Display is IGraphicsBeamPositionStatusBackend statusBackend)
            return statusBackend.TryGetBeamPosition(cycle, out beamPosition);

        // A display that only implements the legacy untimed interface has no
        // cycle-accurate beam contract.  Keep the status-aware host/native
        // gateway fail-closed instead of claiming a fabricated sample from
        // GetBeamPosition().  The direct compatibility helper remains
        // available to callers that explicitly opted into that legacy API.
        beamPosition = 0;
        return false;
    }

    private long WaitForTopOfFrameWithoutTiming(long cycle)
    {
        Display.WaitForTopOfFrame();
        return cycle;
    }

    private long WaitForViewportBottomWithoutTiming(uint viewPort, long cycle)
    {
        Display.WaitForBeginningOfVerticalBlank(viewPort);
        return cycle;
    }

    internal int SetAPen(uint rastPort, uint pen)
        => GraphicsRasterOperations.SetAPen(Memory, rastPort, pen);

    internal int SetBPen(uint rastPort, uint pen)
        => GraphicsRasterOperations.SetBPen(Memory, rastPort, pen);

    internal int SetDrawMode(uint rastPort, uint drawMode)
        => GraphicsRasterOperations.SetDrawMode(Memory, rastPort, drawMode);

    internal int GetAPen(uint rastPort)
        => GraphicsRasterOperations.GetAPen(Memory, rastPort);

    internal int GetBPen(uint rastPort)
        => GraphicsRasterOperations.GetBPen(Memory, rastPort);

    internal int GetDrawMode(uint rastPort)
        => GraphicsRasterOperations.GetDrawMode(Memory, rastPort);

    internal int SetABPenDrMd(uint rastPort, uint foreground, uint background, uint drawMode)
        => GraphicsRasterOperations.SetABPenDrMd(Memory, rastPort, foreground, background, drawMode);

    // SetDrPt is a graphics/gfxmacros.h helper rather than a public LVO, but
    // keeping it beside the RastPort vectors gives host callers and a future
    // native CopperSharp68k body one shared guest-layout implementation.
    internal int SetDrPt(uint rastPort, ushort linePattern)
        => GraphicsRasterOperations.SetDrPt(Memory, rastPort, linePattern);

    // SetAfPt is the companion graphics/gfxmacros.h helper for area
    // patterns.  It is not a public LVO, but sharing the guest-layout
    // publication keeps host callers and a future native CopperSharp68k
    // implementation on the same RastPort contract.
    internal int SetAfPt(uint rastPort, uint areaPattern, sbyte areaPatternSize)
        => GraphicsRasterOperations.SetAfPt(
            Memory,
            rastPort,
            areaPattern,
            areaPatternSize);

    // SetOPen and BNDRYOFF are the classic area-outline macros. Keep their
    // guest-layout implementations alongside the public outline-pen vector
    // without adding synthetic LVOs.
    internal int SetOPen(uint rastPort, byte outlinePen)
        => GraphicsRasterOperations.SetOPen(Memory, rastPort, outlinePen);

    internal int BoundaryOff(uint rastPort)
        => GraphicsRasterOperations.BoundaryOff(Memory, rastPort);

    // SetWrMsk is the classic gfxmacros.h direct Mask assignment. Keep it
    // separate from the public SetWriteMask vector while sharing the same
    // guest-layout byte semantics for host and future native callers.
    internal int SetWrMsk(uint rastPort, uint writeMask)
        => GraphicsRasterOperations.SetWrMsk(Memory, rastPort, writeMask);

    internal int SetWriteMask(uint rastPort, uint writeMask)
        => GraphicsRasterOperations.SetWriteMask(Memory, rastPort, writeMask);

    internal int SetMaxPen(uint rastPort, uint maxPen)
        => GraphicsRasterOperations.SetMaxPen(Memory, rastPort, maxPen);

    internal int GetMaxPen(uint rastPort)
        => GraphicsRasterOperations.GetMaxPen(Memory, rastPort);

    internal int GetOutlinePen(uint rastPort)
        => GraphicsRasterOperations.GetOutlinePen(Memory, rastPort);

    // Compatibility spelling from graphics/gfx.h and gfxmacros.h.  GetOPen
    // shares the V39 GetOutlinePen guest read and does not add a public LVO.
    internal int GetOPen(uint rastPort)
        => GraphicsRasterOperations.GetOPen(Memory, rastPort);

    internal int SetOutlinePen(uint rastPort, uint pen)
        => GraphicsRasterOperations.SetOutlinePen(Memory, rastPort, pen);

    // Compatibility spelling from graphics/gfxmacros.h. This is not a
    // public LVO; it shares the SetOutlinePen guest transaction and result.
    internal int SetAOlPen(uint rastPort, uint pen)
        => GraphicsRasterOperations.SetAOlPen(Memory, rastPort, pen);

    internal int Move(uint rastPort, short x, short y)
        => GraphicsRasterOperations.Move(Memory, rastPort, x, y);

    internal int Draw(uint rastPort, short x, short y)
        => GraphicsRasterOperations.Draw(Memory, rastPort, x, y);

    // DrawCircle is the classic equal-radius DrawEllipse macro helper.
    internal int DrawCircle(uint rastPort, short centerX, short centerY, short radius)
        => GraphicsRasterOperations.DrawCircle(Memory, rastPort, centerX, centerY, radius);

    internal int AreaMove(uint rastPort, short x, short y)
        => GraphicsRasterOperations.AreaMove(Memory, rastPort, x, y);

    internal int AreaDraw(uint rastPort, short x, short y)
        => GraphicsRasterOperations.AreaDraw(Memory, rastPort, x, y);

    internal int AreaEllipse(uint rastPort, short centerX, short centerY, short radiusX, short radiusY)
        => GraphicsRasterOperations.AreaEllipse(Memory, rastPort, centerX, centerY, radiusX, radiusY);

    internal int AreaCircle(uint rastPort, short centerX, short centerY, ushort radius)
        => GraphicsRasterOperations.AreaCircle(Memory, rastPort, centerX, centerY, radius);

    internal int AreaEnd(uint rastPort)
        => GraphicsRasterOperations.AreaEnd(Memory, Allocator, rastPort);

    internal int ReadPixel(uint rastPort, short x, short y)
        => GraphicsRasterOperations.ReadPixel(Memory, rastPort, x, y);

    internal bool TryReadPixel(
        uint rastPort,
        short x,
        short y,
        out int result)
        => GraphicsRasterOperations.TryReadPixel(
            Memory,
            rastPort,
            x,
            y,
            out result);

    internal int WritePixel(uint rastPort, short x, short y)
        => GraphicsRasterOperations.WritePixel(Memory, rastPort, x, y);

    internal bool TryWritePixel(
        uint rastPort,
        short x,
        short y,
        out int result)
        => GraphicsRasterOperations.TryWritePixel(
            Memory,
            rastPort,
            x,
            y,
            out result);

    private bool TryValidatePixelArrayTemporaryRastPort(
        GraphicsRasterOperations.BitmapInfo bitmap,
        int width,
        int height,
        uint array,
        uint temporaryRastPort,
        bool writing,
        byte writeMask)
    {
        // Preserve the classic result/ownership path for empty or malformed
        // geometry, zero-mask writes, and caller arrays that the portable body
        // would reject before it ever dereferenced the temporary workspace.
        if (temporaryRastPort == 0 || width <= 0 || height <= 0 ||
            !GraphicsPixelArrayOperations.TryGetPaddedStrideForStatus(width, out var stride) ||
            (writing && GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask) == 0) ||
            array == 0 ||
            !GraphicsPixelArrayOperations.TryCanAddressArrayForStatus(
                Memory,
                array,
                stride,
                (uint)height,
                (uint)width))
        {
            return true;
        }

        // A supplied temporary RastPort is a native guest structure.  If its
        // links or alignment are not valid, decline the host vector so the
        // resident/CyberGraphX owner can receive the original request.
        return GraphicsPixelArrayOperations.TryValidateTemporaryRastPortForStatus(
            Memory,
            bitmap,
            temporaryRastPort,
            stride);
    }

    private static bool TryAdmitPixelArrayWork(
        GraphicsRasterOperations.BitmapInfo bitmap,
        long width,
        long height,
        uint array,
        bool writing,
        byte writeMask)
    {
        // A null/invalid array or malformed geometry retains the classic
        // result path. The portable body will report its ordinary failure;
        // only a valid, non-empty request that exceeds the bounded host work
        // envelope is left for the native/provider owner.
        if (array == 0 || width <= 0 || height <= 0 ||
            width > int.MaxValue || height > int.MaxValue ||
            !GraphicsPixelArrayOperations.TryGetPaddedStrideForStatus(
                (int)width,
                out var stride))
        {
            return true;
        }

        if (writing &&
            GraphicsRasterOperations.EffectiveWriteMask(bitmap, writeMask) == 0)
        {
            return true;
        }

        // An envelope that cannot fit the portable array contract is a
        // classic claimed failure, not an aggregate-work ownership decline.
        // Preserve that distinction for the public UWORD endpoint maximum;
        // the portable body will return -1 without attempting a host walk.
        var envelopeBytes = (ulong)stride * (ulong)height;
        if (envelopeBytes > int.MaxValue || envelopeBytes > uint.MaxValue ||
            array > uint.MaxValue - (uint)(envelopeBytes - 1))
        {
            return true;
        }

        return GraphicsPixelArrayOperations.TryCanCompletePortableWorkForStatus(
            (uint)width,
            (uint)height,
            stride);
    }

    private bool TryPreflightPixelArrayEnvelopeForStatus(
        long width,
        long height,
        uint array)
    {
        // The V36 bodies claim malformed geometry, NULL arrays, and
        // unrepresentable padded envelopes with their ordinary -1 result.
        // Only a structurally valid envelope whose caller bytes are mapped is
        // an ordering/admission dependency here.
        if (array == 0 || width <= 0 || height <= 0 ||
            width > int.MaxValue || height > int.MaxValue ||
            !GraphicsPixelArrayOperations.TryGetPaddedStrideForStatus(
                (int)width,
                out var stride))
        {
            return true;
        }

        var envelopeBytes = (ulong)stride * (ulong)height;
        if (envelopeBytes == 0 || envelopeBytes > int.MaxValue ||
            envelopeBytes > uint.MaxValue ||
            array > uint.MaxValue - (uint)(envelopeBytes - 1))
        {
            return true;
        }

        return GraphicsPixelArrayOperations.TryCanAddressArrayForStatus(
            Memory,
            array,
            stride,
            (uint)height,
            (uint)width);
    }

    private static bool TryGetPixelArrayBitmapIntersection(
        GraphicsRasterOperations.BitmapInfo bitmap,
        long xStart,
        long yStart,
        long width,
        long height,
        out int probeX,
        out int probeY)
    {
        probeX = 0;
        probeY = 0;
        if (width <= 0 || height <= 0 || bitmap.Width <= 0 || bitmap.Rows <= 0)
            return false;

        var xEnd = width - 1 > long.MaxValue - xStart
            ? long.MaxValue
            : xStart + width - 1;
        var yEnd = height - 1 > long.MaxValue - yStart
            ? long.MaxValue
            : yStart + height - 1;
        var left = Math.Max(0L, xStart);
        var top = Math.Max(0L, yStart);
        var right = Math.Min((long)bitmap.Width - 1, xEnd);
        var bottom = Math.Min((long)bitmap.Rows - 1, yEnd);
        if (left > right || top > bottom)
            return false;

        probeX = checked((int)left);
        probeY = checked((int)top);
        return true;
    }

    private bool TryProbePixelArrayBitmapRegion(
        GraphicsRasterOperations.BitmapInfo bitmap,
        long xStart,
        long yStart,
        long width,
        long height,
        byte planeMask)
    {
        if (width <= 0 || height <= 0 || planeMask == 0)
            return true;

        var xEnd = width - 1 > long.MaxValue - xStart
            ? long.MaxValue
            : xStart + width - 1;
        var yEnd = height - 1 > long.MaxValue - yStart
            ? long.MaxValue
            : yStart + height - 1;
        var left = Math.Max(0L, xStart);
        var top = Math.Max(0L, yStart);
        var right = Math.Min((long)bitmap.Width - 1, xEnd);
        var bottom = Math.Min((long)bitmap.Rows - 1, yEnd);
        if (left > right || top > bottom)
            return true;

        return GraphicsRasterOperations.TryProbeBitmapRegion(
            Memory,
            bitmap,
            checked((int)left),
            checked((int)top),
            checked((int)right),
            checked((int)bottom),
            planeMask);
    }

    internal int ReadPixelLine8(
        uint rastPort,
        int xStart,
        int yStart,
        int width,
        uint array,
        uint temporaryRastPort)
    {
        // Pixel-array helpers are RastPort consumers too.  Keep a direct
        // CopperSharp68k/core caller from bypassing layers.library's
        // clipping, damage, and backing-store ownership boundary.  The
        // register adapter routes layered requests to its explicit provider
        // before reaching this standard-planar path.
        if (!TryRequireUnlayeredRastPort(rastPort))
            return GraphicsRasterOperations.Failure;

        // The Kickstart contract permits a non-negative width.  A zero-width
        // line has no source or destination cells, so claim it after the
        // layer ownership check without requiring a bitmap, pen array, or
        // temporary RastPort envelope that cannot be consumed.
        if (width == 0)
            return GraphicsRasterOperations.Success;

        return GraphicsPixelArrayOperations.ReadPixelLine8(
            Memory,
            rastPort,
            xStart,
            yStart,
            width,
            array,
            temporaryRastPort);
    }

    internal bool TryReadPixelLine8(
        uint rastPort,
        int xStart,
        int yStart,
        int width,
        uint array,
        uint temporaryRastPort,
        out int result)
    {
        if (!TryRequireUnlayeredRastPort(rastPort))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        // Keep the empty-line result on the portable owner.  In particular,
        // do not turn a valid zero-count call with an absent array or
        // temporary workspace into an ownership decline.
        if (width == 0)
        {
            result = GraphicsRasterOperations.Success;
            return true;
        }

        // This is the read-only V36 line path.  Unlike the corresponding
        // writers, it never consumes rp_Mask; keep the status admission
        // aligned with the direct body so a sparse/provider-owned mask byte
        // cannot steal an otherwise valid read.
        if (!GraphicsRasterOperations.TryReadBitmap(
                Memory,
                rastPort,
                out var bitmap))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (!TryAdmitPixelArrayWork(
                bitmap,
                width,
                1,
                array,
                writing: false,
                writeMask: 0))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        // ReadPixelLine8 validates its padded caller output before ReadRows
        // samples the planar source. Keep malformed mapped array storage on
        // the direct -1 result path, but do not observe destination planes
        // before that result is known.
        if (!TryPreflightPixelArrayEnvelopeForStatus(width, 1, array))
        {
            result = GraphicsRasterOperations.Failure;
            return true;
        }

        if (TryGetPixelArrayBitmapIntersection(
                bitmap,
                xStart,
                yStart,
                width,
                1,
                out var probeX,
                out var probeY) &&
            !GraphicsRasterOperations.TryProbeBitmapPixel(
                Memory,
                bitmap,
                probeX,
                probeY))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        var readMask = bitmap.Depth >= 8
            ? (byte)0xFF
            : (byte)((1 << bitmap.Depth) - 1);
        if (!TryProbePixelArrayBitmapRegion(
                bitmap,
                xStart,
                yStart,
                width,
                1,
                readMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (!TryValidatePixelArrayTemporaryRastPort(
                bitmap,
                width,
                1,
                array,
                temporaryRastPort,
                writing: false,
                writeMask: 0))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        result = ReadPixelLine8(
            rastPort,
            xStart,
            yStart,
            width,
            array,
            temporaryRastPort);
        // A valid RastPort owns the vector even when the guest source array
        // or a clipped request produces the classic -1 result.
        return true;
    }

    internal int WritePixelLine8(
        uint rastPort,
        int xStart,
        int yStart,
        int width,
        uint array,
        uint temporaryRastPort,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (!TryRequireUnlayeredRastPort(rastPort))
            return GraphicsRasterOperations.Failure;

        if (width == 0)
            return GraphicsRasterOperations.Success;

        return GraphicsPixelArrayOperations.WritePixelLine8(
            Memory,
            rastPort,
            xStart,
            yStart,
            width,
            array,
            temporaryRastPort,
            pixelVisible);
    }

    internal bool TryWritePixelLine8(
        uint rastPort,
        int xStart,
        int yStart,
        int width,
        uint array,
        uint temporaryRastPort,
        out int result)
    {
        if (!TryRequireUnlayeredRastPort(rastPort))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (width == 0)
        {
            result = GraphicsRasterOperations.Success;
            return true;
        }

        if (!GraphicsRasterOperations.TryReadBitmap(Memory, rastPort, out var bitmap) ||
            !GraphicsRasterOperations.TryReadRastPortByte(
                Memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        var effectiveWriteMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (!TryAdmitPixelArrayWork(
                bitmap,
                width,
                1,
                array,
                writing: true,
                writeMask: writeMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        // The direct V36 body validates the complete caller array before it
        // enters the destination-plane preflight in WriteRows.  Keep that
        // source ordering visible at the status boundary so malformed source
        // storage cannot consume provider-visible destination state first.
        if (effectiveWriteMask != 0 &&
            !TryPreflightPixelArrayEnvelopeForStatus(width, 1, array))
        {
            result = GraphicsRasterOperations.Failure;
            // V36 writers have a public signed count/error result.  Claim the
            // valid RastPort request and publish the direct body's -1 result,
            // but keep the malformed source transparent to destination
            // probes and writes.
            return true;
        }

        if (effectiveWriteMask != 0 &&
            TryGetPixelArrayBitmapIntersection(
                bitmap,
                xStart,
                yStart,
                width,
                1,
                out var probeX,
                out var probeY) &&
            !GraphicsRasterOperations.TryProbeBitmapPixel(
                Memory,
                bitmap,
                probeX,
                probeY))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (effectiveWriteMask != 0 &&
            !TryProbePixelArrayBitmapRegion(
                bitmap,
                xStart,
                yStart,
                width,
                1,
                effectiveWriteMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (!TryValidatePixelArrayTemporaryRastPort(
                bitmap,
                width,
                1,
                array,
                temporaryRastPort,
                writing: true,
                writeMask: writeMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        result = WritePixelLine8(
            rastPort,
            xStart,
            yStart,
            width,
            array,
            temporaryRastPort);
        // A valid RastPort owns the vector even when the guest source array
        // or a clipped request produces the classic -1 result.
        return true;
    }

    internal int ReadPixelArray8(
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        uint temporaryRastPort)
        => !TryRequireUnlayeredRastPort(rastPort)
            ? GraphicsRasterOperations.Failure
            : GraphicsPixelArrayOperations.ReadPixelArray8(
                Memory,
                rastPort,
                xStart,
                yStart,
                xStop,
                yStop,
                array,
                temporaryRastPort);

    internal bool TryReadPixelArray8(
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        uint temporaryRastPort,
        out int result)
    {
        if (!TryRequireUnlayeredRastPort(rastPort) ||
            !GraphicsRasterOperations.TryReadBitmap(Memory, rastPort, out var bitmap))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        var width64 = (long)xStop - xStart + 1L;
        var height64 = (long)yStop - yStart + 1L;
        if (!TryAdmitPixelArrayWork(
                bitmap,
                width64,
                height64,
                array,
                writing: false,
                writeMask: 0))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        // Match the direct ReadPixelArray8 body: the complete padded caller
        // output envelope is admitted before planar source sampling. A valid
        // RastPort still owns a malformed array and publishes the classic -1.
        if (!TryPreflightPixelArrayEnvelopeForStatus(width64, height64, array))
        {
            result = GraphicsRasterOperations.Failure;
            return true;
        }

        if (TryGetPixelArrayBitmapIntersection(
                bitmap,
                xStart,
                yStart,
                width64,
                height64,
                out var probeX,
                out var probeY) &&
            !GraphicsRasterOperations.TryProbeBitmapPixel(
                Memory,
                bitmap,
                probeX,
                probeY))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        var readMask = bitmap.Depth >= 8
            ? (byte)0xFF
            : (byte)((1 << bitmap.Depth) - 1);
        if (!TryProbePixelArrayBitmapRegion(
                bitmap,
                xStart,
                yStart,
                width64,
                height64,
                readMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (width64 <= int.MaxValue && height64 <= int.MaxValue &&
            !TryValidatePixelArrayTemporaryRastPort(
                bitmap,
                (int)width64,
                (int)height64,
                array,
                temporaryRastPort,
                writing: false,
                writeMask: 0))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        result = ReadPixelArray8(
            rastPort,
            xStart,
            yStart,
            xStop,
            yStop,
            array,
            temporaryRastPort);
        // A valid RastPort owns the vector even when the guest source array
        // or a clipped request produces the classic -1 result.
        return true;
    }

    internal int WritePixelArray8(
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        uint temporaryRastPort,
        Func<int, int, bool>? pixelVisible = null)
        => !TryRequireUnlayeredRastPort(rastPort)
            ? GraphicsRasterOperations.Failure
            : GraphicsPixelArrayOperations.WritePixelArray8(
                Memory,
                rastPort,
                xStart,
                yStart,
                xStop,
                yStop,
                array,
                temporaryRastPort,
                pixelVisible);

    internal bool TryWritePixelArray8(
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        uint temporaryRastPort,
        out int result)
    {
        if (!TryRequireUnlayeredRastPort(rastPort) ||
            !GraphicsRasterOperations.TryReadBitmap(Memory, rastPort, out var bitmap) ||
            !GraphicsRasterOperations.TryReadRastPortByte(
                Memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        var width64 = (long)xStop - xStart + 1L;
        var height64 = (long)yStop - yStart + 1L;
        var effectiveWriteMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);
        if (!TryAdmitPixelArrayWork(
                bitmap,
                width64,
                height64,
                array,
                writing: true,
                writeMask: writeMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        // Match the direct WritePixelArray8 source-before-destination order;
        // keep malformed but structurally unrepresentable forms on the
        // classic -1 result path while declining a mapped source fault before
        // any selected planar destination is observed.
        if (effectiveWriteMask != 0 &&
            !TryPreflightPixelArrayEnvelopeForStatus(width64, height64, array))
        {
            result = GraphicsRasterOperations.Failure;
            // Preserve the classic V36 error result for a valid RastPort;
            // unlike the void V40 writer, this vector can report -1 without
            // handing the call to a provider.  Source admission still occurs
            // before any selected destination plane is observed.
            return true;
        }

        if (effectiveWriteMask != 0 &&
            TryGetPixelArrayBitmapIntersection(
                bitmap,
                xStart,
                yStart,
                width64,
                height64,
                out var probeX,
                out var probeY) &&
            !GraphicsRasterOperations.TryProbeBitmapPixel(
                Memory,
                bitmap,
                probeX,
                probeY))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (effectiveWriteMask != 0 &&
            !TryProbePixelArrayBitmapRegion(
                bitmap,
                xStart,
                yStart,
                width64,
                height64,
                effectiveWriteMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        if (width64 <= int.MaxValue && height64 <= int.MaxValue &&
            !TryValidatePixelArrayTemporaryRastPort(
                bitmap,
                (int)width64,
                (int)height64,
                array,
                temporaryRastPort,
                writing: true,
                writeMask))
        {
            result = GraphicsRasterOperations.Failure;
            return false;
        }

        result = WritePixelArray8(
            rastPort,
            xStart,
            yStart,
            xStop,
            yStop,
            array,
            temporaryRastPort);
        // A valid RastPort owns the vector even when the guest source array
        // or a clipped request produces the classic -1 result.
        return true;
    }

    internal int WriteChunkyPixels(
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        int bytesPerRow,
        Func<int, int, bool>? pixelVisible = null)
        => !TryRequireUnlayeredRastPort(rastPort)
            ? GraphicsRasterOperations.Failure
            : GraphicsPixelArrayOperations.WriteChunkyPixels(
                Memory,
                rastPort,
                xStart,
                yStart,
                xStop,
                yStop,
                array,
                bytesPerRow,
                pixelVisible);

    /// <summary>
    /// Status-aware V40 admission for the void chunky-pixel writer.
    ///
    /// The direct body already stages the source row and snapshots every
    /// selected destination plane.  This wrapper mirrors the fields that the
    /// direct body consumes before it claims the register frame, so a native
    /// or provider owner can receive malformed guest storage without a partial
    /// planar write.  Unlike the V36 array vectors, the V40 ABI has no public
    /// count/error result; a failed portable body therefore remains an
    /// ownership decline rather than being converted into a successful D0=0.
    /// </summary>
    internal bool TryWriteChunkyPixels(
        uint rastPort,
        int xStart,
        int yStart,
        int xStop,
        int yStop,
        uint array,
        int bytesPerRow,
        out int result)
    {
        result = GraphicsRasterOperations.Failure;
        if (!TryRequireUnlayeredRastPort(rastPort) ||
            !GraphicsRasterOperations.TryReadBitmap(Memory, rastPort, out var bitmap) ||
            !GraphicsRasterOperations.TryReadRastPortByte(
                Memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return false;
        }

        var width64 = (long)xStop - xStart + 1L;
        var height64 = (long)yStop - yStart + 1L;
        if (width64 <= 0 || height64 <= 0 ||
            width64 > uint.MaxValue || height64 > uint.MaxValue ||
            bytesPerRow <= 0 ||
            (ulong)(uint)bytesPerRow < (ulong)width64)
        {
            return false;
        }

        var effectiveWriteMask = GraphicsRasterOperations.EffectiveWriteMask(
            bitmap,
            writeMask);

        // A zero effective mask is a successful native no-op after the
        // documented geometry check.  Keep the source array and planar
        // envelope out of the admission path just as the direct body does.
        if (effectiveWriteMask != 0)
        {
            if (!GraphicsPixelArrayOperations.TryCanCompletePortableWorkForStatus(
                    (uint)width64,
                    (uint)height64,
                    (uint)bytesPerRow))
            {
                return false;
            }

            // WriteChunkyPixels consumes the logical pixel bytes in each
            // caller row and uses bytesPerRow only to advance to the next
            // row.  Do not claim a dependency on unreadable padding bytes.
            // Keep this source admission ahead of the destination probes:
            // the direct V40 body snapshots the caller array before it
            // preflights the planar destination, so a malformed source must
            // not consume destination memory or provider-visible state before
            // the native/provider handoff.
            if (!GraphicsPixelArrayOperations.TryCanAddressLogicalArrayForStatus(
                    Memory,
                    array,
                    (uint)bytesPerRow,
                    (uint)height64,
                    (uint)width64))
            {
                return false;
            }

            if (TryGetPixelArrayBitmapIntersection(
                    bitmap,
                    xStart,
                    yStart,
                    width64,
                    height64,
                    out var probeX,
                    out var probeY) &&
                !GraphicsRasterOperations.TryProbeBitmapPixel(
                    Memory,
                    bitmap,
                    probeX,
                    probeY))
            {
                return false;
            }

            if (!TryProbePixelArrayBitmapRegion(
                bitmap,
                xStart,
                yStart,
                width64,
                height64,
                effectiveWriteMask))
            {
                return false;
            }
        }

        result = WriteChunkyPixels(
            rastPort,
            xStart,
            yStart,
            xStop,
            yStop,
            array,
            bytesPerRow);
        return result != GraphicsRasterOperations.Failure;
    }

    internal int RectFill(uint rastPort, short xMin, short yMin, short xMax, short yMax)
        => GraphicsRasterOperations.RectFill(Memory, rastPort, xMin, yMin, xMax, yMax);

    internal int DrawEllipse(uint rastPort, short centerX, short centerY, short radiusX, short radiusY)
        => GraphicsRasterOperations.DrawEllipse(Memory, rastPort, centerX, centerY, radiusX, radiusY);

    internal int ScrollRaster(
        uint rastPort,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => GraphicsRasterOperations.ScrollRaster(Memory, rastPort, deltaX, deltaY, xMin, yMin, xMax, yMax);

    internal int ScrollRasterBF(
        uint rastPort,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax)
        => GraphicsRasterOperations.ScrollRasterBF(Memory, rastPort, deltaX, deltaY, xMin, yMin, xMax, yMax);

    internal int Flood(uint rastPort, uint mode, short x, short y)
        => GraphicsRasterOperations.Flood(Memory, Allocator, rastPort, mode, x, y);

    internal int SetRast(uint rastPort, uint pen)
        => GraphicsRasterOperations.SetRast(Memory, rastPort, pen);

    internal int PolyDraw(uint rastPort, ushort count, uint pointsAddress)
        => GraphicsRasterOperations.PolyDraw(Memory, rastPort, count, pointsAddress);

    internal int ClearEOL(uint rastPort)
        => GraphicsRasterOperations.ClearEOL(Memory, rastPort);

    internal int ClearScreen(uint rastPort)
        => GraphicsRasterOperations.ClearScreen(Memory, rastPort);

    internal int EraseRect(uint rastPort, short xMin, short yMin, short xMax, short yMax)
        => GraphicsRasterOperations.EraseRect(Memory, rastPort, xMin, yMin, xMax, yMax);

    internal int TextLength(uint rastPort, uint count)
        => GraphicsRasterOperations.TextLength(Memory, rastPort, count);

    internal uint OpenFont(
        uint textAttrAddress,
        IGraphicsFontLifecycleBackend lifecycle)
        => GraphicsFontOperations.OpenFont(lifecycle, textAttrAddress);

    internal bool TryWeighTAMatch(
        uint requestedTextAttr,
        uint targetTextAttr,
        uint targetTags,
        IGraphicsFontMatchBackend matcher,
        out short weight)
        => matcher.TryWeighTAMatch(
            requestedTextAttr,
            targetTextAttr,
            targetTags,
            out weight);

    internal int CloseFont(
        uint fontAddress,
        IGraphicsFontLifecycleBackend lifecycle)
        => GraphicsFontOperations.CloseFont(lifecycle, fontAddress);

    internal int AddFont(
        uint fontAddress,
        IGraphicsFontLifecycleBackend lifecycle)
        => GraphicsFontOperations.AddFont(lifecycle, fontAddress);

    internal int RemFont(
        uint fontAddress,
        IGraphicsFontLifecycleBackend lifecycle)
        => GraphicsFontOperations.RemFont(lifecycle, fontAddress);

    internal bool ExtendFont(
        uint fontAddress,
        uint fontTags,
        IGraphicsFontLifecycleBackend lifecycle)
        => GraphicsFontOperations.ExtendFont(lifecycle, fontAddress, fontTags);

    internal int StripFont(
        uint fontAddress,
        IGraphicsFontLifecycleBackend lifecycle)
        => GraphicsFontOperations.StripFont(lifecycle, fontAddress);

    internal int AskFont(uint rastPort, uint textAttrAddress)
        => GraphicsFontOperations.AskFont(Memory, rastPort, textAttrAddress);

    internal int TextLength(uint rastPort, uint textAddress, uint count, IGraphicsFontBackend fonts)
        => GraphicsTextOperations.TextLength(Memory, rastPort, textAddress, count, fonts);

    internal bool TryTextLength(
        uint rastPort,
        uint textAddress,
        uint count,
        IGraphicsFontBackend fonts,
        out int length)
        => GraphicsTextOperations.TryTextLength(
            Memory,
            rastPort,
            textAddress,
            count,
            fonts,
            out length);

    internal bool TryTextLength(uint rastPort, uint count, out int length)
        => GraphicsRasterOperations.TryTextLength(Memory, rastPort, count, out length);

    internal int TextExtent(uint rastPort, uint textAddress, uint count, uint extentAddress, IGraphicsFontBackend fonts)
        => GraphicsTextOperations.TextExtent(Memory, rastPort, textAddress, count, extentAddress, fonts);

    internal int TextFit(
        uint rastPort,
        uint textAddress,
        uint count,
        uint extentAddress,
        uint constrainingExtentAddress,
        int direction,
        uint constrainingWidth,
        uint constrainingHeight,
        IGraphicsFontBackend fonts)
        => GraphicsTextOperations.TextFit(
            Memory,
            rastPort,
            textAddress,
            count,
            extentAddress,
            constrainingExtentAddress,
            direction,
            constrainingWidth,
            constrainingHeight,
            fonts);

    internal int FontExtent(uint fontAddress, uint extentAddress, IGraphicsFontBackend fonts)
        => GraphicsTextOperations.FontExtent(Memory, fontAddress, extentAddress, fonts);

    internal int AdvanceText(uint rastPort, uint count)
        => GraphicsRasterOperations.AdvanceText(Memory, rastPort, count);

    internal int SetFont(uint rastPort, uint fontAddress, IGraphicsFontBackend fonts)
        => GraphicsTextOperations.SetFont(Memory, rastPort, fontAddress, fonts);

    internal int AskSoftStyle(uint rastPort, IGraphicsFontBackend fonts)
        => GraphicsTextOperations.AskSoftStyle(Memory, rastPort, fonts);

    internal int SetSoftStyle(uint rastPort, uint style, uint enable, IGraphicsFontBackend fonts)
        => GraphicsTextOperations.SetSoftStyle(Memory, rastPort, style, enable, fonts);

    internal bool SetRPAttrs(
        uint rastPort,
        uint tags,
        IGraphicsFontBackend fonts,
        uint defaultFont = 0)
        => GraphicsRastPortAttributeOperations.Set(
            Memory,
            rastPort,
            tags,
            fonts,
            defaultFont);

    internal bool GetRPAttrs(uint rastPort, uint tags, IGraphicsFontBackend fonts)
        => GraphicsRastPortAttributeOperations.Get(Memory, rastPort, tags, fonts, _layerRaster);

    internal int Text(uint rastPort, uint textAddress, uint count, IGraphicsFontBackend fonts)
        => GraphicsTextOperations.Text(Memory, rastPort, textAddress, count, fonts);

    private bool TryRequireUnlayeredRastPort(uint rastPort)
        => GraphicsRasterOperations.TryReadRastPortLong(
               Memory,
               rastPort,
               GraphicsLayouts.RastPortLayer,
               out var layer) &&
           layer == 0;
}
