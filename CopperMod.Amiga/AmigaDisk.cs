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

        public ReadOnlySpan<byte> ReadEncodedTrack(int cylinder, int head)
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

    internal interface IAmigaDiskMedia
    {
        string Name { get; }

        ReadOnlySpan<byte> ReadEncodedTrack(int cylinder, int head);
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

        public ReadOnlySpan<byte> ReadEncodedTrack(int cylinder, int head)
        {
            var index = AmigaDiskImage.GetTrackIndex(cylinder, head);
            return _encodedTracks[index] ??= AmigaDosTrackEncoder.EncodeTrack(this, cylinder, head);
        }
    }

    internal sealed class TrackBackedDiskMedia : IAmigaSectorDiskMedia
    {
        private readonly byte[][] _encodedTracks;

        public TrackBackedDiskMedia(byte[][] encodedTracks, string name)
        {
            ArgumentNullException.ThrowIfNull(encodedTracks);
            if (encodedTracks.Length != AmigaDiskImage.TrackCount)
            {
                throw new ArgumentException($"Exactly {AmigaDiskImage.TrackCount} encoded tracks are required.", nameof(encodedTracks));
            }

            _encodedTracks = encodedTracks;
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

        public ReadOnlySpan<byte> ReadEncodedTrack(int cylinder, int head)
        {
            var index = AmigaDiskImage.GetTrackIndex(cylinder, head);
            return _encodedTracks[index] ??= AmigaDosTrackEncoder.CreateUnformattedTrack();
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

        public bool Selected { get; private set; }

        public bool DiskChanged { get; private set; }

        public void Insert(AmigaDiskImage disk, bool markChanged = false)
        {
            Disk = disk ?? throw new ArgumentNullException(nameof(disk));
            DiskChanged = markChanged;
        }

        public void Eject()
        {
            Disk = null;
            DiskChanged = true;
        }

        public void ResetPosition()
        {
            Cylinder = 0;
            Head = 0;
            MotorOn = false;
            Selected = false;
        }

        public void SetHead(int head)
        {
            Head = head == 0 ? 0 : 1;
        }

        public void SetMotorOn(bool motorOn)
        {
            MotorOn = motorOn;
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
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

        public ReadOnlySpan<byte> ReadEncodedTrack(int cylinder, int head)
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
        private const ushort DskBytr = 0x01A;
        private const ushort DskPth = 0x020;
        private const ushort DskPtl = 0x022;
        private const ushort DskLen = 0x024;
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

        private readonly AmigaBus _bus;
        private readonly AmigaFloppyDrive[] _drives;
        private ushort _dsklen;
        private ushort _dsksync = 0x4489;
        private uint _diskPointer;
        private byte _ciabPortB = 0xFF;
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
        private int _activeDmaSourceStart;
        private ushort _activeDmaRequestedWords;
        private int _activeDmaTransferredWords;
        private int _activeDmaCylinder;
        private int _activeDmaHead;
        private int _activeDmaTrackLength;
        private double _activeDmaStreamStartPosition;
        private long _activeDmaStartCycle;
        private long _activeDmaDataStartCycle;
        private long _activeDmaCompletionCycle;
        private double _activeDmaCyclesPerByte;

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
            _dsklen = 0;
            _dsksync = 0x4489;
            _diskPointer = 0;
            _ciabPortB = 0xFF;
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
            value = SetActiveLow(value, 3, false);
            value = SetActiveLow(value, 4, drive.Disk != null && drive.Cylinder == 0);
            value = SetActiveLow(value, 5, drive.Disk != null && drive.MotorOn);
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
                DskBytr => ReadDskbytr(),
                DskPth => (ushort)((_diskPointer >> 16) & 0x0007),
                DskPtl => (ushort)(_diskPointer & 0xFFFE),
                DskLen => _dsklen,
                DskSync => _dsksync,
                _ => 0
            };
        }

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            offset = (ushort)(offset & 0x01FE);
            switch (offset)
            {
                case DskPth:
                    _diskPointer = (_diskPointer & 0x0000_FFFE) | ((uint)(value & 0x0007) << 16);
                    break;
                case DskPtl:
                    _diskPointer = (_diskPointer & 0x0007_0000) | (uint)(value & 0xFFFE);
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
                    drive.SetMotorOn((value & 0x80) == 0);
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
            return offset is DskBytr or DskPth or DskPtl or DskLen or DskSync;
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
                if (IsDriveConnected(driveIndex) && drive.Selected && drive.Disk != null && drive.MotorOn)
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
            var driveIndex = GetReadDmaDriveIndex();
            if (requestedWords == 0 || driveIndex < 0 || !IsDiskDmaControlEnabled())
            {
                return false;
            }

            CancelActiveDma(cycle);

            var drive = _drives[driveIndex];
            var stream = _streams[driveIndex];
            var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
            var cyclesPerByte = GetDiskDmaCyclesPerByte(track.Length);
            AdvanceDiskInputTo(cycle);
            ResetStreamIfTrackChanged(drive, stream, cycle);

            var sourceStart = stream.Offset;
            var syncWaitBytes = 0;
            if ((_bus.Paula.Adkcon & 0x0400) != 0)
            {
                var syncOffset = FindSyncOffset(track, _dsksync, stream.Offset);
                if (syncOffset < 0)
                {
                    return false;
                }

                sourceStart = (syncOffset + 2) % track.Length;
                syncWaitBytes = (sourceStart - stream.Offset + track.Length) % track.Length;
            }

            var targetAddress = _diskPointer & 0x00FF_FFFE;
            _transferCount++;
            _lastTransferWords = requestedWords;
            _lastTransferDrive = driveIndex;
            _lastTransferCylinder = drive.Cylinder;
            _lastTransferHead = drive.Head;
            _lastTransferAddress = targetAddress;
            var transferBytes = requestedWords * 2;
            var dataStartCycle = cycle + CyclesForBytes(syncWaitBytes, cyclesPerByte);
            var completionCycle = dataStartCycle + CyclesForBytes(transferBytes, cyclesPerByte);
            _activeDma = true;
            _activeDmaDrive = driveIndex;
            _activeDmaTargetAddress = targetAddress;
            _activeDmaSourceStart = sourceStart;
            _activeDmaRequestedWords = requestedWords;
            _activeDmaTransferredWords = 0;
            _activeDmaCylinder = drive.Cylinder;
            _activeDmaHead = drive.Head;
            _activeDmaTrackLength = track.Length;
            _activeDmaStreamStartPosition = stream.Position;
            _activeDmaStartCycle = cycle;
            _activeDmaDataStartCycle = dataStartCycle;
            _activeDmaCompletionCycle = completionCycle;
            _activeDmaCyclesPerByte = cyclesPerByte;
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
            var targetWords = GetActiveDmaTransferredWordsAt(advanceCycle);
            if (targetWords > _activeDmaTransferredWords)
            {
                var track = activeDrive.ReadEncodedTrack(_activeDmaCylinder, _activeDmaHead);
                for (var word = _activeDmaTransferredWords; word < targetWords; word++)
                {
                    var sourceOffset = (_activeDmaSourceStart + (word * 2)) % _activeDmaTrackLength;
                    var value = BigEndian.ReadUInt16(track, sourceOffset, "encoded disk DMA word");
                    var wordCycle = GetActiveDmaWordCycle(word);
                    _bus.WriteChipWordForDevice(
                        AmigaBusRequester.Disk,
                        AmigaBusAccessKind.DiskDma,
                        _activeDmaTargetAddress + (uint)(word * 2),
                        value,
                        wordCycle);
                }

                _activeDmaTransferredWords = targetWords;
                UpdateActiveDmaPointer();
                UpdateActiveDmaLength();
            }

            if (targetCycle < _activeDmaCompletionCycle)
            {
                return;
            }

            CompleteActiveDma();
        }

        private int GetActiveDmaTransferredWordsAt(long cycle)
        {
            var words = _activeDmaTransferredWords;
            while (words < _activeDmaRequestedWords && GetActiveDmaWordCycle(words) <= cycle)
            {
                words++;
            }

            return words;
        }

        private long GetActiveDmaWordCycle(int word)
        {
            return _activeDmaDataStartCycle + CyclesForBytes((word + 1) * 2, _activeDmaCyclesPerByte);
        }

        private void CompleteActiveDma()
        {
            var transferBytes = _activeDmaRequestedWords * 2;
            _activeDmaTransferredWords = _activeDmaRequestedWords;
            UpdateActiveDmaPointer();
            UpdateActiveDmaLength();
            SetStreamPosition(GetActiveDmaStream(), (_activeDmaSourceStart + transferBytes) % _activeDmaTrackLength, _activeDmaCompletionCycle);
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
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerByte)) % _activeDmaTrackLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            ClearActiveDma();
        }

        private void StopActiveDma(long cycle)
        {
            var elapsedCycles = Math.Max(0, cycle - _activeDmaStartCycle);
            var position = (_activeDmaStreamStartPosition + (elapsedCycles / _activeDmaCyclesPerByte)) % _activeDmaTrackLength;
            SetStreamPosition(GetActiveDmaStream(), position, cycle);
            UpdateActiveDmaLength();
            ClearActiveDma();
        }

        private void ClearActiveDma()
        {
            _activeDma = false;
            _activeDmaDrive = -1;
            _activeDmaTargetAddress = 0;
            _activeDmaSourceStart = 0;
            _activeDmaRequestedWords = 0;
            _activeDmaTransferredWords = 0;
            _activeDmaCylinder = 0;
            _activeDmaHead = 0;
            _activeDmaTrackLength = 0;
            _activeDmaStreamStartPosition = 0;
            _activeDmaStartCycle = 0;
            _activeDmaDataStartCycle = 0;
            _activeDmaCompletionCycle = 0;
            _activeDmaCyclesPerByte = 0;
        }

        private void UpdateActiveDmaPointer()
        {
            _diskPointer = (_activeDmaTargetAddress + (uint)(_activeDmaTransferredWords * 2)) & 0x0007_FFFE;
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

        private static double GetDiskDmaCyclesPerByte(int trackLength)
        {
            if (trackLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackLength), trackLength, "Encoded track length must be positive.");
            }

            return AmigaConstants.A500PalCpuClockHz / (trackLength * DiskRevolutionsPerSecond);
        }

        private void AdvanceStreamTo(DiskStreamState stream, int trackLength, double cyclesPerByte, long cycle)
        {
            if (cycle <= stream.Cycle)
            {
                return;
            }

            var elapsedCycles = cycle - stream.Cycle;
            var position = (stream.Position + (elapsedCycles / cyclesPerByte)) % trackLength;
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

                var track = drive.ReadEncodedTrack(drive.Cylinder, drive.Head);
                ResetStreamIfTrackChanged(drive, stream, cycle);
                AdvanceInputStreamTo(drive, stream, track, GetDiskDmaCyclesPerByte(track.Length), cycle, drive.Selected);
            }
        }

        private void AdvanceInputStreamTo(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            ReadOnlySpan<byte> track,
            double cyclesPerByte,
            long cycle,
            bool selected)
        {
            if (cycle <= stream.Cycle)
            {
                return;
            }

            var startCycle = stream.Cycle;
            var startPosition = stream.Position;
            var elapsedBytes = (cycle - startCycle) / cyclesPerByte;
            var endPositionAbsolute = startPosition + elapsedBytes;
            if (selected)
            {
                UpdateDskbytrData(track, startPosition, endPositionAbsolute);
                SignalSyncMatches(drive, stream, track, startCycle, startPosition, endPositionAbsolute, cyclesPerByte);
            }

            SetStreamPosition(stream, endPositionAbsolute % track.Length, cycle);
        }

        private void UpdateDskbytrData(ReadOnlySpan<byte> track, double startPosition, double endPositionAbsolute)
        {
            var completedBefore = (long)Math.Floor(startPosition);
            var completedAfter = (long)Math.Floor(endPositionAbsolute);
            if (completedAfter <= completedBefore)
            {
                return;
            }

            var byteIndex = Mod((int)(completedAfter - 1), track.Length);
            _dskbytrData = track[byteIndex];
            _dskbytrByteReady = true;
        }

        private void SignalSyncMatches(
            AmigaFloppyDrive drive,
            DiskStreamState stream,
            ReadOnlySpan<byte> track,
            long startCycle,
            double startPosition,
            double endPositionAbsolute,
            double cyclesPerByte)
        {
            var length = track.Length & ~1;
            if (length < 2)
            {
                return;
            }

            foreach (var offset in GetSyncOffsets(drive, stream, track, length))
            {
                var completionPosition = GetFirstCompletionAfter(offset + 2, length, startPosition);
                while (completionPosition <= endPositionAbsolute)
                {
                    var matchCycle = startCycle + CyclesForBytes(completionPosition - startPosition, cyclesPerByte);
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

        private int[] GetSyncOffsets(AmigaFloppyDrive drive, DiskStreamState stream, ReadOnlySpan<byte> track, int length)
        {
            if (stream.SyncCacheWord == _dsksync &&
                stream.SyncCacheCylinder == drive.Cylinder &&
                stream.SyncCacheHead == drive.Head &&
                stream.SyncCacheTrackLength == length)
            {
                return stream.SyncCacheOffsets;
            }

            var offsets = new List<int>();
            for (var offset = 0; offset + 1 < length; offset += 2)
            {
                if (BigEndian.ReadUInt16(track, offset, "encoded disk sync scan word") == _dsksync)
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

        private static long CyclesForBytes(double byteCount, double cyclesPerByte)
        {
            return Math.Max(0, (long)Math.Ceiling(byteCount * cyclesPerByte));
        }

        private void SetStreamPosition(DiskStreamState stream, double position, long cycle)
        {
            stream.Position = position;
            stream.Offset = (int)position & ~1;
            stream.Cycle = cycle;
        }

        private static int FindSyncOffset(ReadOnlySpan<byte> track, ushort sync, int startOffset)
        {
            var length = track.Length & ~1;
            if (length < 2)
            {
                return -1;
            }

            startOffset = Mod(startOffset & ~1, length);
            for (var step = 0; step < length; step += 2)
            {
                var offset = (startOffset + step) % length;
                if (BigEndian.ReadUInt16(track, offset, "encoded disk sync word") == sync)
                {
                    return RewindConsecutiveSync(track, sync, offset);
                }
            }

            return -1;
        }

        private static int RewindConsecutiveSync(ReadOnlySpan<byte> track, ushort sync, int offset)
        {
            if (track.Length < 4)
            {
                return offset;
            }

            var current = offset;
            while (true)
            {
                var previous = (current - 2 + track.Length) % track.Length;
                if (previous == offset || BigEndian.ReadUInt16(track, previous, "encoded disk sync word") != sync)
                {
                    return current;
                }

                current = previous;
            }
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

        public static byte[] DecodeBestEffort(byte[][] encodedTracks, out bool hasCompleteSectorData)
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
                if (track == null || track.Length < EncodedSectorBytesAfterSync)
                {
                    continue;
                }

                DecodeTrack(track, trackNumber, data, decodedSectors, ref decodedSectorCount);
            }

            hasCompleteSectorData = decodedSectorCount == decodedSectors.Length;
            return data;
        }

        private static void DecodeTrack(
            ReadOnlySpan<byte> track,
            int expectedTrackNumber,
            byte[] diskData,
            bool[] decodedSectors,
            ref int decodedSectorCount)
        {
            var length = track.Length & ~1;
            if (length < EncodedSectorBytesAfterSync)
            {
                return;
            }

            for (var offset = 0; offset < length; offset += 2)
            {
                if (ReadUInt16Circular(track, length, offset) != SyncWord ||
                    ReadUInt16Circular(track, length, offset + 2) != SyncWord)
                {
                    continue;
                }

                TryDecodeSector(track, length, offset, expectedTrackNumber, diskData, decodedSectors, ref decodedSectorCount);
            }
        }

        private static void TryDecodeSector(
            ReadOnlySpan<byte> track,
            int length,
            int syncOffset,
            int expectedTrackNumber,
            byte[] diskData,
            bool[] decodedSectors,
            ref int decodedSectorCount)
        {
            var header = DecodeOddEven(ReadMfmLong(track, length, syncOffset + 0x04), ReadMfmLong(track, length, syncOffset + 0x08));
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
                ReadMfmLong(track, length, syncOffset + 0x2C),
                ReadMfmLong(track, length, syncOffset + 0x30));
            var calculatedHeaderChecksum = ComputeMfmChecksum(track, length, syncOffset + 0x04, 10);
            if ((decodedHeaderChecksum & MfmDataMask) != calculatedHeaderChecksum)
            {
                return;
            }

            var decodedDataChecksum = DecodeOddEven(
                ReadMfmLong(track, length, syncOffset + 0x34),
                ReadMfmLong(track, length, syncOffset + 0x38));
            var calculatedDataChecksum = ComputeMfmChecksum(track, length, syncOffset + 0x3C, 256);
            if ((decodedDataChecksum & MfmDataMask) != calculatedDataChecksum)
            {
                return;
            }

            var logicalSector = (trackNumber * AmigaDiskImage.SectorsPerTrack) + sector;
            var destinationOffset = logicalSector * AmigaDiskImage.SectorSize;
            for (var longIndex = 0; longIndex < AmigaDiskImage.SectorSize / 4; longIndex++)
            {
                var odd = ReadMfmLong(track, length, syncOffset + 0x3C + (longIndex * 4));
                var even = ReadMfmLong(track, length, syncOffset + 0x3C + ((AmigaDiskImage.SectorSize / 4) + longIndex) * 4);
                BigEndian.WriteUInt32(diskData, destinationOffset + (longIndex * 4), DecodeOddEven(odd, even));
            }

            if (!decodedSectors[logicalSector])
            {
                decodedSectors[logicalSector] = true;
                decodedSectorCount++;
            }
        }

        private static uint ComputeMfmChecksum(ReadOnlySpan<byte> track, int length, int offset, int longCount)
        {
            var checksum = 0u;
            for (var index = 0; index < longCount; index++)
            {
                checksum ^= ReadMfmLong(track, length, offset + (index * 4));
            }

            return checksum & MfmDataMask;
        }

        private static uint ReadMfmLong(ReadOnlySpan<byte> track, int length, int offset)
        {
            return ReadUInt32Circular(track, length, offset) & MfmDataMask;
        }

        private static uint DecodeOddEven(uint odd, uint even)
        {
            return ((odd & MfmDataMask) << 1) | (even & MfmDataMask);
        }

        private static ushort ReadUInt16Circular(ReadOnlySpan<byte> data, int length, int offset)
        {
            return (ushort)((ReadByteCircular(data, length, offset) << 8) | ReadByteCircular(data, length, offset + 1));
        }

        private static uint ReadUInt32Circular(ReadOnlySpan<byte> data, int length, int offset)
        {
            return ((uint)ReadByteCircular(data, length, offset) << 24) |
                ((uint)ReadByteCircular(data, length, offset + 1) << 16) |
                ((uint)ReadByteCircular(data, length, offset + 2) << 8) |
                ReadByteCircular(data, length, offset + 3);
        }

        private static byte ReadByteCircular(ReadOnlySpan<byte> data, int length, int offset)
        {
            offset %= length;
            if (offset < 0)
            {
                offset += length;
            }

            return data[offset];
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
