using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68020GameContinuationTests
{
    private const uint Code = 0xF80000;

    [Theory]
    [InlineData(0xC218, 0xABCD128Fu, 0xABCD1280u, 1)]
    [InlineData(0xC21F, 0xABCD128Fu, 0xABCD1280u, 2)]
    [InlineData(0xC258, 0xABCD8000u, 0xABCD8000u, 2)]
    [InlineData(0xC25F, 0xABCD8000u, 0xABCD8000u, 2)]
    [InlineData(0xC298, 0xFFFFFFFFu, 0x80012345u, 4)]
    [InlineData(0xC29F, 0xFFFFFFFFu, 0x80012345u, 4)]
    public void AndPostIncrementReadsSizedSourceAndPreservesUpperRegisterAndMemory(ushort opcode, uint before, uint after, uint stride)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode); bus.WriteLong(0x3000, 0x80012345);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.D[1] = before; cpu.State.A[opcode & 7] = 0x3000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(after, cpu.State.D[1]); Assert.Equal(0x3000u + stride, cpu.State.A[opcode & 7]);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31); Assert.Equal(0x80012345u, bus.ReadLong(0x3000));
        Assert.Equal(Code + 2, cpu.State.ProgramCounter);
        if ((opcode & 7) == 7) Assert.Equal(0x3000u + stride, cpu.State.InterruptStackPointer);
    }

    [Fact]
    public void AndPostIncrementSetsZeroFromTheOperandWidthAndRetainsExtend()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0xC218); bus.WriteLong(0x3000, 0x7FA55A12);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[0] = 0x3000; cpu.State.D[1] = 0xFFFFFF80; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(0xFFFFFF00u, cpu.State.D[1]);
        Assert.Equal(0x14, cpu.State.StatusRegister & 31); Assert.Equal(0x3001u, cpu.State.A[0]);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)] [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)] [InlineData(M68kCpuModel.M68040)] [InlineData(M68kCpuModel.M68060)]
    public void StatusPopRetainsSharedAdvancedCoreStackSemantics(M68kCpuModel model)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0x46DF); bus.WriteWord(0x6000, 0x0014);
        using var cpu = M68kCoreFactory.Default.Create(model, bus); cpu.Reset(Code, 0x6000);
        cpu.State.SetUserStackPointer(0x5000); cpu.ExecuteInstruction();
        Assert.Equal(0x0014, cpu.State.StatusRegister); Assert.Equal(0x5000u, cpu.State.A[7]);
        Assert.Equal(0x6002u, cpu.State.InterruptStackPointer); Assert.Equal(Code + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void PostIncrementToStatusReadsWordBeforeApplyingTheFullStatus(int register)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, (ushort)(0x46D8 | register));
        bus.WriteLong(0x3000, 0x251BA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[register] = 0x3000; cpu.ExecuteInstruction();
        Assert.Equal(0x251B, cpu.State.StatusRegister); Assert.Equal(0x3002u, cpu.State.A[register]);
        Assert.Equal(0x5000u, cpu.State.A[7]); Assert.Equal(Code + 2, cpu.State.ProgramCounter);
        Assert.Equal(0x251BA55Au, bus.ReadLong(0x3000)); Assert.Equal(12, cpu.State.NativeCycles);
    }

    [Theory]
    [InlineData(0x201F, 0x0014, 0x5000u)] [InlineData(0x201F, 0x3014, 0x7000u)]
    [InlineData(0x301F, 0x2014, 0x6000u)] [InlineData(0x301F, 0x0014, 0x5000u)]
    [InlineData(0x201F, 0x2014, 0x6002u)]
    public void StatusPopIncrementsTheOldStackBeforeSwitchingBanks(ushort before, ushort after, uint active)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0x46DF);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x6000);
        cpu.State.SetUserStackPointer(0x5000); cpu.State.SetMasterStackPointer(0x7000);
        cpu.State.StatusRegister = before; var oldStack = cpu.State.A[7]; bus.WriteWord(oldStack, after);
        cpu.ExecuteInstruction();
        Assert.Equal(after, cpu.State.StatusRegister); Assert.Equal(active, cpu.State.A[7]);
        Assert.Equal((before & 0x1000) == 0 ? 0x6002u : 0x6000u, cpu.State.InterruptStackPointer);
        Assert.Equal((before & 0x1000) != 0 ? 0x7002u : 0x7000u, cpu.State.MasterStackPointer);
        Assert.Equal(0x5000u, cpu.State.UserStackPointer); Assert.Equal(Code + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0)] [InlineData(7)]
    public void UserModeStatusPopRaisesPrivilegeBeforeReadingOrIncrementingSource(int source)
    {
        var bus = new StatusReadBus(); WriteWords(bus.Memory, Code, (ushort)(0x46D8 | source));
        bus.Memory.WriteLong(8 * 4, 0x2000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x6000);
        cpu.State.SetUserStackPointer(0x5000); cpu.State.StatusRegister = 0x0014;
        cpu.State.A[0] = 0x3000; cpu.ExecuteInstruction();
        Assert.Equal(0x2000u, cpu.State.ProgramCounter); Assert.Equal(0x3000u, cpu.State.A[0]);
        Assert.Equal(0x5000u, cpu.State.UserStackPointer); Assert.Equal(0x5FF8u, cpu.State.A[7]);
        Assert.Equal(Code, bus.Memory.ReadLong(0x5FFA)); Assert.False(bus.SourceRead);
    }

    [Fact]
    public void StatusPopTimingRetainsTheBusSynchronizationBarrier()
    {
        var plan = M68kTimingFormula.CreatePlan(M68020TimingModel.GetDescriptor(M68kInstructionTimingKey.MoveWordPostIncrementToStatusRegister));
        Assert.Equal(12, plan.NativeCycles); Assert.Equal(M68kTimingBarrier.SynchronizeBus, plan.Barriers);
    }

    private sealed class StatusReadBus : IM68kBus, IM68kCodeReader
    {
        public ZeroWaitCodeBus Memory { get; } = new();
        public bool SourceRead { get; private set; }
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind)
        { if (address is 0x3000 or 0x5000 && kind == M68kBusAccessKind.CpuDataRead) SourceRead = true; return Memory.ReadByte(address, ref cycle, kind); }
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind)
        { if (address is 0x3000 or 0x5000 && kind == M68kBusAccessKind.CpuDataRead) SourceRead = true; return Memory.ReadWord(address, ref cycle, kind); }
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
        { if (address is 0x3000 or 0x5000 && kind == M68kBusAccessKind.CpuDataRead) SourceRead = true; return Memory.ReadLong(address, ref cycle, kind); }
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => Memory.WriteByte(address, value, ref cycle, kind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => Memory.WriteWord(address, value, ref cycle, kind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => Memory.WriteLong(address, value, ref cycle, kind);
        public ushort ReadHostWord(uint address) => Memory.ReadWord(address);
        public void ResetExternalDevices(long cycle) => Memory.ResetExternalDevices(cycle);
    }

    [Theory]
    [InlineData(0x9339, 0x00001234u, 0xFF001234u, 0x19)]
    [InlineData(0x9379, 0x80001234u, 0x7FFF1234u, 0x02)]
    [InlineData(0x93B9, 0u, uint.MaxValue, 0x19)]
    public void SubDataToAbsoluteLongHonorsWidthAndArithmeticFlags(ushort opcode, uint before, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0, 0x4000); bus.WriteLong(0x4000, before);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        var source = (opcode & 0x80) != 0 ? 1u : 0xABCD0001u;
        cpu.State.D[1] = source; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000)); Assert.Equal(source, cpu.State.D[1]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(Code + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x4CB4)] [InlineData(0x4CF4)]
    public void IndexedMovemLatchesBaseAndIndexBeforeLoadingTheirRegisters(ushort opcode)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0x9041, 0x6CFC);
        var longSize = (opcode & 0x40) != 0;
        if (longSize) { bus.WriteLong(0x2FF8, 0x80012345); bus.WriteLong(0x2FFC, 0xFFFFFFF0); bus.WriteLong(0x3000, 0x30004000); bus.WriteLong(0x3004, 0x6000); }
        else WriteWords(bus, 0x2FF8, 0x8001, 0xFFF0, 0x4000, 0x6000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[4] = 0x3000; cpu.State.D[6] = uint.MaxValue; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(longSize ? 0x80012345u : 0xFFFF8001u, cpu.State.D[0]); Assert.Equal(0xFFFFFFF0u, cpu.State.D[6]);
        Assert.Equal(longSize ? 0x30004000u : 0x4000u, cpu.State.A[4]); Assert.Equal(0x6000u, cpu.State.A[7]);
        Assert.Equal(0x6000u, cpu.State.InterruptStackPointer); Assert.Equal(0x201F, cpu.State.StatusRegister);
        Assert.Equal(Code + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x4CBB)] [InlineData(0x4CFB)]
    public void PcIndexedMovemUsesTheExtensionAfterTheRegisterMask(ushort opcode)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 3, 0x6804);
        var longSize = (opcode & 0x40) != 0;
        if (longSize) { bus.WriteLong(Code + 12, 0x12345678); bus.WriteLong(Code + 16, 0x87654321); }
        else WriteWords(bus, Code + 12, 0x1234, 0x8765);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.D[6] = 4; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(longSize ? 0x12345678u : 0x1234u, cpu.State.D[0]);
        Assert.Equal(longSize ? 0x87654321u : 0xFFFF8765u, cpu.State.D[1]);
        Assert.Equal(Code + 6, cpu.State.ProgramCounter); Assert.Equal(0x201F, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x4CB4)] [InlineData(0x4CF4)]
    public void EmptyIndexedMovemStillConsumesMaskAndExtension(ushort opcode)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0, 0x6800);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[4] = 0x3000; cpu.State.D[6] = 4; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(0x3000u, cpu.State.A[4]); Assert.Equal(4u, cpu.State.D[6]);
        Assert.Equal(Code + 6, cpu.State.ProgramCounter); Assert.Equal(0x201F, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x0840, 0xFF1F, 0x80000000u, 0u, 0x1B)]
    [InlineData(0x0847, 0xFF20, 0u, 1u, 0x1F)]
    [InlineData(0x0843, 0xFFFF, 1u, 0x80000001u, 0x1F)]
    public void ImmediateRegisterBitChangeUsesModuloThirtyTwoAndOnlyChangesZero(ushort opcode, ushort bit, uint before, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, bit);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.D[opcode & 7] = before; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[opcode & 7]); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 4, cpu.State.ProgramCounter); Assert.Equal(4, cpu.State.NativeCycles);
    }

    [Theory]
    [InlineData(0x0439, 0u, 0xFF000000u, 0x19)]
    [InlineData(0x0479, 0x80001234u, 0x7FFF1234u, 0x02)]
    [InlineData(0x04B9, 0u, uint.MaxValue, 0x19)]
    public void SubiAbsoluteLongConsumesSizedImmediateBeforeAddress(ushort opcode, uint before, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        if ((opcode & 0x80) != 0) WriteWords(bus, Code, opcode, 0, 1, 0, 0x4000);
        else WriteWords(bus, Code, opcode, (opcode & 0x40) == 0 ? (ushort)0xAB01 : (ushort)1, 0, 0x4000);
        bus.WriteLong(0x4000, before);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000)); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + ((opcode & 0x80) != 0 ? 10u : 8u), cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x1555, 0x80, 0x18)] [InlineData(0x1552, 0x80, 0x18)] [InlineData(0x3555, 0x8012, 0x18)]
    public void IndirectToDisplacementReadsBeforeAliasedWrite(ushort opcode, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0xFFFC);
        bus.WriteWord(0x3000, 0x8012); bus.WriteWord(0x4000, 0x8012);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[5] = 0x3000; cpu.State.A[2] = 0x4000; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, (opcode >> 12) == 1 ? ReadByte(bus, 0x3FFC) : bus.ReadWord(0x3FFC));
        Assert.Equal(0x4000u, cpu.State.A[2]); Assert.Equal(0x3000u, cpu.State.A[5]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(Code + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x085A, 0x08AA, 0x00AA, 0x1B)] [InlineData(0x089A, 0x08AA, 0x00AA, 0x1B)]
    [InlineData(0x08DA, 0x08AA, 0x08AA, 0x1B)] [InlineData(0x089A, 0x00AA, 0x00AA, 0x1F)]
    [InlineData(0x08DF, 0x00AA, 0x08AA, 0x1F)]
    public void ImmediateBitPostIncrementUsesModuloEightAndPreservesOtherFlags(ushort opcode, ushort before, ushort expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0xFFFB); bus.WriteWord(0x3000, before);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[opcode & 7] = 0x3000; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadWord(0x3000)); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal((opcode & 7) == 7 ? 0x3002u : 0x3001u, cpu.State.A[opcode & 7]);
        Assert.Equal(Code + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x12F9, 0x80000000u, 0x80000000u, 1u)]
    [InlineData(0x32F9, 0x80000000u, 0x80000000u, 2u)]
    [InlineData(0x22F9, 0x80012345u, 0x80012345u, 4u)]
    [InlineData(0x1EF9, 0x80000000u, 0x80000000u, 2u)]
    public void AbsoluteLongToPostIncrementReadsBeforeWriteAndHonorsStackStride(ushort opcode, uint source, uint expected, uint stride)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0, 0x3000); bus.WriteLong(0x3000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        var register = (opcode >> 9) & 7; cpu.State.A[register] = 0x4000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, bus.ReadLong(0x4000));
        Assert.Equal(0x4000 + stride, cpu.State.A[register]); Assert.Equal(source, bus.ReadLong(0x3000));
        Assert.Equal(0x18, cpu.State.StatusRegister & 31); Assert.Equal(Code + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x4233, 0x00FFFFFFu)] [InlineData(0x4273, 0x0000FFFFu)] [InlineData(0x42B3, 0u)]
    public void FullIndexedClearWritesSizedZeroAndPreservesPointer(uint opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, (ushort)opcode, 0x0162, 16, 24);
        bus.WriteLong(0x2010, 0x3000); bus.WriteLong(0x3018, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[3] = 0x2000; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3018)); Assert.Equal(0x3000u, bus.ReadLong(0x2010));
        Assert.Equal(0x2000u, cpu.State.A[3]); Assert.Equal(0x14, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 8, cpu.State.ProgramCounter); Assert.Equal(19, cpu.State.NativeCycles);
    }

    [Theory]
    [InlineData(0x113C, 1u)] [InlineData(0x1F3C, 2u)]
    public void ByteImmediatePredecrementUsesLowImmediateByteAndStackStride(ushort opcode, uint stride)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0xAB80);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        var register = (opcode >> 9) & 7; cpu.State.A[register] = 0x4000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(0x80, ReadByte(bus, 0x4000 - stride));
        Assert.Equal(0x4000 - stride, cpu.State.A[register]); Assert.Equal(0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x1B87, 0x80u)] [InlineData(0x3B87, 0x80FFu)] [InlineData(0x2B87, 0x80FF1234u)]
    public void FullIndexedRegisterStoreHonorsWidthAndPointerTarget(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0x0162, 16, 24);
        bus.WriteLong(0x2010, 0x3000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[5] = 0x2000; cpu.State.D[7] = expected; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, (opcode >> 12) == 1 ? ReadByte(bus, 0x3018) : (opcode >> 12) == 3 ? bus.ReadWord(0x3018) : bus.ReadLong(0x3018));
        Assert.Equal(0x3000u, bus.ReadLong(0x2010)); Assert.Equal(expected, cpu.State.D[7]);
        Assert.Equal(0x2000u, cpu.State.A[5]); Assert.Equal(0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 8, cpu.State.ProgramCounter); Assert.Equal(16, cpu.State.NativeCycles);
    }

    [Theory]
    [InlineData(0xD339, 0x80000000u)] [InlineData(0xD379, 0x80000000u)] [InlineData(0xD3B9, 0x80000000u)]
    public void AddDataToAbsoluteLongHonorsWidthAndOverflow(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0, 0x4000);
        bus.WriteLong(0x4000, (opcode & 0xC0) == 0 ? 0x7F000000u : (opcode & 0xC0) == 0x40 ? 0x7FFF0000u : 0x7FFFFFFFu);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.D[1] = 1; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000)); Assert.Equal(1u, cpu.State.D[1]);
        Assert.Equal(0xA, cpu.State.StatusRegister & 31); Assert.Equal(Code + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x1119, 0, 1, 0x3FFFu, 0x3001u)]
    [InlineData(0x1F1F, 7, 7, 0x3000u, 0x3000u)]
    [InlineData(0x1F19, 7, 1, 0x3FFEu, 0x3001u)]
    public void BytePostIncrementToPredecrementHandlesAliasingAndStackStride(ushort opcode, int destination, int source, uint writeAddress, uint finalSource)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0x4E71); bus.WriteWord(0x3000, 0x8012);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[destination] = 0x4000; cpu.State.A[source] = 0x3000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(0x80, ReadByte(bus, writeAddress));
        Assert.Equal(finalSource, cpu.State.A[source]); Assert.Equal(writeAddress, cpu.State.A[destination]);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31); Assert.Equal(Code + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x12348000u, 1, 0x12)] [InlineData(0xABCD0000u, 1, 0x19)] [InlineData(0xFFFF0001u, 1, 0x14)]
    public void CmpWordAbsoluteLongPreservesDataAndExtend(uint destination, ushort source, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0xB079, 0, 0x4000); bus.WriteWord(0x4000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.D[0] = destination; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(destination, cpu.State.D[0]); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0A2D, 0xFF000000u)] [InlineData(0x0A6D, 0xF00F0000u)] [InlineData(0x0AAD, 0xF000000Fu)]
    public void EoriDisplacementConsumesImmediateBeforeSignedDisplacement(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus();
        if ((opcode & 0x80) != 0) WriteWords(bus, Code, opcode, 0, 15, 0xFFFC);
        else WriteWords(bus, Code, opcode, 15, 0xFFFC);
        bus.WriteLong(0x3FFC, 0xF0000000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[5] = 0x4000; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3FFC)); Assert.Equal(0x4000u, cpu.State.A[5]);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + ((opcode & 0x80) != 0 ? 8u : 6u), cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x1CF3, 0x80u, 1u)] [InlineData(0x3CF3, 0x8001u, 2u)] [InlineData(0x2CF3, 0x80012345u, 4u)]
    public void FullIndexedSourceToPostIncrementWritesThenAdvances(ushort opcode, uint expected, uint increment)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0x0151);
        bus.WriteLong(0x2000, 0x3000); bus.WriteLong(0x3000, 0x80012345);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[3] = 0x2000; cpu.State.A[6] = 0x4000; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, increment == 1 ? (uint)(bus.ReadWord(0x4000) >> 8) : increment == 2 ? bus.ReadWord(0x4000) : bus.ReadLong(0x4000));
        Assert.Equal(0x4000u + increment, cpu.State.A[6]); Assert.Equal(0x2000u, cpu.State.A[3]);
        Assert.Equal(Code + 4, cpu.State.ProgramCounter); Assert.Equal(0x18, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(0x0039, 0xFF000000u)] [InlineData(0x0079, 0xF00F0000u)] [InlineData(0x00B9, 0xF000000Fu)]
    [InlineData(0x0239, 0x00000000u)] [InlineData(0x0279, 0x00000000u)] [InlineData(0x02B9, 0x00000000u)]
    public void ImmediateLogicalAbsoluteLongHonorsWidthAndFlags(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus();
        if ((opcode & 0x80) != 0) WriteWords(bus, Code, opcode, 0, 15, 0, 0x4000);
        else WriteWords(bus, Code, opcode, 15, 0, 0x4000);
        bus.WriteLong(0x4000, 0xF0000000);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000));
        Assert.Equal((opcode & 0x200) != 0 ? 0x14 : 0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + ((opcode & 0x80) != 0 ? 10u : 8u), cpu.State.ProgramCounter);
    }

    [Fact]
    public void FullSourceMoveConsumesSourceDisplacementsBeforeDestinationDisplacement()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, 0x2974, 0x0162, 0x10, 0x18, 0xFFFC);
        bus.WriteLong(0x2010, 0x3000); bus.WriteLong(0x3018, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus); cpu.Reset(Code, 0x5000);
        cpu.State.A[4] = 0x2000; cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(0x89ABCDEFu, bus.ReadLong(0x1FFC)); Assert.Equal(0x2000u, cpu.State.A[4]);
        Assert.Equal(Code + 10, cpu.State.ProgramCounter); Assert.Equal(0x18, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(0x5219, 1, false)] [InlineData(0x5259, 2, false)] [InlineData(0x5299, 4, false)]
    [InlineData(0x5319, 1, true)] [InlineData(0x5359, 2, true)] [InlineData(0x5399, 4, true)]
    [InlineData(0x5017 + 8, 2, false)] [InlineData(0x5117 + 8, 2, true)]
    public void QuickPostIncrementUpdatesOriginalOperandAndFlags(ushort opcode, uint increment, bool subtract)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0x4E71);
        var size = (opcode >> 6) & 3;
        var source = (opcode & 0xE00) == 0 ? 8u : 1u;
        bus.WriteLong(0x3000, subtract ? 0u : uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x5000); cpu.State.A[opcode & 7] = 0x3000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        var expected = subtract ? unchecked(0u - source) : source - 1;
        Assert.Equal(size == 0 ? (byte)expected : size == 1 ? (ushort)expected : expected,
            size == 0 ? (uint)(bus.ReadWord(0x3000) >> 8) : size == 1 ? bus.ReadWord(0x3000) : bus.ReadLong(0x3000));
        Assert.Equal(0x3000u + increment, cpu.State.A[opcode & 7]);
        Assert.Equal(subtract ? 0x19 : source == 1 ? 0x15 : 0x11, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x92F9, 0xFFFE0000u, 0x3002u)] [InlineData(0x93F9, 2u, 0x2FFEu)]
    [InlineData(0xDEF9, 0xFFFE0000u, 0x2FFEu)] [InlineData(0xDFF9, 2u, 0x3002u)]
    [InlineData(0x9EF9, 0xFFFE0000u, 0x3002u)] [InlineData(0x9FF9, 2u, 0x2FFEu)]
    public void AddressArithmeticSignExtendsWordsAndPreservesFlags(ushort opcode, uint value, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, Code, opcode, 0, 0x4000, 0x4E71);
        bus.WriteLong(0x4000, value);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x3000); cpu.State.A[(opcode >> 9) & 7] = 0x3000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.A[(opcode >> 9) & 7]); Assert.Equal(0x201F, cpu.State.StatusRegister);
        Assert.Equal(Code + 6, cpu.State.ProgramCounter);
        if (((opcode >> 9) & 7) == 7) Assert.Equal(expected, cpu.State.InterruptStackPointer);
    }
}
