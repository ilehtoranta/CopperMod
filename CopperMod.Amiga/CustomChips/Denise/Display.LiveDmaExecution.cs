/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal readonly record struct CpuBatchCopperLookahead(
        uint Pc,
        ushort First,
        ushort Second,
        long FirstRequestCycle,
        long FirstGrantCycle,
        long SecondRequestCycle,
        long SecondGrantCycle,
        long DataCycle,
        long StopCycle,
        bool Suppressed,
        ulong WakeVersion);

    internal sealed partial class Display
    {
        private void AdvanceLiveDmaWithinFrame(long targetCycle)
            => AdvanceLiveDmaWithinFrame(targetCycle, includeCopper: true);

        private void AdvanceLiveDmaWithinFrame(long targetCycle, bool includeCopper)
        {
            targetCycle = Math.Max(_liveFrameStartCycle, targetCycle);
            if (targetCycle < _liveCycle)
            {
                return;
            }

            var eventIterations = 0;
            while (true)
            {
                if (++eventIterations > 1_000_000)
                {
                    throw new InvalidOperationException(
                        $"Live display DMA did not converge within one physical frame: " +
                        $"frameStart={_liveFrameStartCycle}, target={targetCycle}, live={_liveCycle}, " +
                        $"captured={_liveCapturedThroughCycle}, pending={_pendingIndex}/{_pendingWrites.Count}, " +
                        $"line={_liveNextLineStateRow}, bitplane={_liveNextFetchRow}/" +
                        $"{_liveNextFetchPlane}/{_liveNextFetchWord}/{_liveNextFetchSlot}, " +
                        $"sprite={_liveNextSpriteRow}/{_liveNextSpriteIndex}/{_liveNextSpriteWord}, " +
                        $"copperSteps={_liveCopperStepCount}, bus={_bus.ExecutedChipBusHorizon}.");
                }

                SkipLiveRowsWithoutFetches();
                SkipLiveSpriteSlotsWithoutFetches();
                var nextLineStateCycle = GetNextLiveLineStateCycle();
                var nextBitplaneFetchCycle = GetNextLiveBitplaneFetchCycle();
                var nextSpriteFetchCycle = GetNextLiveSpriteFetchCycle();
                var nextPendingWriteCycle = GetNextLivePendingWriteCycle();
                var nextCycle = Math.Min(
                    Math.Min(nextLineStateCycle, nextBitplaneFetchCycle),
                    Math.Min(nextSpriteFetchCycle, nextPendingWriteCycle));
                if (!includeCopper)
                {
                    var nextCopperBarrierCycle = GetNextLiveCopperBarrierCycle();
                    if (nextCopperBarrierCycle <= targetCycle &&
                        nextCopperBarrierCycle <= nextCycle)
                    {
                        var barrierStopCycle = nextCopperBarrierCycle - 1;
                        if (barrierStopCycle < _liveFrameStartCycle)
                        {
                            return;
                        }

                        RecordLiveRasterlinePlanEvent(
                            LiveRasterlinePlanEventKind.CopperBarrier,
                            barrierStopCycle,
                            row: -1,
                            batchStopCycle: barrierStopCycle,
                            cursorA: 0,
                            cursorB: 0,
                            cursorC: 0);
                        AdvanceLiveDisplayStateTo(barrierStopCycle, includeCopper: false);
                        _liveCycle = Math.Max(_liveCycle, barrierStopCycle);
                        _liveCapturedThroughCycle = Math.Max(_liveCapturedThroughCycle, barrierStopCycle);
                        InvalidateLiveWorkCycle();
                        return;
                    }
                }

                if (nextCycle > targetCycle)
                {
                    break;
                }

                var wakeVersionBeforeAdvance = _liveWakeVersion;
                var fixedDisplaySlotDue =
                    nextBitplaneFetchCycle == nextCycle ||
                    nextSpriteFetchCycle == nextCycle;
                // Fixed display DMA owns the physical slot before Copper is
                // allowed to arbitrate for it.  Register events at the same
                // cycle must still become visible, so advance those events but
                // leave Copper stopped at the incoming fixed-slot boundary.
                // The following iteration retries Copper after the fixed
                // requester has committed (or been denied by a stronger fixed
                // owner).
                if (includeCopper && fixedDisplaySlotDue)
                {
                    AdvanceLiveDisplayStateTo(nextCycle - 1, includeCopper: true);
                    AdvanceLiveDisplayStateTo(nextCycle, includeCopper: false);
                }
                else
                {
                    AdvanceLiveDisplayStateTo(nextCycle, includeCopper);
                }
                if (_liveWakeVersion != wakeVersionBeforeAdvance)
                {
                    // A Copper MOVE can rebase prepared bitplane/sprite work
                    // while advancing toward the event selected above.  That
                    // selection is now stale; recompute it before executing a
                    // batch so its stop cycle cannot precede the new cursor.
                    InvalidateLiveWorkCycle();
                    continue;
                }

                if (nextPendingWriteCycle == nextCycle)
                {
                    RecordLiveRasterlinePlanEvent(
                        LiveRasterlinePlanEventKind.PendingWriteOrCopper,
                        nextCycle,
                        row: -1,
                        batchStopCycle: nextCycle,
                        cursorA: 0,
                        cursorB: 0,
                        cursorC: 0);
                    continue;
                }

                if (nextLineStateCycle == nextCycle)
                {
                    RecordLiveRasterlinePlanEvent(
                        LiveRasterlinePlanEventKind.LineStateCapture,
                        nextCycle,
                        _liveNextLineStateRow,
                        batchStopCycle: nextCycle,
                        cursorA: _liveNextLineStateRow,
                        cursorB: 0,
                        cursorC: 0);
                    CaptureLiveLineState(_liveNextLineStateRow);
                    TryBuildPredictedRasterlinePlanForCapturedLine(_liveNextLineStateRow);
                    _liveNextLineStateRow++;
                    InvalidateLiveWorkCycle();
                    continue;
                }

                if (nextSpriteFetchCycle == nextCycle)
                {
                    var batchStopCycle = GetLiveDmaBatchStopCycle(targetCycle, nextLineStateCycle, includeCopper);
                    batchStopCycle = Math.Min(batchStopCycle, nextBitplaneFetchCycle);
                    batchStopCycle = Math.Max(batchStopCycle, nextCycle);
                    RecordLiveRasterlinePlanEvent(
                        LiveRasterlinePlanEventKind.SpriteFetchBatch,
                        nextCycle,
                        _liveNextSpriteRow,
                        batchStopCycle,
                        _liveNextSpriteIndex,
                        _liveNextSpriteWord,
                        0);
                    CaptureLiveSpriteFetchBatch(batchStopCycle);
                    continue;
                }

                var bitplaneBatchStopCycle = GetLiveDmaBatchStopCycle(targetCycle, nextLineStateCycle, includeCopper);
                bitplaneBatchStopCycle = Math.Min(bitplaneBatchStopCycle, nextSpriteFetchCycle);
                bitplaneBatchStopCycle = Math.Max(bitplaneBatchStopCycle, nextCycle);
                RecordLiveRasterlinePlanEvent(
                    LiveRasterlinePlanEventKind.BitplaneFetchBatch,
                    nextCycle,
                    _liveNextFetchRow,
                    bitplaneBatchStopCycle,
                    _liveNextFetchPlane,
                    _liveNextFetchWord,
                    _liveNextFetchSlot);
                CaptureLiveBitplaneFetchBatch(bitplaneBatchStopCycle);
            }

            AdvanceLiveDisplayStateTo(targetCycle, includeCopper);
            _liveCycle = Math.Max(_liveCycle, targetCycle);
            _liveCapturedThroughCycle = Math.Max(_liveCapturedThroughCycle, targetCycle);
            // Moving only the captured horizon does not change the next display
            // request. Keep the versioned agenda and invalidate only the query
            // result that is parameterized by current/target cycle.
            InvalidateLiveWakeCandidateQueryCache();
        }

        private void AdvanceLiveRegisterEventsWithinFrame(long targetCycle, bool includeCopper)
        {
            targetCycle = Math.Max(_liveFrameStartCycle, targetCycle);
            if (targetCycle < _liveCycle)
            {
                return;
            }

            while (true)
            {
                var nextLineStateCycle = GetNextLiveLineStateCycle();
                var nextDisplayEventCycle = GetNextLiveDisplayEventCycle(includeCopper);
                var nextCycle = Math.Min(nextLineStateCycle, nextDisplayEventCycle);
                if (!includeCopper)
                {
                    var nextCopperBarrierCycle = GetNextLiveCopperBarrierCycle();
                    if (nextCopperBarrierCycle <= targetCycle &&
                        nextCopperBarrierCycle <= nextCycle)
                    {
                        var barrierStopCycle = nextCopperBarrierCycle - 1;
                        if (barrierStopCycle < _liveFrameStartCycle)
                        {
                            return;
                        }

                        AdvanceLiveDisplayStateTo(barrierStopCycle, includeCopper: false);
                        _liveCycle = Math.Max(_liveCycle, barrierStopCycle);
                        InvalidateLiveWorkCycle();
                        return;
                    }
                }

                if (nextCycle > targetCycle)
                {
                    break;
                }

                AdvanceLiveDisplayStateTo(nextCycle, includeCopper);
                if (nextLineStateCycle == nextCycle)
                {
                    CaptureLiveLineState(_liveNextLineStateRow);
                    _liveNextLineStateRow++;
                    InvalidateLiveWorkCycle();
                    continue;
                }
            }

            AdvanceLiveDisplayStateTo(targetCycle, includeCopper);
            _liveCycle = Math.Max(_liveCycle, targetCycle);
            InvalidateLiveWakeCandidateQueryCache();
        }

        private long GetLiveDmaBatchStopCycle(long targetCycle, long nextLineStateCycle)
            => GetLiveDmaBatchStopCycle(targetCycle, nextLineStateCycle, includeCopper: true);

        private long GetLiveDmaBatchStopCycle(long targetCycle, long nextLineStateCycle, bool includeCopper)
        {
            var stopCycle = targetCycle;
            var nextDisplayEventCycle = includeCopper
                ? GetNextLiveDisplayEventCycle()
                : Math.Min(GetNextLivePendingWriteCycle(), GetNextLiveCopperBarrierCycle());
            if (nextDisplayEventCycle != long.MaxValue)
            {
                stopCycle = Math.Min(stopCycle, nextDisplayEventCycle - 1);
            }

            if (nextLineStateCycle != long.MaxValue)
            {
                stopCycle = Math.Min(stopCycle, nextLineStateCycle - 1);
            }

            return stopCycle;
        }

        private long GetNextLiveCopperBarrierCycle()
        {
            var frameStopCycle = GetLiveFrameStopCycle();
            var copperCycle = GetNextLiveCopperCycle(frameStopCycle);
            return copperCycle < frameStopCycle ? copperCycle : long.MaxValue;
        }

        internal long? GetNextLiveCopperWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return null;
            }

            var copperCycle = GetNextLiveCopperCycle(Math.Min(targetCycle + 1, GetLiveFrameStopCycle()));
            if (copperCycle == long.MaxValue || copperCycle > targetCycle)
            {
                return null;
            }

            return copperCycle <= currentCycle ? currentCycle + 1 : copperCycle;
        }

        internal long? GetNextLiveCopperCpuBatchBarrierCycle(long currentCycle, long targetCycle)
        {
            if (_liveCopperRequesterEnabled)
            {
                var requesterCycle = GetNextLiveCopperRequesterCycle();
                return requesterCycle == long.MaxValue
                    ? null
                    : NormalizeCopperBatchBarrier(
                        currentCycle,
                        targetCycle,
                        requesterCycle);
            }

            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle ||
                !_liveDmaEnabled ||
                !_liveFrameValid ||
                !IsLiveCopperDmaEnabled())
            {
                return null;
            }

            var frameEndCycle = GetLiveFrameStopCycle();
            if (currentCycle >= frameEndCycle)
            {
                return null;
            }

            targetCycle = Math.Min(targetCycle, frameEndCycle - 1);
            if (_liveCopper.PendingMove)
            {
                return NormalizeCopperBatchBarrier(currentCycle, targetCycle, _liveCopper.PendingMoveStopCycle);
            }

            if (_liveCopper.PendingSkip)
            {
                return NormalizeCopperBatchBarrier(currentCycle, targetCycle, _liveCopper.PendingSkipCycle);
            }

            if (_liveCopper.Stopped)
            {
                return null;
            }

            if (_liveCopper.PendingStart)
            {
                return _copperListPointer == 0
                    ? null
                    : NormalizeCopperBatchBarrier(
                        currentCycle,
                        targetCycle,
                        Math.Max(_liveCopper.Cycle, _liveFrameStartCycle));
            }

            if (_liveCopper.Pc == 0 && _copperListPointer == 0)
            {
                return null;
            }

            if (_liveCopper.RestartArmed || _liveCopper.ReadyToRequest)
            {
                return NormalizeCopperBatchBarrier(currentCycle, targetCycle, _liveCopper.Cycle);
            }

            if (_liveCopper.Waiting)
            {
                var blitterReadyCycle = GetObservedLiveCopperBlitterReadyCycle();
                if (blitterReadyCycle <= _liveCopper.Cycle)
                {
                    var cachedWaitCycle = GetCachedLiveCopperWaitCycle();
                    if (cachedWaitCycle == long.MaxValue)
                    {
                        return null;
                    }

                    var restartCycle = GetCopperWaitRestartArmCycle(
                        cachedWaitCycle,
                        _liveCopper.WaitSecond,
                        _liveCopper.WaitObservedBlitterBusy);
                    if (restartCycle <= currentCycle)
                    {
                        if (!TryGetCopperWaitCycle(
                            _liveCopper.WaitFirst,
                            _liveCopper.WaitSecond,
                            _liveFrameStartCycle,
                            currentCycle + 1,
                            targetCycle + 1,
                            blitterFinished: true,
                            out var nextWaitCycle))
                        {
                            return null;
                        }

                        restartCycle = GetCopperWaitRestartArmCycle(
                            nextWaitCycle,
                            _liveCopper.WaitSecond,
                            _liveCopper.WaitObservedBlitterBusy);
                    }

                    return NormalizeCopperBatchBarrier(
                        currentCycle,
                        targetCycle,
                        restartCycle);
                }

                if (!TryGetCopperWaitCycle(
                    _liveCopper.WaitFirst,
                    _liveCopper.WaitSecond,
                    _liveFrameStartCycle,
                    Math.Max(_liveCopper.Cycle, blitterReadyCycle),
                    targetCycle + 1,
                    blitterFinished: true,
                    out var waitCycle))
                {
                    return NormalizeCopperBatchBarrier(currentCycle, targetCycle, blitterReadyCycle);
                }

                // A WAIT has no CPU-visible effect while it is sleeping. Once it wakes,
                // stop at the next copper-instruction boundary so the live copper can
                // fetch the following instruction from memory that may have changed.
                return NormalizeCopperBatchBarrier(
                    currentCycle,
                    targetCycle,
                    GetCopperWaitRestartArmCycle(
                        Math.Min(waitCycle, blitterReadyCycle),
                        _liveCopper.WaitSecond,
                        _liveCopper.WaitObservedBlitterBusy));
            }

            // Do not scan ahead through copper list memory here. The next copper
            // instruction can be changed by DMA before it is fetched, so the only safe
            // horizon is the current live copper execution point. Once the instruction
            // is latched, PendingMove/PendingSkip/Waiting can provide a longer boundary.
            var fetchCycle = Math.Max(_liveCopper.Cycle, _liveFrameStartCycle);
            return NormalizeCopperBatchBarrier(currentCycle, targetCycle, fetchCycle);
        }

        internal bool TryGetCpuBatchSafeCopperLookahead(
            long currentCycle,
            long targetCycle,
            out CpuBatchCopperLookahead lookahead)
        {
            lookahead = default;
            if (_liveCopperRequesterEnabled)
            {
                return false;
            }

            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle ||
                !_liveDmaEnabled ||
                !_bus.LiveAgnusDmaEnabled ||
                !_liveFrameValid ||
                !IsLiveCopperDmaEnabled() ||
                _liveCopper.Stopped ||
                _liveCopper.PendingMove ||
                _liveCopper.PendingSkip ||
                _liveCopper.Waiting ||
                _liveCopper.RestartArmed ||
                _liveCopper.ReadyToRequest)
            {
                return false;
            }

            uint pc;
            ushort first;
            long firstRequestCycle;
            long firstGrantCycle;
            long secondRequestCycle;
            long secondGrantCycle;
            if (_liveCopper.PendingInstructionSecondWord)
            {
                pc = _liveCopper.Pc;
                first = _liveCopper.PendingInstructionFirst;
                firstRequestCycle = _liveCopper.PendingInstructionFirstAccess.RequestedCycle;
                firstGrantCycle = _liveCopper.PendingInstructionFirstAccess.GrantedCycle;
                secondRequestCycle = _liveCopper.PendingInstructionSecondWordRequestedCycle;
                secondGrantCycle = _liveCopper.PendingInstructionSecondWordCycle;
            }
            else
            {
                pc = _liveCopper.PendingStart ? _copperListPointer : _liveCopper.Pc;
                if (pc == 0)
                {
                    return false;
                }

                firstRequestCycle = Math.Max(
                    _liveCopper.PendingStart ? currentCycle : _liveCopper.Cycle,
                    _liveFrameStartCycle);
                firstGrantCycle =
                    _bus.CausalBusExecutor.PredictCopperWordCycle(pc, firstRequestCycle);
                first = _bus.ReadCurrentChipDmaWord(pc);
                secondRequestCycle = Math.Max(
                    firstGrantCycle + AgnusChipSlotScheduler.SlotCycles,
                    firstRequestCycle +
                    CopperHpToCpuCycles(CopperInstructionDataHpUnits));
                secondGrantCycle = _bus.CausalBusExecutor.PredictCopperWordCycle(
                    AddDmaPointerOffset(pc, 2),
                    secondRequestCycle);
            }

            if (firstGrantCycle < currentCycle ||
                secondGrantCycle < firstGrantCycle ||
                secondGrantCycle >= targetCycle)
            {
                return false;
            }

            var secondAddress = AddDmaPointerOffset(pc, 2);
            var second = _bus.ReadCurrentChipDmaWord(secondAddress);
            if ((first & 1) != 0 || (first == 0xFFFF && second == 0xFFFE))
            {
                return false;
            }

            var register = (ushort)(first & 0x01FE);
            var suppressed = _liveCopper.SuppressNextMove;
            if (!suppressed &&
                (IsCopperDangerStopRegister(register) ||
                 register < 0x180 ||
                 register >= 0x1C0 ||
                 (register & 1) != 0))
            {
                return false;
            }

            var dataCycle = secondGrantCycle;
            var stopCycle = Math.Max(
                secondGrantCycle + AgnusChipSlotScheduler.SlotCycles,
                firstRequestCycle + ((long)CopperMoveHpUnits * CopperHpCycles));
            if (stopCycle > targetCycle)
            {
                return false;
            }

            lookahead = new CpuBatchCopperLookahead(
                pc,
                first,
                second,
                firstRequestCycle,
                firstGrantCycle,
                secondRequestCycle,
                secondGrantCycle,
                dataCycle,
                stopCycle,
                suppressed,
                _liveWakeVersion);
            return true;
        }

        internal bool IsCpuBatchSafeCopperLookaheadCurrent(
            CpuBatchCopperLookahead lookahead)
        {
            if (lookahead.Pc == 0 ||
                _bus.ReadCurrentChipDmaWord(lookahead.Pc) != lookahead.First ||
                _bus.ReadCurrentChipDmaWord(AddDmaPointerOffset(lookahead.Pc, 2)) !=
                    lookahead.Second)
            {
                return false;
            }

            if (_liveWakeVersion == lookahead.WakeVersion &&
                ((_liveCopper.PendingInstructionSecondWord &&
                  _liveCopper.Pc == lookahead.Pc) ||
                 (_liveCopper.PendingMove &&
                  _liveCopper.Pc == AddDmaPointerOffset(lookahead.Pc, 4) &&
                  _liveCopper.PendingMoveRegister == (lookahead.First & 0x01FE) &&
                  _liveCopper.PendingMoveValue == lookahead.Second &&
                  _liveCopper.PendingMoveCycle == lookahead.DataCycle &&
                  _liveCopper.PendingMoveStopCycle == lookahead.StopCycle) ||
                 (!_liveCopper.PendingMove &&
                  !_liveCopper.PendingSkip &&
                  (_liveCopper.PendingStart
                      ? _copperListPointer == lookahead.Pc
                      : _liveCopper.Pc == lookahead.Pc))))
            {
                return true;
            }

            return _liveWakeVersion == lookahead.WakeVersion + 1 &&
                !_liveCopper.PendingMove &&
                !_liveCopper.PendingSkip &&
                _liveCopper.Pc == AddDmaPointerOffset(lookahead.Pc, 4) &&
                _liveCopper.Cycle == lookahead.StopCycle;
        }

        internal int GetLiveCopperCpuBatchBarrierStateForDiagnostics()
        {
            if (_liveCopper.PendingMove)
            {
                return 0;
            }

            if (_liveCopper.PendingSkip)
            {
                return 1;
            }

            if (_liveCopper.PendingStart)
            {
                return 2;
            }

            if (_liveCopper.RestartArmed)
            {
                return 3;
            }

            if (_liveCopper.ReadyToRequest)
            {
                return 4;
            }

            if (_liveCopper.Waiting)
            {
                return 5;
            }

            return 6;
        }

        private static long? NormalizeCopperBatchBarrier(long currentCycle, long targetCycle, long barrierCycle)
        {
            if (barrierCycle > targetCycle)
            {
                return null;
            }

            return Math.Max(currentCycle + 1, barrierCycle);
        }

        internal bool TryGetCopperQuiescentWindow(long cycle, out long startCycle, out long endCycle)
        {
            cycle = Math.Max(0, cycle);
            startCycle = 0;
            endCycle = 0;
            if (!_liveDmaEnabled ||
                !_liveFrameValid ||
                cycle < _liveFrameStartCycle)
            {
                EndCopperQuiescentWindow(cycle);
                return false;
            }

            var frameEndCycle = GetLiveFrameStopCycle();
            if (cycle >= frameEndCycle)
            {
                EndCopperQuiescentWindow(cycle);
                return false;
            }

            var copperCycle = GetNextLiveCopperCycle(frameEndCycle);
            if (copperCycle != long.MaxValue && copperCycle < frameEndCycle)
            {
                EndCopperQuiescentWindow(cycle);
                return false;
            }

            startCycle = cycle;
            endCycle = frameEndCycle;
            RecordCopperQuiescentWindow(cycle, frameEndCycle);
            return true;
        }

        private void RecordCopperQuiescentWindow(long startCycle, long endCycle)
        {
            if (_copperQuiescentActiveStartCycle >= 0 &&
                _copperQuiescentActiveEndCycle == endCycle &&
                startCycle >= _copperQuiescentActiveStartCycle)
            {
                return;
            }

            EndCopperQuiescentWindow(startCycle);
            if (endCycle <= startCycle)
            {
                return;
            }

            var cycles = endCycle - startCycle;
            _copperQuiescentWindowCount++;
            _copperQuiescentTotalCycles += cycles;
            _copperQuiescentMaxCycles = Math.Max(_copperQuiescentMaxCycles, cycles);
            _copperQuiescentActiveStartCycle = startCycle;
            _copperQuiescentActiveEndCycle = endCycle;
        }

        private void EndCopperQuiescentWindow(long cycle)
        {
            if (_copperQuiescentActiveStartCycle < 0)
            {
                return;
            }

            if (cycle >= _copperQuiescentActiveEndCycle)
            {
                _copperQuiescentActiveStartCycle = -1;
                _copperQuiescentActiveEndCycle = -1;
            }
        }

        private void ResetCopperQuiescenceCounters()
        {
            _copperQuiescentWindowCount = 0;
            _copperQuiescentTotalCycles = 0;
            _copperQuiescentMaxCycles = 0;
            _copperQuiescentActiveStartCycle = -1;
            _copperQuiescentActiveEndCycle = -1;
        }

        internal long? GetNextLiveDisplayWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (_liveDisplayWakeCandidateCacheValid &&
                _liveDisplayWakeCandidateCacheCurrentCycle == currentCycle &&
                _liveDisplayWakeCandidateCacheTargetCycle == targetCycle &&
                _liveDisplayWakeCandidateCacheCapturedThroughCycle == _liveCapturedThroughCycle)
            {
                return _liveDisplayWakeCandidateCacheHasValue
                    ? _liveDisplayWakeCandidateCacheValue
                    : null;
            }

            if (!_liveDmaEnabled ||
                !_liveFrameValid ||
                !HasLiveDisplayWork() ||
                targetCycle < currentCycle)
            {
                return CacheLiveDisplayWakeCandidate(currentCycle, targetCycle, null);
            }

            if (TryGetRecordedLiveRasterlinePlanWakeCandidate(currentCycle, targetCycle, out var recordedCycle))
            {
                var candidate = recordedCycle == long.MaxValue
                    ? (long?)null
                    : recordedCycle;
                return CacheLiveDisplayWakeCandidate(currentCycle, targetCycle, candidate);
            }

            var nextCycle = GetNextLiveCpuVisibleWorkCycle();
            if (nextCycle == long.MaxValue || nextCycle > targetCycle)
            {
                return CacheLiveDisplayWakeCandidate(currentCycle, targetCycle, null);
            }

            return CacheLiveDisplayWakeCandidate(
                currentCycle,
                targetCycle,
                nextCycle <= currentCycle ? currentCycle : nextCycle);
        }

        internal long GetRawLiveBusEligibilityCycle()
        {
            if (!_liveDmaEnabled || !_liveFrameValid || !HasLiveDisplayWork())
            {
                return long.MaxValue;
            }

            var currentCycle = Math.Max(_liveFrameStartCycle, _liveCapturedThroughCycle);
            var targetCycle = Math.Max(currentCycle, GetLiveFrameStopCycle() - 1);
            return GetNextLiveDisplayWakeCandidateCycle(currentCycle, targetCycle) ?? long.MaxValue;
        }

        internal ulong RawLiveBusEligibilityVersion
        {
            get
            {
                unchecked
                {
                    return (_liveWakeVersion * 1099511628211UL) ^ (ulong)_liveCapturedThroughCycle;
                }
            }
        }

        internal long NormalizeRawLiveBusEligibilityCycle(
            long rawCycle,
            long currentCycle,
            long targetCycle)
        {
            if (targetCycle > _liveCapturedThroughCycle &&
                GetNextLiveCpuVisibleWorkCycle() <= currentCycle)
            {
                return currentCycle;
            }

            return rawCycle == long.MaxValue
                ? long.MaxValue
                : rawCycle <= currentCycle ? currentCycle : rawCycle;
        }

        private long? CacheLiveDisplayWakeCandidate(long currentCycle, long targetCycle, long? candidate)
        {
            _liveDisplayWakeCandidateCacheCurrentCycle = currentCycle;
            _liveDisplayWakeCandidateCacheTargetCycle = targetCycle;
            _liveDisplayWakeCandidateCacheCapturedThroughCycle = _liveCapturedThroughCycle;
            _liveDisplayWakeCandidateCacheHasValue = candidate.HasValue;
            _liveDisplayWakeCandidateCacheValue = candidate.GetValueOrDefault();
            _liveDisplayWakeCandidateCacheValid = true;
            return candidate;
        }

        private long GetNextLiveDisplayEventCycle(bool includeCopper)
            => includeCopper ? GetNextLiveDisplayEventCycle() : GetNextLivePendingWriteCycle();


    }
}
