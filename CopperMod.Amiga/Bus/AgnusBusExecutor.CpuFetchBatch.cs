/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using CopperMod.Amiga.CustomChips.Denise;

namespace CopperMod.Amiga.Bus
{
    internal readonly record struct CpuChipFetchLease(
        ulong LeaseId,
        long StartCycle,
        long EndCycleExclusive,
        ulong DisplayWakeVersion,
        uint MappingGeneration,
        long SafeCopperMoveCycle,
        CpuBatchCopperLookahead CopperLookahead);

    internal readonly record struct CpuChipFetchWordRequest(
        uint Address,
        long RequestedCycle,
        long ProvenGrantedCycle,
        ushort SpeculativeValue);

    internal readonly record struct CpuChipFetchWordResult(
        ushort Value,
        long GrantedCycle,
        long CompletedCycle);

    internal enum CpuChipFetchBatchStopReason : byte
    {
        Completed,
        Empty,
        InvalidRequest,
        BehindExecutedHorizon,
        LeaseExpired,
        FixedPlanChanged,
        MappingChanged,
        DynamicDeadline,
        WriterHazard,
        UnsupportedOwner,
        GrantMismatch,
        ValueMismatch
    }

    internal readonly record struct CpuChipFetchBatchResult(
        int CommittedWords,
        long CleanThroughCycle,
        long CompletedCycle,
        long TotalWaitCycles,
        CpuChipFetchBatchStopReason StopReason);

    internal sealed partial class AgnusBusExecutor
    {
        private ulong _cpuChipFetchLeaseId;
        private long _cpuChipFetchLeaseAttempts;
        private long _cpuChipFetchLeaseAccepted;
        private long _cpuChipFetchLeaseRejected;
        private long _cpuChipFetchLeaseRejectedInvalid;
        private long _cpuChipFetchLeaseRejectedNoStableInterval;
        private long _cpuChipFetchCopperLookaheadAttempts;
        private long _cpuChipFetchCopperLookaheadAccepted;
        private long _cpuChipFetchCopperLookaheadContinuationAttempts;
        private long _cpuChipFetchCopperLookaheadContinuations;
        private long _cpuChipFetchWordsProven;
        private long _cpuChipFetchProofRejectedLease;
        private long _cpuChipFetchProofRejectedLeaseExpired;
        private long _cpuChipFetchProofRejectedFixedPlan;
        private long _cpuChipFetchProofRejectedMapping;
        private long _cpuChipFetchProofRejectedLeaseWriter;
        private long _cpuChipFetchProofRejectedInput;
        private long _cpuChipFetchProofRejectedWriter;
        private long _cpuChipFetchProofRejectedDynamic;
        private long _cpuChipFetchProofRejectedUnsupportedOwner;
        private long _cpuChipFetchCommitAttempts;
        private long _cpuChipFetchCompletedRuns;
        private long _cpuChipFetchPartialRuns;
        private long _cpuChipFetchCommittedWords;
        private long _cpuChipFetchGrantMismatches;
        private long _cpuChipFetchValueMismatches;
        private long _cpuChipFetchFixedSlotsRefresh;
        private long _cpuChipFetchFixedSlotsBitplane;
        private long _cpuChipFetchFixedSlotsSprite;
        private long _cpuChipFetchFixedSlotsScanned;
        private long _cpuChipFetchMaxFixedSlotsScannedPerWord;
        private long _cpuChipFetchTotalWaitCycles;
        private long _cpuChipFetchRunLength1;
        private long _cpuChipFetchRunLength2;
        private long _cpuChipFetchRunLength3To4;
        private long _cpuChipFetchRunLength5To8;
        private long _cpuChipFetchRunLength9To16;
        private long _cpuChipFetchRunLength17To32;
        private long _cpuChipFetchRunLength33To64;
        private CpuChipFetchBatchStopReason _lastCpuChipFetchProofRejectReason;
        private readonly bool _cpuChipFetchTimelineTraceEnabled =
            Environment.GetEnvironmentVariable("COPPER_CHIP_FETCH_TIMELINE_TRACE") == "1";

