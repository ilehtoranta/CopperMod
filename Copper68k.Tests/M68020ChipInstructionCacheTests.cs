using Copper68k;
using static Copper68k.Tests.M68kInterpreterTestHelpers;

namespace Copper68k.Tests;

public sealed class M68020ChipInstructionCacheTests
{
    // Commodore exec/CacheClearE documents Chip RAM instruction caching;
    // Motorola MC68020 UM section 4 requires explicit instruction-cache invalidation.
    // Observe executed instructions, not a timing constant or internal cache entry.
    [Theory]
    [InlineData(M68kCpuModel.M68020, 1u, 1u)]
    [InlineData(M68kCpuModel.M68EC020, 1u, 1u)]
    [InlineData(M68kCpuModel.M68020, 0u, 7u)]
    [InlineData(M68kCpuModel.M68EC020, 0u, 7u)]
    public void ChipCodeWritesBecomeVisibleAccordingToGuestCacheControl(
        M68kCpuModel model, uint control, uint beforeFlush)
    {
        var bus = new ZeroWaitCodeBus();
        WriteWords(bus, 0x1000, 0x4E7B, 0x0002); // MOVEC D0,CACR
        WriteWords(bus, 0x1180, 0x7201, 0x4E75); // MOVEQ #1,D1; RTS
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x1000, 0x3000);
        cpu.State.D[0] = control;
        cpu.ExecuteInstruction();

        cpu.State.ProgramCounter = 0x1180;
        cpu.ExecuteInstruction();
        Assert.Equal(1u, cpu.State.D[1]);
        bus.WriteWord(0x1180, 0x7207); // Data/DMA write, not a guest cache flush.
        cpu.State.ProgramCounter = 0x1180;
        cpu.ExecuteInstruction();
        Assert.Equal(beforeFlush, cpu.State.D[1]);

        cpu.State.ProgramCounter = 0x1000;
        cpu.State.D[0] = 9; // Clear all and enable.
        cpu.ExecuteInstruction();
        cpu.State.ProgramCounter = 0x1180;
        cpu.ExecuteInstruction();
        Assert.Equal(7u, cpu.State.D[1]);
    }
}
