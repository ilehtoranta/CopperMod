/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        private long _liveCopperRequesterWaitComparisons;
        private long _liveCopperRequesterSkipComparisons;
        private long _liveCopperRequesterBfdBusyObservations;
        private long _liveCopperRequesterCommittedMoves;
        private long _liveCopperRequesterCommittedCopjmps;
        private long _liveCopperRequesterCommittedInterruptMoves;
        private long _liveCopperRequesterPublishedNextRequests;
        private long _liveCopperRequesterNextWaitComparisonCycle;
        private long _liveCopperRequesterLegacyStepCalls;
        private long _liveCopperRequesterCausalDeferrals;
        private long _liveCopperRequesterCausalDeferredCycles;
        private long _liveCopperRequesterMaxCausalLagCycles;
        private long _liveCopperRequesterFirstCausalDeferralCycle = -1;

        private enum LiveCopperTransitionKind : byte
        {
            Start,
            PendingMove,
            SkipComparison,
            WaitComparison,
            RestartArm,
            RestartReady
        }

        internal AgnusLiveCopperDeviceDiagnostics CaptureLiveCopperDeviceDiagnostics()
            => new(
                _liveCopperRequesterWaitComparisons,
                _liveCopperRequesterSkipComparisons,
                _liveCopperRequesterBfdBusyObservations,
                _liveCopperRequesterCommittedMoves,
                _liveCopperRequesterCommittedCopjmps,
                _liveCopperRequesterCommittedInterruptMoves,
                _liveCopperRequesterPublishedNextRequests,
                _liveCopperRequesterLegacyStepCalls,
                PredictionCalls: 0,
                EventDiscoveryCalls: 0,
                BlitterSynchronizationCalls: 0,
                SchedulerDrainCalls: 0,
                RangeAdvanceCalls: 0,
                CausalDeferrals: _liveCopperRequesterCausalDeferrals,
                CausalDeferredCycles: _liveCopperRequesterCausalDeferredCycles,
                MaxCausalLagCycles: _liveCopperRequesterMaxCausalLagCycles,
                FirstCausalDeferralCycle:
                    _liveCopperRequesterFirstCausalDeferralCycle);

        internal bool HasPublishedLiveCopperWordClaimAt(long slotCycle)
        {
            if (!_liveCopperRequesterEnabled)
            {
                return false;
            }

            slotCycle = AgnusChipSlotScheduler.AlignToSlot(
                Math.Max(0, slotCycle));
            var requester = RequireLiveCopperRequester();
            var previousTransitionCycle = -1L;
            var previousTransitionGeneration = 0UL;
            var hasPreviousTransition = false;
            var sameCycleTransitions = 0;
            while (true)
            {
                EnsureLiveCopperRequesterPublication(requester);
                if (requester.TryPeekPendingRequest(out var request))
                {
                    if (request.EarliestEligibleCycle > slotCycle)
                    {
                        return false;
                    }

                    if (request.Channel is 2 or 3)
                    {
                        return ((slotCycle /
                                 AgnusChipSlotScheduler.SlotCycles) & 1) ==
                            ((AgnusChipSlotScheduler.AlignToSlot(
                                request.RequestedCycle) /
                              AgnusChipSlotScheduler.SlotCycles) & 1);
                    }

                    return AgnusHrmOcsSlotTable.IsCopperAccessSlot(slotCycle);
                }

                if (!requester.TryPeekNextTransition(out var transition) ||
                    transition.Phase !=
                    AgnusLiveTransitionPhase.BeforeSlotSelection ||
                    transition.Cycle > slotCycle)
                {
                    return false;
                }

                if (hasPreviousTransition &&
                    transition.Generation == previousTransitionGeneration)
                {
                    throw new InvalidOperationException(
                        "Copper did not consume its before-slot transition generation.");
                }

                if (transition.Cycle < previousTransitionCycle)
                {
                    throw new InvalidOperationException(
                        "Copper moved backwards while settling its before-slot transitions.");
                }

                if (transition.Cycle == previousTransitionCycle)
                {
                    sameCycleTransitions++;
                    if (sameCycleTransitions >= 8)
                    {
                        throw new InvalidOperationException(
                            "Copper did not make cycle progress while settling its before-slot transitions.");
                    }
                }
                else
                {
                    sameCycleTransitions = 0;
                }

                // A WAIT comparison/restart can expose a Copper word on the
                // same physical slot. Settle those bus-free stages before the
                // blitter reserves that slot; otherwise call-site order lets
                // a lower-priority nice blitter steal Copper's grant.
                _bus.CommitLiveCopperTransition(requester);
                previousTransitionCycle = transition.Cycle;
                previousTransitionGeneration = transition.Generation;
                hasPreviousTransition = true;
            }
        }

		internal bool HasIncomingLiveCopperWordClaimAt(long slotCycle)
		{
			slotCycle = AgnusChipSlotScheduler.AlignToSlot(
				Math.Max(0, slotCycle));
			if (_bus.IsMandatoryRefreshSlot(slotCycle))
			{
				return false;
			}

			if (!_liveCopperRequesterEnabled)
			{
				return _liveCopper.PendingInstructionSecondWord &&
					_liveCopper.PendingInstructionSecondWordCycle == slotCycle;
			}

			var requester = RequireLiveCopperRequester();
			EnsureLiveCopperRequesterPublication(requester);
			if (!requester.TryPeekPendingRequest(out var request) ||
				request.EarliestEligibleCycle > slotCycle)
			{
				return false;
			}

			if (AgnusChipSlotScheduler.AlignToSlot(request.RequestedCycle) !=
				slotCycle - AgnusChipSlotScheduler.SlotCycles)
			{
				return false;
			}

			if (request.Channel is 0 or 3)
			{
				return false;
			}

			if (request.Channel == 2)
			{
				return ((slotCycle / AgnusChipSlotScheduler.SlotCycles) & 1) ==
					((AgnusChipSlotScheduler.AlignToSlot(request.RequestedCycle) /
					  AgnusChipSlotScheduler.SlotCycles) & 1);
			}

			return AgnusHrmOcsSlotTable.IsCopperAccessSlot(slotCycle);
		}

        private void ResetLiveCopperRequester(bool resetDiagnostics)
        {
            _liveCopperRequester?.Reset();
            if (!resetDiagnostics)
            {
                return;
            }

            _liveCopperRequesterWaitComparisons = 0;
            _liveCopperRequesterSkipComparisons = 0;
            _liveCopperRequesterBfdBusyObservations = 0;
            _liveCopperRequesterCommittedMoves = 0;
            _liveCopperRequesterCommittedCopjmps = 0;
            _liveCopperRequesterCommittedInterruptMoves = 0;
            _liveCopperRequesterPublishedNextRequests = 0;
            _liveCopperRequesterNextWaitComparisonCycle = 0;
            _liveCopperRequesterLegacyStepCalls = 0;
            _liveCopperRequesterCausalDeferrals = 0;
            _liveCopperRequesterCausalDeferredCycles = 0;
            _liveCopperRequesterMaxCausalLagCycles = 0;
            _liveCopperRequesterFirstCausalDeferralCycle = -1;
        }

        private long GetNextLiveCopperRequesterCycle()
        {
            var requester = RequireLiveCopperRequester();
            EnsureLiveCopperRequesterPublication(requester);
            if (requester.TryPeekPendingRequest(out var request))
            {
                return request.EarliestEligibleCycle;
            }

            return requester.TryPeekNextTransition(out var transition)
                ? transition.Cycle
                : long.MaxValue;
        }

        private void StepLiveCopperRequester(long targetCycle)
        {
            var requester = RequireLiveCopperRequester();
            EnsureLiveCopperRequesterPublication(requester);
            if (requester.TryPeekPendingRequest(out var request))
            {
                var executedBusHorizon = _bus.ExecutedChipBusHorizon;
                if (request.EarliestEligibleCycle < executedBusHorizon)
                {
                    var causalLag =
                        executedBusHorizon - request.EarliestEligibleCycle;
                    _liveCopperRequesterCausalDeferrals++;
                    _liveCopperRequesterCausalDeferredCycles += causalLag;
                    _liveCopperRequesterMaxCausalLagCycles = Math.Max(
                        _liveCopperRequesterMaxCausalLagCycles,
                        causalLag);
                    if (_liveCopperRequesterFirstCausalDeferralCycle < 0)
                    {
                        _liveCopperRequesterFirstCausalDeferralCycle =
                            request.EarliestEligibleCycle;
                    }

                    requester.DeferPendingWord(
                        AgnusChipSlotScheduler.AlignToSlot(
                            executedBusHorizon));
                    return;
                }

                if (request.EarliestEligibleCycle > targetCycle)
                {
                    return;
                }

                if (!_bus.TryCommitLiveCopperRequest(requester, out var observedSlotCycle))
                {
                    requester.RetryDeniedWord(observedSlotCycle);
                }

                return;
            }

            if (requester.TryPeekNextTransition(out var transition) &&
                transition.Cycle <= targetCycle)
            {
                _bus.CommitLiveCopperTransition(requester);
            }
        }

        private void EnsureLiveCopperRequesterPublication(LiveCopperRequester requester)
        {
            if (requester.TryPeekPendingRequest(out var publishedRequest))
            {
                var current =
                    publishedRequest.Channel is 0 or 3
                        ? !_liveCopper.PendingInstructionSecondWord &&
                          !_liveCopper.Stopped &&
                          IsLiveCopperDmaEnabled() &&
                          publishedRequest.Address == _liveCopper.Pc
                        : _liveCopper.PendingInstructionSecondWord &&
                          publishedRequest.Address ==
                          AddDmaPointerOffset(_liveCopper.Pc, 2);
                if (current)
                {
                    return;
                }

                requester.CancelPendingWord();
            }

            if (requester.TryPeekNextTransition(out _))
            {
                if (IsLiveCopperRequesterTransitionCurrent(
                    requester.TransitionKind))
                {
                    return;
                }

                requester.CancelPendingTransition();
            }

            if (_liveCopper.PendingInstructionSecondWord)
            {
                requester.PublishWord(
                    AddDmaPointerOffset(_liveCopper.Pc, 2),
                    _liveCopper.PendingInstructionSecondWordRequestedCycle,
                    GetLiveCopperRequesterEligibilityCycle(
                        _liveCopper.PendingInstructionSecondWordCycle),
                    _liveCopper.PendingInstructionSecondWordPreservePhysicalPhase
                        ? 2
                        : 1);
                _liveCopperRequesterPublishedNextRequests++;
                return;
            }

            if (_liveCopper.PendingMove)
            {
                requester.PublishTransition(
                    LiveCopperTransitionKind.PendingMove,
                    _liveCopper.PendingMoveCycle,
                    AgnusLiveTransitionPhase.AfterSlotCommit);
                return;
            }

            if (_liveCopper.PendingSkip)
            {
                requester.PublishTransition(
                    LiveCopperTransitionKind.SkipComparison,
                    _liveCopper.PendingSkipCycle,
                    AgnusLiveTransitionPhase.AfterSlotCommit);
                return;
            }

            if (_liveCopper.Stopped || !IsLiveCopperDmaEnabled())
            {
                return;
            }

            if (_liveCopper.PendingStart)
            {
                if (_copperListPointer != 0)
                {
                    requester.PublishTransition(
                        LiveCopperTransitionKind.Start,
                        Math.Max(_liveCopper.Cycle, _liveFrameStartCycle),
                        AgnusLiveTransitionPhase.BeforeSlotSelection);
                }

                return;
            }

            if (_liveCopper.RestartArmed)
            {
                requester.PublishTransition(
                    LiveCopperTransitionKind.RestartArm,
                    _liveCopper.Cycle,
                    AgnusLiveTransitionPhase.BeforeSlotSelection);
                return;
            }

            if (_liveCopper.ReadyToRequest)
            {
                requester.PublishTransition(
                    LiveCopperTransitionKind.RestartReady,
                    _liveCopper.Cycle,
                    AgnusLiveTransitionPhase.BeforeSlotSelection);
                return;
            }

            if (_liveCopper.Waiting)
            {
                requester.PublishTransition(
                    LiveCopperTransitionKind.WaitComparison,
                    Math.Max(
                        _liveCopperRequesterNextWaitComparisonCycle,
                        _liveFrameStartCycle),
                    AgnusLiveTransitionPhase.BeforeSlotSelection);
                return;
            }

            if (_liveCopper.Pc == 0 && _copperListPointer == 0)
            {
                return;
            }

            var requestedCycle = Math.Max(_liveCopper.Cycle, _liveFrameStartCycle);
            requester.PublishWord(
                _liveCopper.Pc,
                requestedCycle,
                GetLiveCopperRequesterEligibilityCycle(requestedCycle),
                phase: _liveCopper.PreserveFirstWordPhysicalPhase ? 3 : 0);
            _liveCopperRequesterPublishedNextRequests++;
        }

        private bool IsLiveCopperRequesterTransitionCurrent(
            LiveCopperTransitionKind kind)
            => kind switch
            {
                LiveCopperTransitionKind.Start =>
                    _liveCopper.PendingStart && IsLiveCopperDmaEnabled(),
                LiveCopperTransitionKind.PendingMove => _liveCopper.PendingMove,
                LiveCopperTransitionKind.SkipComparison => _liveCopper.PendingSkip,
                LiveCopperTransitionKind.WaitComparison => _liveCopper.Waiting,
                LiveCopperTransitionKind.RestartArm => _liveCopper.RestartArmed,
                LiveCopperTransitionKind.RestartReady => _liveCopper.ReadyToRequest,
                _ => false
            };

        private void CommitLiveCopperRequesterWord(
            in AgnusLiveSlotGrant grant,
            int phase)
        {
            var access = CreateLiveCopperAccess(grant);
            RecordLiveCopperBitplaneRgaCollision(grant.SlotCycle);
            if (phase is 0 or 3)
            {
                _liveCopper.PreserveFirstWordPhysicalPhase = false;
                var first = grant.SampledValue;
                var secondRequestedCycle = _liveCopper.ConsumeWaitStartTail(
                    first,
                    GetCopperSecondWordRequestCycle(access));
                _liveCopper.PendingInstructionSecondWord = true;
                _liveCopper.PendingInstructionFirst = first;
                _liveCopper.PendingInstructionFirstAccess = access;
                _liveCopper.PendingInstructionSecondWordRequestedCycle =
                    secondRequestedCycle;
                _liveCopper.PendingInstructionSecondWordCycle =
                    AgnusChipSlotScheduler.AlignToSlot(secondRequestedCycle);
                _liveCopper.PendingInstructionSecondWordPreservePhysicalPhase =
                    _bus.GetBeamPosition(access.GrantedCycle).BeamLine !=
                    _bus.GetBeamPosition(secondRequestedCycle).BeamLine;
                _liveCopper.Cycle = _liveCopper.PendingInstructionSecondWordCycle;
                InvalidateLiveDisplayEventCycle();
                return;
            }

            var instruction = new CopperInstructionLatch(
                _liveCopper.PendingInstructionFirst,
                _liveCopper.PendingInstructionFirstAccess,
                grant.SampledValue,
                access,
                CopperHpCycles);
            _liveCopper.PendingInstructionSecondWord = false;
            _liveCopper.Pc = AddDmaPointerOffset(_liveCopper.Pc, 4);
            CommitLiveCopperRequesterInstruction(in instruction);
            InvalidateLiveDisplayEventCycle();
        }

        private void CommitLiveCopperRequesterInstruction(
            in CopperInstructionLatch instruction)
        {
            if (instruction.IsEnd)
            {
                _liveCopper.Stopped = true;
                _liveCopper.Cycle = instruction.MoveStopCycle;
                return;
            }

            if (instruction.IsMove)
            {
                var suppressMove = _liveCopper.SuppressNextMove;
                _liveCopper.SuppressNextMove = false;
                ApplyLiveCopperRequesterMove(
                    instruction.MoveRegister,
                    instruction.Second,
                    instruction.DataCycle,
                    instruction.MoveStopCycle,
                    suppressMove);
                if (!_liveCopper.Stopped)
                {
                    _liveCopper.CompleteMove(instruction.MoveStopCycle);
                }

                return;
            }

            if (instruction.IsWait)
            {
                _liveCopper.Cycle = instruction.ControlStopCycle;
                _liveCopper.Wait(instruction.First, instruction.Second);
                _liveCopperRequesterNextWaitComparisonCycle =
                    instruction.ControlStopCycle;
                return;
            }

            _liveCopper.PendingSkip = true;
            _liveCopper.PendingSkipFirst = instruction.First;
            _liveCopper.PendingSkipSecond = instruction.Second;
            _liveCopper.PendingSkipCycle = instruction.ControlStopCycle;
            _liveCopper.Cycle = instruction.ControlStopCycle;
        }

        private void CommitLiveCopperRequesterTransition(
            LiveCopperTransitionKind kind,
            long cycle)
        {
            switch (kind)
            {
                case LiveCopperTransitionKind.Start:
                    if (_copperListPointer != 0)
                    {
                        _liveCopper.StartFrom(_copperListPointer);
                    }
                    break;

                case LiveCopperTransitionKind.PendingMove:
                    CompleteLiveCopperRequesterPendingMove(cycle);
                    break;

                case LiveCopperTransitionKind.SkipComparison:
                    CommitLiveCopperRequesterSkip(cycle);
                    break;

                case LiveCopperTransitionKind.WaitComparison:
                    CommitLiveCopperRequesterWaitComparison(cycle);
                    break;

                case LiveCopperTransitionKind.RestartArm:
                    CommitLiveCopperRequesterRestartArm(cycle);
                    break;

                case LiveCopperTransitionKind.RestartReady:
                    _liveCopper.AdvanceWaitRestartStage(_liveCopper.Cycle);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

            InvalidateLiveDisplayEventCycle();
        }

        private void CommitLiveCopperRequesterSkip(long cycle)
        {
            _liveCopperRequesterSkipComparisons++;
            var first = _liveCopper.PendingSkipFirst;
            var second = _liveCopper.PendingSkipSecond;
            _liveCopper.PendingSkip = false;
            if (IsCopperComparisonSatisfied(
                first,
                second,
                _liveFrameStartCycle,
                cycle,
                IsCopperBlitterFinishedForWait(second)))
            {
                _liveCopper.SuppressNextMove = true;
            }

            _liveCopper.Cycle = cycle;
        }

        private void CommitLiveCopperRequesterWaitComparison(long cycle)
        {
            _liveCopperRequesterWaitComparisons++;
            var bfdEnabled = (_liveCopper.WaitSecond & 0x8000) == 0;
            if (bfdEnabled && _bus.Blitter.BusPipelineActive)
            {
                _liveCopper.WaitObservedBlitterBusy = true;
                _liveCopperRequesterBfdBusyObservations++;
                _liveCopperRequesterNextWaitComparisonCycle =
                    cycle + CopperHpCycles;
                return;
            }

            if (!IsCopperComparisonSatisfied(
                _liveCopper.WaitFirst,
                _liveCopper.WaitSecond,
                _liveFrameStartCycle,
                cycle,
                blitterFinished: true))
            {
                _liveCopperRequesterNextWaitComparisonCycle =
                    cycle + CopperHpCycles;
                return;
            }

            var bfdReleasedByTermination =
                _liveCopper.WaitObservedBlitterBusy;
            var waitCycle = bfdReleasedByTermination
                ? Math.Max(
                    _liveCopper.Cycle,
                    _bus.Blitter.LastTerminationCycle)
                : cycle;
            var comparisonStartCycle = _liveCopper.WaitObservedBlitterBusy
                ? waitCycle
                : _liveCopper.Cycle;
            if (_liveCopper.SatisfiedWaitRunCount == 0)
            {
                _liveCopper.WaitRunBeganWithBlockingComparison =
                    waitCycle > comparisonStartCycle;
            }
            var resumeCycle = bfdReleasedByTermination
                ? GetCopperBfdReleaseFetchCycle(waitCycle)
                : GetCopperWaitRestartArmCycle(
                    waitCycle,
                    _liveCopper.WaitSecond,
                    observedBlitterBusy: false);
            var readyAtWakeCycle =
                bfdReleasedByTermination ||
                (waitCycle > comparisonStartCycle &&
                 (_liveCopper.WaitFirst & 0x00FE) != 0 &&
                 !IsCopperVerticalComparatorWrapWait(
                     _liveCopper.WaitFirst,
                     _liveCopper.WaitSecond) &&
                 (!IsBitplaneDmaEnabled(_dmacon) ||
                  GetAgnusBitplaneFetchPlaneCount() == 0));
            if (readyAtWakeCycle && !bfdReleasedByTermination)
            {
                // ReadyToRequest bypasses RestartArmed, so preserve the
                // internal restart control phase after the beam comparison.
                resumeCycle += AgnusChipSlotScheduler.SlotCycles;
            }
            var beamSatisfiedAtBlitterTermination =
                !bfdReleasedByTermination ||
                IsCopperComparisonSatisfied(
                    _liveCopper.WaitFirst,
                    _liveCopper.WaitSecond,
                    _liveFrameStartCycle,
                    _bus.Blitter.LastTerminationCycle,
                    blitterFinished: true);
            if (bfdReleasedByTermination &&
                beamSatisfiedAtBlitterTermination &&
                GetCopperHorizontalForCycle(
                    _liveFrameStartCycle,
                    _bus.Blitter.LastTerminationCycle) >= DefaultDdfStart)
            {
                // A pre-origin release establishes the line baseline. Only a
                // release in the active presentation interval exposes this
                // internal control phase on the following MOVE.
                _liveCopper.PendingWaitPresentationPixelOffset =
                    CopperBfdReleasePresentationPixelOffset;
            }
            else if (!bfdReleasedByTermination &&
                readyAtWakeCycle &&
                beamSatisfiedAtBlitterTermination)
            {
                _liveCopper.PendingWaitPresentationPixelOffset =
                    CopperWaitReadyPresentationPixelOffset;
            }
            if (readyAtWakeCycle && !bfdReleasedByTermination)
            {
                if (_bus.Blitter.BusPipelineActive &&
                    _bus.BlitterNastyPriorityEnabled)
                {
                    // A nasty blitter owns the incoming RGA phase while the
                    // CPU request remains hidden. Copper still outranks its
                    // word request, but its first fetch is published on the
                    // following slot. This preserves the pending CPU's exact
                    // BLTPRI-clear boundary instead of releasing it one slot
                    // early. Nice mode already exposes the CPU request and
                    // therefore retains the ordinary wake phase.
                    resumeCycle += AgnusChipSlotScheduler.SlotCycles;
                }

            }
            var reuseVerticalWrapControlPhase =
                !bfdReleasedByTermination &&
                IsCopperVerticalComparatorWrapWait(
                    _liveCopper.WaitFirst,
                    _liveCopper.WaitSecond);
            if (reuseVerticalWrapControlPhase)
            {
                resumeCycle -= AgnusChipSlotScheduler.SlotCycles;
            }
            if (!bfdReleasedByTermination &&
                (_liveCopper.WaitFirst & 0x00FE) <= 2)
            {
                // The first fetch emerging from a line-start WAIT is already
                // carried by the incoming Copper pipeline phase. It remains a
                // normal arbitrated transfer, but is not re-phased as a new
                // steady-state opcode request.
                _liveCopper.PreserveFirstWordPhysicalPhase = true;
            }

            var fourPlaneWait = GetAgnusBitplaneFetchPlaneCount() == 4;
            var fourPlanePreTailWait =
                fourPlaneWait &&
                (_liveCopper.WaitFirst & 0x00FE) < 0x00C0;
            var fourPlaneTailRunPhaseReusable =
                IsFourPlaneWaitTailRunPhaseReusable(_liveCopper.WaitFirst);
            if (fourPlaneTailRunPhaseReusable &&
                waitCycle > comparisonStartCycle &&
                _liveCopper.WaitRunContinuesFromDifferentInstruction &&
                _liveCopper.WaitRunCrossedIntoLineTail)
            {
                _liveCopper.WaitRunCrossedIntoLineTailAfterBlockingComparison = true;
                resumeCycle -= 4L * AgnusChipSlotScheduler.SlotCycles;
            }
            if (fourPlanePreTailWait &&
                waitCycle > comparisonStartCycle &&
                IsBitplaneRgaIncomingPhase(waitCycle))
            {
                _liveCopper.PendingWaitPreTailPixelOffset =
                    (_liveCopper.WaitFirst & 0x0002) * 2;
            }

            var fourPlaneWaitRunContinuation =
                (fourPlanePreTailWait || fourPlaneTailRunPhaseReusable) &&
                waitCycle <= comparisonStartCycle &&
                (_liveCopper.SatisfiedWaitRunCount > 0 ||
                 _liveCopper.WaitRunContinuesFromDifferentInstruction);
            var fourPlaneWaitRunReusesControlPhase =
                fourPlaneWaitRunContinuation &&
                (!_liveCopper.WaitRunContinuesFromDifferentInstruction ||
                 ((_liveCopper.WaitFirst & 0x00FE) -
                  (_liveCopper.PreviousWaitRunFirst & 0x00FE)) <= 12);
            var fourPlaneRunControlBlocked =
                _liveCopper.WaitRunControlBlocked ||
                (_liveCopper.WaitRunContinuesFromDifferentInstruction &&
                 _liveCopper.PreviousWaitRunControlBlocked);
            if (fourPlaneWaitRunContinuation &&
                (fourPlaneRunControlBlocked ||
                 (_liveCopper.WaitRunContinuesFromDifferentInstruction &&
                  (_liveCopper.PreviousWaitRunFirst & 0xFF00) ==
                  (_liveCopper.WaitFirst & 0xFF00) &&
                  ((_liveCopper.PreviousWaitRunFirst & 0x0002) == 0 ||
                   (_liveCopper.WaitFirst & 0x00F0) == 0x00B0))))
            {
                resumeCycle -= AgnusChipSlotScheduler.SlotCycles;
            }

            var restartIncomingRgaBlocked = false;
            var inheritedAdjacentControlPhaseCreated = false;
            if (waitCycle > comparisonStartCycle)
            {
                var incomingRgaBlocked =
                    IsBitplaneRgaIncomingPhase(waitCycle);
                var overlapsAdjacentRgaStage =
                    IsBitplaneRgaDecisionPhase(waitCycle) ||
                    IsBitplaneRgaOutputPhase(waitCycle);
                _liveCopper.WaitStartCarrySkipCount =
                    (byte)(overlapsAdjacentRgaStage ? 2 : 0);
                _liveCopper.WaitStartCarryPending = incomingRgaBlocked;
                _liveCopper.WaitStartCarryAdjacent =
                    incomingRgaBlocked && overlapsAdjacentRgaStage;
                _liveCopper.ArmWaitStartTailAfterMove =
                    incomingRgaBlocked &&
                    ShouldCarryWaitStartTailThroughMove(
                        _liveCopper.WaitFirst,
                        resumeCycle) &&
                    !(GetAgnusBitplaneFetchPlaneCount() == 4 &&
                      (_liveCopper.WaitFirst & 0x00FE) < 0x00C0);
                restartIncomingRgaBlocked =
                    GetNextLiveCopperControlPhase(
                        resumeCycle,
                        incomingRgaBlocked,
                        overlapsAdjacentRgaStage) > resumeCycle;
            }
            else if (_liveCopper.WaitStartCarryPending)
            {
                if (_liveCopper.WaitStartCarrySkipCount > 0)
                {
                    _liveCopper.WaitStartCarrySkipCount--;
                    if (!IsCopperWaitRestartAtPhysicalLineTail(resumeCycle))
                    {
                        _liveCopper.PendingWaitStartTail = true;
                    }
                }
                else
                {
                    restartIncomingRgaBlocked = true;
                    _liveCopper.WaitStartCarryPending = false;
                    if (_liveCopper.WaitStartCarryAdjacent)
                    {
                        _liveCopper.WaitInheritedAdjacentControlPhase = true;
                        _liveCopper.WaitInheritedAdjacentFirst =
                            _liveCopper.WaitFirst;
                        _liveCopper.WaitInheritedAdjacentSecond =
                            _liveCopper.WaitSecond;
                        inheritedAdjacentControlPhaseCreated = true;
                    }

                    _liveCopper.WaitStartCarryAdjacent = false;
                }
            }
            else
            {
                _liveCopper.WaitStartCarryPending = false;
                _liveCopper.WaitStartCarrySkipCount = 0;
            }

            if (fourPlaneWaitRunContinuation)
            {
                restartIncomingRgaBlocked = false;
                _liveCopper.WaitStartCarryPending = false;
                _liveCopper.WaitStartCarryAdjacent = false;
                _liveCopper.WaitRunControlBlocked = fourPlaneRunControlBlocked;
            }

            _liveCopper.WaitRunControlBlocked |= restartIncomingRgaBlocked;
            if (_liveCopper.SatisfiedWaitRunCount > 0 &&
                !_liveCopper.WaitRunControlBlocked &&
                !fourPlanePreTailWait)
            {
                _liveCopper.PendingWaitStartTail = true;
            }

            var reuseInheritedAdjacentControlPhase =
                !inheritedAdjacentControlPhaseCreated &&
                _liveCopper.WaitInheritedAdjacentControlPhase &&
                _liveCopper.WaitInheritedAdjacentFirst == _liveCopper.WaitFirst &&
                _liveCopper.WaitInheritedAdjacentSecond ==
                _liveCopper.WaitSecond;
            var restartAfterDataFetchStop =
                IsCopperWaitRestartAfterDataFetchStop(resumeCycle);
            var reuseExhaustedAdjacentCarryPhase =
                _liveCopper.WaitStartCarryPending &&
                _liveCopper.WaitStartCarryAdjacent &&
                _liveCopper.WaitStartCarrySkipCount == 0 &&
                (restartAfterDataFetchStop ||
                 (_liveCopper.SatisfiedWaitRunCount >= 2 &&
                  !IsCopperWaitRestartAtPhysicalLineTail(resumeCycle) &&
                  !DidCopperWaitRestartCrossPhysicalLine(
                      comparisonStartCycle,
                      resumeCycle)));
            if (!inheritedAdjacentControlPhaseCreated)
            {
                _liveCopper.WaitInheritedAdjacentControlPhase = false;
            }

            var reuseRunControlPhase =
                !restartIncomingRgaBlocked &&
                (reuseInheritedAdjacentControlPhase ||
                 reuseExhaustedAdjacentCarryPhase ||
                 reuseVerticalWrapControlPhase ||
                 fourPlaneWaitRunReusesControlPhase ||
                 (_liveCopper.WaitRunControlBlocked &&
                  _liveCopper.SatisfiedWaitRunCount >= 2 &&
                  (IsCopperWaitRestartAtPhysicalLineTail(resumeCycle) ||
                   DidCopperWaitRestartCrossPhysicalLine(
                       comparisonStartCycle,
                       resumeCycle))));
            var presentNextMoveFromReusedWaitTail =
                reuseRunControlPhase &&
                (IsCopperWaitRestartAtPhysicalLineTail(resumeCycle) ||
                 DidCopperWaitRestartCrossPhysicalLine(
                     comparisonStartCycle,
                     resumeCycle));
            var projectCrossedLineTailRun =
                presentNextMoveFromReusedWaitTail &&
                _liveCopper.WaitRunCrossedIntoLineTail &&
                (_liveCopper.WaitFirst & 0x00FE) == 0x00C4;
            if (reuseRunControlPhase &&
                _liveCopper.WaitRunCrossedIntoLineTailAfterBlockingComparison &&
                !projectCrossedLineTailRun)
            {
                _liveCopper.PendingWaitPresentationPixelOffset =
                    CopperWaitReadyPresentationPixelOffset;
            }
            var waitTailPresentationX =
                projectCrossedLineTailRun
                    ? Math.Max(
                        0,
                        GetCopperWaitTailPresentationX(_liveCopper.WaitFirst) -
                        (_liveCopper.SatisfiedWaitRunCount * 8))
                    : presentNextMoveFromReusedWaitTail ||
                      GetAgnusBitplaneFetchPlaneCount() != 4
                    ? -1
                    : GetCopperWaitTailPresentationX(_liveCopper.WaitFirst);
            presentNextMoveFromReusedWaitTail &= !projectCrossedLineTailRun;
            var restoreControlPhaseAfterMove =
                reuseExhaustedAdjacentCarryPhase &&
                !restartAfterDataFetchStop &&
                !presentNextMoveFromReusedWaitTail;
            CaptureCopperWaitTransition(
                comparisonStartCycle,
                resumeCycle,
                waitCycle,
                restartIncomingRgaBlocked,
                inheritedAdjacentControlPhaseCreated,
                reuseInheritedAdjacentControlPhase,
                reuseRunControlPhase);
            _liveCopper.ArmWaitRestart(
                resumeCycle,
                restartIncomingRgaBlocked,
                reuseRunControlPhase,
                restoreControlPhaseAfterMove,
                presentNextMoveFromReusedWaitTail,
                readyAtWakeCycle);
            _liveCopper.PendingWaitTailPresentationX =
                waitTailPresentationX;
            _liveCopper.PendingWaitPaletteTailX =
                GetCopperWaitPaletteTailX(_liveCopper.WaitFirst);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryCommitUnsatisfiedLiveCopperRequesterWaitComparison(
            LiveCopperRequester requester,
            long cycle,
            long targetCycle)
        {
            if (cycle > targetCycle ||
                !requester.IsCurrentWaitComparisonAt(cycle))
            {
                return false;
            }

            var bfdBusy =
                (_liveCopper.WaitSecond & 0x8000) == 0 &&
                _bus.Blitter.BusPipelineActive;
            if (!bfdBusy &&
                IsCopperComparisonSatisfied(
                    _liveCopper.WaitFirst,
                    _liveCopper.WaitSecond,
                    _liveFrameStartCycle,
                    cycle,
                    blitterFinished: true))
            {
                return false;
            }

            requester.ConsumeCurrentWaitComparison(cycle);
            _bus.RecordPrevalidatedLiveCopperTransitionCommit();
            _liveCopperRequesterWaitComparisons++;
            if (bfdBusy)
            {
                _liveCopper.WaitObservedBlitterBusy = true;
                _liveCopperRequesterBfdBusyObservations++;
            }

            _liveCopperRequesterNextWaitComparisonCycle =
                cycle + CopperHpCycles;
            InvalidateLiveDisplayEventCycle();
            return true;
        }

        private static long GetNextLiveCopperControlPhase(
            long cycle,
            bool incomingRgaBlocked,
            bool adjacentRgaStageBlocked)
        {
            var phaseCycle =
                AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, cycle));
            if (!incomingRgaBlocked)
            {
                return phaseCycle;
            }

            var horizontal = AgnusHrmOcsSlotTable.GetHorizontal(phaseCycle);
            var refresh =
                AgnusHrmOcsSlotTable.GetFixedOwner(horizontal) ==
                AgnusChipSlotOwner.Refresh;
            return adjacentRgaStageBlocked ||
                   refresh ||
                   (horizontal & 0x02) != 0
                ? phaseCycle + (2L * AgnusChipSlotScheduler.SlotCycles)
                : phaseCycle;
        }

        private void CommitLiveCopperRequesterRestartArm(long cycle)
        {
            if (_liveCopper.WaitRestartIncomingRgaBlocked)
            {
                _liveCopper.WaitRestartIncomingRgaBlocked = false;
                _liveCopper.Cycle = cycle + (2L * AgnusChipSlotScheduler.SlotCycles);
                return;
            }

            _liveCopper.AdvanceWaitRestartStage(
                cycle + (2L * AgnusChipSlotScheduler.SlotCycles));
        }

        private void CompleteLiveCopperRequesterPendingMove(long cycle)
        {
            if (!_liveCopper.PendingMove)
            {
                return;
            }

            var register = _liveCopper.PendingMoveRegister;
            var value = _liveCopper.PendingMoveValue;
            var stopCycle = _liveCopper.PendingMoveStopCycle;
            var suppress = _liveCopper.PendingMoveSuppress;
            _liveCopper.PendingMove = false;
            ApplyLiveCopperRequesterMove(register, value, cycle, stopCycle, suppress);
            if (!_liveCopper.Stopped)
            {
                _liveCopper.CompleteMove(stopCycle);
            }
        }

        private void ApplyLiveCopperRequesterMove(
            ushort register,
            ushort value,
            long dataCycle,
            long instructionStopCycle,
            bool suppressMove)
        {
            if (IsCopperDangerStopRegister(register))
            {
                _liveCopper.Stopped = true;
                _liveCopper.Cycle = instructionStopCycle;
                return;
            }

            if (!suppressMove && CanCopperWriteRegister(register))
            {
                RecordCopperQuiescentCopperMove(dataCycle, register);
                var affectsDisplay = IsDisplayRegisterWrite(register);
                if (affectsDisplay)
                {
                    _currentCopperRow = GetOutputRowForCycle(
                        _liveFrameStartCycle,
                        dataCycle);
                    AdvanceLiveDisplayWindowStateToCycle(dataCycle);
                    EnsureTimelineLineStartedBeforeDisplayWrite(dataCycle);
                }

                CommitLateBitplaneRgaCollisionsOnDisable(register, value, dataCycle);
                ApplyCopperMove(
                    register,
                    value,
                    dataCycle,
                    applyHardwareSideEffects: true);
                if (affectsDisplay)
                {
                    CaptureCopperDisplayWrite(dataCycle, register, value);
                    RecordLiveFrameWrite(dataCycle, register, value, isCopper: true);
                    RefreshLiveLineStateAfterDisplayStateChange(dataCycle, register);
                    RecordTimelineDisplayWrite(
                        dataCycle,
                        register,
                        value,
                        isCopper: true);
                }

                _liveCopperRequesterCommittedMoves++;
                if (register == 0x088)
                {
                    _liveCopper.JumpTo(_copperListPointer, dataCycle);
                    _liveCopperRequesterCommittedCopjmps++;
                }
                else if (register == 0x08A)
                {
                    _liveCopper.JumpTo(_copperListPointer2, dataCycle);
                    _liveCopperRequesterCommittedCopjmps++;
                }

                if (register == 0x09C &&
                    (value & AmigaConstants.IntreqCopper) != 0)
                {
                    _liveCopperRequesterCommittedInterruptMoves++;
                }

                _liveWakeVersion++;
            }

            _liveCopper.PresentNextMoveFromReusedWaitTail = false;
            _liveCopper.Cycle = instructionStopCycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AmigaBusAccessResult CreateLiveCopperAccess(
            in AgnusLiveSlotGrant grant)
        {
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                AmigaBusAccessTarget.ChipRam,
                grant.Request.Address,
                AmigaBusAccessSize.Word,
                grant.Request.RequestedCycle,
                isWrite: false);
            return new AmigaBusAccessResult(
                request,
                grant.SlotCycle,
                grant.CompletedCycle);
        }

        private LiveCopperRequester RequireLiveCopperRequester()
            => _liveCopperRequester ??
                throw new InvalidOperationException(
                    "The G2L live Copper requester is not enabled.");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetLiveCopperRequesterEligibilityCycle(long requestedCycle)
        {
            var afterCommittedBus = _bus.ExecutedChipBusHorizon == long.MinValue
                ? 0
                : _bus.ExecutedChipBusHorizon + 1;
            return AgnusChipSlotScheduler.AlignToSlot(
                Math.Max(requestedCycle, afterCommittedBus));
        }

        private sealed class LiveCopperRequester : IAgnusLiveSlotRequester
        {
            private readonly Display _display;
            private AgnusLiveRequestLatch _requestLatch =
                new(AgnusChipSlotOwner.Copper);
            private AgnusLiveTransitionLatch _transitionLatch =
                new(AgnusChipSlotOwner.Copper);
            private LiveCopperTransitionKind _transitionKind;
            private int _phase;
            private ulong _requestGeneration;

            public LiveCopperRequester(Display display)
            {
                _display = display;
            }

            public AgnusChipSlotOwner Owner => AgnusChipSlotOwner.Copper;

            public LiveCopperTransitionKind TransitionKind => _transitionKind;

            public void Reset()
            {
                _requestLatch = new AgnusLiveRequestLatch(AgnusChipSlotOwner.Copper);
                _transitionLatch =
                    new AgnusLiveTransitionLatch(AgnusChipSlotOwner.Copper);
                _transitionKind = default;
                _phase = 0;
                _requestGeneration = 0;
            }

            public void PublishWord(
                uint address,
                long requestedCycle,
                long eligibleCycle,
                int phase)
            {
                _phase = phase;
                var request = _requestLatch.Publish(
                    AmigaBusAccessKind.Copper,
                    address,
                    requestedCycle,
                    Math.Max(requestedCycle, eligibleCycle),
                    AgnusLiveWordTransfer.Read,
                    channel: phase);
                _requestGeneration = request.Generation;
            }

            public void PublishTransition(
                LiveCopperTransitionKind kind,
                long cycle,
                AgnusLiveTransitionPhase phase)
            {
                _transitionKind = kind;
                _transitionLatch.Publish(cycle, phase);
            }

            public bool TryPeekPendingRequest(out AgnusLiveSlotRequest request)
                => _requestLatch.TryPeek(out request);

            public bool TryPeekNextTransition(out AgnusLiveTransition transition)
                => _transitionLatch.TryPeek(out transition);

            public void CommitGrantedSlot(in AgnusLiveSlotGrant grant)
            {
                _requestLatch.Consume(grant);
                _display.CommitLiveCopperRequesterWord(grant, _phase);
            }

            public void CommitTransition(in AgnusLiveTransition transition)
            {
                _transitionLatch.Consume(transition);
                _display.CommitLiveCopperRequesterTransition(
                    _transitionKind,
                    transition.Cycle);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsCurrentWaitComparisonAt(long cycle)
                => _transitionKind == LiveCopperTransitionKind.WaitComparison &&
                   _transitionLatch.TryPeek(out var transition) &&
                   transition.Cycle == cycle &&
                   transition.Phase ==
                       AgnusLiveTransitionPhase.BeforeSlotSelection;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ConsumeCurrentWaitComparison(long cycle)
            {
                if (_requestLatch.TryPeek(out _))
                {
                    throw new InvalidOperationException(
                        "Copper cannot commit a WAIT transition beside a pending word.");
                }

                if (!_transitionLatch.TryPeek(out var transition) ||
                    _transitionKind != LiveCopperTransitionKind.WaitComparison ||
                    transition.Cycle != cycle ||
                    transition.Phase !=
                        AgnusLiveTransitionPhase.BeforeSlotSelection)
                {
                    throw new InvalidOperationException(
                        "The live Copper requester has no current WAIT comparison transition.");
                }

                _transitionLatch.Consume(transition);
            }

            public void RetryDeniedWord(long observedSlotCycle)
            {
                var denied = _requestLatch.Cancel(_requestGeneration);
                var retryCycle = Math.Max(
                    denied.EarliestEligibleCycle + AgnusChipSlotScheduler.SlotCycles,
                    observedSlotCycle + AgnusChipSlotScheduler.SlotCycles);
                PublishWord(
                    denied.Address,
                    denied.RequestedCycle,
                    retryCycle,
                    _phase);
                if (_phase is 0 or 3)
                {
                    _display._liveCopper.Cycle = retryCycle;
                }
                else
                {
                    _display._liveCopper.PendingInstructionSecondWordCycle = retryCycle;
                    _display._liveCopper.Cycle = retryCycle;
                }

                _display.InvalidateLiveDisplayEventCycle();
            }

            public void DeferPendingWord(long earliestEligibleCycle)
            {
                var pending = _requestLatch.Cancel(_requestGeneration);
                PublishWord(
                    pending.Address,
                    pending.RequestedCycle,
                    earliestEligibleCycle,
                    _phase);
                if (_phase is 0 or 3)
                {
                    _display._liveCopper.Cycle = earliestEligibleCycle;
                }
                else
                {
                    _display._liveCopper.PendingInstructionSecondWordCycle =
                        earliestEligibleCycle;
                    _display._liveCopper.Cycle = earliestEligibleCycle;
                }

                _display.InvalidateLiveDisplayEventCycle();
            }

            public void CancelPendingWord()
            {
                if (_requestLatch.HasPending)
                {
                    _requestLatch.Cancel(_requestGeneration);
                }
            }

            public void CancelPendingTransition()
            {
                if (_transitionLatch.TryPeek(out var transition))
                {
                    _transitionLatch.Cancel(transition.Generation);
                }
            }
        }
    }
}
