using System;

namespace CopperDisk;

public interface IAmigaDiskMedia
{
    int Cylinders { get; }

    int Heads { get; }

    IAmigaTrack ReadTrack(int cylinder, int head);
}

public interface IAmigaTrack
{
    int BitLength { get; }

    int StartBit { get; }

    ReadOnlyMemory<byte> Data { get; }

    AmigaTrackFeatures Features { get; }
}

public interface IAmigaSectorDiskMedia : IAmigaDiskMedia
{
    bool HasCompleteSectorData { get; }

    ReadOnlyMemory<byte> Data { get; }

    ReadOnlyMemory<byte> BootBlock { get; }

    ReadOnlyMemory<byte> ReadSector(int cylinder, int head, int sector);

    ReadOnlyMemory<byte> ReadSector(int logicalSector);

    ReadOnlyMemory<byte> ReadBytes(int byteOffset, int byteCount);
}

public interface IWritableAmigaDiskMedia : IAmigaDiskMedia
{
    bool IsDirty { get; }

    bool TryWriteTrack(int cylinder, int head, IAmigaTrack track);
}

[Flags]
public enum AmigaTrackFeatures
{
    None = 0,
    PreservedTrackData = 1 << 0,
    WeakData = 1 << 1,
    ApproximateWeakData = 1 << 2
}
