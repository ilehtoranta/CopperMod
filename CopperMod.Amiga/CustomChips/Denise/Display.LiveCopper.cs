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
                RefreshLiveLineStateAfterDisplayStateChange(cycle);
            }
            finally
            {
                _currentRenderRow = previousRow;
                _currentCopperRow = previousCopperRow;
            }
        }

        private long GetNextLiveCopperCycle(long targetCycle)
        {
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

            return Math.Max(_liveCopper.Cycle, _liveFrameStartCycle);
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

                var resumeCycle = waitCycle + CopperHpToCpuCycles(GetCopperWaitWakeHpUnits(
                    _liveCopper.WaitSecond,
                    _liveCopper.WaitObservedBlitterBusy));
                if (resumeCycle > targetCycle)
                {
                    _liveCopper.Cycle = resumeCycle;
                    _liveCopper.Waiting = false;
                    InvalidateLiveCopperWaitCycle();
                    return;
                }

                _liveCopper.Cycle = resumeCycle;
                _liveCopper.Waiting = false;
                InvalidateLiveCopperWaitCycle();
                return;
            }

            var instruction = LoadLiveCopperInstruction(_liveCopper.Pc, Math.Min(_liveCopper.Cycle, targetCycle));
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
                var suppressMove = _liveCopper.SuppressNextMove;
                _liveCopper.SuppressNextMove = false;
                if (instruction.DataCycle > targetCycle)
                {
                    _liveCopper.PendingMove = true;
                    _liveCopper.PendingMoveRegister = register;
                    _liveCopper.PendingMoveValue = instruction.Second;
                    _liveCopper.PendingMoveCycle = instruction.DataCycle;
                    _liveCopper.PendingMoveStopCycle = instruction.MoveStopCycle;
                    _liveCopper.PendingMoveSuppress = suppressMove;
                    _liveCopper.Cycle = instruction.DataCycle;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                if (instruction.DataCycle <= targetCycle)
                {
                    ApplyLiveCopperMove(register, instruction.Second, instruction.DataCycle, instruction.MoveStopCycle, suppressMove);
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
            var first = _bus.ReadLiveCopperDmaWord(pc, fetchCycle, out var firstAccess);
            var secondRequestCycle = GetCopperSecondWordRequestCycle(firstAccess);
            RecordLiveCopperBitplaneRgaCollision(secondRequestCycle);
            var second = _bus.ReadLiveCopperDmaWord(AddDmaPointerOffset(pc, 2), secondRequestCycle, out var secondAccess);
            return new CopperInstructionLatch(first, firstAccess, second, secondAccess, CopperHpCycles);
        }

        private void RecordLiveCopperBitplaneRgaCollision(long copperRequestCycle)
        {
            var row = GetOutputRowForCycle(_liveFrameStartCycle, copperRequestCycle);
            if (!IsLiveLineValid(row))
            {
                return;
            }

            var state = _liveLineStates[row];
            if (state.PlaneCount <= 0 || state.FetchWords <= 0)
            {
                return;
            }

            for (var word = 0; word < state.FetchWords; word++)
            {
                for (var slot = 0; slot < state.FetchSlotStride; slot++)
                {
                    if (!TryGetBitplanePlaneForFetchSlot(slot, state.PlaneCount, state.FetchResolution, out var plane) ||
                        (state.PlaneHasRowMask & (1 << plane)) == 0)
                    {
                        continue;
                    }

                    var horizontal = state.DataFetchStart + (word * state.FetchSlotStride) + slot;
                    if (horizontal <= OcsDdfHardStopHorizontal)
                    {
                        continue;
                    }

                    var fetch = AgnusChipSlotScheduler.AlignToSlot(
                        state.LineStartCycle + ((long)horizontal * CopperHpCycles));
                    if (fetch == copperRequestCycle)
                    {
                        _liveBitplaneCopperCollisionMasks[GetLiveBitplaneMaskIndex(row, plane)] |= (UInt128)1 << word;
                    }
                }
            }
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
                RecordCopperQuiescentCopperMove(dataCycle, register);
                var affectsDisplay = IsDisplayRegisterWrite(register);
                if (affectsDisplay)
                {
                    _currentCopperRow = GetOutputRowForCycle(_liveFrameStartCycle, dataCycle);
                    AdvanceLiveDisplayWindowStateToCycle(dataCycle);
                    EnsureTimelineLineStartedBeforeDisplayWrite(dataCycle);
                    if (dataCycle > _liveFrameStartCycle && register is 0x08E or 0x090)
                    {
                        _liveFrameHasLateDisplayWindowWrites = true;
                    }
                }

                CommitLateBitplaneRgaCollisionsOnDisable(register, value, dataCycle);
                ApplyCopperMove(register, value, dataCycle, applyHardwareSideEffects: true);
                if (affectsDisplay)
                {
                    CaptureCopperDisplayWrite(dataCycle, register, value);
                    RecordLiveFrameWrite(dataCycle, register, value, isCopper: true);
                    RefreshLiveLineStateAfterDisplayStateChange(dataCycle);
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
            if (_liveNextSpriteRow >= LowResOutputHeight)
            {
                return long.MaxValue;
            }

            if (!IsLiveLineValid(_liveNextSpriteRow))
            {
                return GetOutputRowStartCycle(_liveFrameStartCycle, _liveNextSpriteRow);
            }

            if (!IsSpriteDmaEnabled())
            {
                return long.MaxValue;
            }

            return GetSpriteDmaFetchCycle(_liveFrameStartCycle, _liveNextSpriteRow, _liveNextSpriteIndex, _liveNextSpriteWord);
        }

        private void SkipLiveRowsWithoutFetches()
        {
            var advanced = false;
            while (_liveNextFetchRow < LowResOutputHeight)
            {
                var state = _liveLineStates[_liveNextFetchRow];
                if (!IsLiveLineValid(_liveNextFetchRow))
                {
                    return;
                }

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
                if (!IsLiveLineValid(_liveNextSpriteRow) || !IsSpriteDmaEnabled())
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
