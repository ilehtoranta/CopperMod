using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMovePublicAbiTests
{
    // Activate only after Move's independent 100-byte extent correction.
    // This fixture tests native public-register preservation, not mapped
    // writability probes, layer clipping, raster storage or host dispatch.
    private const int MoveLvo = -240;
    private const int MoveFunctionOrdinal = 39;
    private const int RastPortBytes = 100;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
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
        foreach (var scenario in new[] { "ordinary", "flags-set", "exact-end", "null", "odd", "wrap" })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void MovePublicEntriesPreserveCalleeSavedRegistersAcrossEveryExit(
        bool relocated, bool autoInitEntry, string scenario)
    {
        var success = scenario is "ordinary" or "flags-set" or "exact-end";
        var pointer = scenario switch
        {
            "ordinary" or "flags-set" => RastPort,
            "exact-end" => 0xFFFF_FF9Cu,
            "null" => 0u,
            "odd" => RastPort + 1u,
            "wrap" => 0xFFFF_FF9Eu,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var x = scenario switch
        {
            "ordinary" => 0xCAFE_8000u,
            "flags-set" => 0x1357_0000u,
            "exact-end" => 0xF00D_7FFFu,
            _ => 0xCAFE_1234u
        };
        var y = scenario switch
        {
            "ordinary" => 0xBEEF_7FFFu,
            "flags-set" => 0x2468_FFFFu,
            "exact-end" => 0xFACE_8000u,
            _ => 0xBEEF_FEDCu
        };
        using var fixture = new Fixture(relocated, autoInitEntry);
        fixture.SeedCursor(pointer, scenario == "flags-set" ? (ushort)0xA6D5 : (ushort)0xA6D4);
        var before = fixture.CaptureMemory();
        var expected = before;
        if (success)
        {
            if (pointer == RastPort)
            {
                var arena = before.Arena.ToArray();
                PublishMove(arena.AsSpan((int)(RastPort - ArenaAddress), RastPortBytes), x, y);
                expected = before with { Arena = arena };
            }
            else
            {
                var high = before.High.ToArray();
                // Only snapshot indexing uses the physical alias. The
                // public A1 argument remains the original logical pointer.
                PublishMove(high.AsSpan(0x9C, RastPortBytes), x, y);
                expected = before with { High = high };
            }
        }

        var result = fixture.Invoke(pointer, x, y);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        // Independent controls remain visible alongside the expected D4-only
        // red; no register failure can hide a mutation or incorrect return.
        Check(failures, "native admission and existing coordinate/return conventions", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            // Move is VOID publicly. Zero is this body's existing handled
            // result; a decline retains the full original coordinates.
            Assert.Equal(success ? 0u : x, result.Data0);
            if (!success) Assert.Equal(y, result.Data1);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "actual public caller PC/SP and unchanged A1/A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "complete RastPort/publication and adjacent or wrapping guards", () =>
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

    private static void PublishMove(Span<byte> rastPort, uint x, uint y)
    {
        // Independent classic offsets and WORD coordinate semantics. Preserve
        // every other byte, including the LinePtrn word and unrelated Flags.
        BinaryPrimitives.WriteUInt16BigEndian(rastPort[0x24..], unchecked((ushort)x));
        BinaryPrimitives.WriteUInt16BigEndian(rastPort[0x26..], unchecked((ushort)y));
        rastPort[0x1E] = 15;
        var flags = BinaryPrimitives.ReadUInt16BigEndian(rastPort[0x20..]);
        BinaryPrimitives.WriteUInt16BigEndian(rastPort[0x20..], (ushort)(flags | 1));
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
    private sealed record CallResult(uint Data0, uint Data1, uint Address1, uint Address6,
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

        internal Fixture(bool relocated, bool autoInitEntry)
        {
            Assert.Equal(MoveLvo, (int)GraphicsLvo.Move);
            Assert.Equal(MoveFunctionOrdinal, (-MoveLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
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

            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.Move];
            _vectorSlot = checked((uint)((long)GraphicsBase + MoveLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + MoveFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            var arena = Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray();
            arena.AsSpan((int)(RastPort - ArenaAddress), RastPortBytes).Fill(0xD7);
            _bus.MapWritableMemory(ArenaAddress, arena);
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2); A1 remains the public RP argument
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // JSR d16(A6), using the real library base
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)MoveLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

        internal void SeedCursor(uint pointer, ushort flags)
        {
            // The even wrapping-RP case still has each individual field
            // mapped. A decline is not manufactured by an unmapped store.
            // Do not seed NULL or odd pointers, and never repair after entry.
            if (pointer == 0 || (pointer & 1u) != 0) return;
            _bus.WriteByte(pointer + 0x1E, 0x62, 0);
            _bus.WriteWord(pointer + 0x20, flags);
            _bus.WriteWord(pointer + 0x22, 0x5AA5);
            _bus.WriteWord(pointer + 0x24, 0x1357);
            _bus.WriteWord(pointer + 0x26, 0x2468);
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount), ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint pointer, uint x, uint y)
        {
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = _functionEntry; // The function pointer is also A2's saved-value canary.
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = x;
            _cpu.State.D[1] = y;
            _cpu.State.A[0] = 0xA0A0_A0A1;
            _cpu.State.A[1] = pointer;
            for (var index = 0; index < DataCanaries.Length; index++)
                _cpu.State.D[index + 2] = DataCanaries[index];
            for (var index = 0; index < expectedAddresses.Length; index++)
                _cpu.State.A[index + 2] = expectedAddresses[index];
            _cpu.State.A[6] = GraphicsBase;
            _cpu.ExecuteInstruction();
            Assert.Equal(_autoInitEntry ? _functionEntry : _vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            if (!_autoInitEntry)
                _cpu.ExecuteInstruction(); // The actual physical negative-vector JMP.
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 256; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}; no host graphics/Exec gateway is installed.");
                var opcode = _bus.ReadWord(pc);
                // Observe the three admission Bcc.W decisions rather than
                // require the global fallback PC: the linker may emit a local RTS.
                var isAdmissionBranch = opcode is 0x6700 or 0x6600 or 0x6200;
                if (opcode == 0x4E75) nativeReturnCount++;
                _cpu.ExecuteInstruction();
                usedFallback |= isAdmissionBranch && _cpu.State.ProgramCounter != pc + 4u;
                if (_cpu.State.ProgramCounter != ReturnAddress) continue;

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
                return new CallResult(_cpu.State.D[0], _cpu.State.D[1], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, nativeReturnCount, differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
