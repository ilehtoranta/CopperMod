using System;
using System.Diagnostics;

namespace CopperMod.Amiga
{
    internal sealed partial class AmigaHardwareScheduler
    {
        private void DrainToCore(long targetCycle, AmigaHardwareEventMask mask)
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

            if (mask == SlotContendedMemoryAccessMask)
            {
                DrainSlotContendedAccess(targetCycle);
                return;
            }

            if (CanSkipDrain(targetCycle, mask))
            {
                return;
            }

            if (mask == InterruptPollReadMask &&
                (HostProfilingEnabled
                    ? TrySkipDrainWithRasterlineScheduleProfiled(targetCycle, mask)
                    : TrySkipDrainWithRasterlineSchedule(targetCycle, mask)))
            {
                MarkClean(targetCycle, mask);
                return;
            }

            if (TrySkipDrainWithWakeAgenda(targetCycle, mask, agnusAlreadyAdvanced: false))
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
                    if (HostProfilingEnabled)
                    {
                        var start = Stopwatch.GetTimestamp();
                        _bus.AdvanceAgnusCoreTo(targetCycle);
                        _hostAgnusTicks += Stopwatch.GetTimestamp() - start;
                    }
                    else
                    {
                        _bus.AdvanceAgnusCoreTo(targetCycle);
                    }

                    _agnusEvents++;
                }

                if (TrySkipDrainWithWakeAgenda(targetCycle, mask, agnusAlreadyAdvanced: true))
                {
                    MarkClean(targetCycle, mask);
                    return;
                }

                var cursor = _hasDrained ? Math.Min(_lastDrainCycle, targetCycle) : 0;
                if (_earliestDirtyCycle <= targetCycle)
                {
                    cursor = Math.Min(cursor, _earliestDirtyCycle);
                }

                while (true)
                {
                    var nextCycle = HostProfilingEnabled
                        ? GetNextEventCycleProfiled(cursor, targetCycle, mask)
                        : GetNextEventCycle(cursor, targetCycle, mask);
                    if (nextCycle == long.MaxValue || nextCycle > targetCycle)
                    {
                        break;
                    }

                    var generationBeforeEvent = _generation;
                    if (HostProfilingEnabled)
                    {
                        ProcessEventsAtProfiled(nextCycle, mask);
                    }
                    else
                    {
                        ProcessEventsAt(nextCycle, mask);
                    }

                    _bus.InvalidateRasterlineSchedule(nextCycle, mask);
                    cursor = nextCycle == long.MaxValue ? targetCycle : nextCycle;

                    // Same-cycle work may be scheduled by the event just processed.
                    var sameCycleWorkRemains = HostProfilingEnabled
                        ? HasSameCycleWorkProfiled(cursor, mask)
                        : HasSameCycleWork(cursor, mask);
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

                if (HostProfilingEnabled)
                {
                    ProcessTargetCatchUpProfiled(targetCycle, mask, blitterWasBusyAtDrainStart);
                }
                else
                {
                    ProcessTargetCatchUp(targetCycle, mask, blitterWasBusyAtDrainStart);
                }

                MarkClean(targetCycle, mask);
            }
            finally
            {
                _draining = false;
            }
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

