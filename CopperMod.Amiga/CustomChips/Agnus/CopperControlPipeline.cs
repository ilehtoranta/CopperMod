/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;

namespace CopperMod.Amiga.CustomChips.Agnus
{
    internal enum CopperControlStage : byte
    {
        ReadFirst,
        ReadSecond,
        WaitIdle,
        WaitComparison,
        SkipIdle,
        SkipDelay,
        SkipComparison,
        VblankInhibitedRead
    }

    internal enum CopperInputAction : byte
    {
        None,
        ControlAdvanced,
        WaitSatisfied,
        ReadFirst,
        ReadSecond,
        SkipAndReadFirst,
        DummyRead,
        VblankInhibitedRead
    }

    internal enum CopperOutputAction : byte
    {
        Discarded,
        FirstWord,
        Instruction,
        VblankReload
    }

    internal enum CopperVblankLatchAction : byte
    {
        ReadOldPointer,
        ReloadPointer,
        AwaitDmaEnable
    }

    /// <summary>
    /// Device-local Copper control and its one issued RGA transfer. Control
    /// advances on input phases; RAM is sampled separately on the frozen output
    /// phase. Copying this value preserves both the decoder and the physical tail.
    /// </summary>
    internal struct CopperControlPipeline
    {
        public CopperControlStage Stage { get; private set; }
        public bool HasIssuedWord { get; private set; }
        public CopperInputAction IssuedPurpose { get; private set; }
        public long AcceptedInputCycle { get; private set; }
        public AmigaBusAccessResult IssuedAccess { get; private set; }
        public ushort FirstWord { get; private set; }
        public AmigaBusAccessResult FirstAccess { get; private set; }
        public long ReadIntentCycle { get; private set; }

        private uint _decodeGeneration;
        private uint _issuedGeneration;
        private bool _hasReadIntent;
        private bool _waitHeadPending;
        private bool _vblankScheduled;
        private long _vblankStrobeCycle;
        private long _vblankFrameStartCycle;
        private bool _vblankLatchPending;
        private long _vblankLatchCycle;
        private CopperVblankLatchAction _vblankLatchAction;
        private bool _vblankLatchSamplesPointer;

        public long LastVblankFrameStartCycle { get; private set; }
        public uint VblankReadAddress { get; private set; }
        public uint VblankReloadPointer { get; private set; }
        public bool HasVblankControlWork => _vblankScheduled || _vblankLatchPending;
        public long NextVblankControlCycle => _vblankLatchPending
            ? _vblankLatchCycle
            : _vblankScheduled ? _vblankStrobeCycle : long.MaxValue;

        public bool IsWaiting => Stage is CopperControlStage.WaitIdle or
            CopperControlStage.WaitComparison;

        public bool NeedsComparison => Stage is CopperControlStage.WaitComparison or
            CopperControlStage.SkipComparison;

        public bool IsInstructionReadReady => Stage is CopperControlStage.ReadFirst or
            CopperControlStage.ReadSecond or CopperControlStage.SkipComparison;

        public bool IsReadInputReady => IsInstructionReadReady ||
            Stage == CopperControlStage.VblankInhibitedRead;

        public void Restart()
        {
            // A jump changes the decoder destination, not an accepted RGA
            // transfer. Its output must still occur, but cannot decode into
            // the new instruction stream.
            _decodeGeneration++;
            Stage = CopperControlStage.ReadFirst;
            FirstWord = 0;
            FirstAccess = default;
            _hasReadIntent = false;
            _waitHeadPending = false;
            _vblankLatchPending = false;
        }

        public void ScheduleVblankRestart(long frameStartCycle, long strobeCycle)
        {
            if (strobeCycle < 0 || frameStartCycle <= strobeCycle ||
                strobeCycle != AgnusChipSlotScheduler.AlignToSlot(strobeCycle))
            {
                throw new ArgumentOutOfRangeException(nameof(strobeCycle));
            }
            _vblankFrameStartCycle = frameStartCycle;
            _vblankStrobeCycle = strobeCycle;
            _vblankScheduled = true;
        }

