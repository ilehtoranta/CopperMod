using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Allocator-owned classic Intuition screen prefix.  The Screen envelope
/// contains the embedded ViewPort, RastPort, and BitMap; View and RasInfo are
/// separate public graphics objects. Plane storage remains caller/provider
/// owned unless an explicit contiguous chip-plane form is selected.
/// </summary>
internal readonly record struct ScreenPrefixAllocation(
    uint Screen,
    uint View,
    uint ViewPort,
    uint RasInfo,
    uint RastPort,
    uint BitMap,
    uint Plane0,
    byte Depth,
    ushort Width,
    ushort Height,
    bool OwnsPlane = false,
    uint PlaneBytes = 0,
    IReadOnlyList<uint>? PlanePointers = null);

/// <summary>
/// Portable allocation and exact-owner teardown for one classic Screen
/// prefix.  This is deliberately separate from Intuition's host policy and
/// from CyberGraphX: it publishes only the standard planar embedded prefix.
/// </summary>
internal static class GraphicsScreenPrefixOperations
{
    internal sealed class Registry
    {
        private readonly Dictionary<uint, ScreenPrefixAllocation> _owned = new();
        private readonly List<(uint Address, uint Bytes)> _ranges = new();

        internal bool Add(ScreenPrefixAllocation allocation)
        {
            if (allocation.Screen == 0 || allocation.View == 0 ||
                allocation.RasInfo == 0 || _owned.ContainsKey(allocation.Screen) ||
                (allocation.PlanePointers is not null &&
                    !HasValidPlaneTable(allocation)))
            {
                return false;
            }

            var candidates = GetRanges(allocation);
            foreach (var candidate in candidates)
            {
                foreach (var existing in _ranges)
                {
                    if (Overlaps(candidate.Address, candidate.Bytes,
                                 existing.Address, existing.Bytes))
                    {
                        return false;
                    }
                }
            }

            _owned.Add(allocation.Screen, allocation);
            _ranges.AddRange(candidates);
            return true;
        }

        internal bool TryGet(uint screen, out ScreenPrefixAllocation allocation)
            => _owned.TryGetValue(screen, out allocation);

        internal bool Contains(uint screen)
            => screen != 0 && _owned.ContainsKey(screen);

        internal bool Remove(ScreenPrefixAllocation allocation)
        {
            if (!_owned.Remove(allocation.Screen))
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
        out ScreenPrefixAllocation allocation)
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

