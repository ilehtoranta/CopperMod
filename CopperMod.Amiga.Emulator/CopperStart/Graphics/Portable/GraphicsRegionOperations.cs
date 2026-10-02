using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CopperStart.Graphics.Portable;

/// <summary>
/// Guest-memory implementation of the classic graphics/regions.h API.
///
/// The public Region and RegionRectangle layouts are preserved exactly.  The
/// boolean engine uses coordinate compression rather than host-side pixels, so
/// a region remains a linked list of guest rectangles and can be consumed by a
/// native 68k layer implementation later without a translation object.  The
/// public RegionRectangle bounds remain relative to Region.Bounds; translation
/// is confined to the boolean-engine boundary.
/// </summary>
internal static class GraphicsRegionOperations
{
    /// <summary>
    /// Tracks Region structures returned by NewRegion for this graphics
    /// instance. Region contents may be edited through the public boolean
    /// vectors, but DisposeRegion must not reclaim a foreign layer/provider
    /// object or a stale pointer.
    /// </summary>
    internal sealed class Registry
    {
        private readonly HashSet<uint> _allocated = new();

        internal void Register(uint region)
        {
            if (region != 0)
                _allocated.Add(region);
        }

        internal bool Contains(uint region)
            => region != 0 && _allocated.Contains(region);

        internal void Remove(uint region)
            => _allocated.Remove(region);
    }

    internal const int Success = 0;
    internal const int Failure = -1;

    private const int MaxRectangles = 4096;
    private const long MaxCompressedCells = 1_000_000;

