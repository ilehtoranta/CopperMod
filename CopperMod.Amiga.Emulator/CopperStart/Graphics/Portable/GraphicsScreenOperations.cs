using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// The four guest envelopes owned by an allocator-backed classic screen
/// resource chain.  Plane storage is caller/provider-owned for the table
/// form, or one contiguous chip span for the explicit owned-plane form.
/// </summary>
internal readonly record struct ScreenResourceChainAllocation(
    uint View,
    uint ViewPort,
    uint RasInfo,
    uint BitMap,
    uint Plane0,
    byte Depth,
    ushort Width,
    ushort Height,
    bool OwnsPlane = false,
    uint PlaneBytes = 0,
    IReadOnlyList<uint>? PlanePointers = null);

/// <summary>
/// Allocator transaction and ownership boundary for the portable screen
/// resource chain.  It builds on the layout-only constructor in
/// <see cref="GraphicsRasterOperations"/>. The caller-owned form does not
/// choose a plane provider; the explicit WithPlane form is the only path that
/// claims chip storage. Neither form claims multi-viewport merge or a
/// CyberGraphX surface.
/// </summary>
internal static class GraphicsScreenOperations
{
    internal sealed class Registry
    {
        private readonly Dictionary<uint, ScreenResourceChainAllocation> _owned = new();
        private readonly List<(uint Address, uint Bytes)> _ranges = new();

        internal bool Add(ScreenResourceChainAllocation allocation)
        {
            if (allocation.View == 0 ||
                allocation.ViewPort == 0 ||
                allocation.RasInfo == 0 ||
                allocation.BitMap == 0 ||
                _owned.ContainsKey(allocation.View) ||
                allocation.View == allocation.ViewPort ||
                allocation.View == allocation.RasInfo ||
                allocation.View == allocation.BitMap ||
                allocation.ViewPort == allocation.RasInfo ||
                allocation.ViewPort == allocation.BitMap ||
                allocation.RasInfo == allocation.BitMap ||
                (allocation.PlanePointers is not null &&
                    !HasValidPlaneTable(allocation)) ||
                (allocation.OwnsPlane &&
                    (allocation.Plane0 == 0 ||
                     (allocation.Plane0 & 1u) != 0 ||
                     allocation.Plane0 == allocation.View ||
                     allocation.Plane0 == allocation.ViewPort ||
                     allocation.Plane0 == allocation.RasInfo ||
                     allocation.Plane0 == allocation.BitMap)))
            {
                return false;
            }

            var ranges = GetRanges(allocation);
            foreach (var candidate in ranges)
            {
                foreach (var existing in _ranges)
                {
                    if (Overlaps(
                            candidate.Address,
                            candidate.Bytes,
                            existing.Address,
                            existing.Bytes))
                    {
                        return false;
                    }
                }
            }

            _owned.Add(allocation.View, allocation);
            _ranges.AddRange(ranges);
            return true;
        }

        internal bool TryGet(
            uint view,
            out ScreenResourceChainAllocation allocation)
            => _owned.TryGetValue(view, out allocation);

        internal bool Contains(uint view)
            => view != 0 && _owned.ContainsKey(view);

        internal bool Remove(ScreenResourceChainAllocation allocation)
        {
            if (!_owned.Remove(allocation.View))
                return false;

            foreach (var range in GetRanges(allocation))
            {
                var index = _ranges.FindIndex(item =>
                    item.Address == range.Address && item.Bytes == range.Bytes);
                if (index >= 0)
                    _ranges.RemoveAt(index);
            }
            return true;
        }
    }

    internal static bool TryAllocate(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        byte depth,
        ushort width,
        ushort height,
        uint plane0,
        out ScreenResourceChainAllocation allocation)
        => TryAllocateCore(
            memory,
            allocator,
            registry,
            depth,
            width,
            height,
            plane0,
            planePointers: null,
            ownPlane: false,
            clearPlane: false,
            requireDisplayDma: false,
            out allocation);

