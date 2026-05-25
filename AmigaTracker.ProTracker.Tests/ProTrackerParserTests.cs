using AmigaTracker.Abstractions;
using AmigaTracker.ProTracker;

namespace AmigaTracker.ProTracker.Tests;

public sealed class ProTrackerParserTests
{
    [Theory]
    [InlineData("M.K.")]
    [InlineData("M!K!")]
    [InlineData("4CHN")]
    public void CanLoadRecognizesFourChannelProTrackerSignatures(string signature)
    {
        var data = ModFixtureBuilder.CreateProTracker31();
        WriteAscii(data, 1080, signature);

        Assert.True(new ProTrackerFormat().CanLoad(data));
    }

    [Fact]
    public void CanLoadRecognizesPlausibleLegacyFifteenSampleModule()
    {
        Assert.True(new ProTrackerFormat().CanLoad(ModFixtureBuilder.CreateLegacy15()));
    }

    [Fact]
    public void LoadRejectsPackedModulesWithClearException()
    {
        var format = new ProTrackerFormat();

        Assert.True(format.CanLoad(ModFixtureBuilder.CreatePacked()));
        Assert.Throws<UnsupportedModuleFormatException>(() => format.Load(ModFixtureBuilder.CreatePacked()));
    }

    [Fact]
    public void ParserScansAllOneHundredTwentyEightOrdersLikePtReplay()
    {
        var orders = new byte[128];
        orders[127] = 7;
        var data = ModFixtureBuilder.CreateProTracker31(songLength: 1, orderTable: orders, patternCount: 8);
        using var song = Assert.IsType<ProTrackerSong>(new ProTrackerFormat().Load(data));

        Assert.Equal(8, song.Module.Patterns.Count);
    }

    [Fact]
    public void ParserUsesWordSampleLengthsAndSignedEightBitPcm()
    {
        var sampleData = new byte[] { 0x80, 0xC0, 0x00, 0x40, 0x7F, 0x20 };
        var data = ModFixtureBuilder.CreateProTracker31(sampleData: sampleData, repeatLengthWords: sampleData.Length / 2);
        using var song = Assert.IsType<ProTrackerSong>(new ProTrackerFormat().Load(data));
        var sample = song.Module.Samples[0];

        Assert.Equal(sampleData.Length / 2, sample.LengthWords);
        Assert.Equal(sampleData.Length, sample.LengthBytes);
        Assert.Equal(-1.0f, song.Module.SampleArea[0], precision: 6);
        Assert.Equal(-0.5f, song.Module.SampleArea[1], precision: 6);
        Assert.Equal(0.0f, song.Module.SampleArea[2], precision: 6);
        Assert.Equal(0.5f, song.Module.SampleArea[3], precision: 6);
        Assert.Equal(127.0f / 128.0f, song.Module.SampleArea[4], precision: 6);
    }

    [Fact]
    public void FailrightFixtureParsesWithTrailingDataPreserved()
    {
        var data = File.ReadAllBytes(FindWorkspaceFile(Path.Combine("TestTunes", "ProTracker", "failright.mod")));
        using var song = Assert.IsType<ProTrackerSong>(new ProTrackerFormat().Load(data));

        Assert.Equal("failright", song.Module.Title);
        Assert.Equal(ModLayout.ProTracker31, song.Module.Layout);
        Assert.Equal("M.K.", song.Module.Signature);
        Assert.Equal(29, song.Module.SongLength);
        Assert.Equal(15, song.Module.Patterns.Count);
        Assert.Equal(31, song.Module.Samples.Count);
        Assert.True(song.Module.SampleArea.Length > 20_000);
    }

    private static void WriteAscii(byte[] data, int offset, string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        Array.Copy(bytes, 0, data, offset, bytes.Length);
    }

    internal static string FindWorkspaceFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find fixture '{relativePath}'.");
    }
}
