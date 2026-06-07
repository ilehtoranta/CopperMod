using System;
using System.Collections.Generic;
using System.Numerics;

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
        private const int StandardSpriteHorizontalOffset = 64 - AmigaConstants.PalLowResOverscanBorderX;
        private const int MaxBitplaneFetchWords = 64;
        private const byte Playfield1PriorityMask = 0x01;
        private const byte Playfield2PriorityMask = 0x02;
        private const byte NormalPlayfieldPriorityMask = 0x04;
        private const int LowResOutputHeight = AmigaConstants.PalLowResHeight;
        private const int LastCopperHorizontal = 0xE2;
        private const int CopperHorizontalUnitsPerLine = 227;
        private const int CopperInstructionDataHpUnits = 2;
        private const int CopperMoveHpUnits = 4;
        private const int CopperSkipHpUnits = 4;
        // WAIT is a 3-memory-cycle instruction total; after the fetched WAIT is parked,
        // only the extra wake memory cycle remains.
        private const int CopperWaitWakeHpUnits = 2;
        private const ushort CopconCopperDanger = 0x0002;
        private const long PalFrameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        private const int PalLineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        private const int CopperHpCycles = AmigaConstants.A500PalCpuCyclesPerColorClock;
        private const int PaletteColorCount = 64;
        private const int MaxPaletteFrameSpans = MaxPendingWrites;
        private const int MaxSpriteFrameCommands = 256;
        private const int LiveBitplanePlaneCount = 6;
        private const int LiveBitplaneWordsPerRow = LiveBitplanePlaneCount * MaxBitplaneFetchWords;
        private const int LiveSpriteChannelCount = 8;
        private const int LiveSpriteWordsPerChannel = 2;
        private const int LiveSpriteWordsPerRow = LiveSpriteChannelCount * LiveSpriteWordsPerChannel;
        private const int MaxLivePaletteSnapshots = MaxPendingWrites;
        private static readonly int[] LowResBitplaneFetchSlotsByPlane = [0, 1, 2, 3, 4, 5];
        private readonly AmigaBus _bus;
        private readonly bool _liveDmaEnabled;
        private readonly List<PendingCustomWrite> _pendingWrites = new List<PendingCustomWrite>(MaxPendingWrites);
        private readonly ushort[] _colors = new ushort[32];
        private readonly uint[] _convertedColors = new uint[PaletteColorCount];
        private readonly uint[] _bitplanePointers = new uint[6];
        private readonly int[] _bitplaneBaseRows = new int[6];
        private byte[] _playfieldPriorityMasks = Array.Empty<byte>();
        private readonly ushort[,] _renderPlaneWords = new ushort[6, MaxBitplaneFetchWords];
        private readonly bool[] _renderPlaneHasRow = new bool[6];
        private readonly SpriteState[] _sprites = new SpriteState[8];
        private readonly List<SpriteFrameCommand> _spriteFrameCommands = new List<SpriteFrameCommand>(MaxSpriteFrameCommands * 8);
        private readonly List<SpriteFrameCommand>[] _spriteCommandScratch = new List<SpriteFrameCommand>[8];
        private readonly bool[] _evenSpriteAttached = new bool[MaxSpriteFrameCommands];
        private readonly bool[] _oddSpriteAttached = new bool[MaxSpriteFrameCommands];
        private readonly List<PaletteFrameSpan> _paletteFrameSpans = new List<PaletteFrameSpan>(MaxPaletteFrameSpans);
        private readonly uint[] _paletteFrameSpanColors = new uint[MaxPaletteFrameSpans * PaletteColorCount];
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
        private readonly bool[] _liveSpriteDmaExhausted = new bool[LiveSpriteChannelCount];
        private readonly LiveSpriteDmaState[] _liveSpriteDmaStates = new LiveSpriteDmaState[LiveSpriteChannelCount];
        private readonly ushort[] _livePaletteSnapshotColors = new ushort[MaxLivePaletteSnapshots * 32];
        private readonly uint[] _livePaletteSnapshotConvertedColors = new uint[MaxLivePaletteSnapshots * PaletteColorCount];
        private bool _renderingLiveCapture;
        private bool _advancingLiveDma;
        private bool _liveFrameValid;
        private int _liveGeneration = 1;
        private int _livePaletteSnapshotCount;
        private int _liveCurrentPaletteSnapshotIndex = -1;
        private int _lastAppliedLivePaletteSnapshotIndex = -1;
        private bool _livePaletteSnapshotDirty = true;
        private bool _liveNextDisplayEventValid;
        private long _liveNextDisplayEventCycle;
        private long _liveCycle;
        private long _liveFrameStartCycle;
        private long _liveCapturedThroughCycle;
        private int _liveNextLineStateRow;
        private int _liveNextFetchRow;
        private int _liveNextFetchWord;
        private int _liveNextFetchPlane;
        private int _liveNextFetchSlot;
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
        private CopperPresentationState _liveCopper;
        private bool _liveFrameInitialStateValid;
        private bool _liveFrameWriteOverflowed;
        private bool _liveFrameHasLateDisplayWindowWrites;

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

        internal bool LiveDmaEnabled => _liveDmaEnabled;

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

            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                for (var attempt = 0; attempt < 32; attempt++)
                {
                    var before = _bus.FindHrmDmaCandidate(requestedCycle);
                    var frameStopCycle = _liveFrameStartCycle + PalFrameCycles;
                    if (before < frameStopCycle)
                    {
                        AdvanceLiveDmaWithinFrame(before);
                    }

                    CaptureLiveBitplaneDmaBeforeHrmGrant(requestedCycle);
                    CaptureLiveSpriteDmaBeforeHrmGrant(requestedCycle);
                    if (_bus.FindHrmDmaCandidate(requestedCycle) == before)
                    {
                        return;
                    }
                }
            }
            finally
            {
                EndLiveDmaCapture(savedAdvancingLiveDma);
            }
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
                var adjustedCandidate = _bus.FindHrmDmaCandidate(requestedCycle);
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

                if (_bus.FindHrmDmaCandidate(requestedCycle) == candidate)
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
                colorCounts);
        }

        public void Reset()
        {
            _pendingWrites.Clear();
            _pendingIndex = 0;
            Array.Clear(_bitplanePointers);
            Array.Clear(_bitplaneBaseRows);
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
            _liveFirstDisplayDmaCycle = -1;
            _liveLastDisplayDmaCycle = -1;
            _liveCopper = new CopperPresentationState(_copperListPointer, 0);
            InvalidateLiveDisplayEventCycle();
            ClearLiveFrameCapture(0);
        }

        internal void AdvanceLiveDmaTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
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
                while (targetCycle > _liveFrameStartCycle + PalFrameCycles)
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

            return IsBitplaneDmaEnabledForRendering() ||
                IsLiveCopperDmaEnabled() ||
                IsSpriteDmaEnabled() ||
                TryPeekPendingWrite(out _);
        }

        private void AdvanceIdleLiveDmaTo(long targetCycle)
        {
            while (targetCycle > _liveFrameStartCycle + PalFrameCycles)
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
            for (var y = row; y < LowResOutputHeight; y++)
            {
                _liveLineStates[y].Generation = 0;
            }

            ClearLiveSpriteWordMasksFrom(row);
            ResetLiveSpriteDmaStates(row);
            _liveNextLineStateRow = Math.Min(_liveNextLineStateRow, row);
            _liveNextFetchRow = Math.Min(_liveNextFetchRow, row);
            _liveNextFetchWord = 0;
            _liveNextFetchPlane = 0;
            _liveNextFetchSlot = 0;
            _liveNextSpriteRow = Math.Min(_liveNextSpriteRow, row);
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
        }

        private void StartLiveFrame(long frameStartCycle)
        {
            ClearLiveFrameCapture(frameStartCycle);
            _liveFrameStartCycle = frameStartCycle;
            _liveCycle = frameStartCycle;
            _liveCapturedThroughCycle = frameStartCycle;
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
            _liveFirstDisplayDmaCycle = -1;
            _liveLastDisplayDmaCycle = -1;
            if (IsLiveCopperDmaEnabled())
            {
                _liveCopper = new CopperPresentationState(_copperListPointer, frameStartCycle);
            }
            else if (_liveCopper.Cycle < frameStartCycle)
            {
                _liveCopper.Cycle = frameStartCycle;
            }

            ResetLiveDisplayWindowStateTracking();
            InvalidateLiveDisplayEventCycle();
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
            }
        }

        private static bool IsLivePresentationReplayRegister(ushort offset)
        {
            return offset is 0x02E or
                0x080 or 0x082 or 0x084 or 0x086 or 0x088 or 0x08A or
                0x08E or 0x090 or 0x092 or 0x094 or 0x096 or
                0x100 or 0x102 or 0x104 or 0x108 or 0x10A ||
                (offset >= 0x0E0 && offset <= 0x0F6) ||
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

            CaptureDisplayState(_liveFrameInitialState);
            _liveFrameInitialStateValid = true;
            _liveFrameWrites.Clear();
            _liveFrameWriteOverflowed = false;
            _liveFrameHasLateDisplayWindowWrites = false;
            AdvanceLiveGeneration();
            _spriteFrameCommands.Clear();
            _livePaletteSnapshotCount = 0;
            _liveCurrentPaletteSnapshotIndex = -1;
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
                _liveLineStates[i].Generation = 0;
            }
        }

        private void InvalidateLiveDisplayEventCycle()
        {
            _liveNextDisplayEventValid = false;
            _liveNextDisplayEventCycle = long.MaxValue;
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

        private void AdvanceLiveDmaWithinFrame(long targetCycle)
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
                if (nextCycle > targetCycle)
                {
                    break;
                }

                AdvanceLiveDisplayStateTo(nextCycle);
                if (nextPendingWriteCycle == nextCycle)
                {
                    continue;
                }

                if (nextLineStateCycle == nextCycle)
                {
                    CaptureLiveLineState(_liveNextLineStateRow);
                    _liveNextLineStateRow++;
                    continue;
                }

                if (nextSpriteFetchCycle == nextCycle)
                {
                    CaptureLiveSpriteFetchBatch(targetCycle);
                    continue;
                }

                CaptureLiveBitplaneFetchBatch(targetCycle);
            }

            AdvanceLiveDisplayStateTo(targetCycle);
            _liveCycle = Math.Max(_liveCycle, targetCycle);
            _liveCapturedThroughCycle = Math.Max(_liveCapturedThroughCycle, targetCycle);
        }

        private void AdvanceLiveDisplayStateTo(long targetCycle)
        {
            targetCycle = Math.Max(_liveFrameStartCycle, targetCycle);
            while (true)
            {
                var nextCycle = GetNextLiveDisplayEventCycle();
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
            }
            finally
            {
                _currentRenderRow = previousRow;
                _currentCopperRow = previousCopperRow;
            }
        }

        private long GetNextLiveCopperCycle(long targetCycle)
        {
            if (_liveCopper.PendingMove)
            {
                return _liveCopper.PendingMoveCycle;
            }

            if (_liveCopper.Stopped)
            {
                return long.MaxValue;
            }

            if (_liveCopper.Pc == 0 && _copperListPointer == 0)
            {
                return long.MaxValue;
            }

            if (!IsLiveCopperDmaEnabled())
            {
                return long.MaxValue;
            }

            if (_liveCopper.Waiting)
            {
                var blitterReadyCycle = GetCopperBlitterReadyCycle(_liveCopper.WaitSecond, _liveCopper.Cycle);
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

        private void StepLiveCopper(long targetCycle)
        {
            if (_liveCopper.PendingMove)
            {
                CompletePendingLiveCopperMove(targetCycle);
                return;
            }

            if (_liveCopper.Stopped || !IsLiveCopperDmaEnabled())
            {
                return;
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

                if (!TryGetCopperWaitCycle(
                    _liveCopper.WaitFirst,
                    _liveCopper.WaitSecond,
                    _liveFrameStartCycle,
                    _liveCopper.Cycle,
                    targetCycle + 1,
                    blitterFinished: true,
                    out var waitCycle))
                {
                    _liveCopper.Cycle = targetCycle + 1;
                    return;
                }

                var resumeCycle = waitCycle + CopperHpToCpuCycles(CopperWaitWakeHpUnits);
                if (resumeCycle > targetCycle)
                {
                    _liveCopper.Cycle = resumeCycle;
                    _liveCopper.Waiting = false;
                    return;
                }

                _liveCopper.Cycle = resumeCycle;
                _liveCopper.Waiting = false;
                return;
            }

            var fetchCycle = Math.Min(_liveCopper.Cycle, targetCycle);
            var first = _bus.ReadLiveCopperDmaWord(_liveCopper.Pc, fetchCycle, out var firstAccess);
            fetchCycle = firstAccess.CompletedCycle;
            var dataRequestCycle = Math.Max(
                fetchCycle,
                firstAccess.RequestedCycle + CopperHpToCpuCycles(CopperInstructionDataHpUnits));
            var secondAddress = AddDmaPointerOffset(_liveCopper.Pc, 2);
            var second = _bus.ReadLiveCopperDmaWord(secondAddress, dataRequestCycle, out var secondAccess);
            var dataCycle = secondAccess.GrantedCycle;
            var instructionStopCycle = Math.Max(
                secondAccess.CompletedCycle,
                firstAccess.RequestedCycle + CopperHpToCpuCycles(CopperMoveHpUnits));
            _liveCopper.Pc = AddDmaPointerOffset(_liveCopper.Pc, 4);

            if (first == 0xFFFF && second == 0xFFFE)
            {
                _liveCopper.Stopped = true;
                _liveCopper.Cycle = instructionStopCycle;
                return;
            }

            if ((first & 1) == 0)
            {
                var register = (ushort)(first & 0x01FE);
                var suppressMove = _liveCopper.SuppressNextMove;
                _liveCopper.SuppressNextMove = false;
                if (dataCycle > targetCycle)
                {
                    _liveCopper.PendingMove = true;
                    _liveCopper.PendingMoveRegister = register;
                    _liveCopper.PendingMoveValue = second;
                    _liveCopper.PendingMoveCycle = dataCycle;
                    _liveCopper.PendingMoveStopCycle = instructionStopCycle;
                    _liveCopper.PendingMoveSuppress = suppressMove;
                    _liveCopper.Cycle = dataCycle;
                    InvalidateLiveDisplayEventCycle();
                    return;
                }

                if (dataCycle <= targetCycle)
                {
                    ApplyLiveCopperMove(register, second, dataCycle, instructionStopCycle, suppressMove);
                }

                _liveCopper.Cycle = instructionStopCycle;
                return;
            }

            if ((second & 1) == 0)
            {
                _liveCopper.Cycle = instructionStopCycle;
                _liveCopper.Wait(first, second);
                return;
            }

            if (IsCopperComparisonSatisfied(
                first,
                second,
                _liveFrameStartCycle,
                fetchCycle,
                IsCopperBlitterFinishedForWait(second)))
            {
                _liveCopper.SuppressNextMove = true;
            }

            _liveCopper.Cycle = instructionStopCycle;
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
                _currentCopperRow = GetOutputRowForCycle(_liveFrameStartCycle, dataCycle);
                AdvanceLiveDisplayWindowStateToCycle(dataCycle);
                if (dataCycle > _liveFrameStartCycle && register is 0x08E or 0x090)
                {
                    _liveFrameHasLateDisplayWindowWrites = true;
                }

                ApplyCopperMove(register, value, dataCycle);
                RecordLiveFrameWrite(dataCycle, register, value, isCopper: true);
                RefreshLiveLineStateAfterDisplayStateChange(dataCycle);
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
            if (state.ControlAddress == 0)
            {
                return false;
            }

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
            if (state.ControlAddress == 0)
            {
                return false;
            }

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

        private void CaptureLiveLineState(int row)
        {
            if ((uint)row >= (uint)LowResOutputHeight)
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
            state.PlaneCount = Math.Min((_bplcon0 >> 12) & 0x7, _bitplanePointers.Length);
            state.FetchWords = GetDataFetchWordCount();
            state.DataFetchStart = GetDataFetchStartValue();
            state.FetchSlotStride = GetBitplaneFetchSlotStride(IsHighResolutionEnabled());
            state.PaletteSnapshotIndex = CaptureLivePaletteSnapshot();
            Array.Copy(_bitplanePointers, state.BitplanePointers, _bitplanePointers.Length);
            Array.Copy(_bitplaneBaseRows, state.BitplaneBaseRows, _bitplaneBaseRows.Length);
            state.PlaneHasRowMask = 0;
            for (var plane = 0; plane < LiveBitplanePlaneCount; plane++)
            {
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
        }

        private void RefreshLiveLineStateAfterDisplayStateChange(long cycle)
        {
            if (!_liveFrameValid)
            {
                return;
            }

            var row = GetOutputRowForCycle(_liveFrameStartCycle, cycle);
            if ((uint)row >= (uint)LowResOutputHeight ||
                !IsLiveLineValid(row) ||
                HasCapturedLiveBitplaneWords(row))
            {
                return;
            }

            CaptureLiveLineState(row);
            if (_liveNextFetchRow >= row)
            {
                _liveNextFetchRow = row;
                _liveNextFetchWord = 0;
                _liveNextFetchPlane = 0;
                _liveNextFetchSlot = 0;
            }
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

        private void CaptureLiveBitplaneFetchBatch(long targetCycle)
        {
            while (_liveNextFetchRow < LowResOutputHeight)
            {
                if (!NormalizeLiveBitplaneFetchCursor())
                {
                    return;
                }

                var state = _liveLineStates[_liveNextFetchRow];
                var stopCycle = Math.Min(targetCycle, Math.Min(GetNextLiveDisplayEventCycle() - 1, GetNextLiveLineStateCycle() - 1));
                var fetchHorizontal = state.DataFetchStart + (_liveNextFetchWord * state.FetchSlotStride) + _liveNextFetchSlot;
                var fetchCycle = state.LineStartCycle + ((long)fetchHorizontal * CopperHpCycles);
                if (fetchCycle > stopCycle)
                {
                    return;
                }

                CaptureLiveBitplaneFetch(_liveNextFetchRow, _liveNextFetchPlane, _liveNextFetchWord, fetchCycle, state);
                AdvanceLiveFetchCursor();
            }
        }

        private void CaptureLiveSpriteFetchBatch(long targetCycle)
        {
            while (_liveNextSpriteRow < LowResOutputHeight)
            {
                SkipLiveSpriteSlotsWithoutFetches();
                if (_liveNextSpriteRow >= LowResOutputHeight ||
                    !IsLiveLineValid(_liveNextSpriteRow) ||
                    !IsSpriteDmaEnabled())
                {
                    return;
                }

                var stopCycle = Math.Min(targetCycle, Math.Min(GetNextLiveDisplayEventCycle() - 1, GetNextLiveLineStateCycle() - 1));
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
            ushort value = 0;
            if ((state.PlaneHasRowMask & (1 << plane)) != 0)
            {
                var address = unchecked(state.BitplaneRowAddresses[plane] + (uint)(word * 2));
                if (_bus.TryReadLiveBitplaneDmaWord(address, fetchCycle, out value, out var grantedCycle))
                {
                    _liveBitplaneDmaFetches++;
                    RecordLiveDisplayDmaCycle(grantedCycle);
                }
            }

            _liveBitplaneWords[GetLiveBitplaneWordIndex(row, plane, word)] = value;
            _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(row, plane)] |= 1UL << word;
            _liveFetchBatchWordCount++;
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
                return;
            }

            _liveNextFetchSlot = 0;
            _liveNextFetchPlane = 0;
            _liveNextFetchWord++;
            if (_liveNextFetchWord < state.FetchWords)
            {
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
                return;
            }

            _liveNextSpriteWord = 0;
            _liveNextSpriteIndex++;
            if (_liveNextSpriteIndex < _sprites.Length)
            {
                return;
            }

            _liveNextSpriteIndex = 0;
            _liveNextSpriteRow++;
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

        private bool TryRenderLiveCapturedFrame(Span<uint> bgra, long frameStartCycle, long frameStopCycle)
        {
            if (!_liveFrameValid ||
                _liveFrameStartCycle != frameStartCycle ||
                _liveCapturedThroughCycle < Math.Max(frameStartCycle, frameStopCycle - 1) ||
                !IsLiveCaptureCompleteForRendering(frameStopCycle))
            {
                return false;
            }

            var saved = SaveDisplayState();
            ResetFrameCounters();
            ResetPlayfieldPriorityMasks();
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

            try
            {
                if (!_liveFrameHasLateDisplayWindowWrites &&
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

                RestoreDisplayState(saved);
                _trackDisplayWindowState = savedTrackDisplayWindowState;
                _displayWindowVerticallyOpen = savedDisplayWindowVerticallyOpen;
                _displayWindowStateLine = savedDisplayWindowStateLine;
                RenderSprites(bgra);
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
                    continue;
                }

                ApplyLiveLineStateForRendering(state);
                _displayWindowVerticallyOpen = state.DisplayWindowVerticallyOpen;
                _displayWindowStateLine = StandardVStart + row + 1;
                CapturePaletteFrameSpans(row, row + 1, 0, AmigaConstants.PalLowResWidth);
                FillRows(bgra, row, row + 1);
                RenderBitplanes(bgra, row, row + 1);
            }
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
            var minimumInstructionCycles = Math.Max(1, CopperHpToCpuCycles(CopperMoveHpUnits));
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
            if ((waitSecond & 0x8000) != 0 || !_bus.Blitter.CaptureSnapshot().Busy)
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
            var fetchCycle = Math.Min(copper.Cycle, frameStopCycle);
            var first = ReadCopperWordForPresentation(copper.Pc, fetchCycle, out var firstAccess);
            fetchCycle = firstAccess.CompletedCycle;
            var dataRequestCycle = Math.Max(
                fetchCycle,
                firstAccess.RequestedCycle + CopperHpToCpuCycles(CopperInstructionDataHpUnits));
            var second = ReadCopperWordForPresentation(AddDmaPointerOffset(copper.Pc, 2), dataRequestCycle, out var secondAccess);
            var dataCycle = secondAccess.GrantedCycle;
            var instructionStopCycle = Math.Max(
                secondAccess.CompletedCycle,
                firstAccess.RequestedCycle + CopperHpToCpuCycles(CopperMoveHpUnits));
            copper.Pc = AddDmaPointerOffset(copper.Pc, 4);

            if (first == 0xFFFF && second == 0xFFFE)
            {
                copper.Stopped = true;
                return;
            }

            if ((first & 1) == 0)
            {
                var register = (ushort)(first & 0x01FE);
                var writePixelDelay = GetCopperWritePixelDelay(register);
                var clippedWritePixelDelay = dataCycle <= frameStopCycle ? writePixelDelay : 0;
                RenderPresentationSpan(
                    bgra,
                    frameStartCycle,
                    renderCursorCycle,
                    Math.Min(dataCycle, frameStopCycle),
                    useTimedWrites,
                    renderCursorPixelDelay,
                    clippedWritePixelDelay);
                renderCursorCycle = Math.Max(renderCursorCycle, Math.Min(dataCycle, frameStopCycle));
                renderCursorPixelDelay = clippedWritePixelDelay;
                if (dataCycle <= frameStopCycle)
                {
                    var suppressMove = copper.SuppressNextMove;
                    copper.SuppressNextMove = false;
                    if (IsCopperDangerStopRegister(register))
                    {
                        copper.Stopped = true;
                        copper.Cycle = instructionStopCycle;
                        return;
                    }

                    if (!suppressMove && CanCopperWriteRegister(register))
                    {
                        _currentCopperRow = GetOutputRowForCycle(frameStartCycle, dataCycle);
                        ApplyCopperMove(register, second, dataCycle);
                        if (register == 0x088)
                        {
                            copper.JumpTo(_copperListPointer, dataCycle);
                        }
                        else if (register == 0x08A)
                        {
                            copper.JumpTo(_copperListPointer2, dataCycle);
                        }
                    }
                }

                copper.Cycle = instructionStopCycle;
                return;
            }

            if ((second & 1) == 0)
            {
                copper.Cycle = instructionStopCycle;
                copper.Wait(first, second);
                return;
            }

            if (IsCopperComparisonSatisfied(
                first,
                second,
                frameStartCycle,
                fetchCycle,
                IsCopperBlitterFinishedForWait(second)))
            {
                copper.SuppressNextMove = true;
            }

            copper.Cycle = instructionStopCycle;
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
                var xStart = GetOutputXForCycle(frameStartCycle, segmentStart);
                if (fromPixelDelay != 0 && segmentStart == clippedStart && clippedStart == fromCycle)
                {
                    xStart = Math.Clamp(xStart + fromPixelDelay, 0, AmigaConstants.PalLowResWidth);
                }

                var xStop = segmentStop >= lineStop
                    ? AmigaConstants.PalLowResWidth
                    : GetOutputXForCycle(frameStartCycle, segmentStop);
                if (toPixelDelay != 0 &&
                    segmentStop == clippedStop &&
                    clippedStop == toCycle &&
                    segmentStop < lineStop)
                {
                    xStop = Math.Clamp(xStop + toPixelDelay, 0, AmigaConstants.PalLowResWidth);
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
            for (var line = startLine; line < AmigaConstants.A500PalRasterLines; line++)
            {
                var vertical = line & 0xFF;
                var horizontalStart = line == startLine ? startHorizontal : 0;
                int horizontal;
                if (vertical > targetVertical)
                {
                    horizontal = horizontalStart;
                }
                else if (vertical == targetVertical)
                {
                    horizontal = Math.Max(horizontalStart, targetHorizontal);
                    if ((horizontal & 1) != 0)
                    {
                        horizontal++;
                    }
                }
                else
                {
                    continue;
                }

                if (horizontal > LastCopperHorizontal)
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
            return Math.Max(1, hpUnits * CopperHpCycles);
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

        private static int GetCopperOutputX(int horizontal)
        {
            var expandedHorizontal = horizontal >= 0xE0
                ? horizontal + 0x100
                : horizontal;
            return Math.Clamp((expandedHorizontal - DefaultDdfStart) * 2, 0, AmigaConstants.PalLowResWidth);
        }

        private bool IsCopperBlitterFinishedForWait(ushort second)
        {
            return (second & 0x8000) != 0 || !_bus.Blitter.CaptureSnapshot().Busy;
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
            if (_renderingLiveCapture && TryReadLiveCapturedBitplaneWord(row, plane, word, out var captured))
            {
                return captured;
            }

            if (!_useTimedPresentationReads)
            {
                return _bus.ReadChipWordForPresentation(address);
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

            _lastBitplaneDmaFetches++;
            RecordDisplayDmaCycle(access.GrantedCycle);
            return value;
        }

        private bool TryReadSpriteWordForPresentation(
            uint address,
            int row,
            int spriteIndex,
            int word,
            out ushort value)
            => TryReadSpriteWordForPresentation(address, row, spriteIndex, word, out value, recordLiveCapture: false);

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
                value = _bus.ReadChipWordForPresentation(address);
                return true;
            }

            if (!IsSpriteDmaChannelAvailable(spriteIndex))
            {
                value = 0;
                RecordMissedSpriteDmaSlot(recordLiveCapture);
                return false;
            }

            var fetchCycle = GetSpriteDmaFetchCycle(row, spriteIndex, word);
            var alreadyCaptured = recordLiveCapture && _bus.IsHrmChipSlotReserved(fetchCycle);
            if (!_bus.TryReadDisplayDmaWordForPresentation(
                    AmigaBusRequester.Sprite,
                    AmigaBusAccessKind.Sprite,
                    address,
                    fetchCycle,
                    out value,
                    out var access))
            {
                value = 0;
                RecordMissedSpriteDmaSlot(recordLiveCapture);
                return false;
            }

            if (!alreadyCaptured)
            {
                RecordSpriteDmaFetch(access.GrantedCycle, recordLiveCapture);
            }

            if (recordLiveCapture)
            {
                StoreLiveCapturedSpriteWord(row, spriteIndex, word, value);
            }

            return true;
        }

        private static int GetBitplaneFetchSlotStride(bool highResolution)
            => highResolution ? 4 : 8;

        private static int GetBitplaneFetchSlot(int plane, int fetchSlotStride)
        {
            if (fetchSlotStride <= 4)
            {
                return Math.Clamp(plane, 0, fetchSlotStride - 1);
            }

            return (uint)plane < (uint)LowResBitplaneFetchSlotsByPlane.Length
                ? LowResBitplaneFetchSlotsByPlane[plane]
                : fetchSlotStride - 1;
        }

        private static bool TryGetBitplanePlaneForFetchSlot(int slot, int planeCount, int fetchSlotStride, out int plane)
        {
            if (fetchSlotStride <= 4)
            {
                plane = slot;
                return (uint)plane < (uint)planeCount;
            }

            for (var candidate = 0; candidate < planeCount && candidate < LowResBitplaneFetchSlotsByPlane.Length; candidate++)
            {
                if (LowResBitplaneFetchSlotsByPlane[candidate] == slot)
                {
                    plane = candidate;
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
            if ((uint)spriteIndex >= (uint)_liveSpriteDmaStates.Length ||
                !IsSpriteDmaChannelAvailable(spriteIndex))
            {
                return false;
            }

            if (_liveSpriteDmaExhausted[spriteIndex])
            {
                return false;
            }

            var state = _liveSpriteDmaStates[spriteIndex];
            if (state.ControlAddress == 0)
            {
                return false;
            }

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
            return TryReadSpriteWordForPresentation(address, row, spriteIndex, word, out _, recordLiveCapture: true) &&
                _bus.IsHrmChipSlotReserved(slotCycle);
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

            AppendUniqueSpriteFrameCommand(_spriteFrameCommands, new SpriteFrameCommand(spriteIndex, row, descriptor));
            state.Descriptor = descriptor;
            state.Active = true;
            state.LastVisibleStop = Math.Max(state.LastVisibleStop, descriptor.YStop);
            state.ControlAddress = nextControlAddress;
            state.ControlRow = Math.Clamp(descriptor.YStop + 1, 0, LowResOutputHeight);
            return slotGranted;
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

                ApplyWrite(write.Offset, write.Value, write.Cycle);
                RefreshLiveFrameInitialStateAfterFrameStartWrite(write.Cycle);
                if (_advancingLiveDma)
                {
                    RecordLiveFrameWrite(write.Cycle, write.Offset, write.Value);
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
                    _liveCopper = new CopperPresentationState(_copperListPointer, _liveFrameStartCycle);
                    InvalidateLiveDisplayEventCycle();
                }
            }
        }

        private static void ApplySetClear(ref ushort register, ushort value)
        {
            var mask = (ushort)(value & 0x7FFF);
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
            var mask = (ushort)(value & 0x7FFF);
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
                    var planeCount = Math.Min((_bplcon0 >> 12) & 0x7, _bitplaneBaseRows.Length);
                    SetBitplaneBaseRows(0, planeCount, GetBitplaneDmaEnableBaseRow(cycle));
                }

                return;
            }

            if (offset == 0x100)
            {
                var oldPlaneCount = Math.Min((_bplcon0 >> 12) & 0x7, _bitplaneBaseRows.Length);
                var newPlaneCount = Math.Min((value >> 12) & 0x7, _bitplaneBaseRows.Length);
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
                ApplySpriteWrite(offset, value);
            }
        }

        private void ApplyCopperMove(ushort offset, ushort value, long cycle)
        {
            ApplyWrite(offset, value, cycle);
            if (!HasCopperHardwareSideEffect(offset))
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
            AnchorActiveBitplanePointersToCurrentRow(Math.Min((_bplcon0 >> 12) & 0x7, _bitplaneBaseRows.Length));
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
            var planeCount = Math.Min((_bplcon0 >> 12) & 0x7, _bitplaneBaseRows.Length);
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

        private void SetBitplaneBaseRows(int startPlane, int endPlane, int row)
        {
            startPlane = Math.Clamp(startPlane, 0, _bitplaneBaseRows.Length);
            endPlane = Math.Clamp(endPlane, startPlane, _bitplaneBaseRows.Length);
            for (var i = startPlane; i < endPlane; i++)
            {
                _bitplaneBaseRows[i] = row;
            }
        }

        private void ApplySpriteWrite(ushort offset, ushort value)
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
                        StopManualSpriteFrameCommands(sprite);
                        _sprites[sprite].Pos = value;
                        CaptureManualSpriteFrameCommandIfArmed(sprite);
                        break;
                    case 2:
                        StopManualSpriteFrameCommands(sprite);
                        _sprites[sprite].Ctl = value;
                        _sprites[sprite].ManualArmed = false;
                        break;
                    case 4:
                        StopManualSpriteFrameCommands(sprite);
                        _sprites[sprite].DataA = value;
                        _sprites[sprite].ManualArmed = true;
                        CaptureManualSpriteFrameCommandIfArmed(sprite);
                        break;
                    case 6:
                        StopManualSpriteFrameCommands(sprite);
                        _sprites[sprite].DataB = value;
                        CaptureManualSpriteFrameCommandIfArmed(sprite);
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

            var planeCount = (_bplcon0 >> 12) & 0x7;
            if (planeCount == 0)
            {
                return;
            }

            if (_enforceDmaForFrame && (_dmacon & (DmaconMasterEnable | DmaconBitplaneEnable)) != (DmaconMasterEnable | DmaconBitplaneEnable))
            {
                return;
            }

            planeCount = Math.Min(planeCount, _bitplanePointers.Length);
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
            for (var y = rowStart; y < rowStop; y++)
            {
                _lastBitplaneRows++;
                for (var plane = 0; plane < planeCount; plane++)
                {
                    var mod = (plane & 1) == 0 ? _bpl1mod : _bpl2mod;
                    var rowStride = (fetchWords * 2) + mod;
                    var displaySourceY = y - _bitplaneBaseRows[plane];
                    var planeSourceY = displaySourceY;
                    var liveCapturedMask = _renderingLiveCapture
                        ? _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(y, plane)]
                        : 0UL;
                    planeHasRow[plane] = displaySourceY >= 0 || liveCapturedMask != 0;
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
                            _lastBitplaneWords++;
                            continue;
                        }

                        var address = unchecked(rowAddress + (uint)(word * 2));
                        planeWords[plane, word] = ReadBitplaneWordForPresentation(address, y, plane, word);
                        _lastBitplaneWords++;
                    }
                }

                var xStart = Math.Max(clipLeft, Math.Max(0, originX));
                var xStop = Math.Min(clipRight, Math.Min(AmigaConstants.PalLowResWidth, originX + drawPixels + (highResolution ? 8 : 16)));
                var hamColor = _colors[0];
                if (!highResolution && !dualPlayfield && !holdAndModify && zeroScroll)
                {
                    for (var x = xStart; x < xStop; x++)
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
                        var colorIndex = 0;
                        for (var plane = 0; plane < planeCount; plane++)
                        {
                            if (planeHasRow[plane] && (planeWords[plane, word] & mask) != 0)
                            {
                                colorIndex |= 1 << plane;
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
                var visibleWords = ((Math.Max(1, GetDisplayWindow().Width) * 2) + 15) / 16;
                return Math.Clamp(Math.Max(fetchWords, visibleWords), 0, MaxBitplaneFetchWords);
            }

            return Math.Clamp(((ddfStop - ddfStart) / 8) + 1, 0, MaxBitplaneFetchWords);
        }

        private bool IsHighResolutionEnabled()
        {
            return (_bplcon0 & 0x8000) != 0;
        }

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
            return _ddfStop & (IsHighResolutionEnabled() ? 0x00FC : 0x00F8);
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
                    if (!oddSprite.Descriptor.Attached)
                    {
                        continue;
                    }

                    var evenIndex = FindAttachedEvenSprite(evenSprites, _evenSpriteAttached, oddSprite);
                    if (evenIndex < 0)
                    {
                        _oddSpriteAttached[oddIndex] = true;
                        RenderAttachedOddSpriteWithoutEvenPartner(bgra, spriteIndex, oddSprite);
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

            var sprite = _sprites[spriteIndex];
            var allowStateFallback = !_renderingLiveCapture &&
                (!_useTimedPresentationReads || !_bus.LiveAgnusDmaEnabled);
            if (commands.Count == 0 && allowStateFallback && IsSpriteDmaEnabled() && sprite.Pointer != 0)
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

        private static void AppendUniqueSpriteFrameCommand(List<SpriteFrameCommand> commands, SpriteFrameCommand command)
        {
            if (commands.Count >= commands.Capacity)
            {
                return;
            }

            for (var i = commands.Count - 1; i >= 0; i--)
            {
                if (commands[i].HasSameRenderingAs(command))
                {
                    return;
                }
            }

            commands.Add(command);
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
                if (evenAttached[i] || !SpritesOverlapVertically(evenSprites[i].Descriptor, oddSprite.Descriptor))
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
            if (sprite.Pointer == 0)
            {
                return;
            }

            if (!TryGetCurrentOutputRow(out var row))
            {
                row = 0;
            }

            AppendDmaSpriteFrameCommands(_spriteFrameCommands, spriteIndex, sprite.Pointer, row);
        }

        private void CaptureManualSpriteFrameCommandIfArmed(int spriteIndex)
        {
            if (!_captureSpriteFrameCommands)
            {
                return;
            }

            var sprite = _sprites[spriteIndex];
            if (!sprite.ManualArmed || (sprite.Pos | sprite.Ctl | sprite.DataA | sprite.DataB) == 0)
            {
                return;
            }

            AddSpriteFrameCommand(spriteIndex, CreateManualSpriteDescriptor(sprite));
        }

        private static SpriteDescriptor CreateManualSpriteDescriptor(SpriteState sprite)
        {
            var baseDescriptor = CreateSpriteDescriptor(sprite.Pos, sprite.Ctl, 0, isDma: false, sprite.DataA, sprite.DataB);
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

        private void StopManualSpriteFrameCommands(int spriteIndex)
        {
            if (!_captureSpriteFrameCommands)
            {
                return;
            }

            if (!TryGetCurrentOutputRow(out var row))
            {
                row = 0;
            }

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

        private void AddSpriteFrameCommand(int spriteIndex, SpriteDescriptor descriptor)
        {
            if (_spriteFrameCommands.Count >= MaxSpriteFrameCommands * _sprites.Length)
            {
                return;
            }

            if (!TryGetCurrentOutputRow(out var row))
            {
                row = 0;
            }

            var command = new SpriteFrameCommand(spriteIndex, row, descriptor);
            if (_spriteFrameCommands.Count > 0 &&
                _spriteFrameCommands[_spriteFrameCommands.Count - 1].HasSameRenderingAs(command))
            {
                return;
            }

            _spriteFrameCommands.Add(command);
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

        private bool IsSpriteDmaChannelAvailable(int spriteIndex)
        {
            return spriteIndex < GetUsableSpriteDmaChannelCount();
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

        private bool IsBitplaneDmaEnabledForRendering()
        {
            return !_enforceDmaForFrame || IsBitplaneDmaEnabled(_dmacon);
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
                if (TryReadSpriteWordForPresentation(address, y, spriteIndex, 0, out var dataA) &&
                    TryReadSpriteWordForPresentation(AddDmaPointerOffset(address, 2), y, spriteIndex, 1, out var dataB))
                {
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, dataA, dataB);
                }

                address = AddDmaPointerOffset(address, 4);
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

        private (ushort DataA, ushort DataB) ReadSpriteLine(SpriteFrameCommand command, int y)
        {
            var sprite = command.Descriptor;
            if (y < Math.Max(sprite.YStart, command.Row) || y >= sprite.YStop)
            {
                return ((ushort)0, (ushort)0);
            }

            if (!sprite.IsDma)
            {
                return y == sprite.YStart ? (sprite.ManualDataA, sprite.ManualDataB) : ((ushort)0, (ushort)0);
            }

            var address = AddDmaPointerOffset(sprite.DataAddress, (y - sprite.YStart) * 4);
            if (!TryReadSpriteWordForPresentation(address, y, command.SpriteIndex, 0, out var dataA) ||
                !TryReadSpriteWordForPresentation(AddDmaPointerOffset(address, 2), y, command.SpriteIndex, 1, out var dataB))
            {
                return ((ushort)0, (ushort)0);
            }

            return (dataA, dataB);
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
            public CopperPresentationState(uint pc, long cycle)
            {
                Pc = pc;
                Cycle = cycle;
                Stopped = false;
                Waiting = false;
                SuppressNextMove = false;
                PendingMove = false;
                PendingMoveRegister = 0;
                PendingMoveValue = 0;
                PendingMoveCycle = 0;
                PendingMoveStopCycle = 0;
                PendingMoveSuppress = false;
                WaitFirst = 0;
                WaitSecond = 0;
            }

            public uint Pc;

            public long Cycle;

            public bool Stopped;

            public bool Waiting;

            public bool SuppressNextMove;

            public bool PendingMove;

            public ushort PendingMoveRegister;

            public ushort PendingMoveValue;

            public long PendingMoveCycle;

            public long PendingMoveStopCycle;

            public bool PendingMoveSuppress;

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
                SuppressNextMove = false;
                PendingMove = false;
                PendingMoveRegister = 0;
                PendingMoveValue = 0;
                PendingMoveCycle = 0;
                PendingMoveStopCycle = 0;
                PendingMoveSuppress = false;
            }
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
                    ManualDataA == other.ManualDataA &&
                    ManualDataB == other.ManualDataB;
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
            public int FetchWords;
            public int DataFetchStart;
            public int FetchSlotStride;
            public int PaletteSnapshotIndex;
            public byte PlaneHasRowMask;
            public readonly uint[] BitplanePointers = new uint[6];
            public readonly int[] BitplaneBaseRows = new int[6];
            public readonly uint[] BitplaneRowAddresses = new uint[6];
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
            int[] bitplaneColorCounts)
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
    }

    internal sealed class AmigaCopper
    {
        private const ushort CopconCopperDanger = 0x0002;

        public void ExecuteList(AmigaBus bus, uint listAddress, int maxInstructions = 1024, Action<ushort, ushort>? onMove = null)
        {
            ArgumentNullException.ThrowIfNull(bus);
            var pc = listAddress;
            ushort copcon = 0;
            var suppressNextMove = false;
            for (var i = 0; i < maxInstructions; i++)
            {
                var first = bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc, 0);
                var second = bus.ReadChipWordForDevice(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, pc + 2, 0);
                pc += 4;
                if (first == 0xFFFF && second == 0xFFFE)
                {
                    return;
                }

                if ((first & 1) == 0)
                {
                    var register = (ushort)(first & 0x01FE);
                    var suppressMove = suppressNextMove;
                    suppressNextMove = false;
                    if (IsCopperDangerStopRegister(register, copcon))
                    {
                        return;
                    }

                    if (suppressMove)
                    {
                        continue;
                    }

                    if (!CanCopperWriteRegister(register, copcon))
                    {
                        continue;
                    }

                    if (register == 0x02E)
                    {
                        copcon = second;
                    }

                    onMove?.Invoke(register, second);
                    bus.WriteDeviceWord(AmigaBusRequester.Copper, AmigaBusAccessKind.Copper, 0x00DFF000u + register, second, 0);
                    continue;
                }

                if ((second & 1) != 0)
                {
                    suppressNextMove = IsCopperComparisonSatisfiedAtResetBeam(first, second);
                    continue;
                }
            }
        }

        private static bool CanCopperWriteRegister(ushort offset, ushort copcon)
        {
            if (offset < 0x010)
            {
                return false;
            }

            return offset >= 0x020 || (copcon & CopconCopperDanger) != 0;
        }

        private static bool IsCopperDangerStopRegister(ushort offset, ushort copcon)
        {
            if (offset < 0x010)
            {
                return true;
            }

            return offset < 0x020 && (copcon & CopconCopperDanger) == 0;
        }

        private static bool IsCopperComparisonSatisfiedAtResetBeam(ushort first, ushort second)
        {
            var mask = (ushort)(0x8000 | (second & 0x7FFE));
            var target = (ushort)(first & 0xFFFE);
            return (0 & mask) >= (target & mask);
        }
    }

    internal readonly struct BlitterSpecializationCounters
    {
        public BlitterSpecializationCounters(
            long kernelHits,
            long kernelMisses,
            long generatedKernels,
            long scalarFallbacks)
        {
            KernelHits = kernelHits;
            KernelMisses = kernelMisses;
            GeneratedKernels = generatedKernels;
            ScalarFallbacks = scalarFallbacks;
        }

        public long KernelHits { get; }

        public long KernelMisses { get; }

        public long GeneratedKernels { get; }

        public long ScalarFallbacks { get; }
    }

    internal readonly struct BlitterKernelKey : IEquatable<BlitterKernelKey>
    {
        public BlitterKernelKey(
            bool lineMode,
            bool useA,
            bool useB,
            bool useC,
            bool useD,
            byte minterm,
            int shiftA,
            int shiftB,
            bool descending,
            bool fillEnabled,
            bool fillExclusive,
            bool lineSingleDot,
            bool lineSud,
            bool lineSul,
            bool lineAul,
            bool lineSign)
        {
            LineMode = lineMode;
            UseA = useA;
            UseB = useB;
            UseC = useC;
            UseD = useD;
            Minterm = minterm;
            ShiftA = shiftA;
            ShiftB = shiftB;
            Descending = descending;
            FillEnabled = fillEnabled;
            FillExclusive = fillExclusive;
            LineSingleDot = lineSingleDot;
            LineSud = lineSud;
            LineSul = lineSul;
            LineAul = lineAul;
            LineSign = lineSign;
        }

        public bool LineMode { get; }

        public bool UseA { get; }

        public bool UseB { get; }

        public bool UseC { get; }

        public bool UseD { get; }

        public byte Minterm { get; }

        public int ShiftA { get; }

        public int ShiftB { get; }

        public bool Descending { get; }

        public bool FillEnabled { get; }

        public bool FillExclusive { get; }

        public bool LineSingleDot { get; }

        public bool LineSud { get; }

        public bool LineSul { get; }

        public bool LineAul { get; }

        public bool LineSign { get; }

        public bool Equals(BlitterKernelKey other)
            => LineMode == other.LineMode &&
                UseA == other.UseA &&
                UseB == other.UseB &&
                UseC == other.UseC &&
                UseD == other.UseD &&
                Minterm == other.Minterm &&
                ShiftA == other.ShiftA &&
                ShiftB == other.ShiftB &&
                Descending == other.Descending &&
                FillEnabled == other.FillEnabled &&
                FillExclusive == other.FillExclusive &&
                LineSingleDot == other.LineSingleDot &&
                LineSud == other.LineSud &&
                LineSul == other.LineSul &&
                LineAul == other.LineAul &&
                LineSign == other.LineSign;

        public override bool Equals(object? obj)
            => obj is BlitterKernelKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            hash = (hash * 31) + (LineMode ? 1 : 0);
            hash = (hash * 31) + (UseA ? 1 : 0);
            hash = (hash * 31) + (UseB ? 1 : 0);
            hash = (hash * 31) + (UseC ? 1 : 0);
            hash = (hash * 31) + (UseD ? 1 : 0);
            hash = (hash * 31) + Minterm;
            hash = (hash * 31) + ShiftA;
            hash = (hash * 31) + ShiftB;
            hash = (hash * 31) + (Descending ? 1 : 0);
            hash = (hash * 31) + (FillEnabled ? 1 : 0);
            hash = (hash * 31) + (FillExclusive ? 1 : 0);
            hash = (hash * 31) + (LineSingleDot ? 1 : 0);
            hash = (hash * 31) + (LineSud ? 1 : 0);
            hash = (hash * 31) + (LineSul ? 1 : 0);
            hash = (hash * 31) + (LineAul ? 1 : 0);
            hash = (hash * 31) + (LineSign ? 1 : 0);
            return hash;
        }
    }

    internal struct BlitterAreaKernelState
    {
        public ushort PreviousA;

        public ushort PreviousB;

        public bool FillCarry;
    }

    internal static class BlitterKernelMath
    {
        public static ushort ExecuteArea(
            BlitterKernelKey key,
            ref BlitterAreaKernelState state,
            ushort rawA,
            ushort rawB,
            ushort rawC,
            ushort mask)
        {
            rawA = (ushort)(rawA & mask);
            var sourceA = ShiftSource(rawA, ref state.PreviousA, key.ShiftA, key.Descending);
            var sourceB = ShiftSource(rawB, ref state.PreviousB, key.ShiftB, key.Descending);
            var output = ApplyMinterm(key.Minterm, sourceA, sourceB, rawC);
            return key.FillEnabled
                ? ApplyFill(output, key.FillExclusive, ref state.FillCarry)
                : output;
        }

        public static ushort ExecuteLine(BlitterKernelKey key, ushort lineMask, ushort texture, ushort sourceC)
            => ApplyMinterm(key.Minterm, lineMask, texture, sourceC);

        public static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
        {
            uint result = 0;
            var notA = (ushort)~sourceA;
            var notB = (ushort)~sourceB;
            var notC = (ushort)~sourceC;
            if ((minterm & 0x01) != 0)
            {
                result |= (uint)(notA & notB & notC);
            }

            if ((minterm & 0x02) != 0)
            {
                result |= (uint)(notA & notB & sourceC);
            }

            if ((minterm & 0x04) != 0)
            {
                result |= (uint)(notA & sourceB & notC);
            }

            if ((minterm & 0x08) != 0)
            {
                result |= (uint)(notA & sourceB & sourceC);
            }

            if ((minterm & 0x10) != 0)
            {
                result |= (uint)(sourceA & notB & notC);
            }

            if ((minterm & 0x20) != 0)
            {
                result |= (uint)(sourceA & notB & sourceC);
            }

            if ((minterm & 0x40) != 0)
            {
                result |= (uint)(sourceA & sourceB & notC);
            }

            if ((minterm & 0x80) != 0)
            {
                result |= (uint)(sourceA & sourceB & sourceC);
            }

            return (ushort)result;
        }

        public static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
        {
            shift &= 0x0F;
            if (shift == 0)
            {
                previous = current;
                return current;
            }

            uint combined = descending
                ? ((uint)current << 16) | previous
                : ((uint)previous << 16) | current;
            var value = descending
                ? (ushort)(combined >> (16 - shift))
                : (ushort)(combined >> shift);
            previous = current;
            return value;
        }

        public static ushort ApplyFill(ushort value, bool exclusive, ref bool fillCarry)
        {
            ushort output = 0;
            for (var bit = 0; bit < 16; bit++)
            {
                var mask = (ushort)(1 << bit);
                var input = (value & mask) != 0;
                if (exclusive)
                {
                    if (fillCarry)
                    {
                        output |= mask;
                    }

                    if (input)
                    {
                        fillCarry = !fillCarry;
                    }

                    continue;
                }

                if (fillCarry || input)
                {
                    output |= mask;
                }

                if (input)
                {
                    fillCarry = !fillCarry;
                }
            }

            return output;
        }
    }

    internal readonly struct AmigaBlitterSnapshot
    {
        public AmigaBlitterSnapshot(
            bool busy,
            bool zero,
            long currentCycle,
            uint sourceA,
            uint sourceB,
            uint sourceC,
            uint destinationD,
            int widthWords,
            int height,
            int wordX,
            int rowY,
            bool lineMode,
            long nextDmaCycle,
            long lastDmaCycle,
            int completedMicroOps,
            BlitterSpecializationCounters specializationCounters)
        {
            Busy = busy;
            Zero = zero;
            CurrentCycle = currentCycle;
            SourceA = sourceA;
            SourceB = sourceB;
            SourceC = sourceC;
            DestinationD = destinationD;
            WidthWords = widthWords;
            Height = height;
            WordX = wordX;
            RowY = rowY;
            LineMode = lineMode;
            NextDmaCycle = nextDmaCycle;
            LastDmaCycle = lastDmaCycle;
            CompletedMicroOps = completedMicroOps;
            SpecializationCounters = specializationCounters;
        }

        public bool Busy { get; }

        public bool Zero { get; }

        public long CurrentCycle { get; }

        public uint SourceA { get; }

        public uint SourceB { get; }

        public uint SourceC { get; }

        public uint DestinationD { get; }

        public int WidthWords { get; }

        public int Height { get; }

        public int WordX { get; }

        public int RowY { get; }

        public bool LineMode { get; }

        public long NextDmaCycle { get; }

        public long LastDmaCycle { get; }

        public int CompletedMicroOps { get; }

        public BlitterSpecializationCounters SpecializationCounters { get; }
    }

    internal readonly struct BlitterCompiledKernel
    {
        public BlitterCompiledKernel(BlitterKernelKey key, bool supported)
        {
            Key = key;
            Supported = supported;
        }

        public BlitterKernelKey Key { get; }

        public bool Supported { get; }

        public bool SupportsArea => Supported && !Key.LineMode;

        public bool SupportsLine => Supported && Key.LineMode;

        public ushort ExecuteArea(
            ref BlitterAreaKernelState state,
            ushort rawA,
            ushort rawB,
            ushort rawC,
            ushort mask)
            => BlitterKernelMath.ExecuteArea(Key, ref state, rawA, rawB, rawC, mask);

        public ushort ExecuteLine(ushort lineMask, ushort texture, ushort sourceC)
            => BlitterKernelMath.ExecuteLine(Key, lineMask, texture, sourceC);
    }

    internal sealed class BlitterKernelCache
    {
        private const int CacheCapacity = 1024;

        private readonly BlitterKernelKey[] _keys = new BlitterKernelKey[CacheCapacity];
        private readonly BlitterCompiledKernel[] _kernels = new BlitterCompiledKernel[CacheCapacity];
        private readonly bool[] _valid = new bool[CacheCapacity];
        private long _kernelHits;
        private long _kernelMisses;
        private long _generatedKernels;
        private long _scalarFallbacks;
        private int _count;

        public BlitterCompiledKernel GetOrCreate(BlitterKernelKey key)
        {
            for (var index = 0; index < _count; index++)
            {
                if (_valid[index] && _keys[index].Equals(key))
                {
                    _kernelHits++;
                    return _kernels[index];
                }
            }

            _kernelMisses++;
            var kernel = CreateKernel(key);
            if (!kernel.Supported)
            {
                _scalarFallbacks++;
                return kernel;
            }

            _generatedKernels++;
            if (_count >= CacheCapacity)
            {
                _scalarFallbacks++;
                return default;
            }

            _keys[_count] = key;
            _kernels[_count] = kernel;
            _valid[_count] = true;
            _count++;
            return kernel;
        }

        public void RecordFallback()
        {
            _scalarFallbacks++;
        }

        public BlitterSpecializationCounters CaptureCounters()
            => new BlitterSpecializationCounters(_kernelHits, _kernelMisses, _generatedKernels, _scalarFallbacks);

        private static BlitterCompiledKernel CreateKernel(BlitterKernelKey key)
            => new BlitterCompiledKernel(key, supported: true);
    }

    internal sealed class AmigaBlitter
    {
        private const ushort DmaMasterEnable = 0x0200;
        private const ushort DmaBlitterEnable = 0x0040;
        private const ushort DmaBlitterNasty = 0x0400;
        private const ushort DmaconBlitterZero = 0x2000;
        private const ushort DmaconBlitterBusy = 0x4000;
        private const ushort Bltcon1LineMode = 0x0001;
        private const ushort Bltcon1SingleDot = 0x0002;
        private const ushort Bltcon1Descending = 0x0002;
        private const ushort Bltcon1FillCarryIn = 0x0004;
        private const ushort Bltcon1InclusiveFill = 0x0008;
        private const ushort Bltcon1ExclusiveFill = 0x0010;
        private const ushort Bltcon1LineSud = 0x0010;
        private const ushort Bltcon1LineSul = 0x0008;
        private const ushort Bltcon1LineAul = 0x0004;
        private const ushort Bltcon1LineSign = 0x0040;
        private const int ChipSlotCycles = AgnusChipSlotScheduler.SlotCycles;

        private readonly AmigaBus _bus;
        private readonly bool _specializationEnabled;
        private readonly BlitterKernelCache _kernelCache = new BlitterKernelCache();
        private uint _sourceA;
        private uint _sourceB;
        private uint _sourceC;
        private uint _destinationD;
        private short _sourceAModulo;
        private short _sourceBModulo;
        private short _sourceCModulo;
        private short _destinationDModulo;
        private ushort _bltcon0;
        private ushort _bltcon1;
        private ushort _firstWordMask = 0xFFFF;
        private ushort _lastWordMask = 0xFFFF;
        private ushort _dataA;
        private ushort _dataB;
        private ushort _dataC;
        private ushort _activeFirstWordMask = 0xFFFF;
        private ushort _activeLastWordMask = 0xFFFF;
        private ushort _activeDataA;
        private ushort _activeDataB;
        private ushort _activeDataC;
        private int _dataAShift;
        private int _dataBShift;
        private int _activeDataAShift;
        private int _activeDataBShift;
        private short _activeSourceAModulo;
        private short _activeSourceBModulo;
        private short _activeSourceCModulo;
        private short _activeDestinationDModulo;
        private bool _busy;
        private bool _zeroFlag = true;
        private long _currentCycle;
        private bool _useA;
        private bool _useB;
        private bool _useC;
        private bool _useD;
        private byte _minterm;
        private int _shiftA;
        private int _shiftB;
        private bool _lineMode;
        private bool _descending;
        private int _step;
        private int _widthWords;
        private int _height;
        private int _wordX;
        private int _rowY;
        private uint _workSourceA;
        private uint _workSourceB;
        private uint _workSourceC;
        private uint _workDestinationD;
        private ushort _previousA;
        private ushort _previousB;
        private bool _fillEnabled;
        private bool _fillExclusive;
        private bool _fillCarryInitial;
        private bool _fillCarry;
        private int _lineIndex;
        private int _lineLength;
        private int _lineBit;
        private int _lineY;
        private int _lineLastDrawnY;
        private bool _lineSingleDot;
        private bool _lineSud;
        private bool _lineSul;
        private bool _lineAul;
        private bool _lineSign;
        private int _lineError;
        private int _lineSourceRowStride;
        private int _lineDestinationRowStride;
        private int _lineBPatternStride;
        private long _lastDmaCycle;
        private int _completedMicroOps;
        private bool _completionPending;
        private BlitterCompiledKernel _activeKernel;
        private BlitterAreaKernelState _areaKernelState;

        public AmigaBlitter(AmigaBus bus, bool enableSpecialization = false)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _specializationEnabled = enableSpecialization;
        }

        public ushort DmaconStatusBits
        {
            get
            {
                var status = _zeroFlag ? DmaconBlitterZero : (ushort)0;
                if (_busy)
                {
                    status |= DmaconBlitterBusy;
                }

                return status;
            }
        }

        public void Reset()
        {
            _sourceA = 0;
            _sourceB = 0;
            _sourceC = 0;
            _destinationD = 0;
            _sourceAModulo = 0;
            _sourceBModulo = 0;
            _sourceCModulo = 0;
            _destinationDModulo = 0;
            _bltcon0 = 0;
            _bltcon1 = 0;
            _firstWordMask = 0xFFFF;
            _lastWordMask = 0xFFFF;
            _dataA = 0;
            _dataB = 0;
            _dataC = 0;
            _activeFirstWordMask = 0xFFFF;
            _activeLastWordMask = 0xFFFF;
            _activeDataA = 0;
            _activeDataB = 0;
            _activeDataC = 0;
            _dataAShift = 0;
            _dataBShift = 0;
            _activeDataAShift = 0;
            _activeDataBShift = 0;
            _activeSourceAModulo = 0;
            _activeSourceBModulo = 0;
            _activeSourceCModulo = 0;
            _activeDestinationDModulo = 0;
            _busy = false;
            _zeroFlag = true;
            _currentCycle = 0;
            _useA = false;
            _useB = false;
            _useC = false;
            _useD = false;
            _minterm = 0;
            _shiftA = 0;
            _shiftB = 0;
            _lineMode = false;
            _descending = false;
            _step = 2;
            _widthWords = 0;
            _height = 0;
            _wordX = 0;
            _rowY = 0;
            _workSourceA = 0;
            _workSourceB = 0;
            _workSourceC = 0;
            _workDestinationD = 0;
            _previousA = 0;
            _previousB = 0;
            _fillEnabled = false;
            _fillExclusive = false;
            _fillCarryInitial = false;
            _fillCarry = false;
            _lineIndex = 0;
            _lineLength = 0;
            _lineBit = 0;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineSingleDot = false;
            _lineSud = false;
            _lineSul = false;
            _lineAul = false;
            _lineSign = false;
            _lineError = 0;
            _lineSourceRowStride = 0;
            _lineDestinationRowStride = 0;
            _lineBPatternStride = 0;
            _lastDmaCycle = 0;
            _completedMicroOps = 0;
            _completionPending = false;
            _activeKernel = default;
            _areaKernelState = default;
        }

        public AmigaBlitterSnapshot CaptureSnapshot()
        {
            return new AmigaBlitterSnapshot(
                _busy,
                _zeroFlag,
                _currentCycle,
                _lineMode ? _sourceA : _workSourceA,
                _workSourceB,
                _workSourceC,
                _lineMode ? _destinationD : _workDestinationD,
                _widthWords,
                _height,
                _wordX,
                _rowY,
                _lineMode,
                _currentCycle,
                _lastDmaCycle,
                _completedMicroOps,
                _kernelCache.CaptureCounters());
        }

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            switch (offset)
            {
                case 0x040:
                    _bltcon0 = value;
                    break;
                case 0x042:
                    _bltcon1 = value;
                    break;
                case 0x044:
                    _firstWordMask = value;
                    break;
                case 0x046:
                    _lastWordMask = value;
                    break;
                case 0x048:
                    _sourceC = _bus.WriteChipDmaPointerHigh(_sourceC, value);
                    break;
                case 0x04A:
                    _sourceC = _bus.WriteChipDmaPointerLow(_sourceC, value);
                    break;
                case 0x04C:
                    _sourceB = _bus.WriteChipDmaPointerHigh(_sourceB, value);
                    break;
                case 0x04E:
                    _sourceB = _bus.WriteChipDmaPointerLow(_sourceB, value);
                    break;
                case 0x050:
                    _sourceA = _bus.WriteChipDmaPointerHigh(_sourceA, value);
                    break;
                case 0x052:
                    _sourceA = _bus.WriteChipDmaPointerLow(_sourceA, value);
                    break;
                case 0x054:
                    _destinationD = _bus.WriteChipDmaPointerHigh(_destinationD, value);
                    break;
                case 0x056:
                    _destinationD = _bus.WriteChipDmaPointerLow(_destinationD, value);
                    break;
                case 0x058:
                    StartBlit(value, cycle);
                    break;
                case 0x060:
                    _sourceCModulo = unchecked((short)value);
                    break;
                case 0x062:
                    _sourceBModulo = unchecked((short)value);
                    break;
                case 0x064:
                    _sourceAModulo = unchecked((short)value);
                    break;
                case 0x066:
                    _destinationDModulo = unchecked((short)value);
                    break;
                case 0x070:
                    _dataC = value;
                    break;
                case 0x072:
                    _dataB = value;
                    _dataBShift = (_bltcon1 >> 12) & 0x0F;
                    break;
                case 0x074:
                    _dataA = value;
                    _dataAShift = (_bltcon0 >> 12) & 0x0F;
                    break;
            }
        }

        public void AdvanceTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (!_busy)
            {
                _currentCycle = Math.Max(_currentCycle, targetCycle);
                return;
            }

            while (_busy)
            {
                if (_completionPending)
                {
                    if (_currentCycle > targetCycle)
                    {
                        return;
                    }

                    FinalizePendingCompletion();
                    continue;
                }

                if (!IsBlitterDmaEnabled())
                {
                    _currentCycle = Math.Max(_currentCycle, targetCycle);
                    return;
                }

                var stepEndCycle = GetCurrentStepEndCycle();
                if (stepEndCycle > targetCycle)
                {
                    return;
                }

                if (_lineMode)
                {
                    StepLinePixel(targetCycle);
                }
                else
                {
                    StepAreaWord(targetCycle);
                }
            }
        }

        public long GetPredictedCompletionCycle()
        {
            if (!_busy)
            {
                return _currentCycle;
            }

            if (_completionPending)
            {
                return _currentCycle;
            }

            if (!IsBlitterDmaEnabled())
            {
                return long.MaxValue;
            }

            if (_lineMode)
            {
                var remainingPixels = Math.Max(0, _lineLength - _lineIndex);
                return _currentCycle + ((long)remainingPixels * GetLinePixelCycles());
            }

            var remainingWords = Math.Max(0, ((_height - _rowY - 1) * _widthWords) + (_widthWords - _wordX));
            return _currentCycle + ((long)remainingWords * GetAreaWordCycles());
        }

        public long? GetNextWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (!_busy || targetCycle <= currentCycle)
            {
                return null;
            }

            var completionCycle = GetPredictedCompletionCycle();
            if (completionCycle == long.MaxValue)
            {
                return null;
            }

            if (completionCycle > targetCycle)
            {
                return null;
            }

            return completionCycle <= currentCycle ? currentCycle + 1 : completionCycle;
        }

        public long AdvanceThroughCpuStall(long requestedCycle)
        {
            if (!ShouldStallCpu())
            {
                return requestedCycle;
            }

            var releaseCycle = GetCurrentStepEndCycle();
            AdvanceTo(releaseCycle);
            return Math.Max(requestedCycle, _currentCycle);
        }

        private void StartBlit(ushort bltsize, long cycle)
        {
            AdvanceTo(cycle);
            if (_busy)
            {
                return;
            }

            DecodeCommonRegisters();
            _lineMode = (_bltcon1 & Bltcon1LineMode) != 0;
            _zeroFlag = true;
            _busy = true;
            _completionPending = false;
            _lastDmaCycle = 0;
            _completedMicroOps = 0;
            _currentCycle = _bus.NextChipSlotCycle(Math.Max(_currentCycle, Math.Max(0, cycle)) + ChipSlotCycles);
            _previousA = 0;
            if (_useB)
            {
                _previousB = 0;
            }

            if (_lineMode)
            {
                StartLineBlit(bltsize);
                _activeKernel = _specializationEnabled
                    ? _kernelCache.GetOrCreate(CreateLineKernelKey())
                    : default;
                return;
            }

            _widthWords = bltsize & 0x3F;
            if (_widthWords == 0)
            {
                _widthWords = 64;
            }

            _height = (bltsize >> 6) & 0x03FF;
            if (_height == 0)
            {
                _height = 1024;
            }

            _wordX = 0;
            _rowY = 0;
            _descending = (_bltcon1 & Bltcon1Descending) != 0;
            _step = _descending ? -2 : 2;
            _workSourceA = GetEffectiveBlitterAddress(_sourceA);
            _workSourceB = GetEffectiveBlitterAddress(_sourceB);
            _workSourceC = GetEffectiveBlitterAddress(_sourceC);
            _workDestinationD = GetEffectiveBlitterAddress(_destinationD);
            _activeFirstWordMask = _firstWordMask;
            _activeLastWordMask = _lastWordMask;
            _activeDataA = _dataA;
            _activeDataB = _dataB;
            _activeDataC = _dataC;
            _activeDataAShift = _dataAShift;
            _activeDataBShift = _dataBShift;
            _activeSourceAModulo = _sourceAModulo;
            _activeSourceBModulo = _sourceBModulo;
            _activeSourceCModulo = _sourceCModulo;
            _activeDestinationDModulo = _destinationDModulo;
            _fillEnabled = (_bltcon1 & (Bltcon1InclusiveFill | Bltcon1ExclusiveFill)) != 0;
            _fillExclusive = (_bltcon1 & Bltcon1ExclusiveFill) != 0;
            _fillCarryInitial = (_bltcon1 & Bltcon1FillCarryIn) != 0;
            _fillCarry = _fillCarryInitial;
            _areaKernelState = new BlitterAreaKernelState
            {
                PreviousA = _previousA,
                PreviousB = _previousB,
                FillCarry = _fillCarry
            };
            _activeKernel = _specializationEnabled
                ? _kernelCache.GetOrCreate(CreateAreaKernelKey())
                : default;
        }

        private BlitterKernelKey CreateAreaKernelKey()
            => new BlitterKernelKey(
                false,
                _useA,
                _useB,
                _useC,
                _useD,
                _minterm,
                _useA ? _shiftA : _activeDataAShift,
                _useB ? _shiftB : _activeDataBShift,
                _descending,
                _fillEnabled,
                _fillExclusive,
                false,
                false,
                false,
                false,
                false);

        private BlitterKernelKey CreateLineKernelKey()
            => new BlitterKernelKey(
                true,
                _useA,
                _useB,
                _useC,
                _useD,
                _minterm,
                _shiftA,
                _shiftB,
                false,
                false,
                false,
                _lineSingleDot,
                _lineSud,
                _lineSul,
                _lineAul,
                _lineSign);

        private void DecodeCommonRegisters()
        {
            _useA = (_bltcon0 & 0x0800) != 0;
            _useB = (_bltcon0 & 0x0400) != 0;
            _useC = (_bltcon0 & 0x0200) != 0;
            _useD = (_bltcon0 & 0x0100) != 0;
            _minterm = (byte)(_bltcon0 & 0x00FF);
            _shiftA = (_bltcon0 >> 12) & 0x0F;
            _shiftB = (_bltcon1 >> 12) & 0x0F;
        }

        private void StartLineBlit(ushort bltsize)
        {
            _widthWords = bltsize & 0x3F;
            if (_widthWords == 0)
            {
                _widthWords = 64;
            }

            _height = (bltsize >> 6) & 0x03FF;
            if (_height == 0)
            {
                _height = 1024;
            }

            _lineLength = _height;
            _lineIndex = 0;
            _wordX = 0;
            _rowY = 0;
            _lineBit = _shiftA & 0x0F;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineSingleDot = (_bltcon1 & Bltcon1SingleDot) != 0;
            _lineSud = (_bltcon1 & Bltcon1LineSud) != 0;
            _lineSul = (_bltcon1 & Bltcon1LineSul) != 0;
            _lineAul = (_bltcon1 & Bltcon1LineAul) != 0;
            _lineSign = (_bltcon1 & Bltcon1LineSign) != 0;
            _lineError = unchecked((short)_sourceA);
            _lineSourceRowStride = _sourceCModulo & ~1;
            _lineDestinationRowStride = _lineSourceRowStride;
            _lineBPatternStride = _sourceBModulo & ~1;

            _workSourceC = GetEffectiveBlitterAddress(_sourceC);
            _workSourceB = GetEffectiveBlitterAddress(_sourceB);
            _workDestinationD = GetEffectiveBlitterAddress(_destinationD);
        }

        private void StepAreaWord(long targetCycle)
        {
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetAreaWordCycles();
            var nextReadCycle = stepStart;
            var nextCycle = stepEnd;
            var mask = 0xFFFF;
            if (_wordX == 0)
            {
                mask &= _activeFirstWordMask;
            }

            if (_wordX == _widthWords - 1)
            {
                mask &= _activeLastWordMask;
            }

            var rawA = _activeDataA;
            if (_useA)
            {
                var read = ReadAndStep(ref _workSourceA, _step, nextReadCycle);
                rawA = read.Value;
                RecordBlitterDma(read.BusAccess);
                nextReadCycle = read.BusAccess.CompletedCycle;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            var rawB = _activeDataB;
            if (_useB)
            {
                var read = ReadAndStep(ref _workSourceB, _step, nextReadCycle);
                rawB = read.Value;
                RecordBlitterDma(read.BusAccess);
                nextReadCycle = read.BusAccess.CompletedCycle;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            var rawC = _activeDataC;
            if (_useC)
            {
                var read = ReadAndStep(ref _workSourceC, _step, nextReadCycle);
                rawC = read.Value;
                _activeDataC = rawC;
                RecordBlitterDma(read.BusAccess);
                nextReadCycle = read.BusAccess.CompletedCycle;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            ushort output;
            if (_specializationEnabled && _activeKernel.SupportsArea)
            {
                output = _activeKernel.ExecuteArea(ref _areaKernelState, rawA, rawB, rawC, (ushort)mask);
                _previousA = _areaKernelState.PreviousA;
                _previousB = _areaKernelState.PreviousB;
                _fillCarry = _areaKernelState.FillCarry;
            }
            else
            {
                if (_specializationEnabled)
                {
                    _kernelCache.RecordFallback();
                }

                output = ExecuteAreaScalar(rawA, rawB, rawC, (ushort)mask);
            }

            if (output != 0)
            {
                _zeroFlag = false;
            }

            if (_useD)
            {
                var writeCycle = Math.Max(nextReadCycle, stepEnd - ChipSlotCycles);
                var write = WriteAndStep(ref _workDestinationD, _step, output, writeCycle);
                RecordBlitterDma(write);
                nextCycle = Math.Max(nextCycle, write.CompletedCycle);
            }

            _currentCycle = nextCycle;
            AdvanceAreaPosition(targetCycle);
        }

        private void AdvanceAreaPosition(long targetCycle)
        {
            _wordX++;
            if (_wordX < _widthWords)
            {
                return;
            }

            _wordX = 0;
            _rowY++;
            if (_rowY >= _height)
            {
                CompleteBlit(deferInterrupt: _currentCycle > targetCycle);
                return;
            }

            if (_useA)
            {
                _workSourceA = AddModulo(_workSourceA, _activeSourceAModulo, _descending);
            }

            if (_useB)
            {
                _workSourceB = AddModulo(_workSourceB, _activeSourceBModulo, _descending);
            }

            if (_useC)
            {
                _workSourceC = AddModulo(_workSourceC, _activeSourceCModulo, _descending);
            }

            if (_useD)
            {
                _workDestinationD = AddModulo(_workDestinationD, _activeDestinationDModulo, _descending);
            }

            _fillCarry = _fillCarryInitial;
            _areaKernelState.FillCarry = _fillCarry;
        }

        private void StepLinePixel(long targetCycle)
        {
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetLinePixelCycles();
            var nextCycle = stepEnd;
            if (_useC && (!_lineSingleDot || _lineY != _lineLastDrawnY))
            {
                var nextReadCycle = stepStart;
                if (_useB)
                {
                    var firstB = ReadLineBPattern(nextReadCycle);
                    nextReadCycle = firstB.BusAccess.CompletedCycle;
                    nextCycle = Math.Max(nextCycle, firstB.BusAccess.CompletedCycle);
                    var secondB = ReadLineBPattern(nextReadCycle);
                    _dataB = secondB.Value;
                    nextReadCycle = secondB.BusAccess.CompletedCycle;
                    nextCycle = Math.Max(nextCycle, secondB.BusAccess.CompletedCycle);
                    _workSourceB = _bus.AddChipDmaPointerOffset(_workSourceB, _lineBPatternStride);
                }

                var read = _bus.ReadChipWordForDeviceWithResult(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    _workSourceC,
                    nextReadCycle);
                RecordBlitterDma(read.BusAccess);
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
                var lineMask = RotateRight(_dataA, _lineBit);
                var textureBit = (_dataB & (0x8000 >> ((_shiftB + _lineIndex) & 0x0F))) != 0;
                var texture = textureBit ? (ushort)0xFFFF : (ushort)0;
                var output = _specializationEnabled && _activeKernel.SupportsLine
                    ? _activeKernel.ExecuteLine(lineMask, texture, read.Value)
                    : ApplyMinterm(_minterm, lineMask, texture, read.Value);
                if (_specializationEnabled && !_activeKernel.SupportsLine)
                {
                    _kernelCache.RecordFallback();
                }

                if (output != 0)
                {
                    _zeroFlag = false;
                }

                var destination = _lineIndex == 0 ? _workDestinationD : _workSourceC;
                var write = _bus.WriteChipWordForDeviceWithResult(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    destination,
                    output,
                    Math.Max(read.BusAccess.CompletedCycle, stepEnd - ChipSlotCycles));
                RecordBlitterDma(write);
                nextCycle = Math.Max(nextCycle, write.CompletedCycle);
                _lineLastDrawnY = _lineY;
            }

            _currentCycle = nextCycle;
            _lineIndex++;
            _rowY = _lineIndex;
            if (_lineIndex >= _lineLength)
            {
                CompleteBlit(deferInterrupt: _currentCycle > targetCycle);
                return;
            }

            StepLineAddress();
        }

        private AmigaDeviceWordReadResult ReadLineBPattern(long cycle)
        {
            var read = _bus.ReadChipWordForDeviceWithResult(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                _workSourceB,
                cycle);
            RecordBlitterDma(read.BusAccess);
            return read;
        }

        private void StepLineAddress()
        {
            if (_lineSign)
            {
                _lineError = unchecked(_lineError + _sourceBModulo);
            }
            else
            {
                _lineError = unchecked(_lineError + _sourceAModulo);
                MoveLineMinorAxis();
            }

            MoveLineMajorAxis();
            _lineSign = _lineError < 0;
        }

        private void MoveLineMajorAxis()
        {
            if (_lineSud)
            {
                MoveLineX(_lineAul ? -1 : 1);
            }
            else
            {
                MoveLineY(_lineAul ? -1 : 1);
            }
        }

        private void MoveLineMinorAxis()
        {
            if (_lineSud)
            {
                MoveLineY(_lineSul ? -1 : 1);
            }
            else
            {
                MoveLineX(_lineSul ? -1 : 1);
            }
        }

        private void MoveLineX(int direction)
        {
            if (direction >= 0)
            {
                _lineBit++;
                if (_lineBit <= 15)
                {
                    return;
                }

                _lineBit = 0;
                _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, 2);
                _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, 2);
                return;
            }

            _lineBit--;
            if (_lineBit >= 0)
            {
                return;
            }

            _lineBit = 15;
            _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, -2);
            _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, -2);
        }

        private void MoveLineY(int direction)
        {
            _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, direction >= 0 ? _lineSourceRowStride : -_lineSourceRowStride);
            _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, direction >= 0 ? _lineDestinationRowStride : -_lineDestinationRowStride);
            _lineY += direction;
        }

        private void CompleteBlit(bool deferInterrupt = false)
        {
            if (!_lineMode)
            {
                if (_useA)
                {
                    _sourceA = _workSourceA;
                    _dataA = _previousA;
                }

                if (_useB)
                {
                    _sourceB = _workSourceB;
                    _dataB = _previousB;
                }

                if (_useC)
                {
                    _sourceC = _workSourceC;
                    _dataC = _activeDataC;
                }

                if (_useD)
                {
                    _destinationD = _workDestinationD;
                }
            }
            else
            {
                if (_useA)
                {
                    _sourceA = (uint)(ushort)_lineError;
                }

                if (_useB)
                {
                    _sourceB = _workSourceB;
                }

                _sourceC = _workSourceC;
                _destinationD = _workSourceC;
            }

            if (deferInterrupt)
            {
                _completionPending = true;
                _busy = true;
                return;
            }

            _completionPending = false;
            _busy = false;
            _bus.RequestHardwareInterrupt(AmigaConstants.IntreqBlitter, _currentCycle);
        }

        private void FinalizePendingCompletion()
        {
            _completionPending = false;
            _busy = false;
            _bus.RequestHardwareInterrupt(AmigaConstants.IntreqBlitter, _currentCycle);
        }

        private bool IsBlitterDmaEnabled()
        {
            return (_bus.Paula.Dmacon & (DmaMasterEnable | DmaBlitterEnable)) == (DmaMasterEnable | DmaBlitterEnable);
        }

        private bool ShouldStallCpu()
        {
            return _busy && IsBlitterDmaEnabled() && (_bus.Paula.Dmacon & DmaBlitterNasty) != 0;
        }

        private int GetAreaWordCycles()
        {
            var ticks = 4;
            if (_useB)
            {
                ticks += 2;
            }

            if (_useC)
            {
                ticks += 2;
            }

            return ticks;
        }

        private static int GetLinePixelCycles()
        {
            return 8;
        }

        private long GetCurrentStepEndCycle()
        {
            return _currentCycle + (_lineMode ? GetLinePixelCycles() : GetAreaWordCycles());
        }

        private void RecordBlitterDma(AmigaBusAccessResult access)
        {
            _lastDmaCycle = access.GrantedCycle;
            _completedMicroOps++;
        }

        private ushort ExecuteAreaScalar(ushort rawA, ushort rawB, ushort rawC, ushort mask)
        {
            rawA = (ushort)(rawA & mask);
            var a = ShiftSource(rawA, ref _previousA, _useA ? _shiftA : _activeDataAShift, _descending);
            var b = ShiftSource(rawB, ref _previousB, _useB ? _shiftB : _activeDataBShift, _descending);
            var output = ApplyMinterm(_minterm, a, b, rawC);
            if (_fillEnabled)
            {
                output = ApplyFill(output);
            }

            _areaKernelState.PreviousA = _previousA;
            _areaKernelState.PreviousB = _previousB;
            _areaKernelState.FillCarry = _fillCarry;
            return output;
        }

        private ushort ApplyFill(ushort value)
        {
            var fillCarry = _fillCarry;
            var output = BlitterKernelMath.ApplyFill(value, _fillExclusive, ref fillCarry);
            _fillCarry = fillCarry;
            return output;
        }

        private AmigaDeviceWordReadResult ReadAndStep(ref uint pointer, int step, long cycle)
        {
            var value = _bus.ReadChipWordForDeviceWithResult(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                GetEffectiveBlitterAddress(pointer),
                cycle);
            pointer = _bus.AddChipDmaPointerOffset(pointer, step);
            return value;
        }

        private AmigaBusAccessResult WriteAndStep(ref uint pointer, int step, ushort value, long cycle)
        {
            var access = _bus.WriteChipWordForDeviceWithResult(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                GetEffectiveBlitterAddress(pointer),
                value,
                cycle);
            pointer = _bus.AddChipDmaPointerOffset(pointer, step);
            return access;
        }

        private uint AddModulo(uint pointer, short modulo, bool descending)
        {
            var evenModulo = modulo & ~1;
            return _bus.AddChipDmaPointerOffset(pointer, descending ? -evenModulo : evenModulo);
        }

        private uint GetEffectiveBlitterAddress(uint pointer)
        {
            return _bus.MaskChipDmaAddress(pointer);
        }

        private static ushort RotateRight(ushort value, int bits)
        {
            bits &= 0x0F;
            return bits == 0
                ? value
                : (ushort)((value >> bits) | (value << (16 - bits)));
        }

        private static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
            => BlitterKernelMath.ShiftSource(current, ref previous, shift, descending);

        private static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
            => BlitterKernelMath.ApplyMinterm(minterm, sourceA, sourceB, sourceC);
    }
}
