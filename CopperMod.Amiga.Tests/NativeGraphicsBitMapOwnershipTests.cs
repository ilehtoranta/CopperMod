using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsBitMapOwnershipTests
{
    private const uint CapturedD0 = 0xA1B2_C3D4;
    private const uint AllocationBase = 0x00D0_0100;
    private const uint PrefixBytes = 16;
    private const uint HeaderAndBitMapBytes = 56;
    private const uint PlaneBytes = 12;
    private const uint BitMap = AllocationBase + PrefixBytes;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> IdentityCases()
    {
        foreach (var relocated in new[] { false, true })
        {
            foreach (var (depth, requested) in new[] { (1, 68u), (2, 80u), (4, 104u), (8, 152u) })
            {
                yield return new object[] { relocated, "owned", depth, depth, requested };
                yield return new object[] { relocated, "bad-marker", depth, depth, requested };
            }
            foreach (var unsupportedDepth in new[] { 0, 3, 16 })
                yield return new object[] { relocated, "unsupported-private-depth", 1, unsupportedDepth, 68u };
        }
    }

    [Theory]
    [MemberData(nameof(IdentityCases))]
    public void FreeBitMapRequiresNativeMarkerAndAdmittedPrivateDepth(
        bool relocated, string scenario, int publicDepth, int privateDepth, uint requested)
    {
        using var fixture = new Fixture(relocated);
        var expectedEnvelope = CoherentEnvelope(publicDepth, requested);
        fixture.SeedEnvelope(expectedEnvelope);
        if (scenario == "bad-marker")
        {
            // Keep the matching private depth, all public fields, every
            // plane link, and the stored size exactly as in a valid owner.
            fixture.WriteWord(AllocationBase, 0x464F);
            BinaryPrimitives.WriteUInt16BigEndian(expectedEnvelope.AsSpan(0), 0x464F);
        }
        else if (scenario == "unsupported-private-depth")
        {
            // Only private depth changes. In particular, leave the public
            // one-plane bitmap and its valid marker/plane/span unchanged.
            fixture.WriteWord(AllocationBase + 6, checked((ushort)privateDepth));
            BinaryPrimitives.WriteUInt16BigEndian(expectedEnvelope.AsSpan(6), checked((ushort)privateDepth));
        }
        Assert.Equal(expectedEnvelope, fixture.ReadEnvelope(requested));
        var before = fixture.CaptureMemory();
        var result = fixture.InvokeFree();
        var claimed = scenario == "owned";
        var failures = new List<string>();

        Check(failures, "Free must not allocate", () => Assert.Empty(fixture.Allocations));
        Check(failures, "Exec FreeMem A1/D0", () => Assert.Equal(
            claimed ? new[] { (AllocationBase, requested) } : Array.Empty<(uint, uint)>(), fixture.Frees));
        Check(failures, "memory at retirement", () =>
        {
            Assert.Equal(fixture.Frees.Count, fixture.MemoryAtFrees.Count);
            foreach (var snapshot in fixture.MemoryAtFrees)
                AssertMemoryEqual(before, snapshot);
        });
        Check(failures, "guest envelope and guards after return", () =>
        {
            Assert.Equal(expectedEnvelope, fixture.ReadEnvelope(requested));
            AssertMemoryEqual(before, fixture.CaptureMemory());
        });
        Check(failures, "public library return", () => AssertPublicReturn(result, claimed ? 0u : CapturedD0, !claimed));
        Assert.True(failures.Count == 0,
            $"{fixture.Route}, {scenario}, public depth={publicDepth}, private depth={privateDepth}:\n" +
            string.Join("\n", failures));
    }

    private static byte[] CoherentEnvelope(int depth, uint requested)
    {
        // This is the native constructor's ordinary 17x3, flags=3 layout.
        // It is a private-header identity oracle, not the portable library's
        // instance-ownership registry or a claim about double-free safety.
        Assert.Equal(40, GraphicsLayouts.BitMapSize);
        Assert.Equal(HeaderAndBitMapBytes + PlaneBytes * (uint)depth, requested);
        var expected = new byte[checked((int)requested)];
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(0), 0x424D);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(2), 17);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(6), checked((ushort)depth));
        BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(8), requested);
        var publicOffset = (int)PrefixBytes;
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapBytesPerRow), 4);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapRows), 3);
        expected[publicOffset + GraphicsLayouts.BitMapFlags] = 0x0A; // STANDARD | DISPLAYABLE
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
        private const uint ReturnAddress = CallerAddress + 4;
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
            Route = relocated ? "relocated HUNK / JSR d16(A6)" : "fixed image / JSR d16(A6)";
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
            }
            Assert.True(_nativeCodeAddress >= 0x0020_0000);
            Assert.True(_graphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize >= 0x0020_0000);
            Assert.NotEqual(ExecBase, _graphicsBase);
            var heap = new byte[HeapSize];
            Array.Fill(heap, (byte)0xA5);
            Bus.MapWritableMemory(HeapAddress, heap);
            for (var index = 0; index < LowMemorySize; index++)
                Bus.WriteByte((uint)index, 0xA5, 0);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                // No FreeBitMap path should allocate. Record unexpected
                // calls, then return NULL without changing guest memory.
                Assert.Equal(ExecBase, state.A[6]);
                Allocations.Add((state.D[0], state.D[1]));
                state.D[0] = 0;
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
        internal List<(uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        internal void WriteWord(uint address, ushort value) => Bus.WriteWord(address, value);
        internal void SeedEnvelope(byte[] envelope)
        {
            for (var index = 0; index < envelope.Length; index++)
                Bus.WriteByte(AllocationBase + (uint)index, envelope[index], 0);
        }
        internal byte[] ReadEnvelope(uint requested) => ReadBytes(AllocationBase, checked((int)requested));
        private byte[] ReadBytes(uint address, int length)
            => Enumerable.Range(0, length).Select(index => Bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory()
            => new(ReadBytes(HeapAddress, HeapSize), ReadBytes(0, LowMemorySize),
                ReadBytes(_graphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize));

        private static void PoisonVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult InvokeFree()
        {
            const GraphicsLvo vector = GraphicsLvo.FreeBitMap;
            var entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            var vectorAddress = checked((uint)((long)_graphicsBase + (int)vector));
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE); // real negative-LVO JSR d16(A6)
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = BitMap;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < AddressCanaries.Length; index++)
                _cpu.State.A[index + 2] = AddressCanaries[index];
            _cpu.State.A[6] = _graphicsBase;
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
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback ||
                    (Bus.ReadWord(pc) == 0x4E75 && _cpu.State.D[0] == CapturedD0);
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;
                Assert.True(enteredBody, "The FreeBitMap public entry was not reached.");
                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    var actual = _cpu.State.D[index + 2];
                    if (actual != DataCanaries[index])
                        differences.Add($"D{index + 2} expected {DataCanaries[index]:X8}, actual {actual:X8}");
                }
                for (var index = 0; index < AddressCanaries.Length; index++)
                {
                    var actual = _cpu.State.A[index + 2];
                    if (actual != AddressCanaries[index])
                        differences.Add($"A{index + 2} expected {AddressCanaries[index]:X8}, actual {actual:X8}");
                }
                if (_cpu.State.A[6] != _graphicsBase)
                    differences.Add($"A6 expected {_graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                // Provider-tailchain volatile arguments are deliberately
                // outside this native identity/callee-save contract.
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter,
                    _cpu.State.A[7], ReturnAddress, StackPointer, differences.ToArray());
            }
            throw new InvalidOperationException(
                $"{Route}, FreeBitMap did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
