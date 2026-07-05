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
        private bool _cpuVisibleNoEventCacheValid;
        private long _cpuVisibleNoEventCacheTargetCycle;
        private int _cpuVisibleNoEventCacheInterruptMask;
        private long _cpuVisibleNoEventCacheHits;
        private long _cpuVisibleNoEventCacheMisses;
        private long _cpuVisibleNoEventCacheInvalidations;
        private long _copperQuiescentSlotContendedAccesses;
        private long _copperQuiescentCustomRegisterWrites;
        private long _copperQuiescentCpuScheduleAffectingCustomWrites;
        private long _copperQuiescentCpuBenignCustomWrites;
        private long _copperQuiescentCopperScheduleAffectingCustomMoves;
        private long _copperQuiescentCopperBenignCustomMoves;
        private long _copperQuiescentSchedulerDrains;
        private long _copperQuiescentShadowPredictions;
        private long _copperQuiescentShadowMatches;
        private long _copperQuiescentShadowUnsupported;
        private long _copperQuiescentShadowMismatches;
        private string _copperQuiescentFirstShadowMismatch = string.Empty;
        private long _copperQuiescentFastPathAttempts;
        private long _copperQuiescentFastPathUsed;
        private long _copperQuiescentFastPathRejectedUnsupported;
        private long _copperQuiescentFastPathRejectedInvalidated;
        private long _copperQuiescentFastPathRejectedDynamicDma;
        private long _copperQuiescentFastPathSkippedDrains;
        private long _copperQuiescentFastPathVerificationMismatches;
        private string _copperQuiescentFastPathFirstMismatch = string.Empty;
        private long _copperQuiescentFastPathInvalidatedUntilCycle = -1;
        private long _copperQuiescentWindowCacheStartCycle = -1;
        private long _copperQuiescentWindowCacheEndCycle = -1;
        private bool _copperQuiescentFastPathDisabledByVerification;

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
            _cpuVisibleNoEventCacheValid = false;
            _cpuVisibleNoEventCacheTargetCycle = 0;
            _cpuVisibleNoEventCacheInterruptMask = -1;
            _cpuVisibleNoEventCacheHits = 0;
            _cpuVisibleNoEventCacheMisses = 0;
            _cpuVisibleNoEventCacheInvalidations = 0;
            _copperQuiescentSlotContendedAccesses = 0;
            _copperQuiescentCustomRegisterWrites = 0;
            _copperQuiescentCpuScheduleAffectingCustomWrites = 0;
            _copperQuiescentCpuBenignCustomWrites = 0;
            _copperQuiescentCopperScheduleAffectingCustomMoves = 0;
            _copperQuiescentCopperBenignCustomMoves = 0;
            _copperQuiescentSchedulerDrains = 0;
            _copperQuiescentShadowPredictions = 0;
            _copperQuiescentShadowMatches = 0;
            _copperQuiescentShadowUnsupported = 0;
            _copperQuiescentShadowMismatches = 0;
            _copperQuiescentFirstShadowMismatch = string.Empty;
            _copperQuiescentFastPathAttempts = 0;
            _copperQuiescentFastPathUsed = 0;
            _copperQuiescentFastPathRejectedUnsupported = 0;
            _copperQuiescentFastPathRejectedInvalidated = 0;
            _copperQuiescentFastPathRejectedDynamicDma = 0;
            _copperQuiescentFastPathSkippedDrains = 0;
            _copperQuiescentFastPathVerificationMismatches = 0;
            _copperQuiescentFastPathFirstMismatch = string.Empty;
            _copperQuiescentFastPathInvalidatedUntilCycle = -1;
            _copperQuiescentWindowCacheStartCycle = -1;
            _copperQuiescentWindowCacheEndCycle = -1;
            _copperQuiescentFastPathDisabledByVerification = false;
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
            InvalidateCopperQuiescentWindowCache();
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
            bool isWrite,
            AmigaBusAccessSize size = AmigaBusAccessSize.Word,
            bool allowCopperQuiescentFastPath = true)
        {
            _busAccessDrainCount++;
            var inCopperQuiescentWindow = false;
            var copperQuiescentWindowEndCycle = 0L;
            if (_bus.CopperQuiescentDiagnosticsEnabled)
            {
                inCopperQuiescentWindow = RecordCopperQuiescentCpuDrain(
                    target,
                    address,
                    targetCycle,
                    isWrite,
                    out copperQuiescentWindowEndCycle);
            }

            if (allowCopperQuiescentFastPath &&
                inCopperQuiescentWindow &&
                TrySkipCopperQuiescentCpuDrain(target, targetCycle, isWrite, size, copperQuiescentWindowEndCycle))
            {
                return;
            }

            DrainTo(targetCycle, GetCpuAccessMask(target, address, isWrite));
        }

        internal void RecordCopperQuiescentCpuSlotPrediction(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long grantedCycle,
            long completedCycle,
            bool isWrite)
        {
            if (!_bus.CopperQuiescentShadowPredictionEnabled ||
                !IsCpuChipBusTarget(target) ||
                !TryGetCopperQuiescentWindow(requestedCycle, out var windowEndCycle))
            {
                return;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters ||
                size == AmigaBusAccessSize.Long ||
                completedCycle >= windowEndCycle ||
                _bus.Blitter.Busy ||
                _bus.Paula.HasDmaWorkThrough(completedCycle) ||
                _bus.Disk.ActiveDma)
            {
                _copperQuiescentShadowUnsupported++;
                return;
            }

            if (!TryPredictStaticCpuSingleSlot(
                requestedCycle,
                grantedCycle,
                completedCycle + 512,
                out var predictedGrant))
            {
                _copperQuiescentShadowUnsupported++;
                return;
            }

            _copperQuiescentShadowPredictions++;
            var predictedCompletion = predictedGrant + AgnusChipSlotScheduler.SlotCycles;
            if (predictedGrant == grantedCycle && predictedCompletion == completedCycle)
            {
                _copperQuiescentShadowMatches++;
                return;
            }

            _copperQuiescentShadowMismatches++;
            if (_copperQuiescentFirstShadowMismatch.Length == 0)
            {
                _copperQuiescentFirstShadowMismatch =
                    $"{kind}/{target}/{size}/write={isWrite}/addr=0x{address:X6}/req={requestedCycle}/pred={predictedGrant}->{predictedCompletion}/actual={grantedCycle}->{completedCycle}";
            }

            if (_bus.CopperQuiescentFastPathVerifyEnabled)
            {
                _copperQuiescentFastPathVerificationMismatches++;
                _copperQuiescentFastPathDisabledByVerification = true;
                if (_copperQuiescentFastPathFirstMismatch.Length == 0)
                {
                    _copperQuiescentFastPathFirstMismatch = _copperQuiescentFirstShadowMismatch;
                }
            }
        }

        private bool RecordCopperQuiescentCpuDrain(
            AmigaBusAccessTarget target,
            uint address,
            long targetCycle,
            bool isWrite,
            out long windowEndCycle)
        {
            windowEndCycle = 0;
            if (!IsCpuChipBusTarget(target) ||
                !TryGetCopperQuiescentWindow(targetCycle, out windowEndCycle))
            {
                return false;
            }

            _copperQuiescentSchedulerDrains++;
            if (target == AmigaBusAccessTarget.CustomRegisters)
            {
                return true;
            }

            _copperQuiescentSlotContendedAccesses++;
            return true;
        }

        internal void RecordCopperQuiescentCustomRegisterWrite(
            AmigaBusRequester requester,
            ushort offset,
            long cycle,
            bool scheduleAffecting)
        {
            if (!_bus.CopperQuiescentDiagnosticsEnabled ||
                requester == AmigaBusRequester.Host)
            {
                return;
            }

            if (!TryGetCopperQuiescentWindow(cycle, out var endCycle))
            {
                return;
            }

            _copperQuiescentCustomRegisterWrites++;
            if (scheduleAffecting)
            {
                if (requester == AmigaBusRequester.Copper)
                {
                    _copperQuiescentCopperScheduleAffectingCustomMoves++;
                }
                else
                {
                    _copperQuiescentCpuScheduleAffectingCustomWrites++;
                }

                _copperQuiescentFastPathInvalidatedUntilCycle = Math.Max(
                    _copperQuiescentFastPathInvalidatedUntilCycle,
                    endCycle);
            }
            else
            {
                if (requester == AmigaBusRequester.Copper)
                {
                    _copperQuiescentCopperBenignCustomMoves++;
                }
                else
                {
                    _copperQuiescentCpuBenignCustomWrites++;
                }
            }
        }

        private bool TrySkipCopperQuiescentCpuDrain(
            AmigaBusAccessTarget target,
            long targetCycle,
            bool isWrite,
            AmigaBusAccessSize size,
            long windowEndCycle)
        {
            if (!_bus.CopperQuiescentFastPathEnabled ||
                _copperQuiescentFastPathDisabledByVerification)
            {
                return false;
            }

            _copperQuiescentFastPathAttempts++;
            if (targetCycle >= _copperQuiescentFastPathInvalidatedUntilCycle)
            {
                _copperQuiescentFastPathInvalidatedUntilCycle = -1;
            }

            if (_copperQuiescentFastPathInvalidatedUntilCycle > targetCycle)
            {
                _copperQuiescentFastPathRejectedInvalidated++;
                return false;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters ||
                size == AmigaBusAccessSize.Long)
            {
                _copperQuiescentFastPathRejectedUnsupported++;
                return false;
            }

            if (_bus.Blitter.Busy ||
                _bus.Disk.ActiveDma)
            {
                _copperQuiescentFastPathRejectedDynamicDma++;
                return false;
            }

            if (!TryPredictStaticCpuSingleSlot(targetCycle, actualGrantCycle: null, targetCycle + 512, out var predictedGrant))
            {
                _copperQuiescentFastPathRejectedUnsupported++;
                return false;
            }

            var predictedCompletion = predictedGrant + AgnusChipSlotScheduler.SlotCycles;
            if (predictedCompletion >= windowEndCycle ||
                _bus.Paula.HasDmaWorkThrough(predictedCompletion))
            {
                _copperQuiescentFastPathRejectedDynamicDma++;
                return false;
            }

            _copperQuiescentFastPathUsed++;
            _copperQuiescentFastPathSkippedDrains++;
            return true;
        }

        private bool TryPredictStaticCpuSingleSlot(
            long requestedCycle,
            long? actualGrantCycle,
            long limitCycle,
            out long predictedGrant)
        {
            predictedGrant = 0;
            var candidate = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle));
            if (!AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(candidate))
            {
                candidate += AgnusChipSlotScheduler.SlotCycles;
            }

            var limit = Math.Max(limitCycle, candidate);
            while (candidate <= limit)
            {
                if ((actualGrantCycle.HasValue && candidate == actualGrantCycle.Value) ||
                    !_bus.IsHrmChipSlotReserved(candidate))
                {
                    predictedGrant = candidate;
                    return true;
                }

                candidate += 2 * AgnusChipSlotScheduler.SlotCycles;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetCopperQuiescentWindow(long cycle, out long endCycle)
        {
            if (cycle >= _copperQuiescentWindowCacheStartCycle &&
                cycle < _copperQuiescentWindowCacheEndCycle)
            {
                endCycle = _copperQuiescentWindowCacheEndCycle;
                return true;
            }

            if (_bus.Display.TryGetCopperQuiescentWindow(cycle, out var startCycle, out endCycle))
            {
                _copperQuiescentWindowCacheStartCycle = startCycle;
                _copperQuiescentWindowCacheEndCycle = endCycle;
                return true;
            }

            InvalidateCopperQuiescentWindowCache();
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InvalidateCopperQuiescentWindowCache()
        {
            _copperQuiescentWindowCacheStartCycle = -1;
            _copperQuiescentWindowCacheEndCycle = -1;
        }

        private static bool IsCpuChipBusTarget(AmigaBusAccessTarget target)
            => target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock ||
                target == AmigaBusAccessTarget.CustomRegisters;

        public long GetNextCpuVisibleEventCycle(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out M68kTraceBatchWakeSource wakeSource)
            => GetNextCpuVisibleEventCycle(
                currentCycle,
                targetCycle,
                cpuInterruptMask,
                out wakeSource,
                out _);

        public long GetNextCpuVisibleEventCycle(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out M68kTraceBatchWakeSource wakeSource,
            out AmigaDiskController.SchedulerWakeReason diskWakeReason)
        {
            wakeSource = M68kTraceBatchWakeSource.TargetCycle;
            diskWakeReason = AmigaDiskController.SchedulerWakeReason.None;
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return currentCycle;
            }

            if (TryGetCachedCpuVisibleNoEventTarget(
                currentCycle,
                targetCycle,
                cpuInterruptMask,
                out var cachedTargetCycle))
            {
                return cachedTargetCycle;
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
            var diskCandidate = _bus.Disk.GetNextCpuVisibleWakeCandidateCycle(
                currentCycle,
                targetCycle,
                cpuInterruptMask,
                out var candidateDiskWakeReason);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                diskCandidate,
                M68kTraceBatchWakeSource.Disk,
                ref wakeSource);
            if (wakeSource == M68kTraceBatchWakeSource.Disk)
            {
                diskWakeReason = candidateDiskWakeReason;
            }

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
                _bus.Display.GetNextLiveCopperCpuBatchBarrierCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Copper,
                ref wakeSource);
            candidate = MinWakeCandidate(
                candidate,
                currentCycle,
                targetCycle,
                _bus.Blitter.GetNextWakeCandidateCycle(currentCycle, targetCycle),
                M68kTraceBatchWakeSource.Blitter,
                ref wakeSource);
            if (wakeSource != M68kTraceBatchWakeSource.Disk)
            {
                diskWakeReason = AmigaDiskController.SchedulerWakeReason.None;
            }

            candidate = Math.Clamp(candidate, currentCycle + 1, targetCycle);
            if (wakeSource == M68kTraceBatchWakeSource.TargetCycle &&
                candidate == targetCycle)
            {
                CacheCpuVisibleNoEventTarget(targetCycle, cpuInterruptMask);
            }

            return candidate;
        }

        private bool TryGetCachedCpuVisibleNoEventTarget(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out long cachedTargetCycle)
        {
            cachedTargetCycle = targetCycle;
            if (!_cpuVisibleNoEventCacheValid ||
                _cpuVisibleNoEventCacheTargetCycle != targetCycle ||
                _cpuVisibleNoEventCacheInterruptMask != cpuInterruptMask)
            {
                _cpuVisibleNoEventCacheMisses++;
                return false;
            }

            var pendingPaulaInterruptLevel = _bus.Paula.GetHighestCpuVisibleInterruptLevel(currentCycle);
            var pendingPaulaInterruptCanEnter = pendingPaulaInterruptLevel > 0 &&
                (cpuInterruptMask < 0 || pendingPaulaInterruptLevel > (cpuInterruptMask & 0x07));
            if (_bus.HasPendingCiaInterrupts || pendingPaulaInterruptCanEnter)
            {
                InvalidateCpuVisibleNoEventCache();
                _cpuVisibleNoEventCacheMisses++;
                return false;
            }

            _cpuVisibleNoEventCacheHits++;
            return true;
        }

        private void CacheCpuVisibleNoEventTarget(long targetCycle, int cpuInterruptMask)
        {
            _cpuVisibleNoEventCacheValid = true;
            _cpuVisibleNoEventCacheTargetCycle = targetCycle;
            _cpuVisibleNoEventCacheInterruptMask = cpuInterruptMask;
        }

        private void InvalidateCpuVisibleNoEventCache()
        {
            if (_cpuVisibleNoEventCacheValid)
            {
                _cpuVisibleNoEventCacheInvalidations++;
            }

            _cpuVisibleNoEventCacheValid = false;
        }
    }
}
