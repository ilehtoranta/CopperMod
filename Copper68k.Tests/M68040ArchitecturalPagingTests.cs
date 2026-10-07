namespace Copper68k.Tests;

public sealed class M68040ArchitecturalPagingTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68040)] [InlineData(M68kCpuModel.M68060)]
    public void MovecRootAndTcKeepAtcUntilGuestPflush(M68kCpuModel model)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0x2239, 0, 0x2000, 0x4E7B, 0x0807, 0x4E7B, 0x2003,
            0x2639, 0, 0x2000, 0xF518, 0x2839, 0, 0x2000);
        bus.WriteLong(0x4000, 0x4202); bus.WriteLong(0x4200, 0x4402); bus.WriteLong(0x4408, 0x5001);
        bus.WriteLong(0x6000, 0x6202); bus.WriteLong(0x6200, 0x6402); bus.WriteLong(0x6408, 0x7001);
        bus.WriteLong(0x5000, 0x11223344); bus.WriteLong(0x7000, 0x55667788);
        using var cpu = M68kCoreFactory.Default.Create(model, bus); cpu.Reset(0x1000, 0x8000);
        cpu.State.M68040Mmu.SupervisorRootPointer = 0x4000;
        cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0xA000;
        cpu.State.M68040Mmu.TranslationControl = 0x8000;
        cpu.State.D[0] = 0x6000; cpu.State.D[2] = 0x8000;
        for (var i = 0; i < 6; i++) cpu.ExecuteInstruction();
        Assert.Equal(0x11223344u, cpu.State.D[1]); Assert.Equal(cpu.State.D[1], cpu.State.D[3]);
        Assert.Equal(0x55667788u, cpu.State.D[4]); Assert.Equal(0x101Cu, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68040, 0xF568)] [InlineData(M68kCpuModel.M68060, 0xF5C8)]
    public void ProbeInstructionsUseDfcAndModelResult(M68kCpuModel model, int opcode)
    {
        var bus = new Copper68kTestBus(0x10000); bus.WriteWord(0x1000, (ushort)opcode);
        bus.WriteLong(0x4000, 0x4202); bus.WriteLong(0x4200, 0x4402); bus.WriteLong(0x4408, 0x5001);
        bus.WriteLong(0x6000, 0x6202); bus.WriteLong(0x6200, 0x6402); bus.WriteLong(0x6408, 0x7001);
        using var cpu = M68kCoreFactory.Default.Create(model, bus); cpu.Reset(0x1000, 0x8000);
        cpu.State.M68040Mmu.SupervisorRootPointer = 0x4000; cpu.State.M68040Mmu.UserRootPointer = 0x6000;
        cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0xA000;
        cpu.State.M68040Mmu.TranslationControl = 0x8000;
        cpu.State.DestinationFunctionCode = 1; cpu.State.A[0] = 0x2004;
        cpu.ExecuteInstruction();
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
        if (model == M68kCpuModel.M68060) Assert.Equal(0x7004u, cpu.State.A[0]);
        else { Assert.Equal(0x2004u, cpu.State.A[0]); Assert.Equal(0x7001u, cpu.State.M68040Mmu.Status); }
        Assert.Equal(0x7009u, bus.ReadLong(0x6408)); Assert.Equal(0x5001u, bus.ReadLong(0x4408));
    }

    [Fact]
    public void LongOperandSpanningPagesUsesBothTranslations()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteLong(0x4000, 0x4202); bus.WriteLong(0x4200, 0x4402);
        bus.WriteLong(0x4408, 0x5001); bus.WriteLong(0x440C, 0x7001);
        bus.WriteWord(0x5FFE, 0x1234); bus.WriteWord(0x7000, 0x5678);
        var state = new M68kCpuState(); state.StatusRegister = 0x2700;
        state.M68040Mmu.SupervisorRootPointer = 0x4000; state.M68040Mmu.TranslationControl = 0x8000;
        var logical = new M68040LogicalBus(bus, state); long cycle = 0;
        Assert.Equal(0x12345678u, logical.ReadLong(0x2FFE, ref cycle, M68kBusAccessKind.CpuDataRead));
        logical.WriteLong(0x2FFE, 0x87654321, ref cycle, M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(0x8765, bus.ReadWord(0x5FFE)); Assert.Equal(0x4321, bus.ReadWord(0x7000));
        Assert.Equal(0, bus.ReadWord(0x6000));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void WalksThreeLevelsAndUpdatesUsedAndModified(bool eightK, bool indirect)
    {
        var bus = new Copper68kTestBus(0x10000);
        var mmu = new M68040MmuState { SupervisorRootPointer = 0x4000, TranslationControl = eightK ? 0xC000u : 0x8000u };
        const uint logical = 0xFE345678;
        var root = 0x4000 + (logical >> 25) * 4;
        var pointer = 0x4200 + ((logical >> 18) & 127) * 4;
        var page = 0x4400 + ((logical >> (eightK ? 13 : 12)) & (eightK ? 31u : 63u)) * 4;
        bus.WriteLong(root, 0x4202); bus.WriteLong(pointer, 0x4402);
        bus.WriteLong(page, indirect ? 0x4602u : 0x8001u);
        if (indirect) bus.WriteLong(0x4600, 0x8001);
        Assert.True(mmu.TryTranslate(logical, M68kBusAccessKind.CpuDataRead, false, true, bus.ReadLong,
            out var physical, out _, bus.WriteLong));
        Assert.Equal(0x8000u | (logical & (eightK ? 0x1FFFu : 0xFFFu)), physical);
        Assert.Equal(0x420Au, bus.ReadLong(root)); Assert.Equal(0x440Au, bus.ReadLong(pointer));
        Assert.Equal(0x8009u, bus.ReadLong(indirect ? 0x4600 : page));
        Assert.True(mmu.TryTranslate(logical, M68kBusAccessKind.CpuDataWrite, true, true, bus.ReadLong,
            out _, out _, bus.WriteLong));
        Assert.Equal(0x8019u, bus.ReadLong(indirect ? 0x4600 : page));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CachedReadsDoNotBypassWriteProtection(int protectedLevel)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteLong(0x4000, 0x4202u | (protectedLevel == 0 ? 4u : 0));
        bus.WriteLong(0x4200, 0x4402u | (protectedLevel == 1 ? 4u : 0));
        bus.WriteLong(0x4408, 0x5001u | (protectedLevel == 2 ? 4u : 0));
        var mmu = new M68040MmuState { SupervisorRootPointer = 0x4000, TranslationControl = 0x8000 };
        Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, true, bus.ReadLong, out _, out _, bus.WriteLong));
        Assert.False(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataWrite, true, true, bus.ReadLong, out _, out var fault, bus.WriteLong));
        Assert.Equal(0x100u, fault.FaultReason);
        Assert.Equal(0u, bus.ReadLong(0x4408) & 0x10);
    }

    [Fact]
    public void RootChangeKeepsCachedMappingUntilExplicitFlush()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteLong(0x4000, 0x4202); bus.WriteLong(0x4200, 0x4402); bus.WriteLong(0x4408, 0x5001);
        bus.WriteLong(0x6000, 0x6202); bus.WriteLong(0x6200, 0x6402); bus.WriteLong(0x6408, 0x7001);
        var mmu = new M68040MmuState { SupervisorRootPointer = 0x4000, TranslationControl = 0x8000 };
        Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, true, bus.ReadLong, out var old, out _, bus.WriteLong));
        mmu.SupervisorRootPointer = 0x6000;
        Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, true, bus.ReadLong, out var retained, out _, bus.WriteLong));
        Assert.Equal(old, retained); mmu.Flush();
        Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, true, bus.ReadLong, out var updated, out _, bus.WriteLong));
        Assert.Equal(0x7000u, updated);
    }

    [Fact]
    public void CompilerPeekDoesNotPopulateAtcOrChangeDescriptors()
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteLong(0x4000, 0x4202); bus.WriteLong(0x4200, 0x4402); bus.WriteLong(0x4408, 0x5001);
        var mmu = new M68040MmuState { SupervisorRootPointer = 0x4000, TranslationControl = 0x8000 };
        Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, true, bus.ReadLong, out _, out _));
        Assert.Equal(0x4202u, bus.ReadLong(0x4000)); Assert.Equal(0x5001u, bus.ReadLong(0x4408));
        bus.WriteLong(0x4408, 0x6001);
        Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, true, bus.ReadLong, out var physical, out _, bus.WriteLong));
        Assert.Equal(0x6000u, physical); Assert.Equal(0x420Au, bus.ReadLong(0x4000));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TransparentWriteProtectionAppliesEvenWhenPagingIsDisabled(bool supervisor)
    {
        var mmu = new M68040MmuState { DataTransparentTranslation0 = 0x00FFC004 };
        Assert.True(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataRead, false, supervisor, _ => throw new Exception("Unexpected table read"), out _, out _));
        Assert.False(mmu.TryTranslate(0x2000, M68kBusAccessKind.CpuDataWrite, true, supervisor, _ => throw new Exception("Unexpected table read"), out _, out var fault));
        Assert.Equal(4u, fault.Status);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68040, 60, 0x7008)]
    [InlineData(M68kCpuModel.M68060, 16, 0x4008)]
    public void FaultUsesTranslatedSupervisorStackAndModelFrame(M68kCpuModel model, int frameBytes, int format)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0x2039, 0, 0x2000);
        bus.WriteLong(0x4000, 0x4202); bus.WriteLong(0x4200, 0x4402);
        bus.WriteLong(0x4400, 0x5001); // vector table translated into $5000
        bus.WriteLong(0x441C, 0x9001); // supervisor stack translated into $9000
        bus.WriteLong(0x5008, 0x3000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x8000);
        cpu.State.M68040Mmu.SupervisorRootPointer = 0x4000;
        cpu.State.M68040Mmu.InstructionTransparentTranslation0 = 0xA000;
        cpu.State.M68040Mmu.TranslationControl = 0x8000;
        cpu.ExecuteInstruction();
        Assert.False(cpu.State.Halted); Assert.Equal(0x3000u, cpu.State.ProgramCounter);
        Assert.Equal(0x8000u - (uint)frameBytes, cpu.State.A[7]);
        var physicalSp = 0xA000u - (uint)frameBytes;
        Assert.Equal(format, bus.ReadWord(physicalSp + 6));
        Assert.Equal(0x2700, bus.ReadWord(physicalSp));
        Assert.Equal(0x2000u, bus.ReadLong(physicalSp + (model == M68kCpuModel.M68060 ? 8u : 20u)));
        if (model == M68kCpuModel.M68060) Assert.Equal(0x01450400u, bus.ReadLong(physicalSp + 12));
    }
}
