using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRasterSpanTests
{
    private const uint PrefixBytes = 10;
    private const uint RequestedBytes = 22;
    private const uint NormalBase = 0x00D0_0040;
    private const uint WidthArgument = 0xABCD_0011;
    private const uint HeightArgument = 0x1234_0003;
    private const uint AllocationFlags =
        (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Chip | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData(17u, 3u)]
    [InlineData(WidthArgument, HeightArgument)]
    public void RasterEnvelopeEndingAtLastGuestByteAllocatesAndFreesNatively(uint width, uint height)
    {
        const uint allocationBase = 0xFFFF_FFEA;
        const uint raster = 0xFFFF_FFF4;
        Assert.Equal((ulong)uint.MaxValue, (ulong)allocationBase + RequestedBytes - 1);
        using var fixture = new Fixture(allocationBase);
        var before = fixture.CaptureMemory();
        var allocated = fixture.Invoke(GraphicsLvo.AllocRaster, width, height);
        var failures = new List<string>();
        Check(failures, "allocation", () => Assert.Equal(
            (allocationBase, RequestedBytes, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "no premature release", () => Assert.Empty(fixture.Frees));
        Check(failures, "header", () => fixture.AssertHeader(allocationBase, 17, 3, RequestedBytes));
        Check(failures, "payload", () => Assert.All(fixture.ReadLogicalBytes(raster, 12),
            value => Assert.Equal((byte)0, value)));
        Check(failures, "outside envelope", () => fixture.AssertOutsideHighEnvelopeUnchanged(
            before, allocationBase, RequestedBytes));

        if (!allocated.UsedFallback && allocated.Value == raster)
        {
            var beforeFree = fixture.CaptureMemory();
            var freed = fixture.Invoke(GraphicsLvo.FreeRaster, width, height, raster);
            Check(failures, "release", () => Assert.Equal(
                (allocationBase, RequestedBytes), Assert.Single(fixture.Frees)));
            Check(failures, "no allocation during release", () => Assert.Single(fixture.Allocations));
            Check(failures, "retirement memory", () => AssertRetirementMemory(fixture, beforeFree));
            Check(failures, "after release", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
            Check(failures, "FreeRaster ABI", () => AssertReturn(freed, 0, expectFallback: false));
        }
        else
        {
            failures.Add("FreeRaster was not attempted because AllocRaster did not return the native raster.");
        }
        Check(failures, "AllocRaster ABI", () => AssertReturn(allocated, raster, expectFallback: false));
        AssertNoFailures(failures, $"exact-end raster, D0={width:X8}, D1={height:X8}");
    }

    [Theory]
    [InlineData(0xFFFF_FFECu, 17u, 3u)]
    [InlineData(0xFFFF_FFECu, WidthArgument, HeightArgument)]
    [InlineData(0xFFFF_FFF8u, 17u, 3u)]
    [InlineData(0xFFFF_FFF8u, WidthArgument, HeightArgument)]
    public void WrappingRasterAllocationRollsBackBeforeWritingEitherAddressAlias(
        uint allocationBase, uint width, uint height)
    {
        Assert.True((ulong)allocationBase + RequestedBytes - 1 > uint.MaxValue);
        using var fixture = new Fixture(allocationBase);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.AllocRaster, width, height);
        var failures = new List<string>();
        Check(failures, "allocation", () => Assert.Equal(
            (allocationBase, RequestedBytes, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "exact provisional release", () => Assert.Equal(
            (allocationBase, RequestedBytes), Assert.Single(fixture.Frees)));
        Check(failures, "no publication before release", () => AssertRetirementMemory(fixture, before));
        Check(failures, "no publication after release", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "full-width fallback ABI", () => AssertReturn(result, width, expectFallback: true));
        AssertNoFailures(failures, $"wrapping allocation {allocationBase:X8}, D0={width:X8}");
    }

    [Theory]
    [InlineData(17u, 3u)]
    [InlineData(WidthArgument, HeightArgument)]
    public void MatchingRasterHeaderCannotAuthorizeAWrappedCompleteEnvelope(uint width, uint height)
        => AssertSeededFree(0xFFFF_FFEC, width, height, RequestedBytes, accepted: false);

    [Theory]
    [InlineData(0u, false, true)]
    [InlineData(1u, false, true)]
    [InlineData(10u, false, true)]
    [InlineData(21u, false, true)]
    [InlineData(24u, false, true)]
    [InlineData(uint.MaxValue, false, true)]
    [InlineData(22u, true, true)]
    [InlineData(22u, true, false)]
    public void StoredRasterSpanMustExactlyMatchTheNormalizedDimensions(
        uint storedSpan, bool accepted, bool highWordDimensions)
        => AssertSeededFree(NormalBase,
            highWordDimensions ? WidthArgument : 17u,
            highWordDimensions ? HeightArgument : 3u, storedSpan, accepted);

    [Theory]
    [InlineData(17u, 119u)]
    [InlineData(WidthArgument, 0x1234_0077u)]
    public void PublicPointerTenCannotAuthorizeAnImpossibleZeroPrivateOwner(uint width, uint height)
    {
        // At private address zero, header bytes 4..7 are height 0x0077
        // and span's high word 0x0000: exactly SysBase 0x00770000.
        // Never repair SysBase after seeding, which would erase evidence.
        Assert.Equal(486u, 4u * (height & ushort.MaxValue) + PrefixBytes);
        AssertSeededFree(0, width, height, 0x0000_01E6, accepted: false);
    }

    [Theory]
    [InlineData(65535u, 65535u, 0x1FFF_E00Au, true)]
    [InlineData(0xABCD_FFFFu, 0x1234_FFFFu, 0x1FFF_E00Au, true)]
    [InlineData(0xABCD_FFFFu, 0x1234_FFFFu, 0x2000_E00Au, false)]
    public void SeededMaximumRasterSizeIsRecomputedAsACompleteLongword(
        uint width, uint height, uint storedSpan, bool accepted)
    {
        Assert.Equal(536_862_730u, 8192u * (height & ushort.MaxValue) + PrefixBytes);
        // The mock only records FreeMem; it never allocates, clears, or
        // releases a real half-gigabyte block for these header-only probes.
        AssertSeededFree(NormalBase, width, height, storedSpan, accepted);
    }

    private static void AssertSeededFree(
        uint allocationBase, uint width, uint height, uint storedSpan, bool accepted)
    {
        using var fixture = new Fixture();
        var wordWidth = (ushort)(width & ushort.MaxValue);
        var wordHeight = (ushort)(height & ushort.MaxValue);
        fixture.SeedRaster(allocationBase, wordWidth, wordHeight, storedSpan);
        fixture.AssertHeader(allocationBase, wordWidth, wordHeight, storedSpan);
        fixture.AssertExecBaseUnchanged(); // before execution, never rewritten
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.FreeRaster, width, height,
            checked(allocationBase + PrefixBytes));
        var failures = new List<string>();
        Check(failures, "no allocation", () => Assert.Empty(fixture.Allocations));
        Check(failures, "ownership retirement", () => Assert.Equal(
            accepted ? new[] { (allocationBase, storedSpan) } : Array.Empty<(uint, uint)>(), fixture.Frees));
        Check(failures, "memory at any retirement", () => AssertRetirementMemory(fixture, before));
        Check(failures, "no mutation", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "public ABI", () => AssertReturn(result, accepted ? 0u : width, !accepted));
        AssertNoFailures(failures,
            $"private={allocationBase:X8}, span={storedSpan:X8}, D0={width:X8}, D1={height:X8}");
    }

    private static void AssertRetirementMemory(Fixture fixture, MemorySnapshot before)
    {
        Assert.Equal(fixture.Frees.Count, fixture.MemoryAtFrees.Count);
        foreach (var snapshot in fixture.MemoryAtFrees)
            AssertMemoryEqual(before, snapshot);
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

    private static void AssertReturn(CallResult call, uint expectedD0, bool expectFallback)
    {
        var differences = call.RegisterDifferences.ToList();
        if (call.Value != expectedD0)
            differences.Add($"D0 expected {expectedD0:X8}, actual {call.Value:X8}");
        if (call.UsedFallback != expectFallback)
            differences.Add($"private fallback expected {expectFallback}, observed {call.UsedFallback}");
        if (call.StackPointer != call.CallerStackPointer)
            differences.Add($"SP expected {call.CallerStackPointer:X8}, actual {call.StackPointer:X8}");
        if (call.ProgramCounter != call.CallerReturnAddress)
            differences.Add($"PC expected {call.CallerReturnAddress:X8}, actual {call.ProgramCounter:X8}");
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

        internal Fixture(uint allocationBase = NormalBase)
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
            MapCanaries(0, LowMemorySize);
            // Low RAM's built-in decoder takes precedence over a mapped
            // byte array; seed through the bus so its canaries are real.
            for (var offset = 0; offset < LowMemorySize; offset++)
                Bus.WriteByte((uint)offset, 0xA5, 0);
            Bus.WriteLong(4, ExecBase);
            Bus.MapWritableMemory(CallerAddress, new byte[8]);
            Bus.MapWritableMemory(StackAddress, new byte[0x600]);
            Bus.RegisterHostGateway(ExecBase - 198, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                var size = state.D[0];
                var flags = state.D[1];
                Allocations.Add((allocationBase, size, flags));
                // A bad provider leaves wrapping aliases untouched. Native
                // admission must reject them before any header publication.
                if (size != 0 && (ulong)allocationBase + size - 1 <= uint.MaxValue &&
                    (flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                {
                    Assert.InRange(size, 1u, (uint)NormalMemorySize);
                    Bus.ClearMemory(Physical(allocationBase), checked((int)size));
                }
                state.D[0] = allocationBase;
                PoisonVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                Frees.Add((state.A[1], state.D[0])); // real Exec FreeMem ABI
                MemoryAtFrees.Add(CaptureMemory());
                PoisonVolatileRegisters(state, preserveD0: false);
            });
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, Bus);
        }

        private AmigaBus Bus { get; } = new();
        internal List<(uint Address, uint Size, uint Flags)> Allocations { get; } = new();
        internal List<(uint Address, uint Size)> Frees { get; } = new();
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

        internal MemorySnapshot CaptureMemory()
            => new(ReadLogicalBytes(HighPhysicalBase, HighMemorySize),
                ReadLogicalBytes(NormalPhysicalBase, NormalMemorySize), ReadLogicalBytes(0, LowMemorySize),
                ReadLogicalBytes(GraphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize), Bus.ReadLong(4));

        internal void SeedRaster(uint allocationBase, ushort width, ushort height, uint storedSpan)
        {
            Bus.WriteWord(Physical(allocationBase), 0x5253);
            Bus.WriteWord(Physical(unchecked(allocationBase + 2)), width);
            Bus.WriteWord(Physical(unchecked(allocationBase + 4)), height);
            Bus.WriteLong(Physical(unchecked(allocationBase + 6)), storedSpan);
            // Intentionally do not rewrite address four after seeding.
        }

        internal void AssertHeader(uint allocationBase, ushort width, ushort height, uint storedSpan)
        {
            Assert.Equal((ushort)0x5253, Bus.ReadWord(Physical(allocationBase)));
            Assert.Equal(width, Bus.ReadWord(Physical(unchecked(allocationBase + 2))));
            Assert.Equal(height, Bus.ReadWord(Physical(unchecked(allocationBase + 4))));
            Assert.Equal(storedSpan, Bus.ReadLong(Physical(unchecked(allocationBase + 6))));
        }

        internal void AssertExecBaseUnchanged() => Assert.Equal(ExecBase, Bus.ReadLong(4));

        internal void AssertOutsideHighEnvelopeUnchanged(
            MemorySnapshot before, uint allocationBase, uint size)
        {
            var after = CaptureMemory();
            var start = checked((int)(Physical(allocationBase) - HighPhysicalBase));
            var end = start + (int)size;
            Assert.Equal(before.High.Take(start), after.High.Take(start));
            Assert.Equal(before.High.Skip(end), after.High.Skip(end));
            Assert.Equal(before.Normal, after.Normal);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsBase, after.GraphicsBase);
            Assert.Equal(before.SysBase, after.SysBase);
        }

        private static void PoisonVolatileRegisters(M68kCpuState state, bool preserveD0)
        {
            if (!preserveD0)
                state.D[0] = 0xD0D0_D0D0;
            state.D[1] = 0xD1D1_D1D1;
            state.A[0] = 0xA0A0_A0A1;
            state.A[1] = 0xA1A1_A1A1;
        }

        internal CallResult Invoke(GraphicsLvo vector, uint width, uint height, uint raster = 0)
        {
            AssertExecBaseUnchanged();
            var entry = CodeAddress + (uint)Image.Value.Entries[vector];
            var vectorAddress = checked((uint)((long)GraphicsBase + (int)vector));
            Assert.Equal((ushort)0x4EF9, Bus.ReadWord(vectorAddress));
            Assert.Equal(entry, Bus.ReadLong(vectorAddress + 2));
            Bus.WriteWord(CallerAddress, 0x4EAE); // actual JSR d16(A6)
            Bus.WriteWord(CallerAddress + 2, unchecked((ushort)(short)vector));
            Bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu.Reset(CallerAddress, StackPointer);
            _cpu.State.D[0] = width;
            _cpu.State.D[1] = height;
            _cpu.State.A[0] = raster;
            _cpu.State.A[1] = 0xA1A1_0101;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
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
                    (opcode == 0x4E75 && arrivedByBranch && _cpu.State.D[0] == width);
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

                Assert.True(enteredBody, "The physical graphics vector did not reach its native body.");
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