    /// <summary>
    /// Allocates one standard-planar chip plane and the four public screen
    /// envelopes as one transaction. The existing <see cref="TryAllocate"/>
    /// form remains caller/provider-owned.
    /// </summary>
    internal static bool TryAllocateWithPlane(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        byte depth,
        ushort width,
        ushort height,
        bool clearPlane,
        bool requireDisplayDma,
        out ScreenResourceChainAllocation allocation)
    {
        if (depth != 1)
        {
            allocation = default;
            return false;
        }

        return TryAllocateCore(
            memory,
            allocator,
            registry,
            depth,
            width,
            height,
            plane0: 0,
            planePointers: null,
            ownPlane: true,
            clearPlane,
            requireDisplayDma,
            out allocation);
    }

    /// <summary>
    /// Allocates one contiguous chip span for all standard-planar planes and
    /// publishes a depth-sized table of offsets into that span. The span is
    /// the sole owned plane resource; callers receive the table for exact
    /// teardown but do not own or release its entries independently.
    /// </summary>
    internal static bool TryAllocateWithPlanes(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        byte depth,
        ushort width,
        ushort height,
        bool clearPlanes,
        bool requireDisplayDma,
        out ScreenResourceChainAllocation allocation)
        => TryAllocateCore(
            memory,
            allocator,
            registry,
            depth,
            width,
            height,
            plane0: 0,
            planePointers: null,
            ownPlane: true,
            clearPlane: clearPlanes,
            requireDisplayDma,
            out allocation);

    /// <summary>
    /// Allocates the four public screen envelopes around a caller/provider
    /// owned standard-planar table. Plane storage is never allocated,
    /// cleared, registered, or released by this owner.
    /// </summary>
    internal static bool TryAllocatePlanes(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        byte depth,
        ushort width,
        ushort height,
        IReadOnlyList<uint> planes,
        out ScreenResourceChainAllocation allocation)
        => TryAllocateCore(
            memory,
            allocator,
            registry,
            depth,
            width,
            height,
            plane0: 0,
            planePointers: planes,
            ownPlane: false,
            clearPlane: false,
            requireDisplayDma: false,
            out allocation);

