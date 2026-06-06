using System;
using System.Collections.Generic;

namespace CopperDisk;

public static class AmigaDosTrackDecoder
{
    private const ushort SyncWord = 0x4489;
    private const uint MfmDataMask = 0x5555_5555;
    private const int TrackCount = AmigaDiskGeometry.CylinderCount * AmigaDiskGeometry.HeadCount;
    private const int EncodedSectorBytesAfterSync = 0x43C;
    private const int EncodedSectorBitsAfterSync = EncodedSectorBytesAfterSync * 8;

    public static byte[] DecodeBestEffort(byte[][] encodedTracks, out bool hasCompleteSectorData)
    {
        ArgumentNullException.ThrowIfNull(encodedTracks);
        var tracks = new AmigaEncodedTrack[encodedTracks.Length];
        for (var index = 0; index < encodedTracks.Length; index++)
        {
            tracks[index] = AmigaEncodedTrack.FromBytes(encodedTracks[index] ?? AmigaDosTrackEncoder.CreateUnformattedTrack());
        }

        return DecodeBestEffort(tracks, out hasCompleteSectorData);
    }

    public static byte[] DecodeBestEffort<T>(IReadOnlyList<T> encodedTracks, out bool hasCompleteSectorData)
        where T : IAmigaTrack
    {
        ArgumentNullException.ThrowIfNull(encodedTracks);
        if (encodedTracks.Count != TrackCount)
        {
            throw new ArgumentException($"Exactly {TrackCount} encoded tracks are required.", nameof(encodedTracks));
        }

        var data = new byte[AmigaDiskGeometry.StandardAdfSize];
        var decodedSectors = new bool[TrackCount * AmigaDiskGeometry.SectorsPerTrack];
        var decodedSectorCount = 0;
        for (var trackNumber = 0; trackNumber < encodedTracks.Count; trackNumber++)
        {
            var track = ToEncodedTrack(encodedTracks[trackNumber]);
            if (track.BitLength < EncodedSectorBitsAfterSync)
            {
                continue;
            }

            DecodeTrack(track, trackNumber, data, decodedSectors, ref decodedSectorCount);
        }

        hasCompleteSectorData = decodedSectorCount == decodedSectors.Length;
        return data;
    }

