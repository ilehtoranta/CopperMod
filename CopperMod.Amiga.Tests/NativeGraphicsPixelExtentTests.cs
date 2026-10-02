using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsPixelExtentTests
{
    // Native RP logical-extent coverage only. The shared portable bitmap
    // decoder still has its separate 180-byte admission; it is not an oracle
    // for these high logical pointers. Pixel public-frame debt is also separate.
    private const int ReadPixelLvo = -318;
    private const int WritePixelLvo = -324;
    private const int ClassicRastPortBytes = 100;
    private const uint CapturedX = 0xCAFE_0007;
    private const uint CapturedY = 0xBEEF_0001;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 32 + 180 + 32;
    private const uint RastPort = ArenaAddress + 32;
    private const uint BitMapArena = 0x00D1_0000;
    private const uint BitMap = BitMapArena + 32;
    private const int BitMapArenaSize = 32 + 40 + 32;
    private const uint PlaneArena = 0x00D2_0000;
    private const int PlaneSlotBytes = 64;
    private const int PlaneDataOffset = 16;
    private const int PlaneArenaSize = PlaneSlotBytes * 8;
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
        foreach (var write in new[] { false, true })
        foreach (var scenario in new[]
        {
            "ordinary-hundred", "old-exact-end", "first-new-even", "exact-hundred-end",
            "first-even-wrap", "null", "ordinary-odd", "high-odd"
        })
            yield return new object[] { relocated, write, scenario };
    }

    [Theory]
    [MemberData(nameof(ExtentCases))]
    public void PublicPixelCallsAdmitTheClassicHundredByteLogicalExtent(
        bool relocated, bool write, string scenario)
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
        var depth = scenario switch
        {
            "old-exact-end" => 1,
            "exact-hundred-end" => 8,
            _ => 2
        };
        using var fixture = new Fixture(relocated, write, pointer, depth);
        var before = fixture.CaptureMemory();
        var expectedPlanes = before.Planes.ToArray();
        if (success && write)
        {
            // Literal independent pixel oracle: x=7,y=1 at stride 2 selects
            // the LSB of byte 2. APen=$AA clears each even plane's A5 to A4
            // and sets each odd plane's 5A to 5B. No other byte can change.
            for (var plane = 0; plane < depth; plane++)
                expectedPlanes[plane * PlaneSlotBytes + PlaneDataOffset + 2] =
                    (plane & 1) == 0 ? (byte)0xA4 : (byte)0x5B;
        }
        var expectedResult = success ? (write ? 0u : depth == 8 ? 0x55u : 1u) : CapturedX;

        var result = fixture.Invoke(pointer);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        Check(failures, "native admission versus honest fallback", () =>
        {
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "pixel result and complete declined coordinate arguments", () =>
        {
            Assert.Equal(expectedResult, result.Data0);
            // Native declines preserve the caller's complete registers for
            // the provider, rather than inventing the eventual clipped -1.
            if (!success) Assert.Equal(CapturedY, result.Data1);
        });
        Check(failures, "actual public caller PC/SP and full logical A1/A6", () =>
        {
            Assert.Equal(Fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "RastPort, bitmap, guards and logical-wrap aliases never change", () =>
        {
            Assert.Equal(before.Arena, after.Arena);
            Assert.Equal(before.BitMap, after.BitMap);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
        });
        Check(failures, "only the declared destination bits may change", () =>
            Assert.Equal(expectedPlanes, after.Planes));
        Check(failures, "library/vector/resident, caller and stack guards", () =>
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "currently valid saved-register controls, not a public-frame repair", () =>
            Assert.True(result.RegisterDifferences.Length == 0, string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}/depth{depth}:\n" + string.Join("\n", failures));
    }

    private static uint PlaneAddress(int plane)
        => PlaneArena + (uint)(plane * PlaneSlotBytes + PlaneDataOffset);

    private static byte[] CreateRastPort(int depth)
    {
        var port = Enumerable.Repeat((byte)0xD7, ClassicRastPortBytes).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(0, 4), 0); // Layer=NULL.
        BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(4, 4), BitMap);
        port[0x18] = (byte)((1 << depth) - 1); // Every declared plane selected.
        port[0x19] = 0xAA; // APen; both set and clear paths are observable.
        port[0x1A] = 0x55;
        port[0x1C] = 0; // JAM1.
        BinaryPrimitives.WriteUInt16BigEndian(port.AsSpan(0x20, 2), 0); // No NO_PENS sidecar.
        return port;
    }

    private static byte[] CreateBitMap(int depth)
    {
        var bitmap = new byte[40];
        BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(0, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(2, 2), 2);
        bitmap[4] = 0; // Ordinary non-interleaved planar bitmap.
        bitmap[5] = (byte)depth;
        for (var plane = 0; plane < depth; plane++)
            BinaryPrimitives.WriteUInt32BigEndian(bitmap.AsSpan(8 + plane * 4, 4), PlaneAddress(plane));
        return bitmap;
    }

    private static byte[] CreatePlanes()
    {
        var bytes = Enumerable.Repeat((byte)0x6D, PlaneArenaSize).ToArray();
        for (var plane = 0; plane < 8; plane++)
        {
            var payload = bytes.AsSpan(plane * PlaneSlotBytes + PlaneDataOffset, 4);
            // A wrong byte/row must return a different pen, not coincidentally
            // the target value. Only byte 2 has the A5/5A oracle pattern.
            payload.Fill((plane & 1) == 0 ? (byte)0x5A : (byte)0xA5);
            payload[2] = (plane & 1) == 0 ? (byte)0xA5 : (byte)0x5A;
        }
        return bytes;
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
    private sealed record MemorySnapshot(byte[] Arena, byte[] High, byte[] Low, byte[] BitMap,
        byte[] Planes, byte[] GraphicsImage, byte[] Resident, byte[] Caller, byte[] StackGuards);
    private sealed record CallResult(uint Data0, uint Data1, uint Address1, uint Address6,
        uint ProgramCounter, uint StackPointer, bool UsedFallback, int NativeReturnCount, string[] RegisterDifferences);

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
        private readonly bool _write;
        private readonly uint _nativeCodeAddress;
        private readonly uint _entry;
        private readonly uint _vectorSlot;
        private readonly uint _residentAddress;
        private readonly int _residentByteCount;

        internal Fixture(bool relocated, bool write, uint pointer, int depth)
        {
            _write = write;
            var vector = write ? GraphicsLvo.WritePixel : GraphicsLvo.ReadPixel;
            var lvo = write ? WritePixelLvo : ReadPixelLvo;
            Assert.Equal(lvo, (int)vector);
            Route = (relocated ? "relocated HUNK" : "fixed image") +
                $"/{vector}/public JSR d16(A6)";
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
            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[vector];
            _vectorSlot = checked((uint)((long)GraphicsBase + lvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));

            var port = CreateRastPort(depth);
            _bus.MapReadOnlyMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, 32).ToArray());
            _bus.MapWritableMemory(RastPort, port);
            _bus.MapReadOnlyMemory(RastPort + ClassicRastPortBytes,
                Enumerable.Repeat((byte)0xE9, 80 + 32).ToArray());
            Assert.True(_bus.IsWritableMemoryRange(RastPort, ClassicRastPortBytes));
            Assert.False(_bus.IsWritableMemoryRange(RastPort, ClassicRastPortBytes + 1));
            _bus.MapReadOnlyMemory(BitMapArena, Enumerable.Repeat((byte)0xC5, 32).ToArray());
            _bus.MapWritableMemory(BitMap, CreateBitMap(depth));
            _bus.MapReadOnlyMemory(BitMap + 40, Enumerable.Repeat((byte)0xC6, 32).ToArray());
            _bus.MapWritableMemory(PlaneArena, CreatePlanes());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);

            if (pointer != RastPort && (pointer & 1u) == 0)
            {
                // For the wrapping negative, seed only the first 34 bytes:
                // every individually consumed field is coherent and mapped,
                // but the full 100-byte logical envelope is not admissible.
                // NULL likewise has coherent low backing, so a missing NULL
                // guard cannot hide behind a later malformed-bitmap decline.
                // No Exec/SysBase ownership contract is involved in this test.
                var bytes = pointer == 0xFFFF_FF9E ? 34 : ClassicRastPortBytes;
                Assert.True((ulong)pointer + (uint)bytes - 1 <= uint.MaxValue);
                // Bus seed calls take physical addresses. Only the backing
                // uses the 68000 alias; the public A1 argument stays full32.
                var physicalPointer = pointer & 0x00FF_FFFFu;
                for (var offset = 0; offset < bytes; offset++)
                    _bus.WriteByte(physicalPointer + (uint)offset, port[offset], 0);
                Assert.Equal(port.AsSpan(0, bytes).ToArray(), ReadBytes(physicalPointer, bytes));
            }
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            _bus.WriteWord(CallerAddress, 0x4EAE);
            _bus.WriteWord(CallerAddress + 2, unchecked((ushort)lvo));
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }

        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();

        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(BitMapArena, BitMapArenaSize), ReadBytes(PlaneArena, PlaneArenaSize),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount), ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint pointer)
        {
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            for (var index = 0; index < 8; index++)
                _cpu.State.D[index] = 0xD000_0100u + (uint)index;
            for (var index = 0; index < 6; index++)
                _cpu.State.A[index] = 0xA000_0201u + (uint)(index * 0x10);
            _cpu.State.D[0] = CapturedX;
            _cpu.State.D[1] = CapturedY;
            _cpu.State.A[1] = pointer; // Full 32-bit logical argument, never masked.
            _cpu.State.A[6] = GraphicsBase;
            var expectedData = _cpu.State.D.ToArray();
            var expectedAddresses = _cpu.State.A.ToArray();
            _cpu.ExecuteInstruction();
            Assert.Equal(_vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            _cpu.ExecuteInstruction(); // Actual vector JMP, including WritePixel's thin dispatch entry.
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            var nativeReturnCount = 0;
            for (var instruction = 0; instruction < 10_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address ${pc:X8}; no host graphics/Exec gateway is installed.");
                usedFallback |= pc == _nativeCodeAddress + (uint)Image.Value.Fallback;
                if (_bus.ReadWord(pc) == 0x4E75)
                {
                    nativeReturnCount++;
                    // Successful ReadPixel is 0..255; WritePixel is zero.
                    // CapturedX therefore identifies an honest local/shared
                    // fallback RTS without mistaking pixel-loop Bccs for it.
                    usedFallback |= _cpu.State.D[0] == CapturedX;
                }
                _cpu.ExecuteInstruction();
                if (_cpu.State.ProgramCounter != ReturnAddress) continue;

                var differences = new List<string>();
                // Existing unframed scratch is deliberately NOT normalized
                // or asserted: Read D2/D3/D4/D5/D7/A2, Write D2-D7/A2.
                // Read D6 and both vectors' A3-A6 are valid controls. Later
                // public frames may preserve the excluded registers too.
                if (!_write && _cpu.State.D[6] != expectedData[6])
                    differences.Add($"D6 expected {expectedData[6]:X8}, actual {_cpu.State.D[6]:X8}");
                for (var index = 3; index <= 6; index++)
                    if (_cpu.State.A[index] != expectedAddresses[index])
                        differences.Add($"A{index} expected {expectedAddresses[index]:X8}, actual {_cpu.State.A[index]:X8}");
                return new CallResult(_cpu.State.D[0], _cpu.State.D[1], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, nativeReturnCount, differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
