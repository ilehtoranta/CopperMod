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
        private long _cpuVisibilityRootReads;
        private long _cpuVisibilityLeafUpdates;
        private long _cpuVisibilitySourceRefreshes;
        private long _cpuVisibilityShadowMatches;
        private long _cpuVisibilityShadowMismatches;
        private long _cpuVisibilityPotentialCycles;
        private long _cpuVisibilityPotentialInstructions;
        private long _cpuVisibilityShortHorizonRejections;
        private long _cpuVisibilityLegacyQueryTicks;
        private long _cpuVisibilityExecutorQueryTicks;
        private string _cpuVisibilityFirstShadowMismatch = string.Empty;

        public long CpuVisibilityQueries => _cpuVisibilityQueries;
        public long CpuVisibilityRootReads => _cpuVisibilityRootReads;
        public long CpuVisibilityLeafUpdates => _cpuVisibilityLeafUpdates;
        public long CpuVisibilitySourceRefreshes => _cpuVisibilitySourceRefreshes;
        public long CpuVisibilityShadowMatches => _cpuVisibilityShadowMatches;
        public long CpuVisibilityShadowMismatches => _cpuVisibilityShadowMismatches;
        public long CpuVisibilityPotentialCycles => _cpuVisibilityPotentialCycles;
        public long CpuVisibilityPotentialInstructions => _cpuVisibilityPotentialInstructions;
        public long CpuVisibilityShortHorizonRejections => _cpuVisibilityShortHorizonRejections;
        public long CpuVisibilityLegacyQueryTicks => _cpuVisibilityLegacyQueryTicks;
        public long CpuVisibilityExecutorQueryTicks => _cpuVisibilityExecutorQueryTicks;
        public string CpuVisibilityFirstShadowMismatch => _cpuVisibilityFirstShadowMismatch;
        public bool CpuVisibilityShadowEnabled { get; } =
            ReadBooleanEnvironmentVariable("COPPERMOD_AMIGA_CPU_VISIBILITY_SHADOW", false);

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
            _cpuVisibilityRootReads = 0;
            _cpuVisibilityLeafUpdates = 0;
            _cpuVisibilitySourceRefreshes = 0;
            _cpuVisibilityShadowMatches = 0;
            _cpuVisibilityShadowMismatches = 0;
            _cpuVisibilityPotentialCycles = 0;
            _cpuVisibilityPotentialInstructions = 0;
            _cpuVisibilityShortHorizonRejections = 0;
            _cpuVisibilityLegacyQueryTicks = 0;
            _cpuVisibilityExecutorQueryTicks = 0;
            _cpuVisibilityFirstShadowMismatch = string.Empty;
        }

        public void RecordCpuVisibilityShadow(
            long currentCycle,
            long targetCycle,
            long referenceCycle,
            M68kTraceBatchWakeSource referenceSource,
            AmigaDiskController.SchedulerWakeReason referenceDiskReason,
            in CpuVisibilityHorizon horizon)
        {
            var potentialCycles = Math.Max(0, horizon.Cycle - currentCycle);
            _cpuVisibilityPotentialCycles += potentialCycles;
            _cpuVisibilityPotentialInstructions += potentialCycles / 4;
            if (potentialCycles < 16)
            {
                _cpuVisibilityShortHorizonRejections++;
            }
            var mappedSource = MapLegacyReason(horizon.Reason);
            var diskReason = horizon.Reason == CpuVisibilityHorizonReason.Disk
                ? horizon.DiskReason
                : AmigaDiskController.SchedulerWakeReason.None;
            if (referenceCycle == horizon.Cycle &&
                referenceSource == mappedSource &&
                referenceDiskReason == diskReason)
            {
                _cpuVisibilityShadowMatches++;
                return;
            }

            _cpuVisibilityShadowMismatches++;
            if (_cpuVisibilityFirstShadowMismatch.Length == 0)
            {
                _cpuVisibilityFirstShadowMismatch =
                    $"current={currentCycle},target={targetCycle}," +
                    $"reference={referenceCycle}/{referenceSource}/{referenceDiskReason}," +
                    $"executor={horizon.Cycle}/{horizon.Reason}/{horizon.DiskReason}";
            }
        }

        public void RecordCpuVisibilityQueryTicks(long legacyTicks, long executorTicks)
        {
            _cpuVisibilityLegacyQueryTicks += Math.Max(0, legacyTicks);
            _cpuVisibilityExecutorQueryTicks += Math.Max(0, executorTicks);
        }

        public void InvalidateCpuVisibilityAgenda(CpuVisibilityDirtySource sources = CpuVisibilityDirtySource.All)
            => _cpuVisibilityDirtySources |= sources;

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
                _cpuVisibilityDirtySources = CpuVisibilityDirtySource.All;
                _cpuVisibilityValidThroughCycle = -1;
                RefreshCpuVisibilityAgenda(currentCycle, targetCycle);
                root = _cpuVisibilityAgenda.Get(mask);
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
