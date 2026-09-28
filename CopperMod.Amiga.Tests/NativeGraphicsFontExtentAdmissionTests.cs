using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsFontExtentAdmissionTests
{
    // FontExtent consumes the complete 0x34-byte TextFont prefix from A0 and
    // publishes a packed 12-byte TextExtent through A1. Malformed rows prove
    // pre-admission state; admitted rows and the second matrix prove the
    // separately staged public frame.
    private const int FontExtentLvo = -762;
    private const int FontExtentFunctionOrdinal = 126;
    private const uint CapturedD0 = 0xCAFE_BABEu;
    private const uint FontAddress = 0x00D1_0000;
    private const uint ExtentAddress = 0x00D6_0000;
    private const uint CharLocAddress = 0x00D3_0000;
    private const uint CharSpaceAddress = 0x00D4_0000;
    private const uint CharKernAddress = 0x00D5_0000;
    private const uint HighPhysicalAddress = 0x00FF_FF00;
    private const ushort FontHeight = 9;
    private const ushort FontWidth = 7;
    private const ushort FontBaseline = 6;
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
            "font-ordinary",
            "font-before-last-prefix",
            "font-last-prefix",
            "font-first-even",
            "font-null",
            "font-odd",
            "font-prefix-wrap",
            "font-last-even",
            "font-last-odd",
            "extent-ordinary",
            "extent-before-last-prefix",
            "extent-last-prefix",
            "extent-first-even",
            "extent-null",
            "extent-odd",
            "extent-prefix-wrap",
            "extent-last-even",
            "extent-last-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(AdmissionCases))]
    public void FontExtentPublicEntriesGuardCompleteFontAndResultEnvelopesBeforeScratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        var font = FontAddress;
        var extent = ExtentAddress;
        var success = true;
        switch (scenario)
        {
            case "font-ordinary":
                break;
            case "font-before-last-prefix":
                font = 0xFFFF_FFCAu;
                break;
            case "font-last-prefix":
                font = 0xFFFF_FFCCu;
                break;
            case "font-first-even":
                font = 0x0000_0002u;
                break;
            case "font-null":
                font = 0;
                success = false;
                break;
            case "font-odd":
                font = FontAddress + 1u;
                success = false;
                break;
            case "font-prefix-wrap":
                font = 0xFFFF_FFCEu;
                success = false;
                break;
            case "font-last-even":
                font = 0xFFFF_FFFEu;
                success = false;
                break;
            case "font-last-odd":
                font = 0xFFFF_FFFFu;
                success = false;
                break;
            case "extent-ordinary":
                break;
            case "extent-before-last-prefix":
                extent = 0xFFFF_FFF2u;
                break;
            case "extent-last-prefix":
                extent = 0xFFFF_FFF4u;
                break;
            case "extent-first-even":
                extent = 0x0000_0002u;
                break;
            case "extent-null":
                extent = 0;
                success = false;
                break;
            case "extent-odd":
                extent = ExtentAddress + 1u;
                success = false;
                break;
            case "extent-prefix-wrap":
                extent = 0xFFFF_FFF6u;
                success = false;
                break;
            case "extent-last-even":
                extent = 0xFFFF_FFFEu;
                success = false;
                break;
            case "extent-last-odd":
                extent = 0xFFFF_FFFFu;
                success = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "font-last-prefix")
            Assert.Equal((ulong)uint.MaxValue, (ulong)font + 0x33u);
        if (scenario == "font-prefix-wrap")
            Assert.True((ulong)font + 0x33u > uint.MaxValue);
        if (scenario == "extent-last-prefix")
            Assert.Equal((ulong)uint.MaxValue, (ulong)extent + 0x0Bu);
        if (scenario == "extent-prefix-wrap")
            Assert.True((ulong)extent + 0x0Bu > uint.MaxValue);

        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedFont(font, FontExtentMode.FixedForward);
        var before = fixture.CaptureMemory(extent);
        var expectedOutput = before.Output.ToArray();
        if (success)
            ApplyExpectedExtent(expectedOutput);

        var result = fixture.Invoke(font, extent);
        var after = fixture.CaptureMemory(extent);
        var failures = new List<string>();
        Check(failures, "admission result and native/fallback provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted call publishes the complete 12-byte result", () =>
            Assert.Equal(expectedOutput, after.Output));
        Check(failures, "font, non-result memory, image, caller and stack guards remain unchanged", () =>
        {
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.CharLoc, after.CharLoc);
            Assert.Equal(before.CharSpace, after.CharSpace);
            Assert.Equal(before.CharKern, after.CharKern);
            Assert.Equal(before.ExtentRegion, after.ExtentRegion);
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
        if (!success)
        {
            Check(failures, "pre-admission decline retains A0/A1", () =>
            {
                Assert.Equal(font, result.Address0);
                Assert.Equal(extent, result.Address1);
            });
            Check(failures, "pre-admission decline retains D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }
        else
        {
            Check(failures, "admitted call retains D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> PublicFrameCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "fixed-forward-success",
            "fixed-reverse-success",
            "proportional-forward-success",
            "proportional-reverse-success",
            "fixed-decline",
            "proportional-forward-early-decline",
            "proportional-forward-late-decline",
            "proportional-reverse-early-decline",
            "proportional-reverse-late-decline"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicFrameCases))]
    public void FontExtentPublicEntriesPreserveTheCompleteFrameAcrossNativeExits(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        var mode = scenario switch
        {
            "fixed-forward-success" or "fixed-decline" => FontExtentMode.FixedForward,
            "fixed-reverse-success" => FontExtentMode.FixedReverse,
            "proportional-forward-success" or
                "proportional-forward-early-decline" or
                "proportional-forward-late-decline" => FontExtentMode.ProportionalForward,
            "proportional-reverse-success" or
                "proportional-reverse-early-decline" or
                "proportional-reverse-late-decline" => FontExtentMode.ProportionalReverse,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var success = scenario.EndsWith("-success", StringComparison.Ordinal);
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedFont(FontAddress, mode);
        switch (scenario)
        {
            case "fixed-forward-success":
            case "fixed-reverse-success":
            case "proportional-forward-success":
            case "proportional-reverse-success":
                break;
            case "fixed-decline":
                fixture.SetFontCharSpacePointer(CharSpaceAddress);
                break;
            case "proportional-forward-early-decline":
                fixture.SetFontStyle(1);
                break;
            case "proportional-forward-late-decline":
                fixture.SetFontCharLocPointer(0xFFFF_FFFCu);
                break;
            case "proportional-reverse-early-decline":
                fixture.SetFontRange((byte)'A', (byte)'B');
                break;
            case "proportional-reverse-late-decline":
                fixture.SetCharSpace(6);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var before = fixture.CaptureMemory(ExtentAddress);
        var expectedOutput = before.Output.ToArray();
        if (success)
        {
            switch (mode)
            {
                case FontExtentMode.FixedForward:
                    ApplyExpectedExtent(expectedOutput);
                    break;
                case FontExtentMode.ProportionalForward:
                    ApplyExpectedForwardExtent(expectedOutput, 9);
                    break;
                case FontExtentMode.FixedReverse:
                case FontExtentMode.ProportionalReverse:
                    ApplyExpectedReverseExtent(expectedOutput, -7);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        var result = fixture.Invoke(FontAddress, ExtentAddress);
        var after = fixture.CaptureMemory(ExtentAddress);
        var failures = new List<string>();
        Check(failures, "success/decline result and provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "all native exits retain D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Check(failures, "provider decline retains A0/A1", () =>
        {
            if (!success)
            {
                Assert.Equal(FontAddress, result.Address0);
                Assert.Equal(ExtentAddress, result.Address1);
            }
        });
        Check(failures, "only success publishes the complete result", () =>
            Assert.Equal(expectedOutput, after.Output));
        Check(failures, "font/tables, non-result memory, image, caller and stack stay intact", () =>
        {
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.CharLoc, after.CharLoc);
            Assert.Equal(before.CharSpace, after.CharSpace);
            Assert.Equal(before.CharKern, after.CharKern);
            Assert.Equal(before.ExtentRegion, after.ExtentRegion);
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
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static void ApplyExpectedExtent(byte[] extent)
    {
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x00), FontWidth);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x02), FontHeight);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x04), 0);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x06), -FontBaseline);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x08), checked((ushort)(FontWidth - 1)));
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x0A),
            checked((ushort)(FontHeight - FontBaseline - 1)));
    }

    private static void ApplyExpectedForwardExtent(byte[] extent, ushort width)
    {
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x00), width);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x02), FontHeight);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x04), 0);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x06), -FontBaseline);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x08), checked((ushort)(width - 1)));
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x0A),
            checked((ushort)(FontHeight - FontBaseline - 1)));
    }

    private static void ApplyExpectedReverseExtent(byte[] extent, short width)
    {
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x00), width);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x02), FontHeight);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x04), width);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x06), -FontBaseline);
        BinaryPrimitives.WriteInt16BigEndian(extent.AsSpan(0x08), -1);
        BinaryPrimitives.WriteUInt16BigEndian(extent.AsSpan(0x0A),
            checked((ushort)(FontHeight - FontBaseline - 1)));
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
    private enum FontExtentMode { FixedForward, FixedReverse, ProportionalForward, ProportionalReverse }
    private sealed record MemorySnapshot(byte[] Font, byte[] CharLoc, byte[] CharSpace, byte[] CharKern,
        byte[] ExtentRegion, byte[] High, byte[] Low,
        byte[] Output, byte[] GraphicsImage, byte[] Resident, byte[] Caller, byte[] StackGuards);
    private sealed record CallResult(uint Data0, uint Address0, uint Address1, uint Address6,
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
            Assert.Equal(FontExtentLvo, (int)GraphicsLvo.FontExtent);
            Assert.Equal(FontExtentFunctionOrdinal, (-FontExtentLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "FontExtent/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.FontExtent];
            _vectorSlot = checked((uint)((long)GraphicsBase + FontExtentLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + FontExtentFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(FontAddress, Enumerable.Repeat((byte)0xB9, 0x100).ToArray());
            _bus.MapWritableMemory(CharLocAddress, Enumerable.Repeat((byte)0x83, 0x100).ToArray());
            _bus.MapWritableMemory(CharSpaceAddress, Enumerable.Repeat((byte)0x95, 0x100).ToArray());
            _bus.MapWritableMemory(CharKernAddress, Enumerable.Repeat((byte)0xA7, 0x100).ToArray());
            _bus.MapWritableMemory(ExtentAddress, Enumerable.Repeat((byte)0xCC, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E94); // JSR(A4), leaving public A0/A1 intact.
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)FontExtentLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal void SeedFont(uint pointer, FontExtentMode mode)
        {
            SeedWord(pointer, 0x14, FontHeight);
            SeedByte(pointer, 0x16, 0);
            SeedByte(pointer, 0x17, mode switch
            {
                FontExtentMode.FixedForward => (byte)0,
                FontExtentMode.FixedReverse => (byte)0x04,
                FontExtentMode.ProportionalForward => (byte)0x20,
                FontExtentMode.ProportionalReverse => (byte)0x24,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            });
            SeedWord(pointer, 0x18, FontWidth);
            SeedWord(pointer, 0x1A, FontBaseline);
            SeedByte(pointer, 0x20, (byte)'A');
            SeedByte(pointer, 0x21, (byte)'A');
            var proportional = mode is FontExtentMode.ProportionalForward or FontExtentMode.ProportionalReverse;
            SeedLong(pointer, 0x28, proportional ? CharLocAddress : 0);
            SeedLong(pointer, 0x2C, proportional ? CharSpaceAddress : 0);
            SeedLong(pointer, 0x30, proportional ? CharKernAddress : 0);
            _bus.WriteLong(CharLocAddress, 0);
            _bus.WriteLong(CharLocAddress + 4u, 0);
            _bus.WriteWord(CharSpaceAddress,
                mode == FontExtentMode.ProportionalReverse ? unchecked((ushort)-8) : (ushort)8);
            _bus.WriteWord(CharSpaceAddress + 2u, 8);
            _bus.WriteWord(CharKernAddress, 1);
            _bus.WriteWord(CharKernAddress + 2u, 1);
        }

        internal void SetFontStyle(byte style)
            => _bus.WriteByte(FontAddress + 0x16u, style, 0);

        internal void SetFontRange(byte low, byte high)
        {
            _bus.WriteByte(FontAddress + 0x20u, low, 0);
            _bus.WriteByte(FontAddress + 0x21u, high, 0);
        }

        internal void SetFontCharLocPointer(uint pointer)
            => _bus.WriteLong(FontAddress + 0x28u, pointer);

        internal void SetFontCharSpacePointer(uint pointer)
            => _bus.WriteLong(FontAddress + 0x2Cu, pointer);

        internal void SetCharSpace(short value)
            => _bus.WriteWord(CharSpaceAddress, unchecked((ushort)value));

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
                .Select(index => _bus.ReadByte(((address & 0x00FF_FFFFu) + (uint)index) & 0x00FF_FFFFu))
                .ToArray();

        private static void MaskOutput(byte[] bytes, uint physicalBase, uint outputAddress)
        {
            var physicalEnd = physicalBase + (uint)bytes.Length;
            for (var index = 0; index < 12; index++)
            {
                var physical = ((outputAddress & 0x00FF_FFFFu) + (uint)index) & 0x00FF_FFFFu;
                if (physical >= physicalBase && physical < physicalEnd)
                    bytes[checked((int)(physical - physicalBase))] = 0;
            }
        }

        internal MemorySnapshot CaptureMemory(uint outputAddress)
        {
            var font = ReadBytes(FontAddress, 0x100);
            var charLoc = ReadBytes(CharLocAddress, 0x100);
            var charSpace = ReadBytes(CharSpaceAddress, 0x100);
            var charKern = ReadBytes(CharKernAddress, 0x100);
            var extent = ReadBytes(ExtentAddress, 0x100);
            var high = ReadBytes(HighPhysicalAddress, 0x100);
            var low = ReadBytes(0, 0x100);
            var output = ReadAliasedBytes(outputAddress, 12);
            MaskOutput(font, FontAddress, outputAddress);
            MaskOutput(charLoc, CharLocAddress, outputAddress);
            MaskOutput(charSpace, CharSpaceAddress, outputAddress);
            MaskOutput(charKern, CharKernAddress, outputAddress);
            MaskOutput(extent, ExtentAddress, outputAddress);
            MaskOutput(high, HighPhysicalAddress, outputAddress);
            MaskOutput(low, 0, outputAddress);
            return new MemorySnapshot(
                font,
                charLoc,
                charSpace,
                charKern,
                extent,
                high,
                low,
                output,
                ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                    NativeGraphicsLibraryImageBuilder.VectorTableSize +
                    NativeGraphicsLibraryImageBuilder.PositiveImageSize),
                ReadBytes(_residentAddress, _residentByteCount),
                ReadBytes(CallerAddress, 8),
                ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());
        }

        internal CallResult Invoke(uint font, uint extent)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[2] = _functionEntry;
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = font;
            _cpu.State.A[1] = extent;
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
                    _cpu.State.D[0], _cpu.State.A[0], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback,
                    nativeReturnCount, differences.ToArray());
            }

            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, " +
                $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