    private static bool TryAllocateCore(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        byte depth,
        ushort width,
        ushort height,
        uint plane0,
        IReadOnlyList<uint>? planePointers,
        bool ownPlane,
        bool clearPlane,
        bool requireDisplayDma,
        out ScreenResourceChainAllocation allocation)
    {
        allocation = default;
        if (memory is null || allocator is null || registry is null ||
            depth == 0 || depth > 8 || width == 0 || height == 0 ||
            (!ownPlane && planePointers is null &&
                (depth != 1 || plane0 == 0 || (plane0 & 1u) != 0)) ||
            (!ownPlane && planePointers is not null &&
                !TryValidatePlanePointers(
                    planePointers,
                    depth,
                    view: 0,
                    viewPort: 0,
                    rasInfo: 0,
                    bitMap: 0)))
        {
            return false;
        }

        IReadOnlyList<uint>? callerPlanePointers = null;
        if (!ownPlane)
        {
            callerPlanePointers = planePointers ?? new[] { plane0 };
        }

        var callerPlaneBytes = 0u;
        if (callerPlanePointers is not null &&
            (!TryGetPlaneBytes(width, height, 1, out callerPlaneBytes) ||
             !TryValidateCallerPlaneRanges(
                 callerPlanePointers,
                 callerPlaneBytes)))
        {
            return false;
        }

        var publishedPlanes = planePointers is null
            ? null
            : CopyPlanePointers(planePointers);
        if (publishedPlanes is not null)
            plane0 = publishedPlanes[0];

        var allocated = new List<(uint Address, uint Bytes)>();
        uint ownedPlane = 0;
        uint ownedPlaneBytes = 0;

        if (ownPlane)
        {
            if (!TryGetPlaneBytes(width, height, depth, out ownedPlaneBytes))
                return false;

            var succeeded = allocator.TryAllocate(
                ownedPlaneBytes,
                GraphicsMemoryClass.Chip,
                out ownedPlane);
            if (!succeeded)
            {
                if (ownedPlane != 0)
                    allocator.Free(ownedPlane, ownedPlaneBytes, GraphicsMemoryClass.Chip);
                return false;
            }

            if (ownedPlane == 0 || (ownedPlane & 1u) != 0 ||
                !TryGetGuestSpan(ownedPlane, ownedPlaneBytes) ||
                (requireDisplayDma &&
                 (memory is not IGraphicsDisplayMemory displayMemory ||
                  !displayMemory.IsDisplayDmaRange(ownedPlane, ownedPlaneBytes))) ||
                (clearPlane && !ClearPlane(memory, ownedPlane, ownedPlaneBytes)))
            {
                if (ownedPlane != 0)
                    allocator.Free(ownedPlane, ownedPlaneBytes, GraphicsMemoryClass.Chip);
                return false;
            }

            plane0 = ownedPlane;
            if (depth > 1)
            {
                if (!TryGetPlaneBytes(width, height, 1, out var planeBytes))
                {
                    allocator.Free(ownedPlane, ownedPlaneBytes, GraphicsMemoryClass.Chip);
                    return false;
                }

                var ownedPointers = new uint[depth];
                for (var index = 0; index < ownedPointers.Length; index++)
                {
                    var pointer = (ulong)ownedPlane +
                        ((ulong)planeBytes * (uint)index);
                    if (pointer == 0 || pointer > uint.MaxValue ||
                        (pointer & 1UL) != 0)
                    {
                        allocator.Free(
                            ownedPlane,
                            ownedPlaneBytes,
                            GraphicsMemoryClass.Chip);
                        return false;
                    }

                    ownedPointers[index] = (uint)pointer;
                }

                publishedPlanes = Array.AsReadOnly(ownedPointers);
            }
        }

        bool TryEnvelope(uint bytes, out uint address)
        {
            address = 0;
            var succeeded = allocator.TryAllocate(
                bytes,
                GraphicsMemoryClass.Public,
                out address);
            if (!succeeded)
            {
                // A provider is allowed to return a provisional span with a
                // false status.  The transaction owns that span until it is
                // explicitly released here.
                if (address != 0)
                    allocator.Free(address, bytes, GraphicsMemoryClass.Public);
                address = 0;
                return false;
            }

            var candidateAddress = address;
            if (candidateAddress == 0 || (candidateAddress & 1u) != 0 ||
                !TryProbeEnvelope(memory, candidateAddress, bytes) ||
                allocated.Exists(item => Overlaps(
                    candidateAddress,
                    bytes,
                    item.Address,
                    item.Bytes)) ||
                (ownPlane &&
                 Overlaps(candidateAddress, bytes, ownedPlane, ownedPlaneBytes)) ||
                (!ownPlane &&
                 callerPlanePointers is not null &&
                 OverlapsAny(
                     candidateAddress,
                     bytes,
                     callerPlanePointers,
                     callerPlaneBytes)))
            {
                if (candidateAddress != 0)
                    allocator.Free(candidateAddress, bytes, GraphicsMemoryClass.Public);
                address = 0;
                return false;
            }

            allocated.Add((candidateAddress, bytes));
            return true;
        }

        void ReleaseAllocated()
        {
            if (ownPlane && ownedPlane != 0)
            {
                allocator.Free(
                    ownedPlane,
                    ownedPlaneBytes,
                    GraphicsMemoryClass.Chip);
                ownedPlane = 0;
            }

            for (var index = allocated.Count - 1; index >= 0; index--)
            {
                var item = allocated[index];
                allocator.Free(item.Address, item.Bytes, GraphicsMemoryClass.Public);
            }

            allocated.Clear();
        }

        if (!TryEnvelope((uint)GraphicsLayouts.ViewSize, out var view) ||
            !TryEnvelope((uint)GraphicsLayouts.ViewPortSize, out var viewPort) ||
            !TryEnvelope((uint)GraphicsLayouts.RasInfoSize, out var rasInfo) ||
            !TryEnvelope((uint)GraphicsLayouts.BitMapSize, out var bitMap))
        {
            ReleaseAllocated();
            return false;
        }

        var candidate = new ScreenResourceChainAllocation(
            view,
            viewPort,
            rasInfo,
            bitMap,
            plane0,
            depth,
            width,
            height,
            ownPlane,
            ownedPlaneBytes,
            publishedPlanes);

        var initialized = candidate.PlanePointers is null
            ? GraphicsRasterOperations.InitializeScreenResourceChain(
                memory,
                candidate.View,
                candidate.ViewPort,
                candidate.RasInfo,
                candidate.BitMap,
                candidate.Depth,
                candidate.Width,
                candidate.Height,
                candidate.Plane0)
            : GraphicsRasterOperations.InitializeScreenResourceChainPlanes(
                memory,
                candidate.View,
                candidate.ViewPort,
                candidate.RasInfo,
                candidate.BitMap,
                candidate.Depth,
                candidate.Width,
                candidate.Height,
                candidate.PlanePointers);

        if (!initialized)
        {
            ReleaseAllocated();
            return false;
        }

        if (!registry.Add(candidate))
        {
            if (candidate.PlanePointers is null)
            {
                _ = GraphicsRasterOperations.TeardownScreenResourceChain(
                    memory,
                    candidate.View,
                    candidate.ViewPort,
                    candidate.RasInfo,
                    candidate.BitMap);
            }
            else
            {
                _ = GraphicsRasterOperations.TeardownScreenResourceChainPlanes(
                    memory,
                    candidate.View,
                    candidate.ViewPort,
                    candidate.RasInfo,
                    candidate.BitMap,
                    candidate.PlanePointers);
            }
            ReleaseAllocated();
            return false;
        }

        allocation = candidate;
        return true;
    }

