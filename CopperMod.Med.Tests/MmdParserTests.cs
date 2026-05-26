using System;
using System.IO;
using System.Linq;
using CopperMod.Abstractions;
using CopperMod.Med;

namespace CopperMod.Med.Tests;

public sealed class MmdParserTests
{
    private static readonly string TitleFixturePath = Path.Combine("TestTunes", "Med", "title");

    [Theory]
    [InlineData(0x4D4D4430u)]
    [InlineData(0x4D4D4431u)]
    [InlineData(0x4D4D4432u)]
    [InlineData(0x4D4D4433u)]
    public void CanLoadRecognizesMmdVersions(uint id)
    {
        var data = id == 0x4D4D4430u
            ? MmdFixtureBuilder.CreateMmd0Sample()
            : id == 0x4D4D4431u
                ? MmdFixtureBuilder.CreateMmd1WithCommandPage()
                : MmdFixtureBuilder.CreateMmd2OrMmd3(id);

        Assert.True(new MmdFormat().CanLoad(data));
    }

    [Fact]
    public void TitleFixtureParsesMmd0Structures()
    {
        var data = File.ReadAllBytes(FindWorkspaceFile(TitleFixturePath));
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(data));
        var module = song.Module;

        Assert.Equal(MmdVersion.Mmd0, module.Version);
        Assert.Equal(data.Length, module.ModuleLength);
        Assert.Equal(15, module.Song.NumBlocks);
        Assert.Equal(15, module.Blocks.Count);
        Assert.Equal(16, module.Song.SongLength);
        Assert.Equal(16, module.Song.LegacyPlaySequence.Take(module.Song.SongLength).Count());
        Assert.Equal(11, module.Instruments.Count(i => i.Kind == MmdInstrumentKind.Synth || i.Kind == MmdInstrumentKind.Hybrid));
    }

    [Fact]
    public void TitleFixtureRendersFiniteNonzeroPcmAndAdvances()
    {
        var data = File.ReadAllBytes(FindWorkspaceFile(TitleFixturePath));
        using var song = new MmdFormat().Load(data);
        var options = AudioRenderOptions.Default;
        var energy = 0.0;

        for (var tick = 0; tick < 160; tick++)
        {
            var frames = song.GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            var result = song.RenderTick(buffer, options);

            Assert.Equal(frames, result.FramesWritten);
            for (var i = 0; i < buffer.Length; i++)
            {
                Assert.False(float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i]));
                energy += Math.Abs(buffer[i]);
            }
        }

        Assert.True(energy > 0.01);
        Assert.True(song.Position.Time > TimeSpan.Zero);
    }

    [Fact]
    public void TitleFixtureActivatesAllFourVoicesInOpeningPattern()
    {
        var data = File.ReadAllBytes(FindWorkspaceFile(TitleFixturePath));
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(data));
        var options = AudioRenderOptions.Default;
        var heard = new bool[4];

        for (var tick = 0; tick < 96; tick++)
        {
            var frames = song.GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            song.RenderTick(buffer, options);

            var trace = song.LastTrace;
            Assert.NotNull(trace);
            for (var voice = 0; voice < heard.Length; voice++)
            {
                heard[voice] |= trace!.Voices[voice].IsAudible && trace.Voices[voice].SampleStep > 0.0;
            }
        }

        Assert.All(heard, Assert.True);
    }

    [Fact]
    public void TitleFixturePureSynthStartsFromFirstWaveformSequenceExecution()
    {
        var data = File.ReadAllBytes(FindWorkspaceFile(TitleFixturePath));
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(data));
        song.Seek(new TrackerPosition(4, 0, 0));

        var tick0 = RenderOneTick(song).Voices[1];
        var tick1 = RenderOneTick(song).Voices[1];
        var tick2 = RenderOneTick(song).Voices[1];

        Assert.Equal(10, tick0.InstrumentNumber);
        Assert.Equal(MmdInstrumentKind.Synth, tick0.InstrumentKind);
        Assert.Equal(0, tick0.SynthWaveformIndex);
        Assert.True(tick0.IsAudible);
        Assert.True(tick0.PaulaPointerUpdatedThisTick);
        Assert.True(tick1.IsAudible);
        Assert.False(tick1.PaulaPointerUpdatedThisTick);
        Assert.True(tick2.IsAudible);
        Assert.False(tick2.PaulaPointerUpdatedThisTick);
    }

    [Fact]
    public void TitleFixturePureSynthStartsImmediatelyNearOneMinuteFourteen()
    {
        var data = File.ReadAllBytes(FindWorkspaceFile(TitleFixturePath));
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(data));
        song.Seek(new TrackerPosition(11, 24, 0));

        var tick0 = RenderOneTick(song).Voices[0];

        Assert.Equal(4, tick0.InstrumentNumber);
        Assert.Equal(MmdInstrumentKind.Synth, tick0.InstrumentKind);
        Assert.Equal(0, tick0.SynthWaveformIndex);
        Assert.True(tick0.IsAudible);
        Assert.True(tick0.PaulaPointerUpdatedThisTick);
    }

    [Fact]
    public void GeneratedMmd0SampleFixtureRenders()
    {
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample());
        var energy = RenderTicks(song, 8);

        Assert.True(energy > 0.01);
        Assert.True(song.Position.Time > TimeSpan.Zero);
    }

    [Fact]
    public void Mmd0HardwareFilterStateStartsFromSongFlag()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            songFlags: MmdConstants.FlagFilterOn)));
        var hardwareState = Assert.IsAssignableFrom<IAmigaHardwareStateProvider>(song);

        Assert.True(hardwareState.AmigaHardwareState.AudioFilterEnabled);
    }

    [Fact]
    public void Mmd0F9EnablesHardwareFilterState()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            command: 0x0F,
            commandData: 0xF9)));
        var hardwareState = Assert.IsAssignableFrom<IAmigaHardwareStateProvider>(song);

        Assert.False(hardwareState.AmigaHardwareState.AudioFilterEnabled);
        RenderOneTick(song);

        Assert.True(hardwareState.AmigaHardwareState.AudioFilterEnabled);
    }

    [Fact]
    public void Mmd0F8DisablesHardwareFilterState()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            command: 0x0F,
            commandData: 0xF8,
            songFlags: MmdConstants.FlagFilterOn)));
        var hardwareState = Assert.IsAssignableFrom<IAmigaHardwareStateProvider>(song);

        Assert.True(hardwareState.AmigaHardwareState.AudioFilterEnabled);
        RenderOneTick(song);

        Assert.False(hardwareState.AmigaHardwareState.AudioFilterEnabled);
    }

    [Fact]
    public void Mmd0LowNoteCanUseInstrumentBitFour()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            note: 1,
            instrument: 17,
            repeatLength: 64)));

        Assert.Equal(17, song.Module.Blocks[0].Cells[0, 0].Instrument);
        Assert.True(RenderTicks(song, 4) > 0.01);
    }

    [Fact]
    public void Mmd0EightXyAppliesHoldAndDecayFade()
    {
        var sampleData = Enumerable.Repeat((byte)0x40, 64).ToArray();
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            command: 0x08,
            commandData: 0x21,
            sampleData: sampleData,
            repeatLength: sampleData.Length));

        var energies = RenderTickEnergies(song, 6);

        Assert.True(energies[0] > 0.01);
        Assert.True(energies[1] < energies[0]);
        Assert.True(energies[5] < energies[1]);
    }

    [Fact]
    public void Mmd0EighteenXxCutsVolumeOnRequestedTick()
    {
        var sampleData = Enumerable.Repeat((byte)0x40, 64).ToArray();
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            command: 0x18,
            commandData: 1,
            sampleData: sampleData,
            repeatLength: sampleData.Length));

        var energies = RenderTickEnergies(song, 3);

        Assert.True(energies[0] > 0.01);
        Assert.Equal(0.0, energies[1], precision: 6);
        Assert.Equal(0.0, energies[2], precision: 6);
    }

    [Fact]
    public void Mmd0LoopedSampleWrapsAtRepeatEnd()
    {
        var sampleData = new byte[64];
        Array.Fill(sampleData, (byte)0x40, 0, 32);
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(
            sampleData: sampleData,
            repeatOffset: 16,
            repeatLength: 16));

        var options = AudioRenderOptions.Default;
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];
        song.RenderTick(buffer, options);

        var frameAfterRepeatEnd = 240;
        var frameEnergy = Math.Abs(buffer[frameAfterRepeatEnd * options.ChannelCount]) +
            Math.Abs(buffer[(frameAfterRepeatEnd * options.ChannelCount) + 1]);

        Assert.True(frameEnergy > 0.001);
    }

    [Fact]
    public void Mmd0SampleDataIsDecodedAsSigned8BitPcm()
    {
        var sampleData = new byte[] { 0x80, 0xC0, 0x00, 0x40, 0x7F, 0x20 };
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(sampleData: sampleData)));
        var instrument = song.Module.Instruments[0];

        Assert.False(instrument.Is16Bit);
        Assert.False(instrument.IsStereo);
        Assert.Equal(sampleData.Length, instrument.LeftSamples.Length);
        Assert.Equal(-1.0f, instrument.LeftSamples[0], precision: 6);
        Assert.Equal(-0.5f, instrument.LeftSamples[1], precision: 6);
        Assert.Equal(0.0f, instrument.LeftSamples[2], precision: 6);
        Assert.Equal(0.5f, instrument.LeftSamples[3], precision: 6);
        Assert.Equal(127.0f / 128.0f, instrument.LeftSamples[4], precision: 6);
        Assert.Equal(0.25f, instrument.LeftSamples[5], precision: 6);
    }

    [Fact]
    public void Mmd0PaulaSamplePlaybackUsesEvenByteLength()
    {
        var sampleData = new byte[] { 0x40, 0x40, 0x40, 0x40, 0x7F };
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(sampleData: sampleData)));
        var instrument = song.Module.Instruments[0];

        Assert.Equal((uint)sampleData.Length, instrument.Length);
        Assert.Equal(4, instrument.LeftSamples.Length);
    }

    [Fact]
    public void Mmd2MixingSamplePlaybackCanKeepOddByteLength()
    {
        var sampleData = new byte[] { 0x40, 0x40, 0x40, 0x40, 0x7F };
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd2OrMmd3(MmdConstants.Mmd2, sampleData: sampleData)));
        var instrument = song.Module.Instruments[0];

        Assert.Equal((uint)sampleData.Length, instrument.Length);
        Assert.Equal(sampleData.Length, instrument.LeftSamples.Length);
    }

    [Fact]
    public void StandardPaulaSamplePathKeepsBytesAsSigned8BitWhenS16BitIsPresent()
    {
        var sampleData = new byte[] { 0x80, 0x00, 0x7F, 0x40 };
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(sampleType: 0x10, sampleData: sampleData)));
        var instrument = song.Module.Instruments[0];

        Assert.False(instrument.Is16Bit);
        Assert.Equal(sampleData.Length, instrument.LeftSamples.Length);
        Assert.Equal(-1.0f, instrument.LeftSamples[0], precision: 6);
        Assert.Equal(0.0f, instrument.LeftSamples[1], precision: 6);
        Assert.Equal(127.0f / 128.0f, instrument.LeftSamples[2], precision: 6);
        Assert.Equal(0.5f, instrument.LeftSamples[3], precision: 6);
    }

    [Fact]
    public void MixingSamplePathHonorsSixteenBitFlag()
    {
        var sampleData = new byte[] { 0x80, 0x00, 0x00, 0x00, 0x7F, 0xFF };
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd2OrMmd3(MmdConstants.Mmd2, sampleType: 0x10, sampleData: sampleData)));
        var instrument = song.Module.Instruments[0];

        Assert.True(instrument.Is16Bit);
        Assert.Equal(3, instrument.LeftSamples.Length);
        Assert.Equal(-1.0f, instrument.LeftSamples[0], precision: 6);
        Assert.Equal(0.0f, instrument.LeftSamples[1], precision: 6);
        Assert.Equal(32767.0f / 32768.0f, instrument.LeftSamples[2], precision: 6);
    }

    [Fact]
    public void GeneratedMmd0SynthFixtureRendersWaveformSequence()
    {
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Synth());
        var energy = RenderTicks(song, 8);

        Assert.True(energy > 0.01);
    }

    [Fact]
    public void CxxVolumeCommandCanSilenceRow()
    {
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(command: 0x0C, commandData: 0));
        var energy = RenderTicks(song, 2);

        Assert.Equal(0.0, energy, precision: 6);
    }

    [Fact]
    public void CxxVolumeCommandUsesBcdUnlessVolumeHexFlagIsSet()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(command: 0x0C, commandData: 0x45)));
        var options = AudioRenderOptions.Default;
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];

        song.RenderTick(buffer, options);

        Assert.Equal(45, song.LastTrace!.Voices[0].Volume);
    }

    [Fact]
    public void NineXxCommandChangesTempo2Speed()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(command: 0x09, commandData: 0x06)));
        var options = AudioRenderOptions.Default;
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];

        song.RenderTick(buffer, options);

        Assert.Equal(6, song.LastTrace!.Speed);
    }

    [Fact]
    public void FxxTempoCommandChangesTickFrameCount()
    {
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Sample(command: 0x0F, commandData: 64));
        var options = AudioRenderOptions.Default;
        var before = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(before)];
        song.RenderTick(buffer, options);
        var after = song.GetCurrentTickFrameCount(options);

        Assert.True(after < before);
    }

    [Fact]
    public void BxxLoopFixtureDoesNotHangDurationSimulation()
    {
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0TwoBlockJumpLoop());

        Assert.True(song.Duration.HasTime);
        Assert.True(song.Duration.Time!.Value > TimeSpan.Zero);
    }

    [Fact]
    public void PortamentoFixtureRendersAcrossRows()
    {
        using var song = new MmdFormat().Load(MmdFixtureBuilder.CreateMmd0Portamento());
        var energy = RenderTicks(song, 4);

        Assert.True(energy > 0.01);
        Assert.True(song.Position.Tracker!.Value.Row >= 0);
    }

    [Fact]
    public void Mmd1FixtureParsesFourByteNotesAndCommandPages()
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd1WithCommandPage()));
        var block = song.Module.Blocks[0];

        Assert.Equal(MmdVersion.Mmd1, song.Module.Version);
        Assert.Equal(25, block.Cells[0, 0].Note);
        Assert.Equal(1, block.Cells[0, 0].Instrument);
        Assert.Single(block.AdditionalCommandPages);
        Assert.Equal(0x0C, block.AdditionalCommandPages[0].Commands[0, 0].CommandNumber);
        Assert.Equal(32, block.AdditionalCommandPages[0].Commands[0, 0].Data);
    }

    [Theory]
    [InlineData(0x4D4D4432u, 2)]
    [InlineData(0x4D4D4433u, 3)]
    public void Mmd2AndMmd3FixturesParsePlaySequencesMixingVolumesAndPans(uint id, int version)
    {
        using var song = Assert.IsType<MmdSong>(new MmdFormat().Load(MmdFixtureBuilder.CreateMmd2OrMmd3(id)));
        var module = song.Module;

        Assert.Equal((MmdVersion)version, module.Version);
        Assert.True(module.UsesMixingMode);
        Assert.Single(module.Song.PlaySequences);
        Assert.Single(module.Song.SectionTable);
        Assert.Equal(32, module.Song.TrackVolumes[0]);
        Assert.Equal(48, module.Song.TrackVolumes[1]);
        Assert.Equal(-16, module.Song.TrackPans[0]);
        Assert.Equal(16, module.Song.TrackPans[1]);
        Assert.True(RenderTicks(song, 4) > 0.01);
    }

    private static double RenderTicks(IModuleSong song, int ticks)
    {
        var options = AudioRenderOptions.Default;
        var energy = 0.0;
        for (var tick = 0; tick < ticks; tick++)
        {
            var frames = song.GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            song.RenderTick(buffer, options);
            for (var i = 0; i < buffer.Length; i++)
            {
                Assert.False(float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i]));
                energy += Math.Abs(buffer[i]);
            }
        }

        return energy;
    }

    private static void RenderOneTick(IModuleSong song)
    {
        var options = AudioRenderOptions.Default;
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];
        song.RenderTick(buffer, options);
    }

    private static MmdTickTrace RenderOneTick(MmdSong song)
    {
        var options = AudioRenderOptions.Default;
        var frames = song.GetCurrentTickFrameCount(options);
        var buffer = new float[options.GetSampleCount(frames)];
        song.RenderTick(buffer, options);
        return song.LastTrace ?? throw new InvalidOperationException("Expected a debug trace after rendering one tick.");
    }

    private static double[] RenderTickEnergies(IModuleSong song, int ticks)
    {
        var options = AudioRenderOptions.Default;
        var energies = new double[ticks];
        for (var tick = 0; tick < ticks; tick++)
        {
            var frames = song.GetCurrentTickFrameCount(options);
            var buffer = new float[options.GetSampleCount(frames)];
            song.RenderTick(buffer, options);
            for (var i = 0; i < buffer.Length; i++)
            {
                Assert.False(float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i]));
                energies[tick] += Math.Abs(buffer[i]);
            }
        }

        return energies;
    }

    private static string FindWorkspaceFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find fixture '{name}'.");
    }
}
