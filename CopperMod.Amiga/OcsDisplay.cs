/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CopperMod.Amiga
{
    internal sealed class OcsDisplay
    {
        private const int MaxPendingWrites = 65536;
        private const int StandardHStart = 0x81 - AmigaConstants.PalLowResOverscanBorderX;
        private const int StandardVStart = 0x2C - AmigaConstants.PalLowResOverscanBorderY;
        private const ushort DefaultDiwStart = 0x2C81;
        private const ushort DefaultDiwStop = 0x2CC1;
        private const ushort DefaultDdfStart = 0x0038;
        private const ushort DefaultDdfStop = 0x00D0;
        private const ushort DefaultHighResDdfStart = 0x003C;
        private const ushort DmaconMasterEnable = 0x0200;
        private const ushort DmaconBitplaneEnable = 0x0100;
        private const ushort DmaconCopperEnable = 0x0080;
        private const ushort DmaconSpriteEnable = 0x0020;
        private const ushort DmaconWritableMask = 0x07FF;
        // SPRxPOS stores H8-H1 and SPRxCTL stores H0; after reconstruction,
        // the normal visible left edge is comparator coordinate $80.
        private const int StandardSpriteHorizontalOffset = 128 - AmigaConstants.PalLowResOverscanBorderX;
        private const int MaxBitplaneFetchWords = 64;
        private const byte Playfield1PriorityMask = 0x01;
        private const byte Playfield2PriorityMask = 0x02;
        private const byte NormalPlayfieldPriorityMask = 0x04;
        private const int LowResOutputHeight = AmigaConstants.PalLowResHeight;
        private const int LastCopperHorizontal = 0xE2;
        private const int CopperHorizontalUnitsPerLine = 227;
        private const int CopperInstructionDataHpUnits = 2;
        private const int CopperMoveHpUnits = 4;
        private const int CopperSkipHpUnits = 6;
        // WAIT is a 3-memory-cycle instruction total; after the fetched WAIT is parked,
        // only the extra wake memory cycle remains.
        private const int CopperWaitWakeHpUnits = 2;
        private const int CopperWaitLineEndBlackoutHpUnits = 4;
        private const ushort CopconCopperDanger = 0x0002;
        private const long PalFrameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        private const int PalLineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        private const int CopperHpCycles = AmigaConstants.A500PalCpuCyclesPerColorClock;
        private const int PaletteColorCount = 64;
        private const int MaxPaletteFrameSpans = MaxPendingWrites;
        private const int MaxBitplaneDataSpans = MaxPendingWrites;
        private const int MaxSpriteFrameCommands = 256;
        private const int LiveBitplanePlaneCount = 6;
        private const int LiveBitplaneWordsPerRow = LiveBitplanePlaneCount * MaxBitplaneFetchWords;
        private const int LiveSpriteChannelCount = 8;
        private const int LiveSpriteWordsPerChannel = 2;
        private const int LiveSpriteWordsPerRow = LiveSpriteChannelCount * LiveSpriteWordsPerChannel;
        private const int MaxRowDmaBitplaneEntriesPerRow = LiveBitplanePlaneCount * MaxBitplaneFetchWords;
        private const int MaxRowDmaSpriteEntriesPerRow = LiveSpriteChannelCount * LiveSpriteWordsPerChannel;
        private const byte RowDmaExecutedBitplaneMask = 0x01;
        private const byte RowDmaExecutedSpriteMask = 0x02;
        private const int MaxLivePaletteSnapshots = MaxPendingWrites;
        private const int MaxTimelineStateSnapshots = MaxPendingWrites;
        private const int PlanarChunkPixels = 16;
        private const int MaxTimelineSegmentsPerFrame = LowResOutputHeight + MaxPendingWrites;
        private const int MaxLiveRasterlinePlanEvents = 64;
        private static readonly int[] LowResBitplaneFetchSlotsByPlane = [7, 3, 5, 1, 6, 2];
        private static readonly int[] HighResBitplaneFetchSlotsByPlane = [3, 1, 2, 0];
        private static readonly sbyte[] LowResBitplanePlanesByFetchSlot = [-1, 3, 5, 1, -1, 2, 4, 0];
        private static readonly sbyte[] HighResBitplanePlanesByFetchSlot = [3, 1, 2, 0];
        private readonly AmigaBus _bus;
        private readonly bool _liveDmaEnabled;
        private readonly List<PendingCustomWrite> _pendingWrites = new List<PendingCustomWrite>(MaxPendingWrites);
        private readonly ushort[] _colors = new ushort[32];
        private readonly uint[] _convertedColors = new uint[PaletteColorCount];
        private readonly uint[] _bitplanePointers = new uint[6];
        private readonly int[] _bitplaneBaseRows = new int[6];
        private byte[] _playfieldPriorityMasks = new byte[AmigaConstants.PalLowResWidth * LowResOutputHeight];
        private readonly ushort[,] _renderPlaneWords = new ushort[6, MaxBitplaneFetchWords];
        private readonly bool[] _renderPlaneHasRow = new bool[6];
        private readonly ushort[] _bitplaneDataRegisters = new ushort[6];
        private readonly bool[] _bitplaneDataRegisterWritten = new bool[6];
        private BitplaneDmaReadLatch _bitplaneDmaReadLatch;
        private readonly SpriteState[] _sprites = new SpriteState[8];
        private readonly List<SpriteFrameCommand> _spriteFrameCommands = new List<SpriteFrameCommand>(MaxSpriteFrameCommands * 8);
        private readonly List<SpriteFrameCommand>[] _spriteCommandScratch = new List<SpriteFrameCommand>[8];
        private readonly bool[] _evenSpriteAttached = new bool[MaxSpriteFrameCommands];
        private readonly bool[] _oddSpriteAttached = new bool[MaxSpriteFrameCommands];
        private readonly List<PaletteFrameSpan> _paletteFrameSpans = new List<PaletteFrameSpan>(MaxPaletteFrameSpans);
        private readonly List<BitplaneDataSpan> _bitplaneDataSpans = new List<BitplaneDataSpan>(MaxBitplaneDataSpans);
        private readonly uint[] _paletteFrameSpanColors = new uint[MaxPaletteFrameSpans * PaletteColorCount];
        private readonly byte[] _timelineFastPathColorIndexes = new byte[AmigaConstants.PalLowResWidth];
        private readonly byte[] _timelineFastPathPriorityMasks = new byte[AmigaConstants.PalLowResWidth];
        private readonly SavedDisplayState _savedDisplayState = new SavedDisplayState();
        private readonly SavedDisplayState _liveFrameInitialState = new SavedDisplayState();
        private readonly List<PendingCustomWrite> _liveFrameWrites = new List<PendingCustomWrite>(MaxPendingWrites);
        private int _pendingIndex;
        private uint _copperListPointer;
        private uint _copperListPointer2;
        private ushort _diwStart;
        private ushort _diwStop;
        private ushort _ddfStart;
        private ushort _ddfStop;
        private ushort _bplcon0;
        private ushort _bplcon1;
        private ushort _bplcon2;
        private ushort _copcon;
        private ushort _dmacon;
        private short _bpl1mod;
        private short _bpl2mod;
        private int _renderWidth;
        private int _renderHeight;
        private int _renderInterlaceField;
        private bool _trackDisplayWindowState;
        private bool _displayWindowVerticallyOpen;
        private int _displayWindowStateLine;
        private bool _liveDisplayWindowVerticallyOpen;
        private int _liveDisplayWindowStateLine;
        private int _lastBitplaneNonZeroPixels;
        private int _lastBitplaneRows;
        private int _lastBitplaneWords;
        private int _lastBitplaneMinX;
        private int _lastBitplaneMinY;
        private int _lastBitplaneMaxX;
        private int _lastBitplaneMaxY;
        private int _lastNormalPlayfieldNonZeroPixels;
        private int _lastNormalPlayfieldMinX;
        private int _lastNormalPlayfieldMinY;
        private int _lastNormalPlayfieldMaxX;
        private int _lastNormalPlayfieldMaxY;
        private int _lastPlayfield1NonZeroPixels;
        private int _lastPlayfield1MinX;
        private int _lastPlayfield1MinY;
        private int _lastPlayfield1MaxX;
        private int _lastPlayfield1MaxY;
        private int _lastPlayfield2NonZeroPixels;
        private int _lastPlayfield2MinX;
        private int _lastPlayfield2MinY;
        private int _lastPlayfield2MaxX;
        private int _lastPlayfield2MaxY;
        private readonly int[] _lastBitplaneColorCounts = new int[64];
        private int _lastSpriteNonZeroPixels;
        private int _lastSpriteMinX;
        private int _lastSpriteMinY;
        private int _lastSpriteMaxX;
        private int _lastSpriteMaxY;
        private int _lastBitplaneDmaFetches;
        private int _lastSpriteDmaFetches;
        private int _lastMissedSpriteDmaSlots;
        private long _lastFirstDisplayDmaCycle;
        private long _lastLastDisplayDmaCycle;
        private bool _renderingCopperFrame;
        private bool _captureSpriteFrameCommands;
        private bool _enforceDmaForFrame;
        private bool _useTimedPresentationReads;
        private int _currentCopperRow;
        private int _currentRenderRow;
        private long _renderFrameStartCycle;
        private readonly LiveLineState[] _liveLineStates = new LiveLineState[LowResOutputHeight];
        private readonly ushort[] _liveBitplaneWords = new ushort[LowResOutputHeight * LiveBitplaneWordsPerRow];
        private readonly ulong[] _liveBitplaneWordMasks = new ulong[LowResOutputHeight * LiveBitplanePlaneCount];
        private readonly ushort[] _liveSpriteWords = new ushort[LowResOutputHeight * LiveSpriteWordsPerRow];
        private readonly byte[] _liveSpriteWordMasks = new byte[LowResOutputHeight * LiveSpriteChannelCount];
        private readonly RowDmaPlan[] _rowDmaPlans = new RowDmaPlan[LowResOutputHeight];
        private readonly RowDmaBitplaneEntry[] _rowDmaBitplaneEntries = new RowDmaBitplaneEntry[LowResOutputHeight * MaxRowDmaBitplaneEntriesPerRow];
        private readonly ushort[] _rowDmaBitplaneBatchValues = new ushort[MaxRowDmaBitplaneEntriesPerRow];
        private readonly bool[] _rowDmaBitplaneBatchGranted = new bool[MaxRowDmaBitplaneEntriesPerRow];
        private readonly RowDmaSpriteEntry[] _rowDmaSpriteEntries = new RowDmaSpriteEntry[LowResOutputHeight * MaxRowDmaSpriteEntriesPerRow];
        private readonly byte[] _rowDmaExecutedMasks = new byte[LowResOutputHeight];
        private readonly bool[] _liveSpriteDmaExhausted = new bool[LiveSpriteChannelCount];
        private readonly LiveSpriteDmaState[] _liveSpriteDmaStates = new LiveSpriteDmaState[LiveSpriteChannelCount];
        private SpriteDmaReadLatch _spriteDmaReadLatch;
        private readonly ushort[] _livePaletteSnapshotColors = new ushort[MaxLivePaletteSnapshots * 32];
        private readonly uint[] _livePaletteSnapshotConvertedColors = new uint[MaxLivePaletteSnapshots * PaletteColorCount];
        private readonly List<SpriteFrameCommand> _previousLiveSpriteFrameCommands = new(MaxSpriteFrameCommands * 8);
        private readonly ushort[] _previousLiveSpriteWords = new ushort[LowResOutputHeight * LiveSpriteWordsPerRow];
        private readonly byte[] _previousLiveSpriteWordMasks = new byte[LowResOutputHeight * LiveSpriteChannelCount];
        private readonly byte[] _previousLiveSpriteDeniedMasks = new byte[LowResOutputHeight * LiveSpriteChannelCount];
        private readonly List<SpriteFrameCommand> _carryLiveSpriteFrameCommands = new(MaxSpriteFrameCommands * 8);
        private readonly ushort[] _carryLiveSpriteWords = new ushort[LowResOutputHeight * LiveSpriteWordsPerRow];
        private readonly byte[] _carryLiveSpriteWordMasks = new byte[LowResOutputHeight * LiveSpriteChannelCount];
        private readonly byte[] _carryLiveSpriteDeniedMasks = new byte[LowResOutputHeight * LiveSpriteChannelCount];
        private readonly LiveRasterlinePlanEvent[] _liveRasterlinePlanEvents = new LiveRasterlinePlanEvent[LowResOutputHeight * MaxLiveRasterlinePlanEvents];
        private readonly int[] _liveRasterlinePlanEventCounts = new int[LowResOutputHeight];
        private readonly bool[] _liveRasterlinePlanRowsTouched = new bool[LowResOutputHeight];
        private readonly bool[] _liveRasterlinePlanRowsValid = new bool[LowResOutputHeight];
        private readonly bool[] _liveRasterlinePlanRowsOverflowed = new bool[LowResOutputHeight];
        private readonly int[] _liveRasterlinePlanWakeSearchIndices = new int[LowResOutputHeight];
        private readonly bool[] _liveRasterlinePlanWakeSearchLineStateVisibility = new bool[LowResOutputHeight];
        private readonly long[] _liveRasterlinePlanWakeSearchCycles = new long[LowResOutputHeight];
        private readonly LiveRasterlinePlanEvent[] _predictedRasterlinePlanEvents = new LiveRasterlinePlanEvent[LowResOutputHeight * MaxLiveRasterlinePlanEvents];
        private readonly int[] _predictedRasterlinePlanEventCounts = new int[LowResOutputHeight];
        private readonly LiveRasterlinePredictionStatus[] _predictedRasterlinePlanStatuses = new LiveRasterlinePredictionStatus[LowResOutputHeight];
        private readonly LiveRasterlineDmaDescriptor[] _liveRasterlineDmaDescriptors = new LiveRasterlineDmaDescriptor[LowResOutputHeight];
        private DisplayFrameTimeline _displayTimeline = new DisplayFrameTimeline();
        private DisplayFrameTimeline _archivedDisplayTimeline = new DisplayFrameTimeline();
        private readonly ushort[] _archivedPaletteSnapshotColors = new ushort[MaxLivePaletteSnapshots * 32];
        private readonly uint[] _archivedPaletteSnapshotConvertedColors = new uint[MaxLivePaletteSnapshots * PaletteColorCount];
        private bool _renderingLiveCapture;
        private bool _advancingLiveDma;
        private long _previousLiveSpriteFrameStartCycle = long.MinValue;
        private bool _liveFrameValid;
        private int _liveGeneration = 1;
        private int _livePaletteSnapshotCount;
        private int _liveCurrentPaletteSnapshotIndex = -1;
        private int _lastAppliedLivePaletteSnapshotIndex = -1;
        private bool _livePaletteSnapshotDirty = true;
        private bool _liveNextDisplayEventValid;
        private long _liveNextDisplayEventCycle;
        private bool _liveNextWorkCycleValid;
        private long _liveNextWorkCycle;
        private bool _liveDisplayWakeCandidateCacheValid;
        private long _liveDisplayWakeCandidateCacheCurrentCycle;
        private long _liveDisplayWakeCandidateCacheTargetCycle;
        private long _liveDisplayWakeCandidateCacheCapturedThroughCycle;
        private bool _liveDisplayWakeCandidateCacheHasValue;
        private long _liveDisplayWakeCandidateCacheValue;
        private ulong _liveWakeVersion;
        private bool _liveCopperWaitCycleValid;
        private ushort _liveCopperWaitFirst;
        private ushort _liveCopperWaitSecond;
        private long _liveCopperWaitStartCycle;
        private long _liveCopperWaitCycle;
        private long _liveCycle;
        private long _liveFrameStartCycle;
        private long _liveCapturedThroughCycle;
        private int _liveNextLineStateRow;
        private int _liveNextFetchRow;
        private int _liveNextFetchWord;
        private int _liveNextFetchPlane;
        private int _liveNextFetchSlot;
        private int _livePreparedFetchRow;
        private int _livePreparedFetchWord;
        private int _livePreparedFetchPlane;
        private int _livePreparedFetchSlot;
        private int _liveNextSpriteRow;
        private int _liveNextSpriteIndex;
        private int _liveNextSpriteWord;
        private int _liveBitplaneDmaFetches;
        private int _liveSpriteDmaFetches;
        private int _liveMissedSpriteDmaSlots;
        private int _liveDisplayEventCount;
        private int _liveCopperStepCount;
        private int _livePendingWriteEventCount;
        private int _liveFetchBatchWordCount;
        private long _liveFirstDisplayDmaCycle;
        private long _liveLastDisplayDmaCycle;
        private long _copperQuiescentWindowCount;
        private long _copperQuiescentTotalCycles;
        private long _copperQuiescentMaxCycles;
        private long _copperQuiescentActiveStartCycle = -1;
        private long _copperQuiescentActiveEndCycle = -1;
        private CopperPresentationState _liveCopper;
        private bool _liveFrameInitialStateValid;
        private bool _liveFrameWriteOverflowed;
        private bool _liveFrameHasLateDisplayWindowWrites;
        private bool _liveTimelineUnsafeForFrame;
        private bool _liveTimelineUnsafeRequiresCapturedRows;
        private ushort _liveTimelineUnsafeOffset;
        private bool _liveTimelineUnsafeIsCopper;
        private int _lastTimelineSegmentCount;
        private int _lastTimelineFallbackCount;
        private int _lastTimelineSpriteCommandCount;
        private int _lastActiveTimelineFrameCount;
        private int _lastArchivedTimelineFrameCount;
        private int _lastPlanarChunkCacheHits;
        private int _lastPlanarChunkCacheMisses;
        private int _lastTimelineCoalescedSegmentCount;
        private int _lastTimelineFastPathRowCount;
        private int _lastTimelineFastPathMissCount;
        private int _lastSpriteRecoveryAttemptCount;
        private int _lastSpriteDeniedFetchCount;
        private int _lastArchiveRejectFrameIncomplete;
        private int _lastArchiveRejectTimelineInvalid;
        private int _lastArchiveRejectUnsafeWrite;
        private int _lastArchiveRejectSegmentCapacity;
        private int _lastArchiveRejectMissingLine;
        private int _lastArchiveRejectUnsafeLine;
        private int _lastArchiveRejectMissingBitplaneFetch;
        private int _lastArchiveRejectMissingSpriteFetch;
        private ushort _lastArchiveRejectUnsafeOffset;
        private bool _lastArchiveRejectUnsafeIsCopper;
        private int _lastArchiveRejectMissingSpriteIndex;
        private int _lastArchiveRejectMissingSpriteRow;
        private int _lastArchiveRejectMissingSpriteWord;
        private int _lastArchiveRejectMissingSpriteStatusA;
        private int _lastArchiveRejectMissingSpriteStatusB;
        private int _lastArchiveRejectMissingSpriteCommandRow;
        private int _lastArchiveRejectMissingSpriteYStart;
        private int _lastArchiveRejectMissingSpriteYStop;
        private int _lastArchiveRejectMissingSpriteUsableChannels;
        private int _lastArchiveRejectMissingSpriteDdfStart;
        private ushort _lastArchiveRejectMissingSpriteDmacon;
        private ushort _lastArchiveRejectMissingSpriteBplcon0;
        private int _lastArchiveRejectMissingSpritePreviousStatusA;
        private int _lastArchiveRejectMissingSpritePreviousStatusB;
        private bool _archivedTimelineValid;
        private bool _renderingArchivedTimeline;
        private long _archivedTimelineFrameStartCycle = long.MinValue;
        private long _archivedTimelineFrameStopCycle = long.MinValue;
        private int _archivedPaletteSnapshotCount;
        private int _liveRasterlinePlanRow = -1;
        private long _liveRasterlinePlanLineStartCycle;
        private long _liveRasterlinePlanLineStopCycle;
        private long _liveRasterlinePlanLastCycle = long.MinValue;
        private int _liveRasterlinePlanLineEventCount;
        private bool _liveRasterlinePlanLineValid = true;
        private bool _liveRasterlinePlanLineOverflowed;
        private int _liveRasterlinePlanCompletedLines;
        private int _liveRasterlinePlanCompletedValidLines;
        private int _liveRasterlinePlanCompletedInvalidLines;
        private int _liveRasterlinePlanCompletedOverflowLines;
        private int _liveRasterlinePlanObservedEventCount;
        private int _liveRasterlinePlanPendingWriteOrCopperEvents;
        private int _liveRasterlinePlanLineStateEvents;
        private int _liveRasterlinePlanBitplaneFetchEvents;
        private int _liveRasterlinePlanSpriteFetchEvents;
        private int _liveRasterlinePlanCopperBarrierEvents;
        private int _liveRasterlinePlanMaxEventsPerLine;
        private int _predictedRasterlinePlanLines;
        private int _predictedRasterlinePlanMatchedLines;
        private int _predictedRasterlinePlanMismatchedLines;
        private int _predictedRasterlinePlanUnsupportedLines;
        private int _predictedRasterlinePlanEventTotal;
        private int _predictedRasterlinePlanUnsupportedCopperLines;
        private int _predictedRasterlinePlanUnsupportedPendingWriteLines;
        private int _predictedRasterlinePlanUnsupportedSpriteLines;
        private int _predictedRasterlinePlanUnsupportedInvalidStateLines;
        private int _predictedRasterlinePlanUnsupportedOverflowLines;
        private int _liveRasterlineDescriptorBuilds;
        private int _liveRasterlineDescriptorReplayAttempts;
        private int _liveRasterlineDescriptorReplayedRows;
        private int _liveRasterlineDescriptorFallbackRows;
        private int _liveRasterlineDescriptorBitplaneRows;
        private int _liveRasterlineDescriptorSpriteRows;
        private int _liveRasterlineDescriptorMismatches;
        private int _lastRowDmaPlansBuilt;
        private int _lastRowDmaPlannedRowsExecuted;
        private int _lastRowDmaBitplaneEntriesExecuted;
        private int _lastRowDmaSpriteEntriesExecuted;
        private int _lastRowDmaScalarFallbackRows;
        private int _lastRowDmaPlanInvalidationRows;
        private int _lastRowDmaPlanMismatchRows;

        public OcsDisplay(AmigaBus bus, bool liveDmaEnabled = true)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _liveDmaEnabled = liveDmaEnabled;
            for (var i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = new SpriteState();
                _spriteCommandScratch[i] = new List<SpriteFrameCommand>(MaxSpriteFrameCommands);
                _liveSpriteDmaStates[i] = new LiveSpriteDmaState();
            }

            for (var i = 0; i < _liveLineStates.Length; i++)
            {
                _liveLineStates[i] = new LiveLineState();
            }

            Reset();
        }

        public int Width => AmigaConstants.PalHighResWidth;

        public int Height => AmigaConstants.PalHighResHeight;

        public bool InterlaceEnabled => (_bplcon0 & 0x0004) != 0;

        internal int LiveDisplayEventCount => _liveDisplayEventCount;

        internal int LiveCopperStepCount => _liveCopperStepCount;

        internal int LivePendingWriteEventCount => _livePendingWriteEventCount;

        internal int LiveFetchBatchWordCount => _liveFetchBatchWordCount;

        internal int BitplaneDataSpanCount => _bitplaneDataSpans.Count;

        internal bool LiveDmaEnabled => _liveDmaEnabled;

        internal ulong LiveWakeVersion => _liveWakeVersion;

        internal bool HasLiveDmaCapturedThrough(long cycle)
        {
            return _liveFrameValid &&
                cycle >= _liveFrameStartCycle &&
                cycle <= _liveCapturedThroughCycle;
        }

        internal void CaptureLiveDisplayDmaBeforeHrmGrant(long requestedCycle)
        {
            if (!_liveDmaEnabled ||
                !_liveFrameValid)
            {
                return;
            }

            if (_advancingLiveDma)
            {
                CaptureLiveDisplayDmaBeforeHrmGrant(requestedCycle, includeCopper: false);
                return;
            }

            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                CaptureLiveDisplayDmaBeforeHrmGrant(requestedCycle, includeCopper: true);
            }
            finally
            {
                EndLiveDmaCapture(savedAdvancingLiveDma);
            }
        }

        internal void PrepareLiveDisplaySlotsBeforeHrmGrant(long requestedCycle)
        {
            if (!_liveDmaEnabled ||
                !_liveFrameValid)
            {
                return;
            }

            if (_advancingLiveDma)
            {
                PrepareLiveDisplaySlotsBeforeHrmGrant(requestedCycle, includeCopper: false);
                return;
            }

            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                PrepareLiveDisplaySlotsBeforeHrmGrant(requestedCycle, includeCopper: true);
            }
            finally
            {
                EndLiveDmaCapture(savedAdvancingLiveDma);
            }
        }

        internal bool HasLiveDisplaySlotPreparationWorkBeforeHrmGrant(long requestedCycle)
        {
            if (!_liveDmaEnabled ||
                !_liveFrameValid)
            {
                return false;
            }

            var before = _bus.FindHrmDmaCandidate(requestedCycle);
            var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
            return before > _liveCapturedThroughCycle &&
                before < frameStopCycle &&
                HasLiveSlotPreparationWorkThrough(before, includeCopper: !_advancingLiveDma);
        }

        private void CaptureLiveDisplayDmaBeforeHrmGrant(long requestedCycle, bool includeCopper)
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var before = _bus.FindHrmDmaCandidate(requestedCycle);
                var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
                if (before <= _liveCapturedThroughCycle ||
                    before >= frameStopCycle ||
                    !HasLivePreGrantWorkThrough(before, includeCopper))
                {
                    return;
                }

                if (before < frameStopCycle)
                {
                    AdvanceLiveDmaWithinFrame(before, includeCopper);
                }

                CaptureLiveBitplaneDmaBeforeHrmGrant(requestedCycle);
                CaptureLiveSpriteDmaBeforeHrmGrant(requestedCycle);
                var after = _bus.IsHrmChipSlotReserved(before)
                    ? _bus.FindHrmDmaCandidate(before + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (after == before)
                {
                    return;
                }
            }
        }

        private void PrepareLiveDisplaySlotsBeforeHrmGrant(long requestedCycle, bool includeCopper)
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var before = _bus.FindHrmDmaCandidate(requestedCycle);
                var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
                if (before <= _liveCapturedThroughCycle ||
                    before >= frameStopCycle ||
                    !HasLiveSlotPreparationWorkThrough(before, includeCopper))
                {
                    return;
                }

                if (before < frameStopCycle)
                {
                    AdvanceLiveRegisterEventsWithinFrame(before, includeCopper);
                    PrepareKnownLiveBitplaneSlotsThrough(before);
                }

                var after = _bus.IsHrmChipSlotReserved(before)
                    ? _bus.FindHrmDmaCandidate(before + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (after == before)
                {
                    return;
                }
            }
        }

        private bool HasLivePreGrantWorkThrough(long candidateCycle, bool includeCopper)
        {
            return GetNextLiveWorkCycle(includeCopper) <= candidateCycle;
        }

        private bool HasLiveSlotPreparationWorkThrough(long candidateCycle, bool includeCopper)
        {
            return Math.Min(
                GetNextLiveRegisterEventCycle(includeCopper),
                GetNextPreparedLiveBitplaneFetchCycle()) <= candidateCycle;
        }

        private long GetNextLiveRegisterEventCycle(bool includeCopper)
        {
            var next = Math.Min(GetNextLiveDisplayEventCycle(includeCopper), GetNextLiveLineStateCycle());
            if (includeCopper)
            {
                return next;
            }

            return next < GetNextLiveCopperBarrierCycle() ? next : long.MaxValue;
        }

        private long GetNextLiveWorkCycle()
        {
            if (_liveNextWorkCycleValid)
            {
                return _liveNextWorkCycle;
            }

            _liveNextWorkCycle = Math.Min(
                Math.Min(GetNextLiveDisplayEventCycle(), GetNextLiveLineStateCycle()),
                Math.Min(GetNextLiveBitplaneFetchCycle(), GetNextLiveSpriteFetchCycle()));
            _liveNextWorkCycleValid = true;
            return _liveNextWorkCycle;
        }

        private long GetNextLiveWorkCycle(bool includeCopper)
        {
            if (includeCopper)
            {
                return GetNextLiveWorkCycle();
            }

            var nextWork = Math.Min(
                Math.Min(GetNextLivePendingWriteCycle(), GetNextLiveLineStateCycle()),
                Math.Min(GetNextLiveBitplaneFetchCycle(), GetNextLiveSpriteFetchCycle()));
            return nextWork < GetNextLiveCopperBarrierCycle()
                ? nextWork
                : long.MaxValue;
        }

        private void InvalidateLiveWorkCycle()
        {
            _liveNextWorkCycleValid = false;
            _liveNextWorkCycle = long.MaxValue;
            _liveDisplayWakeCandidateCacheValid = false;
        }

        private void CaptureLiveBitplaneDmaBeforeHrmGrant(long requestedCycle)
        {
            var requestedSlot = _bus.NextChipSlotCycle(requestedCycle);
            var candidate = _bus.FindHrmDmaCandidate(requestedCycle);
            if (candidate <= _liveCapturedThroughCycle)
            {
                return;
            }

            var nextFetchCycle = GetNextKnownLiveBitplaneFetchCycle();
            if (nextFetchCycle == long.MaxValue)
            {
                return;
            }

            if (requestedSlot < nextFetchCycle && !_bus.IsHrmChipSlotReserved(requestedSlot))
            {
                return;
            }

            var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
            while (candidate < frameStopCycle)
            {
                if (candidate < nextFetchCycle)
                {
                    return;
                }

                if (!CaptureKnownLiveBitplaneFetchesThrough(candidate))
                {
                    return;
                }

                nextFetchCycle = GetNextKnownLiveBitplaneFetchCycle();
                var adjustedCandidate = _bus.IsHrmChipSlotReserved(candidate)
                    ? _bus.FindHrmDmaCandidate(candidate + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (adjustedCandidate == candidate)
                {
                    return;
                }

                candidate = adjustedCandidate;
            }
        }

        private void CaptureLiveSpriteDmaBeforeHrmGrant(long requestedCycle)
        {
            if (!IsSpriteDmaEnabled())
            {
                return;
            }

            var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
            while (true)
            {
                var candidate = _bus.FindHrmDmaCandidate(requestedCycle);
                if (candidate >= frameStopCycle ||
                    !TryGetLiveSpriteDmaSlot(candidate, out var row, out var spriteIndex, out var word))
                {
                    return;
                }

                if (!TryCaptureKnownLiveSpriteDmaSlot(row, spriteIndex, word, candidate))
                {
                    return;
                }

                var adjustedCandidate = _bus.IsHrmChipSlotReserved(candidate)
                    ? _bus.FindHrmDmaCandidate(candidate + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (adjustedCandidate == candidate)
                {
                    return;
                }
            }
        }

        [HotPathAllocationAllowed("Display snapshots allocate arrays for tests, diagnostics, and UI inspection outside frame rendering.")]
        public OcsDisplaySnapshot CaptureSnapshot()
        {
            var bitplanePointers = new uint[_bitplanePointers.Length];
            Array.Copy(_bitplanePointers, bitplanePointers, bitplanePointers.Length);
            var bitplaneBaseRows = new int[_bitplaneBaseRows.Length];
            Array.Copy(_bitplaneBaseRows, bitplaneBaseRows, bitplaneBaseRows.Length);
            var colors = new ushort[_colors.Length];
            Array.Copy(_colors, colors, colors.Length);
            var colorCounts = new int[_lastBitplaneColorCounts.Length];
            Array.Copy(_lastBitplaneColorCounts, colorCounts, colorCounts.Length);
            var bitplaneDmaFetches = Math.Max(_lastBitplaneDmaFetches, _liveBitplaneDmaFetches);
            var spriteDmaFetches = Math.Max(_lastSpriteDmaFetches, _liveSpriteDmaFetches);
            var missedSpriteDmaSlots = Math.Max(_lastMissedSpriteDmaSlots, _liveMissedSpriteDmaSlots);
            var firstDisplayDmaCycle = _lastFirstDisplayDmaCycle >= 0
                ? _lastFirstDisplayDmaCycle
                : _liveFirstDisplayDmaCycle;
            var lastDisplayDmaCycle = Math.Max(_lastLastDisplayDmaCycle, _liveLastDisplayDmaCycle);
            return new OcsDisplaySnapshot(
                _bplcon0,
                _bplcon1,
                _bplcon2,
                _diwStart,
                _diwStop,
                _ddfStart,
                _ddfStop,
                _bpl1mod,
                _bpl2mod,
                _lastBitplaneNonZeroPixels,
                _lastBitplaneRows,
                _lastBitplaneWords,
                _lastBitplaneMinX,
                _lastBitplaneMinY,
                _lastBitplaneMaxX,
                _lastBitplaneMaxY,
                _lastNormalPlayfieldNonZeroPixels,
                _lastNormalPlayfieldMinX,
                _lastNormalPlayfieldMinY,
                _lastNormalPlayfieldMaxX,
                _lastNormalPlayfieldMaxY,
                _lastPlayfield1NonZeroPixels,
                _lastPlayfield1MinX,
                _lastPlayfield1MinY,
                _lastPlayfield1MaxX,
                _lastPlayfield1MaxY,
                _lastPlayfield2NonZeroPixels,
                _lastPlayfield2MinX,
                _lastPlayfield2MinY,
                _lastPlayfield2MaxX,
                _lastPlayfield2MaxY,
                _lastSpriteNonZeroPixels,
                _lastSpriteMinX,
                _lastSpriteMinY,
                _lastSpriteMaxX,
                _lastSpriteMaxY,
                bitplaneDmaFetches,
                spriteDmaFetches,
                missedSpriteDmaSlots,
                firstDisplayDmaCycle,
                lastDisplayDmaCycle,
                bitplanePointers,
                bitplaneBaseRows,
                colors,
                colorCounts,
                _lastTimelineSegmentCount,
                _lastTimelineFallbackCount,
                _lastTimelineSpriteCommandCount,
                _lastActiveTimelineFrameCount,
                _lastArchivedTimelineFrameCount,
                _lastPlanarChunkCacheHits,
                _lastPlanarChunkCacheMisses,
                _lastTimelineCoalescedSegmentCount,
                _lastTimelineFastPathRowCount,
                _lastTimelineFastPathMissCount,
                _lastSpriteRecoveryAttemptCount,
                _lastSpriteDeniedFetchCount,
                GetLiveRasterlinePlanLineCount(),
                GetLiveRasterlinePlanValidLineCount(),
                GetLiveRasterlinePlanInvalidLineCount(),
                GetLiveRasterlinePlanOverflowLineCount(),
                _liveRasterlinePlanObservedEventCount,
                _liveRasterlinePlanPendingWriteOrCopperEvents,
                _liveRasterlinePlanLineStateEvents,
                _liveRasterlinePlanBitplaneFetchEvents,
                _liveRasterlinePlanSpriteFetchEvents,
                _liveRasterlinePlanCopperBarrierEvents,
                _liveRasterlinePlanMaxEventsPerLine,
                _predictedRasterlinePlanLines,
                _predictedRasterlinePlanMatchedLines,
                _predictedRasterlinePlanMismatchedLines,
                _predictedRasterlinePlanUnsupportedLines,
                _predictedRasterlinePlanEventTotal,
                _predictedRasterlinePlanUnsupportedCopperLines,
                _predictedRasterlinePlanUnsupportedPendingWriteLines,
                _predictedRasterlinePlanUnsupportedSpriteLines,
                _predictedRasterlinePlanUnsupportedInvalidStateLines,
                _predictedRasterlinePlanUnsupportedOverflowLines,
                _liveRasterlineDescriptorBuilds,
                _liveRasterlineDescriptorReplayAttempts,
                _liveRasterlineDescriptorReplayedRows,
                _liveRasterlineDescriptorFallbackRows,
                _liveRasterlineDescriptorBitplaneRows,
                _liveRasterlineDescriptorSpriteRows,
                _liveRasterlineDescriptorMismatches,
                _lastRowDmaPlansBuilt,
                _lastRowDmaPlannedRowsExecuted,
                _lastRowDmaBitplaneEntriesExecuted,
                _lastRowDmaSpriteEntriesExecuted,
                _lastRowDmaScalarFallbackRows,
                _lastRowDmaPlanInvalidationRows,
                _lastRowDmaPlanMismatchRows,
                _copperQuiescentWindowCount,
                _copperQuiescentTotalCycles,
                _copperQuiescentMaxCycles,
                _copperQuiescentActiveStartCycle,
                _copperQuiescentActiveEndCycle,
                _lastArchiveRejectFrameIncomplete,
                _lastArchiveRejectTimelineInvalid,
                _lastArchiveRejectUnsafeWrite,
                _lastArchiveRejectSegmentCapacity,
                _lastArchiveRejectMissingLine,
                _lastArchiveRejectUnsafeLine,
                _lastArchiveRejectMissingBitplaneFetch,
                _lastArchiveRejectMissingSpriteFetch,
                _lastArchiveRejectUnsafeOffset,
                _lastArchiveRejectUnsafeIsCopper,
                _lastArchiveRejectMissingSpriteIndex,
                _lastArchiveRejectMissingSpriteRow,
                _lastArchiveRejectMissingSpriteWord,
                _lastArchiveRejectMissingSpriteStatusA,
                _lastArchiveRejectMissingSpriteStatusB,
                _lastArchiveRejectMissingSpriteCommandRow,
                _lastArchiveRejectMissingSpriteYStart,
                _lastArchiveRejectMissingSpriteYStop,
                _lastArchiveRejectMissingSpriteUsableChannels,
                _lastArchiveRejectMissingSpriteDdfStart,
                _lastArchiveRejectMissingSpriteDmacon,
                _lastArchiveRejectMissingSpriteBplcon0,
                _lastArchiveRejectMissingSpritePreviousStatusA,
                _lastArchiveRejectMissingSpritePreviousStatusB);
        }

        public void Reset()
        {
            _pendingWrites.Clear();
            _pendingIndex = 0;
            Array.Clear(_bitplanePointers);
            Array.Clear(_bitplaneBaseRows);
            Array.Clear(_bitplaneDataRegisters);
            Array.Clear(_bitplaneDataRegisterWritten);
            _bitplaneDataSpans.Clear();
            _copperListPointer = 0;
            _copperListPointer2 = 0;
            _diwStart = DefaultDiwStart;
            _diwStop = DefaultDiwStop;
            _ddfStart = DefaultDdfStart;
            _ddfStop = DefaultDdfStop;
            _bplcon0 = 0;
            _bplcon1 = 0;
            _bplcon2 = 0;
            _copcon = 0;
            _dmacon = 0;
            _bpl1mod = 0;
            _bpl2mod = 0;
            _renderWidth = Width;
            _renderHeight = Height;
            _renderInterlaceField = 0;
            _trackDisplayWindowState = false;
            _displayWindowVerticallyOpen = false;
            _displayWindowStateLine = 0;
            _liveDisplayWindowVerticallyOpen = false;
            _liveDisplayWindowStateLine = 0;
            _currentRenderRow = -1;
            ResetFrameCounters();
            Array.Clear(_colors);
            _colors[0] = 0x000;
            _colors[1] = 0xFFF;
            UpdateConvertedPalette();
            foreach (var sprite in _sprites)
            {
                sprite.Reset();
            }

            ResetLiveDma();
        }

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
                while (targetCycle >= _liveFrameStartCycle + PalFrameCycles)
                {
                    AdvanceLiveDmaWithinFrame((_liveFrameStartCycle + PalFrameCycles) - 1);
                    StartLiveFrame(_liveFrameStartCycle + PalFrameCycles);
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

        public void ScheduleWrite(long cycle, ushort offset, ushort value)
        {
            if (!_liveDmaEnabled)
            {
                return;
            }

            offset = (ushort)(offset & 0x01FE);
            if (!IsDisplayRegisterWrite(offset))
            {
                return;
            }

            if (CustomRegisterScheduleClassifier.IsDisplayBusScheduleAffectingWrite(offset))
            {
                _bus.NotifyCustomRegisterScheduleChanged(offset, cycle);
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
            if (!_advancingLiveDma && _liveFrameValid && cycle <= _liveCapturedThroughCycle)
            {
                InvalidateLiveCaptureFrom(cycle);
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
            while (targetCycle >= _liveFrameStartCycle + PalFrameCycles)
            {
                StartLiveFrame(_liveFrameStartCycle + PalFrameCycles);
            }

            _liveCycle = Math.Max(_liveCycle, targetCycle);
            _liveCapturedThroughCycle = Math.Max(_liveCapturedThroughCycle, targetCycle);
        }

        private static bool IsDisplayRegisterWrite(ushort offset)
        {
            return offset is 0x02E or
                0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A or
                0x08E or 0x090 or 0x092 or 0x094 or 0x096 or
                0x100 or 0x102 or 0x104 or 0x108 or 0x10A ||
                (offset >= 0x0E0 && offset <= 0x0F6) ||
                (offset >= 0x110 && offset <= 0x11A) ||
                (offset >= 0x120 && offset < 0x180) ||
                (offset >= 0x180 && offset < 0x1C0);
        }

        private void InvalidateLiveCaptureFrom(long cycle)
        {
            if (!_liveFrameValid || cycle < _liveFrameStartCycle || cycle > _liveFrameStartCycle + PalFrameCycles)
            {
                return;
            }

            var row = Math.Clamp(GetOutputRowForCycle(_liveFrameStartCycle, cycle), 0, LowResOutputHeight - 1);
            var invalidateRow = ShouldPreserveCompletedLiveRowForInvalidation(row)
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
            _bus.ClearLiveDisplayDmaSlotsFrom(cycle);
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

        private bool ShouldPreserveCompletedLiveRowForInvalidation(int row)
        {
            if (!IsLiveLineValid(row))
            {
                return false;
            }

            var state = _liveLineStates[row];
            if (state.PlaneCount <= 0 ||
                state.FetchWords <= 0 ||
                !state.DisplayWindowVerticallyOpen ||
                !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return false;
            }

            var expectedMask = state.FetchWords >= 64
                ? ulong.MaxValue
                : (1UL << state.FetchWords) - 1UL;
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
                _liveCapturedThroughCycle < Math.Min(nextFrameStartCycle, _liveFrameStartCycle + PalFrameCycles) - 1)
            {
                RecordArchiveReject(TimelineRejectReason.FrameIncomplete);
                return;
            }

            var frameStopCycle = Math.Min(nextFrameStartCycle, _liveFrameStartCycle + PalFrameCycles);
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

        private void ArchiveLiveSpriteFrameBeforeStarting(long nextFrameStartCycle)
        {
            var frameStopCycle = _liveFrameValid
                ? Math.Min(nextFrameStartCycle, _liveFrameStartCycle + PalFrameCycles)
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
                _previousLiveSpriteFrameStartCycle != _liveFrameStartCycle - PalFrameCycles ||
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
                cycle >= _liveFrameStartCycle + PalFrameCycles)
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
                0x100 or 0x102 or 0x104 or 0x108 or 0x10A ||
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
            _bus.InvalidateLiveDisplayHrmGrantCache();
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
            var copperCycle = GetNextLiveCopperCycle(_liveFrameStartCycle + PalFrameCycles);
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
                _liveRasterlinePlanLineStopCycle = _liveRasterlinePlanLineStartCycle + PalLineCycles - 1;
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
                cycle >= _liveFrameStartCycle + PalFrameCycles)
            {
                return false;
            }

            row = (int)((cycle - _liveFrameStartCycle) / PalLineCycles) - StandardVStart;
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
            var lineStop = lineStart + PalLineCycles - 1;
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
                state.Bplcon0,
                state.Bplcon1,
                state.Bplcon2,
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
                if (TryGetBitplanePlaneForFetchSlot(slot, planeCount, state.FetchSlotStride, out plane))
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
            var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
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

            var copperCycle = GetNextLiveCopperCycle(Math.Min(targetCycle + 1, _liveFrameStartCycle + PalFrameCycles));
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

            var frameEndCycle = _liveFrameStartCycle + PalFrameCycles;
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

            var frameEndCycle = _liveFrameStartCycle + PalFrameCycles;
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
                RecordTimelineFrameWritesAtCycle(cycle);
            }
            finally
            {
                _currentRenderRow = previousRow;
                _currentCopperRow = previousCopperRow;
            }
        }

        private void RecordTimelineFrameWritesAtCycle(long cycle)
        {
            for (var i = _liveFrameWrites.Count - 1; i >= 0; i--)
            {
                var write = _liveFrameWrites[i];
                if (write.Cycle < cycle)
                {
                    break;
                }

                if (write.Cycle != cycle || write.IsCopper)
                {
                    continue;
                }

                RecordTimelineDisplayWrite(write.Cycle, write.Offset, isCopper: false);
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
                var blitterReadyCycle = GetCopperBlitterReadyCycle(_liveCopper.WaitSecond, _liveCopper.Cycle);
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

            var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
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
                var blitterReadyCycle = GetCopperBlitterReadyCycle(_liveCopper.WaitSecond, _liveCopper.Cycle);
                if (blitterReadyCycle > _liveCopper.Cycle)
                {
                    _bus.Blitter.AdvanceTo(Math.Min(blitterReadyCycle, targetCycle));
                    _liveCopper.Cycle = Math.Min(blitterReadyCycle, targetCycle);
                    if (_liveCopper.Cycle < blitterReadyCycle)
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
                        _liveCopper.Cycle = targetCycle + 1;
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
                    _liveCopper.Cycle = targetCycle + 1;
                    return;
                }

                var resumeCycle = waitCycle + CopperHpToCpuCycles(CopperWaitWakeHpUnits);
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
            var first = _bus.ReadLiveCopperDmaWord(pc, fetchCycle, out var firstAccess);
            var secondRequestCycle = GetCopperSecondWordRequestCycle(firstAccess);
            var second = _bus.ReadLiveCopperDmaWord(AddDmaPointerOffset(pc, 2), secondRequestCycle, out var secondAccess);
            return new CopperInstructionLatch(first, firstAccess, second, secondAccess);
        }

        private static long GetCopperSecondWordRequestCycle(AmigaBusAccessResult firstAccess)
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

                ApplyCopperMove(register, value, dataCycle, applyHardwareSideEffects: true);
                if (affectsDisplay)
                {
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
            }

            _liveCopper.Cycle = instructionStopCycle;
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
                CustomRegisterScheduleClassifier.IsScheduleAffectingCustomWrite(register));
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

            var state = _liveLineStates[_liveNextFetchRow];
            var fetchHorizontal = state.DataFetchStart + (_liveNextFetchWord * state.FetchSlotStride) + _liveNextFetchSlot;
            return AgnusChipSlotScheduler.AlignToSlot(state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
        }

        private bool NormalizeLiveBitplaneFetchCursor()
        {
            while (_liveNextFetchRow < LowResOutputHeight)
            {
                var state = _liveLineStates[_liveNextFetchRow];
                if (!IsLiveLineValid(_liveNextFetchRow))
                {
                    return false;
                }

                var planeCount = Math.Max(0, state.PlaneCount);
                if (planeCount <= 0 ||
                    state.FetchWords <= 0 ||
                    !state.DisplayWindowVerticallyOpen ||
                    !IsBitplaneDmaEnabled(state.Dmacon))
                {
                    AdvanceLiveFetchToNextRow(advanceBitplanePointers: false);
                    continue;
                }

                while (_liveNextFetchWord < state.FetchWords)
                {
                    while (_liveNextFetchSlot < state.FetchSlotStride)
                    {
                        if (TryGetBitplanePlaneForFetchSlot(_liveNextFetchSlot, planeCount, state.FetchSlotStride, out var plane))
                        {
                            _liveNextFetchPlane = plane;
                            return true;
                        }

                        _liveNextFetchSlot++;
                    }

                    _liveNextFetchSlot = 0;
                    _liveNextFetchWord++;
                }

                AdvanceLiveFetchToNextRow(advanceBitplanePointers: true);
            }

            return false;
        }

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
                _bus.ReadRowBitplaneDmaFetchesForPresentation(
                    _rowDmaBitplaneEntries.AsSpan(batchStart, batchCount),
                    state.LineStartCycle,
                    _rowDmaBitplaneBatchValues.AsSpan(0, batchCount),
                    _rowDmaBitplaneBatchGranted.AsSpan(0, batchCount),
                    out var grantedCount,
                    out var firstGrantedCycle,
                    out var lastGrantedCycle);
                ConsumeRowDmaBitplaneBatch(row, batchStart, batchCount, grantedCount, firstGrantedCycle, lastGrantedCycle);
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
            else if (bgra.Length >= Width * LowResOutputHeight)
            {
                _renderWidth = Width;
                _renderHeight = LowResOutputHeight;
            }
            else if (bgra.Length >= AmigaConstants.PalLowResWidth * LowResOutputHeight)
            {
                _renderWidth = AmigaConstants.PalLowResWidth;
                _renderHeight = LowResOutputHeight;
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
                    _bus.Agnus.AdvanceTo(frameCaptureStopCycle);
                }

                if (TryRenderLiveCapturedFrame(bgra, frameStartCycle, frameStopCycle))
                {
                    return;
                }

                if (TryRenderArchivedTimelineFrame(bgra, frameStartCycle, frameStopCycle))
                {
                    return;
                }
            }

            ApplyPendingWrites(useTimedWrites ? frameStartCycle : long.MaxValue);
            var savedPresentationState = useTimedWrites ? SaveDisplayState() : null;
            _renderInterlaceField = useTimedWrites && InterlaceEnabled
                ? (int)((frameStartCycle / PalFrameCycles) & 1)
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
                    RenderCopperFrame(bgra, frameStartCycle, frameStartCycle + PalFrameCycles, useTimedWrites);
                }
                else
                {
                    RenderRows(bgra, 0, LowResOutputHeight, frameStartCycle, useTimedWrites);
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

        private static long GetPresentationFrameStopCycle(long frameStartCycle, long frameEndCycle)
        {
            var naturalFrameStop = frameStartCycle + PalFrameCycles;
            if (frameEndCycle <= frameStartCycle)
            {
                return naturalFrameStop;
            }

            return Math.Min(frameEndCycle, naturalFrameStop);
        }

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

        private static int GetTimelineRowStop(long frameStartCycle, long frameStopCycle)
        {
            var rowStop = LowResOutputHeight;
            if (frameStopCycle < frameStartCycle + PalFrameCycles)
            {
                rowStop = Math.Clamp(GetOutputRowForCycle(frameStartCycle, frameStopCycle) + 1, 0, LowResOutputHeight);
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
            var checkFrameStop = frameStopCycle < frameStartCycle + PalFrameCycles;
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

                for (var segmentIndex = 0; segmentIndex < line.SegmentCount; segmentIndex++)
                {
                    var state = timeline.GetState(line.Segments[segmentIndex].StateIndex);
                    if (!IsTimelineSegmentFetchComplete(row, state, timeline))
                    {
                        return TimelineRejectReason.MissingBitplaneFetch;
                    }
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
            var checkFrameStop = frameStopCycle < frameStartCycle + PalFrameCycles;
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
                    var yStop = Math.Min(Math.Min(sprite.YStop, rowStop), LowResOutputHeight);
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
            var checkFrameStop = frameStopCycle < frameStartCycle + PalFrameCycles;
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
                    var yStop = Math.Min(Math.Min(sprite.YStop, rowStop), LowResOutputHeight);
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

            var horizontal = AgnusHrmOcsSlotTable.FirstSpriteHorizontal + (spriteIndex * 4) + (word * 2);
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
            _ddfStart = state.DdfStart;
            _ddfStop = state.DdfStop;
            _bplcon0 = state.Bplcon0;
            _bplcon1 = state.Bplcon1;
            _bplcon2 = state.Bplcon2;
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
            var lineXStart = AmigaConstants.PalLowResWidth;
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
                left.DiwStart == right.DiwStart &&
                left.DiwStop == right.DiwStop &&
                left.DisplayWindowVerticallyOpen == right.DisplayWindowVerticallyOpen &&
                left.DdfStart == right.DdfStart &&
                left.DdfStop == right.DdfStop &&
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
            if ((state.Bplcon0 & 0x8804) != 0 ||
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
            if (window.Width <= 0 || window.Height <= 0)
            {
                return true;
            }

            var rowStart = Math.Max(0, window.Y);
            var rowStop = Math.Min(LowResOutputHeight, window.Y + window.Height);
            if (row < rowStart || row >= rowStop)
            {
                return true;
            }

            var originX = GetDataFetchStartX(window);
            var clipLeft = Math.Max(Math.Max(0, window.X), xStart);
            var clipRight = Math.Min(Math.Min(AmigaConstants.PalLowResWidth, window.X + window.Width), xStop);
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

            var highResolution = IsHighResolutionEnabled(state.Bplcon0);
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
            if (window.Width <= 0 || window.Height <= 0 || fetchWords <= 0)
            {
                return true;
            }

            var fetchPixels = fetchWords * PlanarChunkPixels;
            var drawPixels = highResolution ? fetchPixels / 2 : fetchPixels;
            var originX = GetDataFetchStartX(window);
            var clipLeft = Math.Max(Math.Max(0, window.X), segment.XStart);
            var clipRight = Math.Min(Math.Min(AmigaConstants.PalLowResWidth, window.X + window.Width), segment.XStop);
            var rowStart = Math.Max(0, window.Y);
            var rowStop = Math.Min(LowResOutputHeight, window.Y + window.Height);
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

        private static bool TryGetTimelineBitplaneWord(
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
                ? (int)((frameStartCycle / PalFrameCycles) & 1)
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
                ? (int)((frameStartCycle / PalFrameCycles) & 1)
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
                        RestoreDisplayState(_liveFrameInitialState);
                        ResetDisplayWindowStateTracking();
                        _renderingCopperFrame = true;
                        _currentCopperRow = GetOutputRowForCycle(frameStartCycle, frameStartCycle);
                        var renderCursorCycle = frameStartCycle;
                        var renderCursorPixelDelay = 0;
                        for (var i = 0; i < _liveFrameWrites.Count; i++)
                        {
                            var write = _liveFrameWrites[i];
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
            // Copper palette writes reach Denise one low-res pixel after the bus event;
            // the Copper cycle itself still remains at the data-word grant.
            return offset >= 0x180 && offset < 0x1C0
                ? 1
                : 0;
        }

        private void RenderLiveCapturedRows(Span<uint> bgra)
        {
            for (var row = 0; row < LowResOutputHeight; row++)
            {
                var state = _liveLineStates[row];
                if (!IsLiveLineValid(row))
                {
                    FillRows(bgra, row, row + 1);
                    continue;
                }

                ApplyLiveLineStateForRendering(state);
                _displayWindowVerticallyOpen = state.DisplayWindowVerticallyOpen;
                _displayWindowStateLine = StandardVStart + row + 1;
                _currentRenderRow = row;
                CapturePaletteFrameSpans(row, row + 1, 0, AmigaConstants.PalLowResWidth);
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
            var firstTrailingRow = Math.Clamp(finalLine - StandardVStart + 1, 0, LowResOutputHeight);
            if (firstTrailingRow >= LowResOutputHeight)
            {
                return;
            }

            RenderRows(
                bgra,
                firstTrailingRow,
                LowResOutputHeight,
                frameStartCycle,
                useTimedWrites,
                applyPendingWrites: false);
        }

        private static int GetCopperFrameInstructionLimit(long frameStartCycle, long frameStopCycle)
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
                _bus.Blitter.AdvanceTo(readyCycle);
                renderCursorCycle = Math.Max(renderCursorCycle, readyCycle);
                renderCursorPixelDelay = 0;
                copper.Cycle = Math.Max(copper.Cycle, readyCycle);
                return copper.Cycle < frameStopCycle;
            }

            var resumeCycle = waitCycle + CopperHpToCpuCycles(CopperWaitWakeHpUnits);
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

            return Math.Max(currentCycle, _bus.Blitter.GetPredictedCompletionCycle());
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
            return new CopperInstructionLatch(first, firstAccess, second, secondAccess);
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
            var visibleStopCycle = GetLineStartCycle(frameStartCycle, StandardVStart + LowResOutputHeight);
            var clippedStart = Math.Max(fromCycle, visibleStartCycle);
            var clippedStop = Math.Min(toCycle, visibleStopCycle);
            if (clippedStop <= clippedStart)
            {
                return;
            }

            var firstLine = Math.Clamp(GetBeamLineForCycle(frameStartCycle, clippedStart), StandardVStart, StandardVStart + LowResOutputHeight - 1);
            var lastLine = Math.Clamp(GetBeamLineForCycle(frameStartCycle, clippedStop - 1), StandardVStart, StandardVStart + LowResOutputHeight - 1);
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
                    ? AmigaConstants.PalLowResWidth
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
            for (var line = startLine; line < AmigaConstants.A500PalRasterLines; line++)
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

        private static bool TryGetFullMaskCopperWaitCycle(
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
            for (var line = startLine; line < AmigaConstants.A500PalRasterLines;)
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

        private static bool IsCopperWaitReleaseBlockedAtLineEnd(ushort target, int mask, int line, int horizontal)
        {
            if (horizontal + CopperWaitLineEndBlackoutHpUnits < CopperHorizontalUnitsPerLine)
            {
                return false;
            }

            var preBlackoutHorizontal = (CopperHorizontalUnitsPerLine - CopperWaitLineEndBlackoutHpUnits - 1) & 0xFE;
            var preBlackoutBeam = (ushort)(((line & 0xFF) << 8) | preBlackoutHorizontal);
            return (preBlackoutBeam & mask) >= (target & mask);
        }

        private static bool IsCopperComparisonSatisfied(
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
            return hpUnits * CopperHpCycles;
        }

        private static long GetLineStartCycle(long frameStartCycle, int line)
        {
            return frameStartCycle + ((long)line * PalLineCycles);
        }

        private static long GetCycleForCopperBeam(long frameStartCycle, int line, int horizontal)
        {
            return GetLineStartCycle(frameStartCycle, line) + ((long)horizontal * CopperHpCycles);
        }

        private static int GetBeamLineForCycle(long frameStartCycle, long cycle)
        {
            if (cycle <= frameStartCycle)
            {
                return 0;
            }

            var line = Math.Clamp((int)((cycle - frameStartCycle) / PalLineCycles), 0, AmigaConstants.A500PalRasterLines - 1);
            while (line + 1 < AmigaConstants.A500PalRasterLines && GetLineStartCycle(frameStartCycle, line + 1) <= cycle)
            {
                line++;
            }

            while (line > 0 && GetLineStartCycle(frameStartCycle, line) > cycle)
            {
                line--;
            }

            return line;
        }

        private static int GetCopperHorizontalForCycle(long frameStartCycle, long cycle)
        {
            GetCopperBeamPositionForCycle(frameStartCycle, cycle, out _, out var horizontal);
            return horizontal;
        }

        private static void GetCopperBeamPositionForCycle(long frameStartCycle, long cycle, out int line, out int horizontal)
        {
            if (cycle <= frameStartCycle)
            {
                line = 0;
                horizontal = 0;
                return;
            }

            var frameCycle = cycle - frameStartCycle;
            line = (int)(frameCycle / PalLineCycles);
            if (line >= AmigaConstants.A500PalRasterLines)
            {
                line = AmigaConstants.A500PalRasterLines - 1;
            }

            var lineCycle = frameCycle - ((long)line * PalLineCycles);
            horizontal = (int)(lineCycle / CopperHpCycles);
            if (horizontal > LastCopperHorizontal)
            {
                horizontal = LastCopperHorizontal;
            }
        }

        private static int GetOutputRowForCycle(long frameStartCycle, long cycle)
        {
            return GetBeamLineForCycle(frameStartCycle, cycle) - StandardVStart;
        }

        private static int GetOutputXForCycle(long frameStartCycle, long cycle)
        {
            return GetCopperOutputX(GetCopperHorizontalForCycle(frameStartCycle, cycle));
        }

        private static int GetOutputXForCycle(long frameStartCycle, long cycle, int pixelDelay)
        {
            return GetCopperOutputX(GetCopperHorizontalForCycle(frameStartCycle, cycle), pixelDelay);
        }

        private static int GetCopperOutputX(int horizontal)
        {
            return GetCopperOutputX(horizontal, 0);
        }

        private static int GetCopperOutputX(int horizontal, int pixelDelay)
        {
            var expandedHorizontal = horizontal >= 0xE0
                ? horizontal + 0x100
                : horizontal;
            return Math.Clamp(((expandedHorizontal - DefaultDdfStart) * 2) + pixelDelay, 0, AmigaConstants.PalLowResWidth);
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

        private void RenderRows(
            Span<uint> bgra,
            int rowStart,
            int rowStop,
            long frameStartCycle,
            bool useTimedWrites,
            int xStart = 0,
            int xStop = -1,
            bool applyPendingWrites = true)
        {
            if (xStop < 0)
            {
                xStop = AmigaConstants.PalLowResWidth;
            }

            if (!useTimedWrites)
            {
                CapturePaletteFrameSpans(rowStart, rowStop, xStart, xStop);
                FillRows(bgra, rowStart, rowStop, xStart, xStop);
                RenderBitplanes(bgra, rowStart, rowStop, xStart, xStop);
                return;
            }

            rowStart = Math.Clamp(rowStart, 0, LowResOutputHeight);
            rowStop = Math.Clamp(rowStop, rowStart, LowResOutputHeight);
            xStart = Math.Clamp(xStart, 0, AmigaConstants.PalLowResWidth);
            xStop = Math.Clamp(xStop, xStart, AmigaConstants.PalLowResWidth);
            for (var row = rowStart; row < rowStop; row++)
            {
                _currentRenderRow = row;
                if (applyPendingWrites)
                {
                    ApplyPendingWrites(GetOutputRowStartCycle(frameStartCycle, row));
                }

                AdvanceDisplayWindowStateToLine(StandardVStart + row);
                CapturePaletteFrameSpans(row, row + 1, xStart, xStop);
                FillRows(bgra, row, row + 1, xStart, xStop);
                RenderBitplanes(bgra, row, row + 1, xStart, xStop);
            }

            _currentRenderRow = -1;
        }

        private static long GetOutputRowStartCycle(long frameStartCycle, int row)
        {
            return GetLineStartCycle(frameStartCycle, StandardVStart + row);
        }

        private ushort ReadChipWordForPresentation(uint address, int row)
        {
            if (!_useTimedPresentationReads)
            {
                return _bus.ReadChipWordForPresentation(address);
            }

            row = Math.Clamp(row, 0, LowResOutputHeight - 1);
            return ReadChipWordForPresentationAtCycle(address, GetOutputRowStartCycle(_renderFrameStartCycle, row));
        }

        private ushort ReadChipWordForPresentationAtCycle(uint address, long cycle)
        {
            return _useTimedPresentationReads
                ? _bus.ReadChipWordForPresentation(address, cycle)
                : _bus.ReadChipWordForPresentation(address);
        }

        private ushort ReadCopperWordForPresentation(uint address, long cycle, out AmigaBusAccessResult access)
        {
            if (!_useTimedPresentationReads)
            {
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Copper,
                    AmigaBusAccessKind.Copper,
                    AmigaBusAccessTarget.ChipRam,
                    address,
                    AmigaBusAccessSize.Word,
                    cycle,
                    isWrite: false);
                access = new AmigaBusAccessResult(request, cycle, cycle);
                return _bus.ReadChipWordForPresentation(address);
            }

            return _bus.ReadChipWordForPresentationWithArbitration(
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                address,
                cycle,
                out access);
        }

        private ushort ReadBitplaneWordForPresentation(uint address, int row, int plane, int word)
        {
            if (plane >= GetAgnusBitplaneFetchPlaneCount())
            {
                return IsLatchedOnlyOcsBpu7Plane(_bplcon0, plane)
                    ? _bitplaneDataRegisters[plane]
                    : (ushort)0;
            }

            if (IsLatchedOnlyOcsBpu7Plane(_bplcon0, plane))
            {
                return _bitplaneDataRegisters[plane];
            }

            if (_renderingLiveCapture && TryReadLiveCapturedBitplaneWord(row, plane, word, out var captured))
            {
                return captured;
            }

            if (!_useTimedPresentationReads)
            {
                var immediateValue = _bus.ReadChipWordForPresentation(address);
                LoadBitplaneDataRegister(plane, immediateValue);
                return immediateValue;
            }

            row = Math.Clamp(row, 0, LowResOutputHeight - 1);
            var lineStart = GetOutputRowStartCycle(_renderFrameStartCycle, row);
            var wordStride = GetBitplaneFetchSlotStride(IsHighResolutionEnabled());
            var planeSlot = GetBitplaneFetchSlot(plane, wordStride);
            var fetchHorizontal = GetDataFetchStartValue() + (word * wordStride) + planeSlot;
            var fetchCycle = AgnusChipSlotScheduler.AlignToSlot(lineStart + ((long)fetchHorizontal * CopperHpCycles));
            if (!_bus.TryReadDisplayDmaWordForPresentation(
                    AmigaBusRequester.Bitplane,
                    AmigaBusAccessKind.Bitplane,
                    address,
                    fetchCycle,
                    out var value,
                    out var access))
            {
                return 0;
            }

            _bitplaneDmaReadLatch = new BitplaneDmaReadLatch(row, plane, word, value, granted: true, access.GrantedCycle);
            return ConsumePresentationBitplaneDmaLatch(ref _bitplaneDmaReadLatch);
        }

        private BitplaneDmaReadLatch LoadLiveBitplaneDmaLatch(int row, int plane, int word, uint address, long fetchCycle)
        {
            return _bus.TryReadRowBitplaneDmaWord(address, fetchCycle, out var value, out var grantedCycle)
                ? new BitplaneDmaReadLatch(row, plane, word, value, granted: true, grantedCycle)
                : BitplaneDmaReadLatch.Denied(row, plane, word, grantedCycle);
        }

        private void ConsumeLiveBitplaneDmaLatch(ref BitplaneDmaReadLatch latch)
        {
            if (!latch.HasValue)
            {
                return;
            }

            var row = latch.Row;
            var plane = latch.Plane;
            var word = latch.Word;
            _liveBitplaneWords[GetLiveBitplaneWordIndex(row, plane, word)] = latch.Value;
            _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(row, plane)] |= 1UL << word;
            if (latch.Granted)
            {
                _liveBitplaneDmaFetches++;
                RecordLiveDisplayDmaCycle(latch.GrantedCycle);
            }

            if (!_liveTimelineUnsafeForFrame)
            {
                _displayTimeline.RecordBitplaneFetch(row, plane, word, latch.Value, latch.Granted);
            }

            _liveFetchBatchWordCount++;
            latch = default;
        }

        private ushort ConsumePresentationBitplaneDmaLatch(ref BitplaneDmaReadLatch latch)
        {
            if (!latch.HasValue || !latch.Granted)
            {
                latch = default;
                return 0;
            }

            _lastBitplaneDmaFetches++;
            RecordDisplayDmaCycle(latch.GrantedCycle);
            LoadBitplaneDataRegister(latch.Plane, latch.Value);
            var value = latch.Value;
            latch = default;
            return value;
        }

        private void LoadBitplaneDataRegister(int plane, ushort value)
        {
            if ((uint)plane >= (uint)_bitplaneDataRegisters.Length)
            {
                return;
            }

            _bitplaneDataRegisters[plane] = value;
            _bitplaneDataRegisterWritten[plane] = true;
        }

        private bool TryReadSpriteWordForPresentation(
            uint address,
            int row,
            int spriteIndex,
            int word,
            out ushort value)
            => TryReadSpriteWordForPresentation(
                address,
                row,
                spriteIndex,
                word,
                out value,
                recordLiveCapture: false);

        private bool TryReadSpriteWordForPresentation(
            uint address,
            int row,
            int spriteIndex,
            int word,
            out ushort value,
            bool recordLiveCapture)
        {
            row = Math.Clamp(row, 0, LowResOutputHeight - 1);
            spriteIndex = Math.Clamp(spriteIndex, 0, _sprites.Length - 1);
            word = Math.Clamp(word, 0, LiveSpriteWordsPerChannel - 1);
            if ((_renderingLiveCapture || recordLiveCapture) &&
                TryReadLiveCapturedSpriteWord(row, spriteIndex, word, out value))
            {
                return true;
            }

            if (_renderingLiveCapture && !recordLiveCapture)
            {
                value = 0;
                return false;
            }

            if (!_useTimedPresentationReads && !recordLiveCapture)
            {
                _spriteDmaReadLatch = new SpriteDmaReadLatch(
                    row,
                    spriteIndex,
                    word,
                    _bus.ReadChipWordForPresentation(address),
                    granted: true,
                    grantedCycle: 0);
                return ConsumeSpriteDmaReadLatch(
                    ref _spriteDmaReadLatch,
                    recordDmaFetch: false,
                    recordLiveCapture: false,
                    out value);
            }

            if (!recordLiveCapture &&
                _useTimedPresentationReads &&
                _previousLiveSpriteFrameStartCycle == _renderFrameStartCycle &&
                _previousLiveSpriteFrameCommands.Count > 0)
            {
                return TryReadPreviousLiveSpriteWord(row, spriteIndex, word, out value, out var denied) && !denied;
            }

            if (!IsSpriteDmaSlotAvailable(spriteIndex, word))
            {
                _spriteDmaReadLatch = SpriteDmaReadLatch.Denied(row, spriteIndex, word, GetSpriteDmaFetchCycle(row, spriteIndex, word));
                return ConsumeSpriteDmaReadLatch(
                    ref _spriteDmaReadLatch,
                    recordDmaFetch: false,
                    recordLiveCapture,
                    out value);
            }

            var fetchCycle = GetSpriteDmaFetchCycle(row, spriteIndex, word);
            var alreadyCaptured = recordLiveCapture && _bus.IsHrmChipSlotReserved(fetchCycle);
            _spriteDmaReadLatch = LoadSpriteDmaReadLatch(row, spriteIndex, word, address, fetchCycle);
            if (!_spriteDmaReadLatch.Granted)
            {
                return ConsumeSpriteDmaReadLatch(
                    ref _spriteDmaReadLatch,
                    recordDmaFetch: false,
                    recordLiveCapture,
                    out value);
            }

            return ConsumeSpriteDmaReadLatch(
                ref _spriteDmaReadLatch,
                recordDmaFetch: !alreadyCaptured,
                recordLiveCapture,
                out value);
        }

        private SpriteDmaReadLatch LoadSpriteDmaReadLatch(int row, int spriteIndex, int word, uint address, long fetchCycle)
        {
            return _bus.TryReadRowSpriteDmaWordForPresentation(address, fetchCycle, out var value, out var grantedCycle)
                ? new SpriteDmaReadLatch(row, spriteIndex, word, value, granted: true, grantedCycle)
                : SpriteDmaReadLatch.Denied(row, spriteIndex, word, grantedCycle);
        }

        private bool ConsumeSpriteDmaReadLatch(
            ref SpriteDmaReadLatch latch,
            bool recordDmaFetch,
            bool recordLiveCapture,
            out ushort value)
        {
            if (!latch.HasValue || !latch.Granted)
            {
                value = 0;
                if (latch.HasValue)
                {
                    RecordMissedSpriteDmaSlot(recordLiveCapture);
                }

                latch = default;
                return false;
            }

            value = latch.Value;
            if (recordDmaFetch)
            {
                RecordSpriteDmaFetch(latch.GrantedCycle, recordLiveCapture);
            }

            LoadSpriteDataRegister(latch.SpriteIndex, latch.Word, value);
            if (recordLiveCapture)
            {
                StoreLiveCapturedSpriteWord(latch.Row, latch.SpriteIndex, latch.Word, value);
            }

            latch = default;
            return true;
        }

        private void LoadSpriteDataRegister(int spriteIndex, int word, ushort value)
        {
            if ((uint)spriteIndex >= (uint)_sprites.Length)
            {
                return;
            }

            if (word == 0)
            {
                _sprites[spriteIndex].DataA = value;
            }
            else if (word == 1)
            {
                _sprites[spriteIndex].DataB = value;
            }
        }

        private bool TryReadPreviousLiveSpriteWord(
            int row,
            int spriteIndex,
            int word,
            out ushort value,
            out bool denied)
        {
            value = 0;
            denied = false;
            if (_previousLiveSpriteFrameStartCycle != _renderFrameStartCycle ||
                !HasPreviousLiveSpriteWord(row, spriteIndex, word))
            {
                return false;
            }

            var maskIndex = GetLiveSpriteMaskIndex(row, spriteIndex);
            var bit = (byte)(1 << word);
            denied = (_previousLiveSpriteDeniedMasks[maskIndex] & bit) != 0;
            value = denied
                ? (ushort)0
                : _previousLiveSpriteWords[GetLiveSpriteWordIndex(row, spriteIndex, word)];
            return true;
        }

        private bool HasPreviousLiveSpriteWord(int row, int spriteIndex, int word)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)spriteIndex >= LiveSpriteChannelCount ||
                (uint)word >= LiveSpriteWordsPerChannel)
            {
                return false;
            }

            var maskIndex = GetLiveSpriteMaskIndex(row, spriteIndex);
            var bit = (byte)(1 << word);
            return (_previousLiveSpriteWordMasks[maskIndex] & bit) != 0;
        }

        private static int GetBitplaneFetchSlotStride(bool highResolution)
            => highResolution ? 4 : 8;

        private static int GetBitplaneFetchSlot(int plane, int fetchSlotStride)
        {
            if (fetchSlotStride <= 4)
            {
                return (uint)plane < (uint)HighResBitplaneFetchSlotsByPlane.Length
                    ? HighResBitplaneFetchSlotsByPlane[plane]
                    : fetchSlotStride - 1;
            }

            return (uint)plane < (uint)LowResBitplaneFetchSlotsByPlane.Length
                ? LowResBitplaneFetchSlotsByPlane[plane]
                : fetchSlotStride - 1;
        }

        private static bool TryGetBitplanePlaneForFetchSlot(int slot, int planeCount, int fetchSlotStride, out int plane)
        {
            var planesByFetchSlot = fetchSlotStride <= 4
                ? HighResBitplanePlanesByFetchSlot
                : LowResBitplanePlanesByFetchSlot;
            if ((uint)slot < (uint)planesByFetchSlot.Length)
            {
                plane = planesByFetchSlot[slot];
                if ((uint)plane < (uint)planeCount)
                {
                    return true;
                }
            }

            plane = -1;
            return false;
        }

        private bool TryReadLiveCapturedSpriteWord(int row, int spriteIndex, int word, out ushort value)
        {
            value = 0;
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)spriteIndex >= LiveSpriteChannelCount ||
                (uint)word >= LiveSpriteWordsPerChannel)
            {
                return false;
            }

            var mask = _liveSpriteWordMasks[GetLiveSpriteMaskIndex(row, spriteIndex)];
            if ((mask & (1 << word)) == 0)
            {
                return false;
            }

            value = _liveSpriteWords[GetLiveSpriteWordIndex(row, spriteIndex, word)];
            return true;
        }

        private void StoreLiveCapturedSpriteWord(int row, int spriteIndex, int word, ushort value)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)spriteIndex >= LiveSpriteChannelCount ||
                (uint)word >= LiveSpriteWordsPerChannel)
            {
                return;
            }

            _liveSpriteWords[GetLiveSpriteWordIndex(row, spriteIndex, word)] = value;
            _liveSpriteWordMasks[GetLiveSpriteMaskIndex(row, spriteIndex)] =
                (byte)(_liveSpriteWordMasks[GetLiveSpriteMaskIndex(row, spriteIndex)] | (1 << word));
        }

        private void RecordSpriteDmaFetch(long grantedCycle, bool liveCapture)
        {
            if (liveCapture)
            {
                _liveSpriteDmaFetches++;
                RecordLiveDisplayDmaCycle(grantedCycle);
                return;
            }

            _lastSpriteDmaFetches++;
            RecordDisplayDmaCycle(grantedCycle);
        }

        private void RecordMissedSpriteDmaSlot(bool liveCapture)
        {
            if (liveCapture)
            {
                _liveMissedSpriteDmaSlots++;
                return;
            }

            _lastMissedSpriteDmaSlots++;
        }

        private bool TryReadLiveCapturedBitplaneWord(int row, int plane, int word, out ushort value)
        {
            value = 0;
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)plane >= LiveBitplanePlaneCount ||
                (uint)word >= (uint)MaxBitplaneFetchWords)
            {
                return false;
            }

            if (!IsLiveLineValid(row))
            {
                return false;
            }

            var mask = _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(row, plane)];
            if ((mask & (1UL << word)) == 0)
            {
                return false;
            }

            value = _liveBitplaneWords[GetLiveBitplaneWordIndex(row, plane, word)];
            return true;
        }

        private bool IsLiveCaptureCompleteForRendering(long frameStopCycle)
        {
            for (var row = 0; row < LowResOutputHeight; row++)
            {
                if (!IsLiveLineValid(row))
                {
                    continue;
                }

                var state = _liveLineStates[row];
                if (state.PlaneCount <= 0 ||
                    state.FetchWords <= 0 ||
                    !state.DisplayWindowVerticallyOpen ||
                    !IsBitplaneDmaEnabled(state.Dmacon))
                {
                    continue;
                }

                if (GetFirstLiveBitplaneFetchCycleForRendering(row, state) >= frameStopCycle)
                {
                    continue;
                }

                var expectedMask = state.FetchWords >= 64
                    ? ulong.MaxValue
                    : (1UL << state.FetchWords) - 1UL;
                for (var plane = 0; plane < state.PlaneCount; plane++)
                {
                    var actualMask = _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(row, plane)];
                    if ((actualMask & expectedMask) != expectedMask)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static long GetFirstLiveBitplaneFetchCycleForRendering(int row, LiveLineState state)
        {
            var planeCount = Math.Max(0, state.PlaneCount);
            for (var slot = 0; slot < state.FetchSlotStride; slot++)
            {
                if (TryGetBitplanePlaneForFetchSlot(slot, planeCount, state.FetchSlotStride, out _))
                {
                    var fetchHorizontal = state.DataFetchStart + slot;
                    return AgnusChipSlotScheduler.AlignToSlot(state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles));
                }
            }

            return long.MaxValue;
        }

        private int CountLiveBitplaneFetches()
        {
            var count = 0;
            for (var row = 0; row < LowResOutputHeight; row++)
            {
                if (!IsLiveLineValid(row))
                {
                    continue;
                }

                for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
                {
                    count += BitOperations.PopCount(_liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(row, plane)]);
                }
            }

            return count;
        }

        private bool IsLiveLineValid(int row)
        {
            return (uint)row < (uint)LowResOutputHeight && _liveLineStates[row].Generation == _liveGeneration;
        }

        private static int GetLiveBitplaneWordIndex(int row, int plane, int word)
        {
            return (row * LiveBitplaneWordsPerRow) + (plane * MaxBitplaneFetchWords) + word;
        }

        private static int GetLiveBitplaneMaskIndex(int row, int plane)
        {
            return (row * LiveBitplanePlaneCount) + plane;
        }

        private void ClearLiveBitplaneWordMasks(int row)
        {
            var offset = row * LiveBitplanePlaneCount;
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
                _liveBitplaneWordMasks[offset + plane] = 0;
            }
        }

        private void ClearLiveBitplaneWordMasksFrom(int row)
        {
            row = Math.Clamp(row, 0, LowResOutputHeight);
            var offset = row * LiveBitplanePlaneCount;
            Array.Clear(_liveBitplaneWordMasks, offset, _liveBitplaneWordMasks.Length - offset);
        }

        private void ClearLiveSpriteWordMasksFrom(int row)
        {
            row = Math.Clamp(row, 0, LowResOutputHeight);
            var offset = row * LiveSpriteChannelCount;
            Array.Clear(_liveSpriteWordMasks, offset, _liveSpriteWordMasks.Length - offset);
        }

        private void ResetLiveSpriteDmaStates(int controlRow)
        {
            controlRow = Math.Clamp(controlRow, 0, LowResOutputHeight);
            for (var spriteIndex = 0; spriteIndex < _liveSpriteDmaStates.Length; spriteIndex++)
            {
                _liveSpriteDmaStates[spriteIndex].Reset(_sprites[spriteIndex].Pointer, controlRow);
                _liveSpriteDmaExhausted[spriteIndex] = false;
            }
        }

        private void ResetLiveSpriteDmaState(int spriteIndex, int controlRow)
        {
            if ((uint)spriteIndex >= (uint)_liveSpriteDmaStates.Length)
            {
                return;
            }

            controlRow = Math.Clamp(controlRow, 0, LowResOutputHeight);
            _liveSpriteDmaStates[spriteIndex].Reset(_sprites[spriteIndex].Pointer, controlRow);
            _liveSpriteDmaExhausted[spriteIndex] = false;
            if (_liveFrameValid)
            {
                _liveNextSpriteRow = Math.Min(_liveNextSpriteRow, controlRow);
                _liveNextSpriteIndex = 0;
                _liveNextSpriteWord = 0;
                InvalidateLiveWorkCycle();
            }
        }

        private void UpdateLiveSpriteDmaPointerFromRegisterWrite(int spriteIndex, int controlRow)
        {
            if ((uint)spriteIndex >= (uint)_liveSpriteDmaStates.Length)
            {
                return;
            }

            var state = _liveSpriteDmaStates[spriteIndex];
            if (state.Exhausted || _liveSpriteDmaExhausted[spriteIndex])
            {
                return;
            }

            controlRow = Math.Clamp(controlRow, 0, LowResOutputHeight);
            if (state.LastVisibleStop >= 0 && controlRow <= state.LastVisibleStop)
            {
                state.ControlAddress = _sprites[spriteIndex].Pointer;
                return;
            }

            if (state.Active || state.HasPendingPos)
            {
                state.ControlAddress = _sprites[spriteIndex].Pointer;
                return;
            }

            // SPRxPT writes update the DMA pointer latch; they do not create a new control-word fetch
            // once the sprite sequencer has already advanced past its pending control row.
            if (controlRow > state.ControlRow)
            {
                return;
            }

            state.ControlAddress = _sprites[spriteIndex].Pointer;
            state.PendingPos = 0;
            state.HasPendingPos = false;

            if (_liveFrameValid)
            {
                _liveNextSpriteRow = Math.Min(_liveNextSpriteRow, state.ControlRow);
                _liveNextSpriteIndex = 0;
                _liveNextSpriteWord = 0;
                InvalidateLiveWorkCycle();
            }
        }

        private static int GetLiveSpriteWordIndex(int row, int spriteIndex, int word)
        {
            return (row * LiveSpriteWordsPerRow) + (spriteIndex * LiveSpriteWordsPerChannel) + word;
        }

        private static int GetLiveSpriteMaskIndex(int row, int spriteIndex)
        {
            return (row * LiveSpriteChannelCount) + spriteIndex;
        }

        private long GetSpriteDmaFetchCycle(int row, int spriteIndex, int word)
            => GetSpriteDmaFetchCycle(_renderFrameStartCycle, row, spriteIndex, word);

        private static long GetSpriteDmaFetchCycle(long frameStartCycle, int row, int spriteIndex, int word)
        {
            var lineStart = GetOutputRowStartCycle(frameStartCycle, row);
            var firstSpriteHorizontal = AgnusHrmOcsSlotTable.FirstSpriteHorizontal;
            var horizontal = firstSpriteHorizontal + (Math.Clamp(spriteIndex, 0, 7) * 4) + (Math.Clamp(word, 0, 1) * 2);
            return AgnusChipSlotScheduler.AlignToSlot(lineStart + ((long)horizontal * CopperHpCycles));
        }

        private bool TryGetLiveSpriteDmaSlot(long slotCycle, out int row, out int spriteIndex, out int word)
        {
            row = 0;
            spriteIndex = 0;
            word = 0;
            if (slotCycle < _liveFrameStartCycle || slotCycle >= _liveFrameStartCycle + PalFrameCycles)
            {
                return false;
            }

            var line = GetBeamLineForCycle(_liveFrameStartCycle, slotCycle);
            row = line - StandardVStart;
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return false;
            }

            var horizontal = (int)((slotCycle - GetLineStartCycle(_liveFrameStartCycle, line)) / CopperHpCycles);
            var firstSpriteHorizontal = AgnusHrmOcsSlotTable.FirstSpriteHorizontal;
            var spriteOffset = horizontal - firstSpriteHorizontal;
            if (spriteOffset < 0 || spriteOffset >= _sprites.Length * 4)
            {
                return false;
            }

            var wordOffset = spriteOffset & 0x03;
            if (wordOffset != 0 && wordOffset != 2)
            {
                return false;
            }

            spriteIndex = spriteOffset / 4;
            word = wordOffset / 2;
            return true;
        }

        private bool TryCaptureKnownLiveSpriteDmaSlot(int row, int spriteIndex, int word, long slotCycle)
        {
            if ((uint)spriteIndex >= (uint)_liveSpriteDmaStates.Length)
            {
                return false;
            }

            if (!IsSpriteDmaChannelAvailable(spriteIndex))
            {
                if (WouldLiveSpriteSlotFetchIfChannelAvailable(row, spriteIndex))
                {
                    RecordTimelineSpriteDataFetch(row, spriteIndex, word, 0, granted: false);
                }

                return false;
            }

            if (_liveSpriteDmaExhausted[spriteIndex])
            {
                return false;
            }

            var state = _liveSpriteDmaStates[spriteIndex];
            var savedFrameStart = _renderFrameStartCycle;
            var savedEnforceDma = _enforceDmaForFrame;
            _renderFrameStartCycle = _liveFrameStartCycle;
            _enforceDmaForFrame = true;
            try
            {
                return TryCaptureStatefulLiveSpriteDmaSlot(state, row, spriteIndex, word, slotCycle);
            }
            finally
            {
                _renderFrameStartCycle = savedFrameStart;
                _enforceDmaForFrame = savedEnforceDma;
            }
        }

        private bool TryCaptureStatefulLiveSpriteDmaSlot(
            LiveSpriteDmaState state,
            int row,
            int spriteIndex,
            int word,
            long slotCycle)
        {
            if ((uint)row >= (uint)LowResOutputHeight ||
                (uint)word >= LiveSpriteWordsPerChannel ||
                state.Exhausted)
            {
                return false;
            }

            if (state.Active && row >= state.Descriptor.YStop)
            {
                state.Active = false;
            }

            if (!state.Active && row > state.ControlRow)
            {
                state.ControlRow = row;
            }

            if (!state.Active && row == state.ControlRow)
            {
                return TryCaptureLiveSpriteControlWord(state, row, spriteIndex, word, slotCycle);
            }

            if (!state.Active ||
                row < state.Descriptor.YStart ||
                row >= state.Descriptor.YStop)
            {
                return false;
            }

            var address = AddDmaPointerOffset(state.Descriptor.DataAddress, ((row - state.Descriptor.YStart) * 4) + (word * 2));
            var captured = TryReadSpriteWordForPresentation(
                address,
                row,
                spriteIndex,
                word,
                out var value,
                recordLiveCapture: true);
            if (!captured &&
                word == 1 &&
                !IsSpriteDmaSlotAvailable(spriteIndex, word) &&
                TryGetPriorLiveSpriteDatb(row, spriteIndex, out var priorDatb))
            {
                value = priorDatb;
            }

            var granted = captured && _bus.IsHrmChipSlotReserved(slotCycle);
            RecordTimelineSpriteDataFetch(row, spriteIndex, word, value, granted);
            return granted;
        }

        private bool TryGetPriorLiveSpriteDatb(int row, int spriteIndex, out ushort value)
        {
            value = 0;
            var valid = false;
            for (var y = 0; y < row; y++)
            {
                var status = _displayTimeline.GetSpriteFetchStatus(y, spriteIndex, 1);
                if (status == TimelineFetchStatus.Granted)
                {
                    value = _displayTimeline.GetSpriteWord(y, spriteIndex, 1);
                    valid = true;
                }
                else if (status == TimelineFetchStatus.Denied && valid)
                {
                    value = _displayTimeline.GetSpriteWord(y, spriteIndex, 1);
                }
            }

            return valid;
        }

        private bool TryCaptureLiveSpriteControlWord(
            LiveSpriteDmaState state,
            int row,
            int spriteIndex,
            int word,
            long slotCycle)
        {
            if (word == 0)
            {
                if (!TryReadSpriteWordForPresentation(state.ControlAddress, row, spriteIndex, 0, out var pos, recordLiveCapture: true))
                {
                    return false;
                }

                state.PendingPos = pos;
                state.HasPendingPos = true;
                return _bus.IsHrmChipSlotReserved(slotCycle);
            }

            if (!state.HasPendingPos)
            {
                return false;
            }

            if (!TryReadSpriteWordForPresentation(AddDmaPointerOffset(state.ControlAddress, 2), row, spriteIndex, 1, out var ctl, recordLiveCapture: true))
            {
                return false;
            }

            var slotGranted = _bus.IsHrmChipSlotReserved(slotCycle);
            var pendingPos = state.PendingPos;
            state.PendingPos = 0;
            state.HasPendingPos = false;
            if (slotGranted)
            {
                ApplyLiveSpriteControlRegisterFetch(spriteIndex, pendingPos, ctl, slotCycle);
            }

            if ((pendingPos | ctl) == 0)
            {
                state.Exhausted = true;
                state.Active = false;
                _liveSpriteDmaExhausted[spriteIndex] = true;
                return slotGranted;
            }

            var descriptor = CreateSpriteDescriptor(
                pendingPos,
                ctl,
                AddDmaPointerOffset(state.ControlAddress, 4),
                isDma: true,
                _sprites[spriteIndex].DataA,
                _sprites[spriteIndex].DataB);
            var rawHeight = Math.Max(0, descriptor.YStop - descriptor.YStart);
            var nextControlAddress = AddDmaPointerOffset(descriptor.DataAddress, rawHeight * 4);

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
                _liveSpriteDmaExhausted[spriteIndex] = true;
                return slotGranted;
            }

            var command = new SpriteFrameCommand(spriteIndex, row, descriptor);
            AppendUniqueSpriteFrameCommand(_spriteFrameCommands, command);
            RecordTimelineSpriteFrameCommand(command);
            state.Descriptor = descriptor;
            state.Active = true;
            state.LastVisibleStop = Math.Max(state.LastVisibleStop, descriptor.YStop);
            state.ControlAddress = nextControlAddress;
            state.ControlRow = Math.Clamp(descriptor.YStop + 1, 0, LowResOutputHeight);
            return slotGranted;
        }

        private void ApplyLiveSpriteControlRegisterFetch(int spriteIndex, ushort pos, ushort ctl, long cycle)
        {
            if ((uint)spriteIndex >= (uint)_sprites.Length)
            {
                return;
            }

            StopManualSpriteFrameCommands(spriteIndex, cycle);
            var sprite = _sprites[spriteIndex];
            sprite.Pos = pos;
            sprite.Ctl = ctl;
            sprite.ManualArmed = false;
        }

        private void RecordDisplayDmaCycle(long cycle)
        {
            if (_lastFirstDisplayDmaCycle < 0 || cycle < _lastFirstDisplayDmaCycle)
            {
                _lastFirstDisplayDmaCycle = cycle;
            }

            if (_lastLastDisplayDmaCycle < 0 || cycle > _lastLastDisplayDmaCycle)
            {
                _lastLastDisplayDmaCycle = cycle;
            }
        }

        private void FillRows(Span<uint> bgra, int rowStart, int rowStop, int xStart = 0, int xStop = -1)
        {
            if (xStop < 0)
            {
                xStop = AmigaConstants.PalLowResWidth;
            }

            rowStart = Math.Clamp(rowStart, 0, LowResOutputHeight);
            rowStop = Math.Clamp(rowStop, rowStart, LowResOutputHeight);
            xStart = Math.Clamp(xStart, 0, AmigaConstants.PalLowResWidth);
            xStop = Math.Clamp(xStop, xStart, AmigaConstants.PalLowResWidth);
            var color = ConvertColor(_colors[0]);
            for (var y = rowStart; y < rowStop; y++)
            {
                foreach (var outputY in EnumerateOutputRows(y))
                {
                    if (IsRenderingHighResolutionWidth())
                    {
                        bgra.Slice((outputY * _renderWidth) + (xStart * 2), (xStop - xStart) * 2).Fill(color);
                    }
                    else
                    {
                        bgra.Slice((outputY * _renderWidth) + xStart, xStop - xStart).Fill(color);
                    }
                }
            }
        }

        private void CapturePaletteFrameSpans(int rowStart, int rowStop, int xStart, int xStop)
        {
            rowStart = Math.Clamp(rowStart, 0, LowResOutputHeight);
            rowStop = Math.Clamp(rowStop, rowStart, LowResOutputHeight);
            xStart = Math.Clamp(xStart, 0, AmigaConstants.PalLowResWidth);
            xStop = Math.Clamp(xStop, xStart, AmigaConstants.PalLowResWidth);
            if (rowStart >= rowStop || xStart >= xStop)
            {
                return;
            }

            var window = GetEffectiveDisplayWindow();
            for (var row = rowStart; row < rowStop; row++)
            {
                if (_paletteFrameSpans.Count >= MaxPaletteFrameSpans)
                {
                    return;
                }

                var colorOffset = _paletteFrameSpans.Count * PaletteColorCount;
                Array.Copy(_convertedColors, 0, _paletteFrameSpanColors, colorOffset, PaletteColorCount);
                _paletteFrameSpans.Add(new PaletteFrameSpan(row, xStart, xStop, colorOffset, _bplcon0, _bplcon2, window));
            }
        }

        private void ApplyPendingWrites(long cycle)
        {
            while (_pendingIndex < _pendingWrites.Count && _pendingWrites[_pendingIndex].Cycle <= cycle)
            {
                var write = _pendingWrites[_pendingIndex++];
                if (_advancingLiveDma && write.Cycle != long.MinValue)
                {
                    AdvanceLiveDisplayWindowStateToCycle(write.Cycle);
                }

                if (_trackDisplayWindowState && write.Cycle != long.MinValue)
                {
                    AdvanceDisplayWindowStateToCycle(_renderFrameStartCycle, write.Cycle);
                }

                if (_advancingLiveDma &&
                    write.Cycle > _liveFrameStartCycle &&
                    write.Offset is 0x08E or 0x090)
                {
                    _liveFrameHasLateDisplayWindowWrites = true;
                }

                if (_advancingLiveDma)
                {
                    EnsureTimelineLineStartedBeforeDisplayWrite(write.Cycle);
                }

                ApplyWrite(write.Offset, write.Value, write.Cycle);
                RefreshLiveFrameInitialStateAfterFrameStartWrite(write.Cycle);
                if (_advancingLiveDma)
                {
                    RecordLiveFrameWrite(write.Cycle, write.Offset, write.Value);
                    RecordTimelineDisplayWrite(write.Cycle, write.Offset, isCopper: false);
                }

                if (_advancingLiveDma)
                {
                    if (write.Offset == 0x088)
                    {
                        _liveCopper.JumpTo(_copperListPointer, write.Cycle);
                    }
                    else if (write.Offset == 0x08A)
                    {
                        _liveCopper.JumpTo(_copperListPointer2, write.Cycle);
                    }
                }
            }

            CompactPendingWrites();
        }

        private void RefreshLiveFrameInitialStateAfterFrameStartWrite(long cycle)
        {
            if (_liveFrameValid &&
                _liveFrameInitialStateValid &&
                cycle <= _liveFrameStartCycle)
            {
                CaptureDisplayState(_liveFrameInitialState);
                if (_liveCapturedThroughCycle <= _liveFrameStartCycle)
                {
                    _liveCopper = CreateLiveCopperFrameStartState(_liveFrameStartCycle);
                    InvalidateLiveDisplayEventCycle();
                }
            }
        }

        private void RecordTimelineLineStart(int row, LiveLineState state)
        {
            if (!_advancingLiveDma || !_liveFrameValid)
            {
                return;
            }

            if (_liveTimelineUnsafeForFrame)
            {
                return;
            }

            var snapshotIndex = CaptureTimelineStateSnapshot(row, state);
            _displayTimeline.StartLine(row, snapshotIndex);
        }

        private void EnsureTimelineLineStartedBeforeDisplayWrite(long cycle)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                _liveTimelineUnsafeForFrame ||
                cycle < _liveFrameStartCycle ||
                cycle >= _liveFrameStartCycle + PalFrameCycles)
            {
                return;
            }

            var row = GetOutputRowForCycle(_liveFrameStartCycle, cycle);
            if ((uint)row >= (uint)LowResOutputHeight ||
                _displayTimeline.HasLine(row))
            {
                return;
            }

            if (IsLiveLineValid(row))
            {
                RecordTimelineLineStart(row, _liveLineStates[row]);
                return;
            }

            CaptureLiveLineState(row);
        }

        private void RecordTimelineDisplayWrite(long cycle, ushort offset, bool isCopper)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                cycle < _liveFrameStartCycle ||
                cycle >= _liveFrameStartCycle + PalFrameCycles)
            {
                return;
            }

            if (_liveTimelineUnsafeForFrame)
            {
                return;
            }

            if (!IsDisplayRegisterWrite(offset))
            {
                return;
            }

            if (IsTimelineSpritePointerWrite(offset))
            {
                return;
            }

            if (IsTimelineManualSpriteRegisterWrite(offset))
            {
                return;
            }

            if (IsTimelineCopperPointerLatchWrite(offset))
            {
                return;
            }

            if (IsTimelineCopperJumpWrite(offset))
            {
                return;
            }

            var row = GetOutputRowForCycle(_liveFrameStartCycle, cycle);
            if ((uint)row >= (uint)LowResOutputHeight ||
                !IsLiveLineValid(row) ||
                !_displayTimeline.HasLine(row))
            {
                return;
            }

            var pixelDelay = 0;
            if (isCopper)
            {
                pixelDelay = GetCopperWritePixelDelay(offset);
            }
            else
            {
                pixelDelay = GetTimelineCpuWritePixelDelay(offset);
            }

            var x = GetOutputXForCycle(_liveFrameStartCycle, cycle, pixelDelay);
            var snapshotIndex = CaptureTimelineStateSnapshot(row, _liveLineStates[row]);
            _displayTimeline.RecordDisplayChange(
                row,
                x,
                snapshotIndex,
                IsTimelineUnsafeDisplayWrite(offset),
                offset,
                isCopper);
            if (_displayTimeline.SegmentCount > MaxTimelineSegmentsPerFrame)
            {
                _liveTimelineUnsafeForFrame = true;
            }
        }

        private static bool TryFindFirstUnsafeTimelineLine(DisplayFrameTimeline timeline, out ushort offset, out bool isCopper)
        {
            for (var row = 0; row < LowResOutputHeight; row++)
            {
                if (!timeline.HasLine(row))
                {
                    continue;
                }

                var line = timeline.GetLine(row);
                if (line.UnsafeForTimelineRender)
                {
                    offset = line.UnsafeOffset;
                    isCopper = line.UnsafeIsCopper;
                    return true;
                }
            }

            offset = 0;
            isCopper = false;
            return false;
        }

        private int CaptureTimelineStateSnapshot(int row, LiveLineState fallbackState)
        {
            var snapshot = _displayTimeline.AddStateSnapshot();
            snapshot.LineStartCycle = GetOutputRowStartCycle(_liveFrameStartCycle, row);
            snapshot.Bplcon0 = _bplcon0;
            snapshot.Bplcon1 = _bplcon1;
            snapshot.Bplcon2 = _bplcon2;
            snapshot.DiwStart = _diwStart;
            snapshot.DiwStop = _diwStop;
            snapshot.DdfStart = _ddfStart;
            snapshot.DdfStop = _ddfStop;
            snapshot.Dmacon = _dmacon;
            snapshot.Bpl1Mod = _bpl1mod;
            snapshot.Bpl2Mod = _bpl2mod;
            snapshot.DisplayWindowVerticallyOpen = _liveDisplayWindowVerticallyOpen;
            snapshot.PlaneCount = GetAgnusBitplaneFetchPlaneCount();
            snapshot.DecodePlaneCount = GetDeniseBitplaneDecodePlaneCount();
            snapshot.FetchWords = GetDataFetchWordCount();
            snapshot.DataFetchStart = GetDataFetchStartValue();
            snapshot.FetchSlotStride = GetBitplaneFetchSlotStride(IsHighResolutionEnabled());
            snapshot.PaletteSnapshotIndex = CaptureLivePaletteSnapshot();
            Array.Copy(fallbackState.BitplanePointers, snapshot.BitplanePointers, fallbackState.BitplanePointers.Length);
            Array.Copy(fallbackState.BitplaneBaseRows, snapshot.BitplaneBaseRows, fallbackState.BitplaneBaseRows.Length);
            Array.Copy(fallbackState.BitplaneRowAddresses, snapshot.BitplaneRowAddresses, fallbackState.BitplaneRowAddresses.Length);
            Array.Copy(_bitplaneDataRegisters, snapshot.BitplaneDataRegisters, _bitplaneDataRegisters.Length);
            snapshot.PlaneHasRowMask = fallbackState.PlaneHasRowMask;

            return snapshot.Index;
        }

        private static bool IsTimelineUnsafeDisplayWrite(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            if (offset >= 0x180 && offset < 0x1C0)
            {
                return false;
            }

            if (IsTimelineSpritePointerWrite(offset))
            {
                return false;
            }

            if (IsTimelineManualSpriteRegisterWrite(offset))
            {
                return false;
            }

            if (IsTimelineCopperPointerLatchWrite(offset))
            {
                return false;
            }

            if (IsTimelineCopperJumpWrite(offset))
            {
                return false;
            }

            if (IsTimelineBitplanePointerWrite(offset))
            {
                return false;
            }

            if (offset >= 0x110 && offset <= 0x11A)
            {
                return false;
            }

            if (offset is 0x02E or 0x08E or 0x090 or 0x092 or 0x094 or 0x096 or 0x100 or 0x102 or 0x104 or 0x108 or 0x10A)
            {
                return false;
            }

            return true;
        }

        private static bool IsTimelineSpritePointerWrite(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset >= 0x120 && offset < 0x140;
        }

        private static bool IsTimelineManualSpriteRegisterWrite(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset >= 0x140 && offset < 0x180;
        }

        private static bool IsTimelineCopperPointerLatchWrite(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset is 0x080 or 0x082 or 0x084 or 0x086;
        }

        private static bool IsTimelineCopperJumpWrite(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset is 0x088 or 0x08A;
        }

        private static bool IsTimelineBitplanePointerWrite(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            return offset >= 0x0E0 && offset <= 0x0F6;
        }

        private static int GetTimelineCpuWritePixelDelay(ushort offset)
        {
            offset = (ushort)(offset & 0x01FE);
            // Pending CPU custom writes are applied at their presentation cycle; scrolled
            // playfield output samples the new BPLCON1 shifter state slightly before that
            // low-res span boundary in the existing timed presenter.
            return offset switch
            {
                0x092 or 0x094 => -4,
                0x08E or 0x090 => -3,
                _ => 0
            };
        }

        private static bool IsTimelineUnsafeFrameWrite(ushort offset, bool isCopper)
        {
            offset = (ushort)(offset & 0x01FE);
            if (isCopper)
            {
                return IsTimelineUnsafeDisplayWrite(offset);
            }

            return IsTimelineUnsafeDisplayWrite(offset);
        }

        private static void ApplySetClear(ref ushort register, ushort value)
        {
            var mask = (ushort)(value & DmaconWritableMask);
            if ((value & 0x8000) != 0)
            {
                register |= mask;
            }
            else
            {
                register &= (ushort)~mask;
            }
        }

        private static ushort ApplySetClearPreview(ushort register, ushort value)
        {
            var mask = (ushort)(value & DmaconWritableMask);
            return (value & 0x8000) != 0
                ? (ushort)(register | mask)
                : (ushort)(register & (ushort)~mask);
        }

        private static bool IsBitplaneDmaEnabled(ushort dmacon)
        {
            return (dmacon & (DmaconMasterEnable | DmaconBitplaneEnable)) == (DmaconMasterEnable | DmaconBitplaneEnable);
        }

        private static bool IsBitplaneDmaEnabledAfterSetClear(ushort dmacon, ushort value)
        {
            return IsBitplaneDmaEnabled(ApplySetClearPreview(dmacon, value));
        }

        public bool TryReadByte(ushort offset, out byte value)
        {
            if (offset == 0x00E)
            {
                value = 0x80;
                return true;
            }

            if (offset == 0x00F)
            {
                value = 0x00;
                return true;
            }

            value = 0;
            return false;
        }

        private void ApplyWrite(ushort offset, ushort value, long cycle = long.MinValue)
        {
            if (offset == 0x096)
            {
                var bitplaneDmaWasEnabled = IsBitplaneDmaEnabled(_dmacon);
                var liveCopperDmaWasEnabled = IsLiveCopperDmaEnabled();
                if (bitplaneDmaWasEnabled && !IsBitplaneDmaEnabledAfterSetClear(_dmacon, value))
                {
                    AnchorActiveBitplanePointersToCurrentRow();
                }

                ApplySetClear(ref _dmacon, value);
                if (_advancingLiveDma &&
                    !liveCopperDmaWasEnabled &&
                    IsLiveCopperDmaEnabled() &&
                    cycle != long.MinValue &&
                    _liveCopper.Cycle < cycle)
                {
                    _liveCopper.Cycle = cycle;
                    InvalidateLiveDisplayEventCycle();
                }

                if (!bitplaneDmaWasEnabled && IsBitplaneDmaEnabled(_dmacon))
                {
                    var planeCount = GetAgnusBitplaneFetchPlaneCount();
                    SetBitplaneBaseRows(0, planeCount, GetBitplaneDmaEnableBaseRow(cycle));
                }

                return;
            }

            if (offset == 0x100)
            {
                var oldPlaneCount = GetAgnusBitplaneFetchPlaneCount();
                var newPlaneCount = GetAgnusBitplaneFetchPlaneCount(value);
                AnchorActiveBitplanePointersToCurrentRow(oldPlaneCount);
                _bplcon0 = value;

                if (newPlaneCount > oldPlaneCount && IsBitplaneDmaEnabledForRendering())
                {
                    SetBitplaneBaseRows(oldPlaneCount, newPlaneCount, GetCurrentBitplaneBaseRow());
                }

                return;
            }

            if (offset == 0x102)
            {
                _bplcon1 = value;
                return;
            }

            if (offset == 0x104)
            {
                _bplcon2 = value;
                return;
            }

            if (offset == 0x02E)
            {
                _copcon = value;
                return;
            }

            if (offset == 0x080)
            {
                _copperListPointer = WriteDmaPointerHigh(_copperListPointer, value);
                return;
            }

            if (offset == 0x082)
            {
                _copperListPointer = WriteDmaPointerLow(_copperListPointer, value);
                return;
            }

            if (offset == 0x084)
            {
                _copperListPointer2 = WriteDmaPointerHigh(_copperListPointer2, value);
                return;
            }

            if (offset == 0x086)
            {
                _copperListPointer2 = WriteDmaPointerLow(_copperListPointer2, value);
                return;
            }

            if (offset is 0x088 or 0x08A)
            {
                return;
            }

            if (offset == 0x08E)
            {
                AnchorActiveBitplanePointersToCurrentRow();
                _diwStart = value;
                RebaseInactiveBitplaneRowsToDisplayWindow();
                return;
            }

            if (offset == 0x090)
            {
                AnchorActiveBitplanePointersToCurrentRow();
                _diwStop = value;
                RebaseInactiveBitplaneRowsToDisplayWindow();
                return;
            }

            if (offset == 0x092)
            {
                AnchorActiveBitplanePointersToCurrentRow();
                _ddfStart = value;
                return;
            }

            if (offset == 0x094)
            {
                AnchorActiveBitplanePointersToCurrentRow();
                _ddfStop = value;
                return;
            }

            if (offset == 0x108)
            {
                AnchorActiveBitplanePointersToCurrentRow();
                _bpl1mod = unchecked((short)value);
                return;
            }

            if (offset == 0x10A)
            {
                AnchorActiveBitplanePointersToCurrentRow();
                _bpl2mod = unchecked((short)value);
                return;
            }

            if (offset >= 0x110 && offset <= 0x11A)
            {
                ApplyBitplaneDataWrite(offset, value, cycle);
                return;
            }

            if (offset >= 0x180 && offset < 0x1C0)
            {
                var colorIndex = (offset - 0x180) / 2;
                _colors[colorIndex] = (ushort)(value & 0x0FFF);
                UpdateConvertedColor(colorIndex);
                _livePaletteSnapshotDirty = true;
                return;
            }

            if (offset >= 0x0E0 && offset <= 0x0F6)
            {
                var plane = (offset - 0x0E0) / 4;
                if (plane < _bitplanePointers.Length)
                {
                    if ((offset & 2) == 0)
                    {
                        _bitplanePointers[plane] = WriteDmaPointerHigh(_bitplanePointers[plane], value);
                    }
                    else
                    {
                        _bitplanePointers[plane] = WriteDmaPointerLow(_bitplanePointers[plane], value);
                    }

                    _bitplaneBaseRows[plane] = GetCurrentBitplaneBaseRow();
                }

                return;
            }

            if (offset >= 0x120 && offset < 0x180)
            {
                ApplySpriteWrite(offset, value, cycle);
            }
        }

        private void ApplyBitplaneDataWrite(ushort offset, ushort value, long cycle)
        {
            var plane = (offset - 0x110) / 2;
            if ((uint)plane >= (uint)_bitplaneDataRegisters.Length)
            {
                return;
            }

            _bitplaneDataRegisters[plane] = value;
            _bitplaneDataRegisterWritten[plane] = true;
            if (plane == 0)
            {
                CaptureBitplaneDataShiftGroup(cycle);
            }
        }

        private void CaptureBitplaneDataShiftGroup(long cycle)
        {
            if (_bitplaneDataSpans.Count >= MaxBitplaneDataSpans)
            {
                return;
            }

            if (!TryGetBitplaneDataWritePosition(cycle, out var row, out var xStart))
            {
                return;
            }

            var pixelCount = IsHighResolutionEnabled() ? 8 : 16;
            var xStop = Math.Min(AmigaConstants.PalLowResWidth, xStart + pixelCount);
            if (xStop <= xStart)
            {
                return;
            }

            var span = new BitplaneDataSpan(
                row,
                xStart,
                xStop,
                _bitplaneDataRegisters[0],
                _bitplaneDataRegisters[1],
                _bitplaneDataRegisters[2],
                _bitplaneDataRegisters[3],
                _bitplaneDataRegisters[4],
                _bitplaneDataRegisters[5]);
            _bitplaneDataSpans.Add(span);
            if (_advancingLiveDma &&
                _liveFrameValid &&
                !_liveTimelineUnsafeForFrame &&
                cycle >= _liveFrameStartCycle &&
                cycle < _liveFrameStartCycle + PalFrameCycles)
            {
                _displayTimeline.RecordBitplaneDataSpan(span);
            }
        }

        private bool TryGetBitplaneDataWritePosition(long cycle, out int row, out int xStart)
        {
            if (cycle == long.MinValue)
            {
                row = TryGetCurrentOutputRow(out var currentRow)
                    ? currentRow
                    : 0;
                xStart = GetDataFetchStartX(GetDisplayWindow());
                return (uint)row < (uint)LowResOutputHeight;
            }

            var frameStart = _advancingLiveDma
                ? _liveFrameStartCycle
                : _renderFrameStartCycle;
            row = GetOutputRowForCycle(frameStart, cycle);
            xStart = GetOutputXForCycle(frameStart, cycle);
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return false;
            }

            xStart = Math.Clamp(xStart, 0, AmigaConstants.PalLowResWidth);
            return true;
        }

        private void ApplyCopperMove(ushort offset, ushort value, long cycle, bool applyHardwareSideEffects)
        {
            ApplyWrite(offset, value, cycle);
            if (!applyHardwareSideEffects || !HasCopperHardwareSideEffect(offset))
            {
                return;
            }

            _bus.WriteDeviceWord(
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                0x00DFF000u + offset,
                value,
                cycle);
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

        private static bool HasCopperHardwareSideEffect(ushort offset)
        {
            return offset is 0x096 or 0x09A or 0x09C or 0x09E ||
                (offset >= 0x040 && offset <= 0x074) ||
                offset is 0x020 or 0x022 or 0x024 or 0x07E;
        }

        private int GetCurrentBitplaneBaseRow()
        {
            var windowY = GetDisplayWindow().Y;
            if (_renderingCopperFrame)
            {
                if (_currentCopperRow == 0 && windowY < 0)
                {
                    return windowY;
                }

                return Math.Max(_currentCopperRow, windowY);
            }

            if (_advancingLiveDma)
            {
                if (_currentCopperRow == 0 && windowY < 0)
                {
                    return windowY;
                }

                return Math.Max(_currentCopperRow, windowY);
            }

            return _currentRenderRow >= 0
                ? Math.Max(_currentRenderRow, windowY)
                : windowY;
        }

        private int GetBitplaneDmaEnableBaseRow(long cycle)
        {
            var row = GetCurrentBitplaneBaseRow();
            return ShouldDelayOcsBitplaneDmaEnableUntilNextLine(cycle) ? row + 1 : row;
        }

        private bool ShouldDelayOcsBitplaneDmaEnableUntilNextLine(long cycle)
        {
            if (cycle == long.MinValue)
            {
                return false;
            }

            var frameStartCycle = _renderingCopperFrame
                ? _renderFrameStartCycle
                : _liveFrameValid ? _liveFrameStartCycle : 0;
            var horizontal = GetCopperHorizontalForCycle(frameStartCycle, cycle);
            var fetchStart = GetDataFetchStartValue();
            var fetchStop = GetDataFetchStopValue();
            var fetchUnit = IsHighResolutionEnabled() ? 4 : 8;
            return horizontal >= fetchStart && horizontal <= fetchStop - fetchUnit;
        }

        private void AnchorActiveBitplanePointersToCurrentRow()
        {
            AnchorActiveBitplanePointersToCurrentRow(GetAgnusBitplaneFetchPlaneCount());
        }

        private void AnchorActiveBitplanePointersToCurrentRow(int planeCount)
        {
            planeCount = Math.Clamp(planeCount, 0, _bitplaneBaseRows.Length);
            if (planeCount == 0)
            {
                return;
            }

            if (!IsBitplaneDmaEnabledForRendering())
            {
                return;
            }

            var fetchWords = GetDataFetchWordCount();
            if (fetchWords <= 0)
            {
                return;
            }

            if (!TryGetCurrentOutputRow(out var row) || row < GetDisplayWindow().Y)
            {
                return;
            }

            for (var plane = 0; plane < planeCount; plane++)
            {
                var displaySourceY = row - _bitplaneBaseRows[plane];
                if (displaySourceY < 0)
                {
                    continue;
                }

                var mod = (plane & 1) == 0 ? _bpl1mod : _bpl2mod;
                var rowStride = (fetchWords * 2) + mod;
                var byteOffset = displaySourceY * rowStride;
                _bitplanePointers[plane] = AddDmaPointerOffset(_bitplanePointers[plane], byteOffset);
                _bitplaneBaseRows[plane] = row;
            }
        }

        private void RebaseInactiveBitplaneRowsToDisplayWindow()
        {
            var planeCount = GetAgnusBitplaneFetchPlaneCount();
            if (planeCount == 0)
            {
                return;
            }

            if (TryGetCurrentOutputRow(out var row) && row >= GetDisplayWindow().Y)
            {
                return;
            }

            SetBitplaneBaseRows(0, planeCount, GetDisplayWindow().Y);
        }

        private void RebaseActiveBitplaneRowsToLiveFrameStart()
        {
            var planeCount = GetAgnusBitplaneFetchPlaneCount();
            if (planeCount == 0 || !IsBitplaneDmaEnabled(_dmacon))
            {
                return;
            }

            SetBitplaneBaseRows(0, planeCount, GetDisplayWindow().Y);
        }

        private bool TryGetCurrentOutputRow(out int row)
        {
            if (_currentRenderRow >= 0)
            {
                row = _currentRenderRow;
                return true;
            }

            if (_renderingCopperFrame)
            {
                row = _currentCopperRow;
                return true;
            }

            if (_advancingLiveDma)
            {
                row = _currentCopperRow;
                return true;
            }

            row = 0;
            return false;
        }

        private int GetCurrentSpriteDmaControlRow()
        {
            return TryGetCurrentOutputRow(out var row)
                ? Math.Clamp(row, 0, LowResOutputHeight)
                : 0;
        }

        private int GetCurrentManualSpriteCommandRow(SpriteState sprite, long cycle)
        {
            if (!TryGetCurrentOutputRow(out var row))
            {
                return 0;
            }

            row = Math.Clamp(row, 0, LowResOutputHeight);
            if (cycle == long.MinValue)
            {
                return row;
            }

            var frameStartCycle = _renderingCopperFrame
                ? _renderFrameStartCycle
                : _liveFrameValid ? _liveFrameStartCycle : long.MinValue;
            if (frameStartCycle == long.MinValue)
            {
                return row;
            }

            var descriptor = CreateManualSpriteDescriptor(sprite);
            if (row < descriptor.YStart)
            {
                return row;
            }

            var beamX = GetOutputXForCycle(frameStartCycle, cycle);
            if (beamX >= descriptor.X)
            {
                row++;
            }

            return Math.Clamp(row, 0, LowResOutputHeight);
        }

        private void SetBitplaneBaseRows(int startPlane, int endPlane, int row)
        {
            startPlane = Math.Clamp(startPlane, 0, _bitplaneBaseRows.Length);
            endPlane = Math.Clamp(endPlane, startPlane, _bitplaneBaseRows.Length);
            for (var i = startPlane; i < endPlane; i++)
            {
                _bitplaneBaseRows[i] = row;
            }
        }

        private void ApplySpriteWrite(ushort offset, ushort value, long cycle)
        {
            if (offset >= 0x120 && offset < 0x140)
            {
                var sprite = (offset - 0x120) / 4;
                if (sprite < _sprites.Length)
                {
                    if ((offset & 2) == 0)
                    {
                        _sprites[sprite].Pointer = WriteDmaPointerHigh(_sprites[sprite].Pointer, value);
                        UpdateLiveSpriteDmaPointerFromRegisterWrite(sprite, GetCurrentSpriteDmaControlRow());
                    }
                    else
                    {
                        _sprites[sprite].Pointer = WriteDmaPointerLow(_sprites[sprite].Pointer, value);
                        UpdateLiveSpriteDmaPointerFromRegisterWrite(sprite, GetCurrentSpriteDmaControlRow());
                        CaptureDmaSpriteFrameCommand(sprite);
                    }
                }

                return;
            }

            if (offset >= 0x140 && offset < 0x180)
            {
                var sprite = (offset - 0x140) / 8;
                var register = (offset - 0x140) % 8;
                if (sprite >= _sprites.Length)
                {
                    return;
                }

                switch (register)
                {
                    case 0:
                        StopManualSpriteFrameCommands(sprite, cycle);
                        _sprites[sprite].Pos = value;
                        CaptureManualSpriteFrameCommandIfArmed(sprite, cycle);
                        break;
                    case 2:
                        StopManualSpriteFrameCommands(sprite, cycle);
                        _sprites[sprite].Ctl = value;
                        _sprites[sprite].ManualArmed = false;
                        break;
                    case 4:
                        StopManualSpriteFrameCommands(sprite, cycle);
                        _sprites[sprite].DataA = value;
                        _sprites[sprite].ManualArmed = true;
                        CaptureManualSpriteFrameCommandIfArmed(sprite, cycle);
                        break;
                    case 6:
                        StopManualSpriteFrameCommands(sprite, cycle);
                        _sprites[sprite].DataB = value;
                        CaptureManualSpriteFrameCommandIfArmed(sprite, cycle);
                        break;
                }
            }
        }

        private void ResetFrameCounters()
        {
            _lastBitplaneNonZeroPixels = 0;
            _lastBitplaneRows = 0;
            _lastBitplaneWords = 0;
            _lastBitplaneMinX = AmigaConstants.PalLowResWidth;
            _lastBitplaneMinY = LowResOutputHeight;
            _lastBitplaneMaxX = -1;
            _lastBitplaneMaxY = -1;
            _lastNormalPlayfieldNonZeroPixels = 0;
            _lastNormalPlayfieldMinX = AmigaConstants.PalLowResWidth;
            _lastNormalPlayfieldMinY = LowResOutputHeight;
            _lastNormalPlayfieldMaxX = -1;
            _lastNormalPlayfieldMaxY = -1;
            _lastPlayfield1NonZeroPixels = 0;
            _lastPlayfield1MinX = AmigaConstants.PalLowResWidth;
            _lastPlayfield1MinY = LowResOutputHeight;
            _lastPlayfield1MaxX = -1;
            _lastPlayfield1MaxY = -1;
            _lastPlayfield2NonZeroPixels = 0;
            _lastPlayfield2MinX = AmigaConstants.PalLowResWidth;
            _lastPlayfield2MinY = LowResOutputHeight;
            _lastPlayfield2MaxX = -1;
            _lastPlayfield2MaxY = -1;
            Array.Clear(_lastBitplaneColorCounts);
            _lastSpriteNonZeroPixels = 0;
            _lastSpriteMinX = AmigaConstants.PalLowResWidth;
            _lastSpriteMinY = LowResOutputHeight;
            _lastSpriteMaxX = -1;
            _lastSpriteMaxY = -1;
            _lastBitplaneDmaFetches = 0;
            _lastSpriteDmaFetches = 0;
            _lastMissedSpriteDmaSlots = 0;
            _lastFirstDisplayDmaCycle = -1;
            _lastLastDisplayDmaCycle = -1;
            _lastTimelineSegmentCount = 0;
            _lastTimelineFallbackCount = 0;
            _lastTimelineSpriteCommandCount = 0;
            _lastActiveTimelineFrameCount = 0;
            _lastArchivedTimelineFrameCount = 0;
            _lastPlanarChunkCacheHits = 0;
            _lastPlanarChunkCacheMisses = 0;
            _lastTimelineCoalescedSegmentCount = 0;
            _lastTimelineFastPathRowCount = 0;
            _lastTimelineFastPathMissCount = 0;
            _lastSpriteRecoveryAttemptCount = 0;
            _lastSpriteDeniedFetchCount = 0;
        }

        private void ResetPlayfieldPriorityMasks()
        {
            var length = AmigaConstants.PalLowResWidth * LowResOutputHeight;
            if (_playfieldPriorityMasks.Length != length)
            {
                _playfieldPriorityMasks = new byte[length];
                return;
            }

            Array.Clear(_playfieldPriorityMasks);
        }

        private void SetPlayfieldPriorityMask(int x, int y, byte mask)
        {
            if ((uint)x >= (uint)AmigaConstants.PalLowResWidth || (uint)y >= (uint)LowResOutputHeight)
            {
                return;
            }

            _playfieldPriorityMasks[(y * AmigaConstants.PalLowResWidth) + x] = mask;
        }

        private void RenderBitplanes(Span<uint> bgra, int bandStart, int bandStop, int xClipStart = 0, int xClipStop = -1)
        {
            if (xClipStop < 0)
            {
                xClipStop = AmigaConstants.PalLowResWidth;
            }

            var decodePlaneCount = GetDeniseBitplaneDecodePlaneCount();
            if (decodePlaneCount == 0)
            {
                return;
            }

            var hasBitplaneDataSpans = HasBitplaneDataSpanInBand(bandStart, bandStop, xClipStart, xClipStop);
            var bitplaneDmaEnabled = !_enforceDmaForFrame ||
                (_dmacon & (DmaconMasterEnable | DmaconBitplaneEnable)) == (DmaconMasterEnable | DmaconBitplaneEnable);
            if (!bitplaneDmaEnabled && !hasBitplaneDataSpans)
            {
                return;
            }

            var fetchPlaneCount = GetAgnusBitplaneFetchPlaneCount();
            var planeCount = Math.Min(decodePlaneCount, _bitplanePointers.Length);
            var planeWords = _renderPlaneWords;
            var planeHasRow = _renderPlaneHasRow;
            var window = GetEffectiveDisplayWindow();
            var fetchWords = GetDataFetchWordCount();
            if (window.Width <= 0 || window.Height <= 0 || fetchWords <= 0)
            {
                return;
            }

            var highResolution = IsHighResolutionEnabled();
            var fetchPixels = fetchWords * 16;
            var drawPixels = highResolution ? fetchPixels / 2 : fetchPixels;
            var originX = GetDataFetchStartX(window);
            var clipLeft = Math.Max(Math.Max(0, window.X), xClipStart);
            var clipRight = Math.Min(Math.Min(AmigaConstants.PalLowResWidth, window.X + window.Width), xClipStop);
            var rowStart = Math.Max(Math.Max(0, bandStart), window.Y);
            var rowStop = Math.Min(Math.Min(LowResOutputHeight, bandStop), window.Y + window.Height);
            var holdAndModify = !highResolution && (_bplcon0 & 0x0800) != 0 && planeCount >= 6;
            var dualPlayfield = IsDualPlayfieldEnabled();
            var zeroScroll = (_bplcon1 & 0x00FF) == 0;
            var renderHighWidth = IsRenderingHighResolutionWidth();
            var renderHighHeight = IsRenderingHighResolutionHeight();
            var renderInterlace = InterlaceEnabled;
            var renderInterlaceField = _renderInterlaceField;
            var savedCurrentRenderRow = _currentRenderRow;
            try
            {
                for (var y = rowStart; y < rowStop; y++)
                {
                    _currentRenderRow = y;
                    _lastBitplaneRows++;
                    for (var plane = 0; plane < planeCount; plane++)
                    {
                        if (IsLatchedOnlyOcsBpu7Plane(_bplcon0, plane))
                        {
                            planeHasRow[plane] = true;
                            for (var word = 0; word < fetchWords; word++)
                            {
                                planeWords[plane, word] = _bitplaneDataRegisters[plane];
                            }

                            continue;
                        }

                        if (plane >= fetchPlaneCount)
                        {
                            planeHasRow[plane] = false;
                            for (var word = 0; word < fetchWords; word++)
                            {
                                planeWords[plane, word] = 0;
                            }

                            continue;
                        }

                        var mod = (plane & 1) == 0 ? _bpl1mod : _bpl2mod;
                        var rowStride = (fetchWords * 2) + mod;
                        var displaySourceY = y - _bitplaneBaseRows[plane];
                        var planeSourceY = displaySourceY;
                        var liveCapturedMask = _renderingLiveCapture
                            ? _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(y, plane)]
                            : 0UL;
                        planeHasRow[plane] = bitplaneDmaEnabled && (displaySourceY >= 0 || liveCapturedMask != 0);
                        var rowAddress = unchecked(_bitplanePointers[plane] + (uint)(planeSourceY * rowStride));
                        for (var word = 0; word < fetchWords; word++)
                        {
                            if (!planeHasRow[plane])
                            {
                                planeWords[plane, word] = 0;
                                continue;
                            }

                            if ((liveCapturedMask & (1UL << word)) != 0 &&
                                TryReadLiveCapturedBitplaneWord(y, plane, word, out var captured))
                            {
                                planeWords[plane, word] = captured;
                                LoadBitplaneDataRegister(plane, captured);
                                _lastBitplaneWords++;
                                continue;
                            }

                            var address = unchecked(rowAddress + (uint)(word * 2));
                            planeWords[plane, word] = ReadBitplaneWordForPresentation(address, y, plane, word);
                            _lastBitplaneWords++;
                        }
                    }

                    var xStart = hasBitplaneDataSpans
                        ? clipLeft
                        : Math.Max(clipLeft, Math.Max(0, originX));
                    var xStop = hasBitplaneDataSpans
                        ? clipRight
                        : Math.Min(clipRight, Math.Min(AmigaConstants.PalLowResWidth, originX + drawPixels + (highResolution ? 8 : 16)));
                    var hamColor = _colors[0];
                    if (!highResolution && !dualPlayfield && !holdAndModify && zeroScroll)
                    {
                        for (var x = xStart; x < xStop; x++)
                        {
                            if (!TryGetBitplaneDataSpanColorIndex(x, y, planeCount, highResolution: false, hiresSubPixel: -1, out var colorIndex))
                            {
                                var relativeX = x - originX;
                                if ((uint)relativeX >= (uint)fetchPixels)
                                {
                                    continue;
                                }

                                var word = relativeX >> 4;
                                if ((uint)word >= MaxBitplaneFetchWords)
                                {
                                    continue;
                                }

                                var mask = 1 << (15 - (relativeX & 0x0F));
                                colorIndex = 0;
                                for (var plane = 0; plane < planeCount; plane++)
                                {
                                    if (planeHasRow[plane] && (planeWords[plane, word] & mask) != 0)
                                    {
                                        colorIndex |= 1 << plane;
                                    }
                                }
                            }

                            colorIndex = ApplyUndocumentedNormalPlayfieldPriorityQuirk(colorIndex, planeCount);
                            if (colorIndex != 0)
                            {
                                RecordBitplanePixel(colorIndex, NormalPlayfieldPriorityMask, x, y);
                            }

                            SetPlayfieldPriorityMask(
                                x,
                                y,
                                colorIndex == 0 ? (byte)0 : NormalPlayfieldPriorityMask);
                            WriteLowResolutionOutputPixel(
                                bgra,
                                x,
                                y,
                                _convertedColors[colorIndex],
                                renderHighWidth,
                                renderHighHeight,
                                renderInterlace,
                                renderInterlaceField);
                        }

                        continue;
                    }

                    for (var x = xStart; x < xStop; x++)
                    {
                        if (highResolution)
                        {
                            var leftColorIndex = GetBitplaneColorIndex(planeWords, planeHasRow, planeCount, x, originX, fetchPixels, hiresSubPixel: 0);
                            var rightColorIndex = GetBitplaneColorIndex(planeWords, planeHasRow, planeCount, x, originX, fetchPixels, hiresSubPixel: 1);
                            SetPlayfieldPriorityMask(
                                x,
                                y,
                                (leftColorIndex | rightColorIndex) == 0 ? (byte)0 : NormalPlayfieldPriorityMask);
                            if ((leftColorIndex | rightColorIndex) != 0)
                            {
                                RecordBitplanePixel(
                                    leftColorIndex != 0 ? leftColorIndex : rightColorIndex,
                                    NormalPlayfieldPriorityMask,
                                    x,
                                    y);
                            }

                            if (renderHighWidth)
                            {
                                WriteHighResolutionOutputPixelPair(
                                    bgra,
                                    x,
                                    y,
                                    ConvertColorIndex(leftColorIndex),
                                    ConvertColorIndex(rightColorIndex),
                                    renderHighWidth,
                                    renderHighHeight,
                                    renderInterlace,
                                    renderInterlaceField);
                            }
                            else
                            {
                                WriteLowResolutionOutputPixel(
                                    bgra,
                                    x,
                                    y,
                                    ConvertColorIndex(SelectLowResolutionHiResColorIndex(leftColorIndex, rightColorIndex)),
                                    renderHighWidth,
                                    renderHighHeight,
                                    renderInterlace,
                                    renderInterlaceField);
                            }

                            continue;
                        }

                        var colorIndex = 0;
                        var playfieldPriorityMask = (byte)0;
                        if (dualPlayfield)
                        {
                            var dualPixel = GetDualPlayfieldPixel(planeWords, planeHasRow, planeCount, x, originX, fetchPixels);
                            colorIndex = dualPixel.ColorIndex;
                            playfieldPriorityMask = dualPixel.PriorityMask;
                        }
                        else
                        {
                            colorIndex = GetBitplaneColorIndex(planeWords, planeHasRow, planeCount, x, originX, fetchPixels);
                            colorIndex = ApplyUndocumentedNormalPlayfieldPriorityQuirk(colorIndex, planeCount);
                            playfieldPriorityMask = colorIndex == 0 ? (byte)0 : NormalPlayfieldPriorityMask;
                        }

                        SetPlayfieldPriorityMask(x, y, playfieldPriorityMask);
                        if (colorIndex != 0)
                        {
                            RecordBitplanePixel(colorIndex, playfieldPriorityMask, x, y);
                        }

                        var pixel = holdAndModify
                            ? ConvertHamPixel(colorIndex, ref hamColor)
                            : ConvertColorIndex(colorIndex);
                        WriteLowResolutionOutputPixel(
                            bgra,
                            x,
                            y,
                            pixel,
                            renderHighWidth,
                            renderHighHeight,
                            renderInterlace,
                            renderInterlaceField);
                    }
                }
            }
            finally
            {
                _currentRenderRow = savedCurrentRenderRow;
            }
        }

        private void RecordBitplanePixel(int colorIndex, byte playfieldPriorityMask, int x, int y)
        {
            _lastBitplaneNonZeroPixels++;
            _lastBitplaneMinX = Math.Min(_lastBitplaneMinX, x);
            _lastBitplaneMinY = Math.Min(_lastBitplaneMinY, y);
            _lastBitplaneMaxX = Math.Max(_lastBitplaneMaxX, x);
            _lastBitplaneMaxY = Math.Max(_lastBitplaneMaxY, y);
            if ((uint)colorIndex < (uint)_lastBitplaneColorCounts.Length)
            {
                _lastBitplaneColorCounts[colorIndex]++;
            }

            if ((playfieldPriorityMask & NormalPlayfieldPriorityMask) != 0)
            {
                RecordNormalPlayfieldPixel(x, y);
            }

            if ((playfieldPriorityMask & Playfield1PriorityMask) != 0)
            {
                RecordPlayfield1Pixel(x, y);
            }

            if ((playfieldPriorityMask & Playfield2PriorityMask) != 0)
            {
                RecordPlayfield2Pixel(x, y);
            }
        }

        private void RecordNormalPlayfieldPixel(int x, int y)
        {
            _lastNormalPlayfieldNonZeroPixels++;
            _lastNormalPlayfieldMinX = Math.Min(_lastNormalPlayfieldMinX, x);
            _lastNormalPlayfieldMinY = Math.Min(_lastNormalPlayfieldMinY, y);
            _lastNormalPlayfieldMaxX = Math.Max(_lastNormalPlayfieldMaxX, x);
            _lastNormalPlayfieldMaxY = Math.Max(_lastNormalPlayfieldMaxY, y);
        }

        private void RecordPlayfield1Pixel(int x, int y)
        {
            _lastPlayfield1NonZeroPixels++;
            _lastPlayfield1MinX = Math.Min(_lastPlayfield1MinX, x);
            _lastPlayfield1MinY = Math.Min(_lastPlayfield1MinY, y);
            _lastPlayfield1MaxX = Math.Max(_lastPlayfield1MaxX, x);
            _lastPlayfield1MaxY = Math.Max(_lastPlayfield1MaxY, y);
        }

        private void RecordPlayfield2Pixel(int x, int y)
        {
            _lastPlayfield2NonZeroPixels++;
            _lastPlayfield2MinX = Math.Min(_lastPlayfield2MinX, x);
            _lastPlayfield2MinY = Math.Min(_lastPlayfield2MinY, y);
            _lastPlayfield2MaxX = Math.Max(_lastPlayfield2MaxX, x);
            _lastPlayfield2MaxY = Math.Max(_lastPlayfield2MaxY, y);
        }

        private int GetBitplaneColorIndex(ushort[,] planeWords, bool[] planeHasRow, int planeCount, int x, int originX, int fetchPixels, int hiresSubPixel = -1)
        {
            if (TryGetBitplaneDataSpanColorIndex(x, _currentRenderRow, planeCount, hiresSubPixel >= 0, hiresSubPixel, out var spanColorIndex))
            {
                return spanColorIndex;
            }

            var colorIndex = 0;
            for (var plane = 0; plane < planeCount; plane++)
            {
                if (!planeHasRow[plane])
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
                if (word < 0 || word >= MaxBitplaneFetchWords)
                {
                    continue;
                }

                var bit = 15 - (relativeX & 0x0F);
                colorIndex |= ((planeWords[plane, word] >> bit) & 1) << plane;
            }

            return colorIndex;
        }

        private int ApplyUndocumentedNormalPlayfieldPriorityQuirk(int colorIndex, int planeCount)
        {
            if (planeCount >= 5 && GetPlayfield2Priority() >= 5 && (colorIndex & 0x10) != 0)
            {
                return 0x10;
            }

            return colorIndex;
        }

        private static int SelectLowResolutionHiResColorIndex(int leftColorIndex, int rightColorIndex)
        {
            if (leftColorIndex == rightColorIndex)
            {
                return leftColorIndex;
            }

            if (leftColorIndex == 0)
            {
                return rightColorIndex;
            }

            return leftColorIndex;
        }

        private DualPlayfieldPixel GetDualPlayfieldPixel(ushort[,] planeWords, bool[] planeHasRow, int planeCount, int x, int originX, int fetchPixels)
        {
            if (TryGetBitplaneDataSpanColorIndex(x, _currentRenderRow, planeCount, highResolution: false, hiresSubPixel: -1, out var spanColorIndex))
            {
                return ConvertRawColorIndexToDualPlayfieldPixel(spanColorIndex, planeCount);
            }

            var playfield1 = 0;
            var playfield2 = 0;
            for (var plane = 0; plane < planeCount; plane++)
            {
                if (!planeHasRow[plane])
                {
                    continue;
                }

                var relativeX = x - originX - GetPlaneHorizontalScroll(plane);
                if (relativeX < 0 || relativeX >= fetchPixels)
                {
                    continue;
                }

                var word = relativeX >> 4;
                if (word < 0 || word >= MaxBitplaneFetchWords)
                {
                    continue;
                }

                var bit = 15 - (relativeX & 0x0F);
                var pixelBit = (planeWords[plane, word] >> bit) & 1;
                if ((plane & 1) == 0)
                {
                    playfield1 |= pixelBit << (plane / 2);
                }
                else
                {
                    playfield2 |= pixelBit << (plane / 2);
                }
            }

            if (playfield1 == 0 && playfield2 == 0)
            {
                return default;
            }

            var priorityMask = (byte)0;
            if (playfield1 != 0)
            {
                priorityMask |= Playfield1PriorityMask;
            }

            if (playfield2 != 0)
            {
                priorityMask |= Playfield2PriorityMask;
            }

            var playfield2Color = playfield2 == 0 ? 0 : playfield2 + 8;
            var playfield1Color = GetPlayfield1Priority() >= 5 && playfield1 != 0 ? 0 : playfield1;
            playfield2Color = GetPlayfield2Priority() >= 5 && playfield2 != 0 ? 0 : playfield2Color;
            if ((_bplcon2 & 0x0040) != 0)
            {
                return new DualPlayfieldPixel(playfield2 != 0 ? playfield2Color : playfield1Color, priorityMask);
            }

            return new DualPlayfieldPixel(playfield1 != 0 ? playfield1Color : playfield2Color, priorityMask);
        }

        private bool HasBitplaneDataSpanInBand(int rowStart, int rowStop, int xStart, int xStop)
        {
            if (_bitplaneDataSpans.Count == 0)
            {
                return false;
            }

            if (xStop < 0)
            {
                xStop = AmigaConstants.PalLowResWidth;
            }

            rowStart = Math.Clamp(rowStart, 0, LowResOutputHeight);
            rowStop = Math.Clamp(rowStop, rowStart, LowResOutputHeight);
            xStart = Math.Clamp(xStart, 0, AmigaConstants.PalLowResWidth);
            xStop = Math.Clamp(xStop, xStart, AmigaConstants.PalLowResWidth);
            for (var i = _bitplaneDataSpans.Count - 1; i >= 0; i--)
            {
                var span = _bitplaneDataSpans[i];
                if (span.Row >= rowStart &&
                    span.Row < rowStop &&
                    span.XStop > xStart &&
                    span.XStart < xStop)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetBitplaneDataSpanColorIndex(
            int x,
            int y,
            int planeCount,
            bool highResolution,
            int hiresSubPixel,
            out int colorIndex)
        {
            colorIndex = 0;
            if ((uint)y >= (uint)LowResOutputHeight)
            {
                return false;
            }

            for (var i = _bitplaneDataSpans.Count - 1; i >= 0; i--)
            {
                var span = _bitplaneDataSpans[i];
                if (!span.Contains(x, y))
                {
                    continue;
                }

                var enabledPlanes = Math.Min(planeCount, _bitplaneDataRegisters.Length);
                for (var plane = 0; plane < enabledPlanes; plane++)
                {
                    var relativeX = x - span.XStart - GetPlaneHorizontalScroll(plane);
                    if (highResolution)
                    {
                        relativeX = (relativeX * 2) + Math.Clamp(hiresSubPixel, 0, 1);
                    }

                    if ((uint)relativeX >= 16)
                    {
                        continue;
                    }

                    var bit = 15 - relativeX;
                    colorIndex |= ((span.GetWord(plane) >> bit) & 1) << plane;
                }

                return true;
            }

            return false;
        }

        private DualPlayfieldPixel ConvertRawColorIndexToDualPlayfieldPixel(int rawColorIndex, int planeCount)
        {
            var playfield1 = 0;
            var playfield2 = 0;
            for (var plane = 0; plane < planeCount; plane++)
            {
                var pixelBit = (rawColorIndex >> plane) & 1;
                if ((plane & 1) == 0)
                {
                    playfield1 |= pixelBit << (plane / 2);
                }
                else
                {
                    playfield2 |= pixelBit << (plane / 2);
                }
            }

            if (playfield1 == 0 && playfield2 == 0)
            {
                return default;
            }

            var priorityMask = (byte)0;
            if (playfield1 != 0)
            {
                priorityMask |= Playfield1PriorityMask;
            }

            if (playfield2 != 0)
            {
                priorityMask |= Playfield2PriorityMask;
            }

            var playfield2Color = playfield2 == 0 ? 0 : playfield2 + 8;
            var playfield1Color = GetPlayfield1Priority() >= 5 && playfield1 != 0 ? 0 : playfield1;
            playfield2Color = GetPlayfield2Priority() >= 5 && playfield2 != 0 ? 0 : playfield2Color;
            if ((_bplcon2 & 0x0040) != 0)
            {
                return new DualPlayfieldPixel(playfield2 != 0 ? playfield2Color : playfield1Color, priorityMask);
            }

            return new DualPlayfieldPixel(playfield1 != 0 ? playfield1Color : playfield2Color, priorityMask);
        }

        private int GetPlayfield1Priority()
        {
            return _bplcon2 & 0x0007;
        }

        private int GetPlayfield2Priority()
        {
            return (_bplcon2 >> 3) & 0x0007;
        }

        private int GetDataFetchStartX(DisplayWindow window)
        {
            var ddfStart = GetDataFetchStartValue();
            var defaultStart = IsHighResolutionEnabled() ? DefaultHighResDdfStart : DefaultDdfStart;
            var defaultOrigin = Math.Clamp(window.X, 0, AmigaConstants.PalLowResOverscanBorderX);
            return defaultOrigin + ((ddfStart - defaultStart) * 2);
        }

        private int GetPlaneHorizontalScroll(int plane)
        {
            var playfield1Delay = _bplcon1 & 0x0F;
            return (plane & 1) == 0
                ? playfield1Delay
                : (_bplcon1 >> 4) & 0x0F;
        }

        private DisplayWindow GetDisplayWindow()
        {
            var hStart = _diwStart & 0x00FF;
            var hStop = (_diwStop & 0x00FF) + 0x100;

            var vStart = GetDisplayWindowStartLine();
            var vStop = GetDisplayWindowStopLine(vStart);

            return new DisplayWindow(
                hStart - StandardHStart,
                vStart - StandardVStart,
                hStop - hStart,
                vStop - vStart);
        }

        private int GetDisplayWindowStartLine()
        {
            return (_diwStart >> 8) & 0x00FF;
        }

        private int GetDisplayWindowStopLine(int vStart)
        {
            var vStop = (_diwStop >> 8) & 0x00FF;
            if (vStop < 0x80)
            {
                vStop += 0x100;
            }

            if (vStop <= vStart)
            {
                vStop += 0x100;
            }

            return vStop;
        }

        private DisplayWindow GetEffectiveDisplayWindow()
        {
            return _trackDisplayWindowState && !_displayWindowVerticallyOpen
                ? default
                : GetDisplayWindow();
        }

        private void ResetDisplayWindowStateTracking()
        {
            _displayWindowVerticallyOpen = false;
            _displayWindowStateLine = 0;
        }

        private void ResetLiveDisplayWindowStateTracking()
        {
            _liveDisplayWindowVerticallyOpen = false;
            _liveDisplayWindowStateLine = 0;
        }

        private void AdvanceDisplayWindowStateToCycle(long frameStartCycle, long cycle)
        {
            AdvanceDisplayWindowStateToLine(GetBeamLineForCycle(frameStartCycle, cycle));
        }

        private void AdvanceDisplayWindowStateToLine(int targetLine)
        {
            if (!_trackDisplayWindowState)
            {
                return;
            }

            targetLine = Math.Clamp(targetLine, 0, AmigaConstants.A500PalRasterLines - 1);
            while (_displayWindowStateLine <= targetLine)
            {
                var vStart = GetDisplayWindowStartLine();
                var vStop = GetDisplayWindowStopLine(vStart);
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

        private void AdvanceLiveDisplayWindowStateToCycle(long cycle)
        {
            AdvanceLiveDisplayWindowStateToLine(GetBeamLineForCycle(_liveFrameStartCycle, cycle));
        }

        private void AdvanceLiveDisplayWindowStateToLine(int targetLine)
        {
            targetLine = Math.Clamp(targetLine, 0, AmigaConstants.A500PalRasterLines - 1);
            while (_liveDisplayWindowStateLine <= targetLine)
            {
                var vStart = GetDisplayWindowStartLine();
                var vStop = GetDisplayWindowStopLine(vStart);
                if (_liveDisplayWindowStateLine == vStop)
                {
                    _liveDisplayWindowVerticallyOpen = false;
                }

                if (_liveDisplayWindowStateLine == vStart)
                {
                    _liveDisplayWindowVerticallyOpen = true;
                }

                _liveDisplayWindowStateLine++;
            }
        }

        private int GetDataFetchWordCount()
        {
            var ddfStart = GetDataFetchStartValue();
            var ddfStop = GetDataFetchStopValue();
            if (ddfStop < ddfStart)
            {
                return 0;
            }

            if (IsHighResolutionEnabled())
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

        private bool IsHighResolutionEnabled()
        {
            return (_bplcon0 & 0x8000) != 0;
        }

        private static bool IsHighResolutionEnabled(ushort bplcon0)
        {
            return (bplcon0 & 0x8000) != 0;
        }

        private int GetRequestedBitplaneCount()
            => GetRequestedBitplaneCount(_bplcon0);

        private static int GetRequestedBitplaneCount(ushort bplcon0)
            => (bplcon0 >> 12) & 0x7;

        private int GetAgnusBitplaneFetchPlaneCount()
            => GetAgnusBitplaneFetchPlaneCount(_bplcon0);

        private static int GetAgnusBitplaneFetchPlaneCount(ushort bplcon0)
        {
            var requested = GetRequestedBitplaneCount(bplcon0);
            if (requested <= 0)
            {
                return 0;
            }

            if (IsHighResolutionEnabled(bplcon0))
            {
                return Math.Min(requested, HighResBitplaneFetchSlotsByPlane.Length);
            }

            return requested == 7
                ? 4
                : Math.Min(requested, LiveBitplanePlaneCount);
        }

        private int GetDeniseBitplaneDecodePlaneCount()
            => GetDeniseBitplaneDecodePlaneCount(_bplcon0);

        private static int GetDeniseBitplaneDecodePlaneCount(ushort bplcon0)
        {
            var requested = GetRequestedBitplaneCount(bplcon0);
            return Math.Clamp(requested, 0, LiveBitplanePlaneCount);
        }

        private static bool IsLatchedOnlyOcsBpu7Plane(ushort bplcon0, int plane)
            => !IsHighResolutionEnabled(bplcon0) &&
                GetRequestedBitplaneCount(bplcon0) == 7 &&
                plane >= 4 &&
                plane < LiveBitplanePlaneCount;

        private bool IsDualPlayfieldEnabled()
        {
            return (_bplcon0 & 0x0400) != 0;
        }

        private uint WriteDmaPointerHigh(uint pointer, ushort highWord)
        {
            return _bus.WriteChipDmaPointerHigh(pointer, highWord);
        }

        private uint WriteDmaPointerLow(uint pointer, ushort lowWord)
        {
            return _bus.WriteChipDmaPointerLow(pointer, lowWord);
        }

        private uint AddDmaPointerOffset(uint pointer, int byteOffset)
        {
            return _bus.AddChipDmaPointerOffset(pointer, byteOffset);
        }

        private int GetDataFetchStartValue()
        {
            return _ddfStart & (IsHighResolutionEnabled() ? 0x00FC : 0x00F8);
        }

        private int GetDataFetchStopValue()
        {
            if (IsHighResolutionEnabled())
            {
                return _ddfStop & 0x00FC;
            }

            var blockStart = _ddfStop & 0x00F8;
            return (_ddfStop & 0x0004) != 0
                ? blockStart + 8
                : blockStart;
        }

        private void RenderSprites(Span<uint> bgra)
        {
            for (var spriteGroup = (_sprites.Length / 2) - 1; spriteGroup >= 0; spriteGroup--)
            {
                var spriteIndex = spriteGroup * 2;
                var evenSprites = GetSpriteFrameCommands(spriteIndex);
                var oddSprites = GetSpriteFrameCommands(spriteIndex + 1);
                Array.Clear(_evenSpriteAttached, 0, Math.Min(evenSprites.Count, _evenSpriteAttached.Length));
                Array.Clear(_oddSpriteAttached, 0, Math.Min(oddSprites.Count, _oddSpriteAttached.Length));

                for (var oddIndex = 0; oddIndex < oddSprites.Count; oddIndex++)
                {
                    var oddSprite = oddSprites[oddIndex];
                    var evenIndex = FindAttachedEvenSprite(evenSprites, _evenSpriteAttached, oddSprite);
                    if (evenIndex < 0)
                    {
                        if (oddSprite.Descriptor.Attached)
                        {
                            _oddSpriteAttached[oddIndex] = true;
                        }

                        continue;
                    }

                    _evenSpriteAttached[evenIndex] = true;
                    _oddSpriteAttached[oddIndex] = true;
                    RenderAttachedSpritePair(bgra, spriteIndex, evenSprites[evenIndex], oddSprite);
                }

                for (var i = 0; i < oddSprites.Count; i++)
                {
                    if (!_oddSpriteAttached[i] && !oddSprites[i].Descriptor.Attached)
                    {
                        RenderSprite(bgra, spriteIndex + 1, oddSprites[i]);
                    }
                }

                for (var i = 0; i < evenSprites.Count; i++)
                {
                    if (!_evenSpriteAttached[i])
                    {
                        RenderSprite(bgra, spriteIndex, evenSprites[i]);
                    }
                }
            }
        }

        private void RenderTimelineSprites(Span<uint> bgra, DisplayFrameTimeline timeline)
        {
            for (var spriteGroup = (_sprites.Length / 2) - 1; spriteGroup >= 0; spriteGroup--)
            {
                var spriteIndex = spriteGroup * 2;
                var evenSprites = GetTimelineSpriteFrameCommands(spriteIndex, timeline);
                var oddSprites = GetTimelineSpriteFrameCommands(spriteIndex + 1, timeline);
                Array.Clear(_evenSpriteAttached, 0, Math.Min(evenSprites.Count, _evenSpriteAttached.Length));
                Array.Clear(_oddSpriteAttached, 0, Math.Min(oddSprites.Count, _oddSpriteAttached.Length));

                for (var oddIndex = 0; oddIndex < oddSprites.Count; oddIndex++)
                {
                    var oddSprite = oddSprites[oddIndex];
                    var evenIndex = FindAttachedEvenSprite(evenSprites, _evenSpriteAttached, oddSprite);
                    if (evenIndex < 0)
                    {
                        if (oddSprite.Descriptor.Attached)
                        {
                            _oddSpriteAttached[oddIndex] = true;
                        }

                        continue;
                    }

                    _evenSpriteAttached[evenIndex] = true;
                    _oddSpriteAttached[oddIndex] = true;
                    RenderTimelineAttachedSpritePair(bgra, spriteIndex, evenSprites[evenIndex], oddSprite, timeline);
                }

                for (var i = 0; i < oddSprites.Count; i++)
                {
                    if (!_oddSpriteAttached[i] && !oddSprites[i].Descriptor.Attached)
                    {
                        RenderTimelineSprite(bgra, spriteIndex + 1, oddSprites[i], timeline);
                    }
                }

                for (var i = 0; i < evenSprites.Count; i++)
                {
                    if (!_evenSpriteAttached[i])
                    {
                        RenderTimelineSprite(bgra, spriteIndex, evenSprites[i], timeline);
                    }
                }
            }
        }

        private List<SpriteFrameCommand> GetTimelineSpriteFrameCommands(int spriteIndex, DisplayFrameTimeline timeline)
        {
            var commands = _spriteCommandScratch[spriteIndex];
            timeline.CopySpriteFrameCommands(spriteIndex, commands);
            return commands;
        }

        private List<SpriteFrameCommand> GetSpriteFrameCommands(int spriteIndex)
        {
            var commands = _spriteCommandScratch[spriteIndex];
            commands.Clear();
            for (var i = 0; i < _spriteFrameCommands.Count; i++)
            {
                var command = _spriteFrameCommands[i];
                if (command.SpriteIndex == spriteIndex)
                {
                    AppendUniqueSpriteFrameCommand(commands, command);
                }
            }

            if (commands.Count == 0 &&
                _previousLiveSpriteFrameStartCycle == _renderFrameStartCycle)
            {
                for (var i = 0; i < _previousLiveSpriteFrameCommands.Count; i++)
                {
                    var command = _previousLiveSpriteFrameCommands[i];
                    if (command.SpriteIndex == spriteIndex)
                    {
                        AppendUniqueSpriteFrameCommand(commands, command);
                    }
                }

                if (commands.Count > 0)
                {
                    return commands;
                }
            }

            var sprite = _sprites[spriteIndex];
            var allowStateFallback = !_renderingLiveCapture &&
                (!_useTimedPresentationReads ||
                    !_bus.LiveAgnusDmaEnabled ||
                    HasArchivedLiveSpriteFrameCommands(_renderFrameStartCycle));
            if (commands.Count == 0 && allowStateFallback && IsSpriteDmaEnabled())
            {
                if (IsSpriteDmaChannelAvailable(spriteIndex))
                {
                    AppendDmaSpriteFrameCommands(commands, spriteIndex, sprite.Pointer, 0);
                }
                else if (_useTimedPresentationReads)
                {
                    _lastMissedSpriteDmaSlots++;
                }
            }
            else if (commands.Count == 0 &&
                allowStateFallback &&
                TryGetManualSpriteDescriptor(spriteIndex, out var descriptor))
            {
                AppendUniqueSpriteFrameCommand(
                    commands,
                    new SpriteFrameCommand(spriteIndex, 0, descriptor));
            }

            return commands;
        }

        private bool HasArchivedLiveSpriteFrameCommands(long frameStartCycle)
        {
            return _previousLiveSpriteFrameStartCycle == frameStartCycle &&
                _previousLiveSpriteFrameCommands.Count > 0;
        }

        private static void AppendUniqueSpriteFrameCommand(List<SpriteFrameCommand> commands, SpriteFrameCommand command)
        {
            if (commands.Count >= commands.Capacity)
            {
                return;
            }

            for (var i = commands.Count - 1; i >= 0; i--)
            {
                if (IsPendingSpriteControlReplacement(commands[i], command))
                {
                    commands[i] = command;
                    return;
                }

                if (IsPendingSpriteControlReplacement(command, commands[i]))
                {
                    return;
                }

                if (IsOverlappingRestartOfSameSpriteImage(commands[i], command))
                {
                    return;
                }

                if (IsOverlappingRestartOfSameSpriteImage(command, commands[i]))
                {
                    commands[i] = command;
                    return;
                }

                if (commands[i].SpriteIndex == command.SpriteIndex &&
                    commands[i].Descriptor.HasSameRenderingAs(command.Descriptor))
                {
                    if (commands[i].Row <= command.Row)
                    {
                        return;
                    }

                    commands[i] = command;
                    return;
                }

                if (commands[i].HasSameRenderingAs(command))
                {
                    return;
                }
            }

            commands.Add(command);
        }

        private static bool IsPendingSpriteControlReplacement(SpriteFrameCommand earlier, SpriteFrameCommand later)
        {
            if (earlier.SpriteIndex != later.SpriteIndex ||
                earlier.Row >= later.Row)
            {
                return false;
            }

            var first = earlier.Descriptor;
            var second = later.Descriptor;
            return first.IsDma &&
                second.IsDma &&
                first.DataAddress == second.DataAddress &&
                first.YStart == second.YStart &&
                first.YStop == second.YStop &&
                first.Attached == second.Attached &&
                SpriteDescriptorDataMatches(first, second) &&
                later.Row <= first.YStart;
        }

        private static bool IsOverlappingRestartOfSameSpriteImage(SpriteFrameCommand earlier, SpriteFrameCommand later)
        {
            if (earlier.SpriteIndex != later.SpriteIndex)
            {
                return false;
            }

            var first = earlier.Descriptor;
            var second = later.Descriptor;
            return first.X == second.X &&
                first.YStart <= second.YStart &&
                second.YStart < first.YStop &&
                first.YStop == second.YStop &&
                first.Attached == second.Attached &&
                first.DataAddress == second.DataAddress &&
                first.IsDma == second.IsDma &&
                SpriteDescriptorDataMatches(first, second) &&
                earlier.Row <= later.Row;
        }

        private static bool SpriteDescriptorDataMatches(SpriteDescriptor left, SpriteDescriptor right)
        {
            return left.IsDma == right.IsDma &&
                (left.IsDma ||
                    (left.ManualDataA == right.ManualDataA &&
                        left.ManualDataB == right.ManualDataB));
        }

        private static int FindAttachedEvenSprite(
            IReadOnlyList<SpriteFrameCommand> evenSprites,
            bool[] evenAttached,
            SpriteFrameCommand oddSprite)
        {
            var bestIndex = -1;
            var bestDistance = int.MaxValue;
            for (var i = 0; i < evenSprites.Count; i++)
            {
                if (evenAttached[i] ||
                    !IsAttachedSpritePair(evenSprites[i], oddSprite) ||
                    !SpritesOverlapVertically(evenSprites[i].Descriptor, oddSprite.Descriptor))
                {
                    continue;
                }

                var distance = Math.Abs(evenSprites[i].Row - oddSprite.Row);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool SpritesOverlapVertically(SpriteDescriptor left, SpriteDescriptor right)
        {
            return Math.Max(left.YStart, right.YStart) < Math.Min(left.YStop, right.YStop);
        }

        private static bool IsAttachedSpritePair(SpriteFrameCommand evenSprite, SpriteFrameCommand oddSprite)
        {
            return oddSprite.Descriptor.Attached;
        }

        private bool TryGetManualSpriteDescriptor(int spriteIndex, out SpriteDescriptor descriptor)
        {
            var sprite = _sprites[spriteIndex];
            if (!sprite.ManualArmed || (sprite.Pos | sprite.Ctl | sprite.DataA | sprite.DataB) == 0)
            {
                descriptor = default;
                return false;
            }

            descriptor = CreateManualSpriteDescriptor(sprite);
            return true;
        }

        private void CaptureDmaSpriteFrameCommand(int spriteIndex)
        {
            if (!_captureSpriteFrameCommands ||
                _advancingLiveDma ||
                _useTimedPresentationReads ||
                !IsSpriteDmaEnabled() ||
                !IsSpriteDmaChannelAvailable(spriteIndex))
            {
                return;
            }

            var sprite = _sprites[spriteIndex];
            if (!TryGetCurrentOutputRow(out var row))
            {
                row = 0;
            }

            AppendDmaSpriteFrameCommands(_spriteFrameCommands, spriteIndex, sprite.Pointer, row);
        }

        private void CaptureManualSpriteFrameCommandIfArmed(int spriteIndex, long cycle = long.MinValue)
        {
            if (!_captureSpriteFrameCommands && !_advancingLiveDma)
            {
                return;
            }

            var sprite = _sprites[spriteIndex];
            if (!sprite.ManualArmed || (sprite.Pos | sprite.Ctl | sprite.DataA | sprite.DataB) == 0)
            {
                return;
            }

            var descriptor = CreateManualSpriteDescriptor(sprite);
            if (_captureSpriteFrameCommands || _advancingLiveDma)
            {
                AddSpriteFrameCommand(spriteIndex, descriptor, cycle);
                return;
            }
        }

        private void CaptureInitialManualSpriteFrameCommands()
        {
            for (var spriteIndex = 0; spriteIndex < _sprites.Length; spriteIndex++)
            {
                var sprite = _sprites[spriteIndex];
                if (!sprite.ManualArmed || (sprite.Pos | sprite.Ctl | sprite.DataA | sprite.DataB) == 0)
                {
                    continue;
                }

                var command = new SpriteFrameCommand(spriteIndex, 0, CreateManualSpriteDescriptor(sprite));
                AppendUniqueSpriteFrameCommand(_spriteFrameCommands, command);
                _displayTimeline.AddSpriteFrameCommand(command);
            }
        }

        private static SpriteDescriptor CreateManualSpriteDescriptor(SpriteState sprite)
        {
            var baseDescriptor = CreateSpriteDescriptor(sprite.Pos, sprite.Ctl, 0, isDma: false, sprite.DataA, sprite.DataB);
            if (baseDescriptor.YStop <= baseDescriptor.YStart)
            {
                return new SpriteDescriptor(
                    baseDescriptor.X,
                    baseDescriptor.YStart,
                    baseDescriptor.YStart,
                    baseDescriptor.Attached,
                    baseDescriptor.DataAddress,
                    baseDescriptor.IsDma,
                    baseDescriptor.ManualDataA,
                    baseDescriptor.ManualDataB);
            }

            return new SpriteDescriptor(
                baseDescriptor.X,
                baseDescriptor.YStart,
                LowResOutputHeight,
                baseDescriptor.Attached,
                baseDescriptor.DataAddress,
                baseDescriptor.IsDma,
                baseDescriptor.ManualDataA,
                baseDescriptor.ManualDataB);
        }

        private void StopManualSpriteFrameCommands(int spriteIndex, long cycle = long.MinValue)
        {
            if (!_captureSpriteFrameCommands && !_advancingLiveDma)
            {
                return;
            }

            var row = GetCurrentManualSpriteCommandRow(_sprites[spriteIndex], cycle);

            for (var i = _spriteFrameCommands.Count - 1; i >= 0; i--)
            {
                var command = _spriteFrameCommands[i];
                if (command.SpriteIndex != spriteIndex || command.Descriptor.IsDma)
                {
                    continue;
                }

                if (command.Descriptor.YStop <= row)
                {
                    continue;
                }

                var yStop = Math.Max(command.Descriptor.YStart, row);
                _spriteFrameCommands[i] = new SpriteFrameCommand(
                    command.SpriteIndex,
                    command.Row,
                    command.Descriptor.WithYStop(yStop));
            }

            StopTimelineManualSpriteFrameCommands(spriteIndex, row);
        }

        private void AddSpriteFrameCommand(int spriteIndex, SpriteDescriptor descriptor, long cycle = long.MinValue)
        {
            if (_spriteFrameCommands.Count >= MaxSpriteFrameCommands * _sprites.Length)
            {
                return;
            }

            var row = descriptor.IsDma
                ? (TryGetCurrentOutputRow(out var currentRow) ? currentRow : 0)
                : GetCurrentManualSpriteCommandRow(_sprites[spriteIndex], cycle);

            var command = new SpriteFrameCommand(spriteIndex, row, descriptor);
            if (_spriteFrameCommands.Count > 0 &&
                _spriteFrameCommands[_spriteFrameCommands.Count - 1].HasSameRenderingAs(command))
            {
                return;
            }

            _spriteFrameCommands.Add(command);
            RecordTimelineSpriteFrameCommand(command);
        }

        private void RecordTimelineSpriteFrameCommand(SpriteFrameCommand command)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                _liveTimelineUnsafeForFrame ||
                !_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                return;
            }

            _displayTimeline.AddSpriteFrameCommand(command);
        }

        private void StopTimelineManualSpriteFrameCommands(int spriteIndex, int row)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                _liveTimelineUnsafeForFrame ||
                !_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                return;
            }

            _displayTimeline.StopManualSpriteFrameCommands(spriteIndex, row);
        }

        private void RecordTimelineSpriteDataFetch(int row, int spriteIndex, int word, ushort value, bool granted)
        {
            if (!_advancingLiveDma ||
                !_liveFrameValid ||
                _liveTimelineUnsafeForFrame ||
                !_displayTimeline.IsValidForFrame(_liveFrameStartCycle))
            {
                return;
            }

            _displayTimeline.RecordSpriteDataFetch(row, spriteIndex, word, value, granted);
        }

        private void AppendDmaSpriteFrameCommands(
            List<SpriteFrameCommand> commands,
            int spriteIndex,
            uint pointer,
            int row)
        {
            var controlAddress = pointer;
            var controlRow = Math.Clamp(row, 0, LowResOutputHeight - 1);
            var lastVisibleStop = -1;
            for (var controlBlock = 0; controlBlock < 128; controlBlock++)
            {
                if (!TryReadSpriteWordForPresentation(controlAddress, controlRow, spriteIndex, 0, out var pos) ||
                    !TryReadSpriteWordForPresentation(AddDmaPointerOffset(controlAddress, 2), controlRow, spriteIndex, 1, out var ctl))
                {
                    return;
                }

                if ((pos | ctl) == 0)
                {
                    return;
                }

                var descriptor = CreateSpriteDescriptor(
                    pos,
                    ctl,
                    AddDmaPointerOffset(controlAddress, 4),
                    isDma: true,
                    _sprites[spriteIndex].DataA,
                    _sprites[spriteIndex].DataB);
                var rawHeight = Math.Max(0, descriptor.YStop - descriptor.YStart);
                var nextControlAddress = AddDmaPointerOffset(descriptor.DataAddress, rawHeight * 4);

                if (lastVisibleStop >= 0 && descriptor.YStart <= lastVisibleStop)
                {
                    descriptor = descriptor.WithYStart(Math.Min(LowResOutputHeight, lastVisibleStop + 1));
                }

                var height = Math.Max(0, descriptor.YStop - descriptor.YStart);
                if (height == 0)
                {
                    return;
                }

                if (descriptor.YStart >= row)
                {
                    AppendUniqueSpriteFrameCommand(commands, new SpriteFrameCommand(spriteIndex, row, descriptor));
                }

                lastVisibleStop = Math.Max(lastVisibleStop, descriptor.YStop);
                controlRow = Math.Clamp(descriptor.YStop + 1, 0, LowResOutputHeight - 1);
                controlAddress = nextControlAddress;
            }
        }

        private bool IsSpriteDmaEnabled()
        {
            return (_dmacon & (DmaconMasterEnable | DmaconSpriteEnable)) == (DmaconMasterEnable | DmaconSpriteEnable);
        }

        private static bool IsSpriteDmaEnabled(ushort dmacon)
        {
            return (dmacon & (DmaconMasterEnable | DmaconSpriteEnable)) == (DmaconMasterEnable | DmaconSpriteEnable);
        }

        private bool IsSpriteDmaChannelAvailable(int spriteIndex)
        {
            return spriteIndex < GetUsableSpriteDmaChannelCount();
        }

        private bool IsSpriteDmaSlotAvailable(int spriteIndex, int word)
        {
            if (GetAgnusBitplaneFetchPlaneCount() == 0 || !IsBitplaneDmaEnabledForRendering())
            {
                return true;
            }

            var ddfStart = GetDataFetchStartValue();
            if (!IsHighResolutionEnabled() && ddfStart <= 0x0018 && spriteIndex == 0)
            {
                return word == 0;
            }

            return IsSpriteDmaChannelAvailable(spriteIndex);
        }

        private static bool IsSpriteDmaChannelAvailable(DisplayTimelineState state, int spriteIndex)
        {
            return spriteIndex < GetUsableSpriteDmaChannelCount(state);
        }

        private static bool IsSpriteDmaSlotAvailable(DisplayTimelineState state, int spriteIndex, int word)
        {
            if (state.PlaneCount == 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return true;
            }

            if (!IsHighResolutionEnabled(state.Bplcon0) && state.DataFetchStart <= 0x0018 && spriteIndex == 0)
            {
                return word == 0;
            }

            return IsSpriteDmaChannelAvailable(state, spriteIndex);
        }

        private int GetUsableSpriteDmaChannelCount()
        {
            if (((_bplcon0 >> 12) & 0x7) == 0 || !IsBitplaneDmaEnabledForRendering())
            {
                return _sprites.Length;
            }

            var ddfStart = GetDataFetchStartValue();
            var standardStart = IsHighResolutionEnabled() ? DefaultHighResDdfStart : DefaultDdfStart;
            if (ddfStart >= standardStart)
            {
                return _sprites.Length;
            }

            if (ddfStart <= 0x0018)
            {
                return 0;
            }

            if (ddfStart <= 0x001C)
            {
                return 1;
            }

            if (ddfStart >= 0x0030)
            {
                return 7;
            }

            return Math.Clamp(((ddfStart - 0x001C) / 4) + 1, 1, 7);
        }

        private static int GetUsableSpriteDmaChannelCount(DisplayTimelineState state)
        {
            if (state.PlaneCount == 0 || !IsBitplaneDmaEnabled(state.Dmacon))
            {
                return LiveSpriteChannelCount;
            }

            var ddfStart = state.DataFetchStart;
            var standardStart = IsHighResolutionEnabled(state.Bplcon0) ? DefaultHighResDdfStart : DefaultDdfStart;
            if (ddfStart >= standardStart)
            {
                return LiveSpriteChannelCount;
            }

            if (ddfStart <= 0x0018)
            {
                return 0;
            }

            if (ddfStart <= 0x001C)
            {
                return 1;
            }

            if (ddfStart >= 0x0030)
            {
                return 7;
            }

            return Math.Clamp(((ddfStart - 0x001C) / 4) + 1, 1, 7);
        }

        private bool IsBitplaneDmaEnabledForRendering()
        {
            return !_enforceDmaForFrame || IsBitplaneDmaEnabled(_dmacon);
        }

        private bool IsLiveBitplaneDmaEnabled()
        {
            return GetAgnusBitplaneFetchPlaneCount() > 0 && IsBitplaneDmaEnabled(_dmacon);
        }

        private bool IsCopperDmaEnabled()
        {
            return !_enforceDmaForFrame ||
                (_dmacon & (DmaconMasterEnable | DmaconCopperEnable)) == (DmaconMasterEnable | DmaconCopperEnable);
        }

        private bool IsLiveCopperDmaEnabled()
        {
            return (_dmacon & (DmaconMasterEnable | DmaconCopperEnable)) == (DmaconMasterEnable | DmaconCopperEnable);
        }

        private static SpriteDescriptor CreateSpriteDescriptor(
            ushort pos,
            ushort ctl,
            uint dataAddress,
            bool isDma,
            ushort manualDataA,
            ushort manualDataB)
        {
            var hStart = ((pos & 0x00FF) << 1) | (ctl & 0x0001);
            var vStart = ((pos >> 8) & 0x00FF) | ((ctl & 0x0004) != 0 ? 0x100 : 0);
            var vStop = ((ctl >> 8) & 0x00FF) | ((ctl & 0x0002) != 0 ? 0x100 : 0);
            return new SpriteDescriptor(
                hStart - StandardSpriteHorizontalOffset,
                vStart - StandardVStart,
                vStop - StandardVStart,
                (ctl & 0x0080) != 0,
                dataAddress,
                isDma,
                manualDataA,
                manualDataB);
        }

        private void RenderSprite(Span<uint> bgra, int spriteIndex, SpriteFrameCommand command)
        {
            var sprite = command.Descriptor;
            if (!sprite.IsDma)
            {
                var yStart = Math.Max(sprite.YStart, command.Row);
                var yStop = Math.Min(sprite.YStop, LowResOutputHeight);
                for (var y = yStart; y < yStop; y++)
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, sprite.ManualDataA, sprite.ManualDataB);
                }

                return;
            }

            var address = sprite.DataAddress;
            for (var y = sprite.YStart; y < sprite.YStop; y++)
            {
                if (TryReadSpriteWordForPresentation(
                        address,
                        y,
                        spriteIndex,
                        0,
                        out var dataA,
                        recordLiveCapture: false) &&
                    TryReadSpriteWordForPresentation(
                        AddDmaPointerOffset(address, 2),
                        y,
                        spriteIndex,
                        1,
                        out var dataB,
                        recordLiveCapture: false))
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, dataA, dataB);
                }

                address = AddDmaPointerOffset(address, 4);
            }
        }

        private void RenderTimelineSprite(
            Span<uint> bgra,
            int spriteIndex,
            SpriteFrameCommand command,
            DisplayFrameTimeline timeline)
        {
            var sprite = command.Descriptor;
            if (!sprite.IsDma)
            {
                var yStart = Math.Max(sprite.YStart, command.Row);
                var yStop = Math.Min(sprite.YStop, LowResOutputHeight);
                for (var y = yStart; y < yStop; y++)
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, sprite.ManualDataA, sprite.ManualDataB);
                }

                return;
            }

            for (var y = sprite.YStart; y < sprite.YStop; y++)
            {
                if (TryReadTimelineSpriteLine(command, y, timeline, out var dataA, out var dataB))
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, dataA, dataB);
                }
            }
        }

        private void RenderAttachedSpritePair(Span<uint> bgra, int spriteIndex, SpriteFrameCommand evenCommand, SpriteFrameCommand oddCommand)
        {
            var evenSprite = evenCommand.Descriptor;
            var oddSprite = oddCommand.Descriptor;
            var yStart = Math.Min(evenSprite.YStart, oddSprite.YStart);
            var yStop = Math.Max(evenSprite.YStop, oddSprite.YStop);
            for (var y = yStart; y < yStop; y++)
            {
                var evenData = ReadSpriteLine(evenCommand, y);
                var oddData = ReadSpriteLine(oddCommand, y);
                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    evenSprite.X,
                    oddSprite.X,
                    y,
                    evenData.DataA,
                    evenData.DataB,
                    oddData.DataA,
                    oddData.DataB);
            }
        }

        private void RenderTimelineAttachedSpritePair(
            Span<uint> bgra,
            int spriteIndex,
            SpriteFrameCommand evenCommand,
            SpriteFrameCommand oddCommand,
            DisplayFrameTimeline timeline)
        {
            var evenSprite = evenCommand.Descriptor;
            var oddSprite = oddCommand.Descriptor;
            var yStart = Math.Min(evenSprite.YStart, oddSprite.YStart);
            var yStop = Math.Max(evenSprite.YStop, oddSprite.YStop);
            for (var y = yStart; y < yStop; y++)
            {
                if (!TryReadTimelineSpriteLine(evenCommand, y, timeline, out var evenDataA, out var evenDataB) ||
                    !TryReadTimelineSpriteLine(oddCommand, y, timeline, out var oddDataA, out var oddDataB))
                {
                    continue;
                }

                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    evenSprite.X,
                    oddSprite.X,
                    y,
                    evenDataA,
                    evenDataB,
                    oddDataA,
                    oddDataB);
            }
        }

        private void RenderAttachedOddSpriteWithoutEvenPartner(Span<uint> bgra, int spriteIndex, SpriteFrameCommand oddCommand)
        {
            var oddSprite = oddCommand.Descriptor;
            for (var y = oddSprite.YStart; y < oddSprite.YStop; y++)
            {
                var oddData = ReadSpriteLine(oddCommand, y);
                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    oddSprite.X,
                    oddSprite.X,
                    y,
                    0,
                    0,
                    oddData.DataA,
                    oddData.DataB);
            }
        }

        private void RenderTimelineAttachedOddSpriteWithoutEvenPartner(
            Span<uint> bgra,
            int spriteIndex,
            SpriteFrameCommand oddCommand,
            DisplayFrameTimeline timeline)
        {
            var oddSprite = oddCommand.Descriptor;
            for (var y = oddSprite.YStart; y < oddSprite.YStop; y++)
            {
                if (!TryReadTimelineSpriteLine(oddCommand, y, timeline, out var oddDataA, out var oddDataB))
                {
                    continue;
                }

                RenderAttachedSpriteLine(
                    bgra,
                    spriteIndex,
                    oddSprite.X,
                    oddSprite.X,
                    y,
                    0,
                    0,
                    oddDataA,
                    oddDataB);
            }
        }

        private (ushort DataA, ushort DataB) ReadSpriteLine(SpriteFrameCommand command, int y)
        {
            var sprite = command.Descriptor;
            if (y < Math.Max(sprite.YStart, command.Row) || y >= sprite.YStop)
            {
                return ((ushort)0, (ushort)0);
            }

            if (!sprite.IsDma)
            {
                return (sprite.ManualDataA, sprite.ManualDataB);
            }

            var address = AddDmaPointerOffset(sprite.DataAddress, (y - sprite.YStart) * 4);
            if (!TryReadSpriteWordForPresentation(
                    address,
                    y,
                    command.SpriteIndex,
                    0,
                    out var dataA,
                    recordLiveCapture: false) ||
                !TryReadSpriteWordForPresentation(
                    AddDmaPointerOffset(address, 2),
                    y,
                    command.SpriteIndex,
                    1,
                    out var dataB,
                    recordLiveCapture: false))
            {
                return ((ushort)0, (ushort)0);
            }

            return (dataA, dataB);
        }

        private static bool TryReadTimelineSpriteLine(
            SpriteFrameCommand command,
            int y,
            DisplayFrameTimeline timeline,
            out ushort dataA,
            out ushort dataB)
        {
            dataA = 0;
            dataB = 0;
            var sprite = command.Descriptor;
            if (y < Math.Max(sprite.YStart, command.Row) || y >= sprite.YStop)
            {
                return true;
            }

            if (!sprite.IsDma)
            {
                dataA = sprite.ManualDataA;
                dataB = sprite.ManualDataB;
                return true;
            }

            var statusA = timeline.GetSpriteFetchStatus(y, command.SpriteIndex, 0);
            var statusB = timeline.GetSpriteFetchStatus(y, command.SpriteIndex, 1);
            if (statusA == TimelineFetchStatus.NotAttempted || statusB == TimelineFetchStatus.NotAttempted)
            {
                return false;
            }

            var hasPriorDatb = HasPriorTimelineSpriteDatb(timeline, command, y, command.SpriteIndex);
            if (statusB == TimelineFetchStatus.Denied && !hasPriorDatb)
            {
                return true;
            }

            dataA = statusA == TimelineFetchStatus.Granted
                ? timeline.GetSpriteWord(y, command.SpriteIndex, 0)
                : (ushort)0;
            dataB = statusB == TimelineFetchStatus.Granted ||
                (statusB == TimelineFetchStatus.Denied && hasPriorDatb)
                ? timeline.GetSpriteWord(y, command.SpriteIndex, 1)
                : (ushort)0;
            return true;
        }

        private void RenderSpriteLine(Span<uint> bgra, int spriteIndex, int x, int y, ushort dataA, ushort dataB)
        {
            if (y < 0 || y >= LowResOutputHeight)
            {
                return;
            }

            for (var bit = 15; bit >= 0; bit--)
            {
                var pixel = (((dataB >> bit) & 1) << 1) | ((dataA >> bit) & 1);
                if (pixel == 0)
                {
                    continue;
                }

                var px = x + (15 - bit);
                if (px < 0 || px >= AmigaConstants.PalLowResWidth)
                {
                    continue;
                }

                if (!ShouldSpritePixelDrawOverPlayfields(spriteIndex, px, y))
                {
                    continue;
                }

                WriteSpritePixel(bgra, px, y, ConvertSpriteColorIndex(GetSpriteColorIndex(spriteIndex, pixel), px, y));
            }
        }

        private void RenderAttachedSpriteLine(
            Span<uint> bgra,
            int spriteIndex,
            int evenX,
            int oddX,
            int y,
            ushort evenDataA,
            ushort evenDataB,
            ushort oddDataA,
            ushort oddDataB)
        {
            if (y < 0 || y >= LowResOutputHeight)
            {
                return;
            }

            var xStart = Math.Min(evenX, oddX);
            var xStop = Math.Max(evenX, oddX) + 16;
            for (var px = xStart; px < xStop; px++)
            {
                var evenPixel = GetSpritePixelAt(evenDataA, evenDataB, px - evenX);
                var oddPixel = GetSpritePixelAt(oddDataA, oddDataB, px - oddX);
                var pixel = (oddPixel << 2) | evenPixel;
                if (pixel == 0)
                {
                    continue;
                }

                if (px < 0 || px >= AmigaConstants.PalLowResWidth)
                {
                    continue;
                }

                if (!ShouldSpritePixelDrawOverPlayfields(spriteIndex, px, y))
                {
                    continue;
                }

                WriteSpritePixel(bgra, px, y, ConvertSpriteColorIndex(16 + pixel, px, y));
            }
        }

        private static int GetSpritePixelAt(ushort dataA, ushort dataB, int offset)
        {
            if ((uint)offset >= 16)
            {
                return 0;
            }

            var bit = 15 - offset;
            return (((dataB >> bit) & 1) << 1) | ((dataA >> bit) & 1);
        }

        private void WriteSpritePixel(Span<uint> bgra, int x, int y, uint pixel)
        {
            _lastSpriteNonZeroPixels++;
            _lastSpriteMinX = Math.Min(_lastSpriteMinX, x);
            _lastSpriteMinY = Math.Min(_lastSpriteMinY, y);
            _lastSpriteMaxX = Math.Max(_lastSpriteMaxX, x);
            _lastSpriteMaxY = Math.Max(_lastSpriteMaxY, y);
            WriteLowResolutionOutputPixel(bgra, x, y, pixel);
        }

        private bool ShouldSpritePixelDrawOverPlayfields(int spriteIndex, int x, int y)
        {
            if ((uint)x >= (uint)AmigaConstants.PalLowResWidth || (uint)y >= (uint)LowResOutputHeight)
            {
                return false;
            }

            if (!IsSpritePixelInsideDisplayWindow(x, y))
            {
                return false;
            }

            if (!IsSpritePastDeniseOutputEnable(x, y))
            {
                return false;
            }

            var mask = _playfieldPriorityMasks[(y * AmigaConstants.PalLowResWidth) + x];
            if (mask == 0)
            {
                return true;
            }

            var bplcon2 = GetSpritePriorityRegister(x, y);
            var spriteGroup = spriteIndex / 2;
            if ((mask & NormalPlayfieldPriorityMask) != 0)
            {
                return spriteGroup < GetNormalPlayfieldPriorityPlacement(bplcon2);
            }

            if ((mask & Playfield1PriorityMask) != 0 &&
                spriteGroup >= GetPlayfield1PriorityPlacement(bplcon2))
            {
                return false;
            }

            if ((mask & Playfield2PriorityMask) != 0 &&
                spriteGroup >= GetPlayfield2PriorityPlacement(bplcon2))
            {
                return false;
            }

            return true;
        }

        private bool IsSpritePixelInsideDisplayWindow(int x, int y)
        {
            var window = GetSpriteDisplayWindow(x, y);
            return x >= window.X &&
                x < window.X + window.Width &&
                y >= window.Y &&
                y < window.Y + window.Height;
        }

        private bool IsSpritePastDeniseOutputEnable(int x, int y)
        {
            if (GetRequestedBitplaneCount() <= 0)
            {
                return true;
            }

            var dataFetchStartX = GetDataFetchStartX(GetSpriteDisplayWindow(x, y));
            if (x >= dataFetchStartX)
            {
                return true;
            }

            for (var i = _bitplaneDataSpans.Count - 1; i >= 0; i--)
            {
                var span = _bitplaneDataSpans[i];
                if (span.Row == y && span.XStart <= x)
                {
                    return true;
                }
            }

            return false;
        }

        private DisplayWindow GetSpriteDisplayWindow(int x, int y)
        {
            for (var i = _paletteFrameSpans.Count - 1; i >= 0; i--)
            {
                var span = _paletteFrameSpans[i];
                if (span.Contains(x, y))
                {
                    return span.Window;
                }
            }

            return GetDisplayWindow();
        }

        private ushort GetSpritePriorityRegister(int x, int y)
        {
            for (var i = _paletteFrameSpans.Count - 1; i >= 0; i--)
            {
                var span = _paletteFrameSpans[i];
                if (span.Contains(x, y))
                {
                    return span.Bplcon2;
                }
            }

            return _bplcon2;
        }

        private static int GetPlayfield1PriorityPlacement(ushort bplcon2)
        {
            return Math.Min(bplcon2 & 0x0007, 4);
        }

        private static int GetPlayfield2PriorityPlacement(ushort bplcon2)
        {
            return Math.Min((bplcon2 >> 3) & 0x0007, 4);
        }

        private static int GetNormalPlayfieldPriorityPlacement(ushort bplcon2)
        {
            return GetPlayfield2PriorityPlacement(bplcon2);
        }

        private static int GetSpriteColorIndex(int spriteIndex, int pixel)
        {
            return 16 + ((spriteIndex / 2) * 4) + pixel;
        }

        private uint ConvertSpriteColorIndex(int colorIndex, int x, int y)
        {
            for (var i = _paletteFrameSpans.Count - 1; i >= 0; i--)
            {
                var span = _paletteFrameSpans[i];
                if (span.Contains(x, y) && (uint)colorIndex < PaletteColorCount)
                {
                    return _paletteFrameSpanColors[span.ColorOffset + colorIndex];
                }
            }

            return ConvertColorIndex(colorIndex);
        }

        private static uint ConvertColor(ushort amigaColor)
        {
            var r = (uint)(((amigaColor >> 8) & 0x0F) * 17);
            var g = (uint)(((amigaColor >> 4) & 0x0F) * 17);
            var b = (uint)((amigaColor & 0x0F) * 17);
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private static uint AveragePixels(uint left, uint right)
        {
            if (left == right)
            {
                return left;
            }

            var r = (((left >> 16) & 0xFF) + ((right >> 16) & 0xFF)) >> 1;
            var g = (((left >> 8) & 0xFF) + ((right >> 8) & 0xFF)) >> 1;
            var b = ((left & 0xFF) + (right & 0xFF)) >> 1;
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private uint ConvertColorIndex(int colorIndex)
        {
            if ((uint)colorIndex < (uint)_convertedColors.Length)
            {
                return _convertedColors[colorIndex];
            }

            var baseColor = _colors[colorIndex & 0x1F];
            var r = (uint)((((baseColor >> 8) & 0x0F) * 17) / 2);
            var g = (uint)((((baseColor >> 4) & 0x0F) * 17) / 2);
            var b = (uint)(((baseColor & 0x0F) * 17) / 2);
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private void UpdateConvertedPalette()
        {
            for (var colorIndex = 0; colorIndex < _colors.Length; colorIndex++)
            {
                UpdateConvertedColor(colorIndex);
            }
        }

        private void UpdateConvertedColor(int colorIndex)
        {
            var color = _colors[colorIndex];
            _convertedColors[colorIndex] = ConvertColor(color);
            var r = (uint)((((color >> 8) & 0x0F) * 17) / 2);
            var g = (uint)((((color >> 4) & 0x0F) * 17) / 2);
            var b = (uint)(((color & 0x0F) * 17) / 2);
            _convertedColors[32 + colorIndex] = 0xFF00_0000u | (r << 16) | (g << 8) | b;
        }

        private bool IsRenderingHighResolutionWidth()
        {
            return _renderWidth >= AmigaConstants.PalHighResWidth;
        }

        private bool IsRenderingHighResolutionHeight()
        {
            return _renderHeight >= AmigaConstants.PalHighResHeight;
        }

        private OutputRows EnumerateOutputRows(int y)
        {
            if (!IsRenderingHighResolutionHeight())
            {
                return new OutputRows(y, y);
            }

            var first = (y * 2) + (InterlaceEnabled ? _renderInterlaceField : 0);
            var second = InterlaceEnabled ? first : first + 1;
            return new OutputRows(first, second);
        }

        private void WriteLowResolutionOutputPixel(Span<uint> bgra, int x, int y, uint pixel)
        {
            WriteLowResolutionOutputPixel(
                bgra,
                x,
                y,
                pixel,
                IsRenderingHighResolutionWidth(),
                IsRenderingHighResolutionHeight(),
                InterlaceEnabled,
                _renderInterlaceField);
        }

        private void WriteLowResolutionOutputPixel(
            Span<uint> bgra,
            int x,
            int y,
            uint pixel,
            bool highResolutionWidth,
            bool highResolutionHeight,
            bool interlace,
            int interlaceField)
        {
            if (!highResolutionHeight)
            {
                if (highResolutionWidth)
                {
                    var offset = (y * _renderWidth) + (x * 2);
                    bgra[offset] = pixel;
                    bgra[offset + 1] = pixel;
                }
                else
                {
                    bgra[(y * _renderWidth) + x] = pixel;
                }

                return;
            }

            var firstOutputY = (y * 2) + (interlace ? interlaceField : 0);
            WriteLowResolutionOutputPixelRow(bgra, x, firstOutputY, pixel, highResolutionWidth);
            if (!interlace)
            {
                WriteLowResolutionOutputPixelRow(bgra, x, firstOutputY + 1, pixel, highResolutionWidth);
            }
        }

        private void WriteHighResolutionOutputPixelPair(Span<uint> bgra, int x, int y, uint left, uint right)
        {
            WriteHighResolutionOutputPixelPair(
                bgra,
                x,
                y,
                left,
                right,
                IsRenderingHighResolutionWidth(),
                IsRenderingHighResolutionHeight(),
                InterlaceEnabled,
                _renderInterlaceField);
        }

        private void WriteHighResolutionOutputPixelPair(
            Span<uint> bgra,
            int x,
            int y,
            uint left,
            uint right,
            bool highResolutionWidth,
            bool highResolutionHeight,
            bool interlace,
            int interlaceField)
        {
            if (!highResolutionHeight)
            {
                if (highResolutionWidth)
                {
                    var offset = (y * _renderWidth) + (x * 2);
                    bgra[offset] = left;
                    bgra[offset + 1] = right;
                }
                else
                {
                    bgra[(y * _renderWidth) + x] = AveragePixels(left, right);
                }

                return;
            }

            var firstOutputY = (y * 2) + (interlace ? interlaceField : 0);
            WriteHighResolutionOutputPixelRow(bgra, x, firstOutputY, left, right, highResolutionWidth);
            if (!interlace)
            {
                WriteHighResolutionOutputPixelRow(bgra, x, firstOutputY + 1, left, right, highResolutionWidth);
            }
        }

        private void WriteLowResolutionOutputPixelRow(Span<uint> bgra, int x, int outputY, uint pixel, bool highResolutionWidth)
        {
            if (highResolutionWidth)
            {
                var offset = (outputY * _renderWidth) + (x * 2);
                bgra[offset] = pixel;
                bgra[offset + 1] = pixel;
            }
            else
            {
                bgra[(outputY * _renderWidth) + x] = pixel;
            }
        }

        private void WriteHighResolutionOutputPixelRow(
            Span<uint> bgra,
            int x,
            int outputY,
            uint left,
            uint right,
            bool highResolutionWidth)
        {
            if (highResolutionWidth)
            {
                var offset = (outputY * _renderWidth) + (x * 2);
                bgra[offset] = left;
                bgra[offset + 1] = right;
            }
            else
            {
                bgra[(outputY * _renderWidth) + x] = AveragePixels(left, right);
            }
        }

        private uint ConvertHamPixel(int colorIndex, ref ushort previousColor)
        {
            var data = (ushort)(colorIndex & 0x0F);
            switch ((colorIndex >> 4) & 0x03)
            {
                case 0:
                    previousColor = _colors[data];
                    break;
                case 1:
                    previousColor = (ushort)((previousColor & 0x0FF0) | data);
                    break;
                case 2:
                    previousColor = (ushort)((previousColor & 0x00FF) | (data << 8));
                    break;
                case 3:
                    previousColor = (ushort)((previousColor & 0x0F0F) | (data << 4));
                    break;
            }

            return ConvertColor(previousColor);
        }

        private SavedDisplayState SaveDisplayState()
        {
            var saved = _savedDisplayState;
            CaptureDisplayState(saved);
            return saved;
        }

        private void CaptureDisplayState(SavedDisplayState saved)
        {
            saved.CopperListPointer = _copperListPointer;
            saved.CopperListPointer2 = _copperListPointer2;
            saved.Copcon = _copcon;
            saved.DiwStart = _diwStart;
            saved.DiwStop = _diwStop;
            saved.DdfStart = _ddfStart;
            saved.DdfStop = _ddfStop;
            saved.Bplcon0 = _bplcon0;
            saved.Bplcon1 = _bplcon1;
            saved.Bplcon2 = _bplcon2;
            saved.Dmacon = _dmacon;
            saved.Bpl1Mod = _bpl1mod;
            saved.Bpl2Mod = _bpl2mod;
            Array.Copy(_colors, saved.Colors, _colors.Length);
            Array.Copy(_convertedColors, saved.ConvertedColors, _convertedColors.Length);
            Array.Copy(_bitplanePointers, saved.BitplanePointers, _bitplanePointers.Length);
            Array.Copy(_bitplaneBaseRows, saved.BitplaneBaseRows, _bitplaneBaseRows.Length);
            Array.Copy(_bitplaneDataRegisters, saved.BitplaneDataRegisters, _bitplaneDataRegisters.Length);
            Array.Copy(_bitplaneDataRegisterWritten, saved.BitplaneDataRegisterWritten, _bitplaneDataRegisterWritten.Length);
        }

        private void RestoreDisplayState(SavedDisplayState saved)
        {
            _copperListPointer = saved.CopperListPointer;
            _copperListPointer2 = saved.CopperListPointer2;
            _copcon = saved.Copcon;
            _diwStart = saved.DiwStart;
            _diwStop = saved.DiwStop;
            _ddfStart = saved.DdfStart;
            _ddfStop = saved.DdfStop;
            _bplcon0 = saved.Bplcon0;
            _bplcon1 = saved.Bplcon1;
            _bplcon2 = saved.Bplcon2;
            _dmacon = saved.Dmacon;
            _bpl1mod = saved.Bpl1Mod;
            _bpl2mod = saved.Bpl2Mod;
            Array.Copy(saved.Colors, _colors, _colors.Length);
            Array.Copy(saved.ConvertedColors, _convertedColors, _convertedColors.Length);
            Array.Copy(saved.BitplanePointers, _bitplanePointers, _bitplanePointers.Length);
            Array.Copy(saved.BitplaneBaseRows, _bitplaneBaseRows, _bitplaneBaseRows.Length);
            Array.Copy(saved.BitplaneDataRegisters, _bitplaneDataRegisters, _bitplaneDataRegisters.Length);
            Array.Copy(saved.BitplaneDataRegisterWritten, _bitplaneDataRegisterWritten, _bitplaneDataRegisterWritten.Length);
        }

        private void ApplyLiveLineStateForRendering(LiveLineState state)
        {
            _diwStart = state.DiwStart;
            _diwStop = state.DiwStop;
            _ddfStart = state.DdfStart;
            _ddfStop = state.DdfStop;
            _bplcon0 = state.Bplcon0;
            _bplcon1 = state.Bplcon1;
            _bplcon2 = state.Bplcon2;
            _dmacon = state.Dmacon;
            _bpl1mod = state.Bpl1Mod;
            _bpl2mod = state.Bpl2Mod;
            if (_lastAppliedLivePaletteSnapshotIndex != state.PaletteSnapshotIndex)
            {
                var paletteIndex = Math.Clamp(state.PaletteSnapshotIndex, 0, Math.Max(0, _livePaletteSnapshotCount - 1));
                Array.Copy(_livePaletteSnapshotColors, paletteIndex * _colors.Length, _colors, 0, _colors.Length);
                Array.Copy(_livePaletteSnapshotConvertedColors, paletteIndex * PaletteColorCount, _convertedColors, 0, PaletteColorCount);
                _lastAppliedLivePaletteSnapshotIndex = state.PaletteSnapshotIndex;
            }

            Array.Copy(state.BitplanePointers, _bitplanePointers, _bitplanePointers.Length);
            Array.Copy(state.BitplaneBaseRows, _bitplaneBaseRows, _bitplaneBaseRows.Length);
        }

        private readonly struct DualPlayfieldPixel
        {
            public DualPlayfieldPixel(int colorIndex, byte priorityMask)
            {
                ColorIndex = colorIndex;
                PriorityMask = priorityMask;
            }

            public int ColorIndex { get; }

            public byte PriorityMask { get; }
        }

        private readonly struct OutputRows
        {
            private readonly int _first;
            private readonly int _second;

            public OutputRows(int first, int second)
            {
                _first = first;
                _second = second;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(_first, _second);
            }

            public struct Enumerator
            {
                private readonly int _first;
                private readonly int _second;
                private int _index;

                public Enumerator(int first, int second)
                {
                    _first = first;
                    _second = second;
                    _index = -1;
                    Current = 0;
                }

                public int Current { get; private set; }

                public bool MoveNext()
                {
                    _index++;
                    if (_index == 0)
                    {
                        Current = _first;
                        return true;
                    }

                    if (_index == 1 && _second != _first)
                    {
                        Current = _second;
                        return true;
                    }

                    return false;
                }
            }
        }

        private readonly struct PaletteFrameSpan
        {
            public PaletteFrameSpan(int row, int xStart, int xStop, int colorOffset, ushort bplcon0, ushort bplcon2, DisplayWindow window)
            {
                Row = row;
                XStart = xStart;
                XStop = xStop;
                ColorOffset = colorOffset;
                Bplcon0 = bplcon0;
                Bplcon2 = bplcon2;
                Window = window;
            }

            public int Row { get; }

            public int XStart { get; }

            public int XStop { get; }

            public int ColorOffset { get; }

            public ushort Bplcon0 { get; }

            public ushort Bplcon2 { get; }

            public DisplayWindow Window { get; }

            public bool Contains(int x, int y)
            {
                return y == Row && x >= XStart && x < XStop;
            }
        }

        private readonly struct BitplaneDataSpan
        {
            private readonly ushort _word0;
            private readonly ushort _word1;
            private readonly ushort _word2;
            private readonly ushort _word3;
            private readonly ushort _word4;
            private readonly ushort _word5;

            public BitplaneDataSpan(
                int row,
                int xStart,
                int xStop,
                ushort word0,
                ushort word1,
                ushort word2,
                ushort word3,
                ushort word4,
                ushort word5)
            {
                Row = row;
                XStart = xStart;
                XStop = xStop;
                _word0 = word0;
                _word1 = word1;
                _word2 = word2;
                _word3 = word3;
                _word4 = word4;
                _word5 = word5;
            }

            public int Row { get; }

            public int XStart { get; }

            public int XStop { get; }

            public bool Contains(int x, int y)
            {
                return y == Row && x >= XStart && x < XStop;
            }

            public ushort GetWord(int plane)
            {
                return plane switch
                {
                    0 => _word0,
                    1 => _word1,
                    2 => _word2,
                    3 => _word3,
                    4 => _word4,
                    5 => _word5,
                    _ => 0
                };
            }
        }

        private readonly struct DisplayWindow
        {
            public DisplayWindow(int x, int y, int width, int height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public int X { get; }

            public int Y { get; }

            public int Width { get; }

            public int Height { get; }

        }

        private struct CopperPresentationState
        {
            public CopperPresentationState(uint pc, long cycle, bool pendingStart = false)
            {
                Pc = pc;
                Cycle = cycle;
                Stopped = false;
                Waiting = false;
                PendingStart = pendingStart;
                SuppressNextMove = false;
                PendingMove = false;
                PendingMoveRegister = 0;
                PendingMoveValue = 0;
                PendingMoveCycle = 0;
                PendingMoveStopCycle = 0;
                PendingMoveSuppress = false;
                PendingSkip = false;
                PendingSkipFirst = 0;
                PendingSkipSecond = 0;
                PendingSkipCycle = 0;
                WaitFirst = 0;
                WaitSecond = 0;
            }

            public uint Pc;

            public long Cycle;

            public bool Stopped;

            public bool Waiting;

            public bool PendingStart;

            public bool SuppressNextMove;

            public bool PendingMove;

            public ushort PendingMoveRegister;

            public ushort PendingMoveValue;

            public long PendingMoveCycle;

            public long PendingMoveStopCycle;

            public bool PendingMoveSuppress;

            public bool PendingSkip;

            public ushort PendingSkipFirst;

            public ushort PendingSkipSecond;

            public long PendingSkipCycle;

            public ushort WaitFirst;

            public ushort WaitSecond;

            public void Wait(ushort first, ushort second)
            {
                Waiting = true;
                WaitFirst = first;
                WaitSecond = second;
            }

            public void JumpTo(uint pc, long cycle)
            {
                Pc = pc;
                Cycle = cycle;
                Stopped = false;
                Waiting = false;
                PendingStart = false;
                SuppressNextMove = false;
                PendingMove = false;
                PendingMoveRegister = 0;
                PendingMoveValue = 0;
                PendingMoveCycle = 0;
                PendingMoveStopCycle = 0;
                PendingMoveSuppress = false;
                PendingSkip = false;
                PendingSkipFirst = 0;
                PendingSkipSecond = 0;
                PendingSkipCycle = 0;
            }

            public void StartFrom(uint pc)
            {
                JumpTo(pc, Cycle);
            }
        }

        private readonly struct CopperInstructionLatch
        {
            public CopperInstructionLatch(
                ushort first,
                AmigaBusAccessResult firstAccess,
                ushort second,
                AmigaBusAccessResult secondAccess)
            {
                First = first;
                Second = second;
                DataCycle = secondAccess.GrantedCycle;
                MoveStopCycle = Math.Max(
                    secondAccess.CompletedCycle,
                    firstAccess.RequestedCycle + CopperHpToCpuCycles(CopperMoveHpUnits));
                ControlStopCycle = Math.Max(
                    secondAccess.CompletedCycle,
                    firstAccess.RequestedCycle + CopperHpToCpuCycles(CopperSkipHpUnits));
            }

            public ushort First { get; }

            public ushort Second { get; }

            public long DataCycle { get; }

            public long MoveStopCycle { get; }

            public long ControlStopCycle { get; }

            public bool IsEnd => First == 0xFFFF && Second == 0xFFFE;

            public bool IsMove => (First & 1) == 0;

            public bool IsWait => (Second & 1) == 0;

            public ushort MoveRegister => (ushort)(First & 0x01FE);
        }

        private readonly struct PendingCustomWrite
        {
            public PendingCustomWrite(long cycle, ushort offset, ushort value, bool isCopper = false)
            {
                Cycle = cycle;
                Offset = offset;
                Value = value;
                IsCopper = isCopper;
            }

            public long Cycle { get; }

            public ushort Offset { get; }

            public ushort Value { get; }

            public bool IsCopper { get; }
        }

        private readonly struct SpriteDescriptor
        {
            public SpriteDescriptor(
                int x,
                int yStart,
                int yStop,
                bool attached,
                uint dataAddress,
                bool isDma,
                ushort manualDataA,
                ushort manualDataB)
            {
                X = x;
                YStart = yStart;
                YStop = yStop;
                Attached = attached;
                DataAddress = dataAddress;
                IsDma = isDma;
                ManualDataA = manualDataA;
                ManualDataB = manualDataB;
            }

            public int X { get; }

            public int YStart { get; }

            public int YStop { get; }

            public bool Attached { get; }

            public uint DataAddress { get; }

            public bool IsDma { get; }

            public ushort ManualDataA { get; }

            public ushort ManualDataB { get; }

            public bool HasSameRenderingAs(SpriteDescriptor other)
            {
                return X == other.X &&
                    YStart == other.YStart &&
                    YStop == other.YStop &&
                    Attached == other.Attached &&
                    DataAddress == other.DataAddress &&
                    IsDma == other.IsDma &&
                    (IsDma ||
                        (ManualDataA == other.ManualDataA &&
                            ManualDataB == other.ManualDataB));
            }

            public SpriteDescriptor WithYStop(int yStop)
            {
                return new SpriteDescriptor(X, YStart, yStop, Attached, DataAddress, IsDma, ManualDataA, ManualDataB);
            }

            public SpriteDescriptor WithYStart(int yStart)
            {
                return new SpriteDescriptor(X, yStart, YStop, Attached, DataAddress, IsDma, ManualDataA, ManualDataB);
            }
        }

        private readonly struct SpriteFrameCommand
        {
            public SpriteFrameCommand(int spriteIndex, int row, SpriteDescriptor descriptor)
            {
                SpriteIndex = spriteIndex;
                Row = row;
                Descriptor = descriptor;
            }

            public int SpriteIndex { get; }

            public int Row { get; }

            public SpriteDescriptor Descriptor { get; }

            public bool HasSameRenderingAs(SpriteFrameCommand other)
            {
                return SpriteIndex == other.SpriteIndex &&
                    Row == other.Row &&
                    Descriptor.HasSameRenderingAs(other.Descriptor);
            }
        }

        private readonly struct BitplaneDmaReadLatch
        {
            public BitplaneDmaReadLatch(int row, int plane, int word, ushort value, bool granted, long grantedCycle)
            {
                Row = row;
                Plane = plane;
                Word = word;
                Value = value;
                Granted = granted;
                GrantedCycle = grantedCycle;
                HasValue = true;
            }

            public static BitplaneDmaReadLatch Denied(int row, int plane, int word, long grantedCycle)
                => new BitplaneDmaReadLatch(row, plane, word, 0, granted: false, grantedCycle);

            public int Row { get; }

            public int Plane { get; }

            public int Word { get; }

            public ushort Value { get; }

            public bool Granted { get; }

            public long GrantedCycle { get; }

            public bool HasValue { get; }
        }

        private readonly struct SpriteDmaReadLatch
        {
            public SpriteDmaReadLatch(int row, int spriteIndex, int word, ushort value, bool granted, long grantedCycle)
            {
                Row = row;
                SpriteIndex = spriteIndex;
                Word = word;
                Value = value;
                Granted = granted;
                GrantedCycle = grantedCycle;
                HasValue = true;
            }

            public static SpriteDmaReadLatch Denied(int row, int spriteIndex, int word, long grantedCycle)
                => new SpriteDmaReadLatch(row, spriteIndex, word, 0, granted: false, grantedCycle);

            public int Row { get; }

            public int SpriteIndex { get; }

            public int Word { get; }

            public ushort Value { get; }

            public bool Granted { get; }

            public long GrantedCycle { get; }

            public bool HasValue { get; }
        }

        private sealed class LiveLineState
        {
            public int Generation;
            public long LineStartCycle;
            public ushort Bplcon0;
            public ushort Bplcon1;
            public ushort Bplcon2;
            public ushort DiwStart;
            public ushort DiwStop;
            public bool DisplayWindowVerticallyOpen;
            public ushort DdfStart;
            public ushort DdfStop;
            public ushort Dmacon;
            public short Bpl1Mod;
            public short Bpl2Mod;
            public int PlaneCount;
            public int DecodePlaneCount;
            public int FetchWords;
            public int DataFetchStart;
            public int FetchSlotStride;
            public int PaletteSnapshotIndex;
            public byte PlaneHasRowMask;
            public readonly uint[] BitplanePointers = new uint[6];
            public readonly int[] BitplaneBaseRows = new int[6];
            public readonly uint[] BitplaneRowAddresses = new uint[6];
            public readonly ushort[] BitplaneDataRegisters = new ushort[6];
        }

        private sealed class DisplayFrameTimeline
        {
            private readonly DisplayLineTimeline[] _lines = new DisplayLineTimeline[LowResOutputHeight];
            private readonly List<DisplayTimelineState> _states = new List<DisplayTimelineState>(1024);
            private readonly List<SpriteFrameCommand> _spriteFrameCommands = new List<SpriteFrameCommand>(MaxSpriteFrameCommands * 8);
            private readonly List<BitplaneDataSpan> _bitplaneDataSpans = new List<BitplaneDataSpan>(MaxBitplaneDataSpans);
            private readonly Dictionary<PlanarChunkKey, PlanarChunkDecoded> _planarChunkCache = new Dictionary<PlanarChunkKey, PlanarChunkDecoded>(4096);
            private long _frameStartCycle;
            private int _generation = 1;
            private int _stateCount;

            public DisplayFrameTimeline()
            {
                for (var i = 0; i < _lines.Length; i++)
                {
                    _lines[i] = new DisplayLineTimeline();
                }
            }

            public int SegmentCount { get; private set; }

            public int SpriteCommandCount => _spriteFrameCommands.Count;

            public int PlanarChunkCacheHits { get; private set; }

            public int PlanarChunkCacheMisses { get; private set; }

            public int SpriteDeniedFetchCount { get; private set; }

            public int RecalculateSpriteDeniedFetchCount()
            {
                var count = 0;
                for (var row = 0; row < _lines.Length; row++)
                {
                    var line = _lines[row];
                    if (line.Generation != _generation || !line.Valid)
                    {
                        continue;
                    }

                    for (var spriteIndex = 0; spriteIndex < LiveSpriteChannelCount; spriteIndex++)
                    {
                        var denied = line.SpriteDeniedMasks[spriteIndex] & line.SpriteFetchMasks[spriteIndex];
                        count += BitOperations.PopCount((uint)denied);
                    }
                }

                SpriteDeniedFetchCount = count;
                return count;
            }

            public void Reset(long frameStartCycle)
            {
                _frameStartCycle = frameStartCycle;
                SegmentCount = 0;
                PlanarChunkCacheHits = 0;
                PlanarChunkCacheMisses = 0;
                SpriteDeniedFetchCount = 0;
                _stateCount = 0;
                _spriteFrameCommands.Clear();
                _bitplaneDataSpans.Clear();
                _planarChunkCache.Clear();
                _generation++;
                if (_generation != int.MaxValue)
                {
                    return;
                }

                _generation = 1;
                for (var i = 0; i < _lines.Length; i++)
                {
                    _lines[i].Clear();
                }
            }

            public bool IsValidForFrame(long frameStartCycle)
            {
                return _frameStartCycle == frameStartCycle;
            }

            public void InvalidateFromRow(int row)
            {
                row = Math.Clamp(row, 0, LowResOutputHeight);
                for (var i = row; i < _lines.Length; i++)
                {
                    var line = _lines[i];
                    if (line.Generation != _generation)
                    {
                        continue;
                    }

                    SegmentCount -= line.SegmentCount;
                    line.Clear();
                }

                for (var i = _spriteFrameCommands.Count - 1; i >= 0; i--)
                {
                    if (_spriteFrameCommands[i].Row >= row)
                    {
                        _spriteFrameCommands.RemoveAt(i);
                    }
                }

                for (var i = _bitplaneDataSpans.Count - 1; i >= 0; i--)
                {
                    if (_bitplaneDataSpans[i].Row >= row)
                    {
                        _bitplaneDataSpans.RemoveAt(i);
                    }
                }

                _planarChunkCache.Clear();
            }

            public int CoalesceEquivalentSegments()
            {
                var removed = 0;
                for (var row = 0; row < _lines.Length; row++)
                {
                    var line = _lines[row];
                    if (line.Generation != _generation || !line.Valid || line.SegmentCount <= 1)
                    {
                        continue;
                    }

                    var lineRemoved = 0;
                    var write = 0;
                    for (var read = 1; read < line.SegmentCount; read++)
                    {
                        var previous = line.Segments[write];
                        var current = line.Segments[read];
                        if (previous.XStop == current.XStart &&
                            AreTimelineStatesOutputEquivalent(GetState(previous.StateIndex), GetState(current.StateIndex)))
                        {
                            line.Segments[write] = new DisplayLineSegment(previous.XStart, current.XStop, previous.StateIndex);
                            lineRemoved++;
                            removed++;
                            continue;
                        }

                        write++;
                        if (write != read)
                        {
                            line.Segments[write] = current;
                        }
                    }

                    if (lineRemoved > 0 && write + 1 < line.SegmentCount)
                    {
                        line.Segments.RemoveRange(write + 1, line.SegmentCount - write - 1);
                    }
                }

                if (removed > 0)
                {
                    SegmentCount -= removed;
                    _planarChunkCache.Clear();
                }

                return removed;
            }

            private static bool AreTimelineStatesOutputEquivalent(DisplayTimelineState left, DisplayTimelineState right)
            {
                return left.PaletteSnapshotIndex == right.PaletteSnapshotIndex &&
                    left.Bplcon0 == right.Bplcon0 &&
                    left.Bplcon1 == right.Bplcon1 &&
                    left.Bplcon2 == right.Bplcon2 &&
                    left.DiwStart == right.DiwStart &&
                    left.DiwStop == right.DiwStop &&
                    left.DisplayWindowVerticallyOpen == right.DisplayWindowVerticallyOpen &&
                    left.DdfStart == right.DdfStart &&
                    left.DdfStop == right.DdfStop &&
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

            public DisplayTimelineState AddStateSnapshot()
            {
                if (_stateCount >= MaxTimelineStateSnapshots)
                {
                    if (_states.Count == 0)
                    {
                        _states.Add(new DisplayTimelineState(0));
                    }

                    return _states[Math.Max(0, Math.Min(_stateCount - 1, _states.Count - 1))];
                }

                if (_stateCount >= _states.Count)
                {
                    _states.Add(new DisplayTimelineState(_states.Count));
                }

                var state = _states[_stateCount];
                _stateCount++;
                return state;
            }

            public DisplayTimelineState GetState(int index)
            {
                return _states[Math.Clamp(index, 0, Math.Max(0, _stateCount - 1))];
            }

            public DisplayLineTimeline GetLine(int row)
            {
                return _lines[Math.Clamp(row, 0, LowResOutputHeight - 1)];
            }

            public bool HasLine(int row)
            {
                return (uint)row < (uint)_lines.Length &&
                    _lines[row].Generation == _generation &&
                    _lines[row].Valid;
            }

            public void RecordBitplaneDataSpan(BitplaneDataSpan span)
            {
                if (_bitplaneDataSpans.Count >= MaxBitplaneDataSpans)
                {
                    return;
                }

                _bitplaneDataSpans.Add(span);
                _planarChunkCache.Clear();
            }

            public void CopyBitplaneDataSpansTo(List<BitplaneDataSpan> destination)
            {
                for (var i = 0; i < _bitplaneDataSpans.Count; i++)
                {
                    destination.Add(_bitplaneDataSpans[i]);
                }
            }

            public bool TryGetBitplaneFetchLine(int row, out DisplayLineTimeline line)
            {
                if ((uint)row >= (uint)_lines.Length)
                {
                    line = _lines[0];
                    return false;
                }

                line = _lines[row];
                return line.Generation == _generation && line.Valid;
            }

            public void StartLine(int row, int stateIndex)
            {
                if ((uint)row >= (uint)_lines.Length)
                {
                    return;
                }

                var line = _lines[row];
                if (line.Generation == _generation)
                {
                    SegmentCount -= line.SegmentCount;
                }

                line.Clear();
                line.Generation = _generation;
                line.Valid = true;
                line.Segments.Add(new DisplayLineSegment(0, AmigaConstants.PalLowResWidth, stateIndex));
                SegmentCount++;
            }

            public void RecordDisplayChange(
                int row,
                int x,
                int stateIndex,
                bool unsafeForTimelineRender,
                ushort offset,
                bool isCopper)
            {
                if ((uint)row >= (uint)_lines.Length)
                {
                    return;
                }

                var line = _lines[row];
                if (line.Generation != _generation || !line.Valid || line.SegmentCount <= 0)
                {
                    return;
                }

                if (unsafeForTimelineRender)
                {
                    line.UnsafeForTimelineRender = true;
                    line.UnsafeOffset = (ushort)(offset & 0x01FE);
                    line.UnsafeIsCopper = isCopper;
                }

                x = Math.Clamp(x, 0, AmigaConstants.PalLowResWidth);
                if (x >= AmigaConstants.PalLowResWidth)
                {
                    return;
                }

                if (x <= 0)
                {
                    SegmentCount -= line.SegmentCount;
                    line.Segments.Clear();
                    line.Segments.Add(new DisplayLineSegment(0, AmigaConstants.PalLowResWidth, stateIndex));
                    SegmentCount++;
                    _planarChunkCache.Clear();
                    return;
                }

                var insertIndex = line.SegmentCount;
                for (var i = 0; i < line.SegmentCount; i++)
                {
                    var segment = line.Segments[i];
                    if (x >= segment.XStart && x < segment.XStop)
                    {
                        insertIndex = i + 1;
                        line.Segments[i] = new DisplayLineSegment(segment.XStart, x, segment.StateIndex);
                        break;
                    }

                    if (x <= segment.XStart)
                    {
                        insertIndex = i;
                        break;
                    }
                }

                if (insertIndex < line.SegmentCount)
                {
                    SegmentCount -= line.SegmentCount - insertIndex;
                    line.Segments.RemoveRange(insertIndex, line.SegmentCount - insertIndex);
                }

                line.Segments.Add(new DisplayLineSegment(x, AmigaConstants.PalLowResWidth, stateIndex));
                SegmentCount++;
                _planarChunkCache.Clear();
            }

            public void RecordBitplaneFetch(int row, int plane, int word, ushort value, bool granted)
            {
                if ((uint)row >= (uint)_lines.Length ||
                    (uint)plane >= LiveBitplanePlaneCount ||
                    (uint)word >= MaxBitplaneFetchWords)
                {
                    return;
                }

                var line = _lines[row];
                if (line.Generation != _generation || !line.Valid)
                {
                    return;
                }

                var bit = 1UL << word;
                var index = (plane * MaxBitplaneFetchWords) + word;
                line.BitplaneWords[index] = value;
                line.BitplaneFetchMasks[plane] |= bit;
                if (granted)
                {
                    line.BitplaneDeniedMasks[plane] &= ~bit;
                }
                else
                {
                    line.BitplaneDeniedMasks[plane] |= bit;
                }
            }

            public TimelineFetchStatus GetBitplaneFetchStatus(int row, int plane, int word)
            {
                if ((uint)row >= (uint)_lines.Length ||
                    (uint)plane >= LiveBitplanePlaneCount ||
                    (uint)word >= MaxBitplaneFetchWords)
                {
                    return TimelineFetchStatus.NotAttempted;
                }

                var line = _lines[row];
                if (line.Generation != _generation || !line.Valid)
                {
                    return TimelineFetchStatus.NotAttempted;
                }

                var bit = 1UL << word;
                if ((line.BitplaneFetchMasks[plane] & bit) == 0)
                {
                    return TimelineFetchStatus.NotAttempted;
                }

                return (line.BitplaneDeniedMasks[plane] & bit) != 0
                    ? TimelineFetchStatus.Denied
                    : TimelineFetchStatus.Granted;
            }

            public ushort GetBitplaneWord(int row, int plane, int word)
            {
                if ((uint)row >= (uint)_lines.Length ||
                    (uint)plane >= LiveBitplanePlaneCount ||
                    (uint)word >= MaxBitplaneFetchWords)
                {
                    return 0;
                }

                var line = _lines[row];
                if (line.Generation != _generation || !line.Valid)
                {
                    return 0;
                }

                return _lines[row].BitplaneWords[(plane * MaxBitplaneFetchWords) + word];
            }

            public void AddSpriteFrameCommand(SpriteFrameCommand command)
            {
                if (_spriteFrameCommands.Count >= MaxSpriteFrameCommands * LiveSpriteChannelCount)
                {
                    return;
                }

                for (var i = _spriteFrameCommands.Count - 1; i >= 0; i--)
                {
                    if (_spriteFrameCommands[i].HasSameRenderingAs(command))
                    {
                        return;
                    }
                }

                _spriteFrameCommands.Add(command);
            }

            public void StopManualSpriteFrameCommands(int spriteIndex, int row)
            {
                row = Math.Clamp(row, 0, LowResOutputHeight);
                for (var i = _spriteFrameCommands.Count - 1; i >= 0; i--)
                {
                    var command = _spriteFrameCommands[i];
                    if (command.SpriteIndex != spriteIndex || command.Descriptor.IsDma)
                    {
                        continue;
                    }

                    if (command.Descriptor.YStop <= row)
                    {
                        continue;
                    }

                    var yStop = Math.Max(command.Descriptor.YStart, row);
                    _spriteFrameCommands[i] = new SpriteFrameCommand(
                        command.SpriteIndex,
                        command.Row,
                        command.Descriptor.WithYStop(yStop));
                }
            }

            public void CopySpriteFrameCommands(int spriteIndex, List<SpriteFrameCommand> destination)
            {
                destination.Clear();
                for (var i = 0; i < _spriteFrameCommands.Count; i++)
                {
                    var command = _spriteFrameCommands[i];
                    if (command.SpriteIndex == spriteIndex)
                    {
                        AppendUniqueSpriteFrameCommand(destination, command);
                    }
                }
            }

            public void RecordSpriteDataFetch(int row, int spriteIndex, int word, ushort value, bool granted)
            {
                if ((uint)row >= (uint)_lines.Length ||
                    (uint)spriteIndex >= LiveSpriteChannelCount ||
                    (uint)word >= LiveSpriteWordsPerChannel)
                {
                    return;
                }

                var line = _lines[row];
                if (line.Generation != _generation || !line.Valid)
                {
                    return;
                }

                var bit = (byte)(1 << word);
                var index = (spriteIndex * LiveSpriteWordsPerChannel) + word;
                var wasDenied = (line.SpriteDeniedMasks[spriteIndex] & bit) != 0;
                if (!granted && !wasDenied)
                {
                    SpriteDeniedFetchCount++;
                }
                else if (granted && wasDenied)
                {
                    SpriteDeniedFetchCount--;
                }

                line.SpriteWords[index] = value;
                line.SpriteFetchMasks[spriteIndex] = (byte)(line.SpriteFetchMasks[spriteIndex] | bit);
                if (granted)
                {
                    line.SpriteDeniedMasks[spriteIndex] = (byte)(line.SpriteDeniedMasks[spriteIndex] & ~bit);
                }
                else
                {
                    line.SpriteDeniedMasks[spriteIndex] = (byte)(line.SpriteDeniedMasks[spriteIndex] | bit);
                }
            }

            public TimelineFetchStatus GetSpriteFetchStatus(int row, int spriteIndex, int word)
            {
                if ((uint)row >= (uint)_lines.Length ||
                    (uint)spriteIndex >= LiveSpriteChannelCount ||
                    (uint)word >= LiveSpriteWordsPerChannel)
                {
                    return TimelineFetchStatus.NotAttempted;
                }

                var line = _lines[row];
                if (line.Generation != _generation || !line.Valid)
                {
                    return TimelineFetchStatus.NotAttempted;
                }

                var bit = (byte)(1 << word);
                if ((line.SpriteFetchMasks[spriteIndex] & bit) == 0)
                {
                    return TimelineFetchStatus.NotAttempted;
                }

                return (line.SpriteDeniedMasks[spriteIndex] & bit) != 0
                    ? TimelineFetchStatus.Denied
                    : TimelineFetchStatus.Granted;
            }

            public ushort GetSpriteWord(int row, int spriteIndex, int word)
            {
                if ((uint)row >= (uint)_lines.Length ||
                    (uint)spriteIndex >= LiveSpriteChannelCount ||
                    (uint)word >= LiveSpriteWordsPerChannel)
                {
                    return 0;
                }

                var line = _lines[row];
                if (line.Generation != _generation || !line.Valid)
                {
                    return 0;
                }

                return line.SpriteWords[(spriteIndex * LiveSpriteWordsPerChannel) + word];
            }

            public bool TryGetPlanarChunk(PlanarChunkKey key, out PlanarChunkDecoded chunk)
            {
                if (_planarChunkCache.TryGetValue(key, out var cached))
                {
                    chunk = cached;
                    PlanarChunkCacheHits++;
                    return true;
                }

                chunk = default;
                PlanarChunkCacheMisses++;
                return false;
            }

            public void StorePlanarChunk(PlanarChunkKey key, PlanarChunkDecoded chunk)
            {
                _planarChunkCache[key] = chunk;
            }
        }

        private sealed class DisplayLineTimeline
        {
            public readonly List<DisplayLineSegment> Segments = new List<DisplayLineSegment>(4);
            public readonly ushort[] BitplaneWords = new ushort[LiveBitplaneWordsPerRow];
            public readonly ulong[] BitplaneFetchMasks = new ulong[LiveBitplanePlaneCount];
            public readonly ulong[] BitplaneDeniedMasks = new ulong[LiveBitplanePlaneCount];
            public readonly ushort[] SpriteWords = new ushort[LiveSpriteWordsPerRow];
            public readonly byte[] SpriteFetchMasks = new byte[LiveSpriteChannelCount];
            public readonly byte[] SpriteDeniedMasks = new byte[LiveSpriteChannelCount];
            public int Generation;
            public bool Valid;
            public bool UnsafeForTimelineRender;
            public ushort UnsafeOffset;
            public bool UnsafeIsCopper;

            public int SegmentCount => Segments.Count;

            public void Clear()
            {
                Segments.Clear();
                Valid = false;
                UnsafeForTimelineRender = false;
                UnsafeOffset = 0;
                UnsafeIsCopper = false;
                Array.Clear(BitplaneFetchMasks);
                Array.Clear(BitplaneDeniedMasks);
                Array.Clear(SpriteFetchMasks);
                Array.Clear(SpriteDeniedMasks);
            }
        }

        private readonly struct DisplayLineSegment
        {
            public DisplayLineSegment(int xStart, int xStop, int stateIndex)
            {
                XStart = xStart;
                XStop = xStop;
                StateIndex = stateIndex;
            }

            public int XStart { get; }

            public int XStop { get; }

            public int StateIndex { get; }
        }

        private sealed class DisplayTimelineState
        {
            public DisplayTimelineState(int index)
            {
                Index = index;
            }

            public int Index { get; }

            public long LineStartCycle;
            public ushort Bplcon0;
            public ushort Bplcon1;
            public ushort Bplcon2;
            public ushort DiwStart;
            public ushort DiwStop;
            public bool DisplayWindowVerticallyOpen;
            public ushort DdfStart;
            public ushort DdfStop;
            public ushort Dmacon;
            public short Bpl1Mod;
            public short Bpl2Mod;
            public int PlaneCount;
            public int DecodePlaneCount;
            public int FetchWords;
            public int DataFetchStart;
            public int FetchSlotStride;
            public int PaletteSnapshotIndex;
            public byte PlaneHasRowMask;
            public readonly uint[] BitplanePointers = new uint[6];
            public readonly int[] BitplaneBaseRows = new int[6];
            public readonly uint[] BitplaneRowAddresses = new uint[6];
            public readonly ushort[] BitplaneDataRegisters = new ushort[6];
        }

        private enum TimelineFetchStatus : byte
        {
            NotAttempted = 0,
            Granted = 1,
            Denied = 2
        }

        private enum TimelineRejectReason : byte
        {
            None = 0,
            FrameIncomplete,
            TimelineInvalid,
            UnsafeWrite,
            SegmentCapacity,
            MissingLine,
            UnsafeLine,
            MissingBitplaneFetch,
            MissingSpriteFetch
        }

        private readonly struct PlanarChunkKey : IEquatable<PlanarChunkKey>
        {
            private readonly ushort _bplcon0;
            private readonly ushort _bplcon2;
            private readonly byte _planeCount;
            private readonly bool _dualPlayfield;
            private readonly byte _planeHasRowMask;
            private readonly ushort _word0;
            private readonly ushort _word1;
            private readonly ushort _word2;
            private readonly ushort _word3;
            private readonly ushort _word4;
            private readonly ushort _word5;

            public PlanarChunkKey(
                ushort bplcon0,
                ushort bplcon2,
                int planeCount,
                bool dualPlayfield,
                byte planeHasRowMask,
                ushort word0,
                ushort word1,
                ushort word2,
                ushort word3,
                ushort word4,
                ushort word5)
            {
                _bplcon0 = bplcon0;
                _bplcon2 = bplcon2;
                _planeCount = (byte)planeCount;
                _dualPlayfield = dualPlayfield;
                _planeHasRowMask = planeHasRowMask;
                _word0 = word0;
                _word1 = word1;
                _word2 = word2;
                _word3 = word3;
                _word4 = word4;
                _word5 = word5;
            }

            public bool Equals(PlanarChunkKey other)
            {
                return _bplcon0 == other._bplcon0 &&
                    _bplcon2 == other._bplcon2 &&
                    _planeCount == other._planeCount &&
                    _dualPlayfield == other._dualPlayfield &&
                    _planeHasRowMask == other._planeHasRowMask &&
                    _word0 == other._word0 &&
                    _word1 == other._word1 &&
                    _word2 == other._word2 &&
                    _word3 == other._word3 &&
                    _word4 == other._word4 &&
                    _word5 == other._word5;
            }

            public override bool Equals(object? obj)
            {
                return obj is PlanarChunkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    _bplcon0,
                    _bplcon2,
                    _planeCount,
                    _dualPlayfield,
                    _planeHasRowMask,
                    _word0,
                    _word1,
                    HashCode.Combine(_word2, _word3, _word4, _word5));
            }
        }

        private readonly struct PlanarChunkDecoded
        {
            private readonly ulong _colorIndexesLow;
            private readonly ulong _colorIndexesHigh;
            private readonly ulong _priorityMasksLow;
            private readonly ulong _priorityMasksHigh;

            public PlanarChunkDecoded(
                ulong colorIndexesLow,
                ulong colorIndexesHigh,
                ulong priorityMasksLow,
                ulong priorityMasksHigh)
            {
                _colorIndexesLow = colorIndexesLow;
                _colorIndexesHigh = colorIndexesHigh;
                _priorityMasksLow = priorityMasksLow;
                _priorityMasksHigh = priorityMasksHigh;
            }

            public byte GetColorIndex(int pixel)
            {
                var shift = (pixel & 7) * 8;
                return (byte)(((pixel < 8 ? _colorIndexesLow : _colorIndexesHigh) >> shift) & 0xFF);
            }

            public byte GetPriorityMask(int pixel)
            {
                var shift = (pixel & 7) * 8;
                return (byte)(((pixel < 8 ? _priorityMasksLow : _priorityMasksHigh) >> shift) & 0xFF);
            }
        }

        private sealed class LiveSpriteDmaState
        {
            public uint ControlAddress;
            public int ControlRow;
            public bool Exhausted;
            public bool HasPendingPos;
            public ushort PendingPos;
            public bool Active;
            public SpriteDescriptor Descriptor;
            public int LastVisibleStop;

            public void Reset(uint pointer, int controlRow)
            {
                ControlAddress = pointer;
                ControlRow = controlRow;
                Exhausted = false;
                HasPendingPos = false;
                PendingPos = 0;
                Active = false;
                Descriptor = default;
                LastVisibleStop = -1;
            }
        }

        private sealed class SavedDisplayState
        {
            public uint CopperListPointer;
            public uint CopperListPointer2;
            public ushort Copcon;
            public ushort Bplcon0;
            public ushort Bplcon1;
            public ushort Bplcon2;
            public ushort DiwStart;
            public ushort DiwStop;
            public ushort DdfStart;
            public ushort DdfStop;
            public ushort Dmacon;
            public short Bpl1Mod;
            public short Bpl2Mod;
            public readonly ushort[] Colors = new ushort[32];
            public readonly uint[] ConvertedColors = new uint[64];
            public readonly uint[] BitplanePointers = new uint[6];
            public readonly int[] BitplaneBaseRows = new int[6];
            public readonly ushort[] BitplaneDataRegisters = new ushort[6];
            public readonly bool[] BitplaneDataRegisterWritten = new bool[6];
        }

        private sealed class SpriteState
        {
            public uint Pointer { get; set; }

            public ushort Pos { get; set; }

            public ushort Ctl { get; set; }

            public ushort DataA { get; set; }

            public ushort DataB { get; set; }

            public bool ManualArmed { get; set; }

            public void Reset()
            {
                Pointer = 0;
                Pos = 0;
                Ctl = 0;
                DataA = 0;
                DataB = 0;
                ManualArmed = false;
            }
        }

        private readonly struct RowDmaPlan
        {
            public RowDmaPlan(
                int generation,
                int row,
                int signature,
                int bitplaneStart,
                int bitplaneCount,
                int spriteStart,
                int spriteCount,
                bool valid)
            {
                Generation = generation;
                Row = row;
                Signature = signature;
                BitplaneStart = bitplaneStart;
                BitplaneCount = bitplaneCount;
                SpriteStart = spriteStart;
                SpriteCount = spriteCount;
                Valid = valid;
            }

            public int Generation { get; }

            public int Row { get; }

            public int Signature { get; }

            public int BitplaneStart { get; }

            public int BitplaneCount { get; }

            public int SpriteStart { get; }

            public int SpriteCount { get; }

            public bool Valid { get; }
        }

        private readonly struct RowDmaSpriteEntry
        {
            public RowDmaSpriteEntry(long cycle, int spriteIndex, int word)
            {
                Cycle = cycle;
                SpriteIndex = spriteIndex;
                Word = word;
            }

            public long Cycle { get; }

            public int SpriteIndex { get; }

            public int Word { get; }
        }

        private enum LiveRasterlinePlanEventKind : byte
        {
            PendingWriteOrCopper,
            LineStateCapture,
            BitplaneFetchBatch,
            SpriteFetchBatch,
            CopperBarrier
        }

        private enum LiveRasterlinePredictionStatus
        {
            None,
            PendingValidation,
            Matched,
            Mismatched,
            UnsupportedCopper,
            UnsupportedPendingWrite,
            UnsupportedSprite,
            UnsupportedInvalidState,
            UnsupportedOverflow
        }

        [StructLayout(LayoutKind.Explicit, Size = 33)]
        private readonly struct LiveRasterlinePlanEvent
        {
            public LiveRasterlinePlanEvent(
                LiveRasterlinePlanEventKind kind,
                long cycle,
                int row,
                long batchStopCycle,
                int cursorA,
                int cursorB,
                int cursorC)
            {
                _cycle = cycle;
                _batchStopCycle = batchStopCycle;
                _row = row;
                _cursorA = cursorA;
                _cursorB = cursorB;
                _cursorC = cursorC;
                _kind = kind;
            }

            [FieldOffset(0)]
            private readonly long _cycle;

            [FieldOffset(8)]
            private readonly long _batchStopCycle;

            [FieldOffset(16)]
            private readonly int _row;

            [FieldOffset(20)]
            private readonly int _cursorA;

            [FieldOffset(24)]
            private readonly int _cursorB;

            [FieldOffset(28)]
            private readonly int _cursorC;

            [FieldOffset(32)]
            private readonly LiveRasterlinePlanEventKind _kind;

            public LiveRasterlinePlanEventKind Kind => _kind;

            public long Cycle => _cycle;

            public int Row => _row;

            public long BatchStopCycle => _batchStopCycle;

            public int CursorA => _cursorA;

            public int CursorB => _cursorB;

            public int CursorC => _cursorC;
        }

        private readonly struct LiveRasterlineDmaDescriptor
        {
            public LiveRasterlineDmaDescriptor(
                int generation,
                int row,
                long lineStartCycle,
                long lineStopCycle,
                bool displayWindowVerticallyOpen,
                ushort bplcon0,
                ushort bplcon1,
                ushort bplcon2,
                ushort dmacon,
                short bpl1Mod,
                short bpl2Mod,
                int planeCount,
                int fetchWords,
                int dataFetchStart,
                int fetchSlotStride,
                byte planeHasRowMask,
                uint bitplaneRowAddress0,
                uint bitplaneRowAddress1,
                uint bitplaneRowAddress2,
                uint bitplaneRowAddress3,
                uint bitplaneRowAddress4,
                uint bitplaneRowAddress5,
                bool hasBitplaneFetches,
                bool hasSpriteSlots)
            {
                Generation = generation;
                Row = row;
                LineStartCycle = lineStartCycle;
                LineStopCycle = lineStopCycle;
                DisplayWindowVerticallyOpen = displayWindowVerticallyOpen;
                Bplcon0 = bplcon0;
                Bplcon1 = bplcon1;
                Bplcon2 = bplcon2;
                Dmacon = dmacon;
                Bpl1Mod = bpl1Mod;
                Bpl2Mod = bpl2Mod;
                PlaneCount = planeCount;
                FetchWords = fetchWords;
                DataFetchStart = dataFetchStart;
                FetchSlotStride = fetchSlotStride;
                PlaneHasRowMask = planeHasRowMask;
                BitplaneRowAddress0 = bitplaneRowAddress0;
                BitplaneRowAddress1 = bitplaneRowAddress1;
                BitplaneRowAddress2 = bitplaneRowAddress2;
                BitplaneRowAddress3 = bitplaneRowAddress3;
                BitplaneRowAddress4 = bitplaneRowAddress4;
                BitplaneRowAddress5 = bitplaneRowAddress5;
                HasBitplaneFetches = hasBitplaneFetches;
                HasSpriteSlots = hasSpriteSlots;
            }

            public int Generation { get; }

            public int Row { get; }

            public long LineStartCycle { get; }

            public long LineStopCycle { get; }

            public bool DisplayWindowVerticallyOpen { get; }

            public ushort Bplcon0 { get; }

            public ushort Bplcon1 { get; }

            public ushort Bplcon2 { get; }

            public ushort Dmacon { get; }

            public short Bpl1Mod { get; }

            public short Bpl2Mod { get; }

            public int PlaneCount { get; }

            public int FetchWords { get; }

            public int DataFetchStart { get; }

            public int FetchSlotStride { get; }

            public byte PlaneHasRowMask { get; }

            public uint BitplaneRowAddress0 { get; }

            public uint BitplaneRowAddress1 { get; }

            public uint BitplaneRowAddress2 { get; }

            public uint BitplaneRowAddress3 { get; }

            public uint BitplaneRowAddress4 { get; }

            public uint BitplaneRowAddress5 { get; }

            public bool HasBitplaneFetches { get; }

            public bool HasSpriteSlots { get; }

            public bool IsValid(int generation, int row)
                => Generation == generation && Row == row;

            public uint GetBitplaneRowAddress(int plane)
            {
                return plane switch
                {
                    0 => BitplaneRowAddress0,
                    1 => BitplaneRowAddress1,
                    2 => BitplaneRowAddress2,
                    3 => BitplaneRowAddress3,
                    4 => BitplaneRowAddress4,
                    5 => BitplaneRowAddress5,
                    _ => 0
                };
            }
        }
    }

    internal readonly struct OcsDisplaySnapshot
    {
        public OcsDisplaySnapshot(
            ushort bplcon0,
            ushort bplcon1,
            ushort bplcon2,
            ushort diwStart,
            ushort diwStop,
            ushort ddfStart,
            ushort ddfStop,
            short bpl1mod,
            short bpl2mod,
            int lastBitplaneNonZeroPixels,
            int lastBitplaneRows,
            int lastBitplaneWords,
            int lastBitplaneMinX,
            int lastBitplaneMinY,
            int lastBitplaneMaxX,
            int lastBitplaneMaxY,
            int lastNormalPlayfieldNonZeroPixels,
            int lastNormalPlayfieldMinX,
            int lastNormalPlayfieldMinY,
            int lastNormalPlayfieldMaxX,
            int lastNormalPlayfieldMaxY,
            int lastPlayfield1NonZeroPixels,
            int lastPlayfield1MinX,
            int lastPlayfield1MinY,
            int lastPlayfield1MaxX,
            int lastPlayfield1MaxY,
            int lastPlayfield2NonZeroPixels,
            int lastPlayfield2MinX,
            int lastPlayfield2MinY,
            int lastPlayfield2MaxX,
            int lastPlayfield2MaxY,
            int lastSpriteNonZeroPixels,
            int lastSpriteMinX,
            int lastSpriteMinY,
            int lastSpriteMaxX,
            int lastSpriteMaxY,
            int lastBitplaneDmaFetches,
            int lastSpriteDmaFetches,
            int lastMissedSpriteDmaSlots,
            long lastFirstDisplayDmaCycle,
            long lastLastDisplayDmaCycle,
            uint[] bitplanePointers,
            int[] bitplaneBaseRows,
            ushort[] colors,
            int[] bitplaneColorCounts,
            int lastTimelineSegmentCount,
            int lastTimelineFallbackCount,
            int lastTimelineSpriteCommandCount,
            int lastActiveTimelineFrameCount,
            int lastArchivedTimelineFrameCount,
            int lastPlanarChunkCacheHits,
            int lastPlanarChunkCacheMisses,
            int lastTimelineCoalescedSegmentCount,
            int lastTimelineFastPathRowCount,
            int lastTimelineFastPathMissCount,
            int lastSpriteRecoveryAttemptCount,
            int lastSpriteDeniedFetchCount,
            int lastRasterlinePlanLines,
            int lastRasterlinePlanValidLines,
            int lastRasterlinePlanInvalidLines,
            int lastRasterlinePlanOverflowLines,
            int lastRasterlinePlanEvents,
            int lastRasterlinePlanPendingWriteOrCopperEvents,
            int lastRasterlinePlanLineStateEvents,
            int lastRasterlinePlanBitplaneFetchEvents,
            int lastRasterlinePlanSpriteFetchEvents,
            int lastRasterlinePlanCopperBarrierEvents,
            int lastRasterlinePlanMaxEventsPerLine,
            int lastPredictedRasterlinePlanLines,
            int lastPredictedRasterlinePlanMatchedLines,
            int lastPredictedRasterlinePlanMismatchedLines,
            int lastPredictedRasterlinePlanUnsupportedLines,
            int lastPredictedRasterlinePlanEvents,
            int lastPredictedRasterlinePlanUnsupportedCopperLines,
            int lastPredictedRasterlinePlanUnsupportedPendingWriteLines,
            int lastPredictedRasterlinePlanUnsupportedSpriteLines,
            int lastPredictedRasterlinePlanUnsupportedInvalidStateLines,
            int lastPredictedRasterlinePlanUnsupportedOverflowLines,
            int lastRasterlineDescriptorBuilds,
            int lastRasterlineDescriptorReplayAttempts,
            int lastRasterlineDescriptorReplayedRows,
            int lastRasterlineDescriptorFallbackRows,
            int lastRasterlineDescriptorBitplaneRows,
            int lastRasterlineDescriptorSpriteRows,
            int lastRasterlineDescriptorMismatches,
            int lastRowDmaPlansBuilt,
            int lastRowDmaPlannedRowsExecuted,
            int lastRowDmaBitplaneEntriesExecuted,
            int lastRowDmaSpriteEntriesExecuted,
            int lastRowDmaScalarFallbackRows,
            int lastRowDmaPlanInvalidationRows,
            int lastRowDmaPlanMismatchRows,
            long copperQuiescentWindowCount,
            long copperQuiescentTotalCycles,
            long copperQuiescentMaxCycles,
            long copperQuiescentActiveStartCycle,
            long copperQuiescentActiveEndCycle,
            int lastArchiveRejectFrameIncomplete,
            int lastArchiveRejectTimelineInvalid,
            int lastArchiveRejectUnsafeWrite,
            int lastArchiveRejectSegmentCapacity,
            int lastArchiveRejectMissingLine,
            int lastArchiveRejectUnsafeLine,
            int lastArchiveRejectMissingBitplaneFetch,
            int lastArchiveRejectMissingSpriteFetch,
            ushort lastArchiveRejectUnsafeOffset,
            bool lastArchiveRejectUnsafeIsCopper,
            int lastArchiveRejectMissingSpriteIndex,
            int lastArchiveRejectMissingSpriteRow,
            int lastArchiveRejectMissingSpriteWord,
            int lastArchiveRejectMissingSpriteStatusA,
            int lastArchiveRejectMissingSpriteStatusB,
            int lastArchiveRejectMissingSpriteCommandRow,
            int lastArchiveRejectMissingSpriteYStart,
            int lastArchiveRejectMissingSpriteYStop,
            int lastArchiveRejectMissingSpriteUsableChannels,
            int lastArchiveRejectMissingSpriteDdfStart,
            ushort lastArchiveRejectMissingSpriteDmacon,
            ushort lastArchiveRejectMissingSpriteBplcon0,
            int lastArchiveRejectMissingSpritePreviousStatusA,
            int lastArchiveRejectMissingSpritePreviousStatusB)
        {
            Bplcon0 = bplcon0;
            Bplcon1 = bplcon1;
            Bplcon2 = bplcon2;
            DiwStart = diwStart;
            DiwStop = diwStop;
            DdfStart = ddfStart;
            DdfStop = ddfStop;
            Bpl1Mod = bpl1mod;
            Bpl2Mod = bpl2mod;
            LastBitplaneNonZeroPixels = lastBitplaneNonZeroPixels;
            LastBitplaneRows = lastBitplaneRows;
            LastBitplaneWords = lastBitplaneWords;
            LastBitplaneMinX = lastBitplaneMinX;
            LastBitplaneMinY = lastBitplaneMinY;
            LastBitplaneMaxX = lastBitplaneMaxX;
            LastBitplaneMaxY = lastBitplaneMaxY;
            LastNormalPlayfieldNonZeroPixels = lastNormalPlayfieldNonZeroPixels;
            LastNormalPlayfieldMinX = lastNormalPlayfieldMinX;
            LastNormalPlayfieldMinY = lastNormalPlayfieldMinY;
            LastNormalPlayfieldMaxX = lastNormalPlayfieldMaxX;
            LastNormalPlayfieldMaxY = lastNormalPlayfieldMaxY;
            LastPlayfield1NonZeroPixels = lastPlayfield1NonZeroPixels;
            LastPlayfield1MinX = lastPlayfield1MinX;
            LastPlayfield1MinY = lastPlayfield1MinY;
            LastPlayfield1MaxX = lastPlayfield1MaxX;
            LastPlayfield1MaxY = lastPlayfield1MaxY;
            LastPlayfield2NonZeroPixels = lastPlayfield2NonZeroPixels;
            LastPlayfield2MinX = lastPlayfield2MinX;
            LastPlayfield2MinY = lastPlayfield2MinY;
            LastPlayfield2MaxX = lastPlayfield2MaxX;
            LastPlayfield2MaxY = lastPlayfield2MaxY;
            LastSpriteNonZeroPixels = lastSpriteNonZeroPixels;
            LastSpriteMinX = lastSpriteMinX;
            LastSpriteMinY = lastSpriteMinY;
            LastSpriteMaxX = lastSpriteMaxX;
            LastSpriteMaxY = lastSpriteMaxY;
            LastBitplaneDmaFetches = lastBitplaneDmaFetches;
            LastSpriteDmaFetches = lastSpriteDmaFetches;
            LastMissedSpriteDmaSlots = lastMissedSpriteDmaSlots;
            LastFirstDisplayDmaCycle = lastFirstDisplayDmaCycle;
            LastLastDisplayDmaCycle = lastLastDisplayDmaCycle;
            BitplanePointers = bitplanePointers;
            BitplaneBaseRows = bitplaneBaseRows;
            Colors = colors;
            BitplaneColorCounts = bitplaneColorCounts;
            LastTimelineSegmentCount = lastTimelineSegmentCount;
            LastTimelineFallbackCount = lastTimelineFallbackCount;
            LastTimelineSpriteCommandCount = lastTimelineSpriteCommandCount;
            LastActiveTimelineFrameCount = lastActiveTimelineFrameCount;
            LastArchivedTimelineFrameCount = lastArchivedTimelineFrameCount;
            LastPlanarChunkCacheHits = lastPlanarChunkCacheHits;
            LastPlanarChunkCacheMisses = lastPlanarChunkCacheMisses;
            LastTimelineCoalescedSegmentCount = lastTimelineCoalescedSegmentCount;
            LastTimelineFastPathRowCount = lastTimelineFastPathRowCount;
            LastTimelineFastPathMissCount = lastTimelineFastPathMissCount;
            LastSpriteRecoveryAttemptCount = lastSpriteRecoveryAttemptCount;
            LastSpriteDeniedFetchCount = lastSpriteDeniedFetchCount;
            LastRasterlinePlanLines = lastRasterlinePlanLines;
            LastRasterlinePlanValidLines = lastRasterlinePlanValidLines;
            LastRasterlinePlanInvalidLines = lastRasterlinePlanInvalidLines;
            LastRasterlinePlanOverflowLines = lastRasterlinePlanOverflowLines;
            LastRasterlinePlanEvents = lastRasterlinePlanEvents;
            LastRasterlinePlanPendingWriteOrCopperEvents = lastRasterlinePlanPendingWriteOrCopperEvents;
            LastRasterlinePlanLineStateEvents = lastRasterlinePlanLineStateEvents;
            LastRasterlinePlanBitplaneFetchEvents = lastRasterlinePlanBitplaneFetchEvents;
            LastRasterlinePlanSpriteFetchEvents = lastRasterlinePlanSpriteFetchEvents;
            LastRasterlinePlanCopperBarrierEvents = lastRasterlinePlanCopperBarrierEvents;
            LastRasterlinePlanMaxEventsPerLine = lastRasterlinePlanMaxEventsPerLine;
            LastPredictedRasterlinePlanLines = lastPredictedRasterlinePlanLines;
            LastPredictedRasterlinePlanMatchedLines = lastPredictedRasterlinePlanMatchedLines;
            LastPredictedRasterlinePlanMismatchedLines = lastPredictedRasterlinePlanMismatchedLines;
            LastPredictedRasterlinePlanUnsupportedLines = lastPredictedRasterlinePlanUnsupportedLines;
            LastPredictedRasterlinePlanEvents = lastPredictedRasterlinePlanEvents;
            LastPredictedRasterlinePlanUnsupportedCopperLines = lastPredictedRasterlinePlanUnsupportedCopperLines;
            LastPredictedRasterlinePlanUnsupportedPendingWriteLines = lastPredictedRasterlinePlanUnsupportedPendingWriteLines;
            LastPredictedRasterlinePlanUnsupportedSpriteLines = lastPredictedRasterlinePlanUnsupportedSpriteLines;
            LastPredictedRasterlinePlanUnsupportedInvalidStateLines = lastPredictedRasterlinePlanUnsupportedInvalidStateLines;
            LastPredictedRasterlinePlanUnsupportedOverflowLines = lastPredictedRasterlinePlanUnsupportedOverflowLines;
            LastRasterlineDescriptorBuilds = lastRasterlineDescriptorBuilds;
            LastRasterlineDescriptorReplayAttempts = lastRasterlineDescriptorReplayAttempts;
            LastRasterlineDescriptorReplayedRows = lastRasterlineDescriptorReplayedRows;
            LastRasterlineDescriptorFallbackRows = lastRasterlineDescriptorFallbackRows;
            LastRasterlineDescriptorBitplaneRows = lastRasterlineDescriptorBitplaneRows;
            LastRasterlineDescriptorSpriteRows = lastRasterlineDescriptorSpriteRows;
            LastRasterlineDescriptorMismatches = lastRasterlineDescriptorMismatches;
            LastRowDmaPlansBuilt = lastRowDmaPlansBuilt;
            LastRowDmaPlannedRowsExecuted = lastRowDmaPlannedRowsExecuted;
            LastRowDmaBitplaneEntriesExecuted = lastRowDmaBitplaneEntriesExecuted;
            LastRowDmaSpriteEntriesExecuted = lastRowDmaSpriteEntriesExecuted;
            LastRowDmaScalarFallbackRows = lastRowDmaScalarFallbackRows;
            LastRowDmaPlanInvalidationRows = lastRowDmaPlanInvalidationRows;
            LastRowDmaPlanMismatchRows = lastRowDmaPlanMismatchRows;
            CopperQuiescentWindowCount = copperQuiescentWindowCount;
            CopperQuiescentTotalCycles = copperQuiescentTotalCycles;
            CopperQuiescentMaxCycles = copperQuiescentMaxCycles;
            CopperQuiescentActiveStartCycle = copperQuiescentActiveStartCycle;
            CopperQuiescentActiveEndCycle = copperQuiescentActiveEndCycle;
            LastArchiveRejectFrameIncomplete = lastArchiveRejectFrameIncomplete;
            LastArchiveRejectTimelineInvalid = lastArchiveRejectTimelineInvalid;
            LastArchiveRejectUnsafeWrite = lastArchiveRejectUnsafeWrite;
            LastArchiveRejectSegmentCapacity = lastArchiveRejectSegmentCapacity;
            LastArchiveRejectMissingLine = lastArchiveRejectMissingLine;
            LastArchiveRejectUnsafeLine = lastArchiveRejectUnsafeLine;
            LastArchiveRejectMissingBitplaneFetch = lastArchiveRejectMissingBitplaneFetch;
            LastArchiveRejectMissingSpriteFetch = lastArchiveRejectMissingSpriteFetch;
            LastArchiveRejectUnsafeOffset = lastArchiveRejectUnsafeOffset;
            LastArchiveRejectUnsafeIsCopper = lastArchiveRejectUnsafeIsCopper;
            LastArchiveRejectMissingSpriteIndex = lastArchiveRejectMissingSpriteIndex;
            LastArchiveRejectMissingSpriteRow = lastArchiveRejectMissingSpriteRow;
            LastArchiveRejectMissingSpriteWord = lastArchiveRejectMissingSpriteWord;
            LastArchiveRejectMissingSpriteStatusA = lastArchiveRejectMissingSpriteStatusA;
            LastArchiveRejectMissingSpriteStatusB = lastArchiveRejectMissingSpriteStatusB;
            LastArchiveRejectMissingSpriteCommandRow = lastArchiveRejectMissingSpriteCommandRow;
            LastArchiveRejectMissingSpriteYStart = lastArchiveRejectMissingSpriteYStart;
            LastArchiveRejectMissingSpriteYStop = lastArchiveRejectMissingSpriteYStop;
            LastArchiveRejectMissingSpriteUsableChannels = lastArchiveRejectMissingSpriteUsableChannels;
            LastArchiveRejectMissingSpriteDdfStart = lastArchiveRejectMissingSpriteDdfStart;
            LastArchiveRejectMissingSpriteDmacon = lastArchiveRejectMissingSpriteDmacon;
            LastArchiveRejectMissingSpriteBplcon0 = lastArchiveRejectMissingSpriteBplcon0;
            LastArchiveRejectMissingSpritePreviousStatusA = lastArchiveRejectMissingSpritePreviousStatusA;
            LastArchiveRejectMissingSpritePreviousStatusB = lastArchiveRejectMissingSpritePreviousStatusB;
        }

        public ushort Bplcon0 { get; }

        public ushort Bplcon1 { get; }

        public ushort Bplcon2 { get; }

        public ushort DiwStart { get; }

        public ushort DiwStop { get; }

        public ushort DdfStart { get; }

        public ushort DdfStop { get; }

        public short Bpl1Mod { get; }

        public short Bpl2Mod { get; }

        public int LastBitplaneNonZeroPixels { get; }

        public int LastBitplaneRows { get; }

        public int LastBitplaneWords { get; }

        public int LastBitplaneMinX { get; }

        public int LastBitplaneMinY { get; }

        public int LastBitplaneMaxX { get; }

        public int LastBitplaneMaxY { get; }

        public int LastNormalPlayfieldNonZeroPixels { get; }

        public int LastNormalPlayfieldMinX { get; }

        public int LastNormalPlayfieldMinY { get; }

        public int LastNormalPlayfieldMaxX { get; }

        public int LastNormalPlayfieldMaxY { get; }

        public int LastPlayfield1NonZeroPixels { get; }

        public int LastPlayfield1MinX { get; }

        public int LastPlayfield1MinY { get; }

        public int LastPlayfield1MaxX { get; }

        public int LastPlayfield1MaxY { get; }

        public int LastPlayfield2NonZeroPixels { get; }

        public int LastPlayfield2MinX { get; }

        public int LastPlayfield2MinY { get; }

        public int LastPlayfield2MaxX { get; }

        public int LastPlayfield2MaxY { get; }

        public int LastSpriteNonZeroPixels { get; }

        public int LastSpriteMinX { get; }

        public int LastSpriteMinY { get; }

        public int LastSpriteMaxX { get; }

        public int LastSpriteMaxY { get; }

        public int LastBitplaneDmaFetches { get; }

        public int LastSpriteDmaFetches { get; }

        public int LastMissedSpriteDmaSlots { get; }

        public long LastFirstDisplayDmaCycle { get; }

        public long LastLastDisplayDmaCycle { get; }

        public uint[] BitplanePointers { get; }

        public int[] BitplaneBaseRows { get; }

        public ushort[] Colors { get; }

        public int[] BitplaneColorCounts { get; }

        public int LastTimelineSegmentCount { get; }

        public int LastTimelineFallbackCount { get; }

        public int LastTimelineSpriteCommandCount { get; }

        public int LastActiveTimelineFrameCount { get; }

        public int LastArchivedTimelineFrameCount { get; }

        public int LastPlanarChunkCacheHits { get; }

        public int LastPlanarChunkCacheMisses { get; }

        public int LastTimelineCoalescedSegmentCount { get; }

        public int LastTimelineFastPathRowCount { get; }

        public int LastTimelineFastPathMissCount { get; }

        public int LastSpriteRecoveryAttemptCount { get; }

        public int LastSpriteDeniedFetchCount { get; }

        public int LastRasterlinePlanLines { get; }

        public int LastRasterlinePlanValidLines { get; }

        public int LastRasterlinePlanInvalidLines { get; }

        public int LastRasterlinePlanOverflowLines { get; }

        public int LastRasterlinePlanEvents { get; }

        public int LastRasterlinePlanPendingWriteOrCopperEvents { get; }

        public int LastRasterlinePlanLineStateEvents { get; }

        public int LastRasterlinePlanBitplaneFetchEvents { get; }

        public int LastRasterlinePlanSpriteFetchEvents { get; }

        public int LastRasterlinePlanCopperBarrierEvents { get; }

        public int LastRasterlinePlanMaxEventsPerLine { get; }

        public int LastPredictedRasterlinePlanLines { get; }

        public int LastPredictedRasterlinePlanMatchedLines { get; }

        public int LastPredictedRasterlinePlanMismatchedLines { get; }

        public int LastPredictedRasterlinePlanUnsupportedLines { get; }

        public int LastPredictedRasterlinePlanEvents { get; }

        public int LastPredictedRasterlinePlanUnsupportedCopperLines { get; }

        public int LastPredictedRasterlinePlanUnsupportedPendingWriteLines { get; }

        public int LastPredictedRasterlinePlanUnsupportedSpriteLines { get; }

        public int LastPredictedRasterlinePlanUnsupportedInvalidStateLines { get; }

        public int LastPredictedRasterlinePlanUnsupportedOverflowLines { get; }

        public int LastRasterlineDescriptorBuilds { get; }

        public int LastRasterlineDescriptorReplayAttempts { get; }

        public int LastRasterlineDescriptorReplayedRows { get; }

        public int LastRasterlineDescriptorFallbackRows { get; }

        public int LastRasterlineDescriptorBitplaneRows { get; }

        public int LastRasterlineDescriptorSpriteRows { get; }

        public int LastRasterlineDescriptorMismatches { get; }

        public int LastRowDmaPlansBuilt { get; }

        public int LastRowDmaPlannedRowsExecuted { get; }

        public int LastRowDmaBitplaneEntriesExecuted { get; }

        public int LastRowDmaSpriteEntriesExecuted { get; }

        public int LastRowDmaScalarFallbackRows { get; }

        public int LastRowDmaPlanInvalidationRows { get; }

        public int LastRowDmaPlanMismatchRows { get; }

        public long CopperQuiescentWindowCount { get; }

        public long CopperQuiescentTotalCycles { get; }

        public long CopperQuiescentMaxCycles { get; }

        public long CopperQuiescentActiveStartCycle { get; }

        public long CopperQuiescentActiveEndCycle { get; }

        public int LastArchiveRejectFrameIncomplete { get; }

        public int LastArchiveRejectTimelineInvalid { get; }

        public int LastArchiveRejectUnsafeWrite { get; }

        public int LastArchiveRejectSegmentCapacity { get; }

        public int LastArchiveRejectMissingLine { get; }

        public int LastArchiveRejectUnsafeLine { get; }

        public int LastArchiveRejectMissingBitplaneFetch { get; }

        public int LastArchiveRejectMissingSpriteFetch { get; }

        public ushort LastArchiveRejectUnsafeOffset { get; }

        public bool LastArchiveRejectUnsafeIsCopper { get; }

        public int LastArchiveRejectMissingSpriteIndex { get; }

        public int LastArchiveRejectMissingSpriteRow { get; }

        public int LastArchiveRejectMissingSpriteWord { get; }

        public int LastArchiveRejectMissingSpriteStatusA { get; }

        public int LastArchiveRejectMissingSpriteStatusB { get; }

        public int LastArchiveRejectMissingSpriteCommandRow { get; }

        public int LastArchiveRejectMissingSpriteYStart { get; }

        public int LastArchiveRejectMissingSpriteYStop { get; }

        public int LastArchiveRejectMissingSpriteUsableChannels { get; }

        public int LastArchiveRejectMissingSpriteDdfStart { get; }

        public ushort LastArchiveRejectMissingSpriteDmacon { get; }

        public ushort LastArchiveRejectMissingSpriteBplcon0 { get; }

        public int LastArchiveRejectMissingSpritePreviousStatusA { get; }

        public int LastArchiveRejectMissingSpritePreviousStatusB { get; }
    }

}
