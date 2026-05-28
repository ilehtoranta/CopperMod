using System;
using System.Collections.Generic;
using System.Text;

namespace CopperMod.Amiga
{
    internal sealed class AmigaDosFileSystem
    {
        private const int BlockSize = 512;
        private const int StandardRootBlock = 880;
        private const int NameOffset = 432;
        private const int ParentOffset = 0x1F4;
        private const int SecondaryTypeOffset = 0x1FC;
        private const int FirstDataBlockOffset = 0x10;
        private const int FileSizeOffset = 0x144;
        private const int DataBlockPayloadOffset = 24;
        private const int DataBlockPayloadLength = BlockSize - DataBlockPayloadOffset;
        private const uint PrimaryTypeDirectory = 2;
        private const uint PrimaryTypeData = 8;
        private const int SecondaryTypeRoot = 1;
        private const int SecondaryTypeDirectory = 2;
        private const int SecondaryTypeFile = -3;
        private readonly AmigaDiskImage _disk;
        private readonly List<AmigaDosDirectoryEntry> _entries = new List<AmigaDosDirectoryEntry>();

        public AmigaDosFileSystem(AmigaDiskImage disk)
        {
            _disk = disk ?? throw new ArgumentNullException(nameof(disk));
            if (!IsSupported(disk))
            {
                throw new AmigaEmulationException("Only standard OFS DOS\\0 Amiga disk images are supported by the slim boot filesystem.");
            }

            ScanDirectoryEntries();
        }

        public IReadOnlyList<AmigaDosDirectoryEntry> Entries => _entries;

        public static bool IsSupported(AmigaDiskImage disk)
        {
            return disk.Data.Length >= (StandardRootBlock + 1) * BlockSize &&
                disk.Data[0] == (byte)'D' &&
                disk.Data[1] == (byte)'O' &&
                disk.Data[2] == (byte)'S' &&
                disk.Data[3] == 0;
        }

        public bool TryReadFile(string path, out byte[] data)
        {
            data = Array.Empty<byte>();
            if (!TryFindEntry(path, out var entry) || !entry.IsFile)
            {
                return false;
            }

            data = ReadFile(entry);
            return true;
        }

