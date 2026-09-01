/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        private enum PhysicalShifterLineStatus : byte
        {
            Unchecked,
            Unsupported,
            Decoded
        }

        private bool TryRenderTimelinePhysicalShifter(
            Span<uint> bgra, int row, DisplayLineSegment segment,
            DisplayFrameTimeline timeline)
        {
            if (!UsesPhysicalBitplanePipeline ||
                !TryPreparePhysicalShifterLine(row, timeline, out var line))
            {
                return false;
            }

            var window = GetEffectiveDisplayWindow();
            var firstX = Math.Max(Math.Max(0, GetDisplayWindowOutputXStart(window)), segment.XStart);
            var lastX = Math.Min(Math.Min(LowResWidth, GetDisplayWindowOutputXStop(window)), segment.XStop);
            if (row < Math.Max(0, GetDisplayWindowOutputYStart(window)) ||
                row >= Math.Min(ActiveLowResOutputHeight, GetDisplayWindowOutputYStop(window)))
            {
                return true;
            }

            // DMA enable and BPU describe future transfers/reloads. Neither
            // removes bits which have already entered a serial shifter.
            for (var x = firstX; x < lastX; x++)
            {
                var colorIndex = line.PhysicalShifterPixels[x];
                var priority = colorIndex == 0 ? (byte)0 : NormalPlayfieldPriorityMask;
                SetPlayfieldPriorityMask(x, row, priority);
                if (colorIndex != 0)
                {
                    RecordBitplanePixel(colorIndex, priority, x, row);
                }
                WriteLowResolutionOutputPixel(bgra, x, row, ConvertColorIndex(colorIndex));
            }

            return true;
        }

        private bool TryPreparePhysicalShifterLine(
            int row, DisplayFrameTimeline timeline, out DisplayLineTimeline line)
        {
            line = timeline.GetLine(row);
            if (line.PhysicalShifterStatus != PhysicalShifterLineStatus.Unchecked)
            {
                return line.PhysicalShifterStatus == PhysicalShifterLineStatus.Decoded;
            }

            line.PhysicalShifterStatus = PhysicalShifterLineStatus.Unsupported;
            if (!timeline.HasLine(row) || line.SegmentCount < 2 || line.HasManualBitplaneDataWrite ||
                HasBitplaneDataSpanInBand(row, row + 1, 0, LowResWidth))
            {
                return false;
            }

            var first = timeline.GetState(line.Segments[0].StateIndex);
            var firstPlaneCount = GetDeniseBitplaneDecodePlaneCount(first.Bplcon0);
            var hasPlaneCountTransition = false;
            for (var index = 0; index < line.SegmentCount; index++)
            {
                var state = timeline.GetState(line.Segments[index].StateIndex);
                // Uniform rows keep their existing word-at-a-time fast path.
                // Mode/serial-clock changes and manual data writes require a
                // separate transition; do not infer them from a BPU change.
                if (state.Resolution != DeniseResolution.LowRes ||
                    (state.Bplcon0 & 0x8C04) != 0 ||
                    GetRequestedBitplaneCount(state.Bplcon0) > OcsEcsMaxBitplaneCount ||
                    state.DdfStart != first.DdfStart || state.DdfStop != first.DdfStop)
                {
                    return false;
                }
                hasPlaneCountTransition |=
                    GetDeniseBitplaneDecodePlaneCount(state.Bplcon0) != firstPlaneCount;
            }

            if (!hasPlaneCountTransition)
            {
                return false;
            }

            Span<int> nextWords = stackalloc int[OcsEcsMaxBitplaneCount];
            Span<ushort> dataLatches = stackalloc ushort[OcsEcsMaxBitplaneCount];
            Span<ushort> intermediate = stackalloc ushort[OcsEcsMaxBitplaneCount];
            Span<ushort> shifters = stackalloc ushort[OcsEcsMaxBitplaneCount];
            nextWords.Clear();
            dataLatches.Clear();
            intermediate.Clear();
            shifters.Clear();
            for (var plane = 0; plane < OcsEcsMaxBitplaneCount; plane++)
            {
                var mask = line.BitplaneFetchMasks[plane] & ~line.BitplaneDeniedMasks[plane];
                for (var word = 0; word < MaxBitplaneFetchWords; word++)
                {
                    if ((mask & ((UInt128)1 << word)) != 0 &&
                        (line.BitplaneOutputCycleOffsets[(plane * MaxBitplaneFetchWords) + word] == ushort.MaxValue ||
                        (plane == 0 && line.BitplaneOutputCycleOffsets[word] < StandardHStart)))
                    {
                        // The cropped timeline cannot recover pre-origin
                        // control phases. Do not invent an initial cohort from
                        // the manual BPLxDAT register snapshot in that case.
                        return false;
                    }
                }
            }

            var sampleCounter = GetNextPhysicalShifterSample(line, nextWords, out var samplePlane);
            var stateIndex = 0;
            var control = first;
            var pendingParity = 0;
            var definedDataMask = 0;
            var definedIntermediateMask = 0;
            for (var counter = 0; counter < StandardHStart + LowResWidth; counter++)
            {
                var x = counter - StandardHStart;
                while (stateIndex + 1 < line.SegmentCount &&
                    line.Segments[stateIndex + 1].XStart <= x)
                {
                    control = timeline.GetState(line.Segments[++stateIndex].StateIndex);
                }

                while (sampleCounter <= counter)
                {
                    var word = nextWords[samplePlane]++;
                    dataLatches[samplePlane] =
                        line.BitplaneWords[(samplePlane * MaxBitplaneFetchWords) + word];
                    definedDataMask |= 1 << samplePlane;
                    if (samplePlane == 0)
                    {
                        // BPL1's input phase freezes the complete intermediate
                        // cohort. Later DMA words update only the data latches.
                        dataLatches.CopyTo(intermediate);
                        definedIntermediateMask = definedDataMask;
                        pendingParity = 3;
                    }
                    sampleCounter = GetNextPhysicalShifterSample(line, nextWords, out samplePlane);
                }

                var colorIndex = 0;
                for (var plane = 0; plane < OcsEcsMaxBitplaneCount; plane++)
                {
                    colorIndex |= (shifters[plane] >> 15) << plane;
                    shifters[plane] <<= 1;
                }
                if ((uint)x < (uint)LowResWidth)
                {
                    line.PhysicalShifterPixels[x] = (byte)colorIndex;
                }

                // The pixel is emitted and shifted before its comparator can
                // load the next word. BPU gates this load, not the old bits.
                var planeCount = GetDeniseBitplaneDecodePlaneCount(control.Bplcon0);
                for (var parity = 0; parity < 2; parity++)
                {
                    var flag = 1 << parity;
                    var scroll = (control.Bplcon1 >> (parity * 4)) & 15;
                    if ((pendingParity & flag) == 0 || (counter & 15) != scroll)
                    {
                        continue;
                    }
                    for (var plane = parity; plane < planeCount; plane += 2)
                    {
                        if ((definedIntermediateMask & (1 << plane)) == 0)
                        {
                            // This line does not provide an authoritative
                            // replacement for a preceding raster's data latch.
                            // Leave its existing path in charge until that
                            // cross-line seed is explicitly represented.
                            return false;
                        }
                        shifters[plane] = intermediate[plane];
                    }
                    // Even a suppressed load consumes the pending request.
                    // Re-enabling BPU cannot revive that skipped word.
                    pendingParity &= ~flag;
                }
            }

            line.PhysicalShifterStatus = PhysicalShifterLineStatus.Decoded;
            return true;
        }

        private int GetNextPhysicalShifterSample(
            DisplayLineTimeline line, Span<int> nextWords, out int nextPlane)
        {
            var nextCounter = int.MaxValue;
            nextPlane = -1;
            for (var plane = 0; plane < OcsEcsMaxBitplaneCount; plane++)
            {
                var mask = line.BitplaneFetchMasks[plane] & ~line.BitplaneDeniedMasks[plane];
                var word = nextWords[plane];
                while (word < MaxBitplaneFetchWords && (mask & ((UInt128)1 << word)) == 0)
                {
                    word++;
                }
                nextWords[plane] = word;
                if (word >= MaxBitplaneFetchWords)
                {
                    continue;
                }

                var outputOffset = line.BitplaneOutputCycleOffsets[(plane * MaxBitplaneFetchWords) + word];
                var outputHorizontal = outputOffset / CopperHpCycles;
                // These are already committed RAM OUT cycles. BPLxDAT enters
                // Denise at the next input phase, using the existing horizontal
                // counter origin. No request, grant or display offset changes.
                var counter = GetOcsPaletteOutputX(outputHorizontal + 1) + StandardHStart;
                if (counter < nextCounter)
                {
                    nextCounter = counter;
                    nextPlane = plane;
                }
            }
            return nextCounter;
        }
    }
}
