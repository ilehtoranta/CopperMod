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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryCommitLivePaulaWord(
            in AgnusLiveSlotRequest liveRequest,
            out AmigaDmaWordExecutionResult execution,
            long? exactSlotCycle = null)
        {
            if (_agnusLivePaulaSlotKernel == null ||
                !_agnusLivePaulaEnabled ||
                !_agnusLiveBlitterEnabled ||
                !_agnusLiveCopperEnabled ||
                !_agnusLiveDisplayLedgerEnabled ||
                _chipset != AmigaChipset.OcsPal)
            {
                throw new InvalidOperationException(
                    "The G4L live Paula kernel is not enabled for this bus.");
            }

            var address = MaskChipDmaAddress(liveRequest.Address);
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(
                Math.Max(
                    liveRequest.EarliestEligibleCycle,
                    exactSlotCycle ?? liveRequest.EarliestEligibleCycle));
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                slotCycle,
                isWrite: false,
                liveRequest.Channel);

            if (slotCycle <= _chipDataBusLatchCycle ||
                !TryReserveExactFixedDmaSlot(request, out var access))
            {
                var denied = new AmigaBusAccessResult(
                    request,
                    slotCycle,
                    slotCycle);
                execution = new AmigaDmaWordExecutionResult(
                    value: 0,
                    granted: false,
                    denied);
                return false;
            }

            CaptureDmaAccess(access);
            execution = ExecuteDmaWordRead(address, granted: true, access);
            return true;
        }
    }
}
