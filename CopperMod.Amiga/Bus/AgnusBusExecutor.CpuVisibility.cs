/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.Bus
{
    [Flags]
    internal enum CpuVisibilityDirtySource : byte
    {
        None = 0,
        Raster = 1 << 0,
        All = byte.MaxValue
    }

    internal enum CpuVisibilityHorizonReason : byte
    {
        TargetCycle,
        PendingInterrupt,
        VerticalBlank,
        HorizontalSyncTod,
        CiaTimer,
        Disk,
        Paula,
        Copper,
        ControlEvent,
        Blitter,
        ExternalBoundary
    }

    internal readonly record struct CpuVisibilityHorizon(
        long Cycle,
        CpuVisibilityHorizonReason Reason,
        AmigaDiskController.SchedulerWakeReason DiskReason)
    {
        public bool ReachesTarget => Reason == CpuVisibilityHorizonReason.TargetCycle;
    }

    internal readonly record struct CpuVisibilityExpiredRootSnapshot(
        long Interrupt,
        long VerticalBlank,
        long HorizontalSyncTod,
        long CiaTimer,
        long Disk,
        long Paula,
        long Copper,
        long Control,
        long Blitter);

    internal readonly record struct CpuVisibilityExpiredCopperStateSnapshot(
        long Move,
        long Skip,
        long Start,
        long Restart,
        long Request,
        long Wait,
        long Fetch,
        long WaitImmediateAfterRefresh);

    internal sealed partial class AgnusBusExecutor
    {
        private readonly CpuVisibilityDeadlineAgenda _cpuVisibilityAgenda = new();
        private bool _cpuVisibilityAgendaInitialized;
        private CpuVisibilityDirtySource _cpuVisibilityDirtySources = CpuVisibilityDirtySource.All;
        private long _cpuVisibilityValidFromCycle = -1;
        private long _cpuVisibilityValidThroughCycle = -1;
        private ulong _cpuVisibilityCiaAVersion;
        private ulong _cpuVisibilityCiaBVersion;
        private ulong _cpuVisibilityPaulaVersion;
        private ulong _cpuVisibilityPaulaInterruptVersion;
        private ulong _cpuVisibilityDiskVersion;
        private ulong _cpuVisibilityDisplayVersion;
        private ulong _cpuVisibilityBlitterVersion;
        private long _cpuVisibilityVblankCycle = -1;
        private long _cpuVisibilityHsyncCycle = -1;
        private long _cpuVisibilityControlCycle = -1;
        private bool _cpuVisibilityHsyncTodActive;
        private long _cpuVisibilityQueries;
        private long _cpuVisibilityStoppedQueries;
        private long _cpuVisibilityRootReads;
        private long _cpuVisibilityLeafUpdates;
        private long _cpuVisibilitySourceRefreshes;
        private readonly long[] _cpuVisibilityExpiredRootCounts =
            new long[(int)CpuVisibilityDeadlineSource.Count];
        private readonly long[] _cpuVisibilityExpiredCopperStateCounts = new long[7];
        private long _cpuVisibilityExpiredCopperWaitImmediateRefreshes;

        public long CpuVisibilityQueries => _cpuVisibilityQueries;
        public long CpuVisibilityStoppedQueries => _cpuVisibilityStoppedQueries;
        public long CpuVisibilityRootReads => _cpuVisibilityRootReads;
        public long CpuVisibilityLeafUpdates => _cpuVisibilityLeafUpdates;
        public long CpuVisibilitySourceRefreshes => _cpuVisibilitySourceRefreshes;
        public CpuVisibilityExpiredRootSnapshot CpuVisibilityExpiredRoot =>
            new(
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.Interrupt],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.VerticalBlank],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.HorizontalSyncTod],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.CiaTimer],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.Disk],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.Paula],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.Copper],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.Control],
                _cpuVisibilityExpiredRootCounts[(int)CpuVisibilityDeadlineSource.Blitter]);
        public CpuVisibilityExpiredCopperStateSnapshot CpuVisibilityExpiredCopperState =>
            new(
                _cpuVisibilityExpiredCopperStateCounts[0],
                _cpuVisibilityExpiredCopperStateCounts[1],
                _cpuVisibilityExpiredCopperStateCounts[2],
                _cpuVisibilityExpiredCopperStateCounts[3],
                _cpuVisibilityExpiredCopperStateCounts[4],
                _cpuVisibilityExpiredCopperStateCounts[5],
                _cpuVisibilityExpiredCopperStateCounts[6],
                _cpuVisibilityExpiredCopperWaitImmediateRefreshes);
        internal (long Cycle, AmigaDiskController.SchedulerWakeReason DiskReason)
            GetCpuVisibilityDeadlineForTest(int interruptMask, CpuVisibilityDeadlineSource source)
            => _cpuVisibilityAgenda.GetLeaf(interruptMask, source);

        private void ResetCpuVisibilityAgenda()
        {
            _cpuVisibilityAgenda.Reset();
            _cpuVisibilityAgendaInitialized = false;
            _cpuVisibilityDirtySources = CpuVisibilityDirtySource.All;
            _cpuVisibilityValidFromCycle = -1;
            _cpuVisibilityValidThroughCycle = -1;
            _cpuVisibilityCiaAVersion = ulong.MaxValue;
            _cpuVisibilityCiaBVersion = ulong.MaxValue;
            _cpuVisibilityPaulaVersion = ulong.MaxValue;
            _cpuVisibilityPaulaInterruptVersion = ulong.MaxValue;
            _cpuVisibilityDiskVersion = ulong.MaxValue;
            _cpuVisibilityDisplayVersion = ulong.MaxValue;
            _cpuVisibilityBlitterVersion = ulong.MaxValue;
            _cpuVisibilityVblankCycle = -1;
            _cpuVisibilityHsyncCycle = -1;
            _cpuVisibilityControlCycle = -1;
            _cpuVisibilityHsyncTodActive = false;
            _cpuVisibilityQueries = 0;
            _cpuVisibilityStoppedQueries = 0;
            _cpuVisibilityRootReads = 0;
            _cpuVisibilityLeafUpdates = 0;
            _cpuVisibilitySourceRefreshes = 0;
            Array.Clear(_cpuVisibilityExpiredRootCounts);
            Array.Clear(_cpuVisibilityExpiredCopperStateCounts);
            _cpuVisibilityExpiredCopperWaitImmediateRefreshes = 0;
        }

        public void InvalidateCpuVisibilityAgenda(CpuVisibilityDirtySource sources = CpuVisibilityDirtySource.All)
            => _cpuVisibilityDirtySources |= sources;

        public void RecordStoppedCpuVisibilityQuery()
            => _cpuVisibilityStoppedQueries++;

        public CpuVisibilityHorizon GetNextStoppedCpuInterruptHorizon(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask)
        {
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return new CpuVisibilityHorizon(
                    currentCycle,
                    CpuVisibilityHorizonReason.TargetCycle,
                    AmigaDiskController.SchedulerWakeReason.None);
            }

            _cpuVisibilityQueries++;
            _cpuVisibilityStoppedQueries++;
            if (_bus.HasPendingCiaInterrupts)
            {
                return new CpuVisibilityHorizon(
                    currentCycle + 1,
                    CpuVisibilityHorizonReason.PendingInterrupt,
                    AmigaDiskController.SchedulerWakeReason.None);
            }

            RefreshCpuVisibilityAgenda(currentCycle, targetCycle);
            _cpuVisibilityRootReads++;
            var mask = cpuInterruptMask & 7;
            var bestCycle = targetCycle;
            var bestSource = CpuVisibilityDeadlineSource.Count;
            var bestDiskReason = AmigaDiskController.SchedulerWakeReason.None;
            var horizontalSyncCycle = _bus.NextHorizontalSyncCycle;
            if (horizontalSyncCycle > currentCycle &&
                horizontalSyncCycle < bestCycle)
            {
                bestCycle = horizontalSyncCycle;
                bestSource = CpuVisibilityDeadlineSource.HorizontalSyncTod;
            }

            for (var sourceIndex = 0;
                 sourceIndex < (int)CpuVisibilityDeadlineSource.Count;
                 sourceIndex++)
            {
                var source = (CpuVisibilityDeadlineSource)sourceIndex;
                if (source == CpuVisibilityDeadlineSource.Copper ||
                    source == CpuVisibilityDeadlineSource.Control ||
                    source == CpuVisibilityDeadlineSource.Blitter)
                {
                    continue;
                }

                var (cycle, diskReason) = _cpuVisibilityAgenda.GetLeaf(mask, source);
                if (cycle <= currentCycle)
                {
                    cycle = currentCycle + 1;
                }

                if (cycle < bestCycle)
                {
                    bestCycle = cycle;
                    bestSource = source;
                    bestDiskReason = diskReason;
                }
            }

            var blitterCompletion = _bus.Blitter.BusPipelineActive
                ? _bus.Blitter.GetPredictedCompletionCycle()
                : long.MaxValue;
            if (blitterCompletion <= currentCycle)
            {
                blitterCompletion = currentCycle + 1;
            }
            if (blitterCompletion < bestCycle)
            {
                bestCycle = blitterCompletion;
                bestSource = CpuVisibilityDeadlineSource.Blitter;
                bestDiskReason = AmigaDiskController.SchedulerWakeReason.None;
            }

            return bestSource == CpuVisibilityDeadlineSource.Count
                ? new CpuVisibilityHorizon(
                    targetCycle,
                    CpuVisibilityHorizonReason.TargetCycle,
                    AmigaDiskController.SchedulerWakeReason.None)
                : new CpuVisibilityHorizon(
                    bestCycle,
                    MapReason(bestSource),
                    bestDiskReason);
        }

        /// <summary>
        /// Returns the first event that can become visible to the CPU. This is
        /// a read-only Stage 1 diagnostic: it neither advances devices nor
        /// changes the production CPU batching path.
        /// </summary>
        public CpuVisibilityHorizon GetNextCpuVisibilityHorizon(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask = -1,
            long externalBoundaryCycle = long.MaxValue)
        {
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return new CpuVisibilityHorizon(
                    currentCycle,
                    CpuVisibilityHorizonReason.TargetCycle,
                    AmigaDiskController.SchedulerWakeReason.None);
            }

            var externalWins = externalBoundaryCycle < targetCycle;
            if (externalWins)
            {
                targetCycle = Math.Max(currentCycle + 1, externalBoundaryCycle);
            }

            _cpuVisibilityQueries++;
            if (_bus.HasPendingCiaInterrupts)
            {
                return new CpuVisibilityHorizon(
                    currentCycle + 1,
                    CpuVisibilityHorizonReason.PendingInterrupt,
                    AmigaDiskController.SchedulerWakeReason.None);
            }

            RefreshCpuVisibilityAgenda(currentCycle, targetCycle);
            _cpuVisibilityRootReads++;
            var mask = cpuInterruptMask < 0 ? 0 : cpuInterruptMask & 7;
            var root = _cpuVisibilityAgenda.Get(mask);
            if (root.Cycle <= currentCycle)
            {
                _cpuVisibilityExpiredRootCounts[(int)root.Source]++;
                var expiredCopperState = -1;
                if (root.Source == CpuVisibilityDeadlineSource.Copper)
                {
                    expiredCopperState =
                        _bus.Display.GetLiveCopperCpuBatchBarrierStateForDiagnostics();
                    _cpuVisibilityExpiredCopperStateCounts[expiredCopperState]++;
                }
                _cpuVisibilityDirtySources = CpuVisibilityDirtySource.All;
                _cpuVisibilityValidThroughCycle = -1;
                RefreshCpuVisibilityAgenda(currentCycle, targetCycle);
                root = _cpuVisibilityAgenda.Get(mask);
                if (expiredCopperState == 5 &&
                    root.Source == CpuVisibilityDeadlineSource.Copper &&
                    root.Cycle <= currentCycle + 1)
                {
                    _cpuVisibilityExpiredCopperWaitImmediateRefreshes++;
                }
            }
            if (root.Cycle == long.MaxValue || root.Cycle >= targetCycle)
            {
                return new CpuVisibilityHorizon(
                    targetCycle,
                    externalWins
                        ? CpuVisibilityHorizonReason.ExternalBoundary
                        : CpuVisibilityHorizonReason.TargetCycle,
                    AmigaDiskController.SchedulerWakeReason.None);
            }

            var cycle = root.Cycle <= currentCycle ? currentCycle + 1 : root.Cycle;
            return new CpuVisibilityHorizon(
                Math.Min(cycle, targetCycle),
                MapReason(root.Source),
                root.DiskReason);
        }

        private CpuVisibilityHorizon GetNextCpuVisibilityHorizonIgnoringCopper(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask)
        {
            currentCycle = Math.Max(0, currentCycle);
            targetCycle = Math.Max(currentCycle, targetCycle);
            if (targetCycle <= currentCycle)
            {
                return new CpuVisibilityHorizon(
                    currentCycle,
                    CpuVisibilityHorizonReason.TargetCycle,
                    AmigaDiskController.SchedulerWakeReason.None);
            }
            if (_bus.HasPendingCiaInterrupts)
            {
                return new CpuVisibilityHorizon(
                    currentCycle + 1,
                    CpuVisibilityHorizonReason.PendingInterrupt,
                    AmigaDiskController.SchedulerWakeReason.None);
            }

            RefreshCpuVisibilityAgenda(currentCycle, targetCycle);
            var mask = cpuInterruptMask < 0 ? 0 : cpuInterruptMask & 7;
            var bestCycle = targetCycle;
            var bestSource = CpuVisibilityDeadlineSource.Count;
            var bestDiskReason = AmigaDiskController.SchedulerWakeReason.None;
            for (var sourceIndex = 0;
                 sourceIndex < (int)CpuVisibilityDeadlineSource.Count;
                 sourceIndex++)
            {
                var source = (CpuVisibilityDeadlineSource)sourceIndex;
                if (source == CpuVisibilityDeadlineSource.Copper)
                {
                    continue;
                }

                var (cycle, diskReason) = _cpuVisibilityAgenda.GetLeaf(mask, source);
                if (cycle <= currentCycle)
                {
                    cycle = currentCycle + 1;
                }
                if (cycle < bestCycle)
                {
                    bestCycle = cycle;
                    bestSource = source;
                    bestDiskReason = diskReason;
                }
            }

            return bestSource == CpuVisibilityDeadlineSource.Count
                ? new CpuVisibilityHorizon(
                    targetCycle,
                    CpuVisibilityHorizonReason.TargetCycle,
                    AmigaDiskController.SchedulerWakeReason.None)
                : new CpuVisibilityHorizon(
                    bestCycle,
                    MapReason(bestSource),
                    bestDiskReason);
        }

        private void RefreshCpuVisibilityAgenda(long currentCycle, long targetCycle)
        {
            if (_cpuVisibilityAgendaInitialized && currentCycle < _cpuVisibilityValidFromCycle)
            {
                _cpuVisibilityDirtySources = CpuVisibilityDirtySource.All;
                _cpuVisibilityValidThroughCycle = -1;
            }

            if (_cpuVisibilityAgendaInitialized &&
                _cpuVisibilityDirtySources == CpuVisibilityDirtySource.None &&
                targetCycle <= _cpuVisibilityValidThroughCycle)
            {
                return;
            }

            var refreshAll = !_cpuVisibilityAgendaInitialized ||
                targetCycle > _cpuVisibilityValidThroughCycle;
            var rasterDirty = refreshAll ||
                (_cpuVisibilityDirtySources & CpuVisibilityDirtySource.Raster) != 0;
            var deadlineTarget = Math.Max(targetCycle, _cpuVisibilityValidThroughCycle);
            if (refreshAll)
            {
                var frameCycles = _bus.RasterTiming.GetFrameCycles(
                    _bus.RasterTiming.LongFrameLines);
                var lookahead = currentCycle <= long.MaxValue - frameCycles
                    ? currentCycle + frameCycles
                    : long.MaxValue;
                deadlineTarget = Math.Max(targetCycle, lookahead);
            }

            var hsyncChanged = rasterDirty &&
                _cpuVisibilityHsyncCycle != _bus.NextHorizontalSyncCycle;
            var vblankChanged = rasterDirty &&
                _cpuVisibilityVblankCycle != _bus.NextVerticalBlankCycle;

            if (refreshAll || vblankChanged)
            {
                _cpuVisibilityVblankCycle = _bus.NextVerticalBlankCycle;
                SetAll(CpuVisibilityDeadlineSource.VerticalBlank,
                    _cpuVisibilityVblankCycle > currentCycle && _cpuVisibilityVblankCycle <= deadlineTarget
                        ? _cpuVisibilityVblankCycle
                        : long.MaxValue);
            }

            var refreshNonRaster = refreshAll ||
                (_cpuVisibilityDirtySources & ~CpuVisibilityDirtySource.Raster) != 0;
            var ciaAChanged = refreshNonRaster &&
                _cpuVisibilityCiaAVersion != _bus.CiaA.WakeVersion;
            var ciaBChanged = refreshNonRaster &&
                _cpuVisibilityCiaBVersion != _bus.CiaB.WakeVersion;
            if (refreshAll || ciaBChanged || (hsyncChanged && _cpuVisibilityHsyncTodActive))
            {
                _cpuVisibilityHsyncCycle = _bus.NextHorizontalSyncCycle;
                var hsyncTod = _bus.CiaB.GetNextTodInterruptCycle(
                    deadlineTarget,
                    _bus.NextHorizontalSyncCycle,
                    _bus.LineCycles);
                _cpuVisibilityHsyncTodActive = hsyncTod.HasValue;
                SetAll(CpuVisibilityDeadlineSource.HorizontalSyncTod,
                    Normalize(hsyncTod, currentCycle, deadlineTarget));
            }
            else if (hsyncChanged)
            {
                _cpuVisibilityHsyncCycle = _bus.NextHorizontalSyncCycle;
            }

            if (refreshAll || ciaAChanged || ciaBChanged)
            {
                _cpuVisibilityCiaAVersion = _bus.CiaA.WakeVersion;
                _cpuVisibilityCiaBVersion = _bus.CiaB.WakeVersion;
                SetAll(CpuVisibilityDeadlineSource.CiaTimer,
                    Normalize(_bus.GetNextCiaInterruptCycle(deadlineTarget), currentCycle, deadlineTarget));
            }

            var paulaVersion = refreshNonRaster
                ? _bus.Paula.RegisterWakeVersion
                : _cpuVisibilityPaulaVersion;
            var paulaInterruptVersion = refreshNonRaster
                ? _bus.Paula.CpuInterruptVisibilityVersion
                : _cpuVisibilityPaulaInterruptVersion;
            if (refreshAll || _cpuVisibilityPaulaInterruptVersion != paulaInterruptVersion)
            {
                _cpuVisibilityPaulaInterruptVersion = paulaInterruptVersion;
                for (var mask = 0; mask < 8; mask++)
                {
                    Set(mask, CpuVisibilityDeadlineSource.Interrupt,
                        Normalize(_bus.Paula.GetNextCpuVisibleInterruptCycle(
                            currentCycle, deadlineTarget, mask), currentCycle, deadlineTarget));
                }
            }

            if (refreshAll || _cpuVisibilityPaulaVersion != paulaVersion)
            {
                _cpuVisibilityPaulaVersion = paulaVersion;
                for (var mask = 0; mask < 8; mask++)
                {
                    Set(mask, CpuVisibilityDeadlineSource.Paula,
                        Normalize(_bus.Paula.GetNextCpuWakeCandidateCycle(
                            currentCycle, deadlineTarget, mask), currentCycle, deadlineTarget));
                }
            }

            var diskVersion = refreshNonRaster
                ? _bus.Disk.SchedulerWakeVersion
                : _cpuVisibilityDiskVersion;
            if (refreshAll || _cpuVisibilityDiskVersion != diskVersion)
            {
                _cpuVisibilityDiskVersion = diskVersion;
                for (var mask = 0; mask < 8; mask++)
                {
                    var candidate = _bus.Disk.GetNextCpuVisibleWakeCandidateCycle(
                        currentCycle, deadlineTarget, mask, out var reason);
                    Set(mask, CpuVisibilityDeadlineSource.Disk,
                        Normalize(candidate, currentCycle, deadlineTarget), reason);
                }
            }

            var displayVersion = refreshNonRaster
                ? _bus.Display.LiveWakeVersion
                : _cpuVisibilityDisplayVersion;
            if (refreshAll || _cpuVisibilityDisplayVersion != displayVersion)
            {
                _cpuVisibilityDisplayVersion = displayVersion;
                SetAll(CpuVisibilityDeadlineSource.Copper,
                    Normalize(_bus.Display.GetNextLiveCopperCpuBatchBarrierCycle(
                        currentCycle, deadlineTarget), currentCycle, deadlineTarget));
            }

            var controlCycle = refreshNonRaster
                ? _agenda.Get(AgnusBusAgendaSource.Control)
                : _cpuVisibilityControlCycle;
            if (refreshAll || _cpuVisibilityControlCycle != controlCycle)
            {
                _cpuVisibilityControlCycle = controlCycle;
                SetAll(CpuVisibilityDeadlineSource.Control,
                    controlCycle > currentCycle && controlCycle <= deadlineTarget
                        ? controlCycle
                        : long.MaxValue);
            }

            var blitterVersion = refreshNonRaster
                ? _bus.Blitter.WakeVersion
                : _cpuVisibilityBlitterVersion;
            if (refreshAll || _cpuVisibilityBlitterVersion != blitterVersion)
            {
                _cpuVisibilityBlitterVersion = blitterVersion;
                SetAll(CpuVisibilityDeadlineSource.Blitter,
                    Normalize(_bus.Blitter.GetNextWakeCandidateCycle(
                        currentCycle, deadlineTarget), currentCycle, deadlineTarget));
            }

            _cpuVisibilityAgendaInitialized = true;
            if (refreshAll)
            {
                _cpuVisibilityValidFromCycle = currentCycle;
            }
            _cpuVisibilityDirtySources = CpuVisibilityDirtySource.None;
            _cpuVisibilityValidThroughCycle = Math.Max(
                _cpuVisibilityValidThroughCycle,
                deadlineTarget);
        }

        private void SetAll(CpuVisibilityDeadlineSource source, long cycle)
        {
            _cpuVisibilitySourceRefreshes++;
            for (var mask = 0; mask < 8; mask++)
            {
                if (_cpuVisibilityAgenda.Set(mask, source, cycle))
                {
                    _cpuVisibilityLeafUpdates++;
                }
            }
        }

        private void Set(
            int mask,
            CpuVisibilityDeadlineSource source,
            long cycle,
            AmigaDiskController.SchedulerWakeReason diskReason = AmigaDiskController.SchedulerWakeReason.None)
        {
            if (_cpuVisibilityAgenda.Set(mask, source, cycle, diskReason))
            {
                _cpuVisibilityLeafUpdates++;
            }
        }

        private static long Normalize(long? cycle, long currentCycle, long targetCycle)
            => cycle.HasValue && cycle.Value > currentCycle && cycle.Value <= targetCycle
                ? cycle.Value
                : long.MaxValue;

        private static CpuVisibilityHorizonReason MapReason(CpuVisibilityDeadlineSource source)
            => source switch
            {
                CpuVisibilityDeadlineSource.Interrupt => CpuVisibilityHorizonReason.PendingInterrupt,
                CpuVisibilityDeadlineSource.VerticalBlank => CpuVisibilityHorizonReason.VerticalBlank,
                CpuVisibilityDeadlineSource.HorizontalSyncTod => CpuVisibilityHorizonReason.HorizontalSyncTod,
                CpuVisibilityDeadlineSource.CiaTimer => CpuVisibilityHorizonReason.CiaTimer,
                CpuVisibilityDeadlineSource.Disk => CpuVisibilityHorizonReason.Disk,
                CpuVisibilityDeadlineSource.Paula => CpuVisibilityHorizonReason.Paula,
                CpuVisibilityDeadlineSource.Copper => CpuVisibilityHorizonReason.Copper,
                CpuVisibilityDeadlineSource.Control => CpuVisibilityHorizonReason.ControlEvent,
                CpuVisibilityDeadlineSource.Blitter => CpuVisibilityHorizonReason.Blitter,
                _ => CpuVisibilityHorizonReason.TargetCycle
            };

        internal static M68kTraceBatchWakeSource MapLegacyReason(CpuVisibilityHorizonReason reason)
            => reason switch
            {
                CpuVisibilityHorizonReason.PendingInterrupt => M68kTraceBatchWakeSource.PendingInterrupt,
                CpuVisibilityHorizonReason.VerticalBlank => M68kTraceBatchWakeSource.VerticalBlank,
                CpuVisibilityHorizonReason.HorizontalSyncTod => M68kTraceBatchWakeSource.HorizontalSyncTod,
                CpuVisibilityHorizonReason.CiaTimer => M68kTraceBatchWakeSource.CiaTimer,
                CpuVisibilityHorizonReason.Disk => M68kTraceBatchWakeSource.Disk,
                CpuVisibilityHorizonReason.Paula => M68kTraceBatchWakeSource.Paula,
                CpuVisibilityHorizonReason.Copper => M68kTraceBatchWakeSource.Copper,
                CpuVisibilityHorizonReason.Blitter => M68kTraceBatchWakeSource.Blitter,
                _ => M68kTraceBatchWakeSource.TargetCycle
            };
    }
}
