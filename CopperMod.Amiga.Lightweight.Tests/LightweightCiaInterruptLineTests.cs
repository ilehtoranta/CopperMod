using Copper68k;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

// HRM appendix F: enabled CIA causes assert /IRQ until ICR is read.
// These check the held-line contract, not sub-CCK Paula synchronizer timing.
public sealed class LightweightCiaInterruptLineTests
{
    [Fact]
    public void EnablingPendingCauseAssertsLineAndResetReleasesIt()
    {
        var cia = new LightweightCia();
        cia.Reset();
        cia.LatchFlag(10);
        Assert.False(cia.InterruptAsserted);
        cia.WriteRegister(13, 0x90, 20, out var irqCycle);
        Assert.Equal(20, irqCycle);
        Assert.True(cia.InterruptAsserted);
        cia.Reset();
        Assert.False(cia.InterruptAsserted);
        Assert.Equal(0, cia.ReadRegister(13, 0xFF, 20, out _));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ClearingPaulaCannotLoseAnUnacknowledgedCiaRequest(bool ciaA, bool maskAfterAssertion)
    {
        using var m = new LightweightA500Machine();
        long cycle = 0;
        uint cia = ciaA ? 0xBFE001u : 0xBFD000u;
        ushort bit = ciaA ? (ushort)8 : (ushort)0x2000;
        m.WriteWord(0xDFF09A, (ushort)(0xC000 | bit), ref cycle, M68kBusAccessKind.CpuDataWrite);
        WriteCia(4, 3);
        WriteCia(5, 0);
        WriteCia(13, 0x81);
        WriteCia(14, 0x19); // One-shot: no later underflow can rescue a lost IRQ.
        m.AdvanceHardwareTo(cycle + 100);
        cycle = m.Cycle;
        Assert.Equal(bit, m.Intreq & bit);
        if (maskAfterAssertion) WriteCia(13, 1);

        m.WriteWord(0xDFF09C, bit, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.AdvanceHardwareTo(cycle + 20);
        cycle = m.Cycle;
        Assert.Equal(bit, m.Intreq & bit);
        Assert.Equal(ciaA ? 2 : 6, m.InterruptPinLevel);

        var icr = m.ReadByte(cia + 0xD00, ref cycle, M68kBusAccessKind.CpuDataRead);
        Assert.Equal(0x81, icr);
        m.WriteWord(0xDFF09C, bit, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.AdvanceHardwareTo(cycle + 20);
        Assert.Equal(0, m.Intreq & bit);
        Assert.Equal(0, m.InterruptPinLevel);

        void WriteCia(uint register, byte value) =>
            m.WriteByte(cia + register * 256, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MaskedPendingCauseDoesNotPreventClearingSoftwarePaulaRequest(bool ciaA)
    {
        using var m = new LightweightA500Machine();
        long cycle = 0;
        uint cia = ciaA ? 0xBFE001u : 0xBFD000u;
        ushort bit = ciaA ? (ushort)8 : (ushort)0x2000;
        m.WriteByte(cia + 0x400, 3, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.WriteByte(cia + 0x500, 0, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.WriteByte(cia + 0xE00, 0x19, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.AdvanceHardwareTo(cycle + 100);
        cycle = m.Cycle;
        _ = m.ReadByte(cia + 0x400, ref cycle, M68kBusAccessKind.CpuDataRead);
        Assert.Equal(1, ciaA ? m.CiaAPendingInterrupts : m.CiaBPendingInterrupts);
        m.WriteWord(0xDFF09C, (ushort)(0x8000 | bit), ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.WriteWord(0xDFF09C, bit, ref cycle, M68kBusAccessKind.CpuDataWrite);
        m.AdvanceHardwareTo(cycle + 20);
        Assert.Equal(0, m.Intreq & bit);
    }
}
