/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;

namespace CopperMod.Amiga
{
    using System;

    internal sealed partial class AmigaHardwareScheduler
    {
        public bool HostProfilingEnabled { get; set; }

        public void ResetHostProfile()
        {
            _hostDrainTicks = 0;
            _hostWakeQueryTicks = 0;
            _hostSameCycleQueryTicks = 0;
            _hostRasterlineSkipTicks = 0;
            _hostRasterTicks = 0;
            _hostCiaTicks = 0;
            _hostPaulaTicks = 0;
            _hostDiskTicks = 0;
            _hostAgnusTicks = 0;
            _hostBlitterTicks = 0;
        }

        public void DrainTo(long targetCycle, AmigaHardwareEventMask mask)
        {
            if (!HostProfilingEnabled)
            {
                DrainToCore(targetCycle, mask);
                return;
            }

            var start = Stopwatch.GetTimestamp();
            try
            {
                DrainToCore(targetCycle, mask);
            }
            finally
            {
                _hostDrainTicks += Stopwatch.GetTimestamp() - start;
            }
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
                rasterlineCache.InvalidationCount,
                _wakeAgendaCacheHits,
                _wakeAgendaCacheMisses,
                _wakeAgendaDrainSkips,
                _wakeAgendaInvalidations,
                HostProfilingEnabled,
                _hostDrainTicks,
                _hostWakeQueryTicks,
                _hostSameCycleQueryTicks,
                _hostRasterlineSkipTicks,
                _hostRasterTicks,
                _hostCiaTicks,
                _hostPaulaTicks,
                _hostDiskTicks,
                _hostAgnusTicks,
                _hostBlitterTicks);
        }

