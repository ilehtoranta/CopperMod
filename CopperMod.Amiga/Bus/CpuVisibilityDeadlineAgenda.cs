/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Bus
{
    internal enum CpuVisibilityDeadlineSource : byte
    {
        Interrupt,
        VerticalBlank,
        HorizontalSyncTod,
        CiaTimer,
        Disk,
        Paula,
        Copper,
        Control,
        Blitter,
        Count
    }

    /// <summary>
    /// Fixed-size tournament with one root per 68000 interrupt mask. Leaves
    /// retain both the absolute deadline and its diagnostic payload.
    /// </summary>
    internal sealed class CpuVisibilityDeadlineAgenda
    {
        private const int InterruptMaskCount = 8;
        private const int LeafCount = 16;
        private const int TreeSize = LeafCount * 2;
        private readonly long[] _cycles = new long[InterruptMaskCount * TreeSize];
        private readonly byte[] _winners = new byte[InterruptMaskCount * TreeSize];
        private readonly AmigaDiskController.SchedulerWakeReason[] _diskReasons =
            new AmigaDiskController.SchedulerWakeReason[InterruptMaskCount * LeafCount];

        public CpuVisibilityDeadlineAgenda() => Reset();

        public void Reset()
        {
            Array.Fill(_cycles, long.MaxValue);
            for (var mask = 0; mask < InterruptMaskCount; mask++)
            {
                var treeBase = mask * TreeSize;
                for (var leaf = 0; leaf < LeafCount; leaf++)
                {
                    _winners[treeBase + LeafCount + leaf] = (byte)leaf;
                }

                Rebuild(mask);
            }

            Array.Clear(_diskReasons);
        }

        public bool Set(
            int interruptMask,
            CpuVisibilityDeadlineSource source,
            long cycle,
            AmigaDiskController.SchedulerWakeReason diskReason = AmigaDiskController.SchedulerWakeReason.None)
        {
            interruptMask &= 7;
            var leaf = (int)source;
            if ((uint)leaf >= (uint)CpuVisibilityDeadlineSource.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(source));
            }

            var treeBase = interruptMask * TreeSize;
            var leafNode = treeBase + LeafCount + leaf;
            var diskIndex = (interruptMask * LeafCount) + leaf;
            if (_cycles[leafNode] == cycle && _diskReasons[diskIndex] == diskReason)
            {
                return false;
            }

            _cycles[leafNode] = cycle;
            _diskReasons[diskIndex] = diskReason;
            RebuildPath(treeBase, (LeafCount + leaf) >> 1);
            return true;
        }

        public void SetAll(CpuVisibilityDeadlineSource source, long cycle)
        {
            for (var mask = 0; mask < InterruptMaskCount; mask++)
            {
                _ = Set(mask, source, cycle);
            }
        }

        public (long Cycle, CpuVisibilityDeadlineSource Source, AmigaDiskController.SchedulerWakeReason DiskReason)
            Get(int interruptMask)
        {
            interruptMask &= 7;
            var treeBase = interruptMask * TreeSize;
            var winner = _winners[treeBase + 1];
            return (
                _cycles[treeBase + 1],
                (CpuVisibilityDeadlineSource)winner,
                _diskReasons[(interruptMask * LeafCount) + winner]);
        }

        public (long Cycle, AmigaDiskController.SchedulerWakeReason DiskReason) GetLeaf(
            int interruptMask,
            CpuVisibilityDeadlineSource source)
        {
            interruptMask &= 7;
            var leaf = (int)source;
            var treeBase = interruptMask * TreeSize;
            return (
                _cycles[treeBase + LeafCount + leaf],
                _diskReasons[(interruptMask * LeafCount) + leaf]);
        }

        private void Rebuild(int interruptMask)
        {
            var treeBase = interruptMask * TreeSize;
            for (var node = LeafCount - 1; node > 0; node--)
            {
                SelectWinner(treeBase, node);
            }
        }

        private void RebuildPath(int treeBase, int node)
        {
            while (node > 0)
            {
                SelectWinner(treeBase, node);
                node >>= 1;
            }
        }

        private void SelectWinner(int treeBase, int node)
        {
            var left = treeBase + (node << 1);
            var right = left + 1;
            var destination = treeBase + node;
            // Left wins ties, preserving the documented source priority.
            var winnerNode = _cycles[left] <= _cycles[right] ? left : right;
            _cycles[destination] = _cycles[winnerNode];
            _winners[destination] = _winners[winnerNode];
        }
    }
}