        public long CpuChipFetchLeaseAttempts => _cpuChipFetchLeaseAttempts;
        public long CpuChipFetchLeaseAccepted => _cpuChipFetchLeaseAccepted;
        public long CpuChipFetchLeaseRejected => _cpuChipFetchLeaseRejected;
        public long CpuChipFetchLeaseRejectedInvalid => _cpuChipFetchLeaseRejectedInvalid;
        public long CpuChipFetchLeaseRejectedNoStableInterval => _cpuChipFetchLeaseRejectedNoStableInterval;
        public long CpuChipFetchCopperLookaheadAttempts => _cpuChipFetchCopperLookaheadAttempts;
        public long CpuChipFetchCopperLookaheadAccepted => _cpuChipFetchCopperLookaheadAccepted;
        public long CpuChipFetchCopperLookaheadContinuationAttempts =>
            _cpuChipFetchCopperLookaheadContinuationAttempts;
        public long CpuChipFetchCopperLookaheadContinuations =>
            _cpuChipFetchCopperLookaheadContinuations;
        public long CpuChipFetchWordsProven => _cpuChipFetchWordsProven;
        public long CpuChipFetchProofRejectedLease => _cpuChipFetchProofRejectedLease;
        public long CpuChipFetchProofRejectedLeaseExpired => _cpuChipFetchProofRejectedLeaseExpired;
        public long CpuChipFetchProofRejectedFixedPlan => _cpuChipFetchProofRejectedFixedPlan;
        public long CpuChipFetchProofRejectedMapping => _cpuChipFetchProofRejectedMapping;
        public long CpuChipFetchProofRejectedLeaseWriter => _cpuChipFetchProofRejectedLeaseWriter;
        public long CpuChipFetchProofRejectedInput => _cpuChipFetchProofRejectedInput;
        public long CpuChipFetchProofRejectedWriter => _cpuChipFetchProofRejectedWriter;
        public long CpuChipFetchProofRejectedDynamic => _cpuChipFetchProofRejectedDynamic;
        public long CpuChipFetchProofRejectedUnsupportedOwner =>
            _cpuChipFetchProofRejectedUnsupportedOwner;
        public long CpuChipFetchCommitAttempts => _cpuChipFetchCommitAttempts;
        public long CpuChipFetchCompletedRuns => _cpuChipFetchCompletedRuns;
        public long CpuChipFetchPartialRuns => _cpuChipFetchPartialRuns;
        public long CpuChipFetchCommittedWords => _cpuChipFetchCommittedWords;
        public long CpuChipFetchGrantMismatches => _cpuChipFetchGrantMismatches;
        public long CpuChipFetchValueMismatches => _cpuChipFetchValueMismatches;
        public long CpuChipFetchFixedSlotsRefresh => _cpuChipFetchFixedSlotsRefresh;
        public long CpuChipFetchFixedSlotsBitplane => _cpuChipFetchFixedSlotsBitplane;
        public long CpuChipFetchFixedSlotsSprite => _cpuChipFetchFixedSlotsSprite;
        public long CpuChipFetchFixedSlotsScanned => _cpuChipFetchFixedSlotsScanned;
        public long CpuChipFetchMaxFixedSlotsScannedPerWord => _cpuChipFetchMaxFixedSlotsScannedPerWord;
        public long CpuChipFetchTotalWaitCycles => _cpuChipFetchTotalWaitCycles;
        public long CpuChipFetchRunLength1 => _cpuChipFetchRunLength1;
        public long CpuChipFetchRunLength2 => _cpuChipFetchRunLength2;
        public long CpuChipFetchRunLength3To4 => _cpuChipFetchRunLength3To4;
        public long CpuChipFetchRunLength5To8 => _cpuChipFetchRunLength5To8;
        public long CpuChipFetchRunLength9To16 => _cpuChipFetchRunLength9To16;
        public long CpuChipFetchRunLength17To32 => _cpuChipFetchRunLength17To32;
        public long CpuChipFetchRunLength33To64 => _cpuChipFetchRunLength33To64;
        internal CpuChipFetchBatchStopReason LastCpuChipFetchProofRejectReason =>
            _lastCpuChipFetchProofRejectReason;

