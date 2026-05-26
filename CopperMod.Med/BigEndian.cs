using System;
using System.Buffers.Binary;
using CopperMod.Abstractions;

namespace CopperMod.Med
{
    internal static class BigEndian
    {
        public static byte ReadByte(ReadOnlySpan<byte> data, int offset, string name)
        {
            RequireRange(data, offset, 1, name);
            return data[offset];
        }

        public static sbyte ReadSByte(ReadOnlySpan<byte> data, int offset, string name)
        {
            return unchecked((sbyte)ReadByte(data, offset, name));
        }

        public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, string name)
        {
            RequireRange(data, offset, 2, name);
            return BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        }

        public static short ReadInt16(ReadOnlySpan<byte> data, int offset, string name)
        {
            RequireRange(data, offset, 2, name);
            return BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));
        }

        public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string name)
        {
            RequireRange(data, offset, 4, name);
            return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
        }

        public static int ReadPointer(ReadOnlySpan<byte> data, int offset, string name)
        {
            var value = ReadUInt32(data, offset, name);
            if (value > int.MaxValue)
            {
                throw new ModuleLoadException($"{name} points beyond supported address range.");
            }

            return (int)value;
        }

        public static void RequireRange(ReadOnlySpan<byte> data, int offset, int length, string name)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length)
            {
                throw new ModuleLoadException($"{name} is outside the module data.");
            }
        }

        public static bool HasRange(ReadOnlySpan<byte> data, int offset, int length)
        {
            return offset >= 0 && length >= 0 && offset <= data.Length - length;
        }
    }
}
