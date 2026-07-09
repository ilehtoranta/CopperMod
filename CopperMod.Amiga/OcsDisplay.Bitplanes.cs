/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Numerics;

namespace CopperMod.Amiga
{
    internal sealed partial class OcsDisplay
    {
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


    }
}
