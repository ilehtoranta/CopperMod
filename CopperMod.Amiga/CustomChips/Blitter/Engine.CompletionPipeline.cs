/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.CustomChips.Blitter
{
    internal sealed partial class Blitter
    {
        internal readonly record struct BOnlyCompletionSignals(
            bool Pending,
            bool Completed,
            bool NotificationPending,
            long FinalReadOutputCycle,
            long NextInputCycle,
            long MainCompletionCycle,
            long CopperNotificationCycle)
        {
            internal bool IsCopperFinishedAt(long cycle)
                => Completed && cycle > CopperNotificationCycle;
        }

        private BOnlyCompletionControlState _bOnlyCompletion;

        private bool UsesBOnlyCompletionSignals => _bus.Chipset == AmigaChipset.OcsPal;

        // Delivery belongs to the event, not to the current BLTSIZE generation.
        // A new blit can replace the query state before an old notification is
        // delivered; that notification only invalidates observers of the edge.
        private long _pendingBOnlyCopperNotificationCycle = -1;

        private struct BOnlyCompletionControlState
        {
            internal bool Armed;
            internal bool Completed;
            internal bool HeldForDma;
            internal long FinalReadOutputCycle;
            internal long NextInputCycle;
            internal long MainCompletionCycle;
            internal long CopperNotificationCycle;

            internal readonly bool Pending => Armed && !Completed;

            internal void Arm(long outputCycle)
            {
                Armed = true;
                Completed = false;
                HeldForDma = false;
                FinalReadOutputCycle = outputCycle;
                NextInputCycle = outputCycle;
                MainCompletionCycle = -1;
                CopperNotificationCycle = -1;
            }

            internal void Accept(long inputCycle)
            {
                Completed = true;
                NextInputCycle = -1;
                MainCompletionCycle = inputCycle;
                CopperNotificationCycle = inputCycle + ChipSlotCycles;
            }

            internal void HoldThrough(long cycle)
            {
                if (Pending && NextInputCycle <= cycle)
                {
                    NextInputCycle = AgnusChipSlotScheduler.AlignToSlot(cycle + 1);
                }
            }
        }

        internal BOnlyCompletionSignals CaptureBOnlyCompletionSignals()
            => new(
                _bOnlyCompletion.Pending,
                _bOnlyCompletion.Completed,
                _bOnlyCompletion.Completed &&
                    _pendingBOnlyCopperNotificationCycle == _bOnlyCompletion.CopperNotificationCycle,
                _bOnlyCompletion.Armed ? _bOnlyCompletion.FinalReadOutputCycle : -1,
                _bOnlyCompletion.Pending ? _bOnlyCompletion.NextInputCycle : -1,
                _bOnlyCompletion.Completed ? _bOnlyCompletion.MainCompletionCycle : -1,
                _bOnlyCompletion.Completed ? _bOnlyCompletion.CopperNotificationCycle : -1);

        internal bool CanPredictCopperCompletionForScratch
            => !_deferredRestartPending &&
                !_bOnlyCompletion.Pending &&
                (!_busy || _bOnlyCompletion.Completed);

        internal ushort GetDmaconStatusBitsAt(long cycle)
        {
            if (!_bOnlyCompletion.Armed)
            {
                return DmaconStatusBits;
            }

            var status = _zeroFlag ? DmaconBlitterZero : (ushort)0;
            if (_bOnlyCompletion.Pending || cycle <= _bOnlyCompletion.MainCompletionCycle)
            {
                status |= DmaconBlitterBusy;
            }

            return status;
        }

        internal bool IsCopperBlitterFinishedAt(long cycle)
            => _bOnlyCompletion.Armed
                ? _bOnlyCompletion.Completed && cycle > _bOnlyCompletion.CopperNotificationCycle
                : !BusPipelineActive;

        internal long GetCopperBlitterReadyCycle(long currentCycle)
        {
            if (_bOnlyCompletion.Completed)
            {
                return Math.Max(currentCycle,
                    _bOnlyCompletion.CopperNotificationCycle + ChipSlotCycles);
            }

            if (_bOnlyCompletion.Pending ||
                (UsesBOnlyCompletionSignals && _busy && IsBOnlyAreaBlit()))
            {
                // Counter exhaustion has not been accepted. A predicted drain
                // is not a completion signal; the producer will publish F/N.
                return long.MaxValue;
            }

            if (!_busy)
            {
                return currentCycle;
            }

            var predicted = GetPredictedCompletionCycle();
            return predicted > currentCycle ? predicted : currentCycle + ChipSlotCycles;
        }

        private bool HasBOnlyCompletionSignalWork
            => _bOnlyCompletion.Pending || _pendingBOnlyCopperNotificationCycle >= 0;

        internal long PendingCompletionSignalCycle => GetNextBOnlyCompletionSignalCycle();

        private long GetNextBOnlyCompletionSignalCycle()
        {
            var cycle = _pendingBOnlyCopperNotificationCycle >= 0
                ? _pendingBOnlyCopperNotificationCycle
                : long.MaxValue;
            if (_bOnlyCompletion.Pending && IsBlitterDmaEnabled())
            {
                cycle = Math.Min(cycle, _bOnlyCompletion.NextInputCycle);
            }

            return cycle;
        }

        private void ResetBOnlyCompletionGeneration(long cycle, bool resetNotification = false)
        {
            var hadSignals = _bOnlyCompletion.Armed || _pendingBOnlyCopperNotificationCycle >= 0;
            _bOnlyCompletion = default;
            if (resetNotification)
            {
                _pendingBOnlyCopperNotificationCycle = -1;
            }

            if (hadSignals || (UsesBOnlyCompletionSignals && _busy && IsBOnlyAreaBlit()))
            {
                PublishBOnlyCompletionSignalChange(cycle);
            }
        }

        private void ArmBOnlyCompletionFromFinalRead(bool isFinalWord, long outputCycle)
        {
            if (!isFinalWord || !IsBOnlyAreaBlit() || _bOnlyCompletion.Armed)
            {
                return;
            }

            if (!UsesBOnlyCompletionSignals)
            {
                PublishBOnlyFinalInterrupt(isFinalWord, outputCycle + ChipSlotCycles);
                return;
            }

            // This callback still owns physical OUT G. The trailing input at G
            // observes OUT G+1; the read's CompletedCycle is not that input.
            _bOnlyCompletion.Arm(outputCycle);
            AdvanceBOnlyCompletionInput(outputCycle);
        }

        private void AdvanceBOnlyCompletionInput(long inputCycle)
        {
            System.Diagnostics.Debug.Assert(_bOnlyCompletion.Pending);
            System.Diagnostics.Debug.Assert(inputCycle >= _bOnlyCompletion.NextInputCycle);
            var dmaEnabled = IsBlitterDmaEnabled();
            if (!dmaEnabled ||
                !_bus.CausalBusExecutor.CanAdvanceBlitterControlAt(inputCycle))
            {
                _bOnlyCompletion.HeldForDma = !dmaEnabled;
                _bOnlyCompletion.NextInputCycle = inputCycle + ChipSlotCycles;
                PublishBOnlyCompletionSignalChange(inputCycle);
                return;
            }

            // An older generation's N precedes this generation's final B. It
            // must be delivered separately, never repurposed as the new N.
            System.Diagnostics.Debug.Assert(_pendingBOnlyCopperNotificationCycle < 0 ||
                _pendingBOnlyCopperNotificationCycle <= inputCycle);
            DeliverBOnlyCopperNotificationThrough(inputCycle);
            _bOnlyCompletion.Accept(inputCycle);
            _pendingBOnlyCopperNotificationCycle = _bOnlyCompletion.CopperNotificationCycle;
            _liveBOnlyFinalInterruptPublished = true;
            _bus.RequestHardwareInterrupt(AmigaConstants.IntreqBlitter, inputCycle);
            PublishBOnlyCompletionSignalChange(inputCycle);
        }

        private bool DeliverBOnlyCopperNotificationThrough(long cycle)
        {
            if (_pendingBOnlyCopperNotificationCycle < 0 ||
                _pendingBOnlyCopperNotificationCycle > cycle)
            {
                return false;
            }

            var notificationCycle = _pendingBOnlyCopperNotificationCycle;
            _pendingBOnlyCopperNotificationCycle = -1;
            PublishBOnlyCompletionSignalChange(notificationCycle);
            return true;
        }

        private bool AdvanceBOnlyCompletionSignalThrough(long targetCycle)
        {
            var signalCycle = GetNextBOnlyCompletionSignalCycle();
            if (signalCycle == long.MaxValue || signalCycle > targetCycle)
            {
                return false;
            }

            if (_pendingBOnlyCopperNotificationCycle == signalCycle)
            {
                return DeliverBOnlyCopperNotificationThrough(targetCycle);
            }

            // A CPU may already own the current OUT. Its owner does not block
            // this input, but a skipped deadline must not publish IRQ in the
            // past. Initial acceptance at G uses the direct callback above.
            var inputCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(
                signalCycle,
                Math.Max(_bus.ExecutedChipBusHorizon,
                    _bus.LiveSlotKernelCommittedCpuThroughCycle)));
            if (inputCycle > targetCycle)
            {
                if (_bOnlyCompletion.NextInputCycle != inputCycle)
                {
                    _bOnlyCompletion.NextInputCycle = inputCycle;
                    PublishBOnlyCompletionSignalChange(targetCycle);
                }
                return false;
            }

            AdvanceBOnlyCompletionInput(inputCycle);
            return true;
        }

        private bool BOnlyCompletionSignalPrecedesPipeline()
        {
            if (_bOnlyCompletion.Pending)
            {
                return true;
            }

            var signalCycle = GetNextBOnlyCompletionSignalCycle();
            if (signalCycle == long.MaxValue)
            {
                return false;
            }

            if (!_busy || (RequiresDmaForCurrentBlit() && !IsBlitterDmaEnabled()))
            {
                return true;
            }

            var pipelineCycle = _completionPending
                ? _currentCycle
                : RequiresDmaForCurrentBlit()
                    ? GetNextScalarDmaTransitionCycle()
                    : GetCurrentStepEndCycle();
            return signalCycle <= pipelineCycle;
        }

        private void HoldBOnlyCompletionInputThrough(long cycle)
        {
            if (!_bOnlyCompletion.Pending || _bOnlyCompletion.NextInputCycle > cycle)
            {
                return;
            }

            _bOnlyCompletion.HoldThrough(cycle);
            _bOnlyCompletion.HeldForDma = true;
            PublishBOnlyCompletionSignalChange(cycle);
        }

        private void RebaseBOnlyCompletionInputAt(long cycle)
        {
            if (!_bOnlyCompletion.Pending)
            {
                return;
            }

            if (_bOnlyCompletion.HeldForDma && IsBlitterDmaEnabled())
            {
                // DMA re-enable is an OUT-side register effect. The input at
                // that same CCK is eligible even if an earlier disabled-horizon
                // observation had tentatively held it through the whole CCK.
                _bOnlyCompletion.NextInputCycle = AgnusChipSlotScheduler.AlignToSlot(cycle);
                _bOnlyCompletion.HeldForDma = false;
            }
            else if (_bOnlyCompletion.NextInputCycle < cycle)
            {
                _bOnlyCompletion.NextInputCycle = AgnusChipSlotScheduler.AlignToSlot(cycle);
            }
            else
            {
                return;
            }

            PublishBOnlyCompletionSignalChange(cycle);
        }

        private void PublishBOnlyCompletionSignalChange(long cycle)
        {
            _wakeVersion++;
            _schedulerWakeVersion++;
            _bus.Display.NotifyBlitterCompletionSignalsChanged(cycle);
            _bus.PublishDmaconrState(cycle);
            var nextCycle = GetNextBOnlyCompletionSignalCycle();
            if (nextCycle != long.MaxValue)
            {
                _bus.NotifyHardwareWorkScheduled(nextCycle);
            }
        }
    }
}
