using System;

namespace CopperMod.Amiga
{
    internal enum M68kAcceleratorModel
    {
        M68020,
        M68030
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
        Rte,
        Rtd,
        LinkLong,
        ExtbLong,
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
            DataCache = profile.Model == M68kAcceleratorModel.M68030 ? new M68kInstructionCache() : null;
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
            => _profile.Model == M68kAcceleratorModel.M68030
                ? M68030TimingModel.GetPlan(key)
                : M68020TimingModel.GetPlan(key);

        public bool ProbeInstructionCache(uint address)
            => InstructionCache.Probe(address);

        public bool ProbeDataCache(uint address)
            => DataCache?.Probe(address) == true;

        public void ApplyCacheControl(uint cacheControlRegister, uint cacheAddressRegister)
        {
            if (_profile.Model == M68kAcceleratorModel.M68030)
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
                M68kInstructionTimingKey.Rte => M68kInstructionPlan.CreateFlat(key, "RTE", 20, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Rtd => M68kInstructionPlan.CreateFlat(key, "RTD", 16, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.LinkLong => M68kInstructionPlan.CreateFlat(key, "LINK.L", 16),
                M68kInstructionTimingKey.ExtbLong => M68kInstructionPlan.CreateFlat(key, "EXTB.L", 4),
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
                M68kInstructionTimingKey.Rte => M68kInstructionPlan.CreateHeadTail(key, "RTE", 20, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.SynchronizeBus),
                M68kInstructionTimingKey.Rtd => M68kInstructionPlan.CreateHeadTail(key, "RTD", 16, 0, 0, M68kTimingBarrier.FlushPipeline | M68kTimingBarrier.Branch),
                M68kInstructionTimingKey.LinkLong => M68kInstructionPlan.CreateHeadTail(key, "LINK.L", 16, 2, 2),
                M68kInstructionTimingKey.ExtbLong => M68kInstructionPlan.CreateHeadTail(key, "EXTB.L", 4, 2, 2),
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
