using System;
using CopperMod.Abstractions;
using CopperMod.Med;

namespace CopperMod.Med.Tests;

public sealed class AssemblyGuidedAuditTests
{
    [Fact]
    public void Mmd0TraceCapturesNoteInstrumentAndPaulaRegisterState()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 1,
            instrument: 17,
            repeatLength: 64)));

        var trace = RenderOneTick(song);
        var voice = trace.Voices[0];

        Assert.Equal(0, trace.BlockIndex);
        Assert.Equal(0, trace.Row);
        Assert.Equal(0, trace.Tick);
        Assert.Equal(1, voice.Note);
        Assert.Equal(17, voice.InstrumentNumber);
        Assert.Equal(MmdInstrumentKind.Sample, voice.InstrumentKind);
        Assert.Equal(856, voice.Period);
        Assert.Equal(64, voice.Volume);
        Assert.Equal(64, voice.SampleLength);
        Assert.Equal(0.0, voice.SamplePosition, precision: 6);
        Assert.True(voice.LoopEnabled);
        Assert.Equal(0, voice.LoopStart);
        Assert.Equal(64, voice.LoopEnd);
        Assert.Equal(64, voice.LoopLength);
        Assert.Equal(-1, voice.HoldTicks);
        Assert.True(voice.SampleStep > 0.0);
        Assert.True(voice.IsAudible);
        Assert.InRange(voice.PaulaStartDelayFrames, 1, 16);
    }

    [Fact]
    public void PaulaTraceUsesChipZeroReloadForNonLoopedSamples()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample()));

        var voice = RenderOneTick(song).Voices[0];

        Assert.False(voice.LoopEnabled);
        Assert.Equal(0, voice.PaulaInitialSampleOffset);
        Assert.Equal(64, voice.PaulaInitialSampleLength);
        Assert.Equal(-1, voice.PaulaReloadSampleOffset);
        Assert.Equal(2, voice.PaulaReloadSampleLength);
        Assert.True(voice.PaulaReloadsSilence);
    }

    [Fact]
    public void PaulaTraceReloadsRepeatWindowAfterInitialLoopEnd()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            repeatOffset: 16,
            repeatLength: 16)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.True(voice.LoopEnabled);
        Assert.Equal(0, voice.PaulaInitialSampleOffset);
        Assert.Equal(32, voice.PaulaInitialSampleLength);
        Assert.Equal(16, voice.PaulaReloadSampleOffset);
        Assert.Equal(16, voice.PaulaReloadSampleLength);
        Assert.False(voice.PaulaReloadsSilence);
    }

    [Fact]
    public void PaulaTraceAppliesValidSampleOffsetToInitialPointer()
    {
        var sampleData = new byte[512];
        Array.Fill(sampleData, (byte)0x40);
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd1SampleCommand(
            command: 0x19,
            commandData: 0x01,
            sampleData: sampleData)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.Equal(256.0, voice.SamplePosition, precision: 6);
        Assert.Equal(256, voice.PaulaInitialSampleOffset);
        Assert.Equal(256, voice.PaulaInitialSampleLength);
        Assert.True(voice.PaulaReloadsSilence);
    }

    [Fact]
    public void PaulaTraceRevertsOverflowingSampleOffsetToSampleStart()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd1SampleCommand(
            command: 0x19,
            commandData: 0x01)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.Equal(0.0, voice.SamplePosition, precision: 6);
        Assert.Equal(0, voice.PaulaInitialSampleOffset);
        Assert.Equal(64, voice.PaulaInitialSampleLength);
    }

    [Fact]
    public void SampleOffsetPersistsUntilNextInstrumentNumberClearsIt()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd1SampleOffsetCarryFixture()));

        var first = RenderOneTick(song).Voices[0];
        var carried = RenderOneTick(song).Voices[0];
        _ = RenderOneTick(song);
        var afterInstrumentChange = RenderOneTick(song).Voices[0];

        Assert.Equal(1, first.InstrumentNumber);
        Assert.Equal(256, first.PaulaInitialSampleOffset);
        Assert.Equal(1, carried.InstrumentNumber);
        Assert.Equal(256, carried.PaulaInitialSampleOffset);
        Assert.Equal(2, afterInstrumentChange.InstrumentNumber);
        Assert.Equal(0, afterInstrumentChange.PaulaInitialSampleOffset);
    }

    [Fact]
    public void PlayNoteTraceMatchesHighMmd0NotePeriodWrap()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 63,
            repeatLength: 64)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.Equal(62, voice.NormalizedNoteIndex);
        Assert.Equal(62, voice.PeriodTableIndex);
        Assert.Equal(190, voice.Period);
    }

    [Fact]
    public void PlayNoteTraceOctaveRaisesTooLowTransposedNotes()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 1,
            instrumentTranspose: -1,
            repeatLength: 64)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.Equal(11, voice.NormalizedNoteIndex);
        Assert.Equal(453, voice.Period);
    }

    [Fact]
    public void PlayNoteTraceAppliesGlobalPlayTranspose()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 25,
            playTranspose: -12,
            repeatLength: 64)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.Equal(12, voice.NormalizedNoteIndex);
        Assert.Equal(428, voice.Period);
    }

    [Fact]
    public void IffFiveOctaveSampleSelectsLowOctaveWindowAndPeriod()
    {
        var sampleData = new byte[62];
        for (var i = 0; i < sampleData.Length; i++)
        {
            sampleData[i] = 0x40;
        }

        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 1,
            sampleType: 1,
            sampleData: sampleData)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.Equal(0, voice.NormalizedNoteIndex);
        Assert.Equal(12, voice.PeriodTableIndex);
        Assert.Equal(428, voice.Period);
        Assert.Equal(30, voice.SampleWindowOffset);
        Assert.Equal(32, voice.SampleLength);
    }

    [Fact]
    public void ExtSampleUsesLowerPeriodTable()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 1,
            sampleType: 7,
            repeatLength: 64)));

        var voice = RenderOneTick(song).Voices[0];

        Assert.True(voice.UsesExtendedPeriodTable);
        Assert.Equal(0, voice.PeriodTableIndex);
        Assert.Equal(3424, voice.Period);
    }

    [Fact]
    public void Mmd0FourTrackFixtureActivatesAllFourVoices()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0FourVoiceSampleChord()));

        var trace = RenderOneTick(song);

        Assert.True(trace.Voices.Count >= 4);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(i, trace.Voices[i].TrackIndex);
            Assert.Equal(i + 1, trace.Voices[i].InstrumentNumber);
            Assert.True(trace.Voices[i].IsAudible);
            Assert.True(trace.Voices[i].SampleStep > 0.0);
        }
    }

    [Fact]
    public void PureSynthUsesLowerPeriodTable()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Synth()));

        var voice = RenderOneTick(song).Voices[0];

        Assert.Equal(MmdInstrumentKind.Synth, voice.InstrumentKind);
        Assert.True(voice.UsesExtendedPeriodTable);
        Assert.Equal(24, voice.PeriodTableIndex);
        Assert.Equal(856, voice.Period);
    }

    [Fact]
    public void SynthWaveformSlideDownAccumulatesPeriodChange()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthPeriodSlide()));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];
        var tick2 = RenderOneTick(song).Voices[0];
        var tick3 = RenderOneTick(song).Voices[0];

        Assert.Equal(856, tick0.Period);
        Assert.Equal(856, tick1.Period);
        Assert.Equal(4, tick1.SynthPeriodChangeSpeed);
        Assert.Equal(860, tick2.Period);
        Assert.Equal(4, tick2.SynthPeriodChange);
        Assert.Equal(864, tick3.Period);
        Assert.Equal(8, tick3.SynthPeriodChange);
    }

    [Fact]
    public void SynthWaveformVibratoModulatesPeriod()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthVibrato()));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];

        Assert.Equal(856, tick0.Period);
        Assert.Equal(64, tick0.SynthVibratoDepth);
        Assert.Equal(16, tick0.SynthVibratoSpeed);
        Assert.Equal(862, tick1.Period);
    }

    [Fact]
    public void SynthVolumeEn1ConsumesWaveformBytesOnFollowingExecutions()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthEnvelopeOneShot()));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];
        var tick2 = RenderOneTick(song).Voices[0];
        var tick3 = RenderOneTick(song).Voices[0];

        Assert.Equal(64, tick0.SynthVolume);
        Assert.Equal(0, tick0.SynthEnvelopeWaveformIndex);
        Assert.False(tick0.SynthEnvelopeRestartEnabled);

        Assert.Equal(0, tick1.SynthVolume);
        Assert.Equal(1, tick1.SynthEnvelopePosition);
        Assert.Equal(32, tick2.SynthVolume);
        Assert.Equal(63, tick3.SynthVolume);
    }

    [Fact]
    public void SynthVolumeEn2RestartsAfterOneHundredTwentyEightEnvelopeBytes()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthEnvelopeLoop()));

        _ = RenderOneTick(song);
        MmdVoiceTrace voice = null!;
        for (var i = 0; i < 128; i++)
        {
            voice = RenderOneTick(song).Voices[0];
        }

        Assert.Equal(63, voice.SynthVolume);
        Assert.Equal(0, voice.SynthEnvelopePosition);
        Assert.True(voice.SynthEnvelopeRestartEnabled);

        var restarted = RenderOneTick(song).Voices[0];
        Assert.Equal(0, restarted.SynthVolume);
        Assert.Equal(1, restarted.SynthEnvelopePosition);
    }

    [Fact]
    public void SynthVolumeEstStopsEnvelopeAndContinuesVolumeStream()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthEnvelopeStop()));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];

        Assert.Equal(40, tick0.SynthVolume);
        Assert.Null(tick0.SynthEnvelopeWaveformIndex);
        Assert.False(tick0.SynthEnvelopeRestartEnabled);
        Assert.Equal(40, tick1.SynthVolume);
    }

    [Fact]
    public void SynthWaveformArpCyclesOffsetsUntilAre()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthArpeggio()));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];
        var tick2 = RenderOneTick(song).Voices[0];
        var tick3 = RenderOneTick(song).Voices[0];

        Assert.Equal(0, tick0.SynthArpeggioOffset);
        Assert.Equal(856, tick0.Period);
        Assert.Equal(4, tick1.SynthArpeggioOffset);
        Assert.Equal(678, tick1.Period);
        Assert.Equal(7, tick2.SynthArpeggioOffset);
        Assert.Equal(570, tick2.Period);
        Assert.Equal(0, tick3.SynthArpeggioOffset);
        Assert.Equal(856, tick3.Period);
    }

    [Fact]
    public void SynthWaveformSpeedDelaysOnlyAfterFirstExecution()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthWaveformSpeedDelay()));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];
        var tick2 = RenderOneTick(song).Voices[0];

        Assert.Equal(1, tick0.SynthWaveformIndex);
        Assert.True(tick0.IsAudible);
        Assert.True(tick0.PaulaPointerUpdatedThisTick);
        Assert.Equal(1, tick1.SynthWaveformIndex);
        Assert.True(tick1.IsAudible);
        Assert.False(tick1.PaulaPointerUpdatedThisTick);
        Assert.Equal(1, tick2.SynthWaveformIndex);
        Assert.True(tick2.IsAudible);
        Assert.False(tick2.PaulaPointerUpdatedThisTick);
    }

    [Fact]
    public void PureSynthAfterHybridDoesNotReuseHybridSampleDma()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0HybridToDelayedSynth()));

        var hybrid = RenderOneTick(song).Voices[0];
        var synthStart = RenderOneTick(song).Voices[0];

        Assert.Equal(MmdInstrumentKind.Hybrid, hybrid.InstrumentKind);
        Assert.True(hybrid.IsAudible);
        Assert.Equal(MmdInstrumentKind.Synth, synthStart.InstrumentKind);
        Assert.Equal(1, synthStart.SynthWaveformIndex);
        Assert.Equal(128, synthStart.SampleLength);
        Assert.True(synthStart.IsAudible);
        Assert.True(synthStart.PaulaPointerUpdatedThisTick);
    }

    [Fact]
    public void MixerUsesPaulaStyleSampleHoldByDefault()
    {
        var sampleData = new byte[64];
        sampleData[0] = 0x80;
        Array.Fill(sampleData, (byte)0x7F, 1, sampleData.Length - 1);
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 1,
            sampleData: sampleData,
            repeatLength: sampleData.Length)));

        var options = new AudioRenderOptions(44100, 1);
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];
        song.RenderTick(buffer, options);
        var delayFrames = song.LastTrace!.Voices[0].PaulaStartDelayFrames;

        Assert.InRange(delayFrames, 1, 16);
        for (var i = delayFrames; i < delayFrames + 8; i++)
        {
            Assert.Equal(-0.5f, buffer[i], precision: 6);
        }
    }

    [Fact]
    public void LoopedSamplePlaysInitialBodyBeforeNonZeroRepeatWindow()
    {
        var sampleData = new byte[64];
        sampleData[0] = 0x7F;
        Array.Fill(sampleData, (byte)0x40, 1, 15);
        Array.Fill(sampleData, (byte)0x80, 16, 16);
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 1,
            sampleData: sampleData,
            repeatOffset: 16,
            repeatLength: 16)));

        var options = new AudioRenderOptions(44100, 1);
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];
        song.RenderTick(buffer, options);
        var delayFrames = song.LastTrace!.Voices[0].PaulaStartDelayFrames;

        Assert.True(song.LastTrace.Voices[0].SamplePosition < 16.0);
        Assert.InRange(buffer[delayFrames], 0.49f, 0.50f);
    }

    [Fact]
    public void SynthWaveformSequenceUpdatesPaulaReloadWithoutRestartingCurrentDma()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthWaveformSwitch()));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];

        Assert.Equal(MmdInstrumentKind.Synth, tick0.InstrumentKind);
        Assert.Equal(0, tick0.SynthWaveformIndex);
        Assert.True(tick0.PaulaPointerUpdatedThisTick);
        Assert.Equal(2048, tick0.PaulaInitialSampleLength);

        Assert.Equal(1, tick1.SynthWaveformIndex);
        Assert.True(tick1.PaulaPointerUpdatedThisTick);
        Assert.True(tick1.SamplePosition > 0.0);
        Assert.Equal(2048, tick1.PaulaInitialSampleLength);
        Assert.Equal(2048, tick1.PaulaReloadSampleLength);
    }

    [Fact]
    public void PureSynthRetriggerPreservesCurrentDmaSamplePosition()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0SynthRetrigger()));

        var first = RenderOneTick(song).Voices[0];
        var retriggered = RenderOneTick(song).Voices[0];

        Assert.Equal(MmdInstrumentKind.Synth, first.InstrumentKind);
        Assert.Equal(MmdInstrumentKind.Synth, retriggered.InstrumentKind);
        Assert.True(retriggered.SamplePosition > 0.0);
        Assert.Equal(0, retriggered.PaulaStartDelayFrames);
    }

    [Fact]
    public void Mmd0TraceFloorsStandardPaulaSampleLengthToWords()
    {
        var sampleData = new byte[] { 0x40, 0x40, 0x40, 0x40, 0x7F };
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(sampleData: sampleData)));

        var trace = RenderOneTick(song);
        var voice = trace.Voices[0];

        Assert.Equal((uint)sampleData.Length, song.Module.Instruments[0].Length);
        Assert.Equal(4, voice.SampleLength);
        Assert.False(voice.LoopEnabled);
    }

    [Fact]
    public void Mmd0TraceCapturesRepeatWindowBeforeSampleTail()
    {
        var sampleData = new byte[64];
        Array.Fill(sampleData, (byte)0x40, 0, 32);
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            sampleData: sampleData,
            repeatOffset: 16,
            repeatLength: 16)));

        var trace = RenderOneTick(song);
        var voice = trace.Voices[0];

        Assert.True(voice.LoopEnabled);
        Assert.Equal(64, voice.SampleLength);
        Assert.Equal(16, voice.LoopStart);
        Assert.Equal(32, voice.LoopEnd);
        Assert.Equal(16, voice.LoopLength);
    }

    [Fact]
    public void Mmd0TraceCapturesEightXyHoldDecayTimeline()
    {
        var sampleData = new byte[64];
        Array.Fill(sampleData, (byte)0x40);
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            command: 0x08,
            commandData: 0x21,
            sampleData: sampleData,
            repeatLength: sampleData.Length)));

        var tick0 = RenderOneTick(song).Voices[0];
        var tick1 = RenderOneTick(song).Voices[0];
        var tick2 = RenderOneTick(song).Voices[0];

        Assert.Equal(0, tick0.HoldTicks);
        Assert.False(tick0.Releasing);
        Assert.Equal(64, tick0.Volume);
        Assert.Equal(2, tick0.FadeSpeed);

        Assert.Equal(-1, tick1.HoldTicks);
        Assert.True(tick1.Releasing);
        Assert.Equal(62, tick1.Volume);

        Assert.True(tick2.Releasing);
        Assert.Equal(60, tick2.Volume);
    }

    private static MmdTickTrace RenderOneTick(MmdSong song)
    {
        var options = AudioRenderOptions.Default;
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];
        song.RenderTick(buffer, options);
        if (song.LastTrace == null)
        {
            throw new InvalidOperationException("Expected a debug trace after rendering one tick.");
        }

        return song.LastTrace;
    }
}
