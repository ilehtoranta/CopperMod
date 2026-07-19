/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Scheduler
    {
        private struct WakeAgendaEntry
        {
            public bool Valid;
            public AmigaHardwareEventMask Mask;
            public long ValidFromCycle;
            public long ValidThroughCycle;
            public long CandidateCycle;
            public ulong SchedulerGeneration;
            public long NextHorizontalSyncCycle;
            public long NextVerticalBlankCycle;
            public ulong CiaAWakeVersion;
            public ulong CiaBWakeVersion;
            public ulong PaulaWakeVersion;
            public ulong DiskWakeVersion;
            public ulong DisplayWakeVersion;
            public ulong BlitterWakeVersion;
        }

        private bool TrySkipDrainWithWakeAgenda(
            long targetCycle,
            AmigaHardwareEventMask mask)
        {
            if (!_hasDrained ||
                _earliestDirtyCycle <= targetCycle ||
                !CanUseWakeAgenda(mask))
            {
                return false;
            }

            var cursor = Math.Max(0, Math.Min(GetMaskDrainedThroughCycle(mask), targetCycle));
            var candidate = mask == SlotContendedMemoryAccessMask
                ? GetNextSlotContendedEventCycle(cursor, targetCycle)
                : GetNextEventCycle(cursor, targetCycle, mask);
            if (candidate != long.MaxValue && candidate <= targetCycle)
            {
                return false;
            }

            _wakeAgendaDrainSkips++;
            return true;
        }

        private bool TryGetWakeAgendaCandidate(
            long currentCycle,
            long targetCycle,
            AmigaHardwareEventMask mask,
            out long candidate)
        {
            candidate = long.MaxValue;
            if (!CanUseWakeAgenda(mask) || targetCycle <= currentCycle)
            {
                return false;
            }

            ref var entry = ref GetWakeAgendaEntry(mask);
            if (IsWakeAgendaEntryValid(in entry, mask, currentCycle, targetCycle))
            {
                _wakeAgendaCacheHits++;
                candidate = entry.CandidateCycle <= targetCycle ? entry.CandidateCycle : long.MaxValue;
                return true;
            }

            _wakeAgendaCacheMisses++;
            var horizonCycle = GetWakeAgendaHorizon(targetCycle, mask);
            var horizonCandidate = mask == SlotContendedMemoryAccessMask
                ? GetNextSlotContendedEventCycleUncached(currentCycle, horizonCycle)
                : GetNextEventCycleUncached(currentCycle, horizonCycle, mask);
            var validThroughCycle = horizonCandidate == long.MaxValue
                ? horizonCycle
                : Math.Min(horizonCandidate, horizonCycle);

            entry = new WakeAgendaEntry
            {
                Valid = true,
                Mask = mask,
                ValidFromCycle = currentCycle,
                ValidThroughCycle = validThroughCycle,
                CandidateCycle = horizonCandidate,
            };
            CaptureWakeAgendaVersions(ref entry, mask);

            candidate = horizonCandidate <= targetCycle ? horizonCandidate : long.MaxValue;
            return true;
        }

        private bool IsWakeAgendaEntryValid(
            in WakeAgendaEntry entry,
            AmigaHardwareEventMask mask,
            long currentCycle,
            long targetCycle)
        {
            if (!entry.Valid ||
                entry.Mask != mask ||
                currentCycle < entry.ValidFromCycle ||
                targetCycle > entry.ValidThroughCycle ||
                (entry.CandidateCycle != long.MaxValue && currentCycle > entry.CandidateCycle) ||
                entry.SchedulerGeneration != _generation)
            {
                return false;
            }

            if (entry.PaulaWakeVersion != _bus.Paula.RegisterWakeVersion ||
                entry.DiskWakeVersion != _bus.Disk.SchedulerWakeVersion ||
                entry.BlitterWakeVersion != _bus.Blitter.SchedulerWakeVersion)
            {
                return false;
            }

            // A cached wake agenda is valid only while every source captured for this mask is unchanged.
            if (mask == SlotContendedMemoryAccessMask)
            {
                return entry.DisplayWakeVersion == _bus.Display.LiveWakeVersion;
            }

            return entry.NextHorizontalSyncCycle == _bus.NextHorizontalSyncCycle &&
                entry.NextVerticalBlankCycle == _bus.NextVerticalBlankCycle &&
                entry.CiaAWakeVersion == _bus.CiaA.WakeVersion &&
                entry.CiaBWakeVersion == _bus.CiaB.WakeVersion;
        }

        private ref WakeAgendaEntry GetWakeAgendaEntry(AmigaHardwareEventMask mask)
        {
            if (mask == InterruptPollReadMask)
            {
                return ref _interruptPollWakeAgenda;
            }

            return ref _slotContendedWakeAgenda;
        }

        private void CaptureWakeAgendaVersions(ref WakeAgendaEntry entry, AmigaHardwareEventMask mask)
        {
            entry.SchedulerGeneration = _generation;
            entry.PaulaWakeVersion = _bus.Paula.RegisterWakeVersion;
            entry.DiskWakeVersion = _bus.Disk.SchedulerWakeVersion;
            entry.BlitterWakeVersion = _bus.Blitter.SchedulerWakeVersion;

            if (mask == SlotContendedMemoryAccessMask)
            {
                entry.DisplayWakeVersion = _bus.Display.LiveWakeVersion;
                return;
            }

            entry.NextHorizontalSyncCycle = _bus.NextHorizontalSyncCycle;
            entry.NextVerticalBlankCycle = _bus.NextVerticalBlankCycle;
            entry.CiaAWakeVersion = _bus.CiaA.WakeVersion;
            entry.CiaBWakeVersion = _bus.CiaB.WakeVersion;
        }

        private static bool CanUseWakeAgenda(AmigaHardwareEventMask mask)
        {
            const AmigaHardwareEventMask sensitiveMask =
                AmigaHardwareEventMask.ForceCatchUp |
                AmigaHardwareEventMask.DiskPassiveInput |
                AmigaHardwareEventMask.CpuBoundary |
                AmigaHardwareEventMask.DiskCiaEvents;
            return (mask & sensitiveMask) == 0 &&
                (mask == SlotContendedMemoryAccessMask || mask == InterruptPollReadMask);
        }

        private long GetWakeAgendaHorizon(long targetCycle, AmigaHardwareEventMask mask)
            => CanExtendWakeAgendaToLineEnd(mask) ? GetLineEndCycle(targetCycle) : targetCycle;

        private static bool CanExtendWakeAgendaToLineEnd(AmigaHardwareEventMask mask)
            => mask == SlotContendedMemoryAccessMask || mask == InterruptPollReadMask;

        private long GetLineEndCycle(long targetCycle)
        {
            return Math.Max(targetCycle, _bus.GetLineStopCycle(targetCycle));
        }

        private void InvalidateWakeAgenda()
        {
            var hadValidEntry = _slotContendedWakeAgenda.Valid || _interruptPollWakeAgenda.Valid;
            _slotContendedWakeAgenda = default;
            _interruptPollWakeAgenda = default;
            InvalidateCpuVisibleNoEventCache();
            if (hadValidEntry)
            {
                _wakeAgendaInvalidations++;
            }
        }
    }
}
