using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68020NativeGameOperandTests
{
    private const uint CodeBase = 0xF80000;

    [Fact]
    public void NewIndirectImmediateArithmeticPlansRetainReadModifyWriteBarrier()
    {
        foreach (var key in new[] { M68kInstructionTimingKey.AddiWordImmediateToAddressIndirect,
            M68kInstructionTimingKey.AddiLongImmediateToAddressIndirect })
        {
            Assert.True((M68020TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
            Assert.True((M68030TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
        }
    }

    [Theory]
    [InlineData(0xDEF9, 0xFFFE0000u, 0x3FFEu)] [InlineData(0xDFF9, 0xFFFFFFFCu, 0x3FFCu)]
    public void AddaAbsoluteLongSignExtendsWordAndUpdatesActiveStackWithoutFlags(ushort opcode, uint source, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0, 0x2000); bus.WriteLong(0x2000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.A[7]); Assert.Equal(0x201F, cpu.State.StatusRegister);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter); Assert.Equal(source, bus.ReadLong(0x2000));
    }

    [Theory]
    [InlineData(0x0617, 0x00010000u, 0x7FA55A0Fu, 0x80A55A0Fu, 0x0A)]
    [InlineData(0x0657, 0x00010000u, 0x7FFF5A0Fu, 0x80005A0Fu, 0x0A)]
    [InlineData(0x0697, 0x00000001u, 0xFFFFFFFFu, 0u, 0x15)]
    public void AddiIndirectUsesImmediateWidthAndPreservesTheAddressRegister(ushort opcode, uint immediate,
        uint destination, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, (ushort)(immediate >> 16), (ushort)immediate);
        bus.WriteLong(0x2000, destination); bus.WriteWord(0x2004, 0xA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x2000); cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x2000)); Assert.Equal(0xA55A, bus.ReadWord(0x2004));
        Assert.Equal(0x2000u, cpu.State.A[7]); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + (opcode == 0x0697 ? 6u : 4u), cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x9507, 0xABCD0000u, 1u, 0x1F, 0xABCD00FEu, 0x19)]
    [InlineData(0x9547, 0xABCD0000u, 1u, 0x1F, 0xABCDFFFEu, 0x19)]
    [InlineData(0x9587, 0u, 1u, 0x1F, 0xFFFFFFFEu, 0x19)]
    [InlineData(0x9507, 0xABCD0080u, 1u, 4, 0xABCD007Fu, 2)]
    [InlineData(0x9547, 0xABCD8000u, 1u, 4, 0xABCD7FFFu, 2)]
    [InlineData(0x9587, 0x80000000u, 1u, 4, 0x7FFFFFFFu, 2)]
    [InlineData(0x9507, 0xABCD0001u, 0u, 0x14, 0xABCD0000u, 4)]
    [InlineData(0x9547, 0xABCD0001u, 0u, 0x10, 0xABCD0000u, 0)]
    public void SubxRegisterUsesExtendStickyZeroAndSelectedWidth(ushort opcode, uint destination, uint source,
        int initialFlags, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[2] = destination; cpu.State.D[7] = source;
        cpu.State.StatusRegister = (ushort)(0x2000 | initialFlags);
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[2]); Assert.Equal(source, cpu.State.D[7]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x9502, 0xABCD12FFu)] [InlineData(0x9542, 0xABCDFFFFu)]
    public void SubxAliasedRegisterLatchesBothOperandsBeforeWriting(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[2] = 0xABCD1234; cpu.State.StatusRegister = 0x2014;
        cpu.ExecuteInstruction(); Assert.Equal(expected, cpu.State.D[2]); Assert.Equal(0x19, cpu.State.StatusRegister & 31);
    }

    [Fact]
    public void FullIndexedGameOperandReadsPointerThenOuterDisplacedValue()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x2034, 0x0162, 0x02E0, 0x0018);
        bus.WriteLong(0x22E0, 0x3000); bus.WriteLong(0x3018, 0x12345678);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[4] = 0x2000; cpu.State.D[0] = uint.MaxValue;
        cpu.ExecuteInstruction();
        Assert.Equal(CodeBase + 8, cpu.State.ProgramCounter); Assert.Equal(0x12345678u, cpu.State.D[0]);
        Assert.Equal(0x2000u, cpu.State.A[4]);
    }

    [Theory]
    [InlineData(0xD039, 0xFFFF00FFu, 0x0100, 0xFFFF0000u, 0x15)]
    [InlineData(0xD079, 0xABCD7FFFu, 1, 0xABCD8000u, 0x0A)]
    public void AddAbsoluteLongUsesFullAddressAndSelectedArithmeticWidth(ushort opcode, uint destination, ushort source, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0, 0x2000); bus.WriteWord(0x2000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[0] = destination; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter); Assert.Equal(source, bus.ReadWord(0x2000));
    }

    [Theory]
    [InlineData(0x8000, 0x18)] [InlineData(0, 0x14)]
    public void MoveWordDisplacementToIndexedUsesTheSameUnmodifiedAddressBase(ushort source, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x35AA, 0xFFFE, 0x1002);
        bus.WriteWord(0x2000, source); bus.WriteWord(0x2006, 0xFFFF); bus.WriteWord(0x2008, 0xA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[2] = 0x2002; cpu.State.D[1] = 2; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(source, bus.ReadWord(0x2006)); Assert.Equal(0xA55A, bus.ReadWord(0x2008));
        Assert.Equal(0x2002u, cpu.State.A[2]); Assert.Equal(2u, cpu.State.D[1]);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter); Assert.Equal(flags, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(0x8000, 0x18)] [InlineData(0, 0x14)]
    public void MoveWordPcIndexedToDisplacementKeepsBothExtensionBases(ushort source, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x397B, 0x14FE, 0xFFFE); bus.WriteWord(CodeBase - 8, source);
        bus.WriteWord(0x2000, 0xFFFF); bus.WriteWord(0x2002, 0xA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[4] = 0x2002; cpu.State.D[1] = 0x1234FFFE; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(source, bus.ReadWord(0x2000)); Assert.Equal(0xA55A, bus.ReadWord(0x2002));
        Assert.Equal(0x2002u, cpu.State.A[4]); Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter); Assert.Equal(flags, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(0x0151, 0x8000, 0x1B)] [InlineData(0x0191, 0x0000, 0x1B)]
    [InlineData(0x01D1, 0x8000, 0x1B)] [InlineData(0x01D1, 0x8000, 0x1F)]
    public void DynamicIndirectBitUpdatesUseModuloEightAndOriginalZero(ushort opcode, ushort expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode);
        bus.WriteWord(0x2000, flags == 0x1B && opcode != 0x0151 ? (ushort)0x80AA : (ushort)0x00AA);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[1] = 0x2000; cpu.State.D[0] = 15; cpu.State.StatusRegister = 0x201B;
        cpu.ExecuteInstruction();
        Assert.Equal((ushort)(expected | 0xAA), bus.ReadWord(0x2000));
        Assert.Equal(opcode == 0x0151 ? 0x1F : flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0x2000u, cpu.State.A[1]); Assert.Equal(15u, cpu.State.D[0]);
        Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0639, 0xFF00, 1, 0x0000, 0x15)]
    [InlineData(0x0679, 0x7FFF, 1, 0x8000, 0x0A)]
    [InlineData(0x06B9, 0xFFFF, 1, 0, 0x15)]
    public void AddiAbsoluteLongConsumesImmediateBeforeAddressAndUpdatesSelectedWidth(ushort opcode, ushort initial, ushort immediate, ushort expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        if (opcode == 0x06B9) WriteWords(bus, CodeBase, opcode, 0, immediate, 0, 0x2000);
        else WriteWords(bus, CodeBase, opcode, immediate, 0, 0x2000);
        WriteWords(bus, 0x2000, initial, opcode == 0x06B9 ? (ushort)0xFFFF : (ushort)0xABCD);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadWord(0x2000)); Assert.Equal(opcode == 0x06B9 ? 0 : 0xABCD, bus.ReadWord(0x2002));
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + (opcode == 0x06B9 ? 10u : 8u), cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0xD03B, 0xFFFF00FFu, 0x01000000u, 0xFFFF0000u, 0x15)]
    [InlineData(0xD07B, 0xABCD7FFFu, 0x00010000u, 0xABCD8000u, 0x0A)]
    [InlineData(0xD0BB, 0xFFFFFFFFu, 1u, 0u, 0x15)]
    [InlineData(0x903B, 0xABCD0000u, 0x01000000u, 0xABCD00FFu, 0x19)]
    [InlineData(0x907B, 0xABCD0000u, 0x00010000u, 0xABCDFFFFu, 0x19)]
    [InlineData(0x90BB, 0u, 1u, 0xFFFFFFFFu, 0x19)]
    public void ArithmeticPcIndexedUsesTheOriginalIndexAndSelectedOperandWidth(ushort opcode, uint destination, uint source, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0x14FE); bus.WriteLong(CodeBase - 8, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[1] = 0x1234FFFE; cpu.State.D[0] = destination; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]); Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x5479, 0xFFFE, 0, 0x15)] [InlineData(0x5079, 0x7FF8, 0x8000, 0x0A)]
    public void AddqWordAbsoluteUsesZeroEncodedEightAndWritesOnlyAWord(ushort opcode, ushort initial, ushort expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0, 0x2000); WriteWords(bus, 0x2000, initial, 0xABCD);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadWord(0x2000)); Assert.Equal(0xABCD, bus.ReadWord(0x2002));
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x461B, 0x0FA55A0Fu, 1u)] [InlineData(0x465B, 0x0F5A5A0Fu, 2u)]
    [InlineData(0x469B, 0x0F5AA5F0u, 4u)] [InlineData(0x461F, 0x0FA55A0Fu, 2u)]
    public void NotPostIncrementUsesOriginalAddressAndByteStackStride(ushort opcode, uint expected, uint stride)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); bus.WriteLong(0x2000, 0xF0A55A0F);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x2000); cpu.State.A[3] = 0x2000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, bus.ReadLong(0x2000)); Assert.Equal(0x10, cpu.State.StatusRegister & 31);
        Assert.Equal(0x2000u + stride, cpu.State.A[opcode & 7]); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x80000000u, 0x18)] [InlineData(0x00008000u, 0x10)] [InlineData(0u, 0x14)]
    public void TstLongAbsoluteUsesFullWidthWithoutWriting(uint source, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x4AB9, 0, 0x2000); bus.WriteLong(0x2000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(source, bus.ReadLong(0x2000)); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x80000001u, 0x18)] [InlineData(0u, 0x14)]
    public void MoveLongAbsoluteToAbsoluteConsumesBothAddressesAndWritesBothWords(uint source, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x23F9, 0, 0x3000, 0, 0x2000);
        bus.WriteLong(0x3000, source); bus.WriteLong(0x2000, uint.MaxValue); bus.WriteWord(0x2004, 0xA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(source, bus.ReadLong(0x2000)); Assert.Equal(source, bus.ReadLong(0x3000)); Assert.Equal(0xA55A, bus.ReadWord(0x2004));
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 10, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x4613, 0x0FA55A0Fu)] [InlineData(0x4653, 0x0F5A5A0Fu)] [InlineData(0x4693, 0x0F5AA5F0u)]
    public void NotIndirectWritesOnlySelectedWidthAndKeepsTheAddress(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); bus.WriteLong(0x2000, 0xF0A55A0F);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[3] = 0x2000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, bus.ReadLong(0x2000)); Assert.Equal(0x10, cpu.State.StatusRegister & 31);
        Assert.Equal(0x2000u, cpu.State.A[3]); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x30CB, 0x12348000u, 0x8000, 0x18)]
    [InlineData(0x30CB, 0x12340000u, 0, 0x14)]
    [InlineData(0x30C8, 0x2000u, 0x2000, 0x10)]
    public void MoveWordAddressToPostIncrementLatchesSourceBeforeAliasedIncrement(ushort opcode, uint source, ushort expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); WriteWords(bus, 0x2000, 0xFFFF, 0xA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[0] = 0x2000; cpu.State.A[3] = source; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, bus.ReadWord(0x2000)); Assert.Equal(0xA55A, bus.ReadWord(0x2002));
        Assert.Equal(0x2002u, cpu.State.A[0]); Assert.Equal(source, cpu.State.A[3]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x8000, 0x18)] [InlineData(0, 0x14)]
    public void MoveWordPcDisplacementToAbsoluteLongLatchesSourceBeforeDestinationExtensions(ushort source, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, 0x33FA, 0xFFFA, 0, 0x2000); bus.WriteWord(CodeBase - 4, source);
        WriteWords(bus, 0x2000, 0xFFFF, 0xA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(source, bus.ReadWord(0x2000)); Assert.Equal(0xA55A, bus.ReadWord(0x2002));
        Assert.Equal(CodeBase + 8, cpu.State.ProgramCounter); Assert.Equal(flags, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(0x33D7, 0x8000, 0x18)] [InlineData(0x33D7, 0, 0x14)]
    [InlineData(0x30F9, 0x8000, 0x18)] [InlineData(0x30F9, 0, 0x14)]
    public void MoveWordMemoryFormsKeepFlagsAndAddressUpdatesSized(ushort opcode, ushort source, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0, opcode == 0x33D7 ? (ushort)0x2000 : (ushort)0x4000);
        WriteWords(bus, 0x4000, source, 0xABCD); WriteWords(bus, 0x2000, 0xFFFF, 0xA55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[0] = 0x2000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(source, bus.ReadWord(0x2000)); Assert.Equal(0xA55A, bus.ReadWord(0x2002));
        Assert.Equal(0x4000u, cpu.State.A[7]); Assert.Equal(opcode == 0x30F9 ? 0x2002u : 0x2000u, cpu.State.A[0]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0xC039, 0xFFFF00F0u)] [InlineData(0xC079, 0xFFFFF0A5u)] [InlineData(0xC0B9, 0xF0A55A0Fu)]
    public void AndAbsoluteLongConsumesFullAddressAndPreservesUpperRegisterBytes(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0, 0x2000); bus.WriteLong(0x2000, 0xF0A55A0F);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[0] = opcode == 0xC039 ? 0xFFFF00FFu : uint.MaxValue;
        cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter); Assert.Equal(0xF0A55A0Fu, bus.ReadLong(0x2000));
    }

    [Theory]
    [InlineData(0x4239, 0x00A55A0Fu)] [InlineData(0x4279, 0x00005A0Fu)] [InlineData(0x42B9, 0u)]
    [InlineData(0x4639, 0x0FA55A0Fu)] [InlineData(0x4679, 0x0F5A5A0Fu)] [InlineData(0x46B9, 0x0F5AA5F0u)]
    public void UnaryAbsoluteLongChangesOnlySelectedBytesAndSetsLogicalFlags(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0, 0x2000); bus.WriteLong(0x2000, 0xF0A55A0F);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x2000));
        Assert.Equal((opcode & 0x0400) == 0 ? 0x14 : 0x10, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0000)] [InlineData(0x0203)] [InlineData(0xFFFF)]
    public void MovemWordIndirectSignExtendsAndKeepsTheOriginalBaseForEveryRead(ushort mask)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x4C91, mask);
        for (var i = 0; i < 16; i++) bus.WriteWord(0x2000u + (uint)i * 2, (ushort)(0x8000 + i));
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[1] = 0x2000; cpu.State.StatusRegister = 0x201F;
        var originalD = cpu.State.D.ToArray(); var originalA = cpu.State.A.ToArray();
        cpu.ExecuteInstruction();
        var ordinal = 0u;
        for (var register = 0; register < 16; register++)
        {
            var expected = (mask & (1 << register)) != 0 ? 0xFFFF8000u + ordinal++ :
                register < 8 ? originalD[register] : originalA[register - 8];
            Assert.Equal(expected, register < 8 ? cpu.State.D[register] : cpu.State.A[register - 8]);
        }
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter); Assert.Equal(0x201F, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(0xD03A, 0xABCD00FFu, 0x0100, 0xABCD0000u, 0x15)]
    [InlineData(0xD07A, 0xABCD7FFFu, 0x0001, 0xABCD8000u, 0x0A)]
    [InlineData(0x903A, 0xABCD0000u, 0x0100, 0xABCD00FFu, 0x19)]
    [InlineData(0x907A, 0xABCD0000u, 0x0001, 0xABCDFFFFu, 0x19)]
    public void ArithmeticPcDisplacementUsesExtensionAddressAndSizedFlags(ushort opcode, uint destination, ushort source, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, opcode, 0xFFFA); WriteWords(bus, CodeBase - 4, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[0] = destination; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter); Assert.Equal(source, bus.ReadWord(CodeBase - 4));
    }

    [Fact]
    public void SubLongPcDisplacementReadsBothWordsAndSetsBorrow()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x90BA, 0xFFFA); bus.WriteLong(CodeBase - 4, 1);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[0] = 0; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(uint.MaxValue, cpu.State.D[0]);
        Assert.Equal(0x19, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0xC03B, 0xFFFF00F0u)]
    [InlineData(0xC07B, 0xFFFFF0A5u)]
    [InlineData(0xC0BB, 0xF0A55A0Fu)]
    public void AndPcIndexedUsesExtensionAddressSignedWordIndexAndSelectedWidth(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, opcode, 0x14FE); // D1.W * 4, displacement -2; PC base is extension address.
        WriteWords(bus, CodeBase - 8, 0xF0A5, 0x5A0F);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[1] = 0x1234FFFE; cpu.State.D[0] = uint.MaxValue;
        if (opcode == 0xC03B) cpu.State.D[0] = 0xFFFF00FF;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(0x1234FFFEu, cpu.State.D[1]);
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
        Assert.Equal(0xF0A5, bus.ReadWord(CodeBase - 8));
    }

    [Theory]
    [InlineData(0xC03B)] [InlineData(0xC07B)]
    public void AndPcIndexedZeroClearsNegativeOverflowAndCarry(ushort opcode)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0x1806, 0x4E71, 0, 0);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.D[1] = 0; cpu.State.D[0] = uint.MaxValue;
        cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(opcode == 0xC03B ? 0xFFFFFF00u : 0xFFFF0000u, cpu.State.D[0]);
        Assert.Equal(0x14, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(0xD0D9, 0xFFFE0000u, 0xFFEu, 2u)]
    [InlineData(0xD1D9, 0xFFFFFFFCu, 0xFFCu, 4u)]
    [InlineData(0xD0D1, 0xFFFE0000u, 0xFFEu, 0u)]
    [InlineData(0xD1D1, 0xFFFFFFFCu, 0xFFCu, 0u)]
    public void AddaPostIncrementLatchesSourceAndPreservesAllConditionCodes(ushort opcode, uint source, uint expected, uint stride)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); bus.WriteLong(0x2000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[0] = 0x1000; cpu.State.A[1] = 0x2000;
        cpu.State.StatusRegister = 0x201F; cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.A[0]); Assert.Equal(0x2000u + stride, cpu.State.A[1]);
        Assert.Equal(0x201F, cpu.State.StatusRegister); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
        Assert.Equal(source, bus.ReadLong(0x2000));
    }

    [Theory]
    [InlineData(0xD0D8, 0xFFFE0000u)] [InlineData(0xD1D8, 0xFFFFFFFCu)]
    public void AddaAliasedPostIncrementIncludesTheIncrementInDestination(ushort opcode, uint source)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); bus.WriteLong(0x2000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[0] = 0x2000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(0x2000u, cpu.State.A[0]); Assert.Equal(0x201F, cpu.State.StatusRegister);
    }
}
