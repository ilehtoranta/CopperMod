using System;
using System.Collections.Generic;
using System.Linq;

namespace CopperDisk;

/// <summary>
/// Represents a byte-backed encoded Amiga track stream.
/// </summary>
/// <remarks>
/// The struct stores a read-only memory view over caller-supplied backing data. It does not copy the data; callers must
/// keep the backing bytes stable while the track is in use.
/// </remarks>
public readonly struct AmigaEncodedTrack : IAmigaTrack
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AmigaEncodedTrack"/> struct.
    /// </summary>
    /// <param name="encodedData">The encoded track bytes. The caller owns this memory and must not mutate it while the track is in use.</param>
    /// <param name="bitLength">The number of meaningful bits in <paramref name="encodedData"/>.</param>
    public AmigaEncodedTrack(ReadOnlyMemory<byte> encodedData, int bitLength)
        : this(encodedData, bitLength, startBit: 0, AmigaTrackFeatures.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AmigaEncodedTrack"/> struct.
    /// </summary>
    /// <param name="encodedData">The encoded track bytes. The caller owns this memory and must not mutate it while the track is in use.</param>
    /// <param name="bitLength">The number of meaningful bits in <paramref name="encodedData"/>.</param>
    /// <param name="startBit">The bit position corresponding to the physical index position.</param>
    /// <param name="features">Feature flags for the track stream.</param>
    public AmigaEncodedTrack(
        ReadOnlyMemory<byte> encodedData,
        int bitLength,
        int startBit,
        AmigaTrackFeatures features = AmigaTrackFeatures.None)
        : this(encodedData, bitLength, startBit, features, regions: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AmigaEncodedTrack"/> struct.
    /// </summary>
    /// <param name="encodedData">The encoded track bytes. The caller owns this memory and must not mutate it while the track is in use.</param>
    /// <param name="bitLength">The number of meaningful bits in <paramref name="encodedData"/>.</param>
    /// <param name="startBit">The bit position corresponding to the physical index position.</param>
    /// <param name="features">Feature flags for the track stream.</param>
    /// <param name="regions">Feature-marked bit ranges inside the track stream.</param>
    public AmigaEncodedTrack(
        ReadOnlyMemory<byte> encodedData,
        int bitLength,
        int startBit,
        AmigaTrackFeatures features,
        IReadOnlyList<AmigaTrackRegion>? regions)
    {
        if (encodedData.IsEmpty)
        {
            throw new ArgumentException("Encoded track data must not be empty.", nameof(encodedData));
        }

        if (bitLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length must be positive.");
        }

        if (bitLength > checked(encodedData.Length * 8))
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length cannot exceed the backing data.");
        }

        if (startBit < 0 || startBit >= bitLength)
        {
            throw new ArgumentOutOfRangeException(nameof(startBit), startBit, "Encoded track start bit must be inside the track.");
        }

        EncodedData = encodedData;
        BitLength = bitLength;
        StartBit = startBit;
        Features = features;
        Regions = NormalizeRegions(regions, bitLength);
    }

    /// <summary>
    /// Gets the encoded track bytes. Only the first <see cref="BitLength"/> bits are meaningful.
    /// </summary>
    public ReadOnlyMemory<byte> EncodedData { get; }

    /// <summary>
    /// Gets the number of meaningful bits in <see cref="EncodedData"/>.
    /// </summary>
    public int BitLength { get; }

    /// <summary>
    /// Gets the bit position corresponding to the physical index position.
    /// </summary>
    public int StartBit { get; }

    /// <summary>
    /// Gets feature flags for the track stream.
    /// </summary>
    public AmigaTrackFeatures Features { get; }

    /// <summary>
    /// Gets feature-marked bit ranges inside the encoded stream.
    /// </summary>
    public IReadOnlyList<AmigaTrackRegion> Regions { get; }

    /// <summary>
    /// Gets the number of bytes that contain meaningful encoded bits.
    /// </summary>
    public int ByteLength => (BitLength + 7) / 8;

    /// <summary>
    /// Gets a span over the encoded track bytes.
    /// </summary>
    /// <remarks>Only the first <see cref="BitLength"/> bits are meaningful.</remarks>
    public ReadOnlySpan<byte> EncodedSpan => EncodedData.Span;

    /// <summary>
    /// Creates an encoded track whose full byte array is meaningful.
    /// </summary>
    /// <param name="ownedData">The encoded track bytes. CopperDisk takes ownership and callers must not mutate it while the track is in use.</param>
    /// <returns>An encoded track backed by <paramref name="ownedData"/>.</returns>
    public static AmigaEncodedTrack FromBytes(byte[] ownedData)
    {
        ArgumentNullException.ThrowIfNull(ownedData);
        return new AmigaEncodedTrack(ownedData, checked(ownedData.Length * 8));
    }

    /// <summary>
    /// Reads eight bits at a bit offset, wrapping at the end of the track.
    /// </summary>
    /// <param name="bitOffset">The bit offset to read from.</param>
    /// <returns>The decoded byte.</returns>
    public byte ReadByteAtBit(int bitOffset)
    {
        return (byte)ReadBits(bitOffset, 8);
    }

    /// <summary>
    /// Reads sixteen bits at a bit offset, wrapping at the end of the track.
    /// </summary>
    /// <param name="bitOffset">The bit offset to read from.</param>
    /// <returns>The decoded big-endian 16-bit value.</returns>
    public ushort ReadUInt16AtBit(int bitOffset)
    {
        return (ushort)ReadBits(bitOffset, 16);
    }

    /// <summary>
    /// Reads thirty-two bits at a bit offset, wrapping at the end of the track.
    /// </summary>
    /// <param name="bitOffset">The bit offset to read from.</param>
    /// <returns>The decoded big-endian 32-bit value.</returns>
    public uint ReadUInt32AtBit(int bitOffset)
    {
        return (uint)ReadBits(bitOffset, 32);
    }

    /// <summary>
    /// Wraps a bit offset into a positive range.
    /// </summary>
    /// <param name="value">The bit offset to wrap.</param>
    /// <param name="divisor">The positive wrap length.</param>
    /// <returns>The wrapped offset.</returns>
    public static int WrapBitOffset(int value, int divisor)
    {
        if (divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(divisor));
        }

        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private ulong ReadBits(int bitOffset, int bitCount)
    {
        if (bitCount is < 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        }

        if (BitLength <= 0)
        {
            throw new InvalidOperationException("Cannot read from an empty encoded track.");
        }

        var span = EncodedData.Span;
        bitOffset = WrapBitOffset(bitOffset, BitLength);
        var value = 0ul;
        for (var bit = 0; bit < bitCount; bit++)
        {
            var trackBit = (bitOffset + bit) % BitLength;
            var dataBit = (span[trackBit >> 3] >> (7 - (trackBit & 7))) & 1;
            value = (value << 1) | (uint)dataBit;
        }

        return value;
    }

    private static IReadOnlyList<AmigaTrackRegion> NormalizeRegions(IReadOnlyList<AmigaTrackRegion>? regions, int trackBitLength)
    {
        if (regions == null || regions.Count == 0)
        {
            return Array.Empty<AmigaTrackRegion>();
        }

        var normalized = regions.ToArray();
        for (var index = 0; index < normalized.Length; index++)
        {
            var region = normalized[index];
            if (region.StartBit >= trackBitLength ||
                region.BitLength > trackBitLength - region.StartBit)
            {
                throw new ArgumentOutOfRangeException(nameof(regions), "Track regions must be fully inside the encoded track.");
            }
        }

        return Array.AsReadOnly(normalized);
    }
}
