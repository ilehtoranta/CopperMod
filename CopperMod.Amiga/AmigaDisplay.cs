using System;
using System.Collections.Generic;

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
        private const int CopperWaitWakeHpUnits = 6;
        private const ushort CopconCopperDanger = 0x0002;
        private const long PalFrameCycles = AmigaConstants.A500PalCpuCyclesPerFrame;
        private const int PalLineCycles = AmigaConstants.A500PalCpuCyclesPerRasterLine;
        private const int CopperHpCycles = AmigaConstants.A500PalCpuCyclesPerColorClock;
        private readonly AmigaBus _bus;
        private readonly List<PendingCustomWrite> _pendingWrites = new List<PendingCustomWrite>();
        private readonly ushort[] _colors = new ushort[32];
        private readonly uint[] _convertedColors = new uint[64];
        private readonly uint[] _bitplanePointers = new uint[6];
        private readonly int[] _bitplaneBaseRows = new int[6];
        private byte[] _playfieldPriorityMasks = Array.Empty<byte>();
        private readonly ushort[,] _renderPlaneWords = new ushort[6, MaxBitplaneFetchWords];
        private readonly bool[] _renderPlaneHasRow = new bool[6];
        private readonly SpriteState[] _sprites = new SpriteState[8];
        private readonly List<SpriteFrameCommand> _spriteFrameCommands = new List<SpriteFrameCommand>();
        private readonly List<PaletteFrameSpan> _paletteFrameSpans = new List<PaletteFrameSpan>();
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
        private bool _renderingCopperFrame;
        private bool _captureSpriteFrameCommands;
        private bool _enforceDmaForFrame;
        private bool _useTimedPresentationReads;
        private int _currentCopperRow;
        private int _currentRenderRow;
        private long _renderFrameStartCycle;

        public OcsDisplay(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            for (var i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = new SpriteState();
            }

            Reset();
        }

        public int Width => AmigaConstants.PalHighResWidth;

        public int Height => AmigaConstants.PalHighResHeight;

        public bool InterlaceEnabled => (_bplcon0 & 0x0004) != 0;

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
        }

        public void ScheduleWrite(long cycle, ushort offset, ushort value)
        {
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
        }

        public void RenderFrame(Span<uint> bgra)
        {
            RenderFrame(bgra, 0, long.MaxValue, useTimedWrites: false);
        }

        public void RenderFrame(Span<uint> bgra, long frameStartCycle, long frameEndCycle)
        {
            RenderFrame(bgra, frameStartCycle, frameEndCycle, useTimedWrites: true);
        }

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

            ApplyPendingWrites(useTimedWrites ? frameStartCycle : long.MaxValue);
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
            bgra = bgra.Slice(0, _renderWidth * _renderHeight);

            _captureSpriteFrameCommands = useTimedWrites || _copperListPointer != 0;
            try
            {
                if (useTimedWrites)
                {
                    var frameStopCycle = GetPresentationFrameStopCycle(frameStartCycle, frameEndCycle);
                    RenderTimedPresentationFrame(bgra, frameStartCycle, frameStopCycle);
                }
                else if (_copperListPointer != 0 && IsCopperDmaEnabled())
                {
                    RenderCopperFrame(bgra, frameStartCycle, frameStartCycle + PalFrameCycles, useTimedWrites);
                }
                else
                {
                    RenderRows(bgra, 0, LowResOutputHeight, frameStartCycle, useTimedWrites);
                }
            }
            finally
            {
                _captureSpriteFrameCommands = false;
                _enforceDmaForFrame = false;
            }

            if (useTimedWrites)
            {
                ApplyPendingWrites(frameEndCycle);
            }

            try
            {
                RenderSprites(bgra);
            }
            finally
            {
                _useTimedPresentationReads = false;
                _bus.ClearPresentationWriteHistory();
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

        private void RenderTimedPresentationFrame(Span<uint> bgra, long frameStartCycle, long frameStopCycle)
        {
            RenderCopperFrame(bgra, frameStartCycle, frameStopCycle, useTimedWrites: true);
        }

        private void RenderCopperFrame(Span<uint> bgra, long frameStartCycle, long frameStopCycle, bool useTimedWrites)
        {
            _renderingCopperFrame = true;
            _currentCopperRow = GetOutputRowForCycle(frameStartCycle, frameStartCycle);
            var renderCursorCycle = frameStartCycle;
            var copper = new CopperPresentationState(_copperListPointer, frameStartCycle);
            var safetyRemaining = GetCopperFrameInstructionLimit(frameStartCycle, frameStopCycle);

            try
            {
                while (copper.Cycle < frameStopCycle)
                {
                    if (TryPeekPendingWrite(out var pending) && pending.Cycle <= copper.Cycle)
                    {
                        RenderPresentationSpan(bgra, frameStartCycle, renderCursorCycle, pending.Cycle, useTimedWrites);
                        renderCursorCycle = Math.Max(renderCursorCycle, pending.Cycle);
                        ApplyTimedPendingWrite(ref copper);
                        continue;
                    }

                    if (copper.Stopped || !IsCopperDmaEnabled())
                    {
                        if (!TryAdvanceCopperToNextPendingWrite(bgra, frameStartCycle, frameStopCycle, useTimedWrites, ref renderCursorCycle, ref copper))
                        {
                            break;
                        }

                        continue;
                    }

                    if (copper.Waiting)
                    {
                        if (!TryAdvanceCopperWait(bgra, frameStartCycle, frameStopCycle, useTimedWrites, ref renderCursorCycle, ref copper))
                        {
                            break;
                        }

                        continue;
                    }

                    if (safetyRemaining-- <= 0)
                    {
                        break;
                    }

                    StepCopperInstruction(bgra, frameStartCycle, frameStopCycle, useTimedWrites, ref renderCursorCycle, ref copper);
                }

                RenderPresentationSpan(bgra, frameStartCycle, renderCursorCycle, frameStopCycle, useTimedWrites);
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
            ref CopperPresentationState copper)
        {
            if (!TryPeekPendingWrite(out var pending) || pending.Cycle >= frameStopCycle)
            {
                return false;
            }

            RenderPresentationSpan(bgra, frameStartCycle, renderCursorCycle, pending.Cycle, useTimedWrites);
            renderCursorCycle = Math.Max(renderCursorCycle, pending.Cycle);
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
                    ref copper);
            }

            if (!TryGetCopperWaitCycle(
                copper.WaitFirst,
                copper.WaitSecond,
                frameStartCycle,
                copper.Cycle,
                frameStopCycle,
                IsCopperBlitterFinishedForWait(copper.WaitSecond),
                out var waitCycle))
            {
                return false;
            }

            if (TryPeekPendingWrite(out var pending) && pending.Cycle < waitCycle)
            {
                RenderPresentationSpan(bgra, frameStartCycle, renderCursorCycle, pending.Cycle, useTimedWrites);
                renderCursorCycle = Math.Max(renderCursorCycle, pending.Cycle);
                copper.Cycle = Math.Max(copper.Cycle, pending.Cycle);
                ApplyTimedPendingWrite(ref copper);
                return true;
            }

            var resumeCycle = waitCycle + CopperHpToCpuCycles(CopperWaitWakeHpUnits);
            RenderPresentationSpan(bgra, frameStartCycle, renderCursorCycle, Math.Min(resumeCycle, frameStopCycle), useTimedWrites);
            renderCursorCycle = Math.Max(renderCursorCycle, Math.Min(resumeCycle, frameStopCycle));
            copper.Cycle = Math.Max(copper.Cycle, resumeCycle);
            copper.Waiting = false;
            return copper.Cycle < frameStopCycle;
        }

        private void StepCopperInstruction(
            Span<uint> bgra,
            long frameStartCycle,
            long frameStopCycle,
            bool useTimedWrites,
            ref long renderCursorCycle,
            ref CopperPresentationState copper)
        {
            var fetchCycle = Math.Min(copper.Cycle, frameStopCycle);
            var dataCycle = fetchCycle + CopperHpToCpuCycles(CopperInstructionDataHpUnits);
            var instructionStopCycle = fetchCycle + CopperHpToCpuCycles(CopperMoveHpUnits);
            var first = ReadChipWordForPresentationAtCycle(copper.Pc, fetchCycle);
            var second = ReadChipWordForPresentationAtCycle(AddDmaPointerOffset(copper.Pc, 2), dataCycle);
            copper.Pc = AddDmaPointerOffset(copper.Pc, 4);

            if (first == 0xFFFF && second == 0xFFFE)
            {
                copper.Stopped = true;
                return;
            }

            if ((first & 1) == 0)
            {
                var register = (ushort)(first & 0x01FE);
                RenderPresentationSpan(bgra, frameStartCycle, renderCursorCycle, Math.Min(dataCycle, frameStopCycle), useTimedWrites);
                renderCursorCycle = Math.Max(renderCursorCycle, Math.Min(dataCycle, frameStopCycle));
                if (dataCycle <= frameStopCycle)
                {
                    _currentCopperRow = GetOutputRowForCycle(frameStartCycle, dataCycle);
                    if (CanCopperWriteRegister(register))
                    {
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
                copper.Pc = AddDmaPointerOffset(copper.Pc, 4);
            }

            copper.Cycle = fetchCycle + CopperHpToCpuCycles(CopperSkipHpUnits);
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
            ApplyWrite(write.Offset, write.Value);
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
            bool useTimedWrites)
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
                var xStop = segmentStop >= lineStop
                    ? AmigaConstants.PalLowResWidth
                    : GetOutputXForCycle(frameStartCycle, segmentStop);
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

            var startLine = Math.Clamp(GetBeamLineForCycle(frameStartCycle, currentCycle), 0, AmigaConstants.A500PalRasterLines - 1);
            var startHorizontal = GetCopperHorizontalForCycle(frameStartCycle, currentCycle) & 0xFE;
            if (startHorizontal > LastCopperHorizontal)
            {
                startLine++;
                startHorizontal = 0;
            }

            for (var line = startLine; line < AmigaConstants.A500PalRasterLines; line++)
            {
                var horizontalStart = line == startLine ? startHorizontal : 0;
                for (var horizontal = horizontalStart; horizontal <= LastCopperHorizontal; horizontal += 2)
                {
                    if (!IsCopperComparisonSatisfied(first, second, line - StandardVStart, horizontal, blitterFinished))
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
            var line = GetBeamLineForCycle(frameStartCycle, cycle);
            var lineStart = GetLineStartCycle(frameStartCycle, line);
            var horizontal = (int)(Math.Max(0, cycle - lineStart) / CopperHpCycles);
            return Math.Clamp(horizontal, 0, LastCopperHorizontal);
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

            var colors = new uint[_convertedColors.Length];
            var window = GetDisplayWindow();
            Array.Copy(_convertedColors, colors, colors.Length);
            for (var row = rowStart; row < rowStop; row++)
            {
                _paletteFrameSpans.Add(new PaletteFrameSpan(row, xStart, xStop, colors, _bplcon0, _bplcon2, window));
            }
        }

        private void ApplyPendingWrites(long cycle)
        {
            while (_pendingIndex < _pendingWrites.Count && _pendingWrites[_pendingIndex].Cycle <= cycle)
            {
                var write = _pendingWrites[_pendingIndex++];
                ApplyWrite(write.Offset, write.Value);
            }

            CompactPendingWrites();
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

        private void ApplyWrite(ushort offset, ushort value)
        {
            if (offset == 0x096)
            {
                var bitplaneDmaWasEnabled = IsBitplaneDmaEnabled(_dmacon);
                if (bitplaneDmaWasEnabled && !IsBitplaneDmaEnabledAfterSetClear(_dmacon, value))
                {
                    AnchorActiveBitplanePointersToCurrentRow();
                }

                ApplySetClear(ref _dmacon, value);
                if (!bitplaneDmaWasEnabled && IsBitplaneDmaEnabled(_dmacon))
                {
                    var planeCount = Math.Min((_bplcon0 >> 12) & 0x7, _bitplaneBaseRows.Length);
                    SetBitplaneBaseRows(0, planeCount, GetCurrentBitplaneBaseRow());
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
            ApplyWrite(offset, value);
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

            return _currentRenderRow >= 0
                ? Math.Max(_currentRenderRow, windowY)
                : windowY;
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
            if (_renderingCopperFrame)
            {
                row = _currentCopperRow;
                return true;
            }

            if (_currentRenderRow >= 0)
            {
                row = _currentRenderRow;
                return true;
            }

            row = 0;
            return false;
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
                    }
                    else
                    {
                        _sprites[sprite].Pointer = WriteDmaPointerLow(_sprites[sprite].Pointer, value);
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
            var window = GetDisplayWindow();
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
                    planeHasRow[plane] = displaySourceY >= 0;
                    var rowAddress = unchecked(_bitplanePointers[plane] + (uint)(planeSourceY * rowStride));
                    for (var word = 0; word < fetchWords; word++)
                    {
                        if (!planeHasRow[plane])
                        {
                            planeWords[plane, word] = 0;
                            continue;
                        }

                        var address = unchecked(rowAddress + (uint)(word * 2));
                        planeWords[plane, word] = ReadChipWordForPresentation(address, y);
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
            if ((_bplcon2 & 0x0040) != 0)
            {
                return new DualPlayfieldPixel(playfield2Color != 0 ? playfield2Color : playfield1, priorityMask);
            }

            return new DualPlayfieldPixel(playfield1 != 0 ? playfield1 : playfield2Color, priorityMask);
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

            return new DisplayWindow(
                hStart - StandardHStart,
                vStart - StandardVStart,
                hStop - hStart,
                vStop - vStart);
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
                var evenAttached = evenSprites.Count == 0 ? Array.Empty<bool>() : new bool[evenSprites.Count];
                var oddAttached = oddSprites.Count == 0 ? Array.Empty<bool>() : new bool[oddSprites.Count];

                for (var oddIndex = 0; oddIndex < oddSprites.Count; oddIndex++)
                {
                    var oddSprite = oddSprites[oddIndex];
                    if (!oddSprite.Descriptor.Attached)
                    {
                        continue;
                    }

                    var evenIndex = FindAttachedEvenSprite(evenSprites, evenAttached, oddSprite);
                    if (evenIndex < 0)
                    {
                        oddAttached[oddIndex] = true;
                        RenderAttachedOddSpriteWithoutEvenPartner(bgra, spriteIndex, oddSprite);
                        continue;
                    }

                    evenAttached[evenIndex] = true;
                    oddAttached[oddIndex] = true;
                    RenderAttachedSpritePair(bgra, spriteIndex, evenSprites[evenIndex], oddSprite);
                }

                for (var i = 0; i < oddSprites.Count; i++)
                {
                    if (!oddAttached[i] && !oddSprites[i].Descriptor.Attached)
                    {
                        RenderSprite(bgra, spriteIndex + 1, oddSprites[i]);
                    }
                }

                for (var i = 0; i < evenSprites.Count; i++)
                {
                    if (!evenAttached[i])
                    {
                        RenderSprite(bgra, spriteIndex, evenSprites[i]);
                    }
                }
            }
        }

        private List<SpriteFrameCommand> GetSpriteFrameCommands(int spriteIndex)
        {
            var commands = new List<SpriteFrameCommand>();
            for (var i = 0; i < _spriteFrameCommands.Count; i++)
            {
                var command = _spriteFrameCommands[i];
                if (command.SpriteIndex == spriteIndex)
                {
                    AppendUniqueSpriteFrameCommand(commands, command);
                }
            }

            var sprite = _sprites[spriteIndex];
            if (commands.Count == 0 && IsSpriteDmaEnabled() && sprite.Pointer != 0)
            {
                if (IsSpriteDmaChannelAvailable(spriteIndex))
                {
                    AppendDmaSpriteFrameCommands(commands, spriteIndex, sprite.Pointer, 0);
                }
            }
            else if (TryGetManualSpriteDescriptor(spriteIndex, out var descriptor))
            {
                AppendUniqueSpriteFrameCommand(
                    commands,
                    new SpriteFrameCommand(spriteIndex, 0, descriptor));
            }

            return commands;
        }

        private static void AppendUniqueSpriteFrameCommand(List<SpriteFrameCommand> commands, SpriteFrameCommand command)
        {
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
            IReadOnlyList<bool> evenAttached,
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
            if (!_captureSpriteFrameCommands || !IsSpriteDmaEnabled() || !IsSpriteDmaChannelAvailable(spriteIndex))
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
            for (var controlBlock = 0; controlBlock < 128; controlBlock++)
            {
                var pos = ReadChipWordForPresentation(controlAddress, row);
                var ctl = ReadChipWordForPresentation(AddDmaPointerOffset(controlAddress, 2), row);
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

                var height = Math.Max(0, descriptor.YStop - descriptor.YStart);
                if (height == 0)
                {
                    return;
                }

                if (descriptor.YStart >= row)
                {
                    AppendUniqueSpriteFrameCommand(commands, new SpriteFrameCommand(spriteIndex, row, descriptor));
                }

                controlAddress = AddDmaPointerOffset(descriptor.DataAddress, height * 4);
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
                    RenderSpriteLine(bgra, spriteIndex, sprite.X, y, sprite.ManualDataA, sprite.ManualDataB, pixel => GetSpriteColorIndex(spriteIndex, pixel));
                }

                return;
            }

            var address = sprite.DataAddress;
            for (var y = sprite.YStart; y < sprite.YStop; y++)
            {
                var dataA = ReadChipWordForPresentation(address, y);
                var dataB = ReadChipWordForPresentation(AddDmaPointerOffset(address, 2), y);
                RenderSpriteLine(bgra, spriteIndex, sprite.X, y, dataA, dataB, pixel => GetSpriteColorIndex(spriteIndex, pixel));
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

            return (
                ReadChipWordForPresentation(address, y),
                ReadChipWordForPresentation(AddDmaPointerOffset(address, 2), y));
        }

        private void RenderSpriteLine(Span<uint> bgra, int spriteIndex, int x, int y, ushort dataA, ushort dataB, Func<int, int> mapColor)
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

                WriteSpritePixel(bgra, px, y, ConvertSpriteColorIndex(mapColor(pixel), px, y));
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
                if (span.Contains(x, y) && (uint)colorIndex < (uint)span.Colors.Length)
                {
                    return span.Colors[colorIndex];
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
            public PaletteFrameSpan(int row, int xStart, int xStop, uint[] colors, ushort bplcon0, ushort bplcon2, DisplayWindow window)
            {
                Row = row;
                XStart = xStart;
                XStop = xStop;
                Colors = colors;
                Bplcon0 = bplcon0;
                Bplcon2 = bplcon2;
                Window = window;
            }

            public int Row { get; }

            public int XStart { get; }

            public int XStop { get; }

            public uint[] Colors { get; }

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
                WaitFirst = 0;
                WaitSecond = 0;
            }

            public uint Pc;

            public long Cycle;

            public bool Stopped;

            public bool Waiting;

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
            }
        }

        private readonly struct PendingCustomWrite
        {
            public PendingCustomWrite(long cycle, ushort offset, ushort value)
            {
                Cycle = cycle;
                Offset = offset;
                Value = value;
            }

            public long Cycle { get; }

            public ushort Offset { get; }

            public ushort Value { get; }
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
            bool lineMode)
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

        private readonly AmigaBus _bus;
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

        public AmigaBlitter(AmigaBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
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
                _workDestinationD,
                _widthWords,
                _height,
                _wordX,
                _rowY,
                _lineMode);
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

            while (_busy && _currentCycle <= targetCycle)
            {
                if (!IsBlitterDmaEnabled())
                {
                    _currentCycle = Math.Max(_currentCycle, targetCycle);
                    return;
                }

                if (_lineMode)
                {
                    StepLinePixel();
                }
                else
                {
                    StepAreaWord();
                }
            }
        }

        public long AdvanceThroughCpuStall(long requestedCycle)
        {
            if (!ShouldStallCpu())
            {
                return requestedCycle;
            }

            AdvanceTo(requestedCycle);
            return Math.Max(requestedCycle, _currentCycle);
        }

        private void StartBlit(ushort bltsize, long cycle)
        {
            AdvanceTo(cycle);
            DecodeCommonRegisters();
            _lineMode = (_bltcon1 & Bltcon1LineMode) != 0;
            _zeroFlag = true;
            _busy = true;
            _currentCycle = Math.Max(_currentCycle, Math.Max(0, cycle));

            if (_lineMode)
            {
                StartLineBlit(bltsize);
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
            _previousA = 0;
            _previousB = 0;
            _fillEnabled = (_bltcon1 & (Bltcon1InclusiveFill | Bltcon1ExclusiveFill)) != 0;
            _fillExclusive = (_bltcon1 & Bltcon1ExclusiveFill) != 0;
            _fillCarryInitial = (_bltcon1 & Bltcon1FillCarryIn) != 0;
            _fillCarry = _fillCarryInitial;
        }

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
            _lineDestinationRowStride = _destinationDModulo & ~1;
            if (_lineDestinationRowStride == 0)
            {
                _lineDestinationRowStride = _lineSourceRowStride;
            }

            _workSourceC = GetEffectiveBlitterAddress(_sourceC);
            _workDestinationD = GetEffectiveBlitterAddress(_destinationD);
        }

        private void StepAreaWord()
        {
            var stepStart = _currentCycle;
            var nextCycle = _currentCycle;
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
                var read = ReadAndStep(ref _workSourceA, _step, stepStart);
                rawA = read.Value;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            var rawB = _activeDataB;
            if (_useB)
            {
                var read = ReadAndStep(ref _workSourceB, _step, stepStart);
                rawB = read.Value;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            var rawC = _activeDataC;
            if (_useC)
            {
                var read = ReadAndStep(ref _workSourceC, _step, stepStart);
                rawC = read.Value;
                _activeDataC = rawC;
                nextCycle = Math.Max(nextCycle, read.BusAccess.CompletedCycle);
            }

            rawA = (ushort)(rawA & mask);
            var a = ShiftSource(rawA, ref _previousA, _useA ? _shiftA : _activeDataAShift, _descending);
            var b = ShiftSource(rawB, ref _previousB, _useB ? _shiftB : _activeDataBShift, _descending);
            var output = ApplyMinterm(_minterm, a, b, rawC);
            if (_fillEnabled)
            {
                output = ApplyFill(output);
            }

            if (output != 0)
            {
                _zeroFlag = false;
            }

            if (_useD)
            {
                var write = WriteAndStep(ref _workDestinationD, _step, output, stepStart);
                nextCycle = Math.Max(nextCycle, write.CompletedCycle);
            }

            _currentCycle = Math.Max(nextCycle, stepStart + GetAreaWordCycles());
            AdvanceAreaPosition();
        }

        private void AdvanceAreaPosition()
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
                CompleteBlit();
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
        }

        private void StepLinePixel()
        {
            var stepStart = _currentCycle;
            var nextCycle = _currentCycle;
            if (!_lineSingleDot || _lineY != _lineLastDrawnY)
            {
                var read = _bus.ReadChipWordForDeviceWithResult(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    _workSourceC,
                    nextCycle);
                nextCycle = read.BusAccess.CompletedCycle + 1;
                var lineMask = RotateRight(_dataA, _lineBit);
                var textureBit = (_dataB & (0x8000 >> ((_shiftB + _lineIndex) & 0x0F))) != 0;
                var texture = textureBit ? (ushort)0xFFFF : (ushort)0;
                var output = ApplyMinterm(_minterm, lineMask, texture, read.Value);
                if (output != 0)
                {
                    _zeroFlag = false;
                }

                var write = _bus.WriteChipWordForDeviceWithResult(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    _workDestinationD,
                    output,
                    nextCycle);
                nextCycle = write.CompletedCycle + 1;
                _lineLastDrawnY = _lineY;
            }

            _currentCycle = Math.Max(nextCycle, stepStart + 2);
            _lineIndex++;
            _rowY = _lineIndex;
            if (_lineIndex >= _lineLength)
            {
                CompleteBlit();
                return;
            }

            StepLineAddress();
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

        private void CompleteBlit()
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
                _sourceA = (uint)(ushort)_lineError;
                _sourceC = _workSourceC;
                _destinationD = _workDestinationD;
            }

            _busy = false;
            _bus.WriteDeviceWord(
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.CustomRegister,
                0x00DFF09C,
                (ushort)(0x8000 | AmigaConstants.IntreqBlitter),
                _currentCycle);
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
            return 1;
        }

        private ushort ApplyFill(ushort value)
        {
            ushort output = 0;
            for (var bit = 0; bit < 16; bit++)
            {
                var mask = (ushort)(1 << bit);
                var input = (value & mask) != 0;
                if (_fillExclusive)
                {
                    if (_fillCarry)
                    {
                        output |= mask;
                    }

                    if (input)
                    {
                        _fillCarry = !_fillCarry;
                    }

                    continue;
                }

                if (_fillCarry || input)
                {
                    output |= mask;
                }

                if (input)
                {
                    _fillCarry = !_fillCarry;
                }
            }

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
        {
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

        private static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
        {
            ushort value = 0;
            for (var bit = 0; bit < 16; bit++)
            {
                var mask = 1 << bit;
                var selector = 0;
                if ((sourceA & mask) != 0)
                {
                    selector |= 4;
                }

                if ((sourceB & mask) != 0)
                {
                    selector |= 2;
                }

                if ((sourceC & mask) != 0)
                {
                    selector |= 1;
                }

                if (((minterm >> selector) & 1) != 0)
                {
                    value |= (ushort)mask;
                }
            }

            return value;
        }
    }
}
