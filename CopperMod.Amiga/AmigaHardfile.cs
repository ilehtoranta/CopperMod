using System;
using System.Collections.Generic;
using System.IO;

namespace CopperMod.Amiga
{
    internal enum AmigaHardfileMountMode
    {
        Auto,
        RigidDiskBlock,
        Partition
    }

    internal sealed record AmigaHardfilePartitionMetadata
    {
        public string? DeviceName { get; init; }

        public uint? TableSize { get; init; }

        public uint? SizeBlockLongs { get; init; }

        public uint? SectorOrigin { get; init; }

        public uint? Surfaces { get; init; }

        public uint? SectorsPerBlock { get; init; }

        public uint? BlocksPerTrack { get; init; }

        public uint? ReservedBlocks { get; init; }

        public uint? PreAllocBlocks { get; init; }

        public uint? Interleave { get; init; }

        public uint? LowCylinder { get; init; }

        public uint? HighCylinder { get; init; }

        public uint? NumBuffers { get; init; }

        public uint? BufferMemoryType { get; init; }

        public uint? MaxTransfer { get; init; }

        public uint? Mask { get; init; }

        public int? BootPriority { get; init; }

        public uint? DosType { get; init; }
    }

    internal sealed class AmigaHardfileConfiguration
    {
        public AmigaHardfileConfiguration(
            int unit,
            string path,
            bool readOnly = false,
            long createSizeBytes = 0,
            AmigaHardfileMountMode mountMode = AmigaHardfileMountMode.Auto,
            AmigaHardfilePartitionMetadata? partition = null)
        {
            if (unit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(unit), unit, "Hardfile unit cannot be negative.");
            }

            Unit = unit;
            Path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("A hardfile path is required.", nameof(path))
                : path;
            ReadOnly = readOnly;
            CreateSizeBytes = createSizeBytes;
            MountMode = mountMode;
            Partition = partition;
        }

        public int Unit { get; }

        public string Path { get; }

        public bool ReadOnly { get; }

        public long CreateSizeBytes { get; }

        public AmigaHardfileMountMode MountMode { get; }

