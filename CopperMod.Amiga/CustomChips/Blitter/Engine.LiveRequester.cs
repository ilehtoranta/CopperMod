/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.CustomChips.Blitter
{
    internal sealed partial class Blitter
    {
        private LiveBlitterRequester? _liveBlitterRequester;
        private long _liveBlitterAreaWords;
        private long _liveBlitterLinePixels;
        private long _liveBlitterCompletions;
        private long _liveBlitterInterrupts;
        private readonly System.Collections.Generic.List<long>
            _liveBlitterAfterSlotTransitionCycles = new();
        private bool _liveAreaFinalDCompletionPublished;
        private bool _liveBOnlyFinalInterruptPublished;
        private bool _liveAreaPendingDValid;
        private ushort _liveAreaPendingDValue;
        private bool _liveAreaPendingDCompletesRow;
        private bool _liveAreaPendingDCompletesBlit;
        private bool _liveAreaPendingDRequiresRetireControl;
        private bool _liveAreaFinalDDrainActive;
        private long _liveAreaFinalDDrainRequestCycle;
        private long _liveBlitterCpuAfterSlotBarrierCycle = -1;

        private enum LiveBlitterTransitionKind : byte
        {
            AdvanceBOnlyCompletionSignal,
            AdvanceAreaStartup,
            AdvanceAreaWordControl,
            BeginAreaWord,
            PrepareAreaWrite,
            LatchAreaPipelineWrite,
            BeginAreaFinalDDrain,
            FinishAreaWord,
            BeginLinePixel,
            PrepareLineWrite,
            FinishLinePixel,
            FinalizeCompletion
        }

        internal AgnusLiveBlitterDeviceDiagnostics CaptureLiveBlitterDeviceDiagnostics()
            => new(
                _liveBlitterAreaWords,
                _liveBlitterLinePixels,
                _liveBlitterCompletions,
                _liveBlitterInterrupts,
                SlotQueueReplayCalls: 0,
                DeviceSynchronizationCalls: 0,
                SchedulerDrainCalls: 0,
                RangeAdvanceCalls: 0,
                PredictionCalls: 0,
                EventDiscoveryCalls: 0,
                CausalExecutorCalls: 0);

        private void ResetLiveBlitterRequester(bool resetDiagnostics)
        {
            _liveBlitterRequester?.Reset();
            _liveAreaFinalDCompletionPublished = false;
            _liveBOnlyFinalInterruptPublished = false;
            ResetLiveAreaDPipeline();
            if (!resetDiagnostics)
            {
                return;
            }

            _liveBlitterAreaWords = 0;
            _liveBlitterLinePixels = 0;
            _liveBlitterCompletions = 0;
            _liveBlitterInterrupts = 0;
            _liveBlitterAfterSlotTransitionCycles.Clear();
        }

        private void ResetLiveAreaDPipeline()
        {
            _liveAreaPendingDValid = false;
            _liveAreaPendingDValue = 0;
            _liveAreaPendingDCompletesRow = false;
            _liveAreaPendingDCompletesBlit = false;
            _liveAreaPendingDRequiresRetireControl = false;
            _liveAreaFinalDDrainActive = false;
            _liveAreaFinalDDrainRequestCycle = 0;
            _liveBlitterCpuAfterSlotBarrierCycle = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool UsesLiveAreaDPipeline()
            => !_lineMode &&
                (_useD ||
                 _liveAreaPendingDValid ||
                 _liveAreaFinalDDrainActive) &&
                (_bus.AgnusLiveBlitterEnabled ||
                 (_bus.Chipset == AmigaChipset.OcsPal &&
                  !_useA &&
                  !_useB &&
                  !_useC &&
                  !_fillEnabled));

        // Current channel enables describe future bus work. They must not
        // discard the internal transitions of an area word which has already
        // started under the live requester.
        private bool HasActiveLiveAreaTransition
            => !_lineMode && _areaMicroOpActive;

        private void PrimeLiveBlitterMicroOpUnit()
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                !_busy ||
                _bOnlyCompletion.Pending ||
                HasAreaStartupControl ||
                _completionPending ||
                !IsBlitterDmaEnabled())
            {
                return;
            }

            if (_lineMode)
            {
                if (!_lineMicroOpActive)
                {
                    _ = BeginLineMicroOpPixel();
                }

                return;
            }

            if (!_areaMicroOpActive && BeginAreaMicroOpWord() &&
                GetAreaMicroOpCount() != 0 &&
                GetAreaMicroOp(0) == BlitterSlotQueueOp.WriteD)
            {
                EnsureAreaMicroOpOutputReady();
            }
        }

        private LiveBlitterRequester RequireLiveBlitterRequester()
            => _liveBlitterRequester ??= new LiveBlitterRequester(this);

        private long GetNextLiveBlitterRequesterCycle()
        {
            var requester = RequireLiveBlitterRequester();
            EnsureLiveBlitterRequesterPublication(requester);
            if (requester.TryPeekPendingRequest(out var request))
            {
                return Math.Min(
                    request.EarliestEligibleCycle,
                    GetPendingLiveAreaFinalDCompletionCycle(request));
            }

            return requester.TryPeekNextTransition(out var transition)
                ? transition.Cycle
                : long.MaxValue;
        }

        internal long GetNextLiveAgnusSlotKernelCycle()
        {
            if (!_bus.AgnusLiveBlitterEnabled)
            {
                return long.MaxValue;
            }

            var signalCycle = GetNextBOnlyCompletionSignalCycle();
            if (!_busy || _bOnlyCompletion.Pending)
            {
                return signalCycle;
            }

            if (_areaMicroOpActive && !RequiresDmaForCurrentBlit())
            {
                // The live requester has no bus word after the final channel
                // is removed, but the active word still owns its frozen
                // retire and completion transitions.
                return Math.Min(signalCycle, IsBlitterDmaEnabled()
                    ? GetNextLiveBlitterRequesterCycle()
                    : long.MaxValue);
            }

            if (!RequiresDmaForCurrentBlit())
            {
                // A zero-channel area blit (or a line blit without its DMA
                // source) still consumes sequencer time, but it must not
                // publish a bus request. Let the slot kernel visit its scalar
                // completion edge directly.
                return Math.Min(signalCycle, GetPredictedCompletionCycle());
            }

            return Math.Min(signalCycle, IsBlitterDmaEnabled()
                ? GetNextLiveBlitterRequesterCycle()
                : long.MaxValue);
        }

        private void StepLiveBlitterRequester(long targetCycle)
        {
            var requester = RequireLiveBlitterRequester();
            EnsureLiveBlitterRequesterPublication(requester);
            if (requester.TryPeekPendingRequest(out var request))
            {
                var completionCycle =
                    GetPendingLiveAreaFinalDCompletionCycle(request);
                if (request.EarliestEligibleCycle > targetCycle)
                {
                    if (completionCycle <= targetCycle)
                    {
                        PublishLiveAreaFinalDCompletion(completionCycle);
                        PublishLiveBlitterWakeChange();
                    }

                    return;
                }

                var executedBusHorizon = Math.Max(
                    _bus.ExecutedChipBusHorizon,
                    _bus.LiveSlotKernelCommittedCpuThroughCycle);
                var causalEligibleCycle =
                    AgnusChipSlotScheduler.AlignToSlot(
                        request.EarliestEligibleCycle <= executedBusHorizon
                            ? executedBusHorizon +
                              AgnusChipSlotScheduler.SlotCycles
                            : request.EarliestEligibleCycle);
                if (causalEligibleCycle > request.EarliestEligibleCycle)
                {
                    requester.DeferPendingWord(causalEligibleCycle);
                    if (causalEligibleCycle > targetCycle)
                    {
                        // The G6 caller prepared fixed ownership only through
                        // targetCycle. Do not reserve the causally deferred
                        // future slot until its own arbitration pass has
                        // materialized that fixed suffix.
                        PublishLiveBlitterWakeChange();
                        return;
                    }
                }

                if (!_bus.TryCommitLiveBlitterRequest(
                        requester,
                        out var observedSlotCycle))
                {
                    requester.RetryDeniedWord(observedSlotCycle);
                    if (completionCycle <= observedSlotCycle)
                    {
                        PublishLiveAreaFinalDCompletion(completionCycle);
                    }
                }

                PublishLiveBlitterWakeChange();
                return;
            }

            if (requester.TryPeekNextTransition(out var transition) &&
                transition.Cycle <= targetCycle)
            {
                _bus.CommitLiveBlitterTransition(requester);
                PublishLiveBlitterWakeChange();
            }
        }

        internal void AdvanceLiveAgnusSlotKernelTo(long slotCycle)
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                (!_busy && !HasBOnlyCompletionSignalWork))
            {
                return;
            }

            if (!RequiresDmaForCurrentBlit() &&
                !HasActiveLiveAreaTransition)
            {
                if (HasAdvanceWorkThrough(slotCycle))
                {
                    ExecuteAdmittedWorkThrough(slotCycle);
                }

                return;
            }

            if (!IsBlitterDmaEnabled() && GetNextBOnlyCompletionSignalCycle() > slotCycle)
            {
                HoldBOnlyCompletionInputThrough(slotCycle);
                return;
            }

            var requester = RequireLiveBlitterRequester();
            var settlingDeferredAfterSlot = false;
            for (var step = 0; step < 16; step++)
            {
                var nextCycle = GetNextLiveBlitterRequesterCycle();
                if (nextCycle > slotCycle)
                {
                    return;
                }

                EnsureLiveBlitterRequesterPublication(requester);
                if (requester.TryPeekNextTransition(out var transition) &&
                    transition.Cycle == slotCycle &&
                    transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit &&
                    !settlingDeferredAfterSlot)
                {
                    // Selection at this canonical cycle precedes an after-slot
                    // transition. Committing it here could publish and grant
                    // the next blitter word in the very slot that caused the
                    // transition. Leave it pending until the following slot.
                    return;
                }

                if (requester.TryPeekNextTransition(out transition) &&
                    transition.Cycle < slotCycle &&
                    transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit)
                {
                    // This edge was deliberately held at the preceding slot.
                    // Its internal transition chain is now part of the current
                    // pre-selection boundary; do not impose the same barrier a
                    // second time on a transition it publishes at slotCycle.
                    var crossedCpuBarrier =
                        transition.Cycle == _liveBlitterCpuAfterSlotBarrierCycle;
                    settlingDeferredAfterSlot = !crossedCpuBarrier;
                    if (crossedCpuBarrier)
                    {
                        _liveBlitterCpuAfterSlotBarrierCycle = -1;
                    }
                }

                var version = _wakeVersion;
                // Settle the requester against the arbitration boundary the
                // G6 caller has actually prepared. Passing nextCycle here
                // fragments one Stage-5 target into several artificial calls:
                // a word deferred from an occupied earlier slot then becomes
                // invisible to arbitration at slotCycle.
                StepLiveBlitterRequester(slotCycle);

                if (_wakeVersion == version &&
                    GetNextLiveBlitterRequesterCycle() == nextCycle)
                {
                    throw new InvalidOperationException(
                        $"The G6L blitter requester made no progress at cycle {nextCycle}.");
                }
            }

            var remainingCycle = GetNextLiveBlitterRequesterCycle();
            throw new InvalidOperationException(
                $"The G6L blitter requester did not settle slot {slotCycle}: " +
                $"next={remainingCycle}, bus={_bus.ExecutedChipBusHorizon}, " +
                $"line={_lineMode}, areaIndex={_areaMicroOpIndex}/{GetAreaMicroOpCount()}, " +
                $"lineIndex={_lineMicroOpIndex}/{_lineMicroOpCount}.");
        }

        internal void AdvanceLiveAgnusCpuPreGrantTo(long slotCycle)
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                !_busy ||
                (!RequiresDmaForCurrentBlit() &&
                 !HasActiveLiveAreaTransition) ||
                !IsBlitterDmaEnabled())
            {
                AdvanceLiveAgnusSlotKernelTo(slotCycle);
                return;
            }

            var requester = RequireLiveBlitterRequester();
            var carriedCpuBarrier =
                _liveBlitterCpuAfterSlotBarrierCycle >= 0 &&
                _liveBlitterCpuAfterSlotBarrierCycle < slotCycle;
            var finishesPriorAreaWord =
                requester.TransitionKind == LiveBlitterTransitionKind.FinishAreaWord;
            if ((carriedCpuBarrier || finishesPriorAreaWord) &&
                HasLiveAgnusSlotKernelAfterSlotTransitionAt(slotCycle))
            {
                // Stage 5 can enter this pre-grant drain one internal
                // transition behind the slot kernel after returning across a
                // CPU-owned boundary. Consume exactly that carried transition;
                // the regular slot settle below still stops at any new
                // after-slot edge produced by the current transfer.
                StepLiveBlitterRequester(slotCycle);
                _liveBlitterCpuAfterSlotBarrierCycle = -1;
            }

            AdvanceLiveAgnusSlotKernelTo(slotCycle);
        }

        internal bool TryCommitNextLiveAgnusCpuPreGrantTransition(
            long slotCycle,
            ref bool cpuBoundaryPrepared,
            ref bool settlingDeferredAfterSlot)
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                (!_busy && !HasBOnlyCompletionSignalWork))
            {
                return false;
            }

            if (!RequiresDmaForCurrentBlit() &&
                !HasActiveLiveAreaTransition)
            {
                if (!HasAdvanceWorkThrough(slotCycle))
                {
                    return false;
                }

                var previousCycle = _currentCycle;
                var previousBusy = _busy;
                var previousCompletionPending = _completionPending;
                var schedulerWake = CaptureSchedulerWakeSignature();
                try
                {
                    if (BOnlyCompletionSignalPrecedesPipeline() &&
                        AdvanceBOnlyCompletionSignalThrough(slotCycle))
                    {
                        // An old generation's N can outlive a restart into a
                        // zero-channel program; it is still independent work.
                    }
                    else if (!_busy)
                    {
                        return false;
                    }
                    else if (_completionPending)
                    {
                        FinalizePendingCompletion();
                    }
                    else if (_lineMode)
                    {
                        StepLinePixel(slotCycle);
                    }
                    else
                    {
                        StepAreaWord(slotCycle);
                    }
                }
                finally
                {
                    if (_currentCycle != previousCycle ||
                        _busy != previousBusy ||
                        _completionPending != previousCompletionPending)
                    {
                        _wakeVersion++;
                    }

                    UpdateSchedulerWakeVersionIfChanged(schedulerWake);
                    _bus.PublishDmaconrState(_currentCycle);
                }

                return true;
            }

            if (!IsBlitterDmaEnabled() && GetNextBOnlyCompletionSignalCycle() > slotCycle)
            {
                HoldBOnlyCompletionInputThrough(slotCycle);
                return false;
            }

            var requester = RequireLiveBlitterRequester();
            if (!cpuBoundaryPrepared)
            {
                cpuBoundaryPrepared = true;
                var carriedCpuBarrier =
                    _liveBlitterCpuAfterSlotBarrierCycle >= 0 &&
                    _liveBlitterCpuAfterSlotBarrierCycle < slotCycle;
                var finishesPriorAreaWord =
                    requester.TransitionKind == LiveBlitterTransitionKind.FinishAreaWord;
                if ((carriedCpuBarrier || finishesPriorAreaWord) &&
                    HasLiveAgnusSlotKernelAfterSlotTransitionAt(slotCycle))
                {
                    StepLiveBlitterRequester(slotCycle);
                    _liveBlitterCpuAfterSlotBarrierCycle = -1;
                    return true;
                }
            }

            var nextCycle = GetNextLiveBlitterRequesterCycle();
            if (nextCycle > slotCycle)
            {
                return false;
            }

            EnsureLiveBlitterRequesterPublication(requester);
            if (requester.TryPeekNextTransition(out var transition) &&
                transition.Cycle == slotCycle &&
                transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit &&
                !settlingDeferredAfterSlot)
            {
                return false;
            }

            if (requester.TryPeekNextTransition(out transition) &&
                transition.Cycle < slotCycle &&
                transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit)
            {
                var crossedCpuBarrier =
                    transition.Cycle == _liveBlitterCpuAfterSlotBarrierCycle;
                settlingDeferredAfterSlot = !crossedCpuBarrier;
                if (crossedCpuBarrier)
                {
                    _liveBlitterCpuAfterSlotBarrierCycle = -1;
                }
            }

            var version = _wakeVersion;
            StepLiveBlitterRequester(slotCycle);
            if (_wakeVersion == version &&
                GetNextLiveBlitterRequesterCycle() == nextCycle)
            {
                throw new InvalidOperationException(
                    $"The G6L blitter requester made no progress at cycle {nextCycle}.");
            }

            return true;
        }

        internal bool HasLiveAgnusSlotKernelAfterSlotTransitionThrough(
            long slotCycle)
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                !_busy ||
                (!RequiresDmaForCurrentBlit() &&
                 !HasActiveLiveAreaTransition) ||
                !IsBlitterDmaEnabled())
            {
                return false;
            }

            var requester = RequireLiveBlitterRequester();
            EnsureLiveBlitterRequesterPublication(requester);
            return requester.TryPeekNextTransition(out var transition) &&
                transition.Cycle <= slotCycle &&
                transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit;
        }

        internal bool HasLiveAgnusSlotKernelAfterSlotTransitionAt(long slotCycle)
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                !_busy ||
                (!RequiresDmaForCurrentBlit() &&
                 !HasActiveLiveAreaTransition) ||
                !IsBlitterDmaEnabled())
            {
                return false;
            }

            var requester = RequireLiveBlitterRequester();
            EnsureLiveBlitterRequesterPublication(requester);
            return requester.TryPeekNextTransition(out var transition) &&
                transition.Cycle == slotCycle &&
                transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit;
        }

        internal bool HasLiveAgnusCpuBoundaryFinishTransitionAt(long slotCycle)
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                !_busy ||
                (!RequiresDmaForCurrentBlit() &&
                 !HasActiveLiveAreaTransition) ||
                !IsBlitterDmaEnabled())
            {
                return false;
            }

            var requester = RequireLiveBlitterRequester();
            EnsureLiveBlitterRequesterPublication(requester);
            return requester.TransitionKind == LiveBlitterTransitionKind.FinishAreaWord &&
                requester.TryPeekNextTransition(out var transition) &&
                transition.Cycle == slotCycle &&
                transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit;
        }

        internal bool HasLiveAgnusCpuGrantAfterSlotBarrierAt(long slotCycle)
            => _liveBlitterCpuAfterSlotBarrierCycle == slotCycle &&
                HasLiveAgnusSlotKernelAfterSlotTransitionAt(slotCycle);

        internal void ObserveLiveAgnusSlotKernelCpuGrant(long slotCycle)
        {
            if (!_bus.AgnusLiveBlitterEnabled || !_busy)
            {
                return;
            }

            var requester = RequireLiveBlitterRequester();
            EnsureLiveBlitterRequesterPublication(requester);
            if (requester.TryPeekNextTransition(out var transition) &&
                transition.Cycle == slotCycle &&
                transition.Phase == AgnusLiveTransitionPhase.AfterSlotCommit)
            {
                // A CPU word samples this slot before the transition can be
                // committed. Preserve that boundary across the return to the
                // CPU so the next G6 entry does not collapse both the deferred
                // edge and the transition it publishes into one slot.
                _liveBlitterCpuAfterSlotBarrierCycle = slotCycle;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PublishLiveBlitterWakeChange()
        {
            _wakeVersion++;
            _schedulerWakeVersion++;
            // DMACONR is a device-published register. The Stage-5 executor
            // republishes it from Blitter.AdvanceTo's epilogue, but the G6L
            // slot kernel commits requester words and transitions directly.
            // Keep BBUSY/BLTZERO readback coherent at the same device edge.
            _bus.PublishDmaconrState(_currentCycle);
            var nextCycle = GetNextLiveBlitterRequesterCycle();
            if (nextCycle != long.MaxValue)
            {
                // The Stage-5 scheduler can have a clean-through horizon ahead
                // of the word that just committed. Publishing the following
                // micro-op must retract that horizon or a later CPU access can
                // be granted before an already-due blitter word.
                _bus.NotifyHardwareWorkScheduled(nextCycle);
            }
        }

        private void EnsureLiveBlitterRequesterPublication(
            LiveBlitterRequester requester)
        {
            if (BOnlyCompletionSignalPrecedesPipeline())
            {
                var signalCycle = GetNextBOnlyCompletionSignalCycle();
                if (requester.TransitionKind == LiveBlitterTransitionKind.AdvanceBOnlyCompletionSignal &&
                    requester.TryPeekNextTransition(out var signalTransition) &&
                    signalTransition.Cycle == signalCycle)
                {
                    return;
                }

                requester.CancelPendingWord();
                requester.CancelPendingTransition();
                if (signalCycle != long.MaxValue)
                {
                    requester.PublishTransition(
                        LiveBlitterTransitionKind.AdvanceBOnlyCompletionSignal,
                        signalCycle,
                        AgnusLiveTransitionPhase.BeforeSlotSelection);
                }
                return;
            }

            if (requester.TryPeekPendingRequest(out var request))
            {
                if (IsLiveBlitterRequestCurrent(request))
                {
                    return;
                }

                requester.CancelPendingWord();
            }

            if (requester.TryPeekNextTransition(out var pendingTransition))
            {
                if (IsLiveBlitterTransitionCurrent(
                        requester.TransitionKind,
                        pendingTransition))
                {
                    return;
                }

                requester.CancelPendingTransition();
            }

            if (!_busy)
            {
                return;
            }

            if (_completionPending)
            {
                requester.PublishTransition(
                    LiveBlitterTransitionKind.FinalizeCompletion,
                    _currentCycle,
                    AgnusLiveTransitionPhase.AfterSlotCommit);
                return;
            }

            if (!IsBlitterDmaEnabled())
            {
                return;
            }

            // Do not begin a new zero-channel word. An area word which was
            // already active before its final channel was removed retains its
            // frozen internal transitions on this requester.
            if (!RequiresDmaForCurrentBlit() &&
                !HasActiveLiveAreaTransition)
            {
                return;
            }

            if (HasAreaStartupControl)
            {
                requester.PublishTransition(
                    LiveBlitterTransitionKind.AdvanceAreaStartup,
                    GetNextAreaStartupInputCycle(),
                    AgnusLiveTransitionPhase.BeforeSlotSelection);
                return;
            }

            if (!_lineMode &&
                !_areaMicroOpActive &&
                !_useA && !_useB && !_useC && !_useD &&
                !_liveAreaPendingDValid &&
                !_liveAreaFinalDDrainActive)
            {
                return;
            }

            if (_lineMode)
            {
                PublishLiveLineState(requester);
                return;
            }

            PublishLiveAreaState(requester);
        }

        private void PublishLiveAreaState(LiveBlitterRequester requester)
        {
            if (_liveAreaFinalDDrainActive)
            {
                requester.PublishWord(
                    _workDestinationD,
                    _liveAreaFinalDDrainRequestCycle,
                    channel: 3,
                    write: true,
                    _liveAreaPendingDValue);
                return;
            }

            if (!_areaMicroOpActive)
            {
                requester.PublishTransition(
                    LiveBlitterTransitionKind.BeginAreaWord,
                    _currentCycle,
                    AgnusLiveTransitionPhase.BeforeSlotSelection);
                return;
            }

            if (_areaMicroOpIndex >= GetAreaMicroOpCount())
            {
                if (_areaWordRetireControl.Pending)
                {
                    requester.PublishTransition(
                        LiveBlitterTransitionKind.AdvanceAreaWordControl,
                        _areaWordRetireControl.NextInputCycle,
                        AgnusLiveTransitionPhase.BeforeSlotSelection);
                    return;
                }

                requester.PublishTransition(
                    UsesLiveAreaDPipeline() &&
                    _areaMicroOpFinalWord &&
                    _liveAreaPendingDValid
                        ? LiveBlitterTransitionKind.BeginAreaFinalDDrain
                        : LiveBlitterTransitionKind.FinishAreaWord,
                    GetAreaMicroOpFinishCycle(),
                    AgnusLiveTransitionPhase.AfterSlotCommit);
                return;
            }

            var op = GetAreaMicroOp(_areaMicroOpIndex);
            var requestCycle = GetAreaMicroOpRequestCycle(op);
            if (op == BlitterSlotQueueOp.WriteD &&
                UsesLiveAreaDPipeline() &&
                !_liveAreaPendingDValid)
            {
                requester.PublishTransition(
                    LiveBlitterTransitionKind.LatchAreaPipelineWrite,
                    requestCycle,
                    AgnusLiveTransitionPhase.AfterSlotCommit);
                return;
            }

            requester.PublishWord(
                GetAreaMicroOpAddress(op),
                requestCycle,
                GetAreaChannel(op),
                op == BlitterSlotQueueOp.WriteD,
                op == BlitterSlotQueueOp.WriteD &&
                UsesLiveAreaDPipeline()
                    ? _liveAreaPendingDValue
                    : _areaMicroOpOutput);
        }

        private void PublishLiveLineState(LiveBlitterRequester requester)
        {
            if (!_lineMicroOpActive)
            {
                requester.PublishTransition(
                    LiveBlitterTransitionKind.BeginLinePixel,
                    _currentCycle,
                    AgnusLiveTransitionPhase.BeforeSlotSelection);
                return;
            }

            if (_lineMicroOpIndex >= _lineMicroOpCount)
            {
                requester.PublishTransition(
                    LiveBlitterTransitionKind.FinishLinePixel,
                    _lineMicroOpNextCycle,
                    AgnusLiveTransitionPhase.AfterSlotCommit);
                return;
            }

            var requestCycle = GetLineMicroOpRequestCycle(_lineMicroOpIndex);
            var write = _lineMicroOpIndex == _lineMicroOpCount - 1;
            requester.PublishWord(
                GetLineMicroOpAddress(_lineMicroOpIndex),
                requestCycle,
                GetLineChannel(_lineMicroOpIndex),
                write,
                _lineMicroOpOutput);
        }

        private bool IsLiveBlitterRequestCurrent(
            in AgnusLiveSlotRequest request)
        {
            if (!_busy ||
                _bOnlyCompletion.Pending ||
                HasAreaStartupControl ||
                _completionPending ||
                !IsBlitterDmaEnabled() ||
                request.Owner != AgnusChipSlotOwner.Blitter ||
                request.Kind != AmigaBusAccessKind.Blitter)
            {
                return false;
            }

            if (_lineMode)
            {
                if (!_lineMicroOpActive ||
                    _lineMicroOpIndex >= _lineMicroOpCount)
                {
                    return false;
                }

                var write = _lineMicroOpIndex == _lineMicroOpCount - 1;
                return request.Channel == GetLineChannel(_lineMicroOpIndex) &&
                    request.Address == GetLineMicroOpAddress(_lineMicroOpIndex) &&
                    request.RequestedCycle ==
                    GetLineMicroOpRequestCycle(_lineMicroOpIndex) &&
                    request.Transfer == (write
                        ? AgnusLiveWordTransfer.Write
                        : AgnusLiveWordTransfer.Read) &&
                    (!write ||
                     (_lineMicroOpOutputReady &&
                      request.WriteValue == _lineMicroOpOutput));
            }

            if (!_areaMicroOpActive ||
                _areaMicroOpIndex >= GetAreaMicroOpCount())
            {
                return _liveAreaFinalDDrainActive &&
                    request.Channel == 3 &&
                    request.Address == _workDestinationD &&
                    request.RequestedCycle == _liveAreaFinalDDrainRequestCycle &&
                    request.Transfer == AgnusLiveWordTransfer.Write &&
                    request.WriteValue == _liveAreaPendingDValue;
            }

            var op = GetAreaMicroOp(_areaMicroOpIndex);
            var areaWrite = op == BlitterSlotQueueOp.WriteD;
            return request.Channel == GetAreaChannel(op) &&
                request.Address == GetAreaMicroOpAddress(op) &&
                request.RequestedCycle == GetAreaMicroOpRequestCycle(op) &&
                request.Transfer == (areaWrite
                    ? AgnusLiveWordTransfer.Write
                    : AgnusLiveWordTransfer.Read) &&
                (!areaWrite ||
                 (_areaMicroOpOutputReady &&
                  request.WriteValue == (UsesLiveAreaDPipeline()
                      ? _liveAreaPendingDValue
                      : _areaMicroOpOutput)));
        }

        private bool IsLiveBlitterTransitionCurrent(
            LiveBlitterTransitionKind kind,
            in AgnusLiveTransition transition)
            => kind switch
            {
                LiveBlitterTransitionKind.AdvanceBOnlyCompletionSignal =>
                    BOnlyCompletionSignalPrecedesPipeline() &&
                    transition.Cycle == GetNextBOnlyCompletionSignalCycle(),
                LiveBlitterTransitionKind.AdvanceAreaStartup =>
                    _busy && HasAreaStartupControl && IsBlitterDmaEnabled() &&
                    transition.Cycle == GetNextAreaStartupInputCycle(),
                LiveBlitterTransitionKind.AdvanceAreaWordControl =>
                    _busy &&
                    _areaMicroOpActive &&
                    _areaMicroOpIndex >= GetAreaMicroOpCount() &&
                    _areaWordRetireControl.Pending &&
                    IsBlitterDmaEnabled() &&
                    transition.Cycle == _areaWordRetireControl.NextInputCycle,
                LiveBlitterTransitionKind.BeginAreaWord =>
                    _busy &&
                    !_bOnlyCompletion.Pending &&
                    !HasAreaStartupControl &&
                    !_completionPending &&
                    !_lineMode &&
                    !_areaMicroOpActive &&
                    (_useA || _useB || _useC || _useD) &&
                    IsBlitterDmaEnabled(),
                LiveBlitterTransitionKind.PrepareAreaWrite =>
                    _areaMicroOpActive &&
                    _areaMicroOpIndex < GetAreaMicroOpCount() &&
                    GetAreaMicroOp(_areaMicroOpIndex) ==
                    BlitterSlotQueueOp.WriteD &&
                    !_areaMicroOpOutputReady,
                LiveBlitterTransitionKind.LatchAreaPipelineWrite =>
                    UsesLiveAreaDPipeline() &&
                    _areaMicroOpActive &&
                    _areaMicroOpIndex < GetAreaMicroOpCount() &&
                    GetAreaMicroOp(_areaMicroOpIndex) ==
                    BlitterSlotQueueOp.WriteD &&
                    !_liveAreaPendingDValid,
                LiveBlitterTransitionKind.BeginAreaFinalDDrain =>
                    UsesLiveAreaDPipeline() &&
                    _areaMicroOpActive &&
                    _areaMicroOpFinalWord &&
                    _areaMicroOpIndex >= GetAreaMicroOpCount() &&
                    _liveAreaPendingDValid &&
                    !_liveAreaFinalDDrainActive &&
                    transition.Cycle == GetAreaMicroOpFinishCycle(),
                LiveBlitterTransitionKind.FinishAreaWord =>
                    !_bOnlyCompletion.Pending && _areaMicroOpActive &&
                    _areaMicroOpIndex >= GetAreaMicroOpCount() &&
                    transition.Cycle == GetAreaMicroOpFinishCycle(),
                LiveBlitterTransitionKind.BeginLinePixel =>
                    _busy &&
                    !_completionPending &&
                    _lineMode &&
                    !_lineMicroOpActive &&
                    IsBlitterDmaEnabled(),
                LiveBlitterTransitionKind.PrepareLineWrite =>
                    _lineMicroOpActive &&
                    _lineMicroOpIndex == _lineMicroOpCount - 1 &&
                    !_lineMicroOpOutputReady,
                LiveBlitterTransitionKind.FinishLinePixel =>
                    _lineMicroOpActive &&
                    _lineMicroOpIndex >= _lineMicroOpCount,
                LiveBlitterTransitionKind.FinalizeCompletion =>
                    _busy && !_bOnlyCompletion.Pending && _completionPending,
                _ => false
            };

        private void CommitLiveBlitterWord(
            in AgnusLiveSlotGrant grant)
        {
            var request = grant.Request;
            var access = new AmigaBusAccessResult(
                new AmigaBusAccessRequest(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    AmigaBusAccessTarget.ChipRam,
                    request.Address,
                    AmigaBusAccessSize.Word,
                    request.RequestedCycle,
                    request.Transfer == AgnusLiveWordTransfer.Write),
                grant.SlotCycle,
                grant.CompletedCycle);
            RecordBlitterDma(access);
            if (_lineMode)
            {
                CommitLiveLineMicroOp(request.Channel, grant.SampledValue, access);
                if (_lineMicroOpActive &&
                    _lineMicroOpIndex >= _lineMicroOpCount &&
                    _lineMicroOpNextCycle <= grant.CompletedCycle)
                {
                    var previousCompletionCycle = _lastCompletionCycle;
                    FinishLineMicroOpPixel(_lineMicroOpNextCycle);
                    _liveBlitterLinePixels++;
                    RecordLiveBlitterCompletion(previousCompletionCycle);
                    PrimeLiveBlitterMicroOpUnit();
                }
            }
            else
            {
                if (_liveAreaFinalDDrainActive)
                {
                    CommitLiveAreaFinalDDrain(access);
                    return;
                }

                CommitLiveAreaMicroOp(request.Channel, grant.SampledValue, access);
                if (_areaMicroOpFinalWord &&
                    IsBOnlyAreaBlit() &&
                    _areaMicroOpIndex >= GetAreaMicroOpCount())
                {
                    ArmBOnlyCompletionFromFinalRead(
                        isFinalWord: true,
                        access.GrantedCycle);
                }
                if (_areaMicroOpActive &&
                    !_bOnlyCompletion.Pending &&
                    !_areaWordRetireControl.Pending &&
                    _areaMicroOpIndex >= GetAreaMicroOpCount() &&
                    GetAreaMicroOpFinishCycle() <= grant.CompletedCycle)
                {
                    var finishCycle = GetAreaMicroOpFinishCycle();
                    if (UsesLiveAreaDPipeline() &&
                        _areaMicroOpFinalWord &&
                        _liveAreaPendingDValid)
                    {
                        // The last main D phase has just completed. Commit its
                        // after-slot BBUSY transition now so the HOLD_D idle
                        // phase occupies exactly one slot and the final D
                        // request is visible before the following selection.
                        BeginLiveAreaFinalDDrain(finishCycle);
                        return;
                    }

                    var previousCompletionCycle = _lastCompletionCycle;
                    FinishAreaMicroOpWord(finishCycle);
                    _liveBlitterAreaWords++;
                    RecordLiveBlitterCompletion(previousCompletionCycle);
                    PrimeLiveBlitterMicroOpUnit();
                }
            }
        }

        private void CommitLiveAreaMicroOp(
            int channel,
            ushort sampledValue,
            in AmigaBusAccessResult access)
        {
            var op = GetAreaMicroOp(_areaMicroOpIndex);
            if (channel != GetAreaChannel(op))
            {
                throw new InvalidOperationException(
                    "The granted area micro-operation no longer matches live blitter state.");
            }

            var requestedCycle = GetAreaMicroOpRequestCycle(op);
            switch (op)
            {
                case BlitterSlotQueueOp.ReadA:
                    _areaMicroOpRawA = sampledValue;
                    _workSourceA =
                        _bus.AddChipDmaPointerOffset(_workSourceA, _step);
                    AccountAreaMicroOpReadWait(
                        requestedCycle,
                        access.CompletedCycle);
                    _areaMicroOpNextReadCycle = access.CompletedCycle;
                    _areaMicroOpNextCycle = Math.Max(
                        _areaMicroOpNextCycle,
                        access.CompletedCycle);
                    break;

                case BlitterSlotQueueOp.ReadB:
                    _areaMicroOpRawB = sampledValue;
                    _workSourceB =
                        _bus.AddChipDmaPointerOffset(_workSourceB, _step);
                    AccountAreaMicroOpReadWait(
                        requestedCycle,
                        access.CompletedCycle);
                    _areaMicroOpNextReadCycle = access.CompletedCycle;
                    _areaMicroOpNextCycle = Math.Max(
                        _areaMicroOpNextCycle,
                        access.CompletedCycle);
                    break;

                case BlitterSlotQueueOp.ReadC:
                    _areaMicroOpRawC = sampledValue;
                    _activeDataC = sampledValue;
                    _workSourceC =
                        _bus.AddChipDmaPointerOffset(_workSourceC, _step);
                    AccountAreaMicroOpReadWait(
                        requestedCycle,
                        access.CompletedCycle);
                    _areaMicroOpNextReadCycle = access.CompletedCycle;
                    _areaMicroOpNextCycle = Math.Max(
                        _areaMicroOpNextCycle,
                        access.CompletedCycle);
                    break;

                case BlitterSlotQueueOp.WriteD:
                    var outgoingCompletesBlit =
                        _liveAreaPendingDCompletesBlit;
                    var outgoingRequiresRetireControl =
                        _liveAreaPendingDRequiresRetireControl;
                    if (UsesLiveAreaDPipeline())
                    {
                        CommitLiveAreaPipelinedDPointer();
                        if (_useD)
                        {
                            LatchLiveAreaPipelineWrite();
                        }
                    }
                    else
                    {
                        _workDestinationD =
                            _bus.AddChipDmaPointerOffset(_workDestinationD, _step);
                    }

                    _areaMicroOpNextCycle = Math.Max(
                        _areaMicroOpNextCycle,
                        access.CompletedCycle);
                    ArmAreaWordRetireControl(
                        access.GrantedCycle,
                        outgoingRequiresRetireControl,
                        outgoingCompletesBlit ||
                        (_areaMicroOpFinalWord && _useD));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(channel),
                        channel,
                        null);
            }

            _areaMicroOpIndex++;
            if (op == BlitterSlotQueueOp.WriteD && !_useD)
            {
                // The preserved pipelined D intent has now reached the bus.
                // Rebuild only after consuming it so later words contain no D
                // phase and cannot generate another destination write.
                BuildAreaMicroOpSequence();
            }

            if (_areaMicroOpIndex < GetAreaMicroOpCount() &&
                GetAreaMicroOp(_areaMicroOpIndex) ==
                BlitterSlotQueueOp.WriteD)
            {
                EnsureAreaMicroOpOutputReady();
            }
        }

        private void CommitLiveAreaPipelinedDPointer()
        {
            if (!_liveAreaPendingDValid)
            {
                throw new InvalidOperationException(
                    "A pipelined area D grant requires a latched previous output.");
            }

            _workDestinationD =
                _bus.AddChipDmaPointerOffset(_workDestinationD, _step);
            if (_liveAreaPendingDCompletesRow &&
                !_liveAreaPendingDCompletesBlit)
            {
                _workDestinationD = AddModulo(
                    _workDestinationD,
                    _activeDestinationDModulo,
                    _descending);
            }

            _liveAreaPendingDValid = false;
            _liveAreaPendingDRequiresRetireControl = false;
        }

        private void LatchLiveAreaPipelineWrite()
        {
            EnsureAreaMicroOpOutputReady();
            _liveAreaPendingDValue = _areaMicroOpOutput;
            _liveAreaPendingDCompletesRow =
                _wordX == _widthWords - 1;
            _liveAreaPendingDCompletesBlit =
                _areaMicroOpFinalWord;
            _liveAreaPendingDRequiresRetireControl =
                UsesAreaWordRetireControl;
            _liveAreaPendingDValid = true;
        }

        private void BeginLiveAreaFinalDDrain(
            long completionCycle,
            bool recordLiveDiagnostics = true)
        {
            if (!_liveAreaPendingDValid ||
                !_liveAreaPendingDCompletesBlit)
            {
                throw new InvalidOperationException(
                    "The final area D drain requires the final output latch.");
            }

            _liveAreaFinalDDrainActive = true;
            _liveAreaFinalDDrainRequestCycle =
                completionCycle + AgnusChipSlotScheduler.SlotCycles;
            if (recordLiveDiagnostics)
            {
                _liveBlitterAreaWords++;
            }
            PublishLiveAreaFinalDCompletion(
                completionCycle,
                recordLiveDiagnostics);
        }

        private void CommitLiveAreaFinalDDrain(
            in AmigaBusAccessResult access)
        {
            CommitLiveAreaPipelinedDPointer();
            _liveAreaFinalDDrainActive = false;
            _areaMicroOpIndex = GetAreaMicroOpCount();
            _areaMicroOpNextCycle = Math.Max(
                _areaMicroOpNextCycle,
                access.CompletedCycle);
            var previousCompletionCycle = _lastCompletionCycle;
            FinishAreaMicroOpWord(access.CompletedCycle);
            RecordLiveBlitterCompletion(previousCompletionCycle);
        }

        private long GetPendingLiveAreaFinalDCompletionCycle(
            in AgnusLiveSlotRequest request)
        {
            if (UsesLiveAreaDPipeline())
            {
                return long.MaxValue;
            }

            if (_liveAreaFinalDCompletionPublished ||
                _lineMode ||
                !_areaMicroOpActive ||
                !_areaMicroOpFinalWord ||
                !_useD ||
                _areaMicroOpIndex >= GetAreaMicroOpCount() ||
                GetAreaMicroOp(_areaMicroOpIndex) !=
                    BlitterSlotQueueOp.WriteD ||
                request.Channel != 3)
            {
                return long.MaxValue;
            }

            return _areaMicroOpInternalCompletionCycle;
        }

        private void PublishLiveAreaFinalDCompletion(
            long completionCycle,
            bool recordLiveDiagnostics = true)
        {
            if (_liveAreaFinalDCompletionPublished)
            {
                return;
            }

            // OCS clears BBUSY and requests the interrupt when the last main
            // area cycle finishes. A D-enabled area blit can still have its
            // pipelined final write denied and retried after that edge. Keep
            // the bus pipeline alive while publishing the architectural
            // completion at its own cycle.
            _liveAreaFinalDCompletionPublished = true;
            _currentCycle = completionCycle;
            _lastCompletionCycle = completionCycle;
            if (_bus.BusAccessCaptureEnabled)
            {
                _completionCycles.Add(completionCycle);
            }

            _bus.RequestHardwareInterrupt(
                AmigaConstants.IntreqBlitter,
                completionCycle);
            if (recordLiveDiagnostics)
            {
                _liveBlitterCompletions++;
                _liveBlitterInterrupts++;
            }
        }

        private void CommitLiveLineMicroOp(
            int channel,
            ushort sampledValue,
            in AmigaBusAccessResult access)
        {
            if (channel != GetLineChannel(_lineMicroOpIndex))
            {
                throw new InvalidOperationException(
                    "The granted line micro-operation no longer matches live blitter state.");
            }

            var requestedCycle =
                GetLineMicroOpRequestCycle(_lineMicroOpIndex);
            if (_useB && _lineMicroOpIndex < 2)
            {
                if (_lineMicroOpIndex == 1)
                {
                    _dataB = sampledValue;
                    _workSourceB = _bus.AddChipDmaPointerOffset(
                        _workSourceB,
                        _lineBPatternStride);
                }

                AccountLineMicroOpReadWait(
                    requestedCycle,
                    access.CompletedCycle);
                _lineMicroOpNextReadCycle = access.CompletedCycle;
                _lineMicroOpNextCycle = Math.Max(
                    _lineMicroOpNextCycle,
                    access.CompletedCycle);
            }
            else
            {
                var sourceCIndex = _useB ? 2 : 0;
                if (_lineMicroOpIndex == sourceCIndex)
                {
                    _lineMicroOpSourceC = sampledValue;
                    AccountLineMicroOpReadWait(
                        requestedCycle,
                        access.CompletedCycle);
                    _lineMicroOpNextReadCycle = access.CompletedCycle;
                    _lineMicroOpNextCycle = Math.Max(
                        _lineMicroOpNextCycle,
                        access.CompletedCycle);
                }
                else
                {
                    _lineMicroOpNextCycle = Math.Max(
                        _lineMicroOpNextCycle,
                        access.CompletedCycle);
                    _lineLastDrawnY = _lineY;
                }
            }

            _lineMicroOpIndex++;
            if (_lineMicroOpIndex == _lineMicroOpCount - 1)
            {
                EnsureLineMicroOpOutputReady();
            }
        }

        private void CommitLiveBlitterTransition(
            LiveBlitterTransitionKind kind,
            long cycle,
            AgnusLiveTransitionPhase phase)
        {
            if (_bus.BusAccessCaptureEnabled &&
                phase == AgnusLiveTransitionPhase.AfterSlotCommit)
            {
                _liveBlitterAfterSlotTransitionCycles.Add(cycle);
            }

            var previousCompletionCycle = _lastCompletionCycle;
            switch (kind)
            {
                case LiveBlitterTransitionKind.AdvanceBOnlyCompletionSignal:
                    AdvanceBOnlyCompletionSignalThrough(cycle);
                    break;

                case LiveBlitterTransitionKind.AdvanceAreaStartup:
                    AdvanceAreaStartupInput(cycle);
                    break;

                case LiveBlitterTransitionKind.AdvanceAreaWordControl:
                    AdvanceAreaWordRetireControlInput(cycle);
                    break;

                case LiveBlitterTransitionKind.BeginAreaWord:
                    if (!BeginAreaMicroOpWord())
                    {
                        throw new InvalidOperationException(
                            "The live blitter could not begin its published area word.");
                    }

                    if (GetAreaMicroOpCount() != 0 &&
                        GetAreaMicroOp(0) == BlitterSlotQueueOp.WriteD)
                    {
                        EnsureAreaMicroOpOutputReady();
                    }
                    break;

                case LiveBlitterTransitionKind.PrepareAreaWrite:
                    EnsureAreaMicroOpOutputReady();
                    break;

                case LiveBlitterTransitionKind.LatchAreaPipelineWrite:
                    LatchLiveAreaPipelineWrite();
                    _areaMicroOpIndex++;
                    break;

                case LiveBlitterTransitionKind.BeginAreaFinalDDrain:
                    BeginLiveAreaFinalDDrain(cycle);
                    // BeginLiveAreaFinalDDrain publishes the architectural
                    // completion itself. Do not count that edge again in the
                    // common transition epilogue below.
                    return;

                case LiveBlitterTransitionKind.FinishAreaWord:
                    FinishAreaMicroOpWord(cycle);
                    _liveBlitterAreaWords++;
                    PrimeLiveBlitterMicroOpUnit();
                    break;

                case LiveBlitterTransitionKind.BeginLinePixel:
                    if (!BeginLineMicroOpPixel())
                    {
                        throw new InvalidOperationException(
                            "The live blitter could not begin its published line pixel.");
                    }
                    break;

                case LiveBlitterTransitionKind.PrepareLineWrite:
                    EnsureLineMicroOpOutputReady();
                    break;

                case LiveBlitterTransitionKind.FinishLinePixel:
                    FinishLineMicroOpPixel(cycle);
                    _liveBlitterLinePixels++;
                    PrimeLiveBlitterMicroOpUnit();
                    break;

                case LiveBlitterTransitionKind.FinalizeCompletion:
                    FinalizePendingCompletion();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }

            RecordLiveBlitterCompletion(previousCompletionCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordLiveBlitterCompletion(long previousCompletionCycle)
        {
            if (_lastCompletionCycle == previousCompletionCycle)
            {
                return;
            }

            _liveBlitterCompletions++;
            _liveBlitterInterrupts++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetAreaChannel(BlitterSlotQueueOp op)
            => op switch
            {
                BlitterSlotQueueOp.ReadA => 0,
                BlitterSlotQueueOp.ReadB => 1,
                BlitterSlotQueueOp.ReadC => 2,
                BlitterSlotQueueOp.WriteD => 3,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(op),
                    op,
                    null)
            };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetLineChannel(int index)
        {
            if (_useB && index < 2)
            {
                return 4 + index;
            }

            return index == (_useB ? 2 : 0) ? 6 : 7;
        }

        private sealed class LiveBlitterRequester : IAgnusLiveSlotRequester
        {
            private readonly Blitter _owner;
            private AgnusLiveRequestLatch _request;
            private AgnusLiveTransitionLatch _transition;

            public LiveBlitterRequester(Blitter owner)
            {
                _owner = owner;
                _request =
                    new AgnusLiveRequestLatch(AgnusChipSlotOwner.Blitter);
                _transition =
                    new AgnusLiveTransitionLatch(AgnusChipSlotOwner.Blitter);
            }

            public AgnusChipSlotOwner Owner =>
                AgnusChipSlotOwner.Blitter;

            public LiveBlitterTransitionKind TransitionKind { get; private set; }

            public bool TryPeekPendingRequest(
                out AgnusLiveSlotRequest request)
                => _request.TryPeek(out request);

            public bool TryPeekNextTransition(
                out AgnusLiveTransition transition)
                => _transition.TryPeek(out transition);

            public void CommitGrantedSlot(in AgnusLiveSlotGrant grant)
            {
                _request.Consume(grant);
                _owner.CommitLiveBlitterWord(grant);
            }

            public void CommitTransition(
                in AgnusLiveTransition transition)
            {
                _transition.Consume(transition);
                _owner.CommitLiveBlitterTransition(
                    TransitionKind,
                    transition.Cycle,
                    transition.Phase);
            }

            public void PublishWord(
                uint address,
                long requestedCycle,
                int channel,
                bool write,
                ushort writeValue)
            {
                var eligibleCycle = AgnusChipSlotScheduler.AlignToSlot(
                    Math.Max(0, requestedCycle));
                _request.Publish(
                    AmigaBusAccessKind.Blitter,
                    address,
                    requestedCycle,
                    eligibleCycle,
                    write
                        ? AgnusLiveWordTransfer.Write
                        : AgnusLiveWordTransfer.Read,
                    writeValue,
                    channel);
            }

            public void PublishTransition(
                LiveBlitterTransitionKind kind,
                long cycle,
                AgnusLiveTransitionPhase phase)
            {
                TransitionKind = kind;
                _transition.Publish(Math.Max(0, cycle), phase);
            }

            public void RetryDeniedWord(long observedSlotCycle)
            {
                if (!_request.TryPeek(out var pending))
                {
                    throw new InvalidOperationException(
                        "A denied blitter word must remain pending.");
                }

                _request.Cancel(pending.Generation);
                var retryCycle = AgnusChipSlotScheduler.AlignToSlot(
                    Math.Max(
                        pending.EarliestEligibleCycle +
                        AgnusChipSlotScheduler.SlotCycles,
                        observedSlotCycle +
                        AgnusChipSlotScheduler.SlotCycles));
                _request.Publish(
                    pending.Kind,
                    pending.Address,
                    pending.RequestedCycle,
                    retryCycle,
                    pending.Transfer,
                    pending.WriteValue,
                    pending.Channel);
            }

            public void DeferPendingWord(long earliestEligibleCycle)
            {
                if (!_request.TryPeek(out var pending))
                {
                    throw new InvalidOperationException(
                        "Only a pending blitter word can be deferred.");
                }

                _request.Cancel(pending.Generation);
                _request.Publish(
                    pending.Kind,
                    pending.Address,
                    pending.RequestedCycle,
                    earliestEligibleCycle,
                    pending.Transfer,
                    pending.WriteValue,
                    pending.Channel);
            }

            public void CancelPendingWord()
            {
                if (_request.TryPeek(out var pending))
                {
                    _request.Cancel(pending.Generation);
                }
            }

            public void CancelPendingTransition()
            {
                if (_transition.TryPeek(out var pending))
                {
                    _transition.Cancel(pending.Generation);
                }
            }

            public void Reset()
            {
                _request =
                    new AgnusLiveRequestLatch(AgnusChipSlotOwner.Blitter);
                _transition =
                    new AgnusLiveTransitionLatch(AgnusChipSlotOwner.Blitter);
                TransitionKind = default;
            }
        }

        internal System.Collections.Generic.IReadOnlyList<long>
            LiveAfterSlotTransitionCycles =>
                _liveBlitterAfterSlotTransitionCycles;
    }
}
