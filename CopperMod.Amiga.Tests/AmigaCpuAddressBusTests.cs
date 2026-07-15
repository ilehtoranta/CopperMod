using CopperMod.Amiga;

namespace CopperMod.Amiga.Tests;

public sealed class AmigaCpuAddressBusTests
{
    private const uint CodeAddress = 0x00001000;
    private const uint StackAddress = 0x00002000;
    private const uint WrappedAddress = 0x01000000;

    [Fact]
    public void M68000CpuAccessWrapsTo24BitAddressBus()
    {
        var bus = new AmigaBus();
        WriteMoveByteAbsoluteLongToD0(bus, CodeAddress, WrappedAddress);
        bus.WriteHostByte(0x000000, 0x5A);
        using var cpu = AmigaM68kCoreFactory.Default.Create(M68kBackendKind.AccurateM68000, bus);
        cpu.Reset(CodeAddress, StackAddress);

        cpu.ExecuteInstruction();

        Assert.Equal(0x5Au, cpu.State.D[0] & 0xFF);
    }

    [Fact]
    public void M68010CpuAccessWrapsTo24BitAddressBus()
    {
        var bus = new AmigaBus();
        WriteMoveByteAbsoluteLongToD0(bus, CodeAddress, WrappedAddress);
        bus.WriteHostByte(0x000000, 0x5A);
        bus.MapWritableMemory(WrappedAddress, new byte[] { 0xA5 });
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68010, bus);
        cpu.Reset(CodeAddress, StackAddress);

        cpu.ExecuteInstruction();

        Assert.Equal(0x5Au, cpu.State.D[0] & 0xFF);
    }

    [Fact]
    public void M68EC020CpuAccessWrapsTo24BitAddressBus()
    {
        var bus = new AmigaBus();
        WriteMoveByteAbsoluteLongToD0(bus, CodeAddress, WrappedAddress);
        bus.WriteHostByte(0x000000, 0x5A);
        bus.MapWritableMemory(WrappedAddress, new byte[] { 0xA5 });
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus);
        cpu.Reset(CodeAddress, StackAddress);

        cpu.ExecuteInstruction();

        Assert.Equal(0x5Au, cpu.State.D[0] & 0xFF);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68030)]
    [InlineData(M68kCpuModel.M68040)]
    public void M68020AndLaterCpuAccessKeepsFull32BitPhysicalAddress(M68kCpuModel model)
    {
        var bus = new AmigaBus();
        WriteMoveByteAbsoluteLongToD0(bus, CodeAddress, WrappedAddress);
        bus.WriteHostByte(0x000000, 0x5A);
        bus.MapWritableMemory(WrappedAddress, new byte[] { 0xA5 });
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(CodeAddress, StackAddress);

        cpu.ExecuteInstruction();

        Assert.Equal(0xA5u, cpu.State.D[0] & 0xFF);
    }

    private static void WriteMoveByteAbsoluteLongToD0(AmigaBus bus, uint address, uint source)
    {
        bus.WriteHostWord(address, 0x1039); // MOVE.B (xxx).L,D0
        bus.WriteHostLong(address + 2, source);
        bus.WriteHostWord(address + 6, 0x4E71); // NOP guard
    }
}
