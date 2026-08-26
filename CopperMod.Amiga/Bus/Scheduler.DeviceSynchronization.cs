/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Scheduler
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SynchronizeBlitterThrough(long targetCycle)
        {
            var canonicalSlot = AgnusChipSlotScheduler.AlignToSlot(targetCycle);
            if (_bus.AgnusLiveBlitterEnabled &&
                ((targetCycle != canonicalSlot &&
                  _bus.Blitter.HasLiveAgnusSlotKernelAfterSlotTransitionThrough(
                      targetCycle)) ||
                 (!_bus.AgnusSlotKernelSelected &&
                  targetCycle == canonicalSlot &&
                  _bus.Blitter.HasLiveAgnusCpuGrantAfterSlotBarrierAt(
                      targetCycle))))
            {
                // An after-slot transition belongs to the next physical
                // arbitration boundary. Neither a scalar/intervening CPU-cycle
                // sync nor a later same-slot sync may consume it early and
                // republish its next word as an after-slot edge on that boundary.
                return;
            }

            if (_bus.Blitter.HasAdvanceWorkThrough(targetCycle))
            {
                _bus.Blitter.ExecuteAdmittedWorkThrough(targetCycle);
            }
        }

        internal void SynchronizePaulaThrough(long targetCycle)
            => _bus.Paula.AdvanceTo(targetCycle);

        internal void SynchronizeDiskThrough(long targetCycle)
            => _bus.Disk.AdvanceTo(targetCycle);
    }
}
