using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;
namespace Copper68k.Tests;

public sealed class M68020AddressSourceTests
{
    private const uint CodeBase = 0xF80000;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AndiIndirectConsumesTheSelectedImmediateWidthAndPreservesExtend(bool isLong)
    {
        var bus = new ZeroWaitCodeBus();
        if (isLong) WriteWords(bus, CodeBase, 0x0291, 0x8000, 0x00FF, 0x4E71);
        else WriteWords(bus, CodeBase, 0x0251, 0x8000, 0x4E71);
        WriteWords(bus, 0x2000, 0xFFFF, 0xABCD);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[1] = 0x2000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(0x8000, bus.ReadWord(0x2000));
        Assert.Equal(isLong ? 0xCD : 0xABCD, bus.ReadWord(0x2002)); Assert.Equal(0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + (isLong ? 6u : 4u), cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x01AE, 15u, 0x80, 0)] [InlineData(0x016E, 15u, 0x80, 0)] [InlineData(0x016E, 9u, 0x80, 0x82)]
    public void DynamicBitDisplacementUsesModuloEightAndTheOriginalZeroFlag(ushort opcode, uint bit, ushort initial, ushort expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0xFFFE); WriteWords(bus, 0x2000, (ushort)(initial << 8));
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[6] = 0x2002; cpu.State.D[0] = bit; cpu.State.StatusRegister = 0x201B;
        cpu.ExecuteInstruction(); Assert.Equal((ushort)(expected << 8), bus.ReadWord(0x2000));
        Assert.Equal(bit == 9 ? 0x1F : 0x1B, cpu.State.StatusRegister & 31); Assert.Equal(bit, cpu.State.D[0]);
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0xD150, 0xFFFF, 1u, 0, 0x15)]
    [InlineData(0xD110, 0xFFAB, 1u, 0x00AB, 0x15)]
    [InlineData(0xD150, 0x7FFF, 1u, 0x8000, 10)]
    public void AddToIndirectMemoryWritesSelectedWidthAndPreservesSource(ushort opcode, ushort initial, uint source, ushort expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); WriteWords(bus, 0x2000, initial, 0xABCD);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[0] = 0x2000; cpu.State.D[0] = source;
        cpu.ExecuteInstruction(); Assert.Equal(expected, bus.ReadWord(0x2000)); Assert.Equal(0xABCD, bus.ReadWord(0x2002));
        Assert.Equal(source, cpu.State.D[0]); Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0xFFFFu, 0xFFFF, 0x11)] [InlineData(0xFFFFFFFFu, 0xFFFF, 0x14)]
    [InlineData(0u, 1, 0x19)]
    public void CmpaWordDisplacementComparesSignExtendedSourceAgainstFullAddress(uint destination, ushort source, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0xB2E8, 0xFFFE); WriteWords(bus, 0x2000, source);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[0] = 0x2002; cpu.State.A[1] = destination; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(destination, cpu.State.A[1]); Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x5270, 0xFFFF, 0, 0x15)] [InlineData(0x5370, 0, 0xFFFF, 0x19)]
    [InlineData(0x5070, 0x7FF8, 0x8000, 10)]
    public void IndexedQuickWordUpdatesTheOriginalAddressAndArithmeticFlags(ushort opcode, ushort initial, ushort expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0x10FE); WriteWords(bus, 0x2000, initial, 0xABCD);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[0] = 0x2000; cpu.State.D[1] = 2; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, bus.ReadWord(0x2000)); Assert.Equal(0xABCD, bus.ReadWord(0x2002));
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(0x2000u, cpu.State.A[0]);
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Fact]
    public void PublicA1200FactorySelectsTheExplicitBoardProfileWithoutChangingTheDefault()
    {
        using var aga = M68kCoreFactory.Default.CreateA1200Ec020(new ZeroWaitCodeBus());
        using var ocs = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, new ZeroWaitCodeBus());
        Assert.Same(M68020CpuProfile.A1200Ec02014Mhz, Assert.IsType<M68EC020Interpreter>(aga).Profile);
        Assert.Same(M68020CpuProfile.OcsAccelerator14Mhz, Assert.IsType<M68EC020Interpreter>(ocs).Profile);
    }
    [Theory]
    [InlineData(0x8000, 0x18)] [InlineData(0, 0x14)] [InlineData(0x7FFF, 0x10)]
    public void TstWordIndexedUsesSignedDisplacementAndWordFlags(ushort operand, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x4A70, 0x10FE); WriteWords(bus, 0x2000, operand);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[0] = 0x2000; cpu.State.D[1] = 2; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(operand, bus.ReadWord(0x2000));
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0837, 0x80)] [InlineData(0x0877, 0)] [InlineData(0x08B7, 0)] [InlineData(0x08F7, 0x80)]
    public void ImmediateIndexedBitOperationsReadTheOriginalBitAndKeepOtherFlags(ushort opcode, ushort result)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 15, 0x10FE); WriteWords(bus, 0x2000, 0x80AB);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase, 0x2000); cpu.State.D[1] = 2; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal((ushort)((result << 8) | 0xAB), bus.ReadWord(0x2000));
        Assert.Equal(0x201B, cpu.State.StatusRegister); Assert.Equal(0x2000u, cpu.State.A[7]);
        Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x90D3, 0xFFFE, 0, 0x1002u)]
    [InlineData(0x91D3, 0, 2, 0xFFEu)]
    public void SubaIndirectSignExtendsWordsAndLeavesCcrUntouched(ushort opcode, ushort hi, ushort lo, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); WriteWords(bus, 0x2000, hi, lo);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[3] = 0x2000; cpu.State.A[0] = 0x1000; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(expected, cpu.State.A[0]); Assert.Equal(0x2000u, cpu.State.A[3]);
        Assert.Equal(0x201F, cpu.State.StatusRegister); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x2060, 0)] [InlineData(0x2260, 1)]
    public void MoveaPredecrementLatchesSourceBeforeDestinationAndKeepsFlags(ushort opcode, int destination)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); WriteWords(bus, 0x2000, 0x89AB, 0xCDEF);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[0] = 0x2004; cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction(); Assert.Equal(0x89ABCDEFu, cpu.State.A[destination]);
        if (destination != 0) Assert.Equal(0x2000u, cpu.State.A[0]);
        Assert.Equal(0x201F, cpu.State.StatusRegister); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x0C5B, 2u, 0xFF80, 0x14)]
    [InlineData(0x0C1B, 1u, 0xFF, 0x14)]
    [InlineData(0x0C1F, 2u, 0xFF, 0x14)]
    public void CmpiPostIncrementPreservesExtendAndUsesByteStackStride(ushort opcode, uint stride, ushort operand, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, operand);
        WriteWords(bus, 0x2000, (ushort)(opcode == 0x0C5B ? operand : operand << 8));
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase, 0x2000); cpu.State.A[3] = 0x2000; cpu.State.StatusRegister = 0x201B;
        cpu.ExecuteInstruction(); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0x2000u + stride, cpu.State.A[opcode & 7]); Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0xC012, 0xFFFF00F0u)]
    [InlineData(0xC052, 0xFFFFF0A5u)]
    [InlineData(0xC092, 0xF0A55A0Fu)]
    public void AndAddressIndirectUsesSelectedWidthAndPreservesExtend(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode); WriteWords(bus, 0x2000, 0xF0A5, 0x5A0F);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[2] = 0x2000; cpu.State.D[0] = 0xFFFF00FF;
        if (opcode != 0xC012) cpu.State.D[0] = uint.MaxValue;
        cpu.State.StatusRegister = 0x2017; cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(0x18, cpu.State.StatusRegister & 31);
        Assert.Equal(0x2000u, cpu.State.A[2]); Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(9u, 0x80, 0x1F)]
    [InlineData(15u, 0x80, 0x1B)]
    public void BtstDynamicAddressIndirectUsesModuloEightWithoutWriting(uint bit, byte operand, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x0310); WriteWords(bus, 0x2000, (ushort)(operand << 8));
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[0] = 0x2000; cpu.State.D[1] = bit;
        cpu.State.StatusRegister = 0x201B; cpu.ExecuteInstruction();
        Assert.Equal(flags, cpu.State.StatusRegister & 31); Assert.Equal(operand, (byte)(bus.ReadWord(0x2000) >> 8));
        Assert.Equal(bit, cpu.State.D[1]); Assert.Equal(0x2000u, cpu.State.A[0]);
        Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
    }

    [Fact]
    public void MovemWordPcDisplacementSignExtendsSparseMaskAndPreservesFlags()
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, 0x8001, 0x7FFE, 0xFFFF, 0x4CBA, 0x0205, 0xFFF6, 0x4E71);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase + 6, 0x3000); cpu.State.StatusRegister = 0x201B;
        cpu.State.D[1] = 0xDEADBEEF; cpu.State.A[0] = 0xAABBCCDD;
        cpu.ExecuteInstruction();
        Assert.Equal(0xFFFF8001u, cpu.State.D[0]); Assert.Equal(0x7FFEu, cpu.State.D[2]);
        Assert.Equal(0xFFFFFFFFu, cpu.State.A[1]); Assert.Equal(0xDEADBEEFu, cpu.State.D[1]);
        Assert.Equal(0xAABBCCDDu, cpu.State.A[0]); Assert.Equal(0x201B, cpu.State.StatusRegister);
        Assert.Equal(CodeBase + 12, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x323A, 0xC0DE80FEu)]
    [InlineData(0x123A, 0xC0DE0080u)]
    public void MovePcDisplacementUsesExtensionAddressAndPreservesExtend(ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0x80FE, opcode, 0xFFFC, 0x4E71);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase + 2, 0x3000); cpu.State.D[1] = 0xC0DE0000; cpu.State.StatusRegister = 0x2017;
        cpu.ExecuteInstruction(); Assert.Equal(expected, cpu.State.D[1]);
        Assert.Equal(0x18, cpu.State.StatusRegister & 31); Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(0x943C, 0xCAFE1230u, 0xCAFE1200u)]
    [InlineData(0x947C, 0xCAFE0030u, 0xCAFE0000u)]
    public void SubImmediateSourceConsumesExtensionAndRetainsUpperDestination(ushort opcode, uint initial, uint expected)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, opcode, 0x0030, 0x4E71);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeBase, 0x3000); cpu.State.D[2] = initial;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[2]); Assert.Equal(4, cpu.State.StatusRegister & 31);
        Assert.Equal(CodeBase + 4, cpu.State.ProgramCounter);
        cpu.ExecuteInstruction(); Assert.Equal(CodeBase + 6, cpu.State.ProgramCounter);
    }
    [Theory]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    [InlineData(M68kCpuModel.M68060)]
    public void AddWordAddressSourceRetainsUpperDestinationAndSourceAndSetsCarry(M68kCpuModel model)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, 0xDC4A); // ADD.W A2,D6 (native A1200 ROM)
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(CodeBase, 0x3000);
        cpu.State.A[2] = 0xABCD00FF; cpu.State.D[6] = 0x1234FF01;
        cpu.State.StatusRegister = 0x200A;
        cpu.ExecuteInstruction();
        Assert.Equal(0x12340000u, cpu.State.D[6]);
        Assert.Equal(0xABCD00FFu, cpu.State.A[2]);
        Assert.Equal(0x15, cpu.State.StatusRegister & 31); // X,Z,C; N,V cleared
        Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
        Assert.True(cpu.State.Cycles > 0);
    }

    [Theory]
    [InlineData(0x00000001u, 0xCAFE7FFFu, 0xCAFE8000u, 10)] // signed overflow
    [InlineData(0x0000FFFFu, 0xCAFE0000u, 0xCAFEFFFFu, 8)]
    [InlineData(0xFFFF0002u, 0xCAFE0003u, 0xCAFE0005u, 0)]
    public void AddWordAddressSourceUsesWordArithmetic(uint source, uint destination, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0xDC4A);
        using var cpu = new M68020Interpreter(bus, M68020CpuProfile.OcsAccelerator14Mhz);
        cpu.Reset(CodeBase, 0x3000); cpu.State.A[2] = source; cpu.State.D[6] = destination;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[6]); Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(2, cpu.State.NativeCycles); Assert.Equal(1, cpu.State.Cycles);
    }
}
