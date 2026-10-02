using System.Buffers.Binary;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class NativeGraphicsPortableBitmapExtentTests
{
    // This tests portable logical admission and host register dispatch, not
    // native 68k bodies, their saved registers, or a complete writable-RP probe.
    // AmigaBus host mappings retain all 32 address bits; no CPU alias is used.
    private const int ClassicRastPortBytes = 100;
    private const uint OrdinaryRastPort = 0x00D0_0020;
    private const uint BitMap = 0x00D1_0020;
    private const uint Plane0 = 0x00D2_0020;
    private const uint Plane1 = 0x00D3_0020;
    private const uint GraphicsBase = 0x00A0_0000;
    private const uint Caller = 0x00B8_0020;
    private const uint Stack = 0x00C7_0300;
    private const uint InputX = 0xCAFE_0007;
    private const uint InputY = 0xBEEF_0001;

    public static IEnumerable<object[]> PixelCases()
    {
        foreach (var mapped in new[] { false, true })
        foreach (var write in new[] { false, true })
        foreach (var (name, address, admitted) in new[]
        {
            ("ordinary-100", OrdinaryRastPort, true),
            ("old-end", 0xFFFF_FF4Cu, true),
            ("first-new", 0xFFFF_FF4Eu, true),
            ("legacy-FF80", 0xFFFF_FF80u, true),
            ("exact-100", 0xFFFF_FF9Cu, true),
            ("wrapping-100", 0xFFFF_FF9Eu, false),
            ("null", 0u, false),
            ("odd", OrdinaryRastPort + 1u, false)
        })
            yield return new object[] { mapped, write, name, address, admitted };
    }

    [Theory]
    [MemberData(nameof(PixelCases))]
    public void SharedLogicalEnvelopeAdmitsClassicPortsWithoutClaimingWrappedOnes(
        bool mapped, bool write, string boundary, uint rastPort, bool admitted)
    {
        var failures = new List<string>();
        Check(failures, "decoder", () => VerifyDecoder(
            new Fixture(mapped, rastPort), admitted, truncatedPointer: false));
        foreach (var route in new[] { "core", "adapter", "overlay" })
            Check(failures, route, () => VerifyPixel(
                new Fixture(mapped, rastPort), write, admitted, route));

        // These unchanged ownership controls run once per backing/vector,
        // without multiplying the address matrix or adding a provider model.
        if (boundary == "ordinary-100")
        {
            foreach (var owner in new[] { "layer", "rtg-port", "rtg-bitmap" })
                Check(failures, owner, () => VerifyOwnerDecline(
                    new Fixture(mapped, rastPort, owner: owner), write, owner));
        }
        AssertClean(failures, $"{Backing(mapped)}/{(write ? "WritePixel" : "ReadPixel")}/{boundary}");
    }

    public static IEnumerable<object[]> MappingCases()
    {
        foreach (var mapped in new[] { false, true })
        foreach (var shape in new[] { "readonly-100", "readable-99", "pointer-only", "truncated-pointer" })
            yield return new object[] { mapped, shape };
    }

    [Theory]
    [MemberData(nameof(MappingCases))]
    public void DecoderReadsItsFieldsRatherThanProbingAnEntireMappedOrWritablePort(bool mapped, string shape)
    {
        // A hypothetical 100-byte logical object can have sparse/provider-backed
        // storage. Only rp_BitMap is consumed here; a mapped 99-byte prefix is
        // not the same thing as a 100-byte logical address envelope that wraps.
        VerifyDecoder(new Fixture(mapped, OrdinaryRastPort, shape),
            admitted: shape != "truncated-pointer", truncatedPointer: shape == "truncated-pointer");

        // Read-only and 99-byte ports also contain every field used by pixels.
        // Actual plane storage, not unused RP bytes, is the writable output.
        if (shape is "readonly-100" or "readable-99")
        {
            foreach (var write in new[] { false, true })
                VerifyPixel(new Fixture(mapped, OrdinaryRastPort, shape), write, admitted: true, route: "overlay");
        }
    }

    private static void VerifyDecoder(Fixture fixture, bool admitted, bool truncatedPointer)
    {
        var before = fixture.Memory.Snapshot();
        fixture.Memory.BeginObservation();
        var actual = GraphicsRasterOperations.TryReadBitmap(fixture.Memory, fixture.RastPort, out var bitmap);
        var expectedReads = admitted
            ? new[] { (fixture.RastPort + 4, 4), (BitMap, 2), (BitMap + 2, 2), (BitMap + 4, 1), (BitMap + 5, 1) }
            : truncatedPointer ? new[] { (fixture.RastPort + 4, 4) } : Array.Empty<(uint, int)>();
        var failures = new List<string>();
        Check(failures, "logical admission", () => Assert.Equal(admitted, actual));
        Check(failures, "literal decoded geometry", () => Assert.Equal(
            admitted ? (BitMap, 2, 2, 2, 2, (byte)0, 16) : (0u, 0, 0, 0, 0, (byte)0, 0),
            (bitmap.Address, bitmap.GuestBytesPerRow, bitmap.PlaneBytesPerRow,
                bitmap.Rows, bitmap.Depth, bitmap.Flags, bitmap.Width)));
        Check(failures, "only required fields are read; invalid logical ports read nothing", () =>
            Assert.Equal(expectedReads, fixture.Memory.Reads));
        Check(failures, "truncated LONG fails at the real memory boundary", () => Assert.Equal(
            truncatedPointer ? new[] { (fixture.RastPort + 4, 4, false) } : Array.Empty<(uint, int, bool)>(),
            fixture.Memory.RejectedAccesses));
        Check(failures, "read-only decoder has no publication/probes", () =>
        {
            Assert.Empty(fixture.Memory.Writes);
            Assert.Empty(fixture.Memory.WritableProbes);
            AssertMemoryEqual(before, fixture.Memory.Snapshot());
        });
        Check(failures, "no allocator calls", fixture.AssertNoAllocatorCalls);
        AssertClean(failures, "TryReadBitmap");
    }

    private static void VerifyPixel(Fixture fixture, bool write, bool admitted, string route)
    {
        var before = fixture.Memory.Snapshot();
        var expected = before.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        if (write && admitted)
        {
            // Literal two-plane oracle: x=7/y=1 addresses byte2, bit0. Pen1
            // becomes pen2, preserving all seven other bits and every guard.
            expected[Plane0][2] = 0xA4;
            expected[Plane1][2] = 0x5B;
        }
        var state = CreateState(fixture.RastPort);
        var original = Capture(state);
        fixture.Memory.BeginObservation();
        bool claimed;
        int result;
        if (route == "core")
            claimed = write
                ? fixture.Core.TryWritePixel(fixture.RastPort, 7, 1, out result)
                : fixture.Core.TryReadPixel(fixture.RastPort, 7, 1, out result);
        else
        {
            claimed = fixture.Adapter.TryInvoke(state, write ? -324 : -318, nativeOverlay: route == "overlay");
            result = unchecked((int)state.D[0]);
        }

        var failures = new List<string>();
        Check(failures, "claim/decline", () => Assert.Equal(admitted, claimed));
        Check(failures, "independent pixel result / original decline D0", () => Assert.Equal(
            admitted ? (write ? 0 : 1) : route == "core" ? -1 : unchecked((int)InputX), result));
        if (route != "core")
            Check(failures, "all adapter caller registers, PC, SP, SR and cycles", () =>
                AssertState(original, state, admitted ? (write ? 0u : 1u) : InputX));
        Check(failures, "literal whole-memory pixel publication or unchanged decline", () =>
            AssertMemoryEqual(expected, fixture.Memory.Snapshot()));
        Check(failures, "writes are restricted to the two selected plane bytes", () => Assert.Equal(
            write && admitted ? new[] { (Plane0 + 2, 1), (Plane1 + 2, 1) } : Array.Empty<(uint, int)>(),
            fixture.Memory.Writes));
        Check(failures, "overlay probes plane output, never a writable RP envelope", () => Assert.Equal(
            route == "overlay" && write && admitted
                ? new[] { (Plane0 + 2, 1u), (Plane1 + 2, 1u) } : Array.Empty<(uint, uint)>(),
            fixture.Memory.WritableProbes));
        Check(failures, "no unmapped/tail access", () =>
        {
            Assert.Empty(fixture.Memory.RejectedAccesses);
            Assert.DoesNotContain(fixture.Memory.Reads, read =>
                (ulong)read.Address >= (ulong)fixture.RastPort + 100 &&
                (ulong)read.Address < (ulong)fixture.RastPort + 180);
            if (!admitted)
                Assert.DoesNotContain(fixture.Memory.Reads, read => IsBitmapOrPlane(read.Address));
        });
        Check(failures, "no allocator calls", fixture.AssertNoAllocatorCalls);
        AssertClean(failures, route);
    }

    private static void VerifyOwnerDecline(Fixture fixture, bool write, string owner)
    {
        var before = fixture.Memory.Snapshot();
        var state = CreateState(fixture.RastPort);
        var original = Capture(state);
        fixture.Memory.BeginObservation();
        Assert.False(fixture.Adapter.TryInvoke(state, write ? -324 : -318, nativeOverlay: true));
        AssertState(original, state, InputX);
        AssertMemoryEqual(before, fixture.Memory.Snapshot());
        Assert.Empty(fixture.Memory.Writes);
        Assert.Empty(fixture.Memory.WritableProbes);
        Assert.Empty(fixture.Memory.RejectedAccesses);
        Assert.DoesNotContain(fixture.Memory.Reads, read => IsBitmapOrPlane(read.Address));
        if (owner == "rtg-port") Assert.Contains(fixture.RastPort, fixture.RtgPortQueries);
        if (owner == "rtg-bitmap") Assert.Contains(BitMap, fixture.RtgBitmapQueries);
        fixture.AssertNoAllocatorCalls();
    }

    private static bool IsBitmapOrPlane(uint address)
        => (address >= BitMap && address < BitMap + 40) ||
           (address >= Plane0 && address < Plane0 + 4) ||
           (address >= Plane1 && address < Plane1 + 4);

    private static M68kCpuState CreateState(uint rastPort)
    {
        var state = new M68kCpuState { ProgramCounter = Caller, StatusRegister = 0x2501, Cycles = 823 };
        for (var index = 0; index < 8; index++)
        {
            state.D[index] = 0xD123_4560u + (uint)index;
            state.A[index] = 0xA246_8100u + (uint)index * 0x10u;
        }
        state.D[0] = InputX;
        state.D[1] = InputY;
        state.A[1] = rastPort;
        state.A[6] = GraphicsBase;
        state.A[7] = Stack;
        return state;
    }

    private sealed record CallerState(uint[] D, uint[] A, uint Pc, ushort Sr, long Cycles, long NativeCycles);
    private static CallerState Capture(M68kCpuState state)
        => new(state.D.ToArray(), state.A.ToArray(), state.ProgramCounter, state.StatusRegister,
            state.Cycles, state.NativeCycles);

    private static void AssertState(CallerState before, M68kCpuState after, uint expectedD0)
    {
        var data = before.D.ToArray();
        data[0] = expectedD0;
        Assert.Equal(data, after.D.ToArray());
        Assert.Equal(before.A, after.A.ToArray());
        Assert.Equal(before.Pc, after.ProgramCounter);
        Assert.Equal(before.Sr, after.StatusRegister);
        Assert.Equal(before.Cycles, after.Cycles);
        Assert.Equal(before.NativeCycles, after.NativeCycles);
    }

    private static void AssertMemoryEqual(Dictionary<uint, byte[]> expected, Dictionary<uint, byte[]> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(address => address), actual.Keys.OrderBy(address => address));
        foreach (var (address, bytes) in expected)
            Assert.True(bytes.SequenceEqual(actual[address]), $"Memory/guard at ${address:X8} changed unexpectedly.");
    }

    private static void Check(List<string> failures, string label, Action assertion)
    {
        try { assertion(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { failures.Add(label + ": " + exception.Message); }
    }

    private static void AssertClean(List<string> failures, string context)
        => Assert.True(failures.Count == 0, context + ":\n" + string.Join("\n", failures));
    private static string Backing(bool mapped) => mapped ? "AmigaBus/full32" : "array/full32";

    private sealed class Fixture
    {
        internal Fixture(bool mapped, uint rastPort, string shape = "complete", string owner = "none")
        {
            RastPort = rastPort;
            Memory = new BoundaryMemory(mapped, rastPort, shape, owner == "layer");
            var unused = new UnusedHardware();
            Core = new GraphicsLibraryCore(Memory, Allocator, unused, unused);
            Adapter = new CopperStartGraphicsRegisterAdapter(Core,
                isRtgRastPort: address =>
                {
                    RtgPortQueries.Add(address);
                    return owner == "rtg-port" && address == rastPort;
                },
                isRtgBitMap: address =>
                {
                    RtgBitmapQueries.Add(address);
                    return owner == "rtg-bitmap" && address == BitMap;
                },
                isWritableMemoryRange: Memory.ProbeWritable);
        }

        internal uint RastPort { get; }
        internal BoundaryMemory Memory { get; }
        internal GraphicsLibraryCore Core { get; }
        internal CopperStartGraphicsRegisterAdapter Adapter { get; }
        internal RecordingAllocator Allocator { get; } = new();
        internal List<uint> RtgPortQueries { get; } = new();
        internal List<uint> RtgBitmapQueries { get; } = new();
        internal void AssertNoAllocatorCalls()
        {
            Assert.Equal(0, Allocator.Allocations);
            Assert.Equal(0, Allocator.Frees);
        }
    }

    private sealed class BoundaryMemory : IGraphicsMemory
    {
        private readonly List<Region> _regions = new();
        private readonly AmigaBus? _bus;
        private readonly CopperStartGraphicsMemoryAdapter? _mapped;
        private bool _observing;

        internal BoundaryMemory(bool mapped, uint rastPort, string shape, bool layered)
        {
            if (mapped)
            {
                _bus = new AmigaBus();
                _mapped = new CopperStartGraphicsMemoryAdapter(new HostGuestMemory(_bus));
            }
            var port = Enumerable.Range(0, ClassicRastPortBytes).Select(index => (byte)(index ^ 0xD5)).ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(0, 4), layered ? 0x00D6_0020u : 0u);
            BinaryPrimitives.WriteUInt32BigEndian(port.AsSpan(4, 4), BitMap);
            port[0x18] = 3; // Both destination planes selected.
            port[0x19] = 2; // Foreground pen2.
            port[0x1A] = 0;
            port[0x1C] = 0; // JAM1, no complement or inverse video.
            BinaryPrimitives.WriteUInt16BigEndian(port.AsSpan(0x20, 2), 3); // No private NO_PENS flag.

            var low = Enumerable.Repeat((byte)0xC7, 256).ToArray();
            if (rastPort == 0) port.CopyTo(low, 0); // Coherent NULL decoy must still be rejected before reading.
            Add(0, low, writable: true);
            if (rastPort != 0)
            {
                Add(rastPort - 32, Enumerable.Repeat((byte)0xA5, 32).ToArray(), writable: false);
                if (shape is "pointer-only" or "truncated-pointer")
                    Add(rastPort + 4, port.AsSpan(4, shape == "pointer-only" ? 4 : 3).ToArray(), writable: false);
                else
                {
                    // A wrapping test exposes only bytes physically below the
                    // 32-bit limit. Its bitmap/pen fields still fit, so an
                    // erroneous field-only logical guard would really draw.
                    var count = shape == "readable-99" ? 99 :
                        (int)Math.Min((ulong)ClassicRastPortBytes, (ulong)uint.MaxValue - rastPort + 1);
                    Add(rastPort, port.AsSpan(0, count).ToArray(), writable: shape != "readonly-100");
                    if (shape == "readonly-100")
                        Add(rastPort + 100, Enumerable.Repeat((byte)0x69, 80).ToArray(), writable: false);
                }
                if ((ulong)rastPort + 212 <= 0x1_0000_0000UL)
                    Add(rastPort + 180, Enumerable.Repeat((byte)0x5A, 32).ToArray(), writable: false);
            }

            var bitmap = Enumerable.Repeat((byte)0x96, 40).ToArray();
            BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(0, 2), 2);
            BinaryPrimitives.WriteUInt16BigEndian(bitmap.AsSpan(2, 2), 2);
            bitmap[4] = 0;
            bitmap[5] = 2;
            BinaryPrimitives.WriteUInt32BigEndian(bitmap.AsSpan(8, 4), Plane0);
            BinaryPrimitives.WriteUInt32BigEndian(bitmap.AsSpan(12, 4), Plane1);
            AddGuarded(BitMap, bitmap, writable: false);
            AddGuarded(Plane0, new byte[] { 0x5A, 0x5A, 0xA5, 0x5A }, writable: true);
            AddGuarded(Plane1, new byte[] { 0xA5, 0xA5, 0x5A, 0xA5 }, writable: true);
            AddGuarded(GraphicsBase, Enumerable.Repeat((byte)0x6D, 1024).ToArray(), writable: false);
            AddGuarded(Caller, Enumerable.Repeat((byte)0x4E, 32).ToArray(), writable: false);
            AddGuarded(Stack - 0x300, Enumerable.Repeat((byte)0x87, 0x400).ToArray(), writable: true);

            if (_bus is not null && rastPort != 0)
            {
                Assert.Equal(shape != "truncated-pointer", _bus.IsMappedMemoryRange(rastPort + 4, 4));
                if (shape == "readable-99") Assert.False(_bus.IsMappedMemoryRange(rastPort + 99, 1));
                if (shape == "readonly-100") Assert.False(_bus.IsWritableMemoryRange(rastPort, 100));
                if (shape == "complete" && (ulong)rastPort + 100 <= uint.MaxValue)
                    Assert.False(_bus.IsMappedMemoryRange(rastPort + 100, 1));
            }
        }

        internal List<(uint Address, int Bytes)> Reads { get; } = new();
        internal List<(uint Address, int Bytes)> Writes { get; } = new();
        internal List<(uint Address, uint Bytes)> WritableProbes { get; } = new();
        internal List<(uint Address, int Bytes, bool Write)> RejectedAccesses { get; } = new();
        internal void BeginObservation() => _observing = true;

        private void AddGuarded(uint address, byte[] bytes, bool writable)
        {
            Add(address - 16, Enumerable.Repeat((byte)0xA5, 16).ToArray(), writable: false);
            Add(address, bytes, writable);
            Add(address + (uint)bytes.Length, Enumerable.Repeat((byte)0x5A, 16).ToArray(), writable: false);
        }

        private void Add(uint address, byte[] bytes, bool writable)
        {
            Assert.True((ulong)address + (uint)bytes.Length <= 0x1_0000_0000UL);
            _regions.Add(new Region(address, bytes, writable));
            if (_bus is null) return;
            if (address < 0x0020_0000)
            {
                // Seed actual Chip RAM; a synthetic low mapping would lose
                // precedence to the built-in decode and give a false decoy.
                Assert.True(writable);
                for (var index = 0; index < bytes.Length; index++)
                    Assert.True(_mapped!.TryWriteByte(address + (uint)index, bytes[index]));
            }
            else if (writable) _bus.MapWritableMemory(address, bytes);
            else _bus.MapReadOnlyMemory(address, bytes);
        }

        private Region? Find(uint address, int bytes)
            => _regions.FirstOrDefault(region => address >= region.Address &&
                (ulong)address + (uint)bytes <= (ulong)region.Address + (uint)region.Bytes.Length);

        internal bool ProbeWritable(uint address, uint bytes)
        {
            WritableProbes.Add((address, bytes));
            return bytes <= int.MaxValue && (_bus is not null
                ? _bus.IsWritableMemoryRange(address, (int)bytes)
                : Find(address, (int)bytes) is { Writable: true });
        }

        private bool TryAccess(uint address, int bytes, bool write, out Region region, out int offset)
        {
            if (_observing) (write ? Writes : Reads).Add((address, bytes));
            region = Find(address, bytes)!;
            offset = region is null ? 0 : (int)(address - region.Address);
            // The real-bus path makes its own mapping decision. Do not make a
            // missing test region hide an unexpected low/high guest access.
            var allowed = _bus is not null
                ? _bus.IsMappedMemoryRange(address, bytes) && (!write || _bus.IsWritableMemoryRange(address, bytes))
                : region is not null && (!write || region.Writable);
            if (!allowed && _observing) RejectedAccesses.Add((address, bytes, write));
            return allowed;
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
        internal int Allocations { get; private set; }
        internal int Frees { get; private set; }
        public bool TryAllocate(uint byteCount, GraphicsMemoryClass memoryClass, out uint address)
        {
            Allocations++;
            address = 0;
            return false;
        }
        public void Free(uint address, uint byteCount, GraphicsMemoryClass memoryClass) => Frees++;
    }

    private sealed class UnusedHardware : IGraphicsBlitterBackend, IGraphicsDisplayBackend
    {
        public void Own() => throw new InvalidOperationException("No blitter ownership is in scope.");
        public void Disown() => throw new InvalidOperationException("No blitter ownership is in scope.");
        public void Wait() => throw new InvalidOperationException("No blitter wait is in scope.");
        public void Submit(uint operationAddress) => throw new InvalidOperationException("No blitter submission is in scope.");
        public void PublishView(uint viewAddress) => throw new InvalidOperationException("No display publication is in scope.");
        public void WaitForTopOfFrame() => throw new InvalidOperationException("No display wait is in scope.");
        public void WaitForBeginningOfVerticalBlank(uint viewPortAddress) => throw new InvalidOperationException("No display wait is in scope.");
        public ushort GetBeamPosition() => throw new InvalidOperationException("No beam access is in scope.");
    }
}
