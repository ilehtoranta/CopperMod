using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsNextDisplayInfoPublicAbiTests
{
    private const int NextDisplayInfoLvo = -732;
    private const int NextDisplayInfoFunctionOrdinal = 121;
    private const uint ForeignCursor = GraphicsModeIds.VgaMonitor;
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
        yield return Case("invalid-start", GraphicsModeIds.Invalid, GraphicsModeIds.DefaultMonitor, false);
        yield return Case(
            "private-default-handle",
            GraphicsDisplayDatabase.DefaultModeHandle,
            GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HiresKey,
            false);
        yield return Case(
            "default-zero",
            GraphicsModeIds.DefaultMonitor,
            GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HiresKey,
            false);
        yield return Case(
            "middle",
            GraphicsModeIds.DefaultMonitor | GraphicsModeIds.HiresDualPlayfieldTwoLaceKey,
            GraphicsModeIds.DefaultMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey,
            false);
        yield return Case(
            "last",
            GraphicsModeIds.NtscMonitor | GraphicsModeIds.SuperHiresDualPlayfieldTwoLaceKey,
            GraphicsModeIds.Invalid,
            false);
        yield return Case("foreign", ForeignCursor, ForeignCursor, true);
    }

    public static IEnumerable<object[]> PublicAbiCases()
    {
        foreach (var canonical in CanonicalCases())
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
            yield return canonical.Concat(new object[] { relocated, autoInitEntry }).ToArray();
    }

    [Theory]
    [MemberData(nameof(CanonicalCases))]
    public void NextDisplayInfoExistingCanonicalResultsRemainStable(
        string scenario,
        uint cursor,
        uint expected,
        bool expectedFallback)
    {
        using var fixture = new Fixture(relocated: false, autoInitEntry: false);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(cursor);
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
    public void NextDisplayInfoPublicEntriesPreserveD2D7AndA2A6(
        string scenario,
        uint cursor,
        uint expected,
        bool expectedFallback,
        bool relocated,
        bool autoInitEntry)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(cursor);
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

    private static object[] Case(string name, uint cursor, uint expected, bool fallback)
        => new object[] { name, cursor, expected, fallback };

    private static void AssertMemoryEqual(MemorySnapshot expected, MemorySnapshot actual)
    {
        Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
        Assert.Equal(expected.Resident, actual.Resident);
        Assert.Equal(expected.Caller, actual.Caller);
        Assert.Equal(expected.StackGuards, actual.StackGuards);
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
        byte[] StackGuards);

    private sealed record CallResult(
        uint Data0,
        uint Address6,
        uint ProgramCounter,
        uint StackPointer,
        bool UsedFallback,
        int NativeReturnCount,
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
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _vectorSlot;
        private readonly uint _functionEntry;
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;

        internal Fixture(bool relocated, bool autoInitEntry)
        {
            Assert.Equal(NextDisplayInfoLvo, (int)GraphicsLvo.NextDisplayInfo);
            Assert.Equal(NextDisplayInfoFunctionOrdinal, (-NextDisplayInfoLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "NextDisplayInfo/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.NextDisplayInfo];
            _vectorSlot = checked((uint)((long)GraphicsBase + NextDisplayInfoLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(
                functionArray + NextDisplayInfoFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

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
                _bus.WriteWord(
                    CallerAddress + 2,
                    unchecked((ushort)NextDisplayInfoLvo));
            }

            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(
                M68kBackendKind.AccurateM68000,
                _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

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
                    .ToArray());

        internal CallResult Invoke(uint cursor)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = _functionEntry;

            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = cursor;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = 0xA0A0_0000;
            _cpu.State.A[1] = 0xA1A1_0101;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = GraphicsBase;

            _cpu.ExecuteInstruction();
            Assert.Equal(
                _autoInitEntry ? _functionEntry : _vectorSlot,
                _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            if (!_autoInitEntry)
                _cpu.ExecuteInstruction();
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
                    // The shared fallback may be linked through a branch
                    // island rather than exposing its nominal fragment entry.
                    // This fixture's sole foreign cursor is deliberately not
                    // a canonical self-result, so unchanged D0 at the final
                    // RTS is an independent owner-handoff provenance signal.
                    usedFallback |= cursor == ForeignCursor &&
                                    _cpu.State.D[0] == cursor;
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

                if (_cpu.State.A[6] != GraphicsBase)
                {
                    differences.Add(
                        $"A6 expected {GraphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                }

                return new CallResult(
                    _cpu.State.D[0],
                    _cpu.State.A[6],
                    _cpu.State.ProgramCounter,
                    _cpu.State.A[7],
                    usedFallback,
                    nativeReturnCount,
                    differences.ToArray());
            }

            throw new InvalidOperationException(
                $"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
