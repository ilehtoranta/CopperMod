using System.Buffers.Binary;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsMonitorInfoRegisteredReadbackTests
{
    private const uint Gfx = 0x1000, Database = 0x4000, Buffer = 0x601;
    public static IEnumerable<object[]> Routes()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var route in new[] { 0, 1, 2 })
        foreach (var changedDefault in new[] { false, true })
            yield return new object[] { ntsc, route, changedDefault };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void RegisteredMonitorInfoReadsRawFamilyDataWithoutOpeningNodes(bool ntsc, int route, bool changedDefault)
    {
        var fixture = new Fixture(ntsc);
        var defaultNtsc = ntsc ^ changedDefault;
        fixture.Memory.Long(Database + (uint)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, defaultNtsc ? 0x11000u : 0x21000u);
        var before = fixture.Memory.Bytes.ToArray();
        foreach (var (key, _) in GraphicsDisplayInfoTransferBoundaryTests.CapturedOcsEcsDepths)
        foreach (var owner in new uint[] { 0, 0x1000, 0x11000, 0x21000 })
        foreach (var handle in new[] { false, true })
        foreach (var size in new uint[] { 16, 17, 20, 21, 24, 80, 81, 88, 96, uint.MaxValue })
        {
            var id = owner | key;
            var selectedNtsc = owner < 0x10000 ? defaultNtsc : owner == 0x11000;
            var expected = Captured(selectedNtsc);
            var count = (int)Math.Min(size, 88u);
            var anticipated = before.ToArray();
            expected.AsSpan(0, count).CopyTo(anticipated.AsSpan((int)Buffer));
            Assert.Equal(count, fixture.Invoke(route, handle ? id == 0 ? 0xFFFFFFFEu : id : 0,
                handle ? 0xDEADBEEFu : id, size));
            EqualBytes(anticipated, fixture.Memory.Bytes);
            before.CopyTo(fixture.Memory.Bytes, 0);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0x2001u)]
    [InlineData(0xFFFFFF60u)]
    [InlineData(0xDEADBEEFu)]
    [InlineData(uint.MaxValue)]
    public void RegisteredMonitorInfoNeverDereferencesItsOpaquePointer(uint pointer)
    {
        foreach (var route in new[] { 0, 1, 2 })
        {
            var fixture = new Fixture(false);
            fixture.Memory.Long(Database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + 4, pointer);
            var expected = Captured(false);
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(16), pointer);
            Assert.Equal(88, fixture.Invoke(route, 0, 0, 88));
            Assert.Equal(expected, fixture.Memory.Bytes.Skip((int)Buffer).Take(88));
        }
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var route in new[] { 0, 1, 2 })
        foreach (var field in new[] { "tag", "version", "owner", "extent", "size", "public-pointer", "null-db", "odd-db", "word-db", "wrap-db", "magic", "db-version", "db-size", "ntsc-id", "pal-id", "default-id", "unreadable-tag" })
            yield return new object[] { route, field };
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void RegisteredMonitorInfoDeclinesInvalidDatabaseWithoutOutputWrites(int route, string field)
    {
        var fixture = new Fixture(false);
        var memory = fixture.Memory;
        var descriptor = field switch { "tag" => 0x250u, "version" => 0x254u, "owner" => 0x258u, "extent" => 0x260u, _ => 0u };
        if (descriptor != 0) memory.Long(Gfx + descriptor, 0xBAD0BAD0);
        if (field == "size") memory.Word(Gfx + 0x12, 0x262);
        if (field == "public-pointer") memory.Long(Gfx + GraphicsLayouts.GfxBaseDisplayInfoDataBase, Database + 4);
        if (field.EndsWith("-db"))
        {
            var pointer = field switch { "null-db" => 0u, "odd-db" => Database + 1, "word-db" => Database + 2, _ => 0xFFFFFFFCu };
            memory.Long(Gfx + 0x25C, pointer); memory.Long(Gfx + GraphicsLayouts.GfxBaseDisplayInfoDataBase, pointer);
        }
        var dbOffset = field switch { "magic" => 0, "db-version" => 4, "db-size" => 8,
            "ntsc-id" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset,
            "pal-id" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 12,
            "default-id" => GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, _ => -1 };
        if (dbOffset >= 0) memory.Long(Database + (uint)dbOffset, 0xBAD0BAD0);
        if (field == "unreadable-tag") memory.Unreadable = Gfx + 0x250;
        var before = memory.Bytes.ToArray();
        Assert.Equal(-1, fixture.Invoke(route, 0, 0, 88));
        Assert.Equal(0, memory.OutputWrites);
        EqualBytes(before, memory.Bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RegisteredMonitorInfoReadsOnlyRequestedFieldsAndSnapshotsBeforeOverlap(int route)
    {
        foreach (var (field, boundary) in new[] {
            ((uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + 4, 16u),
            ((uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 16, 20u),
            ((uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 20, 80u) })
        {
            var fixture = new Fixture(false);
            fixture.Memory.Unreadable = Database + field;
            Assert.Equal((int)boundary, fixture.Invoke(route, 0, 0, boundary));
            var before = fixture.Memory.Bytes.ToArray();
            fixture.Memory.OutputWrites = 0;
            Assert.Equal(-1, fixture.Invoke(route, 0, 0, boundary + 1));
            Assert.Equal(0, fixture.Memory.OutputWrites);
            EqualBytes(before, fixture.Memory.Bytes);
        }
        foreach (var destination in new[] { Database + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset,
                     Gfx + 0x250 })
        {
            var fixture = new Fixture(false);
            fixture.Destination = destination;
            var expected = fixture.Memory.Bytes.ToArray();
            Captured(false).CopyTo(expected, (int)destination);
            Assert.Equal(88, fixture.Invoke(route, 0, 0, 88));
            EqualBytes(expected, fixture.Memory.Bytes);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RegisteredMonitorInfoRestoresOutputAfterFailedOrPartialWrites(int route)
    {
        foreach (var failAt in new[] { 1, 88, 89, 100, 176 })
        {
            var fixture = new Fixture(false);
            fixture.Memory.FailWrite = failAt;
            var before = fixture.Memory.Bytes.ToArray();
            Assert.Equal(route == 0 ? 0 : -1, fixture.Invoke(route, 0, 0, 88));
            EqualBytes(before, fixture.Memory.Bytes);
            Assert.Equal(88, fixture.Invoke(route, 0, 0, 88));
            Assert.Equal(Captured(false), fixture.Memory.Bytes.Skip((int)Buffer).Take(88));
        }
    }

    private static byte[] Captured(bool ntsc)
    {
        var result = GraphicsMonitorInfoFamilyRecordTests.Baseline(ntsc);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(16), ntsc ? 1u : 0xDEADBEEFu);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20), ntsc ? 0x000AFFF6u : 0x80007FFFu);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(80), ntsc ? 0xFFF90009u : 0xFFFF0001u);
        return result;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MonitorInfoEmptyQueriesNeedNoNativeStateAndCompactHeadersDoNotPublish(int route)
    {
        foreach (var scenario in new[] { "null", "empty", "invalid-id", "wrap", "unknown" })
        {
            var fixture = new Fixture(false);
            fixture.Memory.RejectAllReads = true;
            if (scenario == "null") fixture.Destination = 0;
            if (scenario == "wrap") fixture.Destination = 0xFFFFFFA9;
            var before = fixture.Memory.Bytes.ToArray();
            var expected = scenario == "unknown" || scenario == "wrap" && route != 0 ? -1 : 0;
            Assert.Equal(expected, fixture.Invoke(route, 0,
                scenario == "invalid-id" ? uint.MaxValue : scenario == "unknown" ? 0xDEADBEEFu : 0x1000u,
                scenario == "empty" ? 0u : 88u));
            EqualBytes(before, fixture.Memory.Bytes);
        }
        foreach (var useHandle in new[] { false, true })
        {
            var fixture = new Fixture(false);
            fixture.Memory.Long(Gfx + 0x250, 0);
            fixture.Memory.Long(Gfx + 0x254, 0);
            Assert.Equal(16, fixture.Invoke(route, useHandle ? 0x1000u : 0u,
                useHandle ? 0xDEADBEEFu : 0x1000u, 16));
            Assert.Equal(GraphicsMonitorInfoFamilyRecordTests.Baseline(false).Take(16),
                fixture.Memory.Bytes.Skip((int)Buffer).Take(16));
        }
    }

    [Theory]
    [InlineData(16u)]
    [InlineData(17u)]
    [InlineData(20u)]
    [InlineData(21u)]
    [InlineData(80u)]
    [InlineData(81u)]
    public void CompactMonitorInfoProvidersAreFieldGranular(uint size)
    {
        var memory = new Memory();
        var pointerCalls = 0; var currentCalls = 0; var originalCalls = 0;
        Assert.Equal((int)size, GraphicsDisplayDatabase.GetDisplayInfoData(memory, 0x1000,
            Buffer, size, 0x80002000, 0xDEADBEEF,
            monitorSpecProvider: _ => { pointerCalls++; return 1; },
            monitorPositionProvider: _ => { currentCalls++; return new(-1, 2); },
            monitorOriginalPositionProvider: _ => { originalCalls++; return new(3, -4); }));
        Assert.Equal(size > 16 ? 1 : 0, pointerCalls);
        Assert.Equal(size > 20 ? 1 : 0, currentCalls);
        Assert.Equal(size > 80 ? 1 : 0, originalCalls);
    }

    private static void EqualBytes(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual)) Assert.Equal(expected, actual);
    }

    private sealed class Fixture
    {
        internal Memory Memory { get; } = new();
        internal uint Destination = Buffer;
        private readonly GraphicsLibraryCore _core;
        private readonly CopperStartGraphicsRegisterAdapter _adapter;
        internal Fixture(bool ntsc)
        {
            GraphicsLibraryImageLayout.CreateGuestImage(Gfx, 0x27C, 0x420, 0x27C, 40, 68,
                "graphics.library", "graphics.library 40.68", GraphicsLibraryImageProfile.NativePal, true, true)
                .CopyTo(Memory.Bytes, (int)Gfx);
            GraphicsDisplayDatabase.CreateNativeDatabaseImage(false, ntsc).CopyTo(Memory.Bytes, (int)Database);
            Memory.Long(Gfx + 0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
            Memory.Long(Gfx + 0x258, Gfx); Memory.Long(Gfx + 0x25C, Database);
            Memory.Long(Gfx + 0x260, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
            Memory.Long(Gfx + GraphicsLayouts.GfxBaseDisplayInfoDataBase, Database);
            // Unreadable default/list/node memory cannot affect a data query.
            Memory.Long(Gfx + GraphicsLayouts.GfxBaseDefaultMonitor, 0x2000);
            Memory.Long(Gfx + GraphicsLayouts.GfxBaseMonitorListHead, 0x2000);
            foreach (var isNtsc in new[] { true, false })
            {
                var record = Captured(isNtsc);
                var index = isNtsc ? 0u : 1u;
                Memory.Long(Database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + index * 4,
                    BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(16)));
                var point = Database + (uint)GraphicsDisplayDatabase.NativeMonitorPositionsOffset + index * 12 + 4;
                Memory.Long(point, BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(20)));
                Memory.Long(point + 4, BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(80)));
            }
            var backend = new Backend(ntsc);
            _core = new GraphicsLibraryCore(Memory, new NoAllocator(), backend, backend, graphicsLibraryBase: Gfx);
            _adapter = new CopperStartGraphicsRegisterAdapter(_core,
                isWritableMemoryRange: (address, size) => address >= Destination && (ulong)address + size <= (ulong)Destination + 88);
        }
        internal int Invoke(int route, uint handle, uint id, uint size)
        {
            if (route == 0) return _core.GetDisplayInfoData(handle, Destination, size, 0x80002000, id);
            var state = new M68kCpuState();
            for (var i = 0; i < 8; i++) { state.D[i] = 0xD0000000u + (uint)i; state.A[i] = 0xA0000000u + (uint)i; }
            state.D[0] = size; state.D[1] = 0x80002000; state.D[2] = id;
            state.A[0] = handle; state.A[1] = Destination; state.A[6] = Gfx;
            var data = state.D.ToArray(); var addresses = state.A.ToArray();
            var claimed = _adapter.TryInvoke(state, (int)GraphicsLvo.GetDisplayInfoData, nativeOverlay: route == 2);
            if (claimed) data[0] = Destination == 0 || handle == 0 && id == uint.MaxValue ? 0 : Math.Min(size, 88u);
            Assert.Equal(data, state.D); Assert.Equal(addresses, state.A);
            return claimed ? (int)state.D[0] : -1;
        }
    }
    private sealed class NoAllocator : IGraphicsAllocatorBackend
    {
        public bool TryAllocate(uint size, GraphicsMemoryClass memoryClass, out uint address) => throw new InvalidOperationException("Readback allocated.");
        public void Free(uint address, uint size, GraphicsMemoryClass memoryClass) => throw new InvalidOperationException("Readback freed.");
    }
    private sealed class Backend(bool ntsc) : IGraphicsDisplayBackend, IGraphicsBlitterBackend, IGraphicsDisplayProfileBackend
    {
        public bool IsNtsc => ntsc;
        public void PublishView(uint address) => throw new InvalidOperationException();
        public void WaitForTopOfFrame() => throw new InvalidOperationException();
        public void WaitForBeginningOfVerticalBlank(uint address) => throw new InvalidOperationException();
        public ushort GetBeamPosition() => throw new InvalidOperationException();
        public void Own() => throw new InvalidOperationException();
        public void Disown() => throw new InvalidOperationException();
        public void Wait() => throw new InvalidOperationException();
        public void Submit(uint address) => throw new InvalidOperationException();
    }
    private sealed class Memory : IGraphicsMemory
    {
        internal byte[] Bytes { get; } = new byte[0x8000];
        internal uint? Unreadable;
        internal bool RejectAllReads;
        internal int OutputWrites, FailWrite;
        internal void Long(uint address, uint value) => BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan((int)address), value);
        internal void Word(uint address, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan((int)address), value);
        private bool Readable(uint address, uint count)
        {
            Assert.False(RejectAllReads, "Empty/invalid query read native state.");
            Assert.False(address >= 0x2000 && address < Database, "Query dereferenced a MonitorSpec.");
            Assert.NotEqual(Gfx + GraphicsLayouts.GfxBaseDefaultMonitor, address);
            Assert.NotEqual(Gfx + GraphicsLayouts.GfxBaseMonitorListHead, address);
            return address <= Bytes.Length - count && !(Unreadable >= address && Unreadable < (ulong)address + count);
        }
        public bool TryReadByte(uint address, out byte value) { var ok = Readable(address, 1); value = ok ? Bytes[address] : (byte)0; return ok; }
        public bool TryReadWord(uint address, out ushort value) { value = 0; if (!Readable(address, 2)) return false; value = BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan((int)address)); return true; }
        public bool TryReadLong(uint address, out uint value) { value = 0; if (!Readable(address, 4)) return false; value = BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan((int)address)); return true; }
        public bool TryWriteByte(uint address, byte value)
        {
            if (address >= Bytes.Length) return false;
            OutputWrites++;
            Bytes[address] = value; // failure may happen after a partial guest write
            if (FailWrite != 0 && OutputWrites == FailWrite) { FailWrite = 0; return false; }
            return true;
        }
        public bool TryWriteWord(uint address, ushort value) => throw new InvalidOperationException("Query wrote a WORD.");
        public bool TryWriteLong(uint address, uint value) => throw new InvalidOperationException("Query wrote a LONG.");
    }
}
