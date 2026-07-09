/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga
{
    internal sealed partial class OcsDisplay
    {
        internal bool HasLiveDmaCapturedThrough(long cycle)
        {
            return _liveFrameValid &&
                cycle >= _liveFrameStartCycle &&
                cycle <= _liveCapturedThroughCycle;
        }

        internal void CaptureLiveDisplayDmaBeforeHrmGrant(long requestedCycle)
        {
            if (!_liveDmaEnabled ||
                !_liveFrameValid)
            {
                return;
            }

            if (_advancingLiveDma)
            {
                CaptureLiveDisplayDmaBeforeHrmGrant(requestedCycle, includeCopper: false);
                return;
            }

            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                CaptureLiveDisplayDmaBeforeHrmGrant(requestedCycle, includeCopper: true);
            }
            finally
            {
                EndLiveDmaCapture(savedAdvancingLiveDma);
            }
        }

        internal void PrepareLiveDisplaySlotsBeforeHrmGrant(long requestedCycle)
        {
            if (!_liveDmaEnabled ||
                !_liveFrameValid)
            {
                return;
            }

            if (_advancingLiveDma)
            {
                PrepareLiveDisplaySlotsBeforeHrmGrant(requestedCycle, includeCopper: false);
                return;
            }

            var savedAdvancingLiveDma = BeginLiveDmaCapture();
            try
            {
                PrepareLiveDisplaySlotsBeforeHrmGrant(requestedCycle, includeCopper: true);
            }
            finally
            {
                EndLiveDmaCapture(savedAdvancingLiveDma);
            }
        }

        internal bool TryRunCpuWaitLiveDmaScratch(
            AgnusHrmSlotEngine slotShadow,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out OcsLiveDmaScratchResult result)
        {
            result = default;
            if (!_liveDmaEnabled ||
                !_liveFrameValid ||
                !HasLiveDisplayWork())
            {
                return false;
            }

            if (size == AmigaBusAccessSize.Long &&
                isWrite &&
                target is not (AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.CustomRegisters))
            {
                result = OcsLiveDmaScratchResult.Unsupported("size-long-write");
                return false;
            }

            if (isWrite &&
                target is (AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.CustomRegisters) &&
                !scratchWrite.HasValue)
            {
                result = OcsLiveDmaScratchResult.Unsupported("scratch-write");
                return false;
            }

            var scratch = new LiveDmaScratchContext(this, slotShadow, scratchWrite);
            return scratch.TryRunCpuWaitGrant(
                kind,
                target,
                address,
                size,
                Math.Max(0, requestedCycle),
                isWrite,
                out result);
        }

        internal bool HasLiveDisplaySlotPreparationWorkBeforeHrmGrant(long requestedCycle)
        {
            if (!_liveDmaEnabled ||
                !_liveFrameValid)
            {
                return false;
            }

            var before = _bus.FindHrmDmaCandidate(requestedCycle);
            var frameStopCycle = GetLiveFrameStopCycle();
            return before > _liveCapturedThroughCycle &&
                before < frameStopCycle &&
                HasLiveSlotPreparationWorkThrough(before, includeCopper: !_advancingLiveDma);
        }

        private void CaptureLiveDisplayDmaBeforeHrmGrant(long requestedCycle, bool includeCopper)
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var before = _bus.FindHrmDmaCandidate(requestedCycle);
                var frameStopCycle = GetLiveFrameStopCycle();
                if (before <= _liveCapturedThroughCycle ||
                    before >= frameStopCycle ||
                    !HasLivePreGrantWorkThrough(before, includeCopper))
                {
                    return;
                }

                if (before < frameStopCycle)
                {
                    AdvanceLiveDmaWithinFrame(before, includeCopper);
                }

                CaptureLiveBitplaneDmaBeforeHrmGrant(requestedCycle);
                CaptureLiveSpriteDmaBeforeHrmGrant(requestedCycle);
                var after = _bus.IsHrmChipSlotReserved(before)
                    ? _bus.FindHrmDmaCandidate(before + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (after == before)
                {
                    return;
                }
            }
        }

        private void PrepareLiveDisplaySlotsBeforeHrmGrant(long requestedCycle, bool includeCopper)
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var before = _bus.FindHrmDmaCandidate(requestedCycle);
                var frameStopCycle = GetLiveFrameStopCycle();
                if (before <= _liveCapturedThroughCycle ||
                    before >= frameStopCycle ||
                    !HasLiveSlotPreparationWorkThrough(before, includeCopper))
                {
                    return;
                }

                if (before < frameStopCycle)
                {
                    AdvanceLiveRegisterEventsWithinFrame(before, includeCopper);
                    PrepareKnownLiveBitplaneSlotsThrough(before);
                }

                var after = _bus.IsHrmChipSlotReserved(before)
                    ? _bus.FindHrmDmaCandidate(before + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (after == before)
                {
                    return;
                }
            }
        }

        private bool HasLivePreGrantWorkThrough(long candidateCycle, bool includeCopper)
        {
            return GetNextLiveWorkCycle(includeCopper) <= candidateCycle;
        }

        private bool HasLiveSlotPreparationWorkThrough(long candidateCycle, bool includeCopper)
        {
            return Math.Min(
                GetNextLiveRegisterEventCycle(includeCopper),
                GetNextPreparedLiveBitplaneFetchCycle()) <= candidateCycle;
        }

        private long GetNextLiveRegisterEventCycle(bool includeCopper)
        {
            var next = Math.Min(GetNextLiveDisplayEventCycle(includeCopper), GetNextLiveLineStateCycle());
            if (includeCopper)
            {
                return next;
            }

            return next < GetNextLiveCopperBarrierCycle() ? next : long.MaxValue;
        }

        private long GetNextLiveWorkCycle()
        {
            if (_liveNextWorkCycleValid)
            {
                return _liveNextWorkCycle;
            }

            _liveNextWorkCycle = Math.Min(
                Math.Min(GetNextLiveDisplayEventCycle(), GetNextLiveLineStateCycle()),
                Math.Min(GetNextLiveBitplaneFetchCycle(), GetNextLiveSpriteFetchCycle()));
            _liveNextWorkCycleValid = true;
            return _liveNextWorkCycle;
        }

        private long GetNextLiveWorkCycle(bool includeCopper)
        {
            if (includeCopper)
            {
                return GetNextLiveWorkCycle();
            }

            var nextWork = Math.Min(
                Math.Min(GetNextLivePendingWriteCycle(), GetNextLiveLineStateCycle()),
                Math.Min(GetNextLiveBitplaneFetchCycle(), GetNextLiveSpriteFetchCycle()));
            return nextWork < GetNextLiveCopperBarrierCycle()
                ? nextWork
                : long.MaxValue;
        }

        private void InvalidateLiveWorkCycle()
        {
            _liveNextWorkCycleValid = false;
            _liveNextWorkCycle = long.MaxValue;
            _liveDisplayWakeCandidateCacheValid = false;
        }

        private void CaptureLiveBitplaneDmaBeforeHrmGrant(long requestedCycle)
        {
            var requestedSlot = _bus.NextChipSlotCycle(requestedCycle);
            var candidate = _bus.FindHrmDmaCandidate(requestedCycle);
            if (candidate <= _liveCapturedThroughCycle)
            {
                return;
            }

            var nextFetchCycle = GetNextKnownLiveBitplaneFetchCycle();
            if (nextFetchCycle == long.MaxValue)
            {
                return;
            }

            if (requestedSlot < nextFetchCycle && !_bus.IsHrmChipSlotReserved(requestedSlot))
            {
                return;
            }

            var frameStopCycle = GetLiveFrameStopCycle();
            while (candidate < frameStopCycle)
            {
                if (candidate < nextFetchCycle)
                {
                    return;
                }

                if (!CaptureKnownLiveBitplaneFetchesThrough(candidate))
                {
                    return;
                }

                nextFetchCycle = GetNextKnownLiveBitplaneFetchCycle();
                var adjustedCandidate = _bus.IsHrmChipSlotReserved(candidate)
                    ? _bus.FindHrmDmaCandidate(candidate + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (adjustedCandidate == candidate)
                {
                    return;
                }

                candidate = adjustedCandidate;
            }
        }

        private void CaptureLiveSpriteDmaBeforeHrmGrant(long requestedCycle)
        {
            if (!IsSpriteDmaEnabled())
            {
                return;
            }

            var frameStopCycle = GetLiveFrameStopCycle();
            while (true)
            {
                var candidate = _bus.FindHrmDmaCandidate(requestedCycle);
                if (candidate >= frameStopCycle ||
                    !TryGetLiveSpriteDmaSlot(candidate, out var row, out var spriteIndex, out var word))
                {
                    return;
                }

                if (!TryCaptureKnownLiveSpriteDmaSlot(row, spriteIndex, word, candidate))
                {
                    return;
                }

                var adjustedCandidate = _bus.IsHrmChipSlotReserved(candidate)
                    ? _bus.FindHrmDmaCandidate(candidate + AgnusChipSlotScheduler.SlotCycles)
                    : _bus.FindHrmDmaCandidate(requestedCycle);
                if (adjustedCandidate == candidate)
                {
                    return;
                }
            }
        }

    }
}
