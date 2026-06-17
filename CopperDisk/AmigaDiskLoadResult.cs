namespace CopperDisk;

/// <summary>
/// Contains a loaded disk image and the display name selected by the loader.
/// </summary>
public sealed class AmigaDiskLoadResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AmigaDiskLoadResult"/> class.
    /// </summary>
    /// <param name="media">The loaded media.</param>
    /// <param name="displayName">The file or archive-entry name to show to users.</param>
    public AmigaDiskLoadResult(IAmigaDiskMedia media, string displayName)
    {
        Media = media;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the loaded disk media.
    /// </summary>
    public IAmigaDiskMedia Media { get; }

    /// <summary>
    /// Gets the file or archive-entry name to show to users.
    /// </summary>
    public string DisplayName { get; }
}
