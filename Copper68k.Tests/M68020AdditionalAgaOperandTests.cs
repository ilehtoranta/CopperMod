using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68020AdditionalAgaOperandTests
{
    private const uint Code = 0xF80000;

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0xE702, 0x0C, 0x60, 0x00)]
    [InlineData(M68kCpuModel.M68EC020, 0xE302, 0x40, 0x80, 0x0A)]
    [InlineData(M68kCpuModel.M68EC020, 0xE702, 0x40, 0x00, 0x06)]
    [InlineData(M68kCpuModel.M68EC020, 0xE102, 0x01, 0x00, 0x17)]
    [InlineData(M68kCpuModel.M68020, 0xE702, 0x0C, 0x60, 0x00)]
    [InlineData(M68kCpuModel.M68030, 0xE702, 0x0C, 0x60, 0x00)]
    [InlineData(M68kCpuModel.M68040, 0xE702, 0x0C, 0x60, 0x00)]
    public void AslByteImmediateRetainsUpperBitsLastCarryAndIntermediateOverflow(M68kCpuModel model, ushort opcode, int source, int expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.D[2] = 0xABCD1200u | (uint)source;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(0xABCD1200u | (uint)expected, cpu.State.D[2]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x5239, 0x7FA55AA5u, 0x80A55AA5u, 0x0A)]
    [InlineData(0x5239, 0xFFA55AA5u, 0x00A55AA5u, 0x15)]
    [InlineData(0x5039, 0xF8A55AA5u, 0x00A55AA5u, 0x15)]
    public void AddqByteAbsoluteLongRetainsAddressWidthQuickEightAndFlags(ushort opcode, uint initial, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode, 0x001E, 0x1234);
        bus.WriteLong(0x1E1234, initial);
        bus.WriteLong(0x1234, 0xDEADBEEF);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x1E1234));
        Assert.Equal(0xDEADBEEFu, bus.ReadLong(0x1234));
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x8210, 0xA5A50001u, 0x80A55AA5u, 0xA5A50081u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x8250, 0xA5A50010u, 0x80005AA5u, 0xA5A58010u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x8210, 0xA5A50000u, 0x005AA55Au, 0xA5A50000u, 0x14)]
    [InlineData(M68kCpuModel.M68020, 0x8210, 0xA5A50001u, 0x80A55AA5u, 0xA5A50081u, 0x18)]
    [InlineData(M68kCpuModel.M68030, 0x8210, 0xA5A50001u, 0x80A55AA5u, 0xA5A50081u, 0x18)]
    [InlineData(M68kCpuModel.M68040, 0x8210, 0xA5A50001u, 0x80A55AA5u, 0xA5A50081u, 0x18)]
    public void OrIndirectPreservesUpperDataBitsSourceAndAddress(M68kCpuModel model, ushort opcode, uint destination, uint source, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode);
        bus.WriteLong(0x2000, source);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.A[0] = 0x2000;
        cpu.State.D[1] = destination;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[1]);
        Assert.Equal(source, bus.ReadLong(0x2000));
        Assert.Equal(0x2000u, cpu.State.A[0]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x065A, 0x7FFFA55Au, 0x8000A55Au, 2u, 0x0A)]
    [InlineData(0x065A, 0xFFFFA55Au, 0x0000A55Au, 2u, 0x15)]
    [InlineData(0x061F, 0xFF5AA55Au, 0x005AA55Au, 2u, 0x15)]
    [InlineData(0x069A, 0xFFFFFFFFu, 0u, 4u, 0x15)]
    public void AddiPostIncrementUsesSizedArithmeticAndStackStride(ushort opcode, uint initial, uint expected, uint stride, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        if ((opcode & 0xC0) == 0x80) WriteWords(bus, Code, opcode, 0, 1);
        else WriteWords(bus, Code, opcode, 1);
        bus.WriteLong(0x2000, initial);
        bus.WriteWord(0x2004, 0x5AA5);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x2000);
        cpu.State.A[opcode & 7] = 0x2000;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x2000));
        Assert.Equal(0x5AA5, bus.ReadWord(0x2004));
        Assert.Equal(0x2000u + stride, cpu.State.A[opcode & 7]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + ((opcode & 0xC0) == 0x80 ? 6u : 4u), cpu.State.ProgramCounter);
        if ((opcode & 7) == 7) Assert.Equal(0x2000u + stride, cpu.State.InterruptStackPointer);
    }

    [Fact]
    public void NewMemoryArithmeticPlansRetainReadModifyWriteBarrier()
    {
        foreach (var key in new[] { M68kInstructionTimingKey.EorByteDataToAddressIndirect, M68kInstructionTimingKey.EorWordDataToAddressIndirect, M68kInstructionTimingKey.EorLongDataToAddressIndirect,
            M68kInstructionTimingKey.AddiByteImmediateToPostIncrement, M68kInstructionTimingKey.AddiWordImmediateToPostIncrement, M68kInstructionTimingKey.AddiLongImmediateToPostIncrement,
            M68kInstructionTimingKey.AddqByteAbsoluteLong })
        {
            Assert.True((M68020TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
            Assert.True((M68030TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
        }
    }

    [Theory]
    [InlineData(0xB353, 0xAAAA5678u, 0xA55A5AA5u, 0xF3225AA5u, 0x18)]
    [InlineData(0xB313, 0xFFFF0080u, 0x80A55AA5u, 0x00A55AA5u, 0x14)]
    [InlineData(0xB393, 0x12345678u, 0x12345678u, 0u, 0x14)]
    public void EorIndirectModifiesOnlySelectedMemoryWidthAndPreservesSource(ushort opcode, uint source, uint initial, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode);
        bus.WriteLong(0x2000, initial);
        bus.WriteWord(0x2004, 0x5AA5);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.D[1] = source;
        cpu.State.A[3] = 0x2000;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x2000));
        Assert.Equal(0x5AA5, bus.ReadWord(0x2004));
        Assert.Equal(source, cpu.State.D[1]);
        Assert.Equal(0x2000u, cpu.State.A[3]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0xB220, 0x00000473u, 0x73u, 0x14)]
    [InlineData(0xB220, 0xABCD0080u, 1u, 0x12)]
    [InlineData(0xB220, 0xABCD0000u, 1u, 0x19)]
    [InlineData(0xB260, 0xABCD8000u, 1u, 0x12)]
    [InlineData(0xB260, 0xABCD0000u, 1u, 0x19)]
    [InlineData(0xB2A0, 0x80000000u, 1u, 0x12)]
    [InlineData(0xB2A0, 0u, 1u, 0x19)]
    [InlineData(0xB227, 0xABCD005Au, 0x5Au, 0x14)]
    public void CmpPredecrementReadsSelectedWidthPreservesOperandsAndExtend(ushort opcode, uint destination, uint source, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode);
        var sizeCode = (opcode >> 6) & 3;
        var sourceRegister = opcode & 7;
        var stride = sizeCode == 0 ? (sourceRegister == 7 ? 2u : 1u) : sizeCode == 1 ? 2u : 4u;
        const uint operand = 0x2000;
        if (sizeCode == 0) bus.WriteWord(operand, (ushort)((source << 8) | 0xA5));
        else if (sizeCode == 1) WriteWords(bus, operand, (ushort)source, 0xA55A);
        else bus.WriteLong(operand, source);
        var originalMemory = bus.ReadLong(operand);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, operand + stride);
        cpu.State.A[sourceRegister] = operand + stride;
        cpu.State.D[1] = destination;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(destination, cpu.State.D[1]);
        Assert.Equal(operand, cpu.State.A[sourceRegister]);
        Assert.Equal(originalMemory, bus.ReadLong(operand));
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + 2, cpu.State.ProgramCounter);
        if (sourceRegister == 7) Assert.Equal(operand, cpu.State.InterruptStackPointer);
    }

    [Theory]
    [InlineData(0x20F8, 0x0068, 0x80000000u, 0x80000000u, 4u, 0x18)]
    [InlineData(0x20F8, 0xFFF0, 0x12345678u, 0x12345678u, 4u, 0x10)]
    [InlineData(0x30F8, 0x0068, 0x80005AA5u, 0x8000A55Au, 2u, 0x18)]
    [InlineData(0x10F8, 0x0068, 0x005AA55Au, 0x00A5A55Au, 1u, 0x14)]
    [InlineData(0x1EF8, 0x0068, 0x805AA55Au, 0x80A5A55Au, 2u, 0x18)]
    public void MoveAbsoluteWordToPostIncrementSignExtendsAddressAndWritesSelectedWidth(ushort opcode, ushort extension, uint source, uint expected, uint stride, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode, extension);
        var sourceAddress = unchecked((uint)(int)(short)extension);
        bus.WriteLong(sourceAddress, source);
        bus.WriteLong(0x2000, 0xA5A5A55A);
        bus.WriteWord(0x2004, 0x5AA5);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x2000);
        var destination = (opcode >> 9) & 7;
        cpu.State.A[destination] = 0x2000;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(source, bus.ReadLong(sourceAddress));
        Assert.Equal(expected, bus.ReadLong(0x2000));
        Assert.Equal(0x5AA5, bus.ReadWord(0x2004));
        Assert.Equal(0x2000u + stride, cpu.State.A[destination]);
        Assert.Equal(Code + 4, cpu.State.ProgramCounter);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        if (destination == 7) Assert.Equal(0x2000u + stride, cpu.State.InterruptStackPointer);
    }

    [Fact]
    public void MoveAbsoluteWordToAliasedPostIncrementReadsBeforeWriting()
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, 0x20F8, 0x0068);
        bus.WriteLong(0x68, 0x87654321);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.A[0] = 0x68;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(0x87654321u, bus.ReadLong(0x68));
        Assert.Equal(0x6Cu, cpu.State.A[0]);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(0x36B9, 0x80005AA5u, 0x8000A55Au, 0x18)]
    [InlineData(0x36B9, 0x00005AA5u, 0x0000A55Au, 0x14)]
    [InlineData(0x16B9, 0x805AA55Au, 0x80A5A55Au, 0x18)]
    public void MoveAbsoluteLongToIndirectUsesFullSourceAddressAndSelectedWidth(ushort opcode, uint source, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode, 0x001E, 0x1234);
        bus.WriteLong(0x1E1234, source);
        bus.WriteLong(0x1234, 0xDEADBEEF);
        bus.WriteLong(0x2000, 0xA5A5A55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.A[3] = 0x2000;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(source, bus.ReadLong(0x1E1234));
        Assert.Equal(0xDEADBEEFu, bus.ReadLong(0x1234));
        Assert.Equal(expected, bus.ReadLong(0x2000));
        Assert.Equal(0x2000u, cpu.State.A[3]);
        Assert.Equal(Code + 6, cpu.State.ProgramCounter);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020, 0xB220)]
    [InlineData(M68kCpuModel.M68030, 0xB220)]
    [InlineData(M68kCpuModel.M68040, 0xB220)]
    [InlineData(M68kCpuModel.M68020, 0x20F8)]
    [InlineData(M68kCpuModel.M68030, 0x20F8)]
    [InlineData(M68kCpuModel.M68040, 0x20F8)]
    public void CapturedFormsRetainSharedModelsAnd040Fallback(M68kCpuModel model, ushort opcode)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode, 0x0068);
        bus.WriteLong(0x68, 0x12345678);
        bus.WriteWord(0x2000, 0x73A5);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.A[0] = opcode == 0xB220 ? 0x2001u : 0x2000u;
        cpu.State.D[1] = 0x473;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(opcode == 0xB220 ? 0x2000u : 0x2004u, cpu.State.A[0]);
        Assert.Equal(0x473u, cpu.State.D[1]);
        Assert.Equal(opcode == 0xB220 ? 0x14 : 0x10, cpu.State.StatusRegister & 31);
        Assert.Equal(opcode == 0xB220 ? 0x73A50000u : 0x12345678u, bus.ReadLong(0x2000));
        Assert.Equal(Code + (opcode == 0xB220 ? 2u : 4u), cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x21D8, 0x0068, 0x87654321u, 0x87654321u, 4u, 0x18)]
    [InlineData(0x31D8, 0xFFF0, 0x80005AA5u, 0x8000A55Au, 2u, 0x18)]
    [InlineData(0x11DF, 0xFFF0, 0x005AA55Au, 0x00A5A55Au, 2u, 0x14)]
    public void MovePostIncrementToAbsoluteWordKeepsSignedDestinationAndSourceStride(ushort opcode, ushort extension, uint source, uint expected, uint stride, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, Code, opcode, extension);
        bus.WriteLong(0x2000, source);
        var destination = unchecked((uint)(int)(short)extension);
        bus.WriteLong(destination, 0xA5A5A55A);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(Code, 0x2000);
        cpu.State.A[opcode & 7] = 0x2000;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(destination));
        Assert.Equal(source, bus.ReadLong(0x2000));
        Assert.Equal(0x2000u + stride, cpu.State.A[opcode & 7]);
        Assert.Equal(Code + 4, cpu.State.ProgramCounter);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        if ((opcode & 7) == 7) Assert.Equal(0x2000u + stride, cpu.State.InterruptStackPointer);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020, 0x36B9)]
    [InlineData(M68kCpuModel.M68030, 0x36B9)]
    [InlineData(M68kCpuModel.M68040, 0x36B9)]
    [InlineData(M68kCpuModel.M68020, 0x21D8)]
    [InlineData(M68kCpuModel.M68030, 0x21D8)]
    [InlineData(M68kCpuModel.M68040, 0x21D8)]
    [InlineData(M68kCpuModel.M68020, 0xB353)]
    [InlineData(M68kCpuModel.M68030, 0xB353)]
    [InlineData(M68kCpuModel.M68040, 0xB353)]
    public void ContinuationFormsRetainSharedModelsAnd040Fallback(M68kCpuModel model, ushort opcode)
    {
        var bus = new ZeroWaitCodeBus();
        if (opcode == 0x36B9) WriteWords(bus, Code, opcode, 0, 0x2000);
        else WriteWords(bus, Code, opcode, 0x3000);
        bus.WriteLong(0x2000, 0x12345678);
        bus.WriteLong(0x3000, 0xA55A5AA5);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(Code, 0x4000);
        cpu.State.A[0] = 0x2000;
        cpu.State.A[3] = opcode == 0xB353 ? 0x2000u : 0x3000u;
        cpu.State.D[1] = 0x5555A55A;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(opcode == 0xB353 ? 0xB76E5678u : 0x12345678u, bus.ReadLong(0x2000));
        Assert.Equal(opcode switch { 0x36B9 => 0x12345AA5u, 0x21D8 => 0x12345678u, _ => 0xA55A5AA5u }, bus.ReadLong(0x3000));
        Assert.Equal(opcode == 0x21D8 ? 0x2004u : 0x2000u, cpu.State.A[0]);
        Assert.Equal(opcode == 0xB353 ? 0x2000u : 0x3000u, cpu.State.A[3]);
        Assert.Equal(0x5555A55Au, cpu.State.D[1]);
        Assert.Equal(opcode == 0xB353 ? 0x18 : 0x10, cpu.State.StatusRegister & 31);
        Assert.Equal(Code + (opcode switch { 0x36B9 => 6u, 0x21D8 => 4u, _ => 2u }), cpu.State.ProgramCounter);
    }
}
