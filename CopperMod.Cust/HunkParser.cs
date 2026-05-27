using System;
using System.Collections.Generic;
using CopperMod.Abstractions;
using CopperMod.Amiga;

namespace CopperMod.Cust
{
    internal static class HunkParser
    {
        public const uint HunkUnit = 0x0000_03E7;
        public const uint HunkName = 0x0000_03E8;
        public const uint HunkCode = 0x0000_03E9;
        public const uint HunkData = 0x0000_03EA;
        public const uint HunkBss = 0x0000_03EB;
        public const uint HunkReloc32 = 0x0000_03EC;
        public const uint HunkSymbol = 0x0000_03F0;
        public const uint HunkDebug = 0x0000_03F1;
        public const uint HunkEnd = 0x0000_03F2;
        public const uint HunkHeader = 0x0000_03F3;
        private const uint HunkIdMask = 0x3FFF_FFFF;
        private const uint ChipMemoryFlag = 0x4000_0000;
        private const uint FastMemoryFlag = 0x8000_0000;

        public static bool Identify(ReadOnlySpan<byte> data)
        {
            if (data.Length < 4)
            {
                return false;
            }

            return NormalizeHunkId(BigEndian.ReadUInt32(data, 0, "hunk")) == HunkHeader;
        }

        public static HunkFile Parse(ReadOnlySpan<byte> data)
        {
            var reader = new HunkReader(data);
            var first = reader.ReadUInt32("hunk header");
            if (NormalizeHunkId(first) != HunkHeader)
            {
                throw new UnsupportedModuleFormatException("The data is not an Amiga Hunk file.");
            }

            SkipResidentLibraryNames(ref reader);
            var tableSize = CheckedInt(reader.ReadUInt32("hunk table size"), "hunk table size");
            var firstHunk = CheckedInt(reader.ReadUInt32("first hunk"), "first hunk");
            var lastHunk = CheckedInt(reader.ReadUInt32("last hunk"), "last hunk");
            if (tableSize <= 0 || firstHunk < 0 || lastHunk < firstHunk)
            {
                throw new ModuleLoadException("The Hunk header has an invalid hunk table.");
            }

            var hunkCount = lastHunk - firstHunk + 1;
            if (hunkCount > tableSize)
            {
                throw new ModuleLoadException("The Hunk table range exceeds the advertised table size.");
            }

            var tableMemoryKinds = new HunkMemoryKind[hunkCount];
            var tableSizes = new int[hunkCount];
            for (var i = 0; i < tableSize; i++)
            {
                var sizeWord = reader.ReadUInt32("hunk memory size");
                if (i < hunkCount)
                {
                    tableMemoryKinds[i] = DecodeMemoryKind(sizeWord);
                    tableSizes[i] = checked(CheckedInt(sizeWord & HunkIdMask, "hunk memory size") * 4);
                }
            }

            var segments = new List<HunkSegment>();
            while (!reader.EndOfData)
            {
                var typeWord = reader.ReadUInt32("hunk section");
                var type = NormalizeHunkId(typeWord);
                if (type == HunkUnit || type == HunkName)
                {
                    SkipSizedString(ref reader);
                    continue;
                }

                if (type != HunkCode && type != HunkData && type != HunkBss)
                {
                    throw new UnsupportedModuleFormatException($"Unsupported Hunk section 0x{type:X} before a load segment.");
                }

                var segmentIndex = segments.Count;
                if (segmentIndex >= hunkCount)
                {
                    throw new ModuleLoadException("The Hunk stream contains more segments than the header advertised.");
                }

                var sizeWords = CheckedInt(reader.ReadUInt32("hunk section size"), "hunk section size");
                var sizeBytes = checked(sizeWords * 4);
                var kind = type switch
                {
                    HunkCode => HunkSegmentKind.Code,
                    HunkData => HunkSegmentKind.Data,
                    _ => HunkSegmentKind.Bss
                };

                var dataBytes = new byte[Math.Max(sizeBytes, tableSizes[segmentIndex])];
                if (type != HunkBss)
                {
                    reader.ReadBytes(sizeBytes, $"hunk {segmentIndex} data").CopyTo(dataBytes);
                }

                var relocations = new List<HunkRelocationBlock>();
                while (true)
                {
                    if (reader.EndOfData)
                    {
                        throw new ModuleLoadException("The Hunk stream ended before HUNK_END.");
                    }

                    var subTypeWord = reader.ReadUInt32("hunk subsection");
                    var subType = NormalizeHunkId(subTypeWord);
                    if (subType == HunkEnd)
                    {
                        break;
                    }

                    switch (subType)
                    {
                        case HunkReloc32:
                            ReadReloc32(ref reader, hunkCount, relocations);
                            break;
                        case HunkSymbol:
                            SkipSymbols(ref reader);
                            break;
                        case HunkDebug:
                            SkipDebug(ref reader);
                            break;
                        default:
                            throw new UnsupportedModuleFormatException($"Unsupported Hunk subsection 0x{subType:X}.");
                    }
                }

                segments.Add(new HunkSegment(
                    segmentIndex,
                    kind,
                    MergeMemoryKind(tableMemoryKinds[segmentIndex], DecodeMemoryKind(typeWord)),
                    Math.Max(sizeBytes, tableSizes[segmentIndex]),
                    dataBytes,
                    relocations));
            }

            if (segments.Count == 0)
            {
                throw new ModuleLoadException("The Hunk file does not contain any loadable segments.");
            }

            return new HunkFile(segments);
        }

