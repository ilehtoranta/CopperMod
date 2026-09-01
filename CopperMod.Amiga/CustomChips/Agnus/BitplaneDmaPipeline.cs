/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Agnus
{
    internal readonly record struct BitplaneDmaPlanProvenance(
        bool IsPlanBacked,
        int Generation,
        int DmaPlanVersion,
        int Signature,
        int EntryIndex);

    internal readonly record struct BitplaneDmaWordMetadata(
        int Row,
        int Plane,
        int Word,
        int StorageWord,
        int Slot,
        bool AddressValid,
        bool LastWord,
        short Modulo)
    {
        public BitplaneDmaPlanProvenance Plan { get; init; }
    }

    /// <summary>
    /// One accepted bitplane address and its following RAM-output phase.
    /// Row-plan changes cannot alter this value; scratch execution copies it.
    /// </summary>
    internal struct BitplaneDmaPipeline
    {
        public bool HasOutput { get; private set; }
        public BitplaneDmaWordMetadata Word { get; private set; }
        public AmigaBusAccessResult Access { get; private set; }
        public bool Granted { get; private set; }
        public bool PointerWrittenAfterInput { get; private set; }
        public long OutputCycle => HasOutput ? Access.GrantedCycle : long.MaxValue;

        public void Accept(
            in BitplaneDmaWordMetadata word,
            in AmigaBusAccessResult access,
            bool granted)
        {
            if (HasOutput || (uint)word.Plane >= 8 || word.Row < 0 || word.Word < 0 ||
                word.StorageWord < 0 || access.Request.Requester != AmigaBusRequester.Bitplane ||
                access.Request.Kind != AmigaBusAccessKind.Bitplane ||
                access.Request.Target != AmigaBusAccessTarget.ChipRam ||
                access.Request.Size != AmigaBusAccessSize.Word || access.Request.IsWrite ||
                access.RequestedCycle < 0 || access.RequestedCycle % AgnusChipSlotScheduler.SlotCycles != 0 ||
                access.GrantedCycle - access.RequestedCycle != AgnusChipSlotScheduler.SlotCycles ||
                access.CompletedCycle - access.GrantedCycle != (granted ? AgnusChipSlotScheduler.SlotCycles : 0))
            {
                throw new InvalidOperationException("A bitplane input must freeze exactly one following output phase.");
            }

            Word = word;
            Access = access;
            Granted = granted;
            HasOutput = true;
            PointerWrittenAfterInput = false;
        }

        public void ObservePointerWrite(int plane)
        {
            if (HasOutput && Word.Plane == plane)
            {
                PointerWrittenAfterInput = true;
            }
        }

        public BitplaneDmaPipeline Complete(long outputCycle)
        {
            if (!HasOutput || OutputCycle != outputCycle)
            {
                throw new InvalidOperationException("A bitplane output must complete at its accepted physical cycle.");
            }

            var completed = this;
            this = default;
            return completed;
        }
    }
}
