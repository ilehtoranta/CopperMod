using System.Buffers.Binary;
using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsBitMapExecAbiTests
{
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const ushort Width = 17;
    private const ushort Height = 3;
    private const uint PrefixBytes = 16;
    private const uint HeaderAndBitMapBytes = 56;
    private const uint PlaneBytes = 12;
    private const uint MockAllocationBase = 0x00D0_0100;
    private const uint MockBitMap = MockAllocationBase + PrefixBytes;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public |
        ExecApi.MemoryFlags.Chip | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    public static IEnumerable<object[]> MockCases()
    {
        foreach (var (depth, requested) in new[] { (1, 68u), (2, 80u), (4, 104u), (8, 152u) })
        foreach (var scenario in new[] { "allocate-and-free", "seeded-free", "odd-allocation" })
            yield return new object[] { depth, requested, scenario };

        foreach (var scenario in new[]
        {
            "null-allocation", "null-free", "alloc-missing-exec", "free-missing-exec", "corrupt-plane"
        })
            yield return new object[] { 8, 152u, scenario };
    }

    [Theory]
    [MemberData(nameof(MockCases))]
    public void BitMapUsesActualExecArgumentsAndPreservesPublicationBoundaries(
        int depth, uint requested, string scenario)
    {
        using var fixture = new Fixture();
        var allocating = scenario is "allocate-and-free" or "odd-allocation" or
            "null-allocation" or "alloc-missing-exec";
        if (!allocating && scenario != "null-free")
            fixture.SeedBitMap(MockAllocationBase, depth, requested);
        if (scenario == "corrupt-plane")
        {
            var lastPlane = MockBitMap + (uint)GraphicsLayouts.BitMapPlanes + (uint)((depth - 1) * 4);
            fixture.Bus.WriteLong(lastPlane, fixture.Bus.ReadLong(lastPlane) + 2);
        }
        if (scenario == "odd-allocation")
            fixture.AllocationResult = MockAllocationBase + 1;
        if (scenario == "null-allocation")
            fixture.AllocationResult = 0;
        if (scenario is "alloc-missing-exec" or "free-missing-exec")
            fixture.SetExecAvailable(false);

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(allocating ? GraphicsLvo.AllocBitMap : GraphicsLvo.FreeBitMap,
            depth, scenario == "null-free" ? 0 : MockBitMap);
        var failures = new List<string>();
        var attemptedAllocation = scenario is "allocate-and-free" or "odd-allocation" or "null-allocation";
        Check(failures, "Exec AllocMem D0/D1", () => Assert.Equal(
            attemptedAllocation ? new[] { (fixture.AllocationResult, requested, AllocationFlags) }
                : Array.Empty<(uint, uint, uint)>(), fixture.Allocations));
        Check(failures, "memory before allocation", () =>
            AssertBoundaryMemory(fixture.MemoryAtAllocations, fixture.Allocations.Count, before));
        Check(failures, "Exec FreeMem A1/D0", () => Assert.Equal(scenario switch
        {
            "seeded-free" => new[] { (MockAllocationBase, requested) },
            "odd-allocation" => new[] { (MockAllocationBase + 1, requested) },
            _ => Array.Empty<(uint, uint)>()
        }, fixture.Frees));
        Check(failures, "memory at retirement", () =>
            AssertBoundaryMemory(fixture.MemoryAtFrees, fixture.Frees.Count, before));
        Check(failures, "publication or rollback", () =>
        {
            if (scenario == "allocate-and-free")
            {
                fixture.AssertPublishedBitMap(MockAllocationBase, depth, requested);
                fixture.AssertOutsideMockAllocationUnchanged(before, requested);
            }
            else
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
            }
        });
        var claimed = scenario is "allocate-and-free" or "seeded-free" or "null-free";
        var originalD0 = allocating ? Width : CapturedFreeD0;
        Check(failures, "caller return", () => AssertReturn(result,
            scenario == "allocate-and-free" ? MockBitMap : claimed ? 0u : originalD0, !claimed));

        if (scenario == "allocate-and-free" && !result.UsedFallback && result.Value == MockBitMap)
        {
            // Retire exactly what the constructor published. In particular,
            // do not repair its private header or plane pointers for Free.
            var beforeFree = fixture.CaptureMemory();
            var freed = fixture.Invoke(GraphicsLvo.FreeBitMap, depth, result.Value);
            Check(failures, "roundtrip FreeMem A1/D0", () => Assert.Equal(
                (MockAllocationBase, requested), Assert.Single(fixture.Frees)));
            Check(failures, "roundtrip allocation count", () => Assert.Single(fixture.Allocations));
            Check(failures, "roundtrip retirement memory", () =>
                AssertBoundaryMemory(fixture.MemoryAtFrees, fixture.Frees.Count, beforeFree));
            Check(failures, "roundtrip guest bytes", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
            Check(failures, "FreeBitMap return", () => AssertReturn(freed, 0, expectFallback: false));
        }
        Assert.True(failures.Count == 0,
            $"depth={depth}, {scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(1, 68u, 72u)]
    [InlineData(2, 80u, 80u)]
    [InlineData(4, 104u, 104u)]
    [InlineData(8, 152u, 152u)]
    public void BitMapRoundtripReturnsExactSpanThroughProductionExecAndCoalescesTheHeap(
        int depth, uint requested, uint occupied)
    {
        using var fixture = new Fixture(productionExec: true);
        var allocationBase = fixture.FirstFreeChunk;
        var initialFreeBytes = fixture.FreeBytes;
        var before = fixture.CaptureMemory();
        var allocated = fixture.Invoke(GraphicsLvo.AllocBitMap, depth);

        AssertReturn(allocated, allocationBase + PrefixBytes, expectFallback: false);
        Assert.Equal((allocationBase, requested, AllocationFlags), Assert.Single(fixture.Allocations));
        Assert.Empty(fixture.Frees);
        AssertBoundaryMemory(fixture.MemoryAtAllocations, fixture.Allocations.Count, before);
        fixture.AssertPublishedBitMap(allocationBase, depth, requested);
        fixture.AssertSingleFreeChunk(allocationBase + occupied, initialFreeBytes - occupied);
        fixture.AssertNonHeapMemoryUnchanged(before);
        for (var offset = requested; offset < occupied; offset++)
            Assert.Equal((byte)0xA5, fixture.Bus.ReadByte(allocationBase + offset));

        // The real service and classic allocator receive unmodified A1/D0.
        // Depth one occupies 72 heap bytes but must release the request of 68.
        var beforeFree = fixture.CaptureMemory();
        var freed = fixture.Invoke(GraphicsLvo.FreeBitMap, depth, allocated.Value);

        AssertReturn(freed, 0, expectFallback: false);
        Assert.Equal((allocationBase, requested), Assert.Single(fixture.Frees));
        Assert.Single(fixture.Allocations);
        AssertBoundaryMemory(fixture.MemoryAtFrees, fixture.Frees.Count, beforeFree);
        fixture.AssertSingleFreeChunk(allocationBase, initialFreeBytes);
        fixture.AssertNonHeapMemoryUnchanged(before);
    }

    private static byte[] ExpectedEnvelope(uint allocationBase, int depth, uint requested)
    {
        Assert.Equal(40, GraphicsLayouts.BitMapSize);
        Assert.Equal(HeaderAndBitMapBytes + PlaneBytes * (uint)depth, requested);
        var expected = new byte[checked((int)requested)];
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(0), 0x424D);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(2), Width);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(4), Height);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(6), checked((ushort)depth));
        BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(8), requested);
        var publicOffset = (int)PrefixBytes;
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapBytesPerRow), 4);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapRows), Height);
        expected[publicOffset + GraphicsLayouts.BitMapFlags] = 0x0A; // STANDARD | DISPLAYABLE
        expected[publicOffset + GraphicsLayouts.BitMapDepth] = checked((byte)depth);
        for (var plane = 0; plane < depth; plane++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                expected.AsSpan(publicOffset + GraphicsLayouts.BitMapPlanes + plane * 4),
                allocationBase + HeaderAndBitMapBytes + PlaneBytes * (uint)plane);
        }
        // Reserved prefix bytes, Pad, unused plane slots, and all plane
        // payloads must remain clear after the constructor publishes.
        return expected;
    }

    private static void AssertBoundaryMemory(
        IReadOnlyCollection<MemorySnapshot> snapshots, int expectedCount, MemorySnapshot expected)
    {
        Assert.Equal(expectedCount, snapshots.Count);
        foreach (var snapshot in snapshots)
            AssertMemoryEqual(expected, snapshot);
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsBase, actual.GraphicsBase);
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertReturn(CallResult result, uint expectedD0, bool expectFallback)
    {
        Assert.Equal(expectedD0, result.Value);
        Assert.Equal(expectFallback, result.UsedFallback);
        Assert.Equal(result.CallerReturnAddress, result.ProgramCounter);
        Assert.Equal(result.CallerStackPointer, result.StackPointer);
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsBase);
    private sealed record CallResult(
        uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer);

    private sealed class Fixture : IDisposable
    {
        private const uint CodeAddress = 0x0040_0000;
        private const uint GraphicsBase = 0x0070_0000;
        private const uint ResidentAddress = 0x0072_0000;
        private const uint ExecBase = 0x0077_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint ReturnAddress = CallerAddress + 4;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private const uint HeapAddress = 0x00D0_0000;
        private const int HeapSize = 0x1000;
        private const int LowMemorySize = 0x100;
        private readonly IM68kCore _cpu;
        private AmigaBusExecMemoryPlatform _platform;

        internal Fixture(bool productionExec = false)
        {
            var entries = Image.Value.Entries.ToDictionary(entry => entry.Key,
                entry => CodeAddress + (uint)entry.Value);
            var fallback = CodeAddress + (uint)Image.Value.Fallback;
            var library = NativeGraphicsLibraryImageBuilder.Build(
                GraphicsBase, ResidentAddress, fallback, entries, fallback);
            Assert.True((ulong)CodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
            Bus.MapWritableMemory(CodeAddress, Image.Value.Code);
            Bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
            Bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
            Bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
            var heap = new byte[HeapSize];
            Array.Fill(heap, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            for (var index = 0; index < LowMemorySize; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            SetExecAvailable(true);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            if (productionExec)
                InstallProductionExec();
            else
                InstallMockExec();
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        internal AmigaBus Bus { get; } = new();
        internal uint AllocationResult { get; set; } = MockAllocationBase;
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal uint FirstFreeChunk => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.First);
        internal uint FreeBytes => Bus.ReadLong(HeapAddress + (uint)ExecLayout.MemHeader.Free);
        internal void SetExecAvailable(bool available) => Bus.WriteLong(4, available ? ExecBase : 0);

        private void InstallMockExec()
        {
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((AllocationResult, size, flags));
                MemoryAtAllocations.Add(CaptureMemory());
                if (AllocationResult != 0 && (AllocationResult & 1) == 0)
                {
                    Assert.InRange(AllocationResult, HeapAddress, HeapAddress + (uint)HeapSize - 1);
                    Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - AllocationResult);
                    if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                        Bus.ClearMemory(AllocationResult, checked((int)size));
                }
                // A deliberately odd provider leaves its A5-filled block
                // untouched: native rollback may not publish any header.
                state.D[0] = AllocationResult;
                PoisonVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatileRegisters(state, preserveD0: false);
            });
        }

        private void InstallProductionExec()
        {
            Bus.MapWritableMemory(ExecBase, new byte[0x1000]);
            _platform = new AmigaBusExecMemoryPlatform(Bus, () => 0, ThrowAlert, null,
                (_, _, _, _, _) => MemoryHandlerResult.DidNothing, _ => { });
            PortableExec.ExecListCore.Initialize(ref _platform, ExecBase + (uint)ExecLayout.ExecBase.MemList);
            PortableExec.ExecMemoryCore.AddMemList<AmigaBusExecMemoryPlatform, PortableExec.ClassicPolicy>(
                ref _platform, ExecBase, (uint)HeapSize,
                ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip, 0, HeapAddress, APTR.Null);
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
                PoisonVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0]));
                MemoryAtFrees.Add(CaptureMemory());
                services.FreeMem(state);
                PoisonVolatileRegisters(state, preserveD0: false);
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

        internal void AssertSingleFreeChunk(uint address, uint bytes)
        {
            Assert.Equal(bytes, FreeBytes);
            Assert.Equal(address, FirstFreeChunk);
            Assert.Equal(0u, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Next));
            Assert.Equal(bytes, Bus.ReadLong(address + (uint)ExecLayout.MemChunk.Bytes));
        }

        internal void SeedBitMap(uint address, int depth, uint requested)
        {
            var envelope = ExpectedEnvelope(address, depth, requested);
            for (var index = 0; index < envelope.Length; index++)
                Bus.WriteByte(address + (uint)index, envelope[index], 0);
        }

        internal void AssertPublishedBitMap(uint address, int depth, uint requested)
            => Assert.Equal(ExpectedEnvelope(address, depth, requested), ReadBytes(address, (int)requested));

        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(GraphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void AssertOutsideMockAllocationUnchanged(MemorySnapshot before, uint requested)
        {
            var after = CaptureMemory();
            var start = (int)(MockAllocationBase - HeapAddress);
            var end = start + (int)requested;
            Assert.Equal(before.Heap.Take(start), after.Heap.Take(start));
            Assert.Equal(before.Heap.Skip(end), after.Heap.Skip(end));
            AssertNonHeapMemoryUnchanged(before);
        }

        internal void AssertNonHeapMemoryUnchanged(MemorySnapshot before)
        {
            var after = CaptureMemory();
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsBase, after.GraphicsBase);
        }

        private static void PoisonVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(GraphicsLvo vector, int depth, uint bitMap = 0)
        {
            var entry = CodeAddress + (uint)Image.Value.Entries[vector];
            var vectorAddress = checked((uint)((long)GraphicsBase + (int)vector));
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE); // JSR d16(A6), not a synthetic vector offset
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            var originalD0 = vector == GraphicsLvo.AllocBitMap ? Width : CapturedFreeD0;
            _cpu.State.D[0] = originalD0;
            _cpu.State.D[1] = Height;
            _cpu.State.D[2] = (uint)depth;
            _cpu.State.D[3] = 3; // BMF_CLEAR | BMF_DISPLAYABLE
            _cpu.State.A[0] = vector == GraphicsLvo.AllocBitMap ? 0 : bitMap;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            _cpu.State.A[6] = GraphicsBase;
            _cpu.ExecuteInstruction();
            Assert.Equal(vectorAddress, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, Bus.ReadLong(_cpu.State.A[7]));
            var usedFallback = false;
            var enteredBody = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                enteredBody |= pc == entry;
                usedFallback |= pc == CodeAddress + (uint)Image.Value.Fallback ||
                    (Bus.ReadWord(pc) == 0x4E75 && _cpu.State.D[0] == originalD0);
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;
                Assert.True(enteredBody, "The BitMap public vector did not reach its native body.");
                // Each independent caller supplies its own graphics A6.
                // This gate covers Exec-call ABI, not public callee-saves
                // or transparent volatile arguments at provider handoff.
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter,
                    _cpu.State.A[7], ReturnAddress, StackPointer);
            }
            throw new InvalidOperationException(
                $"{vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