        private static uint NormalizeHunkId(uint value)
        {
            return value & HunkIdMask;
        }

        private static HunkMemoryKind DecodeMemoryKind(uint value)
        {
            if ((value & ChipMemoryFlag) != 0)
            {
                return HunkMemoryKind.Chip;
            }

            if ((value & FastMemoryFlag) != 0)
            {
                return HunkMemoryKind.Fast;
            }

            return HunkMemoryKind.Any;
        }

        private static HunkMemoryKind MergeMemoryKind(HunkMemoryKind tableKind, HunkMemoryKind sectionKind)
        {
            return sectionKind == HunkMemoryKind.Any ? tableKind : sectionKind;
        }

        private static void SkipResidentLibraryNames(ref HunkReader reader)
        {
            while (true)
            {
                var lengthWords = CheckedInt(reader.ReadUInt32("resident library name length"), "resident library name length");
                if (lengthWords == 0)
                {
                    return;
                }

                reader.Skip(checked(lengthWords * 4), "resident library name");
            }
        }

        private static void SkipSizedString(ref HunkReader reader)
        {
            var lengthWords = CheckedInt(reader.ReadUInt32("hunk string length"), "hunk string length");
            reader.Skip(checked(lengthWords * 4), "hunk string");
        }

        private static void ReadReloc32(ref HunkReader reader, int hunkCount, List<HunkRelocationBlock> relocations)
        {
            while (true)
            {
                var count = CheckedInt(reader.ReadUInt32("relocation count"), "relocation count");
                if (count == 0)
                {
                    return;
                }

                var target = CheckedInt(reader.ReadUInt32("relocation target"), "relocation target");
                if (target < 0 || target >= hunkCount)
                {
                    throw new ModuleLoadException("A Hunk relocation points outside the hunk table.");
                }

                var offsets = new int[count];
                for (var i = 0; i < offsets.Length; i++)
                {
                    offsets[i] = CheckedInt(reader.ReadUInt32("relocation offset"), "relocation offset");
                }

                relocations.Add(new HunkRelocationBlock(target, offsets));
            }
        }

        private static void SkipSymbols(ref HunkReader reader)
        {
            while (true)
            {
                var lengthWords = CheckedInt(reader.ReadUInt32("symbol name length"), "symbol name length");
                if (lengthWords == 0)
                {
                    return;
                }

                reader.Skip(checked(lengthWords * 4), "symbol name");
                reader.Skip(4, "symbol value");
            }
        }

        private static void SkipDebug(ref HunkReader reader)
        {
            var lengthWords = CheckedInt(reader.ReadUInt32("debug length"), "debug length");
            reader.Skip(checked(lengthWords * 4), "debug data");
        }

        private static int CheckedInt(uint value, string fieldName)
        {
            if (value > int.MaxValue)
            {
                throw new ModuleLoadException($"{fieldName} is too large.");
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
                    throw new ModuleLoadException($"Unexpected end of Hunk data while reading {fieldName}.");
                }

                var value = BigEndian.ReadUInt32(_data, _offset, fieldName);
                _offset += 4;
                return value;
            }

            public ReadOnlySpan<byte> ReadBytes(int count, string fieldName)
            {
                if (count < 0 || _offset + count > _data.Length)
                {
                    throw new ModuleLoadException($"Unexpected end of Hunk data while reading {fieldName}.");
                }

                var result = _data.Slice(_offset, count);
                _offset += count;
                return result;
            }

            public void Skip(int count, string fieldName)
            {
                _ = ReadBytes(count, fieldName);
            }
        }
    }
}