        internal bool TryAcquireCpuChipFetchLease(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out CpuChipFetchLease lease,
            bool trackCounters = true)
        {
            if (trackCounters) _cpuChipFetchLeaseAttempts++;
            lease = default;
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle || currentCycle < _executedThroughCycle)
            {
                if (trackCounters)
                {
                    _cpuChipFetchLeaseRejected++;
                    _cpuChipFetchLeaseRejectedInvalid++;
                }
                return false;
            }

            var visibility = GetNextCpuVisibilityHorizon(currentCycle, targetCycle, cpuInterruptMask);
            var copperLookahead = default(CpuBatchCopperLookahead);
            var copperBarrier =
                _bus.Display.GetNextLiveCopperCpuBatchBarrierCycle(currentCycle, targetCycle);
            if (trackCounters && copperBarrier.HasValue)
            {
                _cpuChipFetchCopperLookaheadAttempts++;
            }
            if (copperBarrier.HasValue &&
                _bus.Display.TryGetCpuBatchSafeCopperLookahead(
                    currentCycle,
                    targetCycle,
                    out var safeLookahead) &&
                copperBarrier.Value <=
                    (safeLookahead.FirstGrantCycle >= currentCycle
                        ? safeLookahead.FirstGrantCycle
                        : safeLookahead.SecondGrantCycle))
            {
                var nonCopperVisibility =
                    visibility.Reason == CpuVisibilityHorizonReason.Copper
                        ? GetNextCpuVisibilityHorizonIgnoringCopper(
                            currentCycle,
                            safeLookahead.StopCycle,
                            cpuInterruptMask)
                        : visibility;
                if (nonCopperVisibility.Cycle >= safeLookahead.StopCycle &&
                    !MayWriteChipRamBefore(safeLookahead.StopCycle - 1))
                {
                    if (trackCounters) _cpuChipFetchCopperLookaheadAccepted++;
                    copperLookahead = safeLookahead;
                    visibility = new CpuVisibilityHorizon(
                        safeLookahead.StopCycle,
                        CpuVisibilityHorizonReason.Copper,
                        AmigaDiskController.SchedulerWakeReason.None);
                }
            }
            var endCycle = Math.Min(targetCycle, visibility.Cycle);
            endCycle = Math.Min(
                endCycle,
                GetNextCpuChipFetchDynamicBoundary(
                    currentCycle,
                    endCycle,
                    copperLookahead.StopCycle));
            endCycle = Math.Min(endCycle, GetNextChipRamWriteHazardCycle(currentCycle));
            if (endCycle <= currentCycle)
            {
                if (trackCounters)
                {
                    _cpuChipFetchLeaseRejected++;
                    _cpuChipFetchLeaseRejectedNoStableInterval++;
                }
                return false;
            }

            var leaseId = ++_cpuChipFetchLeaseId;
            if (leaseId == 0)
            {
                leaseId = ++_cpuChipFetchLeaseId;
            }

            // RawLiveBusEligibilityVersion includes the captured-through cursor,
            // which advances as this executor commits the already-proven fixed
            // owners. A lease tracks schedule invalidation, not that expected
            // chronological progress, so it must use the generation-only wake
            // version.
            var safeCopperMoveCycle =
                _bus.Display.TryGetSafeCpuChipFetchCopperMoveBoundary(
                    endCycle,
                    out var pendingSafeCopperMoveCycle)
                    ? pendingSafeCopperMoveCycle
                    : -1;
            lease = new CpuChipFetchLease(leaseId, currentCycle, endCycle,
                _bus.Display.LiveWakeVersion, _bus.InstructionFetchMappingGeneration,
                safeCopperMoveCycle, copperLookahead);
            if (trackCounters) _cpuChipFetchLeaseAccepted++;
            return true;
        }

