namespace CopperDisk;

/// <summary>
/// Defines the standard double-density AmigaDOS floppy geometry used by ADF sector images.
/// </summary>
public static class AmigaDiskGeometry
{
    /// <summary>
    /// The number of bytes in one AmigaDOS sector.
    /// </summary>
    public const int SectorSize = 512;

    /// <summary>
    /// The number of sectors in one standard AmigaDOS track.
    /// </summary>
    public const int SectorsPerTrack = 11;

    /// <summary>
    /// The number of floppy heads.
    /// </summary>
    public const int HeadCount = 2;

    /// <summary>
    /// The number of cylinders in a standard 80-track Amiga disk image.
    /// </summary>
    public const int CylinderCount = 80;

    /// <summary>
    /// The byte length of a standard 880 KiB ADF sector image.
    /// </summary>
    public const int StandardAdfSize = CylinderCount * HeadCount * SectorsPerTrack * SectorSize;

    /// <summary>
    /// The number of physical tracks in a standard Amiga disk image.
    /// </summary>
    public const int TrackCount = CylinderCount * HeadCount;
}
