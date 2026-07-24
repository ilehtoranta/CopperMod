/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

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
        private const int RootHashTableOffset = 24;
        private const int HashTableEntryCount = 72;
        private const uint PrimaryTypeDirectory = 2;
        private const uint PrimaryTypeData = 8;
        private const uint PrimaryTypeFileList = 16;
        private const int SecondaryTypeRoot = 1;
        private const int SecondaryTypeDirectory = 2;
        private const int SecondaryTypeFile = -3;
        private readonly IAmigaDiskImage _disk;
        private readonly List<AmigaDosDirectoryEntry> _entries = new List<AmigaDosDirectoryEntry>();
        private readonly Dictionary<int, int> _headerPhysicalBlocks = new Dictionary<int, int>();
        private readonly bool _isFastFileSystem;
        private int _rootDirectoryKey = StandardRootBlock;

        public AmigaDosFileSystem(IAmigaDiskImage disk)
        {
            _disk = disk ?? throw new ArgumentNullException(nameof(disk));
            if (!IsSupported(disk))
            {
                throw new AmigaEmulationException("Only standard OFS DOS\\0 and FFS DOS\\1 Amiga disk images are supported by the slim boot filesystem.");
            }

            _isFastFileSystem = disk.Data[3] == 1;
            ScanDirectoryEntries();
        }

        public IReadOnlyList<AmigaDosDirectoryEntry> Entries => _entries;

        public string VolumeName { get; private set; } = string.Empty;

        public static bool IsSupported(IAmigaDiskImage disk)
        {
            return disk.HasCompleteSectorData &&
                disk.Data.Length >= (StandardRootBlock + 1) * BlockSize &&
                disk.Data[0] == (byte)'D' &&
                disk.Data[1] == (byte)'O' &&
                disk.Data[2] == (byte)'S' &&
                disk.Data[3] is 0 or 1;
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

            var parent = _rootDirectoryKey;
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

        public IReadOnlyList<AmigaDosDirectoryEntry> ListDirectory(string path)
        {
            var parentBlock = ResolveDirectoryBlock(path);
            var entries = new List<AmigaDosDirectoryEntry>();
            foreach (var entry in _entries)
            {
                if (entry.ParentBlock == parentBlock)
                {
                    entries.Add(entry);
                }
            }

            entries.Sort(CompareDirectoryEntries);
            return entries;
        }

        public bool TryReadWorkbenchDiskObject(string path, out AmigaWorkbenchDiskObject diskObject)
        {
            diskObject = default;
            var targetPath = TrimInfoSuffix(NormalizeDisplayPath(path));
            if (targetPath.Length == 0)
            {
                return false;
            }

            var iconPath = targetPath + ".info";
            if (!TryReadFile(iconPath, out var iconData))
            {
                return false;
            }

            var strings = ExtractIconStrings(iconData);
            var toolTypes = new List<string>();
            string? defaultTool = null;
            foreach (var value in strings)
            {
                if (value.IndexOf('=') >= 0)
                {
                    toolTypes.Add(value);
                    continue;
                }

                var normalized = value.Replace('\\', '/');
                if (defaultTool == null &&
                    (normalized.IndexOf(':') >= 0 || normalized.IndexOf('/') >= 0))
                {
                    defaultTool = NormalizeMountedVolumePath(normalized);
                }
            }

            var stackSize = 4096;
            foreach (var toolType in toolTypes)
            {
                var separator = toolType.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = toolType.Substring(0, separator).Trim().TrimStart('$', '.');
                if (key.Equals("STACK", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(toolType.Substring(separator + 1).Trim(), out var parsedStack) &&
                    parsedStack > 0)
                {
                    stackSize = parsedStack;
                }
            }

            var label = GetFileName(targetPath);
            diskObject = new AmigaWorkbenchDiskObject(
                targetPath,
                iconPath,
                label,
                defaultTool,
                toolTypes,
                stackSize);
            return true;
        }

        public bool TryCreateLaunchRequest(string path, out AmigaProgramLaunchRequest request, out string message)
        {
            request = default;
            message = string.Empty;
            var targetPath = TrimInfoSuffix(NormalizeDisplayPath(path));
            if (targetPath.Length == 0)
            {
                message = "No AmigaDOS path was selected.";
                return false;
            }

            if (!TryFindEntry(targetPath, out var targetEntry))
            {
                message = $"'{targetPath}' was not found on DF0:.";
                return false;
            }

            if (targetEntry.IsDirectory)
            {
                message = $"'{targetPath}' is a drawer, not a launchable program.";
                return false;
            }

            var executablePath = targetPath;
            string? projectPath = null;
            IReadOnlyList<string> toolTypes = Array.Empty<string>();
            var stackSize = 4096;
            if (TryReadWorkbenchDiskObject(targetPath, out var diskObject))
            {
                toolTypes = diskObject.ToolTypes;
                stackSize = diskObject.StackSize;
                if (!string.IsNullOrWhiteSpace(diskObject.DefaultToolPath))
                {
                    executablePath = diskObject.DefaultToolPath!;
                    projectPath = targetPath;
                }
            }

            if (!TryReadFile(executablePath, out var executable))
            {
                message = $"'{executablePath}' could not be read from DF0:.";
                return false;
            }

            if (!AmigaHunkProgramLoader.HasHunkHeader(executable))
            {
                message = $"'{executablePath}' is not a HUNK executable.";
                return false;
            }

            var currentDirectory = GetDirectoryName(projectPath ?? executablePath);
            request = new AmigaProgramLaunchRequest(
                executablePath,
                projectPath,
                currentDirectory,
                toolTypes,
                stackSize,
                cliArguments: null);
            return true;
        }

        public bool TryResolveWorkbenchDefaultTool(out string projectPath, out string toolPath, out IReadOnlyList<string> toolTypes)
        {
            projectPath = string.Empty;
            toolPath = string.Empty;
            toolTypes = Array.Empty<string>();
            foreach (var entry in _entries)
            {
                if (entry.ParentBlock != _rootDirectoryKey ||
                    !entry.IsFile ||
                    entry.Name.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryReadWorkbenchDiskObject(entry.Name, out var diskObject) &&
                    !string.IsNullOrWhiteSpace(diskObject.DefaultToolPath) &&
                    TryReadFile(diskObject.DefaultToolPath!, out _))
                {
                    projectPath = entry.Name;
                    toolPath = diskObject.DefaultToolPath!;
                    toolTypes = diskObject.ToolTypes;
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

                var secondaryType = ReadSecondaryType(offset);
                if (secondaryType is not SecondaryTypeRoot and not SecondaryTypeDirectory and not SecondaryTypeFile)
                {
                    continue;
                }

                var name = ReadDirectoryName(offset + NameOffset);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var headerKey = ReadHeaderKey(offset, block);
                if (secondaryType == SecondaryTypeRoot)
                {
                    VolumeName = name;
                }
                var parent = secondaryType == SecondaryTypeRoot ? 0 : checked((int)ReadUInt32(offset + ParentOffset));
                var size = secondaryType == SecondaryTypeFile ? checked((int)ReadUInt32(offset + FileSizeOffset)) : 0;
                _entries.Add(new AmigaDosDirectoryEntry(headerKey, parent, name, secondaryType, size));
                _headerPhysicalBlocks[headerKey] = block;
            }

            _rootDirectoryKey = ResolveRootDirectoryKey();
        }

        private byte[] ReadFile(AmigaDosDirectoryEntry entry)
        {
            if (_isFastFileSystem)
            {
                return ReadFastFile(entry);
            }

            var output = new byte[entry.Size];
            var outputOffset = 0;
            var headerBlock = ResolvePhysicalHeaderBlock(entry.Block);
            var block = checked((int)ReadUInt32((headerBlock * BlockSize) + FirstDataBlockOffset));
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

        private byte[] ReadFastFile(AmigaDosDirectoryEntry entry)
        {
            var output = new byte[entry.Size];
            var outputOffset = 0;
            var listBlock = ResolvePhysicalHeaderBlock(entry.Block);
            var guard = 0;
            while (listBlock != 0 && outputOffset < output.Length)
            {
                if (++guard > _disk.Data.Length / BlockSize)
                {
                    throw new AmigaEmulationException($"The FFS data block table for {entry.Name} is cyclic.");
                }

                var offset = CheckedBlockOffset(listBlock, $"FFS file list block for {entry.Name}");
                var primaryType = ReadUInt32(offset);
                if (primaryType != PrimaryTypeDirectory && primaryType != PrimaryTypeFileList)
                {
                    throw new AmigaEmulationException($"The FFS file list for {entry.Name} is invalid at block {listBlock}.");
                }

                var entryCount = checked((int)ReadUInt32(offset + 8));
                if (entryCount < 0 || entryCount > HashTableEntryCount)
                {
                    throw new AmigaEmulationException($"The FFS file list block {listBlock} has an invalid table size.");
                }

                for (var i = 0; i < entryCount && outputOffset < output.Length; i++)
                {
                    var tableOffset = offset + RootHashTableOffset + ((HashTableEntryCount - 1 - i) * 4);
                    var dataBlock = checked((int)ReadUInt32(tableOffset));
                    if (dataBlock == 0)
                    {
                        throw new AmigaEmulationException($"The FFS file list block {listBlock} contains an empty data pointer.");
                    }

                    var dataOffset = CheckedBlockOffset(dataBlock, $"FFS data block for {entry.Name}");
                    var copyLength = Math.Min(BlockSize, output.Length - outputOffset);
                    Array.Copy(_disk.Data, dataOffset, output, outputOffset, copyLength);
                    outputOffset += copyLength;
                }

                listBlock = checked((int)ReadUInt32(offset + 0x1F8));
            }

            return output;
        }

        private int ResolveRootDirectoryKey()
        {
            var rootOffset = StandardRootBlock * BlockSize;
            if (rootOffset < 0 || rootOffset + BlockSize > _disk.Data.Length)
            {
                return StandardRootBlock;
            }

            var candidates = new Dictionary<int, int>();
            for (var i = 0; i < HashTableEntryCount; i++)
            {
                var childKey = checked((int)ReadUInt32(rootOffset + RootHashTableOffset + (i * 4)));
                if (childKey == 0 || !_headerPhysicalBlocks.ContainsKey(childKey))
                {
                    continue;
                }

                foreach (var entry in _entries)
                {
                    if (entry.Block != childKey || entry.ParentBlock == 0)
                    {
                        continue;
                    }

                    candidates.TryGetValue(entry.ParentBlock, out var count);
                    candidates[entry.ParentBlock] = count + 1;
                    break;
                }
            }

            var bestKey = StandardRootBlock;
            var bestCount = 0;
            foreach (var pair in candidates)
            {
                if (pair.Value > bestCount)
                {
                    bestKey = pair.Key;
                    bestCount = pair.Value;
                }
            }

            return bestCount == 0 ? StandardRootBlock : bestKey;
        }

        private int ResolvePhysicalHeaderBlock(int headerKey)
        {
            if (_headerPhysicalBlocks.TryGetValue(headerKey, out var block))
            {
                return block;
            }

            return headerKey;
        }

        private int ReadHeaderKey(int offset, int fallbackBlock)
        {
            var headerKey = checked((int)ReadUInt32(offset + 4));
            return headerKey > 0 && headerKey < _disk.Data.Length / BlockSize ? headerKey : fallbackBlock;
        }

        private int ReadSecondaryType(int offset)
        {
            var raw = ReadUInt32(offset + SecondaryTypeOffset);
            if (raw <= 0x7F)
            {
                return (int)raw;
            }

            if (raw <= 0xFF)
            {
                return unchecked((sbyte)(byte)raw);
            }

            return unchecked((int)raw);
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

        private int CheckedBlockOffset(int block, string description)
        {
            if (block < 0)
            {
                throw new AmigaEmulationException($"{description} has an invalid negative block number.");
            }

            var offset = block * BlockSize;
            if (offset < 0 || offset + BlockSize > _disk.Data.Length)
            {
                throw new AmigaEmulationException($"{description} points outside the disk image at block {block}.");
            }

            return offset;
        }

        private uint ReadUInt32(int offset)
        {
            return BigEndian.ReadUInt32(_disk.Data, offset, "AmigaDOS block field");
        }

        private static string[] NormalizePath(string path)
        {
            path = NormalizeDisplayPath(path);
            return path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private int ResolveDirectoryBlock(string path)
        {
            var normalized = NormalizeDisplayPath(path);
            if (normalized.Length == 0)
            {
                return _rootDirectoryKey;
            }

            if (!TryFindEntry(normalized, out var entry) || !entry.IsDirectory)
            {
                return StandardRootBlock;
            }

            return entry.Block;
        }

        private static int CompareDirectoryEntries(AmigaDosDirectoryEntry left, AmigaDosDirectoryEntry right)
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        }

        internal static string CombinePath(string parentPath, string name)
        {
            parentPath = NormalizeDisplayPath(parentPath);
            name = NormalizeDisplayPath(name);
            if (parentPath.Length == 0)
            {
                return name;
            }

            return parentPath + "/" + name;
        }

        internal static string GetDirectoryName(string path)
        {
            path = NormalizeDisplayPath(path);
            var slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path.Substring(0, slash);
        }

        internal static string GetFileName(string path)
        {
            path = NormalizeDisplayPath(path);
            var slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        internal static string NormalizeDisplayPath(string path)
        {
            path = (path ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            var colon = path.IndexOf(':');
            if (colon >= 0)
            {
                var prefix = path.Substring(0, colon).Trim('/');
                var suffix = path.Substring(colon + 1).TrimStart('/');
                path = IsRootVolumePrefix(prefix)
                    ? suffix
                    : suffix.Length == 0
                        ? prefix
                        : prefix.Length == 0
                            ? suffix
                            : prefix + "/" + suffix;
            }

            while (path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            while (path.EndsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }

        private static string NormalizeMountedVolumePath(string path)
        {
            var colon = path.IndexOf(':');
            if (colon > 0 && !IsRootVolumePrefix(path.Substring(0, colon).Trim('/')))
            {
                path = path.Substring(colon + 1);
            }

            return NormalizeDisplayPath(path);
        }

        private static bool IsRootVolumePrefix(string prefix)
        {
            if (prefix.Equals("SYS", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return prefix.Length == 3 &&
                (prefix[0] == 'D' || prefix[0] == 'd') &&
                (prefix[1] == 'F' || prefix[1] == 'f') &&
                prefix[2] is >= '0' and <= '3';
        }

        internal static string TrimInfoSuffix(string path)
        {
            return path.EndsWith(".info", StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - ".info".Length)
                : path;
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

    internal readonly struct AmigaWorkbenchDiskObject
    {
        public AmigaWorkbenchDiskObject(
            string targetPath,
            string iconPath,
            string label,
            string? defaultToolPath,
            IReadOnlyList<string> toolTypes,
            int stackSize)
        {
            TargetPath = targetPath ?? string.Empty;
            IconPath = iconPath ?? string.Empty;
            Label = label ?? string.Empty;
            DefaultToolPath = defaultToolPath;
            ToolTypes = CopyList(toolTypes);
            StackSize = Math.Max(1, stackSize);
        }

        public string TargetPath { get; }

        public string IconPath { get; }

        public string Label { get; }

        public string? DefaultToolPath { get; }

        public IReadOnlyList<string> ToolTypes { get; }

        public int StackSize { get; }

        public bool HasDefaultTool => !string.IsNullOrWhiteSpace(DefaultToolPath);

        private static IReadOnlyList<string> CopyList(IReadOnlyList<string>? values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copy = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }

    internal readonly struct AmigaProgramLaunchRequest
    {
        public AmigaProgramLaunchRequest(
            string executablePath,
            string? projectPath,
            string currentDirectory,
            IReadOnlyList<string> toolTypes,
            int stackSize,
            string? cliArguments)
        {
            ExecutablePath = AmigaDosFileSystem.NormalizeDisplayPath(executablePath);
            ProjectPath = string.IsNullOrWhiteSpace(projectPath)
                ? null
                : AmigaDosFileSystem.NormalizeDisplayPath(projectPath);
            CurrentDirectory = AmigaDosFileSystem.NormalizeDisplayPath(currentDirectory);
            ToolTypes = CopyList(toolTypes);
            StackSize = Math.Max(1, stackSize);
            CliArguments = cliArguments;
        }

        public string ExecutablePath { get; }

        public string? ProjectPath { get; }

        public string CurrentDirectory { get; }

        public IReadOnlyList<string> ToolTypes { get; }

        public int StackSize { get; }

        public string? CliArguments { get; }

        private static IReadOnlyList<string> CopyList(IReadOnlyList<string>? values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copy = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }

    internal readonly struct AmigaProgramLaunchResult
    {
        public AmigaProgramLaunchResult(uint entryAddress, string executablePath, string startupArguments, int stackSize)
        {
            EntryAddress = entryAddress;
            ExecutablePath = executablePath ?? string.Empty;
            StartupArguments = startupArguments ?? string.Empty;
            StackSize = Math.Max(1, stackSize);
        }

        public uint EntryAddress { get; }

        public string ExecutablePath { get; }

        public string StartupArguments { get; }

        public int StackSize { get; }
    }
}
