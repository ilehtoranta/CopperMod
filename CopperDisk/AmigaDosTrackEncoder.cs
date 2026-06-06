using System;
using System.Linq;

namespace CopperDisk;

public static class AmigaDosTrackEncoder
{
    private const int EncodedSectorBytes = 0x440;
    private const int EncodedTrackGapBytes = 0x140;
    public const int EncodedTrackBytes = (EncodedSectorBytes * AmigaDiskGeometry.SectorsPerTrack) + EncodedTrackGapBytes;
    private const uint MfmDataMask = 0x5555_5555;
    private static readonly int[] PhysicalSectorOrder = { 9, 10, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    public static byte[] EncodeTrack(IAmigaSectorDiskMedia disk, int cylinder, int head)
    {
        ArgumentNullException.ThrowIfNull(disk);
        if (cylinder < 0 || cylinder >= AmigaDiskGeometry.CylinderCount)
        {
            throw new ArgumentOutOfRangeException(nameof(cylinder));
        }

        if (head < 0 || head >= AmigaDiskGeometry.HeadCount)
        {
            throw new ArgumentOutOfRangeException(nameof(head));
        }

        var track = Enumerable.Repeat((byte)0xAA, EncodedTrackBytes).ToArray();
        var trackNumber = (cylinder * AmigaDiskGeometry.HeadCount) + head;
        for (var physicalIndex = 0; physicalIndex < PhysicalSectorOrder.Length; physicalIndex++)
        {
            var sector = PhysicalSectorOrder[physicalIndex];
            var sectorsUntilGap = AmigaDiskGeometry.SectorsPerTrack - physicalIndex;
            EncodeSector(
                disk.ReadSector(cylinder, head, sector).Span,
                track.AsSpan(physicalIndex * EncodedSectorBytes, EncodedSectorBytes),
                trackNumber,
                sector,
                sectorsUntilGap);
        }

        return track;
    }

    public static byte[] CreateUnformattedTrack()
    {
        return Enumerable.Repeat((byte)0xAA, EncodedTrackBytes).ToArray();
    }

    private static void EncodeSector(ReadOnlySpan<byte> source, Span<byte> destination, int trackNumber, int sector, int sectorsUntilGap)
    {
        destination.Fill(0xAA);
        BigEndian.WriteUInt16(destination, 0x04, 0x4489);
        BigEndian.WriteUInt16(destination, 0x06, 0x4489);

        var header = 0xFF00_0000u | ((uint)trackNumber << 16) | ((uint)sector << 8) | (uint)sectorsUntilGap;
        Span<uint> headerAndLabel = stackalloc uint[10];
        WriteOddEvenPair(headerAndLabel, 0, header);

        for (var i = 0; i < 4; i++)
        {
            WriteOddEvenSplit(headerAndLabel, 2 + i, 6 + i, 0);
        }

        Span<uint> data = stackalloc uint[256];
        for (var i = 0; i < source.Length / 4; i++)
        {
            var value = BigEndian.ReadUInt32(source, i * 4, "ADF sector longword");
            WriteOddEvenSplit(data, i, 128 + i, value);
        }

        var headerChecksum = ComputeMfmChecksum(headerAndLabel);
        var dataChecksum = ComputeMfmChecksum(data);
        var previousDataBit = (BigEndian.ReadUInt16(destination, 0x06, "MFM sync word") & 1) != 0;
        WriteMfmLongs(destination, 0x08, headerAndLabel, ref previousDataBit);
        WriteOddEvenPair(destination, 0x30, headerChecksum, ref previousDataBit);
        WriteOddEvenPair(destination, 0x38, dataChecksum, ref previousDataBit);
        WriteMfmLongs(destination, 0x40, data, ref previousDataBit);
    }

    private static void WriteOddEvenPair(Span<uint> destination, int offset, uint value)
    {
        destination[offset] = Odd(value);
        destination[offset + 1] = Even(value);
    }

    private static void WriteOddEvenSplit(Span<uint> destination, int oddOffset, int evenOffset, uint value)
    {
        destination[oddOffset] = Odd(value);
        destination[evenOffset] = Even(value);
    }

    private static void WriteOddEvenPair(Span<byte> destination, int offset, uint value, ref bool previousDataBit)
    {
        WriteMfmLong(destination, offset, Odd(value), ref previousDataBit);
        WriteMfmLong(destination, offset + 4, Even(value), ref previousDataBit);
    }

    private static void WriteMfmLongs(Span<byte> destination, int offset, ReadOnlySpan<uint> values, ref bool previousDataBit)
    {
        for (var i = 0; i < values.Length; i++)
        {
            WriteMfmLong(destination, offset + (i * 4), values[i], ref previousDataBit);
        }
    }

    private static void WriteMfmLong(Span<byte> destination, int offset, uint dataBits, ref bool previousDataBit)
    {
        BigEndian.WriteUInt32(destination, offset, EncodeMfmDataBits(dataBits, ref previousDataBit));
    }

    private static uint EncodeMfmDataBits(uint dataBits, ref bool previousDataBit)
    {
        dataBits &= MfmDataMask;
        var result = dataBits;
        for (var dataBit = 30; dataBit >= 0; dataBit -= 2)
        {
            var currentDataBit = ((dataBits >> dataBit) & 1) != 0;
            if (!previousDataBit && !currentDataBit)
            {
                result |= 1u << (dataBit + 1);
            }

            previousDataBit = currentDataBit;
        }

        return result;
    }

    private static uint ComputeMfmChecksum(ReadOnlySpan<uint> encodedLongs)
    {
        var checksum = 0u;
        for (var offset = 0; offset < encodedLongs.Length; offset++)
        {
            checksum ^= encodedLongs[offset];
        }

        return checksum & MfmDataMask;
    }

    private static uint Odd(uint value)
    {
        return (value >> 1) & MfmDataMask;
    }

    private static uint Even(uint value)
    {
        return value & MfmDataMask;
    }
}
