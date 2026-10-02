using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsDisplayResourceTeardownExecAbiTests
{
    private const uint HeapAddress = 0x00D0_0000;
    private const int HeapSize = 0x1000;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint RawBytes = 128;
    private const uint OccupiedBytes = 864;
    private const ushort Marker = 0x4452;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly Bundle MockBundle = new(
        HeapAddress + 0x200, HeapAddress + 0x400, HeapAddress + 0x500, HeapAddress + 0x600,
        HeapAddress + 0x800, HeapAddress + 0x900, HeapAddress + 0xA00, HeapAddress + 0xB00);
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
            out var entries, out var fallback, out _, out _, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out var allocator, out var teardown);
        return new NativeImage(code, entries, fallback, allocator, teardown);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[] { "seeded-success", "constructor-roundtrip", "wrong-input-marker", "topology-mismatch" })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void PrivateDisplayTeardownUsesActualExecArgumentsAndClearsLinksBeforeRetirement(bool relocated, string scenario)
    {
        using var fixture = new Fixture(relocated);
        var bundle = MockBundle;
        var failures = new List<string>();
        if (scenario == "constructor-roundtrip")
        {
            var beforeConstruction = fixture.CaptureMemory();
            var constructed = fixture.Construct();
            Check(failures, "constructor caller", () => AssertReturn(constructed, MockBundle.Screen));
            Check(failures, "constructor result registers", () =>
                Assert.Equal(MockBundle.Addresses, constructed.Data));
            bundle = Bundle.FromRegisters(constructed.Data);
            Check(failures, "constructor exact requests", () =>
                Assert.Equal(ExpectedAllocations(MockBundle), fixture.Allocations));
            Check(failures, "constructor no retirement", () => Assert.Empty(fixture.Frees));
            Check(failures, "constructor guards", () =>
                AssertOutsideAllocationsUnchanged(beforeConstruction, fixture.CaptureMemory(), MockBundle));
            Check(failures, "constructor unchanged two-instruction topology", () => fixture.AssertPublishedBundle(bundle));
            // Deliberately use all eight actual results and untouched guest
            // headers. No seed, repair, or adapter runs after construction.
        }
        else
        {
            fixture.SeedBundle(bundle);
            if (scenario == "topology-mismatch")
                fixture.WriteLong(bundle.CopList + (uint)GraphicsLayouts.CopListNext, bundle.CopList);
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Teardown(bundle, scenario == "wrong-input-marker" ? (ushort)0 : Marker);
        var claimed = scenario is "seeded-success" or "constructor-roundtrip";
        var expected = claimed ? WithClearedLinks(before, bundle) : before;
        Check(failures, "private outcome and caller", () => AssertReturn(result, claimed ? 1u : 0u));
        Check(failures, "all eight FreeMem A1/D0 arguments", () => Assert.Equal(
            claimed ? Releases(bundle) : Array.Empty<(uint Address, uint Size)>(), fixture.Frees));
        Check(failures, "no new allocation during teardown", () => Assert.Equal(
            scenario == "constructor-roundtrip" ? ExpectedAllocations(MockBundle) : Array.Empty<(uint, uint, uint)>(),
            fixture.Allocations));
        Check(failures, "all links clear before the first release", () =>
        {
            Assert.Equal(claimed ? 8 : 0, fixture.MemoryAtFrees.Count);
            foreach (var snapshot in fixture.MemoryAtFrees)
                AssertMemoryEqual(expected, snapshot);
        });
        Check(failures, "final owned bytes and guards", () => AssertMemoryEqual(expected, fixture.CaptureMemory()));
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateDisplayTeardownRetiresRealExecAllocationsAndCoalescesClassicHeap(bool relocated)
    {
        using var fixture = new Fixture(relocated, productionExec: true);
        var first = fixture.FirstFreeChunk;
        var initialFreeBytes = fixture.FreeBytes;
        var expectedBundle = ContiguousBundle(first);
        var before = fixture.CaptureMemory();
        var constructed = fixture.Construct();
        AssertReturn(constructed, expectedBundle.Screen);
        Assert.Equal(expectedBundle.Addresses, constructed.Data);
        Assert.Equal(ExpectedAllocations(expectedBundle), fixture.Allocations);
        Assert.Empty(fixture.Frees);
        var bundle = Bundle.FromRegisters(constructed.Data);
        fixture.AssertPublishedBundle(bundle);
        var published = fixture.CaptureMemory();
        AssertNonHeapMemoryEqual(before, published);
        var freeChunks = new List<FreeChunk> { new(first + OccupiedBytes, initialFreeBytes - OccupiedBytes) };
        AssertFreeList(published, freeChunks);

        // Pass the actual A1/D0 tuple unchanged to the production services.
        // An invalid tuple may silently no-op; exact release and heap-state
        // assertions below establish the failure, not an assumed exception.
        var result = fixture.Teardown(bundle, Marker);
        AssertReturn(result, 1);
        var releases = Releases(bundle);
        Assert.Equal(releases, fixture.Frees);
        Assert.Equal(ExpectedAllocations(expectedBundle), fixture.Allocations);
        Assert.Equal(8, fixture.MemoryAtFrees.Count);
        var cleared = WithClearedLinks(published, bundle);
        AssertMemoryEqual(cleared, fixture.MemoryAtFrees[0]);

        var retired = new HashSet<uint>();
        for (var index = 0; index < releases.Length; index++)
        {
            var atFree = fixture.MemoryAtFrees[index];
            AssertNonHeapMemoryEqual(cleared, atFree);
            AssertFreeList(atFree, freeChunks);
            var addresses = bundle.Addresses;
            for (var block = 0; block < addresses.Length; block++)
            {
                if (!retired.Contains(addresses[block]))
                    Assert.Equal(HeapBytes(cleared, addresses[block], Rounded(Requests[block].Size)),
                        HeapBytes(atFree, addresses[block], Rounded(Requests[block].Size)));
            }
            retired.Add(releases[index].Address);
            freeChunks.Add(new FreeChunk(releases[index].Address, Rounded(releases[index].Size)));
            freeChunks = Coalesce(freeChunks);
        }
        Assert.Equal(new[] { new FreeChunk(first, initialFreeBytes) }, freeChunks);
        var after = fixture.CaptureMemory();
        AssertFreeList(after, freeChunks);
        AssertNonHeapMemoryEqual(before, after);
        // Retired buffers and their rounded padding now belong to classic
        // MemChunk metadata. Only still-owned bytes were held immutable;
        // the free-list oracle accounts for holes before raw/tail coalescing.
    }

    private static (uint Address, uint Size, uint Flags)[] ExpectedAllocations(Bundle bundle)
        => Requests.Select((request, index) => (bundle.Addresses[index], request.Size, request.Flags)).ToArray();

    private static (uint Address, uint Size)[] Releases(Bundle bundle) => new[]
    {
        (bundle.Shf, (uint)GraphicsLayouts.CprListSize), (bundle.Lof, (uint)GraphicsLayouts.CprListSize),
        (bundle.CopList, (uint)GraphicsLayouts.CopListSize), (bundle.Raw, RawBytes), (bundle.Plane, PlaneBytes),
        (bundle.RasInfo, (uint)GraphicsLayouts.RasInfoSize), (bundle.View, (uint)GraphicsLayouts.ViewSize),
        (bundle.Screen, (uint)GraphicsLayouts.ScreenSize)
    };

    private static uint Rounded(uint size) => checked((size + 7) & ~7u);

    private static Bundle ContiguousBundle(uint first)
    {
        var addresses = new uint[Requests.Length];
        var next = first;
        for (var index = 0; index < Requests.Length; index++)
        {
            addresses[index] = next;
            next += Rounded(Requests[index].Size);
        }
        Assert.Equal(OccupiedBytes, next - first);
        return Bundle.FromRegisters(addresses);
    }

    private static byte[] ExpectedCopList(Bundle bundle)
    {
        var bytes = new byte[GraphicsLayouts.CopListSize];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(GraphicsLayouts.CopListSystem), bundle.Screen);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(GraphicsLayouts.CopListViewPort), bundle.ViewPort);
        foreach (var offset in new[]
        {
            GraphicsLayouts.CopListCopIns, GraphicsLayouts.CopListCopPtr,
            GraphicsLayouts.CopListCopLStart, GraphicsLayouts.CopListCopSStart
        })
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), bundle.Raw);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(GraphicsLayouts.CopListCount), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(GraphicsLayouts.CopListMaxCount), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(GraphicsLayouts.CopListFlags), Marker);
        return bytes;
    }

    private static byte[] ExpectedCprList(Bundle bundle)
    {
        var bytes = new byte[GraphicsLayouts.CprListSize];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(GraphicsLayouts.CprListStart), bundle.Raw);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(GraphicsLayouts.CprListMaxCount), 2);
        return bytes;
    }

    private static byte[] ExpectedRaw()
    {
        var bytes = new byte[(int)RawBytes];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, GraphicsLayouts.CopperEndWait);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), GraphicsLayouts.CopperEndMask);
        return bytes;
    }

    private static MemorySnapshot WithClearedLinks(MemorySnapshot source, Bundle bundle)
    {
        var heap = source.Heap.ToArray();
        void Clear(uint address, int offset, int size)
            => heap.AsSpan(checked((int)(address - HeapAddress)) + offset, size).Clear();
        foreach (var offset in new[]
        {
            GraphicsLayouts.ViewViewPort, GraphicsLayouts.ViewLoFCprList, GraphicsLayouts.ViewShFCprList
        })
            Clear(bundle.View, offset, 4);
        foreach (var offset in new[]
        {
            GraphicsLayouts.CopListSystem, GraphicsLayouts.CopListViewPort, GraphicsLayouts.CopListCopIns,
            GraphicsLayouts.CopListCopPtr, GraphicsLayouts.CopListCopLStart, GraphicsLayouts.CopListCopSStart
        })
            Clear(bundle.CopList, offset, 4);
        foreach (var offset in new[] { GraphicsLayouts.CopListCount, GraphicsLayouts.CopListMaxCount, GraphicsLayouts.CopListFlags })
            Clear(bundle.CopList, offset, 2);
        foreach (var address in new[] { bundle.Lof, bundle.Shf })
        {
            Clear(address, GraphicsLayouts.CprListNext, 4);
            Clear(address, GraphicsLayouts.CprListStart, 4);
            Clear(address, GraphicsLayouts.CprListMaxCount, 2);
        }
        // No extra Screen/ViewPort/RasInfo clearing is invented here. Those
        // owned blocks are retired too, but this helper's clear set is fixed.
        return source with { Heap = heap };
    }

    private static byte[] HeapBytes(MemorySnapshot snapshot, uint address, uint size)
        => snapshot.Heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)size)).ToArray();

    private static void AssertOutsideAllocationsUnchanged(MemorySnapshot before, MemorySnapshot after, Bundle bundle)
    {
        var expected = before.Heap.ToArray();
        var addresses = bundle.Addresses;
        for (var index = 0; index < addresses.Length; index++)
        {
            var offset = checked((int)(addresses[index] - HeapAddress));
            after.Heap.AsSpan(offset, checked((int)Requests[index].Size)).CopyTo(expected.AsSpan(offset));
        }
        Assert.Equal(expected, after.Heap);
        AssertNonHeapMemoryEqual(before, after);
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

    private static void AssertFreeList(MemorySnapshot snapshot, IReadOnlyList<FreeChunk> chunks)
    {
        Assert.NotEmpty(chunks);
        Assert.Equal(chunks[0].Address,
            BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.First)));
        Assert.Equal(chunks.Aggregate(0u, (total, chunk) => total + chunk.Size),
            BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.Free)));
        for (var index = 0; index < chunks.Count; index++)
        {
            var bytes = HeapBytes(snapshot, chunks[index].Address, 8);
            Assert.Equal(index + 1 == chunks.Count ? 0u : chunks[index + 1].Address,
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)ExecLayout.MemChunk.Next)));
            Assert.Equal(chunks[index].Size,
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)ExecLayout.MemChunk.Bytes)));
        }
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertReturn(CallResult result, uint expectedD0)
    {
        Assert.Equal(expectedD0, result.Data[0]);
        Assert.Equal(result.CallerReturnAddress, result.ProgramCounter);
        Assert.Equal(result.CallerStackPointer, result.StackPointer);
    }

    private static void AssertNonHeapMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        AssertNonHeapMemoryEqual(expected, actual);
    }

    private sealed record Bundle(uint Screen, uint View, uint RasInfo, uint Plane, uint CopList, uint Lof, uint Shf, uint Raw)
    {
        internal uint ViewPort => Screen + (uint)GraphicsLayouts.ScreenViewPort;
        internal uint BitMap => Screen + (uint)GraphicsLayouts.ScreenBitMap;
        internal uint[] Addresses => new[] { Screen, View, RasInfo, Plane, CopList, Lof, Shf, Raw };
        internal static Bundle FromRegisters(uint[] data)
            => new(data[0], data[1], data[2], data[3], data[4], data[5], data[6], data[7]);
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries,
        int Fallback, int Allocator, int Teardown);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsImage);
    private sealed record FreeChunk(uint Address, uint Size);
    private sealed record CallResult(uint[] Data, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer);

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint ExecBase = 0x0077_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint ReturnAddress = CallerAddress + 6;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private readonly IM68kCore _cpu;
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;
        private AmigaBusExecMemoryPlatform _platform;

        internal Fixture(bool relocated, bool productionExec = false)
        {
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/private JSR absolute";
            if (relocated)
            {
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(Bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
                    Bus.MapWritableMemory(address, new byte[size]);
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(addresses);
                _graphicsBase = program.SegmentBases[0] + (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(item => item.Key,
                    item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    FixedGraphicsBase, FixedResidentAddress, fallback, entries, fallback);
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                Bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                Bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                Bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                Bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                _graphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
            }
            Assert.True(_nativeCodeAddress >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            var heap = new byte[HeapSize];
            Array.Fill(heap, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            for (var index = 0; index < 0x100; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            // No SysBase setup: these private entries receive Exec in A6.
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            if (productionExec)
                InstallProductionExec();
            else
                InstallMockExec();
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal string Route { get; }
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal uint FirstFreeChunk => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.First);
        internal uint FreeBytes => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.Free);

        private void InstallMockExec()
        {
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var index = Allocations.Count;
                Assert.InRange(index, 0, Requests.Length - 1);
                var address = MockBundle.Addresses[index];
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((address, size, flags));
                Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - address);
                if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                    Bus.ClearMemory(address, checked((int)size));
                state.D[0] = address;
                PoisonVolatile(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatile(state, preserveD0: false);
            });
        }

        private void InstallProductionExec()
        {
            Bus.MapWritableMemory(ExecBase, new byte[0x1000]);
            _platform = new AmigaBusExecMemoryPlatform(Bus, () => 0, ThrowAlert, null,
                (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { });
            PortableExec.ExecListCore.Initialize(ref _platform, ExecBase + (uint)ExecLayout.ExecBase.MemList);
            PortableExec.ExecMemoryCore.AddMemList<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, (uint)HeapSize, ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip,
                0, HeapAddress, APTR.Null);
            var context = new ExecMemoryContext(
                bus: Bus,
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
                recordFree: (_, _) => { },
                getExecBase: () => ExecBase,
                allocator: PortableExec.ExecMemoryAllocatorKind.Classic,
                getCurrentTask: () => 0,
                alert: ThrowAlert,
                invokeMemoryHandler: (_, _, _, _, _) => MemoryHandlerResult.DidNothing,
                expungeLibraries: _ => { },
                setActiveState: _ => { });
            var services = new ExecMemoryServices(context);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                services.AllocMem(state);
                PoisonVolatile(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
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

        internal void WriteLong(uint address, uint value) => Bus.WriteLong(address, value);
        private void WriteWord(uint address, ushort value) => Bus.WriteWord(address, value);
        private void SeedBytes(uint address, byte[] bytes)
        {
            for (var index = 0; index < bytes.Length; index++)
                Bus.WriteByte(address + (uint)index, bytes[index], 0);
        }

        internal void SeedBundle(Bundle bundle)
        {
            var addresses = bundle.Addresses;
            for (var index = 0; index < addresses.Length; index++)
                Bus.ClearMemory(addresses[index], checked((int)Requests[index].Size));
            WriteWord(bundle.Screen + (uint)GraphicsLayouts.ScreenWidth, Width);
            WriteWord(bundle.Screen + (uint)GraphicsLayouts.ScreenHeight, Height);
            WriteLong(bundle.View + (uint)GraphicsLayouts.ViewViewPort, bundle.ViewPort);
            WriteLong(bundle.View + (uint)GraphicsLayouts.ViewLoFCprList, bundle.Lof);
            WriteLong(bundle.View + (uint)GraphicsLayouts.ViewShFCprList, bundle.Shf);
            WriteLong(bundle.ViewPort + (uint)GraphicsLayouts.ViewPortDspIns, bundle.CopList);
            WriteLong(bundle.ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, bundle.RasInfo);
            WriteLong(bundle.RasInfo + (uint)GraphicsLayouts.RasInfoBitMap, bundle.BitMap);
            WriteWord(bundle.BitMap + (uint)GraphicsLayouts.BitMapBytesPerRow, 8);
            WriteWord(bundle.BitMap + (uint)GraphicsLayouts.BitMapRows, Height);
            Bus.WriteByte(bundle.BitMap + (uint)GraphicsLayouts.BitMapDepth, 1, 0);
            WriteLong(bundle.BitMap + (uint)GraphicsLayouts.BitMapPlanes, bundle.Plane);
            SeedBytes(bundle.CopList, ExpectedCopList(bundle));
            SeedBytes(bundle.Lof, ExpectedCprList(bundle));
            SeedBytes(bundle.Shf, ExpectedCprList(bundle));
            SeedBytes(bundle.Raw, ExpectedRaw());
            AssertPublishedBundle(bundle);
        }

        internal void AssertPublishedBundle(Bundle bundle)
        {
            Assert.Equal(Width, Bus.ReadWord(bundle.Screen + (uint)GraphicsLayouts.ScreenWidth));
            Assert.Equal(Height, Bus.ReadWord(bundle.Screen + (uint)GraphicsLayouts.ScreenHeight));
            Assert.Equal(bundle.ViewPort, Bus.ReadLong(bundle.View + (uint)GraphicsLayouts.ViewViewPort));
            Assert.Equal(bundle.Lof, Bus.ReadLong(bundle.View + (uint)GraphicsLayouts.ViewLoFCprList));
            Assert.Equal(bundle.Shf, Bus.ReadLong(bundle.View + (uint)GraphicsLayouts.ViewShFCprList));
            Assert.Equal(bundle.CopList, Bus.ReadLong(bundle.ViewPort + (uint)GraphicsLayouts.ViewPortDspIns));
            Assert.Equal(bundle.RasInfo, Bus.ReadLong(bundle.ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo));
            Assert.Equal(bundle.BitMap, Bus.ReadLong(bundle.RasInfo + (uint)GraphicsLayouts.RasInfoBitMap));
            Assert.Equal((ushort)8, Bus.ReadWord(bundle.BitMap + (uint)GraphicsLayouts.BitMapBytesPerRow));
            Assert.Equal(Height, Bus.ReadWord(bundle.BitMap + (uint)GraphicsLayouts.BitMapRows));
            Assert.Equal((byte)1, Bus.ReadByte(bundle.BitMap + (uint)GraphicsLayouts.BitMapDepth));
            Assert.Equal(bundle.Plane, Bus.ReadLong(bundle.BitMap + (uint)GraphicsLayouts.BitMapPlanes));
            Assert.Equal(ExpectedCopList(bundle), ReadBytes(bundle.CopList, GraphicsLayouts.CopListSize));
            Assert.Equal(ExpectedCprList(bundle), ReadBytes(bundle.Lof, GraphicsLayouts.CprListSize));
            Assert.Equal(ExpectedCprList(bundle), ReadBytes(bundle.Shf, GraphicsLayouts.CprListSize));
            Assert.Equal(new byte[(int)PlaneBytes], ReadBytes(bundle.Plane, (int)PlaneBytes));
            Assert.Equal(ExpectedRaw(), ReadBytes(bundle.Raw, (int)RawBytes));
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, 0x100),
                ReadBytes(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        private static void PoisonVolatile(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Construct() => Invoke(Image.Value.Allocator, state =>
        {
            state.D[0] = Width;
            state.D[1] = Height;
            state.D[2] = PlaneBytes;
            state.D[3] = RawBytes;
        });

        internal CallResult Teardown(Bundle bundle, ushort marker) => Invoke(Image.Value.Teardown, state =>
        {
            state.A[0] = bundle.Screen;
            state.A[1] = bundle.View;
            state.A[2] = bundle.RasInfo;
            state.A[3] = bundle.Plane;
            state.A[4] = bundle.CopList;
            state.A[5] = bundle.Lof;
            state.D[0] = bundle.Shf;
            state.D[1] = bundle.Raw;
            state.D[2] = PlaneBytes;
            state.D[3] = RawBytes;
            state.D[4] = marker;
        });

        private CallResult Invoke(int entryOffset, Action<M68kCpuState> arguments)
        {
            var entry = _nativeCodeAddress + (uint)entryOffset;
            // This helper has no public LVO or AUTOINIT slot. Call its real
            // private entry, leaving A0-A5 available for ownership inputs.
            Bus.WriteWord(CallerAddress, 0x4EB9); // JSR absolute.L
            Bus.WriteLong(CallerAddress + 2, entry);
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            arguments(_cpu.State);
            _cpu.State.A[6] = ExecBase;
            _cpu.ExecuteInstruction();
            Assert.Equal(entry, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, Bus.ReadLong(_cpu.State.A[7]));
            for (var instruction = 0; instruction < 100_000; instruction++)
            {
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return new CallResult(_cpu.State.D.ToArray(), _cpu.State.ProgramCounter,
                        _cpu.State.A[7], ReturnAddress, StackPointer);
            }
            throw new InvalidOperationException(
                $"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
