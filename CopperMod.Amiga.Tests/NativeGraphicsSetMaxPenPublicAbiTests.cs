using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsSetMaxPenPublicAbiTests
{
    // SetMaxPen takes A0. Nonzero calls own only Mask; zero is field-free.
    // A1 is a distinct valid decoy. Preserve existing mask/decline semantics
    // while proving D2/D4 preservation at real public native entries.
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
        foreach (var scenario in new[]
        {
            "one", "two", "three", "four", "seven", "eight", "high-bit", "byte-max",
            "last-byte", "null", "odd", "field-wrap", "too-wide", "upper-bits", "all-ones",
            "zero", "zero-null", "zero-odd", "zero-last-byte", "zero-field-wrap", "zero-all-ones"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void MaxPenPublicEntriesUseA0AndPreserveCalleeSavedRegistersAndFieldFreeZero(
        bool relocated, bool autoInitEntry, string scenario)
    {
        var (pointer, argument, mask, success) = GetCase(scenario);
        Assert.Equal(success && argument != 0, mask >= 0);
        if (scenario == "last-byte")
            Assert.Equal((ulong)uint.MaxValue - 1u, (ulong)pointer + 0x18u);
        if (scenario == "field-wrap")
            Assert.Equal(1UL << 32, (ulong)pointer + 0x18u);
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedField(pointer, 0x18, 0x5A);
        var before = fixture.CaptureMemory();
        var expected = before;
        if (mask >= 0)
        {
            Assert.InRange(mask, 0, 255);
            Assert.NotEqual((byte)0x5A, (byte)mask);
            if (pointer == RastPort)
            {
                var arena = before.Arena.ToArray();
                arena[(int)(RastPort - ArenaAddress) + 0x18] = (byte)mask;
                expected = before with { Arena = arena };
            }
            else
            {
                var high = before.High.ToArray();
                var physicalField = ((pointer & 0x00FF_FFFFu) + 0x18u) & 0x00FF_FFFFu;
                Assert.Equal(0x00FF_FFFEu, physicalField);
                high[0xFE] = (byte)mask;
                expected = before with { High = high };
            }
        }

        var result = fixture.Invoke(pointer, argument);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "field-only admission, full decline argument and retained native void-result shape", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            // SetMaxPen is void. Retain this native arm's existing zero D0 on
            // success; do not mix a return-policy change into this ABI unit.
            Assert.Equal(success ? 0u : argument, result.Data0);
            if (!success || argument == 0) Assert.Equal(Fixture.Data1Canary, result.Data1);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "zero retains the field-free four-instruction early return", () =>
        {
            if (argument != 0)
            {
                Assert.Empty(result.ZeroPathOpcodes);
                return;
            }
            // D0==0 cannot distinguish a cloned fallback by result provenance.
            // Observe the actual no-read path instead: TST.L D0, untaken BNE,
            // MOVEQ #0,D0, RTS. Only code/stack accesses are possible here.
            Assert.Collection(result.ZeroPathOpcodes,
                opcode => Assert.Equal((ushort)0x4A80, opcode),
                opcode => Assert.Equal(0x6600, opcode & 0xFF00),
                opcode => Assert.Equal((ushort)0x7000, opcode),
                opcode => Assert.Equal((ushort)0x4E75, opcode));
        });
        Check(failures, "actual public caller PC/SP, A0 input and unchanged A1/A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address0);
            Assert.Equal(NonArgumentRastPort, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "exact owned mask publication; zero and decline preserve all guest bytes", () =>
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

    private static (uint Pointer, uint Argument, int Mask, bool Handled) GetCase(string scenario) => scenario switch
    {
        "one" => (RastPort, 1, 0x01, true),
        "two" => (RastPort, 2, 0x03, true),
        "three" => (RastPort, 3, 0x03, true),
        "four" => (RastPort, 4, 0x07, true),
        "seven" => (RastPort, 7, 0x07, true),
        "eight" => (RastPort, 8, 0x0F, true),
        "high-bit" => (RastPort, 0x80, 0xFF, true),
        "byte-max" => (RastPort, 0xFF, 0xFF, true),
        "last-byte" => (0xFFFF_FFE6, 3, 0x03, true),
        "null" => (0, 3, -1, false),
        "odd" => (RastPort + 1u, 3, -1, false),
        "field-wrap" => (0xFFFF_FFE8, 3, -1, false),
        "too-wide" => (RastPort, 0x100, -1, false),
        "upper-bits" => (RastPort, 0xCAFE_0003, -1, false),
        "all-ones" => (RastPort, uint.MaxValue, -1, false),
        "zero" => (RastPort, 0, -1, true),
        "zero-null" => (0, 0, -1, true),
        "zero-odd" => (RastPort + 1u, 0, -1, true),
        "zero-last-byte" => (0xFFFF_FFE6, 0, -1, true),
        "zero-field-wrap" => (0xFFFF_FFE8, 0, -1, true),
        "zero-all-ones" => (uint.MaxValue, 0, -1, true),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
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
        string[] RegisterDifferences, ushort[] ZeroPathOpcodes);

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
            const int lvo = -990;
            const int ordinal = 164;
            const GraphicsLvo key = GraphicsLvo.SetMaxPen;
            Assert.Equal(lvo, (int)key);
            Assert.Equal(ordinal, (-lvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = "SetMaxPen/" + (relocated ? "relocated HUNK" : "fixed image") + "/" +
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
            var zeroPathOpcodes = value == 0 ? new List<ushort>() : null;
            for (var instruction = 0; instruction < 10_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}; no host graphics/Exec gateway is installed.");
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                var opcode = _bus.ReadWord(pc);
                zeroPathOpcodes?.Add(opcode);
                if (opcode == 0x4E75)
                {
                    nativeReturnCount++;
                    // Nonzero fallback classification uses terminal D0 provenance.
                    // Zero has a separate field-free instruction-trace oracle.
                    usedFallback |= value != 0 && _cpu.State.D[0] == value;
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
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, nativeReturnCount,
                    differences.ToArray(), zeroPathOpcodes?.ToArray() ?? Array.Empty<ushort>());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
