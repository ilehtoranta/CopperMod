using System;

namespace CopperDisk;

public readonly struct AmigaEncodedTrack : IAmigaTrack
{
    public AmigaEncodedTrack(ReadOnlyMemory<byte> data, int bitLength)
        : this(data, bitLength, startBit: 0, AmigaTrackFeatures.None)
    {
    }

    public AmigaEncodedTrack(
        ReadOnlyMemory<byte> data,
        int bitLength,
        int startBit,
        AmigaTrackFeatures features = AmigaTrackFeatures.None)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("Encoded track data must not be empty.", nameof(data));
        }

        if (bitLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length must be positive.");
        }

        if (bitLength > checked(data.Length * 8))
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length cannot exceed the backing data.");
        }

        if (startBit < 0 || startBit >= bitLength)
        {
            throw new ArgumentOutOfRangeException(nameof(startBit), startBit, "Encoded track start bit must be inside the track.");
        }

        Data = data;
        BitLength = bitLength;
        StartBit = startBit;
        Features = features;
    }

    public ReadOnlyMemory<byte> Data { get; }

    public int BitLength { get; }

    public int StartBit { get; }

    public AmigaTrackFeatures Features { get; }

    public int ByteLength => (BitLength + 7) / 8;

    public ReadOnlySpan<byte> Span => Data.Span;

    public static AmigaEncodedTrack FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new AmigaEncodedTrack(data, checked(data.Length * 8));
    }

    public byte ReadByte(int bitOffset)
    {
        return (byte)ReadBits(bitOffset, 8);
    }

    public ushort ReadUInt16(int bitOffset)
    {
        return (ushort)ReadBits(bitOffset, 16);
    }

    public uint ReadUInt32(int bitOffset)
    {
        return (uint)ReadBits(bitOffset, 32);
    }

    public static int Mod(int value, int divisor)
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

        var span = Data.Span;
        bitOffset = Mod(bitOffset, BitLength);
        var value = 0ul;
        for (var bit = 0; bit < bitCount; bit++)
        {
            var trackBit = (bitOffset + bit) % BitLength;
            var dataBit = (span[trackBit >> 3] >> (7 - (trackBit & 7))) & 1;
            value = (value << 1) | (uint)dataBit;
        }

        return value;
    }
}
