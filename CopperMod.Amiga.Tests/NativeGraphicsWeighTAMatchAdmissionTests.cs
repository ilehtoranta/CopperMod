using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsWeighTAMatchAdmissionTests
{
    // Keep A2/A0/A1 admission independent from the later ranking frame.
    private const int WeighTAMatchLvo = -804;
    private const int WeighTAMatchFunctionOrdinal = 133;
    private const uint CapturedD0 = 0xCAFE_BABEu;
    private const uint RequestedAddress = 0x00D1_0000;
    private const uint TargetAddress = 0x00D2_0000;
    private const uint TargetTagsAddress = 0x00D3_0000;
    private const uint HighPhysicalAddress = 0x00FF_FF00;
    private const ushort YSize = 8;
    private const byte Style = 1;
    private const byte Flags = 2;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> AdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "requested-ordinary",
            "requested-before-last-prefix",
            "requested-last-prefix",
            "requested-first-even",
            "requested-null",
            "requested-odd",
            "requested-prefix-wrap",
            "requested-last-even",
            "requested-last-odd",
            "target-ordinary",
            "target-before-last-prefix",
            "target-last-prefix",
            "target-first-even",
            "target-null",
            "target-odd",
            "target-prefix-wrap",
            "target-last-even",
            "target-last-odd",
            "target-tags-nonnull"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(AdmissionCases))]
    public void WeighTAMatchGuardsExactNativeInputsBeforeCalleeSavedScratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        var requested = RequestedAddress;
        var target = TargetAddress;
        var targetTags = 0u;
        var success = true;

        if (scenario == "target-tags-nonnull")
        {
            targetTags = TargetTagsAddress;
            success = false;
        }
        else
        {
            var requestedBoundary = scenario.StartsWith("requested-", StringComparison.Ordinal);
            var boundary = scenario[(requestedBoundary ? "requested-" : "target-").Length..];
            var pointer = ResolveBoundary(
                boundary,
                requestedBoundary ? RequestedAddress : TargetAddress,
                out success);
            if (requestedBoundary) requested = pointer;
            else target = pointer;
            AssertBoundaryArithmetic(boundary, pointer);
        }

        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedTextAttr(requested);
        fixture.SeedTextAttr(target);
        var before = fixture.CaptureMemory();

        var result = fixture.Invoke(requested, target, targetTags);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "result and native/fallback provenance", () =>
        {
            Assert.Equal(success ? 0x7FFFu : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "metric admission is read-only", () =>
        {
            Assert.Equal(before.Requested, after.Requested);
            Assert.Equal(before.Target, after.Target);
            Assert.Equal(before.TargetTags, after.TargetTags);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "caller PC/SP and library base return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "A0/A1/A2 inputs retain their complete logical values", () =>
        {
            Assert.Equal(requested, result.Address0);
            Assert.Equal(target, result.Address1);
            Assert.Equal(targetTags, result.Address2);
        });
        if (success)
            Check(failures, "admitted exact match preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        if (!success)
            Check(failures, "pre-admission decline preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> LateFrameCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ysize-mismatch",
            "requested-tagged",
            "target-tagged",
            "style-mismatch",
            "flags-mismatch"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(LateFrameCases))]
    public void WeighTAMatchPublicFramePreservesCalleeSavedRegistersOnLateDecline(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedTextAttr(RequestedAddress);
        fixture.SeedTextAttr(TargetAddress);
        switch (scenario)
        {
            case "ysize-mismatch":
                fixture.SeedYSize(TargetAddress, checked((ushort)(YSize + 1)));
                break;
            case "requested-tagged":
                fixture.SeedStyle(RequestedAddress, (byte)(Style | 0x80));
                break;
            case "target-tagged":
                fixture.SeedStyle(TargetAddress, (byte)(Style | 0x80));
                break;
            case "style-mismatch":
                fixture.SeedStyle(TargetAddress, checked((byte)(Style + 1)));
                break;
            case "flags-mismatch":
                fixture.SeedFlags(TargetAddress, checked((byte)(Flags + 1)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(RequestedAddress, TargetAddress, 0);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "late decline result and fallback provenance", () =>
        {
            Assert.Equal(CapturedD0, result.Data0);
            Assert.True(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "late ranking decline is read-only", () =>
        {
            Assert.Equal(before.Requested, after.Requested);
            Assert.Equal(before.Target, after.Target);
            Assert.Equal(before.TargetTags, after.TargetTags);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "caller PC/SP, library base, and inputs return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(RequestedAddress, result.Address0);
            Assert.Equal(TargetAddress, result.Address1);
            Assert.Equal(0u, result.Address2);
        });
        Check(failures, "late decline preserves D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));

        Assert.True(failures.Count == 0,
            $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static uint ResolveBoundary(string boundary, uint ordinaryPointer, out bool success)
    {
        success = true;
        switch (boundary)
        {
            case "ordinary":
                return ordinaryPointer;
            case "before-last-prefix":
                return 0xFFFF_FFF6u;
            case "last-prefix":
                return 0xFFFF_FFF8u;
            case "first-even":
                return 0x0000_0002u;
            case "null":
                success = false;
                return 0;
            case "odd":
                success = false;
                return ordinaryPointer + 1u;
            case "prefix-wrap":
                success = false;
                return 0xFFFF_FFFAu;
            case "last-even":
                success = false;
                return 0xFFFF_FFFEu;
            case "last-odd":
                success = false;
                return 0xFFFF_FFFFu;
            default:
                throw new ArgumentOutOfRangeException(nameof(boundary));
        }
    }

    private static void AssertBoundaryArithmetic(string boundary, uint pointer)
    {
        if (boundary == "last-prefix")
            Assert.Equal((ulong)uint.MaxValue, (ulong)pointer + (uint)GraphicsLayouts.TextAttrSize - 1u);
        if (boundary == "prefix-wrap")
            Assert.True((ulong)pointer + (uint)GraphicsLayouts.TextAttrSize - 1u > uint.MaxValue);
    }

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
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
        byte[] Requested,
        byte[] Target,
        byte[] TargetTags,
        byte[] High,
        byte[] Low,
        byte[] GraphicsImage,
        byte[] Resident,
        byte[] Caller,
        byte[] StackGuards);

    private sealed record CallResult(
        uint Data0,
        uint Address0,
        uint Address1,
        uint Address2,
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
            Assert.Equal(WeighTAMatchLvo, (int)GraphicsLvo.WeighTAMatch);
            Assert.Equal(WeighTAMatchFunctionOrdinal, (-WeighTAMatchLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "WeighTAMatch/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
                (autoInitEntry ? "AUTOINIT JSR(A4)" : "public JSR d16(A6)");

            if (relocated)
            {
                var residentByteCount = 0;
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(_bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
                    _bus.MapWritableMemory(address, new byte[size]);
                    if (address == HunkResidentAddress) residentByteCount = size;
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
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                _bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                _bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                _bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                _bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                GraphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
                _residentAddress = library.ResidentAddress;
                _residentByteCount = library.ResidentBytes.Length;
            }

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.WeighTAMatch];
            _vectorSlot = checked((uint)((long)GraphicsBase + WeighTAMatchLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + WeighTAMatchFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(RequestedAddress, Enumerable.Repeat((byte)0xCC, 0x100).ToArray());
            _bus.MapWritableMemory(TargetAddress, Enumerable.Repeat((byte)0x83, 0x100).ToArray());
            _bus.MapWritableMemory(TargetTagsAddress, Enumerable.Repeat((byte)0x6D, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E94); // JSR(A4), preserving public A0/A1/A2/A6.
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)WeighTAMatchLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal void SeedTextAttr(uint pointer)
        {
            SeedLong(pointer, GraphicsLayouts.TextAttrName, 0x1234_5678);
            SeedWord(pointer, GraphicsLayouts.TextAttrYSize, YSize);
            SeedByte(pointer, GraphicsLayouts.TextAttrStyle, Style);
            SeedByte(pointer, GraphicsLayouts.TextAttrFlags, Flags);
        }

        internal void SeedYSize(uint pointer, ushort value)
            => SeedWord(pointer, GraphicsLayouts.TextAttrYSize, value);

        internal void SeedStyle(uint pointer, byte value)
            => SeedByte(pointer, GraphicsLayouts.TextAttrStyle, value);

        internal void SeedFlags(uint pointer, byte value)
            => SeedByte(pointer, GraphicsLayouts.TextAttrFlags, value);

        private void SeedLong(uint pointer, int fieldOffset, uint value)
        {
            for (var index = 0; index < sizeof(uint); index++)
                SeedByte(pointer, fieldOffset + index,
                    unchecked((byte)(value >> (24 - index * 8))));
        }

        private void SeedWord(uint pointer, int fieldOffset, ushort value)
        {
            SeedByte(pointer, fieldOffset, unchecked((byte)(value >> 8)));
            SeedByte(pointer, fieldOffset + 1, unchecked((byte)value));
        }

        private void SeedByte(uint pointer, int fieldOffset, byte value)
        {
            var physical = ((pointer & 0x00FF_FFFFu) + (uint)fieldOffset) & 0x00FF_FFFFu;
            _bus.WriteByte(physical, value, 0);
            Assert.Equal(value, _bus.ReadByte(physical));
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory()
            => new(
                ReadBytes(RequestedAddress, 0x100),
                ReadBytes(TargetAddress, 0x100),
                ReadBytes(TargetTagsAddress, 0x100),
                ReadBytes(HighPhysicalAddress, 0x100),
                ReadBytes(0, 0x100),
                ReadBytes(
                    GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize +
                    NativeGraphicsLibraryImageBuilder.PositiveImageSize),
                ReadBytes(_residentAddress, _residentByteCount),
                ReadBytes(CallerAddress, 8),
                ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint requested, uint target, uint targetTags)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            expectedAddresses[0] = targetTags;
            if (_autoInitEntry) expectedAddresses[2] = _functionEntry;

            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = requested;
            _cpu.State.A[1] = target;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = GraphicsBase;

            _cpu.ExecuteInstruction();
            Assert.Equal(_autoInitEntry ? _functionEntry : _vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            if (!_autoInitEntry) _cpu.ExecuteInstruction();
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 1_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(
                    pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}; no host graphics/Exec gateway is installed.");
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                if (_bus.ReadWord(pc) == 0x4E75)
                {
                    nativeReturnCount++;
                    usedFallback |= _cpu.State.D[0] == CapturedD0;
                }
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress) continue;

                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    if (_cpu.State.D[index + 2] != DataCanaries[index])
                        differences.Add($"D{index + 2} expected {DataCanaries[index]:X8}, " +
                            $"actual {_cpu.State.D[index + 2]:X8}");
                }
                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    if (_cpu.State.A[index + 2] != expectedAddresses[index])
                        differences.Add($"A{index + 2} expected {expectedAddresses[index]:X8}, " +
                            $"actual {_cpu.State.A[index + 2]:X8}");
                }
                if (_cpu.State.A[6] != GraphicsBase)
                    differences.Add($"A6 expected {GraphicsBase:X8}, actual {_cpu.State.A[6]:X8}");

                return new CallResult(
                    _cpu.State.D[0],
                    _cpu.State.A[0],
                    _cpu.State.A[1],
                    _cpu.State.A[2],
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
