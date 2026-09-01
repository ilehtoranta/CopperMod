/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.CustomChips.Agnus
{
    internal enum AgnusCopperDmaPhaseKind : byte
    {
        None,
        Normal,
        WrapDummy
    }

    /// <summary>
    /// Copper clock opportunities, not DMA reservations. A normal input may
    /// advance control or request a word, depending on the current Copper state
    /// and incoming RGA availability. Output describes the preceding input;
    /// it does not imply that a request was issued on that input.
    /// </summary>
    internal readonly record struct AgnusCopperDmaPhase(
        int NominalHorizontal,
        AgnusCopperDmaPhaseKind Input,
        AgnusCopperDmaPhaseKind Output)
    {
        public bool ControlEligible => Input == AgnusCopperDmaPhaseKind.Normal;
    }

    /// <summary>
    /// Pure Copper phase classification for uninterrupted 227- or 228-CCK
    /// horizontal-counter lines. The caller supplies the active physical line
    /// length; this does not select a video standard or model programmable
    /// counter jumps, DMA ownership, WAIT comparison, or register visibility.
    /// </summary>
    internal static class AgnusCopperDmaPhases
    {
        // Same coordinate origin as AgnusHrmOcsSlotTable: physical refresh
        // positions 0/2/4/6 correspond to nominal HPOS 3/5/7/9.
        public const int PhysicalToNominalOffsetCcks = 3;
        public const int InputToOutputDelayCcks = 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AgnusCopperDmaPhase Classify(
            int physicalHorizontal,
            int lineLengthCcks)
        {
            if (lineLengthCcks is not (227 or 228))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lineLengthCcks),
                    lineLengthCcks,
                    "Copper phase classification requires an explicit 227- or 228-CCK line.");
            }

            if ((uint)physicalHorizontal >= (uint)lineLengthCcks)
            {
                throw new ArgumentOutOfRangeException(nameof(physicalHorizontal));
            }

            var nominal = physicalHorizontal + PhysicalToNominalOffsetCcks;
            if (nominal >= lineLengthCcks)
            {
                nominal -= lineLengthCcks;
            }

            var previous = PreviousHorizontal(nominal, lineLengthCcks);
            var beforePrevious = PreviousHorizontal(previous, lineLengthCcks);
            return new AgnusCopperDmaPhase(
                nominal,
                ClassifyInput(previous, nominal),
                ClassifyInput(beforePrevious, previous));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PreviousHorizontal(int horizontal, int lineLengthCcks)
            => horizontal == 0 ? lineLengthCcks - 1 : horizontal - InputToOutputDelayCcks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AgnusCopperDmaPhaseKind ClassifyInput(int previous, int current)
        {
            var toggled = ((previous ^ current) & 1) != 0;
            if ((current & 1) != 0)
            {
                return toggled
                    ? AgnusCopperDmaPhaseKind.Normal
                    : AgnusCopperDmaPhaseKind.None;
            }

            // A 227-CCK wrap repeats the even polarity (226 -> 0). A pending
            // fetch can issue a dummy word here without advancing Copper
            // control; a 228-CCK wrap (227 -> 0) has no such input.
            return toggled
                ? AgnusCopperDmaPhaseKind.None
                : AgnusCopperDmaPhaseKind.WrapDummy;
        }
    }
}
