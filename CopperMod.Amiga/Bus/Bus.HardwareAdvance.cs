/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;

namespace CopperMod.Amiga.Bus
{
    internal sealed partial class Bus
    {
        internal void RequestHardwareInterrupt(ushort intreqBit, long cycle)
        {
            Paula.RequestInterrupt(intreqBit, Math.Max(0, cycle));
            _hardwareScheduler.NotifyWorkScheduled(cycle);
        }

        internal void NotifyHardwareWorkScheduled(long cycle)
            => _hardwareScheduler.NotifyWorkScheduled(cycle);

        public void AdvanceRasterTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.Raster | AmigaHardwareEventMask.ForceCatchUp);

        internal void AdvanceRasterCoreTo(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            if (targetCycle <= _lastRasterAdvanceCycle)
            {
                return;
            }

            _lastRasterAdvanceCycle = targetCycle;
            while (_nextHorizontalSyncCycle <= targetCycle)
            {
                CiaB.IncrementTod(_nextHorizontalSyncCycle, _pendingCiaInterrupts);
                _nextHorizontalSyncIndex++;
                _nextHorizontalSyncCycle += _lineCycles;
            }

            if (targetCycle < _nextVerticalBlankCycle)
            {
                return;
            }

            while (_nextVerticalBlankCycle <= targetCycle)
            {
                CiaA.IncrementTod(_nextVerticalBlankCycle, _pendingCiaInterrupts);
                RequestHardwareInterrupt(AmigaConstants.IntreqVerticalBlank, _nextVerticalBlankCycle);
                _nextVerticalBlankCycle = GetNextVerticalBlankCycle(_nextVerticalBlankCycle);
            }
        }

        private void UpdateBeamPosition(long targetCycle)
        {
            var beam = _beamClock.GetPosition(targetCycle);
            var horizontal = EncodeVhposrHorizontal(beam.BeamHorizontal);
            Paula.SetBeamPosition(beam.BeamLine, horizontal, beam.IsLongFrame);
        }

        private void ResetHorizontalSyncCounter()
        {
            _nextHorizontalSyncIndex = 1;
            _nextHorizontalSyncCycle = Math.Max(1, _lineCycles);
        }

        private void RecalculateRasterEvents(long cycle)
        {
            var beam = _beamClock.GetPosition(cycle);
            var lineStart = beam.FrameStartCycle + ((long)beam.BeamLine * _lineCycles);
            var hsyncOffset = _agnusRegisters.VariableHSyncEnabled
                ? (long)_agnusRegisters.HSyncStart * _rasterTiming.CpuCyclesPerColorClock
                : _lineCycles;
            _nextHorizontalSyncCycle = lineStart + hsyncOffset;
            if (_nextHorizontalSyncCycle <= cycle)
                _nextHorizontalSyncCycle = cycle + _lineCycles;
            _nextHorizontalSyncIndex = beam.BeamLine + 1;
            _nextVerticalBlankCycle = GetNextVerticalBlankCycle(cycle);
            _rasterlineScheduleCache.Reset();
        }

        private long GetNextVerticalBlankCycle(long cycle)
        {
            var beam = _beamClock.GetPosition(cycle);
            if (!_agnusRegisters.VariableVBlankEnabled && !_agnusRegisters.VariableVSyncEnabled)
                return _beamClock.GetNextFrameStartCycle(cycle);

            var line = _agnusRegisters.VariableVBlankEnabled
                ? _agnusRegisters.VBlankStart
                : _agnusRegisters.VSyncStart;
            var candidate = beam.FrameStartCycle + ((long)line * _lineCycles);
            if (candidate <= cycle)
                candidate = _beamClock.GetNextFrameStartCycle(cycle) + ((long)line * _lineCycles);
            return candidate;
        }

        internal AgnusBeamPosition GetBeamPosition(long cycle)
            => _beamClock.GetPosition(cycle);

        internal long GetFrameStopCycle(long frameStartCycle)
            => _beamClock.GetFrameStopCycle(frameStartCycle);

        internal long GetNextFrameStartCycle(long cycle)
            => _beamClock.GetNextFrameStartCycle(cycle);

        public void AdvanceCiasTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.DiskEvents |
                    AmigaHardwareEventMask.CiaTimers |
                    AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceCiaTimersTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.CiaTimers | AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceDmaTo(long targetCycle)
        {
            AdvanceDmaTo(targetCycle, advanceLiveAgnus: true, advancePassiveDiskInput: true);
        }

