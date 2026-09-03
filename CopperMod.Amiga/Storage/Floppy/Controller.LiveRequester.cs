/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Storage.Floppy
{
    internal sealed partial class AmigaDiskController
    {
        private readonly LiveDiskRequester _liveDiskRequester;
        private bool _liveDmaWaitingForSync;
        private bool _liveDmaSyncMatched;
        private int _liveDmaSyncSourceBit;
        private long _liveSyncMatches;
        private long _liveCompletedBlocks;
        private long _liveCancelledBlocks;
        private long _liveInterrupts;

        // A serial deadline alone does not own an RGA phase. The word must
        // already be ready and waiting for this exact fixed output slot.
        internal bool HasPendingDmaWordAt(long outputCycle)
            => _activeDma && _activeDmaRequestPending &&
                IsDiskDmaControlEnabled() &&
                GetDriveOrNull(_activeDmaDrive)?.Disk != null &&
                _activeDmaRequestServiceCycle == outputCycle;

        internal AgnusLiveDiskDeviceDiagnostics CaptureLiveDiskDeviceDiagnostics()
            => new(
                _liveSyncMatches,
                _liveCompletedBlocks,
                _liveCancelledBlocks,
                _liveInterrupts,
                LegacySearchCalls: 0,
                DeviceSynchronizationCalls: 0,
                SchedulerDrainCalls: 0,
                RangeAdvanceCalls: 0,
                PredictionCalls: 0,
                EventDiscoveryCalls: 0,
                CausalExecutorCalls: 0);

        private void ResetLiveDiskRequester()
        {
            _liveDiskRequester.Reset();
            _liveDmaWaitingForSync = false;
            _liveDmaSyncMatched = false;
            _liveDmaSyncSourceBit = 0;
            _liveSyncMatches = 0;
            _liveCompletedBlocks = 0;
            _liveCancelledBlocks = 0;
            _liveInterrupts = 0;
        }

        private void PublishLiveDiskWord()
        {
            if (!_bus.AgnusLiveDiskEnabled || !_activeDmaRequestPending)
            {
                return;
            }

            if (_liveDiskRequester.TryPeekPendingRequest(out var current))
            {
                if (current.Address == _activeDmaRequestTargetAddress &&
                    current.EarliestEligibleCycle == _activeDmaRequestServiceCycle &&
                    current.Transfer == GetLiveDiskTransfer())
                {
                    return;
                }

                throw new InvalidOperationException(
                    "The live disk intent changed before its pending generation committed.");
            }

            _liveDiskRequester.Publish(
                _activeDmaRequestTargetAddress,
                _activeDmaRequestServiceCycle,
                GetLiveDiskTransfer(),
                _activeDmaRequestReadValue);
            _bus.NotifyHardwareWorkScheduled(_activeDmaRequestServiceCycle);
        }

        internal void AdvanceLiveAgnusSlotKernelTo(long slotCycle)
        {
            if (_bus.AgnusLiveDiskEnabled)
            {
                // This boundary advances chronological disk DMA/control
                // events only. Passive serial input is CPU-visible latch state,
                // not a bus requester; the slot kernel synchronizes it at the
                // same explicit custom-register boundaries as Stage 5.
                AdvanceEventsTo(slotCycle);
            }
        }

        internal bool TryCommitNextLiveAgnusSlotKernelTransition(long slotCycle)
        {
            if (!_bus.AgnusLiveDiskEnabled)
            {
                return false;
            }

            var nextCycle = GetRawSlotDmaEligibilityCycle();
            if (nextCycle > slotCycle)
            {
                return false;
            }

            _currentCycle = Math.Max(_currentCycle, nextCycle);

            // A WORDSYNC/MSBSYNC match is chronological only while it gates an
            // active or pending DMA block. Materialize the already-selected
            // exact match boundary; passive rotation remains synchronized only
            // at explicit CPU-visible disk-register reads.
            if (RequiresChronologicalSyncDeadline())
            {
                var syncCycle = GetNextSelectedSyncCompletionCycleCached(nextCycle);
                if (syncCycle == nextCycle)
                {
                    AdvanceDiskInputTo(syncCycle);
                    _schedulerWakeVersion++;
                    return true;
                }
            }

            if ((_pendingReadDmaWords != 0 || _pendingWriteDmaWords != 0) &&
                IsDiskDmaControlEnabled())
            {
                TryStartPendingDma(nextCycle);
                _schedulerWakeVersion++;
                return true;
            }

            if (_activeDma && IsDiskDmaControlEnabled() &&
                GetNextActiveDmaAdvanceCycle() <= nextCycle)
            {
                AdvanceActiveDmaTo(nextCycle);
                _schedulerWakeVersion++;
                return true;
            }

            throw new InvalidOperationException(
                $"The G6L disk requester made no progress at cycle {nextCycle}.");
        }

        private AgnusLiveWordTransfer GetLiveDiskTransfer()
            => _activeDmaWriteMode
                ? AgnusLiveWordTransfer.Read
                : AgnusLiveWordTransfer.Write;

        private void CommitLiveDiskWord(in AgnusLiveSlotGrant grant)
        {
            if (!_activeDmaRequestPending ||
                grant.Request.Address != _activeDmaRequestTargetAddress ||
                grant.Request.EarliestEligibleCycle !=
                    _activeDmaRequestServiceCycle ||
                grant.Request.Transfer != GetLiveDiskTransfer())
            {
                throw new InvalidOperationException(
                    "A live disk grant does not match the controller's pending word.");
            }

            CommitServedActiveDmaRequest(
                grant.SampledValue,
                grant.SlotCycle,
                grant.CompletedCycle);
        }

        private void DeferLiveDiskWord(long deniedSlotCycle)
        {
            var nextCycle = _bus.GetLiveDiskSlotCycle(
                deniedSlotCycle + AgnusChipSlotScheduler.SlotCycles);
            _activeDmaRequestServiceCycle = nextCycle;
            _activeDmaCompletionCycle = Math.Max(
                _activeDmaCompletionCycle,
                nextCycle + AgnusChipSlotScheduler.SlotCycles);
            _liveDiskRequester.Defer(nextCycle);
            _bus.NotifyHardwareWorkScheduled(nextCycle);
        }

        private void CancelLiveDiskWord()
        {
            if (_bus.AgnusLiveDiskEnabled)
            {
                _liveDiskRequester.CancelPendingWord();
            }
        }

        private void RecordLiveDiskSyncMatch()
        {
            if (_bus.AgnusLiveDiskEnabled)
            {
                _liveSyncMatches++;
            }
        }

        private void RecordLiveDiskCompletion()
        {
            if (_bus.AgnusLiveDiskEnabled)
            {
                _liveCompletedBlocks++;
                _liveInterrupts++;
            }
        }

        private void RecordLiveDiskCancellation()
        {
            if (_bus.AgnusLiveDiskEnabled)
            {
                _liveCancelledBlocks++;
            }
        }

        private sealed class LiveDiskRequester : IAgnusLiveSlotRequester
        {
            private readonly AmigaDiskController _owner;
            private AgnusLiveRequestLatch _wordLatch =
                new(AgnusChipSlotOwner.Disk);

            public LiveDiskRequester(AmigaDiskController owner)
            {
                _owner = owner;
            }

            public AgnusChipSlotOwner Owner => AgnusChipSlotOwner.Disk;

            public bool TryPeekPendingRequest(out AgnusLiveSlotRequest request)
                => _wordLatch.TryPeek(out request);

            public bool TryPeekNextTransition(out AgnusLiveTransition transition)
            {
                transition = default;
                return false;
            }

            public void CommitGrantedSlot(in AgnusLiveSlotGrant grant)
            {
                _wordLatch.Consume(grant);
                _owner.CommitLiveDiskWord(grant);
            }

            public void CommitTransition(in AgnusLiveTransition transition)
                => throw new InvalidOperationException(
                    "G5L disk transitions are committed by the disk state machine.");

            public void Publish(
                uint address,
                long slotCycle,
                AgnusLiveWordTransfer transfer,
                ushort readValue)
                => _wordLatch.Publish(
                    AmigaBusAccessKind.DiskDma,
                    address,
                    slotCycle,
                    slotCycle,
                    transfer,
                    writeValue: readValue);

            public void Defer(long earliestEligibleCycle)
            {
                if (!_wordLatch.TryPeek(out var request))
                {
                    throw new InvalidOperationException(
                        "The live disk requester cannot defer an empty latch.");
                }

                _wordLatch.Defer(request.Generation, earliestEligibleCycle);
            }

            public void CancelPendingWord()
            {
                if (_wordLatch.TryPeek(out var request))
                {
                    _wordLatch.Cancel(request.Generation);
                }
            }

            public void Reset()
                => _wordLatch =
                    new AgnusLiveRequestLatch(AgnusChipSlotOwner.Disk);
        }
    }
}
