/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using CopperMod.Amiga.CustomChips.Denise;

namespace CopperMod.Amiga.Bus
{
    /// <summary>
    /// Agnus-owned fixed DMA plan storage for the previous, active, and prepared
    /// rasterlines. Denise temporarily builds and consumes these records while
    /// fixed-slot materialization migrates into the bus executor.
    /// </summary>
    internal sealed class AgnusRasterlineDmaPlanRing
    {
        public const int LineCount = 3;
        private const int SpriteChannelCount = 8;
        private const int SpriteWordsPerChannel = 2;
        private readonly int _maxSpriteEntriesPerLine;

        public AgnusRasterlineDmaPlanRing(int maxBitplaneEntriesPerLine, int maxSpriteEntriesPerLine)
        {
            _maxSpriteEntriesPerLine = maxSpriteEntriesPerLine;
            Plans = new RowDmaPlan[LineCount];
            BitplaneEntries = new RowDmaBitplaneEntry[LineCount * maxBitplaneEntriesPerLine];
            SpriteEntries = new RowDmaSpriteEntry[LineCount * maxSpriteEntriesPerLine];
            ExecutedMasks = new byte[LineCount];
            BitplaneCursorIndices = new int[LineCount];
            SpriteCursorIndices = new int[LineCount];
        }

        public RowDmaPlan[] Plans { get; }

        public RowDmaBitplaneEntry[] BitplaneEntries { get; }

        public RowDmaSpriteEntry[] SpriteEntries { get; }

        public byte[] ExecutedMasks { get; }

        public int[] BitplaneCursorIndices { get; }

        public int[] SpriteCursorIndices { get; }

        public void SetBitplaneEntry(int index, in RowDmaBitplaneEntry entry)
            => BitplaneEntries[index] = entry;

        public void SetSpriteEntry(int index, in RowDmaSpriteEntry entry)
            => SpriteEntries[index] = entry;

        public int MaterializeOcsSpriteEntries(
            int ringSlot,
            long lineStartCycle,
            int cyclesPerHorizontalPosition,
            bool dmaEnabled)
        {
            if ((uint)ringSlot >= LineCount)
            {
                throw new System.ArgumentOutOfRangeException(nameof(ringSlot));
            }

            var start = ringSlot * _maxSpriteEntriesPerLine;
            if (!dmaEnabled)
            {
                return 0;
            }

            var firstCycle = lineStartCycle +
                ((long)AgnusHrmOcsSlotTable.FirstSpriteHorizontal * cyclesPerHorizontalPosition);
            var count = 0;
            for (var channel = 0; channel < SpriteChannelCount; channel++)
            {
                for (var word = 0; word < SpriteWordsPerChannel; word++)
                {
                    var horizontalOffset = (channel * 4) + (word * 2);
                    SpriteEntries[start + count++] = new RowDmaSpriteEntry(
                        firstCycle + ((long)horizontalOffset * cyclesPerHorizontalPosition),
                        channel,
                        word);
                }
            }

            return count;
        }

        public void Commit(int ringSlot, in RowDmaPlan plan)
        {
            Plans[ringSlot] = plan;
            ExecutedMasks[ringSlot] = 0;
            BitplaneCursorIndices[ringSlot] = plan.BitplaneStart;
            SpriteCursorIndices[ringSlot] = plan.SpriteStart;
        }

        public void PatchSpriteSuffix(int ringSlot, in RowDmaPlan plan)
        {
            Plans[ringSlot] = plan;
            SpriteCursorIndices[ringSlot] = plan.SpriteStart;
        }

        public void PatchBitplaneSuffix(int ringSlot, in RowDmaPlan plan)
        {
            Plans[ringSlot] = plan;
            BitplaneCursorIndices[ringSlot] = plan.BitplaneStart;
        }