        public void AdvanceDmaTo(long targetCycle, bool advanceLiveAgnus)
        {
            AdvanceDmaTo(targetCycle, advanceLiveAgnus, advancePassiveDiskInput: true);
        }

        public void AdvanceDmaTo(long targetCycle, bool advanceLiveAgnus, bool advancePassiveDiskInput)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.PaulaRegister |
                    AmigaHardwareEventMask.DiskEvents |
                    (advancePassiveDiskInput ? AmigaHardwareEventMask.DiskPassiveInput : AmigaHardwareEventMask.None) |
                    (advanceLiveAgnus ? AmigaHardwareEventMask.Agnus : AmigaHardwareEventMask.None) |
                    AmigaHardwareEventMask.Blitter |
                    AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceHardwareTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.All |
                    AmigaHardwareEventMask.DiskPassiveInput |
                    AmigaHardwareEventMask.ForceCatchUp);

        public void AdvanceHardwareEventsTo(long targetCycle)
            => _hardwareScheduler.DrainTo(
                targetCycle,
                AmigaHardwareEventMask.All | AmigaHardwareEventMask.CpuBoundary);

        public void AdvanceHardwareEventsTo(long targetCycle, int cpuInterruptMask)
        {
            var mask = (AmigaHardwareEventMask.All & ~AmigaHardwareEventMask.PaulaRegister) |
                AmigaHardwareEventMask.CpuBoundary;
            if (Paula.HasCpuWakeWorkThrough(targetCycle, cpuInterruptMask))
            {
                mask |= AmigaHardwareEventMask.PaulaRegister;
            }

            _hardwareScheduler.DrainTo(targetCycle, mask);
        }

        public AmigaHardwareSchedulerSnapshot CaptureHardwareSchedulerSnapshot()
            => _hardwareScheduler.CaptureSnapshot();

        internal void RecordCopperQuiescentCustomRegisterWrite(
            AmigaBusRequester requester,
            ushort offset,
            long cycle,
            bool scheduleAffecting)
            => _hardwareScheduler.RecordCopperQuiescentCustomRegisterWrite(
                requester,
                offset,
                cycle,
                scheduleAffecting);

        public void SetHardwareSchedulerHostProfilingEnabled(bool enabled)
        {
            _hardwareScheduler.HostProfilingEnabled = enabled;
            _deferredCpuWaitDiagnosticsEnabled = enabled || _deferredCpuBusBatchVerifyEnabled;
        }

        public void ResetHardwareSchedulerHostProfile()
            => _hardwareScheduler.ResetHostProfile();

        internal void SetSlotScheduleAuditSink(Action<AgnusSlotScheduleAuditEntry>? sink)
            => _hrmSlotEngine.SetSlotScheduleAuditSink(sink);

        internal bool TryGetCommittedAgnusSlotOwner(long cycle, out AgnusChipSlotOwner owner)
            => _hrmSlotEngine.TryGetCommittedSlotOwner(cycle, out owner);

        internal AgnusSlotAuditSource PushSlotScheduleAuditSource(AgnusSlotAuditSource source)
        {
            var previous = _hrmSlotEngine.SlotScheduleAuditSource;
            _hrmSlotEngine.SlotScheduleAuditSource = source;
            return previous;
        }

        internal void RestoreSlotScheduleAuditSource(AgnusSlotAuditSource source)
            => _hrmSlotEngine.SlotScheduleAuditSource = source;

        internal AgnusSlotAuditSourceState PushSlotScheduleAuditSource(
            AgnusSlotAuditSource source,
            int sourceA,
            int sourceB,
            int sourceC)
        {
            var previous = _hrmSlotEngine.SlotScheduleAuditSourceState;
            _hrmSlotEngine.SlotScheduleAuditSourceState = new AgnusSlotAuditSourceState(source, sourceA, sourceB, sourceC);
            return previous;
        }

        internal void RestoreSlotScheduleAuditSource(AgnusSlotAuditSourceState state)
            => _hrmSlotEngine.SlotScheduleAuditSourceState = state;

        internal bool TrySkipRasterlineScheduleDrain(
            long currentCycle,
            long targetCycle,
            AmigaHardwareEventMask mask)
            => _rasterlineScheduleCache.TrySkipDrain(currentCycle, targetCycle, mask);

        internal void InvalidateRasterlineSchedule(long cycle, AmigaHardwareEventMask mask)
            => _rasterlineScheduleCache.InvalidateFrom(cycle, mask);

        internal AmigaRasterlineScheduleCacheSnapshot CaptureRasterlineScheduleCacheSnapshot()
            => _rasterlineScheduleCache.CaptureSnapshot();

        internal void AdvanceDmaCoreTo(long targetCycle, bool advanceLiveAgnus, bool advancePassiveDiskInput)
        {
            if (advanceLiveAgnus && LiveAgnusDmaEnabled)
            {
                Agnus.AdvanceTo(targetCycle);
            }

            Paula.AdvanceTo(targetCycle);
            if (advancePassiveDiskInput)
            {
                Disk.AdvanceTo(targetCycle);
            }
            else
            {
                Disk.AdvanceEventsTo(targetCycle);
            }

            Blitter.AdvanceTo(targetCycle);
            Paula.AdvanceTo(targetCycle);
        }

        internal void AdvanceCiaTimersCoreTo(long targetCycle)
        {
            CiaA.AdvanceTo(targetCycle, _pendingCiaInterrupts);
            CiaB.AdvanceTo(targetCycle, _pendingCiaInterrupts);
        }

        internal void AdvanceAgnusCoreTo(long targetCycle)
        {
            if (LiveAgnusDmaEnabled)
            {
                Agnus.AdvanceTo(targetCycle, Display.HasLiveDisplayWork());
            }
        }

        internal long GetNextRasterEventCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle < currentCycle)
            {
                return long.MaxValue;
            }

            var next = Math.Min(_nextHorizontalSyncCycle, _nextVerticalBlankCycle);
            if (next > targetCycle)
            {
                return long.MaxValue;
            }

            return next <= currentCycle ? currentCycle : next;
        }

        internal long GetNextCiaTimerEventCycle(long currentCycle, long targetCycle)
        {
            var ciaA = CiaA.GetNextInterruptCycle(targetCycle);
            var ciaB = CiaB.GetNextInterruptCycle(targetCycle);
            var next = long.MaxValue;
            if (ciaA.HasValue)
            {
                next = Math.Min(next, ciaA.Value);
            }

            if (ciaB.HasValue)
            {
                next = Math.Min(next, ciaB.Value);
            }

            if (next == long.MaxValue || next > targetCycle)
            {
                return long.MaxValue;
            }

            return next <= currentCycle ? currentCycle : next;
        }

        internal long GetNextAgnusEventCycle(long currentCycle, long targetCycle)
        {
            if (!LiveAgnusDmaEnabled)
            {
                return long.MaxValue;
            }

            return Agnus.GetNextWakeCandidateCycle(currentCycle, targetCycle, Display.HasLiveDisplayWork());
        }

        internal long GetNextCpuVisibleAgnusEventCycle(long currentCycle, long targetCycle)
        {
            if (!LiveAgnusDmaEnabled)
            {
                return long.MaxValue;
            }

            return Display.GetNextLiveCopperWakeCandidateCycle(currentCycle, targetCycle) ?? long.MaxValue;
        }

        private void AdvanceDmaBeforeCpuChipAccess(
            AmigaBusAccessTarget target,
            uint address,
            long grantedCycle,
            bool isWrite)
        {
            _hardwareScheduler.DrainForCpuAccess(
                target,
                address,
                grantedCycle,
                isWrite,
                AmigaBusAccessSize.Word,
                allowCopperQuiescentFastPath: false);
        }

        private void AdvanceDmaAfterCpuGrantIfNeeded(
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            long grantedCycle,
            bool isWrite)
        {
            if ((target == AmigaBusAccessTarget.ChipRam ||
                    target == AmigaBusAccessTarget.ExpansionRam ||
                    target == AmigaBusAccessTarget.RealTimeClock ||
                    target == AmigaBusAccessTarget.CustomRegisters) &&
                grantedCycle <= requestedCycle)
            {
                return;
            }

            AdvanceDmaBeforeCpuChipAccess(target, address, grantedCycle, isWrite);
        }

        public long? GetNextCiaInterruptCycle(long maxCycle)
        {
            var ciaA = CiaA.GetNextInterruptCycle(maxCycle);
            var ciaB = CiaB.GetNextInterruptCycle(maxCycle);
            if (!ciaA.HasValue)
            {
                return ciaB;
            }

            if (!ciaB.HasValue)
            {
                return ciaA;
            }

            return Math.Min(ciaA.Value, ciaB.Value);
        }

        internal bool HasPendingCiaInterrupts => _pendingCiaInterrupts.Count != 0;

        internal long NextVerticalBlankCycle => _nextVerticalBlankCycle;

        internal long NextHorizontalSyncCycle => _nextHorizontalSyncCycle;

        internal long LineCycles => _lineCycles;

        public long GetNextStoppedCpuWakeCandidateCycle(long currentCycle, long targetCycle)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle);

        public long GetNextStoppedCpuWakeCandidateCycle(long currentCycle, long targetCycle, int cpuInterruptMask)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, cpuInterruptMask);

        public long GetNextCpuBatchWakeCandidateCycle(long currentCycle, long targetCycle)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, out _);

        public long GetNextCpuBatchWakeCandidateCycle(long currentCycle, long targetCycle, int cpuInterruptMask)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, cpuInterruptMask, out _);

        public long GetNextCpuBatchWakeCandidateCycle(
            long currentCycle,
            long targetCycle,
            out M68kTraceBatchWakeSource wakeSource)
            => GetNextCpuBatchWakeCandidateCycle(currentCycle, targetCycle, cpuInterruptMask: -1, out wakeSource);

        public long GetNextCpuBatchWakeCandidateCycle(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out M68kTraceBatchWakeSource wakeSource)
            => GetNextCpuBatchWakeCandidateCycle(
                currentCycle,
                targetCycle,
                cpuInterruptMask,
                out wakeSource,
                out _);

        internal long GetNextCpuBatchWakeCandidateCycle(
            long currentCycle,
            long targetCycle,
            int cpuInterruptMask,
            out M68kTraceBatchWakeSource wakeSource,
            out AmigaDiskController.SchedulerWakeReason diskWakeReason)
            => _hardwareScheduler.GetNextCpuVisibleEventCycle(
                currentCycle,
                targetCycle,
                cpuInterruptMask,
                out wakeSource,
                out diskWakeReason);

        private static long MinStoppedWakeCandidate(
            long candidate,
            long currentCycle,
            long targetCycle,
            long? eventCycle,
            M68kTraceBatchWakeSource eventSource,
            ref M68kTraceBatchWakeSource wakeSource)
        {
            if (!eventCycle.HasValue || eventCycle.Value > targetCycle)
            {
                return candidate;
            }

            var cycle = eventCycle.Value <= currentCycle ? currentCycle + 1 : eventCycle.Value;
            if (cycle < candidate)
            {
                wakeSource = eventSource;
                return cycle;
            }

            return candidate;
        }

        public AmigaCia GetCia(AmigaCiaId id)
        {
            return id == AmigaCiaId.A ? CiaA : CiaB;
        }

        public byte AbleCiaInterrupts(AmigaCiaId id, byte value, long cycle)
        {
            return GetCia(id).AbleInterrupts(value, cycle, _pendingCiaInterrupts);
        }

        public byte SetCiaInterrupts(AmigaCiaId id, byte value, long cycle)
        {
            return GetCia(id).SetInterrupts(value, cycle, _pendingCiaInterrupts);
        }

        public void PulseCiaFlag(AmigaCiaId id, long cycle)
        {
            GetCia(id).PulseFlag(cycle, _pendingCiaInterrupts);
        }

        public IReadOnlyList<AmigaCiaInterruptEvent> DrainCiaInterrupts()
        {
            if (_pendingCiaInterrupts.Count == 0)
            {
                return Array.Empty<AmigaCiaInterruptEvent>();
            }

            var count = Math.Min(_pendingCiaInterrupts.Count, _drainedCiaInterruptBuffer.Length);
            for (var i = 0; i < count; i++)
            {
                _drainedCiaInterruptBuffer[i] = _pendingCiaInterrupts[i];
            }

            SortCiaInterrupts(_drainedCiaInterruptBuffer, count);
            _pendingCiaInterrupts.Clear();
            _drainedCiaInterrupts.Reset(_drainedCiaInterruptBuffer, count);
            return _drainedCiaInterrupts;
        }

        private static void SortCiaInterrupts(AmigaCiaInterruptEvent[] events, int count)
        {
            for (var i = 1; i < count; i++)
            {
                var value = events[i];
                var j = i - 1;
                while (j >= 0 && CompareCiaInterrupts(events[j], value) > 0)
                {
                    events[j + 1] = events[j];
                    j--;
                }

                events[j + 1] = value;
            }
        }

        private static int CompareCiaInterrupts(AmigaCiaInterruptEvent left, AmigaCiaInterruptEvent right)
        {
            var cycleCompare = left.Cycle.CompareTo(right.Cycle);
            return cycleCompare != 0 ? cycleCompare : left.Cia.CompareTo(right.Cia);
        }


    }
}