    internal static int Free(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        uint view)
    {
        if (view == 0)
            return GraphicsRasterOperations.Success;

        if (!registry.TryGet(view, out var allocation))
            return GraphicsRasterOperations.Failure;

        // Teardown is deliberately attempted before removing the registry
        // entry or returning any envelope to Exec.  A replaced viewport,
        // bitmap, or provider-owned link therefore leaves the whole chain
        // available to its actual owner.
        var tornDown = allocation.PlanePointers is null
            ? GraphicsRasterOperations.TeardownScreenResourceChain(
                memory,
                allocation.View,
                allocation.ViewPort,
                allocation.RasInfo,
                allocation.BitMap)
            : GraphicsRasterOperations.TeardownScreenResourceChainPlanes(
                memory,
                allocation.View,
                allocation.ViewPort,
                allocation.RasInfo,
                allocation.BitMap,
                allocation.PlanePointers);
        if (!tornDown)
        {
            return GraphicsRasterOperations.Failure;
        }

        if (!registry.Remove(allocation))
            return GraphicsRasterOperations.Failure;

        if (allocation.OwnsPlane)
        {
            allocator.Free(
                allocation.Plane0,
                allocation.PlaneBytes,
                GraphicsMemoryClass.Chip);
        }
        allocator.Free(
            allocation.BitMap,
            (uint)GraphicsLayouts.BitMapSize,
            GraphicsMemoryClass.Public);
        allocator.Free(
            allocation.RasInfo,
            (uint)GraphicsLayouts.RasInfoSize,
            GraphicsMemoryClass.Public);
        allocator.Free(
            allocation.ViewPort,
            (uint)GraphicsLayouts.ViewPortSize,
            GraphicsMemoryClass.Public);
        allocator.Free(
            allocation.View,
            (uint)GraphicsLayouts.ViewSize,
            GraphicsMemoryClass.Public);
        return GraphicsRasterOperations.Success;
    }

    private static bool TryGetPlaneBytes(
        ushort width,
        ushort height,
        byte depth,
        out uint bytes)
    {
        bytes = 0;
        var wordsPerRow = ((ulong)width + 15UL) >> 4;
        var total = wordsPerRow * 2UL * height * depth;
        if (total == 0 || total > uint.MaxValue)
            return false;

        bytes = (uint)total;
        return true;
    }

