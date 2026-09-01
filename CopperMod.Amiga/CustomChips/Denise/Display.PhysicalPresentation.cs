/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        internal static int GetOcsPaletteOutputX(int physicalHorizontal)
            // STRHOR is sampled through the next input stage, then loads the
            // Denise counter. Physical h2 has counter2; each CCK contributes
            // two low-resolution pixels. The existing output crop begins at
            // counter98. This changes coordinates, never the transfer cycle.
            // Unwrapped positions also describe the preceding row's h0/h1 tail.
            => (2 * (physicalHorizontal - 1)) - StandardHStart;

        private void RecordPhysicalDisplayWrite(long cycle, ushort offset, bool isCopper)
        {
            // Palette data is consumed at OUT; these Denise control registers
            // are consumed by the following input stage. Both use the same
            // counter origin, independent of DDF and the bus requester.
            var displayCycle = offset is 0x08E or 0x090 or 0x100 or 0x102 or 0x104 or 0x106 or 0x1E4
                ? checked(cycle + CopperHpCycles)
                : cycle;
            var row = GetOutputRowForCycle(_liveFrameStartCycle, displayCycle);
            var rowValid = (uint)row < (uint)LowResOutputHeight;
            var horizontal = _bus.GetBeamPosition(displayCycle).BeamHorizontal;
            var x = Math.Clamp(GetOcsPaletteOutputX(horizontal), 0, LowResWidth);
            var recordCurrentLine = rowValid && IsLiveLineValid(row) && _displayTimeline.HasLine(row);
            if (rowValid)
            {
                CaptureTimelineDisplayTransition(cycle, row, x, offset, isCopper, recordCurrentLine);
            }
            if (recordCurrentLine)
            {
                var snapshot = CaptureTimelineStateSnapshot(row, GetLiveLineState(row));
                _displayTimeline.RecordDisplayChange(row, x, snapshot,
                    IsTimelineUnsafeDisplayWrite(offset), offset, isCopper);
            }

            // Before the horizontal strobe resets Denise, h0/h1 still emit
            // the preceding raster's last pixels. They are an unwrapped
            // counter continuation, not a WAIT-dependent presentation delay.
            var wrappedX = GetOcsPaletteOutputX(horizontal + CopperHorizontalUnitsPerLine);
            var wrappedRow = row - 1;
            if ((uint)wrappedRow < (uint)LowResOutputHeight &&
                (uint)wrappedX < (uint)LowResWidth &&
                IsLiveLineValid(wrappedRow) && _displayTimeline.HasLine(wrappedRow))
            {
                var snapshot = CaptureTimelineStateSnapshot(wrappedRow,
                    GetLiveLineState(wrappedRow));
                _displayTimeline.RecordDisplayChange(wrappedRow, wrappedX, snapshot,
                    IsTimelineUnsafeDisplayWrite(offset), offset, isCopper);
            }
            if (_displayTimeline.SegmentCount > MaxTimelineSegmentsPerFrame)
            {
                _liveTimelineUnsafeForFrame = true;
            }
        }

        private void CaptureTimelineDisplayTransition(
            long cycle, int row, int x, ushort offset, bool isCopper,
            bool recordCurrentLine, int waitPresentationPixelOffset = 0,
            int fivePlaneWaitMovePixelOffset = 0)
        {
            if (!_bus.BusAccessCaptureEnabled)
            {
                return;
            }
            var transitions = _copperPresentationTransitions ??=
                new List<CopperPresentationTransitionTrace>(256);
            if (transitions.Count >= MaxCapturedCopperWaitTransitions)
            {
                return;
            }
            var lineState = GetLiveLineState(row);
            transitions.Add(new CopperPresentationTransitionTrace(
                cycle, row, x, offset,
                _colors[offset >= 0x180 && offset < 0x1C0 ? (offset - 0x180) >> 1 : 0],
                isCopper, recordCurrentLine,
                lineState.Bplcon0, lineState.Dmacon, lineState.PlaneCount,
                lineState.PlaneHasRowMask,
                lineState.BitplaneBaseRows[0], lineState.BitplaneBaseRows[1],
                lineState.BitplaneBaseRows[2], lineState.BitplaneBaseRows[3],
                lineState.BitplaneBaseRows[4],
                _liveCopper.WaitFirst, _liveCopper.SatisfiedWaitRunCount,
                _liveCopper.WaitRunBeganWithBlockingComparison, _liveCopper.WaitRunControlBlocked,
                _liveCopper.WaitComparisonStartCycle, _liveCopper.WaitSatisfiedCycle,
                _liveCopper.WaitRestartCycle,
                IsBitplaneRgaDecisionPhase(_liveCopper.WaitRestartCycle),
                IsBitplaneRgaIncomingPhase(_liveCopper.WaitRestartCycle),
                IsBitplaneRgaOutputPhase(_liveCopper.WaitRestartCycle),
                _liveCopper.PendingInstructionSecondWordRequestedCycle,
                waitPresentationPixelOffset, fivePlaneWaitMovePixelOffset));
        }
    }
}
