using System;
using System.Collections.Generic;

namespace CopperDisk;

internal sealed class TrackBackedDiskMedia : IAmigaSectorDiskMedia
{
    private readonly AmigaEncodedTrack[] _encodedTracks;

    public TrackBackedDiskMedia(IReadOnlyList<IAmigaTrack> encodedTracks)
    {
        _encodedTracks = BuildTrackSet(encodedTracks);
        DataBytes = AmigaDosTrackDecoder.DecodeBestEffort(_encodedTracks, out var hasCompleteDecodedSectorData);
        HasCompleteDecodedSectorData = hasCompleteDecodedSectorData;
    }

    public TrackBackedDiskMedia(IReadOnlyList<AmigaEncodedTrack> encodedTracks)
    {
        _encodedTracks = BuildTrackSet(encodedTracks);
        DataBytes = AmigaDosTrackDecoder.DecodeBestEffort(_encodedTracks, out var hasCompleteDecodedSectorData);
        HasCompleteDecodedSectorData = hasCompleteDecodedSectorData;
    }

    public int Cylinders => AmigaDiskGeometry.CylinderCount;

    public int Heads => AmigaDiskGeometry.HeadCount;

    public bool HasCompleteDecodedSectorData { get; }

    public byte[] DataBytes { get; }

    public ReadOnlyMemory<byte> SectorData => DataBytes;

    public ReadOnlyMemory<byte> BootBlock => DataBytes.AsMemory(0, 1024);

    public IAmigaTrack ReadTrack(int cylinder, int head)
    {
        var index = AdfDiskMedia.GetTrackIndex(cylinder, head);
        return _encodedTracks[index];
    }

    public ReadOnlyMemory<byte> ReadSector(int cylinder, int head, int sector)
    {
        return ReadSector(AdfDiskMedia.GetLogicalSector(cylinder, head, sector));
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
            throw new ArgumentOutOfRangeException(nameof(byteOffset), "Requested disk byte range is outside the decoded sector view.");
        }

        return DataBytes.AsMemory(byteOffset, byteCount);
    }

    private static AmigaEncodedTrack[] BuildTrackSet<T>(IReadOnlyList<T> encodedTracks)
        where T : IAmigaTrack
    {
        ArgumentNullException.ThrowIfNull(encodedTracks);
        if (encodedTracks.Count != AmigaDiskGeometry.TrackCount)
        {
            throw new ArgumentException($"Exactly {AmigaDiskGeometry.TrackCount} encoded tracks are required.", nameof(encodedTracks));
        }

        var tracks = new AmigaEncodedTrack[encodedTracks.Count];
        for (var index = 0; index < encodedTracks.Count; index++)
        {
            var track = encodedTracks[index];
            tracks[index] = new AmigaEncodedTrack(
                track.EncodedData,
                track.BitLength,
                track.StartBit,
                track.Features | AmigaTrackFeatures.PreservedTrackData);
        }

        return tracks;
    }
}
