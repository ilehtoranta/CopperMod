using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

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

    internal interface IAmigaDiskMedia
    {
        string Name { get; }

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
        private readonly AmigaFloppyDrive[] _drives;
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

        public AmigaDiskController(AmigaBus bus, int connectedDriveCount = 1)
        {
            if (connectedDriveCount is < 1 or > MaxFloppyDriveCount)
            {
                throw new ArgumentOutOfRangeException(nameof(connectedDriveCount), connectedDriveCount, "The connected floppy drive count must be between 1 and 4.");
            }

            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
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
                ConnectedDriveCount);
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
            _lastTransferWords = 0;
            _lastTransferDrive = -1;
            _lastTransferCylinder = 0;
            _lastTransferHead = 0;
            _lastTransferAddress = 0;
            _armedDsklen = null;
            _nextIndexPulseCycle = DiskIndexPulseCycles;
            foreach (var stream in _streams)
            {
                stream.Reset();
            }

            _pendingReadDmaWords = 0;
            ClearActiveDma();
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
            var cyclesPerBit = GetDiskDmaCyclesPerBit(track.BitLength);
            AdvanceDiskInputTo(cycle);
            ResetStreamIfTrackChanged(drive, stream, cycle);

            var sourceStartBit = stream.Offset;
            var syncWaitBits = 0;
            var wordSyncEnabled = (_bus.Paula.Adkcon & 0x0400) != 0;
            if (wordSyncEnabled)
            {
                var syncOffset = FindSyncOffset(track, _dsksync, stream.Offset);
                if (syncOffset < 0)
                {
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
            var completionCycle = PredictReadDmaCompletionCycle(track, _dsksync, sourceStartBit, dataStartCycle, requestedWords, cyclesPerBit, wordSyncEnabled);
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
                while (_activeDmaTransferredWords < _activeDmaRequestedWords)
                {
                    var word = _activeDmaTransferredWords;
                    var plan = GetReadDmaWordPlan(
                        track,
                        _dsksync,
                        word,
                        _activeDmaNextSourceBit,
                        _activeDmaNextWordStartCycle,
                        _activeDmaCyclesPerBit,
                        _activeDmaWordSyncEnabled);
                    if (plan.CompletionCycle > advanceCycle)
                    {
                        break;
                    }

                    var value = track.ReadUInt16(plan.SourceBit);
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

            AdvanceActiveDmaTo(cycle);
            if (!_activeDma)
            {
                return;
            }

            var elapsedCycles = Math.Max(0, cycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            ClearActiveDma();
        }

        private void StopActiveDma(long cycle)
        {
            var elapsedCycles = Math.Max(0, cycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerBit)) % _activeDmaTrackBitLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            ClearActiveDma();
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
                AdvanceInputStreamTo(drive, stream, track, GetDiskDmaCyclesPerBit(track.BitLength), cycle, drive.Selected);
            }
        }

        private void AdvanceInputStreamTo(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaEncodedTrack track,
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

        private void UpdateDskbytrData(AmigaEncodedTrack track, double startPosition, double endPositionAbsolute)
        {
            var completedBefore = (long)Math.Floor(startPosition / 8.0);
            var completedAfter = (long)Math.Floor(endPositionAbsolute / 8.0);
            if (completedAfter <= completedBefore)
            {
                return;
            }

            var byteStartBit = Mod((int)((completedAfter - 1) * 8), track.BitLength);
            _dskbytrData = track.ReadByte(byteStartBit);
            if (completedAfter >= 2)
            {
                var wordStartBit = Mod((int)((completedAfter - 2) * 8), track.BitLength);
                _diskDataRegister = track.ReadUInt16(wordStartBit);
            }

            _dskbytrByteReady = true;
        }

        private void SignalSyncMatches(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            AmigaEncodedTrack track,
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

            foreach (var offset in GetSyncOffsets(drive, stream, track, length))
            {
                var completionPosition = GetFirstCompletionAfter(offset + 16, length, startPosition);
                while (completionPosition <= endPositionAbsolute)
                {
                    var matchCycle = startCycle + CyclesForBits(completionPosition - startPosition, cyclesPerBit);
                    _dskbytrWordEqualUntilCycle = Math.Max(_dskbytrWordEqualUntilCycle, matchCycle + DiskWordEqualHoldCycles);
                    _bus.WriteDeviceWord(
                        AmigaBusRequester.Disk,
                        AmigaBusAccessKind.DiskDma,
                        0x00DFF09C,
                        (ushort)(0x8000 | DskSynInterrupt),
                        matchCycle);
                    completionPosition += length;
                }
            }
        }

        private int[] GetSyncOffsets(AmigaFloppyDrive drive, DiskStreamState stream, AmigaEncodedTrack track, int length)
        {
            if (stream.SyncCacheWord == _dsksync &&
                stream.SyncCacheCylinder == drive.Cylinder &&
                stream.SyncCacheHead == drive.Head &&
                stream.SyncCacheTrackLength == length)
            {
                return stream.SyncCacheOffsets;
            }

            var offsets = new List<int>();
            for (var offset = 0; offset < length; offset++)
            {
                if (track.ReadUInt16(offset) == _dsksync)
                {
                    offsets.Add(offset);
                }
            }

            stream.SyncCacheWord = _dsksync;
            stream.SyncCacheCylinder = drive.Cylinder;
            stream.SyncCacheHead = drive.Head;
            stream.SyncCacheTrackLength = length;
            stream.SyncCacheOffsets = offsets.ToArray();
            return stream.SyncCacheOffsets;
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
            AmigaEncodedTrack track,
            ushort sync,
            int sourceStartBit,
            long dataStartCycle,
            ushort requestedWords,
            double cyclesPerBit,
            bool wordSyncEnabled)
        {
            var sourceBit = sourceStartBit;
            var wordStartCycle = dataStartCycle;
            var completionCycle = dataStartCycle;
            for (var word = 0; word < requestedWords; word++)
            {
                var plan = GetReadDmaWordPlan(track, sync, word, sourceBit, wordStartCycle, cyclesPerBit, wordSyncEnabled);
                sourceBit = plan.NextSourceBit;
                wordStartCycle = plan.NextWordStartCycle;
                completionCycle = plan.CompletionCycle;
            }

            return completionCycle;
        }

        private static ActiveDmaWordPlan GetReadDmaWordPlan(
            AmigaEncodedTrack track,
            ushort sync,
            int wordIndex,
            int sourceBit,
            long wordStartCycle,
            double cyclesPerBit,
            bool wordSyncEnabled)
        {
            var nextSourceBit = (sourceBit + 16) % track.BitLength;
            var bitsUntilNextWord = 16;
            if (wordSyncEnabled &&
                wordIndex > 0 &&
                TryFindSyncWithinNextWord(track, sync, sourceBit, out var syncDistance, out var syncOffset))
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

        private static bool TryFindSyncWithinNextWord(AmigaEncodedTrack track, ushort sync, int sourceBit, out int distance, out int syncOffset)
        {
            if (track.BitLength < 16)
            {
                distance = 0;
                syncOffset = -1;
                return false;
            }

            for (var bitDistance = 1; bitDistance < 16; bitDistance++)
            {
                var offset = (sourceBit + bitDistance) % track.BitLength;
                if (track.ReadUInt16(offset) == sync)
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

        private void SetStreamPosition(DiskStreamState stream, double position, long cycle)
        {
            stream.Position = position;
            stream.Offset = Math.Max(0, (int)Math.Floor(position));
            stream.Cycle = cycle;
        }

        private static int FindSyncOffset(AmigaEncodedTrack track, ushort sync, int startOffset)
        {
            var length = track.BitLength;
            if (length < 16)
            {
                return -1;
            }

            startOffset = Mod(startOffset, length);
            for (var step = 0; step < length; step++)
            {
                var offset = (startOffset + step) % length;
                if (track.ReadUInt16(offset) == sync)
                {
                    return RewindConsecutiveSync(track, sync, offset);
                }
            }

            return -1;
        }

        private static int RewindConsecutiveSync(AmigaEncodedTrack track, ushort sync, int offset)
        {
            if (track.BitLength < 32)
            {
                return offset;
            }

            var current = offset;
            var maxSteps = Math.Max(1, track.BitLength / 16);
            for (var step = 0; step < maxSteps; step++)
            {
                var previous = (current - 16 + track.BitLength) % track.BitLength;
                if (previous == offset || track.ReadUInt16(previous) != sync)
                {
                    return current;
                }

                current = previous;
            }

            return current;
        }

        private static int Mod(int value, int modulus)
        {
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private sealed class DiskStreamState
        {
            public int Cylinder { get; set; }

            public int Head { get; set; }

            public int Offset { get; set; }

            public double Position { get; set; }

            public long Cycle { get; set; }

            public ushort SyncCacheWord { get; set; }

            public int SyncCacheCylinder { get; set; }

            public int SyncCacheHead { get; set; }

            public int SyncCacheTrackLength { get; set; }

            public int[] SyncCacheOffsets { get; set; } = Array.Empty<int>();

            public void Reset()
            {
                Cylinder = 0;
                Head = 0;
                Offset = 0;
                Position = 0;
                Cycle = 0;
                InvalidateSyncCache();
            }

            public void InvalidateSyncCache()
            {
                SyncCacheWord = 0;
                SyncCacheCylinder = -1;
                SyncCacheHead = -1;
                SyncCacheTrackLength = 0;
                SyncCacheOffsets = Array.Empty<int>();
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
            int connectedDriveCount)
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
            ConnectedDriveCount = connectedDriveCount;
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

        public int ConnectedDriveCount { get; }
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
