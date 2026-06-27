using System;
using System.Diagnostics;

namespace CopperMod.Amiga
{
    [Flags]
    internal enum AmigaHardwareEventMask
    {
        None = 0,
        Raster = 1 << 0,
        CiaTimers = 1 << 1,
        PaulaRegister = 1 << 2,
        DiskEvents = 1 << 3,
        DiskPassiveInput = 1 << 4,
        Agnus = 1 << 5,
        Blitter = 1 << 6,
        DiskCiaEvents = 1 << 7,
        ForceCatchUp = 1 << 8,
        CiaRegisterSample = 1 << 9,
        DiskRegisterSample = 1 << 10,
        CpuBoundary = 1 << 11,
        All = Raster |
            CiaTimers |
            PaulaRegister |
            DiskEvents |
            Agnus |
            Blitter |
            DiskCiaEvents
    }

    internal readonly record struct AmigaHardwareSchedulerSnapshot(
        long LastDrainCycle,
        long DrainCount,
        long BusAccessDrainCount,
        long SameCycleDrainCount,
        long RasterEvents,
        long CiaEvents,
        long PaulaEvents,
        long DiskEvents,
        long AgnusEvents,
        long BlitterEvents,
        long RasterlineCacheHits,
        long RasterlineCacheMisses,
        long RasterlineCacheRebuilds,
        long RasterlineCacheInvalidations);

    internal sealed class AmigaHardwareScheduler
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
            AmigaHardwareEventMask.DiskEvents |
            AmigaHardwareEventMask.Agnus |
            AmigaHardwareEventMask.Blitter;
        private const AmigaHardwareEventMask DiskWakeCacheKeyMask =
            AmigaHardwareEventMask.DiskEvents |
            AmigaHardwareEventMask.DiskPassiveInput |
            AmigaHardwareEventMask.DiskRegisterSample;

