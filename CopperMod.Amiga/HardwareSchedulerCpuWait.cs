/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga
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

    internal sealed partial class AmigaHardwareScheduler
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
                    AmigaBusAccessTarget.RealTimeClock))
            {
                return CpuWaitGrantAdvanceResult.Unsupported;
            }

            requestedCycle = Math.Max(0, requestedCycle);

            // Hardware before the CPU request cannot observe the pending request.
            if (requestedCycle > 0)
            {
                if (_bus.Blitter.Busy && _bus.Blitter.CanUseCpuWaitAreaMicroOps)
                {
                    var blitterSlot = AgnusChipSlotScheduler.AlignToSlot(
                        Math.Max(0, _bus.Blitter.CurrentCycle));
                    while (blitterSlot < requestedCycle &&
                        _bus.Blitter.Busy &&
                        _bus.Blitter.CanUseCpuWaitAreaMicroOps)
                    {
                        _bus.Blitter.AdvanceCpuWaitAreaMicroOpTo(blitterSlot);
                        blitterSlot += AgnusChipSlotScheduler.SlotCycles;
                    }
                }

                DrainSlotContendedAccess(requestedCycle - 1);
            }

            if (_bus.HasUnsupportedCpuWaitSlotWorkThrough(requestedCycle + _bus.PalLineCycles))
            {
                return CpuWaitGrantAdvanceResult.ReferenceContinuation;
            }

            _busAccessDrainCount++;

            _draining = true;
            _bus.BeginPendingCpuSlotRequest(kind, target, address, size, requestedCycle, isWrite);
            try
            {
                var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
                while (true)
                {
                    if (_bus.Display.HasLiveDisplayWork())
                    {
                        var liveResult = _bus.AdvanceCpuWaitLiveSlot(
                            candidate,
                            out _,
                            out _,
                            out _);
                        if (_bus.HasUnsupportedCpuWaitSlotWorkThrough(candidate + _bus.PalLineCycles) ||
                            liveResult == OcsCpuWaitLiveSlotResult.CopperBarrier)
                        {
                            return CpuWaitGrantAdvanceResult.ReferenceContinuation;
                        }

                        ExecutePendingCpuSlot(candidate);
                    }
                    else
                    {
                        ExecutePendingCpuSlot(candidate);
                    }

                    _bus.SynchronizeHrmBlitterPriority();
                    if (AgnusHrmOcsSlotTable.IsCpuAccessibleSlot(candidate) &&
                        _bus.TryGrantPendingCpuSingleSlot(
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
                        MarkClean(candidate, SlotContendedMemoryAccessMask);
                        return CpuWaitGrantAdvanceResult.Granted;
                    }

                    candidate += AgnusChipSlotScheduler.SlotCycles;
                }
            }
            finally
            {
                _bus.ClearPendingCpuSlotRequest();
                _draining = false;
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
                _bus.HasNonDisplayDynamicCpuWaitSlotWorkThrough(requestedCycle + _bus.PalLineCycles))
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
                MarkClean(grantedCycle, SlotContendedMemoryAccessMask);
                _bus.RecordProductionCpuWaitFixedSlotImageUse(requestedCycle, grantedCycle);
                return CpuWaitGrantAdvanceResult.Granted;
            }
            finally
            {
                _bus.ClearPendingCpuSlotRequest();
                _draining = false;
            }
        }

        private void ExecutePendingCpuSlot(long slotCycle)
        {
            const int MaxSameCyclePasses = 8;
            for (var pass = 0; pass < MaxSameCyclePasses; pass++)
            {
                var generationBefore = _generation;
                ProcessSlotContendedEventsAt(slotCycle, useCpuWaitBlitterMicroOps: true);
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
