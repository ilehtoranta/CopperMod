using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsScreenPrefixDepthFourExecAbiTests
{
    private const uint HeapAddress = 0x00D0_0000;
    private const int ArenaSize = 0x1000;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly Bundle MockBundle = new(
        HeapAddress + 0x100, HeapAddress + 0x300, HeapAddress + 0x400, HeapAddress + 0x500,
        HeapAddress + 0x700, HeapAddress + 0x800, HeapAddress + 0xA00, HeapAddress + 0xB00,
        HeapAddress + 0xD00, HeapAddress + 0xE00);
    private static readonly (uint Size, uint Flags)[] Requests =
    {
        ((uint)GraphicsLayouts.ScreenSize, PublicClear), ((uint)GraphicsLayouts.ViewSize, PublicClear),
        ((uint)GraphicsLayouts.RasInfoSize, PublicClear), (PlaneBytes, ChipClear),
        (8u, PublicClear), (PlaneBytes, ChipClear), (12u, PublicClear), (PlaneBytes, ChipClear),
        (16u, PublicClear), (PlaneBytes, ChipClear)
    };
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(
            out var entries, out var fallback, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out _, out _, out _, out var allocator);
        return new NativeImage(code, entries, fallback, allocator);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "success", "table-null", "fourth-plane-null", "odd-fourth-plane", "invalid-depth", "third-plane-null"
        })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void PrivateDepthFourUsesActualExecFreeAbiAcrossInheritedRetirementAndOwnRollback(bool relocated, string scenario)
    {
        using var fixture = new Fixture(relocated, scenario);
        var bundle = scenario == "odd-fourth-plane" ? MockBundle with { Plane3 = MockBundle.Plane3 + 1 } : MockBundle;
        var allocations = ExpectedMockAllocations(bundle, scenario);
        var releases = Releases(bundle, scenario);
        var expected = fixture.CaptureMemory();
        var result = fixture.Construct(scenario == "invalid-depth" ? 3u : 4u);
        var failures = new List<string>();
        Check(failures, "private D0/PC/SP/A6 return", () => AssertReturn(result, scenario == "success" ? bundle.Screen : 0));
        Check(failures, "raw AllocMem D0/D1 requests", () =>
            Assert.Equal(allocations.Select(item => (item.Size, item.Flags)), fixture.RawRequests));
        Check(failures, "unmodified allocation results", () => Assert.Equal(allocations, fixture.Allocations));
        Check(failures, "FreeMem actual A1/D0 and ownership order", () => Assert.Equal(releases, fixture.Frees));
        Check(failures, "every acquired block is retired at most once", () =>
            Assert.Equal(fixture.Frees.Count, fixture.Frees.Select(item => item.Address).Distinct().Count()));
        Check(failures, "inherited free occurs after allocation eight and before allocation nine", () =>
            AssertCallOrder(fixture, allocations.Length, releases.Length, HasDepthThreePrefix(scenario)));
        if (scenario == "success")
            Check(failures, "eight private ownership results", () => Assert.Equal(bundle.Results, result.Data));

        Check(failures, "allocation and retirement boundary counts", () =>
        {
            Assert.Equal(allocations.Length, fixture.MemoryBeforeAllocations.Count);
            Assert.Equal(allocations.Length, fixture.MemoryAfterAllocations.Count);
            Assert.Equal(releases.Length, fixture.MemoryAtFrees.Count);
            Assert.Equal(releases.Length, fixture.MemoryAfterFrees.Count);
        });
        for (var index = 0; index < allocations.Length; index++)
        {
            expected = BetweenAllocations(expected, bundle, index);
            if (index == 8)
            {
                var depthThree = expected;
                Check(failures, "inherited old eight-byte table release sees completed depth-three publication", () =>
                    AssertMemoryEqual(depthThree, fixture.MemoryAtFrees[0]));
                Check(failures, "inherited mock release does not change the table or any owned bytes", () =>
                    AssertMemoryEqual(depthThree, fixture.MemoryAfterFrees[0]));
            }
            var before = expected;
            Check(failures, $"before allocation {index + 1}: existing prefix, tables and guards", () =>
                AssertMemoryEqual(before, fixture.MemoryBeforeAllocations[index]));
            var allocation = allocations[index];
            if (allocation.Address != 0)
                expected = WithClear(expected, allocation.Address, allocation.Size);
            var after = expected;
            Check(failures, $"after allocation {index + 1}: allocator CLEAR only", () =>
                AssertMemoryEqual(after, fixture.MemoryAfterAllocations[index]));
        }

        if (scenario is "success" or "odd-fourth-plane")
        {
            // The producer fills the new table before the publisher checks
            // its entries. An odd fourth PLANE safely declines there; an odd
            // TABLE could fault earlier and is deliberately not used here.
            expected = WithTable(expected, bundle.Table4, bundle.Planes);
            if (scenario == "success")
                expected = WithPrefix(expected, bundle, 4);
        }
        var retirement = expected;
        for (var index = HasDepthThreePrefix(scenario) ? 1 : 0; index < releases.Length; index++)
        {
            Check(failures, $"before release {index + 1}: publication or unchanged smaller prefix", () =>
                AssertMemoryEqual(retirement, fixture.MemoryAtFrees[index]));
            Check(failures, $"after release {index + 1}: mock retirement never rewrites owned bytes", () =>
                AssertMemoryEqual(retirement, fixture.MemoryAfterFrees[index]));
        }
        Check(failures, "final entire arena, all tables/planes, padding and guards", () =>
            AssertMemoryEqual(expected, fixture.CaptureMemory()));
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PrivateDepthFourUsesRealClassicExecForInterleavedRetirementAndNaturalFourthPlaneOom(bool relocated, bool exhaustHeap)
    {
        var heapSize = exhaustHeap ? 1280 : 2048;
        var scenario = exhaustHeap ? "fourth-plane-null" : "success";
        using var fixture = new Fixture(relocated, scenario, heapSize);
        var first = fixture.FirstFreeChunk;
        var initialFree = fixture.FreeBytes;
        Assert.Equal(HeapAddress + 32u, first);
        Assert.Equal((uint)heapSize - 32u, initialFree);
        var addresses = new uint[Requests.Length];
        var acquired = 0u;
        for (var index = 0; index < Requests.Length; index++)
        {
            if (!exhaustHeap || index != 9)
            {
                // The retired eight-byte table cannot satisfy the later
                // sixteen-byte table or 256-byte plane requests. Classic
                // first-fit therefore continues at the remaining tail.
                addresses[index] = first + acquired;
                acquired += Rounded(Requests[index].Size);
            }
        }
        Assert.Equal(exhaustHeap ? 1200u : 1456u, acquired);
        if (exhaustHeap)
            Assert.Equal(56u, initialFree - acquired + 8u); // separate eight- and 48-byte free chunks
        var bundle = new Bundle(addresses[0], addresses[1], addresses[2], addresses[3], addresses[4],
            addresses[5], addresses[6], addresses[7], addresses[8], addresses[9]);
        var allocations = Requests.Select((item, index) => (Address: addresses[index], item.Size, item.Flags)).ToArray();
        var releases = Releases(bundle, scenario);
        var initial = fixture.CaptureMemory();
        var expected = initial;
        var result = fixture.Construct(4);
        var failures = new List<string>();
        Check(failures, "private D0/PC/SP/A6 return", () => AssertReturn(result, exhaustHeap ? 0 : bundle.Screen));
        Check(failures, "real AllocMem D0/D1 requests", () => Assert.Equal(Requests, fixture.RawRequests));
        Check(failures, "actual classic allocator results", () => Assert.Equal(allocations, fixture.Allocations));
        Check(failures, "native FreeMem actual A1/D0 and order", () => Assert.Equal(releases, fixture.Frees));
        Check(failures, "production FreeMem received the original native arguments", () =>
            Assert.Equal(releases, fixture.RecordedFrees));
        Check(failures, "no table or envelope is retired twice", () =>
            Assert.Equal(fixture.Frees.Count, fixture.Frees.Select(item => item.Address).Distinct().Count()));
        Check(failures, "inherited release precedes both depth-four requests", () =>
            AssertCallOrder(fixture, Requests.Length, releases.Length, inheritedRetirement: true));
        if (!exhaustHeap)
            Check(failures, "eight unmodified constructor results", () => Assert.Equal(bundle.Results, result.Data));
        Check(failures, "real allocation and release boundary counts", () =>
        {
            Assert.Equal(Requests.Length, fixture.MemoryBeforeAllocations.Count);
            Assert.Equal(Requests.Length, fixture.MemoryAfterAllocations.Count);
            Assert.Equal(releases.Length, fixture.MemoryAtFrees.Count);
            Assert.Equal(releases.Length, fixture.MemoryAfterFrees.Count);
        });

        var chunks = new List<FreeChunk> { new(first, initialFree) };
        for (var index = 0; index < Requests.Length; index++)
        {
            expected = BetweenAllocations(expected, bundle, index);
            if (index == 8)
            {
                var depthThree = expected;
                Check(failures, "real inherited release sees the complete depth-three prefix and table", () =>
                    AssertMemoryEqual(depthThree, fixture.MemoryAtFrees[0]));
                chunks.Add(new FreeChunk(bundle.Table2, 8));
                chunks = Coalesce(chunks);
                expected = WithFreeList(expected, chunks);
                var afterInherited = expected;
                Check(failures, "real inherited eight-byte retirement changes only classic free-list metadata", () =>
                    AssertMemoryEqual(afterInherited, fixture.MemoryAfterFrees[0]));
            }
            var before = expected;
            Check(failures, $"real before allocation {index + 1}: publication and interleaved free list", () =>
                AssertMemoryEqual(before, fixture.MemoryBeforeAllocations[index]));
            if (addresses[index] != 0)
            {
                var occupied = Rounded(Requests[index].Size);
                var chunkIndex = chunks.FindIndex(chunk => chunk.Size >= occupied);
                Assert.True(chunkIndex >= 0, "Expected successful allocation must fit a real free chunk.");
                var chunk = chunks[chunkIndex];
                Assert.Equal(addresses[index], chunk.Address);
                Assert.True(chunk.Size > occupied, "This geometry deliberately leaves a tail chunk.");
                chunks[chunkIndex] = new FreeChunk(chunk.Address + occupied, chunk.Size - occupied);
                expected = WithClear(expected, addresses[index], Requests[index].Size);
                expected = WithFreeList(expected, chunks);
            }
            else
            {
                Assert.True(exhaustHeap && index == 9);
                Assert.Equal(new[] { 8u, 48u }, chunks.Select(chunk => chunk.Size));
                Assert.All(chunks, chunk => Assert.True(chunk.Size < PlaneBytes));
            }
            var after = expected;
            Check(failures, $"real after allocation {index + 1}: exact CLEAR and classic split metadata", () =>
                AssertMemoryEqual(after, fixture.MemoryAfterAllocations[index]));
        }
        if (!exhaustHeap)
        {
            expected = WithTable(expected, bundle.Table4, bundle.Planes);
            expected = WithPrefix(expected, bundle, 4);
        }
        var beforeOwnRetirement = expected;
        var retired = new HashSet<uint> { bundle.Table2 };
        Check(failures, "own retirement sees completed depth four or the unchanged depth-three prefix on OOM", () =>
            AssertMemoryEqual(beforeOwnRetirement, fixture.MemoryAtFrees[1]));
        for (var index = 1; index < releases.Length; index++)
        {
            Check(failures, $"real before release {index + 1}: surviving ownership and free list", () =>
            {
                var snapshot = fixture.MemoryAtFrees[index];
                if (index > 1)
                    AssertMemoryEqual(fixture.MemoryAfterFrees[index - 1], snapshot);
                AssertOutsideClassicHeapUnchanged(initial, snapshot, heapSize);
                AssertFreeList(snapshot, chunks);
                AssertStillOwned(beforeOwnRetirement, snapshot, allocations, retired);
            });
            retired.Add(releases[index].Address);
            chunks.Add(new FreeChunk(releases[index].Address, Rounded(releases[index].Size)));
            chunks = Coalesce(chunks);
            Check(failures, $"real after release {index + 1}: exact coalescing and no surviving-block mutation", () =>
            {
                var snapshot = fixture.MemoryAfterFrees[index];
                AssertOutsideClassicHeapUnchanged(initial, snapshot, heapSize);
                AssertFreeList(snapshot, chunks);
                AssertStillOwned(beforeOwnRetirement, snapshot, allocations, retired);
            });
        }
        Check(failures, "final classic ownership, coalescing and no native write after final release", () =>
        {
            var expectedChunks = exhaustHeap
                ? new[] { new FreeChunk(first, initialFree) }
                : new[] { new FreeChunk(bundle.Table2, 8), new FreeChunk(bundle.Table3, 16),
                    new FreeChunk(first + acquired, initialFree - acquired) };
            Assert.Equal(expectedChunks, chunks);
            Assert.Equal(expectedChunks.Aggregate(0u, (sum, chunk) => sum + chunk.Size), fixture.FreeBytes);
            var after = fixture.CaptureMemory();
            AssertFreeList(after, expectedChunks);
            AssertOutsideClassicHeapUnchanged(initial, after, heapSize);
            AssertStillOwned(beforeOwnRetirement, after, allocations, retired);
            AssertMemoryEqual(fixture.MemoryAfterFrees[^1], after);
        });
        // Requests and occupied extents remain distinct: the old tables are
        // released as 8 and 12 bytes, occupying 8 and 16 in ClassicPolicy.
        // Success acquires 1456 rounded bytes and retains 1432. OOM releases
        // every acquired block and restores all 1248 originally free bytes.
        // No native register, constructor header, or allocation is repaired.
        Assert.True(failures.Count == 0, $"{fixture.Route}, classic {(exhaustHeap ? "OOM" : "success")}:\n"
            + string.Join("\n", failures));
    }

    private static bool HasDepthThreePrefix(string scenario) => scenario is not ("invalid-depth" or "third-plane-null");

    private static (uint Address, uint Size, uint Flags)[] ExpectedMockAllocations(Bundle bundle, string scenario)
    {
        var count = scenario switch { "invalid-depth" => 0, "third-plane-null" => 8, "table-null" => 9, _ => 10 };
        return Requests.Take(count).Select((item, index) =>
        {
            var isNull = (scenario == "third-plane-null" && index == 7)
                || (scenario == "table-null" && index == 8) || (scenario == "fourth-plane-null" && index == 9);
            return (isNull ? 0u : bundle.AllocationOrder[index], item.Size, item.Flags);
        }).ToArray();
    }

    private static (uint Address, uint Size)[] Releases(Bundle bundle, string scenario)
    {
        if (scenario == "invalid-depth")
            return Array.Empty<(uint, uint)>();
        if (scenario == "third-plane-null")
            return new[]
            {
                (bundle.Table3, 12u), (bundle.Plane1, PlaneBytes), (bundle.Plane0, PlaneBytes), (bundle.Table2, 8u),
                (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize), (bundle.View, (uint)GraphicsLayouts.ViewSize),
                (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
            };

        // Allocation-failure frames still have the old table above RasInfo:
        // release Table3, then RasInfo (distinct allocations, both size 12).
        // Publisher rollback has repacked them in the opposite order. The
        // identities follow the allocation/frame contract, never equal-size
        // substitutions or a guessed ABI derived from argument values.
        (uint Address, uint Size)[] allocationFailure =
        {
            (bundle.Plane2, PlaneBytes), (bundle.Plane1, PlaneBytes), (bundle.Plane0, PlaneBytes),
            (bundle.Table3, 12), (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize),
            (bundle.View, (uint)GraphicsLayouts.ViewSize), (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
        };
        var own = scenario switch
        {
            "success" => new[] { (bundle.Table3, 12u) },
            "table-null" => allocationFailure,
            "fourth-plane-null" => new[] { (bundle.Table4, 16u) }.Concat(allocationFailure).ToArray(),
            "odd-fourth-plane" => new[]
            {
                (bundle.Plane3, PlaneBytes), (bundle.Table4, 16u), (bundle.Plane2, PlaneBytes),
                (bundle.Plane1, PlaneBytes), (bundle.Plane0, PlaneBytes),
                (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize), (bundle.Table3, 12u),
                (bundle.View, (uint)GraphicsLayouts.ViewSize), (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return new[] { (bundle.Table2, 8u) }.Concat(own).ToArray();
    }

    private static void AssertCallOrder(Fixture fixture, int allocationCount, int releaseCount, bool inheritedRetirement)
    {
        var expected = new List<string>();
        for (var index = 0; index < allocationCount; index++)
        {
            if (inheritedRetirement && index == 8)
                expected.Add("free:1");
            expected.Add($"alloc:{index + 1}");
        }
        for (var index = inheritedRetirement ? 1 : 0; index < releaseCount; index++)
            expected.Add($"free:{index + 1}");
        Assert.Equal(expected, fixture.CallOrder);
    }

    private static MemorySnapshot BetweenAllocations(MemorySnapshot source, Bundle bundle, int index) => index switch
    {
        4 => WithPrefix(source, bundle, 1),
        6 => WithPrefix(WithTable(source, bundle.Table2, bundle.Plane0, bundle.Plane1), bundle, 2),
        8 => WithPrefix(WithTable(source, bundle.Table3, bundle.Plane0, bundle.Plane1, bundle.Plane2), bundle, 3),
        _ => source
    };

    private static MemorySnapshot WithClear(MemorySnapshot source, uint address, uint count)
    {
        var heap = source.Heap.ToArray();
        heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)count)).Clear();
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithPrefix(MemorySnapshot source, Bundle bundle, byte depth)
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
        bitMap[GraphicsLayouts.BitMapDepth] = depth;
        for (var index = 0; index < depth; index++)
            BinaryPrimitives.WriteUInt32BigEndian(bitMap[(GraphicsLayouts.BitMapPlanes + 4 * index)..], bundle.Planes[index]);
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

    private static MemorySnapshot WithFreeList(MemorySnapshot source, IReadOnlyList<FreeChunk> chunks)
    {
        var heap = source.Heap.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(heap.AsSpan((int)ExecLayout.MemHeader.First), chunks[0].Address);
        BinaryPrimitives.WriteUInt32BigEndian(heap.AsSpan((int)ExecLayout.MemHeader.Free), chunks.Aggregate(0u, (sum, chunk) => sum + chunk.Size));
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = heap.AsSpan(checked((int)(chunks[index].Address - HeapAddress)), 8);
            BinaryPrimitives.WriteUInt32BigEndian(chunk[(int)ExecLayout.MemChunk.Next..], index + 1 == chunks.Count ? 0u : chunks[index + 1].Address);
            BinaryPrimitives.WriteUInt32BigEndian(chunk[(int)ExecLayout.MemChunk.Bytes..], chunks[index].Size);
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
    private static void AssertReturn(CallResult result, uint expectedD0)
    {
        Assert.Equal(expectedD0, result.Data[0]);
        Assert.Equal(Fixture.ExecBase, result.Address6);
        Assert.Equal(Fixture.ReturnAddress, result.ProgramCounter);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        Assert.False(result.HitFallback);
    }
    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record Bundle(uint Screen, uint View, uint RasInfo, uint Plane0, uint Table2,
        uint Plane1, uint Table3, uint Plane2, uint Table4, uint Plane3)
    {
        internal uint ViewPort => Screen + (uint)GraphicsLayouts.ScreenViewPort;
        internal uint BitMap => Screen + (uint)GraphicsLayouts.ScreenBitMap;
        internal uint[] Planes => new[] { Plane0, Plane1, Plane2, Plane3 };
        internal uint[] AllocationOrder => new[] { Screen, View, RasInfo, Plane0, Table2, Plane1, Table3, Plane2, Table4, Plane3 };
        internal uint[] Results => new[] { Screen, View, RasInfo, Table4, Plane0, Plane1, Plane2, Plane3 };
    }
    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback, int Allocator);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsImage, byte[] ArenaGuards, byte[] StackGuards, byte[] Caller);
    private sealed record FreeChunk(uint Address, uint Size);
    private sealed record CallResult(uint[] Data, uint Address6, uint ProgramCounter, uint StackPointer, bool HitFallback);

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
        private readonly string _scenario;
        private AmigaBusExecMemoryPlatform _platform;

        internal Fixture(bool relocated, string scenario, int productionHeapSize = 0)
        {
            _scenario = scenario;
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/private JSR absolute";
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
                _graphicsBase = program.SegmentBases[0] + (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize;
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
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var index = 0; index < 0x100; index++)
                _bus.WriteByte((uint)index, 0xC3, 0);
            // This entry is private: A6 is caller-supplied ExecBase. No
            // public LVO, SysBase lookup or public callee-save ABI is implied.
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            _bus.WriteWord(CallerAddress, 0x4EB9); // actual JSR absolute.L
            _bus.WriteLong(CallerAddress + 2, _nativeCodeAddress + (uint)Image.Value.Allocator);
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
                Assert.InRange(index, 0, Requests.Length - 1);
                CallOrder.Add($"alloc:{index + 1}");
                MemoryBeforeAllocations.Add(CaptureMemory());
                var size = state.D[0];
                var flags = state.D[1];
                var address = MockBundle.AllocationOrder[index];
                if ((_scenario == "third-plane-null" && index == 7)
                    || (_scenario == "table-null" && index == 8) || (_scenario == "fourth-plane-null" && index == 9))
                    address = 0;
                else if (_scenario == "odd-fourth-plane" && index == 9)
                    address++;
                RawRequests.Add((size, flags));
                Allocations.Add((address, size, flags));
                if (address != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + ArenaSize - address);
                    // Even the malformed odd plane gets allocator CLEAR;
                    // all Screen/table publication remains native code.
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
                services.FreeMem(state);
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
            ReadBytes(HeapAddress, ArenaSize), ReadBytes(0, 0x100),
            ReadBytes(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(HeapAddress - 0x20, 0x20).Concat(ReadBytes(HeapAddress + ArenaSize, 0x20)).ToArray(),
            ReadBytes(StackAddress, 0x100).Concat(ReadBytes(StackAddress + 0x340, 0x2C0)).ToArray(),
            ReadBytes(CallerAddress, 8));

        internal CallResult Construct(uint depth)
        {
            var entry = _nativeCodeAddress + (uint)Image.Value.Allocator;
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            for (var index = 0; index < 8; index++)
                _cpu.State.D[index] = 0xD000_0100u + (uint)index;
            for (var index = 0; index < 6; index++)
                _cpu.State.A[index] = 0xA000_0201u + (uint)(index * 0x10);
            _cpu.State.A[6] = ExecBase;
            _cpu.State.D[0] = Width;
            _cpu.State.D[1] = Height;
            _cpu.State.D[2] = PlaneBytes;
            _cpu.State.D[3] = depth;
            _cpu.ExecuteInstruction();
            Assert.Equal(entry, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            var hitFallback = false;
            for (var instruction = 0; instruction < 120_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                hitFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                Assert.True((pc >= _nativeCodeAddress && (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length)
                    || pc == ExecBase - 198 || pc == ExecBase - 210, $"Unexpected execution address 0x{pc:X8}.");
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return new CallResult(_cpu.State.D.ToArray(), _cpu.State.A[6], _cpu.State.ProgramCounter,
                        _cpu.State.A[7], hitFallback);
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
