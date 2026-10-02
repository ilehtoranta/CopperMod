using CopperMod.TestSupport;

namespace CopperMod.Sid.Tests;

public sealed class SidCorpusFixtureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "coppermod-sid-corpus-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void HvscResolutionSelectsComposerDespiteDuplicateBasenames()
    {
        Add("MUSICIANS/D/Daf/Arkanoid.sid");
        var expected = Add(SidCorpusFixtures.Arkanoid);
        Add("Galway/Arkanoid.sid");

        Assert.Equal(expected, SidCorpusFixtures.FindInRoot(root, SidCorpusFixtures.Arkanoid));
    }

    [Fact]
    public void MissingHvscComposerDoesNotSelectCoverOrLegacyCopy()
    {
        Add("MUSICIANS/D/Daf/Arkanoid.sid");
        Add("Galway/Arkanoid.sid");

        Assert.Null(SidCorpusFixtures.FindInRoot(root, SidCorpusFixtures.Arkanoid));
    }

    [Fact]
    public void HistoricalWorkspaceLayoutStillResolves()
    {
        var expected = Add("Galway/Arkanoid.sid");

        Assert.Equal(expected, SidCorpusFixtures.FindInRoot(root, SidCorpusFixtures.Arkanoid));
    }

    [Fact]
    public void SpijkerhoekArrangementsHaveSeparateIdentities()
    {
        var original = Add(SidCorpusFixtures.SpijkerhoekVanSanten);
        var cover = Add(SidCorpusFixtures.SpijkerhoekBalai);

        Assert.Equal(original, SidCorpusFixtures.FindInRoot(root, SidCorpusFixtures.SpijkerhoekVanSanten));
        Assert.Equal(cover, SidCorpusFixtures.FindInRoot(root, SidCorpusFixtures.SpijkerhoekBalai));
    }

    private string Add(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, []);
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
