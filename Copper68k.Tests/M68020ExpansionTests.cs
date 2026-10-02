using Copper68k;

namespace Copper68k.Tests;

public sealed class M68020ExpansionTests
{
    public static IEnumerable<object[]> AbsoluteMoveCases()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var size in new[] { 1, 2, 4 })
        foreach (var longAddress in new[] { false, true })
        foreach (var zero in new[] { false, true })
            yield return new object[] { model, size, longAddress, zero };
    }

    [Theory]
    [MemberData(nameof(AbsoluteMoveCases))]
    public void AbsoluteSourceToDisplacementCopiesOperandAndSetsMoveFlags(
        M68kCpuModel model, int size, bool longAddress, bool zero)
    {
        var bus = new ZeroWaitCodeBus();
        // MOVE.B/W/L ($FFFF8030).W or .L,-4(A3). Source word is signed;
        // byte/word stores must leave surrounding bytes untouched. X survives.
        var prefix = size == 1 ? 0x1000 : size == 2 ? 0x3000 : 0x2000;
        var opcode = (ushort)(prefix | 0x0740 | (longAddress ? 0x39 : 0x38));
        var words = longAddress ? new ushort[] { opcode, 0xFFFF, 0x8030, 0xFFFC }
            : new ushort[] { opcode, 0x8030, 0xFFFC };
        M68kInterpreterTestHelpers.WriteWords(bus, 0x1000, words);
        bus.WriteLong(0x00FF8030, zero ? 0u : 0x89ABCDEFu);
        bus.WriteLong(0x4000, 0x55555555);
        bus.WriteLong(0x4004, 0x55555555);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[3] = 0x4006;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(0x1000u + (uint)words.Length * 2, cpu.State.ProgramCounter);
        Assert.Equal(0x4006u, cpu.State.A[3]);
        for (var i = 0; i < 8; i++)
            Assert.Equal(i >= 2 && i < 2 + size ? (zero ? (byte)0 : (byte)(0x89ABCDEFu >> (24 - 8 * (i - 2)))) : (byte)0x55,
                M68kInterpreterTestHelpers.ReadByte(bus, 0x4000u + (uint)i));
        Assert.Equal(zero ? 0x2714 : 0x2718, cpu.State.StatusRegister);
        Assert.True(cpu.State.Cycles > 0);
        Assert.True(cpu.State.NativeCycles > 0);
    }

    [Theory]
    [InlineData(0, 0x89000000u, 0xFFFFFFEFu, 0x89000000u, 0x2718)]
    [InlineData(0, 0x89000000u, 0xFFFFFF00u, 0u, 0x2714)]
    [InlineData(1, 0x89AB0000u, 0xFFFF00FFu, 0x00AB0000u, 0x2710)]
    [InlineData(2, 0x89ABCDEFu, 0x0F0F0F0Fu, 0x090B0D0Fu, 0x2710)]
    public void AndDataToIndirectPreservesSourceAndWidth(int sizeBits, uint initial, uint mask, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0xC511 | (sizeBits << 6))); // AND D2,(A1)
        bus.WriteLong(0x4000, initial);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[1] = 0x4000;
        cpu.State.D[2] = mask;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000));
        Assert.Equal(mask, cpu.State.D[2]);
        Assert.Equal(0x4000u, cpu.State.A[1]);
        Assert.Equal(flags, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    public void EnablingInstructionCacheDoesNotRequireOptionalHostCodeReader(M68kCpuModel model)
    {
        var memory = new Copper68kTestBus();
        memory.WriteWords(0xC01000, 0x7001, 0x4E7B, 0x0002, 0x4E71, 0x4E71);
        using var cpu = M68kCoreFactory.Default.Create(model, new BareBus(memory));
        cpu.Reset(0xC01000, 0x7000);
        for (var i = 0; i < 4; i++) cpu.ExecuteInstruction();
        Assert.Equal(0xC0100Au, cpu.State.ProgramCounter);
        Assert.Equal(1u, cpu.State.CacheControlRegister);
    }

    [Theory]
    [InlineData(0, 0x00FFFFFFu)]
    [InlineData(1, 0x0000FFFFu)]
    [InlineData(2, 0u)]
    public void IndexedClearUsesSignedScaledWordIndexAndOperandWidth(int sizeBits, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x4230 | (sizeBits << 6)), 0x02F8);
        bus.WriteLong(0x3FF4, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.D[0] = 0x1234FFFE; // D0.W = -2; scale 2, displacement -8.
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3FF4));
        Assert.Equal(0x2714, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void ImmediateWordToIndexedDestinationConsumesExtensionsInOrder()
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x31BC, 0x8001, 0x1008); // MOVE.W #$8001,8(A0,D1.W)
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x4000;
        cpu.State.D[1] = 0x1234FFFC;
        cpu.ExecuteInstruction();
        Assert.Equal(0x8001, bus.ReadWord(0x4004));
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x12348000u, 0x2718)]
    [InlineData(0x12340000u, 0x2714)]
    public void AddressWordToIndirectUsesLowWordAndPreservesSource(uint source, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x3288); // MOVE.W A0,(A1)
        bus.WriteLong(0x4000, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = source;
        cpu.State.A[1] = 0x4000;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(((source & 0xFFFF) << 16) | 0xFFFFu, bus.ReadLong(0x4000));
        Assert.Equal(source, cpu.State.A[0]);
        Assert.Equal(flags, cpu.State.StatusRegister);
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void ImmediateWordPostIncrementWritesThenAdvancesPointer(int register)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, (ushort)(0x30FC | register << 9), 0x8001);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x4000);
        cpu.State.A[register] = 0x4000;
        cpu.ExecuteInstruction();
        Assert.Equal(0x8001, bus.ReadWord(0x4000));
        Assert.Equal(0x4002u, cpu.State.A[register]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
        if (register == 7) Assert.Equal(0x4002u, cpu.State.SupervisorStackPointer);
    }

    [Theory]
    [InlineData(0x13DF, 2u, 0x89FFFFFFu)]
    [InlineData(0x33DF, 2u, 0x89ABFFFFu)]
    [InlineData(0x23DF, 4u, 0x89ABCDEFu)]
    public void StackPostIncrementToAbsoluteRespectsByteStackAlignment(ushort opcode, uint increment, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x0000, 0x5000);
        bus.WriteLong(0x4000, 0x89ABCDEF);
        bus.WriteLong(0x5000, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x4000);
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x5000));
        Assert.Equal(0x4000u + increment, cpu.State.A[7]);
        Assert.Equal(cpu.State.A[7], cpu.State.SupervisorStackPointer);
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x0082, 0x8000, 0x0010, 0x00000100u, 0x80000110u, 0x2718, 6u)]
    [InlineData(0x0282, 0x0000, 0x0010, 0x00000100u, 0u, 0x2714, 6u)]
    [InlineData(0x0A02, 0xFFFE, 0x4E71, 0x123456FFu, 0x12345601u, 0x2710, 4u)]
    public void ImmediateLogicalDataFormsUseWidthAndPreserveExtend(ushort opcode, ushort high, ushort low, uint initial, uint expected, ushort flags, uint length)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, high, low);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x4000);
        cpu.State.D[2] = initial;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[2]);
        Assert.Equal(0x1000u + length, cpu.State.ProgramCounter);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0u, 0x12345681u, 0x2718)]
    [InlineData(1u, 0x12345640u, 0x2711)]
    [InlineData(8u, 0x12345600u, 0x2715)]
    [InlineData(9u, 0x12345600u, 0x2704)]
    [InlineData(64u, 0x12345681u, 0x2718)]
    public void ByteRegisterShiftMasksCountAndPreservesUpperBits(uint count, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0xE228); // LSR.B D1,D0
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x4000);
        cpu.State.D[0] = 0x12345681;
        cpu.State.D[1] = count;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x13B0, 0x300Cu, 0x89FFFFFFu)]
    [InlineData(0x33B0, 0x300Cu, 0x89ABFFFFu)]
    [InlineData(0x23B0, 0x300Cu, 0x89ABCDEFu)]
    [InlineData(0x13BB, 0x100Eu, 0x89FFFFFFu)]
    [InlineData(0x33BB, 0x100Eu, 0x89ABFFFFu)]
    [InlineData(0x23BB, 0x100Eu, 0x89ABCDEFu)]
    public void IndexedMoveUsesEachExtensionAndPcOfSourceExtension(ushort opcode, uint source, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x2008, 0x3008);
        bus.WriteLong(source, 0x89ABCDEF);
        bus.WriteLong(0x4004, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x3000;
        cpu.State.A[1] = 0x4000;
        cpu.State.D[2] = 4;
        cpu.State.D[3] = 0x1234FFFC;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4004));
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x10F0, 0x300Cu, 1u, 0x89FFFFFFu)]
    [InlineData(0x30F0, 0x300Cu, 2u, 0x89ABFFFFu)]
    [InlineData(0x20F0, 0x300Cu, 4u, 0x89ABCDEFu)]
    [InlineData(0x10FB, 0x100Eu, 1u, 0x89FFFFFFu)]
    [InlineData(0x30FB, 0x100Eu, 2u, 0x89ABFFFFu)]
    [InlineData(0x20FB, 0x100Eu, 4u, 0x89ABCDEFu)]
    public void IndexedSourcePrecedesAliasedDestinationIncrement(ushort opcode, uint source, uint increment, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x2008);
        bus.WriteLong(source, 0x89ABCDEF);
        bus.WriteLong(0x3000, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x3000;
        cpu.State.D[2] = 4;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(0x3000u + increment, cpu.State.A[0]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x9091, 0u, 1u, 0xFFFFFFFFu, 0x2719)]
    [InlineData(0x9091, 0x80000000u, 1u, 0x7FFFFFFFu, 0x2702)]
    [InlineData(0x9011, 0x12345600u, 0x01000000u, 0x123456FFu, 0x2719)]
    public void SubtractIndirectChecksBorrowOverflowAndOperandWidth(ushort opcode, uint initial, uint source, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        bus.WriteLong(0x3000, source);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[1] = 0x3000;
        cpu.State.D[0] = initial;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]);
        Assert.Equal(flags, cpu.State.StatusRegister);
        Assert.Equal(source, bus.ReadLong(0x3000));
    }

    [Fact]
    public void LongPcIndexedMoveUsesExtensionWordPcAndSignedIndex()
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x203B, 0x1220); // MOVE.L $20(PC,D1.W*2),D0
        bus.WriteLong(0x101A, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[1] = 0x1234FFFC;
        cpu.ExecuteInstruction();
        Assert.Equal(0x89ABCDEFu, cpu.State.D[0]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Fact]
    public void ByteDisplacementToIndirectPreservesAdjacentMemory()
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x14AB, 0xFFFE);
        bus.WriteLong(0x3000, 0x89ABCDEF);
        bus.WriteLong(0x4000, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[3] = 0x3002;
        cpu.State.A[2] = 0x4001;
        cpu.ExecuteInstruction();
        Assert.Equal(0xFF89FFFFu, bus.ReadLong(0x4000));
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xFFFF, 0x00005555u, 0x2715)]
    [InlineData(0x7FFF, 0x80005555u, 0x270A)]
    public void ImmediateWordAddToDisplacementSetsCarryAndOverflow(ushort initial, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x066E, 0x0001, 0xFFFE);
        bus.WriteLong(0x3000, ((uint)initial << 16) | 0x5555u);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[6] = 0x3002;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x0000, 0xFFFF5555u, 0x2719)]
    [InlineData(0x8000, 0x7FFF5555u, 0x2702)]
    public void ImmediateWordSubtractToDisplacementSetsBorrowAndOverflow(ushort initial, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x046E, 0x0001, 0xFFFE);
        bus.WriteLong(0x3000, ((uint)initial << 16) | 0x5555u);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[6] = 0x3002;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Fact]
    public void WordIndirectClearPreservesExtendAndAdjacentBytes()
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x4251);
        bus.WriteLong(0x3000, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[1] = 0x3001;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(0x890000EFu, bus.ReadLong(0x3000));
        Assert.Equal(0x1002u, cpu.State.ProgramCounter);
        Assert.Equal(0x2714, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x1ED7, 0x7002u)]
    [InlineData(0x3ED7, 0x7002u)]
    public void IndirectMoveToAliasedStackPostIncrementReadsBeforeUpdating(ushort opcode, uint expectedStack)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        bus.WriteLong(0x7000, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.ExecuteInstruction();
        Assert.Equal(0x89ABCDEFu, bus.ReadLong(0x7000));
        Assert.Equal(expectedStack, cpu.State.A[7]);
        Assert.Equal(expectedStack, cpu.State.SupervisorStackPointer);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xE238, 1u, 0x80018081u, 0x800180C0u, 0x2719)]
    [InlineData(0xE278, 1u, 0x80018081u, 0x8001C040u, 0x2719)]
    [InlineData(0xE2B8, 1u, 0x80018081u, 0xC000C040u, 0x2719)]
    [InlineData(0xE338, 1u, 0x80018081u, 0x80018003u, 0x2711)]
    [InlineData(0xE378, 1u, 0x80018081u, 0x80010103u, 0x2711)]
    [InlineData(0xE3B8, 1u, 0x80018081u, 0x00030103u, 0x2711)]
    [InlineData(0xE278, 0u, 0x80018081u, 0x80018081u, 0x2718)]
    [InlineData(0xE278, 16u, 0x80018081u, 0x80018081u, 0x2719)]
    [InlineData(0xE278, 64u, 0x80018081u, 0x80018081u, 0x2718)]
    public void RegisterRotateRespectsWidthCountAndExtend(ushort opcode, uint count, uint initial, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[0] = initial;
        cpu.State.D[1] = count;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xE220, 1u, 0x12348081u, 0x123480C0u, 0x2719)]
    [InlineData(0xE260, 1u, 0x12348081u, 0x1234C040u, 0x2719)]
    [InlineData(0xE360, 1u, 0x12348081u, 0x12340102u, 0x2713)]
    [InlineData(0xE328, 1u, 0x12348081u, 0x12348002u, 0x2711)]
    [InlineData(0xE360, 0u, 0x12348081u, 0x12348081u, 0x2718)]
    [InlineData(0xE360, 16u, 0x12348081u, 0x12340000u, 0x2717)]
    [InlineData(0xE260, 63u, 0x12348081u, 0x1234FFFFu, 0x2719)]
    public void RegisterShiftsCoverSignedFillOverflowAndLargeCounts(ushort opcode, uint count, uint initial, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[0] = initial;
        cpu.State.D[1] = count;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xDDB6, 0xFFFFFFFFu, 0u)]
    [InlineData(0xDD76, 0xFFFF5555u, 0x00005555u)]
    [InlineData(0xDD36, 0xFF555555u, 0x00555555u)]
    public void IndexedMemoryAddUsesSourceRegisterAndPreservesWidth(ushort opcode, uint initial, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x2010);
        bus.WriteLong(0x300C, initial);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[6] = 0x3000;
        cpu.State.D[2] = 0xFFFC;
        cpu.State.D[6] = 1;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x300C));
        Assert.Equal(1u, cpu.State.D[6]);
        Assert.Equal(0x2715, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x1792, 0x89FFFFFFu)]
    [InlineData(0x3792, 0x89ABFFFFu)]
    [InlineData(0x2792, 0x89ABCDEFu)]
    public void IndirectMoveToIndexedUsesSignedScaledDestination(ushort opcode, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x1410);
        bus.WriteLong(0x3000, 0x89ABCDEF);
        bus.WriteLong(0x4008, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[2] = 0x3000;
        cpu.State.A[3] = 0x4000;
        cpu.State.D[1] = 0xFFFE;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4008));
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Fact]
    public void SubtractDataWordFromDisplacementUsesLowWord()
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x9B6D, 0xFFFE);
        bus.WriteLong(0x3000, 0x00005555);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[5] = 0x3002;
        cpu.State.D[5] = 0x12340001;
        cpu.ExecuteInstruction();
        Assert.Equal(0xFFFF5555u, bus.ReadLong(0x3000));
        Assert.Equal(0x12340001u, cpu.State.D[5]);
        Assert.Equal(0x2719, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x1827, 0x12345689u)]
    [InlineData(0x3827, 0x123489ABu)]
    public void PredecrementByteAndWordRespectStackAlignmentAndRegisterWidth(ushort opcode, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        bus.WriteLong(0x6FFE, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[4] = 0x12345678;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[4]);
        Assert.Equal(0x6FFEu, cpu.State.SupervisorStackPointer);
        Assert.Equal(0x6FFEu, cpu.State.A[7]);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xB501, 0xFFFFFF7Fu, 0x2710)]
    [InlineData(0xB301, 0xFFFFFF00u, 0x2714)]
    public void ByteEorPreservesUpperBytesAndHandlesAliasing(ushort opcode, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[1] = uint.MaxValue;
        cpu.State.D[2] = 0x80000080;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[1]);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Fact]
    public void ByteDisplacementToAbsoluteConsumesSourceThenDestinationExtensions()
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x13EB, 0xFFFE, 0x0000, 0x4001);
        bus.WriteLong(0x3000, 0x89ABCDEF);
        bus.WriteLong(0x4000, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[3] = 0x3002;
        cpu.ExecuteInstruction();
        Assert.Equal(0xFF89FFFFu, bus.ReadLong(0x4000));
        Assert.Equal(0x1008u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x0C32, 0x00FF, 0x2010, 0x4E71, 0xFF123456u, 0x2714)]
    [InlineData(0x0C72, 0x0001, 0x2010, 0x4E71, 0x00001234u, 0x2719)]
    [InlineData(0x0CB2, 0x0000, 0x0001, 0x2010, 0x80000000u, 0x2712)]
    public void ImmediateIndexedCompareConsumesExtensionsWithoutWriting(ushort opcode, ushort a, ushort b, ushort c, uint memory, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, a, b, c);
        bus.WriteLong(0x300C, memory);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[2] = 0x3000;
        cpu.State.D[2] = 0xFFFC;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(memory, bus.ReadLong(0x300C));
        Assert.Equal(opcode == 0x0CB2 ? 0x1008u : 0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xB03A, 0xFFFF0089u)]
    [InlineData(0xB07A, 0xFFFF89ABu)]
    [InlineData(0xB0BA, 0x89ABCDEFu)]
    public void PcDisplacementCompareUsesExtensionPcAndPreservesExtend(ushort opcode, uint value)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0xFFEE);
        bus.WriteLong(0x0FF0, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[0] = value;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(value, cpu.State.D[0]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2714, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x8103, 0x7008u)]
    [InlineData(0x0000, 0x7000u)]
    public void WordMovemPostIncrementSignExtendsAndFinalPointerWinsRegisterAlias(ushort mask, uint expectedStack)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x4C9F, mask); // D0,D1,A0,A7 from (A7)+
        bus.WriteWords(0x7000, 0x8001, 0x7FFF, 0xFFFF, 0x1234);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expectedStack, cpu.State.A[7]);
        Assert.Equal(expectedStack, cpu.State.SupervisorStackPointer);
        if (mask != 0)
        {
            Assert.Equal(0xFFFF8001u, cpu.State.D[0]);
            Assert.Equal(0x7FFFu, cpu.State.D[1]);
            Assert.Equal(uint.MaxValue, cpu.State.A[0]);
        }
        Assert.Equal(0x271B, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0379, 0xFFFFFFFFu, 0xF7FFFFFFu, 0x271B)]
    [InlineData(0x03B9, 0xFFFFFFFFu, 0xF7FFFFFFu, 0x271B)]
    [InlineData(0x03F9, 0xF7FFFFFFu, 0xFFFFFFFFu, 0x271F)]
    public void DynamicAbsoluteBitOperationsWrapBitNumberAndOnlyChangeZeroFlag(ushort opcode, uint initial, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x0000, 0x3000);
        bus.WriteLong(0x3000, initial);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[1] = 35;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x1E38, 0x12345689u)]
    [InlineData(0x3E38, 0x123489ABu)]
    public void AbsoluteWordByteAndWordLoadsPreserveDataRegisterUpperBits(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus();
        M68kInterpreterTestHelpers.WriteWords(bus, 0x1000, opcode, 0x8030);
        bus.WriteLong(0xFF8030, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[7] = 0x12345678;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[7]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xBE38, 0xFFFF0089u)]
    [InlineData(0xBE78, 0xFFFF89ABu)]
    [InlineData(0xBEB8, 0x89ABCDEFu)]
    public void AbsoluteWordComparePreservesRegisterAndExtend(ushort opcode, uint value)
    {
        var bus = new ZeroWaitCodeBus();
        M68kInterpreterTestHelpers.WriteWords(bus, 0x1000, opcode, 0x8030);
        bus.WriteLong(0xFF8030, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[7] = value;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(value, cpu.State.D[7]);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x2714, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x50F8, 0xFFFF5678u)]
    [InlineData(0x51F8, 0xFF005678u)]
    [InlineData(0x57F8, 0xFFFF5678u)]
    [InlineData(0x56F8, 0xFF005678u)]
    public void AbsoluteWordSetConditionWritesOneByteAndPreservesFlags(ushort opcode, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x3001);
        bus.WriteLong(0x3000, 0xFF125678);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(0x271F, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x50D7, 0x4E71, 0x7000u, 0x7000u)]
    [InlineData(0x50DF, 0x4E71, 0x7000u, 0x7002u)]
    [InlineData(0x50E7, 0x4E71, 0x6FFEu, 0x6FFEu)]
    [InlineData(0x50EF, 0xFFFE, 0x6FFEu, 0x7000u)]
    [InlineData(0x50F7, 0x1202, 0x6FFEu, 0x7000u)]
    public void SetConditionMemoryFormsRespectEaAndStackByteAlignment(ushort opcode, ushort extension, uint target, uint expectedStack)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, extension);
        bus.WriteLong(target, 0x12345678);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[1] = 0xFFFE;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(0xFF345678u, bus.ReadLong(target));
        Assert.Equal(expectedStack, cpu.State.A[7]);
        Assert.Equal(expectedStack, cpu.State.SupervisorStackPointer);
        Assert.Equal(0x271B, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x0891, 0xFF345678u, 0xF7345678u, 0x271B)]
    [InlineData(0x08D1, 0xF7345678u, 0xFF345678u, 0x271F)]
    public void ImmediateIndirectBitOperationsUseModuloEightAndPreserveFlags(ushort opcode, uint initial, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x0023);
        bus.WriteLong(0x3000, initial);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[1] = 0x3000;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x0004, 0x6FFCu)]
    [InlineData(0xFFFC, 0x7004u)]
    public void WordSubaDisplacementSignExtendsWithoutChangingFlags(ushort value, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x9EEE, 0xFFFE);
        bus.WriteWords(0x3000, value);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[6] = 0x3002;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.A[7]);
        Assert.Equal(expected, cpu.State.SupervisorStackPointer);
        Assert.Equal(0x271B, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x11F8, 0x89FFFFFFu)]
    [InlineData(0x31F8, 0x89ABFFFFu)]
    [InlineData(0x21F8, 0x89ABCDEFu)]
    public void AbsoluteWordMemoryMoveUsesBothExtensions(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus();
        M68kInterpreterTestHelpers.WriteWords(bus, 0x1000, opcode, 0x8030, 0x4001);
        bus.WriteLong(0xFF8030, 0x89ABCDEF);
        bus.WriteLong(0x4001, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.ExecuteInstruction();
        uint actual = 0;
        for (var i = 0u; i < 4; i++) actual = (actual << 8) | M68kInterpreterTestHelpers.ReadByte(bus, 0x4001 + i);
        Assert.Equal(expected, actual);
        Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        Assert.Equal(0x2708, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x4638, 0x55CC1234u)]
    [InlineData(0x4678, 0x55331234u)]
    [InlineData(0x46B8, 0x5533EDCBu)]
    public void AbsoluteWordNotRespectsOperandWidthAndPreservesExtend(ushort opcode, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x3000);
        bus.WriteLong(0x3000, 0xAACC1234);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(0x2710, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x4EB8, 0x6FFCu)]
    [InlineData(0x4EF8, 0x7000u)]
    public void AbsoluteWordJumpsFlushSequentialFetchAndJsrSavesReturnPc(ushort opcode, uint stack)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x2000, 0x7001);
        bus.WriteWords(0x2000, 0x7042, 0x4E75);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.ExecuteInstruction();
        Assert.Equal(0x2000u, cpu.State.ProgramCounter);
        Assert.Equal(stack, cpu.State.A[7]);
        cpu.ExecuteInstruction();
        Assert.Equal(0x42u, cpu.State.D[0]);
        if (opcode == 0x4EB8)
        {
            Assert.Equal(0x1004u, bus.ReadLong(stack));
            cpu.ExecuteInstruction();
            Assert.Equal(0x1004u, cpu.State.ProgramCounter);
            Assert.Equal(0x7000u, cpu.State.A[7]);
        }
    }

    [Fact]
    public void LongIndexedAddaReadsBeforeUpdatingAliasedBaseAndPreservesFlags()
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0xDDF6, 0x2010);
        bus.WriteLong(0x300C, 0xFFFFF000);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[6] = 0x3000;
        cpu.State.D[2] = 0xFFFC;
        cpu.State.StatusRegister = 0x271B;
        cpu.ExecuteInstruction();
        Assert.Equal(0x2000u, cpu.State.A[6]);
        Assert.Equal(0x271B, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xE210, 1u, 0x2710, 0x800180C0u, 0x2719)]
    [InlineData(0xE250, 1u, 0x2710, 0x8001C040u, 0x2719)]
    [InlineData(0xE290, 1u, 0x2710, 0xC000C040u, 0x2719)]
    [InlineData(0xE310, 1u, 0x2700, 0x80018002u, 0x2711)]
    [InlineData(0xE350, 1u, 0x2700, 0x80010102u, 0x2711)]
    [InlineData(0xE390, 1u, 0x2700, 0x00030102u, 0x2711)]
    [InlineData(0xE230, 1u, 0x2700, 0x80018040u, 0x2711)]
    [InlineData(0xE270, 1u, 0x2700, 0x80014040u, 0x2711)]
    [InlineData(0xE2B0, 1u, 0x2700, 0x4000C040u, 0x2711)]
    [InlineData(0xE330, 1u, 0x2710, 0x80018003u, 0x2711)]
    [InlineData(0xE370, 1u, 0x2710, 0x80010103u, 0x2711)]
    [InlineData(0xE3B0, 1u, 0x2710, 0x00030103u, 0x2711)]
    [InlineData(0xE230, 0u, 0x2710, 0x80018081u, 0x2719)]
    [InlineData(0xE230, 9u, 0x2710, 0x80018081u, 0x2719)]
    [InlineData(0xE230, 64u, 0x2700, 0x80018081u, 0x2708)]
    public void RotateThroughExtendUsesTheCarryRingIncludingZeroAndWrappedCounts(ushort opcode, uint count, ushort initialFlags, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[0] = 0x80018081;
        cpu.State.D[1] = count;
        cpu.State.StatusRegister = initialFlags;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]);
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x271B, 0xFFE4, 0x2704)]
    [InlineData(0x001B, 0xFFFF, 0x001F)]
    public void ImmediateMoveToCcrWorksInUserModeAndCannotChangeSupervisorBits(ushort before, ushort immediate, ushort after)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x44FC, immediate);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.StatusRegister = before;
        cpu.ExecuteInstruction();
        Assert.Equal(after, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x5313, 0x00345678u, 0xFF345678u, 0x2719)]
    [InlineData(0x5353, 0x00005678u, 0xFFFF5678u, 0x2719)]
    [InlineData(0x5253, 0x7FFF5678u, 0x80005678u, 0x270A)]
    [InlineData(0x5153, 0x00085678u, 0x00005678u, 0x2704)]
    public void QuickIndirectOperationsRespectWidthAndEncodedEight(ushort opcode, uint initial, uint expected, ushort flags)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode);
        bus.WriteLong(0x3000, initial);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[3] = 0x3000;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x3000));
        Assert.Equal(flags, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x1920, 1u, false)]
    [InlineData(0x3920, 2u, false)]
    [InlineData(0x2920, 4u, false)]
    [InlineData(0x1F27, 2u, true)]
    [InlineData(0x3F27, 2u, true)]
    [InlineData(0x2F27, 4u, true)]
    public void PredecrementMoveReadsSourceBeforeUpdatingAliasedDestination(ushort opcode, uint step, bool aliasedStack)
    {
        var bus = new ZeroWaitCodeBus();
        M68kInterpreterTestHelpers.WriteWords(bus, 0x1000, opcode);
        var source = aliasedStack ? 0x7000u : 0x3000u;
        var destination = aliasedStack ? source - step : 0x4000u;
        bus.WriteLong(source - step, 0x89ABCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = source;
        cpu.State.A[4] = destination;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        var width = (opcode >> 12) == 1 ? 1u : step;
        for (var i = 0u; i < width; i++)
            Assert.Equal((byte)(0x89ABCDEFu >> (int)(24 - 8 * i)), M68kInterpreterTestHelpers.ReadByte(bus, destination - step + i));
        Assert.Equal(destination - step, cpu.State.A[aliasedStack ? 7 : 4]);
        if (aliasedStack) Assert.Equal(destination - step, cpu.State.SupervisorStackPointer);
        else Assert.Equal(source - step, cpu.State.A[0]);
        Assert.Equal(0x2718, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0x13F0, 0x89FFFFFFu)]
    [InlineData(0x33F0, 0x89ABFFFFu)]
    [InlineData(0x23F0, 0x89ABCDEFu)]
    [InlineData(0x13FB, 0x89FFFFFFu)]
    [InlineData(0x33FB, 0x89ABFFFFu)]
    [InlineData(0x23FB, 0x89ABCDEFu)]
    public void IndexedToAbsoluteLongMoveUsesSignedScaledIndexAndCorrectPcBase(ushort opcode, uint expected)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, opcode, 0x24FE, 0, 0x4000);
        bus.WriteLong((opcode & 7) == 3 ? 0x0FF8u : 0x2FF6u, 0x89ABCDEF);
        bus.WriteLong(0x4000, uint.MaxValue);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[0] = 0x3000;
        cpu.State.D[2] = 0xFFFF_FFFE;
        cpu.State.StatusRegister = 0x2713;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000));
        Assert.Equal(0x1008u, cpu.State.ProgramCounter);
        Assert.Equal(0x2718, cpu.State.StatusRegister);
    }

    private sealed class BareBus(IM68kBus bus) : IM68kBus
    {
        public byte ReadByte(uint a, ref long c, M68kBusAccessKind k) => bus.ReadByte(a, ref c, k);
        public ushort ReadWord(uint a, ref long c, M68kBusAccessKind k) => bus.ReadWord(a, ref c, k);
        public uint ReadLong(uint a, ref long c, M68kBusAccessKind k) => bus.ReadLong(a, ref c, k);
        public void WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) => bus.WriteByte(a, v, ref c, k);
        public void WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) => bus.WriteWord(a, v, ref c, k);
        public void WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) => bus.WriteLong(a, v, ref c, k);
        public void ResetExternalDevices(long c) => bus.ResetExternalDevices(c);
    }
}
