using Copper68k;

namespace Copper68k.Tests;

public sealed class M68040UncachedFetchTests
{
    // MC68040UM table 7-3 and its following paragraph: noncachable instruction accesses
    // start with a longword at the half-line base, including second-half targets.
    [Theory]
    [InlineData(0x1000u, new uint[] { 0x1000 })]
    [InlineData(0x1002u, new uint[] { 0x1000 })]
    [InlineData(0x1004u, new uint[] { 0x1000, 0x1004 })]
    [InlineData(0x1006u, new uint[] { 0x1000, 0x1004 })]
    [InlineData(0x1008u, new uint[] { 0x1008 })]
    public void DisabledCacheFetchStartsAtHalfLineBase(uint pc, uint[] addresses)
    {
        var bus = new FetchBus();
        bus.Memory.WriteWords(pc, 0x7207); // MOVEQ #7,D1
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(pc, 0x8000);
        cpu.ExecuteInstruction();
        Assert.Equal(7u, cpu.State.D[1]);
        Assert.Equal(addresses, bus.Fetches.Select(f => f.Address));
        Assert.All(bus.Fetches, f => Assert.Equal(4, f.Bytes));
        Assert.Equal(0u, cpu.State.CacheControlRegister);
    }

    // MC68040UM 4.2: a loop wholly within the first three words of a
    // half-line stays in the holding register even with CACR.IE clear.
    [Theory]
    [InlineData(0x1000u)]
    [InlineData(0x1002u)]
    public void ShortLoopRetainsInstructionsWithCacheDisabled(uint pc)
    {
        var bus = new FetchBus();
        bus.Memory.WriteWords(pc, 0x5280, 0x60FC); // ADDQ.L #1,D0; BRA.S loop
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(pc, 0x8000);
        cpu.ExecuteInstruction(); cpu.ExecuteInstruction();
        var fetchCount = bus.Fetches.Count;
        bus.Memory.WriteWord(pc, 0x5480); // External write: ADDQ #2 must stay stale.
        for (var i = 0; i < 20; i++) cpu.ExecuteInstruction();
        Assert.Equal(11u, cpu.State.D[0]);
        Assert.Equal(fetchCount, bus.Fetches.Count);
    }

    [Fact]
    public void LeavingHalfLineAndResetMakeModifiedCodeVisible()
    {
        var bus = new FetchBus();
        bus.Memory.WriteWords(0x1000, 0x7201, 0x60FC);
        bus.Memory.WriteWords(0x1100, 0x7400);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(0x1000, 0x8000); cpu.ExecuteInstruction();
        bus.Memory.WriteWord(0x1000, 0x7207);
        cpu.State.ProgramCounter = 0x1100; cpu.ExecuteInstruction();
        cpu.State.ProgramCounter = 0x1000; cpu.ExecuteInstruction();
        Assert.Equal(7u, cpu.State.D[1]);
        bus.Memory.WriteWord(0x1000, 0x7209);
        cpu.Reset(0x1000, 0x8000); cpu.ExecuteInstruction();
        Assert.Equal(9u, cpu.State.D[1]);
    }

    [Fact]
    public void HoldingRegisterCannotCrossTranslationOrPrivilegeContexts()
    {
        var bus = new FetchBus();
        bus.Memory.WriteWords(0x2000, 0x7201);
        bus.Memory.WriteWords(0x3000, 0x7203);
        bus.Memory.WriteLong(0x4004, 0x2001);
        bus.Memory.WriteLong(0x5004, 0x3001);
        bus.Memory.WriteLong(0x6004, 0x2001);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(0x1000, 0x8000);
        cpu.State.M68040Mmu.SupervisorRootPointer = 0x4000;
        cpu.State.M68040Mmu.UserRootPointer = 0x5000;
        cpu.State.M68040Mmu.TranslationControl = 0x80000000;
        cpu.ExecuteInstruction(); Assert.Equal(1u, cpu.State.D[1]);
        cpu.State.StatusRegister = 0;
        cpu.State.ProgramCounter = 0x1000;
        cpu.ExecuteInstruction(); Assert.Equal(3u, cpu.State.D[1]);
        cpu.State.M68040Mmu.UserRootPointer = 0x6000;
        cpu.State.ProgramCounter = 0x1000;
        cpu.ExecuteInstruction(); Assert.Equal(1u, cpu.State.D[1]);
        Assert.Equal(new uint[] { 0x2000, 0x3000, 0x2000 }, bus.Fetches.Select(f => f.Address));
    }

