using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsMoveExtentTests
{
    // Move's native guard is a logical-address envelope check, not a probe
    // of every mapped/writable byte. Its separate D4 frame debt is excluded.
    private const int ClassicRastPortBytes = 100;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 32 + 180 + 32;
    private const uint RastPort = ArenaAddress + 32;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    public static IEnumerable<object[]> ExtentCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary-hundred", "old-exact-end", "first-new-even", "exact-hundred-end",
            "first-even-wrap", "null", "ordinary-odd", "high-odd"
        })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(ExtentCases))]
    public void PublicMoveAdmitsTheClassicHundredByteLogicalExtent(bool relocated, string scenario)
    {
        var pointer = scenario switch
        {
            "ordinary-hundred" => RastPort,
            "old-exact-end" => 0xFFFF_FF4Cu,
            "first-new-even" => 0xFFFF_FF4Eu,
            "exact-hundred-end" => 0xFFFF_FF9Cu,
            "first-even-wrap" => 0xFFFF_FF9Eu,
            "null" => 0u,
            "ordinary-odd" => RastPort + 1,
            "high-odd" => 0xFFFF_FF9Bu,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var success = scenario is "ordinary-hundred" or "old-exact-end" or
            "first-new-even" or "exact-hundred-end";
        var x = scenario switch
        {
            "ordinary-hundred" or "exact-hundred-end" => 0xCAFE_8000u,
            "old-exact-end" => 0x1357_0000u,
            _ => 0xCAFE_1234u
        };
        var y = scenario switch
        {
            "ordinary-hundred" or "exact-hundred-end" => 0xBEEF_7FFFu,
            "old-exact-end" => 0x2468_FFFFu,
            _ => 0xBEEF_FEDCu
        };
        using var fixture = new Fixture(relocated);
        fixture.SeedCursor(pointer, scenario == "exact-hundred-end" ? (ushort)0xA6D5 : (ushort)0xA6D4);
        var before = fixture.CaptureMemory();
        var expected = before;
        if (success)
        {
            if (pointer == RastPort)
            {
                var arena = before.Arena.ToArray();
                PublishMove(arena, (int)(RastPort - ArenaAddress), x, y);
                expected = before with { Arena = arena };
            }
            else
            {
                // Only snapshot indexing is physical. A1 remains the full
                // logical pointer when the accurate M68000 executes Move.
                var high = before.High.ToArray();
                PublishMove(high, (int)(pointer & 0xFFu), x, y);
                expected = before with { High = high };
            }
        }

        var result = fixture.Invoke(pointer, x, y);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "native admission and local D0 convention", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            // Move is a VOID API. Zero is this native body's existing local
            // handled result, not an additional documented return value.
            Assert.Equal(success ? 0u : x, result.Data0);
            if (!success) Assert.Equal(y, result.Data1);
        });
        Check(failures, "actual public caller return and input/base pointers", () =>
        {
            Assert.Equal(Fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "exact cursor/phase/flags publication or unchanged declined memory", () =>
        {
            Assert.Equal(expected.Arena, after.Arena);
            Assert.Equal(expected.High, after.High);
            Assert.Equal(expected.Low, after.Low);
        });
        Check(failures, "public library/vector bytes, caller and stack guards", () =>
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "currently valid saved registers; D4 debt explicitly separate", () =>
            Assert.True(result.RegisterDifferences.Length == 0, string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static void PublishMove(byte[] bytes, int offset, uint x, uint y)
    {
        // Independent classic RP field offsets. Preserve all other bytes,
        // including unrelated Flags bits and the caller's LinePtrn word.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 0x24, 2), unchecked((ushort)x));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 0x26, 2), unchecked((ushort)y));
        bytes[offset + 0x1E] = 15;
        var flags = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 0x20, 2));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 0x20, 2), (ushort)(flags | 1));
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
        byte[] GraphicsImage, byte[] Caller, byte[] StackGuards);
    private sealed record CallResult(uint Data0, uint Data1, uint Address1, uint Address6,
        uint ProgramCounter, uint StackPointer, bool UsedFallback, string[] RegisterDifferences);

    private sealed class Fixture : IDisposable
    {
        private const uint FixedCodeAddress = 0x0040_0000;
        private const uint FixedGraphicsBase = 0x0070_0000;
        private const uint FixedResidentAddress = 0x0072_0000;
        private const uint HunkSegmentAddress = 0x0080_0000;
        private const uint HunkResidentAddress = 0x00A8_0000;
        private const uint CallerAddress = 0x00B8_0000;
        internal const uint ReturnAddress = CallerAddress + 4;
        private const uint StackAddress = 0x00C7_0000;
        internal const uint StackPointer = StackAddress + 0x300;
        private const uint HighPhysicalAddress = 0x00FF_FF00;
        private readonly AmigaBus _bus = new();
        private readonly IM68kCore _cpu;
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _vectorSlot;

        internal Fixture(bool relocated)
        {
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/public JSR d16(A6)";
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
            }
            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.Move];
            _vectorSlot = checked((uint)((long)GraphicsBase + (int)GraphicsLvo.Move));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));

            // Exactly 100 writable bytes at the ordinary RP, followed by an
            // 80-byte read-only caller tail. This is a mapping control, not
            // a claim that native Move independently probes writability.
            _bus.MapReadOnlyMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, 32).ToArray());
            _bus.MapWritableMemory(RastPort, Enumerable.Repeat((byte)0xD7, ClassicRastPortBytes).ToArray());
            _bus.MapReadOnlyMemory(RastPort + ClassicRastPortBytes,
                Enumerable.Repeat((byte)0xE9, 80 + 32).ToArray());
            Assert.True(_bus.IsWritableMemoryRange(RastPort, ClassicRastPortBytes));
            Assert.False(_bus.IsWritableMemoryRange(RastPort, ClassicRastPortBytes + 1));
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            _bus.WriteWord(CallerAddress, 0x4EAE);
            _bus.WriteWord(CallerAddress + 2, unchecked((ushort)(int)GraphicsLvo.Move));
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }

        internal void SeedCursor(uint pointer, ushort flags)
        {
            // All even cases, including the overflowing-envelope negative
            // control, have these four individual fields mapped. No rejected
            // case can pass just because its intended stores would fault.
            if (pointer == 0 || (pointer & 1u) != 0) return;
            _bus.WriteWord(pointer + 0x20, flags);
            _bus.WriteWord(pointer + 0x24, 0x1357);
            _bus.WriteWord(pointer + 0x26, 0x2468);
            _bus.WriteByte(pointer + 0x1E, 0x62, 0);
        }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2E0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint pointer, uint x, uint y)
        {
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            for (var index = 0; index < 8; index++)
                _cpu.State.D[index] = 0xD000_0100u + (uint)index;
            for (var index = 0; index < 6; index++)
                _cpu.State.A[index] = 0xA000_0201u + (uint)(index * 0x10);
            _cpu.State.D[0] = x;
            _cpu.State.D[1] = y;
            _cpu.State.A[1] = pointer;
            _cpu.State.A[6] = GraphicsBase;
            var expectedData = _cpu.State.D.ToArray();
            var expectedAddresses = _cpu.State.A.ToArray();
            _cpu.ExecuteInstruction();
            Assert.Equal(_vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            _cpu.ExecuteInstruction(); // The actual physical vector's JMP.
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            for (var instruction = 0; instruction < 256; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address ${pc:X8}; no host graphics/Exec gateway is installed.");
                // These are Move's three admission guards. Observe their
                // actual decisions because the linker may clone the fallback
                // RTS near the body instead of visiting its global offset.
                var isAdmissionBranch = _bus.ReadWord(pc) is 0x6700 or 0x6600 or 0x6200;
                _cpu.ExecuteInstruction();
                usedFallback |= isAdmissionBranch && _cpu.State.ProgramCounter != pc + 4u;
                if (_cpu.State.ProgramCounter != ReturnAddress) continue;

                var differences = new List<string>();
                // D4 currently receives discarded coordinate saves and is
                // not framed on declines either. Neither require that defect
                // nor repair it here; a later D4-only frame can pass this test.
                foreach (var index in new[] { 2, 3, 5, 6, 7 })
                    if (_cpu.State.D[index] != expectedData[index])
                        differences.Add($"D{index}: expected {expectedData[index]:X8}, actual {_cpu.State.D[index]:X8}");
                for (var index = 2; index <= 6; index++)
                    if (_cpu.State.A[index] != expectedAddresses[index])
                        differences.Add($"A{index}: expected {expectedAddresses[index]:X8}, actual {_cpu.State.A[index]:X8}");
                return new CallResult(_cpu.State.D[0], _cpu.State.D[1], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
