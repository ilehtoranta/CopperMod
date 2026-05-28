using System;
using System.Collections.Generic;

namespace CopperMod.Amiga
{
    internal sealed class AmigaHunkProgramLoader
    {
        private const uint HunkUnit = 0x0000_03E7;
        private const uint HunkName = 0x0000_03E8;
        private const uint HunkCode = 0x0000_03E9;
        private const uint HunkData = 0x0000_03EA;
        private const uint HunkBss = 0x0000_03EB;
        private const uint HunkReloc32 = 0x0000_03EC;
        private const uint HunkSymbol = 0x0000_03F0;
        private const uint HunkDebug = 0x0000_03F1;
        private const uint HunkEnd = 0x0000_03F2;
        private const uint HunkHeader = 0x0000_03F3;
        private const uint HunkIdMask = 0x3FFF_FFFF;
        private readonly AmigaBus _bus;
        private readonly Func<int, uint> _allocate;

        public AmigaHunkProgramLoader(AmigaBus bus, Func<int, uint> allocate)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _allocate = allocate ?? throw new ArgumentNullException(nameof(allocate));
        }

        public static bool HasHunkHeader(ReadOnlySpan<byte> data)
        {
            return data.Length >= 4 &&
                NormalizeHunkId(BigEndian.ReadUInt32(data, 0, "hunk header")) == HunkHeader;
        }

        public AmigaHunkProgram Load(ReadOnlySpan<byte> data)
        {
            var reader = new HunkReader(data);
            if (NormalizeHunkId(reader.ReadUInt32("hunk header")) != HunkHeader)
            {
                throw new AmigaEmulationException("The AmigaDOS executable is not a HUNK file.");
            }

            SkipResidentLibraryNames(ref reader);
            var tableSize = CheckedInt(reader.ReadUInt32("hunk table size"), "hunk table size");
            var firstHunk = CheckedInt(reader.ReadUInt32("first hunk"), "first hunk");
            var lastHunk = CheckedInt(reader.ReadUInt32("last hunk"), "last hunk");
            if (tableSize <= 0 || firstHunk < 0 || lastHunk < firstHunk)
            {
                throw new AmigaEmulationException("The HUNK executable has an invalid hunk table.");
            }

            var hunkCount = lastHunk - firstHunk + 1;
            if (hunkCount > tableSize)
            {
                throw new AmigaEmulationException("The HUNK executable hunk range exceeds the table size.");
            }

            var allocations = new uint[hunkCount];
            var bases = new uint[hunkCount];
            var declaredSizes = new int[hunkCount];
            for (var i = 0; i < tableSize; i++)
            {
                var sizeWord = reader.ReadUInt32("hunk memory size");
                if (i < hunkCount)
                {
                    declaredSizes[i] = checked(CheckedInt(sizeWord & HunkIdMask, "hunk memory size") * 4);
                    allocations[i] = _allocate(Math.Max(4, declaredSizes[i]) + 4);
                    _bus.ClearMemory(allocations[i], Math.Max(4, declaredSizes[i]) + 4);
                    bases[i] = allocations[i] + 4;
                }
            }

            for (var i = 0; i < hunkCount; i++)
            {
                var nextSegment = i + 1 < hunkCount ? allocations[i + 1] >> 2 : 0;
                _bus.WriteLong(allocations[i], nextSegment);
            }

            var segmentIndex = 0;
            while (!reader.EndOfData && segmentIndex < hunkCount)
            {
                var type = NormalizeHunkId(reader.ReadUInt32("hunk section"));
                if (type == HunkUnit || type == HunkName)
                {
                    SkipSizedString(ref reader);
                    continue;
                }

                if (type != HunkCode && type != HunkData && type != HunkBss)
                {
                    throw new AmigaEmulationException($"Unsupported HUNK section 0x{type:X} in executable.");
                }

                var sourceSegment = segmentIndex++;
                if (type == HunkBss)
                {
                    _ = reader.ReadUInt32("BSS size");
                }
                else
                {
                    var dataBytes = checked(CheckedInt(reader.ReadUInt32("segment data size"), "segment data size") * 4);
                    if (dataBytes > declaredSizes[sourceSegment])
                    {
                        throw new AmigaEmulationException("The HUNK segment data exceeds its declared allocation.");
                    }

                    _bus.CopyToMemory(bases[sourceSegment], reader.ReadBytes(dataBytes, "segment data"));
                }

                while (true)
                {
                    if (reader.EndOfData)
                    {
                        throw new AmigaEmulationException("The HUNK executable ended before HUNK_END.");
                    }

                    var subType = NormalizeHunkId(reader.ReadUInt32("hunk subsection"));
                    if (subType == HunkEnd)
                    {
                        break;
                    }

                    switch (subType)
                    {
                        case HunkReloc32:
                            ReadReloc32(ref reader, sourceSegment, bases);
                            break;
                        case HunkSymbol:
                            SkipSymbols(ref reader);
                            break;
                        case HunkDebug:
                            SkipDebug(ref reader);
                            break;
                        default:
                            throw new AmigaEmulationException($"Unsupported HUNK subsection 0x{subType:X} in executable.");
                    }
                }
            }

            if (segmentIndex == 0)
            {
                throw new AmigaEmulationException("The HUNK executable does not contain loadable code.");
            }

            return new AmigaHunkProgram(bases[0], bases);
        }

