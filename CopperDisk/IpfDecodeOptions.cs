namespace CopperDisk;

/// <summary>
/// Controls how IPF track streams are materialized for an emulator.
/// </summary>
/// <remarks>
/// Options are immutable after construction. <see cref="Default"/> is safe to share across callers.
/// </remarks>
public sealed class IpfDecodeOptions
{
    /// <summary>
    /// Gets a shared options instance matching the Amiga floppy DMA path.
    /// </summary>
    public static IpfDecodeOptions Default { get; } = new IpfDecodeOptions();

    /// <summary>
    /// Gets a value indicating whether tracks are rounded up to a 16-bit boundary.
    /// </summary>
    public bool AlignTracksToWord { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether decoding starts at the index position.
    /// </summary>
    public bool StartAtIndex { get; init; } = true;
}
