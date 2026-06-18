using System;

namespace CopperMod.Amiga
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
        AddaLongImmediateToAddress,
        AddaLongDataToAddress,
        AddaLongAddressDisplacementToAddress,
        SubaLongImmediateToAddress,
        DivuWordImmediateToData,
        DivsWordImmediateToData,
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

        public M68kTimingEngine(M68020CpuProfile profile, M68kCpuState state)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            InstructionCache = new M68kInstructionCache();
            DataCache = profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040
                ? new M68kInstructionCache()
                : null;
        }

        public M68kPipelineState Pipeline { get; } = new M68kPipelineState();

        public M68kInstructionCache InstructionCache { get; }

        public M68kInstructionCache? DataCache { get; }

        public M68kExecutedInstructionTiming LastInstructionTiming { get; private set; }

        public long BusControllerAvailableNativeCycle => Pipeline.BusControllerAvailableNativeCycle;

        public void Reset()
        {
            Pipeline.Reset();
            InstructionCache.Reset();
            DataCache?.Reset();
            LastInstructionTiming = default;
        }

        public M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
            => _profile.Model is M68kAcceleratorModel.M68030 or M68kAcceleratorModel.M68040
                ? M68030TimingModel.GetPlan(key)
                : M68020TimingModel.GetPlan(key);

        public bool ProbeInstructionCache(uint address)
            => InstructionCache.Probe(address);

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

            LastInstructionTiming = new M68kExecutedInstructionTiming(
                plan,
                overlap,
                start,
                _state.NativeCycles,
                Pipeline.BusControllerAvailableNativeCycle);
            return LastInstructionTiming;
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

    internal static class M68020TimingModel
    {
        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.Idle => M68kInstructionPlan.CreateFlat(key, "IDLE", 2, M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Nop => M68kInstructionPlan.CreateFlat(key, "NOP", 4, M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.LineAException => M68kInstructionPlan.CreateFlat(key, "LINEA", 34, ExceptionBarrier()),
                M68kInstructionTimingKey.LineFException => M68kInstructionPlan.CreateFlat(key, "LINEF", 34, ExceptionBarrier()),
                M68kInstructionTimingKey.IllegalInstruction => M68kInstructionPlan.CreateFlat(key, "ILLEGAL", 20, ExceptionBarrier()),
                M68kInstructionTimingKey.PrivilegeViolation => M68kInstructionPlan.CreateFlat(key, "PRIVILEGE", 20, ExceptionBarrier()),
                M68kInstructionTimingKey.FormatError => M68kInstructionPlan.CreateFlat(key, "FORMAT", 20, ExceptionBarrier()),
                M68kInstructionTimingKey.InterruptAcknowledge => M68kInstructionPlan.CreateFlat(key, "INTERRUPT", 44, ExceptionBarrier()),
                M68kInstructionTimingKey.Movec => M68kInstructionPlan.CreateFlat(key, "MOVEC", 12, M68kTimingBarrier.CacheControl),
                M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister => M68kInstructionPlan.CreateFlat(key, "ORI/ANDI/EORI.W #<data>,CCR", 8),
                M68kInstructionTimingKey.ImmediateWordToStatusRegister => M68kInstructionPlan.CreateFlat(key, "ORI/ANDI/EORI.W #<data>,SR", 20, M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Rte => M68kInstructionPlan.CreateFlat(key, "RTE", 20, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Rtd => M68kInstructionPlan.CreateFlat(key, "RTD", 16, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.Rts => M68kInstructionPlan.CreateFlat(key, "RTS", 7, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.LinkLong => M68kInstructionPlan.CreateFlat(key, "LINK.L", 16),
                M68kInstructionTimingKey.ExtbLong => M68kInstructionPlan.CreateFlat(key, "EXTB.L", 4),
                M68kInstructionTimingKey.ExtWordData => M68kInstructionPlan.CreateFlat(key, "EXT.W Dn", 2),
                M68kInstructionTimingKey.TstWordData => M68kInstructionPlan.CreateFlat(key, "TST.W Dn", 2),
                M68kInstructionTimingKey.Moveq => M68kInstructionPlan.CreateFlat(key, "MOVEQ #<data>,Dn", 2),
                M68kInstructionTimingKey.NegLongData => M68kInstructionPlan.CreateFlat(key, "NEG.L Dn", 2),
                M68kInstructionTimingKey.NotByteData => M68kInstructionPlan.CreateFlat(key, "NOT.B Dn", 2),
                M68kInstructionTimingKey.ClrDataLong => M68kInstructionPlan.CreateFlat(key, "CLR.L Dn", 2),
                M68kInstructionTimingKey.ClrDataWord => M68kInstructionPlan.CreateFlat(key, "CLR.W Dn", 2),
                M68kInstructionTimingKey.ClrLongAddressIndirect => M68kInstructionPlan.CreateFlat(key, "CLR.L (An)", 6),
                M68kInstructionTimingKey.ClrLongAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "CLR.L (d16,An)", 8),
                M68kInstructionTimingKey.ClrLongAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "CLR.L (xxx).L", 8),
                M68kInstructionTimingKey.ClrByteAddressIndirect => M68kInstructionPlan.CreateFlat(key, "CLR.B (An)", 4),
                M68kInstructionTimingKey.ClrByteAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "CLR.B (d16,An)", 6),
                M68kInstructionTimingKey.ClrWordAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "CLR.W (d16,An)", 6),
                M68kInstructionTimingKey.ClrLongPostIncrement => M68kInstructionPlan.CreateFlat(key, "CLR.L (An)+", 6),
                M68kInstructionTimingKey.LeaAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "LEA (xxx).L,An", 6),
                M68kInstructionTimingKey.LeaAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "LEA (d16,An),An", 4),
                M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.B #<data>,(xxx).L", 8),
                M68kInstructionTimingKey.MoveWordImmediateToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.W #<data>,(xxx).L", 8),
                M68kInstructionTimingKey.MoveLongImmediateToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.L #<data>,(xxx).L", 10),
                M68kInstructionTimingKey.MoveLongImmediateToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "MOVE.L #<data>,(An)", 8),
                M68kInstructionTimingKey.MoveLongImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.L #<data>,(d16,An)", 10),
                M68kInstructionTimingKey.MoveLongImmediateToPostIncrement => M68kInstructionPlan.CreateFlat(key, "MOVE.L #<data>,(An)+", 8),
                M68kInstructionTimingKey.MoveLongImmediateToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L #<data>,Dn", 6),
                M68kInstructionTimingKey.MoveLongImmediateToAddress => M68kInstructionPlan.CreateFlat(key, "MOVEA.L #<data>,An", 6),
                M68kInstructionTimingKey.MoveLongDataToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L Dn,Dn", 2),
                M68kInstructionTimingKey.MoveLongDataToAddress => M68kInstructionPlan.CreateFlat(key, "MOVEA.L Dn,An", 2),
                M68kInstructionTimingKey.MoveLongDataToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "MOVE.L Dn,(An)", 4),
                M68kInstructionTimingKey.MoveLongDataToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.L Dn,(d16,An)", 6),
                M68kInstructionTimingKey.MoveLongAddressToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L An,Dn", 2),
                M68kInstructionTimingKey.MoveLongAddressToAddress => M68kInstructionPlan.CreateFlat(key, "MOVEA.L An,An", 2),
                M68kInstructionTimingKey.MoveLongAddressToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "MOVE.L An,(An)", 4),
                M68kInstructionTimingKey.MoveLongAddressToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.L An,(d16,An)", 6),
                M68kInstructionTimingKey.MoveLongAddressToPostIncrement => M68kInstructionPlan.CreateFlat(key, "MOVE.L An,(An)+", 4),
                M68kInstructionTimingKey.MoveLongAddressIndirectToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L (An),Dn", 6),
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddress => M68kInstructionPlan.CreateFlat(key, "MOVEA.L (An),An", 6),
                M68kInstructionTimingKey.MoveLongPostIncrementToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L (An)+,Dn", 6),
                M68kInstructionTimingKey.MoveLongPostIncrementToAddress => M68kInstructionPlan.CreateFlat(key, "MOVEA.L (An)+,An", 6),
                M68kInstructionTimingKey.MoveLongAddressDisplacementToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L (d16,An),Dn", 6),
                M68kInstructionTimingKey.MoveLongAddressDisplacementToAddress => M68kInstructionPlan.CreateFlat(key, "MOVEA.L (d16,An),An", 6),
                M68kInstructionTimingKey.MoveLongAddressDisplacementToPostIncrement => M68kInstructionPlan.CreateFlat(key, "MOVE.L (d16,An),(An)+", 10),
                M68kInstructionTimingKey.MoveLongBriefIndexedToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L (d8,An,Xn),Dn", 8),
                M68kInstructionTimingKey.MoveLongBriefIndexedToAddress => M68kInstructionPlan.CreateFlat(key, "MOVEA.L (d8,An,Xn),An", 8),
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "MOVE.L (An),(An)", 8),
                M68kInstructionTimingKey.MoveLongAbsoluteLongToData => M68kInstructionPlan.CreateFlat(key, "MOVE.L (xxx).L,Dn", 8),
                M68kInstructionTimingKey.MoveLongDataToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.L Dn,(xxx).L", 6),
                M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.L An,(xxx).L", 6),
                M68kInstructionTimingKey.MoveByteDataToData => M68kInstructionPlan.CreateFlat(key, "MOVE.B Dn,Dn", 2),
                M68kInstructionTimingKey.MoveByteImmediateToData => M68kInstructionPlan.CreateFlat(key, "MOVE.B #<data>,Dn", 4),
                M68kInstructionTimingKey.MoveByteImmediateToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "MOVE.B #<data>,(An)", 6),
                M68kInstructionTimingKey.MoveByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.B #<data>,(d16,An)", 6),
                M68kInstructionTimingKey.MoveByteImmediateToBriefIndexed => M68kInstructionPlan.CreateFlat(key, "MOVE.B #<data>,(d8,An,Xn)", 8),
                M68kInstructionTimingKey.MoveByteAddressIndirectToData => M68kInstructionPlan.CreateFlat(key, "MOVE.B (An),Dn", 6),
                M68kInstructionTimingKey.MoveBytePostIncrementToData => M68kInstructionPlan.CreateFlat(key, "MOVE.B (An)+,Dn", 6),
                M68kInstructionTimingKey.MoveByteAddressDisplacementToData => M68kInstructionPlan.CreateFlat(key, "MOVE.B (d16,An),Dn", 6),
                M68kInstructionTimingKey.MoveByteAbsoluteLongToData => M68kInstructionPlan.CreateFlat(key, "MOVE.B (xxx).L,Dn", 6),
                M68kInstructionTimingKey.MoveByteBriefIndexedToData => M68kInstructionPlan.CreateFlat(key, "MOVE.B (d8,An,Xn),Dn", 8),
                M68kInstructionTimingKey.MoveByteDataToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.B Dn,(xxx).L", 6),
                M68kInstructionTimingKey.MoveByteDataToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "MOVE.B Dn,(An)", 4),
                M68kInstructionTimingKey.MoveByteDataToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.B Dn,(d16,An)", 6),
                M68kInstructionTimingKey.MoveByteDataToBriefIndexed => M68kInstructionPlan.CreateFlat(key, "MOVE.B Dn,(d8,An,Xn)", 8),
                M68kInstructionTimingKey.MoveByteDataToPostIncrement => M68kInstructionPlan.CreateFlat(key, "MOVE.B Dn,(An)+", 4),
                M68kInstructionTimingKey.MoveByteDataToPredecrement => M68kInstructionPlan.CreateFlat(key, "MOVE.B Dn,-(An)", 4),
                M68kInstructionTimingKey.MoveByteBriefIndexedToPredecrement => M68kInstructionPlan.CreateFlat(key, "MOVE.B (d8,An,Xn),-(An)", 10),
                M68kInstructionTimingKey.MoveBytePostIncrementToPostIncrement => M68kInstructionPlan.CreateFlat(key, "MOVE.B (An)+,(An)+", 8),
                M68kInstructionTimingKey.MoveByteAddressIndirectToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.B (An),(xxx).L", 8),
                M68kInstructionTimingKey.MoveByteAbsoluteLongToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.B (xxx).L,(xxx).L", 10),
                M68kInstructionTimingKey.MoveWordAbsoluteLongToData => M68kInstructionPlan.CreateFlat(key, "MOVE.W (xxx).L,Dn", 6),
                M68kInstructionTimingKey.MoveWordAddressDisplacementToData => M68kInstructionPlan.CreateFlat(key, "MOVE.W (d16,An),Dn", 6),
                M68kInstructionTimingKey.MoveWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "MOVE.W #<data>,Dn", 4),
                M68kInstructionTimingKey.MoveWordImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.W #<data>,(d16,An)", 6),
                M68kInstructionTimingKey.MoveWordDataToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.W Dn,(d16,An)", 6),
                M68kInstructionTimingKey.MoveWordDataToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.W Dn,(xxx).L", 6),
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "MOVE.W (xxx).L,(xxx).L", 10),
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "MOVE.W (xxx).L,(d16,An)", 8),
                M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "ORI/ANDI.B #<data>,(xxx).L", 10),
                M68kInstructionTimingKey.AddiByteImmediateToData => M68kInstructionPlan.CreateFlat(key, "ADDI.B #<data>,Dn", 4),
                M68kInstructionTimingKey.AddiByteImmediateToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "ADDI.B #<data>,(An)", 6),
                M68kInstructionTimingKey.AddiByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "ADDI.B #<data>,(d16,An)", 8),
                M68kInstructionTimingKey.AddiWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "ADDI.W #<data>,Dn", 4),
                M68kInstructionTimingKey.AddiLongImmediateToData => M68kInstructionPlan.CreateFlat(key, "ADDI.L #<data>,Dn", 6),
                M68kInstructionTimingKey.AddiLongImmediateToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "ADDI.L #<data>,(xxx).L", 12),
                M68kInstructionTimingKey.SubiByteImmediateToData => M68kInstructionPlan.CreateFlat(key, "SUBI.B #<data>,Dn", 4),
                M68kInstructionTimingKey.SubiByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "SUBI.B #<data>,(d16,An)", 8),
                M68kInstructionTimingKey.SubiLongImmediateToData => M68kInstructionPlan.CreateFlat(key, "SUBI.L #<data>,Dn", 6),
                M68kInstructionTimingKey.SubByteDataToData => M68kInstructionPlan.CreateFlat(key, "SUB.B Dn,Dn", 2),
                M68kInstructionTimingKey.SubLongDataToData => M68kInstructionPlan.CreateFlat(key, "SUB.L Dn,Dn", 2),
                M68kInstructionTimingKey.SubLongAddressToData => M68kInstructionPlan.CreateFlat(key, "SUB.L An,Dn", 2),
                M68kInstructionTimingKey.SubLongAddressDisplacementToData => M68kInstructionPlan.CreateFlat(key, "SUB.L (d16,An),Dn", 7),
                M68kInstructionTimingKey.AddWordDataToData => M68kInstructionPlan.CreateFlat(key, "ADD.W Dn,Dn", 2),
                M68kInstructionTimingKey.AddLongDataToData => M68kInstructionPlan.CreateFlat(key, "ADD.L Dn,Dn", 2),
                M68kInstructionTimingKey.AddxLongDataToData => M68kInstructionPlan.CreateFlat(key, "ADDX.L Dn,Dn", 2),
                M68kInstructionTimingKey.AddLongPostIncrementToData => M68kInstructionPlan.CreateFlat(key, "ADD.L (An)+,Dn", 6),
                M68kInstructionTimingKey.AddWordDataToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "ADD.W Dn,(d16,An)", 9),
                M68kInstructionTimingKey.AddaLongImmediateToAddress => M68kInstructionPlan.CreateFlat(key, "ADDA.L #<data>,An", 6),
                M68kInstructionTimingKey.AddaLongDataToAddress => M68kInstructionPlan.CreateFlat(key, "ADDA.L Dn,An", 2),
                M68kInstructionTimingKey.AddaLongAddressDisplacementToAddress => M68kInstructionPlan.CreateFlat(key, "ADDA.L (d16,An),An", 7),
                M68kInstructionTimingKey.SubaLongImmediateToAddress => M68kInstructionPlan.CreateFlat(key, "SUBA.L #<data>,An", 6),
                M68kInstructionTimingKey.DivuWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "DIVU.W #<data>,Dn", 46),
                M68kInstructionTimingKey.DivsWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "DIVS.W #<data>,Dn", 58),
                M68kInstructionTimingKey.MuluLong => M68kInstructionPlan.CreateFlat(key, "MULU.L <ea>,Dn", 44),
                M68kInstructionTimingKey.MulsLong => M68kInstructionPlan.CreateFlat(key, "MULS.L <ea>,Dn", 44),
                M68kInstructionTimingKey.DivuLong => M68kInstructionPlan.CreateFlat(key, "DIVU.L <ea>,Dr:Dq", 76),
                M68kInstructionTimingKey.DivsLong => M68kInstructionPlan.CreateFlat(key, "DIVS.L <ea>,Dr:Dq", 82),
                M68kInstructionTimingKey.AbcdByteDataToData => M68kInstructionPlan.CreateFlat(key, "ABCD.B Dn,Dn", 6),
                M68kInstructionTimingKey.AbcdBytePredecrementMemory => M68kInstructionPlan.CreateFlat(key, "ABCD.B -(An),-(An)", 18),
                M68kInstructionTimingKey.SbcdByteDataToData => M68kInstructionPlan.CreateFlat(key, "SBCD.B Dn,Dn", 6),
                M68kInstructionTimingKey.SbcdBytePredecrementMemory => M68kInstructionPlan.CreateFlat(key, "SBCD.B -(An),-(An)", 18),
                M68kInstructionTimingKey.NbcdByteData => M68kInstructionPlan.CreateFlat(key, "NBCD.B Dn", 6),
                M68kInstructionTimingKey.NbcdByteAddressIndirect => M68kInstructionPlan.CreateFlat(key, "NBCD.B (An)", 8),
                M68kInstructionTimingKey.NbcdBytePostIncrement => M68kInstructionPlan.CreateFlat(key, "NBCD.B (An)+", 8),
                M68kInstructionTimingKey.NbcdBytePredecrement => M68kInstructionPlan.CreateFlat(key, "NBCD.B -(An)", 8),
                M68kInstructionTimingKey.NbcdByteAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "NBCD.B (d16,An)", 10),
                M68kInstructionTimingKey.NbcdByteBriefIndexed => M68kInstructionPlan.CreateFlat(key, "NBCD.B (d8,An,Xn)", 12),
                M68kInstructionTimingKey.NbcdByteAbsoluteWord => M68kInstructionPlan.CreateFlat(key, "NBCD.B (xxx).W", 10),
                M68kInstructionTimingKey.NbcdByteAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "NBCD.B (xxx).L", 12),
                M68kInstructionTimingKey.AndByteDataToData => M68kInstructionPlan.CreateFlat(key, "AND.B Dn,Dn", 2),
                M68kInstructionTimingKey.AndWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "AND.W #<data>,Dn", 4),
                M68kInstructionTimingKey.AndLongImmediateToData => M68kInstructionPlan.CreateFlat(key, "AND.L #<data>,Dn", 6),
                M68kInstructionTimingKey.MuluWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "MULU.W #<data>,Dn", 46),
                M68kInstructionTimingKey.EoriWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "EORI.W #<data>,Dn", 4),
                M68kInstructionTimingKey.EoriLongImmediateToData => M68kInstructionPlan.CreateFlat(key, "EORI.L #<data>,Dn", 6),
                M68kInstructionTimingKey.CmpiLongImmediateToData => M68kInstructionPlan.CreateFlat(key, "CMPI.L #<data>,Dn", 6),
                M68kInstructionTimingKey.CmpiLongImmediateToPostIncrement => M68kInstructionPlan.CreateFlat(key, "CMPI.L #<data>,(An)+", 10),
                M68kInstructionTimingKey.CmpiLongImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "CMPI.L #<data>,(d16,An)", 9),
                M68kInstructionTimingKey.CmpiLongImmediateToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "CMPI.L #<data>,(xxx).L", 10),
                M68kInstructionTimingKey.CmpiByteImmediateToData => M68kInstructionPlan.CreateFlat(key, "CMPI.B #<data>,Dn", 4),
                M68kInstructionTimingKey.CmpiByteImmediateToAddressIndirect => M68kInstructionPlan.CreateFlat(key, "CMPI.B #<data>,(An)", 6),
                M68kInstructionTimingKey.CmpiByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "CMPI.B #<data>,(d16,An)", 8),
                M68kInstructionTimingKey.CmpiWordImmediateToData => M68kInstructionPlan.CreateFlat(key, "CMPI.W #<data>,Dn", 4),
                M68kInstructionTimingKey.CmpiWordImmediateToAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "CMPI.W #<data>,(d16,An)", 8),
                M68kInstructionTimingKey.CmpiWordImmediateToAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "CMPI.W #<data>,(xxx).L", 8),
                M68kInstructionTimingKey.CmpaLongImmediateToAddress => M68kInstructionPlan.CreateFlat(key, "CMPA.L #<data>,An", 6),
                M68kInstructionTimingKey.CmpaLongDataToAddress => M68kInstructionPlan.CreateFlat(key, "CMPA.L Dn,An", 2),
                M68kInstructionTimingKey.CmpaLongAddressToAddress => M68kInstructionPlan.CreateFlat(key, "CMPA.L An,An", 2),
                M68kInstructionTimingKey.CmpaLongAddressIndirectToAddress => M68kInstructionPlan.CreateFlat(key, "CMPA.L (An),An", 6),
                M68kInstructionTimingKey.CmpLongDataToData => M68kInstructionPlan.CreateFlat(key, "CMP.L Dn,Dn", 2),
                M68kInstructionTimingKey.CmpLongAddressToData => M68kInstructionPlan.CreateFlat(key, "CMP.L An,Dn", 2),
                M68kInstructionTimingKey.CmpLongAddressIndirectToData => M68kInstructionPlan.CreateFlat(key, "CMP.L (An),Dn", 6),
                M68kInstructionTimingKey.CmpLongPostIncrementToData => M68kInstructionPlan.CreateFlat(key, "CMP.L (An)+,Dn", 6),
                M68kInstructionTimingKey.CmpByteDataToData => M68kInstructionPlan.CreateFlat(key, "CMP.B Dn,Dn", 2),
                M68kInstructionTimingKey.CmpByteAddressIndirectToData => M68kInstructionPlan.CreateFlat(key, "CMP.B (An),Dn", 6),
                M68kInstructionTimingKey.CmpByteAddressDisplacementToData => M68kInstructionPlan.CreateFlat(key, "CMP.B (d16,An),Dn", 6),
                M68kInstructionTimingKey.CmpByteAbsoluteLongToData => M68kInstructionPlan.CreateFlat(key, "CMP.B (xxx).L,Dn", 6),
                M68kInstructionTimingKey.CmpWordDataToData => M68kInstructionPlan.CreateFlat(key, "CMP.W Dn,Dn", 2),
                M68kInstructionTimingKey.CmpWordAddressDisplacementToData => M68kInstructionPlan.CreateFlat(key, "CMP.W (d16,An),Dn", 6),
                M68kInstructionTimingKey.LeaAbsoluteWord => M68kInstructionPlan.CreateFlat(key, "LEA (xxx).W,An", 4),
                M68kInstructionTimingKey.SwapData => M68kInstructionPlan.CreateFlat(key, "SWAP Dn", 4),
                M68kInstructionTimingKey.AsrLongImmediateData => M68kInstructionPlan.CreateFlat(key, "ASR.L #<data>,Dn", 6),
                M68kInstructionTimingKey.AsrWordImmediateData => M68kInstructionPlan.CreateFlat(key, "ASR.W #<data>,Dn", 6),
                M68kInstructionTimingKey.LsrLongImmediateData => M68kInstructionPlan.CreateFlat(key, "LSR.L #<data>,Dn", 6),
                M68kInstructionTimingKey.AslLongImmediateData => M68kInstructionPlan.CreateFlat(key, "ASL.L #<data>,Dn", 8),
                M68kInstructionTimingKey.AslWordImmediateData => M68kInstructionPlan.CreateFlat(key, "ASL.W #<data>,Dn", 8),
                M68kInstructionTimingKey.RorByteImmediateData => M68kInstructionPlan.CreateFlat(key, "ROR.B #<data>,Dn", 8),
                M68kInstructionTimingKey.RorWordImmediateData => M68kInstructionPlan.CreateFlat(key, "ROR.W #<data>,Dn", 8),
                M68kInstructionTimingKey.RolWordImmediateData => M68kInstructionPlan.CreateFlat(key, "ROL.W #<data>,Dn", 8),
                M68kInstructionTimingKey.JsrAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "JSR (xxx).L", 7, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.JmpAddressIndirect => M68kInstructionPlan.CreateFlat(key, "JMP (An)", 4, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.JmpAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "JMP (xxx).L", 6, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.OriByteImmediateToData => M68kInstructionPlan.CreateFlat(key, "ORI.B #<data>,Dn", 4),
                M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "BTST #<data>,(xxx).L", 10),
                M68kInstructionTimingKey.BchgByteImmediateAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "BCHG #<data>,(xxx).L", 12),
                M68kInstructionTimingKey.BclrByteImmediateAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "BCLR #<data>,(xxx).L", 12),
                M68kInstructionTimingKey.BsetByteImmediateAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "BSET #<data>,(xxx).L", 12),
                M68kInstructionTimingKey.BsetByteImmediateAddressDisplacement => M68kInstructionPlan.CreateFlat(key, "BSET #<data>,(d16,An)", 10),
                M68kInstructionTimingKey.BtstImmediateData => M68kInstructionPlan.CreateFlat(key, "BTST #<data>,Dn", 4),
                M68kInstructionTimingKey.BclrImmediateData => M68kInstructionPlan.CreateFlat(key, "BCLR #<data>,Dn", 4),
                M68kInstructionTimingKey.BsetImmediateData => M68kInstructionPlan.CreateFlat(key, "BSET #<data>,Dn", 4),
                M68kInstructionTimingKey.BtstDynamicData => M68kInstructionPlan.CreateFlat(key, "BTST Dn,Dn", 4),
                M68kInstructionTimingKey.BclrDynamicData => M68kInstructionPlan.CreateFlat(key, "BCLR Dn,Dn", 4),
                M68kInstructionTimingKey.SccAbsoluteLong => M68kInstructionPlan.CreateFlat(key, "Scc (xxx).L", 10),
                M68kInstructionTimingKey.BranchByteTaken => M68kInstructionPlan.CreateFlat(key, "Bcc.B taken", 6, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchByteNotTaken => M68kInstructionPlan.CreateFlat(key, "Bcc.B not taken", 4),
                M68kInstructionTimingKey.BsrByte => M68kInstructionPlan.CreateFlat(key, "BSR.B", 7, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchWordTaken => M68kInstructionPlan.CreateFlat(key, "Bcc.W taken", 6, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchWordNotTaken => M68kInstructionPlan.CreateFlat(key, "Bcc.W not taken", 6),
                M68kInstructionTimingKey.BsrWord => M68kInstructionPlan.CreateFlat(key, "BSR.W", 7, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.DbccConditionTrue => M68kInstructionPlan.CreateFlat(key, "DBcc condition true", 6),
                M68kInstructionTimingKey.DbccBranchTaken => M68kInstructionPlan.CreateFlat(key, "DBcc branch taken", 10, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.DbccExpired => M68kInstructionPlan.CreateFlat(key, "DBcc expired", 12),
                M68kInstructionTimingKey.BranchLongTaken => M68kInstructionPlan.CreateFlat(key, "Bcc.L taken", 10, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchLongNotTaken => M68kInstructionPlan.CreateFlat(key, "Bcc.L not taken", 8),
                M68kInstructionTimingKey.BsrLong => M68kInstructionPlan.CreateFlat(key, "BSR.L", 18, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                _ => throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68020)
            };
        }

        private static M68kTimingBarrier ExceptionBarrier()
            => M68kTimingBarrier.Exception | M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus;
    }

    internal static class M68030TimingModel
    {
        public static M68kInstructionPlan GetPlan(M68kInstructionTimingKey key)
        {
            return key switch
            {
                M68kInstructionTimingKey.Idle => M68kInstructionPlan.CreateHeadTail(key, "IDLE", 2, 0, 0, M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Nop => M68kInstructionPlan.CreateHeadTail(key, "NOP", 4, 0, 0, M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.LineAException => M68kInstructionPlan.CreateHeadTail(key, "LINEA", 34, 0, 0, ExceptionBarrier()),
                M68kInstructionTimingKey.LineFException => M68kInstructionPlan.CreateHeadTail(key, "LINEF", 34, 0, 0, ExceptionBarrier()),
                M68kInstructionTimingKey.IllegalInstruction => M68kInstructionPlan.CreateHeadTail(key, "ILLEGAL", 20, 0, 0, ExceptionBarrier()),
                M68kInstructionTimingKey.PrivilegeViolation => M68kInstructionPlan.CreateHeadTail(key, "PRIVILEGE", 20, 0, 0, ExceptionBarrier()),
                M68kInstructionTimingKey.FormatError => M68kInstructionPlan.CreateHeadTail(key, "FORMAT", 20, 0, 0, ExceptionBarrier()),
                M68kInstructionTimingKey.InterruptAcknowledge => M68kInstructionPlan.CreateHeadTail(key, "INTERRUPT", 44, 0, 0, ExceptionBarrier()),
                M68kInstructionTimingKey.Movec => M68kInstructionPlan.CreateHeadTail(key, "MOVEC", 12, 0, 0, M68kTimingBarrier.CacheControl | M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.ImmediateWordToConditionCodeRegister => M68kInstructionPlan.CreateHeadTail(key, "ORI/ANDI/EORI.W #<data>,CCR", 8, 1, 1),
                M68kInstructionTimingKey.ImmediateWordToStatusRegister => M68kInstructionPlan.CreateHeadTail(key, "ORI/ANDI/EORI.W #<data>,SR", 20, 0, 0, M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Rte => M68kInstructionPlan.CreateHeadTail(key, "RTE", 20, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Rtd => M68kInstructionPlan.CreateHeadTail(key, "RTD", 16, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.Rts => M68kInstructionPlan.CreateHeadTail(key, "RTS", 7, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.LinkLong => M68kInstructionPlan.CreateHeadTail(key, "LINK.L", 16, 2, 2),
                M68kInstructionTimingKey.ExtbLong => M68kInstructionPlan.CreateHeadTail(key, "EXTB.L", 4, 2, 2),
                M68kInstructionTimingKey.ExtWordData => M68kInstructionPlan.CreateHeadTail(key, "EXT.W Dn", 2, 1, 1),
                M68kInstructionTimingKey.TstWordData => M68kInstructionPlan.CreateHeadTail(key, "TST.W Dn", 2, 1, 1),
                M68kInstructionTimingKey.Moveq => M68kInstructionPlan.CreateHeadTail(key, "MOVEQ #<data>,Dn", 2, 1, 1),
                M68kInstructionTimingKey.NegLongData => M68kInstructionPlan.CreateHeadTail(key, "NEG.L Dn", 2, 1, 1),
                M68kInstructionTimingKey.NotByteData => M68kInstructionPlan.CreateHeadTail(key, "NOT.B Dn", 2, 1, 1),
                M68kInstructionTimingKey.ClrDataLong => M68kInstructionPlan.CreateHeadTail(key, "CLR.L Dn", 2, 1, 1),
                M68kInstructionTimingKey.ClrDataWord => M68kInstructionPlan.CreateHeadTail(key, "CLR.W Dn", 2, 1, 1),
                M68kInstructionTimingKey.ClrLongAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "CLR.L (An)", 6, 1, 1),
                M68kInstructionTimingKey.ClrLongAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "CLR.L (d16,An)", 8, 1, 1),
                M68kInstructionTimingKey.ClrLongAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "CLR.L (xxx).L", 8, 1, 1),
                M68kInstructionTimingKey.ClrByteAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "CLR.B (An)", 4, 1, 1),
                M68kInstructionTimingKey.ClrByteAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "CLR.B (d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.ClrWordAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "CLR.W (d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.ClrLongPostIncrement => M68kInstructionPlan.CreateHeadTail(key, "CLR.L (An)+", 6, 1, 1),
                M68kInstructionTimingKey.LeaAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "LEA (xxx).L,An", 6, 1, 1),
                M68kInstructionTimingKey.LeaAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "LEA (d16,An),An", 4, 1, 1),
                M68kInstructionTimingKey.MoveByteImmediateToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B #<data>,(xxx).L", 8, 1, 1),
                M68kInstructionTimingKey.MoveWordImmediateToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W #<data>,(xxx).L", 8, 1, 1),
                M68kInstructionTimingKey.MoveLongImmediateToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L #<data>,(xxx).L", 10, 1, 1),
                M68kInstructionTimingKey.MoveLongImmediateToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L #<data>,(An)", 8, 1, 1),
                M68kInstructionTimingKey.MoveLongImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L #<data>,(d16,An)", 10, 1, 1),
                M68kInstructionTimingKey.MoveLongImmediateToPostIncrement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L #<data>,(An)+", 8, 1, 1),
                M68kInstructionTimingKey.MoveLongImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongImmediateToAddress => M68kInstructionPlan.CreateHeadTail(key, "MOVEA.L #<data>,An", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongDataToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.MoveLongDataToAddress => M68kInstructionPlan.CreateHeadTail(key, "MOVEA.L Dn,An", 2, 1, 1),
                M68kInstructionTimingKey.MoveLongDataToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L Dn,(An)", 4, 1, 1),
                M68kInstructionTimingKey.MoveLongDataToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L Dn,(d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L An,Dn", 2, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressToAddress => M68kInstructionPlan.CreateHeadTail(key, "MOVEA.L An,An", 2, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L An,(An)", 4, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L An,(d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressToPostIncrement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L An,(An)+", 4, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressIndirectToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L (An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddress => M68kInstructionPlan.CreateHeadTail(key, "MOVEA.L (An),An", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongPostIncrementToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L (An)+,Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongPostIncrementToAddress => M68kInstructionPlan.CreateHeadTail(key, "MOVEA.L (An)+,An", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressDisplacementToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L (d16,An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressDisplacementToAddress => M68kInstructionPlan.CreateHeadTail(key, "MOVEA.L (d16,An),An", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressDisplacementToPostIncrement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L (d16,An),(An)+", 10, 1, 1),
                M68kInstructionTimingKey.MoveLongBriefIndexedToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L (d8,An,Xn),Dn", 8, 1, 1),
                M68kInstructionTimingKey.MoveLongBriefIndexedToAddress => M68kInstructionPlan.CreateHeadTail(key, "MOVEA.L (d8,An,Xn),An", 8, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressIndirectToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L (An),(An)", 8, 1, 1),
                M68kInstructionTimingKey.MoveLongAbsoluteLongToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L (xxx).L,Dn", 8, 1, 1),
                M68kInstructionTimingKey.MoveLongDataToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L Dn,(xxx).L", 6, 1, 1),
                M68kInstructionTimingKey.MoveLongAddressToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.L An,(xxx).L", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteDataToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.MoveByteImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.MoveByteImmediateToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B #<data>,(An)", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B #<data>,(d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteImmediateToBriefIndexed => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B #<data>,(d8,An,Xn)", 8, 1, 1),
                M68kInstructionTimingKey.MoveByteAddressIndirectToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveBytePostIncrementToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (An)+,Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteAddressDisplacementToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (d16,An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteAbsoluteLongToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (xxx).L,Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteBriefIndexedToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (d8,An,Xn),Dn", 8, 1, 1),
                M68kInstructionTimingKey.MoveByteDataToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B Dn,(xxx).L", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteDataToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B Dn,(An)", 4, 1, 1),
                M68kInstructionTimingKey.MoveByteDataToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B Dn,(d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.MoveByteDataToBriefIndexed => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B Dn,(d8,An,Xn)", 8, 1, 1),
                M68kInstructionTimingKey.MoveByteDataToPostIncrement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B Dn,(An)+", 4, 1, 1),
                M68kInstructionTimingKey.MoveByteDataToPredecrement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B Dn,-(An)", 4, 1, 1),
                M68kInstructionTimingKey.MoveByteBriefIndexedToPredecrement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (d8,An,Xn),-(An)", 10, 1, 1),
                M68kInstructionTimingKey.MoveBytePostIncrementToPostIncrement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (An)+,(An)+", 8, 1, 1),
                M68kInstructionTimingKey.MoveByteAddressIndirectToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (An),(xxx).L", 8, 1, 1),
                M68kInstructionTimingKey.MoveByteAbsoluteLongToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.B (xxx).L,(xxx).L", 10, 1, 1),
                M68kInstructionTimingKey.MoveWordAbsoluteLongToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W (xxx).L,Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveWordAddressDisplacementToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W (d16,An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.MoveWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.MoveWordImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W #<data>,(d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.MoveWordDataToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W Dn,(d16,An)", 6, 1, 1),
                M68kInstructionTimingKey.MoveWordDataToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W Dn,(xxx).L", 6, 1, 1),
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W (xxx).L,(xxx).L", 10, 1, 1),
                M68kInstructionTimingKey.MoveWordAbsoluteLongToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "MOVE.W (xxx).L,(d16,An)", 8, 1, 1),
                M68kInstructionTimingKey.ImmediateLogicalByteToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "ORI/ANDI.B #<data>,(xxx).L", 10, 1, 1),
                M68kInstructionTimingKey.AddiByteImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "ADDI.B #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.AddiByteImmediateToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "ADDI.B #<data>,(An)", 6, 1, 1),
                M68kInstructionTimingKey.AddiByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "ADDI.B #<data>,(d16,An)", 8, 1, 1),
                M68kInstructionTimingKey.AddiWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "ADDI.W #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.AddiLongImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "ADDI.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.AddiLongImmediateToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "ADDI.L #<data>,(xxx).L", 12, 1, 1),
                M68kInstructionTimingKey.SubiByteImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "SUBI.B #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.SubiByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "SUBI.B #<data>,(d16,An)", 8, 1, 1),
                M68kInstructionTimingKey.SubiLongImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "SUBI.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.SubByteDataToData => M68kInstructionPlan.CreateHeadTail(key, "SUB.B Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.SubLongDataToData => M68kInstructionPlan.CreateHeadTail(key, "SUB.L Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.SubLongAddressToData => M68kInstructionPlan.CreateHeadTail(key, "SUB.L An,Dn", 2, 1, 1),
                M68kInstructionTimingKey.SubLongAddressDisplacementToData => M68kInstructionPlan.CreateHeadTail(key, "SUB.L (d16,An),Dn", 7, 1, 1),
                M68kInstructionTimingKey.AddWordDataToData => M68kInstructionPlan.CreateHeadTail(key, "ADD.W Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.AddLongDataToData => M68kInstructionPlan.CreateHeadTail(key, "ADD.L Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.AddxLongDataToData => M68kInstructionPlan.CreateHeadTail(key, "ADDX.L Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.AddLongPostIncrementToData => M68kInstructionPlan.CreateHeadTail(key, "ADD.L (An)+,Dn", 6, 1, 1),
                M68kInstructionTimingKey.AddWordDataToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "ADD.W Dn,(d16,An)", 9, 1, 1),
                M68kInstructionTimingKey.AddaLongImmediateToAddress => M68kInstructionPlan.CreateHeadTail(key, "ADDA.L #<data>,An", 6, 1, 1),
                M68kInstructionTimingKey.AddaLongDataToAddress => M68kInstructionPlan.CreateHeadTail(key, "ADDA.L Dn,An", 2, 1, 1),
                M68kInstructionTimingKey.AddaLongAddressDisplacementToAddress => M68kInstructionPlan.CreateHeadTail(key, "ADDA.L (d16,An),An", 7, 1, 1),
                M68kInstructionTimingKey.SubaLongImmediateToAddress => M68kInstructionPlan.CreateHeadTail(key, "SUBA.L #<data>,An", 6, 1, 1),
                M68kInstructionTimingKey.DivuWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "DIVU.W #<data>,Dn", 46, 1, 1),
                M68kInstructionTimingKey.DivsWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "DIVS.W #<data>,Dn", 58, 1, 1),
                M68kInstructionTimingKey.MuluLong => M68kInstructionPlan.CreateHeadTail(key, "MULU.L <ea>,Dn", 44, 1, 1),
                M68kInstructionTimingKey.MulsLong => M68kInstructionPlan.CreateHeadTail(key, "MULS.L <ea>,Dn", 44, 1, 1),
                M68kInstructionTimingKey.DivuLong => M68kInstructionPlan.CreateHeadTail(key, "DIVU.L <ea>,Dr:Dq", 76, 1, 1),
                M68kInstructionTimingKey.DivsLong => M68kInstructionPlan.CreateHeadTail(key, "DIVS.L <ea>,Dr:Dq", 82, 1, 1),
                M68kInstructionTimingKey.AbcdByteDataToData => M68kInstructionPlan.CreateHeadTail(key, "ABCD.B Dn,Dn", 6, 1, 1),
                M68kInstructionTimingKey.AbcdBytePredecrementMemory => M68kInstructionPlan.CreateHeadTail(key, "ABCD.B -(An),-(An)", 18, 1, 1),
                M68kInstructionTimingKey.SbcdByteDataToData => M68kInstructionPlan.CreateHeadTail(key, "SBCD.B Dn,Dn", 6, 1, 1),
                M68kInstructionTimingKey.SbcdBytePredecrementMemory => M68kInstructionPlan.CreateHeadTail(key, "SBCD.B -(An),-(An)", 18, 1, 1),
                M68kInstructionTimingKey.NbcdByteData => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B Dn", 6, 1, 1),
                M68kInstructionTimingKey.NbcdByteAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B (An)", 8, 1, 1),
                M68kInstructionTimingKey.NbcdBytePostIncrement => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B (An)+", 8, 1, 1),
                M68kInstructionTimingKey.NbcdBytePredecrement => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B -(An)", 8, 1, 1),
                M68kInstructionTimingKey.NbcdByteAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B (d16,An)", 10, 1, 1),
                M68kInstructionTimingKey.NbcdByteBriefIndexed => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B (d8,An,Xn)", 12, 1, 1),
                M68kInstructionTimingKey.NbcdByteAbsoluteWord => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B (xxx).W", 10, 1, 1),
                M68kInstructionTimingKey.NbcdByteAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "NBCD.B (xxx).L", 12, 1, 1),
                M68kInstructionTimingKey.AndByteDataToData => M68kInstructionPlan.CreateHeadTail(key, "AND.B Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.AndWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "AND.W #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.AndLongImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "AND.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.MuluWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "MULU.W #<data>,Dn", 46, 1, 1),
                M68kInstructionTimingKey.EoriWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "EORI.W #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.EoriLongImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "EORI.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.CmpiLongImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "CMPI.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.CmpiLongImmediateToPostIncrement => M68kInstructionPlan.CreateHeadTail(key, "CMPI.L #<data>,(An)+", 10, 1, 1),
                M68kInstructionTimingKey.CmpiLongImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "CMPI.L #<data>,(d16,An)", 9, 1, 1),
                M68kInstructionTimingKey.CmpiLongImmediateToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "CMPI.L #<data>,(xxx).L", 10, 1, 1),
                M68kInstructionTimingKey.CmpiByteImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "CMPI.B #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.CmpiByteImmediateToAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "CMPI.B #<data>,(An)", 6, 1, 1),
                M68kInstructionTimingKey.CmpiByteImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "CMPI.B #<data>,(d16,An)", 8, 1, 1),
                M68kInstructionTimingKey.CmpiWordImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "CMPI.W #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.CmpiWordImmediateToAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "CMPI.W #<data>,(d16,An)", 8, 1, 1),
                M68kInstructionTimingKey.CmpiWordImmediateToAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "CMPI.W #<data>,(xxx).L", 8, 1, 1),
                M68kInstructionTimingKey.CmpaLongImmediateToAddress => M68kInstructionPlan.CreateHeadTail(key, "CMPA.L #<data>,An", 6, 1, 1),
                M68kInstructionTimingKey.CmpaLongDataToAddress => M68kInstructionPlan.CreateHeadTail(key, "CMPA.L Dn,An", 2, 1, 1),
                M68kInstructionTimingKey.CmpaLongAddressToAddress => M68kInstructionPlan.CreateHeadTail(key, "CMPA.L An,An", 2, 1, 1),
                M68kInstructionTimingKey.CmpaLongAddressIndirectToAddress => M68kInstructionPlan.CreateHeadTail(key, "CMPA.L (An),An", 6, 1, 1),
                M68kInstructionTimingKey.CmpLongDataToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.L Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.CmpLongAddressToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.L An,Dn", 2, 1, 1),
                M68kInstructionTimingKey.CmpLongAddressIndirectToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.L (An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.CmpLongPostIncrementToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.L (An)+,Dn", 6, 1, 1),
                M68kInstructionTimingKey.CmpByteDataToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.B Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.CmpByteAddressIndirectToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.B (An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.CmpByteAddressDisplacementToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.B (d16,An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.CmpByteAbsoluteLongToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.B (xxx).L,Dn", 6, 1, 1),
                M68kInstructionTimingKey.CmpWordDataToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.W Dn,Dn", 2, 1, 1),
                M68kInstructionTimingKey.CmpWordAddressDisplacementToData => M68kInstructionPlan.CreateHeadTail(key, "CMP.W (d16,An),Dn", 6, 1, 1),
                M68kInstructionTimingKey.LeaAbsoluteWord => M68kInstructionPlan.CreateHeadTail(key, "LEA (xxx).W,An", 4, 1, 1),
                M68kInstructionTimingKey.SwapData => M68kInstructionPlan.CreateHeadTail(key, "SWAP Dn", 4, 1, 1),
                M68kInstructionTimingKey.AsrLongImmediateData => M68kInstructionPlan.CreateHeadTail(key, "ASR.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.AsrWordImmediateData => M68kInstructionPlan.CreateHeadTail(key, "ASR.W #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.LsrLongImmediateData => M68kInstructionPlan.CreateHeadTail(key, "LSR.L #<data>,Dn", 6, 1, 1),
                M68kInstructionTimingKey.AslLongImmediateData => M68kInstructionPlan.CreateHeadTail(key, "ASL.L #<data>,Dn", 8, 1, 1),
                M68kInstructionTimingKey.AslWordImmediateData => M68kInstructionPlan.CreateHeadTail(key, "ASL.W #<data>,Dn", 8, 1, 1),
                M68kInstructionTimingKey.RorByteImmediateData => M68kInstructionPlan.CreateHeadTail(key, "ROR.B #<data>,Dn", 8, 1, 1),
                M68kInstructionTimingKey.RorWordImmediateData => M68kInstructionPlan.CreateHeadTail(key, "ROR.W #<data>,Dn", 8, 1, 1),
                M68kInstructionTimingKey.RolWordImmediateData => M68kInstructionPlan.CreateHeadTail(key, "ROL.W #<data>,Dn", 8, 1, 1),
                M68kInstructionTimingKey.JsrAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "JSR (xxx).L", 7, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.JmpAddressIndirect => M68kInstructionPlan.CreateHeadTail(key, "JMP (An)", 4, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.JmpAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "JMP (xxx).L", 6, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.OriByteImmediateToData => M68kInstructionPlan.CreateHeadTail(key, "ORI.B #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.BtstByteImmediateAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "BTST #<data>,(xxx).L", 10, 1, 1),
                M68kInstructionTimingKey.BchgByteImmediateAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "BCHG #<data>,(xxx).L", 12, 1, 1),
                M68kInstructionTimingKey.BclrByteImmediateAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "BCLR #<data>,(xxx).L", 12, 1, 1),
                M68kInstructionTimingKey.BsetByteImmediateAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "BSET #<data>,(xxx).L", 12, 1, 1),
                M68kInstructionTimingKey.BsetByteImmediateAddressDisplacement => M68kInstructionPlan.CreateHeadTail(key, "BSET #<data>,(d16,An)", 10, 1, 1),
                M68kInstructionTimingKey.BtstImmediateData => M68kInstructionPlan.CreateHeadTail(key, "BTST #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.BclrImmediateData => M68kInstructionPlan.CreateHeadTail(key, "BCLR #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.BsetImmediateData => M68kInstructionPlan.CreateHeadTail(key, "BSET #<data>,Dn", 4, 1, 1),
                M68kInstructionTimingKey.BtstDynamicData => M68kInstructionPlan.CreateHeadTail(key, "BTST Dn,Dn", 4, 1, 1),
                M68kInstructionTimingKey.BclrDynamicData => M68kInstructionPlan.CreateHeadTail(key, "BCLR Dn,Dn", 4, 1, 1),
                M68kInstructionTimingKey.SccAbsoluteLong => M68kInstructionPlan.CreateHeadTail(key, "Scc (xxx).L", 10, 1, 1),
                M68kInstructionTimingKey.BranchByteTaken => M68kInstructionPlan.CreateHeadTail(key, "Bcc.B taken", 6, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchByteNotTaken => M68kInstructionPlan.CreateHeadTail(key, "Bcc.B not taken", 4, 1, 1),
                M68kInstructionTimingKey.BsrByte => M68kInstructionPlan.CreateHeadTail(key, "BSR.B", 7, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchWordTaken => M68kInstructionPlan.CreateHeadTail(key, "Bcc.W taken", 6, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchWordNotTaken => M68kInstructionPlan.CreateHeadTail(key, "Bcc.W not taken", 6, 1, 1),
                M68kInstructionTimingKey.BsrWord => M68kInstructionPlan.CreateHeadTail(key, "BSR.W", 7, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.DbccConditionTrue => M68kInstructionPlan.CreateHeadTail(key, "DBcc condition true", 6, 1, 1),
                M68kInstructionTimingKey.DbccBranchTaken => M68kInstructionPlan.CreateHeadTail(key, "DBcc branch taken", 10, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.DbccExpired => M68kInstructionPlan.CreateHeadTail(key, "DBcc expired", 12, 1, 1),
                M68kInstructionTimingKey.BranchLongTaken => M68kInstructionPlan.CreateHeadTail(key, "Bcc.L taken", 10, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.BranchLongNotTaken => M68kInstructionPlan.CreateHeadTail(key, "Bcc.L not taken", 8, 1, 1),
                M68kInstructionTimingKey.BsrLong => M68kInstructionPlan.CreateHeadTail(key, "BSR.L", 18, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                _ => throw new UnsupportedM68kTimingException(key, M68kAcceleratorModel.M68030)
            };
        }

        private static M68kTimingBarrier ExceptionBarrier()
            => M68kTimingBarrier.Exception | M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus;
    }

    internal sealed class M68kAcceleratorBusBridge
    {
        private readonly IM68kBus _bus;
        private readonly M68020CpuProfile _profile;
        private readonly M68kCpuState _state;
        private readonly M68kTimingEngine _timing;

        public M68kAcceleratorBusBridge(
            IM68kBus bus,
            M68020CpuProfile profile,
            M68kCpuState state,
            M68kTimingEngine timing)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _timing = timing ?? throw new ArgumentNullException(nameof(timing));
        }

        public byte ReadByte(uint address, AmigaBusAccessKind accessKind)
        {
            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadByte(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Byte, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public ushort ReadWord(uint address, AmigaBusAccessKind accessKind)
        {
            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadWord(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public uint ReadLong(uint address, AmigaBusAccessKind accessKind)
        {
            var cycle = GetBusRequestMachineCycle();
            var value = _bus.ReadLong(address, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Long, ref cycle);
            _timing.CompleteBlockingBusAccess(cycle);
            return value;
        }

        public void WriteByte(uint address, byte value, AmigaBusAccessKind accessKind)
        {
            var cycle = GetBusRequestMachineCycle();
            _bus.WriteByte(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Byte, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        public void WriteWord(uint address, ushort value, AmigaBusAccessKind accessKind)
        {
            var cycle = GetBusRequestMachineCycle();
            _bus.WriteWord(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Word, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        public void WriteLong(uint address, uint value, AmigaBusAccessKind accessKind)
        {
            var cycle = GetBusRequestMachineCycle();
            _bus.WriteLong(address, value, ref cycle, accessKind);
            AddProfileWaitStates(address, M68020BusWidth.Long, ref cycle);
            _timing.RecordPostedBusCompletion(cycle);
        }

        private long GetBusRequestMachineCycle()
        {
            var nativeReady = Math.Max(_state.NativeCycles, _timing.BusControllerAvailableNativeCycle);
            return Math.Max(_state.Cycles, _profile.NativeToMachineCycles(nativeReady));
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

    internal sealed class UnsupportedM68kTimingException : AmigaEmulationException
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
