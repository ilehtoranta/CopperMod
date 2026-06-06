using System;

namespace CopperDisk;

internal sealed class AdfDiskMedia : IAmigaSectorDiskMedia, IWritableAmigaDiskMedia
{
    private readonly byte[][] _encodedTracks = new byte[AmigaDiskGeometry.TrackCount][];

    public AdfDiskMedia(byte[] data)
    {
        if (data.Length != AmigaDiskGeometry.StandardAdfSize)
        {
            throw new AmigaDiskException($"Only standard {AmigaDiskGeometry.StandardAdfSize}-byte sector images are supported.");
        }

        DataBytes = data;
    }

    public int Cylinders => AmigaDiskGeometry.CylinderCount;

    public int Heads => AmigaDiskGeometry.HeadCount;

    public bool HasCompleteSectorData => true;

    public bool IsDirty { get; private set; }

    public byte[] DataBytes { get; }

    public ReadOnlyMemory<byte> Data => DataBytes;

    public ReadOnlyMemory<byte> BootBlock => DataBytes.AsMemory(0, 1024);

    public IAmigaTrack ReadTrack(int cylinder, int head)
    {
        var index = GetTrackIndex(cylinder, head);
        var data = _encodedTracks[index] ??= AmigaDosTrackEncoder.EncodeTrack(this, cylinder, head);
        return new AmigaEncodedTrack(data, checked(data.Length * 8), startBit: 0);
    }

    public bool TryWriteTrack(int cylinder, int head, IAmigaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var index = GetTrackIndex(cylinder, head);
        var trackDataOffset = index * AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize;
        var trackData = DataBytes
            .AsSpan(trackDataOffset, AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize)
            .ToArray();
        var decodedSectors = AmigaDosTrackDecoder.DecodeTrackBestEffort(track, cylinder, head, trackData);
        if (decodedSectors == 0)
        {
            return false;
        }

        trackData.CopyTo(DataBytes.AsSpan(trackDataOffset, trackData.Length));
        _encodedTracks[index] = null!;
        IsDirty = true;
        return true;
    }

    public ReadOnlyMemory<byte> ReadSector(int cylinder, int head, int sector)
    {
        return ReadSector(GetLogicalSector(cylinder, head, sector));
    }

    public ReadOnlyMemory<byte> ReadSector(int logicalSector)
    {
        var offset = checked(logicalSector * AmigaDiskGeometry.SectorSize);
        if (logicalSector < 0 || offset + AmigaDiskGeometry.SectorSize > DataBytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalSector));
        }

        return DataBytes.AsMemory(offset, AmigaDiskGeometry.SectorSize);
    }

    public ReadOnlyMemory<byte> ReadBytes(int byteOffset, int byteCount)
    {
        if (byteOffset < 0 || byteCount < 0 || byteOffset + byteCount > DataBytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), "Requested disk byte range is outside the sector image.");
        }

        return DataBytes.AsMemory(byteOffset, byteCount);
    }

    internal static int GetTrackIndex(int cylinder, int head)
    {
        if (cylinder < 0 || cylinder >= AmigaDiskGeometry.CylinderCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cylinder));
        }

        if (head < 0 || head >= AmigaDiskGeometry.HeadCount)
        {
            throw new ArgumentOutOfRangeException(nameof(head));
        }

        return (cylinder * AmigaDiskGeometry.HeadCount) + head;
    }

    internal static int GetLogicalSector(int cylinder, int head, int sector)
    {
        _ = GetTrackIndex(cylinder, head);
        if (sector < 0 || sector >= AmigaDiskGeometry.SectorsPerTrack)
        {
            throw new ArgumentOutOfRangeException(nameof(sector));
        }

        return ((cylinder * AmigaDiskGeometry.HeadCount) + head) * AmigaDiskGeometry.SectorsPerTrack + sector;
    }
}
