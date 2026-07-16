/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Memory
{
    internal enum AmigaMemoryBackendKind
    {
        ChipRam,
        ExpansionRam,
        RealFastRam,
        Rom
    }

    internal readonly struct AmigaExactCpuDataRamRegion
    {
        public AmigaExactCpuDataRamRegion(
            AmigaMemoryBackendKind kind,
            AmigaBusAccessTarget target,
            uint address,
            byte[] memory,
            int offset)
        {
            Kind = kind;
            Target = target;
            Address = address;
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            Offset = offset;
        }

        public AmigaMemoryBackendKind Kind { get; }

        public AmigaBusAccessTarget Target { get; }

        public uint Address { get; }

        public byte[] Memory { get; }

        public int Offset { get; }
    }

    internal sealed class AmigaChipRamBackend
    {
        private readonly byte[] _data;
        private readonly uint[] _codePageGenerations;
        private readonly int _codeGenerationPageShift;
        private readonly ChipPresentationWriteHistory _presentationWriteHistory;

        public AmigaChipRamBackend(
            int size,
            uint decodeSize,
            uint dmaAddressMask,
            int codeGenerationPageShift)
        {
            _data = new byte[size];
            DecodeSize = decodeSize;
            DmaAddressMask = dmaAddressMask;
            _codeGenerationPageShift = codeGenerationPageShift;
            _codePageGenerations = new uint[Math.Max(1, (size + (1 << codeGenerationPageShift) - 1) >> codeGenerationPageShift)];
            _presentationWriteHistory = new ChipPresentationWriteHistory(size);
        }

        public byte[] Data => _data;

        public int Length => _data.Length;

        public uint DecodeSize { get; }

        public uint DmaAddressMask { get; }

        public ChipPresentationWriteHistory PresentationWriteHistory => _presentationWriteHistory;

        public byte this[int index]
        {
            get => _data[index];
            set => _data[index] = value;
        }

        public void ClearData()
        {
            Array.Clear(_data);
        }

        public bool IsRange(uint address, int byteCount)
        {
            if (byteCount < 0)
            {
                return false;
            }

            if (byteCount == 0)
            {
                return true;
            }

            return address < DecodeSize &&
                (ulong)address + (ulong)byteCount <= (ulong)DecodeSize;
        }

        public bool IsContiguousRange(uint address, int byteCount)
        {
            if (!IsRange(address, byteCount))
            {
                return false;
            }

            var offset = GetOffset(address);
            return (ulong)offset + (ulong)byteCount <= (ulong)_data.Length;
        }

        public bool TryGetOffset(uint address, out int offset)
        {
            if (_data.Length > 0 && address < DecodeSize)
            {
                offset = (int)(address & ((uint)_data.Length - 1u));
                return true;
            }

            offset = 0;
            return false;
        }

        public int GetOffset(uint address)
        {
            if (!TryGetOffset(address, out var offset))
            {
                throw new ArgumentOutOfRangeException(nameof(address), address, "Address is outside the chip RAM decode window.");
            }

            return offset;
        }

        public bool TryGetContiguousMemory(uint address, int byteCount, out byte[] memory, out int offset)
        {
            if (!IsContiguousRange(address, byteCount))
            {
                memory = Array.Empty<byte>();
                offset = 0;
                return false;
            }

            memory = _data;
            offset = GetOffset(address);
            return true;
        }

        public ushort ReadDmaWord(uint address)
        {
            var offset = (int)(address & DmaAddressMask);
            var nextOffset = (offset + 1) & (_data.Length - 1);
            return (ushort)((_data[offset] << 8) | _data[nextOffset]);
        }

        public void WriteDmaWord(uint address, ushort value, long grantedCycle)
        {
            var offset = (int)(address & DmaAddressMask);
            var nextOffset = (offset + 1) & (_data.Length - 1);
            WriteByteAtOffset(offset, (byte)(value >> 8), grantedCycle);
            WriteByteAtOffset(nextOffset, (byte)value, grantedCycle);
        }

        public ushort ReadWordAtOffset(int offset)
        {
            var nextOffset = (offset + 1) & (_data.Length - 1);
            return (ushort)((_data[offset] << 8) | _data[nextOffset]);
        }

        public ushort ReadWordForPresentation(uint address, long cycle)
        {
            address &= DmaAddressMask;
            if (!_presentationWriteHistory.HasWrites ||
                !_presentationWriteHistory.MayNeedPresentationRead(cycle))
            {
                return ReadDmaWord(address);
            }

            var offset = (int)(address & (uint)(_data.Length - 1));
            var nextOffset = (offset + 1) & (_data.Length - 1);
            if (!_presentationWriteHistory.NeedsPresentationRead(offset, cycle) &&
                !_presentationWriteHistory.NeedsPresentationRead(nextOffset, cycle))
            {
                return ReadDmaWord(address);
            }

            var high = _presentationWriteHistory.ReadByte(_data, offset, cycle);
            var low = _presentationWriteHistory.ReadByte(_data, nextOffset, cycle);
            return (ushort)((high << 8) | low);
        }

        public void WriteByteAtOffset(int offset, byte value, long grantedCycle)
        {
            _presentationWriteHistory.RecordByte(offset, _data[offset], value, grantedCycle);
            _data[offset] = value;
        }

        public void WriteWordAtOffset(int offset, ushort value, long grantedCycle)
        {
            var nextOffset = (offset + 1) & (_data.Length - 1);
            WriteByteAtOffset(offset, (byte)(value >> 8), grantedCycle);
            WriteByteAtOffset(nextOffset, (byte)value, grantedCycle);
        }

        public void WriteContiguousWordAtOffset(int offset, ushort value, long grantedCycle)
        {
            WriteByteAtOffset(offset, (byte)(value >> 8), grantedCycle);
            WriteByteAtOffset(offset + 1, (byte)value, grantedCycle);
        }

        public void WriteContiguousLongAtOffset(int offset, uint value, long firstWordCycle, long secondWordCycle)
        {
            WriteContiguousWordAtOffset(offset, (ushort)(value >> 16), firstWordCycle);
            WriteContiguousWordAtOffset(offset + 2, (ushort)value, secondWordCycle);
        }

        public uint GetCodePageGeneration(int offset)
        {
            return _codePageGenerations[offset >> _codeGenerationPageShift];
        }

        public void SetCodePageGeneration(int offset, uint generation)
        {
            _codePageGenerations[offset >> _codeGenerationPageShift] = generation;
        }

        public void ClearCodePageGenerations()
        {
            Array.Clear(_codePageGenerations);
        }
    }

    internal sealed class AmigaLinearRamBackend
    {
        private readonly byte[] _data;
        private readonly uint[] _codePageGenerations;
        private readonly int _codeGenerationPageShift;

        public AmigaLinearRamBackend(
            int size,
            uint baseAddress,
            int codeGenerationPageShift,
            bool initiallyMapped = true)
        {
            try
            {
                _data = new byte[size];
                _codePageGenerations = new uint[Math.Max(1, (size + (1 << codeGenerationPageShift) - 1) >> codeGenerationPageShift)];
            }
            catch (OutOfMemoryException ex)
            {
                throw new AmigaEmulationException(
                    $"Could not allocate {size / (1024 * 1024)} MiB of Autoconfig fast RAM.",
                    ex);
            }

            BaseAddress = baseAddress;
            IsMapped = initiallyMapped;
            _codeGenerationPageShift = codeGenerationPageShift;
        }

        public byte[] Data => _data;

        public int Length => _data.Length;

        public uint BaseAddress { get; private set; }

        public bool IsMapped { get; private set; }

        public byte this[int index]
        {
            get => _data[index];
            set => _data[index] = value;
        }

        public void ClearData()
        {
            Array.Clear(_data);
        }

        public void Map(uint baseAddress)
        {
            BaseAddress = baseAddress;
            IsMapped = true;
        }

        public void Unmap()
        {
            BaseAddress = 0;
            IsMapped = false;
        }

        public bool IsRange(uint address, int byteCount)
        {
            if (!IsMapped || _data.Length == 0 || byteCount < 0 || address < BaseAddress)
            {
                return false;
            }

            var offset = address - BaseAddress;
            return offset < _data.Length && (ulong)offset + (ulong)byteCount <= (ulong)_data.Length;
        }

        public bool TryGetOffset(uint address, out int offset)
        {
            if (IsMapped && _data.Length != 0 && address >= BaseAddress)
            {
                var candidate = address - BaseAddress;
                if (candidate < _data.Length)
                {
                    offset = (int)candidate;
                    return true;
                }
            }

            offset = 0;
            return false;
        }

        public bool TryGetContiguousMemory(uint address, int byteCount, out byte[] memory, out int offset)
        {
            if (!IsRange(address, byteCount))
            {
                memory = Array.Empty<byte>();
                offset = 0;
                return false;
            }

            memory = _data;
            offset = checked((int)(address - BaseAddress));
            return true;
        }

        public bool TryReadValue(uint address, M68kOperandSize size, out uint value)
        {
            var byteCount = GetByteCount(size);
            if (!TryGetContiguousMemory(address, byteCount, out _, out var index))
            {
                value = 0;
                return false;
            }

            value = size switch
            {
                M68kOperandSize.Byte => _data[index],
                M68kOperandSize.Word => (uint)((_data[index] << 8) | _data[index + 1]),
                _ => ((uint)_data[index] << 24) |
                    ((uint)_data[index + 1] << 16) |
                    ((uint)_data[index + 2] << 8) |
                    _data[index + 3]
            };
            return true;
        }

        public bool TryWriteValue(uint address, uint value, M68kOperandSize size)
        {
            var byteCount = GetByteCount(size);
            if (!TryGetContiguousMemory(address, byteCount, out _, out var index))
            {
                return false;
            }

            if (size == M68kOperandSize.Byte)
            {
                _data[index] = (byte)value;
            }
            else if (size == M68kOperandSize.Word)
            {
                _data[index] = (byte)(value >> 8);
                _data[index + 1] = (byte)value;
            }
            else
            {
                _data[index] = (byte)(value >> 24);
                _data[index + 1] = (byte)(value >> 16);
                _data[index + 2] = (byte)(value >> 8);
                _data[index + 3] = (byte)value;
            }

            return true;
        }

        public bool TryReadWordAtOffset(int offset, out ushort value)
        {
            if ((uint)(offset + 1) >= (uint)_data.Length)
            {
                value = 0;
                return false;
            }

            value = (ushort)((_data[offset] << 8) | _data[offset + 1]);
            return true;
        }

        public bool TryWriteWordAtOffset(int offset, ushort value)
        {
            if ((uint)(offset + 1) >= (uint)_data.Length)
            {
                return false;
            }

            _data[offset] = (byte)(value >> 8);
            _data[offset + 1] = (byte)value;
            return true;
        }

        public uint GetCodePageGeneration(int offset)
        {
            return _codePageGenerations[offset >> _codeGenerationPageShift];
        }

        public void SetCodePageGeneration(int offset, uint generation)
        {
            _codePageGenerations[offset >> _codeGenerationPageShift] = generation;
        }

        public void ClearCodePageGenerations()
        {
            Array.Clear(_codePageGenerations);
        }

        private static int GetByteCount(M68kOperandSize size)
            => size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
    }

    internal sealed class AmigaMappedMemoryBackend
    {
        private readonly List<MappedMemoryRegion> _regions = new List<MappedMemoryRegion>();
        private MappedMemoryRegion? _romOverlayRegion;

        public void Clear()
        {
            _regions.Clear();
            _romOverlayRegion = null;
        }

        public void MapMemory(uint baseAddress, ReadOnlySpan<byte> data, bool readOnly)
        {
            if (data.IsEmpty)
            {
                throw new ArgumentException("Mapped memory cannot be empty.", nameof(data));
            }

            var copy = data.ToArray();
            var region = new MappedMemoryRegion(baseAddress, copy, readOnly);
            _regions.Add(region);
            if (readOnly && baseAddress + (uint)copy.Length == 0x0100_0000)
            {
                _romOverlayRegion = region;
            }
        }

        public bool IsRomOverlayAddress(uint address, bool overlayEnabled)
        {
            return overlayEnabled &&
                _romOverlayRegion != null &&
                address < 0x0008_0000;
        }

        public bool TryReadRomOverlayByte(uint address, bool overlayEnabled, out byte value)
        {
            if (!IsRomOverlayAddress(address, overlayEnabled))
            {
                value = 0;
                return false;
            }

            var romAddress = _romOverlayRegion!.BaseAddress + (address % (uint)_romOverlayRegion.Length);
            return _romOverlayRegion.TryReadByte(romAddress, out value);
        }

        public bool TryReadRomOverlayWord(uint address, bool overlayEnabled, out ushort value)
        {
            if (!overlayEnabled ||
                _romOverlayRegion == null ||
                address >= 0x0008_0000 ||
                address + 1 >= 0x0008_0000)
            {
                value = 0;
                return false;
            }

            var offset = checked((int)(address % (uint)_romOverlayRegion.Length));
            if (offset + 1 >= _romOverlayRegion.Length)
            {
                value = 0;
                return false;
            }

            var data = _romOverlayRegion.Data;
            value = (ushort)((data[offset] << 8) | data[offset + 1]);
            return true;
        }

        public bool TryGetRomOverlayReadMemory(
            uint address,
            int byteCount,
            bool overlayEnabled,
            out byte[] memory,
            out int offset)
        {
            memory = Array.Empty<byte>();
            offset = 0;
            if (!IsRomOverlayAddress(address, overlayEnabled))
            {
                return false;
            }

            var lastAddress = address + (uint)(byteCount - 1);
            if (!IsRomOverlayAddress(lastAddress, overlayEnabled))
            {
                return false;
            }

            var overlayRegion = _romOverlayRegion!;
            var overlayOffset = checked((int)(address % (uint)overlayRegion.Length));
            if (overlayOffset + byteCount > overlayRegion.Length)
            {
                return false;
            }

            memory = overlayRegion.Data;
            offset = overlayOffset;
            return true;
        }

        public bool TryGetRomOverlayInstructionFetchMemory(
            uint address,
            uint endAddress,
            bool overlayEnabled,
            out byte[] memory,
            out int offset,
            out uint windowEnd)
        {
            memory = Array.Empty<byte>();
            offset = 0;
            windowEnd = 0;
            if (!overlayEnabled ||
                _romOverlayRegion == null ||
                address >= 0x0008_0000u)
            {
                return false;
            }

            endAddress = Math.Min(endAddress, 0x0008_0000u);
            memory = _romOverlayRegion.Data;
            offset = checked((int)(address % (uint)memory.Length));
            var contiguousEnd = address + (uint)(memory.Length - offset);
            windowEnd = Math.Min(endAddress, contiguousEnd);
            return true;
        }

        public bool ContainsMappedAddress(uint address)
        {
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                if (_regions[i].Contains(address))
                {
                    return true;
                }
            }

            return false;
        }

        public bool ContainsMappedAddressInRange(uint address, int byteCount)
        {
            if (byteCount <= 0)
            {
                return false;
            }

            var start = (ulong)address;
            var end = start + (uint)byteCount;
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                if (_regions[i].Overlaps(start, end))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryReadMappedByte(uint address, out byte value)
        {
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                if (_regions[i].TryReadByte(address, out value))
                {
                    return true;
                }
            }

            value = 0;
            return false;
        }

        public bool TryWriteMappedByte(uint address, byte value)
        {
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                if (_regions[i].TryWriteByte(address, value))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetMappedReadMemory(uint address, int byteCount, out byte[] memory, out int offset)
        {
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                if (_regions[i].TryGetContiguousReadMemory(address, byteCount, out memory, out offset))
                {
                    return true;
                }
            }

            memory = Array.Empty<byte>();
            offset = 0;
            return false;
        }

        public bool TryGetMappedRomInstructionFetchMemory(
            uint address,
            uint endAddress,
            out byte[] memory,
            out int offset,
            out uint windowEnd)
        {
            for (var i = _regions.Count - 1; i >= 0; i--)
            {
                var region = _regions[i];
                if (!region.ReadOnly ||
                    !region.TryGetContiguousReadMemory(address, 2, out memory, out offset))
                {
                    continue;
                }

                var contiguousEnd = address + (uint)(region.Length - offset);
                windowEnd = Math.Min(endAddress, contiguousEnd);
                return true;
            }

            memory = Array.Empty<byte>();
            offset = 0;
            windowEnd = 0;
            return false;
        }

        private sealed class MappedMemoryRegion
        {
            private readonly byte[] _data;

            public MappedMemoryRegion(uint baseAddress, byte[] data, bool readOnly)
            {
                BaseAddress = baseAddress;
                _data = data ?? throw new ArgumentNullException(nameof(data));
                ReadOnly = readOnly;
            }

            public uint BaseAddress { get; }

            public int Length => _data.Length;

            public bool ReadOnly { get; }

            internal byte[] Data => _data;

            public bool Contains(uint address)
            {
                var offset = address - BaseAddress;
                return address >= BaseAddress && offset < _data.Length;
            }

            public bool Overlaps(ulong start, ulong end)
            {
                var regionStart = (ulong)BaseAddress;
                var regionEnd = regionStart + (uint)_data.Length;
                return start < regionEnd && regionStart < end;
            }

            public bool TryGetContiguousReadMemory(uint address, int byteCount, out byte[] memory, out int offset)
            {
                if (address < BaseAddress)
                {
                    memory = Array.Empty<byte>();
                    offset = 0;
                    return false;
                }

                var relative = address - BaseAddress;
                if (relative >= _data.Length ||
                    relative + byteCount > _data.Length)
                {
                    memory = Array.Empty<byte>();
                    offset = 0;
                    return false;
                }

                memory = _data;
                offset = checked((int)relative);
                return true;
            }

            public bool TryReadByte(uint address, out byte value)
            {
                var offset = address - BaseAddress;
                if (address < BaseAddress || offset >= _data.Length)
                {
                    value = 0;
                    return false;
                }

                value = _data[offset];
                return true;
            }

            public bool TryWriteByte(uint address, byte value)
            {
                var offset = address - BaseAddress;
                if (ReadOnly || address < BaseAddress || offset >= _data.Length)
                {
                    return false;
                }

                _data[offset] = value;
                return true;
            }
        }
    }
}
