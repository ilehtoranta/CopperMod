using System;

namespace CopperDisk;

/// <summary>
/// A single decoded IPF floppy track.
/// </summary>
public sealed class IpfTrack : IAmigaTrack
{
    internal IpfTrack(int cylinder, int head, int bitLength, int startBit, byte[] encodedData, AmigaTrackFeatures features)
    {
        Cylinder = cylinder;
        Head = head;
        BitLength = bitLength;
        StartBit = startBit;
        EncodedData = encodedData;
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
    /// <remarks>The returned memory is a read-only view over decoder-owned backing storage.</remarks>
    public ReadOnlyMemory<byte> EncodedData { get; }

    /// <summary>
    /// Gets feature flags exposed by the decoder for this track.
    /// </summary>
    public AmigaTrackFeatures Features { get; }
}
