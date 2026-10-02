using Copper68k;

namespace Copper68k.Tests;

public sealed class M68020FastRamBootTests
{
    public static IEnumerable<object[]> PcIndexedAddresses()
    {
        foreach (var model in new[] { M68kCpuModel.M68020, M68kCpuModel.M68EC020, M68kCpuModel.M68030 })
        foreach (var addressRegister in new[] { false, true })
        foreach (var longIndex in new[] { false, true })
        foreach (var scale in new[] { 0, 1, 2, 3 })
        foreach (var displacement in new[] { -128, 127 })
            yield return new object[] { model, addressRegister, longIndex, scale, displacement };
    }

    [Theory]
    [MemberData(nameof(PcIndexedAddresses))]
    public void LeaPcIndexUsesExtensionAddressAndSignedScaledIndexWithoutChangingFlags(
        M68kCpuModel model, bool addressRegister, bool longIndex, int scale, int displacement)
    {
        var bus = new Copper68kTestBus();
        var extension = (ushort)((addressRegister ? 0xA000 : 0x2000) | (longIndex ? 0x800 : 0) |
            (scale << 9) | (byte)displacement);
        bus.WriteWords(0x1000, 0x45FB, extension); // LEA (d8,PC,D2/A2),A2; source aliases destination.
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        cpu.State.D[2] = cpu.State.A[2] = 0xFFFF0002;
        cpu.State.StatusRegister = 0x271F;
        cpu.ExecuteInstruction();
        var index = longIndex ? -65534 : 2;
        Assert.Equal(unchecked((uint)(0x1002 + displacement + index * (1 << scale))), cpu.State.A[2]);
        Assert.Equal(0xFFFF0002u, cpu.State.D[2]);
        Assert.Equal(0x271F, cpu.State.StatusRegister);
        Assert.Equal(0x1004u, cpu.State.ProgramCounter);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68030)]
    public void UnsupportedFullIndexExtensionStillFailsExplicitly(M68kCpuModel model)
    {
        var bus = new Copper68kTestBus();
        bus.WriteWords(0x1000, 0x41FB, 0x0100);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x7000);
        Assert.Throws<UnsupportedM68kTimingException>(() => cpu.ExecuteInstruction());
    }
}
