/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.CustomChips.Blitter
{
    internal sealed partial class Blitter
    {
        internal readonly record struct AreaWordRetireControlSignals(
            bool Eligible,
            bool Armed,
            bool Pending,
            bool Accepted,
            long NextInputCycle,
            long RetirementCycle,
            bool LivePendingDValid,
            bool LivePendingDRequiresRetireControl,
            bool LivePendingDCompletesBlit,
            bool AreaMicroOpActive,
            int AreaMicroOpIndex,
            int AreaMicroOpCount,
            bool AreaMicroOpFinalWord);

        private AreaWordRetireControlState _areaWordRetireControl;

        internal AreaWordRetireControlSignals CaptureAreaWordRetireControlSignals()
            => new(
                UsesAreaWordRetireControl,
                _areaWordRetireControl.Armed,
                _areaWordRetireControl.Pending,
                _areaWordRetireControl.Accepted,
                _areaWordRetireControl.NextInputCycle,
                _areaWordRetireControl.RetirementCycle,
                _liveAreaPendingDValid,
                _liveAreaPendingDRequiresRetireControl,
                _liveAreaPendingDCompletesBlit,
                _areaMicroOpActive,
                _areaMicroOpIndex,
                GetAreaMicroOpCount(),
                _areaMicroOpFinalWord);

        // A D-only word is not retired by another memory transfer. The area
        // sequencer instead consumes an Agnus input which observes the next
        // physical OUT without owning it. Keep that input as value state so a
        // fixed-DMA collision cannot be flattened into a scalar word delay.
        private struct AreaWordRetireControlState
        {
            internal bool Armed;
            internal bool Accepted;
            internal long OutputCycle;
            internal long NextInputCycle;
            internal long RetirementCycle;

            internal readonly bool Pending => Armed && !Accepted;

            internal void Arm(long outputCycle)
            {
                Armed = true;
                Accepted = false;
                OutputCycle = outputCycle;
                NextInputCycle = outputCycle;
                RetirementCycle = -1;
            }

            internal void Advance(long inputCycle, bool inputAvailable)
            {
                System.Diagnostics.Debug.Assert(Pending);
                System.Diagnostics.Debug.Assert(inputCycle >= NextInputCycle);
                if (inputAvailable)
                {
                    Accepted = true;
                    NextInputCycle = -1;
                    RetirementCycle = inputCycle + ChipSlotCycles;
                    return;
                }

                NextInputCycle = inputCycle + ChipSlotCycles;
            }

            internal void HoldThrough(long cycle)
            {
                if (Pending && NextInputCycle <= cycle)
                {
                    NextInputCycle = AgnusChipSlotScheduler.AlignToSlot(cycle + 1);
                }
            }
        }

        private bool UsesAreaWordRetireControl
            => _bus.Chipset == AmigaChipset.OcsPal &&
                !_lineMode &&
                !_useA &&
                !_useB &&
                !_useC &&
                _useD &&
                !_fillEnabled;

        private void ArmAreaWordRetireControl(
            long outputCycle,
            bool eligible,
            bool isFinalWord,
            AgnusHrmSlotEngine? observationSlots = null)
        {
            if (!eligible ||
                isFinalWord ||
                _areaWordRetireControl.Armed)
            {
                return;
            }

            _areaWordRetireControl.Arm(outputCycle);
            AdvanceAreaWordRetireControlInput(outputCycle, observationSlots);
        }

        private void AdvanceAreaWordRetireControlInput(
            long inputCycle,
            AgnusHrmSlotEngine? observationSlots = null)
        {
            System.Diagnostics.Debug.Assert(_areaWordRetireControl.Pending);
            var available = IsBlitterDmaEnabled() &&
                _bus.CausalBusExecutor.CanAdvanceBlitterControlAt(
                    inputCycle,
                    observationSlots);
            _areaWordRetireControl.Advance(inputCycle, available);
            if (_areaWordRetireControl.Accepted)
            {
                _areaMicroOpNextCycle = Math.Max(
                    _areaMicroOpNextCycle,
                    _areaWordRetireControl.RetirementCycle);
            }
        }

        private bool AdvanceAreaWordRetireControlThrough(
            long targetCycle,
            AgnusHrmSlotEngine? observationSlots = null)
        {
            var advanced = false;
            while (_areaWordRetireControl.Pending)
            {
                var inputCycle = _areaWordRetireControl.NextInputCycle;
                if (observationSlots is null)
                {
                    inputCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(
                        inputCycle,
                        Math.Max(
                            _bus.ExecutedChipBusHorizon,
                            _bus.LiveSlotKernelCommittedCpuThroughCycle)));
                }

                if (inputCycle > targetCycle)
                {
                    return advanced;
                }

                AdvanceAreaWordRetireControlInput(inputCycle, observationSlots);
                advanced = true;
            }

            return advanced;
        }

        private void HoldAreaWordRetireControlThrough(long cycle)
            => _areaWordRetireControl.HoldThrough(cycle);

        private void RebaseAreaWordRetireControlAt(long cycle)
        {
            if (_areaWordRetireControl.Pending &&
                _areaWordRetireControl.NextInputCycle < cycle)
            {
                _areaWordRetireControl.NextInputCycle =
                    AgnusChipSlotScheduler.AlignToSlot(cycle);
            }
        }
    }
}
