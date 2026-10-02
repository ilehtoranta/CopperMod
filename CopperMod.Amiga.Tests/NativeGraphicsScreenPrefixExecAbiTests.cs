using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsScreenPrefixExecAbiTests
{
    private const uint HeapAddress = 0x00D0_0000;
    private const int HeapSize = 0x1000;
    private const int RealHeapSize = 0x200;
    private const uint HeaderOccupiedBytes = 392;
    private const ushort Width = 64;
    private const ushort Height = 32;
    private const uint PlaneBytes = 256;
    private const uint Screen = HeapAddress + 0x200;
    private const uint View = HeapAddress + 0x400;
    private const uint RasInfo = HeapAddress + 0x500;
    private const uint Plane = HeapAddress + 0x600;
    private const uint PublicClear = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private const uint ChipClear = PublicClear | (uint)ExecApi.MemoryFlags.Chip;
    private static readonly uint[] MockAddresses = { Screen, View, RasInfo, Plane };
    private static readonly (uint Size, uint Flags)[] Requests =
    {
        ((uint)GraphicsLayouts.ScreenSize, PublicClear), ((uint)GraphicsLayouts.ViewSize, PublicClear),
        ((uint)GraphicsLayouts.RasInfoSize, PublicClear), (PlaneBytes, ChipClear)
    };
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(
            out var entries, out var fallback, out _, out _, out _, out _,
            out _, out _, out _, out _, out _, out var allocator);
        return new NativeImage(code, entries, fallback, allocator);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[] { "success", "null-screen", "null-view", "null-rasinfo", "null-plane", "odd-plane" })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void ScreenPrefixPrivateAllocatorUsesActualExecCleanupAbiAcrossEveryFailurePath(bool relocated, string scenario)
    {
        using var fixture = new Fixture(relocated);
        fixture.NullAllocationSlot = scenario switch
        {
            "null-screen" => 1,
            "null-view" => 2,
            "null-rasinfo" => 3,
            "null-plane" => 4,
            _ => 0
        };
        fixture.ReturnOddPlane = scenario == "odd-plane";
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke();
        var failures = new List<string>();
        var results = MockAddresses.ToArray();
        if (fixture.NullAllocationSlot != 0)
            results[fixture.NullAllocationSlot - 1] = 0;
        if (fixture.ReturnOddPlane)
            results[3]++;
        var callCount = fixture.NullAllocationSlot == 0 ? 4 : fixture.NullAllocationSlot;
        var expectedAllocations = Requests.Take(callCount)
            .Select((request, index) => (Address: results[index], request.Size, request.Flags)).ToArray();
        var expectedFrees = scenario == "success" ? Array.Empty<(uint Address, uint Size)>() :
            expectedAllocations.Where(call => call.Address != 0).Reverse()
                .Select(call => (call.Address, call.Size)).ToArray();
        Check(failures, "Exec AllocMem D0/D1", () => Assert.Equal(expectedAllocations, fixture.Allocations));
        Check(failures, "Exec FreeMem A1/D0 and cleanup order", () => Assert.Equal(expectedFrees, fixture.Frees));

        // Allocator CLEAR is legitimate, including the intentionally odd
        // mocked plane. The native constructor must not publish anything
        // before all four calls, or after rejecting that odd plane.
        var expected = before;
        var beforeCalls = new List<MemorySnapshot>();
        var afterCalls = new List<MemorySnapshot>();
        foreach (var allocation in expectedAllocations)
        {
            beforeCalls.Add(expected);
            if (allocation.Address != 0)
                expected = WithClearedBlock(expected, allocation.Address, allocation.Size);
            afterCalls.Add(expected);
        }
        Check(failures, "no native publication before allocation completes", () =>
            AssertSnapshots(beforeCalls, fixture.MemoryAtAllocations));
        Check(failures, "allocator-owned CLEAR boundaries", () =>
            AssertSnapshots(afterCalls, fixture.MemoryAfterAllocations));
        Check(failures, "private D0/PC/SP/A6 return", () =>
            AssertReturn(result, scenario == "success" ? Screen : 0u));

        if (scenario == "success")
        {
            Check(failures, "unaltered constructor outputs", () => Assert.Equal(MockAddresses, result.Data.Take(4)));
            Check(failures, "published Screen/View/RasInfo/plane", fixture.AssertPublishedPrefix);
            Check(failures, "constructor guards and protected memory", () =>
                AssertOutsideAllocationsUnchanged(before, fixture.CaptureMemory(), expectedAllocations));
            Check(failures, "no success retirement", () => Assert.Empty(fixture.MemoryAtFrees));
        }
        else
        {
            Check(failures, "no publication or mutation at any retirement", () =>
            {
                Assert.Equal(expectedFrees.Length, fixture.MemoryAtFrees.Count);
                foreach (var snapshot in fixture.MemoryAtFrees)
                    AssertMemoryEqual(expected, snapshot);
            });
            Check(failures, "final unpublished bytes and guards", () => AssertMemoryEqual(expected, fixture.CaptureMemory()));
        }
        if (scenario == "odd-plane")
        {
            // Constructor cleanup recovers View into A1. Both preceding
            // releases may clobber A1; the release adapter must restore it
            // so the third call can still load this exact View pointer.
            Check(failures, "View survives two earlier volatile Exec calls", () =>
            {
                Assert.Equal(4, fixture.Frees.Count);
                Assert.Equal((View, (uint)GraphicsLayouts.ViewSize), fixture.Frees[2]);
            });
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScreenPrefixPrivateAllocatorRollsBackRealHeadersAfterNaturalClassicHeapExhaustion(bool relocated)
    {
        using var fixture = new Fixture(relocated, productionExec: true);
        var screen = fixture.FirstFreeChunk;
        var initialFreeBytes = fixture.FreeBytes;
        Assert.Equal(HeaderOccupiedBytes, Requests.Take(3).Aggregate(0u, (sum, request) => sum + Rounded(request.Size)));
        Assert.InRange(initialFreeBytes, HeaderOccupiedBytes + 1, HeaderOccupiedBytes + PlaneBytes - 1);
        var view = screen + Rounded(Requests[0].Size);
        var rasInfo = view + Rounded(Requests[1].Size);
        var addresses = new[] { screen, view, rasInfo };
        var expectedAllocations = new[]
        {
            (screen, Requests[0].Size, PublicClear), (view, Requests[1].Size, PublicClear),
            (rasInfo, Requests[2].Size, PublicClear), (0u, PlaneBytes, ChipClear)
        };
        var expectedFrees = new[]
        {
            (Address: rasInfo, Size: Requests[2].Size), (Address: view, Size: Requests[1].Size),
            (Address: screen, Size: Requests[0].Size)
        };
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke();

        AssertReturn(result, 0);
        Assert.Equal(expectedAllocations, fixture.Allocations);
        Assert.Equal(expectedFrees, fixture.Frees);
        Assert.Equal(4, fixture.MemoryAtAllocations.Count);
        Assert.Equal(4, fixture.MemoryAfterAllocations.Count);
        Assert.Equal(3, fixture.MemoryAtFrees.Count);
        AssertMemoryEqual(before, fixture.MemoryAtAllocations[0]);
        var beforeRollback = fixture.MemoryAfterAllocations[3];
        AssertMemoryEqual(fixture.MemoryAtAllocations[3], beforeRollback); // actual NULL allocation changed no heap bytes
        AssertMemoryEqual(beforeRollback, fixture.MemoryAtFrees[0]); // no constructor publication after allocation failure
        for (var block = 0; block < addresses.Length; block++)
            Assert.Equal(new byte[(int)Requests[block].Size], HeapBytes(beforeRollback, addresses[block], Requests[block].Size));

        var freeAddress = screen + HeaderOccupiedBytes;
        var freeBytes = initialFreeBytes - HeaderOccupiedBytes;
        for (var release = 0; release < expectedFrees.Length; release++)
        {
            var snapshot = fixture.MemoryAtFrees[release];
            AssertSingleFreeChunk(snapshot, freeAddress, freeBytes);
            AssertProtectedMemoryEqual(before, snapshot);
            for (var block = 0; block < addresses.Length - release; block++)
                Assert.Equal(HeapBytes(beforeRollback, addresses[block], Rounded(Requests[block].Size)),
                    HeapBytes(snapshot, addresses[block], Rounded(Requests[block].Size)));
            Assert.Equal(freeAddress, expectedFrees[release].Address + Rounded(expectedFrees[release].Size));
            freeAddress = expectedFrees[release].Address;
            freeBytes += Rounded(expectedFrees[release].Size);
        }
        var after = fixture.CaptureMemory();
        Assert.Equal(screen, freeAddress);
        Assert.Equal(initialFreeBytes, freeBytes);
        AssertSingleFreeChunk(after, screen, initialFreeBytes);
        AssertProtectedMemoryEqual(before, after);
        // The real 512-byte PUBLIC|CHIP heap fits the three headers but not
        // the plane. No injected NULL or repaired FreeMem arguments are used.
        // Released headers become MemChunks; only still-owned rounded blocks
        // stay immutable while adjacent frees coalesce into the original heap.
    }

    private static uint Rounded(uint size) => checked((size + 7) & ~7u);

    private static MemorySnapshot WithClearedBlock(MemorySnapshot source, uint address, uint size)
    {
        var heap = source.Heap.ToArray();
        heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)size)).Clear();
        return source with { Heap = heap };
    }

    private static byte[] HeapBytes(MemorySnapshot snapshot, uint address, uint size)
        => snapshot.Heap.AsSpan(checked((int)(address - HeapAddress)), checked((int)size)).ToArray();

    private static void AssertOutsideAllocationsUnchanged(MemorySnapshot before, MemorySnapshot after,
        IEnumerable<(uint Address, uint Size, uint Flags)> allocations)
    {
        var expected = before.Heap.ToArray();
        foreach (var allocation in allocations)
        {
            var offset = checked((int)(allocation.Address - HeapAddress));
            after.Heap.AsSpan(offset, checked((int)allocation.Size)).CopyTo(expected.AsSpan(offset));
        }
        Assert.Equal(expected, after.Heap);
        AssertNonHeapMemoryEqual(before, after);
    }

    private static void AssertSingleFreeChunk(MemorySnapshot snapshot, uint address, uint size)
    {
        Assert.Equal(address, BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.First)));
        Assert.Equal(size, BinaryPrimitives.ReadUInt32BigEndian(snapshot.Heap.AsSpan((int)ExecLayout.MemHeader.Free)));
        var chunk = HeapBytes(snapshot, address, 8);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(chunk.AsSpan((int)ExecLayout.MemChunk.Next)));
        Assert.Equal(size, BinaryPrimitives.ReadUInt32BigEndian(chunk.AsSpan((int)ExecLayout.MemChunk.Bytes)));
    }

    private static void AssertSnapshots(IReadOnlyList<MemorySnapshot> expected, IReadOnlyList<MemorySnapshot> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
            AssertMemoryEqual(expected[index], actual[index]);
    }

    private static void AssertProtectedMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        AssertNonHeapMemoryEqual(expected, actual);
        Assert.Equal(expected.Heap.AsSpan(RealHeapSize).ToArray(), actual.Heap.AsSpan(RealHeapSize).ToArray());
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
        Assert.Equal(Fixture.ExecBase, result.ExecBase);
        Assert.Equal(result.CallerReturnAddress, result.ProgramCounter);
        Assert.Equal(result.CallerStackPointer, result.StackPointer);
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback, int Allocator);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsImage);
    private sealed record CallResult(uint[] Data, uint ExecBase, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer);

    private sealed class Fixture : IDisposable
    {
        internal const uint ExecBase = 0x0077_0000;
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
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
        internal int NullAllocationSlot { get; set; }
        internal bool ReturnOddPlane { get; set; }
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAfterAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal uint FirstFreeChunk => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.First);
        internal uint FreeBytes => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.Free);

        private void InstallMockExec()
        {
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var index = Allocations.Count;
                Assert.InRange(index, 0, 3);
                var address = index + 1 == NullAllocationSlot ? 0u : MockAddresses[index];
                if (index == 3 && ReturnOddPlane)
                    address++;
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((address, size, flags));
                MemoryAtAllocations.Add(CaptureMemory());
                if (address != 0)
                {
                    Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - address);
                    if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                        Bus.ClearMemory(address, checked((int)size));
                }
                MemoryAfterAllocations.Add(CaptureMemory());
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
                ref _platform, ExecBase, (uint)RealHeapSize, ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip,
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
                MemoryAtAllocations.Add(CaptureMemory());
                services.AllocMem(state);
                MemoryAfterAllocations.Add(CaptureMemory());
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

        internal void AssertPublishedPrefix()
        {
            var viewPort = Screen + (uint)GraphicsLayouts.ScreenViewPort;
            var bitMap = Screen + (uint)GraphicsLayouts.ScreenBitMap;
            Assert.Equal(Width, Bus.ReadWord(Screen + (uint)GraphicsLayouts.ScreenWidth));
            Assert.Equal(Height, Bus.ReadWord(Screen + (uint)GraphicsLayouts.ScreenHeight));
            Assert.Equal(viewPort, Bus.ReadLong(View + (uint)GraphicsLayouts.ViewViewPort));
            Assert.Equal(RasInfo, Bus.ReadLong(viewPort + (uint)GraphicsLayouts.ViewPortRasInfo));
            Assert.Equal(bitMap, Bus.ReadLong(RasInfo + (uint)GraphicsLayouts.RasInfoBitMap));
            Assert.Equal(bitMap, Bus.ReadLong(Screen + (uint)GraphicsLayouts.ScreenRastPort + (uint)GraphicsLayouts.RastPortBitMap));
            Assert.Equal((ushort)8, Bus.ReadWord(bitMap + (uint)GraphicsLayouts.BitMapBytesPerRow));
            Assert.Equal(Height, Bus.ReadWord(bitMap + (uint)GraphicsLayouts.BitMapRows));
            Assert.Equal((byte)1, Bus.ReadByte(bitMap + (uint)GraphicsLayouts.BitMapDepth));
            Assert.Equal(Plane, Bus.ReadLong(bitMap + (uint)GraphicsLayouts.BitMapPlanes));
            Assert.Equal(new byte[(int)PlaneBytes], ReadBytes(Plane, (int)PlaneBytes));
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

        internal CallResult Invoke()
        {
            var entry = _nativeCodeAddress + (uint)Image.Value.Allocator;
            Bus.WriteWord(CallerAddress, 0x4EB9); // genuine private JSR absolute.L, not a fabricated LVO
            Bus.WriteLong(CallerAddress + 2, entry);
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = Width;
            _cpu.State.D[1] = Height;
            _cpu.State.D[2] = PlaneBytes;
            _cpu.State.A[6] = ExecBase;
            _cpu.ExecuteInstruction();
            Assert.Equal(entry, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, Bus.ReadLong(_cpu.State.A[7]));
            for (var instruction = 0; instruction < 100_000; instruction++)
            {
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return new CallResult(_cpu.State.D.ToArray(), _cpu.State.A[6], _cpu.State.ProgramCounter,
                        _cpu.State.A[7], ReturnAddress, StackPointer);
            }
            throw new InvalidOperationException(
                $"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
