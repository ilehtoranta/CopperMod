/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal readonly record struct AgnusLiveDiskDiagnostics(
        long PublishedRequests,
        long GrantedRequests,
        long DeniedRequests,
        long ReadWords,
        long WriteWords,
        long SyncMatches,
        long CompletedBlocks,
        long CancelledBlocks,
        long Interrupts,
        long ContractViolations,
        long LegacySearchCalls,
        long DeviceSynchronizationCalls,
        long SchedulerDrainCalls,
        long RangeAdvanceCalls,
        long PredictionCalls,
        long EventDiscoveryCalls,
        long CausalExecutorCalls)
    {
        public long ForbiddenCalls =>
            LegacySearchCalls +
            DeviceSynchronizationCalls +
            SchedulerDrainCalls +
            RangeAdvanceCalls +
            PredictionCalls +
            EventDiscoveryCalls +
            CausalExecutorCalls;
    }

    /// <summary>
    /// G5L disk commit boundary. The disk controller publishes one stable
    /// read- or write-word intent for an exact physical disk slot. This kernel
    /// performs only that transfer and never searches for a later grant.
    /// </summary>
    internal sealed class AgnusLiveDiskSlotKernel
    {
        private readonly Bus _bus;
        private long _publishedRequests;
        private long _grantedRequests;
        private long _deniedRequests;
        private long _readWords;
        private long _writeWords;
        private long _contractViolations;

        public AgnusLiveDiskSlotKernel(Bus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public AgnusLiveDiskDiagnostics CaptureDiagnostics(
            in AgnusLiveDiskDeviceDiagnostics device)
            => new(
                _publishedRequests,
                _grantedRequests,
                _deniedRequests,
                _readWords,
                _writeWords,
                device.SyncMatches,
                device.CompletedBlocks,
                device.CancelledBlocks,
                device.Interrupts,
                _contractViolations,
                device.LegacySearchCalls,
                device.DeviceSynchronizationCalls,
                device.SchedulerDrainCalls,
                device.RangeAdvanceCalls,
                device.PredictionCalls,
                device.EventDiscoveryCalls,
                device.CausalExecutorCalls);

        public void Reset()
        {
            _publishedRequests = 0;
            _grantedRequests = 0;
            _deniedRequests = 0;
            _readWords = 0;
            _writeWords = 0;
            _contractViolations = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCommitPendingWord(
            IAgnusLiveSlotRequester requester,
            out long observedSlotCycle)
        {
            ValidateRequester(requester);
            if (!requester.TryPeekPendingRequest(out var request))
            {
                return Reject(
                    "The live disk requester has no published word intent.",
                    out observedSlotCycle);
            }

            _publishedRequests++;
            if (request.Owner != AgnusChipSlotOwner.Disk ||
                request.Owner != requester.Owner ||
                request.Kind != AmigaBusAccessKind.DiskDma ||
                request.Channel != -1)
            {
                return Reject(
                    "The live disk intent has an invalid owner, kind, or channel.",
                    out observedSlotCycle);
            }

            if (requester.TryPeekNextTransition(out _))
            {
                return Reject(
                    "Disk cannot publish a word and local transition simultaneously.",
                    out observedSlotCycle);
            }

            if (!_bus.TryCommitLiveDiskWord(request, out var execution))
            {
                _deniedRequests++;
                observedSlotCycle = execution.Access.GrantedCycle;
                return false;
            }

            var grant = new AgnusLiveSlotGrant(
                request,
                execution.Access.GrantedCycle,
                execution.Access.CompletedCycle,
                execution.Value);
            requester.CommitGrantedSlot(grant);
            _grantedRequests++;
            if (request.Transfer == AgnusLiveWordTransfer.Write)
            {
                _readWords++;
            }
            else
            {
                _writeWords++;
            }

            observedSlotCycle = execution.Access.GrantedCycle;
            return true;
        }

        private void ValidateRequester(IAgnusLiveSlotRequester requester)
        {
            if (requester == null)
            {
                throw new ArgumentNullException(nameof(requester));
            }

            if (requester.Owner != AgnusChipSlotOwner.Disk)
            {
                _contractViolations++;
                throw new InvalidOperationException(
                    "Only the disk requester may use the G5L kernel.");
            }
        }

        private bool Reject(string message, out long observedSlotCycle)
        {
            _contractViolations++;
            observedSlotCycle = -1;
            throw new InvalidOperationException(message);
        }
    }

    internal readonly record struct AgnusLiveDiskDeviceDiagnostics(
        long SyncMatches,
        long CompletedBlocks,
        long CancelledBlocks,
        long Interrupts,
        long LegacySearchCalls,
        long DeviceSynchronizationCalls,
        long SchedulerDrainCalls,
        long RangeAdvanceCalls,
        long PredictionCalls,
        long EventDiscoveryCalls,
        long CausalExecutorCalls);
}
