using System;
using System.Collections.Generic;
using System.IO;

namespace CopperMod.Amiga
{
    internal sealed class AmigaHardfileConfiguration
    {
        public AmigaHardfileConfiguration(int unit, string path, bool readOnly = false, long createSizeBytes = 0)
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
        }

        public int Unit { get; }

        public string Path { get; }

        public bool ReadOnly { get; }

        public long CreateSizeBytes { get; }
    }

    internal sealed class AmigaHardfile : IDisposable
    {
        public const int SectorSize = 512;
        private readonly FileStream _stream;

        private AmigaHardfile(int unit, string path, bool readOnly, FileStream stream)
        {
            Unit = unit;
            Path = path;
            ReadOnly = readOnly;
            _stream = stream;
        }

        public int Unit { get; }

        public string Path { get; }

        public bool ReadOnly { get; }

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

            return new AmigaHardfile(configuration.Unit, fullPath, configuration.ReadOnly, stream);
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

    internal readonly struct AmigaRigidDiskBlock
    {
        private const int ScanBlocks = 16;

        public AmigaRigidDiskBlock(int block, uint partitionListBlock)
        {
            Block = block;
            PartitionListBlock = partitionListBlock;
        }

        public int Block { get; }

        public uint PartitionListBlock { get; }

        public static bool TryFind(AmigaHardfile hardfile, out AmigaRigidDiskBlock rdb)
        {
            ArgumentNullException.ThrowIfNull(hardfile);
            var scanBlocks = (int)Math.Min(ScanBlocks, hardfile.SectorCount);
            for (var block = 0; block < scanBlocks; block++)
            {
                var data = hardfile.ReadBlock(block);
                if (data[0] == (byte)'R' &&
                    data[1] == (byte)'D' &&
                    data[2] == (byte)'S' &&
                    data[3] == (byte)'K')
                {
                    rdb = new AmigaRigidDiskBlock(block, BigEndian.ReadUInt32(data, 0x1C, "RDB partition list block"));
                    return true;
                }
            }

            rdb = default;
            return false;
        }

        public static IReadOnlyList<uint> FindPartitionBlocks(AmigaHardfile hardfile)
        {
            if (!TryFind(hardfile, out var rdb) ||
                rdb.PartitionListBlock == 0xFFFF_FFFF ||
                rdb.PartitionListBlock >= hardfile.SectorCount)
            {
                return Array.Empty<uint>();
            }

            var partitions = new List<uint>();
            var block = rdb.PartitionListBlock;
            var guard = 0;
            while (block != 0xFFFF_FFFF && block < hardfile.SectorCount && guard++ < 128)
            {
                var data = hardfile.ReadBlock(checked((int)block));
                if (data[0] != (byte)'P' ||
                    data[1] != (byte)'A' ||
                    data[2] != (byte)'R' ||
                    data[3] != (byte)'T')
                {
                    break;
                }

                partitions.Add(block);
                block = BigEndian.ReadUInt32(data, 0x10, "PART next block");
            }

            return partitions;
        }
    }
}
