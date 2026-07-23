/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;
using CopperMod.Amiga.CustomChips.Agnus;

namespace CopperMod.Amiga.Bus
{
    internal enum AgnusBusAgendaSource : byte
    {
        Display,
        Paula,
        Disk,
        Copper,
        Blitter,
        Cpu,
        Control,
        Raster,
        Count
    }

    [Flags]
    internal enum AgnusBusExecutionResult : byte
    {
        None = 0,
        Paula = 1 << 0,
        Disk = 1 << 1,
        Fixed = 1 << 2,
        Blitter = 1 << 3
    }

    [Flags]
    internal enum AgnusBusIntentFlags : byte
    {
        None = 0,
        Pending = 1 << 0,
        Write = 1 << 1,
        FixedSlot = 1 << 2
    }

    internal struct AgnusBusIntent
    {
        public AmigaBusRequester Requester;
        public AmigaBusAccessKind Kind;
        public AmigaBusAccessTarget Target;
        public AmigaBusAccessSize Size;
        public AgnusBusIntentFlags Flags;
        public byte Channel;
        public byte Phase;
        public uint Address;
        public long EarliestCycle;

        public readonly bool Pending => (Flags & AgnusBusIntentFlags.Pending) != 0;

        public readonly bool IsWrite => (Flags & AgnusBusIntentFlags.Write) != 0;

        public void Clear() => this = default;
    }

    internal readonly struct AgnusControlEvent
    {
        public AgnusControlEvent(long cycle, AgnusBusAgendaSource source)
        {
            if (cycle < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cycle));
            }

