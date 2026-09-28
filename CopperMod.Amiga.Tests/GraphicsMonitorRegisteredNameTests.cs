using System.Buffers.Binary;
using System.Text;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsMonitorRegisteredNameTests
{
    private const uint Gfx = 0x1000, First = 0x2000, Second = 0x2200, Default = 0x2400, Request = 0x3000;

    [Theory]
    [InlineData("absent")]
    [InlineData("tail")]
    [InlineData("tailpred")]
    [InlineData("partial")]
    public void HostMonitorAdapterDistinguishesCompleteAbsenceFromProviderDecline(string scenario)
    {
        var memory = new Memory();
        var list = Gfx + GraphicsLayouts.GfxBaseMonitorList;
        memory.Long(list, list + 4);
        memory.Long(list + 4, scenario == "tail" ? 1u : 0u);
        memory.Long(list + 8, scenario == "tailpred" ? 0u : list);
        if (scenario == "partial") memory.Unreadable = list + 12;
        memory.Ascii(Request, "absent.monitor");
        var before = memory.Bytes.ToArray();
        var backend = new UnusedDisplayBackend();
        var core = new GraphicsLibraryCore(memory, new NoAllocator(), backend, backend, graphicsLibraryBase: Gfx);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        foreach (var id in new[] { 0u, uint.MaxValue, 0xDEADBEEFu })
        {
            var state = new M68kCpuState();
            for (var register = 0; register < 8; register++)
            {
                state.D[register] = 0xD0000000u + (uint)register;
                state.A[register] = 0xA0000000u + (uint)register;
            }
            state.D[0] = id; state.A[1] = Request; state.A[6] = Gfx;
            var expectedData = state.D.ToArray();
            var expectedAddress = state.A.ToArray();
            if (scenario == "absent") expectedData[0] = 0;
            Assert.Equal(scenario == "absent", adapter.TryInvoke(state, (int)GraphicsLvo.OpenMonitor));
            Assert.Equal(expectedData, state.D);
            Assert.Equal(expectedAddress, state.A);
            Assert.Equal(before, memory.Bytes);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var ntsc in new[] { false, true })
        foreach (var scenario in new[] { "second", "duplicate", "reverse", "canonical-duplicate",
            "alias-collision", "upper-alias", "empty-name", "no-default", "missing", "case",
            "null-head", "odd-head", "wrap-head", "tail", "type", "pad", "pred", "cycle",
            "node-type", "kind", "backlink", "null-name", "saturated", "empty-list",
            "renamed", "high-byte", "unterminated", "reject-write", "partial-write",
            "alias-unreadable-list", "alias-invalid-list", "partial-header", "partial-default", "null-default-alias" })
            yield return new object[] { ntsc, scenario };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void HostMonitorNamesFollowPublicListOrderWithoutAllocatingOrAdopting(bool ntsc, string scenario)
    {
        var memory = new Memory();
        var canonical = ntsc ? "ntsc.monitor" : "pal.monitor";
        Seed(First, "first.monitor"); Seed(Second, "second.monitor"); Seed(Default, canonical);
        List(First, Second, Default);
        memory.Long(Gfx + GraphicsLayouts.GfxBaseDefaultMonitor, Default);
        var requested = "second.monitor";
        uint expected = Second;
        switch (scenario)
        {
            case "duplicate": Name(First, requested); expected = First; break;
            case "reverse": Name(First, requested); List(Second, First, Default); break;
            case "canonical-duplicate": requested = canonical; Name(First, canonical); expected = First; break;
            case "alias-collision": requested = "default.monitor"; Name(First, requested); expected = Default; break;
            case "upper-alias": requested = "DEFAULT.MONITOR"; Name(First, "default.monitor"); expected = Default; break;
            case "empty-name": requested = ""; Name(Second, ""); break;
            case "no-default": memory.Long(Gfx + GraphicsLayouts.GfxBaseDefaultMonitor, 0); break;
            case "missing": requested = "absent.monitor"; expected = 0; break;
            case "case": requested = "SECOND.MONITOR"; expected = 0; break;
            case "null-head": memory.Long(Gfx + GraphicsLayouts.GfxBaseMonitorListHead, 0); expected = 0; break;
            case "odd-head": memory.Long(Gfx + GraphicsLayouts.GfxBaseMonitorListHead, First + 1); expected = 0; break;
            case "wrap-head": memory.Long(Gfx + GraphicsLayouts.GfxBaseMonitorListHead, 0xFFFFFFFE); expected = 0; break;
            case "tail": memory.Long(Gfx + GraphicsLayouts.GfxBaseMonitorListTail, 2); expected = 0; break;
            case "type": memory.Bytes[Gfx + GraphicsLayouts.GfxBaseMonitorListType] = 1; expected = 0; break;
            case "pad": memory.Bytes[Gfx + GraphicsLayouts.GfxBaseMonitorListPad] = 1; expected = 0; break;
            case "pred": memory.Long(First + 4, 0); expected = 0; break;
            case "cycle": memory.Long(First, First); expected = 0; break;
            case "node-type": memory.Bytes[First + GraphicsLayouts.MonitorSpecNodeType] = 17; expected = 0; break;
            case "kind": memory.Word(First + GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0205); expected = 0; break;
            case "backlink": memory.Long(First + GraphicsLayouts.ExtendedNodeLibrary, Gfx + 2); expected = 0; break;
            case "null-name": memory.Long(First + GraphicsLayouts.MonitorSpecNodeName, 0); expected = 0; break;
            case "saturated": memory.Word(Second + GraphicsLayouts.MonitorSpecOpenCount, ushort.MaxValue); expected = 0; break;
            case "empty-list": List(); requested = canonical; expected = 0; break;
            case "renamed": Name(Default, "renamed.monitor"); requested = canonical; expected = 0; break;
            case "high-byte": requested = "?econd.monitor"; memory.Bytes[Second + 0xA0] = 0xE9; expected = 0; break;
            case "unterminated": requested = new string('x', 64); Name(First, requested); expected = 0; break;
            case "reject-write": memory.RejectWord = true; expected = 0; break;
            case "partial-write": memory.RejectWord = memory.PartialWord = true; expected = 0; break;
            case "alias-unreadable-list": requested = "DEFAULT.MONITOR"; memory.Unreadable = Gfx + GraphicsLayouts.GfxBaseMonitorListHead; expected = Default; break;
            case "alias-invalid-list": requested = "default.monitor"; memory.Long(Gfx + GraphicsLayouts.GfxBaseMonitorListTail, 1); expected = Default; break;
            case "partial-header": requested = canonical; memory.Unreadable = Gfx + GraphicsLayouts.GfxBaseMonitorListPad; expected = 0; break;
            case "partial-default": requested = "default.monitor"; memory.Unreadable = Gfx + GraphicsLayouts.GfxBaseDefaultMonitor; expected = 0; break;
            case "null-default-alias": requested = "default.monitor"; memory.Long(Gfx + GraphicsLayouts.GfxBaseDefaultMonitor, 0); expected = 0; break;
        }
        memory.Ascii(Request, requested);
        var original = memory.Bytes.ToArray();
        var registry = new GraphicsMonitorOperations.Registry(ntsc, graphicsLibraryBase: Gfx);
        foreach (var id in new[] { 0u, uint.MaxValue, 0xDEADBEEFu })
        {
            var anticipated = original.ToArray();
            if (expected != 0)
                BinaryPrimitives.WriteUInt16BigEndian(anticipated.AsSpan((int)(expected + GraphicsLayouts.MonitorSpecOpenCount)), 256);
            Assert.Equal(expected, registry.Open(memory, new NoAllocator(), Request, id));
            Assert.Equal(anticipated, memory.Bytes);
            Assert.False(registry.IsOwned(First)); Assert.False(registry.IsOwned(Second)); Assert.False(registry.IsOwned(Default));
            Assert.Equal(0u, registry.FindOpenForDisplay(GraphicsModeIds.PalMonitor));
            Assert.Equal(0u, registry.FindOpenForDisplay(GraphicsModeIds.NtscMonitor));
            if (expected != 0) Assert.Equal(0, registry.Close(memory, new NoAllocator(), expected));
            Assert.Equal(original, memory.Bytes);
        }
        if (memory.RejectWord)
        {
            memory.RejectWord = false;
            Assert.Equal(Second, registry.Open(memory, new NoAllocator(), Request, 0));
            Assert.Equal(0, registry.Close(memory, new NoAllocator(), Second));
            Assert.Equal(original, memory.Bytes);
        }

        void Name(uint node, string value)
        {
            memory.Long(node + GraphicsLayouts.MonitorSpecNodeName, node + 0xA0);
            memory.Ascii(node + 0xA0, value);
        }
        void Seed(uint node, string name)
        {
            memory.Bytes.AsSpan((int)node, GraphicsLayouts.MonitorSpecSize).Fill(0xA5);
            memory.Bytes[node + GraphicsLayouts.MonitorSpecNodeType] = 18;
            memory.Word(node + GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0204);
            memory.Long(node + GraphicsLayouts.ExtendedNodeLibrary, Gfx);
            memory.Word(node + GraphicsLayouts.MonitorSpecOpenCount, 255);
            Name(node, name);
        }
        void List(params uint[] nodes)
        {
            var list = Gfx + GraphicsLayouts.GfxBaseMonitorList;
            memory.Long(list, nodes.Length == 0 ? list + 4 : nodes[0]);
            memory.Long(list + 4, 0);
            memory.Long(list + 8, nodes.Length == 0 ? list : nodes[^1]);
            memory.Word(list + 12, 0);
            for (var index = 0; index < nodes.Length; index++)
            {
                memory.Long(nodes[index], index + 1 == nodes.Length ? list + 4 : nodes[index + 1]);
                memory.Long(nodes[index] + 4, index == 0 ? list : nodes[index - 1]);
            }
        }
    }

    private sealed class NoAllocator : IGraphicsAllocatorBackend
    {
        public bool TryAllocate(uint size, GraphicsMemoryClass memoryClass, out uint address)
            => throw new InvalidOperationException("Registered name lookup must not allocate.");
        public void Free(uint address, uint size, GraphicsMemoryClass memoryClass)
            => throw new InvalidOperationException("Registered name lookup must not free.");
    }

    private sealed class UnusedDisplayBackend : IGraphicsDisplayBackend, IGraphicsBlitterBackend
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
        internal byte[] Bytes { get; } = new byte[0x4000];
        internal bool RejectWord, PartialWord;
        internal uint? Unreadable;
        private bool Readable(uint address, uint length)
            => address <= Bytes.Length - length && !(Unreadable >= address && Unreadable < address + length);
        internal void Long(uint address, uint value) => BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan((int)address), value);
        internal void Word(uint address, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan((int)address), value);
        internal void Ascii(uint address, string value) => Encoding.ASCII.GetBytes(value + '\0').CopyTo(Bytes, (int)address);
        public bool TryReadByte(uint address, out byte value)
        { value = Readable(address, 1) ? Bytes[address] : (byte)0; return Readable(address, 1); }
        public bool TryReadWord(uint address, out ushort value)
        { value = 0; if (!Readable(address, 2)) return false; value = BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan((int)address)); return true; }
        public bool TryReadLong(uint address, out uint value)
        { value = 0; if (!Readable(address, 4)) return false; value = BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan((int)address)); return true; }
        public bool TryWriteByte(uint address, byte value)
        { if (address >= Bytes.Length) return false; Bytes[address] = value; return true; }
        public bool TryWriteWord(uint address, ushort value)
        {
            if (address >= Bytes.Length - 1) return false;
            if (RejectWord) { if (PartialWord) Bytes[address] = (byte)(value >> 8); return false; }
            Word(address, value); return true;
        }
        public bool TryWriteLong(uint address, uint value)
        { if (address >= Bytes.Length - 3) return false; Long(address, value); return true; }
    }
}
