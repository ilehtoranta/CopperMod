using Xunit;

namespace CopperMod.Amiga.Lightweight.Tests;

public sealed class LightweightPaulaOutputTests
{
    private const long Field = LightweightClock.PalLongFieldCycles;
    private const long Frequency = 7_093_790;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    public void SignedFullVolumeChannelsRouteToTheirStereoSide(int channel, int side)
    {
        using var m = new LightweightA500Machine();
        Constant(m, channel, 0x7F7F);
        m.AdvanceHardwareTo(Field);
        var samples = m.AudioSamples.Span;
        for (var i = 0; i < samples.Length; i += 2)
        {
            Assert.Equal((short)16256, samples[i + side]);
            Assert.Equal((short)0, samples[i + (1 - side)]);
        }
    }

    [Fact]
    public void StereoPairSumsWithoutOverflowAtNegativeFullScale()
    {
        using var m = new LightweightA500Machine();
        Constant(m, 0, 0x8080);
        Constant(m, 3, 0x8080);
        m.AdvanceHardwareTo(Field);
        Assert.Equal(short.MinValue, m.AudioSamples.Span[0]);
        Assert.Equal((short)0, m.AudioSamples.Span[1]);
    }

    [Fact]
    public void VolumeChangeContributesOnlyItsPhysicalDurationToPcm()
    {
        using var m = new LightweightA500Machine();
        Constant(m, 0, 0x7F7F);
        Write(m, 0x0A8, 32, 74);
        m.AdvanceHardwareTo(Field);
        var oldTicks = 74L * 48_000;
        var expected = (short)((127 * 64 * oldTicks + 127 * 32 * (Frequency - oldTicks)) * 2 / Frequency);
        Assert.Equal(expected, m.AudioSamples.Span[0]);
        Assert.Equal((short)8128, m.AudioSamples.Span[2]);
    }

    [Fact]
    public void AlternatingFieldLengthsKeepFractionalSamplePhase()
    {
        using var m = new LightweightA500Machine();
        // Enable interlace without bitplanes: long/short fields alternate.
        Write(m, 0x100, 4, 0);
        long cycle = 0;
        long totalSamples = 0;
        for (var field = 0; field < 20; field++)
        {
            var before = cycle * 48_000 / Frequency;
            cycle += (field % 2 == 0 ? 313 : 312) * 454;
            m.AdvanceHardwareTo(cycle);
            var expected = cycle * 48_000 / Frequency - before;
            Assert.Equal(expected * 2, m.AudioSamples.Length);
            totalSamples += m.AudioSamples.Length / 2;
        }
        Assert.Equal(cycle * 48_000 / Frequency, totalSamples);
    }

    [Fact]
    public void PcmMatchesPiecewiseDacIntegralAcrossWritesAndFields()
    {
        using var m = new LightweightA500Machine();
        Constant(m, 0, 0x7F81);
        Constant(m, 1, 0x0080);
        Constant(m, 3, 0x4080);
        Write(m, 0x0A8, 32, 74);
        Write(m, 0x0A8, 64, 120);
        // These writes cannot change the current DAC level or byte deadline.
        Write(m, 0x0A2, 0x2000, 500);
        Write(m, 0x0A4, 4, 502);
        Write(m, 0x09E, 0x8008, 1000);
        Write(m, 0x09E, 0x0008, 2000);

        // An independent interval integral, not the engine resampler. At
        // 131072 CPU cycles all three period-zero channels load their low byte.
        (long Cycle, int Left, int Right)[] levels =
        [
            (0, 12224, 0), (74, 8160, 0), (120, 12224, 0),
            (1000, 8128, 0), (2000, 12224, 0),
            (131072, -16320, -8192), (2 * Field, 0, 0)
        ];
        long sampleIndex = 0;
        for (var field = 1; field <= 2; field++)
        {
            m.AdvanceHardwareTo(field * Field);
            var samples = m.AudioSamples.Span;
            for (var i = 0; i < samples.Length; i += 2, sampleIndex++)
            {
                var start = sampleIndex * Frequency;
                var end = start + Frequency;
                long left = 0, right = 0;
                for (var segment = 0; segment < levels.Length - 1; segment++)
                {
                    var duration = Math.Max(0,
                        Math.Min(end, levels[segment + 1].Cycle * 48_000) -
                        Math.Max(start, levels[segment].Cycle * 48_000));
                    left += levels[segment].Left * duration;
                    right += levels[segment].Right * duration;
                }
                Assert.Equal((short)(left * 2 / Frequency), samples[i]);
                Assert.Equal((short)(right * 2 / Frequency), samples[i + 1]);
            }
        }
        Assert.Equal(2 * Field * 48_000 / Frequency, sampleIndex);
    }

