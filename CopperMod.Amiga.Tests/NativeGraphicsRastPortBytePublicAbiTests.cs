using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRastPortBytePublicAbiTests
{
    // V39 byte accessors take their RastPort in A0, not the A1 used by older
    // pen setters. A1 points at another valid port to expose a copied ABI.
    // Register source: AROS rom/graphics/graphics.conf and CopperSharp SDK.
    // Preserve field-only admission at the top of the 32-bit address space.
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
        foreach (var vector in new[] { "GetAPen", "GetBPen", "GetDrMd", "GetOutlinePen", "SetWriteMask" })
        foreach (var relocated in new[] { false, true })
        foreach (var autoInitEntry in new[] { false, true })
        foreach (var scenario in new[] { "ordinary", "zero", "last-byte", "null", "odd", "field-wrap" })
            yield return new object[] { vector, relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void BytePublicEntriesUseA0AndPreserveCalleeSavedRegistersAndFieldOnlyAdmission(
        string vector, bool relocated, bool autoInitEntry, string scenario)
    {
        var spec = GetVector(vector);
        var success = scenario is "ordinary" or "zero" or "last-byte";
        var pointer = scenario switch
        {
            "ordinary" or "zero" => RastPort,
            "last-byte" => spec.LastValidPort,
            "null" => 0u,
            "odd" => RastPort + 1u,
            "field-wrap" => spec.FirstWrappedPort,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var value = scenario == "zero" ? (byte)0 : (byte)0xE1;
        var argument = 0xCAFE_BA00u | value;
        if (scenario == "last-byte")
            Assert.True((ulong)pointer + (uint)spec.Offset <= uint.MaxValue);
        if (scenario == "field-wrap")
            Assert.True((ulong)pointer + (uint)spec.Offset > uint.MaxValue);
        using var fixture = new Fixture(vector, relocated, autoInitEntry);
        fixture.SeedField(pointer, spec.Offset, spec.Write ? (byte)0x5A : value);
        var before = fixture.CaptureMemory();
        var expected = before;
        if (success && spec.Write)
        {
            if (pointer == RastPort)
            {
                var arena = before.Arena.ToArray();
                arena[(int)(RastPort - ArenaAddress) + spec.Offset] = value;
                expected = before with { Arena = arena };
            }
            else
            {
                var high = before.High.ToArray();
                var physicalField = ((pointer & 0x00FF_FFFFu) + (uint)spec.Offset) & 0x00FF_FFFFu;
                Assert.InRange(physicalField, 0x00FF_FF00u, 0x00FF_FFFFu);
                high[(int)(physicalField & 0xFF)] = value;
                expected = before with { High = high };
            }
        }

        var result = fixture.Invoke(pointer, argument);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "A0 field-only admission, unsigned getter and public mask success", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            // A getter zero-extends its byte. SetWriteMask returns nonzero ULONG
            // success (-1), not the portable helper's internal zero status.
            // Decline retains the full argument for the linked implementation.
            Assert.Equal(success ? (spec.Write ? uint.MaxValue : value) : argument, result.Data0);
            Assert.Equal(Fixture.Data1Canary, result.Data1);
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
        Check(failures, "exactly one setter byte changes; getters and declines change no guest state", () =>
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

    private sealed record VectorSpec(GraphicsLvo Key, int Lvo, int Ordinal,
        int Offset, uint LastValidPort, uint FirstWrappedPort, bool Write);

    private static VectorSpec GetVector(string vector) => vector switch
    {
        // Literal public offsets and last aligned field-owning pointers. A
        // complete 100-byte RastPort would wrap here; only this byte is owned.
        "GetAPen" => new(GraphicsLvo.GetAPen, -858, 142, 0x19, 0xFFFF_FFE6, 0xFFFF_FFE8, false),
        "GetBPen" => new(GraphicsLvo.GetBPen, -864, 143, 0x1A, 0xFFFF_FFE4, 0xFFFF_FFE6, false),
        "GetDrMd" => new(GraphicsLvo.GetDrMd, -870, 144, 0x1C, 0xFFFF_FFE2, 0xFFFF_FFE4, false),
        "GetOutlinePen" => new(GraphicsLvo.GetOutlinePen, -876, 145, 0x1B, 0xFFFF_FFE4, 0xFFFF_FFE6, false),
        "SetWriteMask" => new(GraphicsLvo.SetWriteMask, -984, 163, 0x18, 0xFFFF_FFE6, 0xFFFF_FFE8, true),
        _ => throw new ArgumentOutOfRangeException(nameof(vector))
    };

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

        internal Fixture(string vector, bool relocated, bool autoInitEntry)
        {
            var spec = GetVector(vector);
            var lvo = spec.Lvo;
            var ordinal = spec.Ordinal;
            var key = spec.Key;
            Assert.Equal(lvo, (int)key);
            Assert.Equal(ordinal, (-lvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = vector + "/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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
