/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        internal bool TryAcceptBitplaneInput(
            uint address, long inputCycle, out AmigaBusAccessResult access)
            => _agnusBusExecutor.TryAcceptBitplaneInput(address, inputCycle, out access);

        internal AmigaDmaWordExecutionResult ExecuteAcceptedBitplaneWord(
            int plane, in AmigaBusAccessResult access)
            => _agnusBusExecutor.ExecuteAcceptedBitplaneWord(plane, in access);

        internal AmigaDmaWordExecutionResult ReadAcceptedBitplaneWord(in AmigaBusAccessResult access)
        {
            if (!_hrmSlotEngine.MatchesAcceptedBitplaneWord(in access) ||
                access.Request.Address != MaskChipDmaAddress(access.Request.Address) ||
                access.GrantedCycle < _chipDataBusLatchCycle)
            {
                throw new InvalidOperationException("The bitplane output does not match its accepted physical reservation.");
            }

            var execution = ExecuteDmaWordRead(access.Request.Address, granted: true, access);
            CaptureDmaAccess(access);
            return execution;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryCommitLiveFixedDisplayRead(
            AgnusLiveSlotRequest liveRequest,
            out AmigaDmaWordExecutionResult execution)
        {
            if (_agnusLiveFixedDisplaySlotKernel == null ||
                !_agnusLiveDisplayLedgerEnabled ||
                _chipset != AmigaChipset.OcsPal)
            {
                throw new InvalidOperationException(
                    "The G1L fixed-display slot kernel is not enabled for this bus.");
            }

            var address = MaskChipDmaAddress(liveRequest.Address);
            var requestedCycle = liveRequest.RequestedCycle;
            var requester = liveRequest.Owner == AgnusChipSlotOwner.Bitplane
                ? AmigaBusRequester.Bitplane
                : AmigaBusRequester.Sprite;
            var request = new AmigaBusAccessRequest(
                requester,
                liveRequest.Kind,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            if (liveRequest.Owner == AgnusChipSlotOwner.Bitplane &&
                liveRequest.EarliestEligibleCycle - requestedCycle == AgnusChipSlotScheduler.SlotCycles)
            {
                var accepted = new AmigaBusAccessResult(request,
                    liveRequest.EarliestEligibleCycle,
                    checked(liveRequest.EarliestEligibleCycle + AgnusChipSlotScheduler.SlotCycles));
                execution = ExecuteAcceptedBitplaneWord(liveRequest.Channel, in accepted);
                return true;
            }

            if (requestedCycle <= ExecutedChipBusHorizon)
            {
                var staleAccess = new AmigaBusAccessResult(
                    request,
                    requestedCycle,
                    requestedCycle);
                execution = new AmigaDmaWordExecutionResult(
                    value: 0,
                    granted: false,
                    staleAccess);
                return false;
            }

            AmigaBusAccessResult access;
            bool granted;
            if (!_useChipSlotScheduler)
            {
                access = new AmigaBusAccessResult(
                    request,
                    requestedCycle,
                    requestedCycle);
                granted = true;
            }
            else if (liveRequest.Owner == AgnusChipSlotOwner.Bitplane)
            {
                // The row plan has already selected this A500 PAL OCS
                // bitplane slot. Commit its one physical word directly in the
                // shared slot state without entering the legacy causal
                // executor.
                granted = _hrmSlotEngine.TryCommitPlannedBitplaneSlot(
                    address,
                    requestedCycle,
                    out access);
            }
            else
            {
                granted = _hrmSlotEngine.TryReserveExactFixedDmaSlot(
                    request,
                    out access);
            }

            CaptureDmaAccess(access);
            execution = ExecuteDmaWordRead(address, granted, access);
            return granted;
        }
    }
}
