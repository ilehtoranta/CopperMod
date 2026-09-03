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

        // Destination-only startup clocks six non-reserving control inputs
        // before the first pipelined D output. Each input observes the next
        // physical OUT phase. A blocked OUT retries only that input, so dense
        // display DMA stretches startup without changing the later D cadence.
        private struct DOnlyStartupControlState
        {
            internal int RemainingStages;
            internal long NextInputCycle;

            internal readonly bool Pending => RemainingStages != 0;

            internal void Begin(long startCycle)
            {
                RemainingStages = 6;
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
            if (!_dOnlyStartup.Pending)
            {
                PrimeDOnlyStartupWord(inputCycle);
            }
        }

        // The six explicit startup inputs include the initial destination
        // pipeline fill. Materialize that completed, non-bus word at the last
        // accepted input so the first physical D request is the following CCK.
        private void PrimeDOnlyStartupWord(long completionCycle)
        {
            System.Diagnostics.Debug.Assert(
                _bus.Chipset == AmigaChipset.OcsPal &&
                !_lineMode && !_useA && !_useB && !_useC && _useD &&
                !_fillEnabled && !_areaMicroOpActive &&
                !_liveAreaPendingDValid);

            BeginDmaRollbackSnapshot();
            _areaMicroOpActive = true;
            _areaMicroOpOwnedByBoundedAdvance = false;
            _areaMicroOpIndex = 0;
            _areaMicroOpStepStart = completionCycle;
            _areaMicroOpStepEnd = completionCycle;
            _areaMicroOpNextReadCycle = completionCycle;
            _areaMicroOpNextCycle = completionCycle;
            _areaMicroOpInternalCompletionCycle = completionCycle;
            _areaMicroOpRawA = _activeDataA;
            _areaMicroOpRawB = _activeDataB;
            _areaMicroOpRawC = _activeDataC;
            _areaMicroOpMask = GetCurrentAreaWordMask();
            _areaMicroOpOutput = 0;
            _areaMicroOpOutputReady = false;
            _areaMicroOpFinalWord =
                _rowY == _height - 1 && _wordX == _widthWords - 1;
            EnsureAreaMicroOpOutputReady();
            LatchLiveAreaPipelineWrite();
            _areaMicroOpIndex = GetAreaMicroOpCount();
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
