using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsReadPixelPublicAbiTests
{
    // Queue after the paired native pixel 100-byte extent unit. This fixture
    // changes neither admission policy nor the portable 180-byte reader.
    private const int ReadPixelLvo = -318;
    private const int ReadPixelFunctionOrdinal = 52;
    private const int RastPortBytes = 100;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x300;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint BitMapArena = 0x00D1_0000;
    private const uint BitMap = BitMapArena + 0x20;
    private const int BitMapArenaSize = 0x100;
    private const uint PlaneArena = 0x00D2_0000;
    private const int PlaneSlotBytes = 0x200;
    private const int PlaneDataOffset = 0x20;
    private const int PlaneBytes = 256; // Literal stride 8, rows 32: a 64x32 plane.
    private const int PlaneArenaSize = 8 * PlaneSlotBytes;
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
            "one-plane-clear-msb", "eight-plane-mixed-lsb", "rp-null", "rp-odd", "rp-wrap",
            "bitmap-null", "bitmap-flags", "y-bound", "x-byte-bound",
            "second-plane-null", "second-plane-odd", "second-plane-carry"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void ReadPixelPublicEntriesPreserveCalleeSavedRegistersAcrossEveryExit(
        bool relocated, bool autoInitEntry, string scenario)
    {
        var success = scenario is "one-plane-clear-msb" or "eight-plane-mixed-lsb";
        var latePlaneFailure = scenario is "second-plane-null" or "second-plane-odd" or "second-plane-carry";
        var pointer = scenario switch
        {
            "rp-null" => 0u,
            "rp-odd" => RastPort + 1u,
            "rp-wrap" => 0xFFFF_FF9Eu,
            _ => RastPort
        };
        var x = scenario switch
        {
            "eight-plane-mixed-lsb" => 0xCAFE_0007u,
            "x-byte-bound" => 0xCAFE_0040u,
            _ => 0xCAFE_0000u
        };
        var y = scenario == "y-bound" ? 0xBEEF_0020u : 0xBEEF_0001u;
        var depth = scenario switch
        {
            "one-plane-clear-msb" => 1,
            "eight-plane-mixed-lsb" => 8,
            _ => 2
        };
        var expectedColor = scenario == "eight-plane-mixed-lsb" ? 0xA5u : 0u;
        using var fixture = new Fixture(relocated, autoInitEntry, scenario, pointer, depth);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(pointer, x, y);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        // Keep the pixel/return/memory controls independent of the expected
        // saved-register failures; do not accept a numerically correct D0 alone.
        Check(failures, "native pixel result and honest terminal fallback", () =>
        {
            Assert.Equal(success ? expectedColor : x, result.Data0);
            Assert.Equal(!success, result.UsedFallback);
            Assert.Equal(1, result.NativeReturnCount);
            // The native provider handoff preserves the full caller words;
            // do not substitute the eventual documented out-of-bounds -1.
            if (!success) Assert.Equal(y, result.Data1);
        });
        Check(failures, "actual public caller PC/SP and full logical A1/A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "plane-loop execution and late partial-color rollback", () =>
        {
            if (success)
            {
                Assert.Equal(depth, result.PlaneReads.Length);
                for (var plane = 0; plane < depth; plane++)
                {
                    Assert.Equal(BitMap + 8u + (uint)(plane * 4), result.PlaneReads[plane].TableAddress);
                    Assert.Equal(PlaneAddress(plane), result.PlaneReads[plane].Pointer);
                }
            }
            else if (latePlaneFailure)
            {
                Assert.Equal(new[]
                {
                    new PlaneRead(BitMap + 8, PlaneAddress(0), 0),
                    new PlaneRead(BitMap + 12, SecondPlanePointer(scenario), 1)
                }, result.PlaneReads);
                // At the second table read D0 was already color1; the final
                // x/y assertions above therefore require genuine restoration.
            }
            else
                Assert.Empty(result.PlaneReads);
        });
        Check(failures, "all RastPort/bitmap/plane bytes and logical-wrap aliases unchanged", () =>
        {
            Assert.Equal(before.Arena, after.Arena);
            Assert.Equal(before.BitMap, after.BitMap);
            Assert.Equal(before.Planes, after.Planes);
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

    private static uint PlaneAddress(int plane)
        => PlaneArena + (uint)(plane * PlaneSlotBytes + PlaneDataOffset);

    private static uint SecondPlanePointer(string scenario) => scenario switch
    {
        "second-plane-null" => 0u,
        "second-plane-odd" => PlaneAddress(1) + 1u,
        "second-plane-carry" => 0xFFFF_FFFEu,
        _ => PlaneAddress(1)
    };

    private static byte[] CreateRastPort(string scenario, int depth)
    {
        var port = Enumerable.Repeat((byte)0xD7, RastPortBytes).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(0, 4), 0); // Layer=NULL.
        BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(4, 4), scenario == "bitmap-null" ? 0u : BitMap);
        port[0x18] = (byte)((1 << depth) - 1);
        port[0x19] = 1;
        port[0x1A] = 0;
        port[0x1C] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(port.AsSpan(0x20, 2), 0);
        return port;
    }

    private static byte[] CreateBitMap(string scenario, int depth)
    {
        var bitmap = new byte[40];
        BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(0, 2), 8);
        BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(2, 2), 32);
        // Preserve current native flags=0 admission; no provider/RTG format
        // or allocation-produced header is normalized to fit this fixture.
        bitmap[4] = scenario == "bitmap-flags" ? (byte)8 : (byte)0;
        bitmap[5] = (byte)depth;
        for (var plane = 0; plane < depth; plane++)
            BinaryPrimitives.WriteUInt32BigEndian(bitmap.AsSpan(8 + plane * 4, 4),
                plane == 1 ? SecondPlanePointer(scenario) : PlaneAddress(plane));
        return bitmap;
    }

    private static byte[] CreatePlanes(string scenario)
    {
        var bytes = Enumerable.Repeat((byte)0x6D, PlaneArenaSize).ToArray();
        var targetColor = scenario switch
        {
            "one-plane-clear-msb" => 0u,
            "eight-plane-mixed-lsb" => 0xA5u,
            _ => 1u // In particular, every late failure first accumulates color1.
        };
        var bit = scenario == "eight-plane-mixed-lsb" ? (byte)0x01 : (byte)0x80;
        for (var plane = 0; plane < 8; plane++)
        {
            var payload = bytes.AsSpan(plane * PlaneSlotBytes + PlaneDataOffset, PlaneBytes);
            var target = (targetColor & (1u << plane)) != 0 ? bit : unchecked((byte)~bit);
            // Literal x=0 or7,y=1,stride8 selects byte8. Other rows/bytes
            // and other bit positions produce the opposite plane value.
            payload.Fill((byte)(target ^ 0xFF));
            payload[8] = target;
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
    private sealed record PlaneRead(uint TableAddress, uint Pointer, uint AccumulatedColor);
    private sealed record CallResult(uint Data0, uint Data1, uint Address1, uint Address6,
        uint ProgramCounter, uint StackPointer, bool UsedFallback, int NativeReturnCount,
        PlaneRead[] PlaneReads, string[] RegisterDifferences);

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

        internal Fixture(bool relocated, bool autoInitEntry, string scenario, uint pointer, int depth)
        {
            Assert.Equal(ReadPixelLvo, (int)GraphicsLvo.ReadPixel);
            Assert.Equal(ReadPixelFunctionOrdinal, (-ReadPixelLvo / 6) - 1);
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
            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.ReadPixel];
            _vectorSlot = checked((uint)((long)GraphicsBase + ReadPixelLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + ReadPixelFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            _bus.MapWritableMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray());
            _bus.MapWritableMemory(BitMapArena, Enumerable.Repeat((byte)0xC5, BitMapArenaSize).ToArray());
            _bus.MapWritableMemory(PlaneArena, CreatePlanes(scenario));
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            var port = CreateRastPort(scenario, depth);
            var bitmap = CreateBitMap(scenario, depth);
            SeedBytes(RastPort, port);
            SeedBytes(BitMap, bitmap);
            if (pointer != RastPort)
            {
                // NULL and odd RPs have coherent backing, not secondary bad
                // Layer/BitMap values. The wrapping RP receives only a safe
                // 34-byte prefix, including every field this reader consumes.
                // No Exec/SysBase ownership contract is involved here.
                var bytes = pointer == 0xFFFF_FF9E ? 34 : RastPortBytes;
                Assert.True((ulong)pointer + (uint)bytes - 1 <= uint.MaxValue);
                // Raw Bus writes do not normalize CPU addresses. Keep the
                // logical pointer for A1, but seed and verify its physical alias.
                var physicalPointer = pointer & 0x00FF_FFFFu;
                SeedBytes(physicalPointer, port.AsSpan(0, bytes));
                Assert.Equal(port.AsSpan(0, bytes).ToArray(), ReadBytes(physicalPointer, bytes));
            }
            if (scenario == "bitmap-null")
                SeedBytes(0, bitmap); // A removed NULL guard cannot hide behind invalid low backing.
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2); A1 remains the RP argument.
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE);
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)ReadPixelLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }
        private void SeedBytes(uint address, ReadOnlySpan<byte> bytes)
        {
            for (var index = 0; index < bytes.Length; index++)
                _bus.WriteByte(address + (uint)index, bytes[index], 0);
        }
        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();
        internal MemorySnapshot CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(BitMapArena, BitMapArenaSize), ReadBytes(PlaneArena, PlaneArenaSize),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(_residentAddress, _residentByteCount), ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x2C0).Concat(ReadBytes(StackPointer, 0x300)).ToArray());

        internal CallResult Invoke(uint pointer, uint x, uint y)
        {
            Assert.True(x > 0xFF, "The original X must be distinct from every successful pixel color.");
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry) expectedAddresses[0] = _functionEntry;
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
            if (!_autoInitEntry) _cpu.ExecuteInstruction(); // Actual physical negative-vector JMP.
            Assert.Equal(_entry, _cpu.State.ProgramCounter);

            var usedFallback = false;
            var nativeReturnCount = 0;
            var planeReads = new List<PlaneRead>();
            for (var instruction = 0; instruction < 10_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length,
                    $"Unexpected execution address 0x{pc:X8}; no host graphics/Exec gateway is installed.");
                var opcode = _bus.ReadWord(pc);
                if (opcode == 0x4E75)
                {
                    nativeReturnCount++;
                    // Bit-selection/loop branches are not fallback signals.
                    // Recognize the terminal full-X result, including a cloned
                    // local RTS, without requiring the global fallback PC.
                    usedFallback |= _cpu.State.D[0] == x;
                }
                var tableAddress = _cpu.State.A[2];
                var accumulatedColor = _cpu.State.D[0];
                _cpu.ExecuteInstruction();
                // Existing MOVE.L (A2)+,D4 is the bounded reader's plane-table
                // load. Observe it without altering guest state: late failures
                // must reach plane1 after plane0 has already contributed color.
                if (opcode == 0x281A)
                    planeReads.Add(new PlaneRead(tableAddress, _cpu.State.D[4], accumulatedColor));
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
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, nativeReturnCount,
                    planeReads.ToArray(), differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
