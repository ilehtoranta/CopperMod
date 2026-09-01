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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryAcceptCopperInput(
            uint address,
            long requestedCycle,
            long inputCycle,
            out AmigaBusAccessResult access)
            => _agnusBusExecutor.TryAcceptCopperInput(
                address, requestedCycle, inputCycle, out access);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal AmigaDmaWordExecutionResult ExecuteAcceptedCopperWord(in AmigaBusAccessResult access)
            => _agnusBusExecutor.ExecuteAcceptedCopperWord(in access);

        internal AmigaDmaWordExecutionResult ReadAcceptedCopperWord(in AmigaBusAccessResult access)
        {
            if (!_hrmSlotEngine.MatchesAcceptedCopperWord(in access) ||
                access.Request.Address != MaskChipDmaAddress(access.Request.Address) ||
                access.GrantedCycle < _chipDataBusLatchCycle)
            {
                var owner = _hrmSlotEngine.TryGetCommittedSlotOwner(access.GrantedCycle, out var committed)
                    ? committed.ToString() : "Free";
                throw new InvalidOperationException(
                    $"The Copper output does not match a current accepted physical reservation: " +
                    $"address=${access.Request.Address:X6}, request={access.RequestedCycle}, " +
                    $"OUT={access.GrantedCycle}, complete={access.CompletedCycle}, owner={owner}, " +
                    $"busHorizon={_chipDataBusLatchCycle}, executed={_agnusBusExecutor.ExecutedThroughCycle}.");
            }

            var execution = ExecuteDmaWordRead(access.Request.Address, granted: true, access);
            CaptureDmaAccess(access);
            return execution;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryCommitLiveCopperRead(
            in AgnusLiveSlotRequest liveRequest,
            out AmigaDmaWordExecutionResult execution)
        {
            if (_agnusLiveCopperSlotKernel == null ||
                !_agnusLiveCopperEnabled ||
                !_agnusLiveDisplayLedgerEnabled ||
                _chipset != AmigaChipset.OcsPal)
            {
                throw new InvalidOperationException(
                    "The G2L live Copper kernel is not enabled for this bus.");
            }

            var address = MaskChipDmaAddress(liveRequest.Address);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                liveRequest.RequestedCycle,
                isWrite: false);
            var slotCycle = liveRequest.EarliestEligibleCycle;
            if (liveRequest.Channel is 4 or 5 or 6)
            {
                var accepted = new AmigaBusAccessResult(
                    request, slotCycle, checked(slotCycle + AgnusChipSlotScheduler.SlotCycles));
                execution = ExecuteAcceptedCopperWord(in accepted);
                return true;
            }

            var preservePhysicalPhaseAcrossLine =
                liveRequest.Channel is 2 or 3;
            var granted = _hrmSlotEngine.TryReserveCopperDmaWordExactSlot(
                address,
                liveRequest.RequestedCycle,
                slotCycle,
                preservePhysicalPhaseAcrossLine,
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
            execution = ExecuteDmaWordRead(address, granted: true, access);
            return true;
        }
    }
}
