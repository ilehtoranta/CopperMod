using CopperMod.Replayers;

namespace CopperMod.Ahx;

/// <summary>Locates and verifies the locally supplied AHX 2.3d 68000 replay binary.</summary>
public static class AhxReferencePlayerBinary
{
    public const string EnvironmentVariable = "COPPERMOD_AHX_REPLAYER000";
    public const string AppContextDataKey = "CopperMod.Ahx.Replayer000Path";
    public const string ReplayersDirectoryName = "Replayers";
    public const string FormatDirectoryName = "AHX";
    public const string FileName = "AHX-Replayer000.BIN";
    public static string DefaultRelativePath => Path.Combine(ReplayersDirectoryName, FormatDirectoryName, FileName);
    public const int ExpectedSize = 11_580;
    public const string ExpectedSha256 = "25B8ED4998B797216DFE13FA9984F213AAF1B6D57341B5C9523D854CB17A056F";
    private static readonly BinaryReplayerDescriptor Descriptor = new(
        "ahx-2.3d-replayer000",
        "cycle-exact AHX",
        FormatDirectoryName,
        FileName,
        EnvironmentVariable,
        AppContextDataKey,
        new BinaryReplayerIdentity("AHX 2.3d Replayer000", ExpectedSize, ExpectedSha256));

    /// <summary>Loads the configured binary without searching user-specific directories.</summary>
    public static byte[] LoadConfigured()
        => BinaryReplayerLoader.LoadConfigured(Descriptor);

    /// <summary>Verifies the exact reference-binary identity and returns a private copy.</summary>
    public static byte[] Validate(ReadOnlySpan<byte> player, string? source = null)
        => BinaryReplayerLoader.Validate(Descriptor, player, source);
}
