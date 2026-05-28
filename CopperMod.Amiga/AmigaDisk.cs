using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CopperMod.Amiga
{
    internal sealed class AmigaDiskImage
    {
        public const int SectorSize = 512;
        public const int SectorsPerTrack = 11;
        public const int HeadCount = 2;
        public const int CylinderCount = 80;
        public const int StandardAdfSize = CylinderCount * HeadCount * SectorsPerTrack * SectorSize;

        private AmigaDiskImage(byte[] data, string name)
        {
            if (data.Length != StandardAdfSize)
            {
                throw new AmigaEmulationException($"Only standard {StandardAdfSize}-byte ADF images are supported.");
            }

            Data = data;
            Name = name;
        }

        public byte[] Data { get; }

        public string Name { get; }

        public ReadOnlySpan<byte> BootBlock => Data.AsSpan(0, 1024);

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

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(path);
                var entries = archive.Entries
                    .Where(entry => !string.IsNullOrEmpty(entry.Name) && entry.Name.EndsWith(".adf", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (entries.Length != 1)
                {
                    throw new AmigaEmulationException("A zipped disk image must contain exactly one ADF file.");
                }

                using var stream = entries[0].Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return FromAdfBytes(memory.ToArray(), entries[0].Name);
            }

            throw new AmigaEmulationException("Unsupported disk image extension. Expected .adf or .zip.");
        }

        public static AmigaDiskImage FromAdfBytes(byte[] data, string name = "disk.adf")
        {
            return new AmigaDiskImage(data ?? throw new ArgumentNullException(nameof(data)), name);
        }

        public ReadOnlySpan<byte> ReadSector(int cylinder, int head, int sector)
        {
            if (cylinder < 0 || cylinder >= CylinderCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cylinder));
            }

            if (head < 0 || head >= HeadCount)
            {
                throw new ArgumentOutOfRangeException(nameof(head));
            }

            if (sector < 0 || sector >= SectorsPerTrack)
            {
                throw new ArgumentOutOfRangeException(nameof(sector));
            }

            var lba = ((cylinder * HeadCount) + head) * SectorsPerTrack + sector;
            return ReadSector(lba);
        }

        public ReadOnlySpan<byte> ReadSector(int logicalSector)
        {
            var offset = checked(logicalSector * SectorSize);
            if (logicalSector < 0 || offset + SectorSize > Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalSector));
            }

            return Data.AsSpan(offset, SectorSize);
        }

        public ReadOnlySpan<byte> ReadBytes(int byteOffset, int byteCount)
        {
            if (byteOffset < 0 || byteCount < 0 || byteOffset + byteCount > Data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(byteOffset), "Requested disk byte range is outside the ADF image.");
            }

            return Data.AsSpan(byteOffset, byteCount);
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

        private AmigaDiskImage RequireDisk()
        {
            return Disk ?? throw new AmigaEmulationException("No disk is inserted in DF0:.");
        }
    }

    internal sealed class AmigaDiskController
    {
        private const ushort DskBytr = 0x01A;
        private const ushort DskPth = 0x020;
        private const ushort DskPtl = 0x022;
        private const ushort DskLen = 0x024;
        private const ushort DskSync = 0x07E;
        private const ushort DskBlkInterrupt = 0x0002;
        private const ushort DskDmaEnable = 0x8000;
        private const ushort DskWriteMode = 0x4000;
        private const ushort DskLengthMask = 0x3FFF;
        private const int DiskDmaCompletionBaseDelayCycles = 512;
        private const int DiskDmaCompletionCyclesPerWord = 2;

        private readonly AmigaBus _bus;
        private ushort _dskbytr;
        private ushort _dsklen;
        private ushort _dsksync = 0x4489;
        private uint _diskPointer;
        private byte _ciabPortB = 0xFF;
        private int _transferCount;
        private int _lastTransferWords;
        private int _lastTransferCylinder;
        private int _lastTransferHead;
        private uint _lastTransferAddress;
        private ushort? _armedDsklen;
        private int _streamCylinder;
        private int _streamHead;
        private int _streamOffset;

        public AmigaDiskController(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Drive0 = new AmigaFloppyDrive();
            Reset();
        }

        public AmigaFloppyDrive Drive0 { get; }

        public int TransferCount => _transferCount;

        public AmigaDiskControllerSnapshot CaptureSnapshot()
        {
            return new AmigaDiskControllerSnapshot(
                _diskPointer,
                _dsklen,
                _dsksync,
                _dskbytr,
                Drive0.Cylinder,
                Drive0.Head,
                Drive0.MotorOn,
                Drive0.Selected,
                _ciabPortB,
                _transferCount,
                _lastTransferWords,
                _lastTransferCylinder,
                _lastTransferHead,
                _lastTransferAddress);
        }

        public void Reset()
        {
            _dskbytr = 0;
            _dsklen = 0;
            _dsksync = 0x4489;
            _diskPointer = 0;
            _ciabPortB = 0xFF;
            _transferCount = 0;
            _lastTransferWords = 0;
            _lastTransferCylinder = 0;
            _lastTransferHead = 0;
            _lastTransferAddress = 0;
            _armedDsklen = null;
            _streamCylinder = 0;
            _streamHead = 0;
            _streamOffset = 0;
            Drive0.ResetPosition();
        }

        public byte ReadCiaAPortA(byte latchedPortA)
        {
            var value = (byte)(latchedPortA | 0xFC);
            value = SetActiveLow(value, 2, Drive0.DiskChanged || Drive0.Disk == null);
            value = SetActiveLow(value, 3, false);
            value = SetActiveLow(value, 4, Drive0.Disk != null && Drive0.Cylinder == 0);
            value = SetActiveLow(value, 5, Drive0.Disk != null && Drive0.Selected && Drive0.MotorOn);
            return value;
        }

        public byte ReadByte(ushort offset)
        {
            var value = ReadWord((ushort)(offset & 0x01FE));
            return (offset & 1) == 0 ? (byte)(value >> 8) : (byte)value;
        }

        public ushort ReadWord(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset switch
            {
                DskBytr => _dskbytr,
                DskPth => (ushort)(_diskPointer >> 16),
                DskPtl => (ushort)_diskPointer,
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
                    _diskPointer = (_diskPointer & 0x0000_FFFF) | ((uint)value << 16);
                    break;
                case DskPtl:
                    _diskPointer = (_diskPointer & 0xFFFF_0000) | value;
                    break;
                case DskLen:
                    _dsklen = value;
                    if ((value & DskDmaEnable) == 0 || (value & DskWriteMode) != 0 || (value & DskLengthMask) == 0)
                    {
                        _armedDsklen = null;
                        break;
                    }

                    if (_armedDsklen == value)
                    {
                        _armedDsklen = null;
                        StartReadDma((ushort)(value & DskLengthMask), cycle);
                    }
                    else
                    {
                        _armedDsklen = value;
                    }

                    break;
                case DskSync:
                    _dsksync = value;
                    break;
            }
        }

        public void WriteCiaBRegister(int register, byte value)
        {
            if ((register & 0x0F) != 1)
            {
                return;
            }

            var previous = _ciabPortB;
            _ciabPortB = value;
            Drive0.SetSelected((value & 0x78) != 0x78);
            Drive0.SetMotorOn((value & 0x80) == 0);
            Drive0.SetHead((value & 0x04) == 0 ? 1 : 0);

            var previousStepHigh = (previous & 0x01) != 0;
            var stepHigh = (value & 0x01) != 0;
            if (Drive0.Selected && previousStepHigh && !stepHigh)
            {
                var inward = (value & 0x02) == 0;
                Drive0.Step(inward ? 1 : -1);
            }
        }

        private static byte SetActiveLow(byte value, int bit, bool asserted)
        {
            var mask = (byte)(1 << bit);
            return asserted ? (byte)(value & ~mask) : (byte)(value | mask);
        }

        private void StartReadDma(ushort requestedWords, long cycle)
        {
            if (requestedWords == 0 || Drive0.Disk == null)
            {
                return;
            }

            var track = AmigaDosTrackEncoder.EncodeTrack(Drive0.Disk, Drive0.Cylinder, Drive0.Head);
            ResetStreamIfTrackChanged();
            var sourceStart = _streamOffset;
            if ((_bus.Paula.Adkcon & 0x0400) != 0)
            {
                sourceStart = (FindSyncOffset(track, _dsksync, _streamOffset) + 2) % track.Length;
            }

            var targetAddress = _diskPointer & 0x00FF_FFFE;
            for (var word = 0; word < requestedWords; word++)
            {
                var sourceOffset = (sourceStart + (word * 2)) % track.Length;
                var value = BigEndian.ReadUInt16(track, sourceOffset, "encoded disk DMA word");
                _bus.WriteChipWordForDevice(
                    AmigaBusRequester.Disk,
                    AmigaBusAccessKind.DiskDma,
                    targetAddress + (uint)(word * 2),
                    value,
                    cycle);
            }

            _transferCount++;
            _lastTransferWords = requestedWords;
            _lastTransferCylinder = Drive0.Cylinder;
            _lastTransferHead = Drive0.Head;
            _lastTransferAddress = targetAddress;
            _streamOffset = (sourceStart + (requestedWords * 2)) % track.Length;
            _dskbytr = 0x9000;
            var completionCycle = cycle + Math.Max(
                DiskDmaCompletionBaseDelayCycles,
                requestedWords * DiskDmaCompletionCyclesPerWord);
            _bus.WriteDeviceWord(
                AmigaBusRequester.Disk,
                AmigaBusAccessKind.DiskDma,
                0x00DFF09C,
                (ushort)(0x8000 | DskBlkInterrupt),
                completionCycle);
        }

        private void ResetStreamIfTrackChanged()
        {
            if (_streamCylinder == Drive0.Cylinder && _streamHead == Drive0.Head)
            {
                return;
            }

            _streamCylinder = Drive0.Cylinder;
            _streamHead = Drive0.Head;
            _streamOffset = 0;
        }

        private static int FindSyncOffset(ReadOnlySpan<byte> track, ushort sync, int startOffset)
        {
            startOffset = Math.Clamp(startOffset & ~1, 0, Math.Max(0, track.Length - 2));
            for (var pass = 0; pass < 2; pass++)
            {
                var offset = pass == 0 ? startOffset : 0;
                var end = pass == 0 ? track.Length - 1 : startOffset;
                for (; offset + 1 < end; offset += 2)
                {
                    if (BigEndian.ReadUInt16(track, offset, "encoded disk sync word") == sync)
                    {
                        return offset;
                    }
                }
            }

            return 0;
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
            int lastTransferCylinder,
            int lastTransferHead,
            uint lastTransferAddress)
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
            LastTransferCylinder = lastTransferCylinder;
            LastTransferHead = lastTransferHead;
            LastTransferAddress = lastTransferAddress;
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

        public int LastTransferCylinder { get; }

        public int LastTransferHead { get; }

        public uint LastTransferAddress { get; }
    }

    internal static class AmigaDosTrackEncoder
    {
        private const int EncodedSectorBytes = 0x440;
        private const int EncodedTrackGapBytes = 0x140;
        private const int EncodedTrackBytes = (EncodedSectorBytes * AmigaDiskImage.SectorsPerTrack) + EncodedTrackGapBytes;
        private const uint MfmDataMask = 0x5555_5555;

        public static byte[] EncodeTrack(AmigaDiskImage disk, int cylinder, int head)
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
            for (var sector = 0; sector < AmigaDiskImage.SectorsPerTrack; sector++)
            {
                EncodeSector(disk.ReadSector(cylinder, head, sector), track.AsSpan(sector * EncodedSectorBytes, EncodedSectorBytes), trackNumber, sector);
            }

            return track;
        }

        private static void EncodeSector(ReadOnlySpan<byte> source, Span<byte> destination, int trackNumber, int sector)
        {
            destination.Fill(0xAA);
            BigEndian.WriteUInt16(destination, 0x04, 0x4489);
            BigEndian.WriteUInt16(destination, 0x06, 0x4489);

            var sectorsUntilGap = AmigaDiskImage.SectorsPerTrack - sector;
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