        public bool TryGetFixedOwnerAt(long cycle, out AgnusChipSlotOwner owner, out int entryIndex)
        {
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(System.Math.Max(0, cycle));
            for (var ringSlot = 0; ringSlot < LineCount; ringSlot++)
            {
                var plan = Plans[ringSlot];
                if (!plan.Valid)
                {
                    continue;
                }

                var bitplaneEnd = plan.BitplaneStart + plan.BitplaneCount;
                for (var index = plan.BitplaneStart; index < bitplaneEnd; index++)
                {
                    if (AgnusChipSlotScheduler.AlignToSlot(
                            BitplaneEntries[index].GetCycle(plan.LineStartCycle)) == slotCycle)
                    {
                        owner = AgnusChipSlotOwner.Bitplane;
                        entryIndex = index;
                        return true;
                    }
                }

                var spriteEnd = plan.SpriteStart + plan.SpriteCount;
                for (var index = plan.SpriteStart; index < spriteEnd; index++)
                {
                    if (AgnusChipSlotScheduler.AlignToSlot(SpriteEntries[index].Cycle) == slotCycle)
                    {
                        owner = AgnusChipSlotOwner.Sprite;
                        entryIndex = index;
                        return true;
                    }
                }
            }

            owner = AgnusChipSlotOwner.Free;
            entryIndex = -1;
            return false;
        }

        public void Invalidate(int ringSlot)
        {
            Plans[ringSlot] = default;
            ExecutedMasks[ringSlot] = 0;
            BitplaneCursorIndices[ringSlot] = 0;
            SpriteCursorIndices[ringSlot] = 0;
        }

        public bool TryGetNextSpriteEntry(
            in RowDmaPlan plan,
            int spriteIndex,
            int word,
            out int entryIndex)
        {
            var ringSlot = GetRingSlot(plan.Row);
            var end = plan.SpriteStart + plan.SpriteCount;
            var index = SpriteCursorIndices[ringSlot];
            if (index < plan.SpriteStart || index > end)
            {
                index = plan.SpriteStart;
            }

            for (; index < end; index++)
            {
                var entry = SpriteEntries[index];
                if (entry.SpriteIndex > spriteIndex ||
                    entry.SpriteIndex == spriteIndex && entry.Word >= word)
                {
                    SpriteCursorIndices[ringSlot] = index;
                    entryIndex = index;
                    return true;
                }
            }

            SpriteCursorIndices[ringSlot] = end;
            entryIndex = -1;
            return false;
        }

        public void MarkSpriteEntryConsumed(in RowDmaPlan plan, int entryIndex)
        {
            var end = plan.SpriteStart + plan.SpriteCount;
            SpriteCursorIndices[GetRingSlot(plan.Row)] = System.Math.Min(entryIndex + 1, end);
        }

        public void Reset()
        {
            System.Array.Clear(Plans);
            System.Array.Clear(BitplaneEntries);
            System.Array.Clear(SpriteEntries);
            System.Array.Clear(ExecutedMasks);
            System.Array.Clear(BitplaneCursorIndices);
            System.Array.Clear(SpriteCursorIndices);
        }

        private static int GetRingSlot(int row)
        {
            var slot = row % LineCount;
            return slot < 0 ? slot + LineCount : slot;
        }
    }

    internal readonly struct RowDmaPlan
    {
        public RowDmaPlan(
            int generation,
            int row,
            long lineStartCycle,
            ushort dmacon,
            ushort bplcon0,
            int dmaPlanVersion,
            int signature,
            int bitplaneStart,
            int bitplaneCount,
            int spriteStart,
            int spriteCount,
            bool valid)
        {
            Generation = generation;
            Row = row;
            LineStartCycle = lineStartCycle;
            Dmacon = dmacon;
            Bplcon0 = bplcon0;
            DmaPlanVersion = dmaPlanVersion;
            Signature = signature;
            BitplaneStart = bitplaneStart;
            BitplaneCount = bitplaneCount;
            SpriteStart = spriteStart;
            SpriteCount = spriteCount;
            Valid = valid;
        }

        public int Generation { get; }
        public int Row { get; }
        public long LineStartCycle { get; }
        public ushort Dmacon { get; }
        public ushort Bplcon0 { get; }
        public int DmaPlanVersion { get; }
        public int Signature { get; }
        public int BitplaneStart { get; }
        public int BitplaneCount { get; }
        public int SpriteStart { get; }
        public int SpriteCount { get; }
        public bool Valid { get; }
    }

    internal readonly struct RowDmaSpriteEntry
    {
        public RowDmaSpriteEntry(long cycle, int spriteIndex, int word)
        {
            Cycle = cycle;
            SpriteIndex = spriteIndex;
            Word = word;
        }

        public long Cycle { get; }
        public int SpriteIndex { get; }
        public int Word { get; }
    }
}
