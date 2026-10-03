using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68020BrianOperandTests
{
    [Theory]
    [InlineData((int)M68kInstructionTimingKey.SubiByteImmediateToAddressIndirect)]
    [InlineData((int)M68kInstructionTimingKey.SubiWordImmediateToAddressIndirect)]
    [InlineData((int)M68kInstructionTimingKey.SubiLongImmediateToAddressIndirect)]
    public void SubiIndirectPlansRetainReadModifyWriteBarrier(int keyValue)
    {
        var key = (M68kInstructionTimingKey)keyValue;
        Assert.True((M68020TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
        Assert.True((M68030TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x0417, 0xDE01u, 0u, 0xFF000000u, 0x19)]
    [InlineData(M68kCpuModel.M68EC020, 0x0457, 1u, 0u, 0xFFFF0000u, 0x19)]
    [InlineData(M68kCpuModel.M68EC020, 0x0497, 1u, 0u, 0xFFFFFFFFu, 0x19)]
    [InlineData(M68kCpuModel.M68EC020, 0x0457, 1u, 0x80001234u, 0x7FFF1234u, 2)]
    [InlineData(M68kCpuModel.M68EC020, 0x0497, 1u, 0x80000000u, 0x7FFFFFFFu, 2)]
    [InlineData(M68kCpuModel.M68EC020, 0x0457, 1u, 0x00011234u, 0x00001234u, 4)]
    [InlineData(M68kCpuModel.M68020, 0x0497, 1u, 0u, 0xFFFFFFFFu, 0x19)]
    [InlineData(M68kCpuModel.M68030, 0x0497, 1u, 0u, 0xFFFFFFFFu, 0x19)]
    [InlineData(M68kCpuModel.M68040, 0x0497, 1u, 0u, 0xFFFFFFFFu, 0x19)]
    public void SubiImmediateIndirectRetainsStackPointerSizedWritesAndArithmeticFlags(M68kCpuModel model, ushort opcode, uint immediate, uint value, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        var longSize = (opcode & 0xC0) == 0x80;
        if (longSize) WriteWords(bus, 0xF80000, opcode, (ushort)(immediate >> 16), (ushort)immediate);
        else WriteWords(bus, 0xF80000, opcode, (ushort)immediate);
        bus.WriteLong(0x4000, value);
        bus.WriteLong(0x4004, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x4000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x4004));
        Assert.Equal(0x4000u, cpu.State.A[7]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(longSize ? 0xF80006u : 0xF80004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0, 0x01020304u, 0x201B)]
    [InlineData(M68kCpuModel.M68EC020, 7, 0x80020304u, 0x201B)]
    [InlineData(M68kCpuModel.M68EC020, 8, 0x02020304u, 0x201F)]
    [InlineData(M68kCpuModel.M68EC020, 65535, 0x80020304u, 0x201B)]
    [InlineData(M68kCpuModel.M68020, 0, 0x01020304u, 0x201B)]
    [InlineData(M68kCpuModel.M68030, 0, 0x01020304u, 0x201B)]
    [InlineData(M68kCpuModel.M68040, 0, 0x01020304u, 0x201B)]
    public void BtstImmediateAbsoluteWordUsesByteModuloEightAndOnlyChangesZero(M68kCpuModel model, ushort bit, uint value, int status)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, 0x0838, bit, 0x8000);
        bus.WriteLong(0xFF8000, value);
        bus.WriteLong(0x8000, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(value, bus.ReadLong(0xFF8000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x8000));
        Assert.Equal(status, cpu.State.StatusRegister);
        Assert.Equal(0xF80006u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData((int)M68kInstructionTimingKey.EoriByteImmediateToAddressIndirect)]
    [InlineData((int)M68kInstructionTimingKey.EoriWordImmediateToAddressIndirect)]
    [InlineData((int)M68kInstructionTimingKey.EoriLongImmediateToAddressIndirect)]
    public void EoriIndirectPlansRetainReadModifyWriteBarrier(int keyValue)
    {
        var key = (M68kInstructionTimingKey)keyValue;
        Assert.True((M68020TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
        Assert.True((M68030TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x0A10, 0xDE80u, 0x01ADBEEFu, 0x81ADBEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A50, 0x8001u, 0x0001BEEFu, 0x8000BEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A90, 0x80000001u, 1u, 0x80000000u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0A50, 1u, 0x0001BEEFu, 0x0000BEEFu, 0x14)]
    [InlineData(M68kCpuModel.M68020, 0x0A50, 0x8001u, 0x0001BEEFu, 0x8000BEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68030, 0x0A50, 0x8001u, 0x0001BEEFu, 0x8000BEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68040, 0x0A50, 0x8001u, 0x0001BEEFu, 0x8000BEEFu, 0x18)]
    public void EoriImmediateIndirectRetainsAddressWidthAndLogicalFlags(M68kCpuModel model, ushort opcode, uint immediate, uint value, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        var longSize = (opcode & 0xC0) == 0x80;
        if (longSize) WriteWords(bus, 0xF80000, opcode, (ushort)(immediate >> 16), (ushort)immediate);
        else WriteWords(bus, 0xF80000, opcode, (ushort)immediate);
        bus.WriteLong(0x2020, value);
        bus.WriteLong(0x2024, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.A[0] = 0x2020;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0x2020));
        Assert.Equal(0x12345678u, bus.ReadLong(0x2024));
        Assert.Equal(0x2020u, cpu.State.A[0]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(longSize ? 0xF80006u : 0xF80004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x11FC, 0xDE80, 0x80ADBEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x31FC, 0x8000, 0x8000BEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x11FC, 0xDE00, 0x00ADBEEFu, 0x14)]
    [InlineData(M68kCpuModel.M68020, 0x31FC, 0x8000, 0x8000BEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68030, 0x11FC, 0xDE80, 0x80ADBEEFu, 0x18)]
    [InlineData(M68kCpuModel.M68040, 0x31FC, 0x8000, 0x8000BEEFu, 0x18)]
    public void MoveImmediateToAbsoluteWordConsumesImmediateBeforeSignedAddress(M68kCpuModel model, ushort opcode, ushort immediate, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, opcode, immediate, 0x8000);
        bus.WriteLong(0xFF8000, 0xDEADBEEF);
        bus.WriteLong(0x8000, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0xFF8000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x8000));
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0xF80006u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData((int)M68kInstructionTimingKey.SubqByteAbsoluteWord)]
    [InlineData((int)M68kInstructionTimingKey.SubqWordAbsoluteWord)]
    [InlineData((int)M68kInstructionTimingKey.SubqLongAbsoluteWord)]
    public void SubqAbsoluteWordPlansRetainReadModifyWriteBarrier(int keyValue)
    {
        var key = (M68kInstructionTimingKey)keyValue;
        Assert.True((M68020TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
        Assert.True((M68030TimingModel.GetPlan(key).Barriers & M68kTimingBarrier.ReadModifyWrite) != 0);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x5338, 0u, 0xFF000000u, 0x19)]
    [InlineData(M68kCpuModel.M68EC020, 0x5378, 0u, 0xFFFF0000u, 0x19)]
    [InlineData(M68kCpuModel.M68EC020, 0x53B8, 0u, 0xFFFFFFFFu, 0x19)]
    [InlineData(M68kCpuModel.M68EC020, 0x5378, 0x80001234u, 0x7FFF1234u, 2)]
    [InlineData(M68kCpuModel.M68EC020, 0x5378, 0x00011234u, 0x00001234u, 4)]
    [InlineData(M68kCpuModel.M68EC020, 0x5178, 0x00071234u, 0xFFFF1234u, 0x19)]
    [InlineData(M68kCpuModel.M68020, 0x5378, 0u, 0xFFFF0000u, 0x19)]
    [InlineData(M68kCpuModel.M68030, 0x5378, 0u, 0xFFFF0000u, 0x19)]
    [InlineData(M68kCpuModel.M68040, 0x5378, 0u, 0xFFFF0000u, 0x19)]
    public void SubqAbsoluteWordRetainsSizedWritesAndArithmeticFlags(M68kCpuModel model, ushort opcode, uint value, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, opcode, 0x8000);
        bus.WriteLong(0xFF8000, value);
        bus.WriteLong(0x8000, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0xFF8000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x8000));
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0xF80004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x8183)]
    [InlineData(M68kCpuModel.M68EC020, 0)]
    [InlineData(M68kCpuModel.M68020, 0x8183)]
    [InlineData(M68kCpuModel.M68030, 0x8183)]
    [InlineData(M68kCpuModel.M68040, 0x8183)]
    public void MovemLongToAbsoluteWordSignExtendsAddressAndStoresNormalRegisterOrder(M68kCpuModel model, ushort mask)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, 0x48F8, mask, 0x8000);
        for (uint offset = 0; offset < 24; offset += 4)
            bus.WriteLong(0xFF8000 + offset, 0xA5A5A5A5);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.D[0] = 0x12345678;
        cpu.State.D[1] = 0x87654321;
        cpu.State.D[7] = 0xFEDCBA98;
        cpu.State.A[0] = 0xFFFFFFFF;
        cpu.State.StatusRegister = 0x201F;
        var beforeD = cpu.State.D.ToArray();
        var beforeA = cpu.State.A.ToArray();
        cpu.ExecuteInstruction();
        var expected = mask == 0 ? Array.Empty<uint>() : new uint[] { 0x12345678, 0x87654321, 0xFEDCBA98, 0xFFFFFFFF, 0x4000 };
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], bus.ReadLong(0xFF8000 + (uint)(4 * index)));
        Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0xFF8000 + (uint)(4 * expected.Length)));
        Assert.Equal(beforeD, cpu.State.D);
        Assert.Equal(beforeA, cpu.State.A);
        Assert.Equal(0x201F, cpu.State.StatusRegister);
        Assert.Equal(0xF80006u, cpu.State.ProgramCounter);
        if (model is M68kCpuModel.M68020 or M68kCpuModel.M68EC020)
            Assert.Equal(8L + 3L * expected.Length, cpu.State.NativeCycles);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x4A38, 0x80001234u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x4A78, 0x80001234u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x4AB8, 0x80001234u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x4AB8, 0u, 0x14)]
    [InlineData(M68kCpuModel.M68020, 0x4AB8, 0x80001234u, 0x18)]
    [InlineData(M68kCpuModel.M68030, 0x4AB8, 0x80001234u, 0x18)]
    [InlineData(M68kCpuModel.M68040, 0x4AB8, 0x80001234u, 0x18)]
    public void TstAbsoluteWordSignExtendsAddressPreservesMemoryAndExtend(M68kCpuModel model, ushort opcode, uint value, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, opcode, 0x8000);
        bus.WriteLong(0xFF8000, value);
        bus.WriteLong(0x8000, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(value, bus.ReadLong(0xFF8000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x8000));
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0xF80004u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void MovePcFullIndexedToAbsoluteWordConsumesDestinationAfterBaseDisplacement()
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, 0x31FB, 0x0930, 0, 0x20, 0x8000);
        bus.WriteLong(0xF80026, 0x80001234);
        bus.WriteLong(0xFF8000, 0xDEADBEEF);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.D[0] = 4;
        cpu.ExecuteInstruction();
        Assert.Equal(0x8000BEEFu, bus.ReadLong(0xFF8000));
        Assert.Equal(0xF8000Au, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x11FB, 0x80ADBEEFu)]
    [InlineData(M68kCpuModel.M68EC020, 0x31FB, 0x8000BEEFu)]
    [InlineData(M68kCpuModel.M68EC020, 0x21FB, 0x80001234u)]
    [InlineData(M68kCpuModel.M68020, 0x31FB, 0x8000BEEFu)]
    [InlineData(M68kCpuModel.M68030, 0x31FB, 0x8000BEEFu)]
    [InlineData(M68kCpuModel.M68040, 0x31FB, 0x8000BEEFu)]
    public void MovePcIndexedToAbsoluteWordRetainsBothAddressCalculationsAndWidth(M68kCpuModel model, ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, opcode, 0x0820, 0x8000);
        bus.WriteLong(0xF80026, 0x80001234);
        bus.WriteLong(0xFF8000, 0xDEADBEEF);
        bus.WriteLong(0x8000, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.D[0] = 4;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0xFF8000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x8000));
        Assert.Equal(0x80001234u, bus.ReadLong(0xF80026));
        Assert.Equal(0x2018, cpu.State.StatusRegister);
        Assert.Equal(0xF80006u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x4238, 0x00ADBEEFu)]
    [InlineData(M68kCpuModel.M68EC020, 0x4278, 0x0000BEEFu)]
    [InlineData(M68kCpuModel.M68EC020, 0x42B8, 0u)]
    [InlineData(M68kCpuModel.M68020, 0x4278, 0x0000BEEFu)]
    [InlineData(M68kCpuModel.M68030, 0x4238, 0x00ADBEEFu)]
    [InlineData(M68kCpuModel.M68040, 0x4278, 0x0000BEEFu)]
    public void ClrAbsoluteWordSignExtendsAddressAndOnlyClearsSelectedWidth(M68kCpuModel model, ushort opcode, uint expected)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, opcode, 0x8000);
        bus.WriteLong(0xFF8000, 0xDEADBEEF);
        bus.WriteLong(0x8000, 0x12345678);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, bus.ReadLong(0xFF8000));
        Assert.Equal(0x12345678u, bus.ReadLong(0x8000));
        Assert.Equal(0x2014, cpu.State.StatusRegister);
        Assert.Equal(0xF80004u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void OrPcIndexedResolvesAliasedIndexBeforeUpdatingDestination()
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, 0x843B, 0x2020); // D2.W is both index and destination
        bus.WriteWord(0xF80022, 0x0080);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.D[2] = 0xFFFF0001;
        cpu.ExecuteInstruction();
        Assert.Equal(0xFFFF0081u, cpu.State.D[2]);
        Assert.Equal(0x0080, bus.ReadWord(0xF80022));
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x843B, 0xA5A50001u, 0x80AA55AAu, 0xA5A50081u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x847B, 0xA5A50001u, 0x800055AAu, 0xA5A58001u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x843B, 0xA5A50000u, 0x00AA55AAu, 0xA5A50000u, 0x14)]
    [InlineData(M68kCpuModel.M68020, 0x843B, 0xA5A50001u, 0x80AA55AAu, 0xA5A50081u, 0x18)]
    [InlineData(M68kCpuModel.M68030, 0x847B, 0xA5A50001u, 0x800055AAu, 0xA5A58001u, 0x18)]
    [InlineData(M68kCpuModel.M68040, 0x843B, 0xA5A50001u, 0x80AA55AAu, 0xA5A50081u, 0x18)]
    public void OrPcIndexedUsesExtensionPcAndPreservesUpperBits(M68kCpuModel model, ushort opcode, uint destination, uint source, uint expected, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, opcode, 0x0430); // D0.W*4 + 48 from extension PC
        bus.WriteLong(0xF80022, source);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.D[0] = 0x1234FFFC;
        cpu.State.D[2] = destination;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(expected, cpu.State.D[2]);
        Assert.Equal(source, bus.ReadLong(0xF80022));
        Assert.Equal(0x1234FFFCu, cpu.State.D[0]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0xF80004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68EC020, 0x0820, 0x12348000u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0820, 0xFFFF0000u, 0x14)]
    [InlineData(M68kCpuModel.M68020, 0x0820, 0x12348000u, 0x18)]
    [InlineData(M68kCpuModel.M68030, 0x0820, 0x12348000u, 0x18)]
    [InlineData(M68kCpuModel.M68040, 0x0820, 0x12348000u, 0x18)]
    [InlineData(M68kCpuModel.M68EC020, 0x0930, 0x12348000u, 0x18)]
    public void MoveWordAddressToIndexedStoresLowWordAndPreservesSource(M68kCpuModel model, ushort extension, uint source, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, 0x3D8C, extension, 0, 0x20);
        bus.WriteLong(0x2024, 0xDEADBEEF);
        using var cpu = model == M68kCpuModel.M68EC020
            ? M68kCoreFactory.Default.CreateA1200Ec020(bus)
            : M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.A[4] = source;
        cpu.State.A[6] = 0x2000;
        cpu.State.D[0] = 4;
        cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal((source << 16) | 0xBEEFu, bus.ReadLong(0x2024));
        Assert.Equal(source, cpu.State.A[4]);
        Assert.Equal(0x2000u, cpu.State.A[6]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal((extension & 0x100) == 0 ? 0xF80004u : 0xF80008u, cpu.State.ProgramCounter);
    }

    [Fact]
    public void MoveWordAddressToIndexedKeepsAliasedBaseAndSignExtendsScaledWordIndex()
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xF80000, 0x3D8E, 0x0430); // A6 -> (48,A6,D0.W*4)
        bus.WriteLong(0x2020, 0xDEADBEEF);
        using var cpu = M68kCoreFactory.Default.CreateA1200Ec020(bus);
        cpu.Reset(0xF80000, 0x4000);
        cpu.State.A[6] = 0x2000;
        cpu.State.D[0] = 0x1234FFFC;
        cpu.ExecuteInstruction();
        Assert.Equal(0x2000BEEFu, bus.ReadLong(0x2020));
        Assert.Equal(0x2000u, cpu.State.A[6]);
    }
}
