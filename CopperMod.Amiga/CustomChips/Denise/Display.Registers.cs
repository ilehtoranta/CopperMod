/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
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
                cycle < GetLiveFrameStopCycle())
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
            _lastTimelineMissingBitplaneFallbackCount = 0;
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


    }
}
