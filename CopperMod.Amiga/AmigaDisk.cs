using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CopperMod.Amiga
{
    internal sealed class AmigaDiskImage : IAmigaSectorDiskMedia
    {
        public const int SectorSize = 512;
        public const int SectorsPerTrack = 11;
        public const int HeadCount = 2;
        public const int CylinderCount = 80;
        public const int StandardAdfSize = CylinderCount * HeadCount * SectorsPerTrack * SectorSize;
        internal const int TrackCount = CylinderCount * HeadCount;

        private readonly IAmigaDiskMedia _media;
        private readonly IAmigaSectorDiskMedia? _sectorMedia;

        private AmigaDiskImage(IAmigaDiskMedia media)
        {
            _media = media ?? throw new ArgumentNullException(nameof(media));
            _sectorMedia = media as IAmigaSectorDiskMedia;
        }

        public byte[] Data => RequireSectorMedia().Data;

        public string Name => _media.Name;

        public bool HasCompleteSectorData => _sectorMedia?.HasCompleteSectorData == true;

        public bool HasPreservedTrackData => _media.HasPreservedTrackData;

        public ReadOnlySpan<byte> BootBlock => RequireSectorMedia().BootBlock;

        public static AmigaDiskImage Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A disk image path is required.", nameof(path));
            }

            var extension = Path.GetExtension(path);
            if (extension.Equals(".adf", StringComparison.OrdinalIgnoreCase))
            {
                return FromAdfBytes(File.ReadAllBytes(path), Path.GetFileName(path));
            }

            if (extension.Equals(".ipf", StringComparison.OrdinalIgnoreCase))
            {
                return AmigaIpfDiskDecoder.DecodeBytes(File.ReadAllBytes(path), Path.GetFileName(path));
            }

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(path);
                var entries = archive.Entries
                    .Where(entry =>
                        !string.IsNullOrEmpty(entry.Name) &&
                        (entry.Name.EndsWith(".adf", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (entries.Length != 1)
                {
                    throw new AmigaEmulationException("A zipped disk image must contain exactly one ADF or IPF file.");
                }

                using var stream = entries[0].Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var data = memory.ToArray();
                return entries[0].Name.EndsWith(".ipf", StringComparison.OrdinalIgnoreCase)
                    ? AmigaIpfDiskDecoder.DecodeBytes(data, entries[0].Name)
                    : FromAdfBytes(data, entries[0].Name);
            }

            throw new AmigaEmulationException("Unsupported disk image extension. Expected .adf, .ipf, or .zip.");
        }

        public static AmigaDiskImage FromAdfBytes(byte[] data, string name = "disk.adf")
        {
            return new AmigaDiskImage(new AdfDiskMedia(data ?? throw new ArgumentNullException(nameof(data)), name));
        }

        public static AmigaDiskImage FromEncodedTracks(byte[][] encodedTracks, string name = "disk.ipf")
        {
            return new AmigaDiskImage(new TrackBackedDiskMedia(encodedTracks, name));
        }

        public static AmigaDiskImage FromEncodedTracks(AmigaEncodedTrack[] encodedTracks, string name = "disk.ipf")
        {
            return new AmigaDiskImage(new TrackBackedDiskMedia(encodedTracks, name));
        }

        public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
        {
            return RequireSectorMedia().ReadSector(cylinder, head, sector);
        }

        public ReadOnlySpan<byte> ReadSector(int logicalSector)
        {
            return RequireSectorMedia().ReadSector(logicalSector);
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            return RequireSectorMedia().ReadBytes(byteOffset, byteCount);
        }

        public AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head)
        {
            return _media.ReadEncodedTrack(cylinder, head);
        }

        internal static int GetTrackIndex(int cylinder, int head)
        {
            if (cylinder < 0 || cylinder >= CylinderCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cylinder));
            }

            if (head < 0 || head >= HeadCount)
            {
                throw new ArgumentOutOfRangeException(nameof(head));
            }

            return (cylinder * HeadCount) + head;
        }

        internal static int GetLogicalSector(int cylinder, int head, int sector)
        {
            _ = GetTrackIndex(cylinder, head);
            if (sector < 0 || sector >= SectorsPerTrack)
            {
                throw new ArgumentOutOfRangeException(nameof(sector));
            }

            return ((cylinder * HeadCount) + head) * SectorsPerTrack + sector;
        }

        private IAmigaSectorDiskMedia RequireSectorMedia()
        {
            return _sectorMedia ?? throw new AmigaEmulationException($"Disk image '{Name}' does not expose standard AmigaDOS sectors.");
        }
    }

    internal readonly struct AmigaEncodedTrack
    {
        public AmigaEncodedTrack(ReadOnlyMemory<byte> data, int bitLength)
        {
            if (data.IsEmpty)
            {
                throw new ArgumentException("Encoded track data must not be empty.", nameof(data));
            }

            if (bitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length must be positive.");
            }

            if (bitLength > checked(data.Length * 8))
            {
                throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength, "Encoded track bit length cannot exceed the backing data.");
            }

            Data = data;
            BitLength = bitLength;
        }

        public ReadOnlyMemory<byte> Data { get; }

        public int BitLength { get; }

        public int ByteLength => (BitLength + 7) / 8;

        public ReadOnlySpan<byte> Span => Data.Span;

        public static AmigaEncodedTrack FromBytes(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return new AmigaEncodedTrack(data, checked(data.Length * 8));
        }

        public byte ReadByte(int bitOffset)
        {
            return (byte)ReadBits(bitOffset, 8);
        }

        public ushort ReadUInt16(int bitOffset)
        {
            return (ushort)ReadBits(bitOffset, 16);
        }

        public uint ReadUInt32(int bitOffset)
        {
            return (uint)ReadBits(bitOffset, 32);
        }

        private ulong ReadBits(int bitOffset, int bitCount)
        {
            if (bitCount is < 0 or > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(bitCount));
            }

            if (BitLength <= 0)
            {
                throw new InvalidOperationException("Cannot read from an empty encoded track.");
            }

            var span = Data.Span;
            bitOffset = Mod(bitOffset, BitLength);
            var value = 0ul;
            for (var bit = 0; bit < bitCount; bit++)
            {
                var trackBit = (bitOffset + bit) % BitLength;
                var dataBit = (span[trackBit >> 3] >> (7 - (trackBit & 7))) & 1;
                value = (value << 1) | (uint)dataBit;
            }

            return value;
        }

        internal static int Mod(int value, int modulus)
        {
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }

    internal sealed class AmigaPreparedTrack
    {
        private const int DefaultSyncOffsetCapacity = 16384;

        private readonly ushort[] _rollingWords;
        private readonly int[] _syncOffsets;
        private readonly bool _accelerationEnabled;
        private AmigaEncodedTrack _track;
        private byte[]? _trackArray;
        private int _trackArrayOffset;
        private int _trackArrayCount;
        private bool _hasTrack;
        private bool _rollingWordsValid;
        private bool _syncCacheValid;
        private ushort _syncCacheWord;
        private int _syncOffsetCount;
        private bool _syncOffsetOverflow;

        public AmigaPreparedTrack(AmigaEncodedTrack track)
            : this(Math.Max(1, track.BitLength), DefaultSyncOffsetCapacity)
        {
            Prepare(track);
        }

        internal AmigaPreparedTrack(int maxTrackBits, int syncOffsetCapacity, bool accelerationEnabled = true)
        {
            if (maxTrackBits <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTrackBits));
            }

            if (syncOffsetCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(syncOffsetCapacity));
            }

            _rollingWords = new ushort[maxTrackBits];
            _syncOffsets = new int[syncOffsetCapacity];
            _accelerationEnabled = accelerationEnabled;
        }

        public int BitLength => RequireTrack().BitLength;

        public bool HasRollingWords => _rollingWordsValid;

        public void Prepare(AmigaEncodedTrack track)
        {
            if (track.BitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(track), "Prepared tracks require a non-empty encoded track.");
            }

            _track = track;
            CaptureTrackIdentity(track.Data);
            _hasTrack = true;
            _rollingWordsValid = false;
            _syncCacheValid = false;
            _syncOffsetCount = 0;
            _syncOffsetOverflow = false;
        }

        public void Clear()
        {
            _hasTrack = false;
            _trackArray = null;
            _trackArrayOffset = 0;
            _trackArrayCount = 0;
            _rollingWordsValid = false;
            _syncCacheValid = false;
            _syncOffsetCount = 0;
            _syncOffsetOverflow = false;
        }

        public bool Matches(AmigaEncodedTrack track)
        {
            if (!_hasTrack || _track.BitLength != track.BitLength)
            {
                return false;
            }

            if (_trackArray != null &&
                MemoryMarshal.TryGetArray(track.Data, out ArraySegment<byte> segment) &&
                segment.Array != null)
            {
                return ReferenceEquals(_trackArray, segment.Array) &&
                    _trackArrayOffset == segment.Offset &&
                    _trackArrayCount == segment.Count;
            }

            return _track.Data.Span.SequenceEqual(track.Data.Span);
        }

        public byte ReadByte(int bitOffset)
            => (byte)(ReadUInt16(bitOffset) >> 8);

        public ushort ReadUInt16(int bitOffset)
        {
            var track = RequireTrack();
            if (!_accelerationEnabled || track.BitLength > _rollingWords.Length)
            {
                return track.ReadUInt16(bitOffset);
            }

            EnsureRollingWords(track);
            return _rollingWords[AmigaEncodedTrack.Mod(bitOffset, track.BitLength)];
        }

        public int CopySyncOffsets(ushort sync, Span<int> destination, out bool cacheHit)
        {
            if (!_accelerationEnabled)
            {
                cacheHit = false;
                return CopySyncOffsetsScalar(sync, destination);
            }

            EnsureSyncOffsets(sync, out cacheHit);
            var count = Math.Min(_syncOffsetCount, destination.Length);
            _syncOffsets.AsSpan(0, count).CopyTo(destination);
            return count;
        }

        public bool TryFindSyncWithinNextWord(ushort sync, int sourceBit, out int distance, out int syncOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            if (!_accelerationEnabled)
            {
                return TryFindSyncWithinNextWordScalar(sync, sourceBit, out distance, out syncOffset);
            }

            EnsureSyncOffsets(sync, out _);
            if (_syncOffsetCount == 0)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            if (_syncOffsetOverflow)
            {
                return TryFindSyncWithinNextWordScalar(sync, sourceBit, out distance, out syncOffset);
            }

            return TryFindCachedSyncWithinNextWord(
                _syncOffsets.AsSpan(0, _syncOffsetCount),
                track.BitLength,
                sourceBit,
                out distance,
                out syncOffset);
        }

        public int FindSyncOffset(ushort sync, int startOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                return -1;
            }

            if (!_accelerationEnabled)
            {
                return FindSyncOffsetScalar(sync, startOffset);
            }

            EnsureSyncOffsets(sync, out _);
            if (_syncOffsetCount == 0)
            {
                return -1;
            }

            if (_syncOffsetOverflow)
            {
                return FindSyncOffsetScalar(sync, startOffset);
            }

            startOffset = AmigaEncodedTrack.Mod(startOffset, track.BitLength);
            var index = LowerBound(_syncOffsets, _syncOffsetCount, startOffset);
            var offset = index < _syncOffsetCount ? _syncOffsets[index] : _syncOffsets[0];
            return RewindConsecutiveSync(sync, offset);
        }

        public int RewindConsecutiveSync(ushort sync, int offset)
        {
            var track = RequireTrack();
            if (track.BitLength < 32)
            {
                return offset;
            }

            var current = offset;
            var maxSteps = Math.Max(1, track.BitLength / 16);
            for (var step = 0; step < maxSteps; step++)
            {
                var previous = (current - 16 + track.BitLength) % track.BitLength;
                if (previous == offset || ReadUInt16(previous) != sync)
                {
                    return current;
                }

                current = previous;
            }

            return current;
        }

        private void EnsureSyncOffsets(ushort sync, out bool cacheHit)
        {
            if (_syncCacheValid && _syncCacheWord == sync)
            {
                cacheHit = true;
                return;
            }

            cacheHit = false;
            var track = RequireTrack();
            _syncOffsetCount = 0;
            _syncOffsetOverflow = false;
            for (var offset = 0; offset < track.BitLength; offset++)
            {
                if (ReadUInt16(offset) != sync)
                {
                    continue;
                }

                if (_syncOffsetCount < _syncOffsets.Length)
                {
                    _syncOffsets[_syncOffsetCount++] = offset;
                    continue;
                }

                _syncOffsetOverflow = true;
            }

            _syncCacheWord = sync;
            _syncCacheValid = true;
        }

        private int CopySyncOffsetsScalar(ushort sync, Span<int> destination)
        {
            var track = RequireTrack();
            var count = 0;
            for (var offset = 0; offset < track.BitLength; offset++)
            {
                if (track.ReadUInt16(offset) != sync)
                {
                    continue;
                }

                if (count < destination.Length)
                {
                    destination[count] = offset;
                }

                count++;
            }

            return Math.Min(count, destination.Length);
        }

        private void EnsureRollingWords(AmigaEncodedTrack track)
        {
            if (_rollingWordsValid)
            {
                return;
            }

            for (var offset = 0; offset < track.BitLength; offset++)
            {
                _rollingWords[offset] = track.ReadUInt16(offset);
            }

            _rollingWordsValid = true;
        }

        private AmigaEncodedTrack RequireTrack()
        {
            if (!_hasTrack)
            {
                throw new InvalidOperationException("Prepared track has not been initialized.");
            }

            return _track;
        }

        private void CaptureTrackIdentity(ReadOnlyMemory<byte> data)
        {
            if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) && segment.Array != null)
            {
                _trackArray = segment.Array;
                _trackArrayOffset = segment.Offset;
                _trackArrayCount = segment.Count;
                return;
            }

            _trackArray = null;
            _trackArrayOffset = 0;
            _trackArrayCount = data.Length;
        }

        private bool TryFindSyncWithinNextWordScalar(ushort sync, int sourceBit, out int distance, out int syncOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            for (var bitDistance = 1; bitDistance < 16; bitDistance++)
            {
                var offset = (sourceBit + bitDistance) % track.BitLength;
                if (ReadUInt16(offset) == sync)
                {
                    distance = bitDistance;
                    syncOffset = offset;
                    return true;
                }
            }

            distance = 0;
            syncOffset = -1;
            return false;
        }

        private int FindSyncOffsetScalar(ushort sync, int startOffset)
        {
            var track = RequireTrack();
            if (track.BitLength < 16)
            {
                return -1;
            }

            startOffset = AmigaEncodedTrack.Mod(startOffset, track.BitLength);
            for (var step = 0; step < track.BitLength; step++)
            {
                var offset = (startOffset + step) % track.BitLength;
                if (ReadUInt16(offset) == sync)
                {
                    return RewindConsecutiveSync(sync, offset);
                }
            }

            return -1;
        }

        private static bool TryFindCachedSyncWithinNextWord(
            ReadOnlySpan<int> syncOffsets,
            int trackLength,
            int sourceBit,
            out int distance,
            out int syncOffset)
        {
            var start = sourceBit + 1;
            var stop = sourceBit + 15;
            if (stop < trackLength)
            {
                var index = LowerBound(syncOffsets, syncOffsets.Length, start);
                if (index < syncOffsets.Length && syncOffsets[index] <= stop)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }
            }
            else
            {
                var index = LowerBound(syncOffsets, syncOffsets.Length, start);
                if (index < syncOffsets.Length)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }

                var wrappedStop = stop - trackLength;
                if (syncOffsets[0] <= wrappedStop)
                {
                    syncOffset = syncOffsets[0];
                    distance = (trackLength - sourceBit) + syncOffset;
                    return true;
                }
            }

            distance = 0;
            syncOffset = -1;
            return false;
        }

        private static int LowerBound(ReadOnlySpan<int> values, int count, int target)
        {
            var low = 0;
            var high = count;
            while (low < high)
            {
                var mid = (int)((uint)(low + high) >> 1);
                if (values[mid] < target)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }
    }

    internal readonly struct AmigaDiskSpecializationCounters
    {
        public AmigaDiskSpecializationCounters(
            long preparedTrackHits,
            long preparedTrackMisses,
            long rollingWindowHits,
            long rollingWindowMisses,
            long syncIndexHits,
            long syncIndexMisses)
        {
            PreparedTrackHits = preparedTrackHits;
            PreparedTrackMisses = preparedTrackMisses;
            RollingWindowHits = rollingWindowHits;
            RollingWindowMisses = rollingWindowMisses;
            SyncIndexHits = syncIndexHits;
            SyncIndexMisses = syncIndexMisses;
        }

        public long PreparedTrackHits { get; }

        public long PreparedTrackMisses { get; }

        public long RollingWindowHits { get; }

        public long RollingWindowMisses { get; }

        public long SyncIndexHits { get; }

        public long SyncIndexMisses { get; }
    }

    internal interface IAmigaDiskMedia
    {
        string Name { get; }

        bool HasPreservedTrackData { get; }

        AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head);
    }

    internal interface IAmigaSectorDiskMedia : IAmigaDiskMedia
    {
        byte[] Data { get; }

        bool HasCompleteSectorData { get; }

        ReadOnlySpan<byte> BootBlock { get; }

        ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector);

        ReadOnlySpan<byte> ReadSector(int logicalSector);

        ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount);
    }

    internal sealed class AdfDiskMedia : IAmigaSectorDiskMedia
    {
        private readonly byte[][] _encodedTracks = new byte[AmigaDiskImage.TrackCount][];

        public AdfDiskMedia(byte[] data, string name)
        {
            if (data.Length != AmigaDiskImage.StandardAdfSize)
            {
                throw new AmigaEmulationException($"Only standard {AmigaDiskImage.StandardAdfSize}-byte sector images are supported.");
            }

            Data = data;
            Name = name;
        }

        public byte[] Data { get; }

        public string Name { get; }

        public bool HasPreservedTrackData => false;

        public bool HasCompleteSectorData => true;

        public ReadOnlySpan<byte> BootBlock => Data.AsSpan(0, 1024);

        public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
        {
            return ReadSector(AmigaDiskImage.GetLogicalSector(cylinder, head, sector));
        }

        public ReadOnlySpan<byte> ReadSector(int logicalSector)
        {
            var offset = checked(logicalSector * AmigaDiskImage.SectorSize);
            if (logicalSector < 0 || offset + AmigaDiskImage.SectorSize > Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalSector));
            }

            return Data.AsSpan(offset, AmigaDiskImage.SectorSize);
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            if (byteOffset < 0 || byteCount < 0 || byteOffset + byteCount > Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset), "Requested disk byte range is outside the sector image.");
            }

            return Data.AsSpan(byteOffset, byteCount);
        }

        public AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head)
        {
            var index = AmigaDiskImage.GetTrackIndex(cylinder, head);
            return AmigaEncodedTrack.FromBytes(_encodedTracks[index] ??= AmigaDosTrackEncoder.EncodeTrack(this, cylinder, head));
        }
    }

    internal sealed class TrackBackedDiskMedia : IAmigaSectorDiskMedia
    {
        private readonly AmigaEncodedTrack[] _encodedTracks;

        public TrackBackedDiskMedia(byte[][] encodedTracks, string name)
            : this(WrapByteTracks(encodedTracks), name)
        {
        }

        public TrackBackedDiskMedia(AmigaEncodedTrack[] encodedTracks, string name)
        {
            ArgumentNullException.ThrowIfNull(encodedTracks);
            if (encodedTracks.Length != AmigaDiskImage.TrackCount)
            {
                throw new ArgumentException($"Exactly {AmigaDiskImage.TrackCount} encoded tracks are required.", nameof(encodedTracks));
            }

            _encodedTracks = new AmigaEncodedTrack[encodedTracks.Length];
            for (var index = 0; index < encodedTracks.Length; index++)
            {
                _encodedTracks[index] = encodedTracks[index].BitLength > 0
                    ? encodedTracks[index]
                    : AmigaEncodedTrack.FromBytes(AmigaDosTrackEncoder.CreateUnformattedTrack());
            }

            Data = AmigaDosTrackDecoder.DecodeBestEffort(encodedTracks, out var hasCompleteSectorData);
            HasCompleteSectorData = hasCompleteSectorData;
            Name = name;
        }

        public byte[] Data { get; }

        public string Name { get; }

        public bool HasPreservedTrackData => true;

        public bool HasCompleteSectorData { get; }

        public ReadOnlySpan<byte> BootBlock => Data.AsSpan(0, 1024);

        public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
        {
            return ReadSector(AmigaDiskImage.GetLogicalSector(cylinder, head, sector));
        }

        public ReadOnlySpan<byte> ReadSector(int logicalSector)
        {
            var offset = checked(logicalSector * AmigaDiskImage.SectorSize);
            if (logicalSector < 0 || offset + AmigaDiskImage.SectorSize > Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalSector));
            }

            return Data.AsSpan(offset, AmigaDiskImage.SectorSize);
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            if (byteOffset < 0 || byteCount < 0 || byteOffset + byteCount > Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset), "Requested disk byte range is outside the decoded sector view.");
            }

            return Data.AsSpan(byteOffset, byteCount);
        }

        public AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head)
        {
            var index = AmigaDiskImage.GetTrackIndex(cylinder, head);
            return _encodedTracks[index];
        }

        private static AmigaEncodedTrack[] WrapByteTracks(byte[][] encodedTracks)
        {
            ArgumentNullException.ThrowIfNull(encodedTracks);
            var tracks = new AmigaEncodedTrack[encodedTracks.Length];
            for (var index = 0; index < encodedTracks.Length; index++)
            {
                tracks[index] = AmigaEncodedTrack.FromBytes(encodedTracks[index] ?? AmigaDosTrackEncoder.CreateUnformattedTrack());
            }

            return tracks;
        }
    }

    internal sealed class AmigaFloppyDrive
    {
        private const int MinCylinder = 0;
        private const int MaxCylinder = AmigaDiskImage.CylinderCount - 1;

        public AmigaDiskImage? Disk { get; private set; }

        public bool HasDisk => Disk != null;

        public int Cylinder { get; private set; }

        public int Head { get; private set; }

        public bool MotorOn { get; private set; }

        public long MotorOnCycle { get; private set; }

        public bool Selected { get; private set; }

        public bool DiskChanged { get; private set; }

        public bool WriteProtected { get; private set; }

        public void Insert(AmigaDiskImage disk, bool markChanged = false, bool writeProtected = false)
        {
            Disk = disk ?? throw new ArgumentNullException(nameof(disk));
            DiskChanged = markChanged;
            WriteProtected = writeProtected;
        }

        public void Eject()
        {
            Disk = null;
            DiskChanged = true;
            WriteProtected = false;
        }

        public void ResetPosition()
        {
            Cylinder = 0;
            Head = 0;
            MotorOn = false;
            MotorOnCycle = 0;
            Selected = false;
        }

        public void SetHead(int head)
        {
            Head = head == 0 ? 0 : 1;
        }

        public void SetMotorOn(bool motorOn)
        {
            SetMotorOn(motorOn, 0);
        }

        public void SetMotorOn(bool motorOn, long cycle)
        {
            if (MotorOn == motorOn)
            {
                return;
            }

            MotorOn = motorOn;
            MotorOnCycle = motorOn ? cycle : 0;
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
        }

        public void SetWriteProtected(bool writeProtected)
        {
            WriteProtected = writeProtected;
        }

        public void Step(int delta)
        {
            Cylinder = Math.Clamp(Cylinder + delta, MinCylinder, MaxCylinder);
            if (Disk != null)
            {
                DiskChanged = false;
            }
        }

        public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
        {
            return RequireDisk().ReadSector(cylinder, head, sector);
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            return RequireDisk().ReadBytes(byteOffset, byteCount);
        }

        public AmigaEncodedTrack ReadEncodedTrack(int cylinder, int head)
        {
            return RequireDisk().ReadEncodedTrack(cylinder, head);
        }

        private AmigaDiskImage RequireDisk()
        {
            return Disk ?? throw new AmigaEmulationException("No disk is inserted in DF0:.");
        }
    }

    internal sealed class AmigaDiskController
    {
        private const int MaxFloppyDriveCount = 4;
        private const ushort DskDatr = 0x008;
        private const ushort DskBytr = 0x01A;
        private const ushort DskPth = 0x020;
        private const ushort DskPtl = 0x022;
        private const ushort DskLen = 0x024;
        private const ushort DskDat = 0x026;
        private const ushort DskSync = 0x07E;
        private const ushort DskBlkInterrupt = 0x0002;
        private const ushort DskSynInterrupt = 0x1000;
        private const ushort DmaconMasterEnable = 0x0200;
        private const ushort DmaconDiskEnable = 0x0010;
        private const ushort DskBytReady = 0x8000;
        private const ushort DskBytDmaOn = 0x4000;
        private const ushort DskBytDiskWrite = 0x2000;
        private const ushort DskBytWordEqual = 0x1000;
        private const ushort DskDmaEnable = 0x8000;
        private const ushort DskWriteMode = 0x4000;
        private const ushort DskLengthMask = 0x3FFF;
        private const int MaxDmaTraceEntries = 4096;
        private const int DiskRevolutionsPerSecond = 5;
        private static readonly long DiskWordEqualHoldCycles = Math.Max(
            1,
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz / 500_000.0));
        private static readonly long DiskIndexPulseCycles = Math.Max(
            1,
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz / DiskRevolutionsPerSecond));
        private static readonly long DiskMotorReadyDelayCycles = Math.Max(
            1,
            (long)Math.Round(AmigaConstants.A500PalCpuClockHz * 0.5));

        private readonly AmigaBus _bus;
        private readonly bool _specializationEnabled;
        private readonly AmigaFloppyDrive[] _drives;
        private readonly List<AmigaDiskDmaTraceEntry> _dmaTrace = new List<AmigaDiskDmaTraceEntry>(MaxDmaTraceEntries);
        private long _currentCycle;
        private ushort _dsklen;
        private ushort _dsksync = 0x4489;
        private uint _diskPointer;
        private byte _ciabPortB = 0xFF;
        private ushort _diskDataRegister;
        private byte _dskbytrData;
        private bool _dskbytrByteReady;
        private long _dskbytrWordEqualUntilCycle;
        private int _transferCount;
        private int _lastTransferWords;
        private int _lastTransferDrive;
        private int _lastTransferCylinder;
        private int _lastTransferHead;
        private uint _lastTransferAddress;
        private ushort? _armedDsklen;
        private readonly DiskStreamState[] _streams;
        private long _nextIndexPulseCycle;
        private long _nextDiskInputAdvanceCycle;
        private ushort _pendingReadDmaWords;
        private bool _activeDma;
        private int _activeDmaDrive;
        private uint _activeDmaTargetAddress;
        private int _activeDmaNextSourceBit;
        private long _activeDmaNextWordStartCycle;
        private bool _activeDmaWordSyncEnabled;
        private ushort _activeDmaRequestedWords;
        private int _activeDmaTransferredWords;
        private int _activeDmaCylinder;
        private int _activeDmaHead;
        private int _activeDmaTrackBitLength;
        private double _activeDmaStreamStartPosition;
        private long _activeDmaStartCycle;
        private long _activeDmaCompletionCycle;
        private double _activeDmaCyclesPerBit;
        private long _preparedTrackHits;
        private long _preparedTrackMisses;
        private long _rollingWindowHits;
        private long _rollingWindowMisses;
        private long _syncIndexHits;
        private long _syncIndexMisses;

        public AmigaDiskController(AmigaBus bus, int connectedDriveCount = 1, bool enableSpecialization = false)
        {
            if (connectedDriveCount is < 1 or > MaxFloppyDriveCount)
            {
                throw new ArgumentOutOfRangeException(nameof(connectedDriveCount), connectedDriveCount, "The connected floppy drive count must be between 1 and 4.");
            }

            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _specializationEnabled = enableSpecialization;
            ConnectedDriveCount = connectedDriveCount;
            Drive0 = new AmigaFloppyDrive();
            Drive1 = new AmigaFloppyDrive();
            Drive2 = new AmigaFloppyDrive();
            Drive3 = new AmigaFloppyDrive();
            _drives = new[] { Drive0, Drive1, Drive2, Drive3 };
            _streams = Enumerable.Range(0, MaxFloppyDriveCount)
                .Select(_ => new DiskStreamState())
                .ToArray();
            Reset();
        }

        public int ConnectedDriveCount { get; }

        public AmigaFloppyDrive Drive0 { get; }

        public AmigaFloppyDrive Drive1 { get; }

        public AmigaFloppyDrive Drive2 { get; }

        public AmigaFloppyDrive Drive3 { get; }

        public int TransferCount => _transferCount;

        public AmigaDiskControllerSnapshot CaptureSnapshot()
        {
            return new AmigaDiskControllerSnapshot(
                _diskPointer,
                _dsklen,
                _dsksync,
                PeekDskbytr(),
                Drive0.Cylinder,
                Drive0.Head,
                Drive0.MotorOn,
                Drive0.Selected,
                _ciabPortB,
                _transferCount,
                _lastTransferWords,
                _lastTransferDrive,
                _lastTransferCylinder,
                _lastTransferHead,
                _lastTransferAddress,
                GetSelectedDriveIndex(),
                _activeDmaDrive,
                _activeDma,
                _activeDmaCompletionCycle,
                _diskDataRegister,
                ConnectedDriveCount,
                CaptureSpecializationCounters());
        }

        public AmigaDiskSpecializationCounters CaptureSpecializationCounters()
            => new AmigaDiskSpecializationCounters(
                _preparedTrackHits,
                _preparedTrackMisses,
                _rollingWindowHits,
                _rollingWindowMisses,
                _syncIndexHits,
                _syncIndexMisses);

        public AmigaDiskDmaTraceEntry[] CaptureDmaTrace()
        {
            return _dmaTrace.ToArray();
        }

        public void ClearDmaTrace()
        {
            _dmaTrace.Clear();
        }

        public void Reset()
        {
            _currentCycle = 0;
            _dsklen = 0;
            _dsksync = 0x4489;
            _diskPointer = 0;
            _ciabPortB = 0xFF;
            _diskDataRegister = 0;
            _dskbytrData = 0;
            _dskbytrByteReady = false;
            _dskbytrWordEqualUntilCycle = 0;
            _transferCount = 0;
            _dmaTrace.Clear();
            _lastTransferWords = 0;
            _lastTransferDrive = -1;
            _lastTransferCylinder = 0;
            _lastTransferHead = 0;
            _lastTransferAddress = 0;
            _armedDsklen = null;
            _nextIndexPulseCycle = DiskIndexPulseCycles;
            _nextDiskInputAdvanceCycle = 0;
            foreach (var stream in _streams)
            {
                stream.Reset();
            }

            _pendingReadDmaWords = 0;
            ClearActiveDma();
            _preparedTrackHits = 0;
            _preparedTrackMisses = 0;
            _rollingWindowHits = 0;
            _rollingWindowMisses = 0;
            _syncIndexHits = 0;
            _syncIndexMisses = 0;
            foreach (var drive in _drives)
            {
                drive.ResetPosition();
            }
        }

        public void AdvanceTo(long targetCycle)
        {
            if (targetCycle < _currentCycle)
            {
                return;
            }

            if (CanSkipAdvanceTo(targetCycle))
            {
                _currentCycle = targetCycle;
                return;
            }

            _currentCycle = targetCycle;
            if (_activeDma && targetCycle > _activeDmaCompletionCycle)
            {
                AdvanceDiskInputTo(_activeDmaCompletionCycle);
                AdvanceActiveDmaTo(_activeDmaCompletionCycle);
                AdvanceDiskInputTo(targetCycle);
                TryStartPendingReadDma(targetCycle);
            }
            else
            {
                AdvanceDiskInputTo(targetCycle);
                TryStartPendingReadDma(targetCycle);
                AdvanceActiveDmaTo(targetCycle);
            }

            if (targetCycle < _nextIndexPulseCycle)
            {
                return;
            }

            while (_nextIndexPulseCycle <= targetCycle)
            {
                if (AnyConnectedDriveMotorOn())
                {
                    _bus.PulseCiaFlag(AmigaCiaId.B, _nextIndexPulseCycle);
                }

                _nextIndexPulseCycle += DiskIndexPulseCycles;
            }
        }

        private bool CanSkipAdvanceTo(long targetCycle)
        {
            if (_pendingReadDmaWords != 0)
            {
                return false;
            }

            var nextDiskInput = _nextDiskInputAdvanceCycle > 0
                ? _nextDiskInputAdvanceCycle
                : long.MaxValue;
            var nextEventCycle = Math.Min(
                Math.Min(nextDiskInput, GetNextActiveDmaAdvanceCycle()),
                _nextIndexPulseCycle);
            return targetCycle < nextEventCycle;
        }

        public long? GetNextWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            long? candidate = null;
            candidate = MinWakeCandidate(candidate, GetPendingReadDmaWakeCandidateCycle(currentCycle));

            if (_nextDiskInputAdvanceCycle > 0 && _bus.Paula.IsInterruptEnabled(DskSynInterrupt))
            {
                candidate = MinWakeCandidate(candidate, _nextDiskInputAdvanceCycle);
            }

            if (_activeDma && IsDiskDmaControlEnabled())
            {
                candidate = MinWakeCandidate(candidate, _activeDmaCompletionCycle);
            }

            if ((_bus.CiaB.InterruptMask & AmigaCia.FlagInterruptMask) != 0 && AnyConnectedDriveMotorOn())
            {
                candidate = MinWakeCandidate(candidate, _nextIndexPulseCycle);
            }

            return ClampWakeCandidate(candidate, currentCycle, targetCycle);
        }

        private long? GetPendingReadDmaWakeCandidateCycle(long currentCycle)
        {
            if (_pendingReadDmaWords == 0 || !IsDiskDmaControlEnabled())
            {
                return null;
            }

            var driveIndex = GetSelectedDriveIndex();
            if (driveIndex < 0 || !IsDriveConnected(driveIndex))
            {
                return null;
            }

            var drive = _drives[driveIndex];
            if (drive.Disk == null || !drive.MotorOn)
            {
                return null;
            }

            var readyCycle = GetDriveReadyCycle(drive);
            return currentCycle >= readyCycle ? currentCycle + 1 : readyCycle;
        }

        private long GetNextActiveDmaAdvanceCycle()
        {
            if (!_activeDma)
            {
                return long.MaxValue;
            }

            if (!IsDiskDmaControlEnabled())
            {
                return _currentCycle;
            }

            if (_activeDmaTransferredWords >= _activeDmaRequestedWords)
            {
                return _activeDmaCompletionCycle;
            }

            var nextWordCompletionCycle = _activeDmaNextWordStartCycle + CyclesForBits(16, _activeDmaCyclesPerBit);
            return Math.Min(nextWordCompletionCycle, _activeDmaCompletionCycle);
        }

        private static long? MinWakeCandidate(long? candidate, long? eventCycle)
        {
            if (!eventCycle.HasValue)
            {
                return candidate;
            }

            return MinWakeCandidate(candidate, eventCycle.Value);
        }

        private static long? MinWakeCandidate(long? candidate, long eventCycle)
        {
            if (!candidate.HasValue)
            {
                return eventCycle;
            }

            return Math.Min(candidate.Value, eventCycle);
        }

        private static long? ClampWakeCandidate(long? candidate, long currentCycle, long targetCycle)
        {
            if (!candidate.HasValue || candidate.Value > targetCycle)
            {
                return null;
            }

            return candidate.Value <= currentCycle ? currentCycle + 1 : candidate.Value;
        }

        public byte ReadCiaAPortA(byte latchedPortA)
        {
            var value = (byte)(latchedPortA | 0xFC);
            var driveIndex = GetSelectedLineDriveIndex();
            if (driveIndex >= 0 && !IsDriveConnected(driveIndex))
            {
                return value;
            }

            var drive = driveIndex >= 0 ? _drives[driveIndex] : Drive0;
            value = SetActiveLow(value, 2, drive.DiskChanged || drive.Disk == null);
            value = SetActiveLow(value, 3, drive.Disk != null && drive.WriteProtected);
            value = SetActiveLow(value, 4, drive.Disk != null && drive.Cylinder == 0);
            value = SetActiveLow(value, 5, IsDriveReady(drive, _currentCycle));
            return value;
        }

        public byte ReadByte(ushort offset)
        {
            return TryReadByte(offset, out var value) ? value : (byte)0;
        }

        public bool TryReadByte(ushort offset, out byte value)
        {
            var wordOffset = (ushort)(offset & 0x01FE);
            if (!IsDiskRegister(wordOffset))
            {
                value = 0;
                return false;
            }

            var word = ReadWord(wordOffset);
            value = (offset & 1) == 0 ? (byte)(word >> 8) : (byte)word;
            return true;
        }

        public ushort ReadWord(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset switch
            {
                DskDatr => _diskDataRegister,
                DskBytr => ReadDskbytr(),
                DskPth => (ushort)((_diskPointer >> 16) & (_bus.ChipDmaAddressMask >> 16)),
                DskPtl => (ushort)(_diskPointer & 0xFFFE),
                DskLen => _dsklen,
                DskSync => _dsksync,
                _ => 0
            };
        }

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            _currentCycle = Math.Max(_currentCycle, cycle);
            offset = (ushort)(offset & 0x01FE);
            switch (offset)
            {
                case DskDat:
                    _diskDataRegister = value;
                    break;
                case DskPth:
                    _diskPointer = _bus.WriteChipDmaPointerHigh(_diskPointer, value);
                    break;
                case DskPtl:
                    _diskPointer = _bus.WriteChipDmaPointerLow(_diskPointer, value);
                    break;
                case DskLen:
                    AdvanceActiveDmaTo(cycle);
                    _dsklen = value;
                    if ((value & DskDmaEnable) == 0 || (value & DskWriteMode) != 0 || (value & DskLengthMask) == 0)
                    {
                        CancelActiveDma(cycle);
                        _armedDsklen = null;
                        _pendingReadDmaWords = 0;
                        break;
                    }

                    if (_armedDsklen == value)
                    {
                        _armedDsklen = null;
                        _pendingReadDmaWords = (ushort)(value & DskLengthMask);
                        TryStartPendingReadDma(cycle);
                    }
                    else
                    {
                        _armedDsklen = value;
                    }

                    break;
                case DskSync:
                    _dsksync = value;
                    InvalidateSyncCaches();
                    InvalidateNextDiskInputAdvanceCycle();
                    TryStartPendingReadDma(cycle);
                    break;
            }
        }

        public void WriteCiaBRegister(int register, byte value, long cycle)
        {
            if ((register & 0x0F) != 1)
            {
                return;
            }

            _currentCycle = Math.Max(_currentCycle, cycle);
            var previous = _ciabPortB;
            AdvanceDiskInputTo(cycle);

            _ciabPortB = value;
            var head = (value & 0x04) == 0 ? 1 : 0;
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                if (!IsDriveConnected(driveIndex))
                {
                    _drives[driveIndex].SetSelected(false);
                    continue;
                }

                var selectMask = 1 << (driveIndex + 3);
                var previousSelected = (previous & selectMask) == 0;
                var selected = (value & selectMask) == 0;
                var drive = _drives[driveIndex];
                var previousMotorOn = drive.MotorOn;
                if (selected && !previousSelected)
                {
                    drive.SetMotorOn((value & 0x80) == 0, cycle);
                    if (drive.MotorOn != previousMotorOn)
                    {
                        _streams[driveIndex].Cycle = cycle;
                    }
                }

                drive.SetSelected(selected);
                drive.SetHead(head);
            }

            InvalidateNextDiskInputAdvanceCycle();
            var previousStepHigh = (previous & 0x01) != 0;
            var stepHigh = (value & 0x01) != 0;
            if (previousStepHigh && !stepHigh)
            {
                var inward = (value & 0x02) == 0;
                for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
                {
                    if (!IsDriveConnected(driveIndex))
                    {
                        continue;
                    }

                    var drive = _drives[driveIndex];
                    if (drive.Selected)
                    {
                        drive.Step(inward ? 1 : -1);
                    }
                }
            }

            TryStartPendingReadDma(cycle);
        }

        private static bool IsDiskRegister(ushort offset)
        {
            return offset is DskDatr or DskBytr or DskPth or DskPtl or DskLen or DskSync;
        }

        private ushort ReadDskbytr()
        {
            var value = PeekDskbytr();
            _dskbytrByteReady = false;
            return value;
        }

        private ushort PeekDskbytr()
        {
            var value = (ushort)_dskbytrData;
            if (_dskbytrByteReady)
            {
                value |= DskBytReady;
            }

            if (IsDiskDmaActuallyOn())
            {
                value |= DskBytDmaOn;
            }

            if ((_dsklen & DskWriteMode) != 0)
            {
                value |= DskBytDiskWrite;
            }

            if (_dskbytrWordEqualUntilCycle != 0 && GetDskbytrReferenceCycle() <= _dskbytrWordEqualUntilCycle)
            {
                value |= DskBytWordEqual;
            }

            return value;
        }

        private int GetReadDmaDriveIndex()
        {
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                var drive = _drives[driveIndex];
                if (IsDriveConnected(driveIndex) && drive.Selected && IsDriveReady(drive, _currentCycle))
                {
                    return driveIndex;
                }
            }

            return -1;
        }

        private int GetSelectedDriveIndex()
        {
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                if (_drives[driveIndex].Selected)
                {
                    return driveIndex;
                }
            }

            return -1;
        }

        private int GetSelectedLineDriveIndex()
        {
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                var selectMask = 1 << (driveIndex + 3);
                if ((_ciabPortB & selectMask) == 0)
                {
                    return driveIndex;
                }
            }

            return -1;
        }

        private AmigaFloppyDrive? GetDriveOrNull(int driveIndex)
        {
            return IsDriveConnected(driveIndex) ? _drives[driveIndex] : null;
        }

        private bool IsDriveConnected(int driveIndex)
        {
            return (uint)driveIndex < (uint)ConnectedDriveCount;
        }

        private bool AnyConnectedDriveMotorOn()
        {
            for (var driveIndex = 0; driveIndex < ConnectedDriveCount; driveIndex++)
            {
                var drive = _drives[driveIndex];
                if (drive.Disk != null && drive.MotorOn)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDriveReady(AmigaFloppyDrive drive, long cycle)
        {
            return drive.Disk != null &&
                drive.MotorOn &&
                cycle >= drive.MotorOnCycle + DiskMotorReadyDelayCycles;
        }

        private static long GetDriveReadyCycle(AmigaFloppyDrive drive)
        {
            return drive.MotorOnCycle + DiskMotorReadyDelayCycles;
        }

        private DiskStreamState GetActiveDmaStream()
        {
            return (uint)_activeDmaDrive < (uint)_streams.Length ? _streams[_activeDmaDrive] : _streams[0];
        }

        private long GetDskbytrReferenceCycle()
        {
            var driveIndex = GetSelectedDriveIndex();
            return driveIndex >= 0 ? _streams[driveIndex].Cycle : _streams[0].Cycle;
        }

        private void InvalidateSyncCaches()
        {
            foreach (var stream in _streams)
            {
                stream.InvalidateSyncCache();
            }
        }

        private static byte SetActiveLow(byte value, int bit, bool asserted)
        {
            var mask = (byte)(1 << bit);
            return asserted ? (byte)(value & ~mask) : (byte)(value | mask);
        }

        private void TryStartPendingReadDma(long cycle)
        {
            if (_pendingReadDmaWords == 0)
            {
                return;
            }

            if (StartReadDma(_pendingReadDmaWords, cycle))
            {
                _pendingReadDmaWords = 0;
            }
        }

        private bool StartReadDma(ushort requestedWords, long cycle)
        {
            _currentCycle = Math.Max(_currentCycle, cycle);
            var driveIndex = GetReadDmaDriveIndex();
            if (requestedWords == 0 || driveIndex < 0 || !IsDiskDmaControlEnabled())
            {
                return false;
            }

            CancelActiveDma(cycle);

            var drive = _drives[driveIndex];
            var stream = _streams[driveIndex];
            var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
            var preparedTrack = GetPreparedTrack(drive, stream, track);
            var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
            AdvanceDiskInputTo(cycle);
            ResetStreamIfTrackChanged(drive, stream, cycle);

            var sourceStartBit = stream.Offset;
            var syncWaitBits = 0;
            var wordSyncEnabled = (_bus.Paula.Adkcon & 0x0400) != 0;
            var syncOffsetCount = 0;
            if (wordSyncEnabled)
            {
                syncOffsetCount = GetSyncOffsets(drive, stream, preparedTrack, track.BitLength);
                var syncOffset = FindSyncOffset(preparedTrack, stream.Offset, stream.SyncCacheOffsets, syncOffsetCount);
                if (syncOffset < 0)
                {
                    AppendDmaTrace(
                        AmigaDiskDmaTraceKind.SyncMissing,
                        cycle,
                        driveIndex,
                        drive.Cylinder,
                        drive.Head,
                        targetAddress: _bus.MaskChipDmaAddress(_diskPointer),
                        requestedWords,
                        transferredWords: 0,
                        sourceBit: stream.Offset,
                        syncWaitBits: -1,
                        track.BitLength,
                        wordSyncEnabled,
                        completionCycle: 0);
                    return false;
                }

                sourceStartBit = (syncOffset + 16) % track.BitLength;
                syncWaitBits = (sourceStartBit - stream.Offset + track.BitLength) % track.BitLength;
            }

            var targetAddress = _bus.MaskChipDmaAddress(_diskPointer);
            _transferCount++;
            _lastTransferWords = requestedWords;
            _lastTransferDrive = driveIndex;
            _lastTransferCylinder = drive.Cylinder;
            _lastTransferHead = drive.Head;
            _lastTransferAddress = targetAddress;
            var dataStartCycle = cycle + CyclesForBits(syncWaitBits, cyclesPerBit);
            var completionCycle = PredictReadDmaCompletionCycle(
                preparedTrack,
                _dsksync,
                sourceStartBit,
                dataStartCycle,
                requestedWords,
                cyclesPerBit,
                wordSyncEnabled,
                stream.SyncCacheOffsets,
                syncOffsetCount);
            _activeDma = true;
            _activeDmaDrive = driveIndex;
            _activeDmaTargetAddress = targetAddress;
            _activeDmaNextSourceBit = sourceStartBit;
            _activeDmaNextWordStartCycle = dataStartCycle;
            _activeDmaWordSyncEnabled = wordSyncEnabled;
            _activeDmaRequestedWords = requestedWords;
            _activeDmaTransferredWords = 0;
            _activeDmaCylinder = drive.Cylinder;
            _activeDmaHead = drive.Head;
            _activeDmaTrackBitLength = track.BitLength;
            _activeDmaStreamStartPosition = stream.Position;
            _activeDmaStartCycle = cycle;
            _activeDmaCompletionCycle = completionCycle;
            _activeDmaCyclesPerBit = cyclesPerBit;
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Started,
                cycle,
                driveIndex,
                drive.Cylinder,
                drive.Head,
                targetAddress,
                requestedWords,
                transferredWords: 0,
                sourceStartBit,
                syncWaitBits,
                track.BitLength,
                wordSyncEnabled,
                completionCycle);
            AdvanceActiveDmaTo(cycle);
            return true;
        }

        private void AdvanceActiveDmaTo(long targetCycle)
        {
            if (!_activeDma)
            {
                return;
            }

            var activeDrive = GetDriveOrNull(_activeDmaDrive);
            if (activeDrive?.Disk == null)
            {
                ClearActiveDma();
                return;
            }

            if (!IsDiskDmaControlEnabled())
            {
                StopActiveDma(targetCycle);
                return;
            }

            if (targetCycle < _activeDmaStartCycle)
            {
                return;
            }

            var advanceCycle = Math.Min(targetCycle, _activeDmaCompletionCycle);
            if (_activeDmaTransferredWords < _activeDmaRequestedWords)
            {
                var track = activeDrive.ReadEncodedTrack(_activeDmaCylinder, _activeDmaHead);
                var stream = GetActiveDmaStream();
                var preparedTrack = GetPreparedTrack(activeDrive, stream, track, _activeDmaCylinder, _activeDmaHead);
                while (_activeDmaTransferredWords < _activeDmaRequestedWords)
                {
                    var word = _activeDmaTransferredWords;
                    var plan = GetReadDmaWordPlan(
                        preparedTrack,
                        _dsksync,
                        word,
                        _activeDmaNextSourceBit,
                        _activeDmaNextWordStartCycle,
                        _activeDmaCyclesPerBit,
                        _activeDmaWordSyncEnabled,
                        stream.SyncCacheOffsets,
                        stream.SyncCacheOffsetCount);
                    if (plan.CompletionCycle > advanceCycle)
                    {
                        break;
                    }

                    var value = ReadPreparedUInt16(preparedTrack, plan.SourceBit);
                    _diskDataRegister = value;
                    _bus.WriteChipWordForDevice(
                        AmigaBusRequester.Disk,
                        AmigaBusAccessKind.DiskDma,
                        _bus.AddChipDmaPointerOffset(_activeDmaTargetAddress, word * 2),
                        value,
                        plan.CompletionCycle);
                    _activeDmaNextSourceBit = plan.NextSourceBit;
                    _activeDmaNextWordStartCycle = plan.NextWordStartCycle;
                    _activeDmaTransferredWords++;
                    UpdateActiveDmaPointer();
                    UpdateActiveDmaLength();
                }
            }

            if (targetCycle < _activeDmaCompletionCycle)
            {
                return;
            }

            CompleteActiveDma();
        }

        private void CompleteActiveDma()
        {
            _activeDmaTransferredWords = _activeDmaRequestedWords;
            UpdateActiveDmaPointer();
            UpdateActiveDmaLength();
            var elapsedCycles = Math.Max(0, _activeDmaCompletionCycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, _activeDmaCompletionCycle);
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Completed,
                _activeDmaCompletionCycle,
                _activeDmaDrive,
                _activeDmaCylinder,
                _activeDmaHead,
                _activeDmaTargetAddress,
                _activeDmaRequestedWords,
                _activeDmaTransferredWords,
                GetActiveDmaStream().Offset,
                syncWaitBits: 0,
                _activeDmaTrackBitLength,
                _activeDmaWordSyncEnabled,
                _activeDmaCompletionCycle);
            _bus.WriteDeviceWord(
                AmigaBusRequester.Disk,
                AmigaBusAccessKind.DiskDma,
                0x00DFF09C,
                (ushort)(0x8000 | DskBlkInterrupt),
                _activeDmaCompletionCycle);
            ClearActiveDma();
        }

        private void CancelActiveDma(long cycle)
        {
            if (!_activeDma)
            {
                return;
            }

            if (IsDiskDmaControlEnabled())
            {
                AdvanceActiveDmaTo(cycle);
            }

            if (!_activeDma)
            {
                return;
            }

            var elapsedCycles = Math.Max(0, cycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Cancelled,
                cycle,
                _activeDmaDrive,
                _activeDmaCylinder,
                _activeDmaHead,
                _activeDmaTargetAddress,
                _activeDmaRequestedWords,
                _activeDmaTransferredWords,
                GetActiveDmaStream().Offset,
                syncWaitBits: 0,
                _activeDmaTrackBitLength,
                _activeDmaWordSyncEnabled,
                _activeDmaCompletionCycle);
            ClearActiveDma();
        }

        private void StopActiveDma(long cycle)
        {
            var elapsedCycles = Math.Max(0, cycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            AppendDmaTrace(
                AmigaDiskDmaTraceKind.Stopped,
                cycle,
                _activeDmaDrive,
                _activeDmaCylinder,
                _activeDmaHead,
                _activeDmaTargetAddress,
                _activeDmaRequestedWords,
                _activeDmaTransferredWords,
                GetActiveDmaStream().Offset,
                syncWaitBits: 0,
                _activeDmaTrackBitLength,
                _activeDmaWordSyncEnabled,
                _activeDmaCompletionCycle);
            ClearActiveDma();
        }

        private void AppendDmaTrace(
            AmigaDiskDmaTraceKind kind,
            long cycle,
            int drive,
            int cylinder,
            int head,
            uint targetAddress,
            int requestedWords,
            int transferredWords,
            int sourceBit,
            int syncWaitBits,
            int trackBitLength,
            bool wordSyncEnabled,
            long completionCycle)
        {
            if (_dmaTrace.Count == MaxDmaTraceEntries)
            {
                _dmaTrace.RemoveAt(0);
            }

            _dmaTrace.Add(new AmigaDiskDmaTraceEntry(
                kind,
                _transferCount,
                cycle,
                drive,
                cylinder,
                head,
                GetSelectedDriveIndex(),
                _dsklen,
                _dsksync,
                _bus.Paula.Adkcon,
                PeekDskbytr(),
                _diskDataRegister,
                targetAddress,
                requestedWords,
                transferredWords,
                sourceBit,
                syncWaitBits,
                trackBitLength,
                wordSyncEnabled,
                completionCycle));
        }

        private void ClearActiveDma()
        {
            _activeDma = false;
            _activeDmaDrive = -1;
            _activeDmaTargetAddress = 0;
            _activeDmaNextSourceBit = 0;
            _activeDmaNextWordStartCycle = 0;
            _activeDmaWordSyncEnabled = false;
            _activeDmaRequestedWords = 0;
            _activeDmaTransferredWords = 0;
            _activeDmaCylinder = 0;
            _activeDmaHead = 0;
            _activeDmaTrackBitLength = 0;
            _activeDmaStreamStartPosition = 0;
            _activeDmaStartCycle = 0;
            _activeDmaCompletionCycle = 0;
            _activeDmaCyclesPerBit = 0;
        }

        private void UpdateActiveDmaPointer()
        {
            _diskPointer = _bus.AddChipDmaPointerOffset(_activeDmaTargetAddress, _activeDmaTransferredWords * 2);
        }

        private void UpdateActiveDmaLength()
        {
            var remaining = Math.Max(0, _activeDmaRequestedWords - _activeDmaTransferredWords);
            _dsklen = (ushort)((_dsklen & (DskDmaEnable | DskWriteMode)) | (remaining & DskLengthMask));
        }

        private void ResetStreamIfTrackChanged(AmigaFloppyDrive drive, DiskStreamState stream, long cycle)
        {
            if (stream.Cylinder == drive.Cylinder && stream.Head == drive.Head)
            {
                return;
            }

            stream.Cylinder = drive.Cylinder;
            stream.Head = drive.Head;
            stream.InvalidateSyncCache();
            SetStreamPosition(stream, 0, cycle);
        }

        private bool IsDiskDmaControlEnabled()
        {
            return (_dsklen & DskDmaEnable) != 0 &&
                (_bus.Paula.Dmacon & (DmaconMasterEnable | DmaconDiskEnable)) == (DmaconMasterEnable | DmaconDiskEnable);
        }

        private bool IsDiskDmaActuallyOn()
        {
            return _activeDma && IsDiskDmaControlEnabled();
        }

        private static double GetDiskDmaCyclesPerBit(int trackBitLength)
        {
            if (trackBitLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackBitLength), trackBitLength, "Encoded track bit length must be positive.");
            }

            return AmigaConstants.A500PalCpuClockHz / (trackBitLength * DiskRevolutionsPerSecond);
        }

        private void AdvanceStreamTo(DiskStreamState stream, int trackBitLength, double cyclesPerBit, long cycle)
        {
            if (cycle <= stream.Cycle)
            {
                return;
            }

            var elapsedCycles = cycle - stream.Cycle;
            var position = (stream.Position + (elapsedCycles / cyclesPerBit)) % trackBitLength;
            SetStreamPosition(stream, position, cycle);
        }

        private void AdvanceDiskInputTo(long cycle)
        {
            var nextInputAdvanceCycle = long.MaxValue;
            for (var driveIndex = 0; driveIndex < _drives.Length; driveIndex++)
            {
                var drive = _drives[driveIndex];
                var stream = _streams[driveIndex];
                if (!IsDriveConnected(driveIndex))
                {
                    stream.Cycle = cycle;
                    continue;
                }

                if (cycle <= stream.Cycle)
                {
                    continue;
                }

                if (drive.Disk == null || !drive.MotorOn)
                {
                    stream.Cycle = cycle;
                    continue;
                }

                var readyCycle = GetDriveReadyCycle(drive);
                if (cycle < readyCycle)
                {
                    stream.Cycle = cycle;
                    continue;
                }

                if (stream.Cycle < readyCycle)
                {
                    SetStreamPosition(stream, stream.Position, readyCycle);
                }

                var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
                ResetStreamIfTrackChanged(drive, stream, stream.Cycle);
                var preparedTrack = GetPreparedTrack(drive, stream, track);
                var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
                AdvanceInputStreamTo(drive, stream, preparedTrack, cyclesPerBit, cycle, drive.Selected);
                if (drive.Selected)
                {
                    nextInputAdvanceCycle = Math.Min(
                        nextInputAdvanceCycle,
                        GetNextSelectedInputAdvanceCycle(drive, stream, preparedTrack, cyclesPerBit));
                }
            }

            _nextDiskInputAdvanceCycle = nextInputAdvanceCycle == long.MaxValue ? 0 : nextInputAdvanceCycle;
        }

        private void AdvanceInputStreamTo(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            double cyclesPerBit,
            long cycle,
            bool selected)
        {
            if (cycle <= stream.Cycle)
            {
                return;
            }

            var startCycle = stream.Cycle;
            var startPosition = stream.Position;
            var elapsedBits = (cycle - startCycle) / cyclesPerBit;
            var endPositionAbsolute = startPosition + elapsedBits;
            if (selected)
            {
                UpdateDskbytrData(track, startPosition, endPositionAbsolute);
                SignalSyncMatches(drive, stream, track, startCycle, startPosition, endPositionAbsolute, cyclesPerBit);
            }

            SetStreamPosition(stream, endPositionAbsolute % track.BitLength, cycle);
        }

        private long GetNextSelectedInputAdvanceCycle(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            double cyclesPerBit)
        {
            var nextBytePosition = (Math.Floor(stream.Position / 8.0) + 1.0) * 8.0;
            var nextByteCycle = stream.Cycle + CyclesForBits(nextBytePosition - stream.Position, cyclesPerBit);
            var nextSyncCycle = GetNextSyncCompletionCycle(drive, stream, track, cyclesPerBit);
            var wordEqualExpireCycle = _dskbytrWordEqualUntilCycle >= stream.Cycle
                ? _dskbytrWordEqualUntilCycle + 1
                : long.MaxValue;
            return Math.Min(Math.Min(nextByteCycle, nextSyncCycle), wordEqualExpireCycle);
        }

        private long GetNextSyncCompletionCycle(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            double cyclesPerBit)
        {
            var length = track.BitLength;
            if (length < 16)
            {
                return long.MaxValue;
            }

            var syncOffsetCount = GetSyncOffsets(drive, stream, track, length);
            if (syncOffsetCount <= 0)
            {
                return long.MaxValue;
            }

            var startOffset = (int)Math.Floor(stream.Position - 16.0) + 1;
            var syncOffsetIndex = startOffset <= 0
                ? 0
                : LowerBound(stream.SyncCacheOffsets, syncOffsetCount, startOffset);
            var rotationBase = 0.0;
            if (syncOffsetIndex >= syncOffsetCount)
            {
                syncOffsetIndex = 0;
                rotationBase = length;
            }

            while (true)
            {
                var completionPosition = rotationBase + stream.SyncCacheOffsets[syncOffsetIndex] + 16;
                if (completionPosition > stream.Position)
                {
                    return stream.Cycle + CyclesForBits(completionPosition - stream.Position, cyclesPerBit);
                }

                AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
            }
        }

        private void UpdateDskbytrData(AmigaPreparedTrack track, double startPosition, double endPositionAbsolute)
        {
            var completedBefore = (long)Math.Floor(startPosition / 8.0);
            var completedAfter = (long)Math.Floor(endPositionAbsolute / 8.0);
            if (completedAfter <= completedBefore)
            {
                return;
            }

            var byteStartBit = Mod((int)((completedAfter - 1) * 8), track.BitLength);
            _dskbytrData = ReadPreparedByte(track, byteStartBit);
            if (completedAfter >= 2)
            {
                var wordStartBit = Mod((int)((completedAfter - 2) * 8), track.BitLength);
                _diskDataRegister = ReadPreparedUInt16(track, wordStartBit);
            }

            _dskbytrByteReady = true;
        }

        private void SignalSyncMatches(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaPreparedTrack track,
            long startCycle,
            double startPosition,
            double endPositionAbsolute,
            double cyclesPerBit)
        {
            var length = track.BitLength;
            if (length < 16)
            {
                return;
            }

            var syncOffsetCount = GetSyncOffsets(drive, stream, track, length);
            if (syncOffsetCount <= 0)
            {
                return;
            }

            var startOffset = (int)Math.Floor(startPosition - 16.0) + 1;
            var syncOffsetIndex = startOffset <= 0
                ? 0
                : LowerBound(stream.SyncCacheOffsets, syncOffsetCount, startOffset);
            var rotationBase = 0.0;
            if (syncOffsetIndex >= syncOffsetCount)
            {
                syncOffsetIndex = 0;
                rotationBase = length;
            }

            while (true)
            {
                var completionPosition = rotationBase + stream.SyncCacheOffsets[syncOffsetIndex] + 16;
                if (completionPosition <= startPosition)
                {
                    AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
                    continue;
                }

                if (completionPosition > endPositionAbsolute)
                {
                    break;
                }

                var matchCycle = startCycle + CyclesForBits(completionPosition - startPosition, cyclesPerBit);
                _dskbytrWordEqualUntilCycle = Math.Max(_dskbytrWordEqualUntilCycle, matchCycle + DiskWordEqualHoldCycles);
                _bus.WriteDeviceWord(
                    AmigaBusRequester.Disk,
                    AmigaBusAccessKind.DiskDma,
                    0x00DFF09C,
                    (ushort)(0x8000 | DskSynInterrupt),
                    matchCycle);
                AdvanceSyncCompletionCursor(syncOffsetCount, length, ref syncOffsetIndex, ref rotationBase);
            }
        }

        private static void AdvanceSyncCompletionCursor(
            int syncOffsetCount,
            int trackLength,
            ref int syncOffsetIndex,
            ref double rotationBase)
        {
            syncOffsetIndex++;
            if (syncOffsetIndex < syncOffsetCount)
            {
                return;
            }

            syncOffsetIndex = 0;
            rotationBase += trackLength;
        }

        private AmigaPreparedTrack GetPreparedTrack(AmigaFloppyDrive drive, DiskStreamState stream, AmigaEncodedTrack track)
            => GetPreparedTrack(drive, stream, track, drive.Cylinder, drive.Head);

        private AmigaPreparedTrack GetPreparedTrack(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaEncodedTrack track,
            int cylinder,
            int head)
        {
            if (stream.PreparedTrackValid &&
                stream.PreparedTrackCylinder == cylinder &&
                stream.PreparedTrackHead == head &&
                stream.PreparedTrack.Matches(track))
            {
                if (_specializationEnabled)
                {
                    _preparedTrackHits++;
                }

                return stream.PreparedTrack;
            }

            if (_specializationEnabled)
            {
                _preparedTrackMisses++;
            }

            stream.PreparedTrack.Prepare(track);
            stream.PreparedTrackValid = true;
            stream.PreparedTrackCylinder = cylinder;
            stream.PreparedTrackHead = head;
            return stream.PreparedTrack;
        }

        private ushort ReadPreparedUInt16(AmigaPreparedTrack track, int bitOffset)
        {
            if (_specializationEnabled && track.HasRollingWords)
            {
                _rollingWindowHits++;
            }
            else if (_specializationEnabled)
            {
                _rollingWindowMisses++;
            }

            return track.ReadUInt16(bitOffset);
        }

        private byte ReadPreparedByte(AmigaPreparedTrack track, int bitOffset)
        {
            if (_specializationEnabled && track.HasRollingWords)
            {
                _rollingWindowHits++;
            }
            else if (_specializationEnabled)
            {
                _rollingWindowMisses++;
            }

            return track.ReadByte(bitOffset);
        }

        private int GetSyncOffsets(AmigaFloppyDrive drive, DiskStreamState stream, AmigaPreparedTrack track, int length)
        {
            if (stream.SyncCacheWord == _dsksync &&
                stream.SyncCacheCylinder == drive.Cylinder &&
                stream.SyncCacheHead == drive.Head &&
                stream.SyncCacheTrackLength == length)
            {
                if (_specializationEnabled)
                {
                    _syncIndexHits++;
                }

                return stream.SyncCacheOffsetCount;
            }

            var offsetCount = track.CopySyncOffsets(_dsksync, stream.SyncCacheOffsets, out var preparedCacheHit);
            if (_specializationEnabled && preparedCacheHit)
            {
                _syncIndexHits++;
            }
            else if (_specializationEnabled)
            {
                _syncIndexMisses++;
            }

            stream.SyncCacheWord = _dsksync;
            stream.SyncCacheCylinder = drive.Cylinder;
            stream.SyncCacheHead = drive.Head;
            stream.SyncCacheTrackLength = length;
            stream.SyncCacheOffsetCount = offsetCount;
            return stream.SyncCacheOffsetCount;
        }

        private static double GetFirstCompletionAfter(double completionPosition, int trackLength, double startPosition)
        {
            if (completionPosition <= startPosition)
            {
                var rotations = Math.Floor((startPosition - completionPosition) / trackLength) + 1;
                completionPosition += rotations * trackLength;
            }

            return completionPosition;
        }

        private static long CyclesForBits(double bitCount, double cyclesPerBit)
        {
            return Math.Max(0, (long)Math.Ceiling(bitCount * cyclesPerBit));
        }

        private static long PredictReadDmaCompletionCycle(
            AmigaPreparedTrack track,
            ushort sync,
            int sourceStartBit,
            long dataStartCycle,
            ushort requestedWords,
            double cyclesPerBit,
            bool wordSyncEnabled,
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount)
        {
            if (!wordSyncEnabled)
            {
                return dataStartCycle + (CyclesForBits(16, cyclesPerBit) * requestedWords);
            }

            var sourceBit = sourceStartBit;
            var wordStartCycle = dataStartCycle;
            var completionCycle = dataStartCycle;
            for (var word = 0; word < requestedWords; word++)
            {
                var plan = GetReadDmaWordPlan(
                    track,
                    sync,
                    word,
                    sourceBit,
                    wordStartCycle,
                    cyclesPerBit,
                    wordSyncEnabled,
                    syncOffsets,
                    syncOffsetCount);
                sourceBit = plan.NextSourceBit;
                wordStartCycle = plan.NextWordStartCycle;
                completionCycle = plan.CompletionCycle;
            }

            return completionCycle;
        }

        private static ActiveDmaWordPlan GetReadDmaWordPlan(
            AmigaPreparedTrack track,
            ushort sync,
            int wordIndex,
            int sourceBit,
            long wordStartCycle,
            double cyclesPerBit,
            bool wordSyncEnabled,
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount)
        {
            var nextSourceBit = (sourceBit + 16) % track.BitLength;
            var bitsUntilNextWord = 16;
            if (wordSyncEnabled &&
                wordIndex > 0 &&
                TryFindSyncWithinNextWord(track, sync, sourceBit, syncOffsets, syncOffsetCount, out var syncDistance, out var syncOffset))
            {
                nextSourceBit = (syncOffset + 16) % track.BitLength;
                bitsUntilNextWord = syncDistance + 16;
            }

            var completionCycle = wordStartCycle + CyclesForBits(16, cyclesPerBit);
            return new ActiveDmaWordPlan(
                sourceBit,
                nextSourceBit,
                completionCycle,
                wordStartCycle + CyclesForBits(bitsUntilNextWord, cyclesPerBit));
        }

        private static bool TryFindSyncWithinNextWord(
            AmigaPreparedTrack track,
            ushort sync,
            int sourceBit,
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount,
            out int distance,
            out int syncOffset)
        {
            if (syncOffsetCount > 0 && syncOffsetCount < DiskStreamState.MaxSyncCacheOffsets)
            {
                return TryFindCachedSyncWithinNextWord(
                    syncOffsets,
                    syncOffsetCount,
                    track.BitLength,
                    sourceBit,
                    out distance,
                    out syncOffset);
            }

            return track.TryFindSyncWithinNextWord(sync, sourceBit, out distance, out syncOffset);
        }

        private static bool TryFindCachedSyncWithinNextWord(
            ReadOnlySpan<int> syncOffsets,
            int syncOffsetCount,
            int trackLength,
            int sourceBit,
            out int distance,
            out int syncOffset)
        {
            var start = sourceBit + 1;
            var stop = sourceBit + 15;
            if (stop < trackLength)
            {
                var index = LowerBound(syncOffsets, syncOffsetCount, start);
                if (index < syncOffsetCount && syncOffsets[index] <= stop)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }
            }
            else
            {
                var index = LowerBound(syncOffsets, syncOffsetCount, start);
                if (index < syncOffsetCount)
                {
                    syncOffset = syncOffsets[index];
                    distance = syncOffset - sourceBit;
                    return true;
                }

                var wrappedStop = stop - trackLength;
                if (syncOffsets[0] <= wrappedStop)
                {
                    syncOffset = syncOffsets[0];
                    distance = (trackLength - sourceBit) + syncOffset;
                    return true;
                }
            }

            distance = 0;
            syncOffset = -1;
            return false;
        }

        private void SetStreamPosition(DiskStreamState stream, double position, long cycle)
        {
            stream.Position = position;
            stream.Offset = Math.Max(0, (int)Math.Floor(position));
            stream.Cycle = cycle;
            InvalidateNextDiskInputAdvanceCycle();
        }

        private void InvalidateNextDiskInputAdvanceCycle()
        {
            _nextDiskInputAdvanceCycle = 0;
        }

        private int FindSyncOffset(
            AmigaPreparedTrack track,
            int startOffset,
            ReadOnlySpan<int> syncOffsets = default,
            int syncOffsetCount = 0)
        {
            var length = track.BitLength;
            if (length < 16)
            {
                return -1;
            }

            startOffset = Mod(startOffset, length);
            if (syncOffsetCount > 0 && syncOffsetCount < DiskStreamState.MaxSyncCacheOffsets)
            {
                var index = LowerBound(syncOffsets, syncOffsetCount, startOffset);
                var offset = index < syncOffsetCount ? syncOffsets[index] : syncOffsets[0];
                return track.RewindConsecutiveSync(_dsksync, offset);
            }

            return track.FindSyncOffset(_dsksync, startOffset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int LowerBound(ReadOnlySpan<int> values, int count, int target)
        {
            var low = 0;
            var high = count;
            while (low < high)
            {
                var mid = (int)((uint)(low + high) >> 1);
                if (values[mid] < target)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private static int Mod(int value, int modulus)
        {
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private sealed class DiskStreamState
        {
            public const int MaxSyncCacheOffsets = 16384;
            private const int MaxPreparedTrackBits = 262144;

            public int Cylinder { get; set; }

            public int Head { get; set; }

            public int Offset { get; set; }

            public double Position { get; set; }

            public long Cycle { get; set; }

            public ushort SyncCacheWord { get; set; }

            public int SyncCacheCylinder { get; set; }

            public int SyncCacheHead { get; set; }

            public int SyncCacheTrackLength { get; set; }

            public int[] SyncCacheOffsets { get; } = new int[MaxSyncCacheOffsets];

            public int SyncCacheOffsetCount { get; set; }

            public AmigaPreparedTrack PreparedTrack { get; }

            public bool PreparedTrackValid { get; set; }

            public int PreparedTrackCylinder { get; set; }

            public int PreparedTrackHead { get; set; }

            public DiskStreamState()
            {
                PreparedTrack = new AmigaPreparedTrack(MaxPreparedTrackBits, MaxSyncCacheOffsets);
            }

            public void Reset()
            {
                Cylinder = 0;
                Head = 0;
                Offset = 0;
                Position = 0;
                Cycle = 0;
                PreparedTrack.Clear();
                PreparedTrackValid = false;
                PreparedTrackCylinder = -1;
                PreparedTrackHead = -1;
                InvalidateSyncCache();
            }

            public void InvalidateSyncCache()
            {
                SyncCacheWord = 0;
                SyncCacheCylinder = -1;
                SyncCacheHead = -1;
                SyncCacheTrackLength = 0;
                SyncCacheOffsetCount = 0;
            }
        }

        private readonly record struct ActiveDmaWordPlan(
            int SourceBit,
            int NextSourceBit,
            long CompletionCycle,
            long NextWordStartCycle);
    }

    internal readonly struct AmigaDiskControllerSnapshot
    {
        public AmigaDiskControllerSnapshot(
            uint diskPointer,
            ushort dsklen,
            ushort dsksync,
            ushort dskbytr,
            int cylinder,
            int head,
            bool motorOn,
            bool selected,
            byte ciabPortB,
            int transferCount,
            int lastTransferWords,
            int lastTransferDrive,
            int lastTransferCylinder,
            int lastTransferHead,
            uint lastTransferAddress,
            int selectedDrive,
            int activeDmaDrive,
            bool activeDma,
            long activeDmaCompletionCycle,
            ushort dskdatr,
            int connectedDriveCount,
            AmigaDiskSpecializationCounters specializationCounters)
        {
            DiskPointer = diskPointer;
            Dsklen = dsklen;
            Dsksync = dsksync;
            Dskbytr = dskbytr;
            Cylinder = cylinder;
            Head = head;
            MotorOn = motorOn;
            Selected = selected;
            CiabPortB = ciabPortB;
            TransferCount = transferCount;
            LastTransferWords = lastTransferWords;
            LastTransferDrive = lastTransferDrive;
            LastTransferCylinder = lastTransferCylinder;
            LastTransferHead = lastTransferHead;
            LastTransferAddress = lastTransferAddress;
            SelectedDrive = selectedDrive;
            ActiveDmaDrive = activeDmaDrive;
            ActiveDma = activeDma;
            ActiveDmaCompletionCycle = activeDmaCompletionCycle;
            Dskdatr = dskdatr;
            ConnectedDriveCount = connectedDriveCount;
            SpecializationCounters = specializationCounters;
        }

        public uint DiskPointer { get; }

        public ushort Dsklen { get; }

        public ushort Dsksync { get; }

        public ushort Dskbytr { get; }

        public int Cylinder { get; }

        public int Head { get; }

        public bool MotorOn { get; }

        public bool Selected { get; }

        public byte CiabPortB { get; }

        public int TransferCount { get; }

        public int LastTransferWords { get; }

        public int LastTransferDrive { get; }

        public int LastTransferCylinder { get; }

        public int LastTransferHead { get; }

        public uint LastTransferAddress { get; }

        public int SelectedDrive { get; }

        public int ActiveDmaDrive { get; }

        public bool ActiveDma { get; }

        public long ActiveDmaCompletionCycle { get; }

        public ushort Dskdatr { get; }

        public int ConnectedDriveCount { get; }

        public AmigaDiskSpecializationCounters SpecializationCounters { get; }
    }

    internal enum AmigaDiskDmaTraceKind
    {
        Started,
        Completed,
        Cancelled,
        Stopped,
        SyncMissing
    }

    internal readonly struct AmigaDiskDmaTraceEntry
    {
        public AmigaDiskDmaTraceEntry(
            AmigaDiskDmaTraceKind kind,
            int transferCount,
            long cycle,
            int drive,
            int cylinder,
            int head,
            int selectedDrive,
            ushort dsklen,
            ushort dsksync,
            ushort adkcon,
            ushort dskbytr,
            ushort dskdatr,
            uint targetAddress,
            int requestedWords,
            int transferredWords,
            int sourceBit,
            int syncWaitBits,
            int trackBitLength,
            bool wordSyncEnabled,
            long completionCycle)
        {
            Kind = kind;
            TransferCount = transferCount;
            Cycle = cycle;
            Drive = drive;
            Cylinder = cylinder;
            Head = head;
            SelectedDrive = selectedDrive;
            Dsklen = dsklen;
            Dsksync = dsksync;
            Adkcon = adkcon;
            Dskbytr = dskbytr;
            Dskdatr = dskdatr;
            TargetAddress = targetAddress;
            RequestedWords = requestedWords;
            TransferredWords = transferredWords;
            SourceBit = sourceBit;
            SyncWaitBits = syncWaitBits;
            TrackBitLength = trackBitLength;
            WordSyncEnabled = wordSyncEnabled;
            CompletionCycle = completionCycle;
        }

        public AmigaDiskDmaTraceKind Kind { get; }

        public int TransferCount { get; }

        public long Cycle { get; }

        public int Drive { get; }

        public int Cylinder { get; }

        public int Head { get; }

        public int SelectedDrive { get; }

        public ushort Dsklen { get; }

        public ushort Dsksync { get; }

        public ushort Adkcon { get; }

        public ushort Dskbytr { get; }

        public ushort Dskdatr { get; }

        public uint TargetAddress { get; }

        public int RequestedWords { get; }

        public int TransferredWords { get; }

        public int SourceBit { get; }

        public int SyncWaitBits { get; }

        public int TrackBitLength { get; }

        public bool WordSyncEnabled { get; }

        public long CompletionCycle { get; }
    }

    internal static class AmigaDosTrackEncoder
    {
        private const int EncodedSectorBytes = 0x440;
        private const int EncodedTrackGapBytes = 0x140;
        internal const int EncodedTrackBytes = (EncodedSectorBytes * AmigaDiskImage.SectorsPerTrack) + EncodedTrackGapBytes;
        private const uint MfmDataMask = 0x5555_5555;
        private static readonly int[] PhysicalSectorOrder = { 9, 10, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

        public static byte[] EncodeTrack(IAmigaSectorDiskMedia disk, int cylinder, int head)
        {
            ArgumentNullException.ThrowIfNull(disk);
            if (cylinder < 0 || cylinder >= AmigaDiskImage.CylinderCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cylinder));
            }

            if (head < 0 || head >= AmigaDiskImage.HeadCount)
            {
                throw new ArgumentOutOfRangeException(nameof(head));
            }

            var track = Enumerable.Repeat((byte)0xAA, EncodedTrackBytes).ToArray();
            var trackNumber = (cylinder * AmigaDiskImage.HeadCount) + head;
            for (var physicalIndex = 0; physicalIndex < PhysicalSectorOrder.Length; physicalIndex++)
            {
                var sector = PhysicalSectorOrder[physicalIndex];
                var sectorsUntilGap = AmigaDiskImage.SectorsPerTrack - physicalIndex;
                EncodeSector(
                    disk.ReadSector(cylinder, head, sector),
                    track.AsSpan(physicalIndex * EncodedSectorBytes, EncodedSectorBytes),
                    trackNumber,
                    sector,
                    sectorsUntilGap);
            }

            return track;
        }

        public static byte[] CreateUnformattedTrack()
        {
            return Enumerable.Repeat((byte)0xAA, EncodedTrackBytes).ToArray();
        }

        private static void EncodeSector(ReadOnlySpan<byte> source, Span<byte> destination, int trackNumber, int sector, int sectorsUntilGap)
        {
            destination.Fill(0xAA);
            BigEndian.WriteUInt16(destination, 0x04, 0x4489);
            BigEndian.WriteUInt16(destination, 0x06, 0x4489);

            var header = 0xFF00_0000u | ((uint)trackNumber << 16) | ((uint)sector << 8) | (uint)sectorsUntilGap;
            Span<uint> headerAndLabel = stackalloc uint[10];
            WriteOddEvenPair(headerAndLabel, 0, header);

            for (var i = 0; i < 4; i++)
            {
                WriteOddEvenSplit(headerAndLabel, 2 + i, 6 + i, 0);
            }

            Span<uint> data = stackalloc uint[256];
            for (var i = 0; i < source.Length / 4; i++)
            {
                var value = BigEndian.ReadUInt32(source, i * 4, "ADF sector longword");
                WriteOddEvenSplit(data, i, 128 + i, value);
            }

            var headerChecksum = ComputeMfmChecksum(headerAndLabel);
            var dataChecksum = ComputeMfmChecksum(data);
            var previousDataBit = (BigEndian.ReadUInt16(destination, 0x06, "MFM sync word") & 1) != 0;
            WriteMfmLongs(destination, 0x08, headerAndLabel, ref previousDataBit);
            WriteOddEvenPair(destination, 0x30, headerChecksum, ref previousDataBit);
            WriteOddEvenPair(destination, 0x38, dataChecksum, ref previousDataBit);
            WriteMfmLongs(destination, 0x40, data, ref previousDataBit);
        }

        private static void WriteOddEvenPair(Span<uint> destination, int offset, uint value)
        {
            destination[offset] = Odd(value);
            destination[offset + 1] = Even(value);
        }

        private static void WriteOddEvenSplit(Span<uint> destination, int oddOffset, int evenOffset, uint value)
        {
            destination[oddOffset] = Odd(value);
            destination[evenOffset] = Even(value);
        }

        private static void WriteOddEvenPair(Span<byte> destination, int offset, uint value, ref bool previousDataBit)
        {
            WriteMfmLong(destination, offset, Odd(value), ref previousDataBit);
            WriteMfmLong(destination, offset + 4, Even(value), ref previousDataBit);
        }

        private static void WriteMfmLongs(Span<byte> destination, int offset, ReadOnlySpan<uint> values, ref bool previousDataBit)
        {
            for (var i = 0; i < values.Length; i++)
            {
                WriteMfmLong(destination, offset + (i * 4), values[i], ref previousDataBit);
            }
        }

        private static void WriteMfmLong(Span<byte> destination, int offset, uint dataBits, ref bool previousDataBit)
        {
            BigEndian.WriteUInt32(destination, offset, EncodeMfmDataBits(dataBits, ref previousDataBit));
        }

        private static uint EncodeMfmDataBits(uint dataBits, ref bool previousDataBit)
        {
            dataBits &= MfmDataMask;
            var result = dataBits;
            for (var dataBit = 30; dataBit >= 0; dataBit -= 2)
            {
                var currentDataBit = ((dataBits >> dataBit) & 1) != 0;
                if (!previousDataBit && !currentDataBit)
                {
                    result |= 1u << (dataBit + 1);
                }

                previousDataBit = currentDataBit;
            }

            return result;
        }

        private static uint ComputeMfmChecksum(ReadOnlySpan<uint> encodedLongs)
        {
            var checksum = 0u;
            for (var offset = 0; offset < encodedLongs.Length; offset++)
            {
                checksum ^= encodedLongs[offset];
            }

            return checksum & MfmDataMask;
        }

        private static uint Odd(uint value)
        {
            return (value >> 1) & MfmDataMask;
        }

        private static uint Even(uint value)
        {
            return value & MfmDataMask;
        }
    }

    internal static class AmigaDosTrackDecoder
    {
        private const ushort SyncWord = 0x4489;
        private const uint MfmDataMask = 0x5555_5555;
        private const int TrackCount = AmigaDiskImage.CylinderCount * AmigaDiskImage.HeadCount;
        private const int EncodedSectorBytesAfterSync = 0x43C;
        private const int EncodedSectorBitsAfterSync = EncodedSectorBytesAfterSync * 8;

        public static byte[] DecodeBestEffort(byte[][] encodedTracks, out bool hasCompleteSectorData)
        {
            ArgumentNullException.ThrowIfNull(encodedTracks);
            var tracks = new AmigaEncodedTrack[encodedTracks.Length];
            for (var index = 0; index < encodedTracks.Length; index++)
            {
                tracks[index] = AmigaEncodedTrack.FromBytes(encodedTracks[index] ?? AmigaDosTrackEncoder.CreateUnformattedTrack());
            }

            return DecodeBestEffort(tracks, out hasCompleteSectorData);
        }

        public static byte[] DecodeBestEffort(AmigaEncodedTrack[] encodedTracks, out bool hasCompleteSectorData)
        {
            ArgumentNullException.ThrowIfNull(encodedTracks);
            if (encodedTracks.Length != TrackCount)
            {
                throw new ArgumentException($"Exactly {TrackCount} encoded tracks are required.", nameof(encodedTracks));
            }

            var data = new byte[AmigaDiskImage.StandardAdfSize];
            var decodedSectors = new bool[TrackCount * AmigaDiskImage.SectorsPerTrack];
            var decodedSectorCount = 0;
            for (var trackNumber = 0; trackNumber < encodedTracks.Length; trackNumber++)
            {
                var track = encodedTracks[trackNumber];
                if (track.BitLength < EncodedSectorBitsAfterSync)
                {
                    continue;
                }

                DecodeTrack(track, trackNumber, data, decodedSectors, ref decodedSectorCount);
            }

            hasCompleteSectorData = decodedSectorCount == decodedSectors.Length;
            return data;
        }

        private static void DecodeTrack(
            AmigaEncodedTrack track,
            int expectedTrackNumber,
            byte[] diskData,
            bool[] decodedSectors,
            ref int decodedSectorCount)
        {
            if (track.BitLength < EncodedSectorBitsAfterSync)
            {
                return;
            }

            for (var offset = 0; offset < track.BitLength; offset++)
            {
                if (track.ReadUInt16(offset) != SyncWord ||
                    track.ReadUInt16(offset + 16) != SyncWord)
                {
                    continue;
                }

                TryDecodeSector(track, offset, expectedTrackNumber, diskData, decodedSectors, ref decodedSectorCount);
            }
        }

        private static void TryDecodeSector(
            AmigaEncodedTrack track,
            int syncOffset,
            int expectedTrackNumber,
            byte[] diskData,
            bool[] decodedSectors,
            ref int decodedSectorCount)
        {
            var header = DecodeOddEven(ReadMfmLong(track, syncOffset + (0x04 * 8)), ReadMfmLong(track, syncOffset + (0x08 * 8)));
            if ((header & 0xFF00_0000) != 0xFF00_0000)
            {
                return;
            }

            var trackNumber = (int)((header >> 16) & 0xFF);
            var sector = (int)((header >> 8) & 0xFF);
            if (trackNumber != expectedTrackNumber ||
                (uint)trackNumber >= TrackCount ||
                (uint)sector >= AmigaDiskImage.SectorsPerTrack)
            {
                return;
            }

            var decodedHeaderChecksum = DecodeOddEven(
                ReadMfmLong(track, syncOffset + (0x2C * 8)),
                ReadMfmLong(track, syncOffset + (0x30 * 8)));
            var calculatedHeaderChecksum = ComputeMfmChecksum(track, syncOffset + (0x04 * 8), 10);
            if ((decodedHeaderChecksum & MfmDataMask) != calculatedHeaderChecksum)
            {
                return;
            }

            var decodedDataChecksum = DecodeOddEven(
                ReadMfmLong(track, syncOffset + (0x34 * 8)),
                ReadMfmLong(track, syncOffset + (0x38 * 8)));
            var calculatedDataChecksum = ComputeMfmChecksum(track, syncOffset + (0x3C * 8), 256);
            if ((decodedDataChecksum & MfmDataMask) != calculatedDataChecksum)
            {
                return;
            }

            var logicalSector = (trackNumber * AmigaDiskImage.SectorsPerTrack) + sector;
            var destinationOffset = logicalSector * AmigaDiskImage.SectorSize;
            for (var longIndex = 0; longIndex < AmigaDiskImage.SectorSize / 4; longIndex++)
            {
                var odd = ReadMfmLong(track, syncOffset + (0x3C * 8) + (longIndex * 32));
                var even = ReadMfmLong(track, syncOffset + (0x3C * 8) + (((AmigaDiskImage.SectorSize / 4) + longIndex) * 32));
                BigEndian.WriteUInt32(diskData, destinationOffset + (longIndex * 4), DecodeOddEven(odd, even));
            }

            if (!decodedSectors[logicalSector])
            {
                decodedSectors[logicalSector] = true;
                decodedSectorCount++;
            }
        }

        private static uint ComputeMfmChecksum(AmigaEncodedTrack track, int bitOffset, int longCount)
        {
            var checksum = 0u;
            for (var index = 0; index < longCount; index++)
            {
                checksum ^= ReadMfmLong(track, bitOffset + (index * 32));
            }

            return checksum & MfmDataMask;
        }

        private static uint ReadMfmLong(AmigaEncodedTrack track, int bitOffset)
        {
            return track.ReadUInt32(bitOffset) & MfmDataMask;
        }

        private static uint DecodeOddEven(uint odd, uint even)
        {
            return ((odd & MfmDataMask) << 1) | (even & MfmDataMask);
        }

    }

    internal interface IAmigaDiskDmaEngine
    {
        void ReadBytesToChipRam(AmigaFloppyDrive drive, AmigaBus bus, int diskByteOffset, int byteCount, uint destinationAddress, long requestedCycle);
    }

    internal sealed class ImmediateDiskDmaEngine : IAmigaDiskDmaEngine
    {
        public void ReadBytesToChipRam(AmigaFloppyDrive drive, AmigaBus bus, int diskByteOffset, int byteCount, uint destinationAddress, long requestedCycle)
        {
            _ = requestedCycle;
            ArgumentNullException.ThrowIfNull(drive);
            ArgumentNullException.ThrowIfNull(bus);
            bus.CopyToChipRam(destinationAddress, drive.ReadBytes(diskByteOffset, byteCount));
        }
    }

    internal sealed class CycleExactDiskDmaEngine : IAmigaDiskDmaEngine
    {
        public void ReadBytesToChipRam(AmigaFloppyDrive drive, AmigaBus bus, int diskByteOffset, int byteCount, uint destinationAddress, long requestedCycle)
        {
            throw new NotImplementedException("Cycle-exact Amiga disk DMA is reserved for a later accuracy pass.");
        }
    }
}
