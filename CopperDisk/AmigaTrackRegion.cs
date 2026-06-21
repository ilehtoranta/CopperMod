using System;

namespace CopperDisk;

/// <summary>
/// Describes a feature-marked bit range inside an encoded Amiga track stream.
/// </summary>
public readonly struct AmigaTrackRegion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AmigaTrackRegion"/> struct.
    /// </summary>
    /// <param name="startBit">The zero-based start bit inside the track.</param>
    /// <param name="bitLength">The number of bits covered by the region.</param>
    /// <param name="features">The feature flags that apply to the region.</param>
    public AmigaTrackRegion(int startBit, int bitLength, AmigaTrackFeatures features)
    {
        if (startBit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startBit));
        }

        if (bitLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength));
        }

        StartBit = startBit;
        BitLength = bitLength;
        Features = features;
    }

    /// <summary>
    /// Gets the zero-based start bit inside the track.
    /// </summary>
    public int StartBit { get; }

    /// <summary>
    /// Gets the number of bits covered by the region.
    /// </summary>
    public int BitLength { get; }

    /// <summary>
    /// Gets the feature flags that apply to the region.
    /// </summary>
    public AmigaTrackFeatures Features { get; }
}