            Cycle = cycle;
            Source = source;
        }

        public long Cycle { get; }

        public AgnusBusAgendaSource Source { get; }
    }

    internal readonly struct CpuTimingSequenceRequest
    {
        public CpuTimingSequenceRequest(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long firstRequestedCycle,
            int wordCount,
            bool isWrite,
            ulong instructionFetchShapeBits = 0)
        {
            Kind = kind;
            Target = target;
            Address = address;
            FirstRequestedCycle = firstRequestedCycle;
            WordCount = wordCount;
            IsWrite = isWrite;
            InstructionFetchShapeBits = instructionFetchShapeBits;
        }

        public AmigaBusAccessKind Kind { get; }
        public AmigaBusAccessTarget Target { get; }
        public uint Address { get; }
        public long FirstRequestedCycle { get; }
        public int WordCount { get; }
        public bool IsWrite { get; }
        public ulong InstructionFetchShapeBits { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessKind GetWordKind(int index)
            => ((InstructionFetchShapeBits >> index) & 1UL) != 0
                ? AmigaBusAccessKind.CpuInstructionFetch
                : Kind;
    }

    internal readonly struct CpuTimingSequenceResult
    {
        public CpuTimingSequenceResult(
            int completedWords,
            long firstGrantedCycle,
            long lastGrantedCycle,
            long completedCycle,
            long cleanThroughCycle,
            long totalWaitCycles)
        {
            CompletedWords = completedWords;
            FirstGrantedCycle = firstGrantedCycle;
            LastGrantedCycle = lastGrantedCycle;
            CompletedCycle = completedCycle;
            CleanThroughCycle = cleanThroughCycle;
            TotalWaitCycles = totalWaitCycles;
        }

        public int CompletedWords { get; }
        public long FirstGrantedCycle { get; }
        public long LastGrantedCycle { get; }
        public long CompletedCycle { get; }
        public long CleanThroughCycle { get; }
        public long TotalWaitCycles { get; }
    }

    internal sealed class AgnusDisplayControlState
    {
        public const ulong DmaconWritten = 1UL << 0;
        public const ulong DiwStartWritten = 1UL << 1;
        public const ulong DiwStopWritten = 1UL << 2;
        public const ulong DiwHighWritten = 1UL << 3;
        public const ulong DdfStartWritten = 1UL << 4;
        public const ulong DdfStopWritten = 1UL << 5;
        public const ulong Bplcon0Written = 1UL << 6;
        public const ulong Bplcon1Written = 1UL << 7;
        public const ulong Bplcon2Written = 1UL << 8;
        public const ulong Bplcon3Written = 1UL << 9;
        public const ulong Bpl1ModWritten = 1UL << 10;
        public const ulong Bpl2ModWritten = 1UL << 11;
        public readonly uint[] BitplanePointers = new uint[8];
        public readonly uint[] SpritePointers = new uint[8];
        public readonly uint[] SpriteNextFetchAddresses = new uint[8];
        public readonly uint[] BitplaneLastFetchAddresses = new uint[8];
        public readonly long[] BitplaneLastFetchCycles = new long[8];
        public readonly long[] BitplaneGrantCounts = new long[8];
        public readonly uint[] SpriteLastFetchAddresses = new uint[8];
        public readonly long[] SpriteLastFetchCycles = new long[8];
        public readonly long[] SpriteGrantCounts = new long[8];
        public ushort Dmacon;
        public ushort DiwStart;
        public ushort DiwStop;
        public ushort DiwHigh;
        public ushort DdfStart;
        public ushort DdfStop;
        public ushort Bplcon0;
        public ushort Bplcon1;
        public ushort Bplcon2;
        public ushort Bplcon3;
        public short Bpl1Mod;
        public short Bpl2Mod;
        public long LastWriteCycle = -1;
        public ulong Version;
        public long AppliedWriteCount;
        public long IgnoredHistoricalWriteCount;
        public ulong WrittenMask;
        public long LineStateMatches;
        public long LineStateMismatches;
        public long LineStateDeferredComparisons;
        public string FirstLineStateMismatch = string.Empty;
        public byte BitplanePointerWrittenMask;
        public long BitplanePointerMatches;
        public long BitplanePointerMismatches;
        public string FirstBitplanePointerMismatch = string.Empty;
        public byte SpritePointerWrittenMask;
        public long SpriteAddressMatches;
        public long SpriteAddressMismatches;
        public string FirstSpriteAddressMismatch = string.Empty;

        public void Reset()
        {
            Array.Clear(BitplanePointers);
            Array.Clear(SpritePointers);
            Array.Clear(SpriteNextFetchAddresses);
            Array.Clear(BitplaneLastFetchAddresses);
            Array.Fill(BitplaneLastFetchCycles, -1);
            Array.Clear(BitplaneGrantCounts);
            Array.Clear(SpriteLastFetchAddresses);
            Array.Fill(SpriteLastFetchCycles, -1);
            Array.Clear(SpriteGrantCounts);
            Dmacon = 0;
            DiwStart = 0;
            DiwStop = 0;
            DiwHigh = 0;
            DdfStart = 0;
            DdfStop = 0;
            Bplcon0 = 0;
            Bplcon1 = 0;
            Bplcon2 = 0;
            Bplcon3 = 0;
            Bpl1Mod = 0;
            Bpl2Mod = 0;
            LastWriteCycle = -1;
            Version = 0;
            AppliedWriteCount = 0;
            IgnoredHistoricalWriteCount = 0;
            WrittenMask = 0;
            LineStateMatches = 0;
            LineStateMismatches = 0;
            LineStateDeferredComparisons = 0;
            FirstLineStateMismatch = string.Empty;
            BitplanePointerWrittenMask = 0;
            BitplanePointerMatches = 0;
            BitplanePointerMismatches = 0;
            FirstBitplanePointerMismatch = string.Empty;
            SpritePointerWrittenMask = 0;
            SpriteAddressMatches = 0;
            SpriteAddressMismatches = 0;
            FirstSpriteAddressMismatch = string.Empty;
        }
    }

    internal readonly struct CpuWordRequest
    {
        public CpuWordRequest(
            uint address,
            long requestedCycle,
            AmigaBusAccessKind kind,
            bool isWrite,
            ushort value = 0)
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

        public ushort Value { get; }
    }

    internal readonly struct CpuWordResult
    {
        public CpuWordResult(ushort value, long grantedCycle, long completedCycle)
        {
            Value = value;
            GrantedCycle = grantedCycle;
            CompletedCycle = completedCycle;
        }

        public ushort Value { get; }

        public long GrantedCycle { get; }

        public long CompletedCycle { get; }
    }

    /// <summary>
    /// Fixed-size deadline tournament used by the causal Agnus executor. Updating one
    /// requester is O(log N); reading the next raw eligibility cycle is O(1).
    /// </summary>
    internal sealed class AgnusBusDeadlineAgenda
    {
        private const int LeafCount = 8;
        private readonly long[] _tree = new long[LeafCount * 2];

        public AgnusBusDeadlineAgenda() => Reset();

        public long NextCycle => _tree[1];

        public void Reset()
        {
            Array.Fill(_tree, long.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Get(AgnusBusAgendaSource source)
            => _tree[LeafCount + (int)source];

        public void Set(AgnusBusAgendaSource source, long cycle)
        {
            if ((uint)source >= (uint)AgnusBusAgendaSource.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(source));
            }

            if (cycle < 0 && cycle != long.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(cycle));
            }

            var index = LeafCount + (int)source;
            if (_tree[index] == cycle)
            {
                return;
            }

            _tree[index] = cycle;
            do
            {
                index >>= 1;
                _tree[index] = Math.Min(_tree[index << 1], _tree[(index << 1) + 1]);
            }
            while (index > 1);
        }
    }

    /// <summary>
    /// Chronological owner of slot-contended advancement. The first migration stage
    /// retains the proven HRM slot engine as its arbitration core while moving wake
    /// ownership and persistent request storage behind this boundary.
    /// </summary>
    internal sealed partial class AgnusBusExecutor
    {
        private const string ShadowEnvironmentVariable = "COPPERMOD_AMIGA_CAUSAL_AGNUS_SHADOW";
        private const string ProductionEnvironmentVariable = "COPPERMOD_AMIGA_CAUSAL_AGNUS_EXECUTOR";
        private readonly Bus _bus;
        private readonly AgnusHrmSlotEngine _slots;
        private readonly AgnusBusDeadlineAgenda _agenda = new AgnusBusDeadlineAgenda();
        private readonly CpuEventJournal _cpuEventJournal;
        private bool _flushingCpuEventJournal;
        private bool _legacyCpuSlotRequestPending;
        private ulong _chipWriteHazardDiskVersion = ulong.MaxValue;
        private ulong _chipWriteHazardBlitterVersion = ulong.MaxValue;
        private ulong _chipWriteHazardDiskDmaconVersion = ulong.MaxValue;
        private ulong _chipWriteHazardBlitterDmaconVersion = ulong.MaxValue;
        private long _diskChipWriteHazardCycle = long.MaxValue;
        private long _blitterChipWriteHazardCycle = long.MaxValue;
        private long _chipWriteHazardRefreshes;
        private readonly AgnusBusIntent[] _intents = new AgnusBusIntent[(int)AgnusBusAgendaSource.Count];
        private readonly AgnusBusIntent[] _paulaIntents = new AgnusBusIntent[AmigaConstants.PaulaChannelCount];
        private ulong _paulaVersion = ulong.MaxValue;
        private ulong _diskVersion = ulong.MaxValue;
        private ulong _displayVersion = ulong.MaxValue;
        private long _executedThroughCycle = -1;
        private long _unresolvedCpuTimingFenceCycle = long.MaxValue;
        private long _unresolvedCpuEventFenceCycle = long.MaxValue;
        private long _agendaUpdates;
        private long _agendaReads;
        private long _shadowMatches;
        private long _shadowMismatches;
        private long _fixedPlanShadowMatches;
        private long _fixedPlanShadowMismatches;
        private string _firstFixedPlanShadowMismatch = string.Empty;
        private long _copperGrantedWords;
        private long _copperDeniedWords;
        private uint _lastCopperAddress;
        private ushort _lastCopperValue;
        private long _lastCopperGrantedCycle = -1;
        private bool _pendingCopperMove;
        private ushort _pendingCopperMoveRegister;
        private ushort _pendingCopperMoveValue;
        private long _pendingCopperMoveCycle = -1;
        private long _copperMoveEventsScheduled;
        private long _copperMoveEventsCommitted;
        private long _copperCopjmpEventsCommitted;
        private long _copperDmaconEventsCommitted;
        private ushort _lastCopperMoveRegister;
        private ushort _lastCopperMoveValue;
        private long _lastCopperMoveCycle = -1;
        private long _blitterGrantedReads;
        private long _blitterGrantedWrites;
        private long _blitterDeniedWords;
        private uint _lastBlitterAddress;
        private ushort _lastBlitterValue;
        private long _lastBlitterGrantedCycle = -1;
        private long _paulaGrantedWords;
        private long _paulaDeniedWords;
        private readonly long[] _paulaChannelGrantedWords = new long[AmigaConstants.PaulaChannelCount];
        private readonly uint[] _lastPaulaAddresses = new uint[AmigaConstants.PaulaChannelCount];
        private readonly ushort[] _lastPaulaValues = new ushort[AmigaConstants.PaulaChannelCount];
        private readonly long[] _lastPaulaGrantedCycles = new long[AmigaConstants.PaulaChannelCount];
        private long _diskGrantedReads;
        private long _diskGrantedWrites;
        private long _diskDeniedWords;
        private uint _lastDiskAddress;
        private ushort _lastDiskValue;
        private long _lastDiskGrantedCycle = -1;
        private long _cpuGrantedWords;
        private long _cpuDeniedWords;
        private long _cpuLongWordPhases;
        private long _cpuInstructionFetchGrantedWords;
        private long _cpuDataGrantedWords;
        private long _cpuTimingSequenceRuns;
        private long _cpuTimingSequenceWords;
        private long _cpuTimingSequenceAttempts;
        private long _cpuTimingSequenceBarrierRejects;
        private long _cpuTimingSequenceSlotRejects;
        private uint _lastCpuAddress;
        private long _lastCpuGrantedCycle = -1;
        private string _firstShadowMismatch = string.Empty;
        private long _cachedQueryCurrentCycle;
        private long _cachedQueryTargetCycle;
        private long _cachedQueryResult;
        private bool _queryCacheValid;
        private bool _advancing;

        public AgnusBusExecutor(
            Bus bus,
            AgnusHrmSlotEngine slots,
            int cpuEventJournalCapacity = CpuEventJournal.DefaultCapacity)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _slots = slots ?? throw new ArgumentNullException(nameof(slots));
            _cpuEventJournal = new CpuEventJournal(cpuEventJournalCapacity);
            RasterlinePlans = new AgnusRasterlineDmaPlanRing(
                CustomChips.Denise.Display.MaxRowDmaBitplaneEntriesPerRow,
                CustomChips.Denise.Display.MaxRowDmaSpriteEntriesPerRow);
            DisplayControlState = new AgnusDisplayControlState();
        }

        public long ExecutedThroughCycle => _executedThroughCycle;

        public long AgendaUpdates => _agendaUpdates;

        public long AgendaReads => _agendaReads;

        public long ShadowMatches => _shadowMatches;

        public long ShadowMismatches => _shadowMismatches;

        public long FixedPlanShadowMatches => _fixedPlanShadowMatches;

        public long FixedPlanShadowMismatches => _fixedPlanShadowMismatches;

        public string FirstFixedPlanShadowMismatch => _firstFixedPlanShadowMismatch;

        public long CopperGrantedWords => _copperGrantedWords;

        public long CopperDeniedWords => _copperDeniedWords;

        public uint LastCopperAddress => _lastCopperAddress;

        public ushort LastCopperValue => _lastCopperValue;

        public long LastCopperGrantedCycle => _lastCopperGrantedCycle;

        public bool HasPendingCopperMove => _pendingCopperMove;

        public long PendingCopperMoveCycle => _pendingCopperMoveCycle;

        public long CopperMoveEventsScheduled => _copperMoveEventsScheduled;

        public long CopperMoveEventsCommitted => _copperMoveEventsCommitted;

        public long CopperCopjmpEventsCommitted => _copperCopjmpEventsCommitted;

        public long CopperDmaconEventsCommitted => _copperDmaconEventsCommitted;

        public ushort LastCopperMoveRegister => _lastCopperMoveRegister;

        public ushort LastCopperMoveValue => _lastCopperMoveValue;

        public long LastCopperMoveCycle => _lastCopperMoveCycle;

        public long BlitterGrantedReads => _blitterGrantedReads;

        public long BlitterGrantedWrites => _blitterGrantedWrites;

        public long BlitterDeniedWords => _blitterDeniedWords;

        public uint LastBlitterAddress => _lastBlitterAddress;

        public ushort LastBlitterValue => _lastBlitterValue;

        public long LastBlitterGrantedCycle => _lastBlitterGrantedCycle;

        public long PaulaGrantedWords => _paulaGrantedWords;

        public long PaulaDeniedWords => _paulaDeniedWords;

        public long GetPaulaChannelGrantedWords(int channel) => _paulaChannelGrantedWords[channel];

        public uint GetLastPaulaAddress(int channel) => _lastPaulaAddresses[channel];

        public ushort GetLastPaulaValue(int channel) => _lastPaulaValues[channel];

        public long GetLastPaulaGrantedCycle(int channel) => _lastPaulaGrantedCycles[channel];

        public ref readonly AgnusBusIntent GetPaulaIntent(int channel) => ref _paulaIntents[channel];

        public long DiskGrantedReads => _diskGrantedReads;

        public long DiskGrantedWrites => _diskGrantedWrites;

        public long DiskDeniedWords => _diskDeniedWords;

        public uint LastDiskAddress => _lastDiskAddress;

        public ushort LastDiskValue => _lastDiskValue;

        public long LastDiskGrantedCycle => _lastDiskGrantedCycle;

        public long CpuGrantedWords => _cpuGrantedWords;

        public long CpuDeniedWords => _cpuDeniedWords;

        public long CpuLongWordPhases => _cpuLongWordPhases;

        public long CpuInstructionFetchGrantedWords => _cpuInstructionFetchGrantedWords;

        public long CpuDataGrantedWords => _cpuDataGrantedWords;

        public long CpuTimingSequenceRuns => _cpuTimingSequenceRuns;

        public long CpuTimingSequenceWords => _cpuTimingSequenceWords;

        public long CpuTimingSequenceAttempts => _cpuTimingSequenceAttempts;

        public long CpuTimingSequenceBarrierRejects => _cpuTimingSequenceBarrierRejects;

        public long CpuTimingSequenceSlotRejects => _cpuTimingSequenceSlotRejects;

        public uint LastCpuAddress => _lastCpuAddress;

        public long LastCpuGrantedCycle => _lastCpuGrantedCycle;

        public CpuEventJournal CpuEventJournal => _cpuEventJournal;

        public bool IsFlushingCpuEventJournal => _flushingCpuEventJournal;

        public bool HasPendingCpuWriteOverlap(uint address, int byteCount)
            => _cpuEventJournal.HasPendingOverlap(address, byteCount);

        public long CpuJournalDeadlineCycle => _agenda.Get(AgnusBusAgendaSource.Cpu);

        public long ChipRamWriteHazardRefreshes => _chipWriteHazardRefreshes;

        public bool MayWriteChipRamBefore(long cycle)
        {
            if (cycle < 0)
            {
                return false;
            }

            if (_cpuEventJournal.Count != 0 && CpuJournalDeadlineCycle <= cycle)
            {
                return true;
            }

            ref readonly var cpu = ref _intents[(int)AgnusBusAgendaSource.Cpu];
            ref readonly var disk = ref _intents[(int)AgnusBusAgendaSource.Disk];
            ref readonly var blitter = ref _intents[(int)AgnusBusAgendaSource.Blitter];
            if ((cpu.Pending && cpu.IsWrite && cpu.EarliestCycle <= cycle) ||
                (disk.Pending && disk.IsWrite && disk.EarliestCycle <= cycle) ||
                (blitter.Pending && blitter.IsWrite && blitter.EarliestCycle <= cycle))
            {
                return true;
            }

            RefreshChipRamWriteHazards();
            return _diskChipWriteHazardCycle <= cycle ||
                _blitterChipWriteHazardCycle <= cycle;
        }

        private void RefreshChipRamWriteHazards()
        {
            var diskVersion = _bus.Disk.SchedulerWakeVersion;
            var dmaconVersion = _bus.Paula.RegisterWakeVersion;
            if (_chipWriteHazardDiskVersion != diskVersion ||
                _chipWriteHazardDiskDmaconVersion != dmaconVersion)
            {
                _chipWriteHazardDiskVersion = diskVersion;
                _chipWriteHazardDiskDmaconVersion = dmaconVersion;
                _diskChipWriteHazardCycle = _bus.Disk.GetChipRamWriteHazardCycle();
                _chipWriteHazardRefreshes++;
            }

            var blitterVersion = _bus.Blitter.WakeVersion;
            if (_chipWriteHazardBlitterVersion != blitterVersion ||
                _chipWriteHazardBlitterDmaconVersion != dmaconVersion)
            {
                _chipWriteHazardBlitterVersion = blitterVersion;
                _chipWriteHazardBlitterDmaconVersion = dmaconVersion;
                _blitterChipWriteHazardCycle = _bus.Blitter.GetChipRamWriteHazardCycle();
                _chipWriteHazardRefreshes++;
            }
        }

        public string FirstShadowMismatch => _firstShadowMismatch;

        public bool ShadowEnabled { get; } = ReadBooleanEnvironmentVariable(ShadowEnvironmentVariable, false);

        public bool ProductionEnabled { get; } = ReadBooleanEnvironmentVariable(ProductionEnvironmentVariable, true);

        public AgnusHrmSlotEngine Slots => _slots;

        public AgnusRasterlineDmaPlanRing RasterlinePlans { get; }

        public AgnusDisplayControlState DisplayControlState { get; }

        public void Reset()
        {
            _agenda.Reset();
            ResetCpuVisibilityAgenda();
            _cpuEventJournal.Reset();
            _chipWriteHazardDiskVersion = ulong.MaxValue;
            _chipWriteHazardBlitterVersion = ulong.MaxValue;
            _chipWriteHazardDiskDmaconVersion = ulong.MaxValue;
            _chipWriteHazardBlitterDmaconVersion = ulong.MaxValue;
            _diskChipWriteHazardCycle = long.MaxValue;
            _blitterChipWriteHazardCycle = long.MaxValue;
            _chipWriteHazardRefreshes = 0;
            RasterlinePlans.Reset();
            DisplayControlState.Reset();
            Array.Clear(_intents);
            Array.Clear(_paulaIntents);
            _paulaVersion = ulong.MaxValue;
            _diskVersion = ulong.MaxValue;
            _displayVersion = ulong.MaxValue;
            _executedThroughCycle = -1;
            _unresolvedCpuTimingFenceCycle = long.MaxValue;
            _unresolvedCpuEventFenceCycle = long.MaxValue;
            _agendaUpdates = 0;
            _agendaReads = 0;
            _shadowMatches = 0;
            _shadowMismatches = 0;
            _fixedPlanShadowMatches = 0;
            _fixedPlanShadowMismatches = 0;
            _firstFixedPlanShadowMismatch = string.Empty;
            _copperGrantedWords = 0;
            _copperDeniedWords = 0;
            _lastCopperAddress = 0;
            _lastCopperValue = 0;
            _lastCopperGrantedCycle = -1;
            _pendingCopperMove = false;
            _pendingCopperMoveRegister = 0;
            _pendingCopperMoveValue = 0;
            _pendingCopperMoveCycle = -1;
            _copperMoveEventsScheduled = 0;
            _copperMoveEventsCommitted = 0;
            _copperCopjmpEventsCommitted = 0;
            _copperDmaconEventsCommitted = 0;
            _lastCopperMoveRegister = 0;
            _lastCopperMoveValue = 0;
            _lastCopperMoveCycle = -1;
            _blitterGrantedReads = 0;
            _blitterGrantedWrites = 0;
            _blitterDeniedWords = 0;
            _lastBlitterAddress = 0;
            _lastBlitterValue = 0;
            _lastBlitterGrantedCycle = -1;
            _paulaGrantedWords = 0;
            _paulaDeniedWords = 0;
            Array.Clear(_paulaChannelGrantedWords);
            Array.Clear(_lastPaulaAddresses);
            Array.Clear(_lastPaulaValues);
            Array.Fill(_lastPaulaGrantedCycles, -1L);
            _diskGrantedReads = 0;
            _diskGrantedWrites = 0;
            _diskDeniedWords = 0;
            _lastDiskAddress = 0;
            _lastDiskValue = 0;
            _lastDiskGrantedCycle = -1;
            _cpuGrantedWords = 0;
            _cpuDeniedWords = 0;
            _cpuLongWordPhases = 0;
            _cpuInstructionFetchGrantedWords = 0;
            _cpuDataGrantedWords = 0;
            _cpuTimingSequenceRuns = 0;
            _cpuTimingSequenceWords = 0;
            _cpuTimingSequenceAttempts = 0;
            _cpuTimingSequenceBarrierRejects = 0;
            _cpuTimingSequenceSlotRejects = 0;
            _lastCpuAddress = 0;
            _lastCpuGrantedCycle = -1;
            _firstShadowMismatch = string.Empty;
            _queryCacheValid = false;
            _advancing = false;
        }

        public long AdvanceThrough(long targetCycle)
        {
            targetCycle = Math.Max(0, targetCycle);
            var unresolvedCpuFenceCycle = GetUnresolvedCpuFenceCycle();
            if (unresolvedCpuFenceCycle != long.MaxValue)
            {
                targetCycle = Math.Min(
                    targetCycle,
                    unresolvedCpuFenceCycle - 1);
                if (targetCycle < 0)
                {
                    return _executedThroughCycle;
                }
            }

            if (_advancing)
            {
                _bus.AdvanceCausalBusCoreThrough(targetCycle);
                return _executedThroughCycle;
            }

            _advancing = true;
            try
            {
                _bus.AdvanceCausalBusCoreThrough(targetCycle);
                _executedThroughCycle = Math.Max(_executedThroughCycle, targetCycle);
            }
            finally
            {
                _advancing = false;
            }

            return _executedThroughCycle;
        }

        internal long UnresolvedCpuTimingFenceCycle => _unresolvedCpuTimingFenceCycle;

        internal long UnresolvedCpuEventFenceCycle => _unresolvedCpuEventFenceCycle;

        internal void PublishUnresolvedCpuTiming(long requestedCycle)
        {
            var fenceCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle));
            if (fenceCycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot publish unresolved CPU timing at cycle {fenceCycle} behind executed horizon {_executedThroughCycle}.");
            }

            _unresolvedCpuTimingFenceCycle = Math.Min(
                _unresolvedCpuTimingFenceCycle,
                fenceCycle);
        }

        internal void ClearUnresolvedCpuTiming()
            => _unresolvedCpuTimingFenceCycle = long.MaxValue;

        private void PublishUnresolvedCpuEvent(long requestedCycle)
        {
            var fenceCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle));
            if (fenceCycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot publish unresolved CPU event at cycle {fenceCycle} behind executed horizon {_executedThroughCycle}.");
            }

            _unresolvedCpuEventFenceCycle = Math.Min(
                _unresolvedCpuEventFenceCycle,
                fenceCycle);
        }

        private void ClearUnresolvedCpuEvent()
            => _unresolvedCpuEventFenceCycle = long.MaxValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetUnresolvedCpuFenceCycle()
            => Math.Min(_unresolvedCpuTimingFenceCycle, _unresolvedCpuEventFenceCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ClampPhysicalCommitTarget(long targetCycle)
        {
            var fenceCycle = GetUnresolvedCpuFenceCycle();
            return fenceCycle == long.MaxValue
                ? targetCycle
                : Math.Min(targetCycle, fenceCycle - 1);
        }

        public CpuWordResult ExecuteCpuWord(in CpuWordRequest request)
        {
            var cycle = request.RequestedCycle;
            if (request.IsWrite)
            {
                if (!_bus.TryWriteExactCpuDataWord(request.Address, request.Value, ref cycle) &&
                    !_bus.TryWriteJournaledCpuCustomWord(
                        request.Address,
                        request.Value,
                        ref cycle,
                        request.Kind))
                {
                    _bus.WriteWord(request.Address, request.Value, ref cycle, request.Kind);
                }
                return new CpuWordResult(
                    request.Value,
                    cycle - AgnusChipSlotScheduler.SlotCycles,
                    cycle);
            }

            var value = _bus.TryReadExactCpuDataWord(request.Address, ref cycle, out var exactValue)
                ? exactValue
                : _bus.ReadWord(request.Address, ref cycle, request.Kind);
            return new CpuWordResult(
                value,
                cycle - AgnusChipSlotScheduler.SlotCycles,
                cycle);
        }

        public bool TryEnqueueCpuChipWordWrite(
            uint address,
            ushort value,
            long requestedCycle,
            CpuJournalInstructionPhase phase,
            CpuJournalDependencyFlags dependencies,
            out ulong sequence)
        {
            var wasEmpty = _cpuEventJournal.Count == 0;
            var accepted = _cpuEventJournal.TryEnqueue(
                requestedCycle,
                phase,
                AmigaBusAccessTarget.ChipRam,
                address,
                AmigaBusAccessKind.CpuDataWrite,
                AmigaBusAccessSize.Word,
                isWrite: true,
                value,
                dependencies | CpuJournalDependencyFlags.MemoryWrite,
                out sequence);
            if (accepted && wasEmpty)
            {
                SetDeadline(AgnusBusAgendaSource.Cpu, requestedCycle);
                PublishUnresolvedCpuEvent(requestedCycle);
            }

            return accepted;
        }

        public bool TryEnqueueCpuCustomWordWrite(
            uint address,
            ushort value,
            long requestedCycle,
            CpuJournalInstructionPhase phase,
            out ulong sequence)
        {
            var wasEmpty = _cpuEventJournal.Count == 0;
            var accepted = _cpuEventJournal.TryEnqueue(
                requestedCycle,
                phase,
                AmigaBusAccessTarget.CustomRegisters,
                address,
                AmigaBusAccessKind.CpuDataWrite,
                AmigaBusAccessSize.Word,
                isWrite: true,
                value,
                CpuJournalDependencyFlags.ControlWrite,
                out sequence);
            if (accepted && wasEmpty)
            {
                SetDeadline(AgnusBusAgendaSource.Cpu, requestedCycle);
                PublishUnresolvedCpuEvent(requestedCycle);
            }

            return accepted;
        }

        public bool TryEnqueueCpuChipLongWrite(
            uint address,
            uint value,
            long requestedCycle,
            CpuJournalInstructionPhase phase,
            out ulong firstSequence,
            out ulong secondSequence)
        {
            firstSequence = 0;
            secondSequence = 0;
            if (_cpuEventJournal.AvailableCount < 2)
            {
                return false;
            }

            if (!TryEnqueueCpuChipWordWrite(
                address,
                (ushort)(value >> 16),
                requestedCycle,
                phase,
                CpuJournalDependencyFlags.LongWordFirstHalf,
                out firstSequence))
            {
                return false;
            }

            return TryEnqueueCpuChipWordWrite(
                address + 2,
                (ushort)value,
                requestedCycle + AgnusChipSlotScheduler.SlotCycles,
                phase,
                CpuJournalDependencyFlags.LongWordSecondHalf,
                out secondSequence);
        }

        public long FlushCpuEventJournal()
        {
            var ignoredCycle = 0L;
            return FlushCpuEventJournalCore(ref ignoredCycle, adjustCpuCycle: false);
        }

        public long FlushCpuEventJournal(ref long cpuCycle)
            => FlushCpuEventJournalCore(ref cpuCycle, adjustCpuCycle: true);

        private long FlushCpuEventJournalCore(ref long cpuCycle, bool adjustCpuCycle)
        {
            var completedCycle = -1L;
            var accumulatedDelay = 0L;
            ClearUnresolvedCpuEvent();
            _flushingCpuEventJournal = true;
            try
            {
                while (_cpuEventJournal.Count != 0)
                {
                    ref var entry = ref _cpuEventJournal.Peek();
                    if (entry.Target is not (
                            AmigaBusAccessTarget.ChipRam or
                            AmigaBusAccessTarget.CustomRegisters) ||
                        entry.Size != AmigaBusAccessSize.Word ||
                        !entry.IsWrite)
                    {
                        throw new InvalidOperationException(
                            "Only CPU Chip RAM and classified custom-register word writes are supported by the journal.");
                    }

                    var virtualCompletedCycle = entry.RequestedCycle + AgnusChipSlotScheduler.SlotCycles;
                    var requestedCycle = entry.RequestedCycle + accumulatedDelay;
                    var request = new CpuWordRequest(
                        entry.Address,
                        requestedCycle,
                        entry.Kind,
                        isWrite: true,
                        (ushort)entry.Value);
                    var result = ExecuteCpuWord(in request);
                    completedCycle = result.CompletedCycle;
                    accumulatedDelay = result.CompletedCycle - virtualCompletedCycle;
                    _cpuEventJournal.CommitHead(result.GrantedCycle, result.CompletedCycle);
                    SetDeadline(
                        AgnusBusAgendaSource.Cpu,
                        _cpuEventJournal.Count == 0
                            ? long.MaxValue
                            : _cpuEventJournal.Peek().RequestedCycle + accumulatedDelay);
                }
            }
            finally
            {
                _flushingCpuEventJournal = false;
            }

            if (adjustCpuCycle && accumulatedDelay > 0)
            {
                cpuCycle += accumulatedDelay;
            }

            return completedCycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult Arbitrate(AmigaBusAccessRequest request, AmigaBusAccessResult baseResult)
        {
            // This path arbitrates synchronously; unlike BeginPendingCpuSlotRequest,
            // its CPU request cannot be observed by an intervening scheduler query.
            // Publishing it to the deadline tree only dirties and restores the CPU
            // leaf around every ordinary memory access.
            var result = _slots.Arbitrate(request, baseResult);
            if (request.Requester == AmigaBusRequester.Cpu &&
                request.Target is AmigaBusAccessTarget.ChipRam or AmigaBusAccessTarget.CustomRegisters)
            {
                if (request.Size == AmigaBusAccessSize.Long)
                {
                    RecordGrantedCpuWord(request.Kind, request.Address, result.GrantedCycle, isLongWordPhase: true);
                    RecordGrantedCpuWord(
                        request.Kind,
                        request.Address + 2,
                        result.GrantedCycle + AgnusChipSlotScheduler.SlotCycles,
                        isLongWordPhase: true);
                }
                else
                {
                    RecordGrantedCpuWord(request.Kind, request.Address, result.GrantedCycle, isLongWordPhase: false);
                }
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReserveFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
            => _slots.TryReserveFixedDmaSlot(request, out result);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReserveExactFixedDmaSlot(AmigaBusAccessRequest request, out AmigaBusAccessResult result)
            => _slots.TryReserveExactFixedDmaSlot(request, out result);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult ReserveBitplaneDmaSlot(uint address, long requestedCycle)
            => _slots.ReserveBitplaneDmaSlot(address, requestedCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult ReserveCopperDmaSlot(uint address, long requestedCycle)
            => _slots.ReserveCopperDmaSlot(address, requestedCycle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult ReservePaulaDmaWordSlot(int channel, uint address, long requestedCycle)
            => _slots.ReservePaulaDmaWordSlot(channel, address, requestedCycle);

        public AmigaDmaWordExecutionResult ExecutePaulaWord(int channel, uint address, long requestedCycle)
        {
            ValidatePaulaChannel(channel);
            SetPaulaIntent(channel, address, requestedCycle);
            var execution = _bus.ExecutePaulaDmaWordRead(channel, address, requestedCycle);
            RecordGrantedPaulaWord(channel, address, execution.Value, execution.Access.GrantedCycle);
            ClearPaulaIntent(channel);
            return execution;
        }

        public bool TryExecutePaulaWordExact(
            int channel,
            uint address,
            long slotCycle,
            out AmigaDmaWordExecutionResult execution)
        {
            ValidatePaulaChannel(channel);
            SetPaulaIntent(channel, address, slotCycle);
            if (!_bus.TryExecutePaulaDmaWordReadExactSlot(channel, address, slotCycle, out execution))
            {
                _paulaDeniedWords++;
                SetPaulaIntent(
                    channel,
                    address,
                    AgnusChipSlotScheduler.AlignToSlot(slotCycle + AgnusChipSlotScheduler.SlotCycles));
                return false;
            }

            RecordGrantedPaulaWord(channel, address, execution.Value, execution.Access.GrantedCycle);
            ClearPaulaIntent(channel);
            return true;
        }

        private void SetPaulaIntent(int channel, uint address, long cycle)
        {
            _paulaIntents[channel] = new AgnusBusIntent
            {
                Requester = AmigaBusRequester.Paula,
                Kind = AmigaBusAccessKind.PaulaDma,
                Target = AmigaBusAccessTarget.ChipRam,
                Size = AmigaBusAccessSize.Word,
                Flags = AgnusBusIntentFlags.Pending,
                Address = _bus.MaskChipDmaAddress(address),
                EarliestCycle = Math.Max(0, cycle),
                Channel = (byte)channel
            };
            if (ProductionEnabled)
            {
                RefreshPaulaDeadline();
            }
        }

        private void ClearPaulaIntent(int channel)
        {
            _paulaIntents[channel].Clear();
            if (ProductionEnabled)
            {
                RefreshPaulaDeadline();
            }
        }

        private void RefreshPaulaDeadline()
        {
            var deadline = _bus.Paula.GetRawDmaEligibilityCycle();
            if (ProductionEnabled)
            {
                for (var channel = 0; channel < _paulaIntents.Length; channel++)
                {
                    if (_paulaIntents[channel].Pending)
                    {
                        deadline = Math.Min(deadline, _paulaIntents[channel].EarliestCycle);
                    }
                }
            }

            SetDeadline(AgnusBusAgendaSource.Paula, deadline);
        }

        private void RecordGrantedPaulaWord(int channel, uint address, ushort value, long cycle)
        {
            _paulaGrantedWords++;
            _paulaChannelGrantedWords[channel]++;
            _lastPaulaAddresses[channel] = _bus.MaskChipDmaAddress(address);
            _lastPaulaValues[channel] = value;
            _lastPaulaGrantedCycles[channel] = cycle;
        }

        private static void ValidatePaulaChannel(int channel)
        {
            if ((uint)channel >= AmigaConstants.PaulaChannelCount)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        public bool TryExecuteDiskWordExact(
            uint address,
            bool writeMode,
            ushort diskWordValue,
            long slotCycle,
            out AmigaDmaWordExecutionResult execution)
        {
            SetDiskIntent(address, writeMode, slotCycle);
            if (!_bus.TryExecuteDiskDmaWordExactSlot(
                address,
                writeMode,
                diskWordValue,
                slotCycle,
                out execution))
            {
                _diskDeniedWords++;
                SetDiskIntent(
                    address,
                    writeMode,
                    _bus.PredictDiskDmaGrantCycle(slotCycle + AgnusChipSlotScheduler.SlotCycles));
                return false;
            }

            RecordGrantedDiskWord(address, execution.Value, execution.Access.GrantedCycle, writeMode);
            ClearDiskIntent();
            return true;
        }

        public void CancelDiskIntent() => ClearDiskIntent();

        private void SetDiskIntent(uint address, bool writeMode, long cycle)
        {
            var intent = new AgnusBusIntent
            {
                Requester = AmigaBusRequester.Disk,
                Kind = AmigaBusAccessKind.DiskDma,
                Target = AmigaBusAccessTarget.ChipRam,
                Size = AmigaBusAccessSize.Word,
                Flags = AgnusBusIntentFlags.Pending | (!writeMode ? AgnusBusIntentFlags.Write : 0),
                Address = _bus.MaskChipDmaAddress(address),
                EarliestCycle = Math.Max(0, cycle)
            };
            _intents[(int)AgnusBusAgendaSource.Disk] = intent;
            if (ProductionEnabled)
            {
                RefreshDiskDeadline();
            }
        }

        private void ClearDiskIntent()
        {
            _intents[(int)AgnusBusAgendaSource.Disk].Clear();
            if (ProductionEnabled)
            {
                RefreshDiskDeadline();
            }
        }

        private void RefreshDiskDeadline()
        {
            var deadline = _bus.Disk.GetRawSlotDmaEligibilityCycle();
            ref readonly var intent = ref _intents[(int)AgnusBusAgendaSource.Disk];
            if (ProductionEnabled && intent.Pending)
            {
                deadline = Math.Min(deadline, intent.EarliestCycle);
            }

            SetDeadline(AgnusBusAgendaSource.Disk, deadline);
        }

        private void RecordGrantedDiskWord(uint address, ushort value, long cycle, bool writeMode)
        {
            if (writeMode)
            {
                _diskGrantedReads++;
            }
            else
            {
                _diskGrantedWrites++;
            }

            _lastDiskAddress = _bus.MaskChipDmaAddress(address);
            _lastDiskValue = value;
            _lastDiskGrantedCycle = cycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AmigaBusAccessResult ReserveBlitterDmaWordSlot(uint address, long requestedCycle, bool isWrite)
            => _slots.ReserveBlitterDmaWordSlot(address, requestedCycle, isWrite);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReserveCopperDmaWordExactSlot(
            uint address,
            long requestedCycle,
            long slotCycle,
            bool preservePhysicalPhaseAcrossLine,
            out AmigaBusAccessResult result)
            => _slots.TryReserveCopperDmaWordExactSlot(
                address,
                requestedCycle,
                slotCycle,
                preservePhysicalPhaseAcrossLine,
                out result);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long PredictCopperWordCycle(
            uint address,
            long requestedCycle,
            bool preservePhysicalPhaseAcrossLine = false)
            => _bus.PredictLiveCopperDmaWordCycle(
                address,
                requestedCycle,
                preservePhysicalPhaseAcrossLine);

        public bool TryExecuteCopperWordExact(
            uint address,
            long requestedCycle,
            long slotCycle,
            out ushort value,
            out AmigaBusAccessResult access)
            => TryExecuteCopperWordExact(
                address,
                requestedCycle,
                slotCycle,
                preservePhysicalPhaseAcrossLine: false,
                out value,
                out access);

        public bool TryExecuteCopperWordExact(
            uint address,
            long requestedCycle,
            long slotCycle,
            bool preservePhysicalPhaseAcrossLine,
            out ushort value,
            out AmigaBusAccessResult access)
        {
            if (slotCycle <= _bus.ExecutedChipBusHorizon)
            {
                value = 0;
                access = default;
                _copperDeniedWords++;
                return false;
            }

            SetCopperIntent(address, requestedCycle, slotCycle);

            if (!_bus.TryReadLiveCopperDmaWordExactSlot(
                address,
                requestedCycle,
                slotCycle,
                preservePhysicalPhaseAcrossLine,
                out value,
                out access))
            {
                _copperDeniedWords++;
                var retryCycle = _bus.PredictLiveCopperDmaWordCycle(
                    address,
                    slotCycle + AgnusChipSlotScheduler.SlotCycles,
                    preservePhysicalPhaseAcrossLine);
                SetCopperIntent(address, retryCycle, retryCycle);
                return false;
            }

            RecordGrantedCopperWord(address, value, access.GrantedCycle);
            ClearCopperIntent();
            return true;
        }

        public ushort ExecuteCopperWord(
            uint address,
            long requestedCycle,
            out AmigaBusAccessResult access)
        {
            var value = _bus.ReadLiveCopperDmaWord(address, requestedCycle, out access);
            RecordGrantedCopperWord(address, value, access.GrantedCycle);
            ClearCopperIntent();
            return value;
        }

        private void SetCopperIntent(uint address, long requestedCycle, long eligibleCycle)
        {
            var intent = new AgnusBusIntent
            {
                Requester = AmigaBusRequester.Copper,
                Kind = AmigaBusAccessKind.Copper,
                Target = AmigaBusAccessTarget.ChipRam,
                Size = AmigaBusAccessSize.Word,
                Flags = AgnusBusIntentFlags.Pending,
                Address = _bus.MaskChipDmaAddress(address),
                EarliestCycle = Math.Max(requestedCycle, eligibleCycle)
            };
            _intents[(int)AgnusBusAgendaSource.Copper] = intent;
            SetDeadline(AgnusBusAgendaSource.Copper, intent.EarliestCycle);
        }

        private void ClearCopperIntent()
        {
            _intents[(int)AgnusBusAgendaSource.Copper].Clear();
            SetDeadline(AgnusBusAgendaSource.Copper, long.MaxValue);
        }

        public void ScheduleCopperMoveControl(ushort register, ushort value, long cycle)
        {
            if (cycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot schedule Copper MOVE at cycle {cycle} behind executed horizon {_executedThroughCycle}.");
            }

            _pendingCopperMove = true;
            _pendingCopperMoveRegister = register;
            _pendingCopperMoveValue = value;
            _pendingCopperMoveCycle = cycle;
            _copperMoveEventsScheduled++;
            var intent = new AgnusBusIntent
            {
                Requester = AmigaBusRequester.Copper,
                Kind = AmigaBusAccessKind.Copper,
                Target = AmigaBusAccessTarget.CustomRegisters,
                Size = AmigaBusAccessSize.Word,
                Flags = AgnusBusIntentFlags.Pending | AgnusBusIntentFlags.Write,
                Address = 0x00DFF000u + register,
                EarliestCycle = cycle
            };
            _intents[(int)AgnusBusAgendaSource.Control] = intent;
            SetDeadline(AgnusBusAgendaSource.Control, cycle);
        }

        public void CommitCopperMoveControl(ushort register, ushort value, long cycle)
        {
            if (!_pendingCopperMove)
            {
                // A Copper instruction fetched during the current causal drain
                // has already crossed the bus horizon.  Its data phase is an
                // immediate effect of that grant, not a newly scheduled control
                // event.  Account for it here without reconstructing an event in
                // the past.
                if (cycle > _executedThroughCycle)
                {
                    throw new InvalidOperationException(
                        $"Copper MOVE at cycle {cycle} has no pending Agnus control event.");
                }

                RecordCommittedCopperMove(register, value, cycle);
                return;
            }

            if (
                _pendingCopperMoveRegister != register ||
                _pendingCopperMoveValue != value ||
                _pendingCopperMoveCycle != cycle)
            {
                throw new InvalidOperationException(
                    $"Copper MOVE completion does not match the pending Agnus control event at cycle {cycle}.");
            }

            if (cycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot commit Copper MOVE at cycle {cycle} behind executed horizon {_executedThroughCycle}.");
            }

            _pendingCopperMove = false;
            _pendingCopperMoveCycle = -1;
            RecordCommittedCopperMove(register, value, cycle);
            _intents[(int)AgnusBusAgendaSource.Control].Clear();
            SetDeadline(AgnusBusAgendaSource.Control, long.MaxValue);
            _executedThroughCycle = Math.Max(_executedThroughCycle, cycle);
        }

        private void RecordCommittedCopperMove(ushort register, ushort value, long cycle)
        {
            _copperMoveEventsCommitted++;
            _lastCopperMoveRegister = register;
            _lastCopperMoveValue = value;
            _lastCopperMoveCycle = cycle;
            if (register is 0x088 or 0x08A)
            {
                _copperCopjmpEventsCommitted++;
            }
            else if (register == 0x096)
            {
                _copperDmaconEventsCommitted++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordGrantedCopperWord(uint address, ushort value, long grantedCycle)
        {
            _copperGrantedWords++;
            _lastCopperAddress = _bus.MaskChipDmaAddress(address);
            _lastCopperValue = value;
            _lastCopperGrantedCycle = grantedCycle;
            _executedThroughCycle = Math.Max(_executedThroughCycle, grantedCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReserveBlitterDmaWordExactSlot(
            uint address,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            out AmigaBusAccessResult result)
            => _slots.TryReserveBlitterDmaWordExactSlot(address, requestedCycle, slotCycle, isWrite, out result);

        public AmigaDmaWordExecutionResult ExecuteBlitterWord(
            uint address,
            long requestedCycle,
            bool isWrite,
            ushort writeValue)
        {
            SetBlitterIntent(address, requestedCycle, isWrite);
            var execution = _bus.ExecuteBlitterChipWord(address, requestedCycle, isWrite, writeValue);
            if (!execution.Granted)
            {
                _blitterDeniedWords++;
                return execution;
            }

            RecordGrantedBlitterWord(address, isWrite ? writeValue : execution.Value, execution.Access.GrantedCycle, isWrite);
            ClearBlitterIntent();
            return execution;
        }

        public bool TryExecuteBlitterWordExact(
            uint address,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            ushort writeValue,
            bool displayPrepared,
            out AmigaDmaWordExecutionResult execution)
        {
            SetBlitterIntent(address, Math.Max(requestedCycle, slotCycle), isWrite);
            if (!_bus.TryExecuteBlitterChipWordExactSlot(
                address,
                requestedCycle,
                slotCycle,
                isWrite,
                writeValue,
                displayPrepared,
                out execution))
            {
                _blitterDeniedWords++;
                SetBlitterIntent(
                    address,
                    AgnusChipSlotScheduler.AlignToSlot(slotCycle + AgnusChipSlotScheduler.SlotCycles),
                    isWrite);
                return false;
            }

            RecordGrantedBlitterWord(address, isWrite ? writeValue : execution.Value, execution.Access.GrantedCycle, isWrite);
            ClearBlitterIntent();
            return true;
        }

        private void SetBlitterIntent(uint address, long cycle, bool isWrite)
        {
            var intent = new AgnusBusIntent
            {
                Requester = AmigaBusRequester.Blitter,
                Kind = AmigaBusAccessKind.Blitter,
                Target = AmigaBusAccessTarget.ChipRam,
                Size = AmigaBusAccessSize.Word,
                Flags = AgnusBusIntentFlags.Pending | (isWrite ? AgnusBusIntentFlags.Write : 0),
                Address = _bus.MaskChipDmaAddress(address),
                EarliestCycle = Math.Max(0, cycle)
            };
            _intents[(int)AgnusBusAgendaSource.Blitter] = intent;
            SetDeadline(AgnusBusAgendaSource.Blitter, intent.EarliestCycle);
        }

        private void ClearBlitterIntent()
        {
            _intents[(int)AgnusBusAgendaSource.Blitter].Clear();
            SetDeadline(AgnusBusAgendaSource.Blitter, long.MaxValue);
        }

        private void RecordGrantedBlitterWord(uint address, ushort value, long cycle, bool isWrite)
        {
            if (isWrite)
            {
                _blitterGrantedWrites++;
            }
            else
            {
                _blitterGrantedReads++;
            }

            _lastBlitterAddress = _bus.MaskChipDmaAddress(address);
            _lastBlitterValue = value;
            _lastBlitterGrantedCycle = cycle;
            _executedThroughCycle = Math.Max(_executedThroughCycle, cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGrantCpuDataSingleExactSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            bool allowNiceBlitterSteal,
            out long completedCycle)
        {
            SetCpuIntent(kind, target, address, size, slotCycle, isWrite);
            if (!_slots.TryGrantCpuDataSingleExactSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                slotCycle,
                isWrite,
                allowNiceBlitterSteal,
                out completedCycle))
            {
                _cpuDeniedWords++;
                SetCpuIntent(
                    kind,
                    target,
                    address,
                    size,
                    AgnusChipSlotScheduler.AlignToSlot(slotCycle + AgnusChipSlotScheduler.SlotCycles),
                    isWrite);
                return false;
            }

            RecordGrantedCpuWord(kind, address, slotCycle, isLongWordPhase: false);
            ClearCpuIntent();
            return true;
        }

        internal bool TryCommitCpuDataKnownQuietSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            long slotCycle,
            bool isWrite,
            out long completedCycle)
        {
            if (slotCycle < _executedThroughCycle)
            {
                completedCycle = 0;
                return false;
            }

            SetCpuIntent(
                kind,
                target,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite);
            if (!_slots.TryCommitCpuDataKnownQuietSlot(
                kind,
                target,
                address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                slotCycle,
                isWrite,
                out completedCycle))
            {
                return false;
            }

            RecordGrantedCpuWord(kind, address, slotCycle, isLongWordPhase: false);
            _executedThroughCycle = Math.Max(_executedThroughCycle, slotCycle);
            _queryCacheValid = false;
            return true;
        }

        internal bool TryExecuteCpuTimingSequence(
            in CpuTimingSequenceRequest request,
            out CpuTimingSequenceResult result)
        {
            result = default;
            if (request.Kind is not (AmigaBusAccessKind.CpuDataRead or
                    AmigaBusAccessKind.CpuInstructionFetch) ||
                request.Target is not (AmigaBusAccessTarget.ExpansionRam or AmigaBusAccessTarget.Rom) ||
                request.IsWrite ||
                request.WordCount <= 0 ||
                request.FirstRequestedCycle < 0)
            {
                return false;
            }

            if (request.FirstRequestedCycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot execute CPU timing sequence at cycle {request.FirstRequestedCycle} behind executed horizon {_executedThroughCycle}.");
            }

            _cpuTimingSequenceAttempts++;

            var firstRequestedCycle = Math.Max(0, request.FirstRequestedCycle);
            if (firstRequestedCycle > 0)
            {
                AdvanceThrough(firstRequestedCycle - 1);
                _slots.AdvanceTo(firstRequestedCycle - 1);
            }
            var requestedCycle = firstRequestedCycle;
            var completedCycle = firstRequestedCycle;
            var firstGrantedCycle = -1L;
            var lastGrantedCycle = -1L;
            var totalWaitCycles = 0L;
            var word = 0;
            SetCpuIntent(
                request.GetWordKind(0),
                request.Target,
                request.Address,
                AmigaBusAccessSize.Word,
                requestedCycle,
                isWrite: false);
            try
            {
                while (word < request.WordCount)
                {
                    var candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
                    RefreshDeviceDeadlines();
                    var barrier = GetCpuTimingSequenceBarrier(candidate);
                    if (_slots.TryCommitCpuDataKnownQuietRun(
                        request.Kind,
                        request.Target,
                        request.Address,
                        request.InstructionFetchShapeBits,
                        word,
                        request.WordCount - word,
                        requestedCycle,
                        candidate,
                        barrier,
                        out var quietWords,
                        out completedCycle))
                    {
                        var quietLastGrantedCycle = completedCycle -
                            AgnusChipSlotScheduler.SlotCycles;
                        ref var quietIntent = ref _intents[(int)AgnusBusAgendaSource.Cpu];
                        quietIntent.Kind = request.GetWordKind(word + quietWords - 1);
                        quietIntent.EarliestCycle = completedCycle;
                        RecordGrantedCpuRun(
                            request.Address,
                            quietWords,
                            CountInstructionFetchWords(in request, word, quietWords),
                            candidate,
                            quietLastGrantedCycle);
                        _executedThroughCycle = Math.Max(
                            _executedThroughCycle,
                            quietLastGrantedCycle);
                        firstGrantedCycle = firstGrantedCycle < 0 ? candidate : firstGrantedCycle;
                        lastGrantedCycle = quietLastGrantedCycle;
                        totalWaitCycles += candidate - requestedCycle;
                        requestedCycle = completedCycle;
                        candidate = AgnusChipSlotScheduler.AlignToSlot(requestedCycle);
                        word += quietWords;
                    }

                    if (word >= request.WordCount)
                    {
                        break;
                    }

                    var exactKind = request.GetWordKind(word);
                    ref var exactIntent = ref _intents[(int)AgnusBusAgendaSource.Cpu];
                    exactIntent.Kind = exactKind;
                    exactIntent.EarliestCycle = requestedCycle;
                    var exposeLegacyRequest =
                        !ProductionEnabled ||
                        !_bus.Blitter.Busy ||
                        !_slots.BlitterPriorityEnabled;
                    if (exposeLegacyRequest)
                    {
                        _slots.BeginPendingCpuSlotRequest(
                            exactKind,
                            request.Target,
                            request.Address,
                            AmigaBusAccessSize.Word,
                            requestedCycle,
                            isWrite: false);
                    }

                    try
                    {
                        while (true)
                        {
                            _cpuTimingSequenceBarrierRejects++;
                        AdvanceThrough(candidate);
                        var causalCandidate = _bus.AdvancePendingCpuGrantToCausalBusHorizon(
                            request.Target,
                            candidate);
                        if (causalCandidate != candidate)
                        {
                            candidate = AgnusChipSlotScheduler.AlignToSlot(causalCandidate);
                            continue;
                        }

                        _bus.SynchronizeHrmBlitterPriority();
                        if (_slots.TryGrantCpuDataSingleExactSlot(
                                exactKind,
                            request.Target,
                            request.Address,
                            AmigaBusAccessSize.Word,
                            requestedCycle,
                            candidate,
                            isWrite: false,
                            allowNiceBlitterSteal: true,
                            out completedCycle))
                        {
                            RecordGrantedCpuWord(
                                exactKind,
                                request.Address,
                                candidate,
                                isLongWordPhase: false);
                            _executedThroughCycle = Math.Max(_executedThroughCycle, candidate);
                            firstGrantedCycle = firstGrantedCycle < 0 ? candidate : firstGrantedCycle;
                            lastGrantedCycle = candidate;
                            totalWaitCycles += candidate - requestedCycle;
                            break;
                        }

                        _cpuDeniedWords++;
                        _cpuTimingSequenceSlotRejects++;
                        ref var pending = ref _intents[(int)AgnusBusAgendaSource.Cpu];
                        pending.EarliestCycle = AgnusChipSlotScheduler.AlignToSlot(
                            candidate + AgnusChipSlotScheduler.SlotCycles);
                        candidate += _bus.TryGetCommittedAgnusSlotOwner(candidate, out var deniedOwner) &&
                            deniedOwner == AgnusChipSlotOwner.Copper
                                ? 2 * AgnusChipSlotScheduler.SlotCycles
                                : AgnusChipSlotScheduler.SlotCycles;
                        }
                    }
                    finally
                    {
                        if (exposeLegacyRequest)
                        {
                            _slots.ClearPendingCpuSlotRequest();
                        }
                    }

                    requestedCycle = completedCycle;
                    word++;
                }
            }
            finally
            {
                ClearCpuIntent();
            }

            _executedThroughCycle = Math.Max(_executedThroughCycle, lastGrantedCycle);
            _cpuTimingSequenceRuns++;
            _cpuTimingSequenceWords += request.WordCount;
            _queryCacheValid = false;
            result = new CpuTimingSequenceResult(
                request.WordCount,
                firstGrantedCycle,
                lastGrantedCycle,
                completedCycle,
                lastGrantedCycle,
                totalWaitCycles);
            return true;
        }

        internal bool TryGetCommittedSlotSnapshot(
            long cycle,
            out AgnusChipSlotSnapshot snapshot)
            => _slots.TryGetCommittedSlotSnapshot(cycle, out snapshot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetCpuTimingSequenceBarrier(long candidate)
        {
            var barrier = _slots.NextMandatoryRefreshCycle;
            barrier = Math.Min(barrier, _agenda.Get(AgnusBusAgendaSource.Display));
            barrier = Math.Min(barrier, _agenda.Get(AgnusBusAgendaSource.Paula));
            barrier = Math.Min(barrier, _agenda.Get(AgnusBusAgendaSource.Disk));
            barrier = Math.Min(barrier, _agenda.Get(AgnusBusAgendaSource.Copper));
            barrier = Math.Min(barrier, _agenda.Get(AgnusBusAgendaSource.Control));
            barrier = Math.Min(barrier, _agenda.Get(AgnusBusAgendaSource.Raster));
            barrier = Math.Min(barrier, _agenda.Get(AgnusBusAgendaSource.Blitter));
            if (_bus.Blitter.Busy)
            {
                barrier = Math.Min(
                    barrier,
                    _bus.Blitter.NormalizeRawBusEligibilityCycle(
                        _bus.Blitter.GetRawBusEligibilityCycle(),
                        Math.Max(0, candidate - AgnusChipSlotScheduler.SlotCycles)));
            }

            return barrier;
        }

        public void ClearSlots() => _slots.Clear();

        public void ClearLiveDisplaySlotsFrom(long cycle, AgnusLiveDisplaySlotOwnerMask owners)
            => _slots.ClearLiveDisplaySlotsFrom(cycle, owners);

        public void BeginPendingCpuSlotRequest(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite)
        {
            // The legacy allocator remains the reference owner map for quiet
            // CPU slots during migration.  Do not expose the pending CPU to it
            // while production is chronologically executing an active blitter:
            // its retrospective CPU claim would suppress BLTPRI arbitration
            // before the unified executor can choose the real owner.
            _legacyCpuSlotRequestPending =
                !ProductionEnabled ||
                !_bus.Blitter.Busy ||
                !_slots.BlitterPriorityEnabled;
            if (_legacyCpuSlotRequestPending)
            {
                _slots.BeginPendingCpuSlotRequest(kind, target, address, size, requestedCycle, isWrite);
            }

            var intent = new AgnusBusIntent
            {
                Requester = AmigaBusRequester.Cpu,
                Kind = kind,
                Target = target,
                Size = size,
                Flags = AgnusBusIntentFlags.Pending | (isWrite ? AgnusBusIntentFlags.Write : 0),
                Address = address,
                EarliestCycle = requestedCycle
            };
            _intents[(int)AgnusBusAgendaSource.Cpu] = intent;
        }

        public void ClearPendingCpuSlotRequest()
        {
            if (_legacyCpuSlotRequestPending)
            {
                _slots.ClearPendingCpuSlotRequest();
                _legacyCpuSlotRequestPending = false;
            }

            ClearCpuIntent();
        }

        public void GrantCpuDataSingleSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle)
        {
            SetCpuIntent(kind, target, address, size, requestedCycle, isWrite);
            _slots.GrantCpuDataSingleSlot(
                kind,
                target,
                address,
                size,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle);
            RecordGrantedCpuWord(kind, address, grantedCycle, isLongWordPhase: false);
            ClearCpuIntent();
        }

        public void GrantCpuDataLongSlots(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long requestedCycle,
            bool isWrite,
            out long firstWordCycle,
            out long secondWordCycle,
            out long completedCycle)
        {
            SetCpuIntent(kind, target, address, AmigaBusAccessSize.Long, requestedCycle, isWrite);
            _slots.GrantCpuDataLongSlots(
                kind,
                target,
                address,
                requestedCycle,
                isWrite,
                out firstWordCycle,
                out secondWordCycle,
                out completedCycle);
            RecordGrantedCpuWord(kind, address, firstWordCycle, isLongWordPhase: true);
            RecordGrantedCpuWord(kind, address + 2, secondWordCycle, isLongWordPhase: true);
            ClearCpuIntent();
        }

        public void GrantCpuDataLongWordPhaseSlot(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            long searchCycle,
            long requestedCycle,
            bool isWrite,
            out long grantedCycle,
            out long completedCycle)
        {
            SetCpuIntent(kind, target, address, AmigaBusAccessSize.Word, searchCycle, isWrite);
            _slots.GrantCpuDataLongWordPhaseSlot(
                kind,
                target,
                address,
                searchCycle,
                requestedCycle,
                isWrite,
                out grantedCycle,
                out completedCycle);
            RecordGrantedCpuWord(kind, address, grantedCycle, isLongWordPhase: true);
            ClearCpuIntent();
        }

        private void SetCpuIntent(
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long cycle,
            bool isWrite)
        {
            var intent = new AgnusBusIntent
            {
                Requester = AmigaBusRequester.Cpu,
                Kind = kind,
                Target = target,
                Size = size,
                Flags = AgnusBusIntentFlags.Pending | (isWrite ? AgnusBusIntentFlags.Write : 0),
                Address = address,
                EarliestCycle = Math.Max(0, cycle)
            };
            _intents[(int)AgnusBusAgendaSource.Cpu] = intent;
        }

        private void ClearCpuIntent()
        {
            _intents[(int)AgnusBusAgendaSource.Cpu].Clear();
        }

        private void RecordGrantedCpuWord(
            AmigaBusAccessKind kind,
            uint address,
            long cycle,
            bool isLongWordPhase)
        {
            _cpuGrantedWords++;
            if (kind == AmigaBusAccessKind.CpuInstructionFetch)
            {
                _cpuInstructionFetchGrantedWords++;
            }
            else
            {
                _cpuDataGrantedWords++;
            }
            if (isLongWordPhase)
            {
                _cpuLongWordPhases++;
            }

            _lastCpuAddress = address;
            _lastCpuGrantedCycle = cycle;
        }

        private void RecordGrantedCpuRun(
            uint address,
            int wordCount,
            int instructionFetchWordCount,
            long firstCycle,
            long lastCycle)
        {
            _cpuGrantedWords += wordCount;
            _cpuInstructionFetchGrantedWords += instructionFetchWordCount;
            _cpuDataGrantedWords += wordCount - instructionFetchWordCount;
            _lastCpuAddress = address;
            _lastCpuGrantedCycle = lastCycle;
            _ = firstCycle;
        }

        private static int CountInstructionFetchWords(
            in CpuTimingSequenceRequest request,
            int wordOffset,
            int wordCount)
        {
            var count = 0;
            for (var i = 0; i < wordCount; i++)
            {
                if (request.GetWordKind(wordOffset + i) == AmigaBusAccessKind.CpuInstructionFetch)
                {
                    count++;
                }
            }

            return count;
        }

        public void UpdateGeometry(long frameCycles) => _slots.UpdateGeometry(frameCycles);

        public void SetBlitterPriority(bool enabled) => _slots.BlitterPriorityEnabled = enabled;

        public void ApplyControlEvent(in AgnusControlEvent controlEvent)
        {
            if (controlEvent.Cycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot apply Agnus control event at cycle {controlEvent.Cycle} behind executed horizon {_executedThroughCycle}.");
            }

            Invalidate(controlEvent.Source);
        }

        public void ObserveDisplayControlWrite(
            AmigaBusRequester requester,
            ushort offset,
            ushort value,
            long cycle)
        {
            _ = requester;
            if (!IsDisplayControlRegister(offset))
            {
                return;
            }

            if (cycle < _executedThroughCycle)
            {
                if (ProductionEnabled)
                {
                    throw new InvalidOperationException(
                        $"Cannot apply display control write 0x{offset:X3} at cycle {cycle} " +
                        $"behind executed horizon {_executedThroughCycle}.");
                }

                DisplayControlState.IgnoredHistoricalWriteCount++;
                return;
            }

            var state = DisplayControlState;
            switch (offset)
            {
                case 0x08E: state.DiwStart = value; state.WrittenMask |= AgnusDisplayControlState.DiwStartWritten; break;
                case 0x090: state.DiwStop = value; state.WrittenMask |= AgnusDisplayControlState.DiwStopWritten; break;
                case 0x092: state.DdfStart = value; state.WrittenMask |= AgnusDisplayControlState.DdfStartWritten; break;
                case 0x094: state.DdfStop = value; state.WrittenMask |= AgnusDisplayControlState.DdfStopWritten; break;
                case 0x096:
                    state.Dmacon = (value & 0x8000) != 0
                        ? (ushort)(state.Dmacon | (value & 0x7FFF))
                        : (ushort)(state.Dmacon & ~(value & 0x7FFF));
                    state.WrittenMask |= AgnusDisplayControlState.DmaconWritten;
                    break;
                case 0x100: state.Bplcon0 = value; state.WrittenMask |= AgnusDisplayControlState.Bplcon0Written; break;
                case 0x102: state.Bplcon1 = value; state.WrittenMask |= AgnusDisplayControlState.Bplcon1Written; break;
                case 0x104: state.Bplcon2 = value; state.WrittenMask |= AgnusDisplayControlState.Bplcon2Written; break;
                case 0x106: state.Bplcon3 = value; state.WrittenMask |= AgnusDisplayControlState.Bplcon3Written; break;
                case 0x108: state.Bpl1Mod = unchecked((short)value); state.WrittenMask |= AgnusDisplayControlState.Bpl1ModWritten; break;
                case 0x10A: state.Bpl2Mod = unchecked((short)value); state.WrittenMask |= AgnusDisplayControlState.Bpl2ModWritten; break;
                case 0x1E4: state.DiwHigh = value; state.WrittenMask |= AgnusDisplayControlState.DiwHighWritten; break;
                default:
                    if (offset is >= 0x0E0 and <= 0x0FE)
                    {
                        var plane = (offset - 0x0E0) >> 2;
                        state.BitplanePointers[plane] = (offset & 2) == 0
                            ? _bus.WriteChipDmaPointerHigh(state.BitplanePointers[plane], value)
                            : _bus.WriteChipDmaPointerLow(state.BitplanePointers[plane], value);
                        state.BitplanePointerWrittenMask |= (byte)(1 << plane);
                    }
                    else if (offset is >= 0x120 and <= 0x13E)
                    {
                        var channel = (offset - 0x120) >> 2;
                        state.SpritePointers[channel] = (offset & 2) == 0
                            ? _bus.WriteChipDmaPointerHigh(state.SpritePointers[channel], value)
                            : _bus.WriteChipDmaPointerLow(state.SpritePointers[channel], value);
                        state.SpriteNextFetchAddresses[channel] = state.SpritePointers[channel];
                        state.SpritePointerWrittenMask |= (byte)(1 << channel);
                    }
                    break;
            }

            state.LastWriteCycle = cycle;
            state.Version++;
            state.AppliedWriteCount++;
            Invalidate(AgnusBusAgendaSource.Display);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordGrantedBitplaneFetch(int plane, uint address, long grantedCycle)
        {
            if ((uint)plane >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(plane));
            }

            var state = DisplayControlState;
            state.BitplaneLastFetchAddresses[plane] = address;
            state.BitplaneLastFetchCycles[plane] = grantedCycle;
            state.BitplaneGrantCounts[plane]++;
        }

        public bool TryExecuteBitplaneWord(
            int plane,
            uint address,
            long requestedCycle,
            out ushort value,
            out long grantedCycle)
        {
            if ((uint)plane >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(plane));
            }

            if (requestedCycle <= _bus.ExecutedChipBusHorizon)
            {
                value = 0;
                grantedCycle = requestedCycle;
                return false;
            }

            if (!_bus.TryReadRowBitplaneDmaWord(address, requestedCycle, out value, out grantedCycle))
            {
                return false;
            }

            RecordGrantedBitplaneFetch(plane, address, grantedCycle);
            _executedThroughCycle = Math.Max(_executedThroughCycle, grantedCycle);
            return true;
        }

        public void ExecuteBitplaneRowBatch(
            ReadOnlySpan<RowDmaBitplaneEntry> entries,
            long lineStartCycle,
            Span<ushort> values,
            Span<bool> granted,
            out int grantedCount,
            out long firstGrantedCycle,
            out long lastGrantedCycle)
        {
            if (values.Length < entries.Length)
            {
                throw new ArgumentException("Bitplane value buffer is shorter than the fetch list.", nameof(values));
            }

            if (granted.Length < entries.Length)
            {
                throw new ArgumentException("Bitplane grant buffer is shorter than the fetch list.", nameof(granted));
            }

            grantedCount = 0;
            firstGrantedCycle = -1;
            lastGrantedCycle = -1;
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (!entry.RowPresent)
                {
                    values[index] = 0;
                    granted[index] = false;
                    continue;
                }

                var entryCycle = entry.GetCycle(lineStartCycle);
                if (!TryExecuteBitplaneWord(
                    entry.Plane,
                    entry.Address,
                    entryCycle,
                    out var value,
                    out var grantedCycle))
                {
                    values[index] = 0;
                    granted[index] = false;
                    continue;
                }

                values[index] = value;
                granted[index] = true;
                grantedCount++;
                if (firstGrantedCycle < 0 || grantedCycle < firstGrantedCycle)
                {
                    firstGrantedCycle = grantedCycle;
                }

                if (lastGrantedCycle < 0 || grantedCycle > lastGrantedCycle)
                {
                    lastGrantedCycle = grantedCycle;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordBitplaneRowPointerAdvance(int plane, uint pointer, long cycle)
        {
            if ((uint)plane >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(plane));
            }

            DisplayControlState.BitplanePointers[plane] = pointer;
            DisplayControlState.LastWriteCycle = Math.Max(DisplayControlState.LastWriteCycle, cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordGrantedSpriteFetch(int channel, uint address, long grantedCycle)
        {
            if ((uint)channel >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }

            var state = DisplayControlState;
            if ((state.SpritePointerWrittenMask & (1 << channel)) != 0)
            {
                var expectedAddress = state.SpriteNextFetchAddresses[channel];
                if (expectedAddress == address)
                {
                    state.SpriteAddressMatches++;
                }
                else
                {
                    state.SpriteAddressMismatches++;
                    if (state.FirstSpriteAddressMismatch.Length == 0)
                    {
                        state.FirstSpriteAddressMismatch =
                            $"SPR{channel}PT:agnus=0x{expectedAddress:X8}," +
                            $"denise=0x{address:X8},cycle={grantedCycle}";
                    }
                }
            }

            state.SpriteLastFetchAddresses[channel] = address;
            state.SpriteLastFetchCycles[channel] = grantedCycle;
            state.SpriteGrantCounts[channel]++;
            state.SpriteNextFetchAddresses[channel] = _bus.AddChipDmaPointerOffset(address, 2);
        }

        public bool TryExecuteSpriteWord(
            int channel,
            uint address,
            long requestedCycle,
            out ushort value,
            out long grantedCycle)
        {
            if ((uint)channel >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }

            if (requestedCycle <= _bus.ExecutedChipBusHorizon)
            {
                value = 0;
                grantedCycle = requestedCycle;
                return false;
            }

            if (!_bus.TryReadRowSpriteDmaWord(address, requestedCycle, out value, out grantedCycle))
            {
                return false;
            }

            RecordGrantedSpriteFetch(channel, address, grantedCycle);
            _executedThroughCycle = Math.Max(_executedThroughCycle, grantedCycle);
            return true;
        }

        public void CompareBitplanePointers(long observedCycle, ReadOnlySpan<uint> pointers)
        {
            var state = DisplayControlState;
            var count = Math.Min(8, pointers.Length);
            for (var plane = 0; plane < count; plane++)
            {
                if ((state.BitplanePointerWrittenMask & (1 << plane)) == 0)
                {
                    continue;
                }

                if (state.BitplanePointers[plane] == pointers[plane])
                {
                    state.BitplanePointerMatches++;
                    continue;
                }

                state.BitplanePointerMismatches++;
                if (state.FirstBitplanePointerMismatch.Length == 0)
                {
                    state.FirstBitplanePointerMismatch =
                        $"BPL{plane + 1}PT:agnus=0x{state.BitplanePointers[plane]:X8}," +
                        $"denise=0x{pointers[plane]:X8},cycle={observedCycle}";
                }
            }
        }

        public void CompareDisplayLineControlState(
            long observedCycle,
            ushort dmacon,
            ushort diwStart,
            ushort diwStop,
            ushort diwHigh,
            ushort ddfStart,
            ushort ddfStop,
            ushort bplcon0,
            ushort bplcon1,
            ushort bplcon2,
            ushort bplcon3,
            short bpl1Mod,
            short bpl2Mod)
        {
            var state = DisplayControlState;
            if (state.WrittenMask == 0)
            {
                return;
            }

            if (state.LastWriteCycle > observedCycle)
            {
                state.LineStateDeferredComparisons++;
                return;
            }

            var mismatch = FindDisplayLineControlMismatch(
                state,
                observedCycle,
                dmacon,
                diwStart,
                diwStop,
                diwHigh,
                ddfStart,
                ddfStop,
                bplcon0,
                bplcon1,
                bplcon2,
                bplcon3,
                bpl1Mod,
                bpl2Mod,
                formatDiagnostic: state.FirstLineStateMismatch.Length == 0);

            if (mismatch == null)
            {
                state.LineStateMatches++;
            }
            else
            {
                state.LineStateMismatches++;
                if (state.FirstLineStateMismatch.Length == 0)
                {
                    state.FirstLineStateMismatch = mismatch;
                }
            }
        }

        private static string? FindDisplayLineControlMismatch(
            AgnusDisplayControlState state,
            long cycle,
            ushort dmacon,
            ushort diwStart,
            ushort diwStop,
            ushort diwHigh,
            ushort ddfStart,
            ushort ddfStop,
            ushort bplcon0,
            ushort bplcon1,
            ushort bplcon2,
            ushort bplcon3,
            short bpl1Mod,
            short bpl2Mod,
            bool formatDiagnostic)
        {
            if ((state.WrittenMask & AgnusDisplayControlState.DmaconWritten) != 0 && state.Dmacon != dmacon) return Result("DMACON", state.Dmacon, dmacon, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.DiwStartWritten) != 0 && state.DiwStart != diwStart) return Result("DIWSTRT", state.DiwStart, diwStart, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.DiwStopWritten) != 0 && state.DiwStop != diwStop) return Result("DIWSTOP", state.DiwStop, diwStop, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.DiwHighWritten) != 0 && state.DiwHigh != diwHigh) return Result("DIWHIGH", state.DiwHigh, diwHigh, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.DdfStartWritten) != 0 && state.DdfStart != ddfStart) return Result("DDFSTRT", state.DdfStart, ddfStart, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.DdfStopWritten) != 0 && state.DdfStop != ddfStop) return Result("DDFSTOP", state.DdfStop, ddfStop, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.Bplcon0Written) != 0 && state.Bplcon0 != bplcon0) return Result("BPLCON0", state.Bplcon0, bplcon0, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.Bplcon1Written) != 0 && state.Bplcon1 != bplcon1) return Result("BPLCON1", state.Bplcon1, bplcon1, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.Bplcon2Written) != 0 && state.Bplcon2 != bplcon2) return Result("BPLCON2", state.Bplcon2, bplcon2, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.Bplcon3Written) != 0 && state.Bplcon3 != bplcon3) return Result("BPLCON3", state.Bplcon3, bplcon3, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.Bpl1ModWritten) != 0 && state.Bpl1Mod != bpl1Mod) return Result("BPL1MOD", (ushort)state.Bpl1Mod, (ushort)bpl1Mod, cycle, formatDiagnostic);
            if ((state.WrittenMask & AgnusDisplayControlState.Bpl2ModWritten) != 0 && state.Bpl2Mod != bpl2Mod) return Result("BPL2MOD", (ushort)state.Bpl2Mod, (ushort)bpl2Mod, cycle, formatDiagnostic);
            return null;

            static string Result(string name, ushort agnus, ushort denise, long at, bool format)
                => format
                    ? $"{name}:agnus=0x{agnus:X4},denise=0x{denise:X4},cycle={at}"
                    : "mismatch";
        }

        private static bool IsDisplayControlRegister(ushort offset)
            => offset is 0x08E or 0x090 or 0x092 or 0x094 or 0x096 or
                0x100 or 0x102 or 0x104 or 0x106 or 0x108 or 0x10A or 0x1E4 ||
                offset is >= 0x0E0 and <= 0x0FE ||
                offset is >= 0x120 and <= 0x13E;

        public void SetIntent(AgnusBusAgendaSource source, in AgnusBusIntent intent)
        {
            if (intent.Pending && intent.EarliestCycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot submit {source} intent at cycle {intent.EarliestCycle} behind executed horizon {_executedThroughCycle}.");
            }

            _intents[(int)source] = intent;
            SetDeadline(source, intent.Pending ? intent.EarliestCycle : long.MaxValue);
        }

        public ref readonly AgnusBusIntent GetIntent(AgnusBusAgendaSource source)
            => ref _intents[(int)source];

        public void Invalidate(AgnusBusAgendaSource source)
        {
            _queryCacheValid = false;
            switch (source)
            {
                case AgnusBusAgendaSource.Paula:
                    _paulaVersion = ulong.MaxValue;
                    break;
                case AgnusBusAgendaSource.Disk:
                    _diskVersion = ulong.MaxValue;
                    break;
                case AgnusBusAgendaSource.Display:
                    _displayVersion = ulong.MaxValue;
                    break;
                case AgnusBusAgendaSource.Blitter:
                    break;
            }

            SetDeadline(source, long.MaxValue);
        }

        public void InvalidateDeviceDeadlines()
        {
            Invalidate(AgnusBusAgendaSource.Paula);
            Invalidate(AgnusBusAgendaSource.Disk);
            Invalidate(AgnusBusAgendaSource.Display);
            Invalidate(AgnusBusAgendaSource.Blitter);
        }

        public long GetNextDeviceEligibilityCycle(long currentCycle, long targetCycle)
        {
            if (targetCycle < currentCycle)
            {
                return long.MaxValue;
            }

            RefreshDeviceDeadlines();
            _agendaReads++;
            var candidate = GetNormalizedDeviceCandidate(currentCycle, targetCycle);
            if (candidate == long.MaxValue || candidate > targetCycle)
            {
                return long.MaxValue;
            }

            return candidate;
        }

        public long GetNextSlotContendedCycle(long currentCycle, long targetCycle)
        {
            currentCycle = Math.Max(currentCycle, _executedThroughCycle);
            if (targetCycle < currentCycle)
            {
                return long.MaxValue;
            }

            if (TryGetCpuOnlyCandidate(currentCycle, targetCycle, out var cpuCandidate))
            {
                return cpuCandidate;
            }

            if (_queryCacheValid &&
                _cachedQueryCurrentCycle == currentCycle &&
                _cachedQueryTargetCycle == targetCycle)
            {
                return _cachedQueryResult;
            }

            RefreshPersistentDeadlines();
            _agendaReads++;

            // Display fixed-slot materialization is still provided by the live
            // rasterline plan during this migration stage. Query it once here,
            // together with refresh, instead of relying on a display getter
            // hidden in a four-device scheduler scan.
            var fixedCandidate = _bus.GetNextAgnusEventCycle(currentCycle, targetCycle);
            var dynamicCandidate = GetNormalizedDynamicCandidate(currentCycle, targetCycle);
            var candidate = Math.Min(dynamicCandidate, fixedCandidate);
            var result = candidate <= targetCycle ? candidate : long.MaxValue;
            _cachedQueryCurrentCycle = currentCycle;
            _cachedQueryTargetCycle = targetCycle;
            _cachedQueryResult = result;
            _queryCacheValid = true;
            return result;
        }

        public bool TryAdvanceFixedBatch(
            long currentCycle,
            long targetCycle,
            out long advancedThroughCycle)
        {
            targetCycle = ClampPhysicalCommitTarget(targetCycle);
            advancedThroughCycle = currentCycle;
            if (targetCycle < currentCycle)
            {
                return false;
            }

            // Most CPU horizons contain no fixed DMA at all. Avoid the Copper
            // barrier calculation (which can inspect WAIT state) unless Agnus
            // has concrete fixed work inside this range.
            var fixedCandidate = _bus.GetNextAgnusEventCycle(currentCycle, targetCycle);
            if (fixedCandidate > targetCycle)
            {
                return false;
            }

            RefreshPersistentDeadlines();
            var dynamicBarrier = GetNormalizedDynamicCandidate(currentCycle, targetCycle);
            var copperBarrier = _bus.Display.GetNextLiveCopperCpuBatchBarrierCycle(
                currentCycle,
                targetCycle) ?? long.MaxValue;
            dynamicBarrier = Math.Min(dynamicBarrier, copperBarrier);
            var fixedStopCycle = dynamicBarrier <= targetCycle
                ? dynamicBarrier - 1
                : targetCycle;
            if (fixedStopCycle < fixedCandidate)
            {
                return false;
            }

            _bus.AdvanceAgnusCoreTo(fixedStopCycle);
            _executedThroughCycle = Math.Max(_executedThroughCycle, fixedStopCycle);
            _queryCacheValid = false;
            advancedThroughCycle = fixedStopCycle;
            return true;
        }

        public AgnusBusExecutionResult TryAdvanceSingleDynamicBatch(
            long currentCycle,
            long targetCycle,
            out long advancedThroughCycle)
        {
            targetCycle = ClampPhysicalCommitTarget(targetCycle);
            advancedThroughCycle = currentCycle;
            if (targetCycle < currentCycle)
            {
                return AgnusBusExecutionResult.None;
            }

            RefreshPersistentDeadlines();
            var paula = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Paula),
                currentCycle);
            var disk = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Disk),
                currentCycle);
            if (paula > targetCycle && disk > targetCycle)
            {
                return AgnusBusExecutionResult.None;
            }

            RefreshDeviceDeadlines();
            var fixedCycle = Math.Min(
                _bus.Display.NormalizeRawLiveBusEligibilityCycle(
                    _agenda.Get(AgnusBusAgendaSource.Display),
                    currentCycle,
                    targetCycle),
                _slots.NextMandatoryRefreshCycle);
            var copper = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Copper),
                currentCycle);
            var control = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Control),
                currentCycle);
            var pendingBlitter = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Blitter),
                currentCycle);
            var engineBlitter = _bus.Blitter.Busy
                ? _bus.Blitter.NormalizeRawBusEligibilityCycle(
                    _bus.Blitter.GetRawBusEligibilityCycle(),
                    currentCycle)
                : long.MaxValue;
            var blitter = Math.Min(engineBlitter, pendingBlitter);

            if (paula <= targetCycle && paula < disk)
            {
                var barrier = Math.Min(
                    fixedCycle,
                    Math.Min(disk, Math.Min(copper, Math.Min(control, blitter))));
                var stopCycle = barrier <= targetCycle ? barrier - 1 : targetCycle;
                if (stopCycle >= paula)
                {
                    _bus.Paula.AdvanceDmaObservableTo(stopCycle);
                    _executedThroughCycle = Math.Max(_executedThroughCycle, stopCycle);
                    _queryCacheValid = false;
                    advancedThroughCycle = stopCycle;
                    return AgnusBusExecutionResult.Paula;
                }
            }

            if (disk <= targetCycle && disk < paula)
            {
                var barrier = Math.Min(
                    fixedCycle,
                    Math.Min(paula, Math.Min(copper, Math.Min(control, blitter))));
                var stopCycle = barrier <= targetCycle ? barrier - 1 : targetCycle;
                if (stopCycle >= disk)
                {
                    _bus.Disk.AdvanceEventsTo(stopCycle);
                    _executedThroughCycle = Math.Max(_executedThroughCycle, stopCycle);
                    _queryCacheValid = false;
                    advancedThroughCycle = stopCycle;
                    return AgnusBusExecutionResult.Disk;
                }
            }

            return AgnusBusExecutionResult.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetCpuOnlyCandidate(long currentCycle, long targetCycle, out long candidate)
        {
            candidate = long.MaxValue;
            ref readonly var cpu = ref _intents[(int)AgnusBusAgendaSource.Cpu];
            if (!cpu.Pending)
            {
                return false;
            }

            candidate = AgnusChipSlotScheduler.AlignToSlot(Math.Max(currentCycle, cpu.EarliestCycle));
            if (candidate > targetCycle || _bus.IsMandatoryRefreshSlot(candidate))
            {
                return false;
            }

            RefreshDeviceDeadlines();
            if (_agenda.Get(AgnusBusAgendaSource.Display) <= candidate ||
                _agenda.Get(AgnusBusAgendaSource.Paula) <= candidate ||
                _agenda.Get(AgnusBusAgendaSource.Disk) <= candidate ||
                _agenda.Get(AgnusBusAgendaSource.Copper) <= candidate ||
                _agenda.Get(AgnusBusAgendaSource.Control) <= candidate ||
                _agenda.Get(AgnusBusAgendaSource.Blitter) <= candidate ||
                _bus.Blitter.NormalizeRawBusEligibilityCycle(
                    _bus.Blitter.GetRawBusEligibilityCycle(),
                    candidate) <= candidate)
            {
                candidate = long.MaxValue;
                return false;
            }

            return true;
        }

        public AgnusBusExecutionResult ExecuteEligibleAt(
            long cycle,
            bool useCpuWaitBlitterMicroOps,
            bool processBlitter)
        {
            if (cycle >= GetUnresolvedCpuFenceCycle())
            {
                throw new InvalidOperationException(
                    $"Cannot execute Agnus slot at cycle {cycle} across unresolved CPU fence {GetUnresolvedCpuFenceCycle()}.");
            }

            if (cycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot execute Agnus slot at cycle {cycle} behind executed horizon {_executedThroughCycle}.");
            }

            RefreshPersistentDeadlines();
            if (CanExecuteAsCpuOnlySlot(cycle, processBlitter))
            {
                // The pending CPU intent made this slot a scheduler deadline,
                // but no DMA/control requester can own it and it is not refresh.
                // Preserve the executed horizon exactly as the full path does;
                // the caller commits the pending CPU latch immediately after.
                _executedThroughCycle = Math.Max(_executedThroughCycle, cycle);
                _queryCacheValid = false;
                return AgnusBusExecutionResult.None;
            }

            var result = AgnusBusExecutionResult.None;

            // Preserve the proven causal ordering while moving its ownership out of
            // the scheduler. Each device commits through this executor's HRM facade,
            // so a slot accepted by an earlier requester cannot be accepted again.
            if (_bus.Paula.HasDmaWorkThrough(cycle))
            {
                _bus.Paula.AdvanceDmaObservableTo(cycle);
                result |= AgnusBusExecutionResult.Paula;
            }

            if (_bus.Disk.HasSlotDmaWakeSourceThrough(cycle))
            {
                _bus.Disk.AdvanceEventsTo(cycle);
                result |= AgnusBusExecutionResult.Disk;
            }

            if (_bus.GetNextAgnusEventCycle(cycle, cycle) <= cycle)
            {
                _bus.AdvanceAgnusCoreTo(cycle);
                result |= AgnusBusExecutionResult.Fixed;
            }

            if (processBlitter)
            {
                if (useCpuWaitBlitterMicroOps && _bus.Blitter.CanUseCpuWaitAreaMicroOps)
                {
                    if (_bus.Blitter.AdvanceCpuWaitAreaMicroOpTo(cycle))
                    {
                        result |= AgnusBusExecutionResult.Blitter;
                    }
                }
                else if (_bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, cycle - 1), cycle) <= cycle)
                {
                    _bus.SynchronizeBlitterThrough(cycle);
                    result |= AgnusBusExecutionResult.Blitter;
                }
            }

            _executedThroughCycle = Math.Max(_executedThroughCycle, cycle);
            _queryCacheValid = false;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdvanceCpuOnlySlot(long cycle)
        {
            if (cycle >= GetUnresolvedCpuFenceCycle())
            {
                return false;
            }

            if (cycle < _executedThroughCycle)
            {
                return false;
            }

            RefreshDeviceDeadlines();
            if (!CanExecuteAsCpuOnlySlot(cycle, processBlitter: true))
            {
                return false;
            }

            _executedThroughCycle = Math.Max(_executedThroughCycle, cycle);
            _queryCacheValid = false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanExecuteAsCpuOnlySlot(long cycle, bool processBlitter)
        {
            ref readonly var cpu = ref _intents[(int)AgnusBusAgendaSource.Cpu];
            if (!cpu.Pending || cpu.EarliestCycle > cycle || _bus.IsMandatoryRefreshSlot(cycle))
            {
                return false;
            }

            if (_agenda.Get(AgnusBusAgendaSource.Display) <= cycle ||
                _agenda.Get(AgnusBusAgendaSource.Paula) <= cycle ||
                _agenda.Get(AgnusBusAgendaSource.Disk) <= cycle ||
                _agenda.Get(AgnusBusAgendaSource.Copper) <= cycle ||
                _agenda.Get(AgnusBusAgendaSource.Control) <= cycle ||
                _agenda.Get(AgnusBusAgendaSource.Blitter) <= cycle)
            {
                return false;
            }

            return !processBlitter ||
                !_bus.Blitter.Busy ||
                _bus.Blitter.NormalizeRawBusEligibilityCycle(
                    _bus.Blitter.GetRawBusEligibilityCycle(),
                    Math.Max(0, cycle - AgnusChipSlotScheduler.SlotCycles)) > cycle;
        }

        public AgnusBusExecutionResult CompleteDynamicThrough(
            long targetCycle,
            bool blitterWasBusyAtDrainStart)
        {
            targetCycle = ClampPhysicalCommitTarget(targetCycle);
            if (targetCycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot complete Agnus DMA through cycle {targetCycle} behind executed horizon {_executedThroughCycle}.");
            }

            var result = AgnusBusExecutionResult.None;
            if (_bus.Paula.HasDmaWorkThrough(targetCycle))
            {
                _bus.Paula.AdvanceDmaObservableTo(targetCycle);
                result |= AgnusBusExecutionResult.Paula;
            }

            if (_bus.Disk.HasSlotDmaWakeSourceThrough(targetCycle))
            {
                _bus.Disk.AdvanceEventsTo(targetCycle);
                result |= AgnusBusExecutionResult.Disk;
            }

            if (!blitterWasBusyAtDrainStart &&
                _bus.Blitter.GetNextWakeCandidateCycle(Math.Max(0, targetCycle - 1), targetCycle) <= targetCycle)
            {
                _bus.SynchronizeBlitterThrough(targetCycle);
                result |= AgnusBusExecutionResult.Blitter;
            }

            _executedThroughCycle = Math.Max(_executedThroughCycle, targetCycle);
            _queryCacheValid = false;
            return result;
        }

        public void RecordShadowPrediction(
            long currentCycle,
            long targetCycle,
            long referenceCycle,
            long predictedCycle,
            long referencePaula,
            long referenceDisk,
            long referenceAgnus,
            long referenceBlitter)
        {
            if (referenceCycle == predictedCycle)
            {
                _shadowMatches++;
                return;
            }

            _shadowMismatches++;
            if (_firstShadowMismatch.Length == 0)
            {
                _firstShadowMismatch =
                    $"current={currentCycle},target={targetCycle},reference={referenceCycle},predicted={predictedCycle}," +
                    $"refSources={referencePaula}/{referenceDisk}/{referenceAgnus}/{referenceBlitter}," +
                    $"displayState={_bus.Display.LiveExecutionCycle}/{_bus.Display.LiveCapturedThroughCycle}," +
                    $"display={_agenda.Get(AgnusBusAgendaSource.Display)}," +
                    $"paula={_agenda.Get(AgnusBusAgendaSource.Paula)}," +
                    $"disk={_agenda.Get(AgnusBusAgendaSource.Disk)}," +
                    $"copper={_agenda.Get(AgnusBusAgendaSource.Copper)}," +
                    $"blitter={_agenda.Get(AgnusBusAgendaSource.Blitter)}";
            }
        }

        internal AgnusChipSlotOwner GetPlannedFixedOwnerAt(long cycle, out int entryIndex)
        {
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, cycle));
            if (_slots.IsMandatoryRefreshSlot(slotCycle))
            {
                entryIndex = -1;
                return AgnusChipSlotOwner.Refresh;
            }

            return RasterlinePlans.TryGetFixedOwnerAt(slotCycle, out var owner, out entryIndex)
                ? owner
                : AgnusChipSlotOwner.Free;
        }

        internal long GetNextEligibleCopperControlPhase(
            long cycle,
            bool incomingRgaBlocked,
            bool adjacentRgaStageBlocked)
        {
            var phaseCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, cycle));
            if (!incomingRgaBlocked)
            {
                return phaseCycle;
            }

            // Internal Copper control phases inspect the physical Agnus timeline
            // but do not reserve it. A complete adjacent RGA overlap consumes the
            // phase, as does refresh at the start of a physical raster line. An
            // isolated incoming bitplane phase is carried forward without losing
            // the already armed restart.
            var owner = GetPlannedFixedOwnerAt(phaseCycle, out _);
            var horizontal = AgnusHrmOcsSlotTable.GetHorizontal(phaseCycle);
            var blockedPolarity = (horizontal & 0x02) != 0;
            return adjacentRgaStageBlocked ||
                   owner == AgnusChipSlotOwner.Refresh ||
                   blockedPolarity
                ? phaseCycle + (2L * AgnusChipSlotScheduler.SlotCycles)
                : phaseCycle;
        }

        internal void RecordFixedPlanShadow(
            long cycle,
            AgnusChipSlotOwner referenceOwner,
            ushort liveDmacon = 0,
            ushort liveBplcon0 = 0)
        {
            var plannedOwner = GetPlannedFixedOwnerAt(cycle, out var entryIndex);
            if (plannedOwner == referenceOwner)
            {
                _fixedPlanShadowMatches++;
                return;
            }

            _fixedPlanShadowMismatches++;
            if (_firstFixedPlanShadowMismatch.Length == 0)
            {
                var plan0 = RasterlinePlans.Plans[0];
                var plan1 = RasterlinePlans.Plans[1];
                var plan2 = RasterlinePlans.Plans[2];
                _firstFixedPlanShadowMismatch =
                    $"cycle={cycle},reference={referenceOwner},planned={plannedOwner},entry={entryIndex}," +
                    $"liveDmacon={liveDmacon:X4},liveBplcon0={liveBplcon0:X4}," +
                    $"plans={DescribePlan(in plan0)}/{DescribePlan(in plan1)}/{DescribePlan(in plan2)}";
            }
        }

        private static string DescribePlan(in RowDmaPlan plan)
            => plan.Valid
                ? $"r{plan.Row}@{plan.LineStartCycle}:d{plan.Dmacon:X4}:c{plan.Bplcon0:X4}:b{plan.BitplaneCount}:s{plan.SpriteCount}:v{plan.DmaPlanVersion}"
                : "invalid";

        public void MarkAdvancedThrough(long cycle)
        {
            var fenceCycle = GetUnresolvedCpuFenceCycle();
            if (cycle >= fenceCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot move Agnus executor horizon to {cycle} across unresolved CPU fence {fenceCycle}.");
            }

            if (cycle < _executedThroughCycle)
            {
                throw new InvalidOperationException(
                    $"Cannot move Agnus executor horizon backward from {_executedThroughCycle} to {cycle}.");
            }

            _executedThroughCycle = cycle;
        }

        private void RefreshDeviceDeadlines()
        {
            RefreshPersistentDeadlines();
            RefreshDeadline(
                AgnusBusAgendaSource.Display,
                _bus.Display.RawLiveBusEligibilityVersion,
                ref _displayVersion,
                _bus.Display.GetRawLiveBusEligibilityCycle());
        }

        private void RefreshPersistentDeadlines()
        {
            var paulaVersion = _bus.Paula.RegisterWakeVersion;
            if (_paulaVersion != paulaVersion)
            {
                _paulaVersion = paulaVersion;
                RefreshPaulaDeadline();
            }

            var diskVersion = _bus.Disk.SchedulerWakeVersion;
            if (_diskVersion != diskVersion)
            {
                _diskVersion = diskVersion;
                RefreshDiskDeadline();
            }

        }

        private long GetNormalizedDeviceCandidate(long currentCycle, long targetCycle)
        {
            var display = _bus.Display.NormalizeRawLiveBusEligibilityCycle(
                _agenda.Get(AgnusBusAgendaSource.Display),
                currentCycle,
                targetCycle);
            return Math.Min(display, GetNormalizedDynamicCandidate(currentCycle, targetCycle));
        }

        private long GetNormalizedDynamicCandidate(long currentCycle, long targetCycle)
        {
            var paula = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Paula),
                currentCycle);
            var disk = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Disk),
                currentCycle);
            var copper = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Copper),
                currentCycle);
            var control = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Control),
                currentCycle);
            var engineBlitter = _bus.Blitter.NormalizeRawBusEligibilityCycle(
                _bus.Blitter.GetRawBusEligibilityCycle(),
                currentCycle);
            var pendingBlitter = NormalizePersistentEligibility(
                _agenda.Get(AgnusBusAgendaSource.Blitter),
                currentCycle);
            var blitter = Math.Min(engineBlitter, pendingBlitter);
            return Math.Min(paula, Math.Min(disk, Math.Min(copper, Math.Min(control, blitter))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long NormalizePersistentEligibility(long rawCycle, long currentCycle)
            => rawCycle == long.MaxValue
                ? long.MaxValue
                : rawCycle <= currentCycle ? currentCycle : rawCycle;

        private static bool ReadBooleanEnvironmentVariable(string name, bool defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return value?.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "yes" or "on" => true,
                "0" or "false" or "no" or "off" => false,
                _ => defaultValue
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RefreshDeadline(
            AgnusBusAgendaSource source,
            ulong version,
            ref ulong cachedVersion,
            long cycle)
        {
            if (cachedVersion == version && _agenda.Get(source) == cycle)
            {
                return;
            }

            cachedVersion = version;
            SetDeadline(source, cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetDeadline(AgnusBusAgendaSource source, long cycle)
        {
            if (_agenda.Get(source) == cycle)
            {
                return;
            }

            _agenda.Set(source, cycle);
            _agendaUpdates++;
        }
    }
}
