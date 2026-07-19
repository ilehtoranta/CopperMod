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
            ResetCopperQuiescenceCounters();
            ResetLiveRasterlinePlan(resetDescriptorCounters: true);
            _liveFirstDisplayDmaCycle = -1;
            _liveLastDisplayDmaCycle = -1;
            _liveCopper = new CopperPresentationState(_copperListPointer, 0);
            _previousLiveSpriteFrameStartCycle = long.MinValue;
            _previousLiveSpriteFrameCommands.Clear();
            _archivedTimelineValid = false;
            _archivedTimelineFrameStartCycle = long.MinValue;
            _archivedTimelineFrameStopCycle = long.MinValue;
            _archivedPaletteSnapshotCount = 0;
            _displayTimeline.Reset(0);
            _archivedDisplayTimeline.Reset(long.MinValue);
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
                return;
            }

            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                var frameStopCycle = GetLiveFrameStopCycle();
                while (targetCycle >= frameStopCycle)
                {
                    AdvanceLiveDmaWithinFrame(frameStopCycle - 1);
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

            if (!_advancingLiveDma && _liveFrameValid && cycle <= _liveCapturedThroughCycle)
            {
                InvalidateLiveCaptureFrom(cycle, offset);
            }
        }

        internal void ScheduleWrite(long cycle, ushort offset, ushort value)
            => ScheduleWrite(new AgnusDisplayRegisterWrite(cycle, offset, value));

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
                (offset >= 0x0E0 && offset <= 0x0F6) ||
                (offset >= 0x110 && offset <= 0x11A) ||
                (offset >= 0x120 && offset < 0x180) ||
                (offset >= 0x180 && offset < 0x1C0);
        }

        private void InvalidateLiveCaptureFrom(long cycle, ushort offset)
        {
            if (!_liveFrameValid || cycle < _liveFrameStartCycle || cycle > GetLiveFrameStopCycle())
            {
                return;
            }

            var row = Math.Clamp(GetOutputRowForCycle(_liveFrameStartCycle, cycle), 0, LowResOutputHeight - 1);
            var invalidateRow = ShouldPreserveCompletedLiveRowForInvalidation(row, offset)
                ? Math.Min(row + 1, LowResOutputHeight)
                : row;
            for (var y = invalidateRow; y < LowResOutputHeight; y++)
            {
                _liveLineStates[y].Generation = 0;
            }

            if (!_liveTimelineUnsafeForFrame)
            {
                _displayTimeline.InvalidateFromRow(invalidateRow);
            }
            ClearLiveBitplaneWordMasksFrom(invalidateRow);
            ClearLiveSpriteWordMasksFrom(invalidateRow);
            ResetLiveSpriteDmaStates(invalidateRow);
            ResetLiveRasterlinePlan();
            _liveNextLineStateRow = Math.Min(_liveNextLineStateRow, invalidateRow);
            _liveNextFetchRow = Math.Min(_liveNextFetchRow, invalidateRow);
            _liveNextFetchWord = 0;
            _liveNextFetchPlane = 0;
            _liveNextFetchSlot = 0;
            _livePreparedFetchRow = Math.Min(_livePreparedFetchRow, invalidateRow);
            _livePreparedFetchWord = 0;
            _livePreparedFetchPlane = 0;
            _livePreparedFetchSlot = 0;
            _liveNextSpriteRow = Math.Min(_liveNextSpriteRow, invalidateRow);
            _liveNextSpriteIndex = 0;
            _liveNextSpriteWord = 0;
            _liveCycle = Math.Min(_liveCycle, Math.Max(_liveFrameStartCycle, cycle));
            _liveCapturedThroughCycle = Math.Min(_liveCapturedThroughCycle, Math.Max(_liveFrameStartCycle, cycle - 1));
            if (_liveCopper.Cycle > cycle)
            {
                _liveCopper.Cycle = cycle;
                InvalidateLiveDisplayEventCycle();
            }

            TrimLiveFrameWritesFrom(cycle);
            InvalidateLiveWorkCycle();
        }

        private bool ShouldPreserveCompletedLiveRowForInvalidation(int row, ushort offset)
        {
            if (!IsLiveLineValid(row))
            {
                return false;
            }

            offset = (ushort)(offset & 0x01FE);
            if (offset >= 0x180 && offset < 0x1C0)
            {
                return true;
            }

            var state = _liveLineStates[row];
            if (state.PlaneCount <= 0 ||
                state.FetchWords <= 0 ||
                !state.DisplayWindowVerticallyOpen ||
                !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return false;
            }

            var expectedMask = GetBitplaneWordMask(state.FetchWords);
            var planeCount = Math.Clamp(state.PlaneCount, 0, LiveBitplanePlaneCount);
            for (var plane = 0; plane < planeCount; plane++)
            {
                var actualMask = _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(row, plane)];
                if ((actualMask & expectedMask) != expectedMask)
                {
                    return false;
                }
            }

            return true;
        }

        private void StartLiveFrame(long frameStartCycle)
        {
            ArchiveLiveSpriteFrameBeforeStarting(frameStartCycle);
            ArchiveCompletedTimelineBeforeStarting(frameStartCycle);
            ArchiveLiveFrameWritesBeforeStarting(frameStartCycle);
            ClearLiveFrameCapture(frameStartCycle);
            _liveFrameStartCycle = frameStartCycle;
            _liveCycle = frameStartCycle;
            _liveCapturedThroughCycle = frameStartCycle;
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
            return IsLiveCopperDmaEnabled()
                ? new CopperPresentationState(_copperListPointer, frameStartCycle)
                : new CopperPresentationState(0, frameStartCycle, pendingStart: true);
        }

        private void ArchiveCompletedTimelineBeforeStarting(long nextFrameStartCycle)
        {
            ClearArchiveRejectCounters();
            _archivedTimelineValid = false;
            _archivedTimelineFrameStartCycle = long.MinValue;
            _archivedTimelineFrameStopCycle = long.MinValue;
            _archivedPaletteSnapshotCount = 0;

            if (!_liveFrameValid ||
                _liveFrameStartCycle >= nextFrameStartCycle ||
                _liveCapturedThroughCycle < Math.Min(nextFrameStartCycle, GetLiveFrameStopCycle()) - 1)
            {
                RecordArchiveReject(TimelineRejectReason.FrameIncomplete);
                return;
            }

            var frameStopCycle = Math.Min(nextFrameStartCycle, GetLiveFrameStopCycle());
            if (!_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                RecordArchiveReject(TimelineRejectReason.TimelineInvalid);
                return;
            }

            CompleteTimelineSpriteFetchOutcomes(_displayTimeline, _liveFrameStartCycle, frameStopCycle, allowExactCompletionReads: true);
            var rejectReason = GetTimelineRejectReason(_displayTimeline, _liveFrameStartCycle, frameStopCycle);
            if (rejectReason != TimelineRejectReason.None)
            {
                RecordArchiveReject(rejectReason);
                return;
            }

            var archived = _archivedDisplayTimeline;
            _archivedDisplayTimeline = _displayTimeline;
            _displayTimeline = archived;
            _archivedTimelineValid = true;
            _archivedTimelineFrameStartCycle = _liveFrameStartCycle;
            _archivedTimelineFrameStopCycle = frameStopCycle;
            _archivedPaletteSnapshotCount = _livePaletteSnapshotCount;
            Array.Copy(
                _livePaletteSnapshotColors,
                0,
                _archivedPaletteSnapshotColors,
                0,
                _livePaletteSnapshotCount * _colors.Length);
            Array.Copy(
                _livePaletteSnapshotConvertedColors,
                0,
                _archivedPaletteSnapshotConvertedColors,
                0,
                _livePaletteSnapshotCount * PaletteColorCount);
        }

        private void ArchiveLiveFrameWritesBeforeStarting(long nextFrameStartCycle)
        {
            _archivedFrameWritesValid = false;
            _archivedFrameWritesStartCycle = long.MinValue;
            _archivedFrameWritesStopCycle = long.MinValue;
            _archivedFrameWrites.Clear();

            if (!_liveFrameValid ||
                _liveFrameStartCycle >= nextFrameStartCycle)
            {
                return;
            }

            var frameStopCycle = Math.Min(nextFrameStartCycle, GetLiveFrameStopCycle());
            if (_liveCapturedThroughCycle < frameStopCycle - 1 ||
                !_liveFrameInitialStateValid ||
                _liveFrameWriteOverflowed ||
                _liveTimelineUnsafeRequiresCapturedRows ||
                _liveFrameHasLateDisplayWindowWrites ||
                _liveFrameWrites.Count == 0)
            {
                return;
            }

            CopyDisplayState(_liveFrameInitialState, _archivedFrameInitialState);
            for (var i = 0; i < _liveFrameWrites.Count; i++)
            {
                var write = _liveFrameWrites[i];
                if (write.Cycle >= frameStopCycle)
                {
                    break;
                }

                if (write.Cycle >= _liveFrameStartCycle)
                {
                    _archivedFrameWrites.Add(write);
                }
            }

            if (_archivedFrameWrites.Count == 0)
            {
                return;
            }

            _archivedFrameWritesValid = true;
            _archivedFrameWritesStartCycle = _liveFrameStartCycle;
            _archivedFrameWritesStopCycle = frameStopCycle;
        }

        private void ArchiveLiveSpriteFrameBeforeStarting(long nextFrameStartCycle)
        {
            var frameStopCycle = _liveFrameValid
                ? Math.Min(nextFrameStartCycle, GetLiveFrameStopCycle())
                : nextFrameStartCycle;
            var hasCarryCandidate = SavePreviousLiveSpriteArchiveForCarry();
            if (!_liveFrameValid ||
                _liveFrameStartCycle >= nextFrameStartCycle ||
                _liveCapturedThroughCycle < frameStopCycle - 1)
            {
                ClearPreviousLiveSpriteFrameArchive();
                if (hasCarryCandidate)
                {
                    TryCarryPreviousLiveSpriteArchive(frameStopCycle);
                }

                return;
            }

            _previousLiveSpriteFrameStartCycle = _liveFrameStartCycle;
            _previousLiveSpriteFrameCommands.Clear();
            ClearPreviousLiveSpriteWords();
            if (_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                CompleteTimelineSpriteFetchOutcomes(
                    _displayTimeline,
                    _liveFrameStartCycle,
                    frameStopCycle,
                    allowExactCompletionReads: true);
            }

            for (var i = 0; i < _spriteFrameCommands.Count; i++)
            {
                _previousLiveSpriteFrameCommands.Add(_spriteFrameCommands[i]);
            }

            ArchiveLiveSpriteWords(_displayTimeline);
            PruneIncompletePreviousLiveSpriteFrameCommands(frameStopCycle);
            if (hasCarryCandidate)
            {
                TryCarryPreviousLiveSpriteArchive(frameStopCycle);
            }
        }

        private void ClearPreviousLiveSpriteFrameArchive()
        {
            _previousLiveSpriteFrameStartCycle = long.MinValue;
            _previousLiveSpriteFrameCommands.Clear();
            ClearPreviousLiveSpriteWords();
        }

        private bool SavePreviousLiveSpriteArchiveForCarry()
        {
            _carryLiveSpriteFrameCommands.Clear();
            if (!_liveFrameValid ||
                GetFrameStopCycle(_previousLiveSpriteFrameStartCycle) != _liveFrameStartCycle ||
                _previousLiveSpriteFrameCommands.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < _previousLiveSpriteFrameCommands.Count; i++)
            {
                _carryLiveSpriteFrameCommands.Add(_previousLiveSpriteFrameCommands[i]);
            }

            Array.Copy(_previousLiveSpriteWords, _carryLiveSpriteWords, _previousLiveSpriteWords.Length);
            Array.Copy(_previousLiveSpriteWordMasks, _carryLiveSpriteWordMasks, _previousLiveSpriteWordMasks.Length);
            Array.Copy(_previousLiveSpriteDeniedMasks, _carryLiveSpriteDeniedMasks, _previousLiveSpriteDeniedMasks.Length);
            return true;
        }

        private bool TryCarryPreviousLiveSpriteArchive(long frameStopCycle)
        {
            if (_previousLiveSpriteFrameCommands.Count > 0 ||
                _carryLiveSpriteFrameCommands.Count == 0 ||
                !_liveFrameValid ||
                _liveFrameStartCycle >= frameStopCycle)
            {
                return false;
            }

            for (var i = 0; i < _carryLiveSpriteFrameCommands.Count; i++)
            {
                var command = _carryLiveSpriteFrameCommands[i];
                if (!CanCarryPreviousLiveSpriteCommand(command) ||
                    !HasCompleteCarriedLiveSpriteData(command, frameStopCycle))
                {
                    return false;
                }
            }

            _previousLiveSpriteFrameStartCycle = _liveFrameStartCycle;
            _previousLiveSpriteFrameCommands.Clear();
            for (var i = 0; i < _carryLiveSpriteFrameCommands.Count; i++)
            {
                _previousLiveSpriteFrameCommands.Add(_carryLiveSpriteFrameCommands[i]);
            }

            Array.Copy(_carryLiveSpriteWords, _previousLiveSpriteWords, _previousLiveSpriteWords.Length);
            Array.Copy(_carryLiveSpriteWordMasks, _previousLiveSpriteWordMasks, _previousLiveSpriteWordMasks.Length);
            Array.Copy(_carryLiveSpriteDeniedMasks, _previousLiveSpriteDeniedMasks, _previousLiveSpriteDeniedMasks.Length);
            SeedLiveSpriteCaptureFromCarriedArchive();
            return true;
        }

        private void SeedLiveSpriteCaptureFromCarriedArchive()
        {
            for (var row = 0; row < LowResOutputHeight; row++)
            {
                for (var spriteIndex = 0; spriteIndex < LiveSpriteChannelCount; spriteIndex++)
                {
                    var maskIndex = GetLiveSpriteMaskIndex(row, spriteIndex);
                    var carryMask = (byte)(_carryLiveSpriteWordMasks[maskIndex] & ~_carryLiveSpriteDeniedMasks[maskIndex]);
                    var missingMask = (byte)(carryMask & ~_liveSpriteWordMasks[maskIndex]);
                    if (missingMask == 0)
                    {
                        continue;
                    }

                    for (var word = 0; word < LiveSpriteWordsPerChannel; word++)
                    {
                        var bit = 1 << word;
                        if ((missingMask & bit) == 0)
                        {
                            continue;
                        }

                        _liveSpriteWords[GetLiveSpriteWordIndex(row, spriteIndex, word)] =
                            _carryLiveSpriteWords[GetLiveSpriteWordIndex(row, spriteIndex, word)];
                    }

                    _liveSpriteWordMasks[maskIndex] = (byte)(_liveSpriteWordMasks[maskIndex] | missingMask);
                }
            }
        }

        private bool CanCarryPreviousLiveSpriteCommand(SpriteFrameCommand command)
        {
            var spriteIndex = command.SpriteIndex;
            if (!command.Descriptor.IsDma ||
                (uint)spriteIndex >= LiveSpriteChannelCount ||
                !IsSpriteDmaEnabled() ||
                !IsSpriteDmaChannelAvailable(spriteIndex))
            {
                return false;
            }

            if (HasCompatibleCurrentLiveSpriteCommand(command))
            {
                return true;
            }

            if (HasCurrentLiveSpriteCommand(spriteIndex))
            {
                return false;
            }

            var expectedDataAddress = AddDmaPointerOffset(_sprites[spriteIndex].Pointer, 4);
            if (command.Descriptor.DataAddress != expectedDataAddress)
            {
                return false;
            }

            if (TryGetCapturedLiveSpriteControlBlock(command.Row, spriteIndex, out var pos, out var ctl))
            {
                if ((pos | ctl) == 0)
                {
                    return false;
                }

                var descriptor = CreateSpriteDescriptor(
                    pos,
                    ctl,
                    command.Descriptor.DataAddress,
                    isDma: true,
                    _sprites[spriteIndex].DataA,
                    _sprites[spriteIndex].DataB);
                return descriptor.HasSameRenderingAs(command.Descriptor);
            }

            if (!CurrentSpriteControlBlockMatches(command, _sprites[spriteIndex].Pointer))
            {
                return false;
            }

            return !_liveSpriteDmaExhausted[spriteIndex] &&
                !_liveSpriteDmaStates[spriteIndex].Exhausted;
        }

        private bool CurrentSpriteControlBlockMatches(SpriteFrameCommand command, uint controlAddress)
        {
            var pos = _bus.ReadChipWordForPresentation(controlAddress);
            var ctl = _bus.ReadChipWordForPresentation(AddDmaPointerOffset(controlAddress, 2));
            if ((pos | ctl) == 0)
            {
                return false;
            }

            var descriptor = CreateSpriteDescriptor(
                pos,
                ctl,
                command.Descriptor.DataAddress,
                isDma: true,
                _sprites[command.SpriteIndex].DataA,
                _sprites[command.SpriteIndex].DataB);
            return descriptor.HasSameRenderingAs(command.Descriptor);
        }

        private bool HasCompatibleCurrentLiveSpriteCommand(SpriteFrameCommand command)
        {
            for (var i = 0; i < _spriteFrameCommands.Count; i++)
            {
                if (_spriteFrameCommands[i].HasSameRenderingAs(command))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasCurrentLiveSpriteCommand(int spriteIndex)
        {
            for (var i = 0; i < _spriteFrameCommands.Count; i++)
            {
                if (_spriteFrameCommands[i].SpriteIndex == spriteIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetCapturedLiveSpriteControlBlock(int row, int spriteIndex, out ushort pos, out ushort ctl)
        {
            if (TryReadLiveCapturedSpriteWord(row, spriteIndex, 0, out pos) &&
                TryReadLiveCapturedSpriteWord(row, spriteIndex, 1, out ctl))
            {
                return true;
            }

            pos = 0;
            ctl = 0;
            return false;
        }

        private bool HasCompleteCarriedLiveSpriteData(SpriteFrameCommand command, long frameStopCycle)
        {
            var sprite = command.Descriptor;
            if (!sprite.IsDma)
            {
                return true;
            }

            var rowStop = GetTimelineRowStop(_liveFrameStartCycle, frameStopCycle);
            var yStart = Math.Max(Math.Max(sprite.YStart, command.Row), 0);
            var yStop = Math.Min(Math.Min(sprite.YStop, rowStop), LowResOutputHeight);
            for (var y = yStart; y < yStop; y++)
            {
                var lineStart = GetOutputRowStartCycle(_liveFrameStartCycle, y);
                if (lineStart >= frameStopCycle)
                {
                    break;
                }

                if (!HasCarriedLiveSpriteWord(y, command.SpriteIndex, 0) ||
                    !HasCarriedLiveSpriteWord(y, command.SpriteIndex, 1))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasCarriedLiveSpriteWord(int row, int spriteIndex, int word)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)spriteIndex >= LiveSpriteChannelCount ||
                (uint)word >= LiveSpriteWordsPerChannel)
            {
                return false;
            }

            var maskIndex = GetLiveSpriteMaskIndex(row, spriteIndex);
            var bit = (byte)(1 << word);
            return (_carryLiveSpriteWordMasks[maskIndex] & bit) != 0;
        }

        private void ClearPreviousLiveSpriteWords()
        {
            Array.Clear(_previousLiveSpriteWordMasks);
            Array.Clear(_previousLiveSpriteDeniedMasks);
        }

        private void ArchiveLiveSpriteWords(DisplayFrameTimeline timeline)
        {
            for (var row = 0; row < LowResOutputHeight; row++)
            {
                for (var spriteIndex = 0; spriteIndex < LiveSpriteChannelCount; spriteIndex++)
                {
                    for (var word = 0; word < LiveSpriteWordsPerChannel; word++)
                    {
                        var status = timeline.GetSpriteFetchStatus(row, spriteIndex, word);
                        if (status == TimelineFetchStatus.NotAttempted)
                        {
                            if (!TryReadLiveCapturedSpriteWord(row, spriteIndex, word, out var liveValue))
                            {
                                continue;
                            }

                            StorePreviousLiveSpriteWord(row, spriteIndex, word, liveValue, denied: false);
                            continue;
                        }

                        StorePreviousLiveSpriteWord(
                            row,
                            spriteIndex,
                            word,
                            timeline.GetSpriteWord(row, spriteIndex, word),
                            denied: status == TimelineFetchStatus.Denied);
                    }
                }
            }
        }

        private void StorePreviousLiveSpriteWord(int row, int spriteIndex, int word, ushort value, bool denied)
        {
            var wordIndex = GetLiveSpriteWordIndex(row, spriteIndex, word);
            var maskIndex = GetLiveSpriteMaskIndex(row, spriteIndex);
            var bit = (byte)(1 << word);
            _previousLiveSpriteWords[wordIndex] = value;
            _previousLiveSpriteWordMasks[maskIndex] = (byte)(_previousLiveSpriteWordMasks[maskIndex] | bit);
            if (denied)
            {
                _previousLiveSpriteDeniedMasks[maskIndex] = (byte)(_previousLiveSpriteDeniedMasks[maskIndex] | bit);
            }
            else
            {
                _previousLiveSpriteDeniedMasks[maskIndex] = (byte)(_previousLiveSpriteDeniedMasks[maskIndex] & ~bit);
            }
        }

        private void PruneIncompletePreviousLiveSpriteFrameCommands(long frameStopCycle)
        {
            for (var i = _previousLiveSpriteFrameCommands.Count - 1; i >= 0; i--)
            {
                if (!HasCompletePreviousLiveSpriteData(_previousLiveSpriteFrameCommands[i], frameStopCycle))
                {
                    _previousLiveSpriteFrameCommands.RemoveAt(i);
                }
            }
        }

        private bool HasCompletePreviousLiveSpriteData(SpriteFrameCommand command, long frameStopCycle)
        {
            var sprite = command.Descriptor;
            if (!sprite.IsDma)
            {
                return true;
            }

            var rowStop = GetTimelineRowStop(_previousLiveSpriteFrameStartCycle, frameStopCycle);
            var yStart = Math.Max(Math.Max(sprite.YStart, command.Row), 0);
            var yStop = Math.Min(Math.Min(sprite.YStop, rowStop), LowResOutputHeight);
            for (var y = yStart; y < yStop; y++)
            {
                var lineStart = GetOutputRowStartCycle(_previousLiveSpriteFrameStartCycle, y);
                if (lineStart >= frameStopCycle)
                {
                    break;
                }

                if (!HasPreviousLiveSpriteWord(y, command.SpriteIndex, 0) ||
                    !HasPreviousLiveSpriteWord(y, command.SpriteIndex, 1))
                {
                    return false;
                }
            }

            return true;
        }

        private void ClearArchiveRejectCounters()
        {
            _lastArchiveRejectFrameIncomplete = 0;
            _lastArchiveRejectTimelineInvalid = 0;
            _lastArchiveRejectUnsafeWrite = 0;
            _lastArchiveRejectSegmentCapacity = 0;
            _lastArchiveRejectMissingLine = 0;
            _lastArchiveRejectUnsafeLine = 0;
            _lastArchiveRejectMissingBitplaneFetch = 0;
            _lastArchiveRejectMissingSpriteFetch = 0;
            _lastArchiveRejectUnsafeOffset = 0;
            _lastArchiveRejectUnsafeIsCopper = false;
            _lastArchiveRejectMissingSpriteIndex = -1;
            _lastArchiveRejectMissingSpriteRow = -1;
            _lastArchiveRejectMissingSpriteWord = -1;
            _lastArchiveRejectMissingSpriteStatusA = -1;
            _lastArchiveRejectMissingSpriteStatusB = -1;
            _lastArchiveRejectMissingSpriteCommandRow = -1;
            _lastArchiveRejectMissingSpriteYStart = -1;
            _lastArchiveRejectMissingSpriteYStop = -1;
            _lastArchiveRejectMissingSpriteUsableChannels = -1;
            _lastArchiveRejectMissingSpriteDdfStart = -1;
            _lastArchiveRejectMissingSpriteDmacon = 0;
            _lastArchiveRejectMissingSpriteBplcon0 = 0;
            _lastArchiveRejectMissingSpritePreviousStatusA = -1;
            _lastArchiveRejectMissingSpritePreviousStatusB = -1;
        }

        private void RecordArchiveReject(TimelineRejectReason reason)
        {
            switch (reason)
            {
                case TimelineRejectReason.FrameIncomplete:
                    _lastArchiveRejectFrameIncomplete++;
                    break;
                case TimelineRejectReason.TimelineInvalid:
                    _lastArchiveRejectTimelineInvalid++;
                    break;
                case TimelineRejectReason.UnsafeWrite:
                    _lastArchiveRejectUnsafeWrite++;
                    _lastArchiveRejectUnsafeOffset = _liveTimelineUnsafeOffset;
                    _lastArchiveRejectUnsafeIsCopper = _liveTimelineUnsafeIsCopper;
                    break;
                case TimelineRejectReason.SegmentCapacity:
                    _lastArchiveRejectSegmentCapacity++;
                    break;
                case TimelineRejectReason.MissingLine:
                    _lastArchiveRejectMissingLine++;
                    break;
                case TimelineRejectReason.UnsafeLine:
                    _lastArchiveRejectUnsafeLine++;
                    if (TryFindFirstUnsafeTimelineLine(_displayTimeline, out var unsafeOffset, out var unsafeIsCopper))
                    {
                        _lastArchiveRejectUnsafeOffset = unsafeOffset;
                        _lastArchiveRejectUnsafeIsCopper = unsafeIsCopper;
                    }
                    break;
                case TimelineRejectReason.MissingBitplaneFetch:
                    _lastArchiveRejectMissingBitplaneFetch++;
                    break;
                case TimelineRejectReason.MissingSpriteFetch:
                    _lastArchiveRejectMissingSpriteFetch++;
                    break;
            }
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

            if (_liveFrameWrites.Count >= MaxPendingWrites)
            {
                _liveFrameWriteOverflowed = true;
                return;
            }

            var replayCycle = Math.Max(cycle, _liveFrameStartCycle);
            _liveFrameWrites.Add(new PendingCustomWrite(replayCycle, offset, value, isCopper));
            if (replayCycle > _liveFrameStartCycle && IsTimelineUnsafeFrameWrite(offset, isCopper))
            {
                MarkLiveTimelineUnsafe(offset, isCopper);
            }
        }

        private void TrimLiveFrameWritesFrom(long cycle)
        {
            var removeIndex = _liveFrameWrites.Count;
            while (removeIndex > 0 && _liveFrameWrites[removeIndex - 1].Cycle >= cycle)
            {
                removeIndex--;
            }

            if (removeIndex < _liveFrameWrites.Count)
            {
                _liveFrameWrites.RemoveRange(removeIndex, _liveFrameWrites.Count - removeIndex);
                _liveFrameWriteOverflowed = false;
                _liveTimelineUnsafeForFrame = false;
                _liveTimelineUnsafeRequiresCapturedRows = false;
                _liveTimelineUnsafeOffset = 0;
                _liveTimelineUnsafeIsCopper = false;
                for (var i = 0; i < _liveFrameWrites.Count; i++)
                {
                    if (IsTimelineUnsafeFrameWrite(_liveFrameWrites[i].Offset, _liveFrameWrites[i].IsCopper))
                    {
                        MarkLiveTimelineUnsafe(_liveFrameWrites[i].Offset, _liveFrameWrites[i].IsCopper);
                    }
                }
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
                (offset >= 0x0E0 && offset <= 0x0F6) ||
                (offset >= 0x110 && offset <= 0x11A) ||
                (offset >= 0x120 && offset < 0x180) ||
                (offset >= 0x180 && offset < 0x1C0);
        }

        private void ClearLiveFrameCapture(long frameStartCycle)
        {
            _liveFrameValid = true;
            _liveFrameStartCycle = frameStartCycle;
            _liveCapturedThroughCycle = frameStartCycle;
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
            CaptureDisplayState(_liveFrameInitialState);
            _liveFrameInitialStateValid = true;
            _liveFrameWrites.Clear();
            _liveFrameWriteOverflowed = false;
            _liveFrameHasLateDisplayWindowWrites = false;
            _liveTimelineUnsafeForFrame = false;
            _liveTimelineUnsafeRequiresCapturedRows = false;
            _liveTimelineUnsafeOffset = 0;
            _liveTimelineUnsafeIsCopper = false;
            AdvanceLiveGeneration();
            _liveWakeVersion++;
            _displayTimeline.Reset(frameStartCycle);
            _spriteFrameCommands.Clear();
            CaptureInitialManualSpriteFrameCommands();
            _livePaletteSnapshotCount = 0;
            _liveCurrentPaletteSnapshotIndex = -1;
            _livePaletteSnapshotDirty = true;
            Array.Clear(_liveSpriteWordMasks);
            Array.Clear(_liveSpriteDmaExhausted);
            _renderingArchivedTimeline = false;
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
                _liveLineStates[i].Generation = 0;
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

        private void ResetLiveRasterlinePlan(bool resetDescriptorCounters = false)
        {
            Array.Clear(_liveRasterlinePlanEventCounts);
            Array.Clear(_liveRasterlinePlanRowsTouched);
            Array.Clear(_liveRasterlinePlanRowsValid);
            Array.Clear(_liveRasterlinePlanRowsOverflowed);
            Array.Clear(_liveRasterlinePlanWakeSearchIndices);
            Array.Clear(_liveRasterlinePlanWakeSearchLineStateVisibility);
            Array.Clear(_liveRasterlinePlanWakeSearchCycles);
            Array.Clear(_predictedRasterlinePlanEventCounts);
            Array.Clear(_predictedRasterlinePlanStatuses);
            Array.Clear(_liveRasterlineDmaDescriptors);
            Array.Clear(_rowDmaPlans);
            Array.Clear(_rowDmaExecutedMasks);
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
            if (resetDescriptorCounters)
            {
                _liveRasterlineDescriptorBuilds = 0;
                _liveRasterlineDescriptorReplayAttempts = 0;
                _liveRasterlineDescriptorReplayedRows = 0;
                _liveRasterlineDescriptorFallbackRows = 0;
                _liveRasterlineDescriptorBitplaneRows = 0;
                _liveRasterlineDescriptorSpriteRows = 0;
                _liveRasterlineDescriptorMismatches = 0;
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
                _liveRasterlinePlanRowsTouched[row] = true;
                _liveRasterlinePlanRowsValid[row] = true;
                _liveRasterlinePlanRowsOverflowed[row] = false;
                _liveRasterlinePlanEventCounts[row] = 0;
                _liveRasterlinePlanWakeSearchIndices[row] = 0;
                _liveRasterlinePlanWakeSearchLineStateVisibility[row] = false;
                _liveRasterlinePlanWakeSearchCycles[row] = 0;
                _predictedRasterlinePlanEventCounts[row] = 0;
                _predictedRasterlinePlanStatuses[row] = LiveRasterlinePredictionStatus.None;
            }

            if (expectedRow >= 0 && expectedRow != row)
            {
                _liveRasterlinePlanLineValid = false;
                _liveRasterlinePlanRowsValid[row] = false;
            }

            if (cycle < _liveRasterlinePlanLineStartCycle ||
                cycle > _liveRasterlinePlanLineStopCycle ||
                cycle < _liveRasterlinePlanLastCycle)
            {
                _liveRasterlinePlanLineValid = false;
                _liveRasterlinePlanRowsValid[row] = false;
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
                TryAppendRecordedSpriteEventToPendingDescriptor(_liveRasterlinePlanRow, kind, cycle, batchStopCycle, cursorA, cursorB, cursorC);
            }

            if (_liveRasterlinePlanLineEventCount >= MaxLiveRasterlinePlanEvents)
            {
                _liveRasterlinePlanLineEventCount++;
                _liveRasterlinePlanLineValid = false;
                _liveRasterlinePlanLineOverflowed = true;
                _liveRasterlinePlanRowsValid[_liveRasterlinePlanRow] = false;
                _liveRasterlinePlanRowsOverflowed[_liveRasterlinePlanRow] = true;
                MarkPredictedRasterlinePlanUnsupported(
                    _liveRasterlinePlanRow,
                    LiveRasterlinePredictionStatus.UnsupportedOverflow);
                _liveRasterlinePlanMaxEventsPerLine = Math.Max(
                    _liveRasterlinePlanMaxEventsPerLine,
                    _liveRasterlinePlanLineEventCount);
                return;
            }

            var eventIndex = (_liveRasterlinePlanRow * MaxLiveRasterlinePlanEvents) + _liveRasterlinePlanLineEventCount;
            _liveRasterlinePlanEvents[eventIndex] = new LiveRasterlinePlanEvent(
                kind,
                cycle,
                _liveRasterlinePlanRow,
                batchStopCycle,
                cursorA,
                cursorB,
                cursorC);
            _liveRasterlinePlanLineEventCount++;
            _liveRasterlinePlanEventCounts[_liveRasterlinePlanRow] = _liveRasterlinePlanLineEventCount;
            _liveRasterlinePlanMaxEventsPerLine = Math.Max(
                _liveRasterlinePlanMaxEventsPerLine,
                _liveRasterlinePlanLineEventCount);
        }

        private void TryBuildPredictedRasterlinePlanForCapturedLine(int row)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                _predictedRasterlinePlanStatuses[row] != LiveRasterlinePredictionStatus.None)
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

            _predictedRasterlinePlanStatuses[row] = LiveRasterlinePredictionStatus.PendingValidation;
            _predictedRasterlinePlanEventCounts[row] = 0;
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

            var state = _liveLineStates[row];
            var hasBitplaneFetches =
                state.PlaneCount > 0 &&
                state.FetchWords > 0 &&
                state.DisplayWindowVerticallyOpen &&
                IsBitplaneDmaEnabled(state.Dmacon);
            var hasSpriteSlots = IsSpriteDmaEnabled();
            _liveRasterlineDmaDescriptors[row] = new LiveRasterlineDmaDescriptor(
                _liveGeneration,
                row,
                lineStart,
                lineStop,
                state.DisplayWindowVerticallyOpen,
                state.Resolution,
                state.FetchResolution,
                 state.Bplcon0,
                 state.Bplcon1,
                 state.Bplcon2,
                 state.Bplcon3,
                 state.DiwHigh,
                 state.DiwHighValid,
                 state.AgnusDiwHigh,
                 state.AgnusDiwHighValid,
                 state.AgnusDisplayWindow,
                 state.DeniseDisplayWindow,
                 state.DataFetchWindow,
                 state.Dmacon,
                state.Bpl1Mod,
                state.Bpl2Mod,
                state.PlaneCount,
                state.FetchWords,
                state.DataFetchStart,
                state.FetchSlotStride,
                state.PlaneHasRowMask,
                state.BitplaneRowAddresses[0],
                state.BitplaneRowAddresses[1],
                state.BitplaneRowAddresses[2],
                state.BitplaneRowAddresses[3],
                state.BitplaneRowAddresses[4],
                state.BitplaneRowAddresses[5],
                hasBitplaneFetches,
                hasSpriteSlots);
            _liveRasterlineDescriptorBuilds++;
            if (hasBitplaneFetches)
            {
                _liveRasterlineDescriptorBitplaneRows++;
            }

            if (hasSpriteSlots)
            {
                _liveRasterlineDescriptorSpriteRows++;
            }

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

        private void TryAppendRecordedSpriteEventToPendingDescriptor(
            int row,
            LiveRasterlinePlanEventKind kind,
            long cycle,
            long batchStopCycle,
            int cursorA,
            int cursorB,
            int cursorC)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                _predictedRasterlinePlanStatuses[row] != LiveRasterlinePredictionStatus.PendingValidation ||
                !_liveRasterlineDmaDescriptors[row].IsValid(_liveGeneration, row))
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

            var count = _predictedRasterlinePlanEventCounts[row];
            if (count >= MaxLiveRasterlinePlanEvents)
            {
                return false;
            }

            var baseIndex = row * MaxLiveRasterlinePlanEvents;
            var insertIndex = count;
            while (insertIndex > 0 &&
                IsRasterlinePlanEventAfter(_predictedRasterlinePlanEvents[baseIndex + insertIndex - 1], planEvent))
            {
                _predictedRasterlinePlanEvents[baseIndex + insertIndex] = _predictedRasterlinePlanEvents[baseIndex + insertIndex - 1];
                insertIndex--;
            }

            _predictedRasterlinePlanEvents[baseIndex + insertIndex] = planEvent;
            _predictedRasterlinePlanEventCounts[row] = count + 1;
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

            if (_predictedRasterlinePlanStatuses[row] is LiveRasterlinePredictionStatus.Matched or
                LiveRasterlinePredictionStatus.Mismatched)
            {
                return;
            }

            _predictedRasterlinePlanStatuses[row] = status;
            _predictedRasterlinePlanEventCounts[row] = 0;
            _liveRasterlineDmaDescriptors[row] = default;
        }

        private void ValidatePredictedRasterlinePlan(int row)
        {
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return;
            }

            var status = _predictedRasterlinePlanStatuses[row];
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
                _predictedRasterlinePlanStatuses[row] = LiveRasterlinePredictionStatus.Matched;
                _predictedRasterlinePlanMatchedLines++;
            }
            else
            {
                _predictedRasterlinePlanStatuses[row] = LiveRasterlinePredictionStatus.Mismatched;
                _predictedRasterlinePlanMismatchedLines++;
                _liveRasterlineDescriptorMismatches++;
            }
        }

        private bool DoesPredictedRasterlinePlanMatchRecorded(int row)
        {
            var expectedCount = _predictedRasterlinePlanEventCounts[row];
            var actualCount = Math.Min(_liveRasterlinePlanEventCounts[row], MaxLiveRasterlinePlanEvents);
            if (expectedCount != actualCount)
            {
                return false;
            }

            var baseIndex = row * MaxLiveRasterlinePlanEvents;
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
                !TryGetLiveRasterlinePlanRow(currentCycle, out var currentRow) ||
                !TryGetLiveRasterlinePlanRow(targetCycle, out var targetRow) ||
                currentRow != targetRow ||
                !_liveRasterlinePlanRowsTouched[currentRow] ||
                !_liveRasterlinePlanRowsValid[currentRow] ||
                _liveRasterlinePlanRowsOverflowed[currentRow])
            {
                return false;
            }

            var count = Math.Min(_liveRasterlinePlanEventCounts[currentRow], MaxLiveRasterlinePlanEvents);
            var baseIndex = currentRow * MaxLiveRasterlinePlanEvents;
            var lineStateEventsAreWakeVisible = HasLiveLineStateWakeWork();
            var searchIndex = _liveRasterlinePlanWakeSearchIndices[currentRow];
            if (searchIndex > count ||
                currentCycle < _liveRasterlinePlanWakeSearchCycles[currentRow] ||
                lineStateEventsAreWakeVisible != _liveRasterlinePlanWakeSearchLineStateVisibility[currentRow])
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

                _liveRasterlinePlanWakeSearchIndices[currentRow] = searchIndex;
                _liveRasterlinePlanWakeSearchLineStateVisibility[currentRow] = lineStateEventsAreWakeVisible;
                _liveRasterlinePlanWakeSearchCycles[currentRow] = currentCycle;
                if (cycle <= targetCycle)
                {
                    candidate = cycle;
                    return true;
                }

                return true;
            }

            if (_liveRasterlinePlanWakeSearchIndices[currentRow] != count ||
                _liveRasterlinePlanWakeSearchLineStateVisibility[currentRow] != lineStateEventsAreWakeVisible ||
                _liveRasterlinePlanWakeSearchCycles[currentRow] != currentCycle)
            {
                _liveRasterlinePlanWakeSearchIndices[currentRow] = count;
                _liveRasterlinePlanWakeSearchLineStateVisibility[currentRow] = lineStateEventsAreWakeVisible;
                _liveRasterlinePlanWakeSearchCycles[currentRow] = currentCycle;
            }

            return true;
        }

        private bool HasLiveLineStateWakeWork()
            => IsLiveBitplaneDmaEnabled() || IsSpriteDmaEnabled();

        private long GetNextLiveCpuVisibleWorkCycle()
        {
            var nextLineStateCycle = HasLiveLineStateWakeWork()
                ? GetNextLiveLineStateCycle()
                : long.MaxValue;
            var nextBitplaneFetchCycle = IsLiveBitplaneDmaEnabled()
                ? GetNextLiveBitplaneFetchCycle()
                : long.MaxValue;
            var nextSpriteFetchCycle = IsSpriteDmaEnabled()
                ? GetNextLiveSpriteFetchCycle()
                : long.MaxValue;
            return Math.Min(
                Math.Min(GetNextLiveDisplayEventCycle(), nextLineStateCycle),
                Math.Min(nextBitplaneFetchCycle, nextSpriteFetchCycle));
        }


    }
}
