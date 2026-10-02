using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsTextLengthPublicAbiTests
{
    // Exercise all five native success exits and representative fallback
    // edges entered after body scratch has begun.  Pre-admission malformed
    // RastPorts remain controls for the already-verified early guard.
    private const int TextLengthLvo = -54;
    private const int TextLengthFunctionOrdinal = 8;
    private const uint InitialD0High = 0xCAFE_0000;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint FontAddress = 0x00D1_0000;
    private const uint TextAddress = 0x00D2_0000;
    private const uint CharSpaceAddress = 0x00D3_0000;
    private const uint CharKernAddress = 0x00D4_0000;
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
            "cached-width-success",
            "fixed-font-success",
            "proportional-one-success",
            "proportional-reverse-success",
            "proportional-walk-success",
            "odd-font-decline",
            "wrapped-font-decline",
            "proportional-default-table-decline",
            "proportional-reverse-nonnegative-decline",
            "proportional-walk-table-decline",
            "rastport-null-control",
            "rastport-odd-control",
            "rastport-wrap-control"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void TextLengthPublicEntriesPreserveCompleteRegisterFrameAcrossEveryExit(
        bool relocated,
        bool autoInitEntry,
        string scenario)
    {
        using var fixture = new Fixture(relocated, autoInitEntry);
        var expectation = fixture.Configure(scenario);
        var before = fixture.CaptureMemory();

        var result = fixture.Invoke(expectation.RastPortAddress, expectation.Count);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "native result and success/fallback provenance", () =>
        {
            Assert.Equal(expectation.ExpectedD0, result.Data0);
            Assert.Equal(expectation.UsedFallback, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "public A0/A1/A6 and caller PC/SP", () =>
        {
            Assert.Equal(expectation.ExpectedA0, result.Address0);
            Assert.Equal(expectation.RastPortAddress, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
        });
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0,
                string.Join("; ", result.RegisterDifferences)));
        Check(failures, "metric query preserves RastPort, fonts, tables, source, and aliases", () =>
        {
            Assert.Equal(before.Arena, after.Arena);
            Assert.Equal(before.Font, after.Font);
            Assert.Equal(before.Text, after.Text);
            Assert.Equal(before.CharSpace, after.CharSpace);
            Assert.Equal(before.CharKern, after.CharKern);
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
        Assert.True(failures.Count == 0, $"{fixture.Route}, {scenario}:\n" + string.Join("\n", failures));
    }

    private static uint InitialD0(ushort count) => InitialD0High | count;

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record Expectation(uint RastPortAddress, ushort Count, uint ExpectedD0,
        uint ExpectedA0, bool UsedFallback);
    private sealed record MemorySnapshot(byte[] Arena, byte[] Font, byte[] Text,
        byte[] CharSpace, byte[] CharKern, byte[] High, byte[] Low,
        byte[] GraphicsImage, byte[] Resident, byte[] Caller, byte[] StackGuards);
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
            Assert.Equal(TextLengthLvo, (int)GraphicsLvo.TextLength);
            Assert.Equal(TextLengthFunctionOrdinal, (-TextLengthLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "TextLength/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.TextLength];
            _vectorSlot = checked((uint)((long)GraphicsBase + TextLengthLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + TextLengthFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray());
            _bus.MapWritableMemory(FontAddress, Enumerable.Repeat((byte)0xB9, 0x100).ToArray());
            _bus.MapWritableMemory(TextAddress, Enumerable.Repeat((byte)0x71, 0x200).ToArray());
            _bus.MapWritableMemory(CharSpaceAddress, Enumerable.Repeat((byte)0x83, 0x204).ToArray());
            _bus.MapWritableMemory(CharKernAddress, Enumerable.Repeat((byte)0x95, 0x204).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);

            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2).
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // Real negative-vector JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)TextLengthLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal Expectation Configure(string scenario)
        {
            ushort count = 1;
            uint rastPortAddress = RastPort;
            uint font = FontAddress;
            uint charSpace = CharSpaceAddress;
            uint charKern = CharKernAddress;
            ushort spacing = 3;
            byte flags = 0;
            byte firstCharacter = (byte)'A';
            short firstSpace = 7;
            short firstKern = -2;
            uint expectedD0;
            uint expectedA0;
            var declined = false;

            switch (scenario)
            {
                case "cached-width-success":
                    count = 3;
                    font = 0;
                    spacing = unchecked((ushort)-2);
                    expectedD0 = 18;
                    expectedA0 = TextAddress;
                    break;
                case "fixed-font-success":
                    count = 3;
                    spacing = unchecked((ushort)-2);
                    expectedD0 = 15;
                    expectedA0 = FontAddress;
                    break;
                case "proportional-one-success":
                    flags = 0x20;
                    expectedD0 = 8;
                    expectedA0 = CharKernAddress;
                    break;
                case "proportional-reverse-success":
                    flags = 0x24;
                    spacing = 1;
                    firstSpace = -7;
                    firstKern = 1;
                    expectedD0 = unchecked((uint)-5);
                    expectedA0 = FontAddress;
                    break;
                case "proportional-walk-success":
                    count = 3;
                    flags = 0x20;
                    expectedD0 = 33;
                    expectedA0 = CharKernAddress;
                    break;
                case "odd-font-decline":
                    font = FontAddress + 1u;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                case "wrapped-font-decline":
                    font = 0xFFFF_FFCEu;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                case "proportional-default-table-decline":
                    flags = 0x20;
                    firstCharacter = (byte)'Z';
                    charSpace = 0;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                case "proportional-reverse-nonnegative-decline":
                    flags = 0x24;
                    spacing = 1;
                    firstKern = 1;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                case "proportional-walk-table-decline":
                    count = 3;
                    flags = 0x20;
                    charKern = 0;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                case "rastport-null-control":
                    rastPortAddress = 0;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                case "rastport-odd-control":
                    rastPortAddress = RastPort + 1u;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                case "rastport-wrap-control":
                    rastPortAddress = 0xFFFF_FFC0u;
                    expectedD0 = InitialD0(count);
                    expectedA0 = TextAddress;
                    declined = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            _bus.WriteLong(RastPort + 0x34u, font);
            _bus.WriteByte(RastPort + 0x38u, 0, 0);
            _bus.WriteWord(RastPort + 0x3Cu, 8);
            _bus.WriteWord(RastPort + 0x40u, spacing);
            _bus.WriteByte(FontAddress + 0x16u, 0, 0);
            _bus.WriteByte(FontAddress + 0x17u, flags, 0);
            _bus.WriteWord(FontAddress + 0x18u, 7);
            _bus.WriteByte(FontAddress + 0x20u, (byte)'A', 0);
            _bus.WriteByte(FontAddress + 0x21u, (byte)'B', 0);
            _bus.WriteLong(FontAddress + 0x2Cu, charSpace);
            _bus.WriteLong(FontAddress + 0x30u, charKern);
            _bus.WriteByte(TextAddress + 0u, firstCharacter, 0);
            _bus.WriteByte(TextAddress + 1u, (byte)'B', 0);
            _bus.WriteByte(TextAddress + 2u, (byte)'A', 0);
            _bus.WriteWord(CharSpaceAddress + 0u, unchecked((ushort)firstSpace));
            _bus.WriteWord(CharSpaceAddress + 2u, 11);
            _bus.WriteWord(CharSpaceAddress + 4u, 5); // default slot
            _bus.WriteWord(CharKernAddress + 0u, unchecked((ushort)firstKern));
            _bus.WriteWord(CharKernAddress + 2u, 1);
            _bus.WriteWord(CharKernAddress + 4u, 0); // default slot
            return new Expectation(rastPortAddress, count, expectedD0, expectedA0, declined);
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize),
            ReadBytes(FontAddress, 0x100),
            ReadBytes(TextAddress, 0x200),
            ReadBytes(CharSpaceAddress, 0x204),
            ReadBytes(CharKernAddress, 0x204),
            ReadBytes(HighPhysicalAddress, 0x100),
            ReadBytes(0, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount),
            ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint rastPortAddress, ushort count)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[0] = _functionEntry;
            var initialD0 = InitialD0(count);
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = initialD0;
            _cpu.State.D[1] = 0xD1D1_0101;
            _cpu.State.A[0] = TextAddress;
            _cpu.State.A[1] = rastPortAddress;
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
                    usedFallback |= _cpu.State.D[0] == initialD0;
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
