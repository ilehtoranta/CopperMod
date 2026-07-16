/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaBus
    {
        private bool TryGetContiguousWritableSpan(uint address, int byteCount, out Span<byte> span)
        {
            if (_chipRam.TryGetContiguousMemory(address, byteCount, out var memory, out var offset))
            {
                span = memory.AsSpan(offset, byteCount);
                return true;
            }

            if (_expansionRam.TryGetContiguousMemory(address, byteCount, out memory, out offset))
            {
                span = memory.AsSpan(offset, byteCount);
                return true;
            }

            if (_realFastRam.TryGetContiguousMemory(address, byteCount, out memory, out offset))
            {
                span = memory.AsSpan(offset, byteCount);
                return true;
            }

            span = default;
            return false;
        }

        private bool TryGetContiguousReadableSpan(uint address, int byteCount, out ReadOnlySpan<byte> span)
        {
            if (_chipRam.TryGetContiguousMemory(address, byteCount, out var memory, out var offset))
            {
                span = memory.AsSpan(offset, byteCount);
                return true;
            }

            if (_expansionRam.TryGetContiguousMemory(address, byteCount, out memory, out offset))
            {
                span = memory.AsSpan(offset, byteCount);
                return true;
            }

            if (_realFastRam.TryGetContiguousMemory(address, byteCount, out memory, out offset))
            {
                span = memory.AsSpan(offset, byteCount);
                return true;
            }

            span = default;
            return false;
        }

        private bool IsChipRamRange(uint address, int byteCount)
        {
            if (byteCount < 0)
            {
                return false;
            }

            if (byteCount == 0)
            {
                return true;
            }

            return _chipRam.IsRange(address, byteCount);
        }

        private bool IsContiguousChipRamRange(uint address, int byteCount)
        {
            if (!IsChipRamRange(address, byteCount))
            {
                return false;
            }

            return _chipRam.IsContiguousRange(address, byteCount);
        }

        internal bool IsChipRamAddress(uint address)
        {
            return TryGetChipRamOffset(address, out _);
        }

        private bool TryGetChipRamOffset(uint address, out int offset)
        {
            return _chipRam.TryGetOffset(address, out offset);
        }

        private int GetChipRamOffset(uint address)
        {
            return _chipRam.GetOffset(address);
        }

        private bool IsExpansionRamRange(uint address, int byteCount)
        {
            return _expansionRam.IsRange(address, byteCount);
        }

        private bool IsExpansionRamAddress(uint address)
        {
            return TryGetExpansionRamOffset(address, out _);
        }

        private bool TryGetExpansionRamOffset(uint address, out int offset)
        {
            return _expansionRam.TryGetOffset(address, out offset);
        }

        private bool IsRealFastRamRange(uint address, int byteCount)
        {
            return _realFastRam.IsRange(address, byteCount);
        }

        private bool IsRealFastRamAddress(uint address)
        {
            return TryGetRealFastRamOffset(address, out _);
        }

        private bool TryGetRealFastRamOffset(uint address, out int offset)
        {
            return _realFastRam.TryGetOffset(address, out offset);
        }

        private bool IsRomOverlayAddress(uint address)
        {
            return _mappedMemory.IsRomOverlayAddress(address, _romOverlayEnabled);
        }

        private bool TryReadRomOverlayByte(uint address, out byte value)
        {
            return _mappedMemory.TryReadRomOverlayByte(address, _romOverlayEnabled, out value);
        }

        private bool TryReadRomOverlayWord(uint address, out ushort value)
        {
            return _mappedMemory.TryReadRomOverlayWord(address, _romOverlayEnabled, out value);
        }
    }
}
