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
        internal long GetLiveDiskSlotCycle(long requestedCycle)
        {
            if (!_agnusLiveDiskEnabled || _chipset != AmigaChipset.OcsPal)
            {
                throw new InvalidOperationException(
                    "The G5L live disk kernel is not enabled for this bus.");
            }

            var causalCycle = _chipDataBusLatchCycle >= requestedCycle
                ? _chipDataBusLatchCycle + 1
                : requestedCycle;
            return AgnusHrmOcsSlotTable.AlignToDiskSlot(causalCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryCommitLiveDiskWord(
            in AgnusLiveSlotRequest liveRequest,
            out AmigaDmaWordExecutionResult execution)
        {
            if (_agnusLiveDiskSlotKernel == null ||
                !_agnusLiveDiskEnabled ||
                !_agnusLivePaulaEnabled ||
                !_agnusLiveBlitterEnabled ||
                !_agnusLiveCopperEnabled ||
                !_agnusLiveDisplayLedgerEnabled ||
                _chipset != AmigaChipset.OcsPal)
            {
                throw new InvalidOperationException(
                    "The G5L live disk kernel is not enabled for this bus.");
            }

            var address = MaskChipDmaAddress(liveRequest.Address);
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(
                liveRequest.EarliestEligibleCycle);
            var isChipWrite = liveRequest.Transfer == AgnusLiveWordTransfer.Write;
            // Preserve the established disk-access trace contract: IsWrite
            // describes disk write mode, whose Chip-RAM transfer is a read.
            var isDiskWriteMode = !isChipWrite;
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Disk,
                AmigaBusAccessKind.DiskDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                liveRequest.RequestedCycle,
                isDiskWriteMode);
            var reservationRequest = new AmigaBusAccessRequest(
                AmigaBusRequester.Disk,
                AmigaBusAccessKind.DiskDma,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessSize.Word,
                slotCycle,
                isDiskWriteMode);

            if (slotCycle <= _chipDataBusLatchCycle)
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

            if (!TryReserveExactFixedDmaSlot(
                    reservationRequest,
                    out var reservedAccess) ||
                slotCycle <= _chipDataBusLatchCycle)
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

            var access = new AmigaBusAccessResult(
                request,
                reservedAccess.GrantedCycle,
                reservedAccess.CompletedCycle);
            CaptureDmaAccess(access);
            execution = isChipWrite
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
