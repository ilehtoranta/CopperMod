using CopperMod.Amiga.Lightweight;
using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightPaulaTests
{
    [Fact]
    public void AudioRegistersUseOneOwnerAndApplyOcsLatchRules()
    {
        using var machine = new LightweightA500Machine();
        const ushort channelBase = LightweightRegisters.Aud0lch + 0x20;

        WriteRegister(machine, LightweightRegisters.AdkconWrite, 0x8011, 0);
        WriteRegister(machine, LightweightRegisters.AdkconWrite, 0x0010, 0);
        WriteRegister(machine, channelBase, 0x0012, 0);
        WriteRegister(machine, channelBase + 2, 0x3457, 0);
        WriteRegister(machine, channelBase + 4, 0x0000, 0);
        WriteRegister(machine, channelBase + 6, 0x0000, 0);
        WriteRegister(machine, channelBase + 8, 0x007F, 0);

        var snapshot = machine.GetPaulaChannelSnapshot(2);
        Assert.Equal((ushort)0x0001, machine.Adkcon);
        Assert.Equal((ushort)0x0001,
            machine.GetCustomRegister(LightweightRegisters.Adkconr));
        Assert.Null(machine.UnsupportedActiveFeature);
        Assert.Equal(0x0002_3456u, snapshot.Location);
        Assert.Equal(65_536, snapshot.LengthWords);
        Assert.Equal(0, snapshot.Period);
        Assert.Equal(64, snapshot.Volume);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ManualAudioPlaysHighThenLowByteOnAllFourChannels(int channel)
    {
        using var machine = new LightweightA500Machine();
        var registerBase = (ushort)(LightweightRegisters.Aud0lch + (channel * 0x10));

        WriteRegister(machine, (ushort)(registerBase + 6), 0x0002, 0);
        WriteRegister(machine, (ushort)(registerBase + 0x0A), 0x7F81, 0);

        var high = machine.GetPaulaChannelSnapshot(channel);
        Assert.Equal((sbyte)0x7F, high.CurrentSample);
        Assert.Equal((ushort)0x7F81, high.OutputLatch);
        Assert.True(high.NextByteIsLow);
        Assert.Equal(LightweightPaulaAudioState.HighByte, high.State);
        Assert.Equal(4, high.NextSampleCycle);
        Assert.Equal(LightweightPaulaAudio.GetInterruptBit(channel),
            machine.Intreq & LightweightPaulaAudio.GetInterruptBit(channel));

        machine.AdvanceHardwareTo(4);

        var low = machine.GetPaulaChannelSnapshot(channel);
        Assert.Equal(unchecked((sbyte)0x81), low.CurrentSample);
        Assert.False(low.NextByteIsLow);
        Assert.Equal(8, low.NextSampleCycle);
        Assert.Equal(6, low.ManualIrqCheckCycle);
    }

    [Fact]
    public void ManualAudioKeepsSeparateHoldingAndOutputLatches()
    {
        using var machine = new LightweightA500Machine();

        WriteRegister(machine, LightweightRegisters.Aud0per, 0x0002, 0);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x7F81, 0);
        WriteRegister(machine, LightweightRegisters.IntreqWrite, 0x0080, 2);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x4080, 2);

        var queued = machine.GetPaulaChannelSnapshot(0);
        Assert.Equal((ushort)0x7F81, queued.OutputLatch);
        Assert.Equal((ushort)0x4080, queued.HoldingLatch);
        Assert.True(queued.HoldingLatchWritten);

        machine.AdvanceHardwareTo(4);
        Assert.Equal(unchecked((sbyte)0x81),
            machine.GetPaulaChannelSnapshot(0).CurrentSample);
        machine.AdvanceHardwareTo(6);
        Assert.Equal(-1, machine.GetPaulaChannelSnapshot(0).IrqCheck);
        machine.AdvanceHardwareTo(8);

        var restarted = machine.GetPaulaChannelSnapshot(0);
        Assert.Equal((sbyte)0x40, restarted.CurrentSample);
        Assert.Equal((ushort)0x4080, restarted.OutputLatch);
        Assert.False(restarted.HoldingLatchWritten);
        Assert.Equal(12, restarted.NextSampleCycle);
    }

    [Fact]
    public void ManualCompletionUsesIrqStateSampledOneCckEarlier()
    {
        using var machine = new LightweightA500Machine();

        WriteRegister(machine, LightweightRegisters.Aud0per, 0x0002, 0);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x7F81, 0);
        machine.AdvanceHardwareTo(6);
        Assert.Equal(1, machine.GetPaulaChannelSnapshot(0).IrqCheck);

        WriteRegister(machine, LightweightRegisters.IntreqWrite, 0x0080, 6);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x4080, 6);
        machine.AdvanceHardwareTo(8);

        var completed = machine.GetPaulaChannelSnapshot(0);
        Assert.Equal(LightweightPaulaAudioState.Idle, completed.State);
        Assert.Equal(unchecked((sbyte)0x81), completed.CurrentSample);
        Assert.Equal((ushort)0x7F81, completed.OutputLatch);
        Assert.Equal((ushort)0x4080, completed.HoldingLatch);
        Assert.True(completed.HoldingLatchWritten);
    }

    [Fact]
    public void PeriodWriteBeforeBoundaryChangesOnlyFollowingInterval()
    {
        using var machine = new LightweightA500Machine();

        WriteRegister(machine, LightweightRegisters.Aud0per, 0x0004, 0);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x7F81, 0);
        WriteRegister(machine, LightweightRegisters.Aud0per, 0x0002, 6);

        Assert.Equal(8, machine.GetPaulaChannelSnapshot(0).NextSampleCycle);
        machine.AdvanceHardwareTo(8);

        var low = machine.GetPaulaChannelSnapshot(0);
        Assert.Equal(unchecked((sbyte)0x81), low.CurrentSample);
        Assert.Equal(12, low.NextSampleCycle);
    }

    [Fact]
    public void PeriodZeroUsesFullSixteenBitCounter()
    {
        using var machine = new LightweightA500Machine();

        WriteRegister(machine, LightweightRegisters.Aud0per, 0, 0);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x7F81, 0);

        Assert.Equal(131_072,
            machine.GetPaulaChannelSnapshot(0).NextSampleCycle);
    }

    [Fact]
    public void ManualPlaybackPhaseSurvivesFieldBoundary()
    {
        using var machine = new LightweightA500Machine();
        var startCycle = LightweightClock.PalLongFieldCycles - 2;

        WriteRegister(machine, LightweightRegisters.Aud0per, 0x0002, startCycle);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x7F81, startCycle);
        machine.AdvanceHardwareTo(LightweightClock.PalLongFieldCycles);

        Assert.Equal(1, machine.CompletedFrames);
        Assert.Equal((sbyte)0x7F,
            machine.GetPaulaChannelSnapshot(0).CurrentSample);
        machine.AdvanceHardwareTo(LightweightClock.PalLongFieldCycles + 2);
        Assert.Equal(unchecked((sbyte)0x81),
            machine.GetPaulaChannelSnapshot(0).CurrentSample);
    }

    [Fact]
    public void ManualAudioInterruptUsesHardwareVisibilityDelay()
    {
        using var machine = new LightweightA500Machine();

        WriteRegister(machine, LightweightRegisters.IntenaWrite, 0xC080, 0);
        WriteRegister(machine, LightweightRegisters.Aud0dat, 0x7F81, 0);

        Assert.Equal((ushort)0x0080, machine.Intreq & 0x0080);
        machine.AdvanceHardwareTo(7);
        Assert.Equal(0, machine.InterruptPinLevel);
        machine.AdvanceHardwareTo(8);
        Assert.Equal(4, machine.InterruptPinLevel);
        Assert.Equal(8, machine.InterruptPinChangeCycle);
    }

    [Fact]
    public void AudioDmaRequiresMasterAndChannelEnable()
    {
        using var machine = new LightweightA500Machine();

        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8001, 0);
        Assert.Null(machine.UnsupportedActiveFeature);
        WriteRegister(machine, LightweightRegisters.DmaconWrite, 0x8200, 0);

        Assert.Null(machine.UnsupportedActiveFeature);
        Assert.True(machine.GetPaulaChannelSnapshot(0).DmaEnabled);
        Assert.Equal(30, machine.GetPaulaChannelSnapshot(0).DmaInputCycle);
    }

    private static void WriteRegister(
        LightweightA500Machine machine,
        ushort offset,
        ushort value,
        long cycle)
    {
        machine.AdvanceHardwareTo(cycle);
        machine.WriteCustomRegisterFromCopper(offset, value, cycle);
    }
}