    internal static uint NewRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator)
    {
        var allocationSucceeded = allocator.TryAllocate(
            (uint)GraphicsLayouts.RegionSize,
            GraphicsMemoryClass.Public,
            out var region);
        if (!allocationSucceeded || !IsEvenAddress(region))
        {
            if (region != 0)
                allocator.Free(
                    region,
                    (uint)GraphicsLayouts.RegionSize,
                    GraphicsMemoryClass.Public);
            return 0;
        }

        // A public Region allocation may alias an already-backed guest span.
        // Snapshot it before the empty header is published so a late write
        // failure cannot leak a partially initialized Region across the
        // native/provider boundary or into a retry after the provisional span
        // is released.
        if (!TrySnapshotRange(
                memory,
                region,
                GraphicsLayouts.RegionSize,
                out var original) ||
            !WriteRegionHeader(memory, region, 0, Array.Empty<RegionRect>()))
        {
            if (original is not null)
                RestoreRegionHeader(memory, region, original);
            allocator.Free(region, (uint)GraphicsLayouts.RegionSize, GraphicsMemoryClass.Public);
            return 0;
        }

        return region;
    }

    internal static int DisposeRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region)
    {
        if (region == 0 || !TryReadRegion(memory, region, out _, out var nodes))
            return Failure;

        foreach (var node in nodes)
        {
            allocator.Free(
                node,
                (uint)GraphicsLayouts.RegionRectangleSize,
                GraphicsMemoryClass.Public);
        }

        allocator.Free(region, (uint)GraphicsLayouts.RegionSize, GraphicsMemoryClass.Public);
        return Success;
    }

    internal static bool OrRectRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region,
        uint rectangle)
        => ModifyWithRectangle(
            memory,
            allocator,
            region,
            rectangle,
            RegionBoolean.Or);

    internal static bool AndRectRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region,
        uint rectangle)
        => ModifyWithRectangle(
            memory,
            allocator,
            region,
            rectangle,
            RegionBoolean.And);

    internal static bool XorRectRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region,
        uint rectangle)
        => ModifyWithRectangle(
            memory,
            allocator,
            region,
            rectangle,
            RegionBoolean.Xor);

    internal static bool ClearRectRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region,
        uint rectangle)
        => ModifyWithRectangle(
            memory,
            allocator,
            region,
            rectangle,
            RegionBoolean.Clear);

    internal static bool ClearRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region)
    {
        if (region == 0 || !TryReadRegion(memory, region, out _, out var nodes))
            return false;

        if (!TrySnapshotRegionHeader(memory, region, out var original))
            return false;

        if (!WriteRegionHeader(memory, region, 0, Array.Empty<RegionRect>()))
        {
            RestoreRegionHeader(memory, region, original);
            return false;
        }

        FreeNodes(allocator, nodes);
        return true;
    }

    internal static bool OrRegionRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint sourceRegion,
        uint destinationRegion)
        => ModifyWithRegion(
            memory,
            allocator,
            sourceRegion,
            destinationRegion,
            RegionBoolean.Or);

    internal static bool AndRegionRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint sourceRegion,
        uint destinationRegion)
        => ModifyWithRegion(
            memory,
            allocator,
            sourceRegion,
            destinationRegion,
            RegionBoolean.And);

    internal static bool XorRegionRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint sourceRegion,
        uint destinationRegion)
        => ModifyWithRegion(
            memory,
            allocator,
            sourceRegion,
            destinationRegion,
            RegionBoolean.Xor);

    private static bool ModifyWithRectangle(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region,
        uint rectangle,
        RegionBoolean operation)
    {
        if (!TryReadRectangleForRegion(memory, rectangle, out var input, out var evil) ||
            !TryReadRegion(memory, region, out var current, out var oldNodes))
        {
            return false;
        }

        if (evil)
        {
            return operation switch
            {
                RegionBoolean.And => ClearRegion(memory, allocator, region),
                RegionBoolean.Or or RegionBoolean.Xor or RegionBoolean.Clear => true,
                _ => false
            };
        }

        if (operation == RegionBoolean.And)
        {
            // Intersecting each canonical region rectangle with one input
            // rectangle can only preserve or remove nodes.  Kickstart's
            // AndRectRegion is the one rectangle-region operation that does
            // not allocate and therefore cannot fail for lack of memory.
            return IntersectRegionWithRectangle(
                memory,
                allocator,
                region,
                input,
                current,
                oldNodes);
        }

        var other = new[] { input };
        return TryBuildBoolean(current, other, operation, out var result) &&
               PublishRegion(memory, allocator, region, oldNodes, result);
    }

    private static bool IntersectRegionWithRectangle(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region,
        RegionRect input,
        IReadOnlyList<RegionRect> current,
        IReadOnlyList<uint> oldNodes)
    {
        var result = new List<RegionRect>(current.Count);
        foreach (var rectangle in current)
        {
            var clipped = new RegionRect(
                Math.Max(rectangle.MinX, input.MinX),
                Math.Max(rectangle.MinY, input.MinY),
                Math.Min(rectangle.MaxX, input.MaxX),
                Math.Min(rectangle.MaxY, input.MaxY));
            if (clipped.IsValid)
                result.Add(clipped);
        }

        if (!TrySnapshotRegionHeader(memory, region, out var originalHeader))
            return false;

        var originalNodes = new List<byte[]>(oldNodes.Count);
        foreach (var node in oldNodes)
        {
            if (!TrySnapshotRange(
                    memory,
                    node,
                    GraphicsLayouts.RegionRectangleSize,
                    out var originalNode))
            {
                return false;
            }

            originalNodes.Add(originalNode);
        }

        var bounds = GetBounds(result);
        for (var index = 0; index < result.Count; index++)
        {
            var next = index + 1 < result.Count ? oldNodes[index + 1] : 0u;
            var previous = index == 0
                ? region + (uint)GraphicsLayouts.RegionRectangle
                : oldNodes[index - 1];
            if (!WriteRegionRectangle(
                    memory,
                    oldNodes[index],
                    result[index],
                    bounds,
                    next,
                    previous))
            {
                RestoreRegionHeader(memory, region, originalHeader);
                RestoreRegionNodes(memory, oldNodes, originalNodes);
                return false;
            }
        }

        if (!WriteRegionHeader(
                memory,
                region,
                result.Count == 0 ? 0u : oldNodes[0],
                result))
        {
            RestoreRegionHeader(memory, region, originalHeader);
            RestoreRegionNodes(memory, oldNodes, originalNodes);
            return false;
        }

        for (var index = result.Count; index < oldNodes.Count; index++)
        {
            allocator.Free(
                oldNodes[index],
                (uint)GraphicsLayouts.RegionRectangleSize,
                GraphicsMemoryClass.Public);
        }

        return true;
    }

    private static bool ModifyWithRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint sourceRegion,
        uint destinationRegion,
        RegionBoolean operation)
    {
        if (!TryReadRegion(memory, sourceRegion, out var source, out _) ||
            !TryReadRegion(memory, destinationRegion, out var destination, out var oldNodes))
        {
            return false;
        }

        return TryBuildBoolean(destination, source, operation, out var result) &&
               PublishRegion(memory, allocator, destinationRegion, oldNodes, result);
    }

    private static bool PublishRegion(
        IGraphicsMemory memory,
        IGraphicsAllocatorBackend allocator,
        uint region,
        List<uint> oldNodes,
        List<RegionRect> rectangles)
    {
        if (!TrySnapshotRegionHeader(memory, region, out var original))
            return false;

        var newNodes = new List<uint>(rectangles.Count);
        foreach (var rectangle in rectangles)
        {
            var allocationSucceeded = allocator.TryAllocate(
                (uint)GraphicsLayouts.RegionRectangleSize,
                GraphicsMemoryClass.Public,
                out var node);
            if (!allocationSucceeded || !IsEvenAddress(node))
            {
                if (node != 0)
                    allocator.Free(
                        node,
                        (uint)GraphicsLayouts.RegionRectangleSize,
                        GraphicsMemoryClass.Public);
                FreeNodes(allocator, newNodes);
                RestoreRegionHeader(memory, region, original);
                return false;
            }

            newNodes.Add(node);
        }

        for (var index = 0; index < rectangles.Count; index++)
        {
            var next = index + 1 < newNodes.Count ? newNodes[index + 1] : 0u;
            var previous = index == 0
                ? region + (uint)GraphicsLayouts.RegionRectangle
                : newNodes[index - 1];
            if (!WriteRegionRectangle(
                    memory,
                    newNodes[index],
                    rectangles[index],
                    GetBounds(rectangles),
                    next,
                    previous))
            {
                FreeNodes(allocator, newNodes);
                RestoreRegionHeader(memory, region, original);
                return false;
            }
        }

        if (!WriteRegionHeader(
                memory,
                region,
                newNodes.Count == 0 ? 0u : newNodes[0],
                rectangles))
        {
            FreeNodes(allocator, newNodes);
            RestoreRegionHeader(memory, region, original);
            return false;
        }

        FreeNodes(allocator, oldNodes);
        return true;
    }

    private static bool TryReadRegion(
        IGraphicsMemory memory,
        uint region,
        out List<RegionRect> rectangles,
        out List<uint> nodes)
    {
        rectangles = new List<RegionRect>();
        nodes = new List<uint>();
        if (region == 0 || (region & 1u) != 0 ||
            !TryProbe(memory, region, GraphicsLayouts.RegionSize) ||
            !TryReadRegionBounds(memory, region, out var regionBounds) ||
            !memory.TryReadLong(
                region + (uint)GraphicsLayouts.RegionRectangle,
                out var node))
        {
            return false;
        }

        var visited = new HashSet<uint>();
        var expectedPrevious = region + (uint)GraphicsLayouts.RegionRectangle;
        while (node != 0)
        {
            if (nodes.Count >= MaxRectangles ||
                !visited.Add(node) ||
                (node & 1u) != 0 ||
                !TryProbe(memory, node, GraphicsLayouts.RegionRectangleSize) ||
                !TryReadRectangle(
                    memory,
                    node + (uint)GraphicsLayouts.RegionRectangleBounds,
                    out var relativeRectangle) ||
                !memory.TryReadLong(
                    node + (uint)GraphicsLayouts.RegionRectangleNext,
                    out var next) ||
                !memory.TryReadLong(
                    node + (uint)GraphicsLayouts.RegionRectanglePrevious,
                    out var previous) ||
                previous != expectedPrevious)
            {
                rectangles.Clear();
                nodes.Clear();
                return false;
            }

            if (!TryTranslateFromRegionBounds(
                    relativeRectangle,
                    regionBounds,
                    out var rectangle))
            {
                rectangles.Clear();
                nodes.Clear();
                return false;
            }

            rectangles.Add(rectangle);
            nodes.Add(node);
            expectedPrevious = node;
            node = next;
        }

        return true;
    }

    private static bool TryReadRectangle(
        IGraphicsMemory memory,
        uint address,
        out RegionRect rectangle)
    {
        rectangle = default;
        if (address == 0 ||
            !TryProbe(memory, address, GraphicsLayouts.RectangleSize) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMinX, out var minX) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMinY, out var minY) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMaxX, out var maxX) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMaxY, out var maxY))
        {
            return false;
        }

        var candidate = new RegionRect(
            unchecked((short)minX),
            unchecked((short)minY),
            unchecked((short)maxX),
            unchecked((short)maxY));
        if (!candidate.IsValid)
            return false;

        rectangle = candidate;
        return true;
    }

    private static bool TryReadRectangleForRegion(
        IGraphicsMemory memory,
        uint address,
        out RegionRect rectangle,
        out bool evil)
    {
        rectangle = default;
        evil = false;
        if (address == 0 ||
            !TryProbe(memory, address, GraphicsLayouts.RectangleSize) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMinX, out var minX) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMinY, out var minY) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMaxX, out var maxX) ||
            !memory.TryReadWord(address + (uint)GraphicsLayouts.RectangleMaxY, out var maxY))
        {
            return false;
        }

        rectangle = new RegionRect(
            unchecked((short)minX),
            unchecked((short)minY),
            unchecked((short)maxX),
            unchecked((short)maxY));
        evil = !rectangle.IsValid;
        return true;
    }

    private static bool TryBuildBoolean(
        IReadOnlyList<RegionRect> first,
        IReadOnlyList<RegionRect> second,
        RegionBoolean operation,
        out List<RegionRect> result)
    {
        result = new List<RegionRect>();
        var xCoordinates = new SortedSet<int>();
        var yCoordinates = new SortedSet<int>();
        AddCoordinates(first, xCoordinates, yCoordinates);
        AddCoordinates(second, xCoordinates, yCoordinates);

        if (xCoordinates.Count < 2 || yCoordinates.Count < 2)
            return true;

        var xs = new List<int>(xCoordinates);
        var ys = new List<int>(yCoordinates);
        var width = xs.Count - 1;
        var height = ys.Count - 1;
        if ((long)width * height > MaxCompressedCells)
            return false;

        var cells = new bool[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            var sampleY = ys[y];
            for (var x = 0; x < width; x++)
            {
                var sampleX = xs[x];
                var left = Contains(first, sampleX, sampleY);
                var right = Contains(second, sampleX, sampleY);
                cells[y * width + x] = operation switch
                {
                    RegionBoolean.Or => left || right,
                    RegionBoolean.And => left && right,
                    RegionBoolean.Xor => left ^ right,
                    RegionBoolean.Clear => left && !right,
                    _ => false
                };
            }
        }

        var active = new Dictionary<(int MinX, int MaxX), ActiveBand>();
        for (var y = 0; y < height; y++)
        {
            var rowIntervals = new List<(int MinX, int MaxX)>();
            var x = 0;
            while (x < width)
            {
                while (x < width && !cells[y * width + x])
                    x++;

                if (x == width)
                    break;

                var firstX = x;
                while (x < width && cells[y * width + x])
                    x++;

                rowIntervals.Add((xs[firstX], xs[x]));
            }

            var currentKeys = new HashSet<(int MinX, int MaxX)>(rowIntervals);
            var staleKeys = new List<(int MinX, int MaxX)>();
            foreach (var pair in active)
            {
                if (!currentKeys.Contains(pair.Key))
                {
                    result.Add(pair.Value.ToRectangle());
                    if (result.Count > MaxRectangles)
                        return false;

                    staleKeys.Add(pair.Key);
                }
            }

            foreach (var staleKey in staleKeys)
                active.Remove(staleKey);

            foreach (var interval in rowIntervals)
            {
                if (active.TryGetValue(interval, out var band))
                {
                    band.MaxYExclusive = ys[y + 1];
                }
                else
                {
                    active.Add(
                        interval,
                        new ActiveBand(interval.MinX, interval.MaxX, ys[y], ys[y + 1]));
                }
            }
        }

        foreach (var band in active.Values)
        {
            result.Add(band.ToRectangle());
            if (result.Count > MaxRectangles)
                return false;
        }

        result.Sort(static (left, right) =>
        {
            var compare = left.MinY.CompareTo(right.MinY);
            return compare != 0 ? compare : left.MinX.CompareTo(right.MinX);
        });
        return true;
    }

    private static void AddCoordinates(
        IReadOnlyList<RegionRect> rectangles,
        SortedSet<int> xCoordinates,
        SortedSet<int> yCoordinates)
    {
        foreach (var rectangle in rectangles)
        {
            xCoordinates.Add(rectangle.MinX);
            xCoordinates.Add(rectangle.MaxX + 1);
            yCoordinates.Add(rectangle.MinY);
            yCoordinates.Add(rectangle.MaxY + 1);
        }
    }

    private static bool Contains(
        IReadOnlyList<RegionRect> rectangles,
        int x,
        int y)
    {
        foreach (var rectangle in rectangles)
        {
            if (x >= rectangle.MinX && x <= rectangle.MaxX &&
                y >= rectangle.MinY && y <= rectangle.MaxY)
            {
                return true;
            }
        }

        return false;
    }

    private static bool WriteRegionHeader(
        IGraphicsMemory memory,
        uint region,
        uint firstRectangle,
        IReadOnlyList<RegionRect> rectangles)
    {
        var bounds = GetBounds(rectangles);
        return WriteRectangle(memory, region + (uint)GraphicsLayouts.RegionBounds, bounds) &&
               memory.TryWriteLong(
                   region + (uint)GraphicsLayouts.RegionRectangle,
                   firstRectangle);
    }

    private static bool TrySnapshotRegionHeader(
        IGraphicsMemory memory,
        uint region,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (region == 0 ||
            region > uint.MaxValue - (uint)(GraphicsLayouts.RegionSize - 1))
            return false;

        original = new byte[GraphicsLayouts.RegionSize];
        for (var offset = 0; offset < original.Length; offset++)
        {
            if (!memory.TryReadByte(region + (uint)offset, out original[offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static bool TrySnapshotRange(
        IGraphicsMemory memory,
        uint address,
        int byteCount,
        out byte[] original)
    {
        original = Array.Empty<byte>();
        if (!TryProbe(memory, address, byteCount))
            return false;

        original = new byte[byteCount];
        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out original[offset]))
            {
                original = Array.Empty<byte>();
                return false;
            }
        }

        return true;
    }

    private static void RestoreRegionHeader(
        IGraphicsMemory memory,
        uint region,
        IReadOnlyList<byte> original)
    {
        for (var offset = 0; offset < original.Count; offset++)
            _ = memory.TryWriteByte(region + (uint)offset, original[offset]);
    }

    private static void RestoreRegionNodes(
        IGraphicsMemory memory,
        IReadOnlyList<uint> nodes,
        IReadOnlyList<byte[]> originals)
    {
        for (var index = 0; index < nodes.Count && index < originals.Count; index++)
        {
            var original = originals[index];
            for (var offset = 0; offset < original.Length; offset++)
                _ = memory.TryWriteByte(nodes[index] + (uint)offset, original[offset]);
        }
    }

    private static bool WriteRegionRectangle(
        IGraphicsMemory memory,
        uint address,
        RegionRect rectangle,
        RegionRect regionBounds,
        uint next,
        uint previous)
    {
        if (!TryTranslateToRegionBounds(rectangle, regionBounds, out var relativeRectangle))
            return false;

        return WriteRectangle(
                   memory,
                   address + (uint)GraphicsLayouts.RegionRectangleBounds,
                   relativeRectangle) &&
           memory.TryWriteLong(
               address + (uint)GraphicsLayouts.RegionRectangleNext,
               next) &&
           memory.TryWriteLong(
               address + (uint)GraphicsLayouts.RegionRectanglePrevious,
               previous);
    }

    private static bool TryReadRegionBounds(
        IGraphicsMemory memory,
        uint region,
        out RegionRect bounds)
    {
        bounds = default;
        if (region == 0 ||
            !TryProbe(memory, region + (uint)GraphicsLayouts.RegionBounds, GraphicsLayouts.RectangleSize) ||
            !memory.TryReadWord(
                region + (uint)GraphicsLayouts.RegionBounds + (uint)GraphicsLayouts.RectangleMinX,
                out var minX) ||
            !memory.TryReadWord(
                region + (uint)GraphicsLayouts.RegionBounds + (uint)GraphicsLayouts.RectangleMinY,
                out var minY) ||
            !memory.TryReadWord(
                region + (uint)GraphicsLayouts.RegionBounds + (uint)GraphicsLayouts.RectangleMaxX,
                out var maxX) ||
            !memory.TryReadWord(
                region + (uint)GraphicsLayouts.RegionBounds + (uint)GraphicsLayouts.RectangleMaxY,
                out var maxY))
        {
            return false;
        }

        var candidate = new RegionRect(
            unchecked((short)minX),
            unchecked((short)minY),
            unchecked((short)maxX),
            unchecked((short)maxY));
        if (!candidate.IsValid && !candidate.IsEmpty)
            return false;

        bounds = candidate;
        return true;
    }

    private static bool TryTranslateFromRegionBounds(
        RegionRect relativeRectangle,
        RegionRect regionBounds,
        out RegionRect absoluteRectangle)
    {
        absoluteRectangle = default;
        if (!relativeRectangle.IsValid || !regionBounds.IsValid)
            return false;

        var minX = (long)regionBounds.MinX + relativeRectangle.MinX;
        var minY = (long)regionBounds.MinY + relativeRectangle.MinY;
        var maxX = (long)regionBounds.MinX + relativeRectangle.MaxX;
        var maxY = (long)regionBounds.MinY + relativeRectangle.MaxY;
        if (minX < short.MinValue || minX > short.MaxValue ||
            minY < short.MinValue || minY > short.MaxValue ||
            maxX < short.MinValue || maxX > short.MaxValue ||
            maxY < short.MinValue || maxY > short.MaxValue)
        {
            return false;
        }

        absoluteRectangle = new RegionRect(
            (int)minX,
            (int)minY,
            (int)maxX,
            (int)maxY);
        return true;
    }

    private static bool TryTranslateToRegionBounds(
        RegionRect absoluteRectangle,
        RegionRect regionBounds,
        out RegionRect relativeRectangle)
    {
        relativeRectangle = default;
        if (!absoluteRectangle.IsValid || !regionBounds.IsValid)
            return false;

        var minX = (long)absoluteRectangle.MinX - regionBounds.MinX;
        var minY = (long)absoluteRectangle.MinY - regionBounds.MinY;
        var maxX = (long)absoluteRectangle.MaxX - regionBounds.MinX;
        var maxY = (long)absoluteRectangle.MaxY - regionBounds.MinY;
        if (minX < short.MinValue || minX > short.MaxValue ||
            minY < short.MinValue || minY > short.MaxValue ||
            maxX < short.MinValue || maxX > short.MaxValue ||
            maxY < short.MinValue || maxY > short.MaxValue)
        {
            return false;
        }

        relativeRectangle = new RegionRect(
            (int)minX,
            (int)minY,
            (int)maxX,
            (int)maxY);
        return true;
    }

    private static bool WriteRectangle(
        IGraphicsMemory memory,
        uint address,
        RegionRect rectangle)
        => memory.TryWriteWord(
               address + (uint)GraphicsLayouts.RectangleMinX,
               unchecked((ushort)(short)rectangle.MinX)) &&
           memory.TryWriteWord(
               address + (uint)GraphicsLayouts.RectangleMinY,
               unchecked((ushort)(short)rectangle.MinY)) &&
           memory.TryWriteWord(
               address + (uint)GraphicsLayouts.RectangleMaxX,
               unchecked((ushort)(short)rectangle.MaxX)) &&
           memory.TryWriteWord(
               address + (uint)GraphicsLayouts.RectangleMaxY,
               unchecked((ushort)(short)rectangle.MaxY));

    private static RegionRect GetBounds(IReadOnlyList<RegionRect> rectangles)
    {
        if (rectangles.Count == 0)
            return new RegionRect(0, 0, -1, -1);

        var minX = rectangles[0].MinX;
        var minY = rectangles[0].MinY;
        var maxX = rectangles[0].MaxX;
        var maxY = rectangles[0].MaxY;
        for (var index = 1; index < rectangles.Count; index++)
        {
            var rectangle = rectangles[index];
            minX = Math.Min(minX, rectangle.MinX);
            minY = Math.Min(minY, rectangle.MinY);
            maxX = Math.Max(maxX, rectangle.MaxX);
            maxY = Math.Max(maxY, rectangle.MaxY);
        }

        return new RegionRect(minX, minY, maxX, maxY);
    }

    private static void FreeNodes(
        IGraphicsAllocatorBackend allocator,
        IReadOnlyList<uint> nodes)
    {
        foreach (var node in nodes)
        {
            allocator.Free(
                node,
                (uint)GraphicsLayouts.RegionRectangleSize,
                GraphicsMemoryClass.Public);
        }
    }

    private static bool TryProbe(
        IGraphicsMemory memory,
        uint address,
        int byteCount)
    {
        if (!IsEvenAddress(address) || byteCount <= 0 ||
            address > uint.MaxValue - (uint)(byteCount - 1))
        {
            return false;
        }

        for (var offset = 0; offset < byteCount; offset++)
        {
            if (!memory.TryReadByte(address + (uint)offset, out _))
                return false;
        }

        return true;
    }

    private static bool IsEvenAddress(uint address)
        => address != 0 && (address & 1u) == 0;

    private enum RegionBoolean
    {
        Or,
        And,
        Xor,
        Clear
    }

    private readonly struct RegionRect
    {
        internal RegionRect(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        internal int MinX { get; }
        internal int MinY { get; }
        internal int MaxX { get; }
        internal int MaxY { get; }
        internal bool IsValid => MinX <= MaxX && MinY <= MaxY;
        internal bool IsEmpty => MinX == 0 && MinY == 0 && MaxX == -1 && MaxY == -1;
    }

    private sealed class ActiveBand
    {
        internal ActiveBand(int minX, int maxX, int minY, int maxYExclusive)
        {
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxYExclusive = maxYExclusive;
        }

        internal int MinX { get; }
        internal int MaxX { get; }
        internal int MinY { get; }
        internal int MaxYExclusive { get; set; }

        internal RegionRect ToRectangle()
            => new(MinX, MinY, MaxX - 1, MaxYExclusive - 1);
    }
}
