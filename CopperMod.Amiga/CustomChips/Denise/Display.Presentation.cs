/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        public void RenderFrame(Span<uint> bgra)
        {
            RenderFrame(bgra, 0, long.MaxValue, useTimedWrites: false);
        }

        public void RenderFrame(Span<uint> bgra, long frameStartCycle, long frameEndCycle)
        {
            RenderFrame(bgra, frameStartCycle, frameEndCycle, useTimedWrites: true);
        }

        [HotPath]
        private void RenderFrame(Span<uint> bgra, long frameStartCycle, long frameEndCycle, bool useTimedWrites)
        {
            if (bgra.Length >= Width * Height)
            {
                _renderWidth = Width;
                _renderHeight = Height;
            }
            else if (bgra.Length >= Width * ActiveLowResOutputHeight)
            {
                _renderWidth = Width;
                _renderHeight = ActiveLowResOutputHeight;
            }
            else if (bgra.Length >= LowResWidth * ActiveLowResOutputHeight)
            {
                _renderWidth = LowResWidth;
                _renderHeight = ActiveLowResOutputHeight;
            }
            else
            {
                throw new ArgumentException("The framebuffer is smaller than the PAL display.", nameof(bgra));
            }

            _bitplaneDataSpans.Clear();
            if (useTimedWrites && _bus.LiveAgnusDmaEnabled)
            {
                var frameStopCycle = GetPresentationFrameStopCycle(frameStartCycle, frameEndCycle);
                var frameCaptureStopCycle = Math.Max(frameStartCycle, frameStopCycle - 1);
                if (!_liveFrameValid ||
                    _liveFrameStartCycle != frameStartCycle ||
                    _liveCapturedThroughCycle < frameCaptureStopCycle)
                {
                    _bus.SynchronizeLiveDisplayThrough(frameCaptureStopCycle);
                }

                if (TryRenderLiveCapturedFrame(bgra, frameStartCycle, frameStopCycle))
                {
                    return;
                }

                if (TryRenderArchivedTimelineFrame(bgra, frameStartCycle, frameStopCycle))
                {
                    return;
                }

                if (TryRenderArchivedFrameWriteReplay(bgra, frameStartCycle, frameStopCycle))
                {
                    return;
                }
            }

            ApplyPendingWrites(useTimedWrites ? frameStartCycle : long.MaxValue);
            var savedPresentationState = useTimedWrites ? SaveDisplayState() : null;
            _renderInterlaceField = useTimedWrites && InterlaceEnabled
                ? GetInterlaceField(frameStartCycle)
                : 0;
            ResetFrameCounters();
            ResetPlayfieldPriorityMasks();
            _spriteFrameCommands.Clear();
            _paletteFrameSpans.Clear();
            _bitplaneDataSpans.Clear();
            _enforceDmaForFrame = useTimedWrites;
            _useTimedPresentationReads = useTimedWrites;
            _renderFrameStartCycle = frameStartCycle;
            _trackDisplayWindowState = useTimedWrites;
            ResetDisplayWindowStateTracking();
            bgra = bgra.Slice(0, _renderWidth * _renderHeight);

            _captureSpriteFrameCommands = useTimedWrites || _copperListPointer != 0;
            var renderCompleted = false;
            try
            {
                if (useTimedWrites)
                {
                    RenderTimedPresentationFrame(bgra, frameStartCycle, GetPresentationFrameStopCycle(frameStartCycle, frameEndCycle));
                }
                else if (_copperListPointer != 0 && IsCopperDmaEnabled())
                {
                    RenderCopperFrame(bgra, frameStartCycle, GetFrameStopCycle(frameStartCycle), useTimedWrites);
                }
                else
                {
                    RenderRows(bgra, 0, ActiveLowResOutputHeight, frameStartCycle, useTimedWrites);
                }

                renderCompleted = true;
            }
            finally
            {
                if (!renderCompleted && savedPresentationState != null)
                {
                    RestoreDisplayState(savedPresentationState);
                }

                _captureSpriteFrameCommands = false;
                _enforceDmaForFrame = false;
                _trackDisplayWindowState = false;
            }

            if (useTimedWrites)
            {
                var pendingIndexBeforeFrameEnd = _pendingIndex;
                ApplyPendingWrites(frameEndCycle);
                if (savedPresentationState != null && _pendingIndex != pendingIndexBeforeFrameEnd)
                {
                    CaptureDisplayState(savedPresentationState);
                }
            }

            try
            {
                RenderSprites(bgra);
            }
            finally
            {
                _useTimedPresentationReads = false;
                _bus.ClearPresentationWriteHistory();
                if (savedPresentationState != null)
                {
                    RestoreDisplayState(savedPresentationState);
                }
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

        private bool TryRenderTimelineFrame(
            Span<uint> bgra,
            long frameStartCycle,
            long frameStopCycle,
            DisplayFrameTimeline timeline,
            bool useArchivedPalette,
            bool allowStatefulFallback,
            bool archivedTimeline)
        {
            if (!timeline.IsValidForFrame(frameStartCycle))
            {
                _lastTimelineFallbackCount++;
                return false;
            }

            CompleteTimelineSpriteFetchOutcomes(timeline, frameStartCycle, frameStopCycle, allowExactCompletionReads: false);
            _lastTimelineCoalescedSegmentCount += timeline.CoalesceEquivalentSegments();
            if (!IsTimelineCompleteForRendering(
                    timeline,
                    frameStartCycle,
                    frameStopCycle,
                    requireCurrentFrameSafe: !archivedTimeline))
            {
                _lastTimelineFallbackCount++;
                return false;
            }

            var savedCurrentRenderRow = _currentRenderRow;
            var savedTrackDisplayWindowState = _trackDisplayWindowState;
            var savedDisplayWindowVerticallyOpen = _displayWindowVerticallyOpen;
            var savedDisplayWindowStateLine = _displayWindowStateLine;
            var savedRenderingArchivedTimeline = _renderingArchivedTimeline;
            _renderingArchivedTimeline = useArchivedPalette;
            _trackDisplayWindowState = true;
            _bitplaneDataSpans.Clear();
            timeline.CopyBitplaneDataSpansTo(_bitplaneDataSpans);
            try
            {
                var rowStop = GetTimelineRowStop(frameStartCycle, frameStopCycle);
                for (var row = 0; row < rowStop; row++)
                {
                    var line = timeline.GetLine(row);
                    if (TryRenderTimelineLowResLineFastPath(bgra, row, line, timeline))
                    {
                        _lastTimelineFastPathRowCount++;
                        continue;
                    }

                    _lastTimelineFastPathMissCount++;
                    for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
                    {
                        var segment = line.Segments[segmentIndex];
                        if (segment.XStop <= segment.XStart)
                        {
                            continue;
                        }

                        var state = timeline.GetState(segment.StateIndex);
                        ApplyTimelineStateForRendering(state);
                        _displayWindowVerticallyOpen = state.DisplayWindowVerticallyOpen;
                        _displayWindowStateLine = StandardVStart + row + 1;
                        _currentRenderRow = row;
                        CapturePaletteFrameSpans(row, row + 1, segment.XStart, segment.XStop);
                        FillRows(bgra, row, row + 1, segment.XStart, segment.XStop);
                        if (!TryRenderTimelineCachedBitplanes(bgra, row, segment, state, timeline))
                        {
                            _lastTimelineMissingBitplaneFallbackCount++;
                            if (!allowStatefulFallback)
                            {
                                _lastTimelineFallbackCount++;
                                return false;
                            }

                            RenderBitplanes(bgra, row, row + 1, segment.XStart, segment.XStop);
                        }
                    }
                }

                RenderPresentationTrailingRows(bgra, frameStartCycle, frameStopCycle, useTimedWrites: true);
                RenderTimelineSprites(bgra, timeline);
                _lastTimelineSegmentCount = timeline.SegmentCount;
                _lastTimelineSpriteCommandCount = timeline.SpriteCommandCount;
                _lastPlanarChunkCacheHits = timeline.PlanarChunkCacheHits;
                _lastPlanarChunkCacheMisses = timeline.PlanarChunkCacheMisses;
                _lastSpriteDeniedFetchCount = timeline.RecalculateSpriteDeniedFetchCount();
                if (archivedTimeline)
                {
                    _lastArchivedTimelineFrameCount++;
                }
                else
                {
                    _lastActiveTimelineFrameCount++;
                }

                return true;
            }
            finally
            {
                _currentRenderRow = savedCurrentRenderRow;
                _trackDisplayWindowState = savedTrackDisplayWindowState;
                _displayWindowVerticallyOpen = savedDisplayWindowVerticallyOpen;
                _displayWindowStateLine = savedDisplayWindowStateLine;
                _renderingArchivedTimeline = savedRenderingArchivedTimeline;
            }
        }

        private int GetTimelineRowStop(long frameStartCycle, long frameStopCycle)
        {
            var rowStop = ActiveLowResOutputHeight;
            if (frameStopCycle < GetFrameStopCycle(frameStartCycle))
            {
                rowStop = Math.Clamp(GetOutputRowForCycle(frameStartCycle, frameStopCycle) + 1, 0, ActiveLowResOutputHeight);
            }

            return rowStop;
        }

        private bool IsTimelineCompleteForRendering(
            DisplayFrameTimeline timeline,
            long frameStartCycle,
            long frameStopCycle,
            bool requireCurrentFrameSafe = true)
        {
            return GetTimelineRejectReason(timeline, frameStartCycle, frameStopCycle, requireCurrentFrameSafe) == TimelineRejectReason.None;
        }

        private TimelineRejectReason GetTimelineRejectReason(
            DisplayFrameTimeline timeline,
            long frameStartCycle,
            long frameStopCycle,
            bool requireCurrentFrameSafe = true)
        {
            if (requireCurrentFrameSafe && _liveTimelineUnsafeForFrame)
            {
                return TimelineRejectReason.UnsafeWrite;
            }

            if (timeline.SegmentCount > MaxTimelineSegmentsPerFrame)
            {
                return TimelineRejectReason.SegmentCapacity;
            }

            var rowStop = GetTimelineRowStop(frameStartCycle, frameStopCycle);
            var checkFrameStop = frameStopCycle < GetFrameStopCycle(frameStartCycle);
            for (var row = 0; row < rowStop; row++)
            {
                if (checkFrameStop &&
                    GetOutputRowStartCycle(frameStartCycle, row) >= frameStopCycle)
                {
                    break;
                }

                if (!timeline.HasLine(row))
                {
                    return TimelineRejectReason.MissingLine;
                }

                var line = timeline.GetLine(row);
                if (line.UnsafeForTimelineRender || line.SegmentCount <= 0)
                {
                    return TimelineRejectReason.UnsafeLine;
                }
            }

            if (!IsTimelineSpriteCompleteForRendering(timeline, frameStartCycle, frameStopCycle))
            {
                return TimelineRejectReason.MissingSpriteFetch;
            }

            return TimelineRejectReason.None;
        }

        private bool IsTimelineSpriteCompleteForRendering(DisplayFrameTimeline timeline, long frameStartCycle, long frameStopCycle)
        {
            var rowStop = GetTimelineRowStop(frameStartCycle, frameStopCycle);
            var checkFrameStop = frameStopCycle < GetFrameStopCycle(frameStartCycle);
            for (var spriteIndex = 0; spriteIndex < _sprites.Length; spriteIndex++)
            {
                var commands = GetTimelineSpriteFrameCommands(spriteIndex, timeline);
                for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    var command = commands[commandIndex];
                    var sprite = command.Descriptor;
                    if (!sprite.IsDma)
                    {
                        continue;
                    }

                    var yStart = Math.Max(Math.Max(sprite.YStart, command.Row), 0);
                    var yStop = Math.Min(Math.Min(sprite.YStop, rowStop), ActiveLowResOutputHeight);
                    for (var y = yStart; y < yStop; y++)
                    {
                        if (checkFrameStop &&
                            GetOutputRowStartCycle(frameStartCycle, y) >= frameStopCycle)
                        {
                            break;
                        }

                        var statusA = timeline.GetSpriteFetchStatus(y, spriteIndex, 0);
                        var statusB = timeline.GetSpriteFetchStatus(y, spriteIndex, 1);
                        if (statusA == TimelineFetchStatus.NotAttempted ||
                            statusB == TimelineFetchStatus.NotAttempted)
                        {
                            if (statusA == TimelineFetchStatus.NotAttempted &&
                                IsTimelineSpriteSlotUnavailable(timeline, y, spriteIndex, 0))
                            {
                                timeline.RecordSpriteDataFetch(
                                    y,
                                    spriteIndex,
                                    0,
                                    0,
                                    granted: false);
                            }

                            if (statusB == TimelineFetchStatus.NotAttempted &&
                                IsTimelineSpriteSlotUnavailable(timeline, y, spriteIndex, 1))
                            {
                                timeline.RecordSpriteDataFetch(
                                    y,
                                    spriteIndex,
                                    1,
                                    0,
                                    granted: false);
                            }

                            statusA = timeline.GetSpriteFetchStatus(y, spriteIndex, 0);
                            statusB = timeline.GetSpriteFetchStatus(y, spriteIndex, 1);
                            if (statusA != TimelineFetchStatus.NotAttempted &&
                                statusB != TimelineFetchStatus.NotAttempted)
                            {
                                continue;
                            }

                            var missingWord = statusA == TimelineFetchStatus.NotAttempted ? 0 : 1;
                            CaptureMissingSpriteRejectDiagnostic(
                                spriteIndex,
                                y,
                                missingWord,
                                statusA,
                                statusB,
                                command,
                                timeline);
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private void CompleteTimelineSpriteFetchOutcomes(
            DisplayFrameTimeline timeline,
            long frameStartCycle,
            long frameStopCycle,
            bool allowExactCompletionReads)
        {
            var rowStop = GetTimelineRowStop(frameStartCycle, frameStopCycle);
            var checkFrameStop = frameStopCycle < GetFrameStopCycle(frameStartCycle);
            for (var spriteIndex = 0; spriteIndex < _sprites.Length; spriteIndex++)
            {
                var commands = GetTimelineSpriteFrameCommands(spriteIndex, timeline);
                for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    var command = commands[commandIndex];
                    var sprite = command.Descriptor;
                    if (!sprite.IsDma)
                    {
                        continue;
                    }

                    var yStart = Math.Max(Math.Max(sprite.YStart, command.Row), 0);
                    var yStop = Math.Min(Math.Min(sprite.YStop, rowStop), ActiveLowResOutputHeight);
                    for (var y = yStart; y < yStop; y++)
                    {
                        if (checkFrameStop &&
                            GetOutputRowStartCycle(frameStartCycle, y) >= frameStopCycle)
                        {
                            break;
                        }

                        for (var word = 0; word < LiveSpriteWordsPerChannel; word++)
                        {
                            var status = timeline.GetSpriteFetchStatus(y, spriteIndex, word);
                            if (status == TimelineFetchStatus.Denied && word == 1)
                            {
                                var value = GetDeniedSpriteDataLatch(timeline, command, y, spriteIndex, word);
                                if (value != timeline.GetSpriteWord(y, spriteIndex, word))
                                {
                                    timeline.RecordSpriteDataFetch(
                                        y,
                                        spriteIndex,
                                        word,
                                        value,
                                        granted: false);
                                }
                            }

                            if (status == TimelineFetchStatus.NotAttempted &&
                                IsTimelineSpriteSlotUnavailable(timeline, y, spriteIndex, word))
                            {
                                var value = GetDeniedSpriteDataLatch(timeline, command, y, spriteIndex, word);
                                timeline.RecordSpriteDataFetch(
                                    y,
                                    spriteIndex,
                                    word,
                                    value,
                                    granted: false);
                                continue;
                            }

                            if (allowExactCompletionReads &&
                                timeline.GetSpriteFetchStatus(y, spriteIndex, word) == TimelineFetchStatus.NotAttempted)
                            {
                                CompleteTimelineSpriteFetchFromExactSlot(
                                    timeline,
                                    frameStartCycle,
                                    frameStopCycle,
                                    command,
                                    y,
                                    spriteIndex,
                                    word);
                            }
                        }
                    }
                }
            }
        }

        private void CompleteTimelineSpriteFetchFromExactSlot(
            DisplayFrameTimeline timeline,
            long frameStartCycle,
            long frameStopCycle,
            SpriteFrameCommand command,
            int row,
            int spriteIndex,
            int word)
        {
            var fetchCycle = GetSpriteDmaFetchCycle(frameStartCycle, row, spriteIndex, word);
            if (fetchCycle >= frameStopCycle)
            {
                return;
            }

            var sprite = command.Descriptor;
            if (!sprite.IsDma ||
                row < Math.Max(sprite.YStart, command.Row) ||
                row >= sprite.YStop)
            {
                return;
            }

            var address = AddDmaPointerOffset(sprite.DataAddress, ((row - sprite.YStart) * 4) + (word * 2));
            if (!_bus.TryReadDisplayDmaWordForPresentation(
                    AmigaBusRequester.Sprite,
                    AmigaBusAccessKind.Sprite,
                    address,
                    fetchCycle,
                    out var value,
                    out var access))
            {
                timeline.RecordSpriteDataFetch(
                    row,
                    spriteIndex,
                    word,
                    0,
                    granted: false);
                return;
            }

            RecordLiveDisplayDmaCycle(access.GrantedCycle);
            timeline.RecordSpriteDataFetch(row, spriteIndex, word, value, granted: true);
        }

        private bool TryRecoverTimelineSpriteFetch(
            DisplayFrameTimeline timeline,
            long frameStartCycle,
            long frameStopCycle,
            SpriteFrameCommand command,
            int row,
            int spriteIndex,
            int word)
        {
            _lastSpriteRecoveryAttemptCount++;
            var fetchCycle = GetSpriteDmaFetchCycle(frameStartCycle, row, spriteIndex, word);
            if (fetchCycle >= frameStopCycle)
            {
                return false;
            }

            if (IsTimelineSpriteSlotUnavailable(timeline, row, spriteIndex, word))
            {
                var deniedValue = GetDeniedSpriteDataLatch(timeline, command, row, spriteIndex, word);
                timeline.RecordSpriteDataFetch(
                    row,
                    spriteIndex,
                    word,
                    deniedValue,
                    granted: false);
                return true;
            }

            var sprite = command.Descriptor;
            if (!sprite.IsDma ||
                row < Math.Max(sprite.YStart, command.Row) ||
                row >= sprite.YStop)
            {
                return false;
            }

            var address = AddDmaPointerOffset(sprite.DataAddress, ((row - sprite.YStart) * 4) + (word * 2));
            if (!_bus.TryReadDisplayDmaWordForPresentation(
                    AmigaBusRequester.Sprite,
                    AmigaBusAccessKind.Sprite,
                    address,
                    fetchCycle,
                    out var value,
                    out var access))
            {
                timeline.RecordSpriteDataFetch(
                    row,
                    spriteIndex,
                    word,
                    0,
                    granted: false);
                return true;
            }

            RecordLiveDisplayDmaCycle(access.GrantedCycle);
            timeline.RecordSpriteDataFetch(row, spriteIndex, word, value, granted: true);
            return true;
        }

        private static bool HasPriorTimelineSpriteDatb(
            DisplayFrameTimeline timeline,
            SpriteFrameCommand command,
            int row,
            int spriteIndex)
            => TryGetPriorTimelineSpriteDatb(timeline, command, row, spriteIndex, out _);

        private static ushort GetDeniedSpriteDataLatch(
            DisplayFrameTimeline timeline,
            SpriteFrameCommand command,
            int row,
            int spriteIndex,
            int word)
        {
            return word == 1 &&
                TryGetPriorTimelineSpriteDatb(timeline, command, row, spriteIndex, out var value)
                    ? value
                    : (ushort)0;
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

        private bool IsTimelineSpriteSlotUnavailable(DisplayFrameTimeline timeline, int row, int spriteIndex, int word)
        {
            if (!TryGetTimelineStateForSpriteSlot(timeline, row, spriteIndex, word, out var state))
            {
                return false;
            }

            return !IsSpriteDmaEnabled(state.Dmacon) || !IsSpriteDmaSlotAvailable(state, spriteIndex, word);
        }

        private bool TryGetTimelineStateForSpriteSlot(
            DisplayFrameTimeline timeline,
            int row,
            int spriteIndex,
            int word,
            out DisplayTimelineState state)
        {
            state = null!;
            if (!timeline.HasLine(row))
            {
                return false;
            }

            var line = timeline.GetLine(row);
            if (line.SegmentCount <= 0)
            {
                return false;
            }

            var lineStart = GetOutputRowStartCycle(_renderFrameStartCycle, row);
            var firstSpriteSlot = GetFirstSpriteDmaSlotCycle(lineStart);
            var firstSpriteHorizontal = (int)((firstSpriteSlot - lineStart) / CopperHpCycles);
            var horizontal = firstSpriteHorizontal + (spriteIndex * 4) + (word * 2);
            var x = GetCopperOutputX(horizontal);
            for (var i = 0; i < line.SegmentCount; i++)
            {
                var segment = line.Segments[i];
                if (x >= segment.XStart && x < segment.XStop)
                {
                    state = timeline.GetState(segment.StateIndex);
                    return true;
                }
            }

            state = timeline.GetState(line.Segments[line.SegmentCount - 1].StateIndex);
            return true;
        }

        private void CaptureMissingSpriteRejectDiagnostic(
            int spriteIndex,
            int row,
            int word,
            TimelineFetchStatus statusA,
            TimelineFetchStatus statusB,
            SpriteFrameCommand command,
            DisplayFrameTimeline timeline)
        {
            _lastArchiveRejectMissingSpriteIndex = spriteIndex;
            _lastArchiveRejectMissingSpriteRow = row;
            _lastArchiveRejectMissingSpriteWord = word;
            _lastArchiveRejectMissingSpriteStatusA = (int)statusA;
            _lastArchiveRejectMissingSpriteStatusB = (int)statusB;
            _lastArchiveRejectMissingSpriteCommandRow = command.Row;
            _lastArchiveRejectMissingSpriteYStart = command.Descriptor.YStart;
            _lastArchiveRejectMissingSpriteYStop = command.Descriptor.YStop;
            _lastArchiveRejectMissingSpritePreviousStatusA = row > 0
                ? (int)timeline.GetSpriteFetchStatus(row - 1, spriteIndex, 0)
                : -1;
            _lastArchiveRejectMissingSpritePreviousStatusB = row > 0
                ? (int)timeline.GetSpriteFetchStatus(row - 1, spriteIndex, 1)
                : -1;
            if (TryGetTimelineStateForSpriteSlot(timeline, row, spriteIndex, word, out var state))
            {
                _lastArchiveRejectMissingSpriteUsableChannels = GetUsableSpriteDmaChannelCount(state);
                _lastArchiveRejectMissingSpriteDdfStart = state.DataFetchStart;
                _lastArchiveRejectMissingSpriteDmacon = state.Dmacon;
                _lastArchiveRejectMissingSpriteBplcon0 = state.Bplcon0;
            }
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
                    if (timeline.GetBitplaneFetchStatus(row, plane, word) == TimelineFetchStatus.NotAttempted)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void ApplyTimelineStateForRendering(DisplayTimelineState state)
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
            if (_lastAppliedLivePaletteSnapshotIndex != state.PaletteSnapshotIndex)
            {
                var paletteSnapshotCount = _renderingArchivedTimeline
                    ? _archivedPaletteSnapshotCount
                    : _livePaletteSnapshotCount;
                var paletteColors = _renderingArchivedTimeline
                    ? _archivedPaletteSnapshotColors
                    : _livePaletteSnapshotColors;
                var convertedPaletteColors = _renderingArchivedTimeline
                    ? _archivedPaletteSnapshotConvertedColors
                    : _livePaletteSnapshotConvertedColors;
                var paletteIndex = Math.Clamp(state.PaletteSnapshotIndex, 0, Math.Max(0, paletteSnapshotCount - 1));
                Array.Copy(paletteColors, paletteIndex * _colors.Length, _colors, 0, _colors.Length);
                Array.Copy(convertedPaletteColors, paletteIndex * PaletteColorCount, _convertedColors, 0, PaletteColorCount);
                _lastAppliedLivePaletteSnapshotIndex = state.PaletteSnapshotIndex;
            }

            Array.Copy(state.BitplanePointers, _bitplanePointers, _bitplanePointers.Length);
            Array.Copy(state.BitplaneBaseRows, _bitplaneBaseRows, _bitplaneBaseRows.Length);
            Array.Copy(state.BitplaneDataRegisters, _bitplaneDataRegisters, _bitplaneDataRegisters.Length);
        }

        private bool TryRenderTimelineLowResLineFastPath(
            Span<uint> bgra,
            int row,
            DisplayLineTimeline line,
            DisplayFrameTimeline timeline)
        {
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
            ApplyTimelineStateForRendering(indexState);
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
                ApplyTimelineStateForRendering(state);
                _displayWindowVerticallyOpen = state.DisplayWindowVerticallyOpen;
                _displayWindowStateLine = StandardVStart + row + 1;
                _currentRenderRow = row;
                CapturePaletteFrameSpans(row, row + 1, segment.XStart, segment.XStop);
                FillRows(bgra, row, row + 1, segment.XStart, segment.XStop);
                WritePreparedTimelineLowResFastBitplanes(bgra, row, segment.XStart, segment.XStop, dataFirstX, dataLastX);
            }

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
                HasSameBitplaneDataRegisters(left, right);
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
            int dataLastX)
        {
            var xStart = Math.Max(segmentXStart, dataFirstX);
            var xStop = Math.Min(segmentXStop, dataLastX);
            for (var x = xStart; x < xStop; x++)
            {
                WriteLowResolutionOutputPixel(bgra, x, row, _convertedColors[_timelineFastPathColorIndexes[x]]);
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
                            if (!TryGetTimelineDecodedChunk(row, word, state, planeCount, dualPlayfield: false, timeline, out var chunk))
                            {
                                return false;
                            }

                            var offset = relativeSubPixel & 0x0F;
                            leftColorIndex = chunk.GetColorIndex(offset);
                            rightColorIndex = chunk.GetColorIndex(offset + 1);
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

                    if (!TryGetTimelineDecodedChunk(row, word, state, planeCount, dualPlayfield, timeline, out var chunk))
                    {
                        return false;
                    }

                    var offset = scrolledRelativeX & 0x0F;
                    colorIndex = chunk.GetColorIndex(offset);
                    priorityMask = chunk.GetPriorityMask(offset);
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

            var status = timeline.GetBitplaneFetchStatus(row, plane, word);
            if (status == TimelineFetchStatus.NotAttempted)
            {
                data = 0;
                return false;
            }

            data = timeline.GetBitplaneWord(row, plane, word);
            return true;
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

                var status = timeline.GetBitplaneFetchStatus(row, plane, word);
                if (status == TimelineFetchStatus.NotAttempted)
                {
                    chunk = default;
                    return false;
                }

                words[plane] = timeline.GetBitplaneWord(row, plane, word);
            }

            var key = new PlanarChunkKey(
                state.Bplcon0,
                state.Bplcon2,
                planeCount,
                dualPlayfield,
                planeHasRowMask,
                words[0],
                words[1],
                words[2],
                words[3],
                words[4],
                words[5]);
            if (timeline.TryGetPlanarChunk(key, out chunk))
            {
                return true;
            }

            chunk = DecodePlanarChunk(words, planeHasRowMask, planeCount, dualPlayfield);
            timeline.StorePlanarChunk(key, chunk);
            return true;
        }

        private PlanarChunkDecoded DecodePlanarChunk(
            Span<ushort> words,
            byte planeHasRowMask,
            int planeCount,
            bool dualPlayfield)
        {
            var colorIndexesLow = 0UL;
            var colorIndexesHigh = 0UL;
            var priorityMasksLow = 0UL;
            var priorityMasksHigh = 0UL;
            for (var pixel = 0; pixel < PlanarChunkPixels; pixel++)
            {
                var bit = 15 - pixel;
                var rawColorIndex = 0;
                for (var plane = 0; plane < planeCount; plane++)
                {
                    if ((planeHasRowMask & (1 << plane)) == 0)
                    {
                        continue;
                    }

                    rawColorIndex |= ((words[plane] >> bit) & 1) << plane;
                }

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

        private bool TryRenderArchivedTimelineFrame(Span<uint> bgra, long frameStartCycle, long frameStopCycle)
        {
            if (!_archivedTimelineValid ||
                _archivedTimelineFrameStartCycle != frameStartCycle ||
                _archivedTimelineFrameStopCycle < frameStopCycle)
            {
                return false;
            }

            var saved = SaveDisplayState();
            ResetFrameCounters();
            ResetPlayfieldPriorityMasks();
            _bitplaneDataSpans.Clear();
            _paletteFrameSpans.Clear();
            _renderInterlaceField = InterlaceEnabled
                ? GetInterlaceField(frameStartCycle)
                : 0;
            _renderFrameStartCycle = frameStartCycle;
            _renderingLiveCapture = false;
            _useTimedPresentationReads = true;
            _enforceDmaForFrame = true;
            _captureSpriteFrameCommands = false;
            _lastAppliedLivePaletteSnapshotIndex = -1;
            bgra = bgra.Slice(0, _renderWidth * _renderHeight);
            var rendered = false;

            try
            {
                rendered = TryRenderTimelineFrame(
                    bgra,
                    frameStartCycle,
                    frameStopCycle,
                    _archivedDisplayTimeline,
                    useArchivedPalette: true,
                    allowStatefulFallback: false,
                    archivedTimeline: true);
                return rendered;
            }
            finally
            {
                RestoreDisplayState(saved);
                _renderingLiveCapture = false;
                _useTimedPresentationReads = false;
                _enforceDmaForFrame = false;
                _lastAppliedLivePaletteSnapshotIndex = -1;
                if (rendered)
                {
                    _bus.ClearPresentationWriteHistory();
                }
            }
        }

        private bool TryRenderLiveCapturedFrame(Span<uint> bgra, long frameStartCycle, long frameStopCycle)
        {
            if (!_liveFrameValid ||
                _liveFrameStartCycle != frameStartCycle ||
                _liveCapturedThroughCycle < Math.Max(frameStartCycle, frameStopCycle - 1))
            {
                return false;
            }

            if (!IsLiveCaptureCompleteForRendering(frameStopCycle) &&
                !IsTimelineCompleteForRendering(_displayTimeline, frameStartCycle, frameStopCycle))
            {
                return false;
            }

            var saved = SaveDisplayState();
            ResetFrameCounters();
            ResetPlayfieldPriorityMasks();
            _bitplaneDataSpans.Clear();
            _paletteFrameSpans.Clear();
            _renderInterlaceField = InterlaceEnabled
                ? GetInterlaceField(frameStartCycle)
                : 0;
            _renderFrameStartCycle = frameStartCycle;
            _renderingLiveCapture = true;
            _useTimedPresentationReads = true;
            _enforceDmaForFrame = true;
            _captureSpriteFrameCommands = false;
            _lastAppliedLivePaletteSnapshotIndex = -1;
            var savedTrackDisplayWindowState = _trackDisplayWindowState;
            var savedDisplayWindowVerticallyOpen = _displayWindowVerticallyOpen;
            var savedDisplayWindowStateLine = _displayWindowStateLine;
            var savedRenderingCopperFrame = _renderingCopperFrame;
            _trackDisplayWindowState = true;
            bgra = bgra.Slice(0, _renderWidth * _renderHeight);
            var renderedByTimeline = false;

            try
            {
                renderedByTimeline = TryRenderTimelineFrame(
                    bgra,
                    frameStartCycle,
                    frameStopCycle,
                    _displayTimeline,
                    useArchivedPalette: false,
                    allowStatefulFallback: true,
                    archivedTimeline: false);
                if (!renderedByTimeline)
                {
                    if (!_liveTimelineUnsafeRequiresCapturedRows &&
                        !_liveFrameHasLateDisplayWindowWrites &&
                        _liveFrameInitialStateValid &&
                        !_liveFrameWriteOverflowed)
                    {
                        RenderTimedWriteReplayFrame(
                            bgra,
                            frameStartCycle,
                            frameStopCycle,
                            _liveFrameInitialState,
                            _liveFrameWrites);
                        _renderingCopperFrame = savedRenderingCopperFrame;
                    }
                    else
                    {
                        RenderLiveCapturedRows(bgra);
                    }
                }

                RestoreDisplayState(saved);
                _trackDisplayWindowState = savedTrackDisplayWindowState;
                _displayWindowVerticallyOpen = savedDisplayWindowVerticallyOpen;
                _displayWindowStateLine = savedDisplayWindowStateLine;
                if (!renderedByTimeline)
                {
                    RenderSprites(bgra);
                }

                _lastBitplaneDmaFetches = Math.Max(_liveBitplaneDmaFetches, CountLiveBitplaneFetches());
                _lastSpriteDmaFetches = Math.Max(_lastSpriteDmaFetches, _liveSpriteDmaFetches);
                _lastMissedSpriteDmaSlots = Math.Max(_lastMissedSpriteDmaSlots, _liveMissedSpriteDmaSlots);
                if (_liveFirstDisplayDmaCycle >= 0 && (_lastFirstDisplayDmaCycle < 0 || _liveFirstDisplayDmaCycle < _lastFirstDisplayDmaCycle))
                {
                    _lastFirstDisplayDmaCycle = _liveFirstDisplayDmaCycle;
                }

                _lastLastDisplayDmaCycle = Math.Max(_lastLastDisplayDmaCycle, _liveLastDisplayDmaCycle);
                return true;
            }
            finally
            {
                RestoreDisplayState(saved);
                _renderingLiveCapture = false;
                _useTimedPresentationReads = false;
                _enforceDmaForFrame = false;
                _renderingCopperFrame = savedRenderingCopperFrame;
                _trackDisplayWindowState = savedTrackDisplayWindowState;
                _displayWindowVerticallyOpen = savedDisplayWindowVerticallyOpen;
                _displayWindowStateLine = savedDisplayWindowStateLine;
                _lastAppliedLivePaletteSnapshotIndex = -1;
                _bus.ClearPresentationWriteHistory();
            }
        }

        private bool TryRenderArchivedFrameWriteReplay(Span<uint> bgra, long frameStartCycle, long frameStopCycle)
        {
            if (!_archivedFrameWritesValid ||
                _archivedFrameWritesStartCycle != frameStartCycle ||
                _archivedFrameWritesStopCycle < frameStopCycle)
            {
                return false;
            }

            var saved = SaveDisplayState();
            ResetFrameCounters();
            ResetPlayfieldPriorityMasks();
            _bitplaneDataSpans.Clear();
            _paletteFrameSpans.Clear();
            _renderInterlaceField = InterlaceEnabled
                ? GetInterlaceField(frameStartCycle)
                : 0;
            _renderFrameStartCycle = frameStartCycle;
            _renderingLiveCapture = false;
            _useTimedPresentationReads = true;
            _enforceDmaForFrame = true;
            _captureSpriteFrameCommands = false;
            _lastAppliedLivePaletteSnapshotIndex = -1;
            var savedTrackDisplayWindowState = _trackDisplayWindowState;
            var savedDisplayWindowVerticallyOpen = _displayWindowVerticallyOpen;
            var savedDisplayWindowStateLine = _displayWindowStateLine;
            var savedRenderingCopperFrame = _renderingCopperFrame;
            _trackDisplayWindowState = true;
            bgra = bgra.Slice(0, _renderWidth * _renderHeight);
            var rendered = false;

            try
            {
                RenderTimedWriteReplayFrame(
                    bgra,
                    frameStartCycle,
                    frameStopCycle,
                    _archivedFrameInitialState,
                    _archivedFrameWrites);
                RestoreDisplayState(saved);
                _trackDisplayWindowState = savedTrackDisplayWindowState;
                _displayWindowVerticallyOpen = savedDisplayWindowVerticallyOpen;
                _displayWindowStateLine = savedDisplayWindowStateLine;
                _renderingCopperFrame = savedRenderingCopperFrame;
                RenderSprites(bgra);
                rendered = true;
                return true;
            }
            finally
            {
                RestoreDisplayState(saved);
                _renderingLiveCapture = false;
                _useTimedPresentationReads = false;
                _enforceDmaForFrame = false;
                _renderingCopperFrame = savedRenderingCopperFrame;
                _trackDisplayWindowState = savedTrackDisplayWindowState;
                _displayWindowVerticallyOpen = savedDisplayWindowVerticallyOpen;
                _displayWindowStateLine = savedDisplayWindowStateLine;
                _lastAppliedLivePaletteSnapshotIndex = -1;
                if (rendered)
                {
                    _bus.ClearPresentationWriteHistory();
                }
            }
        }

        private void RenderTimedWriteReplayFrame(
            Span<uint> bgra,
            long frameStartCycle,
            long frameStopCycle,
            SavedDisplayState initialState,
            List<PendingCustomWrite> writes)
        {
            RestoreDisplayState(initialState);
            ResetDisplayWindowStateTracking();
            _renderingCopperFrame = true;
            _currentCopperRow = GetOutputRowForCycle(frameStartCycle, frameStartCycle);
            var renderCursorCycle = frameStartCycle;
            var renderCursorPixelDelay = 0;
            for (var i = 0; i < writes.Count; i++)
            {
                var write = writes[i];
                if (write.Cycle >= frameStopCycle)
                {
                    break;
                }

                if (write.Cycle <= frameStartCycle)
                {
                    continue;
                }

                var writePixelDelay = GetPresentationWritePixelDelay(write);
                RenderPresentationSpan(
                    bgra,
                    frameStartCycle,
                    renderCursorCycle,
                    write.Cycle,
                    useTimedWrites: true,
                    renderCursorPixelDelay,
                    writePixelDelay);
                renderCursorCycle = Math.Max(renderCursorCycle, write.Cycle);
                renderCursorPixelDelay = writePixelDelay;
                ApplyLivePresentationReplayWrite(write, frameStartCycle);
            }

            RenderPresentationSpan(
                bgra,
                frameStartCycle,
                renderCursorCycle,
                frameStopCycle,
                useTimedWrites: true,
                renderCursorPixelDelay,
                toPixelDelay: 0);
            RenderPresentationTrailingRows(bgra, frameStartCycle, frameStopCycle, useTimedWrites: true);
        }

        private void ApplyLivePresentationReplayWrite(PendingCustomWrite write, long frameStartCycle)
        {
            _currentCopperRow = GetOutputRowForCycle(frameStartCycle, write.Cycle);
            if (_trackDisplayWindowState)
            {
                AdvanceDisplayWindowStateToCycle(frameStartCycle, write.Cycle);
            }

            ApplyWrite(write.Offset, write.Value, write.Cycle);
        }

        private static int GetPresentationWritePixelDelay(PendingCustomWrite write)
        {
            return write.IsCopper
                ? GetCopperWritePixelDelay(write.Offset)
                : 0;
        }

        private static int GetCopperWritePixelDelay(ushort offset)
        {
            // Copper palette writes reach Denise two low-res pixels after the bus event;
            // the Copper cycle itself still remains at the data-word grant.
            return offset >= 0x180 && offset < 0x1C0
                ? 2
                : 0;
        }

        private void RenderLiveCapturedRows(Span<uint> bgra)
        {
            for (var row = 0; row < ActiveLowResOutputHeight; row++)
            {
                var state = _liveLineStates[row];
                if (!IsLiveLineValid(row))
                {
                    FillRows(bgra, row, row + 1);
                    continue;
                }

                ApplyLiveLineStateForRendering(state);
                _displayWindowVerticallyOpen = state.DeniseDisplayWindowVerticallyOpen;
                _displayWindowStateLine = StandardVStart + row + 1;
                _currentRenderRow = row;
                CapturePaletteFrameSpans(row, row + 1, 0, LowResWidth);
                FillRows(bgra, row, row + 1);
                RenderBitplanes(bgra, row, row + 1);
            }

            _currentRenderRow = -1;
        }

        private void RenderTimedPresentationFrame(Span<uint> bgra, long frameStartCycle, long frameStopCycle)
        {
            RenderCopperFrame(bgra, frameStartCycle, frameStopCycle, useTimedWrites: true);
        }

        private void RenderCopperFrame(Span<uint> bgra, long frameStartCycle, long frameStopCycle, bool useTimedWrites)
        {
            _renderingCopperFrame = true;
            _currentCopperRow = GetOutputRowForCycle(frameStartCycle, frameStartCycle);
            var renderCursorCycle = frameStartCycle;
            var renderCursorPixelDelay = 0;
            var copper = new CopperPresentationState(_copperListPointer, frameStartCycle);
            var safetyRemaining = GetCopperFrameInstructionLimit(frameStartCycle, frameStopCycle);

            try
            {
                while (copper.Cycle < frameStopCycle)
                {
                    if (TryPeekPendingWrite(out var pending) && pending.Cycle <= copper.Cycle)
                    {
                        RenderPresentationSpan(
                            bgra,
                            frameStartCycle,
                            renderCursorCycle,
                            pending.Cycle,
                            useTimedWrites,
                            renderCursorPixelDelay,
                            toPixelDelay: 0);
                        renderCursorCycle = Math.Max(renderCursorCycle, pending.Cycle);
                        renderCursorPixelDelay = 0;
                        ApplyTimedPendingWrite(ref copper);
                        continue;
                    }

                    if (copper.Stopped || !IsCopperDmaEnabled())
                    {
                        if (!TryAdvanceCopperToNextPendingWrite(
                            bgra,
                            frameStartCycle,
                            frameStopCycle,
                            useTimedWrites,
                            ref renderCursorCycle,
                            ref renderCursorPixelDelay,
                            ref copper))
                        {
                            break;
                        }

                        continue;
                    }

                    if (copper.Waiting)
                    {
                        if (!TryAdvanceCopperWait(
                            bgra,
                            frameStartCycle,
                            frameStopCycle,
                            useTimedWrites,
                            ref renderCursorCycle,
                            ref renderCursorPixelDelay,
                            ref copper))
                        {
                            break;
                        }

                        continue;
                    }

                    if (safetyRemaining-- <= 0)
                    {
                        break;
                    }

                    StepCopperInstruction(
                        bgra,
                        frameStartCycle,
                        frameStopCycle,
                        useTimedWrites,
                        ref renderCursorCycle,
                        ref renderCursorPixelDelay,
                        ref copper);
                }

                RenderPresentationSpan(
                    bgra,
                    frameStartCycle,
                    renderCursorCycle,
                    frameStopCycle,
                    useTimedWrites,
                    renderCursorPixelDelay,
                    toPixelDelay: 0);
                RenderPresentationTrailingRows(bgra, frameStartCycle, frameStopCycle, useTimedWrites);
            }
            finally
            {
                _renderingCopperFrame = false;
                _currentCopperRow = 0;
            }
        }

        private void RenderPresentationTrailingRows(Span<uint> bgra, long frameStartCycle, long frameStopCycle, bool useTimedWrites)
        {
            var finalLine = GetBeamLineForCycle(frameStartCycle, Math.Max(frameStartCycle, frameStopCycle - 1));
            var firstTrailingRow = Math.Clamp(finalLine - StandardVStart + 1, 0, ActiveLowResOutputHeight);
            if (firstTrailingRow >= ActiveLowResOutputHeight)
            {
                return;
            }

            RenderRows(
                bgra,
                firstTrailingRow,
                ActiveLowResOutputHeight,
                frameStartCycle,
                useTimedWrites,
                applyPendingWrites: false);
        }

        private int GetCopperFrameInstructionLimit(long frameStartCycle, long frameStopCycle)
        {
            var frameCycles = Math.Max(1, frameStopCycle - frameStartCycle);
            var minimumInstructionCycles = CopperHpToCpuCycles(CopperMoveHpUnits);
            return (int)Math.Min(int.MaxValue, (frameCycles / minimumInstructionCycles) + 1024);
        }

        private bool TryAdvanceCopperToNextPendingWrite(
            Span<uint> bgra,
            long frameStartCycle,
            long frameStopCycle,
            bool useTimedWrites,
            ref long renderCursorCycle,
            ref int renderCursorPixelDelay,
            ref CopperPresentationState copper)
        {
            if (!TryPeekPendingWrite(out var pending) || pending.Cycle >= frameStopCycle)
            {
                return false;
            }

            RenderPresentationSpan(
                bgra,
                frameStartCycle,
                renderCursorCycle,
                pending.Cycle,
                useTimedWrites,
                renderCursorPixelDelay,
                toPixelDelay: 0);
            renderCursorCycle = Math.Max(renderCursorCycle, pending.Cycle);
            renderCursorPixelDelay = 0;
            copper.Cycle = Math.Max(copper.Cycle, pending.Cycle);
            ApplyTimedPendingWrite(ref copper);
            return true;
        }

        private bool TryAdvanceCopperWait(
            Span<uint> bgra,
            long frameStartCycle,
            long frameStopCycle,
            bool useTimedWrites,
            ref long renderCursorCycle,
            ref int renderCursorPixelDelay,
            ref CopperPresentationState copper)
        {
            if (!IsCopperDmaEnabled())
            {
                return TryAdvanceCopperToNextPendingWrite(
                    bgra,
                    frameStartCycle,
                    frameStopCycle,
                    useTimedWrites,
                    ref renderCursorCycle,
                    ref renderCursorPixelDelay,
                    ref copper);
            }

            if ((copper.WaitSecond & 0x8000) == 0 && _bus.Blitter.Busy)
            {
                copper.WaitObservedBlitterBusy = true;
            }

            var blitterReadyCycle = GetCopperBlitterReadyCycle(copper.WaitSecond, copper.Cycle);
            if (!TryGetCopperWaitCycle(
                copper.WaitFirst,
                copper.WaitSecond,
                frameStartCycle,
                Math.Max(copper.Cycle, blitterReadyCycle),
                frameStopCycle,
                blitterFinished: true,
                out var waitCycle))
            {
                return false;
            }

            var nextWakeCycle = Math.Min(waitCycle, blitterReadyCycle);
            if (TryPeekPendingWrite(out var pending) && pending.Cycle < nextWakeCycle)
            {
                RenderPresentationSpan(
                    bgra,
                    frameStartCycle,
                    renderCursorCycle,
                    pending.Cycle,
                    useTimedWrites,
                    renderCursorPixelDelay,
                    toPixelDelay: 0);
                renderCursorCycle = Math.Max(renderCursorCycle, pending.Cycle);
                renderCursorPixelDelay = 0;
                copper.Cycle = Math.Max(copper.Cycle, pending.Cycle);
                ApplyTimedPendingWrite(ref copper);
                return true;
            }

            if (blitterReadyCycle > copper.Cycle)
            {
                var readyCycle = Math.Min(blitterReadyCycle, frameStopCycle);
                RenderPresentationSpan(
                    bgra,
                    frameStartCycle,
                    renderCursorCycle,
                    readyCycle,
                    useTimedWrites,
                    renderCursorPixelDelay,
                    toPixelDelay: 0);
                _bus.SynchronizeBlitterThrough(readyCycle);
                renderCursorCycle = Math.Max(renderCursorCycle, readyCycle);
                renderCursorPixelDelay = 0;
                copper.Cycle = Math.Max(copper.Cycle, readyCycle);
                return copper.Cycle < frameStopCycle;
            }

            var resumeCycle = waitCycle + CopperHpToCpuCycles(GetCopperWaitWakeHpUnits(
                copper.WaitSecond,
                copper.WaitObservedBlitterBusy));
            RenderPresentationSpan(
                bgra,
                frameStartCycle,
                renderCursorCycle,
                Math.Min(resumeCycle, frameStopCycle),
                useTimedWrites,
                renderCursorPixelDelay,
                toPixelDelay: 0);
            renderCursorCycle = Math.Max(renderCursorCycle, Math.Min(resumeCycle, frameStopCycle));
            renderCursorPixelDelay = 0;
            copper.Cycle = Math.Max(copper.Cycle, resumeCycle);
            copper.Waiting = false;
            return copper.Cycle < frameStopCycle;
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


        private void StepCopperInstruction(
            Span<uint> bgra,
            long frameStartCycle,
            long frameStopCycle,
            bool useTimedWrites,
            ref long renderCursorCycle,
            ref int renderCursorPixelDelay,
            ref CopperPresentationState copper)
        {
            var instruction = LoadPresentationCopperInstruction(copper.Pc, Math.Min(copper.Cycle, frameStopCycle));
            copper.Pc = AddDmaPointerOffset(copper.Pc, 4);

            if (instruction.IsEnd)
            {
                copper.Stopped = true;
                return;
            }

            if (instruction.IsMove)
            {
                var register = instruction.MoveRegister;
                var writePixelDelay = GetCopperWritePixelDelay(register);
                var clippedWritePixelDelay = instruction.DataCycle <= frameStopCycle ? writePixelDelay : 0;
                RenderPresentationSpan(
                    bgra,
                    frameStartCycle,
                    renderCursorCycle,
                    Math.Min(instruction.DataCycle, frameStopCycle),
                    useTimedWrites,
                    renderCursorPixelDelay,
                    clippedWritePixelDelay);
                renderCursorCycle = Math.Max(renderCursorCycle, Math.Min(instruction.DataCycle, frameStopCycle));
                renderCursorPixelDelay = clippedWritePixelDelay;
                if (instruction.DataCycle <= frameStopCycle)
                {
                    var suppressMove = copper.SuppressNextMove;
                    copper.SuppressNextMove = false;
                    if (IsCopperDangerStopRegister(register))
                    {
                        copper.Stopped = true;
                        copper.Cycle = instruction.MoveStopCycle;
                        return;
                    }

                    if (!suppressMove && CanCopperWriteRegister(register))
                    {
                        _currentCopperRow = GetOutputRowForCycle(frameStartCycle, instruction.DataCycle);
                        ApplyCopperMove(register, instruction.Second, instruction.DataCycle, applyHardwareSideEffects: false);
                        if (register == 0x088)
                        {
                            copper.JumpTo(_copperListPointer, instruction.DataCycle);
                        }
                        else if (register == 0x08A)
                        {
                            copper.JumpTo(_copperListPointer2, instruction.DataCycle);
                        }
                    }
                }

                copper.Cycle = instruction.MoveStopCycle;
                return;
            }

            if (instruction.IsWait)
            {
                copper.Cycle = instruction.ControlStopCycle;
                copper.Wait(instruction.First, instruction.Second);
                return;
            }

            if (instruction.ControlStopCycle <= frameStopCycle &&
                IsCopperComparisonSatisfied(
                instruction.First,
                instruction.Second,
                frameStartCycle,
                instruction.ControlStopCycle,
                IsCopperBlitterFinishedForWait(instruction.Second)))
            {
                copper.SuppressNextMove = true;
            }

            copper.Cycle = instruction.ControlStopCycle;
        }

        private CopperInstructionLatch LoadPresentationCopperInstruction(uint pc, long fetchCycle)
        {
            var first = ReadCopperWordForPresentation(pc, fetchCycle, out var firstAccess);
            var secondRequestCycle = GetCopperSecondWordRequestCycle(firstAccess);
            var second = ReadCopperWordForPresentation(AddDmaPointerOffset(pc, 2), secondRequestCycle, out var secondAccess);
            return new CopperInstructionLatch(first, firstAccess, second, secondAccess, CopperHpCycles);
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

        private void ApplyTimedPendingWrite(ref CopperPresentationState copper)
        {
            if (_pendingIndex >= _pendingWrites.Count)
            {
                return;
            }

            var write = _pendingWrites[_pendingIndex++];
            _currentCopperRow = GetOutputRowForCycle(_renderFrameStartCycle, write.Cycle);
            if (_trackDisplayWindowState)
            {
                AdvanceDisplayWindowStateToCycle(_renderFrameStartCycle, write.Cycle);
            }

            ApplyWrite(write.Offset, write.Value, write.Cycle);
            if (write.Offset == 0x088)
            {
                copper.JumpTo(_copperListPointer, write.Cycle);
            }
            else if (write.Offset == 0x08A)
            {
                copper.JumpTo(_copperListPointer2, write.Cycle);
            }

            CompactPendingWrites();
        }

        private void CompactPendingWrites()
        {
            if (_pendingIndex > 1024 && _pendingIndex * 2 > _pendingWrites.Count)
            {
                _pendingWrites.RemoveRange(0, _pendingIndex);
                _pendingIndex = 0;
            }
        }

        private void RenderPresentationSpan(
            Span<uint> bgra,
            long frameStartCycle,
            long fromCycle,
            long toCycle,
            bool useTimedWrites,
            int fromPixelDelay = 0,
            int toPixelDelay = 0)
        {
            if (toCycle <= fromCycle)
            {
                return;
            }

            var visibleStartCycle = GetLineStartCycle(frameStartCycle, StandardVStart);
            var visibleStopCycle = GetLineStartCycle(frameStartCycle, StandardVStart + ActiveLowResOutputHeight);
            var clippedStart = Math.Max(fromCycle, visibleStartCycle);
            var clippedStop = Math.Min(toCycle, visibleStopCycle);
            if (clippedStop <= clippedStart)
            {
                return;
            }

            var firstLine = Math.Clamp(GetBeamLineForCycle(frameStartCycle, clippedStart), StandardVStart, StandardVStart + ActiveLowResOutputHeight - 1);
            var lastLine = Math.Clamp(GetBeamLineForCycle(frameStartCycle, clippedStop - 1), StandardVStart, StandardVStart + ActiveLowResOutputHeight - 1);
            for (var line = firstLine; line <= lastLine; line++)
            {
                var lineStart = GetLineStartCycle(frameStartCycle, line);
                var lineStop = GetLineStartCycle(frameStartCycle, line + 1);
                var segmentStart = Math.Max(clippedStart, lineStart);
                var segmentStop = Math.Min(clippedStop, lineStop);
                if (segmentStop <= segmentStart)
                {
                    continue;
                }

                var row = line - StandardVStart;
                var applyFromDelay = fromPixelDelay != 0 &&
                    segmentStart == clippedStart &&
                    clippedStart == fromCycle;
                var xStart = applyFromDelay
                    ? GetOutputXForCycle(frameStartCycle, segmentStart, fromPixelDelay)
                    : GetOutputXForCycle(frameStartCycle, segmentStart);
                var xStop = segmentStop >= lineStop
                    ? LowResWidth
                    : GetOutputXForCycle(frameStartCycle, segmentStop);
                if (toPixelDelay != 0 &&
                    segmentStop == clippedStop &&
                    clippedStop == toCycle &&
                    segmentStop < lineStop)
                {
                    xStop = GetOutputXForCycle(frameStartCycle, segmentStop, toPixelDelay);
                }

                if (xStop <= xStart)
                {
                    continue;
                }

                RenderRows(bgra, row, row + 1, frameStartCycle, useTimedWrites, xStart, xStop, applyPendingWrites: false);
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
