using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsDisplayModeQueryPublicAbiTests
{
    private const int FindDisplayInfoLvo = -726;
    private const int ModeNotAvailableLvo = -798;
    private const uint ForeignMode = GraphicsModeIds.VgaMonitor;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(
            Image.Value.Code,
            Image.Value.Entries,
            Image.Value.Fallback));

    public static IEnumerable<object[]> CanonicalCases()
    {
        yield return Case(
            "find-invalid",
            FindDisplayInfoLvo,
            GraphicsModeIds.Invalid,
            0x03,
            0,
            false);
        yield return Case(
            "find-default",
            FindDisplayInfoLvo,
            GraphicsModeIds.DefaultMonitor,
            0x03,
            GraphicsDisplayDatabase.DefaultModeHandle,
            false);
        yield return Case(
            "find-recognized",
            FindDisplayInfoLvo,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey,
            0x03,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey,
            false);
        yield return Case(
            "find-foreign",
            FindDisplayInfoLvo,
            ForeignMode,
            0x03,
            ForeignMode,
            true);
        yield return Case(
            "availability-invalid",
            ModeNotAvailableLvo,
            GraphicsModeIds.Invalid,
            0x03,
            GraphicsDisplayDatabase.DiAvailNoMonitor,
            false);
        yield return Case(
            "availability-ordinary",
            ModeNotAvailableLvo,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey,
            0,
            0,
            false);
        yield return Case(
            "availability-superhires",
            ModeNotAvailableLvo,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey,
            0x03,
            0,
            false);
        yield return Case(
            "availability-no-agnus",
            ModeNotAvailableLvo,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey,
            0x02,
            GraphicsDisplayDatabase.DiAvailNoChips,
            false);
        yield return Case(
            "availability-no-denise",
            ModeNotAvailableLvo,
            GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey,
            0x01,
            GraphicsDisplayDatabase.DiAvailNoChips,
            false);
        yield return Case(
            "availability-foreign",
            ModeNotAvailableLvo,
            ForeignMode,
            0x03,
            ForeignMode,
            true);
    }

    public static IEnumerable<object[]> PublicAbiCases()
    {
        foreach (var canonical in CanonicalCases())
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
            yield return canonical.Concat(new object[] { relocated, autoInitEntry }).ToArray();
    }

    public static IEnumerable<object[]> CapabilityAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary-null",
            "invalid-null",
            "foreign-null",
            "superhires-library-base",
            "superhires-first-even",
            "superhires-last-aligned",
            "superhires-null",
            "superhires-odd",
            "superhires-exact-end",
            "superhires-first-wrap",
            "superhires-final-even",
            "superhires-final-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(CanonicalCases))]
    public void DisplayModeQueryExistingResultsRemainStable(
        string scenario,
        int lvo,
        uint modeId,
        byte chipRevision,
        uint expected,
        bool expectedFallback)
    {
        using var fixture = new Fixture(lvo, relocated: false, autoInitEntry: false, chipRevision);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(modeId, expectedFallback);
        var after = fixture.CaptureMemory();

        Assert.Equal(expected, result.Data0);
        Assert.Equal(expectedFallback, result.UsedFallback);
        Assert.Equal(1, result.NativeReturnCount);
        Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
        Assert.Equal(Fixture.StackPointer, result.StackPointer);
        AssertMemoryEqual(before, after);
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void DisplayModeQueryPublicEntriesPreserveD2D7AndA2A6(
        string scenario,
        int lvo,
        uint modeId,
        byte chipRevision,
        uint expected,
        bool expectedFallback,
        bool relocated,
        bool autoInitEntry)
    {
        using var fixture = new Fixture(lvo, relocated, autoInitEntry, chipRevision);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(modeId, expectedFallback);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();

        Check(failures, "canonical result and provider provenance", () =>
        {
            Assert.Equal(expected, result.Data0);
            Assert.Equal(expectedFallback, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "real caller PC/SP and library base", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "native image, resident, caller and stack guards", () =>
            AssertMemoryEqual(before, after));
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        Assert.True(
            failures.Count == 0,
            $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(CapabilityAdmissionCases))]
    public void ModeNotAvailableAdmitsOnlyAnAlignedNonWrappingCapabilityByte(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(
            ModeNotAvailableLvo,
            relocated,
            autoInitEntry,
            (byte)GraphicsChipRevision.SetEcs);
        var modeId = GraphicsModeIds.PalMonitor | GraphicsModeIds.SuperHiresKey;
        var graphicsBase = fixture.GraphicsBase;
        var expected = 0u;
        var expectedFallback = false;
        var expectedCapabilityReads = 1;
        var maximumBase = uint.MaxValue - (uint)GraphicsLayouts.GfxBaseChipRevBits0;
        var lastAlignedBase = maximumBase & ~1u;

        switch (scenario)
        {
            case "ordinary-null":
                modeId = GraphicsModeIds.PalMonitor | GraphicsModeIds.HiresKey;
                graphicsBase = 0;
                expectedCapabilityReads = 0;
                break;
            case "invalid-null":
                modeId = GraphicsModeIds.Invalid;
                graphicsBase = 0;
                expected = GraphicsDisplayDatabase.DiAvailNoMonitor;
                expectedCapabilityReads = 0;
                break;
            case "foreign-null":
                modeId = ForeignMode;
                graphicsBase = 0;
                expected = ForeignMode;
                expectedFallback = true;
                expectedCapabilityReads = 0;
                break;
            case "superhires-library-base":
                break;
            case "superhires-first-even":
                graphicsBase = 2;
                break;
            case "superhires-last-aligned":
                graphicsBase = lastAlignedBase;
                break;
            case "superhires-null":
                graphicsBase = 0;
                expected = modeId;
                expectedFallback = true;
                expectedCapabilityReads = 0;
                break;
            case "superhires-odd":
                graphicsBase = 3;
                expected = modeId;
                expectedFallback = true;
                expectedCapabilityReads = 0;
                break;
            case "superhires-exact-end":
                graphicsBase = maximumBase;
                expected = modeId;
                expectedFallback = true;
                expectedCapabilityReads = 0;
                break;
            case "superhires-first-wrap":
                graphicsBase = maximumBase + 1u;
                expected = modeId;
                expectedFallback = true;
                expectedCapabilityReads = 0;
                break;
            case "superhires-final-even":
                graphicsBase = 0xFFFF_FFFEu;
                expected = modeId;
                expectedFallback = true;
                expectedCapabilityReads = 0;
                break;
            case "superhires-final-odd":
                graphicsBase = uint.MaxValue;
                expected = modeId;
                expectedFallback = true;
                expectedCapabilityReads = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        Assert.Equal(0u, lastAlignedBase & 1u);
        Assert.Equal(
            (ulong)uint.MaxValue - 1u,
            (ulong)lastAlignedBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0);
        if (scenario == "superhires-exact-end")
        {
            Assert.Equal(1u, graphicsBase & 1u);
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0);
        }
        if (scenario == "superhires-first-wrap")
        {
            Assert.Equal(0u, graphicsBase & 1u);
            Assert.True(
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseChipRevBits0 >
                uint.MaxValue);
        }

        fixture.SeedChipRevision(graphicsBase, (byte)GraphicsChipRevision.SetEcs);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(
            modeId,
            expectedFallback,
            graphicsBase,
            publicVector: false);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        // Relocated HUNK mapped-memory reads use the core's verified direct
        // read window and therefore do not publish an AmigaBus byte ledger
        // row.  Their result/fallback assertions still freeze admission;
        // fixed-image routes additionally prove the exact read count.
        var expectedObservableCapabilityReads = relocated
            ? 0
            : expectedCapabilityReads;

        Check(failures, "result and native/fallback provenance", () =>
        {
            Assert.Equal(expected, result.Data0);
            Assert.Equal(expectedFallback, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "capability-byte read count", () =>
            Assert.True(
                result.CapabilityReadCount == expectedObservableCapabilityReads,
                $"Expected {expectedObservableCapabilityReads}, actual " +
                $"{result.CapabilityReadCount}; CPU byte reads: " +
                string.Join(", ", result.CpuByteDataReadAddresses.Select(
                    address => $"{address:X8}"))));
        Check(failures, "caller PC/SP and supplied A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "native image, resident, caller, stack, low and high guards", () =>
            AssertMemoryEqual(before, after));
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(
                result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        Assert.True(
            failures.Count == 0,
            $"{fixture.DescribeRoute(publicVector: false)}/{scenario}:\n" +
            string.Join("\n", failures));
    }

    private static object[] Case(
        string name,
        int lvo,
        uint modeId,
        byte chipRevision,
        uint expected,
        bool fallback)
        => new object[] { name, lvo, modeId, chipRevision, expected, fallback };

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
        Assert.Equal(expected.Resident, actual.Resident);
        Assert.Equal(expected.Caller, actual.Caller);
        Assert.Equal(expected.StackGuards, actual.StackGuards);
        Assert.Equal(expected.Low, actual.Low);
        Assert.Equal(expected.High, actual.High);
    }

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try
        {
            assertion();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record NativeImage(
        byte[] Code,
        IReadOnlyDictionary<GraphicsLvo, int> Entries,
        int Fallback);

    private sealed record MemorySnapshot(
        byte[] GraphicsImage,
        byte[] Resident,
        byte[] Caller,
        byte[] StackGuards,
        byte[] Low,
        byte[] High);

    private sealed record CallResult(
        uint Data0,
        uint Address6,
        uint ProgramCounter,
        uint StackPointer,
        bool UsedFallback,
        int NativeReturnCount,
        int CapabilityReadCount,
        uint[] CpuByteDataReadAddresses,
        string[] RegisterDifferences);

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        private const uint StackAddress = 0x00C7_0000;
        private const uint HighPhysicalAddress = 0x00FF_FF00;
        internal const uint StackPointer = StackAddress + 0x300;
        private static readonly uint[] DataCanaries =
        {
            0xD2D2_0202, 0xD3D3_0303, 0xD4D4_0404,
            0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private static readonly uint[] AddressCanaries =
        {
            0xA2A2_0202, 0xA3A3_0303, 0xA4A4_0404, 0xA5A5_0505
        };
        private readonly AmigaBus _bus = new();
        private readonly IM68kCore _cpu;
        private readonly bool _autoInitEntry;
        private readonly bool _relocated;
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _vectorSlot;
        private readonly uint _functionEntry;
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;

        internal Fixture(int lvo, bool relocated, bool autoInitEntry, byte chipRevision)
        {
            Assert.True(lvo is FindDisplayInfoLvo or ModeNotAvailableLvo);
            var vector = (GraphicsLvo)lvo;
            Assert.Equal(lvo, (int)vector);
            _autoInitEntry = autoInitEntry;
            _relocated = relocated;
            Route = vector + "/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
                    (autoInitEntry ? "AUTOINIT JSR(A2)" : "public JSR d16(A6)");

            if (relocated)
            {
                var residentByteCount = 0;
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(_bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True(
                        (ulong)address + (uint)size <= limit,
                        "HUNK fixture segments overlap.");
                    _bus.MapWritableMemory(address, new byte[size]);
                    if (address == HunkResidentAddress)
                        residentByteCount = size;
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(addresses);
                Assert.True(residentByteCount > 0);
                GraphicsBase = program.SegmentBases[0] + (uint)Hunk.Value.VectorOffset;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
                _residentAddress = program.SegmentBases[1];
                _residentByteCount = residentByteCount;
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(
                    item => item.Key,
                    item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    FixedGraphicsBase,
                    FixedResidentAddress,
                    fallback,
                    entries,
                    fallback);
                Assert.True(
                    (ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                _bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                _bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                _bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                _bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                GraphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
                _residentAddress = library.ResidentAddress;
                _residentByteCount = library.ResidentBytes.Length;
            }

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            _vectorSlot = checked((uint)((long)GraphicsBase + lvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            var functionOrdinal = checked((uint)((-lvo / 6) - 1));
            _functionEntry = _bus.ReadLong(functionArray + functionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(
                HighPhysicalAddress,
                Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            for (var address = 0u; address < 0x400; address++)
                _bus.WriteByte(address, 0xC3, 0);
            SeedChipRevision(GraphicsBase, chipRevision);
            _bus.MapWritableMemory(
                StackAddress,
                Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            _bus.MapWritableMemory(
                CallerAddress,
                Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
            {
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2).
            }
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)lvo));
            }

            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(
                M68kBackendKind.AccurateM68000,
                _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal string DescribeRoute(bool publicVector)
            => GraphicsLvo.ModeNotAvailable + "/" +
               (_relocated ? "relocated HUNK" : "fixed image") + "/" +
               (_autoInitEntry ? "AUTOINIT JSR(A2)" : publicVector
                   ? "public JSR d16(A6)"
                   : "direct native body");

        internal void SeedChipRevision(uint graphicsBase, byte chipRevision)
        {
            var address = CapabilityPhysicalAddress(graphicsBase);
            _bus.WriteByte(address, chipRevision, 0);
            Assert.Equal(chipRevision, _bus.ReadByte(address));
        }

        private static uint CapabilityPhysicalAddress(uint graphicsBase)
            => ((graphicsBase & 0x00FF_FFFFu) +
                (uint)GraphicsLayouts.GfxBaseChipRevBits0) & 0x00FF_FFFFu;

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count)
                .Select(index => _bus.ReadByte(address + (uint)index))
                .ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(
                ReadBytes(
                    GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize +
                    NativeGraphicsLibraryImageBuilder.PositiveImageSize),
                ReadBytes(_residentAddress, _residentByteCount),
                ReadBytes(CallerAddress, 8),
                ReadBytes(StackAddress, 0x2C0)
                    .Concat(ReadBytes(StackPointer, 0x300))
                    .ToArray(),
                ReadBytes(0, 0x400),
                ReadBytes(HighPhysicalAddress, 0x100));

        internal CallResult Invoke(uint modeId, bool expectedFallback)
            => Invoke(modeId, expectedFallback, GraphicsBase, publicVector: true);

        internal CallResult Invoke(
            uint modeId,
            bool expectedFallback,
            uint graphicsBase,
            bool publicVector)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = _functionEntry;

            var entryStackPointer = StackPointer;
            var entryProgramCounter = CallerAddress;
            if (!_autoInitEntry && !publicVector)
            {
                entryStackPointer -= 4u;
                _bus.WriteLong(entryStackPointer, ReturnAddress);
                entryProgramCounter = _entry;
            }

            _cpu.Reset(entryProgramCounter, entryStackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = modeId;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = 0xA0A0_0000;
            _cpu.State.A[1] = 0xA1A1_0101;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = graphicsBase;
            var busAccessStart = _bus.BusAccesses.Count;

            if (_autoInitEntry || publicVector)
            {
                _cpu.ExecuteInstruction();
                Assert.Equal(
                    _autoInitEntry ? _functionEntry : _vectorSlot,
                    _cpu.State.ProgramCounter);
                Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
                Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
                if (!_autoInitEntry)
                    _cpu.ExecuteInstruction();
            }
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 1_500; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(
                    pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}.");
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                if (_bus.ReadWord(pc) == 0x4E75)
                {
                    nativeReturnCount++;
                    usedFallback |= expectedFallback && _cpu.State.D[0] == modeId;
                }

                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;

                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    if (_cpu.State.D[index + 2] != DataCanaries[index])
                    {
                        differences.Add(
                            $"D{index + 2} expected {DataCanaries[index]:X8}, " +
                            $"actual {_cpu.State.D[index + 2]:X8}");
                    }
                }

                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    if (_cpu.State.A[index + 2] != expectedAddresses[index])
                    {
                        differences.Add(
                            $"A{index + 2} expected {expectedAddresses[index]:X8}, " +
                            $"actual {_cpu.State.A[index + 2]:X8}");
                    }
                }

                if (_cpu.State.A[6] != graphicsBase)
                {
                    differences.Add(
                        $"A6 expected {graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                }

                var capabilityAddress = CapabilityPhysicalAddress(graphicsBase);
                var cpuByteDataReadAddresses = _bus.BusAccesses
                    .Skip(busAccessStart)
                    .Where(access =>
                        access.Request.Requester == AmigaBusRequester.Cpu &&
                        access.Request.Kind == AmigaBusAccessKind.CpuDataRead &&
                        access.Request.Size == AmigaBusAccessSize.Byte)
                    .Select(access => access.Request.Address)
                    .ToArray();
                var capabilityReadCount = cpuByteDataReadAddresses.Count(address =>
                    (address & 0x00FF_FFFFu) == capabilityAddress);

                return new CallResult(
                    _cpu.State.D[0],
                    _cpu.State.A[6],
                    _cpu.State.ProgramCounter,
                    _cpu.State.A[7],
                    usedFallback,
                    nativeReturnCount,
                    capabilityReadCount,
                    cpuByteDataReadAddresses,
                    differences.ToArray());
            }

            throw new InvalidOperationException(
                $"{DescribeRoute(publicVector)} did not return: " +
                $"PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
