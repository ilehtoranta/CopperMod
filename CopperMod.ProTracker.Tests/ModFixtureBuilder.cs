using System.Text;
using CopperMod.ProTracker;

namespace CopperMod.ProTracker.Tests;

internal static class ModFixtureBuilder
{
    public static byte[] CreateProTracker31(
        string title = "test mod",
        int songLength = 1,
        byte[]? orderTable = null,
        byte[]? sampleData = null,
        int sampleNumber = 1,
        int period = 856,
        int effect = 0,
        int parameter = 0,
        int row = 0,
        int channel = 0,
        int volume = 64,
        int repeatOffsetWords = 0,
        int repeatLengthWords = 1,
        int fineTune = 0,
        int patternCount = 1)
    {
        orderTable ??= new byte[128];
        var maxOrder = orderTable.Max();
        patternCount = Math.Max(patternCount, maxOrder + 1);
        sampleData ??= Enumerable.Repeat((byte)0x40, 64).ToArray();
        if ((sampleData.Length & 1) != 0)
        {
            sampleData = sampleData.Concat(new byte[] { 0 }).ToArray();
        }

        var data = CreateEmptyModule(
            ProTrackerConstants.ProTrackerSampleCount,
            ProTrackerConstants.ProTrackerHeaderLength,
            patternCount,
            sampleData.Length);

        WriteAscii(data, 0, 20, title);
        WriteSample(data, 0, "sample1", sampleData.Length / 2, fineTune, volume, repeatOffsetWords, repeatLengthWords);
        data[950] = (byte)songLength;
        data[951] = 127;
        Array.Copy(orderTable, 0, data, 952, Math.Min(128, orderTable.Length));
        WriteAscii(data, 1080, 4, "M.K.");
        WriteCell(data, ProTrackerConstants.ProTrackerHeaderLength, 0, row, channel, sampleNumber, period, effect, parameter);
        Array.Copy(sampleData, 0, data, ProTrackerConstants.ProTrackerHeaderLength + (patternCount * ProTrackerConstants.PatternLength), sampleData.Length);
        return data;
    }

    public static byte[] CreateLegacy15(
        string title = "legacy",
        byte[]? sampleData = null,
        int effect = 0,
        int parameter = 0,
        int repeatOffsetUnits = 0,
        int repeatLengthUnits = 2)
    {
        sampleData ??= Enumerable.Repeat((byte)0x40, 64).ToArray();
        if ((sampleData.Length & 1) != 0)
        {
            sampleData = sampleData.Concat(new byte[] { 0 }).ToArray();
        }

        var data = CreateEmptyModule(
            ProTrackerConstants.LegacySampleCount,
            ProTrackerConstants.LegacyHeaderLength,
            patternCount: 1,
            sampleData.Length);

        WriteAscii(data, 0, 20, title);
        WriteSample(data, 0, "legacy sample", sampleData.Length / 2, 0, 64, repeatOffsetUnits, repeatLengthUnits);
        data[470] = 1;
        data[471] = 127;
        WriteCell(data, ProTrackerConstants.LegacyHeaderLength, 0, 0, 0, 1, 856, effect, parameter);
        Array.Copy(sampleData, 0, data, ProTrackerConstants.LegacyHeaderLength + ProTrackerConstants.PatternLength, sampleData.Length);
        return data;
    }

    public static byte[] CreatePacked()
    {
        return Encoding.ASCII.GetBytes("PACKnot-supported");
    }

    public static void WriteCell(
        byte[] data,
        int patternOffset,
        int pattern,
        int row,
        int channel,
        int sampleNumber,
        int period,
        int effect,
        int parameter)
    {
        var offset = patternOffset +
            (pattern * ProTrackerConstants.PatternLength) +
            (row * ProTrackerConstants.PatternRowLength) +
            (channel * ProTrackerConstants.PatternCellLength);
        data[offset] = (byte)((sampleNumber & 0xF0) | ((period >> 8) & 0x0F));
        data[offset + 1] = (byte)(period & 0xFF);
        data[offset + 2] = (byte)(((sampleNumber & 0x0F) << 4) | (effect & 0x0F));
        data[offset + 3] = (byte)(parameter & 0xFF);
    }

    public static void WriteSample(
        byte[] data,
        int index,
        string name,
        int lengthWords,
        int fineTune,
        int volume,
        int repeatOffsetUnits,
        int repeatLengthUnits)
    {
        var offset = 20 + (index * ProTrackerConstants.SampleHeaderLength);
        WriteAscii(data, offset, 22, name);
        WriteUInt16(data, offset + 22, lengthWords);
        data[offset + 24] = (byte)(fineTune & 0x0F);
        data[offset + 25] = (byte)volume;
        WriteUInt16(data, offset + 26, repeatOffsetUnits);
        WriteUInt16(data, offset + 28, repeatLengthUnits);
    }

    private static byte[] CreateEmptyModule(int sampleCount, int patternOffset, int patternCount, int sampleDataLength)
    {
        var headerLength = patternOffset;
        var length = headerLength + (patternCount * ProTrackerConstants.PatternLength) + sampleDataLength;
        var data = new byte[length];
        for (var i = 0; i < sampleCount; i++)
        {
            var offset = 20 + (i * ProTrackerConstants.SampleHeaderLength);
            data[offset + 25] = 0;
            WriteUInt16(data, offset + 28, 1);
        }

        return data;
    }

    private static void WriteAscii(byte[] data, int offset, int length, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        Array.Copy(bytes, 0, data, offset, Math.Min(length, bytes.Length));
    }

    private static void WriteUInt16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)((value >> 8) & 0xFF);
        data[offset + 1] = (byte)(value & 0xFF);
    }
}
