using System;

namespace CopperDisk;

/// <summary>
/// A single decoded floppy track.
/// </summary>
public sealed class IpfTrack : IAmigaTrack
{
    internal IpfTrack(int cylinder, int head, int bitLength, int startBit, byte[] data, AmigaTrackFeatures features)
    {
        Cylinder = cylinder;
        Head = head;
        BitLength = bitLength;
        StartBit = startBit;
        Data = data;
        Features = features;
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
    /// Gets the number of meaningful bits in <see cref="Data"/>.
    /// </summary>
    public int BitLength { get; }

    /// <summary>
    /// Gets the decoded stream start bit.
    /// </summary>
    public int StartBit { get; }

    /// <summary>
    /// Gets the decoded track bytes.
    /// </summary>
    public byte[] Data { get; }

    ReadOnlyMemory<byte> IAmigaTrack.Data => Data;

    /// <summary>
    /// Gets feature flags exposed by the decoder for this track.
    /// </summary>
    public AmigaTrackFeatures Features { get; }
}
