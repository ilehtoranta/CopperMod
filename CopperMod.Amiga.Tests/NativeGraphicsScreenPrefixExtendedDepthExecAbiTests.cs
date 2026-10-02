using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsScreenPrefixExtendedDepthExecAbiTests
{
    // Activate only after the lower-depth ABI/default/extent, depth-five
    // rollback-size, and depth-six ownership/result-restoration repairs.
    private const uint HeapAddress = 0x00D0_0000;
    private const int ArenaSize = 0x2000;
    private const uint ResultArena = 0x00D1_0000;
    private const uint Result = ResultArena + 0x20;
    private const int ResultBytes = 48;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly uint[] MockAddresses = CreateMockAddresses();
    private static readonly (uint Size, uint Flags)[] AllRequests =
    {
        ((uint)GraphicsLayouts.ScreenSize, PublicClear), ((uint)GraphicsLayouts.ViewSize, PublicClear),
        ((uint)GraphicsLayouts.RasInfoSize, PublicClear), (PlaneBytes, ChipClear),
        (8u, PublicClear), (PlaneBytes, ChipClear), (12u, PublicClear), (PlaneBytes, ChipClear),
        (16u, PublicClear), (PlaneBytes, ChipClear), (20u, PublicClear), (PlaneBytes, ChipClear),
        (24u, PublicClear), (PlaneBytes, ChipClear), (28u, PublicClear), (PlaneBytes, ChipClear),
        (32u, PublicClear), (PlaneBytes, ChipClear)
    };
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(
            out var entries, out var fallback, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out var depthSeven, out var depthEight);
        return new NativeImage(code, entries, fallback, depthSeven, depthEight);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var depth in new[] { 7, 8 })
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "success", "table-null", "plane-null", "odd-plane", "invalid-depth", "prior-plane-null"
        })
            yield return new object[] { depth, relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void BothPrivateExtendedDepthBodiesUseActualExecAbiAndRestoreTheCompleteCallerRecord(
        int depth, bool relocated, string scenario)
    {
        var addresses = MockAddresses.ToArray();
        if (scenario == "odd-plane")
            addresses[2 * depth + 1]++;
        var bundle = new Bundle(addresses);
        var ownerDepth = FailureOwnerDepth(depth, scenario);
        var count = scenario switch
        {
            "invalid-depth" => 0,
            "table-null" => 2 * depth + 1,
            "prior-plane-null" => 2 * ownerDepth + 2,
            _ => 2 * depth + 2
        };
        var allocations = AllRequests.Take(count).Select((request, index) =>
        {
            var isNull = (scenario == "table-null" && index == 2 * depth)
                || (scenario == "plane-null" && index == 2 * depth + 1)
                || (scenario == "prior-plane-null" && index == 2 * ownerDepth + 1);
            return (Address: isNull ? 0u : addresses[index], request.Size, request.Flags);
        }).ToArray();
        Verify(depth, relocated, scenario, bundle, allocations, productionHeapSize: 0);
    }

    [Theory]
    [InlineData(7, false, false)]
    [InlineData(7, true, false)]
    [InlineData(7, false, true)]
    [InlineData(7, true, true)]
    [InlineData(8, false, false)]
    [InlineData(8, true, false)]
    [InlineData(8, false, true)]
    [InlineData(8, true, true)]
    public void BothPrivateExtendedDepthBodiesUseRealClassicExecForRetirementAndNaturalLastPlaneOom(
        int depth, bool relocated, bool exhaustHeap)
    {
        var heapSize = exhaustHeap ? (depth == 7 ? 2112 : 2400) : 4096;
        var scenario = exhaustHeap ? "plane-null" : "success";
        var requests = AllRequests.Take(2 * depth + 2).ToArray();
        var addresses = new uint[requests.Length];
        var acquired = 0u;
        for (var index = 0; index < requests.Length; index++)
        {
            if (exhaustHeap && index == 2 * depth + 1)
                continue;
            // Every obsolete-table hole is smaller than the next table's
            // rounded request, and all are smaller than a 256-byte plane.
            // Classic first-fit therefore continues at the remaining tail.
            addresses[index] = HeapAddress + 32u + acquired;
            acquired += Rounded(requests[index].Size);
        }
        Assert.Equal(exhaustHeap ? (depth == 7 ? 2048u : 2336u) : (depth == 7 ? 2304u : 2592u), acquired);
        if (exhaustHeap)
            Assert.Equal(32u, (uint)heapSize - 32u - acquired);
        var bundle = new Bundle(addresses);
        var allocations = requests.Select((request, index) => (Address: addresses[index], request.Size, request.Flags)).ToArray();
        Verify(depth, relocated, scenario, bundle, allocations, heapSize);
    }

    private static void Verify(int depth, bool relocated, string scenario, Bundle bundle,
        (uint Address, uint Size, uint Flags)[] allocations, int productionHeapSize)
    {
        var production = productionHeapSize != 0;
        var success = scenario == "success";
        var ownerDepth = FailureOwnerDepth(depth, scenario);
        var inheritedCount = scenario == "invalid-depth" ? 0 : ownerDepth - 3;
        var releases = Releases(bundle, depth, scenario);
        using var fixture = new Fixture(depth, relocated, scenario, productionHeapSize);
        var initial = fixture.CaptureMemory();
        var expected = initial;
        var initialFree = production ? (uint)productionHeapSize - 32u : 0;
        var chunks = production
            ? new List<FreeChunk> { new(HeapAddress + 32u, initialFree) }
            : new List<FreeChunk>();
        if (production)
        {
            Assert.Equal(HeapAddress + 32u, fixture.FirstFreeChunk);
            Assert.Equal(initialFree, fixture.FreeBytes);
        }
        var failures = new List<string>();
        CallResult? result = null;
        // A malformed real FreeMem can alert before returning. Capture that
        // failure and the original raw tuples; never normalize a native call
        // to keep production Exec running through a broken ownership path.
        Check(failures, "native execution", () => result = fixture.Construct(scenario == "invalid-depth" ? (uint)depth - 1 : (uint)depth));
        Check(failures, "private D0/PC/SP/A6 return and no provider fallback", () =>
        {
            var returned = Assert.IsType<CallResult>(result);
            Assert.Equal(success ? 1u : 0u, returned.Data0);
            Assert.Equal(Fixture.ExecBase, returned.Address6);
            Assert.Equal(Fixture.ReturnAddress, returned.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, returned.StackPointer);
            Assert.False(returned.HitFallback);
        });
        Check(failures, "AllocMem actual D0/D1 requests", () =>
            Assert.Equal(allocations.Select(item => (item.Size, item.Flags)), fixture.RawRequests));
        Check(failures, "unmodified allocator results", () => Assert.Equal(allocations, fixture.Allocations));
        Check(failures, "FreeMem actual A1/D0, original sizes and exact ownership order", () =>
            Assert.Equal(releases, fixture.Frees));
        if (production)
            Check(failures, "production Exec received unchanged native A1/D0", () =>
                Assert.Equal(releases, fixture.RecordedFrees));
        Check(failures, "no table or allocation is retired twice", () =>
            Assert.Equal(fixture.Frees.Count, fixture.Frees.Select(item => item.Address).Distinct().Count()));
        Check(failures, "every interleaved table retirement precedes the next depth's allocations", () =>
            AssertCallOrder(fixture, allocations.Length, releases.Length, inheritedCount));
        Check(failures, "complete allocation and release boundary counts", () =>
        {
            Assert.Equal(allocations.Length, fixture.MemoryBeforeAllocations.Count);
            Assert.Equal(allocations.Length, fixture.MemoryAfterAllocations.Count);
            Assert.Equal(releases.Length, fixture.MemoryAtFrees.Count);
            Assert.Equal(releases.Length, fixture.MemoryAfterFrees.Count);
        });

        for (var index = 0; index < allocations.Length; index++)
        {
            expected = BetweenAllocations(expected, bundle, index);
            if (index >= 8 && index % 2 == 0)
            {
                var releaseIndex = index / 2 - 4;
                Assert.InRange(releaseIndex, 0, inheritedCount - 1);
                var beforeInherited = expected;
                Check(failures, $"before inherited release {releaseIndex + 1}: committed smaller prefix and exact intermediate result", () =>
                    AssertMemoryEqual(beforeInherited, fixture.MemoryAtFrees[releaseIndex]));
                if (production)
                {
                    chunks.Add(new FreeChunk(releases[releaseIndex].Address, Rounded(releases[releaseIndex].Size)));
                    chunks = Coalesce(chunks);
                    expected = WithFreeList(expected, chunks);
                }
                var afterInherited = expected;
                Check(failures, $"after inherited release {releaseIndex + 1}: only classic free-list metadata changes", () =>
                    AssertMemoryEqual(afterInherited, fixture.MemoryAfterFrees[releaseIndex]));
            }
            var beforeAllocation = expected;
            Check(failures, $"before allocation {index + 1}: existing prefix, result, tables and guards", () =>
                AssertMemoryEqual(beforeAllocation, fixture.MemoryBeforeAllocations[index]));
            var allocation = allocations[index];
            if (allocation.Address != 0)
            {
                if (production)
                {
                    var occupied = Rounded(allocation.Size);
                    var chunkIndex = chunks.FindIndex(chunk => chunk.Size >= occupied);
                    Assert.True(chunkIndex >= 0, "Expected successful allocation must fit a real free chunk.");
                    var chunk = chunks[chunkIndex];
                    Assert.Equal(allocation.Address, chunk.Address);
                    Assert.True(chunk.Size > occupied, "This fixture deliberately leaves a tail chunk.");
                    chunks[chunkIndex] = new FreeChunk(chunk.Address + occupied, chunk.Size - occupied);
                }
                expected = WithClear(expected, allocation.Address, allocation.Size);
                if (production)
                    expected = WithFreeList(expected, chunks);
            }
            else if (production)
            {
                Assert.Equal(2 * depth + 1, index);
                var expectedSizes = depth == 7 ? new[] { 8u, 16u, 16u, 24u, 32u }
                    : new[] { 8u, 16u, 16u, 24u, 24u, 32u };
                Assert.Equal(expectedSizes, chunks.Select(chunk => chunk.Size));
                Assert.All(chunks, chunk => Assert.True(chunk.Size < PlaneBytes));
            }
            var afterAllocation = expected;
            Check(failures, $"after allocation {index + 1}: exact requested CLEAR and classic split metadata", () =>
                AssertMemoryEqual(afterAllocation, fixture.MemoryAfterAllocations[index]));
        }

        if (scenario is "success" or "odd-plane")
        {
            // Replacement-table stores happen before publisher preflight.
            // An odd new PLANE safely declines; an odd TABLE could fault at
            // these stores and is deliberately outside this cleanup fixture.
            expected = WithTable(expected, bundle.Table(depth), bundle.Planes(depth));
            if (success)
                expected = WithResult(WithPrefix(expected, bundle, depth), bundle, depth);
        }
        var beforeOwnRetirement = expected;
        var retired = new HashSet<uint>(releases.Take(inheritedCount).Select(item => item.Address));
        for (var index = inheritedCount; index < releases.Length; index++)
        {
            Check(failures, $"before cleanup release {index + 1}: exact committed or intermediate result", () =>
            {
                var snapshot = fixture.MemoryAtFrees[index];
                if (!production || index == inheritedCount)
                    AssertMemoryEqual(beforeOwnRetirement, snapshot);
                else
                    AssertMemoryEqual(fixture.MemoryAfterFrees[index - 1], snapshot);
                if (production)
                {
                    AssertOutsideClassicHeapUnchanged(beforeOwnRetirement, snapshot, productionHeapSize);
                    AssertFreeList(snapshot, chunks);
                    AssertStillOwned(beforeOwnRetirement, snapshot, allocations, retired);
                }
            });
            retired.Add(releases[index].Address);
            if (production)
            {
                chunks.Add(new FreeChunk(releases[index].Address, Rounded(releases[index].Size)));
                chunks = Coalesce(chunks);
            }
            Check(failures, $"after cleanup release {index + 1}: result unchanged until all retirement completes", () =>
            {
                var snapshot = fixture.MemoryAfterFrees[index];
                if (production)
                {
                    AssertOutsideClassicHeapUnchanged(beforeOwnRetirement, snapshot, productionHeapSize);
                    AssertFreeList(snapshot, chunks);
                    AssertStillOwned(beforeOwnRetirement, snapshot, allocations, retired);
                }
                else
                    AssertMemoryEqual(beforeOwnRetirement, snapshot);
            });
        }
        // Failed composed owners restore all twelve original LONGs only
        // after the final release. The distinct initial bytes expose partial,
        // reversed or stale-destination restoration without header repairs.
        var expectedFinal = success ? expected : expected with { ResultArea = initial.ResultArea.ToArray() };
        Check(failures, "final exact 48-byte result and complete surrounding canaries", () =>
        {
            Assert.Equal(ResultBytes, GraphicsLayouts.ScreenPrefixExecResultExtendedSize);
            Assert.Equal(expectedFinal.ResultArea, fixture.CaptureMemory().ResultArea);
        });
        Check(failures, "final memory and allocator ownership", () =>
        {
            var after = fixture.CaptureMemory();
            if (!production)
            {
                AssertMemoryEqual(expectedFinal, after);
                return;
            }
            var acquired = allocations.Where(item => item.Address != 0).Aggregate(0u, (sum, item) => sum + Rounded(item.Size));
            var expectedChunks = success
                ? Enumerable.Range(2, depth - 2).Select(level => new FreeChunk(bundle.Table(level), Rounded((uint)(4 * level))))
                    .Append(new FreeChunk(HeapAddress + 32u + acquired, initialFree - acquired)).ToArray()
                : new[] { new FreeChunk(HeapAddress + 32u, initialFree) };
            Assert.Equal(expectedChunks, chunks);
            Assert.Equal(success ? (depth == 7 ? 1848u : 1592u) : (depth == 7 ? 2080u : 2368u), fixture.FreeBytes);
            AssertFreeList(after, expectedChunks);
            AssertOutsideClassicHeapUnchanged(expectedFinal, after, productionHeapSize);
            AssertStillOwned(beforeOwnRetirement, after, allocations, retired);
            var lastRelease = fixture.MemoryAfterFrees[^1];
            var expectedAfterRelease = success ? lastRelease : lastRelease with { ResultArea = initial.ResultArea.ToArray() };
            AssertMemoryEqual(expectedAfterRelease, after);
        });
        // Classic requests are not rounded in the ABI: e.g. a 28-byte table
        // is freed with 28 even though it occupies 32 bytes. Successful depth
        // seven/eight retain 2216/2472 occupied bytes. Natural OOM restores the
        // entire original free chunk, with no native argument normalization.
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static int FailureOwnerDepth(int depth, string scenario) => scenario == "prior-plane-null" ? depth - 1 : depth;

    private static (uint Address, uint Size)[] Releases(Bundle bundle, int depth, string scenario)
    {
        if (scenario == "invalid-depth")
            return Array.Empty<(uint, uint)>();
        var ownerDepth = FailureOwnerDepth(depth, scenario);
        var releases = Enumerable.Range(2, ownerDepth - 3)
            .Select(level => (Address: bundle.Table(level), Size: (uint)(4 * level))).ToList();
        if (scenario == "success")
        {
            releases.Add((bundle.Table(depth - 1), (uint)(4 * (depth - 1))));
            return releases.ToArray();
        }
        if (scenario is "plane-null" or "prior-plane-null" or "odd-plane")
            releases.Add((bundle.Table(ownerDepth), (uint)(4 * ownerDepth)));
        if (scenario == "odd-plane")
            releases.Add((bundle.Plane(depth - 1), PlaneBytes));
        // Shared publisher rollback removes the replacement table BEFORE the
        // new plane, then consumes the saved lower-depth ownership stack.
        // The caller-owned result record is never an allocation to retire.
        for (var plane = ownerDepth - 2; plane >= 0; plane--)
            releases.Add((bundle.Plane(plane), PlaneBytes));
        releases.Add((bundle.Table(ownerDepth - 1), (uint)(4 * (ownerDepth - 1))));
        releases.Add((bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize));
        releases.Add((bundle.View, (uint)GraphicsLayouts.ViewSize));
        releases.Add((bundle.Screen, (uint)GraphicsLayouts.ScreenSize));
        return releases.ToArray();
    }

    private static void AssertCallOrder(Fixture fixture, int allocations, int releases, int inheritedCount)
    {
        var expected = new List<string>();
        for (var index = 0; index < allocations; index++)
        {
            if (index >= 8 && index % 2 == 0)
            {
                var releaseIndex = index / 2 - 4;
                Assert.InRange(releaseIndex, 0, inheritedCount - 1);
                expected.Add($"free:{releaseIndex + 1}");
            }
            expected.Add($"alloc:{index + 1}");
        }
        for (var index = inheritedCount; index < releases; index++)
            expected.Add($"free:{index + 1}");
        Assert.Equal(expected, fixture.CallOrder);
    }

    private static MemorySnapshot BetweenAllocations(MemorySnapshot source, Bundle bundle, int index)
    {
        if (index < 4 || index % 2 != 0)
            return source;
        var completedDepth = index / 2 - 1;
        var result = completedDepth == 1 ? source : WithTable(source, bundle.Table(completedDepth), bundle.Planes(completedDepth));
        result = WithPrefix(result, bundle, completedDepth);
        return completedDepth < 5 ? result : WithResult(result, bundle, completedDepth);
    }

    private static uint[] CreateMockAddresses()
    {
        var addresses = new uint[18];
        addresses[0] = HeapAddress + 0x100;
        addresses[1] = HeapAddress + 0x300;
        addresses[2] = HeapAddress + 0x400;
        addresses[3] = HeapAddress + 0x500;
        for (var depth = 2; depth <= 8; depth++)
        {
            addresses[2 * depth] = HeapAddress + 0x700u + (uint)(depth - 2) * 0x300u;
            addresses[2 * depth + 1] = addresses[2 * depth] + 0x100;
        }
        return addresses;
    }

    private static MemorySnapshot WithClear(MemorySnapshot source, uint address, uint count)
    {
        var heap = source.Heap.ToArray();
        heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)count)).Clear();
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithPrefix(MemorySnapshot source, Bundle bundle, int depth)
    {
        var heap = source.Heap.ToArray();
        var screen = heap.AsSpan(checked((int)(bundle.Screen - HeapAddress)), GraphicsLayouts.ScreenSize);
        var view = heap.AsSpan(checked((int)(bundle.View - HeapAddress)), GraphicsLayouts.ViewSize);
        var rasInfo = heap.AsSpan(checked((int)(bundle.RasInfo - HeapAddress)), GraphicsLayouts.RasInfoSize);
        screen.Clear();
        view.Clear();
        rasInfo.Clear();
        BinaryPrimitives.WriteUInt16BigEndian(screen[GraphicsLayouts.ScreenWidth..], Width);
        BinaryPrimitives.WriteUInt16BigEndian(screen[GraphicsLayouts.ScreenHeight..], Height);
        BinaryPrimitives.WriteUInt32BigEndian(view[GraphicsLayouts.ViewViewPort..], bundle.ViewPort);
        BinaryPrimitives.WriteUInt16BigEndian(view[GraphicsLayouts.ViewDyOffset..], (ushort)GraphicsLayouts.ViewDefaultDyOffset);
        BinaryPrimitives.WriteUInt16BigEndian(view[GraphicsLayouts.ViewDxOffset..], (ushort)GraphicsLayouts.ViewDefaultDxOffset);
        var viewPort = screen[GraphicsLayouts.ScreenViewPort..];
        BinaryPrimitives.WriteUInt16BigEndian(viewPort[GraphicsLayouts.ViewPortDWidth..], Width);
        BinaryPrimitives.WriteUInt16BigEndian(viewPort[GraphicsLayouts.ViewPortDHeight..], Height);
        viewPort[GraphicsLayouts.ViewPortSpritePriorities] = GraphicsLayouts.ViewPortDefaultSpritePriorities;
        BinaryPrimitives.WriteUInt32BigEndian(viewPort[GraphicsLayouts.ViewPortRasInfo..], bundle.RasInfo);
        var rastPort = screen[GraphicsLayouts.ScreenRastPort..];
        foreach (var offset in new[] { GraphicsLayouts.RastPortMask, GraphicsLayouts.RastPortFgPen, GraphicsLayouts.RastPortOutlinePen })
            rastPort[offset] = 0xFF;
        rastPort[GraphicsLayouts.RastPortDrawMode] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(rastPort[GraphicsLayouts.RastPortLinePattern..], 0xFFFF);
        BinaryPrimitives.WriteUInt32BigEndian(rastPort[GraphicsLayouts.RastPortBitMap..], bundle.BitMap);
        var bitMap = screen[GraphicsLayouts.ScreenBitMap..];
        BinaryPrimitives.WriteUInt16BigEndian(bitMap[GraphicsLayouts.BitMapBytesPerRow..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(bitMap[GraphicsLayouts.BitMapRows..], Height);
        bitMap[GraphicsLayouts.BitMapFlags] = 8;
        bitMap[GraphicsLayouts.BitMapDepth] = checked((byte)depth);
        for (var plane = 0; plane < depth; plane++)
            BinaryPrimitives.WriteUInt32BigEndian(bitMap[(GraphicsLayouts.BitMapPlanes + 4 * plane)..], bundle.Plane(plane));
        BinaryPrimitives.WriteUInt32BigEndian(rasInfo[GraphicsLayouts.RasInfoBitMap..], bundle.BitMap);
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithTable(MemorySnapshot source, uint address, params uint[] planes)
    {
        var heap = source.Heap.ToArray();
        var table = heap.AsSpan(checked((int)(address - HeapAddress)), planes.Length * 4);
        for (var index = 0; index < planes.Length; index++)
            BinaryPrimitives.WriteUInt32BigEndian(table[(index * 4)..], planes[index]);
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithResult(MemorySnapshot source, Bundle bundle, int depth)
    {
        var bytes = source.ResultArea.ToArray();
        var record = bytes.AsSpan((int)(Result - ResultArena), ResultBytes);
        var header = new[] { bundle.Screen, bundle.View, bundle.RasInfo, bundle.Table(depth) };
        for (var index = 0; index < header.Length; index++)
            BinaryPrimitives.WriteUInt32BigEndian(record[(index * 4)..], header[index]);
        for (var plane = 0; plane < depth; plane++)
            BinaryPrimitives.WriteUInt32BigEndian(record[(16 + plane * 4)..], bundle.Plane(plane));
        // Depth five owns only its original 36-byte record. Depth six onward
        // publishes the extended record and explicitly clears unused slots.
        if (depth >= 6)
            for (var plane = depth; plane < 8; plane++)
                BinaryPrimitives.WriteUInt32BigEndian(record[(16 + plane * 4)..], 0);
        return source with { ResultArea = bytes };
    }

    private static MemorySnapshot WithFreeList(MemorySnapshot source, IReadOnlyList<FreeChunk> chunks)
    {
        var heap = source.Heap.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(heap.AsSpan((int)ExecLayout.MemHeader.First), chunks[0].Address);
        BinaryPrimitives.WriteUInt32BigEndian(heap.AsSpan((int)ExecLayout.MemHeader.Free), chunks.Aggregate(0u, (sum, chunk) => sum + chunk.Size));
        for (var index = 0; index < chunks.Count; index++)
        {
            var bytes = heap.AsSpan(checked((int)(chunks[index].Address - HeapAddress)), 8);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[(int)ExecLayout.MemChunk.Next..], index + 1 == chunks.Count ? 0u : chunks[index + 1].Address);
            BinaryPrimitives.WriteUInt32BigEndian(bytes[(int)ExecLayout.MemChunk.Bytes..], chunks[index].Size);
        }
        return source with { Heap = heap };
    }

    private static void AssertFreeList(MemorySnapshot snapshot, IReadOnlyList<FreeChunk> chunks)
    {
        Assert.Equal(chunks[0].Address, BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.First)));
        Assert.Equal(chunks.Aggregate(0u, (sum, chunk) => sum + chunk.Size),
            BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.Free)));
        for (var index = 0; index < chunks.Count; index++)
        {
            var bytes = HeapBytes(snapshot, chunks[index].Address, 8);
            Assert.Equal(index + 1 == chunks.Count ? 0u : chunks[index + 1].Address,
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)ExecLayout.MemChunk.Next)));
            Assert.Equal(chunks[index].Size, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)ExecLayout.MemChunk.Bytes)));
        }
    }

    private static List<FreeChunk> Coalesce(IEnumerable<FreeChunk> chunks)
    {
        var result = new List<FreeChunk>();
        foreach (var chunk in chunks.OrderBy(chunk => chunk.Address))
        {
            if (result.Count != 0 && result[^1].Address + result[^1].Size == chunk.Address)
                result[^1] = result[^1] with { Size = result[^1].Size + chunk.Size };
            else
                result.Add(chunk);
        }
        return result;
    }

    private static void AssertStillOwned(MemorySnapshot beforeRetirement, MemorySnapshot current,
        IEnumerable<(uint Address, uint Size, uint Flags)> allocations, ISet<uint> retired)
    {
        foreach (var allocation in allocations.Where(item => item.Address != 0 && !retired.Contains(item.Address)))
            Assert.Equal(HeapBytes(beforeRetirement, allocation.Address, Rounded(allocation.Size)),
                HeapBytes(current, allocation.Address, Rounded(allocation.Size)));
    }

    private static uint Rounded(uint size) => checked((size + 7) & ~7u);
    private static byte[] HeapBytes(MemorySnapshot snapshot, uint address, uint count)
        => snapshot.Heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)count)).ToArray();
    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        AssertGuardsEqual(expected, actual);
    }
    private static void AssertGuardsEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.ResultArea, actual.ResultArea);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
        Assert.Equal(expected.ArenaGuards, actual.ArenaGuards);
        Assert.Equal(expected.StackGuards, actual.StackGuards);
        Assert.Equal(expected.Caller, actual.Caller);
    }
    private static void AssertOutsideClassicHeapUnchanged(MemorySnapshot expected, MemorySnapshot actual, int heapSize)
    {
        AssertGuardsEqual(expected, actual);
        Assert.Equal(expected.Heap.AsSpan(heapSize).ToArray(), actual.Heap.AsSpan(heapSize).ToArray());
        var expectedHeader = expected.Heap.AsSpan(0, 32).ToArray();
        var actualHeader = actual.Heap.AsSpan(0, 32).ToArray();
        foreach (var offset in new[] { (int)ExecLayout.MemHeader.First, (int)ExecLayout.MemHeader.Free })
        {
            expectedHeader.AsSpan(offset, 4).Clear();
            actualHeader.AsSpan(offset, 4).Clear();
        }
        Assert.Equal(expectedHeader, actualHeader);
    }
    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record Bundle(IReadOnlyList<uint> AllocationOrder)
    {
        internal uint Screen => AllocationOrder[0];
        internal uint View => AllocationOrder[1];
        internal uint RasInfo => AllocationOrder[2];
        internal uint ViewPort => Screen + (uint)GraphicsLayouts.ScreenViewPort;
        internal uint BitMap => Screen + (uint)GraphicsLayouts.ScreenBitMap;
        internal uint Table(int depth) => AllocationOrder[2 * depth];
        internal uint Plane(int plane) => AllocationOrder[2 * plane + 3];
        internal uint[] Planes(int depth) => Enumerable.Range(0, depth).Select(Plane).ToArray();
    }
    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback,
        int DepthSeven, int DepthEight)
    {
        internal int Entry(int depth) => depth == 7 ? DepthSeven : DepthEight;
    }
    private sealed record MemorySnapshot(byte[] Heap, byte[] ResultArea, byte[] Low, byte[] GraphicsImage,
        byte[] ArenaGuards, byte[] StackGuards, byte[] Caller);
    private sealed record FreeChunk(uint Address, uint Size);
    private sealed record CallResult(uint Data0, uint Address6, uint ProgramCounter, uint StackPointer, bool HitFallback);

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        internal const uint ExecBase = 0x0077_0000;
        private const uint CallerAddress = 0x00B8_0000;
        internal const uint ReturnAddress = CallerAddress + 6;
        private const uint StackAddress = 0x00C7_0000;
        internal const uint StackPointer = StackAddress + 0x300;
        private readonly AmigaBus _bus = new();
        private readonly IM68kCore _cpu;
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;
        private readonly int _depth;
        private readonly string _scenario;
        private AmigaBusExecMemoryPlatform _platform;

        internal Fixture(int depth, bool relocated, string scenario, int productionHeapSize)
        {
            Assert.True(depth is 7 or 8);
            _depth = depth;
            _scenario = scenario;
            Route = $"depth {depth}/" + (relocated ? "relocated HUNK" : "fixed image")
                + (productionHeapSize == 0 ? "/mock Exec" : "/production Classic Exec") + "/private JSR";
            if (relocated)
            {
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(_bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
                    _bus.MapWritableMemory(address, new byte[size]);
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(addresses);
                _graphicsBase = program.SegmentBases[0] + (uint)Hunk.Value.VectorOffset;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(item => item.Key, item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(FixedGraphicsBase, FixedResidentAddress, fallback, entries, fallback);
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                _bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                _bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                _bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                _bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                _graphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
            }
            Assert.True(_nativeCodeAddress >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            _bus.MapWritableMemory(HeapAddress - 0x20, Enumerable.Repeat((byte)0xA5, ArenaSize + 0x40).ToArray());
            _bus.MapWritableMemory(ResultArena, Enumerable.Repeat((byte)0x6B, 0x100).ToArray());
            for (var index = 0; index < ResultBytes; index++)
                _bus.WriteByte(Result + (uint)index, checked((byte)(0x80 + index)), 0);
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var index = 0; index < 0x100; index++)
                _bus.WriteByte((uint)index, 0xC3, 0);
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            _bus.WriteWord(CallerAddress, 0x4EB9); // JSR absolute.L, not an invented public vector
            _bus.WriteLong(CallerAddress + 2, _nativeCodeAddress + (uint)Image.Value.Entry(depth));
            _bus.WriteWord(ReturnAddress, 0x4E71);
            if (productionHeapSize != 0)
                InstallProductionExec(productionHeapSize);
            else
                InstallMockExec();
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint FirstFreeChunk => _bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.First);
        internal uint FreeBytes => _bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.Free);
        internal List<(uint Size, uint Flags)> RawRequests { get; } = new();
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<(uint Address, uint Size)> RecordedFrees { get; } = new();
        internal List<string> CallOrder { get; } = new();
        internal List<MemorySnapshot> MemoryBeforeAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAfterAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal List<MemorySnapshot> MemoryAfterFrees { get; } = new();

        private void InstallMockExec()
        {
            _bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var index = Allocations.Count;
                Assert.InRange(index, 0, 2 * _depth + 1);
                CallOrder.Add($"alloc:{index + 1}");
                MemoryBeforeAllocations.Add(CaptureMemory());
                var size = state.D[0];
                var flags = state.D[1];
                var address = MockAddresses[index];
                if ((_scenario == "table-null" && index == 2 * _depth)
                    || (_scenario == "plane-null" && index == 2 * _depth + 1)
                    || (_scenario == "prior-plane-null" && index == 2 * _depth - 1))
                    address = 0;
                else if (_scenario == "odd-plane" && index == 2 * _depth + 1)
                    address++;
                RawRequests.Add((size, flags));
                Allocations.Add((address, size, flags));
                if (address != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + ArenaSize - address);
                    if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                        _bus.ClearMemory(address, checked((int)size));
                }
                MemoryAfterAllocations.Add(CaptureMemory());
                state.D[0] = address;
                PoisonVolatile(state, preserveD0: true);
            });
            _bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                CallOrder.Add($"free:{Frees.Count}");
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: false);
                MemoryAfterFrees.Add(CaptureMemory());
            });
        }

        private void InstallProductionExec(int heapSize)
        {
            _bus.MapWritableMemory(ExecBase, new byte[0x1000]);
            _platform = new AmigaBusExecMemoryPlatform(_bus, () => 0, ThrowAlert, null,
                (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { });
            PortableExec.ExecListCore.Initialize(ref _platform, ExecBase + (uint)ExecLayout.ExecBase.MemList);
            PortableExec.ExecMemoryCore.AddMemList<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, (uint)heapSize, ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip,
                0, HeapAddress, APTR.Null);
            var context = new ExecMemoryContext(
                bus: _bus,
                allocate: Allocate,
                allocateAbsolute: (_, _) => throw new InvalidOperationException("Unexpected AllocAbs."),
                free: Free,
                available: flags => PortableExec.ExecMemoryCore.AvailMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                    ref _platform, ExecBase, (ExecApi.MemoryFlags)flags),
                allocateFromHeader: (_, _, _) => throw new InvalidOperationException("Unexpected Allocate."),
                deallocateToHeader: (_, _, _) => throw new InvalidOperationException("Unexpected Deallocate."),
                typeOfMemory: _ => throw new InvalidOperationException("Unexpected TypeOfMem."),
                recordAlloc: (size, flags, address) => Allocations.Add((address, checked((uint)size), flags)),
                recordAllocAbs: (_, _, _) => throw new InvalidOperationException("Unexpected AllocAbs record."),
                recordFree: (address, size) => RecordedFrees.Add((address, checked((uint)size))),
                getExecBase: () => ExecBase,
                allocator: PortableExec.ExecMemoryAllocatorKind.Classic,
                getCurrentTask: () => 0,
                alert: ThrowAlert,
                invokeMemoryHandler: (_, _, _, _, _) => MemoryHandlerResult.DidNothing,
                expungeLibraries: _ => { },
                setActiveState: _ => { });
            var services = new ExecMemoryServices(context);
            _bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                RawRequests.Add((state.D[0], state.D[1]));
                CallOrder.Add($"alloc:{RawRequests.Count}");
                MemoryBeforeAllocations.Add(CaptureMemory());
                services.AllocMem(state);
                MemoryAfterAllocations.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: true);
            });
            _bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                CallOrder.Add($"free:{Frees.Count}");
                MemoryAtFrees.Add(CaptureMemory());
                services.FreeMem(state); // actual native arguments; no expected-tuple adapter
                PoisonVolatile(state, preserveD0: false);
                MemoryAfterFrees.Add(CaptureMemory());
            });
        }
        private uint Allocate(int size, uint flags)
            => PortableExec.ExecMemoryCore.AllocMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, checked((uint)size), (ExecApi.MemoryFlags)flags).Raw;
        private void Free(uint address, int size)
            => PortableExec.ExecMemoryCore.FreeMem<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, address, checked((uint)size));
        private static void ThrowAlert(uint alert)
            => throw new InvalidOperationException($"Unexpected Exec alert 0x{alert:X8}.");
        private static void PoisonVolatile(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }
        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(HeapAddress, ArenaSize), ReadBytes(ResultArena, 0x100), ReadBytes(0, 0x100),
            ReadBytes(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(HeapAddress - 0x20, 0x20).Concat(ReadBytes(HeapAddress + ArenaSize, 0x20)).ToArray(),
            ReadBytes(StackAddress, 0x100).Concat(ReadBytes(StackAddress + 0x340, 0x2C0)).ToArray(),
            ReadBytes(CallerAddress, 8));

        internal CallResult Construct(uint requestedDepth)
        {
            var entry = _nativeCodeAddress + (uint)Image.Value.Entry(_depth);
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            for (var index = 0; index < 8; index++)
                _cpu.State.D[index] = 0xD000_0100u + (uint)index;
            for (var index = 0; index < 6; index++)
                _cpu.State.A[index] = 0xA000_0201u + (uint)(index * 0x10);
            _cpu.State.A[0] = Result;
            _cpu.State.A[6] = ExecBase;
            _cpu.State.D[0] = Width;
            _cpu.State.D[1] = Height;
            _cpu.State.D[2] = PlaneBytes;
            _cpu.State.D[3] = requestedDepth;
            // Private ABI: result record at A0, caller-supplied Exec in A6,
            // D0=1/0. No public callee-save or D1..D7 return ABI is invented.
            _cpu.ExecuteInstruction();
            Assert.Equal(entry, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            var hitFallback = false;
            for (var instruction = 0; instruction < 250_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                hitFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                Assert.True((pc >= _nativeCodeAddress && (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length)
                    || pc == ExecBase - 198 || pc == ExecBase - 210, $"Unexpected execution address 0x{pc:X8}.");
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return new CallResult(_cpu.State.D[0], _cpu.State.A[6], _cpu.State.ProgramCounter,
                        _cpu.State.A[7], hitFallback);
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
