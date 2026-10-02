using System.Buffers.Binary;
using Copper68k;
using CopperMod.Amiga.CopperStart.Graphics;
using CopperMod.Amiga.CopperStart.Graphics.Portable;

namespace CopperMod.Amiga.Tests;

public sealed class GraphicsMonitorBorrowedReferenceTests
{
    private const uint Monitor = 0x2000;
    private const uint GraphicsBase = 0x8000;
    private const uint Count = Monitor + GraphicsLayouts.MonitorSpecOpenCount;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostRegisterCloseBalancesBorrowedReferenceButOverlayRetainsNativeOwnership(bool nativeOverlay)
    {
        var memory = new MonitorMemory();
        Seed(memory, 256);
        var before = memory.Bytes.ToArray();
        var backend = new NoDisplayOrBlitter();
        var core = new GraphicsLibraryCore(memory, new NoAllocator(), backend, backend,
            graphicsLibraryBase: GraphicsBase);
        var adapter = new CopperStartGraphicsRegisterAdapter(core);
        var state = new M68kCpuState();
        for (var register = 0; register < 8; register++)
        {
            state.D[register] = 0xD0000000u + (uint)register;
            state.A[register] = 0xA0000000u + (uint)register;
        }
        state.A[0] = Monitor;
        state.A[6] = GraphicsBase;
        var expectedData = state.D.ToArray();
        var expectedAddress = state.A.ToArray();

        Assert.Equal(!nativeOverlay, adapter.TryInvoke(state, (int)GraphicsLvo.CloseMonitor, nativeOverlay));
        if (!nativeOverlay)
        {
            expectedData[0] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(before.AsSpan(GraphicsLayouts.MonitorSpecOpenCount), 255);
        }
        Assert.Equal(expectedData, state.D);
        Assert.Equal(expectedAddress, state.A);
        Assert.Equal(before, memory.Bytes);
        Assert.False(core.IsOwnedMonitorSpec(Monitor));
        Assert.Equal(0, memory.OutOfRangeReads);
        if (nativeOverlay) Assert.Empty(memory.WordWrites);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    [InlineData(65535)]
    public void CloseBorrowedMonitorOnlyConsumesOneReferenceWithoutOwnership(int count)
    {
        var memory = new MonitorMemory();
        Seed(memory, (ushort)count);
        var before = memory.Bytes.ToArray();
        var registry = new GraphicsMonitorOperations.Registry(graphicsLibraryBase: GraphicsBase);

        // Only the MonitorSpec is mapped. List/default/name pointers are poison;
        // a held reference remains closeable after its owner unlinks it.
        Assert.Equal(0, registry.Close(memory, new NoAllocator(), Monitor));
        BinaryPrimitives.WriteUInt16BigEndian(before.AsSpan(GraphicsLayouts.MonitorSpecOpenCount), (ushort)(count - 1));
        Assert.Equal(before, memory.Bytes);
        Assert.False(registry.IsOwned(Monitor));
        Assert.Equal(0u, registry.FindOpenForDisplay(GraphicsModeIds.PalMonitor));
        Assert.Equal(0u, registry.FindOpenForDisplay(GraphicsModeIds.NtscMonitor));
        Assert.Equal(0, memory.OutOfRangeReads);
        Assert.Equal(new[] { Count }, memory.WordWrites);
    }

    [Theory]
    [InlineData("zero-count")]
    [InlineData("type")]
    [InlineData("subsystem")]
    [InlineData("subtype")]
    [InlineData("backlink")]
    [InlineData("null-base")]
    [InlineData("odd-base")]
    [InlineData("wrapped-base")]
    [InlineData("odd-monitor")]
    [InlineData("wrapped-monitor")]
    [InlineData("unreadable-type")]
    [InlineData("unreadable-count")]
    public void CloseBorrowedMonitorDeclinesInvalidAssociationWithoutWrites(string scenario)
    {
        var memory = new MonitorMemory();
        Seed(memory, 256);
        var graphics = GraphicsBase;
        var monitor = Monitor;
        switch (scenario)
        {
            case "zero-count": Assert.True(memory.TryWriteWord(Count, 0)); break;
            case "type": memory.Bytes[GraphicsLayouts.MonitorSpecNodeType] = 17; break;
            case "subsystem": memory.Bytes[GraphicsLayouts.MonitorSpecNodeSubsystem] = 3; break;
            case "subtype": memory.Bytes[GraphicsLayouts.MonitorSpecNodeSubtype] = 5; break;
            case "backlink": Assert.True(memory.TryWriteLong(Monitor + GraphicsLayouts.ExtendedNodeLibrary, GraphicsBase + 2)); break;
            case "null-base": graphics = 0; break;
            case "odd-base": graphics++; break;
            case "wrapped-base": graphics = 0xFFFFFFFE; break;
            case "odd-monitor": monitor++; break;
            case "wrapped-monitor": monitor = 0xFFFFFFFE; break;
            case "unreadable-type": memory.Unreadable = Monitor + GraphicsLayouts.MonitorSpecNodeType; break;
            case "unreadable-count": memory.Unreadable = Count + 1; break;
        }
        memory.WordWrites.Clear();
        var before = memory.Bytes.ToArray();
        var registry = new GraphicsMonitorOperations.Registry(graphicsLibraryBase: graphics);
        Assert.Equal(GraphicsMonitorOperations.CloseFailure, registry.Close(memory, new NoAllocator(), monitor));
        Assert.Equal(before, memory.Bytes);
        Assert.Empty(memory.WordWrites);
        Assert.False(registry.IsOwned(monitor));
        Assert.Equal(0, memory.OutOfRangeReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BorrowedCloseRollsBackRejectedCountWriteAndCanRetry(bool partial)
    {
        var memory = new MonitorMemory();
        Seed(memory, 256); // 0100 -> 00FF exposes a torn high-byte publication.
        var before = memory.Bytes.ToArray();
        memory.RejectWord = true;
        memory.PartialWord = partial;
        var registry = new GraphicsMonitorOperations.Registry(graphicsLibraryBase: GraphicsBase);
        Assert.Equal(GraphicsMonitorOperations.CloseFailure, registry.Close(memory, new NoAllocator(), Monitor));
        Assert.Equal(before, memory.Bytes);
        Assert.False(registry.IsOwned(Monitor));
        memory.RejectWord = false;
        Assert.Equal(0, registry.Close(memory, new NoAllocator(), Monitor));
        BinaryPrimitives.WriteUInt16BigEndian(before.AsSpan(GraphicsLayouts.MonitorSpecOpenCount), 255);
        Assert.Equal(before, memory.Bytes);
        Assert.False(registry.IsOwned(Monitor));
    }

    private static void Seed(MonitorMemory memory, ushort count)
    {
        Array.Fill(memory.Bytes, (byte)0xA5);
        Assert.True(memory.TryWriteByte(Monitor + GraphicsLayouts.MonitorSpecNodeType, 18));
        Assert.True(memory.TryWriteWord(Monitor + GraphicsLayouts.MonitorSpecNodeSubsystem, 0x0204));
        Assert.True(memory.TryWriteLong(Monitor + GraphicsLayouts.ExtendedNodeLibrary, GraphicsBase));
        Assert.True(memory.TryWriteWord(Count, count));
        memory.WordWrites.Clear();
    }

    private sealed class NoAllocator : IGraphicsAllocatorBackend
    {
        public bool TryAllocate(uint size, GraphicsMemoryClass memoryClass, out uint address)
            => throw new InvalidOperationException("A borrowed close must not allocate.");
        public void Free(uint address, uint size, GraphicsMemoryClass memoryClass)
            => throw new InvalidOperationException("A borrowed close must not free.");
    }

    private sealed class NoDisplayOrBlitter : IGraphicsDisplayBackend, IGraphicsBlitterBackend
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

    private sealed class MonitorMemory : IGraphicsMemory
    {
        internal byte[] Bytes { get; } = new byte[GraphicsLayouts.MonitorSpecSize];
        internal List<uint> WordWrites { get; } = new();
        internal uint? Unreadable { get; set; }
        internal bool RejectWord { get; set; }
        internal bool PartialWord { get; set; }
        internal int OutOfRangeReads { get; private set; }

        public bool TryReadByte(uint address, out byte value)
        {
            value = 0;
            if (address < Monitor || address - Monitor >= Bytes.Length)
            {
                OutOfRangeReads++;
                return false;
            }
            if (address == Unreadable) return false;
            value = Bytes[address - Monitor];
            return true;
        }
        public bool TryReadWord(uint address, out ushort value)
        {
            value = 0;
            if (!TryReadByte(address, out var high) || !TryReadByte(address + 1, out var low)) return false;
            value = (ushort)((high << 8) | low);
            return true;
        }
        public bool TryReadLong(uint address, out uint value)
        {
            value = 0;
            if (!TryReadWord(address, out var high) || !TryReadWord(address + 2, out var low)) return false;
            value = ((uint)high << 16) | low;
            return true;
        }
        public bool TryWriteByte(uint address, byte value)
        {
            if (address < Monitor || address - Monitor >= Bytes.Length) return false;
            Bytes[address - Monitor] = value;
            return true;
        }
        public bool TryWriteWord(uint address, ushort value)
        {
            WordWrites.Add(address);
            if (RejectWord)
            {
                if (PartialWord) TryWriteByte(address, (byte)(value >> 8));
                return false;
            }
            return TryWriteByte(address, (byte)(value >> 8)) && TryWriteByte(address + 1, (byte)value);
        }
        public bool TryWriteLong(uint address, uint value)
            => TryWriteWord(address, (ushort)(value >> 16)) && TryWriteWord(address + 2, (ushort)value);
    }
}
