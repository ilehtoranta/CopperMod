/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        // Reuses the prepared display-owner segment while a CPU grant search walks
        // forward within the same raster line. The cursor self-invalidates when a
        // Copper/custom-register wake changes the live display generation.
        private BlitterFixedSlotImageCursor _cpuGrantFixedSlotImageCursor;
        // A positive dynamic-DMA rejection is stable for the remainder of the line.
        // Cache only that conservative result; a negative result must be rechecked.
        private long _cpuGrantDynamicDmaRejectSegmentEnd = -1;

        private const int DeferredCpuBusBatchMinimumHorizonCycles = 16;
        // The CPU wait-slot executor remains shadow-only until its display and
        // scheduler state transitions are proven equivalent to the reference drain.
        private const bool DeferredCpuWaitFastPathEnabled = true;

        internal void ArmDeferredCpuBatchExitForTest(long cycle)
            => _deferredCpuBatchExitChipAccessCycle = Math.Max(0, cycle);

        internal void SetCpuWaitSlotContendedCleanThroughForTest(long cycle)
            => _hardwareScheduler.SetCpuWaitSlotContendedCleanThroughForTest(cycle);

        void IM68kDeferredCpuInstructionTiming.BeginDeferredCpuInstructionTiming(long cycle)
        {
            if (_deferredCpuBusBatchActive)
            {
                _deferredCpuInstructionTimingActive = !_captureBusAccesses;
                if (_deferredCpuDataAccessCount == 0)
                {
                    _deferredCpuDataReplayCycle = cycle;
                }

                return;
            }

            _deferredCpuInstructionTimingActive = !_captureBusAccesses;
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;
            _deferredCpuDataReplayCycle = cycle;
        }

        void IM68kDeferredCpuInstructionTiming.FlushDeferredCpuInstructionTiming(ref long cycle)
        {
            FlushDeferredCpuDataTiming(ref cycle);
            if (_deferredCpuBusBatchActive && !_endingDeferredCpuBusBatch)
            {
                EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return;
            }

            _deferredCpuInstructionTimingActive = false;
        }

        bool IM68kDeferredCpuInstructionTiming.IsDeferredCpuBusBatchActive => _deferredCpuBusBatchActive;

        bool IM68kDeferredCpuInstructionTiming.IsInternalNoBusWindowEnabled =>
            _deferredCpuInternalNoBusWindowEnabled && !_captureBusAccesses;

        bool IM68kDeferredCpuInstructionTiming.IsDeferredCpuBusBatchEligibleInstructionFetchWindow(
            in M68kInstructionFetchWindow window)
            => IsDeferredCpuBusBatchEligibleTarget((AmigaBusAccessTarget)window.BusTag);

        bool IM68kDeferredCpuInstructionTiming.TryBeginDeferredCpuBusBatch(
            M68kCpuState state,
            long currentCycle,
            long? targetCycle,
            out long batchTargetCycle,
            out M68kTraceBatchWakeSource wakeSource)
        {
            batchTargetCycle = currentCycle;
            wakeSource = M68kTraceBatchWakeSource.TargetCycle;
            if (_captureBusAccesses ||
                _deferredCpuBusBatchActive)
            {
                return false;
            }

            if (!_deferredCpuBusBatchEnabled)
            {
                if (_agnusBusExecutor.CpuVisibilityShadowEnabled)
                {
                    currentCycle = Math.Max(0, currentCycle);
                    var diagnosticTarget = targetCycle.HasValue
                        ? Math.Max(currentCycle + 1, targetCycle.Value)
                        : currentCycle + DeferredCpuBusBatchDefaultCycleWindow;
                    var diagnosticInterruptMask = (state.StatusRegister >> 8) & 0x07;
                    _ = GetNextCpuBatchWakeCandidateCycle(
                        currentCycle,
                        diagnosticTarget,
                        diagnosticInterruptMask,
                        out _,
                        out _);
                }

                return false;
            }

            _deferredCpuBusBatchAttempts++;
            currentCycle = Math.Max(0, currentCycle);
            var requestedTarget = targetCycle.HasValue
                ? Math.Max(currentCycle + 1, targetCycle.Value)
                : currentCycle + DeferredCpuBusBatchDefaultCycleWindow;
            var interruptMask = (state.StatusRegister >> 8) & 0x07;
            var horizon = _agnusBusExecutor.GetNextCpuVisibilityHorizon(
                currentCycle,
                requestedTarget,
                interruptMask);
            batchTargetCycle = horizon.Cycle;
            wakeSource = AgnusBusExecutor.MapLegacyReason(horizon.Reason);
            var diskWakeReason = horizon.DiskReason;
            if (batchTargetCycle - currentCycle < DeferredCpuBusBatchMinimumHorizonCycles)
            {
                return false;
            }

            _deferredCpuBusBatchActive = true;
            _deferredCpuBusBatchHasTargetCycle = targetCycle.HasValue;
            _deferredCpuBusBatchTargetCycle = batchTargetCycle;
            _deferredCpuInstructionTimingActive = !_captureBusAccesses;
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;
            _deferredCpuDataReplayCycle = currentCycle;
            _deferredCpuBusBatchExecutionStarted = false;
            _deferredCpuBusBatchPendingWakeSource = wakeSource;
            _deferredCpuBusBatchPendingDiskWakeReason = diskWakeReason;

            return true;
        }

        void IM68kDeferredCpuInstructionTiming.BeginDeferredCpuBusBatchExecution()
        {
            if (!_deferredCpuBusBatchExecutionStarted)
            {
                _deferredCpuBusBatchExecutionStarted = true;
                _deferredCpuBusBatchUsed++;
                RecordDeferredCpuBusBatchWakeSource(_deferredCpuBusBatchPendingWakeSource);
                if (_deferredCpuBusBatchPendingWakeSource == M68kTraceBatchWakeSource.Disk)
                {
                    RecordDeferredCpuBusBatchDiskWakeReason(_deferredCpuBusBatchPendingDiskWakeReason);
                }
            }
        }

        void IM68kDeferredCpuInstructionTiming.CompleteDeferredCpuBusBatchExecution(
            int instructionCount,
            int skippedInstructionFlushCount)
        {
            _deferredCpuBusBatchInstructions += instructionCount;
            _deferredCpuBusBatchSkippedInstructionFlushes += skippedInstructionFlushCount;
        }

        void IM68kDeferredCpuInstructionTiming.EndDeferredCpuBusBatch(ref long cycle, M68kDeferredCpuBusBatchExitReason reason)
            => EndDeferredCpuBusBatchCore(ref cycle, reason);

        bool IM68kDeferredCpuInstructionTiming.TryAdvanceInternalNoBusWindow(
            M68kCpuState state,
            long currentCycle,
            int cycles,
            M68kInternalNoBusWindowKind kind)
        {
            if (!_deferredCpuInternalNoBusWindowEnabled ||
                _captureBusAccesses ||
                cycles <= 0)
            {
                return false;
            }

            _deferredCpuInternalNoBusWindowAttempts++;
            currentCycle = Math.Max(0, currentCycle);
            RecordDeferredCpuInternalNoBusWindow(
                cycles,
                cycles,
                M68kTraceBatchWakeSource.TargetCycle,
                wakeLimited: false,
                kind);
            return true;
        }

        private void RecordDeferredCpuInternalNoBusWindow(
            int cycles,
            long advancedCycles,
            M68kTraceBatchWakeSource wakeSource,
            bool wakeLimited,
            M68kInternalNoBusWindowKind kind)
        {
            _deferredCpuInternalNoBusWindowUsed++;
            _deferredCpuInternalNoBusWindowTotalCycles += cycles;
            _deferredCpuInternalNoBusWindowAdvancedCycles += advancedCycles;
            switch (kind)
            {
                case M68kInternalNoBusWindowKind.Multiply:
                    _deferredCpuInternalNoBusWindowMultiply++;
                    break;
                case M68kInternalNoBusWindowKind.Divide:
                    _deferredCpuInternalNoBusWindowDivide++;
                    break;
            }

            if (wakeLimited)
            {
                RecordDeferredCpuInternalNoBusWindowWakeSource(wakeSource);
            }
            else
            {
                _deferredCpuInternalNoBusWindowWakeTargetCycle++;
            }
        }

        private static bool IsDeferredCpuBusBatchEligibleTarget(AmigaBusAccessTarget target)
            => target == AmigaBusAccessTarget.Rom ||
                target is AmigaBusAccessTarget.RealFastRam or AmigaBusAccessTarget.RtgVram ||
                target == AmigaBusAccessTarget.ExpansionRam;

        private bool CanKeepDeferredCpuBusBatchForAccess(
            AmigaBusAccessTarget target,
            AmigaBusAccessKind kind,
            bool isWrite,
            long cycle)
        {
            if (!_deferredCpuBusBatchActive)
            {
                return false;
            }

            if (isWrite)
            {
                return target is AmigaBusAccessTarget.RealFastRam or AmigaBusAccessTarget.RtgVram ||
                    target == AmigaBusAccessTarget.ExpansionRam;
            }

            if (_deferredCpuChipReadSegmentsEnabled &&
                target == AmigaBusAccessTarget.ChipRam &&
                kind == AmigaBusAccessKind.CpuDataRead &&
                !_agnusBusExecutor.MayWriteChipRamBefore(
                    Math.Max(cycle, _deferredCpuBusBatchTargetCycle)))
            {
                return true;
            }

            return target == AmigaBusAccessTarget.Rom ||
                target is AmigaBusAccessTarget.RealFastRam or AmigaBusAccessTarget.RtgVram ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                (kind == AmigaBusAccessKind.CpuInstructionFetch &&
                    target == AmigaBusAccessTarget.HostTrap);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryJournalDeferredCpuChipWordWrite(
            uint address,
            ushort value,
            ref long cycle)
        {
            if (!_deferredCpuBusBatchActive ||
                _agnusBusExecutor.IsFlushingCpuEventJournal)
            {
                return false;
            }

            if (_agnusBusExecutor.HasPendingCpuWriteOverlap(address, 2))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            if (!_agnusBusExecutor.TryEnqueueCpuChipWordWrite(
                address,
                value,
                cycle,
                CpuJournalInstructionPhase.Operand,
                CpuJournalDependencyFlags.None,
                out _))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            cycle += AgnusChipSlotScheduler.SlotCycles;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryJournalDeferredCpuChipLongWrite(
            uint address,
            uint value,
            ref long cycle)
        {
            if (!_deferredCpuBusBatchActive ||
                _agnusBusExecutor.IsFlushingCpuEventJournal)
            {
                return false;
            }

            if (_agnusBusExecutor.HasPendingCpuWriteOverlap(address, 4))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            if (!_agnusBusExecutor.TryEnqueueCpuChipLongWrite(
                address,
                value,
                cycle,
                CpuJournalInstructionPhase.Operand,
                out _,
                out _))
            {
                EndDeferredCpuBusBatchCore(
                    ref cycle,
                    M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                return false;
            }

            cycle += 2 * AgnusChipSlotScheduler.SlotCycles;
            return true;
        }

        private void RecordDeferredCpuBusBatchWakeSource(M68kTraceBatchWakeSource wakeSource)
        {
            switch (wakeSource)
            {
                case M68kTraceBatchWakeSource.PendingInterrupt:
                    _deferredCpuBusBatchWakePendingInterrupt++;
                    break;
                case M68kTraceBatchWakeSource.VerticalBlank:
                    _deferredCpuBusBatchWakeVerticalBlank++;
                    break;
                case M68kTraceBatchWakeSource.HorizontalSyncTod:
                    _deferredCpuBusBatchWakeHorizontalSyncTod++;
                    break;
                case M68kTraceBatchWakeSource.CiaTimer:
                    _deferredCpuBusBatchWakeCiaTimer++;
                    break;
                case M68kTraceBatchWakeSource.Disk:
                    _deferredCpuBusBatchWakeDisk++;
                    break;
                case M68kTraceBatchWakeSource.Paula:
                    _deferredCpuBusBatchWakePaula++;
                    break;
                case M68kTraceBatchWakeSource.Copper:
                    _deferredCpuBusBatchWakeCopper++;
                    break;
                case M68kTraceBatchWakeSource.Blitter:
                    _deferredCpuBusBatchWakeBlitter++;
                    break;
                case M68kTraceBatchWakeSource.TargetCycle:
                case M68kTraceBatchWakeSource.Unknown:
                default:
                    _deferredCpuBusBatchWakeTargetCycle++;
                    break;
            }
        }

        private void RecordDeferredCpuBusBatchDiskWakeReason(AmigaDiskController.SchedulerWakeReason reason)
        {
            switch (reason)
            {
                case AmigaDiskController.SchedulerWakeReason.PendingDma:
                    _deferredCpuBusBatchDiskWakePendingDma++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.ActiveDmaProgress:
                    _deferredCpuBusBatchDiskWakeActiveDmaProgress++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.ActiveDmaCompletion:
                    _deferredCpuBusBatchDiskWakeActiveDmaCompletion++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.SyncCandidate:
                    _deferredCpuBusBatchDiskWakeSyncCandidate++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.IndexPulse:
                    _deferredCpuBusBatchDiskWakeIndexPulse++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.PassiveByteReady:
                    _deferredCpuBusBatchDiskWakePassiveByteReady++;
                    break;
                case AmigaDiskController.SchedulerWakeReason.None:
                default:
                    _deferredCpuBusBatchDiskWakeUnknown++;
                    break;
            }
        }

        private void RecordDeferredCpuInternalNoBusWindowWakeSource(M68kTraceBatchWakeSource wakeSource)
        {
            switch (wakeSource)
            {
                case M68kTraceBatchWakeSource.PendingInterrupt:
                    _deferredCpuInternalNoBusWindowWakePendingInterrupt++;
                    break;
                case M68kTraceBatchWakeSource.VerticalBlank:
                    _deferredCpuInternalNoBusWindowWakeVerticalBlank++;
                    break;
                case M68kTraceBatchWakeSource.HorizontalSyncTod:
                    _deferredCpuInternalNoBusWindowWakeHorizontalSyncTod++;
                    break;
                case M68kTraceBatchWakeSource.CiaTimer:
                    _deferredCpuInternalNoBusWindowWakeCiaTimer++;
                    break;
                case M68kTraceBatchWakeSource.Disk:
                    _deferredCpuInternalNoBusWindowWakeDisk++;
                    break;
                case M68kTraceBatchWakeSource.Paula:
                    _deferredCpuInternalNoBusWindowWakePaula++;
                    break;
                case M68kTraceBatchWakeSource.Copper:
                    _deferredCpuInternalNoBusWindowWakeCopper++;
                    break;
                case M68kTraceBatchWakeSource.Blitter:
                    _deferredCpuInternalNoBusWindowWakeBlitter++;
                    break;
                case M68kTraceBatchWakeSource.TargetCycle:
                case M68kTraceBatchWakeSource.Unknown:
                default:
                    _deferredCpuInternalNoBusWindowWakeTargetCycle++;
                    break;
            }
        }

        private void EndDeferredCpuBusBatchCore(ref long cycle, M68kDeferredCpuBusBatchExitReason reason)
        {
            if (!_deferredCpuBusBatchActive)
            {
                FlushDeferredCpuDataTiming(ref cycle);
                _agnusBusExecutor.FlushCpuEventJournal(ref cycle);
                _deferredCpuInstructionTimingActive = false;
                return;
            }

            _endingDeferredCpuBusBatch = true;
            try
            {
                if (_deferredCpuDataAccessCount != 0)
                {
                    FlushDeferredCpuDataTiming(ref cycle);
                }

                _agnusBusExecutor.FlushCpuEventJournal(ref cycle);
            }
            finally
            {
                _endingDeferredCpuBusBatch = false;
            }

            _deferredCpuBusBatchActive = false;
            _deferredCpuBusBatchHasTargetCycle = false;
            _deferredCpuBusBatchTargetCycle = 0;
            _deferredCpuInstructionTimingActive = false;
            var executionStarted = _deferredCpuBusBatchExecutionStarted;
            _deferredCpuBusBatchExecutionStarted = false;
            if (executionStarted)
            {
                _deferredCpuBusBatchFlushes++;
            }
            _deferredCpuBatchExitChipAccessCycle = reason == M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess
                ? Math.Max(0, cycle)
                : -1;
            if (!executionStarted)
            {
                return;
            }

            switch (reason)
            {
                case M68kDeferredCpuBusBatchExitReason.TargetCycle:
                    _deferredCpuBusBatchExitTargetCycle++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.MaxInstructions:
                    _deferredCpuBusBatchExitMaxInstructions++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.PcLeftFastWindow:
                    _deferredCpuBusBatchExitPcLeftFastWindow++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.Exception:
                    _deferredCpuBusBatchExitException++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.Unsupported:
                    _deferredCpuBusBatchExitUnsupported++;
                    break;
                case M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess:
                case M68kDeferredCpuBusBatchExitReason.HaltedOrStopped:
                case M68kDeferredCpuBusBatchExitReason.Completed:
                case M68kDeferredCpuBusBatchExitReason.None:
                default:
                    _deferredCpuBusBatchExitChipVisibleAccess++;
                    break;
            }
        }

        private void ResetDeferredCpuBusBatchState(bool resetCounters)
        {
            _deferredCpuInstructionTimingActive = false;
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;
            _deferredCpuDataReplayCycle = 0;
            _deferredCpuBusBatchActive = false;
            _deferredCpuBusBatchHasTargetCycle = false;
            _deferredCpuBusBatchTargetCycle = 0;
            _endingDeferredCpuBusBatch = false;
            _deferredCpuBusBatchExecutionStarted = false;
            _deferredCpuBusBatchPendingWakeSource = M68kTraceBatchWakeSource.Unknown;
            _deferredCpuBusBatchPendingDiskWakeReason = AmigaDiskController.SchedulerWakeReason.None;
            _deferredCpuBatchExitChipAccessCycle = -1;
            if (!resetCounters)
            {
                return;
            }

            _deferredCpuBusBatchAttempts = 0;
            _deferredCpuBusBatchUsed = 0;
            _deferredCpuBusBatchInstructions = 0;
            _deferredCpuBusBatchSkippedInstructionFlushes = 0;
            _deferredCpuBusBatchFlushes = 0;
            _deferredCpuBusBatchExitTargetCycle = 0;
            _deferredCpuBusBatchExitMaxInstructions = 0;
            _deferredCpuBusBatchExitChipVisibleAccess = 0;
            _deferredCpuBusBatchExitPcLeftFastWindow = 0;
            _deferredCpuBusBatchExitException = 0;
            _deferredCpuBusBatchExitUnsupported = 0;
            _deferredCpuBusBatchVerificationMismatches = 0;
            _deferredCpuBusBatchFirstMismatch = string.Empty;
            _deferredCpuBusBatchWakeTargetCycle = 0;
            _deferredCpuBusBatchWakePendingInterrupt = 0;
            _deferredCpuBusBatchWakeVerticalBlank = 0;
            _deferredCpuBusBatchWakeHorizontalSyncTod = 0;
            _deferredCpuBusBatchWakeCiaTimer = 0;
            _deferredCpuBusBatchWakeDisk = 0;
            _deferredCpuBusBatchWakePaula = 0;
            _deferredCpuBusBatchWakeCopper = 0;
            _deferredCpuBusBatchWakeBlitter = 0;
            _deferredCpuBusBatchDiskWakePendingDma = 0;
            _deferredCpuBusBatchDiskWakeActiveDmaProgress = 0;
            _deferredCpuBusBatchDiskWakeActiveDmaCompletion = 0;
            _deferredCpuBusBatchDiskWakeSyncCandidate = 0;
            _deferredCpuBusBatchDiskWakeIndexPulse = 0;
            _deferredCpuBusBatchDiskWakePassiveByteReady = 0;
            _deferredCpuBusBatchDiskWakeUnknown = 0;
            _deferredCpuInternalNoBusWindowAttempts = 0;
            _deferredCpuInternalNoBusWindowUsed = 0;
            _deferredCpuInternalNoBusWindowTotalCycles = 0;
            _deferredCpuInternalNoBusWindowAdvancedCycles = 0;
            _deferredCpuInternalNoBusWindowMultiply = 0;
            _deferredCpuInternalNoBusWindowDivide = 0;
            _deferredCpuInternalNoBusWindowWakeTargetCycle = 0;
            _deferredCpuInternalNoBusWindowWakePendingInterrupt = 0;
            _deferredCpuInternalNoBusWindowWakeVerticalBlank = 0;
            _deferredCpuInternalNoBusWindowWakeHorizontalSyncTod = 0;
            _deferredCpuInternalNoBusWindowWakeCiaTimer = 0;
            _deferredCpuInternalNoBusWindowWakeDisk = 0;
            _deferredCpuInternalNoBusWindowWakePaula = 0;
            _deferredCpuInternalNoBusWindowWakeCopper = 0;
            _deferredCpuInternalNoBusWindowWakeBlitter = 0;
            _deferredCpuInternalNoBusWindowVerificationMismatches = 0;
            _deferredCpuInternalNoBusWindowFirstMismatch = string.Empty;
            ResetDeferredCpuWaitDiagnostics();
        }




        private bool TryDeferExactCpuExpansionDataTiming(
            AmigaBusAccessSize size,
            ref long cycle)
        {
            if (!_deferredCpuInstructionTimingActive ||
                _deferredCpuBusBatchHasTargetCycle)
            {
                return false;
            }

            if (_deferredCpuDataAccessCount >= MaxDeferredCpuDataAccesses)
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return false;
            }

            var index = _deferredCpuDataAccessCount;
            if (index == 0)
            {
                _deferredCpuDataReplayCycle = cycle;
            }

            if (size == AmigaBusAccessSize.Long)
            {
                _deferredCpuDataLongShapeBits |= 1UL << index;
            }

            _deferredCpuDataAccessCount = index + 1;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanDeferExactCpuCiaRead(int ciaRegister)
            => ciaRegister is 0 or 1 or 2 or 3 or 0x0E or 0x0F;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDeferExactCpuCiaDataTiming(ref long cycle)
        {
            if (!_deferredCpuInstructionTimingActive)
            {
                return false;
            }

            if (_deferredCpuDataAccessCount >= MaxDeferredCpuDataAccesses)
            {
                FlushDeferredCpuDataTiming(ref cycle);
                return false;
            }

            var index = _deferredCpuDataAccessCount;
            if (index == 0)
            {
                _deferredCpuDataReplayCycle = cycle;
            }

            _deferredCpuDataCiaShapeBits |= 1UL << index;
            _deferredCpuDataAccessCount = index + 1;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushDeferredCpuDataTiming(ref long cycle)
            => FlushDeferredCpuDataTiming(ref cycle, keepBatchActive: false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushDeferredCpuDataTimingForAccess(
            AmigaBusAccessTarget target,
            AmigaBusAccessKind kind,
            bool isWrite,
            ref long cycle)
        {
            FlushDeferredCpuDataTiming(
                ref cycle,
                CanKeepDeferredCpuBusBatchForAccess(target, kind, isWrite, cycle));
            AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
        }

        private void FlushDeferredCpuDataTiming(ref long cycle, bool keepBatchActive)
        {
            var count = _deferredCpuDataAccessCount;
            if (count == 0)
            {
                if (!keepBatchActive &&
                    _deferredCpuBusBatchActive &&
                    !_endingDeferredCpuBusBatch)
                {
                    EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                }

                return;
            }

            var longShapeBits = _deferredCpuDataLongShapeBits;
            var ciaShapeBits = _deferredCpuDataCiaShapeBits;
            var replayCycle = _deferredCpuDataReplayCycle;
            _deferredCpuDataAccessCount = 0;
            _deferredCpuDataLongShapeBits = 0;
            _deferredCpuDataCiaShapeBits = 0;

            for (var i = 0; i < count; i++)
            {
                if (((ciaShapeBits >> i) & 1UL) != 0)
                {
                    replayCycle = CiaPeripheralAccessTiming.AlignToCiaPeripheralAccessCycle(Math.Max(0, replayCycle));
                    continue;
                }

                var size = ((longShapeBits >> i) & 1UL) != 0
                    ? AmigaBusAccessSize.Long
                    : AmigaBusAccessSize.Word;
                CommitExactCpuExpansionDataTiming(
                    ExpansionRamBase,
                    size,
                    ref replayCycle,
                    isWrite: false,
                    AmigaBusAccessKind.CpuDataRead,
                    OcsLiveDmaScratchCpuWrite.None,
                    out _,
                    out _);
            }

            if (cycle < replayCycle)
            {
                cycle = replayCycle;
            }

            _deferredCpuDataReplayCycle = cycle;
            if (!keepBatchActive &&
                _deferredCpuBusBatchActive &&
                !_endingDeferredCpuBusBatch)
            {
                EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataRamTiming(
            in AmigaExactCpuDataRamRegion region,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle)
            => CommitExactCpuDataRamTiming(
                in region,
                size,
                ref cycle,
                isWrite,
                kind,
                OcsLiveDmaScratchCpuWrite.None,
                out grantedCycle,
                out secondWordCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataRamTiming(
            in AmigaExactCpuDataRamRegion region,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ExpansionRam)
            {
                CommitExactCpuExpansionDataTiming(
                    region.Address,
                    size,
                ref cycle,
                isWrite,
                kind,
                OcsLiveDmaScratchCpuWrite.None,
                out grantedCycle,
                out secondWordCycle);
                return;
            }

            CommitExactCpuDataTiming(
                region.Target,
                region.Address,
                size,
                ref cycle,
                isWrite,
                kind,
                scratchWrite,
                out grantedCycle,
                out secondWordCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataTiming(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            out long grantedCycle,
            out long secondWordCycle)
            => CommitExactCpuDataTiming(
                target,
                address,
                size,
                ref cycle,
                isWrite,
                kind,
                OcsLiveDmaScratchCpuWrite.None,
                out grantedCycle,
                out secondWordCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuDataTiming(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle)
        {
            if (!CanKeepDeferredCpuBusBatchForAccess(target, kind, isWrite, cycle))
            {
                var deferInstructionFetchWindowExit =
                    _deferredCpuBusBatchActive &&
                    _deferredCpuDataAccessCount == 0 &&
                    !isWrite &&
                    kind == AmigaBusAccessKind.CpuInstructionFetch;
                if (!deferInstructionFetchWindowExit)
                {
                    FlushDeferredCpuDataTiming(ref cycle);
                }

                if (_deferredCpuBusBatchActive && !_endingDeferredCpuBusBatch)
                {
                    if (!deferInstructionFetchWindowExit)
                    {
                        EndDeferredCpuBusBatchCore(ref cycle, M68kDeferredCpuBusBatchExitReason.ChipVisibleAccess);
                    }
                }
            }

            var causalGrantAttempts = 0;
        RetryCausalCpuGrant:
            if (++causalGrantAttempts > 1024)
            {
                throw new InvalidOperationException(
                    $"CPU Chip RAM arbitration did not advance beyond bus horizon {ExecutedChipBusHorizon} " +
                    $"for {kind} at 0x{address:X8} from cycle {cycle}.");
            }

            var requestedCycle = cycle;
            if (TryCommitExactCpuChipDataAccessFast(
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                kind,
                scratchWrite,
                out grantedCycle,
                out secondWordCycle,
                out var completedCycle,
                out var chronologicalGrantCommitted,
                synchronizeDmaAfterGrant: true))
            {
                if (!chronologicalGrantCommitted &&
                    target == AmigaBusAccessTarget.ChipRam &&
                    grantedCycle <= ExecutedChipBusHorizon)
                {
                    AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
                    goto RetryCausalCpuGrant;
                }

                cycle = completedCycle;
                return;
            }

            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    target,
                    address,
                    size,
                    cycle,
                    kind,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                out var fastCompletedCycle);
                AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite);
                if (target == AmigaBusAccessTarget.ChipRam &&
                    grantedCycle <= ExecutedChipBusHorizon)
                {
                    AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
                    goto RetryCausalCpuGrant;
                }

                cycle = fastCompletedCycle;
                return;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                kind,
                target,
                address,
                size,
                cycle,
                isWrite);
            AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, access.GrantedCycle, isWrite);
            if (target == AmigaBusAccessTarget.ChipRam &&
                access.GrantedCycle <= ExecutedChipBusHorizon)
            {
                AdvanceCpuAccessToCausalBusHorizon(target, ref cycle);
                goto RetryCausalCpuGrant;
            }

            cycle = access.CompletedCycle;
            grantedCycle = access.GrantedCycle;
            secondWordCycle = GetSecondWordCycle(access);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitExactCpuExpansionDataTiming(
            uint address,
            AmigaBusAccessSize size,
            ref long cycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle)
        {
            var requestedCycle = cycle;
            if (TryCommitExactCpuChipDataAccessFast(
                AmigaBusAccessTarget.ExpansionRam,
                address,
                size,
                requestedCycle,
                isWrite,
                kind,
                scratchWrite,
                out grantedCycle,
                out secondWordCycle,
                out var completedCycle,
                out _,
                synchronizeDmaAfterGrant: false))
            {
                cycle = completedCycle;
                return;
            }

            if (_useFastZeroWaitAccesses)
            {
                GrantFastCpuAccessCycles(
                    AmigaBusAccessTarget.ExpansionRam,
                    address,
                    size,
                    cycle,
                    kind,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out var fastCompletedCycle);
                cycle = fastCompletedCycle;
                return;
            }

            var access = Arbitrate(
                AmigaBusRequester.Cpu,
                kind,
                AmigaBusAccessTarget.ExpansionRam,
                address,
                size,
                cycle,
                isWrite);
            cycle = access.CompletedCycle;
            grantedCycle = access.GrantedCycle;
            secondWordCycle = GetSecondWordCycle(access);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private bool TryCommitExactCpuChipDataAccessFast(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            AmigaBusAccessKind kind,
            OcsLiveDmaScratchCpuWrite scratchWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle,
            out bool chronologicalGrantCommitted,
            bool synchronizeDmaAfterGrant)
        {
            grantedCycle = 0;
            secondWordCycle = 0;
            completedCycle = 0;
            chronologicalGrantCommitted = false;

            if (!_exactCpuChipSlotFastPathEnabled ||
                !ShouldUseChipSlotScheduler(target))
            {
                return false;
            }

            var grantRequestCycle = requestedCycle;
            var slotContendedClean = _hardwareScheduler.IsSlotContendedCleanThrough(grantRequestCycle);
            var scratchAudit = default(DeferredCpuWaitScratchAudit);
            var cpuGrantCommitted = false;
            var deferredPreparationUsed = false;
            var verifyDeferredDmaReadOwnership = false;
            var predictedDeferredDmaReadGrant = 0L;
            var predictedDeferredDmaReadCompletion = 0L;
            var predictedDeferredDmaReadTimeline = default(CpuWaitFixedSlotTimelineSignature);
            if (!slotContendedClean &&
                ShouldAttemptDeferredCpuWaitWindowFastPath(grantRequestCycle))
            {
                _deferredCpuBatchExitChipAccessCycle = -1;
                _deferredCpuWaitWindowFastPathAttempts++;
                if (_captureBusAccesses ||
                    size == AmigaBusAccessSize.Long ||
                    target is not (AmigaBusAccessTarget.ChipRam or
                        AmigaBusAccessTarget.ExpansionRam or
                        AmigaBusAccessTarget.RealTimeClock))
                {
                    _deferredCpuWaitWindowFastPathRejectedUnsupported++;
                    if (Display.HasLiveDisplayWork())
                    {
                        RecordProductionCpuWaitFixedSlotImageAttempt();
                        RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.Unsupported);
                    }
                }
                else if (HasUnsupportedCpuWaitSlotWorkThrough(
                    grantRequestCycle + LineCycles))
                {
                    _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    if (Display.HasLiveDisplayWork())
                    {
                        RecordProductionCpuWaitFixedSlotImageAttempt();
                        RecordProductionCpuWaitFixedSlotImageFallback(CpuWaitFixedImageProductionFallback.DynamicDma);
                    }
                    RecordDeferredCpuWaitDynamicRejectShadow(
                        kind,
                        target,
                        address,
                        size,
                        isWrite,
                        requestedCycle);
                }
                else if (Blitter.Busy)
                {
                    _hardwareScheduler.RecordDeferredCpuWaitBlitterOverlap(
                        Blitter.CanUseCpuWaitAreaMicroOps,
                        Blitter.CpuStallActive);
                    if (ShouldRunDeferredCpuWaitSlotShadowAudit)
                    {
                        BeginDeferredCpuWaitScratchAudit(
                            kind,
                            target,
                            address,
                            size,
                            requestedCycle,
                            grantRequestCycle,
                            isWrite,
                            scratchWrite,
                            ref scratchAudit);
                    }

                    var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                        kind,
                        target,
                        address,
                        size,
                        grantRequestCycle,
                        isWrite,
                        out grantedCycle,
                        out completedCycle);
                    if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                    {
                        secondWordCycle = grantedCycle;
                        cpuGrantCommitted = true;
                        deferredPreparationUsed = true;
                    }
                    else if (advanceResult == CpuWaitGrantAdvanceResult.ReferenceContinuation)
                    {
                        _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    }
                    else
                    {
                        _deferredCpuWaitWindowFastPathRejectedUnstable++;
                    }
                }
                else if (Display.HasLiveDisplayWork())
                {
                    var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                        kind,
                        target,
                        address,
                        size,
                        grantRequestCycle,
                        isWrite,
                        out grantedCycle,
                        out completedCycle);
                    if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                    {
                        secondWordCycle = grantedCycle;
                        cpuGrantCommitted = true;
                        deferredPreparationUsed = true;
                    }
                    else if (advanceResult == CpuWaitGrantAdvanceResult.ReferenceContinuation)
                    {
                        _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    }
                    else
                    {
                        _deferredCpuWaitWindowFastPathRejectedUnstable++;
                    }
                }
                else
                {
                    var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                        kind,
                        target,
                        address,
                        size,
                        grantRequestCycle,
                        isWrite,
                        out grantedCycle,
                        out completedCycle);
                    if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                    {
                        secondWordCycle = grantedCycle;
                        cpuGrantCommitted = true;
                        deferredPreparationUsed = true;
                    }
                    else if (advanceResult == CpuWaitGrantAdvanceResult.ReferenceContinuation)
                    {
                        _deferredCpuWaitWindowFastPathRejectedDynamicDma++;
                    }
                    else
                    {
                        _deferredCpuWaitWindowFastPathRejectedUnstable++;
                    }
                }
            }

            if (!slotContendedClean &&
                !cpuGrantCommitted &&
                _deferredDmaReadsVerifyEnabled &&
                !isWrite)
            {
                verifyDeferredDmaReadOwnership = _hardwareScheduler.TryPredictDeferredReadOwnership(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    out predictedDeferredDmaReadGrant,
                    out predictedDeferredDmaReadCompletion,
                    out predictedDeferredDmaReadTimeline);
            }

            if (!slotContendedClean && !cpuGrantCommitted)
            {
                if (ShouldRunDeferredCpuWaitSlotShadowAudit && !scratchAudit.BlitterAttempted)
                {
                    BeginDeferredCpuWaitScratchAudit(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        grantRequestCycle,
                        isWrite,
                        scratchWrite,
                        ref scratchAudit);
                }

                _hardwareScheduler.DrainForCpuAccess(target, address, grantRequestCycle, isWrite, size);
                AdvanceCpuAccessToCausalBusHorizon(target, ref grantRequestCycle);
            }

            // Slot ownership must be resolved by executing every requester in
            // order.  This is required even when the display is currently
            // quiet: Paula, disk, or the blitter may own an intervening slot.
            // Predict/prepare loops can move the eventual CPU grant behind a
            // value that has already been sampled.
            if (!cpuGrantCommitted &&
                size != AmigaBusAccessSize.Long)
            {
                var advanceResult = _hardwareScheduler.AdvanceUntilCpuGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle);
                if (advanceResult == CpuWaitGrantAdvanceResult.Granted)
                {
                    secondWordCycle = grantedCycle;
                    cpuGrantCommitted = true;
                }
            }

            if (!cpuGrantCommitted && Blitter.Busy)
            {
                grantRequestCycle = _hardwareScheduler.ExecuteThroughBlitterCpuStall(grantRequestCycle);
                AdvanceCpuAccessToCausalBusHorizon(target, ref grantRequestCycle);
            }

            grantRequestCycle = Math.Max(0, grantRequestCycle);

            if (!cpuGrantCommitted &&
                (size != AmigaBusAccessSize.Long ||
                    !isWrite ||
                    target is not (AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.ExpansionRam)))
            {
                PrepareLiveDisplayBeforeCpuHrmGrantUntilStable(target, address, size, isWrite, kind, grantRequestCycle);
            }

            if (!cpuGrantCommitted)
            {
                GrantExactCpuDataSlots(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }

            RecordDeferredCpuWaitWindow(
                kind,
                target,
                size,
                isWrite,
                requestedCycle,
                grantedCycle);

            if (CopperQuiescentShadowPredictionEnabled)
            {
                _hardwareScheduler.RecordCopperQuiescentCpuSlotPrediction(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    grantedCycle,
                    completedCycle,
                    isWrite);
            }

            if (scratchAudit.HasSupportedScratch)
            {
                CompleteDeferredCpuWaitScratchAudit(
                    in scratchAudit,
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    grantRequestCycle,
                    isWrite,
                    grantedCycle,
                    secondWordCycle,
                    completedCycle,
                    _hrmSlotEngine.CaptureTimelineSignature(grantRequestCycle, completedCycle));
            }

            if (grantedCycle > requestedCycle)
            {
                Agnus.RecordCpuChipWaitCycles(grantedCycle - requestedCycle);
            }

            if (synchronizeDmaAfterGrant)
            {
                if (target is not (AmigaBusAccessTarget.ChipRam or
                        AmigaBusAccessTarget.ExpansionRam or
                        AmigaBusAccessTarget.RealTimeClock) ||
                    !_hardwareScheduler.TryCatchUpPreparedCpuGrant(requestedCycle, grantedCycle))
                {
                    AdvanceDmaAfterCpuGrantIfNeeded(target, address, requestedCycle, grantedCycle, isWrite);
                }
            }

            if (deferredPreparationUsed)
            {
                RecordDeferredCpuWaitFastPathUse(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    grantRequestCycle,
                    isWrite,
                    grantedCycle,
                    secondWordCycle,
                    completedCycle);
            }

            if (verifyDeferredDmaReadOwnership)
            {
                VerifyDeferredDmaReadOwnership(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    grantedCycle,
                    completedCycle,
                    predictedDeferredDmaReadGrant,
                    predictedDeferredDmaReadCompletion,
                    predictedDeferredDmaReadTimeline);
            }

            chronologicalGrantCommitted = cpuGrantCommitted;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private void GrantExactCpuDataSlots(
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
            if (size == AmigaBusAccessSize.Long && !isWrite)
            {
                GrantCpuDataLongReadSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
                return;
            }

            if (size == AmigaBusAccessSize.Long &&
                isWrite &&
                target is AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.ExpansionRam)
            {
                GrantCpuDataLongWriteSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
                return;
            }

            if (size == AmigaBusAccessSize.Long)
            {
                _agnusBusExecutor.GrantCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
                return;
            }

            _agnusBusExecutor.GrantCpuDataSingleSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle);
            secondWordCycle = grantedCycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldAttemptDeferredCpuWaitWindowFastPath(long requestedCycle)
        {
            if (!DeferredCpuWaitFastPathEnabled ||
                !_deferredCpuBusBatchEnabled ||
                _forceCpuWaitSlotReference ||
                _deferredCpuBatchExitChipAccessCycle < 0)
            {
                return false;
            }

            if (requestedCycle == _deferredCpuBatchExitChipAccessCycle)
            {
                return true;
            }

            if (requestedCycle > _deferredCpuBatchExitChipAccessCycle)
            {
                _deferredCpuBatchExitChipAccessCycle = -1;
            }

            return false;
        }

        internal void BeginPendingCpuSlotRequest(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
            => _agnusBusExecutor.BeginPendingCpuSlotRequest(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite);

        internal void ClearPendingCpuSlotRequest()
            => _agnusBusExecutor.ClearPendingCpuSlotRequest();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasDynamicCpuWaitSlotWorkThrough(long cycle)
            => Display.HasLiveDisplayWork() ||
                HasNonDisplayDynamicCpuWaitSlotWorkThrough(cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasNonDisplayDynamicCpuWaitSlotWorkThrough(long cycle)
            =>
                Blitter.Busy ||
                Disk.ActiveDma ||
                Disk.HasSlotDmaWakeSourceThrough(cycle) ||
                Paula.HasDmaWorkThrough(cycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasUnsupportedCpuWaitSlotWorkThrough(long cycle)
            =>
                (Blitter.Busy && !Blitter.CanUseCpuWaitAreaMicroOps) ||
                Disk.ActiveDma ||
                Disk.HasSlotDmaWakeSourceThrough(cycle) ||
                Paula.HasDmaWorkThrough(cycle);

        internal OcsCpuWaitLiveSlotResult AdvanceCpuWaitLiveSlot(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches,
            out bool completedSafeCopper)
            => Agnus.AdvanceCpuWaitLiveSlotTo(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches,
                out completedSafeCopper);

        internal OcsCpuWaitLiveSlotResult AdvanceOrderedDmaBeforeBlitterSlot(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches,
            out bool advancedPaula,
            out bool advancedDisk)
            => _hardwareScheduler.AdvanceOrderedDmaBeforeBlitterSlot(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches,
                out advancedPaula,
                out advancedDisk);

        internal bool RequiresCanonicalBlitterDisplayPreparation
            => LiveAgnusDmaEnabled && Display.HasLiveDisplayWork();

        internal OcsCpuWaitLiveSlotResult AdvanceBlitterFixedSlot(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches)
            => Agnus.AdvanceBlitterFixedSlotTo(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches);

        internal OcsCpuWaitLiveSlotResult AdvanceFixedDmaBeforeBlitterSlotInScope(
            long slotCycle,
            out int bitplaneFetches,
            out int spriteFetches)
            => _hardwareScheduler.AdvanceFixedDmaBeforeBlitterSlotInScope(
                slotCycle,
                out bitplaneFetches,
                out spriteFetches);

        internal bool TryPredictBlitterFixedSlotGrant(
            uint address,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long firstBlockedCycle,
            out CpuWaitFixedSlotOwner firstBlockedOwner)
        {
            if (!TryPredictBlitterFixedSlotGrantCandidate(
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out firstBlockedCycle,
                    out firstBlockedOwner))
            {
                return false;
            }

            return CanAdvanceBlitterFixedSlotScopeThrough(
                grantedCycle + AgnusChipSlotScheduler.SlotCycles);
        }

        internal bool TryPredictBlitterFixedSlotGrantCandidate(
            uint address,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long firstBlockedCycle,
            out CpuWaitFixedSlotOwner firstBlockedOwner)
        {
            var cursor = default(BlitterFixedSlotImageCursor);
            return TryPredictBlitterFixedSlotGrantCandidate(
                address,
                requestedCycle,
                isWrite,
                ref cursor,
                out grantedCycle,
                out firstBlockedCycle,
                out firstBlockedOwner);
        }

        internal bool TryPredictBlitterFixedSlotGrantCandidate(
            uint address,
            long requestedCycle,
            bool isWrite,
            ref BlitterFixedSlotImageCursor cursor,
            out long grantedCycle,
            out long firstBlockedCycle,
            out CpuWaitFixedSlotOwner firstBlockedOwner)
        {
            requestedCycle = Math.Max(0, requestedCycle);
            grantedCycle = 0;
            firstBlockedCycle = -1;
            firstBlockedOwner = CpuWaitFixedSlotOwner.Free;
            if (_hrmSlotEngine.PendingCpuSlotRequestActive)
            {
                return false;
            }

            var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
            var hasLiveDisplay = LiveAgnusDmaEnabled && Display.HasLiveDisplayWork();
            for (var attempt = 0; attempt < 512; attempt++, candidate += AgnusChipSlotScheduler.SlotCycles)
            {
                if (IsMandatoryRefreshSlot(candidate))
                {
                    if (firstBlockedCycle < 0)
                    {
                        firstBlockedCycle = candidate;
                        firstBlockedOwner = CpuWaitFixedSlotOwner.Refresh;
                    }

                    continue;
                }

                if (!_hrmSlotEngine.CanReserveBlitterDmaWordAtAlignedWithoutFixedOrPendingCpu(
                        address,
                        requestedCycle,
                        candidate,
                        isWrite))
                {
                    continue;
                }

                if (hasLiveDisplay && !Display.HasLiveDmaCapturedThrough(candidate))
                {
                    if (!Display.TryGetBlitterFixedDisplaySlotOwner(
                            candidate,
                            ref cursor,
                            out var owner,
                            out _))
                    {
                        return false;
                    }

                    if (owner != CpuWaitFixedSlotOwner.Free)
                    {
                        if (firstBlockedCycle < 0)
                        {
                            firstBlockedCycle = candidate;
                            firstBlockedOwner = owner;
                        }

                        continue;
                    }
                }

                grantedCycle = candidate;
                return true;
            }

            return false;
        }

        internal bool CanAdvanceBlitterFixedSlotScopeThrough(long targetCycle)
            => !HasBlitterDynamicDmaWorkThrough(targetCycle) &&
                CanAdvanceBlitterFixedDisplayScopeThrough(targetCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasBlitterDynamicDmaWorkThrough(long targetCycle)
            => Disk.ActiveDma ||
                Disk.HasSlotDmaWakeSourceThrough(targetCycle) ||
                Paula.HasDmaWorkThrough(targetCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool CanAdvanceBlitterFixedDisplayScopeThrough(long targetCycle)
            => !LiveAgnusDmaEnabled ||
                Display.GetNextBlitterFixedSlotBarrierCycle(targetCycle) > targetCycle;

        internal string DescribeBlitterFixedSlotPrediction(long cycle)
        {
            _hrmSlotEngine.TryGetCommittedSlotOwner(cycle, out var committedOwner);
            var hasFixedOwner = Display.TryGetCpuWaitFixedSlotOwner(
                cycle,
                out var fixedOwner,
                out var unsupported);
            return $"slot={cycle},hrm={committedOwner},fixed={(hasFixedOwner ? fixedOwner : CpuWaitFixedSlotOwner.Free)},unsupported={unsupported}";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SynchronizeHrmBlitterPriority()
            => _agnusBusExecutor.SetBlitterPriority((Paula.Dmacon & 0x0400) != 0);

        internal bool TryGrantPendingCpuSingleSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            out long completedCycle)
            => _agnusBusExecutor.TryGrantCpuDataSingleExactSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                slotCycle,
                isWrite,
                allowNiceBlitterSteal: true,
                out completedCycle);

        internal void CaptureCpuWaitFixedImageDisplayDma(long grantCycle)
            => Display.CaptureLiveDisplayDmaBeforeHrmGrant(grantCycle);

        internal bool TryPredictCpuWaitFixedSlotGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotImageUnsupported unsupported)
            => TryPredictCpuWaitFixedSlotGrantCore(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle,
                out unsupported,
                out _,
                captureTimeline: false);

        internal bool TryPredictCpuWaitFixedSlotGrant(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotImageUnsupported unsupported,
            out CpuWaitFixedSlotTimelineSignature timeline)
            => TryPredictCpuWaitFixedSlotGrantCore(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle,
                out unsupported,
                out timeline,
                captureTimeline: true);

        private bool TryPredictCpuWaitFixedSlotGrantCore(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle,
            out CpuWaitFixedSlotImageUnsupported unsupported,
            out CpuWaitFixedSlotTimelineSignature timeline,
            bool captureTimeline)
        {
            grantedCycle = 0;
            completedCycle = 0;
            unsupported = CpuWaitFixedSlotImageUnsupported.None;
            timeline = default;
            if (size == AmigaBusAccessSize.Long ||
                target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock))
            {
                unsupported = CpuWaitFixedSlotImageUnsupported.Frame;
                return false;
            }

            var searchCycle = Math.Max(0, requestedCycle);
            for (var attempt = 0; attempt < 512; attempt++)
            {
                if (!_hrmSlotEngine.TryPredictCpuDataSingleSlotFrom(
                        kind,
                        target,
                        address,
                        size,
                        searchCycle,
                        requestedCycle,
                        isWrite,
                        out var candidate))
                {
                    return false;
                }

                CpuWaitFixedSlotOwner owner;
                if (IsMandatoryRefreshSlot(candidate))
                {
                    owner = CpuWaitFixedSlotOwner.Refresh;
                }
                else if (!Display.TryGetBlitterFixedDisplaySlotOwner(
                             candidate,
                             ref _cpuGrantFixedSlotImageCursor,
                             out owner,
                             out unsupported))
                {
                    return false;
                }

                if (owner == CpuWaitFixedSlotOwner.Free)
                {
                    grantedCycle = candidate;
                    completedCycle = candidate + AgnusChipSlotScheduler.SlotCycles;
                    return !captureTimeline || TryCaptureCpuWaitFixedSlotTimeline(
                        requestedCycle,
                        completedCycle,
                        grantedCycle,
                        predicted: true,
                        out timeline,
                        out unsupported);
                }

                searchCycle = candidate + AgnusChipSlotScheduler.SlotCycles;
            }

            grantedCycle = 0;
            completedCycle = 0;
            unsupported = CpuWaitFixedSlotImageUnsupported.Unstable;
            timeline = default;
            return false;
        }

        private bool TryCaptureCpuWaitFixedSlotTimeline(
            long requestedCycle,
            long completedCycle,
            long cpuGrantCycle,
            bool predicted,
            out CpuWaitFixedSlotTimelineSignature timeline,
            out CpuWaitFixedSlotImageUnsupported unsupported)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            var count = 0;
            var firstCycle = 0L;
            var lastCycle = 0L;
            var firstOwner = AgnusChipSlotOwner.Free;
            var lastOwner = AgnusChipSlotOwner.Free;
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle));
            var lastCompletedCycle = Math.Max(0, completedCycle - 1);
            var lastCandidate = lastCompletedCycle -
                (lastCompletedCycle % AgnusChipSlotScheduler.SlotCycles);
            for (; slotCycle <= lastCandidate; slotCycle += AgnusChipSlotScheduler.SlotCycles)
            {
                AgnusChipSlotOwner owner;
                if (slotCycle == cpuGrantCycle)
                {
                    owner = AgnusChipSlotOwner.Cpu;
                }
                else if (!_hrmSlotEngine.TryGetCommittedSlotOwner(slotCycle, out owner))
                {
                    if (!predicted)
                    {
                        owner = AgnusChipSlotOwner.Free;
                    }
                    else if (!Display.TryGetCpuWaitFixedSlotOwner(slotCycle, out var fixedOwner, out unsupported))
                    {
                        timeline = default;
                        return false;
                    }
                    else
                    {
                        owner = fixedOwner switch
                        {
                            CpuWaitFixedSlotOwner.Refresh => AgnusChipSlotOwner.Refresh,
                            CpuWaitFixedSlotOwner.BitplaneRead => AgnusChipSlotOwner.Bitplane,
                            CpuWaitFixedSlotOwner.SpriteRead => AgnusChipSlotOwner.Sprite,
                            _ => AgnusChipSlotOwner.Free
                        };
                    }
                }

                if (count == 0)
                {
                    firstCycle = slotCycle;
                    firstOwner = owner;
                }

                lastCycle = slotCycle;
                lastOwner = owner;
                count++;
                hash = (hash ^ unchecked((ulong)slotCycle)) * prime;
                hash = (hash ^ (ulong)owner) * prime;
            }

            unsupported = CpuWaitFixedSlotImageUnsupported.None;
            timeline = new CpuWaitFixedSlotTimelineSignature(
                count,
                hash,
                firstCycle,
                lastCycle,
                firstOwner,
                lastOwner);
            return true;
        }

        private bool TryPredictCpuGrant(
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
            if (size == AmigaBusAccessSize.Long)
            {
                return _hrmSlotEngine.TryPredictCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }

            secondWordCycle = 0;
            completedCycle = 0;
            if (!_hrmSlotEngine.TryPredictCpuDataSingleSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle))
            {
                return false;
            }

            secondWordCycle = grantedCycle;
            completedCycle = grantedCycle + AgnusChipSlotScheduler.SlotCycles;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private void PrepareLiveDisplayBeforeCpuHrmGrantUntilStable(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            AmigaBusAccessKind kind,
            long grantRequestCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                (size != AmigaBusAccessSize.Byte &&
                    size != AmigaBusAccessSize.Word &&
                    size != AmigaBusAccessSize.Long) ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            var prepareCycle = grantRequestCycle;
            var lastGrant = -1L;
            var lastSecondWord = -1L;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(target, size, isWrite, prepareCycle);
                if (!TryPredictCpuGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out var predictedGrant,
                    out var predictedSecondWord,
                    out _))
                {
                    return;
                }

                if (predictedGrant == lastGrant &&
                    predictedSecondWord == lastSecondWord)
                {
                    return;
                }

                var nextPrepareCycle = size == AmigaBusAccessSize.Long
                    ? predictedSecondWord
                    : predictedGrant;
                if (nextPrepareCycle <= prepareCycle)
                {
                    return;
                }

                lastGrant = predictedGrant;
                lastSecondWord = predictedSecondWord;
                prepareCycle = nextPrepareCycle;
            }
        }

        private bool TryPrepareLiveDisplayBeforeCpuHrmGrantFromFixedSlotImage(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            bool isWrite,
            AmigaBusAccessKind kind,
            long grantRequestCycle)
        {
            // Fixed display ownership can replace the prepare/predict fixed-point
            // loop only while no device with movable ownership can enter the line.
            // Those paths retain canonical gatling-order preparation.
            if (size is not (AmigaBusAccessSize.Byte or AmigaBusAccessSize.Word) ||
                target is not (AmigaBusAccessTarget.ChipRam or
                    AmigaBusAccessTarget.ExpansionRam or
                    AmigaBusAccessTarget.RealTimeClock) ||
                Display.HasLiveSpriteDmaWork() ||
                HasPreparedCpuGrantDynamicDmaBarrier(grantRequestCycle) ||
                !TryPredictCpuWaitFixedSlotGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out var predictedGrant,
                    out _,
                    out _))
            {
                return false;
            }

            Display.CaptureLiveDisplayDmaBeforeHrmGrant(predictedGrant);
            return TryPredictCpuGrant(
                    kind,
                    target,
                    address,
                    size,
                    grantRequestCycle,
                    isWrite,
                    out var committedGrant,
                    out _,
                    out _) &&
                committedGrant == predictedGrant;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasPreparedCpuGrantDynamicDmaBarrier(long grantRequestCycle)
        {
            if (grantRequestCycle <= _cpuGrantDynamicDmaRejectSegmentEnd)
            {
                return true;
            }

            if (!HasNonDisplayDynamicCpuWaitSlotWorkThrough(grantRequestCycle + LineCycles))
            {
                return false;
            }

            var line = Math.Max(0, grantRequestCycle) / LineCycles;
            _cpuGrantDynamicDmaRejectSegmentEnd = ((line + 1) * LineCycles) - 1;
            return true;
        }



        private void GrantCpuDataLongReadSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                requestedCycle,
                requestedCycle,
                isWrite: false,
                out grantedCycle,
                out var firstCompletedCycle);
            PrepareLiveDisplayBeforeCpuLongReadPhaseUntilStable(
                target,
                address,
                kind,
                requestedCycle,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles);
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles,
                requestedCycle,
                isWrite: false,
                out secondWordCycle,
                out completedCycle);
        }

        private void GrantCpuDataLongWriteSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            PrepareLiveDisplayBeforeCpuLongWritePhaseUntilStable(
                target,
                address,
                kind,
                requestedCycle,
                requestedCycle);
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                requestedCycle,
                requestedCycle,
                isWrite: true,
                out grantedCycle,
                out var firstCompletedCycle);
            PrepareLiveDisplayBeforeCpuLongWritePhaseUntilStable(
                target,
                address,
                kind,
                requestedCycle,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles);
            _agnusBusExecutor.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                firstCompletedCycle + AgnusChipSlotScheduler.SlotCycles,
                requestedCycle,
                isWrite: true,
                out secondWordCycle,
                out completedCycle);
        }



        private void PrepareLiveDisplayBeforeCpuLongReadPhaseUntilStable(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessKind kind,
            long requestedCycle,
            long phaseSearchCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            var prepareCycle = phaseSearchCycle;
            var lastGrant = -1L;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(
                    target,
                    AmigaBusAccessSize.Long,
                    isWrite: false,
                    prepareCycle);
                if (!_hrmSlotEngine.TryPredictCpuDataLongWordPhaseSlot(
                        kind,
                        target,
                        address,
                        prepareCycle,
                        requestedCycle,
                        isWrite: false,
                        out var predictedGrant))
                {
                    return;
                }

                if (predictedGrant == lastGrant ||
                    predictedGrant <= prepareCycle)
                {
                    return;
                }

                lastGrant = predictedGrant;
                prepareCycle = predictedGrant;
            }
        }

        private void PrepareLiveDisplayBeforeCpuLongWritePhaseUntilStable(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessKind kind,
            long requestedCycle,
            long phaseSearchCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            var prepareCycle = phaseSearchCycle;
            var lastGrant = -1L;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(
                    target,
                    AmigaBusAccessSize.Long,
                    isWrite: true,
                    prepareCycle);
                if (!_hrmSlotEngine.TryPredictCpuDataLongWordPhaseSlot(
                        kind,
                        target,
                        address,
                        prepareCycle,
                        requestedCycle,
                        isWrite: true,
                        out var predictedGrant))
                {
                    return;
                }

                if (predictedGrant == lastGrant ||
                    predictedGrant <= prepareCycle)
                {
                    return;
                }

                lastGrant = predictedGrant;
                prepareCycle = predictedGrant;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]


        private void PrepareLiveDisplayBeforeCpuHrmGrantIfNeeded(
            AmigaBusAccessTarget target,
            AmigaBusAccessSize size,
            bool isWrite,
            long grantCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                (size != AmigaBusAccessSize.Byte &&
                    size != AmigaBusAccessSize.Word &&
                    size != AmigaBusAccessSize.Long) ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            if (target == AmigaBusAccessTarget.CustomRegisters && !isWrite)
            {
                Display.PrepareLiveDisplaySlotsBeforeHrmGrant(grantCycle);
                return;
            }

            Display.CaptureLiveDisplayDmaBeforeHrmGrant(grantCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PrepareLiveDisplaySlotsBeforeDmaGrant(
            AmigaBusAccessSize size,
            long grantCycle)
        {
            if (!LiveAgnusDmaEnabled ||
                (size != AmigaBusAccessSize.Byte &&
                    size != AmigaBusAccessSize.Word &&
                    size != AmigaBusAccessSize.Long) ||
                !Display.HasLiveDisplayWork())
            {
                return;
            }

            Display.PrepareLiveDisplaySlotsBeforeHrmGrant(grantCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataByte(
            in AmigaExactCpuDataRamRegion region,
            byte value,
            long grantedCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                RememberChipDataBusByte(value, grantedCycle, wasDma: false);
                _chipRam.WriteByteAtOffset(region.Offset, value, grantedCycle);
                TouchCodePage(region.Address);
                return;
            }

            region.Memory[region.Offset] = value;
            TouchCodePage(region.Address);
            NotifyJitEligibleMemoryWritten(region.Address, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataWord(
            in AmigaExactCpuDataRamRegion region,
            ushort value,
            long grantedCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                RememberChipDataBusWord(value, grantedCycle, wasDma: false);
                _chipRam.WriteContiguousWordAtOffset(region.Offset, value, grantedCycle);
                TouchCodePage(region.Address);
                TouchCodePage(region.Address + 1);
                return;
            }

            region.Memory[region.Offset] = (byte)(value >> 8);
            region.Memory[region.Offset + 1] = (byte)value;
            TouchCodePage(region.Address);
            TouchCodePage(region.Address + 1);
            NotifyJitEligibleMemoryWritten(region.Address, 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteExactCpuDataLong(
            in AmigaExactCpuDataRamRegion region,
            uint value,
            long firstWordCycle,
            long secondWordCycle)
        {
            if (region.Target == AmigaBusAccessTarget.ChipRam)
            {
                RememberChipDataBusWord((ushort)(value >> 16), firstWordCycle, wasDma: false);
                RememberChipDataBusWord((ushort)value, secondWordCycle, wasDma: false);
                _chipRam.WriteContiguousLongAtOffset(region.Offset, value, firstWordCycle, secondWordCycle);
                TouchCodePage(region.Address);
                TouchCodePage(region.Address + 1);
                TouchCodePage(region.Address + 2);
                TouchCodePage(region.Address + 3);
                return;
            }

            region.Memory[region.Offset] = (byte)(value >> 24);
            region.Memory[region.Offset + 1] = (byte)(value >> 16);
            region.Memory[region.Offset + 2] = (byte)(value >> 8);
            region.Memory[region.Offset + 3] = (byte)value;
            TouchCodePage(region.Address);
            TouchCodePage(region.Address + 1);
            TouchCodePage(region.Address + 2);
            TouchCodePage(region.Address + 3);
            NotifyJitEligibleMemoryWritten(region.Address, 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGrantFastCpuAccessCycle(
            AmigaBusAccessTarget target,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle)
        {
            if (!isWrite &&
                (target == AmigaBusAccessTarget.ChipRam || target == AmigaBusAccessTarget.ExpansionRam) &&
                _hardwareScheduler.IsSlotContendedCleanThrough(requestedCycle) &&
                !Blitter.Busy &&
                (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target)))
            {
                grantedCycle = Math.Max(0, requestedCycle);
                return true;
            }

            grantedCycle = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GrantFastCpuAccessCycles(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            if (TryGrantFastCpuAccessCycle(target, requestedCycle, isWrite, out var fastCycle))
            {
                grantedCycle = fastCycle;
                completedCycle = fastCycle;
                secondWordCycle = fastCycle;
                return;
            }

            GrantCpuAccessSlowCycles(
                target,
                address,
                size,
                requestedCycle,
                kind,
                isWrite,
                out grantedCycle,
                out secondWordCycle,
                out completedCycle);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrantCpuAccessSlowCycles(
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            out long grantedCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            var originalRequestedCycle = requestedCycle;
            if (target == AmigaBusAccessTarget.ChipRam ||
                target == AmigaBusAccessTarget.ExpansionRam ||
                target == AmigaBusAccessTarget.RealTimeClock ||
                target == AmigaBusAccessTarget.CustomRegisters)
            {
                _hardwareScheduler.DrainForCpuAccess(target, address, requestedCycle, isWrite, size);
                if (Blitter.Busy)
                {
                    requestedCycle = _hardwareScheduler.ExecuteThroughBlitterCpuStall(requestedCycle);
                }

                AdvanceCpuAccessToCausalBusHorizon(target, ref requestedCycle);
            }

            requestedCycle = Math.Max(0, requestedCycle);
            if (target == AmigaBusAccessTarget.Cia)
            {
                grantedCycle = CiaPeripheralAccessTiming.AlignToCiaPeripheralAccessCycle(requestedCycle);
                completedCycle = grantedCycle;
                secondWordCycle = GetCpuSecondWordCycle(size, grantedCycle, completedCycle);
                return;
            }

            if (!_useChipSlotScheduler || !ShouldUseChipSlotScheduler(target))
            {
                grantedCycle = requestedCycle;
                completedCycle = requestedCycle;
                secondWordCycle = GetCpuSecondWordCycle(size, grantedCycle, completedCycle);
                return;
            }

            _agnusBusExecutor.SetBlitterPriority((Paula.Dmacon & 0x0400) != 0);
			PrepareLiveDisplayBeforeCpuHrmGrantUntilStable(
				target,
				address,
				size,
				isWrite,
				kind,
				requestedCycle);

            if (size == AmigaBusAccessSize.Long)
            {
                _agnusBusExecutor.GrantCpuDataLongSlots(
                    kind,
                    target,
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out secondWordCycle,
                    out completedCycle);
            }
            else
            {
                _agnusBusExecutor.GrantCpuDataSingleSlot(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out completedCycle);
                secondWordCycle = grantedCycle;
            }

            if (CopperQuiescentShadowPredictionEnabled)
            {
                _hardwareScheduler.RecordCopperQuiescentCpuSlotPrediction(
                    kind,
                    target,
                    address,
                    size,
                    requestedCycle,
                    grantedCycle,
                    completedCycle,
                    isWrite);
            }

            Agnus.RecordCpuChipWaitCycles(grantedCycle - originalRequestedCycle);
            RecordDeferredCpuWaitWindow(
                kind,
                target,
                size,
                isWrite,
                originalRequestedCycle,
                grantedCycle);
        }



    }
}