        public bool TryFindEntry(string path, out AmigaDosDirectoryEntry entry)
        {
            entry = default;
            var parts = NormalizePath(path);
            if (parts.Length == 0)
            {
                return false;
            }

            var parent = StandardRootBlock;
            for (var i = 0; i < parts.Length; i++)
            {
                var isLast = i == parts.Length - 1;
                var found = false;
                foreach (var candidate in _entries)
                {
                    if (candidate.ParentBlock != parent ||
                        !candidate.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (isLast)
                    {
                        entry = candidate;
                        return true;
                    }

                    if (!candidate.IsDirectory)
                    {
                        return false;
                    }

                    parent = candidate.Block;
                    found = true;
                    break;
                }

                if (!found)
                {
                    return false;
                }
            }

            return false;
        }

        public bool TryResolveWorkbenchDefaultTool(out string projectPath, out string toolPath, out IReadOnlyList<string> toolTypes)
        {
            projectPath = string.Empty;
            toolPath = string.Empty;
            toolTypes = Array.Empty<string>();
            foreach (var entry in _entries)
            {
                if (entry.ParentBlock != StandardRootBlock ||
                    !entry.IsFile ||
                    entry.Name.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryReadFile(entry.Name + ".info", out var iconData))
                {
                    continue;
                }

                var strings = ExtractIconStrings(iconData);
                foreach (var value in strings)
                {
                    if (value.IndexOf('=') >= 0)
                    {
                        continue;
                    }

                    var normalized = value.Replace('\\', '/');
                    if (normalized.IndexOf(':') < 0 && normalized.IndexOf('/') < 0)
                    {
                        continue;
                    }

                    projectPath = entry.Name;
                    toolPath = normalized;
                    toolTypes = strings.FindAll(item => item.IndexOf('=') >= 0);
                    return true;
                }
            }

            return false;
        }

        private void ScanDirectoryEntries()
        {
            var blockCount = _disk.Data.Length / BlockSize;
            for (var block = 0; block < blockCount; block++)
            {
                var offset = block * BlockSize;
                if (ReadUInt32(offset) != PrimaryTypeDirectory)
                {
                    continue;
                }

                var secondaryType = unchecked((int)ReadUInt32(offset + SecondaryTypeOffset));
                if (secondaryType is not SecondaryTypeRoot and not SecondaryTypeDirectory and not SecondaryTypeFile)
                {
                    continue;
                }

                var name = ReadDirectoryName(offset + NameOffset);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var parent = block == StandardRootBlock ? 0 : checked((int)ReadUInt32(offset + ParentOffset));
                var size = secondaryType == SecondaryTypeFile ? checked((int)ReadUInt32(offset + FileSizeOffset)) : 0;
                _entries.Add(new AmigaDosDirectoryEntry(block, parent, name, secondaryType, size));
            }
        }

        private byte[] ReadFile(AmigaDosDirectoryEntry entry)
        {
            var output = new byte[entry.Size];
            var outputOffset = 0;
            var block = checked((int)ReadUInt32((entry.Block * BlockSize) + FirstDataBlockOffset));
            var guard = 0;
            while (block != 0 && outputOffset < output.Length)
            {
                if (++guard > _disk.Data.Length / BlockSize)
                {
                    throw new AmigaEmulationException($"The OFS data chain for {entry.Name} is cyclic.");
                }

                var offset = block * BlockSize;
                if (offset < 0 || offset + BlockSize > _disk.Data.Length || ReadUInt32(offset) != PrimaryTypeData)
                {
                    throw new AmigaEmulationException($"The OFS data block chain for {entry.Name} is invalid at block {block}.");
                }

                var payloadLength = checked((int)ReadUInt32(offset + 12));
                if (payloadLength < 0 || payloadLength > DataBlockPayloadLength)
                {
                    throw new AmigaEmulationException($"The OFS data block {block} has an invalid payload length.");
                }

                var copyLength = Math.Min(payloadLength, output.Length - outputOffset);
                Array.Copy(_disk.Data, offset + DataBlockPayloadOffset, output, outputOffset, copyLength);
                outputOffset += copyLength;
                block = checked((int)ReadUInt32(offset + 16));
            }

            return output;
        }

        private string ReadDirectoryName(int offset)
        {
            var length = _disk.Data[offset];
            if (length == 0 || length > 107 || offset + 1 + length > _disk.Data.Length)
            {
                return string.Empty;
            }

            for (var i = 0; i < length; i++)
            {
                var value = _disk.Data[offset + 1 + i];
                if (value < 32 || value >= 127)
                {
                    return string.Empty;
                }
            }

            return Encoding.ASCII.GetString(_disk.Data, offset + 1, length);
        }

        private uint ReadUInt32(int offset)
        {
            return BigEndian.ReadUInt32(_disk.Data, offset, "AmigaDOS block field");
        }

        private static string[] NormalizePath(string path)
        {
            path = path.Trim().Trim('"').Replace('\\', '/');
            var colon = path.IndexOf(':');
            if (colon >= 0)
            {
                path = path.Substring(colon + 1);
            }

            return path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static List<string> ExtractIconStrings(ReadOnlySpan<byte> data)
        {
            var values = new List<string>();
            for (var offset = 0; offset < data.Length; offset++)
            {
                var length = data[offset];
                if (length < 4 || length > 96 || offset + 1 + length > data.Length)
                {
                    continue;
                }

                var printable = true;
                for (var i = 0; i < length; i++)
                {
                    var value = data[offset + 1 + i];
                    if (value < 32 || value >= 127)
                    {
                        printable = false;
                        break;
                    }
                }

                if (!printable)
                {
                    continue;
                }

                var text = Encoding.ASCII.GetString(data.Slice(offset + 1, length));
                if ((text.IndexOf('=') >= 0 || text.IndexOf(':') >= 0 || text.IndexOf('/') >= 0) &&
                    !values.Contains(text))
                {
                    values.Add(text);
                }
            }

            for (var offset = 0; offset < data.Length; offset++)
            {
                if (data[offset] < 32 || data[offset] >= 127)
                {
                    continue;
                }

                var end = offset;
                while (end < data.Length && data[end] >= 32 && data[end] < 127)
                {
                    end++;
                }

                var length = end - offset;
                if (length >= 4)
                {
                    var text = Encoding.ASCII.GetString(data.Slice(offset, length));
                    if ((text.IndexOf('=') >= 0 || text.IndexOf(':') >= 0 || text.IndexOf('/') >= 0) &&
                        !values.Contains(text))
                    {
                        values.Add(text);
                    }
                }

                offset = end;
            }

            return values;
        }
    }

    internal readonly struct AmigaDosDirectoryEntry
    {
        public AmigaDosDirectoryEntry(int block, int parentBlock, string name, int secondaryType, int size)
        {
            Block = block;
            ParentBlock = parentBlock;
            Name = name;
            SecondaryType = secondaryType;
            Size = size;
        }

        public int Block { get; }

        public int ParentBlock { get; }

        public string Name { get; }

        public int SecondaryType { get; }

        public int Size { get; }

        public bool IsDirectory => SecondaryType == 1 || SecondaryType == 2;

        public bool IsFile => SecondaryType == -3;
    }
}
