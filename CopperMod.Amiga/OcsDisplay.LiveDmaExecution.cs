/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga
{
    internal sealed partial class OcsDisplay
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

            while (true)
            {
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
                        var barrierStopCycle = Math.Max(_liveFrameStartCycle, nextCopperBarrierCycle - 1);
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

                if (TryReplayLiveRasterlineDescriptorTo(
                        targetCycle,
                        includeCopper,
                        nextLineStateCycle,
                        nextBitplaneFetchCycle,
                        nextSpriteFetchCycle,
                        nextPendingWriteCycle))
                {
                    continue;
                }

                if (nextCycle > targetCycle)
                {
                    break;
                }

                AdvanceLiveDisplayStateTo(nextCycle, includeCopper);
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
            InvalidateLiveWorkCycle();
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
                        var barrierStopCycle = Math.Max(_liveFrameStartCycle, nextCopperBarrierCycle - 1);
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
            InvalidateLiveWorkCycle();
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

        private bool TryReplayLiveRasterlineDescriptorTo(
            long targetCycle,
            bool includeCopper,
            long nextLineStateCycle,
            long nextBitplaneFetchCycle,
            long nextSpriteFetchCycle,
            long nextPendingWriteCycle)
        {
            var nextReplayCycle = Math.Min(nextBitplaneFetchCycle, nextSpriteFetchCycle);
            if (nextReplayCycle == long.MaxValue ||
                nextReplayCycle > targetCycle ||
                nextLineStateCycle <= nextReplayCycle ||
                nextPendingWriteCycle <= nextReplayCycle)
            {
                return false;
            }

            if (IsLiveCopperDmaEnabled())
            {
                return false;
            }

            if (!includeCopper && GetNextLiveCopperBarrierCycle() <= nextReplayCycle)
            {
                return false;
            }

            var row = nextBitplaneFetchCycle <= nextSpriteFetchCycle
                ? _liveNextFetchRow
                : _liveNextSpriteRow;
            if (!TryGetLiveRasterlineDmaDescriptor(row, out var descriptor))
            {
                return false;
            }

            var replayStopCycle = Math.Min(
                descriptor.LineStopCycle,
                GetLiveDmaBatchStopCycle(targetCycle, nextLineStateCycle, includeCopper));
            if (nextReplayCycle > replayStopCycle ||
                HasPendingWriteInCycleRange(Math.Max(_liveCycle, descriptor.LineStartCycle), replayStopCycle))
            {
                _liveRasterlineDescriptorFallbackRows++;
                return false;
            }

            if (nextSpriteFetchCycle <= nextBitplaneFetchCycle)
            {
                if (!descriptor.HasSpriteSlots)
                {
                    _liveRasterlineDescriptorFallbackRows++;
                    return false;
                }
            }
            else if (!descriptor.HasBitplaneFetches)
            {
                _liveRasterlineDescriptorFallbackRows++;
                return false;
            }

            _liveRasterlineDescriptorReplayAttempts++;
            AdvanceLiveDisplayStateTo(nextReplayCycle, includeCopper);
            var replayed = false;
            if (nextSpriteFetchCycle <= nextBitplaneFetchCycle)
            {
                RecordLiveRasterlinePlanEvent(
                    LiveRasterlinePlanEventKind.SpriteFetchBatch,
                    nextSpriteFetchCycle,
                    _liveNextSpriteRow,
                    replayStopCycle,
                    _liveNextSpriteIndex,
                    _liveNextSpriteWord,
                    0);
                replayed = ReplayLiveRasterlineDescriptorSpriteBatch(descriptor, replayStopCycle);
            }
            else
            {
                RecordLiveRasterlinePlanEvent(
                    LiveRasterlinePlanEventKind.BitplaneFetchBatch,
                    nextBitplaneFetchCycle,
                    _liveNextFetchRow,
                    replayStopCycle,
                    _liveNextFetchPlane,
                    _liveNextFetchWord,
                    _liveNextFetchSlot);
                replayed = ReplayLiveRasterlineDescriptorBitplaneBatch(descriptor, replayStopCycle);
            }

            if (replayed)
            {
                _liveRasterlineDescriptorReplayedRows++;
                return true;
            }

            _liveRasterlineDescriptorFallbackRows++;
            return false;
        }

        private bool TryGetLiveRasterlineDmaDescriptor(int row, out LiveRasterlineDmaDescriptor descriptor)
        {
            descriptor = default;
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return false;
            }

            descriptor = _liveRasterlineDmaDescriptors[row];
            return descriptor.IsValid(_liveGeneration, row) &&
                IsLiveLineValid(row) &&
                DoesLiveLineStateMatchDescriptor(row, descriptor);
        }

        private bool DoesLiveLineStateMatchDescriptor(int row, LiveRasterlineDmaDescriptor descriptor)
        {
            var state = _liveLineStates[row];
            if (state.LineStartCycle != descriptor.LineStartCycle ||
                state.DisplayWindowVerticallyOpen != descriptor.DisplayWindowVerticallyOpen ||
                state.Bplcon0 != descriptor.Bplcon0 ||
                state.Bplcon1 != descriptor.Bplcon1 ||
                state.Bplcon2 != descriptor.Bplcon2 ||
                state.Dmacon != descriptor.Dmacon ||
                state.Bpl1Mod != descriptor.Bpl1Mod ||
                state.Bpl2Mod != descriptor.Bpl2Mod ||
                state.PlaneCount != descriptor.PlaneCount ||
                state.FetchWords != descriptor.FetchWords ||
                state.DataFetchStart != descriptor.DataFetchStart ||
                state.FetchSlotStride != descriptor.FetchSlotStride ||
                state.PlaneHasRowMask != descriptor.PlaneHasRowMask)
            {
                return false;
            }

            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                if (state.BitplaneRowAddresses[plane] != descriptor.GetBitplaneRowAddress(plane))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ReplayLiveRasterlineDescriptorBitplaneBatch(
            LiveRasterlineDmaDescriptor descriptor,
            long stopCycle)
        {
            if (TryCaptureLiveBitplaneFetchBatchWithRowPlan(stopCycle, out _, out var capturedByPlan))
            {
                return capturedByPlan;
            }

            var captured = false;
            while (_liveNextFetchRow == descriptor.Row)
            {
                if (!TryGetNextDescriptorBitplaneFetch(
                        descriptor,
                        out var fetchCycle,
                        out var plane,
                        out var word,
                        out var slot) ||
                    fetchCycle > stopCycle)
                {
                    return captured;
                }

                _liveNextFetchPlane = plane;
                _liveNextFetchWord = word;
                _liveNextFetchSlot = slot;
                CaptureLiveBitplaneFetch(descriptor.Row, plane, word, fetchCycle, _liveLineStates[descriptor.Row]);
                AdvanceLiveFetchCursor();
                captured = true;
            }

            return captured;
        }

        private bool TryGetNextDescriptorBitplaneFetch(
            LiveRasterlineDmaDescriptor descriptor,
            out long fetchCycle,
            out int plane,
            out int word,
            out int slot)
        {
            fetchCycle = long.MaxValue;
            plane = 0;
            word = _liveNextFetchWord;
            slot = _liveNextFetchSlot;
            if (!descriptor.HasBitplaneFetches ||
                _liveNextFetchRow != descriptor.Row ||
                word >= descriptor.FetchWords)
            {
                return false;
            }

            var planeCount = Math.Max(0, descriptor.PlaneCount);
            while (word < descriptor.FetchWords)
            {
                while (slot < descriptor.FetchSlotStride)
                {
                    if (TryGetBitplanePlaneForFetchSlot(slot, planeCount, descriptor.FetchSlotStride, out plane))
                    {
                        var fetchHorizontal = descriptor.DataFetchStart + (word * descriptor.FetchSlotStride) + slot;
                        fetchCycle = AgnusChipSlotScheduler.AlignToSlot(
                            descriptor.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
                        return true;
                    }

                    slot++;
                }

                slot = 0;
                word++;
            }

            return false;
        }

        private bool ReplayLiveRasterlineDescriptorSpriteBatch(
            LiveRasterlineDmaDescriptor descriptor,
            long stopCycle)
        {
            if (!descriptor.HasSpriteSlots)
            {
                return false;
            }

            if (TryCaptureLiveSpriteFetchBatchWithRowPlan(stopCycle, out _, out var capturedByPlan))
            {
                return capturedByPlan;
            }

            var captured = false;
            while (_liveNextSpriteRow == descriptor.Row)
            {
                SkipLiveSpriteSlotsWithoutFetches();
                if (_liveNextSpriteRow != descriptor.Row ||
                    !IsLiveLineValid(_liveNextSpriteRow) ||
                    !IsSpriteDmaEnabled())
                {
                    return captured;
                }

                var fetchCycle = GetNextLiveSpriteFetchCycle();
                if (fetchCycle > stopCycle)
                {
                    return captured;
                }

                _ = TryCaptureKnownLiveSpriteDmaSlot(
                    _liveNextSpriteRow,
                    _liveNextSpriteIndex,
                    _liveNextSpriteWord,
                    fetchCycle);
                AdvanceLiveSpriteFetchCursor();
                captured = true;
            }

            return captured;
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

            if (_liveCopper.Waiting)
            {
                var blitterReadyCycle = GetCopperBlitterReadyCycle(_liveCopper.WaitSecond, _liveCopper.Cycle);
                if (blitterReadyCycle > _liveCopper.Cycle)
                {
                    return NormalizeCopperBatchBarrier(currentCycle, targetCycle, blitterReadyCycle);
                }

                if (!TryGetCopperWaitCycle(
                    _liveCopper.WaitFirst,
                    _liveCopper.WaitSecond,
                    _liveFrameStartCycle,
                    _liveCopper.Cycle,
                    targetCycle + 1,
                    blitterFinished: true,
                    out var waitCycle))
                {
                    return null;
                }

                // A WAIT has no CPU-visible effect while it is sleeping. Once it wakes,
                // stop at the next copper-instruction boundary so the live copper can
                // fetch the following instruction from memory that may have changed.
                return NormalizeCopperBatchBarrier(
                    currentCycle,
                    targetCycle,
                    waitCycle + CopperHpToCpuCycles(CopperWaitWakeHpUnits));
            }

            // Do not scan ahead through copper list memory here. The next copper
            // instruction can be changed by DMA before it is fetched, so the only safe
            // horizon is the current live copper execution point. Once the instruction
            // is latched, PendingMove/PendingSkip/Waiting can provide a longer boundary.
            var fetchCycle = Math.Max(_liveCopper.Cycle, _liveFrameStartCycle);
            return NormalizeCopperBatchBarrier(currentCycle, targetCycle, fetchCycle);
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