    public static int DecodeTrackBestEffort(IAmigaTrack track, int cylinder, int head, Span<byte> destinationTrackData)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (cylinder < 0 || cylinder >= AmigaDiskGeometry.CylinderCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cylinder));
        }

        if (head < 0 || head >= AmigaDiskGeometry.HeadCount)
        {
            throw new ArgumentOutOfRangeException(nameof(head));
        }

        if (destinationTrackData.Length != AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize)
        {
            throw new ArgumentException("Destination track data must contain exactly one AmigaDOS track.", nameof(destinationTrackData));
        }

        Span<bool> decodedSectors = stackalloc bool[AmigaDiskGeometry.SectorsPerTrack];
        var decodedSectorCount = 0;
        var encoded = ToEncodedTrack(track);
        var expectedTrackNumber = (cylinder * AmigaDiskGeometry.HeadCount) + head;
        if (encoded.BitLength < EncodedSectorBitsAfterSync)
        {
            return 0;
        }

        for (var offset = 0; offset < encoded.BitLength; offset++)
        {
            if (encoded.ReadUInt16(offset) != SyncWord ||
                encoded.ReadUInt16(offset + 16) != SyncWord)
            {
                continue;
            }

            TryDecodeSector(encoded, offset, expectedTrackNumber, destinationTrackData, decodedSectors, ref decodedSectorCount);
        }

        return decodedSectorCount;
    }

    private static void DecodeTrack(
        AmigaEncodedTrack track,
        int expectedTrackNumber,
        byte[] diskData,
        bool[] decodedSectors,
        ref int decodedSectorCount)
    {
        if (track.BitLength < EncodedSectorBitsAfterSync)
        {
            return;
        }

        for (var offset = 0; offset < track.BitLength; offset++)
        {
            if (track.ReadUInt16(offset) != SyncWord ||
                track.ReadUInt16(offset + 16) != SyncWord)
            {
                continue;
            }

            TryDecodeSector(track, offset, expectedTrackNumber, diskData, decodedSectors, ref decodedSectorCount);
        }
    }

    private static void TryDecodeSector(
        AmigaEncodedTrack track,
        int syncOffset,
        int expectedTrackNumber,
        byte[] diskData,
        bool[] decodedSectors,
        ref int decodedSectorCount)
    {
        var header = DecodeOddEven(ReadMfmLong(track, syncOffset + (0x04 * 8)), ReadMfmLong(track, syncOffset + (0x08 * 8)));
        if ((header & 0xFF00_0000) != 0xFF00_0000)
        {
            return;
        }

        var trackNumber = (int)((header >> 16) & 0xFF);
        var sector = (int)((header >> 8) & 0xFF);
        if (trackNumber != expectedTrackNumber ||
            (uint)trackNumber >= TrackCount ||
            (uint)sector >= AmigaDiskGeometry.SectorsPerTrack)
        {
            return;
        }

        var decodedHeaderChecksum = DecodeOddEven(
            ReadMfmLong(track, syncOffset + (0x2C * 8)),
            ReadMfmLong(track, syncOffset + (0x30 * 8)));
        var calculatedHeaderChecksum = ComputeMfmChecksum(track, syncOffset + (0x04 * 8), 10);
        if ((decodedHeaderChecksum & MfmDataMask) != calculatedHeaderChecksum)
        {
            return;
        }

        var decodedDataChecksum = DecodeOddEven(
            ReadMfmLong(track, syncOffset + (0x34 * 8)),
            ReadMfmLong(track, syncOffset + (0x38 * 8)));
        var calculatedDataChecksum = ComputeMfmChecksum(track, syncOffset + (0x3C * 8), 256);
        if ((decodedDataChecksum & MfmDataMask) != calculatedDataChecksum)
        {
            return;
        }

        var logicalSector = (expectedTrackNumber * AmigaDiskGeometry.SectorsPerTrack) + sector;
        var dataOffset = logicalSector * AmigaDiskGeometry.SectorSize;
        var oddDataOffset = syncOffset + (0x3C * 8);
        var evenDataOffset = oddDataOffset + (128 * 32);
        for (var longIndex = 0; longIndex < 128; longIndex++)
        {
            var decoded = DecodeOddEven(
                ReadMfmLong(track, oddDataOffset + (longIndex * 32)),
                ReadMfmLong(track, evenDataOffset + (longIndex * 32)));
            BigEndian.WriteUInt32(diskData.AsSpan(dataOffset + (longIndex * 4), 4), 0, decoded);
        }

        if (!decodedSectors[logicalSector])
        {
            decodedSectors[logicalSector] = true;
            decodedSectorCount++;
        }
    }

    private static void TryDecodeSector(
        AmigaEncodedTrack track,
        int syncOffset,
        int expectedTrackNumber,
        Span<byte> trackData,
        Span<bool> decodedSectors,
        ref int decodedSectorCount)
    {
        var header = DecodeOddEven(ReadMfmLong(track, syncOffset + (0x04 * 8)), ReadMfmLong(track, syncOffset + (0x08 * 8)));
        if ((header & 0xFF00_0000) != 0xFF00_0000)
        {
            return;
        }

        var trackNumber = (int)((header >> 16) & 0xFF);
        var sector = (int)((header >> 8) & 0xFF);
        if (trackNumber != expectedTrackNumber ||
            (uint)sector >= AmigaDiskGeometry.SectorsPerTrack)
        {
            return;
        }

        var decodedHeaderChecksum = DecodeOddEven(
            ReadMfmLong(track, syncOffset + (0x2C * 8)),
            ReadMfmLong(track, syncOffset + (0x30 * 8)));
        var calculatedHeaderChecksum = ComputeMfmChecksum(track, syncOffset + (0x04 * 8), 10);
        if ((decodedHeaderChecksum & MfmDataMask) != calculatedHeaderChecksum)
        {
            return;
        }

        var decodedDataChecksum = DecodeOddEven(
            ReadMfmLong(track, syncOffset + (0x34 * 8)),
            ReadMfmLong(track, syncOffset + (0x38 * 8)));
        var calculatedDataChecksum = ComputeMfmChecksum(track, syncOffset + (0x3C * 8), 256);
        if ((decodedDataChecksum & MfmDataMask) != calculatedDataChecksum)
        {
            return;
        }

        var dataOffset = sector * AmigaDiskGeometry.SectorSize;
        var oddDataOffset = syncOffset + (0x3C * 8);
        var evenDataOffset = oddDataOffset + (128 * 32);
        for (var longIndex = 0; longIndex < 128; longIndex++)
        {
            var decoded = DecodeOddEven(
                ReadMfmLong(track, oddDataOffset + (longIndex * 32)),
                ReadMfmLong(track, evenDataOffset + (longIndex * 32)));
            BigEndian.WriteUInt32(trackData.Slice(dataOffset + (longIndex * 4), 4), 0, decoded);
        }

        if (!decodedSectors[sector])
        {
            decodedSectors[sector] = true;
            decodedSectorCount++;
        }
    }

    private static uint ComputeMfmChecksum(AmigaEncodedTrack track, int bitOffset, int longCount)
    {
        var checksum = 0u;
        for (var index = 0; index < longCount; index++)
        {
            checksum ^= ReadMfmLong(track, bitOffset + (index * 32));
        }

        return checksum & MfmDataMask;
    }

    private static uint ReadMfmLong(AmigaEncodedTrack track, int bitOffset)
    {
        return track.ReadUInt32(bitOffset) & MfmDataMask;
    }

    private static uint DecodeOddEven(uint odd, uint even)
    {
        return ((odd & MfmDataMask) << 1) | (even & MfmDataMask);
    }

    private static AmigaEncodedTrack ToEncodedTrack(IAmigaTrack track)
    {
        return track is AmigaEncodedTrack encoded
            ? encoded
            : new AmigaEncodedTrack(track.Data, track.BitLength, track.StartBit, track.Features);
    }
}
