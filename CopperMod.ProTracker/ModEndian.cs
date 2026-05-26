using System;

namespace CopperMod.ProTracker
{
    internal static class ModEndian
    {
        public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, string fieldName)
        {
            RequireRange(data, offset, 2, fieldName);
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        public static string ReadFixedString(ReadOnlySpan<byte> data, int offset, int length)
        {
            RequireRange(data, offset, length, "fixed string");
            var end = offset;
            var max = offset + length;
            while (end < max && data[end] != 0)
            {
                end++;
            }

            return System.Text.Encoding.ASCII.GetString(data.Slice(offset, end - offset)).TrimEnd();
        }

        public static bool HasRange(ReadOnlySpan<byte> data, int offset, int length)
        {
            return offset >= 0 && length >= 0 && offset <= data.Length - length;
        }

        public static void RequireRange(ReadOnlySpan<byte> data, int offset, int length, string fieldName)
        {
            if (!HasRange(data, offset, length))
            {
                throw new CopperMod.Abstractions.ModuleLoadException(
                    $"The ProTracker module is truncated while reading {fieldName}.");
            }
        }
    }
}
