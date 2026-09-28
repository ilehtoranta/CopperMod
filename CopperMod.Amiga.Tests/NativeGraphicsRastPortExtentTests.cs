using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsRastPortExtentTests
{
    // The classic SDK structure is 100 bytes. Do not derive this oracle from
    // the older, conservative RastPortMinimumSize used by drawing consumers.
    private const int ClassicRastPortBytes = 100;
    private const uint ArenaAddress = 0x00D0_0000;
    private const int ArenaSize = 0x2000;
    private const uint RastPort = ArenaAddress + 0x100;
    private const uint Screen = ArenaAddress + 0x200;
    private const uint View = ArenaAddress + 0x500;
    private const uint RasInfo = ArenaAddress + 0x540;
    private const uint Plane = ArenaAddress + 0x1000;
    private const uint CapturedD0 = 0x1234_5678;
    private static readonly Lazy<NativeImage> Image = new(() =>
    {
        var code = NativeGraphicsRasterBodies.BuildCode(out var entries, out var fallback);
        return new NativeImage(code, entries, fallback);
    });
    private static readonly Lazy<NativeGraphicsLibraryHunkImage> Hunk = new(() =>
        NativeGraphicsLibraryHunkBuilder.Build(Image.Value.Code, Image.Value.Entries, Image.Value.Fallback));

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(false, 98)]
    [InlineData(true, 98)]
    public void PortableInitializerNeedsOnlyTheClassicHundredByteMemoryBoundary(bool mapped, int accessibleBytes)
    {
        var success = accessibleBytes == ClassicRastPortBytes;
        var memory = new BoundaryMemory(mapped, RastPort, accessibleBytes);
        var allocator = new RecordingAllocator();
        var core = CreateCore(memory, allocator);
        var expected = memory.Snapshot();
        if (success)
            ExpectedRastPort().CopyTo(expected, (int)(RastPort - ArenaAddress));
        var initialized = core.InitializeRastPort(RastPort);
        var failures = new List<string>();
        Check(failures, "100-byte initialization admitted; truncated 98-byte object declined", () => Assert.Equal(success, initialized));
        if (success)
            Check(failures, "no access outside the advertised public extent", () => Assert.Empty(memory.RejectedAccesses));
        Check(failures, "complete RastPort and adjacent canaries", () => Assert.Equal(expected, memory.Snapshot()));
        Check(failures, "no allocation or retirement", () =>
        {
            Assert.Empty(allocator.Allocations);
            Assert.Empty(allocator.Frees);
        });
        AssertNoFailures(failures, $"portable core/{(mapped ? "mapped" : "array")} backing, {accessibleBytes} bytes");
    }

    [Fact]
    public void NativeOverlayInitializerProbesOnlyTheClassicHundredByteWritableExtent()
    {
        var memory = new BoundaryMemory(mapped: true, RastPort, ClassicRastPortBytes);
        var allocator = new RecordingAllocator();
        var probes = new List<(uint Address, uint Bytes)>();
        var fontProbes = 0;
        var adapter = new CopperStartGraphicsRegisterAdapter(CreateCore(memory, allocator),
            ensureCompatibilityFont: () =>
            {
                fontProbes++;
                Assert.Equal(new[] { (RastPort, (uint)ClassicRastPortBytes) }, probes);
                return 0; // Existing no-font owner, after writable-span admission.
            },
            isWritableMemoryRange: (address, bytes) =>
            {
                probes.Add((address, bytes));
                return address == RastPort && bytes == ClassicRastPortBytes;
            });
        // The callback deliberately resolves no font and checks admission
        // ordering. This does not add a font-list owner or new default policy.
        var state = new M68kCpuState();
        state.D[0] = CapturedD0;
        state.A[1] = RastPort;
        var expected = memory.Snapshot();
        ExpectedRastPort().CopyTo(expected, (int)(RastPort - ArenaAddress));
        var claimed = adapter.TryInvoke(state, (int)GraphicsLvo.InitRastPort, nativeOverlay: true);
        var failures = new List<string>();
        Check(failures, "overlay claims the writable 100-byte output", () => Assert.True(claimed));
        Check(failures, "independent writable-span callback", () => Assert.Equal(
            new[] { (RastPort, (uint)ClassicRastPortBytes) }, probes));
        Check(failures, "one no-font callback after admission", () => Assert.Equal(1, fontProbes));
        Check(failures, "existing result/input convention", () =>
        {
            Assert.Equal(0u, state.D[0]);
            Assert.Equal(RastPort, state.A[1]);
        });
        Check(failures, "bounded memory accesses", () => Assert.Empty(memory.RejectedAccesses));
        Check(failures, "complete output and adjacent canaries", () => Assert.Equal(expected, memory.Snapshot()));
        Check(failures, "no allocation or retirement", () =>
        {
            Assert.Empty(allocator.Allocations);
            Assert.Empty(allocator.Frees);
        });
        AssertNoFailures(failures, "native overlay/InitRastPort only");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableScreenPrefixKeepsTheBitmapImmediatelyAfterItsRastPort(bool mapped)
    {
        var memory = new BoundaryMemory(mapped, ArenaAddress, ArenaSize);
        var allocator = new RecordingAllocator(Screen, View, RasInfo);
        var core = CreateCore(memory, allocator);
        var before = memory.Snapshot();
        var allocated = core.TryAllocateScreenPrefix(1, 64, 32, Plane, out var allocation);
        var published = memory.Snapshot();
        var failures = new List<string>();
        Check(failures, "prefix allocation", () => Assert.True(allocated));
        Check(failures, "classic adjacency and original topology", () =>
        {
            Assert.Equal(GraphicsLayouts.ScreenBitMap, GraphicsLayouts.ScreenRastPort + ClassicRastPortBytes);
            Assert.Equal(new ScreenPrefixAllocation(Screen, View,
                Screen + (uint)GraphicsLayouts.ScreenViewPort, RasInfo,
                Screen + (uint)GraphicsLayouts.ScreenRastPort,
                Screen + (uint)GraphicsLayouts.ScreenBitMap, Plane, 1, 64, 32), allocation);
            Assert.True(core.IsOwnedScreenPrefix(Screen));
        });
        Check(failures, "three header allocations, no borrowed-plane allocation", () => Assert.Equal(new[]
        {
            (Screen, (uint)GraphicsLayouts.ScreenSize, GraphicsMemoryClass.Public),
            (View, (uint)GraphicsLayouts.ViewSize, GraphicsMemoryClass.Public),
            (RasInfo, (uint)GraphicsLayouts.RasInfoSize, GraphicsMemoryClass.Public)
        }, allocator.Allocations));
        Check(failures, "embedded bitmap geometry survives RP initialization", () => Assert.Equal(
            new byte[] { 0, 8, 0, 32, 8, 1, 0, 0 },
            published.AsSpan((int)(Screen - ArenaAddress) + GraphicsLayouts.ScreenBitMap, 8).ToArray()));
        Check(failures, "complete Screen/View/RasInfo, plane and canaries", () =>
            Assert.Equal(ExpectedPublishedPrefix(before), published));

        // Exercise teardown on the exact published object, even if its byte
        // assertions failed. No bitmap/header repair is allowed in the test.
        var freed = core.FreeScreenPrefix(allocation.Screen);
        Check(failures, "unmodified owner can be retired", () =>
        {
            Assert.Equal(GraphicsRasterOperations.Success, freed);
            Assert.False(core.IsOwnedScreenPrefix(Screen));
        });
        Check(failures, "only the three owned headers retired, in order", () => Assert.Equal(new[]
        {
            (RasInfo, (uint)GraphicsLayouts.RasInfoSize, GraphicsMemoryClass.Public),
            (View, (uint)GraphicsLayouts.ViewSize, GraphicsMemoryClass.Public),
            (Screen, (uint)GraphicsLayouts.ScreenSize, GraphicsMemoryClass.Public)
        }, allocator.Frees));
        Check(failures, "teardown clears only owned headers", () =>
        {
            var expected = before.ToArray();
            expected.AsSpan((int)(Screen - ArenaAddress), GraphicsLayouts.ScreenSize).Clear();
            expected.AsSpan((int)(View - ArenaAddress), GraphicsLayouts.ViewSize).Clear();
            expected.AsSpan((int)(RasInfo - ArenaAddress), GraphicsLayouts.RasInfoSize).Clear();
            Assert.Equal(expected, memory.Snapshot());
        });
        Check(failures, "no rejected memory access", () => Assert.Empty(memory.RejectedAccesses));
        AssertNoFailures(failures, mapped ? "portable Screen prefix/mapped backing" : "portable Screen prefix/array backing");
    }

    public static IEnumerable<object[]> NativeCases()
    {
        foreach (var relocated in new[] { false, true })
        foreach (var scenario in new[] { "ordinary", "exact-end", "null", "odd", "wrap" })
            yield return new object[] { relocated, scenario };
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void NativeInitializerUsesTheHundredByteLogicalEnvelope(bool relocated, string scenario)
    {
        var pointer = scenario switch
        {
            "ordinary" => RastPort,
            "exact-end" => 0xFFFF_FF9Cu,
            "null" => 0u,
            "odd" => RastPort + 1u,
            "wrap" => 0xFFFF_FF9Eu,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var success = scenario is "ordinary" or "exact-end";
        using var fixture = new NativeFixture(relocated);
        var before = fixture.CaptureMemory();
        var result = fixture.Invoke(pointer);
        var expected = before;
        if (success)
        {
            // CPU argument stays logical 32-bit; only this memory oracle
            // translates the high pointer to the 68000's physical alias.
            if (scenario == "exact-end")
            {
                var high = before.High.ToArray();
                ExpectedRastPort().CopyTo(high, 0x9C);
                expected = before with { High = high };
            }
            else
            {
                var arena = before.Arena.ToArray();
                ExpectedRastPort().CopyTo(arena, (int)(RastPort - ArenaAddress));
                expected = before with { Arena = arena };
            }
        }
        var failures = new List<string>();
        Check(failures, "existing D0 and actual admission-branch convention", () =>
        {
            Assert.Equal(success ? 0u : CapturedD0, result.Data0);
            Assert.Equal(!success, result.TookDeclineBranch);
        });
        Check(failures, "native public caller PC/SP and pointer roles", () =>
        {
            Assert.Equal(NativeFixture.ReturnAddress, result.ProgramCounter);
            Assert.Equal(NativeFixture.StackPointer, result.StackPointer);
            Assert.Equal(pointer, result.Address1);
            Assert.Equal(fixture.GraphicsBase, result.Address6);
        });
        Check(failures, "entire public output, tail guard and logical-wrap aliases", () =>
        {
            var actual = fixture.CaptureMemory();
            Assert.Equal(expected.Arena, actual.Arena);
            Assert.Equal(expected.High, actual.High);
            Assert.Equal(expected.Low, actual.Low);
            Assert.Equal(expected.GraphicsImage, actual.GraphicsImage);
            Assert.Equal(expected.Caller, actual.Caller);
            Assert.Equal(expected.StackGuards, actual.StackGuards);
        });
        AssertNoFailures(failures, $"{fixture.Route}, {scenario}");
    }

    private static byte[] ExpectedRastPort()
    {
        // Preserve the current public no-font defaults. The extent test
        // neither supplies a new font policy nor requires callee-save fixes.
        var result = new byte[ClassicRastPortBytes];
        result[GraphicsLayouts.RastPortMask] = 0xFF;
        result[GraphicsLayouts.RastPortFgPen] = 0xFF;
        result[GraphicsLayouts.RastPortOutlinePen] = 0xFF;
        result[GraphicsLayouts.RastPortDrawMode] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(GraphicsLayouts.RastPortLinePattern), 0xFFFF);
        result.AsSpan(GraphicsLayouts.RastPortMinterms, 8).Fill(0xCA);
        return result;
    }

    private static byte[] ExpectedPublishedPrefix(byte[] before)
    {
        var result = before.ToArray();
        var screen = result.AsSpan((int)(Screen - ArenaAddress), GraphicsLayouts.ScreenSize);
        var view = result.AsSpan((int)(View - ArenaAddress), GraphicsLayouts.ViewSize);
        var rasInfo = result.AsSpan((int)(RasInfo - ArenaAddress), GraphicsLayouts.RasInfoSize);
        screen.Clear();
        view.Clear();
        rasInfo.Clear();
        BinaryPrimitives.WriteUInt16BigEndian(screen[GraphicsLayouts.ScreenWidth..], 64);
        BinaryPrimitives.WriteUInt16BigEndian(screen[GraphicsLayouts.ScreenHeight..], 32);
        BinaryPrimitives.WriteUInt32BigEndian(view[GraphicsLayouts.ViewViewPort..], Screen + (uint)GraphicsLayouts.ScreenViewPort);
        BinaryPrimitives.WriteUInt16BigEndian(view[GraphicsLayouts.ViewDyOffset..], (ushort)GraphicsLayouts.ViewDefaultDyOffset);
        BinaryPrimitives.WriteUInt16BigEndian(view[GraphicsLayouts.ViewDxOffset..], (ushort)GraphicsLayouts.ViewDefaultDxOffset);
        var viewPort = screen[GraphicsLayouts.ScreenViewPort..];
        BinaryPrimitives.WriteUInt16BigEndian(viewPort[GraphicsLayouts.ViewPortDWidth..], 64);
        BinaryPrimitives.WriteUInt16BigEndian(viewPort[GraphicsLayouts.ViewPortDHeight..], 32);
        viewPort[GraphicsLayouts.ViewPortSpritePriorities] = GraphicsLayouts.ViewPortDefaultSpritePriorities;
        BinaryPrimitives.WriteUInt32BigEndian(viewPort[GraphicsLayouts.ViewPortRasInfo..], RasInfo);
        ExpectedRastPort().AsSpan().CopyTo(screen.Slice(GraphicsLayouts.ScreenRastPort, ClassicRastPortBytes));
        BinaryPrimitives.WriteUInt32BigEndian(screen[(GraphicsLayouts.ScreenRastPort + GraphicsLayouts.RastPortBitMap)..],
            Screen + (uint)GraphicsLayouts.ScreenBitMap);
        var bitMap = screen[GraphicsLayouts.ScreenBitMap..];
        BinaryPrimitives.WriteUInt16BigEndian(bitMap[GraphicsLayouts.BitMapBytesPerRow..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(bitMap[GraphicsLayouts.BitMapRows..], 32);
        bitMap[GraphicsLayouts.BitMapFlags] = 8;
        bitMap[GraphicsLayouts.BitMapDepth] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(bitMap[GraphicsLayouts.BitMapPlanes..], Plane);
        BinaryPrimitives.WriteUInt32BigEndian(rasInfo[GraphicsLayouts.RasInfoBitMap..], Screen + (uint)GraphicsLayouts.ScreenBitMap);
        return result;
    }

    private static GraphicsLibraryCore CreateCore(IGraphicsMemory memory, RecordingAllocator allocator)
    {
        var unused = new UnusedHardware();
        return new GraphicsLibraryCore(memory, allocator, unused, unused);
    }

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add(label + ": " + exception.Message);
        }
    }

    private static void AssertNoFailures(List<string> failures, string context)
        => Assert.True(failures.Count == 0, context + ":\n" + string.Join("\n", failures));

    private sealed class RecordingAllocator : IGraphicsAllocatorBackend
    {
        private readonly Queue<uint> _addresses;
        internal RecordingAllocator(params uint[] addresses) => _addresses = new Queue<uint>(addresses);
        internal List<(uint Address, uint Bytes, GraphicsMemoryClass Class)> Allocations { get; } = new();
        internal List<(uint Address, uint Bytes, GraphicsMemoryClass Class)> Frees { get; } = new();
        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            Assert.NotEmpty(_addresses);
            address = _addresses.Dequeue();
            Allocations.Add((address, byteCount, memoryClass));
            return true;
        }
        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass)
            => Frees.Add((address, byteCount, memoryClass));
    }

    private sealed class UnusedHardware : IGraphicsBlitterBackend, IGraphicsDisplayBackend
    {
        public void Own() => throw new InvalidOperationException("No blitter operation is in scope.");
        public void Disown() => throw new InvalidOperationException("No blitter operation is in scope.");
        public void Wait() => throw new InvalidOperationException("No blitter operation is in scope.");
        public void Submit(uint operationAddress) => throw new InvalidOperationException("No blitter operation is in scope.");
        public void PublishView(uint viewAddress) => throw new InvalidOperationException("No display publication is in scope.");
        public void WaitForTopOfFrame() => throw new InvalidOperationException("No display wait is in scope.");
        public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) => throw new InvalidOperationException("No display wait is in scope.");
        public ushort GetBeamPosition() => throw new InvalidOperationException("No beam access is in scope.");
    }

    private sealed class BoundaryMemory : IGraphicsMemory
    {
        private readonly byte[] _bytes = Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray();
        private readonly AmigaBus? _bus;
        private readonly CopperStartGraphicsMemoryAdapter? _mapped;
        private readonly uint _accessibleStart;
        private readonly int _accessibleBytes;
        internal BoundaryMemory(bool mapped, uint accessibleStart, int accessibleBytes)
        {
            _accessibleStart = accessibleStart;
            _accessibleBytes = accessibleBytes;
            if (mapped)
            {
                _bus = new AmigaBus();
                _bus.MapWritableMemory(ArenaAddress, _bytes);
                _mapped = new CopperStartGraphicsMemoryAdapter(new HostGuestMemory(_bus));
            }
        }
        internal List<(uint Address, int Bytes)> RejectedAccesses { get; } = new();
        // Canaries exist in the backing store, but are not part of the
        // caller-advertised IGraphicsMemory object in the 100-byte rows.
        private bool TryOffset(uint address, int bytes, out int offset)
        {
            offset = 0;
            if (address < _accessibleStart || (ulong)address + (uint)bytes > (ulong)_accessibleStart + (uint)_accessibleBytes ||
                address < ArenaAddress || (ulong)address + (uint)bytes > (ulong)ArenaAddress + ArenaSize)
            {
                RejectedAccesses.Add((address, bytes));
                return false;
            }
            offset = checked((int)(address - ArenaAddress));
            return true;
        }
        internal byte[] Snapshot() => _bus is null ? _bytes.ToArray() :
            Enumerable.Range(0, ArenaSize).Select(index => _bus.ReadByte(ArenaAddress + (uint)index)).ToArray();
        public bool TryReadByte(uint address, out byte value)
        {
            value = 0;
            if (!TryOffset(address, 1, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryReadByte(address, out value);
            value = _bytes[offset];
            return true;
        }
        public bool TryReadWord(uint address, out ushort value)
        {
            value = 0;
            if (!TryOffset(address, 2, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryReadWord(address, out value);
            value = BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(offset, 2));
            return true;
        }
        public bool TryReadLong(uint address, out uint value)
        {
            value = 0;
            if (!TryOffset(address, 4, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryReadLong(address, out value);
            value = BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(offset, 4));
            return true;
        }
        public bool TryWriteByte(uint address, byte value)
        {
            if (!TryOffset(address, 1, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryWriteByte(address, value);
            _bytes[offset] = value;
            return true;
        }
        public bool TryWriteWord(uint address, ushort value)
        {
            if (!TryOffset(address, 2, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryWriteWord(address, value);
            BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(offset, 2), value);
            return true;
        }
        public bool TryWriteLong(uint address, uint value)
        {
            if (!TryOffset(address, 4, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryWriteLong(address, value);
            BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan(offset, 4), value);
            return true;
        }
    }

    private sealed record NativeImage(byte[] Code, IReadOnlyDictionary<GraphicsLvo, int> Entries, int Fallback);
    private sealed record NativeMemory(byte[] Arena, byte[] High, byte[] Low, byte[] GraphicsImage, byte[] Caller, byte[] StackGuards);
    private sealed record NativeResult(uint Data0, uint Address1, uint Address6, uint ProgramCounter, uint StackPointer, bool TookDeclineBranch);

    private sealed class NativeFixture : IDisposable
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
        private readonly uint _vectorSlot;
        internal NativeFixture(bool relocated)
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
            _vectorSlot = unchecked((uint)((long)GraphicsBase + (int)GraphicsLvo.InitRastPort));
            _bus.MapWritableMemory(ArenaAddress, Enumerable.Repeat((byte)0xA5, ArenaSize).ToArray());
            _bus.MapWritableMemory(HighPhysicalAddress, Enumerable.Repeat((byte)0xB7, 0x100).ToArray());
            _bus.MapWritableMemory(StackAddress, Enumerable.Repeat((byte)0x5A, 0x600).ToArray());
            for (var offset = 0; offset < 0x100; offset++)
                _bus.WriteByte((uint)offset, 0xC3, 0);
            _bus.MapWritableMemory(CallerAddress, new byte[8]);
            _bus.WriteWord(CallerAddress, 0x4EAE); // actual graphics negative vector
            _bus.WriteWord(CallerAddress + 2, unchecked((ushort)(int)GraphicsLvo.InitRastPort));
            _bus.WriteWord(ReturnAddress, 0x4E71);
            // Select the existing no-font path before the initial snapshot.
            _bus.WriteLong(GraphicsBase + (uint)GraphicsLayouts.GfxBaseDefaultFont, 0);
            _cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, _bus);
        }
        internal string Route { get; }
        internal uint GraphicsBase { get; }
        private byte[] ReadBytes(uint address, int count)
            => Enumerable.Range(0, count).Select(index => _bus.ReadByte(address + (uint)index)).ToArray();
        internal NativeMemory CaptureMemory() => new(
            ReadBytes(ArenaAddress, ArenaSize), ReadBytes(HighPhysicalAddress, 0x100), ReadBytes(0, 0x100),
            ReadBytes(GraphicsBase - (uint)NativeGraphicsLibraryImageBuilder.VectorTableSize,
                NativeGraphicsLibraryImageBuilder.VectorTableSize + NativeGraphicsLibraryImageBuilder.PositiveImageSize),
            ReadBytes(CallerAddress, 8),
            ReadBytes(StackAddress, 0x100).Concat(ReadBytes(StackAddress + 0x340, 0x2C0)).ToArray());
        internal NativeResult Invoke(uint pointer)
        {
            _cpu.Reset(CallerAddress, StackPointer);
            Assert.Equal(M68kCpuState.ResetStatusRegister, _cpu.State.StatusRegister);
            for (var index = 0; index < 8; index++)
                _cpu.State.D[index] = 0xD000_0100u + (uint)index;
            for (var index = 0; index < 6; index++)
                _cpu.State.A[index] = 0xA000_0201u + (uint)(index * 0x10);
            _cpu.State.D[0] = CapturedD0;
            _cpu.State.A[1] = pointer;
            _cpu.State.A[6] = GraphicsBase;
            // D6 is existing native scratch, deliberately not a new public
            // callee-save contract in this extent-only regression fixture.
            _cpu.ExecuteInstruction();
            Assert.Equal(_vectorSlot, _cpu.State.ProgramCounter);
            Assert.Equal(StackPointer - 4, _cpu.State.A[7]);
            Assert.Equal(ReturnAddress, _bus.ReadLong(_cpu.State.A[7]));
            _cpu.ExecuteInstruction(); // The actual vector JMP must enter InitRastPort.
            Assert.Equal(_nativeCodeAddress + (uint)Image.Value.Entries[GraphicsLvo.InitRastPort],
                _cpu.State.ProgramCounter);
            var tookDeclineBranch = false;
            for (var instruction = 0; instruction < 10_000; instruction++)
            {
                var pc = _cpu.State.ProgramCounter;
                Assert.True(pc == _vectorSlot || (pc >= _nativeCodeAddress &&
                    (ulong)pc < (ulong)_nativeCodeAddress + (uint)Image.Value.Code.Length),
                    $"Unexpected execution address 0x{pc:X8}; no host graphics gateway is installed.");
                // The linker can clone the fallback RTS near these guards.
                // Observe the three actual admission Bcc.W decisions instead
                // of requiring a visit to one global fallback address. The
                // later font skip is Bcc.S ($6720), not one of these opcodes.
                var isAdmissionBranch = _bus.ReadWord(pc) is 0x6700 or 0x6600 or 0x6200;
                _cpu.ExecuteInstruction();
                tookDeclineBranch |= isAdmissionBranch && _cpu.State.ProgramCounter != pc + 4u;
                if (_cpu.State.ProgramCounter == ReturnAddress)
                    return new NativeResult(_cpu.State.D[0], _cpu.State.A[1], _cpu.State.A[6],
                        _cpu.State.ProgramCounter, _cpu.State.A[7], tookDeclineBranch);
            }
            throw new InvalidOperationException($"{Route} did not return: PC={_cpu.State.ProgramCounter:X8}, "
                + $"SR={_cpu.State.StatusRegister:X4}, A7={_cpu.State.A[7]:X8}.");
        }
        public void Dispose() => _cpu.Dispose();
    }
}
