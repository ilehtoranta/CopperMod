/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        public void BeginPresentationFrame(
            PresentationFrameTarget target,
            long frameStartCycle,
            long frameStopCycle)
        {
            if (!target.IsBound)
            {
                throw new ArgumentException("A presentation target is required.", nameof(target));
            }

            if (_boundPresentationActive)
            {
                throw new InvalidOperationException("A presentation frame is already active.");
            }

            if (!_liveDmaEnabled || !_bus.LiveAgnusDmaEnabled)
            {
                throw new InvalidOperationException("Timed presentation requires live Agnus and display DMA.");
            }

            if (frameStopCycle <= frameStartCycle)
            {
                throw new ArgumentOutOfRangeException(nameof(frameStopCycle));
            }

            ConfigurePresentationDimensions(target.Length);
            _renderedCopperTimelineSegments?.Clear();
            _renderedCopperPixelTraces?.Clear();
            _boundPresentationTarget = target;
            _boundPresentationFrameStartCycle = frameStartCycle;
            _boundPresentationFrameStopCycle = Math.Min(frameStopCycle, GetFrameStopCycle(frameStartCycle));
            _nextBoundPresentationRow = 0;
            _boundPresentationActive = true;
            _boundPresentationCompleted = false;
            _renderFrameStartCycle = frameStartCycle;
            _renderInterlaceField = InterlaceEnabled
                ? GetInterlaceField(frameStartCycle)
                : 0;
            ResetFrameCounters();
            ResetPlayfieldPriorityMasks();
            ClearPaletteFrameSpans();
            _bitplaneDataSpans.Clear();
            // Register setup commonly happens before the presentation target is
            // bound. Manual sprite data is already latched in Denise, so seed the
            // line-local command stream from those latches without reading memory.
            CaptureInitialManualSpriteFrameCommands();
            ClearPresentationField(target.AsSpan());
            RenderBoundPresentationLinesThrough(_liveCapturedThroughCycle, completing: false);
        }

        private void ClearPresentationField(Span<uint> target)
        {
            target = target.Slice(0, _renderWidth * _renderHeight);
            if (!InterlaceEnabled || !IsRenderingHighResolutionHeight())
            {
                target.Fill(0xFF000000u);
                return;
            }

            for (var row = _renderInterlaceField; row < _renderHeight; row += 2)
            {
                target.Slice(row * _renderWidth, _renderWidth).Fill(0xFF000000u);
            }
        }

        public void CompletePresentationFrame(long frameStopCycle)
        {
            if (!_boundPresentationActive)
            {
                throw new InvalidOperationException("No presentation frame is active.");
            }

            try
            {
                var stopCycle = Math.Min(frameStopCycle, _boundPresentationFrameStopCycle);
                var captureStop = Math.Max(_boundPresentationFrameStartCycle, stopCycle - 1);
                if (!_boundPresentationCompleted && _liveCapturedThroughCycle < captureStop)
                {
                    _bus.SynchronizeLiveDisplayThrough(captureStop);
                }

                RenderBoundPresentationLinesThrough(captureStop, completing: true);
                _boundPresentationCompleted = true;
            }
            finally
            {
                _boundPresentationActive = false;
                _boundPresentationTarget = default;
                _currentRenderRow = -1;
                _renderingLiveCapture = false;
                _enforceDmaForFrame = false;
                _lastAppliedLivePaletteSnapshotIndex = -1;
            }
        }

        public void AbortPresentationFrame()
        {
            _boundPresentationActive = false;
            _boundPresentationCompleted = false;
            _boundPresentationTarget = default;
            _currentRenderRow = -1;
            _renderingLiveCapture = false;
            _enforceDmaForFrame = false;
            _lastAppliedLivePaletteSnapshotIndex = -1;
        }

        private void ConfigurePresentationDimensions(int pixelCount)
        {
            if (pixelCount >= Width * Height)
            {
                _renderWidth = Width;
                _renderHeight = Height;
            }
            else if (pixelCount >= Width * ActiveLowResOutputHeight)
            {
                _renderWidth = Width;
                _renderHeight = ActiveLowResOutputHeight;
            }
            else if (pixelCount >= LowResWidth * ActiveLowResOutputHeight)
            {
                _renderWidth = LowResWidth;
                _renderHeight = ActiveLowResOutputHeight;
            }
            else
            {
                throw new ArgumentException("The framebuffer is smaller than the active display.", nameof(pixelCount));
            }
        }

        private long GetPresentationFrameStopCycle(long frameStartCycle, long frameEndCycle)
        {
            var naturalFrameStop = GetFrameStopCycle(frameStartCycle);
            if (frameEndCycle <= frameStartCycle)
            {
                return naturalFrameStop;
            }

            return Math.Min(frameEndCycle, naturalFrameStop);
        }

        private int GetInterlaceField(long frameStartCycle)
            => _bus.GetBeamPosition(frameStartCycle).FrameNumber & 1;

        private void RenderBoundPresentationLinesThrough(
            long capturedThroughCycle,
            bool completing,
            int minimumRenderStop = 0)
        {
            if (!_boundPresentationActive ||
                _boundPresentationCompleted ||
                !_boundPresentationTarget.IsBound ||
                capturedThroughCycle < _boundPresentationFrameStartCycle)
            {
                return;
            }

            var rowStop = GetTimelineRowStop(
                _boundPresentationFrameStartCycle,
                _boundPresentationFrameStopCycle);
            var renderStop = rowStop;
            if (!completing)
            {
                var observedRow = GetOutputRowForCycle(
                    _boundPresentationFrameStartCycle,
                    Math.Min(capturedThroughCycle, _boundPresentationFrameStopCycle - 1));
                // A BPLCON0 transition in row N may resolve a post-hard-stop RGA
                // collision in row N-1. Keep two rows live and finalize older rows.
                renderStop = Math.Clamp(observedRow - 1, 0, rowStop);
            }

            renderStop = Math.Max(
                renderStop,
                Math.Clamp(minimumRenderStop, 0, rowStop));

            if (_nextBoundPresentationRow >= renderStop && !completing)
            {
                return;
            }

            var saved = SaveDisplayState();
            var savedCurrentRenderRow = _currentRenderRow;
            var savedTrackDisplayWindowState = _trackDisplayWindowState;
            var savedDisplayWindowVerticallyOpen = _displayWindowVerticallyOpen;
            var savedDisplayWindowStateLine = _displayWindowStateLine;
            var target = _boundPresentationTarget.AsSpan().Slice(0, _renderWidth * _renderHeight);
            _renderingLiveCapture = true;
            _enforceDmaForFrame = true;
            _trackDisplayWindowState = true;
            _lastAppliedLivePaletteSnapshotIndex = -1;
            _bitplaneDataSpans.Clear();
            _displayTimeline.CopyBitplaneDataSpansTo(_bitplaneDataSpans);

            try
            {
                while (_nextBoundPresentationRow < renderStop)
                {
                    var row = _nextBoundPresentationRow;
                    var mustFinalizeBeforeRingReuse = row < minimumRenderStop;
                    if (!TryRenderBoundPresentationRow(
                            target,
                            row,
                            completing || mustFinalizeBeforeRingReuse))
                    {
                        break;
                    }

                    _nextBoundPresentationRow++;
                    MarkFinalizedPresentationRow(row);
                }

                if (completing)
                {
                    while (_nextBoundPresentationRow < rowStop)
                    {
                        FillRows(target, _nextBoundPresentationRow, _nextBoundPresentationRow + 1);
                        MarkFinalizedPresentationRow(_nextBoundPresentationRow);
                        _nextBoundPresentationRow++;
                    }

                    _lastTimelineSegmentCount = _displayTimeline.SegmentCount;
                    _lastTimelineSpriteCommandCount = _displayTimeline.SpriteCommandCount;
                    _lastPlanarChunkCacheHits = _displayTimeline.PlanarChunkCacheHits;
                    _lastPlanarChunkCacheMisses = _displayTimeline.PlanarChunkCacheMisses;
                    _lastSpriteDeniedFetchCount = _displayTimeline.RecalculateSpriteDeniedFetchCount();
                    _lastActiveTimelineFrameCount++;
                }
            }
            finally
            {
                RestoreDisplayState(saved);
                _currentRenderRow = savedCurrentRenderRow;
                _trackDisplayWindowState = savedTrackDisplayWindowState;
                _displayWindowVerticallyOpen = savedDisplayWindowVerticallyOpen;
                _displayWindowStateLine = savedDisplayWindowStateLine;
                _renderingLiveCapture = false;
                _enforceDmaForFrame = false;
                _lastAppliedLivePaletteSnapshotIndex = -1;
            }
        }

        private void MarkFinalizedPresentationRow(int row)
        {
            var rowStopCycle = GetOutputRowStartCycle(
                _boundPresentationFrameStartCycle,
                row + 1) - 1;
            _liveFinalizedPresentationThroughCycle = Math.Max(
                _liveFinalizedPresentationThroughCycle,
                rowStopCycle);
        }

        private bool TryRenderBoundPresentationRow(Span<uint> target, int row, bool completing)
        {
            ClearPaletteFrameSpans();
            ResetPlayfieldPriorityMasks();
            if (!TryRenderBoundPresentationPlayfieldRow(
                    target,
                    row,
                    completing,
                    out var duplicateXStart,
                    out var duplicateXStop))
            {
                return false;
            }

            CaptureRenderedCopperPixelTrace(target, row, stage: 0);

            DuplicatePreparedPresentationRow(target, row, duplicateXStart, duplicateXStop);
            RenderTimelineSpritesRow(target, _displayTimeline, row);
            CaptureRenderedCopperPixelTrace(target, row, stage: 1);
            return true;
        }

        private void CaptureRenderedCopperPixelTrace(Span<uint> target, int row, byte stage)
        {
            if (!_bus.BusAccessCaptureEnabled || row is < 68 or > 76)
            {
                return;
            }

            var scale = GetRenderHorizontalScale();
            var outputY = IsRenderingHighResolutionHeight() ? row * 2 : row;
            var start = (outputY * _renderWidth) + (217 * scale);
            var traces = _renderedCopperPixelTraces ??= new List<RenderedCopperPixelTrace>(32);
            traces.Add(new RenderedCopperPixelTrace(
                row,
                stage,
                target[start],
                target[start + scale],
                target[start + (2 * scale)],
                target[start + (3 * scale)],
                target[start + (4 * scale)],
                target[start + (5 * scale)]));
        }

        private bool TryRenderBoundPresentationPlayfieldRow(
            Span<uint> target,
            int row,
            bool completing,
            out int duplicateXStart,
            out int duplicateXStop)
        {
            duplicateXStart = 0;
            duplicateXStop = 0;
            if (!_displayTimeline.HasLine(row))
            {
                if (!completing)
                {
                    return false;
                }

                FillRows(target, row, row + 1);
                return true;
            }

            var line = _displayTimeline.GetLine(row);
            if (_bus.BusAccessCaptureEnabled)
            {
                var rendered = _renderedCopperTimelineSegments ??=
                    new List<RenderedCopperTimelineSegmentTrace>(256);
                for (var index = 0;
                     index < line.SegmentCount && rendered.Count < MaxCapturedCopperWaitTransitions;
                     index++)
                {
                    var segment = line.Segments[index];
                    var state = _displayTimeline.GetState(segment.StateIndex);
                    rendered.Add(new RenderedCopperTimelineSegmentTrace(
                        row,
                        segment.XStart,
                        segment.XStop,
                        state.PaletteSnapshotIndex,
                        _livePaletteSnapshots.GetEncodedColor(state.PaletteSnapshotIndex, 0)));
                }
            }
            if (!line.UnsafeForTimelineRender && line.SegmentCount > 0)
            {
                for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
                {
                    var segment = line.Segments[segmentIndex];
                    var state = _displayTimeline.GetState(segment.StateIndex);
                    if (!IsTimelineSegmentFetchComplete(row, state, _displayTimeline))
                    {
                        if (!completing)
                        {
                            return false;
                        }

                        break;
                    }
                }

                if (TryRenderTimelineBorderLineFastPath(
                        target,
                        row,
                        line,
                        _displayTimeline,
                        out duplicateXStart,
                        out duplicateXStop))
                {
                    _lastTimelineFastPathRowCount++;
                    return true;
                }

                if (TryRenderTimelineLowResLineFastPath(
                        target,
                        row,
                        line,
                        _displayTimeline,
                        out duplicateXStart,
                        out duplicateXStop))
                {
                    _lastTimelineFastPathRowCount++;
                    return true;
                }

                _lastTimelineFastPathMissCount++;
                var renderedSegment = false;
                for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
                {
                    var segment = line.Segments[segmentIndex];
                    if (segment.XStop <= segment.XStart)
                    {
                        continue;
                    }

                    var state = _displayTimeline.GetState(segment.StateIndex);
                    ApplyTimelineStateForRendering(state);
                    _displayWindowVerticallyOpen = state.DisplayWindowVerticallyOpen;
                    _displayWindowStateLine = StandardVStart + row + 1;
                    _currentRenderRow = row;
                    CapturePaletteFrameSpans(row, row + 1, segment.XStart, segment.XStop);
                    FillRows(target, row, row + 1, segment.XStart, segment.XStop);
                    if (!TryRenderTimelineCachedBitplanes(target, row, segment, state, _displayTimeline))
                    {
                        _lastTimelineMissingBitplaneFallbackCount++;
                        RenderBitplanes(target, row, row + 1, segment.XStart, segment.XStop);
                    }

                    renderedSegment = true;
                }

                if (renderedSegment)
                {
                    return true;
                }
            }

            if (IsLiveLineValid(row))
            {
                var state = GetLiveLineState(row);
                ApplyLiveLineStateForRendering(state);
                _displayWindowVerticallyOpen = state.DeniseDisplayWindowVerticallyOpen;
                _displayWindowStateLine = StandardVStart + row + 1;
                _currentRenderRow = row;
                CapturePaletteFrameSpans(row, row + 1, 0, LowResWidth);
                FillRows(target, row, row + 1);
                RenderBitplanes(target, row, row + 1);
                return true;
            }

            if (!completing)
            {
                return false;
            }

            FillRows(target, row, row + 1);
            return true;
        }

        private bool TryRenderTimelineBorderLineFastPath(
            Span<uint> target,
            int row,
            DisplayLineTimeline line,
            DisplayFrameTimeline timeline,
            out int duplicateXStart,
            out int duplicateXStop)
        {
            duplicateXStart = LowResWidth;
            duplicateXStop = 0;
            if (line.SegmentCount <= 0)
            {
                duplicateXStart = 0;
                return false;
            }

            for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
            {
                var segment = line.Segments[segmentIndex];
                if (segment.XStop <= segment.XStart)
                {
                    continue;
                }

                var state = timeline.GetState(segment.StateIndex);
                if (state.DecodePlaneCount != 0 || state.Bplcon3 != 0)
                {
                    duplicateXStart = 0;
                    duplicateXStop = 0;
                    return false;
                }
            }

            for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
            {
                var segment = line.Segments[segmentIndex];
                if (segment.XStop <= segment.XStart)
                {
                    continue;
                }

                var state = timeline.GetState(segment.StateIndex);
                FillTimelineLowResolutionSegment(
                    target,
                    row,
                    segment.XStart,
                    segment.XStop,
                    state.PaletteSnapshotIndex);
                duplicateXStart = Math.Min(duplicateXStart, segment.XStart);
                duplicateXStop = Math.Max(duplicateXStop, segment.XStop);
            }

            if (duplicateXStop <= duplicateXStart)
            {
                duplicateXStart = 0;
                duplicateXStop = 0;
            }

            return true;
        }

        private int GetTimelineRowStop(long frameStartCycle, long frameStopCycle)
        {
            var rowStop = ActiveLowResOutputHeight;
            if (frameStopCycle < GetFrameStopCycle(frameStartCycle))
            {
                rowStop = Math.Clamp(
                    GetOutputRowForCycle(frameStartCycle, frameStopCycle) + 1,
                    0,
                    ActiveLowResOutputHeight);
            }

            return rowStop;
        }






        private static bool TryGetPriorTimelineSpriteDatb(
            DisplayFrameTimeline timeline,
            SpriteFrameCommand command,
            int row,
            int spriteIndex,
            out ushort value)
        {
            value = 0;
            var valid = false;
            for (var y = 0; y < row; y++)
            {
                var status = timeline.GetSpriteFetchStatus(y, spriteIndex, 1);
                if (status == TimelineFetchStatus.Granted)
                {
                    value = timeline.GetSpriteWord(y, spriteIndex, 1);
                    valid = true;
                }
                else if (status == TimelineFetchStatus.Denied && valid)
                {
                    value = timeline.GetSpriteWord(y, spriteIndex, 1);
                }
            }

            return valid;
        }




        private bool IsTimelineSegmentFetchComplete(int row, DisplayTimelineState state, DisplayFrameTimeline timeline)
        {
            if (!IsBitplaneDmaEnabled(state.Dmacon) || state.PlaneCount <= 0 || state.FetchWords <= 0)
            {
                return true;
            }

            if (!state.DisplayWindowVerticallyOpen)
            {
                return true;
            }

            var planeCount = Math.Clamp(state.PlaneCount, 0, LiveBitplanePlaneCount);
            var fetchWords = Math.Clamp(state.FetchWords, 0, MaxBitplaneFetchWords);
            for (var plane = 0; plane < planeCount; plane++)
            {
                if ((state.PlaneHasRowMask & (1 << plane)) == 0)
                {
                    continue;
                }

                for (var word = 0; word < fetchWords; word++)
                {
                    if (TryMapTimelineBitplaneWord(state, plane, word, out var capturedWord) &&
                        timeline.GetBitplaneFetchStatus(row, plane, capturedWord) == TimelineFetchStatus.NotAttempted)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void ApplyTimelineStateForRendering(
            DisplayTimelineState state,
            bool copyBitplaneState = true,
            bool copyPaletteState = true)
        {
            _diwStart = state.DiwStart;
            _diwStop = state.DiwStop;
            _diwHigh = state.DiwHigh;
            _diwHighValid = state.DiwHighValid;
            _agnusDiwHigh = state.AgnusDiwHigh;
            _agnusDiwHighValid = state.AgnusDiwHighValid;
            _agnusDisplayWindow = state.AgnusDisplayWindow;
            _deniseDisplayWindow = state.DeniseDisplayWindow;
            _ddfStart = state.DdfStart;
            _ddfStop = state.DdfStop;
            _dataFetchWindow = state.DataFetchWindow;
            _bplcon0 = state.Bplcon0;
            _bplcon1 = state.Bplcon1;
            _bplcon2 = state.Bplcon2;
            _bplcon3 = state.Bplcon3;
            _dmacon = state.Dmacon;
            _bpl1mod = state.Bpl1Mod;
            _bpl2mod = state.Bpl2Mod;
            if (copyPaletteState &&
                _lastAppliedLivePaletteSnapshotIndex != state.PaletteSnapshotIndex)
            {
                _livePaletteSnapshots.CopyTo(
                    state.PaletteSnapshotIndex,
                    _colors,
                    _convertedColors);
                _lastAppliedLivePaletteSnapshotIndex = state.PaletteSnapshotIndex;
            }

            if (copyBitplaneState)
            {
                Array.Copy(state.BitplanePointers, _bitplanePointers, _bitplanePointers.Length);
                Array.Copy(state.BitplaneBaseRows, _bitplaneBaseRows, _bitplaneBaseRows.Length);
                Array.Copy(
                    state.BitplaneWordIndexOffsets,
                    _renderBitplaneWordIndexOffsets,
                    _renderBitplaneWordIndexOffsets.Length);
                Array.Copy(state.BitplaneDataRegisters, _bitplaneDataRegisters, _bitplaneDataRegisters.Length);
            }
        }

        private bool TryRenderTimelineLowResLineFastPath(
            Span<uint> bgra,
            int row,
            DisplayLineTimeline line,
            DisplayFrameTimeline timeline,
            out int duplicateXStart,
            out int duplicateXStop)
        {
            duplicateXStart = 0;
            duplicateXStop = 0;
            if (line.SegmentCount <= 0)
            {
                return false;
            }

            DisplayTimelineState? firstState = null;
            var lineXStart = LowResWidth;
            var lineXStop = 0;
            for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
            {
                var segment = line.Segments[segmentIndex];
                if (segment.XStop <= segment.XStart)
                {
                    continue;
                }

                var state = timeline.GetState(segment.StateIndex);
                if (!IsTimelineLowResLineFastPathSupported(row, segment, state) ||
                    (firstState != null && !HasSameTimelineLowResFastPathShape(firstState, state)))
                {
                    return false;
                }

                firstState ??= state;
                lineXStart = Math.Min(lineXStart, segment.XStart);
                lineXStop = Math.Max(lineXStop, segment.XStop);
            }

            if (firstState is null)
            {
                return true;
            }

            var indexState = firstState;
            // The prepared path reads all bitplane indexing state directly from
            // indexState. Copying the four eight-plane arrays for every palette
            // segment dominated composition time on copper-heavy rasterlines.
            ApplyTimelineStateForRendering(
                indexState,
                copyBitplaneState: false,
                copyPaletteState: false);
            _displayWindowVerticallyOpen = indexState.DisplayWindowVerticallyOpen;
            _displayWindowStateLine = StandardVStart + row + 1;
            _currentRenderRow = row;
            if (!TryPrepareTimelineLowResFastBitplanes(
                    row,
                    lineXStart,
                    lineXStop,
                    indexState,
                    timeline,
                    out var dataFirstX,
                    out var dataLastX))
            {
                return false;
            }

            for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
            {
                var segment = line.Segments[segmentIndex];
                if (segment.XStop <= segment.XStart)
                {
                    continue;
                }

                var state = timeline.GetState(segment.StateIndex);
                ApplyTimelineStateForRendering(
                    state,
                    copyBitplaneState: false,
                    copyPaletteState: false);
                _displayWindowVerticallyOpen = state.DisplayWindowVerticallyOpen;
                _displayWindowStateLine = StandardVStart + row + 1;
                _currentRenderRow = row;
                CaptureLivePaletteFrameSpan(row, segment.XStart, segment.XStop, state);
                if (dataLastX > dataFirstX)
                {
                    // The prepared writer produces both foreground pixels and
                    // playfield color zero. Fill only the uncovered sides instead
                    // of writing the active playfield twice.
                    FillTimelineLowResolutionSegment(
                        bgra,
                        row,
                        segment.XStart,
                        Math.Min(segment.XStop, dataFirstX),
                        state.PaletteSnapshotIndex);
                    FillTimelineLowResolutionSegment(
                        bgra,
                        row,
                        Math.Max(segment.XStart, dataLastX),
                        segment.XStop,
                        state.PaletteSnapshotIndex);
                }
                else
                {
                    FillTimelineLowResolutionSegment(
                        bgra,
                        row,
                        segment.XStart,
                        segment.XStop,
                        state.PaletteSnapshotIndex);
                }
                WritePreparedTimelineLowResFastBitplanes(
                    bgra,
                    row,
                    segment.XStart,
                    segment.XStop,
                    dataFirstX,
                    dataLastX,
                    state.PaletteSnapshotIndex);
            }

            duplicateXStart = lineXStart;
            duplicateXStop = lineXStop;
            return true;
        }

        private static bool HasSameTimelineLowResFastPathShape(DisplayTimelineState left, DisplayTimelineState right)
        {
            return left.Bplcon0 == right.Bplcon0 &&
                left.Bplcon1 == right.Bplcon1 &&
                left.Bplcon2 == right.Bplcon2 &&
                left.Bplcon3 == right.Bplcon3 &&
                left.DiwStart == right.DiwStart &&
                left.DiwStop == right.DiwStop &&
                left.DiwHigh == right.DiwHigh &&
                left.DiwHighValid == right.DiwHighValid &&
                left.AgnusDiwHigh == right.AgnusDiwHigh &&
                left.AgnusDiwHighValid == right.AgnusDiwHighValid &&
                left.AgnusDisplayWindow == right.AgnusDisplayWindow &&
                left.DeniseDisplayWindow == right.DeniseDisplayWindow &&
                left.DisplayWindowVerticallyOpen == right.DisplayWindowVerticallyOpen &&
                left.DdfStart == right.DdfStart &&
                left.DdfStop == right.DdfStop &&
                left.DataFetchWindow == right.DataFetchWindow &&
                left.Dmacon == right.Dmacon &&
                left.Bpl1Mod == right.Bpl1Mod &&
                left.Bpl2Mod == right.Bpl2Mod &&
                left.PlaneCount == right.PlaneCount &&
                left.DecodePlaneCount == right.DecodePlaneCount &&
                left.FetchWords == right.FetchWords &&
                left.DataFetchStart == right.DataFetchStart &&
                left.FetchSlotStride == right.FetchSlotStride &&
                left.PlaneHasRowMask == right.PlaneHasRowMask &&
                HasSameBitplaneWordIndexOffsets(left, right) &&
                HasSameBitplaneDataRegisters(left, right);
        }

        private static bool HasSameBitplaneWordIndexOffsets(
            DisplayTimelineState left,
            DisplayTimelineState right)
        {
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                if (left.BitplaneWordIndexOffsets[plane] != right.BitplaneWordIndexOffsets[plane])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSameBitplaneDataRegisters(DisplayTimelineState left, DisplayTimelineState right)
        {
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                if (left.BitplaneDataRegisters[plane] != right.BitplaneDataRegisters[plane])
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsTimelineLowResLineFastPathSupported(int row, DisplayLineSegment segment, DisplayTimelineState state)
        {
            if (state.Resolution == DeniseResolution.SuperHighRes ||
                (state.Bplcon0 & 0x8804) != 0 ||
                HasBitplaneDataSpanInBand(row, row + 1, segment.XStart, segment.XStop))
            {
                return false;
            }

            var dualPlayfield = (state.Bplcon0 & 0x0400) != 0;
            var planeCount = Math.Clamp(state.DecodePlaneCount, 0, LiveBitplanePlaneCount);
            if ((state.Bplcon1 & 0x00FF) != 0 &&
                (dualPlayfield || !TryGetUniformNormalPlayfieldScroll(state, planeCount, out _)))
            {
                return false;
            }

            return true;
        }

        private bool TryPrepareTimelineLowResFastBitplanes(
            int row,
            int xStart,
            int xStop,
            DisplayTimelineState state,
            DisplayFrameTimeline timeline,
            out int dataFirstX,
            out int dataLastX)
        {
            dataFirstX = 0;
            dataLastX = 0;
            if (state.PlaneCount <= 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return true;
            }

            if (!state.DisplayWindowVerticallyOpen)
            {
                return true;
            }

            var planeCount = Math.Clamp(state.DecodePlaneCount, 0, LiveBitplanePlaneCount);
            var fetchWords = Math.Clamp(state.FetchWords, 0, MaxBitplaneFetchWords);
            if (planeCount <= 0 || fetchWords <= 0)
            {
                return true;
            }

            var window = GetEffectiveDisplayWindow();
            var windowXStart = GetDisplayWindowOutputXStart(window);
            var windowXStop = GetDisplayWindowOutputXStop(window);
            var windowYStart = GetDisplayWindowOutputYStart(window);
            var windowYStop = GetDisplayWindowOutputYStop(window);
            if (windowXStop <= windowXStart || windowYStop <= windowYStart)
            {
                return true;
            }

            var rowStart = Math.Max(0, windowYStart);
            var rowStop = Math.Min(ActiveLowResOutputHeight, windowYStop);
            if (row < rowStart || row >= rowStop)
            {
                return true;
            }

            var originX = GetDataFetchStartX(window);
            var clipLeft = Math.Max(Math.Max(0, windowXStart), xStart);
            var clipRight = Math.Min(Math.Min(LowResWidth, windowXStop), xStop);
            if (clipRight <= clipLeft)
            {
                return true;
            }

            Array.Clear(_timelineFastPathColorIndexes, clipLeft, clipRight - clipLeft);
            Array.Clear(_timelineFastPathPriorityMasks, clipLeft, clipRight - clipLeft);

            var dualPlayfield = (state.Bplcon0 & 0x0400) != 0;
            var normalPlayfieldScroll = 0;
            if (!dualPlayfield)
            {
                _ = TryGetUniformNormalPlayfieldScroll(state, planeCount, out normalPlayfieldScroll);
            }

            var fetchPixels = fetchWords * PlanarChunkPixels;
            var dataOriginX = originX + normalPlayfieldScroll;
            var firstX = Math.Max(clipLeft, dataOriginX);
            var lastX = Math.Min(clipRight, dataOriginX + fetchPixels);
            if (lastX <= firstX)
            {
                return true;
            }

            dataFirstX = firstX;
            dataLastX = lastX;
            var firstWord = Math.Clamp((firstX - dataOriginX) >> 4, 0, fetchWords - 1);
            var lastWord = Math.Clamp((lastX - 1 - dataOriginX) >> 4, 0, fetchWords - 1);
            for (var word = firstWord; word <= lastWord; word++)
            {
                if (!TryGetTimelineDecodedChunk(row, word, state, planeCount, dualPlayfield, timeline, out var chunk))
                {
                    return false;
                }

                var wordStart = dataOriginX + (word * PlanarChunkPixels);
                var chunkXStart = Math.Max(firstX, wordStart);
                var chunkXStop = Math.Min(lastX, wordStart + PlanarChunkPixels);
                for (var x = chunkXStart; x < chunkXStop; x++)
                {
                    var offset = x - wordStart;
                    _timelineFastPathColorIndexes[x] = chunk.GetColorIndex(offset);
                    _timelineFastPathPriorityMasks[x] = chunk.GetPriorityMask(offset);
                }
            }

            for (var x = firstX; x < lastX; x++)
            {
                var colorIndex = _timelineFastPathColorIndexes[x];
                var priorityMask = _timelineFastPathPriorityMasks[x];
                SetPlayfieldPriorityMask(x, row, priorityMask);
                if (colorIndex != 0)
                {
                    RecordBitplanePixel(colorIndex, priorityMask, x, row);
                }
            }

            return true;
        }

        private void WritePreparedTimelineLowResFastBitplanes(
            Span<uint> bgra,
            int row,
            int segmentXStart,
            int segmentXStop,
            int dataFirstX,
            int dataLastX,
            int paletteSnapshotIndex)
        {
            var xStart = Math.Max(segmentXStart, dataFirstX);
            var xStop = Math.Min(segmentXStop, dataLastX);
            if (xStop <= xStart)
            {
                return;
            }

            var scale = GetRenderHorizontalScale();
            var outputY = IsRenderingHighResolutionHeight()
                ? (row * 2) + (InterlaceEnabled ? _renderInterlaceField : 0)
                : row;
            var output = bgra.Slice(
                (outputY * _renderWidth) + (xStart * scale),
                (xStop - xStart) * scale);
            WritePreparedLowResolutionColorRun(
                output,
                xStart,
                xStop,
                scale,
                paletteSnapshotIndex);

        }

        private void DuplicatePreparedPresentationRow(
            Span<uint> bgra,
            int row,
            int xStart,
            int xStop)
        {
            if (!IsRenderingHighResolutionHeight() ||
                InterlaceEnabled ||
                xStop <= xStart)
            {
                return;
            }

            var scale = GetRenderHorizontalScale();
            var pixelStart = xStart * scale;
            var pixelCount = (xStop - xStart) * scale;
            var outputY = row * 2;
            bgra.Slice((outputY * _renderWidth) + pixelStart, pixelCount)
                .CopyTo(bgra.Slice(((outputY + 1) * _renderWidth) + pixelStart, pixelCount));
        }

        private void FillTimelineLowResolutionSegment(
            Span<uint> bgra,
            int row,
            int xStart,
            int xStop,
            int paletteSnapshotIndex)
        {
            xStart = Math.Clamp(xStart, 0, LowResWidth);
            xStop = Math.Clamp(xStop, xStart, LowResWidth);
            if ((uint)row >= (uint)LowResOutputHeight || xStart >= xStop)
            {
                return;
            }

            var encodedColor0 = _livePaletteSnapshots.GetEncodedColor(paletteSnapshotIndex, 0);
            var displayColor = _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, 0);
            var ecsExtensionsEnabled = AreEcsDeniseExtensionsEnabled(_bplcon0);
            var borderColor = ecsExtensionsEnabled && (_bplcon3 & Bplcon3BorderBlank) != 0
                ? 0xFF00_0000u
                : ConvertColor(encodedColor0);
            if (ecsExtensionsEnabled && (_bplcon3 & Bplcon3BorderNotTransparent) == 0)
            {
                borderColor &= 0x00FF_FFFFu;
            }

            var window = GetEffectiveDisplayWindow();
            var windowXStart = GetDisplayWindowOutputXStart(window);
            var windowXStop = GetDisplayWindowOutputXStop(window);
            var windowYStart = GetDisplayWindowOutputYStart(window);
            var windowYStop = GetDisplayWindowOutputYStop(window);
            if (row < windowYStart || row >= windowYStop)
            {
                FillLowResolutionOutputRun(bgra, xStart, xStop, row, borderColor);
                return;
            }

            var displayStart = Math.Clamp(windowXStart, xStart, xStop);
            var displayStop = Math.Clamp(windowXStop, displayStart, xStop);
            FillLowResolutionOutputRun(bgra, xStart, displayStart, row, borderColor);
            FillLowResolutionOutputRun(bgra, displayStart, displayStop, row, displayColor);
            FillLowResolutionOutputRun(bgra, displayStop, xStop, row, borderColor);
        }

        private void WritePreparedLowResolutionColorRun(
            Span<uint> output,
            int xStart,
            int xStop,
            int scale,
            int paletteSnapshotIndex)
        {
            var source = _timelineFastPathColorIndexes;
            var destinationIndex = 0;
            var x = xStart;
            if (Vector256.IsHardwareAccelerated && scale == 1)
            {
                for (; x + 8 <= xStop; x += 8)
                {
                    Vector256.Create(
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x]),
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 1]),
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 2]),
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 3]),
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 4]),
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 5]),
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 6]),
                        _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 7]))
                        .CopyTo(output.Slice(destinationIndex, 8));
                    destinationIndex += 8;
                }
            }
            else if (Vector256.IsHardwareAccelerated && scale == 2)
            {
                for (; x + 4 <= xStop; x += 4)
                {
                    var pixel0 = _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x]);
                    var pixel1 = _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 1]);
                    var pixel2 = _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 2]);
                    var pixel3 = _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x + 3]);
                    Vector256.Create(pixel0, pixel0, pixel1, pixel1, pixel2, pixel2, pixel3, pixel3)
                        .CopyTo(output.Slice(destinationIndex, 8));
                    destinationIndex += 8;
                }
            }

            for (; x < xStop; x++)
            {
                var pixel = _livePaletteSnapshots.GetConvertedColor(paletteSnapshotIndex, source[x]);
                output.Slice(destinationIndex, scale).Fill(pixel);
                destinationIndex += scale;
            }
        }

        private bool TryRenderTimelineCachedBitplanes(
            Span<uint> bgra,
            int row,
            DisplayLineSegment segment,
            DisplayTimelineState state,
            DisplayFrameTimeline timeline)
        {
            var hasBitplaneDataSpans = HasBitplaneDataSpanInBand(row, row + 1, segment.XStart, segment.XStop);
            if (state.PlaneCount <= 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return !hasBitplaneDataSpans;
            }

            if (state.Resolution == DeniseResolution.SuperHighRes)
            {
                return false;
            }

            var highResolution = state.Resolution == DeniseResolution.HighRes;
            var dualPlayfield = (state.Bplcon0 & 0x0400) != 0;
            if ((state.Bplcon0 & 0x0800) != 0 ||
                (!highResolution && (state.Bplcon0 & 0x0004) != 0) ||
                hasBitplaneDataSpans ||
                (highResolution && (dualPlayfield || (state.Bplcon1 & 0x00FF) != 0)))
            {
                return false;
            }

            var planeCount = Math.Clamp(state.DecodePlaneCount, 0, LiveBitplanePlaneCount);
            var fetchWords = Math.Clamp(state.FetchWords, 0, MaxBitplaneFetchWords);
            var window = GetEffectiveDisplayWindow();
            var windowXStart = GetDisplayWindowOutputXStart(window);
            var windowXStop = GetDisplayWindowOutputXStop(window);
            var windowYStart = GetDisplayWindowOutputYStart(window);
            var windowYStop = GetDisplayWindowOutputYStop(window);
            if (windowXStop <= windowXStart || windowYStop <= windowYStart || fetchWords <= 0)
            {
                return true;
            }

            var fetchPixels = fetchWords * PlanarChunkPixels;
            var drawPixels = highResolution ? fetchPixels / 2 : fetchPixels;
            var originX = GetDataFetchStartX(window);
            var clipLeft = Math.Max(Math.Max(0, windowXStart), segment.XStart);
            var clipRight = Math.Min(Math.Min(LowResWidth, windowXStop), segment.XStop);
            var rowStart = Math.Max(0, windowYStart);
            var rowStop = Math.Min(ActiveLowResOutputHeight, windowYStop);
            if (row < rowStart || row >= rowStop || clipRight <= clipLeft)
            {
                return true;
            }

            var zeroScroll = (state.Bplcon1 & 0x00FF) == 0;
            var normalPlayfieldScroll = 0;
            var uniformNormalScroll = !dualPlayfield && TryGetUniformNormalPlayfieldScroll(state, planeCount, out normalPlayfieldScroll);
            var useChunkedScroll = zeroScroll || uniformNormalScroll;
            var renderHighWidth = IsRenderingHighResolutionWidth();
            var renderHighHeight = IsRenderingHighResolutionHeight();
            var renderInterlace = (state.Bplcon0 & 0x0004) != 0;
            var lastX = Math.Min(clipRight, originX + drawPixels + (highResolution ? 8 : 16));
            var cachedWord = -1;
            var cachedChunk = default(PlanarChunkDecoded);
            if (highResolution)
            {
                for (var x = Math.Max(clipLeft, originX); x < lastX; x++)
                {
                    var relativeSubPixel = (x - originX) * 2;
                    var leftColorIndex = 0;
                    var rightColorIndex = 0;
                    if ((uint)relativeSubPixel < (uint)fetchPixels)
                    {
                        var word = relativeSubPixel >> 4;
                        if ((uint)word < (uint)fetchWords)
                        {
                            if (word != cachedWord)
                            {
                                if (!TryGetTimelineDecodedChunk(row, word, state, planeCount, dualPlayfield: false, timeline, out cachedChunk))
                                {
                                    return false;
                                }

                                cachedWord = word;
                            }

                            var offset = relativeSubPixel & 0x0F;
                            leftColorIndex = cachedChunk.GetColorIndex(offset);
                            rightColorIndex = cachedChunk.GetColorIndex(offset + 1);
                        }
                    }

                    var priorityMask = (leftColorIndex | rightColorIndex) == 0 ? (byte)0 : NormalPlayfieldPriorityMask;
                    SetPlayfieldPriorityMask(x, row, priorityMask);
                    if ((leftColorIndex | rightColorIndex) != 0)
                    {
                        RecordBitplanePixel(
                            leftColorIndex != 0 ? leftColorIndex : rightColorIndex,
                            NormalPlayfieldPriorityMask,
                            x,
                            row);
                    }

                    if (renderHighWidth)
                    {
                        WriteHighResolutionOutputPixelPair(
                            bgra,
                            x,
                            row,
                            ConvertColorIndex(leftColorIndex),
                            ConvertColorIndex(rightColorIndex),
                            renderHighWidth,
                            renderHighHeight,
                            renderInterlace,
                            _renderInterlaceField);
                    }
                    else
                    {
                        WriteLowResolutionOutputPixel(
                            bgra,
                            x,
                            row,
                            ConvertColorIndex(SelectLowResolutionHiResColorIndex(leftColorIndex, rightColorIndex)),
                            renderHighWidth,
                            renderHighHeight,
                            renderInterlace,
                            _renderInterlaceField);
                    }
                }

                return true;
            }

            for (var x = Math.Max(clipLeft, originX); x < lastX; x++)
            {
                var relativeX = x - originX;
                if (relativeX < -15 || relativeX >= drawPixels + 16)
                {
                    continue;
                }

                int colorIndex;
                byte priorityMask;
                if (useChunkedScroll)
                {
                    var scrolledRelativeX = uniformNormalScroll ? relativeX - normalPlayfieldScroll : relativeX;
                    if ((uint)scrolledRelativeX >= (uint)fetchPixels)
                    {
                        continue;
                    }

                    var word = scrolledRelativeX >> 4;
                    if ((uint)word >= (uint)fetchWords)
                    {
                        continue;
                    }

                    if (word != cachedWord)
                    {
                        if (!TryGetTimelineDecodedChunk(row, word, state, planeCount, dualPlayfield, timeline, out cachedChunk))
                        {
                            return false;
                        }

                        cachedWord = word;
                    }

                    var offset = scrolledRelativeX & 0x0F;
                    colorIndex = cachedChunk.GetColorIndex(offset);
                    priorityMask = cachedChunk.GetPriorityMask(offset);
                }
                else if (dualPlayfield)
                {
                    if (!TryGetTimelineDualPlayfieldPixel(row, x, originX, fetchPixels, fetchWords, state, planeCount, timeline, out var dualPixel))
                    {
                        return false;
                    }

                    colorIndex = dualPixel.ColorIndex;
                    priorityMask = dualPixel.PriorityMask;
                }
                else
                {
                    if (!TryGetTimelineBitplaneColorIndex(row, x, originX, fetchPixels, fetchWords, state, planeCount, timeline, out colorIndex))
                    {
                        return false;
                    }

                    colorIndex = ApplyUndocumentedNormalPlayfieldPriorityQuirk(colorIndex, planeCount);
                    priorityMask = colorIndex == 0 ? (byte)0 : NormalPlayfieldPriorityMask;
                }

                SetPlayfieldPriorityMask(x, row, priorityMask);
                if (colorIndex != 0)
                {
                    RecordBitplanePixel(colorIndex, priorityMask, x, row);
                }

                WriteLowResolutionOutputPixel(bgra, x, row, _convertedColors[colorIndex]);
            }

            return true;
        }

        private static bool TryGetUniformNormalPlayfieldScroll(DisplayTimelineState state, int planeCount, out int scroll)
        {
            var evenScroll = state.Bplcon1 & 0x0F;
            var oddScroll = (state.Bplcon1 >> 4) & 0x0F;
            var hasEvenPlane = false;
            var hasOddPlane = false;
            for (var plane = 0; plane < planeCount; plane++)
            {
                if ((state.PlaneHasRowMask & (1 << plane)) == 0)
                {
                    continue;
                }

                if ((plane & 1) == 0)
                {
                    hasEvenPlane = true;
                }
                else
                {
                    hasOddPlane = true;
                }
            }

            scroll = hasEvenPlane ? evenScroll : oddScroll;
            return !hasEvenPlane || !hasOddPlane || evenScroll == oddScroll;
        }

        private bool TryGetTimelineBitplaneColorIndex(
            int row,
            int x,
            int originX,
            int fetchPixels,
            int fetchWords,
            DisplayTimelineState state,
            int planeCount,
            DisplayFrameTimeline timeline,
            out int colorIndex,
            int hiresSubPixel = -1)
        {
            colorIndex = 0;
            for (var plane = 0; plane < planeCount; plane++)
            {
                if ((state.PlaneHasRowMask & (1 << plane)) == 0)
                {
                    continue;
                }

                var relativeX = x - originX - GetPlaneHorizontalScroll(plane);
                if (hiresSubPixel >= 0)
                {
                    relativeX = (relativeX * 2) + hiresSubPixel;
                }

                if (relativeX < 0 || relativeX >= fetchPixels)
                {
                    continue;
                }

                var word = relativeX >> 4;
                if ((uint)word >= (uint)fetchWords)
                {
                    continue;
                }

                if (!TryGetTimelineBitplaneWord(row, plane, word, state, timeline, out var data))
                {
                    return false;
                }

                var bit = 15 - (relativeX & 0x0F);
                colorIndex |= ((data >> bit) & 1) << plane;
            }

            return true;
        }

        private bool TryGetTimelineDualPlayfieldPixel(
            int row,
            int x,
            int originX,
            int fetchPixels,
            int fetchWords,
            DisplayTimelineState state,
            int planeCount,
            DisplayFrameTimeline timeline,
            out DualPlayfieldPixel pixel)
        {
            var rawColorIndex = 0;
            for (var plane = 0; plane < planeCount; plane++)
            {
                if ((state.PlaneHasRowMask & (1 << plane)) == 0)
                {
                    continue;
                }

                var relativeX = x - originX - GetPlaneHorizontalScroll(plane);
                if (relativeX < 0 || relativeX >= fetchPixels)
                {
                    continue;
                }

                var word = relativeX >> 4;
                if ((uint)word >= (uint)fetchWords)
                {
                    continue;
                }

                if (!TryGetTimelineBitplaneWord(row, plane, word, state, timeline, out var data))
                {
                    pixel = default;
                    return false;
                }

                var bit = 15 - (relativeX & 0x0F);
                rawColorIndex |= ((data >> bit) & 1) << plane;
            }

            pixel = ConvertRawColorIndexToDualPlayfieldPixel(rawColorIndex, planeCount);
            return true;
        }

        private bool TryGetTimelineBitplaneWord(
            int row,
            int plane,
            int word,
            DisplayTimelineState state,
            DisplayFrameTimeline timeline,
            out ushort data)
        {
            if (IsLatchedOnlyOcsBpu7Plane(state.Bplcon0, plane))
            {
                data = state.BitplaneDataRegisters[plane];
                return true;
            }

            if (!TryMapTimelineBitplaneWord(state, plane, word, out var capturedWord))
            {
                data = 0;
                return true;
            }

            var status = timeline.GetBitplaneFetchStatus(row, plane, capturedWord);
            if (status == TimelineFetchStatus.NotAttempted)
            {
                data = 0;
                return false;
            }

            data = timeline.GetBitplaneWord(row, plane, capturedWord);
            return true;
        }

        private static bool TryMapTimelineBitplaneWord(
            DisplayTimelineState state,
            int plane,
            int word,
            out int capturedWord)
        {
            capturedWord = word + state.BitplaneWordIndexOffsets[plane];
            return (uint)capturedWord < (uint)MaxBitplaneFetchWords;
        }

        private bool TryGetTimelineDecodedChunk(
            int row,
            int word,
            DisplayTimelineState state,
            int planeCount,
            bool dualPlayfield,
            DisplayFrameTimeline timeline,
            out PlanarChunkDecoded chunk)
        {
            Span<ushort> words = stackalloc ushort[LiveBitplanePlaneCount];
            var planeHasRowMask = state.PlaneHasRowMask;
            for (var plane = 0; plane < planeCount; plane++)
            {
                if ((planeHasRowMask & (1 << plane)) == 0)
                {
                    words[plane] = 0;
                    continue;
                }

                if (IsLatchedOnlyOcsBpu7Plane(state.Bplcon0, plane))
                {
                    words[plane] = state.BitplaneDataRegisters[plane];
                    continue;
                }

                if (!TryMapTimelineBitplaneWord(state, plane, word, out var capturedWord))
                {
                    words[plane] = 0;
                    continue;
                }

                var status = timeline.GetBitplaneFetchStatus(row, plane, capturedWord);
                if (status == TimelineFetchStatus.NotAttempted)
                {
                    chunk = default;
                    return false;
                }

                words[plane] = timeline.GetBitplaneWord(row, plane, capturedWord);
            }

            timeline.RecordPlanarChunkDecode();
            chunk = DecodePlanarChunk(words, planeHasRowMask, planeCount, dualPlayfield);
            return true;
        }

        private PlanarChunkDecoded DecodePlanarChunk(
            Span<ushort> words,
            byte planeHasRowMask,
            int planeCount,
            bool dualPlayfield)
        {
            var rawColorIndexesLow = 0UL;
            var rawColorIndexesHigh = 0UL;
            for (var plane = 0; plane < planeCount; plane++)
            {
                if ((planeHasRowMask & (1 << plane)) == 0)
                {
                    continue;
                }

                rawColorIndexesLow |= PlanarByteExpansion[words[plane] >> 8] << plane;
                rawColorIndexesHigh |= PlanarByteExpansion[words[plane] & 0xFF] << plane;
            }

            var colorIndexesLow = 0UL;
            var colorIndexesHigh = 0UL;
            var priorityMasksLow = 0UL;
            var priorityMasksHigh = 0UL;
            for (var pixel = 0; pixel < PlanarChunkPixels; pixel++)
            {
                var shift = (pixel & 7) * 8;
                var packed = pixel < 8 ? rawColorIndexesLow : rawColorIndexesHigh;
                var rawColorIndex = (int)((packed >> shift) & 0xFF);

                if (dualPlayfield)
                {
                    var dual = ConvertRawColorIndexToDualPlayfieldPixel(rawColorIndex, planeCount);
                    PackPlanarChunkPixel(
                        pixel,
                        (byte)dual.ColorIndex,
                        dual.PriorityMask,
                        ref colorIndexesLow,
                        ref colorIndexesHigh,
                        ref priorityMasksLow,
                        ref priorityMasksHigh);
                    continue;
                }

                var colorIndex = ApplyUndocumentedNormalPlayfieldPriorityQuirk(rawColorIndex, planeCount);
                PackPlanarChunkPixel(
                    pixel,
                    (byte)colorIndex,
                    colorIndex == 0 ? (byte)0 : NormalPlayfieldPriorityMask,
                    ref colorIndexesLow,
                    ref colorIndexesHigh,
                    ref priorityMasksLow,
                    ref priorityMasksHigh);
            }

            return new PlanarChunkDecoded(colorIndexesLow, colorIndexesHigh, priorityMasksLow, priorityMasksHigh);
        }

        private static void PackPlanarChunkPixel(
            int pixel,
            byte colorIndex,
            byte priorityMask,
            ref ulong colorIndexesLow,
            ref ulong colorIndexesHigh,
            ref ulong priorityMasksLow,
            ref ulong priorityMasksHigh)
        {
            var shift = (pixel & 7) * 8;
            if (pixel < 8)
            {
                colorIndexesLow |= (ulong)colorIndex << shift;
                priorityMasksLow |= (ulong)priorityMask << shift;
                return;
            }

            colorIndexesHigh |= (ulong)colorIndex << shift;
            priorityMasksHigh |= (ulong)priorityMask << shift;
        }




        private static int GetCopperWritePixelDelay(ushort offset)
        {
            // Copper palette writes reach Denise two low-res pixels after the bus event;
            // the Copper cycle itself still remains at the data-word grant.
            return offset >= 0x180 && offset < 0x1C0
                ? 2
                : 0;
        }








        private long GetCopperBlitterReadyCycle(ushort waitSecond, long currentCycle)
        {
            if ((waitSecond & 0x8000) != 0 || !_bus.Blitter.Busy)
            {
                return currentCycle;
            }

            var predicted = _bus.Blitter.GetPredictedCompletionCycle();
            return predicted > currentCycle
                ? predicted
                : currentCycle + AgnusChipSlotScheduler.SlotCycles;
        }




        private bool TryPeekPendingWrite(out PendingCustomWrite write)
        {
            if (_pendingIndex < _pendingWrites.Count)
            {
                write = _pendingWrites[_pendingIndex];
                return true;
            }

            write = default;
            return false;
        }


        private void CompactPendingWrites()
        {
            if (_pendingIndex > 1024 && _pendingIndex * 2 > _pendingWrites.Count)
            {
                _pendingWrites.RemoveRange(0, _pendingIndex);
                _pendingIndex = 0;
            }
        }


        private bool TryGetCopperWaitCycle(
            ushort first,
            ushort second,
            long frameStartCycle,
            long currentCycle,
            long frameStopCycle,
            bool blitterFinished,
            out long waitCycle)
        {
            if (!blitterFinished)
            {
                waitCycle = 0;
                return false;
            }

            GetCopperBeamPositionForCycle(frameStartCycle, currentCycle, out var startLine, out var startHorizontal);
            startHorizontal &= 0xFE;
            if (startHorizontal > LastCopperHorizontal)
            {
                startLine++;
                startHorizontal = 0;
            }

            var mask = GetCopperComparisonMask(second);
            var target = (ushort)(first & 0xFFFE);
            if (mask == 0xFFFE)
            {
                return TryGetFullMaskCopperWaitCycle(
                    target,
                    frameStartCycle,
                    currentCycle,
                    frameStopCycle,
                    startLine,
                    startHorizontal,
                    out waitCycle);
            }

            var verticalMask = mask & 0xFF00;
            var horizontalMask = mask & 0x00FE;
            var targetVertical = target & verticalMask;
            var targetHorizontal = target & horizontalMask;
            var zeroStartHorizontal = -2;
            for (var line = startLine; line < _timing.LongFrameLines; line++)
            {
                var horizontalStart = line == startLine ? startHorizontal : 0;
                var vertical = (((line & 0xFF) << 8) & verticalMask);
                int horizontal;
                if (vertical > targetVertical)
                {
                    horizontal = horizontalStart;
                }
                else if (vertical == targetVertical)
                {
                    if (line == startLine)
                    {
                        horizontal = GetFirstMaskedCopperWaitHorizontal(horizontalMask, targetHorizontal, horizontalStart);
                    }
                    else
                    {
                        if (zeroStartHorizontal == -2)
                        {
                            zeroStartHorizontal = GetFirstMaskedCopperWaitHorizontal(horizontalMask, targetHorizontal, 0);
                        }

                        horizontal = zeroStartHorizontal;
                    }
                }
                else
                {
                    continue;
                }

                if (horizontal < 0 || horizontal > LastCopperHorizontal)
                {
                    continue;
                }

                if (IsCopperWaitReleaseBlockedAtLineEnd(target, mask, line, horizontal))
                {
                    continue;
                }

                waitCycle = GetCycleForCopperBeam(frameStartCycle, line, horizontal);
                if (waitCycle < currentCycle)
                {
                    waitCycle = currentCycle;
                }

                return waitCycle < frameStopCycle;
            }

            waitCycle = 0;
            return false;
        }

        private static int GetFirstMaskedCopperWaitHorizontal(int mask, int target, int startHorizontal)
        {
            var horizontal = Math.Max(0, startHorizontal) & 0xFE;
            if (IsContiguousHighHorizontalMask(mask))
            {
                if ((horizontal & mask) >= target)
                {
                    return horizontal <= LastCopperHorizontal ? horizontal : -1;
                }

                return target <= LastCopperHorizontal ? target : -1;
            }

            for (; horizontal <= LastCopperHorizontal; horizontal += 2)
            {
                if ((horizontal & mask) >= target)
                {
                    return horizontal;
                }
            }

            return -1;
        }

        private static bool IsContiguousHighHorizontalMask(int mask)
        {
            return mask is 0x00FE or 0x00FC or 0x00F8 or 0x00F0 or 0x00E0 or 0x00C0 or 0x0080 or 0x0000;
        }

        private bool TryGetFullMaskCopperWaitCycle(
            ushort target,
            long frameStartCycle,
            long currentCycle,
            long frameStopCycle,
            int startLine,
            int startHorizontal,
            out long waitCycle)
        {
            var targetVertical = (target >> 8) & 0xFF;
            var targetHorizontal = target & 0xFE;
            for (var line = startLine; line < _timing.LongFrameLines;)
            {
                var vertical = line & 0xFF;
                int horizontal;
                if (vertical > targetVertical)
                {
                    horizontal = line == startLine ? startHorizontal : 0;
                }
                else if (vertical == targetVertical)
                {
                    horizontal = Math.Max(line == startLine ? startHorizontal : 0, targetHorizontal);
                    if ((horizontal & 1) != 0)
                    {
                        horizontal++;
                    }
                }
                else
                {
                    var candidateLine = (line & ~0xFF) + targetVertical;
                    if (candidateLine <= line)
                    {
                        candidateLine += 0x100;
                    }

                    line = candidateLine;
                    continue;
                }

                if (horizontal > LastCopperHorizontal)
                {
                    line++;
                    continue;
                }

                if (IsCopperWaitReleaseBlockedAtLineEnd(target, 0xFFFE, line, horizontal))
                {
                    line++;
                    continue;
                }

                waitCycle = GetCycleForCopperBeam(frameStartCycle, line, horizontal);
                if (waitCycle < currentCycle)
                {
                    waitCycle = currentCycle;
                }

                return waitCycle < frameStopCycle;
            }

            waitCycle = 0;
            return false;
        }

        private bool IsCopperWaitReleaseBlockedAtLineEnd(ushort target, int mask, int line, int horizontal)
        {
            if (horizontal + CopperWaitLineEndBlackoutHpUnits < CopperHorizontalUnitsPerLine)
            {
                return false;
            }

            var preBlackoutHorizontal = (CopperHorizontalUnitsPerLine - CopperWaitLineEndBlackoutHpUnits - 1) & 0xFE;
            var preBlackoutBeam = (ushort)(((line & 0xFF) << 8) | preBlackoutHorizontal);
            return (preBlackoutBeam & mask) >= (target & mask);
        }

        private bool IsCopperComparisonSatisfied(
            ushort first,
            ushort second,
            long frameStartCycle,
            long cycle,
            bool blitterFinished)
        {
            var line = GetBeamLineForCycle(frameStartCycle, cycle);
            var horizontal = GetCopperHorizontalForCycle(frameStartCycle, cycle);
            return IsCopperComparisonSatisfied(first, second, line - StandardVStart, horizontal, blitterFinished);
        }

        private static long CopperHpToCpuCycles(int hpUnits)
        {
            System.Diagnostics.Debug.Assert(hpUnits > 0, "Copper HP cycle conversion expects positive units.");
            return hpUnits * AgnusChipSlotScheduler.SlotCycles;
        }

        private static int GetCopperWaitWakeHpUnits(ushort waitSecond, bool observedBlitterBusy)
            => (waitSecond & 0x8000) == 0 && !observedBlitterBusy
                ? CopperBfdNoBusyWakeHpUnits
                : CopperWaitWakeHpUnits;

        private static long GetCopperWaitRestartArmCycle(
            long waitCycle,
            ushort waitSecond,
            bool observedBlitterBusy)
            => waitCycle + CopperHpToCpuCycles(GetCopperWaitWakeHpUnits(waitSecond, observedBlitterBusy)) -
                (2L * AgnusChipSlotScheduler.SlotCycles);


        private long GetLineStartCycle(long frameStartCycle, int line)
        {
            return _bus.GetLineStartCycle(frameStartCycle, line);
        }

        private long GetCycleForCopperBeam(long frameStartCycle, int line, int horizontal)
        {
            return GetLineStartCycle(frameStartCycle, line) + ((long)horizontal * CopperHpCycles);
        }

        private int GetBeamLineForCycle(long frameStartCycle, long cycle)
        {
            _ = frameStartCycle;
            return _bus.GetBeamPosition(cycle).BeamLine;
        }

        private int GetCopperHorizontalForCycle(long frameStartCycle, long cycle)
        {
            GetCopperBeamPositionForCycle(frameStartCycle, cycle, out _, out var horizontal);
            return horizontal;
        }

        private void GetCopperBeamPositionForCycle(long frameStartCycle, long cycle, out int line, out int horizontal)
        {
            _ = frameStartCycle;
            var beam = _bus.GetBeamPosition(cycle);
            line = beam.BeamLine;
            horizontal = beam.BeamHorizontal;
            if (horizontal > LastCopperHorizontal)
            {
                horizontal = LastCopperHorizontal;
            }
        }

        private int GetOutputRowForCycle(long frameStartCycle, long cycle)
        {
            return GetBeamLineForCycle(frameStartCycle, cycle) - StandardVStart;
        }

        private int GetOutputXForCycle(long frameStartCycle, long cycle)
        {
            return GetCopperOutputX(GetCopperHorizontalForCycle(frameStartCycle, cycle));
        }

        private int GetOutputXForCycle(long frameStartCycle, long cycle, int pixelDelay)
        {
            return GetCopperOutputX(GetCopperHorizontalForCycle(frameStartCycle, cycle), pixelDelay);
        }

        private int GetCopperOutputX(int horizontal)
        {
            return GetCopperOutputX(horizontal, 0);
        }

        private int GetCopperOutputX(int horizontal, int pixelDelay)
        {
            return GetCopperOutputXForPresentation(horizontal, pixelDelay, LowResWidth);
        }

        internal static int GetCopperOutputXForPresentation(int horizontal, int pixelDelay, int outputWidth)
        {
            var beamX = (horizontal - DefaultDdfStart) * 2;
            if (beamX < 0 && horizontal >= DefaultDdfStart - 8)
            {
                return Math.Clamp(pixelDelay, 0, outputWidth);
            }

            return Math.Clamp(beamX + pixelDelay, 0, outputWidth);
        }

        private bool IsCopperBlitterFinishedForWait(ushort second)
        {
            return (second & 0x8000) != 0 || !_bus.Blitter.Busy;
        }

        private static bool IsCopperComparisonSatisfied(
            ushort first,
            ushort second,
            int row,
            int horizontal,
            bool blitterFinished)
        {
            if (!blitterFinished)
            {
                return false;
            }

            var mask = GetCopperComparisonMask(second);
            var beam = GetCopperBeamWord(row, horizontal);
            var target = (ushort)(first & 0xFFFE);
            return (beam & mask) >= (target & mask);
        }

        private static ushort GetCopperComparisonMask(ushort second)
        {
            return (ushort)(0x8000 | (second & 0x7FFE));
        }

        private static ushort GetCopperBeamWord(int row, int horizontal)
        {
            var vertical = (row + StandardVStart) & 0xFF;
            return (ushort)((vertical << 8) | (horizontal & 0xFE));
        }


    }
}
