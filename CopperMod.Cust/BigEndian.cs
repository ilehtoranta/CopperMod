using System;

namespace CopperMod.Cust
{
    internal static class BigEndian
    {
        public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, string fieldName)
        {
            if (offset < 0 || offset + 2 > data.Length)
            {
                throw new ArgumentOutOfRangeException(fieldName, "The requested 16-bit field is outside the data.");
            }

            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string fieldName)
        {
            if (offset < 0 || offset + 4 > data.Length)
            {
                throw new ArgumentOutOfRangeException(fieldName, "The requested 32-bit field is outside the data.");
            }

            return ((uint)data[offset] << 24) |
                ((uint)data[offset + 1] << 16) |
                ((uint)data[offset + 2] << 8) |
                data[offset + 3];
        }

        public static void WriteUInt16(Span<byte> data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        public static void WriteUInt32(Span<byte> data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
