/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaHardwareScheduler
    {
        // v1 caches only the hot INTREQR poll path; live Agnus/display stays
        // on explicit advancement until every display invalidation source is queryable.
        private const AmigaHardwareEventMask InterruptPollReadMask =
            AmigaHardwareEventMask.Raster |
            AmigaHardwareEventMask.CiaTimers |
            AmigaHardwareEventMask.PaulaRegister |
            AmigaHardwareEventMask.DiskEvents |
            AmigaHardwareEventMask.Blitter;
        private const AmigaHardwareEventMask SlotContendedMemoryAccessMask =
            AmigaHardwareEventMask.PaulaRegister |
            AmigaHardwareEventMask.PaulaDma |
            AmigaHardwareEventMask.DiskEvents |
            AmigaHardwareEventMask.Agnus |
            AmigaHardwareEventMask.Blitter;
        private const AmigaHardwareEventMask DiskWakeCacheKeyMask =
            AmigaHardwareEventMask.DiskEvents |
            AmigaHardwareEventMask.DiskPassiveInput;

        private readonly AmigaBus _bus;
        private long _lastDrainCycle;
        private long _rasterDrainCycle;
        private long _ciaDrainCycle;
        private long _paulaDrainCycle;
        private long _paulaDmaDrainCycle;
        private long _diskEventDrainCycle;
        private long _diskCiaDrainCycle;
        private long _diskPassiveDrainCycle;
        private long _agnusDrainCycle;
        private long _blitterDrainCycle;
        private long _earliestDirtyCycle = long.MaxValue;
        /// <summary>
        /// Cached minimum of drain cycles for the <see cref="SlotContendedMemoryAccessMask"/>.
        /// When <c>targetCycle &lt;= _slotContendedCleanThroughCycle</c> and no dirty/generation
        /// invalidation, the drain can be skipped with a single comparison.
        /// </summary>
        private long _slotContendedCleanThroughCycle = -1;
        private ulong _generation;
        private ulong _lastCleanGeneration;
        private bool _hasDrained;
        private bool _draining;
        private long _drainCount;
        private long _busAccessDrainCount;
        private long _sameCycleDrainCount;
        private long _rasterEvents;
        private long _ciaEvents;
        private long _paulaEvents;
        private long _diskEvents;
        private long _agnusEvents;
        private long _blitterEvents;
        private long _hostDrainTicks;
        private long _hostWakeQueryTicks;
        private long _hostSameCycleQueryTicks;
        private long _hostRasterlineSkipTicks;
        private long _hostRasterTicks;
        private long _hostCiaTicks;
        private long _hostPaulaTicks;
        private long _hostDiskTicks;
        private long _hostAgnusTicks;
        private long _hostBlitterTicks;
        private bool _diskWakeFalseCacheValid;
        private AmigaHardwareEventMask _diskWakeFalseCacheKey;
        private long _diskWakeFalseThroughCycle;
        private WakeAgendaEntry _slotContendedWakeAgenda;
        private WakeAgendaEntry _interruptPollWakeAgenda;
        private long _wakeAgendaCacheHits;
        private long _wakeAgendaCacheMisses;
        private long _wakeAgendaDrainSkips;
        private long _wakeAgendaInvalidations;

        public AmigaHardwareScheduler(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public void Reset()
        {
            _lastDrainCycle = 0;
            _rasterDrainCycle = -1;
            _ciaDrainCycle = -1;
            _paulaDrainCycle = -1;
            _paulaDmaDrainCycle = -1;
            _diskEventDrainCycle = -1;
            _diskCiaDrainCycle = -1;
            _diskPassiveDrainCycle = -1;
            _agnusDrainCycle = -1;
            _blitterDrainCycle = -1;
            _earliestDirtyCycle = long.MaxValue;
            _slotContendedCleanThroughCycle = -1;
            _generation = 0;
            _lastCleanGeneration = 0;
            _hasDrained = false;
            _draining = false;
            _drainCount = 0;
            _busAccessDrainCount = 0;
            _sameCycleDrainCount = 0;
            _rasterEvents = 0;
            _ciaEvents = 0;
            _paulaEvents = 0;
            _diskEvents = 0;
            _agnusEvents = 0;
            _blitterEvents = 0;
            _wakeAgendaCacheHits = 0;
            _wakeAgendaCacheMisses = 0;
            _wakeAgendaDrainSkips = 0;
            _wakeAgendaInvalidations = 0;
            _slotContendedWakeAgenda = default;
            _interruptPollWakeAgenda = default;
            ResetHostProfile();
            InvalidateDiskWakeFalseCache();
        }

        public void NotifyWorkScheduled(long cycle)
        {
            cycle = Math.Max(0, cycle);
            InvalidateWakeAgenda();
            InvalidateDiskWakeFalseCache();
            _slotContendedCleanThroughCycle = Math.Min(_slotContendedCleanThroughCycle, cycle - 1);
            _bus.InvalidateRasterlineSchedule(cycle, AmigaHardwareEventMask.All);
            if (_hasDrained && cycle <= _lastDrainCycle)
            {
                _generation++;
                _earliestDirtyCycle = Math.Min(_earliestDirtyCycle, cycle);
            }
        }

        /// <summary>
        /// Fast check: returns true if no hardware events need processing for a slot-contended
        /// (chip RAM / expansion RAM) access at the given cycle. This is a single comparison
        /// against a precomputed horizon and avoids the full drain-check path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSlotContendedCleanThrough(long targetCycle)
            => targetCycle <= _slotContendedCleanThroughCycle;

        public void DrainForCpuAccess(
            AmigaBusAccessTarget target,
            uint address,
            long targetCycle,
            bool isWrite)
        {
            _busAccessDrainCount++;
            DrainTo(targetCycle, GetCpuAccessMask(target, address, isWrite));
        }

        public long GetNextCpuVisibleEventCycle(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out M68kTraceBatchWakeSource wakeSource)
        {
            wakeSource = M68kTraceBatchWakeSource.TargetCycle;
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return currentCycle;
            }

            var candidate = targetCycle;
            var pendingPaulaInterruptLevel = _bus.Paula.GetHighestCpuVisibleInterruptLevel(currentCycle);
            var pendingPaulaInterruptCanEnter = pendingPaulaInterruptLevel > 0 &&
                (cpuInterruptMask < 0 || pendingPaulaInterruptLevel > (cpuInterruptMask & 0x07));
            if (_bus.HasPendingCiaInterrupts || pendingPaulaInterruptCanEnter)
            {
                candidate = currentCycle + 1;
                wakeSource = M68kTraceBatchWakeSource.PendingInterrupt;
            }

            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.Paula.GetNextCpuVisibleInterruptCycle(currentCycle, targetCycle, cpuInterruptMask),
                M68kTraceBatchWakeSource.PendingInterrupt,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.NextVerticalBlankCycle,
                M68kTraceBatchWakeSource.VerticalBlank,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.CiaB.GetNextTodInterruptCycle(targetCycle, _bus.NextHorizontalSyncCycle, _bus.PalLineCycles),
                M68kTraceBatchWakeSource.HorizontalSyncTod,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.GetNextCiaInterruptCycle(targetCycle),
                M68kTraceBatchWakeSource.CiaTimer,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.Disk.GetNextWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Disk,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.Paula.GetNextCpuWakeCandidateCycle(currentCycle, targetCycle, cpuInterruptMask),
                M68kTraceBatchWakeSource.Paula,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.Display.GetNextLiveCopperWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Copper,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.Blitter.GetNextWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Blitter,
                ref wakeSource);
            return Math.Clamp(candidate, currentCycle + 1, targetCycle);
        }
    }
}
