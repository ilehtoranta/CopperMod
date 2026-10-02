using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRastPortPublicAbiTests
{
    // Activate only after the independently tested 100-byte extent unit.
    // This fixture changes neither InitRastPort's defaults nor font policy.
    private const int RastPortBytes = 100;
    private const uint CapturedD0 = 0x1234_5678;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint FontArena = 0x00D1_0000;
    private const uint Font = FontArena + 0x20;
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

    public static IEnumerable<object[]> PublicAbiCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[] { "no-font", "default-font", "null", "odd", "wrap", "exact-end" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void InitRastPortPublicEntriesPreserveCalleeSavedRegistersAcrossEveryExit(
        bool relocated, bool autoInitEntry, string scenario)
    {
        var withFont = scenario == "default-font";
        var success = scenario is "no-font" or "default-font" or "exact-end";
        var pointer = scenario switch
        {
            "no-font" or "default-font" => RastPort,
            "null" => 0u,
            "odd" => RastPort + 1u,
            "wrap" => 0xFFFF_FF9Eu,
            "exact-end" => 0xFFFF_FF9Cu,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        using var fixture = new Fixture(relocated, autoInitEntry, withFont);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(pointer);
        var after = fixture.CaptureMemory();
        var expected = before;
        if (success)
        {
            if (scenario == "exact-end")
            {
                var high = before.High.ToArray();
                ExpectedRastPort(withFont).CopyTo(high, 0x9C);
                expected = before with { High = high };
            }
            else
            {
                var arena = before.Arena.ToArray();
                ExpectedRastPort(withFont).CopyTo(arena, (int)(RastPort - ArenaAddress));
                expected = before with { Arena = arena };
            }
        }
        var failures = new List<string>();
        // Keep independent controls visible when the expected D6-only frame
        // regression is red. No saved-register failure can skip these checks.
        Check(failures, "existing D0 and native fallback contract", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
        });
        Check(failures, "actual public caller PC/SP and A1/A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "complete 100-byte output or unchanged declined memory", () =>
        {
            Assert.Equal(expected.Arena, after.Arena);
            Assert.Equal(expected.High, after.High);
            Assert.Equal(expected.Low, after.Low);
        });
        Check(failures, "font, library header, public vectors and caller guards unchanged", () =>
        {
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0, string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static byte[] ExpectedRastPort(bool withFont)
    {
        var result = new byte[RastPortBytes];
        result[GraphicsLayouts.RastPortMask] = 0xFF;
        result[GraphicsLayouts.RastPortFgPen] = 0xFF;
        result[GraphicsLayouts.RastPortOutlinePen] = 0xFF;
        result[GraphicsLayouts.RastPortDrawMode] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(GraphicsLayouts.RastPortLinePattern), 0xFFFF);
        result.AsSpan(GraphicsLayouts.RastPortMinterms, 8).Fill(0xCA);
        if (withFont)
        {
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(GraphicsLayouts.RastPortFont), Font);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(GraphicsLayouts.RastPortTextHeight), FontHeight);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(GraphicsLayouts.RastPortTextWidth), FontWidth);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(GraphicsLayouts.RastPortTextBaseline), FontBaseline);
        }
        // AlgoStyle, TextFlags and TxSpacing retain the current cleared
        // InitRastPort values; no SetFont/provider policy is inferred here.
        return result;
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
    private sealed record MemorySnapshot(byte[] Arena, byte[] High, byte[] Low, byte[] Font,
        byte[] GraphicsImage, byte[] Caller, byte[] StackGuards);
    private sealed record CallResult(uint Data0, uint Address1, uint Address6, uint ProgramCounter,
        uint StackPointer, bool UsedFallback, string[] RegisterDifferences);

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
        private const uint HighPhysicalAddress = 0x00FF_FF00;
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
        internal Fixture(bool relocated, bool autoInitEntry, bool withFont)
        {
            _autoInitEntry = autoInitEntry;
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/" +
                (autoInitEntry ? "AUTOINIT JSR(A2)" : "public JSR d16(A6)");
            uint residentAddress;
            if (relocated)
            {
                var addresses = new Queue<uint>(new[] { HunkSegmentAddress, HunkResidentAddress });
                var loader = new AmigaHunkProgramLoader(_bus, size =>
                {
                    var address = addresses.Dequeue();
                    var limit = address == HunkSegmentAddress ? HunkResidentAddress : CallerAddress;
                    Assert.True((ulong)address + (uint)size <= limit, "HUNK fixture segments overlap.");
                    _bus.MapWritableMemory(address, new byte[size]);
                    return address;
                });
                var program = loader.Load(Hunk.Value.Bytes);
                Assert.Empty(addresses);
                GraphicsBase = program.SegmentBases[0] + (uint)Hunk.Value.VectorOffset;
                _nativeCodeAddress = program.SegmentBases[0] + (uint)Hunk.Value.NativeCodeOffset;
                residentAddress = program.SegmentBases[1];
            }
            else
            {
                var entries = Image.Value.Entries.ToDictionary(item => item.Key, item => FixedCodeAddress + (uint)item.Value);
                var fallback = FixedCodeAddress + (uint)Image.Value.Fallback;
                var library = NativeGraphicsLibraryImageBuilder.Build(FixedGraphicsBase, FixedResidentAddress, fallback, entries, fallback);
                Assert.True((ulong)FixedCodeAddress + (uint)Image.Value.Code.Length <= library.VectorBase);
                _bus.MapWritableMemory(FixedCodeAddress, Image.Value.Code);
                _bus.MapWritableMemory(library.VectorBase, library.VectorBytes);
                _bus.MapWritableMemory(library.LibraryBase, library.PositiveBytes);
                _bus.MapWritableMemory(library.ResidentAddress, library.ResidentBytes);
                GraphicsBase = library.LibraryBase;
                _nativeCodeAddress = FixedCodeAddress;
                residentAddress = library.ResidentAddress;
            }
            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.InitRastPort];
            _vectorSlot = checked((uint)((long)GraphicsBase + (int)GraphicsLvo.InitRastPort));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(residentAddress + 0x16) + 4);
            var functionIndex = (-(int)GraphicsLvo.InitRastPort / NativeGraphicsLibraryImageBuilder.VectorStubSize) - 1;
            _functionEntry = _bus.ReadLong(functionArray + (uint)(functionIndex * 4));
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray());
            _bus.MapWritableMemory(FontArena, Enumerable.Repeat((byte)0xE9, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            SeedFont();
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultFont, withFont ? Font : 0);
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2); A1 remains the RastPort argument
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE);
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)(int)GraphicsLvo.InitRastPort));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        // A mapped standard TextFont with one character plus its undefined
        // glyph. InitRastPort consumes only the three existing metric fields;
        // this does not introduce drawing, malformed-font or provider tests.
        private void SeedFont()
        {
            _bus.ClearMemory(Font, GraphicsLayouts.TextFontMinimumSize);
            _bus.WriteByte(Font + (uint)GraphicsLayouts.TextFontNodeType, GraphicsLayouts.TextFontNodeTypeFont, 0);
            _bus.WriteWord(Font + (uint)GraphicsLayouts.ExecMessageLength, (ushort)GraphicsLayouts.TextFontMinimumSize);
            _bus.WriteLong(Font + (uint)GraphicsLayouts.TextFontName, Font + 0x40);
            _bus.WriteWord(Font + (uint)GraphicsLayouts.TextFontYSize, FontHeight);
            _bus.WriteWord(Font + (uint)GraphicsLayouts.TextFontXSize, FontWidth);
            _bus.WriteWord(Font + (uint)GraphicsLayouts.TextFontBaseline, FontBaseline);
            _bus.WriteByte(Font + (uint)GraphicsLayouts.TextFontLoChar, 63, 0);
            _bus.WriteByte(Font + (uint)GraphicsLayouts.TextFontHiChar, 63, 0);
            _bus.WriteLong(Font + (uint)GraphicsLayouts.TextFontCharData, Font + 0x70);
            _bus.WriteWord(Font + (uint)GraphicsLayouts.TextFontModulo, 2);
            _bus.WriteLong(Font + (uint)GraphicsLayouts.TextFontCharLoc, Font + 0x60);
            _bus.WriteLong(Font + 0x60, FontWidth);
            _bus.WriteLong(Font + 0x64, 0x0008_0000u | FontWidth);
            var name = new byte[] { (byte)'a', (byte)'b', (byte)'i', (byte)'.', (byte)'f', (byte)'o', (byte)'n', (byte)'t', 0 };
            for (var index = 0; index < name.Length; index++)
                _bus.WriteByte(Font + 0x40 + (uint)index, name[index], 0);
            for (var index = 0; index < FontHeight; index++)
                _bus.WriteWord(Font + 0x70 + (uint)(index * 2), 0x7C7C);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }
        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(FontArena, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x100).Concat(ReadBytes(StackAddress + 0x340, 0x2C0)).ToArray());

        internal CallResult Invoke(uint pointer)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = _functionEntry; // A2's entry value is also its preservation canary
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = 0xA0A0_A0A1;
            _cpu.State.A[1] = pointer; // full logical pointer, never a masked physical argument
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = GraphicsBase;
            _cpu.ExecuteInstruction();
            Assert.Equal(_autoInitEntry ? _functionEntry : _vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            var enteredBody = false;
            var usedFallback = false;
            for (var instruction = 0; instruction < 10_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc == _vectorSlot || (pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length),
                    $"Unexpected execution address 0x{pc:X8}; no host graphics gateway is installed.");
                enteredBody |= pc == _entry;
                // Recognize the exported fallback or an equivalent local
                // decline RTS; CapturedD0 can never be a successful result.
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback ||
                    (_bus.ReadWord(pc) == 0x4E75 && _cpu.State.D[0] == CapturedD0);
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;
                Assert.True(enteredBody, "The InitRastPort public entry was not reached.");
                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                {
                    var actual = _cpu.State.D[index + 2];
                    if (actual != DataCanaries[index])
                        differences.Add($"D{index + 2} expected {DataCanaries[index]:X8}, actual {actual:X8}");
                }
                for (var index = 0; index < expectedAddresses.Length; index++)
                {
                    var actual = _cpu.State.A[index + 2];
                    if (actual != expectedAddresses[index])
                        differences.Add($"A{index + 2} expected {expectedAddresses[index]:X8}, actual {actual:X8}");
                }
                if (_cpu.State.A[6] != GraphicsBase)
                    differences.Add($"A6 expected {GraphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                // D0/D1/A0/A1 are not callee-save registers. The separate
                // checks retain only this initializer's existing D0/A1 behavior.
                return new CallResult(_cpu.State.D[0], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }
        public void Dispose() => _cpu.Dispose();
    }
}
