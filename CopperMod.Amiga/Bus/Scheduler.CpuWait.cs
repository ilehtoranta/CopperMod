/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Bus
{
    internal enum CpuWaitGrantAdvanceResult : byte
    {
        Unsupported,
        Granted,
        ReferenceContinuation,
        InterruptBoundary
    }

    internal enum CpuWaitFixedImageProductionFallback : byte
    {
        None,
        Unsupported,
        DynamicDma,
        Frame,
        Copper,
        PendingWrite,
        RasterlinePlan,
        SpriteState,
        Unstable
    }

    internal sealed partial class Scheduler
    {
        private const int M68000InterruptSetupCycles = 4;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool AdvanceCpuTimingSequence(
            in CpuTimingSequenceRequest request,
            out CpuTimingSequenceResult result)
        {
            if (!_bus.CausalBusExecutor.TryExecuteCpuTimingSequence(request, out result))
            {
                return false;
            }

            MarkClean(result.CleanThroughCycle, SlotContendedMemoryAccessMask);
            return true;
        }

        internal void RecordDeferredCpuWaitBlitterOverlap(bool supported, bool nasty)
        {
            _deferredCpuWaitBlitterOverlapAttempts++;
            if (supported)
            {
                _deferredCpuWaitBlitterOverlapSupported++;
            }
            else
            {
                _deferredCpuWaitBlitterOverlapUnsupported++;
            }

            if (nasty)
            {
                _deferredCpuWaitBlitterOverlapNasty++;
            }
        }

        internal void SetCpuWaitSlotContendedCleanThroughForTest(long cycle)
            => _slotContendedCleanThroughCycle = cycle;

        internal void SetCpuWaitPreviouslyDrainedThroughForTest(long cycle)
        {
            cycle = Math.Max(0, cycle);
            _hasDrained = true;
            _lastDrainCycle = cycle;
            _paulaDmaDrainCycle = cycle;
            _diskEventDrainCycle = cycle;
            _agnusDrainCycle = cycle;
            _blitterDrainCycle = cycle;
            _earliestDirtyCycle = long.MaxValue;
            _lastCleanGeneration = _generation;
            _slotContendedCleanThroughCycle = cycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long ExecuteThroughBlitterCpuStall(long requestedCycle)
        {
            if (!_bus.Blitter.CpuStallActive)
            {
                return requestedCycle;
            }

            var releaseCycle = _bus.Blitter.CpuStallReleaseCycle;
            SynchronizeBlitterThrough(releaseCycle);
            return Math.Max(requestedCycle, _bus.Blitter.CurrentCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CpuWaitGrantAdvanceResult AdvanceUntilCpuGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle)
            => AdvanceUntilCpuGrantCore(
                kind,
                target,
                address,
                size,
                requestedCycle,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle);

        internal CpuWaitGrantAdvanceResult AdvanceUntilCpuGrantOrInterrupt(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            int cpuInterruptMask,
            out long grantedCycle,
            out long completedCycle)
            => AdvanceUntilCpuGrantCore(
                kind,
                target,
                address,
                size,
                requestedCycle,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle,
                cpuInterruptMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal CpuWaitGrantAdvanceResult AdvanceUntilCpuLongWordPhaseGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long searchCycle,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle)
            => AdvanceUntilCpuGrantCore(
                kind,
                target,
                address,
                AmigaBusAccessSize.Word,
                searchCycle,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle);

        private CpuWaitGrantAdvanceResult AdvanceUntilCpuGrantCore(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long searchCycle,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            int cpuInterruptMask = -1)
        {
            grantedCycle = 0;
            completedCycle = 0;
			if (_draining ||
				size == AmigaBusAccessSize.Long ||
				target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock or
					AmigaBusAccessTarget.CustomRegisters))
            {
                return CpuWaitGrantAdvanceResult.Unsupported;
            }

            requestedCycle = Math.Max(0, requestedCycle);
            searchCycle = Math.Max(requestedCycle, searchCycle);
            var entryExecutorHorizon =
                _bus.CausalBusExecutor.ExecutedThroughCycle;

            var firstCandidateCycle = searchCycle;
            if (_bus.Blitter.CpuStallActive &&
                !_bus.AgnusLiveBlitterEnabled)
            {
                firstCandidateCycle =
                    ExecuteThroughBlitterCpuStall(requestedCycle);
            }

            // An expansion/RTC/custom-register access can enter this shared
            // slot path after another requester has already advanced Agnus
            // past the CPU's nominal request cycle. Never drain, publish, or
            // arbitrate a retroactive slot; start after the executed horizon.
            // Equality remains legal because the horizon can identify the
            // current unresolved memory cycle rather than a consumed slot.
            if (_bus.AgnusLiveBlitterEnabled &&
                firstCandidateCycle < entryExecutorHorizon)
            {
                firstCandidateCycle = entryExecutorHorizon + 1;
            }

            // Hardware before the CPU request cannot observe the pending request.
            if (firstCandidateCycle > 0)
            {
                if (_bus.AgnusLiveBlitterEnabled && _bus.Blitter.BusPipelineActive)
                {
                    var blitterCycle = _bus.Blitter.GetRawBusEligibilityCycle();
                    if (blitterCycle < firstCandidateCycle)
                    {
                        // A preceding drain can mark its full target clean even
                        // when committing one blitter micro-op publishes another
                        // word inside that target. Reassert the raw deadline at
                        // the CPU boundary so older blitter work is settled before
                        // the CPU request becomes visible.
                        NotifyWorkScheduled(blitterCycle);
                    }
                }

                _bus.CausalBusExecutor.BeginCpuPreGrantDrain();
                try
                {
                    if (_bus.AgnusLiveBlitterEnabled &&
                        !_bus.AgnusSlotKernelSelected)
                    {
                        // The generic drain may visit a control edge and then
                        // mark through the following free memory slot before a
                        // newly published live word is re-queried. Walk only
                        // the requester slots older than this CPU request so
                        // they arbitrate chronologically while the CPU remains
                        // invisible.
                        for (var step = 0; step < 16; step++)
                        {
                            if (!_bus.Blitter.BusPipelineActive)
                            {
                                break;
                            }

                            var rawBlitterCycle =
                                _bus.Blitter.GetRawBusEligibilityCycle();
                            var blitterSlot =
                                _bus.Blitter.NormalizeRawBusEligibilityCycle(
                                    rawBlitterCycle,
                                    _bus.CausalBusExecutor.ExecutedThroughCycle);
                            if (blitterSlot >=
                                firstCandidateCycle -
                                AgnusChipSlotScheduler.SlotCycles)
                            {
                                break;
                            }

                            if (blitterSlot > 0)
                            {
                                DrainSlotContendedAccess(blitterSlot - 1);
                            }

                            _bus.AdvanceDueLiveFixedRequestersTo(blitterSlot);
                            _bus.PrepareCpuWaitLiveDisplaySlots(blitterSlot);
                            _bus.Display.AdvanceLiveAgnusSlotKernelTo(blitterSlot);
                            _bus.Blitter.AdvanceLiveAgnusCpuPreGrantTo(blitterSlot);
                        }
                    }

                    DrainSlotContendedAccess(firstCandidateCycle - 1);
                }
                finally
                {
                    _bus.CausalBusExecutor.EndCpuPreGrantDrain();
                }
            }

            _busAccessDrainCount++;
            _bus.BeginPendingCpuSlotRequest(kind, target, address, size, requestedCycle, isWrite);
            try
            {
                var interruptible = cpuInterruptMask >= 0;

                if (searchCycle > requestedCycle)
                {
                    // A 68000 longword keeps the CPU bus request pending across
                    // the mandatory inter-phase memory cycle. That already
                    // committed slot contributes to the three-cycle nice
                    // blitter quota before the second word can arbitrate.
                    _bus.CausalBusExecutor.ObservePendingCpuDmaCycle(
                        AgnusChipSlotScheduler.AlignToSlot(searchCycle) -
                        AgnusChipSlotScheduler.SlotCycles);
                }

                var candidate = AgnusChipSlotScheduler.AlignToSlot(firstCandidateCycle);
                while (true)
                {
                    if (interruptible &&
                        TryGetCpuRecognitionEligibleInterruptCycle(
                            cpuInterruptMask,
                            candidate,
                            out var interruptBoundaryCycle))
                    {
                        DrainSlotContendedAccess(interruptBoundaryCycle);
                        completedCycle = interruptBoundaryCycle;
                        return CpuWaitGrantAdvanceResult.InterruptBoundary;
                    }

                    if (_bus.Display.HasLiveDisplayWork())
                    {
                        // Every CPU target using this exact causal grant path
                        // must see the unexecuted fixed-display/Copper suffix
                        // before HRM considers the candidate slot. Chip-RAM
                        // writes are not special: committing one first and
                        // allowing Copper or bitplane DMA to replace it later
                        // changes the physical bus chronology. This reserves
                        // ownership only; memory is sampled by chronological
                        // execution below.
                        var resumeCpuPublication =
                            _bus.CausalBusExecutor
                                .SuspendPendingCpuPublicationForFixedPreparation();
                        try
                        {
                            _bus.PrepareCpuWaitLiveDisplaySlots(candidate);
                        }
                        finally
                        {
                            if (resumeCpuPublication)
                            {
                                _bus.CausalBusExecutor
                                    .ResumePendingCpuPublicationAfterFixedPreparation();
                            }
                        }
                    }

                    // A candidate CPU slot is only usable after every older
                    // device event has executed.  Driving Denise to the
                    // candidate first can sample a later display word before
                    // an older blitter/Paula access.  Keep the pending CPU
                    // request visible to arbitration and let the chronological
                    // scheduler execute the whole interval.
                    if (_bus.CausalBusExecutor.TryAdvanceCpuOnlySlot(candidate))
                    {
                        MarkClean(candidate, SlotContendedMemoryAccessMask);
                    }
                    else
                    {
                        DrainSlotContendedAccess(candidate);
                    }

                    var causalCandidate =
                        _bus.AdvancePendingCpuGrantToCausalBusHorizon(
                            target,
                            candidate);
                    if (causalCandidate != candidate)
                    {
                        _bus.CausalBusExecutor.ObservePendingCpuDmaCycle(
                            candidate);
                        candidate = AgnusChipSlotScheduler.AlignToSlot(
                            causalCandidate);
                        continue;
                    }

                    if (_bus.AgnusLiveBlitterEnabled)
                    {
                        _bus.CausalBusExecutor
                            .ExecuteEligibleAtPendingCpuBoundary(candidate);
                    }

                    // The HRM nice-blitter rule counts every memory cycle for
                    // which the pending CPU request remains unsatisfied, not
                    // only blitter-owned cycles. Record the committed owner
                    // before its completion horizon moves the retry forward.
                    _bus.CausalBusExecutor.ObservePendingCpuDmaCycle(candidate);

                    // A competing requester may have committed the candidate
                    // while the CPU intent remained pending.  Its completion
                    // advances the data-bus horizon beyond that candidate, so
                    // retry from the first causally usable CPU cycle instead of
                    // attempting to grant the already-executed slot again.
                    causalCandidate = _bus.AdvancePendingCpuGrantToCausalBusHorizon(
                        target,
                        candidate);
                    if (causalCandidate != candidate)
                    {
                        candidate = AgnusChipSlotScheduler.AlignToSlot(causalCandidate);
                        continue;
                    }

                    _bus.SynchronizeHrmBlitterPriority();
                    if (_bus.TryGrantPendingCpuSingleSlot(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        candidate,
                        isWrite,
                        out completedCycle))
                    {
                        if (_bus.AgnusLiveBlitterEnabled &&
                            !_bus.AgnusSlotKernelSelected)
                        {
                            _bus.ObserveLiveSlotKernelCpuGrant(candidate);
                        }
                        grantedCycle = candidate;
                        return CpuWaitGrantAdvanceResult.Granted;
                    }

                    // A Copper transfer occupies one memory cycle. The adjacent
                    // cycle remains a legal CPU opportunity.
                    candidate += AgnusChipSlotScheduler.SlotCycles;
                }
            }
            finally
            {
                _bus.ClearPendingCpuSlotRequest();
            }
        }

        private bool TryGetCpuRecognitionEligibleInterruptCycle(
            int cpuInterruptMask,
            long candidateCycle,
            out long recognitionCycle)
        {
            recognitionCycle = 0;
            var level = _bus.Paula.GetHighestCpuVisibleInterruptLevel(candidateCycle);
            if (level <= 0 || level <= (cpuInterruptMask & 0x07))
            {
                return false;
            }

            var pinAssertCycle =
                _bus.Paula.GetCpuInterruptReleaseCycleForLevel(level, candidateCycle);
            if (!pinAssertCycle.HasValue)
            {
                return false;
            }

            // A transition exactly four CPU clocks before the poll is staged
            // until the next poll. The first eligible boundary is therefore
            // assertion + setup + one clock.
            recognitionCycle = pinAssertCycle.Value + M68000InterruptSetupCycles + 1;
            return recognitionCycle <= candidateCycle;
        }


        internal CpuWaitGrantAdvanceResult AdvanceUntilCpuGrantUsingFixedSlotImage(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle)
        {
            _bus.RecordProductionCpuWaitFixedSlotImageAttempt();
            grantedCycle = 0;
            completedCycle = 0;
            if (_draining ||
                _bus.DeferredCpuWaitFixedImageProductionDisabled ||
                size == AmigaBusAccessSize.Long ||
                target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock))
            {
                _bus.RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.Unsupported);
                return CpuWaitGrantAdvanceResult.Unsupported;
            }

            requestedCycle = Math.Max(0, requestedCycle);
            if (requestedCycle > 0)
            {
                DrainSlotContendedAccess(requestedCycle - 1);
            }

            if (!_bus.Display.HasLiveDisplayWork() ||
                _bus.HasNonDisplayDynamicCpuWaitSlotWorkThrough(requestedCycle + _bus.LineCycles))
            {
                _bus.RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.DynamicDma);
                return CpuWaitGrantAdvanceResult.ReferenceContinuation;
            }

            CpuWaitFixedSlotImageUnsupported unsupported;
            var supported = _bus.TryPredictCpuWaitFixedSlotGrant(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle,
                out unsupported);
            if (!supported)
            {
                _bus.RecordProductionCpuWaitFixedSlotImageFallback(unsupported switch
                {
                    CpuWaitFixedSlotImageUnsupported.Frame => CpuWaitFixedImageProductionFallback.Frame,
                    CpuWaitFixedSlotImageUnsupported.Copper => CpuWaitFixedImageProductionFallback.Copper,
                    CpuWaitFixedSlotImageUnsupported.PendingWrite => CpuWaitFixedImageProductionFallback.PendingWrite,
                    CpuWaitFixedSlotImageUnsupported.RasterlinePlan => CpuWaitFixedImageProductionFallback.RasterlinePlan,
                    CpuWaitFixedSlotImageUnsupported.SpriteState => CpuWaitFixedImageProductionFallback.SpriteState,
                    CpuWaitFixedSlotImageUnsupported.Unstable => CpuWaitFixedImageProductionFallback.Unstable,
                    _ => CpuWaitFixedImageProductionFallback.Unsupported
                });
                return CpuWaitGrantAdvanceResult.ReferenceContinuation;
            }

            var predictedGrant = grantedCycle;
            _bus.CaptureCpuWaitFixedImageDisplayDma(predictedGrant);
            if (!_bus.TryPredictCpuWaitFixedSlotGrant(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle,
                    out _) ||
                grantedCycle != predictedGrant)
            {
                _bus.RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.Unstable);
                return CpuWaitGrantAdvanceResult.ReferenceContinuation;
            }

            _busAccessDrainCount++;
            _draining = true;
            _bus.BeginPendingCpuSlotRequest(kind, target, address, size, requestedCycle, isWrite);
            try
            {
                if (!_bus.TryGrantPendingCpuSingleSlot(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        grantedCycle,
                        isWrite,
                        out var committedCompletion))
                {
                    _bus.RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.Unstable);
                    return CpuWaitGrantAdvanceResult.Unsupported;
                }

                completedCycle = committedCompletion;
                _bus.RecordProductionCpuWaitFixedSlotImageUse(requestedCycle, grantedCycle);
                return CpuWaitGrantAdvanceResult.Granted;
            }
            finally
            {
                _bus.ClearPendingCpuSlotRequest();
                _draining = false;
            }
        }

        internal bool TryCatchUpPreparedCpuGrant(long requestedCycle, long grantedCycle)
        {
            if (_draining)
            {
                return false;
            }

            if (IsSlotContendedCleanThrough(grantedCycle))
            {
                return true;
            }

            // The grant is already present in the HRM slot table, so a normal
            // drain will preserve it while executing all preceding DMA in
            // causal order.  Do not fast-forward Agnus and then replay the
            // other requesters behind it.
            DrainSlotContendedAccess(grantedCycle);
            return IsSlotContendedCleanThrough(grantedCycle);
        }

        internal bool TryPredictDeferredReadOwnership(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotTimelineSignature timeline)
        {
            _bus.RecordProductionCpuWaitFixedSlotImageAttempt();
            grantedCycle = 0;
            completedCycle = 0;
            timeline = default;
            if (_draining ||
                _bus.DeferredCpuWaitFixedImageProductionDisabled ||
                size == AmigaBusAccessSize.Long ||
                target is not (AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.ExpansionRam))
            {
                _bus.RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.Unsupported);
                return false;
            }

            requestedCycle = Math.Max(0, requestedCycle);
            if (!_bus.Display.HasLiveDisplayWork() ||
                _bus.Display.HasLiveSpriteDmaWork() ||
                _bus.HasNonDisplayDynamicCpuWaitSlotWorkThrough(requestedCycle + _bus.LineCycles))
            {
                _bus.RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.DynamicDma);
                return false;
            }

            if (_bus.TryPredictCpuWaitFixedSlotGrant(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite: false,
                    out grantedCycle,
                    out completedCycle,
                    out var unsupported,
                    out timeline))
            {
                return true;
            }

            _bus.RecordProductionCpuWaitFixedSlotImageFallback(unsupported switch
            {
                CpuWaitFixedSlotImageUnsupported.Frame => CpuWaitFixedImageProductionFallback.Frame,
                CpuWaitFixedSlotImageUnsupported.Copper => CpuWaitFixedImageProductionFallback.Copper,
                CpuWaitFixedSlotImageUnsupported.PendingWrite => CpuWaitFixedImageProductionFallback.PendingWrite,
                CpuWaitFixedSlotImageUnsupported.RasterlinePlan => CpuWaitFixedImageProductionFallback.RasterlinePlan,
                CpuWaitFixedSlotImageUnsupported.SpriteState => CpuWaitFixedImageProductionFallback.SpriteState,
                CpuWaitFixedSlotImageUnsupported.Unstable => CpuWaitFixedImageProductionFallback.Unstable,
                _ => CpuWaitFixedImageProductionFallback.Unsupported
            });
            return false;
        }

        private void ExecutePendingCpuSlot(long slotCycle, bool processBlitter)
        {
            const int MaxSameCyclePasses = 8;
            for (var pass = 0; pass < MaxSameCyclePasses; pass++)
            {
                var generationBefore = _generation;
                ProcessSlotContendedEventsAt(
                    slotCycle,
                    useCpuWaitBlitterMicroOps: true,
                    processBlitter);
                _bus.InvalidateRasterlineSchedule(slotCycle, SlotContendedMemoryAccessMask);

                if (!HasSlotContendedSameCycleWork(slotCycle) ||
                    generationBefore == _generation)
                {
                    return;
                }
            }

            Debug.Fail("Pending CPU slot execution did not stabilize within the same cycle.");
        }

    }
}
