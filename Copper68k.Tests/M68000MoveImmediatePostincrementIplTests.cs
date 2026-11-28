namespace Copper68k.Tests;

public sealed class M68000MoveImmediatePostincrementIplTests
{
    [Theory]
    [InlineData(0, 0x10FC)]
    [InlineData(1, 0x10FC)]
    [InlineData(2, 0x10FC)]
    [InlineData(0, 0x30FC)]
    [InlineData(1, 0x30FC)]
    [InlineData(2, 0x30FC)]
    public void SamplesBeforeOperandRefillAndWaitedWrite(int dispatch, ushort opcode)
    {
        var bus = new WaitedWriteBus();
        bus.Words[0x1000] = 0x6002; // Taken branch fills the target instruction queue.
        bus.Words[0x1004] = opcode;
        bus.Words[0x1006] = 0x0012;
        bus.Words[0x1008] = 0x4E71;
        bus.Words[0x100A] = 0x60FE;
        var cpu = new M68kInterpreter(bus, (M68kOpcodePlanDispatch)dispatch);
        cpu.Reset(0x1000, 0x3000);
        cpu.State.A[0] = 0x2000;
        cpu.State.StatusRegister = 0x2000;
        cpu.ExecuteInstruction();
        var previousReady = bus.Accesses[^1].Cycle;
        bus.Accesses.Clear();

        cpu.ExecuteInstruction();

        Assert.Equal(new uint[] { 0x1008, 0x2000, 0x100A }, bus.Accesses.Select(a => a.Address));
        Assert.Equal((ushort)0x12, bus.WrittenValue);
        Assert.Equal(opcode == 0x10FC ? 0x2001u : 0x2002u, cpu.State.A[0]);
        Assert.Equal(0x1008u, cpu.State.ProgramCounter);
        var recognition = (IM68000InterruptRecognition)cpu;
        Assert.Equal(previousReady, recognition.LastInterruptSampleCycle);
        Assert.False(recognition.HasRecognizedInterrupt(6, bus.Accesses[0].Cycle, 0));
    }

    private sealed class WaitedWriteBus : IM68kBus, IM68000BusCycleTiming
    {
        internal Dictionary<uint, ushort> Words { get; } = [];
        internal List<(uint Address, long Cycle)> Accesses { get; } = [];
        internal ushort WrittenValue { get; private set; }
        public int M68000BusCycleStartDelay => 2;
        public bool RequiresExactM68000PipelineFallback => true;

        public M68000BusAccessTiming GetM68000BusAccessTiming(
            uint address, M68kOperandSize size, M68kBusAccessKind kind, bool write, long requested, long completed)
            => new(completed, completed + 2);

        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind)
        {
            cycle += 2;
            Accesses.Add((address, cycle));
            return Words.GetValueOrDefault(address);
        }

        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind)
            => throw new InvalidOperationException("Unexpected byte read.");

        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
            => ((uint)ReadWord(address, ref cycle, kind) << 16) | ReadWord(address + 2, ref cycle, kind);

        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind)
            => WriteWord(address, value, ref cycle, kind);

        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
        {
            cycle += 14;
            Accesses.Add((address, cycle));
            WrittenValue = value;
        }

        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
            => throw new InvalidOperationException("Unexpected longword write.");

        public void ResetExternalDevices(long cycle) { }
    }
}
