/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.CustomChips.Denise
{
    internal enum CpuWaitFixedSlotOwner : byte
    {
        Free,
        Refresh,
        BitplaneRead,
        SpriteRead
    }

    internal enum CpuWaitFixedSlotImageUnsupported : byte
    {
        None,
        Frame,
        Copper,
        PendingWrite,
        RasterlinePlan,
        SpriteState,
        Unstable
    }

    internal readonly struct CpuWaitFixedSlotTimelineSignature : IEquatable<CpuWaitFixedSlotTimelineSignature>
    {
        internal CpuWaitFixedSlotTimelineSignature(
            int slotCount,
            ulong hash,
            long firstSlotCycle,
            long lastSlotCycle,
            AgnusChipSlotOwner firstOwner,
            AgnusChipSlotOwner lastOwner)
        {
            SlotCount = slotCount;
            Hash = hash;
            FirstSlotCycle = firstSlotCycle;
            LastSlotCycle = lastSlotCycle;
            FirstOwner = firstOwner;
            LastOwner = lastOwner;
        }

        internal int SlotCount { get; }
        internal ulong Hash { get; }
        internal long FirstSlotCycle { get; }
        internal long LastSlotCycle { get; }
        internal AgnusChipSlotOwner FirstOwner { get; }
        internal AgnusChipSlotOwner LastOwner { get; }

        public bool Equals(CpuWaitFixedSlotTimelineSignature other)
            => SlotCount == other.SlotCount &&
                Hash == other.Hash &&
                FirstSlotCycle == other.FirstSlotCycle &&
                LastSlotCycle == other.LastSlotCycle &&
                FirstOwner == other.FirstOwner &&
                LastOwner == other.LastOwner;

        public override bool Equals(object? obj)
            => obj is CpuWaitFixedSlotTimelineSignature other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(SlotCount, Hash, FirstSlotCycle, LastSlotCycle, FirstOwner, LastOwner);

        public override string ToString()
            => $"{SlotCount}@{FirstSlotCycle}->{LastSlotCycle}/0x{Hash:X16}/{FirstOwner}->{LastOwner}";
    }

    internal struct BlitterFixedSlotImageCursor
    {
        internal int Generation;
        internal ulong WakeVersion;
        internal int BeamLine;
        internal long LineStart;
        internal bool Valid;

        internal void Clear()
        {
            Generation = 0;
            WakeVersion = 0;
            BeamLine = -1;
            LineStart = 0;
            Valid = false;
        }
    }

    internal sealed partial class Display
    {
        private const int CpuWaitFixedSlotsPerLine = CanonicalLineCycles / AgnusChipSlotScheduler.SlotCycles;
        private readonly byte[] _cpuWaitFixedSlotOwners =
            new byte[AmigaConstants.A500PalRasterLines * CpuWaitFixedSlotsPerLine];
        private readonly int[] _cpuWaitFixedSlotGenerations = new int[AmigaConstants.A500PalRasterLines];
        private readonly int[] _cpuWaitFixedSlotSignatures = new int[AmigaConstants.A500PalRasterLines];
        private readonly long[] _cpuWaitFixedSlotLineStarts = new long[AmigaConstants.A500PalRasterLines];
        private readonly bool[] _cpuWaitFixedSlotImageHasSpriteOwners = new bool[AmigaConstants.A500PalRasterLines];
        private long _cpuWaitFixedSlotImageBuilds;
        private long _cpuWaitFixedSlotImageHits;
        private long _cpuWaitFixedSlotImageMisses;
        private long _cpuWaitFixedSlotImageInvalidations;
        private long _cpuWaitFixedSlotImagePredictedSlots;
        private long _cpuWaitFixedSlotImageUnsupportedFrame;
        private long _cpuWaitFixedSlotImageUnsupportedCopper;
        private long _cpuWaitFixedSlotImageUnsupportedPendingWrite;
        private long _cpuWaitFixedSlotImageUnsupportedRasterlinePlan;
        private long _cpuWaitFixedSlotImageUnsupportedSpriteState;
        private bool _cpuWaitFixedSlotImageDiagnosticsEnabled;

        internal long CpuWaitFixedSlotImageBuilds => _cpuWaitFixedSlotImageBuilds;
        internal long CpuWaitFixedSlotImageHits => _cpuWaitFixedSlotImageHits;
        internal long CpuWaitFixedSlotImageMisses => _cpuWaitFixedSlotImageMisses;
        internal long CpuWaitFixedSlotImageInvalidations => _cpuWaitFixedSlotImageInvalidations;
        internal long CpuWaitFixedSlotImagePredictedSlots => _cpuWaitFixedSlotImagePredictedSlots;
        internal long CpuWaitFixedSlotImageUnsupportedFrame => _cpuWaitFixedSlotImageUnsupportedFrame;
        internal long CpuWaitFixedSlotImageUnsupportedCopper => _cpuWaitFixedSlotImageUnsupportedCopper;
        internal long CpuWaitFixedSlotImageUnsupportedPendingWrite => _cpuWaitFixedSlotImageUnsupportedPendingWrite;
        internal long CpuWaitFixedSlotImageUnsupportedRasterlinePlan => _cpuWaitFixedSlotImageUnsupportedRasterlinePlan;
        internal long CpuWaitFixedSlotImageUnsupportedSpriteState => _cpuWaitFixedSlotImageUnsupportedSpriteState;

        internal void SetCpuWaitFixedSlotImageDiagnosticsEnabled(bool enabled)
            => _cpuWaitFixedSlotImageDiagnosticsEnabled = enabled;

        internal void ResetCpuWaitFixedSlotImageDiagnostics()
        {
            _cpuWaitFixedSlotImageBuilds = 0;
            _cpuWaitFixedSlotImageHits = 0;
            _cpuWaitFixedSlotImageMisses = 0;
            _cpuWaitFixedSlotImageInvalidations = 0;
            _cpuWaitFixedSlotImagePredictedSlots = 0;
            _cpuWaitFixedSlotImageUnsupportedFrame = 0;
            _cpuWaitFixedSlotImageUnsupportedCopper = 0;
            _cpuWaitFixedSlotImageUnsupportedPendingWrite = 0;
            _cpuWaitFixedSlotImageUnsupportedRasterlinePlan = 0;
            _cpuWaitFixedSlotImageUnsupportedSpriteState = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetCpuWaitFixedSlotOwner(
            long slotCycle,
            out CpuWaitFixedSlotOwner owner,
            out CpuWaitFixedSlotImageUnsupported unsupported)
        {
            if (!_timing.IsCanonicalPal)
            {
                owner = CpuWaitFixedSlotOwner.Free;
                unsupported = CpuWaitFixedSlotImageUnsupported.Frame;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedFrame++;
                }
                return false;
            }

            slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, slotCycle));
            if (_bus.IsMandatoryRefreshSlot(slotCycle))
            {
                owner = CpuWaitFixedSlotOwner.Refresh;
                unsupported = CpuWaitFixedSlotImageUnsupported.None;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImagePredictedSlots++;
                }
                return true;
            }

            if (!TryGetCpuWaitFixedSlotLine(slotCycle, out var beamLine, out var lineStart, out var row))
            {
                owner = CpuWaitFixedSlotOwner.Free;
                unsupported = CpuWaitFixedSlotImageUnsupported.Frame;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedFrame++;
                }
                return false;
            }

            if (!TryEnsureCpuWaitFixedSlotImage(
                    beamLine,
                    lineStart,
                    row,
                    lineStart + LineCycles - 1,
                    out unsupported))
            {
                owner = CpuWaitFixedSlotOwner.Free;
                return false;
            }

            var slot = (int)((slotCycle - lineStart) / AgnusChipSlotScheduler.SlotCycles);
            owner = (CpuWaitFixedSlotOwner)_cpuWaitFixedSlotOwners[
                (beamLine * CpuWaitFixedSlotsPerLine) + slot];
            if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
            {
                _cpuWaitFixedSlotImagePredictedSlots++;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetBlitterFixedSlotOwner(
            long slotCycle,
            ref BlitterFixedSlotImageCursor cursor,
            out CpuWaitFixedSlotOwner owner,
            out CpuWaitFixedSlotImageUnsupported unsupported)
        {
            if (!_timing.IsCanonicalPal)
            {
                owner = CpuWaitFixedSlotOwner.Free;
                unsupported = CpuWaitFixedSlotImageUnsupported.Frame;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedFrame++;
                }
                cursor.Clear();
                return false;
            }

            slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, slotCycle));
            if (_bus.IsMandatoryRefreshSlot(slotCycle))
            {
                owner = CpuWaitFixedSlotOwner.Refresh;
                unsupported = CpuWaitFixedSlotImageUnsupported.None;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImagePredictedSlots++;
                }
                return true;
            }

            return TryGetBlitterFixedDisplaySlotOwner(
                slotCycle,
                ref cursor,
                out owner,
                out unsupported);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetBlitterFixedDisplaySlotOwner(
            long slotCycle,
            ref BlitterFixedSlotImageCursor cursor,
            out CpuWaitFixedSlotOwner owner,
            out CpuWaitFixedSlotImageUnsupported unsupported)
        {
            if (!_timing.IsCanonicalPal)
            {
                owner = CpuWaitFixedSlotOwner.Free;
                unsupported = CpuWaitFixedSlotImageUnsupported.Frame;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedFrame++;
                }
                cursor.Clear();
                return false;
            }

            System.Diagnostics.Debug.Assert(
                slotCycle == AgnusChipSlotScheduler.AlignToSlot(slotCycle),
                "Blitter display candidate must already be aligned to an Agnus slot.");
            System.Diagnostics.Debug.Assert(
                !_bus.IsMandatoryRefreshSlot(slotCycle),
                "Mandatory refresh must be rejected before display ownership lookup.");

            if (cursor.Valid &&
                cursor.Generation == _liveGeneration &&
                cursor.WakeVersion == _liveWakeVersion &&
                slotCycle >= cursor.LineStart &&
                slotCycle < cursor.LineStart + LineCycles)
            {
                var slot = (int)((slotCycle - cursor.LineStart) / AgnusChipSlotScheduler.SlotCycles);
                owner = (CpuWaitFixedSlotOwner)_cpuWaitFixedSlotOwners[
                    (cursor.BeamLine * CpuWaitFixedSlotsPerLine) + slot];
                unsupported = CpuWaitFixedSlotImageUnsupported.None;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageHits++;
                    _cpuWaitFixedSlotImagePredictedSlots++;
                }
                return true;
            }

            cursor.Clear();
            if (!TryGetCpuWaitFixedSlotLine(slotCycle, out var beamLine, out var lineStart, out var row))
            {
                owner = CpuWaitFixedSlotOwner.Free;
                unsupported = CpuWaitFixedSlotImageUnsupported.Frame;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedFrame++;
                }
                return false;
            }

            if (!TryEnsureCpuWaitFixedSlotImage(
                    beamLine,
                    lineStart,
                    row,
                    slotCycle,
                    out unsupported))
            {
                owner = CpuWaitFixedSlotOwner.Free;
                return false;
            }

            var ownerSlot = (int)((slotCycle - lineStart) / AgnusChipSlotScheduler.SlotCycles);
            owner = (CpuWaitFixedSlotOwner)_cpuWaitFixedSlotOwners[
                (beamLine * CpuWaitFixedSlotsPerLine) + ownerSlot];
            if (!_cpuWaitFixedSlotImageHasSpriteOwners[beamLine])
            {
                cursor.Generation = _liveGeneration;
                cursor.WakeVersion = _liveWakeVersion;
                cursor.BeamLine = beamLine;
                cursor.LineStart = lineStart;
                cursor.Valid = true;
            }

            if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
            {
                _cpuWaitFixedSlotImagePredictedSlots++;
            }
            return true;
        }

        private bool TryGetCpuWaitFixedSlotLine(long cycle, out int beamLine, out long lineStart, out int row)
        {
            beamLine = -1;
            lineStart = 0;
            row = -1;
            if (!_liveDmaEnabled || !_liveFrameValid || cycle < _liveFrameStartCycle || cycle >= GetLiveFrameStopCycle())
            {
                return false;
            }

            var frameCycle = cycle - _liveFrameStartCycle;
            beamLine = (int)(frameCycle / LineCycles);
            if ((uint)beamLine >= AmigaConstants.A500PalRasterLines)
            {
                return false;
            }

            lineStart = _liveFrameStartCycle + ((long)beamLine * LineCycles);
            row = beamLine - StandardVStart;
            return (uint)row < LowResOutputHeight;
        }

        private bool TryEnsureCpuWaitFixedSlotImage(
            int beamLine,
            long lineStart,
            int row,
            long barrierHorizon,
            out CpuWaitFixedSlotImageUnsupported unsupported)
        {
            if (GetNextLiveCopperBarrierCycle() <= barrierHorizon)
            {
                unsupported = CpuWaitFixedSlotImageUnsupported.Copper;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedCopper++;
                }
                return false;
            }

            if (GetNextLivePendingWriteCycle() <= barrierHorizon)
            {
                unsupported = CpuWaitFixedSlotImageUnsupported.PendingWrite;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedPendingWrite++;
                }
                return false;
            }

            var state = _liveLineStates[row];
            var plan = _rowDmaPlans[row];
            if (state.Generation != _liveGeneration ||
                !plan.Valid ||
                plan.Generation != _liveGeneration ||
                plan.Row != row ||
                plan.Signature != ComputeRowDmaPlanSignature(state))
            {
                unsupported = CpuWaitFixedSlotImageUnsupported.RasterlinePlan;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedRasterlinePlan++;
                }
                return false;
            }

            var signature = ComputeCpuWaitFixedSlotImageSignature(state, plan, row);
            if (_cpuWaitFixedSlotGenerations[beamLine] == _liveGeneration &&
                _cpuWaitFixedSlotSignatures[beamLine] == signature &&
                _cpuWaitFixedSlotLineStarts[beamLine] == lineStart)
            {
                unsupported = CpuWaitFixedSlotImageUnsupported.None;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageHits++;
                }
                return true;
            }

            if (plan.SpriteCount > 0 && _liveNextSpriteRow < row)
            {
                unsupported = CpuWaitFixedSlotImageUnsupported.SpriteState;
                if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
                {
                    _cpuWaitFixedSlotImageUnsupportedSpriteState++;
                }
                return false;
            }

            if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
            {
                _cpuWaitFixedSlotImageMisses++;
                if (_cpuWaitFixedSlotGenerations[beamLine] != 0)
                {
                    _cpuWaitFixedSlotImageInvalidations++;
                }
            }

            var ownerBase = beamLine * CpuWaitFixedSlotsPerLine;
            Array.Clear(_cpuWaitFixedSlotOwners, ownerBase, CpuWaitFixedSlotsPerLine);
            _cpuWaitFixedSlotImageHasSpriteOwners[beamLine] = false;
            var bitplaneEnd = plan.BitplaneStart + plan.BitplaneCount;
            for (var index = plan.BitplaneStart; index < bitplaneEnd; index++)
            {
                var entry = _rowDmaBitplaneEntries[index];
                if (entry.RowPresent)
                {
                    SetCpuWaitFixedSlotOwner(ownerBase, lineStart, entry.GetCycle(state.LineStartCycle), CpuWaitFixedSlotOwner.BitplaneRead);
                }
            }

            var spriteEnd = plan.SpriteStart + plan.SpriteCount;
            for (var index = plan.SpriteStart; index < spriteEnd; index++)
            {
                var entry = _rowDmaSpriteEntries[index];
                if (IsCpuWaitFixedImagePendingSpriteEntry(row, entry) &&
                    WouldCpuWaitFixedImageSpriteFetch(row, entry.SpriteIndex, entry.Word))
                {
                    SetCpuWaitFixedSlotOwner(ownerBase, lineStart, entry.Cycle, CpuWaitFixedSlotOwner.SpriteRead);
                    _cpuWaitFixedSlotImageHasSpriteOwners[beamLine] = true;
                }
            }

            _cpuWaitFixedSlotGenerations[beamLine] = _liveGeneration;
            _cpuWaitFixedSlotSignatures[beamLine] = signature;
            _cpuWaitFixedSlotLineStarts[beamLine] = lineStart;
            if (_cpuWaitFixedSlotImageDiagnosticsEnabled)
            {
                _cpuWaitFixedSlotImageBuilds++;
            }
            unsupported = CpuWaitFixedSlotImageUnsupported.None;
            return true;
        }

        private bool IsCpuWaitFixedImagePendingSpriteEntry(int row, RowDmaSpriteEntry entry)
        {
            if (row > _liveNextSpriteRow)
            {
                return true;
            }

            if (row < _liveNextSpriteRow)
            {
                return false;
            }

            return entry.SpriteIndex > _liveNextSpriteIndex ||
                entry.SpriteIndex == _liveNextSpriteIndex && entry.Word >= _liveNextSpriteWord;
        }

        private bool WouldCpuWaitFixedImageSpriteFetch(int row, int spriteIndex, int word)
        {
            var lineState = _liveLineStates[row];
            if ((uint)spriteIndex >= (uint)_liveSpriteDmaStates.Length ||
                !IsSpriteDmaSlotAvailable(lineState, spriteIndex, word) ||
                _liveSpriteDmaExhausted[spriteIndex])
            {
                return false;
            }

            var state = _liveSpriteDmaStates[spriteIndex];
            if (state.Exhausted)
            {
                return false;
            }

            if (!state.Active)
            {
                return row == state.ControlRow;
            }

            return row >= state.Descriptor.YStart && row < state.Descriptor.YStop;
        }

        private int ComputeCpuWaitFixedSlotImageSignature(LiveLineState state, RowDmaPlan plan, int row)
        {
            unchecked
            {
                var hash = (plan.Signature * 397) ^ row;
                hash = (hash * 397) ^ _liveNextFetchRow;
                hash = (hash * 397) ^ _liveNextFetchWord;
                hash = (hash * 397) ^ _liveNextFetchSlot;
                hash = (hash * 397) ^ _livePreparedFetchRow;
                hash = (hash * 397) ^ _livePreparedFetchWord;
                hash = (hash * 397) ^ _livePreparedFetchSlot;
                hash = (hash * 397) ^ _liveNextSpriteRow;
                hash = (hash * 397) ^ _liveNextSpriteIndex;
                hash = (hash * 397) ^ _liveNextSpriteWord;
                for (var sprite = 0; sprite < _liveSpriteDmaStates.Length; sprite++)
                {
                    var spriteState = _liveSpriteDmaStates[sprite];
                    hash = (hash * 397) ^ (spriteState.Active ? 1 : 0);
                    hash = (hash * 397) ^ (spriteState.Exhausted ? 1 : 0);
                    hash = (hash * 397) ^ spriteState.ControlRow;
                    hash = (hash * 397) ^ spriteState.Descriptor.YStart;
                    hash = (hash * 397) ^ spriteState.Descriptor.YStop;
                }

                return hash;
            }
        }

        private void SetCpuWaitFixedSlotOwner(
            int ownerBase,
            long lineStart,
            long cycle,
            CpuWaitFixedSlotOwner owner)
        {
            var slot = (int)((cycle - lineStart) / AgnusChipSlotScheduler.SlotCycles);
            if ((uint)slot < CpuWaitFixedSlotsPerLine)
            {
                _cpuWaitFixedSlotOwners[ownerBase + slot] = (byte)owner;
            }
        }
    }
}
