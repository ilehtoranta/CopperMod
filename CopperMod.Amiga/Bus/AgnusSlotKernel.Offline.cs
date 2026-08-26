/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal readonly struct FixedSlotPlanEntry
    {
        public FixedSlotPlanEntry(AgnusChipSlotOwner owner, byte channel, byte phase)
        {
            if (owner is not (AgnusChipSlotOwner.Free or
                AgnusChipSlotOwner.Bitplane or
                AgnusChipSlotOwner.Sprite))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(owner),
                    owner,
                    "The offline fixed plan contains only bitplane, sprite, or free slots.");
            }

            Owner = owner;
            Channel = channel;
            Phase = phase;
        }

        public AgnusChipSlotOwner Owner { get; }

        public byte Channel { get; }

        public byte Phase { get; }

        public bool Occupied => Owner != AgnusChipSlotOwner.Free;

        public AgnusChipSlotPriority Priority
            => Owner switch
            {
                AgnusChipSlotOwner.Bitplane => AgnusChipSlotPriority.Bitplane,
                AgnusChipSlotOwner.Sprite => AgnusChipSlotPriority.Sprite,
                _ => AgnusChipSlotPriority.Cpu
            };
    }

    internal readonly struct AgnusOfflineFixedSlotPatch
    {
        public AgnusOfflineFixedSlotPatch(
            long slotCycle,
            FixedSlotPlanEntry entry,
            uint address = 0)
        {
            SlotCycle = slotCycle;
            Entry = entry;
            Address = address;
        }

        public long SlotCycle { get; }

        public FixedSlotPlanEntry Entry { get; }

        public uint Address { get; }
    }

    internal readonly struct BusControlMutation
    {
        public BusControlMutation(
            long cycle,
            ushort register,
            ushort value,
            int patchStart,
            int patchCount)
        {
            Cycle = cycle;
            Register = register;
            Value = value;
            PatchStart = patchStart;
            PatchCount = patchCount;
        }

        public long Cycle { get; }

        public ushort Register { get; }

        public ushort Value { get; }

        public int PatchStart { get; }

        public int PatchCount { get; }
    }

    // The production Copper interpreter is the semantics oracle. Offline traces
    // normalize its decoded action into this bounded contract so this slot
    // requester never reinterprets custom-register numbers or instruction bits.
    internal enum AgnusOfflineCopperAction : byte
    {
        Move,
        Wait,
        Skip,
        Copjmp,
        End
    }

    internal readonly struct AgnusOfflineCopperInstruction
    {
        public AgnusOfflineCopperInstruction(
            uint pc,
            long firstRequestedCycle,
            long firstGrantedCycle,
            long secondRequestedCycle,
            long secondGrantedCycle,
            ushort firstWord,
            ushort secondWord,
            AgnusOfflineCopperAction action,
            long transitionCycle,
            uint nextPc,
            long nextRequestedCycle,
            bool comparisonSatisfied,
            bool waitForBlitter,
            bool commitMove,
            ushort moveRegister,
            ushort moveValue,
            bool preserveSecondWordPhysicalPhase,
            int patchStart,
            int patchCount)
        {
            Pc = pc;
            FirstRequestedCycle = firstRequestedCycle;
            FirstGrantedCycle = firstGrantedCycle;
            SecondRequestedCycle = secondRequestedCycle;
            SecondGrantedCycle = secondGrantedCycle;
            FirstWord = firstWord;
            SecondWord = secondWord;
            Action = action;
            TransitionCycle = transitionCycle;
            NextPc = nextPc;
            NextRequestedCycle = nextRequestedCycle;
            ComparisonSatisfied = comparisonSatisfied;
            WaitForBlitter = waitForBlitter;
            CommitMove = commitMove;
            MoveRegister = moveRegister;
            MoveValue = moveValue;
            PreserveSecondWordPhysicalPhase = preserveSecondWordPhysicalPhase;
            PatchStart = patchStart;
            PatchCount = patchCount;
        }

        public uint Pc { get; }

        public long FirstRequestedCycle { get; }

        public long FirstGrantedCycle { get; }

        public long SecondRequestedCycle { get; }

        public long SecondGrantedCycle { get; }

        public ushort FirstWord { get; }

        public ushort SecondWord { get; }

        public AgnusOfflineCopperAction Action { get; }

        public long TransitionCycle { get; }

        public uint NextPc { get; }

        public long NextRequestedCycle { get; }

        public bool ComparisonSatisfied { get; }

        public bool WaitForBlitter { get; }

        public bool CommitMove { get; }

        public ushort MoveRegister { get; }

        public ushort MoveValue { get; }

        public bool PreserveSecondWordPhysicalPhase { get; }

        public int PatchStart { get; }

        public int PatchCount { get; }
    }

    internal readonly struct AgnusOfflineCopperTransitionRecord :
        IEquatable<AgnusOfflineCopperTransitionRecord>
    {
        public AgnusOfflineCopperTransitionRecord(
            int instructionIndex,
            uint pc,
            long firstRequestedCycle,
            long firstGrantedCycle,
            long firstCompletedCycle,
            long secondRequestedCycle,
            long secondGrantedCycle,
            long secondCompletedCycle,
            ushort firstWord,
            ushort secondWord,
            AgnusOfflineCopperAction action,
            long transitionCycle,
            uint nextPc,
            long nextRequestedCycle,
            bool comparisonSatisfied,
            bool waitForBlitter,
            bool moveSuppressed,
            bool mutationCommitted,
            bool waiting,
            bool stopped)
        {
            InstructionIndex = instructionIndex;
            Pc = pc;
            FirstRequestedCycle = firstRequestedCycle;
            FirstGrantedCycle = firstGrantedCycle;
            FirstCompletedCycle = firstCompletedCycle;
            SecondRequestedCycle = secondRequestedCycle;
            SecondGrantedCycle = secondGrantedCycle;
            SecondCompletedCycle = secondCompletedCycle;
            FirstWord = firstWord;
            SecondWord = secondWord;
            Action = action;
            TransitionCycle = transitionCycle;
            NextPc = nextPc;
            NextRequestedCycle = nextRequestedCycle;
            ComparisonSatisfied = comparisonSatisfied;
            WaitForBlitter = waitForBlitter;
            MoveSuppressed = moveSuppressed;
            MutationCommitted = mutationCommitted;
            Waiting = waiting;
            Stopped = stopped;
        }

        public int InstructionIndex { get; }

        public uint Pc { get; }

        public long FirstRequestedCycle { get; }

        public long FirstGrantedCycle { get; }

        public long FirstCompletedCycle { get; }

        public long SecondRequestedCycle { get; }

        public long SecondGrantedCycle { get; }

        public long SecondCompletedCycle { get; }

        public ushort FirstWord { get; }

        public ushort SecondWord { get; }

        public AgnusOfflineCopperAction Action { get; }

        public long TransitionCycle { get; }

        public uint NextPc { get; }

        public long NextRequestedCycle { get; }

        public bool ComparisonSatisfied { get; }

        public bool WaitForBlitter { get; }

        public bool MoveSuppressed { get; }

        public bool MutationCommitted { get; }

        public bool Waiting { get; }

        public bool Stopped { get; }

        public bool Equals(AgnusOfflineCopperTransitionRecord other)
            => InstructionIndex == other.InstructionIndex &&
                Pc == other.Pc &&
                FirstRequestedCycle == other.FirstRequestedCycle &&
                FirstGrantedCycle == other.FirstGrantedCycle &&
                FirstCompletedCycle == other.FirstCompletedCycle &&
                SecondRequestedCycle == other.SecondRequestedCycle &&
                SecondGrantedCycle == other.SecondGrantedCycle &&
                SecondCompletedCycle == other.SecondCompletedCycle &&
                FirstWord == other.FirstWord &&
                SecondWord == other.SecondWord &&
                Action == other.Action &&
                TransitionCycle == other.TransitionCycle &&
                NextPc == other.NextPc &&
                NextRequestedCycle == other.NextRequestedCycle &&
                ComparisonSatisfied == other.ComparisonSatisfied &&
                WaitForBlitter == other.WaitForBlitter &&
                MoveSuppressed == other.MoveSuppressed &&
                MutationCommitted == other.MutationCommitted &&
                Waiting == other.Waiting &&
                Stopped == other.Stopped;

        public override bool Equals(object? obj)
            => obj is AgnusOfflineCopperTransitionRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                InstructionIndex,
                Pc,
                FirstRequestedCycle,
                FirstGrantedCycle,
                FirstWord,
                SecondWord,
                Action,
                TransitionCycle);
    }

    internal readonly struct AgnusOfflineCopperMutationRecord :
        IEquatable<AgnusOfflineCopperMutationRecord>
    {
        public AgnusOfflineCopperMutationRecord(
            int instructionIndex,
            long cycle,
            AgnusOfflineCopperAction action,
            ushort register,
            ushort value,
            int patchCount)
        {
            InstructionIndex = instructionIndex;
            Cycle = cycle;
            Action = action;
            Register = register;
            Value = value;
            PatchCount = patchCount;
        }

        public int InstructionIndex { get; }

        public long Cycle { get; }

        public AgnusOfflineCopperAction Action { get; }

        public ushort Register { get; }

        public ushort Value { get; }

        public int PatchCount { get; }

        public bool Equals(AgnusOfflineCopperMutationRecord other)
            => InstructionIndex == other.InstructionIndex &&
                Cycle == other.Cycle &&
                Action == other.Action &&
                Register == other.Register &&
                Value == other.Value &&
                PatchCount == other.PatchCount;

        public override bool Equals(object? obj)
            => obj is AgnusOfflineCopperMutationRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(InstructionIndex, Cycle, Action, Register, Value, PatchCount);
    }

    // BLTCON0/1 decoding, masks, shifts, fill, pointer/modulo stepping, and
    // line drawing stay in the production Blitter. The offline boundary sees
    // only the ordered DMA operation that the existing implementation emitted.
    internal enum AgnusOfflineBlitterMicroOpKind : byte
    {
        AreaReadA,
        AreaReadB,
        AreaReadC,
        AreaWriteD,
        LineReadB,
        LineReadC,
        LineWriteD
    }

    internal readonly struct AgnusOfflineBlitterMicroOp
    {
        public AgnusOfflineBlitterMicroOp(
            AgnusOfflineBlitterMicroOpKind kind,
            uint address,
            ushort oracleValue,
            bool isWrite,
            long requestedCycle,
            long delayAfterPreviousCompletion)
        {
            Kind = kind;
            Address = address;
            OracleValue = oracleValue;
            IsWrite = isWrite;
            RequestedCycle = requestedCycle;
            DelayAfterPreviousCompletion = delayAfterPreviousCompletion;
        }

        public AgnusOfflineBlitterMicroOpKind Kind { get; }

        public uint Address { get; }

        public ushort OracleValue { get; }

        public bool IsWrite { get; }

        public long RequestedCycle { get; }

        public long DelayAfterPreviousCompletion { get; }
    }

    internal readonly struct AgnusOfflineBlitterMicroOpRecord :
        IEquatable<AgnusOfflineBlitterMicroOpRecord>
    {
        public AgnusOfflineBlitterMicroOpRecord(
            int index,
            AgnusOfflineBlitterMicroOpKind kind,
            uint address,
            ushort value,
            bool isWrite,
            long requestedCycle,
            long grantedCycle,
            long completedCycle)
        {
            Index = index;
            Kind = kind;
            Address = address;
            Value = value;
            IsWrite = isWrite;
            RequestedCycle = requestedCycle;
            GrantedCycle = grantedCycle;
            CompletedCycle = completedCycle;
        }

        public int Index { get; }

        public AgnusOfflineBlitterMicroOpKind Kind { get; }

        public uint Address { get; }

        public ushort Value { get; }

        public bool IsWrite { get; }

        public long RequestedCycle { get; }

        public long GrantedCycle { get; }

        public long CompletedCycle { get; }

        public bool Equals(AgnusOfflineBlitterMicroOpRecord other)
            => Index == other.Index &&
                Kind == other.Kind &&
                Address == other.Address &&
                Value == other.Value &&
                IsWrite == other.IsWrite &&
                RequestedCycle == other.RequestedCycle &&
                GrantedCycle == other.GrantedCycle &&
                CompletedCycle == other.CompletedCycle;

        public override bool Equals(object? obj)
            => obj is AgnusOfflineBlitterMicroOpRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Index,
                Kind,
                Address,
                Value,
                IsWrite,
                RequestedCycle,
                GrantedCycle,
                CompletedCycle);
    }

    internal readonly struct AgnusOfflineBlitterCompletionRecord :
        IEquatable<AgnusOfflineBlitterCompletionRecord>
    {
        public AgnusOfflineBlitterCompletionRecord(
            long cycle,
            long interruptCycle,
            bool zero,
            uint sourceA,
            uint sourceB,
            uint sourceC,
            uint destinationD)
        {
            Cycle = cycle;
            InterruptCycle = interruptCycle;
            Zero = zero;
            SourceA = sourceA;
            SourceB = sourceB;
            SourceC = sourceC;
            DestinationD = destinationD;
        }

        public long Cycle { get; }

        public long InterruptCycle { get; }

        public bool Zero { get; }

        public uint SourceA { get; }

        public uint SourceB { get; }

        public uint SourceC { get; }

        public uint DestinationD { get; }

        public bool Equals(AgnusOfflineBlitterCompletionRecord other)
            => Cycle == other.Cycle &&
                InterruptCycle == other.InterruptCycle &&
                Zero == other.Zero &&
                SourceA == other.SourceA &&
                SourceB == other.SourceB &&
                SourceC == other.SourceC &&
                DestinationD == other.DestinationD;

        public override bool Equals(object? obj)
            => obj is AgnusOfflineBlitterCompletionRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Cycle,
                InterruptCycle,
                Zero,
                SourceA,
                SourceB,
                SourceC,
                DestinationD);
    }

    // Paula remains the authority for register decoding, byte phases, reloads,
    // manual AUDxDAT playback, modulation, and interrupt timing. The offline
    // boundary receives only the DMA words and semantic transitions emitted by
    // that production state machine.
    internal readonly struct AgnusOfflinePaulaDmaWord
    {
        public AgnusOfflinePaulaDmaWord(
            int channel,
            uint address,
            ushort oracleValue,
            long requestedCycle,
            int nextChannelWordIndex)
        {
            Channel = channel;
            Address = address;
            OracleValue = oracleValue;
            RequestedCycle = requestedCycle;
            NextChannelWordIndex = nextChannelWordIndex;
        }

        public int Channel { get; }

        public uint Address { get; }

        public ushort OracleValue { get; }

        public long RequestedCycle { get; }

        public int NextChannelWordIndex { get; }
    }

    internal readonly struct AgnusOfflinePaulaDmaWordRecord :
        IEquatable<AgnusOfflinePaulaDmaWordRecord>
    {
        public AgnusOfflinePaulaDmaWordRecord(
            int index,
            int channel,
            uint address,
            ushort value,
            long requestedCycle,
            long grantedCycle,
            long completedCycle)
        {
            Index = index;
            Channel = channel;
            Address = address;
            Value = value;
            RequestedCycle = requestedCycle;
            GrantedCycle = grantedCycle;
            CompletedCycle = completedCycle;
        }

        public int Index { get; }

        public int Channel { get; }

        public uint Address { get; }

        public ushort Value { get; }

        public long RequestedCycle { get; }

        public long GrantedCycle { get; }

        public long CompletedCycle { get; }

        public bool Equals(AgnusOfflinePaulaDmaWordRecord other)
            => Index == other.Index &&
                Channel == other.Channel &&
                Address == other.Address &&
                Value == other.Value &&
                RequestedCycle == other.RequestedCycle &&
                GrantedCycle == other.GrantedCycle &&
                CompletedCycle == other.CompletedCycle;

        public override bool Equals(object? obj)
            => obj is AgnusOfflinePaulaDmaWordRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Index,
                Channel,
                Address,
                Value,
                RequestedCycle,
                GrantedCycle,
                CompletedCycle);
    }

    internal enum AgnusOfflinePaulaEventKind : byte
    {
        RegisterWrite,
        DmaEnabled,
        DmaDisabled,
        WordLoaded,
        SampleHigh,
        SampleLow,
        LengthReloaded,
        ManualData,
        Interrupt
    }

    internal readonly struct AgnusOfflinePaulaEventRecord :
        IEquatable<AgnusOfflinePaulaEventRecord>
    {
        public AgnusOfflinePaulaEventRecord(
            long cycle,
            AgnusOfflinePaulaEventKind kind,
            int channel,
            uint location,
            uint currentAddress,
            int lengthWords,
            int remainingWords,
            int period,
            int volume,
            sbyte currentSample,
            bool dmaEnabled,
            ushort dataWord,
            byte state,
            ushort intreq)
        {
            Cycle = cycle;
            Kind = kind;
            Channel = channel;
            Location = location;
            CurrentAddress = currentAddress;
            LengthWords = lengthWords;
            RemainingWords = remainingWords;
            Period = period;
            Volume = volume;
            CurrentSample = currentSample;
            DmaEnabled = dmaEnabled;
            DataWord = dataWord;
            State = state;
            Intreq = intreq;
        }

        public long Cycle { get; }

        public AgnusOfflinePaulaEventKind Kind { get; }

        public int Channel { get; }

        public uint Location { get; }

        public uint CurrentAddress { get; }

        public int LengthWords { get; }

        public int RemainingWords { get; }

        public int Period { get; }

        public int Volume { get; }

        public sbyte CurrentSample { get; }

        public bool DmaEnabled { get; }

        public ushort DataWord { get; }

        public byte State { get; }

        public ushort Intreq { get; }

        public bool Equals(AgnusOfflinePaulaEventRecord other)
            => Cycle == other.Cycle &&
                Kind == other.Kind &&
                Channel == other.Channel &&
                Location == other.Location &&
                CurrentAddress == other.CurrentAddress &&
                LengthWords == other.LengthWords &&
                RemainingWords == other.RemainingWords &&
                Period == other.Period &&
                Volume == other.Volume &&
                CurrentSample == other.CurrentSample &&
                DmaEnabled == other.DmaEnabled &&
                DataWord == other.DataWord &&
                State == other.State &&
                Intreq == other.Intreq;

        public override bool Equals(object? obj)
            => obj is AgnusOfflinePaulaEventRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Cycle, Kind, Channel, CurrentAddress, DataWord);
    }

    // The production disk controller owns serial recovery, WORDSYNC,
    // DSKLEN/DSKPT semantics, and completion. The slot kernel sees only the
    // resulting causal Chip-bus word stream and semantic publications.
    internal readonly struct AgnusOfflineDiskDmaWord
    {
        public AgnusOfflineDiskDmaWord(
            uint address,
            ushort oracleValue,
            bool writeMode,
            long requestedCycle,
            long delayAfterPreviousCompletion)
        {
            Address = address;
            OracleValue = oracleValue;
            WriteMode = writeMode;
            RequestedCycle = requestedCycle;
            DelayAfterPreviousCompletion = delayAfterPreviousCompletion;
        }

        public uint Address { get; }

        public ushort OracleValue { get; }

        public bool WriteMode { get; }

        public long RequestedCycle { get; }

        public long DelayAfterPreviousCompletion { get; }
    }

    internal readonly struct AgnusOfflineDiskDmaWordRecord :
        IEquatable<AgnusOfflineDiskDmaWordRecord>
    {
        public AgnusOfflineDiskDmaWordRecord(
            int index,
            uint address,
            ushort value,
            bool writeMode,
            long requestedCycle,
            long grantedCycle,
            long completedCycle)
        {
            Index = index;
            Address = address;
            Value = value;
            WriteMode = writeMode;
            RequestedCycle = requestedCycle;
            GrantedCycle = grantedCycle;
            CompletedCycle = completedCycle;
        }

        public int Index { get; }

        public uint Address { get; }

        public ushort Value { get; }

        public bool WriteMode { get; }

        public long RequestedCycle { get; }

        public long GrantedCycle { get; }

        public long CompletedCycle { get; }

        public bool Equals(AgnusOfflineDiskDmaWordRecord other)
            => Index == other.Index &&
                Address == other.Address &&
                Value == other.Value &&
                WriteMode == other.WriteMode &&
                RequestedCycle == other.RequestedCycle &&
                GrantedCycle == other.GrantedCycle &&
                CompletedCycle == other.CompletedCycle;

        public override bool Equals(object? obj)
            => obj is AgnusOfflineDiskDmaWordRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Index,
                Address,
                Value,
                WriteMode,
                RequestedCycle,
                GrantedCycle,
                CompletedCycle);
    }

    internal enum AgnusOfflineDiskEventKind : byte
    {
        RegisterWrite,
        DmaStarted,
        DmaWord,
        SyncMatch,
        SyncMissing,
        DmaCancelled,
        DmaStopped,
        DmaCompleted,
        Interrupt
    }

    internal readonly struct AgnusOfflineDiskEventRecord :
        IEquatable<AgnusOfflineDiskEventRecord>
    {
        public AgnusOfflineDiskEventRecord(
            long cycle,
            AgnusOfflineDiskEventKind kind,
            uint diskPointer,
            ushort dsklen,
            ushort dsksync,
            ushort adkcon,
            ushort dskbytr,
            ushort dskdatr,
            bool activeDma,
            bool writeMode,
            int requestedWords,
            int transferredWords,
            int sourceBit,
            ushort shiftRegister,
            byte fifoWords,
            ushort intreq)
        {
            Cycle = cycle;
            Kind = kind;
            DiskPointer = diskPointer;
            Dsklen = dsklen;
            Dsksync = dsksync;
            Adkcon = adkcon;
            Dskbytr = dskbytr;
            Dskdatr = dskdatr;
            ActiveDma = activeDma;
            WriteMode = writeMode;
            RequestedWords = requestedWords;
            TransferredWords = transferredWords;
            SourceBit = sourceBit;
            ShiftRegister = shiftRegister;
            FifoWords = fifoWords;
            Intreq = intreq;
        }

        public long Cycle { get; }

        public AgnusOfflineDiskEventKind Kind { get; }

        public uint DiskPointer { get; }

        public ushort Dsklen { get; }

        public ushort Dsksync { get; }

        public ushort Adkcon { get; }

        public ushort Dskbytr { get; }

        public ushort Dskdatr { get; }

        public bool ActiveDma { get; }

        public bool WriteMode { get; }

        public int RequestedWords { get; }

        public int TransferredWords { get; }

        public int SourceBit { get; }

        public ushort ShiftRegister { get; }

        public byte FifoWords { get; }

        public ushort Intreq { get; }

        public bool Equals(AgnusOfflineDiskEventRecord other)
            => Cycle == other.Cycle &&
                Kind == other.Kind &&
                DiskPointer == other.DiskPointer &&
                Dsklen == other.Dsklen &&
                Dsksync == other.Dsksync &&
                Adkcon == other.Adkcon &&
                Dskbytr == other.Dskbytr &&
                Dskdatr == other.Dskdatr &&
                ActiveDma == other.ActiveDma &&
                WriteMode == other.WriteMode &&
                RequestedWords == other.RequestedWords &&
                TransferredWords == other.TransferredWords &&
                SourceBit == other.SourceBit &&
                ShiftRegister == other.ShiftRegister &&
                FifoWords == other.FifoWords &&
                Intreq == other.Intreq;

        public override bool Equals(object? obj)
            => obj is AgnusOfflineDiskEventRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Cycle, Kind, DiskPointer, Dsklen, Dskdatr);
    }

    internal readonly struct CpuLongRequest
    {
        public CpuLongRequest(
            uint address,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            uint value = 0)
        {
            if (requestedCycle < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedCycle));
            }

            Address = address;
            RequestedCycle = requestedCycle;
            Kind = kind;
            IsWrite = isWrite;
            Value = value;
        }

        public uint Address { get; }

        public long RequestedCycle { get; }

        public AmigaBusAccessKind Kind { get; }

        public bool IsWrite { get; }

        public uint Value { get; }
    }

    internal readonly struct CpuLongResult
    {
        public CpuLongResult(
            uint value,
            long firstGrantedCycle,
            long secondGrantedCycle,
            long completedCycle)
        {
            Value = value;
            FirstGrantedCycle = firstGrantedCycle;
            SecondGrantedCycle = secondGrantedCycle;
            CompletedCycle = completedCycle;
        }

        public uint Value { get; }

        public long FirstGrantedCycle { get; }

        public long SecondGrantedCycle { get; }

        public long CompletedCycle { get; }
    }

    internal readonly struct AgnusCommittedSlotRecord : IEquatable<AgnusCommittedSlotRecord>
    {
        public AgnusCommittedSlotRecord(
            long cycle,
            long requestedCycle,
            long completedCycle,
            AgnusChipSlotOwner owner,
            AmigaBusRequester requester,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            ushort value,
            bool addressValid,
            bool valueValid,
            bool granted,
            bool isWrite,
            byte channel,
            byte phase)
        {
            Cycle = cycle;
            RequestedCycle = requestedCycle;
            CompletedCycle = completedCycle;
            Owner = owner;
            Requester = requester;
            Kind = kind;
            Target = target;
            Address = address;
            Value = value;
            AddressValid = addressValid;
            ValueValid = valueValid;
            Granted = granted;
            IsWrite = isWrite;
            Channel = channel;
            Phase = phase;
        }

        public long Cycle { get; }

        public long RequestedCycle { get; }

        public long CompletedCycle { get; }

        public AgnusChipSlotOwner Owner { get; }

        public AmigaBusRequester Requester { get; }

        public AmigaBusAccessKind Kind { get; }

        public AmigaBusAccessTarget Target { get; }

        public uint Address { get; }

        public ushort Value { get; }

        public bool AddressValid { get; }

        public bool ValueValid { get; }

        public bool Granted { get; }

        public bool IsWrite { get; }

        public byte Channel { get; }

        public byte Phase { get; }

        public bool Equals(AgnusCommittedSlotRecord other)
            => Cycle == other.Cycle &&
                RequestedCycle == other.RequestedCycle &&
                CompletedCycle == other.CompletedCycle &&
                Owner == other.Owner &&
                Requester == other.Requester &&
                Kind == other.Kind &&
                Target == other.Target &&
                Address == other.Address &&
                Value == other.Value &&
                AddressValid == other.AddressValid &&
                ValueValid == other.ValueValid &&
                Granted == other.Granted &&
                IsWrite == other.IsWrite &&
                Channel == other.Channel &&
                Phase == other.Phase;

        public override bool Equals(object? obj)
            => obj is AgnusCommittedSlotRecord other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Cycle,
                RequestedCycle,
                CompletedCycle,
                Owner,
                Requester,
                Kind,
                Target,
                Address);
    }

    internal readonly struct AgnusOfflineReplayComparison
    {
        public AgnusOfflineReplayComparison(
            bool equal,
            int comparedSlots,
            int mismatchIndex,
            AgnusCommittedSlotRecord expected,
            AgnusCommittedSlotRecord actual,
            int copperTransitionMismatchIndex = -1,
            AgnusOfflineCopperTransitionRecord expectedCopperTransition = default,
            AgnusOfflineCopperTransitionRecord actualCopperTransition = default,
            int copperMutationMismatchIndex = -1,
            AgnusOfflineCopperMutationRecord expectedCopperMutation = default,
            AgnusOfflineCopperMutationRecord actualCopperMutation = default,
            int blitterMicroOpMismatchIndex = -1,
            AgnusOfflineBlitterMicroOpRecord expectedBlitterMicroOp = default,
            AgnusOfflineBlitterMicroOpRecord actualBlitterMicroOp = default,
            bool blitterCompletionMismatch = false,
            AgnusOfflineBlitterCompletionRecord expectedBlitterCompletion = default,
            AgnusOfflineBlitterCompletionRecord actualBlitterCompletion = default,
            int paulaDmaWordMismatchIndex = -1,
            AgnusOfflinePaulaDmaWordRecord expectedPaulaDmaWord = default,
            AgnusOfflinePaulaDmaWordRecord actualPaulaDmaWord = default,
            int paulaEventMismatchIndex = -1,
            AgnusOfflinePaulaEventRecord expectedPaulaEvent = default,
            AgnusOfflinePaulaEventRecord actualPaulaEvent = default,
            int diskDmaWordMismatchIndex = -1,
            AgnusOfflineDiskDmaWordRecord expectedDiskDmaWord = default,
            AgnusOfflineDiskDmaWordRecord actualDiskDmaWord = default,
            int diskEventMismatchIndex = -1,
            AgnusOfflineDiskEventRecord expectedDiskEvent = default,
            AgnusOfflineDiskEventRecord actualDiskEvent = default)
        {
            Equal = equal;
            ComparedSlots = comparedSlots;
            MismatchIndex = mismatchIndex;
            Expected = expected;
            Actual = actual;
            CopperTransitionMismatchIndex = copperTransitionMismatchIndex;
            ExpectedCopperTransition = expectedCopperTransition;
            ActualCopperTransition = actualCopperTransition;
            CopperMutationMismatchIndex = copperMutationMismatchIndex;
            ExpectedCopperMutation = expectedCopperMutation;
            ActualCopperMutation = actualCopperMutation;
            BlitterMicroOpMismatchIndex = blitterMicroOpMismatchIndex;
            ExpectedBlitterMicroOp = expectedBlitterMicroOp;
            ActualBlitterMicroOp = actualBlitterMicroOp;
            BlitterCompletionMismatch = blitterCompletionMismatch;
            ExpectedBlitterCompletion = expectedBlitterCompletion;
            ActualBlitterCompletion = actualBlitterCompletion;
            PaulaDmaWordMismatchIndex = paulaDmaWordMismatchIndex;
            ExpectedPaulaDmaWord = expectedPaulaDmaWord;
            ActualPaulaDmaWord = actualPaulaDmaWord;
            PaulaEventMismatchIndex = paulaEventMismatchIndex;
            ExpectedPaulaEvent = expectedPaulaEvent;
            ActualPaulaEvent = actualPaulaEvent;
            DiskDmaWordMismatchIndex = diskDmaWordMismatchIndex;
            ExpectedDiskDmaWord = expectedDiskDmaWord;
            ActualDiskDmaWord = actualDiskDmaWord;
            DiskEventMismatchIndex = diskEventMismatchIndex;
            ExpectedDiskEvent = expectedDiskEvent;
            ActualDiskEvent = actualDiskEvent;
        }

        public bool Equal { get; }

        public int ComparedSlots { get; }

        public int MismatchIndex { get; }

        public AgnusCommittedSlotRecord Expected { get; }

        public AgnusCommittedSlotRecord Actual { get; }

        public int CopperTransitionMismatchIndex { get; }

        public AgnusOfflineCopperTransitionRecord ExpectedCopperTransition { get; }

        public AgnusOfflineCopperTransitionRecord ActualCopperTransition { get; }

        public int CopperMutationMismatchIndex { get; }

        public AgnusOfflineCopperMutationRecord ExpectedCopperMutation { get; }

        public AgnusOfflineCopperMutationRecord ActualCopperMutation { get; }

        public int BlitterMicroOpMismatchIndex { get; }

        public AgnusOfflineBlitterMicroOpRecord ExpectedBlitterMicroOp { get; }

        public AgnusOfflineBlitterMicroOpRecord ActualBlitterMicroOp { get; }

        public bool BlitterCompletionMismatch { get; }

        public AgnusOfflineBlitterCompletionRecord ExpectedBlitterCompletion { get; }

        public AgnusOfflineBlitterCompletionRecord ActualBlitterCompletion { get; }

        public int PaulaDmaWordMismatchIndex { get; }

        public AgnusOfflinePaulaDmaWordRecord ExpectedPaulaDmaWord { get; }

        public AgnusOfflinePaulaDmaWordRecord ActualPaulaDmaWord { get; }

        public int PaulaEventMismatchIndex { get; }

        public AgnusOfflinePaulaEventRecord ExpectedPaulaEvent { get; }

        public AgnusOfflinePaulaEventRecord ActualPaulaEvent { get; }

        public int DiskDmaWordMismatchIndex { get; }

        public AgnusOfflineDiskDmaWordRecord ExpectedDiskDmaWord { get; }

        public AgnusOfflineDiskDmaWordRecord ActualDiskDmaWord { get; }

        public int DiskEventMismatchIndex { get; }

        public AgnusOfflineDiskEventRecord ExpectedDiskEvent { get; }

        public AgnusOfflineDiskEventRecord ActualDiskEvent { get; }
    }

    internal readonly struct AgnusSlotKernelDiagnostics
    {
        public AgnusSlotKernelDiagnostics(
            long slotDecisions,
            long emptySlotDecisions,
            long controlMutations,
            long copperFetches,
            int copperTransitions,
            int copperMutations,
            long blitterMicroOps,
            int blitterCompletions,
            long paulaDmaWords = 0,
            int paulaEvents = 0,
            long diskDmaWords = 0,
            int diskEvents = 0)
        {
            SlotDecisions = slotDecisions;
            EmptySlotDecisions = emptySlotDecisions;
            ControlMutations = controlMutations;
            CopperFetches = copperFetches;
            CopperTransitions = copperTransitions;
            CopperMutations = copperMutations;
            BlitterMicroOps = blitterMicroOps;
            BlitterCompletions = blitterCompletions;
            PaulaDmaWords = paulaDmaWords;
            PaulaEvents = paulaEvents;
            DiskDmaWords = diskDmaWords;
            DiskEvents = diskEvents;
        }

        public long SlotDecisions { get; }

        public long EmptySlotDecisions { get; }

        public long ControlMutations { get; }

        public long CopperFetches { get; }

        public int CopperTransitions { get; }

        public int CopperMutations { get; }

        public long BlitterMicroOps { get; }

        public int BlitterCompletions { get; }

        public long PaulaDmaWords { get; }

        public int PaulaEvents { get; }

        public long DiskDmaWords { get; }

        public int DiskEvents { get; }

        // These remain explicit diagnostic invariants while the kernel is
        // offline-only. There are no production objects to call from this type.
        public long GenericSchedulerDrains => 0;

        public long DeviceWideAdvanceCalls => 0;

        public long ProductionBusCalls => 0;

        public long GenericEventDiscoveries => 0;

        public long VisibilityHorizonQueries => 0;

        public long SpeculativePredictions => 0;

        public long Rollbacks => 0;
    }

    /// <summary>
    /// Bounded, deterministic input and oracle-output storage for the staged
    /// offline Agnus replay through Stage 2.4. It is deliberately not connected
    /// to production.
    /// </summary>
    internal sealed class AgnusOfflineReplayTrace
    {
        public const int DefaultMaximumSlots = 4096;
        public const int DefaultMaximumCpuRequests = 128;
        public const int DefaultMaximumControlMutations = 64;
        public const int DefaultMaximumFixedPatches = 512;
        public const int DefaultMaximumCopperInstructions = 128;
        public const int DefaultMaximumCopperMutations = 64;
        public const int DefaultMaximumBlitterMicroOps = 1024;
        public const int DefaultMaximumPaulaDmaWords = 1024;
        public const int DefaultMaximumPaulaEvents = 2048;
        public const int DefaultMaximumDiskDmaWords = 1024;
        public const int DefaultMaximumDiskEvents = 2048;

        private readonly FixedSlotPlanEntry[] _initialFixedPlan;
        private readonly uint[] _initialFixedAddresses;
        private readonly CpuWordRequest[] _cpuRequests;
        private readonly BusControlMutation[] _controlMutations;
        private readonly AgnusOfflineFixedSlotPatch[] _fixedPatches;
        private readonly AgnusOfflineCopperInstruction[] _copperInstructions;
        private readonly AgnusOfflineCopperTransitionRecord[] _expectedCopperTransitions;
        private readonly AgnusOfflineCopperMutationRecord[] _expectedCopperMutations;
        private readonly AgnusOfflineBlitterMicroOp[] _blitterMicroOps;
        private readonly AgnusOfflineBlitterMicroOpRecord[] _expectedBlitterMicroOps;
        private readonly AgnusOfflinePaulaDmaWord[] _paulaDmaWords;
        private readonly AgnusOfflinePaulaDmaWordRecord[] _expectedPaulaDmaWords;
        private readonly AgnusOfflinePaulaEventRecord[] _expectedPaulaEvents;
        private readonly AgnusOfflineDiskDmaWord[] _diskDmaWords;
        private readonly AgnusOfflineDiskDmaWordRecord[] _expectedDiskDmaWords;
        private readonly AgnusOfflineDiskEventRecord[] _expectedDiskEvents;
        private readonly int[] _firstPaulaWordByChannel = new int[AmigaConstants.PaulaChannelCount];
        private readonly int[] _lastPaulaWordByChannel = new int[AmigaConstants.PaulaChannelCount];
        private readonly AgnusCommittedSlotRecord[] _expectedSlots;
        private int _cpuRequestCount;
        private int _controlMutationCount;
        private int _fixedPatchCount;
        private int _copperInstructionCount;
        private int _expectedCopperMutationCount;
        private int _blitterMicroOpCount;
        private int _paulaDmaWordCount;
        private int _paulaEventCount;
        private int _diskDmaWordCount;
        private int _diskEventCount;
        private long _lastCpuRequestedCycle = -1;
        private long _lastControlMutationCycle = -1;
        private bool _expectedCopperSuppressNextMove;
        private bool _blitterConfigured;
        private bool _blitterNasty;
        private bool _blitterCompletionConfigured;
        private long _blitterStartCycle;
        private long _blitterCompletionDelay;
        private AgnusOfflineBlitterCompletionRecord _expectedBlitterCompletion;

        public AgnusOfflineReplayTrace(
            long firstCycle,
            int slotCount,
            int maximumCpuRequests = DefaultMaximumCpuRequests,
            int maximumControlMutations = DefaultMaximumControlMutations,
            int maximumFixedPatches = DefaultMaximumFixedPatches,
            int maximumCopperInstructions = DefaultMaximumCopperInstructions,
            int maximumCopperMutations = DefaultMaximumCopperMutations,
            int maximumBlitterMicroOps = DefaultMaximumBlitterMicroOps,
            int maximumPaulaDmaWords = DefaultMaximumPaulaDmaWords,
            int maximumPaulaEvents = DefaultMaximumPaulaEvents,
            int maximumDiskDmaWords = DefaultMaximumDiskDmaWords,
            int maximumDiskEvents = DefaultMaximumDiskEvents)
        {
            if (firstCycle < 0 ||
                firstCycle % AgnusChipSlotScheduler.SlotCycles != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firstCycle),
                    "The replay window must begin on an exact physical Chip slot.");
            }

            if (slotCount <= 0 || slotCount > DefaultMaximumSlots)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            }

            if (maximumCpuRequests <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCpuRequests));
            }

            if (maximumControlMutations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumControlMutations));
            }

            if (maximumFixedPatches <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFixedPatches));
            }

            if (maximumCopperInstructions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCopperInstructions));
            }

            if (maximumCopperMutations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCopperMutations));
            }

            if (maximumBlitterMicroOps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBlitterMicroOps));
            }

            if (maximumPaulaDmaWords <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPaulaDmaWords));
            }

            if (maximumPaulaEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPaulaEvents));
            }

            if (maximumDiskDmaWords <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDiskDmaWords));
            }

            if (maximumDiskEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDiskEvents));
            }

            FirstCycle = firstCycle;
            SlotCount = slotCount;
            _initialFixedPlan = new FixedSlotPlanEntry[slotCount];
            _initialFixedAddresses = new uint[slotCount];
            _cpuRequests = new CpuWordRequest[maximumCpuRequests];
            _controlMutations = new BusControlMutation[maximumControlMutations];
            _fixedPatches = new AgnusOfflineFixedSlotPatch[maximumFixedPatches];
            _copperInstructions = new AgnusOfflineCopperInstruction[maximumCopperInstructions];
            _expectedCopperTransitions =
                new AgnusOfflineCopperTransitionRecord[maximumCopperInstructions];
            _expectedCopperMutations =
                new AgnusOfflineCopperMutationRecord[maximumCopperMutations];
            _blitterMicroOps =
                new AgnusOfflineBlitterMicroOp[maximumBlitterMicroOps];
            _expectedBlitterMicroOps =
                new AgnusOfflineBlitterMicroOpRecord[maximumBlitterMicroOps];
            _paulaDmaWords = new AgnusOfflinePaulaDmaWord[maximumPaulaDmaWords];
            _expectedPaulaDmaWords =
                new AgnusOfflinePaulaDmaWordRecord[maximumPaulaDmaWords];
            _expectedPaulaEvents =
                new AgnusOfflinePaulaEventRecord[maximumPaulaEvents];
            _diskDmaWords =
                new AgnusOfflineDiskDmaWord[maximumDiskDmaWords];
            _expectedDiskDmaWords =
                new AgnusOfflineDiskDmaWordRecord[maximumDiskDmaWords];
            _expectedDiskEvents =
                new AgnusOfflineDiskEventRecord[maximumDiskEvents];
            Array.Fill(_firstPaulaWordByChannel, -1);
            Array.Fill(_lastPaulaWordByChannel, -1);
            _expectedSlots = new AgnusCommittedSlotRecord[slotCount];
        }

        public long FirstCycle { get; }

        public int SlotCount { get; }

        public long EndCycle
            => FirstCycle + ((long)(SlotCount - 1) * AgnusChipSlotScheduler.SlotCycles);

        public int CpuRequestCount => _cpuRequestCount;

        public int ControlMutationCount => _controlMutationCount;

        public int FixedPatchCount => _fixedPatchCount;

        public int CopperInstructionCount => _copperInstructionCount;

        public int ExpectedCopperMutationCount => _expectedCopperMutationCount;

        public int BlitterMicroOpCount => _blitterMicroOpCount;

        public int PaulaDmaWordCount => _paulaDmaWordCount;

        public int PaulaEventCount => _paulaEventCount;

        public int DiskDmaWordCount => _diskDmaWordCount;

        public int DiskEventCount => _diskEventCount;

        public bool BlitterConfigured => _blitterConfigured;

        public bool BlitterNasty => _blitterNasty;

        public long BlitterStartCycle => _blitterStartCycle;

        public long BlitterCompletionDelay => _blitterCompletionDelay;

        public bool BlitterCompletionConfigured => _blitterCompletionConfigured;

        public void SetInitialFixedSlot(
            long slotCycle,
            in FixedSlotPlanEntry entry,
            uint address)
        {
            var index = GetSlotIndex(slotCycle);
            _initialFixedPlan[index] = entry;
            _initialFixedAddresses[index] = address;
        }

        public void AddCpuRequest(in CpuWordRequest request)
        {
            if (_cpuRequestCount == _cpuRequests.Length)
            {
                throw new InvalidOperationException("The bounded offline CPU-request buffer is full.");
            }

            if (request.RequestedCycle < _lastCpuRequestedCycle)
            {
                throw new InvalidOperationException("Offline CPU requests must be recorded chronologically.");
            }

            if (request.RequestedCycle > EndCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "The CPU request lies outside the bounded replay window.");
            }

            _cpuRequests[_cpuRequestCount++] = request;
            _lastCpuRequestedCycle = request.RequestedCycle;
        }

        public void AddControlMutation(
            long cycle,
            ushort register,
            ushort value,
            ReadOnlySpan<AgnusOfflineFixedSlotPatch> patches)
        {
            if (cycle < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cycle));
            }

            if (cycle < FirstCycle || cycle > EndCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cycle),
                    "The control mutation lies outside the bounded replay window.");
            }

            if (_controlMutationCount == _controlMutations.Length)
            {
                throw new InvalidOperationException("The bounded offline control-mutation buffer is full.");
            }

            if (cycle < _lastControlMutationCycle)
            {
                throw new InvalidOperationException("Offline control mutations must be recorded chronologically.");
            }

            if (patches.Length > _fixedPatches.Length - _fixedPatchCount)
            {
                throw new InvalidOperationException("The bounded offline fixed-patch buffer is full.");
            }

            var patchStart = _fixedPatchCount;
            for (var index = 0; index < patches.Length; index++)
            {
                var patch = patches[index];
                if (patch.SlotCycle <= cycle)
                {
                    throw new InvalidOperationException(
                        "A control mutation may patch only physical slots after its exact cycle.");
                }

                _ = GetSlotIndex(patch.SlotCycle);
                _fixedPatches[_fixedPatchCount++] = patch;
            }

            _controlMutations[_controlMutationCount++] = new BusControlMutation(
                cycle,
                register,
                value,
                patchStart,
                patches.Length);
            _lastControlMutationCycle = cycle;
        }

        /// <summary>
        /// Records one already-decoded Copper oracle transition. Raw instruction
        /// words remain in the record for memory-sampling parity, but this replay
        /// contract deliberately does not infer action or register semantics from
        /// them.
        /// </summary>
        public void AddCopperInstruction(
            uint pc,
            long firstRequestedCycle,
            long firstGrantedCycle,
            long secondRequestedCycle,
            long secondGrantedCycle,
            ushort firstWord,
            ushort secondWord,
            AgnusOfflineCopperAction action,
            long transitionCycle,
            uint nextPc,
            long nextRequestedCycle,
            bool comparisonSatisfied = false,
            bool waitForBlitter = false,
            bool commitMove = false,
            ushort moveRegister = 0,
            ushort moveValue = 0,
            bool preserveSecondWordPhysicalPhase = false,
            ReadOnlySpan<AgnusOfflineFixedSlotPatch> patches = default)
        {
            if (_copperInstructionCount == _copperInstructions.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline Copper-instruction buffer is full.");
            }

            ValidateCopperCycle(firstRequestedCycle, nameof(firstRequestedCycle));
            ValidateCopperCycle(firstGrantedCycle, nameof(firstGrantedCycle));
            ValidateCopperCycle(secondRequestedCycle, nameof(secondRequestedCycle));
            ValidateCopperCycle(secondGrantedCycle, nameof(secondGrantedCycle));
            ValidateCopperCycle(transitionCycle, nameof(transitionCycle));
            if (firstGrantedCycle < firstRequestedCycle ||
                secondRequestedCycle < firstGrantedCycle + AgnusChipSlotScheduler.SlotCycles ||
                secondGrantedCycle < secondRequestedCycle ||
                transitionCycle < secondGrantedCycle)
            {
                throw new InvalidOperationException(
                    "Offline Copper fetch and transition cycles must be chronological.");
            }

            var isTerminal = action == AgnusOfflineCopperAction.End;
            if (isTerminal)
            {
                if (nextRequestedCycle != -1)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(nextRequestedCycle),
                        "A terminal Copper instruction has no next request.");
                }
            }
            else
            {
                ValidateCopperCycle(
                    nextRequestedCycle,
                    nameof(nextRequestedCycle),
                    allowBeyondWindow: true);
                if (nextRequestedCycle < transitionCycle)
                {
                    throw new InvalidOperationException(
                        "The next Copper request cannot precede the current transition.");
                }
            }

            if (_copperInstructionCount > 0)
            {
                ref readonly var previous = ref _copperInstructions[_copperInstructionCount - 1];
                if (previous.Action == AgnusOfflineCopperAction.End ||
                    previous.NextPc != pc ||
                    previous.NextRequestedCycle != firstRequestedCycle)
                {
                    throw new InvalidOperationException(
                        "Offline Copper instructions must form one exact chronological state chain.");
                }
            }

            if (patches.Length > _fixedPatches.Length - _fixedPatchCount)
            {
                throw new InvalidOperationException("The bounded offline fixed-patch buffer is full.");
            }

            var moveLike = action is
                AgnusOfflineCopperAction.Move or
                AgnusOfflineCopperAction.Copjmp;
            var moveSuppressed = moveLike && _expectedCopperSuppressNextMove;
            var mutationCommitted = moveLike && commitMove && !moveSuppressed;
            if (mutationCommitted &&
                _expectedCopperMutationCount == _expectedCopperMutations.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline Copper-mutation buffer is full.");
            }

            for (var index = 0; index < patches.Length; index++)
            {
                var patch = patches[index];
                if (patch.SlotCycle <= transitionCycle)
                {
                    throw new InvalidOperationException(
                        "A Copper mutation may patch only physical slots after its exact transition.");
                }

                _ = GetSlotIndex(patch.SlotCycle);
            }

            var patchStart = _fixedPatchCount;
            for (var index = 0; index < patches.Length; index++)
            {
                _fixedPatches[_fixedPatchCount++] = patches[index];
            }

            var instructionIndex = _copperInstructionCount;
            if (moveLike)
            {
                _expectedCopperSuppressNextMove = false;
            }

            if (action == AgnusOfflineCopperAction.Skip && comparisonSatisfied)
            {
                _expectedCopperSuppressNextMove = true;
            }

            var instruction = new AgnusOfflineCopperInstruction(
                pc,
                firstRequestedCycle,
                firstGrantedCycle,
                secondRequestedCycle,
                secondGrantedCycle,
                firstWord,
                secondWord,
                action,
                transitionCycle,
                nextPc,
                nextRequestedCycle,
                comparisonSatisfied,
                waitForBlitter,
                commitMove,
                moveRegister,
                moveValue,
                preserveSecondWordPhysicalPhase,
                patchStart,
                patches.Length);
            _copperInstructions[instructionIndex] = instruction;
            _expectedCopperTransitions[instructionIndex] =
                new AgnusOfflineCopperTransitionRecord(
                    instructionIndex,
                    pc,
                    firstRequestedCycle,
                    firstGrantedCycle,
                    firstGrantedCycle + AgnusChipSlotScheduler.SlotCycles,
                    secondRequestedCycle,
                    secondGrantedCycle,
                    secondGrantedCycle + AgnusChipSlotScheduler.SlotCycles,
                    firstWord,
                    secondWord,
                    action,
                    transitionCycle,
                    nextPc,
                    nextRequestedCycle,
                    comparisonSatisfied,
                    waitForBlitter,
                    moveSuppressed,
                    mutationCommitted,
                    waiting: action == AgnusOfflineCopperAction.Wait,
                    stopped: isTerminal);
            _copperInstructionCount++;

            if (!mutationCommitted)
            {
                return;
            }

            _expectedCopperMutations[_expectedCopperMutationCount++] =
                new AgnusOfflineCopperMutationRecord(
                    instructionIndex,
                    transitionCycle,
                    action,
                    moveRegister,
                    moveValue,
                    patches.Length);
        }

        public void StartBlitterReplay(long startCycle, bool nasty)
        {
            if (_blitterConfigured)
            {
                throw new InvalidOperationException(
                    "The bounded offline trace contains only one normalized blitter operation.");
            }

            ValidateBlitterCycle(startCycle, nameof(startCycle));
            _blitterConfigured = true;
            _blitterNasty = nasty;
            _blitterStartCycle = startCycle;
        }

        public void AddBlitterMicroOp(
            AgnusOfflineBlitterMicroOpKind kind,
            uint address,
            ushort oracleValue,
            bool isWrite,
            long requestedCycle,
            long grantedCycle,
            long completedCycle)
        {
            if (!_blitterConfigured)
            {
                throw new InvalidOperationException(
                    "Start the normalized blitter replay before adding micro-operations.");
            }

            if (_blitterCompletionConfigured)
            {
                throw new InvalidOperationException(
                    "Blitter micro-operations cannot be appended after completion.");
            }

            if (_blitterMicroOpCount == _blitterMicroOps.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline blitter micro-operation buffer is full.");
            }

            ValidateBlitterCycle(requestedCycle, nameof(requestedCycle));
            ValidateBlitterCycle(grantedCycle, nameof(grantedCycle));
            ValidateBlitterCycle(completedCycle, nameof(completedCycle), allowBeyondWindow: true);
            if (requestedCycle < _blitterStartCycle ||
                grantedCycle < requestedCycle ||
                completedCycle != grantedCycle + AgnusChipSlotScheduler.SlotCycles)
            {
                throw new InvalidOperationException(
                    "Offline blitter DMA cycles must describe one exact granted physical slot.");
            }

            if (isWrite !=
                (kind is AgnusOfflineBlitterMicroOpKind.AreaWriteD or
                    AgnusOfflineBlitterMicroOpKind.LineWriteD))
            {
                throw new ArgumentException(
                    "Only normalized D micro-operations may write Chip RAM.",
                    nameof(isWrite));
            }

            var delay = requestedCycle - _blitterStartCycle;
            if (_blitterMicroOpCount > 0)
            {
                ref readonly var previous =
                    ref _expectedBlitterMicroOps[_blitterMicroOpCount - 1];
                delay = requestedCycle - previous.CompletedCycle;
                if (delay < 0)
                {
                    throw new InvalidOperationException(
                        "Offline blitter micro-operations must form one causal state chain.");
                }
            }

            var index = _blitterMicroOpCount++;
            _blitterMicroOps[index] = new AgnusOfflineBlitterMicroOp(
                kind,
                address,
                oracleValue,
                isWrite,
                requestedCycle,
                delay);
            _expectedBlitterMicroOps[index] =
                new AgnusOfflineBlitterMicroOpRecord(
                    index,
                    kind,
                    address,
                    oracleValue,
                    isWrite,
                    requestedCycle,
                    grantedCycle,
                    completedCycle);
        }

        public void CompleteBlitterReplay(
            long completionCycle,
            bool zero,
            uint sourceA,
            uint sourceB,
            uint sourceC,
            uint destinationD)
        {
            if (!_blitterConfigured)
            {
                throw new InvalidOperationException(
                    "Start the normalized blitter replay before recording completion.");
            }

            if (_blitterCompletionConfigured)
            {
                throw new InvalidOperationException(
                    "The normalized blitter completion is already recorded.");
            }

            ValidateBlitterCycle(
                completionCycle,
                nameof(completionCycle),
                allowBeyondWindow: false);
            var causalBase = _blitterStartCycle;
            if (_blitterMicroOpCount > 0)
            {
                causalBase =
                    _expectedBlitterMicroOps[_blitterMicroOpCount - 1].CompletedCycle;
            }

            if (completionCycle < causalBase)
            {
                throw new InvalidOperationException(
                    "Blitter completion cannot precede its final causal transition.");
            }

            _blitterCompletionDelay = completionCycle - causalBase;
            _expectedBlitterCompletion =
                new AgnusOfflineBlitterCompletionRecord(
                    completionCycle,
                    completionCycle,
                    zero,
                    sourceA,
                    sourceB,
                    sourceC,
                    destinationD);
            _blitterCompletionConfigured = true;
        }

        public void AddPaulaDmaWord(
            int channel,
            uint address,
            ushort value,
            long requestedCycle,
            long grantedCycle,
            long completedCycle)
        {
            if ((uint)channel >= AmigaConstants.PaulaChannelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }

            if (_paulaDmaWordCount == _paulaDmaWords.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline Paula DMA-word buffer is full.");
            }

            ValidatePaulaCycle(requestedCycle, nameof(requestedCycle));
            ValidatePaulaCycle(grantedCycle, nameof(grantedCycle));
            ValidatePaulaCycle(completedCycle, nameof(completedCycle), allowBeyondWindow: true);
            if (grantedCycle < requestedCycle ||
                completedCycle != grantedCycle + AgnusChipSlotScheduler.SlotCycles ||
                !AgnusHrmOcsSlotTable.IsFixedDmaSlotForOwner(
                    AgnusChipSlotOwner.Paula,
                    grantedCycle,
                    channel))
            {
                throw new ArgumentException(
                    "A Paula word must use its channel's exact HRM slot and one-slot completion.");
            }

            var index = _paulaDmaWordCount++;
            _paulaDmaWords[index] =
                new AgnusOfflinePaulaDmaWord(
                    channel,
                    address,
                    value,
                    requestedCycle,
                    nextChannelWordIndex: -1);
            var previous = _lastPaulaWordByChannel[channel];
            if (previous < 0)
            {
                _firstPaulaWordByChannel[channel] = index;
            }
            else
            {
                var prior = _paulaDmaWords[previous];
                if (requestedCycle < prior.RequestedCycle)
                {
                    throw new InvalidOperationException(
                        "Paula DMA words must be chronological within each channel.");
                }

                _paulaDmaWords[previous] =
                    new AgnusOfflinePaulaDmaWord(
                        prior.Channel,
                        prior.Address,
                        prior.OracleValue,
                        prior.RequestedCycle,
                        index);
            }

            _lastPaulaWordByChannel[channel] = index;
            _expectedPaulaDmaWords[index] =
                new AgnusOfflinePaulaDmaWordRecord(
                    index,
                    channel,
                    address,
                    value,
                    requestedCycle,
                    grantedCycle,
                    completedCycle);
        }

        public void AddPaulaEvent(in AgnusOfflinePaulaEventRecord record)
        {
            if ((uint)record.Channel >= AmigaConstants.PaulaChannelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(record));
            }

            if (_paulaEventCount == _expectedPaulaEvents.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline Paula-event buffer is full.");
            }

            if (record.Cycle < FirstCycle || record.Cycle > EndCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(record),
                    "The Paula event lies outside the bounded replay window.");
            }

            if (_paulaEventCount > 0 &&
                record.Cycle < _expectedPaulaEvents[_paulaEventCount - 1].Cycle)
            {
                throw new InvalidOperationException(
                    "Offline Paula events must be recorded chronologically.");
            }

            _expectedPaulaEvents[_paulaEventCount++] = record;
        }

        public void AddDiskDmaWord(
            uint address,
            ushort value,
            bool writeMode,
            long requestedCycle,
            long grantedCycle,
            long completedCycle)
        {
            if (_diskDmaWordCount == _diskDmaWords.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline disk DMA-word buffer is full.");
            }

            ValidateDiskCycle(requestedCycle, nameof(requestedCycle));
            ValidateDiskCycle(grantedCycle, nameof(grantedCycle));
            ValidateDiskCycle(completedCycle, nameof(completedCycle), allowBeyondWindow: true);
            if (grantedCycle < requestedCycle ||
                completedCycle != grantedCycle + AgnusChipSlotScheduler.SlotCycles ||
                !AgnusHrmOcsSlotTable.IsFixedDmaSlotForOwner(
                    AgnusChipSlotOwner.Disk,
                    grantedCycle))
            {
                throw new ArgumentException(
                    "A disk word must use an exact HRM disk slot and one-slot completion.");
            }

            var delay = requestedCycle - FirstCycle;
            if (_diskDmaWordCount > 0)
            {
                var previous = _expectedDiskDmaWords[_diskDmaWordCount - 1];
                delay = requestedCycle - previous.CompletedCycle;
                if (delay < 0)
                {
                    throw new InvalidOperationException(
                        "Disk DMA words must form one causal chronological chain.");
                }
            }

            var index = _diskDmaWordCount++;
            _diskDmaWords[index] = new AgnusOfflineDiskDmaWord(
                address,
                value,
                writeMode,
                requestedCycle,
                delay);
            _expectedDiskDmaWords[index] =
                new AgnusOfflineDiskDmaWordRecord(
                    index,
                    address,
                    value,
                    writeMode,
                    requestedCycle,
                    grantedCycle,
                    completedCycle);
        }

        public void AddDiskEvent(in AgnusOfflineDiskEventRecord record)
        {
            if (_diskEventCount == _expectedDiskEvents.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline disk-event buffer is full.");
            }

            if (record.Cycle < FirstCycle || record.Cycle > EndCycle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(record),
                    "The disk event lies outside the bounded replay window.");
            }

            if (_diskEventCount > 0 &&
                record.Cycle < _expectedDiskEvents[_diskEventCount - 1].Cycle)
            {
                throw new InvalidOperationException(
                    "Offline disk events must be recorded chronologically.");
            }

            _expectedDiskEvents[_diskEventCount++] = record;
        }

        public void SetExpectedSlot(int index, in AgnusCommittedSlotRecord record)
        {
            if ((uint)index >= (uint)SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            _expectedSlots[index] = record;
        }

        public ref readonly AgnusCommittedSlotRecord GetExpectedSlot(int index)
        {
            if ((uint)index >= (uint)SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _expectedSlots[index];
        }

        public ulong CaptureDeterministicHash()
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            hash = Mix(hash, (ulong)FirstCycle, prime);
            hash = Mix(hash, (uint)SlotCount, prime);
            for (var index = 0; index < _cpuRequestCount; index++)
            {
                var request = _cpuRequests[index];
                hash = Mix(hash, request.Address, prime);
                hash = Mix(hash, (ulong)request.RequestedCycle, prime);
                hash = Mix(hash, (uint)request.Kind, prime);
                hash = Mix(hash, request.IsWrite ? 1u : 0u, prime);
                hash = Mix(hash, request.Value, prime);
            }

            for (var index = 0; index < _controlMutationCount; index++)
            {
                var mutation = _controlMutations[index];
                hash = Mix(hash, (ulong)mutation.Cycle, prime);
                hash = Mix(hash, mutation.Register, prime);
                hash = Mix(hash, mutation.Value, prime);
                hash = Mix(hash, (uint)mutation.PatchCount, prime);
            }

            for (var index = 0; index < _copperInstructionCount; index++)
            {
                var instruction = _copperInstructions[index];
                hash = Mix(hash, instruction.Pc, prime);
                hash = Mix(hash, (ulong)instruction.FirstRequestedCycle, prime);
                hash = Mix(hash, (ulong)instruction.FirstGrantedCycle, prime);
                hash = Mix(hash, (ulong)instruction.SecondRequestedCycle, prime);
                hash = Mix(hash, (ulong)instruction.SecondGrantedCycle, prime);
                hash = Mix(hash, instruction.FirstWord, prime);
                hash = Mix(hash, instruction.SecondWord, prime);
                hash = Mix(hash, (uint)instruction.Action, prime);
                hash = Mix(hash, (ulong)instruction.TransitionCycle, prime);
                hash = Mix(hash, instruction.NextPc, prime);
                hash = Mix(hash, unchecked((ulong)instruction.NextRequestedCycle), prime);
                hash = Mix(hash, instruction.ComparisonSatisfied ? 1u : 0u, prime);
                hash = Mix(hash, instruction.WaitForBlitter ? 1u : 0u, prime);
                hash = Mix(hash, instruction.CommitMove ? 1u : 0u, prime);
                hash = Mix(hash, instruction.MoveRegister, prime);
                hash = Mix(hash, instruction.MoveValue, prime);
                hash = Mix(hash, (uint)instruction.PatchCount, prime);
            }

            hash = Mix(hash, _blitterConfigured ? 1u : 0u, prime);
            hash = Mix(hash, _blitterNasty ? 1u : 0u, prime);
            hash = Mix(hash, unchecked((ulong)_blitterStartCycle), prime);
            hash = Mix(hash, unchecked((ulong)_blitterCompletionDelay), prime);
            for (var index = 0; index < _blitterMicroOpCount; index++)
            {
                var microOp = _blitterMicroOps[index];
                hash = Mix(hash, (uint)microOp.Kind, prime);
                hash = Mix(hash, microOp.Address, prime);
                hash = Mix(hash, microOp.OracleValue, prime);
                hash = Mix(hash, microOp.IsWrite ? 1u : 0u, prime);
                hash = Mix(hash, unchecked((ulong)microOp.RequestedCycle), prime);
                hash = Mix(
                    hash,
                    unchecked((ulong)microOp.DelayAfterPreviousCompletion),
                    prime);
                var expectedMicroOp = _expectedBlitterMicroOps[index];
                hash = Mix(
                    hash,
                    unchecked((ulong)expectedMicroOp.GrantedCycle),
                    prime);
                hash = Mix(
                    hash,
                    unchecked((ulong)expectedMicroOp.CompletedCycle),
                    prime);
            }

            if (_blitterCompletionConfigured)
            {
                hash = Mix(
                    hash,
                    unchecked((ulong)_expectedBlitterCompletion.Cycle),
                    prime);
                hash = Mix(hash, _expectedBlitterCompletion.Zero ? 1u : 0u, prime);
                hash = Mix(hash, _expectedBlitterCompletion.SourceA, prime);
                hash = Mix(hash, _expectedBlitterCompletion.SourceB, prime);
                hash = Mix(hash, _expectedBlitterCompletion.SourceC, prime);
                hash = Mix(hash, _expectedBlitterCompletion.DestinationD, prime);
            }

            for (var index = 0; index < _paulaDmaWordCount; index++)
            {
                var word = _paulaDmaWords[index];
                var expectedWord = _expectedPaulaDmaWords[index];
                hash = Mix(hash, (uint)word.Channel, prime);
                hash = Mix(hash, word.Address, prime);
                hash = Mix(hash, word.OracleValue, prime);
                hash = Mix(hash, unchecked((ulong)word.RequestedCycle), prime);
                hash = Mix(hash, unchecked((ulong)expectedWord.GrantedCycle), prime);
                hash = Mix(hash, unchecked((ulong)expectedWord.CompletedCycle), prime);
            }

            for (var index = 0; index < _paulaEventCount; index++)
            {
                var record = _expectedPaulaEvents[index];
                hash = Mix(hash, unchecked((ulong)record.Cycle), prime);
                hash = Mix(hash, (uint)record.Kind, prime);
                hash = Mix(hash, (uint)record.Channel, prime);
                hash = Mix(hash, record.CurrentAddress, prime);
                hash = Mix(hash, record.DataWord, prime);
                hash = Mix(hash, record.Intreq, prime);
            }

            for (var index = 0; index < _diskDmaWordCount; index++)
            {
                var word = _diskDmaWords[index];
                var expectedWord = _expectedDiskDmaWords[index];
                hash = Mix(hash, word.Address, prime);
                hash = Mix(hash, word.OracleValue, prime);
                hash = Mix(hash, word.WriteMode ? 1u : 0u, prime);
                hash = Mix(hash, unchecked((ulong)word.RequestedCycle), prime);
                hash = Mix(hash, unchecked((ulong)expectedWord.GrantedCycle), prime);
                hash = Mix(hash, unchecked((ulong)expectedWord.CompletedCycle), prime);
            }

            for (var index = 0; index < _diskEventCount; index++)
            {
                var record = _expectedDiskEvents[index];
                hash = Mix(hash, unchecked((ulong)record.Cycle), prime);
                hash = Mix(hash, (uint)record.Kind, prime);
                hash = Mix(hash, record.DiskPointer, prime);
                hash = Mix(hash, record.Dsklen, prime);
                hash = Mix(hash, record.Dskdatr, prime);
                hash = Mix(hash, record.Intreq, prime);
            }

            for (var index = 0; index < SlotCount; index++)
            {
                var expected = _expectedSlots[index];
                hash = Mix(hash, (ulong)expected.Cycle, prime);
                hash = Mix(hash, (uint)expected.Owner, prime);
                hash = Mix(hash, expected.Address, prime);
                hash = Mix(hash, expected.Value, prime);
                hash = Mix(hash, expected.Granted ? 1u : 0u, prime);
            }

            return hash;
        }

        internal FixedSlotPlanEntry GetInitialFixedEntry(int index)
            => _initialFixedPlan[index];

        internal uint GetInitialFixedAddress(int index)
            => _initialFixedAddresses[index];

        internal ref readonly CpuWordRequest GetCpuRequest(int index)
            => ref _cpuRequests[index];

        internal ref readonly BusControlMutation GetControlMutation(int index)
            => ref _controlMutations[index];

        internal ref readonly AgnusOfflineFixedSlotPatch GetFixedPatch(int index)
            => ref _fixedPatches[index];

        internal ref readonly AgnusOfflineCopperInstruction GetCopperInstruction(int index)
            => ref _copperInstructions[index];

        internal ref readonly AgnusOfflineCopperTransitionRecord GetExpectedCopperTransition(
            int index)
            => ref _expectedCopperTransitions[index];

        internal ref readonly AgnusOfflineCopperMutationRecord GetExpectedCopperMutation(int index)
            => ref _expectedCopperMutations[index];

        internal ref readonly AgnusOfflineBlitterMicroOp GetBlitterMicroOp(int index)
            => ref _blitterMicroOps[index];

        internal ref readonly AgnusOfflineBlitterMicroOpRecord GetExpectedBlitterMicroOp(
            int index)
            => ref _expectedBlitterMicroOps[index];

        internal AgnusOfflineBlitterCompletionRecord GetExpectedBlitterCompletion()
        {
            if (!_blitterCompletionConfigured)
            {
                throw new InvalidOperationException(
                    "The normalized blitter replay has no completion record.");
            }

            return _expectedBlitterCompletion;
        }

        internal int GetFirstPaulaWordIndex(int channel)
            => _firstPaulaWordByChannel[channel];

        internal ref readonly AgnusOfflinePaulaDmaWord GetPaulaDmaWord(int index)
            => ref _paulaDmaWords[index];

        internal ref readonly AgnusOfflinePaulaDmaWordRecord GetExpectedPaulaDmaWord(
            int index)
            => ref _expectedPaulaDmaWords[index];

        internal ref readonly AgnusOfflinePaulaEventRecord GetExpectedPaulaEvent(int index)
            => ref _expectedPaulaEvents[index];

        internal ref readonly AgnusOfflineDiskDmaWord GetDiskDmaWord(int index)
            => ref _diskDmaWords[index];

        internal ref readonly AgnusOfflineDiskDmaWordRecord GetExpectedDiskDmaWord(
            int index)
            => ref _expectedDiskDmaWords[index];

        internal ref readonly AgnusOfflineDiskEventRecord GetExpectedDiskEvent(int index)
            => ref _expectedDiskEvents[index];

        internal int GetSlotIndex(long slotCycle)
        {
            if (slotCycle < 0 ||
                slotCycle % AgnusChipSlotScheduler.SlotCycles != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slotCycle),
                    "Fixed-plan cycles must identify an exact physical Chip slot.");
            }

            var delta = slotCycle - FirstCycle;
            if (delta < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCycle));
            }

            var index = delta / AgnusChipSlotScheduler.SlotCycles;
            if ((ulong)index >= (ulong)SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCycle));
            }

            return (int)index;
        }

        private void ValidateCopperCycle(
            long cycle,
            string parameterName,
            bool allowBeyondWindow = false)
        {
            if (cycle < FirstCycle ||
                cycle % AgnusChipSlotScheduler.SlotCycles != 0 ||
                (!allowBeyondWindow && cycle > EndCycle))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Copper cycles must identify an exact physical slot in the bounded replay.");
            }
        }

        private void ValidateBlitterCycle(
            long cycle,
            string parameterName,
            bool allowBeyondWindow = false)
        {
            if (cycle < FirstCycle ||
                cycle % AgnusChipSlotScheduler.SlotCycles != 0 ||
                (!allowBeyondWindow && cycle > EndCycle))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Blitter cycles must identify an exact physical slot in the bounded replay.");
            }
        }

        private void ValidatePaulaCycle(
            long cycle,
            string parameterName,
            bool allowBeyondWindow = false)
        {
            if (cycle < FirstCycle ||
                cycle % AgnusChipSlotScheduler.SlotCycles != 0 ||
                (!allowBeyondWindow && cycle > EndCycle))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Paula cycles must identify an exact physical slot in the bounded replay.");
            }
        }

        private void ValidateDiskCycle(
            long cycle,
            string parameterName,
            bool allowBeyondWindow = false)
        {
            if (cycle < FirstCycle ||
                cycle % AgnusChipSlotScheduler.SlotCycles != 0 ||
                (!allowBeyondWindow && cycle > EndCycle))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Disk cycles must identify an exact physical slot in the bounded replay.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix(ulong hash, ulong value, ulong prime)
            => unchecked((hash ^ value) * prime);
    }

    /// <summary>
    /// Stage 2.4 offline-only A500 PAL OCS arbitration prototype. No production
    /// bus, scheduler, or custom-chip path references this type.
    /// </summary>
    internal sealed class AgnusSlotKernel
    {
        private const int CpuDmaWaitCyclesBeforeNiceBlitterYield = 3;

        private readonly FixedSlotPlanEntry[] _fixedPlan;
        private readonly uint[] _fixedAddresses;
        private readonly AgnusCommittedSlotRecord[] _committedSlots;
        private readonly CpuWordResult[] _cpuResults;
        private readonly AgnusOfflineCopperTransitionRecord[] _copperTransitions;
        private readonly AgnusOfflineCopperMutationRecord[] _copperMutations;
        private readonly AgnusOfflineBlitterMicroOpRecord[] _blitterMicroOps;
        private readonly AgnusOfflinePaulaDmaWordRecord[] _paulaDmaWords;
        private readonly AgnusOfflinePaulaEventRecord[] _paulaEvents;
        private readonly AgnusOfflineDiskDmaWordRecord[] _diskDmaWords;
        private readonly AgnusOfflineDiskEventRecord[] _diskEvents;
        private readonly int[] _nextPaulaWordByChannel = new int[AmigaConstants.PaulaChannelCount];
        private AgnusOfflineReplayTrace? _trace;
        private byte[]? _chipRam;
        private uint _chipRamMask;
        private long _committedSlot = -AgnusChipSlotScheduler.SlotCycles;
        private long _nextSlot;
        private int _committedSlotCount;
        private int _nextCpuRequest;
        private int _nextControlMutation;
        private int _cpuResultCount;
        private int _nextCopperInstruction;
        private int _copperTransitionCount;
        private int _copperMutationCount;
        private int _nextBlitterMicroOp;
        private int _blitterMicroOpCount;
        private int _paulaDmaWordCount;
        private int _nextPaulaEvent;
        private int _paulaEventCount;
        private int _nextDiskDmaWord;
        private int _diskDmaWordCount;
        private int _nextDiskEvent;
        private int _diskEventCount;
        private long _diskRequestedCycle;
        private bool _cpuPending;
        private CpuWordRequest _cpuRequest;
        private long _cpuEarliestEligibleCycle;
        private int _cpuDmaWaitCycles;
        private bool _copperSecondWordPending;
        private uint _copperPc;
        private long _copperRequestedCycle;
        private long _copperFirstRequestedCycle;
        private ushort _copperFirstWord;
        private long _copperFirstGrantedCycle;
        private bool _copperSuppressNextMove;
        private bool _copperWaiting;
        private bool _copperStopped;
        private bool _blitterConfigured;
        private bool _blitterStarted;
        private bool _blitterBusy;
        private bool _blitterNasty;
        private long _blitterRequestedCycle;
        private long _blitterCompletionCycle;
        private int _blitterCompletionCount;
        private AgnusOfflineBlitterCompletionRecord _blitterCompletion;
        private long _slotDecisions;
        private long _emptySlotDecisions;
        private long _controlMutationsApplied;
        private long _copperFetchesCommitted;

        public AgnusSlotKernel(
            AmigaChipset chipset,
            int maximumSlots = AgnusOfflineReplayTrace.DefaultMaximumSlots,
            int maximumCpuRequests = AgnusOfflineReplayTrace.DefaultMaximumCpuRequests,
            int maximumCopperInstructions =
                AgnusOfflineReplayTrace.DefaultMaximumCopperInstructions,
            int maximumCopperMutations =
                AgnusOfflineReplayTrace.DefaultMaximumCopperMutations,
            int maximumBlitterMicroOps =
                AgnusOfflineReplayTrace.DefaultMaximumBlitterMicroOps,
            int maximumPaulaDmaWords =
                AgnusOfflineReplayTrace.DefaultMaximumPaulaDmaWords,
            int maximumPaulaEvents =
                AgnusOfflineReplayTrace.DefaultMaximumPaulaEvents,
            int maximumDiskDmaWords =
                AgnusOfflineReplayTrace.DefaultMaximumDiskDmaWords,
            int maximumDiskEvents =
                AgnusOfflineReplayTrace.DefaultMaximumDiskEvents)
        {
            if (chipset != AmigaChipset.OcsPal)
            {
                throw new NotSupportedException(
                    "The Stage 2.4 Agnus slot kernel prototype supports A500 PAL OCS only.");
            }

            if (maximumSlots <= 0 || maximumSlots > AgnusOfflineReplayTrace.DefaultMaximumSlots)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSlots));
            }

            if (maximumCpuRequests <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCpuRequests));
            }

            if (maximumCopperInstructions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCopperInstructions));
            }

            if (maximumCopperMutations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCopperMutations));
            }

            if (maximumBlitterMicroOps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBlitterMicroOps));
            }

            if (maximumPaulaDmaWords <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPaulaDmaWords));
            }

            if (maximumPaulaEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPaulaEvents));
            }

            if (maximumDiskDmaWords <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDiskDmaWords));
            }

            if (maximumDiskEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDiskEvents));
            }

            _fixedPlan = new FixedSlotPlanEntry[maximumSlots];
            _fixedAddresses = new uint[maximumSlots];
            _committedSlots = new AgnusCommittedSlotRecord[maximumSlots];
            _cpuResults = new CpuWordResult[maximumCpuRequests];
            _copperTransitions =
                new AgnusOfflineCopperTransitionRecord[maximumCopperInstructions];
            _copperMutations =
                new AgnusOfflineCopperMutationRecord[maximumCopperMutations];
            _blitterMicroOps =
                new AgnusOfflineBlitterMicroOpRecord[maximumBlitterMicroOps];
            _paulaDmaWords =
                new AgnusOfflinePaulaDmaWordRecord[maximumPaulaDmaWords];
            _paulaEvents =
                new AgnusOfflinePaulaEventRecord[maximumPaulaEvents];
            _diskDmaWords =
                new AgnusOfflineDiskDmaWordRecord[maximumDiskDmaWords];
            _diskEvents =
                new AgnusOfflineDiskEventRecord[maximumDiskEvents];
        }

        public long CommittedSlot => _committedSlot;

        public int CommittedSlotCount => _committedSlotCount;

        public int CpuResultCount => _cpuResultCount;

        public int CopperTransitionCount => _copperTransitionCount;

        public int CopperMutationCount => _copperMutationCount;

        public int BlitterMicroOpCount => _blitterMicroOpCount;

        public int BlitterCompletionCount => _blitterCompletionCount;

        public int PaulaDmaWordCount => _paulaDmaWordCount;

        public int PaulaEventCount => _paulaEventCount;

        public int DiskDmaWordCount => _diskDmaWordCount;

        public int DiskEventCount => _diskEventCount;

        public long SlotDecisions => _slotDecisions;

        public long EmptySlotDecisions => _emptySlotDecisions;

        public long ControlMutationsApplied => _controlMutationsApplied;

        public long CopperFetchesCommitted => _copperFetchesCommitted;

        public bool CopperWaiting => _copperWaiting;

        public bool CopperStopped => _copperStopped;

        public bool BlitterBusy => _blitterBusy;

        public long BlitterInterruptCycle => _blitterCompletion.InterruptCycle;

        public AgnusSlotKernelDiagnostics CaptureDiagnostics()
            => new(
                _slotDecisions,
                _emptySlotDecisions,
                _controlMutationsApplied,
                _copperFetchesCommitted,
                _copperTransitionCount,
                _copperMutationCount,
                _blitterMicroOpCount,
                _blitterCompletionCount,
                _paulaDmaWordCount,
                _paulaEventCount,
                _diskDmaWordCount,
                _diskEventCount);

        public void Load(AgnusOfflineReplayTrace trace, byte[] chipRam)
        {
            ArgumentNullException.ThrowIfNull(trace);
            ArgumentNullException.ThrowIfNull(chipRam);
            if (trace.SlotCount > _fixedPlan.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel slot capacity.");
            }

            if (trace.CpuRequestCount > _cpuResults.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel CPU-result capacity.");
            }

            if (trace.CopperInstructionCount > _copperTransitions.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel Copper-transition capacity.");
            }

            if (trace.ExpectedCopperMutationCount > _copperMutations.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel Copper-mutation capacity.");
            }

            if (trace.BlitterMicroOpCount > _blitterMicroOps.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel blitter micro-operation capacity.");
            }

            if (trace.PaulaDmaWordCount > _paulaDmaWords.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel Paula DMA-word capacity.");
            }

            if (trace.PaulaEventCount > _paulaEvents.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel Paula-event capacity.");
            }

            if (trace.DiskDmaWordCount > _diskDmaWords.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel disk DMA-word capacity.");
            }

            if (trace.DiskEventCount > _diskEvents.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trace),
                    "The trace exceeds the bounded kernel disk-event capacity.");
            }

            if (trace.BlitterConfigured && !trace.BlitterCompletionConfigured)
            {
                throw new InvalidOperationException(
                    "The normalized blitter replay must include its exact completion.");
            }

            if (chipRam.Length < 2 || (chipRam.Length & (chipRam.Length - 1)) != 0)
            {
                throw new ArgumentException(
                    "Offline Chip RAM must have a power-of-two byte length.",
                    nameof(chipRam));
            }

            _trace = trace;
            _chipRam = chipRam;
            _chipRamMask = (uint)(chipRam.Length - 1);
            for (var index = 0; index < trace.SlotCount; index++)
            {
                _fixedPlan[index] = trace.GetInitialFixedEntry(index);
                _fixedAddresses[index] = trace.GetInitialFixedAddress(index);
                _committedSlots[index] = default;
            }

            for (var index = 0; index < trace.CopperInstructionCount; index++)
            {
                _copperTransitions[index] = default;
            }

            for (var index = 0; index < trace.ExpectedCopperMutationCount; index++)
            {
                _copperMutations[index] = default;
            }

            for (var index = 0; index < trace.BlitterMicroOpCount; index++)
            {
                _blitterMicroOps[index] = default;
            }

            for (var index = 0; index < trace.PaulaDmaWordCount; index++)
            {
                _paulaDmaWords[index] = default;
            }

            for (var index = 0; index < trace.PaulaEventCount; index++)
            {
                _paulaEvents[index] = default;
            }

            for (var index = 0; index < trace.DiskDmaWordCount; index++)
            {
                _diskDmaWords[index] = default;
            }

            for (var index = 0; index < trace.DiskEventCount; index++)
            {
                _diskEvents[index] = default;
            }

            _committedSlot = trace.FirstCycle - AgnusChipSlotScheduler.SlotCycles;
            _nextSlot = trace.FirstCycle;
            _committedSlotCount = 0;
            _nextCpuRequest = 0;
            _nextControlMutation = 0;
            _cpuResultCount = 0;
            _nextCopperInstruction = 0;
            _copperTransitionCount = 0;
            _copperMutationCount = 0;
            _nextBlitterMicroOp = 0;
            _blitterMicroOpCount = 0;
            _paulaDmaWordCount = 0;
            _nextPaulaEvent = 0;
            _paulaEventCount = 0;
            _nextDiskDmaWord = 0;
            _diskDmaWordCount = 0;
            _nextDiskEvent = 0;
            _diskEventCount = 0;
            _diskRequestedCycle = trace.DiskDmaWordCount > 0
                ? trace.GetDiskDmaWord(0).RequestedCycle
                : long.MaxValue;
            for (var channel = 0; channel < AmigaConstants.PaulaChannelCount; channel++)
            {
                _nextPaulaWordByChannel[channel] =
                    trace.GetFirstPaulaWordIndex(channel);
            }
            _cpuPending = false;
            _cpuRequest = default;
            _cpuEarliestEligibleCycle = 0;
            _cpuDmaWaitCycles = 0;
            _copperSecondWordPending = false;
            if (trace.CopperInstructionCount > 0)
            {
                ref readonly var firstInstruction = ref trace.GetCopperInstruction(0);
                _copperPc = firstInstruction.Pc;
                _copperRequestedCycle = firstInstruction.FirstRequestedCycle;
            }
            else
            {
                _copperPc = 0;
                _copperRequestedCycle = long.MaxValue;
            }

            _copperFirstRequestedCycle = -1;
            _copperFirstWord = 0;
            _copperFirstGrantedCycle = -1;
            _copperSuppressNextMove = false;
            _copperWaiting = false;
            _copperStopped = false;
            _blitterConfigured = trace.BlitterConfigured;
            _blitterStarted = false;
            _blitterBusy = false;
            _blitterNasty = trace.BlitterNasty;
            _blitterRequestedCycle = trace.BlitterMicroOpCount > 0
                ? trace.GetBlitterMicroOp(0).RequestedCycle
                : long.MaxValue;
            _blitterCompletionCycle =
                trace.BlitterConfigured && trace.BlitterMicroOpCount == 0
                    ? trace.BlitterStartCycle + trace.BlitterCompletionDelay
                    : long.MaxValue;
            _blitterCompletionCount = 0;
            _blitterCompletion = default;
            _slotDecisions = 0;
            _emptySlotDecisions = 0;
            _controlMutationsApplied = 0;
            _copperFetchesCommitted = 0;
        }

        public AgnusOfflineReplayComparison Replay()
        {
            var trace = RequireTrace();
            while (_nextSlot <= trace.EndCycle)
            {
                ApplyDiskEventsThrough(_nextSlot);
                ApplyPaulaEventsThrough(_nextSlot);
                ApplyMutationsThrough(_nextSlot);
                ApplyBlitterStartThrough(_nextSlot);
                ApplyBlitterCompletionThrough(_nextSlot);
                ActivateCpuRequestIfEligible(_nextSlot);
                CommitNextSlot();
            }

            if (_cpuPending || _nextCpuRequest != trace.CpuRequestCount)
            {
                throw new InvalidOperationException(
                    "The bounded replay window ended before every CPU request was granted.");
            }

            if (_nextControlMutation != trace.ControlMutationCount)
            {
                throw new InvalidOperationException(
                    "The bounded replay window ended before every control mutation was applied.");
            }

            if (_nextCopperInstruction != trace.CopperInstructionCount ||
                _copperTransitionCount != trace.CopperInstructionCount)
            {
                throw new InvalidOperationException(
                    "The bounded replay window ended before every Copper transition completed.");
            }

            if (_nextBlitterMicroOp != trace.BlitterMicroOpCount ||
                _blitterMicroOpCount != trace.BlitterMicroOpCount ||
                (trace.BlitterConfigured &&
                    (_blitterBusy || _blitterCompletionCount != 1)))
            {
                throw new InvalidOperationException(
                    "The bounded replay window ended before the normalized blitter operation completed.");
            }

            if (_paulaDmaWordCount != trace.PaulaDmaWordCount ||
                _nextPaulaEvent != trace.PaulaEventCount)
            {
                throw new InvalidOperationException(
                    "The bounded replay window ended before every Paula transition completed.");
            }

            if (_nextDiskDmaWord != trace.DiskDmaWordCount ||
                _nextDiskEvent != trace.DiskEventCount)
            {
                throw new InvalidOperationException(
                    "The bounded replay window ended before every disk transition completed.");
            }

            for (var index = 0; index < trace.SlotCount; index++)
            {
                var expected = trace.GetExpectedSlot(index);
                var actual = _committedSlots[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: index,
                        mismatchIndex: index,
                        expected,
                        actual);
                }
            }

            for (var index = 0; index < trace.CopperInstructionCount; index++)
            {
                var expected = trace.GetExpectedCopperTransition(index);
                var actual = _copperTransitions[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        copperTransitionMismatchIndex: index,
                        expectedCopperTransition: expected,
                        actualCopperTransition: actual);
                }
            }

            if (_copperMutationCount != trace.ExpectedCopperMutationCount)
            {
                var index = Math.Min(
                    _copperMutationCount,
                    trace.ExpectedCopperMutationCount);
                return new AgnusOfflineReplayComparison(
                    equal: false,
                    comparedSlots: trace.SlotCount,
                    mismatchIndex: -1,
                    expected: default,
                    actual: default,
                    copperMutationMismatchIndex: index,
                    expectedCopperMutation:
                        index < trace.ExpectedCopperMutationCount
                            ? trace.GetExpectedCopperMutation(index)
                            : default,
                    actualCopperMutation:
                        index < _copperMutationCount
                            ? _copperMutations[index]
                            : default);
            }

            for (var index = 0; index < trace.ExpectedCopperMutationCount; index++)
            {
                var expected = trace.GetExpectedCopperMutation(index);
                var actual = _copperMutations[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        copperMutationMismatchIndex: index,
                        expectedCopperMutation: expected,
                        actualCopperMutation: actual);
                }
            }

            for (var index = 0; index < trace.BlitterMicroOpCount; index++)
            {
                var expected = trace.GetExpectedBlitterMicroOp(index);
                var actual = _blitterMicroOps[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        blitterMicroOpMismatchIndex: index,
                        expectedBlitterMicroOp: expected,
                        actualBlitterMicroOp: actual);
                }
            }

            if (trace.BlitterConfigured)
            {
                var expected = trace.GetExpectedBlitterCompletion();
                if (!expected.Equals(_blitterCompletion))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        blitterCompletionMismatch: true,
                        expectedBlitterCompletion: expected,
                        actualBlitterCompletion: _blitterCompletion);
                }
            }

            for (var index = 0; index < trace.PaulaDmaWordCount; index++)
            {
                var expected = trace.GetExpectedPaulaDmaWord(index);
                var actual = _paulaDmaWords[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        paulaDmaWordMismatchIndex: index,
                        expectedPaulaDmaWord: expected,
                        actualPaulaDmaWord: actual);
                }
            }

            for (var index = 0; index < trace.PaulaEventCount; index++)
            {
                var expected = trace.GetExpectedPaulaEvent(index);
                var actual = _paulaEvents[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        paulaEventMismatchIndex: index,
                        expectedPaulaEvent: expected,
                        actualPaulaEvent: actual);
                }
            }

            for (var index = 0; index < trace.DiskDmaWordCount; index++)
            {
                var expected = trace.GetExpectedDiskDmaWord(index);
                var actual = _diskDmaWords[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        diskDmaWordMismatchIndex: index,
                        expectedDiskDmaWord: expected,
                        actualDiskDmaWord: actual);
                }
            }

            for (var index = 0; index < trace.DiskEventCount; index++)
            {
                var expected = trace.GetExpectedDiskEvent(index);
                var actual = _diskEvents[index];
                if (!expected.Equals(actual))
                {
                    return new AgnusOfflineReplayComparison(
                        equal: false,
                        comparedSlots: trace.SlotCount,
                        mismatchIndex: -1,
                        expected: default,
                        actual: default,
                        diskEventMismatchIndex: index,
                        expectedDiskEvent: expected,
                        actualDiskEvent: actual);
                }
            }

            return new AgnusOfflineReplayComparison(
                equal: true,
                comparedSlots: trace.SlotCount,
                mismatchIndex: -1,
                expected: default,
                actual: default);
        }

        public long AdvanceThrough(long targetCycle)
        {
            var trace = RequireTrace();
            targetCycle = Math.Min(targetCycle, trace.EndCycle);
            while (_nextSlot <= targetCycle)
            {
                ApplyDiskEventsThrough(_nextSlot);
                ApplyPaulaEventsThrough(_nextSlot);
                ApplyMutationsThrough(_nextSlot);
                ApplyBlitterStartThrough(_nextSlot);
                ApplyBlitterCompletionThrough(_nextSlot);
                ActivateCpuRequestIfEligible(_nextSlot);
                CommitNextSlot();
            }

            return _committedSlot;
        }

        public CpuWordResult GrantCpuWord(in CpuWordRequest request)
        {
            var trace = RequireTrace();
            if (_cpuPending || _nextCpuRequest < trace.CpuRequestCount)
            {
                throw new InvalidOperationException(
                    "Direct CPU grants require a loaded trace with no queued CPU requests.");
            }

            _cpuPending = true;
            _cpuRequest = request;
            _cpuEarliestEligibleCycle = request.RequestedCycle;
            _cpuDmaWaitCycles = 0;
            var resultIndex = _cpuResultCount;
            while (_cpuResultCount == resultIndex)
            {
                if (_nextSlot > trace.EndCycle)
                {
                    throw new InvalidOperationException("The CPU request was not granted within the bounded trace.");
                }

                ApplyMutationsThrough(_nextSlot);
                ApplyDiskEventsThrough(_nextSlot);
                ApplyPaulaEventsThrough(_nextSlot);
                ApplyBlitterStartThrough(_nextSlot);
                ApplyBlitterCompletionThrough(_nextSlot);
                CommitNextSlot();
            }

            return _cpuResults[resultIndex];
        }

        public CpuLongResult GrantCpuLong(in CpuLongRequest request)
        {
            var firstRequest = new CpuWordRequest(
                request.Address,
                request.RequestedCycle,
                request.Kind,
                request.IsWrite,
                (ushort)(request.Value >> 16));
            var first = GrantCpuWord(firstRequest);
            var secondRequest = new CpuWordRequest(
                request.Address + 2,
                first.GrantedCycle + (2 * AgnusChipSlotScheduler.SlotCycles),
                request.Kind,
                request.IsWrite,
                (ushort)request.Value);
            var second = GrantCpuWord(secondRequest);
            return new CpuLongResult(
                ((uint)first.Value << 16) | second.Value,
                first.GrantedCycle,
                second.GrantedCycle,
                second.CompletedCycle);
        }

        public void ApplyControlMutation(long cycle, in BusControlMutation mutation)
        {
            var trace = RequireTrace();
            if (cycle != mutation.Cycle)
            {
                throw new ArgumentException(
                    "The supplied control cycle must match the recorded mutation.",
                    nameof(cycle));
            }

            if (cycle <= _committedSlot)
            {
                throw new InvalidOperationException(
                    "A control mutation cannot modify state behind the authoritative committed cursor.");
            }

            for (var offset = 0; offset < mutation.PatchCount; offset++)
            {
                var patch = trace.GetFixedPatch(mutation.PatchStart + offset);
                if (patch.SlotCycle <= _committedSlot || patch.SlotCycle <= cycle)
                {
                    throw new InvalidOperationException(
                        "A control mutation may patch only uncommitted future slots.");
                }

                var index = trace.GetSlotIndex(patch.SlotCycle);
                _fixedPlan[index] = patch.Entry;
                _fixedAddresses[index] = patch.Address;
            }

            _controlMutationsApplied++;
        }

        public AgnusChipSlotOwner GetOwnerForSlot(long slotCycle, bool cpuPending)
            => GetOwnerForSlot(
                slotCycle,
                cpuPending,
                diskPending: false,
                paulaPending: false,
                copperPending: false,
                blitterPending: false);

        private AgnusChipSlotOwner GetOwnerForSlot(
            long slotCycle,
            bool cpuPending,
            bool diskPending,
            bool paulaPending,
            bool copperPending,
            bool blitterPending)
        {
            var trace = RequireTrace();
            var index = trace.GetSlotIndex(slotCycle);
            if (AgnusHrmOcsSlotTable.IsMandatoryRefreshSlot(slotCycle))
            {
                return AgnusChipSlotOwner.Refresh;
            }

            var entry = _fixedPlan[index];
            if (entry.Occupied)
            {
                return entry.Owner;
            }

            if (diskPending)
            {
                return AgnusChipSlotOwner.Disk;
            }

            if (paulaPending)
            {
                return AgnusChipSlotOwner.Paula;
            }

            if (copperPending)
            {
                return AgnusChipSlotOwner.Copper;
            }

            if (blitterPending &&
                (!cpuPending ||
                    _blitterNasty ||
                    _cpuDmaWaitCycles <
                        CpuDmaWaitCyclesBeforeNiceBlitterYield))
            {
                return AgnusChipSlotOwner.Blitter;
            }

            return cpuPending ? AgnusChipSlotOwner.Cpu : AgnusChipSlotOwner.Free;
        }

        public ref readonly AgnusCommittedSlotRecord GetCommittedSlot(int index)
        {
            if ((uint)index >= (uint)_committedSlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _committedSlots[index];
        }

        public ref readonly CpuWordResult GetCpuResult(int index)
        {
            if ((uint)index >= (uint)_cpuResultCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _cpuResults[index];
        }

        public ref readonly AgnusOfflineCopperTransitionRecord GetCopperTransition(int index)
        {
            if ((uint)index >= (uint)_copperTransitionCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _copperTransitions[index];
        }

        public ref readonly AgnusOfflineCopperMutationRecord GetCopperMutation(int index)
        {
            if ((uint)index >= (uint)_copperMutationCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _copperMutations[index];
        }

        public ref readonly AgnusOfflineBlitterMicroOpRecord GetBlitterMicroOp(int index)
        {
            if ((uint)index >= (uint)_blitterMicroOpCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _blitterMicroOps[index];
        }

        public AgnusOfflineBlitterCompletionRecord GetBlitterCompletion()
        {
            if (_blitterCompletionCount == 0)
            {
                throw new InvalidOperationException(
                    "The normalized blitter operation has not completed.");
            }

            return _blitterCompletion;
        }

        public ref readonly AgnusOfflinePaulaDmaWordRecord GetPaulaDmaWord(int index)
        {
            if ((uint)index >= (uint)_paulaDmaWordCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _paulaDmaWords[index];
        }

        public ref readonly AgnusOfflinePaulaEventRecord GetPaulaEvent(int index)
        {
            if ((uint)index >= (uint)_paulaEventCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _paulaEvents[index];
        }

        public ref readonly AgnusOfflineDiskDmaWordRecord GetDiskDmaWord(int index)
        {
            if ((uint)index >= (uint)_diskDmaWordCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _diskDmaWords[index];
        }

        public ref readonly AgnusOfflineDiskEventRecord GetDiskEvent(int index)
        {
            if ((uint)index >= (uint)_diskEventCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref _diskEvents[index];
        }

        private void ApplyDiskEventsThrough(long cycle)
        {
            var trace = RequireTrace();
            while (_nextDiskEvent < trace.DiskEventCount)
            {
                ref readonly var record =
                    ref trace.GetExpectedDiskEvent(_nextDiskEvent);
                if (record.Cycle > cycle)
                {
                    break;
                }

                if (_diskEventCount == _diskEvents.Length)
                {
                    throw new InvalidOperationException(
                        "The bounded offline disk-event buffer is full.");
                }

                _diskEvents[_diskEventCount++] = record;
                _nextDiskEvent++;
            }
        }

        private void ApplyPaulaEventsThrough(long cycle)
        {
            var trace = RequireTrace();
            while (_nextPaulaEvent < trace.PaulaEventCount)
            {
                ref readonly var record =
                    ref trace.GetExpectedPaulaEvent(_nextPaulaEvent);
                if (record.Cycle > cycle)
                {
                    break;
                }

                if (_paulaEventCount == _paulaEvents.Length)
                {
                    throw new InvalidOperationException(
                        "The bounded offline Paula-event buffer is full.");
                }

                // This is a normalized publication, not Paula execution. The
                // production state machine already produced the transition.
                _paulaEvents[_paulaEventCount++] = record;
                _nextPaulaEvent++;
            }
        }

        private void ApplyMutationsThrough(long slotCycle)
        {
            var trace = RequireTrace();
            while (_nextControlMutation < trace.ControlMutationCount)
            {
                ref readonly var mutation = ref trace.GetControlMutation(_nextControlMutation);
                if (mutation.Cycle > slotCycle)
                {
                    break;
                }

                ApplyControlMutation(mutation.Cycle, mutation);
                _nextControlMutation++;
            }
        }

        private void ActivateCpuRequestIfEligible(long slotCycle)
        {
            if (_cpuPending)
            {
                return;
            }

            var trace = RequireTrace();
            if (_nextCpuRequest >= trace.CpuRequestCount)
            {
                return;
            }

            ref readonly var request = ref trace.GetCpuRequest(_nextCpuRequest);
            if (request.RequestedCycle > slotCycle)
            {
                return;
            }

            _cpuRequest = request;
            _cpuPending = true;
            _cpuEarliestEligibleCycle = request.RequestedCycle;
            _cpuDmaWaitCycles = 0;
        }

        private void ApplyBlitterCompletionThrough(long cycle)
        {
            if (!_blitterBusy ||
                _nextBlitterMicroOp != RequireTrace().BlitterMicroOpCount ||
                _blitterCompletionCycle > cycle)
            {
                return;
            }

            if (_blitterCompletionCount != 0)
            {
                throw new InvalidOperationException(
                    "The normalized blitter completion may commit only once.");
            }

            var oracle = RequireTrace().GetExpectedBlitterCompletion();
            _blitterCompletion = new AgnusOfflineBlitterCompletionRecord(
                _blitterCompletionCycle,
                _blitterCompletionCycle,
                oracle.Zero,
                oracle.SourceA,
                oracle.SourceB,
                oracle.SourceC,
                oracle.DestinationD);
            _blitterCompletionCount = 1;
            _blitterBusy = false;
        }

        private void ApplyBlitterStartThrough(long cycle)
        {
            if (!_blitterConfigured ||
                _blitterStarted ||
                RequireTrace().BlitterStartCycle > cycle)
            {
                return;
            }

            _blitterStarted = true;
            _blitterBusy = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CommitNextSlot()
        {
            var trace = RequireTrace();
            if (_nextSlot <= _committedSlot)
            {
                throw new InvalidOperationException(
                    "The Agnus slot kernel cannot commit behind its authoritative cursor.");
            }

            var index = trace.GetSlotIndex(_nextSlot);
            var cpuEligible =
                _cpuPending &&
                _cpuRequest.RequestedCycle <= _nextSlot &&
                _cpuEarliestEligibleCycle <= _nextSlot;
            var copperEligible = IsCopperEligible(_nextSlot);
            var blitterEligible = IsBlitterEligible(_nextSlot);
            var diskEligible = IsDiskEligible(_nextSlot);
            var paulaChannel = GetEligiblePaulaChannel(_nextSlot);
            var owner = GetOwnerForSlot(
                _nextSlot,
                cpuEligible,
                diskEligible,
                paulaChannel >= 0,
                copperEligible,
                blitterEligible);
            if (cpuEligible &&
                owner != AgnusChipSlotOwner.Cpu &&
                _cpuDmaWaitCycles < CpuDmaWaitCyclesBeforeNiceBlitterYield)
            {
                _cpuDmaWaitCycles++;
            }

            AgnusCommittedSlotRecord record;
            switch (owner)
            {
                case AgnusChipSlotOwner.Refresh:
                    record = CreateRefreshRecord(_nextSlot);
                    break;
                case AgnusChipSlotOwner.Bitplane:
                case AgnusChipSlotOwner.Sprite:
                    record = CommitFixedSlot(index, _nextSlot, owner);
                    break;
                case AgnusChipSlotOwner.Cpu:
                    record = CommitCpuSlot(_nextSlot);
                    break;
                case AgnusChipSlotOwner.Copper:
                    record = CommitCopperSlot(_nextSlot);
                    break;
                case AgnusChipSlotOwner.Blitter:
                    record = CommitBlitterSlot(_nextSlot);
                    break;
                case AgnusChipSlotOwner.Paula:
                    record = CommitPaulaSlot(_nextSlot, paulaChannel);
                    break;
                case AgnusChipSlotOwner.Disk:
                    record = CommitDiskSlot(_nextSlot);
                    break;
                default:
                    _emptySlotDecisions++;
                    record = CreateFreeRecord(_nextSlot);
                    break;
            }

            _committedSlots[_committedSlotCount++] = record;
            _committedSlot = _nextSlot;
            _nextSlot += AgnusChipSlotScheduler.SlotCycles;
            _slotDecisions++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsCopperEligible(long slotCycle)
        {
            var trace = RequireTrace();
            if (_copperStopped ||
                _nextCopperInstruction >= trace.CopperInstructionCount ||
                _copperRequestedCycle > slotCycle)
            {
                return false;
            }

            ref readonly var instruction =
                ref trace.GetCopperInstruction(_nextCopperInstruction);
            if (_copperSecondWordPending && instruction.PreserveSecondWordPhysicalPhase)
            {
                return ((slotCycle / AgnusChipSlotScheduler.SlotCycles) & 1) ==
                    ((_copperRequestedCycle / AgnusChipSlotScheduler.SlotCycles) & 1);
            }

            return AgnusHrmOcsSlotTable.IsCopperAccessSlot(slotCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsBlitterEligible(long slotCycle)
        {
            var trace = RequireTrace();
            return _blitterBusy &&
                _nextBlitterMicroOp < trace.BlitterMicroOpCount &&
                _blitterRequestedCycle <= slotCycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetEligiblePaulaChannel(long slotCycle)
        {
            var horizontal = AgnusHrmOcsSlotTable.GetHorizontal(slotCycle);
            var delta = horizontal - AgnusHrmOcsSlotTable.FirstPaulaHorizontal;
            if ((uint)delta > 6 || (delta & 1) != 0)
            {
                return -1;
            }

            var channel = delta >> 1;
            var index = _nextPaulaWordByChannel[channel];
            if (index < 0)
            {
                return -1;
            }

            ref readonly var word = ref RequireTrace().GetPaulaDmaWord(index);
            return word.RequestedCycle <= slotCycle ? channel : -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsDiskEligible(long slotCycle)
        {
            var trace = RequireTrace();
            return _nextDiskDmaWord < trace.DiskDmaWordCount &&
                _diskRequestedCycle <= slotCycle &&
                AgnusHrmOcsSlotTable.IsFixedDmaSlotForOwner(
                    AgnusChipSlotOwner.Disk,
                    slotCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AgnusCommittedSlotRecord CommitDiskSlot(long slotCycle)
        {
            var trace = RequireTrace();
            var index = _nextDiskDmaWord;
            ref readonly var word = ref trace.GetDiskDmaWord(index);
            ushort value;
            if (word.WriteMode)
            {
                value = ReadWord(word.Address);
            }
            else
            {
                value = word.OracleValue;
                WriteWord(word.Address, value);
            }

            var completedCycle = slotCycle + AgnusChipSlotScheduler.SlotCycles;
            if (_diskDmaWordCount == _diskDmaWords.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline disk DMA-word buffer is full.");
            }

            _diskDmaWords[index] =
                new AgnusOfflineDiskDmaWordRecord(
                    index,
                    MaskAddress(word.Address),
                    value,
                    word.WriteMode,
                    _diskRequestedCycle,
                    slotCycle,
                    completedCycle);
            _diskDmaWordCount++;
            _nextDiskDmaWord++;
            if (_nextDiskDmaWord < trace.DiskDmaWordCount)
            {
                ref readonly var next = ref trace.GetDiskDmaWord(_nextDiskDmaWord);
                _diskRequestedCycle =
                    completedCycle + next.DelayAfterPreviousCompletion;
            }
            else
            {
                _diskRequestedCycle = long.MaxValue;
            }

            return new AgnusCommittedSlotRecord(
                slotCycle,
                _diskDmaWords[index].RequestedCycle,
                completedCycle,
                AgnusChipSlotOwner.Disk,
                AmigaBusRequester.Disk,
                AmigaBusAccessKind.DiskDma,
                AmigaBusAccessTarget.ChipRam,
                MaskAddress(word.Address),
                value,
                addressValid: true,
                valueValid: true,
                granted: true,
                word.WriteMode,
                channel: 0,
                phase: 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AgnusCommittedSlotRecord CommitPaulaSlot(long slotCycle, int channel)
        {
            var trace = RequireTrace();
            var index = _nextPaulaWordByChannel[channel];
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "A Paula slot cannot commit without an eligible channel request.");
            }

            ref readonly var word = ref trace.GetPaulaDmaWord(index);
            var value = ReadWord(word.Address);
            var completedCycle = slotCycle + AgnusChipSlotScheduler.SlotCycles;
            if (_paulaDmaWordCount == _paulaDmaWords.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline Paula DMA-word buffer is full.");
            }

            _paulaDmaWords[index] =
                new AgnusOfflinePaulaDmaWordRecord(
                    index,
                    channel,
                    MaskAddress(word.Address),
                    value,
                    word.RequestedCycle,
                    slotCycle,
                    completedCycle);
            _paulaDmaWordCount++;
            _nextPaulaWordByChannel[channel] = word.NextChannelWordIndex;
            return new AgnusCommittedSlotRecord(
                slotCycle,
                word.RequestedCycle,
                completedCycle,
                AgnusChipSlotOwner.Paula,
                AmigaBusRequester.Paula,
                AmigaBusAccessKind.PaulaDma,
                AmigaBusAccessTarget.ChipRam,
                MaskAddress(word.Address),
                value,
                addressValid: true,
                valueValid: true,
                granted: true,
                isWrite: false,
                (byte)channel,
                phase: 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AgnusCommittedSlotRecord CommitBlitterSlot(long slotCycle)
        {
            var trace = RequireTrace();
            var index = _nextBlitterMicroOp;
            ref readonly var microOp = ref trace.GetBlitterMicroOp(index);
            ushort value;
            if (microOp.IsWrite)
            {
                value = microOp.OracleValue;
                WriteWord(microOp.Address, value);
            }
            else
            {
                value = ReadWord(microOp.Address);
            }

            var completedCycle = slotCycle + AgnusChipSlotScheduler.SlotCycles;
            if (_blitterMicroOpCount == _blitterMicroOps.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline blitter micro-operation buffer is full.");
            }

            _blitterMicroOps[_blitterMicroOpCount++] =
                new AgnusOfflineBlitterMicroOpRecord(
                    index,
                    microOp.Kind,
                    MaskAddress(microOp.Address),
                    value,
                    microOp.IsWrite,
                    _blitterRequestedCycle,
                    slotCycle,
                    completedCycle);
            _nextBlitterMicroOp++;
            if (_nextBlitterMicroOp < trace.BlitterMicroOpCount)
            {
                ref readonly var next = ref trace.GetBlitterMicroOp(_nextBlitterMicroOp);
                _blitterRequestedCycle =
                    completedCycle + next.DelayAfterPreviousCompletion;
            }
            else
            {
                _blitterRequestedCycle = long.MaxValue;
                _blitterCompletionCycle =
                    completedCycle + trace.BlitterCompletionDelay;
            }

            return new AgnusCommittedSlotRecord(
                slotCycle,
                _blitterMicroOps[_blitterMicroOpCount - 1].RequestedCycle,
                completedCycle,
                AgnusChipSlotOwner.Blitter,
                AmigaBusRequester.Blitter,
                AmigaBusAccessKind.Blitter,
                AmigaBusAccessTarget.ChipRam,
                MaskAddress(microOp.Address),
                value,
                addressValid: true,
                valueValid: true,
                granted: true,
                microOp.IsWrite,
                channel: 0,
                phase: 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AgnusCommittedSlotRecord CommitCopperSlot(long slotCycle)
        {
            var trace = RequireTrace();
            ref readonly var instruction =
                ref trace.GetCopperInstruction(_nextCopperInstruction);
            var requestedCycle = _copperRequestedCycle;
            var address = _copperSecondWordPending
                ? AddPointerOffset(_copperPc, 2)
                : _copperPc;
            var value = ReadWord(address);
            var phase = _copperSecondWordPending ? (byte)1 : (byte)0;
            var record = new AgnusCommittedSlotRecord(
                slotCycle,
                requestedCycle,
                slotCycle + AgnusChipSlotScheduler.SlotCycles,
                AgnusChipSlotOwner.Copper,
                AmigaBusRequester.Copper,
                AmigaBusAccessKind.Copper,
                AmigaBusAccessTarget.ChipRam,
                MaskAddress(address),
                value,
                addressValid: true,
                valueValid: true,
                granted: true,
                isWrite: false,
                channel: 0,
                phase);
            _copperFetchesCommitted++;

            if (!_copperSecondWordPending)
            {
                _copperFirstRequestedCycle = requestedCycle;
                _copperFirstGrantedCycle = slotCycle;
                _copperFirstWord = value;
                _copperSecondWordPending = true;
                _copperWaiting = false;
                _copperRequestedCycle = Math.Max(
                    slotCycle + AgnusChipSlotScheduler.SlotCycles,
                    requestedCycle + (2L * AgnusChipSlotScheduler.SlotCycles));
                return record;
            }

            CompleteCopperInstruction(in instruction, value, slotCycle);
            return record;
        }

        private void CompleteCopperInstruction(
            in AgnusOfflineCopperInstruction instruction,
            ushort secondWord,
            long secondGrantedCycle)
        {
            var instructionIndex = _nextCopperInstruction;
            var secondCompletedCycle =
                secondGrantedCycle + AgnusChipSlotScheduler.SlotCycles;
            var moveStopCycle = Math.Max(
                secondCompletedCycle,
                _copperFirstRequestedCycle +
                    (4L * AgnusChipSlotScheduler.SlotCycles));
            var controlStopCycle = Math.Max(
                secondCompletedCycle,
                _copperFirstRequestedCycle +
                    (6L * AgnusChipSlotScheduler.SlotCycles));
            var moveLike = instruction.Action is
                AgnusOfflineCopperAction.Move or
                AgnusOfflineCopperAction.Copjmp;
            var moveSuppressed = moveLike && _copperSuppressNextMove;
            if (moveLike)
            {
                _copperSuppressNextMove = false;
            }

            if (instruction.Action == AgnusOfflineCopperAction.Skip &&
                instruction.ComparisonSatisfied)
            {
                _copperSuppressNextMove = true;
            }

            var mutationCommitted =
                moveLike &&
                instruction.CommitMove &&
                !moveSuppressed;
            var transitionCycle = instruction.Action switch
            {
                AgnusOfflineCopperAction.Move => secondGrantedCycle,
                AgnusOfflineCopperAction.Copjmp => secondGrantedCycle,
                AgnusOfflineCopperAction.Wait => controlStopCycle,
                AgnusOfflineCopperAction.Skip => controlStopCycle,
                _ => moveStopCycle
            };
            var nextRequestedCycle = instruction.Action switch
            {
                AgnusOfflineCopperAction.Move => moveStopCycle,
                AgnusOfflineCopperAction.Copjmp => moveStopCycle,
                AgnusOfflineCopperAction.Skip => controlStopCycle,
                AgnusOfflineCopperAction.Wait => instruction.NextRequestedCycle,
                _ => -1
            };

            if (mutationCommitted)
            {
                CommitCopperMutation(
                    instructionIndex,
                    transitionCycle,
                    in instruction);
            }

            _copperWaiting = instruction.Action == AgnusOfflineCopperAction.Wait;
            _copperStopped = instruction.Action == AgnusOfflineCopperAction.End;
            if (_copperTransitionCount == _copperTransitions.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline Copper-transition buffer is full.");
            }

            _copperTransitions[_copperTransitionCount++] =
                new AgnusOfflineCopperTransitionRecord(
                    instructionIndex,
                    _copperPc,
                    _copperFirstRequestedCycle,
                    _copperFirstGrantedCycle,
                    _copperFirstGrantedCycle + AgnusChipSlotScheduler.SlotCycles,
                    _copperRequestedCycle,
                    secondGrantedCycle,
                    secondCompletedCycle,
                    _copperFirstWord,
                    secondWord,
                    instruction.Action,
                    transitionCycle,
                    instruction.NextPc,
                    nextRequestedCycle,
                    instruction.ComparisonSatisfied,
                    instruction.WaitForBlitter,
                    moveSuppressed,
                    mutationCommitted,
                    _copperWaiting,
                    _copperStopped);

            _nextCopperInstruction++;
            _copperSecondWordPending = false;
            _copperPc = instruction.NextPc;
            _copperRequestedCycle = nextRequestedCycle;
        }

        private void CommitCopperMutation(
            int instructionIndex,
            long transitionCycle,
            in AgnusOfflineCopperInstruction instruction)
        {
            var trace = RequireTrace();
            for (var offset = 0; offset < instruction.PatchCount; offset++)
            {
                var patch = trace.GetFixedPatch(instruction.PatchStart + offset);
                if (patch.SlotCycle <= _committedSlot ||
                    patch.SlotCycle <= transitionCycle)
                {
                    throw new InvalidOperationException(
                        "A Copper mutation may patch only uncommitted future slots.");
                }

                var index = trace.GetSlotIndex(patch.SlotCycle);
                _fixedPlan[index] = patch.Entry;
                _fixedAddresses[index] = patch.Address;
            }

            if (_copperMutationCount == _copperMutations.Length)
            {
                throw new InvalidOperationException(
                    "The bounded offline Copper-mutation buffer is full.");
            }

            _copperMutations[_copperMutationCount++] =
                new AgnusOfflineCopperMutationRecord(
                    instructionIndex,
                    transitionCycle,
                    instruction.Action,
                    instruction.MoveRegister,
                    instruction.MoveValue,
                    instruction.PatchCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AgnusCommittedSlotRecord CommitFixedSlot(
            int index,
            long slotCycle,
            AgnusChipSlotOwner owner)
        {
            var entry = _fixedPlan[index];
            var address = _fixedAddresses[index];
            var value = ReadWord(address);
            var requester = owner == AgnusChipSlotOwner.Bitplane
                ? AmigaBusRequester.Bitplane
                : AmigaBusRequester.Sprite;
            var kind = owner == AgnusChipSlotOwner.Bitplane
                ? AmigaBusAccessKind.Bitplane
                : AmigaBusAccessKind.Sprite;
            return new AgnusCommittedSlotRecord(
                slotCycle,
                slotCycle,
                slotCycle + AgnusChipSlotScheduler.SlotCycles,
                owner,
                requester,
                kind,
                AmigaBusAccessTarget.ChipRam,
                MaskAddress(address),
                value,
                addressValid: true,
                valueValid: true,
                granted: true,
                isWrite: false,
                entry.Channel,
                entry.Phase);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private AgnusCommittedSlotRecord CommitCpuSlot(long slotCycle)
        {
            var request = _cpuRequest;
            ushort value;
            if (request.IsWrite)
            {
                WriteWord(request.Address, request.Value);
                value = request.Value;
            }
            else
            {
                value = ReadWord(request.Address);
            }

            var completedCycle = slotCycle + AgnusChipSlotScheduler.SlotCycles;
            if (_cpuResultCount == _cpuResults.Length)
            {
                throw new InvalidOperationException("The bounded offline CPU-result buffer is full.");
            }

            _cpuResults[_cpuResultCount++] = new CpuWordResult(value, slotCycle, completedCycle);
            _cpuPending = false;
            _cpuDmaWaitCycles = 0;
            if (_nextCpuRequest < RequireTrace().CpuRequestCount)
            {
                _nextCpuRequest++;
            }

            return new AgnusCommittedSlotRecord(
                slotCycle,
                request.RequestedCycle,
                completedCycle,
                AgnusChipSlotOwner.Cpu,
                AmigaBusRequester.Cpu,
                request.Kind,
                AmigaBusAccessTarget.ChipRam,
                MaskAddress(request.Address),
                value,
                addressValid: true,
                valueValid: true,
                granted: true,
                request.IsWrite,
                channel: 0,
                phase: 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort ReadWord(uint address)
        {
            var chipRam = _chipRam ??
                throw new InvalidOperationException("No offline Chip RAM is loaded.");
            var first = (int)(address & _chipRamMask);
            var second = (int)((address + 1) & _chipRamMask);
            return (ushort)((chipRam[first] << 8) | chipRam[second]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteWord(uint address, ushort value)
        {
            var chipRam = _chipRam ??
                throw new InvalidOperationException("No offline Chip RAM is loaded.");
            var first = (int)(address & _chipRamMask);
            var second = (int)((address + 1) & _chipRamMask);
            chipRam[first] = (byte)(value >> 8);
            chipRam[second] = (byte)value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint MaskAddress(uint address)
            => address & _chipRamMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint AddPointerOffset(uint address, uint offset)
            => (address + offset) & _chipRamMask;

        private AgnusOfflineReplayTrace RequireTrace()
            => _trace ??
                throw new InvalidOperationException("Load an offline replay trace before using the kernel.");

        private static AgnusCommittedSlotRecord CreateRefreshRecord(long slotCycle)
            => new(
                slotCycle,
                slotCycle,
                slotCycle + AgnusChipSlotScheduler.SlotCycles,
                AgnusChipSlotOwner.Refresh,
                AmigaBusRequester.Host,
                AmigaBusAccessKind.HostTrap,
                AmigaBusAccessTarget.ChipRam,
                address: 0,
                value: 0,
                addressValid: false,
                valueValid: false,
                granted: true,
                isWrite: false,
                channel: 0,
                phase: 0);

        private static AgnusCommittedSlotRecord CreateFreeRecord(long slotCycle)
            => new(
                slotCycle,
                slotCycle,
                slotCycle,
                AgnusChipSlotOwner.Free,
                AmigaBusRequester.Host,
                AmigaBusAccessKind.HostTrap,
                AmigaBusAccessTarget.ChipRam,
                address: 0,
                value: 0,
                addressValid: false,
                valueValid: false,
                granted: false,
                isWrite: false,
                channel: 0,
                phase: 0);
    }
}
