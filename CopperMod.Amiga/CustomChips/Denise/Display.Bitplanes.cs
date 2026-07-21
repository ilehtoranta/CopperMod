/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Numerics;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        private void RenderBitplanes(Span<uint> bgra, int bandStart, int bandStop, int xClipStart = 0, int xClipStop = -1)
        {
            if (xClipStop < 0)
            {
                xClipStop = LowResWidth;
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
            var windowXStart = GetDisplayWindowOutputXStart(window);
            var windowXStop = GetDisplayWindowOutputXStop(window);
            var windowYStart = GetDisplayWindowOutputYStart(window);
            var windowYStop = GetDisplayWindowOutputYStop(window);
            if (windowXStop <= windowXStart || windowYStop <= windowYStart || fetchWords <= 0)
            {
                return;
            }

            var resolution = GetDeniseResolution(_bplcon0);
            var highResolution = resolution == DeniseResolution.HighRes;
            var superHighResolution = resolution == DeniseResolution.SuperHighRes;
            var fetchPixels = fetchWords * 16;
            var samplesPerLowResSpan = GetResolutionSamplesPerLowResSpan(resolution);
            var drawPixels = fetchPixels / samplesPerLowResSpan;
            var originX = GetDataFetchStartX(window);
            var clipLeft = Math.Max(Math.Max(0, windowXStart), xClipStart);
            var clipRight = Math.Min(Math.Min(LowResWidth, windowXStop), xClipStop);
            var rowStart = Math.Max(Math.Max(0, bandStart), windowYStart);
            var rowStop = Math.Min(Math.Min(LowResOutputHeight, bandStop), windowYStop);
            var holdAndModify = !superHighResolution && !highResolution && (_bplcon0 & 0x0800) != 0 && planeCount >= 6;
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

                        var displaySourceY = y - _bitplaneBaseRows[plane];
                        var liveCapturedMask = _renderingLiveCapture
                            ? _liveBitplaneWordMasks[GetLiveBitplaneMaskIndex(y, plane)]
                            : (UInt128)0;
                        var liveWordIndexOffset = _renderingLiveCapture
                            ? _renderBitplaneWordIndexOffsets[plane]
                            : 0;
                        planeHasRow[plane] = bitplaneDmaEnabled && (displaySourceY >= 0 || liveCapturedMask != 0);
                        for (var word = 0; word < fetchWords; word++)
                        {
                            if (!planeHasRow[plane])
                            {
                                planeWords[plane, word] = 0;
                                continue;
                            }

                            var capturedWord = word + liveWordIndexOffset;
                            if ((uint)capturedWord < (uint)MaxBitplaneFetchWords &&
                                (liveCapturedMask & ((UInt128)1 << capturedWord)) != 0 &&
                                TryReadLiveCapturedBitplaneWord(y, plane, capturedWord, out var captured))
                            {
                                planeWords[plane, word] = captured;
                                LoadBitplaneDataRegister(plane, captured);
                                _lastBitplaneWords++;
                                continue;
                            }

                            // A word absent from the causal line capture was not
                            // fetched. Never reconstruct it from current Chip RAM.
                            planeWords[plane, word] = 0;
                            _lastBitplaneWords++;
                        }
                    }

                    var xStart = hasBitplaneDataSpans
                        ? clipLeft
                        : Math.Max(clipLeft, Math.Max(0, originX));
                    var xStop = hasBitplaneDataSpans
                        ? clipRight
                        : Math.Min(
                            clipRight,
                            Math.Min(
                                LowResWidth,
                                originX + drawPixels + (PlanarChunkPixels / samplesPerLowResSpan)));
                    var hamColor = _colors[0];
                    if (!superHighResolution && !highResolution && !dualPlayfield && !holdAndModify && zeroScroll)
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
                        if (superHighResolution)
                        {
                            var color0 = GetSuperHighResolutionColorIndex(planeWords, planeHasRow, planeCount, x, y, originX, fetchPixels, 0);
                            var color1 = GetSuperHighResolutionColorIndex(planeWords, planeHasRow, planeCount, x, y, originX, fetchPixels, 1);
                            var color2 = GetSuperHighResolutionColorIndex(planeWords, planeHasRow, planeCount, x, y, originX, fetchPixels, 2);
                            var color3 = GetSuperHighResolutionColorIndex(planeWords, planeHasRow, planeCount, x, y, originX, fetchPixels, 3);
                            var combined = color0 | color1 | color2 | color3;
                            var mask0 = GetSuperHighResolutionPlayfieldPriorityMask(color0, dualPlayfield);
                            var mask1 = GetSuperHighResolutionPlayfieldPriorityMask(color1, dualPlayfield);
                            var mask2 = GetSuperHighResolutionPlayfieldPriorityMask(color2, dualPlayfield);
                            var mask3 = GetSuperHighResolutionPlayfieldPriorityMask(color3, dualPlayfield);
                            SetPlayfieldSampleState(x, y, 0, color0, mask0);
                            SetPlayfieldSampleState(x, y, 1, color1, mask1);
                            SetPlayfieldSampleState(x, y, 2, color2, mask2);
                            SetPlayfieldSampleState(x, y, 3, color3, mask3);
                            if (combined != 0)
                            {
                                RecordBitplanePixel(combined, (byte)(mask0 | mask1 | mask2 | mask3), x, y);
                            }

                            var pair01 = ConvertSuperHighResolutionColorPair(color0, color1);
                            var pair23 = ConvertSuperHighResolutionColorPair(color2, color3);
                            WriteSuperHighResolutionOutputPixels(
                                bgra,
                                x,
                                y,
                                pair01.Left,
                                pair01.Right,
                                pair23.Left,
                                pair23.Right);
                            continue;
                        }

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

        private int GetSuperHighResolutionColorIndex(
            ushort[,] planeWords,
            bool[] planeHasRow,
            int planeCount,
            int x,
            int y,
            int originX,
            int fetchPixels,
            int subpixel)
        {
            for (var i = _bitplaneDataSpans.Count - 1; i >= 0; i--)
            {
                var span = _bitplaneDataSpans[i];
                if (!span.Contains(x, y))
                {
                    continue;
                }

                var manualIndex = 0;
                for (var plane = 0; plane < Math.Min(planeCount, 2); plane++)
                {
                    var relative = ((x - span.XStart) * 4) + subpixel - GetPlaneHorizontalScroll(plane);
                    if ((uint)relative < 16)
                    {
                        manualIndex |= ((span.GetWord(plane) >> (15 - relative)) & 1) << plane;
                    }
                }

                return manualIndex;
            }

            var colorIndex = 0;
            for (var plane = 0; plane < Math.Min(planeCount, 2); plane++)
            {
                if (!planeHasRow[plane])
                {
                    continue;
                }

                var relative = ((x - originX) * 4) + subpixel - GetPlaneHorizontalScroll(plane);
                if ((uint)relative >= (uint)fetchPixels)
                {
                    continue;
                }

                var word = relative >> 4;
                colorIndex |= ((planeWords[plane, word] >> (15 - (relative & 0x0F))) & 1) << plane;
            }

            return colorIndex;
        }

        private static byte GetSuperHighResolutionPlayfieldPriorityMask(int rawColorIndex, bool dualPlayfield)
        {
            if (rawColorIndex == 0)
            {
                return 0;
            }

            if (!dualPlayfield)
            {
                return NormalPlayfieldPriorityMask;
            }

            var mask = (byte)0;
            if ((rawColorIndex & 1) != 0)
            {
                mask |= Playfield1PriorityMask;
            }

            if ((rawColorIndex & 2) != 0)
            {
                mask |= Playfield2PriorityMask;
            }

            return mask;
        }

        private (uint Left, uint Right) ConvertSuperHighResolutionColorPair(int leftIndex, int rightIndex)
        {
            var colorRegister = ((rightIndex & 3) << 2) | (leftIndex & 3);
            var encoded = _colors[colorRegister];
            return (ConvertSuperHighResolutionComponentColor(encoded, highPair: true),
                ConvertSuperHighResolutionComponentColor(encoded, highPair: false));
        }

        private (uint Left, uint Right) ConvertSuperHighResolutionSpriteColorPair(
            int leftIndex,
            int rightIndex,
            int x,
            int y)
        {
            var colorRegister = 16 + ((rightIndex & 3) << 2) + (leftIndex & 3);
            var encoded = GetSuperHighResolutionEncodedColor(colorRegister, x, y);
            return (ConvertSuperHighResolutionComponentColor(encoded, highPair: true),
                ConvertSuperHighResolutionComponentColor(encoded, highPair: false));
        }

        private ushort GetSuperHighResolutionEncodedColor(int colorRegister, int x, int y)
        {
            var spanIndex = GetPaletteFrameSpanIndex(x, y);
            if (spanIndex >= 0)
            {
                ref readonly var span = ref GetPaletteFrameSpan(spanIndex);
                return GetPaletteFrameSpanEncodedColor(span, colorRegister);
            }

            return _colors[colorRegister];
        }

        private static uint ConvertSuperHighResolutionComponentColor(ushort encoded, bool highPair)
        {
            var shift = highPair ? 2 : 0;
            var r = (uint)(((encoded >> (8 + shift)) & 3) * 85);
            var g = (uint)(((encoded >> (4 + shift)) & 3) * 85);
            var b = (uint)(((encoded >> shift) & 3) * 85);
            return 0xFF00_0000u | (r << 16) | (g << 8) | b;
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
                xStop = LowResWidth;
            }

            rowStart = Math.Clamp(rowStart, 0, LowResOutputHeight);
            rowStop = Math.Clamp(rowStop, rowStart, LowResOutputHeight);
            xStart = Math.Clamp(xStart, 0, LowResWidth);
            xStop = Math.Clamp(xStop, xStart, LowResWidth);
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
            var resolution = GetDeniseResolution(_bplcon0);
            var highResolution = resolution == DeniseResolution.HighRes;
            var superHighResolution = resolution == DeniseResolution.SuperHighRes;
            var defaultStart = highResolution || superHighResolution
                ? DefaultHighResDdfStart
                : DefaultDdfStart;
            var defaultOrigin = superHighResolution
                ? Math.Clamp(GetDisplayWindowOutputXStart(window), 0, AmigaConstants.PalLowResOverscanBorderX)
                : GetDisplayWindowOutputXStart(window) < AmigaConstants.PalLowResOverscanBorderX
                    ? StandardBitplanePrerollOrigin
                    : AmigaConstants.PalLowResOverscanBorderX;
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
            return _deniseDisplayWindow;
        }

        private int GetDisplayWindowStartLine()
        {
            return _deniseDisplayWindow.VerticalStart;
        }

        private int GetDisplayWindowStopLine(int vStart)
        {
            return _deniseDisplayWindow.VerticalStop;
        }

        private static int GetDisplayWindowOutputXStart(DisplayWindow window)
            => window.HorizontalStart - StandardHStart;

        private static int GetDisplayWindowOutputXStop(DisplayWindow window)
            => window.HorizontalStop - StandardHStart;

        private static int GetDisplayWindowOutputYStart(DisplayWindow window)
            => window.VerticalStart - StandardVStart;

        private static int GetDisplayWindowOutputYStop(DisplayWindow window)
            => window.VerticalStop - StandardVStart;

        private void RefreshDisplayGeometry()
        {
            _agnusDisplayWindow = DisplayGeometryDecoder.DecodeDisplayWindow(
                _chipset.DmaChip,
                _diwStart,
                _diwStop,
                _agnusDiwHigh,
                _agnusDiwHighValid);
            _deniseDisplayWindow = DisplayGeometryDecoder.DecodeDisplayWindow(
                _chipset.DisplayChip,
                _diwStart,
                _diwStop,
                _diwHigh,
                _diwHighValid);
            _dataFetchWindow = DisplayGeometryDecoder.DecodeDataFetchWindow(
                _chipset.DmaChip,
                _bplcon0,
                _ddfStart,
                _ddfStop);
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
            _liveDeniseDisplayWindowVerticallyOpen = false;
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

            targetLine = Math.Clamp(targetLine, 0, _timing.LongFrameLines - 1);
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
            targetLine = Math.Clamp(targetLine, 0, _timing.LongFrameLines - 1);
            while (_liveDisplayWindowStateLine <= targetLine)
            {
                var vStart = _agnusDisplayWindow.VerticalStart;
                var vStop = _agnusDisplayWindow.VerticalStop;
                if (_liveDisplayWindowStateLine == vStop)
                {
                    _liveDisplayWindowVerticallyOpen = false;
                }

                if (_liveDisplayWindowStateLine == vStart)
                {
                    _liveDisplayWindowVerticallyOpen = true;
                }

                if (_liveDisplayWindowStateLine == _deniseDisplayWindow.VerticalStop)
                {
                    _liveDeniseDisplayWindowVerticallyOpen = false;
                }

                if (_liveDisplayWindowStateLine == _deniseDisplayWindow.VerticalStart)
                {
                    _liveDeniseDisplayWindowVerticallyOpen = true;
                }

                _liveDisplayWindowStateLine++;
            }
        }

        private int GetDataFetchWordCount()
        {
            return DisplayGeometryDecoder.GetDataFetchWordCount(
                _dataFetchWindow,
                _ddfStart,
                _ddfStop,
                MaxBitplaneFetchWords);
        }

        private static bool IsSuperHighResolutionRequested(ushort bplcon0)
            => (bplcon0 & (0x8000 | Bplcon0SuperHires)) == Bplcon0SuperHires;

        internal static DeniseResolution GetDeniseResolution(DisplayChipModel deniseModel, ushort bplcon0)
        {
            if ((bplcon0 & 0x8000) != 0)
            {
                return DeniseResolution.HighRes;
            }

            return deniseModel.SupportsEcsRegisters() && IsSuperHighResolutionRequested(bplcon0)
                ? DeniseResolution.SuperHighRes
                : DeniseResolution.LowRes;
        }

        private DeniseResolution GetDeniseResolution(ushort bplcon0)
            => GetDeniseResolution(_chipset.DisplayChip, bplcon0);

        private DeniseResolution GetAgnusFetchResolution(ushort bplcon0)
        {
            return DisplayGeometryDecoder.GetDataFetchResolution(_chipset.DmaChip, bplcon0);
        }

        private static int GetResolutionSamplesPerLowResSpan(DeniseResolution resolution)
            => resolution switch
            {
                DeniseResolution.SuperHighRes => 4,
                DeniseResolution.HighRes => 2,
                _ => 1
            };

        private static int GetResolutionFetchSlotStride(DeniseResolution resolution)
            => resolution switch
            {
                DeniseResolution.SuperHighRes => 2,
                DeniseResolution.HighRes => 4,
                _ => 8
            };

        private int GetBitplaneFetchSlotStrideForBplcon0(ushort bplcon0)
            => GetResolutionFetchSlotStride(GetAgnusFetchResolution(bplcon0));

        private int GetRequestedBitplaneCount()
            => GetRequestedBitplaneCount(_bplcon0);

        private static int GetRequestedBitplaneCount(ushort bplcon0)
            => (bplcon0 >> 12) & 0x7;

        private int GetAgnusBitplaneFetchPlaneCount()
            => GetAgnusBitplaneFetchPlaneCount(_bplcon0);

        private int GetAgnusBitplaneFetchPlaneCount(ushort bplcon0)
            => GetAgnusBitplaneFetchPlaneCount(
                _chipset.DmaChip,
                _chipset.DisplayChip,
                GetAgnusFetchResolution(bplcon0),
                bplcon0);

        internal static int GetAgnusBitplaneFetchPlaneCount(
            DmaChipModel agnusModel,
            DisplayChipModel deniseModel,
            DeniseResolution resolution,
            ushort bplcon0)
        {
            var requested = GetRequestedBitplaneCount(bplcon0);
            if (requested <= 0)
            {
                return 0;
            }

            if (!agnusModel.SupportsEcsRegisters() && resolution == DeniseResolution.SuperHighRes)
            {
                resolution = DeniseResolution.LowRes;
            }

            if (agnusModel.SupportsEcsRegisters() && resolution == DeniseResolution.SuperHighRes)
            {
                return Math.Min(requested, SuperHighResBitplaneFetchSlotsByPlane.Length);
            }

            if (resolution == DeniseResolution.HighRes)
            {
                return Math.Min(requested, HighResBitplaneFetchSlotsByPlane.Length);
            }

            return !deniseModel.SupportsEcsRegisters() && requested == 7
                ? 4
                : Math.Min(requested, OcsEcsMaxBitplaneCount);
        }

        private int GetDeniseBitplaneDecodePlaneCount()
            => GetDeniseBitplaneDecodePlaneCount(_bplcon0);

        private int GetDeniseBitplaneDecodePlaneCount(ushort bplcon0)
            => GetDeniseBitplaneDecodePlaneCount(
                _chipset.DisplayChip,
                GetDeniseResolution(bplcon0),
                bplcon0);

        internal static int GetDeniseBitplaneDecodePlaneCount(
            DisplayChipModel deniseModel,
            DeniseResolution resolution,
            ushort bplcon0)
        {
            var requested = GetRequestedBitplaneCount(bplcon0);
            if (!deniseModel.SupportsEcsRegisters() && resolution == DeniseResolution.SuperHighRes)
            {
                resolution = DeniseResolution.LowRes;
            }

            if (resolution == DeniseResolution.SuperHighRes)
            {
                return Math.Min(requested, 2);
            }

            if (resolution == DeniseResolution.HighRes)
            {
                return Math.Min(requested, 4);
            }

            return Math.Clamp(requested, 0, OcsEcsMaxBitplaneCount);
        }

        private bool IsLatchedOnlyOcsBpu7Plane(ushort bplcon0, int plane)
            => !_chipset.SupportsEcsDisplayRegisters &&
                GetDeniseResolution(bplcon0) == DeniseResolution.LowRes &&
                GetRequestedBitplaneCount(bplcon0) == 7 &&
                plane >= 4 &&
                plane < OcsEcsMaxBitplaneCount;

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
            return _dataFetchWindow.Start;
        }

        private int GetDataFetchStopValue()
        {
            return _dataFetchWindow.Stop;
        }


    }
}
