/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal readonly record struct AgnusLiveCopperDiagnostics(
        long PublishedRequests,
        long GrantedRequests,
        long DeniedRequests,
        long GrantedFirstWords,
        long GrantedSecondWords,
        long SampledWords,
        long PublishedTransitions,
        long CommittedTransitions,
        long WaitComparisons,
        long SkipComparisons,
        long BfdBusyObservations,
        long CommittedMoves,
        long CommittedCopjmps,
        long CommittedInterruptMoves,
        long PublishedNextRequests,
        long ContractViolations,
        long LegacyStepCalls,
        long PredictionCalls,
        long EventDiscoveryCalls,
        long BlitterSynchronizationCalls,
        long SchedulerDrainCalls,
        long RangeAdvanceCalls,
        long CausalDeferrals,
        long CausalDeferredCycles,
        long MaxCausalLagCycles,
        long FirstCausalDeferralCycle)
    {
        public long ForbiddenCalls =>
            LegacyStepCalls +
            PredictionCalls +
            EventDiscoveryCalls +
            BlitterSynchronizationCalls +
            SchedulerDrainCalls +
            RangeAdvanceCalls;
    }

    /// <summary>
    /// G2L Copper commit boundary. The requester exposes one current word intent
    /// or one exact device-local transition. The kernel attempts only the
    /// supplied physical slot; a denial leaves retry publication to the
    /// requester and never searches or advances another device.
    /// </summary>
    internal sealed class AgnusLiveCopperSlotKernel
    {
        private readonly Bus _bus;
        private long _publishedRequests;
        private long _grantedRequests;
        private long _deniedRequests;
        private long _grantedFirstWords;
        private long _grantedSecondWords;
        private long _sampledWords;
        private long _publishedTransitions;
        private long _committedTransitions;
        private long _contractViolations;

        public AgnusLiveCopperSlotKernel(Bus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public AgnusLiveCopperDiagnostics CaptureDiagnostics(
            in AgnusLiveCopperDeviceDiagnostics device)
            => new(
                _publishedRequests,
                _grantedRequests,
                _deniedRequests,
                _grantedFirstWords,
                _grantedSecondWords,
                _sampledWords,
                _publishedTransitions,
                _committedTransitions,
                device.WaitComparisons,
                device.SkipComparisons,
                device.BfdBusyObservations,
                device.CommittedMoves,
                device.CommittedCopjmps,
                device.CommittedInterruptMoves,
                device.PublishedNextRequests,
                _contractViolations,
                device.LegacyStepCalls,
                device.PredictionCalls,
                device.EventDiscoveryCalls,
                device.BlitterSynchronizationCalls,
                device.SchedulerDrainCalls,
                device.RangeAdvanceCalls,
                device.CausalDeferrals,
                device.CausalDeferredCycles,
                device.MaxCausalLagCycles,
                device.FirstCausalDeferralCycle);

        public void Reset()
        {
            _publishedRequests = 0;
            _grantedRequests = 0;
            _deniedRequests = 0;
            _grantedFirstWords = 0;
            _grantedSecondWords = 0;
            _sampledWords = 0;
            _publishedTransitions = 0;
            _committedTransitions = 0;
            _contractViolations = 0;
        }

        /// <summary>
        /// Accounts for an exact WAIT comparison whose already-published
        /// transition was validated and consumed by the concrete Copper
        /// requester. No comparison is skipped or projected.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordPrevalidatedTransitionCommit()
        {
            _publishedTransitions++;
            _committedTransitions++;
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
                    "The live Copper requester has no published word intent.",
                    out observedSlotCycle);
            }

            _publishedRequests++;
            if (request.Owner != AgnusChipSlotOwner.Copper ||
                request.Owner != requester.Owner ||
                request.Kind != AmigaBusAccessKind.Copper ||
                request.Transfer != AgnusLiveWordTransfer.Read ||
                request.Channel is < 0 or > 2)
            {
                return Reject(
                    "The live Copper word intent has invalid owner, kind, transfer, or phase.",
                    out observedSlotCycle);
            }

            if (requester.TryPeekNextTransition(out _))
            {
                return Reject(
                    "Copper cannot publish a word and a local transition simultaneously.",
                    out observedSlotCycle);
            }

            if (!_bus.TryCommitLiveCopperRead(request, out var execution))
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
            _sampledWords++;
            if (request.Channel == 0)
            {
                _grantedFirstWords++;
            }
            else
            {
                _grantedSecondWords++;
            }

            observedSlotCycle = execution.Access.GrantedCycle;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CommitPendingTransition(IAgnusLiveSlotRequester requester)
        {
            ValidateRequester(requester);
            if (requester.TryPeekPendingRequest(out _))
            {
                throw new InvalidOperationException(
                    "Copper cannot commit a local transition beside a pending word.");
            }

            if (!requester.TryPeekNextTransition(out var transition))
            {
                throw new InvalidOperationException(
                    "The live Copper requester has no published transition.");
            }

            _publishedTransitions++;
            if (transition.Owner != AgnusChipSlotOwner.Copper ||
                transition.Owner != requester.Owner)
            {
                _contractViolations++;
                throw new InvalidOperationException(
                    "The live Copper transition has an invalid owner.");
            }

            requester.CommitTransition(transition);
            _committedTransitions++;
        }

        private void ValidateRequester(IAgnusLiveSlotRequester requester)
        {
            if (requester == null)
            {
                throw new ArgumentNullException(nameof(requester));
            }

            if (requester.Owner != AgnusChipSlotOwner.Copper)
            {
                _contractViolations++;
                throw new InvalidOperationException(
                    "Only the Copper requester may use the G2L kernel.");
            }
        }

        private bool Reject(string message, out long observedSlotCycle)
        {
            _contractViolations++;
            observedSlotCycle = -1;
            throw new InvalidOperationException(message);
        }
    }

    internal readonly record struct AgnusLiveCopperDeviceDiagnostics(
        long WaitComparisons,
        long SkipComparisons,
        long BfdBusyObservations,
        long CommittedMoves,
        long CommittedCopjmps,
        long CommittedInterruptMoves,
        long PublishedNextRequests,
        long LegacyStepCalls,
        long PredictionCalls,
        long EventDiscoveryCalls,
        long BlitterSynchronizationCalls,
        long SchedulerDrainCalls,
        long RangeAdvanceCalls,
        long CausalDeferrals,
        long CausalDeferredCycles,
        long MaxCausalLagCycles,
        long FirstCausalDeferralCycle);
}
