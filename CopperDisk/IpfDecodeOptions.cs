namespace CopperDisk;

/// <summary>
/// Controls how IPF track streams are materialized for an emulator.
/// </summary>
public sealed class IpfDecodeOptions
{
    /// <summary>
    /// Gets a shared options instance matching the Amiga floppy DMA path.
    /// </summary>
    public static IpfDecodeOptions Default { get; } = new IpfDecodeOptions();

    /// <summary>
    /// Gets or sets a value indicating whether tracks are rounded up to a 16-bit boundary.
    /// </summary>
    public bool AlignTracksToWord { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether decoding starts at the index position.
    /// </summary>
    public bool StartAtIndex { get; set; } = true;
}
