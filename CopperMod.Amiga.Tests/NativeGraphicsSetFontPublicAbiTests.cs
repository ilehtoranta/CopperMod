using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsSetFontPublicAbiTests
{
    // Native public-register coverage plus the exact field-scoped RastPort
    // publication boundary. Host overlay admission keeps its separate,
    // conservative 100-byte writable probe; SetRPAttrsA's tail caller and
    // malformed font contents remain separate.
    private const int SetFontLvo = -66;
    private const int SetFontFunctionOrdinal = 10;
    private const int RastPortBytes = 100;
    private const uint CapturedD0 = 0x1234_5678;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint FontArena = 0x00D1_0000;
    private const uint Font = FontArena + 0x20;
    private const uint WrappingFont = 0xFFFF_FFCE;
    private const uint WrappingGraphicsBase = 0xFFFF_FF64;
    private const ushort FontHeight = 11;
    private const ushort FontWidth = 7;
    private const ushort FontBaseline = 8;
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
        {
            foreach (var autoInitEntry in new[] { false, true })
            foreach (var scenario in new[]
            {
                "explicit-valid", "explicit-odd", "explicit-wrap", "default-valid",
                "default-null", "default-odd", "default-wrap"
            })
                yield return new object[] { relocated, autoInitEntry, scenario };

            // Invalid A6 cannot address a real graphics.library negative
            // vector. The real AUTOINIT function pointer admits these calls
            // without forging a vector table or rewriting A6 after entry.
            foreach (var scenario in new[] { "base-null", "base-odd", "base-wrap" })
                yield return new object[] { relocated, true, scenario };
        }
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void SetFontPublicEntriesPreserveCalleeSavedRegistersAcrossEveryExit(
        bool relocated, bool autoInitEntry, string scenario)
    {
        var success = scenario is "explicit-valid" or "default-valid";
        var fontArgument = scenario switch
        {
            "explicit-valid" => Font,
            "explicit-odd" => Font + 1u,
            "explicit-wrap" => WrappingFont,
            "default-valid" or "default-null" or "default-odd" or "default-wrap" or
                "base-null" or "base-odd" or "base-wrap" => 0u,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        using var fixture = new Fixture(relocated, autoInitEntry, scenario);
        var before = fixture.CaptureMemory();
        var expectedArena = before.Arena.ToArray();
        if (success)
        {
            // Independent classic offsets; neither an initializer nor a
            // repaired guest header is used to construct the expected result.
            var expectedRastPort = expectedArena.AsSpan((int)(RastPort - ArenaAddress), RastPortBytes);
            BinaryPrimitives.WriteUInt32BigEndian(expectedRastPort[0x34..], Font);
            expectedRastPort[0x38] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(expectedRastPort[0x3A..], FontHeight);
            BinaryPrimitives.WriteUInt16BigEndian(expectedRastPort[0x3C..], FontWidth);
            BinaryPrimitives.WriteUInt16BigEndian(expectedRastPort[0x3E..], FontBaseline);
        }

        var result = fixture.Invoke(fontArgument);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        // Collect independent controls so the expected D6-only failure does
        // not conceal an incorrect return, mutation or public-call route.
        Check(failures, "existing D0 convention and body-local return", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.Declined);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "actual caller PC/SP and existing A1/A6 behavior", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(fontArgument == 0 && success ? Font : fontArgument, result.Address0);
            Assert.Equal(fixture.RastPortAddress, result.Address1);
            Assert.Equal(fixture.EntryGraphicsBase, result.Address6);
        });
        Check(failures, "complete RastPort, adjacent canaries and logical-wrap aliases", () =>
        {
            Assert.Equal(expectedArena, after.Arena);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
        });
        Check(failures, "font, library/vector/resident, caller and stack guards unchanged", () =>
        {
            Assert.Equal(before.Font, after.Font);
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
        foreach (var useDefaultFont in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary",
            "last-publication-slot",
            "before-last-publication-slot",
            "first-even-port",
            "null-port",
            "odd-port",
            "publication-wrap",
            "font-slot-wrap",
            "last-even-port",
            "last-odd-port"
        })
            yield return new object[] { relocated, autoInitEntry, useDefaultFont, scenario };
    }

    [Theory]
    [MemberData(nameof(RastPortBoundaryCases))]
    public void SetFontPublicEntriesGuardTheCompleteConsumedRastPortEnvelope(
        bool relocated,
        bool autoInitEntry,
        bool useDefaultFont,
        string scenario)
    {
        var (pointer, success) = scenario switch
        {
            "ordinary" => (RastPort, true),
            "last-publication-slot" => (0xFFFF_FFC0u, true),
            "before-last-publication-slot" => (0xFFFF_FFBEu, true),
            "first-even-port" => (0x0000_0002u, true),
            "null-port" => (0x0000_0000u, false),
            "odd-port" => (0x00D0_0101u, false),
            "publication-wrap" => (0xFFFF_FFC2u, false),
            "font-slot-wrap" => (0xFFFF_FFCAu, false),
            "last-even-port" => (0xFFFF_FFFEu, false),
            "last-odd-port" => (0xFFFF_FFFFu, false),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        if (scenario == "last-publication-slot")
        {
            Assert.Equal((ulong)uint.MaxValue, (ulong)pointer + 0x3Fu);
            Assert.True((ulong)pointer + 99u > uint.MaxValue);
        }
        if (scenario == "publication-wrap")
        {
            Assert.Equal(0xFFFF_FFFEu, pointer + 0x3Cu);
            Assert.True((ulong)pointer + 0x3Fu > uint.MaxValue);
        }
        if (scenario == "font-slot-wrap")
            Assert.True((ulong)pointer + 0x37u > uint.MaxValue);

        const string fixtureScenario = "explicit-valid";
        var fontArgument = useDefaultFont ? 0u : Font;
        using var fixture = new Fixture(relocated, autoInitEntry, fixtureScenario, pointer);
        var before = fixture.CaptureMemory();
        var expectedArena = before.Arena.ToArray();
        var expectedHigh = before.High.ToArray();
        var expectedLow = before.Low.ToArray();
        if (success)
            ApplyExpectedPhysicalPublication(
                expectedArena,
                expectedHigh,
                expectedLow,
                pointer);

        var result = fixture.Invoke(fontArgument);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "RastPort admission, D0 result and body-local decline provenance", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.Declined);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "complete public A0/A1 arguments, caller PC/SP and A6", () =>
        {
            Assert.Equal(success ? Font : fontArgument, result.Address0);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.EntryGraphicsBase, result.Address6);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
        });
        Check(failures, "only the five admitted physical RastPort fields change", () =>
        {
            Assert.Equal(expectedArena, after.Arena);
            Assert.Equal(expectedHigh, after.High);
            Assert.Equal(expectedLow, after.Low);
        });
        Check(failures, "font, library/vector/resident, caller and stack guards unchanged", () =>
        {
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0, string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0,
            $"{fixture.Route}/{(useDefaultFont ? "default" : "explicit")}, {scenario}:\n"
            + string.Join("\n", failures));
    }

    private static void ApplyExpectedPhysicalPublication(
        byte[] arena,
        byte[] high,
        byte[] low,
        uint pointer)
    {
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x34, unchecked((byte)(Font >> 24)));
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x35, unchecked((byte)(Font >> 16)));
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x36, unchecked((byte)(Font >> 8)));
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x37, unchecked((byte)Font));
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x38, 0);
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x3A, (byte)(FontHeight >> 8));
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x3B, (byte)FontHeight);
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x3C, (byte)(FontWidth >> 8));
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x3D, (byte)FontWidth);
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x3E, (byte)(FontBaseline >> 8));
        SetExpectedPhysicalByte(arena, high, low, pointer, 0x3F, (byte)FontBaseline);
    }

    private static void SetExpectedPhysicalByte(
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
    private sealed record CallResult(uint Data0, uint Address0, uint Address1, uint Address6, uint ProgramCounter,
        uint StackPointer, bool Declined, int NativeReturnCount, string[] RegisterDifferences);

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
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;
        private readonly uint _rastPort;

        internal Fixture(bool relocated, bool autoInitEntry, string scenario, uint rastPort = RastPort)
        {
            Assert.Equal(SetFontLvo, (int)GraphicsLvo.SetFont);
            Assert.Equal(SetFontFunctionOrdinal, (-SetFontLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            _rastPort = rastPort;
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/" +
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.SetFont];
            // Physical negative-vector placement is derived from the actual
            // library base and the public signed LVO, never a vector-index helper.
            _vectorSlot = checked((uint)((long)GraphicsBase + SetFontLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + SetFontFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            EntryGraphicsBase = scenario switch
            {
                "base-null" => 0u,
                "base-odd" => GraphicsBase + 1u,
                "base-wrap" => WrappingGraphicsBase,
                _ => GraphicsBase
            };
            if (EntryGraphicsBase != GraphicsBase)
                Assert.True(autoInitEntry, "A malformed A6 must not be disguised as a public vector call.");
            var defaultFont = scenario switch
            {
                "default-null" => 0u,
                "default-odd" => Font + 1u,
                "default-wrap" => WrappingFont,
                _ => Font
            };

            _bus.MapWritableMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray());
            _bus.MapWritableMemory(FontArena, Enumerable.Repeat((byte)0xE9, 0x100).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            SeedFont();
            SeedRastPort(_rastPort);
            _bus.WriteLong(GraphicsBase + 0x9A, defaultFont);
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2); preserves A0/A1 as the public arguments
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE);
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)SetFontLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        private void SeedRastPort(uint pointer)
        {
            // Distinct old values make every permitted publication observable.
            // Seed actual physical cells, including high-end and wrapped
            // aliases, so the native A1 guard cannot hide behind mapping gaps.
            SeedPhysicalByte(pointer, 0x34, 0xDE);
            SeedPhysicalByte(pointer, 0x35, 0xED);
            SeedPhysicalByte(pointer, 0x36, 0x24);
            SeedPhysicalByte(pointer, 0x37, 0x68);
            SeedPhysicalByte(pointer, 0x38, 0x07);
            SeedPhysicalByte(pointer, 0x39, 0x5B); // TextFlags remains unchanged.
            SeedPhysicalByte(pointer, 0x3A, 0xE2);
            SeedPhysicalByte(pointer, 0x3B, 0xE2);
            SeedPhysicalByte(pointer, 0x3C, 0xE3);
            SeedPhysicalByte(pointer, 0x3D, 0xE3);
            SeedPhysicalByte(pointer, 0x3E, 0xE4);
            SeedPhysicalByte(pointer, 0x3F, 0xE4);
            SeedPhysicalByte(pointer, 0x40, 0x6B); // TxSpacing/trailing guard.
            SeedPhysicalByte(pointer, 0x41, 0x7C);
        }

        private void SeedPhysicalByte(uint pointer, int fieldOffset, byte value)
        {
            var physical = ((pointer & 0x00FF_FFFFu) + (uint)fieldOffset) & 0x00FF_FFFFu;
            _bus.WriteByte(physical, value, 0);
            Assert.Equal(value, _bus.ReadByte(physical));
        }

        private void SeedFont()
        {
            // A coherent 52-byte standard TextFont and mapped name/strike.
            // SetFont itself consumes only the existing three metric words.
            _bus.ClearMemory(Font, 52);
            _bus.WriteByte(Font + 0x08, 0x0C, 0);
            _bus.WriteWord(Font + 0x12, 52);
            _bus.WriteLong(Font + 0x0A, Font + 0x40);
            _bus.WriteWord(Font + 0x14, FontHeight);
            _bus.WriteWord(Font + 0x18, FontWidth);
            _bus.WriteWord(Font + 0x1A, FontBaseline);
            _bus.WriteByte(Font + 0x20, 63, 0);
            _bus.WriteByte(Font + 0x21, 63, 0);
            _bus.WriteLong(Font + 0x22, Font + 0x70);
            _bus.WriteWord(Font + 0x26, 2);
            _bus.WriteLong(Font + 0x28, Font + 0x60);
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
        internal uint EntryGraphicsBase { get; }
        internal uint ReturnAddress { get; }
        internal uint RastPortAddress => _rastPort;
        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(FontArena, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount), ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint fontArgument)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = _functionEntry; // A2's actual entry value is its preservation canary
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = fontArgument;
            _cpu.State.A[1] = _rastPort;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = EntryGraphicsBase; // never rewritten after the actual public call
            _cpu.ExecuteInstruction();
            Assert.Equal(_autoInitEntry ? _functionEntry : _vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            var enteredBody = false;
            var declined = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 10_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc == _vectorSlot || (pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length),
                    $"Unexpected execution address 0x{pc:X8}; no host graphics gateway is installed.");
                enteredBody |= pc == _entry;
                if (_bus.ReadWord(pc) == 0x4E75)
                {
                    nativeReturnCount++;
                    // This emitter uses its own restore-and-RTS decline, not
                    // the BuildCode fallback PC. CapturedD0 is never success.
                    declined |= _cpu.State.D[0] == CapturedD0;
                }
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress)
                    continue;
                Assert.True(enteredBody, "The SetFont public entry was not reached.");
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
                if (_cpu.State.A[6] != EntryGraphicsBase)
                    differences.Add($"A6 expected {EntryGraphicsBase:X8}, actual {_cpu.State.A[6]:X8}");
                // D0/D1/A0/A1 are publicly volatile. Only SetFont's existing
                // D0 and unchanged A1 behavior are retained as extra controls.
                return new CallResult(_cpu.State.D[0], _cpu.State.A[0], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], declined, nativeReturnCount, differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