        private bool TrySkipDrainWithRasterlineScheduleProfiled(long targetCycle, AmigaHardwareEventMask mask)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                return TrySkipDrainWithRasterlineSchedule(targetCycle, mask);
            }
            finally
            {
                _hostRasterlineSkipTicks += Stopwatch.GetTimestamp() - start;
            }
        }

        private long GetNextEventCycleProfiled(long currentCycle, long targetCycle, AmigaHardwareEventMask mask)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                return GetNextEventCycle(currentCycle, targetCycle, mask);
            }
            finally
            {
                _hostWakeQueryTicks += Stopwatch.GetTimestamp() - start;
            }
        }

        private long GetNextSlotContendedEventCycleProfiled(long currentCycle, long targetCycle)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                return GetNextSlotContendedEventCycle(currentCycle, targetCycle);
            }
            finally
            {
                _hostWakeQueryTicks += Stopwatch.GetTimestamp() - start;
            }
        }

        private void ProcessEventsAtProfiled(long cycle, AmigaHardwareEventMask mask)
        {
            Debug.Assert(cycle >= 0, "Hardware scheduler event cycles must be non-negative.");
            InvalidateWakeAgenda();
            if ((mask & AmigaHardwareEventMask.Raster) != 0 &&
                _bus.GetNextRasterEventCycle(cycle, cycle) <= cycle)
            {
                var start = Stopwatch.GetTimestamp();
                _bus.AdvanceRasterCoreTo(cycle);
                _hostRasterTicks += Stopwatch.GetTimestamp() - start;
                _rasterEvents++;
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0 &&
                _bus.GetNextCiaTimerEventCycle(cycle, cycle) <= cycle)
            {
                var start = Stopwatch.GetTimestamp();
                _bus.AdvanceCiaTimersCoreTo(cycle);
                _hostCiaTicks += Stopwatch.GetTimestamp() - start;
                _ciaEvents++;
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0 &&
                _bus.Paula.HasRegisterObservableWorkThrough(cycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceRegisterObservableTo(cycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                _paulaEvents++;
            }

            if ((mask & AmigaHardwareEventMask.PaulaInterruptSources) != 0 &&
                _bus.Paula.HasInterruptSourceWorkThrough(cycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceInterruptSourcesTo(cycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                _paulaEvents++;
            }

            if ((mask & AmigaHardwareEventMask.PaulaDma) != 0 &&
                _bus.Paula.HasDmaWorkThrough(cycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceDmaObservableTo(cycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                _paulaEvents++;
            }

            if ((mask & (AmigaHardwareEventMask.DiskEvents | AmigaHardwareEventMask.DiskPassiveInput)) != 0 &&
                HasDiskWakeSourceThrough(cycle, mask) &&
                HasDiskWorkThrough(cycle, mask))
            {
                var start = Stopwatch.GetTimestamp();
                if ((mask & (AmigaHardwareEventMask.ForceCatchUp | AmigaHardwareEventMask.DiskPassiveInput)) != 0)
                {
                    _bus.Disk.AdvanceTo(cycle);
                }
                else
                {
                    _bus.Disk.AdvanceEventsTo(cycle);
                }

                _hostDiskTicks += Stopwatch.GetTimestamp() - start;
                InvalidateDiskWakeFalseCache();
                _diskEvents++;
            }

            if ((mask & AmigaHardwareEventMask.Agnus) != 0 &&
                GetNextAgnusEventCycle(cycle, cycle, mask) <= cycle)
            {
                var start = Stopwatch.GetTimestamp();
                _bus.AdvanceAgnusCoreTo(cycle);
                _hostAgnusTicks += Stopwatch.GetTimestamp() - start;
                _agnusEvents++;
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0 &&
                _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, cycle - 1), cycle) <= cycle)
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Blitter.AdvanceTo(cycle);
                _hostBlitterTicks += Stopwatch.GetTimestamp() - start;
                _blitterEvents++;
            }
        }

        private void ProcessSlotContendedEventsAtProfiled(long cycle)
        {
            Debug.Assert(cycle >= 0, "Hardware scheduler event cycles must be non-negative.");
            InvalidateWakeAgenda();
            if (_bus.Paula.HasDmaWorkThrough(cycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceDmaObservableTo(cycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                _paulaEvents++;
            }

            if (HasDiskWakeSourceThrough(cycle, SlotContendedMemoryAccessMask) &&
                HasDiskWorkThrough(cycle, SlotContendedMemoryAccessMask))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Disk.AdvanceEventsTo(cycle);
                _hostDiskTicks += Stopwatch.GetTimestamp() - start;
                InvalidateDiskWakeFalseCache();
                _diskEvents++;
            }

            if (_bus.GetNextAgnusEventCycle(cycle, cycle) <= cycle)
            {
                var start = Stopwatch.GetTimestamp();
                _bus.AdvanceAgnusCoreTo(cycle);
                _hostAgnusTicks += Stopwatch.GetTimestamp() - start;
                _agnusEvents++;
            }

            if (_bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, cycle - 1), cycle) <= cycle)
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Blitter.AdvanceTo(cycle);
                _hostBlitterTicks += Stopwatch.GetTimestamp() - start;
                _blitterEvents++;
            }
        }

        private void ProcessSlotContendedTargetCatchUpProfiled(long targetCycle, bool blitterWasBusyAtDrainStart)
        {
            if (_bus.Paula.HasDmaWorkThrough(targetCycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceDmaObservableTo(targetCycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }

            if (HasDiskWakeSourceThrough(targetCycle, SlotContendedMemoryAccessMask) &&
                HasDiskWorkThrough(targetCycle, SlotContendedMemoryAccessMask))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Disk.AdvanceEventsTo(targetCycle);
                _hostDiskTicks += Stopwatch.GetTimestamp() - start;
                InvalidateDiskWakeFalseCache();
                InvalidateWakeAgenda();
            }

            if (!blitterWasBusyAtDrainStart &&
                _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle)
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Blitter.AdvanceTo(targetCycle);
                _hostBlitterTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }
        }

        private void ProcessTargetCatchUpProfiled(long targetCycle, AmigaHardwareEventMask mask, bool blitterWasBusyAtDrainStart)
        {
            var forceCatchUp = (mask & AmigaHardwareEventMask.ForceCatchUp) != 0;
            if ((mask & AmigaHardwareEventMask.Raster) != 0 &&
                (forceCatchUp || _bus.GetNextRasterEventCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.AdvanceRasterCoreTo(targetCycle);
                _hostRasterTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.CiaTimers) != 0 &&
                (forceCatchUp ||
                    (mask & AmigaHardwareEventMask.CiaRegisterSample) != 0 ||
                    _bus.GetNextCiaTimerEventCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.AdvanceCiaTimersCoreTo(targetCycle);
                _hostCiaTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.PaulaRegister) != 0 &&
                (forceCatchUp || _bus.Paula.HasRegisterObservableWorkThrough(targetCycle)))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceRegisterObservableTo(targetCycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.PaulaInterruptSources) != 0 &&
                (forceCatchUp || _bus.Paula.HasInterruptSourceWorkThrough(targetCycle)))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceInterruptSourcesTo(targetCycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.PaulaDma) != 0 &&
                (forceCatchUp || _bus.Paula.HasDmaWorkThrough(targetCycle)))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Paula.AdvanceDmaObservableTo(targetCycle);
                _hostPaulaTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }

            if ((mask & (AmigaHardwareEventMask.DiskEvents | AmigaHardwareEventMask.DiskPassiveInput)) != 0 &&
                (forceCatchUp ||
                    (HasDiskWakeSourceThrough(targetCycle, mask) && HasDiskWorkThrough(targetCycle, mask))))
            {
                var start = Stopwatch.GetTimestamp();
                if (forceCatchUp || (mask & AmigaHardwareEventMask.DiskPassiveInput) != 0)
                {
                    _bus.Disk.AdvanceTo(targetCycle);
                }
                else
                {
                    _bus.Disk.AdvanceEventsTo(targetCycle);
                }

                _hostDiskTicks += Stopwatch.GetTimestamp() - start;
                InvalidateDiskWakeFalseCache();
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.DiskCiaEvents) != 0 &&
                (forceCatchUp || (_bus.Disk.HasCiaWakeSource() && _bus.Disk.HasCiaEventThrough(targetCycle))))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Disk.AdvanceCiaEventsTo(targetCycle);
                _hostDiskTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }

            if ((mask & AmigaHardwareEventMask.Blitter) != 0 &&
                !blitterWasBusyAtDrainStart &&
                (forceCatchUp || _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                var start = Stopwatch.GetTimestamp();
                _bus.Blitter.AdvanceTo(targetCycle);
                _hostBlitterTicks += Stopwatch.GetTimestamp() - start;
                InvalidateWakeAgenda();
            }
        }

        private bool HasSameCycleWorkProfiled(long cycle, AmigaHardwareEventMask mask)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                return HasSameCycleWork(cycle, mask);
            }
            finally
            {
                _hostSameCycleQueryTicks += Stopwatch.GetTimestamp() - start;
            }
        }
    }
}
