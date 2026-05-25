using AmigaTracker.Abstractions;
using AmigaTracker.ProTracker;

namespace AmigaTracker.ProTracker.Tests;

public sealed class ProTrackerAssemblyGuidedTests
{
    [Fact]
    public void FirstNewRowArrivesAfterPtCounterWrapAndTriggersPaulaState()
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(repeatLengthWords: 32));

        var trace = RenderUntilNewRow(song);
        var voice = trace.Voices[0];

        Assert.Equal(0, trace.SongPosition);
        Assert.Equal(0, trace.PatternIndex);
        Assert.Equal(0, trace.Row);
        Assert.Equal(0, trace.Counter);
        Assert.Equal(856, voice.Period);
        Assert.Equal(1, voice.SampleNumber);
        Assert.Equal(64, voice.Volume);
        Assert.True(voice.TriggeredThisTick);
        Assert.True(voice.IsAudible);
        Assert.InRange(voice.PaulaStartDelayFrames, 15, 25);
    }

    [Fact]
    public void FxxBelowThirtyTwoSetsSpeedAndClearsCounter()
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xF, parameter: 0x03));

        var trace = RenderUntilNewRow(song);

        Assert.Equal(3, trace.Speed);
        Assert.Equal(0, trace.Counter);
    }

    [Fact]
    public void FxxAtThirtyTwoOrAboveSetsCiaBpm()
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xF, parameter: 64));
        var before = song.GetCurrentTickFrameCount(AudioRenderOptions.Default);

        var trace = RenderUntilNewRow(song);
        var after = song.GetCurrentTickFrameCount(AudioRenderOptions.Default);

        Assert.Equal(64, trace.Bpm);
        Assert.True(after > before);
    }

    [Fact]
    public void F00EndsPlayback()
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xF, parameter: 0));
        song.LoopingEnabled = false;

        var trace = RenderUntilNewRow(song);

        Assert.True(trace.Ended);
    }

    [Fact]
    public void CxxSetsHexVolume()
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xC, parameter: 0x21));

        var voice = RenderUntilNewRow(song).Voices[0];

        Assert.Equal(0x21, voice.Volume);
    }

    [Fact]
    public void ChannelWaveformCapturesRawSampleDataBeforeVolumeScaling()
    {
        var sampleData = Enumerable.Range(0, 64)
            .Select(i => (byte)((i & 1) == 0 ? 0x80 : 0x7F))
            .ToArray();
        using var song = Load(ModFixtureBuilder.CreateProTracker31(
            sampleData: sampleData,
            volume: 8,
            repeatLengthWords: sampleData.Length / 2));
        song.ChannelWaveformCaptureEnabled = true;
        var options = new AudioRenderOptions(44100, 1);
        var rawPeak = 0.0f;
        var audioPeak = 0.0f;

        for (var i = 0; i < 16 && rawPeak <= 0.9f; i++)
        {
            var frames = song.GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            song.RenderTick(buffer, options);
            audioPeak = Math.Max(audioPeak, buffer.Max(sample => Math.Abs(sample)));

            var channel = Assert.Single(song.LastChannelWaveform!.Channels.Where(candidate => candidate.ChannelIndex == 0));
            rawPeak = Math.Max(rawPeak, channel.Samples.Max(sample => Math.Abs(sample)));
        }

        Assert.True(rawPeak > 0.9f);
        Assert.True(audioPeak < 0.05f);
    }

    [Fact]
    public void E5SetsFinetuneBeforePeriodLookup()
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xE, parameter: 0x51));

        var voice = RenderUntilNewRow(song).Voices[0];

        Assert.Equal(1, voice.FineTune);
        Assert.Equal(850, voice.Period);
    }

    [Fact]
    public void E00EnablesAndE01DisablesLedFilterState()
    {
        using var enableSong = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xE, parameter: 0x00));
        var enableHardware = Assert.IsAssignableFrom<IAmigaHardwareStateProvider>(enableSong);

        RenderUntilNewRow(enableSong);

        Assert.True(enableHardware.AmigaHardwareState.AudioFilterEnabled);

        using var disableSong = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xE, parameter: 0x01));
        var disableHardware = Assert.IsAssignableFrom<IAmigaHardwareStateProvider>(disableSong);

        RenderUntilNewRow(disableSong);

        Assert.False(disableHardware.AmigaHardwareState.AudioFilterEnabled);
    }

    [Fact]
    public void EDxDelaysNoteUntilRequestedTick()
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xE, parameter: 0xD2));

        var row = RenderUntilNewRow(song);
        var tick1 = RenderOneTick(song);
        var tick2 = RenderOneTick(song);

        Assert.False(row.Voices[0].TriggeredThisTick);
        Assert.False(tick1.Voices[0].TriggeredThisTick);
        Assert.True(tick2.Voices[0].TriggeredThisTick);
    }

    [Fact]
    public void BxxJumpsToRequestedSongPositionAfterCurrentRow()
    {
        var orders = new byte[128];
        orders[0] = 0;
        orders[1] = 1;
        var data = ModFixtureBuilder.CreateProTracker31(songLength: 2, orderTable: orders, effect: 0xB, parameter: 0x01, patternCount: 2);
        using var song = Load(data);

        RenderUntilNewRow(song);

        Assert.Equal(1, song.Position.Tracker!.Value.Order);
    }

    [Fact]
    public void DxxBreaksToBcdRowInNextPosition()
    {
        var orders = new byte[128];
        orders[0] = 0;
        orders[1] = 1;
        var data = ModFixtureBuilder.CreateProTracker31(songLength: 2, orderTable: orders, effect: 0xD, parameter: 0x12, patternCount: 2);
        using var song = Load(data);

        RenderUntilNewRow(song);

        var tracker = song.Position.Tracker!.Value;
        Assert.Equal(1, tracker.Order);
        Assert.Equal(12, tracker.Row);
    }

    [Fact]
    public void SampleOffsetAppliesToInitialPointerAndPersistsForZeroParameter()
    {
        var sampleData = Enumerable.Repeat((byte)0x40, 1024).ToArray();
        var data = ModFixtureBuilder.CreateProTracker31(sampleData: sampleData, effect: 0x9, parameter: 0x01, patternCount: 1);
        ModFixtureBuilder.WriteCell(data, ProTrackerConstants.ProTrackerHeaderLength, 0, 1, 0, 1, 856, 0x9, 0x00);
        using var song = Load(data);

        var first = RenderUntilNewRow(song).Voices[0];
        RenderRows(song, 1);
        var carried = song.LastTrace!.Voices[0];

        Assert.Equal(256, first.PaulaInitialSampleOffset);
        Assert.Equal(256, carried.PaulaInitialSampleOffset);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x20)]
    [InlineData(0x30)]
    [InlineData(0x40)]
    [InlineData(0x50)]
    [InlineData(0x60)]
    [InlineData(0x70)]
    [InlineData(0x80)]
    [InlineData(0x90)]
    [InlineData(0xA0)]
    [InlineData(0xB0)]
    [InlineData(0xC1)]
    [InlineData(0xD1)]
    [InlineData(0xE1)]
    [InlineData(0xF1)]
    public void EveryExtendedCommandIsHandledWithoutRejectingPlayback(int extendedParameter)
    {
        using var song = Load(ModFixtureBuilder.CreateProTracker31(effect: 0xE, parameter: extendedParameter, repeatLengthWords: 32));

        for (var i = 0; i < 16; i++)
        {
            RenderOneTick(song);
        }

        Assert.NotNull(song.LastTrace);
    }

    [Fact]
    public void EFxMutatesRuntimeSampleBufferForInvertLoop()
    {
        var sampleData = Enumerable.Repeat((byte)0x40, 64).ToArray();
        var data = ModFixtureBuilder.CreateProTracker31(
            sampleData: sampleData,
            effect: 0xE,
            parameter: 0xF1,
            repeatOffsetWords: 1,
            repeatLengthWords: 16);
        using var song = Load(data);
        var before = song.Module.SampleArea[3];

        for (var i = 0; i < 80; i++)
        {
            RenderOneTick(song);
        }

        Assert.NotEqual(before, song.Module.SampleArea[3]);
    }

    [Fact]
    public void ReplayAllowsLoopHeaderToReachBeyondDeclaredSampleLength()
    {
        var sampleData = Enumerable.Repeat((byte)0x40, 1024).ToArray();
        var data = ModFixtureBuilder.CreateProTracker31(
            sampleData: sampleData,
            repeatOffsetWords: 244,
            repeatLengthWords: 18);
        ModFixtureBuilder.WriteSample(data, 0, "short loop", lengthWords: 6, fineTune: 0, volume: 64, repeatOffsetUnits: 244, repeatLengthUnits: 18);
        using var song = Load(data);

        var trace = RenderUntilNewRow(song);
        for (var i = 0; i < 32; i++)
        {
            RenderOneTick(song);
        }

        Assert.Equal(488, trace.Voices[0].PaulaReloadSampleOffset);
        Assert.True(song.LastTrace!.Voices[0].IsAudible);
    }

    [Fact]
    public void MultipleChannelsSumLouderThanSingleChannel()
    {
        var oneVoice = ModFixtureBuilder.CreateProTracker31(repeatLengthWords: 32);
        var fourVoice = ModFixtureBuilder.CreateProTracker31(repeatLengthWords: 32);
        for (var channel = 1; channel < 4; channel++)
        {
            ModFixtureBuilder.WriteCell(fourVoice, ProTrackerConstants.ProTrackerHeaderLength, 0, 0, channel, 1, 856, 0, 0);
        }

        using var oneSong = Load(oneVoice);
        using var fourSong = Load(fourVoice);

        var oneEnergy = RenderUntilEnergy(oneSong, 8);
        var fourEnergy = RenderUntilEnergy(fourSong, 8);

        Assert.True(fourEnergy > oneEnergy * 2.5);
    }

    [Fact]
    public void FailrightFixtureRendersFiniteNonzeroPcmAndActivatesAllVoices()
    {
        var data = File.ReadAllBytes(ProTrackerParserTests.FindWorkspaceFile(Path.Combine("TestTunes", "ProTracker", "failright.mod")));
        using var song = Load(data);
        var options = AudioRenderOptions.Default;
        var heard = new bool[4];
        var energy = 0.0;

        for (var i = 0; i < 360; i++)
        {
            var frames = song.GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            song.RenderTick(buffer, options);
            for (var sample = 0; sample < buffer.Length; sample++)
            {
                Assert.False(float.IsNaN(buffer[sample]) || float.IsInfinity(buffer[sample]));
                energy += Math.Abs(buffer[sample]);
            }

            var trace = song.LastTrace;
            if (trace != null)
            {
                for (var channel = 0; channel < heard.Length; channel++)
                {
                    heard[channel] |= trace.Voices[channel].IsAudible && trace.Voices[channel].SampleStep > 0.0;
                }
            }
        }

        Assert.True(energy > 0.01);
        Assert.True(song.Position.Time > TimeSpan.Zero);
        Assert.All(heard, Assert.True);
    }

    private static ProTrackerSong Load(byte[] data)
    {
        return Assert.IsType<ProTrackerSong>(new ProTrackerFormat().Load(data));
    }

    private static ProTrackerTickTrace RenderUntilNewRow(ProTrackerSong song)
    {
        for (var i = 0; i < 16; i++)
        {
            var trace = RenderOneTick(song);
            if (trace.NewRowProcessed)
            {
                return trace;
            }
        }

        throw new InvalidOperationException("Expected a new row within 16 ticks.");
    }

    private static void RenderRows(ProTrackerSong song, int rows)
    {
        var rendered = 0;
        while (rendered < rows)
        {
            if (RenderOneTick(song).NewRowProcessed)
            {
                rendered++;
            }
        }
    }

    private static ProTrackerTickTrace RenderOneTick(ProTrackerSong song)
    {
        var options = AudioRenderOptions.Default;
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];
        song.RenderTick(buffer, options);
        return song.LastTrace ?? throw new InvalidOperationException("Expected trace.");
    }

    private static double RenderUntilEnergy(ProTrackerSong song, int ticks)
    {
        var options = new AudioRenderOptions(44100, 1);
        var energy = 0.0;
        for (var i = 0; i < ticks; i++)
        {
            var frames = song.GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            song.RenderTick(buffer, options);
            for (var sample = 0; sample < buffer.Length; sample++)
            {
                energy += Math.Abs(buffer[sample]);
            }
        }

        return energy;
    }
}
