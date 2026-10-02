using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsBitMapPublicAbiTests
{
    private const ushort Width = 17;
    private const ushort Height = 3;
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const uint AllocationBase = 0x00D0_0100;
    private const uint PrefixBytes = 16;
    private const uint HeaderAndBitMapBytes = 56;
    private const uint PlaneBytes = 12;
    private const uint BitMap = AllocationBase + PrefixBytes;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public |
        ExecApi.MemoryFlags.Chip | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> PublicAbiCases()
    {
        var depthScenarios = new[]
        {
            "alloc-success", "alloc-null-result", "alloc-odd-result", "alloc-missing-exec",
            "free-owned", "free-missing-exec", "free-corrupt-plane"
        };
        var earlyScenarios = new[]
        {
            "alloc-friend", "alloc-unsupported-flags", "free-null", "free-foreign-marker"
        };
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        {
            foreach (var (depth, requested) in new[] { (1, 68u), (2, 80u), (4, 104u), (8, 152u) })
            foreach (var scenario in depthScenarios)
                yield return new object[] { relocated, autoInitEntry, depth, requested, scenario };

            foreach (var scenario in earlyScenarios)
                yield return new object[] { relocated, autoInitEntry, 8, 152u, scenario };
        }
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void BitMapPublicEntriesPreserveCalleeSavedRegistersAcrossImagesDepthsAndExitPaths(
        bool relocated, bool autoInitEntry, int depth, uint requested, string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var allocating = scenario.StartsWith("alloc-", StringComparison.Ordinal);
        var vector = allocating ? GraphicsLvo.AllocBitMap : GraphicsLvo.FreeBitMap;
        var a0 = allocating ? 0u : BitMap;
        var bitmapFlags = 3u; // BMF_CLEAR | BMF_DISPLAYABLE
        if ((!allocating && scenario != "free-null") || scenario == "alloc-friend")
            fixture.SeedBitMap(depth, requested);
        switch (scenario)
        {
            case "alloc-null-result":
                fixture.AllocationResult = 0;
                break;
            case "alloc-odd-result":
                fixture.AllocationResult = AllocationBase + 1;
                break;
            case "alloc-missing-exec":
            case "free-missing-exec":
                fixture.SetExecAvailable(false);
                break;
            case "alloc-friend":
                a0 = BitMap;
                break;
            case "alloc-unsupported-flags":
                bitmapFlags = 7; // INTERLEAVED remains outside the native ladder.
                break;
            case "free-null":
                a0 = 0;
                break;
            case "free-corrupt-plane":
                fixture.WriteLong(BitMap + (uint)GraphicsLayouts.BitMapPlanes + (uint)((depth - 1) * 4),
                    AllocationBase + HeaderAndBitMapBytes + PlaneBytes * (uint)(depth - 1) + 2);
                break;
            case "free-foreign-marker":
                // A foreign prefix has no native private depth. This reaches
                // the one-plane marker guard after the depth ladder declines.
                // A wrong marker with a matching native depth is a separate
                // ownership-admission bug, not part of this public-frame gate.
                fixture.WriteWord(AllocationBase, 0x464F);
                fixture.WriteWord(AllocationBase + 6, 0);
                break;
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(vector, depth, bitmapFlags, a0);
        var claimed = scenario is "alloc-success" or "free-owned" or "free-null";
        var originalD0 = allocating ? Width : CapturedFreeD0;
        var expectedD0 = scenario == "alloc-success" ? BitMap : claimed ? 0u : originalD0;
        var failures = new List<string>();

        // Report allocator/publication failures independently of changed
        // saved registers, including both calls of a constructor roundtrip.
        Check(failures, "Exec allocation", () => Assert.Equal(
            scenario is "alloc-success" or "alloc-null-result" or "alloc-odd-result"
                ? new[] { (fixture.AllocationResult, requested, AllocationFlags) }
                : Array.Empty<(uint, uint, uint)>(), fixture.Allocations));
        Check(failures, "memory before allocation", () =>
            AssertBoundaryMemory(fixture.MemoryAtAllocations, fixture.Allocations.Count, before));
        Check(failures, "Exec A1/D0 release", () => Assert.Equal(scenario switch
        {
            "free-owned" => new[] { (AllocationBase, requested) },
            "alloc-odd-result" => new[] { (AllocationBase + 1, requested) },
            _ => Array.Empty<(uint, uint)>()
        }, fixture.Frees));
        Check(failures, "retirement memory", () =>
            AssertBoundaryMemory(fixture.MemoryAtFrees, fixture.Frees.Count, before));
        Check(failures, "publication or rollback", () =>
        {
            if (scenario == "alloc-success")
            {
                fixture.AssertPublishedBitMap(depth, requested);
                fixture.AssertOutsideAllocationUnchanged(before, requested);
            }
            else
            {
                AssertMemoryEqual(before, fixture.CaptureMemory());
            }
        });
        Check(failures, "public library return", () => AssertPublicReturn(result, expectedD0, !claimed));

        if (scenario == "alloc-success")
        {
            if (!result.UsedFallback && result.Value == BitMap)
            {
                // Use the constructor's actual header and returned pointer,
                // even after recording failed Alloc register assertions.
                var beforeFree = fixture.CaptureMemory();
                var freed = fixture.Invoke(GraphicsLvo.FreeBitMap, depth, bitmapFlags, result.Value);
                Check(failures, "roundtrip release", () => Assert.Equal(
                    (AllocationBase, requested), Assert.Single(fixture.Frees)));
                Check(failures, "roundtrip allocation count", () => Assert.Single(fixture.Allocations));
                Check(failures, "roundtrip retirement memory", () =>
                    AssertBoundaryMemory(fixture.MemoryAtFrees, fixture.Frees.Count, beforeFree));
                Check(failures, "roundtrip memory", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
                Check(failures, "roundtrip Free public return", () => AssertPublicReturn(freed, 0, false));
            }
            else
            {
                failures.Add("Roundtrip Free was not attempted because no native BitMap pointer was returned.");
            }
        }
        Assert.True(failures.Count == 0,
            $"{fixture.Route}, depth={depth}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static byte[] ExpectedEnvelope(int depth, uint requested)
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
                AllocationBase + HeaderAndBitMapBytes + PlaneBytes * (uint)plane);
        }
        return expected; // Reserved bytes, unused plane slots, and payload remain clear.
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

    private static void AssertPublicReturn(CallResult call, uint expectedD0, bool expectFallback)
    {
        var differences = call.RegisterDifferences.ToList();
        if (call.Value != expectedD0)
            differences.Add($"D0 expected {expectedD0:X8}, actual {call.Value:X8}");
        if (call.UsedFallback != expectFallback)
            differences.Add($"private fallback expected {expectFallback}, observed {call.UsedFallback}");
        if (call.ProgramCounter != call.CallerReturnAddress)
            differences.Add($"PC expected {call.CallerReturnAddress:X8}, actual {call.ProgramCounter:X8}");
        if (call.StackPointer != call.CallerStackPointer)
            differences.Add($"SP expected {call.CallerStackPointer:X8}, actual {call.StackPointer:X8}");
        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Heap, byte[] Low, byte[] GraphicsBase);
    private sealed record CallResult(
        uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer, string[] RegisterDifferences);

    private sealed class Fixture : IDisposable
    {
        private const uint ExecBase = 0x0077_0000;
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint StackAddress = 0x00C7_0000;
        private const uint StackPointer = StackAddress + 0x300;
        private const uint HeapAddress = 0x00D0_0000;
        private const int HeapSize = 0x200;
        private const int LowMemorySize = 0x100;
        private static readonly uint[] DataCanaries =
        {
            0xD2D2_0202, 0xD3D3_0303, 0xD4D4_0404,
            0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505
        };
        private readonly IM68kCore _cpu;
        private readonly bool _autoInitEntry;
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;
        private readonly uint _functionArray;

        internal Fixture(bool relocated, bool autoInitEntry)
        {
            _autoInitEntry = autoInitEntry;
            Route = $"{(relocated ? "relocated HUNK" : "fixed image")}/" +
                (autoInitEntry ? "AUTOINIT JSR(A2)" : "JSR d16(A6)");
            uint residentAddress;
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
                residentAddress = program.SegmentBases[1];
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(entry => entry.Key,
                    entry => FixedCodeAddress + (uint)entry.Value);
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
                residentAddress = library.ResidentAddress;
            }
            Assert.True(_nativeCodeAddress >= 0x0020_0000);
            Assert.True(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            Assert.Equal((ushort)0x4AFC, Bus.ReadWord(residentAddress));
            _functionArray = Bus.ReadLong(Bus.ReadLong(residentAddress + 0x16) + 4);
            var heap = new byte[HeapSize];
            Array.Fill(heap, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            for (var index = 0; index < LowMemorySize; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            SetExecAvailable(true);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
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
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal string Route { get; }
        internal uint AllocationResult { get; set; } = AllocationBase;
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal void SetExecAvailable(bool available) => WriteLong(4, available ? ExecBase : 0);
        internal void WriteWord(uint address, ushort value) => Bus.WriteWord(address, value);
        internal void WriteLong(uint address, uint value) => Bus.WriteLong(address, value);
        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(_graphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void SeedBitMap(int depth, uint requested)
        {
            var envelope = ExpectedEnvelope(depth, requested);
            for (var index = 0; index < envelope.Length; index++)
                Bus.WriteByte(AllocationBase + (uint)index, envelope[index], 0);
        }

        internal void AssertPublishedBitMap(int depth, uint requested)
            => Assert.Equal(ExpectedEnvelope(depth, requested), ReadBytes(AllocationBase, (int)requested));

        internal void AssertOutsideAllocationUnchanged(MemorySnapshot before, uint requested)
        {
            var after = CaptureMemory();
            var start = (int)(AllocationBase - HeapAddress);
            var end = start + (int)requested;
            Assert.Equal(before.Heap.Take(start), after.Heap.Take(start));
            Assert.Equal(before.Heap.Skip(end), after.Heap.Skip(end));
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

        internal CallResult Invoke(GraphicsLvo vector, int depth, uint bitmapFlags, uint a0)
        {
            var allocating = vector == GraphicsLvo.AllocBitMap;
            var entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            var functionIndex = (-(int)vector / NativeGraphicsLibraryImageBuilder.VectorStubSize) - 1;
            var functionEntry = Bus.ReadLong(_functionArray + (uint)(functionIndex * 4));
            Assert.Equal(entry, functionEntry);
            var firstTarget = _autoInitEntry ? functionEntry : checked((uint)((long)_graphicsBase + (int)vector));
            var returnAddress = CallerAddress + (_autoInitEntry ? 2u : 4u);
            if (_autoInitEntry)
            {
                Bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2), independent AUTOINIT pointer call
            }
            else
            {
                Assert.Equal((ushort)0x4EF9, Bus.ReadWord(firstTarget));
                Assert.Equal(entry, Bus.ReadLong(firstTarget + 2));
                Bus.WriteWord(CallerAddress, 0x4EAE); // actual negative-LVO JSR d16(A6)
                Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            }
            Bus.WriteWord(returnAddress, 0x4E71);
            var expectedData = DataCanaries.ToArray();
            if (allocating)
            {
                // D2/D3 are both public inputs and callee-saved registers.
                expectedData[0] = (uint)depth;
                expectedData[1] = bitmapFlags;
            }
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = functionEntry; // A2's route-specific preservation canary
            var originalD0 = allocating ? Width : CapturedFreeD0;
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = originalD0;
            _cpu.State.D[1] = allocating ? Height : 0xD1D1_0101u;
            _cpu.State.A[0] = a0; // Alloc friend and Free bitmap both use public A0.
            _cpu.State.A[1] = 0xA1A1_A1A1;
            for (var index = 0; index < expectedData.Length; index++)
                _cpu.State.D[index + 2] = expectedData[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = _graphicsBase;
            _cpu.ExecuteInstruction();
            Assert.Equal(firstTarget, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(returnAddress, Bus.ReadLong(_cpu.State.A[7]));
            var usedFallback = false;
            var enteredBody = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                enteredBody |= pc == entry;
                // Original D0 is nonzero and differs from every successful
                // result, including on a linker-local fallback RTS route.
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback ||
                    (Bus.ReadWord(pc) == 0x4E75 && _cpu.State.D[0] == originalD0);
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != returnAddress)
                    continue;
                Assert.True(enteredBody, "The BitMap public entry was not reached.");
                var differences = new List<string>();
                for (var index = 0; index < expectedData.Length; index++)
                {
                    var actual = _cpu.State.D[index + 2];
                    if (actual != expectedData[index])
                        differences.Add($"D{index + 2} expected {expectedData[index]:X8}, actual {actual:X8}");
                }
                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    var actual = _cpu.State.A[index + 2];
                    if (actual != expectedAddresses[index])
                        differences.Add($"A{index + 2} expected {expectedAddresses[index]:X8}, actual {actual:X8}");
                }
                if (_cpu.State.A[6] != _graphicsBase)
                    differences.Add($"A6 expected {_graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                // Full public callee-save proof does not assert transparent
                // volatile D1/A0/A1 arguments at a provider tailchain.
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter,
                    _cpu.State.A[7], returnAddress, StackPointer, differences.ToArray());
            }
            throw new InvalidOperationException(
                $"{Route}, {vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
