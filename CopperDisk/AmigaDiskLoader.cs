using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CopperDisk;

/// <summary>
/// Loads Amiga ADF, ADZ, DMS, IPF, and ZIP-wrapped disk images into CopperDisk media objects.
/// </summary>
/// <remarks>
/// Loader methods are intended for package consumers that need emulator-ready media rather than a filesystem-level
/// view. Byte-array entry points take ownership of the supplied arrays and do not defensively copy them.
/// </remarks>
public static class AmigaDiskLoader
{
    /// <summary>
    /// Loads an ADF, ADZ, DMS, IPF, or ZIP file containing exactly one supported disk image.
    /// </summary>
    /// <param name="path">The disk image path.</param>
    /// <param name="ipfOptions">Optional IPF decode options used for direct or ZIP-wrapped IPF images.</param>
    /// <returns>The loaded media and display name.</returns>
    public static AmigaDiskLoadResult Load(string path, IpfDecodeOptions? ipfOptions = null)
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

        if (extension.Equals(".adz", StringComparison.OrdinalIgnoreCase))
        {
            return new AmigaDiskLoadResult(FromAdzBytes(File.ReadAllBytes(path)), Path.GetFileName(path));
        }

        if (extension.Equals(".dms", StringComparison.OrdinalIgnoreCase))
        {
            return new AmigaDiskLoadResult(FromDmsBytes(File.ReadAllBytes(path)), Path.GetFileName(path));
        }

        if (extension.Equals(".ipf", StringComparison.OrdinalIgnoreCase))
        {
            return new AmigaDiskLoadResult(FromIpfBytes(File.ReadAllBytes(path), ipfOptions), Path.GetFileName(path));
        }

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            var entries = archive.Entries
                .Where(entry =>
                    !string.IsNullOrEmpty(entry.Name) &&
                    IsSupportedDiskEntryName(entry.Name))
                .ToArray();
            if (entries.Length != 1)
            {
                throw new AmigaDiskException("A zipped disk image must contain exactly one ADF, ADZ, DMS, or IPF file.");
            }