    [Fact]
    public void VolumeAttachmentIntegratesOldTargetLevelBeforeHighByteLoad()
    {
        using var m = new LightweightA500Machine();
        Constant(m, 1, 0x7F7F);
        Write(m, 0x09E, 0x8001, 0);
        Write(m, 0x0A6, 2, 0);
        Write(m, 0x0AA, 32, 74);
        m.AdvanceHardwareTo(Field);
        var oldTicks = 74L * 48_000;
        var expected = (short)((127 * 64 * oldTicks +
            127 * 32 * (Frequency - oldTicks)) * 2 / Frequency);
        Assert.Equal(expected, m.AudioSamples.Span[1]);
        Assert.Equal((short)8128, m.AudioSamples.Span[3]);
        for (var i = 0; i < m.AudioSamples.Length; i += 2)
            Assert.Equal((short)0, m.AudioSamples.Span[i]);
    }

    [Fact]
    public void HardwareAdvancePartitionDoesNotChangePcmOrChannelTiming()
    {
        using var whole = new LightweightA500Machine();
        using var split = new LightweightA500Machine();
        foreach (var m in new[] { whole, split })
        {
            Write(m, 0x0A6, 113, 0);
            Write(m, 0x0AA, 0x7F81, 0);
        }
        whole.AdvanceHardwareTo(1000);
        for (var cycle = 1; cycle <= 1000; cycle++) split.AdvanceHardwareTo(cycle);
        Write(whole, 0x0A8, 13, 1000);
        Write(split, 0x0A8, 13, 1000);
        whole.AdvanceHardwareTo(Field);
        for (var cycle = 1001L; cycle < Field; cycle += 79) split.AdvanceHardwareTo(cycle);
        split.AdvanceHardwareTo(Field);
        Assert.Equal(whole.AudioSamples.ToArray(), split.AudioSamples.ToArray());
        Assert.Equal(whole.GetPaulaChannelSnapshot(0), split.GetPaulaChannelSnapshot(0));
    }

    [Fact]
    public void ExternalResetPreservesAlreadyGeneratedAudioAndFractionalClock()
    {
        using var m = new LightweightA500Machine();
        Constant(m, 0, 0x7F7F);
        m.ResetExternalDevices(1000);
        m.AdvanceHardwareTo(Field);
        Assert.Equal((short)16256, m.AudioSamples.Span[0]);
        Assert.Equal((short)0, m.AudioSamples.Span[20]);
        Assert.Equal(Field * 48_000 / Frequency * 2, m.AudioSamples.Length);
        m.Reset();
        Assert.True(m.AudioSamples.IsEmpty);
        m.AdvanceHardwareTo(Field);
        Assert.All(m.AudioSamples.ToArray(), sample => Assert.Equal((short)0, sample));
    }

    [Theory]
    [InlineData(0x0001)]
    [InlineData(0x0010)]
    [InlineData(0x0011)]
    public void AttachAppliesVolumeAtHighAndPeriodAtLowAndMutesSource(ushort mask)
    {
        using var m = new LightweightA500Machine();
        Write(m, 0x09E, (ushort)(0x8000 | mask), 0);
        Write(m, 0x0A6, 2, 0);
        Write(m, 0x0AA, 0x0020, 0);
        Assert.Equal((mask & 1) != 0 ? 32 : 64, m.GetPaulaChannelSnapshot(1).Volume);
        Assert.Equal(428, m.GetPaulaChannelSnapshot(1).Period);
        m.AdvanceHardwareTo(4);
        Assert.Equal((mask & 16) != 0 ? 32 : 428, m.GetPaulaChannelSnapshot(1).Period);
        m.AdvanceHardwareTo(Field);
        Assert.All(m.AudioSamples.ToArray(), value => Assert.Equal((short)0, value));
        Assert.Null(m.UnsupportedActiveFeature);
    }

    [Fact]
    public void PeriodModulationAffectsNextReloadWithoutMovingCurrentDeadline()
    {
        using var m = new LightweightA500Machine();
        Write(m, 0x09E, 0x8010, 0);
        Write(m, 0x0B6, 10, 0);
        Write(m, 0x0BA, 0x1122, 0);
        Write(m, 0x0A6, 2, 0);
        Write(m, 0x0AA, 5, 0);
        m.AdvanceHardwareTo(4);
        Assert.Equal(5, m.GetPaulaChannelSnapshot(1).Period);
        Assert.Equal(20, m.GetPaulaChannelSnapshot(1).NextSampleCycle);
        m.AdvanceHardwareTo(20);
        Assert.Equal(30, m.GetPaulaChannelSnapshot(1).NextSampleCycle);
    }

    [Theory]
    [InlineData(0x0008)]
    [InlineData(0x0080)]
    public void LastChannelAttachMutesWithoutWrappingToChannelZero(ushort mask)
    {
        using var m = new LightweightA500Machine();
        Write(m, 0x09E, (ushort)(0x8000 | mask), 0);
        Constant(m, 3, 0x7F7F);
        m.AdvanceHardwareTo(Field);
        Assert.Equal(428, m.GetPaulaChannelSnapshot(0).Period);
        Assert.Equal(64, m.GetPaulaChannelSnapshot(0).Volume);
        Assert.All(m.AudioSamples.ToArray(), value => Assert.Equal((short)0, value));
    }

