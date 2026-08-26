/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal readonly record struct AgnusLiveBlitterDiagnostics(
        long PublishedRequests,
        long GrantedRequests,
        long DeniedRequests,
        long GrantedReads,
        long GrantedWrites,
        long AreaMicroOps,
        long LineMicroOps,
        long PublishedTransitions,
        long CommittedTransitions,
        long LastDeniedCycle,
        AgnusChipSlotOwner LastDeniedOwner,
        long AreaWords,
        long LinePixels,
        long NastyGrants,
        long Completions,
        long Interrupts,
        long ContractViolations,
        long SlotQueueReplayCalls,
        long DeviceSynchronizationCalls,
        long SchedulerDrainCalls,
        long RangeAdvanceCalls,
        long PredictionCalls,
        long EventDiscoveryCalls,
        long CausalExecutorCalls)
    {
        public long ForbiddenCalls =>
            SlotQueueReplayCalls +
            DeviceSynchronizationCalls +
            SchedulerDrainCalls +
            RangeAdvanceCalls +
            PredictionCalls +
            EventDiscoveryCalls +
            CausalExecutorCalls;
    }

    /// <summary>
    /// G3L blitter commit boundary. It attempts only the currently published
    /// physical slot and performs exactly one Chip-RAM word transfer on a
    /// grant. It neither discovers nor advances another device.
    /// </summary>
    internal sealed class AgnusLiveBlitterSlotKernel
    {
        private readonly Bus _bus;
        private long _publishedRequests;
        private long _grantedRequests;
        private long _deniedRequests;
        private long _grantedReads;
        private long _grantedWrites;
        private long _areaMicroOps;
        private long _lineMicroOps;
        private long _publishedTransitions;
        private long _committedTransitions;
        private long _lastDeniedCycle = -1;
        private AgnusChipSlotOwner _lastDeniedOwner;
        private long _nastyGrants;
        private long _contractViolations;

        public AgnusLiveBlitterSlotKernel(Bus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public AgnusLiveBlitterDiagnostics CaptureDiagnostics(
            in AgnusLiveBlitterDeviceDiagnostics device)
            => new(
                _publishedRequests,
                _grantedRequests,
                _deniedRequests,
                _grantedReads,
                _grantedWrites,
                _areaMicroOps,
                _lineMicroOps,
                _publishedTransitions,
                _committedTransitions,
                _lastDeniedCycle,
                _lastDeniedOwner,
                device.AreaWords,
                device.LinePixels,
                _nastyGrants,
                device.Completions,
                device.Interrupts,
                _contractViolations,
                device.SlotQueueReplayCalls,
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
            _grantedReads = 0;
            _grantedWrites = 0;
            _areaMicroOps = 0;
            _lineMicroOps = 0;
            _publishedTransitions = 0;
            _committedTransitions = 0;
            _lastDeniedCycle = -1;
            _lastDeniedOwner = AgnusChipSlotOwner.Free;
            _nastyGrants = 0;
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
                    "The live blitter requester has no published word intent.",
                    out observedSlotCycle);
            }

            _publishedRequests++;
            if (request.Owner != AgnusChipSlotOwner.Blitter ||
                request.Owner != requester.Owner ||
                request.Kind != AmigaBusAccessKind.Blitter ||
                request.Channel is < 0 or > 7)
            {
                return Reject(
                    "The live blitter intent has invalid owner, kind, or micro-operation identity.",
                    out observedSlotCycle);
            }

            if (requester.TryPeekNextTransition(out _))
            {
                return Reject(
                    "The blitter cannot publish a word and a local transition simultaneously.",
                    out observedSlotCycle);
            }

            if (!_bus.TryCommitLiveBlitterWord(request, out var execution))
            {
                _deniedRequests++;
                _lastDeniedCycle = execution.Access.GrantedCycle;
                _lastDeniedOwner = _bus.LastLiveBlitterDeniedOwner;
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
                _grantedWrites++;
            }
            else
            {
                _grantedReads++;
            }

            if (request.Channel <= 3)
            {
                _areaMicroOps++;
            }
            else
            {
                _lineMicroOps++;
            }

            if (_bus.BlitterNastyPriorityEnabled)
            {
                _nastyGrants++;
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
                    "The blitter cannot commit a local transition beside a pending word.");
            }

            if (!requester.TryPeekNextTransition(out var transition))
            {
                throw new InvalidOperationException(
                    "The live blitter requester has no published transition.");
            }

            _publishedTransitions++;
            if (transition.Owner != AgnusChipSlotOwner.Blitter ||
                transition.Owner != requester.Owner)
            {
                _contractViolations++;
                throw new InvalidOperationException(
                    "The live blitter transition has an invalid owner.");
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

            if (requester.Owner != AgnusChipSlotOwner.Blitter)
            {
                _contractViolations++;
                throw new InvalidOperationException(
                    "Only the blitter requester may use the G3L kernel.");
            }
        }

        private bool Reject(string message, out long observedSlotCycle)
        {
            _contractViolations++;
            observedSlotCycle = -1;
            throw new InvalidOperationException(message);
        }
    }

    internal readonly record struct AgnusLiveBlitterDeviceDiagnostics(
        long AreaWords,
        long LinePixels,
        long Completions,
        long Interrupts,
        long SlotQueueReplayCalls,
        long DeviceSynchronizationCalls,
        long SchedulerDrainCalls,
        long RangeAdvanceCalls,
        long PredictionCalls,
        long EventDiscoveryCalls,
        long CausalExecutorCalls);
}