    private static bool HasValidPlaneTable(
        ScreenResourceChainAllocation allocation)
        => allocation.PlanePointers is not null &&
           TryValidatePlanePointers(
               allocation.PlanePointers,
               allocation.Depth,
               allocation.View,
               allocation.ViewPort,
               allocation.RasInfo,
               allocation.BitMap);

    private static bool TryValidatePlanePointers(
        IReadOnlyList<uint> planes,
        byte depth,
        uint view,
        uint viewPort,
        uint rasInfo,
        uint bitMap)
    {
        if (planes is null || depth == 0 || depth > 8 ||
            planes.Count != depth)
        {
            return false;
        }

        for (var index = 0; index < planes.Count; index++)
        {
            var plane = planes[index];
            if (plane == 0 || (plane & 1u) != 0 ||
                plane == view || plane == viewPort ||
                plane == rasInfo || plane == bitMap)
            {
                return false;
            }

            for (var prior = 0; prior < index; prior++)
            {
                if (planes[prior] == plane)
                    return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<uint> CopyPlanePointers(
        IReadOnlyList<uint> source)
    {
        var copy = new uint[source.Count];
        for (var index = 0; index < source.Count; index++)
            copy[index] = source[index];
        return Array.AsReadOnly(copy);
    }

    private static bool TryGetGuestSpan(uint address, uint bytes)
        => address != 0 && bytes != 0 &&
           address <= uint.MaxValue - (bytes - 1u);

    private static bool Overlaps(
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

    private static List<(uint Address, uint Bytes)> GetRanges(
        ScreenResourceChainAllocation allocation)
    {
        var ranges = new List<(uint Address, uint Bytes)>
        {
            (allocation.View, (uint)GraphicsLayouts.ViewSize),
            (allocation.ViewPort, (uint)GraphicsLayouts.ViewPortSize),
            (allocation.RasInfo, (uint)GraphicsLayouts.RasInfoSize),
            (allocation.BitMap, (uint)GraphicsLayouts.BitMapSize)
        };
        if (allocation.OwnsPlane)
            ranges.Add((allocation.Plane0, allocation.PlaneBytes));
        return ranges;
    }

    private static bool TryValidateCallerPlaneRanges(
        IReadOnlyList<uint> planes,
        uint planeBytes)
    {
        if (planes is null || planeBytes == 0)
            return false;

        // This owner never clears or copies caller plane bytes.  Distinct
        // links may therefore intentionally share storage; exact duplicate
        // links remain malformed and are rejected by TryValidatePlanePointers.
        // The admission boundary here is span validity, not an ownership
        // claim over the caller's raster contents.
        for (var index = 0; index < planes.Count; index++)
        {
            if (!TryGetGuestSpan(planes[index], planeBytes))
                return false;
        }

        return true;
    }

    private static bool OverlapsAny(
        uint address,
        uint bytes,
        IReadOnlyList<uint> planes,
        uint planeBytes)
    {
        for (var index = 0; index < planes.Count; index++)
        {
            if (Overlaps(address, bytes, planes[index], planeBytes))
                return true;
        }

        return false;
    }

    private static bool ClearPlane(
        IGraphicsMemory memory,
        uint address,
        uint bytes)
    {
        for (var offset = 0u; offset < bytes; offset++)
        {
            if (!memory.TryWriteByte(address + offset, 0))
                return false;
        }

        return true;
    }

    private static bool TryProbeEnvelope(
        IGraphicsMemory memory,
        uint address,
        uint bytes)
    {
        if (address == 0 || (address & 1u) != 0 || bytes == 0 ||
            address > uint.MaxValue - (bytes - 1u))
        {
            return false;
        }

        for (var offset = 0u; offset < bytes; offset++)
        {
            if (!memory.TryReadByte(address + offset, out _))
                return false;
        }

        return true;
    }
}
