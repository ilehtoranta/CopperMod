using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68040CacheControlTests
{
    // MC68040UM 2.2.2.5: CACR bit 15 enables instructions, bit 31 data.
    // Bit 0 is reserved, unlike the MC68020/030 EI bit.
    [Theory]
    [InlineData(0x00008000u, 1u)]
    [InlineData(0x80008000u, 1u)]
    [InlineData(0x80000000u, 7u)]
    [InlineData(0x00000001u, 7u)]
    [InlineData(0u, 7u)]
    public void InstructionEnableUsesThe040Bit(uint control, uint afterWrite)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xC01000, 0x4E7B, 0x0002);
        WriteWords(bus, 0xC01180, 0x7201);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(0xC01000, 0xC03000);
        cpu.State.D[0] = control;
        cpu.ExecuteInstruction();
        cpu.State.ProgramCounter = 0xC01180;
        cpu.ExecuteInstruction();
        Assert.Equal(1u, cpu.State.D[1]);
        bus.WriteWord(0xC01180, 0x7207);
        cpu.State.ProgramCounter = 0xC01180;
        cpu.ExecuteInstruction();
        Assert.Equal(afterWrite, cpu.State.D[1]);
    }

    public static IEnumerable<object[]> MaintenanceCases()
    {
        foreach (var model in new[] { M68kCpuModel.M68040, M68kCpuModel.M68060 })
        foreach (ushort opcode in new ushort[] { 0xF488, 0xF490, 0xF498, 0xF4A8, 0xF4B0, 0xF4B8 })
            yield return new object[] { model, opcode };
    }

    [Theory]
    [MemberData(nameof(MaintenanceCases))]
    public void GuestCacheMaintenanceMakesModifiedChipCodeVisible(M68kCpuModel model, ushort opcode)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0x1000, 0x4E7B, 0x0002);
        WriteWords(bus, 0x1040, opcode);
        WriteWords(bus, 0x1180, 0x7201);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x3000);
        cpu.State.D[0] = 0x8000;
        cpu.ExecuteInstruction();
        cpu.State.ProgramCounter = 0x1180; cpu.ExecuteInstruction();
        bus.WriteWord(0x1180, 0x7207);
        cpu.State.ProgramCounter = 0x1180; cpu.ExecuteInstruction();
        Assert.Equal(1u, cpu.State.D[1]);
        cpu.State.A[0] = 0x118F;
        cpu.State.StatusRegister = 0x271F;
        cpu.State.ProgramCounter = 0x1040; cpu.ExecuteInstruction();
        Assert.Equal(0x271F, cpu.State.StatusRegister);
        cpu.State.ProgramCounter = 0x1180; cpu.ExecuteInstruction();
        Assert.Equal(7u, cpu.State.D[1]);
    }

    [Theory]
    [InlineData(0xF448)] // Data only.
    [InlineData(0xF418)] // No cache.
    [InlineData(0xF488)] // Different line.
    [InlineData(0xF490)] // Different page.
    public void UnselectedInstructionEntriesStayCached(int opcode)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0xC01000, 0x4E7B, 0x0002);
        WriteWords(bus, 0xC01040, (ushort)opcode);
        WriteWords(bus, 0xC01180, 0x7201);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(0xC01000, 0xC03000);
        cpu.State.D[0] = 0x8000; cpu.ExecuteInstruction();
        cpu.State.ProgramCounter = 0xC01180; cpu.ExecuteInstruction();
        bus.WriteWord(0xC01180, 0x7207);
        cpu.State.A[0] = 0xC02000;
        cpu.State.ProgramCounter = 0xC01040; cpu.ExecuteInstruction();
        cpu.State.ProgramCounter = 0xC01180; cpu.ExecuteInstruction();
        Assert.Equal(1u, cpu.State.D[1]);
    }

    [Theory]
    [InlineData(0xF498, 0x001Fu, 8)]
    [InlineData(0xF4B8, 0x001Fu, 8)]
    [InlineData(0xF480, 0x201Fu, 4)] // Illegal scope.
    public void CacheMaintenanceHonorsPrivilegeAndEncoding(int opcode, uint sr, int vector)
    {
        var bus = new Copper68kTestBus(0x10000);
        bus.WriteWords(0x1000, (ushort)opcode);
        bus.WriteLong((uint)vector * 4, 0x4000);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68040, bus);
        cpu.Reset(0x1000, 0x7000); cpu.State.StatusRegister = (ushort)sr;
        cpu.ExecuteInstruction();
        Assert.Equal(0x4000u, cpu.State.ProgramCounter);
        Assert.Equal(0x1000u, bus.ReadLong(0x6FFA));
        Assert.Equal(vector * 4, bus.ReadWord(0x6FFE));
    }
}
