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
        private sealed class LiveDmaScratchContext
        {
            private const int ScratchChipWriteOverlayCapacity = 16;
            private readonly OcsDisplay _display;
            private readonly AgnusHrmSlotEngine _slots;
            private readonly OcsLiveDmaScratchCpuWrite _pendingCpuWrite;
            private readonly long _frameStopCycle;
            private CopperPresentationState _copper;
            private bool _copperFirstWordPending;
            private uint _copperFirstWordAddress;
            private ushort _copperFirstWord;
            private AmigaBusAccessResult _copperFirstWordAccess;
            private long _capturedThroughCycle;
            private int _preparedFetchRow;
            private int _preparedFetchWord;
            private int _preparedFetchPlane;
            private int _preparedFetchSlot;
            private int _nextFetchRow;
            private int _nextFetchWord;
            private int _nextFetchPlane;
            private int _nextFetchSlot;
            private int _nextLineStateRow;
            private int _nextSpriteRow;
            private int _nextSpriteIndex;
            private int _nextSpriteWord;
            private int _pendingWriteIndex;
            private readonly bool[] _spriteExhausted = new bool[LiveSpriteChannelCount];
            private readonly LiveSpriteDmaState[] _spriteStates = new LiveSpriteDmaState[LiveSpriteChannelCount];
            private readonly LiveLineState[] _lineStates = new LiveLineState[LowResOutputHeight];
            private readonly bool[] _lineStateDirtyFromScratchWrite = new bool[LowResOutputHeight];
            private readonly uint[] _bitplanePointers = new uint[LiveBitplanePlaneCount];
            private readonly int[] _bitplaneBaseRows = new int[LiveBitplanePlaneCount];
            private readonly ushort[] _bitplaneDataRegisters = new ushort[LiveBitplanePlaneCount];
            private readonly uint[] _spritePointers = new uint[LiveSpriteChannelCount];
            private readonly ushort[] _spritePos = new ushort[LiveSpriteChannelCount];
            private readonly ushort[] _spriteCtl = new ushort[LiveSpriteChannelCount];
            private readonly ushort[] _spriteDataA = new ushort[LiveSpriteChannelCount];
            private readonly ushort[] _spriteDataB = new ushort[LiveSpriteChannelCount];
            private readonly bool[] _spriteManualArmed = new bool[LiveSpriteChannelCount];
            private uint _copperListPointer;
            private uint _copperListPointer2;
            private ushort _copcon;
            private ushort _bplcon0;
            private ushort _bplcon1;
            private ushort _bplcon2;
            private ushort _diwStart;
            private ushort _diwStop;
            private ushort _ddfStart;
            private ushort _ddfStop;
            private ushort _dmacon;
            private short _bpl1mod;
            private short _bpl2mod;
            private bool _displayWindowVerticallyOpen;
            private int _displayWindowStateLine;
            private int _bitplaneFetches;
            private int _spriteFetches;
            private int _copperSteps;
            private long _firstDmaCycle = -1;
            private long _lastDmaCycle = -1;
            private bool _spriteRegisterWriteSeen;
            private string _unsupported = string.Empty;
            private long _requestCycle;
            private int _requestRow = -1;
            private bool _dynamicBitplaneScheduleChanged;
            private readonly ScratchChipByteWrite[] _chipWriteOverlay = new ScratchChipByteWrite[ScratchChipWriteOverlayCapacity];
            private int _chipWriteOverlayCount;

            public LiveDmaScratchContext(
                OcsDisplay display,
                AgnusHrmSlotEngine slots,
                OcsLiveDmaScratchCpuWrite pendingCpuWrite)
            {
                _display = display;
                _slots = slots;
                _pendingCpuWrite = pendingCpuWrite;
                _frameStopCycle = display.GetLiveFrameStopCycle();
                _copper = display._liveCopper;
                _capturedThroughCycle = display._liveCapturedThroughCycle;
                _preparedFetchRow = display._livePreparedFetchRow;
                _preparedFetchWord = display._livePreparedFetchWord;
                _preparedFetchPlane = display._livePreparedFetchPlane;
                _preparedFetchSlot = display._livePreparedFetchSlot;
                _nextFetchRow = display._liveNextFetchRow;
                _nextFetchWord = display._liveNextFetchWord;
                _nextFetchPlane = display._liveNextFetchPlane;
                _nextFetchSlot = display._liveNextFetchSlot;
                _nextLineStateRow = display._liveNextLineStateRow;
                _nextSpriteRow = display._liveNextSpriteRow;
                _nextSpriteIndex = display._liveNextSpriteIndex;
                _nextSpriteWord = display._liveNextSpriteWord;
                _pendingWriteIndex = display._pendingIndex;
                _copperListPointer = display._copperListPointer;
                _copperListPointer2 = display._copperListPointer2;
                _copcon = display._copcon;
                _bplcon0 = display._bplcon0;
                _bplcon1 = display._bplcon1;
                _bplcon2 = display._bplcon2;
                _diwStart = display._diwStart;
                _diwStop = display._diwStop;
                _ddfStart = display._ddfStart;
                _ddfStop = display._ddfStop;
                _dmacon = display._dmacon;
                _bpl1mod = display._bpl1mod;
                _bpl2mod = display._bpl2mod;
                _displayWindowVerticallyOpen = display._liveDisplayWindowVerticallyOpen;
                _displayWindowStateLine = display._liveDisplayWindowStateLine;
                Array.Copy(display._bitplanePointers, _bitplanePointers, _bitplanePointers.Length);
                Array.Copy(display._bitplaneBaseRows, _bitplaneBaseRows, _bitplaneBaseRows.Length);
                Array.Copy(display._bitplaneDataRegisters, _bitplaneDataRegisters, _bitplaneDataRegisters.Length);
                Array.Copy(display._liveSpriteDmaExhausted, _spriteExhausted, _spriteExhausted.Length);
                for (var i = 0; i < _spriteStates.Length; i++)
                {
                    _spritePointers[i] = display._sprites[i].Pointer;
                    _spritePos[i] = display._sprites[i].Pos;
                    _spriteCtl[i] = display._sprites[i].Ctl;
                    _spriteDataA[i] = display._sprites[i].DataA;
                    _spriteDataB[i] = display._sprites[i].DataB;
                    _spriteManualArmed[i] = display._sprites[i].ManualArmed;
                    _spriteStates[i] = CloneSpriteState(display._liveSpriteDmaStates[i]);
                }
            }

            public bool TryRunCpuWaitGrant(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite,
                out OcsLiveDmaScratchResult result)
            {
                result = default;
                _requestCycle = requestedCycle;
                _requestRow = Math.Clamp(GetOutputRowForCycle(_display._liveFrameStartCycle, requestedCycle), 0, LowResOutputHeight - 1);
                SeedBitplaneCursorsForCpuRequest(requestedCycle);

                _slots.BeginPendingCpuSlotRequest(kind, target, address, size, requestedCycle, isWrite);
                try
                {
                    return TryRunCpuWaitGrantWithPendingCpu(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        isWrite,
                        out result);
                }
                finally
                {
                    _slots.ClearPendingCpuSlotRequest();
                }
            }

            private void SeedBitplaneCursorsForCpuRequest(long requestedCycle)
            {
                var row = Math.Clamp(GetOutputRowForCycle(_display._liveFrameStartCycle, requestedCycle), 0, LowResOutputHeight - 1);
                if (!TryGetBitplaneCursorAtOrAfterCycle(
                        row,
                        requestedCycle,
                        out var word,
                        out var slot,
                        out var plane))
                {
                    return;
                }

                if (_preparedFetchRow > row ||
                    _preparedFetchRow == row && IsBitplaneCursorAfter(_preparedFetchWord, _preparedFetchSlot, word, slot))
                {
                    _preparedFetchRow = row;
                    _preparedFetchWord = word;
                    _preparedFetchPlane = plane;
                    _preparedFetchSlot = slot;
                }

                if (_nextFetchRow > row ||
                    _nextFetchRow == row && IsBitplaneCursorAfter(_nextFetchWord, _nextFetchSlot, word, slot))
                {
                    _nextFetchRow = row;
                    _nextFetchWord = word;
                    _nextFetchPlane = plane;
                    _nextFetchSlot = slot;
                }
            }

            private bool TryGetBitplaneCursorAtOrAfterCycle(
                int row,
                long cycle,
                out int word,
                out int slot,
                out int plane)
            {
                word = 0;
                slot = 0;
                plane = 0;
                var state = GetScratchLineState(row);
                if (TryGetScratchRowDmaPlan(row, state, out var plan) &&
                    TryFindScratchRowDmaBitplaneEntryAtOrAfterCycle(plan, state.LineStartCycle, cycle, out var entryIndex))
                {
                    var entry = _display._rowDmaBitplaneEntries[entryIndex];
                    word = entry.Word;
                    slot = entry.Slot;
                    plane = entry.Plane;
                    return true;
                }

                var planeCount = Math.Max(0, state.PlaneCount);
                if (planeCount <= 0 ||
                    state.FetchWords <= 0 ||
                    !state.DisplayWindowVerticallyOpen ||
                    !IsBitplaneDmaEnabled(state.Dmacon))
                {
                    return false;
                }

                for (var candidateWord = 0; candidateWord < state.FetchWords; candidateWord++)
                {
                    for (var candidateSlot = 0; candidateSlot < state.FetchSlotStride; candidateSlot++)
                    {
                        if (!TryGetBitplanePlaneForFetchSlot(candidateSlot, planeCount, state.FetchSlotStride, out var candidatePlane))
                        {
                            continue;
                        }

                        var fetchHorizontal = state.DataFetchStart + (candidateWord * state.FetchSlotStride) + candidateSlot;
                        var fetchCycle = AgnusChipSlotScheduler.AlignToSlot(state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
                        if (fetchCycle < cycle)
                        {
                            continue;
                        }

                        word = candidateWord;
                        slot = candidateSlot;
                        plane = candidatePlane;
                        return true;
                    }
                }

                return false;
            }

            private static bool IsBitplaneCursorAfter(int currentWord, int currentSlot, int targetWord, int targetSlot)
                => currentWord > targetWord ||
                   currentWord == targetWord && currentSlot > targetSlot;

            private bool TryRunCpuWaitGrantWithPendingCpu(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite,
                out OcsLiveDmaScratchResult result)
            {
                result = default;
                if (size == AmigaBusAccessSize.Long && !isWrite)
                {
                    return TryRunCpuWaitLongReadGrantWithPendingCpu(
                        kind,
                        target,
                        address,
                        requestedCycle,
                        out result);
                }

                if (size == AmigaBusAccessSize.Long && isWrite)
                {
                    return TryRunCpuWaitLongWriteGrantWithPendingCpu(
                        kind,
                        target,
                        address,
                        requestedCycle,
                        out result);
                }

                if (!TryRunCpuWaitSingleSlotExecutor(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        isWrite,
                        out var granted,
                        out var completed))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                var second = granted;

                if (!ApplyPendingCpuWrite(granted, second))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                if (_dynamicBitplaneScheduleChanged)
                {
                    result = new OcsLiveDmaScratchResult(
                        supported: false,
                        unsupportedReason: "bitplane-window",
                        granted,
                        second,
                        completed,
                        timeline: default,
                        _bitplaneFetches,
                        _spriteFetches,
                        _copperSteps,
                        _firstDmaCycle,
                        _lastDmaCycle,
                        BuildScratchDetail(requestedCycle));
                    return false;
                }

                var timeline = _slots.CaptureTimelineSignature(requestedCycle, completed);
                result = new OcsLiveDmaScratchResult(
                    supported: true,
                    unsupportedReason: string.Empty,
                    granted,
                    second,
                    completed,
                    timeline,
                    _bitplaneFetches,
                    _spriteFetches,
                    _copperSteps,
                    _firstDmaCycle,
                    _lastDmaCycle,
                    BuildScratchDetail(requestedCycle));
                return true;
            }

            private bool TryRunCpuWaitSingleSlotExecutor(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite,
                out long grantedCycle,
                out long completedCycle)
            {
                const int MaxSlots = 4096;
                grantedCycle = 0;
                completedCycle = 0;
                var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
                for (var slot = 0; slot < MaxSlots; slot++, candidate += AgnusChipSlotScheduler.SlotCycles)
                {
                    if (candidate < _frameStopCycle &&
                        HasWorkThrough(candidate) &&
                        !AdvanceThrough(candidate))
                    {
                        return false;
                    }

                    if (_slots.TryGrantCpuDataSingleExactSlot(
                            kind,
                            target,
                            address,
                            size,
                            requestedCycle,
                            candidate,
                            isWrite,
                            allowNiceBlitterSteal: true,
                            out completedCycle))
                    {
                        grantedCycle = candidate;
                        return true;
                    }
                }

                _unsupported = "slot-loop";
                return false;
            }

            private string BuildScratchDetail(long requestedCycle)
            {
                var detailRow = Math.Clamp(GetOutputRowForCycle(_display._liveFrameStartCycle, requestedCycle), 0, LowResOutputHeight - 1);
                var detailState = GetScratchLineState(detailRow);
                var detailPlan = _display._rowDmaPlans[detailRow];
                var nextPending = _pendingWriteIndex < _display._pendingWrites.Count
                    ? _display._pendingWrites[_pendingWriteIndex]
                    : default;
                return $"req={requestedCycle},captured={_capturedThroughCycle},copper={_copper.Cycle},pc=0x{_copper.Pc:X6},wait={_copper.Waiting},pm={_copper.PendingMove},ps={_copper.PendingSkip},pf={_copperFirstWordPending},regs=0x{_bplcon0:X4}/0x{_dmacon:X4}/0x{_ddfStart:X4}-0x{_ddfStop:X4},pending={_pendingWriteIndex}/{_display._pendingWrites.Count}:{nextPending.Cycle}:0x{nextPending.Offset:X3}=0x{nextPending.Value:X4},prep={_preparedFetchRow}:{_preparedFetchWord}:{_preparedFetchSlot},next={_nextFetchRow}:{_nextFetchWord}:{_nextFetchSlot},prepcy={GetNextPreparedBitplaneFetchCycle()},nextcy={GetNextBitplaneFetchCycle()},row={detailRow},line={detailState.LineStartCycle},df={detailState.DataFetchStart},fw={detailState.FetchWords},stride={detailState.FetchSlotStride},planes={detailState.PlaneCount},dmacon=0x{detailState.Dmacon:X4},ddf=0x{detailState.DdfStart:X4}-0x{detailState.DdfStop:X4},win={detailState.DisplayWindowVerticallyOpen},gen={detailState.Generation},plan={detailPlan.Valid}:{detailPlan.Generation}:{detailPlan.Row}:{detailPlan.BitplaneCount}:{detailPlan.Signature}:{ComputeRowDmaPlanSignature(detailState)}";
            }

            private bool TryRunCpuWaitLongWriteGrantWithPendingCpu(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                long requestedCycle,
                out OcsLiveDmaScratchResult result)
            {
                result = default;
                if (!TryFindStableLongWritePhaseGrant(
                        kind,
                        target,
                        address,
                        requestedCycle,
                        requestedCycle,
                        out _))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                _slots.GrantCpuDataLongWordPhaseSlot(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    requestedCycle,
                    isWrite: true,
                    out var first,
                    out var firstCompleted);

                if (!ApplyPendingCpuLongWriteFirstWord(first))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                if (!TryFindStableLongWritePhaseGrant(
                        kind,
                        target,
                        address,
                        firstCompleted + AgnusChipSlotScheduler.SlotCycles,
                        requestedCycle,
                        out _))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                _slots.GrantCpuDataLongWordPhaseSlot(
                    kind,
                    target,
                    address,
                    firstCompleted + AgnusChipSlotScheduler.SlotCycles,
                    requestedCycle,
                    isWrite: true,
                    out var second,
                    out var completed);

                if (!ApplyPendingCpuLongWriteSecondWord(second))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                var timeline = _slots.CaptureTimelineSignature(requestedCycle, completed);
                result = new OcsLiveDmaScratchResult(
                    supported: true,
                    unsupportedReason: string.Empty,
                    first,
                    second,
                    completed,
                    timeline,
                    _bitplaneFetches,
                    _spriteFetches,
                    _copperSteps,
                    _firstDmaCycle,
                    _lastDmaCycle);
                return true;
            }

            private bool TryRunCpuWaitLongReadGrantWithPendingCpu(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                long requestedCycle,
                out OcsLiveDmaScratchResult result)
            {
                result = default;
                if (!TryFindStableLongReadPackedGrant(
                        kind,
                        target,
                        address,
                        requestedCycle,
                        out _,
                        out _,
                        out _))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                _slots.GrantCpuDataLongWordPhaseSlot(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    requestedCycle,
                    isWrite: false,
                    out var first,
                    out var firstCompleted);

                var secondSearch = firstCompleted + AgnusChipSlotScheduler.SlotCycles;
                if (!TryFindStableLongReadPhaseGrant(
                        kind,
                        target,
                        address,
                        secondSearch,
                        requestedCycle,
                        out _))
                {
                    result = OcsLiveDmaScratchResult.Unsupported(_unsupported);
                    return false;
                }

                _slots.GrantCpuDataLongWordPhaseSlot(
                    kind,
                    target,
                    address,
                    secondSearch,
                    requestedCycle,
                    isWrite: false,
                    out var second,
                    out var completed);

                var timeline = _slots.CaptureTimelineSignature(requestedCycle, completed);
                result = new OcsLiveDmaScratchResult(
                    supported: true,
                    unsupportedReason: string.Empty,
                    first,
                    second,
                    completed,
                    timeline,
                    _bitplaneFetches,
                    _spriteFetches,
                    _copperSteps,
                    _firstDmaCycle,
                    _lastDmaCycle);
                return true;
            }

            private bool TryFindStableLongReadPackedGrant(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                long requestedCycle,
                out long predictedGrant,
                out long predictedSecond,
                out long predictedCompletion)
            {
                predictedGrant = -1;
                predictedSecond = -1;
                predictedCompletion = -1;
                var candidate = requestedCycle;
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    if (!AdvanceBeforeHrmGrant(candidate))
                    {
                        return false;
                    }

                    if (!_slots.TryPredictCpuDataLongSlots(
                            kind,
                            target,
                            address,
                            requestedCycle,
                            isWrite: false,
                            out var nextGrant,
                            out var nextSecond,
                            out var nextCompletion))
                    {
                        _unsupported = "cpu-predict";
                        return false;
                    }

                    if (nextGrant == predictedGrant &&
                        nextSecond == predictedSecond &&
                        nextCompletion == predictedCompletion)
                    {
                        break;
                    }

                    predictedGrant = nextGrant;
                    predictedSecond = nextSecond;
                    predictedCompletion = nextCompletion;
                    if (predictedSecond <= candidate)
                    {
                        break;
                    }

                    candidate = predictedSecond;
                }

                if (predictedGrant < 0)
                {
                    _unsupported = "unstable";
                    return false;
                }

                return true;
            }

            private bool TryFindStableLongReadPhaseGrant(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                long searchCycle,
                long requestedCycle,
                out long predictedGrant)
            {
                predictedGrant = -1;
                var candidate = searchCycle;
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    if (!AdvanceBeforeHrmGrant(candidate))
                    {
                        return false;
                    }

                    if (!_slots.TryPredictCpuDataLongWordPhaseSlot(
                            kind,
                            target,
                            address,
                            candidate,
                            requestedCycle,
                            isWrite: false,
                            out var nextGrant))
                    {
                        _unsupported = "cpu-predict";
                        return false;
                    }

                    if (nextGrant == predictedGrant)
                    {
                        break;
                    }

                    predictedGrant = nextGrant;
                    if (predictedGrant <= candidate)
                    {
                        break;
                    }

                    candidate = predictedGrant;
                }

                if (predictedGrant < 0)
                {
                    _unsupported = "unstable";
                    return false;
                }

                return true;
            }

            private bool TryFindStableLongWritePhaseGrant(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                long searchCycle,
                long requestedCycle,
                out long predictedGrant)
            {
                predictedGrant = -1;
                var candidate = searchCycle;
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    if (!AdvanceBeforeHrmGrant(candidate))
                    {
                        return false;
                    }

                    if (!_slots.TryPredictCpuDataLongWordPhaseSlot(
                            kind,
                            target,
                            address,
                            candidate,
                            requestedCycle,
                            isWrite: true,
                            out var nextGrant))
                    {
                        _unsupported = "cpu-predict";
                        return false;
                    }

                    if (nextGrant == predictedGrant)
                    {
                        break;
                    }

                    predictedGrant = nextGrant;
                    if (predictedGrant <= candidate)
                    {
                        break;
                    }

                    candidate = predictedGrant;
                }

                if (predictedGrant < 0)
                {
                    _unsupported = "unstable";
                    return false;
                }

                return true;
            }

            private bool TryPredictCpuGrant(
                AmigaBusAccessKind kind,
                AmigaBusAccessTarget target,
                uint address,
                AmigaBusAccessSize size,
                long requestedCycle,
                bool isWrite,
                out long granted,
                out long second,
                out long completed)
            {
                if (size == AmigaBusAccessSize.Long)
                {
                    return _slots.TryPredictCpuDataLongSlots(
                        kind,
                        target,
                        address,
                        requestedCycle,
                        isWrite,
                        out granted,
                        out second,
                        out completed);
                }

                second = 0;
                completed = 0;
                if (!_slots.TryPredictCpuDataSingleSlot(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        isWrite,
                        out granted))
                {
                    return false;
                }

                second = granted;
                completed = granted + AgnusChipSlotScheduler.SlotCycles;
                return true;
            }

            private bool ApplyPendingCpuLongWriteFirstWord(long grantedCycle)
            {
                if (!_pendingCpuWrite.HasValue)
                {
                    return true;
                }

                if (_pendingCpuWrite.Size != AmigaBusAccessSize.Long)
                {
                    _unsupported = "long-write-size";
                    return false;
                }

                if (_pendingCpuWrite.Target == AmigaBusAccessTarget.ChipRam)
                {
                    return WriteScratchChipWord(
                        _pendingCpuWrite.Address,
                        (ushort)(_pendingCpuWrite.Value >> 16),
                        grantedCycle);
                }

                if (_pendingCpuWrite.Target == AmigaBusAccessTarget.CustomRegisters)
                {
                    return ApplyPendingCpuCustomRegisterWord(
                        _pendingCpuWrite.Address,
                        (ushort)(_pendingCpuWrite.Value >> 16),
                        grantedCycle);
                }

                _unsupported = "long-write-target";
                return false;
            }

            private bool ApplyPendingCpuLongWriteSecondWord(long grantedCycle)
            {
                if (!_pendingCpuWrite.HasValue)
                {
                    return true;
                }

                if (_pendingCpuWrite.Size != AmigaBusAccessSize.Long)
                {
                    _unsupported = "long-write-size";
                    return false;
                }

                if (_pendingCpuWrite.Target == AmigaBusAccessTarget.ChipRam)
                {
                    return WriteScratchChipWord(
                        _display.AddDmaPointerOffset(_pendingCpuWrite.Address, 2),
                        (ushort)_pendingCpuWrite.Value,
                        grantedCycle);
                }

                if (_pendingCpuWrite.Target == AmigaBusAccessTarget.CustomRegisters)
                {
                    return ApplyPendingCpuCustomRegisterWord(
                        _pendingCpuWrite.Address + 2,
                        (ushort)_pendingCpuWrite.Value,
                        grantedCycle);
                }

                _unsupported = "long-write-target";
                return false;
            }

            private bool ApplyPendingCpuWrite(long grantedCycle, long secondWordCycle)
            {
                if (!_pendingCpuWrite.HasValue)
                {
                    return true;
                }

                if (_pendingCpuWrite.Target == AmigaBusAccessTarget.CustomRegisters)
                {
                    if (_pendingCpuWrite.Size == AmigaBusAccessSize.Word)
                    {
                        return ApplyPendingCpuCustomRegisterWord(
                            _pendingCpuWrite.Address,
                            (ushort)_pendingCpuWrite.Value,
                            grantedCycle);
                    }

                    if (_pendingCpuWrite.Size == AmigaBusAccessSize.Long)
                    {
                        return ApplyPendingCpuCustomRegisterWord(
                                _pendingCpuWrite.Address,
                                (ushort)(_pendingCpuWrite.Value >> 16),
                                grantedCycle) &&
                            ApplyPendingCpuCustomRegisterWord(
                                _pendingCpuWrite.Address + 2,
                                (ushort)_pendingCpuWrite.Value,
                                secondWordCycle);
                    }

                    _unsupported = "custom-write-size";
                    return false;
                }

                if (_pendingCpuWrite.Target != AmigaBusAccessTarget.ChipRam)
                {
                    return true;
                }

                if (_pendingCpuWrite.Size == AmigaBusAccessSize.Byte)
                {
                    return WriteScratchChipByte(
                        _pendingCpuWrite.Address,
                        (byte)_pendingCpuWrite.Value,
                        grantedCycle);
                }

                if (_pendingCpuWrite.Size == AmigaBusAccessSize.Word)
                {
                    return WriteScratchChipWord(
                        _pendingCpuWrite.Address,
                        (ushort)_pendingCpuWrite.Value,
                        grantedCycle);
                }

                if (_pendingCpuWrite.Size == AmigaBusAccessSize.Long)
                {
                    return WriteScratchChipWord(
                            _pendingCpuWrite.Address,
                            (ushort)(_pendingCpuWrite.Value >> 16),
                            grantedCycle) &&
                        WriteScratchChipWord(
                            _display.AddDmaPointerOffset(_pendingCpuWrite.Address, 2),
                            (ushort)_pendingCpuWrite.Value,
                            secondWordCycle);
                }

                _unsupported = "write-size";
                return false;
            }

            private bool ApplyPendingCpuCustomRegisterWord(uint address, ushort value, long cycle)
            {
                if (address < 0x00DFF000 ||
                    address + 1 >= 0x00DFF200 ||
                    (address & 1) != 0)
                {
                    _unsupported = "custom-word-address";
                    return false;
                }

                return ApplyScratchCopperMove((ushort)(address - 0x00DFF000), value, cycle);
            }

            private bool ApplyScratchCpuScheduledWrite(ushort register, ushort value, long cycle)
            {
                register = CustomRegisterScheduleClassifier.NormalizeOffset(register);
                if (!IsDisplayRegisterWrite(register))
                {
                    _unsupported = $"pending-nondisplay-{register:X3}";
                    return false;
                }

                if (ApplyScratchCopperMove(register, value, cycle))
                {
                    return true;
                }

                if (!_unsupported.StartsWith("pending-", StringComparison.Ordinal))
                {
                    _unsupported = "pending-" + _unsupported;
                }

                return false;
            }

            private bool WriteScratchChipWord(uint address, ushort value, long cycle)
                => WriteScratchChipByte(
                        address,
                        (byte)(value >> 8),
                        cycle) &&
                    WriteScratchChipByte(
                        _display.AddDmaPointerOffset(address, 1),
                        (byte)value,
                        cycle);

            private bool WriteScratchChipByte(uint address, byte value, long cycle)
            {
                if (_chipWriteOverlayCount >= _chipWriteOverlay.Length)
                {
                    _unsupported = "chip-write-overlay";
                    return false;
                }

                address = _display._bus.MaskChipDmaAddress(address & 0x00FF_FFFEu);
                _chipWriteOverlay[_chipWriteOverlayCount++] = new ScratchChipByteWrite(address, value, cycle);
                return true;
            }

            private ushort ReadScratchChipWordForPresentation(uint address, long cycle)
            {
                address = _display._bus.MaskChipDmaAddress(address & 0x00FF_FFFEu);
                var lowAddress = _display.AddDmaPointerOffset(address, 1);
                var value = _display._bus.ReadChipWordForPresentation(address, cycle);
                for (var i = _chipWriteOverlayCount - 1; i >= 0; i--)
                {
                    var write = _chipWriteOverlay[i];
                    if (write.Cycle > cycle)
                    {
                        continue;
                    }

                    if (write.Address == address)
                    {
                        value = (ushort)((write.Value << 8) | (value & 0x00FF));
                        break;
                    }
                }

                for (var i = _chipWriteOverlayCount - 1; i >= 0; i--)
                {
                    var write = _chipWriteOverlay[i];
                    if (write.Cycle > cycle)
                    {
                        continue;
                    }

                    if (write.Address == lowAddress)
                    {
                        value = (ushort)((value & 0xFF00) | write.Value);
                        break;
                    }
                }

                return value;
            }

            private bool AdvanceBeforeHrmGrant(long requestedCycle)
            {
                for (var attempt = 0; attempt < 256; attempt++)
                {
                    var before = FindDmaCandidate(requestedCycle);
                    if (before >= _frameStopCycle)
                    {
                        return true;
                    }

                    var hasWork = HasWorkThrough(before);
                    if (_unsupported.Length != 0)
                    {
                        return false;
                    }

                    if (!hasWork)
                    {
                        return true;
                    }

                    if (!AdvanceThrough(before))
                    {
                        return false;
                    }

                    var after = _slots.IsReserved(before)
                        ? FindDmaCandidate(before + AgnusChipSlotScheduler.SlotCycles)
                        : FindDmaCandidate(requestedCycle);
                    if (after == before)
                    {
                        return true;
                    }
                }

                _unsupported = "loop";
                return false;
            }

            private long FindDmaCandidate(long requestedCycle)
            {
                var candidate = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle));
                while (_slots.IsReserved(candidate))
                {
                    candidate += AgnusChipSlotScheduler.SlotCycles;
                }

                return candidate;
            }

            private bool HasWorkThrough(long cycle)
            {
                return Math.Min(
                    GetNextPendingWriteCycle(),
                    Math.Min(
                        GetNextLineStateCycle(),
                        Math.Min(
                        GetNextPreparedBitplaneFetchCycle(),
                        Math.Min(
                            GetNextBitplaneFetchCycle(),
                            Math.Min(GetNextSpriteFetchCycle(), GetNextCopperCycle(cycle)))))) <= cycle;
            }

            private bool AdvanceThrough(long cycle)
            {
                while (true)
                {
                    var nextPendingWrite = GetNextPendingWriteCycle();
                    var nextLineState = GetNextLineStateCycle();
                    var nextPreparedBitplane = GetNextPreparedBitplaneFetchCycle();
                    var nextBitplane = GetNextBitplaneFetchCycle();
                    var nextSprite = GetNextSpriteFetchCycle();
                    var nextCopper = GetNextCopperCycle(cycle);
                    var next = Math.Min(
                        nextPendingWrite,
                        Math.Min(
                            nextLineState,
                            Math.Min(
                            nextPreparedBitplane,
                            Math.Min(
                                nextBitplane,
                                Math.Min(nextSprite, nextCopper)))));
                    if (_unsupported.Length != 0)
                    {
                        return false;
                    }

                    if (next > cycle)
                    {
                        _capturedThroughCycle = Math.Max(_capturedThroughCycle, cycle);
                        return true;
                    }

                    if (nextPendingWrite == next)
                    {
                        if (!ApplyNextPendingWrite(cycle))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (nextCopper == next)
                    {
                        if (!StepCopper(cycle))
                        {
                            return false;
                        }

                        _copperSteps++;
                        continue;
                    }

                    if (nextLineState == next)
                    {
                        CaptureScratchLineStateFromCurrentRegisters(_nextLineStateRow);
                        _nextLineStateRow++;
                        continue;
                    }

                    if (nextPreparedBitplane == next)
                    {
                        if (!ReservePreparedBitplaneSlot())
                        {
                            return false;
                        }

                        continue;
                    }

                    if (nextBitplane == next)
                    {
                        if (!ReserveBitplaneFetch())
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!ReserveSpriteFetch())
                    {
                        return false;
                    }
                }
            }

            private long GetNextLineStateCycle()
            {
                if (_nextLineStateRow >= LowResOutputHeight)
                {
                    return long.MaxValue;
                }

                return GetOutputRowStartCycle(_display._liveFrameStartCycle, _nextLineStateRow);
            }

            private long GetNextPendingWriteCycle()
            {
                return _pendingWriteIndex < _display._pendingWrites.Count
                    ? _display._pendingWrites[_pendingWriteIndex].Cycle
                    : long.MaxValue;
            }

            private bool ApplyNextPendingWrite(long targetCycle)
            {
                if (_pendingWriteIndex >= _display._pendingWrites.Count)
                {
                    return true;
                }

                var write = _display._pendingWrites[_pendingWriteIndex];
                if (write.Cycle > targetCycle)
                {
                    return true;
                }

                _pendingWriteIndex++;
                if (!ApplyScratchCpuScheduledWrite(write.Offset, write.Value, write.Cycle))
                {
                    return false;
                }

                _capturedThroughCycle = Math.Max(_capturedThroughCycle, write.Cycle);
                return true;
            }

            private long GetNextPreparedBitplaneFetchCycle()
            {
                if (!NormalizePreparedBitplaneCursor())
                {
                    return long.MaxValue;
                }

                var state = _display._liveLineStates[_preparedFetchRow];
                state = GetScratchLineState(_preparedFetchRow);
                if (TryGetScratchRowDmaPlan(_preparedFetchRow, state, out var plan) &&
                    TryFindExactScratchRowDmaBitplaneEntry(plan, _preparedFetchWord, _preparedFetchSlot, out var entry))
                {
                    return entry.GetCycle(state.LineStartCycle);
                }

                var fetchHorizontal = state.DataFetchStart + (_preparedFetchWord * state.FetchSlotStride) + _preparedFetchSlot;
                return AgnusChipSlotScheduler.AlignToSlot(state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
            }

            private bool NormalizePreparedBitplaneCursor()
            {
                while (_preparedFetchRow < LowResOutputHeight)
                {
                    var state = GetScratchLineState(_preparedFetchRow);
                    if (TryGetScratchRowDmaPlan(_preparedFetchRow, state, out var plan))
                    {
                        if (TryFindNextScratchRowDmaBitplaneEntry(
                                plan,
                                _preparedFetchWord,
                                _preparedFetchSlot,
                                out var entryIndex))
                        {
                            var entry = _display._rowDmaBitplaneEntries[entryIndex];
                            _preparedFetchPlane = entry.Plane;
                            _preparedFetchWord = entry.Word;
                            _preparedFetchSlot = entry.Slot;
                            return true;
                        }

                        AdvancePreparedBitplaneCursorToNextRow();
                        continue;
                    }

                    var planeCount = Math.Max(0, state.PlaneCount);
                    if (planeCount <= 0 ||
                        state.FetchWords <= 0 ||
                        !state.DisplayWindowVerticallyOpen ||
                        !IsBitplaneDmaEnabled(state.Dmacon))
                    {
                        AdvancePreparedBitplaneCursorToNextRow();
                        continue;
                    }

                    if (_spriteRegisterWriteSeen)
                    {
                        _unsupported = "sprite-bitplane";
                        return false;
                    }

                    while (_preparedFetchWord < state.FetchWords)
                    {
                        while (_preparedFetchSlot < state.FetchSlotStride)
                        {
                            if (TryGetBitplanePlaneForFetchSlot(_preparedFetchSlot, planeCount, state.FetchSlotStride, out var plane))
                            {
                                _preparedFetchPlane = plane;
                                return true;
                            }

                            _preparedFetchSlot++;
                        }

                        _preparedFetchSlot = 0;
                        _preparedFetchWord++;
                    }

                    AdvancePreparedBitplaneCursorToNextRow();
                }

                return false;
            }

            private bool ReservePreparedBitplaneSlot()
            {
                var state = GetScratchLineState(_preparedFetchRow);
                if (TryGetScratchRowDmaPlan(_preparedFetchRow, state, out var plan) &&
                    TryFindExactScratchRowDmaBitplaneEntry(plan, _preparedFetchWord, _preparedFetchSlot, out var entry))
                {
                    if (entry.RowPresent)
                    {
                        var access = _slots.ReserveBitplaneDmaSlot(entry.Address, entry.GetCycle(state.LineStartCycle));
                        if (access.CompletedCycle > access.GrantedCycle)
                        {
                            RecordDma(access.GrantedCycle);
                        }
                    }

                    AdvancePreparedBitplaneCursor();
                    return true;
                }

                if ((state.PlaneHasRowMask & (1 << _preparedFetchPlane)) != 0)
                {
                    var address = _display.AddDmaPointerOffset(state.BitplaneRowAddresses[_preparedFetchPlane], _preparedFetchWord * 2);
                    var fetchCycle = GetNextPreparedBitplaneFetchCycle();
                    var access = _slots.ReserveBitplaneDmaSlot(address, fetchCycle);
                    if (access.CompletedCycle > access.GrantedCycle)
                    {
                        RecordDma(access.GrantedCycle);
                    }
                }

                AdvancePreparedBitplaneCursor();
                return true;
            }

            private void AdvancePreparedBitplaneCursor()
            {
                var state = GetScratchLineState(_preparedFetchRow);
                _preparedFetchSlot++;
                if (_preparedFetchSlot < state.FetchSlotStride)
                {
                    return;
                }

                _preparedFetchSlot = 0;
                _preparedFetchPlane = 0;
                _preparedFetchWord++;
                if (_preparedFetchWord >= state.FetchWords)
                {
                    AdvancePreparedBitplaneCursorToNextRow();
                }
            }

            private void AdvancePreparedBitplaneCursorToNextRow()
            {
                _preparedFetchRow++;
                _preparedFetchWord = 0;
                _preparedFetchPlane = 0;
                _preparedFetchSlot = 0;
            }

            private long GetNextBitplaneFetchCycle()
            {
                if (!NormalizeBitplaneCursor())
                {
                    return long.MaxValue;
                }

                var state = GetScratchLineState(_nextFetchRow);
                if (TryGetScratchRowDmaPlan(_nextFetchRow, state, out var plan) &&
                    TryFindNextScratchRowDmaBitplaneEntry(plan, _nextFetchWord, _nextFetchSlot, out var entryIndex))
                {
                    return _display._rowDmaBitplaneEntries[entryIndex].GetCycle(state.LineStartCycle);
                }

                var fetchHorizontal = state.DataFetchStart + (_nextFetchWord * state.FetchSlotStride) + _nextFetchSlot;
                return AgnusChipSlotScheduler.AlignToSlot(state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
            }

            private bool NormalizeBitplaneCursor()
            {
                while (_nextFetchRow < LowResOutputHeight)
                {
                    var state = GetScratchLineState(_nextFetchRow);
                    if (TryGetScratchRowDmaPlan(_nextFetchRow, state, out var plan))
                    {
                        if (TryFindNextScratchRowDmaBitplaneEntry(
                                plan,
                                _nextFetchWord,
                                _nextFetchSlot,
                                out var entryIndex))
                        {
                            var entry = _display._rowDmaBitplaneEntries[entryIndex];
                            _nextFetchPlane = entry.Plane;
                            _nextFetchWord = entry.Word;
                            _nextFetchSlot = entry.Slot;
                            return true;
                        }

                        AdvanceBitplaneCursorToNextRow();
                        continue;
                    }

                    var planeCount = Math.Max(0, state.PlaneCount);
                    if (planeCount <= 0 ||
                        state.FetchWords <= 0 ||
                        !state.DisplayWindowVerticallyOpen ||
                        !IsBitplaneDmaEnabled(state.Dmacon))
                    {
                        AdvanceBitplaneCursorToNextRow();
                        continue;
                    }

                    if (_spriteRegisterWriteSeen)
                    {
                        _unsupported = "sprite-bitplane";
                        return false;
                    }

                    while (_nextFetchWord < state.FetchWords)
                    {
                        while (_nextFetchSlot < state.FetchSlotStride)
                        {
                            if (TryGetBitplanePlaneForFetchSlot(_nextFetchSlot, planeCount, state.FetchSlotStride, out var plane))
                            {
                                _nextFetchPlane = plane;
                                return true;
                            }

                            _nextFetchSlot++;
                        }

                        _nextFetchSlot = 0;
                        _nextFetchWord++;
                    }

                    AdvanceBitplaneCursorToNextRow();
                }

                return false;
            }

            private bool ReserveBitplaneFetch()
            {
                var state = GetScratchLineState(_nextFetchRow);
                if (TryReserveBitplaneFetchWithRowPlan(state))
                {
                    return true;
                }

                if ((state.PlaneHasRowMask & (1 << _nextFetchPlane)) != 0)
                {
                    var address = _display.AddDmaPointerOffset(state.BitplaneRowAddresses[_nextFetchPlane], _nextFetchWord * 2);
                    var fetchCycle = GetNextBitplaneFetchCycle();
                    var access = _slots.ReserveBitplaneDmaSlot(address, fetchCycle);
                    if (access.CompletedCycle > access.GrantedCycle)
                    {
                        _bitplaneFetches++;
                        RecordDma(access.GrantedCycle);
                    }
                }

                AdvanceBitplaneCursor();
                return true;
            }

            private bool TryReserveBitplaneFetchWithRowPlan(LiveLineState state)
            {
                if (!TryGetScratchRowDmaPlan(_nextFetchRow, state, out var plan) ||
                    !TryFindNextScratchRowDmaBitplaneEntry(plan, _nextFetchWord, _nextFetchSlot, out var entryIndex))
                {
                    return false;
                }

                var entry = _display._rowDmaBitplaneEntries[entryIndex];
                _nextFetchPlane = entry.Plane;
                _nextFetchWord = entry.Word;
                _nextFetchSlot = entry.Slot;
                if (entry.RowPresent)
                {
                    var access = _slots.ReserveBitplaneDmaSlot(entry.Address, entry.GetCycle(state.LineStartCycle));
                    if (access.CompletedCycle > access.GrantedCycle)
                    {
                        _bitplaneFetches++;
                        RecordDma(access.GrantedCycle);
                    }
                }

                AdvanceBitplaneCursor();
                return true;
            }

            private bool TryGetScratchRowDmaPlan(int row, LiveLineState state, out RowDmaPlan plan)
            {
                plan = default;
                if ((uint)row >= (uint)LowResOutputHeight)
                {
                    return false;
                }

                plan = _display._rowDmaPlans[row];
                if (plan.Valid &&
                    plan.Generation == _display._liveGeneration &&
                    plan.Row == row &&
                    plan.Signature == ComputeRowDmaPlanSignature(state))
                {
                    return true;
                }

                if (plan.Valid &&
                    row == _requestRow &&
                    IsStartedRequestRowDmaPlan(row, state, plan))
                {
                    return true;
                }

                return false;
            }

            private bool IsStartedRequestRowDmaPlan(int row, LiveLineState state, RowDmaPlan plan)
            {
                if (plan.BitplaneCount <= 0 ||
                    plan.Generation != _display._liveGeneration ||
                    plan.Row != row)
                {
                    return false;
                }

                var first = _display._rowDmaBitplaneEntries[plan.BitplaneStart];
                return first.GetCycle(state.LineStartCycle) <= _requestCycle;
            }

            private bool TryFindNextScratchRowDmaBitplaneEntry(
                RowDmaPlan plan,
                int word,
                int slot,
                out int entryIndex)
            {
                var end = plan.BitplaneStart + plan.BitplaneCount;
                for (var index = plan.BitplaneStart; index < end; index++)
                {
                    var entry = _display._rowDmaBitplaneEntries[index];
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

            private bool TryFindExactScratchRowDmaBitplaneEntry(
                RowDmaPlan plan,
                int word,
                int slot,
                out RowDmaBitplaneEntry entry)
            {
                var end = plan.BitplaneStart + plan.BitplaneCount;
                for (var index = plan.BitplaneStart; index < end; index++)
                {
                    var candidate = _display._rowDmaBitplaneEntries[index];
                    if (candidate.Word == word && candidate.Slot == slot)
                    {
                        entry = candidate;
                        return true;
                    }
                }

                entry = default;
                return false;
            }

            private bool TryFindScratchRowDmaBitplaneEntryAtOrAfterCycle(
                RowDmaPlan plan,
                long lineStartCycle,
                long cycle,
                out int entryIndex)
            {
                var end = plan.BitplaneStart + plan.BitplaneCount;
                for (var index = plan.BitplaneStart; index < end; index++)
                {
                    if (_display._rowDmaBitplaneEntries[index].GetCycle(lineStartCycle) >= cycle)
                    {
                        entryIndex = index;
                        return true;
                    }
                }

                entryIndex = -1;
                return false;
            }

            private void AdvanceBitplaneCursor()
            {
                var state = GetScratchLineState(_nextFetchRow);
                _nextFetchSlot++;
                if (_nextFetchSlot < state.FetchSlotStride)
                {
                    return;
                }

                _nextFetchSlot = 0;
                _nextFetchPlane = 0;
                _nextFetchWord++;
                if (_nextFetchWord >= state.FetchWords)
                {
                    AdvanceBitplaneCursorToNextRow();
                }
            }

            private void AdvanceBitplaneCursorToNextRow()
            {
                _nextFetchRow++;
                _nextFetchWord = 0;
                _nextFetchPlane = 0;
                _nextFetchSlot = 0;
            }

            private long GetNextSpriteFetchCycle()
            {
                if (!IsSpriteDmaEnabled())
                {
                    return long.MaxValue;
                }

                if (!NormalizeSpriteCursor())
                {
                    return long.MaxValue;
                }

                return GetSpriteDmaFetchCycle(_display._liveFrameStartCycle, _nextSpriteRow, _nextSpriteIndex, _nextSpriteWord);
            }

            private bool NormalizeSpriteCursor()
            {
                while (_nextSpriteRow < LowResOutputHeight)
                {
                    if (!IsSpriteDmaEnabled())
                    {
                        return false;
                    }

                    if ((uint)_nextSpriteIndex >= (uint)_spriteStates.Length)
                    {
                        AdvanceSpriteCursor();
                        continue;
                    }

                    if (!_display.IsSpriteDmaChannelAvailable(_nextSpriteIndex) ||
                        _spriteExhausted[_nextSpriteIndex] ||
                        !CanSpriteSlotFetch(_nextSpriteRow, _nextSpriteIndex))
                    {
                        AdvanceSpriteCursor();
                        continue;
                    }

                    return true;
                }

                return false;
            }

            private bool CanSpriteSlotFetch(int row, int spriteIndex)
            {
                var state = _spriteStates[spriteIndex];
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

            private bool ReserveSpriteFetch()
            {
                var row = _nextSpriteRow;
                var spriteIndex = _nextSpriteIndex;
                var word = _nextSpriteWord;
                var slotCycle = GetSpriteDmaFetchCycle(_display._liveFrameStartCycle, row, spriteIndex, word);
                var address = GetSpriteScratchAddress(row, spriteIndex, word);
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Sprite,
                    AmigaBusAccessKind.Sprite,
                    AmigaBusAccessTarget.ChipRam,
                    address,
                    AmigaBusAccessSize.Word,
                    slotCycle,
                    isWrite: false);
                if (_slots.TryReserveExactFixedDmaSlot(request, out var access))
                {
                    _spriteFetches++;
                    RecordDma(access.GrantedCycle);
                    AdvanceSpriteStateAfterFetch(row, spriteIndex, word, address, access.GrantedCycle);
                }

                AdvanceSpriteCursor();
                return true;
            }

            private uint GetSpriteScratchAddress(int row, int spriteIndex, int word)
            {
                var state = _spriteStates[spriteIndex];
                if (!state.Active)
                {
                    return _display.AddDmaPointerOffset(state.ControlAddress, word * 2);
                }

                return _display.AddDmaPointerOffset(state.Descriptor.DataAddress, ((row - state.Descriptor.YStart) * 4) + (word * 2));
            }

            private void AdvanceSpriteStateAfterFetch(int row, int spriteIndex, int word, uint address, long grantedCycle)
            {
                var state = _spriteStates[spriteIndex];
                if (state.Active)
                {
                    return;
                }

                var value = ReadScratchChipWordForPresentation(address, grantedCycle);
                if (word == 0)
                {
                    state.PendingPos = value;
                    state.HasPendingPos = true;
                    return;
                }

                if (!state.HasPendingPos)
                {
                    _unsupported = "sprite-control";
                    return;
                }

                var pendingPos = state.PendingPos;
                state.PendingPos = 0;
                state.HasPendingPos = false;
                if ((pendingPos | value) == 0)
                {
                    state.Exhausted = true;
                    state.Active = false;
                    _spriteExhausted[spriteIndex] = true;
                    return;
                }

                var descriptor = CreateSpriteDescriptor(
                    pendingPos,
                    value,
                    _display.AddDmaPointerOffset(state.ControlAddress, 4),
                    isDma: true,
                    _spriteDataA[spriteIndex],
                    _spriteDataB[spriteIndex]);
                var rawHeight = Math.Max(0, descriptor.YStop - descriptor.YStart);
                state.ControlAddress = _display.AddDmaPointerOffset(descriptor.DataAddress, rawHeight * 4);
                if (state.LastVisibleStop >= 0 && descriptor.YStart <= state.LastVisibleStop)
                {
                    descriptor = descriptor.WithYStart(Math.Min(LowResOutputHeight, state.LastVisibleStop + 1));
                }

                if (descriptor.YStart < row)
                {
                    descriptor = descriptor.WithYStart(row);
                }

                if (descriptor.YStop <= descriptor.YStart)
                {
                    state.Exhausted = true;
                    state.Active = false;
                    _spriteExhausted[spriteIndex] = true;
                    return;
                }

                state.Descriptor = descriptor;
                state.Active = true;
                state.LastVisibleStop = Math.Max(state.LastVisibleStop, descriptor.YStop);
                state.ControlRow = Math.Clamp(descriptor.YStop + 1, 0, LowResOutputHeight);
            }

            private void AdvanceSpriteCursor()
            {
                _nextSpriteWord++;
                if (_nextSpriteWord < LiveSpriteWordsPerChannel)
                {
                    return;
                }

                _nextSpriteWord = 0;
                _nextSpriteIndex++;
                if (_nextSpriteIndex < LiveSpriteChannelCount)
                {
                    return;
                }

                _nextSpriteIndex = 0;
                _nextSpriteRow++;
            }

            private long GetNextCopperCycle(long targetCycle)
            {
                if (_copperFirstWordPending)
                {
                    return GetCopperSecondWordRequestCycle(_copperFirstWordAccess);
                }

                if (_copper.PendingMove)
                {
                    return _copper.PendingMoveCycle;
                }

                if (_copper.PendingSkip)
                {
                    return _copper.PendingSkipCycle;
                }

                if (_copper.Stopped || !IsLiveCopperDmaEnabled())
                {
                    return long.MaxValue;
                }

                if (_copper.PendingStart)
                {
                    return _display._copperListPointer == 0
                        ? long.MaxValue
                        : Math.Max(_copper.Cycle, _display._liveFrameStartCycle);
                }

                if (_copper.Pc == 0 && _display._copperListPointer == 0)
                {
                    return long.MaxValue;
                }

                if (_copper.Waiting)
                {
                    if (!_display.TryGetCopperWaitCycle(
                            _copper.WaitFirst,
                            _copper.WaitSecond,
                            _display._liveFrameStartCycle,
                            _copper.Cycle,
                            targetCycle + 1,
                            blitterFinished: true,
                            out var waitCycle))
                    {
                        return long.MaxValue;
                    }

                    return waitCycle;
                }

                return Math.Max(_copper.Cycle, _display._liveFrameStartCycle);
            }

            private bool StepCopper(long targetCycle)
            {
                if (_copperFirstWordPending)
                {
                    var secondRequestCycle = GetCopperSecondWordRequestCycle(_copperFirstWordAccess);
                    if (secondRequestCycle > targetCycle)
                    {
                        return true;
                    }

                    var secondAddress = _display.AddDmaPointerOffset(_copperFirstWordAddress, 2);
                    var secondAccess = _slots.ReserveCopperDmaSlot(secondAddress, secondRequestCycle);
                    var second = ReadScratchChipWordForPresentation(secondAddress, secondAccess.GrantedCycle);
                    RecordDma(secondAccess.GrantedCycle);
                    var instruction = new CopperInstructionLatch(
                        _copperFirstWord,
                        _copperFirstWordAccess,
                        second,
                        secondAccess);
                    _copperFirstWordPending = false;
                    _copper.Pc = _display.AddDmaPointerOffset(_copper.Pc, 4);
                    return ApplyLoadedCopperInstruction(instruction, targetCycle);
                }

                if (_copper.PendingMove)
                {
                    if (_copper.PendingMoveCycle > targetCycle)
                    {
                        return true;
                    }

                    var register = _copper.PendingMoveRegister;
                    var value = _copper.PendingMoveValue;
                    var dataCycle = _copper.PendingMoveCycle;
                    var stopCycle = _copper.PendingMoveStopCycle;
                    var suppress = _copper.PendingMoveSuppress;
                    _copper.PendingMove = false;
                    return ApplyCopperMove(register, value, dataCycle, stopCycle, suppress);
                }

                if (_copper.PendingSkip)
                {
                    if (_copper.PendingSkipCycle > targetCycle)
                    {
                        return true;
                    }

                    var first = _copper.PendingSkipFirst;
                    var second = _copper.PendingSkipSecond;
                    var skipCycle = _copper.PendingSkipCycle;
                    _copper.PendingSkip = false;
                    if (IsCopperComparisonSatisfied(
                            first,
                            second,
                            _display._liveFrameStartCycle,
                            skipCycle,
                            _display.IsCopperBlitterFinishedForWait(second)))
                    {
                        _copper.SuppressNextMove = true;
                    }

                    _copper.Cycle = skipCycle;
                    return true;
                }

                if (_copper.Stopped || !IsLiveCopperDmaEnabled())
                {
                    return true;
                }

                if (_copper.PendingStart)
                {
                    if (_display._copperListPointer == 0)
                    {
                        return true;
                    }

                    _copper.StartFrom(_display._copperListPointer);
                }

                if (_copper.Waiting)
                {
                    var blitterReadyCycle = _display.GetCopperBlitterReadyCycle(_copper.WaitSecond, _copper.Cycle);
                    if (blitterReadyCycle > _copper.Cycle)
                    {
                        _copper.Cycle = Math.Min(blitterReadyCycle, targetCycle);
                        if (_copper.Cycle < blitterReadyCycle)
                        {
                            return true;
                        }
                    }

                    long waitCycle;
                    if (blitterReadyCycle <= _copper.Cycle)
                    {
                        if (!_display.TryGetCopperWaitCycle(
                                _copper.WaitFirst,
                                _copper.WaitSecond,
                                _display._liveFrameStartCycle,
                                _copper.Cycle,
                                targetCycle + 1,
                                blitterFinished: true,
                                out waitCycle))
                        {
                            _copper.Cycle = targetCycle + 1;
                            return true;
                        }
                    }
                    else if (!_display.TryGetCopperWaitCycle(
                            _copper.WaitFirst,
                            _copper.WaitSecond,
                            _display._liveFrameStartCycle,
                            _copper.Cycle,
                            targetCycle + 1,
                            blitterFinished: true,
                            out waitCycle))
                    {
                        _copper.Cycle = targetCycle + 1;
                        return true;
                    }

                    var resumeCycle = waitCycle + CopperHpToCpuCycles(CopperWaitWakeHpUnits);
                    if (resumeCycle > targetCycle)
                    {
                        _copper.Cycle = resumeCycle;
                        _copper.Waiting = false;
                        return true;
                    }

                    _copper.Cycle = resumeCycle;
                    _copper.Waiting = false;
                    return true;
                }

                BeginCopperInstruction(_copper.Pc, Math.Min(_copper.Cycle, targetCycle));
                return true;
            }

            private bool ApplyLoadedCopperInstruction(CopperInstructionLatch instruction, long targetCycle)
            {
                if (instruction.IsEnd)
                {
                    _copper.Stopped = true;
                    _copper.Cycle = instruction.MoveStopCycle;
                    return true;
                }

                if (instruction.IsMove)
                {
                    var register = instruction.MoveRegister;
                    var suppress = _copper.SuppressNextMove;
                    _copper.SuppressNextMove = false;
                    if (instruction.DataCycle > targetCycle)
                    {
                        _copper.PendingMove = true;
                        _copper.PendingMoveRegister = register;
                        _copper.PendingMoveValue = instruction.Second;
                        _copper.PendingMoveCycle = instruction.DataCycle;
                        _copper.PendingMoveStopCycle = instruction.MoveStopCycle;
                        _copper.PendingMoveSuppress = suppress;
                        _copper.Cycle = instruction.DataCycle;
                        return true;
                    }

                    return ApplyCopperMove(register, instruction.Second, instruction.DataCycle, instruction.MoveStopCycle, suppress);
                }

                if (instruction.IsWait)
                {
                    _copper.Cycle = instruction.ControlStopCycle;
                    _copper.Wait(instruction.First, instruction.Second);
                    return true;
                }

                if (instruction.ControlStopCycle > targetCycle)
                {
                    _copper.PendingSkip = true;
                    _copper.PendingSkipFirst = instruction.First;
                    _copper.PendingSkipSecond = instruction.Second;
                    _copper.PendingSkipCycle = instruction.ControlStopCycle;
                    _copper.Cycle = instruction.ControlStopCycle;
                    return true;
                }

                if (IsCopperComparisonSatisfied(
                        instruction.First,
                        instruction.Second,
                        _display._liveFrameStartCycle,
                        instruction.ControlStopCycle,
                        _display.IsCopperBlitterFinishedForWait(instruction.Second)))
                {
                    _copper.SuppressNextMove = true;
                }

                _copper.Cycle = instruction.ControlStopCycle;
                return true;
            }

            private void BeginCopperInstruction(uint pc, long fetchCycle)
            {
                _copperFirstWordAddress = pc;
                _copperFirstWordAccess = _slots.ReserveCopperDmaSlot(pc, fetchCycle);
                _copperFirstWord = ReadScratchChipWordForPresentation(pc, _copperFirstWordAccess.GrantedCycle);
                RecordDma(_copperFirstWordAccess.GrantedCycle);
                _copperFirstWordPending = true;
                _copper.Cycle = GetCopperSecondWordRequestCycle(_copperFirstWordAccess);
            }

            private bool ApplyCopperMove(
                ushort register,
                ushort value,
                long dataCycle,
                long instructionStopCycle,
                bool suppressMove)
            {
                if (IsCopperDangerStopRegister(register))
                {
                    _copper.Stopped = true;
                    _copper.Cycle = instructionStopCycle;
                    return true;
                }

                if (!suppressMove && CanCopperWriteRegister(register))
                {
                    if (!ApplyScratchCopperMove(register, value, dataCycle))
                    {
                        return false;
                    }
                }

                _copper.Cycle = instructionStopCycle;
                return true;
            }

            private bool ApplyScratchCopperMove(ushort register, ushort value, long cycle)
            {
                if (register == 0x096)
                {
                    PreserveStartedBitplaneRow(cycle);
                    var bitplaneDmaWasEnabled = IsBitplaneDmaEnabled(_dmacon);
                    _dmacon = ApplySetClearPreview(_dmacon, value);
                    if (bitplaneDmaWasEnabled != IsBitplaneDmaEnabled(_dmacon))
                    {
                        MarkDynamicBitplaneScheduleChange(cycle);
                    }

                    if (!bitplaneDmaWasEnabled && IsBitplaneDmaEnabled(_dmacon))
                    {
                        SetScratchBitplaneBaseRows(
                            0,
                            GetAgnusBitplaneFetchPlaneCount(_bplcon0),
                            GetCurrentBitplaneBaseRow(cycle));
                    }

                    ResetBitplaneCursorsFromCycle(cycle);
                    return true;
                }

                if (register == 0x098)
                {
                    return true;
                }

                if (register == 0x100)
                {
                    PreserveStartedBitplaneRow(cycle);
                    var oldPlaneCount = GetAgnusBitplaneFetchPlaneCount(_bplcon0);
                    var newPlaneCount = GetAgnusBitplaneFetchPlaneCount(value);
                    if (oldPlaneCount != newPlaneCount)
                    {
                        MarkDynamicBitplaneScheduleChange(cycle);
                    }

                    _bplcon0 = value;
                    if (newPlaneCount > oldPlaneCount && IsBitplaneDmaEnabled(_dmacon))
                    {
                        SetScratchBitplaneBaseRows(
                            oldPlaneCount,
                            newPlaneCount,
                            GetCurrentBitplaneBaseRow(cycle));
                    }

                    ResetBitplaneCursorsFromCycle(cycle);
                    return true;
                }

                if (register == 0x102)
                {
                    _bplcon1 = value;
                    return true;
                }

                if (register == 0x104)
                {
                    _bplcon2 = value;
                    return true;
                }

                if (register == 0x02E)
                {
                    _copcon = value;
                    return true;
                }

                if (register == 0x080)
                {
                    _copperListPointer = _display.WriteDmaPointerHigh(_copperListPointer, value);
                    return true;
                }

                if (register == 0x082)
                {
                    _copperListPointer = _display.WriteDmaPointerLow(_copperListPointer, value);
                    return true;
                }

                if (register == 0x084)
                {
                    _copperListPointer2 = _display.WriteDmaPointerHigh(_copperListPointer2, value);
                    return true;
                }

                if (register == 0x086)
                {
                    _copperListPointer2 = _display.WriteDmaPointerLow(_copperListPointer2, value);
                    return true;
                }

                if (register == 0x088)
                {
                    _copper.JumpTo(_copperListPointer, cycle);
                    return true;
                }

                if (register == 0x08A)
                {
                    _copper.JumpTo(_copperListPointer2, cycle);
                    return true;
                }

                if (register == 0x08E)
                {
                    MarkDynamicBitplaneScheduleChange(cycle);
                    PreserveStartedBitplaneRow(cycle);
                    _diwStart = value;
                    ResetBitplaneCursorsFromCycle(cycle);
                    return true;
                }

                if (register == 0x090)
                {
                    MarkDynamicBitplaneScheduleChange(cycle);
                    PreserveStartedBitplaneRow(cycle);
                    _diwStop = value;
                    ResetBitplaneCursorsFromCycle(cycle);
                    return true;
                }

                if (register == 0x092)
                {
                    MarkDynamicBitplaneScheduleChange(cycle);
                    PreserveStartedBitplaneRow(cycle);
                    _ddfStart = value;
                    ResetBitplaneCursorsFromCycle(cycle);
                    return true;
                }

                if (register == 0x094)
                {
                    MarkDynamicBitplaneScheduleChange(cycle);
                    PreserveStartedBitplaneRow(cycle);
                    _ddfStop = value;
                    ResetBitplaneCursorsFromCycle(cycle);
                    return true;
                }

                if (register == 0x108)
                {
                    _bpl1mod = unchecked((short)value);
                    return true;
                }

                if (register == 0x10A)
                {
                    _bpl2mod = unchecked((short)value);
                    return true;
                }

                if (register >= 0x110 && register <= 0x11A)
                {
                    var plane = (register - 0x110) / 2;
                    if ((uint)plane < (uint)_bitplaneDataRegisters.Length)
                    {
                        _bitplaneDataRegisters[plane] = value;
                    }

                    return true;
                }

                if (register >= 0x180 && register < 0x1C0)
                {
                    return true;
                }

                if (register >= 0x0E0 && register <= 0x0F6)
                {
                    var plane = (register - 0x0E0) / 4;
                    if ((uint)plane < (uint)_bitplanePointers.Length)
                    {
                        _bitplanePointers[plane] = (register & 2) == 0
                            ? _display.WriteDmaPointerHigh(_bitplanePointers[plane], value)
                            : _display.WriteDmaPointerLow(_bitplanePointers[plane], value);
                        _bitplaneBaseRows[plane] = GetCurrentBitplaneBaseRow(cycle);
                    }

                    return true;
                }

                if (register >= 0x120 && register < 0x180)
                {
                    if (IsBitplaneDmaEnabled(_dmacon) && GetAgnusBitplaneFetchPlaneCount(_bplcon0) > 0)
                    {
                        _unsupported = $"copper-sprite-bitplane-{register:X3}";
                        return false;
                    }

                    ApplyScratchSpriteWrite(register, value, cycle);
                    return true;
                }

                if (HasCopperHardwareSideEffect(register) ||
                    CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(register))
                {
                    _unsupported = $"copper-sidefx-{register:X3}";
                    return false;
                }

                return true;
            }

            private void SetScratchBitplaneBaseRows(int startPlane, int stopPlane, int row)
            {
                startPlane = Math.Clamp(startPlane, 0, _bitplaneBaseRows.Length);
                stopPlane = Math.Clamp(stopPlane, startPlane, _bitplaneBaseRows.Length);
                for (var plane = startPlane; plane < stopPlane; plane++)
                {
                    _bitplaneBaseRows[plane] = row;
                }
            }

            private void MarkDynamicBitplaneScheduleChange(long cycle)
            {
                if (cycle >= _requestCycle)
                {
                    _dynamicBitplaneScheduleChanged = true;
                }
            }

            private void ApplyScratchSpriteWrite(ushort register, ushort value, long cycle)
            {
                _spriteRegisterWriteSeen = true;
                if (register >= 0x120 && register < 0x140)
                {
                    var sprite = (register - 0x120) / 4;
                    if ((uint)sprite >= (uint)_spritePointers.Length)
                    {
                        return;
                    }

                    _spritePointers[sprite] = (register & 2) == 0
                        ? _display.WriteDmaPointerHigh(_spritePointers[sprite], value)
                        : _display.WriteDmaPointerLow(_spritePointers[sprite], value);
                    UpdateSpriteDmaPointerFromRegisterWrite(sprite, GetSpriteControlRow(cycle));
                    return;
                }

                if (register >= 0x140 && register < 0x180)
                {
                    var sprite = (register - 0x140) / 8;
                    var spriteRegister = (register - 0x140) % 8;
                    if ((uint)sprite >= (uint)_spritePointers.Length)
                    {
                        return;
                    }

                    switch (spriteRegister)
                    {
                        case 0:
                            _spritePos[sprite] = value;
                            break;
                        case 2:
                            _spriteCtl[sprite] = value;
                            _spriteManualArmed[sprite] = false;
                            break;
                        case 4:
                            _spriteDataA[sprite] = value;
                            _spriteManualArmed[sprite] = true;
                            break;
                        case 6:
                            _spriteDataB[sprite] = value;
                            break;
                    }
                }
            }

            private void UpdateSpriteDmaPointerFromRegisterWrite(int spriteIndex, int controlRow)
            {
                if ((uint)spriteIndex >= (uint)_spriteStates.Length)
                {
                    return;
                }

                var state = _spriteStates[spriteIndex];
                if (state.Exhausted || _spriteExhausted[spriteIndex])
                {
                    return;
                }

                controlRow = Math.Clamp(controlRow, 0, LowResOutputHeight);
                if (state.LastVisibleStop >= 0 && controlRow <= state.LastVisibleStop)
                {
                    state.ControlAddress = _spritePointers[spriteIndex];
                    return;
                }

                if (state.Active || state.HasPendingPos)
                {
                    state.ControlAddress = _spritePointers[spriteIndex];
                    return;
                }

                if (controlRow > state.ControlRow)
                {
                    return;
                }

                state.ControlAddress = _spritePointers[spriteIndex];
                state.PendingPos = 0;
                state.HasPendingPos = false;
                _nextSpriteRow = Math.Min(_nextSpriteRow, state.ControlRow);
                _nextSpriteIndex = 0;
                _nextSpriteWord = 0;
            }

            private int GetSpriteControlRow(long cycle)
                => Math.Clamp(GetOutputRowForCycle(_display._liveFrameStartCycle, cycle), 0, LowResOutputHeight);

            private LiveLineState GetScratchLineState(int row)
            {
                row = Math.Clamp(row, 0, LowResOutputHeight - 1);
                var state = _lineStates[row];
                if (state == null)
                {
                    state = new LiveLineState();
                    _lineStates[row] = state;
                }

                if (state.Generation != 0)
                {
                    return state;
                }

                CaptureScratchLineState(row, state);
                return state;
            }

            private void CaptureScratchLineState(int row, LiveLineState state)
            {
                if (!_lineStateDirtyFromScratchWrite[row] && _display.IsLiveLineValid(row))
                {
                    CopyScratchLineState(_display._liveLineStates[row], state);
                    return;
                }

                CaptureScratchLineStateFromCurrentRegisters(row, state);
                _lineStateDirtyFromScratchWrite[row] = false;
            }

            private void CaptureScratchLineStateFromCurrentRegisters(int row)
            {
                if ((uint)row >= (uint)LowResOutputHeight)
                {
                    return;
                }

                var state = _lineStates[row];
                if (state == null)
                {
                    state = new LiveLineState();
                    _lineStates[row] = state;
                }

                CaptureScratchLineStateFromCurrentRegisters(row, state);
            }

            private void CaptureScratchLineStateFromCurrentRegisters(int row, LiveLineState state)
            {
                AdvanceDisplayWindowStateToLine(StandardVStart + row);
                state.Generation = _display._liveGeneration;
                state.LineStartCycle = GetOutputRowStartCycle(_display._liveFrameStartCycle, row);
                state.DisplayWindowVerticallyOpen = _displayWindowVerticallyOpen;
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
                state.PlaneCount = GetAgnusBitplaneFetchPlaneCount(_bplcon0);
                state.DecodePlaneCount = GetDeniseBitplaneDecodePlaneCount(_bplcon0);
                state.FetchWords = GetDataFetchWordCount();
                state.DataFetchStart = GetDataFetchStartValue();
                state.FetchSlotStride = GetBitplaneFetchSlotStride(IsHighResolutionEnabled(_bplcon0));
                state.PaletteSnapshotIndex = -1;
                state.PlaneHasRowMask = 0;
                Array.Copy(_bitplanePointers, state.BitplanePointers, _bitplanePointers.Length);
                Array.Copy(_bitplaneBaseRows, state.BitplaneBaseRows, _bitplaneBaseRows.Length);
                Array.Copy(_bitplaneDataRegisters, state.BitplaneDataRegisters, _bitplaneDataRegisters.Length);
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
            }

            private static void CopyScratchLineState(LiveLineState source, LiveLineState destination)
            {
                destination.Generation = source.Generation;
                destination.LineStartCycle = source.LineStartCycle;
                destination.DisplayWindowVerticallyOpen = source.DisplayWindowVerticallyOpen;
                destination.Bplcon0 = source.Bplcon0;
                destination.Bplcon1 = source.Bplcon1;
                destination.Bplcon2 = source.Bplcon2;
                destination.DiwStart = source.DiwStart;
                destination.DiwStop = source.DiwStop;
                destination.DdfStart = source.DdfStart;
                destination.DdfStop = source.DdfStop;
                destination.Dmacon = source.Dmacon;
                destination.Bpl1Mod = source.Bpl1Mod;
                destination.Bpl2Mod = source.Bpl2Mod;
                destination.PlaneCount = source.PlaneCount;
                destination.DecodePlaneCount = source.DecodePlaneCount;
                destination.FetchWords = source.FetchWords;
                destination.DataFetchStart = source.DataFetchStart;
                destination.FetchSlotStride = source.FetchSlotStride;
                destination.PaletteSnapshotIndex = source.PaletteSnapshotIndex;
                destination.PlaneHasRowMask = source.PlaneHasRowMask;
                Array.Copy(source.BitplanePointers, destination.BitplanePointers, source.BitplanePointers.Length);
                Array.Copy(source.BitplaneBaseRows, destination.BitplaneBaseRows, source.BitplaneBaseRows.Length);
                Array.Copy(source.BitplaneRowAddresses, destination.BitplaneRowAddresses, source.BitplaneRowAddresses.Length);
                Array.Copy(source.BitplaneDataRegisters, destination.BitplaneDataRegisters, source.BitplaneDataRegisters.Length);
            }

            private void ResetBitplaneCursorsFromCycle(long cycle)
            {
                var row = Math.Clamp(GetOutputRowForCycle(_display._liveFrameStartCycle, cycle), 0, LowResOutputHeight - 1);
                var currentRowStarted = HasStartedBitplaneFetch(row, cycle);
                var invalidateRow = currentRowStarted
                    ? Math.Min(row + 1, LowResOutputHeight)
                    : row;
                for (var i = invalidateRow; i < _lineStates.Length; i++)
                {
                    _lineStates[i]?.Generation = 0;
                    _lineStateDirtyFromScratchWrite[i] = true;
                }

                if (!currentRowStarted && _nextFetchRow >= row)
                {
                    _nextFetchRow = row;
                    _nextFetchWord = 0;
                    _nextFetchPlane = 0;
                    _nextFetchSlot = 0;
                }
                else if (currentRowStarted && _nextFetchRow > row)
                {
                    _nextFetchRow = Math.Min(_nextFetchRow, invalidateRow);
                    _nextFetchWord = 0;
                    _nextFetchPlane = 0;
                    _nextFetchSlot = 0;
                }

                if (!currentRowStarted && _preparedFetchRow >= row)
                {
                    _preparedFetchRow = row;
                    _preparedFetchWord = 0;
                    _preparedFetchPlane = 0;
                    _preparedFetchSlot = 0;
                }
                else if (currentRowStarted && _preparedFetchRow > row)
                {
                    _preparedFetchRow = Math.Min(_preparedFetchRow, invalidateRow);
                    _preparedFetchWord = 0;
                    _preparedFetchPlane = 0;
                    _preparedFetchSlot = 0;
                }
            }

            private void PreserveStartedBitplaneRow(long cycle)
            {
                var row = Math.Clamp(GetOutputRowForCycle(_display._liveFrameStartCycle, cycle), 0, LowResOutputHeight - 1);
                var state = GetScratchLineState(row);
                if (HasStartedBitplaneFetch(row, cycle))
                {
                    return;
                }

                var currentRegisterState = new LiveLineState();
                CaptureScratchLineStateFromCurrentRegisters(row, currentRegisterState);
                if (HasStartedBitplaneFetch(currentRegisterState, cycle))
                {
                    CopyScratchLineState(currentRegisterState, state);
                    return;
                }

                state.Generation = 0;
            }

            private bool HasStartedBitplaneFetch(int row, long cycle)
            {
                var state = _lineStates[row];
                if (state == null ||
                    state.Generation == 0 ||
                    state.PlaneCount <= 0 ||
                    state.FetchWords <= 0 ||
                    !state.DisplayWindowVerticallyOpen ||
                    !IsBitplaneDmaEnabled(state.Dmacon))
                {
                    return false;
                }

                return HasStartedBitplaneFetch(state, cycle);
            }

            private static bool HasStartedBitplaneFetch(LiveLineState state, long cycle)
            {
                if (state.PlaneCount <= 0 ||
                    state.FetchWords <= 0 ||
                    !state.DisplayWindowVerticallyOpen ||
                    !IsBitplaneDmaEnabled(state.Dmacon))
                {
                    return false;
                }

                for (var slot = 0; slot < state.FetchSlotStride; slot++)
                {
                    if (TryGetBitplanePlaneForFetchSlot(slot, Math.Max(0, state.PlaneCount), state.FetchSlotStride, out _))
                    {
                        var fetchHorizontal = state.DataFetchStart + slot;
                        var fetchCycle = AgnusChipSlotScheduler.AlignToSlot(state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
                        return fetchCycle <= cycle;
                    }
                }

                return false;
            }

            private void AdvanceDisplayWindowStateToLine(int targetLine)
            {
                targetLine = Math.Clamp(targetLine, 0, AmigaConstants.A500PalRasterLines - 1);
                while (_displayWindowStateLine <= targetLine)
                {
                    var vStart = (_diwStart >> 8) & 0x00FF;
                    var vStop = (_diwStop >> 8) & 0x00FF;
                    if (vStop < 0x80)
                    {
                        vStop += 0x100;
                    }

                    if (vStop <= vStart)
                    {
                        vStop += 0x100;
                    }

                    if (_displayWindowStateLine == vStop)
                    {
                        _displayWindowVerticallyOpen = false;
                    }

                    if (_displayWindowStateLine == vStart)
                    {
                        _displayWindowVerticallyOpen = true;
                    }

                    _displayWindowStateLine++;
                }
            }

            private int GetCurrentBitplaneBaseRow(long cycle)
            {
                var windowY = ((_diwStart >> 8) & 0x00FF) - StandardVStart;
                var row = GetOutputRowForCycle(_display._liveFrameStartCycle, cycle);
                if (row == 0 && windowY < 0)
                {
                    return windowY;
                }

                return Math.Max(row, windowY);
            }

            private int GetDataFetchWordCount()
            {
                var ddfStart = GetDataFetchStartValue();
                var ddfStop = GetDataFetchStopValue();
                if (ddfStop < ddfStart)
                {
                    return 0;
                }

                if (IsHighResolutionEnabled(_bplcon0))
                {
                    var fetchWords = ((ddfStop - ddfStart) / 4) + 2;
                    if (ddfStart == DefaultHighResDdfStart && ddfStop == DefaultDdfStop)
                    {
                        fetchWords++;
                    }

                    return Math.Clamp(fetchWords, 0, MaxBitplaneFetchWords);
                }

                return Math.Clamp(((ddfStop - ddfStart) / 8) + 1, 0, MaxBitplaneFetchWords);
            }

            private int GetDataFetchStartValue()
                => _ddfStart & (IsHighResolutionEnabled(_bplcon0) ? 0x00FC : 0x00F8);

            private int GetDataFetchStopValue()
            {
                if (IsHighResolutionEnabled(_bplcon0))
                {
                    return _ddfStop & 0x00FC;
                }

                var blockStart = _ddfStop & 0x00F8;
                return (_ddfStop & 0x0004) != 0
                    ? blockStart + 8
                    : blockStart;
            }

            private bool CanCopperWriteRegister(ushort offset)
            {
                if (offset < 0x010)
                {
                    return false;
                }

                return offset >= 0x020 || (_copcon & CopconCopperDanger) != 0;
            }

            private bool IsCopperDangerStopRegister(ushort offset)
            {
                if (offset < 0x010)
                {
                    return true;
                }

                return offset < 0x020 && (_copcon & CopconCopperDanger) == 0;
            }

            private void RecordDma(long cycle)
            {
                if (_firstDmaCycle < 0 || cycle < _firstDmaCycle)
                {
                    _firstDmaCycle = cycle;
                }

                if (_lastDmaCycle < 0 || cycle > _lastDmaCycle)
                {
                    _lastDmaCycle = cycle;
                }
            }

            private bool IsLiveCopperDmaEnabled()
                => (_dmacon & (DmaconMasterEnable | DmaconCopperEnable)) == (DmaconMasterEnable | DmaconCopperEnable);

            private bool IsSpriteDmaEnabled()
                => (_dmacon & (DmaconMasterEnable | DmaconSpriteEnable)) == (DmaconMasterEnable | DmaconSpriteEnable);

            private static LiveSpriteDmaState CloneSpriteState(LiveSpriteDmaState source)
            {
                return new LiveSpriteDmaState
                {
                    ControlAddress = source.ControlAddress,
                    ControlRow = source.ControlRow,
                    Exhausted = source.Exhausted,
                    HasPendingPos = source.HasPendingPos,
                    PendingPos = source.PendingPos,
                    Active = source.Active,
                    Descriptor = source.Descriptor,
                    LastVisibleStop = source.LastVisibleStop
                };
            }

            private readonly struct ScratchChipByteWrite
            {
                public ScratchChipByteWrite(uint address, byte value, long cycle)
                {
                    Address = address;
                    Value = value;
                    Cycle = cycle;
                }

                public uint Address { get; }

                public byte Value { get; }

                public long Cycle { get; }
            }
        }


    }
}
