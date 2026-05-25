using System;

namespace AmigaTracker.Sid
{
    internal static class BigEndian
    {
        public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, string field)
        {
            if (offset < 0 || offset + 2 > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), $"SID field '{field}' is outside the input data.");
            }

            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string field)
        {
            if (offset < 0 || offset + 4 > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), $"SID field '{field}' is outside the input data.");
            }

            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];
        }
    }
}
