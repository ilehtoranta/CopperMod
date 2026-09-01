/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using BlitterEngine = CopperMod.Amiga.CustomChips.Blitter.Blitter;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        // Display-only scratch execution cannot run a pending blitter control
        // input or a deferred restart. It can consume a completion whose F/N
        // boundaries were already frozen when this value state was captured.
        private readonly record struct CopperBlitterWaitSnapshot(
            BlitterEngine.BOnlyCompletionSignals Completion,
            bool BusPipelineActive,
            bool CanPredictCompletion,
            long LastTerminationCycle,
            bool NastyPriorityEnabled)
        {
            public bool IsFinishedAt(long cycle)
                => Completion.Completed
                    ? Completion.IsCopperFinishedAt(cycle)
                    : !Completion.Pending && !BusPipelineActive;

            public long GetReadyCycle(long currentCycle)
            {
                if (!CanPredictCompletion)
                {
                    return long.MaxValue;
                }
                return Completion.Completed
                    ? Math.Max(currentCycle,
                        Completion.CopperNotificationCycle + AgnusChipSlotScheduler.SlotCycles)
                    : currentCycle;
            }
        }

        private CopperBlitterWaitSnapshot CaptureCopperBlitterWaitSnapshot()
            => new(
                _bus.Blitter.CaptureBOnlyCompletionSignals(),
                _bus.Blitter.BusPipelineActive,
                _bus.Blitter.CanPredictCopperCompletionForScratch,
                _bus.Blitter.LastTerminationCycle,
                _bus.BlitterNastyPriorityEnabled);
    }
}