    internal static bool TryAllocateWithPlane(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        byte depth,
        ushort width,
        ushort height,
        bool clearPlane,
        bool requireDisplayDma,
        out ScreenPrefixAllocation allocation)
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
    /// Allocates one contiguous chip span for all standard-planar planes in
    /// the embedded Screen prefix. The span is released as one owner resource
    /// after exact topology and plane-table validation.
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
        out ScreenPrefixAllocation allocation)
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
    /// Allocates one classic Screen prefix around a caller/provider-owned
    /// standard-planar table. The embedded Screen structures are owned by the
    /// registry; the supplied plane spans remain entirely external.
    /// </summary>
    internal static bool TryAllocatePlanes(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        Registry registry,
        byte depth,
        ushort width,
        ushort height,
        IReadOnlyList<uint> planes,
        out ScreenPrefixAllocation allocation)
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
        out ScreenPrefixAllocation allocation)
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
                    screen: 0,
                    view: 0,
                    rasInfo: 0,
                    viewPort: 0,
                    rastPort: 0,
                    bitMap: 0)))
        {
            return false;
        }

        IReadOnlyList<uint>? callerPlanePointers = null;
        if (!ownPlane)
            callerPlanePointers = planePointers ?? new[] { plane0 };

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

        var allocated = new List<(uint Address, uint Bytes, GraphicsMemoryClass Class)>();
        var ownedPlane = 0u;
        var ownedPlaneBytes = 0u;

        if (ownPlane)
        {
            if (!TryGetPlaneBytes(width, height, depth, out ownedPlaneBytes) ||
                !allocator.TryAllocate(
                    ownedPlaneBytes,
                    GraphicsMemoryClass.Chip,
                    out ownedPlane))
            {
                if (ownedPlane != 0)
                    allocator.Free(ownedPlane, ownedPlaneBytes, GraphicsMemoryClass.Chip);
                return false;
            }

            if (ownedPlane == 0 || (ownedPlane & 1u) != 0 ||
                !TryProbeRange(memory, ownedPlane, ownedPlaneBytes) ||
                (requireDisplayDma &&
                 (memory is not IGraphicsDisplayMemory displayMemory ||
                  !displayMemory.IsDisplayDmaRange(ownedPlane, ownedPlaneBytes))) ||
                (clearPlane && !ClearRange(memory, ownedPlane, ownedPlaneBytes)))
            {
                if (ownedPlane != 0)
                    allocator.Free(ownedPlane, ownedPlaneBytes, GraphicsMemoryClass.Chip);
                return false;
            }

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
        else
        {
            ownedPlane = plane0;
        }

        bool TryEnvelope(uint bytes, GraphicsMemoryClass memoryClass, out uint address)
        {
            address = 0;
            var succeeded = allocator.TryAllocate(bytes, memoryClass, out address);
            if (!succeeded)
            {
                if (address != 0)
                    allocator.Free(address, bytes, memoryClass);
                address = 0;
                return false;
            }

            var candidateAddress = address;
            if (candidateAddress == 0 || (candidateAddress & 1u) != 0 ||
                !TryProbeRange(memory, candidateAddress, bytes) ||
                allocated.Exists(item =>
                    Overlaps(candidateAddress, bytes, item.Address, item.Bytes)) ||
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
                allocator.Free(candidateAddress, bytes, memoryClass);
                address = 0;
                return false;
            }

            allocated.Add((address, bytes, memoryClass));
            return true;
        }

        void ReleaseAllocated()
        {
            for (var index = allocated.Count - 1; index >= 0; index--)
            {
                var item = allocated[index];
                allocator.Free(item.Address, item.Bytes, item.Class);
            }

            allocated.Clear();
            if (ownPlane && ownedPlane != 0)
            {
                allocator.Free(ownedPlane, ownedPlaneBytes, GraphicsMemoryClass.Chip);
                ownedPlane = 0;
            }
        }

        if (!TryEnvelope((uint)GraphicsLayouts.ScreenSize,
                GraphicsMemoryClass.Public, out var screen) ||
            !TryEnvelope((uint)GraphicsLayouts.ViewSize,
                GraphicsMemoryClass.Public, out var view) ||
            !TryEnvelope((uint)GraphicsLayouts.RasInfoSize,
                GraphicsMemoryClass.Public, out var rasInfo) ||
            !TryGetEmbeddedAddresses(screen, out var viewPort, out var rastPort,
                out var bitMap))
        {
            ReleaseAllocated();
            return false;
        }

        var candidate = new ScreenPrefixAllocation(
            screen,
            view,
            viewPort,
            rasInfo,
            rastPort,
            bitMap,
            ownedPlane,
            depth,
            width,
            height,
            ownPlane,
            ownedPlaneBytes,
            publishedPlanes);

        if (!Initialize(memory, candidate) || !registry.Add(candidate))
        {
            _ = TeardownMemory(memory, candidate);
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
        uint screen)
    {
        if (screen == 0)
            return GraphicsRasterOperations.Success;
        if (!registry.TryGet(screen, out var allocation) ||
            !TeardownMemory(memory, allocation) ||
            !registry.Remove(allocation))
        {
            return GraphicsRasterOperations.Failure;
        }

        if (allocation.OwnsPlane)
        {
            allocator.Free(
                allocation.Plane0,
                allocation.PlaneBytes,
                GraphicsMemoryClass.Chip);
        }
        allocator.Free(
            allocation.RasInfo,
            (uint)GraphicsLayouts.RasInfoSize,
            GraphicsMemoryClass.Public);
        allocator.Free(
            allocation.View,
            (uint)GraphicsLayouts.ViewSize,
            GraphicsMemoryClass.Public);
        allocator.Free(
            allocation.Screen,
            (uint)GraphicsLayouts.ScreenSize,
            GraphicsMemoryClass.Public);
        return GraphicsRasterOperations.Success;
    }

    private static bool Initialize(
        IGraphicsMemory memory,
        ScreenPrefixAllocation allocation)
    {
        if (!TrySnapshot(memory, allocation.Screen,
                GraphicsLayouts.ScreenSize, out var screenOriginal) ||
            !TrySnapshot(memory, allocation.View,
                GraphicsLayouts.ViewSize, out var viewOriginal) ||
            !TrySnapshot(memory, allocation.RasInfo,
                GraphicsLayouts.RasInfoSize, out var rasInfoOriginal) ||
            !TrySnapshot(memory, allocation.BitMap,
                GraphicsLayouts.BitMapSize, out var bitMapOriginal))
        {
            return false;
        }

        var initialized = ClearRange(memory, allocation.Screen,
                (uint)GraphicsLayouts.ScreenSize) &&
            GraphicsRasterOperations.InitializeView(memory, allocation.View) &&
            GraphicsRasterOperations.InitializeViewPort(memory, allocation.ViewPort) &&
            GraphicsRasterOperations.InitializeRasInfo(memory, allocation.RasInfo) &&
            GraphicsRasterOperations.InitializeBitMap(
                memory,
                allocation.BitMap,
                allocation.Depth,
                allocation.Width,
                allocation.Height) &&
            GraphicsRasterOperations.InitializeRastPort(memory, allocation.RastPort) &&
            memory.TryWriteWord(
                allocation.Screen + (uint)GraphicsLayouts.ScreenWidth,
                allocation.Width) &&
            memory.TryWriteWord(
                allocation.Screen + (uint)GraphicsLayouts.ScreenHeight,
                allocation.Height) &&
            memory.TryWriteWord(
                allocation.ViewPort + (uint)GraphicsLayouts.ViewPortDWidth,
                allocation.Width) &&
            memory.TryWriteWord(
                allocation.ViewPort + (uint)GraphicsLayouts.ViewPortDHeight,
                allocation.Height) &&
            memory.TryWriteLong(
                allocation.RasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                allocation.BitMap) &&
            memory.TryWriteLong(
                allocation.ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                allocation.RasInfo) &&
            memory.TryWriteLong(
                allocation.View + (uint)GraphicsLayouts.ViewViewPort,
                allocation.ViewPort) &&
            memory.TryWriteLong(
                allocation.RastPort + (uint)GraphicsLayouts.RastPortBitMap,
                allocation.BitMap);
        if (!initialized)
        {
            Restore(memory, allocation.Screen, screenOriginal);
            Restore(memory, allocation.View, viewOriginal);
            Restore(memory, allocation.RasInfo, rasInfoOriginal);
            Restore(memory, allocation.BitMap, bitMapOriginal);
            return false;
        }

        if (allocation.PlanePointers is null)
        {
            if (!memory.TryWriteLong(
                    allocation.BitMap + (uint)GraphicsLayouts.BitMapPlanes,
                    allocation.Plane0))
            {
                Restore(memory, allocation.Screen, screenOriginal);
                Restore(memory, allocation.View, viewOriginal);
                Restore(memory, allocation.RasInfo, rasInfoOriginal);
                Restore(memory, allocation.BitMap, bitMapOriginal);
                return false;
            }
        }
        else
        {
            for (var plane = 0; plane < allocation.PlanePointers.Count; plane++)
            {
                if (!memory.TryWriteLong(
                        allocation.BitMap + (uint)GraphicsLayouts.BitMapPlanes +
                        ((uint)plane * sizeof(uint)),
                        allocation.PlanePointers[plane]))
                {
                    Restore(memory, allocation.Screen, screenOriginal);
                    Restore(memory, allocation.View, viewOriginal);
                    Restore(memory, allocation.RasInfo, rasInfoOriginal);
                    Restore(memory, allocation.BitMap, bitMapOriginal);
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TeardownMemory(
        IGraphicsMemory memory,
        ScreenPrefixAllocation allocation)
    {
        if (!TryValidateExactTopology(memory, allocation) ||
            !TrySnapshot(memory, allocation.Screen,
                GraphicsLayouts.ScreenSize, out var screenOriginal) ||
            !TrySnapshot(memory, allocation.View,
                GraphicsLayouts.ViewSize, out var viewOriginal) ||
            !TrySnapshot(memory, allocation.RasInfo,
                GraphicsLayouts.RasInfoSize, out var rasInfoOriginal))
        {
            return false;
        }

        if (!ClearRange(memory, allocation.Screen,
                (uint)GraphicsLayouts.ScreenSize) ||
            !ClearRange(memory, allocation.View,
                (uint)GraphicsLayouts.ViewSize) ||
            !ClearRange(memory, allocation.RasInfo,
                (uint)GraphicsLayouts.RasInfoSize))
        {
            Restore(memory, allocation.Screen, screenOriginal);
            Restore(memory, allocation.View, viewOriginal);
            Restore(memory, allocation.RasInfo, rasInfoOriginal);
            return false;
        }

        return true;
    }

    private static bool TryValidateExactTopology(
        IGraphicsMemory memory,
        ScreenPrefixAllocation allocation)
    {
        if (!TryGetEmbeddedAddresses(allocation.Screen,
                out var viewPort, out var rastPort, out var bitMap) ||
            viewPort != allocation.ViewPort ||
            rastPort != allocation.RastPort ||
            bitMap != allocation.BitMap ||
            !TryProbeRange(memory, allocation.Screen,
                (uint)GraphicsLayouts.ScreenSize) ||
            !TryProbeRange(memory, allocation.View,
                (uint)GraphicsLayouts.ViewSize) ||
            !TryProbeRange(memory, allocation.RasInfo,
                (uint)GraphicsLayouts.RasInfoSize) ||
            !memory.TryReadLong(
                allocation.View + (uint)GraphicsLayouts.ViewViewPort,
                out var linkedViewPort) ||
            linkedViewPort != allocation.ViewPort ||
            !memory.TryReadLong(
                allocation.ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo,
                out var linkedRasInfo) ||
            linkedRasInfo != allocation.RasInfo ||
            !memory.TryReadLong(
                allocation.RasInfo + (uint)GraphicsLayouts.RasInfoBitMap,
                out var linkedBitMap) ||
            linkedBitMap != allocation.BitMap ||
            !memory.TryReadLong(
                allocation.RastPort + (uint)GraphicsLayouts.RastPortBitMap,
                out var rastPortBitMap) ||
            rastPortBitMap != allocation.BitMap ||
            !TryValidatePublishedPlanes(memory, allocation))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetEmbeddedAddresses(
        uint screen,
        out uint viewPort,
        out uint rastPort,
        out uint bitMap)
    {
        viewPort = rastPort = bitMap = 0;
        if (screen == 0 || (screen & 1u) != 0 ||
            !TryAddress(screen, GraphicsLayouts.ScreenViewPort,
                GraphicsLayouts.ViewPortSize, out viewPort) ||
            !TryAddress(screen, GraphicsLayouts.ScreenRastPort,
                GraphicsLayouts.RastPortSize, out rastPort) ||
            !TryAddress(screen, GraphicsLayouts.ScreenBitMap,
                GraphicsLayouts.BitMapSize, out bitMap))
        {
            return false;
        }

        return true;
    }

    private static bool HasValidPlaneTable(ScreenPrefixAllocation allocation)
        => allocation.PlanePointers is not null &&
           TryValidatePlanePointers(
               allocation.PlanePointers,
               allocation.Depth,
               allocation.Screen,
               allocation.View,
               allocation.RasInfo,
               allocation.ViewPort,
               allocation.RastPort,
               allocation.BitMap);

    private static bool TryValidatePlanePointers(
        IReadOnlyList<uint> planes,
        byte depth,
        uint screen,
        uint view,
        uint rasInfo,
        uint viewPort,
        uint rastPort,
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
                plane == screen || plane == view || plane == rasInfo ||
                plane == viewPort || plane == rastPort || plane == bitMap)
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

    private static bool TryValidatePublishedPlanes(
        IGraphicsMemory memory,
        ScreenPrefixAllocation allocation)
    {
        if (allocation.PlanePointers is null)
        {
            return memory.TryReadLong(
                    allocation.BitMap + (uint)GraphicsLayouts.BitMapPlanes,
                    out var linkedPlane) &&
                linkedPlane == allocation.Plane0;
        }

        for (var plane = 0; plane < allocation.PlanePointers.Count; plane++)
        {
            if (!memory.TryReadLong(
                    allocation.BitMap + (uint)GraphicsLayouts.BitMapPlanes +
                    ((uint)plane * sizeof(uint)),
                    out var linkedPlane) ||
                linkedPlane != allocation.PlanePointers[plane])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAddress(
        uint baseAddress,
        int offset,
        int bytes,
        out uint address)
    {
        address = 0;
        if (offset < 0 || bytes <= 0 ||
            baseAddress > uint.MaxValue - (uint)offset)
        {
            return false;
        }

        address = baseAddress + (uint)offset;
        return address <= uint.MaxValue - (uint)(bytes - 1);
    }

    private static List<(uint Address, uint Bytes)> GetRanges(
        ScreenPrefixAllocation allocation)
    {
        var ranges = new List<(uint Address, uint Bytes)>
        {
            (allocation.Screen, (uint)GraphicsLayouts.ScreenSize),
            (allocation.View, (uint)GraphicsLayouts.ViewSize),
            (allocation.RasInfo, (uint)GraphicsLayouts.RasInfoSize)
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

        // The prefix publishes links only; it never clears or copies caller
        // plane bytes. Distinct links may intentionally share storage, while
        // exact duplicate links remain malformed and are rejected by
        // TryValidatePlanePointers. Validate span safety here without
        // imposing ownership semantics on the caller's raster contents.
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

    private static bool Overlaps(uint leftAddress, uint leftBytes,
        uint rightAddress, uint rightBytes)
    {
        if (leftBytes == 0 || rightBytes == 0)
            return false;
        var leftEnd = (ulong)leftAddress + leftBytes;
        var rightEnd = (ulong)rightAddress + rightBytes;
        return (ulong)leftAddress < rightEnd &&
               (ulong)rightAddress < leftEnd;
    }

    private static bool TryGetGuestSpan(uint address, uint bytes)
        => address != 0 && bytes != 0 &&
           address <= uint.MaxValue - (bytes - 1u);

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

    private static bool TryProbeRange(
        IGraphicsMemory memory,
        uint address,
        uint bytes)
    {
        if (address == 0 || bytes == 0 ||
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

    private static bool ClearRange(
        IGraphicsMemory memory,
        uint address,
        uint bytes)
    {
        if (!TryProbeRange(memory, address, bytes))
            return false;
        for (var offset = 0u; offset < bytes; offset++)
        {
            if (!memory.TryWriteByte(address + offset, 0))
                return false;
        }
        return true;
    }

    private static bool TrySnapshot(
        IGraphicsMemory memory,
        uint address,
        int bytes,
        out byte[] snapshot)
    {
        snapshot = Array.Empty<byte>();
        if (bytes <= 0 || !TryProbeRange(memory, address, (uint)bytes))
            return false;
        snapshot = new byte[bytes];
        for (var offset = 0; offset < bytes; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out snapshot[offset]))
                return false;
        }
        return true;
    }

    private static void Restore(
        IGraphicsMemory memory,
        uint address,
        byte[] snapshot)
    {
        for (var offset = 0u; offset < snapshot.Length; offset++)
            _ = memory.TryWriteByte(address + offset, snapshot[offset]);
    }
}
