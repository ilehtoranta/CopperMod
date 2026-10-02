using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsTextFitRastPortBoundaryTests
{
    // TextFit's operation-wide A1 reads end at the signed RP.TxSpacing WORD
    // at +0x40/+0x41. D1-D3 are live public inputs and the native body reuses
    // A6 immediately, so this unit proves only exact pre-read admission; the
    // wider successful/post-scratch public frame remains a separate change.
    private const int TextFitLvo = -696;
    private const int TextFitFunctionOrdinal = 115;
    private const uint CapturedD0Prefix = 0xCAFE_0000;
    private const uint CapturedD1 = 0xD1D1_0001;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint FontAddress = 0x00D1_0000;
    private const uint TextAddress = 0x00D2_0000;
    private const uint CharLocAddress = 0x00D3_0000;
    private const uint CharSpaceAddress = 0x00D4_0000;
    private const uint CharKernAddress = 0x00D5_0000;
    private const uint ExtentAddress = 0x00D6_0000;
    private const ushort FontHeight = 9;
    private const ushort FontBaseline = 6;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> RastPortBoundaryCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var proportional in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary",
            "last-spacing-slot",
            "before-last-spacing-slot",
            "first-even-port",
            "null-port",
            "odd-port",
            "spacing-slot-wrap",
            "font-slot-wrap",
            "last-even-port",
            "last-odd-port"
        })
            yield return new object[] { relocated, autoInitEntry, proportional, scenario };
    }

    [Theory]
    [MemberData(nameof(RastPortBoundaryCases))]
    public void TextFitPublicEntriesGuardTheCompleteConsumedRastPortEnvelope(
        bool relocated,
        bool autoInitEntry,
        bool proportional,
        string scenario)
    {
        var (pointer, success) = scenario switch
        {
            "ordinary" => (RastPort, true),
            "last-spacing-slot" => (0xFFFF_FFBEu, true),
            "before-last-spacing-slot" => (0xFFFF_FFBCu, true),
            "first-even-port" => (0x0000_0002u, true),
            "null-port" => (0x0000_0000u, false),
            "odd-port" => (0x00D0_0101u, false),
            "spacing-slot-wrap" => (0xFFFF_FFC0u, false),
            "font-slot-wrap" => (0xFFFF_FFCAu, false),
            "last-even-port" => (0xFFFF_FFFEu, false),
            "last-odd-port" => (0xFFFF_FFFFu, false),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        if (scenario == "last-spacing-slot")
        {
            Assert.Equal((ulong)uint.MaxValue, (ulong)pointer + 0x41u);
            Assert.True((ulong)pointer + 99u > uint.MaxValue);
        }
        if (scenario == "spacing-slot-wrap")
        {
            Assert.True((ulong)pointer + 0x38u <= uint.MaxValue);
            Assert.True((ulong)pointer + 0x40u > uint.MaxValue);
        }
        if (scenario == "font-slot-wrap")
            Assert.True((ulong)pointer + 0x37u > uint.MaxValue);

        var mode = proportional ? TextFitMode.ForwardProportional : TextFitMode.Fixed;
        using var fixture = new Fixture(relocated, autoInitEntry, mode);
        fixture.SeedRastPort(pointer);
        var before = fixture.CaptureMemory();
        var expectedExtent = before.Extent.ToArray();
        if (success)
            ApplyExpectedExtent(expectedExtent, proportional ? (ushort)18 : (ushort)8);

        var result = fixture.Invoke(pointer, mode);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "complete A1 admission, D0 and terminal provenance", () =>
        {
            Assert.Equal(success ? 1u : fixture.InputD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "caller PC/SP and RastPort argument", () =>
        {
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
        });
        Check(failures, "only an admitted call publishes the complete 12-byte extent", () =>
            Assert.Equal(expectedExtent, after.Extent));
        Check(failures, "query preserves RastPort aliases, font, text, and metric tables", () =>
        {
            Assert.Equal(before.Arena, after.Arena);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.Text, after.Text);
            Assert.Equal(before.CharLoc, after.CharLoc);
            Assert.Equal(before.CharSpace, after.CharSpace);
            Assert.Equal(before.CharKern, after.CharKern);
        });
        Check(failures, "library/vector/resident, caller and stack guards unchanged", () =>
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "all admitted paths preserve D2-D7/A2-A6", () =>
            Assert.True(result.CalleeSaveDifferences.Length == 0,
                string.Join("; ", result.CalleeSaveDifferences)));
        if (!success)
            Check(failures, "pre-admission decline retains every public input", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0,
            $"{fixture.Route}/{(proportional ? "proportional" : "fixed")}, {scenario}:\n"
            + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> PublicFrameCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "fixed-success",
            "fixed-empty-success",
            "forward-one-success",
            "forward-one-no-fit",
            "forward-multi-success",
            "forward-multi-no-fit",
            "reverse-success",
            "reverse-no-fit",
            "fixed-decline",
            "forward-one-decline",
            "forward-multi-decline",
            "reverse-early-decline",
            "reverse-late-decline"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicFrameCases))]
    public void TextFitPublicEntriesPreserveTheCompleteFrameAcrossNativeExits(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        var mode = scenario.StartsWith("reverse", StringComparison.Ordinal)
            ? TextFitMode.ReverseProportional
            : scenario.StartsWith("forward", StringComparison.Ordinal)
                ? TextFitMode.ForwardProportional
                : TextFitMode.Fixed;
        var (count, width, expectedD0, expectedWidth, zeroResult, decline) = scenario switch
        {
            "fixed-success" => (3u, 15u, 1u, 8, false, false),
            "fixed-empty-success" => (0u, 15u, 0u, 0, true, false),
            "forward-one-success" => (1u, 10u, 1u, 8, false, false),
            "forward-one-no-fit" => (1u, 7u, 0u, 0, true, false),
            "forward-multi-success" => (2u, 20u, 1u, 18, false, false),
            "forward-multi-no-fit" => (2u, 17u, 0u, 0, true, false),
            "reverse-success" => (1u, 8u, 1u, -7, false, false),
            "reverse-no-fit" => (1u, 6u, 0u, 0, true, false),
            "fixed-decline" => (3u, 15u, 0u, 0, false, true),
            "forward-one-decline" => (1u, 10u, 0u, 0, false, true),
            "forward-multi-decline" => (2u, 20u, 0u, 0, false, true),
            "reverse-early-decline" => (2u, 8u, 0u, 0, false, true),
            "reverse-late-decline" => (1u, 8u, 0u, 0, false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var inputD0 = CapturedD0Prefix | count;
        var inputD1 = mode == TextFitMode.ReverseProportional ? 0xD1D1_FFFFu : CapturedD1;
        using var fixture = new Fixture(relocated, autoInitEntry, mode);
        fixture.SeedRastPort(RastPort);
        switch (scenario)
        {
            case "fixed-decline":
                fixture.SetRastPortAlgoStyle(1);
                break;
            case "forward-one-decline":
            case "forward-multi-decline":
                fixture.SetFontCharSpace(0);
                break;
            case "reverse-late-decline":
                fixture.SetFontCharLoc(0);
                break;
        }

        var before = fixture.CaptureMemory();
        var expectedExtent = before.Extent.ToArray();
        if (!decline)
        {
            if (zeroResult)
                Array.Clear(expectedExtent, 0, GraphicsLayouts.TextExtentSize);
            else if (mode == TextFitMode.ReverseProportional)
                ApplyExpectedReverseExtent(expectedExtent);
            else
                ApplyExpectedExtent(expectedExtent, checked((ushort)expectedWidth));
        }

        var result = fixture.Invoke(RastPort, mode, inputD0, inputD1, width, FontHeight);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "fitted result and native/fallback provenance", () =>
        {
            Assert.Equal(decline ? inputD0 : expectedD0, result.Data0);
            Assert.Equal(decline, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "caller PC/SP and RastPort argument", () =>
        {
            Assert.Equal(RastPort, result.Address1);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
        });
        Check(failures, "all public paths preserve D2-D7/A2-A6", () =>
            Assert.True(result.CalleeSaveDifferences.Length == 0,
                string.Join("; ", result.CalleeSaveDifferences)));
        if (decline)
            Check(failures, "post-admission decline retains complete call provenance", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        Check(failures, "only an accepted path publishes its complete extent", () =>
            Assert.Equal(expectedExtent, after.Extent));
        Check(failures, "RastPort, font, text, tables, aliases and guards remain read-only", () =>
        {
            Assert.Equal(before.Arena, after.Arena);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.Text, after.Text);
            Assert.Equal(before.CharLoc, after.CharLoc);
            Assert.Equal(before.CharSpace, after.CharSpace);
            Assert.Equal(before.CharKern, after.CharKern);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static void ApplyExpectedExtent(byte[] extent, ushort width)
    {
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x00), width);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x02), FontHeight);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x04), 0);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x06), -FontBaseline);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x08), (ushort)(width - 1));
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x0A),
            (ushort)(FontHeight - FontBaseline - 1));
    }

    private static void ApplyExpectedReverseExtent(byte[] extent)
    {
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x00), -7);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x02), FontHeight);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x04), -7);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x06), -FontBaseline);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x08), -1);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x0A),
            (ushort)(FontHeight - FontBaseline - 1));
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
    private enum TextFitMode { Fixed, ForwardProportional, ReverseProportional }
    private sealed record MemorySnapshot(byte[] Arena, byte[] High, byte[] Low, byte[] Font,
        byte[] Text, byte[] CharLoc, byte[] CharSpace, byte[] CharKern, byte[] Extent,
        byte[] GraphicsImage, byte[] Resident, byte[] Caller, byte[] StackGuards);
    private sealed record CallResult(uint Data0, uint Address1, uint ProgramCounter,
        uint StackPointer, bool UsedFallback, int NativeReturnCount,
        string[] RegisterDifferences, string[] CalleeSaveDifferences);

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
            0xD4D4_0404, 0xD5D5_0505, 0xD6D6_0606, 0xD7D7_0707
        };
        private const uint Address4Canary = 0xA4A4_0404;
        private const uint Address5Canary = 0xA5A5_0505;
        private readonly AmigaBus _bus = new();
        private readonly IM68kCore _cpu;
        private readonly bool _autoInitEntry;
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _vectorSlot;
        private readonly uint _functionEntry;
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;
        private readonly uint _inputD2;
        private readonly uint _inputD3 = FontHeight;

        internal Fixture(bool relocated, bool autoInitEntry, TextFitMode mode)
        {
            Assert.Equal(TextFitLvo, (int)GraphicsLvo.TextFit);
            Assert.Equal(TextFitFunctionOrdinal, (-TextFitLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            _inputD2 = mode switch
            {
                TextFitMode.Fixed => 15u,
                TextFitMode.ForwardProportional => 20u,
                TextFitMode.ReverseProportional => 8u,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            InputD0 = CapturedD0Prefix | (mode switch
            {
                TextFitMode.Fixed => 3u,
                TextFitMode.ForwardProportional => 2u,
                TextFitMode.ReverseProportional => 1u,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            });
            Route = "TextFit/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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
                var entries = Image.Value.Entries.ToDictionary(item => item.Key, item => FixedCodeAddress + (uint)item.Value);
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.TextFit];
            _vectorSlot = checked((uint)((long)GraphicsBase + TextFitLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + TextFitFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray());
            _bus.MapWritableMemory(FontAddress, Enumerable.Repeat((byte)0xB9, 0x100).ToArray());
            _bus.MapWritableMemory(TextAddress, Enumerable.Repeat((byte)0x71, 0x100).ToArray());
            _bus.MapWritableMemory(CharLocAddress, Enumerable.Repeat((byte)0x83, 0x100).ToArray());
            _bus.MapWritableMemory(CharSpaceAddress, Enumerable.Repeat((byte)0x95, 0x100).ToArray());
            _bus.MapWritableMemory(CharKernAddress, Enumerable.Repeat((byte)0xA7, 0x100).ToArray());
            _bus.MapWritableMemory(ExtentAddress, Enumerable.Repeat((byte)0xCC, 0x40).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            SeedFont(mode);

            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E94); // JSR(A4), leaving public A2/A3 intact.
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)TextFitLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }
        internal uint InputD0 { get; }

        private void SeedFont(TextFitMode mode)
        {
            _bus.WriteWord(FontAddress + 0x14u, FontHeight);
            _bus.WriteByte(FontAddress + 0x16u, 0, 0);
            _bus.WriteByte(FontAddress + 0x17u,
                mode == TextFitMode.Fixed ? (byte)0 : (byte)0x20, 0);
            _bus.WriteWord(FontAddress + 0x18u, 7);
            _bus.WriteWord(FontAddress + 0x1Au, FontBaseline);
            _bus.WriteByte(FontAddress + 0x20u, (byte)'A', 0);
            _bus.WriteByte(FontAddress + 0x21u, (byte)'A', 0);
            _bus.WriteLong(FontAddress + 0x28u, CharLocAddress);
            _bus.WriteLong(FontAddress + 0x2Cu, CharSpaceAddress);
            _bus.WriteLong(FontAddress + 0x30u, CharKernAddress);
            _bus.WriteByte(TextAddress, (byte)'A', 0);
            _bus.WriteByte(TextAddress + 1u, (byte)'A', 0);
            _bus.WriteLong(CharLocAddress, 0);
            _bus.WriteLong(CharLocAddress + 4u, 0);
            _bus.WriteWord(CharSpaceAddress,
                mode == TextFitMode.ReverseProportional ? unchecked((ushort)-7) : (ushort)7);
            _bus.WriteWord(CharSpaceAddress + 2u,
                mode == TextFitMode.ReverseProportional ? (ushort)4 : (ushort)7);
            _bus.WriteWord(CharKernAddress,
                mode == TextFitMode.ReverseProportional ? (ushort)0 : (ushort)1);
            _bus.WriteWord(CharKernAddress + 2u,
                mode == TextFitMode.ReverseProportional ? (ushort)0 : (ushort)1);
        }

        internal void SeedRastPort(uint pointer)
        {
            SeedLong(pointer, 0x34, FontAddress);
            SeedByte(pointer, 0x38, 0);
            SeedWord(pointer, 0x40, 1);
        }

        internal void SetRastPortAlgoStyle(byte style)
            => _bus.WriteByte(RastPort + 0x38u, style, 0);

        internal void SetFontCharLoc(uint charLoc)
            => _bus.WriteLong(FontAddress + 0x28u, charLoc);

        internal void SetFontCharSpace(uint charSpace)
            => _bus.WriteLong(FontAddress + 0x2Cu, charSpace);

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

        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize),
            ReadBytes(HighPhysicalAddress, 0x100),
            ReadBytes(0, 0x100),
            ReadBytes(FontAddress, 0x100),
            ReadBytes(TextAddress, 0x100),
            ReadBytes(CharLocAddress, 0x100),
            ReadBytes(CharSpaceAddress, 0x100),
            ReadBytes(CharKernAddress, 0x100),
            ReadBytes(ExtentAddress, 0x40),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount),
            ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(
            uint pointer,
            TextFitMode mode,
            uint? inputD0 = null,
            uint? inputD1 = null,
            uint? inputD2 = null,
            uint? inputD3 = null)
        {
            var expectedD0 = inputD0 ?? InputD0;
            var expectedD1 = inputD1 ??
                (mode == TextFitMode.ReverseProportional ? 0xD1D1_FFFFu : CapturedD1);
            var expectedD2 = inputD2 ?? _inputD2;
            var expectedD3 = inputD3 ?? _inputD3;
            var expectedA4 = _autoInitEntry ? _functionEntry : Address4Canary;
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = expectedD0;
            _cpu.State.D[1] = expectedD1;
            _cpu.State.D[2] = expectedD2;
            _cpu.State.D[3] = expectedD3;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 4] = DataCanaries[index];
            _cpu.State.A[0] = TextAddress;
            _cpu.State.A[1] = pointer;
            _cpu.State.A[2] = ExtentAddress;
            _cpu.State.A[3] = 0;
            _cpu.State.A[4] = expectedA4;
            _cpu.State.A[5] = Address5Canary;
            _cpu.State.A[6] = GraphicsBase;

            _cpu.ExecuteInstruction();
            Assert.Equal(_autoInitEntry ? _functionEntry : _vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            if (!_autoInitEntry) _cpu.ExecuteInstruction();
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 10_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}; no host graphics/Exec gateway is installed.");
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                if (_bus.ReadWord(pc) == 0x4E75)
                {
                    nativeReturnCount++;
                    usedFallback |= _cpu.State.D[0] == expectedD0;
                }
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress) continue;

                var differences = new List<string>();
                var calleeSaveDifferences = new List<string>();
                var expectedData = new[]
                {
                    expectedD0, expectedD1, expectedD2, expectedD3,
                    DataCanaries[0], DataCanaries[1], DataCanaries[2], DataCanaries[3]
                };
                for (var index = 1; index < expectedData.Length; index++)
                {
                    if (_cpu.State.D[index] != expectedData[index])
                    {
                        var difference = $"D{index} expected {expectedData[index]:X8}, " +
                            $"actual {_cpu.State.D[index]:X8}";
                        differences.Add(difference);
                        if (index >= 2) calleeSaveDifferences.Add(difference);
                    }
                }
                var expectedAddresses = new[]
                {
                    TextAddress, pointer, ExtentAddress, 0u, expectedA4, Address5Canary, GraphicsBase
                };
                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    if (_cpu.State.A[index] != expectedAddresses[index])
                    {
                        var difference = $"A{index} expected {expectedAddresses[index]:X8}, " +
                            $"actual {_cpu.State.A[index]:X8}";
                        differences.Add(difference);
                        if (index >= 2) calleeSaveDifferences.Add(difference);
                    }
                }
                return new CallResult(
                    _cpu.State.D[0], _cpu.State.A[1], _cpu.State.ProgramCounter,
                    _cpu.State.A[7], usedFallback, nativeReturnCount,
                    differences.ToArray(), calleeSaveDifferences.ToArray());
            }
            throw new InvalidOperationException($"{Route}/{mode} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
