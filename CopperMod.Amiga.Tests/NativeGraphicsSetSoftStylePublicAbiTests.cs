using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsSetSoftStylePublicAbiTests
{
    // SetSoftStyle takes A1/D0/D1 and uses D2-D6 internally. Preserve the
    // current enable-mask merge, 52-byte font admission and NULL decline.
    // Public contract: d0.se/autodocs/graphics.library/SetSoftStyle.
    private const int RastPortBytes = 100;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint NonArgumentRastPort = ArenaAddress + 0x200;
    private const uint FontAddress = 0x00D1_0000;
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
        foreach (var scenario in new[]
        {
            "plain",
            "intrinsic",
            "partial-enable",
            "disabled",
            "all-intrinsic",
            "high-intrinsic",
            "all-algorithmic",
            "clear-algorithmic",
            "upper-enable-only",
            "upper-request-only",
            "enable-intrinsic-only",
            "last-font",
            "null-font",
            "odd-font",
            "font-wrap",
            "last-odd-font"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void SetStylePublicEntriesPreserveCalleeSavedRegistersAndExistingStyleMerge(
        bool relocated, bool autoInitEntry, string scenario)
    {
        // Literal results are independently checked bit-by-bit against the
        // documented enable mask; do not derive an oracle from the emitter.
        var (font, intrinsic, algorithmic, argument, enable, expectedAlgorithmic, expectedResult, success) = scenario switch
        {
            "plain" => (FontAddress, (byte)0x00, (byte)0x00, 0xCAFE0003u, 0xFFFFFFFFu, (byte)0x03, 0x00000003u, true),
            "intrinsic" => (FontAddress, (byte)0x05, (byte)0x00, 0xDEAD0000u, 0xFFFFFFFFu, (byte)0x00, 0x00000005u, true),
            "partial-enable" => (FontAddress, (byte)0x02, (byte)0xA5, 0xCAFE0058u, 0xD00D003Cu, (byte)0x99, 0x0000009Bu, true),
            "disabled" => (FontAddress, (byte)0x05, (byte)0xA2, 0xCAFE00FFu, 0x00000000u, (byte)0xA2, 0x000000A7u, true),
            "all-intrinsic" => (FontAddress, (byte)0xFF, (byte)0xA5, 0xCAFE0000u, 0xFFFFFFFFu, (byte)0xA5, 0x000000FFu, true),
            "high-intrinsic" => (FontAddress, (byte)0xE7, (byte)0x81, 0xCAFE0018u, 0xFFFFFFFFu, (byte)0x99, 0x000000FFu, true),
            "all-algorithmic" => (FontAddress, (byte)0x00, (byte)0x00, 0xCAFE00FFu, 0xFFFFFFFFu, (byte)0xFF, 0x000000FFu, true),
            "clear-algorithmic" => (FontAddress, (byte)0x00, (byte)0xFF, 0xCAFE0000u, 0xFFFFFFFFu, (byte)0x00, 0x00000000u, true),
            "upper-enable-only" => (FontAddress, (byte)0x00, (byte)0x5A, 0xCAFE00A5u, 0xFFFF0000u, (byte)0x5A, 0x0000005Au, true),
            "upper-request-only" => (FontAddress, (byte)0x00, (byte)0x5A, 0xCAFE0000u, 0x000000FFu, (byte)0x00, 0x00000000u, true),
            "enable-intrinsic-only" => (FontAddress, (byte)0x05, (byte)0xA2, 0xCAFE00FFu, 0x00000005u, (byte)0xA2, 0x000000A7u, true),
            "last-font" => (0xFFFFFFCCu, (byte)0x05, (byte)0x22, 0xCAFE000Bu, 0xD00D000Fu, (byte)0x2A, 0x0000002Fu, true),
            "null-font" => (0x00000000u, (byte)0x05, (byte)0xA5, 0xCAFEBA11u, 0x1234003Cu, (byte)0xA5, 0xCAFEBA11u, false),
            "odd-font" => (0x00D10001u, (byte)0x05, (byte)0xA5, 0xCAFEBA11u, 0x1234003Cu, (byte)0xA5, 0xCAFEBA11u, false),
            "font-wrap" => (0xFFFFFFCEu, (byte)0x05, (byte)0xA5, 0xCAFEBA11u, 0x1234003Cu, (byte)0xA5, 0xCAFEBA11u, false),
            "last-odd-font" => (0xFFFFFFFFu, (byte)0x05, (byte)0xA5, 0xCAFEBA11u, 0x1234003Cu, (byte)0xA5, 0xCAFEBA11u, false),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        if (scenario == "last-font")
            Assert.Equal((ulong)uint.MaxValue, (ulong)font + 0x33u);
        if (scenario == "font-wrap")
            Assert.True((ulong)font + 0x33u > uint.MaxValue);
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedFontPointer(RastPort, font);
        fixture.SeedField(font, 0x16, intrinsic);
        fixture.SeedField(RastPort, 0x38, algorithmic);
        var before = fixture.CaptureMemory();

        var result = fixture.Invoke(RastPort, argument, enable);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "existing style result, null/malformed declines and full argument provenance", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(expectedResult, result.Data0);
            // D1 is volatile; this frame-only unit retains its current value.
            Assert.Equal(enable, result.Data1);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "public caller PC/SP and A1/A6; unchanged existing volatile A0 behavior", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(RastPort, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            // A0 and D1 are volatile scratch, not part of the callee-save set.
            // Retain this body's existing A0 result while framing D2-D6.
            Assert.Equal(success && font != 0 ? font : NonArgumentRastPort, result.Address0);
        });
        Check(failures, "only the admitted AlgoStyle byte may change; decoy/font/low/high guards remain", () =>
        {
            var expectedArena = before.Arena.ToArray();
            expectedArena[(int)(RastPort - ArenaAddress) + 0x38] = expectedAlgorithmic;
            Assert.Equal(expectedArena, after.Arena);
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
        });
        Check(failures, "library/vector/resident, caller and stack guards unchanged", () =>
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0, string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    public static IEnumerable<object[]> RastPortBoundaryCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary",
            "last-style-slot",
            "before-last-style-slot",
            "first-even-port",
            "null-port",
            "odd-port",
            "style-slot-wrap",
            "font-slot-wrap",
            "last-even-port",
            "last-odd-port"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(RastPortBoundaryCases))]
    public void SetStylePublicEntriesGuardTheCompleteConsumedRastPortEnvelope(
        bool relocated, bool autoInitEntry, string scenario)
    {
        const uint argument = 0xCAFE_000B;
        const uint enable = 0xD00D_000F;
        const byte intrinsic = 0x05;
        const byte initialAlgorithmic = 0x22;
        const byte expectedAlgorithmic = 0x2A;
        const uint expectedResult = 0x2F;
        var (pointer, success) = scenario switch
        {
            "ordinary" => (RastPort, true),
            "last-style-slot" => (0xFFFF_FFC6u, true),
            "before-last-style-slot" => (0xFFFF_FFC4u, true),
            "first-even-port" => (0x0000_0002u, true),
            "null-port" => (0x0000_0000u, false),
            "odd-port" => (0x00D0_0101u, false),
            "style-slot-wrap" => (0xFFFF_FFC8u, false),
            "font-slot-wrap" => (0xFFFF_FFCAu, false),
            "last-even-port" => (0xFFFF_FFFEu, false),
            "last-odd-port" => (0xFFFF_FFFFu, false),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        if (scenario == "last-style-slot")
        {
            Assert.Equal(0xFFFF_FFFEu, pointer + 0x38u);
            Assert.True((ulong)pointer + 99u > uint.MaxValue);
        }
        if (scenario == "style-slot-wrap")
        {
            Assert.Equal((ulong)uint.MaxValue, (ulong)pointer + 0x37u);
            Assert.True((ulong)pointer + 0x38u > uint.MaxValue);
        }
        if (scenario == "font-slot-wrap")
            Assert.True((ulong)pointer + 0x37u > uint.MaxValue);

        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedFontPointer(pointer, FontAddress);
        fixture.SeedField(FontAddress, 0x16, intrinsic);
        fixture.SeedField(pointer, 0x38, initialAlgorithmic);
        var before = fixture.CaptureMemory();

        var result = fixture.Invoke(pointer, argument, enable);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "RastPort admission, merge result and complete decline provenance", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(success ? expectedResult : argument, result.Data0);
            Assert.Equal(enable, result.Data1);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "public caller PC/SP and A1/A6; unchanged existing volatile A0 behavior", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(success ? FontAddress : NonArgumentRastPort, result.Address0);
        });
        Check(failures, "only an admitted physical AlgoStyle byte changes", () =>
        {
            var expectedArena = before.Arena.ToArray();
            var expectedHigh = before.High.ToArray();
            var expectedLow = before.Low.ToArray();
            if (success)
                SetExpectedPhysicalField(
                    expectedArena,
                    expectedHigh,
                    expectedLow,
                    pointer,
                    0x38,
                    expectedAlgorithmic);
            Assert.Equal(expectedArena, after.Arena);
            Assert.Equal(expectedHigh, after.High);
            Assert.Equal(expectedLow, after.Low);
            Assert.Equal(before.Font, after.Font);
        });
        Check(failures, "library/vector/resident, caller and stack guards unchanged", () =>
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0, string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static void SetExpectedPhysicalField(
        byte[] arena,
        byte[] high,
        byte[] low,
        uint pointer,
        int fieldOffset,
        byte value)
    {
        const uint highPhysicalAddress = 0x00FF_FF00;
        var physical = ((pointer & 0x00FF_FFFFu) + (uint)fieldOffset) & 0x00FF_FFFFu;
        if (physical >= ArenaAddress && physical < ArenaAddress + (uint)ArenaSize)
            arena[checked((int)(physical - ArenaAddress))] = value;
        else if (physical >= highPhysicalAddress)
            high[checked((int)(physical - highPhysicalAddress))] = value;
        else if (physical < 0x100)
            low[checked((int)physical)] = value;
        else
            throw new InvalidOperationException($"Unexpected physical test field 0x{physical:X8}.");
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
        byte[] GraphicsImage, byte[] Resident, byte[] Caller, byte[] StackGuards);
    private sealed record CallResult(uint Data0, uint Data1, uint Address0, uint Address1, uint Address6,
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
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _vectorSlot;
        private readonly uint _functionEntry;
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;

        internal Fixture(bool relocated, bool autoInitEntry)
        {
            const int lvo = -90;
            const int ordinal = 14;
            const GraphicsLvo key = GraphicsLvo.SetSoftStyle;
            Assert.Equal(lvo, (int)key);
            Assert.Equal(ordinal, (-lvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "SetSoftStyle/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
                (autoInitEntry ? "AUTOINIT JSR(A2)" : "public JSR d16(A6)");
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
                var library = NativeGraphicsLibraryImageBuilder.Build(FixedGraphicsBase, FixedResidentAddress, fallback, entries, fallback);
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[key];
            _vectorSlot = checked((uint)((long)GraphicsBase + lvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + (uint)ordinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            var arena = Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray();
            arena.AsSpan((int)(RastPort - ArenaAddress), RastPortBytes).Fill(0xD7);
            _bus.MapWritableMemory(ArenaAddress, arena);
            _bus.MapWritableMemory(FontAddress, Enumerable.Repeat((byte)0xB9, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++) _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2).
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)lvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal void SeedFontPointer(uint pointer, uint font)
        {
            // Classic RastPort.Font is the LONG at 0x34; TextFont.Style is
            // the byte at 0x16. References: d0.se/include/graphics/rastport.h
            // and d0.se/include/graphics/text.h (32-bit pointers, WORD packing).
            // Seed the actual physical cells so a wrapping logical A1 cannot
            // hide the missing native owner guard behind unmapped memory.
            for (var index = 0; index < sizeof(uint); index++)
                SeedField(pointer, 0x34 + index, (byte)(font >> (24 - index * 8)));
            var physicalField = ((pointer & 0x00FF_FFFFu) + 0x34u) & 0x00FF_FFFFu;
            Assert.Equal(font, _bus.ReadLong(physicalField));
        }

        internal void SeedField(uint pointer, int fieldOffset, byte value)
        {
            // Back the exact physical cell, including the alias of a wrapping
            // field, so no missing guard can hide behind unmapped memory.
            // Read back before entry; retain the full RastPort/font addresses.
            var physicalField = ((pointer & 0x00FF_FFFFu) + (uint)fieldOffset) & 0x00FF_FFFFu;
            _bus.WriteByte(physicalField, value, 0);
            Assert.Equal(value, _bus.ReadByte(physicalField));
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(FontAddress, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount), ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint pointer, uint value, uint enable)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[0] = _functionEntry;
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = value;
            _cpu.State.D[1] = enable;
            Assert.NotEqual(NonArgumentRastPort, pointer);
            _cpu.State.A[0] = NonArgumentRastPort;
            _cpu.State.A[1] = pointer;
            for (var index = 0; index < DataCanaries.Length; index++) _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++) _cpu.State.A[index + 2] = expectedAddresses[index];
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
                    // The linker can clone fallback RTS instructions. Observe
                    // only terminal result provenance, never internal Bccs.
                    usedFallback |= _cpu.State.D[0] == value;
                }
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress) continue;

                var differences = new List<string>();
                for (var index = 0; index < DataCanaries.Length; index++)
                    if (_cpu.State.D[index + 2] != DataCanaries[index])
                        differences.Add($"D{index + 2} expected {DataCanaries[index]:X8}, actual {_cpu.State.D[index + 2]:X8}");
                for (var index = 0; index < expectedAddresses.Length; index++)
                    if (_cpu.State.A[index + 2] != expectedAddresses[index])
                        differences.Add($"A{index + 2} expected {expectedAddresses[index]:X8}, actual {_cpu.State.A[index + 2]:X8}");
                if (_cpu.State.A[6] != GraphicsBase)
                    differences.Add($"A6 expected {GraphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                return new CallResult(_cpu.State.D[0], _cpu.State.D[1], _cpu.State.A[0], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, nativeReturnCount, differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
