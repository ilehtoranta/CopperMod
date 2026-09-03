/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Diagnostics;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        private void CaptureLiveLineState(int row, bool recordTimeline = true)
        {
            if (!_hostProfilingEnabled)
            {
                CaptureLiveLineStateCore(row, recordTimeline);
                return;
            }

            var start = Stopwatch.GetTimestamp();
            CaptureLiveLineStateCore(row, recordTimeline);
            _hostLineCaptureTicks += Stopwatch.GetTimestamp() - start;
            _hostLineCaptureCalls++;
        }

        private void CaptureLiveLineStateCore(int row, bool recordTimeline)
        {
            if ((uint)row >= (uint)_liveHardwareRowCount)
            {
                return;
            }

            // A valid entry that already owns fetched data cannot be reused by
            // this call. Avoid running presentation finalization for the very
            // common repeated lookup of the active line.
            if (IsLiveLineValid(row) &&
                (HasCapturedLiveBitplaneWords(row) || HasStartedLiveBitplaneFetches(row, _liveCycle)))
            {
                return;
            }

            if (!_presentationIndependentDisplayLedgerEnabled)
            {
                // The legacy three-line capture ring must finalize older output
                // before this row can reuse its slot. G1L retains the complete
                // frame and lets presentation consume it after arbitration.
                RenderBoundPresentationLinesThrough(
                    GetOutputRowStartCycle(_liveFrameStartCycle, row),
                    completing: false,
                    minimumRenderStop: Math.Max(0, row - RasterlineRingSize + 1));
            }

            AdvanceLiveDisplayWindowStateToLine(StandardVStart + row);
            var state = GetLiveLineState(row);
            state.Generation = _liveGeneration;
            state.LineStartCycle = GetOutputRowStartCycle(_liveFrameStartCycle, row);
            state.DisplayWindowVerticallyOpen = _liveDisplayWindowVerticallyOpen;
            state.DeniseDisplayWindowVerticallyOpen = _liveDeniseDisplayWindowVerticallyOpen;
            state.Resolution = GetDeniseResolution(_bplcon0);
            state.FetchResolution = _dataFetchWindow.Resolution;
            state.Bplcon0 = _bplcon0;
            state.Bplcon1 = _bplcon1;
            state.Bplcon2 = _bplcon2;
            state.Bplcon3 = _bplcon3;
            state.DiwStart = _diwStart;
            state.DiwStop = _diwStop;
            state.DiwHigh = _diwHigh;
            state.DiwHighValid = _diwHighValid;
            state.AgnusDiwHigh = _agnusDiwHigh;
            state.AgnusDiwHighValid = _agnusDiwHighValid;
            state.AgnusDisplayWindow = _agnusDisplayWindow;
            state.DeniseDisplayWindow = _deniseDisplayWindow;
            state.DdfStart = _ddfStart;
            state.DdfStop = _ddfStop;
            state.DataFetchWindow = _dataFetchWindow;
            state.Dmacon = _dmacon;
            state.Bpl1Mod = _bpl1mod;
            state.Bpl2Mod = _bpl2mod;
            _bus.CausalBusExecutor.CompareDisplayLineControlState(
                _liveCycle,
                state.Dmacon,
                state.DiwStart,
                state.DiwStop,
                state.DiwHigh,
                state.DdfStart,
                state.DdfStop,
                state.Bplcon0,
                state.Bplcon1,
                state.Bplcon2,
                state.Bplcon3,
                state.Bpl1Mod,
                state.Bpl2Mod);
            state.PlaneCount = GetAgnusBitplaneFetchPlaneCount();
            state.DecodePlaneCount = GetDeniseBitplaneDecodePlaneCount();
            state.FetchWords = GetDataFetchWordCount();
            state.DataFetchStart = _dataFetchWindow.Start;
            state.FetchSlotStride = DisplayGeometryDecoder.GetDataFetchSlotStride(_dataFetchWindow);
            state.BitplaneInputSuppressedThroughCycle = long.MinValue;
            state.PaletteSnapshotIndex = row < LowResOutputHeight
                ? CaptureLivePaletteSnapshot(row)
                : -1;
            state.PlaneHasRowMask = 0;
            if (state.PlaneCount > 0 || state.DecodePlaneCount > 0)
            {
                // Plane-local state is presentation payload, not Agnus scheduling
                // state.  Do not copy it for border-only lines.  This is the common
                // case while ROM/host boot code owns the CPU and previously caused
                // five small array helpers to run at every raster boundary.
                for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
                {
                    state.BitplanePointers[plane] = _bitplanePointers[plane];
                    state.BitplaneBaseRows[plane] = _bitplaneBaseRows[plane];
                    state.BitplaneDataRegisters[plane] = _bitplaneDataRegisters[plane];
                    state.BitplaneWordIndexOffsets[plane] = 0;

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
                    state.BitplaneRowAddresses[plane] = AddDmaPointerOffset(
                        state.BitplanePointers[plane],
                        displaySourceY * rowStride);
                    state.PlaneHasRowMask |= (byte)(1 << plane);
                }

                _bus.CausalBusExecutor.CompareBitplanePointers(_liveCycle, state.BitplanePointers);
            }

            AdvanceRowDmaPlanVersion(state);
            InvalidateRowDmaPlan(row);

            ClearLiveBitplaneWordMasks(row);
            Array.Clear(
                _liveSpriteWordMasks,
                GetLiveCaptureSlot(row) * LiveSpriteChannelCount,
                LiveSpriteChannelCount);
            if (recordTimeline && row < LowResOutputHeight && !_displayTimeline.HasLine(row))
            {
                RecordTimelineLineStart(row, state);
            }
        }

        private void RefreshLiveLineStateAfterDisplayStateChange(long cycle, ushort offset)
        {
            if (!_liveFrameValid)
            {
                return;
            }

            var row = GetOutputRowForCycle(_liveFrameStartCycle, cycle);
            if ((uint)row >= (uint)_liveHardwareRowCount ||
                !IsLiveLineValid(row))
            {
                return;
            }

            offset &= 0x01FE;
            if (UsesPhysicalBitplanePipeline && offset is >= 0x00E0 and <= 0x00FE)
            {
                RebaseFuturePhysicalBitplanePointer(row, (offset - 0x00E0) / 4, cycle);
                return;
            }

            if (HasCapturedLiveBitplaneWords(row) ||
                HasStartedLiveBitplaneFetches(row, cycle))
            {
                offset &= 0x01FE;
                if (offset == 0x0100)
                {
                    // BPLCON0 changes the live plane count and fetch/decode mode.
                    // Reconcile only this explicitly causal within-line control.
                    RefreshStartedLiveLineDmaState(row, cycle);
                    return;
                }

                // This object is now the immutable presentation baseline and the
                // ledger of already committed fetches. Future register state is
                // observed when the invalidated remainder is rebuilt.
                InvalidateRowDmaPlan(row);
                return;
            }

            // The rasterline already owns its ring entry.  Re-capturing it here
            // used to treat every pre-fetch register write as a new line: all
            // eight-plane arrays were copied and the capture masks were cleared
            // from the CPU/custom-register hot path.  Patch the unexecuted state
            // in place instead.  The started-line helper also rebases both DMA
            // cursors and invalidates only the plan suffix affected by the write.
            RefreshStartedLiveLineDmaState(row, cycle);

            if (_liveNextSpriteRow >= row)
            {
                _liveNextSpriteRow = row;
                _liveNextSpriteIndex = 0;
                _liveNextSpriteWord = 0;
                AdvanceLiveSpriteFetchCursorToCycle(cycle);
            }
        }

        private void RefreshStartedLiveLineDmaState(int row, long cycle)
        {
            var state = GetLiveLineState(row);
            var oldPlaneCount = state.PlaneCount;
            state.DisplayWindowVerticallyOpen = _liveDisplayWindowVerticallyOpen;
            state.DeniseDisplayWindowVerticallyOpen = _liveDeniseDisplayWindowVerticallyOpen;
            state.Resolution = GetDeniseResolution(_bplcon0);
            state.FetchResolution = _dataFetchWindow.Resolution;
            state.Bplcon0 = _bplcon0;
            state.Bplcon1 = _bplcon1;
            state.Bplcon2 = _bplcon2;
            state.Bplcon3 = _bplcon3;
            state.DiwStart = _diwStart;
            state.DiwStop = _diwStop;
            state.DiwHigh = _diwHigh;
            state.DiwHighValid = _diwHighValid;
            state.AgnusDiwHigh = _agnusDiwHigh;
            state.AgnusDiwHighValid = _agnusDiwHighValid;
            state.AgnusDisplayWindow = _agnusDisplayWindow;
            state.DeniseDisplayWindow = _deniseDisplayWindow;
            state.DdfStart = _ddfStart;
            state.DdfStop = _ddfStop;
            state.DataFetchWindow = _dataFetchWindow;
            state.Dmacon = _dmacon;
            state.Bpl1Mod = _bpl1mod;
            state.Bpl2Mod = _bpl2mod;
            _bus.CausalBusExecutor.CompareDisplayLineControlState(
                cycle,
                state.Dmacon,
                state.DiwStart,
                state.DiwStop,
                state.DiwHigh,
                state.DdfStart,
                state.DdfStop,
                state.Bplcon0,
                state.Bplcon1,
                state.Bplcon2,
                state.Bplcon3,
                state.Bpl1Mod,
                state.Bpl2Mod);
            state.PlaneCount = GetAgnusBitplaneFetchPlaneCount();
            state.DecodePlaneCount = GetDeniseBitplaneDecodePlaneCount();
            var transitionCutoffCycle = GetBitplaneCursorCutoffAfterPlaneCountChange(
                cycle,
                oldPlaneCount,
                state.PlaneCount);
            if (transitionCutoffCycle > cycle)
            {
                state.BitplaneInputSuppressedThroughCycle = Math.Max(
                    state.BitplaneInputSuppressedThroughCycle,
                    transitionCutoffCycle);
            }
            var bitplaneCursorCutoffCycle = Math.Max(
                cycle,
                state.BitplaneInputSuppressedThroughCycle);
            state.FetchWords = GetDataFetchWordCount();
            state.DataFetchStart = _dataFetchWindow.Start;
            state.FetchSlotStride = DisplayGeometryDecoder.GetDataFetchSlotStride(_dataFetchWindow);
            // PaletteSnapshotIndex is the immutable presentation baseline
            // captured at line start. Mid-line changes are timeline events;
            // rebuilding future DMA must not replace that baseline.

            var oldPlaneHasRowMask = state.PlaneHasRowMask;
            state.PlaneHasRowMask = 0;
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                var oldRowAddress = state.BitplaneRowAddresses[plane];
                var oldWordOffset = state.BitplaneWordIndexOffsets[plane];
                var capturedStop = GetCapturedBitplaneStorageStop(row, plane);
                var oldPlaneHadRow = (oldPlaneHasRowMask & (1 << plane)) != 0;

                state.BitplanePointers[plane] = _bitplanePointers[plane];
                state.BitplaneBaseRows[plane] = _bitplaneBaseRows[plane];
                state.BitplaneDataRegisters[plane] = _bitplaneDataRegisters[plane];
                state.BitplaneWordIndexOffsets[plane] = 0;

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
                var rowAddress = AddDmaPointerOffset(
                    state.BitplanePointers[plane],
                    displaySourceY * rowStride);
                state.PlaneHasRowMask |= (byte)(1 << plane);

                if (oldPlaneHadRow && capturedStop > 0)
                {
                    var nextLogicalWord = GetFirstBitplaneWordAfterCycle(state, cycle, plane);
                    var oldNextLogicalWord = capturedStop - oldWordOffset;
                    var nextSourceAddress = AddDmaPointerOffset(oldRowAddress, oldNextLogicalWord * 2);
                    state.BitplaneWordIndexOffsets[plane] = capturedStop - nextLogicalWord;
                    rowAddress = AddDmaPointerOffset(nextSourceAddress, -(nextLogicalWord * 2));
                }

                state.BitplaneRowAddresses[plane] = rowAddress;
            }

            AdvanceRowDmaPlanVersion(state);

            InvalidateRowDmaPlan(row);

            // Physical lookahead can move capture beyond this row before a
            // causal write rebuilds its unexecuted suffix. Rewind from either
            // the current or a future row so those transfers are sampled at
            // their physical slots instead of being revisited behind horizon.
            if (_liveNextFetchRow >= row)
            {
                SetBitplaneCursorAfterCycle(
                    ref _liveBitplaneFetchTimeline.Captured,
                    state,
                    bitplaneCursorCutoffCycle);
            }

            if (_livePreparedFetchRow >= row)
            {
                SetBitplaneCursorAfterCycle(
                    ref _liveBitplaneFetchTimeline.Prepared,
                    state,
                    bitplaneCursorCutoffCycle);
            }

            InvalidateLiveWorkCycle();
        }

        private long GetBitplaneCursorCutoffAfterPlaneCountChange(
            long writeCycle,
            int oldPlaneCount,
            int newPlaneCount)
        {
            if (!UsesPhysicalBitplanePipeline || oldPlaneCount != 0 || newPlaneCount <= 0)
            {
                return writeCycle;
            }

            // The OCS RGA decision for a bitplane input occurs two color clocks
            // earlier. A simultaneous BPLCON0 write cannot retroactively admit
            // that input, so restart after the last already-decided candidate.
            return checked(writeCycle + (2L * CopperHpCycles));
        }

        private int GetCapturedBitplaneStorageStop(int row, int plane)
        {
            var mask = _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(row, plane)];
            var issuedStop = _physicalBitplanePipeline.HasOutput &&
                _physicalBitplanePipeline.Word.Row == row && _physicalBitplanePipeline.Word.Plane == plane
                ? _physicalBitplanePipeline.Word.StorageWord + 1 : 0;
            for (var word = MaxBitplaneFetchWords - 1; word >= 0; word--)
            {
                if ((mask & ((UInt128)1 << word)) != 0)
                {
                    return Math.Max(word + 1, issuedStop);
                }
            }

            return issuedStop;
        }

        private int GetFirstBitplaneWordAfterCycle(LiveLineState state, long cycle, int targetPlane)
        {
            for (var word = 0; word < state.FetchWords; word++)
            {
                for (var slot = 0; slot < state.FetchSlotStride; slot++)
                {
                    if (!TryGetBitplanePlaneForFetchSlot(
                            slot,
                            state.PlaneCount,
                            state.FetchResolution,
                            out var plane) ||
                        plane != targetPlane)
                    {
                        continue;
                    }

                    var fetchCycle = AgnusChipSlotScheduler.AlignToSlot(
                        state.LineStartCycle +
                        ((long)(state.DataFetchStart + (word * state.FetchSlotStride) + slot) * CopperHpCycles));
                    if (fetchCycle > cycle)
                    {
                        return word;
                    }
                }
            }

            return state.FetchWords;
        }

        private void SetBitplaneCursorAfterCycle(
            ref LiveBitplaneFetchCursor cursor,
            LiveLineState state,
            long cycle)
        {
            cycle = Math.Max(
                cycle,
                Math.Max(_liveCapturedThroughCycle, _bus.ExecutedChipBusHorizon));
            cursor.Row = state.Row;
            cursor.Word = state.FetchWords;
            cursor.Plane = 0;
            cursor.Slot = 0;
            for (var word = 0; word < state.FetchWords; word++)
            {
                for (var slot = 0; slot < state.FetchSlotStride; slot++)
                {
                    if (!TryGetBitplanePlaneForFetchSlot(
                            slot,
                            state.PlaneCount,
                            state.FetchResolution,
                            out var plane))
                    {
                        continue;
                    }

                    var fetchCycle = AgnusChipSlotScheduler.AlignToSlot(
                        state.LineStartCycle +
                        ((long)(state.DataFetchStart + (word * state.FetchSlotStride) + slot) * CopperHpCycles));
                    if (fetchCycle <= cycle)
                    {
                        continue;
                    }

                    cursor.Word = word;
                    cursor.Plane = plane;
                    cursor.Slot = slot;
                    return;
                }
            }
        }

        private void BuildRowDmaPlan(int row, LiveLineState state)
        {
            if ((uint)row >= (uint)_liveHardwareRowCount ||
                state.Generation != _liveGeneration)
            {
                return;
            }

            var ringSlot = GetRasterlineRingSlot(row);
            var existing = _rowDmaPlans[ringSlot];
            if (existing.Valid &&
                existing.Generation == _liveGeneration &&
                existing.Row == row &&
                existing.DmaPlanVersion == state.DmaPlanVersion)
            {
                return;
            }

            var signature = ComputeRowDmaPlanSignature(state);

            var bitplaneStart = ringSlot * MaxRowDmaBitplaneEntriesPerRow;
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
                        if (!TryGetBitplanePlaneForFetchSlot(slot, planeCount, state.FetchResolution, out var plane))
                        {
                            continue;
                        }

                        var fetchHorizontal = state.DataFetchStart + (word * state.FetchSlotStride) + slot;
                        var cycleOffset = fetchHorizontal * CopperHpCycles;
                        if (state.LineStartCycle + cycleOffset <=
                            state.BitplaneInputSuppressedThroughCycle)
                        {
                            continue;
                        }

                        var rowPresent = (state.PlaneHasRowMask & (1 << plane)) != 0;
                        var address = rowPresent
                            ? AddDmaPointerOffset(state.BitplaneRowAddresses[plane], word * 2)
                            : 0u;
                        var entry = new RowDmaBitplaneEntry(
                            cycleOffset,
                            plane,
                            word,
                            slot,
                            address,
                            rowPresent,
                            delayedOutput: UsesPhysicalBitplanePipeline);
                        _agnusRasterlinePlans.SetBitplaneEntry(bitplaneStart + bitplaneCount++, entry);
                    }
                }
            }

            var spriteStart = ringSlot * MaxRowDmaSpriteEntriesPerRow;
            var spriteCount = _agnusRasterlinePlans.MaterializeOcsSpriteEntries(
                ringSlot,
                state.LineStartCycle,
                CopperHpCycles,
                IsSpriteDmaEnabled(state.Dmacon));

            var plan = new RowDmaPlan(
                _liveGeneration,
                row,
                state.LineStartCycle,
                state.Dmacon,
                state.Bplcon0,
                state.DmaPlanVersion,
                signature,
                bitplaneStart,
                bitplaneCount,
                spriteStart,
                spriteCount,
                valid: true);
            _agnusRasterlinePlans.Commit(ringSlot, plan);
            _lastRowDmaPlansBuilt++;
        }

        private void PatchActiveRowSpriteDmaPlan(int row)
        {
            var ringSlot = GetRasterlineRingSlot(row);
            var plan = _rowDmaPlans[ringSlot];
            if (!plan.Valid || plan.Row != row)
            {
                return;
            }

            var spriteCount = _agnusRasterlinePlans.MaterializeOcsSpriteEntries(
                ringSlot,
                plan.LineStartCycle,
                CopperHpCycles,
                IsSpriteDmaEnabled());
            var patched = new RowDmaPlan(
                plan.Generation,
                plan.Row,
                plan.LineStartCycle,
                _dmacon,
                plan.Bplcon0,
                plan.DmaPlanVersion,
                plan.Signature,
                plan.BitplaneStart,
                plan.BitplaneCount,
                ringSlot * MaxRowDmaSpriteEntriesPerRow,
                spriteCount,
                valid: true);
            _agnusRasterlinePlans.PatchSpriteSuffix(ringSlot, in patched);
        }

        private void EnsureActiveRowBitplaneDmaPlanCurrent(int row)
        {
            var ringSlot = GetRasterlineRingSlot(row);
            var state = GetLiveLineState(row);
            var plan = _rowDmaPlans[ringSlot];
            if (!plan.Valid || plan.Row != row)
            {
                BuildRowDmaPlan(row, state);
                plan = _rowDmaPlans[ringSlot];
            }

            if (!plan.Valid || plan.Row != row ||
                plan.Dmacon == _dmacon && plan.Bplcon0 == _bplcon0)
            {
                return;
            }

            var bitplaneStart = ringSlot * MaxRowDmaBitplaneEntriesPerRow;
            var bitplaneCount = 0;
            var planeCount = GetAgnusBitplaneFetchPlaneCount();
            var fetchWords = GetDataFetchWordCount();
            var fetchResolution = _dataFetchWindow.Resolution;
            var fetchStride = DisplayGeometryDecoder.GetDataFetchSlotStride(_dataFetchWindow);
            if (planeCount > 0 &&
                fetchWords > 0 &&
                state.DisplayWindowVerticallyOpen &&
                IsBitplaneDmaEnabled(_dmacon))
            {
                for (var word = 0; word < fetchWords; word++)
                {
                    for (var slot = 0; slot < fetchStride; slot++)
                    {
                        if (!TryGetBitplanePlaneForFetchSlot(slot, planeCount, fetchResolution, out var plane))
                        {
                            continue;
                        }

                        var fetchHorizontal = _dataFetchWindow.Start + (word * fetchStride) + slot;
                        var fetchCycle = state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles);
                        if (fetchCycle <= state.BitplaneInputSuppressedThroughCycle)
                        {
                            continue;
                        }

                        var rowPresent = (state.PlaneHasRowMask & (1 << plane)) != 0;
                        var address = rowPresent
                            ? AddDmaPointerOffset(state.BitplaneRowAddresses[plane], word * 2)
                            : 0u;
                        _agnusRasterlinePlans.SetBitplaneEntry(
                            bitplaneStart + bitplaneCount++,
                            new RowDmaBitplaneEntry(
                                fetchHorizontal * CopperHpCycles,
                                plane,
                                word,
                                slot,
                                address,
                                rowPresent,
                                delayedOutput: UsesPhysicalBitplanePipeline));
                    }
                }
            }

            var patched = new RowDmaPlan(
                plan.Generation,
                plan.Row,
                plan.LineStartCycle,
                _dmacon,
                _bplcon0,
                plan.DmaPlanVersion,
                plan.Signature,
                bitplaneStart,
                bitplaneCount,
                plan.SpriteStart,
                plan.SpriteCount,
                valid: true);
            _agnusRasterlinePlans.PatchBitplaneSuffix(ringSlot, in patched);
        }

        private void InvalidateRowDmaPlan(int row)
        {
            if ((uint)row >= (uint)_liveHardwareRowCount)
            {
                return;
            }

            var ringSlot = GetRasterlineRingSlot(row);
            if (!_rowDmaPlans[ringSlot].Valid || _rowDmaPlans[ringSlot].Row != row)
            {
                return;
            }

            _agnusRasterlinePlans.Invalidate(ringSlot);
            _lastRowDmaPlanInvalidationRows++;
        }

        private bool TryGetValidRowDmaPlan(
            int row,
            LiveLineState state,
            out RowDmaPlan plan,
            bool recordFallback = true)
        {
            plan = default;
            if ((uint)row >= (uint)_liveHardwareRowCount)
            {
                return false;
            }

            var ringSlot = GetRasterlineRingSlot(row);
            plan = _rowDmaPlans[ringSlot];
            if (!plan.Valid)
            {
                BuildRowDmaPlan(row, state);
                plan = _rowDmaPlans[ringSlot];
                if (!plan.Valid)
                {
                    if (recordFallback)
                    {
                        _lastRowDmaScalarFallbackRows++;
                    }

                    return false;
                }

                return true;
            }

            if (plan.Generation != _liveGeneration || plan.Row != row)
            {
                // The three-entry plan cache is indexed by rasterline ring slot.
                // Seeing an older row here is normal lazy replacement, not a
                // state/version mismatch.
                _agnusRasterlinePlans.Invalidate(ringSlot);
                _rowDmaBitplaneCursorIndices[ringSlot] = 0;
                BuildRowDmaPlan(row, state);
                plan = _rowDmaPlans[ringSlot];
                if (!plan.Valid)
                {
                    if (recordFallback)
                    {
                        _lastRowDmaScalarFallbackRows++;
                    }

                    return false;
                }
            }
            else if (plan.DmaPlanVersion != state.DmaPlanVersion)
            {
                _agnusRasterlinePlans.Invalidate(ringSlot);
                _rowDmaBitplaneCursorIndices[ringSlot] = 0;
                _lastRowDmaPlanMismatchRows++;
                BuildRowDmaPlan(row, state);
                plan = _rowDmaPlans[ringSlot];
                if (!plan.Valid)
                {
                    if (recordFallback)
                    {
                        _lastRowDmaScalarFallbackRows++;
                    }

                    return false;
                }
            }

            return true;
        }

        private static void AdvanceRowDmaPlanVersion(LiveLineState state)
        {
            state.DmaPlanVersion = state.DmaPlanVersion == int.MaxValue
                ? 1
                : state.DmaPlanVersion + 1;
        }

        private void RecordRowDmaPlanExecuted(int row, byte mask)
        {
            if ((uint)row >= (uint)_liveHardwareRowCount)
            {
                return;
            }

            var ringSlot = GetRasterlineRingSlot(row);
            var previous = _rowDmaExecutedMasks[ringSlot];
            if (previous == 0)
            {
                _lastRowDmaPlannedRowsExecuted++;
            }

            _rowDmaExecutedMasks[ringSlot] = (byte)(previous | mask);
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
                hash = AddRowDmaPlanSignature(hash, state.Bplcon3);
                hash = AddRowDmaPlanSignature(hash, state.DiwStart);
                hash = AddRowDmaPlanSignature(hash, state.DiwStop);
                hash = AddRowDmaPlanSignature(hash, state.DiwHigh);
                hash = AddRowDmaPlanSignature(hash, state.DiwHighValid ? 1 : 0);
                hash = AddRowDmaPlanSignature(hash, state.AgnusDiwHigh);
                hash = AddRowDmaPlanSignature(hash, state.AgnusDiwHighValid ? 1 : 0);
                hash = AddRowDmaPlanSignature(hash, state.AgnusDisplayWindow.GetHashCode());
                hash = AddRowDmaPlanSignature(hash, state.DeniseDisplayWindow.GetHashCode());
                hash = AddRowDmaPlanSignature(hash, state.DdfStart);
                hash = AddRowDmaPlanSignature(hash, state.DdfStop);
                hash = AddRowDmaPlanSignature(hash, state.DataFetchWindow.GetHashCode());
                hash = AddRowDmaPlanSignature(hash, state.Dmacon);
                hash = AddRowDmaPlanSignature(hash, state.Bpl1Mod);
                hash = AddRowDmaPlanSignature(hash, state.Bpl2Mod);
                hash = AddRowDmaPlanSignature(hash, state.PlaneCount);
                hash = AddRowDmaPlanSignature(hash, state.FetchWords);
                hash = AddRowDmaPlanSignature(hash, state.DataFetchStart);
                hash = AddRowDmaPlanSignature(hash, state.FetchSlotStride);
                hash = AddRowDmaPlanSignature(hash, (int)state.BitplaneInputSuppressedThroughCycle);
                hash = AddRowDmaPlanSignature(hash, (int)(state.BitplaneInputSuppressedThroughCycle >> 32));
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
            var state = GetLiveLineState(row);
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
            if ((uint)row >= (uint)_liveHardwareRowCount)
            {
                return false;
            }

            var offset = GetLiveCaptureSlot(row) * LiveBitplanePlaneCount;
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                if (_liveBitplaneWordMasks[offset + plane] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private int CaptureLivePaletteSnapshot(int row)
        {
            if (!_livePaletteSnapshotDirty &&
                _liveCurrentPaletteSnapshotIndex >= 0 &&
                _liveCurrentPaletteSnapshotRow == row)
            {
                return _liveCurrentPaletteSnapshotIndex;
            }

            var index = _livePaletteSnapshots.GetOrAddForRasterline(
                row,
                _colors,
                _convertedColors.AsSpan(0, PaletteColorCount));
            _liveCurrentPaletteSnapshotIndex = index;
            _liveCurrentPaletteSnapshotRow = row;
            _livePaletteSnapshotDirty = false;
            return index;
        }

        private void CaptureLiveBitplaneFetchBatch(long stopCycle)
        {
            if (!_hostProfilingEnabled)
            {
                CaptureLiveBitplaneFetchBatchCore(stopCycle);
                return;
            }

            var start = Stopwatch.GetTimestamp();
            CaptureLiveBitplaneFetchBatchCore(stopCycle);
            _hostBitplaneCaptureTicks += Stopwatch.GetTimestamp() - start;
            _hostBitplaneCaptureCalls++;
        }

        private void CaptureLiveBitplaneFetchBatchCore(long stopCycle)
        {
            if (UsesPhysicalBitplanePipeline)
            {
                CapturePhysicalBitplaneBatch(stopCycle);
                return;
            }

            while (_liveNextFetchRow < _liveHardwareRowCount)
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
                var state = GetLiveLineState(row);
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
                        if (!TryGetBitplanePlaneForFetchSlot(slot, planeCount, state.FetchResolution, out var plane))
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
            if (_presentationIndependentDisplayLedgerEnabled)
            {
                // G1L deliberately commits one request and one grant at a
                // time. The legacy row batch is a rollback-path optimization,
                // not part of the live requester boundary.
                return false;
            }

            var row = _liveNextFetchRow;
            if ((uint)row >= (uint)_liveHardwareRowCount ||
                !IsLiveLineValid(row))
            {
                return false;
            }

            var state = GetLiveLineState(row);
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
                    _rowDmaBitplaneCursorIndices[GetRasterlineRingSlot(row)] = index;
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
                var grantedCount = 0;
                var firstGrantedCycle = -1L;
                var lastGrantedCycle = -1L;
                if (_bus.SlotScheduleAuditEnabled)
                {
                    var firstEntry = _rowDmaBitplaneEntries[batchStart];
                    var previousAuditSource = _bus.PushSlotScheduleAuditSource(
                        AgnusSlotAuditSource.RowBitplanePresentation,
                        row,
                        firstEntry.Word,
                        firstEntry.Slot);
                    try
                    {
                        _bus.CausalBusExecutor.ExecuteBitplaneRowBatch(
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
                }
                else
                {
                    _bus.CausalBusExecutor.ExecuteBitplaneRowBatch(
                        _rowDmaBitplaneEntries.AsSpan(batchStart, batchCount),
                        state.LineStartCycle,
                        _rowDmaBitplaneBatchValues.AsSpan(0, batchCount),
                        _rowDmaBitplaneBatchGranted.AsSpan(0, batchCount),
                        out grantedCount,
                        out firstGrantedCycle,
                        out lastGrantedCycle);
                }

                ConsumeRowDmaBitplaneBatch(
                    row,
                    state,
                    batchStart,
                    batchCount,
                    grantedCount,
                    firstGrantedCycle,
                    lastGrantedCycle);
                _lastRowDmaBitplaneEntriesExecuted += batchCount;
                capturedAny = true;
                _rowDmaBitplaneCursorIndices[GetRasterlineRingSlot(row)] = batchStart + batchCount;
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
            LiveLineState state,
            int entryStart,
            int count,
            int grantedCount,
            long firstGrantedCycle,
            long lastGrantedCycle)
        {
            var liveSlot = GetLiveCaptureSlot(row);
            var liveWordBase = liveSlot * LiveBitplaneWordsPerRow;
            var liveMaskBase = liveSlot * LiveBitplanePlaneCount;
            var timelineLine = _displayTimeline.GetLine(row);
            var recordTimeline = !_presentationIndependentDisplayLedgerEnabled &&
                !_liveTimelineUnsafeForFrame &&
                _displayTimeline.TryGetBitplaneFetchLine(row, out timelineLine);
            var allGranted = grantedCount == count;
            for (var offset = 0; offset < count; offset++)
            {
                var entry = _rowDmaBitplaneEntries[entryStart + offset];
                var capturedWord = entry.Word + state.BitplaneWordIndexOffsets[entry.Plane];
                if ((uint)capturedWord >= (uint)MaxBitplaneFetchWords)
                {
                    continue;
                }

                var value = _rowDmaBitplaneBatchValues[offset];
                var granted = allGranted || _rowDmaBitplaneBatchGranted[offset];
                _committedDisplayLedger?.Append(
                    AgnusCommittedDisplayEvent.BitplaneSample(
                        entry.GetCycle(state.LineStartCycle),
                        row,
                        entry.Plane,
                        capturedWord,
                        entry.Address,
                        entry.RowPresent,
                        value,
                        granted));
                _liveBitplaneWords[liveWordBase + (entry.Plane * MaxBitplaneFetchWords) + capturedWord] = value;
                _liveBitplaneWordMasks[liveMaskBase + entry.Plane] |= (UInt128)1 << capturedWord;
                if (recordTimeline)
                {
                    var bit = (UInt128)1 << capturedWord;
                    var index = (entry.Plane * MaxBitplaneFetchWords) + capturedWord;
                    timelineLine.BitplaneWords[index] = value;
                    _displayTimeline.RecordBitplaneOutputCycle(
                        timelineLine, index, entry.GetOutputCycle(state.LineStartCycle));
                    timelineLine.BitplaneFetchMasks[entry.Plane] |= bit;
                    if (granted)
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
            var ringSlot = GetRasterlineRingSlot(plan.Row);
            var index = _rowDmaBitplaneCursorIndices[ringSlot];
            if (index < plan.BitplaneStart || index > end)
            {
                index = plan.BitplaneStart;
            }

            for (; index < end; index++)
            {
                var entry = _rowDmaBitplaneEntries[index];
                if (entry.Word > word ||
                    entry.Word == word && entry.Slot >= slot)
                {
                    _rowDmaBitplaneCursorIndices[ringSlot] = index;
                    entryIndex = index;
                    return true;
                }
            }

            _rowDmaBitplaneCursorIndices[ringSlot] = end;
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

            var state = GetLiveLineState(row);
            var ringSlot = GetRasterlineRingSlot(row);
            var currentPlan = _rowDmaPlans[ringSlot];
            if (currentPlan.Valid &&
                currentPlan.Row == row &&
                currentPlan.Dmacon != _dmacon)
            {
                PatchActiveRowSpriteDmaPlan(row);
            }

            if (!TryGetValidRowDmaPlan(row, state, out var plan) ||
                plan.SpriteCount <= 0)
            {
                return false;
            }

            while (_liveNextSpriteRow == row)
            {
                if (!_agnusRasterlinePlans.TryGetNextSpriteEntry(
                    in plan,
                    _liveNextSpriteIndex,
                    _liveNextSpriteWord,
                    out var entryIndex))
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
                _agnusRasterlinePlans.MarkSpriteEntryConsumed(in plan, entryIndex);
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

        private long GetNextKnownLiveBitplaneFetchCycle()
        {
            if (UsesPhysicalBitplanePipeline && _physicalBitplanePipeline.HasOutput)
            {
                return _physicalBitplanePipeline.OutputCycle;
            }

            SkipLiveRowsWithoutFetches();
            if (_liveNextFetchRow >= _liveHardwareRowCount ||
                !IsLiveLineValid(_liveNextFetchRow))
            {
                return long.MaxValue;
            }

            return GetNextLiveBitplaneFetchCycle();
        }

        private long GetNextPreparedLiveBitplaneFetchCycle()
        {
            if (UsesPhysicalBitplanePipeline)
            {
                // An OCS input is issued by the causal event cursor. Future
                // row plans are queried, never committed as issued transfers.
                return long.MaxValue;
            }

            if (!NormalizePreparedLiveBitplaneFetchCursor())
            {
                return long.MaxValue;
            }

            return GetBitplaneFetchCycle(in _liveBitplaneFetchTimeline.Prepared);
        }

        private long GetBitplaneFetchCycle(in LiveBitplaneFetchCursor cursor)
        {
            var state = GetLiveLineState(cursor.Row);
            var fetchHorizontal = state.DataFetchStart +
                (cursor.Word * state.FetchSlotStride) +
                cursor.Slot;
            return AgnusChipSlotScheduler.AlignToSlot(
                state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
        }

        private bool NormalizePreparedLiveBitplaneFetchCursor()
            => NormalizeBitplaneFetchCursor(
                ref _liveBitplaneFetchTimeline.Prepared,
                advanceCapturedPointers: false);

        private bool NormalizeBitplaneFetchCursor(
            ref LiveBitplaneFetchCursor cursor,
            bool advanceCapturedPointers)
        {
            while (cursor.Row < _liveHardwareRowCount)
            {
                if (!IsLiveLineValid(cursor.Row))
                {
                    if (!advanceCapturedPointers && cursor.Row < _liveNextFetchRow)
                    {
                        // The prepared cursor is advisory and may not have been
                        // used for several lines.  Never probe an expired row
                        // through the mutating ring accessor: catch preparation
                        // up to the causally executed capture cursor instead.
                        cursor = _liveBitplaneFetchTimeline.Captured;
                        continue;
                    }

                    return false;
                }

                var state = GetLiveLineState(cursor.Row);

                var planeCount = Math.Max(0, state.PlaneCount);
                if (planeCount <= 0 ||
                    state.FetchWords <= 0 ||
                    !state.DisplayWindowVerticallyOpen ||
                    !IsBitplaneDmaEnabled(state.Dmacon))
                {
                    AdvanceBitplaneFetchToNextRow(ref cursor, advanceCapturedPointers: false);
                    continue;
                }

                while (cursor.Word < state.FetchWords)
                {
                    while (cursor.Slot < state.FetchSlotStride)
                    {
                        if (TryGetBitplanePlaneForFetchSlot(
                                cursor.Slot,
                                planeCount,
                                state.FetchResolution,
                                out var plane))
                        {
                            cursor.Plane = plane;
                            return true;
                        }

                        cursor.Slot++;
                    }

                    cursor.Slot = 0;
                    cursor.Word++;
                }

                AdvanceBitplaneFetchToNextRow(ref cursor, advanceCapturedPointers);
            }

            return false;
        }

        private void PrepareKnownLiveBitplaneSlotsThrough(long targetCycle)
        {
            var previousAuditSource = _bus.PushSlotScheduleAuditSource(AgnusSlotAuditSource.PreparedBitplaneSlot);
            try
            {
                while (_livePreparedFetchRow < _liveHardwareRowCount)
                {
                    if (!NormalizePreparedLiveBitplaneFetchCursor())
                    {
                        return;
                    }

                    var state = GetLiveLineState(_livePreparedFetchRow);
                    var fetchCycle = GetNextPreparedLiveBitplaneFetchCycle();
                    if (fetchCycle > targetCycle)
                    {
                        return;
                    }

                    if ((state.PlaneHasRowMask & (1 << _livePreparedFetchPlane)) != 0)
                    {
                        var address = AddDmaPointerOffset(
                            state.BitplaneRowAddresses[_livePreparedFetchPlane],
                            _livePreparedFetchWord * 2);
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
            => AdvanceBitplaneFetchCursor(
                ref _liveBitplaneFetchTimeline.Prepared,
                advanceCapturedPointers: false);

        private void AdvancePreparedLiveFetchToNextRow()
            => AdvanceBitplaneFetchToNextRow(
                ref _liveBitplaneFetchTimeline.Prepared,
                advanceCapturedPointers: false);

        private bool CaptureKnownLiveBitplaneFetchesThrough(long targetCycle)
        {
            if (UsesPhysicalBitplanePipeline)
            {
                var hasWork = GetNextPhysicalBitplaneCycle() <= targetCycle;
                CapturePhysicalBitplaneBatch(targetCycle);
                return hasWork;
            }

            var captured = false;
            while (_liveNextFetchRow < _liveHardwareRowCount)
            {
                SkipLiveRowsWithoutFetches();
                if (_liveNextFetchRow >= _liveHardwareRowCount ||
                    !IsLiveLineValid(_liveNextFetchRow))
                {
                    return captured;
                }

                var fetchCycle = GetNextLiveBitplaneFetchCycle();
                if (fetchCycle > targetCycle)
                {
                    return captured;
                }

                if (CaptureLiveBitplaneFetch(fetchCycle))
                {
                    AdvanceLiveFetchCursor();
                }
                captured = true;
            }

            return captured;
        }

        private void CaptureLiveBitplaneFetch(int row, int plane, int word, long fetchCycle, LiveLineState state)
        {
            EnsureActiveRowBitplaneDmaPlanCurrent(row);
            var capturedWord = word + state.BitplaneWordIndexOffsets[plane];
            if ((uint)capturedWord >= (uint)MaxBitplaneFetchWords)
            {
                return;
            }

            BitplaneDmaReadLatch latch;
            if ((state.PlaneHasRowMask & (1 << plane)) != 0)
            {
                var address = AddDmaPointerOffset(state.BitplaneRowAddresses[plane], word * 2);
                latch = LoadLiveBitplaneDmaLatch(row, plane, capturedWord, address, fetchCycle);
            }
            else
            {
                latch = BitplaneDmaReadLatch.Denied(
                    row,
                    plane,
                    capturedWord,
                    address: 0,
                    addressValid: false,
                    fetchCycle);
            }

            _bitplaneDmaReadLatch = latch;
            ConsumeLiveBitplaneDmaLatch(ref _bitplaneDmaReadLatch);
        }

        private void CaptureLiveBitplaneFetch(int row, RowDmaBitplaneEntry entry)
        {
            var state = GetLiveLineState(row);
            EnsureActiveRowBitplaneDmaPlanCurrent(row);
            var capturedWord = entry.Word + state.BitplaneWordIndexOffsets[entry.Plane];
            if ((uint)capturedWord >= (uint)MaxBitplaneFetchWords)
            {
                return;
            }

            var cycle = entry.GetCycle(state.LineStartCycle);
            _bitplaneDmaReadLatch = entry.RowPresent
                ? LoadLiveBitplaneDmaLatch(row, entry.Plane, capturedWord, entry.Address, cycle)
                : BitplaneDmaReadLatch.Denied(
                    row,
                    entry.Plane,
                    capturedWord,
                    address: 0,
                    addressValid: false,
                    cycle);
            ConsumeLiveBitplaneDmaLatch(ref _bitplaneDmaReadLatch);
        }

        private bool CaptureLiveBitplaneFetch(long fetchCycle)
        {
            if (UsesPhysicalBitplanePipeline)
            {
                return StepPhysicalBitplane(fetchCycle);
            }

            if ((uint)_liveNextFetchRow >= (uint)_liveHardwareRowCount ||
                (uint)_liveNextFetchPlane >= (uint)_bitplanePointers.Length ||
                (uint)_liveNextFetchWord >= (uint)MaxBitplaneFetchWords)
            {
                return false;
            }

            var state = GetLiveLineState(_liveNextFetchRow);
            if (!IsLiveLineValid(_liveNextFetchRow))
            {
                return false;
            }

            CaptureLiveBitplaneFetch(_liveNextFetchRow, _liveNextFetchPlane, _liveNextFetchWord, fetchCycle, state);
            return true;
        }

        private void RecordLiveDisplayDmaCycle(long cycle)
        {
            MarkLiveCausalDisplayCommit(cycle);
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

            MarkLiveCausalDisplayCommit(lastCycle);

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
            => AdvanceBitplaneFetchCursor(
                ref _liveBitplaneFetchTimeline.Captured,
                advanceCapturedPointers: true);

        private void AdvanceLiveFetchToNextRow(bool advanceBitplanePointers)
            => AdvanceBitplaneFetchToNextRow(
                ref _liveBitplaneFetchTimeline.Captured,
                advanceBitplanePointers);

        private void AdvanceBitplaneFetchCursor(
            ref LiveBitplaneFetchCursor cursor,
            bool advanceCapturedPointers)
        {
            if (cursor.Row >= _liveHardwareRowCount)
            {
                return;
            }

            var state = GetLiveLineState(cursor.Row);
            cursor.Slot++;
            if (cursor.Slot < state.FetchSlotStride)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            cursor.Slot = 0;
            cursor.Plane = 0;
            cursor.Word++;
            if (cursor.Word < state.FetchWords)
            {
                InvalidateLiveWorkCycle();
                return;
            }

            AdvanceBitplaneFetchToNextRow(ref cursor, advanceCapturedPointers);
        }

        private void AdvanceBitplaneFetchToNextRow(
            ref LiveBitplaneFetchCursor cursor,
            bool advanceCapturedPointers)
        {
            if (advanceCapturedPointers)
            {
                AdvanceLiveBitplanePointersPastCapturedRow(cursor.Row);
            }

            cursor.Row++;
            cursor.Word = 0;
            cursor.Plane = 0;
            cursor.Slot = 0;
            InvalidateLiveWorkCycle();
        }

        private void AdvanceLiveBitplanePointersPastCapturedRow(int row)
        {
            if ((uint)row >= (uint)_liveHardwareRowCount || !IsLiveLineValid(row))
            {
                return;
            }

            var state = GetLiveLineState(row);
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
                _agnusRegisters.SetBitplanePointerFromDma(plane, _bitplanePointers[plane], _liveCycle);
                _bus.CausalBusExecutor.RecordBitplaneRowPointerAdvance(
                    plane,
                    _bitplanePointers[plane],
                    _liveCycle);
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

        private void AdvanceLiveSpriteFetchCursorToCycle(long cycle)
        {
            var causalRow = Math.Clamp(
                GetOutputRowForCycle(_liveFrameStartCycle, cycle),
                0,
                LowResOutputHeight);
            if (_liveNextSpriteRow < causalRow)
            {
                _liveNextSpriteRow = causalRow;
                _liveNextSpriteIndex = 0;
                _liveNextSpriteWord = 0;
            }

            while (_liveNextSpriteRow < LowResOutputHeight &&
                GetSpriteDmaFetchCycle(
                    _liveFrameStartCycle,
                    _liveNextSpriteRow,
                    _liveNextSpriteIndex,
                    _liveNextSpriteWord) < cycle)
            {
                AdvanceLiveSpriteFetchCursor();
            }

            InvalidateLiveWorkCycle();
        }


    }
}