        private void ReadReloc32(ref HunkReader reader, int sourceSegment, IReadOnlyList<uint> bases)
        {
            while (true)
            {
                var count = CheckedInt(reader.ReadUInt32("relocation count"), "relocation count");
                if (count == 0)
                {
                    return;
                }

                var target = CheckedInt(reader.ReadUInt32("relocation target"), "relocation target");
                if (target < 0 || target >= bases.Count)
                {
                    throw new AmigaEmulationException("A HUNK relocation points outside the hunk table.");
                }

                for (var i = 0; i < count; i++)
                {
                    var offset = CheckedInt(reader.ReadUInt32("relocation offset"), "relocation offset");
                    var address = bases[sourceSegment] + (uint)offset;
                    var value = _bus.ReadLong(address);
                    _bus.WriteLong(address, value + bases[target]);
                }
            }
        }

        private static uint NormalizeHunkId(uint value)
        {
            return value & HunkIdMask;
        }

        private static void SkipResidentLibraryNames(ref HunkReader reader)
        {
            while (true)
            {
                var length = CheckedInt(reader.ReadUInt32("resident library name length"), "resident library name length");
                if (length == 0)
                {
                    return;
                }

                reader.Skip(checked(length * 4), "resident library name");
            }
        }

        private static void SkipSizedString(ref HunkReader reader)
        {
            var length = CheckedInt(reader.ReadUInt32("string length"), "string length");
            reader.Skip(checked(length * 4), "string");
        }

        private static void SkipSymbols(ref HunkReader reader)
        {
            while (true)
            {
                var length = CheckedInt(reader.ReadUInt32("symbol length"), "symbol length");
                if (length == 0)
                {
                    return;
                }

                reader.Skip(checked(length * 4), "symbol name");
                reader.Skip(4, "symbol value");
            }
        }

        private static void SkipDebug(ref HunkReader reader)
        {
            var length = CheckedInt(reader.ReadUInt32("debug length"), "debug length");
            reader.Skip(checked(length * 4), "debug data");
        }

        private static int CheckedInt(uint value, string name)
        {
            if (value > int.MaxValue)
            {
                throw new AmigaEmulationException($"The HUNK {name} is too large.");
            }

            return (int)value;
        }

        private ref struct HunkReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _offset;

            public HunkReader(ReadOnlySpan<byte> data)
            {
                _data = data;
                _offset = 0;
            }

            public bool EndOfData => _offset >= _data.Length;

            public uint ReadUInt32(string fieldName)
            {
                if (_offset + 4 > _data.Length)
                {
                    throw new AmigaEmulationException($"Unexpected end of HUNK data while reading {fieldName}.");
                }

                var value = BigEndian.ReadUInt32(_data, _offset, fieldName);
                _offset += 4;
                return value;
            }

            public ReadOnlySpan<byte> ReadBytes(int count, string fieldName)
            {
                if (count < 0 || _offset + count > _data.Length)
                {
                    throw new AmigaEmulationException($"Unexpected end of HUNK data while reading {fieldName}.");
                }

                var value = _data.Slice(_offset, count);
                _offset += count;
                return value;
            }

            public void Skip(int count, string fieldName)
            {
                _ = ReadBytes(count, fieldName);
            }
        }
    }

    internal readonly struct AmigaHunkProgram
    {
        public AmigaHunkProgram(uint entryAddress, IReadOnlyList<uint> segmentBases)
        {
            EntryAddress = entryAddress;
            SegmentBases = segmentBases;
        }

        public uint EntryAddress { get; }

        public IReadOnlyList<uint> SegmentBases { get; }
    }
}
