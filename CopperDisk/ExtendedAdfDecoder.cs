namespace CopperDisk;

// Modern UAE extended ADF ("UAE-1ADF") layout compatibility reference:
// WinUAE disk.cpp and https://github.com/dirkwhoffmann/vAmiga/issues/440.
internal static class ExtendedAdfDecoder
{
    private const int MagicLength = 8;
    private const int HeaderLength = 12;
    private const int TrackHeaderLength = 12;
    private const int NormalTrackDataLength = AmigaDiskGeometry.SectorsPerTrack * AmigaDiskGeometry.SectorSize;
    private const int MaxDoubleDensityRawTrackBytes = 20_000;

    public static bool IsModernExtendedAdf(ReadOnlySpan<byte> image)
        => HasMagic(image, (byte)'U', (byte)'A', (byte)'E', (byte)'-', (byte)'1', (byte)'A', (byte)'D', (byte)'F');

    public static bool IsOldExtendedAdf(ReadOnlySpan<byte> image)
        => HasMagic(image, (byte)'U', (byte)'A', (byte)'E', (byte)'-', (byte)'-', (byte)'A', (byte)'D', (byte)'F');

    public static AmigaEncodedTrack[] Decode(ReadOnlySpan<byte> image)
    {
        if (image.Length < MagicLength)
        {
            throw Error("File is too short to contain an extended ADF header.");
        }

        if (IsOldExtendedAdf(image))
        {
            throw Error("Old UAE--ADF extended ADF images are not supported.");
        }

        if (!IsModernExtendedAdf(image))
        {
            throw Error("File header does not start with UAE-1ADF.");
        }

        if (image.Length < HeaderLength)
        {
            throw Error("File is too short to contain the UAE-1ADF track count.");
        }

        var trackCount = BigEndian.ReadUInt16(image, 10, "track count");
        if (trackCount != AmigaDiskGeometry.TrackCount)
        {
            throw Error($"Only standard DD UAE-1ADF images with {AmigaDiskGeometry.TrackCount} tracks are supported.");
        }

        var tableLength = HeaderLength + (trackCount * TrackHeaderLength);
        if (image.Length < tableLength)
        {
            throw Error("The UAE-1ADF track table is truncated.");
        }

        var entries = new TrackEntry[trackCount];
        var dataOffset = tableLength;
        for (var track = 0; track < trackCount; track++)
        {
            var headerOffset = HeaderLength + (track * TrackHeaderLength);
            var typeWord = BigEndian.ReadUInt16(image, headerOffset + 2, "track type");
            var revolutions = (typeWord >> 8) + 1;
            var type = typeWord & 0xFF;
            var availableBytes = BigEndian.ReadUInt32(image, headerOffset + 4, "track byte length");
            var bitLength = BigEndian.ReadUInt32(image, headerOffset + 8, "track bit length");

            if (revolutions != 1)
            {
                throw Error($"Track {track} uses {revolutions} revolutions; multirevolution UAE-1ADF tracks are not supported.");
            }

            if ((availableBytes & 1) != 0)
            {
                throw Error($"Track {track} has an odd byte length.");
            }

            if (availableBytes > int.MaxValue || bitLength > int.MaxValue)
            {
                throw Error($"Track {track} is too large.");
            }

            if (availableBytes > MaxDoubleDensityRawTrackBytes)
            {
                throw Error($"Track {track} is larger than a standard double-density track.");
            }

            var trackBytes = checked((int)availableBytes);
            var trackBits = checked((int)bitLength);
            if ((long)trackBits > (long)trackBytes * 8)
            {
                throw Error($"Track {track} bit length exceeds its backing bytes.");
            }

            if (type == 0)
            {
                if (trackBytes != NormalTrackDataLength)
                {
                    throw Error($"Track {track} has {trackBytes} normal-track bytes; expected {NormalTrackDataLength}.");
                }

                if (trackBits != 0 && trackBits != NormalTrackDataLength * 8)
                {
                    throw Error($"Track {track} has an invalid normal-track bit length.");
                }
            }
            else if (type == 1)
            {
                if (trackBytes == 0 || trackBits == 0)
                {
                    throw Error($"Raw MFM track {track} must have non-empty data and a positive bit length.");
                }
            }
            else
            {
                throw Error($"Unsupported UAE-1ADF track type {type} on track {track}.");
            }

            if ((long)dataOffset + trackBytes > image.Length)
            {
                throw Error($"Track {track} data extends past the end of the image.");
            }

            entries[track] = new TrackEntry(type, dataOffset, trackBytes, trackBits);
            dataOffset += trackBytes;
        }

        if (dataOffset != image.Length)
        {
            throw Error("The image contains trailing bytes after the declared track data.");
        }

        return DecodeTracks(image, entries);
    }

    private static AmigaEncodedTrack[] DecodeTracks(ReadOnlySpan<byte> image, TrackEntry[] entries)
    {
        var sectorData = new byte[AmigaDiskGeometry.StandardAdfSize];
        for (var track = 0; track < entries.Length; track++)
        {
            var entry = entries[track];
            if (entry.Type == 0)
            {
                image.Slice(entry.Offset, entry.ByteLength).CopyTo(
                    sectorData.AsSpan(track * NormalTrackDataLength, NormalTrackDataLength));
            }
        }

        var sectorMedia = new AdfDiskMedia(sectorData);
        var tracks = new AmigaEncodedTrack[entries.Length];
        for (var track = 0; track < entries.Length; track++)
        {
            var entry = entries[track];
            if (entry.Type == 0)
            {
                var cylinder = track / AmigaDiskGeometry.HeadCount;
                var head = track % AmigaDiskGeometry.HeadCount;
                tracks[track] = ToEncodedTrack(sectorMedia.ReadTrack(cylinder, head));
            }
            else
            {
                tracks[track] = new AmigaEncodedTrack(
                    image.Slice(entry.Offset, entry.ByteLength).ToArray(),
                    entry.BitLength,
                    startBit: 0,
                    AmigaTrackFeatures.PreservedTrackData);
            }
        }

        return tracks;
    }

    private static AmigaEncodedTrack ToEncodedTrack(IAmigaTrack track)
        => track is AmigaEncodedTrack encoded
            ? encoded
            : new AmigaEncodedTrack(track.EncodedData, track.BitLength, track.StartBit, track.Features);

    private static bool HasMagic(ReadOnlySpan<byte> image, byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7)
        => image.Length >= MagicLength &&
            image[0] == b0 &&
            image[1] == b1 &&
            image[2] == b2 &&
            image[3] == b3 &&
            image[4] == b4 &&
            image[5] == b5 &&
            image[6] == b6 &&
            image[7] == b7;

    private static AmigaDiskException Error(string message)
        => new("Unable to decode extended ADF disk image: " + message);

    private readonly record struct TrackEntry(int Type, int Offset, int ByteLength, int BitLength);
}
