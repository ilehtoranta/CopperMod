namespace CopperDisk;

/// <summary>
/// Controls how SuperCard Pro flux captures are materialized for an emulator.
/// </summary>
public sealed class ScpDecodeOptions
{
    /// <summary>
    /// The default Amiga DD bit-cell count used for one index-to-index revolution.
    /// </summary>
    public const int AmigaDoubleDensityBitCellsPerRevolution = AmigaDosTrackEncoder.EncodedTrackByteCount * 8;

    /// <summary>
    /// Gets a shared options instance matching A500 PAL DD media.
    /// </summary>
    public static ScpDecodeOptions Default { get; } = new ScpDecodeOptions();

    /// <summary>
    /// Gets the zero-based revolution to decode from each stored track.
    /// </summary>
    public int RevolutionIndex { get; init; }

    /// <summary>
    /// Gets the target Amiga bit-cell count for one decoded revolution.
    /// </summary>
    public int BitCellsPerRevolution { get; init; } = AmigaDoubleDensityBitCellsPerRevolution;

    /// <summary>
    /// Gets a value indicating whether non-Amiga SCP disk type values are accepted.
    /// </summary>
    public bool AllowNonAmigaDiskTypes { get; init; }
}
