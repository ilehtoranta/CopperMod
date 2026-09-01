/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga.CustomChips.Denise
{
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct RowDmaBitplaneEntry
    {
        public RowDmaBitplaneEntry(
            int cycleOffset,
            int plane,
            int word,
            int slot,
            uint address,
            bool rowPresent)
            : this(cycleOffset, plane, word, slot, address, rowPresent, delayedOutput: false)
        {
        }

        public RowDmaBitplaneEntry(
            int cycleOffset,
            int plane,
            int word,
            int slot,
            uint address,
            bool rowPresent,
            bool delayedOutput)
        {
            _address = address;
            _cycleOffset = checked((ushort)cycleOffset);
            _plane = checked((byte)plane);
            _word = checked((byte)word);
            _slot = checked((byte)slot);
            _flags = (byte)((rowPresent ? 1 : 0) | (delayedOutput ? 2 : 0));
        }

        private readonly uint _address;

        private readonly ushort _cycleOffset;

        private readonly byte _plane;

        private readonly byte _word;

        private readonly byte _slot;

        private readonly byte _flags;

        public int CycleOffset => _cycleOffset;

        public int Plane => _plane;

        public int Word => _word;

        public int Slot => _slot;

        public uint Address => _address;

        public bool RowPresent => (_flags & 1) != 0;

        public bool DelayedOutput => (_flags & 2) != 0;

        // The row plan's geometry is the address-input phase, not RAM OUT.
        public long GetCycle(long lineStartCycle)
            => lineStartCycle + _cycleOffset;

        public long GetOutputCycle(long lineStartCycle)
            => GetCycle(lineStartCycle) + (DelayedOutput ? AgnusChipSlotScheduler.SlotCycles : 0);
    }
}
