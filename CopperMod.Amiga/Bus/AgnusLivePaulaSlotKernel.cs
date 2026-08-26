/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal readonly record struct AgnusLivePaulaDiagnostics(
        long PublishedRequests,
        long GrantedRequests,
        long DeniedRequests,
        long Channel0Grants,
        long Channel1Grants,
        long Channel2Grants,
        long Channel3Grants,
        long HighSampleTransitions,
        long LowSampleTransitions,
        long LengthReloads,
        long DmaEnableTransitions,
        long DmaDisableTransitions,
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
    /// G4L Paula commit boundary. The requester has already selected one
    /// channel-local word intent. This kernel attempts only its exact physical
    /// audio slot and samples exactly one Chip-RAM word on a grant.
    /// </summary>
    internal sealed class AgnusLivePaulaSlotKernel
    {
        private readonly Bus _bus;
        private readonly long[] _channelGrants =
            new long[AmigaConstants.PaulaChannelCount];
        private long _publishedRequests;
        private long _grantedRequests;
        private long _deniedRequests;
        private long _contractViolations;

        public AgnusLivePaulaSlotKernel(Bus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public AgnusLivePaulaDiagnostics CaptureDiagnostics(
            in AgnusLivePaulaDeviceDiagnostics device)
            => new(
                _publishedRequests,
                _grantedRequests,
                _deniedRequests,
                _channelGrants[0],
                _channelGrants[1],
                _channelGrants[2],
                _channelGrants[3],
                device.HighSampleTransitions,
                device.LowSampleTransitions,
                device.LengthReloads,
                device.DmaEnableTransitions,
                device.DmaDisableTransitions,
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
            Array.Clear(_channelGrants);
            _publishedRequests = 0;
            _grantedRequests = 0;
            _deniedRequests = 0;
            _contractViolations = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryCommitPendingWord(
            IAgnusLiveSlotRequester requester,
            out long observedSlotCycle)
            => TryCommitPendingWordAtSlot(requester, null, out observedSlotCycle);

        public bool TryCommitPendingWordAtSlot(
            IAgnusLiveSlotRequester requester,
            long slotCycle,
            out long observedSlotCycle)
            => TryCommitPendingWordAtSlot(requester, (long?)slotCycle, out observedSlotCycle);

        private bool TryCommitPendingWordAtSlot(
            IAgnusLiveSlotRequester requester,
            long? slotCycle,
            out long observedSlotCycle)
        {
            ValidateRequester(requester);
            if (!requester.TryPeekPendingRequest(out var request))
            {
                return Reject(
                    "The live Paula requester has no published word intent.",
                    out observedSlotCycle);
            }

            _publishedRequests++;
            if (request.Owner != AgnusChipSlotOwner.Paula ||
                request.Owner != requester.Owner ||
                request.Kind != AmigaBusAccessKind.PaulaDma ||
                request.Transfer != AgnusLiveWordTransfer.Read ||
                request.Channel is < 0 or >= AmigaConstants.PaulaChannelCount)
            {
                return Reject(
                    "The live Paula intent has an invalid owner, kind, transfer, or channel.",
                    out observedSlotCycle);
            }

            if (requester.TryPeekNextTransition(out _))
            {
                return Reject(
                    "Paula cannot publish a word and a local transition simultaneously.",
                    out observedSlotCycle);
            }

            if (!_bus.TryCommitLivePaulaWord(request, out var execution, slotCycle))
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
            _channelGrants[request.Channel]++;
            observedSlotCycle = execution.Access.GrantedCycle;
            return true;
        }

        private void ValidateRequester(IAgnusLiveSlotRequester requester)
        {
            if (requester == null)
            {
                throw new ArgumentNullException(nameof(requester));
            }

            if (requester.Owner != AgnusChipSlotOwner.Paula)
            {
                _contractViolations++;
                throw new InvalidOperationException(
                    "Only a Paula audio requester may use the G4L kernel.");
            }
        }

        private bool Reject(string message, out long observedSlotCycle)
        {
            _contractViolations++;
            observedSlotCycle = -1;
            throw new InvalidOperationException(message);
        }
    }

    internal readonly record struct AgnusLivePaulaDeviceDiagnostics(
        long HighSampleTransitions,
        long LowSampleTransitions,
        long LengthReloads,
        long DmaEnableTransitions,
        long DmaDisableTransitions,
        long Interrupts,
        long LegacySearchCalls,
        long DeviceSynchronizationCalls,
        long SchedulerDrainCalls,
        long RangeAdvanceCalls,
        long PredictionCalls,
        long EventDiscoveryCalls,
        long CausalExecutorCalls);
}
