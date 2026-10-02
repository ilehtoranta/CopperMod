using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsCloseFontAdmissionTests
{
    private const int CloseFontLvo = -78;
    private const int CloseFontFunctionOrdinal = 12;
    private const uint CapturedD0 = 0xCAFE_BABEu;
    private const uint FontAddress = 0x00D2_0000;
    private const uint ForeignFontAddress = 0x00D3_0000;
    private const uint HighPhysicalAddress = 0x00FF_FF00;
    private const ushort InitialAccessors = 3;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> GraphicsBaseAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary",
            "before-last-prefix",
            "last-prefix",
            "first-even",
            "null",
            "odd",
            "prefix-wrap",
            "last-even",
            "last-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(GraphicsBaseAdmissionCases))]
    public void CloseFontBodyGuardsTheCompleteDefaultFontFieldBeforeCalleeSavedScratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var graphicsBase = fixture.GraphicsBase;
        var success = true;
        var lastPrefix = uint.MaxValue - (uint)(GraphicsLayouts.GfxBaseDefaultFont + 3);
        switch (scenario)
        {
            case "ordinary":
                break;
            case "before-last-prefix":
                graphicsBase = lastPrefix - 2u;
                break;
            case "last-prefix":
                graphicsBase = lastPrefix;
                break;
            case "first-even":
                graphicsBase = 0x0000_0002u;
                break;
            case "null":
                graphicsBase = 0;
                success = false;
                break;
            case "odd":
                graphicsBase = 3;
                success = false;
                break;
            case "prefix-wrap":
                graphicsBase = lastPrefix + 2u;
                success = false;
                break;
            case "last-even":
                graphicsBase = 0xFFFF_FFFEu;
                success = false;
                break;
            case "last-odd":
                graphicsBase = 0xFFFF_FFFFu;
                success = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "last-prefix")
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultFont + 3u);
        if (scenario == "prefix-wrap")
            Assert.True(
                (ulong)graphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultFont + 3u > uint.MaxValue);

        fixture.SeedFontAccessors(FontAddress, InitialAccessors);
        fixture.SeedDefaultFont(graphicsBase, FontAddress);
        var before = fixture.CaptureMemory();
        var expectedFont = before.Font.ToArray();
        if (success)
            BinaryPrimitives.WriteUInt16BigEndian(
                expectedFont.AsSpan(GraphicsLayouts.TextFontAccessors),
                InitialAccessors - 1);

        var result = fixture.InvokeWithGraphicsBase(FontAddress, graphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "GfxBase result and native/fallback provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted resident decrements its accessor word", () =>
            Assert.Equal(expectedFont, after.Font));
        Check(failures, "non-font memory, image, caller, and stack guards remain unchanged", () =>
        {
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "caller PC/SP, A1, and supplied A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(FontAddress, result.Address1);
            Assert.Equal(graphicsBase, result.Address6);
        });
        if (success)
            Check(failures, "admitted close preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        if (!success)
            Check(failures, "pre-admission decline retains D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0,
            $"{fixture.GraphicsBaseBoundaryRoute}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> SelectedFontAdmissionCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary",
            "before-last-prefix",
            "last-prefix",
            "first-even",
            "null",
            "odd",
            "prefix-wrap",
            "last-even",
            "last-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(SelectedFontAdmissionCases))]
    public void CloseFontBodyGuardsTheCompleteSelectedDefaultBeforeD7Scratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        Assert.Equal(0x34, GraphicsLayouts.TextFontMinimumSize);
        using var fixture = new Fixture(relocated, autoInitEntry);
        var font = FontAddress;
        var success = true;
        var lastPrefix = uint.MaxValue - (uint)(GraphicsLayouts.TextFontMinimumSize - 1);
        switch (scenario)
        {
            case "ordinary":
                break;
            case "before-last-prefix":
                font = lastPrefix - 2u;
                break;
            case "last-prefix":
                font = lastPrefix;
                break;
            case "first-even":
                font = 0x0000_0002u;
                break;
            case "null":
                font = 0;
                success = false;
                break;
            case "odd":
                font = FontAddress + 1u;
                success = false;
                break;
            case "prefix-wrap":
                font = lastPrefix + 2u;
                success = false;
                break;
            case "last-even":
                font = 0xFFFF_FFFEu;
                success = false;
                break;
            case "last-odd":
                font = 0xFFFF_FFFFu;
                success = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "last-prefix")
            Assert.Equal(
                (ulong)uint.MaxValue,
                (ulong)font + (uint)GraphicsLayouts.TextFontMinimumSize - 1u);
        if (scenario == "prefix-wrap")
            Assert.True(
                (ulong)font + (uint)GraphicsLayouts.TextFontMinimumSize - 1u > uint.MaxValue);

        fixture.SeedFontAccessors(font, InitialAccessors);
        fixture.SeedDefaultFont(fixture.GraphicsBase, font);
        var inputFont = font == 0 ? FontAddress : font;
        var beforeSelectedFont = fixture.CaptureSelectedFont(font);
        var beforeOtherMemory = fixture.CaptureMemoryExcludingFont(font);
        var expectedSelectedFont = beforeSelectedFont.ToArray();
        if (success)
            BinaryPrimitives.WriteUInt16BigEndian(
                expectedSelectedFont.AsSpan(GraphicsLayouts.TextFontAccessors),
                InitialAccessors - 1);

        var result = fixture.InvokeWithGraphicsBase(inputFont, fixture.GraphicsBase);
        var afterSelectedFont = fixture.CaptureSelectedFont(font);
        var afterOtherMemory = fixture.CaptureMemoryExcludingFont(font);
        var failures = new List<string>();
        Check(failures, "selected-font result and native/fallback provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted resident decrements its accessor word", () =>
            Assert.Equal(expectedSelectedFont, afterSelectedFont));
        Check(failures, "all memory outside the selected font remains unchanged", () =>
        {
            Assert.Equal(beforeOtherMemory.Font, afterOtherMemory.Font);
            Assert.Equal(beforeOtherMemory.High, afterOtherMemory.High);
            Assert.Equal(beforeOtherMemory.Low, afterOtherMemory.Low);
            Assert.Equal(beforeOtherMemory.GraphicsImage, afterOtherMemory.GraphicsImage);
            Assert.Equal(beforeOtherMemory.Resident, afterOtherMemory.Resident);
            Assert.Equal(beforeOtherMemory.Caller, afterOtherMemory.Caller);
            Assert.Equal(beforeOtherMemory.StackGuards, afterOtherMemory.StackGuards);
        });
        Check(failures, "caller PC/SP, A1, and A6 return intact", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(inputFont, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        if (success)
            Check(failures, "admitted close preserves D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        if (!success)
            Check(failures, "pre-admission decline retains D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0,
            $"{fixture.GraphicsBaseBoundaryRoute}, selected font {scenario}:\n" +
            string.Join("\n", failures));
    }

    public static IEnumerable<object[]> LateFrameCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[] { "foreign-pointer", "zero-accessors" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(LateFrameCases))]
    public void CloseFontPublicFramePreservesCalleeSavedRegistersAfterAdmission(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedDefaultFont(fixture.GraphicsBase, FontAddress);
        var inputFont = FontAddress;
        var success = true;
        switch (scenario)
        {
            case "foreign-pointer":
                fixture.SeedFontAccessors(FontAddress, InitialAccessors);
                inputFont = ForeignFontAddress;
                success = false;
                break;
            case "zero-accessors":
                fixture.SeedFontAccessors(FontAddress, 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var before = fixture.CaptureMemory();
        var result = fixture.InvokeWithGraphicsBase(inputFont, fixture.GraphicsBase);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "late result and native/fallback provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "late zero/identity result publishes no memory", () =>
        {
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "late result preserves A1, caller PC/SP, and A6", () =>
        {
            Assert.Equal(inputFont, result.Address1);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "late result preserves D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0,
            $"{fixture.GraphicsBaseBoundaryRoute}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> PublicNullCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
            yield return new object[] { relocated, autoInitEntry };
    }

    [Theory]
    [MemberData(nameof(PublicNullCases))]
    public void CloseFontNullIsAReadFreePublicNoOpWithACompleteCalleeSaveContract(
        bool relocated,
        bool autoInitEntry)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var before = fixture.CaptureMemory();
        var result = fixture.InvokePublic(0);
        var after = fixture.CaptureMemory();
        AssertNullNoOp(fixture, fixture.Route, fixture.GraphicsBase, before, result, after);
    }

    public static IEnumerable<object[]> UnusableBaseNullCases()
    {
        var firstWrappingBase = uint.MaxValue -
                                (uint)(GraphicsLayouts.GfxBaseDefaultFont + 3) + 2u;
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var graphicsBase in new[] { 0u, 3u, firstWrappingBase })
            yield return new object[] { relocated, autoInitEntry, graphicsBase };
    }

    [Theory]
    [MemberData(nameof(UnusableBaseNullCases))]
    public void CloseFontNullDoesNotReadAnUnusableGraphicsBase(
        bool relocated,
        bool autoInitEntry,
        uint graphicsBase)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var before = fixture.CaptureMemory();
        var result = fixture.InvokeWithGraphicsBase(0, graphicsBase);
        var after = fixture.CaptureMemory();
        AssertNullNoOp(fixture, fixture.GraphicsBaseBoundaryRoute, graphicsBase, before, result, after);
    }

    private static void AssertNullNoOp(
        Fixture fixture,
        string route,
        uint graphicsBase,
        MemorySnapshot before,
        CallResult result,
        MemorySnapshot after)
    {
        var failures = new List<string>();
        Check(failures, "NULL returns native success", () =>
        {
            Assert.Equal(0u, result.Data0);
            Assert.False(result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "NULL publishes no guest, image, caller, or stack bytes", () =>
        {
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "NULL preserves A1, caller PC/SP, and supplied A6", () =>
        {
            Assert.Equal(0u, result.Address1);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(graphicsBase, result.Address6);
        });
        Check(failures, "NULL preserves D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0, route + ", NULL:\n" + string.Join("\n", failures));
    }

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Font, byte[] High, byte[] Low,
        byte[] GraphicsImage, byte[] Resident, byte[] Caller, byte[] StackGuards);
    private sealed record CallResult(uint Data0, uint Address1, uint Address6,
        uint ProgramCounter, uint StackPointer, bool UsedFallback, int NativeReturnCount,
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
        private const uint Address0Canary = 0xA0A0_0101;
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
            Assert.Equal(CloseFontLvo, (int)GraphicsLvo.CloseFont);
            Assert.Equal(CloseFontFunctionOrdinal, (-CloseFontLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "CloseFont/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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
                var entries = Image.Value.Entries.ToDictionary(item => item.Key,
                    item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(
                    FixedGraphicsBase, FixedResidentAddress, fallback, entries, fallback);
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.CloseFont];
            _vectorSlot = checked((uint)((long)GraphicsBase + CloseFontLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + CloseFontFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(FontAddress, Enumerable.Repeat((byte)0x83, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E94); // JSR(A4), preserving public A1/A6.
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)CloseFontLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);

            SeedFontAccessors(FontAddress, InitialAccessors);
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultFont, FontAddress);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal string GraphicsBaseBoundaryRoute => "CloseFont/" +
            (GraphicsBase == FixedGraphicsBase ? "fixed image" : "relocated HUNK") + "/" +
            (_autoInitEntry ? "AUTOINIT JSR(A4)" : "direct native body");
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal void SeedDefaultFont(uint graphicsBase, uint font)
            => SeedLong(graphicsBase, GraphicsLayouts.GfxBaseDefaultFont, font);

        internal void SeedFontAccessors(uint font, ushort value)
            => SeedWord(font, GraphicsLayouts.TextFontAccessors, value);

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

        private byte[] ReadAliasedBytes(uint address, int count)
            => Enumerable.Range(0, count)
                .Select(index => _bus.ReadByte(unchecked(address + (uint)index) & 0x00FF_FFFFu))
                .ToArray();

        internal byte[] CaptureSelectedFont(uint font)
            => ReadAliasedBytes(font, GraphicsLayouts.TextFontMinimumSize);

        internal MemorySnapshot CaptureMemory()
            => new(
                ReadBytes(FontAddress, 0x100),
                ReadBytes(HighPhysicalAddress, 0x100),
                ReadBytes(0, 0x100),
                ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize +
                    NativeGraphicsLibraryImageBuilder.PositiveImageSize),
                ReadBytes(_residentAddress, _residentByteCount),
                ReadBytes(CallerAddress, 8),
                ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal MemorySnapshot CaptureMemoryExcludingFont(uint font)
        {
            var snapshot = CaptureMemory();
            MaskSpan(snapshot.Font, FontAddress, font, GraphicsLayouts.TextFontMinimumSize);
            MaskSpan(snapshot.High, HighPhysicalAddress, font, GraphicsLayouts.TextFontMinimumSize);
            MaskSpan(snapshot.Low, 0, font, GraphicsLayouts.TextFontMinimumSize);
            return snapshot;
        }

        private static void MaskSpan(byte[] bytes, uint physicalBase, uint pointer, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var physical = unchecked(pointer + (uint)index) & 0x00FF_FFFFu;
                if (physical < physicalBase) continue;
                var offset = physical - physicalBase;
                if (offset < (uint)bytes.Length) bytes[offset] = 0;
            }
        }

        internal CallResult InvokePublic(uint font)
        {
            var expectedAddresses = PrepareRegisters(font, GraphicsBase);
            _cpu.Reset(CallerAddress, StackPointer);
            PrepareCpu(font, GraphicsBase, expectedAddresses);
            _cpu.ExecuteInstruction();
            Assert.Equal(_autoInitEntry ? _functionEntry : _vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            if (!_autoInitEntry) _cpu.ExecuteInstruction();
            Assert.Equal(_entry, _cpu.State.ProgramCounter);
            return ExecuteBody(expectedAddresses, GraphicsBase, Route);
        }

        internal CallResult InvokeWithGraphicsBase(uint font, uint graphicsBase)
        {
            var expectedAddresses = PrepareRegisters(font, graphicsBase);
            if (!_autoInitEntry)
                _bus.WriteLong(StackPointer - 4u, ReturnAddress);
            _cpu.Reset(_autoInitEntry ? CallerAddress : _entry,
                _autoInitEntry ? StackPointer : StackPointer - 4u);
            PrepareCpu(font, graphicsBase, expectedAddresses);
            if (_autoInitEntry)
            {
                _cpu.ExecuteInstruction();
                Assert.Equal(_functionEntry, _cpu.State.ProgramCounter);
                Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
                Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            }
            Assert.Equal(_entry, _cpu.State.ProgramCounter);
            return ExecuteBody(expectedAddresses, graphicsBase, GraphicsBaseBoundaryRoute);
        }

        private uint[] PrepareRegisters(uint font, uint graphicsBase)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[2] = _functionEntry;
            return expectedAddresses;
        }

        private void PrepareCpu(uint font, uint graphicsBase, uint[] expectedAddresses)
        {
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = Address0Canary;
            _cpu.State.A[1] = font;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = graphicsBase;
        }

        private CallResult ExecuteBody(uint[] expectedAddresses, uint graphicsBase, string route)
        {
            var usedFallback = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 1_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc >= _nativeCodeAddress &&
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
                if (_cpu.State.A[6] != graphicsBase)
                    differences.Add($"A6 expected {graphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                return new CallResult(
                    _cpu.State.D[0], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback,
                    nativeReturnCount, differences.ToArray());
            }

            throw new InvalidOperationException(
                $"{route} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
