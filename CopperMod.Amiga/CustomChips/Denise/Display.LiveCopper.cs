/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        private void AdvanceLiveDisplayStateTo(long targetCycle)
            => AdvanceLiveDisplayStateTo(targetCycle, includeCopper: true);

        private void AdvanceLiveDisplayStateTo(long targetCycle, bool includeCopper)
        {
            targetCycle = Math.Max(_liveFrameStartCycle, targetCycle);
            while (true)
            {
                var nextCycle = GetNextLiveDisplayEventCycle(includeCopper);
                if (nextCycle > targetCycle)
                {
                    break;
                }

                var pendingCycle = TryPeekPendingWrite(out var pending) ? pending.Cycle : long.MaxValue;
                if (pendingCycle <= nextCycle)
                {
                    ApplyPendingWritesForLiveDma(pendingCycle);
                    _liveCycle = Math.Max(_liveCycle, pendingCycle);
                    _liveDisplayEventCount++;
                    _livePendingWriteEventCount++;
                    InvalidateLiveDisplayEventCycle();
                    continue;
                }

                if (!includeCopper)
                {
                    break;
                }

                StepLiveCopper(targetCycle);
                _liveCycle = Math.Max(_liveCycle, _liveCopper.Cycle);
                _liveDisplayEventCount++;
                _liveCopperStepCount++;
                InvalidateLiveDisplayEventCycle();
            }

            _liveCycle = Math.Max(_liveCycle, targetCycle);
        }

        private void ApplyPendingWritesForLiveDma(long cycle)
        {
            var previousRow = _currentRenderRow;
            var previousCopperRow = _currentCopperRow;
            var row = GetOutputRowForCycle(_liveFrameStartCycle, cycle);
            _currentRenderRow = row;
            _currentCopperRow = row;
            try
            {
                ApplyPendingWrites(cycle);
                if (IsLiveLineValid(row))
                {
                    InvalidateRowDmaPlan(row);
                }
            }
            finally
            {
                _currentRenderRow = previousRow;
                _currentCopperRow = previousCopperRow;
            }
        }

        private long GetNextLiveCopperCycle(long targetCycle)
        {
            if (_liveCopper.PendingInstructionSecondWord)
            {
                return _liveCopper.PendingInstructionSecondWordCycle;
            }

            if (_liveCopper.PendingMove)
            {
                return _liveCopper.PendingMoveCycle;
            }

            if (_liveCopper.PendingSkip)
            {
                return _liveCopper.PendingSkipCycle;
            }

            if (_liveCopper.Stopped)
            {
                return long.MaxValue;
            }

            if (!IsLiveCopperDmaEnabled())
            {
                return long.MaxValue;
            }

            if (_liveCopper.PendingStart)
            {
                return _copperListPointer == 0
                    ? long.MaxValue
                    : Math.Max(_liveCopper.Cycle, _liveFrameStartCycle);
            }

            if (_liveCopper.Pc == 0 && _copperListPointer == 0)
            {
                return long.MaxValue;
            }

            if (_liveCopper.RestartArmed || _liveCopper.ReadyToRequest)
            {
                return _liveCopper.Cycle;
            }

            if (_liveCopper.Waiting)
            {
                var blitterReadyCycle = GetObservedLiveCopperBlitterReadyCycle();
                if (blitterReadyCycle <= _liveCopper.Cycle)
                {
                    var cachedWaitCycle = GetCachedLiveCopperWaitCycle();
                    return cachedWaitCycle <= targetCycle ? cachedWaitCycle : long.MaxValue;
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
                    return long.MaxValue;
                }

                return Math.Min(waitCycle, blitterReadyCycle);
            }

            var requestCycle = Math.Max(_liveCopper.Cycle, _liveFrameStartCycle);
            return _bus.CausalBusExecutor.PredictCopperWordCycle(_liveCopper.Pc, requestCycle);
        }

        private long GetCachedLiveCopperWaitCycle()
        {
            if (_liveCopperWaitCycleValid &&
                _liveCopperWaitFirst == _liveCopper.WaitFirst &&
                _liveCopperWaitSecond == _liveCopper.WaitSecond &&
                _liveCopperWaitStartCycle == _liveCopper.Cycle)
            {
                return _liveCopperWaitCycle;
            }

            var frameStopCycle = GetLiveFrameStopCycle();
            _liveCopperWaitFirst = _liveCopper.WaitFirst;
            _liveCopperWaitSecond = _liveCopper.WaitSecond;
            _liveCopperWaitStartCycle = _liveCopper.Cycle;
            _liveCopperWaitCycle = TryGetCopperWaitCycle(
                _liveCopper.WaitFirst,
                _liveCopper.WaitSecond,
                _liveFrameStartCycle,
                _liveCopper.Cycle,
                frameStopCycle,
                blitterFinished: true,
                out var waitCycle)
                    ? waitCycle
                    : long.MaxValue;
            _liveCopperWaitCycleValid = true;
            return _liveCopperWaitCycle;
        }

        private long GetObservedLiveCopperBlitterReadyCycle()
        {
            if ((_liveCopper.WaitSecond & 0x8000) != 0)
            {
                return _liveCopper.Cycle;
            }

            if (_bus.Blitter.Busy)
            {
                _liveCopper.WaitObservedBlitterBusy = true;
                return Math.Max(_liveCopper.Cycle, _bus.Blitter.GetPredictedCompletionCycle());
            }

            return _liveCopper.WaitObservedBlitterBusy
                ? Math.Max(_liveCopper.Cycle, _bus.Blitter.LastCompletionCycle)
                : _liveCopper.Cycle;
        }


        private void StepLiveCopper(long targetCycle)
        {
            CopperInstructionLatch instruction;
            if (_liveCopper.PendingInstructionSecondWord)
            {
                var secondRequestCycle = _liveCopper.PendingInstructionSecondWordCycle;
                if (secondRequestCycle > targetCycle)
                {
                    return;
                }

                RecordLiveCopperBitplaneRgaCollision(secondRequestCycle);
                var secondAddress = AddDmaPointerOffset(_liveCopper.Pc, 2);
                if (!_bus.CausalBusExecutor.TryExecuteCopperWordExact(
                        secondAddress,
                        secondRequestCycle,
                        secondRequestCycle,
                        out var second,
                        out var secondAccess))
                {
                    var retryRequestCycle = secondRequestCycle + AgnusChipSlotScheduler.SlotCycles;
                    _liveCopper.PendingInstructionSecondWordCycle =
                        _bus.CausalBusExecutor.PredictCopperWordCycle(secondAddress, retryRequestCycle);
                    _liveCopper.Cycle = _liveCopper.PendingInstructionSecondWordCycle;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                instruction = new CopperInstructionLatch(
                    _liveCopper.PendingInstructionFirst,
                    _liveCopper.PendingInstructionFirstAccess,
                    second,
                    secondAccess,
                    CopperHpCycles);
                _liveCopper.PendingInstructionSecondWord = false;
            }
            else
            {
            if (_liveCopper.PendingMove)
            {
                CompletePendingLiveCopperMove(targetCycle);
                return;
            }

            if (_liveCopper.PendingSkip)
            {
                CompletePendingLiveCopperSkip(targetCycle);
                return;
            }

            if (_liveCopper.Stopped || !IsLiveCopperDmaEnabled())
            {
                return;
            }

            if (_liveCopper.PendingStart)
            {
                if (_copperListPointer == 0)
                {
                    return;
                }

                _liveCopper.StartFrom(_copperListPointer);
            }

            if (_liveCopper.RestartArmed)
            {
                if (_liveCopper.Cycle > targetCycle)
                {
                    return;
                }

                if (_liveCopper.WaitRestartIncomingRgaBlocked)
                {
                    _liveCopper.WaitRestartIncomingRgaBlocked = false;
                    _liveCopper.Cycle += 2L * AgnusChipSlotScheduler.SlotCycles;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                _liveCopper.AdvanceWaitRestartStage(
                    _liveCopper.Cycle + (2L * AgnusChipSlotScheduler.SlotCycles));
                InvalidateLiveDisplayEventCycle();
                return;
            }

            if (_liveCopper.ReadyToRequest)
            {
                _liveCopper.AdvanceWaitRestartStage(_liveCopper.Cycle);
            }

            if (_liveCopper.Waiting)
            {
                var blitterReadyCycle = GetObservedLiveCopperBlitterReadyCycle();
                if (blitterReadyCycle > _liveCopper.Cycle)
                {
                    var advanceCycle = Math.Min(blitterReadyCycle, targetCycle);
                    _bus.SynchronizeBlitterThrough(advanceCycle);
                    _liveCopper.Cycle = advanceCycle;
                    if (_liveCopper.Cycle < blitterReadyCycle)
                    {
                        return;
                    }

                    if (_bus.Blitter.Busy)
                    {
                        return;
                    }

                }

                long waitCycle;
                if (blitterReadyCycle <= _liveCopper.Cycle)
                {
                    waitCycle = GetCachedLiveCopperWaitCycle();
                    if (waitCycle > targetCycle)
                    {
                        return;
                    }
                }
                else if (!TryGetCopperWaitCycle(
                             _liveCopper.WaitFirst,
                             _liveCopper.WaitSecond,
                             _liveFrameStartCycle,
                             _liveCopper.Cycle,
                             targetCycle + 1,
                             blitterFinished: true,
                             out waitCycle))
                {
                    return;
                }

                var comparisonStartCycle = _liveCopper.Cycle;
                var resumeCycle = GetCopperWaitRestartArmCycle(
                    waitCycle,
                    _liveCopper.WaitSecond,
                    _liveCopper.WaitObservedBlitterBusy);
                var restartIncomingRgaBlocked = false;
                if (waitCycle > comparisonStartCycle)
                {
                    var incomingRgaBlocked = IsBitplaneRgaIncomingPhase(waitCycle);
                    var overlapsAdjacentRgaStage =
                        IsBitplaneRgaDecisionPhase(waitCycle) || IsBitplaneRgaOutputPhase(waitCycle);
                    _liveCopper.WaitStartCarrySkipCount = (byte)(overlapsAdjacentRgaStage ? 2 : 0);
                    _liveCopper.WaitStartCarryPending = incomingRgaBlocked;
                    restartIncomingRgaBlocked =
                        _bus.CausalBusExecutor.GetNextEligibleCopperControlPhase(
                            resumeCycle,
                            incomingRgaBlocked,
                            overlapsAdjacentRgaStage) > resumeCycle;
                }
                else if (_liveCopper.WaitStartCarryPending)
                {
                    if (_liveCopper.WaitStartCarrySkipCount > 0)
                    {
                        _liveCopper.WaitStartCarrySkipCount--;
                    }
                    else
                    {
                        restartIncomingRgaBlocked = true;
                        _liveCopper.WaitStartCarryPending = false;
                    }
                }
                else
                {
                    _liveCopper.WaitStartCarryPending = false;
                    _liveCopper.WaitStartCarrySkipCount = 0;
                }
                if (resumeCycle > targetCycle)
                {
                    _liveCopper.ArmWaitRestart(resumeCycle, restartIncomingRgaBlocked);
                    InvalidateLiveCopperWaitCycle();
                    return;
                }

                _liveCopper.ArmWaitRestart(resumeCycle, restartIncomingRgaBlocked);
                InvalidateLiveCopperWaitCycle();
                return;
            }

                var firstRequestCycle = Math.Max(_liveCopper.Cycle, _liveFrameStartCycle);
                var fetchCycle = _bus.CausalBusExecutor.PredictCopperWordCycle(_liveCopper.Pc, firstRequestCycle);
                if (fetchCycle > targetCycle)
                {
                    return;
                }

                RecordLiveCopperBitplaneRgaCollision(fetchCycle);
                if (!_bus.CausalBusExecutor.TryExecuteCopperWordExact(
                        _liveCopper.Pc,
                        firstRequestCycle,
                        fetchCycle,
                        out var first,
                        out var firstAccess))
                {
                    _liveCopper.Cycle = fetchCycle + AgnusChipSlotScheduler.SlotCycles;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                var secondAddress = AddDmaPointerOffset(_liveCopper.Pc, 2);
                var secondRequestCycle = _bus.CausalBusExecutor.PredictCopperWordCycle(
                    secondAddress,
                    GetCopperSecondWordRequestCycle(firstAccess));
                if (secondRequestCycle > targetCycle)
                {
                    _liveCopper.PendingInstructionSecondWord = true;
                    _liveCopper.PendingInstructionFirst = first;
                    _liveCopper.PendingInstructionFirstAccess = firstAccess;
                    _liveCopper.PendingInstructionSecondWordCycle = secondRequestCycle;
                    _liveCopper.Cycle = secondRequestCycle;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                RecordLiveCopperBitplaneRgaCollision(secondRequestCycle);
                if (!_bus.CausalBusExecutor.TryExecuteCopperWordExact(
                        secondAddress,
                        secondRequestCycle,
                        secondRequestCycle,
                        out var second,
                        out var secondAccess))
                {
                    _liveCopper.PendingInstructionSecondWord = true;
                    _liveCopper.PendingInstructionFirst = first;
                    _liveCopper.PendingInstructionFirstAccess = firstAccess;
                    var retryRequestCycle = secondRequestCycle + AgnusChipSlotScheduler.SlotCycles;
                    _liveCopper.PendingInstructionSecondWordCycle =
                        _bus.CausalBusExecutor.PredictCopperWordCycle(secondAddress, retryRequestCycle);
                    _liveCopper.Cycle = _liveCopper.PendingInstructionSecondWordCycle;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                instruction = new CopperInstructionLatch(first, firstAccess, second, secondAccess, CopperHpCycles);
            }

            _liveCopper.Pc = AddDmaPointerOffset(_liveCopper.Pc, 4);

            if (instruction.IsEnd)
            {
                _liveCopper.Stopped = true;
                _liveCopper.Cycle = instruction.MoveStopCycle;
                return;
            }

            if (instruction.IsMove)
            {
                var register = instruction.MoveRegister;
                var dataCycle = instruction.DataCycle;
                var suppressMove = _liveCopper.SuppressNextMove;
                _liveCopper.SuppressNextMove = false;
                var commitMove = !suppressMove &&
                    !IsCopperDangerStopRegister(register) &&
                    CanCopperWriteRegister(register);
                // The instruction words may have been granted while the causal
                // executor is draining the current slot.  In that case the data
                // phase is already behind the executed horizon and must be
                // committed synchronously below; scheduling a second control
                // event would both be late and move the write into a later line.
                if (commitMove && dataCycle > _bus.ExecutedChipBusHorizon)
                {
                    _bus.CausalBusExecutor.ScheduleCopperMoveControl(
                        register,
                        instruction.Second,
                        dataCycle);
                }

                if (dataCycle > targetCycle)
                {
                    _liveCopper.PendingMove = true;
                    _liveCopper.PendingMoveRegister = register;
                    _liveCopper.PendingMoveValue = instruction.Second;
                    _liveCopper.PendingMoveCycle = dataCycle;
                    _liveCopper.PendingMoveStopCycle = instruction.MoveStopCycle;
                    _liveCopper.PendingMoveSuppress = suppressMove;
                    _liveCopper.Cycle = dataCycle;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                if (dataCycle <= targetCycle)
                {
                    ApplyLiveCopperMove(register, instruction.Second, dataCycle, instruction.MoveStopCycle, suppressMove);
                }

                _liveCopper.Cycle = instruction.MoveStopCycle;
                return;
            }

            if (instruction.IsWait)
            {
                _liveCopper.Cycle = instruction.ControlStopCycle;
                _liveCopper.Wait(instruction.First, instruction.Second);
                return;
            }

            if (instruction.ControlStopCycle > targetCycle)
            {
                _liveCopper.PendingSkip = true;
                _liveCopper.PendingSkipFirst = instruction.First;
                _liveCopper.PendingSkipSecond = instruction.Second;
                _liveCopper.PendingSkipCycle = instruction.ControlStopCycle;
                _liveCopper.Cycle = instruction.ControlStopCycle;
                InvalidateLiveDisplayEventCycle();
                return;
            }

            if (IsCopperComparisonSatisfied(
                instruction.First,
                instruction.Second,
                _liveFrameStartCycle,
                instruction.ControlStopCycle,
                IsCopperBlitterFinishedForWait(instruction.Second)))
            {
                _liveCopper.SuppressNextMove = true;
            }

            _liveCopper.Cycle = instruction.ControlStopCycle;
        }




        private CopperInstructionLatch LoadLiveCopperInstruction(uint pc, long fetchCycle)
        {
            RecordLiveCopperBitplaneRgaCollision(fetchCycle);
            var first = _bus.CausalBusExecutor.ExecuteCopperWord(pc, fetchCycle, out var firstAccess);
            var secondRequestCycle = GetCopperSecondWordRequestCycle(firstAccess);
            RecordLiveCopperBitplaneRgaCollision(secondRequestCycle);
            var second = _bus.CausalBusExecutor.ExecuteCopperWord(
                AddDmaPointerOffset(pc, 2),
                secondRequestCycle,
                out var secondAccess);
            return new CopperInstructionLatch(first, firstAccess, second, secondAccess, CopperHpCycles);
        }

        private void RecordLiveCopperBitplaneRgaCollision(long copperRequestCycle)
        {
            var row = GetOutputRowForCycle(_liveFrameStartCycle, copperRequestCycle);
            if (!IsLiveLineValid(row))
            {
                return;
            }

            var state = GetLiveLineState(row);
            if (state.PlaneCount <= 0 || state.FetchWords <= 0)
            {
                return;
            }

            if (TryMapCopperRequestToLateBitplaneCollision(
                    state,
                    copperRequestCycle,
                    out var plane,
                    out var capturedWord))
            {
                // A following BPLCON0 disable resolves the late request against
                // the word selected by this RGA decision.
                _liveBitplaneCopperCollisionMasks[GetLiveBitplaneMaskIndex(row, plane)] |=
                    (UInt128)1 << capturedWord;
            }
        }

        private bool IsBitplaneRgaIncomingPhase(long cycle)
            => IsBitplaneRgaStage(cycle, stage: 1);

        private bool IsBitplaneRgaDecisionPhase(long cycle)
            => IsBitplaneRgaStage(cycle, stage: 0);

        private bool IsBitplaneRgaOutputPhase(long cycle)
            => IsBitplaneRgaStage(cycle, stage: 2);

        private bool IsBitplaneRgaStage(
            long cycle,
            int stage)
        {
            var row = GetOutputRowForCycle(_liveFrameStartCycle, cycle);
            if (!IsLiveLineValid(row))
            {
                return false;
            }

            var state = GetLiveLineState(row);
            if (state.PlaneCount <= 0 || state.FetchWords <= 0 || state.FetchSlotStride <= 0)
            {
                return false;
            }

            BuildBitplaneRgaStageMasks(state);

            var lineOffset = cycle - state.LineStartCycle;
            if (lineOffset < 0 || lineOffset % CopperHpCycles != 0)
            {
                return false;
            }

            return stage switch
            {
                0 => IsRgaStageSet(
                    state.BitplaneRgaDecision0,
                    state.BitplaneRgaDecision1,
                    state.BitplaneRgaDecision2,
                    state.BitplaneRgaDecision3,
                    (int)(lineOffset / CopperHpCycles)),
                1 => IsRgaStageSet(
                    state.BitplaneRgaIncoming0,
                    state.BitplaneRgaIncoming1,
                    state.BitplaneRgaIncoming2,
                    state.BitplaneRgaIncoming3,
                    (int)(lineOffset / CopperHpCycles)),
                _ => IsRgaStageSet(
                    state.BitplaneRgaOutput0,
                    state.BitplaneRgaOutput1,
                    state.BitplaneRgaOutput2,
                    state.BitplaneRgaOutput3,
                    (int)(lineOffset / CopperHpCycles))
            };
        }

        private void BuildBitplaneRgaStageMasks(LiveLineState state)
        {
            state.BitplaneRgaDecision0 = 0;
            state.BitplaneRgaDecision1 = 0;
            state.BitplaneRgaDecision2 = 0;
            state.BitplaneRgaDecision3 = 0;
            state.BitplaneRgaIncoming0 = 0;
            state.BitplaneRgaIncoming1 = 0;
            state.BitplaneRgaIncoming2 = 0;
            state.BitplaneRgaIncoming3 = 0;
            state.BitplaneRgaOutput0 = 0;
            state.BitplaneRgaOutput1 = 0;
            state.BitplaneRgaOutput2 = 0;
            state.BitplaneRgaOutput3 = 0;

            for (var word = 0; word < state.FetchWords; word++)
            {
                for (var slot = 0; slot < state.FetchSlotStride; slot++)
                {
                    if (!TryGetBitplanePlaneForFetchSlot(
                            slot,
                            state.PlaneCount,
                            state.FetchResolution,
                            out var plane) ||
                        (state.PlaneHasRowMask & (1 << plane)) == 0)
                    {
                        continue;
                    }

                    var output = state.DataFetchStart + (word * state.FetchSlotStride) + slot;
                    SetRgaStage(ref state.BitplaneRgaOutput0, ref state.BitplaneRgaOutput1,
                        ref state.BitplaneRgaOutput2, ref state.BitplaneRgaOutput3, output);
                    SetRgaStage(ref state.BitplaneRgaIncoming0, ref state.BitplaneRgaIncoming1,
                        ref state.BitplaneRgaIncoming2, ref state.BitplaneRgaIncoming3, output - 1);
                    SetRgaStage(ref state.BitplaneRgaDecision0, ref state.BitplaneRgaDecision1,
                        ref state.BitplaneRgaDecision2, ref state.BitplaneRgaDecision3, output - 2);
                }
            }
        }

        private static void SetRgaStage(
            ref ulong mask0,
            ref ulong mask1,
            ref ulong mask2,
            ref ulong mask3,
            int horizontal)
        {
            if ((uint)horizontal >= 256u)
            {
                return;
            }

            var bit = 1UL << (horizontal & 63);
            switch (horizontal >> 6)
            {
                case 0: mask0 |= bit; break;
                case 1: mask1 |= bit; break;
                case 2: mask2 |= bit; break;
                default: mask3 |= bit; break;
            }
        }

        private static bool IsRgaStageSet(
            ulong mask0,
            ulong mask1,
            ulong mask2,
            ulong mask3,
            int horizontal)
        {
            if ((uint)horizontal >= 256u)
            {
                return false;
            }

            var bit = 1UL << (horizontal & 63);
            return (horizontal >> 6) switch
            {
                0 => (mask0 & bit) != 0,
                1 => (mask1 & bit) != 0,
                2 => (mask2 & bit) != 0,
                _ => (mask3 & bit) != 0
            };
        }

        private bool TryMapCopperRequestToLateBitplaneCollision(
            LiveLineState state,
            long copperRequestCycle,
            out int plane,
            out int capturedWord)
        {
            plane = 0;
            capturedWord = 0;
            if (state.FetchSlotStride <= 0)
            {
                return false;
            }

            var fetchCycle = copperRequestCycle + CopperHpCycles;
            if (AgnusChipSlotScheduler.AlignToSlot(fetchCycle) != fetchCycle)
            {
                return false;
            }

            var lineOffset = fetchCycle - state.LineStartCycle;
            if (lineOffset < 0 || lineOffset % CopperHpCycles != 0)
            {
                return false;
            }

            var horizontal = (int)(lineOffset / CopperHpCycles);
            if (horizontal <= OcsDdfHardStopHorizontal)
            {
                return false;
            }

            var fetchOffset = horizontal - state.DataFetchStart;
            if (fetchOffset < 0)
            {
                return false;
            }

            var word = fetchOffset / state.FetchSlotStride;
            var slot = fetchOffset % state.FetchSlotStride;
            if ((uint)word >= (uint)state.FetchWords ||
                !TryGetBitplanePlaneForFetchSlot(
                    slot,
                    state.PlaneCount,
                    state.FetchResolution,
                    out plane) ||
                (state.PlaneHasRowMask & (1 << plane)) == 0)
            {
                return false;
            }

            capturedWord = word + state.BitplaneWordIndexOffsets[plane];
            return (uint)capturedWord < (uint)MaxBitplaneFetchWords;
        }

        private void CommitLateBitplaneRgaCollisionsOnDisable(ushort register, ushort value, long dataCycle)
        {
            if (register != 0x100 ||
                GetAgnusBitplaneFetchPlaneCount(_bplcon0) <= 0 ||
                GetAgnusBitplaneFetchPlaneCount(value) > 0)
            {
                return;
            }

            CommitLateBitplaneRgaCollisionsForPreviousRow(dataCycle);
        }

        private void CommitLateBitplaneRgaCollisionsForPreviousRow(long dataCycle)
        {
            var row = GetOutputRowForCycle(_liveFrameStartCycle, dataCycle) - 1;
            if ((uint)row >= LowResOutputHeight)
            {
                return;
            }

            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                var mask = _liveBitplaneCopperCollisionMasks[GetLiveBitplaneMaskIndex(row, plane)];
                for (var word = 0; word < MaxBitplaneFetchWords; word++)
                {
                    if ((mask & ((UInt128)1 << word)) == 0)
                    {
                        continue;
                    }

                    _liveBitplaneWords[GetLiveBitplaneWordIndex(row, plane, word)] = 0;
                    _displayTimeline.RecordBitplaneFetch(row, plane, word, 0, granted: false);
                }
            }
        }

        private long GetCopperSecondWordRequestCycle(AmigaBusAccessResult firstAccess)
            => Math.Max(
                firstAccess.CompletedCycle,
                firstAccess.RequestedCycle + CopperHpToCpuCycles(CopperInstructionDataHpUnits));

        private void CompletePendingLiveCopperSkip(long targetCycle)
        {
            if (!_liveCopper.PendingSkip || _liveCopper.PendingSkipCycle > targetCycle)
            {
                return;
            }

            var first = _liveCopper.PendingSkipFirst;
            var second = _liveCopper.PendingSkipSecond;
            var skipCycle = _liveCopper.PendingSkipCycle;
            _liveCopper.PendingSkip = false;
            if (IsCopperComparisonSatisfied(
                first,
                second,
                _liveFrameStartCycle,
                skipCycle,
                IsCopperBlitterFinishedForWait(second)))
            {
                _liveCopper.SuppressNextMove = true;
            }

            _liveCopper.Cycle = skipCycle;
            InvalidateLiveDisplayEventCycle();
        }

        private void CompletePendingLiveCopperMove(long targetCycle)
        {
            if (!_liveCopper.PendingMove || _liveCopper.PendingMoveCycle > targetCycle)
            {
                return;
            }

            var register = _liveCopper.PendingMoveRegister;
            var value = _liveCopper.PendingMoveValue;
            var dataCycle = _liveCopper.PendingMoveCycle;
            var stopCycle = _liveCopper.PendingMoveStopCycle;
            var suppressMove = _liveCopper.PendingMoveSuppress;
            _liveCopper.PendingMove = false;
            ApplyLiveCopperMove(register, value, dataCycle, stopCycle, suppressMove);
            if (!_liveCopper.Stopped)
            {
                _liveCopper.Cycle = stopCycle;
            }

            InvalidateLiveDisplayEventCycle();
        }

        private void ApplyLiveCopperMove(
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
                _bus.CausalBusExecutor.CommitCopperMoveControl(register, value, dataCycle);
                RecordCopperQuiescentCopperMove(dataCycle, register);
                var affectsDisplay = IsDisplayRegisterWrite(register);
                if (affectsDisplay)
                {
                    _currentCopperRow = GetOutputRowForCycle(_liveFrameStartCycle, dataCycle);
                    AdvanceLiveDisplayWindowStateToCycle(dataCycle);
                    EnsureTimelineLineStartedBeforeDisplayWrite(dataCycle);
                }

                CommitLateBitplaneRgaCollisionsOnDisable(register, value, dataCycle);
                ApplyCopperMove(register, value, dataCycle, applyHardwareSideEffects: true);
                if (affectsDisplay)
                {
                    CaptureCopperDisplayWrite(dataCycle, register, value);
                    RecordLiveFrameWrite(dataCycle, register, value, isCopper: true);
                    RefreshLiveLineStateAfterDisplayStateChange(dataCycle, register);
                    RecordTimelineDisplayWrite(dataCycle, register, isCopper: true);
                }

                if (register == 0x088)
                {
                    _liveCopper.JumpTo(_copperListPointer, dataCycle);
                }
                else if (register == 0x08A)
                {
                    _liveCopper.JumpTo(_copperListPointer2, dataCycle);
                }

                // A Copper MOVE can invalidate fixed DMA ownership even when it is
                // executed outside the current blitter advancement scope.
                _liveWakeVersion++;
            }

            _liveCopper.Cycle = instructionStopCycle;
        }

        private void CaptureCopperDisplayWrite(long cycle, ushort register, ushort value)
        {
            if (!_bus.BusAccessCaptureEnabled)
            {
                return;
            }

            (_copperDisplayWrites ??= new BoundedWriteLog(MaxCapturedCopperDisplayWrites))
                .Add(new CustomRegisterWrite(cycle, register, value));
        }

        private void RecordCopperQuiescentCopperMove(long cycle, ushort register)
        {
            if (_copperQuiescentActiveStartCycle < 0 ||
                cycle < _copperQuiescentActiveStartCycle ||
                cycle > _copperQuiescentActiveEndCycle)
            {
                return;
            }

            _bus.RecordCopperQuiescentCustomRegisterWrite(
                AmigaBusRequester.Copper,
                register,
                cycle,
                CustomRegisterScheduleClassifier.GetPotentialImpact(_chipset, register));
        }

        private long GetNextLiveLineStateCycle()
        {
            if (_liveNextLineStateRow >= LowResOutputHeight)
            {
                return long.MaxValue;
            }

            return GetOutputRowStartCycle(_liveFrameStartCycle, _liveNextLineStateRow);
        }

        private long GetNextLiveBitplaneFetchCycle()
        {
            if (!NormalizeLiveBitplaneFetchCursor())
            {
                return long.MaxValue;
            }

            return GetBitplaneFetchCycle(in _liveBitplaneFetchTimeline.Captured);
        }

        private bool NormalizeLiveBitplaneFetchCursor()
            => NormalizeBitplaneFetchCursor(
                ref _liveBitplaneFetchTimeline.Captured,
                advanceCapturedPointers: true);

        private long GetNextLiveSpriteFetchCycle()
        {
            if (_liveNextSpriteRow >= LowResOutputHeight || !IsSpriteDmaEnabled())
            {
                return long.MaxValue;
            }

            if (!IsLiveLineValid(_liveNextSpriteRow))
            {
                return GetOutputRowStartCycle(_liveFrameStartCycle, _liveNextSpriteRow);
            }

            return GetSpriteDmaFetchCycle(_liveFrameStartCycle, _liveNextSpriteRow, _liveNextSpriteIndex, _liveNextSpriteWord);
        }

        private void SkipLiveRowsWithoutFetches()
        {
            var advanced = false;
            while (_liveNextFetchRow < LowResOutputHeight)
            {
                if (!IsLiveLineValid(_liveNextFetchRow))
                {
                    return;
                }

                var state = GetLiveLineState(_liveNextFetchRow);

                if (state.PlaneCount > 0 &&
                    state.FetchWords > 0 &&
                    state.DisplayWindowVerticallyOpen &&
                    IsBitplaneDmaEnabled(state.Dmacon))
                {
                    return;
                }

                _liveNextFetchRow++;
                _liveNextFetchWord = 0;
                _liveNextFetchPlane = 0;
                _liveNextFetchSlot = 0;
                advanced = true;
            }

            if (advanced)
            {
                InvalidateLiveWorkCycle();
            }
        }

        private void SkipLiveSpriteSlotsWithoutFetches()
        {
            while (_liveNextSpriteRow < LowResOutputHeight)
            {
                if (!IsLiveLineValid(_liveNextSpriteRow))
                {
                    if (_liveNextSpriteRow < _liveNextLineStateRow)
                    {
                        // DMA may be enabled after the three-line ring has
                        // already overwritten this cursor row. Expired sprite
                        // slots cannot be replayed; resume on the most recently
                        // captured line so passed slots are denied causally and
                        // later slots on that line remain observable.
                        _liveNextSpriteRow = Math.Max(
                            _liveNextSpriteRow + 1,
                            Math.Max(0, _liveNextLineStateRow - 1));
                        _liveNextSpriteIndex = 0;
                        _liveNextSpriteWord = 0;
                        InvalidateLiveWorkCycle();
                        continue;
                    }

                    return;
                }

                if (!IsSpriteDmaEnabled())
                {
                    return;
                }

                if (!IsSpriteDmaChannelAvailable(_liveNextSpriteIndex))
                {
                    if (WouldLiveSpriteSlotFetchIfChannelAvailable(_liveNextSpriteRow, _liveNextSpriteIndex))
                    {
                        RecordMissedSpriteDmaSlot(liveCapture: true);
                        RecordTimelineSpriteDataFetch(
                            _liveNextSpriteRow,
                            _liveNextSpriteIndex,
                            _liveNextSpriteWord,
                            0,
                            granted: false);
                    }
                }
                else if (CanLiveSpriteSlotFetch(_liveNextSpriteRow, _liveNextSpriteIndex))
                {
                    return;
                }

                AdvanceLiveSpriteFetchCursor();
            }
        }

        private bool WouldLiveSpriteSlotFetchIfChannelAvailable(int row, int spriteIndex)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)spriteIndex >= (uint)_liveSpriteDmaStates.Length ||
                _liveSpriteDmaExhausted[spriteIndex])
            {
                return false;
            }

            var state = _liveSpriteDmaStates[spriteIndex];
            if (state.Active)
            {
                return row >= state.Descriptor.YStart && row < state.Descriptor.YStop;
            }

            return row == state.ControlRow;
        }

        private bool CanLiveSpriteSlotFetch(int row, int spriteIndex)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)spriteIndex >= (uint)_liveSpriteDmaStates.Length ||
                !IsSpriteDmaChannelAvailable(spriteIndex) ||
                _liveSpriteDmaExhausted[spriteIndex])
            {
                return false;
            }

            var state = _liveSpriteDmaStates[spriteIndex];
            if (state.Active && row >= state.Descriptor.YStop)
            {
                state.Active = false;
            }

            if (state.Active)
            {
                return row >= state.Descriptor.YStart && row < state.Descriptor.YStop;
            }

            if (row > state.ControlRow)
            {
                state.ControlRow = row;
            }

            return row == state.ControlRow;
        }


    }
}
