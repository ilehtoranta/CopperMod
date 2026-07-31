/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    /// <summary>
    /// Direction of the single Chip-RAM word transfer represented by a live
    /// requester intent. Arbitration never asks a requester to execute a
    /// transfer while discovering ownership.
    /// </summary>
    internal enum AgnusLiveWordTransfer : byte
    {
        Read,
        Write
    }

    /// <summary>
    /// Orders a device-local transition relative to a physical Chip-bus slot
    /// at the same canonical scheduler cycle.
    /// </summary>
    internal enum AgnusLiveTransitionPhase : byte
    {
        BeforeSlotSelection,
        AfterSlotCommit
    }

    /// <summary>
    /// Canonical ordering within one scheduler cycle. A production kernel must
    /// use the same order instead of relying on call-site order.
    /// </summary>
    internal static class AgnusLiveTimelineOrdering
    {
        public const byte SlotSelection = 1;
        public const byte SlotCommit = 2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetTransitionOrder(AgnusLiveTransitionPhase phase)
            => phase switch
            {
                AgnusLiveTransitionPhase.BeforeSlotSelection => 0,
                AgnusLiveTransitionPhase.AfterSlotCommit => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null)
            };
    }

    /// <summary>
    /// Stable pre-arbitration intent for exactly one Chip-RAM word transfer.
    /// It contains no predicted grant, sampled value, decoded post-transfer
    /// action, next request, or fixed-plan patch.
    /// </summary>
    internal readonly struct AgnusLiveSlotRequest
    {
        public AgnusLiveSlotRequest(
            AgnusChipSlotOwner owner,
            ulong generation,
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            long earliestEligibleCycle,
            AgnusLiveWordTransfer transfer,
            ushort writeValue = 0,
            int channel = -1)
        {
            ValidateRequesterOwner(owner);
            if (generation == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generation),
                    "A live requester generation must be non-zero.");
            }

            if (requestedCycle < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedCycle),
                    "A live requester cycle must be non-negative.");
            }

            if (earliestEligibleCycle < requestedCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(earliestEligibleCycle),
                    "A live request cannot become eligible before it was requested.");
            }

            if (transfer is not (AgnusLiveWordTransfer.Read or AgnusLiveWordTransfer.Write))
            {
                throw new ArgumentOutOfRangeException(nameof(transfer), transfer, null);
            }

            Owner = owner;
            Generation = generation;
            Kind = kind;
            Address = address;
            RequestedCycle = requestedCycle;
            EarliestEligibleCycle = earliestEligibleCycle;
            Transfer = transfer;
            WriteValue = writeValue;
            Channel = channel;
        }

        public AgnusChipSlotOwner Owner { get; }

        public ulong Generation { get; }

        public AmigaBusAccessKind Kind { get; }

        public uint Address { get; }

        public long RequestedCycle { get; }

        public long EarliestEligibleCycle { get; }

        public AgnusLiveWordTransfer Transfer { get; }

        public ushort WriteValue { get; }

        public int Channel { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ValidateRequesterOwner(AgnusChipSlotOwner owner)
        {
            if (owner is AgnusChipSlotOwner.Copper or
                AgnusChipSlotOwner.Blitter or
                AgnusChipSlotOwner.Paula or
                AgnusChipSlotOwner.Disk or
                AgnusChipSlotOwner.Sprite or
                AgnusChipSlotOwner.Bitplane)
            {
                return;
            }

            throw new ArgumentOutOfRangeException(
                nameof(owner),
                owner,
                "Only live DMA requesters publish through this contract.");
        }
    }

    /// <summary>
    /// The kernel's authorization to commit one previously observed request.
    /// The sampled value is produced by the kernel's single Chip-RAM transfer,
    /// not by requester-side event discovery.
    /// </summary>
    internal readonly struct AgnusLiveSlotGrant
    {
        public AgnusLiveSlotGrant(
            in AgnusLiveSlotRequest request,
            long slotCycle,
            long completedCycle,
            ushort sampledValue)
        {
            if (slotCycle < request.EarliestEligibleCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slotCycle),
                    "A live request cannot be granted before it is eligible.");
            }

            if (completedCycle < slotCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(completedCycle),
                    "A live slot cannot complete before its grant.");
            }

            Request = request;
            SlotCycle = slotCycle;
            CompletedCycle = completedCycle;
            SampledValue = sampledValue;
        }

        public AgnusLiveSlotRequest Request { get; }

        public long SlotCycle { get; }

        public long CompletedCycle { get; }

        public ushort SampledValue { get; }
    }

    /// <summary>
    /// Stable identity for exactly one pending device-local transition. The
    /// requester retains the transition payload; the kernel only orders it.
    /// </summary>
    internal readonly struct AgnusLiveTransition
    {
        public AgnusLiveTransition(
            AgnusChipSlotOwner owner,
            ulong generation,
            long cycle,
            AgnusLiveTransitionPhase phase)
        {
            AgnusLiveSlotRequest.ValidateRequesterOwner(owner);
            if (generation == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generation),
                    "A live transition generation must be non-zero.");
            }

            if (cycle < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cycle),
                    "A live transition cycle must be non-negative.");
            }

            _ = AgnusLiveTimelineOrdering.GetTransitionOrder(phase);
            Owner = owner;
            Generation = generation;
            Cycle = cycle;
            Phase = phase;
        }

        public AgnusChipSlotOwner Owner { get; }

        public ulong Generation { get; }

        public long Cycle { get; }

        public AgnusLiveTransitionPhase Phase { get; }
    }

    /// <summary>
    /// Production-live requester boundary. Peek operations are pure. Commit
    /// operations consume exactly the token supplied by the kernel and may
    /// publish the requester's next state; they must not call a scheduler,
    /// discover another device's event, or advance a device through a range.
    /// </summary>
    internal interface IAgnusLiveSlotRequester
    {
        AgnusChipSlotOwner Owner { get; }

        bool TryPeekPendingRequest(out AgnusLiveSlotRequest request);

        bool TryPeekNextTransition(out AgnusLiveTransition transition);

        void CommitGrantedSlot(in AgnusLiveSlotGrant grant);

        void CommitTransition(in AgnusLiveTransition transition);
    }

    /// <summary>
    /// Allocation-free single-request latch shared by live adapters. It makes
    /// duplicate publication, stale grants, and cross-requester commits fail
    /// closed before device state can be mutated.
    /// </summary>
    internal struct AgnusLiveRequestLatch
    {
        private readonly AgnusChipSlotOwner _owner;
        private AgnusLiveSlotRequest _pending;
        private ulong _generation;
        private bool _hasPending;

        public AgnusLiveRequestLatch(AgnusChipSlotOwner owner)
        {
            AgnusLiveSlotRequest.ValidateRequesterOwner(owner);
            _owner = owner;
            _pending = default;
            _generation = 0;
            _hasPending = false;
        }

        public bool HasPending => _hasPending;

        public AgnusChipSlotOwner Owner => _owner;

        public AgnusLiveSlotRequest Publish(
            AmigaBusAccessKind kind,
            uint address,
            long requestedCycle,
            long earliestEligibleCycle,
            AgnusLiveWordTransfer transfer,
            ushort writeValue = 0,
            int channel = -1)
        {
            if (_hasPending)
            {
                throw new InvalidOperationException(
                    "A live requester cannot publish a second request before the first is committed or cancelled.");
            }

            _generation = NextGeneration(_generation);
            _pending = new AgnusLiveSlotRequest(
                _owner,
                _generation,
                kind,
                address,
                requestedCycle,
                earliestEligibleCycle,
                transfer,
                writeValue,
                channel);
            _hasPending = true;
            return _pending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPeek(out AgnusLiveSlotRequest request)
        {
            request = _pending;
            return _hasPending;
        }

        public AgnusLiveSlotRequest Consume(in AgnusLiveSlotGrant grant)
        {
            RequireCurrent(grant.Request);
            _hasPending = false;
            return _pending;
        }

        public AgnusLiveSlotRequest Cancel(ulong generation)
        {
            if (!_hasPending || generation == 0 || generation != _pending.Generation)
            {
                throw new InvalidOperationException(
                    "A live requester can cancel only its current pending generation.");
            }

            _hasPending = false;
            return _pending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RequireCurrent(in AgnusLiveSlotRequest request)
        {
            if (!_hasPending ||
                request.Owner != _owner ||
                request.Generation != _pending.Generation ||
                request.Kind != _pending.Kind ||
                request.Address != _pending.Address ||
                request.RequestedCycle != _pending.RequestedCycle ||
                request.EarliestEligibleCycle != _pending.EarliestEligibleCycle ||
                request.Transfer != _pending.Transfer ||
                request.WriteValue != _pending.WriteValue ||
                request.Channel != _pending.Channel)
            {
                throw new InvalidOperationException(
                    "The live slot grant does not match the requester's current pending intent.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong NextGeneration(ulong generation)
        {
            generation++;
            return generation == 0 ? 1 : generation;
        }
    }

    /// <summary>
    /// Allocation-free identity latch for one exact device-local transition.
    /// The device retains the transition payload and uses this latch only to
    /// make peek/commit ordering stable and stale-safe.
    /// </summary>
    internal struct AgnusLiveTransitionLatch
    {
        private readonly AgnusChipSlotOwner _owner;
        private AgnusLiveTransition _pending;
        private ulong _generation;
        private bool _hasPending;

        public AgnusLiveTransitionLatch(AgnusChipSlotOwner owner)
        {
            AgnusLiveSlotRequest.ValidateRequesterOwner(owner);
            _owner = owner;
            _pending = default;
            _generation = 0;
            _hasPending = false;
        }

        public bool HasPending => _hasPending;

        public AgnusLiveTransition Publish(
            long cycle,
            AgnusLiveTransitionPhase phase)
        {
            if (_hasPending)
            {
                throw new InvalidOperationException(
                    "A live requester cannot publish a second transition before the first is committed or cancelled.");
            }

            _generation = NextGeneration(_generation);
            _pending = new AgnusLiveTransition(
                _owner,
                _generation,
                cycle,
                phase);
            _hasPending = true;
            return _pending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPeek(out AgnusLiveTransition transition)
        {
            transition = _pending;
            return _hasPending;
        }

        public AgnusLiveTransition Consume(in AgnusLiveTransition transition)
        {
            RequireCurrent(transition);
            _hasPending = false;
            return _pending;
        }

        public AgnusLiveTransition Cancel(ulong generation)
        {
            if (!_hasPending || generation == 0 || generation != _pending.Generation)
            {
                throw new InvalidOperationException(
                    "A live requester can cancel only its current pending transition generation.");
            }

            _hasPending = false;
            return _pending;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RequireCurrent(in AgnusLiveTransition transition)
        {
            if (!_hasPending ||
                transition.Owner != _owner ||
                transition.Generation != _pending.Generation ||
                transition.Cycle != _pending.Cycle ||
                transition.Phase != _pending.Phase)
            {
                throw new InvalidOperationException(
                    "The live transition does not match the requester's current pending transition.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong NextGeneration(ulong generation)
        {
            generation++;
            return generation == 0 ? 1 : generation;
        }
    }
}
