namespace CopperDisk;

public static class AmigaDiskGeometry
{
    public const int SectorSize = 512;
    public const int SectorsPerTrack = 11;
    public const int HeadCount = 2;
    public const int CylinderCount = 80;
    public const int StandardAdfSize = CylinderCount * HeadCount * SectorsPerTrack * SectorSize;
    public const int TrackCount = CylinderCount * HeadCount;
}
