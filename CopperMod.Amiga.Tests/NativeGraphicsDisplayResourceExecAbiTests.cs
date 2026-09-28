using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsDisplayResourceExecAbiTests
{
    // Prerequisites: base Screen-prefix cleanup adapters, its correct private
    // RastPort defaults, and the independently tested 100-byte RastPort extent.
    private const uint HeapAddress = 0x00D0_0000;
    private const int ArenaSize = 0x2000;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint RawBytes = 128;
    private const ushort Marker = 0x4452;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly Bundle MockBundle = new(
        HeapAddress + 0x100, HeapAddress + 0x300, HeapAddress + 0x400, HeapAddress + 0x500,
        HeapAddress + 0x700, HeapAddress + 0x800, HeapAddress + 0x900, HeapAddress + 0xA00);
    private static readonly (uint Size, uint Flags)[] Requests =
    {
        ((uint)GraphicsLayouts.ScreenSize, PublicClear), ((uint)GraphicsLayouts.ViewSize, PublicClear),
        ((uint)GraphicsLayouts.RasInfoSize, PublicClear), (PlaneBytes, ChipClear),
        ((uint)GraphicsLayouts.CopListSize, PublicClear), ((uint)GraphicsLayouts.CprListSize, PublicClear),
        ((uint)GraphicsLayouts.CprListSize, PublicClear), (RawBytes, ChipClear)
    };
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(
            out var entries, out var fallback, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out var allocator);
        return new NativeImage(code, entries, fallback, allocator);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "success", "coplist-null", "lof-null", "shf-null", "raw-null", "invalid-raw-size", "prefix-plane-null"
        })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void PrivateDisplayAllocatorUsesActualExecAbiAcrossAllFourOwnRollbackPaths(bool relocated, string scenario)
    {
        var nullSlot = NullSlot(scenario);
        var count = scenario == "invalid-raw-size" ? 0 : nullSlot == 0 ? Requests.Length : nullSlot;
        var allocations = Requests.Take(count).Select((request, index) =>
            (Address: index + 1 == nullSlot ? 0u : MockBundle.Addresses[index], request.Size, request.Flags)).ToArray();
        Verify(relocated, scenario, MockBundle, allocations, productionHeapSize: 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PrivateDisplayAllocatorUsesRealClassicExecForSuccessAndNaturalRawCopperOom(bool relocated, bool exhaustHeap)
    {
        var heapSize = exhaustHeap ? 832 : 1024;
        var scenario = exhaustHeap ? "raw-null" : "success";
        var addresses = new uint[Requests.Length];
        var acquired = 0u;
        for (var index = 0; index < Requests.Length; index++)
        {
            if (exhaustHeap && index == 7)
                continue;
            addresses[index] = HeapAddress + 32u + acquired;
            acquired += Rounded(Requests[index].Size);
        }
        Assert.Equal(exhaustHeap ? 736u : 864u, acquired);
        if (exhaustHeap)
            Assert.Equal(64u, (uint)heapSize - 32u - acquired);
        var bundle = Bundle.FromAddresses(addresses);
        var allocations = Requests.Select((request, index) => (Address: addresses[index], request.Size, request.Flags)).ToArray();
        Verify(relocated, scenario, bundle, allocations, heapSize);
    }

    private static int NullSlot(string scenario) => scenario switch
    {
        "prefix-plane-null" => 4,
        "coplist-null" => 5,
        "lof-null" => 6,
        "shf-null" => 7,
        "raw-null" => 8,
        _ => 0
    };

    private static void Verify(bool relocated, string scenario, Bundle bundle,
        (uint Address, uint Size, uint Flags)[] allocations, int productionHeapSize)
    {
        var production = productionHeapSize != 0;
        var success = scenario == "success";
        // Before any display-side publication, acquired ownership is exactly
        // the successful prefix of the allocation sequence. Rollback is its
        // reverse, retaining original requested sizes rather than rounded ones.
        var releases = success ? Array.Empty<(uint Address, uint Size)>() : allocations
            .Where(item => item.Address != 0).Reverse().Select(item => (item.Address, item.Size)).ToArray();
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
        // Bad real FreeMem arguments may alert or silently fail. Preserve all
        // observed A1/D0 tuples; never substitute an expected address or size.
        Check(failures, "native execution", () => result = fixture.Construct(scenario == "invalid-raw-size" ? 2u : RawBytes));
        Check(failures, "private D0/PC/SP/A6 return", () =>
        {
            var returned = Assert.IsType<CallResult>(result);
            Assert.Equal(success ? bundle.Screen : 0u, returned.Data[0]);
            Assert.Equal(Fixture.ExecBase, returned.Address6);
            Assert.Equal(Fixture.ReturnAddress, returned.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, returned.StackPointer);
        });
        if (success)
            Check(failures, "all eight private ownership results", () =>
                Assert.Equal(bundle.Addresses, Assert.IsType<CallResult>(result).Data));
        Check(failures, "AllocMem actual D0/D1 requests", () =>
            Assert.Equal(allocations.Select(item => (item.Size, item.Flags)), fixture.RawRequests));
        Check(failures, "unmodified allocator results", () => Assert.Equal(allocations, fixture.Allocations));
        Check(failures, "FreeMem actual A1/D0 and exact reverse ownership", () => Assert.Equal(releases, fixture.Frees));
        if (production)
            Check(failures, "production Exec received native A1/D0 unchanged", () => Assert.Equal(releases, fixture.RecordedFrees));
        Check(failures, "no duplicate retirement", () =>
            Assert.Equal(fixture.Frees.Count, fixture.Frees.Select(item => item.Address).Distinct().Count()));
        Check(failures, "all acquisitions precede rollback", () => Assert.Equal(
            Enumerable.Range(1, allocations.Length).Select(index => $"alloc:{index}")
                .Concat(Enumerable.Range(1, releases.Length).Select(index => $"free:{index}")), fixture.CallOrder));
        Check(failures, "complete allocation and release snapshots", () =>
        {
            Assert.Equal(allocations.Length, fixture.MemoryBeforeAllocations.Count);
            Assert.Equal(allocations.Length, fixture.MemoryAfterAllocations.Count);
            Assert.Equal(releases.Length, fixture.MemoryAtFrees.Count);
            Assert.Equal(releases.Length, fixture.MemoryAfterFrees.Count);
        });

        for (var index = 0; index < allocations.Length; index++)
        {
            // The base allocator legitimately publishes the one-plane prefix
            // before the first CopList request. No display-side links exist yet.
            if (index == 4)
                expected = WithPrefix(expected, bundle);
            var beforeAllocation = expected;
            Check(failures, $"before allocation {index + 1}: prefix-only publication and complete guards", () =>
                AssertMemoryEqual(beforeAllocation, fixture.MemoryBeforeAllocations[index]));
            var allocation = allocations[index];
            if (allocation.Address != 0)
            {
                if (production)
                {
                    var occupied = Rounded(allocation.Size);
                    Assert.Single(chunks);
                    Assert.Equal(allocation.Address, chunks[0].Address);
                    Assert.True(chunks[0].Size > occupied, "Every expected success leaves a nonempty tail chunk.");
                    chunks[0] = new FreeChunk(chunks[0].Address + occupied, chunks[0].Size - occupied);
                }
                expected = WithClear(expected, allocation.Address, allocation.Size);
                if (production)
                    expected = WithFreeList(expected, chunks);
            }
            else if (production)
            {
                Assert.Equal(7, index);
                Assert.Single(chunks);
                Assert.Equal(64u, chunks[0].Size);
                Assert.True(chunks[0].Size < RawBytes);
            }
            var afterAllocation = expected;
            Check(failures, $"after allocation {index + 1}: exact requested CLEAR and optional classic split", () =>
                AssertMemoryEqual(afterAllocation, fixture.MemoryAfterAllocations[index]));
        }
        if (success)
            expected = WithDisplayPublication(expected, bundle);
        var beforeRetirement = expected;
        var retired = new HashSet<uint>();
        for (var index = 0; index < releases.Length; index++)
        {
            Check(failures, $"before release {index + 1}: no display publication or native cleanup writes", () =>
            {
                var snapshot = fixture.MemoryAtFrees[index];
                AssertMemoryEqual(index == 0 ? beforeRetirement : fixture.MemoryAfterFrees[index - 1], snapshot);
                if (production)
                {
                    AssertOutsideClassicHeapUnchanged(beforeRetirement, snapshot, productionHeapSize);
                    AssertFreeList(snapshot, chunks);
                    AssertStillOwned(beforeRetirement, snapshot, allocations, retired);
                }
            });
            retired.Add(releases[index].Address);
            if (production)
            {
                chunks.Add(new FreeChunk(releases[index].Address, Rounded(releases[index].Size)));
                chunks = Coalesce(chunks);
            }
            Check(failures, $"after release {index + 1}: only retired memory belongs to Exec", () =>
            {
                var snapshot = fixture.MemoryAfterFrees[index];
                if (production)
                {
                    AssertOutsideClassicHeapUnchanged(beforeRetirement, snapshot, productionHeapSize);
                    AssertFreeList(snapshot, chunks);
                    AssertStillOwned(beforeRetirement, snapshot, allocations, retired);
                }
                else
                    AssertMemoryEqual(beforeRetirement, snapshot);
            });
        }
        Check(failures, "final full memory and heap ownership", () =>
        {
            var after = fixture.CaptureMemory();
            if (!production || success)
                AssertMemoryEqual(expected, after);
            if (!production)
                return;
            var expectedChunks = success
                ? new[] { new FreeChunk(HeapAddress + 32u + 864u, 128u) }
                : new[] { new FreeChunk(HeapAddress + 32u, 800u) };
            Assert.Equal(expectedChunks, chunks);
            Assert.Equal(success ? 128u : 800u, fixture.FreeBytes);
            AssertFreeList(after, expectedChunks);
            AssertOutsideClassicHeapUnchanged(expected, after, productionHeapSize);
            AssertStillOwned(beforeRetirement, after, allocations, retired);
            if (!success)
                AssertMemoryEqual(fixture.MemoryAfterFrees[^1], after);
        });
        // Success leaves all eight buffers owned by the caller. Paired display
        // teardown has its own fixture; this one proves the allocator's rollback.
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static MemorySnapshot WithClear(MemorySnapshot source, uint address, uint count)
    {
        var heap = source.Heap.ToArray();
        heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)count)).Clear();
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithPrefix(MemorySnapshot source, Bundle bundle)
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
        bitMap[GraphicsLayouts.BitMapDepth] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(bitMap[GraphicsLayouts.BitMapPlanes..], bundle.Plane);
        BinaryPrimitives.WriteUInt32BigEndian(rasInfo[GraphicsLayouts.RasInfoBitMap..], bundle.BitMap);
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithDisplayPublication(MemorySnapshot source, Bundle bundle)
    {
        var heap = source.Heap.ToArray();
        var screen = heap.AsSpan(checked((int)(bundle.Screen - HeapAddress)), GraphicsLayouts.ScreenSize);
        var view = heap.AsSpan(checked((int)(bundle.View - HeapAddress)), GraphicsLayouts.ViewSize);
        var copList = heap.AsSpan(checked((int)(bundle.CopList - HeapAddress)), GraphicsLayouts.CopListSize);
        BinaryPrimitives.WriteUInt32BigEndian(view[GraphicsLayouts.ViewLoFCprList..], bundle.Lof);
        BinaryPrimitives.WriteUInt32BigEndian(view[GraphicsLayouts.ViewShFCprList..], bundle.Shf);
        BinaryPrimitives.WriteUInt32BigEndian(screen[(GraphicsLayouts.ScreenViewPort + GraphicsLayouts.ViewPortDspIns)..], bundle.CopList);
        BinaryPrimitives.WriteUInt32BigEndian(copList[GraphicsLayouts.CopListSystem..], bundle.Screen);
        BinaryPrimitives.WriteUInt32BigEndian(copList[GraphicsLayouts.CopListViewPort..], bundle.ViewPort);
        foreach (var offset in new[]
        {
            GraphicsLayouts.CopListCopIns, GraphicsLayouts.CopListCopPtr,
            GraphicsLayouts.CopListCopLStart, GraphicsLayouts.CopListCopSStart
        })
            BinaryPrimitives.WriteUInt32BigEndian(copList[offset..], bundle.Raw);
        BinaryPrimitives.WriteUInt16BigEndian(copList[GraphicsLayouts.CopListCount..], 2);
        BinaryPrimitives.WriteUInt16BigEndian(copList[GraphicsLayouts.CopListMaxCount..], 2);
        BinaryPrimitives.WriteUInt16BigEndian(copList[GraphicsLayouts.CopListFlags..], Marker);
        foreach (var address in new[] { bundle.Lof, bundle.Shf })
        {
            var cpr = heap.AsSpan(checked((int)(address - HeapAddress)), GraphicsLayouts.CprListSize);
            BinaryPrimitives.WriteUInt32BigEndian(cpr[GraphicsLayouts.CprListStart..], bundle.Raw);
            BinaryPrimitives.WriteUInt16BigEndian(cpr[GraphicsLayouts.CprListMaxCount..], 2);
        }
        var raw = heap.AsSpan(checked((int)(bundle.Raw - HeapAddress)), (int)RawBytes);
        // 64x32 is deliberately outside all generated-planar selectors. This
        // byte-level envelope check neither runs nor models the hardware Copper.
        BinaryPrimitives.WriteUInt16BigEndian(raw, 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(raw[2..], 0xFFFE);
        return source with { Heap = heap };
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

    private sealed record Bundle(uint Screen, uint View, uint RasInfo, uint Plane, uint CopList, uint Lof, uint Shf, uint Raw)
    {
        internal uint ViewPort => Screen + (uint)GraphicsLayouts.ScreenViewPort;
        internal uint BitMap => Screen + (uint)GraphicsLayouts.ScreenBitMap;
        internal uint[] Addresses => new[] { Screen, View, RasInfo, Plane, CopList, Lof, Shf, Raw };
        internal static Bundle FromAddresses(uint[] addresses)
            => new(addresses[0], addresses[1], addresses[2], addresses[3], addresses[4], addresses[5], addresses[6], addresses[7]);
    }
    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback, int Allocator);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsImage,
        byte[] ArenaGuards, byte[] StackGuards, byte[] Caller);
    private sealed record FreeChunk(uint Address, uint Size);
    private sealed record CallResult(uint[] Data, uint Address6, uint ProgramCounter, uint StackPointer);

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
                (productionHeapSize == 0 ? "/mock Exec" : "/production Classic Exec") + "/private JSR";
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
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var index = 0; index < 0x100; index++)
                _bus.WriteByte((uint)index, 0xC3, 0);
            // No SysBase dependency is supplied: this is a private A6=Exec entry.
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
                var address = index + 1 == NullSlot(_scenario) ? 0u : MockBundle.Addresses[index];
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
                services.FreeMem(state); // original native A1/D0, no argument repair
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
        internal CallResult Construct(uint rawBytes)
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
            _cpu.State.D[3] = rawBytes;
            // The private entry returns eight ownership pointers in D0..D7;
            // this deliberately does not impose a public callee-save contract.
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
                    return new CallResult(_cpu.State.D.ToArray(), _cpu.State.A[6], _cpu.State.ProgramCounter, _cpu.State.A[7]);
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }
        public void Dispose() => _cpu.Dispose();
    }
}