    [Fact]
    public void ClearingAttachUnmutesAtWriteAndDoesNotReplayModulation()
    {
        using var m = new LightweightA500Machine();
        Write(m, 0x09E, 0x8001, 0);
        Constant(m, 0, 0x7F7F);
        Write(m, 0x09E, 1, 1000);
        m.AdvanceHardwareTo(Field);
        Assert.Equal((short)0, m.AudioSamples.Span[0]);
        Assert.Equal((short)16256, m.AudioSamples.Span[20]);
    }

    [Theory]
    [InlineData(0x8010, 1392)] // Period-only requests at low byte, h15 of line 3.
    [InlineData(0x8001, 938)]  // Volume attachment requests at high byte.
    [InlineData(0x8011, 938)]  // Combined preserves high-byte request.
    public void DmaAttachmentUsesTheCorrespondingDataRequestPhase(ushort adk, long nextInput)
    {
        using var m = new LightweightA500Machine();
        Write(m, 0x0A2, 0x2000, 0);
        Write(m, 0x0A4, 4, 0);
        Write(m, 0x0A6, 400, 0);
        m.WriteChipWordDma(0x2002, 32);
        Write(m, 0x09E, adk, 0);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(488);
        if (adk == 0x8010)
        {
            Assert.Equal(long.MaxValue, m.GetPaulaChannelSnapshot(0).DmaInputCycle);
            m.AdvanceHardwareTo(1288);
        }
        Assert.Equal(nextInput, m.GetPaulaChannelSnapshot(0).DmaInputCycle);
    }

    private static void Constant(LightweightA500Machine m, int channel, ushort word)
    {
        Write(m, (ushort)(0x0A6 + channel * 16), 0, 0);
        Write(m, (ushort)(0x0AA + channel * 16), word, 0);
    }

    [Fact]
    public void PeriodAttachedDelayedInterruptWaitsForLowByteBoundary()
    {
        using var m = new LightweightA500Machine();
        Write(m, 0x0A2, 0x2000, 0);
        Write(m, 0x0A4, 1, 0);
        Write(m, 0x0A6, 400, 0);
        m.WriteChipWordDma(0x2000, 32);
        Write(m, 0x09E, 0x8010, 0);
        Write(m, 0x096, 0x8201, 0);
        m.AdvanceHardwareTo(1394); // Reload grant after first low-byte request.
        Assert.True(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        Write(m, 0x09C, 0x0080, 1400);
        m.AdvanceHardwareTo(2088); // Next high byte must not consume period-only IRQ.
        Assert.Equal(0, m.Intreq & 0x80);
        Assert.True(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
        m.AdvanceHardwareTo(2888);
        Assert.NotEqual(0, m.Intreq & 0x80);
        Assert.False(m.GetPaulaChannelSnapshot(0).DelayedInterruptPending);
    }

    [Fact]
    public void DmaPcmAndModulationAreIndependentOfAdvancePartition()
    {
        using var whole = new LightweightA500Machine();
        using var split = new LightweightA500Machine();
        foreach (var m in new[] { whole, split })
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var b = (ushort)(0x0A0 + channel * 16);
                var address = 0x2000 + channel * 0x100;
                for (var word = 0; word < 4; word++)
                    m.WriteChipWordDma((uint)(address + word * 2),
                        (ushort)(channel == 0 ? 20 + word * 10 : 0x7F81 ^ word * 0x1234));
                Write(m, (ushort)(b + 2), (ushort)address, 0);
                Write(m, (ushort)(b + 4), 4, 0);
                Write(m, (ushort)(b + 6), 200, 0);
            }
            Write(m, 0x09E, 0x8001, 0); // DMA-driven volume modulation on channel 1.
            Write(m, 0x096, 0x820F, 0);
        }
        for (var field = 1; field <= 3; field++)
        {
            whole.AdvanceHardwareTo(field * Field);
            for (var cycle = split.Cycle + 31; cycle < field * Field; cycle += 31)
                split.AdvanceHardwareTo(cycle);
            split.AdvanceHardwareTo(field * Field);
            Assert.Equal(whole.AudioSamples.ToArray(), split.AudioSamples.ToArray());
            Assert.Contains(whole.AudioSamples.ToArray(), value => value != 0);
            for (var channel = 0; channel < 4; channel++)
                Assert.Equal(whole.GetPaulaChannelSnapshot(channel), split.GetPaulaChannelSnapshot(channel));
        }
    }

    private static void Write(LightweightA500Machine m, ushort register, ushort value, long cycle)
    {
        m.AdvanceHardwareTo(cycle);
        m.WriteCustomRegisterFromCopper(register, value, cycle);
    }
}
