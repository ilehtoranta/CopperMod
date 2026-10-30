using CopperMod.Abstractions;
using CopperMod.EaglePlayer;
using CopperMod.Rendering;
using System.IO.Compression;

namespace CopperMod.Tests;

public sealed class TfmxPlaybackTests
{
    [TfmxFixtureFact]
    public void ArchivedTfmxMatchesExtractedCompanionPlayback()
    {
        var modulePath = Environment.GetEnvironmentVariable("COPPERMOD_TFMX_TEST_MODULE")!;
        var samplesPath = Path.ChangeExtension(modulePath, ".sam");
        Assert.True(File.Exists(samplesPath), "TFMX .sam companion is required for this test.");
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var output = archive.CreateEntry("music/" + Path.GetFileName(modulePath)).Open())
                output.Write(File.ReadAllBytes(modulePath));
            using (var output = archive.CreateEntry("music/" + Path.GetFileName(samplesPath)).Open())
                output.Write(File.ReadAllBytes(samplesPath));
        }

        using var extracted = ModuleFormatRegistry.LoadFile(modulePath);
        using var archived = ModuleFormatRegistry.Load(stream.ToArray());
        var expected = Render(extracted);
        Assert.Contains(expected, value => Math.Abs(value) > 0.001f);
        Assert.Equal(expected, Render(archived));
    }

    [Fact]
    public void DefaultRegistryRecognizesTfmxButRejectsShortOrUnrelatedHeaders()
    {
        var format = Assert.Single(ModuleFormatRegistry.CreateDefaultFormats().OfType<TfmxFormat>());
        var data = new byte[512];
        "TFMX-SONG"u8.CopyTo(data);
        Assert.True(format.CanLoad(data));
        Assert.False(format.CanLoad(data.AsSpan(0, 20)));
        Assert.False(format.CanLoad(new byte[512]));
    }

    [TfmxFixtureFact]
    public void NormalLoaderRendersResetsSeeksAndCapturesChannels()
    {
        using var song = ModuleFormatRegistry.LoadFile(Environment.GetEnvironmentVariable("COPPERMOD_TFMX_TEST_MODULE")!);
        Assert.Equal("TFMX", song.Metadata.FormatName);
        Assert.Equal(4, song.Metadata.ChannelCount);
        var selector = Assert.IsAssignableFrom<IModuleSubSongSelector>(song);
        var waveform = Assert.IsAssignableFrom<IModuleChannelWaveformProvider>(song);
        Assert.True(selector.SubSongCount >= 1);
        waveform.ChannelWaveformCaptureEnabled = true;
        var first = Render(song);
        Assert.Contains(first, v => Math.Abs(v) > 0.001f);
        Assert.All(first, v => Assert.True(float.IsFinite(v)));
        Assert.NotNull(waveform.LastChannelWaveform);
        song.Reset();
        Assert.Equal(first, Render(song));
        song.Seek(TimeSpan.FromSeconds(0.2));
        Assert.True(song.Position.Time >= TimeSpan.FromSeconds(0.2));
        selector.SelectSubSong(0);
        Assert.Equal(first, Render(song));
        Assert.DoesNotContain(song.Diagnostics, d => d.Severity != ModuleDiagnosticSeverity.Info);
    }

    private static float[] Render(IModuleSong song)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        for (var tick = 0; tick < 12; tick++)
        {
            var frames = song.GetCurrentTickFrameCount();
            var samples = new float[frames * 2];
            song.RenderTick(samples);
            foreach (var sample in samples) writer.Write(sample);
        }
        var bytes = output.ToArray();
        var result = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }
}

public sealed class TfmxFixtureFactAttribute : FactAttribute
{
    public TfmxFixtureFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("COPPERMOD_TFMX_TEST_MODULE")))
            Skip = "Set COPPERMOD_TFMX_TEST_MODULE to a local TFMX song with its companion samples.";
    }
}
