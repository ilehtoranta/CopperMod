using System.Collections.Generic;

namespace CopperMod.Ipf;

/// <summary>
/// A decoded IPF floppy disk image.
/// </summary>
public sealed class IpfDiskImage
{
    internal IpfDiskImage(
        int minCylinder,
        int maxCylinder,
        int minHead,
        int maxHead,
        IReadOnlyList<IpfTrack> tracks)
    {
        MinCylinder = minCylinder;
        MaxCylinder = maxCylinder;
        MinHead = minHead;
        MaxHead = maxHead;
        Tracks = tracks;
    }

    /// <summary>
    /// Gets the lowest cylinder number described by the image metadata.
    /// </summary>
    public int MinCylinder { get; }

    /// <summary>
    /// Gets the highest cylinder number described by the image metadata.
    /// </summary>
    public int MaxCylinder { get; }

    /// <summary>
    /// Gets the lowest head number described by the image metadata.
    /// </summary>
    public int MinHead { get; }

    /// <summary>
    /// Gets the highest head number described by the image metadata.
    /// </summary>
    public int MaxHead { get; }

    /// <summary>
    /// Gets the decoded track streams.
    /// </summary>
    public IReadOnlyList<IpfTrack> Tracks { get; }
}
