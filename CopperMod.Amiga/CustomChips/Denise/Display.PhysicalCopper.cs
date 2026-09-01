/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        // Adoption is limited to the physical OCS PAL timeline. Other chipset
        // counter geometries retain their existing execution path.
        internal bool UsesPhysicalCopperPipeline => _chipset == AmigaChipset.OcsPal;

        internal void NotifyBlitterCompletionSignalsChanged(long cycle)
        {
            // Completion is not a DMA reservation. Invalidate predictions
            // without disturbing an already accepted Copper word.
            _liveWakeVersion++;
            InvalidateLiveDisplayEventCycle();
        }

        private long GetNextPhysicalCopperInputCycle(
            long earliestCycle,
            bool allowDummy,
            AgnusHrmSlotEngine? scratchSlots = null)
        {
            var cycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, earliestCycle));
            while (true)
            {
                var phase = scratchSlots == null
                    ? _bus.CausalBusExecutor.GetCopperPhaseAt(cycle)
                    : scratchSlots.GetCopperPhaseAt(cycle);
                if (phase.ControlEligible ||
                    allowDummy && phase.Input == AgnusCopperDmaPhaseKind.WrapDummy)
                {
                    return cycle;
                }
                cycle += AgnusChipSlotScheduler.SlotCycles;
            }
        }

        private bool IsPhysicalCopperBeamComparisonSatisfied(
            ushort first,
            ushort second,
            long cycle,
            AgnusHrmSlotEngine? scratchSlots = null)
        {
            var phase = scratchSlots == null
                ? _bus.CausalBusExecutor.GetCopperPhaseAt(cycle)
                : scratchSlots.GetCopperPhaseAt(cycle);
            var beam = _bus.GetBeamPosition(cycle);
            var mask = GetCopperComparisonMask(second);
            var beamWord = (ushort)(((beam.BeamLine & 0xFF) << 8) |
                (phase.NominalHorizontal & 0xFE));
            return (beamWord & mask) >= (first & mask);
        }

        private long GetNextPhysicalCopperCycle(
            in CopperPresentationState copper,
            bool dmaEnabled,
            uint listPointer,
            AgnusHrmSlotEngine? scratchSlots = null,
            CopperBlitterWaitSnapshot? scratchBlitter = null)
            => Math.Min(copper.PhysicalPipeline.NextVblankControlCycle,
                GetNextPhysicalCopperExecutionCycle(
                    copper, dmaEnabled, listPointer, scratchSlots, scratchBlitter));

        private long GetNextPhysicalCopperExecutionCycle(
            in CopperPresentationState copper,
            bool dmaEnabled,
            uint listPointer,
            AgnusHrmSlotEngine? scratchSlots = null,
            CopperBlitterWaitSnapshot? scratchBlitter = null)
        {
            if (copper.PhysicalPipeline.HasIssuedWord)
            {
                // No enable, jump, or horizon normalization may cancel or
                // move a transfer whose input has already been accepted.
                return copper.PhysicalPipeline.IssuedAccess.GrantedCycle;
            }
            if (copper.Stopped || !dmaEnabled ||
                copper.PendingStart && listPointer == 0 ||
                copper.Pc == 0 && listPointer == 0)
            {
                return long.MaxValue;
            }

            var earliest = Math.Max(copper.Cycle, _liveFrameStartCycle);
            if (scratchSlots == null)
            {
                earliest = Math.Max(earliest, Math.Max(
                    _bus.ExecutedChipBusHorizon,
                    _bus.CausalBusExecutor.ExecutedThroughCycle));
            }
            var cycle = GetNextPhysicalCopperInputCycle(
                earliest, copper.PhysicalPipeline.IsInstructionReadReady, scratchSlots);
            if (copper.PhysicalPipeline.Stage != CopperControlStage.WaitComparison)
            {
                return cycle;
            }

            var blitterReadyCycle = GetCopperBlitterReadyCycle(
                copper.WaitSecond, cycle, scratchBlitter);
            if (blitterReadyCycle == long.MaxValue)
            {
                return long.MaxValue;
            }
            if (blitterReadyCycle > cycle)
            {
                cycle = GetNextPhysicalCopperInputCycle(
                    blitterReadyCycle,
                    allowDummy: false, scratchSlots);
            }

            var stop = GetLiveFrameStopCycle();
            while (cycle < stop && !IsPhysicalCopperBeamComparisonSatisfied(
                copper.WaitFirst, copper.WaitSecond, cycle, scratchSlots))
            {
                var beam = _bus.GetBeamPosition(cycle);
                var mask = GetCopperComparisonMask(copper.WaitSecond);
                if ((((beam.BeamLine & 0xFF) << 8) & mask & 0xFF00) <
                    (copper.WaitFirst & mask & 0xFF00))
                {
                    cycle = _bus.GetNextLineStartCycle(cycle);
                }
                else
                {
                    cycle += AgnusChipSlotScheduler.SlotCycles;
                }
                cycle = GetNextPhysicalCopperInputCycle(cycle, allowDummy: false, scratchSlots);
            }
            return cycle < stop ? cycle : long.MaxValue;
        }

        private bool PreparePhysicalCopperInputPlan(long inputCycle)
        {
            var row = GetOutputRowForCycle(_liveFrameStartCycle, inputCycle);
            if ((uint)row >= (uint)LowResOutputHeight)
            {
                return true;
            }
            if (!IsLiveLineValid(row))
            {
                // Capture the current line only. Building its future ownership
                // table does not execute a fetch or sample future chip RAM.
                CaptureLiveLineState(row);
            }
            return TryGetValidRowDmaPlan(row, GetLiveLineState(row), out _, recordFallback: false);
        }

        private CopperInputAction AdvancePhysicalCopperControl(
            ref CopperPresentationState copper,
            long cycle,
            bool inputAvailable,
            AgnusHrmSlotEngine? scratchSlots = null,
            CopperBlitterWaitSnapshot? scratchBlitter = null)
        {
            var stage = copper.PhysicalPipeline.Stage;
            if (copper.PhysicalPipeline.IsReadInputReady)
            {
                copper.PhysicalPipeline.ObserveReadIntent(GetNextPhysicalCopperInputCycle(
                    Math.Max(copper.Cycle, _liveFrameStartCycle), allowDummy: false, scratchSlots));
            }
            var phase = scratchSlots == null
                ? _bus.CausalBusExecutor.GetCopperPhaseAt(cycle)
                : scratchSlots.GetCopperPhaseAt(cycle);
            var blitterFinished = IsCopperBlitterFinishedForWait(
                copper.WaitSecond, cycle, scratchBlitter);
            var comparisonSatisfied = copper.PhysicalPipeline.NeedsComparison &&
                blitterFinished && IsPhysicalCopperBeamComparisonSatisfied(
                    copper.WaitFirst, copper.WaitSecond, cycle, scratchSlots);
            var action = copper.PhysicalPipeline.AdvanceInput(
                phase.Input, inputAvailable, comparisonSatisfied);
            var next = GetNextPhysicalCopperInputCycle(
                cycle + AgnusChipSlotScheduler.SlotCycles,
                copper.PhysicalPipeline.IsInstructionReadReady, scratchSlots);
            switch (action)
            {
                case CopperInputAction.ControlAdvanced:
                    if (stage == CopperControlStage.WaitIdle)
                    {
                        copper.WaitComparisonStartCycle = next;
                    }
                    copper.Cycle = next;
                    break;
                case CopperInputAction.WaitSatisfied:
                    copper.WaitRestartStage = CopperWaitRestartStage.None;
                    copper.RecordWaitControlTransition(copper.WaitComparisonStartCycle, cycle, next);
                    copper.Cycle = next;
                    break;
                case CopperInputAction.SkipAndReadFirst:
                    copper.SuppressNextMove |= comparisonSatisfied;
                    break;
                case CopperInputAction.None:
                    if (stage == CopperControlStage.WaitComparison && !blitterFinished)
                    {
                        copper.WaitObservedBlitterBusy = true;
                    }
                    copper.Cycle = next;
                    break;
            }
            return action;
        }

        private static uint GetPhysicalCopperReadAddress(
            in CopperPresentationState copper,
            CopperInputAction action)
            => action == CopperInputAction.DummyRead ? 0u :
                action == CopperInputAction.VblankInhibitedRead ? copper.PhysicalPipeline.VblankReadAddress :
                action == CopperInputAction.ReadSecond ? copper.Pc + 2u : copper.Pc;

        private static long GetPhysicalCopperRequestCycle(
            in CopperPresentationState copper,
            CopperInputAction action,
            long inputCycle)
            // The wrap dummy is a separate transfer. It cannot lend its
            // earlier input to the real word that remains ready behind it.
            => action == CopperInputAction.DummyRead
                ? inputCycle
                : copper.PhysicalPipeline.ReadIntentCycle;

        private void StepPhysicalLiveCopper(long targetCycle)
        {
            var cycle = GetNextPhysicalCopperCycle(
                _liveCopper, IsLiveCopperDmaEnabled(), _copperListPointer);
            if (cycle > targetCycle)
            {
                return;
            }
            if (_liveCopper.PhysicalPipeline.HasIssuedWord &&
                _liveCopper.PhysicalPipeline.IssuedAccess.GrantedCycle == cycle)
            {
                var execution = _bus.CausalBusExecutor.ExecuteAcceptedCopperWord(
                    _liveCopper.PhysicalPipeline.IssuedAccess);
                CompletePhysicalLiveCopperWord(execution.Value, execution.Access);
                return;
            }
            if (_liveCopper.PhysicalPipeline.IsVblankLatchDue(cycle))
            {
                CompletePhysicalCopperVblankLatch(ref _liveCopper, cycle, _copperListPointer);
                InvalidateLiveDisplayEventCycle();
                return;
            }

            var strobeDue = _liveCopper.PhysicalPipeline.IsVblankStrobeDue(cycle);
            var wasStopped = _liveCopper.Stopped;
            var dmaEnabled = IsLiveCopperDmaEnabled();
            // The current RGA input is evaluated before the frame strobe.
            // A dummy output at this same cycle was completed above; its
            // following real input therefore still precedes the strobe.
            if (!strobeDue || GetNextPhysicalCopperExecutionCycle(
                    _liveCopper, dmaEnabled, _copperListPointer) == cycle)
            {
                StepPhysicalLiveCopperInput(cycle);
            }
            if (strobeDue)
            {
                BeginPhysicalCopperVblankRestart(
                    ref _liveCopper, cycle, _copperListPointer, dmaEnabled, wasStopped);
                InvalidateLiveDisplayEventCycle();
            }
        }

        private void BeginPhysicalCopperVblankRestart(
            ref CopperPresentationState copper,
            long cycle,
            uint listPointer,
            bool dmaEnabled,
            bool wasStopped)
        {
            var oldReadAddress = copper.PhysicalPipeline.HasIssuedWord
                ? copper.PhysicalPipeline.IssuedAccess.Request.Address
                : copper.PhysicalPipeline.Stage == CopperControlStage.ReadSecond
                    ? AddDmaPointerOffset(copper.Pc, 2)
                    : copper.Pc;
            if (!wasStopped && copper.PhysicalPipeline.Stage == CopperControlStage.ReadSecond)
            {
                // The in-flight IR2 is consumed without decoding; its next
                // old-IP word is the inhibited transfer before list reload.
                oldReadAddress = AddDmaPointerOffset(oldReadAddress, 2);
            }
            copper.PhysicalPipeline.BeginVblankRestart(
                cycle, oldReadAddress, listPointer, dmaEnabled, wasStopped);
            copper.Cycle = copper.PhysicalPipeline.NextVblankControlCycle;
            copper.Stopped = false;
            copper.PendingStart = false;
            copper.SuppressNextMove = false;
        }

        private static void CompletePhysicalCopperVblankLatch(
            ref CopperPresentationState copper,
            long cycle,
            uint listPointer)
        {
            var action = copper.PhysicalPipeline.CompleteVblankLatch(cycle, listPointer);
            if (action == CopperVblankLatchAction.ReloadPointer)
            {
                copper.JumpTo(copper.PhysicalPipeline.VblankReloadPointer, cycle);
            }
            else if (action == CopperVblankLatchAction.AwaitDmaEnable)
            {
                // No new transfer is created while DMA is off. Preserve
                // the existing enable-boundary pointer-load behavior.
                copper.JumpTo(0, cycle);
                copper.PendingStart = true;
            }
            else
            {
                copper.Cycle = cycle;
            }
        }

        private void StepPhysicalLiveCopperInput(long cycle)
        {
            if (_liveCopper.PendingStart)
            {
                _liveCopper.StartFrom(_copperListPointer);
            }

            var planReady = PreparePhysicalCopperInputPlan(cycle);
            var inputAvailable = planReady && _bus.CausalBusExecutor.CanAcceptCopperInputAt(cycle);
            var stage = _liveCopper.PhysicalPipeline.Stage;
            var action = AdvancePhysicalCopperControl(
                ref _liveCopper, cycle, inputAvailable);
            // Count only this live input's comparison. The shared control
            // helper and deadline queries also serve read-only scratch work.
            if (_liveCopperRequesterEnabled && stage == CopperControlStage.WaitComparison &&
                inputAvailable &&
                _bus.CausalBusExecutor.GetCopperPhaseAt(cycle).Input == AgnusCopperDmaPhaseKind.Normal)
            {
                _liveCopperRequesterWaitComparisons++;
                if (!IsCopperBlitterFinishedForWait(_liveCopper.WaitSecond, cycle))
                {
                    _liveCopperRequesterBfdBusyObservations++;
                }
            }
            if (action == CopperInputAction.WaitSatisfied)
            {
                CaptureCopperWaitTransition(
                    _liveCopper.WaitComparisonStartCycle, _liveCopper.Cycle, cycle,
                    false, false, false, false);
            }
            if (action is CopperInputAction.ReadFirst or CopperInputAction.ReadSecond or
                CopperInputAction.SkipAndReadFirst or CopperInputAction.DummyRead or
                CopperInputAction.VblankInhibitedRead)
            {
                var address = GetPhysicalCopperReadAddress(_liveCopper, action);
                if (!_bus.CausalBusExecutor.TryAcceptCopperInput(
                    address, GetPhysicalCopperRequestCycle(_liveCopper, action, cycle), cycle, out var access))
                {
                    throw new InvalidOperationException("Copper input availability changed during acceptance.");
                }
                _liveCopper.PhysicalPipeline.AcceptWord(action, cycle, access);
                _liveCopper.Cycle = access.GrantedCycle;
                if (_liveCopperRequesterEnabled)
                {
                    _liveCopperRequesterPublishedNextRequests++;
                    if (action == CopperInputAction.SkipAndReadFirst)
                    {
                        _liveCopperRequesterSkipComparisons++;
                    }
                }
            }
            InvalidateLiveDisplayEventCycle();
        }

        private bool CompletePhysicalCopperWordState(
            ref CopperPresentationState copper,
            ushort value,
            in AmigaBusAccessResult access,
            out CopperInstructionLatch instruction)
        {
            var output = copper.PhysicalPipeline.CompleteWord(value, access.GrantedCycle);
            copper.Cycle = Math.Max(copper.Cycle, access.GrantedCycle);
            instruction = default;
            if (output == CopperOutputAction.Discarded)
            {
                return false;
            }
            if (output == CopperOutputAction.VblankReload)
            {
                copper.JumpTo(copper.PhysicalPipeline.VblankReloadPointer, access.GrantedCycle);
                return false;
            }
            if (output == CopperOutputAction.FirstWord)
            {
                copper.PendingInstructionSecondWord = true;
                copper.PendingInstructionFirst = value;
                copper.PendingInstructionFirstAccess = access;
                copper.PendingInstructionSecondWordRequestedCycle = access.CompletedCycle;
                return false;
            }

            instruction = new CopperInstructionLatch(
                copper.PhysicalPipeline.FirstWord, copper.PhysicalPipeline.FirstAccess,
                value, access, CopperHpCycles);
            copper.PendingInstructionSecondWord = false;
            copper.Pc = AddDmaPointerOffset(copper.Pc, 4);
            if (instruction.IsEnd)
            {
                copper.Stopped = true;
                return false;
            }
            if (instruction.IsMove)
            {
                return true;
            }

            copper.WaitFirst = instruction.First;
            copper.WaitSecond = instruction.Second;
            copper.WaitObservedBlitterBusy = false;
            copper.WaitComparisonStartCycle = 0;
            if (instruction.IsWait)
            {
                copper.WaitRestartStage = CopperWaitRestartStage.WaitingForComparison;
                copper.PhysicalPipeline.BeginWait();
            }
            else
            {
                copper.PhysicalPipeline.BeginSkip();
            }
            return false;
        }

        private void CompletePhysicalLiveCopperWord(ushort value, in AmigaBusAccessResult access)
        {
            if (CompletePhysicalCopperWordState(ref _liveCopper, value, access, out var instruction))
            {
                var suppress = _liveCopper.SuppressNextMove;
                _liveCopper.SuppressNextMove = false;
                // Register visibility belongs to the actual IR2 output, not
                // the earlier request or the following control phase.
                ApplyLiveCopperMove(instruction.MoveRegister, instruction.Second,
                    access.GrantedCycle, access.CompletedCycle, suppress);
            }
            InvalidateLiveDisplayEventCycle();
        }

        private void EnsurePhysicalCopperRequesterPublication(LiveCopperRequester requester)
        {
            if (requester.TryPeekPendingRequest(out var pending))
            {
                if (!_liveCopper.PhysicalPipeline.HasIssuedWord ||
                    pending.EarliestEligibleCycle != _liveCopper.PhysicalPipeline.IssuedAccess.GrantedCycle ||
                    pending.Address != _liveCopper.PhysicalPipeline.IssuedAccess.Request.Address)
                {
                    throw new InvalidOperationException("Published Copper output lost its physical transfer.");
                }
                return;
            }

            var cycle = GetNextPhysicalCopperCycle(
                _liveCopper, IsLiveCopperDmaEnabled(), _copperListPointer);
            if (requester.TryPeekNextTransition(out var transition))
            {
                if (transition.Cycle == cycle && !_liveCopper.PhysicalPipeline.HasIssuedWord)
                {
                    return;
                }
                requester.CancelPendingTransition();
            }
            if (_liveCopper.PhysicalPipeline.HasIssuedWord)
            {
                var access = _liveCopper.PhysicalPipeline.IssuedAccess;
                var channel = _liveCopper.PhysicalPipeline.IssuedPurpose switch
                {
                    CopperInputAction.ReadSecond => 5,
                    CopperInputAction.DummyRead => 6,
                    _ => 4
                };
                requester.PublishWord(access.Request.Address, access.RequestedCycle,
                    access.GrantedCycle, channel);
            }
            else if (cycle != long.MaxValue)
            {
                requester.PublishTransition(LiveCopperTransitionKind.PhysicalInput,
                    cycle, AgnusLiveTransitionPhase.AfterSlotCommit);
            }
        }
    }
}
