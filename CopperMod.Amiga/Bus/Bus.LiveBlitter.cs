/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        private AgnusChipSlotOwner _liveBlitterPublishedIntentBlocker;

        internal bool BlitterNastyPriorityEnabled =>
            (Paula.Dmacon & 0x0400) != 0;

        internal AgnusChipSlotOwner LastLiveBlitterDeniedOwner =>
            _liveBlitterPublishedIntentBlocker != AgnusChipSlotOwner.Free
                ? _liveBlitterPublishedIntentBlocker
                :
            _hrmSlotEngine.LastDeniedFixedSlotBlocker?.Owner ??
            AgnusChipSlotOwner.Free;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PublishLiveBlitterPriority()
        {
            if (_agnusLiveBlitterEnabled)
            {
                SynchronizeHrmBlitterPriority();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryCommitLiveBlitterWord(
            in AgnusLiveSlotRequest liveRequest,
            out AmigaDmaWordExecutionResult execution)
        {
            if (_agnusLiveBlitterSlotKernel == null ||
                !_agnusLiveBlitterEnabled ||
                !_agnusLiveCopperEnabled ||
                !_agnusLiveDisplayLedgerEnabled ||
                _chipset != AmigaChipset.OcsPal)
            {
                throw new InvalidOperationException(
                    "The G3L live blitter kernel is not enabled for this bus.");
            }

            var address = MaskChipDmaAddress(liveRequest.Address);
            var isWrite = liveRequest.Transfer == AgnusLiveWordTransfer.Write;
            _liveBlitterPublishedIntentBlocker = AgnusChipSlotOwner.Free;
            PublishLiveBlitterPriority();
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(
                liveRequest.EarliestEligibleCycle);
            if (Display.HasPublishedLiveCopperWordClaimAt(slotCycle))
            {
                // The dynamic requesters publish independently, but the
                // physical arbiter still resolves all already-published
                // intents before committing a word. Copper outranks the
                // blitter even when BLTPRI becomes visible on this exact slot.
                _liveBlitterPublishedIntentBlocker =
                    AgnusChipSlotOwner.Copper;
                var request = new AmigaBusAccessRequest(
                    AmigaBusRequester.Blitter,
                    AmigaBusAccessKind.Blitter,
                    AmigaBusAccessTarget.ChipRam,
                    address,
                    AmigaBusAccessSize.Word,
                    liveRequest.RequestedCycle,
                    isWrite);
                execution = new AmigaDmaWordExecutionResult(
                    value: 0,
                    granted: false,
                    new AmigaBusAccessResult(
                        request,
                        slotCycle,
                        slotCycle));
                return false;
            }

            var granted = _hrmSlotEngine.TryReserveBlitterDmaWordExactSlot(
                address,
                liveRequest.RequestedCycle,
                slotCycle,
                isWrite,
                out var access);
            if (!granted)
            {
                execution = new AmigaDmaWordExecutionResult(
                    value: 0,
                    granted: false,
                    access);
                return false;
            }

            CaptureDmaAccess(access);
            execution = isWrite
                ? ExecuteDmaWordWrite(
                    address,
                    liveRequest.WriteValue,
                    granted: true,
                    access)
                : ExecuteDmaWordRead(address, granted: true, access);
            return true;
        }
    }
}
