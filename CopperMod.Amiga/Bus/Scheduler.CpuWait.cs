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
        ReferenceContinuation
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

            // Hardware before the CPU request cannot observe the pending request.
            if (requestedCycle > 0)
            {
                DrainSlotContendedAccess(requestedCycle - 1);
            }

            var firstCandidateCycle = requestedCycle;
            var blitterStallReleased = _bus.Blitter.CpuStallActive;
            if (blitterStallReleased)
            {
                firstCandidateCycle = ExecuteThroughBlitterCpuStall(requestedCycle);
            }

            _busAccessDrainCount++;
            _bus.BeginPendingCpuSlotRequest(kind, target, address, size, requestedCycle, isWrite);
            try
            {
                var candidate = AgnusChipSlotScheduler.AlignToSlot(firstCandidateCycle);
                while (true)
                {
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
                        grantedCycle = candidate;
                        return CpuWaitGrantAdvanceResult.Granted;
                    }

                    candidate += AgnusChipSlotScheduler.SlotCycles;
                }
            }
            finally
            {
                _bus.ClearPendingCpuSlotRequest();
            }
        }


        internal CpuWaitGrantAdvanceResult AdvanceUntilCpuGrantUsingFixedSlotImage(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotTimelineSignature timeline,
            out bool verifyTimeline)
        {
            _bus.RecordProductionCpuWaitFixedSlotImageAttempt();
            grantedCycle = 0;
            completedCycle = 0;
            timeline = default;
            verifyTimeline = false;
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

            verifyTimeline = _bus.ShouldVerifyProductionCpuWaitFixedSlotImage;
            CpuWaitFixedSlotImageUnsupported unsupported;
            var supported = verifyTimeline
                ? _bus.TryPredictCpuWaitFixedSlotGrant(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle,
                    out unsupported,
                    out timeline)
                : _bus.TryPredictCpuWaitFixedSlotGrant(
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
