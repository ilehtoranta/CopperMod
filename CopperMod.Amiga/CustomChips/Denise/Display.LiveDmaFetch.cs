/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        private void CaptureLiveLineState(int row, bool recordTimeline = true)
        {
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return;
            }

            if (IsLiveLineValid(row) &&
                (HasCapturedLiveBitplaneWords(row) || HasStartedLiveBitplaneFetches(row, _liveCycle)))
            {
                return;
            }

            AdvanceLiveDisplayWindowStateToLine(StandardVStart + row);
            var state = _liveLineStates[row];
            state.Generation = _liveGeneration;
            state.LineStartCycle = GetOutputRowStartCycle(_liveFrameStartCycle, row);
            state.DisplayWindowVerticallyOpen = _liveDisplayWindowVerticallyOpen;
            state.Bplcon0 = _bplcon0;
            state.Bplcon1 = _bplcon1;
            state.Bplcon2 = _bplcon2;
            state.DiwStart = _diwStart;
            state.DiwStop = _diwStop;
            state.DdfStart = _ddfStart;
            state.DdfStop = _ddfStop;
            state.Dmacon = _dmacon;
            state.Bpl1Mod = _bpl1mod;
            state.Bpl2Mod = _bpl2mod;
            state.PlaneCount = GetAgnusBitplaneFetchPlaneCount();
            state.DecodePlaneCount = GetDeniseBitplaneDecodePlaneCount();
            state.FetchWords = GetDataFetchWordCount();
            state.DataFetchStart = GetDataFetchStartValue();
            state.FetchSlotStride = GetBitplaneFetchSlotStride(IsHighResolutionEnabled());
            state.PaletteSnapshotIndex = CaptureLivePaletteSnapshot();
            Array.Copy(_bitplanePointers, state.BitplanePointers, _bitplanePointers.Length);
            Array.Copy(_bitplaneBaseRows, state.BitplaneBaseRows, _bitplaneBaseRows.Length);
            Array.Copy(_bitplaneDataRegisters, state.BitplaneDataRegisters, _bitplaneDataRegisters.Length);
            state.PlaneHasRowMask = 0;
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                if (IsLatchedOnlyOcsBpu7Plane(state.Bplcon0, plane))
                {
                    state.BitplaneRowAddresses[plane] = 0;
                    state.PlaneHasRowMask |= (byte)(1 << plane);
                    continue;
                }

                var displaySourceY = row - state.BitplaneBaseRows[plane];
                if (displaySourceY < 0)
                {
                    state.BitplaneRowAddresses[plane] = 0;
                    continue;
                }

                var mod = (plane & 1) == 0 ? state.Bpl1Mod : state.Bpl2Mod;
                var rowStride = (state.FetchWords * 2) + mod;
                state.BitplaneRowAddresses[plane] = unchecked(state.BitplanePointers[plane] + (uint)(displaySourceY * rowStride));
                state.PlaneHasRowMask |= (byte)(1 << plane);
            }

            ClearLiveBitplaneWordMasks(row);
            BuildRowDmaPlan(row, state);
            if (recordTimeline)
            {
                RecordTimelineLineStart(row, state);
            }
        }

        private void RefreshLiveLineStateAfterDisplayStateChange(long cycle)
        {
            if (!_liveFrameValid)
            {
                return;
            }

            var row = GetOutputRowForCycle(_liveFrameStartCycle, cycle);
            if ((uint)row >= (uint)LowResOutputHeight ||
                !IsLiveLineValid(row))
            {
                return;
            }

            if (HasCapturedLiveBitplaneWords(row) ||
                HasStartedLiveBitplaneFetches(row, cycle))
            {
                InvalidateRowDmaPlan(row);
                return;
            }

            CaptureLiveLineState(row, recordTimeline: !_displayTimeline.HasLine(row));
            if (_liveNextFetchRow >= row)
            {
                _liveNextFetchRow = row;
                _liveNextFetchWord = 0;
                _liveNextFetchPlane = 0;
                _liveNextFetchSlot = 0;
                InvalidateLiveWorkCycle();
            }

            if (_livePreparedFetchRow >= row)
            {
                _livePreparedFetchRow = row;
                _livePreparedFetchWord = 0;
                _livePreparedFetchPlane = 0;
                _livePreparedFetchSlot = 0;
                InvalidateLiveWorkCycle();
            }

            if (_liveNextSpriteRow >= row)
            {
                _liveNextSpriteRow = row;
                _liveNextSpriteIndex = 0;
                _liveNextSpriteWord = 0;
                InvalidateLiveWorkCycle();
            }
        }

        private void BuildRowDmaPlan(int row, LiveLineState state)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                state.Generation != _liveGeneration)
            {
                return;
            }

            var bitplaneStart = row * MaxRowDmaBitplaneEntriesPerRow;
            var bitplaneCount = 0;
            if (state.PlaneCount > 0 &&
                state.FetchWords > 0 &&
                state.DisplayWindowVerticallyOpen &&
                IsBitplaneDmaEnabled(state.Dmacon))
            {
                var planeCount = Math.Max(0, state.PlaneCount);
                for (var word = 0; word < state.FetchWords; word++)
                {
                    for (var slot = 0; slot < state.FetchSlotStride; slot++)
                    {
                        if (!TryGetBitplanePlaneForFetchSlot(slot, planeCount, state.FetchSlotStride, out var plane))
                        {
                            continue;
                        }

                        var fetchHorizontal = state.DataFetchStart + (word * state.FetchSlotStride) + slot;
                        var cycleOffset = fetchHorizontal * CopperHpCycles;
                        var rowPresent = (state.PlaneHasRowMask & (1 << plane)) != 0;
                        var address = rowPresent
                            ? unchecked(state.BitplaneRowAddresses[plane] + (uint)(word * 2))
                            : 0u;
                        _rowDmaBitplaneEntries[bitplaneStart + bitplaneCount++] =
                            new RowDmaBitplaneEntry(cycleOffset, plane, word, slot, address, rowPresent);
                    }
                }
            }

            var spriteStart = row * MaxRowDmaSpriteEntriesPerRow;
            var spriteCount = 0;
            if (IsSpriteDmaEnabled(state.Dmacon))
            {
                for (var spriteIndex = 0; spriteIndex < LiveSpriteChannelCount; spriteIndex++)
                {
                    for (var word = 0; word < LiveSpriteWordsPerChannel; word++)
                    {
                        var cycle = GetSpriteDmaFetchCycle(_liveFrameStartCycle, row, spriteIndex, word);
                        _rowDmaSpriteEntries[spriteStart + spriteCount++] =
                            new RowDmaSpriteEntry(cycle, spriteIndex, word);
                    }
                }
            }

            _rowDmaPlans[row] = new RowDmaPlan(
                _liveGeneration,
                row,
                ComputeRowDmaPlanSignature(state),
                bitplaneStart,
                bitplaneCount,
                spriteStart,
                spriteCount,
                valid: true);
            _rowDmaExecutedMasks[row] = 0;
            _lastRowDmaPlansBuilt++;
        }

        private void InvalidateRowDmaPlan(int row)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                !_rowDmaPlans[row].Valid)
            {
                return;
            }

            _rowDmaPlans[row] = default;
            _rowDmaExecutedMasks[row] = 0;
            _lastRowDmaPlanInvalidationRows++;
        }

        private bool TryGetValidRowDmaPlan(
            int row,
            LiveLineState state,
            out RowDmaPlan plan,
            bool recordFallback = true)
        {
            plan = default;
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return false;
            }

            plan = _rowDmaPlans[row];
            if (!plan.Valid)
            {
                if (recordFallback)
                {
                    _lastRowDmaScalarFallbackRows++;
                }

                return false;
            }

            if (plan.Generation != _liveGeneration ||
                plan.Row != row ||
                plan.Signature != ComputeRowDmaPlanSignature(state))
            {
                _rowDmaPlans[row] = default;
                _lastRowDmaPlanMismatchRows++;
                if (recordFallback)
                {
                    _lastRowDmaScalarFallbackRows++;
                }

                return false;
            }

            return true;
        }

        private void RecordRowDmaPlanExecuted(int row, byte mask)
        {
            if ((uint)row >= (uint)_rowDmaExecutedMasks.Length)
            {
                return;
            }

            var previous = _rowDmaExecutedMasks[row];
            if (previous == 0)
            {
                _lastRowDmaPlannedRowsExecuted++;
            }

            _rowDmaExecutedMasks[row] = (byte)(previous | mask);
        }

        private static int ComputeRowDmaPlanSignature(LiveLineState state)
        {
            unchecked
            {
                var hash = 17;
                hash = AddRowDmaPlanSignature(hash, state.Generation);
                hash = AddRowDmaPlanSignature(hash, (int)state.LineStartCycle);
                hash = AddRowDmaPlanSignature(hash, (int)(state.LineStartCycle >> 32));
                hash = AddRowDmaPlanSignature(hash, state.Bplcon0);
                hash = AddRowDmaPlanSignature(hash, state.Bplcon1);
                hash = AddRowDmaPlanSignature(hash, state.Bplcon2);
                hash = AddRowDmaPlanSignature(hash, state.DdfStart);
                hash = AddRowDmaPlanSignature(hash, state.DdfStop);
                hash = AddRowDmaPlanSignature(hash, state.Dmacon);
                hash = AddRowDmaPlanSignature(hash, state.Bpl1Mod);
                hash = AddRowDmaPlanSignature(hash, state.Bpl2Mod);
                hash = AddRowDmaPlanSignature(hash, state.PlaneCount);
                hash = AddRowDmaPlanSignature(hash, state.FetchWords);
                hash = AddRowDmaPlanSignature(hash, state.DataFetchStart);
                hash = AddRowDmaPlanSignature(hash, state.FetchSlotStride);
                hash = AddRowDmaPlanSignature(hash, state.PlaneHasRowMask);
                for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
                {
                    hash = AddRowDmaPlanSignature(hash, (int)state.BitplaneRowAddresses[plane]);
                }

                return hash;
            }
        }

        private static int AddRowDmaPlanSignature(int hash, int value)
            => unchecked((hash * 397) ^ value);

        private bool HasStartedLiveBitplaneFetches(int row, long cycle)
        {
            var state = _liveLineStates[row];
            if (state.PlaneCount <= 0 ||
                state.FetchWords <= 0 ||
                !state.DisplayWindowVerticallyOpen ||
                !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return false;
            }

            return GetFirstLiveBitplaneFetchCycleForRendering(row, state) <= cycle;
        }

        private bool HasCapturedLiveBitplaneWords(int row)
        {
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return false;
            }

            var offset = row * LiveBitplanePlaneCount;
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                if (_liveBitplaneWordMasks[offset + plane] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private int CaptureLivePaletteSnapshot()
        {
            if (!_livePaletteSnapshotDirty && _liveCurrentPaletteSnapshotIndex >= 0)
            {
                return _liveCurrentPaletteSnapshotIndex;
            }

            if (_livePaletteSnapshotCount >= MaxLivePaletteSnapshots)
            {
                return _liveCurrentPaletteSnapshotIndex >= 0 ? _liveCurrentPaletteSnapshotIndex : 0;
            }

            var index = _livePaletteSnapshotCount++;
            Array.Copy(_colors, 0, _livePaletteSnapshotColors, index * _colors.Length, _colors.Length);
            Array.Copy(_convertedColors, 0, _livePaletteSnapshotConvertedColors, index * PaletteColorCount, PaletteColorCount);
            _liveCurrentPaletteSnapshotIndex = index;
            _livePaletteSnapshotDirty = false;
            return index;
        }

        private void CaptureLiveBitplaneFetchBatch(long stopCycle)
        {
            while (_liveNextFetchRow < LowResOutputHeight)
            {
                if (!NormalizeLiveBitplaneFetchCursor())
                {
                    return;
                }

                if (TryCaptureLiveBitplaneFetchBatchWithRowPlan(stopCycle, out var stoppedBeforeStop))
                {
                    if (stoppedBeforeStop)
                    {
                        return;
                    }

                    continue;
                }

                var row = _liveNextFetchRow;
                var state = _liveLineStates[row];
                var planeCount = Math.Max(0, state.PlaneCount);
                var fetchWords = state.FetchWords;
                var fetchSlotStride = state.FetchSlotStride;
                var dataFetchStart = state.DataFetchStart;
                var lineStartCycle = state.LineStartCycle;
                var word = _liveNextFetchWord;
                var slot = _liveNextFetchSlot;
                var advanced = false;

                while (word < fetchWords)
                {
                    while (slot < fetchSlotStride)
                    {
                        if (!TryGetBitplanePlaneForFetchSlot(slot, planeCount, fetchSlotStride, out var plane))
                        {
                            slot++;
                            continue;
                        }

                        var fetchHorizontal = dataFetchStart + (word * fetchSlotStride) + slot;
                        var fetchCycle = lineStartCycle + ((long)fetchHorizontal * CopperHpCycles);
                        if (fetchCycle > stopCycle)
                        {
                            _liveNextFetchRow = row;
                            _liveNextFetchWord = word;
                            _liveNextFetchPlane = plane;
                            _liveNextFetchSlot = slot;
                            if (advanced)
                            {
                                InvalidateLiveWorkCycle();
                            }

                            return;
                        }

                        CaptureLiveBitplaneFetch(row, plane, word, fetchCycle, state);
                        slot++;
                        advanced = true;
                    }

                    slot = 0;
                    word++;
                }

                _liveNextFetchRow = row;
                _liveNextFetchWord = word;
                _liveNextFetchPlane = 0;
                _liveNextFetchSlot = slot;
                AdvanceLiveFetchToNextRow(advanceBitplanePointers: true);
            }
        }

        private bool TryCaptureLiveBitplaneFetchBatchWithRowPlan(long stopCycle, out bool stoppedBeforeStop)
            => TryCaptureLiveBitplaneFetchBatchWithRowPlan(stopCycle, out stoppedBeforeStop, out _);

        private bool TryCaptureLiveBitplaneFetchBatchWithRowPlan(
            long stopCycle,
            out bool stoppedBeforeStop,
            out bool capturedAny)
        {
            stoppedBeforeStop = false;
            capturedAny = false;
            var row = _liveNextFetchRow;
            if ((uint)row >= (uint)LowResOutputHeight ||
                !IsLiveLineValid(row))
            {
                return false;
            }

            var state = _liveLineStates[row];
            if (!TryGetValidRowDmaPlan(row, state, out var plan) ||
                plan.BitplaneCount <= 0)
            {
                return false;
            }

            if (!TryFindNextRowDmaBitplaneEntry(plan, _liveNextFetchWord, _liveNextFetchSlot, out var entryIndex))
            {
                _lastRowDmaScalarFallbackRows++;
                return false;
            }

            ExecuteRowDmaBitplaneBatch(
                row,
                state,
                plan,
                entryIndex,
                stopCycle,
                out stoppedBeforeStop,
                out capturedAny);
            if (stoppedBeforeStop)
            {
                return true;
            }

            return true;
        }

        private void ExecuteRowDmaBitplaneBatch(
            int row,
            LiveLineState state,
            RowDmaPlan plan,
            int entryIndex,
            long stopCycle,
            out bool stoppedBeforeStop,
            out bool capturedAny)
        {
            stoppedBeforeStop = false;
            capturedAny = false;
            var batchStart = entryIndex;
            var batchCount = 0;
            var end = plan.BitplaneStart + plan.BitplaneCount;
            for (var index = entryIndex; index < end; index++)
            {
                var entry = _rowDmaBitplaneEntries[index];
                if (entry.GetCycle(state.LineStartCycle) > stopCycle)
                {
                    _liveNextFetchRow = row;
                    _liveNextFetchWord = entry.Word;
                    _liveNextFetchPlane = entry.Plane;
                    _liveNextFetchSlot = entry.Slot;
                    stoppedBeforeStop = true;
                    break;
                }

                batchCount++;
            }

            if (batchCount > 0)
            {
                var firstEntry = _rowDmaBitplaneEntries[batchStart];
                var grantedCount = 0;
                var firstGrantedCycle = -1L;
                var lastGrantedCycle = -1L;
                var previousAuditSource = _bus.PushSlotScheduleAuditSource(
                    AgnusSlotAuditSource.RowBitplanePresentation,
                    row,
                    firstEntry.Word,
                    firstEntry.Slot);
                try
                {
                    _bus.ReadRowBitplaneDmaFetchesForPresentation(
                        _rowDmaBitplaneEntries.AsSpan(batchStart, batchCount),
                        state.LineStartCycle,
                        _rowDmaBitplaneBatchValues.AsSpan(0, batchCount),
                        _rowDmaBitplaneBatchGranted.AsSpan(0, batchCount),
                        out grantedCount,
                        out firstGrantedCycle,
                        out lastGrantedCycle);
                }
                finally
                {
                    _bus.RestoreSlotScheduleAuditSource(previousAuditSource);
                }

                ConsumeRowDmaBitplaneBatch(
                    row,
                    batchStart,
                    batchCount,
                    grantedCount,
                    firstGrantedCycle,
                    lastGrantedCycle);
                _lastRowDmaBitplaneEntriesExecuted += batchCount;
                capturedAny = true;
            }

            if (stoppedBeforeStop)
            {
                if (capturedAny)
                {
                    InvalidateLiveWorkCycle();
                }

                return;
            }

            _liveNextFetchRow = row;
            _liveNextFetchWord = state.FetchWords;
            _liveNextFetchPlane = 0;
            _liveNextFetchSlot = 0;
            AdvanceLiveFetchToNextRow(advanceBitplanePointers: true);
            RecordRowDmaPlanExecuted(row, RowDmaExecutedBitplaneMask);
        }

        private void ConsumeRowDmaBitplaneBatch(
            int row,
            int entryStart,
            int count,
            int grantedCount,
            long firstGrantedCycle,
            long lastGrantedCycle)
        {
            var liveWordBase = row * LiveBitplaneWordsPerRow;
            var liveMaskBase = row * LiveBitplanePlaneCount;
            var timelineLine = _displayTimeline.GetLine(row);
            var recordTimeline = !_liveTimelineUnsafeForFrame &&
                _displayTimeline.TryGetBitplaneFetchLine(row, out timelineLine);
            var allGranted = grantedCount == count;
            for (var offset = 0; offset < count; offset++)
            {
                var entry = _rowDmaBitplaneEntries[entryStart + offset];
                var value = _rowDmaBitplaneBatchValues[offset];
                _liveBitplaneWords[liveWordBase + (entry.Plane * MaxBitplaneFetchWords) + entry.Word] = value;
                _liveBitplaneWordMasks[liveMaskBase + entry.Plane] |= 1UL << entry.Word;
                if (recordTimeline)
                {
                    var bit = 1UL << entry.Word;
                    var index = (entry.Plane * MaxBitplaneFetchWords) + entry.Word;
                    timelineLine.BitplaneWords[index] = value;
                    timelineLine.BitplaneFetchMasks[entry.Plane] |= bit;
                    if (allGranted || _rowDmaBitplaneBatchGranted[offset])
                    {
                        timelineLine.BitplaneDeniedMasks[entry.Plane] &= ~bit;
                    }
                    else
                    {
                        timelineLine.BitplaneDeniedMasks[entry.Plane] |= bit;
                    }
                }
            }

            if (grantedCount > 0)
            {
                _liveBitplaneDmaFetches += grantedCount;
                RecordLiveDisplayDmaCycleRange(firstGrantedCycle, lastGrantedCycle);
            }

            _liveFetchBatchWordCount += count;
            _bitplaneDmaReadLatch = default;
        }

        private bool TryFindNextRowDmaBitplaneEntry(
            RowDmaPlan plan,
            int word,
            int slot,
            out int entryIndex)
        {
            var end = plan.BitplaneStart + plan.BitplaneCount;
            for (var index = plan.BitplaneStart; index < end; index++)
            {
                var entry = _rowDmaBitplaneEntries[index];
                if (entry.Word > word ||
                    entry.Word == word && entry.Slot >= slot)
                {
                    entryIndex = index;
                    return true;
                }
            }

            entryIndex = -1;
            return false;
        }

        private void CaptureLiveSpriteFetchBatch(long stopCycle)
        {
            while (_liveNextSpriteRow < LowResOutputHeight)
            {
                if (TryCaptureLiveSpriteFetchBatchWithRowPlan(stopCycle, out var stoppedBeforeStop))
                {
                    if (stoppedBeforeStop)
                    {
                        return;
                    }

                    continue;
                }

                SkipLiveSpriteSlotsWithoutFetches();
                if (_liveNextSpriteRow >= LowResOutputHeight ||
                    !IsLiveLineValid(_liveNextSpriteRow) ||
                    !IsSpriteDmaEnabled())
                {
                    return;
                }

                var fetchCycle = GetNextLiveSpriteFetchCycle();
                if (fetchCycle > stopCycle)
                {
                    return;
                }

                _ = TryCaptureKnownLiveSpriteDmaSlot(
                    _liveNextSpriteRow,
                    _liveNextSpriteIndex,
                    _liveNextSpriteWord,
                    fetchCycle);
                AdvanceLiveSpriteFetchCursor();
            }
        }

        private bool TryCaptureLiveSpriteFetchBatchWithRowPlan(long stopCycle, out bool stoppedBeforeStop)
            => TryCaptureLiveSpriteFetchBatchWithRowPlan(stopCycle, out stoppedBeforeStop, out _);

        private bool TryCaptureLiveSpriteFetchBatchWithRowPlan(
            long stopCycle,
            out bool stoppedBeforeStop,
            out bool capturedAny)
        {
            stoppedBeforeStop = false;
            capturedAny = false;
            SkipLiveSpriteSlotsWithoutFetches();
            var row = _liveNextSpriteRow;
            if ((uint)row >= (uint)LowResOutputHeight ||
                !IsLiveLineValid(row) ||
                !IsSpriteDmaEnabled())
            {
                return false;
            }

            var state = _liveLineStates[row];
            if (!TryGetValidRowDmaPlan(row, state, out var plan) ||
                plan.SpriteCount <= 0)
            {
                return false;
            }

            while (_liveNextSpriteRow == row)
            {
                if (!TryFindNextRowDmaSpriteEntry(plan, _liveNextSpriteIndex, _liveNextSpriteWord, out var entryIndex))
                {
                    _lastRowDmaScalarFallbackRows++;
                    return false;
                }

                var entry = _rowDmaSpriteEntries[entryIndex];
                if (entry.Cycle > stopCycle)
                {
                    _liveNextSpriteRow = row;
                    _liveNextSpriteIndex = entry.SpriteIndex;
                    _liveNextSpriteWord = entry.Word;
                    if (capturedAny)
                    {
                        InvalidateLiveWorkCycle();
                    }

                    stoppedBeforeStop = true;
                    return true;
                }

                _liveNextSpriteRow = row;
                _liveNextSpriteIndex = entry.SpriteIndex;
                _liveNextSpriteWord = entry.Word;
                _ = TryCaptureKnownLiveSpriteDmaSlot(row, entry.SpriteIndex, entry.Word, entry.Cycle);
                _lastRowDmaSpriteEntriesExecuted++;
                capturedAny = true;
                AdvanceLiveSpriteFetchCursor();
                SkipLiveSpriteSlotsWithoutFetches();

                if (_liveNextSpriteRow > row)
                {
                    RecordRowDmaPlanExecuted(row, RowDmaExecutedSpriteMask);
                    return true;
                }

                if (_liveNextSpriteRow < row ||
                    !IsLiveLineValid(_liveNextSpriteRow) ||
                    !IsSpriteDmaEnabled())
                {
                    return true;
                }
            }

            if (capturedAny)
            {
                RecordRowDmaPlanExecuted(row, RowDmaExecutedSpriteMask);
                return true;
            }

            return false;
        }

        private bool TryFindNextRowDmaSpriteEntry(
            RowDmaPlan plan,
            int spriteIndex,
            int word,
            out int entryIndex)
        {
            var end = plan.SpriteStart + plan.SpriteCount;
            for (var index = plan.SpriteStart; index < end; index++)
            {
                var entry = _rowDmaSpriteEntries[index];
                if (entry.SpriteIndex > spriteIndex ||
                    entry.SpriteIndex == spriteIndex && entry.Word >= word)
                {
                    entryIndex = index;
                    return true;
                }
            }

            entryIndex = -1;
            return false;
        }

        private long GetNextKnownLiveBitplaneFetchCycle()
        {
            SkipLiveRowsWithoutFetches();
            if (_liveNextFetchRow >= LowResOutputHeight ||
                !IsLiveLineValid(_liveNextFetchRow))
            {
                return long.MaxValue;
            }

            return GetNextLiveBitplaneFetchCycle();
        }

        private long GetNextPreparedLiveBitplaneFetchCycle()
        {
            if (!NormalizePreparedLiveBitplaneFetchCursor())
            {
                return long.MaxValue;
            }

            var state = _liveLineStates[_livePreparedFetchRow];
            var fetchHorizontal = state.DataFetchStart + (_livePreparedFetchWord * state.FetchSlotStride) + _livePreparedFetchSlot;
            return AgnusChipSlotScheduler.AlignToSlot(state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
        }

        private bool NormalizePreparedLiveBitplaneFetchCursor()
        {
            while (_livePreparedFetchRow < LowResOutputHeight)
            {
                var state = _liveLineStates[_livePreparedFetchRow];
                if (!IsLiveLineValid(_livePreparedFetchRow))
                {
                    return false;
                }

                var planeCount = Math.Max(0, state.PlaneCount);
                if (planeCount <= 0 ||
                    state.FetchWords <= 0 ||
                    !state.DisplayWindowVerticallyOpen ||
                    !IsBitplaneDmaEnabled(state.Dmacon))
                {
                    AdvancePreparedLiveFetchToNextRow();
                    continue;
                }

                while (_livePreparedFetchWord < state.FetchWords)
                {
                    while (_livePreparedFetchSlot < state.FetchSlotStride)
                    {
                        if (TryGetBitplanePlaneForFetchSlot(_livePreparedFetchSlot, planeCount, state.FetchSlotStride, out var plane))
                        {
                            _livePreparedFetchPlane = plane;
                            return true;
                        }

                        _livePreparedFetchSlot++;
                    }

                    _livePreparedFetchSlot = 0;
                    _livePreparedFetchWord++;
                }

                AdvancePreparedLiveFetchToNextRow();
            }

            return false;
        }

        private void PrepareKnownLiveBitplaneSlotsThrough(long targetCycle)
        {
            var previousAuditSource = _bus.PushSlotScheduleAuditSource(AgnusSlotAuditSource.PreparedBitplaneSlot);
            try
            {
                while (_livePreparedFetchRow < LowResOutputHeight)
                {
                    if (!NormalizePreparedLiveBitplaneFetchCursor())
                    {
                        return;
                    }

                    var state = _liveLineStates[_livePreparedFetchRow];
                    var fetchCycle = GetNextPreparedLiveBitplaneFetchCycle();
                    if (fetchCycle > targetCycle)
                    {
                        return;
                    }

                    if ((state.PlaneHasRowMask & (1 << _livePreparedFetchPlane)) != 0)
                    {
                        var address = unchecked(state.BitplaneRowAddresses[_livePreparedFetchPlane] + (uint)(_livePreparedFetchWord * 2));
                        if (TryGetValidRowDmaPlan(
                                _livePreparedFetchRow,
                                state,
                                out var plan,
                                recordFallback: false) &&
                            TryFindExactRowDmaBitplaneEntry(
                                plan,
                                _livePreparedFetchWord,
                                _livePreparedFetchSlot,
                                out var entry) &&
                            entry.RowPresent)
                        {
                            _ = _bus.TryReserveRowBitplaneDmaSlot(entry.Address, entry.GetCycle(state.LineStartCycle), out _);
                        }
                        else
                        {
                            _ = _bus.TryReserveRowBitplaneDmaSlot(address, fetchCycle, out _);
                        }
                    }

                    AdvancePreparedLiveFetchCursor();
                }
            }
            finally
            {
                _bus.RestoreSlotScheduleAuditSource(previousAuditSource);
            }
        }

        private bool TryFindExactRowDmaBitplaneEntry(
            RowDmaPlan plan,
            int word,
            int slot,
            out RowDmaBitplaneEntry entry)
        {
            var end = plan.BitplaneStart + plan.BitplaneCount;
            for (var index = plan.BitplaneStart; index < end; index++)
            {
                var candidate = _rowDmaBitplaneEntries[index];
                if (candidate.Word == word && candidate.Slot == slot)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private void AdvancePreparedLiveFetchCursor()
        {
            if (_livePreparedFetchRow >= LowResOutputHeight)
            {
                return;
            }

            var state = _liveLineStates[_livePreparedFetchRow];
            _livePreparedFetchSlot++;
            if (_livePreparedFetchSlot < state.FetchSlotStride)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            _livePreparedFetchSlot = 0;
            _livePreparedFetchPlane = 0;
            _livePreparedFetchWord++;
            if (_livePreparedFetchWord < state.FetchWords)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            AdvancePreparedLiveFetchToNextRow();
        }

        private void AdvancePreparedLiveFetchToNextRow()
        {
            _livePreparedFetchRow++;
            _livePreparedFetchWord = 0;
            _livePreparedFetchPlane = 0;
            _livePreparedFetchSlot = 0;
            InvalidateLiveWorkCycle();
        }

        private bool CaptureKnownLiveBitplaneFetchesThrough(long targetCycle)
        {
            var captured = false;
            while (_liveNextFetchRow < LowResOutputHeight)
            {
                SkipLiveRowsWithoutFetches();
                if (_liveNextFetchRow >= LowResOutputHeight ||
                    !IsLiveLineValid(_liveNextFetchRow))
                {
                    return captured;
                }

                var fetchCycle = GetNextLiveBitplaneFetchCycle();
                if (fetchCycle > targetCycle)
                {
                    return captured;
                }

                CaptureLiveBitplaneFetch(fetchCycle);
                AdvanceLiveFetchCursor();
                captured = true;
            }

            return captured;
        }

        private void CaptureLiveBitplaneFetch(int row, int plane, int word, long fetchCycle, LiveLineState state)
        {
            BitplaneDmaReadLatch latch;
            if ((state.PlaneHasRowMask & (1 << plane)) != 0)
            {
                var address = unchecked(state.BitplaneRowAddresses[plane] + (uint)(word * 2));
                latch = LoadLiveBitplaneDmaLatch(row, plane, word, address, fetchCycle);
            }
            else
            {
                latch = BitplaneDmaReadLatch.Denied(row, plane, word, fetchCycle);
            }

            _bitplaneDmaReadLatch = latch;
            ConsumeLiveBitplaneDmaLatch(ref _bitplaneDmaReadLatch);
        }

        private void CaptureLiveBitplaneFetch(int row, RowDmaBitplaneEntry entry)
        {
            var cycle = entry.GetCycle(_liveLineStates[row].LineStartCycle);
            _bitplaneDmaReadLatch = entry.RowPresent
                ? LoadLiveBitplaneDmaLatch(row, entry.Plane, entry.Word, entry.Address, cycle)
                : BitplaneDmaReadLatch.Denied(row, entry.Plane, entry.Word, cycle);
            ConsumeLiveBitplaneDmaLatch(ref _bitplaneDmaReadLatch);
        }

        private void CaptureLiveBitplaneFetch(long fetchCycle)
        {
            if ((uint)_liveNextFetchRow >= (uint)LowResOutputHeight ||
                (uint)_liveNextFetchPlane >= (uint)_bitplanePointers.Length ||
                (uint)_liveNextFetchWord >= (uint)MaxBitplaneFetchWords)
            {
                return;
            }

            var state = _liveLineStates[_liveNextFetchRow];
            if (!IsLiveLineValid(_liveNextFetchRow))
            {
                return;
            }

            CaptureLiveBitplaneFetch(_liveNextFetchRow, _liveNextFetchPlane, _liveNextFetchWord, fetchCycle, state);
        }

        private void RecordLiveDisplayDmaCycle(long cycle)
        {
            if (_liveFirstDisplayDmaCycle < 0 || cycle < _liveFirstDisplayDmaCycle)
            {
                _liveFirstDisplayDmaCycle = cycle;
            }

            if (_liveLastDisplayDmaCycle < 0 || cycle > _liveLastDisplayDmaCycle)
            {
                _liveLastDisplayDmaCycle = cycle;
            }
        }

        private void RecordLiveDisplayDmaCycleRange(long firstCycle, long lastCycle)
        {
            if (firstCycle < 0)
            {
                return;
            }

            if (_liveFirstDisplayDmaCycle < 0 || firstCycle < _liveFirstDisplayDmaCycle)
            {
                _liveFirstDisplayDmaCycle = firstCycle;
            }

            if (_liveLastDisplayDmaCycle < 0 || lastCycle > _liveLastDisplayDmaCycle)
            {
                _liveLastDisplayDmaCycle = lastCycle;
            }
        }

        private void AdvanceLiveFetchCursor()
        {
            if (_liveNextFetchRow >= LowResOutputHeight)
            {
                return;
            }

            var state = _liveLineStates[_liveNextFetchRow];
            _liveNextFetchSlot++;
            if (_liveNextFetchSlot < state.FetchSlotStride)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            _liveNextFetchSlot = 0;
            _liveNextFetchPlane = 0;
            _liveNextFetchWord++;
            if (_liveNextFetchWord < state.FetchWords)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            AdvanceLiveFetchToNextRow(advanceBitplanePointers: true);
        }

        private void AdvanceLiveFetchToNextRow(bool advanceBitplanePointers)
        {
            if (advanceBitplanePointers)
            {
                AdvanceLiveBitplanePointersPastCapturedRow(_liveNextFetchRow);
            }

            _liveNextFetchRow++;
            _liveNextFetchWord = 0;
            _liveNextFetchPlane = 0;
            _liveNextFetchSlot = 0;
            InvalidateLiveWorkCycle();
        }

        private void AdvanceLiveBitplanePointersPastCapturedRow(int row)
        {
            if ((uint)row >= (uint)LowResOutputHeight || !IsLiveLineValid(row))
            {
                return;
            }

            var state = _liveLineStates[row];
            if (!IsBitplaneDmaEnabled(state.Dmacon) || state.FetchWords <= 0)
            {
                return;
            }

            var planeCount = Math.Clamp(state.PlaneCount, 0, _bitplanePointers.Length);
            for (var plane = 0; plane < planeCount; plane++)
            {
                if ((state.PlaneHasRowMask & (1 << plane)) == 0)
                {
                    continue;
                }

                if (_bitplanePointers[plane] != state.BitplanePointers[plane] ||
                    _bitplaneBaseRows[plane] != state.BitplaneBaseRows[plane])
                {
                    continue;
                }

                var mod = (plane & 1) == 0 ? state.Bpl1Mod : state.Bpl2Mod;
                var rowStride = (state.FetchWords * 2) + mod;
                _bitplanePointers[plane] = AddDmaPointerOffset(state.BitplaneRowAddresses[plane], rowStride);
                _bitplaneBaseRows[plane] = row + 1;
            }
        }

        private void AdvanceLiveSpriteFetchCursor()
        {
            if (_liveNextSpriteRow >= LowResOutputHeight)
            {
                return;
            }

            _liveNextSpriteWord++;
            if (_liveNextSpriteWord < 2)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            _liveNextSpriteWord = 0;
            _liveNextSpriteIndex++;
            if (_liveNextSpriteIndex < _sprites.Length)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            _liveNextSpriteIndex = 0;
            _liveNextSpriteRow++;
            InvalidateLiveWorkCycle();
        }


    }
}
