using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;
using CopperMod.Amiga.Firmware;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsSetFontExtentTests
{
    // This is the register adapter's native-overlay admission boundary, not
    // execution of the native SetFont body or a native callee-save proof.
    // "Mapped" selects actual AmigaBus memory backing, not another LVO route.
    private const int ClassicRastPortBytes = 100;
    private const int LegacyProbeBytes = 180;
    private const uint RastPort = 0x00D0_0020;
    private const uint Font = 0x00D0_1000;
    private const uint GraphicsBase = 0x00D0_2000;
    private const ushort FontHeight = 13;
    private const ushort FontWidth = 9;
    private const ushort FontBaseline = 9;

    public static IEnumerable<object[]> OverlayCases()
    {
        foreach (var mapped in new[] { false, true })
        foreach (var guestDefault in new[] { false, true })
        foreach (var boundary in new[]
        {
            "hundred-unmapped", "hundred-readonly", "ninety-nine",
            "protected-last", "ordinary", "readonly"
        })
            yield return new object[] { mapped, guestDefault, boundary };
    }

    [Theory]
    [MemberData(nameof(OverlayCases))]
    public void OverlayRequiresExactlyOneHundredWritableBytesBeforeResolvingFonts(
        bool mapped, bool guestDefault, string boundary)
    {
        var fixture = new Fixture(mapped, boundary);
        var succeeds = boundary is "hundred-unmapped" or "hundred-readonly" or "ordinary";
        var before = fixture.Memory.Snapshot();
        var expected = succeeds ? ExpectedPublication(before) : before;
        var state = CreateState(guestDefault);
        var expectedData = state.D.ToArray();
        if (succeeds) expectedData[0] = 0;
        var expectedAddresses = state.A.ToArray();
        var originalPc = state.ProgramCounter;
        var originalSr = state.StatusRegister;
        var originalCycles = state.Cycles;
        var originalNativeCycles = state.NativeCycles;
        fixture.Memory.BeginObservation();

        var claimed = fixture.Adapter.TryInvoke(state, (int)GraphicsLvo.SetFont, nativeOverlay: true);

        // Keep the admission, side-effect and probe diagnostics independent:
        // an obsolete 180-byte probe must not hide a wrong claim or mutation.
        var failures = new List<string>();
        Check(failures, "claim/decline", () => Assert.Equal(succeeds, claimed));
        Check(failures, "literal complete writable-envelope probe", () => Assert.Equal(
            new[] { (RastPort, (uint)ClassicRastPortBytes) }, fixture.Memory.WritableProbes));
        Check(failures, "probe precedes font/default resolution and publication", () =>
        {
            Assert.NotEmpty(fixture.Memory.Events);
            Assert.Equal("probe", fixture.Memory.Events[0]);
            Assert.Equal(succeeds, fixture.Memory.FontReads > 0);
            Assert.Equal(succeeds && guestDefault ? 1 : 0, fixture.Memory.DefaultReads);
            Assert.Equal(0, fixture.CompatibilityDefaultCalls);
            if (!succeeds) Assert.Empty(fixture.Memory.Writes);
        });
        Check(failures, "all adapter caller registers, PC and cycle counters", () =>
        {
            Assert.Equal(expectedData, state.D.ToArray());
            Assert.Equal(expectedAddresses, state.A.ToArray());
            Assert.Equal(originalPc, state.ProgramCounter);
            Assert.Equal(originalSr, state.StatusRegister);
            Assert.Equal(originalCycles, state.Cycles);
            Assert.Equal(originalNativeCycles, state.NativeCycles);
        });
        Check(failures, "only font/cache/style fields change; caller tails and guards survive", () =>
            AssertMemoryEqual(expected, fixture.Memory.Snapshot()));
        Check(failures, "no speculative access to unmapped or protected bytes", () =>
            Assert.Empty(fixture.Memory.RejectedAccesses));
        Check(failures, "no allocation or retirement", fixture.AssertNoAllocatorCalls);
        AssertNoFailures(failures, Context(mapped, guestDefault, boundary, "overlay"));
    }

    public static IEnumerable<object[]> CoreCases()
    {
        foreach (var mapped in new[] { false, true })
        foreach (var guestDefault in new[] { false, true })
        foreach (var partialBaselineFailure in new[] { false, true })
            yield return new object[] { mapped, guestDefault, partialBaselineFailure };
    }

    [Theory]
    [MemberData(nameof(CoreCases))]
    public void PureCoreKeepsFieldScopedPublicationAndRollsBackAPartialLateWrite(
        bool mapped, bool guestDefault, bool partialBaselineFailure)
    {
        // The pure core has no complete-envelope callback. Do not make it
        // inherit the overlay's unrelated conservative admission policy.
        var fixture = new Fixture(mapped, "hundred-readonly");
        var before = fixture.Memory.Snapshot();
        var expected = partialBaselineFailure ? before : ExpectedPublication(before);
        var backend = new GraphicsMemoryFontBackend(fixture.Memory, fixture.Allocator,
            fontList: fixture.FontList);
        fixture.Memory.BeginObservation();
        fixture.Memory.FailNextBaselineWrite = partialBaselineFailure;

        var result = fixture.Core.SetFont(RastPort, guestDefault ? 0u : Font, backend);

        var failures = new List<string>();
        Check(failures, "field-scoped core outcome", () => Assert.Equal(partialBaselineFailure
            ? GraphicsRasterOperations.Failure : GraphicsRasterOperations.Success, result));
        Check(failures, "real guest font metrics/default resolution", () =>
        {
            Assert.True(fixture.Memory.FontReads > 0);
            Assert.Equal(guestDefault ? 1 : 0, fixture.Memory.DefaultReads);
            Assert.Equal(0, fixture.CompatibilityDefaultCalls);
            Assert.Empty(fixture.Memory.WritableProbes);
        });
        Check(failures, "late injected failure occurs exactly once", () =>
        {
            Assert.Equal(partialBaselineFailure ? 1 : 0, fixture.Memory.InjectedFailures);
            Assert.False(fixture.Memory.FailNextBaselineWrite);
            Assert.NotEmpty(fixture.Memory.Writes);
        });
        Check(failures, "complete publication or complete rollback, including partial WORD", () =>
            AssertMemoryEqual(expected, fixture.Memory.Snapshot()));
        Check(failures, "bounded accessible fields only", () =>
            Assert.Empty(fixture.Memory.RejectedAccesses));
        Check(failures, "no allocation or retirement", fixture.AssertNoAllocatorCalls);
        AssertNoFailures(failures, Context(mapped, guestDefault,
            partialBaselineFailure ? "partial-baseline-failure" : "success", "pure core"));
    }

    private static M68kCpuState CreateState(bool guestDefault)
    {
        var state = new M68kCpuState
        {
            ProgramCounter = 0x0012_3456,
            StatusRegister = 0x2501,
            Cycles = 823
        };
        for (var index = 0; index < 8; index++)
        {
            state.D[index] = 0xD123_4560u + (uint)index;
            state.A[index] = 0xA246_8100u + (uint)index * 0x10u;
        }
        state.A[0] = guestDefault ? 0u : Font;
        state.A[1] = RastPort;
        state.A[6] = GraphicsBase;
        state.A[7] = 0x00C7_0300;
        return state;
    }

    private static Dictionary<uint, byte[]> ExpectedPublication(Dictionary<uint, byte[]> before)
    {
        var expected = before.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var port = expected[RastPort];
        BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(0x34, 4), Font);
        port[0x38] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(port.AsSpan(0x3A, 2), FontHeight);
        BinaryPrimitives.WriteUInt16BigEndian(port.AsSpan(0x3C, 2), FontWidth);
        BinaryPrimitives.WriteUInt16BigEndian(port.AsSpan(0x3E, 2), FontBaseline);
        return expected;
    }

    private static void AssertMemoryEqual(Dictionary<uint, byte[]> expected, Dictionary<uint, byte[]> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(address => address), actual.Keys.OrderBy(address => address));
        foreach (var (address, bytes) in expected)
            Assert.True(bytes.SequenceEqual(actual[address]), $"Memory/guard at ${address:X8} changed unexpectedly.");
    }

    private static string Context(bool mapped, bool guestDefault, string boundary, string route)
        => $"{route}/{(mapped ? "AmigaBus" : "array")}/{(guestDefault ? "guest default" : "explicit font")}/{boundary}";

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

    private sealed class Fixture
    {
        internal Fixture(bool mapped, string boundary)
        {
            Memory = new BoundaryMemory(mapped, boundary);
            Allocator = new RecordingAllocator();
            var unused = new UnusedHardware();
            Core = new GraphicsLibraryCore(Memory, Allocator, unused, unused);
            FontList = new GraphicsGuestFontListBackend(Memory, GraphicsBase);
            Assert.True(FontList.Initialize(Font));
            Assert.True(FontList.TryAdd(Font));
            Assert.True(FontList.TryEnumerate(out var members));
            Assert.Equal(new[] { Font }, members);
            Assert.True(FontList.TryGetDefaultFont(out var selected));
            Assert.Equal(Font, selected);
            Adapter = new CopperStartGraphicsRegisterAdapter(Core,
                ensureCompatibilityFont: () =>
                {
                    CompatibilityDefaultCalls++;
                    return 0;
                },
                fontList: FontList,
                isWritableMemoryRange: Memory.ProbeWritable);
        }

        internal BoundaryMemory Memory { get; }
        internal RecordingAllocator Allocator { get; }
        internal GraphicsLibraryCore Core { get; }
        internal GraphicsGuestFontListBackend FontList { get; }
        internal CopperStartGraphicsRegisterAdapter Adapter { get; }
        internal int CompatibilityDefaultCalls { get; private set; }

        internal void AssertNoAllocatorCalls()
        {
            Assert.Empty(Allocator.Allocations);
            Assert.Empty(Allocator.Frees);
        }
    }

    private sealed class BoundaryMemory : IGraphicsMemory
    {
        private readonly List<Region> _regions = new();
        private readonly AmigaBus? _bus;
        private readonly CopperStartGraphicsMemoryAdapter? _mapped;
        private bool _observing;

        internal BoundaryMemory(bool mapped, string boundary)
        {
            if (mapped)
            {
                _bus = new AmigaBus();
                _mapped = new CopperStartGraphicsMemoryAdapter(new HostGuestMemory(_bus));
            }

            Add(RastPort - 32, Enumerable.Repeat((byte)0xA5, 32).ToArray(), writable: false);
            var port = Enumerable.Range(0, LegacyProbeBytes).Select(index => (byte)(index ^ 0xD5)).ToArray();
            Array.Clear(port, 0, 8); // No bitmap or layer owner is involved.
            BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(0x34, 4), 0xDEED_2468);
            port[0x38] = 7;
            var (writableBytes, readOnlyBytes) = boundary switch
            {
                "hundred-unmapped" => (100, 0),
                "hundred-readonly" => (100, 80),
                "ninety-nine" => (99, 0),
                "protected-last" => (99, 81),
                "ordinary" => (180, 0),
                "readonly" => (0, 180),
                _ => throw new ArgumentOutOfRangeException(nameof(boundary))
            };
            if (writableBytes != 0)
                Add(RastPort, port.AsSpan(0, writableBytes).ToArray(), writable: true);
            if (readOnlyBytes != 0)
                Add(RastPort + (uint)writableBytes,
                    port.AsSpan(writableBytes, readOnlyBytes).ToArray(), writable: false);
            Add(RastPort + LegacyProbeBytes, Enumerable.Repeat((byte)0x5A, 32).ToArray(), writable: false);

            var font = new byte[256];
            BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(GraphicsLayouts.TextFontYSize, 2), FontHeight);
            BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(GraphicsLayouts.TextFontXSize, 2), FontWidth);
            BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(GraphicsLayouts.TextFontBaseline, 2), FontBaseline);
            BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(GraphicsLayouts.TextFontBoldSmear, 2), 1);
            font[GraphicsLayouts.TextFontFlags] = 1; // ROMFONT, with a coherent fixed-cell strike.
            font[GraphicsLayouts.TextFontLoChar] = (byte)'A';
            font[GraphicsLayouts.TextFontHiChar] = (byte)'A';
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(GraphicsLayouts.TextFontCharData, 4), Font + 0x80);
            BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(GraphicsLayouts.TextFontModulo, 2), 4);
            font.AsSpan(0x80, FontHeight * 4).Fill(0x96); // Character plus default cell.
            Add(Font, font, writable: true);
            Add(GraphicsBase, Enumerable.Repeat((byte)0xC7, 1024).ToArray(), writable: true);

            if (_bus is not null)
            {
                Assert.Equal(writableBytes >= ClassicRastPortBytes,
                    _bus.IsWritableMemoryRange(RastPort, ClassicRastPortBytes));
                Assert.Equal(writableBytes >= LegacyProbeBytes,
                    _bus.IsWritableMemoryRange(RastPort, LegacyProbeBytes));
            }
        }

        internal List<(uint Address, uint Bytes)> WritableProbes { get; } = new();
        internal List<string> Events { get; } = new();
        internal List<(uint Address, int Bytes)> Writes { get; } = new();
        internal List<(uint Address, int Bytes, bool Write)> RejectedAccesses { get; } = new();
        internal int FontReads { get; private set; }
        internal int DefaultReads { get; private set; }
        internal bool FailNextBaselineWrite { get; set; }
        internal int InjectedFailures { get; private set; }

        internal void BeginObservation() => _observing = true;

        private void Add(uint address, byte[] bytes, bool writable)
        {
            _regions.Add(new Region(address, bytes, writable));
            if (_bus is null) return;
            if (writable) _bus.MapWritableMemory(address, bytes);
            else _bus.MapReadOnlyMemory(address, bytes);
        }

        private Region? Find(uint address, uint bytes)
            => _regions.FirstOrDefault(region => address >= region.Address &&
                (ulong)address + bytes <= (ulong)region.Address + (uint)region.Bytes.Length);

        internal bool ProbeWritable(uint address, uint bytes)
        {
            WritableProbes.Add((address, bytes));
            Events.Add("probe");
            return _bus is not null
                ? bytes <= int.MaxValue && _bus.IsWritableMemoryRange(address, (int)bytes)
                : Find(address, bytes) is { Writable: true };
        }

        private bool TryAccess(uint address, int bytes, bool write, out Region region, out int offset)
        {
            region = Find(address, (uint)bytes)!;
            offset = 0;
            if (_observing && write)
            {
                Writes.Add((address, bytes));
                Events.Add("write");
            }
            if (region is null || (write && !region.Writable))
            {
                if (_observing) RejectedAccesses.Add((address, bytes, write));
                return false;
            }
            offset = (int)(address - region.Address);
            if (_observing && !write)
            {
                if (region.Address == Font)
                {
                    FontReads++;
                    Events.Add("font");
                }
                if (address == GraphicsBase + GraphicsLibraryImageLayout.GfxBaseDefaultFont)
                {
                    DefaultReads++;
                    Events.Add("default");
                }
            }
            return true;
        }

        internal Dictionary<uint, byte[]> Snapshot() => _regions.ToDictionary(region => region.Address,
            region => _bus is null ? region.Bytes.ToArray() : Enumerable.Range(0, region.Bytes.Length)
                .Select(index => _bus.ReadByte(region.Address + (uint)index)).ToArray());

        public bool TryReadByte(uint address, out byte value)
        {
            value = 0;
            if (!TryAccess(address, 1, false, out var region, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryReadByte(address, out value);
            value = region.Bytes[offset];
            return true;
        }

        public bool TryReadWord(uint address, out ushort value)
        {
            value = 0;
            if (!TryAccess(address, 2, false, out var region, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryReadWord(address, out value);
            value = BinaryPrimitives.ReadUInt16BigEndian(region.Bytes.AsSpan(offset, 2));
            return true;
        }

        public bool TryReadLong(uint address, out uint value)
        {
            value = 0;
            if (!TryAccess(address, 4, false, out var region, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryReadLong(address, out value);
            value = BinaryPrimitives.ReadUInt32BigEndian(region.Bytes.AsSpan(offset, 4));
            return true;
        }

        public bool TryWriteByte(uint address, byte value)
        {
            if (!TryAccess(address, 1, true, out var region, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryWriteByte(address, value);
            region.Bytes[offset] = value;
            return true;
        }

        public bool TryWriteWord(uint address, ushort value)
        {
            if (!TryAccess(address, 2, true, out var region, out var offset)) return false;
            if (FailNextBaselineWrite && address == RastPort + 0x3E)
            {
                FailNextBaselineWrite = false;
                InjectedFailures++;
                // Deliberately commit the first byte before reporting failure.
                // The next write is allowed, so rollback must undo it as well.
                if (_mapped is not null)
                    Assert.True(_mapped.TryWriteByte(address, (byte)(value >> 8)));
                else region.Bytes[offset] = (byte)(value >> 8);
                return false;
            }
            if (_mapped is not null) return _mapped.TryWriteWord(address, value);
            BinaryPrimitives.WriteUInt16BigEndian(region.Bytes.AsSpan(offset, 2), value);
            return true;
        }

        public bool TryWriteLong(uint address, uint value)
        {
            if (!TryAccess(address, 4, true, out var region, out var offset)) return false;
            if (_mapped is not null) return _mapped.TryWriteLong(address, value);
            BinaryPrimitives.WriteUInt32BigEndian(region.Bytes.AsSpan(offset, 4), value);
            return true;
        }

        private sealed record Region(uint Address, byte[] Bytes, bool Writable);
    }

    private sealed class RecordingAllocator : IGraphicsAllocatorBackend
    {
        internal List<(uint Bytes, GraphicsMemoryClass Class)> Allocations { get; } = new();
        internal List<(uint Address, uint Bytes, GraphicsMemoryClass Class)> Frees { get; } = new();
        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            Allocations.Add((byteCount, memoryClass));
            address = 0;
            return false;
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
}
