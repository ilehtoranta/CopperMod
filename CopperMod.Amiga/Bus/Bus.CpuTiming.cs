/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        // Reuses the prepared display-owner segment while a CPU grant search walks
        // forward within the same raster line. The cursor self-invalidates when a
        // Copper/custom-register wake changes the live display generation.
        private BlitterFixedSlotImageCursor _cpuGrantFixedSlotImageCursor;
        // A positive dynamic-DMA rejection is stable for the remainder of the line.
        // Cache only that conservative result; a negative result must be rechecked.
        private long _cpuGrantDynamicDmaRejectSegmentEnd = -1;

        private const int DeferredCpuBusBatchMinimumHorizonCycles = 16;
        // The CPU wait-slot executor remains shadow-only until its display and
        // scheduler state transitions are proven equivalent to the reference drain.
        private const bool DeferredCpuWaitFastPathEnabled = true;

        internal void ArmDeferredCpuBatchExitForTest(long cycle)
            => _deferredCpuBatchExitChipAccessCycle = Math.Max(0, cycle);

        internal void SetCpuWaitSlotContendedCleanThroughForTest(long cycle)
            => _hardwareScheduler.SetCpuWaitSlotContendedCleanThroughForTest(cycle);

        void IM68kDeferredCpuInstructionTiming.BeginDeferredCpuInstructionTiming(long cycle)
        {
            if (_deferredCpuBusBatchActive)
            {
                _deferredCpuInstructionTimingActive = !_captureBusAccesses;
                if (_deferredCpuDataAccessCount == 0)
                {
                    _deferredCpuDataReplayCycle = Math.Max(
                        _deferredCpuDataReplayCycle,
                        cycle);
                }

                return;
            }

            _deferredCpuInstructionTimingActive = !_captureBusAccesses;
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;
            _deferredCpuDataInstructionFetchShapeBits = 0;
            _deferredCpuDataRomShapeBits = 0;
            _deferredCpuDataReplayCycle = cycle;
        }

        void IM68kDeferredCpuInstructionTiming.FlushDeferredCpuInstructionTiming(ref long cycle)
        {
            FlushDeferredCpuDataTiming(ref cycle);
            if (_deferredCpuBusBatchActive && !_endingDeferredCpuBusBatch)
            {
                EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return;
            }

            _deferredCpuInstructionTimingActive = false;
        }

        bool IM68kDeferredCpuInstructionTiming.IsDeferredCpuBusBatchActive => _deferredCpuBusBatchActive;

        bool IM68kDeferredCpuInstructionTiming.IsInternalNoBusWindowEnabled =>
            _deferredCpuInternalNoBusWindowEnabled && !_captureBusAccesses;

        bool IM68kDeferredCpuInstructionTiming.IsDeferredCpuBusBatchEligibleInstructionFetchWindow(
            in M68kInstructionFetchWindow window)
            => IsDeferredCpuBusBatchEligibleTarget((AmigaBusAccessTarget)window.BusTag);

        ulong IM68kDeferredCpuInstructionTiming.CaptureDeferredCpuInstructionFetchTimingToken()
            => _lastDeferredCpuInstructionFetchTimingToken;

        bool IM68kDeferredCpuInstructionTiming.TryBeginDeferredCpuBusBatch(
            M68kCpuState state,
            long currentCycle,
            long? targetCycle,
            out long batchTargetCycle,
            out M68kTraceBatchWakeSource wakeSource)
        {
            batchTargetCycle = currentCycle;
            wakeSource = M68kTraceBatchWakeSource.TargetCycle;
            if (_captureBusAccesses ||
                _deferredCpuBusBatchActive)
            {
                return false;
            }

            if (!Blitter.Busy &&
                currentCycle < _agnusBusExecutor.ExecutedThroughCycle)
            {
                currentCycle = _agnusBusExecutor.ExecutedThroughCycle;
                state.Cycles = currentCycle;
            }

            if (!_deferredCpuBusBatchEnabled)
            {
                if (_agnusBusExecutor.CpuVisibilityShadowEnabled)
                {
                    currentCycle = Math.Max(0, currentCycle);
                    var diagnosticTarget = targetCycle.HasValue
                        ? Math.Max(currentCycle + 1, targetCycle.Value)
                        : currentCycle + DeferredCpuBusBatchDefaultCycleWindow;
                    var diagnosticInterruptMask = (state.StatusRegister >> 8) & 0x07;
                    _ = GetNextCpuBatchWakeCandidateCycle(
                        currentCycle,
                        diagnosticTarget,
                        diagnosticInterruptMask,
                        out _,
                        out _);
                }

                return false;
            }

            _deferredCpuBusBatchAttempts++;
            currentCycle = Math.Max(0, currentCycle);
            var requestedTarget = targetCycle.HasValue
                ? Math.Max(currentCycle + 1, targetCycle.Value)
                : currentCycle + DeferredCpuBusBatchDefaultCycleWindow;
            var interruptMask = (state.StatusRegister >> 8) & 0x07;
            var horizon = _agnusBusExecutor.GetNextCpuVisibilityHorizon(
                currentCycle,
                requestedTarget,
                interruptMask);
            batchTargetCycle = horizon.Cycle;
            wakeSource = AgnusBusExecutor.MapLegacyReason(horizon.Reason);
            var diskWakeReason = horizon.DiskReason;
            if (batchTargetCycle - currentCycle < DeferredCpuBusBatchMinimumHorizonCycles)
            {
                return false;
            }

            _deferredCpuBusBatchActive = true;
            _deferredCpuBusBatchHasTargetCycle = targetCycle.HasValue;
            _deferredCpuBusBatchTargetCycle = batchTargetCycle;
            _deferredCpuInstructionTimingActive = !_captureBusAccesses;
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;
            _deferredCpuDataInstructionFetchShapeBits = 0;
            _deferredCpuDataRomShapeBits = 0;
            _deferredCpuDataReplayCycle = currentCycle;
            _deferredCpuProjectedRetireDelay = 0;
            _deferredCpuProjectedMaxRetireDelay = 0;
            _deferredCpuProjectedMaxRetireVirtualCycle = -1;
            _deferredCpuProjectedMaxRetireDependencyCompletedCycle = -1;
            _deferredCpuProjectedMaxRetireDependencyToken = 0;
            _deferredCpuProjectedMaxRetireJournalCount = 0;
            _deferredCpuProjectedLastVirtualCycle = -1;
            _deferredCpuProjectedLastDependencyCompletedCycle = -1;
            _deferredCpuProjectedLastDependencyToken = 0;
            _deferredCpuProjectedLastJournalCount = 0;
            _deferredCpuProjectedLastEntryCount = 0;
            _deferredCpuProjectedLastRejectReason = 0;
            _deferredCpuProjectedLastCpuEventCount = 0;
            _deferredCpuProjectedLastFailedIndex = -1;
            _deferredCpuProjectedLastFailedRequestedCycle = -1;
            _deferredCpuProjectedLastUnsupported = 0;
            _deferredCpuProjectedEntryCount = 0;
            _deferredCpuProjectedCompletedCycle = -1;
            _deferredCpuInstructionPublicationPhaseSlip = 0;
            _deferredCpuInstructionPublicationPhaseSlipGroup = 0;
            _deferredCpuInstructionPublicationPhaseSlipToken = 0;
            _deferredCpuInstructionPublicationPhaseSlipVirtualReadyCycle = -1;
            _deferredCpuInstructionPublicationPhaseSlipActualReadyCycle = -1;
            _deferredCpuInstructionPublicationPhaseSlipContext = default;
            _deferredCpuRetirementPublicationShadowFirstCaptured = false;
            _deferredCpuTrimmedPendingTransitionActive = false;
            _deferredCpuIrcPairShadowLastToken = 0;
            _deferredCpuIrcPairShadowLastReadyCycle = -1;
            _deferredCpuIrcPairShadowEntries = 0;
            _deferredCpuIrcPairShadowFirstPredecessorReadyCycle = -1;
            _deferredCpuIrcPairShadowFirstPredictedReadyCycle = -1;
            _deferredCpuIrcPairShadowPredecessorReadyCycle = -1;
            _deferredCpuIrcPairShadowPredictedReadyCycle = -1;
            _agnusBusExecutor.ClearUnresolvedCpuTiming();
            _deferredCpuBusBatchExecutionStarted = false;
            _deferredCpuBusBatchPendingWakeSource = wakeSource;
            _deferredCpuBusBatchPendingDiskWakeReason = diskWakeReason;

            return true;
        }

        void IM68kDeferredCpuInstructionTiming.BeginDeferredCpuBusBatchExecution()
        {
            if (!_deferredCpuBusBatchExecutionStarted)
            {
                _deferredCpuBusBatchExecutionStarted = true;
                _deferredCpuBusBatchUsed++;
                RecordDeferredCpuBusBatchWakeSource(_deferredCpuBusBatchPendingWakeSource);
                if (_deferredCpuBusBatchPendingWakeSource == M68kTraceBatchWakeSource.Disk)
                {
                    RecordDeferredCpuBusBatchDiskWakeReason(_deferredCpuBusBatchPendingDiskWakeReason);
                }
            }
        }

        void IM68kDeferredCpuInstructionTiming.CompleteDeferredCpuBusBatchExecution(
            int instructionCount,
            int skippedInstructionFlushCount)
        {
            _deferredCpuBusBatchInstructions += instructionCount;
            _deferredCpuBusBatchSkippedInstructionFlushes += skippedInstructionFlushCount;
        }

        bool IM68kDeferredCpuInstructionTiming.CanContinueDeferredCpuBusBatch(
            long virtualRetireCycle,
            long batchTargetCycle,
            ulong timingDependencyToken,
            out long projectedRetireCycle)
        {
            projectedRetireCycle = virtualRetireCycle;
            _deferredCpuProjectedLastVirtualCycle = virtualRetireCycle;
            _deferredCpuProjectedLastDependencyCompletedCycle = -2;
            _deferredCpuProjectedLastDependencyToken = timingDependencyToken;
            _deferredCpuProjectedLastJournalCount = _deferredCpuDataAccessCount;
            _deferredCpuProjectedLastEntryCount = _deferredCpuProjectedEntryCount;
            _deferredCpuProjectedLastRejectReason = 0;
            _deferredCpuProjectedLastCpuEventCount =
                _agnusBusExecutor.CpuEventJournal.Count;
            _deferredCpuProjectedLastFailedIndex = -1;
            _deferredCpuProjectedLastFailedRequestedCycle = -1;
            _deferredCpuProjectedLastUnsupported = 0;
            if (!_deferredCpuBusBatchActive ||
                _agnusBusExecutor.CpuEventJournal.Count != 0)
            {
                return false;
            }

            var count = _deferredCpuDataAccessCount;
            if (count == 0)
            {
                return virtualRetireCycle < batchTargetCycle;
            }

            // Leave enough room for the largest supported 68000 instruction
            // shape. Journal overflow from inside the next instruction would
            // otherwise force a non-clean batch exit before its cancellable
            // prefetch suffix can be classified.
            if (count >= MaxDeferredCpuDataAccesses - 8)
            {
                return false;
            }

            if (!TryProjectDeferredCpuTimingContinuation(
                    count,
                    _deferredCpuDataLongShapeBits,
                    _deferredCpuDataCiaShapeBits,
                    _deferredCpuDataRomShapeBits,
                    _deferredCpuDataInstructionFetchShapeBits,
                    timingDependencyToken,
                    out var dependencyCompletedCycle))
            {
                return false;
            }

            _deferredCpuProjectedLastDependencyCompletedCycle =
                dependencyCompletedCycle;

            projectedRetireCycle = virtualRetireCycle;
            if (dependencyCompletedCycle >= 0)
            {
                projectedRetireCycle = Math.Max(projectedRetireCycle, dependencyCompletedCycle);
            }
            _deferredCpuProjectedRetireDelay = projectedRetireCycle - virtualRetireCycle;
            if (_deferredCpuProjectedRetireDelay > _deferredCpuProjectedMaxRetireDelay)
            {
                _deferredCpuProjectedMaxRetireDelay = _deferredCpuProjectedRetireDelay;
                _deferredCpuProjectedMaxRetireVirtualCycle = virtualRetireCycle;
                _deferredCpuProjectedMaxRetireDependencyCompletedCycle =
                    dependencyCompletedCycle;
                _deferredCpuProjectedMaxRetireDependencyToken = timingDependencyToken;
                _deferredCpuProjectedMaxRetireJournalCount = count;
            }
            return projectedRetireCycle < batchTargetCycle;
        }

        bool IM68kDeferredCpuInstructionTiming.TryTrimDeferredCpuInstructionFetchSuffix(
            ulong requiredThroughToken,
            out M68kDeferredCpuTrimmedInstructionFetch trimmedFetch)
        {
            trimmedFetch = default;
            var count = _deferredCpuDataAccessCount;
            var firstTrimmedIndex = count;
            while (firstTrimmedIndex > 0 &&
                _deferredCpuDataTimingTokens[firstTrimmedIndex - 1] > requiredThroughToken)
            {
                var candidateIndex = firstTrimmedIndex - 1;
                var candidatePhase =
                    _deferredCpuDataInstructionFetchPublicationPhases[candidateIndex];
                if (candidatePhase == M68kInstructionFetchPublicationPhase.Required)
                {
                    var candidateContext =
                        _deferredCpuDataInstructionFetchPublicationContexts[candidateIndex];
                    var isIdentityBoundIrc = candidateContext.RequiresIrcGap;
                    if (isIdentityBoundIrc)
                    {
                        break;
                    }
                }

                if (candidatePhase is
                    M68kInstructionFetchPublicationPhase.RetirementQueue or
                    M68kInstructionFetchPublicationPhase.CancellableSuccessor)
                {
                    var nominalCycle = _deferredCpuDataRequestedCycles[candidateIndex] +
                        _deferredCpuDataRequestedRetireDelays[candidateIndex];
                    var translatedCycle = nominalCycle +
                        CalculateDeferredCpuPublicationResidualSlip(
                            candidateIndex,
                            nominalCycle,
                            _deferredCpuInstructionPublicationPhaseSlip);
                    _deferredCpuRetirementPublicationShadowEntries++;
                    _deferredCpuRetirementPublicationShadowLastNominalCycle = nominalCycle;
                    _deferredCpuRetirementPublicationShadowLastFloor =
                        _deferredCpuDataInstructionFetchRetirementFloors[candidateIndex];
                    _deferredCpuRetirementPublicationShadowLastInheritedDelay =
                        _deferredCpuInstructionPublicationPhaseSlip;
                    _deferredCpuRetirementPublicationShadowLastTranslatedCycle = translatedCycle;
                    _deferredCpuRetirementPublicationShadowLastPhase = candidatePhase;
                    _deferredCpuRetirementPublicationShadowLastGroup =
                        _deferredCpuDataInstructionFetchPublicationContexts[candidateIndex].Group;
                    RecordDeferredCpuPublicationShadowContext(
                        candidateIndex,
                        nominalCycle,
                        _deferredCpuDataInstructionFetchRetirementFloors[candidateIndex],
                        _deferredCpuInstructionPublicationPhaseSlip,
                        wasReplay: false);
                    if (TryPredictCpuWaitFixedSlotGrant(
                            AmigaBusAccessKind.CpuInstructionFetch,
                            AmigaBusAccessTarget.ExpansionRam,
                            ExpansionRamBase,
                            AmigaBusAccessSize.Word,
                            nominalCycle,
                            isWrite: false,
                            out var legacyGrant,
                            out _,
                            out _))
                    {
                        _deferredCpuRetirementPublicationShadowLastLegacyReadyCycle = legacyGrant;
                    }
                }

                firstTrimmedIndex--;
            }

            if (firstTrimmedIndex == count)
            {
                return false;
            }

            for (var index = firstTrimmedIndex; index < count; index++)
            {
                var mask = 1UL << index;
                if ((_deferredCpuDataInstructionFetchShapeBits & mask) == 0 ||
                    (_deferredCpuDataCiaShapeBits & mask) != 0 ||
                    (_deferredCpuDataLongShapeBits & mask) != 0)
                {
                    return false;
                }
            }

            var trimmedVirtualRequestedCycle =
                _deferredCpuDataRequestedCycles[firstTrimmedIndex] +
                    _deferredCpuDataRequestedRetireDelays[firstTrimmedIndex];
            trimmedFetch = new M68kDeferredCpuTrimmedInstructionFetch(
                _deferredCpuDataTimingTokens[firstTrimmedIndex],
                _deferredCpuDataInstructionFetchPublicationPhases[firstTrimmedIndex],
                _deferredCpuDataInstructionFetchRetirementFloors[firstTrimmedIndex],
                _deferredCpuDataInstructionFetchPublicationContexts[firstTrimmedIndex],
                trimmedVirtualRequestedCycle);
            _deferredCpuTrimmedPendingTransitionActive = true;
            _deferredCpuTrimmedPendingOriginalCycle = trimmedVirtualRequestedCycle;
            _deferredCpuDataAccessCount = firstTrimmedIndex;
            var retainedMask = firstTrimmedIndex == 0
                ? 0UL
                : (1UL << firstTrimmedIndex) - 1UL;
            _deferredCpuDataLongShapeBits &= retainedMask;
            _deferredCpuDataCiaShapeBits &= retainedMask;
            _deferredCpuDataInstructionFetchShapeBits &= retainedMask;
            _deferredCpuDataRomShapeBits &= retainedMask;
            if (_deferredCpuProjectedEntryCount > firstTrimmedIndex)
            {
                _deferredCpuProjectedEntryCount = firstTrimmedIndex;
            }
            RefreshDeferredCpuTimingFence();
            return true;
        }

        private void RefreshDeferredCpuTimingFence()
        {
            _agnusBusExecutor.ClearUnresolvedCpuTiming();
            if (_deferredCpuDataAccessCount == 0)
            {
                return;
            }

            var firstRequestedCycle =
                _deferredCpuDataRequestedCycles[0] +
                _deferredCpuDataRequestedRetireDelays[0];
            var executedThroughCycle = _agnusBusExecutor.ExecutedThroughCycle;
            if (firstRequestedCycle < executedThroughCycle)
            {
                // A chip-visible operation can flush a preceding journal while
                // its CPU-side caller is still on the speculative timeline.
                // The retained fetches have not been committed, so preserve
                // their relative order and move the whole journal forward to
                // the physical horizon before publishing the replacement fence.
                var rebaseCycles = executedThroughCycle - firstRequestedCycle;
                for (var index = 0; index < _deferredCpuDataAccessCount; index++)
                {
                    _deferredCpuDataRequestedCycles[index] += rebaseCycles;
                }

                _deferredCpuDataReplayCycle = Math.Max(
                    _deferredCpuDataReplayCycle,
                    executedThroughCycle);
                firstRequestedCycle = executedThroughCycle;
            }

            _agnusBusExecutor.PublishUnresolvedCpuTiming(
                firstRequestedCycle);
        }

        // A flush replays the journal through the physical bus and can advance
        // Agnus beyond the CPU's speculative cycle. A new journal must start
        // from that horizon, otherwise a later suffix trim can republish its
        // fence behind already-executed DMA.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeDeferredCpuJournalStart(ref long cycle)
        {
            if (_deferredCpuDataAccessCount == 0 &&
                cycle < _agnusBusExecutor.ExecutedThroughCycle)
            {
                cycle = _agnusBusExecutor.ExecutedThroughCycle;
            }
        }

        private bool TryProjectDeferredCpuTimingContinuation(
            int count,
            ulong longShapeBits,
            ulong ciaShapeBits,
            ulong romShapeBits,
            ulong instructionFetchShapeBits,
            ulong dependencyToken,
            out long dependencyCompletedCycle)
        {
            dependencyCompletedCycle = -1;
            if (count < _deferredCpuProjectedEntryCount ||
                longShapeBits != 0 ||
                ciaShapeBits != 0 ||
                romShapeBits != 0)
            {
                _deferredCpuProjectedLastRejectReason =
                    count < _deferredCpuProjectedEntryCount ? 1 :
                    longShapeBits != 0 ? 2 :
                    ciaShapeBits != 0 ? 3 : 4;
                return false;
            }

            for (var index = _deferredCpuProjectedEntryCount; index < count; index++)
            {
                var requestedCycle = _deferredCpuDataRequestedCycles[index] +
                    _deferredCpuDataRequestedRetireDelays[index];
                if (_deferredCpuProjectedCompletedCycle >= 0)
                {
                    requestedCycle = Math.Max(
                        requestedCycle,
                        _deferredCpuProjectedCompletedCycle);
                }

                var publicationContext =
                    _deferredCpuDataInstructionFetchPublicationContexts[index];
                if (publicationContext.RequiresIrcGap &&
                    publicationContext.PredecessorToken != 0)
                {
                    for (var predecessorIndex = index - 1;
                        predecessorIndex >= 0;
                        predecessorIndex--)
                    {
                        if (_deferredCpuDataTimingTokens[predecessorIndex] !=
                            publicationContext.PredecessorToken)
                        {
                            continue;
                        }

                        requestedCycle = Math.Max(
                            requestedCycle,
                            _deferredCpuProjectedEntryGrantCycles[predecessorIndex] + 4);
                        break;
                    }
                }

                var kind = ((instructionFetchShapeBits >> index) & 1UL) != 0
                    ? AmigaBusAccessKind.CpuInstructionFetch
                    : AmigaBusAccessKind.CpuDataRead;
                if (!TryPredictCpuWaitFixedSlotGrant(
                        kind,
                        AmigaBusAccessTarget.ExpansionRam,
                        ExpansionRamBase,
                        AmigaBusAccessSize.Word,
                        requestedCycle,
                        isWrite: false,
                        out var grantedCycle,
                        out var completedCycle,
                        out var unsupported))
                {
                    _deferredCpuProjectedLastRejectReason = 5;
                    _deferredCpuProjectedLastFailedIndex = index;
                    _deferredCpuProjectedLastFailedRequestedCycle = requestedCycle;
                    _deferredCpuProjectedLastUnsupported = (int)unsupported;
                    return false;
                }


                _deferredCpuProjectedCompletedCycle = completedCycle;
                _deferredCpuProjectedEntryGrantCycles[index] = grantedCycle;
                _deferredCpuProjectedEntryCompletedCycles[index] = completedCycle;
                _deferredCpuProjectedEntryCount = index + 1;
            }

            if (dependencyToken != 0)
            {
                for (var index = 0; index < _deferredCpuProjectedEntryCount; index++)
                {
                    if (_deferredCpuDataTimingTokens[index] == dependencyToken)
                    {
                        dependencyCompletedCycle =
                            _deferredCpuProjectedEntryCompletedCycles[index];
                        break;
                    }
                }
            }

            return true;
        }

        M68kDeferredCpuBusCheckpoint IM68kDeferredCpuInstructionTiming.SynchronizeDeferredCpuBusBatchTiming(
            ref long cycle,
            in M68kDeferredCpuBusCheckpointRequest request)
        {
            var hadDeferredEntries = _deferredCpuDataAccessCount != 0;
            FlushDeferredCpuDataTiming(
                ref cycle,
                keepBatchActive: true,
                in request,
                out var checkpoint);
            cycle = checkpoint.ArchitecturalRetireCycle;
            if (hadDeferredEntries)
            {
                _previousDeferredCpuBusCheckpoint = _lastDeferredCpuBusCheckpoint;
                _lastDeferredCpuBusCheckpoint = checkpoint;
            }
            return checkpoint;
        }

        void IM68kDeferredCpuInstructionTiming.EndDeferredCpuBusBatch(ref long cycle, M68kDeferredCpuBusBatchExitReason reason)
            => EndDeferredCpuBusBatchCore(ref cycle, reason);

        bool IM68kDeferredCpuInstructionTiming.TryAdvanceInternalNoBusWindow(
            M68kCpuState state,
            long currentCycle,
            int cycles,
            M68kInternalNoBusWindowKind kind)
        {
            if (!_deferredCpuInternalNoBusWindowEnabled ||
                _captureBusAccesses ||
                cycles <= 0)
            {
                return false;
            }

            _deferredCpuInternalNoBusWindowAttempts++;
            currentCycle = Math.Max(0, currentCycle);
            RecordDeferredCpuInternalNoBusWindow(
                cycles,
                cycles,
                M68kTraceBatchWakeSource.TargetCycle,
                wakeLimited: false,
                kind);
            return true;
        }

        private void RecordDeferredCpuInternalNoBusWindow(
            int cycles,
            long advancedCycles,
            M68kTraceBatchWakeSource wakeSource,
            bool wakeLimited,
            M68kInternalNoBusWindowKind kind)
        {
            _deferredCpuInternalNoBusWindowUsed++;
            _deferredCpuInternalNoBusWindowTotalCycles += cycles;
            _deferredCpuInternalNoBusWindowAdvancedCycles += advancedCycles;
            switch (kind)
            {
                case M68kInternalNoBusWindowKind.Multiply:
                    _deferredCpuInternalNoBusWindowMultiply++;
                    break;
                case M68kInternalNoBusWindowKind.Divide:
                    _deferredCpuInternalNoBusWindowDivide++;
                    break;
            }

            if (wakeLimited)
            {
                RecordDeferredCpuInternalNoBusWindowWakeSource(wakeSource);
            }
            else
            {
                _deferredCpuInternalNoBusWindowWakeTargetCycle++;
            }
        }

        private static bool IsDeferredCpuBusBatchEligibleTarget(AmigaBusAccessTarget target)
            => target == AmigaBusAccessTarget.Rom ||
                target is AmigaBusAccessTarget.RealFastRam or AmigaBusAccessTarget.RtgVram ||
                target == AmigaBusAccessTarget.ExpansionRam;

        private bool CanKeepDeferredCpuBusBatchForAccess(
            AmigaBusAccessTarget target,
            AmigaBusAccessKind kind,
            bool isWrite,
            long cycle)
        {
            if (!_deferredCpuBusBatchActive)
            {
                return false;
            }

            if (isWrite)
            {
                return target is AmigaBusAccessTarget.RealFastRam or AmigaBusAccessTarget.RtgVram ||
                    target == AmigaBusAccessTarget.ExpansionRam;
            }

            if (_deferredCpuChipReadSegmentsEnabled &&
                target == AmigaBusAccessTarget.ChipRam &&
                kind == AmigaBusAccessKind.CpuDataRead &&
                !_agnusBusExecutor.MayWriteChipRamBefore(
                    Math.Max(cycle, _deferredCpuBusBatchTargetCycle)))
            {
                return true;
            }

            return target == AmigaBusAccessTarget.Rom ||
                target is AmigaBusAccessTarget.RealFastRam or AmigaBusAccessTarget.RtgVram ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                (kind == AmigaBusAccessKind.CpuInstructionFetch &&
                    target == AmigaBusAccessTarget.HostTrap);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryJournalDeferredCpuChipWordWrite(
            uint address,
            ushort value,
            ref long cycle)
        {
            if (!_deferredCpuChipWriteJournalEnabled ||
                !_deferredCpuBusBatchActive ||
                _agnusBusExecutor.IsFlushingCpuEventJournal)
            {
                return false;
            }

            if (_agnusBusExecutor.HasPendingCpuWriteOverlap(address, 2))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            if (!_agnusBusExecutor.TryEnqueueCpuChipWordWrite(
                address,
                value,
                cycle,
                CpuJournalInstructionPhase.Operand,
                CpuJournalDependencyFlags.None,
                out _))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            cycle += AgnusChipSlotScheduler.SlotCycles;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryJournalDeferredCpuChipLongWrite(
            uint address,
            uint value,
            ref long cycle)
        {
            if (!_deferredCpuChipWriteJournalEnabled ||
                !_deferredCpuBusBatchActive ||
                _agnusBusExecutor.IsFlushingCpuEventJournal)
            {
                return false;
            }

            if (_agnusBusExecutor.HasPendingCpuWriteOverlap(address, 4))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            if (!_agnusBusExecutor.TryEnqueueCpuChipLongWrite(
                address,
                value,
                cycle,
                CpuJournalInstructionPhase.Operand,
                out _,
                out _))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            cycle += 2 * AgnusChipSlotScheduler.SlotCycles;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryJournalDeferredCpuCustomWordWrite(
            uint address,
            ushort value,
            ref long cycle)
        {
            if ((!_deferredCpuCustomPointerWritesEnabled &&
                    !_deferredCpuCustomCompositionWritesEnabled) ||
                !_deferredCpuBusBatchActive ||
                _agnusBusExecutor.IsFlushingCpuEventJournal ||
                address < CustomRegisterBaseAddress ||
                address >= CustomRegisterBaseAddress + 0x200u)
            {
                return false;
            }

            var offset = (ushort)(address - CustomRegisterBaseAddress);
            var classification = CpuDeferredCustomAccessClassifier.ClassifyCustom(
                    _chipset,
                    offset,
                    isWrite: true);
            var enabled =
                (_deferredCpuCustomPointerWritesEnabled &&
                    CpuDeferredCustomAccessClassifier.IsDmaPointerRegister(offset)) ||
                (_deferredCpuCustomCompositionWritesEnabled &&
                    CustomRegisterScheduleClassifier.IsColorRegister(offset));
            if (classification != CpuDeferredPeripheralAccess.JournalableWrite || !enabled)
            {
                return false;
            }

            // Publish all bus activity preceding this MOVE before the value
            // event is enqueued. The timing and value journals are separate
            // fixed stores, so draining both here preserves their causal
            // instruction order without ending the outer CPU batch.
            FlushDeferredCpuDataTiming(ref cycle, keepBatchActive: true);

            if (!_agnusBusExecutor.TryEnqueueCpuCustomWordWrite(
                address,
                value,
                cycle,
                CpuJournalInstructionPhase.Operand,
                out _))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            _agnusBusExecutor.FlushCpuEventJournal(ref cycle);
            cycle += AgnusChipSlotScheduler.SlotCycles;
            return true;
        }

        private void RecordDeferredCpuBusBatchWakeSource(M68kTraceBatchWakeSource wakeSource)
        {
            switch (wakeSource)
            {
                case M68kTraceBatchWakeSource.PendingInterrupt:
                    _deferredCpuBusBatchWakePendingInterrupt++;
                    break;
                case M68kTraceBatchWakeSource.VerticalBlank:
                    _deferredCpuBusBatchWakeVerticalBlank++;
                    break;
                case M68kTraceBatchWakeSource.HorizontalSyncTod:
                    _deferredCpuBusBatchWakeHorizontalSyncTod++;
                    break;
                case M68kTraceBatchWakeSource.CiaTimer:
                    _deferredCpuBusBatchWakeCiaTimer++;
                    break;
                case M68kTraceBatchWakeSource.Disk:
                    _deferredCpuBusBatchWakeDisk++;
                    break;
                case M68kTraceBatchWakeSource.Paula:
                    _deferredCpuBusBatchWakePaula++;
                    break;
                case M68kTraceBatchWakeSource.Copper:
                    _deferredCpuBusBatchWakeCopper++;
                    break;
                case M68kTraceBatchWakeSource.Blitter:
                    _deferredCpuBusBatchWakeBlitter++;
                    break;
                case M68kTraceBatchWakeSource.TargetCycle:
                case M68kTraceBatchWakeSource.Unknown:
                default:
                    _deferredCpuBusBatchWakeTargetCycle++;
                    break;
            }
        }

        private void RecordDeferredCpuBusBatchDiskWakeReason(AmigaDiskController.SchedulerWakeReason reason)
        {
            switch (reason)
            {
                case AmigaDiskController.SchedulerWakeReason.PendingDma:
                    _deferredCpuBusBatchDiskWakePendingDma++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.ActiveDmaProgress:
                    _deferredCpuBusBatchDiskWakeActiveDmaProgress++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.ActiveDmaCompletion:
                    _deferredCpuBusBatchDiskWakeActiveDmaCompletion++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.SyncCandidate:
                    _deferredCpuBusBatchDiskWakeSyncCandidate++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.IndexPulse:
                    _deferredCpuBusBatchDiskWakeIndexPulse++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.PassiveByteReady:
                    _deferredCpuBusBatchDiskWakePassiveByteReady++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.None:
                default:
                    _deferredCpuBusBatchDiskWakeUnknown++;
                    break;
            }
        }

        private void RecordDeferredCpuInternalNoBusWindowWakeSource(M68kTraceBatchWakeSource wakeSource)
        {
            switch (wakeSource)
            {
                case M68kTraceBatchWakeSource.PendingInterrupt:
                    _deferredCpuInternalNoBusWindowWakePendingInterrupt++;
                    break;
                case M68kTraceBatchWakeSource.VerticalBlank:
                    _deferredCpuInternalNoBusWindowWakeVerticalBlank++;
                    break;
                case M68kTraceBatchWakeSource.HorizontalSyncTod:
                    _deferredCpuInternalNoBusWindowWakeHorizontalSyncTod++;
                    break;
                case M68kTraceBatchWakeSource.CiaTimer:
                    _deferredCpuInternalNoBusWindowWakeCiaTimer++;
                    break;
                case M68kTraceBatchWakeSource.Disk:
                    _deferredCpuInternalNoBusWindowWakeDisk++;
                    break;
                case M68kTraceBatchWakeSource.Paula:
                    _deferredCpuInternalNoBusWindowWakePaula++;
                    break;
                case M68kTraceBatchWakeSource.Copper:
                    _deferredCpuInternalNoBusWindowWakeCopper++;
                    break;
                case M68kTraceBatchWakeSource.Blitter:
                    _deferredCpuInternalNoBusWindowWakeBlitter++;
                    break;
                case M68kTraceBatchWakeSource.TargetCycle:
                case M68kTraceBatchWakeSource.Unknown:
                default:
                    _deferredCpuInternalNoBusWindowWakeTargetCycle++;
                    break;
            }
        }

        private void EndDeferredCpuBusBatchCore(ref long cycle, M68kDeferredCpuBusBatchExitReason reason)
        {
            if (!_deferredCpuBusBatchActive)
            {
                FlushDeferredCpuDataTiming(ref cycle);
                _agnusBusExecutor.FlushCpuEventJournal(ref cycle);
                _deferredCpuInstructionTimingActive = false;
                return;
            }

            var architecturalExitCycle = cycle;
            var hadCpuValueEvents = _agnusBusExecutor.CpuEventJournal.Count != 0;
            _endingDeferredCpuBusBatch = true;
            try
            {
                if (_deferredCpuDataAccessCount != 0)
                {
                    var checkpointRequest = default(M68kDeferredCpuBusCheckpointRequest);
                    FlushDeferredCpuDataTiming(
                        ref cycle,
                        keepBatchActive: true,
                        in checkpointRequest,
                        out var checkpoint);
                    architecturalExitCycle = Math.Max(
                        architecturalExitCycle,
                        checkpoint.ArchitecturalRetireCycle);
                }

                _agnusBusExecutor.FlushCpuEventJournal(ref cycle);
            }
            finally
            {
                _endingDeferredCpuBusBatch = false;
            }

            if (!hadCpuValueEvents &&
                reason is not M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess and
                    not M68kDeferredCpuBusBatchExitReason.Exception)
            {
                cycle = architecturalExitCycle;
            }

            _deferredCpuBusBatchActive = false;
            _deferredCpuBusBatchHasTargetCycle = false;
            _deferredCpuBusBatchTargetCycle = 0;
            _deferredCpuInstructionTimingActive = false;
            var executionStarted = _deferredCpuBusBatchExecutionStarted;
            _deferredCpuBusBatchExecutionStarted = false;
            if (executionStarted)
            {
                _deferredCpuBusBatchFlushes++;
            }
            _deferredCpuBatchExitChipAccessCycle = reason == M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess
                ? Math.Max(0, cycle)
                : -1;
            if (!executionStarted)
            {
                return;
            }

            switch (reason)
            {
                case M68kDeferredCpuBusBatchExitReason.TargetCycle:
                    _deferredCpuBusBatchExitTargetCycle++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.MaxInstructions:
                    _deferredCpuBusBatchExitMaxInstructions++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.PcLeftFastWindow:
                    _deferredCpuBusBatchExitPcLeftFastWindow++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.Exception:
                    _deferredCpuBusBatchExitException++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.Unsupported:
                    _deferredCpuBusBatchExitUnsupported++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess:
                case M68kDeferredCpuBusBatchExitReason.HaltedOrStopped:
                case M68kDeferredCpuBusBatchExitReason.Completed:
                case M68kDeferredCpuBusBatchExitReason.None:
                default:
                    _deferredCpuBusBatchExitChipVisibleAccess++;
                    break;
            }
        }

        private void ResetDeferredCpuBusBatchState(bool resetCounters)
        {
            _deferredCpuInstructionTimingActive = false;
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;
            _deferredCpuDataInstructionFetchShapeBits = 0;
            _deferredCpuDataRomShapeBits = 0;
            _deferredCpuDataReplayCycle = 0;
            _deferredCpuProjectedRetireDelay = 0;
            _deferredCpuProjectedEntryCount = 0;
            _deferredCpuProjectedCompletedCycle = -1;
            _agnusBusExecutor.ClearUnresolvedCpuTiming();
            _deferredCpuBusBatchActive = false;
            _deferredCpuBusBatchHasTargetCycle = false;
            _deferredCpuBusBatchTargetCycle = 0;
            _endingDeferredCpuBusBatch = false;
            _deferredCpuBusBatchExecutionStarted = false;
            _deferredCpuBusBatchPendingWakeSource = M68kTraceBatchWakeSource.Unknown;
            _deferredCpuBusBatchPendingDiskWakeReason = AmigaDiskController.SchedulerWakeReason.None;
            _deferredCpuBatchExitChipAccessCycle = -1;
            if (!resetCounters)
            {
                return;
            }

            _deferredCpuBusBatchAttempts = 0;
            _deferredCpuBusBatchUsed = 0;
            _deferredCpuBusBatchInstructions = 0;
            _deferredCpuBusBatchSkippedInstructionFlushes = 0;
            _deferredCpuBusBatchFlushes = 0;
            _deferredCpuTimingProjectionAttempts = 0;
            _deferredCpuTimingProjectionSupported = 0;
            _deferredCpuTimingProjectionMatches = 0;
            _deferredCpuTimingProjectionMismatches = 0;
            _deferredCpuTimingProjectionFirstMismatch = string.Empty;
            _deferredCpuBusBatchExitTargetCycle = 0;
            _deferredCpuBusBatchExitMaxInstructions = 0;
            _deferredCpuBusBatchExitChipVisibleAccess = 0;
            _deferredCpuBusBatchExitPcLeftFastWindow = 0;
            _deferredCpuBusBatchExitException = 0;
            _deferredCpuBusBatchExitUnsupported = 0;
            _deferredCpuBusBatchVerificationMismatches = 0;
            _deferredCpuBusBatchFirstMismatch = string.Empty;
            _deferredCpuBusBatchWakeTargetCycle = 0;
            _deferredCpuBusBatchWakePendingInterrupt = 0;
            _deferredCpuBusBatchWakeVerticalBlank = 0;
            _deferredCpuBusBatchWakeHorizontalSyncTod = 0;
            _deferredCpuBusBatchWakeCiaTimer = 0;
            _deferredCpuBusBatchWakeDisk = 0;
            _deferredCpuBusBatchWakePaula = 0;
            _deferredCpuBusBatchWakeCopper = 0;
            _deferredCpuBusBatchWakeBlitter = 0;
            _deferredCpuBusBatchDiskWakePendingDma = 0;
            _deferredCpuBusBatchDiskWakeActiveDmaProgress = 0;
            _deferredCpuBusBatchDiskWakeActiveDmaCompletion = 0;
            _deferredCpuBusBatchDiskWakeSyncCandidate = 0;
            _deferredCpuBusBatchDiskWakeIndexPulse = 0;
            _deferredCpuBusBatchDiskWakePassiveByteReady = 0;
            _deferredCpuBusBatchDiskWakeUnknown = 0;
            _deferredCpuInternalNoBusWindowAttempts = 0;
            _deferredCpuInternalNoBusWindowUsed = 0;
            _deferredCpuInternalNoBusWindowTotalCycles = 0;
            _deferredCpuInternalNoBusWindowAdvancedCycles = 0;
            _deferredCpuInternalNoBusWindowMultiply = 0;
            _deferredCpuInternalNoBusWindowDivide = 0;
            _deferredCpuInternalNoBusWindowWakeTargetCycle = 0;
            _deferredCpuInternalNoBusWindowWakePendingInterrupt = 0;
            _deferredCpuInternalNoBusWindowWakeVerticalBlank = 0;
            _deferredCpuInternalNoBusWindowWakeHorizontalSyncTod = 0;
            _deferredCpuInternalNoBusWindowWakeCiaTimer = 0;
            _deferredCpuInternalNoBusWindowWakeDisk = 0;
            _deferredCpuInternalNoBusWindowWakePaula = 0;
            _deferredCpuInternalNoBusWindowWakeCopper = 0;
            _deferredCpuInternalNoBusWindowWakeBlitter = 0;
            _deferredCpuInternalNoBusWindowVerificationMismatches = 0;
            _deferredCpuInternalNoBusWindowFirstMismatch = string.Empty;
            ResetDeferredCpuWaitDiagnostics();
        }




        private bool TryDeferExactCpuExpansionDataTiming(
            AmigaBusAccessSize size,
            ref long cycle,
            AmigaBusAccessKind kind = AmigaBusAccessKind.CpuDataRead,
            AmigaBusAccessTarget target = AmigaBusAccessTarget.ExpansionRam,
            M68kInstructionFetchPublicationPhase instructionFetchPublicationPhase =
                M68kInstructionFetchPublicationPhase.Required,
            long instructionFetchRetirementFloor = long.MinValue,
            in M68kInstructionFetchPublicationContext instructionFetchPublicationContext = default)
        {
            if (!_deferredCpuInstructionTimingActive)
            {
                return false;
            }

            SynchronizeDeferredCpuJournalStart(ref cycle);

            // Instruction-fetch delay feeds directly back into the blitter's
            // nice/nasty CPU-steal cadence. Preserve the exact live request
            // while a blit is active; replaying it after later micro-ops have
            // executed changes which side owns the first contended slot.
            if (kind == AmigaBusAccessKind.CpuInstructionFetch &&
                (Blitter.Busy || _deferredCpuWaitDiagnosticsEnabled))
            {
                return false;
            }

            if (_deferredCpuDataAccessCount >= MaxDeferredCpuDataAccesses)
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return false;
            }

            var index = _deferredCpuDataAccessCount;
            if (index == 0)
            {
                _deferredCpuDataReplayCycle = cycle;
            }

            _deferredCpuDataRequestedCycles[index] = cycle;
            _deferredCpuDataRequestedRetireDelays[index] = _deferredCpuProjectedRetireDelay;
            var timingToken = ++_deferredCpuDataTimingTokenClock;
            if (timingToken == 0)
            {
                timingToken = ++_deferredCpuDataTimingTokenClock;
            }
            _deferredCpuDataTimingTokens[index] = timingToken;

            if (size == AmigaBusAccessSize.Long)
            {
                _deferredCpuDataLongShapeBits |= 1UL << index;
            }

            if (kind == AmigaBusAccessKind.CpuInstructionFetch)
            {
                _deferredCpuDataInstructionFetchShapeBits |= 1UL << index;
                _deferredCpuDataInstructionFetchPublicationPhases[index] =
                    instructionFetchPublicationPhase;
                _deferredCpuDataInstructionFetchRetirementFloors[index] =
                    instructionFetchRetirementFloor;
                _deferredCpuDataInstructionFetchPublicationContexts[index] =
                    instructionFetchPublicationContext;
                _deferredCpuInstructionFetchLastCapturedPublicationContext =
                    instructionFetchPublicationContext;
                _lastDeferredCpuInstructionFetchTimingToken = timingToken;
            }

            if (target == AmigaBusAccessTarget.Rom)
            {
                _deferredCpuDataRomShapeBits |= 1UL << index;
            }

            _deferredCpuDataAccessCount = index + 1;
            _agnusBusExecutor.PublishUnresolvedCpuTiming(
                _deferredCpuDataRequestedCycles[index] +
                _deferredCpuDataRequestedRetireDelays[index]);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanDeferExactCpuCiaRead(int ciaRegister)
            => ciaRegister is 0 or 1 or 2 or 3 or 0x0E or 0x0F;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDeferExactCpuCiaDataTiming(ref long cycle)
        {
            if (!_deferredCpuInstructionTimingActive)
            {
                return false;
            }

            SynchronizeDeferredCpuJournalStart(ref cycle);

            if (_deferredCpuDataAccessCount >= MaxDeferredCpuDataAccesses)
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return false;
            }

            var index = _deferredCpuDataAccessCount;
            if (index == 0)
            {
                _deferredCpuDataReplayCycle = cycle;
            }

            _deferredCpuDataRequestedCycles[index] = cycle;
            _deferredCpuDataRequestedRetireDelays[index] = _deferredCpuProjectedRetireDelay;
            var timingToken = ++_deferredCpuDataTimingTokenClock;
            if (timingToken == 0)
            {
                timingToken = ++_deferredCpuDataTimingTokenClock;
            }
            _deferredCpuDataTimingTokens[index] = timingToken;
            _deferredCpuDataCiaShapeBits |= 1UL << index;
            _deferredCpuDataAccessCount = index + 1;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushDeferredCpuDataTiming(ref long cycle)
            => FlushDeferredCpuDataTiming(ref cycle, keepBatchActive: false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushDeferredCpuDataTimingForAccess(
            AmigaBusAccessTarget target,
            AmigaBusAccessKind kind,
            bool isWrite,
            ref long cycle)
        {
            FlushDeferredCpuDataTiming(
                ref cycle,
                CanKeepDeferredCpuBusBatchForAccess(target, kind, isWrite, cycle));
            AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
        }

        private void FlushDeferredCpuDataTiming(ref long cycle, bool keepBatchActive)
        {
            var request = default(M68kDeferredCpuBusCheckpointRequest);
            FlushDeferredCpuDataTiming(
                ref cycle,
                keepBatchActive,
                in request,
                out _);
        }

        private void FlushDeferredCpuDataTiming(
            ref long cycle,
            bool keepBatchActive,
            in M68kDeferredCpuBusCheckpointRequest checkpointRequest,
            out M68kDeferredCpuBusCheckpoint checkpoint)
        {
            var count = _deferredCpuDataAccessCount;
            if (count == 0)
            {
                _agnusBusExecutor.ClearUnresolvedCpuTiming();
                if (!keepBatchActive &&
                    _deferredCpuBusBatchActive &&
                    !_endingDeferredCpuBusBatch)
                {
                    EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                }

                var hasPendingTransition =
                    checkpointRequest.HasPendingSuccessor ||
                    _deferredCpuTrimmedPendingTransitionActive;
                var pendingOriginalCycle = checkpointRequest.HasPendingSuccessor
                    ? checkpointRequest.PendingSuccessorVirtualCycle
                    : _deferredCpuTrimmedPendingOriginalCycle;
                checkpoint = new M68kDeferredCpuBusCheckpoint(
                    cycle,
                    cycle,
                    cycle,
                    -1,
                    -1,
                    0,
                    hasPendingTransition,
                    pendingOriginalCycle,
                    cycle + AgnusChipSlotScheduler.SlotCycles,
                    checkpointRequest.PendingSuccessorPredecessorToken,
                    checkpointRequest.PendingSuccessorRequiresIrcGap,
                    false);
                _deferredCpuTrimmedPendingTransitionActive = false;
                return;
            }

            var longShapeBits = _deferredCpuDataLongShapeBits;
            var ciaShapeBits = _deferredCpuDataCiaShapeBits;
            var instructionFetchShapeBits = _deferredCpuDataInstructionFetchShapeBits;
            var romShapeBits = _deferredCpuDataRomShapeBits;
            var replayCycle = _deferredCpuDataReplayCycle;
            var virtualRetireCycle = cycle;
            _agnusBusExecutor.ClearUnresolvedCpuTiming();
            var projectionSupported = TryProjectDeferredCpuTimingJournal(
                count,
                longShapeBits,
                ciaShapeBits,
                romShapeBits,
                instructionFetchShapeBits,
                0,
                out var projectedCompletedCycle,
                out _,
                out _);
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;
            _deferredCpuDataInstructionFetchShapeBits = 0;
            _deferredCpuDataRomShapeBits = 0;

            var lastReadyCycle = replayCycle;
            var queueReadyCycle0 = -1L;
            var queueReadyCycle1 = -1L;
            var requiredReadyCycle = -1L;
            byte resolvedQueueMask = 0;
            var replayedRequestedCycleJournal =
                longShapeBits == 0 &&
                ciaShapeBits == 0 &&
                romShapeBits == 0 &&
                TryReplayDeferredCpuRequestedCycleJournal(
                    count,
                    instructionFetchShapeBits,
                    ref replayCycle,
                    in checkpointRequest,
                    out _,
                    out lastReadyCycle,
                    out queueReadyCycle0,
                    out queueReadyCycle1,
                    out requiredReadyCycle,
                    out resolvedQueueMask);
            for (var i = 0; !replayedRequestedCycleJournal && i < count; i++)
            {
                if (((ciaShapeBits >> i) & 1UL) != 0)
                {
                    replayCycle = CiaPeripheralAccessTiming.AlignToCiaPeripheralAccessCycle(Math.Max(0, replayCycle));
                    continue;
                }

                var isLong = ((longShapeBits >> i) & 1UL) != 0;
                var kind = ((instructionFetchShapeBits >> i) & 1UL) != 0
                    ? AmigaBusAccessKind.CpuInstructionFetch
                    : AmigaBusAccessKind.CpuDataRead;
                var target = ((romShapeBits >> i) & 1UL) != 0
                    ? AmigaBusAccessTarget.Rom
                    : AmigaBusAccessTarget.ExpansionRam;
                if (!isLong)
                {
                    var runLength = 1;
                    while (i + runLength < count &&
                        ((ciaShapeBits >> (i + runLength)) & 1UL) == 0 &&
                        ((longShapeBits >> (i + runLength)) & 1UL) == 0 &&
                        (((romShapeBits >> (i + runLength)) & 1UL) != 0) ==
                            (target == AmigaBusAccessTarget.Rom))
                    {
                        runLength++;
                    }

                    var runInstructionFetchShapeBits = instructionFetchShapeBits >> i;
                    if (runLength < 64)
                    {
                        runInstructionFetchShapeBits &= (1UL << runLength) - 1UL;
                    }

                    if (TryReplayDeferredCpuExpansionWordTimingSequence(
                        runLength,
                        runInstructionFetchShapeBits,
                        target,
                        ref replayCycle))
                    {
                        i += runLength - 1;
                        continue;
                    }
                }

                var size = isLong
                    ? AmigaBusAccessSize.Long
                    : AmigaBusAccessSize.Word;
                if (target == AmigaBusAccessTarget.Rom)
                {
                    CommitExactCpuDataTiming(
                        target,
                        0x00FC0000u,
                        size,
                        ref replayCycle,
                        isWrite: false,
                        kind,
                        out _,
                        out _);
                }
                else
                {
                    CommitExactCpuExpansionDataTiming(
                        ExpansionRamBase,
                        size,
                        ref replayCycle,
                        isWrite: false,
                        kind,
                        OcsLiveDmaScratchCpuWrite.None,
                        out _,
                        out _);
                }
            }

            if (cycle < replayCycle)
            {
                cycle = replayCycle;
            }

            if (replayedRequestedCycleJournal)
            {
                cycle = Math.Max(
                    cycle,
                    virtualRetireCycle + _deferredCpuProjectedRetireDelay);
            }

            var architecturalRetireCycle = Math.Max(
                virtualRetireCycle + _deferredCpuProjectedRetireDelay,
                requiredReadyCycle);
            var hasResolvedPendingTransition =
                checkpointRequest.HasPendingSuccessor ||
                _deferredCpuTrimmedPendingTransitionActive;
            var resolvedPendingOriginalCycle = checkpointRequest.HasPendingSuccessor
                ? checkpointRequest.PendingSuccessorVirtualCycle
                : _deferredCpuTrimmedPendingOriginalCycle;
            var resolvedPendingRequestCycle =
                architecturalRetireCycle + AgnusChipSlotScheduler.SlotCycles;
            if (checkpointRequest.PendingSuccessorRequiresIrcGap &&
                checkpointRequest.PendingSuccessorPredecessorToken != 0 &&
                checkpointRequest.PendingSuccessorPredecessorToken ==
                    _deferredCpuIrcPairShadowLastToken)
            {
                resolvedPendingRequestCycle = Math.Max(
                    resolvedPendingOriginalCycle,
                    _deferredCpuIrcPairShadowLastReadyCycle + 4);
                if (_deferredCpuIrcPairShadowEntries == 0)
                {
                    _deferredCpuIrcPairShadowFirstPredecessorReadyCycle =
                        _deferredCpuIrcPairShadowLastReadyCycle;
                    _deferredCpuIrcPairShadowFirstPredictedReadyCycle =
                        resolvedPendingRequestCycle;
                }
                _deferredCpuIrcPairShadowEntries++;
                _deferredCpuIrcPairShadowPredecessorReadyCycle =
                    _deferredCpuIrcPairShadowLastReadyCycle;
                _deferredCpuIrcPairShadowPredictedReadyCycle =
                    resolvedPendingRequestCycle;
            }
            checkpoint = new M68kDeferredCpuBusCheckpoint(
                architecturalRetireCycle,
                replayCycle,
                lastReadyCycle,
                queueReadyCycle0,
                queueReadyCycle1,
                resolvedQueueMask,
                hasResolvedPendingTransition,
                resolvedPendingOriginalCycle,
                resolvedPendingRequestCycle,
                checkpointRequest.PendingSuccessorPredecessorToken,
                checkpointRequest.PendingSuccessorRequiresIrcGap,
                false);
            _deferredCpuTrimmedPendingTransitionActive = false;

            if (projectionSupported)
            {
                if (projectedCompletedCycle == replayCycle)
                {
                    _deferredCpuTimingProjectionMatches++;
                }
                else
                {
                    _deferredCpuTimingProjectionMismatches++;
                    if (_deferredCpuTimingProjectionFirstMismatch.Length == 0)
                    {
                        _deferredCpuTimingProjectionFirstMismatch =
                            $"count={count}, actual={replayCycle}, projected={projectedCompletedCycle}, " +
                            $"first={_deferredCpuDataRequestedCycles[0]}";
                    }
                }
            }

            _deferredCpuDataReplayCycle = cycle;
            _deferredCpuProjectedRetireDelay = 0;
            _deferredCpuProjectedEntryCount = 0;
            _deferredCpuProjectedCompletedCycle = -1;
            if (!keepBatchActive &&
                _deferredCpuBusBatchActive &&
                !_endingDeferredCpuBusBatch)
            {
                EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
            }
        }

        private bool TryReplayDeferredCpuRequestedCycleJournal(
            int count,
            ulong instructionFetchShapeBits,
            ref long replayCycle,
            in M68kDeferredCpuBusCheckpointRequest checkpointRequest,
            out long replayCumulativeDelay,
            out long lastReadyCycle,
            out long queueReadyCycle0,
            out long queueReadyCycle1,
            out long requiredReadyCycle,
            out byte resolvedQueueMask)
        {
            replayCumulativeDelay = 0;
            lastReadyCycle = replayCycle;
            queueReadyCycle0 = -1;
            queueReadyCycle1 = -1;
            requiredReadyCycle = -1;
            resolvedQueueMask = 0;
            if (count <= 0)
            {
                return false;
            }

            if (_captureBusAccesses ||
                !_agnusBusExecutor.ProductionEnabled ||
                !_useChipSlotScheduler ||
                _deferredCpuWaitDiagnosticsEnabled ||
                replayCycle < _agnusBusExecutor.ExecutedThroughCycle)
            {
                return false;
            }

            var actualCompletedCycle = replayCycle;
            for (var index = 0; index < count; index++)
            {
                var token = _deferredCpuDataTimingTokens[index];
                var entryNominalCycle = _deferredCpuDataRequestedCycles[index] +
                    _deferredCpuDataRequestedRetireDelays[index];
                var publicationContext =
                    _deferredCpuDataInstructionFetchPublicationContexts[index];
                if (publicationContext.RequiresIrcGap &&
                    publicationContext.PredecessorToken != 0 &&
                    publicationContext.PredecessorToken == _deferredCpuIrcPairShadowLastToken)
                {
                    var predictedRequestCycle = Math.Max(
                        entryNominalCycle,
                        _deferredCpuIrcPairShadowLastReadyCycle + 4);
                    if (TryPredictCpuWaitFixedSlotGrant(
                            AmigaBusAccessKind.CpuInstructionFetch,
                            AmigaBusAccessTarget.ExpansionRam,
                            ExpansionRamBase,
                            AmigaBusAccessSize.Word,
                            predictedRequestCycle,
                            isWrite: false,
                            out var predictedGrantCycle,
                            out _,
                            out _))
                    {
                        if (_deferredCpuIrcPairShadowEntries == 0)
                        {
                            _deferredCpuIrcPairShadowFirstPredecessorReadyCycle =
                                _deferredCpuIrcPairShadowLastReadyCycle;
                            _deferredCpuIrcPairShadowFirstPredictedReadyCycle =
                                predictedGrantCycle;
                        }
                        _deferredCpuIrcPairShadowEntries++;
                        _deferredCpuIrcPairShadowPredecessorReadyCycle =
                            _deferredCpuIrcPairShadowLastReadyCycle;
                        _deferredCpuIrcPairShadowPredictedReadyCycle = predictedGrantCycle;
                    }
                }
                var requestedCycle = Math.Max(
                    actualCompletedCycle,
                    entryNominalCycle);
                if (publicationContext.RequiresIrcGap &&
                    publicationContext.PredecessorToken != 0 &&
                    publicationContext.PredecessorToken == _deferredCpuIrcPairShadowLastToken)
                {
                    requestedCycle = Math.Max(
                        requestedCycle,
                        _deferredCpuIrcPairShadowLastReadyCycle + 4);
                }
                var runCycle = requestedCycle;
                if (!TryReplayDeferredCpuExpansionWordTimingSequence(
                    1,
                    (instructionFetchShapeBits >> index) & 1UL,
                    AmigaBusAccessTarget.ExpansionRam,
                    ref runCycle))
                {
                    if (index == 0)
                    {
                        return false;
                    }

                    throw new InvalidOperationException(
                        "Deferred CPU requested-cycle replay failed after committing an earlier run.");
                }

                actualCompletedCycle = runCycle;
                var identityReadyCycle = runCycle - AgnusChipSlotScheduler.SlotCycles;
                var readyCycle = runCycle;
                if (((instructionFetchShapeBits >> index) & 1UL) != 0 &&
                    _deferredCpuDataInstructionFetchPublicationPhases[index] is
                        M68kInstructionFetchPublicationPhase.RetirementQueue or
                        M68kInstructionFetchPublicationPhase.CancellableSuccessor)
                {
                    var nominalCycle = _deferredCpuDataRequestedCycles[index] +
                        _deferredCpuDataRequestedRetireDelays[index];
                    var inheritedDelay = _deferredCpuInstructionPublicationPhaseSlip;
                    _deferredCpuRetirementPublicationShadowEntries++;
                    _deferredCpuRetirementPublicationShadowLastNominalCycle = nominalCycle;
                    _deferredCpuRetirementPublicationShadowLastFloor =
                        _deferredCpuDataInstructionFetchRetirementFloors[index];
                    _deferredCpuRetirementPublicationShadowLastInheritedDelay = inheritedDelay;
                    _deferredCpuRetirementPublicationShadowLastLegacyReadyCycle = readyCycle;
                    var translatedReadyCycle = Math.Max(
                        actualCompletedCycle,
                        nominalCycle + CalculateDeferredCpuPublicationResidualSlip(
                            index,
                            nominalCycle,
                            inheritedDelay));
                    _deferredCpuRetirementPublicationShadowLastTranslatedCycle =
                        translatedReadyCycle;
                    readyCycle = translatedReadyCycle;
                    _deferredCpuRetirementPublicationShadowLastPhase =
                        _deferredCpuDataInstructionFetchPublicationPhases[index];
                    _deferredCpuRetirementPublicationShadowLastGroup =
                        _deferredCpuDataInstructionFetchPublicationContexts[index].Group;
                    RecordDeferredCpuPublicationShadowContext(
                        index,
                        nominalCycle,
                        _deferredCpuDataInstructionFetchRetirementFloors[index],
                        inheritedDelay,
                        wasReplay: true);
                    if (inheritedDelay > 0)
                    {
                        _deferredCpuInstructionPublicationPhaseSlip = 0;
                        _deferredCpuInstructionPublicationPhaseSlipGroup = 0;
                    }
                }
                _deferredCpuIrcPairShadowLastToken = token;
                _deferredCpuIrcPairShadowLastReadyCycle = identityReadyCycle;
                lastReadyCycle = readyCycle;
                if (token != 0 && token == checkpointRequest.RequiredThroughToken)
                {
                    requiredReadyCycle = readyCycle;
                    if (checkpointRequest.RequiredVirtualReadyCycle >= 0)
                    {
                        _deferredCpuInstructionPublicationPhaseSlip = Math.Max(
                            0,
                            readyCycle - checkpointRequest.RequiredVirtualReadyCycle);
                        _deferredCpuInstructionPublicationPhaseSlipGroup =
                            _deferredCpuDataInstructionFetchPublicationContexts[index].Group;
                        _deferredCpuInstructionPublicationPhaseSlipToken = token;
                        _deferredCpuInstructionPublicationPhaseSlipVirtualReadyCycle =
                            checkpointRequest.RequiredVirtualReadyCycle;
                        _deferredCpuInstructionPublicationPhaseSlipActualReadyCycle =
                            readyCycle;
                        _deferredCpuInstructionPublicationPhaseSlipContext =
                            publicationContext;
                    }
                }

                if (token != 0 && token == checkpointRequest.QueueToken0)
                {
                    queueReadyCycle0 = readyCycle;
                    resolvedQueueMask |= 1;
                }

                if (token != 0 && token == checkpointRequest.QueueToken1)
                {
                    queueReadyCycle1 = readyCycle;
                    resolvedQueueMask |= 2;
                }
            }

            replayCycle = actualCompletedCycle;
            return true;
        }

        private void RecordDeferredCpuPublicationShadowContext(
            int index,
            long nominalCycle,
            long retirementFloor,
            long inheritedDelay,
            bool wasReplay)
        {
            if (wasReplay)
            {
                _deferredCpuRetirementPublicationShadowReplayEntries++;
            }
            else
            {
                _deferredCpuRetirementPublicationShadowTrimEntries++;
            }
            _deferredCpuRetirementPublicationShadowLastWasReplay = wasReplay;
            var context = _deferredCpuDataInstructionFetchPublicationContexts[index];
            _deferredCpuRetirementPublicationShadowLastContext = context;
            if (_deferredCpuRetirementPublicationShadowFirstCaptured || inheritedDelay <= 0)
            {
                return;
            }

            _deferredCpuRetirementPublicationShadowFirstCaptured = true;
            _deferredCpuRetirementPublicationShadowFirstNominalCycle = nominalCycle;
            _deferredCpuRetirementPublicationShadowFirstFloor = retirementFloor;
            _deferredCpuRetirementPublicationShadowFirstInheritedDelay = inheritedDelay;
            _deferredCpuRetirementPublicationShadowFirstTranslatedCycle = nominalCycle +
                CalculateDeferredCpuPublicationResidualSlip(index, nominalCycle, inheritedDelay);
            _deferredCpuRetirementPublicationShadowFirstSlipGroup =
                _deferredCpuInstructionPublicationPhaseSlipGroup;
            _deferredCpuRetirementPublicationShadowFirstContext = context;
        }

        private long CalculateDeferredCpuPublicationResidualSlip(
            int index,
            long nominalCycle,
            long inheritedDelay)
        {
            if (inheritedDelay <= 0)
            {
                return 0;
            }

            var context = _deferredCpuDataInstructionFetchPublicationContexts[index];
            var ordinaryPublicationCycle = context.EntryBusCycle +
                AgnusChipSlotScheduler.SlotCycles;
            var alreadyAbsorbed = Math.Max(0, nominalCycle - ordinaryPublicationCycle);
            return Math.Max(0, inheritedDelay - alreadyAbsorbed);
        }

        private bool TryProjectDeferredCpuTimingJournal(
            int count,
            ulong longShapeBits,
            ulong ciaShapeBits,
            ulong romShapeBits,
            ulong instructionFetchShapeBits,
            ulong dependencyToken,
            out long projectedCompletedCycle,
            out long cumulativeDelay,
            out long dependencyCompletedCycle)
        {
            projectedCompletedCycle = 0;
            cumulativeDelay = 0;
            dependencyCompletedCycle = -1;
            _deferredCpuTimingProjectionAttempts++;
            if (count <= 0 ||
                longShapeBits != 0 ||
                ciaShapeBits != 0 ||
                romShapeBits != 0)
            {
                return false;
            }

            var virtualCompletedCycle = _deferredCpuDataRequestedCycles[0];
            var previousCompletedCycle = -1L;
            for (var index = 0; index < count; index++)
            {
                var virtualRequestedCycle = _deferredCpuDataRequestedCycles[index];
                var requestedCycle = virtualRequestedCycle +
                    _deferredCpuDataRequestedRetireDelays[index];
                if (previousCompletedCycle >= 0)
                {
                    requestedCycle = Math.Max(requestedCycle, previousCompletedCycle);
                }

                var kind = ((instructionFetchShapeBits >> index) & 1UL) != 0
                    ? AmigaBusAccessKind.CpuInstructionFetch
                    : AmigaBusAccessKind.CpuDataRead;
                if (!TryPredictCpuWaitFixedSlotGrant(
                        kind,
                        AmigaBusAccessTarget.ExpansionRam,
                        ExpansionRamBase,
                        AmigaBusAccessSize.Word,
                        requestedCycle,
                        isWrite: false,
                        out _,
                        out var completedCycle,
                        out _))
                {
                    return false;
                }

                virtualCompletedCycle = Math.Max(
                    virtualCompletedCycle,
                    virtualRequestedCycle) + AgnusChipSlotScheduler.SlotCycles;
                cumulativeDelay = completedCycle - virtualCompletedCycle;
                previousCompletedCycle = completedCycle;
                if (dependencyToken != 0 &&
                    _deferredCpuDataTimingTokens[index] == dependencyToken)
                {
                    dependencyCompletedCycle = completedCycle;
                }
            }

            projectedCompletedCycle = previousCompletedCycle;
            _deferredCpuTimingProjectionSupported++;
            return true;
        }

        private bool TryReplayDeferredCpuExpansionWordTimingSequence(
            int wordCount,
            ulong instructionFetchShapeBits,
            AmigaBusAccessTarget target,
            ref long cycle)
        {
            if (_captureBusAccesses ||
                !_agnusBusExecutor.ProductionEnabled ||
                !_useChipSlotScheduler ||
                _deferredCpuWaitDiagnosticsEnabled ||
                cycle < _agnusBusExecutor.ExecutedThroughCycle)
            {
                return false;
            }

            var requestedCycle = cycle;
            var request = new CpuTimingSequenceRequest(
                AmigaBusAccessKind.CpuDataRead,
                target,
                target == AmigaBusAccessTarget.Rom ? 0x00FC0000u : ExpansionRamBase,
                requestedCycle,
                wordCount,
                isWrite: false,
                instructionFetchShapeBits);
            if (!_hardwareScheduler.AdvanceCpuTimingSequence(request, out var result) ||
                result.CompletedWords != wordCount)
            {
                return false;
            }

            if (result.TotalWaitCycles > 0)
            {
                Agnus.RecordCpuChipWaitCycles(result.TotalWaitCycles);
            }

            cycle = result.CompletedCycle;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataRamTiming(
            in AmigaExactCpuDataRamRegion region,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle)
            => CommitExactCpuDataRamTiming(
                in region,
                size,
                ref cycle,
                isWrite,
                kind,
                OcsLiveDmaScratchCpuWrite.None,
                out grantedCycle,
                out secondWordCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataRamTiming(
            in AmigaExactCpuDataRamRegion region,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ExpansionRam)
            {
                CommitExactCpuExpansionDataTiming(
                    region.Address,
                    size,
                ref cycle,
                isWrite,
                kind,
                OcsLiveDmaScratchCpuWrite.None,
                out grantedCycle,
                out secondWordCycle);
                return;
            }

            CommitExactCpuDataTiming(
                region.Target,
                region.Address,
                size,
                ref cycle,
                isWrite,
                kind,
                scratchWrite,
                out grantedCycle,
                out secondWordCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataTiming(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle)
            => CommitExactCpuDataTiming(
                target,
                address,
                size,
                ref cycle,
                isWrite,
                kind,
                OcsLiveDmaScratchCpuWrite.None,
                out grantedCycle,
                out secondWordCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataTiming(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle)
        {
            if (!CanKeepDeferredCpuBusBatchForAccess(target, kind, isWrite, cycle))
            {
                var deferInstructionFetchWindowExit =
                    _deferredCpuBusBatchActive &&
                    _deferredCpuDataAccessCount == 0 &&
                    !isWrite &&
                    kind == AmigaBusAccessKind.CpuInstructionFetch;
                if (!deferInstructionFetchWindowExit)
                {
                    FlushDeferredCpuDataTiming(ref cycle);
                }

                if (_deferredCpuBusBatchActive && !_endingDeferredCpuBusBatch)
                {
                    if (!deferInstructionFetchWindowExit)
                    {
                        EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                    }
                }
            }

            var causalGrantAttempts = 0;
        RetryCausalCpuGrant:
            if (++causalGrantAttempts > 1024)
            {
                throw new InvalidOperationException(
                    $"CPU Chip RAM arbitration did not advance beyond bus horizon {ExecutedChipBusHorizon} " +
					$"for {kind} at 0x{address:X8} from cycle {cycle}.");
            }

            var requestedCycle = cycle;
            if (TryCommitExactCpuChipDataAccessFast(
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                kind,
                scratchWrite,
                out grantedCycle,
                out secondWordCycle,
                out var completedCycle,
                out var chronologicalGrantCommitted,
                synchronizeDmaAfterGrant: true))
            {
                if (!chronologicalGrantCommitted &&
                    target == AmigaBusAccessTarget.ChipRam &&
                    grantedCycle <= ExecutedChipBusHorizon)
                {
                    AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
                    goto RetryCausalCpuGrant;
                }

                cycle = completedCycle;
                return;
            }

            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    size,
                    cycle,
                    kind,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                out var fastCompletedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite);
                if (target == AmigaBusAccessTarget.ChipRam &&
                    grantedCycle <= ExecutedChipBusHorizon)
                {
                    AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
                    goto RetryCausalCpuGrant;
                }

                cycle = fastCompletedCycle;
                return;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                size,
                cycle,
                isWrite);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite);
            if (target == AmigaBusAccessTarget.ChipRam &&
                access.GrantedCycle <= ExecutedChipBusHorizon)
            {
                AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
                goto RetryCausalCpuGrant;
            }

            cycle = access.CompletedCycle;
            grantedCycle = access.GrantedCycle;
            secondWordCycle = GetSecondWordCycle(access);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuExpansionDataTiming(
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle)
        {
            var requestedCycle = cycle;
            if (TryCommitExactCpuChipDataAccessFast(
                AmigaBusAccessTarget.ExpansionRam,
                address,
                size,
                requestedCycle,
                isWrite,
                kind,
                scratchWrite,
                out grantedCycle,
                out secondWordCycle,
                out var completedCycle,
                out _,
                synchronizeDmaAfterGrant: false))
            {
                cycle = completedCycle;
                return;
            }

            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    AmigaBusAccessTarget.ExpansionRam,
                    address,
                    size,
                    cycle,
                    kind,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out var fastCompletedCycle);
                cycle = fastCompletedCycle;
                return;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                kind,
                AmigaBusAccessTarget.ExpansionRam,
                address,
                size,
                cycle,
                isWrite);
            cycle = access.CompletedCycle;
            grantedCycle = access.GrantedCycle;
            secondWordCycle = GetSecondWordCycle(access);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private bool TryCommitExactCpuChipDataAccessFast(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle,
            out bool chronologicalGrantCommitted,
            bool synchronizeDmaAfterGrant)
        {
            grantedCycle = 0;
            secondWordCycle = 0;
            completedCycle = 0;
            chronologicalGrantCommitted = false;

			if ((!_exactCpuChipSlotFastPathEnabled && !_agnusBusExecutor.ProductionEnabled) ||
                !ShouldUseChipSlotScheduler(target))
            {
                return false;
            }

            var grantRequestCycle = requestedCycle;
            var slotContendedClean = _hardwareScheduler.IsSlotContendedCleanThrough(grantRequestCycle);
            var scratchAudit = default(DeferredCpuWaitScratchAudit);
            var cpuGrantCommitted = false;
            var deferredPreparationUsed = false;
            var verifyDeferredDmaReadOwnership = false;
            var predictedDeferredDmaReadGrant = 0L;
            var predictedDeferredDmaReadCompletion = 0L;
            var predictedDeferredDmaReadTimeline = default(CpuWaitFixedSlotTimelineSignature);
            if (!slotContendedClean &&
                ShouldAttemptDeferredCpuWaitWindowFastPath(grantRequestCycle))
            {
                _deferredCpuBatchExitChipAccessCycle = -1;
                _deferredCpuWaitWindowFastPathAttempts++;
                if (_captureBusAccesses ||
                    size == AmigaBusAccessSize.Long ||
                    target is not (AmigaBusAccessTarget.ChipRam or
                        AmigaBusAccessTarget.ExpansionRam or
                        AmigaBusAccessTarget.RealTimeClock))
                {
                    _deferredCpuWaitWindowFastPathRejectedUnsupported++;
                    if (Display.HasLiveDisplayWork())
                    {
                        RecordProductionCpuWaitFixedSlotImageAttempt();
                        RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.Unsupported);
                    }
                }
                else if (HasUnsupportedCpuWaitSlotWorkThrough(
                    grantRequestCycle + LineCycles))
                {
                    _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    if (Display.HasLiveDisplayWork())
                    {
                        RecordProductionCpuWaitFixedSlotImageAttempt();
                        RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.DynamicDma);
                    }
                    RecordDeferredCpuWaitDynamicRejectShadow(
                        kind,
                        target,
                        address,
                        size,
                        isWrite,
                        requestedCycle);
                }
                else if (Blitter.Busy)
                {
                    _hardwareScheduler.RecordDeferredCpuWaitBlitterOverlap(
                        Blitter.CanUseCpuWaitAreaMicroOps,
                        Blitter.CpuStallActive);
                    if (ShouldRunDeferredCpuWaitSlotShadowAudit)
                    {
                        BeginDeferredCpuWaitScratchAudit(
                            kind,
                            target,
                            address,
                            size,
                            requestedCycle,
                            grantRequestCycle,
                            isWrite,
                            scratchWrite,
                            ref scratchAudit);
                    }

                    var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                        kind,
                        target,
                        address,
                        size,
                        grantRequestCycle,
                        isWrite,
                        out grantedCycle,
                        out completedCycle);
                    if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                    {
                        secondWordCycle = grantedCycle;
                        cpuGrantCommitted = true;
                        deferredPreparationUsed = true;
                    }
                    else if (advanceResult == CpuWaitGrantAdvanceResult.ReferenceContinuation)
                    {
                        _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    }
                    else
                    {
                        _deferredCpuWaitWindowFastPathRejectedUnstable++;
                    }
                }
                else if (Display.HasLiveDisplayWork())
                {
                    var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                        kind,
                        target,
                        address,
                        size,
                        grantRequestCycle,
                        isWrite,
                        out grantedCycle,
                        out completedCycle);
                    if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                    {
                        secondWordCycle = grantedCycle;
                        cpuGrantCommitted = true;
                        deferredPreparationUsed = true;
                    }
                    else if (advanceResult == CpuWaitGrantAdvanceResult.ReferenceContinuation)
                    {
                        _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    }
                    else
                    {
                        _deferredCpuWaitWindowFastPathRejectedUnstable++;
                    }
                }
                else
                {
                    var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                        kind,
                        target,
                        address,
                        size,
                        grantRequestCycle,
                        isWrite,
                        out grantedCycle,
                        out completedCycle);
                    if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                    {
                        secondWordCycle = grantedCycle;
                        cpuGrantCommitted = true;
                        deferredPreparationUsed = true;
                    }
                    else if (advanceResult == CpuWaitGrantAdvanceResult.ReferenceContinuation)
                    {
                        _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    }
                    else
                    {
                        _deferredCpuWaitWindowFastPathRejectedUnstable++;
                    }
                }
            }

            if (!slotContendedClean &&
                !cpuGrantCommitted &&
                _deferredDmaReadsVerifyEnabled &&
                !isWrite)
            {
                verifyDeferredDmaReadOwnership = _hardwareScheduler.TryPredictDeferredReadOwnership(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    out predictedDeferredDmaReadGrant,
                    out predictedDeferredDmaReadCompletion,
                    out predictedDeferredDmaReadTimeline);
            }

            if (!slotContendedClean && !cpuGrantCommitted)
            {
                if (ShouldRunDeferredCpuWaitSlotShadowAudit && !scratchAudit.BlitterAttempted)
                {
                    BeginDeferredCpuWaitScratchAudit(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        grantRequestCycle,
                        isWrite,
                        scratchWrite,
                        ref scratchAudit);
                }

                _hardwareScheduler.DrainForCpuAccess(target, address, grantRequestCycle, isWrite, size);
                AdvanceCpuAccessToCausalBusHorizon(target, ref grantRequestCycle);
            }

            // Slot ownership must be resolved by executing every requester in
            // order.  This is required even when the display is currently
            // quiet: Paula, disk, or the blitter may own an intervening slot.
            // Predict/prepare loops can move the eventual CPU grant behind a
            // value that has already been sampled.
            if (!cpuGrantCommitted &&
                size != AmigaBusAccessSize.Long)
            {
                var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle);
                if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                {
                    secondWordCycle = grantedCycle;
                    cpuGrantCommitted = true;
                }
            }

            if (!cpuGrantCommitted && Blitter.Busy)
            {
                grantRequestCycle = _hardwareScheduler.ExecuteThroughBlitterCpuStall(grantRequestCycle);
                AdvanceCpuAccessToCausalBusHorizon(target, ref grantRequestCycle);
            }

            grantRequestCycle = Math.Max(0, grantRequestCycle);

            if (!cpuGrantCommitted &&
                (size != AmigaBusAccessSize.Long ||
                    !isWrite ||
                    target is not (AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.ExpansionRam)))
            {
                PrepareLiveDisplayBeforeCpuHrmGrantUntilStable(target, address, size, isWrite, kind, grantRequestCycle);
            }

            if (!cpuGrantCommitted)
            {
                GrantExactCpuDataSlots(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }

            RecordDeferredCpuWaitWindow(
                kind,
                target,
                size,
                isWrite,
                requestedCycle,
                grantedCycle);

            if (CopperQuiescentShadowPredictionEnabled)
            {
                _hardwareScheduler.RecordCopperQuiescentCpuSlotPrediction(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    grantedCycle,
                    completedCycle,
                    isWrite);
            }

            if (scratchAudit.HasSupportedScratch)
            {
                CompleteDeferredCpuWaitScratchAudit(
                    in scratchAudit,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    grantRequestCycle,
                    isWrite,
                    grantedCycle,
                    secondWordCycle,
                    completedCycle,
                    _hrmSlotEngine.CaptureTimelineSignature(grantRequestCycle, completedCycle));
            }

            if (grantedCycle > requestedCycle)
            {
                Agnus.RecordCpuChipWaitCycles(grantedCycle - requestedCycle);
            }

            if (synchronizeDmaAfterGrant)
            {
                if (target is not (AmigaBusAccessTarget.ChipRam or
                        AmigaBusAccessTarget.ExpansionRam or
                        AmigaBusAccessTarget.RealTimeClock) ||
                    !_hardwareScheduler.TryCatchUpPreparedCpuGrant(requestedCycle, grantedCycle))
                {
                    AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite);
                }
            }

            if (deferredPreparationUsed)
            {
                RecordDeferredCpuWaitFastPathUse(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    grantRequestCycle,
                    isWrite,
                    grantedCycle,
                    secondWordCycle,
                    completedCycle);
            }

            if (verifyDeferredDmaReadOwnership)
            {
                VerifyDeferredDmaReadOwnership(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    grantedCycle,
                    completedCycle,
                    predictedDeferredDmaReadGrant,
                    predictedDeferredDmaReadCompletion,
                    predictedDeferredDmaReadTimeline);
            }

            chronologicalGrantCommitted = cpuGrantCommitted;

            if (_captureBusAccesses)
            {
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Cpu,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite);
                var access = new AmigaBusAccessResult(request, grantedCycle, completedCycle);
                _busAccesses.Add(access);
                Agnus.RecordCpuChipAccess(access);
                _ = RememberCpuBusAccess(access);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private void GrantExactCpuDataSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            if (size == AmigaBusAccessSize.Long && !isWrite)
            {
                GrantCpuDataLongReadSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
                return;
            }

            if (size == AmigaBusAccessSize.Long &&
                isWrite &&
                target is AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.ExpansionRam)
            {
                GrantCpuDataLongWriteSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
                return;
            }

            if (size == AmigaBusAccessSize.Long)
            {
                _agnusBusExecutor.GrantCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
                return;
            }

            _agnusBusExecutor.GrantCpuDataSingleSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle);
            secondWordCycle = grantedCycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldAttemptDeferredCpuWaitWindowFastPath(long requestedCycle)
        {
            if (!DeferredCpuWaitFastPathEnabled ||
                !_deferredCpuBusBatchEnabled ||
                _forceCpuWaitSlotReference ||
                _deferredCpuBatchExitChipAccessCycle < 0)
            {
                return false;
            }

            if (requestedCycle == _deferredCpuBatchExitChipAccessCycle)
            {
                return true;
            }

            if (requestedCycle > _deferredCpuBatchExitChipAccessCycle)
            {
                _deferredCpuBatchExitChipAccessCycle = -1;
            }

            return false;
        }

        internal void BeginPendingCpuSlotRequest(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
            => _agnusBusExecutor.BeginPendingCpuSlotRequest(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite);

        internal void ClearPendingCpuSlotRequest()
            => _agnusBusExecutor.ClearPendingCpuSlotRequest();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasDynamicCpuWaitSlotWorkThrough(long cycle)
            => Display.HasLiveDisplayWork() ||
                HasNonDisplayDynamicCpuWaitSlotWorkThrough(cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasNonDisplayDynamicCpuWaitSlotWorkThrough(long cycle)
            =>
                Blitter.Busy ||
                Disk.ActiveDma ||
                Disk.HasSlotDmaWakeSourceThrough(cycle) ||
                Paula.HasDmaWorkThrough(cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasUnsupportedCpuWaitSlotWorkThrough(long cycle)
            =>
                (Blitter.Busy && !Blitter.CanUseCpuWaitAreaMicroOps) ||
                Disk.ActiveDma ||
                Disk.HasSlotDmaWakeSourceThrough(cycle) ||
                Paula.HasDmaWorkThrough(cycle);

        internal OcsCpuWaitLiveSlotResult AdvanceCpuWaitLiveSlot(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches,
            out bool completedSafeCopper)
            => Agnus.AdvanceCpuWaitLiveSlotTo(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches,
                out completedSafeCopper);

        internal OcsCpuWaitLiveSlotResult AdvanceOrderedDmaBeforeBlitterSlot(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches,
            out bool advancedPaula,
            out bool advancedDisk)
            => _hardwareScheduler.AdvanceOrderedDmaBeforeBlitterSlot(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches,
                out advancedPaula,
                out advancedDisk);

        internal bool RequiresCanonicalBlitterDisplayPreparation
            => LiveAgnusDmaEnabled && Display.HasLiveDisplayWork();

        internal OcsCpuWaitLiveSlotResult AdvanceBlitterFixedSlot(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches)
            => Agnus.AdvanceBlitterFixedSlotTo(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches);

        internal OcsCpuWaitLiveSlotResult AdvanceFixedDmaBeforeBlitterSlotInScope(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches)
            => _hardwareScheduler.AdvanceFixedDmaBeforeBlitterSlotInScope(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches);

        internal bool TryPredictBlitterFixedSlotGrant(
            uint address,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long firstBlockedCycle,
            out CpuWaitFixedSlotOwner firstBlockedOwner)
        {
            if (!TryPredictBlitterFixedSlotGrantCandidate(
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out firstBlockedCycle,
                    out firstBlockedOwner))
            {
                return false;
            }

            return CanAdvanceBlitterFixedSlotScopeThrough(
                grantedCycle + AgnusChipSlotScheduler.SlotCycles);
        }

        internal bool TryPredictBlitterFixedSlotGrantCandidate(
            uint address,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long firstBlockedCycle,
            out CpuWaitFixedSlotOwner firstBlockedOwner)
        {
            var cursor = default(BlitterFixedSlotImageCursor);
            return TryPredictBlitterFixedSlotGrantCandidate(
                address,
                requestedCycle,
                isWrite,
                ref cursor,
                out grantedCycle,
                out firstBlockedCycle,
                out firstBlockedOwner);
        }

        internal bool TryPredictBlitterFixedSlotGrantCandidate(
            uint address,
            long requestedCycle,
            bool isWrite,
            ref BlitterFixedSlotImageCursor cursor,
            out long grantedCycle,
            out long firstBlockedCycle,
            out CpuWaitFixedSlotOwner firstBlockedOwner)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            grantedCycle = 0;
            firstBlockedCycle = -1;
            firstBlockedOwner = CpuWaitFixedSlotOwner.Free;
            if (_hrmSlotEngine.PendingCpuSlotRequestActive)
            {
                return false;
            }

            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            var hasLiveDisplay = LiveAgnusDmaEnabled && Display.HasLiveDisplayWork();
            for (var attempt = 0; attempt < 512; attempt++, candidate += AgnusChipSlotScheduler.SlotCycles)
            {
                if (IsMandatoryRefreshSlot(candidate))
                {
                    if (firstBlockedCycle < 0)
                    {
                        firstBlockedCycle = candidate;
                        firstBlockedOwner = CpuWaitFixedSlotOwner.Refresh;
                    }

                    continue;
                }

                if (!_hrmSlotEngine.CanReserveBlitterDmaWordAtAlignedWithoutFixedOrPendingCpu(
                        address,
                        requestedCycle,
                        candidate,
                        isWrite))
                {
                    continue;
                }

                if (hasLiveDisplay && !Display.HasLiveDmaCapturedThrough(candidate))
                {
                    if (!Display.TryGetBlitterFixedDisplaySlotOwner(
                            candidate,
                            ref cursor,
                            out var owner,
                            out _))
                    {
                        return false;
                    }

                    if (owner != CpuWaitFixedSlotOwner.Free)
                    {
                        if (firstBlockedCycle < 0)
                        {
                            firstBlockedCycle = candidate;
                            firstBlockedOwner = owner;
                        }

                        continue;
                    }
                }

                grantedCycle = candidate;
                return true;
            }

            return false;
        }

        internal bool CanAdvanceBlitterFixedSlotScopeThrough(long targetCycle)
            => !HasBlitterDynamicDmaWorkThrough(targetCycle) &&
                CanAdvanceBlitterFixedDisplayScopeThrough(targetCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasBlitterDynamicDmaWorkThrough(long targetCycle)
            => Disk.ActiveDma ||
                Disk.HasSlotDmaWakeSourceThrough(targetCycle) ||
                Paula.HasDmaWorkThrough(targetCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool CanAdvanceBlitterFixedDisplayScopeThrough(long targetCycle)
            => !LiveAgnusDmaEnabled ||
                Display.GetNextBlitterFixedSlotBarrierCycle(targetCycle) > targetCycle;

        internal string DescribeBlitterFixedSlotPrediction(long cycle)
        {
            _hrmSlotEngine.TryGetCommittedSlotOwner(cycle, out var committedOwner);
            var hasFixedOwner = Display.TryGetCpuWaitFixedSlotOwner(
                cycle,
                out var fixedOwner,
                out var unsupported);
            return $"slot={cycle},hrm={committedOwner},fixed={(hasFixedOwner ? fixedOwner : CpuWaitFixedSlotOwner.Free)},unsupported={unsupported}";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SynchronizeHrmBlitterPriority()
            => _agnusBusExecutor.SetBlitterPriority((Paula.Dmacon & 0x0400) != 0);

        internal bool TryGrantPendingCpuSingleSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            out long completedCycle)
            => _agnusBusExecutor.TryGrantCpuDataSingleExactSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                slotCycle,
                isWrite,
                allowNiceBlitterSteal: true,
                out completedCycle);

        internal void CaptureCpuWaitFixedImageDisplayDma(long grantCycle)
            => Display.CaptureLiveDisplayDmaBeforeHrmGrant(grantCycle);

        internal void PrepareCpuWaitLiveDisplaySlots(long grantCycle)
            => Display.PrepareLiveDisplaySlotsBeforeHrmGrant(grantCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long AdvancePendingCpuGrantToCausalBusHorizon(
            AmigaBusAccessTarget target,
            long cycle)
        {
            AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
            return cycle;
        }

        internal bool TryPredictCpuWaitFixedSlotGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotImageUnsupported unsupported)
            => TryPredictCpuWaitFixedSlotGrantCore(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle,
                out unsupported,
                out _,
                captureTimeline: false,
                allowBlankingFrameFallback: false);

        internal bool TryPredictCpuWaitFixedSlotGrantAcrossPhysicalFrame(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotImageUnsupported unsupported)
            => TryPredictCpuWaitFixedSlotGrantCore(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle,
                out unsupported,
                out _,
                captureTimeline: false,
                allowBlankingFrameFallback: true);

        internal bool TryPredictCpuWaitFixedSlotGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotImageUnsupported unsupported,
            out CpuWaitFixedSlotTimelineSignature timeline)
            => TryPredictCpuWaitFixedSlotGrantCore(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle,
                out unsupported,
                out timeline,
                captureTimeline: true,
                allowBlankingFrameFallback: false);

        private bool TryPredictCpuWaitFixedSlotGrantCore(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotImageUnsupported unsupported,
            out CpuWaitFixedSlotTimelineSignature timeline,
            bool captureTimeline,
            bool allowBlankingFrameFallback)
        {
            grantedCycle = 0;
            completedCycle = 0;
            unsupported = CpuWaitFixedSlotImageUnsupported.None;
            timeline = default;
            if (size == AmigaBusAccessSize.Long ||
                target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock))
            {
                unsupported = CpuWaitFixedSlotImageUnsupported.Frame;
                return false;
            }

            var searchCycle = Math.Max(0, requestedCycle);
            for (var attempt = 0; attempt < 512; attempt++)
            {
                if (!_hrmSlotEngine.TryPredictCpuDataSingleSlotFrom(
                        kind,
                        target,
                        address,
                        size,
                        searchCycle,
                        requestedCycle,
                        isWrite,
                        out var candidate))
                {
                    return false;
                }

                CpuWaitFixedSlotOwner owner;
                if (IsMandatoryRefreshSlot(candidate))
                {
                    owner = CpuWaitFixedSlotOwner.Refresh;
                }
                else if (!Display.TryGetBlitterFixedDisplaySlotOwner(
                             candidate,
                             ref _cpuGrantFixedSlotImageCursor,
                             out owner,
                             out unsupported))
                {
                    if (allowBlankingFrameFallback &&
                        unsupported == CpuWaitFixedSlotImageUnsupported.Frame &&
                        Display.CanPredictCpuWaitBlankingSlotAsFree(
                            candidate,
                            candidate))
                    {
                        owner = CpuWaitFixedSlotOwner.Free;
                        unsupported = CpuWaitFixedSlotImageUnsupported.None;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (owner == CpuWaitFixedSlotOwner.Free)
                {
                    grantedCycle = candidate;
                    completedCycle = candidate + AgnusChipSlotScheduler.SlotCycles;
                    return !captureTimeline || TryCaptureCpuWaitFixedSlotTimeline(
                        requestedCycle,
                        completedCycle,
                        grantedCycle,
                        predicted: true,
                        allowBlankingFrameFallback,
                        out timeline,
                        out unsupported);
                }

                searchCycle = candidate + AgnusChipSlotScheduler.SlotCycles;
            }

            grantedCycle = 0;
            completedCycle = 0;
            unsupported = CpuWaitFixedSlotImageUnsupported.Unstable;
            timeline = default;
            return false;
        }

        private bool TryCaptureCpuWaitFixedSlotTimeline(
            long requestedCycle,
            long completedCycle,
            long cpuGrantCycle,
            bool predicted,
            out CpuWaitFixedSlotTimelineSignature timeline,
            out CpuWaitFixedSlotImageUnsupported unsupported)
            => TryCaptureCpuWaitFixedSlotTimeline(
                requestedCycle,
                completedCycle,
                cpuGrantCycle,
                predicted,
                allowBlankingFrameFallback: false,
                out timeline,
                out unsupported);

        private bool TryCaptureCpuWaitFixedSlotTimeline(
            long requestedCycle,
            long completedCycle,
            long cpuGrantCycle,
            bool predicted,
            bool allowBlankingFrameFallback,
            out CpuWaitFixedSlotTimelineSignature timeline,
            out CpuWaitFixedSlotImageUnsupported unsupported)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            var count = 0;
            var firstCycle = 0L;
            var lastCycle = 0L;
            var firstOwner = AgnusChipSlotOwner.Free;
            var lastOwner = AgnusChipSlotOwner.Free;
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle));
            var lastCompletedCycle = Math.Max(0, completedCycle - 1);
            var lastCandidate = lastCompletedCycle -
                (lastCompletedCycle % AgnusChipSlotScheduler.SlotCycles);
            for (; slotCycle <= lastCandidate; slotCycle += AgnusChipSlotScheduler.SlotCycles)
            {
                AgnusChipSlotOwner owner;
                if (slotCycle == cpuGrantCycle)
                {
                    owner = AgnusChipSlotOwner.Cpu;
                }
                else if (!_hrmSlotEngine.TryGetCommittedSlotOwner(slotCycle, out owner))
                {
                    if (!predicted)
                    {
                        owner = AgnusChipSlotOwner.Free;
                    }
                    else if (!Display.TryGetCpuWaitFixedSlotOwner(slotCycle, out var fixedOwner, out unsupported))
                    {
                        if (allowBlankingFrameFallback &&
                            unsupported == CpuWaitFixedSlotImageUnsupported.Frame &&
                            Display.CanPredictCpuWaitBlankingSlotAsFree(
                                slotCycle,
                                lastCandidate))
                        {
                            owner = AgnusChipSlotOwner.Free;
                        }
                        else
                        {
                            timeline = default;
                            return false;
                        }
                    }
                    else
                    {
                        owner = fixedOwner switch
                        {
                            CpuWaitFixedSlotOwner.Refresh => AgnusChipSlotOwner.Refresh,
                            CpuWaitFixedSlotOwner.BitplaneRead => AgnusChipSlotOwner.Bitplane,
                            CpuWaitFixedSlotOwner.SpriteRead => AgnusChipSlotOwner.Sprite,
                            _ => AgnusChipSlotOwner.Free
                        };
                    }
                }

                if (count == 0)
                {
                    firstCycle = slotCycle;
                    firstOwner = owner;
                }

                lastCycle = slotCycle;
                lastOwner = owner;
                count++;
                hash = (hash ^ unchecked((ulong)slotCycle)) * prime;
                hash = (hash ^ (ulong)owner) * prime;
            }

            unsupported = CpuWaitFixedSlotImageUnsupported.None;
            timeline = new CpuWaitFixedSlotTimelineSignature(
                count,
                hash,
                firstCycle,
                lastCycle,
                firstOwner,
                lastOwner);
            return true;
        }

        private bool TryPredictCpuGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            if (size == AmigaBusAccessSize.Long)
            {
                return _hrmSlotEngine.TryPredictCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }

            secondWordCycle = 0;
            completedCycle = 0;
            if (!_hrmSlotEngine.TryPredictCpuDataSingleSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle))
            {
                return false;
            }

            secondWordCycle = grantedCycle;
            completedCycle = grantedCycle + AgnusChipSlotScheduler.SlotCycles;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private void PrepareLiveDisplayBeforeCpuHrmGrantUntilStable(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            AmigaBusAccessKind kind,
            long grantRequestCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                (size != AmigaBusAccessSize.Byte &&
                    size != AmigaBusAccessSize.Word &&
                    size != AmigaBusAccessSize.Long) ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            var prepareCycle = grantRequestCycle;
            var lastGrant = -1L;
            var lastSecondWord = -1L;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(target, size, isWrite, prepareCycle);
                if (!TryPredictCpuGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out var predictedGrant,
                    out var predictedSecondWord,
                    out _))
                {
                    return;
                }

                if (predictedGrant == lastGrant &&
                    predictedSecondWord == lastSecondWord)
                {
                    return;
                }

                var nextPrepareCycle = size == AmigaBusAccessSize.Long
                    ? predictedSecondWord
                    : predictedGrant;
                if (nextPrepareCycle <= prepareCycle)
                {
                    return;
                }

                lastGrant = predictedGrant;
                lastSecondWord = predictedSecondWord;
                prepareCycle = nextPrepareCycle;
            }
        }

        private bool TryPrepareLiveDisplayBeforeCpuHrmGrantFromFixedSlotImage(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            AmigaBusAccessKind kind,
            long grantRequestCycle)
        {
            // Fixed display ownership can replace the prepare/predict fixed-point
            // loop only while no device with movable ownership can enter the line.
            // Those paths retain canonical gatling-order preparation.
            if (size is not (AmigaBusAccessSize.Byte or AmigaBusAccessSize.Word) ||
                target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock) ||
                Display.HasLiveSpriteDmaWork() ||
                HasPreparedCpuGrantDynamicDmaBarrier(grantRequestCycle) ||
                !TryPredictCpuWaitFixedSlotGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out var predictedGrant,
                    out _,
                    out _))
            {
                return false;
            }

            Display.CaptureLiveDisplayDmaBeforeHrmGrant(predictedGrant);
            return TryPredictCpuGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out var committedGrant,
                    out _,
                    out _) &&
                committedGrant == predictedGrant;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasPreparedCpuGrantDynamicDmaBarrier(long grantRequestCycle)
        {
            if (grantRequestCycle <= _cpuGrantDynamicDmaRejectSegmentEnd)
            {
                return true;
            }

            if (!HasNonDisplayDynamicCpuWaitSlotWorkThrough(grantRequestCycle + LineCycles))
            {
                return false;
            }

            var line = Math.Max(0, grantRequestCycle) / LineCycles;
            _cpuGrantDynamicDmaRejectSegmentEnd = ((line + 1) * LineCycles) - 1;
            return true;
        }



        private void GrantCpuDataLongReadSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                requestedCycle,
                requestedCycle,
                isWrite: false,
                out grantedCycle,
                out var firstCompletedCycle);
            PrepareLiveDisplayBeforeCpuLongReadPhaseUntilStable(
                target,
                address,
                kind,
                requestedCycle,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles);
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles,
                requestedCycle,
                isWrite: false,
                out secondWordCycle,
                out completedCycle);
        }

        private void GrantCpuDataLongWriteSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            PrepareLiveDisplayBeforeCpuLongWritePhaseUntilStable(
                target,
                address,
                kind,
                requestedCycle,
                requestedCycle);
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                requestedCycle,
                requestedCycle,
                isWrite: true,
                out grantedCycle,
                out var firstCompletedCycle);
            PrepareLiveDisplayBeforeCpuLongWritePhaseUntilStable(
                target,
                address,
                kind,
                requestedCycle,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles);
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles,
                requestedCycle,
                isWrite: true,
                out secondWordCycle,
                out completedCycle);
        }



        private void PrepareLiveDisplayBeforeCpuLongReadPhaseUntilStable(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessKind kind,
            long requestedCycle,
            long phaseSearchCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            var prepareCycle = phaseSearchCycle;
            var lastGrant = -1L;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(
                    target,
                    AmigaBusAccessSize.Long,
                    isWrite: false,
                    prepareCycle);
                if (!_hrmSlotEngine.TryPredictCpuDataLongWordPhaseSlot(
                        kind,
                        target,
                        address,
                        prepareCycle,
                        requestedCycle,
                        isWrite: false,
                        out var predictedGrant))
                {
                    return;
                }

                if (predictedGrant == lastGrant ||
                    predictedGrant <= prepareCycle)
                {
                    return;
                }

                lastGrant = predictedGrant;
                prepareCycle = predictedGrant;
            }
        }

        private void PrepareLiveDisplayBeforeCpuLongWritePhaseUntilStable(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessKind kind,
            long requestedCycle,
            long phaseSearchCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            var prepareCycle = phaseSearchCycle;
            var lastGrant = -1L;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(
                    target,
                    AmigaBusAccessSize.Long,
                    isWrite: true,
                    prepareCycle);
                if (!_hrmSlotEngine.TryPredictCpuDataLongWordPhaseSlot(
                        kind,
                        target,
                        address,
                        prepareCycle,
                        requestedCycle,
                        isWrite: true,
                        out var predictedGrant))
                {
                    return;
                }

                if (predictedGrant == lastGrant ||
                    predictedGrant <= prepareCycle)
                {
                    return;
                }

                lastGrant = predictedGrant;
                prepareCycle = predictedGrant;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private void PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(
            AmigaBusAccessTarget target,
            AmigaBusAccessSize size,
            bool isWrite,
            long grantCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                (size != AmigaBusAccessSize.Byte &&
                    size != AmigaBusAccessSize.Word &&
                    size != AmigaBusAccessSize.Long) ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters && !isWrite)
            {
                Display.PrepareLiveDisplaySlotsBeforeHrmGrant(grantCycle);
                return;
            }

            Display.CaptureLiveDisplayDmaBeforeHrmGrant(grantCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepareLiveDisplaySlotsBeforeDmaGrant(
            AmigaBusAccessSize size,
            long grantCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                (size != AmigaBusAccessSize.Byte &&
                    size != AmigaBusAccessSize.Word &&
                    size != AmigaBusAccessSize.Long) ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            Display.PrepareLiveDisplaySlotsBeforeHrmGrant(grantCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataByte(
            in AmigaExactCpuDataRamRegion region,
            byte value,
            long grantedCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                RememberChipDataBusByte(value, grantedCycle, wasDma: false);
                _chipRam.WriteByteAtOffset(region.Offset, value, grantedCycle);
                return;
            }

            region.Memory[region.Offset] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataWord(
            in AmigaExactCpuDataRamRegion region,
            ushort value,
            long grantedCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                RememberChipDataBusWord(value, grantedCycle, wasDma: false);
                _chipRam.WriteContiguousWordAtOffset(region.Offset, value, grantedCycle);
                return;
            }

            region.Memory[region.Offset] = (byte)(value >> 8);
            region.Memory[region.Offset + 1] = (byte)value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataLong(
            in AmigaExactCpuDataRamRegion region,
            uint value,
            long firstWordCycle,
            long secondWordCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                RememberChipDataBusWord((ushort)(value >> 16), firstWordCycle, wasDma: false);
                RememberChipDataBusWord((ushort)value, secondWordCycle, wasDma: false);
                _chipRam.WriteContiguousLongAtOffset(region.Offset, value, firstWordCycle, secondWordCycle);
                return;
            }

            region.Memory[region.Offset] = (byte)(value >> 24);
            region.Memory[region.Offset + 1] = (byte)(value >> 16);
            region.Memory[region.Offset + 2] = (byte)(value >> 8);
            region.Memory[region.Offset + 3] = (byte)value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGrantFastCpuAccessCycle(
            AmigaBusAccessTarget target,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle)
        {
            if (!isWrite &&
                (target == AmigaBusAccessTarget.ChipRam || target == AmigaBusAccessTarget.ExpansionRam) &&
                _hardwareScheduler.IsSlotContendedCleanThrough(requestedCycle) &&
                !Blitter.Busy &&
                (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target)))
            {
                grantedCycle = Math.Max(0, requestedCycle);
                return true;
            }

            grantedCycle = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GrantFastCpuAccessCycles(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            if (TryGrantFastCpuAccessCycle(target, requestedCycle, isWrite, out var fastCycle))
            {
                grantedCycle = fastCycle;
                completedCycle = fastCycle;
                secondWordCycle = fastCycle;
                return;
            }

            GrantCpuAccessSlowCycles(
                target,
                address,
                size,
                requestedCycle,
                kind,
                isWrite,
                out grantedCycle,
                out secondWordCycle,
                out completedCycle);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrantCpuAccessSlowCycles(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            var originalRequestedCycle = requestedCycle;
            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock ||
                target == AmigaBusAccessTarget.CustomRegisters)
            {
                _hardwareScheduler.DrainForCpuAccess(target, address, requestedCycle, isWrite, size);
                if (Blitter.Busy)
                {
                    requestedCycle = _hardwareScheduler.ExecuteThroughBlitterCpuStall(requestedCycle);
                }

                AdvanceCpuAccessToCausalBusHorizon(target, ref requestedCycle);
            }

            requestedCycle = Math.Max(0, requestedCycle);
            if (target == AmigaBusAccessTarget.Cia)
            {
                grantedCycle = CiaPeripheralAccessTiming.AlignToCiaPeripheralAccessCycle(requestedCycle);
                completedCycle = grantedCycle;
                secondWordCycle = GetCpuSecondWordCycle(size, grantedCycle, completedCycle);
                return;
            }

            if (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target))
            {
                grantedCycle = requestedCycle;
                completedCycle = requestedCycle;
                secondWordCycle = GetCpuSecondWordCycle(size, grantedCycle, completedCycle);
                return;
            }

            _agnusBusExecutor.SetBlitterPriority((Paula.Dmacon & 0x0400) != 0);
			PrepareLiveDisplayBeforeCpuHrmGrantUntilStable(
				target,
				address,
				size,
				isWrite,
				kind,
				requestedCycle);

            if (size == AmigaBusAccessSize.Long)
            {
                _agnusBusExecutor.GrantCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }
            else
            {
                _agnusBusExecutor.GrantCpuDataSingleSlot(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle);
                secondWordCycle = grantedCycle;
            }

            if (CopperQuiescentShadowPredictionEnabled)
            {
                _hardwareScheduler.RecordCopperQuiescentCpuSlotPrediction(
                    kind,
                    target,
                    address,
                    size,
                    originalRequestedCycle,
                    grantedCycle,
                    completedCycle,
                    isWrite);
            }

            Agnus.RecordCpuChipWaitCycles(grantedCycle - originalRequestedCycle);
            RecordDeferredCpuWaitWindow(
                kind,
                target,
                size,
                isWrite,
                originalRequestedCycle,
                grantedCycle);
        }



    }
}
