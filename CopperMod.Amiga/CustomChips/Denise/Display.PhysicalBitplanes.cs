/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal sealed partial class Display
    {
        private BitplaneDmaPipeline _physicalBitplanePipeline;

        private bool UsesPhysicalBitplanePipeline => _chipset == AmigaChipset.OcsPal;

        private long GetNextPhysicalBitplaneCycle()
        {
            if (_physicalBitplanePipeline.HasOutput)
            {
                return _physicalBitplanePipeline.OutputCycle;
            }

            return IsLiveBitplaneDmaEnabled() && NormalizeLiveBitplaneFetchCursor()
                ? GetBitplaneFetchCycle(in _liveBitplaneFetchTimeline.Captured)
                : long.MaxValue;
        }

        private void CapturePhysicalBitplaneBatch(long stopCycle)
        {
            while (true)
            {
                var cycle = GetNextPhysicalBitplaneCycle();
                if (cycle > stopCycle)
                {
                    return;
                }

                if (StepPhysicalBitplane(cycle))
                {
                    AdvanceLiveFetchCursor();
                }
                InvalidateLiveWorkCycle();
            }
        }

        // True means the old input cursor is consumed. The input itself leaves
        // it in place until OUT; a causal rebase may independently replace it.
        private bool StepPhysicalBitplane(long cycle)
        {
            if (_physicalBitplanePipeline.HasOutput)
            {
                var issued = _physicalBitplanePipeline.Complete(cycle);
                var word = issued.Word;
                if (!issued.Granted)
                {
                    _bitplaneDmaReadLatch = BitplaneDmaReadLatch.Denied(
                        word.Row, word.Plane, word.StorageWord,
                        issued.Access.Request.Address, word.AddressValid, cycle);
                }
                else if (_liveBitplaneRequester != null)
                {
                    _bitplaneDmaReadLatch = LoadAcceptedBitplaneDmaLatchThroughContract(in issued);
                }
                else
                {
                    var access = issued.Access;
                    var result = _bus.ExecuteAcceptedBitplaneWord(word.Plane, in access);
                    _bitplaneDmaReadLatch = new BitplaneDmaReadLatch(
                        word.Row, word.Plane, word.StorageWord,
                        access.Request.Address, addressValid: true,
                        result.Value, granted: true, cycle);
                }

                ConsumeLiveBitplaneDmaLatch(ref _bitplaneDmaReadLatch);
                if (word.Plan.IsPlanBacked)
                {
                    // Count the entry frozen at IN, including a denied word.
                    // Its row-plan storage may already describe a new suffix.
                    _lastRowDmaBitplaneEntriesExecuted++;
                }
                CompletePhysicalBitplanePointer(in issued);
                return _liveNextFetchRow == word.Row &&
                    _liveNextFetchWord == word.Word && _liveNextFetchSlot == word.Slot;
            }

            var row = _liveNextFetchRow;
            var plane = _liveNextFetchPlane;
            var logicalWord = _liveNextFetchWord;
            var state = GetLiveLineState(row);
            EnsureActiveRowBitplaneDmaPlanCurrent(row);
            var storageWord = logicalWord + state.BitplaneWordIndexOffsets[plane];
            if ((uint)storageWord >= (uint)MaxBitplaneFetchWords)
            {
                return true;
            }

            var addressValid = (state.PlaneHasRowMask & (1 << plane)) != 0;
            var address = addressValid
                ? AddDmaPointerOffset(state.BitplaneRowAddresses[plane], logicalWord * 2)
                : 0u;
            var provenance = default(BitplaneDmaPlanProvenance);
            if (TryGetValidRowDmaPlan(row, state, out var plan, recordFallback: false) &&
                TryFindNextRowDmaBitplaneEntry(plan, logicalWord, _liveNextFetchSlot, out var entryIndex))
            {
                var entry = _rowDmaBitplaneEntries[entryIndex];
                if (IsPhysicalBitplanePlanInput(
                        in plan, in entry, state, row, plane, logicalWord, _liveNextFetchSlot, cycle))
                {
                    addressValid = entry.RowPresent;
                    address = entry.Address;
                    provenance = new BitplaneDmaPlanProvenance(
                        IsPlanBacked: true, plan.Generation, plan.DmaPlanVersion, plan.Signature, entryIndex);
                    _rowDmaBitplaneCursorIndices[GetRasterlineRingSlot(row)] = entryIndex + 1;
                }
            }

            var lastWord = logicalWord == state.FetchWords - 1;
            var metadata = new BitplaneDmaWordMetadata(
                row, plane, logicalWord, storageWord, _liveNextFetchSlot,
                addressValid, lastWord,
                lastWord ? ((plane & 1) == 0 ? _bpl1mod : _bpl2mod) : (short)0)
            {
                Plan = provenance
            };
            var accepted = default(AmigaBusAccessResult);
            var granted = addressValid && _bus.TryAcceptBitplaneInput(address, cycle, out accepted);
            var request = new AmigaBusAccessRequest(
                AmigaBusRequester.Bitplane, AmigaBusAccessKind.Bitplane,
                AmigaBusAccessTarget.ChipRam, address, AmigaBusAccessSize.Word,
                cycle, isWrite: false);
            var outputCycle = checked(cycle + CopperHpCycles);
            var accessResult = granted ? accepted : new AmigaBusAccessResult(request, outputCycle, outputCycle);
            _physicalBitplanePipeline.Accept(in metadata, in accessResult, granted);
            return false;
        }

        private static bool IsPhysicalBitplanePlanInput(
            in RowDmaPlan plan,
            in RowDmaBitplaneEntry entry,
            LiveLineState state,
            int row,
            int plane,
            int word,
            int slot,
            long inputCycle)
            => plan.Valid && plan.Row == row && plan.Generation == state.Generation &&
                plan.DmaPlanVersion == state.DmaPlanVersion &&
                plan.LineStartCycle == state.LineStartCycle && entry.DelayedOutput &&
                entry.Plane == plane && entry.Word == word && entry.Slot == slot &&
                entry.GetCycle(plan.LineStartCycle) == inputCycle;

        private void RebaseFuturePhysicalBitplanePointer(int row, int plane, long cycle)
        {
            _physicalBitplanePipeline.ObservePointerWrite(plane);
            var state = GetLiveLineState(row);
            var nextWord = GetFirstBitplaneWordAfterCycle(state, cycle, plane);
            state.BitplanePointers[plane] = _bitplanePointers[plane];
            state.BitplaneBaseRows[plane] = row;
            state.BitplaneRowAddresses[plane] = AddDmaPointerOffset(
                _bitplanePointers[plane], -(nextWord * 2));
            state.PlaneHasRowMask |= (byte)(1 << plane);
            AdvanceRowDmaPlanVersion(state);
            InvalidateRowDmaPlan(row);
            InvalidateLiveWorkCycle();
        }

        private void CompletePhysicalBitplanePointer(in BitplaneDmaPipeline issued)
        {
            if (!issued.Granted || (!issued.Word.LastWord && !issued.PointerWrittenAfterInput))
            {
                return;
            }

            var word = issued.Word;
            var pointer = AddDmaPointerOffset(issued.Access.Request.Address, 2 + word.Modulo);
            _bitplanePointers[word.Plane] = pointer;
            _bitplaneBaseRows[word.Plane] = word.LastWord ? word.Row + 1 : word.Row;
            _agnusRegisters.SetBitplanePointerFromDma(word.Plane, pointer, issued.OutputCycle);
            _bus.CausalBusExecutor.RecordBitplaneRowPointerAdvance(word.Plane, pointer, issued.OutputCycle);
            if (!word.LastWord)
            {
                var state = GetLiveLineState(word.Row);
                state.BitplanePointers[word.Plane] = pointer;
                state.BitplaneBaseRows[word.Plane] = word.Row;
                var nextWord = GetFirstBitplaneWordAfterCycle(state, issued.OutputCycle, word.Plane);
                state.BitplaneRowAddresses[word.Plane] = AddDmaPointerOffset(pointer, -(nextWord * 2));
                AdvanceRowDmaPlanVersion(state);
                InvalidateRowDmaPlan(word.Row);
            }
        }
    }
}
