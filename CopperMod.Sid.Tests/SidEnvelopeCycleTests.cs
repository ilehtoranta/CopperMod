namespace CopperMod.Sid.Tests;

// Register-driven tests; no fabricated private latch combinations. Timing sources
// and the deterministic reset convention are in docs/SID-timing-contract.md.
public sealed class SidEnvelopeCycleTests
{
    private static readonly int[] Periods =
        [9, 32, 63, 95, 149, 220, 267, 313, 392, 977, 1954, 3126, 3907, 11720, 19532, 31251];
    public static IEnumerable<object[]> Rates => Enumerable.Range(0, 16).Select(i => new object[] { i });

    [Theory, MemberData(nameof(Rates))]
    public void AttackComparisonAndStepAreSeparateEvents(int rate)
    {
        var voice = new SidVoice();
        voice.Write(5, (byte)(rate << 4)); voice.Write(6, 0xf0); voice.Write(4, 1);
        Clock(voice, Periods[rate]);
        Assert.Equal(0, voice.EnvelopeCounter);
        Assert.True(voice.EnvelopeDebugState.RateResetPending);
        Clock(voice, 2);
        Assert.Equal(0, voice.EnvelopeCounter);
        Clock(voice, 1);
        Assert.Equal(1, voice.EnvelopeCounter);
        Clock(voice, Periods[rate] - 1);
        Assert.Equal(1, voice.EnvelopeCounter);
        Clock(voice, 1);
        Assert.Equal(2, voice.EnvelopeCounter);
    }

    [Fact]
    public void GatePipelineEnablesAttackOnSecondClockWithoutResettingRate()
    {
        var voice = new SidVoice();
        voice.Write(6, 0x0f); Clock(voice, 20); voice.Write(4, 1);
        Clock(voice, 1);
        Assert.Equal(3, voice.EnvelopeState);
        Assert.False(voice.EnvelopeDebugState.CounterEnabled);
        Clock(voice, 1);
        Assert.Equal(0, voice.EnvelopeState);
        Assert.True(voice.EnvelopeDebugState.CounterEnabled);
        Assert.Equal(22, voice.RateCounter);
    }

    [Fact]
    public void FasterRateWaitsForTheLfsrToReturnToItsComparator()
    {
        var voice = new SidVoice();
        voice.Write(5, 0xf0); voice.Write(4, 1); Clock(voice, 20); voice.Write(5, 0);
        // 32767 distinct LFSR states, comparison, reset, two step clocks.
        Clock(voice, 32767 + 9 + 2 - 20);
        Assert.Equal(0, voice.EnvelopeCounter);
        Clock(voice, 1);
        Assert.Equal(1, voice.EnvelopeCounter);
    }

