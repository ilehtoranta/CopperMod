namespace CopperDisk;

/// <summary>
/// Controls format-specific disk loading options.
/// </summary>
public sealed class AmigaDiskLoadOptions
{
    /// <summary>
    /// Gets a shared options instance matching default decoder behavior.
    /// </summary>
    public static AmigaDiskLoadOptions Default { get; } = new AmigaDiskLoadOptions();

    /// <summary>
    /// Gets IPF decode options used for direct and ZIP-wrapped IPF images.
    /// </summary>
    public IpfDecodeOptions? Ipf { get; init; }

    /// <summary>
    /// Gets SCP decode options used for direct and ZIP-wrapped SCP images.
    /// </summary>
    public ScpDecodeOptions? Scp { get; init; }
}