        public AmigaHardfilePartitionMetadata? Partition { get; }
    }

    internal sealed class AmigaHardfile : IDisposable
    {
        public const int SectorSize = 512;
        private readonly FileStream _stream;

        private AmigaHardfile(
            int unit,
            string path,
            bool readOnly,
            AmigaHardfileMountMode mountMode,
            AmigaHardfilePartitionMetadata? partition,
            FileStream stream)
        {
            Unit = unit;
            Path = path;
            ReadOnly = readOnly;
            MountMode = mountMode;
            Partition = partition;
            _stream = stream;
        }

        public int Unit { get; }

        public string Path { get; }

        public bool ReadOnly { get; }

        public AmigaHardfileMountMode MountMode { get; }

        public AmigaHardfilePartitionMetadata? Partition { get; }

        public long Length => _stream.Length;

        public long SectorCount => Length / SectorSize;

        public bool HasRigidDiskBlock => AmigaRigidDiskBlock.TryFind(this, out _);

        public static AmigaHardfile Open(AmigaHardfileConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var fullPath = System.IO.Path.GetFullPath(configuration.Path);
            if (!File.Exists(fullPath))
            {
                if (configuration.CreateSizeBytes <= 0)
                {
                    throw new FileNotFoundException("Hardfile image was not found.", fullPath);
                }

                CreateBlank(fullPath, configuration.CreateSizeBytes);
            }

            var access = configuration.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite;
            var sharing = configuration.ReadOnly ? FileShare.Read : FileShare.None;
            var stream = new FileStream(fullPath, FileMode.Open, access, sharing);
            if (stream.Length % SectorSize != 0)
            {
                stream.Dispose();
                throw new AmigaEmulationException($"Hardfile '{fullPath}' size must be a multiple of {SectorSize} bytes.");
            }

            var hardfile = new AmigaHardfile(
                configuration.Unit,
                fullPath,
                configuration.ReadOnly,
                configuration.MountMode,
                configuration.Partition,
                stream);
            if (configuration.MountMode == AmigaHardfileMountMode.RigidDiskBlock && !hardfile.HasRigidDiskBlock)
            {
                hardfile.Dispose();
                throw new AmigaEmulationException($"Hardfile '{fullPath}' was configured for RDB mounting but no valid RDB was found.");
            }

            return hardfile;
        }

        public static void CreateBlank(string path, long sizeBytes)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A hardfile path is required.", nameof(path));
            }

            if (sizeBytes <= 0 || sizeBytes % SectorSize != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, $"Hardfile size must be a positive multiple of {SectorSize} bytes.");
            }

            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(sizeBytes);
        }

        public IReadOnlyList<AmigaHardfilePartition> GetMountablePartitions()
        {
            if (MountMode != AmigaHardfileMountMode.Partition &&
                AmigaRigidDiskBlock.TryFind(this, out var rdb))
            {
                return rdb.Partitions;
            }

            if (MountMode == AmigaHardfileMountMode.RigidDiskBlock)
            {
                throw new AmigaEmulationException($"Hardfile '{Path}' was configured for RDB mounting but no valid RDB was found.");
            }

            return new[]
            {
                AmigaHardfilePartition.CreateSynthetic(this, 0)
            };
        }

        public IReadOnlyList<AmigaRdbFileSystem> GetRigidDiskBlockFileSystems()
        {
            if (MountMode == AmigaHardfileMountMode.Partition)
            {
                return Array.Empty<AmigaRdbFileSystem>();
            }

            return AmigaRigidDiskBlock.TryFind(this, out var rdb)
                ? rdb.FileSystems
                : Array.Empty<AmigaRdbFileSystem>();
        }

        public void Read(long byteOffset, Span<byte> destination)
        {
            ValidateRange(byteOffset, destination.Length);
            _stream.Position = byteOffset;
            var total = 0;
            while (total < destination.Length)
            {
                var read = _stream.Read(destination[total..]);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Could not read {destination.Length} bytes from hardfile '{Path}'.");
                }

                total += read;
            }
        }

        public void Write(long byteOffset, ReadOnlySpan<byte> source)
        {
            if (ReadOnly)
            {
                throw new UnauthorizedAccessException($"Hardfile '{Path}' is read-only.");
            }

            ValidateRange(byteOffset, source.Length);
            _stream.Position = byteOffset;
            _stream.Write(source);
        }

        public byte[] ReadBlock(int block)
        {
            if (block < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(block));
            }

            var data = new byte[SectorSize];
            Read((long)block * SectorSize, data);
            return data;
        }

        public byte[] ReadBytes(long byteOffset, int byteCount)
        {
            var data = new byte[byteCount];
            Read(byteOffset, data);
            return data;
        }

        public void Flush()
            => _stream.Flush(flushToDisk: true);

        public void Dispose()
            => _stream.Dispose();

        private void ValidateRange(long byteOffset, int byteCount)
        {
            if (byteOffset < 0 || byteCount < 0 || byteOffset + byteCount > Length)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "Hardfile access is outside the image bounds.");
            }
        }
    }

    internal readonly struct AmigaDosEnvec
    {
        public const int LongCount = 17;
        public const uint DosTypeOfs = 0x444F_5300;
        private readonly uint[] _values;

        public AmigaDosEnvec(IReadOnlyList<uint> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count < LongCount)
            {
                throw new ArgumentException($"A DOS environment vector must contain at least {LongCount} longs.", nameof(values));
            }

            _values = new uint[LongCount];
            for (var i = 0; i < _values.Length; i++)
            {
                _values[i] = values[i];
            }
        }

        public uint this[int index] => _values[index];

        public int BootPriority => unchecked((int)_values[15]);

        public uint DosType => _values[16];

        public uint[] ToArray()
        {
            var copy = new uint[LongCount];
            Array.Copy(_values, copy, copy.Length);
            return copy;
        }

        public static AmigaDosEnvec CreateSynthetic(AmigaHardfile hardfile, int partitionIndex)
        {
            ArgumentNullException.ThrowIfNull(hardfile);
            var metadata = hardfile.Partition;
            var surfaces = Math.Max(1u, metadata?.Surfaces ?? 1u);
            var sectorsPerBlock = Math.Max(1u, metadata?.SectorsPerBlock ?? 1u);
            var blocksPerTrack = Math.Max(1u, metadata?.BlocksPerTrack ?? 32u);
            var lowCylinder = metadata?.LowCylinder ?? 0u;
            var logicalBlocks = Math.Max(1u, (uint)Math.Min(uint.MaxValue, hardfile.SectorCount / sectorsPerBlock));
            var blocksPerCylinder = Math.Max(1u, surfaces * blocksPerTrack);
            var computedCylinders = Math.Max(1u, (logicalBlocks + blocksPerCylinder - 1u) / blocksPerCylinder);
            var highCylinder = metadata?.HighCylinder ?? (lowCylinder + computedCylinders - 1u);
            var bootPriority = metadata?.BootPriority ?? (partitionIndex == 0 ? 0 : -5);

            return new AmigaDosEnvec(new[]
            {
                metadata?.TableSize ?? 16u,
                metadata?.SizeBlockLongs ?? (uint)(AmigaHardfile.SectorSize / 4),
                metadata?.SectorOrigin ?? 0u,
                surfaces,
                sectorsPerBlock,
                blocksPerTrack,
                metadata?.ReservedBlocks ?? 2u,
                metadata?.PreAllocBlocks ?? 0u,
                metadata?.Interleave ?? 0u,
                lowCylinder,
                highCylinder,
                metadata?.NumBuffers ?? 30u,
                metadata?.BufferMemoryType ?? 0x0000_0001u,
                metadata?.MaxTransfer ?? 0x0020_0000u,
                metadata?.Mask ?? 0x7FFF_FFFEu,
                unchecked((uint)bootPriority),
                metadata?.DosType ?? DosTypeOfs
            });
        }
    }

    internal sealed class AmigaHardfilePartition
    {
        public AmigaHardfilePartition(
            int unit,
            string deviceName,
            AmigaDosEnvec environment,
            bool bootable,
            bool fromRigidDiskBlock,
            uint? sourceBlock)
        {
            Unit = unit;
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "DH0" : deviceName.Trim();
            Environment = environment;
            Bootable = bootable;
            FromRigidDiskBlock = fromRigidDiskBlock;
            SourceBlock = sourceBlock;
        }

        public int Unit { get; }

        public string DeviceName { get; }

        public AmigaDosEnvec Environment { get; }

        public bool Bootable { get; }

        public bool FromRigidDiskBlock { get; }

        public uint? SourceBlock { get; }

        public int BootPriority => Bootable ? Environment.BootPriority : -128;

        public static AmigaHardfilePartition CreateSynthetic(AmigaHardfile hardfile, int partitionIndex)
        {
            var name = hardfile.Partition?.DeviceName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "DH" + Math.Min(partitionIndex, 9).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return new AmigaHardfilePartition(
                hardfile.Unit,
                name,
                AmigaDosEnvec.CreateSynthetic(hardfile, partitionIndex),
                bootable: true,
                fromRigidDiskBlock: false,
                sourceBlock: null);
        }
    }

    internal sealed class AmigaRdbFileSystem
    {
        public AmigaRdbFileSystem(
            uint block,
            uint dosType,
            uint version,
            uint patchFlags,
            uint nodeType,
            uint task,
            uint lockValue,
            uint handler,
            uint stackSize,
            int priority,
            uint startup,
            uint globalVec,
            byte[] loadSegData)
        {
            Block = block;
            DosType = dosType;
            Version = version;
            PatchFlags = patchFlags;
            NodeType = nodeType;
            Task = task;
            Lock = lockValue;
            Handler = handler;
            StackSize = stackSize;
            Priority = priority;
            Startup = startup;
            GlobalVec = globalVec;
            LoadSegData = loadSegData ?? Array.Empty<byte>();
        }

        public uint Block { get; }

        public uint DosType { get; }

        public uint Version { get; }

        public uint PatchFlags { get; }

        public uint NodeType { get; }

        public uint Task { get; }

        public uint Lock { get; }

        public uint Handler { get; }

        public uint StackSize { get; }

        public int Priority { get; }

        public uint Startup { get; }

        public uint GlobalVec { get; }

        public byte[] LoadSegData { get; }
    }

    internal sealed class AmigaRigidDiskBlock
    {
        private const int ScanBlocks = 16;
        private const int MaxListBlocks = 128;
        private const uint NullBlock = 0xFFFF_FFFFu;
        private const uint RdbMagic = 0x5244_534Bu;
        private const uint PartMagic = 0x5041_5254u;
        private const uint FshdMagic = 0x4653_4844u;
        private const uint LsegMagic = 0x4C53_4547u;
        private const uint PartitionFlagBootable = 1u << 0;
        private const uint PartitionFlagNoMount = 1u << 1;

        private AmigaRigidDiskBlock(
            uint block,
            uint blockBytes,
            uint partitionListBlock,
            uint fileSystemHeaderListBlock,
            IReadOnlyList<AmigaHardfilePartition> partitions,
            IReadOnlyList<AmigaRdbFileSystem> fileSystems)
        {
            Block = block;
            BlockBytes = blockBytes;
            PartitionListBlock = partitionListBlock;
            FileSystemHeaderListBlock = fileSystemHeaderListBlock;
            Partitions = partitions;
            FileSystems = fileSystems;
        }

        public uint Block { get; }

        public uint BlockBytes { get; }

        public uint PartitionListBlock { get; }

        public uint FileSystemHeaderListBlock { get; }

        public IReadOnlyList<AmigaHardfilePartition> Partitions { get; }

        public IReadOnlyList<AmigaRdbFileSystem> FileSystems { get; }

        public static bool TryFind(AmigaHardfile hardfile, out AmigaRigidDiskBlock rdb)
        {
            ArgumentNullException.ThrowIfNull(hardfile);
            var scanSectors = (int)Math.Min(ScanBlocks, hardfile.SectorCount);
            for (var sector = 0; sector < scanSectors; sector++)
            {
                var probe = hardfile.ReadBlock(sector);
                if (ReadUInt32(probe, 0x00, "RDB id") != RdbMagic)
                {
                    continue;
                }

                var blockBytes = ReadUInt32(probe, 0x10, "RDB block bytes");
                if (blockBytes < AmigaHardfile.SectorSize ||
                    blockBytes > int.MaxValue ||
                    (blockBytes % AmigaHardfile.SectorSize) != 0)
                {
                    continue;
                }

                var byteOffset = (long)sector * AmigaHardfile.SectorSize;
                if ((byteOffset % blockBytes) != 0)
                {
                    continue;
                }

                if (!TryReadRdbBlockAtOffset(hardfile, byteOffset, (int)blockBytes, out var data) ||
                    !HasValidChecksum(data))
                {
                    continue;
                }

                var block = checked((uint)(byteOffset / blockBytes));
                var partitionList = ReadUInt32(data, 0x1C, "RDB partition list block");
                var fileSystemList = ReadUInt32(data, 0x20, "RDB file system list block");
                var partitions = ReadPartitions(hardfile, blockBytes, partitionList);
                var fileSystems = ReadFileSystems(hardfile, blockBytes, fileSystemList);
                rdb = new AmigaRigidDiskBlock(block, blockBytes, partitionList, fileSystemList, partitions, fileSystems);
                return true;
            }

            rdb = null!;
            return false;
        }

        public static IReadOnlyList<uint> FindPartitionBlocks(AmigaHardfile hardfile)
        {
            if (!TryFind(hardfile, out var rdb))
            {
                return Array.Empty<uint>();
            }

            var blocks = new List<uint>();
            var block = rdb.PartitionListBlock;
            for (var guard = 0; block != NullBlock && guard < MaxListBlocks; guard++)
            {
                if (!TryReadRdbBlock(hardfile, block, rdb.BlockBytes, out var data) ||
                    ReadUInt32(data, 0x00, "PART id") != PartMagic ||
                    !HasValidChecksum(data))
                {
                    break;
                }

                blocks.Add(block);
                block = ReadUInt32(data, 0x10, "PART next block");
            }

            return blocks;
        }

        private static IReadOnlyList<AmigaHardfilePartition> ReadPartitions(AmigaHardfile hardfile, uint blockBytes, uint firstBlock)
        {
            var partitions = new List<AmigaHardfilePartition>();
            var block = firstBlock;
            for (var guard = 0; block != NullBlock && guard < MaxListBlocks; guard++)
            {
                if (!TryReadRdbBlock(hardfile, block, blockBytes, out var data) ||
                    ReadUInt32(data, 0x00, "PART id") != PartMagic ||
                    !HasValidChecksum(data))
                {
                    break;
                }

                var flags = ReadUInt32(data, 0x14, "PART flags");
                if ((flags & PartitionFlagNoMount) == 0)
                {
                    var environment = ReadEnvironment(data, 0x80);
                    var name = ReadBstr(data, 0x24, 32);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "DH" + Math.Min(partitions.Count, 9).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    partitions.Add(new AmigaHardfilePartition(
                        hardfile.Unit,
                        name,
                        environment,
                        (flags & PartitionFlagBootable) != 0,
                        fromRigidDiskBlock: true,
                        sourceBlock: block));
                }

                block = ReadUInt32(data, 0x10, "PART next block");
            }

            return partitions;
        }

        private static IReadOnlyList<AmigaRdbFileSystem> ReadFileSystems(AmigaHardfile hardfile, uint blockBytes, uint firstBlock)
        {
            var fileSystems = new List<AmigaRdbFileSystem>();
            var block = firstBlock;
            for (var guard = 0; block != NullBlock && guard < MaxListBlocks; guard++)
            {
                if (!TryReadRdbBlock(hardfile, block, blockBytes, out var data) ||
                    ReadUInt32(data, 0x00, "FSHD id") != FshdMagic ||
                    !HasValidChecksum(data))
                {
                    break;
                }

                if (TryReadLoadSegData(hardfile, blockBytes, ReadUInt32(data, 0x48, "FSHD seglist block"), out var loadData))
                {
                    fileSystems.Add(new AmigaRdbFileSystem(
                        block,
                        ReadUInt32(data, 0x20, "FSHD DosType"),
                        ReadUInt32(data, 0x24, "FSHD Version"),
                        ReadUInt32(data, 0x28, "FSHD PatchFlags"),
                        ReadUInt32(data, 0x2C, "FSHD Type"),
                        ReadUInt32(data, 0x30, "FSHD Task"),
                        ReadUInt32(data, 0x34, "FSHD Lock"),
                        ReadUInt32(data, 0x38, "FSHD Handler"),
                        ReadUInt32(data, 0x3C, "FSHD StackSize"),
                        unchecked((int)ReadUInt32(data, 0x40, "FSHD Priority")),
                        ReadUInt32(data, 0x44, "FSHD Startup"),
                        ReadUInt32(data, 0x4C, "FSHD GlobalVec"),
                        loadData));
                }

                block = ReadUInt32(data, 0x10, "FSHD next block");
            }

            return fileSystems;
        }

        private static bool TryReadLoadSegData(AmigaHardfile hardfile, uint blockBytes, uint firstBlock, out byte[] loadData)
        {
            if (firstBlock == NullBlock)
            {
                loadData = Array.Empty<byte>();
                return true;
            }

            var result = new List<byte>();
            var block = firstBlock;
            for (var guard = 0; block != NullBlock && guard < MaxListBlocks; guard++)
            {
                if (!TryReadRdbBlock(hardfile, block, blockBytes, out var data) ||
                    ReadUInt32(data, 0x00, "LSEG id") != LsegMagic ||
                    !HasValidChecksum(data))
                {
                    loadData = Array.Empty<byte>();
                    return false;
                }

                var summedLongs = ReadUInt32(data, 0x04, "LSEG summed longs");
                if (summedLongs < 5)
                {
                    loadData = Array.Empty<byte>();
                    return false;
                }

                var byteCount = checked((int)(summedLongs - 5u) * 4);
                result.AddRange(data.AsSpan(0x14, byteCount).ToArray());
                block = ReadUInt32(data, 0x10, "LSEG next block");
            }

            loadData = result.ToArray();
            return block == NullBlock;
        }

        private static AmigaDosEnvec ReadEnvironment(ReadOnlySpan<byte> data, int offset)
        {
            var values = new uint[AmigaDosEnvec.LongCount];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = ReadUInt32(data, offset + i * 4, "PART environment");
            }

            return new AmigaDosEnvec(values);
        }

        private static bool TryReadRdbBlock(AmigaHardfile hardfile, uint block, uint blockBytes, out byte[] data)
        {
            if (block == NullBlock)
            {
                data = Array.Empty<byte>();
                return false;
            }

            var byteOffset = (ulong)block * blockBytes;
            if (byteOffset > long.MaxValue ||
                byteOffset + blockBytes > (ulong)hardfile.Length)
            {
                data = Array.Empty<byte>();
                return false;
            }

            return TryReadRdbBlockAtOffset(hardfile, (long)byteOffset, checked((int)blockBytes), out data);
        }

        private static bool TryReadRdbBlockAtOffset(AmigaHardfile hardfile, long byteOffset, int blockBytes, out byte[] data)
        {
            if (byteOffset < 0 || byteOffset + blockBytes > hardfile.Length)
            {
                data = Array.Empty<byte>();
                return false;
            }

            data = hardfile.ReadBytes(byteOffset, blockBytes);
            return true;
        }

        private static bool HasValidChecksum(ReadOnlySpan<byte> data)
        {
            var summedLongs = ReadUInt32(data, 0x04, "RDB summed longs");
            if (summedLongs < 5 || summedLongs > data.Length / 4)
            {
                return false;
            }

            var sum = 0u;
            for (var i = 0; i < summedLongs; i++)
            {
                unchecked
                {
                    sum += ReadUInt32(data, i * 4, "RDB checksum long");
                }
            }

            return sum == 0;
        }

        private static string ReadBstr(ReadOnlySpan<byte> data, int offset, int maximumBytes)
        {
            if (offset < 0 || offset >= data.Length || maximumBytes <= 1)
            {
                return string.Empty;
            }

            var length = Math.Min(data[offset], Math.Min(maximumBytes - 1, data.Length - offset - 1));
            Span<char> chars = stackalloc char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)data[offset + 1 + i];
            }

            return new string(chars);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string fieldName)
            => BigEndian.ReadUInt32(data, offset, fieldName);
    }
}
