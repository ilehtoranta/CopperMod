namespace CopperMod.Amiga.Lightweight;

/// <summary>Fixed configuration for the first lightweight engine milestone.</summary>
public sealed record LightweightA500Configuration
{
    public int ChipRamBytes { get; init; } = 512 * 1024;
    public int SlowRamBytes { get; init; } = 512 * 1024;
    /// <summary>908 retains every OCS hires pixel; 454 is a lowres-only output contract.</summary>
    public int FramebufferWidth { get; init; } = 454;
    public int FramebufferHeight { get; init; } = 313;
    public int AudioSampleRate { get; init; } = 48_000;
    public int AudioChannels { get; init; } = 2;
}
