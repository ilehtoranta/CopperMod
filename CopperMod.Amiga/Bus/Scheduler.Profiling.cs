/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;

namespace CopperMod.Amiga.Bus
{
    using System;

    internal sealed partial class Scheduler
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
                _cpuVisibleNoEventCacheHits,
                _cpuVisibleNoEventCacheMisses,
                _cpuVisibleNoEventCacheInvalidations,
                _copperQuiescentSlotContendedAccesses,
                _copperQuiescentCustomRegisterWrites,
                _copperQuiescentCpuScheduleAffectingCustomWrites,
                _copperQuiescentCpuBenignCustomWrites,
                _copperQuiescentCopperScheduleAffectingCustomMoves,
                _copperQuiescentCopperBenignCustomMoves,
                _copperQuiescentSchedulerDrains,
                _copperQuiescentShadowPredictions,
                _copperQuiescentShadowMatches,
                _copperQuiescentShadowUnsupported,
                _copperQuiescentShadowMismatches,
                _copperQuiescentFirstShadowMismatch,
                _copperQuiescentFastPathAttempts,
                _copperQuiescentFastPathUsed,
                _copperQuiescentFastPathRejectedUnsupported,
                _copperQuiescentFastPathRejectedInvalidated,
                _copperQuiescentFastPathRejectedDynamicDma,
                _copperQuiescentFastPathSkippedDrains,
                _copperQuiescentFastPathVerificationMismatches,
                _copperQuiescentFastPathFirstMismatch,
                _bus.DeferredCpuBusBatchAttempts,
                _bus.DeferredCpuBusBatchUsed,
                _bus.DeferredCpuBusBatchInstructions,
                _bus.DeferredCpuBusBatchSkippedInstructionFlushes,
                _bus.DeferredCpuBusBatchFlushes,
                _bus.DeferredCpuBusBatchExitTargetCycle,
                _bus.DeferredCpuBusBatchExitMaxInstructions,
                _bus.DeferredCpuBusBatchExitChipVisibleAccess,
                _bus.DeferredCpuBusBatchExitPcLeftFastWindow,
                _bus.DeferredCpuBusBatchExitException,
                _bus.DeferredCpuBusBatchExitUnsupported,
                _bus.DeferredCpuBusBatchVerificationMismatches,
                _bus.DeferredCpuBusBatchFirstMismatch,
                _bus.DeferredCpuBusBatchWakeTargetCycle,
                _bus.DeferredCpuBusBatchWakePendingInterrupt,
                _bus.DeferredCpuBusBatchWakeVerticalBlank,
                _bus.DeferredCpuBusBatchWakeHorizontalSyncTod,
                _bus.DeferredCpuBusBatchWakeCiaTimer,
                _bus.DeferredCpuBusBatchWakeDisk,
                _bus.DeferredCpuBusBatchWakePaula,
                _bus.DeferredCpuBusBatchWakeCopper,
                _bus.DeferredCpuBusBatchWakeBlitter,
                _bus.DeferredCpuBusBatchDiskWakePendingDma,
                _bus.DeferredCpuBusBatchDiskWakeActiveDmaProgress,
                _bus.DeferredCpuBusBatchDiskWakeActiveDmaCompletion,
                _bus.DeferredCpuBusBatchDiskWakeSyncCandidate,
                _bus.DeferredCpuBusBatchDiskWakeIndexPulse,
                _bus.DeferredCpuBusBatchDiskWakePassiveByteReady,
                _bus.DeferredCpuBusBatchDiskWakeUnknown,
                _bus.DeferredCpuInternalNoBusWindowAttempts,
                _bus.DeferredCpuInternalNoBusWindowUsed,
                _bus.DeferredCpuInternalNoBusWindowTotalCycles,
                _bus.DeferredCpuInternalNoBusWindowAdvancedCycles,
                _bus.DeferredCpuInternalNoBusWindowMultiply,
                _bus.DeferredCpuInternalNoBusWindowDivide,
                _bus.DeferredCpuInternalNoBusWindowWakeTargetCycle,
                _bus.DeferredCpuInternalNoBusWindowWakePendingInterrupt,
                _bus.DeferredCpuInternalNoBusWindowWakeVerticalBlank,
                _bus.DeferredCpuInternalNoBusWindowWakeHorizontalSyncTod,
                _bus.DeferredCpuInternalNoBusWindowWakeCiaTimer,
                _bus.DeferredCpuInternalNoBusWindowWakeDisk,
                _bus.DeferredCpuInternalNoBusWindowWakePaula,
                _bus.DeferredCpuInternalNoBusWindowWakeCopper,
                _bus.DeferredCpuInternalNoBusWindowWakeBlitter,
                _bus.DeferredCpuInternalNoBusWindowVerificationMismatches,
                _bus.DeferredCpuInternalNoBusWindowFirstMismatch,
                _bus.DeferredCpuWaitWindowAttempts,
                _bus.DeferredCpuWaitWindowEligible,
                _bus.DeferredCpuWaitWindowTotalCycles,
                _bus.DeferredCpuWaitWindowMaxCycles,
                _bus.DeferredCpuWaitWindowInstructionFetch,
                _bus.DeferredCpuWaitWindowDataRead,
                _bus.DeferredCpuWaitWindowDataWrite,
                _bus.DeferredCpuWaitWindowCustom,
                _bus.DeferredCpuWaitWindowChipRam,
                _bus.DeferredCpuWaitWindowExpansionRam,
                _bus.DeferredCpuWaitWindowRealTimeClock,
                _bus.DeferredCpuWaitWindowCustomRegisters,
                _bus.DeferredCpuWaitWindowByte,
                _bus.DeferredCpuWaitWindowWord,
                _bus.DeferredCpuWaitWindowLong,
                _bus.DeferredCpuWaitWindowRead,
                _bus.DeferredCpuWaitWindowWrite,
                _bus.DeferredCpuWaitWindowSingleSlot,
                _bus.DeferredCpuWaitWindowLongSlot,
                _bus.DeferredCpuWaitWindowFastPathAttempts,
                _bus.DeferredCpuWaitWindowFastPathUsed,
                _bus.DeferredCpuWaitWindowFastPathRejectedUnsupported,
                _bus.DeferredCpuWaitWindowFastPathRejectedDynamicDma,
                _bus.DeferredCpuWaitWindowFastPathRejectedUnstable,
                _bus.DeferredCpuWaitWindowFastPathAdvancedCycles,
                _bus.DeferredCpuWaitWindowFastPathMaxAdvancedCycles,
                _bus.DeferredCpuWaitSlotShadowAttempts,
                _bus.DeferredCpuWaitSlotShadowMatches,
                _bus.DeferredCpuWaitSlotShadowMismatches,
                _bus.DeferredCpuWaitSlotShadowUnsupported,
                _bus.DeferredCpuWaitSlotShadowGrantMismatches,
                _bus.DeferredCpuWaitSlotShadowCompletionMismatches,
                _bus.DeferredCpuWaitSlotShadowSlotOwnerMismatches,
                _bus.DeferredCpuWaitSlotShadowBlitterStateMismatches,
                _bus.DeferredCpuWaitSlotShadowPaulaMismatches,
                _bus.DeferredCpuWaitSlotShadowDiskMismatches,
                _bus.DeferredCpuWaitSlotShadowDisplayMismatches,
                _bus.DeferredCpuWaitSlotShadowCopperMismatches,
                _bus.DeferredCpuWaitSlotShadowLiveAttempts,
                _bus.DeferredCpuWaitSlotShadowLiveSupported,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupported,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedPendingWrite,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedBitplaneWindow,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedCopperWaitWindow,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedRasterlinePlan,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedCpuPredict,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedUnstable,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedScratchWrite,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedLongWrite,
                _bus.DeferredCpuWaitSlotShadowLiveUnsupportedOther,
                _bus.DeferredCpuWaitSlotShadowLiveLongAccesses,
                _bus.DeferredCpuWaitSlotShadowLiveBitplaneFetches,
                _bus.DeferredCpuWaitSlotShadowLiveSpriteFetches,
                _bus.DeferredCpuWaitSlotShadowLiveCopperSteps,
                _bus.DeferredCpuWaitSlotShadowBlitterScratchAttempts,
                _bus.DeferredCpuWaitSlotShadowBlitterScratchSupported,
                _bus.DeferredCpuWaitSlotShadowBlitterScratchUnsupported,
                _bus.DeferredCpuWaitSlotShadowBlitterScratchMatches,
                _bus.DeferredCpuWaitSlotShadowBlitterScratchMismatches,
                _bus.DeferredCpuWaitSlotShadowBlitterScratchPartial,
                _bus.DeferredCpuWaitSlotShadowBlitterScratchMicroOps,
                _bus.DeferredCpuWaitSlotShadowFirstMismatch,
                _bus.DeferredCpuWaitFixedImageAttempts,
                _bus.DeferredCpuWaitFixedImageSupported,
                _bus.DeferredCpuWaitFixedImageMatches,
                _bus.DeferredCpuWaitFixedImageMismatches,
                _bus.DeferredCpuWaitFixedImageUnsupported,
                _bus.Display.CpuWaitFixedSlotImageBuilds,
                _bus.Display.CpuWaitFixedSlotImageHits,
                _bus.Display.CpuWaitFixedSlotImageMisses,
                _bus.Display.CpuWaitFixedSlotImageInvalidations,
                _bus.Display.CpuWaitFixedSlotImagePredictedSlots,
                _bus.Display.CpuWaitFixedSlotImageUnsupportedFrame,
                _bus.Display.CpuWaitFixedSlotImageUnsupportedCopper,
                _bus.Display.CpuWaitFixedSlotImageUnsupportedPendingWrite,
                _bus.Display.CpuWaitFixedSlotImageUnsupportedRasterlinePlan,
                _bus.Display.CpuWaitFixedSlotImageUnsupportedSpriteState,
                _bus.DeferredCpuWaitFixedImageFirstMismatch,
                _bus.DeferredCpuWaitFixedImageProductionAttempts,
                _bus.DeferredCpuWaitFixedImageProductionUsed,
                _bus.DeferredCpuWaitFixedImageProductionPreGrantDrainsSkipped,
                _bus.DeferredCpuWaitFixedImageProductionPostGrantCatchups,
                _bus.DeferredCpuWaitFixedImageProductionPredictedWaitCycles,
                _bus.DeferredCpuWaitFixedImageProductionFallbackUnsupported,
                _bus.DeferredCpuWaitFixedImageProductionFallbackDynamicDma,
                _bus.DeferredCpuWaitFixedImageProductionFallbackFrame,
                _bus.DeferredCpuWaitFixedImageProductionFallbackCopper,
                _bus.DeferredCpuWaitFixedImageProductionFallbackPendingWrite,
                _bus.DeferredCpuWaitFixedImageProductionFallbackRasterlinePlan,
                _bus.DeferredCpuWaitFixedImageProductionFallbackSpriteState,
                _bus.DeferredCpuWaitFixedImageProductionFallbackUnstable,
                _bus.DeferredCpuWaitFixedImageProductionVerificationMatches,
                _bus.DeferredCpuWaitFixedImageProductionVerificationMismatches,
                _bus.DeferredCpuWaitFixedImageProductionDisabled,
                _bus.DeferredCpuWaitFixedImageProductionFirstMismatch,
                _deferredCpuWaitBlitterOverlapAttempts,
                _deferredCpuWaitBlitterOverlapSupported,
                _deferredCpuWaitBlitterOverlapUnsupported,
                _deferredCpuWaitBlitterOverlapNasty,
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
                _hostBlitterTicks,
                _bus.CausalBusExecutor.AgendaReads,
                _bus.CausalBusExecutor.AgendaUpdates,
                _bus.CausalBusExecutor.ShadowMatches,
                _bus.CausalBusExecutor.ShadowMismatches,
                _bus.CausalBusExecutor.FirstShadowMismatch,
                _bus.CausalBusExecutor.FixedPlanShadowMatches,
                _bus.CausalBusExecutor.FixedPlanShadowMismatches,
                _bus.CausalBusExecutor.FirstFixedPlanShadowMismatch);
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
                    SynchronizeDiskThrough(cycle);
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
                SynchronizeBlitterThrough(cycle);
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

            if (HasSlotContendedDiskWorkThrough(cycle))
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
                SynchronizeBlitterThrough(cycle);
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

            if (HasSlotContendedDiskWorkThrough(targetCycle))
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
                SynchronizeBlitterThrough(targetCycle);
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
                    SynchronizeDiskThrough(targetCycle);
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
                (forceCatchUp
                    ? _bus.Blitter.HasAdvanceWorkThrough(targetCycle)
                    : _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle))
            {
                var start = Stopwatch.GetTimestamp();
                SynchronizeBlitterThrough(targetCycle);
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