    [Fact]
    public void ReleaseWritesCannotUnlockZero()
    {
        var voice = new SidVoice();
        for (var rate = 15; rate >= 0; --rate)
        {
            voice.Write(6, (byte)rate); Clock(voice, 32780);
            Assert.Equal(0, voice.EnvelopeCounter);
            Assert.False(voice.EnvelopeDebugState.CounterEnabled);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(7)] [InlineData(14)] [InlineData(15)]
    public void SustainStopsAtRepeatedNibbleAndReleaseReachesZero(int sustain)
    {
        var voice = new SidVoice();
        voice.Write(6, (byte)(sustain << 4)); voice.Write(4, 1); Clock(voice, 40000);
        Assert.Equal(sustain * 17, voice.EnvelopeCounter);
        var phase = voice.RateCounter; Clock(voice, 1);
        Assert.NotEqual(phase, voice.RateCounter);
        voice.Write(4, 0); Clock(voice, 40000);
        Assert.Equal(0, voice.EnvelopeCounter);
        Clock(voice, 40000);
        Assert.Equal(0, voice.EnvelopeCounter);
    }

    [Fact]
    public void LoweringSustainResumesDecayAndRaisingItNeverRaisesEnvelope()
    {
        var voice = new SidVoice();
        voice.Write(6, 0xe0); voice.Write(4, 1); Clock(voice, 10000);
        Assert.Equal(0xee, voice.EnvelopeCounter);
        voice.Write(6, 0xd0); Clock(voice, 10000);
        Assert.Equal(0xdd, voice.EnvelopeCounter);
        voice.Write(6, 0xf0); Clock(voice, 10000);
        Assert.True(voice.EnvelopeCounter < 0xdd);
    }

    [Fact]
    public void DividerIsLatchedAtThresholdsAndSurvivesRetrigger()
    {
        var voice = new SidVoice(); voice.Write(4, 1); Clock(voice, 2298);
        Assert.Equal(255, voice.EnvelopeCounter);
        Assert.Equal(0, voice.EnvelopeState);
        Clock(voice, 3);
        Assert.Equal(1, voice.EnvelopeState);
        foreach (var (level, period) in new[] { (0x5d, 2), (0x36, 4), (0x1a, 8), (0x0e, 16), (6, 30) })
        {
            for (var i = 0; voice.EnvelopeCounter > level && i < 40000; ++i) Clock(voice, 1);
            Assert.Equal(level, voice.EnvelopeCounter);
            Clock(voice, 1);
            Assert.Equal(period, voice.EnvelopeDebugState.DividerPeriod);
        }
        voice.Write(4, 0); Clock(voice, 3); voice.Write(4, 1);
        for (var i = 0; voice.EnvelopeCounter < 8 && i < 40000; ++i) Clock(voice, 1);
        Assert.Equal(8, voice.EnvelopeCounter);
        Assert.Equal(30, voice.EnvelopeDebugState.DividerPeriod);
    }

    [Fact]
    public void CompletedGateOnThenOffCanWrapZeroButOneClockGateCannot()
    {
        foreach (var gateClocks in new[] { 1, 2 })
        {
            var voice = new SidVoice(); voice.Write(4, 1); Clock(voice, gateClocks);
            voice.Write(4, 0); Clock(voice, 12 - gateClocks);
            Assert.Equal(gateClocks == 2 ? 255 : 0, voice.EnvelopeCounter);
        }
    }

    [Fact]
    public void EnvelopeReadLatchPrecedesTheStep()
    {
        var voice = new SidVoice(); voice.Write(4, 1); Clock(voice, 12);
        Assert.Equal(1, voice.EnvelopeCounter); Assert.Equal(0, voice.ReadEnvelope());
        Clock(voice, 1); Assert.Equal(1, voice.ReadEnvelope());
    }

    [Fact]
    public void RetriggerAtMaximumWrapsAndFreezesUntilAnotherGateTransition()
    {
        var voice = new SidVoice(); voice.Write(4, 1); Clock(voice, 2298);
        Assert.Equal(255, voice.EnvelopeCounter);
        voice.Write(4, 0); Clock(voice, 2); voice.Write(4, 1); Clock(voice, 7);
        Assert.Equal(0, voice.EnvelopeCounter);
        Assert.False(voice.EnvelopeDebugState.CounterEnabled);
        Clock(voice, 100);
        Assert.Equal(0, voice.EnvelopeCounter);
        voice.Write(4, 0); Clock(voice, 2); voice.Write(4, 1); Clock(voice, 30);
        Assert.True(voice.EnvelopeCounter > 0);
    }

    [Theory]
    [InlineData(1)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(2301)] [InlineData(2302)] [InlineData(2303)]
    public void CopyPreservesEveryPendingEnvelopeEvent(int cycles)
    {
        var source = new SidVoice(); source.Write(4, 1); Clock(source, cycles);
        var copy = new SidVoice(); copy.CopyStateFrom(source);
        for (var i = 0; i < 1000; ++i)
        {
            Clock(source, 1); Clock(copy, 1);
            Assert.Equal(source.EnvelopeDebugState, copy.EnvelopeDebugState);
        }
    }

    private static void Clock(SidVoice voice, int cycles)
    {
        for (var i = 0; i < cycles; ++i) voice.ClockEnvelope();
    }
}