    [Theory]
    [InlineData(0x1000u, 4)] // Fits within the first six bytes.
    [InlineData(0x1002u, 4)]
    [InlineData(0x1004u, 4)] // Last-word branch: holding register must be replaced.
    [InlineData(0x1006u, 4)] // Crosses the half-line.
    [InlineData(0x1004u, 2)] // Self branch exercises the specialized hot block.
    [InlineData(0x1006u, 2)]
    public void BatchAndScalarUseTheSameUncachedFetches(uint pc, int loopBytes)
    {
        var scalarBus = new FetchBus(); var batchBus = new FetchBus();
        var words = loopBytes == 2 ? new ushort[] { 0x60FE } : new ushort[] { 0x5280, 0x60FC };
        scalarBus.Memory.WriteWords(pc, words); batchBus.Memory.WriteWords(pc, words);
        using var scalar = new M68040Interpreter(scalarBus);
        using var batch = new M68040Interpreter(batchBus);
        scalar.Reset(pc, 0x8000); batch.Reset(pc, 0x8000);
        for (var i = 0; i < 40; i++) scalar.ExecuteInstruction();
        Assert.Equal(40, batch.ExecuteInstructions(40, null, new Boundary()));
        Assert.Equal(scalar.State.D, batch.State.D);
        Assert.Equal(scalar.State.ProgramCounter, batch.State.ProgramCounter);
        Assert.Equal(scalar.State.Cycles, batch.State.Cycles);
        Assert.Equal(scalar.State.NativeCycles, batch.State.NativeCycles);
        Assert.Equal(scalarBus.Fetches, batchBus.Fetches);
        if ((pc & 7) + loopBytes <= 6) Assert.InRange(batchBus.Fetches.Count, 1, 2);
        else Assert.True(batchBus.Fetches.Count > 2);
    }

    private sealed class Boundary : IM68kInstructionBoundary
    {
        public bool BeforeInstruction() => true;
        public void AfterInstruction(long previousCycle, long currentCycle) { }
    }

