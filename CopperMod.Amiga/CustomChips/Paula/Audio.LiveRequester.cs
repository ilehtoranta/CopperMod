/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using CopperMod.Amiga.Bus;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.CustomChips.Paula
{
    internal sealed partial class Paula
    {
        private readonly LivePaulaRequester[] _livePaulaRequesters;
        private long _liveHighSampleTransitions;
        private long _liveLowSampleTransitions;
        private long _liveLengthReloads;
        private long _liveDmaEnableTransitions;
        private long _liveDmaDisableTransitions;
        private long _liveInterrupts;

        internal AgnusLivePaulaDeviceDiagnostics CaptureLivePaulaDeviceDiagnostics()
            => new(
                _liveHighSampleTransitions,
                _liveLowSampleTransitions,
                _liveLengthReloads,
                _liveDmaEnableTransitions,
                _liveDmaDisableTransitions,
                _liveInterrupts,
                LegacySearchCalls: 0,
                DeviceSynchronizationCalls: 0,
                SchedulerDrainCalls: 0,
                RangeAdvanceCalls: 0,
                PredictionCalls: 0,
                EventDiscoveryCalls: 0,
                CausalExecutorCalls: 0);

        private LivePaulaRequester[] CreateLivePaulaRequesters()
        {
            var requesters =
                new LivePaulaRequester[AmigaConstants.PaulaChannelCount];
            for (var channel = 0; channel < requesters.Length; channel++)
            {
                requesters[channel] = new LivePaulaRequester(this, channel);
            }

            return requesters;
        }

        private void ResetLivePaulaRequester()
        {
            foreach (var requester in _livePaulaRequesters)
            {
                requester.Reset();
            }

            _liveHighSampleTransitions = 0;
            _liveLowSampleTransitions = 0;
            _liveLengthReloads = 0;
            _liveDmaEnableTransitions = 0;
            _liveDmaDisableTransitions = 0;
            _liveInterrupts = 0;
        }

        private bool StepLivePaulaRequester(
            long targetCycle,
            bool commitAtTargetSlot = false)
        {
            targetCycle = Math.Max(0, targetCycle);
            var nextCycle = GetDmaWakeCandidateCycle();
            if (nextCycle > targetCycle)
            {
                return true;
            }

            // The scheduler admits only the earliest Paula-local event. This
            // advances the existing semantic state machine to one boundary;
            // it does not search for a grant or advance another device.
            AdvanceTimelineTo(
                _registerTimeline,
                nextCycle,
                PaulaTimelineKind.Register);

            for (var channel = 0; channel < _livePaulaRequesters.Length; channel++)
            {
                PublishLivePaulaWord(channel);
                var requester = _livePaulaRequesters[channel];
                if (!requester.TryPeekPendingRequest(out var request) ||
                    request.EarliestEligibleCycle > nextCycle)
                {
                    continue;
                }

                var granted = commitAtTargetSlot
                    ? _bus.TryCommitLivePaulaRequestAtSlot(
                        requester,
                        targetCycle,
                        out var observedSlotCycle)
                    : _bus.TryCommitLivePaulaRequest(
                        requester,
                        out observedSlotCycle);
                if (!granted)
                {
                    DeferLivePaulaWord(channel, observedSlotCycle);
                }
                InvalidateRegisterWakeCandidateCache();
                return granted;
            }

            return true;
        }

        internal void AdvanceLiveAgnusSlotKernelTo(long slotCycle)
        {
            if (!_bus.AgnusLivePaulaEnabled)
            {
                return;
            }

            for (var step = 0; step < 16; step++)
            {
                var nextCycle = GetDmaWakeCandidateCycle();
                if (nextCycle > slotCycle)
                {
                    return;
                }

                var version = _registerWakeVersion;
                // The G6 CPU boundary may enter after a fixed Paula slot with
                // no intervening CPU access. Commit the requester at its own
                // canonical deadline; forcing it into slotCycle skips that
                // physical audio slot and perturbs the later sample phase.
                if (!StepLivePaulaRequester(nextCycle, commitAtTargetSlot: true))
                {
                    return;
                }
                if (_registerWakeVersion == version &&
                    GetDmaWakeCandidateCycle() == nextCycle)
                {
                    throw new InvalidOperationException(
                        $"The G6L Paula requester made no progress at cycle {nextCycle}.");
                }
            }

            throw new InvalidOperationException(
                $"The G6L Paula requester did not settle slot {slotCycle}: " +
                $"next={GetDmaWakeCandidateCycle()}, bus={_bus.ExecutedChipBusHorizon}.");
        }

        internal bool TryCommitNextLiveAgnusSlotKernelTransition(
            long slotCycle,
            out bool stopAfterTransition)
        {
            stopAfterTransition = false;
            if (!_bus.AgnusLivePaulaEnabled)
            {
                return false;
            }

            var nextCycle = GetDmaWakeCandidateCycle();
            if (nextCycle > slotCycle)
            {
                return false;
            }

            var version = _registerWakeVersion;
            var granted = StepLivePaulaRequester(
                nextCycle,
                commitAtTargetSlot: true);
            if (_registerWakeVersion == version &&
                GetDmaWakeCandidateCycle() == nextCycle)
            {
                throw new InvalidOperationException(
                    $"The G6L Paula requester made no progress at cycle {nextCycle}.");
            }

            // A denied fixed-slot request is deferred by the transition. Match
            // the Stage-5 boundary and let the next physical slot re-enter the
            // requester instead of consuming another transition immediately.
            stopAfterTransition = !granted;
            return true;
        }

        internal long GetNextLiveAgnusSlotKernelCycle()
            => _bus.AgnusLivePaulaEnabled
                ? GetDmaWakeCandidateCycle()
                : long.MaxValue;

        private void AdvanceLivePaulaRequesterThrough(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            var repeatedCycle = long.MinValue;
            var repeatedCycleCount = 0;
            while (true)
            {
                var beforeCycle = GetDmaWakeCandidateCycle();
                if (beforeCycle > targetCycle)
                {
                    return;
                }

                var beforeVersion = _registerWakeVersion;
                StepLivePaulaRequester(beforeCycle);
                var afterCycle = GetDmaWakeCandidateCycle();
                if (afterCycle == beforeCycle)
                {
                    if (repeatedCycle == afterCycle)
                    {
                        repeatedCycleCount++;
                    }
                    else
                    {
                        repeatedCycle = afterCycle;
                        repeatedCycleCount = 1;
                    }

                    if (repeatedCycleCount > 32)
                    {
                        throw new InvalidOperationException(
                            $"The G4L Paula requester repeated cycle {afterCycle} without advancing.");
                    }
                }
                else
                {
                    repeatedCycle = long.MinValue;
                    repeatedCycleCount = 0;
                }

                if (afterCycle == beforeCycle &&
                    _registerWakeVersion == beforeVersion)
                {
                    var pending = "";
                    for (var channel = 0; channel < _livePaulaRequesters.Length; channel++)
                    {
                        if (_livePaulaRequesters[channel].TryPeekPendingRequest(out var request))
                        {
                            pending += $" ch{channel}={request.RequestedCycle}/{request.EarliestEligibleCycle}";
                        }

                        pending += $" state{channel}={_registerTimeline.Channels[channel].DescribeLiveWake(_registerTimeline, this)}";
                    }

                    throw new InvalidOperationException(
                        $"The G4L Paula requester made no progress at cycle {beforeCycle}; last={_registerTimeline.LastCycle}; pending:{pending}.");
                }
            }
        }

        private void PublishLivePaulaWord(int channel)
        {
            if (!_bus.AgnusLivePaulaEnabled ||
                (uint)channel >= AmigaConstants.PaulaChannelCount)
            {
                return;
            }

            var state = _registerTimeline.Channels[channel];
            var requester = _livePaulaRequesters[channel];
            if (!state.TryGetPendingLiveDmaWord(
                    out var address,
                    out var requestedCycle,
                    out var eligibleCycle))
            {
                return;
            }

            if (requester.TryPeekPendingRequest(out var current))
            {
                if (current.Address == address &&
                    current.RequestedCycle == requestedCycle &&
                    current.EarliestEligibleCycle == eligibleCycle)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "A live Paula channel changed intent before its pending generation committed.");
            }

            requester.Publish(
                address,
                requestedCycle,
                eligibleCycle);
            _bus.NotifyHardwareWorkScheduled(eligibleCycle);
        }

        private void CommitLivePaulaWord(
            int channel,
            in AgnusLiveSlotGrant grant)
        {
            var state = _registerTimeline.Channels[channel];
            if (!state.MatchesPendingLiveDmaWord(
                    grant.Request.Address,
                    grant.Request.RequestedCycle))
            {
                state.TryGetPendingLiveDmaWord(
                    out var pendingAddress,
                    out var pendingRequestedCycle,
                    out var pendingEligibleCycle);
                throw new InvalidOperationException(
                    $"A live Paula grant does not match channel {channel}: " +
                    $"grant=0x{grant.Request.Address:X8}/" +
                    $"{grant.Request.RequestedCycle}/{grant.Request.EarliestEligibleCycle}, " +
                    $"pending=0x{pendingAddress:X8}/{pendingRequestedCycle}/" +
                    $"{pendingEligibleCycle}, " +
                    $"wake={state.DescribeLiveWake(_registerTimeline, this)}.");
            }

            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                AmigaBusAccessTarget.ChipRam,
                grant.Request.Address,
                AmigaBusAccessSize.Word,
                grant.Request.RequestedCycle,
                isWrite: false,
                channel);
            var access = new AmigaBusAccessResult(
                request,
                grant.SlotCycle,
                grant.CompletedCycle);
            var latch = new PaulaDmaReadLatch(
                channel,
                grant.Request.Address,
                grant.Request.RequestedCycle,
                grant.SampledValue,
                access);
            var queue = _dmaReadLatchQueues[channel];
            // Audio can already be one word ahead of the register timeline.
            // Retire its oldest audio-only record before publishing this live
            // word, or that prefix can never compact and the queue grows forever.
            _ = queue.TryConsume(
                grant.Request.Address,
                grant.Request.RequestedCycle,
                PaulaTimelineKind.Register,
                out _);
            queue.AddConsumed(
                latch,
                PaulaTimelineKind.Register);
            RememberRegisterDmaReadLatch(
                channel,
                PaulaTimelineKind.Register,
                latch);
            state.CommitPendingLiveDmaWord(latch);
            state.CommitLivePostGrantTransitions(
                grant.SlotCycle,
                this,
                _registerTimeline);
            _paulaDmaWordExecutionCount++;
        }

        private void DeferLivePaulaWord(int channel, long deniedSlotCycle)
        {
            var nextCycle = GetNextAudioDmaSlotCycle(
                channel,
                deniedSlotCycle + AgnusChipSlotScheduler.SlotCycles);
            _registerTimeline.Channels[channel].DeferPendingLiveDmaWord(nextCycle);
            _livePaulaRequesters[channel].Defer(nextCycle);
            _bus.NotifyHardwareWorkScheduled(nextCycle);
        }

        private void CancelLivePaulaWord(
            int channel,
            PaulaTimelineKind kind)
        {
            if (!_bus.AgnusLivePaulaEnabled ||
                kind != PaulaTimelineKind.Register ||
                (uint)channel >= AmigaConstants.PaulaChannelCount)
            {
                return;
            }

            _livePaulaRequesters[channel].CancelPendingWord();
            InvalidateRegisterWakeCandidateCache();
        }

        private void RecordLivePaulaDmaEnableTransition(
            PaulaTimelineKind kind,
            bool enabled)
        {
            if (!_bus.AgnusLivePaulaEnabled ||
                kind != PaulaTimelineKind.Register)
            {
                return;
            }

            if (enabled)
            {
                _liveDmaEnableTransitions++;
            }
            else
            {
                _liveDmaDisableTransitions++;
            }
        }

        private void RecordLivePaulaSampleTransition(
            PaulaTimelineKind kind,
            bool high)
        {
            if (!_bus.AgnusLivePaulaEnabled ||
                kind != PaulaTimelineKind.Register)
            {
                return;
            }

            if (high)
            {
                _liveHighSampleTransitions++;
            }
            else
            {
                _liveLowSampleTransitions++;
            }
        }

        private void RecordLivePaulaLengthReload(PaulaTimelineKind kind)
        {
            if (_bus.AgnusLivePaulaEnabled &&
                kind == PaulaTimelineKind.Register)
            {
                _liveLengthReloads++;
            }
        }

        private void RecordLivePaulaInterrupt(PaulaTimelineKind kind)
        {
            if (_bus.AgnusLivePaulaEnabled &&
                kind == PaulaTimelineKind.Register)
            {
                _liveInterrupts++;
            }
        }

        private sealed class LivePaulaRequester : IAgnusLiveSlotRequester
        {
            private readonly Paula _owner;
            private readonly int _channel;
            private AgnusLiveRequestLatch _wordLatch =
                new(AgnusChipSlotOwner.Paula);

            public LivePaulaRequester(Paula owner, int channel)
            {
                _owner = owner;
                _channel = channel;
            }

            public AgnusChipSlotOwner Owner => AgnusChipSlotOwner.Paula;

            public bool TryPeekPendingRequest(
                out AgnusLiveSlotRequest request)
                => _wordLatch.TryPeek(out request);

            public bool TryPeekNextTransition(
                out AgnusLiveTransition transition)
            {
                transition = default;
                return false;
            }

            public void CommitGrantedSlot(in AgnusLiveSlotGrant grant)
            {
                _wordLatch.Consume(grant);
                _owner.CommitLivePaulaWord(_channel, grant);
            }

            public void CommitTransition(in AgnusLiveTransition transition)
                => throw new InvalidOperationException(
                    "G4L Paula publishes local state through its channel state machine, not as a bus transfer.");

            public void Publish(
                uint address,
                long requestedCycle,
                long eligibleCycle)
                => _wordLatch.Publish(
                    AmigaBusAccessKind.PaulaDma,
                    address,
                    requestedCycle,
                    eligibleCycle,
                    AgnusLiveWordTransfer.Read,
                    channel: _channel);

            public void Defer(long earliestEligibleCycle)
            {
                if (!_wordLatch.TryPeek(out var request))
                {
                    throw new InvalidOperationException(
                        "The live Paula requester cannot defer an empty latch.");
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
                    new AgnusLiveRequestLatch(AgnusChipSlotOwner.Paula);
        }

        private sealed partial class PaulaChannel
        {
            public bool TryGetPendingLiveDmaWord(
                out uint address,
                out long requestedCycle,
                out long eligibleCycle)
            {
                address = _pendingDmaRequestAddress;
                requestedCycle = _pendingDmaRequestCycle;
                eligibleCycle = _pendingDmaServiceCycle;
                return DmaEnabled &&
                    _hasPendingDmaWord &&
                    !_pendingDmaServed;
            }

            public bool MatchesPendingLiveDmaWord(
                uint address,
                long requestedCycle)
                => _hasPendingDmaWord &&
                    !_pendingDmaServed &&
                    _pendingDmaRequestAddress == address &&
                    _pendingDmaRequestCycle == requestedCycle;

            public void CommitPendingLiveDmaWord(PaulaDmaReadLatch latch)
                => MarkPendingDmaServed(latch);

            public void DeferPendingLiveDmaWord(long earliestEligibleCycle)
            {
                if (!_hasPendingDmaWord ||
                    _pendingDmaServed ||
                    earliestEligibleCycle <= _pendingDmaServiceCycle)
                {
                    throw new InvalidOperationException(
                        "A denied live Paula word must move to a later physical channel slot.");
                }

                _pendingDmaServiceCycle = earliestEligibleCycle;
            }

            public void CommitLivePostGrantTransitions(
                long slotCycle,
                Paula paula,
                PaulaTimelineState timeline)
            {
                // AUDxDAT is strobed on the granted audio slot, before the
                // period transition which is waiting on that word. A reload can
                // therefore arm intreq2 in time for the transition even though
                // the fetched data remains unavailable until CompletedCycle.
                // Replay only the interrupt side effect of that blocked
                // transition; unrelated reloads retain intreq2 until their next
                // eligible state transition.
                var consumeAtGrant =
                    _pendingDmaArmsDelayedInterrupt &&
                    _hasDataWord &&
                    _nextSampleCycle == _pendingDmaLoadCycle &&
                    ((_nextByteIsLow &&
                        paula.IsPeriodAttached(timeline, Index)) ||
                     (!_nextByteIsLow &&
                        paula.UsesNormalOrVolumeDmaTransition(timeline, Index)));
                TryArmDelayedInterruptBeforeNextAction(slotCycle);
                if (consumeAtGrant)
                {
                    TryConsumeDelayedInterrupt(
                        paula,
                        timeline,
                        PaulaTimelineKind.Register,
                        slotCycle);
                }
            }

            public string DescribeLiveWake(
                PaulaTimelineState timeline,
                Paula paula)
                => $"{GetNextDmaWakeCandidateCycle(timeline, paula)}" +
                    $"/arm={_intreq2ArmCycle}/consume={_intreq2ConsumeCycle}" +
                    $"/pending={_hasPendingDmaWord}/{_pendingDmaServed}" +
                    $"/service={_pendingDmaServiceCycle}/load={_pendingDmaLoadCycle}" +
                    $"/data={_hasDataWord}/prefetch={_hasPrefetchedDmaWord}" +
                    $"/sample={_nextSampleCycle}";
        }
    }
}