        internal bool TryProveCpuChipInstructionFetch(
            in CpuChipFetchLease lease,
            uint address,
            long physicalRequestedCycle,
            out CpuChipFetchWordRequest proof,
            bool trackCounters = true)
        {
            proof = default;
            _lastCpuChipFetchProofRejectReason = CpuChipFetchBatchStopReason.Completed;
            var leaseStop = GetCpuChipFetchLeaseStopReason(in lease);
            if (leaseStop != CpuChipFetchBatchStopReason.Completed)
            {
                _lastCpuChipFetchProofRejectReason = leaseStop;
                if (trackCounters)
                {
                    _cpuChipFetchProofRejectedLease++;
                    switch (leaseStop)
                    {
                        case CpuChipFetchBatchStopReason.LeaseExpired:
                            _cpuChipFetchProofRejectedLeaseExpired++;
                            break;
                        case CpuChipFetchBatchStopReason.FixedPlanChanged:
                            _cpuChipFetchProofRejectedFixedPlan++;
                            break;
                        case CpuChipFetchBatchStopReason.MappingChanged:
                            _cpuChipFetchProofRejectedMapping++;
                            break;
                        case CpuChipFetchBatchStopReason.WriterHazard:
                            _cpuChipFetchProofRejectedLeaseWriter++;
                            break;
                    }
                }
                return false;
            }
            if ((address & 1) != 0 ||
                !_bus.IsChipRamInstructionFetchAddress(address) ||
                physicalRequestedCycle < lease.StartCycle ||
                physicalRequestedCycle < _executedThroughCycle)
            {
                _lastCpuChipFetchProofRejectReason = CpuChipFetchBatchStopReason.InvalidRequest;
                if (trackCounters) _cpuChipFetchProofRejectedInput++;
                return false;
            }
            if (MayWriteChipRamBefore(lease.EndCycleExclusive - 1))
            {
                _lastCpuChipFetchProofRejectReason = CpuChipFetchBatchStopReason.WriterHazard;
                if (trackCounters) _cpuChipFetchProofRejectedWriter++;
                return false;
            }

            if (!TryFindCpuChipFetchGrant(
                    physicalRequestedCycle,
                    lease.EndCycleExclusive,
                    lease.CopperLookahead,
                    out var grantCycle,
                    out var stopReason))
            {
                _lastCpuChipFetchProofRejectReason = stopReason;
                if (trackCounters)
                {
                    if (stopReason == CpuChipFetchBatchStopReason.UnsupportedOwner)
                        _cpuChipFetchProofRejectedUnsupportedOwner++;
                    else
                        _cpuChipFetchProofRejectedDynamic++;
                }
                return false;
            }

            proof = new CpuChipFetchWordRequest(address, physicalRequestedCycle, grantCycle,
                _bus.ReadCpuInstructionFetchChipWordSpeculative(address));
            if (trackCounters) _cpuChipFetchWordsProven++;
            return true;
        }