    [Theory]
    [InlineData(0xF498, 7u)] // CINVA IC
    [InlineData(0xF4B8, 7u)] // CPUSHA IC
    [InlineData(0xF458, 1u)] // CINVA DC leaves held instructions alone.
    public void CacheMaintenanceSelectsTheInstructionHoldingRegister(int opcode, uint expected)
    {
        var bus = new FetchBus();
        bus.Memory.WriteWords(0x1000, 0x7201, 0x4E71, (ushort)opcode);
        using var cpu = new M68040Interpreter(bus);
        cpu.Reset(0x1000, 0x8000); cpu.ExecuteInstruction();
        bus.Memory.WriteWord(0x1000, 0x7207);
        cpu.State.ProgramCounter = 0x1004; cpu.ExecuteInstruction();
        cpu.State.ProgramCounter = 0x1000; cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[1]);
    }

    [Fact]
    public void HostMappingChangesInvalidateHeldCodeDuringBatchExecution()
    {
        var bus = new FetchBus();
        bus.Memory.WriteWords(0x1000, 0x7201, 0x60FC);
        using var cpu = new M68040Interpreter(bus);
        cpu.Reset(0x1000, 0x8000);
        cpu.ExecuteInstructions(2, null, new Boundary());
        bus.Memory.WriteWord(0x1000, 0x7207);
        bus.CpuPhysicalAddressMapGeneration++;
        cpu.ExecuteInstructions(2, null, new Boundary());
        Assert.Equal(7u, cpu.State.D[1]);
    }

    [Fact]
    public void IntegerFallbackUsesThe040FetchBuffer()
    {
        const ushort eor = 0xB110; // EOR.B D0,(A0), handled by the integer fallback.
        Assert.Equal(M68020OpcodeKind.Unsupported, M68020OpcodeDispatchTable.M68040Kinds[eor]);
        var bus = new FetchBus();
        bus.Memory.WriteWords(0x1000, eor, 0x60FC);
        using var cpu = new M68040Interpreter(bus);
        cpu.Reset(0x1000, 0x8000);
        cpu.State.A[0] = 0x2000; cpu.State.D[0] = 1; cpu.State.D[1] = 2;
        cpu.ExecuteInstruction(); cpu.ExecuteInstruction();
        bus.Memory.WriteWord(0x1000, 0xB310); // The held EOR D0 must remain visible.
        cpu.ExecuteInstruction();
        Assert.Equal(0, bus.Memory.Memory[0x2000]);
        Assert.All(bus.Fetches, f => Assert.Equal(4, f.Bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptReturnPreservesTheBitTestResultAcrossAnEdgeWrite(bool interruptAfterTest)
    {
        var bus = new FetchBus();
        // BTST #4,(A0); BEQ miss; MOVEQ #1,D1; BRA done; miss: MOVEQ #0,D1
        bus.Memory.WriteWords(0x1000, 0x0810, 4, 0x6704, 0x7201, 0x6002, 0x7200);
        // IRQ writes edge, then RTE restores the pre-interrupt flags.
        bus.Memory.WriteWords(0x1100, 0x10BC, 0x10, 0x4E73);
        bus.Memory.WriteLong(0x6C, 0x1100);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(0x1000, 0x8000); cpu.State.StatusRegister = 0x2000;
        cpu.State.A[0] = 0x2000;
        if (interruptAfterTest) cpu.ExecuteInstruction();
        var savedPc = cpu.State.ProgramCounter;
        var savedSr = cpu.State.StatusRegister;
        cpu.RequestInterrupt(3, 0x6C);
        Assert.Equal(savedPc, bus.Memory.ReadLong(0x7FFA));
        cpu.ExecuteInstruction(); cpu.ExecuteInstruction();
        Assert.Equal(savedSr, cpu.State.StatusRegister);
        if (!interruptAfterTest) cpu.ExecuteInstruction();
        cpu.ExecuteInstruction(); cpu.ExecuteInstruction();
        Assert.Equal(interruptAfterTest ? 0u : 1u, cpu.State.D[1]);
        Assert.Equal(0x10, bus.Memory.Memory[0x2000]);
    }

    private sealed class FetchBus : IM68kBus, IM68kCodeReader, IM68kStablePhysicalAddressMap
    {
        public uint CpuPhysicalAddressMapGeneration { get; set; }
        public bool IsCpuPhysicalAddressMapped(uint address, int byteCount, M68kBusAccessKind kind)
            => address + byteCount <= 0x10000;
        internal Copper68kTestBus Memory { get; } = new(0x10000);
        internal List<(uint Address, int Bytes)> Fetches { get; } = new();
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind)
            => Memory.ReadByte(address, ref cycle, kind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind)
        {
            if (kind == M68kBusAccessKind.CpuInstructionFetch) Fetches.Add((address, 2));
            cycle += 4;
            return Memory.ReadWord(address);
        }
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
        {
            if (kind == M68kBusAccessKind.CpuInstructionFetch) Fetches.Add((address, 4));
            cycle += 8; // A declared 16-bit test bridge, not a 040 timing oracle.
            return Memory.ReadLong(address);
        }
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind)
            => Memory.WriteByte(address, value, ref cycle, kind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
            => Memory.WriteWord(address, value, ref cycle, kind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
            => Memory.WriteLong(address, value, ref cycle, kind);
        public ushort ReadHostWord(uint address) => Memory.ReadWord(address);
        public bool HasHostGateway(uint address) => false;
        public bool TryInvokeHostGateway(uint pc, uint token, M68kCpuState state) => false;
        public void ResetExternalDevices(long cycle) { }
    }
}
