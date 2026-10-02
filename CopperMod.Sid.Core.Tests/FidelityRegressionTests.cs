using CopperMod.Sid;
using Xunit.Abstractions;

// Regression cases from docs/SID-6581-fidelity-audit-2026-09-26.md.
// Source comparisons are not hardware certification.
public sealed class FidelityRegressionTests(ITestOutputHelper output)
{
    static SidChip Chip() => new(SidChipModel.Mos6581, 0xD400,
        sidEmulationProfile: SidEmulationProfile.ReferenceMeasured);

    [Fact]
    public void ReleaseRateChangeAloneMustNotUnlockZeroEnvelope()
    {
        var chip = Chip();
        chip.Reset();
        chip.Write(0x06, 0x0F);
        chip.Render(20);
        Assert.Equal(0, chip.DebugState.Voices[0].EnvelopeCounter);
        chip.Write(0x06, 0x00);
        chip.Render(32757);
        output.WriteLine($"Envelope after release-only write: {chip.DebugState.Voices[0].EnvelopeCounter:X2}");
        Assert.Equal(0, chip.DebugState.Voices[0].EnvelopeCounter);
    }

    [Fact]
    public void ShortTestPulseMustPreserveNoiseHistoryOnAssertion()
    {
        var chip = Chip();
        chip.Reset();
        chip.Write(0x00, 0xFF);
        chip.Write(0x01, 0xFF);
        chip.Write(0x04, 0x80);
        chip.Render(100);
        var before = chip.DebugState.Voices[0].NoiseShiftRegister;
        Assert.NotEqual(0x7FFFFEu, before);
        chip.Write(0x04, 0x88);
        chip.Render(1);
        var after = chip.DebugState.Voices[0].NoiseShiftRegister;
        output.WriteLine($"Noise before TEST={before:X6}; after one cycle={after:X6}");
        Assert.Equal(before, after);
    }

    [Fact]
    public void RingBitMustNotZeroSawPulseWithoutTriangle()
    {
        var plain = Chip();
        var ring = Chip();
        foreach (var chip in new[] { plain, ring })
        {
            chip.Reset();
            chip.Trace = new SidCycleTrace();
            chip.Write(0x00, 0xFF);
            chip.Write(0x01, 0xFF);
            chip.Write(0x04, chip == plain ? (byte)0x60 : (byte)0x64);
            chip.Render(600);
        }
        var a = plain.Trace!.Frames.Where(f => f.VoiceIndex == 0).ToArray();
        var b = ring.Trace!.Frames.Where(f => f.VoiceIndex == 0).ToArray();
        var mismatch = Enumerable.Range(0, a.Length).FirstOrDefault(i => a[i].WaveformDac != b[i].WaveformDac, -1);
        if (mismatch >= 0)
            output.WriteLine($"Cycle {a[mismatch].Cycle}: plain DAC={a[mismatch].WaveformDac:X3}, ring DAC={b[mismatch].WaveformDac:X3}");
        Assert.Equal(-1, mismatch);
    }

    [Fact]
    public void SameStaticPulseDacMustNotChangePolarityWithPulseWidth()
    {
        SidCycleTraceFrame StaticPulse(ushort width)
        {
            var chip = Chip();
            chip.Reset();
            chip.Trace = new SidCycleTrace();
            chip.Write(0x04, 0x48); // Hold accumulator at zero, then release TEST.
            chip.Render(1);
            chip.Write(0x02, (byte)width);
            chip.Write(0x03, (byte)(width >> 8));
            chip.Write(0x04, 0x40); // FREQ=0, both widths produce indefinitely low pulse DAC.
            chip.Render(100);
            return chip.Trace.Frames.Last(f => f.VoiceIndex == 0);
        }
        var narrow = StaticPulse(1);
        var wide = StaticPulse(0x800);
        Assert.Equal(0u, narrow.WaveformDac);
        Assert.Equal(narrow.WaveformDac, wide.WaveformDac);
        output.WriteLine($"PW=1 output={narrow.WaveformOutput:R}; PW=0x800 output={wide.WaveformOutput:R}");
        Assert.Equal(wide.WaveformOutput, narrow.WaveformOutput, precision: 12);
    }