        internal CpuChipFetchBatchResult ExecuteCpuChipFetchBatch(in CpuChipFetchLease lease, ReadOnlySpan<CpuChipFetchWordRequest> requests, Span<CpuChipFetchWordResult> results)
        {
            _cpuChipFetchCommitAttempts++;
            if (requests.Length == 0)
            {
                return new CpuChipFetchBatchResult(0, _executedThroughCycle, _executedThroughCycle, 0, CpuChipFetchBatchStopReason.Empty);
            }
            if (results.Length < requests.Length)
            {
                return new CpuChipFetchBatchResult(0, _executedThroughCycle, _executedThroughCycle, 0, CpuChipFetchBatchStopReason.InvalidRequest);
            }

            var leaseStop = GetCpuChipFetchLeaseStopReason(in lease);
            if (leaseStop != CpuChipFetchBatchStopReason.Completed)
            {
                return new CpuChipFetchBatchResult(0, _executedThroughCycle, _executedThroughCycle, 0, leaseStop);
            }

            var prefix = 0;
            var previousRequested = lease.StartCycle;
            var previousCompleted = lease.StartCycle;
            var stopReason = CpuChipFetchBatchStopReason.Completed;
            for (; prefix < requests.Length; prefix++)
            {
                ref readonly var request = ref requests[prefix];
                if ((request.Address & 1) != 0 || !_bus.IsChipRamInstructionFetchAddress(request.Address) ||
                    request.RequestedCycle < previousRequested || request.RequestedCycle < previousCompleted ||
                    request.RequestedCycle < _executedThroughCycle)
                {
                    stopReason = request.RequestedCycle < _executedThroughCycle
                        ? CpuChipFetchBatchStopReason.BehindExecutedHorizon : CpuChipFetchBatchStopReason.InvalidRequest;
                    break;
                }
                if (!TryFindCpuChipFetchGrant(
                        request.RequestedCycle,
                        lease.EndCycleExclusive,
                        lease.CopperLookahead,
                        out var expectedGrant,
                        out stopReason))
                {
                    break;
                }
                if (expectedGrant != request.ProvenGrantedCycle)
                {
                    _cpuChipFetchGrantMismatches++;
                    stopReason = CpuChipFetchBatchStopReason.GrantMismatch;
                    break;
                }
                if (_bus.ReadCpuInstructionFetchChipWordSpeculative(request.Address) != request.SpeculativeValue)
                {
                    _cpuChipFetchValueMismatches++;
                    stopReason = CpuChipFetchBatchStopReason.ValueMismatch;
                    break;
                }
                previousRequested = request.RequestedCycle;
                previousCompleted = expectedGrant + AgnusChipSlotScheduler.SlotCycles;
            }

            if (prefix == 0)
            {
                return new CpuChipFetchBatchResult(0, _executedThroughCycle, _executedThroughCycle, 0, stopReason);
            }

            var completedCycle = _executedThroughCycle;
            var totalWaitCycles = 0L;
            try
            {
                for (var index = 0; index < prefix; index++)
                {
                    ref readonly var request = ref requests[index];
                    if ((lease.CopperLookahead.Pc == 0
                            ? _bus.Display.LiveWakeVersion != lease.DisplayWakeVersion
                            : !_bus.Display.IsCpuBatchSafeCopperLookaheadCurrent(
                                lease.CopperLookahead)) ||
                        _bus.InstructionFetchMappingGeneration != lease.MappingGeneration)
                    {
                        throw new InvalidOperationException("CPU Chip fetch lease changed during executor commitment.");
                    }
                    var scannedFixedSlots = CountCpuChipFetchFixedSlots(
                        request.RequestedCycle,
                        request.ProvenGrantedCycle);
                    AdvanceThrough(request.ProvenGrantedCycle - 1);
                    if (_bus.AdvancePendingCpuGrantToCausalBusHorizon(
                            AmigaBusAccessTarget.ChipRam,
                            request.ProvenGrantedCycle) != request.ProvenGrantedCycle)
                    {
                        throw new InvalidOperationException(
                            "Proven CPU Chip fetch grant moved during chronological commitment.");
                    }
                    if (!TryCommitCpuDataKnownQuietSlot(AmigaBusAccessKind.CpuInstructionFetch,
                            AmigaBusAccessTarget.ChipRam, request.Address, request.RequestedCycle,
                            request.ProvenGrantedCycle, isWrite: false, out completedCycle))
                    {
                        throw new InvalidOperationException("Proven CPU Chip fetch grant was not commit-able.");
                    }
                    var value = _bus.ReadCpuInstructionFetchChipWordAtGrantedSlot(request.Address, request.ProvenGrantedCycle);
                    if (value != request.SpeculativeValue)
                    {
                        _cpuChipFetchValueMismatches++;
                        throw new InvalidOperationException("CPU Chip fetch value changed after proof.");
                    }
                    RecordCpuChipFetchFixedSlotScan(scannedFixedSlots);
                    results[index] = new CpuChipFetchWordResult(value, request.ProvenGrantedCycle, completedCycle);
                    totalWaitCycles += request.ProvenGrantedCycle - request.RequestedCycle;
                }
            }
            finally
            {
                ClearCpuIntent();
            }

            _cpuChipFetchCommittedWords += prefix;
            _cpuChipFetchTotalWaitCycles += totalWaitCycles;
            RecordCpuChipFetchRunLength(prefix);
            if (prefix == requests.Length)
            {
                _cpuChipFetchCompletedRuns++;
                stopReason = CpuChipFetchBatchStopReason.Completed;
            }
            else
            {
                _cpuChipFetchPartialRuns++;
            }
            if (_cpuChipFetchTimelineTraceEnabled)
            {
                ref readonly var first = ref requests[0];
                ref readonly var last = ref requests[prefix - 1];
                Console.WriteLine(
                    $"chip-fetch-commit\t{lease.StartCycle}\t{lease.EndCycleExclusive}\t{prefix}\t" +
                    $"{first.Address:X8}\t{first.RequestedCycle}\t{first.ProvenGrantedCycle}\t" +
                    $"{last.Address:X8}\t{last.RequestedCycle}\t{last.ProvenGrantedCycle}\t" +
                    $"{completedCycle}\t{totalWaitCycles}");
            }
            return new CpuChipFetchBatchResult(prefix, _executedThroughCycle, completedCycle, totalWaitCycles, stopReason);
        }

