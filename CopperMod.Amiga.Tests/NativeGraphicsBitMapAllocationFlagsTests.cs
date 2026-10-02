using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsBitMapAllocationFlagsTests
{
    private const ushort Width = 17;
    private const ushort Height = 3;
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const uint AllocationBase = 0x00D0_0100;
    private const uint PrefixBytes = 16;
    private const uint HeaderAndBitMapBytes = 56;
    private const uint PlaneBytes = 12;
    private const uint BitMap = AllocationBase + PrefixBytes;
    private const byte AllocationFill = 0xA5;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> AllocationFlagCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var (depth, requested) in new[] { (1, 68u), (2, 80u), (4, 104u), (8, 152u) })
        foreach (var bitmapFlags in new[] { 0u, 1u, 2u, 3u })
            yield return new object[] { relocated, depth, requested, bitmapFlags };
    }

    [Theory]
    [MemberData(nameof(AllocationFlagCases))]
    public void BitMapAllocatesChipStorageAndClearsOnlyRequestedRasterPayload(
        bool relocated, int depth, uint requested, uint bitmapFlags)
    {
        using var fixture = new Fixture(relocated);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.AllocBitMap, depth, bitmapFlags, a0: 0);
        var failures = new List<string>();

        Check(failures, "Exec AllocMem D0/D1", () => Assert.Equal(
            (AllocationBase, requested, ExpectedExecFlags(bitmapFlags)), Assert.Single(fixture.Allocations)));
        Check(failures, "no writes before Exec allocation", () =>
            AssertMemoryEqual(before, Assert.Single(fixture.MemoryAtAllocations)));
        Check(failures, "Exec CLEAR option", () =>
            fixture.AssertMemoryReturnedByExec(before, requested, bitmapFlags));
        Check(failures, "no premature release", () => Assert.Empty(fixture.Frees));
        Check(failures, "complete private/public header", () =>
            fixture.AssertPublishedHeader(depth, requested, bitmapFlags));
        Check(failures, "plane payload initialization", () =>
            fixture.AssertPublishedPayload(requested, bitmapFlags));
        Check(failures, "allocation guards", () =>
            fixture.AssertOutsideAllocationUnchanged(before, requested));
        Check(failures, "AllocBitMap public return", () => AssertPublicReturn(result, BitMap));

        if (!result.UsedFallback && result.Value == BitMap)
        {
            // Free precisely the constructor's result. Do not zero reserved
            // bytes or repair any geometry, flags, or plane pointers here.
            var beforeFree = fixture.CaptureMemory();
            var freed = fixture.Invoke(GraphicsLvo.FreeBitMap, depth, bitmapFlags, result.Value);
            Check(failures, "roundtrip Exec FreeMem A1/D0", () => Assert.Equal(
                (AllocationBase, requested), Assert.Single(fixture.Frees)));
            Check(failures, "roundtrip allocation count", () => Assert.Single(fixture.Allocations));
            Check(failures, "unmodified envelope at release", () =>
                AssertMemoryEqual(beforeFree, Assert.Single(fixture.MemoryAtFrees)));
            Check(failures, "no retirement writes", () =>
                AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
            Check(failures, "FreeBitMap public return", () => AssertPublicReturn(freed, 0));
        }
        else
        {
            failures.Add("Roundtrip Free was not attempted because no native BitMap pointer was returned.");
        }

        Assert.True(failures.Count == 0,
            $"{fixture.Route}, depth={depth}, bitmapFlags={bitmapFlags:X8}:\n" + string.Join("\n", failures));
    }

    private static uint ExpectedExecFlags(uint bitmapFlags)
        => (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip) |
            ((bitmapFlags & 1u) != 0 ? (uint)ExecApi.MemoryFlags.Clear : 0u);

    private static byte[] ExpectedHeader(int depth, uint requested, uint bitmapFlags)
    {
        Assert.Equal(40, GraphicsLayouts.BitMapSize);
        Assert.Equal(HeaderAndBitMapBytes + PlaneBytes * (uint)depth, requested);

        // Exact zeros in the private reserved bytes, public pad and unused
        // plane slots preserve our deterministic native layout. They are not
        // an independently established byte-for-byte Kickstart ROM oracle.
        var expected = new byte[checked((int)HeaderAndBitMapBytes)];
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(0), 0x424D);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(2), Width);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(4), Height);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(6), checked((ushort)depth));
        BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(8), requested);
        var publicOffset = (int)PrefixBytes;
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapBytesPerRow), 4);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapRows), Height);
        // Retain the native arm's existing STANDARD/DISPLAYABLE publication;
        // BMF_CLEAR is an allocation request, not copied into these flags.
        expected[publicOffset + GraphicsLayouts.BitMapFlags] = checked((byte)(0x08u | (bitmapFlags & 2u)));
        expected[publicOffset + GraphicsLayouts.BitMapDepth] = checked((byte)depth);
        for (var plane = 0; plane < depth; plane++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                expected.AsSpan(publicOffset + GraphicsLayouts.BitMapPlanes + plane * 4),
                AllocationBase + HeaderAndBitMapBytes + PlaneBytes * (uint)plane);
        }
        return expected;
    }

    private static void Check(List<string> failures, string contract, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(contract + ": " + exception.Message);
        }
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.Heap, actual.Heap);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.GraphicsBase, actual.GraphicsBase);
    }

    private static void AssertPublicReturn(CallResult call, uint expectedD0)
    {
        var differences = call.RegisterDifferences.ToList();
        if (call.Value != expectedD0)
            differences.Add($"D0 expected {expectedD0:X8}, actual {call.Value:X8}");
        if (call.UsedFallback)
            differences.Add("Unexpected private fallback.");
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
        private readonly uint _graphicsBase;
        private readonly uint _nativeCodeAddress;

        internal Fixture(bool relocated)
        {
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/JSR d16(A6)";
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

            var heap = new byte[HeapSize];
            Array.Fill(heap, AllocationFill);
            Bus.MapWritableMemory(HeapAddress, heap);
            for (var index = 0; index < LowMemorySize; index++)
                Bus.WriteByte((uint)index, AllocationFill, 0);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((AllocationBase, size, flags));
                MemoryAtAllocations.Add(CaptureMemory());
                Assert.InRange(size, 1u, HeapAddress + (uint)HeapSize - AllocationBase);

                // Model Exec's actual requested option, not the caller's
                // BMF_CLEAR expectation. Otherwise a wrong emitted D1 could
                // silently pass the payload/header assertions.
                if ((flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                    Bus.ClearMemory(AllocationBase, checked((int)size));
                MemoryReturnedByExec.Add(CaptureMemory());
                state.D[0] = AllocationBase;
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
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryReturnedByExec { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();

        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(_graphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        internal void AssertMemoryReturnedByExec(MemorySnapshot before, uint requested, uint bitmapFlags)
        {
            var expectedHeap = before.Heap.ToArray();
            if ((bitmapFlags & 1u) != 0)
                Array.Clear(expectedHeap, checked((int)(AllocationBase - HeapAddress)), checked((int)requested));
            AssertMemoryEqual(new MemorySnapshot(expectedHeap, before.Low, before.GraphicsBase),
                Assert.Single(MemoryReturnedByExec));
        }

        internal void AssertPublishedHeader(int depth, uint requested, uint bitmapFlags)
            => Assert.Equal(ExpectedHeader(depth, requested, bitmapFlags),
                ReadBytes(AllocationBase, checked((int)HeaderAndBitMapBytes)));

        internal void AssertPublishedPayload(uint requested, uint bitmapFlags)
        {
            // BMF_CLEAR requests color-zero plane storage. With no clear
            // request, retain the bytes supplied by this Exec fixture: this
            // checks our native body does not add an unrequested payload
            // clear, not a universal promise about arbitrary ROM allocations.
            var expectedByte = (bitmapFlags & 1u) != 0 ? (byte)0 : AllocationFill;
            var bytes = checked((int)(requested - HeaderAndBitMapBytes));
            Assert.Equal(Enumerable.Repeat(expectedByte, bytes),
                ReadBytes(AllocationBase + HeaderAndBitMapBytes, bytes));
        }

        internal void AssertOutsideAllocationUnchanged(MemorySnapshot before, uint requested)
        {
            var after = CaptureMemory();
            var start = checked((int)(AllocationBase - HeapAddress));
            var end = start + checked((int)requested);
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
            var firstTarget = checked((uint)((long)_graphicsBase + (int)vector));
            var returnAddress = CallerAddress + 4;
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(firstTarget));
            Assert.Equal(entry, Bus.ReadLong(firstTarget + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE); // actual negative-LVO JSR d16(A6)
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(returnAddress, 0x4E71);

            var expectedData = DataCanaries.ToArray();
            if (allocating)
            {
                expectedData[0] = (uint)depth;
                expectedData[1] = bitmapFlags;
            }
            var originalD0 = allocating ? Width : CapturedFreeD0;
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = originalD0;
            _cpu.State.D[1] = allocating ? Height : 0xD1D1_0101u;
            _cpu.State.A[0] = a0; // NULL friend on Alloc; actual public handle on Free.
            _cpu.State.A[1] = 0xA1A1_A1A1;
            for (var index = 0; index < expectedData.Length; index++)
                _cpu.State.D[index + 2] = expectedData[index];
            for (var index = 0; index < AddressCanaries.Length; index++)
                _cpu.State.A[index + 2] = AddressCanaries[index];
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
                for (var index = 0; index < AddressCanaries.Length; index++)
                {
                    var actual = _cpu.State.A[index + 2];
                    if (actual != AddressCanaries[index])
                        differences.Add($"A{index + 2} expected {AddressCanaries[index]:X8}, actual {actual:X8}");
                }
                if (_cpu.State.A[6] != _graphicsBase)
                    differences.Add($"A6 expected {_graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
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