    [Fact]
    public void WriteOnlyReadsMustNotKeepOpenBusAliveIndefinitely()
    {
        var chip = Chip();
        chip.Write(0, 0xA5, 0);
        byte value = 0;
        for (var cycle = 1000; cycle <= 100000; cycle += 1000)
            value = chip.Read(0, cycle);
        output.WriteLine($"Bus after 100 reads over 100000 cycles={value:X2}");
        Assert.Equal(0, value);
    }

    [Fact]
    public void RenderingMustNotUndoEarlierPotReadBusValue()
    {
        var sid = new SidSystem(new[] { new SidChipPlacement(0, 0xD400) }, SidChipModel.Mos6581,
            sidEmulationProfile: SidEmulationProfile.ReferenceMeasured);
        sid.Reset();
        for (var cycle = 1; cycle <= 64; cycle++)
            Assert.True(sid.TryWrite(0xD400, 0x33, cycle));
        Assert.True(sid.TryRead(0xD419, 65, out var pot));
        Assert.Equal(0xFF, pot);
        sid.AdvanceTo(65);
        Assert.True(sid.TryRead(0xD400, 65, out var bus));
        output.WriteLine($"POT read={pot:X2}; bus after rendering same cycle={bus:X2}");
        Assert.Equal(pot, bus);
    }

    [Fact]
    public void Mos6581Osc3ShouldUseCurrentWaveformRatherThan8580Delay()
    {
        var chip = Chip();
        chip.Reset();
        chip.Write(0x12, 0x28);
        chip.Render(1);
        chip.Write(0x0E, 0x00);
        chip.Write(0x0F, 0x80);
        chip.Write(0x12, 0x20);
        chip.Render(2);
        var phase = chip.DebugState.Voices[2].Accumulator;
        var osc3 = chip.Read(0x1B);
        output.WriteLine($"Phase={phase:X6}; expected current saw high byte={phase >> 16:X2}; OSC3={osc3:X2}");
        Assert.Equal((byte)(phase >> 16), osc3);
    }

    [Theory]
    [InlineData(44100, SidConstants.PalCpuCyclesPerSecond)]
    [InlineData(48000, SidConstants.PalCpuCyclesPerSecond)]
    [InlineData(48000, 960000)] // Integer-ratio control: exact 20 input cycles per output sample.
    public void ResamplingPureInBandToneShouldNotAddLargeTimingDistortion(int sampleRate, int clock)
    {
        const double frequency = 10000;
        var resampler = new SidWindowedSincResampler(clock, sampleRate);
        var targets = new SidSampleClock(clock, sampleRate);
        var warmup = sampleRate / 20;
        var count = sampleRate / 10; // Exactly 1000 cycles of the test tone.
        var samples = new double[count];
        long cycle = 0;
        double sinFit = 0, cosFit = 0;
        for (var n = 1; n <= warmup + count; n++)
        {
            var target = targets.GetSampleTargetCycle(n);
            while (cycle < target)
                resampler.Push(Math.Sin(2 * Math.PI * frequency * ++cycle / clock));
            var sample = resampler.Read(targets.GetSampleFractionalCycle(n));
            if (n <= warmup) continue;
            samples[n - warmup - 1] = sample;
            var angle = 2 * Math.PI * frequency * n / sampleRate;
            sinFit += sample * Math.Sin(angle) * 2 / count;
            cosFit += sample * Math.Cos(angle) * 2 / count;
        }
        double residualPower = 0;
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * frequency * (warmup + i + 1) / sampleRate;
            var error = samples[i] - sinFit * Math.Sin(angle) - cosFit * Math.Cos(angle);
            residualPower += error * error / count;
        }
        var relativeResidual = Math.Sqrt(residualPower / ((sinFit * sinFit + cosFit * cosFit) / 2));
        output.WriteLine($"{clock} Hz clock, {sampleRate} Hz output, 10 kHz input: relative residual={relativeResidual:R}, {20 * Math.Log10(relativeResidual):F2} dB after removing fitted gain and phase");
        // -80 dB is an audit quality target, not a SID hardware specification.
        Assert.True(relativeResidual < 0.0001, "In-band timing distortion exceeds the audit target of -80 dB.");
    }
}
