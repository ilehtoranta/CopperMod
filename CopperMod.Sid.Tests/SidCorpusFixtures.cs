namespace CopperMod.TestSupport;

/// <summary>Composer-qualified fixtures shared by SID playback and CLI tests.</summary>
internal static class SidCorpusFixtures
{
    public const string Arkanoid = "MUSICIANS/G/Galway_Martin/Arkanoid.sid";
    public const string YieArKungFuII = "MUSICIANS/G/Galway_Martin/Yie_Ar_Kung_Fu_II.sid";
    public const string GreenBeret = "MUSICIANS/G/Galway_Martin/Green_Beret.sid";
    public const string ShortCircuit = "MUSICIANS/G/Galway_Martin/Short_Circuit.sid";
    public const string Wizball = "MUSICIANS/G/Galway_Martin/Wizball.sid";
    public const string GameOver = "MUSICIANS/G/Galway_Martin/Game_Over.sid";
    public const string Commando = "MUSICIANS/H/Hubbard_Rob/Commando.sid";
    public const string GreatGianaSisters = "MUSICIANS/H/Huelsbeck_Chris/Great_Giana_Sisters.sid";
    public const string FlimbosQuestIntro = "MUSICIANS/O/Ouwehand_Reyn/Flimbos_Quest_intro.sid";
    public const string Tetris = "MUSICIANS/B/Beben_Wally/Tetris.sid";
    public const string GIHero = "MUSICIANS/T/Tel_Jeroen/G_I_Hero.sid";
    public const string SpijkerhoekVanSanten = "MUSICIANS/0-9/20CC/van_Santen_Edwin/Spijkerhoek.sid";
    public const string SpijkerhoekBalai = "MUSICIANS/A/Audial_Arts/Balai_Rodney/Spijkerhoek.sid";

    // Preserve the small historical TestTunes/SID layout. Never search by basename
    // recursively: an HVSC tree contains unrelated versions of these same titles.
    private static readonly IReadOnlyDictionary<string, string[]> LegacyPaths =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Arkanoid] = ["Galway/Arkanoid.sid", "Arkanoid.sid"],
            [YieArKungFuII] = ["Galway/Yie_Ar_Kung_Fu_II.sid"],
            [GreenBeret] = ["Galway/Green_Beret.sid", "Green_Beret.sid"],
            [ShortCircuit] = ["Galway/Short_Circuit.sid", "Short_Circuit.sid"],
            [Wizball] = ["Galway/Wizball.sid"],
            [GameOver] = ["Galway/Game_Over.sid"],
            [Commando] = ["Tough/Commando.sid"],
            [GreatGianaSisters] = ["Tough/Great_Giana_Sisters.sid"],
            [FlimbosQuestIntro] = ["Tough/Flimbos_Quest_intro.sid"],
            [Tetris] = ["Wally Beben/Tetris.sid"],
            [GIHero] = ["Jeroen Tel/G_I_Hero.sid"],
        };

    public static string Find(string relativePath)
    {
        var corpusRoot = Environment.GetEnvironmentVariable("SID_CORPUS_ROOT");
        if (!string.IsNullOrWhiteSpace(corpusRoot))
        {
            // An explicit root is authoritative; do not silently borrow another corpus.
            return FindInRoot(corpusRoot, relativePath)
                ?? throw new FileNotFoundException($"Required SID corpus fixture is missing under SID_CORPUS_ROOT '{corpusRoot}': {relativePath}");
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var path = FindInRoot(Path.Combine(directory.FullName, "TestTunes", "SID"), relativePath);
            if (path != null) return path;
        }

        throw new FileNotFoundException($"Required SID corpus fixture is missing: {relativePath}. Set SID_CORPUS_ROOT to your C64Music directory.");
    }

    internal static string? FindInRoot(string root, string relativePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (File.Exists(candidate)) return candidate;

        // Once an HVSC layout is present, only its exact composer path is accepted.
        if (!Directory.Exists(Path.Combine(root, "MUSICIANS")) && LegacyPaths.TryGetValue(relativePath, out var aliases))
        {
            foreach (var alias in aliases)
            {
                candidate = Path.GetFullPath(Path.Combine(root, alias));
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
