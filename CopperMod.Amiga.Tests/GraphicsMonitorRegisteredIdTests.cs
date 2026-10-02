using System.Buffers.Binary;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsMonitorRegisteredIdTests
{
    private const uint Gfx = 0x1000, Monitor = 0x2000, Default = 0x3000, Database = 0x4000;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var adapter in new[] { false, true })
        foreach (var scenario in new[] { "borrowed", "empty", "opposite", "changed-default", "default-null",
            "default-odd", "default-unreadable", "unreadable-list", "tag", "version", "both-markers", "owner",
            "extent", "short-base", "public-pointer", "null-db", "odd-db", "word-db", "wrap-db",
            "magic", "db-version", "db-size", "ntsc-record", "pal-record", "default-id", "unreadable-slot",
            "odd-node", "wrap-node", "node-type", "node-kind", "backlink", "saturated", "unreadable-count",
            "reject-write", "partial-write", "truncated-descriptor", "unreadable-runtime" })
            yield return new object[] { ntsc, adapter, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void HostRegisteredIdsMatchNativeSelectionWithoutAllocationOrAdoption(bool ntsc, bool adapter, string scenario)
    {
        var memory = new Memory();
        GraphicsLibraryImageLayout.CreateGuestImage(Gfx, 0x27C, 0x420, 0x27C, 40, 68,
            "graphics.library", "graphics.library 40.68", GraphicsLibraryImageProfile.NativePal, true, true)
            .CopyTo(memory.Bytes, (int)Gfx);
        GraphicsDisplayDatabase.CreateNativeDatabaseImage(false, ntsc).CopyTo(memory.Bytes, (int)Database);
        memory.Long(Gfx + 0x250, GraphicsLibraryImageLayout.NativeRuntimeDescriptorValidTag);
        memory.Long(Gfx + 0x258, Gfx);
        memory.Long(Gfx + 0x25C, Database);
        memory.Long(Gfx + 0x260, (uint)GraphicsDisplayDatabase.NativeDatabaseSize);
        memory.Long(Gfx + GraphicsLayouts.GfxBaseDisplayInfoDataBase, Database);
        for (uint offset = 0x264; offset < 0x27C; offset += 4) memory.Long(Gfx + offset, 0xDEADBEEF);
        foreach (var node in new[] { Monitor, Default })
        {
            memory.Bytes.AsSpan((int)node, GraphicsLayouts.MonitorSpecSize).Fill(0xA5);
            memory.Bytes[node + GraphicsLayouts.MonitorSpecNodeType] = 18;
            memory.Word(node + GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0204);
            memory.Long(node + GraphicsLayouts.ExtendedNodeLibrary, Gfx);
            memory.Word(node + GraphicsLayouts.MonitorSpecOpenCount, 255);
            memory.Long(node + GraphicsLayouts.MonitorSpecNodeName, uint.MaxValue);
            memory.Word(node + GraphicsLayouts.MonitorSpecFlags, 0);
        }
        memory.Long(Gfx + GraphicsLayouts.GfxBaseDefaultMonitor, Default);
        // List and node links are deliberately unusable: registration is separate.
        memory.Long(Gfx + GraphicsLayouts.GfxBaseMonitorListHead, 0xDEADBEEF);
        var selectedSlot = Database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 0u : 4u);
        var otherSlot = Database + (uint)GraphicsDisplayDatabase.NativeMonitorRegistrationsOffset + (ntsc ? 4u : 0u);
        memory.Long(selectedSlot, scenario is "empty" or "opposite" or "changed-default" ? 0 : Monitor);
        memory.Long(otherSlot, scenario is "opposite" or "changed-default" ? Monitor : 0);
        if (scenario == "changed-default") memory.Long(Database + (uint)GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, ntsc ? 0x21000u : 0x11000u);
        if (scenario is "default-null" or "default-odd") memory.Long(Gfx + GraphicsLayouts.GfxBaseDefaultMonitor, scenario == "default-null" ? 0 : Default + 1);
        var descriptor = scenario switch { "tag" => 0x250, "version" => 0x254, "owner" => 0x258, "extent" => 0x260, _ => -1 };
        if (descriptor >= 0) memory.Long(Gfx + (uint)descriptor, 0xBAD0BAD0);
        if (scenario == "both-markers") { memory.Long(Gfx + 0x250, 0xBAD0BAD0); memory.Long(Gfx + 0x254, 0xBAD0BAD0); }
        if (scenario == "short-base") memory.Word(Gfx + 0x12, 0x262);
        if (scenario == "public-pointer") memory.Long(Gfx + GraphicsLayouts.GfxBaseDisplayInfoDataBase, Database + 4);
        if (scenario is "null-db" or "odd-db" or "word-db" or "wrap-db")
        {
            var pointer = scenario switch { "null-db" => 0u, "odd-db" => Database + 1, "word-db" => Database + 2, _ => 0xFFFFFFFCu };
            memory.Long(Gfx + 0x25C, pointer); memory.Long(Gfx + GraphicsLayouts.GfxBaseDisplayInfoDataBase, pointer);
        }
        var dbField = scenario switch { "magic" => 0, "db-version" => 4, "db-size" => 8,
            "ntsc-record" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset,
            "pal-record" => GraphicsDisplayDatabase.NativeMonitorPositionsOffset + 12,
            "default-id" => GraphicsDisplayDatabase.NativeDefaultMonitorIdOffset, _ => -1 };
        if (dbField >= 0) memory.Long(Database + (uint)dbField, 0xBAD0BAD0);
        if (scenario is "odd-node" or "wrap-node") memory.Long(selectedSlot, scenario == "odd-node" ? Monitor + 1 : 0xFFFFFFFE);
        if (scenario == "node-type") memory.Bytes[Monitor + GraphicsLayouts.MonitorSpecNodeType] = 17;
        if (scenario == "node-kind") memory.Word(Monitor + GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0205);
        if (scenario == "backlink") memory.Long(Monitor + GraphicsLayouts.ExtendedNodeLibrary, 0);
        if (scenario == "saturated") memory.Word(Monitor + GraphicsLayouts.MonitorSpecOpenCount, ushort.MaxValue);
        memory.Unreadable = scenario switch { "default-unreadable" => Gfx + GraphicsLayouts.GfxBaseDefaultMonitor,
            "unreadable-list" => Gfx + GraphicsLayouts.GfxBaseMonitorListHead, "unreadable-slot" => selectedSlot,
            "unreadable-count" => Monitor + GraphicsLayouts.MonitorSpecOpenCount, "truncated-descriptor" => Gfx + 0x254, _ => null };
        memory.RejectWord = scenario is "reject-write" or "partial-write";
        memory.UnreadableRuntime = scenario == "unreadable-runtime";
        memory.PartialWord = scenario == "partial-write";
        var registry = new GraphicsMonitorOperations.Registry(!ntsc, graphicsLibraryBase: Gfx);
        var backend = new UnusedBackend();
        var core = new GraphicsLibraryCore(memory, new NoAllocator(), backend, backend, graphicsLibraryBase: Gfx);
        var registers = new CopperStartGraphicsRegisterAdapter(core);
        var original = memory.Bytes.ToArray();
        var keys = new uint[] { 0, 4, 0x8000, 0x8004, 0x8020, 0x8024, 0x800, 0x804,
            0x80, 0x84, 0x400, 0x404, 0x8400, 0x8404, 0x8420, 0x8424, 0x440, 0x444,
            0x8440, 0x8444, 0x8460, 0x8464 };
        foreach (var id in keys.SelectMany(key => new[] { key, key | 0x1000u, key | 0x11000u, key | 0x21000u })
            .Concat(new[] { uint.MaxValue, 0xFFFFu, 0xDEADBEEFu }))
        {
            var family = id >> 16;
            if (family == 0) family = (scenario == "changed-default" ? !ntsc : ntsc) ? 1u : 2u;
            var active = (scenario is "opposite" or "changed-default" ? !ntsc : ntsc) ? 1u : 2u;
            var invalidDatabase = descriptor >= 0 || dbField >= 0 || scenario is "both-markers" or "short-base" or "public-pointer" or
                "null-db" or "odd-db" or "word-db" or "wrap-db" or "truncated-descriptor" or "unreadable-runtime";
            var missing = scenario == "empty" || family != active;
            var failed = id == 0 ? scenario is "default-null" or "default-odd" or "default-unreadable" || memory.RejectWord
                : invalidDatabase || family == active && scenario is "unreadable-slot" or "odd-node" or "wrap-node" or
                    "node-type" or "node-kind" or "backlink" or "saturated" or "unreadable-count" or "reject-write" or "partial-write";
            var expected = failed ? 0 : id == 0 ? Default : missing ? 0 : Monitor;
            var claimed = !failed;
            if (id == uint.MaxValue) { expected = 0; claimed = true; }
            if (id is 0xFFFFu or 0xDEADBEEFu) { expected = 0; claimed = false; }
            var anticipated = original.ToArray();
            if (expected != 0) BinaryPrimitives.WriteUInt16BigEndian(anticipated.AsSpan((int)(expected + GraphicsLayouts.MonitorSpecOpenCount), 2), 256);
            if (adapter)
            {
                var state = new M68kCpuState();
                for (var i = 0; i < 8; i++) { state.D[i] = 0xD0000000u + (uint)i; state.A[i] = 0xA0000000u + (uint)i; }
                state.D[0] = id; state.A[1] = 0; state.A[6] = Gfx;
                var data = state.D.ToArray(); var addresses = state.A.ToArray();
                if (claimed) data[0] = expected;
                Assert.Equal(claimed, registers.TryInvoke(state, (int)GraphicsLvo.OpenMonitor));
                Assert.Equal(data, state.D); Assert.Equal(addresses, state.A);
                Assert.False(core.IsOwnedMonitorSpec(Monitor));
            }
            else
            {
                Assert.Equal(expected, registry.Open(memory, new NoAllocator(), 0, id));
                Assert.False(registry.IsOwned(Monitor)); Assert.False(registry.IsOwned(Default));
            }
            // Compare every byte, retaining detailed xUnit diagnostics only on
            // mismatch. Avoid per-element assertion overhead across91 requests.
            if (!anticipated.AsSpan().SequenceEqual(memory.Bytes)) Assert.Equal(anticipated, memory.Bytes);
            if (expected != 0) Assert.Equal(0, adapter ? core.CloseMonitor(expected) : registry.Close(memory, new NoAllocator(), expected));
            if (!original.AsSpan().SequenceEqual(memory.Bytes)) Assert.Equal(original, memory.Bytes);
        }
        if (memory.RejectWord)
        {
            memory.RejectWord = false;
            Assert.Equal(Monitor, registry.Open(memory, new NoAllocator(), 0, ntsc ? 0x11000u : 0x21000u));
            Assert.Equal(0, registry.Close(memory, new NoAllocator(), Monitor));
            Assert.Equal(original, memory.Bytes);
        }
        if (scenario == "borrowed")
        {
            // Internal display discovery must not synthesize an open reference.
            Assert.Equal(Monitor, registry.Open(memory, new NoAllocator(), 0,
                ntsc ? 0x11000u : 0x21000u, initialOpenCount: 0));
            Assert.Equal(original, memory.Bytes);
            Assert.False(registry.IsOwned(Monitor));
        }
    }

    private sealed class NoAllocator : IGraphicsAllocatorBackend
    {
        public bool TryAllocate(uint size, GraphicsMemoryClass memoryClass, out uint address) => throw new InvalidOperationException("ID lookup must not allocate.");
        public void Free(uint address, uint size, GraphicsMemoryClass memoryClass) => throw new InvalidOperationException("ID lookup must not free.");
    }
    private sealed class UnusedBackend : IGraphicsDisplayBackend, IGraphicsBlitterBackend
    {
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
        internal bool RejectWord, PartialWord, UnreadableRuntime;
        private bool Readable(uint address, uint size) => address <= Bytes.Length - size &&
            !(Unreadable >= address && Unreadable < address + size) &&
            !(UnreadableRuntime && address < Gfx + 0x264 && address + size > Gfx + 0x250);
        internal void Long(uint address, uint value) => BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan((int)address), value);
        internal void Word(uint address, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan((int)address), value);
        public bool TryReadByte(uint address, out byte value) { value = Readable(address, 1) ? Bytes[address] : (byte)0; return Readable(address, 1); }
        public bool TryReadWord(uint address, out ushort value) { value = 0; if (!Readable(address, 2)) return false; value = BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan((int)address)); return true; }
        public bool TryReadLong(uint address, out uint value) { value = 0; if (!Readable(address, 4)) return false; value = BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan((int)address)); return true; }
        public bool TryWriteByte(uint address, byte value) { if (address >= Bytes.Length) return false; Bytes[address] = value; return true; }
        public bool TryWriteWord(uint address, ushort value)
        {
            if (address >= Bytes.Length - 1) return false;
            if (RejectWord) { if (PartialWord) Bytes[address] = (byte)(value >> 8); return false; }
            Word(address, value); return true;
        }
        public bool TryWriteLong(uint address, uint value) { if (address >= Bytes.Length - 3) return false; Long(address, value); return true; }
    }
}
