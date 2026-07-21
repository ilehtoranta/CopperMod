/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CopperMod.Amiga.CustomChips.Blitter
{
    internal enum BlitterAdvanceMode : byte
    {
        Reference,
        Bounded,
        Verify
    }

    internal readonly struct BlitterAdvanceCounters
    {
        public BlitterAdvanceCounters(
            long calls,
            long idleExits,
            long horizonExits,
            long boundedAttempts,
            long boundedUses,
            long slotsExamined,
            long microOpsCompleted,
            long wordsCompleted,
            long deniedSlots,
            long displayPreparations,
            long paulaSlots,
            long diskSlots,
            long barriers,
            long fallbacks,
            long verifyMatches,
            long verifyMismatches,
            string firstMismatch)
        {
            Calls = calls;
            IdleExits = idleExits;
            HorizonExits = horizonExits;
            BoundedAttempts = boundedAttempts;
            BoundedUses = boundedUses;
            SlotsExamined = slotsExamined;
            MicroOpsCompleted = microOpsCompleted;
            WordsCompleted = wordsCompleted;
            DeniedSlots = deniedSlots;
            DisplayPreparations = displayPreparations;
            PaulaSlots = paulaSlots;
            DiskSlots = diskSlots;
            Barriers = barriers;
            Fallbacks = fallbacks;
            VerifyMatches = verifyMatches;
            VerifyMismatches = verifyMismatches;
            FirstMismatch = firstMismatch;
        }

        public long Calls { get; }

        public long IdleExits { get; }

        public long HorizonExits { get; }

        public long BoundedAttempts { get; }

        public long BoundedUses { get; }

        public long SlotsExamined { get; }

        public long MicroOpsCompleted { get; }

        public long WordsCompleted { get; }

        public long DeniedSlots { get; }

        public long DisplayPreparations { get; }

        public long PaulaSlots { get; }

        public long DiskSlots { get; }

        public long Barriers { get; }

        public long Fallbacks { get; }

        public long VerifyMatches { get; }

        public long VerifyMismatches { get; }

        public string FirstMismatch { get; }
    }

    internal readonly struct BlitterSpecializationCounters
    {
        public BlitterSpecializationCounters(
            long kernelHits,
            long kernelMisses,
            long generatedKernels,
            long scalarFallbacks,
            long slotQueueAttempts = 0,
            long slotQueueEnabledBlits = 0,
            long slotQueueUnsupportedBlits = 0,
            long slotQueueWords = 0,
            long slotQueueCommittedOps = 0,
            long specializedReservations = 0,
            long rowPipelineAttempts = 0,
            long rowPipelineUsed = 0,
            long rowPipelineWords = 0,
            long rowPipelineCompletions = 0,
            long dOnlyRowWords = 0,
            long aToDRowWords = 0,
            long rowPipelineFallbacks = 0)
        {
            KernelHits = kernelHits;
            KernelMisses = kernelMisses;
            GeneratedKernels = generatedKernels;
            ScalarFallbacks = scalarFallbacks;
            SlotQueueAttempts = slotQueueAttempts;
            SlotQueueEnabledBlits = slotQueueEnabledBlits;
            SlotQueueUnsupportedBlits = slotQueueUnsupportedBlits;
            SlotQueueWords = slotQueueWords;
            SlotQueueCommittedOps = slotQueueCommittedOps;
            SpecializedReservations = specializedReservations;
            RowPipelineAttempts = rowPipelineAttempts;
            RowPipelineUsed = rowPipelineUsed;
            RowPipelineWords = rowPipelineWords;
            RowPipelineCompletions = rowPipelineCompletions;
            DOnlyRowWords = dOnlyRowWords;
            AToDRowWords = aToDRowWords;
            RowPipelineFallbacks = rowPipelineFallbacks;
        }

        public long KernelHits { get; }

        public long KernelMisses { get; }

        public long GeneratedKernels { get; }

        public long ScalarFallbacks { get; }

        public long SlotQueueAttempts { get; }

        public long SlotQueueEnabledBlits { get; }

        public long SlotQueueUnsupportedBlits { get; }

        public long SlotQueueWords { get; }

        public long SlotQueueCommittedOps { get; }

        public long SpecializedReservations { get; }

        public long RowPipelineAttempts { get; }

        public long RowPipelineUsed { get; }

        public long RowPipelineWords { get; }

        public long RowPipelineCompletions { get; }

        public long DOnlyRowWords { get; }

        public long AToDRowWords { get; }

        public long RowPipelineFallbacks { get; }
    }

    internal readonly struct BlitterPatternEntry
    {
        public BlitterPatternEntry(
            ushort bltcon0,
            ushort bltcon1,
            byte minterm,
            bool useA,
            bool useB,
            bool useC,
            bool useD,
            bool fillEnabled,
            bool fillExclusive,
            bool lineMode,
            bool descending,
            int widthWords,
            int height,
            long count)
        {
            Bltcon0 = bltcon0;
            Bltcon1 = bltcon1;
            Minterm = minterm;
            UseA = useA;
            UseB = useB;
            UseC = useC;
            UseD = useD;
            FillEnabled = fillEnabled;
            FillExclusive = fillExclusive;
            LineMode = lineMode;
            Descending = descending;
            WidthWords = widthWords;
            Height = height;
            Count = count;
        }

        public ushort Bltcon0 { get; }

        public ushort Bltcon1 { get; }

        public byte Minterm { get; }

        public bool UseA { get; }

        public bool UseB { get; }

        public bool UseC { get; }

        public bool UseD { get; }

        public bool FillEnabled { get; }

        public bool FillExclusive { get; }

        public bool LineMode { get; }

        public bool Descending { get; }

        public int WidthWords { get; }

        public int Height { get; }

        public long Count { get; }
    }

    internal readonly struct BlitterPatternKey : IEquatable<BlitterPatternKey>
    {
        public BlitterPatternKey(
            ushort bltcon0,
            ushort bltcon1,
            byte minterm,
            bool useA,
            bool useB,
            bool useC,
            bool useD,
            bool fillEnabled,
            bool fillExclusive,
            bool lineMode,
            bool descending,
            int widthWords,
            int height)
        {
            Bltcon0 = bltcon0;
            Bltcon1 = bltcon1;
            Minterm = minterm;
            UseA = useA;
            UseB = useB;
            UseC = useC;
            UseD = useD;
            FillEnabled = fillEnabled;
            FillExclusive = fillExclusive;
            LineMode = lineMode;
            Descending = descending;
            WidthWords = widthWords;
            Height = height;
        }

        public ushort Bltcon0 { get; }

        public ushort Bltcon1 { get; }

        public byte Minterm { get; }

        public bool UseA { get; }

        public bool UseB { get; }

        public bool UseC { get; }

        public bool UseD { get; }

        public bool FillEnabled { get; }

        public bool FillExclusive { get; }

        public bool LineMode { get; }

        public bool Descending { get; }

        public int WidthWords { get; }

        public int Height { get; }

        public bool Equals(BlitterPatternKey other)
            => Bltcon0 == other.Bltcon0 &&
                Bltcon1 == other.Bltcon1 &&
                Minterm == other.Minterm &&
                UseA == other.UseA &&
                UseB == other.UseB &&
                UseC == other.UseC &&
                UseD == other.UseD &&
                FillEnabled == other.FillEnabled &&
                FillExclusive == other.FillExclusive &&
                LineMode == other.LineMode &&
                Descending == other.Descending &&
                WidthWords == other.WidthWords &&
                Height == other.Height;

        public override bool Equals(object? obj)
            => obj is BlitterPatternKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Bltcon0);
            hash.Add(Bltcon1);
            hash.Add(Minterm);
            hash.Add(UseA);
            hash.Add(UseB);
            hash.Add(UseC);
            hash.Add(UseD);
            hash.Add(FillEnabled);
            hash.Add(FillExclusive);
            hash.Add(LineMode);
            hash.Add(Descending);
            hash.Add(WidthWords);
            hash.Add(Height);
            return hash.ToHashCode();
        }

        public BlitterPatternEntry ToEntry(long count)
            => new BlitterPatternEntry(
                Bltcon0,
                Bltcon1,
                Minterm,
                UseA,
                UseB,
                UseC,
                UseD,
                FillEnabled,
                FillExclusive,
                LineMode,
                Descending,
                WidthWords,
                Height,
                count);
    }

    internal readonly struct BlitterKernelKey : IEquatable<BlitterKernelKey>
    {
        private const int MintermShift = 0;
        private const int ShiftAShift = 8;
        private const int ShiftBShift = 12;
        private const int FlagShift = 16;
        private const uint NibbleMask = 0x0F;
        private const uint ByteMask = 0xFF;
        private const uint LineModeFlag = 1u << (FlagShift + 0);
        private const uint UseAFlag = 1u << (FlagShift + 1);
        private const uint UseBFlag = 1u << (FlagShift + 2);
        private const uint UseCFlag = 1u << (FlagShift + 3);
        private const uint UseDFlag = 1u << (FlagShift + 4);
        private const uint DescendingFlag = 1u << (FlagShift + 5);
        private const uint FillEnabledFlag = 1u << (FlagShift + 6);
        private const uint FillExclusiveFlag = 1u << (FlagShift + 7);
        private const uint LineSingleDotFlag = 1u << (FlagShift + 8);
        private const uint LineSudFlag = 1u << (FlagShift + 9);
        private const uint LineSulFlag = 1u << (FlagShift + 10);
        private const uint LineAulFlag = 1u << (FlagShift + 11);
        private const uint LineSignFlag = 1u << (FlagShift + 12);

        public BlitterKernelKey(
            bool lineMode,
            bool useA,
            bool useB,
            bool useC,
            bool useD,
            byte minterm,
            int shiftA,
            int shiftB,
            bool descending,
            bool fillEnabled,
            bool fillExclusive,
            bool lineSingleDot,
            bool lineSud,
            bool lineSul,
            bool lineAul,
            bool lineSign)
        {
            _bits =
                ((uint)minterm << MintermShift) |
                (((uint)shiftA & NibbleMask) << ShiftAShift) |
                (((uint)shiftB & NibbleMask) << ShiftBShift) |
                (lineMode ? LineModeFlag : 0) |
                (useA ? UseAFlag : 0) |
                (useB ? UseBFlag : 0) |
                (useC ? UseCFlag : 0) |
                (useD ? UseDFlag : 0) |
                (descending ? DescendingFlag : 0) |
                (fillEnabled ? FillEnabledFlag : 0) |
                (fillExclusive ? FillExclusiveFlag : 0) |
                (lineSingleDot ? LineSingleDotFlag : 0) |
                (lineSud ? LineSudFlag : 0) |
                (lineSul ? LineSulFlag : 0) |
                (lineAul ? LineAulFlag : 0) |
                (lineSign ? LineSignFlag : 0);
        }

        private readonly uint _bits;

        public bool LineMode => (_bits & LineModeFlag) != 0;

        public bool UseA => (_bits & UseAFlag) != 0;

        public bool UseB => (_bits & UseBFlag) != 0;

        public bool UseC => (_bits & UseCFlag) != 0;

        public bool UseD => (_bits & UseDFlag) != 0;

        public byte Minterm => (byte)((_bits >> MintermShift) & ByteMask);

        public int ShiftA => (int)((_bits >> ShiftAShift) & NibbleMask);

        public int ShiftB => (int)((_bits >> ShiftBShift) & NibbleMask);

        public bool Descending => (_bits & DescendingFlag) != 0;

        public bool FillEnabled => (_bits & FillEnabledFlag) != 0;

        public bool FillExclusive => (_bits & FillExclusiveFlag) != 0;

        public bool LineSingleDot => (_bits & LineSingleDotFlag) != 0;

        public bool LineSud => (_bits & LineSudFlag) != 0;

        public bool LineSul => (_bits & LineSulFlag) != 0;

        public bool LineAul => (_bits & LineAulFlag) != 0;

        public bool LineSign => (_bits & LineSignFlag) != 0;

        public bool Equals(BlitterKernelKey other)
            => _bits == other._bits;

        public override bool Equals(object? obj)
            => obj is BlitterKernelKey other && Equals(other);

        public override int GetHashCode()
            => (int)_bits;
    }

    internal struct BlitterAreaKernelState
    {
        public ushort PreviousA;

        public ushort PreviousB;

        public bool FillCarry;
    }

    internal static class BlitterKernelMath
    {
        public static ushort ExecuteArea(
            BlitterKernelKey key,
            ref BlitterAreaKernelState state,
            ushort rawA,
            ushort rawB,
            ushort rawC,
            ushort mask)
        {
            rawA = (ushort)(rawA & mask);
            var sourceA = ShiftSource(rawA, ref state.PreviousA, key.ShiftA, key.Descending);
            var sourceB = ShiftSource(rawB, ref state.PreviousB, key.ShiftB, key.Descending);
            var output = ApplyMinterm(key.Minterm, sourceA, sourceB, rawC);
            return key.FillEnabled
                ? ApplyFill(output, key.FillExclusive, ref state.FillCarry)
                : output;
        }

        public static ushort ExecuteLine(BlitterKernelKey key, ushort lineMask, ushort texture, ushort sourceC)
            => ApplyMinterm(key.Minterm, lineMask, texture, sourceC);

        public static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
        {
            uint result = 0;
            var notA = (ushort)~sourceA;
            var notB = (ushort)~sourceB;
            var notC = (ushort)~sourceC;
            if ((minterm & 0x01) != 0)
            {
                result |= (uint)(notA & notB & notC);
            }

            if ((minterm & 0x02) != 0)
            {
                result |= (uint)(notA & notB & sourceC);
            }

            if ((minterm & 0x04) != 0)
            {
                result |= (uint)(notA & sourceB & notC);
            }

            if ((minterm & 0x08) != 0)
            {
                result |= (uint)(notA & sourceB & sourceC);
            }

            if ((minterm & 0x10) != 0)
            {
                result |= (uint)(sourceA & notB & notC);
            }

            if ((minterm & 0x20) != 0)
            {
                result |= (uint)(sourceA & notB & sourceC);
            }

            if ((minterm & 0x40) != 0)
            {
                result |= (uint)(sourceA & sourceB & notC);
            }

            if ((minterm & 0x80) != 0)
            {
                result |= (uint)(sourceA & sourceB & sourceC);
            }

            return (ushort)result;
        }

        public static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
        {
            shift &= 0x0F;
            if (shift == 0)
            {
                previous = current;
                return current;
            }

            uint combined = descending
                ? ((uint)current << 16) | previous
                : ((uint)previous << 16) | current;
            var value = descending
                ? (ushort)(combined >> (16 - shift))
                : (ushort)(combined >> shift);
            previous = current;
            return value;
        }

        public static ushort ApplyFill(ushort value, bool exclusive, ref bool fillCarry)
        {
            ushort output = 0;
            for (var bit = 0; bit < 16; bit++)
            {
                var mask = (ushort)(1 << bit);
                var input = (value & mask) != 0;
                if (exclusive)
                {
                    if (fillCarry)
                    {
                        output |= mask;
                    }

                    if (input)
                    {
                        fillCarry = !fillCarry;
                    }

                    continue;
                }

                if (fillCarry || input)
                {
                    output |= mask;
                }

                if (input)
                {
                    fillCarry = !fillCarry;
                }
            }

            return output;
        }
    }

    internal readonly struct AmigaBlitterSnapshot
    {
        public AmigaBlitterSnapshot(
            bool busy,
            bool zero,
            ushort bltcon0,
            ushort bltcon1,
            long currentCycle,
            uint sourceA,
            uint sourceB,
            uint sourceC,
            uint destinationD,
            int widthWords,
            int height,
            int wordX,
            int rowY,
            bool lineMode,
            long nextDmaCycle,
            long lastDmaCycle,
            int completedMicroOps,
            BlitterSpecializationCounters specializationCounters,
            BlitterAdvanceCounters advanceCounters,
            BlitterPatternEntry[] topPatterns)
        {
            Busy = busy;
            Zero = zero;
            Bltcon0 = bltcon0;
            Bltcon1 = bltcon1;
            CurrentCycle = currentCycle;
            SourceA = sourceA;
            SourceB = sourceB;
            SourceC = sourceC;
            DestinationD = destinationD;
            WidthWords = widthWords;
            Height = height;
            WordX = wordX;
            RowY = rowY;
            LineMode = lineMode;
            NextDmaCycle = nextDmaCycle;
            LastDmaCycle = lastDmaCycle;
            CompletedMicroOps = completedMicroOps;
            SpecializationCounters = specializationCounters;
            AdvanceCounters = advanceCounters;
            TopPatterns = topPatterns;
        }

        public bool Busy { get; }

        public bool Zero { get; }

        public ushort Bltcon0 { get; }

        public ushort Bltcon1 { get; }

        public long CurrentCycle { get; }

        public uint SourceA { get; }

        public uint SourceB { get; }

        public uint SourceC { get; }

        public uint DestinationD { get; }

        public int WidthWords { get; }

        public int Height { get; }

        public int WordX { get; }

        public int RowY { get; }

        public bool LineMode { get; }

        public long NextDmaCycle { get; }

        public long LastDmaCycle { get; }

        public int CompletedMicroOps { get; }

        public BlitterSpecializationCounters SpecializationCounters { get; }

        public BlitterAdvanceCounters AdvanceCounters { get; }

        public BlitterPatternEntry[] TopPatterns { get; }
    }

    internal readonly struct BlitterCompiledKernel
    {
        public BlitterCompiledKernel(BlitterKernelKey key, bool supported)
        {
            Key = key;
            Supported = supported;
        }

        public BlitterKernelKey Key { get; }

        public bool Supported { get; }

        public bool SupportsArea => Supported && !Key.LineMode;

        public bool SupportsLine => Supported && Key.LineMode;

        public ushort ExecuteArea(
            ref BlitterAreaKernelState state,
            ushort rawA,
            ushort rawB,
            ushort rawC,
            ushort mask)
            => BlitterKernelMath.ExecuteArea(Key, ref state, rawA, rawB, rawC, mask);

        public ushort ExecuteLine(ushort lineMask, ushort texture, ushort sourceC)
            => BlitterKernelMath.ExecuteLine(Key, lineMask, texture, sourceC);
    }

    internal sealed class BlitterKernelCache
    {
        private const int CacheCapacity = 1024;

        private readonly BlitterKernelKey[] _keys = new BlitterKernelKey[CacheCapacity];
        private readonly BlitterCompiledKernel[] _kernels = new BlitterCompiledKernel[CacheCapacity];
        private readonly bool[] _valid = new bool[CacheCapacity];
        private long _kernelHits;
        private long _kernelMisses;
        private long _generatedKernels;
        private long _scalarFallbacks;
        private int _count;

        public BlitterCompiledKernel GetOrCreate(BlitterKernelKey key)
        {
            for (var index = 0; index < _count; index++)
            {
                if (_valid[index] && _keys[index].Equals(key))
                {
                    _kernelHits++;
                    return _kernels[index];
                }
            }

            _kernelMisses++;
            var kernel = CreateKernel(key);
            if (!kernel.Supported)
            {
                _scalarFallbacks++;
                return kernel;
            }

            _generatedKernels++;
            if (_count >= CacheCapacity)
            {
                _scalarFallbacks++;
                return default;
            }

            _keys[_count] = key;
            _kernels[_count] = kernel;
            _valid[_count] = true;
            _count++;
            return kernel;
        }

        public void RecordFallback()
        {
            _scalarFallbacks++;
        }

        public BlitterSpecializationCounters CaptureCounters()
            => new BlitterSpecializationCounters(_kernelHits, _kernelMisses, _generatedKernels, _scalarFallbacks);

        private static BlitterCompiledKernel CreateKernel(BlitterKernelKey key)
            => new BlitterCompiledKernel(key, supported: true);
    }

    internal readonly struct BlitterCpuWaitScratchResult
    {
        public BlitterCpuWaitScratchResult(
            bool supported,
            string unsupportedReason,
            long grantedCycle,
            long secondWordCycle,
            long completedCycle,
            AgnusSlotTimelineSignature timeline,
            int microOps,
            bool startedFromPartial,
            long firstDmaCycle,
            long lastDmaCycle)
        {
            Supported = supported;
            UnsupportedReason = unsupportedReason ?? string.Empty;
            GrantedCycle = grantedCycle;
            SecondWordCycle = secondWordCycle;
            CompletedCycle = completedCycle;
            Timeline = timeline;
            MicroOps = microOps;
            StartedFromPartial = startedFromPartial;
            FirstDmaCycle = firstDmaCycle;
            LastDmaCycle = lastDmaCycle;
        }

        public static BlitterCpuWaitScratchResult Unsupported(string reason)
            => new BlitterCpuWaitScratchResult(false, reason, -1, -1, -1, default, 0, false, -1, -1);

        public bool Supported { get; }

        public string UnsupportedReason { get; }

        public long GrantedCycle { get; }

        public long SecondWordCycle { get; }

        public long CompletedCycle { get; }

        public AgnusSlotTimelineSignature Timeline { get; }

        public int MicroOps { get; }

        public bool StartedFromPartial { get; }

        public long FirstDmaCycle { get; }

        public long LastDmaCycle { get; }

        public string ToDetailString()
            => Supported
                ? $"bltscratch=ops:{MicroOps},partial:{StartedFromPartial},firstLast:{FirstDmaCycle}->{LastDmaCycle}"
                : $"bltscratch=unsupported:{UnsupportedReason}";
    }

    internal sealed class Blitter
    {
        private const ushort DmaMasterEnable = 0x0200;
        private const ushort DmaBlitterEnable = 0x0040;
        private const ushort DmaBlitterNasty = 0x0400;
        private const ushort DmaconBlitterZero = 0x2000;
        private const ushort DmaconBlitterBusy = 0x4000;
        private const ushort Bltcon1LineMode = 0x0001;
        private const ushort Bltcon1SingleDot = 0x0002;
        private const ushort Bltcon1Descending = 0x0002;
        private const ushort Bltcon1FillCarryIn = 0x0004;
        private const ushort Bltcon1InclusiveFill = 0x0008;
        private const ushort Bltcon1ExclusiveFill = 0x0010;
        private const ushort Bltcon1LineSud = 0x0010;
        private const ushort Bltcon1LineSul = 0x0008;
        private const ushort Bltcon1LineAul = 0x0004;
        private const ushort Bltcon1LineSign = 0x0040;
        private const ushort BltSize = 0x058;
        private const ushort Bltcon0Low = 0x05A;
        private const ushort BltSizeVertical = 0x05C;
        private const ushort BltSizeHorizontal = 0x05E;
        private const int LegacyMaximumWidthWords = 64;
        private const int BOnlyFinalPipelineDrainSlots = 8;
        private const int LegacyMaximumHeight = 1024;
        private const int EcsMaximumWidthWords = 2048;
        private const int EcsMaximumHeight = 32768;
        private const int ChipSlotCycles = AgnusChipSlotScheduler.SlotCycles;
        private readonly AmigaBus _bus;
        private readonly bool _specializationEnabled;
        private readonly bool _patternLoggingEnabled;
        private readonly BlitterKernelCache _kernelCache = new BlitterKernelCache();
        private readonly Dictionary<BlitterPatternKey, long> _patternCounts = new Dictionary<BlitterPatternKey, long>();
        private readonly BlitterSlotQueueOp[] _areaSlotQueueOps = new BlitterSlotQueueOp[4];
        private uint _sourceA;
        private uint _sourceB;
        private uint _sourceC;
        private uint _destinationD;
        private short _sourceAModulo;
        private short _sourceBModulo;
        private short _sourceCModulo;
        private short _destinationDModulo;
        private ushort _bltcon0;
        private ushort _bltcon1;
        private ushort _firstWordMask = 0xFFFF;
        private ushort _lastWordMask = 0xFFFF;
        private ushort _dataA;
        private ushort _dataB;
        private ushort _dataC;
        private ushort _activeFirstWordMask = 0xFFFF;
        private ushort _activeLastWordMask = 0xFFFF;
        private ushort _activeDataA;
        private ushort _activeDataB;
        private ushort _activeDataC;
        private int _dataAShift;
        private int _dataBShift;
        private int _activeDataAShift;
        private int _activeDataBShift;
        private short _activeSourceAModulo;
        private short _activeSourceBModulo;
        private short _activeSourceCModulo;
        private short _activeDestinationDModulo;
        private bool _busy;
        private bool _zeroFlag = true;
        private long _currentCycle;
        private bool _useA;
        private bool _useB;
        private bool _useC;
        private bool _useD;
        private byte _minterm;
        private int _shiftA;
        private int _shiftB;
        private bool _lineMode;
        private bool _descending;
        private int _step;
        private int _widthWords;
        private int _height;
        private int _storedWidthWords;
        private int _storedHeight;
        private int _wordX;
        private int _rowY;
        private uint _workSourceA;
        private uint _workSourceB;
        private uint _workSourceC;
        private uint _workDestinationD;
        private ushort _previousA;
        private ushort _previousB;
        private bool _fillEnabled;
        private bool _fillExclusive;
        private bool _fillCarryInitial;
        private bool _fillCarry;
        private int _areaWordCycles = 4;
        private int _lineIndex;
        private int _lineLength;
        private int _lineBit;
        private int _lineY;
        private int _lineLastDrawnY;
        private bool _lineSingleDot;
        private bool _lineSud;
        private bool _lineSul;
        private bool _lineAul;
        private bool _lineSign;
        private int _lineError;
        private int _lineSourceRowStride;
        private int _lineDestinationRowStride;
        private int _lineBPatternStride;
        private long _lastDmaCycle;
        private long _lastCompletionCycle;
        private readonly List<long> _completionCycles = new List<long>();
        private int _completedMicroOps;
        private bool _completionPending;
        private ulong _wakeVersion;
        private ulong _schedulerWakeVersion;
        private readonly List<DeferredRegisterWrite> _deferredRegisterWrites = new List<DeferredRegisterWrite>();
        private bool _deferredRestartPending;
        private int _deferredRestartWidthWords;
        private int _deferredRestartHeight;
        private BlitterCompiledKernel _activeKernel;
        private BlitterAreaKernelState _areaKernelState;
        private BlitterDmaReadLatch _sourceALatch;
        private BlitterDmaReadLatch _sourceBLatch;
        private BlitterDmaReadLatch _sourceCLatch;
        private BlitterDmaWriteLatch _destinationDLatch;
        private BlitterDmaRollbackSnapshot _dmaRollbackSnapshot;
        private bool _dmaRollbackSnapshotActive;
        private bool _areaMicroOpActive;
        private bool _areaMicroOpOwnedByBoundedAdvance;
        private int _areaMicroOpIndex;
        private long _areaMicroOpStepStart;
        private long _areaMicroOpStepEnd;
        private long _areaMicroOpNextReadCycle;
        private long _areaMicroOpNextCycle;
        private long _areaMicroOpInternalCompletionCycle;
        private ushort _areaMicroOpRawA;
        private ushort _areaMicroOpRawB;
        private ushort _areaMicroOpRawC;
        private ushort _areaMicroOpMask;
        private ushort _areaMicroOpOutput;
        private bool _areaMicroOpOutputReady;
        private bool _areaMicroOpFinalWord;
        private bool _lineMicroOpActive;
        private int _lineMicroOpIndex;
        private int _lineMicroOpCount;
        private long _lineMicroOpStepEnd;
        private long _lineMicroOpNextReadCycle;
        private long _lineMicroOpNextCycle;
        private ushort _lineMicroOpSourceC;
        private ushort _lineMicroOpOutput;
        private bool _lineMicroOpOutputReady;
        private bool _lineMicroOpDraw;
        private long _cpuWaitExactSlotCycle = -1;
        private bool _orderedSlotDisplayPrepared;
        private bool _areaSlotQueueEnabled;
        private int _areaSlotQueueOpCount;
        private BlitterSlotQueueKind _areaSlotQueueKind;
        private long _slotQueueAttempts;
        private long _slotQueueEnabledBlits;
        private long _slotQueueUnsupportedBlits;
        private long _slotQueueWords;
        private long _slotQueueCommittedOps;
        private long _specializedReservations;
        private long _rowPipelineAttempts;
        private long _rowPipelineUsed;
        private long _rowPipelineWords;
        private long _rowPipelineCompletions;
        private long _dOnlyRowWords;
        private long _aToDRowWords;
        private long _rowPipelineFallbacks;
        private BlitterAdvanceMode _advanceMode;
        private bool _boundedFixedSlotExecutionEnabled;
        private bool _boundedExtendedModesEnabled;
        private BlitterFixedSlotImageCursor _boundedFixedSlotImageCursor;
        private bool _advanceProfilingEnabled;
        private long _advanceCalls;
        private long _advanceIdleExits;
        private long _advanceHorizonExits;
        private long _advanceBoundedAttempts;
        private long _advanceBoundedUses;
        private long _advanceSlotsExamined;
        private long _advanceMicroOpsCompleted;
        private long _advanceWordsCompleted;
        private long _advanceDeniedSlots;
        private long _advanceDisplayPreparations;
        private long _advancePaulaSlots;
        private long _advanceDiskSlots;
        private long _advanceBarriers;
        private long _advanceFallbacks;
        private long _advanceVerifyMatches;
        private long _advanceVerifyMismatches;
        private string _advanceFirstMismatch = string.Empty;

        public Blitter(AmigaBus bus, bool enableSpecialization = false)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _specializationEnabled = enableSpecialization;
            _patternLoggingEnabled = false;
            _advanceMode = BlitterAdvanceMode.Reference;
        }

        public ushort DmaconStatusBits
        {
            get
            {
                var status = _zeroFlag ? DmaconBlitterZero : (ushort)0;
                if (_busy)
                {
                    status |= DmaconBlitterBusy;
                }

                return status;
            }
        }

        public void Reset()
        {
            _sourceA = 0;
            _sourceB = 0;
            _sourceC = 0;
            _destinationD = 0;
            _sourceAModulo = 0;
            _sourceBModulo = 0;
            _sourceCModulo = 0;
            _destinationDModulo = 0;
            _bltcon0 = 0;
            _bltcon1 = 0;
            _firstWordMask = 0xFFFF;
            _lastWordMask = 0xFFFF;
            _dataA = 0;
            _dataB = 0;
            _dataC = 0;
            _activeFirstWordMask = 0xFFFF;
            _activeLastWordMask = 0xFFFF;
            _activeDataA = 0;
            _activeDataB = 0;
            _activeDataC = 0;
            _dataAShift = 0;
            _dataBShift = 0;
            _activeDataAShift = 0;
            _activeDataBShift = 0;
            _activeSourceAModulo = 0;
            _activeSourceBModulo = 0;
            _activeSourceCModulo = 0;
            _activeDestinationDModulo = 0;
            _busy = false;
            _zeroFlag = true;
            _currentCycle = 0;
            _useA = false;
            _useB = false;
            _useC = false;
            _useD = false;
            _minterm = 0;
            _shiftA = 0;
            _shiftB = 0;
            _lineMode = false;
            _descending = false;
            _step = 2;
            _widthWords = 0;
            _height = 0;
            _storedWidthWords = 0;
            _storedHeight = 0;
            _wordX = 0;
            _rowY = 0;
            _workSourceA = 0;
            _workSourceB = 0;
            _workSourceC = 0;
            _workDestinationD = 0;
            _previousA = 0;
            _previousB = 0;
            _fillEnabled = false;
            _fillExclusive = false;
            _fillCarryInitial = false;
            _fillCarry = false;
            _lineIndex = 0;
            _lineLength = 0;
            _lineBit = 0;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineSingleDot = false;
            _lineSud = false;
            _lineSul = false;
            _lineAul = false;
            _lineSign = false;
            _lineError = 0;
            _lineSourceRowStride = 0;
            _lineDestinationRowStride = 0;
            _lineBPatternStride = 0;
            _lastDmaCycle = 0;
            _lastCompletionCycle = 0;
            _completionCycles.Clear();
            _completedMicroOps = 0;
            _completionPending = false;
            _wakeVersion++;
            _deferredRegisterWrites.Clear();
            _deferredRestartPending = false;
            _deferredRestartWidthWords = 0;
            _deferredRestartHeight = 0;
            _activeKernel = default;
            _areaKernelState = default;
            _sourceALatch = default;
            _sourceBLatch = default;
            _sourceCLatch = default;
            _destinationDLatch = default;
            ClearAreaMicroOpState();
            ClearLineMicroOpState();
            _areaSlotQueueEnabled = false;
            _areaSlotQueueOpCount = 0;
            _areaSlotQueueKind = BlitterSlotQueueKind.None;
            _boundedFixedSlotImageCursor.Clear();
            _slotQueueAttempts = 0;
            _slotQueueEnabledBlits = 0;
            _slotQueueUnsupportedBlits = 0;
            _slotQueueWords = 0;
            _slotQueueCommittedOps = 0;
            _specializedReservations = 0;
            _rowPipelineAttempts = 0;
            _rowPipelineUsed = 0;
            _rowPipelineWords = 0;
            _rowPipelineCompletions = 0;
            _dOnlyRowWords = 0;
            _aToDRowWords = 0;
            _rowPipelineFallbacks = 0;
            ResetAdvanceProfileCounters();
            _patternCounts.Clear();
            _bus.PublishDmaconrState(0);
        }

        public AmigaBlitterSnapshot CaptureSnapshot()
        {
            return new AmigaBlitterSnapshot(
                _busy,
                _zeroFlag,
                _bltcon0,
                _bltcon1,
                _currentCycle,
                _lineMode ? _sourceA : _workSourceA,
                _workSourceB,
                _workSourceC,
                _lineMode ? _destinationD : _workDestinationD,
                _widthWords,
                _height,
                _wordX,
                _rowY,
                _lineMode,
                _currentCycle,
                _lastDmaCycle,
                _completedMicroOps,
                CaptureSpecializationCounters(),
                CaptureAdvanceCounters(),
                CaptureTopPatterns(8));
        }

        internal BlitterAdvanceMode AdvanceMode
        {
            get => _advanceMode;
            set => _advanceMode = value;
        }

        internal void SetBoundedFixedSlotExecutionEnabledForTest(bool enabled)
            => _boundedFixedSlotExecutionEnabled = enabled;

        internal void SetBoundedExtendedModesEnabledForTest(bool enabled)
            => _boundedExtendedModesEnabled = enabled;

        internal void SetAdvanceProfilingEnabled(bool enabled)
            => _advanceProfilingEnabled = enabled;

        internal void ResetAdvanceProfileCounters()
        {
            _advanceCalls = 0;
            _advanceIdleExits = 0;
            _advanceHorizonExits = 0;
            _advanceBoundedAttempts = 0;
            _advanceBoundedUses = 0;
            _advanceSlotsExamined = 0;
            _advanceMicroOpsCompleted = 0;
            _advanceWordsCompleted = 0;
            _advanceDeniedSlots = 0;
            _advanceDisplayPreparations = 0;
            _advancePaulaSlots = 0;
            _advanceDiskSlots = 0;
            _advanceBarriers = 0;
            _advanceFallbacks = 0;
            _advanceVerifyMatches = 0;
            _advanceVerifyMismatches = 0;
            _advanceFirstMismatch = string.Empty;
        }

        private BlitterAdvanceCounters CaptureAdvanceCounters()
            => new BlitterAdvanceCounters(
                _advanceCalls,
                _advanceIdleExits,
                _advanceHorizonExits,
                _advanceBoundedAttempts,
                _advanceBoundedUses,
                _advanceSlotsExamined,
                _advanceMicroOpsCompleted,
                _advanceWordsCompleted,
                _advanceDeniedSlots,
                _advanceDisplayPreparations,
                _advancePaulaSlots,
                _advanceDiskSlots,
                _advanceBarriers,
                _advanceFallbacks,
                _advanceVerifyMatches,
                _advanceVerifyMismatches,
                _advanceFirstMismatch);

        private BlitterSpecializationCounters CaptureSpecializationCounters()
        {
            var kernelCounters = _kernelCache.CaptureCounters();
            return new BlitterSpecializationCounters(
                kernelCounters.KernelHits,
                kernelCounters.KernelMisses,
                kernelCounters.GeneratedKernels,
                kernelCounters.ScalarFallbacks,
                _slotQueueAttempts,
                _slotQueueEnabledBlits,
                _slotQueueUnsupportedBlits,
                _slotQueueWords,
                _slotQueueCommittedOps,
                _specializedReservations,
                _rowPipelineAttempts,
                _rowPipelineUsed,
                _rowPipelineWords,
                _rowPipelineCompletions,
                _dOnlyRowWords,
                _aToDRowWords,
                _rowPipelineFallbacks);
        }

        public bool Busy => _busy;

        internal ulong WakeVersion => _wakeVersion;

        internal ulong SchedulerWakeVersion => _schedulerWakeVersion;

        public void WriteRegister(ushort offset, ushort value, long cycle)
        {
            System.Diagnostics.Debug.Assert(cycle >= 0, "Blitter register write cycles must be non-negative.");
            offset = CustomRegisterScheduleClassifier.NormalizeOffset(offset);
            if ((offset is Bltcon0Low or BltSizeVertical or BltSizeHorizontal) &&
                !_bus.AgnusRegisters.IsEcs)
            {
                return;
            }

            var impact = CustomRegisterScheduleClassifier.GetPotentialImpact(_bus.Chipset, offset) &
                HardwareScheduleImpact.Blitter;
            if (impact != HardwareScheduleImpact.None)
            {
                _bus.NotifyCustomRegisterScheduleChanged(offset, cycle, impact);
            }

            var schedulerWake = CaptureSchedulerWakeSignature();
            try
            {
                if (offset == BltSize)
                {
                    _storedWidthWords = DecodeLegacyWidth(value);
                    _storedHeight = DecodeLegacyHeight(value);
                    TriggerBlit(_storedWidthWords, _storedHeight, cycle);
                    _wakeVersion++;
                    return;
                }

                if (offset == BltSizeVertical)
                {
                    _storedHeight = DecodeEcsHeight(value);
                    _wakeVersion++;
                    return;
                }

                if (offset == BltSizeHorizontal)
                {
                    _storedWidthWords = DecodeEcsWidth(value);
                    if (_storedHeight == 0)
                    {
                        _storedHeight = EcsMaximumHeight;
                    }

                    TriggerBlit(_storedWidthWords, _storedHeight, cycle);
                    _wakeVersion++;
                    return;
                }

                if (_busy && ShouldDeferBusyRegisterWrite(offset))
                {
                    _deferredRegisterWrites.Add(new DeferredRegisterWrite(offset, value));
                    _wakeVersion++;
                    return;
                }

                if (offset == 0x096 && _busy && RequiresDmaForCurrentBlit())
                {
                    // A DMA-gated blit may have no scheduler wake while disabled.
                    // Re-enable it at the write horizon, never at its stale start
                    // cycle, so the first granted word is causally ordered.
                    _currentCycle = Math.Max(_currentCycle, cycle);
                }

                ApplyRegisterWrite(offset, value);
                if (offset == 0x040 && _busy && (value & 0x0100) == 0)
                {
                    _useD = false;
                    if (!_lineMode)
                    {
                        // BLTCON0 can suppress a destination cycle while a blit
                        // is active.  The causal executor keeps the DMA sequence
                        // explicitly, so remove any D operation that has not yet
                        // reached its bus slot.
                        BuildAreaMicroOpSequence();
                    }
                }

                _wakeVersion++;
            }
            finally
            {
                UpdateSchedulerWakeVersionIfChanged(schedulerWake);
                _bus.PublishDmaconrState(cycle);
            }
        }

        private void ApplyRegisterWrite(ushort offset, ushort value)
        {
            switch (offset)
            {
                case 0x040:
                    _bltcon0 = value;
                    break;
                case Bltcon0Low:
                    _bltcon0 = (ushort)((_bltcon0 & 0xFF00) | (value & 0x00FF));
                    break;
                case 0x042:
                    _bltcon1 = value;
                    break;
                case 0x044:
                    _firstWordMask = value;
                    break;
                case 0x046:
                    _lastWordMask = value;
                    break;
                case 0x048:
                    _sourceC = _bus.WriteChipDmaPointerHigh(_sourceC, value);
                    break;
                case 0x04A:
                    _sourceC = _bus.WriteChipDmaPointerLow(_sourceC, value);
                    break;
                case 0x04C:
                    _sourceB = _bus.WriteChipDmaPointerHigh(_sourceB, value);
                    break;
                case 0x04E:
                    _sourceB = _bus.WriteChipDmaPointerLow(_sourceB, value);
                    break;
                case 0x050:
                    _sourceA = _bus.WriteChipDmaPointerHigh(_sourceA, value);
                    break;
                case 0x052:
                    _sourceA = _bus.WriteChipDmaPointerLow(_sourceA, value);
                    break;
                case 0x054:
                    _destinationD = _bus.WriteChipDmaPointerHigh(_destinationD, value);
                    break;
                case 0x056:
                    _destinationD = _bus.WriteChipDmaPointerLow(_destinationD, value);
                    break;
                case 0x060:
                    _sourceCModulo = unchecked((short)value);
                    break;
                case 0x062:
                    _sourceBModulo = unchecked((short)value);
                    break;
                case 0x064:
                    _sourceAModulo = unchecked((short)value);
                    break;
                case 0x066:
                    _destinationDModulo = unchecked((short)value);
                    break;
                case 0x070:
                    _dataC = value;
                    break;
                case 0x072:
                    _dataB = value;
                    _dataBShift = (_bltcon1 >> 12) & 0x0F;
                    break;
                case 0x074:
                    _dataA = value;
                    _dataAShift = (_bltcon0 >> 12) & 0x0F;
                    break;
            }
        }

        private static bool ShouldDeferBusyRegisterWrite(ushort offset)
        {
            return offset == 0x044 ||
                offset == 0x046 ||
                (offset >= 0x048 && offset <= 0x056) ||
                (offset >= 0x060 && offset <= 0x066);
        }

        // The scheduler owns admission and calls this only after
        // HasAdvanceWorkThrough(targetCycle) succeeds.
        internal void ExecuteAdmittedWorkThrough(long targetCycle)
        {
            System.Diagnostics.Debug.Assert(targetCycle >= 0, "Blitter advance cycles must be non-negative.");
            System.Diagnostics.Debug.Assert(_busy, "Admitted blitter work requires an active blit.");
            System.Diagnostics.Debug.Assert(
                HasAdvanceWorkThrough(targetCycle),
                "Admitted blitter work must be due at the requested cycle.");
            if (_advanceProfilingEnabled)
            {
                _advanceCalls++;
            }

            var previousCycle = _currentCycle;
            var previousBusy = _busy;
            var previousCompletionPending = _completionPending;
            var schedulerWake = CaptureSchedulerWakeSignature();
            var boundedAreaScope = default(BlitterDmaAdvanceScope);
            try
            {
                if (RequiresDmaForCurrentBlit() && IsBlitterDmaEnabled())
                {
                    ExecuteCausalDmaThrough(targetCycle);
                    return;
                }

                while (_busy)
                {
                    if (_completionPending)
                    {
                        if (_currentCycle > targetCycle)
                        {
                            return;
                        }

                        FinalizePendingCompletion();
                        continue;
                    }

                    if (RequiresDmaForCurrentBlit() && !IsBlitterDmaEnabled())
                    {
                        _currentCycle = Math.Max(_currentCycle, targetCycle);
                        return;
                    }

                    var stepEndCycle = GetCurrentStepEndCycle();
                    if (stepEndCycle > targetCycle)
                    {
                        if (_advanceProfilingEnabled)
                        {
                            _advanceHorizonExits++;
                        }

                        return;
                    }

                    if (_lineMode)
                    {
                        if (_advanceMode == BlitterAdvanceMode.Bounded && CanUseBoundedLineMicroOps)
                        {
                            if (_advanceProfilingEnabled)
                            {
                                _advanceBoundedAttempts++;
                            }

                            if (!AdvanceBoundedLineMicroOpTo(targetCycle))
                            {
                                if (_advanceProfilingEnabled)
                                {
                                    _advanceFallbacks++;
                                }

                                return;
                            }

                            if (_advanceProfilingEnabled)
                            {
                                _advanceBoundedUses++;
                            }
                        }
                        else
                        {
                            _boundedFixedSlotImageCursor.Clear();
                            if (_advanceProfilingEnabled && _advanceMode != BlitterAdvanceMode.Reference)
                            {
                                _advanceBoundedAttempts++;
                                _advanceFallbacks++;
                            }

                            StepLinePixel(targetCycle);
                        }
                    }
                    else if (_advanceMode == BlitterAdvanceMode.Bounded && CanUseBoundedAreaMicroOps)
                    {
                        if (_advanceProfilingEnabled)
                        {
                            _advanceBoundedAttempts++;
                        }

                        if (!AdvanceBoundedAreaMicroOpTo(targetCycle, ref boundedAreaScope))
                        {
                            if (_advanceProfilingEnabled)
                            {
                                _advanceFallbacks++;
                            }

                            return;
                        }

                        if (_advanceProfilingEnabled)
                        {
                            _advanceBoundedUses++;
                        }
                    }
                    else
                    {
                        _boundedFixedSlotImageCursor.Clear();
                        if (_advanceProfilingEnabled && _advanceMode != BlitterAdvanceMode.Reference)
                        {
                            _advanceBoundedAttempts++;
                            _advanceFallbacks++;
                        }

                        StepAreaWord(targetCycle);
                    }
                }
            }
            finally
            {
                RecordBoundedAdvanceScope(ref boundedAreaScope);
                if (_currentCycle != previousCycle ||
                    _busy != previousBusy ||
                    _completionPending != previousCompletionPending)
                {
                    _wakeVersion++;
                }

                UpdateSchedulerWakeVersionIfChanged(schedulerWake);
                _bus.PublishDmaconrState(_currentCycle);
            }
        }

        public long GetPredictedCompletionCycle()
        {
            if (!_busy)
            {
                return _currentCycle;
            }

            if (_completionPending)
            {
                return _currentCycle;
            }

            if (RequiresDmaForCurrentBlit() && !IsBlitterDmaEnabled())
            {
                return long.MaxValue;
            }

            if (_lineMode)
            {
                var remainingPixels = Math.Max(0, _lineLength - _lineIndex);
                return _currentCycle + ((long)remainingPixels * GetLinePixelCycles());
            }

            var remainingWords = Math.Max(0, ((_height - _rowY - 1) * _widthWords) + (_widthWords - _wordX));
            var finalPipelineDrain = IsBOnlyAreaBlit()
                ? (long)BOnlyFinalPipelineDrainSlots * ChipSlotCycles
                : 0;
            return _currentCycle + ((long)remainingWords * GetAreaWordCycles()) + finalPipelineDrain;
        }

        public long? GetNextWakeCandidateCycle(long currentCycle, long targetCycle)
        {
            if (!_busy || targetCycle <= currentCycle)
            {
                return null;
            }

            if (RequiresDmaForCurrentBlit() && IsBlitterDmaEnabled())
            {
                var dueCycle = GetNextCausalDmaTransitionCycle();
                var causalCursor = Math.Max(currentCycle, _bus.ExecutedChipBusHorizon);
                var candidate = AgnusChipSlotScheduler.AlignToSlot(
                    Math.Max(dueCycle, causalCursor + 1));
                return candidate <= targetCycle ? candidate : null;
            }

            var completionCycle = GetPredictedCompletionCycle();
            if (completionCycle == long.MaxValue)
            {
                return null;
            }

            if (completionCycle > targetCycle)
            {
                return null;
            }

            return completionCycle <= currentCycle ? currentCycle + 1 : completionCycle;
        }

        internal long GetRawBusEligibilityCycle()
        {
            if (!_busy)
            {
                return long.MaxValue;
            }

            if (RequiresDmaForCurrentBlit())
            {
                return IsBlitterDmaEnabled()
                    ? GetNextCausalDmaTransitionCycle()
                    : long.MaxValue;
            }

            return GetPredictedCompletionCycle();
        }

        internal long NormalizeRawBusEligibilityCycle(long rawCycle, long currentCycle)
        {
            if (rawCycle == long.MaxValue)
            {
                return long.MaxValue;
            }

            if (RequiresDmaForCurrentBlit() && IsBlitterDmaEnabled())
            {
                var causalCursor = Math.Max(currentCycle, _bus.ExecutedChipBusHorizon);
                return AgnusChipSlotScheduler.AlignToSlot(Math.Max(rawCycle, causalCursor + 1));
            }

            return rawCycle <= currentCycle ? currentCycle + 1 : rawCycle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HasAdvanceWorkThrough(long targetCycle)
        {
            if (!_busy)
            {
                return false;
            }

            if (_completionPending)
            {
                return _currentCycle <= targetCycle;
            }

            // A disabled DMA blit still has to follow the caller's time horizon so
            // re-enabling DMA cannot resume it in the past.
            if (RequiresDmaForCurrentBlit() && !IsBlitterDmaEnabled())
            {
                return _currentCycle < targetCycle;
            }

            if (RequiresDmaForCurrentBlit())
            {
                return GetNextCausalDmaTransitionCycle() <= targetCycle;
            }

            return GetCurrentStepEndCycle() <= targetCycle;
        }

        private long GetNextCausalDmaTransitionCycle()
        {
            if (_completionPending)
            {
                return _currentCycle;
            }

            if (_lineMode)
            {
                if (!_lineMicroOpActive)
                {
                    // Starting a pixel is an internal transition.  Waking here
                    // lets the exact request cycle be derived without reserving
                    // or sampling a future slot.
                    return _currentCycle;
                }

                return _lineMicroOpIndex < _lineMicroOpCount
                    ? GetLineMicroOpRequestCycle(_lineMicroOpIndex)
                    : _lineMicroOpNextCycle;
            }

            if (!_areaMicroOpActive)
            {
                return _currentCycle;
            }

            return _areaMicroOpIndex < GetAreaMicroOpCount()
                ? GetAreaMicroOpRequestCycle(GetAreaMicroOp(_areaMicroOpIndex))
                : GetAreaMicroOpFinishCycle();
        }

        private void ExecuteCausalDmaThrough(long targetCycle)
        {
            while (_busy)
            {
                if (_completionPending)
                {
                    if (_currentCycle > targetCycle)
                    {
                        return;
                    }

                    FinalizePendingCompletion();
                    continue;
                }

                if (!IsBlitterDmaEnabled())
                {
                    _currentCycle = Math.Max(_currentCycle, targetCycle);
                    return;
                }

                if (_lineMode)
                {
                    if (!_lineMicroOpActive && !BeginLineMicroOpPixel())
                    {
                        return;
                    }

                    if (_lineMicroOpIndex >= _lineMicroOpCount)
                    {
                        if (_lineMicroOpNextCycle > targetCycle)
                        {
                            return;
                        }

                        FinishLineMicroOpPixel(targetCycle);
                        continue;
                    }

                    var lineRequestCycle = GetLineMicroOpRequestCycle(_lineMicroOpIndex);
                    if (!TryExecuteCausalLineDmaSlot(lineRequestCycle, targetCycle))
                    {
                        return;
                    }

                    continue;
                }

                if (!_areaMicroOpActive && !BeginAreaMicroOpWord())
                {
                    return;
                }

                if (_areaMicroOpIndex >= GetAreaMicroOpCount())
                {
                    if (GetAreaMicroOpFinishCycle() > targetCycle)
                    {
                        return;
                    }

                    FinishAreaMicroOpWord(targetCycle);
                    continue;
                }

                var op = GetAreaMicroOp(_areaMicroOpIndex);
                var areaRequestCycle = GetAreaMicroOpRequestCycle(op);
                if (!TryExecuteCausalAreaDmaSlot(op, areaRequestCycle, targetCycle))
                {
                    return;
                }

                continue;
            }
        }

        private long GetAreaMicroOpFinishCycle()
        {
            var stepEnd = _areaMicroOpStepEnd;
            var nextCycle = _areaMicroOpNextCycle;
            ExtendBOnlyInternalPhaseForRefresh(
                _areaMicroOpNextReadCycle,
                ref stepEnd,
                ref nextCycle);
            var internalCompletionCycle = _areaMicroOpOutputReady
                ? _areaMicroOpInternalCompletionCycle
                : Math.Max(stepEnd, _areaMicroOpNextReadCycle);
            return _areaMicroOpFinalWord && _useD
                ? internalCompletionCycle
                : nextCycle;
        }

        private bool TryExecuteCausalAreaDmaSlot(
            BlitterSlotQueueOp op,
            long requestCycle,
            long targetCycle)
        {
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, targetCycle));
            if (slotCycle != targetCycle || slotCycle < requestCycle)
            {
                return false;
            }

            if (_bus.AdvanceOrderedDmaBeforeBlitterSlot(
                    slotCycle,
                    out _,
                    out _,
                    out _,
                    out _) == OcsCpuWaitLiveSlotResult.CopperBarrier)
            {
                return false;
            }

            if (_bus.IsMandatoryRefreshSlot(slotCycle))
            {
                return false;
            }

            _cpuWaitExactSlotCycle = slotCycle;
            _orderedSlotDisplayPrepared = _bus.RequiresCanonicalBlitterDisplayPreparation;
            try
            {
                if (!ExecuteAreaMicroOp(op, requestCycle))
                {
                    return false;
                }

                _areaMicroOpIndex++;
                return true;
            }
            finally
            {
                _cpuWaitExactSlotCycle = -1;
                _orderedSlotDisplayPrepared = false;
            }
        }

        private bool TryExecuteCausalLineDmaSlot(long requestCycle, long targetCycle)
        {
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, targetCycle));
            if (slotCycle != targetCycle || slotCycle < requestCycle)
            {
                return false;
            }

            if (_bus.AdvanceOrderedDmaBeforeBlitterSlot(
                    slotCycle,
                    out _,
                    out _,
                    out _,
                    out _) == OcsCpuWaitLiveSlotResult.CopperBarrier)
            {
                return false;
            }

            if (_bus.IsMandatoryRefreshSlot(slotCycle))
            {
                return false;
            }

            _cpuWaitExactSlotCycle = slotCycle;
            _orderedSlotDisplayPrepared = _bus.RequiresCanonicalBlitterDisplayPreparation;
            try
            {
                if (!ExecuteLineMicroOp(_lineMicroOpIndex, requestCycle))
                {
                    return false;
                }

                _lineMicroOpIndex++;
                return true;
            }
            finally
            {
                _cpuWaitExactSlotCycle = -1;
                _orderedSlotDisplayPrepared = false;
            }
        }

        internal void AdvanceDmaGateHorizonTo(long cycle)
        {
            if (_busy && RequiresDmaForCurrentBlit() && !IsBlitterDmaEnabled())
            {
                _currentCycle = Math.Max(_currentCycle, cycle);
            }
        }

        private SchedulerWakeSignature CaptureSchedulerWakeSignature()
            => _busy
                ? new SchedulerWakeSignature(_completionPending, GetPredictedCompletionCycle())
                : SchedulerWakeSignature.Idle;

        private void UpdateSchedulerWakeVersionIfChanged(SchedulerWakeSignature previous)
        {
            if (!CaptureSchedulerWakeSignature().Equals(previous))
            {
                _schedulerWakeVersion++;
            }
        }

        internal bool CpuStallActive => ShouldStallCpu();

        internal long CpuStallReleaseCycle => ShouldStallCpu()
            ? GetCurrentStepEndCycle()
            : _currentCycle;

        internal long CurrentCycle => _currentCycle;

        internal bool CanUseCpuWaitAreaMicroOps
            => _areaMicroOpActive ||
                (_busy &&
                !_completionPending &&
                !_lineMode &&
                !_fillEnabled &&
                !_descending &&
                (_useA || _useB || _useC || _useD) &&
                RequiresDmaForCurrentBlit() &&
                IsBlitterDmaEnabled());

        private bool CanUseBoundedAreaMicroOps
            => (_areaMicroOpActive && _areaMicroOpOwnedByBoundedAdvance) ||
                (!_areaMicroOpActive &&
                _busy &&
                !_completionPending &&
                !_lineMode &&
                (_boundedExtendedModesEnabled || (!_fillEnabled && !_descending)) &&
                _areaSlotQueueOpCount != 0 &&
                RequiresDmaForCurrentBlit() &&
                IsBlitterDmaEnabled() &&
                (_boundedFixedSlotExecutionEnabled ||
                    !_bus.RequiresCanonicalBlitterDisplayPreparation));

        private bool CanUseBoundedLineMicroOps
            => _boundedExtendedModesEnabled &&
                !_lineMicroOpActive &&
                _busy &&
                !_completionPending &&
                _lineMode &&
                _useC &&
                RequiresDmaForCurrentBlit() &&
                IsBlitterDmaEnabled() &&
                (_boundedFixedSlotExecutionEnabled ||
                    !_bus.RequiresCanonicalBlitterDisplayPreparation);

        internal bool TryRunCpuWaitSlotScratch(
            AgnusHrmSlotEngine slots,
            AmigaBusAccessKind kind,
            AmigaBusAccessTarget target,
            uint address,
            AmigaBusAccessSize size,
            long requestedCycle,
            bool isWrite,
            out BlitterCpuWaitScratchResult result)
        {
            if (!_busy)
            {
                result = BlitterCpuWaitScratchResult.Unsupported("idle");
                return false;
            }

            if (size == AmigaBusAccessSize.Long)
            {
                result = BlitterCpuWaitScratchResult.Unsupported("cpu-long");
                return false;
            }

            if (_completionPending)
            {
                result = BlitterCpuWaitScratchResult.Unsupported("completion-pending");
                return false;
            }

            if (_lineMode || _fillEnabled || _descending)
            {
                result = BlitterCpuWaitScratchResult.Unsupported(
                    _lineMode ? "line" : _fillEnabled ? "fill" : "descending");
                return false;
            }

            if ((!_useA && !_useB && !_useC && !_useD) ||
                !RequiresDmaForCurrentBlit() ||
                !IsBlitterDmaEnabled())
            {
                result = BlitterCpuWaitScratchResult.Unsupported("no-dma");
                return false;
            }

            var scratch = new CpuWaitScratchState(this);
            slots.BeginPendingCpuSlotRequest(kind, target, address, size, requestedCycle, isWrite);
            try
            {
                var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Min(
                    Math.Max(0, requestedCycle),
                    Math.Max(0, scratch.CurrentCycle)));
                for (var attempt = 0; attempt < 4096; attempt++, slotCycle += ChipSlotCycles)
                {
                    if (!scratch.TryAdvanceAtSlot(slots, slotCycle, out var unsupportedReason))
                    {
                        if (unsupportedReason.Length != 0)
                        {
                            result = BlitterCpuWaitScratchResult.Unsupported(unsupportedReason);
                            return false;
                        }
                    }

                    if (slots.TryGrantCpuDataSingleExactSlot(
                        kind,
                        target,
                        address,
                        size,
                        requestedCycle,
                        slotCycle,
                        isWrite,
                        allowNiceBlitterSteal: true,
                        out var completedCycle))
                    {
                        var timeline = slots.CaptureOwnerTimelineSignature(requestedCycle, completedCycle);
                        result = new BlitterCpuWaitScratchResult(
                            supported: true,
                            unsupportedReason: string.Empty,
                            slotCycle,
                            slotCycle,
                            completedCycle,
                            timeline,
                            scratch.MicroOps,
                            scratch.StartedFromPartial,
                            scratch.FirstDmaCycle,
                            scratch.LastDmaCycle);
                        return true;
                    }
                }
            }
            finally
            {
                slots.ClearPendingCpuSlotRequest();
            }

            result = BlitterCpuWaitScratchResult.Unsupported("loop");
            return false;
        }

        internal bool AdvanceCpuWaitAreaMicroOpTo(long targetCycle)
        {
            if (!CanUseCpuWaitAreaMicroOps && !_areaMicroOpActive)
            {
                return false;
            }

            _cpuWaitExactSlotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, targetCycle));
            try
            {
                return AdvanceAreaMicroOpTo(targetCycle);
            }
            finally
            {
                _cpuWaitExactSlotCycle = -1;
                _bus.PublishDmaconrState(_currentCycle);
            }
        }

        internal void AdvanceCpuWaitAreaMicroOpsBefore(long targetCycle)
        {
            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, _currentCycle));
            try
            {
                while (slotCycle < targetCycle &&
                    _busy &&
                    CanUseCpuWaitAreaMicroOps)
                {
                    _cpuWaitExactSlotCycle = slotCycle;
                    AdvanceAreaMicroOpTo(slotCycle);

                    slotCycle += ChipSlotCycles;
                }
            }
            finally
            {
                _cpuWaitExactSlotCycle = -1;
                _bus.PublishDmaconrState(_currentCycle);
            }
        }

        private void TriggerBlit(int widthWords, int height, long cycle)
        {
            if (_busy)
            {
                _deferredRestartPending = true;
                _deferredRestartWidthWords = widthWords;
                _deferredRestartHeight = height;
                return;
            }

            StartBlit(widthWords, height, cycle);
        }

        private void StartBlit(int widthWords, int height, long cycle)
        {
            System.Diagnostics.Debug.Assert(cycle >= 0, "Blitter start cycles must be non-negative.");
            System.Diagnostics.Debug.Assert(widthWords > 0, "Blitter width must be positive.");
            System.Diagnostics.Debug.Assert(height > 0, "Blitter height must be positive.");
            if (_busy)
            {
                return;
            }

            DecodeCommonRegisters();
            _lineMode = (_bltcon1 & Bltcon1LineMode) != 0;
            _zeroFlag = true;
            _busy = true;
            _completionPending = false;
            _lastDmaCycle = 0;
            _lastCompletionCycle = 0;
            _completedMicroOps = 0;
            _areaSlotQueueEnabled = false;
            _areaSlotQueueOpCount = 0;
            _areaSlotQueueKind = BlitterSlotQueueKind.None;
            ClearAreaMicroOpState();
            ClearLineMicroOpState();
            _currentCycle = _bus.NextChipSlotCycle(Math.Max(_currentCycle, cycle) + ChipSlotCycles);
            _bus.NotifyHardwareWorkScheduled(_currentCycle);
            _previousA = 0;
            if (_useB)
            {
                _previousB = 0;
            }

            if (_lineMode)
            {
                StartLineBlit(widthWords, height);
                _activeKernel = _specializationEnabled
                    ? _kernelCache.GetOrCreate(CreateLineKernelKey())
                    : default;
                RecordBlitterPattern();
                return;
            }

            _widthWords = widthWords;
            _height = height;

            _wordX = 0;
            _rowY = 0;
            _descending = (_bltcon1 & Bltcon1Descending) != 0;
            _step = _descending ? -2 : 2;
            _workSourceA = GetEffectiveBlitterAddress(_sourceA);
            _workSourceB = GetEffectiveBlitterAddress(_sourceB);
            _workSourceC = GetEffectiveBlitterAddress(_sourceC);
            _workDestinationD = GetEffectiveBlitterAddress(_destinationD);
            _activeFirstWordMask = _firstWordMask;
            _activeLastWordMask = _lastWordMask;
            _activeDataA = _dataA;
            _activeDataB = _dataB;
            _activeDataC = _dataC;
            _activeDataAShift = _dataAShift;
            _activeDataBShift = _dataBShift;
            _activeSourceAModulo = _sourceAModulo;
            _activeSourceBModulo = _sourceBModulo;
            _activeSourceCModulo = _sourceCModulo;
            _activeDestinationDModulo = _destinationDModulo;
            _fillEnabled = _descending && (_bltcon1 & (Bltcon1InclusiveFill | Bltcon1ExclusiveFill)) != 0;
            _fillExclusive = (_bltcon1 & Bltcon1ExclusiveFill) != 0;
            _fillCarryInitial = (_bltcon1 & Bltcon1FillCarryIn) != 0;
            _fillCarry = _fillCarryInitial;
            _areaWordCycles = CalculateAreaWordCycles();
            _areaKernelState = new BlitterAreaKernelState
            {
                PreviousA = _previousA,
                PreviousB = _previousB,
                FillCarry = _fillCarry
            };
            _activeKernel = _specializationEnabled
                ? _kernelCache.GetOrCreate(CreateAreaKernelKey())
                : default;
            BuildAreaMicroOpSequence();
            TryBuildAreaSlotQueue();
            RecordBlitterPattern();
        }

        private void BuildAreaMicroOpSequence()
        {
            _areaSlotQueueOpCount = 0;
            if (_useA)
            {
                _areaSlotQueueOps[_areaSlotQueueOpCount++] = BlitterSlotQueueOp.ReadA;
            }

            if (_useB)
            {
                _areaSlotQueueOps[_areaSlotQueueOpCount++] = BlitterSlotQueueOp.ReadB;
            }

            if (_useC)
            {
                _areaSlotQueueOps[_areaSlotQueueOpCount++] = BlitterSlotQueueOp.ReadC;
            }

            if (_useD)
            {
                _areaSlotQueueOps[_areaSlotQueueOpCount++] = BlitterSlotQueueOp.WriteD;
            }
        }

        private void TryBuildAreaSlotQueue()
        {
            _areaSlotQueueEnabled = false;
            _areaSlotQueueKind = BlitterSlotQueueKind.None;
            if (!_specializationEnabled)
            {
                return;
            }

            _slotQueueAttempts++;
            if (_lineMode ||
                _fillEnabled ||
                _areaSlotQueueOpCount == 0)
            {
                _slotQueueUnsupportedBlits++;
                return;
            }

            _areaSlotQueueEnabled = true;
            _areaSlotQueueKind = SelectAreaSlotQueueKind();
            _slotQueueEnabledBlits++;
            if (!_descending)
            {
                _rowPipelineAttempts++;
                if (_areaSlotQueueKind == BlitterSlotQueueKind.WriteD ||
                    _areaSlotQueueKind == BlitterSlotQueueKind.ReadAWriteD)
                {
                    _rowPipelineUsed++;
                }
                else
                {
                    _rowPipelineFallbacks++;
                }
            }
        }

        private BlitterSlotQueueKind SelectAreaSlotQueueKind()
        {
            if (!_descending && !_useC && !_useB && !_useA && _useD)
            {
                return BlitterSlotQueueKind.WriteD;
            }

            if (!_descending && !_useC && !_useB && _useA && _useD)
            {
                return BlitterSlotQueueKind.ReadAWriteD;
            }

            if (!_useC && _useB && _useA && _useD)
            {
                return BlitterSlotQueueKind.ReadAReadBWriteD;
            }

            return BlitterSlotQueueKind.Generic;
        }

        private void RecordBlitterPattern()
        {
            if (!_patternLoggingEnabled)
            {
                return;
            }

            var key = new BlitterPatternKey(
                _bltcon0,
                _bltcon1,
                _minterm,
                _useA,
                _useB,
                _useC,
                _useD,
                _lineMode ? false : _fillEnabled,
                _lineMode ? false : _fillExclusive,
                _lineMode,
                _lineMode ? false : _descending,
                _widthWords,
                _height);
            _patternCounts.TryGetValue(key, out var count);
            _patternCounts[key] = count + 1;
        }

        private BlitterPatternEntry[] CaptureTopPatterns(int maxEntries)
        {
            if (!_patternLoggingEnabled)
            {
                return Array.Empty<BlitterPatternEntry>();
            }

            if (_patternCounts.Count == 0 || maxEntries <= 0)
            {
                return Array.Empty<BlitterPatternEntry>();
            }

            var entries = new List<KeyValuePair<BlitterPatternKey, long>>(_patternCounts);
            entries.Sort(static (left, right) =>
            {
                var countComparison = right.Value.CompareTo(left.Value);
                if (countComparison != 0)
                {
                    return countComparison;
                }

                var bltconComparison = left.Key.Bltcon0.CompareTo(right.Key.Bltcon0);
                if (bltconComparison != 0)
                {
                    return bltconComparison;
                }

                bltconComparison = left.Key.Bltcon1.CompareTo(right.Key.Bltcon1);
                if (bltconComparison != 0)
                {
                    return bltconComparison;
                }

                var widthComparison = left.Key.WidthWords.CompareTo(right.Key.WidthWords);
                return widthComparison != 0
                    ? widthComparison
                    : left.Key.Height.CompareTo(right.Key.Height);
            });

            var resultCount = Math.Min(maxEntries, entries.Count);
            var result = new BlitterPatternEntry[resultCount];
            for (var index = 0; index < resultCount; index++)
            {
                var entry = entries[index];
                result[index] = entry.Key.ToEntry(entry.Value);
            }

            return result;
        }

        private BlitterKernelKey CreateAreaKernelKey()
            => new BlitterKernelKey(
                false,
                _useA,
                _useB,
                _useC,
                _useD,
                _minterm,
                GetActiveAreaSourceAShift(),
                _useB ? _shiftB : _activeDataBShift,
                _descending,
                _fillEnabled,
                _fillExclusive,
                false,
                false,
                false,
                false,
                false);

        private BlitterKernelKey CreateLineKernelKey()
            => new BlitterKernelKey(
                true,
                _useA,
                _useB,
                _useC,
                _useD,
                _minterm,
                _shiftA,
                _shiftB,
                false,
                false,
                false,
                _lineSingleDot,
                _lineSud,
                _lineSul,
                _lineAul,
                _lineSign);

        private void DecodeCommonRegisters()
        {
            _useA = (_bltcon0 & 0x0800) != 0;
            _useB = (_bltcon0 & 0x0400) != 0;
            _useC = (_bltcon0 & 0x0200) != 0;
            _useD = (_bltcon0 & 0x0100) != 0;
            _minterm = (byte)(_bltcon0 & 0x00FF);
            _shiftA = (_bltcon0 >> 12) & 0x0F;
            _shiftB = (_bltcon1 >> 12) & 0x0F;
        }

        private void StartLineBlit(int widthWords, int height)
        {
            _widthWords = widthWords;
            _height = height;

            _lineLength = _height;
            _lineIndex = 0;
            _wordX = 0;
            _rowY = 0;
            _lineBit = _shiftA & 0x0F;
            _lineY = 0;
            _lineLastDrawnY = int.MinValue;
            _lineSingleDot = (_bltcon1 & Bltcon1SingleDot) != 0;
            _lineSud = (_bltcon1 & Bltcon1LineSud) != 0;
            _lineSul = (_bltcon1 & Bltcon1LineSul) != 0;
            _lineAul = (_bltcon1 & Bltcon1LineAul) != 0;
            _lineSign = (_bltcon1 & Bltcon1LineSign) != 0;
            _lineError = unchecked((short)_sourceA);
            _lineSourceRowStride = _sourceCModulo & ~1;
            _lineDestinationRowStride = _lineSourceRowStride;
            _lineBPatternStride = _sourceBModulo & ~1;

            _workSourceC = GetEffectiveBlitterAddress(_sourceC);
            _workSourceB = GetEffectiveBlitterAddress(_sourceB);
            _workDestinationD = GetEffectiveBlitterAddress(_destinationD);
        }

        private void StepAreaWord(long targetCycle)
        {
            if (_areaSlotQueueEnabled)
            {
                StepAreaWordFromSlotQueue(targetCycle);
                return;
            }

            BeginDmaRollbackSnapshot();
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetAreaWordCycles();
            stepEnd += GetAreaFillIdlePhaseDelay(stepStart, stepEnd);
            var nextReadCycle = stepStart;
            var nextCycle = stepEnd;
            var isFinalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            var mask = 0xFFFF;
            if (_wordX == 0)
            {
                mask &= _activeFirstWordMask;
            }

            if (_wordX == _widthWords - 1)
            {
                mask &= _activeLastWordMask;
            }

            var rawA = _activeDataA;
            if (_useA)
            {
                var requestCycle = nextReadCycle;
                _sourceALatch = LoadSourceDmaLatch(BlitterDmaSource.A, ref _workSourceA, _step, requestCycle);
                var access = _sourceALatch.BusAccess;
                if (!TryConsumeSourceDmaLatch(ref _sourceALatch, out rawA))
                {
                    return;
                }

                var stall = access.GrantedCycle - requestCycle;
                stepEnd += stall;
                nextCycle += stall;
                nextReadCycle = access.CompletedCycle;
                nextCycle = Math.Max(nextCycle, access.CompletedCycle);
            }

            var rawB = _activeDataB;
            if (_useB)
            {
                var requestCycle = nextReadCycle;
                _sourceBLatch = LoadSourceDmaLatch(BlitterDmaSource.B, ref _workSourceB, _step, requestCycle);
                var access = _sourceBLatch.BusAccess;
                if (!TryConsumeSourceDmaLatch(ref _sourceBLatch, out rawB))
                {
                    return;
                }

                var stall = access.GrantedCycle - requestCycle;
                stepEnd += stall;
                nextCycle += stall;
                nextReadCycle = access.CompletedCycle;
                nextCycle = Math.Max(nextCycle, access.CompletedCycle);
            }

            var rawC = _activeDataC;
            if (_useC)
            {
                var requestCycle = nextReadCycle;
                _sourceCLatch = LoadSourceDmaLatch(BlitterDmaSource.C, ref _workSourceC, _step, requestCycle);
                var access = _sourceCLatch.BusAccess;
                if (!TryConsumeSourceDmaLatch(ref _sourceCLatch, out rawC))
                {
                    return;
                }

                var stall = access.GrantedCycle - requestCycle;
                stepEnd += stall;
                nextCycle += stall;
                _activeDataC = rawC;
                nextReadCycle = access.CompletedCycle;
                nextCycle = Math.Max(nextCycle, access.CompletedCycle);
            }

            var output = ExecuteAreaFromSourceLatches(rawA, rawB, rawC, (ushort)mask);

            if (output != 0)
            {
                _zeroFlag = false;
            }

            ExtendBOnlyInternalPhaseForRefresh(nextReadCycle, ref stepEnd, ref nextCycle);

            var internalCompletionCycle = Math.Max(stepEnd, nextReadCycle);
            if (_useD)
            {
                var writeCycle = Math.Max(nextReadCycle, stepEnd - ChipSlotCycles);
                _destinationDLatch = CreateDestinationDmaLatch(output);
                if (!TryCommitDestinationDmaLatch(ref _workDestinationD, _step, ref _destinationDLatch, writeCycle, out var write))
                {
                    return;
                }

                nextCycle = Math.Max(nextCycle, write.CompletedCycle);
            }

            _currentCycle = isFinalWord && _useD
                ? internalCompletionCycle
                : nextCycle;
            EndDmaRollbackSnapshot();
            AdvanceAreaPosition(targetCycle);
        }

        private bool AdvanceAreaMicroOpTo(long targetCycle)
        {
            if (!_areaMicroOpActive)
            {
                if (!BeginAreaMicroOpWord())
                {
                    return false;
                }
            }

            while (_areaMicroOpIndex < GetAreaMicroOpCount())
            {
                var op = GetAreaMicroOp(_areaMicroOpIndex);
                var requestCycle = GetAreaMicroOpRequestCycle(op);
                if (requestCycle > targetCycle)
                {
                    return false;
                }

                if (!ExecuteAreaMicroOp(op, requestCycle))
                {
                    return false;
                }

                _areaMicroOpIndex++;
                return true;
            }

            if (_areaMicroOpNextCycle > targetCycle)
            {
                return false;
            }

            FinishAreaMicroOpWord(targetCycle);
            return true;
        }

        private bool AdvanceBoundedAreaMicroOpTo(
            long targetCycle,
            ref BlitterDmaAdvanceScope scope)
        {
            if (!_areaMicroOpActive && !BeginAreaMicroOpWord())
            {
                return false;
            }

            if (_areaMicroOpIndex >= GetAreaMicroOpCount())
            {
                if (_areaMicroOpNextCycle > targetCycle)
                {
                    return false;
                }

                FinishAreaMicroOpWord(targetCycle);
                return true;
            }

            if (!_boundedFixedSlotExecutionEnabled)
            {
                return AdvanceAdmittedAreaMicroOpWord(targetCycle);
            }

            _areaMicroOpOwnedByBoundedAdvance = true;
            scope.Initialize(GetAreaMicroOpRequestCycle(GetAreaMicroOp(_areaMicroOpIndex)));
            var plannedGrantCycles = default(BlitterAreaWordGrantPlan);
            if (!TryBuildBoundedAreaWordPlan(
                    ref plannedGrantCycles,
                    out var requiresOrderedDynamicDma))
            {
                _boundedFixedSlotImageCursor.Clear();
                if (requiresOrderedDynamicDma)
                {
                    ResetBoundedFixedDmaScope(ref scope);
                    return AdvanceBoundedAreaWordOrdered(targetCycle, ref scope);
                }

                scope.Barrier = true;
                ResetBoundedFixedDmaScope(ref scope);
                return AdvanceAdmittedAreaMicroOpWord(targetCycle);
            }

            while (_areaMicroOpIndex < GetAreaMicroOpCount())
            {
                var op = GetAreaMicroOp(_areaMicroOpIndex);
                var requestCycle = GetAreaMicroOpRequestCycle(op);
                var grantCycle = plannedGrantCycles.Get(_areaMicroOpIndex);
                if (!PrepareBoundedFixedDmaSlot(
                        grantCycle,
                        ref scope,
                        out var liveResult))
                {
                    _boundedFixedSlotImageCursor.Clear();
                    if (liveResult == OcsCpuWaitLiveSlotResult.CopperBarrier)
                    {
                        scope.Barrier = true;
                    }

                    ResetBoundedFixedDmaScope(ref scope);
                    return AdvanceAdmittedAreaMicroOpWord(targetCycle);
                }

                _cpuWaitExactSlotCycle = grantCycle;
                _orderedSlotDisplayPrepared = true;
                if (!ExecuteAreaMicroOp(op, requestCycle))
                {
                    RecordBoundedAdvanceDenial(op, requestCycle, grantCycle);
                    _cpuWaitExactSlotCycle = -1;
                    _orderedSlotDisplayPrepared = false;
                    scope.Barrier = true;
                    ResetBoundedFixedDmaScope(ref scope);
                    return AdvanceAdmittedAreaMicroOpWord(targetCycle);
                }

                _cpuWaitExactSlotCycle = -1;
                _orderedSlotDisplayPrepared = false;
                _areaMicroOpIndex++;
            }

            FinishAreaMicroOpWord(targetCycle);
            return true;
        }

        private bool AdvanceBoundedAreaWordOrdered(
            long targetCycle,
            ref BlitterDmaAdvanceScope scope)
        {
            while (_areaMicroOpIndex < GetAreaMicroOpCount())
            {
                var op = GetAreaMicroOp(_areaMicroOpIndex);
                var requestCycle = GetAreaMicroOpRequestCycle(op);
                var committed = false;
                for (var attempt = 0; attempt < 512; attempt++)
                {
                    if (!TryPrepareBoundedOrderedDmaSlot(
                            GetAreaMicroOpAddress(op),
                            requestCycle,
                            op == BlitterSlotQueueOp.WriteD,
                            ref scope,
                            out var grantCycle))
                    {
                        return FallBackBoundedAreaWord(
                            targetCycle,
                            ref scope,
                            barrier: true);
                    }

                    _cpuWaitExactSlotCycle = grantCycle;
                    _orderedSlotDisplayPrepared = true;
                    if (ExecuteAreaMicroOp(op, requestCycle))
                    {
                        _cpuWaitExactSlotCycle = -1;
                        _orderedSlotDisplayPrepared = false;
                        _areaMicroOpIndex++;
                        committed = true;
                        break;
                    }

                    RecordBoundedAdvanceDenial(op, requestCycle, grantCycle);
                    _cpuWaitExactSlotCycle = -1;
                    _orderedSlotDisplayPrepared = false;
                }

                if (!committed)
                {
                    return FallBackBoundedAreaWord(
                        targetCycle,
                        ref scope,
                        barrier: true);
                }
            }

            FinishAreaMicroOpWord(targetCycle);
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool FallBackBoundedAreaWord(
            long targetCycle,
            ref BlitterDmaAdvanceScope scope,
            bool barrier)
        {
            _cpuWaitExactSlotCycle = -1;
            _orderedSlotDisplayPrepared = false;
            _boundedFixedSlotImageCursor.Clear();
            scope.Barrier |= barrier;
            ResetBoundedFixedDmaScope(ref scope);
            return AdvanceAdmittedAreaMicroOpWord(targetCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetBoundedFixedDmaScope(ref BlitterDmaAdvanceScope scope)
        {
            scope.Initialized = false;
            scope.CurrentSlot = 0;
        }

        private void RecordBoundedAdvanceScope(ref BlitterDmaAdvanceScope scope)
        {
            if (!_advanceProfilingEnabled)
            {
                return;
            }

            _advanceSlotsExamined += scope.SlotsExamined;
            _advanceDisplayPreparations += scope.DisplayPreparations;
            _advancePaulaSlots += scope.PaulaSlots;
            _advanceDiskSlots += scope.DiskSlots;
            if (scope.Barrier)
            {
                _advanceBarriers++;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RecordBoundedAdvanceDenial(
            BlitterSlotQueueOp op,
            long requestedCycle,
            long plannedGrantCycle)
        {
            if (!_advanceProfilingEnabled || _advanceFirstMismatch.Length != 0)
            {
                return;
            }

            _advanceFirstMismatch =
                $"bounded-denied/op={op}/req={requestedCycle}/planned={plannedGrantCycle}/" +
                _bus.DescribeBlitterFixedSlotPrediction(plannedGrantCycle);
        }

        private bool TryBuildBoundedAreaWordPlan(
            ref BlitterAreaWordGrantPlan grantCycles,
            out bool requiresOrderedDynamicDma)
        {
            requiresOrderedDynamicDma = false;
            var nextReadCycle = _areaMicroOpNextReadCycle;
            var stepEndCycle = _areaMicroOpStepEnd;
            var lastCompletionCycle = _areaMicroOpStepStart;
            for (var index = _areaMicroOpIndex; index < GetAreaMicroOpCount(); index++)
            {
                var op = GetAreaMicroOp(index);
                var requestCycle = op == BlitterSlotQueueOp.WriteD
                    ? Math.Max(nextReadCycle, stepEndCycle - ChipSlotCycles)
                    : nextReadCycle;
                if (!_bus.TryPredictBlitterFixedSlotGrantCandidate(
                        GetAreaMicroOpAddress(op),
                        requestCycle,
                        op == BlitterSlotQueueOp.WriteD,
                        ref _boundedFixedSlotImageCursor,
                        out var grantCycle,
                        out _,
                        out _))
                {
                    return false;
                }

                grantCycles.Set(index, grantCycle);
                lastCompletionCycle = grantCycle + ChipSlotCycles;
                if (op != BlitterSlotQueueOp.WriteD)
                {
                    var stallCycles = grantCycle - requestCycle;
                    stepEndCycle += stallCycles;
                    nextReadCycle = lastCompletionCycle;
                }
            }

            if (!_bus.CanAdvanceBlitterFixedDisplayScopeThrough(lastCompletionCycle))
            {
                return false;
            }

            var hasDynamicDma = _bus.HasBlitterDynamicDmaWorkThrough(lastCompletionCycle);
            requiresOrderedDynamicDma = hasDynamicDma &&
                !_bus.RequiresCanonicalBlitterDisplayPreparation;
            return !hasDynamicDma;
        }

        private bool PrepareBoundedFixedDmaSlot(
            long grantedCycle,
            ref BlitterDmaAdvanceScope scope,
            out OcsCpuWaitLiveSlotResult liveResult)
        {
            var firstExaminedSlot = Math.Max(scope.CurrentSlot, grantedCycle);
            if (grantedCycle >= firstExaminedSlot)
            {
                scope.SlotsExamined +=
                    ((grantedCycle - firstExaminedSlot) / ChipSlotCycles) + 1;
            }

            liveResult = _bus.AdvanceFixedDmaBeforeBlitterSlotInScope(
                grantedCycle,
                out var bitplaneFetches,
                out var spriteFetches);
            if (bitplaneFetches != 0 || spriteFetches != 0)
            {
                scope.DisplayPreparations++;
            }

            scope.CurrentSlot = grantedCycle + ChipSlotCycles;
            return liveResult != OcsCpuWaitLiveSlotResult.CopperBarrier;
        }

        private bool TryPrepareBoundedOrderedDmaSlot(
            uint address,
            long requestedCycle,
            bool isWrite,
            ref BlitterDmaAdvanceScope scope,
            out long grantedCycle)
        {
            if (!_bus.TryPredictBlitterFixedSlotGrantCandidate(
                    address,
                    requestedCycle,
                    isWrite,
                    ref _boundedFixedSlotImageCursor,
                    out grantedCycle,
                    out _,
                    out _))
            {
                scope.Barrier = true;
                return false;
            }

            if (!_bus.CanAdvanceBlitterFixedDisplayScopeThrough(grantedCycle + ChipSlotCycles))
            {
                scope.Barrier = true;
                return false;
            }

            var firstExaminedSlot = Math.Max(
                scope.CurrentSlot,
                AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle)));
            if (grantedCycle >= firstExaminedSlot)
            {
                scope.SlotsExamined +=
                    ((grantedCycle - firstExaminedSlot) / ChipSlotCycles) + 1;
            }

            var liveResult = _bus.AdvanceOrderedDmaBeforeBlitterSlot(
                grantedCycle,
                out var bitplaneFetches,
                out var spriteFetches,
                out var advancedPaula,
                out var advancedDisk);
            if (advancedPaula)
            {
                scope.PaulaSlots++;
            }

            if (advancedDisk)
            {
                scope.DiskSlots++;
            }

            if (bitplaneFetches != 0 || spriteFetches != 0)
            {
                scope.DisplayPreparations++;
            }

            scope.CurrentSlot = grantedCycle + ChipSlotCycles;
            if (liveResult != OcsCpuWaitLiveSlotResult.CopperBarrier)
            {
                return true;
            }

            scope.Barrier = true;
            return false;
        }

        private uint GetAreaMicroOpAddress(BlitterSlotQueueOp op)
            => GetEffectiveBlitterAddress(op switch
            {
                BlitterSlotQueueOp.ReadA => _workSourceA,
                BlitterSlotQueueOp.ReadB => _workSourceB,
                BlitterSlotQueueOp.ReadC => _workSourceC,
                BlitterSlotQueueOp.WriteD => _workDestinationD,
                _ => 0
            });

        private bool AdvanceAdmittedAreaMicroOpWord(long targetCycle)
        {
            // AdvanceTo has already admitted this word from its nominal step end.
            // Scalar StepAreaWord then completes every DMA operation, even when
            // contention pushes a later request beyond targetCycle.
            while (true)
            {
                if (!_areaMicroOpActive && !BeginAreaMicroOpWord())
                {
                    return false;
                }

                while (_areaMicroOpIndex < GetAreaMicroOpCount())
                {
                    var op = GetAreaMicroOp(_areaMicroOpIndex);
                    var requestCycle = GetAreaMicroOpRequestCycle(op);
                    if (!ExecuteAreaMicroOp(op, requestCycle))
                    {
                        break;
                    }

                    _areaMicroOpIndex++;
                }

                if (_areaMicroOpActive && _areaMicroOpIndex >= GetAreaMicroOpCount())
                {
                    FinishAreaMicroOpWord(targetCycle);
                    return true;
                }

                // A denied generic slot execution rolls the word back exactly like
                // StepAreaWord. Retry only when the scalar outer loop would still
                // admit the word at the updated cycle.
                if (_areaMicroOpActive || GetCurrentStepEndCycle() > targetCycle)
                {
                    return false;
                }
            }
        }

        private bool BeginAreaMicroOpWord()
        {
            if (_lineMode ||
                (! _useA && !_useB && !_useC && !_useD))
            {
                return false;
            }

            BeginDmaRollbackSnapshot();
            _areaMicroOpActive = true;
            _areaMicroOpOwnedByBoundedAdvance = false;
            _areaMicroOpIndex = 0;
            _areaMicroOpStepStart = _currentCycle;
            _areaMicroOpStepEnd = _areaMicroOpStepStart + GetAreaWordCycles();
            if (_fillEnabled)
            {
                _areaMicroOpStepEnd += GetAreaFillIdlePhaseDelay(
                    _areaMicroOpStepStart,
                    _areaMicroOpStepEnd);
            }
            _areaMicroOpNextReadCycle = _areaMicroOpStepStart;
            _areaMicroOpNextCycle = _areaMicroOpStepEnd;
            _areaMicroOpInternalCompletionCycle = _areaMicroOpStepEnd;
            _areaMicroOpRawA = _activeDataA;
            _areaMicroOpRawB = _activeDataB;
            _areaMicroOpRawC = _activeDataC;
            _areaMicroOpMask = GetCurrentAreaWordMask();
            _areaMicroOpOutput = 0;
            _areaMicroOpOutputReady = false;
            _areaMicroOpFinalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            return true;
        }

        private int GetAreaMicroOpCount()
            => _areaSlotQueueOpCount;

        private BlitterSlotQueueOp GetAreaMicroOp(int index)
            => _areaSlotQueueOps[index];

        private long GetAreaMicroOpRequestCycle(BlitterSlotQueueOp op)
            => op == BlitterSlotQueueOp.WriteD
                ? Math.Max(_areaMicroOpNextReadCycle, _areaMicroOpStepEnd - ChipSlotCycles)
                : _areaMicroOpNextReadCycle;

        private bool ExecuteAreaMicroOp(BlitterSlotQueueOp op, long requestCycle)
        {
            switch (op)
            {
                case BlitterSlotQueueOp.ReadA:
                {
                    _sourceALatch = LoadSourceDmaLatch(BlitterDmaSource.A, ref _workSourceA, _step, requestCycle);
                    var access = _sourceALatch.BusAccess;
                    if (!TryConsumeSourceDmaLatch(ref _sourceALatch, out _areaMicroOpRawA))
                    {
                        return false;
                    }

                    AccountAreaMicroOpReadWait(requestCycle, access.CompletedCycle);
                    _areaMicroOpNextReadCycle = access.CompletedCycle;
                    _areaMicroOpNextCycle = Math.Max(_areaMicroOpNextCycle, access.CompletedCycle);
                    return true;
                }

                case BlitterSlotQueueOp.ReadB:
                {
                    _sourceBLatch = LoadSourceDmaLatch(BlitterDmaSource.B, ref _workSourceB, _step, requestCycle);
                    var access = _sourceBLatch.BusAccess;
                    if (!TryConsumeSourceDmaLatch(ref _sourceBLatch, out _areaMicroOpRawB))
                    {
                        return false;
                    }

                    AccountAreaMicroOpReadWait(requestCycle, access.CompletedCycle);
                    _areaMicroOpNextReadCycle = access.CompletedCycle;
                    _areaMicroOpNextCycle = Math.Max(_areaMicroOpNextCycle, access.CompletedCycle);
                    return true;
                }

                case BlitterSlotQueueOp.ReadC:
                {
                    _sourceCLatch = LoadSourceDmaLatch(BlitterDmaSource.C, ref _workSourceC, _step, requestCycle);
                    var access = _sourceCLatch.BusAccess;
                    if (!TryConsumeSourceDmaLatch(ref _sourceCLatch, out _areaMicroOpRawC))
                    {
                        return false;
                    }

                    _activeDataC = _areaMicroOpRawC;
                    AccountAreaMicroOpReadWait(requestCycle, access.CompletedCycle);
                    _areaMicroOpNextReadCycle = access.CompletedCycle;
                    _areaMicroOpNextCycle = Math.Max(_areaMicroOpNextCycle, access.CompletedCycle);
                    return true;
                }

                case BlitterSlotQueueOp.WriteD:
                {
                    EnsureAreaMicroOpOutputReady();
                    _destinationDLatch = CreateDestinationDmaLatch(_areaMicroOpOutput);
                    if (!TryCommitDestinationDmaLatch(ref _workDestinationD, _step, ref _destinationDLatch, requestCycle, out var write))
                    {
                        return false;
                    }

                    _areaMicroOpNextCycle = Math.Max(_areaMicroOpNextCycle, write.CompletedCycle);
                    return true;
                }

                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AccountAreaMicroOpReadWait(long requestedCycle, long completedCycle)
        {
            var waitCycles = completedCycle - (requestedCycle + ChipSlotCycles);
            if (waitCycles <= 0)
            {
                return;
            }

            _areaMicroOpStepEnd += waitCycles;
            _areaMicroOpNextCycle += waitCycles;
        }

        private void EnsureAreaMicroOpOutputReady()
        {
            if (_areaMicroOpOutputReady)
            {
                return;
            }

            _areaMicroOpOutput = ExecuteAreaFromSourceLatches(
                _areaMicroOpRawA,
                _areaMicroOpRawB,
                _areaMicroOpRawC,
                _areaMicroOpMask);
            if (_areaMicroOpOutput != 0)
            {
                _zeroFlag = false;
            }

            _areaMicroOpOutputReady = true;
            _areaMicroOpInternalCompletionCycle = Math.Max(_areaMicroOpStepEnd, _areaMicroOpNextReadCycle);
        }

        private void FinishAreaMicroOpWord(long targetCycle)
        {
            ExtendBOnlyInternalPhaseForRefresh(
                _areaMicroOpNextReadCycle,
                ref _areaMicroOpStepEnd,
                ref _areaMicroOpNextCycle);
            if (!_areaMicroOpOutputReady)
            {
                EnsureAreaMicroOpOutputReady();
            }

            _currentCycle = _areaMicroOpFinalWord && _useD
                ? _areaMicroOpInternalCompletionCycle
                : _areaMicroOpNextCycle;
            ClearAreaMicroOpState();
            EndDmaRollbackSnapshot();
            AdvanceAreaPosition(targetCycle);
        }

        private void ClearAreaMicroOpState()
        {
            _areaMicroOpActive = false;
            _areaMicroOpOwnedByBoundedAdvance = false;
            _areaMicroOpIndex = 0;
        }

        private bool AdvanceBoundedLineMicroOpTo(long targetCycle)
        {
            if (!_lineMicroOpActive && !BeginLineMicroOpPixel())
            {
                return false;
            }

            if (_lineMicroOpIndex >= _lineMicroOpCount)
            {
                if (_lineMicroOpNextCycle > targetCycle)
                {
                    return false;
                }

                FinishLineMicroOpPixel(targetCycle);
                return true;
            }

            if (!_boundedFixedSlotExecutionEnabled)
            {
                return AdvanceAdmittedLineMicroOpPixel(targetCycle);
            }

            var scope = new BlitterDmaAdvanceScope(
                GetLineMicroOpRequestCycle(_lineMicroOpIndex));
            try
            {
                while (_lineMicroOpIndex < _lineMicroOpCount)
                {
                    var requestCycle = GetLineMicroOpRequestCycle(_lineMicroOpIndex);
                    var write = _lineMicroOpIndex == _lineMicroOpCount - 1;
                    if (!TryPrepareBoundedDmaSlot(
                            GetLineMicroOpAddress(_lineMicroOpIndex),
                            requestCycle,
                            write,
                            ref scope,
                            out var grantCycle))
                    {
                        return AdvanceAdmittedLineMicroOpPixel(targetCycle);
                    }

                    _cpuWaitExactSlotCycle = grantCycle;
                    _orderedSlotDisplayPrepared = true;
                    if (!ExecuteLineMicroOp(_lineMicroOpIndex, requestCycle))
                    {
                        _cpuWaitExactSlotCycle = -1;
                        _orderedSlotDisplayPrepared = false;
                        scope.Barrier = true;
                        return AdvanceAdmittedLineMicroOpPixel(targetCycle);
                    }

                    _cpuWaitExactSlotCycle = -1;
                    _orderedSlotDisplayPrepared = false;
                    _lineMicroOpIndex++;
                }

                FinishLineMicroOpPixel(targetCycle);
                return true;
            }
            finally
            {
                _cpuWaitExactSlotCycle = -1;
                _orderedSlotDisplayPrepared = false;
                if (_advanceProfilingEnabled)
                {
                    _advanceSlotsExamined += scope.SlotsExamined;
                    _advanceDisplayPreparations += scope.DisplayPreparations;
                    _advancePaulaSlots += scope.PaulaSlots;
                    _advanceDiskSlots += scope.DiskSlots;
                    if (scope.Barrier)
                    {
                        _advanceBarriers++;
                    }
                }
            }
        }

        private uint GetLineMicroOpAddress(int index)
        {
            if (_useB && index < 2)
            {
                return GetEffectiveBlitterAddress(_workSourceB);
            }

            var sourceCIndex = _useB ? 2 : 0;
            if (index == sourceCIndex)
            {
                return GetEffectiveBlitterAddress(_workSourceC);
            }

            return GetEffectiveBlitterAddress(_lineIndex == 0
                ? _workDestinationD
                : _workSourceC);
        }

        private bool AdvanceAdmittedLineMicroOpPixel(long targetCycle)
        {
            while (true)
            {
                if (!_lineMicroOpActive && !BeginLineMicroOpPixel())
                {
                    return false;
                }

                while (_lineMicroOpIndex < _lineMicroOpCount)
                {
                    var requestCycle = GetLineMicroOpRequestCycle(_lineMicroOpIndex);
                    if (!ExecuteLineMicroOp(_lineMicroOpIndex, requestCycle))
                    {
                        break;
                    }

                    _lineMicroOpIndex++;
                }

                if (_lineMicroOpActive && _lineMicroOpIndex >= _lineMicroOpCount)
                {
                    FinishLineMicroOpPixel(targetCycle);
                    return true;
                }

                if (_lineMicroOpActive || GetCurrentStepEndCycle() > targetCycle)
                {
                    return false;
                }
            }
        }

        private bool AdvanceLineMicroOpTo(long targetCycle)
        {
            if (!_lineMicroOpActive && !BeginLineMicroOpPixel())
            {
                return false;
            }

            if (_lineMicroOpIndex < _lineMicroOpCount)
            {
                var requestCycle = GetLineMicroOpRequestCycle(_lineMicroOpIndex);
                if (requestCycle > targetCycle || !ExecuteLineMicroOp(_lineMicroOpIndex, requestCycle))
                {
                    return false;
                }

                _lineMicroOpIndex++;
                return true;
            }

            if (_lineMicroOpNextCycle > targetCycle)
            {
                return false;
            }

            FinishLineMicroOpPixel(targetCycle);
            return true;
        }

        private bool BeginLineMicroOpPixel()
        {
            if (!_lineMode || !_useC)
            {
                return false;
            }

            BeginDmaRollbackSnapshot();
            _lineMicroOpActive = true;
            _lineMicroOpIndex = 0;
            _lineMicroOpStepEnd = _currentCycle + GetLinePixelCycles();
            _lineMicroOpNextReadCycle = _useB ? _currentCycle : _currentCycle + ChipSlotCycles;
            _lineMicroOpNextCycle = _lineMicroOpStepEnd;
            _lineMicroOpSourceC = 0;
            _lineMicroOpOutput = 0;
            _lineMicroOpOutputReady = false;
            _lineMicroOpDraw = !_lineSingleDot || _lineY != _lineLastDrawnY;
            _lineMicroOpCount = _lineMicroOpDraw ? (_useB ? 4 : 2) : 0;
            return true;
        }

        private long GetLineMicroOpRequestCycle(int index)
            => index == _lineMicroOpCount - 1
                ? Math.Max(_lineMicroOpNextReadCycle, _lineMicroOpStepEnd - ChipSlotCycles)
                : _lineMicroOpNextReadCycle;

        private bool ExecuteLineMicroOp(int index, long requestCycle)
        {
            if (_useB && index < 2)
            {
                _sourceBLatch = LoadLineBPatternLatch(requestCycle);
                var access = _sourceBLatch.BusAccess;
                if (!TryConsumeSourceDmaLatch(
                        ref _sourceBLatch,
                        out var value))
                {
                    return false;
                }

                if (index == 1)
                {
                    _dataB = value;
                    _workSourceB = _bus.AddChipDmaPointerOffset(_workSourceB, _lineBPatternStride);
                }

                AccountLineMicroOpReadWait(requestCycle, access.CompletedCycle);
                _lineMicroOpNextReadCycle = access.CompletedCycle;
                _lineMicroOpNextCycle = Math.Max(_lineMicroOpNextCycle, access.CompletedCycle);
                return true;
            }

            var sourceCIndex = _useB ? 2 : 0;
            if (index == sourceCIndex)
            {
                _sourceCLatch = LoadSourceDmaLatch(BlitterDmaSource.C, _workSourceC, requestCycle);
                var access = _sourceCLatch.BusAccess;
                if (!TryConsumeSourceDmaLatch(ref _sourceCLatch, out _lineMicroOpSourceC))
                {
                    return false;
                }

                AccountLineMicroOpReadWait(requestCycle, access.CompletedCycle);
                _lineMicroOpNextReadCycle = access.CompletedCycle;
                _lineMicroOpNextCycle = Math.Max(_lineMicroOpNextCycle, access.CompletedCycle);
                return true;
            }

            EnsureLineMicroOpOutputReady();
            var destination = _lineIndex == 0 ? _workDestinationD : _workSourceC;
            _destinationDLatch = CreateDestinationDmaLatch(_lineMicroOpOutput);
            if (!TryCommitDestinationDmaLatch(
                    destination,
                    ref _destinationDLatch,
                    requestCycle,
                    out var write))
            {
                return false;
            }

            _lineMicroOpNextCycle = Math.Max(_lineMicroOpNextCycle, write.CompletedCycle);
            _lineLastDrawnY = _lineY;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AccountLineMicroOpReadWait(long requestedCycle, long completedCycle)
        {
            var waitCycles = completedCycle - (requestedCycle + ChipSlotCycles);
            if (waitCycles <= 0)
            {
                return;
            }

            _lineMicroOpStepEnd += waitCycles;
            _lineMicroOpNextCycle += waitCycles;
        }

        private void EnsureLineMicroOpOutputReady()
        {
            if (_lineMicroOpOutputReady)
            {
                return;
            }

            var lineMask = RotateRight(_dataA, _lineBit);
            var textureBit = (_dataB & (0x8000 >> ((_shiftB + _lineIndex) & 0x0F))) != 0;
            var texture = textureBit ? (ushort)0xFFFF : (ushort)0;
            _lineMicroOpOutput = ExecuteLineFromSourceLatches(lineMask, texture, _lineMicroOpSourceC);
            if (_lineMicroOpOutput != 0)
            {
                _zeroFlag = false;
            }

            _lineMicroOpOutputReady = true;
        }

        private void FinishLineMicroOpPixel(long targetCycle)
        {
            _currentCycle = _lineMicroOpNextCycle;
            ClearLineMicroOpState();
            EndDmaRollbackSnapshot();
            if (_advanceProfilingEnabled)
            {
                _advanceWordsCompleted++;
            }

            _lineIndex++;
            _rowY = _lineIndex;
            if (_lineIndex >= _lineLength)
            {
                CompleteBlit(deferInterrupt: _currentCycle > targetCycle);
                return;
            }

            StepLineAddress();
        }

        private void ClearLineMicroOpState()
        {
            _lineMicroOpActive = false;
            _lineMicroOpIndex = 0;
            _lineMicroOpCount = 0;
            _lineMicroOpStepEnd = 0;
            _lineMicroOpNextReadCycle = 0;
            _lineMicroOpNextCycle = 0;
            _lineMicroOpSourceC = 0;
            _lineMicroOpOutput = 0;
            _lineMicroOpOutputReady = false;
            _lineMicroOpDraw = false;
        }

        private void StepAreaWordFromSlotQueue(long targetCycle)
        {
            switch (_areaSlotQueueKind)
            {
                case BlitterSlotQueueKind.WriteD:
                    StepAreaWordQueuedWriteD(targetCycle);
                    return;
                case BlitterSlotQueueKind.ReadAWriteD:
                    StepAreaWordQueuedReadAWriteD(targetCycle);
                    return;
                case BlitterSlotQueueKind.ReadAReadBWriteD:
                    StepAreaWordQueuedReadAReadBWriteD(targetCycle);
                    return;
            }

            BeginDmaRollbackSnapshot();
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetAreaWordCycles();
            var nextReadCycle = stepStart;
            var nextCycle = stepEnd;
            var isFinalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            var mask = GetCurrentAreaWordMask();
            var rawA = _activeDataA;
            var rawB = _activeDataB;
            var rawC = _activeDataC;
            var output = (ushort)0;
            var outputReady = false;
            var internalCompletionCycle = stepEnd;
            for (var index = 0; index < _areaSlotQueueOpCount; index++)
            {
                switch (_areaSlotQueueOps[index])
                {
                    case BlitterSlotQueueOp.ReadA:
                    {
                        var requestCycle = nextReadCycle;
                        _sourceALatch = LoadSourceDmaLatch(BlitterDmaSource.A, ref _workSourceA, _step, requestCycle);
                        var access = _sourceALatch.BusAccess;
                        if (!TryConsumeSourceDmaLatch(ref _sourceALatch, out rawA))
                        {
                            return;
                        }

                        var stall = access.GrantedCycle - requestCycle;
                        stepEnd += stall;
                        nextCycle += stall;
                        nextReadCycle = access.CompletedCycle;
                        nextCycle = Math.Max(nextCycle, access.CompletedCycle);
                        _slotQueueCommittedOps++;
                        break;
                    }

                    case BlitterSlotQueueOp.ReadB:
                    {
                        var requestCycle = nextReadCycle;
                        _sourceBLatch = LoadSourceDmaLatch(BlitterDmaSource.B, ref _workSourceB, _step, requestCycle);
                        var access = _sourceBLatch.BusAccess;
                        if (!TryConsumeSourceDmaLatch(ref _sourceBLatch, out rawB))
                        {
                            return;
                        }

                        var stall = access.GrantedCycle - requestCycle;
                        stepEnd += stall;
                        nextCycle += stall;
                        nextReadCycle = access.CompletedCycle;
                        nextCycle = Math.Max(nextCycle, access.CompletedCycle);
                        _slotQueueCommittedOps++;
                        break;
                    }

                    case BlitterSlotQueueOp.ReadC:
                    {
                        var requestCycle = nextReadCycle;
                        _sourceCLatch = LoadSourceDmaLatch(BlitterDmaSource.C, ref _workSourceC, _step, requestCycle);
                        var access = _sourceCLatch.BusAccess;
                        if (!TryConsumeSourceDmaLatch(ref _sourceCLatch, out rawC))
                        {
                            return;
                        }

                        var stall = access.GrantedCycle - requestCycle;
                        stepEnd += stall;
                        nextCycle += stall;
                        _activeDataC = rawC;
                        nextReadCycle = access.CompletedCycle;
                        nextCycle = Math.Max(nextCycle, access.CompletedCycle);
                        _slotQueueCommittedOps++;
                        break;
                    }

                    case BlitterSlotQueueOp.WriteD:
                    {
                        if (!outputReady)
                        {
                            output = ExecuteAreaFromSourceLatches(rawA, rawB, rawC, mask);
                            if (output != 0)
                            {
                                _zeroFlag = false;
                            }

                            outputReady = true;
                            internalCompletionCycle = Math.Max(stepEnd, nextReadCycle);
                        }

                        var writeCycle = Math.Max(nextReadCycle, stepEnd - ChipSlotCycles);
                        _destinationDLatch = CreateDestinationDmaLatch(output);
                        if (!TryCommitDestinationDmaLatch(ref _workDestinationD, _step, ref _destinationDLatch, writeCycle, out var write))
                        {
                            return;
                        }

                        nextCycle = Math.Max(nextCycle, write.CompletedCycle);
                        _slotQueueCommittedOps++;
                        break;
                    }
                }
            }

            if (!outputReady)
            {
                output = ExecuteAreaFromSourceLatches(rawA, rawB, rawC, mask);
                if (output != 0)
                {
                    _zeroFlag = false;
                }

                ExtendBOnlyInternalPhaseForRefresh(nextReadCycle, ref stepEnd, ref nextCycle);
                internalCompletionCycle = Math.Max(stepEnd, nextReadCycle);
            }

            _slotQueueWords++;
            _currentCycle = isFinalWord && _useD
                ? internalCompletionCycle
                : nextCycle;
            EndDmaRollbackSnapshot();
            AdvanceAreaPosition(targetCycle);
        }

        private void ExtendBOnlyInternalPhaseForRefresh(
            long dmaCompletionCycle,
            ref long stepEnd,
            ref long nextCycle)
        {
            if (_useA || !_useB || _useC || _useD || dmaCompletionCycle >= stepEnd)
            {
                return;
            }

            var slotCycle = AgnusChipSlotScheduler.AlignToSlot(dmaCompletionCycle);
            while (slotCycle < stepEnd)
            {
                if (IsBlitterSequencerPausedSlot(slotCycle))
                {
                    stepEnd += ChipSlotCycles;
                    nextCycle += ChipSlotCycles;
                }

                slotCycle += ChipSlotCycles;
            }
        }

        private bool IsBlitterSequencerPausedSlot(long slotCycle)
        {
            if (_bus.IsMandatoryRefreshSlot(slotCycle))
            {
                return true;
            }

            return _bus.TryGetCommittedAgnusSlotOwner(slotCycle, out var owner) &&
                owner is AgnusChipSlotOwner.Copper or
                    AgnusChipSlotOwner.Paula or
                    AgnusChipSlotOwner.Disk or
                    AgnusChipSlotOwner.Sprite or
                    AgnusChipSlotOwner.Bitplane;
        }

        private void StepAreaWordQueuedWriteD(long targetCycle)
        {
            BeginDmaRollbackSnapshot();
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetAreaWordCycles();
            var isFinalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            var output = ExecuteAreaFromSourceLatches(_activeDataA, _activeDataB, _activeDataC, GetCurrentAreaWordMask());
            if (output != 0)
            {
                _zeroFlag = false;
            }

            _destinationDLatch = CreateDestinationDmaLatch(output);
            if (!TryCommitDestinationDmaLatch(ref _workDestinationD, _step, ref _destinationDLatch, stepEnd - ChipSlotCycles, out var write))
            {
                return;
            }

            _slotQueueWords++;
            _slotQueueCommittedOps++;
            _rowPipelineWords++;
            _dOnlyRowWords++;
            _currentCycle = isFinalWord
                ? stepEnd
                : Math.Max(stepEnd, write.CompletedCycle);
            EndDmaRollbackSnapshot();
            AdvanceAreaPosition(targetCycle);
        }

        private void StepAreaWordQueuedReadAWriteD(long targetCycle)
        {
            BeginDmaRollbackSnapshot();
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetAreaWordCycles();
            var nextCycle = stepEnd;
            var isFinalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            _sourceALatch = LoadSourceDmaLatch(BlitterDmaSource.A, ref _workSourceA, _step, stepStart);
            var sourceAAccess = _sourceALatch.BusAccess;
            if (!TryConsumeSourceDmaLatch(ref _sourceALatch, out var rawA))
            {
                return;
            }

            var nextReadCycle = sourceAAccess.CompletedCycle;
            nextCycle = Math.Max(nextCycle, nextReadCycle);
            var output = ExecuteAreaFromSourceLatches(rawA, _activeDataB, _activeDataC, GetCurrentAreaWordMask());
            if (output != 0)
            {
                _zeroFlag = false;
            }

            var internalCompletionCycle = Math.Max(stepEnd, nextReadCycle);
            _destinationDLatch = CreateDestinationDmaLatch(output);
            if (!TryCommitDestinationDmaLatch(ref _workDestinationD, _step, ref _destinationDLatch, Math.Max(nextReadCycle, stepEnd - ChipSlotCycles), out var write))
            {
                return;
            }

            nextCycle = Math.Max(nextCycle, write.CompletedCycle);
            _slotQueueWords++;
            _slotQueueCommittedOps += 2;
            _rowPipelineWords++;
            _aToDRowWords++;
            _currentCycle = isFinalWord
                ? internalCompletionCycle
                : nextCycle;
            EndDmaRollbackSnapshot();
            AdvanceAreaPosition(targetCycle);
        }

        private void StepAreaWordQueuedReadAReadBWriteD(long targetCycle)
        {
            BeginDmaRollbackSnapshot();
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetAreaWordCycles();
            var nextCycle = stepEnd;
            var isFinalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            _sourceALatch = LoadSourceDmaLatch(BlitterDmaSource.A, ref _workSourceA, _step, stepStart);
            var sourceAAccess = _sourceALatch.BusAccess;
            if (!TryConsumeSourceDmaLatch(ref _sourceALatch, out var rawA))
            {
                return;
            }

            var nextReadCycle = sourceAAccess.CompletedCycle;
            nextCycle = Math.Max(nextCycle, nextReadCycle);

            _sourceBLatch = LoadSourceDmaLatch(BlitterDmaSource.B, ref _workSourceB, _step, nextReadCycle);
            var sourceBAccess = _sourceBLatch.BusAccess;
            if (!TryConsumeSourceDmaLatch(ref _sourceBLatch, out var rawB))
            {
                return;
            }

            nextReadCycle = sourceBAccess.CompletedCycle;
            nextCycle = Math.Max(nextCycle, nextReadCycle);

            var output = ExecuteAreaFromSourceLatches(rawA, rawB, _activeDataC, GetCurrentAreaWordMask());
            if (output != 0)
            {
                _zeroFlag = false;
            }

            var internalCompletionCycle = Math.Max(stepEnd, nextReadCycle);
            _destinationDLatch = CreateDestinationDmaLatch(output);
            if (!TryCommitDestinationDmaLatch(ref _workDestinationD, _step, ref _destinationDLatch, Math.Max(nextReadCycle, stepEnd - ChipSlotCycles), out var write))
            {
                return;
            }

            nextCycle = Math.Max(nextCycle, write.CompletedCycle);
            _slotQueueWords++;
            _slotQueueCommittedOps += 3;
            _currentCycle = isFinalWord
                ? internalCompletionCycle
                : nextCycle;
            EndDmaRollbackSnapshot();
            AdvanceAreaPosition(targetCycle);
        }

        private ushort GetCurrentAreaWordMask()
        {
            var mask = 0xFFFF;
            if (_wordX == 0)
            {
                mask &= _activeFirstWordMask;
            }

            if (_wordX == _widthWords - 1)
            {
                mask &= _activeLastWordMask;
            }

            return (ushort)mask;
        }

        private void AdvanceAreaPosition(long targetCycle)
        {
            if (_advanceProfilingEnabled)
            {
                _advanceWordsCompleted++;
            }

            _wordX++;
            if (_wordX < _widthWords)
            {
                return;
            }

            _wordX = 0;
            _rowY++;
            if (_rowY >= _height)
            {
                if (IsBOnlyAreaBlit())
                {
                    _currentCycle += BOnlyFinalPipelineDrainSlots * ChipSlotCycles;
                }

                CompleteBlit(deferInterrupt: _currentCycle > targetCycle);
                return;
            }

            if (_useA)
            {
                _workSourceA = AddModulo(_workSourceA, _activeSourceAModulo, _descending);
            }

            if (_useB)
            {
                _workSourceB = AddModulo(_workSourceB, _activeSourceBModulo, _descending);
            }

            if (_useC)
            {
                _workSourceC = AddModulo(_workSourceC, _activeSourceCModulo, _descending);
            }

            if (_useD)
            {
                _workDestinationD = AddModulo(_workDestinationD, _activeDestinationDModulo, _descending);
            }

            _fillCarry = _fillCarryInitial;
            _areaKernelState.FillCarry = _fillCarry;
        }

        private void StepLinePixel(long targetCycle)
        {
            BeginDmaRollbackSnapshot();
            var stepStart = _currentCycle;
            var stepEnd = stepStart + GetLinePixelCycles();
            var nextCycle = stepEnd;
            if (_useC && (!_lineSingleDot || _lineY != _lineLastDrawnY))
            {
                var nextReadCycle = _useB ? stepStart : stepStart + ChipSlotCycles;
                if (_useB)
                {
                    _sourceBLatch = LoadLineBPatternLatch(nextReadCycle);
                    var firstBAccess = _sourceBLatch.BusAccess;
                    if (!TryConsumeSourceDmaLatch(ref _sourceBLatch, out _))
                    {
                        return;
                    }

                    nextReadCycle = firstBAccess.CompletedCycle;
                    nextCycle = Math.Max(nextCycle, firstBAccess.CompletedCycle);
                    _sourceBLatch = LoadLineBPatternLatch(nextReadCycle);
                    var secondBAccess = _sourceBLatch.BusAccess;
                    if (!TryConsumeSourceDmaLatch(ref _sourceBLatch, out _dataB))
                    {
                        return;
                    }

                    nextReadCycle = secondBAccess.CompletedCycle;
                    nextCycle = Math.Max(nextCycle, secondBAccess.CompletedCycle);
                    _workSourceB = _bus.AddChipDmaPointerOffset(_workSourceB, _lineBPatternStride);
                }

                _sourceCLatch = LoadSourceDmaLatch(BlitterDmaSource.C, _workSourceC, nextReadCycle);
                var sourceCAccess = _sourceCLatch.BusAccess;
                if (!TryConsumeSourceDmaLatch(ref _sourceCLatch, out var sourceC))
                {
                    return;
                }

                nextCycle = Math.Max(nextCycle, sourceCAccess.CompletedCycle);
                var lineMask = RotateRight(_dataA, _lineBit);
                var textureBit = (_dataB & (0x8000 >> ((_shiftB + _lineIndex) & 0x0F))) != 0;
                var texture = textureBit ? (ushort)0xFFFF : (ushort)0;
                var output = ExecuteLineFromSourceLatches(lineMask, texture, sourceC);

                if (output != 0)
                {
                    _zeroFlag = false;
                }

                var destination = _lineIndex == 0 ? _workDestinationD : _workSourceC;
                _destinationDLatch = CreateDestinationDmaLatch(output);
                if (!TryCommitDestinationDmaLatch(
                    destination,
                    ref _destinationDLatch,
                    Math.Max(sourceCAccess.CompletedCycle, stepEnd - ChipSlotCycles),
                    out var write))
                {
                    return;
                }

                nextCycle = Math.Max(nextCycle, write.CompletedCycle);
                _lineLastDrawnY = _lineY;
            }

            _currentCycle = nextCycle;
            EndDmaRollbackSnapshot();
            _lineIndex++;
            _rowY = _lineIndex;
            if (_lineIndex >= _lineLength)
            {
                CompleteBlit(deferInterrupt: _currentCycle > targetCycle);
                return;
            }

            StepLineAddress();
        }

        private void StepLineAddress()
        {
            if (_lineSign)
            {
                _lineError = unchecked(_lineError + _sourceBModulo);
            }
            else
            {
                _lineError = unchecked(_lineError + _sourceAModulo);
                MoveLineMinorAxis();
            }

            MoveLineMajorAxis();
            _lineSign = _lineError < 0;
        }

        private void MoveLineMajorAxis()
        {
            if (_lineSud)
            {
                MoveLineX(_lineAul ? -1 : 1);
            }
            else
            {
                MoveLineY(_lineAul ? -1 : 1);
            }
        }

        private void MoveLineMinorAxis()
        {
            if (_lineSud)
            {
                MoveLineY(_lineSul ? -1 : 1);
            }
            else
            {
                MoveLineX(_lineSul ? -1 : 1);
            }
        }

        private void MoveLineX(int direction)
        {
            if (direction >= 0)
            {
                _lineBit++;
                if (_lineBit <= 15)
                {
                    return;
                }

                _lineBit = 0;
                _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, 2);
                _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, 2);
                return;
            }

            _lineBit--;
            if (_lineBit >= 0)
            {
                return;
            }

            _lineBit = 15;
            _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, -2);
            _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, -2);
        }

        private void MoveLineY(int direction)
        {
            _workSourceC = _bus.AddChipDmaPointerOffset(_workSourceC, direction >= 0 ? _lineSourceRowStride : -_lineSourceRowStride);
            _workDestinationD = _bus.AddChipDmaPointerOffset(_workDestinationD, direction >= 0 ? _lineDestinationRowStride : -_lineDestinationRowStride);
            _lineY += direction;
        }

        private void CompleteBlit(bool deferInterrupt = false)
        {
            if (IsActiveRowPipeline())
            {
                _rowPipelineCompletions++;
            }

            if (!_lineMode)
            {
                if (_useA)
                {
                    _sourceA = _workSourceA;
                    _dataA = _previousA;
                }

                if (_useB)
                {
                    _sourceB = _workSourceB;
                    _dataB = _previousB;
                }

                if (_useC)
                {
                    _sourceC = _workSourceC;
                    _dataC = _activeDataC;
                }

                if (_useD)
                {
                    _destinationD = _workDestinationD;
                }
            }
            else
            {
                if (_useA)
                {
                    _sourceA = (uint)(ushort)_lineError;
                }

                if (_useB)
                {
                    _sourceB = _workSourceB;
                }

                _sourceC = _workSourceC;
                _destinationD = _workSourceC;
            }

            if (deferInterrupt)
            {
                _completionPending = true;
                _busy = true;
                return;
            }

            _completionPending = false;
            FinishCompletedBlit();
        }

        private void FinalizePendingCompletion()
        {
            _completionPending = false;
            FinishCompletedBlit();
        }

        private void FinishCompletedBlit()
        {
            _lastCompletionCycle = _currentCycle;
            if (_bus.BusAccessCaptureEnabled)
            {
                _completionCycles.Add(_currentCycle);
            }
            _busy = false;
            _bus.RequestHardwareInterrupt(AmigaConstants.IntreqBlitter, _currentCycle);
            ApplyDeferredRegisterWrites();
            if (!_deferredRestartPending)
            {
                return;
            }

            var widthWords = _deferredRestartWidthWords;
            var height = _deferredRestartHeight;
            _deferredRestartPending = false;
            _deferredRestartWidthWords = 0;
            _deferredRestartHeight = 0;
            StartBlit(widthWords, height, _currentCycle);
        }

        private bool IsBOnlyAreaBlit()
            => !_lineMode && !_useA && _useB && !_useC && !_useD;

        internal long LastCompletionCycle => _lastCompletionCycle;

        internal IReadOnlyList<long> CompletionCycles => _completionCycles;




        private bool IsActiveRowPipeline()
            => _areaSlotQueueEnabled &&
                (_areaSlotQueueKind == BlitterSlotQueueKind.WriteD ||
                    _areaSlotQueueKind == BlitterSlotQueueKind.ReadAWriteD);

        private void ApplyDeferredRegisterWrites()
        {
            if (_deferredRegisterWrites.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _deferredRegisterWrites.Count; i++)
            {
                var write = _deferredRegisterWrites[i];
                ApplyRegisterWrite(write.Offset, write.Value);
            }

            _deferredRegisterWrites.Clear();
        }

        private bool IsBlitterDmaEnabled()
        {
            return (_bus.Paula.Dmacon & (DmaMasterEnable | DmaBlitterEnable)) == (DmaMasterEnable | DmaBlitterEnable);
        }

        private bool RequiresDmaForCurrentBlit()
        {
            return _lineMode
                ? _useC
                : _useA || _useB || _useC || _useD;
        }

        private bool ShouldStallCpu()
        {
            return _busy && RequiresDmaForCurrentBlit() && IsBlitterDmaEnabled() && (_bus.Paula.Dmacon & DmaBlitterNasty) != 0;
        }

        private int CalculateAreaWordCycles()
        {
            var ticks = 4;
            if (_useB)
            {
                ticks += 2;
            }

            if (_useC)
            {
                ticks += 2;
            }

            if (_fillEnabled && !_useC)
            {
                ticks += 2;
            }

            return ticks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetAreaWordCycles() => _areaWordCycles;

        private long GetAreaFillIdlePhaseDelay(long stepStart, long stepEnd)
        {
            if (!_fillEnabled || _useC)
            {
                return 0;
            }

            var delay = 0L;
            var idleCycle = stepEnd - (2 * ChipSlotCycles);
            while (_bus.IsFixedDmaSlotReservedOrPredicted(idleCycle + delay))
            {
                delay += ChipSlotCycles;
            }

            return delay;
        }

        private static int GetLinePixelCycles()
        {
            return 8;
        }

        private long GetCurrentStepEndCycle()
        {
            return _currentCycle + (_lineMode ? GetLinePixelCycles() : GetAreaWordCycles());
        }

        private void RecordBlitterDma(AmigaBusAccessResult access)
        {
            _lastDmaCycle = access.GrantedCycle;
            _completedMicroOps++;
            if (_advanceProfilingEnabled)
            {
                _advanceMicroOpsCompleted++;
            }
        }

        private void BeginDmaRollbackSnapshot()
        {
            _dmaRollbackSnapshot = new BlitterDmaRollbackSnapshot(
                _currentCycle,
                _workSourceA,
                _workSourceB,
                _workSourceC,
                _workDestinationD,
                _previousA,
                _previousB,
                _fillCarry,
                _areaKernelState,
                _activeDataC,
                _zeroFlag,
                _dataB,
                _lineLastDrawnY);
            _dmaRollbackSnapshotActive = true;
        }

        private void EndDmaRollbackSnapshot()
        {
            _dmaRollbackSnapshotActive = false;
        }

        private void RollbackDmaStepToDeniedCycle(long deniedCycle)
        {
            if (_cpuWaitExactSlotCycle >= 0 && (_areaMicroOpActive || _lineMicroOpActive))
            {
                _sourceALatch = default;
                _sourceBLatch = default;
                _sourceCLatch = default;
                _destinationDLatch = default;
                if (_advanceProfilingEnabled)
                {
                    _advanceDeniedSlots++;
                }

                return;
            }

            if (_dmaRollbackSnapshotActive)
            {
                _workSourceA = _dmaRollbackSnapshot.WorkSourceA;
                _workSourceB = _dmaRollbackSnapshot.WorkSourceB;
                _workSourceC = _dmaRollbackSnapshot.WorkSourceC;
                _workDestinationD = _dmaRollbackSnapshot.WorkDestinationD;
                _previousA = _dmaRollbackSnapshot.PreviousA;
                _previousB = _dmaRollbackSnapshot.PreviousB;
                _fillCarry = _dmaRollbackSnapshot.FillCarry;
                _areaKernelState = _dmaRollbackSnapshot.AreaKernelState;
                _activeDataC = _dmaRollbackSnapshot.ActiveDataC;
                _zeroFlag = _dmaRollbackSnapshot.ZeroFlag;
                _dataB = _dmaRollbackSnapshot.DataB;
                _lineLastDrawnY = _dmaRollbackSnapshot.LineLastDrawnY;
                _sourceALatch = default;
                _sourceBLatch = default;
                _sourceCLatch = default;
                _destinationDLatch = default;
                ClearAreaMicroOpState();
                _dmaRollbackSnapshotActive = false;
            }

            _currentCycle = Math.Max(_currentCycle, deniedCycle);
        }

        private ushort ExecuteAreaFromSourceLatches(ushort rawA, ushort rawB, ushort rawC, ushort mask)
        {
            if (_specializationEnabled && _activeKernel.SupportsArea)
            {
                var output = _activeKernel.ExecuteArea(ref _areaKernelState, rawA, rawB, rawC, mask);
                _previousA = _areaKernelState.PreviousA;
                _previousB = _areaKernelState.PreviousB;
                _fillCarry = _areaKernelState.FillCarry;
                return output;
            }

            if (_specializationEnabled)
            {
                _kernelCache.RecordFallback();
            }

            return ExecuteAreaScalar(rawA, rawB, rawC, mask);
        }

        private ushort ExecuteLineFromSourceLatches(ushort lineMask, ushort texture, ushort sourceC)
        {
            if (_specializationEnabled && _activeKernel.SupportsLine)
            {
                return _activeKernel.ExecuteLine(lineMask, texture, sourceC);
            }

            if (_specializationEnabled)
            {
                _kernelCache.RecordFallback();
            }

            return ApplyMinterm(_minterm, lineMask, texture, sourceC);
        }

        private ushort ExecuteAreaScalar(ushort rawA, ushort rawB, ushort rawC, ushort mask)
        {
            rawA = (ushort)(rawA & mask);
            var a = ShiftSource(rawA, ref _previousA, GetActiveAreaSourceAShift(), _descending);
            var b = ShiftSource(rawB, ref _previousB, _useB ? _shiftB : _activeDataBShift, _descending);
            var output = ApplyMinterm(_minterm, a, b, rawC);
            if (_fillEnabled)
            {
                output = ApplyFill(output);
            }

            _areaKernelState.PreviousA = _previousA;
            _areaKernelState.PreviousB = _previousB;
            _areaKernelState.FillCarry = _fillCarry;
            return output;
        }

        // An all-ones immediate A mask is invariant across the data latch; keep
        // first/last masks on the active ASH instead of a stale BLTADAT shift.
        private int GetActiveAreaSourceAShift()
            => _useA || _activeDataA == 0xFFFF
                ? _shiftA
                : _activeDataAShift;

        private ushort ApplyFill(ushort value)
        {
            var fillCarry = _fillCarry;
            var output = BlitterKernelMath.ApplyFill(value, _fillExclusive, ref fillCarry);
            _fillCarry = fillCarry;
            return output;
        }

        private BlitterDmaReadLatch LoadSourceDmaLatch(BlitterDmaSource source, ref uint pointer, int step, long cycle)
        {
            var latch = LoadSourceDmaLatch(source, GetEffectiveBlitterAddress(pointer), cycle);
            if (latch.Granted)
            {
                pointer = _bus.AddChipDmaPointerOffset(pointer, step);
            }

            return latch;
        }

        private BlitterDmaReadLatch LoadLineBPatternLatch(long cycle)
            => LoadSourceDmaLatch(BlitterDmaSource.B, _workSourceB, cycle);

        private BlitterDmaReadLatch LoadSourceDmaLatch(BlitterDmaSource source, uint address, long cycle)
        {
            var effectiveAddress = GetEffectiveBlitterAddress(address);
            var predictedGrant = 0L;
            var firstBlockedCycle = -1L;
            var firstBlockedOwner = CpuWaitFixedSlotOwner.Free;
            var verifySupported = _advanceMode == BlitterAdvanceMode.Verify &&
                _cpuWaitExactSlotCycle < 0 &&
                _bus.TryPredictBlitterFixedSlotGrant(
                    effectiveAddress,
                    cycle,
                    isWrite: false,
                    out predictedGrant,
                    out firstBlockedCycle,
                    out firstBlockedOwner);
            var execution = _cpuWaitExactSlotCycle >= 0
                ? ExecuteSourceDmaLatchAtCpuWaitSlot(effectiveAddress, cycle)
                : _bus.CausalBusExecutor.ExecuteBlitterWord(
                    effectiveAddress,
                    cycle,
                    isWrite: false,
                    writeValue: 0);
            RecordAdvanceGrantVerification(
                verifySupported,
                predictedGrant,
                execution.Access.GrantedCycle,
                effectiveAddress,
                isWrite: false,
                cycle,
                firstBlockedCycle,
                firstBlockedOwner);
            if (_specializationEnabled)
            {
                _specializedReservations++;
            }

            if (execution.Granted)
            {
                RecordBlitterDma(execution.Access);
            }

            return new BlitterDmaReadLatch(source, execution.Granted, execution.Access, execution.Value);
        }

        private AmigaDmaWordExecutionResult ExecuteSourceDmaLatchAtCpuWaitSlot(uint effectiveAddress, long cycle)
        {
            _ = _bus.CausalBusExecutor.TryExecuteBlitterWordExact(
                effectiveAddress,
                cycle,
                _cpuWaitExactSlotCycle,
                isWrite: false,
                writeValue: 0,
                displayPrepared: _orderedSlotDisplayPrepared,
                out var execution);
            return execution;
        }

        private bool TryConsumeSourceDmaLatch(ref BlitterDmaReadLatch latch, out ushort value)
        {
            if (!latch.Granted)
            {
                RollbackDmaStepToDeniedCycle(latch.BusAccess.GrantedCycle);
                latch = default;
                value = 0;
                return false;
            }

            value = ConsumeSourceDmaLatch(ref latch);
            return true;
        }

        private ushort ConsumeSourceDmaLatch(ref BlitterDmaReadLatch latch)
        {
            var value = latch.Value;
            latch = default;
            return value;
        }

        private static BlitterDmaWriteLatch CreateDestinationDmaLatch(ushort value)
            => new BlitterDmaWriteLatch(value);

        private bool TryCommitDestinationDmaLatch(
            ref uint pointer,
            int step,
            ref BlitterDmaWriteLatch latch,
            long cycle,
            out AmigaBusAccessResult access)
        {
            var execution = CommitDestinationDmaLatch(ref pointer, step, ref latch, cycle);
            access = execution.Access;
            if (execution.Granted)
            {
                return true;
            }

            RollbackDmaStepToDeniedCycle(execution.GrantedCycle);
            return false;
        }

        private bool TryCommitDestinationDmaLatch(
            uint address,
            ref BlitterDmaWriteLatch latch,
            long cycle,
            out AmigaBusAccessResult access)
        {
            var execution = CommitDestinationDmaLatch(address, ref latch, cycle);
            access = execution.Access;
            if (execution.Granted)
            {
                return true;
            }

            RollbackDmaStepToDeniedCycle(execution.GrantedCycle);
            return false;
        }

        private AmigaDmaWordExecutionResult CommitDestinationDmaLatch(ref uint pointer, int step, ref BlitterDmaWriteLatch latch, long cycle)
        {
            var execution = CommitDestinationDmaLatch(GetEffectiveBlitterAddress(pointer), ref latch, cycle);
            if (execution.Granted)
            {
                pointer = _bus.AddChipDmaPointerOffset(pointer, step);
            }

            return execution;
        }

        private AmigaDmaWordExecutionResult CommitDestinationDmaLatch(uint address, ref BlitterDmaWriteLatch latch, long cycle)
        {
            var effectiveAddress = GetEffectiveBlitterAddress(address);
            var predictedGrant = 0L;
            var firstBlockedCycle = -1L;
            var firstBlockedOwner = CpuWaitFixedSlotOwner.Free;
            var verifySupported = _advanceMode == BlitterAdvanceMode.Verify &&
                _cpuWaitExactSlotCycle < 0 &&
                _bus.TryPredictBlitterFixedSlotGrant(
                    effectiveAddress,
                    cycle,
                    isWrite: true,
                    out predictedGrant,
                    out firstBlockedCycle,
                    out firstBlockedOwner);
            var execution = _cpuWaitExactSlotCycle >= 0
                ? ExecuteDestinationDmaLatchAtCpuWaitSlot(effectiveAddress, latch.Value, cycle)
                : _bus.CausalBusExecutor.ExecuteBlitterWord(effectiveAddress, cycle, isWrite: true, latch.Value);
            RecordAdvanceGrantVerification(
                verifySupported,
                predictedGrant,
                execution.Access.GrantedCycle,
                effectiveAddress,
                isWrite: true,
                cycle,
                firstBlockedCycle,
                firstBlockedOwner);
            if (_specializationEnabled)
            {
                _specializedReservations++;
            }

            if (!execution.Granted)
            {
                latch = default;
                return execution;
            }

            RecordBlitterDma(execution.Access);
            latch = default;
            return execution;
        }

        private void RecordAdvanceGrantVerification(
            bool supported,
            long predictedGrant,
            long actualGrant,
            uint address,
            bool isWrite,
            long requestedCycle,
            long firstBlockedCycle,
            CpuWaitFixedSlotOwner firstBlockedOwner)
        {
            if (_advanceMode != BlitterAdvanceMode.Verify || !supported)
            {
                return;
            }

            if (predictedGrant == actualGrant)
            {
                _advanceVerifyMatches++;
                return;
            }

            _advanceVerifyMismatches++;
            if (_advanceFirstMismatch.Length == 0)
            {
                _advanceFirstMismatch =
                    $"req={requestedCycle},addr=0x{address:X8},write={isWrite},pred={predictedGrant},actual={actualGrant}," +
                    $"firstBlocked={firstBlockedCycle}/{firstBlockedOwner}," +
                    _bus.DescribeBlitterFixedSlotPrediction(actualGrant);
            }
        }

        private AmigaDmaWordExecutionResult ExecuteDestinationDmaLatchAtCpuWaitSlot(
            uint effectiveAddress,
            ushort value,
            long cycle)
        {
            _ = _bus.CausalBusExecutor.TryExecuteBlitterWordExact(
                effectiveAddress,
                cycle,
                _cpuWaitExactSlotCycle,
                isWrite: true,
                writeValue: value,
                displayPrepared: _orderedSlotDisplayPrepared,
                out var execution);
            return execution;
        }

        private bool TryPrepareBoundedDmaSlot(
            uint address,
            long requestedCycle,
            bool isWrite,
            ref BlitterDmaAdvanceScope scope,
            out long grantedCycle)
        {
            if (!_bus.TryPredictBlitterFixedSlotGrant(
                    address,
                    requestedCycle,
                    isWrite,
                    out grantedCycle,
                    out _,
                    out _))
            {
                scope.Barrier = true;
                return false;
            }

            var firstExaminedSlot = Math.Max(
                scope.CurrentSlot,
                AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestedCycle)));
            if (grantedCycle >= firstExaminedSlot)
            {
                scope.SlotsExamined +=
                    ((grantedCycle - firstExaminedSlot) / ChipSlotCycles) + 1;
            }

            var liveResult = _bus.AdvanceOrderedDmaBeforeBlitterSlot(
                grantedCycle,
                out var bitplaneFetches,
                out var spriteFetches,
                out var advancedPaula,
                out var advancedDisk);
            if (advancedPaula)
            {
                scope.PaulaSlots++;
            }

            if (advancedDisk)
            {
                scope.DiskSlots++;
            }

            if (liveResult == OcsCpuWaitLiveSlotResult.CopperBarrier)
            {
                scope.Barrier = true;
                return false;
            }

            if (bitplaneFetches != 0 || spriteFetches != 0)
            {
                scope.DisplayPreparations++;
            }

            scope.CurrentSlot = grantedCycle + ChipSlotCycles;
            return true;
        }

        private ref struct BlitterDmaAdvanceScope
        {
            public BlitterDmaAdvanceScope(long requestCycle)
            {
                CurrentSlot = 0;
                SlotsExamined = 0;
                DisplayPreparations = 0;
                PaulaSlots = 0;
                DiskSlots = 0;
                Initialized = false;
                Barrier = false;
                Initialize(requestCycle);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Initialize(long requestCycle)
            {
                if (Initialized)
                {
                    return;
                }

                CurrentSlot = AgnusChipSlotScheduler.AlignToSlot(Math.Max(0, requestCycle));
                Initialized = true;
            }

            public long CurrentSlot;

            public long SlotsExamined;

            public long DisplayPreparations;

            public long PaulaSlots;

            public long DiskSlots;

            public bool Initialized;

            public bool Barrier;
        }

        private struct BlitterAreaWordGrantPlan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly long Get(int index)
                => index switch
                {
                    0 => Grant0,
                    1 => Grant1,
                    2 => Grant2,
                    _ => Grant3
                };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Set(int index, long cycle)
            {
                switch (index)
                {
                    case 0:
                        Grant0 = cycle;
                        break;
                    case 1:
                        Grant1 = cycle;
                        break;
                    case 2:
                        Grant2 = cycle;
                        break;
                    default:
                        Grant3 = cycle;
                        break;
                }
            }

            private long Grant0;
            private long Grant1;
            private long Grant2;
            private long Grant3;
        }

        private uint AddModulo(uint pointer, short modulo, bool descending)
        {
            var evenModulo = modulo & ~1;
            return _bus.AddChipDmaPointerOffset(pointer, descending ? -evenModulo : evenModulo);
        }

        private uint GetEffectiveBlitterAddress(uint pointer)
        {
            return _bus.MaskChipDmaAddress(pointer);
        }

        private static int DecodeLegacyWidth(ushort value)
        {
            var width = value & 0x003F;
            return width == 0 ? LegacyMaximumWidthWords : width;
        }

        private static int DecodeLegacyHeight(ushort value)
        {
            var height = (value >> 6) & 0x03FF;
            return height == 0 ? LegacyMaximumHeight : height;
        }

        private static int DecodeEcsWidth(ushort value)
        {
            var width = value & 0x07FF;
            return width == 0 ? EcsMaximumWidthWords : width;
        }

        private static int DecodeEcsHeight(ushort value)
        {
            var height = value & 0x7FFF;
            return height == 0 ? EcsMaximumHeight : height;
        }

        private sealed class CpuWaitScratchState
        {
            private readonly AmigaBlitter _owner;
            private long _stepStart;
            private long _stepEnd;
            private long _nextReadCycle;
            private long _nextCycle;
            private long _internalCompletionCycle;
            private bool _wordActive;
            private int _opIndex;
            private bool _outputReady;
            private bool _finalWord;
            private uint _workSourceA;
            private uint _workSourceB;
            private uint _workSourceC;
            private uint _workDestinationD;
            private int _widthWords;
            private int _height;
            private int _wordX;
            private int _rowY;
            private bool _busy;
            private bool _deferredRestartPending;
            private int _deferredRestartWidthWords;
            private int _deferredRestartHeight;

            public CpuWaitScratchState(AmigaBlitter owner)
            {
                _owner = owner;
                CurrentCycle = owner._currentCycle;
                _workSourceA = owner._workSourceA;
                _workSourceB = owner._workSourceB;
                _workSourceC = owner._workSourceC;
                _workDestinationD = owner._workDestinationD;
                _widthWords = owner._widthWords;
                _height = owner._height;
                _wordX = owner._wordX;
                _rowY = owner._rowY;
                _busy = owner._busy;
                _deferredRestartPending = owner._deferredRestartPending;
                _deferredRestartWidthWords = owner._deferredRestartWidthWords;
                _deferredRestartHeight = owner._deferredRestartHeight;
                FirstDmaCycle = -1;
                LastDmaCycle = -1;
                if (owner._areaMicroOpActive)
                {
                    StartedFromPartial = true;
                    _wordActive = true;
                    _opIndex = owner._areaMicroOpIndex;
                    _stepStart = owner._areaMicroOpStepStart;
                    _stepEnd = owner._areaMicroOpStepEnd;
                    _nextReadCycle = owner._areaMicroOpNextReadCycle;
                    _nextCycle = owner._areaMicroOpNextCycle;
                    _internalCompletionCycle = owner._areaMicroOpInternalCompletionCycle;
                    _outputReady = owner._areaMicroOpOutputReady;
                    _finalWord = owner._areaMicroOpFinalWord;
                }
            }

            public long CurrentCycle { get; private set; }

            public int MicroOps { get; private set; }

            public bool StartedFromPartial { get; }

            public long FirstDmaCycle { get; private set; }

            public long LastDmaCycle { get; private set; }

            public bool TryAdvanceAtSlot(
                AgnusHrmSlotEngine slots,
                long slotCycle,
                out string unsupportedReason)
            {
                unsupportedReason = string.Empty;
                if (!_busy)
                {
                    return false;
                }

                if (!_wordActive)
                {
                    BeginWord();
                }

                if (_opIndex >= GetOpCount())
                {
                    if (_nextCycle > slotCycle)
                    {
                        return false;
                    }

                    FinishWord();
                    return true;
                }

                var op = GetOp(_opIndex);
                var requestCycle = GetRequestCycle(op);
                if (requestCycle > slotCycle)
                {
                    return false;
                }

                var address = GetAddress(op);
                var write = op == BlitterSlotQueueOp.WriteD;
                if (!slots.TryReserveBlitterDmaWordExactSlot(
                    address,
                    requestCycle,
                    slotCycle,
                    write,
                    out var access))
                {
                    return false;
                }

                CommitOp(op, access);
                RecordDma(access.GrantedCycle);
                _opIndex++;
                return true;
            }

            private void BeginWord()
            {
                _wordActive = true;
                _opIndex = 0;
                _stepStart = CurrentCycle;
                _stepEnd = _stepStart + _owner.GetAreaWordCycles();
                _nextReadCycle = _stepStart;
                _nextCycle = _stepEnd;
                _internalCompletionCycle = _stepEnd;
                _outputReady = false;
                _finalWord = _rowY == _height - 1 && _wordX == _widthWords - 1;
            }

            private int GetOpCount()
            {
                var count = 0;
                if (_owner._useA)
                {
                    count++;
                }

                if (_owner._useB)
                {
                    count++;
                }

                if (_owner._useC)
                {
                    count++;
                }

                if (_owner._useD)
                {
                    count++;
                }

                return count;
            }

            private BlitterSlotQueueOp GetOp(int index)
            {
                if (_owner._useA)
                {
                    if (index == 0)
                    {
                        return BlitterSlotQueueOp.ReadA;
                    }

                    index--;
                }

                if (_owner._useB)
                {
                    if (index == 0)
                    {
                        return BlitterSlotQueueOp.ReadB;
                    }

                    index--;
                }

                if (_owner._useC)
                {
                    if (index == 0)
                    {
                        return BlitterSlotQueueOp.ReadC;
                    }

                    index--;
                }

                return BlitterSlotQueueOp.WriteD;
            }

            private long GetRequestCycle(BlitterSlotQueueOp op)
                => op == BlitterSlotQueueOp.WriteD
                    ? Math.Max(_nextReadCycle, _stepEnd - ChipSlotCycles)
                    : _nextReadCycle;

            private uint GetAddress(BlitterSlotQueueOp op)
                => op switch
                {
                    BlitterSlotQueueOp.ReadA => _owner.GetEffectiveBlitterAddress(_workSourceA),
                    BlitterSlotQueueOp.ReadB => _owner.GetEffectiveBlitterAddress(_workSourceB),
                    BlitterSlotQueueOp.ReadC => _owner.GetEffectiveBlitterAddress(_workSourceC),
                    _ => _owner.GetEffectiveBlitterAddress(_workDestinationD)
                };

            private void CommitOp(BlitterSlotQueueOp op, AmigaBusAccessResult access)
            {
                switch (op)
                {
                    case BlitterSlotQueueOp.ReadA:
                        _workSourceA = _owner._bus.AddChipDmaPointerOffset(_workSourceA, _owner._step);
                        _nextReadCycle = access.CompletedCycle;
                        _nextCycle = Math.Max(_nextCycle, access.CompletedCycle);
                        break;
                    case BlitterSlotQueueOp.ReadB:
                        _workSourceB = _owner._bus.AddChipDmaPointerOffset(_workSourceB, _owner._step);
                        _nextReadCycle = access.CompletedCycle;
                        _nextCycle = Math.Max(_nextCycle, access.CompletedCycle);
                        break;
                    case BlitterSlotQueueOp.ReadC:
                        _workSourceC = _owner._bus.AddChipDmaPointerOffset(_workSourceC, _owner._step);
                        _nextReadCycle = access.CompletedCycle;
                        _nextCycle = Math.Max(_nextCycle, access.CompletedCycle);
                        break;
                    case BlitterSlotQueueOp.WriteD:
                        EnsureOutputReady();
                        _workDestinationD = _owner._bus.AddChipDmaPointerOffset(_workDestinationD, _owner._step);
                        _nextCycle = Math.Max(_nextCycle, access.CompletedCycle);
                        break;
                }
            }

            private void EnsureOutputReady()
            {
                if (_outputReady)
                {
                    return;
                }

                _outputReady = true;
                _internalCompletionCycle = Math.Max(_stepEnd, _nextReadCycle);
            }

            private void FinishWord()
            {
                _owner.ExtendBOnlyInternalPhaseForRefresh(
                    _nextReadCycle,
                    ref _stepEnd,
                    ref _nextCycle);
                if (!_outputReady)
                {
                    EnsureOutputReady();
                }

                CurrentCycle = _finalWord && _owner._useD
                    ? _internalCompletionCycle
                    : _nextCycle;
                _wordActive = false;
                _opIndex = 0;
                AdvancePosition();
            }

            private void AdvancePosition()
            {
                _wordX++;
                if (_wordX < _widthWords)
                {
                    return;
                }

                _wordX = 0;
                _rowY++;
                if (_rowY >= _height)
                {
                    if (!_deferredRestartPending)
                    {
                        _busy = false;
                        return;
                    }

                    _deferredRestartPending = false;
                    _widthWords = _deferredRestartWidthWords;
                    _height = _deferredRestartHeight;
                    _deferredRestartWidthWords = 0;
                    _deferredRestartHeight = 0;
                    _wordX = 0;
                    _rowY = 0;
                    CurrentCycle = _owner._bus.NextChipSlotCycle(CurrentCycle + ChipSlotCycles);
                    return;
                }

                if (_owner._useA)
                {
                    _workSourceA = _owner.AddModulo(_workSourceA, _owner._activeSourceAModulo, descending: false);
                }

                if (_owner._useB)
                {
                    _workSourceB = _owner.AddModulo(_workSourceB, _owner._activeSourceBModulo, descending: false);
                }

                if (_owner._useC)
                {
                    _workSourceC = _owner.AddModulo(_workSourceC, _owner._activeSourceCModulo, descending: false);
                }

                if (_owner._useD)
                {
                    _workDestinationD = _owner.AddModulo(_workDestinationD, _owner._activeDestinationDModulo, descending: false);
                }
            }

            private void RecordDma(long cycle)
            {
                if (FirstDmaCycle < 0)
                {
                    FirstDmaCycle = cycle;
                }

                LastDmaCycle = cycle;
                MicroOps++;
            }
        }

        private static ushort RotateRight(ushort value, int bits)
        {
            bits &= 0x0F;
            return bits == 0
                ? value
                : (ushort)((value >> bits) | (value << (16 - bits)));
        }

        private static ushort ShiftSource(ushort current, ref ushort previous, int shift, bool descending)
            => BlitterKernelMath.ShiftSource(current, ref previous, shift, descending);

        private static ushort ApplyMinterm(byte minterm, ushort sourceA, ushort sourceB, ushort sourceC)
            => BlitterKernelMath.ApplyMinterm(minterm, sourceA, sourceB, sourceC);

        private enum BlitterDmaSource
        {
            A,
            B,
            C
        }

        private enum BlitterSlotQueueOp : byte
        {
            ReadA,
            ReadB,
            ReadC,
            WriteD
        }

        private enum BlitterSlotQueueKind : byte
        {
            None,
            Generic,
            WriteD,
            ReadAWriteD,
            ReadAReadBWriteD
        }

        private readonly struct BlitterDmaReadLatch
        {
            public BlitterDmaReadLatch(
                BlitterDmaSource source,
                bool granted,
                AmigaBusAccessResult busAccess,
                ushort value)
            {
                Source = source;
                Granted = granted;
                BusAccess = busAccess;
                Value = value;
            }

            public BlitterDmaSource Source { get; }

            public bool Granted { get; }

            public AmigaBusAccessResult BusAccess { get; }

            public ushort Value { get; }
        }

        private readonly struct BlitterDmaWriteLatch
        {
            public BlitterDmaWriteLatch(ushort value)
            {
                Value = value;
            }

            public ushort Value { get; }
        }

        private readonly struct BlitterDmaRollbackSnapshot
        {
            public BlitterDmaRollbackSnapshot(
                long currentCycle,
                uint workSourceA,
                uint workSourceB,
                uint workSourceC,
                uint workDestinationD,
                ushort previousA,
                ushort previousB,
                bool fillCarry,
                BlitterAreaKernelState areaKernelState,
                ushort activeDataC,
                bool zeroFlag,
                ushort dataB,
                int lineLastDrawnY)
            {
                CurrentCycle = currentCycle;
                WorkSourceA = workSourceA;
                WorkSourceB = workSourceB;
                WorkSourceC = workSourceC;
                WorkDestinationD = workDestinationD;
                PreviousA = previousA;
                PreviousB = previousB;
                FillCarry = fillCarry;
                AreaKernelState = areaKernelState;
                ActiveDataC = activeDataC;
                ZeroFlag = zeroFlag;
                DataB = dataB;
                LineLastDrawnY = lineLastDrawnY;
            }

            public long CurrentCycle { get; }

            public uint WorkSourceA { get; }

            public uint WorkSourceB { get; }

            public uint WorkSourceC { get; }

            public uint WorkDestinationD { get; }

            public ushort PreviousA { get; }

            public ushort PreviousB { get; }

            public bool FillCarry { get; }

            public BlitterAreaKernelState AreaKernelState { get; }

            public ushort ActiveDataC { get; }

            public bool ZeroFlag { get; }

            public ushort DataB { get; }

            public int LineLastDrawnY { get; }
        }

        private readonly struct SchedulerWakeSignature : IEquatable<SchedulerWakeSignature>
        {
            public static readonly SchedulerWakeSignature Idle = new SchedulerWakeSignature(false, false, long.MaxValue);

            private SchedulerWakeSignature(bool busy, bool completionPending, long completionCycle)
            {
                Busy = busy;
                CompletionPending = completionPending;
                CompletionCycle = completionCycle;
            }

            public SchedulerWakeSignature(bool completionPending, long completionCycle)
                : this(true, completionPending, completionCycle)
            {
            }

            public bool Busy { get; }

            public bool CompletionPending { get; }

            public long CompletionCycle { get; }

            public bool Equals(SchedulerWakeSignature other)
                => Busy == other.Busy &&
                    CompletionPending == other.CompletionPending &&
                    CompletionCycle == other.CompletionCycle;
        }

        private readonly struct DeferredRegisterWrite
        {
            public DeferredRegisterWrite(ushort offset, ushort value)
            {
                Offset = offset;
                Value = value;
            }

            public ushort Offset { get; }

            public ushort Value { get; }
        }
    }
}