        internal long GetNextChipRamWriteHazardCycle(long currentCycle)
        {
            currentCycle = Math.Max(0, currentCycle);
            var hazard = _cpuEventJournal.Count == 0 ? long.MaxValue : CpuJournalDeadlineCycle;
            ref readonly var cpu = ref _intents[(int)AgnusBusAgendaSource.Cpu];
            ref readonly var disk = ref _intents[(int)AgnusBusAgendaSource.Disk];
            ref readonly var blitter = ref _intents[(int)AgnusBusAgendaSource.Blitter];
            if (cpu.Pending && cpu.IsWrite) hazard = Math.Min(hazard, cpu.EarliestCycle);
            if (disk.Pending && disk.IsWrite) hazard = Math.Min(hazard, disk.EarliestCycle);
            if (blitter.Pending && blitter.IsWrite) hazard = Math.Min(hazard, blitter.EarliestCycle);
            RefreshChipRamWriteHazards();
            hazard = Math.Min(hazard, _diskChipWriteHazardCycle);
            hazard = Math.Min(hazard, _blitterChipWriteHazardCycle);
            return hazard <= currentCycle ? currentCycle : hazard;
        }

        private CpuChipFetchBatchStopReason GetCpuChipFetchLeaseStopReason(in CpuChipFetchLease lease)
        {
            if (lease.LeaseId == 0 || lease.LeaseId != _cpuChipFetchLeaseId || lease.EndCycleExclusive <= lease.StartCycle)
                return CpuChipFetchBatchStopReason.LeaseExpired;
            if (_bus.InstructionFetchMappingGeneration != lease.MappingGeneration)
                return CpuChipFetchBatchStopReason.MappingChanged;
            if (lease.CopperLookahead.Pc == 0
                    ? _bus.Display.LiveWakeVersion != lease.DisplayWakeVersion
                    : !_bus.Display.IsCpuBatchSafeCopperLookaheadCurrent(
                        lease.CopperLookahead))
                return CpuChipFetchBatchStopReason.FixedPlanChanged;
            if (MayWriteChipRamBefore(lease.EndCycleExclusive - 1))
                return CpuChipFetchBatchStopReason.WriterHazard;
            return CpuChipFetchBatchStopReason.Completed;
        }

        internal bool IsCpuChipFetchLeaseCurrent(in CpuChipFetchLease lease)
            => GetCpuChipFetchLeaseStopReason(in lease) == CpuChipFetchBatchStopReason.Completed;

