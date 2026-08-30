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
