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
        private const int MaxPendingWrites = 65536;
        private const int StandardHStart = 0x81 - AmigaConstants.PalLowResOverscanBorderX;
        private const int StandardVStart = 0x2C - AmigaConstants.PalLowResOverscanBorderY;
        private const ushort DefaultDiwStart = AgnusRegisterBank.DefaultDiwStart;
        private const ushort DefaultDiwStop = AgnusRegisterBank.DefaultDiwStop;
        private const ushort DefaultDdfStart = AgnusRegisterBank.DefaultDdfStart;
        private const ushort DefaultDdfStop = AgnusRegisterBank.DefaultDdfStop;
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
        // A satisfied WAIT is observed on the following Copper phase. The next
        // instruction's first DMA word is available one Copper memory cycle later.
        private const int CopperWaitWakeHpUnits = 5;
        private const int CopperWaitLineEndBlackoutHpUnits = 4;
        private const int LineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
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
        private const int MaxCapturedCopperDisplayWrites = 65536;
        private static readonly int[] LowResBitplaneFetchSlotsByPlane = [7, 3, 5, 1, 6, 2];
        private static readonly int[] HighResBitplaneFetchSlotsByPlane = [3, 1, 2, 0];
        private static readonly sbyte[] LowResBitplanePlanesByFetchSlot = [-1, 3, 5, 1, -1, 2, 4, 0];
        private static readonly sbyte[] HighResBitplanePlanesByFetchSlot = [3, 1, 2, 0];
        private readonly AmigaBus _bus;
        private readonly AgnusRegisterBank _agnusRegisters;
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
        private readonly SavedDisplayState _archivedFrameInitialState = new SavedDisplayState();
        private readonly List<PendingCustomWrite> _liveFrameWrites = new List<PendingCustomWrite>(MaxPendingWrites);
        private readonly List<PendingCustomWrite> _archivedFrameWrites = new List<PendingCustomWrite>(MaxPendingWrites);
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
        private BoundedWriteLog? _copperDisplayWrites;
        private DisplayFrameTimeline _displayTimeline = new DisplayFrameTimeline();
        private DisplayFrameTimeline _archivedDisplayTimeline = new DisplayFrameTimeline();
        private readonly ushort[] _archivedPaletteSnapshotColors = new ushort[MaxLivePaletteSnapshots * 32];
        private readonly uint[] _archivedPaletteSnapshotConvertedColors = new uint[MaxLivePaletteSnapshots * PaletteColorCount];
        private long _archivedFrameWritesStartCycle = long.MinValue;
        private long _archivedFrameWritesStopCycle = long.MinValue;
        private bool _archivedFrameWritesValid;
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
        private int _lastTimelineMissingBitplaneFallbackCount;
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

        public Display(AmigaBus bus, AgnusRegisterBank agnusRegisters, bool liveDmaEnabled = true)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _agnusRegisters = agnusRegisters ?? throw new ArgumentNullException(nameof(agnusRegisters));
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

        internal IReadOnlyList<CustomRegisterWrite> CopperDisplayWrites
            => _copperDisplayWrites ?? (IReadOnlyList<CustomRegisterWrite>)Array.Empty<CustomRegisterWrite>();

        internal int BitplaneDataSpanCount => _bitplaneDataSpans.Count;

        internal bool LiveDmaEnabled => _liveDmaEnabled;

        internal ulong LiveWakeVersion => _liveWakeVersion;

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
                _lastTimelineMissingBitplaneFallbackCount,
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
            _copperDisplayWrites?.Clear();
            _pendingIndex = 0;
            Array.Clear(_bitplanePointers);
            Array.Clear(_bitplaneBaseRows);
            Array.Clear(_bitplaneDataRegisters);
            Array.Clear(_bitplaneDataRegisterWritten);
            _bitplaneDataSpans.Clear();
            _copperListPointer = _agnusRegisters.CopperListPointer1;
            _copperListPointer2 = _agnusRegisters.CopperListPointer2;
            _diwStart = _agnusRegisters.DiwStart;
            _diwStop = _agnusRegisters.DiwStop;
            _ddfStart = _agnusRegisters.DdfStart;
            _ddfStop = _agnusRegisters.DdfStop;
            _bplcon0 = 0;
            _bplcon1 = 0;
            _bplcon2 = 0;
            _copcon = _agnusRegisters.CopperControl;
            _dmacon = 0;
            _bpl1mod = _agnusRegisters.BitplaneModulo1;
            _bpl2mod = _agnusRegisters.BitplaneModulo2;
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
            var previousAuditSource = _bus.PushSlotScheduleAuditSource(AgnusSlotAuditSource.LiveBitplaneFetch, row, word, plane);
            try
            {
                return _bus.TryReadRowBitplaneDmaWord(address, fetchCycle, out var value, out var grantedCycle)
                    ? new BitplaneDmaReadLatch(row, plane, word, value, granted: true, grantedCycle)
                    : BitplaneDmaReadLatch.Denied(row, plane, word, grantedCycle);
            }
            finally
            {
                _bus.RestoreSlotScheduleAuditSource(previousAuditSource);
            }
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
            if (slotCycle < _liveFrameStartCycle || slotCycle >= GetLiveFrameStopCycle())
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
                CommitLiveBitplanePointersToAgnus(write.Cycle);
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
                cycle >= GetLiveFrameStopCycle())
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
                cycle >= GetLiveFrameStopCycle())
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

            // The 358-pixel presentation row begins at h=$38 and therefore
            // extends eight color clocks past the 227-clock physical raster
            // boundary.  A write during those first clocks of the new beam
            // line both establishes that line's x=0 state and changes the
            // trailing pixels of the preceding presentation row.  Keep the
            // normal clamped x=0 record above, then stitch the same physical
            // state change into the preceding row at its unwrapped position.
            GetCopperBeamPositionForCycle(
                _liveFrameStartCycle,
                cycle,
                out _,
                out var horizontal);
            var wrappedX = ((horizontal + CopperHorizontalUnitsPerLine - DefaultDdfStart) * 2) + pixelDelay;
            var wrappedRow = row - 1;
            if ((uint)wrappedRow < (uint)LowResOutputHeight &&
                (uint)wrappedX < (uint)AmigaConstants.PalLowResWidth &&
                IsLiveLineValid(wrappedRow) &&
                _displayTimeline.HasLine(wrappedRow))
            {
                var wrappedSnapshotIndex = CaptureTimelineStateSnapshot(
                    wrappedRow,
                    _liveLineStates[row]);
                _displayTimeline.RecordDisplayChange(
                    wrappedRow,
                    wrappedX,
                    wrappedSnapshotIndex,
                    IsTimelineUnsafeDisplayWrite(offset),
                    offset,
                    isCopper);
            }

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

        private static void CopyDisplayState(SavedDisplayState source, SavedDisplayState destination)
        {
            destination.CopperListPointer = source.CopperListPointer;
            destination.CopperListPointer2 = source.CopperListPointer2;
            destination.Copcon = source.Copcon;
            destination.DiwStart = source.DiwStart;
            destination.DiwStop = source.DiwStop;
            destination.DdfStart = source.DdfStart;
            destination.DdfStop = source.DdfStop;
            destination.Bplcon0 = source.Bplcon0;
            destination.Bplcon1 = source.Bplcon1;
            destination.Bplcon2 = source.Bplcon2;
            destination.Dmacon = source.Dmacon;
            destination.Bpl1Mod = source.Bpl1Mod;
            destination.Bpl2Mod = source.Bpl2Mod;
            Array.Copy(source.Colors, destination.Colors, source.Colors.Length);
            Array.Copy(source.ConvertedColors, destination.ConvertedColors, source.ConvertedColors.Length);
            Array.Copy(source.BitplanePointers, destination.BitplanePointers, source.BitplanePointers.Length);
            Array.Copy(source.BitplaneBaseRows, destination.BitplaneBaseRows, source.BitplaneBaseRows.Length);
            Array.Copy(source.BitplaneDataRegisters, destination.BitplaneDataRegisters, source.BitplaneDataRegisters.Length);
            Array.Copy(source.BitplaneDataRegisterWritten, destination.BitplaneDataRegisterWritten, source.BitplaneDataRegisterWritten.Length);
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

    internal readonly struct OcsLiveDmaScratchCpuWrite
    {
        private OcsLiveDmaScratchCpuWrite(
            bool hasValue,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            uint value)
        {
            HasValue = hasValue;
            Target = target;
            Address = address;
            Size = size;
            Value = value;
        }

        public static OcsLiveDmaScratchCpuWrite None { get; } = default;

        public static OcsLiveDmaScratchCpuWrite Byte(
            AmigaBusAccessTarget target,
            uint address,
            byte value)
            => new OcsLiveDmaScratchCpuWrite(
                hasValue: true,
                target,
                address,
                AmigaBusAccessSize.Byte,
                value);

        public static OcsLiveDmaScratchCpuWrite Word(
            AmigaBusAccessTarget target,
            uint address,
            ushort value)
            => new OcsLiveDmaScratchCpuWrite(
                hasValue: true,
                target,
                address,
                AmigaBusAccessSize.Word,
                value);

        public static OcsLiveDmaScratchCpuWrite Long(
            AmigaBusAccessTarget target,
            uint address,
            uint value)
            => new OcsLiveDmaScratchCpuWrite(
                hasValue: true,
                target,
                address,
                AmigaBusAccessSize.Long,
                value);

        public bool HasValue { get; }

        public AmigaBusAccessTarget Target { get; }

        public uint Address { get; }

        public AmigaBusAccessSize Size { get; }

        public uint Value { get; }
    }

    internal readonly struct OcsLiveDmaScratchResult
    {
        public OcsLiveDmaScratchResult(
            bool supported,
            string unsupportedReason,
            long grantedCycle,
            long secondWordCycle,
            long completedCycle,
            AgnusSlotTimelineSignature timeline,
            int bitplaneFetches,
            int spriteFetches,
            int copperSteps,
            long firstDmaCycle,
            long lastDmaCycle,
            string detail = "")
        {
            Supported = supported;
            UnsupportedReason = unsupportedReason;
            GrantedCycle = grantedCycle;
            SecondWordCycle = secondWordCycle;
            CompletedCycle = completedCycle;
            Timeline = timeline;
            BitplaneFetches = bitplaneFetches;
            SpriteFetches = spriteFetches;
            CopperSteps = copperSteps;
            FirstDmaCycle = firstDmaCycle;
            LastDmaCycle = lastDmaCycle;
            Detail = detail;
        }

        public static OcsLiveDmaScratchResult Unsupported(string reason)
            => new OcsLiveDmaScratchResult(
                supported: false,
                unsupportedReason: reason,
                grantedCycle: -1,
                secondWordCycle: -1,
                completedCycle: -1,
                timeline: default,
                bitplaneFetches: 0,
                spriteFetches: 0,
                copperSteps: 0,
                firstDmaCycle: -1,
                lastDmaCycle: -1);

        public bool Supported { get; }

        public string UnsupportedReason { get; }

        public long GrantedCycle { get; }

        public long SecondWordCycle { get; }

        public long CompletedCycle { get; }

        public AgnusSlotTimelineSignature Timeline { get; }

        public int BitplaneFetches { get; }

        public int SpriteFetches { get; }

        public int CopperSteps { get; }

        public long FirstDmaCycle { get; }

        public long LastDmaCycle { get; }

        public string Detail { get; }

        public bool HasLiveDmaCoverage => BitplaneFetches != 0 || SpriteFetches != 0 || CopperSteps != 0;

        public string ToDetailString()
            => $"scratchLive={BitplaneFetches}/{SpriteFetches}/cop:{CopperSteps}/grant:{GrantedCycle},{SecondWordCycle}->{CompletedCycle}/firstLast:{FirstDmaCycle}->{LastDmaCycle}/unsup:{UnsupportedReason}{(Detail.Length == 0 ? string.Empty : "/detail:" + Detail)}";
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
            int lastTimelineMissingBitplaneFallbackCount,
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
            LastTimelineMissingBitplaneFallbackCount = lastTimelineMissingBitplaneFallbackCount;
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

        public int LastTimelineMissingBitplaneFallbackCount { get; }

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
