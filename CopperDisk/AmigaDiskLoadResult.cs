namespace CopperDisk;

public sealed class AmigaDiskLoadResult
{
    public AmigaDiskLoadResult(IAmigaDiskMedia media, string displayName)
    {
        Media = media;
        DisplayName = displayName;
    }

    public IAmigaDiskMedia Media { get; }

    public string DisplayName { get; }
}
