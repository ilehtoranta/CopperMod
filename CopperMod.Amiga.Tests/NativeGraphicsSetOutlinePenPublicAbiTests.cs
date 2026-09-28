using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsSetOutlinePenPublicAbiTests
{
    // SetOutlinePen takes A0 and owns its pen byte plus Flags WORD. A1 points
    // at another valid port, so routing and D4 preservation are both observable.
    // Keep the existing prefix, last aligned Flags WORD and backed-wrap oracles.
    private const int RastPortBytes = 100;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint NonArgumentRastPort = ArenaAddress + 0x200;
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
        foreach (var scenario in new[] { "ordinary", "zero-pen", "old-zero", "old-ff", "already-outlined", "last-prefix", "null", "odd", "prefix-wrap" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void OutlinePublicEntriesUseA0AndPreserveCalleeSavedRegistersAndFlagsPrefixAdmission(
        bool relocated, bool autoInitEntry, string scenario)
    {
        var success = scenario is "ordinary" or "zero-pen" or "old-zero" or "old-ff"
            or "already-outlined" or "last-prefix";
        var pointer = scenario switch
        {
            "ordinary" or "zero-pen" or "old-zero" or "old-ff" or "already-outlined" => RastPort,
            "last-prefix" => 0xFFFF_FFDEu,
            "null" => 0u,
            "odd" => RastPort + 1u,
            "prefix-wrap" => 0xFFFF_FFE0u,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var oldPen = scenario switch { "old-zero" => (byte)0, "old-ff" => (byte)0xFF, _ => (byte)0x83 };
        var newPen = scenario == "zero-pen" ? (byte)0 : (byte)0xE1;
        var argument = 0xCAFE_BA00u | newPen;
        var flags = scenario == "already-outlined" ? (ushort)0xE6DD : (ushort)0xE6D5;
        if (scenario == "last-prefix")
            Assert.Equal((ulong)uint.MaxValue, (ulong)pointer + 0x21u);
        if (scenario == "prefix-wrap")
            Assert.True((ulong)pointer + 0x21u > uint.MaxValue);
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedField(pointer, 0x1B, oldPen);
        fixture.SeedField(pointer, 0x20, (byte)(flags >> 8));
        fixture.SeedField(pointer, 0x21, (byte)flags);
        var before = fixture.CaptureMemory();
        var expected = before;
        if (success)
        {
            // Literal AREAOUTLINE (0x0008) is the only added flag. In particular,
            // the preexisting NO_PENS (0x4000) and all other flags remain unchanged.
            var publications = new[]
            {
                (Offset: 0x1B, Value: newPen),
                (Offset: 0x20, Value: (byte)0xE6),
                (Offset: 0x21, Value: (byte)0xDD)
            };
            if (pointer == RastPort)
            {
                var arena = before.Arena.ToArray();
                foreach (var field in publications)
                    arena[(int)(RastPort - ArenaAddress) + field.Offset] = field.Value;
                expected = before with { Arena = arena };
            }
            else
            {
                var high = before.High.ToArray();
                foreach (var field in publications)
                {
                    var physicalField = ((pointer & 0x00FF_FFFFu) + (uint)field.Offset) & 0x00FF_FFFFu;
                    Assert.InRange(physicalField, 0x00FF_FF00u, 0x00FF_FFFFu);
                    high[(int)(physicalField & 0xFF)] = field.Value;
                }
                expected = before with { High = high };
            }
        }

        var result = fixture.Invoke(pointer, argument);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "Flags-prefix admission and unsigned previous-pen result", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(success ? (uint)oldPen : argument, result.Data0);
            // D1 is a scratch register on the handled path; decline
            // must still leave the caller input intact before the fallback.
            if (!success) Assert.Equal(Fixture.Data1Canary, result.Data1);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "actual public caller PC/SP, A0 input and unchanged A1/A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address0);
            Assert.Equal(NonArgumentRastPort, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "exact pen and Flags publication; every decline leaves all guest state unchanged", () =>
        {
            Assert.Equal(expected.Arena, after.Arena);
            Assert.Equal(expected.High, after.High);
            Assert.Equal(expected.Low, after.Low);
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

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record MemorySnapshot(byte[] Arena, byte[] High, byte[] Low,
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
        internal const uint Data1Canary = 0xD1D1_0101;
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
            const int lvo = -978;
            const int ordinal = 162;
            const GraphicsLvo key = GraphicsLvo.SetOutlinePen;
            Assert.Equal(lvo, (int)key);
            Assert.Equal(ordinal, (-lvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "SetOutlinePen/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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

        internal void SeedField(uint pointer, int fieldOffset, byte value)
        {
            // Back the exact physical cell, including the alias of a wrapping
            // field, so no missing guard can hide behind unmapped memory.
            // Read back before entry; never normalize the public A0 argument.
            var physicalField = ((pointer & 0x00FF_FFFFu) + (uint)fieldOffset) & 0x00FF_FFFFu;
            _bus.WriteByte(physicalField, value, 0);
            Assert.Equal(value, _bus.ReadByte(physicalField));
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount), ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint pointer, uint value)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[0] = _functionEntry;
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = value;
            _cpu.State.D[1] = Data1Canary;
            Assert.NotEqual(NonArgumentRastPort, pointer);
            _cpu.State.A[0] = pointer;
            _cpu.State.A[1] = NonArgumentRastPort;
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