        internal bool TryAdvanceCpuChipFetchSafeCopperBoundary(
            in CpuChipFetchLease lease,
            out long advancedThroughCycle)
        {
            advancedThroughCycle = _executedThroughCycle;
            if (!IsCpuChipFetchLeaseCurrent(in lease))
            {
                return false;
            }

            if (lease.CopperLookahead.Pc != 0)
            {
                _cpuChipFetchCopperLookaheadContinuationAttempts++;
                AdvanceThrough(lease.CopperLookahead.StopCycle - 1);
                if (!_bus.Display.IsCpuBatchSafeCopperLookaheadCurrent(
                        lease.CopperLookahead))
                {
                    return false;
                }

                _queryCacheValid = false;
                _cpuChipFetchCopperLookaheadContinuations++;
                advancedThroughCycle = _executedThroughCycle;
                return true;
            }

            if (
                lease.SafeCopperMoveCycle < lease.StartCycle ||
                !_bus.Display.TryGetSafeCpuChipFetchCopperMoveBoundary(
                    lease.EndCycleExclusive,
                    out var moveCycle) ||
                moveCycle != lease.SafeCopperMoveCycle)
            {
                return false;
            }

            // This is not inferred from an arbitrary lease end. Acquisition
            // recorded the exact pending safe MOVE, and the live state above
            // must still describe that same boundary before it is committed.
            if (_bus.AdvanceCpuWaitLiveSlot(
                    moveCycle,
                    out _,
                    out _,
                    out var completedSafeCopper) != OcsCpuWaitLiveSlotResult.Processed ||
                !completedSafeCopper)
            {
                return false;
            }

            _executedThroughCycle = Math.Max(_executedThroughCycle, moveCycle);
            _queryCacheValid = false;
            advancedThroughCycle = _executedThroughCycle;
            return true;
        }

        private bool TryFindCpuChipFetchGrant(
            long requestedCycle,
            long endCycleExclusive,
            CpuBatchCopperLookahead copperLookahead,
            out long grantCycle,
            out CpuChipFetchBatchStopReason stopReason)
        {
            var safeCopperThroughCycle = copperLookahead.StopCycle;
            var candidate = AgnusChipSlotScheduler.AlignToSlot(Math.Max(requestedCycle, _executedThroughCycle + 1));
            while (candidate < endCycleExclusive)
            {
                if (GetNextCpuChipFetchDynamicBoundary(
                        candidate,
                        endCycleExclusive,
                        safeCopperThroughCycle) <= candidate) break;
                if (safeCopperThroughCycle > candidate &&
                    (candidate == copperLookahead.FirstGrantCycle ||
                     candidate == copperLookahead.SecondGrantCycle))
                {
                    candidate += AgnusChipSlotScheduler.SlotCycles;
                    continue;
                }
                if (!TryGetCpuChipFetchFixedOwner(candidate, out var owner))
                {
                    grantCycle = 0;
                    stopReason = CpuChipFetchBatchStopReason.UnsupportedOwner;
                    return false;
                }
                if (owner == AgnusChipSlotOwner.Free)
                {
                    grantCycle = candidate;
                    stopReason = CpuChipFetchBatchStopReason.Completed;
                    return true;
                }
                if (owner is not (AgnusChipSlotOwner.Refresh or AgnusChipSlotOwner.Bitplane or AgnusChipSlotOwner.Sprite))
                {
                    grantCycle = 0;
                    stopReason = CpuChipFetchBatchStopReason.UnsupportedOwner;
                    return false;
                }
                candidate += AgnusChipSlotScheduler.SlotCycles;
            }
            grantCycle = 0;
            stopReason = GetNextChipRamWriteHazardCycle(requestedCycle) < endCycleExclusive
                ? CpuChipFetchBatchStopReason.WriterHazard : CpuChipFetchBatchStopReason.DynamicDeadline;
            return false;
        }

