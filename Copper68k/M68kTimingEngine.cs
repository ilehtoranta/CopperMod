/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal enum M68kAcceleratorModel
    {
        M68020,
        M68030,
        M68040
    }

    [Flags]
    internal enum M68kTimingBarrier
    {
        None = 0,
        FlushPipeline = 1 << 0,
        SynchronizeBus = 1 << 1,
        Exception = 1 << 2,
        Branch = 1 << 3,
        CacheControl = 1 << 4,
        ReadModifyWrite = 1 << 5
    }

    internal enum M68kInstructionTimingKey
    {
        Idle,
        Nop,
        LineAException,
        LineFException,
        IllegalInstruction,
        PrivilegeViolation,
        FormatError,
        InterruptAcknowledge,
        Movec,
        ImmediateWordToConditionCodeRegister,
        ImmediateWordToStatusRegister,
        Rte,
        Rtd,
        Rts,
        LinkLong,
        ExtbLong,
        ExtWordData,
        TstWordData,
        Moveq,
        NegxLongData,
        NegLongData,
        NotByteData,
        ClrDataLong,
        ClrDataWord,
        ClrLongAddressIndirect,
        ClrLongAddressDisplacement,
        ClrLongAbsoluteLong,
        ClrByteAddressIndirect,
        ClrByteAddressDisplacement,
        ClrWordAddressDisplacement,
        ClrLongPostIncrement,
        LeaAbsoluteLong,
        LeaAddressDisplacement,
        MoveByteImmediateToAbsoluteLong,
        MoveWordImmediateToAbsoluteLong,
        MoveLongImmediateToAbsoluteLong,
        MoveLongImmediateToAddressIndirect,
        MoveLongImmediateToAddressDisplacement,
        MoveLongImmediateToPostIncrement,
        MoveLongImmediateToData,
        MoveLongImmediateToAddress,
        MoveLongDataToData,
        MoveLongDataToAddress,
        MoveLongDataToAddressIndirect,
        MoveLongDataToAddressDisplacement,
        MoveLongAddressToData,
        MoveLongAddressToAddress,
        MoveLongAddressToAddressIndirect,
        MoveLongAddressToAddressDisplacement,
        MoveLongAddressToPostIncrement,
        MoveLongAddressIndirectToData,
        MoveLongAddressIndirectToAddress,
        MoveLongPostIncrementToData,
        MoveLongPostIncrementToAddress,
        MoveLongAddressDisplacementToData,
        MoveLongAddressDisplacementToAddress,
        MoveLongAddressDisplacementToPostIncrement,
        MoveLongBriefIndexedToData,
        MoveLongBriefIndexedToAddress,
        MoveLongAddressIndirectToAddressIndirect,
        MoveLongAbsoluteLongToData,
        MoveLongAbsoluteWordToAddressDisplacement,
        MoveLongAbsoluteLongToAddressDisplacement,
        MoveLongDataToAbsoluteLong,
        MoveLongAddressToAbsoluteLong,
        MoveByteDataToData,
        MoveByteImmediateToData,
        MoveByteImmediateToAddressIndirect,
        MoveByteImmediateToAddressDisplacement,
        MoveByteImmediateToBriefIndexed,
        MoveByteAddressIndirectToData,
        MoveBytePostIncrementToData,
        MoveByteAddressDisplacementToData,
        MoveByteAbsoluteLongToData,
        MoveByteBriefIndexedToData,
        MoveByteDataToAbsoluteLong,
        MoveByteDataToAddressIndirect,
        MoveByteDataToAddressDisplacement,
        MoveByteDataToBriefIndexed,
        MoveByteDataToPostIncrement,
        MoveByteDataToPredecrement,
        MoveByteBriefIndexedToPredecrement,
        MoveBytePostIncrementToPostIncrement,
        MoveByteAddressIndirectToAbsoluteLong,
        MoveByteAbsoluteLongToAbsoluteLong,
        MoveWordAbsoluteLongToData,
        MoveWordAddressDisplacementToData,
        MoveWordImmediateToData,
        MoveWordImmediateToAddressDisplacement,
        MoveWordDataToAddressDisplacement,
        MoveWordDataToAbsoluteLong,
        MoveWordAbsoluteLongToAbsoluteLong,
        MoveWordAbsoluteLongToAddressDisplacement,
        ImmediateLogicalByteToAbsoluteLong,
        AddiByteImmediateToData,
        AddiByteImmediateToAddressIndirect,
        AddiByteImmediateToAddressDisplacement,
        AddiWordImmediateToData,
        AddiLongImmediateToData,
        AddiLongImmediateToAbsoluteLong,
        SubiByteImmediateToData,
        SubiByteImmediateToAddressDisplacement,
        SubiLongImmediateToData,
        SubByteDataToData,
        SubLongDataToData,
        SubLongAddressToData,
        SubLongAddressDisplacementToData,
        AddWordDataToData,
        AddLongDataToData,
        AddxLongDataToData,
        AddLongPostIncrementToData,
        AddWordDataToAddressDisplacement,
        AddLongDataToAddressDisplacement,
        AddaLongImmediateToAddress,
        AddaLongDataToAddress,
        AddaLongAddressDisplacementToAddress,
        SubaLongImmediateToAddress,
        DivuWordEffectiveAddressToData,
        DivsWordEffectiveAddressToData,
        DivuWordImmediateToData,
        DivsWordImmediateToData,
        MuluWordEffectiveAddressToData,
        MulsWordEffectiveAddressToData,
        MuluLong,
        MulsLong,
        DivuLong,
        DivsLong,
        AbcdByteDataToData,
        AbcdBytePredecrementMemory,
        SbcdByteDataToData,
        SbcdBytePredecrementMemory,
        NbcdByteData,
        NbcdByteAddressIndirect,
        NbcdBytePostIncrement,
        NbcdBytePredecrement,
        NbcdByteAddressDisplacement,
        NbcdByteBriefIndexed,
        NbcdByteAbsoluteWord,
        NbcdByteAbsoluteLong,
        AndByteDataToData,
        AndWordImmediateToData,
        AndLongImmediateToData,
        MuluWordImmediateToData,
        EoriWordImmediateToData,
        EoriLongImmediateToData,
        CmpiLongImmediateToData,
        CmpiLongImmediateToPostIncrement,
        CmpiLongImmediateToAddressDisplacement,
        CmpiLongImmediateToAbsoluteLong,
        CmpiByteImmediateToData,
        CmpiByteImmediateToAddressIndirect,
        CmpiByteImmediateToAddressDisplacement,
        CmpiWordImmediateToData,
        CmpiWordImmediateToAddressDisplacement,
        CmpiWordImmediateToAbsoluteLong,
        CmpaLongImmediateToAddress,
        CmpaLongDataToAddress,
        CmpaLongAddressToAddress,
        CmpaLongAddressIndirectToAddress,
        CmpLongDataToData,
        CmpLongAddressToData,
        CmpLongAddressIndirectToData,
        CmpLongPostIncrementToData,
        CmpByteDataToData,
        CmpByteAddressIndirectToData,
        CmpByteAddressDisplacementToData,
        CmpByteAbsoluteLongToData,
        CmpWordDataToData,
        CmpWordAddressDisplacementToData,
        LeaAbsoluteWord,
        SwapData,
        AsrLongImmediateData,
        AsrWordImmediateData,
        LsrLongImmediateData,
        AslLongImmediateData,
        AslWordImmediateData,
        RorByteImmediateData,
        RorWordImmediateData,
        RolWordImmediateData,
        JsrAbsoluteLong,
        JmpAddressIndirect,
        JmpAbsoluteLong,
        MovemLongRegistersToPredecrement,
        MovemLongPostIncrementToRegisters,
        OriByteImmediateToData,
        BtstByteImmediateAbsoluteLong,
        BchgByteImmediateAbsoluteLong,
        BclrByteImmediateAbsoluteLong,
        BsetByteImmediateAbsoluteLong,
        BsetByteImmediateAddressDisplacement,
        BtstImmediateData,
        BclrImmediateData,
        BsetImmediateData,
        BtstDynamicData,
        BclrDynamicData,
        SccAbsoluteLong,
        BranchByteTaken,
        BranchByteNotTaken,
        BsrByte,
        BranchWordTaken,
        BranchWordNotTaken,
        BsrWord,
        DbccConditionTrue,
        DbccBranchTaken,
        DbccExpired,
        BranchLongTaken,
        BranchLongNotTaken,
        BsrLong
    }

    internal readonly record struct M68kInstructionPlan(
        M68kInstructionTimingKey Key,
        string Name,
        int NativeCycles,
        int HeadCycles,
        int TailCycles,
        bool UsesHeadTail,
        M68kTimingBarrier Barriers)
    {
        public static M68kInstructionPlan CreateFlat(
            M68kInstructionTimingKey key,
            string name,
            int nativeCycles,
            M68kTimingBarrier barriers = M68kTimingBarrier.None)
            => new(key, name, nativeCycles, 0, 0, UsesHeadTail: false, barriers);

        public static M68kInstructionPlan CreateHeadTail(
            M68kInstructionTimingKey key,
            string name,
            int cacheCaseCycles,
            int headCycles,
            int tailCycles,
            M68kTimingBarrier barriers = M68kTimingBarrier.None)
            => new(key, name, cacheCaseCycles, headCycles, tailCycles, UsesHeadTail: true, barriers);
    }

    internal readonly record struct M68kExecutedInstructionTiming(
        M68kInstructionPlan Plan,
        int OverlapCycles,
        long StartNativeCycle,
        long EndNativeCycle,
        long PendingBusNativeCycle)
    {
        public long ElapsedNativeCycles => EndNativeCycle - StartNativeCycle;
    }

    internal sealed class M68kPipelineState
    {
        public long InstructionBoundaryNativeCycle { get; set; }

        public long BusControllerAvailableNativeCycle { get; set; }

        public int PendingTailCycles { get; set; }

        public bool SuppressNextOverlap { get; set; }

        public void Reset()
        {
            InstructionBoundaryNativeCycle = 0;
            BusControllerAvailableNativeCycle = 0;
            PendingTailCycles = 0;
            SuppressNextOverlap = false;
        }
    }

    internal sealed class M68kInstructionCache
    {
        private readonly uint[] _tags;
        private readonly bool[] _valid;
        private readonly int _lineSize;

        public M68kInstructionCache(int byteSize = 256, int lineSize = 4)
        {
            if (byteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize));
            }

            if (lineSize <= 0 || (lineSize & (lineSize - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineSize));
            }

            _lineSize = lineSize;
            _tags = new uint[Math.Max(1, byteSize / lineSize)];
            _valid = new bool[_tags.Length];
        }

        public bool Enabled { get; private set; }

        public bool Frozen { get; private set; }

        public int LineSize => _lineSize;

        public void Reset()
        {
            Clear();
            Enabled = false;
            Frozen = false;
        }

        public void ApplyControl(bool enabled, bool frozen, bool clearAll, bool clearEntry, uint entryAddress)
        {
            Enabled = enabled;
            Frozen = frozen;
            if (clearAll)
            {
                Clear();
            }
            else if (clearEntry)
            {
                ClearEntry(entryAddress);
            }
        }

        public bool Probe(uint address)
        {
            if (!Enabled)
            {
                return false;
            }

            var lineAddress = address & ~((uint)_lineSize - 1u);
            var index = GetIndex(lineAddress);
            if (_valid[index] && _tags[index] == lineAddress)
            {
                return true;
            }

            if (!Frozen)
            {
                _valid[index] = true;
                _tags[index] = lineAddress;
            }

            return false;
        }

        public void Clear()
            => Array.Clear(_valid);

        public void ClearEntry(uint address)
        {
            var lineAddress = address & ~((uint)_lineSize - 1u);
            var index = GetIndex(lineAddress);
            if (_tags[index] == lineAddress)
            {
                _valid[index] = false;
            }
        }

        private int GetIndex(uint lineAddress)
            => (int)((lineAddress / (uint)_lineSize) % (uint)_tags.Length);
    }

    internal sealed class M68kTimingEngine
    {
        private readonly M68020CpuProfile _profile;
        private readonly M68kCpuState _state;
        private readonly M68kInstructionPlan _branchByteTakenPlan;
        private readonly int _nativeCyclesPerMachineCycle;
        private M68kExecutedInstructionTiming _lastInstructionTiming;
        private M68kInstructionTimingKey _lastFlatTimingKey;
        private int _lastFlatOverlapCycles;
        private long _lastFlatStartNativeCycle;
        private long _lastFlatEndNativeCycle;
        private long _lastFlatPendingBusNativeCycle;
        private bool _lastTimingIsFlat;

        public M68kTimingEngine(M68020CpuProfile profile, M68kCpuState state)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _branchByteTakenPlan = profile.FixedInstructionNativeCycles is int fixedCycles
                ? fixedCycles == 1
                    ? M68040FixedTimingModel.GetPlan(M68kInstructionTimingKey.BranchByteTaken)
                    : M68kTimingFormula.CreateFixedPlan(M68kInstructionTimingKey.BranchByteTaken, fixedCycles)
                : profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040
                    ? M68030TimingModel.GetPlan(M68kInstructionTimingKey.BranchByteTaken)
                    : M68020TimingModel.GetPlan(M68kInstructionTimingKey.BranchByteTaken);
            _nativeCyclesPerMachineCycle = profile.NativeCyclesPerMachineCycle;
            InstructionCache = new M68kInstructionCache();
            DataCache = profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040
                ? new M68kInstructionCache()
                : null;
        }

        public M68kPipelineState Pipeline { get; } = new M68kPipelineState();

        public M68kInstructionCache InstructionCache { get; }

        public M68kInstructionCache? DataCache { get; }

        public M68kExecutedInstructionTiming LastInstructionTiming
            => _lastTimingIsFlat
                ? new M68kExecutedInstructionTiming(
                    GetPlan(_lastFlatTimingKey),
                    _lastFlatOverlapCycles,
                    _lastFlatStartNativeCycle,
                    _lastFlatEndNativeCycle,
                    _lastFlatPendingBusNativeCycle)
                : _lastInstructionTiming;

        public long BusControllerAvailableNativeCycle => Pipeline.BusControllerAvailableNativeCycle;

        public void Reset()
        {
            Pipeline.Reset();
            InstructionCache.Reset();
            DataCache?.Reset();
            _lastInstructionTiming = default;
            _lastTimingIsFlat = false;
        }

        public M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
            => _profile.FixedInstructionNativeCycles is int fixedCycles
                ? fixedCycles == 1
                    ? M68040FixedTimingModel.GetPlan(key)
                    : M68kTimingFormula.CreateFixedPlan(key, fixedCycles)
                : _profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040
                ? M68030TimingModel.GetPlan(key)
                : M68020TimingModel.GetPlan(key);

        public bool ProbeInstructionCache(uint address)
            => _profile.IsInstructionCacheableAddress(address) &&
                InstructionCache.Probe(address);

        public bool ProbeDataCache(uint address)
            => DataCache?.Probe(address) == true;

        public void ApplyCacheControl(uint cacheControlRegister, uint cacheAddressRegister)
        {
            if (_profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040)
            {
                InstructionCache.ApplyControl(
                    enabled: (cacheControlRegister & 0x0000_0001) != 0,
                    frozen: (cacheControlRegister & 0x0000_0002) != 0,
                    clearAll: (cacheControlRegister & 0x0000_0008) != 0,
                    clearEntry: (cacheControlRegister & 0x0000_0004) != 0,
                    cacheAddressRegister);
                DataCache?.ApplyControl(
                    enabled: (cacheControlRegister & 0x0000_0100) != 0,
                    frozen: (cacheControlRegister & 0x0000_0200) != 0,
                    clearAll: (cacheControlRegister & 0x0000_0800) != 0,
                    clearEntry: (cacheControlRegister & 0x0000_0400) != 0,
                    cacheAddressRegister);
                return;
            }

            InstructionCache.ApplyControl(
                enabled: (cacheControlRegister & 0x0000_0001) != 0,
                frozen: (cacheControlRegister & 0x0000_0002) != 0,
                clearAll: (cacheControlRegister & 0x0000_0008) != 0,
                clearEntry: (cacheControlRegister & 0x0000_0004) != 0,
                cacheAddressRegister);
        }

        public void RecordPostedBusCompletion(long completedMachineCycle)
        {
            var nativeCompletion = _profile.MachineToNativeCycles(completedMachineCycle);
            if (Pipeline.BusControllerAvailableNativeCycle < nativeCompletion)
            {
                Pipeline.BusControllerAvailableNativeCycle = nativeCompletion;
            }
        }

        public void CompleteBlockingBusAccess(long completedMachineCycle)
        {
            RecordPostedBusCompletion(completedMachineCycle);
            SynchronizeNativeToBus();
        }

        public void SynchronizeNativeToMachine()
        {
            var nativeCycles = _profile.MachineToNativeCycles(_state.Cycles);
            if (_state.NativeCycles < nativeCycles)
            {
                _state.NativeCycles = nativeCycles;
            }
        }

        public void SynchronizeNativeToBus()
        {
            if (_state.NativeCycles < Pipeline.BusControllerAvailableNativeCycle)
            {
                _state.NativeCycles = Pipeline.BusControllerAvailableNativeCycle;
            }

            SynchronizeMachineToNative();
        }

        public M68kExecutedInstructionTiming CompleteInstruction(M68kInstructionPlan plan)
        {
            if (plan.NativeCycles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(plan), plan.NativeCycles, "Instruction cycles must be non-negative.");
            }

            var start = Math.Max(_state.NativeCycles, _profile.MachineToNativeCycles(_state.Cycles));
            _state.NativeCycles = start;

            if ((plan.Barriers & M68kTimingBarrier.SynchronizeBus) != 0)
            {
                SynchronizeNativeToBus();
                start = _state.NativeCycles;
            }

            var overlap = 0;
            if (plan.UsesHeadTail &&
                !Pipeline.SuppressNextOverlap &&
                (plan.Barriers & (M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Exception | M68kTimingBarrier.Branch)) == 0)
            {
                overlap = Math.Min(Pipeline.PendingTailCycles, Math.Max(0, plan.HeadCycles));
            }

            var elapsed = Math.Max(0, plan.NativeCycles - overlap);
            _state.NativeCycles += elapsed;

            if ((plan.Barriers & M68kTimingBarrier.SynchronizeBus) != 0)
            {
                SynchronizeNativeToBus();
            }

            SynchronizeMachineToNative();
            Pipeline.InstructionBoundaryNativeCycle = _state.NativeCycles;
            Pipeline.PendingTailCycles =
                (plan.Barriers & (M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Exception | M68kTimingBarrier.Branch)) == 0
                    ? Math.Max(0, plan.TailCycles)
                    : 0;
            Pipeline.SuppressNextOverlap =
                (plan.Barriers & (M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Exception | M68kTimingBarrier.Branch | M68kTimingBarrier.CacheControl | M68kTimingBarrier.ReadModifyWrite)) != 0;

            _lastInstructionTiming = new M68kExecutedInstructionTiming(
                plan,
                overlap,
                start,
                _state.NativeCycles,
                Pipeline.BusControllerAvailableNativeCycle);
            _lastTimingIsFlat = false;
            return _lastInstructionTiming;
        }

        internal void CompleteFlatInstruction(M68kInstructionPlan plan)
        {
            if (_profile.Model is not (
                    M68kAcceleratorModel.M68020 or
                    M68kAcceleratorModel.M68030 or
                    M68kAcceleratorModel.M68040) ||
                plan.Barriers != M68kTimingBarrier.None)
            {
                CompleteInstruction(plan);
                return;
            }

            var machineNativeCycles = _state.Cycles * _nativeCyclesPerMachineCycle;
            var start = _state.NativeCycles >= machineNativeCycles
                ? _state.NativeCycles
                : machineNativeCycles;
            var pipeline = Pipeline;
            var overlap = 0;
            if (plan.UsesHeadTail && !pipeline.SuppressNextOverlap)
            {
                overlap = Math.Min(pipeline.PendingTailCycles, Math.Max(0, plan.HeadCycles));
            }

            var elapsed = plan.NativeCycles - overlap;
            if (elapsed < 0)
            {
                elapsed = 0;
            }

            var end = start + elapsed;
            _state.NativeCycles = end;
            long machineCycles;
            if (_nativeCyclesPerMachineCycle == 2)
            {
                machineCycles = (end + 1) >> 1;
            }
            else if (_nativeCyclesPerMachineCycle == 4)
            {
                machineCycles = (end + 3) >> 2;
            }
            else
            {
                machineCycles = (end + _nativeCyclesPerMachineCycle - 1) /
                    _nativeCyclesPerMachineCycle;
            }

            if (_state.Cycles < machineCycles)
            {
                _state.Cycles = machineCycles;
            }

            pipeline.InstructionBoundaryNativeCycle = end;
            pipeline.PendingTailCycles = Math.Max(0, plan.TailCycles);
            pipeline.SuppressNextOverlap = false;
            SetLastFlatTiming(
                plan.Key,
                overlap,
                start,
                end,
                pipeline.BusControllerAvailableNativeCycle);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void CompleteHotBranchByteTaken()
        {
            var plan = _branchByteTakenPlan;
            var machineNativeCycles = _state.Cycles * _nativeCyclesPerMachineCycle;
            var start = _state.NativeCycles >= machineNativeCycles
                ? _state.NativeCycles
                : machineNativeCycles;
            var end = start + plan.NativeCycles;
            _state.NativeCycles = end;
            long machineCycles;
            if (_nativeCyclesPerMachineCycle == 2)
            {
                machineCycles = (end + 1) >> 1;
            }
            else if (_nativeCyclesPerMachineCycle == 4)
            {
                machineCycles = (end + 3) >> 2;
            }
            else
            {
                machineCycles = (end + _nativeCyclesPerMachineCycle - 1) /
                    _nativeCyclesPerMachineCycle;
            }

            if (_state.Cycles < machineCycles)
            {
                _state.Cycles = machineCycles;
            }

            Pipeline.InstructionBoundaryNativeCycle = end;
            Pipeline.PendingTailCycles = 0;
            Pipeline.SuppressNextOverlap = true;
            SetLastFlatTiming(
                plan.Key,
                0,
                start,
                end,
                Pipeline.BusControllerAvailableNativeCycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetLastFlatTiming(
            M68kInstructionTimingKey key,
            int overlapCycles,
            long startNativeCycle,
            long endNativeCycle,
            long pendingBusNativeCycle)
        {
            _lastFlatTimingKey = key;
            _lastFlatOverlapCycles = overlapCycles;
            _lastFlatStartNativeCycle = startNativeCycle;
            _lastFlatEndNativeCycle = endNativeCycle;
            _lastFlatPendingBusNativeCycle = pendingBusNativeCycle;
            _lastTimingIsFlat = true;
        }

        private void SynchronizeMachineToNative()
        {
            var machineCycles = _profile.NativeToMachineCycles(_state.NativeCycles);
            if (_state.Cycles < machineCycles)
            {
                _state.Cycles = machineCycles;
            }
        }
    }

    internal static class M68kTimingDescriptorCache
    {
        public static M68kTimingDescriptor[] Create(bool useHeadTail)
        {
            var keys = Enum.GetValues<M68kInstructionTimingKey>();
            var descriptors = new M68kTimingDescriptor[keys.Length];
            foreach (var key in keys)
            {
                if (M68kTimingDescriptor.TryCreateSpecialControlDescriptor(key, useHeadTail, out var descriptor) ||
                    M68kTimingDescriptor.TryCreateOperandShapeDescriptor(key, useHeadTail, out descriptor))
                {
                    descriptors[(int)key] = descriptor;
                }
            }

            return descriptors;
        }
    }

    internal static class M68020TimingModel
    {
        private static readonly M68kTimingDescriptor[] Descriptors = M68kTimingDescriptorCache.Create(useHeadTail: false);
        private static readonly M68kInstructionPlan[] Plans = CreatePlans(Descriptors);

        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Plans.Length &&
                Plans[index].Name is not null)
            {
                return Plans[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020);
        }

        internal static M68kTimingDescriptor GetDescriptor(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Descriptors.Length &&
                Descriptors[index].LegacyLabel is not null)
            {
                return Descriptors[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020);
        }

        internal static M68kInstructionPlan GetCompatibilityPlan(M68kInstructionTimingKey key)
        {
            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020);
        }

        private static M68kInstructionPlan[] CreatePlans(M68kTimingDescriptor[] descriptors)
        {
            var plans = new M68kInstructionPlan[descriptors.Length];
            for (var i = 0; i < descriptors.Length; i++)
            {
                if (descriptors[i].LegacyLabel is not null)
                {
                    plans[i] = M68kTimingFormula.CreatePlan(descriptors[i]);
                }
            }

            return plans;
        }
    }

    internal static class M68030TimingModel
    {
        private static readonly M68kTimingDescriptor[] Descriptors = M68kTimingDescriptorCache.Create(useHeadTail: true);
        private static readonly M68kInstructionPlan[] Plans = CreatePlans(Descriptors);

        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Plans.Length &&
                Plans[index].Name is not null)
            {
                return Plans[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68030);
        }

        internal static M68kTimingDescriptor GetDescriptor(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Descriptors.Length &&
                Descriptors[index].LegacyLabel is not null)
            {
                return Descriptors[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68030);
        }

        internal static M68kInstructionPlan GetCompatibilityPlan(M68kInstructionTimingKey key)
        {
            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68030);
        }

        private static M68kInstructionPlan[] CreatePlans(M68kTimingDescriptor[] descriptors)
        {
            var plans = new M68kInstructionPlan[descriptors.Length];
            for (var i = 0; i < descriptors.Length; i++)
            {
                if (descriptors[i].LegacyLabel is not null)
                {
                    plans[i] = M68kTimingFormula.CreatePlan(descriptors[i]);
                }
            }

            return plans;
        }
    }

    internal static class M68040FixedTimingModel
    {
        private static readonly M68kInstructionPlan[] Plans = CreatePlans();

        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            var index = (int)key;
            if ((uint)index < (uint)Plans.Length && Plans[index].Name is not null)
            {
                return Plans[index];
            }

            throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68040);
        }

        private static M68kInstructionPlan[] CreatePlans()
        {
            var keys = Enum.GetValues<M68kInstructionTimingKey>();
            var plans = new M68kInstructionPlan[(int)keys[^1] + 1];
            foreach (var key in keys)
            {
                plans[(int)key] = M68kTimingFormula.CreateFixedPlan(key, nativeCycles: 1);
            }

            return plans;
        }
    }

    internal sealed class M68kTimedBusAdapter
    {
        private readonly IM68kBus _bus;
        private readonly M68020CpuProfile _profile;
        private readonly M68kCpuState _state;
        private readonly M68kTimingEngine _timing;
        private readonly IM68kCodeReader? _codeReader;
        private readonly IM68kFastMemoryBus? _fastMemoryBus;
        private readonly M68040LogicalBus? _m68040LogicalBus;
        private readonly IM68kBus? _m68040PhysicalBus;
        private readonly bool _directUncachedInstructionFetch;
        private readonly bool _hasInstructionFetchWaitStates;

        public M68kTimedBusAdapter(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kTimingEngine timing)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _timing = timing ?? throw new ArgumentNullException(nameof(timing));
            _codeReader = bus as IM68kCodeReader;
            _fastMemoryBus = bus as IM68kFastMemoryBus;
            _m68040LogicalBus = bus as M68040LogicalBus;
            _m68040PhysicalBus = _m68040LogicalBus?.PhysicalBus;
            _directUncachedInstructionFetch = !profile.FastInstructionFetch;
            for (var i = 0; i < profile.BusTiming.Count; i++)
            {
                if (profile.BusTiming[i].WaitStates != 0)
                {
                    _hasInstructionFetchWaitStates = true;
                    break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ushort ReadInstructionFetchWordHot(
            uint address,
            out bool cacheHit,
            out bool requiresSynchronization,
            out long completedMachineCycle)
        {
            if (!_directUncachedInstructionFetch || _timing.InstructionCache.Enabled)
            {
                return ReadInstructionFetchWord(
                    address,
                    out cacheHit,
                    out requiresSynchronization,
                    out completedMachineCycle);
            }

            cacheHit = false;
            var cycle = GetBusRequestMachineCycle();
            var value = _m68040LogicalBus is { } logicalBus
                ? logicalBus.CanUseDirectIdentityAccess(address, byteCount: 2)
                    ? _m68040PhysicalBus!.ReadWord(
                        address,
                        ref cycle,
                        M68kBusAccessKind.CpuInstructionFetch)
                    : logicalBus.ReadWord(
                        address,
                        ref cycle,
                        M68kBusAccessKind.CpuInstructionFetch)
                : _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
            if (_hasInstructionFetchWaitStates)
            {
                AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            }

            _timing.RecordPostedBusCompletion(cycle);
            requiresSynchronization = true;
            completedMachineCycle = cycle;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint ReadDataLongHot(uint address)
        {
            if (_hasInstructionFetchWaitStates || _profile.FastNonChipMemoryAccess)
            {
                return ReadLong(address, M68kBusAccessKind.CpuDataRead);
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _m68040LogicalBus is { } logicalBus
                ? logicalBus.CanUseDirectIdentityAccess(address, byteCount: 4)
                    ? _m68040PhysicalBus!.ReadLong(
                        address,
                        ref cycle,
                        M68kBusAccessKind.CpuDataRead)
                    : logicalBus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead)
                : _bus.ReadLong(address, ref cycle, M68kBusAccessKind.CpuDataRead);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteDataLongHot(uint address, uint value)
        {
            if (_hasInstructionFetchWaitStates || _profile.FastNonChipMemoryAccess)
            {
                WriteLong(address, value, M68kBusAccessKind.CpuDataWrite);
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            if (_m68040LogicalBus is { } logicalBus)
            {
                if (logicalBus.CanUseDirectIdentityAccess(address, byteCount: 4))
                {
                    _m68040PhysicalBus!.WriteLong(
                        address,
                        value,
                        ref cycle,
                        M68kBusAccessKind.CpuDataWrite);
                }
                else
                {
                    logicalBus.WriteLong(
                        address,
                        value,
                        ref cycle,
                        M68kBusAccessKind.CpuDataWrite);
                }
            }
            else
            {
                _bus.WriteLong(address, value, ref cycle, M68kBusAccessKind.CpuDataWrite);
            }

            _timing.RecordPostedBusCompletion(cycle);
        }

        internal ushort ReadInstructionFetchWord(
            uint address,
            out bool cacheHit,
            out bool requiresSynchronization,
            out long completedMachineCycle)
        {
            if (_codeReader is not null && _timing.ProbeInstructionCache(address))
            {
                cacheHit = true;
                requiresSynchronization = false;
                completedMachineCycle = _state.Cycles;
                return _codeReader.ReadHostWord(address);
            }

            if (CanUseFastInstructionFetch(address, M68kBusAccessKind.CpuInstructionFetch) &&
                _codeReader is not null)
            {
                cacheHit = false;
                requiresSynchronization = false;
                completedMachineCycle = _state.Cycles;
                return _codeReader.ReadHostWord(address);
            }

            cacheHit = false;
            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadWord(address, ref cycle, M68kBusAccessKind.CpuInstructionFetch);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
            requiresSynchronization = true;
            completedMachineCycle = cycle;
            return value;
        }

        public byte ReadByte(uint address, M68kBusAccessKind accessKind)
        {
            if (CanUseFastCiaAPortAAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastByte(address, accessKind, out var ciaFastValue))
            {
                CompleteMinimalBlockingFastAccess();
                return ciaFastValue;
            }

            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastByte(address, accessKind, out var fastValue))
            {
                return fastValue;
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadByte(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Byte, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public ushort ReadWord(uint address, M68kBusAccessKind accessKind)
        {
            if (CanUseFastInstructionFetch(address, accessKind) &&
                _codeReader is not null)
            {
                return _codeReader.ReadHostWord(address);
            }

            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastWord(address, accessKind, out var fastValue))
            {
                return fastValue;
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadWord(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public uint ReadLong(uint address, M68kBusAccessKind accessKind)
        {
            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryReadFastLong(address, accessKind, out var fastValue))
            {
                return fastValue;
            }

            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadLong(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Long, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public void WriteByte(uint address, byte value, M68kBusAccessKind accessKind)
        {
            if (CanUseFastCiaAPortAAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastByte(address, value, accessKind))
            {
                RecordMinimalPostedFastAccess();
                return;
            }

            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastByte(address, value, accessKind))
            {
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            _bus.WriteByte(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Byte, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        public void WriteWord(uint address, ushort value, M68kBusAccessKind accessKind)
        {
            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastWord(address, value, accessKind))
            {
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            _bus.WriteWord(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        public void WriteLong(uint address, uint value, M68kBusAccessKind accessKind)
        {
            if (CanUseFastNonChipMemoryAccess(address, accessKind) &&
                _fastMemoryBus is not null &&
                _fastMemoryBus.TryWriteFastLong(address, value, accessKind))
            {
                return;
            }

            var cycle = GetBusRequestMachineCycle();
            _bus.WriteLong(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Long, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetBusRequestMachineCycle()
        {
            var nativeReady = Math.Max(_state.NativeCycles, _timing.BusControllerAvailableNativeCycle);
            return Math.Max(_state.Cycles, _profile.NativeToMachineCycles(nativeReady));
        }

        private bool CanUseFastInstructionFetch(uint address, M68kBusAccessKind accessKind)
        {
            if (!_profile.FastInstructionFetch ||
                accessKind != M68kBusAccessKind.CpuInstructionFetch)
            {
                return false;
            }

            return M68020CpuProfile.IsInstructionCacheableTarget(_profile.GetBusTimingRule(address).Target);
        }

        private bool CanUseFastNonChipMemoryAccess(uint address, M68kBusAccessKind accessKind)
        {
            if (!_profile.FastNonChipMemoryAccess ||
                accessKind == M68kBusAccessKind.CpuInstructionFetch)
            {
                return false;
            }

            var target = _profile.GetBusTimingRule(address).Target;
            return target is
                M68020MemoryTarget.ExpansionRam or
                M68020MemoryTarget.RealFastRam or
                M68020MemoryTarget.Rom;
        }

        private bool CanUseFastCiaAPortAAccess(uint address, M68kBusAccessKind accessKind)
            => _profile.FastCiaAPortAAccess &&
                accessKind != M68kBusAccessKind.CpuInstructionFetch &&
                (address & 0x00FF_FFFFu) == 0x00BF_E001u;

        private void CompleteMinimalBlockingFastAccess()
        {
            _timing.CompleteBlockingBusAccess(GetBusRequestMachineCycle() + 1);
        }

        private void RecordMinimalPostedFastAccess()
        {
            _timing.RecordPostedBusCompletion(GetBusRequestMachineCycle() + 1);
        }

        private void AddProfileWaitStates(uint address, M68020BusWidth transferWidth, ref long cycle)
        {
            var rule = _profile.GetBusTimingRule(address);
            var transfers = CountProfileTransfers(address, transferWidth, rule.Width);
            cycle += (long)Math.Max(0, rule.WaitStates) * transfers;
        }

        private static int CountProfileTransfers(uint address, M68020BusWidth transferWidth, M68020BusWidth targetWidth)
        {
            var bytes = (int)transferWidth;
            var width = Math.Max(1, (int)targetWidth);
            var startLane = (int)(address % (uint)width);
            return (startLane + bytes + width - 1) / width;
        }
    }

    internal sealed class UnsupportedM68kTimingException : M68kEmulationException
    {
        public UnsupportedM68kTimingException(ushort opcode, uint programCounter, M68020CpuProfile profile)
            : base($"Unsupported exact {profile.ModelName} timing for opcode 0x{opcode:X4} at 0x{programCounter:X8} in profile {profile.Name}.")
        {
            Opcode = opcode;
            ProgramCounter = programCounter;
            ProfileName = profile.Name;
        }

        public UnsupportedM68kTimingException(M68kInstructionTimingKey key, M68kAcceleratorModel model)
            : base($"Unsupported exact {model} timing plan for {key}.")
        {
            TimingKey = key;
            ProfileName = model.ToString();
        }

        public ushort Opcode { get; }

        public uint ProgramCounter { get; }

        public M68kInstructionTimingKey TimingKey { get; }

        public string ProfileName { get; }
    }
}
