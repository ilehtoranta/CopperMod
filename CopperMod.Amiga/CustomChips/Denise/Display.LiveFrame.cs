/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        internal void ResetLiveDma()
        {
            _liveFrameValid = false;
            _liveCycle = 0;
            _liveFrameStartCycle = 0;
            _liveCapturedThroughCycle = -1;
            _liveCausalDisplayStateThroughCycle = -1;
            _liveFinalizedPresentationThroughCycle = -1;
            _liveNextLineStateRow = 0;
            _liveNextFetchRow = 0;
            _liveNextFetchWord = 0;
            _liveNextFetchPlane = 0;
            _liveNextFetchSlot = 0;
            _liveNextSpriteRow = 0;
            _liveNextSpriteIndex = 0;
            _liveNextSpriteWord = 0;
            _liveBitplaneDmaFetches = 0;
            _liveSpriteDmaFetches = 0;
            _liveMissedSpriteDmaSlots = 0;
            _liveDisplayEventCount = 0;
            _liveCopperStepCount = 0;
            _livePendingWriteEventCount = 0;
            _liveFetchBatchWordCount = 0;
            _liveCpuVisibleWorkCycleCacheHits = 0;
            _liveCpuVisibleWorkCycleRebuilds = 0;
            ResetCopperQuiescenceCounters();
            ResetLiveRasterlinePlan(resetCounters: true);
            _liveFirstDisplayDmaCycle = -1;
            _liveLastDisplayDmaCycle = -1;
            _liveCopper = new CopperPresentationState(_copperListPointer, 0);
            _displayTimeline.Reset(0);
            _liveWakeVersion++;
            InvalidateLiveDisplayEventCycle();
            ClearLiveFrameCapture(0);
        }

        internal void AdvanceLiveDmaTo(long targetCycle)
        {
            System.Diagnostics.Debug.Assert(targetCycle >= 0, "Live display DMA advance cycles must be non-negative.");
            if (targetCycle < _liveCycle)
            {
                return;
            }

            if (!_liveDmaEnabled || !HasLiveDisplayWork())
            {
                AdvanceIdleLiveDmaTo(targetCycle);
                RenderBoundPresentationLinesThrough(_liveCapturedThroughCycle, completing: false);
                return;
            }

            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                var frameStopCycle = GetLiveFrameStopCycle();
                while (targetCycle >= frameStopCycle)
                {
                    AdvanceLiveDmaWithinFrame(frameStopCycle - 1);
                    if (_boundPresentationActive &&
                        !_boundPresentationCompleted &&
                        _boundPresentationFrameStartCycle == _liveFrameStartCycle)
                    {
                        RenderBoundPresentationLinesThrough(frameStopCycle - 1, completing: true);
                        _boundPresentationCompleted = true;
                    }

                    StartLiveFrame(frameStopCycle);
                    frameStopCycle = GetLiveFrameStopCycle();
                }

                AdvanceLiveDmaWithinFrame(targetCycle);
            }
            finally
            {
                EndLiveDmaCapture(savedAdvancingLiveDma);
            }
        }

        private bool BeginLiveDmaCapture()
        {
            var savedAdvancingLiveDma = _advancingLiveDma;
            _advancingLiveDma = true;
            return savedAdvancingLiveDma;
        }

        private void EndLiveDmaCapture(bool savedAdvancingLiveDma)
        {
            _advancingLiveDma = savedAdvancingLiveDma;
            if (!savedAdvancingLiveDma)
            {
                RenderBoundPresentationLinesThrough(_liveCapturedThroughCycle, completing: false);
            }
        }

        private long GetFrameStopCycle(long frameStartCycle)
            => _bus.GetFrameStopCycle(frameStartCycle);

        private long GetLiveFrameStopCycle()
            => GetFrameStopCycle(_liveFrameStartCycle);

        private long GetNextFrameStartCycle(long cycle)
            => _bus.GetNextFrameStartCycle(cycle);

        public void ScheduleWrite(AgnusDisplayRegisterWrite registerWrite)
        {
            if (!_liveDmaEnabled)
            {
                return;
            }

            registerWrite = registerWrite.Normalize();
            var cycle = registerWrite.Cycle;
            var offset = registerWrite.Offset;
            var value = registerWrite.Value;
            if (!IsDisplayRegisterWrite(offset))
            {
                return;
            }

            if (!_advancingLiveDma &&
                _liveFrameValid &&
                cycle <= _liveFinalizedPresentationThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot schedule display register 0x{offset:X3} at cycle {cycle} " +
                    $"behind the finalized presentation horizon {_liveFinalizedPresentationThroughCycle}.");
            }

            if (!_advancingLiveDma &&
                _liveFrameValid &&
                cycle < _liveCausalDisplayStateThroughCycle)
            {
                _ = _bus.TryGetCommittedAgnusSlotOwner(cycle, out var writeCycleOwner);
                _ = _bus.TryGetCommittedAgnusSlotOwner(
                    _liveCausalDisplayStateThroughCycle,
                    out var horizonOwner);
                throw new InvalidOperationException(
                    $"Cannot schedule display register 0x{offset:X3} at cycle {cycle} " +
                    $"behind the causal display horizon {_liveCausalDisplayStateThroughCycle}; " +
                    $"coverage={_liveCapturedThroughCycle}, " +
                    $"live={_liveCycle}, bus={_bus.ExecutedChipBusHorizon}, " +
                    $"writeOwner={writeCycleOwner}, horizonOwner={horizonOwner}.");
            }

            var impact = CustomRegisterScheduleClassifier.GetPotentialEventScheduleImpact(
                _chipset,
                offset);
            if (CustomRegisterScheduleClassifier.AffectsEventSchedule(impact))
            {
                _bus.NotifyCustomRegisterScheduleChanged(offset, cycle, impact);
            }

            if (_pendingWrites.Count >= MaxPendingWrites)
            {
                _pendingWrites.RemoveRange(0, MaxPendingWrites / 2);
                _pendingIndex = Math.Max(0, _pendingIndex - (MaxPendingWrites / 2));
            }

            var pending = new PendingCustomWrite(cycle, offset, value);
            var insertIndex = _pendingWrites.Count;
            while (insertIndex > _pendingIndex && _pendingWrites[insertIndex - 1].Cycle > cycle)
            {
                insertIndex--;
            }

            _pendingWrites.Insert(insertIndex, pending);
            _liveWakeVersion++;
            InvalidateLiveDisplayEventCycle();

            // Slot preparation can run ahead of captured display state. A timing write
            // must release those future reservations even when no captured rows rewind.
            var changedSlotOwners = CustomRegisterScheduleClassifier.GetPreparedDisplaySlotOwnerChanges(
                _chipset,
                offset,
                value);
            if (changedSlotOwners != AgnusLiveDisplaySlotOwnerMask.None)
            {
                _bus.ClearLiveDisplayDmaSlotsFrom(cycle, changedSlotOwners);
            }

        }

        internal void ScheduleWrite(long cycle, ushort offset, ushort value)
            => ScheduleWrite(new AgnusDisplayRegisterWrite(cycle, offset, value));

        private void MarkLiveCausalDisplayCommit(long cycle)
        {
            if (_liveFrameValid && cycle >= _liveFrameStartCycle)
            {
                _liveCausalDisplayStateThroughCycle = Math.Max(
                    _liveCausalDisplayStateThroughCycle,
                    cycle);
            }
        }

        internal bool HasLiveDisplayWork()
        {
            if (!_liveDmaEnabled)
            {
                return false;
            }

            return IsLiveBitplaneDmaEnabled() ||
                IsLiveCopperDmaEnabled() ||
                IsSpriteDmaEnabled() ||
                TryPeekPendingWrite(out _);
        }

        private void AdvanceIdleLiveDmaTo(long targetCycle)
        {
            var frameStopCycle = GetLiveFrameStopCycle();
            while (targetCycle >= frameStopCycle)
            {
                StartLiveFrame(frameStopCycle);
                frameStopCycle = GetLiveFrameStopCycle();
            }

            _liveCycle = Math.Max(_liveCycle, targetCycle);
            _liveCapturedThroughCycle = Math.Max(_liveCapturedThroughCycle, targetCycle);
        }

        private static bool IsDisplayRegisterWrite(ushort offset)
        {
            return offset is 0x02E or
                0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A or
                0x08E or 0x090 or 0x092 or 0x094 or 0x096 or
                0x100 or 0x102 or 0x104 or 0x106 or 0x108 or 0x10A or 0x1E4 ||
                (offset >= 0x0E0 && offset <= 0x0FE) ||
                (offset >= 0x110 && offset <= 0x11E) ||
                (offset >= 0x120 && offset < 0x180) ||
                (offset >= 0x180 && offset < 0x1C0);
        }

        private void StartLiveFrame(long frameStartCycle)
        {
            ClearLiveFrameCapture(frameStartCycle);
            _liveFrameStartCycle = frameStartCycle;
            _liveCycle = frameStartCycle;
            _liveCapturedThroughCycle = frameStartCycle;
            _liveCausalDisplayStateThroughCycle = frameStartCycle;
            _liveFinalizedPresentationThroughCycle = frameStartCycle - 1;
            _liveNextLineStateRow = 0;
            _liveNextFetchRow = 0;
            _liveNextFetchWord = 0;
            _liveNextFetchPlane = 0;
            _liveNextFetchSlot = 0;
            _livePreparedFetchRow = 0;
            _livePreparedFetchWord = 0;
            _livePreparedFetchPlane = 0;
            _livePreparedFetchSlot = 0;
            _liveNextSpriteRow = 0;
            _liveNextSpriteIndex = 0;
            _liveNextSpriteWord = 0;
            _liveBitplaneDmaFetches = 0;
            _liveSpriteDmaFetches = 0;
            _liveMissedSpriteDmaSlots = 0;
            _liveDisplayEventCount = 0;
            _liveCopperStepCount = 0;
            _livePendingWriteEventCount = 0;
            _liveFetchBatchWordCount = 0;
            ResetLiveRasterlinePlan();
            _liveFirstDisplayDmaCycle = -1;
            _liveLastDisplayDmaCycle = -1;
            _liveCopper = CreateLiveCopperFrameStartState(frameStartCycle);

            ResetLiveDisplayWindowStateTracking();
            InvalidateLiveDisplayEventCycle();
        }

        private CopperPresentationState CreateLiveCopperFrameStartState(long frameStartCycle)
        {
            return IsLiveCopperDmaEnabled() && _copperListPointer != 0
                ? new CopperPresentationState(_copperListPointer, frameStartCycle)
                : new CopperPresentationState(0, frameStartCycle, pendingStart: true);
        }

        private void RecordLiveFrameWrite(long cycle, ushort offset, ushort value, bool isCopper = false)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                cycle >= GetLiveFrameStopCycle())
            {
                return;
            }

            offset = (ushort)(offset & 0x01FE);
            if (!IsLivePresentationReplayRegister(offset))
            {
                return;
            }

            var replayCycle = Math.Max(cycle, _liveFrameStartCycle);
            if (replayCycle > _liveFrameStartCycle && IsTimelineUnsafeFrameWrite(offset, isCopper))
            {
                MarkLiveTimelineUnsafe(offset, isCopper);
            }
        }

        private void MarkLiveTimelineUnsafe(ushort offset, bool isCopper)
        {
            offset = (ushort)(offset & 0x01FE);
            if (!_liveTimelineUnsafeForFrame)
            {
                _liveTimelineUnsafeOffset = offset;
                _liveTimelineUnsafeIsCopper = isCopper;
            }

            _liveTimelineUnsafeForFrame = true;
            _liveTimelineUnsafeRequiresCapturedRows |= IsCapturedRowOnlyUnsafeWrite(offset);
        }

        private static bool IsCapturedRowOnlyUnsafeWrite(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset is 0x100 or 0x104 or 0x108 or 0x10A;
        }

        private static bool IsLivePresentationReplayRegister(ushort offset)
        {
            return offset is 0x02E or
                0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A or
                0x08E or 0x090 or 0x092 or 0x094 or 0x096 or
                0x100 or 0x102 or 0x104 or 0x106 or 0x108 or 0x10A or 0x1E4 ||
                (offset >= 0x0E0 && offset <= 0x0FE) ||
                (offset >= 0x110 && offset <= 0x11E) ||
                (offset >= 0x120 && offset < 0x180) ||
                (offset >= 0x180 && offset < 0x1C0);
        }

        private void ClearLiveFrameCapture(long frameStartCycle)
        {
            _liveFrameValid = true;
            _liveFrameStartCycle = frameStartCycle;
            _liveCapturedThroughCycle = frameStartCycle;
            _liveCausalDisplayStateThroughCycle = frameStartCycle;
            _liveFinalizedPresentationThroughCycle = frameStartCycle - 1;
            var savedAdvancingLiveDma = _advancingLiveDma;
            _advancingLiveDma = false;
            try
            {
                ApplyPendingWrites(frameStartCycle);
            }
            finally
            {
                _advancingLiveDma = savedAdvancingLiveDma;
            }

            RebaseActiveBitplaneRowsToLiveFrameStart();
            _liveTimelineUnsafeForFrame = false;
            _liveTimelineUnsafeRequiresCapturedRows = false;
            _liveTimelineUnsafeOffset = 0;
            _liveTimelineUnsafeIsCopper = false;
            AdvanceLiveGeneration();
            _liveWakeVersion++;
            _displayTimeline.Reset(frameStartCycle);
            _spriteFrameCommands.Clear();
            CaptureInitialManualSpriteFrameCommands();
            _livePaletteSnapshots.Clear();
            _liveCurrentPaletteSnapshotIndex = -1;
            _liveCurrentPaletteSnapshotRow = -1;
            _livePaletteSnapshotDirty = true;
            Array.Clear(_liveSpriteWordMasks);
            Array.Clear(_liveSpriteDmaExhausted);
            ResetLiveSpriteDmaStates(0);
            _liveNextSpriteRow = 0;
            _liveNextSpriteIndex = 0;
            _liveNextSpriteWord = 0;
            _liveBitplaneDmaFetches = 0;
            _liveSpriteDmaFetches = 0;
            _liveMissedSpriteDmaSlots = 0;
            _liveFirstDisplayDmaCycle = -1;
            _liveLastDisplayDmaCycle = -1;
            ResetLiveDisplayWindowStateTracking();
            InvalidateLiveDisplayEventCycle();
        }

        private void AdvanceLiveGeneration()
        {
            _liveGeneration++;
            if (_liveGeneration != int.MaxValue)
            {
                return;
            }

            _liveGeneration = 1;
            for (var i = 0; i < _liveLineStates.Length; i++)
            {
                GetLiveLineState(i).Generation = 0;
            }
        }

        private void InvalidateLiveDisplayEventCycle()
        {
            _liveNextDisplayEventValid = false;
            _liveNextDisplayEventCycle = long.MaxValue;
            InvalidateLiveCopperWaitCycle();
            InvalidateLiveWorkCycle();
        }

        private void InvalidateLiveCopperWaitCycle()
        {
            _liveCopperWaitCycleValid = false;
            _liveCopperWaitCycle = long.MaxValue;
        }

        private long GetNextLiveDisplayEventCycle()
        {
            if (_liveNextDisplayEventValid)
            {
                return _liveNextDisplayEventCycle;
            }

            var pendingCycle = TryPeekPendingWrite(out var pending) ? pending.Cycle : long.MaxValue;
            var copperCycle = GetNextLiveCopperCycle(GetLiveFrameStopCycle());
            _liveNextDisplayEventCycle = Math.Min(pendingCycle, copperCycle);
            _liveNextDisplayEventValid = true;
            return _liveNextDisplayEventCycle;
        }

        private long GetNextLivePendingWriteCycle()
        {
            return TryPeekPendingWrite(out var pending)
                ? pending.Cycle
                : long.MaxValue;
        }

        private void ResetLiveRasterlinePlan(bool resetCounters = false)
        {
            Array.Fill(_liveRasterlinePlanRows, -1);
            Array.Clear(_liveRasterlinePlanEventCounts);
            Array.Clear(_liveRasterlinePlanRowsTouched);
            Array.Clear(_liveRasterlinePlanRowsValid);
            Array.Clear(_liveRasterlinePlanRowsOverflowed);
            Array.Clear(_liveRasterlinePlanWakeSearchIndices);
            Array.Clear(_liveRasterlinePlanWakeSearchLineStateVisibility);
            Array.Clear(_liveRasterlinePlanWakeSearchCycles);
            Array.Clear(_predictedRasterlinePlanEventCounts);
            Array.Clear(_predictedRasterlinePlanStatuses);
            Array.Clear(_rowDmaPlans);
            Array.Clear(_rowDmaExecutedMasks);
            Array.Clear(_rowDmaBitplaneCursorIndices);
            _liveRasterlinePlanRow = -1;
            _liveRasterlinePlanLineStartCycle = 0;
            _liveRasterlinePlanLineStopCycle = 0;
            _liveRasterlinePlanLastCycle = long.MinValue;
            _liveRasterlinePlanLineEventCount = 0;
            _liveRasterlinePlanLineValid = true;
            _liveRasterlinePlanLineOverflowed = false;
            _liveRasterlinePlanCompletedLines = 0;
            _liveRasterlinePlanCompletedValidLines = 0;
            _liveRasterlinePlanCompletedInvalidLines = 0;
            _liveRasterlinePlanCompletedOverflowLines = 0;
            _liveRasterlinePlanObservedEventCount = 0;
            _liveRasterlinePlanPendingWriteOrCopperEvents = 0;
            _liveRasterlinePlanLineStateEvents = 0;
            _liveRasterlinePlanBitplaneFetchEvents = 0;
            _liveRasterlinePlanSpriteFetchEvents = 0;
            _liveRasterlinePlanCopperBarrierEvents = 0;
            _liveRasterlinePlanMaxEventsPerLine = 0;
            _predictedRasterlinePlanLines = 0;
            _predictedRasterlinePlanMatchedLines = 0;
            _predictedRasterlinePlanMismatchedLines = 0;
            _predictedRasterlinePlanUnsupportedLines = 0;
            _predictedRasterlinePlanEventTotal = 0;
            _predictedRasterlinePlanUnsupportedCopperLines = 0;
            _predictedRasterlinePlanUnsupportedPendingWriteLines = 0;
            _predictedRasterlinePlanUnsupportedSpriteLines = 0;
            _predictedRasterlinePlanUnsupportedInvalidStateLines = 0;
            _predictedRasterlinePlanUnsupportedOverflowLines = 0;
            if (resetCounters)
            {
                _lastRowDmaPlansBuilt = 0;
                _lastRowDmaPlannedRowsExecuted = 0;
                _lastRowDmaBitplaneEntriesExecuted = 0;
                _lastRowDmaSpriteEntriesExecuted = 0;
                _lastRowDmaScalarFallbackRows = 0;
                _lastRowDmaPlanInvalidationRows = 0;
                _lastRowDmaPlanMismatchRows = 0;
            }
        }

        private bool TryBeginLiveRasterlinePlanEvent(long cycle, int expectedRow)
        {
            if (_liveRasterlinePlanRow >= 0 &&
                cycle >= _liveRasterlinePlanLineStartCycle &&
                cycle <= _liveRasterlinePlanLineStopCycle &&
                (expectedRow < 0 || expectedRow == _liveRasterlinePlanRow))
            {
                if (cycle < _liveRasterlinePlanLastCycle)
                {
                    _liveRasterlinePlanLineValid = false;
                    _liveRasterlinePlanRowsValid[GetRasterlineRingSlot(_liveRasterlinePlanRow)] = false;
                }
                else if (cycle > _liveRasterlinePlanLastCycle)
                {
                    _liveRasterlinePlanLastCycle = cycle;
                }

                return true;
            }

            if (!TryGetLiveRasterlinePlanRow(cycle, out var row))
            {
                return false;
            }

            if (_liveRasterlinePlanRow != row)
            {
                FinalizeLiveRasterlinePlanLine();
                _liveRasterlinePlanRow = row;
                _liveRasterlinePlanLineStartCycle = GetOutputRowStartCycle(_liveFrameStartCycle, row);
                _liveRasterlinePlanLineStopCycle = _liveRasterlinePlanLineStartCycle +
                    _bus.GetLineCyclesAt(_liveRasterlinePlanLineStartCycle) - 1;
                _liveRasterlinePlanLastCycle = long.MinValue;
                _liveRasterlinePlanLineEventCount = 0;
                _liveRasterlinePlanLineValid = true;
                _liveRasterlinePlanLineOverflowed = false;
                var slot = GetRasterlineRingSlot(row);
                _liveRasterlinePlanRows[slot] = row;
                _liveRasterlinePlanRowsTouched[slot] = true;
                _liveRasterlinePlanRowsValid[slot] = true;
                _liveRasterlinePlanRowsOverflowed[slot] = false;
                _liveRasterlinePlanEventCounts[slot] = 0;
                _liveRasterlinePlanWakeSearchIndices[slot] = 0;
                _liveRasterlinePlanWakeSearchLineStateVisibility[slot] = false;
                _liveRasterlinePlanWakeSearchCycles[slot] = 0;
                _predictedRasterlinePlanEventCounts[slot] = 0;
                _predictedRasterlinePlanStatuses[slot] = LiveRasterlinePredictionStatus.None;
            }

            if (expectedRow >= 0 && expectedRow != row)
            {
                _liveRasterlinePlanLineValid = false;
                _liveRasterlinePlanRowsValid[GetRasterlineRingSlot(row)] = false;
            }

            if (cycle < _liveRasterlinePlanLineStartCycle ||
                cycle > _liveRasterlinePlanLineStopCycle ||
                cycle < _liveRasterlinePlanLastCycle)
            {
                _liveRasterlinePlanLineValid = false;
                _liveRasterlinePlanRowsValid[GetRasterlineRingSlot(row)] = false;
            }

            if (cycle > _liveRasterlinePlanLastCycle)
            {
                _liveRasterlinePlanLastCycle = cycle;
            }

            return true;
        }

        private bool TryGetLiveRasterlinePlanRow(long cycle, out int row)
        {
            row = -1;
            if (!_liveFrameValid ||
                cycle < _liveFrameStartCycle ||
                cycle >= GetLiveFrameStopCycle())
            {
                return false;
            }

            row = _bus.GetBeamPosition(cycle).BeamLine - StandardVStart;
            return (uint)row < (uint)LowResOutputHeight;
        }

        private void RecordLiveRasterlinePlanEvent(
            LiveRasterlinePlanEventKind kind,
            long cycle,
            int row,
            long batchStopCycle,
            int cursorA,
            int cursorB,
            int cursorC)
        {
            RecordLiveRasterlinePlanEventCore(
                kind,
                cycle,
                row,
                batchStopCycle,
                cursorA,
                cursorB,
                cursorC);
        }

        private void RecordLiveRasterlinePlanEventCore(
            LiveRasterlinePlanEventKind kind,
            long cycle,
            int row,
            long batchStopCycle,
            int cursorA,
            int cursorB,
            int cursorC)
        {
            if (!TryBeginLiveRasterlinePlanEvent(cycle, row))
            {
                return;
            }

            _liveRasterlinePlanObservedEventCount++;
            IncrementLiveRasterlinePlanEventKind(kind);
            if (kind == LiveRasterlinePlanEventKind.PendingWriteOrCopper ||
                kind == LiveRasterlinePlanEventKind.CopperBarrier)
            {
                MarkPredictedRasterlinePlanUnsupported(
                    _liveRasterlinePlanRow,
                    kind == LiveRasterlinePlanEventKind.CopperBarrier
                        ? LiveRasterlinePredictionStatus.UnsupportedCopper
                        : LiveRasterlinePredictionStatus.UnsupportedPendingWrite);
            }
            else if (kind == LiveRasterlinePlanEventKind.SpriteFetchBatch)
            {
                TryAppendRecordedSpriteEventToPendingPrediction(_liveRasterlinePlanRow, kind, cycle, batchStopCycle, cursorA, cursorB, cursorC);
            }

            if (_liveRasterlinePlanLineEventCount >= MaxLiveRasterlinePlanEvents)
            {
                _liveRasterlinePlanLineEventCount++;
                _liveRasterlinePlanLineValid = false;
                _liveRasterlinePlanLineOverflowed = true;
                var planSlot = GetRasterlineRingSlot(_liveRasterlinePlanRow);
                _liveRasterlinePlanRowsValid[planSlot] = false;
                _liveRasterlinePlanRowsOverflowed[planSlot] = true;
                MarkPredictedRasterlinePlanUnsupported(
                    _liveRasterlinePlanRow,
                    LiveRasterlinePredictionStatus.UnsupportedOverflow);
                _liveRasterlinePlanMaxEventsPerLine = Math.Max(
                    _liveRasterlinePlanMaxEventsPerLine,
                    _liveRasterlinePlanLineEventCount);
                return;
            }

            var eventIndex = (GetRasterlineRingSlot(_liveRasterlinePlanRow) * MaxLiveRasterlinePlanEvents) +
                _liveRasterlinePlanLineEventCount;
            _liveRasterlinePlanEvents[eventIndex] = new LiveRasterlinePlanEvent(
                kind,
                cycle,
                _liveRasterlinePlanRow,
                batchStopCycle,
                cursorA,
                cursorB,
                cursorC);
            _liveRasterlinePlanLineEventCount++;
            _liveRasterlinePlanEventCounts[GetRasterlineRingSlot(_liveRasterlinePlanRow)] =
                _liveRasterlinePlanLineEventCount;
            _liveRasterlinePlanMaxEventsPerLine = Math.Max(
                _liveRasterlinePlanMaxEventsPerLine,
                _liveRasterlinePlanLineEventCount);
        }

        private void TryBuildPredictedRasterlinePlanForCapturedLine(int row)
        {
            var slot = GetRasterlineRingSlot(row);
            if ((uint)row >= (uint)LowResOutputHeight ||
                _liveRasterlinePlanRows[slot] != row ||
                _predictedRasterlinePlanStatuses[slot] != LiveRasterlinePredictionStatus.None)
            {
                return;
            }

            if (!IsLiveLineValid(row))
            {
                MarkPredictedRasterlinePlanUnsupported(row, LiveRasterlinePredictionStatus.UnsupportedInvalidState);
                return;
            }

            var lineStart = GetOutputRowStartCycle(_liveFrameStartCycle, row);
            var lineStop = lineStart + _bus.GetLineCyclesAt(lineStart) - 1;
            if (IsLiveCopperDmaEnabled())
            {
                MarkPredictedRasterlinePlanUnsupported(row, LiveRasterlinePredictionStatus.UnsupportedCopper);
                return;
            }

            if (HasPendingWriteInCycleRange(lineStart, lineStop))
            {
                MarkPredictedRasterlinePlanUnsupported(row, LiveRasterlinePredictionStatus.UnsupportedPendingWrite);
                return;
            }

            _predictedRasterlinePlanStatuses[slot] = LiveRasterlinePredictionStatus.PendingValidation;
            _predictedRasterlinePlanEventCounts[slot] = 0;
            if (!TryAppendPredictedRasterlinePlanEvent(
                    row,
                    new LiveRasterlinePlanEvent(
                        LiveRasterlinePlanEventKind.LineStateCapture,
                        lineStart,
                        row,
                        lineStart,
                        row,
                        0,
                        0)))
            {
                MarkPredictedRasterlinePlanUnsupported(row, LiveRasterlinePredictionStatus.UnsupportedOverflow);
                return;
            }

            var state = GetLiveLineState(row);
            var hasBitplaneFetches =
                state.PlaneCount > 0 &&
                state.FetchWords > 0 &&
                state.DisplayWindowVerticallyOpen &&
                IsBitplaneDmaEnabled(state.Dmacon);
            if (!hasBitplaneFetches)
            {
                return;
            }

            var firstFetchCycle = GetFirstLiveBitplaneFetchCycleForRendering(row, state);
            if (firstFetchCycle == long.MaxValue ||
                firstFetchCycle > lineStop ||
                !TryGetFirstLiveBitplaneFetchCursor(state, out var firstPlane, out var firstSlot))
            {
                return;
            }

            if (!TryAppendPredictedRasterlinePlanEvent(
                    row,
                    new LiveRasterlinePlanEvent(
                        LiveRasterlinePlanEventKind.BitplaneFetchBatch,
                        firstFetchCycle,
                        row,
                        lineStop,
                        firstPlane,
                        0,
                        firstSlot)))
            {
                MarkPredictedRasterlinePlanUnsupported(row, LiveRasterlinePredictionStatus.UnsupportedOverflow);
            }
        }

        private static bool TryGetFirstLiveBitplaneFetchCursor(LiveLineState state, out int plane, out int slot)
        {
            plane = 0;
            slot = 0;
            var planeCount = Math.Max(0, state.PlaneCount);
            for (; slot < state.FetchSlotStride; slot++)
            {
                if (TryGetBitplanePlaneForFetchSlot(slot, planeCount, state.FetchResolution, out plane))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryAppendRecordedSpriteEventToPendingPrediction(
            int row,
            LiveRasterlinePlanEventKind kind,
            long cycle,
            long batchStopCycle,
            int cursorA,
            int cursorB,
            int cursorC)
        {
            var slot = GetRasterlineRingSlot(row);
            if ((uint)row >= (uint)LowResOutputHeight ||
                _liveRasterlinePlanRows[slot] != row ||
                _predictedRasterlinePlanStatuses[slot] != LiveRasterlinePredictionStatus.PendingValidation)
            {
                return;
            }

            _ = TryAppendPredictedRasterlinePlanEvent(
                row,
                new LiveRasterlinePlanEvent(kind, cycle, row, batchStopCycle, cursorA, cursorB, cursorC));
        }

        private bool TryAppendPredictedRasterlinePlanEvent(int row, LiveRasterlinePlanEvent planEvent)
        {
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return false;
            }

            var slot = GetRasterlineRingSlot(row);
            if (_liveRasterlinePlanRows[slot] != row)
            {
                return false;
            }

            var count = _predictedRasterlinePlanEventCounts[slot];
            if (count >= MaxLiveRasterlinePlanEvents)
            {
                return false;
            }

            var baseIndex = slot * MaxLiveRasterlinePlanEvents;
            var insertIndex = count;
            while (insertIndex > 0 &&
                IsRasterlinePlanEventAfter(_predictedRasterlinePlanEvents[baseIndex + insertIndex - 1], planEvent))
            {
                _predictedRasterlinePlanEvents[baseIndex + insertIndex] = _predictedRasterlinePlanEvents[baseIndex + insertIndex - 1];
                insertIndex--;
            }

            _predictedRasterlinePlanEvents[baseIndex + insertIndex] = planEvent;
            _predictedRasterlinePlanEventCounts[slot] = count + 1;
            _predictedRasterlinePlanEventTotal++;
            return true;
        }

        private static bool IsRasterlinePlanEventAfter(LiveRasterlinePlanEvent left, LiveRasterlinePlanEvent right)
        {
            if (left.Cycle != right.Cycle)
            {
                return left.Cycle > right.Cycle;
            }

            return GetRasterlinePlanEventOrder(left.Kind) > GetRasterlinePlanEventOrder(right.Kind);
        }

        private static int GetRasterlinePlanEventOrder(LiveRasterlinePlanEventKind kind)
        {
            return kind switch
            {
                LiveRasterlinePlanEventKind.PendingWriteOrCopper => 0,
                LiveRasterlinePlanEventKind.CopperBarrier => 1,
                LiveRasterlinePlanEventKind.LineStateCapture => 2,
                LiveRasterlinePlanEventKind.SpriteFetchBatch => 3,
                LiveRasterlinePlanEventKind.BitplaneFetchBatch => 4,
                _ => 5
            };
        }

        private void MarkPredictedRasterlinePlanUnsupported(int row, LiveRasterlinePredictionStatus status)
        {
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return;
            }

            var slot = GetRasterlineRingSlot(row);
            if (_liveRasterlinePlanRows[slot] != row)
            {
                return;
            }

            if (_predictedRasterlinePlanStatuses[slot] is LiveRasterlinePredictionStatus.Matched or
                LiveRasterlinePredictionStatus.Mismatched)
            {
                return;
            }

            _predictedRasterlinePlanStatuses[slot] = status;
            _predictedRasterlinePlanEventCounts[slot] = 0;
        }

        private void ValidatePredictedRasterlinePlan(int row)
        {
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return;
            }

            var slot = GetRasterlineRingSlot(row);
            if (_liveRasterlinePlanRows[slot] != row)
            {
                return;
            }

            var status = _predictedRasterlinePlanStatuses[slot];
            if (status == LiveRasterlinePredictionStatus.None)
            {
                return;
            }

            if (status != LiveRasterlinePredictionStatus.PendingValidation)
            {
                _predictedRasterlinePlanUnsupportedLines++;
                IncrementPredictedRasterlinePlanUnsupportedReason(status);
                return;
            }

            _predictedRasterlinePlanLines++;
            if (DoesPredictedRasterlinePlanMatchRecorded(row))
            {
                _predictedRasterlinePlanStatuses[slot] = LiveRasterlinePredictionStatus.Matched;
                _predictedRasterlinePlanMatchedLines++;
            }
            else
            {
                _predictedRasterlinePlanStatuses[slot] = LiveRasterlinePredictionStatus.Mismatched;
                _predictedRasterlinePlanMismatchedLines++;
            }
        }

        private bool DoesPredictedRasterlinePlanMatchRecorded(int row)
        {
            var slot = GetRasterlineRingSlot(row);
            if (_liveRasterlinePlanRows[slot] != row)
            {
                return false;
            }

            var expectedCount = _predictedRasterlinePlanEventCounts[slot];
            var actualCount = Math.Min(_liveRasterlinePlanEventCounts[slot], MaxLiveRasterlinePlanEvents);
            if (expectedCount != actualCount)
            {
                return false;
            }

            var baseIndex = slot * MaxLiveRasterlinePlanEvents;
            for (var i = 0; i < expectedCount; i++)
            {
                var expected = _predictedRasterlinePlanEvents[baseIndex + i];
                var actual = _liveRasterlinePlanEvents[baseIndex + i];
                if (expected.Kind != actual.Kind ||
                    expected.Cycle != actual.Cycle ||
                    expected.Row != actual.Row ||
                    expected.BatchStopCycle != actual.BatchStopCycle ||
                    expected.CursorA != actual.CursorA ||
                    expected.CursorB != actual.CursorB ||
                    expected.CursorC != actual.CursorC)
                {
                    return false;
                }
            }

            return true;
        }

        private void IncrementPredictedRasterlinePlanUnsupportedReason(LiveRasterlinePredictionStatus status)
        {
            switch (status)
            {
                case LiveRasterlinePredictionStatus.UnsupportedCopper:
                    _predictedRasterlinePlanUnsupportedCopperLines++;
                    break;
                case LiveRasterlinePredictionStatus.UnsupportedPendingWrite:
                    _predictedRasterlinePlanUnsupportedPendingWriteLines++;
                    break;
                case LiveRasterlinePredictionStatus.UnsupportedSprite:
                    _predictedRasterlinePlanUnsupportedSpriteLines++;
                    break;
                case LiveRasterlinePredictionStatus.UnsupportedInvalidState:
                    _predictedRasterlinePlanUnsupportedInvalidStateLines++;
                    break;
                case LiveRasterlinePredictionStatus.UnsupportedOverflow:
                    _predictedRasterlinePlanUnsupportedOverflowLines++;
                    break;
            }
        }

        private bool HasPendingWriteInCycleRange(long startCycle, long stopCycle)
        {
            for (var i = _pendingIndex; i < _pendingWrites.Count; i++)
            {
                var cycle = _pendingWrites[i].Cycle;
                if (cycle > stopCycle)
                {
                    return false;
                }

                if (cycle >= startCycle)
                {
                    return true;
                }
            }

            return false;
        }

        private void IncrementLiveRasterlinePlanEventKind(LiveRasterlinePlanEventKind kind)
        {
            switch (kind)
            {
                case LiveRasterlinePlanEventKind.PendingWriteOrCopper:
                    _liveRasterlinePlanPendingWriteOrCopperEvents++;
                    break;
                case LiveRasterlinePlanEventKind.LineStateCapture:
                    _liveRasterlinePlanLineStateEvents++;
                    break;
                case LiveRasterlinePlanEventKind.BitplaneFetchBatch:
                    _liveRasterlinePlanBitplaneFetchEvents++;
                    break;
                case LiveRasterlinePlanEventKind.SpriteFetchBatch:
                    _liveRasterlinePlanSpriteFetchEvents++;
                    break;
                case LiveRasterlinePlanEventKind.CopperBarrier:
                    _liveRasterlinePlanCopperBarrierEvents++;
                    break;
            }
        }

        private void FinalizeLiveRasterlinePlanLine()
        {
            if (_liveRasterlinePlanRow < 0)
            {
                return;
            }

            ValidatePredictedRasterlinePlan(_liveRasterlinePlanRow);
            _liveRasterlinePlanCompletedLines++;
            if (_liveRasterlinePlanLineOverflowed)
            {
                _liveRasterlinePlanCompletedOverflowLines++;
            }

            if (_liveRasterlinePlanLineValid && !_liveRasterlinePlanLineOverflowed)
            {
                _liveRasterlinePlanCompletedValidLines++;
            }
            else
            {
                _liveRasterlinePlanCompletedInvalidLines++;
            }

            _liveRasterlinePlanRow = -1;
            _liveRasterlinePlanLineStartCycle = 0;
            _liveRasterlinePlanLineStopCycle = 0;
            _liveRasterlinePlanLastCycle = long.MinValue;
            _liveRasterlinePlanLineEventCount = 0;
            _liveRasterlinePlanLineValid = true;
            _liveRasterlinePlanLineOverflowed = false;
        }

        private int GetLiveRasterlinePlanLineCount()
            => _liveRasterlinePlanCompletedLines + (_liveRasterlinePlanRow >= 0 ? 1 : 0);

        private int GetLiveRasterlinePlanValidLineCount()
            => _liveRasterlinePlanCompletedValidLines +
                (_liveRasterlinePlanRow >= 0 && _liveRasterlinePlanLineValid && !_liveRasterlinePlanLineOverflowed ? 1 : 0);

        private int GetLiveRasterlinePlanInvalidLineCount()
            => _liveRasterlinePlanCompletedInvalidLines +
                (_liveRasterlinePlanRow >= 0 && (!_liveRasterlinePlanLineValid || _liveRasterlinePlanLineOverflowed) ? 1 : 0);

        private int GetLiveRasterlinePlanOverflowLineCount()
            => _liveRasterlinePlanCompletedOverflowLines +
                (_liveRasterlinePlanRow >= 0 && _liveRasterlinePlanLineOverflowed ? 1 : 0);

        private bool TryGetRecordedLiveRasterlinePlanWakeCandidate(
            long currentCycle,
            long targetCycle,
            out long candidate)
        {
            candidate = long.MaxValue;
            if (targetCycle > _liveCapturedThroughCycle ||
                targetCycle < currentCycle ||
                !TryGetLiveRasterlinePlanRangeRow(currentCycle, targetCycle, out var currentRow))
            {
                return false;
            }

            var slot = GetRasterlineRingSlot(currentRow);
            if (_liveRasterlinePlanRows[slot] != currentRow ||
                !_liveRasterlinePlanRowsTouched[slot] ||
                !_liveRasterlinePlanRowsValid[slot] ||
                _liveRasterlinePlanRowsOverflowed[slot])
            {
                return false;
            }

            var count = Math.Min(_liveRasterlinePlanEventCounts[slot], MaxLiveRasterlinePlanEvents);
            var baseIndex = slot * MaxLiveRasterlinePlanEvents;
            var lineStateEventsAreWakeVisible = HasLiveLineStateWakeWork();
            var searchIndex = _liveRasterlinePlanWakeSearchIndices[slot];
            if (searchIndex > count ||
                currentCycle < _liveRasterlinePlanWakeSearchCycles[slot] ||
                lineStateEventsAreWakeVisible != _liveRasterlinePlanWakeSearchLineStateVisibility[slot])
            {
                searchIndex = 0;
            }

            while (searchIndex < count)
            {
                var planEvent = _liveRasterlinePlanEvents[baseIndex + searchIndex];
                if (planEvent.Kind == LiveRasterlinePlanEventKind.LineStateCapture &&
                    !lineStateEventsAreWakeVisible)
                {
                    searchIndex++;
                    continue;
                }

                var cycle = planEvent.Cycle;
                if (cycle <= currentCycle)
                {
                    searchIndex++;
                    continue;
                }

                _liveRasterlinePlanWakeSearchIndices[slot] = searchIndex;
                _liveRasterlinePlanWakeSearchLineStateVisibility[slot] = lineStateEventsAreWakeVisible;
                _liveRasterlinePlanWakeSearchCycles[slot] = currentCycle;
                if (cycle <= targetCycle)
                {
                    candidate = cycle;
                    return true;
                }

                return true;
            }

            if (_liveRasterlinePlanWakeSearchIndices[slot] != count ||
                _liveRasterlinePlanWakeSearchLineStateVisibility[slot] != lineStateEventsAreWakeVisible ||
                _liveRasterlinePlanWakeSearchCycles[slot] != currentCycle)
            {
                _liveRasterlinePlanWakeSearchIndices[slot] = count;
                _liveRasterlinePlanWakeSearchLineStateVisibility[slot] = lineStateEventsAreWakeVisible;
                _liveRasterlinePlanWakeSearchCycles[slot] = currentCycle;
            }

            return true;
        }

        private bool TryGetLiveRasterlinePlanRangeRow(
            long currentCycle,
            long targetCycle,
            out int row)
        {
            if (_liveRasterlinePlanRow >= 0 &&
                currentCycle >= _liveRasterlinePlanLineStartCycle &&
                targetCycle <= _liveRasterlinePlanLineStopCycle)
            {
                row = _liveRasterlinePlanRow;
                return true;
            }

            if (!TryGetLiveRasterlinePlanRow(currentCycle, out row) ||
                !TryGetLiveRasterlinePlanRow(targetCycle, out var targetRow))
            {
                return false;
            }

            return row == targetRow;
        }

        private bool HasLiveLineStateWakeWork()
            => IsLiveBitplaneDmaEnabled() || IsSpriteDmaEnabled();

        internal bool HasLiveSpriteDmaWork()
            => _liveDmaEnabled && IsSpriteDmaEnabled();

        private long GetNextLiveCpuVisibleWorkCycle()
        {
            if (_liveCpuVisibleWorkCycleValid &&
                _liveCpuVisibleWorkCycleVersion == _liveWakeVersion)
            {
                _liveCpuVisibleWorkCycleCacheHits++;
                return _liveCpuVisibleWorkCycle;
            }

            var nextLineStateCycle = HasLiveLineStateWakeWork()
                ? GetNextLiveLineStateCycle()
                : long.MaxValue;
            var nextBitplaneFetchCycle = IsLiveBitplaneDmaEnabled()
                ? GetNextLiveBitplaneFetchCycle()
                : long.MaxValue;
            var nextSpriteFetchCycle = IsSpriteDmaEnabled()
                ? GetNextLiveSpriteFetchCycle()
                : long.MaxValue;
            _liveCpuVisibleWorkCycle = Math.Min(
                Math.Min(GetNextLiveDisplayEventCycle(), nextLineStateCycle),
                Math.Min(nextBitplaneFetchCycle, nextSpriteFetchCycle));
            _liveCpuVisibleWorkCycleVersion = _liveWakeVersion;
            _liveCpuVisibleWorkCycleValid = true;
            _liveCpuVisibleWorkCycleRebuilds++;
            return _liveCpuVisibleWorkCycle;
        }


    }
}
