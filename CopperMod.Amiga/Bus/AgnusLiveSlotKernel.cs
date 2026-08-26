/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal readonly record struct AgnusLiveSlotKernelDiagnostics(
        long CpuGrantCalls,
        long CpuWordPhases,
        long SlotIterations,
        long EmptySlotsInspected,
        long GenericSchedulerDrains,
        long CausalExecutorCalls,
        long DiskRangeAdvanceCalls,
        long DiskTransitionCommits,
        long PaulaRangeAdvanceCalls,
        long PaulaTransitionCommits,
        long DisplayPreparationRangeCalls,
        long DisplayRequesterRangeCalls,
        long DisplayTransitionCommits,
        long BlitterTransitionCommits,
        long BlitterRangeAdvanceCalls,
        long ContractViolations)
    {
        public long DisplayRangeAdvanceCalls =>
            DisplayPreparationRangeCalls + DisplayRequesterRangeCalls;

        public long RangeAdvanceCalls =>
            DiskRangeAdvanceCalls +
            PaulaRangeAdvanceCalls +
            DisplayRangeAdvanceCalls +
            BlitterRangeAdvanceCalls;

        public long ForbiddenCalls =>
            GenericSchedulerDrains +
            CausalExecutorCalls +
            RangeAdvanceCalls;
    }

    /// <summary>
    /// G6L production-disabled CPU boundary. The kernel publishes one CPU
    /// request, advances the fixed set of live requesters at one canonical
    /// slot, and attempts that exact CPU slot. It never enters Scheduler or
    /// AgnusBusExecutor advancement.
    /// </summary>
    internal sealed class AgnusLiveSlotKernel
    {
        private readonly Bus _bus;
        private long _cpuGrantCalls;
        private long _cpuWordPhases;
        private long _slotIterations;
        private long _emptySlotsInspected;
        private long _diskRangeAdvanceCalls;
        private long _diskTransitionCommits;
        private long _paulaRangeAdvanceCalls;
        private long _paulaTransitionCommits;
        private long _displayPreparationRangeCalls;
        private long _displayRequesterRangeCalls;
        private long _displayTransitionCommits;
        private long _blitterTransitionCommits;
        private long _blitterRangeAdvanceCalls;
        private long _contractViolations;
        private long _committedCpuThroughCycle = -1;

        public AgnusLiveSlotKernel(Bus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public AgnusLiveSlotKernelDiagnostics CaptureDiagnostics()
            => new(
                _cpuGrantCalls,
                _cpuWordPhases,
                _slotIterations,
                _emptySlotsInspected,
                GenericSchedulerDrains: 0,
                CausalExecutorCalls: 0,
                _diskRangeAdvanceCalls,
                _diskTransitionCommits,
                _paulaRangeAdvanceCalls,
                _paulaTransitionCommits,
                _displayPreparationRangeCalls,
                _displayRequesterRangeCalls,
                _displayTransitionCommits,
                _blitterTransitionCommits,
                _blitterRangeAdvanceCalls,
                _contractViolations);

        public void Reset()
        {
            _cpuGrantCalls = 0;
            _cpuWordPhases = 0;
            _slotIterations = 0;
            _emptySlotsInspected = 0;
            _diskRangeAdvanceCalls = 0;
            _diskTransitionCommits = 0;
            _paulaRangeAdvanceCalls = 0;
            _paulaTransitionCommits = 0;
            _displayPreparationRangeCalls = 0;
            _displayRequesterRangeCalls = 0;
            _displayTransitionCommits = 0;
            _blitterTransitionCommits = 0;
            _blitterRangeAdvanceCalls = 0;
            _contractViolations = 0;
            _committedCpuThroughCycle = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordDiskRangeAdvanceCall()
            => _diskRangeAdvanceCalls++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordDiskTransitionCommit()
            => _diskTransitionCommits++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordPaulaRangeAdvanceCall()
            => _paulaRangeAdvanceCalls++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordPaulaTransitionCommit()
            => _paulaTransitionCommits++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordDisplayPreparationRangeCall()
            => _displayPreparationRangeCalls++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordDisplayRequesterRangeCall()
            => _displayRequesterRangeCalls++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordDisplayTransitionCommit()
            => _displayTransitionCommits++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordBlitterTransitionCommit()
            => _blitterTransitionCommits++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordBlitterRangeAdvanceCall()
            => _blitterRangeAdvanceCalls++;

        internal long CommittedCpuThroughCycle => _committedCpuThroughCycle;

        public void GrantCpuAccess(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            if (target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock or
                    AmigaBusAccessTarget.CustomRegisters))
            {
                _contractViolations++;
                throw new InvalidOperationException(
                    "The live Agnus slot kernel received a non-Chip-bus CPU target.");
            }

            _cpuGrantCalls++;
            requestedCycle = Math.Max(0, requestedCycle);
            var customReadVisibilityCycle =
                target == AmigaBusAccessTarget.CustomRegisters && !isWrite
                    ? requestedCycle
                    : -1;

            if (size != AmigaBusAccessSize.Long)
            {
                GrantCpuWordPhase(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    requestedCycle,
                    isWrite,
                    deferAfterSlotBlitterTransitions: false,
                    customReadVisibilityCycle,
                    out grantedCycle,
                    out completedCycle);
                secondWordCycle = grantedCycle;
                return;
            }

            GrantCpuWordPhase(
                kind,
                target,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                requestedCycle,
                isWrite,
                deferAfterSlotBlitterTransitions: true,
                customReadVisibilityCycle,
                out grantedCycle,
                out var firstCompletedCycle);
            GrantCpuWordPhase(
                kind,
                target,
                address,
                AmigaBusAccessSize.Word,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles,
                requestedCycle,
                isWrite,
                deferAfterSlotBlitterTransitions: false,
                customReadVisibilityCycle: -1,
                out secondWordCycle,
                out completedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GrantCpuWordPhase(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long searchCycle,
            long requestedCycle,
            bool isWrite,
            bool deferAfterSlotBlitterTransitions,
            long customReadVisibilityCycle,
            out long grantedCycle,
            out long completedCycle)
        {
            _cpuWordPhases++;
            var causalStart = Math.Max(
                _bus.ExecutedChipBusHorizon + 1,
                _bus.GetNextLiveSlotKernelCycle(searchCycle));
            var candidate = AgnusChipSlotScheduler.AlignToSlot(causalStart);
            var firstCandidate = candidate;
            _bus.BeginLiveSlotKernelCpuRequest(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite);
            try
            {
                while (true)
                {
                    _slotIterations++;
                    var horizonBefore = _bus.ExecutedChipBusHorizon;
                    // Disk and Paula own their fixed slots before the display
                    // planner decides whether Copper may reuse one. A request
                    // can be published by a device transition on this exact
                    // cycle, so settle the matured fixed requesters explicitly
                    // before materializing the display/Copper suffix.
                    if (customReadVisibilityCycle >= 0 &&
                        candidate >= customReadVisibilityCycle)
                    {
                        // Stage 5 makes raster, CIA, Paula-register, and passive
                        // disk state visible at every custom-register read
                        // boundary. Settle older physical disk slots first:
                        // synchronizing before this loop can reanchor WORDSYNC
                        // state that older grants must still sample. Conversely,
                        // a transition on this exact candidate belongs after the
                        // read barrier. Catch up only through the preceding slot,
                        // publish the boundary, then settle this candidate. Repeat
                        // this checkpoint after every denied candidate: Stage 5
                        // advances register-visible state through the eventual
                        // grant, not merely through the original request. A sync
                        // transition may itself publish DMA for the final pass.
                        _bus.AdvanceDueLiveFixedRequestersTo(
                            candidate - AgnusChipSlotScheduler.SlotCycles);
                        _bus.SynchronizeLiveSlotKernelCustomReadBoundaryTo(
                            candidate);
                        _bus.AdvanceDueLiveFixedRequestersTo(candidate);
                    }
                    else
                    {
                        _bus.AdvanceDueLiveFixedRequestersTo(candidate);
                    }
                    // The display control horizon can be ahead of its
                    // uncommitted fixed-fetch cursor. Materialize the same
                    // candidate-bounded display/Copper suffix used by the
                    // Stage-5 oracle before any requester or CPU can claim
                    // this physical slot.
                    _bus.PrepareLiveSlotKernelDisplaySlots(candidate);
                    // Keep the CPU invisible while the fixed suffix is being
                    // materialized. Publish it only for the physical
                    // arbitration window so fixed preparation cannot commit a
                    // speculative CPU owner that a dynamic requester later
                    // replaces.
                    _bus.PublishLiveSlotKernelCpuRequest(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        isWrite);
                    var preserveFirstPhaseAfterSlotBoundary =
                        deferAfterSlotBlitterTransitions &&
                        (candidate == firstCandidate ||
                         _bus.IsLiveSlotKernelFixedDmaSlotPredicted(
                             candidate + AgnusChipSlotScheduler.SlotCycles));
                    var advanceBlitter =
                        !preserveFirstPhaseAfterSlotBoundary ||
                        !_bus.HasLiveSlotKernelBlitterAfterSlotTransitionThrough(
                            candidate) ||
                        _bus.HasLiveSlotKernelBlitterCpuBoundaryFinishAt(candidate);
                    _bus.AdvanceLiveSlotKernelRequesters(
                        candidate,
                        advanceBlitter);
                    if (candidate < searchCycle ||
                        candidate <= _committedCpuThroughCycle)
                    {
                        // A 68000 longword keeps the CPU bus request active
                        // during its interphase slot. That slot cannot grant
                        // the second word yet, but a fixed/DMA owner still
                        // contributes to the nice-blitter wait quota.
                        _bus.ObserveLiveSlotKernelCpuWaitCycle(candidate);
                    }
                    else if (_bus.TryGrantLiveSlotKernelCpuWord(
                            kind,
                            target,
                            address,
                            size,
                            requestedCycle,
                            candidate,
                            isWrite,
                            out completedCycle))
                    {
                        _committedCpuThroughCycle = Math.Max(
                            _committedCpuThroughCycle,
                            candidate);
                        _bus.ObserveLiveSlotKernelCpuGrant(candidate);
                        grantedCycle = candidate;
                        return;
                    }

                    _bus.WithdrawLiveSlotKernelCpuRequest();

                    if (_bus.ExecutedChipBusHorizon == horizonBefore)
                    {
                        _emptySlotsInspected++;
                    }

                    candidate += AgnusChipSlotScheduler.SlotCycles;
                }
            }
            finally
            {
                _bus.ClearLiveSlotKernelCpuRequest();
            }
        }
    }
}
