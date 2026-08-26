using CopperMod.Replayers;

namespace CopperMod.EaglePlayer;

/// <summary>An authenticated, first-use-installable asset from the EaglePlayer distribution.</summary>
public sealed class EaglePlayerReplayerRegistration
{
    internal EaglePlayerReplayerRegistration(
        string id,
        string displayName,
        EaglePlayerAssetKind kind,
        string archiveMemberPath,
        int size,
        string sha256)
    {
        Id = id;
        DisplayName = displayName;
        Kind = kind;
        ArchiveMemberPath = archiveMemberPath;
        Descriptor = new BinaryReplayerDescriptor(
            $"eagleplayer-2.06-{id}",
            $"EaglePlayer 2.06 {displayName}",
            Path.Combine("EaglePlayer", id),
            Path.GetFileName(archiveMemberPath),
            $"COPPERMOD_EAGLEPLAYER_{ToEnvironmentToken(id)}",
            $"CopperMod.EaglePlayer.{id}.Path",
            new BinaryReplayerIdentity("EaglePlayer 2.06 archive", size, sha256));
        ArchiveSource = new BinaryReplayerArchiveSource(
            new Uri(EaglePlayerReplayerCatalog.ArchiveUrl),
            archiveMemberPath,
            EaglePlayerReplayerCatalog.ExpectedArchiveSize,
            EaglePlayerReplayerCatalog.ExpectedArchiveSha256);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public EaglePlayerAssetKind Kind { get; }
    public string ArchiveMemberPath { get; }
    public BinaryReplayerDescriptor Descriptor { get; }
    public BinaryReplayerArchiveSource ArchiveSource { get; }
    public string DefaultRelativePath => Descriptor.DefaultRelativePath;

    /// <summary>Loads this asset, downloading its authenticated source archive when absent.</summary>
    public byte[] LoadConfigured()
        => BinaryReplayerInstaller.EnsureInstalled(Descriptor, ArchiveSource);

    private static string ToEnvironmentToken(string value)
        => string.Concat(value.Select(static character => char.IsLetterOrDigit(character)
            ? char.ToUpperInvariant(character)
            : '_'));
}
