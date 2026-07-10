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

    internal sealed partial class AmigaHardwareScheduler
    {
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
                DrainSlotContendedAccess(requestedCycle - 1);
            }

            if (_bus.HasNonDisplayDynamicCpuWaitSlotWorkThrough(requestedCycle + _bus.PalLineCycles))
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
                        if (_bus.HasNonDisplayDynamicCpuWaitSlotWorkThrough(candidate + _bus.PalLineCycles) ||
                            liveResult == OcsCpuWaitLiveSlotResult.CopperBarrier)
                        {
                            return CpuWaitGrantAdvanceResult.ReferenceContinuation;
                        }
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
