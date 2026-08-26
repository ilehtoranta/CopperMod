using CopperMod.Replayers;

namespace CopperMod.EaglePlayer;

/// <summary>Locates and verifies the Wanted Team TFMX v4 EaglePlayer binary.</summary>
public static class TfmxReplayerBinary
{
    public const string EnvironmentVariable = "COPPERMOD_TFMX_EAGLEPLAYER";
    public const string AppContextDataKey = "CopperMod.EaglePlayer.TfmxPath";
    public const string FileName = "TFMX";
    public const int ExpectedSize = 8_008;
    public const string ExpectedSha256 = "FDF114E1D76EC0FC1D6E7471EC63FE93F0A043D367A562B8B2A398B74538DC21";
    public const string ArchiveUrl = "https://aminet.net/mus/play/EP_TFMX.lha";
    public const string ArchiveMemberPath = "TFMX/EaglePlayers/TFMX";
    public const int ExpectedArchiveSize = 11_074;
    public const string ExpectedArchiveSha256 = "72C8882D4AB5E2FAB50C03A1D02D19468374A814C87DBD55D582C2254D59541F";

    /// <summary>Relative location used by the shared binary-replayer convention.</summary>
    public static string DefaultRelativePath => Path.Combine("Replayers", "EaglePlayer", "TFMX", FileName);

    /// <summary>Trusted identity and discovery configuration for the TFMX player.</summary>
    public static BinaryReplayerDescriptor Descriptor { get; } = new(
        "wanted-team-tfmx-v4",
        "Wanted Team TFMX v4 EaglePlayer",
        Path.Combine("EaglePlayer", "TFMX"),
        FileName,
        EnvironmentVariable,
        AppContextDataKey,
        new BinaryReplayerIdentity("Wanted Team TFMX v4", ExpectedSize, ExpectedSha256));

    /// <summary>Authenticated Aminet source used for a first-use installation.</summary>
    public static BinaryReplayerArchiveSource ArchiveSource { get; } = new(
        new Uri(ArchiveUrl),
        ArchiveMemberPath,
        ExpectedArchiveSize,
        ExpectedArchiveSha256);

    /// <summary>Loads TFMX, downloading and caching the authenticated original archive when absent.</summary>
    public static byte[] LoadConfigured()
        => BinaryReplayerInstaller.EnsureInstalled(Descriptor, ArchiveSource);

    /// <summary>Authenticates explicitly supplied TFMX player bytes.</summary>
    public static byte[] Validate(ReadOnlySpan<byte> player, string? source = null)
        => BinaryReplayerLoader.Validate(Descriptor, player, source);
}