        private long GetNextCpuChipFetchDynamicBoundary(
            long currentCycle,
            long targetCycle,
            long safeCopperThroughCycle = 0)
        {
            RefreshDeviceDeadlines();
            var boundary = long.MaxValue;
            // Display refresh/bitplane/sprite slots are the fixed schedule this
            // path is responsible for traversing. Copper and pending display
            // writes remain represented by the Copper/Control barriers below.
            boundary = MinFutureBoundary(boundary, _agenda.Get(AgnusBusAgendaSource.Paula), currentCycle);
            boundary = MinFutureBoundary(boundary, _agenda.Get(AgnusBusAgendaSource.Disk), currentCycle);
            var copperBarrier =
                _bus.Display.GetNextLiveCopperCpuBatchBarrierCycle(currentCycle, targetCycle);
            if (copperBarrier.HasValue &&
                (safeCopperThroughCycle <= currentCycle ||
                 copperBarrier.Value > safeCopperThroughCycle))
            {
                boundary = MinFutureBoundary(boundary, copperBarrier.Value, currentCycle);
            }
            boundary = MinFutureBoundary(boundary, _agenda.Get(AgnusBusAgendaSource.Control), currentCycle);
            boundary = MinFutureBoundary(boundary, _agenda.Get(AgnusBusAgendaSource.Raster), currentCycle);
            boundary = MinFutureBoundary(boundary, _agenda.Get(AgnusBusAgendaSource.Blitter), currentCycle);
            return boundary;
        }

        private bool TryGetCpuChipFetchFixedOwner(long slotCycle, out AgnusChipSlotOwner owner)
        {
            // The fixed image can validate a live frame even while DMA is
            // temporarily quiet. That matters for sprite slots armed by prior
            // line state: treating a quiet interval as static can make the
            // CPU consume a slot that scalar materializes as sprite DMA.
            if (_bus.LiveDisplayDmaEnabled &&
                _bus.Display.TryGetCpuChipFetchFixedSlotOwner(slotCycle, out var fixedOwner, out _))
            {
                owner = fixedOwner switch
                {
                    CpuWaitFixedSlotOwner.Refresh => AgnusChipSlotOwner.Refresh,
                    CpuWaitFixedSlotOwner.BitplaneRead => AgnusChipSlotOwner.Bitplane,
                    CpuWaitFixedSlotOwner.SpriteRead => AgnusChipSlotOwner.Sprite,
                    _ => AgnusChipSlotOwner.Free
                };
                return true;
            }

            if (_bus.LiveDisplayDmaEnabled && _bus.Display.HasLiveDisplayDmaOrWriteWork())
            {
                owner = AgnusChipSlotOwner.Free;
                return false;
            }

            owner = GetPlannedFixedOwnerAt(slotCycle, out _);
            return true;
        }

        private int CountCpuChipFetchFixedSlots(long requestedCycle, long grantedCycle)
        {
            var candidate = AgnusChipSlotScheduler.AlignToSlot(
                Math.Max(requestedCycle, _executedThroughCycle + 1));
            var scanned = 0;
            while (candidate < grantedCycle &&
                TryGetCpuChipFetchFixedOwner(candidate, out var owner))
            {
                scanned++;
                switch (owner)
                {
                    case AgnusChipSlotOwner.Refresh:
                        _cpuChipFetchFixedSlotsRefresh++;
                        break;
                    case AgnusChipSlotOwner.Bitplane:
                        _cpuChipFetchFixedSlotsBitplane++;
                        break;
                    case AgnusChipSlotOwner.Sprite:
                        _cpuChipFetchFixedSlotsSprite++;
                        break;
                }
                candidate += AgnusChipSlotScheduler.SlotCycles;
            }

            return scanned;
        }

        private void RecordCpuChipFetchFixedSlotScan(int scanned)
        {
            _cpuChipFetchFixedSlotsScanned += scanned;
            if (scanned > _cpuChipFetchMaxFixedSlotsScannedPerWord)
            {
                _cpuChipFetchMaxFixedSlotsScannedPerWord = scanned;
            }
        }

        private void RecordCpuChipFetchRunLength(int length)
        {
            if (length == 1) _cpuChipFetchRunLength1++;
            else if (length == 2) _cpuChipFetchRunLength2++;
            else if (length <= 4) _cpuChipFetchRunLength3To4++;
            else if (length <= 8) _cpuChipFetchRunLength5To8++;
            else if (length <= 16) _cpuChipFetchRunLength9To16++;
            else if (length <= 32) _cpuChipFetchRunLength17To32++;
            else _cpuChipFetchRunLength33To64++;
        }

        private static long MinFutureBoundary(long best, long candidate, long currentCycle)
            => candidate <= currentCycle ? currentCycle : Math.Min(best, candidate);
    }
}
