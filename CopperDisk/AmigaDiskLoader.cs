using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CopperDisk;

public static class AmigaDiskLoader
{
    public static AmigaDiskLoadResult Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A disk image path is required.", nameof(path));
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".adf", StringComparison.OrdinalIgnoreCase))
        {
            return new AmigaDiskLoadResult(FromAdfBytes(File.ReadAllBytes(path)), Path.GetFileName(path));
        }

        if (extension.Equals(".ipf", StringComparison.OrdinalIgnoreCase))
        {
            return new AmigaDiskLoadResult(FromIpfBytes(File.ReadAllBytes(path)), Path.GetFileName(path));
        }

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            var entries = archive.Entries
                .Where(entry =>
                    !string.IsNullOrEmpty(entry.Name) &&
                    (entry.Name.EndsWith(".adf", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (entries.Length != 1)
            {
                throw new AmigaDiskException("A zipped disk image must contain exactly one ADF or IPF file.");
            }

            using var stream = entries[0].Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();
            var media = entries[0].Name.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase)
                ? FromIpfBytes(data)
                : FromAdfBytes(data);
            return new AmigaDiskLoadResult(media, entries[0].Name);
        }

        throw new AmigaDiskException("Unsupported disk image extension. Expected .adf, .ipf, or .zip.");
    }

    public static IAmigaDiskMedia FromAdfBytes(byte[] data)
    {
        return new AdfDiskMedia(data ?? throw new ArgumentNullException(nameof(data)));
    }

    public static IAmigaDiskMedia FromIpfBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            var ipf = IpfDecoder.Decode(data);
            var tracks = CreateUnformattedTrackSet();
            foreach (var track in ipf.Tracks)
            {
                if ((uint)track.Cylinder >= AmigaDiskGeometry.CylinderCount ||
                    (uint)track.Head >= AmigaDiskGeometry.HeadCount)
                {
                    continue;
                }

                tracks[(track.Cylinder * AmigaDiskGeometry.HeadCount) + track.Head] = new AmigaEncodedTrack(
                    track.Data,
                    track.BitLength,
                    track.StartBit,
                    track.Features);
            }

            return FromEncodedTracks(tracks);
        }
        catch (IpfDecodeException ex)
        {
            throw new AmigaDiskException($"Unable to decode IPF disk image: {ex.Message}", ex);
        }
    }

    public static IAmigaDiskMedia FromEncodedTracks(IReadOnlyList<IAmigaTrack> tracks)
    {
        return new TrackBackedDiskMedia(tracks);
    }

    public static IAmigaDiskMedia FromEncodedTracks(IReadOnlyList<AmigaEncodedTrack> tracks)
    {
        return new TrackBackedDiskMedia(tracks);
    }

    public static IAmigaDiskMedia FromEncodedTracks(byte[][] encodedTracks)
    {
        ArgumentNullException.ThrowIfNull(encodedTracks);
        var tracks = new AmigaEncodedTrack[encodedTracks.Length];
        for (var index = 0; index < encodedTracks.Length; index++)
        {
            tracks[index] = AmigaEncodedTrack.FromBytes(encodedTracks[index] ?? AmigaDosTrackEncoder.CreateUnformattedTrack());
        }

        return FromEncodedTracks(tracks);
    }

    internal static AmigaEncodedTrack[] CreateUnformattedTrackSet()
    {
        var tracks = new AmigaEncodedTrack[AmigaDiskGeometry.TrackCount];
        for (var index = 0; index < tracks.Length; index++)
        {
            tracks[index] = AmigaEncodedTrack.FromBytes(AmigaDosTrackEncoder.CreateUnformattedTrack());
        }

        return tracks;
    }
}
