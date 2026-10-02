using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsBitMapSpanTests
{
    private const uint CapturedFreeD0 = 0xA1B2_C3D4;
    private const uint PrefixBytes = 16;
    private const uint HeaderBytes = 56;
    private const uint NormalBase = 0x00D0_0040;
    private const uint Width = 17;
    private const ushort Rows = 3;
    private const ushort RowBytes = 4;
    private const uint OverflowWidth = 524272;
    private const ushort OverflowRowBytes = 65534;
    private const ushort OverflowRows = 32770;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public |
        ExecApi.MemoryFlags.Chip | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    public static IEnumerable<object[]> DepthCases()
    {
        yield return new object[] { 1, 68u, 0xFFFF_FFBCu };
        yield return new object[] { 2, 80u, 0xFFFF_FFB0u };
        yield return new object[] { 4, 104u, 0xFFFF_FF98u };
        yield return new object[] { 8, 152u, 0xFFFF_FF68u };
    }

    public static IEnumerable<object[]> RoundtripCases()
    {
        foreach (var row in DepthCases())
        {
            yield return new[] { row[0], row[1], (object)NormalBase };
            yield return row;
        }
    }

    public static IEnumerable<object[]> StoredSpanCases()
    {
        foreach (var row in DepthCases())
        {
            var expected = (uint)row[1];
            yield return new[] { row[0], row[1], (object)(expected + 2) };
            yield return new[] { row[0], row[1], (object)(expected - 2) };
        }
    }

    [Theory]
    [MemberData(nameof(RoundtripCases))]
    public void CompleteBitMapEnvelopeRoundtripsAtNormalAndExactEndAddresses(
        int depth, uint requested, uint allocationBase)
    {
        Assert.Equal(HeaderBytes + (uint)RowBytes * Rows * (uint)depth, requested);
        Assert.True((ulong)allocationBase + requested - 1 <= uint.MaxValue);
        if (allocationBase != NormalBase)
            Assert.Equal((ulong)uint.MaxValue, (ulong)allocationBase + requested - 1);
        var publicBitMap = checked(allocationBase + PrefixBytes);
        using var fixture = new Fixture(allocationBase);
        var before = fixture.CaptureMemory();
        var allocated = fixture.Invoke(GraphicsLvo.AllocBitMap, depth);
        var failures = new List<string>();
        Check(failures, "allocation", () => Assert.Equal(
            (allocationBase, requested, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "allocation-entry memory", () => AssertAllocationMemory(fixture, before));
        Check(failures, "no premature release", () => Assert.Empty(fixture.Frees));
        Check(failures, "complete publication", () => Assert.Equal(
            ExpectedEnvelope(allocationBase, depth, requested), fixture.ReadLogicalBytes(allocationBase, (int)requested)));
        Check(failures, "outside envelope", () =>
            fixture.AssertOutsideEnvelopeUnchanged(before, allocationBase, requested));

        if (!allocated.UsedFallback && allocated.Value == publicBitMap)
        {
            // Free the constructor's actual header and full logical pointer,
            // including the exact-end case; never repair either between calls.
            var beforeFree = fixture.CaptureMemory();
            var freed = fixture.Invoke(GraphicsLvo.FreeBitMap, depth, allocated.Value);
            Check(failures, "release", () => Assert.Equal(
                (allocationBase, requested), Assert.Single(fixture.Frees)));
            Check(failures, "no allocation during release", () => Assert.Single(fixture.Allocations));
            Check(failures, "retirement memory", () => AssertRetirementMemory(fixture, beforeFree));
            Check(failures, "after release", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
            Check(failures, "FreeBitMap ABI", () => AssertReturn(freed, 0, expectFallback: false));
        }
        else
        {
            failures.Add("FreeBitMap was not attempted because no native public pointer was returned.");
        }
        Check(failures, "AllocBitMap ABI", () => AssertReturn(allocated, publicBitMap, expectFallback: false));
        AssertNoFailures(failures, $"depth={depth}, roundtrip private={allocationBase:X8}");
    }

    [Theory]
    [MemberData(nameof(DepthCases))]
    public void WrappingBitMapAllocationRollsBackBeforeWritingEitherAddressAlias(
        int depth, uint requested, uint exactEndBase)
    {
        var allocationBase = checked(exactEndBase + 2);
        Assert.True((ulong)allocationBase + HeaderBytes - 1 <= uint.MaxValue);
        Assert.True((ulong)allocationBase + requested - 1 > uint.MaxValue);
        using var fixture = new Fixture(allocationBase, malformedAllocation: true);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.AllocBitMap, depth);
        var failures = new List<string>();
        Check(failures, "allocation", () => Assert.Equal(
            (allocationBase, requested, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "allocation-entry memory", () => AssertAllocationMemory(fixture, before));
        Check(failures, "exact provisional release", () => Assert.Equal(
            (allocationBase, requested), Assert.Single(fixture.Frees)));
        Check(failures, "no publication before release", () => AssertRetirementMemory(fixture, before));
        Check(failures, "no publication after release", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "fallback ABI", () => AssertReturn(result, Width, expectFallback: true));
        AssertNoFailures(failures, $"depth={depth}, wrapping allocation {allocationBase:X8}");
    }

    [Theory]
    [MemberData(nameof(DepthCases))]
    public void IndependentlySeededExactEndBitMapCanBeFreed(
        int depth, uint requested, uint exactEndBase)
    {
        Assert.Equal((ulong)uint.MaxValue, (ulong)exactEndBase + requested - 1);
        using var fixture = new Fixture();
        fixture.SeedBytes(exactEndBase, ExpectedEnvelope(exactEndBase, depth, requested));
        AssertSeededFree(fixture, exactEndBase, requested, accepted: true, $"depth={depth}, seeded exact-end");
    }

    [Theory]
    [MemberData(nameof(DepthCases))]
    public void MatchingBitMapHeaderCannotAuthorizeAWrappedCompleteEnvelope(
        int depth, uint requested, uint exactEndBase)
    {
        var allocationBase = checked(exactEndBase + 2);
        Assert.True((ulong)allocationBase + HeaderBytes - 1 <= uint.MaxValue);
        Assert.True((ulong)allocationBase + requested - 1 > uint.MaxValue);
        using var fixture = new Fixture();
        // Only payload wraps. Seed the coherent header and every plane link,
        // but never write a fake payload through the low-address alias.
        fixture.SeedBytes(allocationBase,
            ExpectedHeader(allocationBase, depth, Width, RowBytes, Rows, requested));
        AssertSeededFree(fixture, allocationBase, requested, accepted: false, $"depth={depth}, seeded wrapping");
    }

    [Theory]
    [MemberData(nameof(StoredSpanCases))]
    public void StoredBitMapSpanMustEqualTheCompletePlanarAggregate(
        int depth, uint expectedSpan, uint storedSpan)
    {
        using var fixture = new Fixture();
        fixture.SeedBytes(NormalBase, ExpectedEnvelope(NormalBase, depth, expectedSpan));
        fixture.WriteLong(NormalBase + 8, storedSpan); // only the exact-size field changes
        Assert.Equal(ExpectedHeader(NormalBase, depth, Width, RowBytes, Rows, storedSpan),
            fixture.ReadLogicalBytes(NormalBase, (int)HeaderBytes));
        AssertSeededFree(fixture, NormalBase, storedSpan, accepted: false,
            $"depth={depth}, stored={storedSpan}, expected={expectedSpan}");
    }

    [Fact]
    public void NullBitMapFreeSucceedsWithoutExecCallsOrMemoryMutation()
    {
        using var fixture = new Fixture();
        AssertFreeCall(fixture, publicBitMap: 0, allocationBase: 0, requested: 0,
            accepted: true, "NULL FreeBitMap");
    }

    [Fact]
    public void PublicPointerSixteenCannotAuthorizeAnImpossibleZeroPrivateOwner()
    {
        using var fixture = new Fixture();
        // Only marker/width at 0..3 are seeded. SysBase at 4..7 is never
        // overwritten or repaired. This is a no-release/no-mutation control;
        // the unchanged public<=16 source guard establishes pre-header
        // rejection, which this row does not isolate from a later decline.
        fixture.SeedBytes(0, new byte[] { 0x42, 0x4D, 0, 17 });
        fixture.AssertExecBaseUnchanged();
        AssertSeededFree(fixture, 0, 68, accepted: false, "zero private owner");
    }

    [Theory]
    [InlineData(2, 0x0002_0030u)]
    [InlineData(4, 0x0004_0028u)]
    [InlineData(8, 0x0008_0018u)]
    public void OverflowingPlaneAggregationDeclinesBeforeCallingAllocMem(int depth, uint encodedLowSpan)
    {
        AssertOverflowGeometry(depth, encodedLowSpan);
        using var fixture = new Fixture(returnNullAllocation: true);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.AllocBitMap, depth, width: OverflowWidth, rows: OverflowRows);
        var failures = new List<string>();
        Check(failures, "overflow rejected before Exec allocation", () => Assert.Empty(fixture.Allocations));
        Check(failures, "allocation-entry memory", () => AssertAllocationMemory(fixture, before));
        Check(failures, "no release", () => Assert.Empty(fixture.Frees));
        Check(failures, "no retirement callback", () => Assert.Empty(fixture.MemoryAtFrees));
        Check(failures, "no mutation", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "full-width input D0 and saved registers", () =>
            AssertReturn(result, OverflowWidth, expectFallback: true));
        AssertNoFailures(failures, $"depth={depth}, allocation aggregate overflow, encoded={encodedLowSpan:X8}");
    }

    [Theory]
    [InlineData(2, 0x0002_0030u)]
    [InlineData(4, 0x0004_0028u)]
    [InlineData(8, 0x0008_0018u)]
    public void OverflowingPlaneAggregationCannotBeAuthorizedByWrappedStoredSpanOrLinks(
        int depth, uint encodedLowSpan)
    {
        AssertOverflowGeometry(depth, encodedLowSpan);
        using var fixture = new Fixture();
        var header = ExpectedHeader(NormalBase, depth, OverflowWidth, OverflowRowBytes, OverflowRows, encodedLowSpan);
        fixture.SeedBytes(NormalBase, header);
        Assert.Equal(header, fixture.ReadLogicalBytes(NormalBase, (int)HeaderBytes));
        // No payload is allocated or mapped. Native Free only needs this
        // header to discover the public row/height aggregate cannot fit ULONG.
        AssertSeededFree(fixture, NormalBase, encodedLowSpan, accepted: false,
            $"depth={depth}, free aggregate overflow, encoded={encodedLowSpan:X8}");
    }

    private static void AssertOverflowGeometry(int depth, uint encodedLowSpan)
    {
        Assert.Equal(65534u, ((OverflowWidth + 15u) >> 4) * 2u);
        var planeBytes = (ulong)OverflowRowBytes * OverflowRows;
        Assert.Equal(0x8000_FFFCuL, planeBytes);
        var trueSpan = HeaderBytes + planeBytes * (uint)depth;
        Assert.True(trueSpan > uint.MaxValue);
        Assert.Equal(encodedLowSpan, unchecked((uint)trueSpan));
        Assert.True((ulong)NormalBase + encodedLowSpan - 1 <= uint.MaxValue);
    }

    private static void AssertSeededFree(
        Fixture fixture, uint allocationBase, uint requested, bool accepted, string context)
        => AssertFreeCall(fixture, checked(allocationBase + PrefixBytes), allocationBase, requested, accepted, context);

    private static void AssertFreeCall(
        Fixture fixture, uint publicBitMap, uint allocationBase, uint requested, bool accepted, string context)
    {
        fixture.AssertExecBaseUnchanged(); // before execution, never rewritten after seed setup
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.FreeBitMap, depth: 1, publicBitMap: publicBitMap);
        var failures = new List<string>();
        Check(failures, "no allocation", () => Assert.Empty(fixture.Allocations));
        Check(failures, "no allocation callback", () => Assert.Empty(fixture.MemoryAtAllocations));
        Check(failures, "ownership retirement", () => Assert.Equal(
            accepted && publicBitMap != 0 ? new[] { (allocationBase, requested) } : Array.Empty<(uint, uint)>(),
            fixture.Frees));
        Check(failures, "memory at any retirement", () => AssertRetirementMemory(fixture, before));
        Check(failures, "no mutation", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "public ABI", () => AssertReturn(result, accepted ? 0u : CapturedFreeD0, !accepted));
        AssertNoFailures(failures, context);
    }

    private static byte[] ExpectedEnvelope(uint allocationBase, int depth, uint requested)
    {
        Assert.Equal(HeaderBytes + (uint)RowBytes * Rows * (uint)depth, requested);
        var expected = new byte[checked((int)requested)];
        ExpectedHeader(allocationBase, depth, Width, RowBytes, Rows, requested).CopyTo(expected, 0);
        return expected;
    }

    private static byte[] ExpectedHeader(
        uint allocationBase, int depth, uint width, ushort rowBytes, ushort rows, uint storedSpan)
    {
        Assert.Equal(40, GraphicsLayouts.BitMapSize);
        var expected = new byte[(int)HeaderBytes];
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(0), 0x424D);
        // Preserve existing constructor storage, including overflow width's
        // low0xFFF0. Stride is not rederived from this private word here.
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(2), (ushort)(width & ushort.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(4), rows);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(6), checked((ushort)depth));
        BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(8), storedSpan);
        var publicOffset = (int)PrefixBytes;
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapBytesPerRow), rowBytes);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(publicOffset + GraphicsLayouts.BitMapRows), rows);
        expected[publicOffset + GraphicsLayouts.BitMapFlags] = 0x0A;
        expected[publicOffset + GraphicsLayouts.BitMapDepth] = checked((byte)depth);
        var planeBytes = (ulong)rowBytes * rows;
        for (var plane = 0; plane < depth; plane++)
        {
            // For aggregation negatives the low32-bit links deliberately
            // match the old emitter's wrapped pointer progression.
            var planeAddress = unchecked((uint)((ulong)allocationBase + HeaderBytes + planeBytes * (uint)plane));
            BinaryPrimitives.WriteUInt32BigEndian(
                expected.AsSpan(publicOffset + GraphicsLayouts.BitMapPlanes + plane * 4), planeAddress);
        }
        return expected;
    }

    private static void AssertAllocationMemory(Fixture fixture, MemorySnapshot expected)
    {
        Assert.Equal(fixture.Allocations.Count, fixture.MemoryAtAllocations.Count);
        foreach (var snapshot in fixture.MemoryAtAllocations)
            AssertMemoryEqual(expected, snapshot);
    }

    private static void AssertRetirementMemory(Fixture fixture, MemorySnapshot expected)
    {
        Assert.Equal(fixture.Frees.Count, fixture.MemoryAtFrees.Count);
        foreach (var snapshot in fixture.MemoryAtFrees)
            AssertMemoryEqual(expected, snapshot);
    }

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.SysBase, actual.SysBase);
        Assert.Equal(expected.High, actual.High);
        Assert.Equal(expected.Normal, actual.Normal);
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

    private static void AssertNoFailures(List<string> failures, string context)
        => Assert.True(failures.Count == 0, context + ":\n" + string.Join("\n", failures));

    private static void AssertReturn(CallResult result, uint expectedD0, bool expectFallback)
    {
        var differences = result.RegisterDifferences.ToList();
        if (result.Value != expectedD0)
            differences.Add($"D0 expected {expectedD0:X8}, actual {result.Value:X8}");
        if (result.UsedFallback != expectFallback)
            differences.Add($"private fallback expected {expectFallback}, observed {result.UsedFallback}");
        if (result.StackPointer != result.CallerStackPointer)
            differences.Add($"SP expected {result.CallerStackPointer:X8}, actual {result.StackPointer:X8}");
        if (result.ProgramCounter != result.CallerReturnAddress)
            differences.Add($"PC expected {result.CallerReturnAddress:X8}, actual {result.ProgramCounter:X8}");
        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    private sealed record NativeImage(
        byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] High, byte[] Normal, byte[] Low, byte[] GraphicsBase, uint SysBase);
    private sealed record CallResult(
        uint Value, bool UsedFallback, uint ProgramCounter, uint StackPointer,
        uint CallerReturnAddress, uint CallerStackPointer, string[] RegisterDifferences);

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
        private const uint NormalPhysicalBase = 0x00D0_0000;
        private const int NormalMemorySize = 0x200;
        private const uint HighPhysicalBase = 0x00FF_FF00;
        private const int HighMemorySize = 0x100;
        private const int LowMemorySize = 0x200;
        private static readonly uint[] DataCanaries =
        {
            0xD2D2_0202, 0xD3D3_0303, 0xD4D4_0404,
            0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505, GraphicsBase
        };
        private readonly IM68kCore _cpu;

        internal Fixture(uint allocationBase = NormalBase, bool malformedAllocation = false, bool returnNullAllocation = false)
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
            MapCanaries(HighPhysicalBase, HighMemorySize);
            MapCanaries(NormalPhysicalBase, NormalMemorySize);
            for (var offset = 0; offset < LowMemorySize; offset++)
                Bus.WriteByte((uint)offset, 0xA5, 0); // seed the built-in low-RAM decoder
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var size = state.D[0];
                var flags = state.D[1];
                var result = returnNullAllocation ? 0u : allocationBase;
                Allocations.Add((result, size, flags));
                MemoryAtAllocations.Add(CaptureMemory());
                // Aggregation probes return NULL even for a wrongly issued
                // large request. No test maps or clears a giant payload.
                if (result != 0 && !malformedAllocation && (flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                {
                    Assert.InRange(size, 1u, (uint)NormalMemorySize);
                    Assert.True((ulong)result + size - 1 <= uint.MaxValue);
                    var physicalBase = Physical(result);
                    if (physicalBase >= HighPhysicalBase)
                        Assert.True((ulong)physicalBase + size <= (ulong)HighPhysicalBase + HighMemorySize);
                    else
                        Assert.InRange(physicalBase, NormalPhysicalBase, NormalPhysicalBase + NormalMemorySize - size);
                    Bus.ClearMemory(physicalBase, checked((int)size));
                }
                // Only deliberately malformed non-NULL address providers
                // skip CLEAR; their bytes must stay untouched until rollback.
                state.D[0] = result;
                PoisonVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0])); // retain full logical address, never mask Exec arguments
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
        internal List<MemorySnapshot> MemoryAtAllocations { get; } = new();
        internal List<MemorySnapshot> MemoryAtFrees { get; } = new();
        private static uint Physical(uint address) => address & 0x00FF_FFFFu;

        private void MapCanaries(uint address, int length)
        {
            var memory = new byte[length];
            Array.Fill(memory, (byte)0xA5);
            Bus.MapWritableMemory(address, memory);
        }

        internal byte[] ReadLogicalBytes(uint address, int length)
            => Enumerable.Range(0, length)
                .Select(index => Bus.ReadByte(Physical(unchecked(address + (uint)index)))).ToArray();
        internal void WriteLong(uint address, uint value) => Bus.WriteLong(Physical(address), value);
        internal void SeedBytes(uint allocationBase, byte[] bytes)
        {
            for (var index = 0; index < bytes.Length; index++)
                Bus.WriteByte(Physical(unchecked(allocationBase + (uint)index)), bytes[index], 0);
            Assert.Equal(bytes, ReadLogicalBytes(allocationBase, bytes.Length));
        }
        internal void AssertExecBaseUnchanged() => Assert.Equal(ExecBase, Bus.ReadLong(4));
        internal MemorySnapshot CaptureMemory()
            => new(ReadLogicalBytes(HighPhysicalBase, HighMemorySize),
                ReadLogicalBytes(NormalPhysicalBase, NormalMemorySize), ReadLogicalBytes(0, LowMemorySize),
                ReadLogicalBytes(GraphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize), Bus.ReadLong(4));

        internal void AssertOutsideEnvelopeUnchanged(MemorySnapshot before, uint allocationBase, uint requested)
        {
            var after = CaptureMemory();
            var physicalBase = Physical(allocationBase);
            if (physicalBase >= HighPhysicalBase)
            {
                AssertOutsideWindowUnchanged(before.High, after.High, checked((int)(physicalBase - HighPhysicalBase)), requested);
                Assert.Equal(before.Normal, after.Normal);
            }
            else
            {
                AssertOutsideWindowUnchanged(before.Normal, after.Normal, checked((int)(physicalBase - NormalPhysicalBase)), requested);
                Assert.Equal(before.High, after.High);
            }
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsBase, after.GraphicsBase);
            Assert.Equal(before.SysBase, after.SysBase);
        }

        private static void AssertOutsideWindowUnchanged(byte[] before, byte[] after, int start, uint requested)
        {
            var end = checked(start + (int)requested);
            Assert.InRange(start, 0, before.Length - checked((int)requested));
            Assert.Equal(before.Length, after.Length);
            Assert.Equal(before.Take(start), after.Take(start));
            Assert.Equal(before.Skip(end), after.Skip(end));
        }

        private static void PoisonVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(GraphicsLvo vector, int depth, uint publicBitMap = 0, uint width = Width, ushort rows = Rows)
        {
            AssertExecBaseUnchanged();
            var allocating = vector == GraphicsLvo.AllocBitMap;
            var entry = CodeAddress + (uint)Image.Value.Entries[vector];
            var vectorAddress = checked((uint)((long)GraphicsBase + (int)vector));
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE);
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(ReturnAddress, 0x4E71);
            var expectedData = DataCanaries.ToArray();
            if (allocating)
            {
                expectedData[0] = (uint)depth;
                expectedData[1] = 3; // BMF_CLEAR | BMF_DISPLAYABLE
            }
            var originalD0 = allocating ? width : CapturedFreeD0;
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = originalD0;
            _cpu.State.D[1] = allocating ? rows : 0xD1D1_0101u;
            _cpu.State.A[0] = allocating ? 0 : publicBitMap;
            _cpu.State.A[1] = 0xA1A1_A1A1;
            for (var index = 0; index < expectedData.Length; index++)
                _cpu.State.D[index + 2] = expectedData[index];
            for (var index = 0; index < AddressCanaries.Length; index++)
                _cpu.State.A[index + 2] = AddressCanaries[index];
            _cpu.ExecuteInstruction();
            Assert.Equal(vectorAddress, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, Bus.ReadLong(_cpu.State.A[7]));
            var usedFallback = false;
            var arrivedByBranch = false;
            var enteredBody = false;
            for (var instruction = 0; instruction < 8192; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                var opcode = Bus.ReadWord(pc);
                enteredBody |= pc == entry;
                usedFallback |= pc == CodeAddress + (uint)Image.Value.Fallback ||
                    (opcode == 0x4E75 && arrivedByBranch && _cpu.State.D[0] == originalD0);
                uint? branchTarget = null;
                if ((opcode & 0xF000) == 0x6000 && (opcode & 0x0F00) != 0x0100)
                {
                    var displacement = (opcode & 0xFF) == 0
                        ? unchecked((short)Bus.ReadWord(pc + 2)) : unchecked((sbyte)opcode);
                    branchTarget = unchecked((uint)((long)pc + 2 + displacement));
                }
                _cpu.ExecuteInstruction();
                arrivedByBranch = branchTarget == _cpu.State.ProgramCounter;
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;
                Assert.True(enteredBody, "The physical BitMap public vector did not reach its native body.");
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
                return new CallResult(_cpu.State.D[0], usedFallback, _cpu.State.ProgramCounter, _cpu.State.A[7],
                    ReturnAddress, StackPointer, differences.ToArray());
            }
            throw new InvalidOperationException(
                $"{vector} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
