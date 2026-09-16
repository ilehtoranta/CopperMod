using Copper68k;
using CopperMod.Amiga.Lightweight;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightCopperReadStrobeTests
{
    // HRM chapter 2, Starting the Copper After Reset: MOVE.W COPJMP1(a1),d0.
    [Theory]
    [InlineData(0x88, false, 0x2000)]
    [InlineData(0x8A, false, 0x3000)]
    [InlineData(0x88, true, 0x2000)]
    [InlineData(0x89, true, 0x2000)]
    [InlineData(0x8A, true, 0x3000)]
    [InlineData(0x8B, true, 0x3000)]
    public void CpuReadSelectsCopperListWhileDmaIsDisabled(int offset, bool byteRead, uint expected)
    {
        using var m = new LightweightA500Machine();
        long cycle = 0;
        m.WriteLong(0xDFF080, 0x2000, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.WriteLong(0xDFF084, 0x3000, ref cycle, M68kBusAccessKind.CpuDataWrite);
        Assert.Equal(0u, m.CopperProgramCounter);
        if (byteRead) _ = m.ReadByte(0xDFF000u + (uint)offset, ref cycle, M68kBusAccessKind.CpuDataRead);
        else _ = m.ReadWord(0xDFF000u + (uint)offset, ref cycle, M68kBusAccessKind.CpuDataRead);
        Assert.Equal(expected, m.CopperProgramCounter);
        Assert.Equal(long.MaxValue, m.CopperPendingOutputCycle);
        // Strobing a disabled Copper must not fetch or execute the list yet.
        m.AdvanceHardwareTo(cycle + 40);
        Assert.Equal(expected, m.CopperProgramCounter);
    }

    [Fact]
    public void LongReadStrobesBothListsInWordOrder()
    {
        using var m = new LightweightA500Machine();
        long cycle = 0;
        m.WriteLong(0xDFF080, 0x2000, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.WriteLong(0xDFF084, 0x3000, ref cycle, M68kBusAccessKind.CpuDataWrite);
        _ = m.ReadLong(0xDFF088, ref cycle, M68kBusAccessKind.CpuDataRead);
        Assert.Equal(0x3000u, m.CopperProgramCounter);
    }
}
