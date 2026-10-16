namespace Copper68k.Tests;

public sealed class M68000BtstPrefetchTests
{
    public static TheoryData<int, ushort, int, int> Cases => new()
    {
        { 0, 0x0810, 12, 0 }, { 1, 0x0810, 12, 0 }, { 2, 0x0810, 12, 0 },
        { 0, 0x0839, 20, 0 }, { 1, 0x0839, 20, 0 }, { 2, 0x0839, 20, 0 },
        { 0, 0x0310, 8, 0 }, { 1, 0x0310, 8, 0 }, { 2, 0x0310, 8, 0 },
        { 0, 0x0339, 16, 0 }, { 1, 0x0339, 16, 0 }, { 2, 0x0339, 16, 0 },
        { 0, 0x0810, 12, 12 }, { 1, 0x0810, 12, 12 }, { 2, 0x0810, 12, 12 },
        { 0, 0x0839, 20, 12 }, { 1, 0x0839, 20, 12 }, { 2, 0x0839, 20, 12 },
        { 0, 0x0310, 8, 12 }, { 1, 0x0310, 8, 12 }, { 2, 0x0310, 8, 12 },
        { 0, 0x0339, 16, 12 }, { 1, 0x0339, 16, 12 }, { 2, 0x0339, 16, 12 }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void MemoryBitTestCompletesItsFinalPrefetchAfterAWaitedRead(int dispatch, ushort opcode, int nominalCycles, int wait)
    {
        var bus = new WaitedReadBus(wait);
        var words = new List<ushort> { opcode };
        if ((opcode & 0x0100) == 0) words.Add(7);
        if ((opcode & 0x3F) == 0x39) words.AddRange([0, 0x2000]);
        var next = 0x1004u + (uint)words.Count * 2;
        words.AddRange([0x4E71, 0x60FE]);
        bus.Words[0x1000] = 0x6002; // prime IR/IRC through an ordinary taken branch
        bus.Words[0x1002] = 0x4E71;
        for (var i = 0; i < words.Count; i++) bus.Words[0x1004u + (uint)i * 2] = words[i];
        var cpu = new M68kInterpreter(bus, (M68kOpcodePlanDispatch)dispatch);
        cpu.Reset(0x1000, 0x3000);
        cpu.State.A[0] = 0x2000;
        cpu.State.D[1] = 7;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        bus.Accesses.Clear();

        var elapsed = cpu.ExecuteInstruction();

        var data = Assert.Single(bus.Accesses.Where(a => a.Address == 0x2000));
        // CE000 and Moira perform this fetch after the operand read even when
        // DTACK has already carried execution past the nominal cycle count.
        var final = Assert.Single(bus.Accesses.Where(a => a.Cycle > data.Cycle));
        Assert.Equal(next + 2, final.Address);
        Assert.Equal(nominalCycles + wait, elapsed);
        Assert.Equal(next, cpu.State.ProgramCounter);
        Assert.Equal(0x271B, cpu.State.StatusRegister);
        var pipeline = ((IM68000PipelineStateTransfer)cpu).ExportM68000PipelineState();
        Assert.Equal(2, pipeline.PrefetchCount);
        Assert.Equal(next, pipeline.PrefetchAddress);
        Assert.Equal(final.Cycle, ((IM68000InterruptRecognition)cpu).LastInterruptSampleCycle);
    }

    private sealed class WaitedReadBus(int wait) : IM68kBus, IM68000BusCycleTiming
    {
        internal Dictionary<uint, ushort> Words { get; } = [];
        internal List<(uint Address, long Cycle)> Accesses { get; } = [];
        public int M68000BusCycleStartDelay => 2;
        public bool RequiresExactM68000PipelineFallback => true;
        public M68000BusAccessTiming GetM68000BusAccessTiming(uint a, M68kOperandSize s, M68kBusAccessKind k, bool w, long requested, long completed)
            => new(completed, completed + 2);
        public byte ReadByte(uint a, ref long c, M68kBusAccessKind k)
        {
            Assert.Equal(0x2000u, a);
            c += wait + 2; Accesses.Add((a, c)); return 0x80;
        }
        public ushort ReadWord(uint a, ref long c, M68kBusAccessKind k)
        { c += 2; Accesses.Add((a, c)); return Words.GetValueOrDefault(a); }
        public uint ReadLong(uint a, ref long c, M68kBusAccessKind k)
            => ((uint)ReadWord(a, ref c, k) << 16) | ReadWord(a + 2, ref c, k);
        public void WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) => throw new InvalidOperationException("BTST must not write");
        public void WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) => throw new InvalidOperationException("BTST must not write");
        public void WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) => throw new InvalidOperationException("BTST must not write");
        public void ResetExternalDevices(long c) { }
    }
}