        private readonly AmigaBus _bus;
        private long _lastDrainCycle;
        private long _rasterDrainCycle;
        private long _ciaDrainCycle;
        private long _paulaDrainCycle;
        private long _diskEventDrainCycle;
        private long _diskCiaDrainCycle;
        private long _diskPassiveDrainCycle;
        private long _agnusDrainCycle;
        private long _blitterDrainCycle;
        private long _earliestDirtyCycle = long.MaxValue;
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
        private bool _diskWakeFalseCacheValid;
        private AmigaHardwareEventMask _diskWakeFalseCacheKey;
        private long _diskWakeFalseThroughCycle;

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
            _diskEventDrainCycle = -1;
            _diskCiaDrainCycle = -1;
            _diskPassiveDrainCycle = -1;
            _agnusDrainCycle = -1;
            _blitterDrainCycle = -1;
            _earliestDirtyCycle = long.MaxValue;
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
            InvalidateDiskWakeFalseCache();
        }

        public void NotifyWorkScheduled(long cycle)
        {
            cycle = Math.Max(0, cycle);
            InvalidateDiskWakeFalseCache();
            _bus.InvalidateRasterlineSchedule(cycle, AmigaHardwareEventMask.All);
            if (_hasDrained && cycle <= _lastDrainCycle)
            {
                _generation++;
                _earliestDirtyCycle = Math.Min(_earliestDirtyCycle, cycle);
            }
        }

        public void DrainForCpuAccess(
            AmigaBusAccessTarget target,
            uint address,
            long targetCycle,
            bool isWrite)
        {
            _busAccessDrainCount++;
            DrainTo(targetCycle, GetCpuAccessMask(target, address, isWrite));
        }

        public void DrainTo(long targetCycle, AmigaHardwareEventMask mask)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (mask == AmigaHardwareEventMask.None)
            {
                MarkClean(targetCycle, mask);
                return;
            }

            if (_draining)
            {
                return;
            }

            if (CanSkipDrain(targetCycle, mask))
            {
                return;
            }

            if (mask == InterruptPollReadMask &&
                TrySkipDrainWithRasterlineSchedule(targetCycle, mask))
            {
                MarkClean(targetCycle, mask);
                return;
            }

            _draining = true;
            _drainCount++;
            if (_hasDrained && targetCycle == _lastDrainCycle)
            {
                _sameCycleDrainCount++;
            }

            try
            {
                var blitterWasBusyAtDrainStart = _bus.Blitter.Busy;
                var forceCatchUp = (mask & AmigaHardwareEventMask.ForceCatchUp) != 0;
                var cpuBoundary = (mask & AmigaHardwareEventMask.CpuBoundary) != 0;
                if ((mask & AmigaHardwareEventMask.Agnus) != 0 &&
                    !cpuBoundary &&
                    (forceCatchUp || _bus.Display.HasLiveDisplayWork()))
                {
                    _bus.AdvanceAgnusCoreTo(targetCycle);
                    _agnusEvents++;
                }

                var cursor = _hasDrained ? Math.Min(_lastDrainCycle, targetCycle) : 0;
                if (_earliestDirtyCycle <= targetCycle)
                {
                    cursor = Math.Min(cursor, _earliestDirtyCycle);
                }

                while (true)
                {
                    var nextCycle = GetNextEventCycle(cursor, targetCycle, mask);
                    if (nextCycle == long.MaxValue || nextCycle > targetCycle)
                    {
                        break;
                    }

                    var generationBeforeEvent = _generation;
                    ProcessEventsAt(nextCycle, mask);
                    _bus.InvalidateRasterlineSchedule(nextCycle, mask);
                    cursor = nextCycle == long.MaxValue ? targetCycle : nextCycle;

                    var sameCycleWorkRemains = HasSameCycleWork(cursor, mask);
                    var madeSameCycleProgress = _generation != generationBeforeEvent;
                    if (cursor < targetCycle && (!sameCycleWorkRemains || !madeSameCycleProgress))
                    {
                        cursor++;
                    }
                    else if (cursor == targetCycle && (!sameCycleWorkRemains || !madeSameCycleProgress))
                    {
                        break;
                    }
                }

                ProcessTargetCatchUp(targetCycle, mask, blitterWasBusyAtDrainStart);
                MarkClean(targetCycle, mask);
            }
            finally
            {
                _draining = false;
            }
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
                _bus.Paula.GetNextWakeCandidateCycle(currentCycle, targetCycle),
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

        public AmigaHardwareSchedulerSnapshot CaptureSnapshot()
        {
            var rasterlineCache = _bus.CaptureRasterlineScheduleCacheSnapshot();
            return new AmigaHardwareSchedulerSnapshot(
                _lastDrainCycle,
                _drainCount,
                _busAccessDrainCount,
                _sameCycleDrainCount,
                _rasterEvents,
                _ciaEvents,
                _paulaEvents,
                _diskEvents,
                _agnusEvents,
                _blitterEvents,
                rasterlineCache.HitCount,
                rasterlineCache.MissCount,
                rasterlineCache.RebuildCount,
                rasterlineCache.InvalidationCount);
        }

        private void MarkClean(long targetCycle, AmigaHardwareEventMask mask)
        {
            _lastDrainCycle = Math.Max(_lastDrainCycle, targetCycle);
            if (mask == InterruptPollReadMask)
            {
                _rasterDrainCycle = Math.Max(_rasterDrainCycle, targetCycle);
                _ciaDrainCycle = Math.Max(_ciaDrainCycle, targetCycle);
                _paulaDrainCycle = Math.Max(_paulaDrainCycle, targetCycle);
                _diskEventDrainCycle = Math.Max(_diskEventDrainCycle, targetCycle);
                _blitterDrainCycle = Math.Max(_blitterDrainCycle, targetCycle);
                CompleteCleanMark(targetCycle);
                return;
            }

            if ((mask & AmigaHardwareEventMask.Raster) != 0)
            {
                _rasterDrainCycle = Math.Max(_rasterDrainCycle, targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0)
            {
                _ciaDrainCycle = Math.Max(_ciaDrainCycle, targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0)
            {
                _paulaDrainCycle = Math.Max(_paulaDrainCycle, targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.DiskEvents) != 0)
            {
                _diskEventDrainCycle = Math.Max(_diskEventDrainCycle, targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.DiskCiaEvents) != 0)
            {
                _diskCiaDrainCycle = Math.Max(_diskCiaDrainCycle, targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.DiskPassiveInput) != 0)
            {
                _diskPassiveDrainCycle = Math.Max(_diskPassiveDrainCycle, targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.Agnus) != 0)
            {
                _agnusDrainCycle = Math.Max(_agnusDrainCycle, targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0)
            {
                _blitterDrainCycle = Math.Max(_blitterDrainCycle, targetCycle);
            }

            CompleteCleanMark(targetCycle);
        }

        private void CompleteCleanMark(long targetCycle)
        {
            if (_earliestDirtyCycle <= targetCycle)
            {
                _earliestDirtyCycle = long.MaxValue;
            }

            _lastCleanGeneration = _generation;
            _hasDrained = true;
        }

        private bool CanSkipDrain(long targetCycle, AmigaHardwareEventMask mask)
        {
            var dirtyAffectsTarget = _earliestDirtyCycle <= targetCycle;
            return _hasDrained &&
                IsMaskDrainedThrough(mask, targetCycle) &&
                !dirtyAffectsTarget &&
                _generation == _lastCleanGeneration &&
                !HasSameCycleWork(targetCycle, mask);
        }

        private bool TrySkipDrainWithRasterlineSchedule(long targetCycle, AmigaHardwareEventMask mask)
        {
            if (!_hasDrained ||
                _earliestDirtyCycle <= targetCycle)
            {
                return false;
            }

            var cursor = Math.Min(GetMaskDrainedThroughCycle(mask), targetCycle);
            var targetLineStartCycle = targetCycle - (targetCycle % _bus.PalLineCycles);
            if (cursor < targetLineStartCycle - 1)
            {
                return false;
            }

            return _bus.TrySkipRasterlineScheduleDrain(cursor, targetCycle, mask);
        }

        private bool IsMaskDrainedThrough(AmigaHardwareEventMask mask, long targetCycle)
        {
            return ((mask & AmigaHardwareEventMask.Raster) == 0 || _rasterDrainCycle >= targetCycle) &&
                ((mask & AmigaHardwareEventMask.CiaTimers) == 0 || _ciaDrainCycle >= targetCycle) &&
                ((mask & AmigaHardwareEventMask.PaulaRegister) == 0 || _paulaDrainCycle >= targetCycle) &&
                ((mask & AmigaHardwareEventMask.DiskEvents) == 0 || _diskEventDrainCycle >= targetCycle) &&
                ((mask & AmigaHardwareEventMask.DiskCiaEvents) == 0 || _diskCiaDrainCycle >= targetCycle) &&
                ((mask & AmigaHardwareEventMask.DiskPassiveInput) == 0 || _diskPassiveDrainCycle >= targetCycle) &&
                ((mask & AmigaHardwareEventMask.Agnus) == 0 || _agnusDrainCycle >= targetCycle) &&
                ((mask & AmigaHardwareEventMask.Blitter) == 0 || _blitterDrainCycle >= targetCycle);
        }

        private long GetMaskDrainedThroughCycle(AmigaHardwareEventMask mask)
        {
            var cycle = long.MaxValue;
            if ((mask & AmigaHardwareEventMask.Raster) != 0)
            {
                cycle = Math.Min(cycle, _rasterDrainCycle);
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0)
            {
                cycle = Math.Min(cycle, _ciaDrainCycle);
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0)
            {
                cycle = Math.Min(cycle, _paulaDrainCycle);
            }

            if ((mask & AmigaHardwareEventMask.DiskEvents) != 0)
            {
                cycle = Math.Min(cycle, _diskEventDrainCycle);
            }

            if ((mask & AmigaHardwareEventMask.DiskCiaEvents) != 0)
            {
                cycle = Math.Min(cycle, _diskCiaDrainCycle);
            }

            if ((mask & AmigaHardwareEventMask.DiskPassiveInput) != 0)
            {
                cycle = Math.Min(cycle, _diskPassiveDrainCycle);
            }

            if ((mask & AmigaHardwareEventMask.Agnus) != 0)
            {
                cycle = Math.Min(cycle, _agnusDrainCycle);
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0)
            {
                cycle = Math.Min(cycle, _blitterDrainCycle);
            }

            return cycle == long.MaxValue ? _lastDrainCycle : cycle;
        }

        private long GetNextEventCycle(long currentCycle, long targetCycle, AmigaHardwareEventMask mask)
        {
            var candidate = long.MaxValue;
            if ((mask & AmigaHardwareEventMask.Raster) != 0)
            {
                candidate = Min(candidate, _bus.GetNextRasterEventCycle(currentCycle, targetCycle));
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0)
            {
                candidate = Min(candidate, _bus.GetNextCiaTimerEventCycle(currentCycle, targetCycle));
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0)
            {
                candidate = Min(candidate, GetNextPaulaEventCycle(currentCycle, targetCycle));
            }

            if ((mask & (AmigaHardwareEventMask.DiskEvents | AmigaHardwareEventMask.DiskPassiveInput)) != 0 &&
                HasDiskWakeSourceThrough(targetCycle, mask))
            {
                candidate = Min(candidate, GetNextDiskEventCycle(currentCycle, targetCycle, mask));
            }

            if ((mask & AmigaHardwareEventMask.Agnus) != 0)
            {
                candidate = Min(candidate, GetNextAgnusEventCycle(currentCycle, targetCycle, mask));
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0)
            {
                candidate = Min(candidate, _bus.Blitter.GetNextWakeCandidateCycle(currentCycle, targetCycle));
            }

            return candidate;
        }

        private void ProcessEventsAt(long cycle, AmigaHardwareEventMask mask)
        {
            Debug.Assert(cycle >= 0, "Hardware scheduler event cycles must be non-negative.");
            if ((mask & AmigaHardwareEventMask.Raster) != 0 &&
                _bus.GetNextRasterEventCycle(cycle, cycle) <= cycle)
            {
                _bus.AdvanceRasterCoreTo(cycle);
                _rasterEvents++;
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0 &&
                _bus.GetNextCiaTimerEventCycle(cycle, cycle) <= cycle)
            {
                _bus.AdvanceCiaTimersCoreTo(cycle);
                _ciaEvents++;
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0 &&
                _bus.Paula.HasRegisterObservableWorkThrough(cycle))
            {
                _bus.Paula.AdvanceRegisterObservableTo(cycle);
                _paulaEvents++;
            }

            if ((mask & (AmigaHardwareEventMask.DiskEvents | AmigaHardwareEventMask.DiskPassiveInput)) != 0 &&
                HasDiskWakeSourceThrough(cycle, mask) &&
                HasDiskWorkThrough(cycle, mask))
            {
                if ((mask & (AmigaHardwareEventMask.ForceCatchUp | AmigaHardwareEventMask.DiskPassiveInput)) != 0)
                {
                    _bus.Disk.AdvanceTo(cycle);
                }
                else
                {
                    _bus.Disk.AdvanceEventsTo(cycle);
                }

                InvalidateDiskWakeFalseCache();
                _diskEvents++;
            }

            if ((mask & AmigaHardwareEventMask.Agnus) != 0 &&
                GetNextAgnusEventCycle(cycle, cycle, mask) <= cycle)
            {
                _bus.AdvanceAgnusCoreTo(cycle);
                _agnusEvents++;
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0 &&
                _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, cycle - 1), cycle) <= cycle)
            {
                _bus.Blitter.AdvanceTo(cycle);
                _blitterEvents++;
            }
        }

        private void ProcessTargetCatchUp(long targetCycle, AmigaHardwareEventMask mask, bool blitterWasBusyAtDrainStart)
        {
            var forceCatchUp = (mask & AmigaHardwareEventMask.ForceCatchUp) != 0;
            if ((mask & AmigaHardwareEventMask.Raster) != 0 &&
                (forceCatchUp || _bus.GetNextRasterEventCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                _bus.AdvanceRasterCoreTo(targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0 &&
                (forceCatchUp ||
                    (mask & AmigaHardwareEventMask.CiaRegisterSample) != 0 ||
                    _bus.GetNextCiaTimerEventCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                _bus.AdvanceCiaTimersCoreTo(targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0 &&
                (forceCatchUp || _bus.Paula.HasRegisterObservableWorkThrough(targetCycle)))
            {
                _bus.Paula.AdvanceRegisterObservableTo(targetCycle);
            }

            if ((mask & (AmigaHardwareEventMask.DiskEvents | AmigaHardwareEventMask.DiskPassiveInput)) != 0 &&
                (forceCatchUp ||
                    (HasDiskWakeSourceThrough(targetCycle, mask) && HasDiskWorkThrough(targetCycle, mask))))
            {
                if (forceCatchUp || (mask & AmigaHardwareEventMask.DiskPassiveInput) != 0)
                {
                    _bus.Disk.AdvanceTo(targetCycle);
                }
                else
                {
                    _bus.Disk.AdvanceEventsTo(targetCycle);
                }

                InvalidateDiskWakeFalseCache();
            }

            if ((mask & AmigaHardwareEventMask.DiskCiaEvents) != 0 &&
                (forceCatchUp || (_bus.Disk.HasCiaWakeSource() && _bus.Disk.HasCiaEventThrough(targetCycle))))
            {
                _bus.Disk.AdvanceCiaEventsTo(targetCycle);
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0 &&
                !blitterWasBusyAtDrainStart &&
                (forceCatchUp || _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                _bus.Blitter.AdvanceTo(targetCycle);
            }
        }

        private bool HasSameCycleWork(long cycle, AmigaHardwareEventMask mask)
        {
            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0 &&
                _bus.Paula.HasRegisterObservableWorkThrough(cycle))
            {
                return true;
            }

            return (mask & (AmigaHardwareEventMask.DiskEvents | AmigaHardwareEventMask.DiskPassiveInput)) != 0 &&
                HasDiskWakeSourceThrough(cycle, mask) &&
                HasDiskWorkThrough(cycle, mask);
        }

        private long GetNextPaulaEventCycle(long currentCycle, long targetCycle)
        {
            if (_bus.Paula.HasRegisterObservableWorkThrough(currentCycle))
            {
                return currentCycle;
            }

            return _bus.Paula.GetNextWakeCandidateCycle(currentCycle, targetCycle) ?? long.MaxValue;
        }

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
                var candidate = _bus.Disk.GetNextWakeCandidateCycle(currentCycle, targetCycle) ?? long.MaxValue;
                if ((mask & AmigaHardwareEventMask.DiskRegisterSample) != 0)
                {
                    candidate = Min(
                        candidate,
                        _bus.Disk.GetNextEventWakeCandidateCycle(
                            currentCycle,
                            targetCycle,
                            includeActiveDmaProgress: true));
                }

                return candidate;
            }

            var includeActiveDmaProgress = (mask & AmigaHardwareEventMask.DiskRegisterSample) != 0;
            return _bus.Disk.GetNextEventWakeCandidateCycle(currentCycle, targetCycle, includeActiveDmaProgress) ?? long.MaxValue;
        }

        private long GetNextAgnusEventCycle(long currentCycle, long targetCycle, AmigaHardwareEventMask mask)
        {
            return (mask & AmigaHardwareEventMask.CpuBoundary) != 0
                ? _bus.GetNextCpuVisibleAgnusEventCycle(currentCycle, targetCycle)
                : _bus.GetNextAgnusEventCycle(currentCycle, targetCycle);
        }

        private bool HasDiskWorkThrough(long cycle, AmigaHardwareEventMask mask)
        {
            return (mask & AmigaHardwareEventMask.DiskPassiveInput) != 0
                ? _bus.Disk.HasWakeCandidateThrough(cycle) ||
                    ((mask & AmigaHardwareEventMask.DiskRegisterSample) != 0 &&
                        _bus.Disk.HasEventWakeCandidateThrough(cycle, includeActiveDmaProgress: true))
                : _bus.Disk.HasEventWakeCandidateThrough(
                    cycle,
                    includeActiveDmaProgress: (mask & AmigaHardwareEventMask.DiskRegisterSample) != 0);
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

            var includeActiveDmaProgress = (mask & AmigaHardwareEventMask.DiskRegisterSample) != 0;
            var horizonCycle = GetDiskWakeFalseCacheHorizon(targetCycle);
            if (horizonCycle > targetCycle &&
                !_bus.Disk.HasSchedulerWakeSourceThrough(
                    horizonCycle,
                    includePassiveInput,
                    includeEvents,
                    includeActiveDmaProgress))
            {
                CacheDiskWakeFalseThrough(cacheKey, horizonCycle);
                return false;
            }

            var hasSource = _bus.Disk.HasSchedulerWakeSourceThrough(
                targetCycle,
                includePassiveInput,
                includeEvents,
                includeActiveDmaProgress);
            if (!hasSource)
            {
                CacheDiskWakeFalseThrough(cacheKey, targetCycle);
            }

            return hasSource;
        }

        private long GetDiskWakeFalseCacheHorizon(long targetCycle)
        {
            var lineCycles = _bus.PalLineCycles;
            if (lineCycles <= 1)
            {
                return targetCycle;
            }

            var lineCycle = targetCycle % lineCycles;
            var cyclesUntilLineEnd = lineCycles - lineCycle - 1;
            return cyclesUntilLineEnd <= 0
                ? targetCycle
                : targetCycle + cyclesUntilLineEnd;
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

        private static AmigaHardwareEventMask GetCpuAccessMask(
            AmigaBusAccessTarget target,
            uint address,
            bool isWrite)
        {
            if (target == AmigaBusAccessTarget.Cia)
            {
                return AmigaHardwareEventMask.Raster |
                    AmigaHardwareEventMask.CiaTimers |
                    AmigaHardwareEventMask.DiskCiaEvents |
                    AmigaHardwareEventMask.CiaRegisterSample;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters && !isWrite)
            {
                return GetCustomRegisterReadMask(address);
            }

            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock)
            {
                return SlotContendedMemoryAccessMask;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                return AmigaHardwareEventMask.All;
            }

            return AmigaHardwareEventMask.None;
        }

        private static AmigaHardwareEventMask GetCustomRegisterReadMask(uint address)
        {
            switch (AmigaBus.GetCustomRegisterReadAdvanceKindForScheduler(address))
            {
                case CustomRegisterReadAdvanceKind.BeamPosition:
                case CustomRegisterReadAdvanceKind.InputOnly:
                    return AmigaHardwareEventMask.None;

                case CustomRegisterReadAdvanceKind.BlitterStatus:
                    return AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.Blitter;

                case CustomRegisterReadAdvanceKind.InterruptSources:
                    return AmigaHardwareEventMask.Raster |
                        AmigaHardwareEventMask.CiaTimers |
                        AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.DiskEvents |
                        AmigaHardwareEventMask.Blitter;

                case CustomRegisterReadAdvanceKind.DiskEventOnly:
                    return AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.DiskEvents |
                        AmigaHardwareEventMask.DiskRegisterSample;

                case CustomRegisterReadAdvanceKind.DiskPassiveInput:
                    return AmigaHardwareEventMask.PaulaRegister |
                        AmigaHardwareEventMask.DiskEvents |
                        AmigaHardwareEventMask.DiskPassiveInput |
                        AmigaHardwareEventMask.DiskRegisterSample;

                default:
                    return AmigaHardwareEventMask.PaulaRegister;
            }
        }

        private static long Min(long left, long? right)
            => right.HasValue ? Math.Min(left, right.Value) : left;

        private static long Min(long left, long right)
            => Math.Min(left, right);

        private static long MinWakeCandidate(
            long candidate,
            long currentCycle,
            long targetCycle,
            long? eventCycle,
            M68kTraceBatchWakeSource eventSource,
            ref M68kTraceBatchWakeSource wakeSource)
        {
            if (!eventCycle.HasValue || eventCycle.Value > targetCycle)
            {
                return candidate;
            }

            var normalized = Math.Clamp(eventCycle.Value, currentCycle + 1, targetCycle);
            if (normalized < candidate)
            {
                wakeSource = eventSource;
                return normalized;
            }

            return candidate;
        }
    }
}
