/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal readonly record struct AgnusLiveFixedDisplayDiagnostics(
        long PublishedRequests,
        long GrantedRequests,
        long DeniedRequests,
        long ChipWordTransfers,
        long RequesterCommits,
        long ContractViolations)
    {
        public long OutstandingRequests =>
            PublishedRequests - GrantedRequests - DeniedRequests;
    }

    /// <summary>
    /// G1L fixed-display commit boundary. It observes one stable requester
    /// intent, asks the bus to commit one exact fixed slot, transfers one word,
    /// and returns that grant to the same requester. It performs no scheduler
    /// drain, event discovery, or requester-range advancement.
    /// </summary>
    internal sealed class AgnusLiveFixedDisplaySlotKernel
    {
        private readonly Bus _bus;
        private long _publishedRequests;
        private long _grantedRequests;
        private long _deniedRequests;
        private long _chipWordTransfers;
        private long _requesterCommits;
        private long _contractViolations;

        public AgnusLiveFixedDisplaySlotKernel(Bus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public AgnusLiveFixedDisplayDiagnostics CaptureDiagnostics()
            => new(
                _publishedRequests,
                _grantedRequests,
                _deniedRequests,
                _chipWordTransfers,
                _requesterCommits,
                _contractViolations);

        public void Reset()
        {
            _publishedRequests = 0;
            _grantedRequests = 0;
            _deniedRequests = 0;
            _chipWordTransfers = 0;
            _requesterCommits = 0;
            _contractViolations = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCommitPendingRead(
            IAgnusLiveSlotRequester requester,
            out long observedSlotCycle)
        {
            if (requester == null)
            {
                throw new ArgumentNullException(nameof(requester));
            }

            if (!requester.TryPeekPendingRequest(out var request))
            {
                return RejectContract(
                    "A fixed-display requester must publish one intent before arbitration.",
                    out observedSlotCycle);
            }

            _publishedRequests++;
            if (request.Owner != requester.Owner ||
                request.Owner is not (AgnusChipSlotOwner.Bitplane or AgnusChipSlotOwner.Sprite) ||
                request.Transfer != AgnusLiveWordTransfer.Read ||
                request.EarliestEligibleCycle != request.RequestedCycle ||
                (uint)request.Channel >= 8 ||
                request.Kind != GetExpectedKind(request.Owner))
            {
                return RejectContract(
                    "The fixed-display request does not match its requester owner or transfer kind.",
                    out observedSlotCycle);
            }

            if (requester.TryPeekNextTransition(out _))
            {
                return RejectContract(
                    "Fixed display sampling cannot publish a device transition beside its word request.",
                    out observedSlotCycle);
            }

            if (!_bus.TryCommitLiveFixedDisplayRead(request, out var execution))
            {
                _deniedRequests++;
                observedSlotCycle = execution.GrantedCycle;
                return false;
            }

            _chipWordTransfers++;
            var grant = new AgnusLiveSlotGrant(
                request,
                execution.GrantedCycle,
                execution.CompletedCycle,
                execution.Value);
            requester.CommitGrantedSlot(grant);
            _requesterCommits++;
            _grantedRequests++;
            observedSlotCycle = execution.GrantedCycle;
            return true;
        }

        private bool RejectContract(string message, out long observedSlotCycle)
        {
            _contractViolations++;
            observedSlotCycle = -1;
            throw new InvalidOperationException(message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AmigaBusAccessKind GetExpectedKind(AgnusChipSlotOwner owner)
            => owner == AgnusChipSlotOwner.Bitplane
                ? AmigaBusAccessKind.Bitplane
                : AmigaBusAccessKind.Sprite;
    }
}
