using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsScreenPrefixDepthTwoExecAbiTests
{
    private const uint HeapAddress = 0x00D0_0000;
    private const int ArenaSize = 0x1000;
    private const int ClassicHeapSize = 768;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly Bundle MockBundle = new(
        HeapAddress + 0x100, HeapAddress + 0x300, HeapAddress + 0x400,
        HeapAddress + 0x500, HeapAddress + 0x700, HeapAddress + 0x800);
    private static readonly (uint Size, uint Flags)[] Requests =
    {
        ((uint)GraphicsLayouts.ScreenSize, PublicClear), ((uint)GraphicsLayouts.ViewSize, PublicClear),
        ((uint)GraphicsLayouts.RasInfoSize, PublicClear), (PlaneBytes, ChipClear),
        (8u, PublicClear), (PlaneBytes, ChipClear)
    };
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(
            out var entries, out var fallback, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out _, out _, out var allocator);
        return new NativeImage(code, entries, fallback, allocator);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "success", "invalid-depth", "base-null", "table-null", "second-plane-null", "odd-second-plane"
        })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void PrivateDepthTwoAllocatorUsesActualExecArgumentsAndPreservesRollbackOwnership(bool relocated, string scenario)
    {
        using var fixture = new Fixture(relocated, scenario);
        var bundle = scenario == "odd-second-plane" ? MockBundle with { Plane1 = MockBundle.Plane1 + 1 } : MockBundle;
        var expectedAllocations = ExpectedMockAllocations(bundle, scenario);
        var expectedReleases = Releases(bundle, scenario);
        var expected = fixture.CaptureMemory();
        var result = fixture.Construct(scenario == "invalid-depth" ? 1u : 2u);
        var failures = new List<string>();
        Check(failures, "private return/PC/SP/A6", () => AssertReturn(result, scenario == "success" ? bundle.Screen : 0));
        Check(failures, "raw AllocMem D0/D1 requests", () => Assert.Equal(
            expectedAllocations.Select(item => (item.Size, item.Flags)), fixture.RawRequests));
        Check(failures, "allocation results", () => Assert.Equal(expectedAllocations, fixture.Allocations));
        Check(failures, "FreeMem A1/D0 order and original sizes", () => Assert.Equal(expectedReleases, fixture.Frees));
        if (scenario == "success")
            Check(failures, "six private ownership results", () => Assert.Equal(bundle.Results, result.Data.Take(6)));

        Check(failures, "allocation boundary snapshot counts", () =>
        {
            Assert.Equal(expectedAllocations.Length, fixture.MemoryBeforeAllocations.Count);
            Assert.Equal(expectedAllocations.Length, fixture.MemoryAfterAllocations.Count);
        });
        for (var index = 0; index < expectedAllocations.Length; index++)
        {
            // The nested allocator publishes its legitimate one-plane prefix
            // before depth two requests the table. Rollback does not unpublish
            // that prefix; it retires the allocations which contain it.
            if (index == 4)
                expected = WithPrefix(expected, bundle, depthTwo: false);
            var allocation = expectedAllocations[index];
            var expectedBefore = expected;
            Check(failures, $"before allocation {index}", () =>
                AssertMemoryEqual(expectedBefore, fixture.MemoryBeforeAllocations[index]));
            if (allocation.Address != 0)
                expected = WithClear(expected, allocation.Address, allocation.Size);
            var expectedAfter = expected;
            Check(failures, $"after allocation {index} CLEAR", () =>
                AssertMemoryEqual(expectedAfter, fixture.MemoryAfterAllocations[index]));
        }
        if (scenario is "success" or "odd-second-plane")
        {
            // Table writes precede the publisher's plane-pointer preflight.
            // An odd second PLANE is therefore safe to test here; an odd
            // TABLE would fault before the publisher can decline it.
            expected = WithTable(expected, bundle);
            if (scenario == "success")
                expected = WithPrefix(expected, bundle, depthTwo: true);
        }
        var expectedAtRetirement = expected;
        Check(failures, "no rollback publication/clearing beyond the existing prefix and table", () =>
        {
            Assert.Equal(expectedReleases.Length, fixture.MemoryAtFrees.Count);
            foreach (var snapshot in fixture.MemoryAtFrees)
                AssertMemoryEqual(expectedAtRetirement, snapshot);
        });
        Check(failures, "final complete arena and guards", () => AssertMemoryEqual(expected, fixture.CaptureMemory()));
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateDepthTwoSecondPlaneOomRetiresRealExecAllocationsAndRestoresClassicHeap(bool relocated)
    {
        using var fixture = new Fixture(relocated, "classic-oom", productionExec: true);
        var first = fixture.FirstFreeChunk;
        var initialFree = fixture.FreeBytes;
        Assert.Equal(HeapAddress + 32u, first);
        Assert.Equal(736u, initialFree);
        var addresses = new uint[6];
        var occupied = 0u;
        for (var index = 0; index < 5; index++)
        {
            addresses[index] = first + occupied;
            occupied += Rounded(Requests[index].Size);
        }
        Assert.Equal(656u, occupied); // 352 + 24 + 16 + 256 + 8
        Assert.Equal(80u, initialFree - occupied); // sixth request needs 256
        var bundle = new Bundle(addresses[0], addresses[1], addresses[2], addresses[3], addresses[4], 0);
        var expectedAllocations = Requests.Select((item, index) => (Address: addresses[index], item.Size, item.Flags)).ToArray();
        var expectedReleases = Releases(bundle, "second-plane-null");
        var initial = fixture.CaptureMemory();
        var expected = initial;
        var result = fixture.Construct(2);
        var failures = new List<string>();
        Check(failures, "private return/PC/SP/A6", () => AssertReturn(result, 0));
        Check(failures, "raw production allocation requests", () => Assert.Equal(Requests, fixture.RawRequests));
        Check(failures, "production allocation results including genuine OOM", () => Assert.Equal(expectedAllocations, fixture.Allocations));
        Check(failures, "actual native FreeMem A1/D0", () => Assert.Equal(expectedReleases, fixture.Frees));
        Check(failures, "production service recorded the unchanged native arguments", () => Assert.Equal(expectedReleases, fixture.RecordedFrees));
        Check(failures, "allocation boundary counts", () =>
        {
            Assert.Equal(6, fixture.MemoryBeforeAllocations.Count);
            Assert.Equal(6, fixture.MemoryAfterAllocations.Count);
        });

        var remaining = initialFree;
        for (var index = 0; index < Requests.Length; index++)
        {
            if (index == 4)
                expected = WithPrefix(expected, bundle, depthTwo: false);
            var expectedBefore = expected;
            Check(failures, $"production before allocation {index}", () =>
                AssertMemoryEqual(expectedBefore, fixture.MemoryBeforeAllocations[index]));
            if (addresses[index] != 0)
            {
                remaining -= Rounded(Requests[index].Size);
                expected = WithClear(expected, addresses[index], Requests[index].Size);
                expected = WithFreeList(expected,
                    new[] { new FreeChunk(addresses[index] + Rounded(Requests[index].Size), remaining) });
            }
            var expectedAfter = expected;
            Check(failures, $"production after allocation {index} and rounded padding", () =>
                AssertMemoryEqual(expectedAfter, fixture.MemoryAfterAllocations[index]));
        }
        var beforeRetirement = expected;
        Check(failures, "five releases after OOM, with one-plane prefix and zero table intact", () =>
        {
            Assert.Equal(expectedReleases.Length, fixture.MemoryAtFrees.Count);
            AssertMemoryEqual(beforeRetirement, fixture.MemoryAtFrees[0]);
        });
        var chunks = new List<FreeChunk> { new(first + occupied, initialFree - occupied) };
        var retired = new HashSet<uint>();
        for (var index = 0; index < expectedReleases.Length; index++)
        {
            Check(failures, $"production retirement {index} free list and still-owned bytes", () =>
            {
                var snapshot = fixture.MemoryAtFrees[index];
                AssertOutsideClassicHeapUnchanged(initial, snapshot);
                AssertFreeList(snapshot, chunks);
                for (var block = 0; block < 5; block++)
                {
                    if (!retired.Contains(addresses[block]))
                        Assert.Equal(HeapBytes(beforeRetirement, addresses[block], Rounded(Requests[block].Size)),
                            HeapBytes(snapshot, addresses[block], Rounded(Requests[block].Size)));
                }
            });
            retired.Add(expectedReleases[index].Address);
            chunks.Add(new FreeChunk(expectedReleases[index].Address, Rounded(expectedReleases[index].Size)));
            chunks = Coalesce(chunks);
        }
        Check(failures, "complete single-chunk heap restoration and outer guards", () =>
        {
            Assert.Equal(new[] { new FreeChunk(first, initialFree) }, chunks);
            var after = fixture.CaptureMemory();
            AssertFreeList(after, chunks);
            AssertOutsideClassicHeapUnchanged(initial, after);
        });
        // Freed bodies and padding belong to Exec after retirement. The free
        // list and still-owned snapshots are checked independently, without
        // requiring allocator metadata to preserve a retired Screen header.
        Assert.True(failures.Count == 0, $"{fixture.Route}, classic-oom:\n" + string.Join("\n", failures));
    }

    private static (uint Address, uint Size, uint Flags)[] ExpectedMockAllocations(Bundle bundle, string scenario)
    {
        var count = scenario switch { "invalid-depth" => 0, "base-null" => 1, "table-null" => 5, _ => 6 };
        return Requests.Take(count).Select((item, index) =>
        {
            var isNull = scenario == "base-null" || (scenario == "table-null" && index == 4)
                || (scenario == "second-plane-null" && index == 5);
            return (isNull ? 0u : bundle.AllocationOrder[index], item.Size, item.Flags);
        }).ToArray();
    }

    private static (uint Address, uint Size)[] Releases(Bundle bundle, string scenario) => scenario switch
    {
        "table-null" => new[]
        {
            (bundle.Plane0, PlaneBytes), (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize),
            (bundle.View, (uint)GraphicsLayouts.ViewSize), (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
        },
        "second-plane-null" => new[]
        {
            (bundle.Table, 8u), (bundle.Plane0, PlaneBytes), (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize),
            (bundle.View, (uint)GraphicsLayouts.ViewSize), (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
        },
        "odd-second-plane" => new[]
        {
            (bundle.Plane1, PlaneBytes), (bundle.Plane0, PlaneBytes), (bundle.Table, 8u),
            (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize), (bundle.View, (uint)GraphicsLayouts.ViewSize),
            (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
        },
        _ => Array.Empty<(uint, uint)>()
    };

    private static MemorySnapshot WithClear(MemorySnapshot source, uint address, uint count)
    {
        var heap = source.Heap.ToArray();
        heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)count)).Clear();
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithPrefix(MemorySnapshot source, Bundle bundle, bool depthTwo)
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
        bitMap[GraphicsLayouts.BitMapDepth] = depthTwo ? (byte)2 : (byte)1;
        BinaryPrimitives.WriteUInt32BigEndian(bitMap[GraphicsLayouts.BitMapPlanes..], bundle.Plane0);
        if (depthTwo)
            BinaryPrimitives.WriteUInt32BigEndian(bitMap[(GraphicsLayouts.BitMapPlanes + 4)..], bundle.Plane1);
        BinaryPrimitives.WriteUInt32BigEndian(rasInfo[GraphicsLayouts.RasInfoBitMap..], bundle.BitMap);
        return source with { Heap = heap };
    }

    private static MemorySnapshot WithTable(MemorySnapshot source, Bundle bundle)
    {
        var heap = source.Heap.ToArray();
        var table = heap.AsSpan(checked((int)(bundle.Table - HeapAddress)), 8);
        BinaryPrimitives.WriteUInt32BigEndian(table, bundle.Plane0);
        BinaryPrimitives.WriteUInt32BigEndian(table[4..], bundle.Plane1);
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
    private static void AssertOutsideClassicHeapUnchanged(MemorySnapshot expected, MemorySnapshot actual)
    {
        AssertGuardsEqual(expected, actual);
        Assert.Equal(expected.Heap.AsSpan(ClassicHeapSize).ToArray(), actual.Heap.AsSpan(ClassicHeapSize).ToArray());
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

    private sealed record Bundle(uint Screen, uint View, uint RasInfo, uint Plane0, uint Table, uint Plane1)
    {
        internal uint ViewPort => Screen + (uint)GraphicsLayouts.ScreenViewPort;
        internal uint BitMap => Screen + (uint)GraphicsLayouts.ScreenBitMap;
        internal uint[] AllocationOrder => new[] { Screen, View, RasInfo, Plane0, Table, Plane1 };
        internal uint[] Results => new[] { Screen, View, RasInfo, Table, Plane0, Plane1 };
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

        internal Fixture(bool relocated, string scenario, bool productionExec = false)
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
            Assert.NotEqual(ExecBase, _graphicsBase);
            _bus.MapWritableMemory(HeapAddress - 0x20, Enumerable.Repeat((byte)0xA5, ArenaSize + 0x40).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var index = 0; index < 0x100; index++)
                _bus.WriteByte((uint)index, 0xC3, 0);
            // Private entry only: no SysBase inference, GfxBase LVO, public
            // callee-save contract, or synthetic constructor/header repair.
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            _bus.WriteWord(CallerAddress, 0x4EB9); // JSR absolute.L
            _bus.WriteLong(CallerAddress + 2, _nativeCodeAddress + (uint)Image.Value.Allocator);
            _bus.WriteWord(ReturnAddress, 0x4E71);
            if (productionExec)
                InstallProductionExec();
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
        internal List<MemorySnapshot> MemoryBeforeAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAfterAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();

        private void InstallMockExec()
        {
            _bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var index = Allocations.Count;
                Assert.InRange(index, 0, Requests.Length - 1);
                MemoryBeforeAllocations.Add(CaptureMemory());
                var size = state.D[0];
                var flags = state.D[1];
                var address = MockBundle.AllocationOrder[index];
                if (_scenario == "base-null" || (_scenario == "table-null" && index == 4)
                    || (_scenario == "second-plane-null" && index == 5))
                    address = 0;
                else if (_scenario == "odd-second-plane" && index == 5)
                    address++;
                RawRequests.Add((size, flags));
                Allocations.Add((address, size, flags));
                if (address != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + ArenaSize - address);
                    // This mock explicitly honors CLEAR even for the odd
                    // byte-backed plane result. It never clears on NULL and
                    // does not publish any Screen/table/header fields itself.
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
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: false);
            });
        }

        private void InstallProductionExec()
        {
            _bus.MapWritableMemory(ExecBase, new byte[0x1000]);
            _platform = new AmigaBusExecMemoryPlatform(_bus, () => 0, ThrowAlert, null,
                (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { });
            PortableExec.ExecListCore.Initialize(ref _platform, ExecBase + (uint)ExecLayout.ExecBase.MemList);
            PortableExec.ExecMemoryCore.AddMemList<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, ClassicHeapSize, ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip,
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
                MemoryBeforeAllocations.Add(CaptureMemory());
                services.AllocMem(state);
                MemoryAfterAllocations.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: true);
            });
            _bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFrees.Add(CaptureMemory());
                services.FreeMem(state);
                PoisonVolatile(state, preserveD0: false);
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
            for (var instruction = 0; instruction < 100_000; instruction++)
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
