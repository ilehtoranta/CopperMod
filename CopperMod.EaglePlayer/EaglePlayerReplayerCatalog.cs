using System.Reflection;
using System.Text.Json;

namespace CopperMod.EaglePlayer;

/// <summary>Authenticated registry of all executable assets in the EaglePlayer 2.06 archive.</summary>
public static class EaglePlayerReplayerCatalog
{
    public const string ArchiveUrl = "https://aminet.net/mus/play/Eagleplayer.lha";
    public const int ExpectedArchiveSize = 3_092_246;
    public const string ExpectedArchiveSha256 = "20E11A13C2528D6CE864C02B1170872F10F52517DDCD82F4839E46F228C9BFB3";
    public const int ExpectedPlayerCount = 114;
    public const int ExpectedCompanionCount = 4;

    private static readonly IReadOnlyList<EaglePlayerReplayerRegistration> RegisteredAssets = LoadManifest();
    private static readonly IReadOnlyDictionary<string, EaglePlayerReplayerRegistration> AssetsById =
        RegisteredAssets.ToDictionary(static asset => asset.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<EaglePlayerReplayerRegistration> Assets => RegisteredAssets;
    public static IReadOnlyList<EaglePlayerReplayerRegistration> Players { get; } =
        RegisteredAssets.Where(static asset => asset.Kind == EaglePlayerAssetKind.Player).ToArray();
    public static IReadOnlyList<EaglePlayerReplayerRegistration> Companions { get; } =
        RegisteredAssets.Where(static asset => asset.Kind == EaglePlayerAssetKind.Companion).ToArray();

    /// <summary>Gets a registered player or companion by stable slug.</summary>
    public static EaglePlayerReplayerRegistration Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !AssetsById.TryGetValue(id, out var registration))
        {
            throw new KeyNotFoundException($"No EaglePlayer 2.06 asset is registered as '{id}'.");
        }

        return registration;
    }

    private static IReadOnlyList<EaglePlayerReplayerRegistration> LoadManifest()
    {
        var assembly = typeof(EaglePlayerReplayerCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(static name => name.EndsWith("EaglePlayerReplayers.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded EaglePlayer identity manifest is unavailable.");
        var entries = JsonSerializer.Deserialize<ManifestEntry[]>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The EaglePlayer identity manifest is empty.");

        var registrations = entries.Select(static entry => new EaglePlayerReplayerRegistration(
            Slugify(Path.GetFileName(entry.Name)),
            Path.GetFileName(entry.Name),
            entry.Companion ? EaglePlayerAssetKind.Companion : EaglePlayerAssetKind.Player,
            entry.Name,
            entry.Size,
            entry.Sha256)).ToArray();
        if (registrations.Count(static asset => asset.Kind == EaglePlayerAssetKind.Player) != ExpectedPlayerCount ||
            registrations.Count(static asset => asset.Kind == EaglePlayerAssetKind.Companion) != ExpectedCompanionCount)
        {
            throw new InvalidOperationException("The EaglePlayer identity manifest asset counts do not match the audited archive.");
        }

        var duplicate = registrations.GroupBy(static asset => asset.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"EaglePlayer asset ID '{duplicate.Key}' is not unique.");
        }

        return Array.AsReadOnly(registrations);
    }

    private static string Slugify(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var separatorPending = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && result.Length != 0)
                {
                    result.Append('-');
                }

                result.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }
        }

        return result.ToString();
    }

    private sealed class ManifestEntry
    {
        public required string Name { get; init; }
        public required int Size { get; init; }
        public required string Sha256 { get; init; }
        public bool Companion { get; init; }
    }
}
