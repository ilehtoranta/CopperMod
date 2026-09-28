using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using ExecApi = global::Amiga.Exec;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsDBufInfoSpanTests
{
    private const uint CapturedD0 = 0xA1B2_C3D4;
    private const uint PrefixBytes = 4;
    private const uint RequestedBytes = PrefixBytes + (uint)GraphicsLayouts.DBufInfoSize;
    private const uint NormalBase = 0x00D0_0040;
    private const uint ExactEndBase = 0xFFFF_FFA8;
    private const uint AllocationFlags = (uint)(ExecApi.MemoryFlags.Public | ExecApi.MemoryFlags.Clear);
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });

    [Theory]
    [InlineData(NormalBase)]
    [InlineData(ExactEndBase)]
    public void CompleteDBufInfoEnvelopeAllocatesAndFreesThroughItsUnmodifiedPublicPointer(uint allocationBase)
    {
        var publicInfo = checked(allocationBase + PrefixBytes);
        Assert.True((ulong)allocationBase + RequestedBytes - 1 <= uint.MaxValue);
        if (allocationBase == ExactEndBase)
        {
            Assert.Equal(0xFFFF_FFACu, publicInfo);
            Assert.Equal((ulong)uint.MaxValue, (ulong)allocationBase + RequestedBytes - 1);
        }
        using var fixture = new Fixture(allocationBase);
        var before = fixture.CaptureMemory();
        var allocated = fixture.Invoke(GraphicsLvo.AllocDBufInfo);
        var failures = new List<string>();
        Check(failures, "allocation", () => Assert.Equal(
            (allocationBase, RequestedBytes, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "no premature release", () => Assert.Empty(fixture.Frees));
        Check(failures, "complete publication", () => fixture.AssertPublishedInfo(allocationBase));
        Check(failures, "outside envelope", () => fixture.AssertOutsideEnvelopeUnchanged(before, allocationBase));

        if (!allocated.UsedFallback && allocated.Value == publicInfo)
        {
            // No header repair or pointer substitution between the two
            // public calls: FreeDBufInfo receives exactly AllocDBufInfo's D0.
            var beforeFree = fixture.CaptureMemory();
            var freed = fixture.Invoke(GraphicsLvo.FreeDBufInfo, allocated.Value);
            Check(failures, "release", () => Assert.Equal(
                (allocationBase, RequestedBytes), Assert.Single(fixture.Frees)));
            Check(failures, "no allocation during release", () => Assert.Single(fixture.Allocations));
            Check(failures, "retirement memory", () => AssertRetirementMemory(fixture, beforeFree));
            Check(failures, "after release", () => AssertMemoryEqual(beforeFree, fixture.CaptureMemory()));
            Check(failures, "FreeDBufInfo ABI", () => AssertReturn(freed, 0, expectFallback: false));
        }
        else
        {
            failures.Add("FreeDBufInfo was not attempted because AllocDBufInfo did not return the native public pointer.");
        }
        Check(failures, "AllocDBufInfo ABI", () => AssertReturn(allocated, publicInfo, expectFallback: false));
        AssertNoFailures(failures, $"DBufInfo roundtrip private={allocationBase:X8}, public={publicInfo:X8}");
    }

    [Theory]
    [InlineData(0xFFFF_FFAAu)]
    [InlineData(0xFFFF_FFFCu)]
    public void WrappingDBufInfoAllocationRollsBackBeforeWritingEitherAddressAlias(uint allocationBase)
    {
        Assert.True((ulong)allocationBase + RequestedBytes - 1 > uint.MaxValue);
        using var fixture = new Fixture(allocationBase, malformedAllocation: true);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.AllocDBufInfo);
        var failures = new List<string>();
        Check(failures, "allocation", () => Assert.Equal(
            (allocationBase, RequestedBytes, AllocationFlags), Assert.Single(fixture.Allocations)));
        Check(failures, "exact provisional release", () => Assert.Equal(
            (allocationBase, RequestedBytes), Assert.Single(fixture.Frees)));
        Check(failures, "no publication before release", () => AssertRetirementMemory(fixture, before));
        Check(failures, "no publication after release", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "full-width fallback ABI", () => AssertReturn(result, CapturedD0, expectFallback: true));
        AssertNoFailures(failures, $"wrapping DBufInfo allocation {allocationBase:X8}");
    }

    [Fact]
    public void IndependentlySeededDBufInfoEndingAtLastGuestByteCanBeFreed()
    {
        Assert.Equal((ulong)uint.MaxValue, (ulong)ExactEndBase + RequestedBytes - 1);
        AssertSeededFree(ExactEndBase, accepted: true, seedCompleteEnvelope: true);
    }

    [Theory]
    [InlineData(0xFFFF_FFAEu)]
    [InlineData(0xFFFF_FFFCu)]
    public void MatchingDBufInfoMarkerCannotAuthorizeAWrappedCompleteEnvelope(uint publicInfo)
    {
        var allocationBase = checked(publicInfo - PrefixBytes);
        Assert.True((ulong)allocationBase + RequestedBytes - 1 > uint.MaxValue);
        AssertSeededFree(allocationBase, accepted: false, seedCompleteEnvelope: false);
    }

    [Fact]
    public void PublicPointerFourCannotAuthorizeAnImpossibleZeroPrivateOwner()
    {
        // Seed only the four private bytes at 0..3. SysBase at 4..7 is
        // intact by construction, never repaired after writing a fake object.
        AssertSeededFree(0, accepted: false, seedCompleteEnvelope: false);
    }

    [Fact]
    public void NullDBufInfoFreeSucceedsWithoutExecCallsOrMemoryMutation()
    {
        using var fixture = new Fixture();
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(GraphicsLvo.FreeDBufInfo, 0);
        var failures = new List<string>();
        Check(failures, "no allocation", () => Assert.Empty(fixture.Allocations));
        Check(failures, "no release", () => Assert.Empty(fixture.Frees));
        Check(failures, "no retirement callback", () => Assert.Empty(fixture.MemoryAtFrees));
        Check(failures, "no mutation", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "NULL ABI", () => AssertReturn(result, 0, expectFallback: false));
        AssertNoFailures(failures, "NULL DBufInfo");
    }

    private static void AssertSeededFree(uint allocationBase, bool accepted, bool seedCompleteEnvelope)
    {
        using var fixture = new Fixture();
        if (seedCompleteEnvelope)
        {
            fixture.SeedInfo(allocationBase);
            fixture.AssertPublishedInfo(allocationBase);
        }
        else
        {
            fixture.SeedPrivatePrefix(allocationBase);
        }
        fixture.AssertPrivatePrefix(allocationBase);
        fixture.AssertExecBaseUnchanged(); // before execution; never rewritten after seeding
        var before = fixture.CaptureMemory();
        var publicInfo = checked(allocationBase + PrefixBytes);
        var result = fixture.Invoke(GraphicsLvo.FreeDBufInfo, publicInfo);
        var failures = new List<string>();
        Check(failures, "no allocation", () => Assert.Empty(fixture.Allocations));
        Check(failures, "ownership retirement", () => Assert.Equal(
            accepted ? new[] { (allocationBase, RequestedBytes) } : Array.Empty<(uint, uint)>(), fixture.Frees));
        Check(failures, "memory at any retirement", () => AssertRetirementMemory(fixture, before));
        Check(failures, "no mutation", () => AssertMemoryEqual(before, fixture.CaptureMemory()));
        Check(failures, "public ABI", () => AssertReturn(result, accepted ? 0u : CapturedD0, !accepted));
        AssertNoFailures(failures, $"seeded DBufInfo private={allocationBase:X8}, public={publicInfo:X8}");
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
        Assert.Equal(expected.SysBase, actual.SysBase);
        Assert.Equal(expected.High, actual.High);
        Assert.Equal(expected.Normal, actual.Normal);
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
    private sealed record MemorySnapshot(
        byte[] High, byte[] Normal, byte[] Low, byte[] Structures, byte[] GraphicsBase, uint SysBase);
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
        private const uint StructuresAddress = 0x00D1_0000;
        private const uint ViewPort = StructuresAddress;
        private const uint RasInfo = StructuresAddress + 0x60;
        private const int StructuresSize = 0x100;
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

        internal Fixture(uint allocationBase = NormalBase, bool malformedAllocation = false)
        {
            Assert.Equal(88u, RequestedBytes);
            Assert.Equal(20, GraphicsLayouts.ExecMessageSize);
            Assert.Equal(malformedAllocation, (ulong)allocationBase + RequestedBytes - 1 > uint.MaxValue);
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
            Bus.MapWritableMemory(StructuresAddress, new byte[StructuresSize]);
            Bus.WriteLong(ViewPort + (uint)GraphicsLayouts.ViewPortRasInfo, RasInfo);
            Bus.WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoNext, 0);
            Bus.WriteLong(RasInfo + (uint)GraphicsLayouts.RasInfoBitMap, 0);
            // Low RAM's built-in decoder takes precedence over a mapped
            // byte array. Seed through the bus so its canaries are real.
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
                Assert.Equal(RequestedBytes, size);
                Assert.Equal(AllocationFlags, flags);
                // Only the two deliberately malformed providers skip CLEAR:
                // the native body must reject their full logical envelopes
                // before publishing either high or wrapped low bytes.
                if (!malformedAllocation && (flags & (uint)ExecApi.MemoryFlags.Clear) != 0)
                {
                    Assert.True((ulong)allocationBase + size - 1 <= uint.MaxValue);
                    Bus.ClearMemory(Physical(allocationBase), checked((int)size));
                }
                state.D[0] = allocationBase;
                PoisonVolatileRegisters(state, preserveD0: true);
            });
            Bus.RegisterHostGateway(ExecBase - 210, state =>
            {
                Assert.Equal(ExecBase, state.A[6]);
                // Do not mask the pointer here. Exec receives the original
                // logical allocation base in A1 and all 88 bytes in D0.
                Frees.Add((state.A[1], state.D[0]));
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

        private byte[] ReadLogicalBytes(uint address, int length)
            => Enumerable.Range(0, length)
                .Select(index => Bus.ReadByte(Physical(unchecked(address + (uint)index)))).ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(ReadLogicalBytes(HighPhysicalBase, HighMemorySize),
                ReadLogicalBytes(NormalPhysicalBase, NormalMemorySize), ReadLogicalBytes(0, LowMemorySize),
                ReadLogicalBytes(StructuresAddress, StructuresSize),
                ReadLogicalBytes(GraphicsBase, NativeGraphicsLibraryImageBuilder.PositiveImageSize), Bus.ReadLong(4));

        internal void SeedPrivatePrefix(uint allocationBase)
        {
            Bus.WriteWord(Physical(allocationBase), 0x4442);
            Bus.WriteWord(Physical(unchecked(allocationBase + 2)), 0);
            // Never clear an entire forged envelope here: a private-zero
            // or wrapped owner must not corrupt SysBase during test setup.
        }

        internal void AssertPrivatePrefix(uint allocationBase)
        {
            Assert.Equal((ushort)0x4442, Bus.ReadWord(Physical(allocationBase)));
            Assert.Equal((ushort)0, Bus.ReadWord(Physical(unchecked(allocationBase + 2))));
        }

        internal void SeedInfo(uint allocationBase)
        {
            var expected = ExpectedEnvelope();
            Assert.True((ulong)allocationBase + RequestedBytes - 1 <= uint.MaxValue);
            for (var index = 0; index < expected.Length; index++)
                Bus.WriteByte(Physical(unchecked(allocationBase + (uint)index)), expected[index], 0);
        }

        internal void AssertPublishedInfo(uint allocationBase)
            => Assert.Equal(ExpectedEnvelope(), ReadLogicalBytes(allocationBase, checked((int)RequestedBytes)));

        internal void AssertExecBaseUnchanged() => Assert.Equal(ExecBase, Bus.ReadLong(4));

        internal void AssertOutsideEnvelopeUnchanged(MemorySnapshot before, uint allocationBase)
        {
            var after = CaptureMemory();
            var physicalBase = Physical(allocationBase);
            if (physicalBase >= HighPhysicalBase)
            {
                var start = checked((int)(physicalBase - HighPhysicalBase));
                AssertOutsideWindowUnchanged(before.High, after.High, start);
                Assert.Equal(before.Normal, after.Normal);
            }
            else
            {
                Assert.InRange(physicalBase, NormalPhysicalBase,
                    NormalPhysicalBase + (uint)NormalMemorySize - RequestedBytes);
                var start = checked((int)(physicalBase - NormalPhysicalBase));
                AssertOutsideWindowUnchanged(before.Normal, after.Normal, start);
                Assert.Equal(before.High, after.High);
            }
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.Structures, after.Structures);
            Assert.Equal(before.GraphicsBase, after.GraphicsBase);
            Assert.Equal(before.SysBase, after.SysBase);
        }

        private static void AssertOutsideWindowUnchanged(byte[] before, byte[] after, int start)
        {
            var end = checked(start + (int)RequestedBytes);
            Assert.InRange(start, 0, before.Length - checked((int)RequestedBytes));
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

        internal CallResult Invoke(GraphicsLvo vector, uint publicInfo = 0)
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
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = vector == GraphicsLvo.AllocDBufInfo ? ViewPort : 0xA0A0_A0A1;
            _cpu.State.A[1] = vector == GraphicsLvo.FreeDBufInfo ? publicInfo : 0xA1A1_A1A1;
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
                    (opcode == 0x4E75 && arrivedByBranch && _cpu.State.D[0] == CapturedD0);
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

                Assert.True(enteredBody, "The physical DBufInfo public vector did not reach its native body.");
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
