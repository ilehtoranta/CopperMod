/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void BeginLiveSlotKernelCpuRequest(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            if (_agnusLiveSlotKernel == null ||
                !AgnusSlotKernelSelected ||
                target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock or
                    AmigaBusAccessTarget.CustomRegisters))
            {
                throw new InvalidOperationException(
                    "The production-disabled live Agnus slot kernel is not selected.");
            }

            _hrmSlotEngine.BeginCpuDmaWait(requestedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PublishLiveSlotKernelCpuRequest(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            SynchronizeHrmBlitterPriority();
            _hrmSlotEngine.PublishPendingCpuSlotRequest(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WithdrawLiveSlotKernelCpuRequest()
            => _hrmSlotEngine.WithdrawPendingCpuSlotRequest();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ClearLiveSlotKernelCpuRequest()
            => _hrmSlotEngine.ClearPendingCpuSlotRequest();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGrantLiveSlotKernelCpuWord(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            out long completedCycle)
            => _hrmSlotEngine.TryGrantCpuDataSingleExactSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                slotCycle,
                isWrite,
                allowNiceBlitterSteal: true,
                out completedCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ObserveLiveSlotKernelCpuWaitCycle(long slotCycle)
            => _hrmSlotEngine.ObservePendingCpuDmaCycle(slotCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ObserveLiveSlotKernelCpuGrant(long slotCycle)
            => Blitter.ObserveLiveAgnusSlotKernelCpuGrant(slotCycle);

        internal long LiveSlotKernelCommittedCpuThroughCycle =>
            _agnusLiveSlotKernel?.CommittedCpuThroughCycle ?? -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SynchronizeLiveSlotKernelCustomReadBoundaryTo(long targetCycle)
        {
            // The Stage-5 custom-register read barrier publishes non-bus
            // observables before the CPU samples the register. DMA ownership
            // remains with the live requesters; only their register-visible
            // timelines are synchronized here.
            if (GetNextRasterEventCycle(targetCycle, targetCycle) <= targetCycle)
            {
                AdvanceRasterCoreTo(targetCycle);
            }

            if (GetNextCiaTimerEventCycle(targetCycle, targetCycle) <= targetCycle)
            {
                AdvanceCiaTimersCoreTo(targetCycle);
            }

            if (Paula.HasRegisterObservableWorkThrough(targetCycle))
            {
                Paula.AdvanceRegisterObservableTo(targetCycle);
            }

            Disk.SynchronizeInputThrough(targetCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceDueLiveFixedRequestersTo(long slotCycle)
        {
            // Stage-5 display preparation may inspect a future fixed slot
            // before the causal executor reaches a due Disk/Paula deadline.
            // Settle only requesters whose raw deadline has actually matured;
            // advancing an idle requester here would perturb passive device
            // state beyond the pre-grant operation's responsibility.
            if (Disk.GetRawSlotDmaEligibilityCycle() <= slotCycle)
            {
                if (AgnusSlotKernelSelected)
                {
                    PrepareLiveSlotKernelDisk(slotCycle);
                }
                else
                {
                    Disk.AdvanceLiveAgnusSlotKernelTo(slotCycle);
                }
            }

            if (Paula.GetRawDmaEligibilityCycle() <= slotCycle)
            {
                if (AgnusSlotKernelSelected)
                {
                    PrepareLiveSlotKernelPaula(slotCycle);
                }
                else
                {
                    Paula.AdvanceLiveAgnusSlotKernelTo(slotCycle);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepareLiveSlotKernelDisk(long slotCycle)
        {
            for (var transition = 0; transition < 16; transition++)
            {
                if (!Disk.TryCommitNextLiveAgnusSlotKernelTransition(slotCycle))
                {
                    return;
                }

                _agnusLiveSlotKernel!.RecordDiskTransitionCommit();
            }

            throw new InvalidOperationException(
                $"The live disk requester did not settle slot {slotCycle} within 16 transitions.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepareLiveSlotKernelPaula(long slotCycle)
        {
            for (var transition = 0; transition < 16; transition++)
            {
                if (!Paula.TryCommitNextLiveAgnusSlotKernelTransition(
                        slotCycle,
                        out var stopAfterTransition))
                {
                    return;
                }

                _agnusLiveSlotKernel!.RecordPaulaTransitionCommit();
                if (stopAfterTransition)
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"The live Paula requester did not settle slot {slotCycle} within 16 transitions.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PrepareLiveSlotKernelDisplaySlots(long slotCycle)
        {
            // A first Chip-bus access can enter after scheduler-driven boot
            // work has advanced into the middle of a frame. The one-transition
            // boundary may therefore have to consume a bounded frame suffix
            // before steady-state one-slot operation begins.
            for (var transition = 0; transition < 32_768; transition++)
            {
                if (!Display.TryCommitNextLiveSlotKernelTransition(slotCycle))
                {
                    return;
                }

                _agnusLiveSlotKernel!.RecordDisplayTransitionCommit();
            }

            throw new InvalidOperationException(
                $"The live display requester did not settle slot {slotCycle} within 32768 transitions.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepareLiveSlotKernelBlitter(long slotCycle)
        {
            var cpuBoundaryPrepared = false;
            var settlingDeferredAfterSlot = false;
            for (var transition = 0; transition < 16; transition++)
            {
                if (!Blitter.TryCommitNextLiveAgnusCpuPreGrantTransition(
                        slotCycle,
                        ref cpuBoundaryPrepared,
                        ref settlingDeferredAfterSlot))
                {
                    return;
                }

                _agnusLiveSlotKernel!.RecordBlitterTransitionCommit();
            }

            throw new InvalidOperationException(
                $"The live blitter requester did not settle slot {slotCycle} within 16 transitions.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdvanceLiveSlotKernelRequesters(
            long slotCycle,
            bool advanceBlitter)
        {
            // A fixed slot is reusable by Copper only when its fixed requester
            // is genuinely idle, not merely because that requester has not
            // been advanced yet at this canonical cycle.
            if (Disk.GetRawSlotDmaEligibilityCycle() <= slotCycle)
            {
                PrepareLiveSlotKernelDisk(slotCycle);
            }

            if (Paula.GetNextLiveAgnusSlotKernelCycle() <= slotCycle)
            {
                PrepareLiveSlotKernelPaula(slotCycle);
            }

            if (Display.GetNextLiveAgnusSlotKernelCycle() <= slotCycle ||
                Display.HasLiveAgnusFrameBoundaryThrough(slotCycle))
            {
                PrepareLiveSlotKernelDisplaySlots(slotCycle);
            }

            if (advanceBlitter &&
                Blitter.GetNextLiveAgnusSlotKernelCycle() <= slotCycle)
            {
                PrepareLiveSlotKernelBlitter(slotCycle);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasLiveSlotKernelBlitterAfterSlotTransitionThrough(
            long slotCycle)
            => Blitter.HasLiveAgnusSlotKernelAfterSlotTransitionThrough(slotCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasLiveSlotKernelBlitterCpuBoundaryFinishAt(long slotCycle)
            => Blitter.HasLiveAgnusCpuBoundaryFinishTransitionAt(slotCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsLiveSlotKernelFixedDmaSlotPredicted(long slotCycle)
            => IsMandatoryRefreshSlot(slotCycle) ||
               Display.HasLiveAgnusFixedRequestAt(slotCycle) ||
               (Display.TryGetCpuChipFetchFixedSlotOwner(
                    slotCycle,
                    out var owner,
                    out _) &&
                owner != CustomChips.Denise.CpuWaitFixedSlotOwner.Free);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long GetNextLiveSlotKernelCycle(long cpuSearchCycle)
            => Math.Min(
                cpuSearchCycle,
                Math.Min(
                    Disk.GetRawSlotDmaEligibilityCycle(),
                    Math.Min(
                        Paula.GetNextLiveAgnusSlotKernelCycle(),
                        Math.Min(
                            Display.GetNextLiveAgnusSlotKernelCycle(),
                            Blitter.GetNextLiveAgnusSlotKernelCycle()))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGrantCpuAccessThroughLiveSlotKernel(
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
            if (_agnusLiveSlotKernel == null ||
                !AgnusSlotKernelSelected ||
                target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock or
                    AmigaBusAccessTarget.CustomRegisters))
            {
                grantedCycle = 0;
                secondWordCycle = 0;
                completedCycle = 0;
                return false;
            }

            _agnusLiveSlotKernel.GrantCpuAccess(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out secondWordCycle,
                out completedCycle);
            return true;
        }
    }
}
