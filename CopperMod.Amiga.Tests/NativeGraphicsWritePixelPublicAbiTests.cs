using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsWritePixelPublicAbiTests
{
    // Activate after the paired pixel extent unit and the separate ReadPixel
    // public-frame unit. This fixture tests WritePixel's existing native
    // outcomes and callee-saved registers, not portable/provider dispatch,
    // complete mapped-memory admission, clipping, or additional draw modes.
    private const int WritePixelLvo = -324;
    private const int WritePixelFunctionOrdinal = 53;
    private const int RastPortBytes = 100;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 32 + RastPortBytes + 32;
    private const uint RastPort = ArenaAddress + 32;
    private const uint BitMapArena = 0x00D1_0000;
    private const uint BitMap = BitMapArena + 32;
    private const int BitMapArenaSize = 32 + 40 + 32;
    private const uint PlaneArena = 0x00D2_0000;
    private const int PlaneSlotBytes = 64;
    private const int PlaneDataOffset = 16;
    private const int PlaneArenaSize = PlaneSlotBytes * 2;
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
            "apen", "inverse-jam2-bpen", "complement", "zero-mask",
            "inverse-jam1", "inverse-complement", "rp-null", "rp-odd",
            "rp-wrap", "bitmap-null", "no-pens", "y-out-of-range",
            "x-byte-out-of-range", "second-plane-null", "second-plane-odd",
            "second-plane-address-carry"
        })
            yield return new object[] { relocated, autoInitEntry, scenario };
    }

    [Theory]
    [MemberData(nameof(PublicAbiCases))]
    public void WritePixelPublicEntriesPreserveCalleeSavedRegistersAcrossEveryExit(
        bool relocated, bool autoInitEntry, string scenario)
    {
        var handled = scenario is "apen" or "inverse-jam2-bpen" or "complement" or
            "zero-mask" or "inverse-jam1" or "inverse-complement";
        var pointer = scenario switch
        {
            "rp-null" => 0u,
            "rp-odd" => RastPort + 1u,
            "rp-wrap" => 0xFFFF_FF9Eu,
            _ => RastPort
        };
        var x = scenario == "x-byte-out-of-range" ? 0xCAFE_0010u : 0xCAFE_0007u;
        var y = scenario == "y-out-of-range" ? 0xBEEF_0002u : 0xBEEF_0001u;
        using var fixture = new Fixture(relocated, autoInitEntry, scenario, pointer);
        var before = fixture.CaptureMemory();
        var expectedPlanes = before.Planes.ToArray();
        // Independent literal pixel oracle: stride=2, x=7, y=1 selects bit 0
        // of byte 2. Initial bytes A5/5A, APen=0 and BPen=3 make all three
        // mutating paths distinct; each no-op would differ from APen writes.
        switch (scenario)
        {
            case "apen":
                expectedPlanes[PlaneDataOffset + 2] = 0xA4;
                break;
            case "inverse-jam2-bpen":
                expectedPlanes[PlaneSlotBytes + PlaneDataOffset + 2] = 0x5B;
                break;
            case "complement":
                expectedPlanes[PlaneDataOffset + 2] = 0xA4;
                expectedPlanes[PlaneSlotBytes + PlaneDataOffset + 2] = 0x5B;
                break;
        }

        var result = fixture.Invoke(pointer, x, y);
        var after = fixture.CaptureMemory();
        var failures = new List<string>();
        // Keep every existing outcome/memory/return control visible beside
        // the expected saved-register red; never normalize guest scratch.
        Check(failures, "native handled/decline result and complete coordinate arguments", () =>
        {
            Assert.Equal(!handled, result.UsedFallback);
            // This native arm returns zero when handled. A decline leaves
            // the eventual provider/clipped result unresolved and restores
            // both full input registers, including their hostile high words.
            Assert.Equal(handled ? 0u : x, result.Data0);
            if (!handled) Assert.Equal(y, result.Data1);
            Assert.Equal(1, result.NativeReturnCount);
        });
        Check(failures, "actual public caller PC/SP and original logical A1/A6", () =>
        {
            Assert.Equal(fixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(Fixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "RastPort, bitmap, adjacent guards and high/low aliases unchanged", () =>
        {
            Assert.Equal(before.Arena, after.Arena);
            Assert.Equal(before.BitMap, after.BitMap);
            Assert.Equal(before.High, after.High);
            Assert.Equal(before.Low, after.Low);
        });
        Check(failures, "only the independent expected destination bits may change", () =>
            Assert.Equal(expectedPlanes, after.Planes));
        if (scenario is "second-plane-null" or "second-plane-odd" or "second-plane-address-carry")
        {
            Check(failures, "late second-plane decline is atomic for the valid first plane", () =>
            {
                Assert.Equal((byte)0xA5, before.Planes[PlaneDataOffset + 2]);
                Assert.Equal(before.Planes.AsSpan(0, PlaneSlotBytes).ToArray(),
                    after.Planes.AsSpan(0, PlaneSlotBytes).ToArray());
            });
        }
        Check(failures, "library/vector/resident, caller and stack guards unchanged", () =>
        {
            Assert.Equal(before.GraphicsImage, after.GraphicsImage);
            Assert.Equal(before.Resident, after.Resident);
            Assert.Equal(before.Caller, after.Caller);
            Assert.Equal(before.StackGuards, after.StackGuards);
        });
        Check(failures, "all public callee-saved registers D2-D7/A2-A6", () =>
            Assert.True(result.RegisterDifferences.Length == 0, string.Join("; ", result.RegisterDifferences)));
        Assert.True(failures.Count == 0, $"{fixture.Route}/{scenario}:\n" + string.Join("\n", failures));
    }

    private static uint PlaneAddress(int plane)
        => PlaneArena + (uint)(plane * PlaneSlotBytes + PlaneDataOffset);

    private static byte[] CreateRastPort(string scenario)
    {
        var port = Enumerable.Repeat((byte)0xD7, RastPortBytes).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(0, 4), 0); // Layer=NULL.
        BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(4, 4), scenario == "bitmap-null" ? 0u : BitMap);
        port[0x18] = scenario == "zero-mask" ? (byte)0 : (byte)3;
        port[0x19] = 0; // APen=0 clears both selected plane bits.
        port[0x1A] = 3; // BPen=3 sets both selected plane bits.
        port[0x1C] = scenario switch
        {
            "inverse-jam2-bpen" => 5, // INVERSVID | JAM2.
            "complement" => 2,
            "inverse-jam1" => 4,
            "inverse-complement" => 6,
            _ => 0 // JAM1.
        };
        BinaryPrimitives.WriteUInt16BigEndian(port.AsSpan(0x20, 2),
            scenario == "no-pens" ? (ushort)0x4000 : (ushort)0);
        return port;
    }

    private static byte[] CreateBitMap(string scenario)
    {
        var bitmap = new byte[40];
        BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(0, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(2, 2), 2);
        bitmap[4] = 0; // Ordinary, non-interleaved planar bitmap.
        bitmap[5] = 2;
        // The validated zero-mask no-op must not depend on valid plane links.
        var first = scenario == "zero-mask" ? 0u : PlaneAddress(0);
        var second = scenario switch
        {
            "zero-mask" or "second-plane-null" => 0u,
            "second-plane-odd" => PlaneAddress(1) + 1u,
            "second-plane-address-carry" => 0xFFFF_FFFEu,
            _ => PlaneAddress(1)
        };
        BinaryPrimitives.WriteUInt32BigEndian(bitmap.AsSpan(8, 4), first);
        BinaryPrimitives.WriteUInt32BigEndian(bitmap.AsSpan(12, 4), second);
        return bitmap;
    }

    private static byte[] CreatePlanes()
    {
        var bytes = Enumerable.Repeat((byte)0x6D, PlaneArenaSize).ToArray();
        for (var plane = 0; plane < 2; plane++)
        {
            var payload = bytes.AsSpan(plane * PlaneSlotBytes + PlaneDataOffset, 4);
            payload.Fill(plane == 0 ? (byte)0x5A : (byte)0xA5);
            payload[2] = plane == 0 ? (byte)0xA5 : (byte)0x5A;
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

        internal Fixture(bool relocated, bool autoInitEntry, string scenario, uint pointer)
        {
            Assert.Equal(WritePixelLvo, (int)GraphicsLvo.WritePixel);
            Assert.Equal(WritePixelFunctionOrdinal, (-WritePixelLvo / 6) - 1);
            _autoInitEntry = autoInitEntry;
            Route = (relocated ? "relocated HUNK" : "fixed image") + "/WritePixel/" +
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
            // Both public routes name the thin dispatch, which must execute
            // its own BRA into the multi-plane body without a host gateway.
            _entry = _nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.WritePixel];
            _vectorSlot = checked((uint)((long)GraphicsBase + WritePixelLvo));
            Assert.Equal((ushort)0x4EF9, _bus.ReadWord(_vectorSlot));
            Assert.Equal(_entry, _bus.ReadLong(_vectorSlot + 2));
            Assert.Equal((ushort)0x4AFC, _bus.ReadWord(_residentAddress));
            var functionArray = _bus.ReadLong(_bus.ReadLong(_residentAddress + 0x16) + 4);
            _functionEntry = _bus.ReadLong(functionArray + WritePixelFunctionOrdinal * 4u);
            Assert.Equal(_entry, _functionEntry);

            var port = CreateRastPort(scenario);
            _bus.MapReadOnlyMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, 32).ToArray());
            _bus.MapWritableMemory(RastPort, port);
            _bus.MapReadOnlyMemory(RastPort + RastPortBytes, Enumerable.Repeat((byte)0xE9, 32).ToArray());
            Assert.True(_bus.IsWritableMemoryRange(RastPort, RastPortBytes));
            Assert.False(_bus.IsWritableMemoryRange(RastPort, RastPortBytes + 1));
            _bus.MapReadOnlyMemory(BitMapArena, Enumerable.Repeat((byte)0xC5, 32).ToArray());
            _bus.MapWritableMemory(BitMap, CreateBitMap(scenario));
            _bus.MapReadOnlyMemory(BitMap + 40, Enumerable.Repeat((byte)0xC6, 32).ToArray());
            _bus.MapWritableMemory(PlaneArena, CreatePlanes());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            if (pointer == 0 || pointer == 0xFFFF_FF9E)
            {
                // Coherent fields prevent a missing RP guard from passing by
                // accident at a later bitmap/mode guard. The wrap case needs
                // only its 34 individually read bytes; the complete 100-byte
                // logical envelope is invalid. No Exec/SysBase use is involved.
                var byteCount = pointer == 0 ? RastPortBytes : 34;
                Assert.True((ulong)pointer + (uint)byteCount - 1 <= uint.MaxValue);
                // Raw Bus writes do not normalize CPU addresses. Keep the
                // logical pointer for A1, but seed and verify its physical alias.
                var physicalPointer = pointer & 0x00FF_FFFFu;
                for (var offset = 0; offset < byteCount; offset++)
                    _bus.WriteByte(physicalPointer + (uint)offset, port[offset], 0);
                Assert.Equal(port.AsSpan(0, byteCount).ToArray(), ReadBytes(physicalPointer, byteCount));
            }
            if (scenario == "bitmap-null")
            {
                // Coherent low backing keeps the NULL guard independently observable.
                var lowBitmap = CreateBitMap(scenario);
                for (var offset = 0; offset < lowBitmap.Length; offset++)
                    _bus.WriteByte((uint)offset, lowBitmap[offset], 0);
            }
            _bus.MapWritableMemory(CallerAddress, Enumerable.Repeat((byte)0x91, 8).ToArray());
            ReturnAddress = CallerAddress + (autoInitEntry ? 2u : 4u);
            if (autoInitEntry)
                _bus.WriteWord(CallerAddress, 0x4E92); // JSR(A2).
            else
            {
                _bus.WriteWord(CallerAddress, 0x4EAE); // JSR d16(A6).
                _bus.WriteWord(CallerAddress + 2, unchecked((ushort)WritePixelLvo));
            }
            _bus.WriteWord(ReturnAddress, 0x4E71);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }

        internal string Route { get; }
        internal uint GraphicsBase { get; }
        internal uint ReturnAddress { get; }

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
            var expectedAddresses = AddressCanaries.ToArray();
            if (_autoInitEntry)
                expectedAddresses[0] = _functionEntry; // A2's function pointer is also its preservation canary.
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            _cpu.State.D[0] = x;
            _cpu.State.D[1] = y;
            _cpu.State.A[0] = 0xA0A0_A0A1;
            _cpu.State.A[1] = pointer; // Never mask the caller's logical argument.
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
                _cpu.ExecuteInstruction(); // Actual physical negative-vector JMP.
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
                    // Every handled result is zero; the original hostile-high
                    // X is always nonzero. Only terminal RTS is observed, so
                    // successful preflight/draw-mode/plane-loop Bccs cannot
                    // masquerade as a decline, including validated no-ops.
                    usedFallback |= _cpu.State.D[0] == x;
                }
                _cpu.ExecuteInstruction();
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
                // Do not normalize any register. Volatile D0/D1/A0/A1 and CCR
                // have no general preservation assertion; D0/D1/A1 above are
                // only this native arm's independent existing outcome controls.
                return new CallResult(_cpu.State.D[0], _cpu.State.D[1], _cpu.State.A[1], _cpu.State.A[6],
                    _cpu.State.ProgramCounter, _cpu.State.A[7], usedFallback, nativeReturnCount, differences.ToArray());
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }

        public void Dispose() => _cpu.Dispose();
    }
}
