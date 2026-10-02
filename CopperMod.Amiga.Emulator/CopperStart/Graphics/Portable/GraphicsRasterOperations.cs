using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

internal static class GraphicsRasterOperations
{
    // PolyDraw consumes a bounded guest point stream, but each connected
    // segment may still traverse the full signed-WORD coordinate range. Keep
    // the aggregate Bresenham walk within the same host work contract used by
    // the other transactional planar primitives before entering any pixel
    // probe or publication loop.
    private const ulong PortableWorkLimit = int.MaxValue;

    // ScrollRaster stages a source snapshot, probes the clipped destination,
    // captures its rollback bytes, and then publishes the moved/backfilled
    // pixels. Keep the aggregate logical-cell work bounded before the source
    // walk begins; a representable UWORD rectangle must not force a large
    // host staging pass before a later destination guard declines it.
    private const ulong ScrollPassesPerCell = 4;

    // ClearScreen is one guest vector, but its portable transaction probes
    // the text-prefix and remaining-row rectangles, snapshots the selected
    // planes, and then publishes both fills.  Bound the combined logical-cell
    // work before either clipped walk starts; otherwise two individually
    // representable rectangles could still force an aggregate multi-billion
    // pixel host traversal.
    private const ulong ClearScreenPassesPerCell = 4;
    private const ulong ClearEolPassesPerCell = 4;
    private const ulong BltPatternPassesPerCell = 3;

    // DrawEllipse preflights the outline, snapshots its complete bounding
    // rectangle for rollback, and then publishes the outline.  Bound the
    // combined envelope before any selected plane is read.

    // Flood is a single guest vector, but its portable implementation must
    // complete three potentially full-raster passes before it can publish a
    // result: destination preflight, rollback snapshot, and connected-region
    // traversal.  Keep that aggregate logical-cell work inside the host
    // contract instead of allowing a valid UWORD bitmap to drive a
    // multi-billion-entry managed walk or pending list.
    private const ulong FloodPassesPerCell = 3;

    // AreaEnd builds one temporary mask per recorded shape, rasterizes the
    // shape, snapshots its destination, and then publishes the fill. Keep
    // the aggregate logical-cell work inside the same bounded host contract
    // before any managed mask or guest temporary raster is acquired.
    private const ulong AreaPassesPerCell = 4;

    // SetRast probes selected planes, retains a rollback snapshot, and then
    // publishes the pen. A depth multiplier can make that transaction
    // unstageable even when one plane and the logical pixel count are each
    // individually representable.
    private const ulong SetRastPassesPerCell = 3;

    internal sealed class MintermScratch
    {
        private readonly byte[] _original = new byte[8];
        private readonly byte[] _setterOriginal = new byte[8];
        private readonly byte[] _setterNext = new byte[8];
        private bool _acquired;

        internal bool TryAcquire(
            out byte[] original,
            out byte[] setterOriginal,
            out byte[] setterNext)
        {
            original = _original;
            setterOriginal = _setterOriginal;
            setterNext = _setterNext;
            if (_acquired)
                return false;
            _acquired = true;
            return true;
        }

        internal void Release() => _acquired = false;
    }

    internal sealed class VisibilityScratch
    {
        private readonly Dictionary<long, bool> _cache = new();
        private readonly Func<int, int, bool> _callback;
        private Func<int, int, bool>? _source;
        private bool _acquired;

        internal VisibilityScratch()
            => _callback = IsVisible;

        internal bool TryAcquire(
            Func<int, int, bool> source,
            out Func<int, int, bool> callback)
        {
            callback = _callback;
            if (_acquired)
                return false;
            _cache.Clear();
            _source = source;
            _acquired = true;
            return true;
        }

        internal void Release()
        {
            _source = null;
            _cache.Clear();
            _acquired = false;
        }

        private bool IsVisible(int pixelX, int pixelY)
        {
            var key = ((long)pixelX << 32) | (uint)pixelY;
            if (_cache.TryGetValue(key, out var visible))
                return visible;

            visible = _source!(pixelX, pixelY);
            _cache[key] = visible;
            return visible;
        }
    }

    internal sealed class EndpointVisibilityScratch
    {
        private readonly Func<int, int, bool> _callback;
        private Func<int, int, bool>? _source;
        private int _endpointX;
        private int _endpointY;
        private bool _acquired;

        internal EndpointVisibilityScratch()
            => _callback = IsVisible;

        internal bool Resolved { get; private set; }
        internal bool Visible { get; private set; }

        internal bool TryAcquire(
            Func<int, int, bool> source,
            int endpointX,
            int endpointY,
            out Func<int, int, bool> callback)
        {
            callback = _callback;
            if (_acquired)
                return false;
            _source = source;
            _endpointX = endpointX;
            _endpointY = endpointY;
            Resolved = false;
            Visible = false;
            _acquired = true;
            return true;
        }

        internal void Release()
        {
            _source = null;
            Resolved = false;
            Visible = false;
            _acquired = false;
        }

        private bool IsVisible(int x, int y)
        {
            if (x != _endpointX || y != _endpointY)
                return _source!(x, y);
            if (!Resolved)
            {
                Visible = _source!(x, y);
                Resolved = true;
            }
            return Visible;
        }
    }

    internal sealed class FloodScratch
    {
        internal VisibilityScratch Visibility { get; } = new();
        internal List<uint> SnapshotAddresses { get; } = new();
        internal List<byte> SnapshotValues { get; } = new();
        internal List<int> Pending { get; } = new();

        internal void Reset()
        {
            SnapshotAddresses.Clear();
            SnapshotValues.Clear();
            Pending.Clear();
            SnapshotAddresses.TrimExcess();
            SnapshotValues.TrimExcess();
            Pending.TrimExcess();
        }
    }

    internal sealed class AreaScratch
    {
        internal VisibilityScratch Visibility { get; } = new();
        internal EndpointVisibilityScratch EndpointVisibility { get; } = new();
        internal MintermScratch Minterms { get; } = new();
        internal readonly List<List<AreaPoint>> _polygons = new();
        internal readonly List<List<AreaPoint>> _polygonPool = new();
        internal readonly List<AreaEllipseInfo> _ellipses = new();
        internal readonly List<AreaShape> _shapes = new();
        internal readonly List<AreaEdge> _edges = new();
        internal readonly List<AreaCrossing> _crossings = new();
        internal readonly List<uint> _snapshotAddresses = new();
        internal readonly List<byte> _snapshotValues = new();
        internal readonly List<(int X, int Y)> _outlineEllipsePoints = new();
        internal readonly List<uint> _outlineSnapshotAddresses = new();
        internal readonly List<byte> _outlineSnapshotValues = new();
        // The Area collectors are individually atomic and otherwise allocate
        // fresh rollback arrays for every vector. A validated layered raster
        // scope is non-reentrant, so retain the largest collector rollback
        // shape in its owner-provided scratch instead.
        internal readonly byte[] _stateOriginal =
            new byte[GraphicsLayouts.AreaInfoFirstY + 2 -
                GraphicsLayouts.AreaInfoVectorPointer];
        internal readonly byte[] _vectorOriginal0 = new byte[4];
        internal readonly byte[] _vectorOriginal1 = new byte[4];
        internal readonly byte[] _vectorOriginal2 = new byte[4];
        internal readonly byte[] _vectorOriginal3 = new byte[4];
        internal readonly byte[] _vectorOriginal4 = new byte[4];
        internal readonly byte[] _vectorOriginal5 = new byte[4];
        private bool[] _managedMask = Array.Empty<bool>();

        internal List<AreaPoint> AcquirePolygon(int index)
        {
            while (_polygonPool.Count <= index)
                _polygonPool.Add(new List<AreaPoint>());
            var polygon = _polygonPool[index];
            polygon.Clear();
            return polygon;
        }

        internal bool TryAcquireManagedMask(int cells, out bool[] mask)
        {
            mask = _managedMask;
            if (cells <= 0)
                return false;
            if (mask.Length < cells)
            {
                try
                {
                    Array.Resize(ref _managedMask, cells);
                }
                catch (OutOfMemoryException)
                {
                    return false;
                }
                mask = _managedMask;
            }
            Array.Clear(mask, 0, cells);
            return true;
        }

        internal void Reset()
        {
            _polygons.Clear();
            foreach (var polygon in _polygonPool)
            {
                polygon.Clear();
                polygon.TrimExcess();
            }
            _polygonPool.Clear();
            _ellipses.Clear();
            _shapes.Clear();
            _edges.Clear();
            _crossings.Clear();
            _snapshotAddresses.Clear();
            _snapshotValues.Clear();
            _outlineEllipsePoints.Clear();
            _outlineSnapshotAddresses.Clear();
            _outlineSnapshotValues.Clear();
            _polygons.TrimExcess();
            _polygonPool.TrimExcess();
            _ellipses.TrimExcess();
            _shapes.TrimExcess();
            _edges.TrimExcess();
            _crossings.TrimExcess();
            _snapshotAddresses.TrimExcess();
            _snapshotValues.TrimExcess();
            _outlineEllipsePoints.TrimExcess();
            _outlineSnapshotAddresses.TrimExcess();
            _outlineSnapshotValues.TrimExcess();
            _managedMask = Array.Empty<bool>();
        }
    }

    /// <summary>
    /// Tracks bitmap structures allocated through this graphics-library
    /// instance. The classic FreeBitMap contract is specifically for a
    /// bitmap returned by AllocBitMap; keeping that ownership at the library
    /// boundary prevents a foreign/RTG bitmap or stale pointer from being
    /// interpreted as portable planar storage.
    /// </summary>
    internal sealed class Registry
    {
        private readonly Dictionary<uint, BitMapAllocation> _allocated = new();

        internal bool TryRegister(IGraphicsMemory memory, uint bitMap)
        {
            if (!TryCaptureAllocatedBitMap(memory, bitMap, out var allocation))
                return false;

            return _allocated.TryAdd(bitMap, allocation);
        }

        internal bool Contains(uint bitMap)
            => bitMap != 0 && _allocated.ContainsKey(bitMap);

        internal bool TryGet(uint bitMap, out BitMapAllocation allocation)
            => _allocated.TryGetValue(bitMap, out allocation!);

        internal void Remove(uint bitMap)
            => _allocated.Remove(bitMap);
    }

    /// <summary>
    /// Immutable storage contract captured immediately after AllocBitMap
    /// publishes a portable planar bitmap. Guest code may update a BitMap's
    /// public geometry for drawing, but FreeBitMap must never reinterpret
    /// those mutable fields as a different allocator span.
    /// </summary>
    internal sealed class BitMapAllocation
    {
        private readonly ushort _bytesPerRow;
        private readonly ushort _rows;
        private readonly byte _depth;
        private readonly byte _flags;
        private readonly uint[] _planes;

        internal BitMapAllocation(
            ushort bytesPerRow,
            ushort rows,
            byte depth,
            byte flags,
            uint[] planes)
        {
            _bytesPerRow = bytesPerRow;
            _rows = rows;
            _depth = depth;
            _flags = flags;
            _planes = planes;
        }

        internal bool Matches(
            ushort bytesPerRow,
            ushort rows,
            byte depth,
            byte flags,
            uint[] planes)
        {
            if (_bytesPerRow != bytesPerRow ||
                _rows != rows ||
                _depth != depth ||
                _flags != flags ||
                _planes.Length != depth ||
                planes.Length < depth)
            {
                return false;
            }

            for (var plane = 0; plane < depth; plane++)
            {
                if (_planes[plane] != planes[plane])
                    return false;
            }

            return true;
        }
    }

    internal const int Success = 0;
    internal const int Failure = -1;
    // Flood is a public BOOL vector rather than a void/status raster helper:
    // Kickstart returns TRUE (1) after a valid fill, including a zero-mask
    // no-write operation. Failure remains a private decline sentinel.
    internal const int FloodSuccess = 1;

    // These are the classic AreaInfo flag values.  They are part of the
    // guest-visible collector ABI, not private tags: MOVE starts a polygon,
    // DRAW continues it, CLOSEDRAW terminates it, and ELLIPSE consumes two
    // consecutive vector entries (center/radii).
    private const byte AreaMoveFlag = 0;
    private const byte AreaDrawFlag = 1;
    private const byte AreaCloseDrawFlag = 2;
    private const byte AreaEllipseFlag = 3;

    private const uint BmfClear = 1u << 0;
    private const uint BmfDisplayable = 1u << 1;
    // Public BitMap flags are kept as guest-width values, but the blitter
    // compatibility path also needs to distinguish the V39/V40 interleaved
    // source quirk in BltMaskBitMapRastPort.
    internal const uint BmfInterleaved = 1u << 2;
    private const uint BmfStandard = 1u << 3;
    private const uint BmfMinPlanes = 1u << 4;
    private const uint PublicBitmapFlags =
        BmfDisplayable | BmfInterleaved | BmfStandard;

    private const byte DrawModeJam2 = 1;
    private const byte DrawModeComplement = 2;
    private const byte DrawModeInverseVideo = 4;

    internal static bool InitializeRastPort(
        IGraphicsMemory memory,
        uint rastPort,
        uint defaultFont = 0,
        GraphicsFontMetrics? defaultMetrics = null)
    {
        // RastPort contains word/long fields and is read by the native
        // drawing/text entry points through 68k-aligned accesses.  Do not
        // turn an odd guest address into a host-only byte clear; the native
        // or provider owner must retain that address-error boundary.
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (!TryClear(memory, rastPort, GraphicsLayouts.RastPortSize, out var original))
            return false;

        var initialized =
            TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortMask, 0xFF) &&
            TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortFgPen, 0xFF) &&
            TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortBgPen, 0) &&
            TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortOutlinePen, 0xFF) &&
            TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortDrawMode, 1) &&
            TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortLinePatternCount, 0) &&
            TryWriteRastPortWord(memory, rastPort, GraphicsLayouts.RastPortLinePattern, 0xFFFF) &&
            WriteRastPortMinterms(memory, rastPort, ComputeMinterms(0xFF, 0, DrawModeJam2));
        if (!initialized)
        {
            Restore(memory, rastPort, original);
            return false;
        }

        if (defaultFont == 0 || !defaultMetrics.HasValue)
            return true;

        var metrics = defaultMetrics.Value;
        if (TryWriteRastPortLong(
                   memory,
                   rastPort,
                   GraphicsLayouts.RastPortFont,
                   defaultFont) &&
            TryWriteRastPortByte(
                   memory,
                   rastPort,
                   GraphicsLayouts.RastPortAlgoStyle,
                   0) &&
            TryWriteRastPortWord(
                   memory,
                   rastPort,
                   GraphicsLayouts.RastPortTextHeight,
                   metrics.Height) &&
            TryWriteRastPortWord(
                   memory,
                   rastPort,
                   GraphicsLayouts.RastPortTextWidth,
                   metrics.Width) &&
            TryWriteRastPortWord(
                   memory,
                   rastPort,
                   GraphicsLayouts.RastPortTextBaseline,
                   metrics.Baseline))
        {
            return true;
        }

        Restore(memory, rastPort, original);
        return false;
    }

    internal static bool InitializeView(IGraphicsMemory memory, uint view)
    {
        // View contains word/long fields and is consumed by 68k word
        // accesses.  An odd guest base would raise an address error on the
        // native implementation; keep the portable boundary available for
        // that owner instead of byte-clearing an impossible structure.
        if (view == 0 || (view & 1u) != 0)
            return false;

        if (!TryClear(memory, view, GraphicsLayouts.ViewSize, out var original) ||
            !memory.TryWriteWord(
                view + (uint)GraphicsLayouts.ViewDyOffset,
                unchecked((ushort)GraphicsLayouts.ViewDefaultDyOffset)) ||
            !memory.TryWriteWord(
                view + (uint)GraphicsLayouts.ViewDxOffset,
                unchecked((ushort)GraphicsLayouts.ViewDefaultDxOffset)))
        {
            Restore(memory, view, original);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the guest-side portion of a View before it reaches the
    /// scheduler-aware publication boundary.  Copper-list pointers and a
    /// missing RasInfo chain are legal while a view is being assembled; the
    /// structure ranges and any linked nodes that are present are not.
    /// </summary>
    internal static bool ValidateView(IGraphicsMemory memory, uint view)
        => ValidateView(memory, view, validateBitMap: false);

    /// <summary>
    /// Validates the complete display chain used by LoadView.  Unlike the
    /// construction/copper paths, publication requires present bitmap links
    /// to carry a complete standard-planar display envelope.  A present
    /// RasInfo chain also follows the same one-node/single-playfield or
    /// exactly-two-node/DUALPF cardinality used by the copper builder; a null
    /// head remains legal while a ViewPort is still being assembled.  When a
    /// visible ViewPort supplies a DspIns link, the portable owner claims only
    /// the bounded preallocated blank CopList/raw/LOF/SHF envelope; foreign
    /// display/provider links remain on their explicit owner path.
    /// </summary>
    internal static bool ValidateViewForLoad(IGraphicsMemory memory, uint view)
    {
        if ((view & 1u) != 0 ||
            !TryProbeRange(memory, view, GraphicsLayouts.ViewSize) ||
            !memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                out var viewPort))
        {
            return false;
        }

        if (!memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewLoFCprList,
                out var viewLoFCprList) ||
            !memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewShFCprList,
                out var viewShFCprList))
        {
            return false;
        }

        var visibleNode = 0;

        // A resident display traversal consumes only the public Modes/Next
        // links of a hidden ViewPort. Keep that node available to a native
        // or provider owner instead of requiring a displayable bitmap that
        // will never be presented. Visible nodes retain the complete strict
        // display and one-node/DUALPF envelope.
        for (var node = 0; node < 64 && viewPort != 0; node++)
        {
            var currentViewPort = viewPort;
            if ((currentViewPort & 1u) != 0 ||
                !TryAddress(
                    currentViewPort,
                    GraphicsLayouts.ViewPortModes,
                    sizeof(ushort),
                    out var modesAddress) ||
                !TryAddress(
                    currentViewPort,
                    GraphicsLayouts.ViewPortNext,
                    sizeof(uint),
                    out var nextAddress) ||
                !memory.TryReadWord(modesAddress, out var modes) ||
                !memory.TryReadLong(nextAddress, out viewPort))
            {
                return false;
            }

            if ((modes & GraphicsModeIds.ViewPortHidden) != 0)
                continue;

            if (!TryProbeRange(memory, currentViewPort, GraphicsLayouts.ViewPortSize) ||
                SpansOverlap(
                    view,
                    (uint)GraphicsLayouts.ViewSize,
                    currentViewPort,
                    (uint)GraphicsLayouts.ViewPortSize) ||
                !memory.TryReadLong(
                    currentViewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                    out var rasInfo) ||
                !memory.TryReadLong(
                    currentViewPort + (uint)GraphicsLayouts.ViewPortColorMap,
                    out var colorMap) ||
                !memory.TryReadLong(
                    currentViewPort + (uint)GraphicsLayouts.ViewPortDspIns,
                    out var dspIns) ||
                !memory.TryReadLong(
                    currentViewPort + (uint)GraphicsLayouts.ViewPortSprIns,
                    out var sprIns) ||
                !memory.TryReadLong(
                    currentViewPort + (uint)GraphicsLayouts.ViewPortClrIns,
                    out var clrIns) ||
                !memory.TryReadLong(
                    currentViewPort + (uint)GraphicsLayouts.ViewPortUCopIns,
                    out var uCopIns))
            {
                return false;
            }

            var hasPreallocatedCopperState =
                dspIns != 0;
            if (hasPreallocatedCopperState &&
                (visibleNode != 0 || viewPort != 0 ||
                 !ValidatePreallocatedLoadViewResources(
                     memory,
                     currentViewPort,
                     rasInfo,
                     dspIns,
                     viewLoFCprList,
                     viewShFCprList)))
            {
                return false;
            }

            if (rasInfo != 0 &&
                (!ValidateLoadViewRasInfoChain(
                    memory,
                    view,
                    currentViewPort,
                    rasInfo) ||
                 !ValidateViewPortForMake(memory, currentViewPort)))
            {
                return false;
            }

            visibleNode++;
        }

        return viewPort == 0;
    }

    private static bool ValidateLoadViewRasInfoChain(
        IGraphicsMemory memory,
        uint view,
        uint viewPort,
        uint rasInfo)
    {
        // LoadView consumes the complete visible ViewPort/RasInfo/BitMap
        // topology.  Keep those public envelopes pairwise disjoint before a
        // display owner can observe the chain; otherwise a provider can make
        // RasInfo or BitMap fields alias View/ViewPort state and turn a
        // later projection write into corruption of the caller's root View.
        for (var index = 0; index < 64 && rasInfo != 0; index++)
        {
            if ((rasInfo & 1u) != 0 ||
                !TryProbeRange(memory, rasInfo, GraphicsLayouts.RasInfoSize) ||
                SpansOverlap(
                    rasInfo,
                    (uint)GraphicsLayouts.RasInfoSize,
                    view,
                    (uint)GraphicsLayouts.ViewSize) ||
                SpansOverlap(
                    rasInfo,
                    (uint)GraphicsLayouts.RasInfoSize,
                    viewPort,
                    (uint)GraphicsLayouts.ViewPortSize) ||
                !memory.TryReadLong(
                    rasInfo + (uint)GraphicsLayouts.RasInfoNext,
                    out var next) ||
                !memory.TryReadLong(
                    rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                    out var bitMap) ||
                !memory.TryReadWord(
                    rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset,
                    out _) ||
                !memory.TryReadWord(
                    rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset,
                    out _))
            {
                return false;
            }

            if (bitMap != 0 &&
                (SpansOverlap(
                        bitMap,
                        (uint)GraphicsLayouts.BitMapSize,
                        view,
                        (uint)GraphicsLayouts.ViewSize) ||
                 SpansOverlap(
                        bitMap,
                        (uint)GraphicsLayouts.BitMapSize,
                        viewPort,
                        (uint)GraphicsLayouts.ViewPortSize) ||
                 SpansOverlap(
                        bitMap,
                        (uint)GraphicsLayouts.BitMapSize,
                        rasInfo,
                        (uint)GraphicsLayouts.RasInfoSize) ||
                 !TryReadDisplayBitmapLayout(memory, bitMap, out _)))
            {
                return false;
            }

            if (next == rasInfo)
                return false;

            rasInfo = next;
        }

        return rasInfo == 0;
    }

    private static bool ValidatePreallocatedLoadViewResources(
        IGraphicsMemory memory,
        uint viewPort,
        uint rasInfo,
        uint dspIns,
        uint viewLoFCprList,
        uint viewShFCprList)
    {
        if (dspIns == 0 ||
            viewLoFCprList == 0 ||
            viewShFCprList == 0 ||
            rasInfo == 0 ||
            (rasInfo & 1u) != 0 ||
            !TryProbeRange(memory, rasInfo, GraphicsLayouts.RasInfoSize) ||
            !TryProbeRange(memory, dspIns, GraphicsLayouts.CopListSize) ||
            (dspIns & 1u) != 0 ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortColorMap,
                out var colorMap) ||
            colorMap != 0 ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortSprIns,
                out var sprIns) ||
            sprIns != 0 ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortClrIns,
                out var clrIns) ||
            clrIns != 0 ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortUCopIns,
                out var uCopIns) ||
            uCopIns != 0 ||
            !memory.TryReadLong(
                dspIns + (uint)GraphicsLayouts.CopListViewPort,
                out var copListViewPort) ||
            copListViewPort != viewPort ||
            !memory.TryReadLong(
                dspIns + (uint)GraphicsLayouts.CopListCopPtr,
                out var raw) ||
            raw == 0 ||
            (raw & 1u) != 0 ||
            !memory.TryReadLong(
                dspIns + (uint)GraphicsLayouts.CopListCopLStart,
                out var lofStart) ||
            lofStart != raw ||
            !memory.TryReadLong(
                dspIns + (uint)GraphicsLayouts.CopListCopSStart,
                out var shfStart) ||
            shfStart != raw ||
            !memory.TryReadWord(
                dspIns + (uint)GraphicsLayouts.CopListCount,
                out var count) ||
            !memory.TryReadWord(
                dspIns + (uint)GraphicsLayouts.CopListMaxCount,
                out var maxCount) ||
            maxCount == 0)
        {
            return false;
        }

        var generated = count == 12;
        var rawSpan = generated ? 48 : sizeof(ushort) * 2;
        if (!TryAddress(raw, rawSpan - sizeof(ushort), sizeof(ushort), out var rawMaskAddress) ||
            !TryProbeRange(memory, raw, rawSpan))
        {
            return false;
        }

        if (generated)
        {
            if (maxCount < 12 ||
                !ValidateGeneratedLoadViewCopperProgram(memory, viewPort, rasInfo, raw))
            {
                return false;
            }
        }
        else if (count != 0 ||
                 !memory.TryReadWord(raw, out var endWait) ||
                 endWait != GraphicsLayouts.CopperEndWait ||
                 !memory.TryReadWord(rawMaskAddress, out var endMask) ||
                 endMask != GraphicsLayouts.CopperEndMask)
        {
            return false;
        }

        foreach (var cprList in new[] { viewLoFCprList, viewShFCprList })
        {
            if ((cprList & 1u) != 0 ||
                !TryProbeRange(memory, cprList, GraphicsLayouts.CprListSize) ||
                !memory.TryReadLong(
                    cprList + (uint)GraphicsLayouts.CprListNext,
                    out var next) ||
                next != 0 ||
                !memory.TryReadLong(
                    cprList + (uint)GraphicsLayouts.CprListStart,
                    out var start) ||
                start != raw ||
                !memory.TryReadWord(
                    cprList + (uint)GraphicsLayouts.CprListMaxCount,
                    out var capacity) ||
                capacity == 0 ||
                (generated && capacity < 12))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateGeneratedLoadViewCopperProgram(
        IGraphicsMemory memory,
        uint viewPort,
        uint rasInfo,
        uint raw)
    {
        if (!memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDWidth,
                out var width) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDHeight,
                out var height))
        {
            return false;
        }

        var smallPlanar = width == 16 && height == 1;
        var widePlanar = width == 320 && height == 200;
        var palWidePlanar = width == 320 && height == 256;
        var palLoresLacePlanar = width == 320 && height == 512;
        var ntscLoresLacePlanar = width == 320 && height == 400;
        var hiresPlanar = width == 640 && height == 200;
        var palHiresPlanar = width == 640 && height == 256;
        var palHiresLacePlanar = width == 640 && height == 512;
        var ntscHiresLacePlanar = width == 640 && height == 400;
        if (!smallPlanar && !widePlanar && !palWidePlanar &&
            !palLoresLacePlanar && !ntscLoresLacePlanar && !hiresPlanar &&
            !palHiresPlanar && !palHiresLacePlanar && !ntscHiresLacePlanar)
            return false;

        var expected = new ushort[]
        {
            0x008E, 0x2C81,
            0x0090, widePlanar || palWidePlanar || palLoresLacePlanar || ntscLoresLacePlanar || hiresPlanar || palHiresPlanar || palHiresLacePlanar || ntscHiresLacePlanar
                ? (ushort)0xF4C1
                : (ushort)0x2D00,
            0x0092, hiresPlanar || palHiresPlanar || palHiresLacePlanar
                || ntscHiresLacePlanar
                ? (ushort)0x003C
                : (ushort)0x0038,
            0x0094, hiresPlanar || palHiresPlanar || palHiresLacePlanar
                || ntscHiresLacePlanar
                ? (ushort)0x00D4
                : widePlanar || palWidePlanar || palLoresLacePlanar || ntscLoresLacePlanar
                    ? (ushort)0x00D0
                    : (ushort)0x0038,
            0x0108, 0x0000,
            0x010A, 0x0000,
            0x0100, hiresPlanar || palHiresPlanar
                ? (ushort)0x9000
                : palHiresLacePlanar || ntscHiresLacePlanar
                    ? (ushort)0x9004
                    : palLoresLacePlanar || ntscLoresLacePlanar
                    ? (ushort)0x1004
                    : (ushort)0x1000,
            0x0102, 0x0000,
            0x0104, 0x0000,
            0x00E0, 0x0000,
            0x00E2, 0x0000,
            GraphicsLayouts.CopperEndWait, GraphicsLayouts.CopperEndMask
        };

        for (var index = 0; index < expected.Length; index++)
        {
            if (!memory.TryReadWord(raw + (uint)(index * sizeof(ushort)), out var value))
                return false;

            if (index is 19 or 21)
                continue;

            if (value != expected[index])
                return false;
        }

        if ((smallPlanar && (width != 16 || height != 1)) ||
            (widePlanar && (width != 320 || height != 200)) ||
            (palWidePlanar && (width != 320 || height != 256)) ||
            (palLoresLacePlanar && (width != 320 || height != 512)) ||
            (ntscLoresLacePlanar && (width != 320 || height != 400)) ||
            (hiresPlanar && (width != 640 || height != 200)) ||
            (palHiresPlanar && (width != 640 || height != 256)) ||
            (palHiresLacePlanar && (width != 640 || height != 512)) ||
            (ntscHiresLacePlanar && (width != 640 || height != 400)) ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDxOffset,
                out var dxOffset) ||
            dxOffset != 0 ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDyOffset,
                out var dyOffset) ||
            dyOffset != 0 ||
            !memory.TryReadWord(
                viewPort + (uint)GraphicsLayouts.ViewPortModes,
                out var modes) ||
            modes != (palHiresLacePlanar || ntscHiresLacePlanar
                ? (ushort)(GraphicsModeIds.HiresMode | GraphicsModeIds.InterlaceMode)
                : palLoresLacePlanar || ntscLoresLacePlanar
                    ? GraphicsModeIds.InterlaceMode
                    : (ushort)0) ||
            !memory.TryReadByte(
                viewPort + (uint)GraphicsLayouts.ViewPortExtendedModes,
                out var extendedModes) ||
            extendedModes != 0 ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoNext,
                out var next) ||
            next != 0 ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var bitMap) ||
            !TryReadDisplayBitmapLayout(memory, bitMap, out var bitmap) ||
            bitmap.BytesPerRow != (hiresPlanar || palHiresPlanar || palHiresLacePlanar || ntscHiresLacePlanar
                ? 80
                : widePlanar || palWidePlanar || palLoresLacePlanar || ntscLoresLacePlanar
                    ? 40
                    : 2) ||
            bitmap.Rows != (smallPlanar
                ? 1
                : palLoresLacePlanar
                    ? 512
                    : ntscLoresLacePlanar
                        ? 400
                : palWidePlanar || palHiresPlanar
                    ? 256
                    : palHiresLacePlanar
                        ? 512
                        : ntscHiresLacePlanar
                            ? 400
                        : 200) ||
            bitmap.Depth != 1 ||
            (bitmap.Flags & 0x0006) != 0 ||
            !memory.TryReadLong(
                bitMap + (uint)GraphicsLayouts.BitMapPlanes,
                out var plane))
        {
            return false;
        }

        if (!memory.TryReadWord(raw + 38u, out var planeHigh) ||
            !memory.TryReadWord(raw + 42u, out var planeLow))
        {
            return false;
        }

        return plane == ((uint)planeHigh << 16 | planeLow);
    }

    private static bool ValidateView(
        IGraphicsMemory memory,
        uint view,
        bool validateBitMap)
    {
        if ((view & 1u) != 0 ||
            !TryProbeRange(memory, view, GraphicsLayouts.ViewSize) ||
            !memory.TryReadLong(view + (uint)GraphicsLayouts.ViewViewPort, out var viewPort))
        {
            return false;
        }

        if (viewPort == 0)
            return true;

        return ValidateViewPortChain(memory, viewPort, validateBitMap);
    }

    internal static bool InitializeViewPort(IGraphicsMemory memory, uint viewPort)
    {
        if (viewPort == 0 || (viewPort & 1u) != 0 ||
            !TryClear(memory, viewPort, GraphicsLayouts.ViewPortSize, out var original))
            return false;

        if (memory.TryWriteByte(
                viewPort + (uint)GraphicsLayouts.ViewPortSpritePriorities,
                GraphicsLayouts.ViewPortDefaultSpritePriorities))
        {
            return true;
        }

        Restore(memory, viewPort, original);
        return false;
    }

    /// <summary>
    /// Releases the guest-visible intermediate and user copper-list links
    /// owned by a ViewPort.  Allocation and hardware-list storage remain an
    /// explicit CopperStart/native backend responsibility; the public
    /// graphics.library contract requires these four fields to be NULL after
    /// teardown.
    /// </summary>
    internal static int FreeViewPortCopLists(
        IGraphicsMemory memory,
        uint viewPort)
    {
        // Native FreeVPortCopLists resolves the optional ViewPortExtra with
        // GfxLookUp() and therefore reduces a NULL pointer to a successful
        // no-op.  It must not be turned into a synthetic memory access (or a
        // provider callback) by the portable path.
        if (viewPort == 0)
            return Success;

        if ((viewPort & 1u) != 0)
            return Failure;

        var links = new[]
        {
            GraphicsLayouts.ViewPortDspIns,
            GraphicsLayouts.ViewPortSprIns,
            GraphicsLayouts.ViewPortClrIns,
            GraphicsLayouts.ViewPortUCopIns
        };

        // The link fields are addressed as 68000 LONGs.  Reject a base whose
        // final link would cross the 32-bit guest address envelope before
        // adding the offsets below; unchecked uint arithmetic would otherwise
        // wrap a malformed high-memory ViewPort into unrelated low memory.
        var finalLinkEnd = (ulong)viewPort +
            (uint)(GraphicsLayouts.ViewPortUCopIns + sizeof(uint) - 1);
        if (finalLinkEnd > uint.MaxValue)
            return Failure;

        // FreeVPortCopLists only consumes and clears these four public copper
        // links.  Do not snapshot the unrelated ViewPort header: a native or
        // provider-owned caller may have a sparse envelope where, for
        // example, DWidth/DHeight is unreadable while the teardown links are
        // still writable.  Reading only the fields we publish preserves the
        // ownership boundary and still gives the portable path an atomic
        // rollback if a later link write faults.
        var original = new byte[links.Length][];
        for (var index = 0; index < links.Length; index++)
        {
            if (!TrySnapshot(
                    memory,
                    viewPort + (uint)links[index],
                    sizeof(uint),
                    out original[index]) ||
                !memory.TryReadLong(
                    viewPort + (uint)links[index],
                    out _))
            {
                return Failure;
            }
        }

        foreach (var offset in links)
        {
            if (!memory.TryWriteLong(viewPort + (uint)offset, 0))
            {
                for (var index = 0; index < links.Length; index++)
                {
                    Restore(
                        memory,
                        viewPort + (uint)links[index],
                        original[index]);
                }

                return Failure;
            }
        }

        return Success;
    }

    /// <summary>
    /// Clears a caller-owned RasInfo chain node before it is linked from a
    /// viewport.  RasInfo has no public InitRasInfo vector, but Intuition and
    /// screen construction use the same documented zero-initialized layout.
    /// Keeping this operation in the portable layer avoids host references in
    /// guest chains and gives native 68k callers the same guarded behavior.
    /// </summary>
    internal static bool InitializeRasInfo(IGraphicsMemory memory, uint rasInfo)
        => rasInfo != 0 && (rasInfo & 1u) == 0 &&
           TryClear(memory, rasInfo, GraphicsLayouts.RasInfoSize, out _);

    /// <summary>Initializes the caller-owned temporary raster descriptor.</summary>
    internal static bool InitializeTmpRas(
        IGraphicsMemory memory,
        uint tmpRas,
        uint buffer,
        uint byteCount)
    {
        if (tmpRas == 0 || (tmpRas & 1u) != 0 ||
            !TryProbeRange(memory, tmpRas, GraphicsLayouts.TmpRasSize))
        {
            return false;
        }

        // InitTmpRas only publishes the caller-owned descriptor.  The
        // resident vector stores the pointer and byte count without touching
        // the backing span; AreaEnd/Flood/Text perform their own capacity and
        // readability checks when they consume the descriptor.  Keeping that
        // split is important for native/provider ownership of sparse or
        // temporarily unmapped scratch storage, including the empty form.

        if (!TrySnapshot(memory, tmpRas, GraphicsLayouts.TmpRasSize, out var original))
            return false;

        if (memory.TryWriteLong(
                tmpRas + (uint)GraphicsLayouts.TmpRasRasPtr,
                buffer) &&
            memory.TryWriteLong(
                tmpRas + (uint)GraphicsLayouts.TmpRasByteCount,
                byteCount))
        {
            return true;
        }

        Restore(memory, tmpRas, original);
        return false;
    }

    internal static bool InitializeBitMap(
        IGraphicsMemory memory,
        uint bitMap,
        byte depth,
        ushort width,
        ushort height)
    {
        if (bitMap == 0 || (bitMap & 1u) != 0)
            return false;

        // Standard OCS/ECS BitMaps expose at most the eight public plane
        // links in the guest structure.  Keep malformed depths outside that
        // portable planar contract unclaimed before publishing the geometry;
        // an AGA/RTG provider can own wider surfaces.
        if (depth == 0 || depth > 8)
            return false;

        // Kickstart's InitBitMap writes only the eight-byte geometry/header
        // prefix.  The caller owns Planes[], and the constructor must neither
        // read nor probe those links; bitmap consumers validate the selected
        // plane spans when they actually use the descriptor.
        if (!TrySnapshot(memory, bitMap, GraphicsLayouts.BitMapPlanes, out var original))
            return false;

        var bytesPerRow = (uint)((width + 15u) >> 4) * 2u;
        if (bytesPerRow > ushort.MaxValue ||
            !memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, (ushort)bytesPerRow) ||
            !memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, height) ||
            !memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapFlags, (byte)BmfStandard) ||
            !memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, depth) ||
            !memory.TryWriteWord(bitMap + 6u, 0))
        {
            Restore(memory, bitMap, original);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Initializes one bounded, caller-owned classic screen resource chain.
    /// This is the graphics-side construction contract used by a future
    /// native OpenScreen owner: no guest allocation or provider callback is
    /// performed here, and the caller retains ownership of the plane span.
    /// The four public envelopes are snapshotted before publication so a
    /// late link/write failure restores the complete pre-call state.
    /// </summary>
    internal static bool InitializeScreenResourceChain(
        IGraphicsMemory memory,
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        byte depth,
        ushort width,
        ushort height,
        uint plane0)
    {
        // Preserve the original one-plane ABI while routing the actual
        // publication through the explicit plane-table form.  A caller that
        // supplies depth > 1 must opt into the table contract below rather
        // than silently publishing a partially initialized BitMap.
        return InitializeScreenResourceChainPlanes(
            memory,
            view,
            viewPort,
            rasInfo,
            bitMap,
            depth,
            width,
            height,
            new[] { plane0 });
    }

    /// <summary>
    /// Initializes a caller-owned classic screen resource chain with one
    /// plane link for every declared BitMap depth.  This is the first
    /// multi-plane lifecycle unit: it publishes only standard planar
    /// View/ViewPort/RasInfo/BitMap topology, performs no allocation, and
    /// never claims or clears the supplied plane spans.
    /// </summary>
    internal static bool InitializeScreenResourceChainPlanes(
        IGraphicsMemory memory,
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        byte depth,
        ushort width,
        ushort height,
        IReadOnlyList<uint> planes)
    {
        if (width == 0 || height == 0)
            return false;

        // Plane storage remains caller-owned, but its byte span must not
        // alias any public View/ViewPort/RasInfo/BitMap envelope.  Otherwise
        // a later structure initialization or teardown could overwrite the
        // caller's raster while the links still look structurally valid.
        if (!TryGetScreenPlaneBytes(width, height, out var planeBytes) ||
            !TryValidateScreenPlaneSpans(
                view,
                viewPort,
                rasInfo,
                bitMap,
                planes,
                planeBytes))
        {
            return false;
        }

        if (!TryValidateScreenResourceBasesPlanes(
            memory,
            view,
            viewPort,
            rasInfo,
            bitMap,
            depth,
            planes,
            out var viewOriginal,
            out var viewPortOriginal,
            out var rasInfoOriginal,
            out var bitMapOriginal))
        {
            return false;
        }

        bool RestoreAll()
        {
            Restore(memory, view, viewOriginal);
            Restore(memory, viewPort, viewPortOriginal);
            Restore(memory, rasInfo, rasInfoOriginal);
            Restore(memory, bitMap, bitMapOriginal);
            return false;
        }

        if (!InitializeView(memory, view) ||
            !InitializeViewPort(memory, viewPort) ||
            !InitializeRasInfo(memory, rasInfo) ||
            !InitializeBitMap(memory, bitMap, depth, width, height) ||
            !memory.TryWriteWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDWidth,
                width) ||
            !memory.TryWriteWord(
                viewPort + (uint)GraphicsLayouts.ViewPortDHeight,
                height) ||
            !memory.TryWriteLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                bitMap) ||
            !memory.TryWriteLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                rasInfo) ||
            !memory.TryWriteLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                viewPort))
        {
            return RestoreAll();
        }

        for (var plane = 0; plane < planes.Count; plane++)
        {
            if (!memory.TryWriteLong(
                    bitMap + (uint)GraphicsLayouts.BitMapPlanes +
                    ((uint)plane * sizeof(uint)),
                    planes[plane]))
            {
                return RestoreAll();
            }
        }

        return true;
    }

    private static bool TryGetScreenPlaneBytes(
        ushort width,
        ushort height,
        out uint bytes)
    {
        bytes = 0;
        var wordsPerRow = ((ulong)width + 15UL) >> 4;
        var total = wordsPerRow * 2UL * height;
        if (total == 0 || total > uint.MaxValue)
            return false;
        bytes = (uint)total;
        return true;
    }

    private static bool TryValidateScreenPlaneSpans(
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        IReadOnlyList<uint>? planes,
        uint planeBytes)
    {
        if (planes is null || planeBytes == 0)
            return false;

        // This caller-owned path never clears or copies plane bytes, so two
        // supplied, non-identical plane links may intentionally share
        // storage. Exact duplicate links remain malformed in the structural
        // validator below. The forbidden alias is with the public structure
        // envelopes that this initializer owns and rewrites.
        for (var index = 0; index < planes.Count; index++)
        {
            var plane = planes[index];
            if (!TryGuestSpan(plane, planeBytes) ||
                SpansOverlap(plane, planeBytes, view,
                    (uint)GraphicsLayouts.ViewSize) ||
                SpansOverlap(plane, planeBytes, viewPort,
                    (uint)GraphicsLayouts.ViewPortSize) ||
                SpansOverlap(plane, planeBytes, rasInfo,
                    (uint)GraphicsLayouts.RasInfoSize) ||
                SpansOverlap(plane, planeBytes, bitMap,
                    (uint)GraphicsLayouts.BitMapSize))
            {
                return false;
            }

        }

        return true;
    }

    private static bool TryGuestSpan(uint address, uint bytes)
        => address != 0 && bytes != 0 &&
           address <= uint.MaxValue - (bytes - 1u);

    private static bool SpansOverlap(
        uint leftAddress,
        uint leftBytes,
        uint rightAddress,
        uint rightBytes)
    {
        if (leftBytes == 0 || rightBytes == 0)
            return false;
        var leftEnd = (ulong)leftAddress + leftBytes;
        var rightEnd = (ulong)rightAddress + rightBytes;
        return (ulong)leftAddress < rightEnd &&
               (ulong)rightAddress < leftEnd;
    }

    /// <summary>
    /// Tears down a screen resource chain previously published by
    /// <see cref="InitializeScreenResourceChain"/>.  The public structures
    /// are cleared, but the caller-owned plane bytes are never touched.
    /// Link identity is checked before mutation so a provider/native
    /// replacement remains outside this ownership boundary.
    /// </summary>
    internal static bool TeardownScreenResourceChain(
        IGraphicsMemory memory,
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap)
    {
        if (!TryValidateScreenResourceBases(
                memory,
                view,
                viewPort,
                rasInfo,
                bitMap,
                depth: 1,
                plane0: 0,
                out var viewOriginal,
                out var viewPortOriginal,
                out var rasInfoOriginal,
                out var bitMapOriginal,
                requirePlane: false))
        {
            return false;
        }

        if (!memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                out var linkedViewPort) ||
            linkedViewPort != viewPort ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var linkedRasInfo) ||
            linkedRasInfo != rasInfo ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var linkedBitMap) ||
            linkedBitMap != bitMap)
        {
            return false;
        }

        bool RestoreAll()
        {
            Restore(memory, view, viewOriginal);
            Restore(memory, viewPort, viewPortOriginal);
            Restore(memory, rasInfo, rasInfoOriginal);
            Restore(memory, bitMap, bitMapOriginal);
            return false;
        }

        if (!TryClear(memory, view, GraphicsLayouts.ViewSize, out _) ||
            !TryClear(memory, viewPort, GraphicsLayouts.ViewPortSize, out _) ||
            !TryClear(memory, rasInfo, GraphicsLayouts.RasInfoSize, out _) ||
            !TryClear(memory, bitMap, GraphicsLayouts.BitMapSize, out _))
        {
            return RestoreAll();
        }

        return true;
    }

    /// <summary>
    /// Tears down a caller-owned multi-plane chain only when every published
    /// plane link still has the exact identity supplied at construction.
    /// The plane bytes remain untouched; a replaced link declines before any
    /// public envelope is mutated.
    /// </summary>
    internal static bool TeardownScreenResourceChainPlanes(
        IGraphicsMemory memory,
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        IReadOnlyList<uint> planes)
    {
        if (planes is null || planes.Count > byte.MaxValue)
            return false;

        if (!TryValidateScreenResourceBasesPlanes(
                memory,
                view,
                viewPort,
                rasInfo,
                bitMap,
                depth: (byte)planes.Count,
                planes,
                out var viewOriginal,
                out var viewPortOriginal,
                out var rasInfoOriginal,
                out var bitMapOriginal))
        {
            return false;
        }

        if (!memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                out var linkedViewPort) ||
            linkedViewPort != viewPort ||
            !memory.TryReadLong(
                viewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var linkedRasInfo) ||
            linkedRasInfo != rasInfo ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var linkedBitMap) ||
            linkedBitMap != bitMap)
        {
            return false;
        }

        for (var plane = 0; plane < planes.Count; plane++)
        {
            if (!memory.TryReadLong(
                    bitMap + (uint)GraphicsLayouts.BitMapPlanes +
                    ((uint)plane * sizeof(uint)),
                    out var linkedPlane) ||
                linkedPlane != planes[plane])
            {
                return false;
            }
        }

        bool RestoreAll()
        {
            Restore(memory, view, viewOriginal);
            Restore(memory, viewPort, viewPortOriginal);
            Restore(memory, rasInfo, rasInfoOriginal);
            Restore(memory, bitMap, bitMapOriginal);
            return false;
        }

        if (!TryClear(memory, view, GraphicsLayouts.ViewSize, out _) ||
            !TryClear(memory, viewPort, GraphicsLayouts.ViewPortSize, out _) ||
            !TryClear(memory, rasInfo, GraphicsLayouts.RasInfoSize, out _) ||
            !TryClear(memory, bitMap, GraphicsLayouts.BitMapSize, out _))
        {
            return RestoreAll();
        }

        return true;
    }

    private static bool TryValidateScreenResourceBases(
        IGraphicsMemory memory,
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        byte depth,
        uint plane0,
        out byte[] viewOriginal,
        out byte[] viewPortOriginal,
        out byte[] rasInfoOriginal,
        out byte[] bitMapOriginal,
        bool requirePlane = true)
    {
        viewOriginal = Array.Empty<byte>();
        viewPortOriginal = Array.Empty<byte>();
        rasInfoOriginal = Array.Empty<byte>();
        bitMapOriginal = Array.Empty<byte>();
        // This first screen unit deliberately owns one standard-planar
        // playfield. Multi-plane publication will add an explicit plane
        // table/capacity contract rather than silently leaving depth>1 links
        // caller-owned.
        if (view == 0 || viewPort == 0 || rasInfo == 0 || bitMap == 0 ||
            (view & 1u) != 0 || (viewPort & 1u) != 0 ||
            (rasInfo & 1u) != 0 || (bitMap & 1u) != 0 ||
            (requirePlane && (plane0 == 0 || (plane0 & 1u) != 0)) ||
            depth != 1)
        {
            return false;
        }

        if (!TryValidateScreenResourceEnvelopeSpans(
                view,
                viewPort,
                rasInfo,
                bitMap))
        {
            return false;
        }

        return TrySnapshot(memory, view, GraphicsLayouts.ViewSize, out viewOriginal) &&
            TrySnapshot(memory, viewPort, GraphicsLayouts.ViewPortSize, out viewPortOriginal) &&
            TrySnapshot(memory, rasInfo, GraphicsLayouts.RasInfoSize, out rasInfoOriginal) &&
            TrySnapshot(memory, bitMap, GraphicsLayouts.BitMapSize, out bitMapOriginal);
    }

    private static bool TryValidateScreenResourceBasesPlanes(
        IGraphicsMemory memory,
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap,
        byte depth,
        IReadOnlyList<uint>? planes,
        out byte[] viewOriginal,
        out byte[] viewPortOriginal,
        out byte[] rasInfoOriginal,
        out byte[] bitMapOriginal)
    {
        viewOriginal = Array.Empty<byte>();
        viewPortOriginal = Array.Empty<byte>();
        rasInfoOriginal = Array.Empty<byte>();
        bitMapOriginal = Array.Empty<byte>();
        if (memory is null || view == 0 || viewPort == 0 ||
            rasInfo == 0 || bitMap == 0 || (view & 1u) != 0 ||
            (viewPort & 1u) != 0 || (rasInfo & 1u) != 0 ||
            (bitMap & 1u) != 0 || depth == 0 || depth > 8 ||
            planes is null || planes.Count != depth)
        {
            return false;
        }

        if (!TryValidateScreenResourceEnvelopeSpans(
                view,
                viewPort,
                rasInfo,
                bitMap))
        {
            return false;
        }

        var structures = new[] { view, viewPort, rasInfo, bitMap };
        for (var index = 0; index < planes.Count; index++)
        {
            var plane = planes[index];
            if (plane == 0 || (plane & 1u) != 0 ||
                Array.IndexOf(structures, plane) >= 0)
            {
                return false;
            }

            for (var prior = 0; prior < index; prior++)
            {
                if (planes[prior] == plane)
                    return false;
            }
        }

        return TrySnapshot(memory, view, GraphicsLayouts.ViewSize, out viewOriginal) &&
            TrySnapshot(memory, viewPort, GraphicsLayouts.ViewPortSize, out viewPortOriginal) &&
            TrySnapshot(memory, rasInfo, GraphicsLayouts.RasInfoSize, out rasInfoOriginal) &&
            TrySnapshot(memory, bitMap, GraphicsLayouts.BitMapSize, out bitMapOriginal);
    }

    private static bool TryValidateScreenResourceEnvelopeSpans(
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap)
    {
        var envelopes = new[]
        {
            (Address: view, Bytes: (uint)GraphicsLayouts.ViewSize),
            (Address: viewPort, Bytes: (uint)GraphicsLayouts.ViewPortSize),
            (Address: rasInfo, Bytes: (uint)GraphicsLayouts.RasInfoSize),
            (Address: bitMap, Bytes: (uint)GraphicsLayouts.BitMapSize)
        };

        for (var index = 0; index < envelopes.Length; index++)
        {
            var current = envelopes[index];
            if (!TryGuestSpan(current.Address, current.Bytes))
                return false;

            for (var prior = 0; prior < index; prior++)
            {
                var previous = envelopes[prior];
                if (SpansOverlap(
                        current.Address,
                        current.Bytes,
                        previous.Address,
                        previous.Bytes))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Initializes the classic AreaInfo vector collector.  Amiga graphics
    /// stores each vertex as an X/Y word pair and keeps one byte per vertex in
    /// a flag table placed immediately after the 4-byte coordinate table.
    /// The caller therefore supplies at least <c>5 * maxVectors</c> bytes.
    /// </summary>
    internal static bool InitializeArea(
        IGraphicsMemory memory,
        uint areaInfo,
        uint buffer,
        short maxVectors)
    {
        // AreaInfo contains word and long fields and is consumed through
        // native 68k word accesses.  Keep an odd descriptor available to a
        // native/provider owner instead of byte-processing it on the host
        // path (a real 68000 would take an address error).  InitArea itself
        // only publishes the descriptor fields; it does not dereference the
        // caller-owned vector/flag buffer.  AreaMove/AreaDraw/AreaEllipse
        // retain the operation-specific storage probes when consuming it.
        if (areaInfo == 0 || (areaInfo & 1u) != 0 ||
            !TryProbeRange(memory, areaInfo, GraphicsLayouts.AreaInfoSize) ||
            maxVectors < 0)
            return false;

        var vectorCount = (uint)maxVectors;
        var flagTable = (ulong)buffer + ((ulong)vectorCount * 4u);
        var bufferEnd = flagTable + vectorCount;
        var requiredBytes = (ulong)vectorCount * 5u;
        // `bufferEnd` is an exclusive address.  A vector/flag buffer whose
        // final byte is $FFFFFFFF is still a valid guest envelope; only an
        // exclusive end beyond the 32-bit address space must decline.  Keep
        // the actual flag-table pointer itself inclusive and word-addressed,
        // while allowing the exact one-past-the-end sentinel here.
        if (flagTable > uint.MaxValue ||
            bufferEnd > (1UL << 32) ||
            requiredBytes > uint.MaxValue)
            return false;

        if (!TrySnapshot(memory, areaInfo, GraphicsLayouts.AreaInfoSize, out var original))
            return false;

        var initialized =
            memory.TryWriteLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable,
                buffer) &&
            memory.TryWriteLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
                buffer) &&
            memory.TryWriteLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFlagTable,
                (uint)flagTable) &&
            memory.TryWriteLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer,
                (uint)flagTable) &&
            memory.TryWriteWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                0) &&
            memory.TryWriteWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoMaxCount,
                (ushort)maxVectors) &&
            memory.TryWriteWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFirstX,
                0) &&
            memory.TryWriteWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFirstY,
                0);
        if (initialized)
            return true;

        Restore(memory, areaInfo, original);
        return false;
    }

    /// <summary>Records the first point of a new area polygon.</summary>
    internal static int AreaMove(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        AreaScratch? scratch = null,
        bool allowLayered = false)
    {
        if ((!allowLayered && !TryRequireUnlayeredRastPort(memory, rastPort)) ||
            !TryReadAreaState(memory, rastPort, out var state))
            return Failure;

        // Kickstart first reserves one vector for the new move.  Closing a
        // DRAW-terminated polygon can consume one more vector, so the
        // post-close capacity is checked separately below.  A pending MOVE
        // is replaced in place and consumes no additional slot.
        if (state.Count >= state.MaxCount)
            return Failure;

        var replacesPendingMove = state.Count != 0 && state.LastFlag == AreaMoveFlag;
        var closesPolygon = state.Count != 0 &&
            !replacesPendingMove &&
            state.LastFlag == AreaDrawFlag;
        var needsClosingVector = false;
        if (closesPolygon)
        {
            if (!TryReadAreaPoint(memory, state.VectorTable, state.Count - 1u, out var lastPoint))
                return Failure;

            needsClosingVector = lastPoint.X != state.FirstX || lastPoint.Y != state.FirstY;
        }

        var pointsToAdd = state.Count == 0
            ? 1u
            : replacesPendingMove
                ? 0u
                : needsClosingVector
                    ? 2u
                    : 1u;
        if (pointsToAdd > (uint)state.MaxCount - state.Count)
            return Failure;

        var vectorBase = state.VectorPointer;
        var flagBase = state.FlagPointer;
        if (replacesPendingMove)
        {
            if (state.VectorPointer < 4u || state.FlagPointer == 0)
                return Failure;

            vectorBase -= 4u;
            flagBase--;
        }

        var closingVectorPointer = state.VectorPointer;
        var closingFlagPointer = state.FlagPointer;
        uint moveVectorPointer;
        uint moveFlagPointer;
        if (replacesPendingMove)
        {
            moveVectorPointer = vectorBase;
            moveFlagPointer = flagBase;
        }
        else if (needsClosingVector)
        {
            if (!TryAddress(state.VectorPointer, 4, 4, out moveVectorPointer) ||
                !TryAddress(state.FlagPointer, 1, 1, out moveFlagPointer))
            {
                return Failure;
            }
        }
        else
        {
            moveVectorPointer = state.VectorPointer;
            moveFlagPointer = state.FlagPointer;
        }

        if ((needsClosingVector &&
             (!TryAddress(state.VectorPointer, 4, 4, out _) ||
              !TryAddress(state.FlagPointer, 1, 1, out _) ||
              !TryProbeAreaVector(memory, closingVectorPointer, closingFlagPointer))) ||
            (closesPolygon && !needsClosingVector &&
             (state.FlagPointer == 0 ||
              !memory.TryReadByte(state.FlagPointer - 1u, out _))) ||
            !TryProbeAreaVector(memory, moveVectorPointer, moveFlagPointer) ||
            !TryAddress(
                state.VectorPointer,
                checked((int)(pointsToAdd * 4u)),
                1,
                out var nextVectorPointer) ||
            !TryAddress(
                state.FlagPointer,
                checked((int)pointsToAdd),
                1,
                out var nextFlagPointer))
        {
            return Failure;
        }

        // The pointer advance is relative to the original current pointers;
        // the replacement case has zero added vectors and therefore keeps
        // them unchanged.
        if (!TryProbeAreaStateUpdate(memory, state))
        {
            return Failure;
        }

        byte[] closingVectorOriginal = Array.Empty<byte>();
        byte closingFlagOriginal = 0;
        byte previousFlagOriginal = 0;
        if (!TrySnapshotAreaStateUpdate(
                memory,
                state,
                out var stateOriginal,
                scratch?._stateOriginal) ||
            (needsClosingVector &&
             !TrySnapshotAreaVector(
                 memory,
                 closingVectorPointer,
                 closingFlagPointer,
                 out closingVectorOriginal,
                 out closingFlagOriginal,
                 scratch?._vectorOriginal0)) ||
            (closesPolygon && !needsClosingVector &&
             !memory.TryReadByte(state.FlagPointer - 1u, out previousFlagOriginal)) ||
            !TrySnapshotAreaVector(
                memory,
                 moveVectorPointer,
                 moveFlagPointer,
                 out var newVectorOriginal,
                 out var newFlagOriginal,
                 scratch?._vectorOriginal1))
        {
            return Failure;
        }

        if (needsClosingVector &&
            !WriteAreaVector(
                memory,
                state.VectorPointer,
                state.FlagPointer,
                state.FirstX,
                state.FirstY,
                AreaCloseDrawFlag,
                scratch?._vectorOriginal2))
        {
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        if (closesPolygon && !needsClosingVector &&
            !memory.TryWriteByte(state.FlagPointer - 1u, AreaCloseDrawFlag))
        {
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        if (!WriteAreaVector(
                memory,
                moveVectorPointer,
                moveFlagPointer,
                x,
                y,
                AreaMoveFlag,
                scratch?._vectorOriginal3))
        {
            if (needsClosingVector)
            {
                RestoreAreaVector(
                    memory,
                    closingVectorPointer,
                    closingFlagPointer,
                    closingVectorOriginal,
                    closingFlagOriginal);
            }
            else if (closesPolygon)
            {
                _ = memory.TryWriteByte(state.FlagPointer - 1u, previousFlagOriginal);
            }
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        var newCount = state.Count + pointsToAdd;
        if (memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
                nextVectorPointer) &&
            memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer,
                nextFlagPointer) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                (ushort)newCount) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFirstX,
                unchecked((ushort)x)) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFirstY,
                unchecked((ushort)y)))
        {
            return Success;
        }

        if (needsClosingVector)
        {
            RestoreAreaVector(
                memory,
                closingVectorPointer,
                closingFlagPointer,
                closingVectorOriginal,
                closingFlagOriginal);
        }
        else if (closesPolygon)
        {
            _ = memory.TryWriteByte(state.FlagPointer - 1u, previousFlagOriginal);
        }
        RestoreAreaVector(
            memory,
            moveVectorPointer,
            moveFlagPointer,
            newVectorOriginal,
            newFlagOriginal);
        RestoreAreaStateUpdate(memory, state, stateOriginal);
        return Failure;
    }

    /// <summary>Records one additional point in the current area polygon.</summary>
    internal static int AreaDraw(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        AreaScratch? scratch = null,
        bool allowLayered = false)
    {
        if ((!allowLayered && !TryRequireUnlayeredRastPort(memory, rastPort)) ||
            !TryReadAreaState(memory, rastPort, out var state) ||
            state.Count >= state.MaxCount ||
            !TryProbeAreaStateUpdate(memory, state) ||
            !TryProbeAreaVector(memory, state.VectorPointer, state.FlagPointer) ||
            !TrySnapshotAreaStateUpdate(
                memory,
                state,
                out var stateOriginal,
                scratch?._stateOriginal) ||
            !TrySnapshotAreaVector(
                memory,
                state.VectorPointer,
                state.FlagPointer,
                out var vectorOriginal,
                out var flagOriginal,
                scratch?._vectorOriginal0))
        {
            return Failure;
        }

        if (!WriteAreaVector(
                memory,
                state.VectorPointer,
                state.FlagPointer,
                x,
                y,
                AreaDrawFlag,
                scratch?._vectorOriginal1))
        {
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        if (!TryAddress(state.VectorPointer, 4, 1, out var nextVectorPointer) ||
            !TryAddress(state.FlagPointer, 1, 1, out var nextFlagPointer))
        {
            RestoreAreaVector(
                memory,
                state.VectorPointer,
                state.FlagPointer,
                vectorOriginal,
                flagOriginal);
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        var newCount = state.Count + 1u;
        if (memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
                nextVectorPointer) &&
            memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer,
                nextFlagPointer) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                (ushort)newCount))
        {
            return Success;
        }

        RestoreAreaVector(
            memory,
            state.VectorPointer,
            state.FlagPointer,
            vectorOriginal,
            flagOriginal);
        RestoreAreaStateUpdate(memory, state, stateOriginal);
        return Failure;
    }

    /// <summary>
    /// Records an ellipse in the AreaInfo vector stream. Kickstart reserves
    /// two vectors for each AreaEllipse call; the first stores the center and
    /// the second stores the positive horizontal/vertical radii. Rendering is
    /// deferred to AreaEnd so callers can combine ellipses and polygons.
    /// </summary>
    internal static int AreaEllipse(
        IGraphicsMemory memory,
        uint rastPort,
        short centerX,
        short centerY,
        short radiusX,
        short radiusY,
        AreaScratch? scratch = null,
        bool allowLayered = false)
    {
        if (radiusX <= 0 || radiusY <= 0 ||
            (!allowLayered && !TryRequireUnlayeredRastPort(memory, rastPort)) ||
            !TryReadAreaState(memory, rastPort, out var state))
        {
            return Failure;
        }

        // Kickstart checks for the two-vector ellipse reservation before it
        // performs the pending-move replacement below.  That ordering is
        // guest-visible when an existing ellipse plus a trailing AreaMove has
        // only one free slot: the call fails instead of using the replacement
        // to squeeze the ellipse into the remaining capacity.
        if (state.MaxCount < 2 ||
            state.Count > state.MaxCount ||
            state.Count > (uint)state.MaxCount - 2u)
        {
            return Failure;
        }

        // A non-closed polygon is explicitly closed before the ellipse is
        // appended.  The native collector may either append the first point
        // with CLOSEDRAW or just retag an already matching final point.  A
        // pending MOVE is erased after this close step and replaced by the
        // ellipse's center vector.
        var replacesPendingMove = state.Count != 0 && state.LastFlag == AreaMoveFlag;
        var closesPolygon = state.Count != 0 &&
            !replacesPendingMove &&
            state.LastFlag == AreaDrawFlag;
        var needsClosingVector = false;
        if (closesPolygon)
        {
            if (!TryReadAreaPoint(memory, state.VectorTable, state.Count - 1u, out var lastPoint))
                return Failure;

            needsClosingVector = lastPoint.X != state.FirstX || lastPoint.Y != state.FirstY;
        }

        var effectiveCount = (uint)state.Count + (needsClosingVector ? 1u : 0u);
        if (replacesPendingMove)
            effectiveCount--;
        if (effectiveCount > state.MaxCount ||
            effectiveCount > (uint)state.MaxCount - 2u)
        {
            return Failure;
        }

        uint centerVectorPointer;
        uint centerFlagPointer;
        if (replacesPendingMove)
        {
            if (state.VectorPointer < 4u || state.FlagPointer == 0)
                return Failure;

            centerVectorPointer = state.VectorPointer - 4u;
            centerFlagPointer = state.FlagPointer - 1u;
        }
        else if (needsClosingVector)
        {
            if (!TryAddress(state.VectorPointer, 4, 4, out centerVectorPointer) ||
                !TryAddress(state.FlagPointer, 1, 1, out centerFlagPointer))
            {
                return Failure;
            }
        }
        else
        {
            centerVectorPointer = state.VectorPointer;
            centerFlagPointer = state.FlagPointer;
        }

        if (!TryAddress(centerVectorPointer, 4, 4, out var radiusVectorPointer) ||
            !TryAddress(centerFlagPointer, 1, 1, out var radiusFlagPointer) ||
            !TryAddress(centerVectorPointer, 8, 1, out var nextVectorPointer) ||
            !TryAddress(centerFlagPointer, 2, 1, out var nextFlagPointer) ||
            !TryProbeAreaStateUpdate(memory, state) ||
            (needsClosingVector &&
             !TryProbeAreaVector(memory, state.VectorPointer, state.FlagPointer)) ||
            (closesPolygon && !needsClosingVector &&
             (state.FlagPointer == 0 ||
              !memory.TryReadByte(state.FlagPointer - 1u, out _))) ||
            !TryProbeAreaVector(memory, centerVectorPointer, centerFlagPointer) ||
            !TryProbeAreaVector(memory, radiusVectorPointer, radiusFlagPointer))
        {
            return Failure;
        }

        byte[] closingVectorOriginal = Array.Empty<byte>();
        byte closingFlagOriginal = 0;
        byte previousFlagOriginal = 0;
        if (!TrySnapshotAreaStateUpdate(
                memory,
                state,
                out var stateOriginal,
                scratch?._stateOriginal) ||
            (needsClosingVector &&
             !TrySnapshotAreaVector(
                 memory,
                 state.VectorPointer,
                 state.FlagPointer,
                 out closingVectorOriginal,
                 out closingFlagOriginal,
                 scratch?._vectorOriginal0)) ||
            (closesPolygon && !needsClosingVector &&
             !memory.TryReadByte(state.FlagPointer - 1u, out previousFlagOriginal)) ||
            !TrySnapshotAreaVector(
                memory,
                centerVectorPointer,
                centerFlagPointer,
                out var centerVectorOriginal,
                out var centerFlagOriginal,
                scratch?._vectorOriginal1) ||
            !TrySnapshotAreaVector(
                memory,
                radiusVectorPointer,
                radiusFlagPointer,
                out var radiusVectorOriginal,
                out var radiusFlagOriginal,
                scratch?._vectorOriginal2))
        {
            return Failure;
        }

        if (needsClosingVector &&
            !WriteAreaVector(
                memory,
                state.VectorPointer,
                state.FlagPointer,
                state.FirstX,
                state.FirstY,
                AreaCloseDrawFlag,
                scratch?._vectorOriginal3))
        {
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        if (closesPolygon && !needsClosingVector &&
            !memory.TryWriteByte(state.FlagPointer - 1u, AreaCloseDrawFlag))
        {
            if (needsClosingVector)
            {
                RestoreAreaVector(
                    memory,
                    state.VectorPointer,
                    state.FlagPointer,
                    closingVectorOriginal,
                    closingFlagOriginal);
            }
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        if (!WriteAreaVector(
                memory,
                centerVectorPointer,
                centerFlagPointer,
                centerX,
                centerY,
                AreaEllipseFlag,
                scratch?._vectorOriginal4) ||
            !WriteAreaVector(
                memory,
                radiusVectorPointer,
                radiusFlagPointer,
                radiusX,
                radiusY,
                AreaEllipseFlag,
                scratch?._vectorOriginal5))
        {
            if (needsClosingVector)
            {
                RestoreAreaVector(
                    memory,
                    state.VectorPointer,
                    state.FlagPointer,
                    closingVectorOriginal,
                    closingFlagOriginal);
            }
            else if (closesPolygon)
            {
                _ = memory.TryWriteByte(state.FlagPointer - 1u, previousFlagOriginal);
            }
            RestoreAreaVector(
                memory,
                centerVectorPointer,
                centerFlagPointer,
                centerVectorOriginal,
                centerFlagOriginal);
            RestoreAreaStateUpdate(memory, state, stateOriginal);
            return Failure;
        }

        var newCount = effectiveCount + 2u;
        if (memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
                nextVectorPointer) &&
            memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer,
                nextFlagPointer) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                (ushort)newCount))
        {
            return Success;
        }

        if (needsClosingVector)
        {
            RestoreAreaVector(
                memory,
                state.VectorPointer,
                state.FlagPointer,
                closingVectorOriginal,
                closingFlagOriginal);
        }
        else if (closesPolygon)
        {
            _ = memory.TryWriteByte(state.FlagPointer - 1u, previousFlagOriginal);
        }
        RestoreAreaVector(
            memory,
            centerVectorPointer,
            centerFlagPointer,
            centerVectorOriginal,
            centerFlagOriginal);
        RestoreAreaVector(
            memory,
            radiusVectorPointer,
            radiusFlagPointer,
            radiusVectorOriginal,
            radiusFlagOriginal);
        RestoreAreaStateUpdate(memory, state, stateOriginal);
        return Failure;
    }

    /// <summary>
    /// Implements the classic AreaCircle graphics macro as a portable helper.
    /// The macro expands to AreaEllipse with equal positive radii, so it uses
    /// the same two-vector collector representation and rollback boundary.
    /// </summary>
    internal static int AreaCircle(
        IGraphicsMemory memory,
        uint rastPort,
        short centerX,
        short centerY,
        ushort radius)
    {
        if (radius == 0 || radius > short.MaxValue)
            return Failure;

        return AreaEllipse(
            memory,
            rastPort,
            centerX,
            centerY,
            (short)radius,
            (short)radius);
    }

    /// <summary>
    /// Processes the portable AreaInfo stream with an integer scanline fill.
    /// Polygon subpaths and recorded ellipses use an even/odd crossing rule;
    /// the guest vector state is reset only after all guarded raster writes
    /// succeed. Temporary-raster ownership and layer damage remain explicit
    /// later boundaries, so this operation is claimed only for non-layered
    /// RastPorts by the host adapter.
    /// </summary>
    internal static int AreaEnd(IGraphicsMemory memory, uint rastPort)
        => AreaEnd(memory, null, rastPort);

    /// <summary>
    /// AreaEnd variant used by the graphics-library core. An unset TmpRas is
    /// backed by a temporary guest Chip allocation for the duration of the
    /// operation; an explicitly attached descriptor remains caller-owned.
    /// </summary>
    internal static int AreaEnd(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend? allocator,
        uint rastPort,
        AreaScratch? scratch = null,
        bool allowLayered = false,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (pixelVisible is null)
            return AreaEndCore(memory, allocator, rastPort, scratch, allowLayered, null);

        var visibility = scratch?.Visibility ?? new VisibilityScratch();
        if (!visibility.TryAcquire(pixelVisible, out var stablePixelVisible))
            return Failure;

        try
        {
            return AreaEndCore(
                memory,
                allocator,
                rastPort,
                scratch,
                allowLayered,
                stablePixelVisible);
        }
        finally
        {
            visibility.Release();
        }
    }

    private static int AreaEndCore(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend? allocator,
        uint rastPort,
        AreaScratch? scratch,
        bool allowLayered,
        Func<int, int, bool>? pixelVisible)
    {
        if ((!allowLayered && !TryRequireUnlayeredRastPort(memory, rastPort)) ||
            (allowLayered && pixelVisible is null &&
             !TryRequireUnlayeredRastPort(memory, rastPort)) ||
            !TryReadAreaState(memory, rastPort, out var state))
        {
            return Failure;
        }

        // AreaEnd is also the collector's explicit boundary between fills.
        // An already-empty AreaInfo has no geometry or temporary raster to
        // consume, so the classic vector reports success without requiring a
        // bitmap or mutating the caller's empty collector state.
        if (state.Count == 0)
        {
            return Success;
        }

        // AreaEnd's planar renderer has no private graphics-context colour
        // sidecar.  When a readable RPF_NO_PENS marker is present, honor it
        // before probing the public bitmap, draw-mode, or mask fields.  A
        // missing Flags word remains an ordinary sparse-port case, so the
        // hint is intentionally opportunistic.
        if (TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var privateFlags) &&
            (privateFlags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return Failure;
        }

        var drawMode = GetDrawMode(memory, rastPort);
        if (drawMode < 0 ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return Failure;
        }

        // A zero public write mask is a collector-only no-op.  It is safe to
        // consume the recorded area without probing the destination bitmap,
        // whose pointer and plane storage may be unavailable while every
        // destination plane is masked off.
        if (writeMask == 0)
        {
            return ResetAreaState(memory, state, scratch?._stateOriginal)
                ? Success
                : Failure;
        }

        if (!TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return Failure;
        }

        var effectiveWriteMask = EffectiveWriteMask(bitmap, writeMask);

        // Decode the provider-backed collector before admitting public
        // AreaPtrn/AreaPtSz/FgPen/BPen state.  The shape stream is caller-owned
        // geometry, while the destination colour fields may belong to a
        // sparse/provider-owned graphics context.
        var polygons = scratch?._polygons ?? new List<List<AreaPoint>>();
        var ellipses = scratch?._ellipses ?? new List<AreaEllipseInfo>();
        var shapes = scratch?._shapes ?? new List<AreaShape>();
        var providerShapesDecoded = false;
        if (pixelVisible is not null)
        {
            if (effectiveWriteMask == 0 ||
                !TryDecodeArea(
                    memory,
                    state,
                    out polygons,
                    out ellipses,
                    out shapes,
                    scratch))
            {
                return effectiveWriteMask == 0 &&
                    ResetAreaState(memory, state, scratch?._stateOriginal)
                    ? Success
                    : Failure;
            }

            if (!TryFitAreaPortableWork(shapes))
                return Failure;
            if (!TryGetAreaTemporaryRasterBytes(
                    polygons,
                    ellipses,
                    out var providerAreaBounds,
                    out _ ))
                return Failure;
            if (!TryHasVisibleBitmapRegion(
                    bitmap,
                    providerAreaBounds.MinimumX,
                     providerAreaBounds.MinimumY,
                     providerAreaBounds.MaximumX,
                     providerAreaBounds.MaximumY,
                     pixelVisible,
                    out var hasVisibleDestination))
            {
                return Failure;
            }

            if (!hasVisibleDestination)
            {
                // A fully hidden provider polygon/ellipse is a successful
                // clipped no-op.  Consume the collector, but do not read
                // area-pattern or public pen storage and do not allocate
                // temporary raster ownership that cannot publish a pixel.
                return ResetAreaState(memory, state, scratch?._stateOriginal)
                    ? Success
                    : Failure;
            }

            providerShapesDecoded = true;
        }

        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtrn,
                out var areaPattern))
        {
            return Failure;
        }

        // AreaEnd shares BltPattern's source rules.  A null area pattern is
        // an all-one source, and COMPLEMENT|JAM2 inverts the selected region
        // without consulting either pen.  Read the signed pattern height
        // before the pen fields so a negative multicolour JAM2 source can
        // deposit its encoded planes directly, just like BltPattern, without
        // claiming the public FgPen/BgPen bytes from a provider-owned port.
        var areaPatternSize = (byte)0;
        var complementJam2 = (drawMode & 0x03) == 0x03;
        if (areaPattern != 0 &&
            !complementJam2 &&
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtSz,
                out areaPatternSize))
        {
            return Failure;
        }

        var multicolorJam2 = areaPattern != 0 &&
            unchecked((sbyte)areaPatternSize) < 0 &&
            (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0;

        // The shared planar writer ignores colour in COMPLEMENT mode. Keep
        // FgPen outside AreaEnd's admission envelope for that inversion-only
        // form and for encoded multicolour JAM2; non-complement fills still
        // consume the foreground pen when they need a scalar source colour.
        var foreground = 0;
        if (effectiveWriteMask != 0 &&
            (drawMode & DrawModeComplement) == 0 &&
            !multicolorJam2)
        {
            foreground = GetAPen(memory, rastPort);
            if (foreground < 0)
                return Failure;
        }

        // A NULL AreaPtrn uses the implicit all-one source.  Only inverse-
        // video JAM2 with that source, or a real non-multicolour pattern,
        // consumes BPen.  Encoded multicolour JAM2 deposits source planes
        // directly and therefore must not claim the public background byte.
        byte background = 0;
        var needsBackgroundPen = (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0 &&
            (areaPattern == 0
                ? (drawMode & DrawModeInverseVideo) != 0
                : !multicolorJam2);
        if (effectiveWriteMask != 0 && needsBackgroundPen &&
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortBgPen,
                out background))
        {
            return Failure;
        }

        if (!providerShapesDecoded &&
            !TryDecodeArea(
                memory,
                state,
                out polygons,
                out ellipses,
                out shapes,
                scratch))
        {
            return Failure;
        }

        // A zero RastPort mask selects no destination planes.  Preserve the
        // collector boundary (AreaEnd still consumes the recorded vectors),
        // but do not validate shape bounds, area-pattern storage, or TmpRas
        // capacity that cannot affect a destination write.  This is the same
        // successful no-op contract used by the other planar primitives and
        // avoids turning an out-of-raster shape into a false failure when the
        // caller has intentionally masked every plane off.
        writeMask = effectiveWriteMask;
        if (writeMask == 0)
            return ResetAreaState(memory, state, scratch?._stateOriginal)
                ? Success
                : Failure;

        // With no area pattern, INVERSVID complements the implicit all-one
        // source.  JAM1 (including COMPLEMENT|INVERSVID) therefore has no
        // selected fill cells.  Consume the valid AreaInfo stream without
        // allocating or probing scratch/destination storage that cannot be
        // written; JAM2 and explicit patterns retain the full path below.
        if (areaPattern == 0 &&
            (drawMode & 4) != 0 && (drawMode & 1) == 0)
        {
            return ResetAreaState(memory, state, scratch?._stateOriginal)
                ? Success
                : Failure;
        }

        if (!TryFitAreaPortableWork(shapes))
            return Failure;

        // The temporary raster is work space for the largest area object,
        // not necessarily for the whole destination bitmap or the union
        // of every recorded object.  Keep the explicit descriptor strict,
        // but size this operation from the largest decoded shape so a
        // small valid polygon is not rejected just because another shape
        // is far away in the same AreaInfo stream.
        if (!TryGetAreaTemporaryRasterBytes(
                polygons,
                ellipses,
                out var areaBounds,
                out var requiredTmpRasBytes))
        {
            return Failure;
        }
        uint temporaryRaster;
        uint ownedTemporaryRaster;
        if (scratch is not null)
        {
            // A validated layered host owns bounded, non-escaping managed
            // mask storage in AreaScratch.  Keep TmpRas allocation out of the
            // warmed provider path; the mask is not guest-visible and the
            // same publication/rollback envelope is retained below.
            temporaryRaster = 0;
            ownedTemporaryRaster = 0;
        }
        else if (!TryAcquireAreaTemporaryRaster(
                     memory,
                     allocator,
                     rastPort,
                     requiredTmpRasBytes,
                     out temporaryRaster,
                     out ownedTemporaryRaster))
        {
            return Failure;
        }

        try
        {
            // AreaEnd consumes TmpRas as the one-bit-per-pixel areafill mask.
            // Clear both caller-owned and library-owned work space before
            // rasterizing so stale bits cannot leak into a later fill.
            if (temporaryRaster != 0 &&
                !ClearTemporaryRaster(memory, temporaryRaster, requiredTmpRasBytes))
            {
                return Failure;
            }

            // AreaEnd combines a decoded vector stream, optional pattern source,
            // planar destination, and optional outline pass into one guest
            // operation.  Probe every possible destination in the shape bounds
            // and the complete pattern envelope before the first scanline write so
            // a malformed late row cannot expose a partial fill.
            if (!TryProbeAreaDestination(
                    memory,
                    bitmap,
                     polygons,
                     ellipses,
                     writeMask,
                     pixelVisible) ||
                !TryProbeAreaPattern(
                    memory,
                    areaPattern,
                    unchecked((sbyte)areaPatternSize),
                    bitmap.Depth,
                    drawMode))
            {
                return Failure;
            }

        if (!TrySnapshotBitmapRegion(
            memory,
            bitmap,
                    areaBounds.MinimumX,
                    areaBounds.MinimumY,
                    areaBounds.MaximumX,
                    areaBounds.MaximumY,
                    writeMask,
                    out var destinationSnapshotAddresses,
                     out var destinationSnapshotValues,
                     scratch?._snapshotAddresses,
                     scratch?._snapshotValues,
                     pixelVisible))
            {
                return Failure;
            }

            var destinationCommitted = false;
            ushort areaFlags = 0;
            short originalCursorX = 0;
            short originalCursorY = 0;
            byte originalPatternCount = 0;
            var outlineStateCaptured = false;

            try
            {
                if (!TryReadRastPortWord(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortFlags,
                        out areaFlags))
                {
                    return Failure;
                }

                var outlinePen = -1;
                var drawOutlines = (areaFlags & GraphicsLayouts.RastPortAreaOutline) != 0;
                if (drawOutlines)
                {
                    outlinePen = GetOutlinePen(memory, rastPort);
                    if (outlinePen < 0)
                        return Failure;
                }

                if (drawOutlines &&
                    (!TryReadRastPortSignedWord(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortCurrentX,
                        out originalCursorX) ||
                     !TryReadRastPortSignedWord(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortCurrentY,
                        out originalCursorY) ||
                     !TryReadRastPortByte(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortLinePatternCount,
                        out originalPatternCount)))
                {
                    return Failure;
                }

                outlineStateCaptured = drawOutlines;

                var noCrossFill = (areaFlags & GraphicsLayouts.RastPortNoCrossFill) != 0;
                if (!FillAreaShapes(
                        memory,
                        rastPort,
                        bitmap,
                        shapes,
                        foreground,
                        background,
                        drawMode,
                        writeMask,
                        areaPattern,
                        unchecked((sbyte)areaPatternSize),
                        temporaryRaster,
                        drawOutlines,
                        outlinePen,
                        noCrossFill,
                        scratch,
                        pixelVisible))
                    return Failure;

                if (drawOutlines &&
                    (!TryWriteRastPortWord(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortCurrentX,
                        unchecked((ushort)originalCursorX)) ||
                     !TryWriteRastPortWord(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortCurrentY,
                        unchecked((ushort)originalCursorY)) ||
                     !TryWriteRastPortWord(
                        memory,
                        rastPort,
                        GraphicsLayouts.RastPortFlags,
                        areaFlags)))
                {
                    return Failure;
                }

                if (!ResetAreaState(memory, state, scratch?._stateOriginal))
                    return Failure;

                destinationCommitted = true;
                return Success;
            }
            finally
            {
                if (!destinationCommitted)
                {
                    RestoreBitmapSnapshot(
                        memory,
                        destinationSnapshotAddresses,
                        destinationSnapshotValues);

                    if (outlineStateCaptured)
                    {
                        _ = TryWriteRastPortByte(
                            memory,
                            rastPort,
                            GraphicsLayouts.RastPortFgPen,
                            unchecked((byte)foreground));
                        _ = TryWriteRastPortByte(
                            memory,
                            rastPort,
                            GraphicsLayouts.RastPortLinePatternCount,
                            originalPatternCount);
                        // Outline drawing can publish pixels before its final
                        // cursor/Flags state fails. Restore all WORD fields
                        // byte-wise so a repeated WORD rejection cannot leave
                        // AreaEnd's caller state half-published.
                        Restore(
                            memory,
                            rastPort + (uint)GraphicsLayouts.RastPortCurrentX,
                            new[]
                            {
                                (byte)((ushort)originalCursorX >> 8),
                                (byte)(ushort)originalCursorX
                            });
                        Restore(
                            memory,
                            rastPort + (uint)GraphicsLayouts.RastPortCurrentY,
                            new[]
                            {
                                (byte)((ushort)originalCursorY >> 8),
                                (byte)(ushort)originalCursorY
                            });
                        Restore(
                            memory,
                            rastPort + (uint)GraphicsLayouts.RastPortFlags,
                            new[] { (byte)(areaFlags >> 8), (byte)areaFlags });
                    }
                }
            }
        }
        finally
        {
            if (ownedTemporaryRaster != 0)
                allocator!.Free(
                    ownedTemporaryRaster,
                    requiredTmpRasBytes,
                    GraphicsMemoryClass.Chip);
        }
    }

    private static bool TryProbeAreaDestination(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        List<List<AreaPoint>> polygons,
        List<AreaEllipseInfo> ellipses,
        byte writeMask,
        Func<int, int, bool>? pixelVisible = null)
    {
        var minimumX = int.MaxValue;
        var minimumY = int.MaxValue;
        var maximumX = int.MinValue;
        var maximumY = int.MinValue;

        foreach (var polygon in polygons)
        {
            foreach (var point in polygon)
            {
                minimumX = Math.Min(minimumX, point.X);
                minimumY = Math.Min(minimumY, point.Y);
                maximumX = Math.Max(maximumX, point.X);
                maximumY = Math.Max(maximumY, point.Y);
            }
        }

        foreach (var ellipse in ellipses)
        {
            minimumX = Math.Min(minimumX, ellipse.CenterX - ellipse.RadiusX);
            minimumY = Math.Min(minimumY, ellipse.CenterY - ellipse.RadiusY);
            maximumX = Math.Max(maximumX, ellipse.CenterX + ellipse.RadiusX);
            maximumY = Math.Max(maximumY, ellipse.CenterY + ellipse.RadiusY);
        }

        // Direct AreaEnd is a non-layered primitive and does not provide the
        // layer library's software clipping.  Keep that all-or-nothing
        // boundary when no provider predicate is supplied.  An explicit
        // provider, however, owns logical clipping; admit the clipped bitmap
        // envelope so its fill/outline paths can skip impossible samples.
        if (minimumX == int.MaxValue)
            return true;

        if (pixelVisible is null &&
            (minimumX < 0 || minimumY < 0 ||
             maximumX >= bitmap.Width || maximumY >= bitmap.Rows))
        {
            return false;
        }

        minimumX = Math.Max(0, minimumX);
        minimumY = Math.Max(0, minimumY);
        maximumX = Math.Min(bitmap.Width - 1, maximumX);
        maximumY = Math.Min(bitmap.Rows - 1, maximumY);
        if (minimumX > maximumX || minimumY > maximumY)
            return true;

        return TryProbeBitmapRegion(
            memory,
            bitmap,
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            writeMask,
            pixelVisible);
    }

    private readonly struct AreaRasterBounds
    {
        internal AreaRasterBounds(
            int minimumX,
            int minimumY,
            int maximumX,
            int maximumY,
            int bytesPerRow)
        {
            MinimumX = minimumX;
            MinimumY = minimumY;
            MaximumX = maximumX;
            MaximumY = maximumY;
            BytesPerRow = bytesPerRow;
        }

        internal int MinimumX { get; }
        internal int MinimumY { get; }
        internal int MaximumX { get; }
        internal int MaximumY { get; }
        internal int BytesPerRow { get; }
        internal int Width => MaximumX - MinimumX + 1;
        internal int Height => MaximumY - MinimumY + 1;
    }

    private static bool TryFitAreaPortableWork(IReadOnlyList<AreaShape> shapes)
    {
        ulong aggregateCells = 0;
        // IReadOnlyList<T>.GetEnumerator boxes List<T>.Enumerator. AreaEnd
        // validates this bound twice around provider admission, so use the
        // typed indexed contract and keep the warmed raster path allocation-free.
        for (var index = 0; index < shapes.Count; index++)
        {
            var shape = shapes[index];
            if (!shape.IsEllipse &&
                (shape.Polygon is null || shape.Polygon.Count < 3))
            {
                continue;
            }

            if (!TryGetAreaShapeBounds(shape, out var bounds))
                return false;

            var cells = (ulong)(uint)bounds.Width * (uint)bounds.Height;
            if (cells == 0 ||
                cells > PortableWorkLimit / AreaPassesPerCell ||
                aggregateCells >
                    (PortableWorkLimit / AreaPassesPerCell) - cells)
            {
                return false;
            }

            aggregateCells += cells;
        }

        return aggregateCells != 0;
    }

    private static bool TryGetAreaTemporaryRasterBytes(
        List<List<AreaPoint>> polygons,
        List<AreaEllipseInfo> ellipses,
        out AreaRasterBounds bounds,
        out uint requiredBytes)
    {
        bounds = default;
        requiredBytes = 0;
        var minimumX = int.MaxValue;
        var minimumY = int.MaxValue;
        var maximumX = int.MinValue;
        var maximumY = int.MinValue;
        ulong largestShapeBytes = 0;

        foreach (var polygon in polygons)
        {
            var shapeMinimumX = int.MaxValue;
            var shapeMinimumY = int.MaxValue;
            var shapeMaximumX = int.MinValue;
            var shapeMaximumY = int.MinValue;
            foreach (var point in polygon)
            {
                minimumX = Math.Min(minimumX, point.X);
                minimumY = Math.Min(minimumY, point.Y);
                maximumX = Math.Max(maximumX, point.X);
                maximumY = Math.Max(maximumY, point.Y);
                shapeMinimumX = Math.Min(shapeMinimumX, point.X);
                shapeMinimumY = Math.Min(shapeMinimumY, point.Y);
                shapeMaximumX = Math.Max(shapeMaximumX, point.X);
                shapeMaximumY = Math.Max(shapeMaximumY, point.Y);
            }

            if (shapeMinimumX != int.MaxValue &&
                TryCreateAreaRasterBounds(
                    shapeMinimumX,
                    shapeMinimumY,
                    shapeMaximumX,
                    shapeMaximumY,
                    out _,
                    out var shapeBytes))
            {
                largestShapeBytes = Math.Max(largestShapeBytes, shapeBytes);
            }
        }

        foreach (var ellipse in ellipses)
        {
            var ellipseMinimumX = ellipse.CenterX - ellipse.RadiusX;
            var ellipseMinimumY = ellipse.CenterY - ellipse.RadiusY;
            var ellipseMaximumX = ellipse.CenterX + ellipse.RadiusX;
            var ellipseMaximumY = ellipse.CenterY + ellipse.RadiusY;
            minimumX = Math.Min(minimumX, ellipseMinimumX);
            minimumY = Math.Min(minimumY, ellipseMinimumY);
            maximumX = Math.Max(maximumX, ellipseMaximumX);
            maximumY = Math.Max(maximumY, ellipseMaximumY);

            if (TryCreateAreaRasterBounds(
                    ellipseMinimumX,
                    ellipseMinimumY,
                    ellipseMaximumX,
                    ellipseMaximumY,
                    out _,
                    out var ellipseBytes))
            {
                largestShapeBytes = Math.Max(largestShapeBytes, ellipseBytes);
            }
        }

        if (minimumX == int.MaxValue || minimumY == int.MaxValue ||
            maximumX < minimumX || maximumY < minimumY)
        {
            return false;
        }

        // The destination transaction still needs the union bounds, while
        // the scratch capacity follows the largest individual shape.  Each
        // shape gets its own local origin when the mask is cleared/applied.
        return TryCreateAreaRasterBounds(
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            out bounds,
            out _) &&
            largestShapeBytes != 0 &&
            largestShapeBytes <= uint.MaxValue &&
            (requiredBytes = (uint)largestShapeBytes) != 0;
    }

    private static bool TryGetAreaShapeBounds(
        AreaShape shape,
        out AreaRasterBounds bounds)
    {
        bounds = default;
        if (shape.IsEllipse)
        {
            var ellipse = shape.Ellipse;
            return TryCreateAreaRasterBounds(
                ellipse.CenterX - ellipse.RadiusX,
                ellipse.CenterY - ellipse.RadiusY,
                ellipse.CenterX + ellipse.RadiusX,
                ellipse.CenterY + ellipse.RadiusY,
                out bounds,
                out _);
        }

        var polygon = shape.Polygon;
        if (polygon is null || polygon.Count == 0)
            return false;

        var minimumX = int.MaxValue;
        var minimumY = int.MaxValue;
        var maximumX = int.MinValue;
        var maximumY = int.MinValue;
        foreach (var point in polygon)
        {
            minimumX = Math.Min(minimumX, point.X);
            minimumY = Math.Min(minimumY, point.Y);
            maximumX = Math.Max(maximumX, point.X);
            maximumY = Math.Max(maximumY, point.Y);
        }

        return TryCreateAreaRasterBounds(
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            out bounds,
            out _);
    }

    private static bool TryCreateAreaRasterBounds(
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY,
        out AreaRasterBounds bounds,
        out uint bytes)
    {
        bounds = default;
        bytes = 0;
        if (maximumX < minimumX || maximumY < minimumY)
            return false;

        // RASSIZE rounds each object's width to complete 16-pixel words; the
        // height remains a logical row count.  Do not clip coordinates here:
        // destination validation and the per-shape mask use the same native
        // envelope even when a later provider chooses a different clipping
        // policy.
        var width = (ulong)((long)maximumX - minimumX + 1L);
        var height = (ulong)((long)maximumY - minimumY + 1L);
        var bytesPerRow = ((width + 15u) / 16u) * 2u;
        var total = bytesPerRow * height;
        if (width == 0 || height == 0 || bytesPerRow == 0 ||
            bytesPerRow > int.MaxValue || total > uint.MaxValue)
        {
            return false;
        }

        bounds = new AreaRasterBounds(
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            (int)bytesPerRow);
        bytes = (uint)total;
        return true;
    }

    private static bool TryCreateManagedAreaMask(
        AreaRasterBounds bounds,
        out bool[]? mask)
    {
        mask = null;
        var cells = (ulong)(uint)bounds.Width * (uint)bounds.Height;
        if (cells == 0 || cells > int.MaxValue)
            return false;

        try
        {
            mask = new bool[(int)cells];
            return true;
        }
        catch (OutOfMemoryException)
        {
            return false;
        }
    }

    private static bool TryAcquireManagedAreaMask(
        AreaScratch scratch,
        AreaRasterBounds bounds,
        out bool[]? mask)
    {
        mask = null;
        var cells = (ulong)(uint)bounds.Width * (uint)bounds.Height;
        if (cells == 0 || cells > int.MaxValue ||
            !scratch.TryAcquireManagedMask((int)cells, out var acquired))
        {
            return false;
        }
        mask = acquired;
        return true;
    }

    private static bool TryProbeAreaPattern(
        IGraphicsMemory memory,
        uint areaPattern,
        sbyte areaPatternSize,
        int depth,
        int drawMode)
    {
        // JAM1 still consumes the pattern as its foreground stencil.  Only
        // COMPLEMENT|JAM2 ignores the source and inverts the whole selected
        // region, so every other non-null pattern must be probed up front.
        if (areaPattern == 0 || (drawMode & 3) == 3)
            return true;

        if ((areaPattern & 1u) != 0 || depth <= 0)
            return false;

        var exponent = areaPatternSize >= 0
            ? areaPatternSize
            : -(int)areaPatternSize;
        if (exponent > 15)
            return false;

        var rows = 1u << exponent;
        var planes = areaPatternSize >= 0 ? 1u : (uint)depth;
        var byteCount = (ulong)rows * 2u * planes;
        return byteCount <= uint.MaxValue &&
            TryProbeContiguous(memory, areaPattern, (uint)byteCount, 1);
    }

    /// <summary>
    /// Updates the guest RasInfo bitmap pointer used by a viewport. DBufInfo
    /// synchronization and display publication stay outside this pure-memory
    /// operation and must be supplied by the scheduler-aware host boundary.
    /// </summary>
    internal static int ChangeViewPortBitMap(
        IGraphicsMemory memory,
        uint viewPort,
        uint bitMap)
        => ChangeViewPortBitMap(memory, viewPort, bitMap, 0);

    internal static int ChangeViewPortBitMap(
        IGraphicsMemory memory,
        uint viewPort,
        uint bitMap,
        uint dbufInfo)
        => ChangeViewPortBitMap(memory, viewPort, bitMap, dbufInfo, out _);

    internal static int ChangeViewPortBitMap(
        IGraphicsMemory memory,
        uint viewPort,
        uint bitMap,
        uint dbufInfo,
        out uint previousBitMap)
        => ChangeViewPortBitMap(
            memory,
            viewPort,
            bitMap,
            dbufInfo,
            out previousBitMap,
            dbufRegistry: null);

    internal static int ChangeViewPortBitMap(
        IGraphicsMemory memory,
        uint viewPort,
        uint bitMap,
        uint dbufInfo,
        out uint previousBitMap,
        GraphicsDoubleBufferOperations.Registry? dbufRegistry)
    {
        if (!TryValidateViewPortBitMapChange(
                memory,
                viewPort,
                bitMap,
                dbufInfo,
                dbufRegistry,
                out var rasInfo,
                out var currentBitMap))
        {
            previousBitMap = 0;
            return Failure;
        }

        previousBitMap = currentBitMap;

        // ChangeVPBitMap is a display-layout operation, not a general bitmap
        // conversion.  The replacement must retain the attached viewport's
        // depth, row count, word alignment, and BytesPerRow.  A missing current
        // bitmap is tolerated while a viewport is being assembled; an
        // existing malformed one is rejected before the guest pointer changes.
        var bitmapAddress = rasInfo + (uint)GraphicsLayouts.RasInfoBitMap;
        if (!TrySnapshot(memory, bitmapAddress, sizeof(uint), out var originalBitmapLink))
            return Failure;

        if (!memory.TryWriteLong(bitmapAddress, bitMap))
        {
            // A guarded guest long write may fail after publishing a prefix
            // of the pointer. Restore the previous association byte-wise so
            // a rejecting LONG adapter cannot preserve a half-published
            // bitmap link for the next display-boundary retry.
            Restore(memory, bitmapAddress, originalBitmapLink);
            return Failure;
        }

        return Success;
    }

    /// <summary>
    /// Performs the complete non-mutating ChangeVPBitMap admission check.
    /// Display capability callbacks are allowed to observe a request only
    /// after this envelope has been proven: viewport/RasInfo, DBufInfo
    /// ownership, replacement bitmap, and the currently attached bitmap all
    /// have to satisfy the standard-planar display contract.
    /// </summary>
    internal static bool TryValidateViewPortBitMapChange(
        IGraphicsMemory memory,
        uint viewPort,
        uint bitMap,
        uint dbufInfo,
        GraphicsDoubleBufferOperations.Registry? dbufRegistry,
        out uint rasInfo,
        out uint currentBitMap)
    {
        rasInfo = 0;
        currentBitMap = 0;

        var dbufValid = dbufRegistry is null
            ? GraphicsDoubleBufferOperations.ValidateForChange(memory, dbufInfo)
            : GraphicsDoubleBufferOperations.ValidateForChange(memory, dbufRegistry, dbufInfo);
        if (!dbufValid ||
            !TryReadViewportRasInfo(memory, viewPort, out rasInfo) ||
            !ValidateRasInfoChain(memory, rasInfo, validateBitMap: false) ||
            !memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out currentBitMap) ||
            !TryReadBitmapLayout(memory, bitMap, out var nextLayout) ||
            !ValidateDisplayableBitmapEnvelope(memory, bitMap, nextLayout))
        {
            return false;
        }

        // ChangeVPBitMap is a display-layout operation, not a general bitmap
        // conversion. The replacement must retain the attached viewport's
        // depth, row count, word alignment, and BytesPerRow. A missing
        // current bitmap is tolerated while a viewport is being assembled;
        // an existing malformed one is rejected before the guest pointer
        // changes.
        if (currentBitMap == 0)
            return true;

        return TryReadBitmapLayout(memory, currentBitMap, out var currentLayout) &&
            ValidateDisplayableBitmapEnvelope(memory, currentBitMap, currentLayout) &&
            currentLayout.BytesPerRow == nextLayout.BytesPerRow &&
            currentLayout.Depth == nextLayout.Depth &&
            (currentLayout.Rows == 0 || currentLayout.Rows == nextLayout.Rows);
    }

    private static bool ValidateDisplayableBitmapEnvelope(
        IGraphicsMemory memory,
        uint bitMap,
        BitmapLayout layout)
    {
        if (!TryAddress(
                bitMap,
                GraphicsLayouts.BitMapFlags,
                sizeof(byte),
                out var flagsAddress) ||
            !memory.TryReadByte(flagsAddress, out var flags))
        {
            return false;
        }

        // Geometry-only BitMaps are still useful while a viewport is being
        // assembled and remain accepted by the portable association path.
        // Once the public flags advertise BMF_DISPLAYABLE, however, the
        // replacement is an actual display surface: every plane must be
        // word-aligned and its complete declared row span must be readable
        // before RasInfo.BitMap is changed.
        if ((flags & BmfDisplayable) == 0)
            return true;

        return TryReadDisplayBitmapLayout(memory, bitMap, out var displayLayout) &&
               displayLayout.BytesPerRow == layout.BytesPerRow &&
               displayLayout.Depth == layout.Depth;
    }

    /// <summary>
    /// Allocates one chip-memory bitplane.  The size is rounded to complete
    /// 16-bit words per row, matching the classic AllocRaster contract.
    /// </summary>
    internal static uint AllocRaster(
        IGraphicsAllocatorBackend allocator,
        uint width,
        uint height)
    {
        if (!TryGetRasterByteCount(width, height, out var byteCount))
        {
            return 0;
        }

        var allocationSucceeded = allocator.TryAllocate(
            byteCount,
            GraphicsMemoryClass.Chip,
            out var address);
        if (!allocationSucceeded)
        {
            // Keep the same guarded ownership rule as AllocBitMap: a
            // backend that returns a provisional address with a false status
            // still transfers that span to this boundary for cleanup.
            if (address != 0)
                allocator.Free(address, byteCount, GraphicsMemoryClass.Chip);
            return 0;
        }

        if (address == 0)
            return 0;

        // AllocRaster returns a sequence of 16-bit words.  Keep the guest
        // base word-aligned even when a host allocator is deliberately more
        // permissive; an odd base would turn the first word access into the
        // native 68000 address-error/provider boundary.
        if ((address & 1u) != 0 || !TryGetGuestSpan(address, byteCount))
        {
            allocator.Free(address, byteCount, GraphicsMemoryClass.Chip);
            return 0;
        }

        return address;
    }

    /// <summary>
    /// Releases one allocation made by AllocRaster.  The public ABI is void;
    /// the internal status lets the register adapter reject malformed input
    /// without asking an allocator to free an unknown range.
    /// </summary>
    internal static int FreeRaster(
        IGraphicsAllocatorBackend allocator,
        uint address,
        uint width,
        uint height)
    {
        // AllocRaster returns a sequence of 16-bit words.  A caller-owned
        // odd PLANEPTR is therefore not a valid native 68000 free envelope:
        // reject it before reconstructing the byte span or invoking the
        // allocator so a byte-addressable host cannot release the wrong
        // chip-memory block.  The native/provider owner remains available
        // for any non-standard pointer policy.
        if ((address & 1u) != 0)
            return Failure;

        if (!TryGetRasterByteCount(width, height, out var byteCount) ||
            !TryGetGuestSpan(address, byteCount))
            return Failure;

        allocator.Free(address, byteCount, GraphicsMemoryClass.Chip);
        return Success;
    }

    /// <summary>
    /// Reads the V39 bitmap attributes without exposing the guest structure to
    /// callers.  Unknown attributes intentionally return zero.
    /// </summary>
    internal static uint GetBitMapAttr(
        IGraphicsMemory memory,
        uint bitMap,
        uint attribute)
    {
        return TryGetBitMapAttr(memory, bitMap, attribute, out var value)
            ? value
            : 0;
    }

    /// <summary>
    /// Reads a V39 bitmap attribute while preserving the distinction between
    /// a valid unknown attribute (which returns zero) and an unreadable guest
    /// envelope (which must remain available to native/provider ownership at
    /// a register boundary).
    /// </summary>
    internal static bool TryGetBitMapAttr(
        IGraphicsMemory memory,
        uint bitMap,
        uint attribute,
        out uint value)
    {
        value = 0;
        if (bitMap == 0 || (bitMap & 1u) != 0)
        {
            return false;
        }

        // GetBitMapAttr is a field query, not a general bitmap admission
        // probe.  The classic vector reads only the requested scalar for
        // BMA_HEIGHT/BMA_DEPTH/BMA_WIDTH; an unrelated unreadable flag byte,
        // plane link, or compact tail must not steal a valid query from the
        // native/provider owner.  BMA_FLAGS is the one capability query that
        // needs the full standard-planar envelope when DISPLAYABLE is set.
        switch (attribute)
        {
            case 0: // BMA_HEIGHT
                return TryAddress(
                           bitMap,
                           GraphicsLayouts.BitMapRows,
                           sizeof(ushort),
                           out var rowsAddress) &&
                       memory.TryReadWord(rowsAddress, out var rows) &&
                       PublishBitMapAttr(rows, out value);

            case 4: // BMA_DEPTH
                return TryAddress(
                           bitMap,
                           GraphicsLayouts.BitMapDepth,
                           sizeof(byte),
                           out var depthAddress) &&
                       memory.TryReadByte(depthAddress, out var depth) &&
                       PublishBitMapAttr(depth, out value);

            case 8: // BMA_WIDTH
                if (!TryAddress(
                        bitMap,
                        GraphicsLayouts.BitMapBytesPerRow,
                        sizeof(ushort),
                        out var bytesPerRowAddress) ||
                    !memory.TryReadWord(bytesPerRowAddress, out var bytesPerRow) ||
                    bytesPerRow == 0 || (bytesPerRow & 1) != 0)
                {
                    return false;
                }

                // Interleaved BitMaps publish an aggregate BytesPerRow but
                // their logical width is the per-plane stride.  Flags and
                // Depth are secondary inputs for that one representation;
                // if Flags is unavailable, retain the scalar aggregate-width
                // answer instead of turning an otherwise valid query into a
                // full-layout admission failure.
                if (TryAddress(
                        bitMap,
                        GraphicsLayouts.BitMapFlags,
                        sizeof(byte),
                        out var widthFlagsAddress) &&
                    memory.TryReadByte(widthFlagsAddress, out var widthFlags) &&
                    IsInterleaved(widthFlags))
                {
                    if (!TryAddress(
                            bitMap,
                            GraphicsLayouts.BitMapDepth,
                            sizeof(byte),
                            out var widthDepthAddress) ||
                        !memory.TryReadByte(widthDepthAddress, out var widthDepth) ||
                        !TryGetPlaneBytesPerRow(
                            bytesPerRow,
                            widthDepth,
                            widthFlags,
                            out var planeBytesPerRow))
                    {
                        return false;
                    }

                    bytesPerRow = planeBytesPerRow;
                }

                return PublishBitMapAttr(
                    (uint)bytesPerRow * 8u,
                    out value);

            case 12: // BMA_FLAGS
                if (!TryAddress(
                        bitMap,
                        GraphicsLayouts.BitMapFlags,
                        sizeof(byte),
                        out var flagsAddress) ||
                    !memory.TryReadByte(flagsAddress, out var flags))
                {
                    return false;
                }

                // BMA_FLAGS reports the public storage/display
                // classification, not allocator request bits such as
                // BMF_CLEAR or BMF_MINPLANES.  When DISPLAYABLE is absent,
                // no plane validation is needed to answer the query.
                var publicFlags = (uint)(flags & PublicBitmapFlags);
                if ((publicFlags & BmfDisplayable) == 0)
                    return PublishBitMapAttr(publicFlags, out value);

                if (!TryReadBitmapLayout(memory, bitMap, out var layout))
                    return false;

                return PublishBitMapAttr(
                    GetPublicBitmapFlags(memory, bitMap, layout, flags),
                    out value);

            default:
                // Unknown attributes are documented to return zero.  Keep
                // that result behind only the public non-null/alignment
                // boundary; no guest bitmap field is part of this query.
                value = 0;
                return true;
        }
    }

    private static bool PublishBitMapAttr(uint candidate, out uint value)
    {
        value = candidate;
        return true;
    }

    private static uint GetPublicBitmapFlags(
        IGraphicsMemory memory,
        uint bitMap,
        BitmapLayout layout,
        byte flags)
    {
        var publicFlags = (uint)(flags & PublicBitmapFlags);
        if ((publicFlags & BmfDisplayable) == 0)
            return publicFlags;

        // BMF_DISPLAYABLE is a capability result, not just a remembered
        // allocation request.  Kickstart only reports it when the bitmap's
        // row stride and every declared plane start satisfy the native
        // display-DMA alignment rule.  Keep the query consistent with
        // AllocBitMap's publication check even for guest headers that were
        // initialized or mutated outside this allocator.
        var planes = new uint[layout.Depth];
        for (var plane = 0; plane < layout.Depth; plane++)
        {
            var offset = GraphicsLayouts.BitMapPlanes + (plane * sizeof(uint));
            if (!TryAddress(bitMap, offset, sizeof(uint), out var planeAddress) ||
                !memory.TryReadLong(planeAddress, out planes[plane]))
            {
                return publicFlags & ~BmfDisplayable;
            }
        }

        return HasDisplayablePlaneEnvelope(
                memory,
                layout.BytesPerRow,
                layout.PlaneBytesPerRow,
                layout.Rows,
                planes,
                layout.Depth,
                layout.Flags)
            ? publicFlags
            : publicFlags & ~BmfDisplayable;
    }

    /// <summary>
    /// Allocates a standard planar BitMap and its chip-memory planes.  A
    /// mapped standard friend contributes its row stride so blits between the
    /// two surfaces do not have to cross a narrower allocation boundary.  A
    /// requested interleaved block is required on ECS/AGA: if the chip
    /// allocator cannot provide one, the routine fails without publishing a
    /// replacement bitmap.  OCS ignores the request and uses separate planes.
    /// BMF_MINPLANES uses the documented compact
    /// plane-pointer span instead of reserving all eight links.  A non-null
    /// friend that is not even a readable BitMap geometry header is rejected
    /// here; a CyberGraphX/native provider can claim that foreign friend path
    /// without this portable allocator guessing its layout.
    /// </summary>
    internal static uint AllocBitMap(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint width,
        uint height,
        uint depth,
        uint flags,
        uint friendBitMap,
        byte[]? originalBitmapBuffer = null,
        uint[]? allocatedPlaneBuffer = null,
        bool supportsInterleaved = true)
    {
        // ECS/AGA AllocBitMap rejects widths above the largest safe
        // word-addressable display span.  OCS deliberately performs no such
        // check (the classic library leaves that safety burden to callers),
        // so keep the limit tied to the active chipset capability rather than
        // baking it into the common geometry helper.
        if (supportsInterleaved && width > 32760u)
            return 0;

        ushort friendPlaneBytesPerRow = 0;
        var friendLayout = default(BitmapLayout);
        if (friendBitMap != 0 &&
            (!TryReadBitmapLayout(memory, friendBitMap, out friendLayout) ||
             friendLayout.PlaneBytesPerRow == 0))
        {
            return 0;
        }

        if (friendBitMap != 0)
            friendPlaneBytesPerRow = friendLayout.PlaneBytesPerRow;

        if (!TryGetBitmapGeometry(
                width,
                height,
                depth,
                friendPlaneBytesPerRow,
                out var planeBytesPerRow,
                out var rows,
                out var planeBytes))
            return 0;

        // Kickstart ignores BMF_INTERLEAVED on OCS.  Keep the request in the
        // caller's flag word for the native/provider boundary, but publish
        // only storage that the active chipset can actually represent.
        var interleavedRequested = (flags & BmfInterleaved) != 0;
        var interleavedSupported = interleavedRequested && supportsInterleaved;
        var bitmapFlags = (byte)((flags & (BmfClear | BmfDisplayable | BmfMinPlanes)) |
            (interleavedSupported ? BmfInterleaved : 0) |
            BmfStandard);
        var bytesPerRow = (uint)(planeBytesPerRow *
            ((bitmapFlags & BmfInterleaved) != 0 ? depth : 1u));
        if (bytesPerRow > ushort.MaxValue)
            return 0;
        var bitmapBytes = GetBitmapStructureBytes((byte)depth, bitmapFlags);
        if (!allocator.TryAllocate(bitmapBytes, GraphicsMemoryClass.Public, out var bitMap) ||
            bitMap == 0 ||
            (bitMap & 1u) != 0)
        {
            if (bitMap != 0)
                allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
            return 0;
        }

        if (!TrySnapshot(
                memory,
                bitMap,
                checked((int)bitmapBytes),
                out var originalBitmap,
                originalBitmapBuffer))
        {
            allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
            return 0;
        }

        if (!ClearGuestMemory(memory, bitMap, checked((int)bitmapBytes)) ||
            !memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, (ushort)bytesPerRow) ||
            !memory.TryWriteWord(bitMap + (uint)GraphicsLayouts.BitMapRows, rows) ||
            !memory.TryWriteByte(
                bitMap + (uint)GraphicsLayouts.BitMapFlags,
                bitmapFlags) ||
            !memory.TryWriteByte(bitMap + (uint)GraphicsLayouts.BitMapDepth, (byte)depth))
        {
            Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
            allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
            return 0;
        }

        var interleaved = interleavedSupported;
        // Rollback must describe the storage we actually published, not the
        // caller's request.  On OCS BMF_INTERLEAVED is deliberately ignored,
        // so the planes below are separate allocations even when the request
        // carried that bit.  Feeding the raw request into FreeAllocatedBitMap
        // would release only plane zero as an aggregate interleaved span and
        // leak the remaining plane allocations when a late header write fails.
        var allocationFlags = bitmapFlags;
        var clear = (flags & BmfClear) != 0;
        var planeCount = (int)depth;
        var allocatedPlanes = allocatedPlaneBuffer ?? new uint[planeCount];
        if (allocatedPlanes.Length < planeCount)
        {
            Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
            allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
            return 0;
        }
        Array.Clear(allocatedPlanes, 0, planeCount);
        var planeAllocationBytes = planeBytes;
        if (interleaved)
        {
            var totalBytes = (ulong)planeBytes * (uint)planeCount;
            uint block = 0;
            var allocationSucceeded = totalBytes <= uint.MaxValue &&
                allocator.TryAllocate((uint)totalBytes, GraphicsMemoryClass.Chip, out block);
            var contiguous = allocationSucceeded && block != 0;
            if (!contiguous && block != 0)
            {
                // Allocator implementations normally clear the out address
                // on failure, but the portable contract must also be safe
                // for a backend that returns a provisional address together
                // with a false status.  Treat that address as owned until
                // this boundary releases it; otherwise a failed interleaved
                // request leaks a chip span before the bitmap is rolled back.
                allocator.Free(block, (uint)totalBytes, GraphicsMemoryClass.Chip);
                block = 0;
            }
            if (contiguous && !TryGetGuestSpan(block, (uint)totalBytes))
            {
                allocator.Free(block, (uint)totalBytes, GraphicsMemoryClass.Chip);
                Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
                allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
                return 0;
            }
            if (!contiguous)
            {
                // Kickstart treats BMF_INTERLEAVED as a storage contract on
                // ECS/AGA: unlike the OCS case where the flag is ignored,
                // an unavailable contiguous chip block makes AllocBitMap()
                // fail. Do not silently publish a separate-plane bitmap;
                // callers use the interleaved result for display-safe row
                // placement and must be able to retry with different flags.
                Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
                allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
                return 0;
            }
            else
            {
                for (var plane = 0; plane < planeCount; plane++)
                {
                    // BMF_INTERLEAVED publishes one aggregate row stride.
                    // Each plane starts at its slice within the first row;
                    // later rows advance by that aggregate stride.  Keeping
                    // the links row-interleaved is what lets display DMA and
                    // ordinary raster consumers observe the Kickstart layout.
                    allocatedPlanes[plane] = (uint)((ulong)block +
                        ((ulong)plane * planeBytesPerRow));
                }

                if (clear && !ClearPlaneMemory(memory, block, (uint)totalBytes))
                {
                    allocator.Free(block, (uint)totalBytes, GraphicsMemoryClass.Chip);
                    Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
                    allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
                    return 0;
                }
            }
        }
        if (!interleaved)
        {
            for (var plane = 0; plane < planeCount; plane++)
            {
                if (!allocator.TryAllocate(planeAllocationBytes, GraphicsMemoryClass.Chip, out allocatedPlanes[plane]) ||
                    !TryGetGuestSpan(allocatedPlanes[plane], planeAllocationBytes))
                {
                    if (allocatedPlanes[plane] != 0)
                        allocator.Free(allocatedPlanes[plane], planeAllocationBytes, GraphicsMemoryClass.Chip);
                    for (var previous = 0; previous < plane; previous++)
                    {
                        if (allocatedPlanes[previous] != 0)
                            allocator.Free(allocatedPlanes[previous], planeAllocationBytes, GraphicsMemoryClass.Chip);
                    }

                    Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
                    allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
                    return 0;
                }

                if (clear && !ClearPlaneMemory(memory, allocatedPlanes[plane], planeAllocationBytes))
                {
                    for (var previous = 0; previous <= plane; previous++)
                    {
                        if (allocatedPlanes[previous] != 0)
                            allocator.Free(allocatedPlanes[previous], planeAllocationBytes, GraphicsMemoryClass.Chip);
                    }

                    Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
                    allocator.Free(bitMap, bitmapBytes, GraphicsMemoryClass.Public);
                    return 0;
                }
            }
        }

        for (var plane = 0; plane < planeCount; plane++)
        {
            if (!memory.TryWriteLong(
                    bitMap + (uint)GraphicsLayouts.BitMapPlanes + (uint)(plane * 4),
                    allocatedPlanes[plane]))
            {
                FreeAllocatedBitMap(
                    memory,
                    allocator,
                    bitMap,
                    planeBytesPerRow,
                    rows,
                    (byte)depth,
                    allocationFlags,
                    allocatedPlanes);
                Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
                return 0;
            }
        }

        // BMF_DISPLAYABLE is a capability result, not merely a request bit.
        // The native display DMA path requires word-aligned plane starts (the
        // row stride is already rounded to an even number above).  Allocators
        // used by a host/native bridge are allowed to return a less-aligned
        // chip address; keep the allocation usable, but clear the published
        // displayable bit instead of advertising a surface that LoadView must
        // later reject.
        if ((allocationFlags & BmfDisplayable) != 0 &&
            !HasDisplayablePlaneEnvelope(
                memory,
                (ushort)bytesPerRow,
                planeBytesPerRow,
                rows,
                allocatedPlanes,
                planeCount,
                allocationFlags))
        {
            allocationFlags = (byte)(allocationFlags & ~BmfDisplayable);
            if (!memory.TryWriteByte(
                    bitMap + (uint)GraphicsLayouts.BitMapFlags,
                    allocationFlags))
            {
                FreeAllocatedBitMap(
                    memory,
                    allocator,
                    bitMap,
                    planeBytesPerRow,
                    rows,
                    (byte)depth,
                    allocationFlags,
                    allocatedPlanes);
                Restore(memory, bitMap, originalBitmap, checked((int)bitmapBytes));
                return 0;
            }
        }

        return bitMap;
    }

    private static bool HasDisplayableAlignment(
        ushort bytesPerRow,
        uint[] planes,
        int planeCount)
    {
        if ((bytesPerRow & 1) != 0 || planeCount <= 0 || planes.Length < planeCount)
            return false;

        for (var plane = 0; plane < planeCount; plane++)
        {
            if (planes[plane] == 0 || (planes[plane] & 1u) != 0)
                return false;
        }

        return true;
    }

    private static bool HasDisplayablePlaneEnvelope(
        IGraphicsMemory memory,
        ushort guestBytesPerRow,
        ushort planeBytesPerRow,
        ushort rows,
        uint[] planes,
        int planeCount,
        byte flags)
    {
        if (!HasDisplayableAlignment(planeBytesPerRow, planes, planeCount))
            return false;

        // A memory-only CopperSharp68k provider may not know the host's
        // chipset address classes.  Preserve the portable alignment rule in
        // that case; CopperStart's adapter implements the sidecar below and
        // requires every complete plane span to be chip-DMA addressable.
        if (memory is not IGraphicsDisplayMemory displayMemory)
            return true;

        var byteCount = GetPlaneTouchedSpan(
            guestBytesPerRow,
            planeBytesPerRow,
            rows,
            flags);
        if (rows == 0 || byteCount == 0 || byteCount > uint.MaxValue)
            return false;

        for (var plane = 0; plane < planeCount; plane++)
        {
            if (!displayMemory.IsDisplayDmaRange(planes[plane], (uint)byteCount))
                return false;
        }

        return true;
    }

    /// <summary>Releases a standard planar bitmap created by AllocBitMap.</summary>
    internal static int FreeBitMap(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint bitMap,
        uint[]? planeBuffer = null,
        BitMapAllocation? expectedAllocation = null)
    {
        if (!TryReadAllocatedBitMap(
                memory,
                bitMap,
                out var bytesPerRow,
                out var rows,
                out var depth,
                out var flags,
                out var planes,
                planeBuffer))
            return Failure;

        // The public BitMap header is writable guest state. Its validated
        // values must still match the allocation contract captured by the
        // owning graphics.library instance before they can select allocator
        // byte counts or ownership classes.
        if (expectedAllocation is not null &&
            !expectedAllocation.Matches(
                bytesPerRow,
                rows,
                depth,
                flags,
                planes))
        {
            return Failure;
        }

        var planeBytes = (uint)bytesPerRow * rows;
        if ((flags & BmfInterleaved) != 0)
        {
            if (planes[0] != 0)
                allocator.Free(planes[0], planeBytes * depth, GraphicsMemoryClass.Chip);
        }
        else
        {
            for (var plane = 0; plane < depth; plane++)
            {
                if (planes[plane] != 0)
                    allocator.Free(planes[plane], planeBytes, GraphicsMemoryClass.Chip);
            }
        }

        allocator.Free(
            bitMap,
            GetBitmapStructureBytes(depth, flags),
            GraphicsMemoryClass.Public);
        return Success;
    }

    private static bool TryCaptureAllocatedBitMap(
        IGraphicsMemory memory,
        uint bitMap,
        out BitMapAllocation allocation)
    {
        allocation = null!;
        if (!TryReadAllocatedBitMap(
                memory,
                bitMap,
                out var bytesPerRow,
                out var rows,
                out var depth,
                out var flags,
                out var planes))
        {
            return false;
        }

        allocation = new BitMapAllocation(
            bytesPerRow,
            rows,
            depth,
            flags,
            (uint[])planes.Clone());
        return true;
    }

    private static bool TryGetBitmapGeometry(
        uint width,
        uint height,
        uint depth,
        ushort minimumBytesPerRow,
        out ushort bytesPerRow,
        out ushort rows,
        out uint planeBytes)
    {
        bytesPerRow = 0;
        rows = 0;
        planeBytes = 0;
        if (width == 0 || height == 0 || depth == 0 || depth > 8 || height > ushort.MaxValue)
            return false;

        var requestedBytes = (((ulong)width + 15UL) >> 4) * 2UL;
        var bytes = Math.Max(requestedBytes, minimumBytesPerRow);
        var total = bytes * height;
        if (bytes == 0 || bytes > ushort.MaxValue || total > uint.MaxValue)
            return false;

        bytesPerRow = (ushort)bytes;
        rows = (ushort)height;
        planeBytes = (uint)total;
        return true;
    }

    internal static bool TryGetPlaneBytesPerRow(
        ushort bytesPerRow,
        byte depth,
        byte flags,
        out ushort planeBytesPerRow)
    {
        planeBytesPerRow = bytesPerRow;
        if ((flags & BmfInterleaved) == 0)
        {
            // AllocBitMap publishes BMF_STANDARD bitplanes on the word
            // boundary required by the chipset. Preserve compact legacy
            // layouts, whose odd byte stride is handled by the native path,
            // but never claim a malformed standard planar endpoint.
            return bytesPerRow != 0 &&
                ((flags & BmfStandard) == 0 || (bytesPerRow & 1) == 0);
        }

        // V39 interleaved BitMaps publish the complete row-to-row stride in
        // BytesPerRow.  Each plane still advances by one physical plane row,
        // so the portable renderer derives that stride before addressing the
        // linked plane pointers.
        if (depth == 0 || bytesPerRow == 0 || bytesPerRow % depth != 0)
            return false;

        var perPlane = bytesPerRow / depth;
        if (perPlane == 0 || (perPlane & 1) != 0)
            return false;

        planeBytesPerRow = (ushort)perPlane;
        return true;
    }

    private static bool IsInterleaved(byte flags)
        => (flags & BmfInterleaved) != 0;

    private static uint GetPlaneRowStride(
        ushort guestBytesPerRow,
        ushort planeBytesPerRow,
        byte flags)
        => IsInterleaved(flags) ? guestBytesPerRow : planeBytesPerRow;

    internal static int GetBitmapPlaneRowStride(BitmapInfo bitmap)
        => checked((int)GetPlaneRowStride(
            checked((ushort)bitmap.GuestBytesPerRow),
            checked((ushort)bitmap.PlaneBytesPerRow),
            bitmap.Flags));

    private static ulong GetPlaneTouchedSpan(
        ushort guestBytesPerRow,
        ushort planeBytesPerRow,
        ushort rows,
        byte flags)
    {
        if (rows == 0 || planeBytesPerRow == 0)
            return 0;

        return ((ulong)(rows - 1) * GetPlaneRowStride(
                   guestBytesPerRow,
                   planeBytesPerRow,
                   flags)) + planeBytesPerRow;
    }

    internal static ulong GetBitmapPlaneTouchedSpan(BitmapInfo bitmap)
        => GetPlaneTouchedSpan(
            checked((ushort)bitmap.GuestBytesPerRow),
            checked((ushort)bitmap.PlaneBytesPerRow),
            checked((ushort)bitmap.Rows),
            bitmap.Flags);

    /// <summary>
    /// Computes the classic <c>RASSIZE(width,height)</c> macro result. The
    /// public macro uses 32-bit ULONG arithmetic: rows are rounded up to a
    /// 16-pixel word boundary and the final multiplication wraps at the
    /// guest width. It is a pure layout helper and does not validate or
    /// allocate guest memory.
    /// </summary>
    internal static uint RasSize(uint width, uint height)
        => unchecked((((width + 15u) >> 3) & 0xFFFEu) * height);

    private static uint GetBitmapStructureBytes(byte depth, uint flags)
    {
        if ((flags & BmfMinPlanes) == 0)
            return (uint)GraphicsLayouts.BitMapSize;

        return (uint)GraphicsLayouts.BitMapPlanes + ((uint)depth * 4u);
    }

    private static bool TryReadAllocatedBitMap(
        IGraphicsMemory memory,
        uint bitMap,
        out ushort bytesPerRow,
        out ushort rows,
        out byte depth,
        out byte flags,
        out uint[] planes,
        uint[]? planeBuffer = null)
    {
        bytesPerRow = 0;
        rows = 0;
        depth = 0;
        flags = 0;
        planes = Array.Empty<uint>();
        // FreeBitMap consumes WORD/LONG fields from the public header.  A
        // byte-addressable host can expose an odd header, but a native 68000
        // would take an address error before any allocator ownership could
        // be interpreted.  Keep that malformed envelope available to the
        // native/provider owner and do not release a guessed span.
        if (bitMap == 0 || (bitMap & 1u) != 0 ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapBytesPerRow,
                sizeof(ushort),
                out var bytesPerRowAddress) ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapRows,
                sizeof(ushort),
                out var rowsAddress) ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapFlags,
                sizeof(byte),
                out var flagsAddress) ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapDepth,
                sizeof(byte),
                out var depthAddress) ||
            !memory.TryReadWord(bytesPerRowAddress, out bytesPerRow) ||
            !memory.TryReadWord(rowsAddress, out rows) ||
            !memory.TryReadByte(flagsAddress, out flags) ||
            !memory.TryReadByte(depthAddress, out depth) ||
            bytesPerRow == 0 || rows == 0 || depth == 0 || depth > 8)
        {
            return false;
        }

        // FreeBitMap is only valid for a standard planar allocation returned
        // by this graphics path.  A registered guest header can still be
        // corrupted after allocation, so do not turn its fields into
        // unchecked allocator spans.  Probe the complete public envelope
        // before inspecting or releasing any plane storage.
        if ((flags & BmfStandard) == 0)
            return false;

        if (!TryGetPlaneBytesPerRow(
                bytesPerRow,
                depth,
                flags,
                out var planeBytesPerRow))
        {
            return false;
        }

        var structureBytes = GetBitmapStructureBytes(depth, flags);
        if (structureBytes > int.MaxValue ||
            !TryProbeRange(memory, bitMap, (int)structureBytes))
        {
            return false;
        }

        planes = planeBuffer ?? new uint[depth];
        if (planes.Length < depth)
            return false;
        Array.Clear(planes, 0, depth);
        for (var plane = 0; plane < depth; plane++)
        {
            if (!TryAddress(
                    bitMap,
                    GraphicsLayouts.BitMapPlanes + (plane * sizeof(uint)),
                    sizeof(uint),
                    out var planeAddress) ||
                !memory.TryReadLong(planeAddress, out planes[plane]))
            {
                planes = Array.Empty<uint>();
                return false;
            }
        }

        var planeBytes = (ulong)planeBytesPerRow * rows;
        if (planeBytes == 0 || planeBytes > uint.MaxValue)
            return false;

        // Every plane owned by AllocBitMap must still describe a readable
        // complete raster.  This preserves the ownership token on malformed
        // headers and prevents a forged NULL, sentinel, or wrapped pointer
        // from reaching the allocator.  Interleaved allocations additionally
        // require the exact contiguous plane sequence that the allocator
        // published.
        for (var plane = 0; plane < depth; plane++)
        {
            if (planes[plane] == 0 ||
                ((flags & BmfInterleaved) != 0
                    ? !TryProbeContiguousRows(
                        memory,
                        planes[plane],
                        bytesPerRow,
                        rows,
                        planeBytesPerRow)
                    : !TryProbeContiguous(memory, planes[plane], planeBytesPerRow, rows)))
            {
                return false;
            }
        }

        if ((flags & BmfInterleaved) != 0)
        {
            var firstPlane = planes[0];
            for (var plane = 1; plane < depth; plane++)
            {
                var expected = (ulong)firstPlane + ((ulong)plane * planeBytesPerRow);
                if (expected > uint.MaxValue || planes[plane] != (uint)expected)
                    return false;
            }
        }

        // Interleaved storage is one contiguous chip allocation shared by all
        // declared planes.  Validate the complete release span before
        // multiplying in FreeBitMap; otherwise a malformed guest header can
        // wrap the allocator byte count and release the wrong range.
        if ((flags & BmfInterleaved) != 0 &&
            planeBytes * depth > uint.MaxValue)
        {
            return false;
        }

        // The caller releases one physical plane span at a time (or the
        // complete interleaved block), so expose the derived per-plane row
        // stride rather than the public interleaved aggregate.
        bytesPerRow = planeBytesPerRow;
        return true;
    }

    private static void FreeAllocatedBitMap(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint bitMap,
        ushort bytesPerRow,
        ushort rows,
        byte depth,
        byte flags,
        uint[] planes)
    {
        _ = memory;
        var planeBytes = (uint)bytesPerRow * rows;
        if ((flags & BmfInterleaved) != 0)
        {
            if (planes.Length > 0 && planes[0] != 0)
                allocator.Free(planes[0], planeBytes * depth, GraphicsMemoryClass.Chip);
        }
        else
        {
            foreach (var plane in planes)
            {
                if (plane != 0)
                    allocator.Free(plane, planeBytes, GraphicsMemoryClass.Chip);
            }
        }

        allocator.Free(
            bitMap,
            GetBitmapStructureBytes(depth, flags),
            GraphicsMemoryClass.Public);
    }

    private static bool ClearGuestMemory(IGraphicsMemory memory, uint address, int byteCount)
    {
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(address + (uint)offset, 0))
                return false;
        }

        return true;
    }

    private static bool ClearPlaneMemory(IGraphicsMemory memory, uint address, uint byteCount)
    {
        if (!TryGetGuestSpan(address, byteCount))
            return false;

        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(address + offset, 0))
                return false;
        }

        return true;
    }

    private static bool TryGetRasterByteCount(uint width, uint height, out uint byteCount)
    {
        byteCount = 0;
        if (width == 0 || height == 0)
            return false;

        var wordsPerRow = ((ulong)width + 15UL) >> 4;
        var bytes = wordsPerRow * 2UL * height;
        if (bytes == 0 || bytes > uint.MaxValue)
            return false;

        byteCount = (uint)bytes;
        return true;
    }

    internal static int SetAPen(
        IGraphicsMemory memory,
        uint rastPort,
        uint pen,
        byte[]? oldMintermBuffer = null,
        byte[]? nextMintermBuffer = null)
        => SetByteAndRestartPattern(
            memory,
            rastPort,
            GraphicsLayouts.RastPortFgPen,
            unchecked((byte)pen),
            oldMintermBuffer,
            nextMintermBuffer);

    internal static int SetBPen(IGraphicsMemory memory, uint rastPort, uint pen)
        => SetByteAndRestartPattern(
            memory,
            rastPort,
            GraphicsLayouts.RastPortBgPen,
            unchecked((byte)pen));

    internal static int SetDrawMode(IGraphicsMemory memory, uint rastPort, uint drawMode)
        => SetByteAndRestartPattern(
            memory,
            rastPort,
            GraphicsLayouts.RastPortDrawMode,
            unchecked((byte)drawMode));

    /// <summary>
    /// Implements the classic <c>SetDrPt</c> graphics macro at the shared
    /// guest-layout boundary.  The macro is not an LVO: it publishes the
    /// line pattern, marks the next segment as a fresh line, and resets the
    /// preshift to the first pattern bit.  Keeping the three writes
    /// transactional gives host callers the same rollback contract as the
    /// portable line vectors while leaving the layout directly usable by a
    /// future CopperSharp68k body.
    /// </summary>
    internal static int SetDrPt(
        IGraphicsMemory memory,
        uint rastPort,
        ushort linePattern)
    {
        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePattern,
                out var oldPattern) ||
            !TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var oldFlags) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out var oldPatternCount) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePattern,
                sizeof(ushort)) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                sizeof(ushort)) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte)))
        {
            return Failure;
        }

        if (TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePattern,
                linePattern) &&
            TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                (ushort)(oldFlags | GraphicsLayouts.RastPortFirstDot)) &&
            TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                15))
        {
            return Success;
        }

        // The line-pattern and Flags WORDs may each accept only their high
        // byte before the adapter rejects the publication. Restore them
        // byte-wise so a repeated WORD rejection cannot leave a new pattern
        // paired with stale FRST_DOT state (or vice versa).
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortLinePattern,
            new[] { (byte)(oldPattern >> 8), (byte)oldPattern });
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortFlags,
            new[] { (byte)(oldFlags >> 8), (byte)oldFlags });
        _ = TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortLinePatternCount,
            oldPatternCount);
        return Failure;
    }

    /// <summary>
    /// Implements the classic <c>SetAfPt</c> graphics macro at the shared
    /// guest-layout boundary.  The macro only publishes the area-pattern
    /// pointer and its signed height exponent; it does not dereference the
    /// pattern table.  Keep both fields in one small transaction so a
    /// byte-addressable host that rejects the second publication cannot leave
    /// a new pointer paired with the old pattern size.
    /// </summary>
    internal static int SetAfPt(
        IGraphicsMemory memory,
        uint rastPort,
        uint areaPattern,
        sbyte areaPatternSize)
    {
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtrn,
                out var oldAreaPattern) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtSz,
                out var oldAreaPatternSize) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortAreaPtrn,
                sizeof(uint),
                out _) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortAreaPtSz,
                sizeof(byte),
                out _))
        {
            return Failure;
        }

        if (TryWriteRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtrn,
                areaPattern) &&
            TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtSz,
                unchecked((byte)areaPatternSize)))
        {
            return Success;
        }

        // The area-pattern pointer is a guest APTR LONG and may have
        // accepted only a prefix before the adapter rejects the publication.
        // Restore it byte-wise so a repeated LONG rejection cannot leave a
        // new pointer paired with the old pattern-size byte.
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortAreaPtrn,
            new[]
            {
                (byte)(oldAreaPattern >> 24),
                (byte)(oldAreaPattern >> 16),
                (byte)(oldAreaPattern >> 8),
                (byte)oldAreaPattern
            });
        _ = TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortAreaPtSz,
            oldAreaPatternSize);
        return Failure;
    }

    /// <summary>
    /// Implements the classic <c>SetOPen</c> graphics macro. Unlike the V39
    /// <c>SetOutlinePen</c> vector this helper has no return value and
    /// directly enables the area-outline flag while publishing AOlPen.
    /// </summary>
    internal static int SetOPen(
        IGraphicsMemory memory,
        uint rastPort,
        byte outlinePen)
    {
        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                out var oldOutlinePen) ||
            !TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var oldFlags) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                sizeof(byte),
                out _) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortFlags,
                sizeof(ushort),
                out _))
        {
            return Failure;
        }

        if (TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                outlinePen) &&
            TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                (ushort)(oldFlags | GraphicsLayouts.RastPortAreaOutline)))
        {
            return Success;
        }

        _ = TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortOutlinePen,
            oldOutlinePen);
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortFlags,
            new[] { (byte)(oldFlags >> 8), (byte)oldFlags });
        return Failure;
    }

    /// <summary>
    /// Implements the classic <c>BNDRYOFF</c> macro by clearing only the
    /// public area-outline flag. Pen, pattern, and unrelated private flags
    /// remain untouched.
    /// </summary>
    internal static int BoundaryOff(
        IGraphicsMemory memory,
        uint rastPort)
    {
        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var oldFlags) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortFlags,
                sizeof(ushort),
                out _))
        {
            return Failure;
        }

        var nextFlags = (ushort)(oldFlags & ~GraphicsLayouts.RastPortAreaOutline);
        if (TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                nextFlags))
        {
            return Success;
        }

        // A host memory adapter may reject the second byte of the WORD
        // publication after the first byte has already landed. Restore the
        // original Flags bytes independently so a repeated WORD rejection
        // cannot leave the area-outline state half-cleared.
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortFlags,
            new[] { (byte)(oldFlags >> 8), (byte)oldFlags });
        return Failure;
    }

    /// <summary>
    /// Implements the classic <c>SetWrMsk</c> graphics macro at the shared
    /// guest-layout boundary.  The macro assigns the UBYTE RastPort mask
    /// directly, so ULONG callers are narrowed to the low guest byte and no
    /// bitmap or plane is inspected.
    /// </summary>
    internal static int SetWrMsk(
        IGraphicsMemory memory,
        uint rastPort,
        uint writeMask)
        => TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                unchecked((byte)writeMask))
            ? Success
            : Failure;

    internal static int GetAPen(IGraphicsMemory memory, uint rastPort)
        => TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFgPen,
                out var value)
            ? value
            : Failure;

    internal static int GetBPen(IGraphicsMemory memory, uint rastPort)
        => TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortBgPen,
                out var value)
            ? value
            : Failure;

    internal static int GetDrawMode(IGraphicsMemory memory, uint rastPort)
        => TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortDrawMode,
                out var value)
            ? value
            : Failure;

    internal static int SetABPenDrMd(
        IGraphicsMemory memory,
        uint rastPort,
        uint foreground,
        uint background,
        uint drawMode)
    {
        // Probe every scalar destination before changing guest state.  The
        // line phase write is part of this vector's observable update, so a
        // truncated RastPort must fail without leaving only the pen/mode
        // fields partially changed.  The derived minterm cache is checked
        // for an addressable publication span below, not read-probed.
        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFgPen,
                out var oldForeground) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortBgPen,
                out var oldBackground) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortDrawMode,
                out var oldDrawMode) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out var oldPatternCount) ||
            !TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var oldFlags) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortMinterms,
                8,
                out _))
        {
            return Failure;
        }

        // The eight minterms are a private cache derived from the three
        // public pen/mode bytes.  Kickstart regenerates them; it does not
        // consume their old contents.  Keep the exact native read envelope
        // by accepting a write-valid cache whose provider intentionally
        // faults reads, while still retaining a rollback image whenever the
        // cache is readable.  If it is not readable, reconstruct the old
        // image from the scalar fields before publishing the transition.
        var oldMinterms = new byte[8];
        if (!TryReadRastPortMinterms(memory, rastPort, out _, oldMinterms))
            ComputeMinterms(oldForeground, oldBackground, oldDrawMode, oldMinterms);

        var foregroundByte = unchecked((byte)foreground);
        var backgroundByte = unchecked((byte)background);
        var drawModeByte = unchecked((byte)drawMode);
        var nextFlags = (ushort)(oldFlags & ~GraphicsLayouts.RastPortNoPens);
        var nextMinterms = ComputeMinterms(foregroundByte, backgroundByte, drawModeByte);

        if (!TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFgPen,
                foregroundByte) ||
            !TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortBgPen,
                backgroundByte) ||
            !TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortDrawMode,
                drawModeByte) ||
            !WriteRastPortMinterms(memory, rastPort, nextMinterms) ||
            !RestartLinePattern(memory, rastPort) ||
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                nextFlags))
        {
            _ = TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortFgPen, oldForeground);
            _ = TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortBgPen, oldBackground);
            _ = TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortDrawMode, oldDrawMode);
            _ = TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortLinePatternCount, oldPatternCount);
            // The final Flags publication is a WORD and may have accepted
            // only its high byte before declining. Restore it byte-wise so a
            // repeated WORD rejection cannot leave the NoPens/FRST_DOT state
            // half-updated.
            Restore(
                memory,
                rastPort + (uint)GraphicsLayouts.RastPortFlags,
                new[] { (byte)(oldFlags >> 8), (byte)oldFlags });
            _ = WriteRastPortMinterms(memory, rastPort, oldMinterms);
            return Failure;
        }

        return Success;
    }

    internal static int SetWriteMask(IGraphicsMemory memory, uint rastPort, uint writeMask)
        => TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                unchecked((byte)writeMask))
            ? Success
            : Failure;

    /// <summary>
    /// Applies the classic planar SetMaxPen optimization. RastPort has no
    /// public max-pen field: a nonzero request publishes the complete
    /// contiguous mask needed to represent the requested pen range, while a
    /// zero request leaves the current mask untouched. Keeping this as a pure
    /// guest-memory operation makes the behavior available to both the host
    /// bridge and a future native 68k implementation.
    /// </summary>
    internal static int SetMaxPen(IGraphicsMemory memory, uint rastPort, uint maxPen)
    {
        // Kickstart's SetMaxPen implementation returns immediately for
        // maxpen == 0, before it reads or writes RastPort->Mask.  Preserve
        // that field-free no-op at the portable/native boundary: a caller
        // may use the zero request while a RastPort is still being assembled,
        // and an odd, truncated, or provider-owned envelope must not be
        // claimed merely because the operation cannot consume it.
        if (maxPen == 0)
            return Success;

        // The classic vector's ULONG input is defined only for pen values
        // 1..255.  Do not silently narrow a larger request to byte.MaxValue:
        // that would claim a malformed/native-owned call and publish a mask
        // the caller did not actually request.  Keep the invalid envelope
        // available to a native or provider implementation instead.
        if (maxPen > byte.MaxValue)
            return Failure;

        // The resident routine does not consume the previous Mask value for
        // a nonzero request; it computes the contiguous mask from maxpen and
        // stores that byte directly. Keep the ownership boundary write-only
        // so a readable-but-writable faulting Mask field cannot make the
        // portable path steal a call from native/provider ownership.
        return TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                MaxPenMask(maxPen))
            ? Success
            : Failure;
    }

    /// <summary>
    /// Returns the exclusive pen limit representable by the current planar
    /// write mask.  RPTAG_MaxPen reports the number of contiguous pen values,
    /// not the highest pen index: a zero mask is the 256-pen sentinel and a
    /// two-plane mask (0x03) reports four.
    /// </summary>
    internal static int GetMaxPen(IGraphicsMemory memory, uint rastPort)
    {
        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return Failure;
        }

        return MaxPenMaskToLimit(writeMask);
    }

    private static byte MaxPenMask(uint maxPen)
    {
        if (maxPen >= 0xFF)
            return 0xFF;

        var value = (byte)maxPen;
        var mask = 1;
        while (value > 1)
        {
            value >>= 1;
            mask = (mask << 1) | 1;
        }

        return (byte)mask;
    }

    private static int MaxPenMaskToLimit(byte writeMask)
    {
        // The resident GetRPAttrsA implementation treats an empty mask as
        // the all-planes/256-pen sentinel, then doubles once per set mask
        // bit position.  This is intentionally one past the highest pen
        // index, matching the documented SetMaxPen range contract.
        var limit = 1;
        var value = writeMask;
        if (value == 0)
            return 0x100;

        while (value != 0)
        {
            value >>= 1;
            limit <<= 1;
        }

        return limit;
    }

    internal static int GetOutlinePen(IGraphicsMemory memory, uint rastPort)
        => TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                out var value)
            ? value
            : Failure;

    /// <summary>
    /// Compatibility spelling for the pre-V39 <c>GetOPen</c> helper.  The
    /// V39 public vector is named <c>GetOutlinePen</c>, but classic
    /// gfxmacros/native callers still use the shorter name.  Keep one guest
    /// read envelope so the alias has identical alignment and failure
    /// ownership without adding a second LVO.
    /// </summary>
    internal static int GetOPen(IGraphicsMemory memory, uint rastPort)
        => GetOutlinePen(memory, rastPort);

    internal static int SetOutlinePen(IGraphicsMemory memory, uint rastPort, uint pen)
    {
        // SetOutlinePen consumes only AOlPen and the Flags word.  Probe those
        // exact destinations before changing either one so a truncated guest
        // RastPort cannot publish a new outline pen while the area-outline
        // flag write fails, without claiming unrelated DrawMode/line-state
        // bytes that the native routine never reads.
        if (!TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                sizeof(byte)) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                sizeof(ushort)))
        {
            return Failure;
        }

        byte oldPen = 0;
        ushort flags = 0;
        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                out oldPen) ||
            !TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out flags) ||
            !TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                unchecked((byte)pen)) ||
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                (ushort)(flags | GraphicsLayouts.RastPortAreaOutline)))
        {
            _ = TryWriteRastPortByte(memory, rastPort, GraphicsLayouts.RastPortOutlinePen, oldPen);
            // The Flags WORD may have accepted only its high byte before
            // declining. Restore it byte-wise so repeated-WORD rejection on
            // the bridge cannot leave the area-outline bit half-published.
            Restore(
                memory,
                rastPort + (uint)GraphicsLayouts.RastPortFlags,
                new[] { (byte)(flags >> 8), (byte)flags });
            return Failure;
        }

        return oldPen;
    }

    /// <summary>
    /// Implements the historical <c>SetAOlPen</c> spelling used by
    /// compatibility headers. It is a macro-level helper, not a second
    /// public LVO: the guest operation is the same transactional outline-pen
    /// update as <c>SetOutlinePen</c>, including its previous-pen result.
    /// </summary>
    internal static int SetAOlPen(IGraphicsMemory memory, uint rastPort, uint pen)
        => SetOutlinePen(memory, rastPort, pen);

    internal static int Move(IGraphicsMemory memory, uint rastPort, short x, short y)
    {
        // Native Move() publishes cp_x, cp_y, linpatcnt, and FRST_DOT.  It
        // only consumes the old Flags word for the read/modify/write of
        // FRST_DOT; the old cursor and pattern phase are not inputs.  Keep
        // those three destinations address-validated rather than making a
        // read-faulting but writable RastPort fall through to native/provider
        // ownership merely because the portable rollback journal cannot read
        // its prior bytes.
        if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                sizeof(ushort),
                out _) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                sizeof(ushort),
                out _) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte),
                out _) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                sizeof(ushort)))
        {
            return Failure;
        }

        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var oldFlags))
        {
            return Failure;
        }

        // The old cursor/phase bytes are a best-effort rollback journal only;
        // an otherwise valid native Move() does not consume them.  Preserve
        // exact rollback for ordinary readable guest memory while allowing a
        // provider to expose write-valid, read-faulting cursor storage.
        ushort oldX = 0;
        ushort oldY = 0;
        byte oldPatternCount = 0;
        var haveCursorSnapshot =
            TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                out oldX) &&
            TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                out oldY) &&
            TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out oldPatternCount);

        if (!TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                unchecked((ushort)x)) ||
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                unchecked((ushort)y)) ||
            !TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                15) ||
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                (ushort)(oldFlags | GraphicsLayouts.RastPortFirstDot)))
        {
            if (haveCursorSnapshot)
            {
                // A failing WORD may already have published its high byte.
                // Restore each captured field byte-wise so a bridge that
                // rejects the repeated WORD used by a conventional rollback
                // cannot leave Move's cursor or phase half-updated.
                Restore(
                    memory,
                    rastPort + (uint)GraphicsLayouts.RastPortCurrentX,
                    new[] { (byte)(oldX >> 8), (byte)oldX });
                Restore(
                    memory,
                    rastPort + (uint)GraphicsLayouts.RastPortCurrentY,
                    new[] { (byte)(oldY >> 8), (byte)oldY });
                Restore(
                    memory,
                    rastPort + (uint)GraphicsLayouts.RastPortLinePatternCount,
                    new[] { oldPatternCount });
            }

            Restore(
                memory,
                rastPort + (uint)GraphicsLayouts.RastPortFlags,
                new[] { (byte)(oldFlags >> 8), (byte)oldFlags });
            return Failure;
        }

        return Success;
    }

    internal static int Draw(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        VisibilityScratch? visibilityScratch = null,
        bool consumeFirstDot = true)
    {
        if (pixelVisible is null)
        {
            return DrawCore(memory, rastPort, x, y, null,
                snapshotAddressBuffer, snapshotValueBuffer, consumeFirstDot);
        }

        var scratch = visibilityScratch ?? new VisibilityScratch();
        if (!scratch.TryAcquire(pixelVisible, out var stablePixelVisible))
            return Failure;
        try
        {
            return DrawCore(memory, rastPort, x, y, stablePixelVisible,
                snapshotAddressBuffer, snapshotValueBuffer, consumeFirstDot);
        }
        finally
        {
            scratch.Release();
        }
    }

    private static int DrawCore(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        Func<int, int, bool>? pixelVisible,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer,
        bool consumeFirstDot = true)
    {
        // A layered RastPort belongs to layers.library (or an explicit host
        // provider).  The portable planar path must not silently draw into
        // the backing bitmap without that clipping/damage boundary.  The
        // register adapter normally routes this case before reaching Draw,
        // but keep the pure/core entry point equally fail-closed so a future
        // CopperSharp68k caller cannot bypass the ownership split.
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return Failure;
        }

        // A private-colour RastPort is owned by the native/provider renderer.
        // Admit that boundary before reading the public draw mode, mask, or
        // bitmap fields; those bytes are not part of this planar operation's
        // colour contract and may be unavailable while the provider owns the
        // port.
        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags) ||
            (flags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return Failure;
        }

        if (!TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                out var x0) ||
            !TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                out var y0) ||
            !TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return Failure;
        }

        var drawMode = GetDrawMode(memory, rastPort);
        if (drawMode < 0 ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask) ||
            !TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePattern,
                out var linePattern) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out var patternCount))
            return Failure;

        // A zero effective mask is a successful cursor/line-state operation
        // but cannot publish a destination pixel.  Do not make an unrelated
        // public pen byte part of that admission envelope: sparse/native
        // callers may legitimately omit FgPen/BgPen when no plane is
        // selected.  Keep the original mask value for the shared state
        // transition and collapse it only after the bitmap depth is known.
        var effectiveMask = EffectiveWriteMask(bitmap, writeMask);

        // SetBitmapPixel ignores the colour in COMPLEMENT mode. Keep FgPen
        // and BPen behind the same field-level ownership boundary as the
        // other patterned primitives: a valid line with no visible sample
        // of that source must not consume an unrelated public pen byte.
        // Resolve each pen lazily at the first visible sample that needs it;
        // a late unreadable pen still rolls back the complete line below.
        var color = 0;
        var foregroundLoaded = effectiveMask == 0 ||
            (drawMode & DrawModeComplement) != 0;
        var background = 0;
        var backgroundLoaded = effectiveMask == 0 ||
            (drawMode & DrawModeComplement) != 0 ||
            (drawMode & DrawModeJam2) == 0;

        var success = true;
        var currentX = (int)x0;
        var currentY = (int)y0;
        var targetX = (int)x;
        var targetY = (int)y;
        var originalPatternCount = patternCount;
        var dx = Math.Abs(targetX - currentX);
        var sx = currentX < targetX ? 1 : -1;
        var dy = -Math.Abs(targetY - currentY);
        var sy = currentY < targetY ? 1 : -1;
        var error = dx + dy;
        var majorDistance = Math.Max(dx, -dy);
        // Move()/SetDrPt() mark a fresh line with FRST_DOT.  The flag is
        // normally consumed after that first line so connected Draw() calls
        // do not touch the shared endpoint a second time (observable in
        // COMPLEMENT mode). The clipping owner can retain it for a cut line;
        // that policy does not alter the incoming shared-dot semantics or
        // the full logical phase/cursor advance. linpatcnt is the current
        // preshift: SetDrPt() publishes 15
        // for the first LinePtrn bit, while InitRastPort's all-ones pattern
        // is phase-independent.
        var firstDotRequested = (flags & GraphicsLayouts.RastPortFirstDot) != 0;
        // FRST_DOT is the sole ownership marker for the segment start.  The
        // current pattern phase may legitimately be zero after a 16-pixel
        // segment; that does not turn a connected Draw into a fresh line.
        var connectedSharedDot = !firstDotRequested;
        var oneDotMode = (flags & GraphicsLayouts.RastPortOneDot) != 0;
        var majorAxisIsX = dx >= -dy;

        // Draw updates one logical line, including its pattern phase and
        // current position.  Preflight the complete raster traversal and the
        // guest state fields that are written after it so a malformed late
        // row cannot expose a partially drawn line.
        var lineSnapshotAddresses = snapshotAddressBuffer ?? new List<uint>();
        var lineSnapshotValues = snapshotValueBuffer ?? new List<byte>();
        lineSnapshotAddresses.Clear();
        lineSnapshotValues.Clear();
        if (!TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                1) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                GraphicsLayouts.RastPortCurrentY + 2 - GraphicsLayouts.RastPortCurrentX) ||
            (firstDotRequested && consumeFirstDot &&
             !TryRastPortRange(
                 memory,
                 rastPort,
                 GraphicsLayouts.RastPortFlags,
                 2)) ||
            !TryPreflightLineDestination(
                memory,
                bitmap,
                currentX,
                currentY,
                targetX,
                targetY,
                drawMode,
                writeMask,
                linePattern,
                patternCount,
                oneDotMode,
                majorAxisIsX,
                lineSnapshotAddresses,
                lineSnapshotValues,
                pixelVisible: pixelVisible))
        {
            return Failure;
        }

        // A zero destination mask still consumes the connected segment's
        // line-state transition, but no raster sample can be observed. Skip
        // the potentially 65K-step Bresenham walk and publish only the same
        // FRST_DOT, line-pattern phase, and current-position fields that a
        // real Draw would commit. Keep the writes transactional so a truncated
        // RastPort remains available to the native/provider owner.
        writeMask = effectiveMask;
        if (writeMask == 0)
        {
            var nextPatternCount = unchecked((byte)((patternCount - majorDistance) & 0x0F));
            if (firstDotRequested && consumeFirstDot &&
                !TryWriteRastPortWord(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortFlags,
                    (ushort)(flags & ~GraphicsLayouts.RastPortFirstDot)))
            {
                return Failure;
            }

            if (!TryWriteRastPortByte(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortLinePatternCount,
                    nextPatternCount) ||
                !TryWriteRastPortWord(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortCurrentX,
                    unchecked((ushort)x)) ||
                !TryWriteRastPortWord(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortCurrentY,
                    unchecked((ushort)y)))
            {
                RestoreDrawTransaction(
                    memory,
                    rastPort,
                    flags,
                    originalPatternCount,
                    (short)x0,
                    (short)y0,
                    Array.Empty<uint>(),
                    Array.Empty<byte>());
                return Failure;
            }

            return Success;
        }

        var lastRaster = int.MinValue;
        var firstPoint = true;
        while (true)
        {
            // linpatcnt is the current preshift, not a count of emitted
            // samples.  SetDrPt() initializes it to 15, so the first dot
            // consumes LinePtrn bit 15; connected segments continue from the
            // endpoint phase.  The phase is advanced once per major-axis
            // raster, not once for the shared endpoint.
            var majorStep = Math.Max(Math.Abs(currentX - (int)x0), Math.Abs(currentY - (int)y0));
            var patternBit = (linePattern & (1 << ((patternCount - majorStep) & 0x0F))) != 0;
            if ((drawMode & 4) != 0)
                patternBit = !patternBit;
            // The blitter's ONE_DOT mode guarantees one pixel per horizontal
            // raster.  Keep the explicit guard even though the normal
            // Bresenham walk already has one sample per major-axis step; the
            // horizontal-row predicate is observable on shallow lines.
            // RastPort ONE_DOT is the blitter line-mode SING/ONEDOT bit:
            // it emits at most one destination dot for each horizontal
            // raster line, not one dot for each selected major axis.  Keep
            // the traversal and pattern phase intact for skipped samples;
            // only the destination publication is suppressed.
            var raster = oneDotMode ? currentY : (majorAxisIsX ? currentX : currentY);
            var duplicateRaster = oneDotMode && raster == lastRaster;
            if (!duplicateRaster)
            {
                if (patternBit)
                {
                    var destinationVisible = IsVisibleDestination(
                        bitmap, pixelVisible, currentX, currentY);
                    if (destinationVisible && !TryEnsureIntegerPen(
                            memory, rastPort, true, ref color,
                            ref foregroundLoaded))
                    {
                        success = false;
                        break;
                    }

                    if (destinationVisible)
                    {
                        success &= SetBitmapPixel(
                            memory,
                            bitmap,
                            currentX,
                            currentY,
                            color,
                            drawMode,
                            writeMask);
                    }
                }
                else if ((drawMode & DrawModeJam2) != 0)
                {
                    var destinationVisible = IsVisibleDestination(
                        bitmap, pixelVisible, currentX, currentY);
                    if (destinationVisible &&
                        (drawMode & DrawModeComplement) == 0 &&
                        !TryEnsureIntegerPen(
                            memory, rastPort, false, ref background,
                            ref backgroundLoaded))
                    {
                        success = false;
                        break;
                    }

                    // JAM2 supplies BPen for source-zero samples.  Preserve
                    // COMPLEMENT while doing so: COMPLEMENT|JAM2 is the
                    // native whole-line inversion mode, so both foreground
                    // and background samples toggle the destination.
                    if (destinationVisible)
                    {
                        success &= SetBitmapPixel(
                            memory,
                            bitmap,
                            currentX,
                            currentY,
                            background,
                            drawMode & 2,
                            writeMask);
                    }
                }

                // Native Draw() renders the shared vertex even for a
                // connected segment.  In COMPLEMENT mode it then undoes that
                // one toggle when the segment actually writes the dot,
                // leaving the endpoint owned by the previous segment.
                // Non-COMPLEMENT connected lines intentionally keep the
                // write, so a caller changing pens between segments still
                // observes the native endpoint update.
                // A connected vertex belongs to the preceding segment.  In
                // COMPLEMENT mode the first sample is therefore undone after
                // the normal write, including JAM2 source-zero samples that
                // come from BPen when the line pattern bit is clear.
                if (firstPoint &&
                    connectedSharedDot &&
                    (drawMode & 2) != 0 &&
                    (patternBit || (drawMode & 1) != 0))
                {
                    // The normal sample above already established that this
                    // logical cell is visible.  Reuse that admission for the
                    // connected-vertex undo instead of asking a stateful layer
                    // predicate a second time for the same destination.
                    success &= SetBitmapPixel(
                        memory,
                        bitmap,
                        currentX,
                        currentY,
                        color,
                        2,
                        writeMask);
                }
            }

            if (!duplicateRaster)
                lastRaster = raster;

            firstPoint = false;
            if (currentX == targetX && currentY == targetY)
                break;

            var doubleError = error * 2;
            if (doubleError >= dy)
            {
                error += dy;
                currentX += sx;
            }

            if (doubleError <= dx)
            {
                error += dx;
                currentY += sy;
            }
        }

        if (!success)
        {
            RestoreDrawTransaction(
                memory,
                rastPort,
                flags,
                originalPatternCount,
                (short)x0,
                (short)y0,
                lineSnapshotAddresses,
                lineSnapshotValues);
            return Failure;
        }

        if (firstDotRequested && consumeFirstDot &&
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                (ushort)(flags & ~GraphicsLayouts.RastPortFirstDot)))
        {
            RestoreDrawTransaction(
                memory,
                rastPort,
                flags,
                originalPatternCount,
                (short)x0,
                (short)y0,
                lineSnapshotAddresses,
                lineSnapshotValues);
            return Failure;
        }

        patternCount = unchecked((byte)((patternCount - majorDistance) & 0x0F));
        if (!TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                patternCount) ||
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                unchecked((ushort)x)) ||
            !TryWriteRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                unchecked((ushort)y)))
        {
            RestoreDrawTransaction(
                memory,
                rastPort,
                flags,
                originalPatternCount,
                (short)x0,
                (short)y0,
                lineSnapshotAddresses,
                lineSnapshotValues);
            return Failure;
        }

        return success ? Success : Failure;
    }

    private static void RestoreDrawTransaction(
        IGraphicsMemory memory,
        uint rastPort,
        ushort flags,
        byte patternCount,
        short currentX,
        short currentY,
        IReadOnlyList<uint> snapshotAddresses,
        IReadOnlyList<byte> snapshotValues)
    {
        // Draw may have already published pixels before a final cursor or
        // Flags WORD is rejected. Restore the guest state byte-wise so a
        // repeated WORD rejection cannot strand a half-advanced line.
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortFlags,
            new[] { (byte)(flags >> 8), (byte)flags });
        _ = TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortLinePatternCount,
            patternCount);
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortCurrentX,
            new[] { (byte)((ushort)currentX >> 8), (byte)(ushort)currentX });
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortCurrentY,
            new[] { (byte)((ushort)currentY >> 8), (byte)(ushort)currentY });

        for (var index = snapshotAddresses.Count - 1; index >= 0; index--)
            _ = memory.TryWriteByte(snapshotAddresses[index], snapshotValues[index]);
    }

    private static bool TryPreflightLineDestination(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int startX,
        int startY,
        int targetX,
        int targetY,
        int drawMode,
        byte writeMask,
        ushort linePattern,
        byte patternCount,
        bool oneDotMode,
        bool majorAxisIsX,
        List<uint>? snapshotAddresses = null,
        List<byte>? snapshotValues = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
            return true;

        var currentX = startX;
        var currentY = startY;
        var dx = Math.Abs(targetX - currentX);
        var sx = currentX < targetX ? 1 : -1;
        var dy = -Math.Abs(targetY - currentY);
        var sy = currentY < targetY ? 1 : -1;
        var error = dx + dy;
        var lastRaster = int.MinValue;
        while (true)
        {
            var majorStep = Math.Max(Math.Abs(currentX - startX), Math.Abs(currentY - startY));
            var patternBit = (linePattern & (1 << ((patternCount - majorStep) & 0x0F))) != 0;
            if ((drawMode & 4) != 0)
                patternBit = !patternBit;

            // ONE_DOT follows the hardware's "one dot per horizontal line"
            // rule.  A shallow line therefore suppresses repeated samples
            // on the same Y row while still advancing the line-pattern phase
            // over every traversed major-axis step.
            var raster = oneDotMode ? currentY : (majorAxisIsX ? currentX : currentY);
            var duplicateRaster = oneDotMode && raster == lastRaster;
            // Keep preflight in lockstep with the publication loop: JAM2
            // writes both pattern-one (APen) and pattern-zero (BPen) samples,
            // while a JAM1 zero sample is not a destination write.  The
            // connected COMPLEMENT undo is a second write to the same
            // already-probed cell, so no special pattern-bit branch is
            // needed here.
            var sampleWrites = patternBit || (drawMode & 1) != 0;
            var inBitmap = currentX >= 0 && currentY >= 0 &&
                currentX < bitmap.Width && currentY < bitmap.Rows;
            if (!duplicateRaster && sampleWrites && inBitmap)
            {
                if ((pixelVisible?.Invoke(currentX, currentY) ?? true) &&
                    !TryProbeBitmapWrite(
                        memory,
                        bitmap,
                        currentX,
                        currentY,
                        writeMask,
                        snapshotAddresses,
                        snapshotValues))
                {
                    return false;
                }
            }

            if (!duplicateRaster)
                lastRaster = raster;

            if (currentX == targetX && currentY == targetY)
                break;

            var doubleError = error * 2;
            if (doubleError >= dy)
            {
                error += dy;
                currentX += sx;
            }

            if (doubleError <= dx)
            {
                error += dx;
                currentY += sy;
            }
        }

        return true;
    }

    internal static int ReadPixel(IGraphicsMemory memory, uint rastPort, short x, short y)
    {
        return TryReadPixel(memory, rastPort, x, y, out var result)
            ? result
            : Failure;
    }

    /// <summary>
    /// Status-aware ReadPixel boundary for the register adapter.  An
    /// outside coordinate is a valid graphics.library result (-1); an
    /// unreadable/odd RastPort or a missing declared plane is malformed guest
    /// state and must remain available to native/provider ownership.
    /// </summary>
    internal static bool TryReadPixel(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        out int result)
    {
        result = Failure;
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            layer != 0)
        {
            // A layered read belongs to layers.library/native ownership.  A
            // pure core caller has no visibility predicate to describe the
            // logical layer, so keep the vector available to that provider.
            return false;
        }

        if (!TryReadBitmap(memory, rastPort, out var bitmap))
            return false;

        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return true;

        if (!TryReadBitmapPixel(memory, bitmap, x, y, out var color))
            return false;

        result = color;
        return true;
    }

    internal static int WritePixel(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        Func<int, int, bool>? pixelVisible = null)
    {
        return TryWritePixel(memory, rastPort, x, y, pixelVisible, out var result)
            ? result
            : Failure;
    }

    /// <summary>
    /// Status-aware WritePixel boundary matching the public result split used
    /// by <see cref="TryReadPixel"/>.  Only a valid outside coordinate claims
    /// the vector with -1; malformed drawing state declines before mutation.
    /// </summary>
    internal static bool TryWritePixel(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        out int result)
        => TryWritePixel(memory, rastPort, x, y, null, out result);

    internal static bool TryWritePixel(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        Func<int, int, bool>? pixelVisible,
        out int result)
        => TryWritePixel(
            memory,
            rastPort,
            x,
            y,
            pixelVisible,
            visibilityChecked: false,
            out result);

    /// <summary>
    /// Writes one logical pixel after an optional provider visibility
    /// decision.  AreaEnd's closed-outline complement undo can pass
    /// <paramref name="visibilityChecked"/> when the endpoint was already
    /// admitted by the preceding Draw traversal; this prevents a stateful
    /// layer provider from being queried twice for the same logical cell.
    /// </summary>
    internal static bool TryWritePixel(
        IGraphicsMemory memory,
        uint rastPort,
        short x,
        short y,
        Func<int, int, bool>? pixelVisible,
        bool visibilityChecked,
        out int result)
    {
        result = Failure;
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            // Keep direct/core writes behind the same explicit layer
            // provider boundary as Draw and PolyDraw.  An AreaEnd outline
            // undo may supply that predicate explicitly, allowing the
            // provider-backed path to toggle its already-admitted endpoint.
            return false;
        }

        // Native WritePixel uses a private graphics-context colour sidecar
        // when RPF_NO_PENS is set instead of consuming RastPort->FgPen.  The
        // portable standard-planar path has no such provider-owned sidecar;
        // decline before reading the public pen or bitmap so CyberGraphX,
        // Layers, or a future native CopperSharp68k body can supply it.
        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags) ||
            (flags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return false;
        }

        var drawMode = GetDrawMode(memory, rastPort);
        if (!TryReadBitmap(memory, rastPort, out var bitmap) ||
            drawMode < 0 ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var mask))
        {
            return false;
        }

        // A mask that selects only planes outside the declared bitmap depth
        // is an effective zero-mask operation.  Keep the public colour bytes
        // behind the same field-level ownership boundary as Draw and
        // PolyDraw: the native/provider owner may legitimately supply those
        // fields even though this single-pixel call can still claim its
        // bounded no-write result.
        var effectiveMask = EffectiveWriteMask(bitmap, mask);

        // The planar writer ignores the supplied colour in COMPLEMENT mode:
        // every selected destination plane is toggled from its current bit.
        // Do not claim an unrelated/provider-owned FgPen byte for that
        // inversion-only form; non-complement modes retain the normal pen
        // dependency.
        //
        // An explicit provider visibility predicate also establishes a
        // clipped logical destination.  Check the planar bitmap envelope
        // before resolving APen/BPen in that path: an off-raster cell is a
        // successful -1 no-op and must not consume a sparse/provider-owned
        // pen byte that cannot affect any destination storage.  Keep the
        // direct/core path's historical pen admission order unchanged.
        if (pixelVisible is not null &&
            (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows))
        {
            return true;
        }

        var color = 0;
        if (effectiveMask != 0 && (drawMode & DrawModeComplement) == 0)
        {
            color = GetAPen(memory, rastPort);
            if (color < 0)
                return false;
        }

        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return true;

        if (!visibilityChecked &&
            pixelVisible is not null &&
            !pixelVisible(x, y))
            return true;

        // WritePixel is a one-cell drawing operation, not an unconditional
        // planar store.  The classic routine feeds the selected cell through
        // the RastPort minterms: the implicit source bit is set, INVERSVID
        // flips that source selection, JAM2 supplies BPen for a source-zero
        // cell, and COMPLEMENT toggles only when the resulting source cell is
        // selected.  Keep this decision here instead of teaching the low
        // level writer about implicit pattern cells; Draw, Text, and template
        // operations already make the same selection before calling it.
        var sourceSelected = (drawMode & DrawModeInverseVideo) == 0;
        if (effectiveMask != 0 && !sourceSelected)
        {
            if ((drawMode & DrawModeComplement) != 0)
            {
                result = Success;
                return true;
            }

            if ((drawMode & DrawModeJam2) == 0)
            {
                result = Success;
                return true;
            }

            color = GetBPen(memory, rastPort);
            if (color < 0)
                return false;
        }

        var pixelDrawMode = (drawMode & DrawModeComplement) != 0
            ? DrawModeComplement
            : 0;
        if (!SetBitmapPixel(memory, bitmap, x, y, color, pixelDrawMode, mask))
            return false;

        result = Success;
        return true;
    }

    internal static int RectFill(
        IGraphicsMemory memory,
        uint rastPort,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        VisibilityScratch? visibilityScratch = null)
    {
        if (pixelVisible is null)
        {
            return RectFillCore(memory, rastPort, xMin, yMin, xMax, yMax,
                null, snapshotAddressBuffer, snapshotValueBuffer);
        }

        var scratch = visibilityScratch ?? new VisibilityScratch();
        if (!scratch.TryAcquire(pixelVisible, out var stablePixelVisible))
            return Failure;
        try
        {
            return RectFillCore(memory, rastPort, xMin, yMin, xMax, yMax,
                stablePixelVisible, snapshotAddressBuffer, snapshotValueBuffer);
        }
        finally
        {
            scratch.Release();
        }
    }

    private static int RectFillCore(
        IGraphicsMemory memory,
        uint rastPort,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        Func<int, int, bool>? pixelVisible,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer)
    {
        // The public RectFill contract describes (xmin,ymin) as the upper
        // left and (xmax,ymax) as the lower right.  Kickstart treats an
        // inverted rectangle as an empty operation, not as an error: the
        // void vector simply skips the fill when either endpoint is reversed.
        // Check that geometry before requiring any bitmap state.  A readable
        // non-null layer remains an explicit provider/native ownership
        // boundary even for an empty rectangle; an unreadable/partially
        // mapped layer cannot establish that ownership and the no-op is still
        // claimable by the standard-planar path.
        if (xMax < xMin || yMax < yMin)
        {
            if (TryReadRastPortLong(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortLayer,
                    out var emptyLayer) &&
                emptyLayer != 0 &&
                pixelVisible is null)
            {
                return Failure;
            }

            return Success;
        }

        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return Failure;
        }

        return BltPatternCore(
            memory,
            rastPort,
            0,
            xMin,
            yMin,
            xMax,
            yMax,
            0,
            pixelVisible,
            snapshotAddressBuffer,
            snapshotValueBuffer,
            false);
    }

    /// <summary>
    /// Applies the RastPort area-fill rules through a rectangular stencil.
    /// The mask is a contiguous, word-padded one-bit pattern; a null mask
    /// selects every pixel in the inclusive destination rectangle.  This is
    /// the pure planar portion of BltPattern.  Layer clipping and temporary
    /// raster ownership remain explicit host/native boundaries.
    /// </summary>
    internal static int BltPattern(
        IGraphicsMemory memory,
        uint rastPort,
        uint mask,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        uint byteCount,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        bool rejectNegativeByteCount = false,
        VisibilityScratch? visibilityScratch = null)
    {
        if (pixelVisible is null)
        {
            return BltPatternCore(
                memory, rastPort, mask, xMin, yMin, xMax, yMax, byteCount,
                null, snapshotAddressBuffer, snapshotValueBuffer,
                rejectNegativeByteCount);
        }

        var scratch = visibilityScratch ?? new VisibilityScratch();
        if (!scratch.TryAcquire(pixelVisible, out var stablePixelVisible))
            return Failure;
        try
        {
            return BltPatternCore(
                memory, rastPort, mask, xMin, yMin, xMax, yMax, byteCount,
                stablePixelVisible, snapshotAddressBuffer, snapshotValueBuffer,
                rejectNegativeByteCount);
        }
        finally
        {
            scratch.Release();
        }
    }

    private static int BltPatternCore(
        IGraphicsMemory memory,
        uint rastPort,
        uint mask,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        uint byteCount,
        Func<int, int, bool>? pixelVisible,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer,
        bool rejectNegativeByteCount)
    {
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return Failure;
        }

        if (xMax < xMin || yMax < yMin)
            return Success;

        if (!TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return Failure;
        }

        var width = (int)xMax - xMin + 1;
        var height = (int)yMax - yMin + 1;
        if (width <= 0 || height <= 0)
            return Failure;

        // A provider-backed destination is the ownership boundary for every
        // later source field: area pattern metadata, pens, and an external
        // stencil are only guest-owned for visible destination samples. Do
        // this classification before admitting those fields so a fully
        // hidden rectangle is a successful no-op without consuming them.
        var left = Math.Max(0, (int)xMin);
        var top = Math.Max(0, (int)yMin);
        var right = Math.Min(bitmap.Width - 1, (int)xMax);
        var bottom = Math.Min(bitmap.Rows - 1, (int)yMax);
        if (left > right || top > bottom)
            return Success;

        // Reject a maximal bitmap walk before invoking a provider callback.
        // This is a host safety envelope, not source admission: only the
        // inverse-video/null-pattern JAM1 shortcut is exempt, and that
        // shortcut is identified from the two scalar mode fields here.
        var earlyRequestedPixels = (ulong)(uint)width * (uint)height;
        var earlyBitmapPixels = (ulong)bitmap.Width * (uint)bitmap.Rows;
        if (earlyRequestedPixels > int.MaxValue &&
            earlyBitmapPixels > int.MaxValue)
        {
            if (!TryReadRastPortByte(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortDrawMode,
                    out var earlyDrawMode) ||
                !TryReadRastPortLong(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortAreaPtrn,
                    out var earlyAreaPattern) ||
                !(mask == 0 && earlyAreaPattern == 0 &&
                  (earlyDrawMode & 4) != 0 &&
                  (earlyDrawMode & 1) == 0))
            {
                return Failure;
            }
        }

        if (pixelVisible is not null)
        {
            if (!TryHasVisibleBitmapRegion(
                    bitmap,
                    left,
                    top,
                    right,
                    bottom,
                    pixelVisible,
                    out var hasVisibleDestination))
            {
                return Failure;
            }

            if (!hasVisibleDestination)
                return Success;
        }

        // RPF_NO_PENS is an optional provider/native colour-ownership hint.
        // Honor it when the Flags word is readable, but keep sparse planar
        // ports (which omit the hint) on the portable path. This read is
        // deliberately after destination visibility classification.
        if (TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags) &&
            (flags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return Failure;
        }

        // No destination planes are selected after the bitmap-depth mask is
        // applied. BltPattern/RectFill remain successful bounded no-ops in
        // that case, but RPF_NO_PENS above remains an explicit ownership
        // decision even for a zero-effective-mask call.
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
            return Success;

        // The public gateway supplies a signed WORD byteCnt. Preserve the
        // classic bounded no-op above when no destination plane is selected,
        // but do not let a negative stride address an external stencil on a
        // drawing path that would otherwise consume it.
        if (rejectNegativeByteCount && mask != 0)
            return Failure;

        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortDrawMode,
                out var drawMode) ||
            !TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtrn,
                out var areaPattern))
        {
            return Failure;
        }

        // RectFill's no-pattern path only consumes AreaPtrn, pens, and
        // DrawMode.  AreaPtSz belongs to the delegated BltPattern pattern
        // decoder and is not read when the pattern pointer is NULL or when
        // COMPLEMENT|JAM2 takes its whole-region inversion shortcut (the
        // native vector can therefore fill a caller-owned RastPort whose
        // trailing AreaPtSz byte is unavailable). Preserve that field-level
        // ownership boundary while still requiring it for a source pattern
        // that the selected draw mode actually decodes.
        var areaPatternSize = (byte)0;
        var complementJam2 = (drawMode & 0x03) == 0x03;
        if (areaPattern != 0 &&
            !complementJam2 &&
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtSz,
                out areaPatternSize))
        {
            return Failure;
        }

        // The shared planar writer ignores colour in COMPLEMENT mode.  A
        // negative AreaPtSz selects a multicolor source whose encoded planes
        // are deposited directly in ordinary JAM2, so that mode also consumes
        // neither public pen.  Keep those fields outside the portable
        // admission envelope; JAM1/complement still needs FgPen, while a
        // positive/NULL pattern retains the documented pen phases.  Resolve
        // the fields lazily after the explicit provider visibility gate so a
        // fully hidden clipped rectangle cannot claim sparse/provider-owned
        // colour storage.
        var multicolorJam2 = areaPattern != 0 &&
            unchecked((sbyte)areaPatternSize) < 0 &&
            (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0;
        byte foreground = 0;
        var foregroundLoaded = (drawMode & DrawModeComplement) != 0 || multicolorJam2;

        // A null area pattern is an all-one source.  In that case JAM1,
        // COMPLEMENT|JAM2, and ordinary JAM2 do not consume BPen; only an
        // inverse-video JAM2 source selects the background pen.  A positive
        // one-bit pattern can contain source-zero cells and therefore keeps
        // the BPen dependency; multicolor JAM2 emits its encoded color,
        // including zero, without a background phase.
        byte background = 0;
        var needsBackgroundPen = (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0 &&
            (areaPattern == 0
                ? (drawMode & DrawModeInverseVideo) != 0
                : unchecked((sbyte)areaPatternSize) >= 0);
        var backgroundLoaded = !needsBackgroundPen;

        // Bound a full-size guest rectangle before probing an external stencil
        // or area-pattern source.  The signed-word API can describe a full
        // 65536 x 65536 request; when the destination bitmap itself also
        // exceeds the portable pixel budget, a provider/native fallback must
        // own that request rather than letting the host enter a multi-billion
        // cell walk.  Small clipped bitmaps retain the classic clipping
        // behavior even when the caller uses extreme coordinates.
        var requestedPixels = (ulong)(uint)width * (uint)height;
        var implicitInverseJam1NoOp = mask == 0 && areaPattern == 0 &&
            (drawMode & 4) != 0 && (drawMode & 1) == 0;
        var bitmapPixels = (ulong)bitmap.Width * (uint)bitmap.Rows;
        if (requestedPixels > int.MaxValue &&
            bitmapPixels > int.MaxValue &&
            !implicitInverseJam1NoOp)
            return Failure;

        // When the requested rectangle covers the complete bitmap, use the
        // bitmap envelope itself as an early aggregate admission check. This
        // keeps the full-raster case ahead of all stencil and plane reads,
        // including clipped requests whose signed-WORD rectangle is wider
        // than the actual destination.
        if (!implicitInverseJam1NoOp &&
            bitmapPixels > PortableWorkLimit / BltPatternPassesPerCell &&
            requestedPixels >= bitmapPixels)
        {
            return Failure;
        }

        if (mask != 0)
        {
            var requiredBytes = checked(((width + 15) / 16) * 2);
            if (byteCount == 0 || (byteCount & 1u) != 0 || byteCount < (uint)requiredBytes ||
                (mask & 1u) != 0 ||
                (pixelVisible is null &&
                 !TryProbeContiguousRows(
                     memory,
                     mask,
                     byteCount,
                     (uint)height,
                     (uint)requiredBytes)))
            {
                return Failure;
            }
        }

        if (areaPattern != 0 && (areaPattern & 1u) != 0 &&
            !complementJam2)
            return Failure;

        // With no AreaPtrn, the mask itself is the one-bit template.  The
        // classic BltPattern path forces JAM2 to its JAM1 equivalent so zero
        // bits in that external stencil do not deposit BPen.  Preserve
        // INVERSVID while applying that local rule; COMPLEMENT remains
        // selected-region inversion below.
        var effectiveDrawMode = drawMode;
        if (mask != 0 && areaPattern == 0 &&
            (drawMode & 0x03) == 1)
        {
            effectiveDrawMode = (byte)(drawMode & 4);
        }

        // With no AreaPtrn the classic BltPattern implementation delegates
        // the external mask to the template path.  INVERSVID therefore
        // inverts that one-bit template as well as selecting the inverse pen
        // phase.  COMPLEMENT|JAM2 is the exception: it deliberately takes
        // the whole-region invert shortcut and leaves the supplied mask as
        // the selected destination stencil.
        var invertExternalStencil = mask != 0 &&
            areaPattern == 0 &&
            (drawMode & 4) != 0 &&
            (drawMode & 0x03) != 0x03;

        // The external template already carries the INVERSVID decision in
        // its selected stencil cells.  Keep that bit out of the null-pattern
        // pen decision, otherwise JAM1 would treat the all-one source as an
        // empty pattern after the stencil has been inverted.
        var patternDrawMode = mask != 0 && areaPattern == 0
            ? (byte)(effectiveDrawMode & ~4)
            : effectiveDrawMode;

        // With no external stencil or AreaPtrn, INVERSVID turns the implicit
        // all-one source into all zeroes.  In JAM1 (including its
        // COMPLEMENT|INVERSVID form) that means no destination pixel is
        // selected.  Keep this a successful no-op without probing the planar
        // storage; JAM2 still writes BPen and all patterned paths retain their
        // normal destination preflight below.
        if (mask == 0 && areaPattern == 0 && implicitInverseJam1NoOp)
        {
            return Success;
        }

        // The first patterned pass evaluates the external stencil/area
        // source and, when a layer predicate is supplied, the visibility
        // callback once per clipped pixel.  Bound that logical walk before
        // entering it; the later byte snapshot has the same limit, but it is
        // too late to protect this preflight loop from a maximal 16-bit
        // rectangle.  The inverse-video/null-pattern no-op above is kept ahead
        // of this guard so it remains a bounded successful no-op.
        var touchedPixels = (ulong)(uint)(right - left + 1) *
            (uint)(bottom - top + 1);
        var selectedPlanes = 0u;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) != 0)
                selectedPlanes++;
        }

        var destinationBytesPerRow = (uint)((right >> 3) - (left >> 3) + 1);
        var destinationRows = (uint)(bottom - top + 1);
        var snapshotBytes = (ulong)destinationBytesPerRow *
            destinationRows * selectedPlanes;
        var logicalWork = touchedPixels * BltPatternPassesPerCell;
        if (touchedPixels > PortableWorkLimit / BltPatternPassesPerCell ||
            logicalWork > PortableWorkLimit ||
            snapshotBytes > PortableWorkLimit ||
            logicalWork > PortableWorkLimit - snapshotBytes)
            return Failure;

        // SetBitmapPixel guards one destination pixel at a time.  A patterned
        // rectangle is one guest operation, so preflight every selected plane
        // in the clipped destination before publishing the first pixel.  This
        // keeps a malformed later row from exposing a partially filled
        // RectFill/BltPattern region while preserving the clipped no-op path.
        if (writeMask != 0)
        {
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    if (pixelVisible is not null && !pixelVisible(x, y))
                        continue;

                    var stencilBit = false;
                    if (mask != 0 &&
                        !TryReadStencilBit(
                            memory,
                            mask,
                            (uint)byteCount,
                            y - yMin,
                            x - xMin,
                            out stencilBit))
                    {
                        return Failure;
                    }

                    if (invertExternalStencil)
                        stencilBit = !stencilBit;

                    if (mask != 0 && !stencilBit)
                        continue;

                    if (!TryEnsurePen(memory, rastPort,
                            GraphicsLayouts.RastPortFgPen,
                            ref foreground, ref foregroundLoaded) ||
                        !TryEnsurePen(memory, rastPort,
                            GraphicsLayouts.RastPortBgPen,
                            ref background, ref backgroundLoaded))
                        return Failure;

                    if (!TryGetAreaPatternPixel(
                            memory,
                        areaPattern,
                        unchecked((sbyte)areaPatternSize),
                        bitmap.Depth,
                        x,
                        y,
                        foreground,
                        background,
                        patternDrawMode,
                        out _,
                        out var drawPixel))
                    {
                        return Failure;
                    }

                    if (drawPixel &&
                        (pixelVisible is null || pixelVisible(x, y)) &&
                        !TryProbeBitmapWrite(memory, bitmap, x, y, writeMask))
                    {
                        return Failure;
                    }
                }
            }
        }

        if (!TrySnapshotBitmapRegion(
                memory,
                bitmap,
                left,
                top,
                right,
                bottom,
                writeMask,
                out var snapshotAddresses,
                out var snapshotValues,
                snapshotAddressBuffer,
                snapshotValueBuffer,
                pixelVisible))
        {
            return Failure;
        }

        var success = true;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (pixelVisible is not null && !pixelVisible(x, y))
                    continue;

                var stencilBit = false;
                if (mask != 0 &&
                    !TryReadStencilBit(
                        memory,
                        mask,
                        (uint)byteCount,
                        y - yMin,
                        x - xMin,
                        out stencilBit))
                {
                    RestoreBitmapSnapshot(
                        memory,
                        snapshotAddresses,
                        snapshotValues);
                    return Failure;
                }

                if (invertExternalStencil)
                    stencilBit = !stencilBit;

                if (mask != 0 && !stencilBit)
                    continue;

                if (!TryEnsurePen(memory, rastPort,
                        GraphicsLayouts.RastPortFgPen,
                        ref foreground, ref foregroundLoaded) ||
                    !TryEnsurePen(memory, rastPort,
                        GraphicsLayouts.RastPortBgPen,
                        ref background, ref backgroundLoaded))
                {
                    RestoreBitmapSnapshot(
                        memory,
                        snapshotAddresses,
                        snapshotValues);
                    return Failure;
                }

                if (!TryGetAreaPatternPixel(
                        memory,
                    areaPattern,
                    unchecked((sbyte)areaPatternSize),
                    bitmap.Depth,
                    x,
                    y,
                    foreground,
                    background,
                    patternDrawMode,
                    out var color,
                    out var drawPixel))
                {
                    RestoreBitmapSnapshot(
                        memory,
                        snapshotAddresses,
                        snapshotValues);
                    return Failure;
                }

                if (drawPixel)
                    success &= SetBitmapPixel(
                        memory,
                        bitmap,
                        x,
                        y,
                        color,
                        effectiveDrawMode,
                        writeMask);
            }
        }

        if (!success)
            RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
        return success ? Success : Failure;
    }

    private static bool TryEnsurePen(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        ref byte value,
        ref bool loaded)
    {
        if (loaded)
            return true;
        if (!TryReadRastPortByte(memory, rastPort, offset, out value))
            return false;
        loaded = true;
        return true;
    }

    /// <summary>
    /// Draws a planar ellipse outline using integer midpoint steps.  The
    /// non-layered Kickstart vector does not software-clip the outline; the
    /// portable safety boundary therefore rejects an ellipse whose outline
    /// would leave the declared bitmap instead of silently clipping it.
    /// </summary>
    /// <summary>
    /// Implements the classic <c>DrawCircle</c> graphics macro. The macro
    /// expands to an equal-radius <c>DrawEllipse</c> call, so the same
    /// positive-radius, planar-mask, clipping, and rollback rules apply.
    /// </summary>
    internal static int DrawCircle(
        IGraphicsMemory memory,
        uint rastPort,
        short centerX,
        short centerY,
        short radius,
        Func<int, int, bool>? pixelVisible = null,
        List<(int X, int Y)>? pointBuffer = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null)
        => DrawEllipse(
            memory,
            rastPort,
            centerX,
            centerY,
            radius,
            radius,
            pixelVisible,
            pointBuffer,
            snapshotAddressBuffer,
            snapshotValueBuffer);

    internal static int DrawEllipse(
        IGraphicsMemory memory,
        uint rastPort,
        short centerX,
        short centerY,
        short radiusX,
        short radiusY,
        Func<int, int, bool>? pixelVisible = null,
        List<(int X, int Y)>? pointBuffer = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        VisibilityScratch? visibilityScratch = null)
    {
        if (pixelVisible is null)
        {
            return DrawEllipseCore(
                memory, rastPort, centerX, centerY, radiusX, radiusY, null,
                pointBuffer, snapshotAddressBuffer, snapshotValueBuffer);
        }

        var scratch = visibilityScratch ?? new VisibilityScratch();
        if (!scratch.TryAcquire(pixelVisible, out var stablePixelVisible))
            return Failure;
        try
        {
            return DrawEllipseCore(
                memory, rastPort, centerX, centerY, radiusX, radiusY,
                stablePixelVisible, pointBuffer, snapshotAddressBuffer,
                snapshotValueBuffer);
        }
        finally
        {
            scratch.Release();
        }
    }

    private static int DrawEllipseCore(
        IGraphicsMemory memory,
        uint rastPort,
        short centerX,
        short centerY,
        short radiusX,
        short radiusY,
        Func<int, int, bool>? pixelVisible,
        List<(int X, int Y)>? pointBuffer,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer)
    {
        if (radiusX <= 0 || radiusY <= 0 ||
            !TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null) ||
            !TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return Failure;
        }

        // DrawEllipse does not mutate the RastPort Flags word.  Treat
        // RPF_NO_PENS as an opportunistic provider/native colour-ownership
        // hint: honor it when readable, but do not make an absent or
        // temporarily faulted optional word steal an otherwise valid sparse
        // standard-planar ellipse from the portable path.
        if (TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags) &&
            (flags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return Failure;
        }

        var drawMode = GetDrawMode(memory, rastPort);
        if (drawMode < 0 ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return Failure;
        }

        var effectiveWriteMask = EffectiveWriteMask(bitmap, writeMask);

        // A zero write mask selects no destination planes. Keep the common
        // planar primitive contract: succeed without probing outline points
        // or bitmap storage, even when the requested non-layered ellipse lies
        // outside the bitmap. Nonzero masks retain the native no-clipping
        // safety boundary below.
        writeMask = effectiveWriteMask;
        if (writeMask == 0)
            return Success;

        var a = (long)radiusX;
        var b = (long)radiusY;
        var a2 = a * a;
        var b2 = b * b;
        var x = 0L;
        var y = b;
        var dx = 0L;
        var dy = 2L * a2 * y;
        var decision = b2 - (a2 * b) + (a2 / 4);
        var points = pointBuffer ?? new List<(int X, int Y)>();
        points.Clear();
        var previousX = long.MinValue;
        var previousY = long.MinValue;

        while (dx < dy)
        {
            CollectEllipseSymmetric(points, centerX, centerY, x, y,
                ref previousX, ref previousY);
            if (decision < 0)
            {
                x++;
                dx += 2L * b2;
                decision += dx + b2;
            }
            else
            {
                x++;
                y--;
                dx += 2L * b2;
                dy -= 2L * a2;
                decision += dx - dy + b2;
            }
        }

        decision = b2 * (x * x + x) + (b2 / 4) + a2 * (y - 1) * (y - 1) - a2 * b2;
        while (y >= 0)
        {
            CollectEllipseSymmetric(points, centerX, centerY, x, y,
                ref previousX, ref previousY);
            if (decision > 0)
            {
                y--;
                dy -= 2L * a2;
                decision += a2 - dy;
            }
            else
            {
                x++;
                y--;
                dx += 2L * b2;
                dy -= 2L * a2;
                decision += dx - dy + a2;
            }
        }

        var left = centerX - radiusX;
        var top = centerY - radiusY;
        var right = centerX + radiusX;
        var bottom = centerY + radiusY;
        var selectedPlanes = 0u;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) != 0)
                selectedPlanes++;
        }

        if (!TryAdmitDrawEllipseWork(
                bitmap,
                left,
                top,
                right,
                bottom,
                selectedPlanes,
                (ulong)points.Count,
                pixelVisible is not null))
        {
            return Failure;
        }

        var visiblePointCount = 0;
        foreach (var point in points)
        {
            var inBitmap = point.X >= 0 && point.Y >= 0 &&
                point.X < bitmap.Width && point.Y < bitmap.Rows;
            if (!inBitmap)
            {
                // A provider-backed RastPort owns logical clipping, but its
                // planar backing store still admits only in-bitmap cells.
                // Do not query that provider for an off-raster outline point;
                // the non-layered path retains the classic all-or-nothing
                // safety boundary and rejects the ellipse instead.
                if (pixelVisible is not null)
                    continue;

                return Failure;
            }

            if (pixelVisible is not null && !pixelVisible(point.X, point.Y))
                continue;

            if (!TryProbeBitmapWrite(memory, bitmap, point.X, point.Y, writeMask))
            {
                return Failure;
            }

            visiblePointCount++;
        }

        // An explicit provider clip may hide the complete outline.  In that
        // case the operation is a successful clipped no-op and must not
        // claim a public FgPen byte that cannot affect any destination cell.
        // The non-layered path reaches this point only after its usual
        // no-clipping envelope has admitted every sample.
        if (visiblePointCount == 0)
            return Success;

        // DrawEllipse routes outline samples through SetBitmapPixel, so
        // COMPLEMENT does not consume APen either. Resolve the foreground
        // only after provider visibility and planar destination admission;
        // sparse/provider-owned pens remain outside an invisible outline's
        // portable ownership envelope.
        var color = 0;
        if ((drawMode & DrawModeComplement) == 0)
        {
            color = GetAPen(memory, rastPort);
            if (color < 0)
                return Failure;
        }

        if (!TrySnapshotBitmapRegion(
                memory,
                bitmap,
                left,
                top,
                right,
                bottom,
                writeMask,
                out var snapshotAddresses,
                out var snapshotValues,
                snapshotAddressBuffer,
                snapshotValueBuffer,
                pixelVisible))
        {
            return Failure;
        }

        var success = true;
        foreach (var point in points)
        {
            var inBitmap = point.X >= 0 && point.Y >= 0 &&
                point.X < bitmap.Width && point.Y < bitmap.Rows;
            if (!inBitmap)
                continue;

            if (pixelVisible is null || pixelVisible(point.X, point.Y))
            {
                success &= SetBitmapPixel(
                    memory,
                    bitmap,
                    point.X,
                    point.Y,
                    color,
                    drawMode,
                    writeMask);
            }
        }

        if (!success)
            RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);

        return success ? Success : Failure;
    }

    private static void CollectEllipseSymmetric(
        List<(int X, int Y)> points,
        short centerX,
        short centerY,
        long offsetX,
        long offsetY,
        ref long previousX,
        ref long previousY)
    {
        if (offsetX == previousX && offsetY == previousY)
            return;
        previousX = offsetX;
        previousY = offsetY;
        points.Add(((int)(centerX + offsetX), (int)(centerY + offsetY)));
        if (offsetX != 0)
            points.Add(((int)(centerX - offsetX), (int)(centerY + offsetY)));
        if (offsetY != 0)
            points.Add(((int)(centerX + offsetX), (int)(centerY - offsetY)));
        if (offsetX != 0 && offsetY != 0)
            points.Add(((int)(centerX - offsetX), (int)(centerY - offsetY)));
    }

    /// <summary>
    /// Scrolls a clipped non-layered raster rectangle. Positive deltas move
    /// the image toward the origin; the vacated area receives BPen.
    /// </summary>
    internal static int ScrollRaster(
        IGraphicsMemory memory,
        uint rastPort,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        Func<int, int, bool>? pixelVisible = null,
        List<int>? sourcePixelBuffer = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        List<uint>? nestedSnapshotAddressBuffer = null,
        List<byte>? nestedSnapshotValueBuffer = null,
        List<uint>? tertiarySnapshotAddressBuffer = null,
        List<byte>? tertiarySnapshotValueBuffer = null,
        MintermScratch? mintermScratch = null,
        VisibilityScratch? visibilityScratch = null,
        VisibilityScratch? nestedVisibilityScratch = null,
        MintermScratch? nestedMintermScratch = null)
        => ScrollRaster(
            memory,
            rastPort,
            deltaX,
            deltaY,
            xMin,
            yMin,
            xMax,
            yMax,
            useBackgroundPen: true,
            pixelVisible,
            sourcePixelBuffer,
            snapshotAddressBuffer,
            snapshotValueBuffer,
            nestedSnapshotAddressBuffer,
            nestedSnapshotValueBuffer,
            tertiarySnapshotAddressBuffer,
            tertiarySnapshotValueBuffer,
            mintermScratch,
            visibilityScratch,
            nestedVisibilityScratch,
            nestedMintermScratch);

    internal static int ScrollRasterBF(
        IGraphicsMemory memory,
        uint rastPort,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        Func<int, int, bool>? pixelVisible = null,
        List<int>? sourcePixelBuffer = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        List<uint>? nestedSnapshotAddressBuffer = null,
        List<byte>? nestedSnapshotValueBuffer = null,
        List<uint>? tertiarySnapshotAddressBuffer = null,
        List<byte>? tertiarySnapshotValueBuffer = null,
        MintermScratch? mintermScratch = null,
        VisibilityScratch? visibilityScratch = null,
        VisibilityScratch? nestedVisibilityScratch = null,
        MintermScratch? nestedMintermScratch = null)
        => ScrollRaster(
            memory,
            rastPort,
            deltaX,
            deltaY,
            xMin,
            yMin,
            xMax,
            yMax,
            useBackgroundPen: false,
            pixelVisible,
            sourcePixelBuffer,
            snapshotAddressBuffer,
            snapshotValueBuffer,
            nestedSnapshotAddressBuffer,
            nestedSnapshotValueBuffer,
            tertiarySnapshotAddressBuffer,
            tertiarySnapshotValueBuffer,
            mintermScratch,
            visibilityScratch,
            nestedVisibilityScratch,
            nestedMintermScratch);

    private static int ScrollRaster(
        IGraphicsMemory memory,
        uint rastPort,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        bool useBackgroundPen,
        Func<int, int, bool>? pixelVisible,
        List<int>? sourcePixelBuffer,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer,
        List<uint>? nestedSnapshotAddressBuffer,
        List<byte>? nestedSnapshotValueBuffer,
        List<uint>? tertiarySnapshotAddressBuffer,
        List<byte>? tertiarySnapshotValueBuffer,
        MintermScratch? mintermScratch,
        VisibilityScratch? visibilityScratch,
        VisibilityScratch? nestedVisibilityScratch,
        MintermScratch? nestedMintermScratch)
    {
        byte[]? originalMintermBuffer = null;
        if (mintermScratch is not null &&
            !mintermScratch.TryAcquire(out originalMintermBuffer, out _, out _))
        {
            return Failure;
        }

        try
        {
            if (pixelVisible is null)
            {
                return ScrollRasterCore(
                    memory, rastPort, deltaX, deltaY, xMin, yMin, xMax, yMax,
                    useBackgroundPen, null, sourcePixelBuffer,
                    snapshotAddressBuffer, snapshotValueBuffer,
                    nestedSnapshotAddressBuffer, nestedSnapshotValueBuffer,
                    tertiarySnapshotAddressBuffer, tertiarySnapshotValueBuffer,
                    originalMintermBuffer, nestedVisibilityScratch, nestedMintermScratch);
            }

            var visibility = visibilityScratch ?? new VisibilityScratch();
            if (!visibility.TryAcquire(pixelVisible, out var stablePixelVisible))
                return Failure;

            try
            {
                return ScrollRasterCore(
                    memory, rastPort, deltaX, deltaY, xMin, yMin, xMax, yMax,
                    useBackgroundPen, stablePixelVisible, sourcePixelBuffer,
                    snapshotAddressBuffer, snapshotValueBuffer,
                    nestedSnapshotAddressBuffer, nestedSnapshotValueBuffer,
                    tertiarySnapshotAddressBuffer, tertiarySnapshotValueBuffer,
                    originalMintermBuffer, nestedVisibilityScratch, nestedMintermScratch);
            }
            finally
            {
                visibility.Release();
            }
        }
        finally
        {
            mintermScratch?.Release();
        }
    }

    private static int ScrollRasterCore(
        IGraphicsMemory memory,
        uint rastPort,
        short deltaX,
        short deltaY,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        bool useBackgroundPen,
        Func<int, int, bool>? pixelVisible,
        List<int>? sourcePixelBuffer,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer,
        List<uint>? nestedSnapshotAddressBuffer,
        List<byte>? nestedSnapshotValueBuffer,
        List<uint>? tertiarySnapshotAddressBuffer,
        List<byte>? tertiarySnapshotValueBuffer,
        byte[]? originalMintermBuffer,
        VisibilityScratch? nestedVisibilityScratch,
        MintermScratch? nestedMintermScratch)
    {
        // Reversed scroll bounds describe an empty affected rectangle in the
        // portable G07 contract. Preserve a readable non-null layer as an
        // explicit provider boundary, but do not require an unreadable or
        // partially mapped layer just to complete the void no-op.
        if (xMax < xMin || yMax < yMin)
        {
            if (TryReadRastPortLong(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortLayer,
                    out var emptyLayer) &&
                emptyLayer != 0 &&
                pixelVisible is null)
            {
                return Failure;
            }

            return Success;
        }

        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return Failure;
        }

        // RPF_NO_PENS is an explicit provider/native colour-ownership
        // boundary.  Admit it before probing the public bitmap or mask;
        // private-colour owners may keep those planar fields unavailable
        // while handling the complete scroll operation.
        if (!TryAdmitPublicPenPath(memory, rastPort) ||
            !TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return Failure;
        }

        // ScrollRaster only publishes pixels through the selected destination
        // planes. A zero mask still crosses the native scroll setup boundary
        // for a real displacement (which restarts linpatcnt), but must not
        // stage a potentially enormous source rectangle that cannot be
        // committed. True no-op geometry remains unchanged below.
        writeMask = EffectiveWriteMask(bitmap, writeMask);

        var left = Math.Max(0, (int)xMin);
        var top = Math.Max(0, (int)yMin);
        var right = Math.Min(bitmap.Width - 1, xMax);
        var bottom = Math.Min(bitmap.Rows - 1, yMax);
        if (left > right || top > bottom)
            return Success;

        // Native ScrollRaster treats a displacement that reaches either
        // logical dimension as an unchanged operation: every source pixel
        // would be scrolled out, so the rectangle is deliberately left
        // untouched. ScrollRasterBF has the same move boundary but clears
        // the complete rectangle with its BPen backfill path below.
        var logicalWidth = (long)right - xMin + 1L;
        var logicalHeight = (long)bottom - yMin + 1L;
        if (logicalWidth <= 0 || logicalHeight <= 0)
            return Success;

        var absoluteDeltaX = Math.Abs((int)deltaX);
        var absoluteDeltaY = Math.Abs((int)deltaY);
        if (useBackgroundPen &&
            (absoluteDeltaX >= logicalWidth || absoluteDeltaY >= logicalHeight))
        {
            return Success;
        }

        if (writeMask == 0)
        {
            // A zero-mask zero-displacement request is a complete no-op.
            // Keep this before the line-pattern update, while allowing a
            // non-zero-mask no-op to continue through destination probing so
            // malformed guest storage still fails closed.
            if (deltaX == 0 && deltaY == 0)
                return Success;

            // The BF full-displacement path reaches EraseRect in native
            // graphics.library, while ordinary ScrollRaster returns before
            // the move when every source cell would be vacated. Both cases
            // are bounded here without probing or staging any source pixels.
            if (!TryAddress(
                    rastPort,
                    GraphicsLayouts.RastPortLinePatternCount,
                    sizeof(byte),
                    out var zeroMaskPatternAddress) ||
                !memory.TryReadByte(zeroMaskPatternAddress, out _) ||
                !memory.TryWriteByte(zeroMaskPatternAddress, 15))
            {
                return Failure;
            }

            return Success;
        }

        if (pixelVisible is not null)
        {
            if (!TryHasVisibleBitmapRegion(
                    bitmap,
                    left,
                    top,
                    right,
                    bottom,
                    pixelVisible,
                    out var hasVisibleDestination))
            {
                return Failure;
            }

            // A provider-backed scroll with no visible destination cells must
            // not consume public BPen/FgPen or minterm storage.  A real move
            // still crosses the native line-state boundary; a zero
            // displacement remains the documented complete no-op.
            if (!hasVisibleDestination)
            {
                if (deltaX == 0 && deltaY == 0)
                    return Success;

                if (!TryAddress(
                        rastPort,
                        GraphicsLayouts.RastPortLinePatternCount,
                        sizeof(byte),
                        out var hiddenPatternAddress) ||
                    !memory.TryReadByte(hiddenPatternAddress, out _) ||
                    !memory.TryWriteByte(hiddenPatternAddress, 15))
                {
                    return Failure;
                }

                return Success;
            }
        }

        // Both ScrollRaster variants use the RastPort's background pen for
        // vacated cells. The BF suffix changes the full-displacement
        // boundary (it erases the rectangle instead of leaving it intact),
        // not the pen selected for the normal vacated strips.
        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortBgPen,
                out var background))
        {
            return Failure;
        }

        var destinationCells = (ulong)(uint)(right - left + 1) *
            (uint)(bottom - top + 1);
        if (destinationCells > PortableWorkLimit / ScrollPassesPerCell)
            return Failure;

        // Keep the source rectangle in the caller's coordinate space.  The
        // logical scroll rectangle may extend beyond the bitmap; clipping the
        // snapshot first would incorrectly turn a source pixel just outside
        // the clipped edge into an in-bounds pixel after a delta is applied.
        var sourceLeft = Math.Max(0, Math.Max((int)xMin, left + deltaX));
        var sourceTop = Math.Max(0, Math.Max((int)yMin, top + deltaY));
        var sourceRight = Math.Min(bitmap.Width - 1, Math.Min((int)xMax, right + deltaX));
        var sourceBottom = Math.Min(bitmap.Rows - 1, Math.Min((int)yMax, bottom + deltaY));
        var sourceWidth = sourceRight >= sourceLeft ? sourceRight - sourceLeft + 1 : 0;
        var sourceHeight = sourceBottom >= sourceTop ? sourceBottom - sourceTop + 1 : 0;
        var sourceCells = (long)sourceWidth * sourceHeight;
        if (sourceCells > int.MaxValue)
            return Failure;

        if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte),
                out var patternCountAddress) ||
            !memory.TryReadByte(patternCountAddress, out var originalPatternCount) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFgPen,
                out var originalForeground) ||
            !TryReadRastPortMintermsOrReconstruct(
                memory,
                rastPort,
                originalForeground,
                out var originalMinterms,
                originalMintermBuffer))
        {
            return Failure;
        }

        var source = sourcePixelBuffer ?? new List<int>((int)sourceCells);
        source.Clear();
        for (var row = 0; row < sourceHeight; row++)
        {
            for (var column = 0; column < sourceWidth; column++)
            {
                if (!TryReadBitmapPixel(
                        memory,
                        bitmap,
                        sourceLeft + column,
                        sourceTop + row,
                        out var sourcePixel))
                {
                    return Failure;
                }
                source.Add(sourcePixel);
            }
        }

        if (!TryProbeBitmapRegion(
                memory,
                bitmap,
                left,
                top,
                right,
                bottom,
                writeMask,
                pixelVisible))
        {
            return Failure;
        }

        // ScrollRasterBF delegates vacated pixels to EraseRect.  When the
        // logical source rectangle has no overlap with the visible clipped
        // destination (for example, a displacement larger than a rectangle
        // whose left edge is already off-screen), every visible destination
        // pixel is vacated.  The strip formulas below only cover one edge in
        // that case and would leave the remainder stale; erase the complete
        // visible rectangle as one transactional backfill instead.
        if (!useBackgroundPen && (sourceWidth == 0 || sourceHeight == 0))
        {
            return EraseRect(
                memory,
                rastPort,
                unchecked((short)left),
                unchecked((short)top),
                unchecked((short)right),
                unchecked((short)bottom),
                pixelVisible,
                nestedSnapshotAddressBuffer,
                nestedSnapshotValueBuffer,
                tertiarySnapshotAddressBuffer,
                tertiarySnapshotValueBuffer,
                nestedMintermScratch,
                nestedVisibilityScratch) == Success
                ? Success
                : Failure;
        }

        // A zero displacement has no moved or vacated cells.  Keep this
        // explicit no-op after the source/destination probes so malformed
        // guest storage still declines at the portable boundary, while a
        // valid request does not manufacture a line-pattern phase reset for
        // an operation that changed neither the raster nor its scroll state.
        if (deltaX == 0 && deltaY == 0)
            return Success;

        if (!TrySnapshotBitmapRegion(
                memory,
                bitmap,
                left,
                top,
                right,
                bottom,
                writeMask,
                out var snapshotAddresses,
                out var snapshotValues,
                snapshotAddressBuffer,
                snapshotValueBuffer,
                pixelVisible))
        {
            return Failure;
        }

        var success = true;
        for (var row = top; row <= bottom; row++)
        {
            for (var column = left; column <= right; column++)
            {
                var sourceX = column + deltaX;
                var sourceY = row + deltaY;
                var color = sourceX >= sourceLeft && sourceX <= sourceRight &&
                    sourceY >= sourceTop && sourceY <= sourceBottom
                    ? source[(sourceY - sourceTop) * sourceWidth + (sourceX - sourceLeft)]
                    : background;
                if (!useBackgroundPen &&
                    (sourceX < sourceLeft || sourceX > sourceRight ||
                     sourceY < sourceTop || sourceY > sourceBottom))
                {
                    // ScrollRasterBF delegates each vacated strip to
                    // EraseRect.  Leave those cells untouched during the
                    // move so the classic BPen/RectFill draw-mode rules can
                    // be applied after the source pixels have landed.
                    continue;
                }

                if (pixelVisible is null || pixelVisible(column, row))
                {
                    success &= SetBitmapPixel(
                        memory,
                        bitmap,
                        column,
                        row,
                        color,
                        0,
                        writeMask);
                }
            }
        }

        if (!success)
        {
            RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
            return Failure;
        }

        if (!useBackgroundPen)
        {
            var backfillSuccess = true;
            // Derive the vacated strips from the clipped source envelope,
            // rather than from the caller's raw xMin/yMin. When a logical
            // rectangle begins off-screen, a negative scroll can expose a
            // visible destination whose source coordinate is still outside
            // the bitmap. Using the unclipped origin would submit an
            // entirely off-screen EraseRect and leave that edge stale.
            var horizontalBackfillLeft = left;
            var horizontalBackfillRight = right;
            if (deltaX > 0)
            {
                horizontalBackfillLeft = Math.Max(
                    left,
                    sourceRight - deltaX + 1);
            }
            else if (deltaX < 0)
            {
                horizontalBackfillRight = Math.Min(
                    right,
                    sourceLeft - deltaX - 1);
            }

            if (deltaX > 0)
            {
                if (horizontalBackfillLeft <= horizontalBackfillRight)
                {
                    backfillSuccess &= EraseRect(
                        memory,
                        rastPort,
                        unchecked((short)horizontalBackfillLeft),
                        yMin,
                        unchecked((short)horizontalBackfillRight),
                        yMax,
                        pixelVisible,
                        nestedSnapshotAddressBuffer,
                        nestedSnapshotValueBuffer,
                        tertiarySnapshotAddressBuffer,
                        tertiarySnapshotValueBuffer,
                        nestedMintermScratch,
                        nestedVisibilityScratch) == Success;
                }
            }
            else if (deltaX < 0)
            {
                if (horizontalBackfillLeft <= horizontalBackfillRight)
                {
                    backfillSuccess &= EraseRect(
                        memory,
                        rastPort,
                        unchecked((short)horizontalBackfillLeft),
                        yMin,
                        unchecked((short)horizontalBackfillRight),
                        yMax,
                        pixelVisible,
                        nestedSnapshotAddressBuffer,
                        nestedSnapshotValueBuffer,
                        tertiarySnapshotAddressBuffer,
                        tertiarySnapshotValueBuffer,
                        nestedMintermScratch,
                        nestedVisibilityScratch) == Success;
                }
            }

            var verticalBackfillTop = top;
            var verticalBackfillBottom = bottom;
            if (deltaY > 0)
            {
                verticalBackfillTop = Math.Max(
                    top,
                    sourceBottom - deltaY + 1);
            }
            else if (deltaY < 0)
            {
                verticalBackfillBottom = Math.Min(
                    bottom,
                    sourceTop - deltaY - 1);
            }

            // The vertical strip is the set difference from the horizontal
            // strip above. Exclude the already-backfilled edge using the
            // derived visible bounds, which handles clipped negative deltas
            // symmetrically with the in-bitmap quadrants.
            var verticalBackfillLeft = left;
            var verticalBackfillRight = right;
            if (deltaX < 0)
            {
                verticalBackfillLeft = Math.Max(
                    left,
                    horizontalBackfillRight + 1);
            }
            else if (deltaX > 0)
            {
                verticalBackfillRight = Math.Min(
                    right,
                    horizontalBackfillLeft - 1);
            }

            if (deltaY > 0)
            {
                // The horizontal vacated strip already covers the full
                // logical height.  Keep the vertical strip's X span on the
                // source-preserving side of that strip so a two-dimensional
                // ScrollRasterBF does not apply EraseRect twice to the
                // corner where the strips overlap.  The distinction is
                // observable for COMPLEMENT draw mode, where a second fill
                // would toggle the corner back to its pre-scroll value.
                // Exclude whichever horizontal vacated strip was already
                // handled above.  A negative deltaX vacates the left edge,
                // so the bottom strip starts after that left strip; a
                // positive deltaX vacates the right edge, so it ends before
                // that right strip.  Keeping both sides conditional makes
                // the backfill a true union for every diagonal direction.
                if (verticalBackfillLeft <= verticalBackfillRight &&
                    verticalBackfillTop <= verticalBackfillBottom)
                {
                    backfillSuccess &= EraseRect(
                        memory,
                        rastPort,
                        unchecked((short)verticalBackfillLeft),
                        unchecked((short)verticalBackfillTop),
                        unchecked((short)verticalBackfillRight),
                        unchecked((short)verticalBackfillBottom),
                        pixelVisible,
                        nestedSnapshotAddressBuffer,
                        nestedSnapshotValueBuffer,
                        tertiarySnapshotAddressBuffer,
                        tertiarySnapshotValueBuffer,
                        nestedMintermScratch,
                        nestedVisibilityScratch) == Success;
                }
            }
            else if (deltaY < 0)
            {
                // Mirror the same union envelope for the top strip.  The
                // horizontal edge may have been vacated on either side;
                // never visit its corner a second time under COMPLEMENT.
                if (verticalBackfillLeft <= verticalBackfillRight &&
                    verticalBackfillTop <= verticalBackfillBottom)
                {
                    backfillSuccess &= EraseRect(
                        memory,
                        rastPort,
                        unchecked((short)verticalBackfillLeft),
                        unchecked((short)verticalBackfillTop),
                        unchecked((short)verticalBackfillRight),
                        unchecked((short)verticalBackfillBottom),
                        pixelVisible,
                        nestedSnapshotAddressBuffer,
                        nestedSnapshotValueBuffer,
                        tertiarySnapshotAddressBuffer,
                        tertiarySnapshotValueBuffer,
                        nestedMintermScratch,
                        nestedVisibilityScratch) == Success;
                }
            }

            if (!backfillSuccess)
            {
                RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
                _ = memory.TryWriteByte(patternCountAddress, originalPatternCount);
                return Failure;
            }

            // EraseRect preserves the phase while temporarily selecting
            // BPen. ScrollRasterBF has its own native phase transition; publish
            // it explicitly so a successful clipped/diagonal move cannot
            // leave a stale preshift when the provider path elides one of its
            // strips. Keep this byte write transactional with the moved
            // pixels and the caller's original phase.
            if (!memory.TryWriteByte(patternCountAddress, 15))
            {
                RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
                _ = memory.TryWriteByte(patternCountAddress, originalPatternCount);
                return Failure;
            }

            return Success;
        }

        if (!memory.TryWriteByte(patternCountAddress, 15))
        {
            RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
            _ = memory.TryWriteByte(patternCountAddress, originalPatternCount);
            return Failure;
        }

        return Success;
    }

    private static bool IsVisibleDestination(
        BitmapInfo bitmap,
        Func<int, int, bool>? pixelVisible,
        int pixelX,
        int pixelY)
        => pixelX >= 0 && pixelY >= 0 &&
            pixelX < bitmap.Width && pixelY < bitmap.Rows &&
            (pixelVisible is null || pixelVisible(pixelX, pixelY));

    private static bool TryEnsureIntegerPen(
        IGraphicsMemory memory,
        uint rastPort,
        bool foreground,
        ref int value,
        ref bool loaded)
    {
        if (loaded)
            return true;
        value = foreground ? GetAPen(memory, rastPort) : GetBPen(memory, rastPort);
        if (value < 0)
            return false;
        loaded = true;
        return true;
    }

    /// <summary>
    /// Flood-fills a non-layered planar bitmap. Mode 0 stops at OutlinePen;
    /// mode 1 fills the connected region matching the seed pixel.
    /// </summary>
    internal static int Flood(
        IGraphicsMemory memory,
        uint rastPort,
        uint mode,
        short x,
        short y)
        => Flood(memory, null, rastPort, mode, x, y);

    /// <summary>
    /// Flood variant used by the graphics-library core. When the caller has
    /// not attached a TmpRas, allocate the visited bitmap through the same
    /// guest allocator boundary as the rest of graphics.library. The
    /// operation owns that scratch allocation only for the duration of the
    /// call; an explicitly attached descriptor remains caller-owned.
    /// </summary>
    internal static int Flood(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend? allocator,
        uint rastPort,
        uint mode,
        short x,
        short y,
        FloodScratch? scratch = null,
        bool allowLayered = false,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (pixelVisible is null)
            return FloodCore(memory, allocator, rastPort, mode, x, y, scratch, allowLayered, null);

        var visibility = scratch?.Visibility ?? new VisibilityScratch();
        if (!visibility.TryAcquire(pixelVisible, out var stablePixelVisible))
            return Failure;

        try
        {
            return FloodCore(
                memory,
                allocator,
                rastPort,
                mode,
                x,
                y,
                scratch,
                allowLayered,
                stablePixelVisible);
        }
        finally
        {
            visibility.Release();
        }
    }

    private static int FloodCore(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend? allocator,
        uint rastPort,
        uint mode,
        short x,
        short y,
        FloodScratch? scratch,
        bool allowLayered,
        Func<int, int, bool>? pixelVisible)
    {
        if (mode > 1 ||
            (!allowLayered && !TryRequireUnlayeredRastPort(memory, rastPort)) ||
            (allowLayered && pixelVisible is null &&
             !TryRequireUnlayeredRastPort(memory, rastPort)))
        {
            return Failure;
        }

        // Flood uses the same public pen/area-pattern path as AreaEnd.  It
        // cannot render a private graphics-context colour request, so honor
        // a readable RPF_NO_PENS marker before probing the public bitmap or
        // write-mask fields.  The marker is an opportunistic hint for sparse
        // ports, not a required field.
        if (TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags) &&
            (flags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return Failure;
        }

        if (!TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return Failure;
        }

        // The seed-colour form (mode 1) never consults OutlinePen.  Keep
        // that field behind the outline-mode branch so an otherwise valid
        // RastPort whose optional outline byte is unreadable remains
        // claimable, matching the native Flood dispatch order.
        byte outlinePen = 0;
        if (mode == 0 &&
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortOutlinePen,
                out outlinePen))
        {
            return Failure;
        }

        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return Failure;

        // Flood cannot publish a destination pixel when no RastPort planes
        // are selected. Keep the public mode/layer/bitmap/mask/seed envelope
        // above, then complete as a bounded BOOL success before inspecting
        // optional TmpRas or area-pattern storage and before staging the full
        // connected raster region.
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
            return FloodSuccess;

        var cellCount = (ulong)(uint)bitmap.Width * (uint)bitmap.Rows;
        if (cellCount > PortableWorkLimit / FloodPassesPerCell)
            return Failure;

        var requiredTmpRasBytes = (ulong)(uint)bitmap.PlaneBytesPerRow * (uint)bitmap.Rows;
        if (requiredTmpRasBytes == 0 ||
            requiredTmpRasBytes > uint.MaxValue ||
            !TryGetOptionalTmpRas(
                memory,
                rastPort,
                (uint)requiredTmpRasBytes,
                out var temporaryRaster,
                out var hasTemporaryRaster))
            return Failure;

        var drawMode = GetDrawMode(memory, rastPort);
        if (drawMode < 0 ||
            !TryReadBitmapPixel(memory, bitmap, x, y, out var seedColor) ||
            !TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtrn,
                out var areaPattern))
        {
            return Failure;
        }

        // Read the signed pattern height before the public pen fields.  A
        // negative AreaPtSz selects encoded multicolour planes; ordinary
        // JAM2 deposits those planes directly and therefore has no scalar
        // FgPen/BgPen dependency.  Keep the height behind the same
        // COMPLEMENT|JAM2 shortcut used by the other pattern paths.
        var areaPatternSize = (byte)0;
        var complementJam2 = (drawMode & 0x03) == 0x03;
        if (areaPattern != 0 &&
            !complementJam2 &&
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortAreaPtSz,
                out areaPatternSize))
        {
            return Failure;
        }

        var multicolorJam2 = areaPattern != 0 &&
            unchecked((sbyte)areaPatternSize) < 0 &&
            (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0;

        // COMPLEMENT ignores both pen colours in the shared planar writer.
        // Keep FgPen outside Flood's admission envelope for that inversion-
        // only form and for encoded multicolour JAM2; patterned and other
        // non-complement modes retain the source foreground dependency.
        // Resolve the public fields lazily after destination visibility is
        // known, so a fully hidden provider region cannot claim sparse pen
        // storage that cannot affect the projected raster.
        var foreground = 0;
        var foregroundLoaded = (drawMode & DrawModeComplement) != 0 || multicolorJam2;

        // A NULL area pattern is an all-one source, and COMPLEMENT ignores
        // both pen colours while toggling each selected destination plane.
        // Only non-complement JAM2 with a real pattern, or inverse-video
        // JAM2 with the implicit all-one pattern, can consume BgPen.  An
        // encoded multicolour JAM2 source carries its own background values.
        var background = 0;
        var requiresBackground = (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0 &&
            (areaPattern == 0
                ? (drawMode & DrawModeInverseVideo) != 0
                : !multicolorJam2);
        var backgroundLoaded = !requiresBackground;
        // Flood may discover a connected region incrementally, but it is one
        // guest operation.  Probe the complete native destination envelope
        // and the optional area-pattern source before the first seed write so
        // a late unreadable row cannot leave a partially filled region.
        if (!TryProbeBitmapRegion(
                memory,
                bitmap,
                0,
                0,
                bitmap.Width - 1,
                bitmap.Rows - 1,
                writeMask,
                pixelVisible) ||
            !TryProbeAreaPattern(
                memory,
                areaPattern,
                unchecked((sbyte)areaPatternSize),
                bitmap.Depth,
                drawMode))
        {
            return Failure;
        }

        List<uint> destinationSnapshotAddresses;
        List<byte> destinationSnapshotValues;
        if (pixelVisible is null
                ? !TrySnapshotBitmapPlanes(
                    memory,
                    bitmap,
                    writeMask,
                    out destinationSnapshotAddresses,
                    out destinationSnapshotValues,
                    scratch?.SnapshotAddresses,
                    scratch?.SnapshotValues)
                : !TrySnapshotVisibleBitmap(
                     memory,
                     bitmap,
                     writeMask,
                     pixelVisible!,
                    out destinationSnapshotAddresses,
                    out destinationSnapshotValues,
                    scratch?.SnapshotAddresses,
                    scratch?.SnapshotValues))
        {
            return Failure;
        }

        var requiredBytes = (uint)requiredTmpRasBytes;
        var ownedTemporaryRaster = 0u;
        var destinationCommitted = false;
        if (!hasTemporaryRaster && allocator is not null)
        {
            if (!allocator.TryAllocate(
                    requiredBytes,
                    GraphicsMemoryClass.Chip,
                    out var allocatedRaster) ||
                    allocatedRaster == 0 ||
                    (allocatedRaster & 1u) != 0 ||
                    !TryProbeContiguous(memory, allocatedRaster, requiredBytes, 1))
            {
                if (allocatedRaster != 0)
                    allocator.Free(allocatedRaster, requiredBytes, GraphicsMemoryClass.Chip);

                return Failure;
            }

            temporaryRaster = allocatedRaster;
            ownedTemporaryRaster = allocatedRaster;
            hasTemporaryRaster = true;
        }

        // A caller-supplied TmpRas is not merely a size hint: Flood uses its
        // byte-per-row layout for the connected-region walk. An allocator-
        // backed scratch raster follows the same guest path, but is released
        // in the finally block below.
        try
        {
            if (hasTemporaryRaster &&
                !ClearTemporaryRaster(memory, temporaryRaster, requiredBytes))
            {
                return Failure;
            }

            var visited = hasTemporaryRaster ? null : new bool[(int)cellCount];
            var pending = scratch?.Pending ?? new List<int>();
            pending.Clear();
            pending.Add(y * bitmap.Width + x);
            var success = true;
            while (pending.Count > 0)
            {
                var pendingIndex = pending.Count - 1;
                var index = pending[pendingIndex];
                pending.RemoveAt(pendingIndex);
                var pixelX = index % bitmap.Width;
                var pixelY = index / bitmap.Width;
                if (hasTemporaryRaster)
                {
                    if (!TryGetTemporaryRasterBit(
                            memory,
                            temporaryRaster,
                            bitmap.PlaneBytesPerRow,
                            pixelX,
                            pixelY,
                            out var alreadyVisited))
                    {
                        success = false;
                        break;
                    }

                    if (alreadyVisited ||
                        !TrySetTemporaryRasterBit(
                            memory,
                            temporaryRaster,
                            bitmap.PlaneBytesPerRow,
                            pixelX,
                            pixelY))
                    {
                        if (alreadyVisited)
                            continue;

                        success = false;
                        break;
                    }
                }
                else
                {
                    if (visited![index])
                        continue;

                    visited[index] = true;
                }

                if (!TryReadBitmapPixel(memory, bitmap, pixelX, pixelY, out var currentColor))
                {
                    success = false;
                    break;
                }

                var matches = mode == 0
                    ? currentColor != outlinePen
                    : currentColor == seedColor;
                if (!matches)
                    continue;

                // Pattern suppression controls only the destination write. It
                // must not stop region discovery: Flood connectivity is based
                // on the source raster, not on whether AreaPt emits a pixel.
                // Apply provider visibility before decoding the pattern or
                // resolving public pens; hidden cells cannot publish a pixel.
                var destinationVisible = pixelVisible is null || pixelVisible(pixelX, pixelY);
                if (destinationVisible)
                {
                    if (!TryEnsureIntegerPen(
                            memory,
                            rastPort,
                            foreground: true,
                            ref foreground,
                            ref foregroundLoaded) ||
                        !TryEnsureIntegerPen(
                            memory,
                            rastPort,
                            foreground: false,
                            ref background,
                            ref backgroundLoaded) ||
                        !TryGetAreaPatternPixel(
                            memory,
                            areaPattern,
                            unchecked((sbyte)areaPatternSize),
                            bitmap.Depth,
                            pixelX,
                            pixelY,
                            unchecked((byte)foreground),
                            unchecked((byte)background),
                            (byte)drawMode,
                            out var fillColor,
                            out var drawPixel))
                    {
                        success = false;
                        break;
                    }

                    if (drawPixel &&
                        !SetBitmapPixel(memory, bitmap, pixelX, pixelY, fillColor, drawMode, writeMask))
                    {
                        success = false;
                        break;
                    }
                }

                if (pixelX > 0)
                    pending.Add(index - 1);
                if (pixelX + 1 < bitmap.Width)
                    pending.Add(index + 1);
                if (pixelY > 0)
                    pending.Add(index - bitmap.Width);
                if (pixelY + 1 < bitmap.Rows)
                    pending.Add(index + bitmap.Width);
            }

            destinationCommitted = success;
            return success ? FloodSuccess : Failure;
        }
        finally
        {
            if (!destinationCommitted)
            {
                RestoreBitmapSnapshot(
                    memory,
                    destinationSnapshotAddresses,
                    destinationSnapshotValues);
            }

            if (ownedTemporaryRaster != 0)
                allocator!.Free(
                    ownedTemporaryRaster,
                    requiredBytes,
                    GraphicsMemoryClass.Chip);
        }
    }

    internal static int SetRast(
        IGraphicsMemory memory,
        uint rastPort,
        uint pen,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        VisibilityScratch? visibilityScratch = null)
    {
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            // SetRast has no meaningful pure-core clipping policy for a
            // Layer.  Let the explicit layers/native provider own the
            // logical surface unless it supplies a visibility predicate.
            return Failure;
        }

        // RPF_NO_PENS is an explicit provider/native colour-ownership
        // boundary. SetRast receives a pen argument rather than consuming
        // FgPen/BgPen, but it still claims the RastPort's standard-planar
        // destination. Admit the marker before probing the public bitmap or
        // mask so a private-colour owner can keep those fields unavailable.
        if (!TryAdmitPublicPenPath(memory, rastPort) ||
            !TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var mask))
        {
            return Failure;
        }

        // No destination planes are selected.  SetRast remains a successful
        // valid-object no-op, but must not walk the complete logical bitmap;
        // a maximum-height guest raster would otherwise turn this harmless
        // masked operation into an unbounded host loop.
        mask = EffectiveWriteMask(bitmap, mask);
        if (mask == 0)
            return Success;

        var selectedPlanes = 0u;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((mask & (1 << plane)) != 0)
                selectedPlanes++;
        }

        if (!TryAdmitSetRastWork(bitmap, selectedPlanes, pixelVisible is not null))
            return Failure;

        // SetRast is one logical initialization transaction. A layered
        // snapshot and publication share one reusable visibility memoizer.
        VisibilityScratch? acquiredVisibility = null;
        var stablePixelVisible = pixelVisible;
        if (pixelVisible is not null)
        {
            acquiredVisibility = visibilityScratch ?? new VisibilityScratch();
            if (!acquiredVisibility.TryAcquire(
                    pixelVisible,
                    out stablePixelVisible))
                return Failure;
        }

        // The ordinary SetRast path snapshots a complete selected plane
        // before publishing any writes.  A maximum 16-bit bitmap can expose
        // a plane span larger than Int32.MaxValue; letting that span reach
        // the byte-by-byte probe/snapshot would turn a valid guest envelope
        // into an unbounded host walk (or a List growth/OOM).  Provider
        // clipped paths retain their visibility-aware bounded staging.
        // SetRast is an unconditional raster initialization vector.  Unlike
        // RectFill/Draw it does not apply the current DrawMode (in particular,
        // COMPLEMENT must not toggle the existing contents instead of setting
        // the requested pen).  The write mask still protects individual
        // bitplanes, matching the classic RastPort contract.
        //
        // A layer/provider supplies a visibility predicate because its
        // logical bitmap can extend beyond the mapped display fragments.  In
        // that case, probing or snapshotting the complete planar allocation
        // would incorrectly fault on an invisible row and would make a
        // clipped SetRast impossible to complete.  Keep the complete-raster
        // probe for the ordinary path, but constrain the transactional
        // snapshot to the same visible pixels used by the write loop.
        try
        {
            List<uint> snapshotAddresses;
            List<byte> snapshotValues;
            if (stablePixelVisible is null)
            {
                if (!TryProbeBitmapRegion(
                    memory,
                    bitmap,
                    0,
                    0,
                    bitmap.Width - 1,
                    bitmap.Rows - 1,
                    mask) ||
                !TrySnapshotBitmapPlanes(
                    memory,
                    bitmap,
                    mask,
                    out snapshotAddresses,
                    out snapshotValues,
                    snapshotAddressBuffer,
                    snapshotValueBuffer))
                {
                    return Failure;
                }
            }
            else if (!TrySnapshotVisibleBitmap(
                         memory,
                         bitmap,
                         mask,
                         stablePixelVisible,
                         out snapshotAddresses,
                         out snapshotValues,
                         snapshotAddressBuffer,
                         snapshotValueBuffer))
            {
                return Failure;
            }

            var success = true;
            for (var y = 0; y < bitmap.Rows; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (stablePixelVisible is not null &&
                        !stablePixelVisible(x, y))
                        continue;

                    if (!SetBitmapPixel(
                        memory,
                        bitmap,
                        x,
                        y,
                        unchecked((byte)pen),
                        0,
                        mask))
                        success = false;
                }
            }

            if (!success)
            {
                RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
                return Failure;
            }

            return Success;
        }
        finally
        {
            acquiredVisibility?.Release();
        }
    }

    private static bool TryAdmitEraseRectWork(
        ulong logicalCells,
        uint selectedPlanes,
        int left,
        int right,
        int top,
        int bottom)
    {
        if (logicalCells == 0 || selectedPlanes == 0)
            return true;

        // The delegated BltPattern path evaluates its source twice and
        // probes/publishes selected planes. EraseRect adds its own outer
        // rollback snapshot around that work. Count the complete worst-case
        // pixel transaction, then add both clipped byte snapshots.
        var pixelPasses = 2UL + (2UL * selectedPlanes);
        if (logicalCells > PortableWorkLimit / pixelPasses)
            return false;

        var firstByte = left >> 3;
        var lastByte = right >> 3;
        var bytesPerRow = (ulong)(uint)(lastByte - firstByte + 1);
        var rows = (ulong)(uint)(bottom - top + 1);
        var snapshotBytes = bytesPerRow * rows * selectedPlanes;
        var total = logicalCells * pixelPasses;
        if (snapshotBytes > PortableWorkLimit / 2 ||
            total > PortableWorkLimit - (snapshotBytes * 2))
        {
            return false;
        }

        return true;
    }

    private static bool TryAdmitDrawEllipseWork(
        BitmapInfo bitmap,
        int left,
        int top,
        int right,
        int bottom,
        uint selectedPlanes,
        ulong pointCount,
        bool visibilityAware)
    {
        if (selectedPlanes == 0 || pointCount == 0)
            return true;

        var clippedCells = GetClippedLogicalCellCount(
            bitmap,
            left,
            top,
            right,
            bottom);
        if (clippedCells == 0)
            return visibilityAware;

        if (clippedCells > PortableWorkLimit)
            return false;

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(bitmap.Width - 1, right);
        var clippedBottom = Math.Min(bitmap.Rows - 1, bottom);
        var firstByte = clippedLeft >> 3;
        var lastByte = clippedRight >> 3;
        var bytesPerRow = (ulong)(uint)(lastByte - firstByte + 1);
        var rows = (ulong)(uint)(clippedBottom - clippedTop + 1);
        var snapshotCells = visibilityAware
            ? clippedCells
            : bytesPerRow * rows;
        if (snapshotCells > PortableWorkLimit / selectedPlanes)
            return false;

        var snapshotWork = snapshotCells * selectedPlanes;
        var pointWork = pointCount * selectedPlanes * 2UL;
        return pointWork <= PortableWorkLimit - snapshotWork;
    }

    private static bool TryAdmitSetRastWork(
        BitmapInfo bitmap,
        uint selectedPlanes,
        bool visibilityAware)
    {
        if (selectedPlanes == 0)
            return true;

        var logicalCells = (ulong)(uint)bitmap.Width * (uint)bitmap.Rows;
        if (logicalCells > PortableWorkLimit / selectedPlanes)
            return false;

        var perPixelWork = logicalCells * selectedPlanes;
        if (visibilityAware)
        {
            // The visibility-aware helper probes/snapshots each visible
            // pixel and the publication loop visits the same logical cells.
            return perPixelWork <= PortableWorkLimit / SetRastPassesPerCell;
        }

        var rowBytes = (ulong)(uint)bitmap.PlaneBytesPerRow * (uint)bitmap.Rows;
        if (rowBytes > PortableWorkLimit / selectedPlanes)
            return false;

        var rowWork = rowBytes * selectedPlanes;
        var total = perPixelWork;
        if (rowWork > PortableWorkLimit - total)
            return false;

        total += rowWork;
        if (rowWork > PortableWorkLimit - total)
            return false;

        return true;
    }

    private static bool TrySnapshotBitmapPlanes(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        byte writeMask,
        out List<uint> snapshotAddresses,
        out List<byte> snapshotValues,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null)
    {
        snapshotAddresses = snapshotAddressBuffer ?? new List<uint>();
        snapshotValues = snapshotValueBuffer ?? new List<byte>();
        snapshotAddresses.Clear();
        snapshotValues.Clear();
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
            return true;

        var byteCount = (ulong)(uint)bitmap.PlaneBytesPerRow * (uint)bitmap.Rows;
        if (byteCount == 0 || byteCount > int.MaxValue)
            return false;

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) == 0)
                continue;

            var planePointerAddress = (ulong)bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes +
                (uint)(plane * 4);
            if (planePointerAddress > uint.MaxValue ||
                !memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var row = 0; row < bitmap.Rows; row++)
            {
                var rowOffset = (ulong)(uint)row * GetPlaneRowStride(
                        checked((ushort)bitmap.GuestBytesPerRow),
                        checked((ushort)bitmap.PlaneBytesPerRow),
                        bitmap.Flags);
                if (rowOffset > uint.MaxValue - planeAddress ||
                    (ulong)(uint)(bitmap.PlaneBytesPerRow - 1) >
                        uint.MaxValue - (ulong)planeAddress - rowOffset)
                    return false;

                for (var offset = 0u; offset < (uint)bitmap.PlaneBytesPerRow; offset++)
                {
                    var address = planeAddress + (uint)rowOffset + offset;
                    if (!memory.TryReadByte(address, out var value))
                        return false;

                    snapshotAddresses.Add(address);
                    snapshotValues.Add(value);
                }
            }
        }

        return true;
    }

    private static bool TrySnapshotVisibleBitmap(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        byte writeMask,
        Func<int, int, bool> pixelVisible,
        out List<uint> snapshotAddresses,
        out List<byte> snapshotValues,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null)
    {
        snapshotAddresses = snapshotAddressBuffer ?? new List<uint>();
        snapshotValues = snapshotValueBuffer ?? new List<byte>();
        snapshotAddresses.Clear();
        snapshotValues.Clear();
        writeMask = EffectiveWriteMask(bitmap, writeMask);

        var selectedPlanes = 0;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) != 0)
                selectedPlanes++;
        }

        // Bits above the declared depth select no storage. Treat that
        // effective zero mask as the same bounded no-op as an explicit zero
        // mask instead of applying a pixel-work budget to a walk that would
        // publish no plane writes.
        if (selectedPlanes == 0)
            return true;

        // SetRast's visibility-aware path snapshots through this helper
        // directly rather than the clipped-region preflight. Keep that
        // provider predicate bounded by the same logical pixel contract so a
        // maximum bitmap cannot force a multi-billion callback walk.
        var touchedPixels = (ulong)(uint)bitmap.Width * (uint)bitmap.Rows;
        if (touchedPixels > int.MaxValue)
            return false;

        for (var y = 0; y < bitmap.Rows; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!pixelVisible(x, y))
                    continue;

                if (!TryProbeBitmapWrite(
                        memory,
                        bitmap,
                        x,
                        y,
                        writeMask,
                        snapshotAddresses,
                        snapshotValues))
                {
                    snapshotAddresses.Clear();
                    snapshotValues.Clear();
                    return false;
                }
            }
        }

        return true;
    }

    internal static void RestoreBitmapSnapshot(
        IGraphicsMemory memory,
        IReadOnlyList<uint> snapshotAddresses,
        IReadOnlyList<byte> snapshotValues)
    {
        for (var index = snapshotAddresses.Count - 1; index >= 0; index--)
            _ = memory.TryWriteByte(snapshotAddresses[index], snapshotValues[index]);
    }

    private static void RestoreEraseRectRastPortState(
        IGraphicsMemory memory,
        uint rastPort,
        byte foreground,
        byte patternCount,
        IReadOnlyList<byte> minterms)
    {
        // EraseRect temporarily routes the classic RectFill path through
        // BPen by changing FgPen.  A failing host-backed write must not leak
        // that implementation detail into the caller's RastPort, so restore
        // all state that SetAPen can touch.  The writes are deliberately
        // best-effort: the original failure may have made one or more guest
        // bytes unreadable, but each independent field should still be
        // recovered when its backing store is available.
        _ = TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortFgPen,
            foreground);
        _ = WriteRastPortMinterms(memory, rastPort, minterms);
        _ = TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortLinePatternCount,
            patternCount);
    }

    internal static int PolyDraw(
        IGraphicsMemory memory,
        uint rastPort,
        ushort count,
        uint pointsAddress,
        Func<int, int, bool>? pixelVisible = null,
        List<(short X, short Y)>? pointBuffer = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        List<uint>? drawSnapshotAddressBuffer = null,
        List<byte>? drawSnapshotValueBuffer = null,
        VisibilityScratch? visibilityScratch = null,
        Func<short, short, short, short, bool>? consumeFirstDotForSegment = null)
    {
        if (pixelVisible is null)
        {
            return PolyDrawCore(
                memory, rastPort, count, pointsAddress, null, pointBuffer,
                snapshotAddressBuffer, snapshotValueBuffer,
                drawSnapshotAddressBuffer, drawSnapshotValueBuffer,
                consumeFirstDotForSegment);
        }

        var scratch = visibilityScratch ?? new VisibilityScratch();
        if (!scratch.TryAcquire(pixelVisible, out var stablePixelVisible))
            return Failure;
        try
        {
            return PolyDrawCore(
                memory, rastPort, count, pointsAddress, stablePixelVisible,
                pointBuffer, snapshotAddressBuffer, snapshotValueBuffer,
                drawSnapshotAddressBuffer, drawSnapshotValueBuffer,
                consumeFirstDotForSegment);
        }
        finally
        {
            scratch.Release();
        }
    }

    private static int PolyDrawCore(
        IGraphicsMemory memory,
        uint rastPort,
        ushort count,
        uint pointsAddress,
        Func<int, int, bool>? pixelVisible,
        List<(short X, short Y)>? pointBuffer,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer,
        List<uint>? drawSnapshotAddressBuffer,
        List<byte>? drawSnapshotValueBuffer,
        Func<short, short, short, short, bool>? consumeFirstDotForSegment)
    {
        // The public prototype names a LONG, but Kickstart 3.1 consumes only
        // the low 16 bits (the original ROM treats it as a UWORD).  An empty
        // standard-planar stream is a no-read success, but a readable layer
        // link still transfers ownership to layers.library/provider code.
        // Keep the layer probe opportunistic so a null or unreadable RastPort
        // retains the classic empty no-op boundary.
        if (count == 0)
        {
            if (TryReadRastPortLong(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortLayer,
                    out var emptyLayer) &&
                emptyLayer != 0 &&
                pixelVisible is null)
            {
                return Failure;
            }

            return Success;
        }

        // Keep the pure PolyDraw entry point behind the same explicit layer
        // ownership boundary as Draw.  A provider may opt in through the
        // pixel predicate; without one, a layered RastPort must remain
        // available to layers.library/native code rather than being painted
        // into its backing planar bitmap.
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return Failure;
        }

        // A private-colour RastPort is owned by the native/provider renderer.
        // Admit that boundary before reading the caller's point stream or any
        // public line state.  The provider may own both the colour contract
        // and the point-buffer address space, so a portable PolyDraw must not
        // consume either before declining.
        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var privateFlags) ||
            (privateFlags & GraphicsLayouts.RastPortNoPens) != 0)
        {
            return Failure;
        }

        // Read the complete caller-owned point stream before touching the
        // RastPort. PolyDraw is one guest operation; a truncated later point
        // must not leave CurrentX/CurrentY at the first point or draw an
        // incomplete chain.
        var pointCount = count;
        var points = pointBuffer ?? new List<(short X, short Y)>(pointCount);
        points.Clear();
        for (var index = 0; index < pointCount; index++)
        {
            var offset = checked((uint)(index * 4));
            if (!TryReadPoint(memory, pointsAddress, offset, out var x, out var y))
                return Failure;

            points.Add((x, y));
        }

        if (!TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                out var originalX) ||
            !TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                out var originalY))
        {
            return Failure;
        }

        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var originalFlags) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out var originalPatternCount))
        {
            return Failure;
        }

        var chainSnapshotAddresses = snapshotAddressBuffer ?? new List<uint>();
        var chainSnapshotValues = snapshotValueBuffer ?? new List<byte>();
        chainSnapshotAddresses.Clear();
        chainSnapshotValues.Clear();
        if (!TryPreflightPolyDraw(
                memory,
                rastPort,
                points,
                chainSnapshotAddresses,
                chainSnapshotValues,
                pixelVisible,
                consumeFirstDotForSegment))
            return Failure;

        // PolyDraw is a connected Draw sequence: the first pair is the first
        // line endpoint, not an implicit Move.  The initial segment therefore
        // starts at the caller's current pen and consumes its current
        // FRST_DOT/line-pattern state exactly as a direct Draw call would.
        var segmentStartX = originalX;
        var segmentStartY = originalY;
        for (var index = 0; index < pointCount; index++)
        {
            var targetX = points[index].X;
            var targetY = points[index].Y;
            var consumeFirstDot = consumeFirstDotForSegment?.Invoke(
                segmentStartX,
                segmentStartY,
                targetX,
                targetY) ?? true;
            if (DrawCore(
                    memory,
                    rastPort,
                    targetX,
                    targetY,
                    pixelVisible,
                    drawSnapshotAddressBuffer,
                    drawSnapshotValueBuffer,
                    consumeFirstDot) != Success)
            {
                RestorePolyDrawTransaction(
                    memory,
                    rastPort,
                    originalFlags,
                    originalPatternCount,
                    originalX,
                    originalY,
                    chainSnapshotAddresses,
                    chainSnapshotValues);
                return Failure;
            }

            segmentStartX = targetX;
            segmentStartY = targetY;
        }

        return Success;
    }

    private static bool TryPreflightPolyDraw(
        IGraphicsMemory memory,
        uint rastPort,
        IReadOnlyList<(short X, short Y)> points,
        List<uint>? snapshotAddresses = null,
        List<byte>? snapshotValues = null,
        Func<int, int, bool>? pixelVisible = null,
        Func<short, short, short, short, bool>? consumeFirstDotForSegment = null)
    {
        if (!TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                out var currentX) ||
            !TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                out var currentY) ||
            !TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask) ||
            !TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePattern,
                out var linePattern) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out var patternCount) ||
            !TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags))
        {
            return false;
        }

        // PolyDraw is a connected sequence of native Draw calls.  Its
        // provider-owned private GC colours have the same boundary as Draw;
        // decline before the preflight probes public APen/BPen.
        if ((flags & GraphicsLayouts.RastPortNoPens) != 0)
            return false;

        var effectiveWriteMask = EffectiveWriteMask(bitmap, writeMask);
        var drawMode = GetDrawMode(memory, rastPort);
        if (drawMode < 0 ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                1) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                GraphicsLayouts.RastPortCurrentY + 2 - GraphicsLayouts.RastPortCurrentX) ||
            !TryRastPortRange(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                2))
        {
            return false;
        }

        // The direct/core path keeps its historical eager public-pen
        // admission before walking the connected destination stream.  Only
        // an explicit provider path may defer these fields until it knows a
        // visible destination write exists.
        if (pixelVisible is null &&
            effectiveWriteMask != 0 &&
            (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0 &&
            GetBPen(memory, rastPort) < 0)
        {
            return false;
        }

        if (pixelVisible is null &&
            effectiveWriteMask != 0 &&
            (drawMode & DrawModeComplement) == 0 &&
            GetAPen(memory, rastPort) < 0)
        {
            return false;
        }

        ulong aggregateLineWork = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var targetX = points[index].X;
            var targetY = points[index].Y;
            var dx = Math.Abs(targetX - currentX);
            var dy = -Math.Abs(targetY - currentY);
            var majorDistance = Math.Max(dx, -dy);
            if (effectiveWriteMask != 0)
            {
                // The Bresenham loop includes both endpoints. ONE_DOT may
                // suppress destination writes, but it still traverses every
                // major-axis sample to preserve the native line phase.
                var segmentWork = (ulong)(uint)majorDistance + 1UL;
                if (segmentWork > PortableWorkLimit - aggregateLineWork)
                    return false;

                aggregateLineWork += segmentWork;
            }

            var consumeFirstDot = consumeFirstDotForSegment?.Invoke(
                unchecked((short)currentX),
                unchecked((short)currentY),
                targetX,
                targetY) ?? true;
            var firstDotRequested = (flags & GraphicsLayouts.RastPortFirstDot) != 0;
            var oneDotMode = (flags & GraphicsLayouts.RastPortOneDot) != 0;
            var majorAxisIsX = dx >= -dy;

            if (!TryPreflightLineDestination(
                    memory,
                    bitmap,
                    currentX,
                    currentY,
                    targetX,
                    targetY,
                    drawMode,
                    writeMask,
                    linePattern,
                    patternCount,
                    oneDotMode,
                    majorAxisIsX,
                    snapshotAddresses,
                    snapshotValues,
                    pixelVisible))
            {
                return false;
            }

            patternCount = unchecked((byte)((patternCount - majorDistance) & 0x0F));
            if (firstDotRequested && consumeFirstDot)
                flags = (ushort)(flags & ~GraphicsLayouts.RastPortFirstDot);

            currentX = targetX;
            currentY = targetY;
        }

        // PolyDraw delegates each segment to Draw, whose planar writer lazily
        // admits public pens at the first visible destination sample.  A
        // provider-backed stream with no staged destination writes therefore
        // must not consume sparse/provider-owned FgPen or BPen storage during
        // preflight.  Keep direct/core admission unchanged, including the
        // historical eager field checks for a malformed public RastPort.
        var admitPublicPens = snapshotAddresses is not null &&
            snapshotAddresses.Count != 0;
        if (effectiveWriteMask != 0 &&
            pixelVisible is not null &&
            admitPublicPens &&
            (drawMode & DrawModeJam2) != 0 &&
            (drawMode & DrawModeComplement) == 0 &&
            GetBPen(memory, rastPort) < 0)
        {
            return false;
        }

        if (effectiveWriteMask != 0 &&
            pixelVisible is not null &&
            admitPublicPens &&
            (drawMode & DrawModeComplement) == 0 &&
            GetAPen(memory, rastPort) < 0)
        {
            return false;
        }

        return true;
    }

    private static void RestorePolyDrawTransaction(
        IGraphicsMemory memory,
        uint rastPort,
        ushort flags,
        byte patternCount,
        short currentX,
        short currentY,
        IReadOnlyList<uint> snapshotAddresses,
        IReadOnlyList<byte> snapshotValues)
    {
        // A nested Draw may have emitted pixels before PolyDraw discovers a
        // rejected final state publication. Restore the chain's guest state
        // byte-wise so a repeated WORD rejection cannot strand a half-drawn
        // polyline.
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortFlags,
            new[] { (byte)(flags >> 8), (byte)flags });
        _ = TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortLinePatternCount,
            patternCount);
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortCurrentX,
            new[] { (byte)((ushort)currentX >> 8), (byte)(ushort)currentX });
        Restore(
            memory,
            rastPort + (uint)GraphicsLayouts.RastPortCurrentY,
            new[] { (byte)((ushort)currentY >> 8), (byte)(ushort)currentY });

        for (var index = snapshotAddresses.Count - 1; index >= 0; index--)
            _ = memory.TryWriteByte(snapshotAddresses[index], snapshotValues[index]);
    }

    internal static int ClearEOL(
        IGraphicsMemory memory,
        uint rastPort,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        List<uint>? nestedSnapshotAddressBuffer = null,
        List<byte>? nestedSnapshotValueBuffer = null,
        VisibilityScratch? visibilityScratch = null,
        MintermScratch? mintermScratch = null)
    {
        byte[]? originalMinterms = null;
        byte[]? setterOriginalMinterms = null;
        byte[]? setterNextMinterms = null;
        if (mintermScratch is not null &&
            !mintermScratch.TryAcquire(
                out originalMinterms,
                out setterOriginalMinterms,
                out setterNextMinterms))
        {
            return Failure;
        }

        try
        {
            if (pixelVisible is null)
            {
                return ClearEolCore(memory, rastPort, null,
                    snapshotAddressBuffer, snapshotValueBuffer,
                    nestedSnapshotAddressBuffer, nestedSnapshotValueBuffer,
                    originalMinterms, setterOriginalMinterms, setterNextMinterms);
            }

            var scratch = visibilityScratch ?? new VisibilityScratch();
            if (!scratch.TryAcquire(pixelVisible, out var stablePixelVisible))
                return Failure;
            try
            {
                return ClearEolCore(memory, rastPort, stablePixelVisible,
                    snapshotAddressBuffer, snapshotValueBuffer,
                    nestedSnapshotAddressBuffer, nestedSnapshotValueBuffer,
                    originalMinterms, setterOriginalMinterms, setterNextMinterms);
            }
            finally
            {
                scratch.Release();
            }
        }
        finally
        {
            mintermScratch?.Release();
        }
    }

    private static int ClearEolCore(
        IGraphicsMemory memory,
        uint rastPort,
        Func<int, int, bool>? pixelVisible,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer,
        List<uint>? nestedSnapshotAddressBuffer,
        List<byte>? nestedSnapshotValueBuffer,
        byte[]? originalMintermBuffer,
        byte[]? setterOriginalMintermBuffer,
        byte[]? setterNextMintermBuffer)
    {
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null) ||
            !TryAdmitPublicPenPath(memory, rastPort) ||
            !TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return Failure;
        }

        var pen = 0;
        byte writeMask;
        if (pixelVisible is null)
        {
            // Keep the direct path's classic clear-pen admission order
            // unchanged.  Provider-backed calls below establish their
            // visible destination envelope first.
            if (!TryReadClearPen(
                    memory,
                    rastPort,
                    bitmap,
                    out pen,
                    out writeMask))
            {
                return Failure;
            }
        }
        else if (!TryReadRastPortByte(
                     memory,
                     rastPort,
                     GraphicsLayouts.RastPortMask,
                     out writeMask))
        {
            return Failure;
        }

        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
        {
            // No destination plane is selected.  Admit the bounded vector
            // before reading text geometry or the cursor position: neither
            // field can affect a clear that has no plane to publish.
            return TryResetClearLinePattern(memory, rastPort)
                ? Success
                : Failure;
        }

        if (!TryReadTextGeometry(memory, rastPort, out var top, out var height))
            return Failure;

        if (!TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                out var x))
            return Failure;

        // ClearEOL delegates its drawable span to RectFill, whose guest
        // coordinates are signed WORDs.  Do not narrow an oversized bitmap
        // edge (or a text row that cannot fit that envelope) into a wrapped
        // negative endpoint: that would turn a real clear into an inverted
        // no-op while claiming the standard-planar vector.
        var clearRight = (long)bitmap.Width - 1L;
        var clearBottom = (long)top + height - 1L;
        if (!FitsSignedGuestCoordinate(top) ||
            !FitsSignedGuestCoordinate(clearRight) ||
            !FitsSignedGuestCoordinate(clearBottom))
        {
            return Failure;
        }

        if (pixelVisible is not null)
        {
            if (!TryHasVisibleBitmapRegion(
                    bitmap,
                    x,
                    top,
                    bitmap.Width - 1,
                    top + height - 1,
                    pixelVisible,
                    out var hasVisibleDestination))
            {
                return Failure;
            }

            // A provider-backed clear whose complete destination row is
            // hidden is a successful clipped no-op.  Publish the native
            // line-pattern transition without consuming provider-owned
            // public pen or minterm storage.
            if (!hasVisibleDestination)
            {
                return TryResetClearLinePattern(memory, rastPort)
                    ? Success
                    : Failure;
            }

            if (!TryReadClearPen(
                    memory,
                    rastPort,
                    bitmap,
                    out pen,
                    out writeMask))
            {
                return Failure;
            }

            writeMask = EffectiveWriteMask(bitmap, writeMask);
        }

        if (writeMask != 0)
        {
            var logicalCells = GetClippedLogicalCellCount(
                bitmap,
                x,
                top,
                bitmap.Width - 1,
                top + height - 1);
            var selectedPlanes = 0u;
            for (var plane = 0; plane < bitmap.Depth; plane++)
            {
                if ((writeMask & (1 << plane)) != 0)
                    selectedPlanes++;
            }

            // ClearEOL retains an outer rollback snapshot and delegates the
            // actual fill to a second preflight/snapshot/publication pass.
            // Admit the depth-multiplied transaction before reading the line
            // phase or any selected plane storage.
            if (selectedPlanes != 0 &&
                logicalCells > PortableWorkLimit /
                    (ClearEolPassesPerCell * selectedPlanes))
            {
                return Failure;
            }
        }

        if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte),
                out var patternCountAddress) ||
            !memory.TryReadByte(patternCountAddress, out var originalPatternCount) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFgPen,
                out var originalForeground) ||
            !TryReadRastPortMintermsOrReconstruct(
                memory,
                rastPort,
                originalForeground,
                out var originalMinterms,
                originalMintermBuffer) ||
            !TrySnapshotBitmapRegion(
                memory,
                bitmap,
                x,
                top,
                bitmap.Width - 1,
                top + height - 1,
                writeMask,
                out var snapshotAddresses,
                out var snapshotValues,
                snapshotAddressBuffer,
                snapshotValueBuffer,
                pixelVisible))
        {
            return Failure;
        }

        // Native ClearEOL selects the clear pen with SetAPen and then calls
        // RectFill without replacing DrawMode. This preserves the combined
        // JAM2|COMPLEMENT and JAM2|INVERSVID minterm behavior instead of
        // collapsing every clear into a plain pixel store.
        var fillSuccess = SetAPen(
                memory, rastPort, (byte)pen,
                setterOriginalMintermBuffer, setterNextMintermBuffer) == Success &&
            RectFillCore(
                memory,
                rastPort,
                unchecked((short)x),
                unchecked((short)top),
                unchecked((short)(bitmap.Width - 1)),
                unchecked((short)(top + height - 1)),
                pixelVisible,
                nestedSnapshotAddressBuffer,
                nestedSnapshotValueBuffer) == Success &&
            SetAPen(
                memory, rastPort, originalForeground,
                setterOriginalMintermBuffer, setterNextMintermBuffer) == Success &&
            memory.TryWriteByte(patternCountAddress, 15);
        if (!fillSuccess)
        {
            RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
            RestoreEraseRectRastPortState(
                memory,
                rastPort,
                originalForeground,
                originalPatternCount,
                originalMinterms);
            return Failure;
        }

        return Success;
    }

    internal static int ClearScreen(
        IGraphicsMemory memory,
        uint rastPort,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        List<uint>? nestedSnapshotAddressBuffer = null,
        List<byte>? nestedSnapshotValueBuffer = null,
        VisibilityScratch? visibilityScratch = null,
        MintermScratch? mintermScratch = null)
    {
        byte[]? originalMinterms = null;
        byte[]? setterOriginalMinterms = null;
        byte[]? setterNextMinterms = null;
        if (mintermScratch is not null &&
            !mintermScratch.TryAcquire(
                out originalMinterms,
                out setterOriginalMinterms,
                out setterNextMinterms))
        {
            return Failure;
        }

        try
        {
            if (pixelVisible is null)
            {
                return ClearScreenCore(memory, rastPort, null,
                    snapshotAddressBuffer, snapshotValueBuffer,
                    nestedSnapshotAddressBuffer, nestedSnapshotValueBuffer,
                    originalMinterms, setterOriginalMinterms, setterNextMinterms);
            }

            var scratch = visibilityScratch ?? new VisibilityScratch();
            if (!scratch.TryAcquire(pixelVisible, out var stablePixelVisible))
                return Failure;
            try
            {
                return ClearScreenCore(memory, rastPort, stablePixelVisible,
                    snapshotAddressBuffer, snapshotValueBuffer,
                    nestedSnapshotAddressBuffer, nestedSnapshotValueBuffer,
                    originalMinterms, setterOriginalMinterms, setterNextMinterms);
            }
            finally
            {
                scratch.Release();
            }
        }
        finally
        {
            mintermScratch?.Release();
        }
    }

    private static int ClearScreenCore(
        IGraphicsMemory memory,
        uint rastPort,
        Func<int, int, bool>? pixelVisible,
        List<uint>? snapshotAddressBuffer,
        List<byte>? snapshotValueBuffer,
        List<uint>? nestedSnapshotAddressBuffer,
        List<byte>? nestedSnapshotValueBuffer,
        byte[]? originalMintermBuffer,
        byte[]? setterOriginalMintermBuffer,
        byte[]? setterNextMintermBuffer)
    {
        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null) ||
            !TryAdmitPublicPenPath(memory, rastPort) ||
            !TryReadBitmap(memory, rastPort, out var bitmap))
        {
            return Failure;
        }

        var pen = 0;
        byte writeMask;
        if (pixelVisible is null)
        {
            // Keep the direct path's classic clear-pen admission order
            // unchanged.  Provider-backed calls below establish their
            // complete visible destination envelope first.
            if (!TryReadClearPen(
                    memory,
                    rastPort,
                    bitmap,
                    out pen,
                    out writeMask))
            {
                return Failure;
            }
        }
        else if (!TryReadRastPortByte(
                     memory,
                     rastPort,
                     GraphicsLayouts.RastPortMask,
                     out writeMask))
        {
            return Failure;
        }

        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
        {
            // Match ClearEOL's bounded zero-mask contract: publish the
            // phase reset before reading text geometry or the cursor
            // position. Neither can affect a clear with no destination plane.
            return TryResetClearLinePattern(memory, rastPort)
                ? Success
                : Failure;
        }

        if (!TryReadTextGeometry(memory, rastPort, out var top, out var height) ||
            !TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                out var x))
        {
            return Failure;
        }

        // ClearScreen uses two RectFill calls below.  Every endpoint must fit
        // the signed WORD graphics ABI before publication; unchecked casts
        // would otherwise wrap a wide Bitmap/height into a reversed rectangle
        // and report a false successful clear.
        var screenRight = (long)bitmap.Width - 1L;
        var screenBottom = (long)bitmap.Rows - 1L;
        var textBottom = (long)top + height - 1L;
        var remainingTop = (long)top + height;
        if (!FitsSignedGuestCoordinate(top) ||
            !FitsSignedGuestCoordinate(screenRight) ||
            !FitsSignedGuestCoordinate(screenBottom) ||
            !FitsSignedGuestCoordinate(textBottom) ||
            !FitsSignedGuestCoordinate(remainingTop))
        {
            return Failure;
        }

        if (pixelVisible is not null)
        {
            if (!TryHasVisibleBitmapRegion(
                    bitmap,
                    x,
                    top,
                    bitmap.Width - 1,
                    top + height - 1,
                    pixelVisible,
                    out var hasVisibleDestination) ||
                !TryHasVisibleBitmapRegion(
                    bitmap,
                    0,
                    top + height,
                    bitmap.Width - 1,
                    bitmap.Rows - 1,
                    pixelVisible,
                    out var hasVisibleRemaining))
            {
                return Failure;
            }

            // The provider owns both the hidden text prefix and the hidden
            // remaining rows.  Complete the native line-state transition
            // without reading public clear pens or reconstructing minterms.
            if (!hasVisibleDestination && !hasVisibleRemaining)
            {
                return TryResetClearLinePattern(memory, rastPort)
                    ? Success
                    : Failure;
            }

            if (!TryReadClearPen(
                    memory,
                    rastPort,
                    bitmap,
                    out pen,
                    out writeMask))
            {
                return Failure;
            }

            writeMask = EffectiveWriteMask(bitmap, writeMask);
        }

        if (writeMask != 0)
        {
            // ClearScreen performs two clipped probes and two publication
            // walks, then retains a rollback snapshot for the selected
            // planes.  Admit the complete transaction before touching either
            // plane pointer; bounding each rectangle independently is not
            // sufficient when their combined logical work exceeds the host
            // contract.
            var firstCells = GetClippedLogicalCellCount(
                bitmap,
                x,
                top,
                bitmap.Width - 1,
                top + height - 1);
            var remainingCells = GetClippedLogicalCellCount(
                bitmap,
                0,
                top + height,
                bitmap.Width - 1,
                bitmap.Rows - 1);
            var logicalCells = firstCells + remainingCells;
            var logicalWork = logicalCells * ClearScreenPassesPerCell;

            var selectedPlanes = 0u;
            for (var plane = 0; plane < bitmap.Depth; plane++)
            {
                if ((writeMask & (1 << plane)) != 0)
                    selectedPlanes++;
            }

            var snapshotBytes = (ulong)(uint)bitmap.PlaneBytesPerRow *
                (uint)bitmap.Rows * selectedPlanes;
            if (logicalWork > PortableWorkLimit ||
                snapshotBytes > PortableWorkLimit ||
                logicalWork > PortableWorkLimit - snapshotBytes)
            {
                return Failure;
            }
        }

        if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte),
                out var patternCountAddress) ||
            !memory.TryReadByte(patternCountAddress, out var originalPatternCount) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFgPen,
                out var originalForeground) ||
            !TryReadRastPortMintermsOrReconstruct(
                memory,
                rastPort,
                originalForeground,
                out var originalMinterms,
                originalMintermBuffer))
        {
            return Failure;
        }

        // ClearScreen is one guest vector even though its text-line prefix
        // and remaining rows are naturally represented by two rectangles.
        // Probe both clipped spans before publishing either one; otherwise a
        // malformed later plane/row could leave the upper text area cleared
        // while the vector reports failure.
        if (!TryProbeBitmapRegion(
                memory,
                bitmap,
                x,
                top,
                bitmap.Width - 1,
                top + height - 1,
                writeMask,
                pixelVisible) ||
            !TryProbeBitmapRegion(
                memory,
                bitmap,
                0,
                top + height,
                bitmap.Width - 1,
                bitmap.Rows - 1,
                writeMask,
                pixelVisible))
        {
            return Failure;
        }

        List<uint> snapshotAddresses;
        List<byte> snapshotValues;
        if (pixelVisible is null)
        {
            if (!TrySnapshotBitmapPlanes(
                    memory,
                    bitmap,
                    writeMask,
                    out snapshotAddresses,
                    out snapshotValues,
                    snapshotAddressBuffer,
                    snapshotValueBuffer))
            {
                return Failure;
            }
        }
        else if (!TrySnapshotBitmapRegion(
                     memory,
                     bitmap,
                     0,
                     0,
                     bitmap.Width - 1,
                     bitmap.Rows - 1,
                     writeMask,
                     out snapshotAddresses,
                     out snapshotValues,
                     snapshotAddressBuffer,
                     snapshotValueBuffer,
                     pixelVisible))
        {
            return Failure;
        }

        // Native ClearScreen performs the text-line ClearEOL and the
        // remaining-row fill through RectFill while the selected clear pen
        // is temporarily installed. Keep DrawMode intact so combined modes
        // retain their normal minterm semantics.
        var fillSuccess = SetAPen(
                memory, rastPort, (byte)pen,
                setterOriginalMintermBuffer, setterNextMintermBuffer) == Success &&
            RectFillCore(
                memory,
                rastPort,
                unchecked((short)x),
                unchecked((short)top),
                unchecked((short)(bitmap.Width - 1)),
                unchecked((short)(top + height - 1)),
                pixelVisible,
                nestedSnapshotAddressBuffer,
                nestedSnapshotValueBuffer) == Success &&
            RectFillCore(
                memory,
                rastPort,
                0,
                unchecked((short)(top + height)),
                unchecked((short)(bitmap.Width - 1)),
                unchecked((short)(bitmap.Rows - 1)),
                pixelVisible,
                nestedSnapshotAddressBuffer,
                nestedSnapshotValueBuffer) == Success &&
            SetAPen(
                memory, rastPort, originalForeground,
                setterOriginalMintermBuffer, setterNextMintermBuffer) == Success &&
            memory.TryWriteByte(patternCountAddress, 15);
        if (!fillSuccess)
        {
            RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
            RestoreEraseRectRastPortState(
                memory,
                rastPort,
                originalForeground,
                originalPatternCount,
                originalMinterms);
            return Failure;
        }

        return Success;
    }

    internal static int EraseRect(
        IGraphicsMemory memory,
        uint rastPort,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        List<uint>? nestedSnapshotAddressBuffer = null,
        List<byte>? nestedSnapshotValueBuffer = null,
        MintermScratch? mintermScratch = null,
        VisibilityScratch? visibilityScratch = null)
    {
        // Reversed EraseRect bounds are an empty non-layered operation. Keep
        // a readable non-null layer as the provider boundary, but do not
        // require an unreadable/partially mapped layer merely to complete the
        // no-op before bitmap, pen, or minterm state is touched.
        if (xMax < xMin || yMax < yMin)
        {
            if (TryReadRastPortLong(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortLayer,
                    out var emptyLayer) &&
                emptyLayer != 0 &&
                pixelVisible is null)
            {
                return Failure;
            }

            return Success;
        }

        if (!TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) ||
            (layer != 0 && pixelVisible is null))
        {
            return Failure;
        }

        // As with the other public-colour drawing vectors, do not consume
        // planar bitmap/mask state after a readable private-colour marker.
        if (!TryAdmitPublicPenPath(memory, rastPort) ||
            !TryReadBitmap(memory, rastPort, out var bitmap) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out var writeMask))
        {
            return Failure;
        }

        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask != 0)
        {
            var left = Math.Max(0, (int)xMin);
            var top = Math.Max(0, (int)yMin);
            var right = Math.Min(bitmap.Width - 1, (int)xMax);
            var bottom = Math.Min(bitmap.Rows - 1, (int)yMax);
            var logicalCells = left <= right && top <= bottom
                ? (ulong)(uint)(right - left + 1) * (uint)(bottom - top + 1)
                : 0;
            var selectedPlanes = 0u;
            for (var plane = 0; plane < bitmap.Depth; plane++)
            {
                if ((writeMask & (1 << plane)) != 0)
                    selectedPlanes++;
            }

            if (selectedPlanes != 0 &&
                !TryAdmitEraseRectWork(
                    logicalCells,
                    selectedPlanes,
                    left,
                    right,
                    top,
                    bottom))
            {
                return Failure;
            }
        }

        // EraseRect wraps an outer rollback snapshot around a nested RectFill.
        // Keep provider ownership stable across both phases so a stateful
        // layer cannot approve a destination cell for the snapshot and hide it
        // when the nested fill publishes. The validated Layers path supplies
        // reusable scratch, while ordinary non-layered calls need no memoizer.
        VisibilityScratch? acquiredVisibilityScratch = null;
        var stablePixelVisible = pixelVisible;
        if (pixelVisible is not null)
        {
            acquiredVisibilityScratch = visibilityScratch ?? new VisibilityScratch();
            if (!acquiredVisibilityScratch.TryAcquire(
                    pixelVisible,
                    out stablePixelVisible))
            {
                return Failure;
            }
        }

        byte[]? originalMintermBuffer = null;
        byte[]? setterOriginalMintermBuffer = null;
        byte[]? setterNextMintermBuffer = null;
        var mintermScratchAcquired = false;
        if (mintermScratch is not null)
        {
            if (!mintermScratch.TryAcquire(
                    out originalMintermBuffer,
                    out setterOriginalMintermBuffer,
                    out setterNextMintermBuffer))
            {
                acquiredVisibilityScratch?.Release();
                return Failure;
            }
            mintermScratchAcquired = true;
        }

        try
        {
            if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte),
                out var patternCountAddress) ||
            !memory.TryReadByte(patternCountAddress, out var originalPatternCount) ||
            !TrySnapshotBitmapRegion(
                memory,
                bitmap,
                xMin,
                yMin,
                xMax,
                yMax,
                writeMask,
                out var snapshotAddresses,
                out var snapshotValues,
                snapshotAddressBuffer,
                snapshotValueBuffer,
                stablePixelVisible))
            {
                return Failure;
            }

            // The native V40.63 EraseRect leaves the caller's line-pattern
            // phase unchanged, including a fully clipped provider-backed
            // operation. Do not consume public FgPen/BgPen or reconstruct
            // minterms when there is no visible destination cell.
            if (stablePixelVisible is not null && snapshotAddresses.Count == 0)
                return Success;

            if (!TryReadRastPortByte(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortFgPen,
                    out var originalForeground) ||
                !TryReadRastPortMintermsOrReconstruct(
                    memory,
                    rastPort,
                    originalForeground,
                    out var originalMinterms,
                    originalMintermBuffer))
            {
                return Failure;
            }

            var backgroundPen = GetBPen(memory, rastPort);
            if (backgroundPen < 0 ||
            SetAPen(
                memory,
                rastPort,
                (byte)backgroundPen,
                setterOriginalMintermBuffer,
                setterNextMintermBuffer) != Success ||
            BltPatternCore(
                memory,
                rastPort,
                0,
                xMin,
                yMin,
                xMax,
                yMax,
                0,
                stablePixelVisible,
                nestedSnapshotAddressBuffer,
                nestedSnapshotValueBuffer,
                false) != Success ||
            SetAPen(
                memory,
                rastPort,
                originalForeground,
                setterOriginalMintermBuffer,
                setterNextMintermBuffer) != Success)
            {
                RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
                RestoreEraseRectRastPortState(
                    memory,
                    rastPort,
                    originalForeground,
                    originalPatternCount,
                    originalMinterms);
                return Failure;
            }

            // The temporary SetAPen calls restart the phase, but the native
            // EraseRect vector preserves the caller-visible line state. Put
            // the saved byte back as the final transactional publication.
            if (!memory.TryWriteByte(patternCountAddress, originalPatternCount))
            {
                RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
                RestoreEraseRectRastPortState(
                    memory,
                    rastPort,
                    originalForeground,
                    originalPatternCount,
                    originalMinterms);
                return Failure;
            }

            return Success;
        }
        finally
        {
            if (mintermScratchAcquired)
                mintermScratch!.Release();
            acquiredVisibilityScratch?.Release();
        }
    }

    internal static int TextLength(IGraphicsMemory memory, uint rastPort, uint count)
    {
        return TryTextLength(memory, rastPort, count, out var length)
            ? length
            : Failure;
    }

    internal static bool TryTextLength(
        IGraphicsMemory memory,
        uint rastPort,
        uint count,
        out int length)
        => TryMeasureFixedText(memory, rastPort, count, out length);

    internal static int AdvanceText(IGraphicsMemory memory, uint rastPort, uint count)
    {
        // Do not use TextLength's -1 failure sentinel here: a valid signed
        // TxSpacing sequence can itself measure -1 pixel.  Keep the success
        // bit separate so the cursor can advance in either direction.
        if (!TryMeasureFixedText(memory, rastPort, count, out var length) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortCurrentX,
                sizeof(short),
                out var currentXAddress) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                sizeof(short),
                out var currentYAddress) ||
            !TryReadSignedWord(memory, currentXAddress, out var x) ||
            !TryReadSignedWord(memory, currentYAddress, out var y))
            return Failure;

        var next = (long)x + length;
        var result = Move(memory, rastPort, unchecked((short)next), y);
        return result == Success ? length : Failure;
    }

    private static bool TryMeasureFixedText(
        IGraphicsMemory memory,
        uint rastPort,
        uint count,
        out int length)
    {
        length = 0;
        if (rastPort == 0 || (rastPort & 1u) != 0)
            return false;

        if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortTextWidth,
                sizeof(ushort),
                out var textWidthAddress) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortTextSpacing,
                sizeof(ushort),
                out var textSpacingAddress) ||
            !memory.TryReadWord(textWidthAddress, out var textWidth) ||
            !memory.TryReadWord(textSpacingAddress, out var rawSpacing))
        {
            return false;
        }

        if (count == 0)
            return true;

        // TxSpacing is a signed WORD.  Reverse-path and deliberately
        // condensed fonts use negative inter-character spacing; promoting
        // the raw guest word to an unsigned value turns a valid negative
        // length into a spurious overflow.
        var spacing = unchecked((short)rawSpacing);
        // TxSpacing is the extra displacement for every rendered character,
        // including the final character.  TextLength is the value that Text
        // adds to cp_x after the complete string, so omitting the trailing
        // per-character spacing would leave TextLength/Text out of sync.
        var measured = ((long)textWidth * count) + ((long)spacing * count);
        if (measured < int.MinValue || measured > int.MaxValue)
            return false;

        length = (int)measured;
        return true;
    }

    private static int SetByte(IGraphicsMemory memory, uint baseAddress, int offset, byte value)
        => TryWriteAddressedByte(memory, baseAddress, offset, value) ? Success : Failure;

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

    private static bool TryRastPortRange(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        int byteCount)
        // Address zero is a valid byte-address in some host test memories,
        // but it is not a valid guest RastPort base for a native 68000 call.
        // Keep the shared envelope guard from claiming a NULL/provider-owned
        // object before any field probe.
        => rastPort != 0 &&
           TryAddress(rastPort, offset, byteCount, out var address) &&
           TryProbeRange(memory, address, byteCount);

    internal static bool TryReadRastPortByte(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out byte value)
    {
        value = 0;
        // RastPort contains word/long fields even when this particular
        // operation is reading a byte pen or mode.  A native 68000 cannot
        // consume the object from an odd base, so keep a host byte-addressable
        // memory model from claiming that foreign/malformed object.
        return rastPort != 0 && (rastPort & 1u) == 0 &&
               TryAddress(rastPort, offset, sizeof(byte), out var address) &&
               memory.TryReadByte(address, out value);
    }

    internal static bool TryReadRastPortLong(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out uint value)
    {
        value = 0;
        return rastPort != 0 && (rastPort & 1u) == 0 &&
               TryAddress(rastPort, offset, sizeof(uint), out var address) &&
               memory.TryReadLong(address, out value);
    }

    private static bool TryRequireUnlayeredRastPort(
        IGraphicsMemory memory,
        uint rastPort)
        => TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLayer,
                out var layer) &&
           layer == 0;

    internal static bool TryWriteRastPortByte(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        byte value)
        => rastPort != 0 && (rastPort & 1u) == 0 &&
           TryAddress(rastPort, offset, sizeof(byte), out var address) &&
           memory.TryWriteByte(address, value);

    internal static bool TryWriteRastPortLong(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        uint value)
        => rastPort != 0 && (rastPort & 1u) == 0 &&
           TryAddress(rastPort, offset, sizeof(uint), out var address) &&
           memory.TryWriteLong(address, value);

    private static bool TryReadRastPortMinterms(
        IGraphicsMemory memory,
        uint rastPort,
        out byte[] minterms,
        byte[]? buffer = null)
    {
        minterms = buffer ?? new byte[8];
        if (minterms.Length != 8)
            return false;
        for (var index = 0; index < minterms.Length; index++)
        {
            if (!TryReadRastPortByte(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortMinterms + index,
                    out minterms[index]))
            {
                minterms = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static bool TryReadRastPortMintermsOrReconstruct(
        IGraphicsMemory memory,
        uint rastPort,
        byte foreground,
        out byte[] minterms,
        byte[]? buffer = null)
    {
        minterms = buffer ?? new byte[8];
        if (minterms.Length != 8)
            return false;

        // The resident clear/scroll vectors only need their old minterms as
        // rollback data.  The cache is derived from the public scalar pens
        // and draw mode, so a provider-backed RastPort may fault reads from
        // this private span while still permitting a complete transaction.
        if (TryReadRastPortMinterms(memory, rastPort, out _, minterms))
            return true;

        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortBgPen,
                out var background) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortDrawMode,
                out var drawMode))
        {
            minterms = Array.Empty<byte>();
            return false;
        }

        ComputeMinterms(foreground, background, drawMode, minterms);
        return true;
    }

    private static bool WriteRastPortMinterms(
        IGraphicsMemory memory,
        uint rastPort,
        IReadOnlyList<byte> minterms)
    {
        if (minterms.Count != 8)
            return false;

        for (var index = 0; index < minterms.Count; index++)
        {
            if (!TryWriteRastPortByte(
                    memory,
                    rastPort,
                    GraphicsLayouts.RastPortMinterms + index,
                    minterms[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ComputeMinterms(
        byte foreground,
        byte background,
        byte drawMode,
        byte[]? buffer = null)
    {
        var minterms = buffer ?? new byte[8];
        if (minterms.Length != 8)
            throw new ArgumentException("Minterm buffer must contain exactly eight bytes.", nameof(buffer));
        if ((drawMode & DrawModeComplement) != 0)
        {
            var complementMinterm = (byte)((drawMode & DrawModeInverseVideo) != 0 ? 0x6A : 0x9A);
            Array.Fill(minterms, complementMinterm);
            return minterms;
        }

        for (var plane = 0; plane < minterms.Length; plane++)
        {
            var minA = ((foreground >> plane) & 1) * 3;
            var minB = (drawMode & DrawModeJam2) != 0
                ? ((background >> plane) & 1) * 3
                : 2;
            var selector = (drawMode & DrawModeInverseVideo) != 0
                ? (minB << 2) | minA
                : (minA << 2) | minB;
            minterms[plane] = (byte)((selector << 4) | 0x0A);
        }

        return minterms;
    }

    internal static bool TryReadRastPortWord(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out ushort value)
    {
        value = 0;
        return rastPort != 0 && (rastPort & 1u) == 0 &&
               TryAddress(rastPort, offset, sizeof(ushort), out var address) &&
               memory.TryReadWord(address, out value);
    }

    internal static bool TryWriteRastPortWord(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        ushort value)
        => rastPort != 0 && (rastPort & 1u) == 0 &&
           TryAddress(rastPort, offset, sizeof(ushort), out var address) &&
           memory.TryWriteWord(address, value);

    internal static bool TryReadRastPortSignedWord(
        IGraphicsMemory memory,
        uint rastPort,
        int offset,
        out short value)
    {
        if (!TryReadRastPortWord(memory, rastPort, offset, out var raw))
        {
            value = 0;
            return false;
        }

        value = unchecked((short)raw);
        return true;
    }

    private static bool TryWriteAddressedByte(
        IGraphicsMemory memory,
        uint baseAddress,
        int offset,
        byte value)
        => TryAddress(baseAddress, offset, sizeof(byte), out var address) &&
           memory.TryWriteByte(address, value);

    private static int SetByteAndRestartPattern(
        IGraphicsMemory memory,
        uint baseAddress,
        int offset,
        byte value,
        byte[]? oldMintermBuffer = null,
        byte[]? nextMintermBuffer = null)
    {
        // Read the prior scalar fields through the shared RastPort helpers as
        // well.  A host byte-addressable odd/NULL base must be rejected before
        // even the rollback snapshot probes it.
        if (!TryAddress(baseAddress, offset, sizeof(byte), out var valueAddress) ||
            !TryAddress(
                baseAddress,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte),
                out var patternCountAddress) ||
            !TryReadRastPortByte(memory, baseAddress, offset, out var oldValue) ||
            !TryReadRastPortByte(
                memory,
                baseAddress,
                GraphicsLayouts.RastPortLinePatternCount,
                out var oldPatternCount))
        {
            return Failure;
        }

        // This setter publishes one requested byte, all eight derived
        // minterms, the line-pattern phase, and the Flags word as a single
        // RastPort state transition.  Read-probe the scalar destinations and
        // address-check the derived cache before the first write; otherwise a
        // truncated guest object could expose a new pen/mode and only
        // discover the missing tail while publishing the derived state.  The
        // later rollback still handles a provider that deliberately faults an
        // otherwise mapped write, but a read-faulting cache remains claimable.
        if (!TryRastPortRange(memory, baseAddress, offset, sizeof(byte)) ||
            !TryRastPortRange(
                memory,
                baseAddress,
                GraphicsLayouts.RastPortLinePatternCount,
                sizeof(byte)) ||
            !TryRastPortRange(
                memory,
                baseAddress,
                GraphicsLayouts.RastPortFlags,
                sizeof(ushort)) ||
            !TryAddress(
                baseAddress,
                GraphicsLayouts.RastPortMinterms,
                8,
                out _))
        {
            return Failure;
        }

        if (!TryReadRastPortByte(memory, baseAddress, GraphicsLayouts.RastPortFgPen, out var currentForeground) ||
            !TryReadRastPortByte(memory, baseAddress, GraphicsLayouts.RastPortBgPen, out var currentBackground) ||
            !TryReadRastPortByte(memory, baseAddress, GraphicsLayouts.RastPortDrawMode, out var currentDrawMode) ||
            !TryReadRastPortWord(memory, baseAddress, GraphicsLayouts.RastPortFlags, out var oldFlags))
        {
            return Failure;
        }

        var oldMinterms = oldMintermBuffer ?? new byte[8];
        if (oldMinterms.Length != 8)
            return Failure;
        if (!TryReadRastPortMinterms(memory, baseAddress, out _, oldMinterms))
            ComputeMinterms(currentForeground, currentBackground, currentDrawMode, oldMinterms);

        var nextForeground = offset == GraphicsLayouts.RastPortFgPen ? value : currentForeground;
        var nextBackground = offset == GraphicsLayouts.RastPortBgPen ? value : currentBackground;
        var nextDrawMode = offset == GraphicsLayouts.RastPortDrawMode ? value : currentDrawMode;
        var nextMinterms = ComputeMinterms(
            nextForeground,
            nextBackground,
            nextDrawMode,
            nextMintermBuffer);
        var nextFlags = (ushort)(oldFlags & ~GraphicsLayouts.RastPortNoPens);

        return memory.TryWriteByte(valueAddress, value) &&
               WriteRastPortMinterms(memory, baseAddress, nextMinterms) &&
               RestartLinePattern(memory, baseAddress) &&
               TryWriteRastPortWord(memory, baseAddress, GraphicsLayouts.RastPortFlags, nextFlags)
            ? Success
            : RestoreByteSetter(
                memory,
                baseAddress,
                offset,
                oldValue,
                oldPatternCount,
                oldFlags,
                oldMinterms);
    }

    private static int RestoreByteSetter(
        IGraphicsMemory memory,
        uint baseAddress,
        int offset,
        byte oldValue,
        byte oldPatternCount,
        ushort oldFlags,
        byte[] oldMinterms)
    {
        _ = TryWriteAddressedByte(memory, baseAddress, offset, oldValue);
        _ = TryWriteAddressedByte(
            memory,
            baseAddress,
            GraphicsLayouts.RastPortLinePatternCount,
            oldPatternCount);
        Restore(
            memory,
            baseAddress + (uint)GraphicsLayouts.RastPortFlags,
            new[] { (byte)(oldFlags >> 8), (byte)oldFlags });
        _ = WriteRastPortMinterms(memory, baseAddress, oldMinterms);
        return Failure;
    }

    private static bool RestartLinePattern(IGraphicsMemory memory, uint rastPort)
        => TryWriteRastPortByte(
            memory,
            rastPort,
            GraphicsLayouts.RastPortLinePatternCount,
            15);

    private static bool TryClear(
        IGraphicsMemory memory,
        uint address,
        int size,
        out byte[] original)
    {
        if (!TrySnapshot(memory, address, size, out original))
            return false;

        for (var offset = 0; offset < size; offset++)
        {
            if (!memory.TryWriteByte(address + (uint)offset, 0))
            {
                Restore(memory, address, original);
                return false;
            }
        }

        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        int size,
        out byte[] original,
        byte[]? buffer = null)
    {
        original = Array.Empty<byte>();
        if (!TryGetAddress(address, size, out _))
            return false;

        original = buffer ?? new byte[size];
        if (original.Length < size)
            return false;
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
        byte[] original,
        int length = -1)
    {
        var byteCount = length < 0 ? original.Length : Math.Min(length, original.Length);
        for (var offset = 0; offset < byteCount; offset++)
            _ = memory.TryWriteByte(address + (uint)offset, original[offset]);
    }

    private static bool TryReadSignedWord(IGraphicsMemory memory, uint address, out short value)
    {
        if (!memory.TryReadWord(address, out var raw))
        {
            value = 0;
            return false;
        }

        value = unchecked((short)raw);
        return true;
    }

    private static bool TryDecodeArea(
        IGraphicsMemory memory,
        AreaState state,
        out List<List<AreaPoint>> polygons,
        out List<AreaEllipseInfo> ellipses,
        out List<AreaShape> shapes,
        AreaScratch? scratch = null)
    {
        polygons = scratch?._polygons ?? new List<List<AreaPoint>>();
        ellipses = scratch?._ellipses ?? new List<AreaEllipseInfo>();
        shapes = scratch?._shapes ?? new List<AreaShape>();
        polygons.Clear();
        ellipses.Clear();
        shapes.Clear();
        var polygonIndex = 0;
        List<AreaPoint>? current = null;

        for (var index = 0u; index < state.Count; index++)
        {
            if (!TryReadAreaPoint(memory, state.VectorTable, index, out var point) ||
                !memory.TryReadByte(state.FlagTable + index, out var flag))
            {
                return false;
            }

            switch (flag)
            {
                case AreaMoveFlag:
                    current = scratch?.AcquirePolygon(polygonIndex++) ??
                        new List<AreaPoint>();
                    polygons.Add(current);
                    shapes.Add(AreaShape.FromPolygon(current));
                    current.Add(point);
                    break;
                case AreaEllipseFlag:
                    if (index + 1u >= state.Count ||
                        !memory.TryReadByte(state.FlagTable + index + 1u, out var dataFlag) ||
                        dataFlag != AreaEllipseFlag ||
                        !TryReadAreaPoint(memory, state.VectorTable, index + 1u, out var radii) ||
                        radii.X <= 0 || radii.Y <= 0)
                    {
                        return false;
                    }

                    var ellipse = new AreaEllipseInfo(point.X, point.Y, radii.X, radii.Y);
                    ellipses.Add(ellipse);
                    shapes.Add(AreaShape.FromEllipse(ellipse));
                    current = null;
                    index++;
                    break;
                case AreaDrawFlag:
                    if (current is null)
                        return false;

                    current.Add(point);
                    break;
                case AreaCloseDrawFlag:
                    if (current is null)
                        return false;

                    current.Add(point);
                    current = null;
                    break;
                default:
                    return false;
            }
        }

        return polygons.Count != 0 || ellipses.Count != 0;
    }

    private static bool TryReadAreaPoint(
        IGraphicsMemory memory,
        uint vectorTable,
        uint index,
        out AreaPoint point)
    {
        point = default;
        var offset = (ulong)index * 4u;
        if ((ulong)vectorTable + offset > uint.MaxValue - 3u)
            return false;

        var address = vectorTable + (uint)offset;
        if (!TryReadSignedWord(memory, address, out var x) ||
            !TryReadSignedWord(memory, address + 2u, out var y))
        {
            return false;
        }

        point = new AreaPoint(x, y);
        return true;
    }

    private static bool FillAreaShapes(
        IGraphicsMemory memory,
        uint rastPort,
        BitmapInfo bitmap,
        List<AreaShape> shapes,
        int foreground,
        byte background,
        int drawMode,
        byte writeMask,
        uint areaPattern,
        sbyte areaPatternSize,
        uint temporaryRaster,
        bool drawOutlines,
        int outlinePen,
        bool noCrossFill,
        AreaScratch? scratch = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        // Kickstart processes each recorded shape in sequence: it builds a
        // mask for one polygon/ellipse, applies BltPattern, then advances to
        // the next item. Keeping that transaction boundary matters for
        // COMPLEMENT, where overlapping shapes must toggle the destination
        // once per shape rather than once for the union of all shapes. The
        // public NOCROSSFILL flag selects this same per-shape mask boundary;
        // carry it explicitly so a future CopperSharp68k/native area filler
        // can diverge without changing the host/provider call contract.
        _ = noCrossFill;
        foreach (var shape in shapes)
        {
            if (!shape.IsEllipse &&
                (shape.Polygon is null || shape.Polygon.Count < 3))
            {
                continue;
            }

            if (!TryGetAreaShapeBounds(shape, out var shapeBounds))
                return false;

            bool[]? managedMask = null;
            if (temporaryRaster == 0 &&
                !(scratch is null
                    ? TryCreateManagedAreaMask(shapeBounds, out managedMask)
                    : TryAcquireManagedAreaMask(
                        scratch,
                        shapeBounds,
                        out managedMask)))
            {
                return false;
            }

            if (shape.IsEllipse)
            {
                if (!ClearAreaMask(memory, temporaryRaster, shapeBounds, managedMask) ||
                    !FillAreaEllipse(
                        memory,
                        bitmap,
                        shape.Ellipse,
                        foreground,
                        background,
                        drawMode,
                        writeMask,
                        areaPattern,
                        areaPatternSize,
                        temporaryRaster,
                        shapeBounds,
                        managedMask,
                        pixelVisible))
                {
                    return false;
                }

                if (drawOutlines &&
                    !DrawAreaShapeOutline(
                        memory,
                        rastPort,
                        shape,
                        outlinePen,
                        foreground,
                        scratch,
                        pixelVisible))
                {
                    return false;
                }

                continue;
            }

            var polygon = shape.Polygon!;

            if (!ClearAreaMask(memory, temporaryRaster, shapeBounds, managedMask) ||
                !FillAreaPolygon(
                    memory,
                    bitmap,
                    polygon,
                    foreground,
                    background,
                    drawMode,
                    writeMask,
                    areaPattern,
                    areaPatternSize,
                    temporaryRaster,
                    shapeBounds,
                    managedMask,
                    scratch,
                    pixelVisible))
            {
                return false;
            }

            if (drawOutlines &&
                !DrawAreaShapeOutline(
                    memory,
                    rastPort,
                shape,
                outlinePen,
                foreground,
                scratch,
                pixelVisible))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FillAreaPolygon(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        List<AreaPoint> polygon,
        int foreground,
        byte background,
        int drawMode,
        byte writeMask,
        uint areaPattern,
        sbyte areaPatternSize,
        uint temporaryRaster,
        AreaRasterBounds areaBounds,
        bool[]? managedMask,
        AreaScratch? scratch = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        var minimumY = bitmap.Rows;
        var maximumY = -1;
        var minimumX = bitmap.Width;
        var maximumX = -1;
        var edges = scratch?._edges ?? new List<AreaEdge>();
        edges.Clear();
        foreach (var point in polygon)
        {
            minimumX = Math.Min(minimumX, point.X);
            maximumX = Math.Max(maximumX, point.X);
            minimumY = Math.Min(minimumY, point.Y);
            maximumY = Math.Max(maximumY, point.Y);
        }

        for (var index = 0; index < polygon.Count; index++)
        {
            var first = polygon[index];
            var second = polygon[(index + 1) % polygon.Count];
            if (first.Y != second.Y)
                edges.Add(new AreaEdge(first, second));
        }

        minimumY = Math.Max(0, minimumY);
        maximumY = Math.Min(bitmap.Rows - 1, maximumY);
        if (minimumY <= maximumY)
        {
            var crossings = scratch?._crossings ??
                new List<AreaCrossing>(edges.Count);
            crossings.Clear();
            for (var y = minimumY; y <= maximumY; y++)
            {
                crossings.Clear();
                foreach (var edge in edges)
                    AddEdgeCrossing(edge, y, crossings);

                crossings.Sort(CompareCrossings);
                for (var crossing = 0; crossing + 1 < crossings.Count; crossing += 2)
                {
                    var left = CeilDivide(crossings[crossing].Numerator, crossings[crossing].Denominator);
                    var right = CeilDivide(
                                    crossings[crossing + 1].Numerator,
                                    crossings[crossing + 1].Denominator) - 1;
                    var clippedLeft = Math.Max(0L, left);
                    var clippedRight = Math.Min(bitmap.Width - 1L, right);
                    for (var x = clippedLeft; x <= clippedRight; x++)
                    {
                        if (!TrySetAreaMaskBit(
                                memory,
                                temporaryRaster,
                                managedMask,
                                areaBounds,
                                (int)x - areaBounds.MinimumX,
                                y - areaBounds.MinimumY))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return ApplyAreaMask(
            memory,
            bitmap,
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            foreground,
            background,
            drawMode,
            writeMask,
            areaPattern,
            areaPatternSize,
            temporaryRaster,
            areaBounds,
            managedMask,
            pixelVisible);
    }

    private static bool FillAreaEllipse(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        AreaEllipseInfo ellipse,
        int foreground,
        byte background,
        int drawMode,
        byte writeMask,
        uint areaPattern,
        sbyte areaPatternSize,
        uint temporaryRaster,
        AreaRasterBounds areaBounds,
        bool[]? managedMask,
        Func<int, int, bool>? pixelVisible = null)
    {
        var minimumY = Math.Max(0, ellipse.CenterY - ellipse.RadiusY);
        var maximumY = Math.Min(bitmap.Rows - 1, ellipse.CenterY + ellipse.RadiusY);
        if (minimumY <= maximumY)
        {
            var radiusY = (long)ellipse.RadiusY;
            var radiusX = (long)ellipse.RadiusX;
            var radiusY2 = radiusY * radiusY;
            for (var y = minimumY; y <= maximumY; y++)
            {
                var dy = (long)y - ellipse.CenterY;
                var remaining = radiusY2 - (dy * dy);
                if (remaining < 0)
                    continue;

                var numerator = radiusX * radiusX * remaining;
                var halfWidth = IntegerSqrt(numerator / radiusY2);
                var clippedLeft = Math.Max(0L, (long)ellipse.CenterX - halfWidth);
                var clippedRight = Math.Min(bitmap.Width - 1L, (long)ellipse.CenterX + halfWidth);
                for (var x = clippedLeft; x <= clippedRight; x++)
                {
                    if (!TrySetAreaMaskBit(
                            memory,
                            temporaryRaster,
                            managedMask,
                            areaBounds,
                            (int)x - areaBounds.MinimumX,
                            y - areaBounds.MinimumY))
                    {
                        return false;
                    }
                }
            }
        }

        return ApplyAreaMask(
            memory,
            bitmap,
            ellipse.CenterX - ellipse.RadiusX,
            ellipse.CenterY - ellipse.RadiusY,
            ellipse.CenterX + ellipse.RadiusX,
            ellipse.CenterY + ellipse.RadiusY,
            foreground,
            background,
            drawMode,
            writeMask,
            areaPattern,
            areaPatternSize,
            temporaryRaster,
        areaBounds,
            managedMask,
            pixelVisible);
    }

    private static bool ApplyAreaMask(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY,
        int foreground,
        byte background,
        int drawMode,
        byte writeMask,
        uint areaPattern,
        sbyte areaPatternSize,
        uint temporaryRaster,
        AreaRasterBounds areaBounds,
        bool[]? managedMask,
        Func<int, int, bool>? pixelVisible = null)
    {
        // AreaEnd's explicit layer/provider path may carry a shape whose
        // bounds extend beyond the planar backing bitmap.  Clamp the
        // publication walk to the bitmap envelope before consulting the
        // provider; the direct non-layered path rejects such a shape during
        // destination preflight.
        var clippedMinimumX = Math.Max(0, Math.Max(areaBounds.MinimumX, minimumX));
        var clippedMinimumY = Math.Max(0, Math.Max(areaBounds.MinimumY, minimumY));
        var clippedMaximumX = Math.Min(bitmap.Width - 1, Math.Min(areaBounds.MaximumX, maximumX));
        var clippedMaximumY = Math.Min(bitmap.Rows - 1, Math.Min(areaBounds.MaximumY, maximumY));
        for (var y = clippedMinimumY; y <= clippedMaximumY; y++)
        {
            for (var x = clippedMinimumX; x <= clippedMaximumX; x++)
            {
                if (!TryGetAreaMaskBit(
                        memory,
                        temporaryRaster,
                        managedMask,
                        areaBounds,
                        x - areaBounds.MinimumX,
                        y - areaBounds.MinimumY,
                        out var maskBit))
                {
                    return false;
                }

                if (!maskBit)
                    continue;

                if (pixelVisible is not null && !pixelVisible(x, y))
                    continue;

                if (!TryGetAreaPatternPixel(
                        memory,
                        areaPattern,
                        areaPatternSize,
                        bitmap.Depth,
                        x,
                        y,
                        (byte)foreground,
                        background,
                        (byte)drawMode,
                        out var color,
                        out var drawPixel))
                {
                    return false;
                }

                if (drawPixel && !SetBitmapPixel(memory, bitmap, x, y, color, drawMode, writeMask))
                    return false;
            }
        }

        return true;
    }

    private static bool ClearAreaMask(
        IGraphicsMemory memory,
        uint temporaryRaster,
        AreaRasterBounds areaBounds,
        bool[]? managedMask)
    {
        if (temporaryRaster != 0)
        {
            var bytes = (ulong)(uint)areaBounds.BytesPerRow * (uint)areaBounds.Height;
            return bytes <= uint.MaxValue && ClearTemporaryRaster(memory, temporaryRaster, (uint)bytes);
        }

        if (managedMask is null)
            return false;

        Array.Clear(managedMask, 0, managedMask.Length);
        return true;
    }

    private static void AddEdgeCrossing(AreaEdge edge, int y, List<AreaCrossing> crossings)
    {
        var first = edge.First;
        var second = edge.Second;
        if (first.Y > second.Y)
            (first, second) = (second, first);

        var scanline = (2L * y) + 1;
        var lower = 2L * first.Y;
        var upper = 2L * second.Y;
        if (scanline < lower || scanline >= upper)
            return;

        var deltaY = second.Y - first.Y;
        var deltaX = second.X - first.X;
        var denominator = 2L * deltaY;
        var numerator = (2L * first.X * deltaY) + (deltaX * (scanline - lower));
        crossings.Add(new AreaCrossing(numerator, denominator));
    }

    private static int CompareCrossings(AreaCrossing first, AreaCrossing second)
    {
        var left = first.Numerator * second.Denominator;
        var right = second.Numerator * first.Denominator;
        return left.CompareTo(right);
    }

    private static long CeilDivide(long numerator, long denominator)
    {
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));

        return numerator >= 0
            ? (numerator + denominator - 1) / denominator
            : numerator / denominator;
    }

    private static long IntegerSqrt(long value)
    {
        if (value <= 0)
            return 0;

        var low = 0L;
        var high = 1_500_000_000L;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (middle <= value / middle)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return high;
    }

    private static bool DrawAreaShapeOutline(
        IGraphicsMemory memory,
        uint rastPort,
        AreaShape shape,
        int outlinePen,
        int originalPen,
        AreaScratch? scratch = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        byte[]? setterOriginalMinterms = null;
        byte[]? setterNextMinterms = null;
        if (scratch is not null &&
            !scratch.Minterms.TryAcquire(
                out _,
                out setterOriginalMinterms,
                out setterNextMinterms))
        {
            return false;
        }

        try
        {
            return DrawAreaShapeOutlineCore(
                memory,
                rastPort,
                shape,
                outlinePen,
                originalPen,
                scratch,
                pixelVisible,
                setterOriginalMinterms,
                setterNextMinterms);
        }
        finally
        {
            if (scratch is not null)
                scratch.Minterms.Release();
        }
    }

    private static bool DrawAreaShapeOutlineCore(
        IGraphicsMemory memory,
        uint rastPort,
        AreaShape shape,
        int outlinePen,
        int originalPen,
        AreaScratch? scratch,
        Func<int, int, bool>? pixelVisible,
        byte[]? setterOriginalMinterms,
        byte[]? setterNextMinterms)
    {
        // Kickstart changes APen before each PolyDraw/DrawEllipse outline and
        // restores it immediately afterwards.  SetAPen also restarts the
        // line-pattern phase, which is why every recorded shape starts from
        // LinePtrn bit 15 and the caller's phase is left at the restart value.
        if (SetAPen(
                memory,
                rastPort,
                unchecked((byte)outlinePen),
                setterOriginalMinterms,
                setterNextMinterms) != Success ||
            !TryWriteRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                15))
            return false;

        var success = shape.IsEllipse
            ? DrawEllipse(
                  memory,
                  rastPort,
                  unchecked((short)shape.Ellipse.CenterX),
                  unchecked((short)shape.Ellipse.CenterY),
                  unchecked((short)shape.Ellipse.RadiusX),
                  unchecked((short)shape.Ellipse.RadiusY),
                  pixelVisible,
                  scratch?._outlineEllipsePoints,
                  scratch?._outlineSnapshotAddresses,
                  scratch?._outlineSnapshotValues) == Success
            : DrawAreaPolygonOutline(
                  memory,
                  rastPort,
                  shape.Polygon,
                  scratch,
                  pixelVisible);

        // Restore the pen even when the outline itself failed so the outer
        // AreaEnd rollback sees the same public RastPort state as before the
        // attempted operation.
        var restored = SetAPen(
                           memory,
                           rastPort,
                           unchecked((byte)originalPen),
                           setterOriginalMinterms,
                           setterNextMinterms) == Success &&
                       TryWriteRastPortByte(
                           memory,
                           rastPort,
                           GraphicsLayouts.RastPortLinePatternCount,
                           15);
        return success && restored;
    }

    private static bool DrawAreaPolygonOutline(
        IGraphicsMemory memory,
        uint rastPort,
        List<AreaPoint>? polygon,
        AreaScratch? scratch = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        if (polygon is null || polygon.Count < 2)
            return true;

        // AreaEnd positions the pen at the first vertex before this outline
        // sequence.  Draw each stored endpoint, including the native closing
        // copy, so the outline follows the same connected-DRAW semantics as
        // Kickstart PolyDraw.
        if (Move(
                memory,
                rastPort,
                unchecked((short)polygon[0].X),
                unchecked((short)polygon[0].Y)) != Success)
            return false;

        var closed = polygon[^1].X == polygon[0].X &&
                     polygon[^1].Y == polygon[0].Y;
        for (var index = 1; index < polygon.Count; index++)
        {
            var duplicateClosingEndpoint = closed && index == polygon.Count - 1;
            var undoComplement = duplicateClosingEndpoint &&
                                 TryGetLinePatternSample(
                                     memory,
                                     rastPort,
                                     polygon[index - 1],
                                     polygon[index],
                                     out var patternSample) &&
                                 patternSample &&
                                 ((GetDrawMode(memory, rastPort) & 2) != 0);
            if (!DrawAreaOutlineSegment(
                    memory,
                    rastPort,
                    polygon[index],
                    undoComplement,
                    scratch,
                    pixelVisible))
            {
                return false;
            }
        }

        if (polygon[^1].X != polygon[0].X || polygon[^1].Y != polygon[0].Y)
        {
            var undoComplement =
                TryGetLinePatternSample(
                    memory,
                    rastPort,
                    polygon[^1],
                    polygon[0],
                out var patternSample) &&
                patternSample &&
                ((GetDrawMode(memory, rastPort) & 2) != 0);
            if (!DrawAreaOutlineSegment(
                    memory,
                    rastPort,
                    polygon[0],
                    undoComplement,
                    scratch,
                    pixelVisible))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DrawAreaOutlineSegment(
        IGraphicsMemory memory,
        uint rastPort,
        AreaPoint endpoint,
        bool undoComplement,
        AreaScratch? scratch,
        Func<int, int, bool>? pixelVisible)
    {
        EndpointVisibilityScratch? acquiredVisibility = null;
        var segmentVisibility = pixelVisible;
        if (pixelVisible is not null && undoComplement)
        {
            acquiredVisibility = scratch?.EndpointVisibility ??
                new EndpointVisibilityScratch();
            if (!acquiredVisibility.TryAcquire(
                    pixelVisible,
                    endpoint.X,
                    endpoint.Y,
                    out segmentVisibility))
            {
                return false;
            }
        }

        try
        {
            if (Draw(
                    memory,
                    rastPort,
                    unchecked((short)endpoint.X),
                    unchecked((short)endpoint.Y),
                    segmentVisibility,
                    scratch?._outlineSnapshotAddresses,
                    scratch?._outlineSnapshotValues) != Success)
            {
                return false;
            }

            if (!undoComplement ||
                (acquiredVisibility is not null &&
                 (!acquiredVisibility.Resolved || !acquiredVisibility.Visible)))
            {
                return true;
            }

            return TryWritePixel(
                memory,
                rastPort,
                unchecked((short)endpoint.X),
                unchecked((short)endpoint.Y),
                pixelVisible,
                visibilityChecked: acquiredVisibility?.Resolved == true,
                out _);
        }
        finally
        {
            acquiredVisibility?.Release();
        }
    }

    private static bool TryGetLinePatternSample(
        IGraphicsMemory memory,
        uint rastPort,
        AreaPoint first,
        AreaPoint second,
        out bool patternSample)
    {
        patternSample = false;
        if (!TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePattern,
                out var linePattern) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortLinePatternCount,
                out var patternCount) ||
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortDrawMode,
                out var drawMode))
        {
            return false;
        }

        var majorDistance = Math.Max(
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));
        patternSample = (linePattern &
                         (1 << ((patternCount - majorDistance) & 0x0F))) != 0;
        if ((drawMode & 4) != 0)
            patternSample = !patternSample;
        return true;
    }

    private static bool ResetAreaState(
        IGraphicsMemory memory,
        AreaState state,
        byte[]? originalBuffer = null)
    {
        if (!TrySnapshotAreaStateUpdate(
                memory,
                state,
                out var original,
                originalBuffer))
            return false;

        if (memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
                state.VectorTable) &&
            memory.TryWriteLong(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer,
                state.FlagTable) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                0) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFirstX,
                0) &&
            memory.TryWriteWord(
                state.AreaInfo + (uint)GraphicsLayouts.AreaInfoFirstY,
                0))
        {
            return true;
        }

        RestoreAreaStateUpdate(memory, state, original);
        return false;
    }

    private static bool TryReadAreaState(
        IGraphicsMemory memory,
        uint rastPort,
        out AreaState state)
    {
        state = default;
        // AreaInfo vectors are reached through RastPort's word/long fields.
        // A permissive byte-addressable host must not claim an odd RastPort
        // base that would take an address error on a native 68000; keep that
        // malformed structure on the native/provider boundary before reading
        // the AreaInfo link.
        if (rastPort == 0 || (rastPort & 1u) != 0 ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortAreaInfo,
                sizeof(uint),
                out var areaInfoAddress) ||
            !memory.TryReadLong(areaInfoAddress, out var areaInfo) ||
            (areaInfo & 1u) != 0 ||
            !TryProbeRange(memory, areaInfo, GraphicsLayouts.AreaInfoSize))
        {
            return false;
        }

        if (!memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoVectorTable,
                out var vectorTable) ||
            !memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
                out var vectorPointer) ||
            !memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFlagTable,
                out var flagTable) ||
            !memory.TryReadLong(
                areaInfo + (uint)GraphicsLayouts.AreaInfoFlagPointer,
                out var flagPointer) ||
            !memory.TryReadWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoCount,
                out var count) ||
            !memory.TryReadWord(
                areaInfo + (uint)GraphicsLayouts.AreaInfoMaxCount,
                out var maxCount) ||
            !TryReadSignedWord(
                memory,
                areaInfo + (uint)GraphicsLayouts.AreaInfoFirstX,
                out var firstX) ||
            !TryReadSignedWord(
                memory,
                areaInfo + (uint)GraphicsLayouts.AreaInfoFirstY,
                out var firstY) ||
            count > maxCount)
        {
            return false;
        }

        // InitArea(NULL, 0) publishes the native empty-collector form: all
        // four storage pointers are NULL and both counters are zero. Native
        // AreaEnd only inspects Count before taking the no-op path, so retain
        // that exact descriptor as a valid empty AreaInfo instead of forcing
        // callers to provide a dummy five-byte buffer. Any non-empty or
        // partially-null collector remains strict and available to the
        // native/provider owner.
        var emptyZeroCapacityCollector =
            count == 0 &&
            maxCount == 0 &&
            vectorTable == 0 &&
            vectorPointer == 0 &&
            flagTable == 0 &&
            flagPointer == 0;
        if (!emptyZeroCapacityCollector && (vectorTable == 0 || flagTable == 0))
            return false;

        if (emptyZeroCapacityCollector)
        {
            state = new AreaState(
                areaInfo,
                0,
                0,
                0,
                0,
                count,
                maxCount,
                firstX,
                firstY,
                0);
            return true;
        }

        // The coordinate stream is an array of X/Y words.  A forged odd
        // vector table would be accepted by a byte-addressable host memory
        // implementation but would fault when the native collector reads a
        // coordinate pair.
        if ((vectorTable & 1u) != 0)
            return false;

        // A zero-capacity collector is a valid empty AreaInfo envelope.  It
        // cannot accept AreaMove/AreaDraw/AreaEllipse, but InitArea still
        // publishes a coherent collector that AreaEnd may close as a no-op.
        // Keep the pointer/flag-table relationship checks below active for
        // this case; accepting zero must not turn forged pointers into a
        // portable success boundary.
        if ((ulong)vectorTable + (ulong)maxCount * 4u >= (1UL << 32) ||
            (ulong)flagTable + maxCount >= (1UL << 32) ||
            (ulong)vectorTable + (ulong)maxCount * 4u != flagTable)
        {
            return false;
        }

        var expectedVectorPointer = (ulong)vectorTable + (ulong)count * 4u;
        var expectedFlagPointer = (ulong)flagTable + count;
        if (expectedVectorPointer > uint.MaxValue ||
            expectedFlagPointer > uint.MaxValue ||
            vectorPointer != (uint)expectedVectorPointer ||
            flagPointer != (uint)expectedFlagPointer)
        {
            return false;
        }

        var lastFlag = (byte)0;
        if (count != 0 && !memory.TryReadByte(flagPointer - 1u, out lastFlag))
            return false;

        state = new AreaState(
            areaInfo,
            vectorTable,
            flagTable,
            vectorPointer,
            flagPointer,
            count,
            maxCount,
            firstX,
            firstY,
            lastFlag);
        return true;
    }

    private static bool TryReadViewportRasInfo(
        IGraphicsMemory memory,
        uint viewPort,
        out uint rasInfo)
    {
        rasInfo = 0;
        // A NULL ViewPort is an absent display object, never a structure at
        // guest address zero.  Keep this explicit even when a host memory
        // adapter maps page zero with plausible fields: ChangeVPBitMap must
        // not publish through a null viewport envelope.
        if (viewPort == 0 ||
            (viewPort & 1u) != 0 ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortRasInfo,
                sizeof(uint),
                out var rasInfoAddress) ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortExtendedModes,
                sizeof(byte),
                out var extendedModesAddress) ||
            !memory.TryReadByte(extendedModesAddress, out var extendedModes) ||
            extendedModes != 0 ||
            !memory.TryReadLong(rasInfoAddress, out rasInfo) ||
            rasInfo == 0 ||
            (rasInfo & 1u) != 0 ||
            !TryGetAddress(rasInfo, GraphicsLayouts.RasInfoSize, out _))
        {
            return false;
        }

        // Probe the first chain node before a pointer update so a malformed
        // guest chain cannot be partially modified.  The complete chain is
        // validated by TryValidateViewPortBitMapChange after this focused
        // link read; unrelated ViewPort geometry is intentionally outside
        // ChangeVPBitMap's documented bitmap-exchange field set.
        return memory.TryReadLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, out _) &&
            memory.TryReadWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, out _) &&
            memory.TryReadWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, out _);
    }

    /// <summary>
    /// Validates the guest state that ScrollVPort may reinterpret.  Every
    /// RasInfo node is probed before the display boundary is notified, and a
    /// bounded walk prevents malformed cyclic chains from hanging the guest.
    /// </summary>
    internal static bool ValidateViewPortForScroll(
        IGraphicsMemory memory,
        uint viewPort)
    {
        // A NULL ViewPort is the absent display-object sentinel.  Keep it
        // distinct from a host mapping that happens to expose a plausible
        // page-zero envelope; scroll/copper callers require a real object.
        if (viewPort == 0 ||
            (viewPort & 1u) != 0 ||
            !TryGetAddress(viewPort, GraphicsLayouts.ViewPortSize, out _) ||
            !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, out _) ||
            !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, out _) ||
            !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDxOffset, out _) ||
            !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDyOffset, out _) ||
            !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortModes, out _) ||
            !memory.TryReadByte(viewPort + (uint)GraphicsLayouts.ViewPortExtendedModes, out var extendedModes) ||
            extendedModes != 0 ||
            !memory.TryReadLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, out var rasInfo) ||
            rasInfo == 0)
        {
            return false;
        }

        return ValidateRasInfoChain(memory, rasInfo, validateBitMap: false);
    }

    /// <summary>
    /// Validates only the guest fields needed before AllocDBufInfo asks the
    /// display owner about double-buffer capability.  ViewPort geometry is
    /// consumed by display rebuild/scroll paths, not by DBufInfo allocation.
    /// </summary>
    internal static bool ValidateViewPortForDoubleBuffer(
        IGraphicsMemory memory,
        uint viewPort)
    {
        if (!TryReadViewportRasInfo(memory, viewPort, out var rasInfo))
            return false;

        return ValidateRasInfoChain(memory, rasInfo, validateBitMap: false);
    }

    /// <summary>
    /// Validates only the public links that ScrollVPort reinterprets.  The
    /// resident vector consumes the viewport's RasInfo chain and its
    /// offsets; viewport display geometry belongs to MakeVPort/coercion and
    /// is not part of this offset-refresh admission.
    /// </summary>
    internal static bool ValidateViewPortForScrollOperation(
        IGraphicsMemory memory,
        uint viewPort)
    {
        if (!TryReadViewportRasInfo(memory, viewPort, out var rasInfo))
            return false;

        return ValidateRasInfoChain(memory, rasInfo, validateBitMap: false);
    }

    /// <summary>
    /// Validates the guest fields consumed by WaitBOVP.  A viewport can be
    /// assembled before Intuition links its RasInfo chain, so a null chain is
    /// legal for this timing-only operation.  Once a chain is present it is
    /// still caller-owned structure and must be a bounded, readable chain;
    /// otherwise a malformed display description must remain available to a
    /// native or provider implementation.
    /// </summary>
    internal static bool ValidateViewPortForWaitBovp(
        IGraphicsMemory memory,
        uint viewPort)
    {
        // WaitBOVP is a timing-only operation.  Its portable scheduler
        // boundary consumes the viewport's vertical extent/offset and the
        // standard-mode guard; probing the complete structure would claim
        // unrelated display/copper fields that the resident wait does not
        // inspect while a viewport is still being assembled.
        // WaitBOVP consumes a concrete ViewPort's geometry.  Address zero is
        // not a legal structure even when the host memory adapter maps page
        // zero; accepting it would claim a native/provider wait on a null
        // display object.
        if (viewPort == 0 ||
            (viewPort & 1u) != 0 ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortDHeight,
                sizeof(ushort),
                out var dHeightAddress) ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortDyOffset,
                sizeof(ushort),
                out var dyOffsetAddress) ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortExtendedModes,
                sizeof(byte),
                out var extendedModesAddress) ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortRasInfo,
                sizeof(uint),
                out var rasInfoAddress) ||
            !memory.TryReadWord(dHeightAddress, out _) ||
            !memory.TryReadWord(dyOffsetAddress, out _) ||
            !memory.TryReadByte(extendedModesAddress, out var extendedModes) ||
            extendedModes != 0 ||
            !memory.TryReadLong(rasInfoAddress, out var rasInfo))
        {
            return false;
        }

        return rasInfo == 0 || ValidateRasInfoChain(memory, rasInfo, validateBitMap: false);
    }

    /// <summary>
    /// Validates only the public fields consumed by CalcIVG.  The resident
    /// routine counts the viewport's display copper list through
    /// <c>vp_DspIns</c>; an unmade viewport may legitimately have a NULL
    /// display list while its RasInfo and display geometry are still being
    /// assembled.  Do not reuse the scroll/rebuild validator here: requiring
    /// a RasInfo chain would claim a query whose native implementation does
    /// not inspect that unrelated state.
    /// </summary>
    internal static bool ValidateViewForCalcIvg(
        IGraphicsMemory memory,
        uint view,
        uint viewPort)
    {
        if (view == 0 || viewPort == 0 ||
            (view & 1u) != 0 || (viewPort & 1u) != 0 ||
            !TryAddress(
                view,
                GraphicsLayouts.ViewViewPort,
                sizeof(uint),
                out var viewPortLink) ||
            !memory.TryReadLong(viewPortLink, out _) ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortDspIns,
                sizeof(uint),
                out var displayInstructions) ||
            !memory.TryReadLong(displayInstructions, out var displayList) ||
            (displayList & 1u) != 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the guest fields needed by GetVPModeID.  Mode identity is
    /// derivable from <c>vp_Modes</c> (or a valid ColorMap association) while
    /// a viewport is still being assembled, so a null <c>vp_RasInfo</c> is a
    /// legal query state.  If a RasInfo chain is already present, however,
    /// it remains a caller-owned envelope and must be structurally valid;
    /// this keeps a truncated or cyclic chain available to the native or
    /// monitor provider instead of claiming a partial viewport.
    /// </summary>
    internal static bool ValidateViewPortForModeQuery(
        IGraphicsMemory memory,
        uint viewPort)
    {
        // Keep the public NULL sentinel separate from an address-zero
        // envelope.  GetVPModeID already performs a narrow ColorMap read,
        // but this helper is also used by direct/native admission paths.
        if (viewPort == 0 ||
            (viewPort & 1u) != 0 ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortModes,
                sizeof(ushort),
                out var modesAddress) ||
            !memory.TryReadWord(modesAddress, out _) ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortExtendedModes,
                sizeof(byte),
                out var extendedModesAddress) ||
            !memory.TryReadByte(extendedModesAddress, out var extendedModes) ||
            extendedModes != 0 ||
            !TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortRasInfo,
                sizeof(uint),
                out var rasInfoAddress) ||
            !memory.TryReadLong(rasInfoAddress, out var rasInfo))
        {
            return false;
        }

        return rasInfo == 0 || ValidateRasInfoChain(memory, rasInfo, validateBitMap: false);
    }

    /// <summary>
    /// Reads the first field consumed by the resident GetVPModeID path.
    /// When a ColorMap is attached, that vector does not inspect the rest of
    /// the ViewPort before resolving the map's mode identity.  Keep this
    /// narrow probe separate from the assembling/no-ColorMap validator so an
    /// owned map can remain claimable when unrelated geometry is sparse.
    /// </summary>
    internal static bool TryReadViewPortColorMap(
        IGraphicsMemory memory,
        uint viewPort,
        out uint colorMap)
    {
        colorMap = 0;
        return viewPort != 0 &&
            (viewPort & 1u) == 0 &&
            TryAddress(
                viewPort,
                GraphicsLayouts.ViewPortColorMap,
                sizeof(uint),
                out var colorMapAddress) &&
            memory.TryReadLong(colorMapAddress, out colorMap);
    }

    /// <summary>
    /// Validates the extra RasInfo contract consumed by MakeVPort.  A
    /// dual-playfield viewport is represented by two RasInfo nodes: the
    /// first node describes playfield A and its <c>Next</c> link must name
    /// playfield B.  ScrollVPort intentionally accepts an assembling chain,
    /// but MakeVPort must reject a missing second playfield before allocating
    /// temporary display state or calling a copper backend.
    /// </summary>
    internal static bool ValidateViewPortForMake(
        IGraphicsMemory memory,
        uint viewPort)
        => ValidateViewPortForMake(memory, 0, viewPort);

    /// <summary>
    /// Validates the MakeVPort topology with an optional root View envelope.
    /// The ordinary two-argument form is retained for MrgCop's per-viewport
    /// admission.  When a root View is supplied, the public ViewPort and every
    /// RasInfo/BitMap link are required to remain disjoint from that View and
    /// from the ViewPort itself before a copper provider can publish links.
    /// This is an alias guard only; provider-owned bitmap geometry remains
    /// outside the MakeVPort claim.
    /// </summary>
    internal static bool ValidateViewPortForMake(
        IGraphicsMemory memory,
        uint view,
        uint viewPort)
    {
        if (!ValidateViewPortForScroll(memory, viewPort) ||
            !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortModes, out var modes) ||
            !memory.TryReadLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, out var rasInfo))
        {
            return false;
        }

        if (view != 0 &&
            ((view & 1u) != 0 ||
             !TryProbeRange(memory, view, GraphicsLayouts.ViewSize) ||
             SpansOverlap(
                 view,
                 (uint)GraphicsLayouts.ViewSize,
                 viewPort,
                 (uint)GraphicsLayouts.ViewPortSize) ||
             !ValidateRasInfoChain(
                 memory,
                 rasInfo,
                 validateBitMap: false,
                 excludedView: view,
                 excludedViewPort: viewPort)))
        {
            return false;
        }

        const ushort DualPlayfield = 0x0400;
        if (!memory.TryReadLong(
                rasInfo + (uint)GraphicsLayouts.RasInfoNext,
                out var secondRasInfo))
        {
            return false;
        }

        if ((modes & DualPlayfield) == 0)
        {
            // A single-playfield viewport owns one RasInfo only.  A tail is
            // not an alternate single-playfield representation; it is an
            // ambiguous/provider-owned chain and must not reach MakeVPort.
            return secondRasInfo == 0;
        }

        // DUALPF requires one and only one companion RasInfo.  The second
        // node's Next field is documented to terminate the pair; accepting a
        // third node would make copper plane ownership ambiguous.
        return secondRasInfo != 0 &&
            (secondRasInfo & 1u) == 0 &&
            TryProbeRange(memory, secondRasInfo, GraphicsLayouts.RasInfoSize) &&
            memory.TryReadLong(
                secondRasInfo + (uint)GraphicsLayouts.RasInfoNext,
                out var secondNext) &&
            secondNext == 0 &&
            memory.TryReadLong(secondRasInfo + (uint)GraphicsLayouts.RasInfoBitMap, out _) &&
            memory.TryReadWord(secondRasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, out _) &&
            memory.TryReadWord(secondRasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, out _);
    }

    /// <summary>
    /// Validates the complete ViewPort chain consumed by MrgCop.  The generic
    /// View validator deliberately accepts assembling RasInfo chains for
    /// LoadView and mode queries; a copper merge, however, must see the same
    /// one-node/single-playfield or two-node/DUALPF contract as MakeVPort
    /// before a backend can publish a merged stream.  Hidden ViewPorts are
    /// different: resident MrgCop reads their public mode/next links and then
    /// skips them, so their RasInfo/display state remains available to the
    /// native/provider owner rather than being consumed by this validator.
    /// </summary>
    internal static bool ValidateViewForCopperMerge(
        IGraphicsMemory memory,
        uint view)
        => ValidateViewForCopperMerge(memory, view, out _);

    internal static bool ValidateViewForCopperMerge(
        IGraphicsMemory memory,
        uint view,
        out bool hasVisibleViewPort)
    {
        hasVisibleViewPort = false;
        if ((view & 1u) != 0 ||
            !TryProbeRange(memory, view, GraphicsLayouts.ViewSize) ||
            !memory.TryReadLong(
                view + (uint)GraphicsLayouts.ViewViewPort,
                out var viewPort))
        {
            return false;
        }

        for (var node = 0; node < 64 && viewPort != 0; node++)
        {
            var currentViewPort = viewPort;
            if ((currentViewPort & 1u) != 0 ||
                !TryAddress(
                    currentViewPort,
                    GraphicsLayouts.ViewPortModes,
                    sizeof(ushort),
                    out var modesAddress) ||
                !TryAddress(
                    currentViewPort,
                    GraphicsLayouts.ViewPortNext,
                    sizeof(uint),
                    out var nextAddress) ||
                !memory.TryReadWord(
                    modesAddress,
                    out var modes) ||
                !memory.TryReadLong(
                    nextAddress,
                    out viewPort))
            {
                return false;
            }

            // VP_HIDE is a display-time filter.  The resident MrgCop loop
            // does not dereference RasInfo, ViewPortExtra, or display data for
            // hidden nodes, so malformed hidden state must not make the
            // portable path steal the call from native/provider ownership.
            if ((modes & GraphicsModeIds.ViewPortHidden) != 0)
                continue;

            hasVisibleViewPort = true;
            if (!ValidateViewPortForMake(memory, view, currentViewPort))
            {
                return false;
            }
        }

        return viewPort == 0;
    }

    private static bool ValidateRasInfoChain(
        IGraphicsMemory memory,
        uint rasInfo,
        bool validateBitMap,
        uint excludedView = 0,
        uint excludedViewPort = 0)
    {
        // A RasInfo chain is a linked list with no count field.  The bounded
        // walk is deterministic and rejects both self-cycles and longer
        // cycles without allocating a host collection.
        for (var index = 0; index < 64 && rasInfo != 0; index++)
        {
            if ((rasInfo & 1u) != 0 ||
                !TryProbeRange(memory, rasInfo, GraphicsLayouts.RasInfoSize) ||
                (excludedView != 0 &&
                 (SpansOverlap(
                      rasInfo,
                      (uint)GraphicsLayouts.RasInfoSize,
                      excludedView,
                      (uint)GraphicsLayouts.ViewSize) ||
                  (excludedViewPort != 0 &&
                   SpansOverlap(
                       rasInfo,
                       (uint)GraphicsLayouts.RasInfoSize,
                       excludedViewPort,
                       (uint)GraphicsLayouts.ViewPortSize)))) ||
                !memory.TryReadLong(rasInfo + (uint)GraphicsLayouts.RasInfoNext, out var next) ||
                !memory.TryReadLong(rasInfo + (uint)GraphicsLayouts.RasInfoBitMap, out var bitMap) ||
                !memory.TryReadWord(rasInfo + (uint)GraphicsLayouts.RasInfoRxOffset, out _) ||
                !memory.TryReadWord(rasInfo + (uint)GraphicsLayouts.RasInfoRyOffset, out _))
            {
                return false;
            }

            // LoadView publishes a complete display chain, so a present
            // bitmap link must at least describe a valid standard-planar
            // envelope.  ScrollVPort deliberately calls this walk with
            // validation disabled: its display/provider boundary owns the
            // bitmap association and may still be assembling that surface.
            if (bitMap != 0 &&
                ((excludedView != 0 &&
                  SpansOverlap(
                      bitMap,
                      (uint)GraphicsLayouts.BitMapSize,
                      excludedView,
                      (uint)GraphicsLayouts.ViewSize)) ||
                 (excludedViewPort != 0 &&
                  SpansOverlap(
                      bitMap,
                      (uint)GraphicsLayouts.BitMapSize,
                      excludedViewPort,
                      (uint)GraphicsLayouts.ViewPortSize)) ||
                 (validateBitMap && !TryReadDisplayBitmapLayout(memory, bitMap, out _))))
                return false;

            if (next == rasInfo)
                return false;

            rasInfo = next;
        }

        return rasInfo == 0;
    }

    private static bool ValidateViewPortChain(
        IGraphicsMemory memory,
        uint viewPort,
        bool validateBitMap)
    {
        // ViewPort.Next is an ordinary guest linked list.  Keep a bounded
        // walk so malformed/cyclic guest data cannot hang a native caller or
        // the host projection boundary.
        for (var node = 0; node < 64 && viewPort != 0; node++)
        {
            if ((viewPort & 1u) != 0 ||
                !TryProbeRange(memory, viewPort, GraphicsLayouts.ViewPortSize) ||
                !memory.TryReadLong(viewPort + (uint)GraphicsLayouts.ViewPortDspIns, out _) ||
                !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDWidth, out _) ||
                !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDHeight, out _) ||
                !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDxOffset, out _) ||
                !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortDyOffset, out _) ||
                !memory.TryReadWord(viewPort + (uint)GraphicsLayouts.ViewPortModes, out _) ||
                !memory.TryReadByte(viewPort + (uint)GraphicsLayouts.ViewPortExtendedModes, out var extendedModes) ||
                extendedModes != 0 ||
                !memory.TryReadLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo, out var rasInfo) ||
                (rasInfo != 0 && !ValidateRasInfoChain(memory, rasInfo, validateBitMap)) ||
                !memory.TryReadLong(viewPort + (uint)GraphicsLayouts.ViewPortNext, out var next))
            {
                return false;
            }

            // A direct back-edge to the current chain head is the common
            // malformed cycle and can be rejected without managed state.
            if (next == viewPort)
                return false;

            viewPort = next;
        }

        return viewPort == 0;
    }

    private static bool TryReadBitmapLayout(
        IGraphicsMemory memory,
        uint bitMap,
        out BitmapLayout layout)
        => TryReadBitmapLayout(memory, bitMap, requireDisplayPlanes: false, out layout);

    private static bool TryReadDisplayBitmapLayout(
        IGraphicsMemory memory,
        uint bitMap,
        out BitmapLayout layout)
        => TryReadBitmapLayout(memory, bitMap, requireDisplayPlanes: true, out layout);

    private static bool TryReadBitmapLayout(
        IGraphicsMemory memory,
        uint bitMap,
        bool requireDisplayPlanes,
        out BitmapLayout layout)
    {
        layout = default;
        // A NULL BitMap is an absent association, never a geometry header.
        // Keep this explicit even when a test or host memory adapter maps
        // address zero: native graphics vectors treat A0 == 0 as the null
        // sentinel, and accepting a plausible header there would let
        // ChangeVPBitMap or AllocBitMap(friend) publish a null surface rather
        // than leaving the request available to the native/provider owner.
        if (bitMap == 0 ||
            (bitMap & 1u) != 0 ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapBytesPerRow,
                sizeof(ushort),
                out var bytesPerRowAddress) ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapRows,
                sizeof(ushort),
                out var rowsAddress) ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapFlags,
                sizeof(byte),
                out var flagsAddress) ||
            !TryAddress(
                bitMap,
                GraphicsLayouts.BitMapDepth,
                sizeof(byte),
                out var depthAddress) ||
            !memory.TryReadWord(bytesPerRowAddress, out var bytesPerRow) ||
            !memory.TryReadWord(rowsAddress, out var rows) ||
            !memory.TryReadByte(flagsAddress, out var flags) ||
            !memory.TryReadByte(depthAddress, out var depth) ||
            bytesPerRow == 0 || (bytesPerRow & 1) != 0 || depth == 0 || depth > 8 ||
            (requireDisplayPlanes && rows == 0))
        {
            return false;
        }

        var structureBytes = GetBitmapStructureBytes(depth, flags);
        if (structureBytes > int.MaxValue ||
            !TryGetAddress(bitMap, (int)structureBytes, out _) ||
            !TryProbeRange(memory, bitMap, (int)structureBytes))
        {
            // InitBitMap-compatible callers may expose a compact declared-
            // depth header while retaining the public BMF_STANDARD marker.
            // Preserve the full-envelope fast path for ordinary structures,
            // but accept the depth-sized form when the unused tail is not
            // mapped; BMF_MINPLANES already takes this path directly.
            var compactBytes = (uint)GraphicsLayouts.BitMapPlanes +
                ((uint)depth * sizeof(uint));
            if (compactBytes > int.MaxValue ||
                !TryGetAddress(bitMap, (int)compactBytes, out _) ||
                !TryProbeRange(memory, bitMap, (int)compactBytes))
            {
                return false;
            }
        }

        if (!TryGetPlaneBytesPerRow(
                bytesPerRow,
                depth,
                flags,
                out var planeBytesPerRow))
        {
            return false;
        }

        // A linked LoadView bitmap is a displayable standard-planar surface,
        // not merely a geometry header.  Require every declared plane link
        // before publication when the caller is validating a display chain.
        // ChangeVPBitMap retains its existing geometry-only association
        // contract, and ScrollVPort is provider-owned altogether.
        if (!requireDisplayPlanes)
        {
            layout = new BitmapLayout(
                bytesPerRow,
                planeBytesPerRow,
                rows,
                depth,
                flags);
            return true;
        }

        uint firstPlaneAddress = 0;
        for (var plane = 0; plane < depth; plane++)
        {
            var planePointer = (ulong)bitMap +
                (uint)GraphicsLayouts.BitMapPlanes +
                (uint)(plane * 4);
            if (planePointer > uint.MaxValue ||
                !memory.TryReadLong((uint)planePointer, out var planeAddress) ||
                planeAddress == 0 ||
                (planeAddress & 1) != 0)
            {
                return false;
            }

            if (plane == 0)
            {
                firstPlaneAddress = planeAddress;
            }
            else if (IsInterleaved(flags))
            {
                var expected = (ulong)firstPlaneAddress +
                    ((ulong)plane * planeBytesPerRow);
                if (expected > uint.MaxValue || planeAddress != (uint)expected)
                    return false;
            }

            // LoadView publishes a display surface to the scheduler.  A
            // non-null plane pointer is not enough: the complete declared
            // row envelope must be readable before the host/native boundary
            // can expose the View.  Keep this check on the strict display
            // path; construction helpers intentionally accept geometry-only
            // bitmap headers while a screen is still being assembled.
            var planeSpan = GetPlaneTouchedSpan(
                bytesPerRow,
                planeBytesPerRow,
                rows,
                flags);
            if (planeSpan == 0 || planeSpan > int.MaxValue ||
                (IsInterleaved(flags)
                    ? !TryProbeContiguousRows(
                        memory,
                        planeAddress,
                        bytesPerRow,
                        rows,
                        planeBytesPerRow)
                    : !TryProbeContiguous(memory, planeAddress, planeBytesPerRow, rows)))
            {
                return false;
            }

            if (memory is IGraphicsDisplayMemory displayMemory)
            {
                if (planeSpan > uint.MaxValue ||
                    !displayMemory.IsDisplayDmaRange(planeAddress, (uint)planeSpan))
                {
                    return false;
                }
            }
        }

        layout = new BitmapLayout(
            bytesPerRow,
            planeBytesPerRow,
            rows,
            depth,
            flags);
        return true;
    }

    private static bool TryGetAddress(uint address, int byteCount, out uint endAddress)
    {
        endAddress = 0;
        if (address == 0 || byteCount <= 0 || address > uint.MaxValue - (uint)(byteCount - 1))
            return false;

        endAddress = address + (uint)(byteCount - 1);
        return true;
    }

    private static bool TryGetGuestSpan(uint address, uint byteCount)
        => address != 0 &&
           byteCount != 0 &&
           address <= uint.MaxValue - (byteCount - 1);

    private static bool TryProbeRange(IGraphicsMemory memory, uint address, int byteCount)
    {
        if (!TryGetAddress(address, byteCount, out _))
            return false;

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }

        return true;
    }

    private static bool WriteAreaVector(
        IGraphicsMemory memory,
        uint vectorPointer,
        uint flagPointer,
        short x,
        short y,
        byte flag,
        byte[]? originalBuffer = null)
    {
        if (!TrySnapshotAreaVector(
                memory,
                vectorPointer,
                flagPointer,
                out var originalVector,
                out var originalFlag,
                originalBuffer))
        {
            return false;
        }

        if (memory.TryWriteWord(vectorPointer, unchecked((ushort)x)) &&
            memory.TryWriteWord(vectorPointer + 2u, unchecked((ushort)y)) &&
            memory.TryWriteByte(flagPointer, flag))
        {
            return true;
        }

        RestoreAreaVector(
            memory,
            vectorPointer,
            flagPointer,
            originalVector,
            originalFlag);
        return false;
    }

    private static bool TrySnapshotAreaStateUpdate(
        IGraphicsMemory memory,
        AreaState state,
        out byte[] original,
        byte[]? buffer = null)
        => TrySnapshot(
            memory,
            state.AreaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
            GraphicsLayouts.AreaInfoFirstY + 2 - GraphicsLayouts.AreaInfoVectorPointer,
            out original,
            buffer);

    private static void RestoreAreaStateUpdate(
        IGraphicsMemory memory,
        AreaState state,
        byte[] original)
        => Restore(
            memory,
            state.AreaInfo + (uint)GraphicsLayouts.AreaInfoVectorPointer,
            original);

    private static bool TrySnapshotAreaVector(
        IGraphicsMemory memory,
        uint vectorPointer,
        uint flagPointer,
        out byte[] vectorOriginal,
        out byte flagOriginal,
        byte[]? buffer = null)
    {
        vectorOriginal = Array.Empty<byte>();
        flagOriginal = 0;
        if (!TrySnapshot(
                memory,
                vectorPointer,
                4,
                out vectorOriginal,
                buffer) ||
            !memory.TryReadByte(flagPointer, out flagOriginal))
        {
            vectorOriginal = Array.Empty<byte>();
            flagOriginal = 0;
            return false;
        }

        return true;
    }

    private static void RestoreAreaVector(
        IGraphicsMemory memory,
        uint vectorPointer,
        uint flagPointer,
        byte[] vectorOriginal,
        byte flagOriginal)
    {
        Restore(memory, vectorPointer, vectorOriginal);
        _ = memory.TryWriteByte(flagPointer, flagOriginal);
    }

    private static bool TryProbeAreaVector(
        IGraphicsMemory memory,
        uint vectorPointer,
        uint flagPointer)
        => TryProbeRange(memory, vectorPointer, 4) &&
           TryProbeRange(memory, flagPointer, 1);

    private static bool TryProbeAreaStateUpdate(IGraphicsMemory memory, AreaState state)
        => TryAddress(
               state.AreaInfo,
               GraphicsLayouts.AreaInfoVectorPointer,
               GraphicsLayouts.AreaInfoFirstY + 2 - GraphicsLayouts.AreaInfoVectorPointer,
               out var address) &&
           TryProbeRange(
               memory,
               address,
               GraphicsLayouts.AreaInfoFirstY + 2 - GraphicsLayouts.AreaInfoVectorPointer);

    private static bool TryReadPoint(
        IGraphicsMemory memory,
        uint baseAddress,
        uint offset,
        out short x,
        out short y)
    {
        x = 0;
        y = 0;
        // PolyDraw consumes each coordinate pair with native 68k word
        // accesses.  A NULL or odd point-table address is therefore not a
        // portable guest object even when a host memory adapter happens to
        // expose byte zero or unaligned bytes; leave those calls available
        // to the resident/native provider before probing the stream.
        if (baseAddress == 0 || (baseAddress & 1u) != 0 ||
            offset > uint.MaxValue - baseAddress)
            return false;

        var pointAddress = baseAddress + offset;
        if (pointAddress > uint.MaxValue - 2u ||
            !TryReadSignedWord(memory, pointAddress, out x) ||
            !TryReadSignedWord(memory, pointAddress + 2u, out y))
        {
            x = 0;
            y = 0;
            return false;
        }

        return true;
    }

    private static short ReadSignedWord(IGraphicsMemory memory, uint address)
        => TryReadSignedWord(memory, address, out var value) ? value : (short)0;

    internal static bool TryReadBitmap(IGraphicsMemory memory, uint rastPort, out BitmapInfo bitmap)
    {
        bitmap = default;
        if (rastPort == 0 || (rastPort & 1u) != 0 ||
            !TryAddress(
                rastPort,
                0,
                GraphicsLayouts.RastPortSize,
                out _) ||
            !TryAddress(
                rastPort,
                GraphicsLayouts.RastPortBitMap,
                sizeof(uint),
                out var bitMapPointerAddress) ||
            !memory.TryReadLong(bitMapPointerAddress, out var bitMapAddress) ||
            bitMapAddress == 0 || (bitMapAddress & 1u) != 0 ||
            !TryAddress(
                bitMapAddress,
                GraphicsLayouts.BitMapBytesPerRow,
                sizeof(ushort),
                out var bytesPerRowAddress) ||
            !TryAddress(
                bitMapAddress,
                GraphicsLayouts.BitMapRows,
                sizeof(ushort),
                out var rowsAddress) ||
            !TryAddress(
                bitMapAddress,
                GraphicsLayouts.BitMapFlags,
                sizeof(byte),
                out var flagsAddress) ||
            !TryAddress(
                bitMapAddress,
                GraphicsLayouts.BitMapDepth,
                sizeof(byte),
                out var depthAddress) ||
            !memory.TryReadWord(bytesPerRowAddress, out var bytesPerRow) ||
            !memory.TryReadWord(rowsAddress, out var rows) ||
            !memory.TryReadByte(flagsAddress, out var flags) ||
            !memory.TryReadByte(depthAddress, out var depth) ||
            bytesPerRow == 0 || rows == 0 || depth == 0 || depth > 8)
        {
            return false;
        }

        if (!TryGetPlaneBytesPerRow(
                bytesPerRow,
                depth,
                flags,
                out var planeBytesPerRow))
        {
            return false;
        }

        bitmap = new BitmapInfo(
            bitMapAddress,
            bytesPerRow,
            planeBytesPerRow,
            rows,
            depth,
            flags);
        return true;
    }

    internal static bool TryReadBitmapPixel(IGraphicsMemory memory, BitmapInfo bitmap, int x, int y, out int color)
    {
        color = 0;
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return false;

        var bitMask = (byte)(0x80 >> (x & 7));
        var anyPlane = false;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if (!TryGetPlaneByteAddress(memory, bitmap, plane, x, y, out var byteAddress) ||
                !memory.TryReadByte(byteAddress, out var value))
            {
                // A ReadPixel result combines every declared bitplane.  A
                // missing plane is malformed guest state, not an implicit
                // zero plane; fail closed instead of returning a truncated
                // colour value.
                return false;
            }

            anyPlane = true;
            if ((value & bitMask) != 0)
                color |= 1 << plane;
        }

        return anyPlane;
    }

    internal static bool TryProbeBitmapPixel(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int x,
        int y)
    {
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return false;

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if (!TryGetPlaneByteAddress(memory, bitmap, plane, x, y, out var byteAddress) ||
                !memory.TryReadByte(byteAddress, out _))
            {
                return false;
            }
        }

        return bitmap.Depth != 0;
    }

    internal static bool TryProbeBitmapWrite(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int x,
        int y,
        byte writeMask,
        List<uint>? snapshotAddresses = null,
        List<byte>? snapshotValues = null)
    {
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows || writeMask == 0)
            return true;

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) == 0)
                continue;

            if (!TryGetPlaneByteAddress(memory, bitmap, plane, x, y, out var byteAddress) ||
                !memory.TryReadByte(byteAddress, out var value))
            {
                return false;
            }

            snapshotAddresses?.Add(byteAddress);
            snapshotValues?.Add(value);
        }

        return true;
    }

    private static bool TryHasVisibleBitmapRegion(
        BitmapInfo bitmap,
        int left,
        int top,
        int right,
        int bottom,
        Func<int, int, bool> pixelVisible,
        out bool hasVisible)
    {
        hasVisible = false;
        if (left > right || top > bottom)
            return true;

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(bitmap.Width - 1, right);
        var clippedBottom = Math.Min(bitmap.Rows - 1, bottom);
        if (clippedLeft > clippedRight || clippedTop > clippedBottom)
            return true;

        var cells = (ulong)(uint)(clippedRight - clippedLeft + 1) *
            (uint)(clippedBottom - clippedTop + 1);
        if (cells > PortableWorkLimit || cells > int.MaxValue)
            return false;

        for (var y = clippedTop; y <= clippedBottom; y++)
        {
            for (var x = clippedLeft; x <= clippedRight; x++)
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

    internal static bool TryProbeBitmapRegion(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int left,
        int top,
        int right,
        int bottom,
        byte writeMask,
        Func<int, int, bool>? pixelVisible = null)
    {
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0 || left > right || top > bottom)
            return true;

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(bitmap.Width - 1, right);
        var clippedBottom = Math.Min(bitmap.Rows - 1, bottom);
        if (clippedLeft > clippedRight || clippedTop > clippedBottom)
            return true;

        var firstByte = clippedLeft >> 3;
        var lastByte = clippedRight >> 3;
        var bytesPerRow = (uint)(lastByte - firstByte + 1);
        var clippedRows = (ulong)(uint)(clippedBottom - clippedTop + 1);
        var selectedPlanes = 0u;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) != 0)
                selectedPlanes++;
        }

        if (selectedPlanes == 0)
            return true;

        // Both the ordinary planar preflight and the visibility-aware
        // provider path are byte/pixel walks.  Reject a region whose worst-
        // case work exceeds the portable host limit before either path can
        // spend billions of iterations on a valid but unstageable UWORD
        // bitmap envelope.
        var touchedPixels = (ulong)(uint)(clippedRight - clippedLeft + 1) *
            (uint)(clippedBottom - clippedTop + 1);
        if ((selectedPlanes != 0 &&
             (ulong)bytesPerRow * clippedRows * selectedPlanes > int.MaxValue) ||
            touchedPixels > int.MaxValue)
        {
            return false;
        }

        if (pixelVisible is not null)
        {
            for (var y = clippedTop; y <= clippedBottom; y++)
            {
                for (var x = clippedLeft; x <= clippedRight; x++)
                {
                    if (pixelVisible(x, y) &&
                        !TryProbeBitmapWrite(memory, bitmap, x, y, writeMask))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // A region operation touches complete planar bytes, not individual
        // pixels.  Probe each selected plane's byte span once per row.  The
        // former per-pixel walk repeated the same byte read up to eight times
        // and could turn a malformed but very wide guest bitmap into an
        // unbounded host loop before the first write.  Keep all arithmetic in
        // the guest-address width so a wrapped row is rejected before any
        // caller-visible mutation.
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) == 0)
                continue;

            var planePointerAddress = (ulong)bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes +
                (uint)(plane * 4);
            if (planePointerAddress > uint.MaxValue ||
                !memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var y = clippedTop; y <= clippedBottom; y++)
            {
                var rowOffset = (ulong)(uint)y * GetPlaneRowStride(
                        checked((ushort)bitmap.GuestBytesPerRow),
                        checked((ushort)bitmap.PlaneBytesPerRow),
                        bitmap.Flags) +
                    (uint)firstByte;
                var rowAddress = (ulong)planeAddress + rowOffset;
                if (rowAddress > uint.MaxValue ||
                    (ulong)(uint)(bytesPerRow - 1) > (ulong)uint.MaxValue - rowAddress)
                {
                    return false;
                }

                for (var offset = 0; offset < bytesPerRow; offset++)
                {
                    if (!memory.TryReadByte((uint)rowAddress + (uint)offset, out _))
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Restricts a RastPort write mask to the bitmap's declared planes. Bits
    /// above <c>bm_Depth</c> do not select storage; treating them as an
    /// effective zero mask keeps all planar primitives bounded and avoids
    /// staging a logical raster for a call that cannot publish a bit.
    /// </summary>
    internal static byte EffectiveWriteMask(BitmapInfo bitmap, byte writeMask)
    {
        if (bitmap.Depth <= 0 || writeMask == 0)
            return 0;

        if (bitmap.Depth >= 8)
            return writeMask;

        return (byte)(writeMask & ((1 << bitmap.Depth) - 1));
    }

    internal static bool SetBitmapPixel(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int x,
        int y,
        int color,
        int drawMode,
        byte writeMask)
    {
        if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
            return true;

        // Keep the low-level pixel writer on the same effective-mask
        // contract as the higher-level line/fill paths.  WritePixel reaches
        // this helper directly, so a mask containing only bits above the
        // declared bitmap depth must be an explicit bounded no-op rather
        // than relying on every caller to normalize it first.
        writeMask = EffectiveWriteMask(bitmap, writeMask);

        // A zero write mask deliberately selects no destination planes.  The
        // graphics primitives still succeed; they simply leave the raster
        // unchanged.  Treat masks that select only planes beyond the bitmap's
        // declared depth the same way.
        if (writeMask == 0)
            return true;

        var bitMask = (byte)(0x80 >> (x & 7));
        // Validate every selected plane before the first write.  This keeps a
        // malformed depth-2+ bitmap from exposing a partial colour update
        // when an earlier plane is addressable but a later one is not.
        Span<uint> selectedPlaneAddresses = stackalloc uint[8];
        Span<byte> selectedPlaneValues = stackalloc byte[8];
        Span<int> selectedPlaneIndices = stackalloc int[8];
        var selectedPlaneCount = 0;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) == 0)
                continue;

            if (!TryGetPlaneByteAddress(memory, bitmap, plane, x, y, out var byteAddress) ||
                !memory.TryReadByte(byteAddress, out var originalValue))
            {
                return false;
            }

            selectedPlaneAddresses[selectedPlaneCount] = byteAddress;
            selectedPlaneValues[selectedPlaneCount] = originalValue;
            selectedPlaneIndices[selectedPlaneCount] = plane;
            selectedPlaneCount++;
        }

        for (var index = 0; index < selectedPlaneCount; index++)
        {
            var byteAddress = selectedPlaneAddresses[index];
            var value = selectedPlaneValues[index];
            var plane = selectedPlaneIndices[index];

            var set = ((color >> plane) & 1) != 0;
            if ((drawMode & 2) != 0)
                set = !((value & bitMask) != 0);

            var next = set ? (byte)(value | bitMask) : (byte)(value & ~bitMask);
            if (!memory.TryWriteByte(byteAddress, next))
            {
                for (var rollback = 0; rollback < index; rollback++)
                    _ = memory.TryWriteByte(selectedPlaneAddresses[rollback], selectedPlaneValues[rollback]);

                return false;
            }
        }

        return true;
    }

    internal static bool TryGetPlaneByteAddress(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int plane,
        int x,
        int y,
        out uint byteAddress)
    {
        byteAddress = 0;
        if (plane < 0 || plane >= bitmap.Depth ||
            x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Rows)
        {
            return false;
        }

        var planePointerAddress = (ulong)bitmap.Address +
            (uint)GraphicsLayouts.BitMapPlanes +
            (uint)(plane * 4);
        if (planePointerAddress > uint.MaxValue ||
            !memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
            planeAddress == 0 ||
            (planeAddress & 1u) != 0)
        {
            return false;
        }

        // BytesPerRow and the row index are guest 16-bit quantities.  Keep
        // the multiplication wide until the final 32-bit guest-address
        // check; an int overflow here would wrap a valid modulo calculation
        // into a different plane row.
        var byteOffset = (ulong)(uint)y * GetPlaneRowStride(
                checked((ushort)bitmap.GuestBytesPerRow),
                checked((ushort)bitmap.PlaneBytesPerRow),
                bitmap.Flags) +
            (uint)(x >> 3);
        if (byteOffset > uint.MaxValue - planeAddress)
            return false;

        byteAddress = planeAddress + (uint)byteOffset;
        return true;
    }

    private static bool TryReadTextGeometry(
        IGraphicsMemory memory,
        uint rastPort,
        out int top,
        out int height)
    {
        top = 0;
        height = 0;
        if (!TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortCurrentY,
                out var baseline) ||
            !TryReadRastPortSignedWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortTextBaseline,
                out var textBaseline) ||
            !TryReadRastPortLong(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFont,
                out var fontAddress))
        {
            return false;
        }

        // A non-null TextFont is a native guest structure consumed through
        // aligned WORD fields.  Do not reinterpret an odd pointer as a
        // host-only font session by falling through to the cached TxHeight;
        // the resident ClearEOL/ClearScreen path would take an address-error
        // boundary before it could read tf_YSize.  A null or even-but-unmapped
        // pointer retains the compatibility fallback below for synthetic
        // backends whose metrics live outside the guest TextFont envelope.
        if (fontAddress != 0 && (fontAddress & 1u) != 0)
            return false;

        // ClearEOL/ClearScreen use the selected TextFont's Y size for the
        // cleared row height; TxBaseline remains the RastPort's baseline
        // offset.  Host-only compatibility sessions may expose an abstract
        // font backend without a guest TextFont envelope, so retain the
        // cached TxHeight fallback when the optional guest header is absent,
        // unreadable, or carries the zero placeholder used by InitRastPort.
        ushort textHeight = 0;
        if (fontAddress != 0 &&
            (fontAddress & 1u) == 0 &&
            TryAddress(
                fontAddress,
                GraphicsLayouts.TextFontYSize,
                sizeof(ushort),
                out var fontHeightAddress) &&
            memory.TryReadWord(fontHeightAddress, out var fontHeight) &&
            fontHeight != 0)
        {
            textHeight = fontHeight;
        }
        else if (!TryReadRastPortWord(
                     memory,
                     rastPort,
                     GraphicsLayouts.RastPortTextHeight,
                     out textHeight) ||
                 textHeight == 0)
        {
            return false;
        }

        top = baseline - textBaseline;
        height = textHeight;
        return true;
    }

    private static bool TryReadClearPen(
        IGraphicsMemory memory,
        uint rastPort,
        BitmapInfo bitmap,
        out int pen,
        out byte writeMask)
    {
        pen = 0;
        writeMask = 0;
        byte background = 0;
        // The destination mask is the first ownership decision.  A zero
        // effective mask has no drawable destination, so the clear vectors
        // must be able to publish their line-pattern reset without probing
        // the optional public mode or pen bytes that would only affect a
        // write which cannot occur.
        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortMask,
                out writeMask))
        {
            return false;
        }

        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
            return true;

        if (!TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortDrawMode,
                out var drawMode))
        {
            return false;
        }

        // ClearEOL/ClearScreen clear to colour zero, except when the JAM2
        // bit is set, where the graphics.library contract uses BPen.  The
        // classic bgfill_pen() helper tests JAM2 as a bit rather than
        // requiring an otherwise-clean base mode, so JAM2|COMPLEMENT and
        // JAM2|INVERSVID retain BPen for the subsequent SetAPen/RectFill
        // transaction as well.
        if ((drawMode & DrawModeJam2) != 0 &&
            !TryReadRastPortByte(
                memory,
                rastPort,
                GraphicsLayouts.RastPortBgPen,
                out background))
        {
            return false;
        }

        if ((drawMode & DrawModeJam2) != 0)
            pen = background;

        return true;
    }

    private static bool TryResetClearLinePattern(
        IGraphicsMemory memory,
        uint rastPort)
    {
        return TryAddress(
                   rastPort,
                   GraphicsLayouts.RastPortLinePatternCount,
                   sizeof(byte),
                   out var patternCountAddress) &&
            memory.TryWriteByte(patternCountAddress, 15);
    }

    private static bool FitsSignedGuestCoordinate(long value)
        => value >= short.MinValue && value <= short.MaxValue;

    private static bool TryAdmitPublicPenPath(
        IGraphicsMemory memory,
        uint rastPort)
    {
        // A readable RPF_NO_PENS marker transfers colour ownership to the
        // native/provider graphics context. Keep an unavailable optional
        // Flags word permissive so sparse compatibility RastPorts retain the
        // ordinary public-pen path, matching the other planar primitives.
        return !(
            TryReadRastPortWord(
                memory,
                rastPort,
                GraphicsLayouts.RastPortFlags,
                out var flags) &&
            (flags & GraphicsLayouts.RastPortNoPens) != 0);
    }

    private static bool FillRectangle(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int xMin,
        int yMin,
        int xMax,
        int yMax,
        int pen,
        byte writeMask,
        Func<int, int, bool>? pixelVisible = null,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null)
    {
        if (xMin > xMax || yMin > yMax)
            return true;

        // A clear/fill with no selected destination planes is a successful
        // no-op. Keep the caller's line-state publication outside this helper,
        // but avoid walking every clipped pixel of a large bitmap here.
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0)
            return true;

        if (!TryProbeBitmapRegion(
                memory,
                bitmap,
                xMin,
                yMin,
                xMax,
                yMax,
                writeMask,
                pixelVisible))
        {
            return false;
        }

        if (!TrySnapshotBitmapRegion(
                memory,
                bitmap,
                xMin,
                yMin,
                xMax,
                yMax,
                writeMask,
                out var snapshotAddresses,
                out var snapshotValues,
                snapshotAddressBuffer,
                snapshotValueBuffer,
                pixelVisible))
        {
            return false;
        }

        // The clipped byte span above is the complete destination contract.
        // Keep the publication walk on that same clipped rectangle: callers
        // may legally pass signed WORD bounds far outside the bitmap, and
        // visiting every off-screen coordinate would turn a harmless no-op
        // into an unbounded host loop.
        var clippedLeft = Math.Max(0, xMin);
        var clippedTop = Math.Max(0, yMin);
        var clippedRight = Math.Min(bitmap.Width - 1, xMax);
        var clippedBottom = Math.Min(bitmap.Rows - 1, yMax);
        if (clippedLeft > clippedRight || clippedTop > clippedBottom)
            return true;

        var success = true;
        for (var y = clippedTop; y <= clippedBottom; y++)
        {
            for (var x = clippedLeft; x <= clippedRight; x++)
            {
                if (pixelVisible is null || pixelVisible(x, y))
                    success &= SetBitmapPixel(memory, bitmap, x, y, pen, 0, writeMask);
            }
        }

        if (!success)
            RestoreBitmapSnapshot(memory, snapshotAddresses, snapshotValues);
        return success;
    }

    private static ulong GetClippedLogicalCellCount(
        BitmapInfo bitmap,
        int left,
        int top,
        int right,
        int bottom)
    {
        if (left > right || top > bottom)
            return 0;

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(bitmap.Width - 1, right);
        var clippedBottom = Math.Min(bitmap.Rows - 1, bottom);
        if (clippedLeft > clippedRight || clippedTop > clippedBottom)
            return 0;

        return (ulong)(uint)(clippedRight - clippedLeft + 1) *
            (uint)(clippedBottom - clippedTop + 1);
    }

    internal static bool TrySnapshotBitmapRegion(
        IGraphicsMemory memory,
        BitmapInfo bitmap,
        int left,
        int top,
        int right,
        int bottom,
        byte writeMask,
        out List<uint> snapshotAddresses,
        out List<byte> snapshotValues,
        List<uint>? snapshotAddressBuffer = null,
        List<byte>? snapshotValueBuffer = null,
        Func<int, int, bool>? pixelVisible = null)
    {
        snapshotAddresses = snapshotAddressBuffer ?? new List<uint>();
        snapshotValues = snapshotValueBuffer ?? new List<byte>();
        snapshotAddresses.Clear();
        snapshotValues.Clear();
        writeMask = EffectiveWriteMask(bitmap, writeMask);
        if (writeMask == 0 || left > right || top > bottom)
            return true;

        var clippedLeft = Math.Max(0, left);
        var clippedTop = Math.Max(0, top);
        var clippedRight = Math.Min(bitmap.Width - 1, right);
        var clippedBottom = Math.Min(bitmap.Rows - 1, bottom);
        if (clippedLeft > clippedRight || clippedTop > clippedBottom)
            return true;

        var firstByte = clippedLeft >> 3;
        var lastByte = clippedRight >> 3;
        var bytesPerRow = (uint)(lastByte - firstByte + 1);
        var clippedRows = (ulong)(uint)(clippedBottom - clippedTop + 1);
        var selectedPlanes = 0u;
        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) != 0)
                selectedPlanes++;
        }

        if (selectedPlanes == 0)
            return true;

        var touchedPixels = (ulong)(uint)(clippedRight - clippedLeft + 1) *
            (uint)(clippedBottom - clippedTop + 1);
        if ((ulong)bytesPerRow * clippedRows * selectedPlanes > int.MaxValue ||
            touchedPixels > int.MaxValue)
            return false;

        if (pixelVisible is not null)
        {
            for (var y = clippedTop; y <= clippedBottom; y++)
            {
                for (var x = clippedLeft; x <= clippedRight; x++)
                {
                    if (!pixelVisible(x, y))
                        continue;

                    if (!TryProbeBitmapWrite(
                            memory,
                            bitmap,
                            x,
                            y,
                            writeMask,
                            snapshotAddresses,
                            snapshotValues))
                    {
                        snapshotAddresses.Clear();
                        snapshotValues.Clear();
                        return false;
                    }
                }
            }

            return true;
        }

        for (var plane = 0; plane < bitmap.Depth; plane++)
        {
            if ((writeMask & (1 << plane)) == 0)
                continue;

            var planePointerAddress = (ulong)bitmap.Address +
                (uint)GraphicsLayouts.BitMapPlanes +
                (uint)(plane * 4);
            if (planePointerAddress > uint.MaxValue ||
                !memory.TryReadLong((uint)planePointerAddress, out var planeAddress) ||
                planeAddress == 0)
            {
                return false;
            }

            for (var y = clippedTop; y <= clippedBottom; y++)
            {
                var rowOffset = (ulong)(uint)y * GetPlaneRowStride(
                        checked((ushort)bitmap.GuestBytesPerRow),
                        checked((ushort)bitmap.PlaneBytesPerRow),
                        bitmap.Flags) +
                    (uint)firstByte;
                var rowAddress = (ulong)planeAddress + rowOffset;
                if (rowAddress > uint.MaxValue ||
                    (ulong)(bytesPerRow - 1) > (ulong)uint.MaxValue - rowAddress)
                {
                    return false;
                }

                for (var offset = 0u; offset < bytesPerRow; offset++)
                {
                    var address = (uint)rowAddress + offset;
                    if (!memory.TryReadByte(address, out var value))
                        return false;

                    snapshotAddresses.Add(address);
                    snapshotValues.Add(value);
                }
            }
        }

        return true;
    }

    private static bool TryGetAreaPatternPixel(
        IGraphicsMemory memory,
        uint areaPattern,
        sbyte areaPatternSize,
        int depth,
        int x,
        int y,
        byte foreground,
        byte background,
        byte drawMode,
        out int color,
        out bool drawPixel)
    {
        color = foreground;
        drawPixel = true;

        var inverseVideo = (drawMode & 4) != 0;
        var jam2 = (drawMode & 1) != 0;
        var complement = (drawMode & 2) != 0;

        // COMPLEMENT|JAM2 is the classic whole-region inversion shortcut.
        // The area-pattern source and its size are not part of that
        // operation, so return before touching either guest pattern word.
        // This keeps an unrelated or provider-owned AreaPtrn allocation from
        // stealing an otherwise valid native inversion call.
        if (complement && jam2)
        {
            drawPixel = true;
            return true;
        }

        // A null area pattern is an all-one source.  INVERSVID therefore
        // turns that source into all zeroes: JAM1 writes nothing, while JAM2
        // deposits BPen.  COMPLEMENT|JAM2 is the native whole-region invert
        // shortcut and deliberately ignores both pens and the source.
        if (areaPattern == 0)
        {
            if (inverseVideo)
            {
                drawPixel = jam2;
                color = background;
                return true;
            }

            color = foreground;
            drawPixel = true;
            return true;
        }

        if (areaPatternSize >= 0)
        {
            var exponent = areaPatternSize;
            if (exponent > 15)
                return false;

            var rows = 1 << exponent;
            var row = y & (rows - 1);
            var addressOffset = (ulong)row * 2u;
            if (addressOffset > uint.MaxValue ||
                areaPattern > uint.MaxValue - (uint)addressOffset ||
                !memory.TryReadWord(areaPattern + (uint)addressOffset, out var patternWord))
            {
                return false;
            }

            var patternBit = (patternWord & (0x8000 >> (x & 15))) != 0;
            if (inverseVideo)
                patternBit = !patternBit;

            if (complement || !jam2)
            {
                drawPixel = patternBit;
                color = foreground;
                return true;
            }

            // JAM2 writes both phases of the pattern: foreground for source
            // one and background for source zero.
            color = patternBit ? foreground : background;
            return true;
        }

        // Negative AreaPtSz selects a multicolour pattern: one contiguous
        // power-of-two-height plane follows another in guest memory.
        var negativeExponent = -(int)areaPatternSize;
        if (negativeExponent > 15)
            return false;

        var patternRows = 1 << negativeExponent;
        var rowOffset = (ulong)(y & (patternRows - 1)) * 2u;
        var planeStride = (ulong)patternRows * 2u;
        color = 0;
        for (var plane = 0; plane < depth; plane++)
        {
            var addressOffset = ((ulong)plane * planeStride) + rowOffset;
            if (addressOffset > uint.MaxValue ||
                areaPattern > uint.MaxValue - (uint)addressOffset ||
                !memory.TryReadWord(areaPattern + (uint)addressOffset, out var patternWord))
            {
                return false;
            }

            if ((patternWord & (0x8000 >> (x & 15))) != 0)
                color |= 1 << plane;
        }

        // INVERSVID reverses the complete source drawing area before the
        // destination draw-mode is applied.  For a multicolor pattern that
        // means inverting every declared source plane, not merely the final
        // non-zero test used by COMPLEMENT.
        if (inverseVideo)
            color = ((1 << depth) - 1) ^ color;

        var sourceNonZero = color != 0;

        // A JAM1/complement operation uses the non-zero multicolour source as
        // its stencil, while JAM2 deposits the pattern colour itself,
        // including zero.
        if (complement || !jam2)
        {
            color = foreground;
            drawPixel = sourceNonZero;
        }

        return true;
    }

    private static bool TryReadStencilBit(
        IGraphicsMemory memory,
        uint mask,
        uint bytesPerRow,
        int row,
        int column,
        out bool set)
    {
        set = false;
        if (row < 0 || column < 0)
            return false;

        var offset = ((ulong)(uint)row * bytesPerRow) + (uint)(column >> 3);
        if (offset > uint.MaxValue || mask > uint.MaxValue - (uint)offset ||
            !memory.TryReadByte(mask + (uint)offset, out var value))
        {
            return false;
        }

        set = (value & (0x80 >> (column & 7))) != 0;
        return true;
    }

    private static bool TryProbeContiguous(
        IGraphicsMemory memory,
        uint address,
        uint bytesPerRow,
        uint rows)
    {
        var byteCount = (ulong)bytesPerRow * rows;
        // Portable preflight is intentionally bounded by the host's
        // transactional/list representation.  A guest UWORD geometry can
        // describe a span above Int32.MaxValue; walking that span byte by
        // byte would make a malformed or merely unstageable request hang or
        // exhaust host memory before the native/provider boundary can claim
        // it.
        if (byteCount == 0 || byteCount > int.MaxValue ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        for (var row = 0u; row < rows; row++)
        {
            var rowAddress = address + row * bytesPerRow;
            for (var offset = 0u; offset < bytesPerRow; offset++)
            {
                if (!memory.TryReadByte(rowAddress + offset, out _))
                    return false;
            }
        }

        return true;
    }

    private static bool TryProbeContiguousRows(
        IGraphicsMemory memory,
        uint address,
        uint rowStride,
        uint rows,
        uint touchedBytes)
    {
        if (address == 0 || rowStride == 0 || rows == 0 || touchedBytes == 0)
            return false;

        var lastRowOffset = (ulong)(rows - 1) * rowStride;
        var touchedSpan = (ulong)touchedBytes * rows;
        if (touchedSpan == 0 || touchedSpan > int.MaxValue ||
            lastRowOffset > uint.MaxValue ||
            (ulong)address + lastRowOffset + touchedBytes - 1UL > uint.MaxValue)
        {
            return false;
        }

        for (var row = 0u; row < rows; row++)
        {
            var rowAddress = address + (row * rowStride);
            for (var offset = 0u; offset < touchedBytes; offset++)
            {
                if (!memory.TryReadByte(rowAddress + offset, out _))
                    return false;
            }
        }

        return true;
    }

    private static bool TryAcquireAreaTemporaryRaster(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend? allocator,
        uint rastPort,
        uint requiredBytes,
        out uint temporaryRaster,
        out uint ownedTemporaryRaster)
    {
        temporaryRaster = 0;
        ownedTemporaryRaster = 0;
        if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortTmpRas,
                sizeof(uint),
                out var tmpRasAddress) ||
            !memory.TryReadLong(tmpRasAddress, out var tmpRas))
        {
            return false;
        }

        if (tmpRas != 0)
        {
            if (!TryGetOptionalTmpRas(
                    memory,
                    rastPort,
                    requiredBytes,
                    out temporaryRaster,
                    out var hasTemporaryRaster))
            {
                return false;
            }

            return hasTemporaryRaster;
        }

        var allocatedRaster = 0u;
        if (allocator is null ||
            !allocator.TryAllocate(
                requiredBytes,
                GraphicsMemoryClass.Chip,
                out allocatedRaster) ||
            allocatedRaster == 0 ||
            (allocatedRaster & 1u) != 0 ||
            !TryProbeContiguous(memory, allocatedRaster, requiredBytes, 1))
        {
            if (allocatedRaster != 0)
                allocator?.Free(allocatedRaster, requiredBytes, GraphicsMemoryClass.Chip);

            return allocator is null;
        }

        ownedTemporaryRaster = allocatedRaster;
        temporaryRaster = allocatedRaster;
        return true;
    }

    private static bool TryGetOptionalTmpRas(
        IGraphicsMemory memory,
        uint rastPort,
        uint requiredBytes,
        out uint buffer,
        out bool hasTemporaryRaster)
    {
        buffer = 0;
        hasTemporaryRaster = false;
        if (!TryAddress(
                rastPort,
                GraphicsLayouts.RastPortTmpRas,
                sizeof(uint),
                out var tmpRasAddress) ||
            !memory.TryReadLong(tmpRasAddress, out var tmpRas) ||
            tmpRas == 0)
        {
            // Existing RastPorts assembled by Intuition may leave TmpRas
            // unset until a caller needs an operation.  Preserve that
            // transitional path; an explicitly attached descriptor is
            // validated strictly below.
            return true;
        }

        if ((tmpRas & 1u) != 0 ||
            !TryAddress(
                tmpRas,
                GraphicsLayouts.TmpRasRasPtr,
                sizeof(uint),
                out var rasBufferAddress) ||
            !TryAddress(
                tmpRas,
                GraphicsLayouts.TmpRasByteCount,
                sizeof(uint),
                out var byteCountAddress) ||
            !memory.TryReadLong(rasBufferAddress, out var rasBuffer) ||
            !memory.TryReadLong(byteCountAddress, out var byteCount) ||
            rasBuffer == 0 ||
            byteCount == 0)
        {
            return false;
        }

        if (requiredBytes == 0 ||
            requiredBytes > byteCount ||
            !TryProbeContiguous(memory, rasBuffer, requiredBytes, 1))
        {
            return false;
        }

        buffer = rasBuffer;
        hasTemporaryRaster = true;
        return true;
    }

    private static bool ClearTemporaryRaster(
        IGraphicsMemory memory,
        uint buffer,
        uint byteCount)
    {
        for (var offset = 0u; offset < byteCount; offset++)
        {
            if (!memory.TryWriteByte(buffer + offset, 0))
                return false;
        }

        return true;
    }

    private static bool TrySetAreaMaskBit(
        IGraphicsMemory memory,
        uint temporaryRaster,
        bool[]? managedMask,
        AreaRasterBounds bounds,
        int x,
        int y)
    {
        if (temporaryRaster != 0)
        {
            return TrySetTemporaryRasterBit(
                memory,
                temporaryRaster,
                bounds.BytesPerRow,
                x,
                y);
        }

        if (managedMask is null ||
            x < 0 || x >= bounds.Width ||
            y < 0 || y >= bounds.Height)
        {
            return false;
        }

        managedMask[(y * bounds.Width) + x] = true;
        return true;
    }

    private static bool TryGetAreaMaskBit(
        IGraphicsMemory memory,
        uint temporaryRaster,
        bool[]? managedMask,
        AreaRasterBounds bounds,
        int x,
        int y,
        out bool set)
    {
        if (temporaryRaster != 0)
        {
            return TryGetTemporaryRasterBit(
                memory,
                temporaryRaster,
                bounds.BytesPerRow,
                x,
                y,
                out set);
        }

        set = false;
        if (managedMask is null ||
            x < 0 || x >= bounds.Width ||
            y < 0 || y >= bounds.Height)
        {
            return false;
        }

        set = managedMask[(y * bounds.Width) + x];
        return true;
    }

    private static bool TryGetTemporaryRasterBit(
        IGraphicsMemory memory,
        uint buffer,
        int bytesPerRow,
        int x,
        int y,
        out bool set)
    {
        set = false;
        var offset = (ulong)(uint)y * (uint)bytesPerRow + (uint)(x >> 3);
        if (offset > uint.MaxValue - buffer ||
            !memory.TryReadByte(buffer + (uint)offset, out var value))
        {
            return false;
        }

        set = (value & (0x80 >> (x & 7))) != 0;
        return true;
    }

    private static bool TrySetTemporaryRasterBit(
        IGraphicsMemory memory,
        uint buffer,
        int bytesPerRow,
        int x,
        int y)
    {
        var offset = (ulong)(uint)y * (uint)bytesPerRow + (uint)(x >> 3);
        if (offset > uint.MaxValue - buffer ||
            !memory.TryReadByte(buffer + (uint)offset, out var value))
        {
            return false;
        }

        value |= (byte)(0x80 >> (x & 7));
        return memory.TryWriteByte(buffer + (uint)offset, value);
    }

    private readonly struct BitmapLayout
    {
        internal BitmapLayout(
            ushort bytesPerRow,
            ushort planeBytesPerRow,
            ushort rows,
            byte depth,
            byte flags)
        {
            BytesPerRow = bytesPerRow;
            PlaneBytesPerRow = planeBytesPerRow;
            Rows = rows;
            Depth = depth;
            Flags = flags;
        }

        internal ushort BytesPerRow { get; }
        internal ushort PlaneBytesPerRow { get; }
        internal ushort Rows { get; }
        internal byte Depth { get; }
        internal byte Flags { get; }
        internal uint Width => (uint)PlaneBytesPerRow * 8u;
    }

    internal readonly struct BitmapInfo
    {
        internal BitmapInfo(uint address, ushort bytesPerRow, ushort rows, byte depth)
            : this(address, bytesPerRow, bytesPerRow, rows, depth, 0)
        {
        }

        internal BitmapInfo(
            uint address,
            ushort guestBytesPerRow,
            ushort planeBytesPerRow,
            ushort rows,
            byte depth,
            byte flags)
            : this(
                address,
                guestBytesPerRow,
                planeBytesPerRow,
                rows,
                depth,
                flags,
                planeBytesPerRow * 8)
        {
        }

        private BitmapInfo(
            uint address,
            ushort guestBytesPerRow,
            ushort planeBytesPerRow,
            ushort rows,
            byte depth,
            byte flags,
            int width)
        {
            Address = address;
            // Existing portable consumers treat BytesPerRow as the physical
            // stride of one linked plane.  Keep that internal convention
            // stable while retaining the aggregate guest field for the
            // interleaved ABI and display-layout comparisons.
            BytesPerRow = planeBytesPerRow;
            PlaneBytesPerRow = planeBytesPerRow;
            GuestBytesPerRow = guestBytesPerRow;
            Rows = rows;
            Depth = depth;
            Flags = flags;
            Width = width;
        }

        /// <summary>
        /// Returns a view of this guest bitmap with a compatibility width.
        /// The V39/V40 interleaved BltMaskBitMapRastPort path constructs its
        /// temporary mask bitmap from the aggregate guest BytesPerRow, so its
        /// source width is wider than the normal per-plane geometry.
        /// </summary>
        internal BitmapInfo WithWidth(int width)
            => new(
                Address,
                checked((ushort)GuestBytesPerRow),
                checked((ushort)PlaneBytesPerRow),
                checked((ushort)Rows),
                checked((byte)Depth),
                Flags,
                width);

        internal uint Address { get; }
        internal int BytesPerRow { get; }
        internal int PlaneBytesPerRow { get; }
        internal int GuestBytesPerRow { get; }
        internal int Rows { get; }
        internal int Depth { get; }
        internal byte Flags { get; }
        internal int Width { get; }
    }

    internal readonly struct AreaPoint
    {
        internal AreaPoint(short x, short y)
        {
            X = x;
            Y = y;
        }

        internal int X { get; }
        internal int Y { get; }
    }

    internal readonly struct AreaEllipseInfo
    {
        internal AreaEllipseInfo(int centerX, int centerY, int radiusX, int radiusY)
        {
            CenterX = centerX;
            CenterY = centerY;
            RadiusX = radiusX;
            RadiusY = radiusY;
        }

        internal int CenterX { get; }
        internal int CenterY { get; }
        internal int RadiusX { get; }
        internal int RadiusY { get; }
    }

    internal readonly struct AreaShape
    {
        private AreaShape(List<AreaPoint>? polygon, AreaEllipseInfo ellipse, bool isEllipse)
        {
            Polygon = polygon;
            Ellipse = ellipse;
            IsEllipse = isEllipse;
        }

        internal static AreaShape FromPolygon(List<AreaPoint> polygon)
            => new(polygon, default, false);

        internal static AreaShape FromEllipse(AreaEllipseInfo ellipse)
            => new(null, ellipse, true);

        internal List<AreaPoint>? Polygon { get; }
        internal AreaEllipseInfo Ellipse { get; }
        internal bool IsEllipse { get; }
    }

    internal readonly struct AreaEdge
    {
        internal AreaEdge(AreaPoint first, AreaPoint second)
        {
            First = first;
            Second = second;
        }

        internal AreaPoint First { get; }
        internal AreaPoint Second { get; }
    }

    internal readonly struct AreaCrossing
    {
        internal AreaCrossing(long numerator, long denominator)
        {
            Numerator = numerator;
            Denominator = denominator;
        }

        internal long Numerator { get; }
        internal long Denominator { get; }
    }

    private readonly struct AreaState
    {
        internal AreaState(
            uint areaInfo,
            uint vectorTable,
            uint flagTable,
            uint vectorPointer,
            uint flagPointer,
            ushort count,
            ushort maxCount,
            short firstX,
            short firstY,
            byte lastFlag)
        {
            AreaInfo = areaInfo;
            VectorTable = vectorTable;
            FlagTable = flagTable;
            VectorPointer = vectorPointer;
            FlagPointer = flagPointer;
            Count = count;
            MaxCount = maxCount;
            FirstX = firstX;
            FirstY = firstY;
            LastFlag = lastFlag;
        }

        internal uint AreaInfo { get; }
        internal uint VectorTable { get; }
        internal uint FlagTable { get; }
        internal uint VectorPointer { get; }
        internal uint FlagPointer { get; }
        internal ushort Count { get; }
        internal ushort MaxCount { get; }
        internal short FirstX { get; }
        internal short FirstY { get; }
        internal byte LastFlag { get; }
    }
}