        public bool IsVblankStrobeDue(long cycle)
            => _vblankScheduled && cycle == _vblankStrobeCycle;

        public bool IsVblankLatchDue(long cycle)
            => _vblankLatchPending && cycle == _vblankLatchCycle;

        public void BeginVblankRestart(
            long cycle,
            uint oldReadAddress,
            uint reloadPointer,
            bool dmaEnabled,
            bool wasStopped)
        {
            if (!IsVblankStrobeDue(cycle) ||
                HasIssuedWord && IssuedAccess.GrantedCycle != cycle + AgnusChipSlotScheduler.SlotCycles)
            {
                throw new InvalidOperationException("Copper vblank strobe must follow its current input phase.");
            }

            // A satisfied WAIT does not become a loading-IR1 state until
            // its first word is accepted. SKIP makes that same transition
            // on its combined compare/input phase.
            var entryStage = HasIssuedWord && IssuedPurpose == CopperInputAction.SkipAndReadFirst
                ? CopperControlStage.ReadFirst
                : Stage == CopperControlStage.ReadFirst && _waitHeadPending
                    ? CopperControlStage.WaitComparison
                    : Stage;
            _vblankScheduled = false;
            LastVblankFrameStartCycle = _vblankFrameStartCycle;
            VblankReadAddress = oldReadAddress;
            VblankReloadPointer = reloadPointer;
            _vblankLatchCycle = cycle + AgnusChipSlotScheduler.SlotCycles;
            _vblankLatchPending = true;
            _vblankLatchSamplesPointer = !HasIssuedWord &&
                (wasStopped || entryStage is not (CopperControlStage.ReadFirst or CopperControlStage.ReadSecond));
            _vblankLatchAction = !dmaEnabled
                ? CopperVblankLatchAction.AwaitDmaEnable
                : !wasStopped && entryStage == CopperControlStage.ReadFirst
                    ? CopperVblankLatchAction.ReloadPointer
                    : CopperVblankLatchAction.ReadOldPointer;
            // The strobe changes decoding, never the physical transfer. A word
            // accepted on this input still reaches OUT, but cannot execute an
            // old MOVE. Its completion precedes the nonreserving latch step.
            _decodeGeneration++;
            FirstWord = 0;
            FirstAccess = default;
            _hasReadIntent = false;
            _waitHeadPending = false;
        }

        public CopperVblankLatchAction CompleteVblankLatch(long cycle, uint listPointer)
        {
            if (!IsVblankLatchDue(cycle) || HasIssuedWord)
            {
                throw new InvalidOperationException("Copper vblank latch must follow the retained output.");
            }
            _vblankLatchPending = false;
            if (_vblankLatchSamplesPointer)
            {
                VblankReloadPointer = listPointer;
            }
            Stage = _vblankLatchAction == CopperVblankLatchAction.ReadOldPointer
                ? CopperControlStage.VblankInhibitedRead
                : CopperControlStage.ReadFirst;
            return _vblankLatchAction;
        }

        public void ObserveReadIntent(long cycle)
        {
            if (IsReadInputReady && !_hasReadIntent)
            {
                ReadIntentCycle = cycle;
                _hasReadIntent = true;
            }
        }

        public void BeginWait() => Stage = CopperControlStage.WaitIdle;

        public void BeginSkip() => Stage = CopperControlStage.SkipIdle;

