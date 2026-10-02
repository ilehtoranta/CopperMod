using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68020CmpmTests
{
    private const uint CodeBase = 0xF80000;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var profile in new[] { 0, 1, 2 })
        foreach (var size in new[] { 0, 1, 2 })
        foreach (var flags in new[] { 0x14, 0x19, 0x12, 0x1B })
            yield return [profile, size, flags];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CmpmComparesSelectedWidthPreservesExtendAndDoesNotWriteMemory(int profile, int size, int flags)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, (ushort)(0xB308 | size << 6));
        var sign = size == 0 ? 0x80u : size == 1 ? 0x8000u : 0x80000000u;
        var source = flags == 0x19 ? 1u : flags == 0x12 ? 1u : flags == 0x1B ? sign : 0u;
        var destination = flags == 0x12 ? sign : flags == 0x1B ? sign - 1 : 0u;
        Store(bus, 0x2000, size, source);
        Store(bus, 0x3000, size, destination);
        using var cpu = Create(bus, profile);
        cpu.Reset(CodeBase, 0x4000);
        cpu.State.A[0] = 0x2000;
        cpu.State.A[1] = 0x3000;
        cpu.State.StatusRegister = 0x201F;

        cpu.ExecuteInstruction();

        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0x2000u + (1u << size), cpu.State.A[0]);
        Assert.Equal(0x3000u + (1u << size), cpu.State.A[1]);
        Assert.Equal(source, Load(bus, 0x2000, size));
        Assert.Equal(destination, Load(bus, 0x3000, size));
        Assert.Equal(CodeBase + 2, cpu.State.ProgramCounter);
        Assert.Equal(9, cpu.State.NativeCycles); // Existing cache-case operand policy, not elapsed bus time.
        Assert.Equal(5, cpu.State.Cycles);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void AliasedAddressRegisterUsesTheNextOperandAfterSourceIncrement(int size)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, CodeBase, (ushort)(0xB108 | size << 6));
        Store(bus, 0x2000, size, 0);
        Store(bus, 0x2000 + (1u << size), size, 1);
        using var cpu = Create(bus, 1);
        cpu.Reset(CodeBase, 0x4000); cpu.State.A[0] = 0x2000; cpu.State.StatusRegister = 0x201F;

        cpu.ExecuteInstruction();

        Assert.Equal(0x2000u + (2u << size), cpu.State.A[0]);
        Assert.Equal(0x10, cpu.State.StatusRegister & 31);
    }

    [Fact]
    public void ByteStackRegisterUsesTwoByteStrideForBothAliasedOperands()
    {
        var bus = new ZeroWaitCodeBus(); WriteWords(bus, CodeBase, 0xBF0F);
        WriteWords(bus, 0x4000, 0x0080, 0x0180);
        using var cpu = Create(bus, 1);
        cpu.Reset(CodeBase, 0x4000); cpu.State.StatusRegister = 0x201F;
        cpu.ExecuteInstruction();
        Assert.Equal(0x4004u, cpu.State.A[7]);
        Assert.Equal(0x10, cpu.State.StatusRegister & 31);
    }

    private static IM68kCore Create(ZeroWaitCodeBus bus, int profile) => profile switch
    {
        0 => M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus),
        1 => M68kCoreFactory.Default.CreateA1200Ec020(bus),
        _ => M68kCoreFactory.Default.Create(M68kCpuModel.M68EC020, bus)
    };

    private static void Store(ZeroWaitCodeBus bus, uint address, int size, uint value)
    {
        if (size == 2) bus.WriteLong(address, value);
        else bus.WriteWord(address, (ushort)(size == 0 ? value << 8 : value));
    }

    private static uint Load(ZeroWaitCodeBus bus, uint address, int size) => size switch
    {
        0 => ReadByte(bus, address), 1 => bus.ReadWord(address), _ => bus.ReadLong(address)
    };
}
