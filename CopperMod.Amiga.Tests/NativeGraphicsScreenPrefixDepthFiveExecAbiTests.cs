using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsScreenPrefixDepthFiveExecAbiTests
{
    // Prerequisites: the smaller allocators' Exec adapters, correct private
    // RastPort defaults, and the independently tested 100-byte extent.
    private const uint HeapAddress = 0x00D0_0000;
    private const int ArenaSize = 0x2000;
    private const uint ResultArena = 0x00D1_0000;
    private const uint Result = ResultArena + 0x20;
    private const int ResultBytes = 36;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly Bundle MockBundle = new(
        HeapAddress + 0x100, HeapAddress + 0x300, HeapAddress + 0x400, HeapAddress + 0x500,
        HeapAddress + 0x700, HeapAddress + 0x800, HeapAddress + 0xA00, HeapAddress + 0xB00,
        HeapAddress + 0xD00, HeapAddress + 0xE00, HeapAddress + 0x1000, HeapAddress + 0x1100);
    private static readonly (uint Size, uint Flags)[] Requests =
    {
        ((uint)GraphicsLayouts.ScreenSize, PublicClear), ((uint)GraphicsLayouts.ViewSize, PublicClear),
        ((uint)GraphicsLayouts.RasInfoSize, PublicClear), (PlaneBytes, ChipClear),
        (8u, PublicClear), (PlaneBytes, ChipClear), (12u, PublicClear), (PlaneBytes, ChipClear),
        (16u, PublicClear), (PlaneBytes, ChipClear), (20u, PublicClear), (PlaneBytes, ChipClear)
    };
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(
            out var entries, out var fallback, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out var allocator);
        return new NativeImage(code, entries, fallback, allocator);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "success", "table-null", "fifth-plane-null", "odd-fifth-plane", "invalid-depth", "fourth-plane-null"
        })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void PrivateDepthFiveUsesActualExecAbiAndPublishesOnlyTheCompletedGuestResult(bool relocated, string scenario)
    {
        var bundle = scenario == "odd-fifth-plane" ? MockBundle with { Plane4 = MockBundle.Plane4 + 1 } : MockBundle;
        var count = scenario switch { "invalid-depth" => 0, "fourth-plane-null" => 10, "table-null" => 11, _ => 12 };
        var allocations = Requests.Take(count).Select((request, index) =>
        {
            var isNull = (scenario == "fourth-plane-null" && index == 9) ||
                (scenario == "table-null" && index == 10) || (scenario == "fifth-plane-null" && index == 11);
            return (Address: isNull ? 0u : bundle.AllocationOrder[index], request.Size, request.Flags);
        }).ToArray();
        Verify(relocated, scenario, bundle, allocations, productionHeapSize: 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PrivateDepthFiveUsesRealClassicExecForInterleavedTableRetirementAndNaturalFifthPlaneOom(
        bool relocated, bool exhaustHeap)
    {
        var heapSize = exhaustHeap ? 1536 : 2048;
        var scenario = exhaustHeap ? "fifth-plane-null" : "success";
        var addresses = new uint[Requests.Length];
        var acquired = 0u;
        for (var index = 0; index < Requests.Length; index++)
        {
            if (exhaustHeap && index == 11)
                continue;
            // The retired eight- and sixteen-byte chunks cannot satisfy
            // the later twenty-byte table or any 256-byte plane request.
            addresses[index] = HeapAddress + 32u + acquired;
            acquired += Rounded(Requests[index].Size);
        }
        Assert.Equal(exhaustHeap ? 1480u : 1736u, acquired);
        if (exhaustHeap)
            Assert.Equal(48u, (uint)heapSize - 32u - acquired + 8u + 16u);
        var bundle = new Bundle(addresses[0], addresses[1], addresses[2], addresses[3],
            addresses[4], addresses[5], addresses[6], addresses[7], addresses[8], addresses[9], addresses[10], addresses[11]);
        var allocations = Requests.Select((request, index) => (Address: addresses[index], request.Size, request.Flags)).ToArray();
        Verify(relocated, scenario, bundle, allocations, heapSize);
    }

    private static void Verify(bool relocated, string scenario, Bundle bundle,
        (uint Address, uint Size, uint Flags)[] allocations, int productionHeapSize)
    {
        var production = productionHeapSize != 0;
        var success = scenario == "success";
        var inheritedCount = scenario == "invalid-depth" ? 0 : scenario == "fourth-plane-null" ? 1 : 2;
        var releases = Releases(bundle, scenario);
        using var fixture = new Fixture(relocated, scenario, productionHeapSize);
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
        // An unadapted real FreeMem may alert before returning. Preserve the
        // captured raw tuple and independent snapshots in that red report;
        // never normalize its arguments to make production Exec continue.
        Check(failures, "native execution", () => result = fixture.Construct(scenario == "invalid-depth" ? 4u : 5u));
        Check(failures, "private D0/PC/SP/A6 return", () =>
        {
            var returned = Assert.IsType<CallResult>(result);
            Assert.Equal(success ? 1u : 0u, returned.Data0);
            Assert.Equal(Fixture.ExecBase, returned.Address6);
            Assert.Equal(Fixture.ReturnAddress, returned.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, returned.StackPointer);
        });
        Check(failures, "raw AllocMem D0/D1 requests", () =>
            Assert.Equal(allocations.Select(item => (item.Size, item.Flags)), fixture.RawRequests));
        Check(failures, "unmodified allocator results", () => Assert.Equal(allocations, fixture.Allocations));
        Check(failures, "FreeMem actual A1/D0, original sizes and ownership order", () => Assert.Equal(releases, fixture.Frees));
        if (production)
            Check(failures, "production Exec received the original native A1/D0", () => Assert.Equal(releases, fixture.RecordedFrees));
        Check(failures, "no owner retired twice", () => Assert.Equal(
            fixture.Frees.Count, fixture.Frees.Select(item => item.Address).Distinct().Count()));
        Check(failures, "inherited table retirements precede the next depth's requests", () =>
            AssertCallOrder(fixture, allocations.Length, releases.Length, inheritedCount));
        Check(failures, "all allocation and retirement snapshots captured", () =>
        {
            Assert.Equal(allocations.Length, fixture.MemoryBeforeAllocations.Count);
            Assert.Equal(allocations.Length, fixture.MemoryAfterAllocations.Count);
            Assert.Equal(releases.Length, fixture.MemoryAtFrees.Count);
            Assert.Equal(releases.Length, fixture.MemoryAfterFrees.Count);
        });

        for (var index = 0; index < allocations.Length; index++)
        {
            expected = BetweenAllocations(expected, bundle, index);
            if (index is 8 or 10)
            {
                var releaseIndex = index == 8 ? 0 : 1;
                var beforeInherited = expected;
                Check(failures, $"before inherited release {releaseIndex + 1}: smaller prefix complete, result untouched", () =>
                    AssertMemoryEqual(beforeInherited, fixture.MemoryAtFrees[releaseIndex]));
                if (production)
                {
                    chunks.Add(new FreeChunk(releases[releaseIndex].Address, Rounded(releases[releaseIndex].Size)));
                    chunks = Coalesce(chunks);
                    expected = WithFreeList(expected, chunks);
                }
                var afterInherited = expected;
                Check(failures, $"after inherited release {releaseIndex + 1}: only Exec-owned free-list metadata changes", () =>
                    AssertMemoryEqual(afterInherited, fixture.MemoryAfterFrees[releaseIndex]));
            }
            var beforeAllocation = expected;
            Check(failures, $"before allocation {index + 1}: complete earlier publication and result guards", () =>
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
                Assert.Equal(11, index);
                Assert.Equal(new[] { 8u, 16u, 24u }, chunks.Select(chunk => chunk.Size));
                Assert.All(chunks, chunk => Assert.True(chunk.Size < PlaneBytes));
            }
            var afterAllocation = expected;
            Check(failures, $"after allocation {index + 1}: exact requested CLEAR and optional classic split", () =>
                AssertMemoryEqual(afterAllocation, fixture.MemoryAfterAllocations[index]));
        }

        if (scenario is "success" or "odd-fifth-plane")
        {
            // Table stores precede pointer validation. The odd PLANE safely
            // declines in the publisher; an odd TABLE is not used here.
            expected = WithTable(expected, bundle.Table5, bundle.Planes);
            if (success)
                expected = WithResult(WithPrefix(expected, bundle, 5), bundle);
        }
        var beforeOwnRetirement = expected;
        var retired = new HashSet<uint>(releases.Take(inheritedCount).Select(item => item.Address));
        for (var index = inheritedCount; index < releases.Length; index++)
        {
            Check(failures, $"before own release {index + 1}: exact result publication or unchanged failure record", () =>
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
            Check(failures, $"after own release {index + 1}: surviving buffers and result guards", () =>
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
        Check(failures, "final result record and complete surrounding canaries", () =>
        {
            Assert.Equal(ResultBytes, GraphicsLayouts.ScreenPrefixExecResultSize);
            Assert.Equal(expected.ResultArea, fixture.CaptureMemory().ResultArea);
        });
        Check(failures, "final memory and allocator ownership", () =>
        {
            var after = fixture.CaptureMemory();
            if (!production)
            {
                AssertMemoryEqual(expected, after);
                return;
            }
            var acquired = allocations.Where(item => item.Address != 0).Aggregate(0u, (sum, item) => sum + Rounded(item.Size));
            var expectedChunks = success
                ? new[] { new FreeChunk(bundle.Table2, 8), new FreeChunk(bundle.Table3, 16), new FreeChunk(bundle.Table4, 16),
                    new FreeChunk(HeapAddress + 32u + acquired, initialFree - acquired) }
                : new[] { new FreeChunk(HeapAddress + 32u, initialFree) };
            Assert.Equal(expectedChunks, chunks);
            Assert.Equal(success ? 320u : 1504u, fixture.FreeBytes);
            AssertFreeList(after, expectedChunks);
            AssertOutsideClassicHeapUnchanged(beforeOwnRetirement, after, productionHeapSize);
            AssertStillOwned(beforeOwnRetirement, after, allocations, retired);
            AssertMemoryEqual(fixture.MemoryAfterFrees[^1], after);
        });
        // Success keeps 1696 occupied bytes; natural OOM restores the single
        // original 1504-byte chunk. Result bytes are never repaired by tests.
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static (uint Address, uint Size)[] Releases(Bundle bundle, string scenario)
    {
        if (scenario == "invalid-depth")
            return Array.Empty<(uint, uint)>();
        if (scenario == "fourth-plane-null")
            return new[]
            {
                (bundle.Table2, 8u), (bundle.Table4, 16u), (bundle.Plane2, PlaneBytes), (bundle.Plane1, PlaneBytes),
                (bundle.Plane0, PlaneBytes), (bundle.Table3, 12u), (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize),
                (bundle.View, (uint)GraphicsLayouts.ViewSize), (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
            };
        (uint Address, uint Size)[] allocationFailure =
        {
            (bundle.Plane3, PlaneBytes), (bundle.Plane2, PlaneBytes), (bundle.Plane1, PlaneBytes), (bundle.Plane0, PlaneBytes),
            (bundle.Table4, 16u), (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize),
            (bundle.View, (uint)GraphicsLayouts.ViewSize), (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
        };
        var own = scenario switch
        {
            "success" => new[] { (bundle.Table4, 16u) },
            "table-null" => allocationFailure,
            "fifth-plane-null" => new[] { (bundle.Table5, 20u) }.Concat(allocationFailure).ToArray(),
            "odd-fifth-plane" => new[]
            {
                // This is the original allocation size, NOT the plane's
                // address accidentally copied into D1 by the old emitter.
                (bundle.Plane4, PlaneBytes), (bundle.Table5, 20u), (bundle.Plane3, PlaneBytes), (bundle.Plane2, PlaneBytes),
                (bundle.Plane1, PlaneBytes), (bundle.Plane0, PlaneBytes), (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize),
                (bundle.Table4, 16u), (bundle.View, (uint)GraphicsLayouts.ViewSize), (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return new[] { (bundle.Table2, 8u), (bundle.Table3, 12u) }.Concat(own).ToArray();
    }

    private static void AssertCallOrder(Fixture fixture, int allocations, int releases, int inheritedCount)
    {
        var expected = new List<string>();
        for (var index = 0; index < allocations; index++)
        {
            if (index == 8 && inheritedCount >= 1) expected.Add("free:1");
            if (index == 10 && inheritedCount == 2) expected.Add("free:2");
            expected.Add($"alloc:{index + 1}");
        }
        for (var index = inheritedCount; index < releases; index++)
            expected.Add($"free:{index + 1}");
        Assert.Equal(expected, fixture.CallOrder);
    }

    private static MemorySnapshot BetweenAllocations(MemorySnapshot source, Bundle bundle, int index) => index switch
    {
        4 => WithPrefix(source, bundle, 1),
        6 => WithPrefix(WithTable(source, bundle.Table2, bundle.Planes.Take(2).ToArray()), bundle, 2),
        8 => WithPrefix(WithTable(source, bundle.Table3, bundle.Planes.Take(3).ToArray()), bundle, 3),
        10 => WithPrefix(WithTable(source, bundle.Table4, bundle.Planes.Take(4).ToArray()), bundle, 4),
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

    private static MemorySnapshot WithResult(MemorySnapshot source, Bundle bundle)
    {
        var bytes = source.ResultArea.ToArray();
        // Nine consecutive guest LONGs are the private depth-five result ABI.
        // No native data register is substituted for this caller-owned record.
        var result = bytes.AsSpan((int)(Result - ResultArena), ResultBytes);
        var values = new[] { bundle.Screen, bundle.View, bundle.RasInfo, bundle.Table5,
            bundle.Plane0, bundle.Plane1, bundle.Plane2, bundle.Plane3, bundle.Plane4 };
        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteUInt32BigEndian(result[(index * 4)..], values[index]);
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

    private sealed record Bundle(uint Screen, uint View, uint RasInfo, uint Plane0, uint Table2, uint Plane1,
        uint Table3, uint Plane2, uint Table4, uint Plane3, uint Table5, uint Plane4)
    {
        internal uint ViewPort => Screen + (uint)GraphicsLayouts.ScreenViewPort;
        internal uint BitMap => Screen + (uint)GraphicsLayouts.ScreenBitMap;
        internal uint[] Planes => new[] { Plane0, Plane1, Plane2, Plane3, Plane4 };
        internal uint[] AllocationOrder => new[] { Screen, View, RasInfo, Plane0, Table2, Plane1, Table3, Plane2,
            Table4, Plane3, Table5, Plane4 };
    }
    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback, int Allocator);
    private sealed record MemorySnapshot(byte[] Heap, byte[] ResultArea, byte[] Low, byte[] GraphicsImage,
        byte[] ArenaGuards, byte[] StackGuards, byte[] Caller);
    private sealed record FreeChunk(uint Address, uint Size);
    private sealed record CallResult(uint Data0, uint Address6, uint ProgramCounter, uint StackPointer);

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
        internal Fixture(bool relocated, string scenario, int productionHeapSize)
        {
            _scenario = scenario;
            Route = (relocated ? "relocated HUNK" : "fixed image") +
                (productionHeapSize == 0 ? "/mock Exec" : "/production Classic Exec") + "/private result-record JSR";
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
            Assert.NotEqual(ExecBase, _graphicsBase);
            _bus.MapWritableMemory(HeapAddress - 0x20, Enumerable.Repeat((byte)0xA5, ArenaSize + 0x40).ToArray());
            _bus.MapWritableMemory(ResultArena, Enumerable.Repeat((byte)0x6B, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var index = 0; index < 0x100; index++)
                _bus.WriteByte((uint)index, 0xC3, 0);
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            _bus.WriteWord(CallerAddress, 0x4EB9);
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
                if ((_scenario == "fourth-plane-null" && index == 9) ||
                    (_scenario == "table-null" && index == 10) || (_scenario == "fifth-plane-null" && index == 11))
                    address = 0;
                else if (_scenario == "odd-fifth-plane" && index == 11)
                    address++;
                RawRequests.Add((size, flags));
                Allocations.Add((address, size, flags));
                if (address != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + ArenaSize - address);
                    // Even the deliberately odd plane receives byte-wise
                    // allocator CLEAR. No native header/table is fabricated.
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
                services.FreeMem(state); // unchanged native A1/D0, never inferred from an expected tuple
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
        internal CallResult Construct(uint depth)
        {
            var entry = _nativeCodeAddress + (uint)Image.Value.Allocator;
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
            _cpu.State.D[3] = depth;
            // Private result-record ABI: no public callee-save or D1..D7
            // ownership-return contract is imposed on this different entry.
            _cpu.ExecuteInstruction();
            Assert.Equal(entry, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            for (var instruction = 0; instruction < 150_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True((pc >= _nativeCodeAddress && (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length)
                    || pc == ExecBase - 198 || pc == ExecBase - 210, $"Unexpected execution address 0x{pc:X8}.");
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return new CallResult(_cpu.State.D[0], _cpu.State.A[6], _cpu.State.ProgramCounter, _cpu.State.A[7]);
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }
        public void Dispose() => _cpu.Dispose();
    }
}
