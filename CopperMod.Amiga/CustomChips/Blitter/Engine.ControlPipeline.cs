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
        private BOnlyStartupControlState _bOnlyStartup;
        private DOnlyStartupControlState _dOnlyStartup;

        // Startup clocks the sequencer on accepted Agnus inputs. Its idle
        // stages carry no memory transfer and do not reserve their OUT slots.
        // Keep this value state separate from the first word's physical OUT
        // cursor so a stalled input cannot be hidden by a scalar start delay.
        private struct BOnlyStartupControlState
        {
            internal int RemainingStages;
            internal long NextInputCycle;

            internal readonly bool Pending => RemainingStages != 0;

            internal readonly long EarliestWordOutputCycle
                => NextInputCycle + ((RemainingStages + 1L) * ChipSlotCycles);

            internal void Begin(long startCycle)
            {
                RemainingStages = 2;
                NextInputCycle = AgnusChipSlotScheduler.AlignToSlot(
                    startCycle + ChipSlotCycles);
            }

            internal void Advance(long inputCycle, bool inputAvailable)
            {
                System.Diagnostics.Debug.Assert(Pending && inputCycle >= NextInputCycle);
                if (inputAvailable)
                {
                    RemainingStages--;
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

        // Destination-only startup has one control input at the normal area
        // startup cursor. The input itself owns no bus slot. If its outgoing
        // phase is unavailable, the input is retried on the next Agnus clock;
        // an accepted input leaves the first D request on the existing area
        // word timeline.
        private struct DOnlyStartupControlState
        {
            internal bool Pending;
            internal long NextInputCycle;

            internal void Begin(long inputCycle)
            {
                Pending = true;
                NextInputCycle = AgnusChipSlotScheduler.AlignToSlot(inputCycle);
            }

            internal void Advance(long inputCycle, bool inputAvailable)
            {
                System.Diagnostics.Debug.Assert(Pending && inputCycle >= NextInputCycle);
                if (inputAvailable)
                {
                    Pending = false;
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

        private bool HasAreaStartupControl
            => _bOnlyStartup.Pending || _dOnlyStartup.Pending;

        private long GetNextAreaStartupInputCycle()
            => _bOnlyStartup.Pending
                ? _bOnlyStartup.NextInputCycle
                : _dOnlyStartup.Pending
                    ? _dOnlyStartup.NextInputCycle
                    : long.MaxValue;

        private void AdvanceBOnlyStartupInput(long inputCycle)
        {
            _bOnlyStartup.Advance(inputCycle,
                _bus.CausalBusExecutor.CanAdvanceBlitterControlAt(inputCycle));
            _currentCycle = _bOnlyStartup.Pending
                ? _bOnlyStartup.NextInputCycle
                : _bOnlyStartup.EarliestWordOutputCycle;
        }

        private void AdvanceAreaStartupInput(long inputCycle)
        {
            if (_bOnlyStartup.Pending)
            {
                AdvanceBOnlyStartupInput(inputCycle);
                return;
            }

            System.Diagnostics.Debug.Assert(_dOnlyStartup.Pending);
            _dOnlyStartup.Advance(inputCycle,
                _bus.CausalBusExecutor.CanAdvanceBlitterControlAt(inputCycle));
            _currentCycle = _dOnlyStartup.Pending
                ? _dOnlyStartup.NextInputCycle
                : inputCycle;
        }

        private void HoldBOnlyStartupThrough(long cycle)
        {
            _bOnlyStartup.HoldThrough(cycle);
            if (_bOnlyStartup.Pending)
            {
                _currentCycle = Math.Max(_currentCycle, _bOnlyStartup.NextInputCycle);
            }
        }

        private void HoldAreaStartupThrough(long cycle)
        {
            HoldBOnlyStartupThrough(cycle);
            _dOnlyStartup.HoldThrough(cycle);
            if (_dOnlyStartup.Pending)
            {
                _currentCycle = Math.Max(_currentCycle, _dOnlyStartup.NextInputCycle);
            }
        }

    }
}