        private bool CanSkipSlotContendedDrain(long targetCycle)
        {
            var dirtyAffectsTarget = _earliestDirtyCycle <= targetCycle;
            return _hasDrained &&
                _paulaDrainCycle >= targetCycle &&
                _diskEventDrainCycle >= targetCycle &&
                _agnusDrainCycle >= targetCycle &&
                _blitterDrainCycle >= targetCycle &&
                !dirtyAffectsTarget &&
                _generation == _lastCleanGeneration &&
                !HasSlotContendedSameCycleWork(targetCycle);
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

        private void DrainSlotContendedAccess(long targetCycle)
        {
            if (CanSkipSlotContendedDrain(targetCycle))
            {
                return;
            }

            if (TrySkipDrainWithWakeAgenda(
                targetCycle,
                SlotContendedMemoryAccessMask,
                agnusAlreadyAdvanced: false))
            {
                MarkClean(targetCycle, SlotContendedMemoryAccessMask);
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
                if (_bus.Display.HasLiveDisplayWork())
                {
                    if (HostProfilingEnabled)
                    {
                        var start = Stopwatch.GetTimestamp();
                        _bus.AdvanceAgnusCoreTo(targetCycle);
                        _hostAgnusTicks += Stopwatch.GetTimestamp() - start;
                    }
                    else
                    {
                        _bus.AdvanceAgnusCoreTo(targetCycle);
                    }

                    _agnusEvents++;
                }

                if (TrySkipDrainWithWakeAgenda(
                    targetCycle,
                    SlotContendedMemoryAccessMask,
                    agnusAlreadyAdvanced: true))
                {
                    MarkClean(targetCycle, SlotContendedMemoryAccessMask);
                    return;
                }

                var cursor = _hasDrained ? Math.Min(_lastDrainCycle, targetCycle) : 0;
                if (_earliestDirtyCycle <= targetCycle)
                {
                    cursor = Math.Min(cursor, _earliestDirtyCycle);
                }

                while (true)
                {
                    var nextCycle = HostProfilingEnabled
                        ? GetNextSlotContendedEventCycleProfiled(cursor, targetCycle)
                        : GetNextSlotContendedEventCycle(cursor, targetCycle);
                    if (nextCycle == long.MaxValue || nextCycle > targetCycle)
                    {
                        break;
                    }

                    var generationBeforeEvent = _generation;
                    if (HostProfilingEnabled)
                    {
                        ProcessSlotContendedEventsAtProfiled(nextCycle);
                    }
                    else
                    {
                        ProcessSlotContendedEventsAt(nextCycle);
                    }

                    _bus.InvalidateRasterlineSchedule(nextCycle, SlotContendedMemoryAccessMask);
                    cursor = nextCycle == long.MaxValue ? targetCycle : nextCycle;

                    // Same-cycle Paula/disk work must remain visible to the CPU access.
                    bool sameCycleWorkRemains;
                    if (HostProfilingEnabled)
                    {
                        var start = Stopwatch.GetTimestamp();
                        sameCycleWorkRemains = HasSlotContendedSameCycleWork(cursor);
                        _hostSameCycleQueryTicks += Stopwatch.GetTimestamp() - start;
                    }
                    else
                    {
                        sameCycleWorkRemains = HasSlotContendedSameCycleWork(cursor);
                    }

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

                if (HostProfilingEnabled)
                {
                    ProcessSlotContendedTargetCatchUpProfiled(targetCycle, blitterWasBusyAtDrainStart);
                }
                else
                {
                    ProcessSlotContendedTargetCatchUp(targetCycle, blitterWasBusyAtDrainStart);
                }

                MarkClean(targetCycle, SlotContendedMemoryAccessMask);
            }
            finally
            {
                _draining = false;
            }
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
            return TryGetWakeAgendaCandidate(currentCycle, targetCycle, mask, out var candidate)
                ? candidate
                : GetNextEventCycleUncached(currentCycle, targetCycle, mask);
        }

        private long GetNextEventCycleUncached(long currentCycle, long targetCycle, AmigaHardwareEventMask mask)
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

        private long GetNextSlotContendedEventCycle(long currentCycle, long targetCycle)
        {
            return TryGetWakeAgendaCandidate(
                    currentCycle,
                    targetCycle,
                    SlotContendedMemoryAccessMask,
                    out var candidate)
                ? candidate
                : GetNextSlotContendedEventCycleUncached(currentCycle, targetCycle);
        }

        private long GetNextSlotContendedEventCycleUncached(long currentCycle, long targetCycle)
        {
            var candidate = long.MaxValue;
            candidate = Min(candidate, GetNextPaulaEventCycle(currentCycle, targetCycle));

            if (HasDiskWakeSourceThrough(targetCycle, SlotContendedMemoryAccessMask))
            {
                candidate = Min(
                    candidate,
                    GetNextDiskEventCycle(currentCycle, targetCycle, SlotContendedMemoryAccessMask));
            }

            candidate = Min(candidate, _bus.GetNextAgnusEventCycle(currentCycle, targetCycle));
            candidate = Min(candidate, _bus.Blitter.GetNextWakeCandidateCycle(currentCycle, targetCycle));
            return candidate;
        }

        private void ProcessEventsAt(long cycle, AmigaHardwareEventMask mask)
        {
            Debug.Assert(cycle >= 0, "Hardware scheduler event cycles must be non-negative.");
            InvalidateWakeAgenda();
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

        private void ProcessSlotContendedEventsAt(long cycle)
        {
            Debug.Assert(cycle >= 0, "Hardware scheduler event cycles must be non-negative.");
            InvalidateWakeAgenda();
            if (_bus.Paula.HasRegisterObservableWorkThrough(cycle))
            {
                _bus.Paula.AdvanceRegisterObservableTo(cycle);
                _paulaEvents++;
            }

            if (HasDiskWakeSourceThrough(cycle, SlotContendedMemoryAccessMask) &&
                HasDiskWorkThrough(cycle, SlotContendedMemoryAccessMask))
            {
                _bus.Disk.AdvanceEventsTo(cycle);
                InvalidateDiskWakeFalseCache();
                _diskEvents++;
            }

            if (_bus.GetNextAgnusEventCycle(cycle, cycle) <= cycle)
            {
                _bus.AdvanceAgnusCoreTo(cycle);
                _agnusEvents++;
            }

            if (_bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, cycle - 1), cycle) <= cycle)
            {
                _bus.Blitter.AdvanceTo(cycle);
                _blitterEvents++;
            }
        }

        private void ProcessSlotContendedTargetCatchUp(long targetCycle, bool blitterWasBusyAtDrainStart)
        {
            if (_bus.Paula.HasRegisterObservableWorkThrough(targetCycle))
            {
                _bus.Paula.AdvanceRegisterObservableTo(targetCycle);
                InvalidateWakeAgenda();
            }

            if (HasDiskWakeSourceThrough(targetCycle, SlotContendedMemoryAccessMask) &&
                HasDiskWorkThrough(targetCycle, SlotContendedMemoryAccessMask))
            {
                _bus.Disk.AdvanceEventsTo(targetCycle);
                InvalidateDiskWakeFalseCache();
                InvalidateWakeAgenda();
            }

            if (!blitterWasBusyAtDrainStart &&
                _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle)
            {
                _bus.Blitter.AdvanceTo(targetCycle);
                InvalidateWakeAgenda();
            }
        }

        private void ProcessTargetCatchUp(long targetCycle, AmigaHardwareEventMask mask, bool blitterWasBusyAtDrainStart)
        {
            var forceCatchUp = (mask & AmigaHardwareEventMask.ForceCatchUp) != 0;
            if ((mask & AmigaHardwareEventMask.Raster) != 0 &&
                (forceCatchUp || _bus.GetNextRasterEventCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                _bus.AdvanceRasterCoreTo(targetCycle);
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0 &&
                (forceCatchUp ||
                    (mask & AmigaHardwareEventMask.CiaRegisterSample) != 0 ||
                    _bus.GetNextCiaTimerEventCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                _bus.AdvanceCiaTimersCoreTo(targetCycle);
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0 &&
                (forceCatchUp || _bus.Paula.HasRegisterObservableWorkThrough(targetCycle)))
            {
                _bus.Paula.AdvanceRegisterObservableTo(targetCycle);
                InvalidateWakeAgenda();
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
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.DiskCiaEvents) != 0 &&
                (forceCatchUp || (_bus.Disk.HasCiaWakeSource() && _bus.Disk.HasCiaEventThrough(targetCycle))))
            {
                _bus.Disk.AdvanceCiaEventsTo(targetCycle);
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0 &&
                !blitterWasBusyAtDrainStart &&
                (forceCatchUp || _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                _bus.Blitter.AdvanceTo(targetCycle);
                InvalidateWakeAgenda();
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

        private bool HasSlotContendedSameCycleWork(long cycle)
        {
            return _bus.Paula.HasRegisterObservableWorkThrough(cycle) ||
                (HasDiskWakeSourceThrough(cycle, SlotContendedMemoryAccessMask) &&
                    HasDiskWorkThrough(cycle, SlotContendedMemoryAccessMask));
        }

        private long GetNextPaulaEventCycle(long currentCycle, long targetCycle)
        {
            if (_bus.Paula.HasRegisterObservableWorkThrough(currentCycle))
            {
                return currentCycle;
            }

            return _bus.Paula.GetNextWakeCandidateCycle(currentCycle, targetCycle) ?? long.MaxValue;
        }

        private long GetNextAgnusEventCycle(long currentCycle, long targetCycle, AmigaHardwareEventMask mask)
        {
            // CPU-boundary drains must not reserve pure refresh/display slots.
            return (mask & AmigaHardwareEventMask.CpuBoundary) != 0
                ? _bus.GetNextCpuVisibleAgnusEventCycle(currentCycle, targetCycle)
                : _bus.GetNextAgnusEventCycle(currentCycle, targetCycle);
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
