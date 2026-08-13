namespace CopperMod.Replayers;

/// <summary>Describes one trusted native replayer binary.</summary>
public sealed class BinaryReplayerIdentity
{
    /// <summary>Creates a trusted binary identity.</summary>
    public BinaryReplayerIdentity(string version, int size, string sha256)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A binary replayer version is required.", nameof(version));
        }

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A binary replayer size must be positive.");
        }

        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A binary replayer SHA256 must contain exactly 64 hexadecimal characters.", nameof(sha256));
        }

        Version = version;
        Size = size;
        Sha256 = sha256.ToUpperInvariant();
    }

    /// <summary>Human-readable version label.</summary>
    public string Version { get; }

    /// <summary>Exact file length in bytes.</summary>
    public int Size { get; }

    /// <summary>Uppercase hexadecimal SHA256 digest.</summary>
    public string Sha256 { get; }
}