            using var stream = entries[0].Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();
            var media = LoadBytesByExtension(entries[0].Name, data, ipfOptions);
            return new AmigaDiskLoadResult(media, entries[0].Name);
        }

        throw new AmigaDiskException("Unsupported disk image extension. Expected .adf, .adz, .dms, .ipf, or .zip.");
    }

    /// <summary>
    /// Creates writable sector media from a standard ADF sector image.
    /// </summary>
    /// <param name="ownedData">The 880 KiB ADF image bytes. CopperDisk takes ownership and callers must not mutate the array while the media is in use.</param>
    /// <returns>Writable Amiga sector media backed by <paramref name="ownedData"/>.</returns>
    public static IWritableAmigaSectorDiskMedia FromAdfBytes(byte[] ownedData)
    {
        return new AdfDiskMedia(ownedData ?? throw new ArgumentNullException(nameof(ownedData)));
    }

    /// <summary>
    /// Decompresses a gzip-compressed ADF image into read-only sector media.
    /// </summary>
    /// <param name="adzImage">The ADZ image bytes.</param>
    /// <returns>Read-only Amiga sector media backed by the decompressed ADF image.</returns>
    public static IAmigaSectorDiskMedia FromAdzBytes(ReadOnlySpan<byte> adzImage)
    {
        try
        {
            using var input = new MemoryStream(adzImage.ToArray(), writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(AmigaDiskGeometry.StandardAdfSize);
            gzip.CopyTo(output);
            return new ReadOnlyAdfDiskMedia(output.ToArray());
        }
        catch (InvalidDataException ex)
        {
            throw new AmigaDiskException($"Unable to decode ADZ disk image: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decodes a DMS disk image into read-only sector media.
    /// </summary>
    /// <param name="dmsImage">The DMS image bytes.</param>
    /// <returns>Read-only Amiga sector media backed by the decoded ADF image.</returns>
    public static IAmigaSectorDiskMedia FromDmsBytes(ReadOnlySpan<byte> dmsImage)
    {
        return new ReadOnlyAdfDiskMedia(DmsDecoder.Decode(dmsImage));
    }

    /// <summary>
    /// Decodes an IPF image into sector media with preserved encoded track streams.
    /// </summary>
    /// <param name="ipfImage">The IPF image bytes. The input is read during decoding and is not retained by the returned media.</param>
    /// <param name="options">Optional IPF decode options.</param>
    /// <returns>Read-only Amiga sector media backed by decoded IPF tracks.</returns>
    public static IAmigaSectorDiskMedia FromIpfBytes(ReadOnlySpan<byte> ipfImage, IpfDecodeOptions? options = null)
    {
        try
        {
            var ipf = IpfDecoder.Decode(ipfImage, options);
            var tracks = CreateUnformattedTrackSet();
            foreach (var track in ipf.Tracks)
            {
                if ((uint)track.Cylinder >= AmigaDiskGeometry.CylinderCount ||
                    (uint)track.Head >= AmigaDiskGeometry.HeadCount)
                {
                    continue;
                }

                tracks[(track.Cylinder * AmigaDiskGeometry.HeadCount) + track.Head] = new AmigaEncodedTrack(
                    track.EncodedData,
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

    /// <summary>
    /// Creates sector media from encoded track streams.
    /// </summary>
    /// <param name="tracks">Exactly <see cref="AmigaDiskGeometry.TrackCount"/> encoded tracks. Track data is retained as read-only media backing.</param>
    /// <returns>Read-only Amiga sector media backed by the supplied tracks.</returns>
    public static IAmigaSectorDiskMedia FromEncodedTracks(IReadOnlyList<IAmigaTrack> tracks)
    {
        return new TrackBackedDiskMedia(tracks);
    }

    /// <summary>
    /// Creates sector media from encoded track streams.
    /// </summary>
    /// <param name="tracks">Exactly <see cref="AmigaDiskGeometry.TrackCount"/> encoded tracks. Track data is retained as read-only media backing.</param>
    /// <returns>Read-only Amiga sector media backed by the supplied tracks.</returns>
    public static IAmigaSectorDiskMedia FromEncodedTracks(IReadOnlyList<AmigaEncodedTrack> tracks)
    {
        return new TrackBackedDiskMedia(tracks);
    }

    /// <summary>
    /// Creates sector media from owned encoded track byte arrays.
    /// </summary>
    /// <param name="ownedTrackBytes">
    /// Exactly <see cref="AmigaDiskGeometry.TrackCount"/> encoded tracks. CopperDisk takes ownership of non-null arrays;
    /// callers must not mutate them while the media is in use. Null entries are treated as unformatted tracks.
    /// </param>
    /// <returns>Read-only Amiga sector media backed by the supplied track bytes.</returns>
    public static IAmigaSectorDiskMedia FromEncodedTrackBytes(IReadOnlyList<byte[]?> ownedTrackBytes)
    {
        ArgumentNullException.ThrowIfNull(ownedTrackBytes);
        var tracks = new AmigaEncodedTrack[ownedTrackBytes.Count];
        for (var index = 0; index < ownedTrackBytes.Count; index++)
        {
            tracks[index] = AmigaEncodedTrack.FromBytes(ownedTrackBytes[index] ?? AmigaDosTrackEncoder.CreateUnformattedTrack());
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

    private static bool IsSupportedDiskEntryName(string name)
        => name.EndsWith(".adf", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".adz", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".dms", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase);

    private static IAmigaDiskMedia LoadBytesByExtension(string name, byte[] data, IpfDecodeOptions? ipfOptions)
    {
        var extension = Path.GetExtension(name);
        if (extension.Equals(".adf", StringComparison.OrdinalIgnoreCase))
        {
            return FromAdfBytes(data);
        }

        if (extension.Equals(".adz", StringComparison.OrdinalIgnoreCase))
        {
            return FromAdzBytes(data);
        }

        if (extension.Equals(".dms", StringComparison.OrdinalIgnoreCase))
        {
            return FromDmsBytes(data);
        }

        if (extension.Equals(".ipf", StringComparison.OrdinalIgnoreCase))
        {
            return FromIpfBytes(data, ipfOptions);
        }

        throw new AmigaDiskException("Unsupported disk image extension. Expected .adf, .adz, .dms, .ipf, or .zip.");
    }
}
