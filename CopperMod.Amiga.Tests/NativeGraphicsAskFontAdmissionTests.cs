using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsAskFontAdmissionTests
{
    // Keep pointer admission separate from AskFont's later public frame.  The
    // malformed rows prove that A0, A1, and the selected TextFont are rejected
    // before any callee-saved scratch or TextAttr byte escapes to the caller.
    private const int AskFontLvo = -474;
    private const int AskFontFunctionOrdinal = 78;
    private const uint CapturedD0 = 0xCAFE_BABEu;
    private const uint RastPortAddress = 0x00D1_0000;
    private const uint FontAddress = 0x00D2_0000;
    private const uint TextAttrAddress = 0x00D3_0000;
    private const uint HighPhysicalAddress = 0x00FF_FF00;
    private const uint FontName = 0x1234_5678;
    private const ushort FontYSize = 0x3456;
    private const byte FontStyle = 0x5A;
    private const byte FontFlags = 0xA5;
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
            "textattr-ordinary",
            "textattr-before-last-prefix",
            "textattr-last-prefix",
            "textattr-first-even",
            "textattr-null",
            "textattr-odd",
            "textattr-prefix-wrap",
            "textattr-last-even",
            "textattr-last-odd",
            "rastport-ordinary",
            "rastport-before-last-prefix",
            "rastport-last-prefix",
            "rastport-first-even",
            "rastport-null",
            "rastport-odd",
            "rastport-prefix-wrap",
            "rastport-last-even",
            "rastport-last-odd",
            "font-ordinary",
            "font-before-last-prefix",
            "font-last-prefix",
            "font-first-even",
            "font-null",
            "font-odd",
            "font-prefix-wrap",
            "font-last-even",
            "font-last-odd"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(AdmissionCases))]
    public void AskFontPublicEntriesGuardCompletePointerEnvelopesBeforeCalleeSavedScratch(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        var textAttr = TextAttrAddress;
        var rastPort = RastPortAddress;
        var font = FontAddress;
        var success = true;
        switch (scenario)
        {
            case "textattr-ordinary":
            case "rastport-ordinary":
            case "font-ordinary":
                break;
            case "textattr-before-last-prefix":
                textAttr = 0xFFFF_FFF6u;
                break;
            case "textattr-last-prefix":
                textAttr = 0xFFFF_FFF8u;
                break;
            case "textattr-first-even":
                textAttr = 0x0000_0002u;
                break;
            case "textattr-null":
                textAttr = 0;
                success = false;
                break;
            case "textattr-odd":
                textAttr = TextAttrAddress + 1u;
                success = false;
                break;
            case "textattr-prefix-wrap":
                textAttr = 0xFFFF_FFFAu;
                success = false;
                break;
            case "textattr-last-even":
                textAttr = 0xFFFF_FFFEu;
                success = false;
                break;
            case "textattr-last-odd":
                textAttr = 0xFFFF_FFFFu;
                success = false;
                break;
            case "rastport-before-last-prefix":
                rastPort = 0xFFFF_FFC6u;
                break;
            case "rastport-last-prefix":
                rastPort = 0xFFFF_FFC8u;
                break;
            case "rastport-first-even":
                rastPort = 0x0000_0002u;
                break;
            case "rastport-null":
                rastPort = 0;
                success = false;
                break;
            case "rastport-odd":
                rastPort = RastPortAddress + 1u;
                success = false;
                break;
            case "rastport-prefix-wrap":
                rastPort = 0xFFFF_FFCAu;
                success = false;
                break;
            case "rastport-last-even":
                rastPort = 0xFFFF_FFFEu;
                success = false;
                break;
            case "rastport-last-odd":
                rastPort = 0xFFFF_FFFFu;
                success = false;
                break;
            case "font-before-last-prefix":
                font = 0xFFFF_FFE6u;
                break;
            case "font-last-prefix":
                font = 0xFFFF_FFE8u;
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
                font = 0xFFFF_FFEAu;
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
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        if (scenario == "textattr-last-prefix")
            Assert.Equal((ulong)uint.MaxValue, (ulong)textAttr + 0x07u);
        if (scenario == "textattr-prefix-wrap")
            Assert.True((ulong)textAttr + 0x07u > uint.MaxValue);
        if (scenario == "rastport-last-prefix")
            Assert.Equal((ulong)uint.MaxValue, (ulong)rastPort + 0x37u);
        if (scenario == "rastport-prefix-wrap")
            Assert.True((ulong)rastPort + 0x37u > uint.MaxValue);
        if (scenario == "font-last-prefix")
            Assert.Equal((ulong)uint.MaxValue, (ulong)font + 0x17u);
        if (scenario == "font-prefix-wrap")
            Assert.True((ulong)font + 0x17u > uint.MaxValue);

        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedFont(font);
        fixture.SeedRastPort(rastPort, font);
        var before = fixture.CaptureMemory(textAttr);
        var expectedOutput = before.Output.ToArray();
        if (success)
            ApplyExpectedTextAttr(expectedOutput);

        var result = fixture.Invoke(textAttr, rastPort);
        var after = fixture.CaptureMemory(textAttr);
        var failures = new List<string>();
        Check(failures, "admission result and native/fallback provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "only an admitted call publishes all eight TextAttr bytes", () =>
            Assert.Equal(expectedOutput, after.Output));
        Check(failures, "source, non-output memory, image, caller and stack guards remain unchanged", () =>
        {
            Assert.Equal(before.RastPort, after.RastPort);
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.TextAttrRegion, after.TextAttrRegion);
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
                Assert.Equal(textAttr, result.Address0);
                Assert.Equal(rastPort, result.Address1);
            });
            Check(failures, "pre-admission decline retains D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }
        else
        {
            Check(failures, "admitted publication retains D2-D7/A2-A6", () =>
                Assert.True(result.RegisterDifferences.Length == 0,
                    string.Join("; ", result.RegisterDifferences)));
        }
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static void ApplyExpectedTextAttr(byte[] textAttr)
    {
        BinaryPrimitives.WriteUInt32BigEndian(textAttr.AsSpan(0x00), FontName);
        BinaryPrimitives.WriteUInt16BigEndian(textAttr.AsSpan(0x04), FontYSize);
        textAttr[0x06] = FontStyle;
        textAttr[0x07] = FontFlags;
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
    private sealed record MemorySnapshot(byte[] RastPort, byte[] Font, byte[] TextAttrRegion,
        byte[] High, byte[] Low, byte[] Output, byte[] GraphicsImage, byte[] Resident,
        byte[] Caller, byte[] StackGuards);
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
            Assert.Equal(AskFontLvo, (int)GraphicsLvo.AskFont);
            Assert.Equal(AskFontFunctionOrdinal, (-AskFontLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "AskFont/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.AskFont];
            _vectorSlot = checked((uint)((long)GraphicsBase + AskFontLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + AskFontFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(RastPortAddress, Enumerable.Repeat((byte)0xB9, 0x100).ToArray());
            _bus.MapWritableMemory(FontAddress, Enumerable.Repeat((byte)0x83, 0x100).ToArray());
            _bus.MapWritableMemory(TextAttrAddress, Enumerable.Repeat((byte)0xCC, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E94); // JSR(A4), preserving public A0/A1.
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)AskFontLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal void SeedRastPort(uint pointer, uint font)
            => SeedLong(pointer, GraphicsLayouts.RastPortFont, font);

        internal void SeedFont(uint pointer)
        {
            SeedLong(pointer, GraphicsLayouts.TextFontName, FontName);
            SeedWord(pointer, GraphicsLayouts.TextFontYSize, FontYSize);
            SeedByte(pointer, GraphicsLayouts.TextFontStyle, FontStyle);
            SeedByte(pointer, GraphicsLayouts.TextFontFlags, FontFlags);
        }

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
            for (var index = 0; index < GraphicsLayouts.TextAttrSize; index++)
            {
                var physical = ((outputAddress & 0x00FF_FFFFu) + (uint)index) & 0x00FF_FFFFu;
                if (physical >= physicalBase && physical < physicalEnd)
                    bytes[checked((int)(physical - physicalBase))] = 0;
            }
        }

        internal MemorySnapshot CaptureMemory(uint outputAddress)
        {
            var rastPort = ReadBytes(RastPortAddress, 0x100);
            var font = ReadBytes(FontAddress, 0x100);
            var textAttr = ReadBytes(TextAttrAddress, 0x100);
            var high = ReadBytes(HighPhysicalAddress, 0x100);
            var low = ReadBytes(0, 0x100);
            var output = ReadAliasedBytes(outputAddress, GraphicsLayouts.TextAttrSize);
            MaskOutput(rastPort, RastPortAddress, outputAddress);
            MaskOutput(font, FontAddress, outputAddress);
            MaskOutput(textAttr, TextAttrAddress, outputAddress);
            MaskOutput(high, HighPhysicalAddress, outputAddress);
            MaskOutput(low, 0, outputAddress);
            return new MemorySnapshot(
                rastPort,
                font,
                textAttr,
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

        internal CallResult Invoke(uint textAttr, uint rastPort)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[2] = _functionEntry;
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = textAttr;
            _cpu.State.A[1] = rastPort;
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