        public CopperInputAction AdvanceInput(
            AgnusCopperDmaPhaseKind phase,
            bool inputAvailable,
            bool comparisonSatisfied)
        {
            if (HasIssuedWord || _vblankLatchPending || !inputAvailable)
            {
                return CopperInputAction.None;
            }

            if (phase == AgnusCopperDmaPhaseKind.WrapDummy)
            {
                return IsInstructionReadReady
                    ? CopperInputAction.DummyRead
                    : CopperInputAction.None;
            }

            if (phase != AgnusCopperDmaPhaseKind.Normal)
            {
                return CopperInputAction.None;
            }

            switch (Stage)
            {
                case CopperControlStage.ReadFirst:
                    return CopperInputAction.ReadFirst;
                case CopperControlStage.ReadSecond:
                    return CopperInputAction.ReadSecond;
                case CopperControlStage.VblankInhibitedRead:
                    return CopperInputAction.VblankInhibitedRead;
                case CopperControlStage.WaitIdle:
                    Stage = CopperControlStage.WaitComparison;
                    return CopperInputAction.ControlAdvanced;
                case CopperControlStage.WaitComparison:
                    if (!comparisonSatisfied)
                    {
                        return CopperInputAction.None;
                    }
                    Stage = CopperControlStage.ReadFirst;
                    _waitHeadPending = true;
                    return CopperInputAction.WaitSatisfied;
                case CopperControlStage.SkipIdle:
                    Stage = CopperControlStage.SkipDelay;
                    return CopperInputAction.ControlAdvanced;
                case CopperControlStage.SkipDelay:
                    Stage = CopperControlStage.SkipComparison;
                    return CopperInputAction.ControlAdvanced;
                case CopperControlStage.SkipComparison:
                    return CopperInputAction.SkipAndReadFirst;
                default:
                    throw new InvalidOperationException("Unknown Copper control stage.");
            }
        }

        public void AcceptWord(
            CopperInputAction purpose,
            long inputCycle,
            in AmigaBusAccessResult access)
        {
            var purposeMatchesStage = purpose switch
            {
                CopperInputAction.ReadFirst => Stage == CopperControlStage.ReadFirst,
                CopperInputAction.ReadSecond => Stage == CopperControlStage.ReadSecond,
                CopperInputAction.SkipAndReadFirst => Stage == CopperControlStage.SkipComparison,
                CopperInputAction.DummyRead => IsInstructionReadReady && access.Request.Address == 0,
                CopperInputAction.VblankInhibitedRead => Stage == CopperControlStage.VblankInhibitedRead &&
                    access.Request.Address == VblankReadAddress,
                _ => false
            };
            if (HasIssuedWord || !purposeMatchesStage || inputCycle < 0 ||
                inputCycle != AgnusChipSlotScheduler.AlignToSlot(inputCycle) ||
                access.Request.Requester != AmigaBusRequester.Copper ||
                access.Request.Kind != AmigaBusAccessKind.Copper ||
                access.Request.Target != AmigaBusAccessTarget.ChipRam ||
                access.Request.Size != AmigaBusAccessSize.Word || access.Request.IsWrite ||
                access.RequestedCycle < 0 || access.RequestedCycle > inputCycle ||
                access.GrantedCycle != inputCycle + AgnusChipSlotScheduler.SlotCycles ||
                access.CompletedCycle != access.GrantedCycle + AgnusChipSlotScheduler.SlotCycles)
            {
                throw new InvalidOperationException("Invalid Copper input/output transfer.");
            }

            HasIssuedWord = true;
            IssuedPurpose = purpose;
            AcceptedInputCycle = inputCycle;
            IssuedAccess = access;
            _issuedGeneration = _decodeGeneration;
            if (purpose != CopperInputAction.DummyRead)
            {
                _hasReadIntent = false;
                _waitHeadPending = false;
            }
        }

        public CopperOutputAction CompleteWord(ushort value, long outputCycle)
        {
            if (!HasIssuedWord || outputCycle != IssuedAccess.GrantedCycle)
            {
                throw new InvalidOperationException("Copper output must use its accepted physical cycle.");
            }

            HasIssuedWord = false;
            if (_issuedGeneration != _decodeGeneration ||
                IssuedPurpose == CopperInputAction.DummyRead)
            {
                return CopperOutputAction.Discarded;
            }

            if (IssuedPurpose == CopperInputAction.VblankInhibitedRead)
            {
                Stage = CopperControlStage.ReadFirst;
                return CopperOutputAction.VblankReload;
            }

            if (IssuedPurpose is CopperInputAction.ReadFirst or CopperInputAction.SkipAndReadFirst)
            {
                FirstWord = value;
                FirstAccess = IssuedAccess;
                Stage = CopperControlStage.ReadSecond;
                return CopperOutputAction.FirstWord;
            }

            Stage = CopperControlStage.ReadFirst;
            return CopperOutputAction.Instruction;
        }
    }
}
