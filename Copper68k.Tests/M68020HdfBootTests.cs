using Copper68k;

namespace Copper68k.Tests;

public sealed class M68020HdfBootTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    public void NewlyReachedIndexFormsStillRejectUnsupportedFullExtensions(M68kCpuModel model)
    {
        foreach (var opcode in new ushort[] { 0x4EF4, 0x367B, 0x11A9 })
        {
            var bus = new Copper68kTestBus(0x10000);
            if (opcode == 0x11A9) bus.WriteWords(0x1000, opcode, 0, 0x0100);
            else bus.WriteWords(0x1000, opcode, 0x0100);
            bus.WriteLong(0x3000, 0x12345678);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            for (var i = 0; i < 7; i++) cpu.State.A[i] = 0x3000;
            cpu.State.StatusRegister = 0x271F;
            Assert.Throws<UnsupportedM68kTimingException>(() => cpu.ExecuteInstruction());
            Assert.Equal(0x12345678u, bus.ReadLong(0x3000));
            Assert.Equal(0x7000u, cpu.State.A[7]);
            Assert.Equal(0x3000u, cpu.State.A[3]);
            Assert.Equal(0x271F, cpu.State.StatusRegister);
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void MovemLongPcDisplacementUsesPcAfterMaskAndLoadsRegistersInAscendingOrder(M68kCpuModel model)
    {
        foreach (var mask in new ushort[] { 0, 0x8001, 0xA55A, 0xFFFF })
        foreach (var displacement in new[] { -128, 128 })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, 0x4CFA, mask, (ushort)displacement);
            var address = (uint)(0x1004 + displacement);
            for (var i = 0; i < 16; i++) bus.WriteLong(address + (uint)(4 * i), 0x11220000u + (uint)i);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            for (var i = 0; i < 8; i++) cpu.State.D[i] = cpu.State.A[i] = 0x4000u + (uint)i;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            var rank = 0u;
            for (var i = 0; i < 16; i++)
            {
                var expected = (mask & (1 << i)) != 0 ? 0x11220000u + rank++ : 0x4000u + (uint)(i & 7);
                Assert.Equal(expected, i < 8 ? cpu.State.D[i] : cpu.State.A[i - 8]);
            }
            if (model != M68kCpuModel.M68040)
                Assert.Equal(14 + 4 * rank, cpu.State.NativeCycles); // Bounded register-count timing policy.
            Assert.Equal(0x271F, cpu.State.StatusRegister);
            Assert.Equal(0x1006u, cpu.State.ProgramCounter);
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void CmpiBytePredecrementUsesLowImmediateByteAndPreservesMemoryAndExtend(M68kCpuModel model)
    {
        foreach (var register in new[] { 1, 7 })
        foreach (var operand in new[] { (0, 0x19), (0x80, 0x12), (1, 0x14), (2, 0x10) })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, (ushort)(0x0C20 | register), 0xAB01);
            var target = register == 7 ? 0x2FFEu : 0x2FFFu;
            bus.Memory[target] = (byte)operand.Item1;
            bus.Memory[target + 1] = 0xEE;
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.A[register] = 0x3000;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            Assert.Equal(0x2700 | operand.Item2, cpu.State.StatusRegister);
            Assert.Equal(target, cpu.State.A[register]);
            Assert.Equal((operand.Item1 << 8) | 0xEE, bus.ReadWord(target));
            Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void AddBytePostincrementPreservesUpperDataAndHandlesStackAlignment(M68kCpuModel model)
    {
        foreach (var register in new[] { 4, 7 })
        foreach (var operand in new[] { (0xFFu, 0u, 0x15), (0x7Fu, 0x80u, 0x0A), (0u, 1u, 0) })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWord(0x1000, (ushort)(0xD218 | register));
            bus.WriteWord(0x3000, 0x01EE);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.A[register] = 0x3000;
            cpu.State.D[1] = 0xABCDEF00 | operand.Item1;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            Assert.Equal(0xABCDEF00 | operand.Item2, cpu.State.D[1]);
            Assert.Equal(0x2700 | operand.Item3, cpu.State.StatusRegister);
            Assert.Equal(register == 7 ? 0x3002u : 0x3001u, cpu.State.A[register]);
            Assert.Equal(0x01EE, bus.ReadWord(0x3000));
            Assert.Equal(0x1002u, cpu.State.ProgramCounter);
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void MoveaWordPcIndexUsesExtensionPcAndReadsBeforeAliasedDestinationWrite(M68kCpuModel model)
    {
        foreach (var value in new ushort[] { 0x8001, 0x7FFF, 0 })
        foreach (var addressIndex in new[] { false, true })
        foreach (var longIndex in new[] { false, true })
        {
            var bus = new Copper68kTestBus(0x100000);
            var extension = (ushort)((addressIndex ? 0xB000 : 0x3000) | (longIndex ? 0x800 : 0) | 0x680);
            bus.WriteWords(0x1000, 0x367B, extension); // MOVEA.W (-128,PC,D3/A3*8),A3
            var address = (uint)(0x1002 - 128 + (longIndex ? 65538 : 2) * 8);
            bus.WriteWord(address, value);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.A[3] = cpu.State.D[3] = 0x10002;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            Assert.Equal(value == 0x8001 ? 0xFFFF8001u : (uint)value, cpu.State.A[3]);
            Assert.Equal(0x10002u, cpu.State.D[3]);
            Assert.Equal(value, bus.ReadWord(address));
            Assert.Equal(0x271F, cpu.State.StatusRegister);
            Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void MoveByteDisplacementToPredecrementReadsAliasedSourceBeforeUpdatingAddress(M68kCpuModel model)
    {
        foreach (var register in new[] { 2, 7 })
        foreach (var value in new byte[] { 0, 0x80, 0x7F })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, (ushort)(0x1128 | register << 9 | register), 0x0004);
            bus.WriteWord(0x3004, (ushort)((value << 8) | 0xEE));
            bus.WriteLong(0x2FFC, 0xAABBCCDD);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.A[register] = 0x3000;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            Assert.Equal(register == 7 ? 0x2FFEu : 0x2FFFu, cpu.State.A[register]);
            Assert.Equal(register == 7 ? 0xAABB00DDu | (uint)value << 8 : 0xAABBCC00u | value, bus.ReadLong(0x2FFC));
            Assert.Equal((value << 8) | 0xEE, bus.ReadWord(0x3004));
            Assert.Equal(value == 0 ? 0x2714 : value == 0x80 ? 0x2718 : 0x2710, cpu.State.StatusRegister);
            Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void MoveByteDisplacementToIndexUsesBothExtensionsAndPreservesSurroundingBytes(M68kCpuModel model)
    {
        foreach (var value in new byte[] { 0, 0x80, 0x7F })
        foreach (var addressIndex in new[] { false, true })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, 0x11A9, 0xFFFE, (ushort)(addressIndex ? 0xAC7F : 0x307F));
            bus.WriteWord(0x1FFE, (ushort)((value << 8) | 0xEE));
            var target = addressIndex ? 0x308Fu : 0x307Du; // A2.L*4=16 or D3.W=-2, displacement +127.
            bus.Memory[target - 1] = 0xAA;
            bus.Memory[target] = 0xFF;
            bus.Memory[target + 1] = 0xBB;
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.A[0] = 0x3000;
            cpu.State.A[1] = 0x2000;
            cpu.State.A[2] = 4;
            cpu.State.D[3] = 0x1234FFFE;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            Assert.Equal(value, bus.Memory[target]);
            Assert.Equal(0xAA, bus.Memory[target - 1]);
            Assert.Equal(0xBB, bus.Memory[target + 1]);
            Assert.Equal((value << 8) | 0xEE, bus.ReadWord(0x1FFE));
            Assert.Equal(value == 0 ? 0x2714 : value == 0x80 ? 0x2718 : 0x2710, cpu.State.StatusRegister);
            Assert.Equal(0x1006u, cpu.State.ProgramCounter);
            Assert.Equal(0x3000u, cpu.State.A[0]);
            Assert.Equal(0x2000u, cpu.State.A[1]);
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020, -128)]
    [InlineData(M68kCpuModel.M68EC020, 128)]
    [InlineData(M68kCpuModel.M68030, -128)]
    [InlineData(M68kCpuModel.M68040, 128)]
    public void SubaLongPcDisplacementUsesFullLongAndPreservesFlags(M68kCpuModel model, int displacement)
    {
        for (var register = 0; register < 8; register++)
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, (ushort)(0x91FA | (register << 9)), (ushort)displacement);
            var address = (uint)(0x1002 + displacement);
            bus.WriteLong(address, 0x56788000);
            bus.WriteWord(address + 4, 0x4321);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.A[register] = 0x1234;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            Assert.Equal(0xA9879234u, cpu.State.A[register]);
            Assert.Equal(0x271F, cpu.State.StatusRegister);
            Assert.Equal(0x1004u, cpu.State.ProgramCounter);
            Assert.Equal(0x56788000u, bus.ReadLong(address));
            Assert.Equal(0x4321, bus.ReadWord(address + 4));
        }
    }

    public static IEnumerable<object[]> PcAddCases()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030, M68kCpuModel.M68040 })
        foreach (var displacement in new[] { -128, 128 })
        foreach (var arithmetic in new[] {
            new uint[] { 0, 1, 1, 0 },
            new uint[] { 0xFFFFFFFF, 1, 0, 0x15 },
            new uint[] { 0x7FFFFFFF, 1, 0x80000000, 0x0A },
            new uint[] { 0x80000000, 0x80000000, 0, 0x17 } })
            yield return new object[] { model, displacement, arithmetic[0], arithmetic[1], arithmetic[2], (ushort)arithmetic[3] };
    }

    [Theory]
    [MemberData(nameof(PcAddCases))]
    public void AddLongPcDisplacementReadsMemoryFromExtensionPcAndSetsArithmeticFlags(
        M68kCpuModel model, int displacement, uint destination, uint source, uint result, ushort flags)
    {
        for (var register = 0; register < 8; register++)
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, (ushort)(0xD0BA | (register << 9)), (ushort)displacement);
            var address = (uint)(0x1002 + displacement);
            bus.WriteLong(address, source);
            bus.WriteWord(address + 4, 0xFFFF); // Discriminate the extension PC from the following PC.
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.D[register] = destination;
            cpu.State.StatusRegister = 0x271F;
            cpu.ExecuteInstruction();
            Assert.Equal(result, cpu.State.D[register]);
            Assert.Equal((ushort)(0x2700 | flags), cpu.State.StatusRegister);
            Assert.Equal(0x1004u, cpu.State.ProgramCounter);
            Assert.Equal(source, bus.ReadLong(address));
            Assert.Equal(0xFFFF, bus.ReadWord(address + 4));
            Assert.Equal(0x7000u, cpu.State.A[7]);
        }
    }
}
