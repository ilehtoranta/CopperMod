using CopperMod.Sid;

public sealed class TimingAndNumericsTests
{
    [Fact]
    public void AWriteAfterAReadAtTheSameTimestampStillDrivesTheBus()
    {
        var sid = new SidSystem([new SidChipPlacement(0, 0xd400)], SidChipModel.Mos6581);
        sid.TryRead(0xd419, 20, out _);
        sid.TryWrite(0xd400, 0xa5, 20);
        sid.TryRead(0xd400, 20, out var value);
        Assert.Equal(0xa5, value);
        sid.TryRead(0xd41b, 20, out _);
        sid.TryWrite(0xd401, 0x80, 20);
        sid.TryRead(0xd41b, 20, out _);
        sid.TryRead(0xd41b, 21, out _);
        Assert.Equal(0x80a5u, sid.GetRegisterChipDebugState(0).Voices[0].Accumulator);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BusReadsSurviveDigitalReplayAndEveryCompactionBoundary(bool traced)
    {
        var sid = new SidSystem([new SidChipPlacement(0, 0xd400)], SidChipModel.Mos6581);
        if (traced) sid.Trace = new SidCycleTrace();
        for (var group = 0; group < 4; ++group)
        {
            var start = group * 100;
            for (var i = 1; i <= 64; ++i) sid.TryWrite(0xd400, 0x33, start + i);
            sid.TryRead(0xd419, start + 65, out var pot);
            Assert.Equal(255, pot);
            // Digital catch-up must not replay old writes onto the bus.
            sid.TryRead(0xd41b, start + 65, out var osc);
            sid.TryRead(0xd400, start + 65, out var bus);
            Assert.Equal(osc, bus);
            sid.TryRead(0xd419, start + 66, out _);
            sid.AdvanceTo(start + 66);
            sid.TryRead(0xd400, start + 66, out bus);
            Assert.Equal(255, bus);
        }
    }

    [Theory]
    [InlineData((int)SidChipModel.Mos6581)] [InlineData((int)SidChipModel.Mos8580)]
    public void ShortTestPreservesHistoryAtBothNoiseShiftPhases(int modelValue)
    {
        var model = (SidChipModel)modelValue;
        foreach (var cycle in new[] { 15, 16, 17, 18, 19 })
        {
            var chip = new SidChip(model, 0xd400);
            chip.Write(1, 0x80); chip.Write(4, 0x80); chip.Render(cycle);
            var noise = chip.DebugState.Voices[0].NoiseShiftRegister;
            chip.Write(4, 0x88); chip.Render(1);
            Assert.Equal(noise, chip.DebugState.Voices[0].NoiseShiftRegister);
            chip.Write(4, 0x80); chip.Render(1);
            var released = ((noise << 1) | ((~noise >> 17) & 1)) & 0x7fffff;
            Assert.Equal(released, chip.DebugState.Voices[0].NoiseShiftRegister);
        }
    }

    [Theory]
    [InlineData((int)SidChipModel.Mos6581, 1)] [InlineData((int)SidChipModel.Mos8580, 0)]
    public void Osc3SawDelayIsChipSpecific(int modelValue, int expected)
    {
        var model = (SidChipModel)modelValue;
        var chip = new SidChip(model, 0xd400);
        chip.Write(0x0f, 0x80); chip.Write(0x12, 0x20); chip.Render(2);
        Assert.Equal(expected, chip.Read(0x1b));
    }

    [Fact]
    public void Mos8580DelayedSawUsesCurrentPulseGate()
    {
        var chip = new SidChip(SidChipModel.Mos8580, 0xd400);
        chip.Write(0x0f, 0x80); chip.Write(0x12, 0x60); chip.Render(10);
        Assert.NotEqual(0, chip.Read(0x1b));
        chip.Write(0x10, 255); chip.Write(0x11, 15); chip.Render(2);
        Assert.Equal(0, chip.Read(0x1b));
    }

    [Theory]
    [InlineData(SidEmulationProfile.Balanced)] [InlineData(SidEmulationProfile.ReferenceMeasured)]
    public void RingCannotChangeAnySawSelectedWaveform(SidEmulationProfile profile)
    {
        foreach (var model in new[] { SidChipModel.Mos6581, SidChipModel.Mos8580 })
        foreach (var selector in new[] { 0x20, 0x30, 0x60, 0x70, 0xa0, 0xb0, 0xe0, 0xf0 })
        {
            var a = new SidChip(model, 0xd400, sidEmulationProfile: profile);
            var b = new SidChip(model, 0xd400, sidEmulationProfile: profile);
            a.Write(0, 255); a.Write(1, 255); a.Write(4, (byte)selector);
            b.Write(0, 255); b.Write(1, 255); b.Write(4, (byte)(selector | 4));
            for (var cycle = 0; cycle < 520; ++cycle) Assert.Equal(a.Render(1), b.Render(1));
            Assert.Equal(a.DebugState.Voices[0].Accumulator, b.DebugState.Voices[0].Accumulator);
        }
    }

    [Theory]
    [InlineData(44100)] [InlineData(48000)] [InlineData(96000)]
    public void RationalSamplePhaseIsExactAcrossLongPlaybackAndChunks(int rate)
    {
        var clock = new SidSampleClock(SidConstants.PalCpuCyclesPerSecond, rate);
        foreach (var sampleIndex in new[] { 1L, 100000L, 1000000000000L })
        {
            var cycle = clock.GetSampleTargetCycle(sampleIndex);
            var remainder = (Int128)sampleIndex * SidConstants.PalCpuCyclesPerSecond - (Int128)cycle * rate;
            Assert.Equal((double)remainder / rate, clock.GetSampleFractionalCycle(sampleIndex));
            Assert.InRange(clock.GetSampleFractionalCycle(sampleIndex), -0.5, 0.5);
        }
        Span<double> phases = stackalloc double[7];
        for (var chunk = 0; chunk < 100; ++chunk)
        {
            clock.FillSampleFractions(phases);
            for (var i = 0; i < phases.Length; ++i)
                Assert.Equal(clock.GetSampleFractionalCycle(chunk * 7L + i + 1), phases[i]);
            clock.AdvanceFrames(7);
        }
    }

    [Fact]
    public void FractionalResamplingAndDigitalClockAllocateNoMemoryAfterConfiguration()
    {
        var filter = new SidWindowedSincResampler(985248, 48000);
        var chip = new SidChip(SidChipModel.Mos6581, 0xd400);
        chip.Write(4, 0x21);
        Run(); // JIT and warm up
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run()
        {
            for (var i = 0; i < 2000; ++i)
            {
                filter.Push(chip.Render(1));
                if (i % 20 == 0) _ = filter.Read((i % 7) / 16.0 - 0.25);
            }
        }
    }

    [Fact]
    public void VcrInterpolationTracksTheContinuousCircuitEquation()
    {
        var model = SidFilterProfileDefinition.Resolve(SidChipModel.Mos6581, SidFilterProfileId.Mos6581Balanced).Analog6581Model!;
        double maximumError = 0;
        var worst = "";
        for (var cutoff = 0; cutoff < 2048; cutoff += 17)
        for (var i = 0; i <= 1024; ++i)
        {
            var delta = i * 1.8 / 1024;
            var error = Math.Abs(model.MapVcrConductanceScale(cutoff, delta) - model.EvaluateVcrConductanceScale(cutoff, delta));
            if (error > maximumError) { maximumError = error; worst = $"cutoff={cutoff}, delta={delta:R}"; }
        }
        Assert.True(maximumError < 0.0001, $"VCR lookup error {maximumError:R}, {worst}");
    }
}
