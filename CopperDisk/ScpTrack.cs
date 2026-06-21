using System;
using System.Collections.Generic;

namespace CopperDisk;

/// <summary>
/// A single decoded SuperCard Pro track.
/// </summary>
public sealed class ScpTrack : IAmigaTrack
{
    internal ScpTrack(
        int cylinder,
        int head,
        int bitLength,
        int startBit,
        byte[] encodedData,
        AmigaTrackFeatures features,
        IReadOnlyList<AmigaTrackRegion>? regions)
    {
        Cylinder = cylinder;
        Head = head;
        BitLength = bitLength;
        StartBit = startBit;
        EncodedData = encodedData;
        Features = features;
        Regions = regions == null || regions.Count == 0
            ? Array.Empty<AmigaTrackRegion>()
            : Array.AsReadOnly(new List<AmigaTrackRegion>(regions).ToArray());
    }

    /// <summary>
    /// Gets the physical cylinder number.
    /// </summary>
    public int Cylinder { get; }

    /// <summary>
    /// Gets the physical head number.
    /// </summary>
    public int Head { get; }

    /// <summary>
    /// Gets the number of meaningful bits in <see cref="EncodedData"/>.
    /// </summary>
    public int BitLength { get; }

    /// <summary>
    /// Gets the decoded stream start bit.
    /// </summary>
    public int StartBit { get; }

    /// <summary>
    /// Gets the decoded encoded-track bytes.
    /// </summary>
    public ReadOnlyMemory<byte> EncodedData { get; }

    /// <summary>
    /// Gets feature flags exposed by the decoder for this track.
    /// </summary>
    public AmigaTrackFeatures Features { get; }

    /// <summary>
    /// Gets feature-marked bit ranges exposed by the decoder for this track.
    /// </summary>
    public IReadOnlyList<AmigaTrackRegion> Regions { get; }
}
