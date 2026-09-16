using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightPaulaDmaTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FixedSlotRetainsAddressThenDataBeforePaulaLoad(int channel)
    {
        using var m = new LightweightA500Machine();
        Configure(m, channel);
        var slot = 32 + channel * 4;
        m.WriteChipWordDma(0x1000, 0x1122);
        Write(m, 0x096, (ushort)(0x8200 | 1 << channel), 0);
        m.AdvanceHardwareTo(slot - 2);
        var accepted = m.GetPaulaChannelSnapshot(channel);
        Assert.Equal(0x1000u, accepted.PendingAddress);
        Assert.Equal(slot, accepted.DmaOutputCycle);
        Assert.Equal(3, accepted.RemainingWords);
        Assert.False(m.CanCopperOwnOutputSlot(slot));
        Assert.False(m.CanBlitterAdvanceControl(slot - 2));
        // A future location update must not redirect an accepted address.
        Write(m, (ushort)(0x0A2 + channel * 16), 0x2000, slot - 1);
        m.WriteChipWordDma(0x1000, 0x3344);
        m.AdvanceHardwareTo(slot);
        var output = m.GetPaulaChannelSnapshot(channel);
        Assert.Equal(0x1002u, output.CurrentAddress);
        Assert.Equal(2, output.RemainingWords);
        Assert.Equal((ushort)0x3344, output.PendingData);
        Assert.True(m.IsHigherPriorityOutputOwned(slot));
        Assert.Equal(0, m.Intreq & (0x80 << channel));
        m.WriteChipWordDma(0x1000, 0x5566);
        m.AdvanceHardwareTo(slot + 2);
        Assert.Equal(LightweightPaulaAudioState.DmaStartupData,
            m.GetPaulaChannelSnapshot(channel).State);
        Assert.Equal((ushort)0x3344, m.GetPaulaChannelSnapshot(channel).PendingData);
        Assert.NotEqual(0, m.Intreq & (0x80 << channel));
        Assert.Equal((sbyte)0, m.GetPaulaChannelSnapshot(channel).CurrentSample);
    }

    [Fact]
    public void StartupDiscardsFirstWordAndRequestsOnlyOneWordPerLine()
    {
        using var m = new LightweightA500Machine();
        Configure(m, period: 4);
        m.WriteChipWordDma(0x1000, 0x7777);
        m.WriteChipWordDma(0x1002, 0x1182);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(487);
        Assert.Equal((sbyte)0, m.GetPaulaChannelSnapshot(0).CurrentSample);
        m.AdvanceHardwareTo(488);
        var high = m.GetPaulaChannelSnapshot(0);
        Assert.Equal((ushort)0x1182, high.OutputLatch);
        Assert.Equal((sbyte)0x11, high.CurrentSample);
        Assert.Equal(938, high.DmaInputCycle);
        m.AdvanceHardwareTo(496);
        Assert.Equal(unchecked((sbyte)0x82), m.GetPaulaChannelSnapshot(0).CurrentSample);
        m.AdvanceHardwareTo(900);
        Assert.Equal(unchecked((sbyte)0x82), m.GetPaulaChannelSnapshot(0).CurrentSample);
        Assert.Equal(0x1004u, m.GetPaulaChannelSnapshot(0).CurrentAddress);
    }

    [Fact]
    public void LengthOneReloadArmsInterruptAtGrantAndSurvivesIdle()
    {
        using var m = new LightweightA500Machine();
        Configure(m, length: 1, period: 200);
        m.WriteChipWordDma(0x1000, 0x1182);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(939);
        Assert.False(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        m.AdvanceHardwareTo(940);
        Assert.True(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        Write(m, 0x096, 1, 944);
        m.AdvanceHardwareTo(1288);
        Assert.Equal(LightweightPaulaAudioState.Idle, m.GetPaulaChannelSnapshot(0).State);
        Assert.True(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        Write(m, 0x09C, 0x0080, 1290);
        Write(m, 0x096, 0x8001, 1292);
        Assert.False(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        Assert.NotEqual(0, m.Intreq & 0x80);
    }

    [Fact]
    public void ReloadUsesNewLocationAndLengthAtNextAddressPhase()
    {
        using var m = new LightweightA500Machine();
        Configure(m, length: 1, period: 200);
        m.WriteChipWordDma(0x1000, 0x1122);
        m.WriteChipWordDma(0x2000, 0x3344);
        Write(m, 0x096, 0x8201, 0);
        Write(m, 0x0A2, 0x2000, 600);
        Write(m, 0x0A4, 2, 600);
        m.AdvanceHardwareTo(940);
        Assert.Equal(0x2002u, m.GetPaulaChannelSnapshot(0).CurrentAddress);
        Assert.Equal(1, m.GetPaulaChannelSnapshot(0).RemainingWords);
        m.AdvanceHardwareTo(1288);
        Assert.Equal((ushort)0x3344, m.GetPaulaChannelSnapshot(0).OutputLatch);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0x0200)]
    public void DmaOffBeforeInputCancelsButAcceptedReadStillCompletes(ushort disable)
    {
        using var m = new LightweightA500Machine();
        Configure(m);
        Write(m, 0x096, 0x8201, 0);
        Write(m, 0x096, disable, 28);
        m.AdvanceHardwareTo(34);
        Assert.Equal(0x1000u, m.GetPaulaChannelSnapshot(0).CurrentAddress);
        Write(m, 0x096, (ushort)(0x8000 | disable), 36);
        m.AdvanceHardwareTo(484);
        Write(m, 0x096, disable, 485);
        m.AdvanceHardwareTo(488);
        Assert.Equal(0x1002u, m.GetPaulaChannelSnapshot(0).CurrentAddress);
        Assert.NotEqual(0, m.Intreq & 0x80);
        Assert.Equal(long.MaxValue, m.GetPaulaChannelSnapshot(0).DmaInputCycle);
    }

    [Fact]
    public void BriefTogglePreservesHighLowAndPrefetchedWord()
    {
        using var m = new LightweightA500Machine();
        Configure(m, period: 200);
        m.WriteChipWordDma(0x1002, 0x1122);
        m.WriteChipWordDma(0x1004, 0x3344);
        Write(m, 0x096, 0x8201, 0);
        Write(m, 0x096, 1, 600);
        Write(m, 0x096, 0x8001, 602);
        Assert.Equal(888, m.GetPaulaChannelSnapshot(0).NextSampleCycle);
        m.AdvanceHardwareTo(888);
        Assert.Equal((sbyte)0x22, m.GetPaulaChannelSnapshot(0).CurrentSample);
        m.AdvanceHardwareTo(1288);
        Assert.Equal((sbyte)0x33, m.GetPaulaChannelSnapshot(0).CurrentSample);
        Assert.Equal((ushort)0x3344, m.GetPaulaChannelSnapshot(0).OutputLatch);
    }

    [Fact]
    public void LateEnableWaitsForNextLineAndPendingRequestCrossesField()
    {
        using var m = new LightweightA500Machine();
        Configure(m);
        Write(m, 0x096, 0x8201, LightweightClock.PalLongFieldCycles - 2);
        m.AdvanceHardwareTo(LightweightClock.PalLongFieldCycles);
        Assert.Equal(LightweightClock.PalLongFieldCycles + 30,
            m.GetPaulaChannelSnapshot(0).DmaInputCycle);
        m.AdvanceHardwareTo(LightweightClock.PalLongFieldCycles + 34);
        Assert.Equal(0x1002u, m.GetPaulaChannelSnapshot(0).CurrentAddress);
        Assert.Equal(1, m.CompletedFrames);
    }

    [Fact]
    public void CpuChipAccessWaitsForAudioAndInterruptAppearsAfterLoadDelay()
    {
        using var m = new LightweightA500Machine();
        Configure(m);
        Write(m, 0x09A, 0xC080, 0);
        Write(m, 0x096, 0x8201, 0);
        long cpuCycle = 32;
        _ = m.ReadWord(0x2000, ref cpuCycle, Copper68k.M68kBusAccessKind.CpuDataRead);
        Assert.Equal(36, m.Cycle);
        m.AdvanceHardwareTo(41);
        Assert.Equal(0, m.InterruptPinLevel);
        m.AdvanceHardwareTo(42);
        Assert.Equal(4, m.InterruptPinLevel);
    }

    [Fact]
    public void ZeroLengthAndChipPointerWrapUseHardwareWidths()
    {
        using var m = new LightweightA500Machine();
        Configure(m, length: 0);
        Write(m, 0x0A0, 7, 0);
        Write(m, 0x0A2, 0xFFFE, 0);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(32);
        Assert.Equal(0u, m.GetPaulaChannelSnapshot(0).CurrentAddress);
        Assert.Equal(65535, m.GetPaulaChannelSnapshot(0).RemainingWords);
    }

    private static void Configure(LightweightA500Machine m, int channel = 0,
        ushort length = 3, ushort period = 200)
    {
        var b = 0x0A0 + channel * 16;
        Write(m, (ushort)(b + 2), 0x1000, 0);
        Write(m, (ushort)(b + 4), length, 0);
        Write(m, (ushort)(b + 6), period, 0);
    }

    [Fact]
    public void CpuLongwordRetainsBothWordPhasesWithAllFourChannelsEnabled()
    {
        using var m = new LightweightA500Machine();
        for (var channel = 0; channel < 4; channel++) Configure(m, channel);
        m.WriteChipWordDma(0x2000, 0x1234);
        m.WriteChipWordDma(0x2002, 0x5678);
        Write(m, 0x096, 0x820F, 0);
        long cpuCycle = 32;
        var value = m.ReadLong(0x2000, ref cpuCycle, Copper68k.M68kBusAccessKind.CpuDataRead);
        Assert.Equal(0x12345678u, value);
        Assert.Equal(40, cpuCycle);
        m.AdvanceHardwareTo(46);
        for (var channel = 0; channel < 4; channel++)
            Assert.Equal(0x1002u, m.GetPaulaChannelSnapshot(channel).CurrentAddress);
    }

    [Fact]
    public void CpuDataWriteOverwritesQueuedDmaHoldingLatchOnly()
    {
        using var m = new LightweightA500Machine();
        Configure(m, period: 200);
        m.WriteChipWordDma(0x1002, 0x1122);
        m.WriteChipWordDma(0x1004, 0x3344);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(942);
        Assert.Equal((ushort)0x3344, m.GetPaulaChannelSnapshot(0).HoldingLatch);
        Write(m, 0x0AA, 0x5566, 944);
        Assert.Equal((ushort)0x1122, m.GetPaulaChannelSnapshot(0).OutputLatch);
        m.AdvanceHardwareTo(1288);
        Assert.Equal((ushort)0x5566, m.GetPaulaChannelSnapshot(0).OutputLatch);
        Assert.Equal((sbyte)0x55, m.GetPaulaChannelSnapshot(0).CurrentSample);
    }

    [Fact]
    public void QueuedDataSurvivesFieldBoundaryWithoutRestartingPeriod()
    {
        using var m = new LightweightA500Machine();
        Configure(m, period: 200);
        m.WriteChipWordDma(0x1002, 0x1122);
        m.WriteChipWordDma(0x1004, 0x3344);
        var start = LightweightClock.PalLongFieldCycles - 2 * 454;
        Write(m, 0x096, 0x8201, start);
        m.AdvanceHardwareTo(start + 896);
        Assert.Equal((sbyte)0x22, m.GetPaulaChannelSnapshot(0).CurrentSample);
        m.AdvanceHardwareTo(LightweightClock.PalLongFieldCycles);
        Assert.Equal(start + 1288, m.GetPaulaChannelSnapshot(0).NextSampleCycle);
        m.AdvanceHardwareTo(start + 1288);
        Assert.Equal((sbyte)0x33, m.GetPaulaChannelSnapshot(0).CurrentSample);
    }

    [Fact]
    public void ResetCancelsAcceptedDmaAndDelayedInterrupt()
    {
        using var m = new LightweightA500Machine();
        Configure(m, length: 1);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(940);
        Assert.True(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        m.Reset();
        var reset = m.GetPaulaChannelSnapshot(0);
        Assert.False(reset.DmaEnabled);
        Assert.False(reset.DelayedInterruptPending);
        Assert.Equal(long.MaxValue, reset.DmaLoadCycle);
        Assert.Equal(long.MaxValue, m.PaulaNextCycle);
        m.AdvanceHardwareTo(1000);
        Assert.Equal(0, m.Intreq & 0x0780);
    }

    [Fact]
    public void DelayedReloadIrqIsConsumedAtHighByteBoundaryAfterClear()
    {
        using var m = new LightweightA500Machine();
        Configure(m, length: 1, period: 200);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(940);
        Write(m, 0x09C, 0x0080, 944);
        m.AdvanceHardwareTo(1286);
        Assert.Equal(0, m.Intreq & 0x80);
        Assert.True(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        m.AdvanceHardwareTo(1288);
        Assert.NotEqual(0, m.Intreq & 0x80);
        Assert.False(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
    }

    private static void Write(LightweightA500Machine m, ushort offset, ushort value, long cycle)
    {
        m.AdvanceHardwareTo(cycle);
        m.WriteCustomRegisterFromCopper(offset, value, cycle);
    }

    [Fact]
    public void AudioInterruptWakesStoppedCpuThroughLevelFourAutovector()
    {
        using var m = new LightweightA500Machine();
        m.WriteChipWordDma(0x1000, 0x4E72); // STOP #$2000
        m.WriteChipWordDma(0x1002, 0x2000);
        m.WriteChipWordDma(0x0070, 0);
        m.WriteChipWordDma(0x0072, 0x1100);
        m.WriteChipWordDma(0x1100, 0x4E71);
        m.WriteChipWordDma(0x1102, 0x60FC);
        m.Reset();
        Configure(m);
        Write(m, 0x09A, 0xC080, 0);
        Write(m, 0x096, 0x8201, 0);
        m.ExecuteFrame();
        Assert.False(m.Cpu.Stopped);
        Assert.Equal(4, (m.Cpu.StatusRegister >> 8) & 7);
        Assert.InRange(m.Cpu.ProgramCounter, 0x1100u, 0x1104u);
        Assert.True(m.GetPaulaChannelSnapshot(0).DmaEnabled);
    }
}
