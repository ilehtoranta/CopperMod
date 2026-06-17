using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CopperDisk;

/// <summary>
/// Loads Amiga ADF, IPF, and ZIP-wrapped disk images into CopperDisk media objects.
/// </summary>
/// <remarks>
/// Loader methods are intended for package consumers that need emulator-ready media rather than a filesystem-level
/// view. Byte-array entry points take ownership of the supplied arrays and do not defensively copy them.
/// </remarks>
public static class AmigaDiskLoader
{
    /// <summary>
    /// Loads an ADF, IPF, or ZIP file containing exactly one ADF or IPF image.
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
                ? FromIpfBytes(data, ipfOptions)
                : FromAdfBytes(data);
            return new AmigaDiskLoadResult(media, entries[0].Name);
        }

        throw new AmigaDiskException("Unsupported disk image extension. Expected .adf, .ipf, or .zip.");
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
}
