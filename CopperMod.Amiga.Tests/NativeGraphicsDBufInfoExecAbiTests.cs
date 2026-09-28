using Amiga;
using CopperMod.Amiga.CopperStart.Exec;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;
using PortableExec = global::CopperStart.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsDBufInfoExecAbiTests
{
    private const uint CapturedD0 = 0xA1B2_C3D4;
    private const uint MockAllocationBase = 0x00D0_0100;
    private const uint PrefixBytes = 4;
    private const uint RequestedBytes = PrefixBytes + (uint)GraphicsLayouts.DBufInfoSize;
    private const uint MockInfo = MockAllocationBase + PrefixBytes;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData("allocate-and-free")]
    [InlineData("seeded-free")]
    [InlineData("odd-allocation")]
    [InlineData("null-allocation")]
    [InlineData("null-free")]
    [InlineData("foreign-marker")]
    [InlineData("alloc-missing-exec")]
    [InlineData("free-missing-exec")]
    public void DBufInfoUsesExactExecArgumentsAndAtomicEnvelopePublication(string scenario)
    {
        using var fixture = new Fixture();
        var allocating = scenario is "allocate-and-free" or "odd-allocation" or
            "null-allocation" or "alloc-missing-exec";
        if (!allocating && scenario != "null-free")
            fixture.SeedInfo(MockAllocationBase);
        if (scenario == "foreign-marker")
            fixture.Bus.WriteWord(MockAllocationBase, 0x464F);
        if (scenario == "odd-allocation")
            fixture.AllocationResult = MockAllocationBase + 1;
        if (scenario == "null-allocation")
            fixture.AllocationResult = 0;
        if (scenario is "alloc-missing-exec" or "free-missing-exec")
            fixture.SetExecAvailable(false);

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(allocating ? GraphicsLvo.AllocDBufInfo : GraphicsLvo.FreeDBufInfo,
            scenario == "null-free" ? 0 : MockInfo);
        var failures = new List<string>();
        var attemptedAllocation = scenario is "allocate-and-free" or "odd-allocation" or "null-allocation";
        Check(failures, "Exec allocation", () => Assert.Equal(
            attemptedAllocation ? new[] { (fixture.AllocationResult, RequestedBytes, AllocationFlags) }
                : Array.Empty<(uint, uint, uint)>(), fixture.Allocations));
        Check(failures, "Exec FreeMem A1/D0", () => Assert.Equal(scenario switch
        {
            "seeded-free" => new[] { (MockAllocationBase, RequestedBytes) },
            "odd-allocation" => new[] { (MockAllocationBase + 1, RequestedBytes) },
            _ => Array.Empty<(uint, uint)>()
        }, fixture.Frees));
        Check(failures, "memory at retirement", () => AssertRetirementMemory(fixture, before));
        Check(failures, "publication or rollback", () =>
        {
            if (scenario == "allocate-and-free")
            {
                fixture.AssertPublishedInfo(MockAllocationBase);
                fixture.AssertOutsideMockAllocationUnchanged(before);
            }
            else
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
            }
        });
        var claimed = scenario is "allocate-and-free" or "seeded-free" or "null-free";
        Check(failures, "caller return", () => AssertReturn(result,
            scenario == "allocate-and-free" ? MockInfo : claimed ? 0u : CapturedD0, !claimed));

        if (scenario == "allocate-and-free" && !result.UsedFallback && result.Value == MockInfo)
        {
            var beforeFree = fixture.CaptureMemory();
            var freed = fixture.Invoke(GraphicsLvo.FreeDBufInfo, result.Value);
            Check(failures, "roundtrip release", () => Assert.Equal(
                (MockAllocationBase, RequestedBytes), Assert.Single(fixture.Frees)));
            Check(failures, "roundtrip allocation count", () => Assert.Single(fixture.Allocations));
            Check(failures, "roundtrip retirement memory", () => AssertRetirementMemory(fixture, beforeFree));
            Check(failures, "roundtrip guest bytes", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
            Check(failures, "FreeDBufInfo return", () => AssertReturn(freed, 0, expectFallback: false));
        }
        Assert.True(failures.Count == 0, scenario + ":\n" + string.Join("\n", failures));
    }

    [Fact]
    public void DBufInfoRoundtripReturnsAllEightyEightBytesThroughProductionExecAndCoalescesTheHeap()
    {
        using var fixture = new Fixture(productionExec: true);
        var allocationBase = fixture.FirstFreeChunk;
        var initialFreeBytes = fixture.FreeBytes;
        var before = fixture.CaptureMemory();
        var allocated = fixture.Invoke(GraphicsLvo.AllocDBufInfo);

        AssertReturn(allocated, allocationBase + PrefixBytes, expectFallback: false);
        Assert.Equal((allocationBase, RequestedBytes, AllocationFlags), Assert.Single(fixture.Allocations));
        Assert.Empty(fixture.Frees);
        Assert.Equal(88u, RequestedBytes);
        fixture.AssertPublishedInfo(allocationBase);
        fixture.AssertSingleFreeChunk(allocationBase + RequestedBytes, initialFreeBytes - RequestedBytes);
        fixture.AssertNonHeapMemoryUnchanged(before);

        // This is the native allocation's unmodified header and public
        // pointer. ExecMemoryServices consumes real A1/D0, not an adapter
        // that rearranges legacy D0/D1 staging into a successful release.
        var beforeFree = fixture.CaptureMemory();
        var freed = fixture.Invoke(GraphicsLvo.FreeDBufInfo, allocated.Value);

        AssertReturn(freed, 0, expectFallback: false);
        Assert.Equal((allocationBase, RequestedBytes), Assert.Single(fixture.Frees));
        Assert.Single(fixture.Allocations);
        AssertRetirementMemory(fixture, beforeFree);
        fixture.AssertSingleFreeChunk(allocationBase, initialFreeBytes);
        fixture.AssertNonHeapMemoryUnchanged(before);
    }

    private static byte[] ExpectedEnvelope()
    {
        var expected = new byte[checked((int)RequestedBytes)];
        expected[0] = 0x44;
        expected[1] = 0x42;
        foreach (var message in new[] { GraphicsLayouts.DBufInfoSafeMessage, GraphicsLayouts.DBufInfoDispMessage })
        {
            var length = (int)PrefixBytes + message + GraphicsLayouts.ExecMessageLength;
            expected[length] = (byte)(GraphicsLayouts.ExecMessageSize >> 8);
            expected[length + 1] = (byte)GraphicsLayouts.ExecMessageSize;
        }
        return expected;
    }

    private static void AssertRetirementMemory(Fixture fixture, MemorySnapshot expected)
    {
        Assert.Equal(fixture.Frees.Count, fixture.MemoryAtFrees.Count);
        foreach (var snapshot in fixture.MemoryAtFrees)
            AssertMemoryEqual(expected, snapshot);
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.Structures, actual.Structures);
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
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] Structures, byte[] GraphicsBase);
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
        private const uint StructuresAddress = 0x00D1_0000;
        private const uint ViewPort = StructuresAddress;
        private const uint RasInfo = StructuresAddress + 0x60;
        private const int StructuresSize = 0x100;
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
            Bus.MapWritableMemory(StructuresAddress, new byte[StructuresSize]);
            Bus.WriteLong(ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, RasInfo);
            Bus.WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
            Bus.WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
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
                if (AllocationResult != 0 && (AllocationResult & 1) == 0)
                {
                    Assert.InRange(AllocationResult, HeapAddress, HeapAddress + (uint)HeapSize - 1);
                    Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - AllocationResult);
                    if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                        Bus.ClearMemory(AllocationResult, checked((int)size));
                }
                // The deliberately odd provider does not clear its block;
                // no header byte may be published before rollback.
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
                ref _platform, ExecBase, (uint)HeapSize, ExecApi.MemoryFlags.Public, 0, HeapAddress, APTR.Null);
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

        internal void SeedInfo(uint address)
        {
            var envelope = ExpectedEnvelope();
            for (var index = 0; index < envelope.Length; index++)
                Bus.WriteByte(address + (uint)index, envelope[index], 0);
        }

        internal void AssertPublishedInfo(uint address)
        {
            Assert.Equal(88u, RequestedBytes);
            Assert.Equal(20, GraphicsLayouts.ExecMessageSize);
            Assert.Equal(ExpectedEnvelope(), ReadBytes(address, (int)RequestedBytes));
        }

        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(StructuresAddress, StructuresSize),
                ReadBytes(GraphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void AssertOutsideMockAllocationUnchanged(MemorySnapshot before)
        {
            var after = CaptureMemory();
            var start = (int)(MockAllocationBase - HeapAddress);
            var end = start + (int)RequestedBytes;
            Assert.Equal(before.Heap.Take(start), after.Heap.Take(start));
            Assert.Equal(before.Heap.Skip(end), after.Heap.Skip(end));
            AssertNonHeapMemoryUnchanged(before);
        }

        internal void AssertNonHeapMemoryUnchanged(MemorySnapshot before)
        {
            var after = CaptureMemory();
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.Structures, after.Structures);
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

        internal CallResult Invoke(GraphicsLvo vector, uint dbufInfo = 0)
        {
            var entry = CodeAddress + (uint)Image.Value.Entries[vector];
            var vectorAddress = checked((uint)((long)GraphicsBase + (int)vector));
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE);
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.A[0] = vector == GraphicsLvo.AllocDBufInfo ? ViewPort : 0xA0A0_A0A1;
            _cpu.State.A[1] = vector == GraphicsLvo.FreeDBufInfo ? dbufInfo : 0xA1A1_A1A1;
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
                    (Bus.ReadWord(pc) == 0x4E75 && _cpu.State.D[0] == CapturedD0);
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;
                Assert.True(enteredBody, "The DBufInfo public vector did not reach its native body.");
                // This unit proves the allocator-call ABI only. Full public
                // D2-D7/A2-A6 preservation is intentionally a later gate.
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
