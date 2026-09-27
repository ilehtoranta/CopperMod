using Copper68k;

namespace Copper68k.Tests;

public sealed class M68020IndexedJsrTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void IndexedJmpUsesTheAddressBaseWithoutTouchingTheStackOrFlags(M68kCpuModel model)
    {
        for (var register = 0; register < 8; register++)
        foreach (var addressIndex in new[] { false, true })
        {
            var bus = new Copper68kTestBus(0x10000);
            bus.WriteWords(0x1000, (ushort)(0x4EF0 | register), (ushort)(addressIndex ? 0xB680 : 0x3680));
            bus.WriteLong(0x6FFC, 0x12345678);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            for (var a = 0; a < 7; a++) cpu.State.A[a] = 0x2000;
            cpu.State.D[3] = cpu.State.A[3] = 0xFFFE;
            cpu.State.StatusRegister = 0x271F;
            var baseAddress = cpu.State.A[register];
            cpu.ExecuteInstruction();
            Assert.Equal(baseAddress - 128 - 16, cpu.State.ProgramCounter);
            Assert.Equal(0x7000u, cpu.State.A[7]);
            Assert.Equal(0x12345678u, bus.ReadLong(0x6FFC));
            Assert.Equal(0x271F, cpu.State.StatusRegister);
        }
    }

    // M68000PRM, JSR (4-109): push the following instruction's PC, preserve CCR.
    public static IEnumerable<object[]> IndexCases()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var addressIndex in new[] { false, true })
        foreach (var longIndex in new[] { false, true })
        foreach (var scale in new[] { 0, 1, 2, 3 })
        foreach (var displacement in new[] { -128, 126 })
            yield return new object[] { model, addressIndex, longIndex, scale, displacement };
    }

    [Theory]
    [MemberData(nameof(IndexCases))]
    public void IndexedJsrUsesSignedScaledIndexAndReturnsAfterExtension(
        M68kCpuModel model, bool addressIndex, bool longIndex, int scale, int displacement)
    {
        var bus = new Copper68kTestBus(0x100000);
        var extension = (ushort)((addressIndex ? 0xB000 : 0x3000) | (longIndex ? 0x800 : 0) |
            (scale << 9) | (byte)displacement);
        bus.WriteWords(0x1000, 0x4EB2, extension);
        var target = (uint)(0x2000 + displacement + (longIndex ? 65534 : -2) * (1 << scale));
        bus.WriteWord(target, 0x4E75); // RTS
        bus.WriteLong(0x6FF8, 0x11223344);
        bus.WriteLong(0x7000, 0x55667788);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.A[2] = 0x2000;
        cpu.State.D[3] = cpu.State.A[3] = 0x0000FFFE;
        cpu.State.StatusRegister = 0x271F;

        cpu.ExecuteInstruction();

        Assert.Equal(target, cpu.State.ProgramCounter);
        Assert.Equal(0x6FFCu, cpu.State.A[7]);
        Assert.Equal(0x1004u, bus.ReadLong(0x6FFC));
        Assert.Equal(0x11223344u, bus.ReadLong(0x6FF8));
        Assert.Equal(0x55667788u, bus.ReadLong(0x7000));
        Assert.Equal(0x271F, cpu.State.StatusRegister);
        Assert.Equal(0x2000u, cpu.State.A[2]);
        Assert.Equal(0xFFFEu, cpu.State.A[3]);
        Assert.Equal(0xFFFEu, cpu.State.D[3]);
        // This is the existing bounded indexed-JSR timing policy, not hardware certification.
        Assert.Equal(11, cpu.State.NativeCycles);

        cpu.ExecuteInstruction();
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
        Assert.Equal(0x7000u, cpu.State.A[7]);
        Assert.Equal(0x271F, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)] // Keep the existing 040 fallback behavior.
    public void EveryBaseRegisterAndStackIndexUseValuesBeforeTheReturnAddressPush(M68kCpuModel model)
    {
        for (var baseRegister = 0; baseRegister < 8; baseRegister++)
        {
            var bus = new Copper68kTestBus(0x20000);
            bus.WriteWords(0x1000, (ushort)(0x4EB0 | baseRegister), 0xFA80); // -128,An,A7.L*2
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x1000, 0x7000);
            cpu.State.A[baseRegister] = baseRegister == 7 ? 0x7000u : 0x2000u;
            cpu.ExecuteInstruction();
            Assert.Equal((baseRegister == 7 ? 0x7000u : 0x2000u) + 0xE000u - 128, cpu.State.ProgramCounter);
            Assert.Equal(0x6FFCu, cpu.State.A[7]);
            Assert.Equal(0x1004u, bus.ReadLong(0x6FFC));
        }
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    public void KickstartHdfBootCallPreservesUserFlagsAndPushesRomReturnAddress(M68kCpuModel model)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0xFC4E28, 0x4EB2, 0x0000); // JSR (0,A2,D0.W)
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0xFC4E28, 0xC014C6);
        cpu.State.StatusRegister = 0;
        cpu.State.A[7] = 0xC014C6;
        cpu.State.A[2] = 0xC01AE0;
        cpu.State.D[0] = 0x20;
        cpu.ExecuteInstruction();
        Assert.Equal(0xC01B00u, cpu.State.ProgramCounter);
        Assert.Equal(0xC014C2u, cpu.State.A[7]);
        Assert.Equal(0xFC4E2Cu, bus.ReadLong(0xC014C2));
        Assert.Equal(0, cpu.State.StatusRegister);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    public void UnsupportedFullExtensionDoesNotPushAReturnAddress(M68kCpuModel model)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, 0x4EB2, 0x0100);
        bus.WriteLong(0x6FFC, 0x12345678);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        Assert.Throws<UnsupportedM68kTimingException>(() => cpu.ExecuteInstruction());
        Assert.Equal(0x7000u, cpu.State.A[7]);
        Assert.Equal(0x12345678u, bus.ReadLong(0x6FFC));
    }
}
