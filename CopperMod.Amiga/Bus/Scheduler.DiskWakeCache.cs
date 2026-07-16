/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

namespace CopperMod.Amiga
{
    using System;

    internal sealed partial class AmigaHardwareScheduler
    {
        private long GetNextDiskEventCycle(long currentCycle, long targetCycle, AmigaHardwareEventMask mask)
        {
            if (!HasDiskWakeSourceThrough(targetCycle, mask))
            {
                return long.MaxValue;
            }

            if (HasDiskWorkThrough(currentCycle, mask))
            {
                return currentCycle;
            }

            if ((mask & AmigaHardwareEventMask.DiskPassiveInput) != 0)
            {
                return _bus.Disk.GetNextWakeCandidateCycle(currentCycle, targetCycle) ?? long.MaxValue;
            }

            return _bus.Disk.GetNextEventWakeCandidateCycle(currentCycle, targetCycle, includeActiveDmaProgress: true) ?? long.MaxValue;
        }

        private bool HasDiskWorkThrough(long cycle, AmigaHardwareEventMask mask)
        {
            return (mask & AmigaHardwareEventMask.DiskPassiveInput) != 0
                ? _bus.Disk.HasWakeCandidateThrough(cycle)
                : _bus.Disk.HasEventWakeCandidateThrough(
                    cycle,
                    includeActiveDmaProgress: true);
        }

        private bool HasDiskWakeSourceThrough(long targetCycle, AmigaHardwareEventMask mask)
        {
            var includePassiveInput = (mask & AmigaHardwareEventMask.DiskPassiveInput) != 0;
            var includeEvents = (mask & AmigaHardwareEventMask.DiskEvents) != 0;
            if (!includePassiveInput && !includeEvents)
            {
                return false;
            }

            var cacheKey = mask & DiskWakeCacheKeyMask;
            if (_diskWakeFalseCacheValid &&
                _diskWakeFalseCacheKey == cacheKey &&
                targetCycle <= _diskWakeFalseThroughCycle)
            {
                return false;
            }

            // False disk wake lookups are cached only to the current line end and invalidated on disk progress.
            var horizonCycle = GetLineEndCycle(targetCycle);
            if (horizonCycle > targetCycle &&
                !_bus.Disk.HasSchedulerWakeSourceThrough(
                    horizonCycle,
                    includePassiveInput,
                    includeEvents,
                    includeActiveDmaProgress: true))
            {
                CacheDiskWakeFalseThrough(cacheKey, horizonCycle);
                return false;
            }

            var hasSource = _bus.Disk.HasSchedulerWakeSourceThrough(
                targetCycle,
                includePassiveInput,
                includeEvents,
                includeActiveDmaProgress: true);
            if (!hasSource)
            {
                CacheDiskWakeFalseThrough(cacheKey, targetCycle);
            }

            return hasSource;
        }

        private void CacheDiskWakeFalseThrough(AmigaHardwareEventMask cacheKey, long throughCycle)
        {
            var canExtendExisting =
                _diskWakeFalseCacheValid &&
                _diskWakeFalseCacheKey == cacheKey;

            _diskWakeFalseCacheValid = true;
            _diskWakeFalseCacheKey = cacheKey;
            _diskWakeFalseThroughCycle = canExtendExisting
                ? Math.Max(_diskWakeFalseThroughCycle, throughCycle)
                : throughCycle;
        }

        private void InvalidateDiskWakeFalseCache()
        {
            _diskWakeFalseCacheValid = false;
            _diskWakeFalseCacheKey = AmigaHardwareEventMask.None;
            _diskWakeFalseThroughCycle = long.MinValue;
        }
    }
}
