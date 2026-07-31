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
        private bool _liveAreaPendingDValid;
        private ushort _liveAreaPendingDValue;
        private bool _liveAreaPendingDCompletesRow;
        private bool _liveAreaPendingDCompletesBlit;
        private bool _liveAreaFinalDDrainActive;
        private long _liveAreaFinalDDrainRequestCycle;

        private enum LiveBlitterTransitionKind : byte
        {
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
            _liveAreaFinalDDrainActive = false;
            _liveAreaFinalDDrainRequestCycle = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool UsesLiveAreaDPipeline()
            => _bus.AgnusLiveBlitterEnabled &&
                !_lineMode &&
                (_useD ||
                 _liveAreaPendingDValid ||
                 _liveAreaFinalDDrainActive);

        private void PrimeLiveBlitterMicroOpUnit()
        {
            if (!_bus.AgnusLiveBlitterEnabled ||
                !_busy ||
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

                var causalEligibleCycle =
                    AgnusChipSlotScheduler.AlignToSlot(
                        Math.Max(
                            request.EarliestEligibleCycle,
                            _bus.ExecutedChipBusHorizon));
                if (causalEligibleCycle > request.EarliestEligibleCycle)
                {
                    requester.DeferPendingWord(causalEligibleCycle);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PublishLiveBlitterWakeChange()
        {
            _wakeVersion++;
            _schedulerWakeVersion++;
        }

        private void EnsureLiveBlitterRequesterPublication(
            LiveBlitterRequester requester)
        {
            if (requester.TryPeekPendingRequest(out var request))
            {
                if (IsLiveBlitterRequestCurrent(request))
                {
                    return;
                }

                requester.CancelPendingWord();
            }

            if (requester.TryPeekNextTransition(out _))
            {
                if (IsLiveBlitterTransitionCurrent(requester.TransitionKind))
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
            LiveBlitterTransitionKind kind)
            => kind switch
            {
                LiveBlitterTransitionKind.BeginAreaWord =>
                    _busy &&
                    !_completionPending &&
                    !_lineMode &&
                    !_areaMicroOpActive &&
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
                    !_liveAreaFinalDDrainActive,
                LiveBlitterTransitionKind.FinishAreaWord =>
                    _areaMicroOpActive &&
                    _areaMicroOpIndex >= GetAreaMicroOpCount(),
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
                    _busy && _completionPending,
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
                if (_areaMicroOpActive &&
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
        }

        private void LatchLiveAreaPipelineWrite()
        {
            EnsureAreaMicroOpOutputReady();
            _liveAreaPendingDValue = _areaMicroOpOutput;
            _liveAreaPendingDCompletesRow =
                _wordX == _widthWords - 1;
            _liveAreaPendingDCompletesBlit =
                _areaMicroOpFinalWord;
            _liveAreaPendingDValid = true;
        }

        private void BeginLiveAreaFinalDDrain(long completionCycle)
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
            _liveBlitterAreaWords++;
            PublishLiveAreaFinalDCompletion(completionCycle);
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

        private void PublishLiveAreaFinalDCompletion(long completionCycle)
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
            _liveBlitterCompletions++;
            _liveBlitterInterrupts++;
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
