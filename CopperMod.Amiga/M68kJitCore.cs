using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace CopperMod.Amiga
{
    internal sealed class M68kJitCore : IM68kBatchCore, IM68kInstructionFrequencyProvider
    {
        private const int CompileThreshold = 64;
        private const int MaxTraceInstructions = 64;
        private const int MaxTraceBytes = 256;
        private const int V2Tier0TraceInstructions = 32;
        private const int V2Tier0TraceBytes = 128;
        private const int V2Tier1TraceInstructions = 64;
        private const int V2Tier1TraceBytes = 256;
        private const int V2Tier2TraceInstructions = 96;
        private const int V2Tier2TraceBytes = 384;
        private const int V2Tier3TraceInstructions = 128;
        private const int V2Tier3TraceBytes = 512;
        private const int V2Tier1PromotionHits = 4096;
        private const int V2Tier2PromotionHits = 16384;
        private const int V2Tier3PromotionHits = 65536;
        private const int V2Tier1BranchExitPromotionExits = 512;
        private const int V2Tier2BranchExitPromotionExits = 2048;
        private const int V2Tier3BranchExitPromotionExits = 4096;
        private const int V2TierCeilingBranchExitLimitExits = 128;
        private const int V2UnsupportedGraphHoleDisableExits = 8;
        private const int V2ZeroInstructionExitDisableExits = 8;
        private const int V2DiagnosticTopCount = 5;
        private const int V2MaxExternalHandoffPrequeues = 4;
        private const int AsyncCompileQueueCapacity = 1024;
        private const int BlacklistHits = 256;
        private const int CodeGenerationPageShift = 8;
        private const int CodeGenerationPageSize = 1 << CodeGenerationPageShift;
        private const ushort LogicFlags = M68kCpuState.Negative |
            M68kCpuState.Zero |
            M68kCpuState.Overflow |
            M68kCpuState.Carry;
        private const ushort ArithmeticFlags = LogicFlags | M68kCpuState.Extend;

        private readonly IM68kBus _bus;
        private readonly AmigaBus? _amigaBus;
        private readonly M68kInterpreter _fallback;
        private readonly bool _v2Enabled;
        private readonly bool _v2Tier3Enabled;
        private readonly bool _v2MemoryReadEnabled;
        private readonly bool _v2BusAccessEnabled;
        private readonly bool _v2FastReadEnabled;
        private readonly bool _v2BusGraphEnabled;
        private readonly bool _asyncJitEnabled;
        private readonly bool _asyncJitSyncFallbackEnabled;
        private readonly M68kAsyncJitCompiler? _asyncCompiler;
        private readonly M68kInstructionFrequencyMatrix _instructionFrequency = new M68kInstructionFrequencyMatrix();
        private readonly Dictionary<uint, TraceEntry> _traces = new Dictionary<uint, TraceEntry>();
        private readonly Dictionary<uint, HashSet<uint>> _traceRootsByPage = new Dictionary<uint, HashSet<uint>>();
        private readonly Dictionary<uint, int> _hotCounters = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _blacklist = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _v2TraceHits = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _v2TraceBranchExits = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _v2UnsupportedGraphHoleExits = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _v2ZeroInstructionExits = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _v2BlockedPromotionTiers = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _v2HandoffQueuePressures = new Dictionary<uint, int>();
        private readonly HashSet<uint> _v2BranchPressureLimitedRoots = new HashSet<uint>();
        private readonly HashSet<ulong> _v2BranchPressureLimitedTargets = new HashSet<ulong>();
        private readonly HashSet<uint> _v2BranchPressureQueuedTargets = new HashSet<uint>();
        private readonly HashSet<uint> _v2DisabledRoots = new HashSet<uint>();
        private readonly Dictionary<uint, string> _v2DisabledRootReasons = new Dictionary<uint, string>();
        private readonly Dictionary<string, long> _v2UnsupportedOperationCauses = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2UnsupportedEaCauses = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2GraphHoleCauses = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2TraceHandoffBlockCauses = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2TraceHandoffFailureCauses = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2BranchPressureLimitCauses = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2BranchPressureTargetStates = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _pureTraceBatchLengthBuckets = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _pureTraceBatchWakeSources = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2BusAccessBatchLengthBuckets = new Dictionary<string, long>();
        private readonly Dictionary<string, long> _v2BusAccessBatchWakeSources = new Dictionary<string, long>();
        private readonly Dictionary<ulong, TraceEntry> _v2HandoffTraceCache = new Dictionary<ulong, TraceEntry>();
        private bool _v2PendingOutOfBlockBranch;
        private uint _v2PendingOutOfBlockBranchRoot;
        private uint _v2PendingOutOfBlockBranchTarget;
        private uint _v2PendingOutOfBlockBranchCodeStart;
        private int _v2PendingOutOfBlockBranchByteLength;
        private bool _v2PendingOutOfBlockBranchIsFallthrough;
        private bool _disposed;
        private int _asyncCompileEpoch;
        private int _asyncCompileSequence;
        private static readonly MethodInfo CheckV2ConditionMethod =
            typeof(M68kJitCore).GetMethod(nameof(CheckV2Condition), BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(CheckV2Condition));
        private static readonly MethodInfo CheckV2ConditionFromPendingFlagsMethod =
            typeof(M68kJitCore).GetMethod(nameof(CheckV2ConditionFromPendingFlags), BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(CheckV2ConditionFromPendingFlags));
        private static readonly MethodInfo ShiftV2RegisterMethod =
            typeof(M68kJitCore).GetMethod(nameof(ShiftV2Register), BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ShiftV2Register));
        private static readonly MethodInfo DivideV2RegisterMethod =
            typeof(M68kJitCore).GetMethod(nameof(DivideV2Register), BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(DivideV2Register));
        private static readonly MethodInfo ReadMemoryValueForV2BatchMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(ReadMemoryValueForV2Batch),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(M68kOperandSize) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ReadMemoryValueForV2Batch));
        private static readonly MethodInfo ReadMemoryValueForV2BatchSlowMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(ReadMemoryValueForV2BatchSlow),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(M68kOperandSize) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ReadMemoryValueForV2BatchSlow));
        private static readonly MethodInfo ReadMemoryValueForV2BatchSlowRefMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(ReadMemoryValueForV2BatchSlowRef),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(long).MakeByRefType(), typeof(uint), typeof(M68kOperandSize) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ReadMemoryValueForV2BatchSlowRef));
        private static readonly MethodInfo WriteMemoryValueForV2BatchMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(WriteMemoryValueForV2Batch),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(uint), typeof(M68kOperandSize) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(WriteMemoryValueForV2Batch));
        private static readonly MethodInfo WriteMemoryValueForV2BatchSlowRefMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(WriteMemoryValueForV2BatchSlowRef),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(long).MakeByRefType(), typeof(uint), typeof(uint), typeof(M68kOperandSize) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(WriteMemoryValueForV2BatchSlowRef));
        private static readonly MethodInfo PushLongMethod =
            typeof(M68kJitCore).GetMethod(nameof(PushLong), BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(PushLong));
        private static readonly MethodInfo PullLongMethod =
            typeof(M68kJitCore).GetMethod(nameof(PullLong), BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(PullLong));
        private static readonly MethodInfo ExecuteV2JumpToMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(ExecuteV2JumpTo),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(bool), typeof(int) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ExecuteV2JumpTo));
        private static readonly MethodInfo ExecuteV2DivideByZeroMethod =
            typeof(M68kJitCore).GetMethod(nameof(ExecuteV2DivideByZero), BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ExecuteV2DivideByZero));
        private static readonly MethodInfo ExecuteCompiledMovemForV2BatchMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(ExecuteCompiledMovemForV2Batch),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[]
                {
                    typeof(int),
                    typeof(int),
                    typeof(uint),
                    typeof(ushort),
                    typeof(ushort),
                    typeof(int),
                    typeof(ushort),
                    typeof(int)
                },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ExecuteCompiledMovemForV2Batch));
        private static readonly MethodInfo ExecuteStatusImmediateForV2BatchMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(ExecuteCompiledStatusImmediate),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(int), typeof(uint) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ExecuteCompiledStatusImmediate));
        private static readonly MethodInfo ExecutePeaForV2BatchMethod =
            typeof(M68kJitCore).GetMethod(
                nameof(ExecuteCompiledPea),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(int), typeof(int), typeof(uint), typeof(ushort), typeof(ushort) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(ExecuteCompiledPea));
        private static readonly FieldInfo AmigaBusField =
            typeof(M68kJitCore).GetField(nameof(_amigaBus), BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(typeof(M68kJitCore).FullName, nameof(_amigaBus));
        private static readonly FieldInfo AmigaBusRealFastRamField =
            typeof(AmigaBus).GetField("_realFastRam", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(typeof(AmigaBus).FullName, "_realFastRam");
        private static readonly FieldInfo AmigaBusRealFastRamBaseField =
            typeof(AmigaBus).GetField("<RealFastRamBase>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(typeof(AmigaBus).FullName, "<RealFastRamBase>k__BackingField");
        private static readonly FieldInfo AmigaBusRomOverlayRegionField =
            typeof(AmigaBus).GetField("_romOverlayRegion", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(typeof(AmigaBus).FullName, "_romOverlayRegion");
        private static readonly FieldInfo AmigaBusRomOverlayEnabledField =
            typeof(AmigaBus).GetField("_romOverlayEnabled", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(typeof(AmigaBus).FullName, "_romOverlayEnabled");
        private static readonly FieldInfo MappedMemoryRegionDataField =
            AmigaBusRomOverlayRegionField.FieldType.GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(AmigaBusRomOverlayRegionField.FieldType.FullName, "_data");
        private static readonly FieldInfo MappedMemoryRegionBaseAddressField =
            AmigaBusRomOverlayRegionField.FieldType.GetField("<BaseAddress>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingFieldException(AmigaBusRomOverlayRegionField.FieldType.FullName, "<BaseAddress>k__BackingField");
        private static readonly MethodInfo TryReadJitZeroWaitMemoryMethod =
            typeof(AmigaBus).GetMethod(
                nameof(AmigaBus.TryReadJitZeroWaitMemory),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(M68kOperandSize), typeof(uint).MakeByRefType() },
                modifiers: null) ??
            throw new MissingMethodException(typeof(AmigaBus).FullName, nameof(AmigaBus.TryReadJitZeroWaitMemory));
        private static readonly MethodInfo TryWriteJitZeroWaitMemoryMethod =
            typeof(AmigaBus).GetMethod(
                nameof(AmigaBus.TryWriteJitZeroWaitMemory),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(uint), typeof(M68kOperandSize) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(AmigaBus).FullName, nameof(AmigaBus.TryWriteJitZeroWaitMemory));
        private static readonly MethodInfo TryGetJitZeroWaitReadMemoryMethod =
            typeof(AmigaBus).GetMethod(
                nameof(AmigaBus.TryGetJitZeroWaitReadMemory),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(int), typeof(byte[]).MakeByRefType(), typeof(int).MakeByRefType() },
                modifiers: null) ??
            throw new MissingMethodException(typeof(AmigaBus).FullName, nameof(AmigaBus.TryGetJitZeroWaitReadMemory));
        private static readonly MethodInfo TryGetJitZeroWaitWriteMemoryMethod =
            typeof(AmigaBus).GetMethod(
                nameof(AmigaBus.TryGetJitZeroWaitWriteMemory),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(int), typeof(byte[]).MakeByRefType(), typeof(int).MakeByRefType() },
                modifiers: null) ??
            throw new MissingMethodException(typeof(AmigaBus).FullName, nameof(AmigaBus.TryGetJitZeroWaitWriteMemory));
        private static readonly MethodInfo CompleteJitZeroWaitWriteMethod =
            typeof(AmigaBus).GetMethod(
                nameof(AmigaBus.CompleteJitZeroWaitWrite),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(uint), typeof(int) },
                modifiers: null) ??
            throw new MissingMethodException(typeof(AmigaBus).FullName, nameof(AmigaBus.CompleteJitZeroWaitWrite));
        private M68kJitCounters _counters;
        private long _compiledInstructionPreviousCycle;

        public M68kJitCore(IM68kBus bus)
            : this(
                bus,
                IsV2EnabledByDefault(),
                IsV2Tier3EnabledByDefault(),
                IsV2MemoryReadEnabledByDefault(),
                IsV2BusAccessEnabledByDefault(),
                IsV2FastReadEnabledByDefault(),
                IsV2BusGraphEnabledByDefault())
        {
        }

        internal M68kJitCore(
            IM68kBus bus,
            bool enableV2,
            bool enableV2Tier3 = false,
            bool enableV2MemoryRead = false,
            bool enableV2BusAccess = true,
            bool enableV2FastRead = false,
            bool enableV2BusGraph = false)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _amigaBus = bus as AmigaBus;
            _v2Enabled = enableV2;
            _v2Tier3Enabled = enableV2Tier3;
            _v2MemoryReadEnabled = enableV2MemoryRead;
            _v2BusAccessEnabled = enableV2BusAccess;
            _v2FastReadEnabled = enableV2FastRead;
            _v2BusGraphEnabled = enableV2BusGraph;
            _asyncJitEnabled = IsAsyncJitEnabledByDefault();
            _asyncJitSyncFallbackEnabled = IsAsyncJitSyncFallbackEnabledByDefault();
            State = new M68kCpuState();
            _fallback = new M68kInterpreter(bus, State, _instructionFrequency);
            if (_asyncJitEnabled)
            {
                _asyncCompiler = new M68kAsyncJitCompiler(
                    GetAsyncJitWorkerCount(),
                    AsyncCompileQueueCapacity);
            }

            if (_amigaBus != null)
            {
                _amigaBus.JitEligibleMemoryWritten += InvalidateWrittenCodeRange;
            }
        }

        public M68kCpuState State { get; }

        public M68kJitCounters Counters
        {
            get
            {
                var counters = _counters;
                counters.V2UnsupportedOperationTop = FormatV2DiagnosticTop(_v2UnsupportedOperationCauses);
                counters.V2UnsupportedEaTop = FormatV2DiagnosticTop(_v2UnsupportedEaCauses);
                counters.V2GraphHoleTop = FormatV2DiagnosticTop(_v2GraphHoleCauses);
                counters.V2TraceHandoffBlockTop = FormatV2DiagnosticTop(_v2TraceHandoffBlockCauses);
                counters.V2TraceHandoffFailureTop = FormatV2DiagnosticTop(_v2TraceHandoffFailureCauses);
                counters.V2BranchPressureLimitTop = FormatV2DiagnosticTop(_v2BranchPressureLimitCauses);
                counters.V2BranchPressureTargetStateTop = FormatV2DiagnosticTop(_v2BranchPressureTargetStates);
                counters.PureTraceBatchLengthHistogram = FormatBatchLengthHistogram(_pureTraceBatchLengthBuckets);
                counters.PureTraceBatchWakeSourceTop = FormatV2DiagnosticTop(_pureTraceBatchWakeSources);
                counters.V2BusAccessBatchLengthHistogram = FormatBatchLengthHistogram(_v2BusAccessBatchLengthBuckets);
                counters.V2BusAccessBatchWakeSourceTop = FormatV2DiagnosticTop(_v2BusAccessBatchWakeSources);
                if (_asyncCompiler != null)
                {
                    counters.AsyncMaxQueueDepth = Math.Max(counters.AsyncMaxQueueDepth, _asyncCompiler.MaxQueueDepth);
                }

                return counters;
            }
        }

        public bool InstructionFrequencyEnabled
        {
            get => _instructionFrequency.Enabled;
            set => _instructionFrequency.Enabled = value;
        }

        public M68kInstructionFrequencySnapshot CaptureInstructionFrequency()
            => _instructionFrequency.CaptureSnapshot();

        public void ResetInstructionFrequency()
            => _instructionFrequency.Reset();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _asyncCompiler?.Dispose();
            if (_amigaBus != null)
            {
                _amigaBus.JitEligibleMemoryWritten -= InvalidateWrittenCodeRange;
            }
        }

        private static bool IsV2EnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsV2Tier3EnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2_TIER3");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsV2MemoryReadEnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2_MEMORY_READ");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsV2BusAccessEnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2_BUS_ACCESS");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsV2FastReadEnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2_FAST_READ");
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
                IsV2EnabledByDefault();
        }

        private static bool IsV2BusGraphEnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2_BUS_GRAPH");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAsyncJitEnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAsyncJitSyncFallbackEnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_SYNC_FALLBACK");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetAsyncJitWorkerCount()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_ASYNC_WORKERS");
            if (int.TryParse(value, out var requested))
            {
                return Math.Clamp(requested, 1, Math.Max(1, Environment.ProcessorCount));
            }

            return Math.Min(2, Math.Max(1, Environment.ProcessorCount - 2));
        }

        private static bool IsV2InlineZeroWaitMemoryEnabled()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2_INLINE_ZERO_WAIT");
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        public int ExecuteInstruction()
        {
            var startCycles = State.Cycles;
            return ExecuteInstructions(1, null, NoOpBoundary.Instance) == 0
                ? 0
                : (int)(State.Cycles - startCycles);
        }

        public int ExecuteInstructions(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
        {
            ArgumentNullException.ThrowIfNull(boundary);
            DrainAsyncCompileResults();
            var instructions = 0;
            while (!State.Halted &&
                instructions < maxInstructions &&
                (!targetCycle.HasValue || State.Cycles < targetCycle.Value))
            {
                DrainAsyncCompileResults();
                if (State.Stopped)
                {
                    if (!ExecuteStoppedInstruction(boundary, targetCycle, out var stoppedFastForwardedCycles))
                    {
                        break;
                    }

                    if (stoppedFastForwardedCycles > 0)
                    {
                        _counters.StoppedFastForwards++;
                        _counters.StoppedFastForwardCycles += Math.Max(0, stoppedFastForwardedCycles - 1);
                    }

                    instructions++;
                    continue;
                }

                var traced = TryExecuteTrace(maxInstructions - instructions, targetCycle, boundary);
                if (traced > 0)
                {
                    instructions += traced;
                    continue;
                }

                if (!ExecuteFallbackInstruction(boundary))
                {
                    break;
                }

                instructions++;
            }

            DrainAsyncCompileResults();
            return instructions;
        }

        public void Reset(uint programCounter, uint stackPointer)
        {
            _fallback.Reset(programCounter, stackPointer);
            ClearRuntimeState();
        }

        public void BeginSubroutine(uint address, uint stackPointer, uint returnAddress)
        {
            _fallback.BeginSubroutine(address, stackPointer, returnAddress);
            ClearRuntimeState();
        }

        public void RequestInterrupt(int level, uint vectorAddress)
        {
            _fallback.RequestInterrupt(level, vectorAddress);
        }

        private int TryExecuteTrace(
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary,
            bool allowV2TraceHandoff = true)
        {
            var pc = Normalize(State.ProgramCounter);
            if (_amigaBus != null && _amigaBus.IsChipRamAddress(pc))
            {
                RemoveTrace(pc);
                return 0;
            }

            if (!_traces.TryGetValue(pc, out var trace))
            {
                return 0;
            }

            if (!TryValidateTraceGeneration(pc, ref trace))
            {
                return 0;
            }

            var v2BatchExecuted = TryExecuteV2TraceBatch(trace, maxInstructions, targetCycle, boundary, allowV2TraceHandoff);
            if (v2BatchExecuted.HasValue)
            {
                return v2BatchExecuted.Value;
            }

            var pureBatchExecuted = TryExecutePureTraceBatch(trace, maxInstructions, targetCycle, boundary);
            if (pureBatchExecuted.HasValue)
            {
                return pureBatchExecuted.Value;
            }

            var compiled = trace.Compiled;
            if (compiled == null)
            {
                return 0;
            }

            _counters.TraceHits++;
            var executed = compiled(this, maxInstructions, targetCycle ?? long.MaxValue, boundary);
            if (executed == 0)
            {
                _counters.SideExits++;
            }
            else
            {
                RecordTraceInstructionKinds(trace, executed);
            }

            return executed;
        }

        private bool TryValidateTraceGeneration(uint pc, ref TraceEntry trace)
        {
            if (_amigaBus == null ||
                _amigaBus.CodeRangeGenerationMatches(
                    trace.CodeStart,
                    trace.ByteLength,
                    trace.StartGeneration,
                    trace.EndGeneration))
            {
                return true;
            }

            if (TraceCodeWordsMatch(trace))
            {
                trace = RefreshTraceGenerations(trace);
                _traces[pc] = trace;
                return true;
            }

            RemoveTrace(pc);
            _counters.GenerationMismatches++;
            _counters.GenerationGuardExits++;
            return false;
        }

        private int? TryExecuteV2TraceBatch(
            TraceEntry trace,
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary,
            bool allowV2TraceHandoff)
        {
            if (IsRomTraceRoot(trace.Root))
            {
                return null;
            }

            if (!targetCycle.HasValue ||
                !CanUseV2TraceForInstructionBudget(trace, maxInstructions) ||
                trace.V2Compiled == null ||
                !trace.V2PureCpuBatchEligible ||
                _v2DisabledRoots.Contains(trace.Root) ||
                _instructionFrequency.Enabled ||
                (_amigaBus != null && _amigaBus.HasHostTrapStub(trace.Root)) ||
                boundary is not IM68kPureCpuTraceBatchBoundary batchBoundary)
            {
                return TryExecuteV2BusAccessTraceBatch(trace, maxInstructions, targetCycle, boundary, out _);
            }

            var previousCycle = State.Cycles;
            if (!batchBoundary.TryBeginPureCpuTraceBatch(State, targetCycle.Value, out var batchTargetCycle))
            {
                return null;
            }

            _counters.TraceHits++;
            _counters.V2TraceHits++;
            var executed = ExecuteV2PureTraceBatchChain(
                trace,
                maxInstructions,
                batchTargetCycle,
                boundary,
                out var outOfBlockHandoffTrace);
            if (executed == 0)
            {
                _counters.SideExits++;
                _counters.V2SideExits++;
                _counters.V2SideExitEntryMismatch++;
                _counters.PureTraceBatchSideExits++;
                RecordV2ZeroInstructionExit(trace.Root);
                return 0;
            }

            batchBoundary.AfterPureCpuTraceBatch(previousCycle, State.Cycles, executed);
            _counters.DirectIlInstructions += executed;
            _counters.DirectCpuIlInstructions += executed;
            _counters.PureTraceBatchExecutions++;
            _counters.PureTraceBatchInstructions += executed;
            _counters.PureTraceBatchBoundaryCallsSaved += Math.Max(0, executed - 1);
            RecordTraceBatchDiagnostics(_pureTraceBatchLengthBuckets, _pureTraceBatchWakeSources, executed, boundary);

            if (allowV2TraceHandoff &&
                outOfBlockHandoffTrace.V2Compiled != null &&
                executed < maxInstructions &&
                State.Cycles < targetCycle.Value &&
                !State.Halted &&
                !State.Stopped)
            {
                _counters.V2TraceHandoffAttempts++;
                var handoffExecuted = TryExecuteV2BusAccessTraceBatch(
                    outOfBlockHandoffTrace,
                    maxInstructions - executed,
                    targetCycle,
                    boundary,
                    out var handoffBlockReason);
                if (handoffExecuted is int handoffCount && handoffCount > 0)
                {
                    _counters.V2TraceHandoffExecutions++;
                    _counters.V2TraceHandoffInstructions += handoffCount;
                    return executed + handoffCount;
                }

                RecordV2TraceHandoffBlock(
                    handoffExecuted.HasValue ? "execute-zero" : handoffBlockReason,
                    outOfBlockHandoffTrace.Root);
            }

            return executed;
        }

        private int ExecuteV2PureTraceBatchChain(
            TraceEntry trace,
            int maxInstructions,
            long batchTargetCycle,
            IM68kInstructionBoundary boundary,
            out TraceEntry outOfBlockHandoffTrace)
        {
            outOfBlockHandoffTrace = default;
            var totalExecuted = 0;
            var remaining = maxInstructions;
            var current = trace;
            var pendingHandoff = false;
            while (remaining > 0 &&
                State.Cycles < batchTargetCycle &&
                !State.Halted &&
                !State.Stopped &&
                current.V2Compiled != null)
            {
                if (totalExecuted != 0)
                {
                    _counters.TraceHits++;
                    _counters.V2TraceHits++;
                }

                ClearPendingV2OutOfBlockBranch();
                var executed = current.V2Compiled(this, remaining, batchTargetCycle, boundary);
                if (executed == 0)
                {
                    return totalExecuted;
                }

                totalExecuted += executed;
                remaining -= executed;
                if (pendingHandoff)
                {
                    _counters.V2TraceHandoffExecutions++;
                    _counters.V2TraceHandoffInstructions += executed;
                    pendingHandoff = false;
                }

                MaybePromoteV2Trace(current, executed);
                if (!_v2PendingOutOfBlockBranch)
                {
                    if (State.Cycles >= batchTargetCycle && remaining > 0)
                    {
                        RecordV2TraceHandoffBlock("target-cycle", Normalize(State.ProgramCounter), countFailure: false);
                    }

                    break;
                }

                var pc = Normalize(_v2PendingOutOfBlockBranchTarget);
                if (remaining <= 0 ||
                    State.Halted ||
                    State.Stopped)
                {
                    RecordV2OutOfBlockSideExit(
                        _v2PendingOutOfBlockBranchRoot,
                        _v2PendingOutOfBlockBranchTarget,
                        _v2PendingOutOfBlockBranchCodeStart,
                        _v2PendingOutOfBlockBranchByteLength,
                        _v2PendingOutOfBlockBranchIsFallthrough);
                    ClearPendingV2OutOfBlockBranch();
                    break;
                }

                if (State.Cycles >= batchTargetCycle)
                {
                    RecordV2TraceHandoffBlock("target-cycle", pc, countFailure: false);
                    ClearPendingV2OutOfBlockBranch();
                    break;
                }

                _counters.V2TraceHandoffAttempts++;
                if (!TryGetV2PureHandoffTrace(current.Root, pc, out var next, out var pureBlockReason))
                {
                    _ = TryGetV2BusHandoffTrace(
                        current.Root,
                        pc,
                        remaining,
                        batchTargetCycle,
                        boundary,
                        out outOfBlockHandoffTrace,
                        out var busBlockReason);
                    if (outOfBlockHandoffTrace.V2Compiled == null)
                    {
                        RecordV2TraceHandoffBlock(
                            busBlockReason.Length == 0 ? pureBlockReason : busBlockReason,
                            pc);
                        RecordV2OutOfBlockSideExit(
                            _v2PendingOutOfBlockBranchRoot,
                            _v2PendingOutOfBlockBranchTarget,
                            _v2PendingOutOfBlockBranchCodeStart,
                            _v2PendingOutOfBlockBranchByteLength,
                            _v2PendingOutOfBlockBranchIsFallthrough);
                    }

                    ClearPendingV2OutOfBlockBranch();
                    break;
                }

                current = next;
                pendingHandoff = true;
            }

            return totalExecuted;
        }

        private bool TryGetV2PureHandoffTrace(uint sourceRoot, uint pc, out TraceEntry trace, out string blockReason)
        {
            trace = default;
            blockReason = string.Empty;
            if (Normalize(State.ProgramCounter) != pc)
            {
                blockReason = "pc-mismatch";
                return false;
            }

            if (TryGetCachedV2HandoffTrace(sourceRoot, pc, out trace))
            {
                if (!trace.V2PureCpuBatchEligible)
                {
                    blockReason = "not-pure";
                    return false;
                }

                return true;
            }

            if (!_traces.TryGetValue(pc, out trace))
            {
                if (IsPendingV2GraphHoleTarget(pc))
                {
                    if (_asyncJitEnabled &&
                        TryMakeV2HandoffTargetTrace(pc, out trace, out blockReason))
                    {
                        CacheV2HandoffTrace(sourceRoot, pc, trace);
                        if (trace.V2PureCpuBatchEligible)
                        {
                            return true;
                        }

                        blockReason = "not-pure";
                        return false;
                    }

                    if (!string.IsNullOrEmpty(blockReason))
                    {
                        return false;
                    }

                    blockReason = "no-trace-graph-hole";
                    return false;
                }

                if (TryMakeV2HandoffTargetTrace(pc, out trace, out blockReason) &&
                    trace.V2PureCpuBatchEligible)
                {
                    CacheV2HandoffTrace(sourceRoot, pc, trace);
                    return true;
                }

                if (string.IsNullOrEmpty(blockReason))
                {
                    blockReason = "no-trace-unqueued";
                }

                return false;
            }

            if (trace.V2Compiled == null)
            {
                blockReason = "no-v2";
                return false;
            }

            if (!trace.V2PureCpuBatchEligible)
            {
                blockReason = "not-pure";
                return false;
            }

            if (_v2DisabledRoots.Contains(trace.Root))
            {
                blockReason = GetV2DisabledRootBlockReason(trace.Root);
                return false;
            }

            if (_amigaBus != null && _amigaBus.HasHostTrapStub(trace.Root))
            {
                blockReason = "host-trap";
                return false;
            }

            if (!TryValidateTraceGeneration(pc, ref trace))
            {
                blockReason = "generation";
                return false;
            }

            CacheV2HandoffTrace(sourceRoot, pc, trace);
            return true;
        }

        private bool TryGetV2BusHandoffTrace(
            uint sourceRoot,
            uint pc,
            int remaining,
            long batchTargetCycle,
            IM68kInstructionBoundary boundary,
            out TraceEntry trace,
            out string blockReason)
        {
            trace = default;
            blockReason = string.Empty;
            if (!_v2BusAccessEnabled)
            {
                blockReason = "bus-disabled";
                return false;
            }

            if (remaining <= 0)
            {
                blockReason = "max-instructions";
                return false;
            }

            if (State.Cycles >= batchTargetCycle)
            {
                blockReason = "target-cycle";
                return false;
            }

            if (State.Halted)
            {
                blockReason = "halted";
                return false;
            }

            if (State.Stopped)
            {
                blockReason = "stopped";
                return false;
            }

            if (boundary is not IM68kBusAccessTraceBatchBoundary)
            {
                blockReason = "boundary";
                return false;
            }

            if (Normalize(State.ProgramCounter) != pc)
            {
                blockReason = "pc-mismatch";
                return false;
            }

            if (TryGetCachedV2HandoffTrace(sourceRoot, pc, out trace))
            {
                if (trace.V2PureCpuBatchEligible)
                {
                    blockReason = "pure";
                    return false;
                }

                return true;
            }

            if (!_traces.TryGetValue(pc, out trace))
            {
                if (IsPendingV2GraphHoleTarget(pc))
                {
                    if (_asyncJitEnabled &&
                        TryMakeV2HandoffTargetTrace(pc, out trace, out blockReason))
                    {
                        CacheV2HandoffTrace(sourceRoot, pc, trace);
                        if (!trace.V2PureCpuBatchEligible)
                        {
                            return true;
                        }

                        blockReason = "pure";
                        return false;
                    }

                    if (!string.IsNullOrEmpty(blockReason))
                    {
                        return false;
                    }

                    blockReason = "no-trace-graph-hole";
                    return false;
                }

                if (TryMakeV2HandoffTargetTrace(pc, out trace, out blockReason) &&
                    !trace.V2PureCpuBatchEligible)
                {
                    CacheV2HandoffTrace(sourceRoot, pc, trace);
                    return true;
                }

                if (string.IsNullOrEmpty(blockReason))
                {
                    blockReason = "no-trace-unqueued";
                }

                return false;
            }

            if (trace.V2Compiled == null)
            {
                blockReason = "no-v2";
                return false;
            }

            if (trace.V2PureCpuBatchEligible)
            {
                blockReason = "pure";
                return false;
            }

            if (_v2DisabledRoots.Contains(trace.Root))
            {
                blockReason = GetV2DisabledRootBlockReason(trace.Root);
                return false;
            }

            if (_amigaBus != null && _amigaBus.HasHostTrapStub(trace.Root))
            {
                blockReason = "host-trap";
                return false;
            }

            if (!TryValidateTraceGeneration(pc, ref trace))
            {
                blockReason = "generation";
                return false;
            }

            CacheV2HandoffTrace(sourceRoot, pc, trace);
            return true;
        }

        private bool TryGetCachedV2HandoffTrace(uint sourceRoot, uint target, out TraceEntry trace)
        {
            trace = default;
            var key = GetV2HandoffCacheKey(sourceRoot, target);
            if (!_v2HandoffTraceCache.TryGetValue(key, out var cached))
            {
                return false;
            }

            if (!_traces.TryGetValue(target, out trace) ||
                trace.Root != cached.Root ||
                trace.V2Compiled == null ||
                _v2DisabledRoots.Contains(trace.Root) ||
                (_amigaBus != null && _amigaBus.HasHostTrapStub(trace.Root)) ||
                !TryValidateTraceGeneration(target, ref trace))
            {
                _v2HandoffTraceCache.Remove(key);
                return false;
            }

            _v2HandoffTraceCache[key] = trace;
            return true;
        }

        private void CacheV2HandoffTrace(uint sourceRoot, uint target, TraceEntry trace)
        {
            if (trace.V2Compiled != null)
            {
                _v2HandoffQueuePressures.Remove(Normalize(target));
                _v2HandoffTraceCache[GetV2HandoffCacheKey(sourceRoot, target)] = trace;
            }
        }

        private static ulong GetV2HandoffCacheKey(uint sourceRoot, uint target)
            => ((ulong)Normalize(sourceRoot) << 24) | Normalize(target);

        private bool IsPendingV2GraphHoleTarget(uint target)
        {
            if (!_v2PendingOutOfBlockBranch ||
                _v2PendingOutOfBlockBranchByteLength <= 0)
            {
                return false;
            }

            var codeStart = Normalize(_v2PendingOutOfBlockBranchCodeStart);
            var offset = Normalize(target - codeStart);
            return offset < (uint)_v2PendingOutOfBlockBranchByteLength;
        }

        private bool TryMakeV2HandoffTargetTrace(uint target, out TraceEntry trace, out string blockReason)
        {
            trace = default;
            blockReason = string.Empty;
            target = Normalize(target);
            if (!_v2Enabled)
            {
                blockReason = "no-trace-v2-disabled";
                return false;
            }

            if (_amigaBus == null)
            {
                blockReason = "no-trace-no-bus";
                return false;
            }

            if (_amigaBus.IsChipRamAddress(target))
            {
                blockReason = "no-trace-chip";
                return false;
            }

            if (_amigaBus.HasHostTrapStub(target))
            {
                blockReason = "no-trace-host";
                return false;
            }

            if (_v2DisabledRoots.Contains(target))
            {
                blockReason = "no-trace-" + GetV2DisabledRootBlockReason(target);
                return false;
            }

            DrainAsyncCompileResults();
            if (TryGetInstalledV2HandoffTargetTrace(target, out trace))
            {
                _v2HandoffQueuePressures.Remove(target);
                blockReason = "no-trace-installed-drained";
                return true;
            }

            if (!M68kDecoder.TryDecode(_amigaBus, target, out var instruction, out _))
            {
                blockReason = "no-trace-decode";
                return false;
            }

            if (!IsV2Instruction(instruction, out _))
            {
                blockReason = "no-trace-unsupported";
                return false;
            }

            if (!_asyncJitEnabled)
            {
                blockReason = "no-trace-sync-disabled";
                return false;
            }

            var handoffPressure = IncrementV2HandoffQueuePressure(target);
            if (TryQueueAsyncTraceCompilation(
                target,
                V2TraceTier.Tier0,
                M68kJitCompileReason.Handoff,
                CompileThreshold * 8 + Math.Min(handoffPressure, 4096) * 64,
                branchPressurePromotion: false,
                out var queueResult))
            {
                blockReason = queueResult switch
                {
                    M68kAsyncJitQueueResult.Queued => "no-trace-queued",
                    M68kAsyncJitQueueResult.DedupedPending => "no-trace-queued-pending",
                    M68kAsyncJitQueueResult.DedupedCompiling => "no-trace-compiling",
                    _ => "no-trace-queue-dropped"
                };
                DrainAsyncCompileResults();
                if (TryGetInstalledV2HandoffTargetTrace(target, out trace))
                {
                    _v2HandoffQueuePressures.Remove(target);
                    blockReason = "no-trace-installed-drained";
                    return true;
                }

                _counters.V2TraceHandoffQueuedNotReady++;
                return false;
            }

            blockReason = "no-trace-snapshot-failed";
            if (!_asyncJitSyncFallbackEnabled)
            {
                return false;
            }

            if (!TryCompileTrace(target, M68kJitCompileReason.Handoff, out trace))
            {
                _blacklist[target] = BlacklistHits;
                _counters.BlacklistCount++;
                blockReason = "no-trace-compile-failed";
                return false;
            }

            AddTrace(trace);
            _v2HandoffQueuePressures.Remove(target);
            _counters.CompiledTraces++;
            if (trace.V2Compiled == null)
            {
                blockReason = "no-trace-compiled-no-v2";
                return false;
            }

            blockReason = "no-trace-compiled";
            return true;
        }

        private bool TryGetInstalledV2HandoffTargetTrace(uint target, out TraceEntry trace)
        {
            trace = default;
            target = Normalize(target);
            if (!_traces.TryGetValue(target, out trace) ||
                trace.V2Compiled == null ||
                _v2DisabledRoots.Contains(trace.Root) ||
                (_amigaBus != null && _amigaBus.HasHostTrapStub(trace.Root)) ||
                !TryValidateTraceGeneration(target, ref trace))
            {
                return false;
            }

            return true;
        }

        private int IncrementV2HandoffQueuePressure(uint target)
        {
            target = Normalize(target);
            _v2HandoffQueuePressures.TryGetValue(target, out var pressure);
            pressure = Math.Min(16_384, pressure + 1);
            _v2HandoffQueuePressures[target] = pressure;
            return pressure;
        }

        private int? TryExecuteV2BusAccessTraceBatch(
            TraceEntry trace,
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary,
            out string unavailableReason)
        {
            unavailableReason = string.Empty;
            if (trace.V2Compiled == null)
            {
                unavailableReason = "no-v2";
                return null;
            }

            if (IsRomTraceRoot(trace.Root))
            {
                unavailableReason = "rom-boundary";
                return null;
            }

            if (trace.V2PureCpuBatchEligible)
            {
                unavailableReason = "pure";
                return null;
            }

            if (_v2DisabledRoots.Contains(trace.Root))
            {
                unavailableReason = GetV2DisabledRootBlockReason(trace.Root);
                return null;
            }

            if (!targetCycle.HasValue)
            {
                unavailableReason = "no-target-cycle";
                return null;
            }

            if (!CanUseV2TraceForInstructionBudget(trace, maxInstructions))
            {
                unavailableReason = "max-instructions";
                return null;
            }

            if (_instructionFrequency.Enabled)
            {
                unavailableReason = "frequency";
                return null;
            }

            if (_amigaBus != null && _amigaBus.HasHostTrapStub(trace.Root))
            {
                unavailableReason = "host-trap";
                return null;
            }

            if (boundary is not IM68kBusAccessTraceBatchBoundary batchBoundary)
            {
                unavailableReason = "boundary";
                return null;
            }

            var previousCycle = State.Cycles;
            if (!batchBoundary.TryBeginBusAccessTraceBatch(State, targetCycle.Value, out var batchTargetCycle))
            {
                unavailableReason = "boundary-refused";
                return null;
            }

            _counters.TraceHits++;
            _counters.V2TraceHits++;
            var executed = ExecuteV2BusAccessTraceBatchChain(
                trace,
                maxInstructions,
                batchTargetCycle,
                boundary,
                out var directCpuInstructions,
                out var directMemoryInstructions);
            if (executed == 0)
            {
                _counters.SideExits++;
                _counters.V2SideExits++;
                _counters.V2SideExitEntryMismatch++;
                RecordV2ZeroInstructionExit(trace.Root);
                return 0;
            }

            batchBoundary.AfterBusAccessTraceBatch(previousCycle, State.Cycles, executed);
            _counters.DirectIlInstructions += executed;
            _counters.DirectCpuIlInstructions += directCpuInstructions;
            _counters.DirectMemoryIlInstructions += directMemoryInstructions;
            _counters.V2BusAccessBatchExecutions++;
            _counters.V2BusAccessBatchInstructions += executed;
            _counters.V2BusAccessBatchBoundaryCallsSaved += Math.Max(0, executed - 1);
            RecordTraceBatchDiagnostics(_v2BusAccessBatchLengthBuckets, _v2BusAccessBatchWakeSources, executed, boundary);
            return executed;
        }

        private static bool CanUseV2TraceForInstructionBudget(TraceEntry trace, int maxInstructions)
        {
            if (maxInstructions > 1)
            {
                return true;
            }

            return maxInstructions == 1 && trace.Compiled == null;
        }

        private static bool IsRomTraceRoot(uint root)
            => root is >= 0x00F8_0000 and < 0x0100_0000;

        private int ExecuteV2BusAccessTraceBatchChain(
            TraceEntry trace,
            int maxInstructions,
            long batchTargetCycle,
            IM68kInstructionBoundary boundary,
            out int directCpuInstructions,
            out int directMemoryInstructions)
        {
            var totalExecuted = 0;
            var remaining = maxInstructions;
            var current = trace;
            var pendingHandoff = false;
            directCpuInstructions = 0;
            directMemoryInstructions = 0;
            while (remaining > 0 &&
                State.Cycles < batchTargetCycle &&
                !State.Halted &&
                !State.Stopped &&
                current.V2Compiled != null)
            {
                if (totalExecuted != 0)
                {
                    _counters.TraceHits++;
                    _counters.V2TraceHits++;
                }

                ClearPendingV2OutOfBlockBranch();
                var currentIsPure = current.V2PureCpuBatchEligible;
                var executed = current.V2Compiled(this, remaining, batchTargetCycle, boundary);
                if (executed == 0)
                {
                    if (pendingHandoff)
                    {
                        RecordV2TraceHandoffBlock("entry-zero", current.Root);
                    }

                    return totalExecuted;
                }

                if (pendingHandoff)
                {
                    _counters.V2TraceHandoffExecutions++;
                    _counters.V2TraceHandoffInstructions += executed;
                    pendingHandoff = false;
                }

                totalExecuted += executed;
                remaining -= executed;
                if (currentIsPure)
                {
                    directCpuInstructions += executed;
                }
                else
                {
                    directMemoryInstructions += executed;
                }

                MaybePromoteV2Trace(current, executed);
                if (!_v2PendingOutOfBlockBranch)
                {
                    if (State.Cycles >= batchTargetCycle && remaining > 0)
                    {
                        RecordV2TraceHandoffBlock("target-cycle", Normalize(State.ProgramCounter), countFailure: false);
                    }

                    break;
                }

                var pc = Normalize(_v2PendingOutOfBlockBranchTarget);
                if (State.Cycles >= batchTargetCycle)
                {
                    RecordV2TraceHandoffBlock("target-cycle", pc, countFailure: false);
                    ClearPendingV2OutOfBlockBranch();
                    break;
                }

                if (remaining <= 0 ||
                    State.Halted ||
                    State.Stopped)
                {
                    ClearPendingV2OutOfBlockBranch();
                    break;
                }

                _counters.V2TraceHandoffAttempts++;
                if (!TryGetV2BusHandoffTrace(
                    current.Root,
                    pc,
                    remaining,
                    batchTargetCycle,
                    boundary,
                    out var next,
                    out var busBlockReason) &&
                    !TryGetV2PureHandoffTrace(current.Root, pc, out next, out var pureBlockReason))
                {
                    var blockReason = SelectV2BusThenPureHandoffBlockReason(busBlockReason, pureBlockReason);
                    RecordV2TraceHandoffBlock(blockReason, pc);
                    RecordV2OutOfBlockSideExit(
                        _v2PendingOutOfBlockBranchRoot,
                        _v2PendingOutOfBlockBranchTarget,
                        _v2PendingOutOfBlockBranchCodeStart,
                        _v2PendingOutOfBlockBranchByteLength,
                        _v2PendingOutOfBlockBranchIsFallthrough);
                    ClearPendingV2OutOfBlockBranch();
                    break;
                }

                ClearPendingV2OutOfBlockBranch();
                current = next;
                pendingHandoff = true;
            }

            return totalExecuted;
        }

        private static string SelectV2BusThenPureHandoffBlockReason(string busBlockReason, string pureBlockReason)
        {
            if (string.IsNullOrEmpty(busBlockReason))
            {
                return pureBlockReason;
            }

            if (string.IsNullOrEmpty(pureBlockReason))
            {
                return busBlockReason;
            }

            if (busBlockReason == "pure")
            {
                return pureBlockReason;
            }

            return pureBlockReason == "not-pure"
                ? busBlockReason
                : pureBlockReason;
        }

        private int? TryExecutePureTraceBatch(
            TraceEntry trace,
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary)
        {
            if (IsRomTraceRoot(trace.Root))
            {
                return null;
            }

            if (!targetCycle.HasValue ||
                maxInstructions <= 1 ||
                !trace.PureCpuBatchEligible ||
                boundary is not IM68kPureCpuTraceBatchBoundary batchBoundary)
            {
                return null;
            }

            var pureCompiled = trace.PureCompiled;
            if (pureCompiled == null)
            {
                return null;
            }

            var previousCycle = State.Cycles;
            if (!batchBoundary.TryBeginPureCpuTraceBatch(State, targetCycle.Value, out var batchTargetCycle))
            {
                return null;
            }

            _counters.TraceHits++;
            var executed = pureCompiled(this, maxInstructions, batchTargetCycle, boundary);
            if (executed == 0)
            {
                _counters.SideExits++;
                _counters.PureTraceBatchSideExits++;
                return 0;
            }

            batchBoundary.AfterPureCpuTraceBatch(previousCycle, State.Cycles, executed);
            RecordTraceInstructionKinds(trace, executed);
            _counters.PureTraceBatchExecutions++;
            _counters.PureTraceBatchInstructions += executed;
            _counters.PureTraceBatchBoundaryCallsSaved += Math.Max(0, executed - 1);
            RecordTraceBatchDiagnostics(_pureTraceBatchLengthBuckets, _pureTraceBatchWakeSources, executed, boundary);
            return executed;
        }

        private bool ExecuteFallbackInstruction(IM68kInstructionBoundary boundary)
        {
            if (!boundary.BeforeInstruction())
            {
                return false;
            }

            var previousCycle = State.Cycles;
            var root = Normalize(State.ProgramCounter);
            _fallback.ExecuteInstruction();
            boundary.AfterInstruction(previousCycle, State.Cycles);
            _counters.FallbackInstructions++;
            ObserveHotRoot(root);
            return true;
        }

        private bool ExecuteStoppedInstruction(
            IM68kInstructionBoundary boundary,
            long? targetCycle,
            out long fastForwardedCycles)
        {
            fastForwardedCycles = 0;
            if (targetCycle.HasValue &&
                boundary is IM68kStoppedCpuFastForwardBoundary stoppedBoundary)
            {
                return stoppedBoundary.TryFastForwardStoppedInstruction(
                    State,
                    targetCycle.Value,
                    out fastForwardedCycles);
            }

            if (!boundary.BeforeInstruction())
            {
                return false;
            }

            var previousCycle = State.Cycles;
            State.Cycles++;
            boundary.AfterInstruction(previousCycle, State.Cycles);
            _counters.FallbackInstructions++;
            return true;
        }

        private void ObserveHotRoot(uint pc)
        {
            if (_amigaBus == null ||
                _traces.ContainsKey(pc) ||
                _amigaBus.IsChipRamAddress(pc))
            {
                return;
            }

            if (_blacklist.TryGetValue(pc, out var remaining))
            {
                remaining--;
                if (remaining <= 0)
                {
                    _blacklist.Remove(pc);
                }
                else
                {
                    _blacklist[pc] = remaining;
                }

                return;
            }

            _hotCounters.TryGetValue(pc, out var count);
            count++;
            if (count < CompileThreshold)
            {
                _hotCounters[pc] = count;
                return;
            }

            _hotCounters.Remove(pc);
            if (_asyncJitEnabled)
            {
                if (TryQueueAsyncTraceCompilation(
                    pc,
                    V2TraceTier.Tier0,
                    M68kJitCompileReason.HotRoot,
                    count + CompileThreshold,
                    branchPressurePromotion: false))
                {
                    return;
                }

                if (!_asyncJitSyncFallbackEnabled)
                {
                    _blacklist[pc] = BlacklistHits;
                    _counters.BlacklistCount++;
                    return;
                }
            }

            if (!TryCompileTrace(pc, out var trace))
            {
                _blacklist[pc] = BlacklistHits;
                _counters.BlacklistCount++;
                return;
            }

            AddTrace(trace);
            _counters.CompiledTraces++;
        }

        private bool TryCompileTrace(uint root, out TraceEntry trace)
            => TryCompileTrace(root, M68kJitCompileReason.HotRoot, out trace);

        private bool TryCompileTrace(uint root, M68kJitCompileReason reason, out TraceEntry trace)
        {
            trace = default;
            if (!TryCreateTraceCompilationPlan(
                root,
                V2TraceTier.Tier0,
                reason,
                out var plan))
            {
                return false;
            }

            trace = CompileTraceFromPlan(plan);
            RecordTracePlanCompiledCounters(plan);
            return true;
        }

        private bool TryCreateTraceCompilationPlan(
            uint root,
            V2TraceTier tier,
            M68kJitCompileReason reason,
            out M68kTraceCompilationPlan plan)
        {
            plan = default;
            if (_amigaBus == null || _amigaBus.IsChipRamAddress(root))
            {
                return false;
            }

            Span<M68kDecodedInstruction> instructions = stackalloc M68kDecodedInstruction[MaxTraceInstructions];
            var count = 0;
            var pc = root;
            var byteCount = 0;
            while (count < MaxTraceInstructions && byteCount < MaxTraceBytes)
            {
                if (_amigaBus.IsChipRamAddress(pc))
                {
                    break;
                }

                if (!M68kDecoder.TryDecode(_amigaBus, pc, out var instruction, out var decodeReason))
                {
                    CountBailout(decodeReason);
                    break;
                }

                if (byteCount + instruction.Length > MaxTraceBytes)
                {
                    break;
                }

                instructions[count++] = instruction;
                byteCount += instruction.Length;
                if (instruction.StopsTrace)
                {
                    break;
                }

                pc = Normalize(pc + (uint)instruction.Length);
            }

            if (count == 0)
            {
                return false;
            }

            var traceInstructions = instructions[..count].ToArray();
            var pureCpuBatchEligible = IsPureCpuBatchEligible(traceInstructions);
            var v2Trace = _v2Enabled && TryCreateV2TracePlan(root, tier, out var compiledV2Trace)
                ? compiledV2Trace
                : default;
            var guardStart = root;
            var guardEnd = Normalize(root + (uint)byteCount);
            if (!v2Trace.IsEmpty)
            {
                ExtendCodeRange(ref guardStart, ref guardEnd, v2Trace.CodeStart, v2Trace.ByteLength);
            }

            var guardByteLength = GetCodeRangeByteLength(guardStart, guardEnd);
            var codeWords = CaptureTraceWords(guardStart, guardByteLength);
            CaptureTraceInstructionKindPrefixes(
                traceInstructions,
                out var directInstructionPrefixes,
                out var helperInstructionPrefixes,
                out var directCpuInstructionPrefixes,
                out var directMemoryInstructionPrefixes,
                out var specializedHelperInstructionPrefixes);
            var startGeneration = _amigaBus.GetCodePageGeneration(guardStart);
            var endGeneration = _amigaBus.GetCodePageGeneration(Normalize(guardStart + (uint)guardByteLength - 1u));
            plan = new M68kTraceCompilationPlan(
                root,
                tier,
                reason,
                guardStart,
                guardByteLength,
                startGeneration,
                endGeneration,
                codeWords,
                traceInstructions,
                pureCpuBatchEligible,
                directInstructionPrefixes,
                helperInstructionPrefixes,
                directCpuInstructionPrefixes,
                directMemoryInstructionPrefixes,
                specializedHelperInstructionPrefixes,
                v2Trace,
                _v2BusGraphEnabled);
            return true;
        }

        private static TraceEntry CompileTraceFromPlan(M68kTraceCompilationPlan plan)
        {
            var compileV2Only = ShouldCompileV2OnlyTrace(plan);
            var compiled = compileV2Only
                ? null
                : Compile(plan.Root, plan.TraceInstructions, emitBoundaryCalls: true);
            var pureCompiled = !compileV2Only && plan.PureCpuBatchEligible
                ? Compile(plan.Root, plan.TraceInstructions, emitBoundaryCalls: false)
                : null;
            var v2Compiled = plan.V2Trace.IsEmpty
                ? null
                : CompileV2(
                    plan.Root,
                    plan.V2Trace.Tier,
                    plan.V2Trace.Instructions,
                    plan.V2Trace.CodeStart,
                    plan.V2Trace.ByteLength,
                    plan.V2Trace.PureCpuBatchEligible,
                    plan.V2Trace.FastReadOnlyBatchEligible,
                    plan.V2BusGraphEnabled);
            return new TraceEntry(
                plan.Root,
                plan.CodeStart,
                plan.ByteLength,
                plan.StartGeneration,
                plan.EndGeneration,
                plan.CodeWords,
                plan.DirectInstructionPrefixes,
                plan.HelperInstructionPrefixes,
                plan.DirectCpuInstructionPrefixes,
                plan.DirectMemoryInstructionPrefixes,
                plan.SpecializedHelperInstructionPrefixes,
                compiled,
                plan.PureCpuBatchEligible,
                pureCompiled,
                v2Compiled,
                plan.V2Trace.Tier,
                plan.V2Trace.InstructionCount,
                plan.V2Trace.HasInternalLoop,
                plan.V2Trace.PureCpuBatchEligible);
        }

        private static bool ShouldCompileV2OnlyTrace(M68kTraceCompilationPlan plan)
        {
            if (plan.V2Trace.IsEmpty)
            {
                return false;
            }

            return plan.Reason is M68kJitCompileReason.Handoff or
                M68kJitCompileReason.GraphHole or
                M68kJitCompileReason.BranchPressure;
        }

        private static void PrepareTraceEntry(TraceEntry trace)
        {
            if (trace.Compiled != null)
            {
                PrepareCompiledTrace(trace.Compiled);
            }

            if (trace.PureCompiled != null)
            {
                PrepareCompiledTrace(trace.PureCompiled);
            }

            if (trace.V2Compiled != null)
            {
                PrepareCompiledTrace(trace.V2Compiled);
            }
        }

        private static void PrepareCompiledTrace(CompiledTrace trace)
        {
            try
            {
                RuntimeHelpers.PrepareDelegate(trace);
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void RecordTracePlanCompiledCounters(M68kTraceCompilationPlan plan)
        {
            if (!plan.V2Trace.IsEmpty)
            {
                RecordV2CompiledTrace(plan.V2Trace.Tier);
                if (plan.V2Trace.WorkerGraphExpanded)
                {
                    _counters.V2WorkerExpandedGraphs++;
                    _counters.V2WorkerExpandedGraphInstructions += plan.V2Trace.InstructionCount;
                    _counters.V2WorkerExpandedGraphBytes += plan.V2Trace.ByteLength;
                }

                QueueV2ExternalHandoffTargets(plan);
            }
        }

        private void QueueV2ExternalHandoffTargets(M68kTraceCompilationPlan plan)
        {
            if (!_asyncJitEnabled ||
                _amigaBus == null ||
                plan.V2Trace.IsEmpty ||
                plan.V2Trace.ExternalHandoffTargets == null ||
                plan.V2Trace.ExternalHandoffTargets.Length == 0)
            {
                return;
            }

            var queued = 0;
            foreach (var rawTarget in plan.V2Trace.ExternalHandoffTargets)
            {
                var target = Normalize(rawTarget);
                if (target == Normalize(plan.Root) ||
                    _v2DisabledRoots.Contains(target) ||
                    _amigaBus.IsChipRamAddress(target) ||
                    _amigaBus.HasHostTrapStub(target))
                {
                    continue;
                }

                if (_traces.TryGetValue(target, out var existing))
                {
                    if (TryValidateTraceGeneration(target, ref existing) &&
                        IsV2GraphHoleTargetChainable(existing))
                    {
                        CacheV2HandoffTrace(plan.Root, target, existing);
                    }

                    continue;
                }

                if (!M68kDecoder.TryDecode(_amigaBus, target, out var instruction, out _) ||
                    !IsV2Instruction(instruction, out _))
                {
                    continue;
                }

                var handoffPressure = IncrementV2HandoffQueuePressure(target);
                if (TryQueueAsyncTraceCompilation(
                    target,
                    V2TraceTier.Tier0,
                    M68kJitCompileReason.Handoff,
                    CompileThreshold * 16 +
                        plan.V2Trace.InstructionCount * 16 +
                        Math.Min(handoffPressure, 4096) * 64,
                    branchPressurePromotion: false,
                    out var queueResult) &&
                    queueResult is
                        M68kAsyncJitQueueResult.Queued or
                        M68kAsyncJitQueueResult.DedupedPending or
                        M68kAsyncJitQueueResult.DedupedCompiling)
                {
                    queued++;
                    if (queued >= V2MaxExternalHandoffPrequeues)
                    {
                        return;
                    }
                }
            }
        }

        private static bool TryCreateTraceCompilationPlanFromSnapshot(
            M68kTraceCompilationInput input,
            out M68kTraceCompilationPlan plan)
        {
            plan = default;
            if (input.Snapshot.IsEmpty)
            {
                return false;
            }

            var reader = new M68kSnapshotCodeReader(input.Snapshot);
            Span<M68kDecodedInstruction> instructions = stackalloc M68kDecodedInstruction[MaxTraceInstructions];
            var count = 0;
            var pc = input.Root;
            var byteCount = 0;
            while (count < MaxTraceInstructions && byteCount < MaxTraceBytes)
            {
                if (!reader.ContainsRange(pc, 2) ||
                    !M68kDecoder.TryDecode(reader, pc, out var instruction, out _))
                {
                    break;
                }

                if (byteCount + instruction.Length > MaxTraceBytes ||
                    !reader.ContainsRange(instruction.ProgramCounter, instruction.Length))
                {
                    break;
                }

                instructions[count++] = instruction;
                byteCount += instruction.Length;
                if (instruction.StopsTrace)
                {
                    break;
                }

                pc = Normalize(pc + (uint)instruction.Length);
            }

            if (count == 0)
            {
                return false;
            }

            var traceInstructions = instructions[..count].ToArray();
            var pureCpuBatchEligible = IsPureCpuBatchEligible(traceInstructions);
            var v2Trace = input.Options.V2Enabled &&
                TryCreateV2TracePlanFromSnapshot(reader, input.Root, input.Tier, input.Options, out var compiledV2Trace)
                    ? compiledV2Trace
                    : default;
            var guardStart = input.Root;
            var guardEnd = Normalize(input.Root + (uint)byteCount);
            if (!v2Trace.IsEmpty)
            {
                ExtendCodeRange(ref guardStart, ref guardEnd, v2Trace.CodeStart, v2Trace.ByteLength);
            }

            var guardByteLength = GetCodeRangeByteLength(guardStart, guardEnd);
            if (!reader.ContainsRange(guardStart, guardByteLength) ||
                !reader.TryCaptureWords(guardStart, guardByteLength, out var codeWords) ||
                !reader.TryGetGeneration(guardStart, out var startGeneration) ||
                !reader.TryGetGeneration(Normalize(guardStart + (uint)guardByteLength - 1u), out var endGeneration))
            {
                return false;
            }

            CaptureTraceInstructionKindPrefixes(
                traceInstructions,
                out var directInstructionPrefixes,
                out var helperInstructionPrefixes,
                out var directCpuInstructionPrefixes,
                out var directMemoryInstructionPrefixes,
                out var specializedHelperInstructionPrefixes);
            plan = new M68kTraceCompilationPlan(
                input.Root,
                input.Tier,
                input.Reason,
                guardStart,
                guardByteLength,
                startGeneration,
                endGeneration,
                codeWords,
                traceInstructions,
                pureCpuBatchEligible,
                directInstructionPrefixes,
                helperInstructionPrefixes,
                directCpuInstructionPrefixes,
                directMemoryInstructionPrefixes,
                specializedHelperInstructionPrefixes,
                v2Trace,
                input.Options.V2BusGraphEnabled);
            return true;
        }

        private static bool TryCreateV2PromotionCompilationPlanFromSnapshot(
            M68kV2PromotionCompilationInput input,
            out M68kV2PromotionCompilationPlan plan)
        {
            plan = default;
            if (input.Snapshot.IsEmpty ||
                !input.Options.V2Enabled)
            {
                return false;
            }

            var reader = new M68kSnapshotCodeReader(input.Snapshot);
            if (!TryCreateV2TracePlanFromSnapshot(reader, input.Root, input.Tier, input.Options, out var v2Trace))
            {
                return false;
            }

            var codeStart = input.CurrentCodeStart;
            var codeEnd = Normalize(input.CurrentCodeStart + (uint)input.CurrentByteLength);
            ExtendCodeRange(ref codeStart, ref codeEnd, v2Trace.CodeStart, v2Trace.ByteLength);
            var byteLength = GetCodeRangeByteLength(codeStart, codeEnd);
            if (!reader.ContainsRange(codeStart, byteLength) ||
                !reader.TryCaptureWords(codeStart, byteLength, out var codeWords) ||
                !reader.TryGetGeneration(codeStart, out var startGeneration) ||
                !reader.TryGetGeneration(Normalize(codeStart + (uint)byteLength - 1u), out var endGeneration))
            {
                return false;
            }

            plan = new M68kV2PromotionCompilationPlan(
                input.Root,
                input.Tier,
                codeStart,
                byteLength,
                startGeneration,
                endGeneration,
                codeWords,
                v2Trace,
                input.Options.V2BusGraphEnabled);
            return true;
        }

        private static bool TryCreateV2TracePlanFromSnapshot(
            M68kSnapshotCodeReader reader,
            uint root,
            V2TraceTier tier,
            M68kTraceCompilationOptions options,
            out V2TracePlan trace)
        {
            trace = default;
            if (!options.V2Enabled)
            {
                return false;
            }

            var collectionTier = GetWorkerGraphExpansionTier(tier, options);
            if (!TryCollectV2InstructionsFromSnapshot(
                reader,
                root,
                collectionTier,
                options,
                out var instructions,
                out var codeStart,
                out var byteLength,
                out var hasInternalLoop,
                out var pureCpuBatchEligible,
                out var fastReadOnlyBatchEligible,
                out var externalHandoffTargets))
            {
                return false;
            }

            trace = new V2TracePlan(
                collectionTier,
                instructions,
                codeStart,
                byteLength,
                hasInternalLoop,
                pureCpuBatchEligible,
                fastReadOnlyBatchEligible,
                externalHandoffTargets,
                workerGraphExpanded: collectionTier != tier);
            return true;
        }

        private static bool TryCollectV2InstructionsFromSnapshot(
            M68kSnapshotCodeReader reader,
            uint root,
            V2TraceTier tier,
            M68kTraceCompilationOptions options,
            out M68kDecodedInstruction[] instructions,
            out uint codeStart,
            out int byteLength,
            out bool hasInternalLoop,
            out bool pureCpuBatchEligible,
            out bool fastReadOnlyBatchEligible,
            out uint[] externalHandoffTargets)
        {
            instructions = Array.Empty<M68kDecodedInstruction>();
            codeStart = root;
            byteLength = 0;
            hasInternalLoop = false;
            pureCpuBatchEligible = true;
            fastReadOnlyBatchEligible = false;
            externalHandoffTargets = Array.Empty<uint>();
            var (maxInstructions, maxBytes) = GetV2TierLimits(tier);
            var collected = new Dictionary<uint, M68kDecodedInstruction>();
            var queued = new HashSet<uint>();
            var externalTargets = new List<uint>();
            var work = new Queue<uint>();
            var codeEnd = root;
            var hasBranch = false;
            var hasBusAccess = false;
            var hasFastReadOnlyAccess = false;
            var canCollectBusGraph = options.V2BusAccessEnabled && options.V2BusGraphEnabled;
            EnqueueV2GraphAddress(root, codeStart, codeEnd, maxBytes, work, queued);
            while (work.Count > 0 && collected.Count < maxInstructions)
            {
                var pc = work.Dequeue();
                if (collected.ContainsKey(pc))
                {
                    continue;
                }

                while (collected.Count < maxInstructions)
                {
                    if (collected.ContainsKey(pc))
                    {
                        break;
                    }

                    if (!reader.ContainsRange(pc, 2) ||
                        reader.HasHostTrapStub(pc) ||
                        !M68kDecoder.TryDecode(reader, pc, out var instruction, out _))
                    {
                        if (collected.Count == 0)
                        {
                            return false;
                        }

                        break;
                    }

                    if (!reader.ContainsRange(instruction.ProgramCounter, instruction.Length) ||
                        !TryExtendV2CodeRange(
                            codeStart,
                            codeEnd,
                            instruction.ProgramCounter,
                            instruction.Length,
                            maxBytes,
                            out var candidateCodeStart,
                            out var candidateCodeEnd))
                    {
                        if (collected.Count == 0)
                        {
                            return false;
                        }

                        break;
                    }

                    if (!IsV2Instruction(instruction, options, out _))
                    {
                        if (collected.Count == 0)
                        {
                            return false;
                        }

                        break;
                    }

                    if (IsV2TerminalBusControlInstruction(instruction))
                    {
                        if (hasBranch && !canCollectBusGraph)
                        {
                            break;
                        }

                        collected.Add(pc, instruction);
                        codeStart = candidateCodeStart;
                        codeEnd = candidateCodeEnd;
                        hasBusAccess = true;
                        break;
                    }

                    if (IsV2BranchInstruction(instruction))
                    {
                        if (hasBusAccess && !canCollectBusGraph)
                        {
                            break;
                        }

                        collected.Add(pc, instruction);
                        codeStart = candidateCodeStart;
                        codeEnd = candidateCodeEnd;
                        hasBranch = true;
                        if (instruction.Operation == M68kJitOperation.Jmp)
                        {
                            break;
                        }

                        var target = GetBranchTarget(instruction);
                        if (reader.ContainsRange(target, 2) &&
                            CanIncludeV2GraphAddress(codeStart, codeEnd, target, maxBytes))
                        {
                            hasInternalLoop |= target <= instruction.ProgramCounter;
                            EnqueueV2GraphAddress(target, codeStart, codeEnd, maxBytes, work, queued);
                        }
                        else
                        {
                            AddV2ExternalHandoffTarget(externalTargets, target);
                        }

                        if (instruction.Operation is M68kJitOperation.Bcc or M68kJitOperation.Dbcc)
                        {
                            var fallthrough = Normalize(instruction.ProgramCounter + (uint)instruction.Length);
                            if (reader.ContainsRange(fallthrough, 2) &&
                                CanIncludeV2GraphAddress(codeStart, codeEnd, fallthrough, maxBytes))
                            {
                                EnqueueV2GraphAddress(fallthrough, codeStart, codeEnd, maxBytes, work, queued);
                            }
                            else
                            {
                                AddV2ExternalHandoffTarget(externalTargets, fallthrough);
                            }
                        }

                        break;
                    }

                    var instructionPure = IsV2PureCpuInstruction(instruction);
                    var instructionFastReadOnly = !options.V2BusAccessEnabled &&
                        options.V2FastReadEnabled &&
                        IsV2FastReadOnlyBatchInstruction(instruction) &&
                        ((!hasFastReadOnlyAccess && collected.Count == 0) ||
                            IsV2PcIndexFastReadOnlyBatchInstruction(instruction));
                    if (instructionFastReadOnly)
                    {
                        hasFastReadOnlyAccess = true;
                    }

                    if (!instructionPure && !instructionFastReadOnly)
                    {
                        if (hasBranch && !canCollectBusGraph)
                        {
                            break;
                        }

                        hasBusAccess = true;
                    }

                    collected.Add(pc, instruction);
                    codeStart = candidateCodeStart;
                    codeEnd = candidateCodeEnd;

                    pc = Normalize(pc + (uint)instruction.Length);
                    if (!reader.ContainsRange(pc, 2) ||
                        !CanIncludeV2GraphAddress(codeStart, codeEnd, pc, maxBytes))
                    {
                        break;
                    }
                }
            }

            if (collected.Count == 0)
            {
                return false;
            }

            instructions = new M68kDecodedInstruction[collected.Count];
            var index = 0;
            foreach (var instruction in SortV2GraphInstructions(root, collected.Values))
            {
                instructions[index++] = instruction;
            }

            byteLength = GetCodeRangeByteLength(codeStart, codeEnd);
            pureCpuBatchEligible = !hasBusAccess;
            fastReadOnlyBatchEligible = pureCpuBatchEligible && hasFastReadOnlyAccess;
            externalHandoffTargets = externalTargets.ToArray();
            return !hasBusAccess || options.V2BusAccessEnabled;
        }

        private void RecordTraceInstructionKinds(TraceEntry trace, int executed)
        {
            executed = Math.Clamp(executed, 0, trace.DirectInstructionPrefixes.Length - 1);
            _counters.DirectIlInstructions += trace.DirectInstructionPrefixes[executed];
            _counters.HelperIlInstructions += trace.HelperInstructionPrefixes[executed];
            _counters.DirectCpuIlInstructions += trace.DirectCpuInstructionPrefixes[executed];
            _counters.DirectMemoryIlInstructions += trace.DirectMemoryInstructionPrefixes[executed];
            _counters.SpecializedHelperIlInstructions += trace.SpecializedHelperInstructionPrefixes[executed];
        }

        private bool TraceCodeWordsMatch(TraceEntry trace)
        {
            if (_amigaBus == null)
            {
                return true;
            }

            var address = trace.CodeStart;
            for (var i = 0; i < trace.CodeWords.Length; i++)
            {
                if (_amigaBus.ReadHostWord(address) != trace.CodeWords[i])
                {
                    return false;
                }

                address = Normalize(address + 2);
            }

            return true;
        }

        private ushort[] CaptureTraceWords(uint root, int byteLength)
        {
            if (_amigaBus == null)
            {
                return Array.Empty<ushort>();
            }

            var wordCount = (byteLength + 1) / 2;
            var words = new ushort[wordCount];
            var address = root;
            for (var i = 0; i < words.Length; i++)
            {
                words[i] = _amigaBus.ReadHostWord(address);
                address = Normalize(address + 2);
            }

            return words;
        }

        private TraceEntry RefreshTraceGenerations(TraceEntry trace)
        {
            if (_amigaBus == null)
            {
                return trace;
            }

            return trace.WithGenerations(
                _amigaBus.GetCodePageGeneration(trace.CodeStart),
                _amigaBus.GetCodePageGeneration(Normalize(trace.CodeStart + (uint)trace.ByteLength - 1u)));
        }

        private static void CaptureTraceInstructionKindPrefixes(
            ReadOnlySpan<M68kDecodedInstruction> instructions,
            out int[] directInstructionPrefixes,
            out int[] helperInstructionPrefixes,
            out int[] directCpuInstructionPrefixes,
            out int[] directMemoryInstructionPrefixes,
            out int[] specializedHelperInstructionPrefixes)
        {
            directInstructionPrefixes = new int[instructions.Length + 1];
            helperInstructionPrefixes = new int[instructions.Length + 1];
            directCpuInstructionPrefixes = new int[instructions.Length + 1];
            directMemoryInstructionPrefixes = new int[instructions.Length + 1];
            specializedHelperInstructionPrefixes = new int[instructions.Length + 1];
            for (var i = 0; i < instructions.Length; i++)
            {
                directInstructionPrefixes[i + 1] = directInstructionPrefixes[i];
                helperInstructionPrefixes[i + 1] = helperInstructionPrefixes[i];
                directCpuInstructionPrefixes[i + 1] = directCpuInstructionPrefixes[i];
                directMemoryInstructionPrefixes[i + 1] = directMemoryInstructionPrefixes[i];
                specializedHelperInstructionPrefixes[i + 1] = specializedHelperInstructionPrefixes[i];
                switch (M68kOperationEmitter.GetInstructionKind(instructions[i]))
                {
                    case M68kIlInstructionKind.DirectCpu:
                        directInstructionPrefixes[i + 1]++;
                        directCpuInstructionPrefixes[i + 1]++;
                        break;
                    case M68kIlInstructionKind.DirectMemory:
                        directInstructionPrefixes[i + 1]++;
                        directMemoryInstructionPrefixes[i + 1]++;
                        break;
                    case M68kIlInstructionKind.SpecializedHelper:
                        directInstructionPrefixes[i + 1]++;
                        specializedHelperInstructionPrefixes[i + 1]++;
                        break;
                    default:
                        helperInstructionPrefixes[i + 1]++;
                        break;
                }
            }
        }

        private static bool IsPureCpuBatchEligible(ReadOnlySpan<M68kDecodedInstruction> instructions)
        {
            for (var i = 0; i < instructions.Length; i++)
            {
                if (!IsPureCpuInstruction(instructions[i]))
                {
                    return false;
                }
            }

            return instructions.Length > 0;
        }

        private static bool IsPureCpuInstruction(M68kDecodedInstruction instruction)
        {
            return M68kOperationEmitter.CanEmitPureCpuBatch(instruction);
        }

        private void CountBailout(M68kJitBailoutReason reason)
        {
            switch (reason)
            {
                case M68kJitBailoutReason.UnsupportedEa:
                    _counters.UnsupportedEa++;
                    break;
                case M68kJitBailoutReason.HostTrap:
                    _counters.HostTrapBailouts++;
                    break;
                case M68kJitBailoutReason.SystemInstruction:
                    _counters.SystemInstructionBailouts++;
                    break;
                case M68kJitBailoutReason.ExceptionInstruction:
                    _counters.ExceptionInstructionBailouts++;
                    break;
                default:
                    _counters.UnsupportedOpcode++;
                    break;
            }
        }

        private static CompiledTrace Compile(
            uint root,
            ReadOnlySpan<M68kDecodedInstruction> instructions,
            bool emitBoundaryCalls)
        {
            var method = new DynamicMethod(
                (emitBoundaryCalls ? "M68kTrace_" : "M68kPureTrace_") + root.ToString("X6"),
                typeof(int),
                new[] { typeof(M68kJitCore), typeof(int), typeof(long), typeof(IM68kInstructionBoundary) },
                typeof(M68kJitCore).Module,
                skipVisibility: true);
            var il = method.GetILGenerator();
            var returnLabels = new Label[instructions.Length + 1];
            for (var i = 0; i < returnLabels.Length; i++)
            {
                returnLabels[i] = il.DefineLabel();
            }

            var canEnter = GetInstanceMethod(emitBoundaryCalls
                ? nameof(CanEnterCompiledInstruction)
                : nameof(CanEnterPureCompiledInstruction));
            var begin = GetInstanceMethod(nameof(BeginCompiledInstruction));
            var finish = emitBoundaryCalls
                ? GetInstanceMethod(nameof(FinishCompiledInstruction))
                : null;
            var emitContext = M68kOperationEmitter.EmitTraceLocals(il);

            for (var i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg_2);
                if (emitBoundaryCalls)
                {
                    il.Emit(OpCodes.Ldarg_3);
                }

                il.Emit(OpCodes.Call, canEnter);
                il.Emit(OpCodes.Brfalse, returnLabels[i]);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.ProgramCounter));
                il.Emit(OpCodes.Ldc_I4, instruction.Opcode);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)Normalize(instruction.ProgramCounter + (uint)instruction.Length)));
                il.Emit(OpCodes.Call, begin);
                il.Emit(OpCodes.Brfalse, returnLabels[i]);

                _ = M68kOperationEmitter.Emit(il, instruction, emitContext);
                il.Emit(OpCodes.Brfalse, returnLabels[i]);

                if (emitBoundaryCalls)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_3);
                    il.Emit(OpCodes.Call, finish!);
                }
            }

            il.Emit(OpCodes.Ldc_I4, instructions.Length);
            il.Emit(OpCodes.Ret);

            for (var i = 0; i < returnLabels.Length; i++)
            {
                il.MarkLabel(returnLabels[i]);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ret);
            }

            return (CompiledTrace)method.CreateDelegate(typeof(CompiledTrace));
        }

        private bool TryCompileV2Trace(uint root, V2TraceTier tier, out V2TraceCompilation trace)
        {
            trace = default;
            if (!TryCreateV2TracePlan(root, tier, out var plan))
            {
                return false;
            }

            trace = CompileV2FromPlan(root, plan, _v2BusGraphEnabled);
            RecordV2CompiledTrace(tier);
            return true;
        }

        private bool TryCreateV2TracePlan(uint root, V2TraceTier tier, out V2TracePlan trace)
        {
            trace = default;
            if (!_v2Enabled)
            {
                RecordV2Rejection(V2TraceRejectionReason.Disabled);
                return false;
            }

            if (_v2DisabledRoots.Contains(root))
            {
                RecordV2Rejection(V2TraceRejectionReason.Disabled);
                return false;
            }

            if (_amigaBus == null)
            {
                RecordV2Rejection(V2TraceRejectionReason.NoBus);
                return false;
            }

            if (_amigaBus.IsChipRamAddress(root))
            {
                RecordV2Rejection(V2TraceRejectionReason.ChipRam);
                return false;
            }

            if (!TryCollectV2Instructions(
                root,
                tier,
                out var instructions,
                out var codeStart,
                out var byteLength,
                out var hasInternalLoop,
                out var pureCpuBatchEligible,
                out var fastReadOnlyBatchEligible,
                out var externalHandoffTargets,
                out var rejectionReason,
                out var rejectedInstruction))
            {
                RecordV2Rejection(rejectionReason, rejectedInstruction);
                return false;
            }

            trace = new V2TracePlan(
                tier,
                instructions,
                codeStart,
                byteLength,
                hasInternalLoop,
                pureCpuBatchEligible,
                fastReadOnlyBatchEligible,
                externalHandoffTargets);
            return true;
        }

        private static V2TraceCompilation CompileV2FromPlan(uint root, V2TracePlan plan, bool useGraph)
        {
            var compiled = CompileV2(
                root,
                plan.Tier,
                plan.Instructions,
                plan.CodeStart,
                plan.ByteLength,
                plan.PureCpuBatchEligible,
                plan.FastReadOnlyBatchEligible,
                useGraph);
            return new V2TraceCompilation(
                compiled,
                plan.Tier,
                plan.Instructions,
                plan.CodeStart,
                plan.ByteLength,
                plan.HasInternalLoop,
                plan.PureCpuBatchEligible);
        }

        private bool TryCollectV2Instructions(
            uint root,
            V2TraceTier tier,
            out M68kDecodedInstruction[] instructions,
            out uint codeStart,
            out int byteLength,
            out bool hasInternalLoop,
            out bool pureCpuBatchEligible,
            out bool fastReadOnlyBatchEligible,
            out uint[] externalHandoffTargets,
            out V2TraceRejectionReason rejectionReason,
            out M68kDecodedInstruction rejectedInstruction)
        {
            instructions = Array.Empty<M68kDecodedInstruction>();
            codeStart = root;
            byteLength = 0;
            hasInternalLoop = false;
            pureCpuBatchEligible = true;
            fastReadOnlyBatchEligible = false;
            externalHandoffTargets = Array.Empty<uint>();
            rejectionReason = V2TraceRejectionReason.Empty;
            rejectedInstruction = default;
            if (_amigaBus == null)
            {
                rejectionReason = V2TraceRejectionReason.NoBus;
                return false;
            }

            var (maxInstructions, maxBytes) = GetV2TierLimits(tier);
            var collected = new Dictionary<uint, M68kDecodedInstruction>();
            var queued = new HashSet<uint>();
            var externalTargets = new List<uint>();
            var work = new Queue<uint>();
            var codeEnd = root;
            var hasBranch = false;
            var hasBusAccess = false;
            var hasFastReadOnlyAccess = false;
            var canCollectBusGraph = _v2BusAccessEnabled && _v2BusGraphEnabled;
            EnqueueV2GraphAddress(root, codeStart, codeEnd, maxBytes, work, queued);
            while (work.Count > 0 && collected.Count < maxInstructions)
            {
                var pc = work.Dequeue();
                if (collected.ContainsKey(pc))
                {
                    continue;
                }

                while (collected.Count < maxInstructions)
                {
                    if (collected.ContainsKey(pc))
                    {
                        break;
                    }

                    if (_amigaBus.IsChipRamAddress(pc))
                    {
                        if (collected.Count == 0)
                        {
                            rejectionReason = V2TraceRejectionReason.ChipRam;
                            return false;
                        }

                        break;
                    }

                    if (_amigaBus.HasHostTrapStub(pc))
                    {
                        if (collected.Count == 0)
                        {
                            rejectionReason = V2TraceRejectionReason.HostTrap;
                            return false;
                        }

                        break;
                    }

                    if (!M68kDecoder.TryDecode(_amigaBus, pc, out var instruction, out _))
                    {
                        if (collected.Count == 0)
                        {
                            rejectionReason = V2TraceRejectionReason.DecodeFailure;
                            return false;
                        }

                        break;
                    }

                    if (!TryExtendV2CodeRange(
                        codeStart,
                        codeEnd,
                        instruction.ProgramCounter,
                        instruction.Length,
                        maxBytes,
                        out var candidateCodeStart,
                        out var candidateCodeEnd))
                    {
                        if (collected.Count == 0)
                        {
                            rejectionReason = V2TraceRejectionReason.Budget;
                            return false;
                        }

                        break;
                    }

                    if (!IsV2Instruction(instruction, out var instructionRejectionReason))
                    {
                        if (collected.Count == 0)
                        {
                            rejectionReason = instructionRejectionReason;
                            rejectedInstruction = instruction;
                            return false;
                        }

                        RecordV2UnsupportedInstruction(instructionRejectionReason, instruction);
                        break;
                    }

                    if (IsV2TerminalBusControlInstruction(instruction))
                    {
                        if (hasBranch && !canCollectBusGraph)
                        {
                            break;
                        }

                        collected.Add(pc, instruction);
                        codeStart = candidateCodeStart;
                        codeEnd = candidateCodeEnd;
                        hasBusAccess = true;
                        break;
                    }

                    if (IsV2BranchInstruction(instruction))
                    {
                        if (hasBusAccess && !canCollectBusGraph)
                        {
                            break;
                        }

                        collected.Add(pc, instruction);
                        codeStart = candidateCodeStart;
                        codeEnd = candidateCodeEnd;
                        hasBranch = true;
                        if (instruction.Operation == M68kJitOperation.Jmp)
                        {
                            break;
                        }

                        var target = GetBranchTarget(instruction);
                        if (CanIncludeV2GraphAddress(codeStart, codeEnd, target, maxBytes))
                        {
                            hasInternalLoop |= target <= instruction.ProgramCounter;
                            EnqueueV2GraphAddress(target, codeStart, codeEnd, maxBytes, work, queued);
                        }
                        else
                        {
                            AddV2ExternalHandoffTarget(externalTargets, target);
                        }

                        if (instruction.Operation is M68kJitOperation.Bcc or M68kJitOperation.Dbcc)
                        {
                            var fallthrough = Normalize(instruction.ProgramCounter + (uint)instruction.Length);
                            if (CanIncludeV2GraphAddress(codeStart, codeEnd, fallthrough, maxBytes))
                            {
                                EnqueueV2GraphAddress(fallthrough, codeStart, codeEnd, maxBytes, work, queued);
                            }
                            else
                            {
                                AddV2ExternalHandoffTarget(externalTargets, fallthrough);
                            }
                        }

                        break;
                    }

                    var instructionPure = IsV2PureCpuInstruction(instruction);
                    var instructionFastReadOnly = !_v2BusAccessEnabled &&
                        _v2FastReadEnabled &&
                        IsV2FastReadOnlyBatchInstruction(instruction) &&
                        ((!hasFastReadOnlyAccess && collected.Count == 0) ||
                            IsV2StaticallyZeroWaitFastReadInstruction(instruction) ||
                            IsV2PcIndexFastReadOnlyBatchInstruction(instruction));
                    if (instructionFastReadOnly)
                    {
                        hasFastReadOnlyAccess = true;
                    }

                    if (!instructionPure && !instructionFastReadOnly)
                    {
                        if (hasBranch && !canCollectBusGraph)
                        {
                            break;
                        }

                        hasBusAccess = true;
                    }

                    collected.Add(pc, instruction);
                    codeStart = candidateCodeStart;
                    codeEnd = candidateCodeEnd;

                    pc = Normalize(pc + (uint)instruction.Length);
                    if (!CanIncludeV2GraphAddress(codeStart, codeEnd, pc, maxBytes))
                    {
                        break;
                    }
                }
            }

            if (collected.Count == 0)
            {
                rejectionReason = V2TraceRejectionReason.Empty;
                return false;
            }

            instructions = new M68kDecodedInstruction[collected.Count];
            var index = 0;
            foreach (var instruction in SortV2GraphInstructions(root, collected.Values))
            {
                instructions[index++] = instruction;
            }

            byteLength = GetCodeRangeByteLength(codeStart, codeEnd);
            pureCpuBatchEligible = !hasBusAccess;
            fastReadOnlyBatchEligible = pureCpuBatchEligible && hasFastReadOnlyAccess;
            externalHandoffTargets = externalTargets.ToArray();
            if (hasBusAccess && !_v2BusAccessEnabled)
            {
                rejectionReason = V2TraceRejectionReason.Empty;
                return false;
            }

            return true;
        }

        private void MaybePromoteV2Trace(TraceEntry trace, int executed)
        {
            if (!_v2Enabled || trace.V2Compiled == null || executed <= 0 || _v2DisabledRoots.Contains(trace.Root))
            {
                return;
            }

            _v2TraceHits.TryGetValue(trace.Root, out var hits);
            hits++;
            _v2TraceHits[trace.Root] = hits;
            _v2TraceBranchExits.TryGetValue(trace.Root, out var branchExits);
            var nextTier = GetNextV2PromotionTier(
                trace,
                hits,
                branchExits,
                out var branchPressurePromotion);
            if (nextTier == V2TraceTier.None)
            {
                return;
            }

            if (_asyncJitEnabled)
            {
                if (TryQueueAsyncV2Promotion(
                    trace,
                    nextTier,
                    branchPressurePromotion,
                    CalculateAsyncCompilePriority(
                        trace.Root,
                        nextTier,
                        M68kJitCompileReason.Promotion,
                        hits,
                        branchExits,
                        trace.V2HasInternalLoop)) ||
                    !_asyncJitSyncFallbackEnabled)
                {
                    return;
                }
            }

            if (!TryCompileV2Trace(trace.Root, nextTier, out var promoted))
            {
                return;
            }

            if (!V2PromotionExpandsTrace(trace, promoted) &&
                (!branchPressurePromotion || !CanV2TierPromoteFurther(nextTier)))
            {
                BlockV2PromotionTier(trace.Root, nextTier);
                return;
            }

            var codeStart = trace.CodeStart;
            var codeEnd = Normalize(trace.CodeStart + (uint)trace.ByteLength);
            ExtendCodeRange(ref codeStart, ref codeEnd, promoted.CodeStart, promoted.ByteLength);
            var byteLength = GetCodeRangeByteLength(codeStart, codeEnd);
            var codeWords = codeStart != trace.CodeStart || byteLength != trace.ByteLength
                ? CaptureTraceWords(codeStart, byteLength)
                : trace.CodeWords;
            var startGeneration = _amigaBus?.GetCodePageGeneration(codeStart) ?? trace.StartGeneration;
            var endGeneration = _amigaBus?.GetCodePageGeneration(Normalize(codeStart + (uint)byteLength - 1u)) ??
                trace.EndGeneration;
            var updated = trace.WithV2(
                codeStart,
                byteLength,
                startGeneration,
                endGeneration,
                codeWords,
                promoted.Compiled!,
                promoted.Tier,
                promoted.InstructionCount,
                promoted.HasInternalLoop,
                promoted.PureCpuBatchEligible);
            AddTrace(updated);
            _v2TraceHits[trace.Root] = hits;
            _v2TraceBranchExits[trace.Root] = 0;
            _v2BlockedPromotionTiers.Remove(trace.Root);
            _counters.V2TierPromotions++;
            if (branchPressurePromotion)
            {
                _counters.V2TierPressurePromotions++;
            }
        }

        private static bool V2PromotionExpandsTrace(TraceEntry trace, V2TraceCompilation promoted)
            => promoted.CodeStart != trace.CodeStart ||
                promoted.ByteLength > trace.ByteLength ||
                promoted.InstructionCount > trace.V2InstructionCount;

        private bool CanV2TierPromoteFurther(V2TraceTier tier)
            => tier switch
            {
                V2TraceTier.Tier0 or V2TraceTier.Tier1 => true,
                V2TraceTier.Tier2 => _v2Tier3Enabled,
                _ => false
            };

        private bool IsV2PromotionTierBlocked(uint root, V2TraceTier tier)
        {
            return _v2BlockedPromotionTiers.TryGetValue(root, out var blocked) &&
                (blocked & (1 << (int)tier)) != 0;
        }

        private void BlockV2PromotionTier(uint root, V2TraceTier tier)
        {
            _v2BlockedPromotionTiers.TryGetValue(root, out var blocked);
            _v2BlockedPromotionTiers[root] = blocked | (1 << (int)tier);
        }

        private V2TraceTier GetNextV2PromotionTier(
            TraceEntry trace,
            int hits,
            int branchExits,
            out bool branchPressurePromotion)
        {
            branchPressurePromotion = false;
            if (TryGetV2PromotionCandidate(trace, hits, branchExits, V2TraceTier.Tier1, out branchPressurePromotion) ||
                TryGetV2PromotionCandidate(trace, hits, branchExits, V2TraceTier.Tier2, out branchPressurePromotion) ||
                TryGetV2PromotionCandidate(trace, hits, branchExits, V2TraceTier.Tier3, out branchPressurePromotion))
            {
                return branchPressurePromotion
                    ? GetHighestEligibleV2PromotionTier(trace, branchExits)
                    : GetHighestEligibleV2HitPromotionTier(trace, hits);
            }

            return V2TraceTier.None;
        }

        private bool TryGetV2PromotionCandidate(
            TraceEntry trace,
            int hits,
            int branchExits,
            V2TraceTier candidateTier,
            out bool branchPressurePromotion)
        {
            branchPressurePromotion = false;
            if (candidateTier <= trace.V2Tier ||
                IsV2PromotionTierBlocked(trace.Root, candidateTier) ||
                (candidateTier == V2TraceTier.Tier3 && !_v2Tier3Enabled))
            {
                return false;
            }

            var hitThreshold = candidateTier switch
            {
                V2TraceTier.Tier1 => V2Tier1PromotionHits,
                V2TraceTier.Tier2 => V2Tier2PromotionHits,
                V2TraceTier.Tier3 => V2Tier3PromotionHits,
                _ => int.MaxValue
            };
            if (trace.V2HasInternalLoop && hits >= hitThreshold)
            {
                return true;
            }

            var branchThreshold = candidateTier switch
            {
                V2TraceTier.Tier1 => V2Tier1BranchExitPromotionExits,
                V2TraceTier.Tier2 => V2Tier2BranchExitPromotionExits,
                V2TraceTier.Tier3 => V2Tier3BranchExitPromotionExits,
                _ => int.MaxValue
            };
            if (branchExits >= branchThreshold)
            {
                branchPressurePromotion = true;
                return true;
            }

            return false;
        }

        private V2TraceTier GetHighestEligibleV2PromotionTier(TraceEntry trace, int branchExits)
        {
            var tier = V2TraceTier.None;
            if (TryGetV2PromotionCandidate(trace, hits: 0, branchExits, V2TraceTier.Tier1, out _))
            {
                tier = V2TraceTier.Tier1;
            }

            if (TryGetV2PromotionCandidate(trace, hits: 0, branchExits, V2TraceTier.Tier2, out _))
            {
                tier = V2TraceTier.Tier2;
            }

            if (TryGetV2PromotionCandidate(trace, hits: 0, branchExits, V2TraceTier.Tier3, out _))
            {
                tier = V2TraceTier.Tier3;
            }

            return tier;
        }

        private V2TraceTier GetHighestEligibleV2HitPromotionTier(TraceEntry trace, int hits)
        {
            var tier = V2TraceTier.None;
            if (TryGetV2PromotionCandidate(trace, hits, branchExits: 0, V2TraceTier.Tier1, out _))
            {
                tier = V2TraceTier.Tier1;
            }

            if (TryGetV2PromotionCandidate(trace, hits, branchExits: 0, V2TraceTier.Tier2, out _))
            {
                tier = V2TraceTier.Tier2;
            }

            if (TryGetV2PromotionCandidate(trace, hits, branchExits: 0, V2TraceTier.Tier3, out _))
            {
                tier = V2TraceTier.Tier3;
            }

            return tier;
        }

        private static (int Instructions, int Bytes) GetV2TierLimits(V2TraceTier tier)
            => tier switch
            {
                V2TraceTier.Tier3 => (V2Tier3TraceInstructions, V2Tier3TraceBytes),
                V2TraceTier.Tier2 => (V2Tier2TraceInstructions, V2Tier2TraceBytes),
                V2TraceTier.Tier1 => (V2Tier1TraceInstructions, V2Tier1TraceBytes),
                _ => (V2Tier0TraceInstructions, V2Tier0TraceBytes)
            };

        private static V2TraceTier GetWorkerGraphExpansionTier(
            V2TraceTier tier,
            M68kTraceCompilationOptions options)
        {
            if (!options.WorkerGraphExpansionEnabled)
            {
                return tier;
            }

            return tier switch
            {
                V2TraceTier.Tier0 => V2TraceTier.Tier1,
                V2TraceTier.Tier1 => V2TraceTier.Tier2,
                V2TraceTier.Tier2 when options.V2Tier3Enabled => V2TraceTier.Tier3,
                _ => tier
            };
        }

        private static int GetCodeRangeByteLength(uint codeStart, uint codeEnd)
            => (int)Normalize(codeEnd - codeStart);

        private static void ExtendCodeRange(ref uint codeStart, ref uint codeEnd, uint address, int byteLength)
        {
            var candidateStart = Normalize(address);
            var candidateEnd = Normalize(candidateStart + (uint)byteLength);
            if (candidateStart < codeStart)
            {
                codeStart = candidateStart;
            }

            if (candidateEnd > codeEnd)
            {
                codeEnd = candidateEnd;
            }
        }

        private static bool TryExtendV2CodeRange(
            uint codeStart,
            uint codeEnd,
            uint address,
            int byteLength,
            int maxBytes,
            out uint newCodeStart,
            out uint newCodeEnd)
        {
            newCodeStart = codeStart;
            newCodeEnd = codeEnd;
            ExtendCodeRange(ref newCodeStart, ref newCodeEnd, address, byteLength);
            return GetCodeRangeByteLength(newCodeStart, newCodeEnd) <= maxBytes;
        }

        private static void EnqueueV2GraphAddress(
            uint address,
            uint codeStart,
            uint codeEnd,
            int maxBytes,
            Queue<uint> work,
            HashSet<uint> queued)
        {
            if (!CanIncludeV2GraphAddress(codeStart, codeEnd, address, maxBytes) || !queued.Add(address))
            {
                return;
            }

            work.Enqueue(address);
        }

        private static void AddV2ExternalHandoffTarget(List<uint> targets, uint target)
        {
            target = Normalize(target);
            if (!targets.Contains(target))
            {
                targets.Add(target);
            }
        }

        private static bool CanIncludeV2GraphAddress(uint codeStart, uint codeEnd, uint address, int maxBytes)
            => TryExtendV2CodeRange(codeStart, codeEnd, address, 2, maxBytes, out _, out _);

        private static List<M68kDecodedInstruction> SortV2GraphInstructions(
            uint root,
            IEnumerable<M68kDecodedInstruction> instructions)
        {
            var sorted = new List<M68kDecodedInstruction>(instructions);
            sorted.Sort((left, right) => Normalize(left.ProgramCounter - root).CompareTo(Normalize(right.ProgramCounter - root)));
            return sorted;
        }

        private bool IsV2Instruction(M68kDecodedInstruction instruction, out V2TraceRejectionReason rejectionReason)
        {
            rejectionReason = V2TraceRejectionReason.None;
            var allowMemoryRead = _v2MemoryReadEnabled || _v2FastReadEnabled || _v2BusAccessEnabled;
            var supported = instruction.Operation switch
            {
                M68kJitOperation.Nop => true,
                M68kJitOperation.Moveq => true,
                M68kJitOperation.Move => IsV2MoveInstruction(instruction),
                M68kJitOperation.Movea => IsV2ReadableSource(instruction.Source, allowMemoryRead) &&
                    instruction.Destination.Kind == M68kJitEaKind.AddressRegister,
                M68kJitOperation.Lea => IsV2AddressOnlyEa(instruction.Source),
                M68kJitOperation.Addq or M68kJitOperation.Subq => instruction.Destination.Kind is
                    M68kJitEaKind.DataRegister or M68kJitEaKind.AddressRegister ||
                    (_v2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Clr => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (_v2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Tst => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (allowMemoryRead && IsV2MemoryReadEa(instruction.Destination)),
                M68kJitOperation.Addi or M68kJitOperation.Subi => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Andi or M68kJitOperation.Ori or M68kJitOperation.Eori => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (_v2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Cmpi => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (allowMemoryRead && IsV2MemoryReadEa(instruction.Destination)),
                M68kJitOperation.Add or M68kJitOperation.Sub => IsV2AddSubInstruction(instruction),
                M68kJitOperation.Cmp => IsV2ReadableSource(instruction.Source, allowMemoryRead),
                M68kJitOperation.Cmpa => IsV2ReadableSource(instruction.Source, allowMemoryRead),
                M68kJitOperation.Cmpm => IsV2CmpmInstruction(instruction, _v2BusAccessEnabled),
                M68kJitOperation.And or M68kJitOperation.Or => IsV2LogicalInstruction(instruction, allowMemoryRead),
                M68kJitOperation.Eor => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (_v2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Not or M68kJitOperation.Neg => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.ExtWord or M68kJitOperation.ExtLong or M68kJitOperation.Swap or M68kJitOperation.Exg => true,
                M68kJitOperation.ShiftRegister => true,
                M68kJitOperation.BitImmediate or M68kJitOperation.BitDynamic => IsV2BitInstruction(instruction),
                M68kJitOperation.OriToCcr or
                M68kJitOperation.OriToSr or
                M68kJitOperation.AndiToCcr or
                M68kJitOperation.AndiToSr or
                M68kJitOperation.EoriToCcr or
                M68kJitOperation.EoriToSr or
                M68kJitOperation.MoveToCcr or
                M68kJitOperation.MoveToSr => true,
                M68kJitOperation.Pea => IsV2AddressOnlyEa(instruction.Source),
                M68kJitOperation.Movem => true,
                M68kJitOperation.Mulu or M68kJitOperation.Muls => IsV2MultiplyInstruction(instruction),
                M68kJitOperation.Divu or M68kJitOperation.Divs => IsV2DivideInstruction(instruction),
                M68kJitOperation.Jmp or M68kJitOperation.Jsr => IsV2AddressOnlyEa(instruction.Source),
                M68kJitOperation.Bsr or M68kJitOperation.Rts => true,
                M68kJitOperation.Bra or M68kJitOperation.Bcc or M68kJitOperation.Dbcc => true,
                _ => false
            };
            if (supported)
            {
                return true;
            }

            rejectionReason = instruction.Operation switch
            {
                M68kJitOperation.Move or
                M68kJitOperation.Movea or
                M68kJitOperation.Lea or
                M68kJitOperation.Add or
                M68kJitOperation.Addi or
                M68kJitOperation.Addq or
                M68kJitOperation.Sub or
                M68kJitOperation.Subi or
                M68kJitOperation.Subq or
                M68kJitOperation.Tst or
                M68kJitOperation.Cmpi or
                M68kJitOperation.Cmp or
                M68kJitOperation.Cmpa or
                M68kJitOperation.And or
                M68kJitOperation.Andi or
                M68kJitOperation.Or or
                M68kJitOperation.Ori or
                M68kJitOperation.Eor or
                M68kJitOperation.Eori or
                M68kJitOperation.Not or
                M68kJitOperation.Neg or
                M68kJitOperation.Mulu or
                M68kJitOperation.Muls or
                M68kJitOperation.Divu or
                M68kJitOperation.Divs or
                M68kJitOperation.Jmp or
                M68kJitOperation.Jsr => V2TraceRejectionReason.UnsupportedEa,
                _ => V2TraceRejectionReason.UnsupportedOperation
            };
            return false;
        }

        private static bool IsV2Instruction(
            M68kDecodedInstruction instruction,
            M68kTraceCompilationOptions options,
            out V2TraceRejectionReason rejectionReason)
        {
            rejectionReason = V2TraceRejectionReason.None;
            var allowMemoryRead = options.V2MemoryReadEnabled || options.V2FastReadEnabled || options.V2BusAccessEnabled;
            var supported = instruction.Operation switch
            {
                M68kJitOperation.Nop => true,
                M68kJitOperation.Moveq => true,
                M68kJitOperation.Move => IsV2MoveInstruction(instruction, options, allowMemoryRead),
                M68kJitOperation.Movea => IsV2ReadableSource(instruction.Source, allowMemoryRead) &&
                    instruction.Destination.Kind == M68kJitEaKind.AddressRegister,
                M68kJitOperation.Lea => IsV2AddressOnlyEa(instruction.Source),
                M68kJitOperation.Addq or M68kJitOperation.Subq => instruction.Destination.Kind is
                    M68kJitEaKind.DataRegister or M68kJitEaKind.AddressRegister ||
                    (options.V2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Clr => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (options.V2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Tst => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (allowMemoryRead && IsV2MemoryReadEa(instruction.Destination)),
                M68kJitOperation.Addi or M68kJitOperation.Subi => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Andi or M68kJitOperation.Ori or M68kJitOperation.Eori => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (options.V2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Cmpi => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (allowMemoryRead && IsV2MemoryReadEa(instruction.Destination)),
                M68kJitOperation.Add or M68kJitOperation.Sub => IsV2AddSubInstruction(instruction, options, allowMemoryRead),
                M68kJitOperation.Cmp => IsV2ReadableSource(instruction.Source, allowMemoryRead),
                M68kJitOperation.Cmpa => IsV2ReadableSource(instruction.Source, allowMemoryRead),
                M68kJitOperation.Cmpm => IsV2CmpmInstruction(instruction, options.V2BusAccessEnabled),
                M68kJitOperation.And or M68kJitOperation.Or => IsV2LogicalInstruction(instruction, options, allowMemoryRead),
                M68kJitOperation.Eor => instruction.Destination.Kind == M68kJitEaKind.DataRegister ||
                    (options.V2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination)),
                M68kJitOperation.Not or M68kJitOperation.Neg => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.ExtWord or M68kJitOperation.ExtLong or M68kJitOperation.Swap or M68kJitOperation.Exg => true,
                M68kJitOperation.ShiftRegister => true,
                M68kJitOperation.BitImmediate or M68kJitOperation.BitDynamic => IsV2BitInstruction(instruction, options),
                M68kJitOperation.OriToCcr or
                M68kJitOperation.OriToSr or
                M68kJitOperation.AndiToCcr or
                M68kJitOperation.AndiToSr or
                M68kJitOperation.EoriToCcr or
                M68kJitOperation.EoriToSr or
                M68kJitOperation.MoveToCcr or
                M68kJitOperation.MoveToSr => true,
                M68kJitOperation.Pea => IsV2AddressOnlyEa(instruction.Source),
                M68kJitOperation.Movem => true,
                M68kJitOperation.Mulu or M68kJitOperation.Muls => IsV2MultiplyInstruction(instruction, options),
                M68kJitOperation.Divu or M68kJitOperation.Divs => IsV2DivideInstruction(instruction, options),
                M68kJitOperation.Jmp or M68kJitOperation.Jsr => IsV2AddressOnlyEa(instruction.Source),
                M68kJitOperation.Bsr or M68kJitOperation.Rts => true,
                M68kJitOperation.Bra or M68kJitOperation.Bcc or M68kJitOperation.Dbcc => true,
                _ => false
            };
            if (supported)
            {
                return true;
            }

            rejectionReason = instruction.Operation switch
            {
                M68kJitOperation.Move or
                M68kJitOperation.Movea or
                M68kJitOperation.Lea or
                M68kJitOperation.Add or
                M68kJitOperation.Addi or
                M68kJitOperation.Addq or
                M68kJitOperation.Sub or
                M68kJitOperation.Subi or
                M68kJitOperation.Subq or
                M68kJitOperation.Tst or
                M68kJitOperation.Cmpi or
                M68kJitOperation.Cmp or
                M68kJitOperation.Cmpa or
                M68kJitOperation.And or
                M68kJitOperation.Andi or
                M68kJitOperation.Or or
                M68kJitOperation.Ori or
                M68kJitOperation.Eor or
                M68kJitOperation.Eori or
                M68kJitOperation.Not or
                M68kJitOperation.Neg or
                M68kJitOperation.Mulu or
                M68kJitOperation.Muls or
                M68kJitOperation.Divu or
                M68kJitOperation.Divs or
                M68kJitOperation.Jmp or
                M68kJitOperation.Jsr => V2TraceRejectionReason.UnsupportedEa,
                _ => V2TraceRejectionReason.UnsupportedOperation
            };
            return false;
        }

        private static bool IsV2MoveInstruction(
            M68kDecodedInstruction instruction,
            M68kTraceCompilationOptions options,
            bool allowMemoryRead)
        {
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                return IsV2ReadableSource(instruction.Source, allowMemoryRead);
            }

            return options.V2BusAccessEnabled &&
                IsV2ReadableSource(instruction.Source, allowMemoryRead) &&
                IsV2MemoryWriteEa(instruction.Destination);
        }

        private static bool IsV2LogicalInstruction(
            M68kDecodedInstruction instruction,
            M68kTraceCompilationOptions options,
            bool allowMemoryRead)
        {
            if (instruction.Variant == 1)
            {
                return options.V2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination);
            }

            return instruction.Variant == 0 &&
                instruction.Source.Kind != M68kJitEaKind.AddressRegister &&
                IsV2ReadableSource(instruction.Source, allowMemoryRead);
        }

        private static bool IsV2AddSubInstruction(
            M68kDecodedInstruction instruction,
            M68kTraceCompilationOptions options,
            bool allowMemoryRead)
        {
            if (instruction.Variant == 2)
            {
                return IsV2ReadableSource(instruction.Source, allowMemoryRead);
            }

            if (instruction.Variant == 1)
            {
                return options.V2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination);
            }

            return instruction.Variant == 0 &&
                instruction.Register >= 0 &&
                IsV2ReadableSource(instruction.Source, allowMemoryRead);
        }

        private static bool IsV2MultiplyInstruction(M68kDecodedInstruction instruction, M68kTraceCompilationOptions options)
            => instruction.Source.Kind is M68kJitEaKind.DataRegister or M68kJitEaKind.Immediate ||
                (options.V2BusAccessEnabled && IsV2MemoryReadEa(instruction.Source));

        private static bool IsV2DivideInstruction(M68kDecodedInstruction instruction, M68kTraceCompilationOptions options)
            => instruction.Source.Kind is M68kJitEaKind.DataRegister or M68kJitEaKind.Immediate ||
                (options.V2BusAccessEnabled && IsV2MemoryReadEa(instruction.Source));

        private static bool IsV2BitInstruction(M68kDecodedInstruction instruction, M68kTraceCompilationOptions options)
        {
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                return true;
            }

            if (!options.V2BusAccessEnabled && !options.V2MemoryReadEnabled && (!options.V2FastReadEnabled || instruction.Variant != 0))
            {
                return false;
            }

            return instruction.Variant == 0
                ? IsV2MemoryReadEa(instruction.Destination)
                : IsV2MemoryWriteEa(instruction.Destination);
        }

        private static bool IsV2RegisterOrImmediateSource(M68kDecodedEa ea)
            => ea.Kind is M68kJitEaKind.DataRegister or M68kJitEaKind.AddressRegister or M68kJitEaKind.Immediate;

        private static bool IsV2ReadableSource(M68kDecodedEa ea, bool allowMemoryRead)
            => IsV2RegisterOrImmediateSource(ea) || (allowMemoryRead && IsV2MemoryReadEa(ea));

        private static bool IsV2CmpmInstruction(M68kDecodedInstruction instruction, bool busAccessEnabled)
            => busAccessEnabled &&
                instruction.Size == M68kOperandSize.Byte &&
                instruction.Source.Kind == M68kJitEaKind.AddressPostincrement &&
                instruction.Destination.Kind == M68kJitEaKind.AddressPostincrement;

        private static bool IsV2MemoryReadEa(M68kDecodedEa ea)
            => ea.Kind is M68kJitEaKind.AddressIndirect or
                M68kJitEaKind.AddressPostincrement or
                M68kJitEaKind.AddressPredecrement or
                M68kJitEaKind.AddressDisplacement or
                M68kJitEaKind.AddressIndex or
                M68kJitEaKind.AbsoluteWord or
                M68kJitEaKind.AbsoluteLong or
                M68kJitEaKind.PcDisplacement or
                M68kJitEaKind.PcIndex;

        private static bool IsV2MemoryWriteEa(M68kDecodedEa ea)
            => ea.Kind is M68kJitEaKind.AddressIndirect or
                M68kJitEaKind.AddressPostincrement or
                M68kJitEaKind.AddressPredecrement or
                M68kJitEaKind.AddressDisplacement or
                M68kJitEaKind.AddressIndex or
                M68kJitEaKind.AbsoluteWord or
                M68kJitEaKind.AbsoluteLong;

        private bool IsV2MoveInstruction(M68kDecodedInstruction instruction)
        {
            var allowMemoryRead = _v2MemoryReadEnabled || _v2FastReadEnabled || _v2BusAccessEnabled;
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                return IsV2ReadableSource(instruction.Source, allowMemoryRead);
            }

            return _v2BusAccessEnabled &&
                IsV2ReadableSource(instruction.Source, allowMemoryRead) &&
                IsV2MemoryWriteEa(instruction.Destination);
        }

        private bool IsV2LogicalInstruction(M68kDecodedInstruction instruction, bool allowMemoryRead)
        {
            if (instruction.Variant == 1)
            {
                return _v2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination);
            }

            return instruction.Variant == 0 &&
                instruction.Source.Kind != M68kJitEaKind.AddressRegister &&
                IsV2ReadableSource(instruction.Source, allowMemoryRead);
        }

        private static bool IsV2PureCpuInstruction(M68kDecodedInstruction instruction)
        {
            return instruction.Operation switch
            {
                M68kJitOperation.Move => !IsV2MemoryReadEa(instruction.Source) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Movea => !IsV2MemoryReadEa(instruction.Source),
                M68kJitOperation.Add or
                M68kJitOperation.Sub => !IsV2MemoryReadEa(instruction.Source) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Addq or
                M68kJitOperation.Subq => !IsV2MemoryReadEa(instruction.Destination) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Cmp => !IsV2MemoryReadEa(instruction.Source) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Cmpi => !IsV2MemoryReadEa(instruction.Destination) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Cmpa => !IsV2MemoryReadEa(instruction.Source),
                M68kJitOperation.Cmpm => false,
                M68kJitOperation.And or
                M68kJitOperation.Or => !IsV2MemoryReadEa(instruction.Source) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Eor or
                M68kJitOperation.Andi or
                M68kJitOperation.Ori or
                M68kJitOperation.Eori => !IsV2MemoryReadEa(instruction.Destination) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Clr or
                M68kJitOperation.Tst or
                M68kJitOperation.BitImmediate or
                M68kJitOperation.BitDynamic => !IsV2MemoryReadEa(instruction.Destination) &&
                    !IsV2MemoryWriteEa(instruction.Destination),
                M68kJitOperation.Mulu or
                M68kJitOperation.Muls or
                M68kJitOperation.Divu or
                M68kJitOperation.Divs => !IsV2MemoryReadEa(instruction.Source),
                M68kJitOperation.Movem => false,
                M68kJitOperation.Jsr or M68kJitOperation.Bsr or M68kJitOperation.Rts => false,
                _ => true
            };
        }

        private bool IsV2AddSubInstruction(M68kDecodedInstruction instruction)
        {
            if (instruction.Variant == 2)
            {
                return IsV2ReadableSource(instruction.Source, _v2MemoryReadEnabled || _v2FastReadEnabled || _v2BusAccessEnabled);
            }

            if (instruction.Variant == 1)
            {
                return _v2BusAccessEnabled && IsV2MemoryWriteEa(instruction.Destination);
            }

            return instruction.Variant == 0 &&
                instruction.Register >= 0 &&
                IsV2ReadableSource(instruction.Source, _v2MemoryReadEnabled || _v2FastReadEnabled || _v2BusAccessEnabled);
        }

        private bool IsV2MultiplyInstruction(M68kDecodedInstruction instruction)
            => instruction.Source.Kind is M68kJitEaKind.DataRegister or M68kJitEaKind.Immediate ||
                (_v2BusAccessEnabled && IsV2MemoryReadEa(instruction.Source));

        private bool IsV2DivideInstruction(M68kDecodedInstruction instruction)
            => instruction.Source.Kind is M68kJitEaKind.DataRegister or M68kJitEaKind.Immediate ||
                (_v2BusAccessEnabled && IsV2MemoryReadEa(instruction.Source));

        private bool IsV2BitInstruction(M68kDecodedInstruction instruction)
        {
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                return true;
            }

            if (!_v2BusAccessEnabled && !_v2MemoryReadEnabled && (!_v2FastReadEnabled || instruction.Variant != 0))
            {
                return false;
            }

            return instruction.Variant == 0
                ? IsV2MemoryReadEa(instruction.Destination)
                : IsV2MemoryWriteEa(instruction.Destination);
        }

        private static bool IsV2FastReadOnlyBatchInstruction(M68kDecodedInstruction instruction)
            => TryGetV2FastReadOnlyMemoryEa(instruction, out _);

        private static bool IsV2PcIndexFastReadOnlyBatchInstruction(M68kDecodedInstruction instruction)
            => TryGetV2FastReadOnlyMemoryEa(instruction, out var ea) &&
                ea.Kind == M68kJitEaKind.PcIndex;

        private bool IsV2StaticallyZeroWaitFastReadInstruction(M68kDecodedInstruction instruction)
        {
            if (_amigaBus == null ||
                !TryGetV2FastReadOnlyMemoryEa(instruction, out var ea) ||
                !TryGetV2StaticMemoryReadAddress(ea, out var address))
            {
                return false;
            }

            return _amigaBus.TryReadJitZeroWaitMemory(address, instruction.Size, out _);
        }

        private static bool TryGetV2FastReadOnlyMemoryEa(M68kDecodedInstruction instruction, out M68kDecodedEa ea)
        {
            ea = instruction.Operation switch
            {
                M68kJitOperation.Move when instruction.Destination.Kind == M68kJitEaKind.DataRegister => instruction.Source,
                M68kJitOperation.Movea when instruction.Destination.Kind == M68kJitEaKind.AddressRegister => instruction.Source,
                M68kJitOperation.Tst => instruction.Destination,
                M68kJitOperation.Cmp => instruction.Source,
                M68kJitOperation.BitImmediate or M68kJitOperation.BitDynamic when instruction.Variant == 0 => instruction.Destination,
                _ => M68kDecodedEa.None
            };

            return IsV2FastReadOnlyMemoryEa(ea);
        }

        private static bool TryGetV2StaticMemoryReadAddress(M68kDecodedEa ea, out uint address)
        {
            address = ea.Kind switch
            {
                M68kJitEaKind.AbsoluteWord => unchecked((uint)(short)ea.Extension0),
                M68kJitEaKind.AbsoluteLong => ((uint)ea.Extension0 << 16) | ea.Extension1,
                M68kJitEaKind.PcDisplacement => unchecked((uint)(ea.ExtensionAddress + (short)ea.Extension0)),
                _ => 0
            };

            return ea.Kind is M68kJitEaKind.AbsoluteWord or
                M68kJitEaKind.AbsoluteLong or
                M68kJitEaKind.PcDisplacement;
        }

        private static bool IsV2FastReadOnlyMemoryEa(M68kDecodedEa ea)
            => ea.Kind is M68kJitEaKind.AddressIndirect or
                M68kJitEaKind.AddressDisplacement or
                M68kJitEaKind.AddressIndex or
                M68kJitEaKind.AbsoluteWord or
                M68kJitEaKind.AbsoluteLong or
                M68kJitEaKind.PcDisplacement or
                M68kJitEaKind.PcIndex;

        private static bool IsV2AddressOnlyEa(M68kDecodedEa ea)
            => ea.Kind is M68kJitEaKind.AddressIndirect or
                M68kJitEaKind.AddressDisplacement or
                M68kJitEaKind.AddressIndex or
                M68kJitEaKind.AbsoluteWord or
                M68kJitEaKind.AbsoluteLong or
                M68kJitEaKind.PcDisplacement or
                M68kJitEaKind.PcIndex;

        private static bool IsV2BranchInstruction(M68kDecodedInstruction instruction)
            => instruction.Operation is M68kJitOperation.Bra or M68kJitOperation.Bcc or M68kJitOperation.Dbcc or M68kJitOperation.Jmp;

        private static bool IsV2TerminalBusControlInstruction(M68kDecodedInstruction instruction)
            => instruction.Operation is M68kJitOperation.Jsr or
                M68kJitOperation.Bsr or
                M68kJitOperation.Rts or
                M68kJitOperation.OriToSr or
                M68kJitOperation.AndiToSr or
                M68kJitOperation.EoriToSr or
                M68kJitOperation.MoveToSr;

        private static void GetV2RegisterMasks(
            M68kDecodedInstruction[] instructions,
            out int loadDataRegisters,
            out int loadAddressRegisters,
            out int dirtyDataRegisters,
            out int dirtyAddressRegisters)
        {
            loadDataRegisters = 0;
            loadAddressRegisters = 0;
            dirtyDataRegisters = 0;
            dirtyAddressRegisters = 0;
            for (var i = 0; i < instructions.Length; i++)
            {
                MarkV2DirtyRegisterMasks(
                    instructions[i],
                    ref dirtyDataRegisters,
                    ref dirtyAddressRegisters);
                MarkV2LoadRegisterMasks(
                    instructions[i],
                    ref loadDataRegisters,
                    ref loadAddressRegisters);
            }

            loadDataRegisters |= dirtyDataRegisters;
            loadAddressRegisters |= dirtyAddressRegisters;
        }

        private static void MarkV2DirtyRegisterMasks(
            M68kDecodedInstruction instruction,
            ref int dirtyDataRegisters,
            ref int dirtyAddressRegisters)
        {
            MarkV2EaSideEffectDirty(instruction.Source, ref dirtyAddressRegisters);
            MarkV2EaSideEffectDirty(instruction.Destination, ref dirtyAddressRegisters);
            switch (instruction.Operation)
            {
                case M68kJitOperation.Moveq:
                case M68kJitOperation.Mulu:
                case M68kJitOperation.Muls:
                case M68kJitOperation.Divu:
                case M68kJitOperation.Divs:
                case M68kJitOperation.ExtWord:
                case M68kJitOperation.ExtLong:
                case M68kJitOperation.Swap:
                case M68kJitOperation.ShiftRegister:
                    MarkV2DataRegisterDirty(instruction.Register, ref dirtyDataRegisters);
                    return;
                case M68kJitOperation.Move:
                    if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref dirtyDataRegisters);
                    }

                    return;
                case M68kJitOperation.Movea:
                case M68kJitOperation.Lea:
                    MarkV2AddressRegisterDirty(instruction.Destination.Kind == M68kJitEaKind.AddressRegister
                        ? instruction.Destination.Register
                        : instruction.Register, ref dirtyAddressRegisters);
                    return;
                case M68kJitOperation.Addq:
                case M68kJitOperation.Subq:
                    if (instruction.Destination.Kind == M68kJitEaKind.AddressRegister)
                    {
                        MarkV2AddressRegisterDirty(instruction.Destination.Register, ref dirtyAddressRegisters);
                    }
                    else if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref dirtyDataRegisters);
                    }

                    return;
                case M68kJitOperation.Add:
                case M68kJitOperation.Sub:
                    if (instruction.Variant == 2)
                    {
                        MarkV2AddressRegisterDirty(instruction.Register, ref dirtyAddressRegisters);
                    }
                    else if (instruction.Variant == 0)
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref dirtyDataRegisters);
                    }

                    return;
                case M68kJitOperation.Clr:
                case M68kJitOperation.Neg:
                case M68kJitOperation.Not:
                case M68kJitOperation.Addi:
                case M68kJitOperation.Subi:
                case M68kJitOperation.Andi:
                case M68kJitOperation.Ori:
                case M68kJitOperation.Eori:
                    if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref dirtyDataRegisters);
                    }

                    return;
                case M68kJitOperation.And:
                case M68kJitOperation.Or:
                    if (instruction.Variant == 0)
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref dirtyDataRegisters);
                    }
                    else if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref dirtyDataRegisters);
                    }

                    return;
                case M68kJitOperation.Eor:
                    if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref dirtyDataRegisters);
                    }

                    return;
                case M68kJitOperation.Exg:
                    if (instruction.Variant == 0)
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref dirtyDataRegisters);
                        MarkV2DataRegisterDirty(instruction.QuickValue, ref dirtyDataRegisters);
                    }
                    else if (instruction.Variant == 1)
                    {
                        MarkV2AddressRegisterDirty(instruction.Register, ref dirtyAddressRegisters);
                        MarkV2AddressRegisterDirty(instruction.QuickValue, ref dirtyAddressRegisters);
                    }
                    else
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref dirtyDataRegisters);
                        MarkV2AddressRegisterDirty(instruction.QuickValue, ref dirtyAddressRegisters);
                    }

                    return;
                case M68kJitOperation.BitImmediate:
                case M68kJitOperation.BitDynamic:
                    if (instruction.Variant != 0 &&
                        instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref dirtyDataRegisters);
                    }

                    return;
                case M68kJitOperation.Dbcc:
                    MarkV2DataRegisterDirty(instruction.Register, ref dirtyDataRegisters);
                    return;
            }
        }

        private static void MarkV2EaSideEffectDirty(M68kDecodedEa ea, ref int dirtyAddressRegisters)
        {
            if (ea.Kind is M68kJitEaKind.AddressPostincrement or M68kJitEaKind.AddressPredecrement)
            {
                MarkV2AddressRegisterDirty(ea.Register, ref dirtyAddressRegisters);
            }
        }

        private static void MarkV2LoadRegisterMasks(
            M68kDecodedInstruction instruction,
            ref int loadDataRegisters,
            ref int loadAddressRegisters)
        {
            MarkV2EaRegisterReferences(instruction.Source, ref loadDataRegisters, ref loadAddressRegisters);
            MarkV2EaRegisterReferences(instruction.Destination, ref loadDataRegisters, ref loadAddressRegisters);
            switch (instruction.Operation)
            {
                case M68kJitOperation.Add:
                case M68kJitOperation.Sub:
                    if (instruction.Variant == 2)
                    {
                        MarkV2AddressRegisterDirty(instruction.Register, ref loadAddressRegisters);
                    }
                    else
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    }

                    return;
                case M68kJitOperation.Cmp:
                    MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    return;
                case M68kJitOperation.Cmpa:
                    MarkV2AddressRegisterDirty(instruction.Register, ref loadAddressRegisters);
                    return;
                case M68kJitOperation.And:
                case M68kJitOperation.Or:
                    if (instruction.Variant == 0)
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    }
                    else
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                        if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                        {
                            MarkV2DataRegisterDirty(instruction.Destination.Register, ref loadDataRegisters);
                        }
                    }

                    return;
                case M68kJitOperation.Eor:
                    MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref loadDataRegisters);
                    }

                    return;
                case M68kJitOperation.Mulu:
                case M68kJitOperation.Muls:
                case M68kJitOperation.Divu:
                case M68kJitOperation.Divs:
                    MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    return;
                case M68kJitOperation.ExtWord:
                case M68kJitOperation.ExtLong:
                case M68kJitOperation.Swap:
                case M68kJitOperation.Not:
                case M68kJitOperation.Neg:
                    MarkV2DataRegisterDirty(instruction.Register >= 0
                        ? instruction.Register
                        : instruction.Destination.Register, ref loadDataRegisters);
                    return;
                case M68kJitOperation.ShiftRegister:
                    MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    if ((instruction.Variant & 8) != 0)
                    {
                        MarkV2DataRegisterDirty(instruction.QuickValue, ref loadDataRegisters);
                    }

                    return;
                case M68kJitOperation.Exg:
                    if (instruction.Variant == 0)
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                        MarkV2DataRegisterDirty(instruction.QuickValue, ref loadDataRegisters);
                    }
                    else if (instruction.Variant == 1)
                    {
                        MarkV2AddressRegisterDirty(instruction.Register, ref loadAddressRegisters);
                        MarkV2AddressRegisterDirty(instruction.QuickValue, ref loadAddressRegisters);
                    }
                    else
                    {
                        MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                        MarkV2AddressRegisterDirty(instruction.QuickValue, ref loadAddressRegisters);
                    }

                    return;
                case M68kJitOperation.BitImmediate:
                    if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref loadDataRegisters);
                    }

                    return;
                case M68kJitOperation.BitDynamic:
                    MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                    {
                        MarkV2DataRegisterDirty(instruction.Destination.Register, ref loadDataRegisters);
                    }

                    return;
                case M68kJitOperation.Dbcc:
                    MarkV2DataRegisterDirty(instruction.Register, ref loadDataRegisters);
                    return;
            }
        }

        private static void MarkV2EaRegisterReferences(
            M68kDecodedEa ea,
            ref int loadDataRegisters,
            ref int loadAddressRegisters)
        {
            switch (ea.Kind)
            {
                case M68kJitEaKind.DataRegister:
                    MarkV2DataRegisterDirty(ea.Register, ref loadDataRegisters);
                    return;
                case M68kJitEaKind.AddressRegister:
                case M68kJitEaKind.AddressIndirect:
                case M68kJitEaKind.AddressPostincrement:
                case M68kJitEaKind.AddressPredecrement:
                case M68kJitEaKind.AddressDisplacement:
                    MarkV2AddressRegisterDirty(ea.Register, ref loadAddressRegisters);
                    return;
                case M68kJitEaKind.AddressIndex:
                    MarkV2AddressRegisterDirty(ea.Register, ref loadAddressRegisters);
                    MarkV2IndexRegisterReference(ea.Extension0, ref loadDataRegisters, ref loadAddressRegisters);
                    return;
                case M68kJitEaKind.PcIndex:
                    MarkV2IndexRegisterReference(ea.Extension0, ref loadDataRegisters, ref loadAddressRegisters);
                    return;
            }
        }

        private static void MarkV2IndexRegisterReference(
            ushort extension,
            ref int loadDataRegisters,
            ref int loadAddressRegisters)
        {
            var indexRegister = (extension >> 12) & 7;
            if ((extension & 0x8000) != 0)
            {
                MarkV2AddressRegisterDirty(indexRegister, ref loadAddressRegisters);
            }
            else
            {
                MarkV2DataRegisterDirty(indexRegister, ref loadDataRegisters);
            }
        }

        private static void MarkV2DataRegisterDirty(int register, ref int dirtyDataRegisters)
        {
            if ((uint)register < 8)
            {
                dirtyDataRegisters |= 1 << register;
            }
        }

        private static void MarkV2AddressRegisterDirty(int register, ref int dirtyAddressRegisters)
        {
            if ((uint)register < 8)
            {
                dirtyAddressRegisters |= 1 << register;
            }
        }

        private static CompiledTrace CompileV2(
            uint root,
            V2TraceTier tier,
            M68kDecodedInstruction[] instructions,
            uint codeStart,
            int byteLength,
            bool pureCpuBatchEligible,
            bool fastReadOnlyBatchEligible,
            bool busGraphEnabled)
        {
            GetV2RegisterMasks(
                instructions,
                out var loadDataRegisters,
                out var loadAddressRegisters,
                out var dirtyDataRegisters,
                out var dirtyAddressRegisters);
            if (V2TraceNeedsFullEntryRegisterLoad(instructions))
            {
                loadDataRegisters = 0xFF;
                loadAddressRegisters = 0xFF;
            }

            return pureCpuBatchEligible
                ? CompileV2Pure(
                    root,
                    tier,
                    instructions,
                    codeStart,
                    byteLength,
                    fastReadOnlyBatchEligible,
                    loadDataRegisters,
                    loadAddressRegisters,
                    dirtyDataRegisters,
                    dirtyAddressRegisters)
                : CompileV2BusAccessBatch(
                    root,
                    tier,
                    instructions,
                    codeStart,
                    byteLength,
                    busGraphEnabled,
                    loadDataRegisters,
                    loadAddressRegisters,
                    dirtyDataRegisters,
                    dirtyAddressRegisters);
        }

        private static bool V2TraceNeedsFullEntryRegisterLoad(M68kDecodedInstruction[] instructions)
        {
            for (var i = 0; i < instructions.Length; i++)
            {
                if (instructions[i].Operation is
                    M68kJitOperation.Divu or
                    M68kJitOperation.Divs or
                    M68kJitOperation.Movem or
                    M68kJitOperation.Jsr or
                    M68kJitOperation.Bsr or
                    M68kJitOperation.Rts or
                    M68kJitOperation.OriToCcr or
                    M68kJitOperation.OriToSr or
                    M68kJitOperation.AndiToCcr or
                    M68kJitOperation.AndiToSr or
                    M68kJitOperation.EoriToCcr or
                    M68kJitOperation.EoriToSr or
                    M68kJitOperation.MoveToCcr or
                    M68kJitOperation.MoveToSr or
                    M68kJitOperation.Pea)
                {
                    return true;
                }
            }

            return false;
        }

        private static CompiledTrace CompileV2Pure(
            uint root,
            V2TraceTier tier,
            M68kDecodedInstruction[] instructions,
            uint codeStart,
            int byteLength,
            bool fastReadOnlyBatchEligible,
            int loadDataRegisters,
            int loadAddressRegisters,
            int dirtyDataRegisters,
            int dirtyAddressRegisters)
        {
            var method = new DynamicMethod(
                "M68kV2Trace_" + tier + "_" + root.ToString("X6"),
                typeof(int),
                new[] { typeof(M68kJitCore), typeof(int), typeof(long), typeof(IM68kInstructionBoundary) },
                typeof(M68kJitCore).Module,
                skipVisibility: true);
            var il = method.GetILGenerator();
            var labels = new Label[instructions.Length];
            var addressToIndex = new Dictionary<uint, int>(instructions.Length);
            for (var i = 0; i < instructions.Length; i++)
            {
                labels[i] = il.DefineLabel();
                addressToIndex[instructions[i].ProgramCounter] = i;
            }

            var exit = il.DefineLabel();
            var returnZero = il.DefineLabel();
            var context = V2EmitContext.Create(il);
            var fastReadFailureLabels = new List<(Label Label, uint ProgramCounter)>();
            context.EmitLoadState(root, returnZero, loadDataRegisters, loadAddressRegisters);

            il.Emit(OpCodes.Br, labels[addressToIndex[root]]);
            for (var i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                il.MarkLabel(labels[i]);
                var fastReadFailureEnabled = fastReadOnlyBatchEligible &&
                    IsV2FastReadOnlyBatchInstruction(instruction);
                var fastReadFailureLabel = fastReadFailureEnabled
                    ? il.DefineLabel()
                    : returnZero;
                context.SetFastReadOnlyFailure(
                    fastReadFailureEnabled,
                    fastReadFailureLabel);
                context.EmitCanContinuePure(exit);
                if (fastReadFailureEnabled)
                {
                    context.EmitSaveFastReadFailureBookkeeping();
                }

                context.EmitStartInstruction(instruction);
                EmitV2Instruction(
                    il,
                    context,
                    instruction,
                    labels,
                    addressToIndex,
                    exit,
                    root,
                    codeStart,
                    byteLength,
                    recordOutOfBlockSideExit: false);
                if (instruction.Operation is M68kJitOperation.Bcc or M68kJitOperation.Dbcc)
                {
                    var fallthrough = Normalize(instruction.ProgramCounter + (uint)instruction.Length);
                    EmitV2BranchToTarget(
                        il,
                        context,
                        fallthrough,
                        labels,
                        addressToIndex,
                        exit,
                        root,
                        codeStart,
                        byteLength,
                        isFallthrough: true,
                        recordOutOfBlockSideExit: false);
                }
                else if (!IsV2BranchInstruction(instruction) &&
                    (i + 1 >= instructions.Length ||
                        instructions[i + 1].ProgramCounter != Normalize(instruction.ProgramCounter + (uint)instruction.Length)))
                {
                    il.Emit(OpCodes.Br, exit);
                }

                if (fastReadFailureEnabled)
                {
                    fastReadFailureLabels.Add((fastReadFailureLabel, instruction.ProgramCounter));
                }
            }

            foreach (var (label, programCounter) in fastReadFailureLabels)
            {
                il.MarkLabel(label);
                context.EmitRollbackFastReadFailure(programCounter, returnZero);
                context.EmitWriteback(dirtyDataRegisters, dirtyAddressRegisters);
                il.Emit(OpCodes.Ldloc, context.Executed);
                il.Emit(OpCodes.Ret);
            }

            il.MarkLabel(exit);
            context.EmitReturnZeroIfNoInstructions(returnZero);
            context.EmitWriteback(dirtyDataRegisters, dirtyAddressRegisters);
            il.Emit(OpCodes.Ldloc, context.Executed);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(returnZero);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            return (CompiledTrace)method.CreateDelegate(typeof(CompiledTrace));
        }

        private static CompiledTrace CompileV2BusAccessBatch(
            uint root,
            V2TraceTier tier,
            M68kDecodedInstruction[] instructions,
            uint codeStart,
            int byteLength,
            bool useGraph,
            int loadDataRegisters,
            int loadAddressRegisters,
            int dirtyDataRegisters,
            int dirtyAddressRegisters)
        {
            var method = new DynamicMethod(
                "M68kV2BusAccessTrace_" + tier + "_" + root.ToString("X6"),
                typeof(int),
                new[] { typeof(M68kJitCore), typeof(int), typeof(long), typeof(IM68kInstructionBoundary) },
                typeof(M68kJitCore).Module,
                skipVisibility: true);
            var il = method.GetILGenerator();
            var exit = il.DefineLabel();
            var returnZero = il.DefineLabel();
            var context = V2EmitContext.Create(il);
            context.SetZeroWaitProbe(enabled: true);
            context.EmitLoadState(root, returnZero, loadDataRegisters, loadAddressRegisters);

            if (useGraph)
            {
                var labels = new Label[instructions.Length];
                var addressToIndex = new Dictionary<uint, int>(instructions.Length);
                for (var i = 0; i < instructions.Length; i++)
                {
                    labels[i] = il.DefineLabel();
                    addressToIndex[instructions[i].ProgramCounter] = i;
                }

                il.Emit(OpCodes.Br, labels[addressToIndex[root]]);
                for (var i = 0; i < instructions.Length; i++)
                {
                    var instruction = instructions[i];
                    il.MarkLabel(labels[i]);
                    context.EmitCanContinue(exit);
                    context.EmitStartInstruction(instruction);
                    EmitV2Instruction(
                        il,
                        context,
                        instruction,
                        labels,
                        addressToIndex,
                        exit,
                        root,
                        codeStart,
                        byteLength,
                        recordOutOfBlockSideExit: false);
                    if (instruction.Operation is M68kJitOperation.Bcc or M68kJitOperation.Dbcc)
                    {
                        var fallthrough = Normalize(instruction.ProgramCounter + (uint)instruction.Length);
                        EmitV2BranchToTarget(
                            il,
                            context,
                            fallthrough,
                            labels,
                            addressToIndex,
                            exit,
                            root,
                            codeStart,
                            byteLength,
                            isFallthrough: true,
                            recordOutOfBlockSideExit: false);
                    }
                    else if (!IsV2BranchInstruction(instruction) &&
                        (i + 1 >= instructions.Length ||
                            instructions[i + 1].ProgramCounter != Normalize(instruction.ProgramCounter + (uint)instruction.Length)))
                    {
                        il.Emit(OpCodes.Br, exit);
                    }
                }
            }
            else
            {
                foreach (var instruction in instructions)
                {
                    context.EmitCanContinue(exit);
                    context.EmitStartInstruction(instruction);
                    EmitV2Instruction(
                        il,
                        context,
                        instruction,
                        Array.Empty<Label>(),
                        new Dictionary<uint, int>(),
                        exit,
                        root,
                        root,
                        0,
                        recordOutOfBlockSideExit: false);
                }
            }

            il.MarkLabel(exit);
            context.EmitReturnZeroIfNoInstructions(returnZero);
            context.EmitWriteback(dirtyDataRegisters, dirtyAddressRegisters);
            il.Emit(OpCodes.Ldloc, context.Executed);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(returnZero);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            return (CompiledTrace)method.CreateDelegate(typeof(CompiledTrace));
        }

        private static void EmitV2Instruction(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength,
            bool recordOutOfBlockSideExit)
        {
            switch (instruction.Operation)
            {
                case M68kJitOperation.Nop:
                    context.EmitAddCycles(4);
                    return;
                case M68kJitOperation.Moveq:
                    EmitV2Moveq(il, context, instruction);
                    return;
                case M68kJitOperation.Move:
                    EmitV2Move(il, context, instruction);
                    return;
                case M68kJitOperation.Movea:
                    EmitV2Movea(il, context, instruction);
                    return;
                case M68kJitOperation.Lea:
                    EmitV2Lea(il, context, instruction);
                    return;
                case M68kJitOperation.Add:
                    EmitV2AddSub(il, context, instruction, add: true);
                    return;
                case M68kJitOperation.Sub:
                    EmitV2AddSub(il, context, instruction, add: false);
                    return;
                case M68kJitOperation.Addi:
                    EmitV2ImmediateArithmetic(il, context, instruction, add: true);
                    return;
                case M68kJitOperation.Subi:
                    EmitV2ImmediateArithmetic(il, context, instruction, add: false);
                    return;
                case M68kJitOperation.Addq:
                    EmitV2QuickArithmetic(il, context, instruction, add: true);
                    return;
                case M68kJitOperation.Subq:
                    EmitV2QuickArithmetic(il, context, instruction, add: false);
                    return;
                case M68kJitOperation.Clr:
                    EmitV2Clr(il, context, instruction);
                    return;
                case M68kJitOperation.Tst:
                    EmitV2Tst(il, context, instruction);
                    return;
                case M68kJitOperation.Cmp:
                    EmitV2Compare(il, context, instruction);
                    return;
                case M68kJitOperation.Cmpa:
                    EmitV2CompareAddress(il, context, instruction);
                    return;
                case M68kJitOperation.Cmpm:
                    EmitV2CompareMemory(il, context, instruction);
                    return;
                case M68kJitOperation.Cmpi:
                    EmitV2CompareImmediate(il, context, instruction);
                    return;
                case M68kJitOperation.Andi:
                    EmitV2ImmediateLogical(il, context, instruction, operation: 0);
                    return;
                case M68kJitOperation.Ori:
                    EmitV2ImmediateLogical(il, context, instruction, operation: 1);
                    return;
                case M68kJitOperation.Eori:
                    EmitV2ImmediateLogical(il, context, instruction, operation: 2);
                    return;
                case M68kJitOperation.And:
                    EmitV2Logical(il, context, instruction, operation: 0);
                    return;
                case M68kJitOperation.Or:
                    EmitV2Logical(il, context, instruction, operation: 1);
                    return;
                case M68kJitOperation.Eor:
                    EmitV2Logical(il, context, instruction, operation: 2);
                    return;
                case M68kJitOperation.Not:
                    EmitV2Not(il, context, instruction);
                    return;
                case M68kJitOperation.Neg:
                    EmitV2Neg(il, context, instruction);
                    return;
                case M68kJitOperation.ExtWord:
                    EmitV2Ext(il, context, instruction, M68kOperandSize.Word);
                    return;
                case M68kJitOperation.ExtLong:
                    EmitV2Ext(il, context, instruction, M68kOperandSize.Long);
                    return;
                case M68kJitOperation.Swap:
                    EmitV2Swap(il, context, instruction);
                    return;
                case M68kJitOperation.Exg:
                    EmitV2Exchange(il, context, instruction);
                    return;
                case M68kJitOperation.ShiftRegister:
                    EmitV2ShiftRegister(il, context, instruction);
                    return;
                case M68kJitOperation.Movem:
                    EmitV2Movem(il, context, instruction, exit);
                    return;
                case M68kJitOperation.Mulu:
                    EmitV2Multiply(il, context, instruction, signed: false);
                    return;
                case M68kJitOperation.Muls:
                    EmitV2Multiply(il, context, instruction, signed: true);
                    return;
                case M68kJitOperation.Divu:
                    EmitV2Divide(il, context, instruction, exit, signed: false);
                    return;
                case M68kJitOperation.Divs:
                    EmitV2Divide(il, context, instruction, exit, signed: true);
                    return;
                case M68kJitOperation.BitImmediate:
                    EmitV2BitOperation(il, context, instruction, immediateBit: true);
                    return;
                case M68kJitOperation.BitDynamic:
                    EmitV2BitOperation(il, context, instruction, immediateBit: false);
                    return;
                case M68kJitOperation.OriToCcr:
                case M68kJitOperation.OriToSr:
                case M68kJitOperation.AndiToCcr:
                case M68kJitOperation.AndiToSr:
                case M68kJitOperation.EoriToCcr:
                case M68kJitOperation.EoriToSr:
                case M68kJitOperation.MoveToCcr:
                case M68kJitOperation.MoveToSr:
                    EmitV2StatusImmediate(il, context, instruction, exit);
                    return;
                case M68kJitOperation.Pea:
                    EmitV2Pea(il, context, instruction);
                    return;
                case M68kJitOperation.Jmp:
                    EmitV2Jmp(il, context, instruction, exit, root, codeStart, byteLength, recordOutOfBlockSideExit);
                    return;
                case M68kJitOperation.Jsr:
                    EmitV2Jsr(il, context, instruction, exit, root, codeStart, byteLength);
                    return;
                case M68kJitOperation.Bra:
                    EmitV2Bra(il, context, instruction, labels, addressToIndex, exit, root, codeStart, byteLength, recordOutOfBlockSideExit);
                    return;
                case M68kJitOperation.Bsr:
                    EmitV2Bsr(il, context, instruction, exit, root, codeStart, byteLength);
                    return;
                case M68kJitOperation.Bcc:
                    EmitV2Bcc(il, context, instruction, labels, addressToIndex, exit, root, codeStart, byteLength, recordOutOfBlockSideExit);
                    return;
                case M68kJitOperation.Dbcc:
                    EmitV2Dbcc(il, context, instruction, labels, addressToIndex, exit, root, codeStart, byteLength, recordOutOfBlockSideExit);
                    return;
                case M68kJitOperation.Rts:
                    EmitV2Rts(il, context, exit, root, codeStart, byteLength);
                    return;
            }

            il.Emit(OpCodes.Br, exit);
        }

        private static void EmitV2Moveq(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Stloc, value);
            context.EmitStoreDataRegister(instruction.Register, value, M68kOperandSize.Long);
            context.EmitSetPendingLogic(value, M68kOperandSize.Long);
            context.EmitAddCycles(4);
        }

        private static void EmitV2Move(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
            il.Emit(OpCodes.Stloc, value);
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                context.EmitStoreDataRegister(instruction.Destination.Register, value, instruction.Size);
            }
            else
            {
                var address = il.DeclareLocal(typeof(uint));
                EmitV2ResolveMemoryWriteAddress(il, context, instruction.Destination, instruction.Size);
                il.Emit(OpCodes.Stloc, address);
                EmitV2StoreMemoryValue(il, context, address, value, instruction.Size);
            }

            context.EmitSetPendingLogic(value, instruction.Size);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2Movea(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
            if (instruction.Size == M68kOperandSize.Word)
            {
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Conv_U4);
            }

            il.Emit(OpCodes.Stloc, value);
            context.EmitStoreAddressRegister(instruction.Destination.Register, value);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2Lea(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var address = il.DeclareLocal(typeof(uint));
            EmitV2ResolveAddress(il, context, instruction.Source);
            il.Emit(OpCodes.Stloc, address);
            context.EmitStoreAddressRegister(instruction.Register, address);
            context.EmitAddCycles(8);
        }

        private static void EmitV2QuickArithmetic(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            bool add)
        {
            if (instruction.Destination.Kind == M68kJitEaKind.AddressRegister)
            {
                var value = il.DeclareLocal(typeof(uint));
                context.EmitLoadAddressRegister(instruction.Destination.Register);
                EmitLoadUIntConstant(il, M68kCpuState.SignExtend((uint)instruction.QuickValue, instruction.Size));
                il.Emit(add ? OpCodes.Add : OpCodes.Sub);
                il.Emit(OpCodes.Stloc, value);
                context.EmitStoreAddressRegister(instruction.Destination.Register, value);
                context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 8 : 6);
                return;
            }

            if (instruction.Destination.Kind != M68kJitEaKind.DataRegister)
            {
                var address = il.DeclareLocal(typeof(uint));
                var memoryDestination = il.DeclareLocal(typeof(uint));
                var memorySource = il.DeclareLocal(typeof(uint));
                var memoryResult = il.DeclareLocal(typeof(uint));
                EmitV2ResolveMemoryWriteAddress(il, context, instruction.Destination, instruction.Size);
                il.Emit(OpCodes.Stloc, address);
                EmitV2ReadMemoryValueFromAddress(il, context, address, instruction.Size);
                il.Emit(OpCodes.Stloc, memoryDestination);
                il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
                il.Emit(OpCodes.Stloc, memorySource);
                EmitV2ArithmeticResult(il, memoryDestination, memorySource, memoryResult, instruction.Size, add);
                EmitV2StoreMemoryValue(il, context, address, memoryResult, instruction.Size);
                context.EmitSetPendingArithmetic(add ? V2PendingFlags.Add : V2PendingFlags.Subtract, memoryDestination, memorySource, memoryResult, instruction.Size, setExtend: true);
                context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 8 : 4);
                return;
            }

            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add);
            context.EmitStoreDataRegister(instruction.Destination.Register, result, instruction.Size);
            context.EmitSetPendingArithmetic(add ? V2PendingFlags.Add : V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: true);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 8 : 4);
        }

        private static void EmitV2AddSub(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            bool add)
        {
            if (instruction.Variant == 2)
            {
                var source = il.DeclareLocal(typeof(uint));
                var result = il.DeclareLocal(typeof(uint));
                EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
                if (instruction.Size == M68kOperandSize.Word)
                {
                    il.Emit(OpCodes.Conv_I2);
                    il.Emit(OpCodes.Conv_U4);
                }

                il.Emit(OpCodes.Stloc, source);
                context.EmitLoadAddressRegister(instruction.Register);
                il.Emit(OpCodes.Ldloc, source);
                il.Emit(add ? OpCodes.Add : OpCodes.Sub);
                il.Emit(OpCodes.Stloc, result);
                context.EmitStoreAddressRegister(instruction.Register, result);
                context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 8 : 6);
                return;
            }

            if (instruction.Variant == 1)
            {
                var address = il.DeclareLocal(typeof(uint));
                var memoryDestinationValue = il.DeclareLocal(typeof(uint));
                var memorySourceValue = il.DeclareLocal(typeof(uint));
                var memoryResult = il.DeclareLocal(typeof(uint));
                EmitV2ResolveMemoryWriteAddress(il, context, instruction.Destination, instruction.Size);
                il.Emit(OpCodes.Stloc, address);
                EmitV2ReadMemoryValueFromAddress(il, context, address, instruction.Size);
                il.Emit(OpCodes.Stloc, memoryDestinationValue);
                context.EmitLoadDataRegister(instruction.Register, instruction.Size);
                il.Emit(OpCodes.Stloc, memorySourceValue);
                EmitV2ArithmeticResult(il, memoryDestinationValue, memorySourceValue, memoryResult, instruction.Size, add);
                EmitV2StoreMemoryValue(il, context, address, memoryResult, instruction.Size);
                context.EmitSetPendingArithmetic(add ? V2PendingFlags.Add : V2PendingFlags.Subtract, memoryDestinationValue, memorySourceValue, memoryResult, instruction.Size, setExtend: true);
                context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
                return;
            }

            var destination = il.DeclareLocal(typeof(uint));
            var sourceValue = il.DeclareLocal(typeof(uint));
            var arithmeticResult = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
            il.Emit(OpCodes.Stloc, sourceValue);
            EmitV2ArithmeticResult(il, destination, sourceValue, arithmeticResult, instruction.Size, add);
            context.EmitStoreDataRegister(instruction.Register, arithmeticResult, instruction.Size);
            context.EmitSetPendingArithmetic(add ? V2PendingFlags.Add : V2PendingFlags.Subtract, destination, sourceValue, arithmeticResult, instruction.Size, setExtend: true);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2ImmediateArithmetic(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            bool add)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitLoadUIntConstant(il, instruction.Source.Immediate & M68kCpuState.Mask(instruction.Size));
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add);
            context.EmitStoreDataRegister(instruction.Destination.Register, result, instruction.Size);
            context.EmitSetPendingArithmetic(add ? V2PendingFlags.Add : V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: true);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 16 : 8);
        }

        private static void EmitV2Tst(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            }
            else
            {
                EmitV2LoadSourceValue(il, context, instruction.Destination, instruction.Size);
            }

            il.Emit(OpCodes.Stloc, value);
            context.EmitSetPendingLogic(value, instruction.Size);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2Clr(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, value);
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                context.EmitStoreDataRegister(instruction.Destination.Register, value, instruction.Size);
            }
            else
            {
                var address = il.DeclareLocal(typeof(uint));
                var previous = il.DeclareLocal(typeof(uint));
                EmitV2ResolveMemoryWriteAddress(il, context, instruction.Destination, instruction.Size);
                il.Emit(OpCodes.Stloc, address);
                EmitV2ReadMemoryValueFromAddress(il, context, address, instruction.Size);
                il.Emit(OpCodes.Stloc, previous);
                EmitV2StoreMemoryValue(il, context, address, value, instruction.Size);
            }

            context.EmitSetPendingLogic(value, instruction.Size);
            context.EmitAddCycles(GetV2ClrCycles(instruction));
        }

        private static int GetV2ClrCycles(M68kDecodedInstruction instruction)
        {
            return instruction.Size == M68kOperandSize.Long ? 12 : 8;
        }

        private static void EmitV2Compare(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add: false);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: false);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2CompareAddress(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadAddressRegister(instruction.Register);
            il.Emit(OpCodes.Stloc, destination);
            EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
            if (instruction.Size == M68kOperandSize.Word)
            {
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Conv_U4);
            }

            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, M68kOperandSize.Long, add: false);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source, result, M68kOperandSize.Long, setExtend: false);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 8 : 6);
        }

        private static void EmitV2CompareMemory(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
            il.Emit(OpCodes.Stloc, source);
            EmitV2LoadSourceValue(il, context, instruction.Destination, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add: false);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: false);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 20 : 12);
        }

        private static void EmitV2CompareImmediate(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
            {
                context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            }
            else
            {
                EmitV2LoadSourceValue(il, context, instruction.Destination, instruction.Size);
            }

            il.Emit(OpCodes.Stloc, destination);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.Source.Immediate));
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add: false);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: false);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 16 : 8);
        }

        private static void EmitV2ImmediateLogical(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction, int operation)
        {
            if (instruction.Destination.Kind != M68kJitEaKind.DataRegister)
            {
                var address = il.DeclareLocal(typeof(uint));
                var destinationValue = il.DeclareLocal(typeof(uint));
                var resultValue = il.DeclareLocal(typeof(uint));
                EmitV2ResolveMemoryWriteAddress(il, context, instruction.Destination, instruction.Size);
                il.Emit(OpCodes.Stloc, address);
                EmitV2ReadMemoryValueFromAddress(il, context, address, instruction.Size);
                il.Emit(OpCodes.Stloc, destinationValue);
                il.Emit(OpCodes.Ldloc, destinationValue);
                EmitLoadUIntConstant(il, instruction.Source.Immediate & M68kCpuState.Mask(instruction.Size));
                il.Emit(operation == 0 ? OpCodes.And : operation == 1 ? OpCodes.Or : OpCodes.Xor);
                EmitLoadUIntConstant(il, M68kCpuState.Mask(instruction.Size));
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, resultValue);
                EmitV2StoreMemoryValue(il, context, address, resultValue, instruction.Size);
                context.EmitSetPendingLogic(resultValue, instruction.Size);
                context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 16 : 8);
                return;
            }

            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            EmitLoadUIntConstant(il, instruction.Source.Immediate & M68kCpuState.Mask(instruction.Size));
            il.Emit(operation == 0 ? OpCodes.And : operation == 1 ? OpCodes.Or : OpCodes.Xor);
            EmitLoadUIntConstant(il, M68kCpuState.Mask(instruction.Size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, result);
            context.EmitStoreDataRegister(instruction.Destination.Register, result, instruction.Size);
            context.EmitSetPendingLogic(result, instruction.Size);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 16 : 8);
        }

        private static void EmitV2Logical(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction, int operation)
        {
            if (instruction.Destination.Kind != M68kJitEaKind.DataRegister &&
                (instruction.Operation == M68kJitOperation.Eor || instruction.Variant != 0))
            {
                var address = il.DeclareLocal(typeof(uint));
                var destinationValue = il.DeclareLocal(typeof(uint));
                var sourceValue = il.DeclareLocal(typeof(uint));
                var memoryResult = il.DeclareLocal(typeof(uint));
                EmitV2ResolveMemoryWriteAddress(il, context, instruction.Destination, instruction.Size);
                il.Emit(OpCodes.Stloc, address);
                EmitV2ReadMemoryValueFromAddress(il, context, address, instruction.Size);
                il.Emit(OpCodes.Stloc, destinationValue);
                context.EmitLoadDataRegister(instruction.Register, instruction.Size);
                il.Emit(OpCodes.Stloc, sourceValue);
                il.Emit(OpCodes.Ldloc, destinationValue);
                il.Emit(OpCodes.Ldloc, sourceValue);
                il.Emit(operation == 0 ? OpCodes.And : operation == 1 ? OpCodes.Or : OpCodes.Xor);
                EmitLoadUIntConstant(il, M68kCpuState.Mask(instruction.Size));
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Stloc, memoryResult);
                EmitV2StoreMemoryValue(il, context, address, memoryResult, instruction.Size);
                context.EmitSetPendingLogic(memoryResult, instruction.Size);
                context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
                return;
            }

            var destinationRegister = instruction.Operation == M68kJitOperation.Eor || instruction.Variant != 0
                ? instruction.Destination.Register
                : instruction.Register;
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(destinationRegister, instruction.Size);
            if (instruction.Operation == M68kJitOperation.Eor || instruction.Variant != 0)
            {
                context.EmitLoadDataRegister(instruction.Register, instruction.Size);
            }
            else
            {
                EmitV2LoadSourceValue(il, context, instruction.Source, instruction.Size);
            }

            il.Emit(operation == 0 ? OpCodes.And : operation == 1 ? OpCodes.Or : OpCodes.Xor);
            EmitLoadUIntConstant(il, M68kCpuState.Mask(instruction.Size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, result);
            context.EmitStoreDataRegister(destinationRegister, result, instruction.Size);
            context.EmitSetPendingLogic(result, instruction.Size);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2Not(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            EmitLoadUIntConstant(il, M68kCpuState.Mask(instruction.Size));
            il.Emit(OpCodes.Xor);
            EmitLoadUIntConstant(il, M68kCpuState.Mask(instruction.Size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, value);
            context.EmitStoreDataRegister(instruction.Destination.Register, value, instruction.Size);
            context.EmitSetPendingLogic(value, instruction.Size);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2Neg(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, destination);
            context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add: false);
            context.EmitStoreDataRegister(instruction.Destination.Register, result, instruction.Size);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: true);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2Ext(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction, M68kOperandSize size)
        {
            var value = il.DeclareLocal(typeof(uint));
            if (size == M68kOperandSize.Word)
            {
                context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Byte);
                il.Emit(OpCodes.Conv_I1);
                il.Emit(OpCodes.Conv_U4);
                EmitLoadUIntConstant(il, 0xFFFF);
                il.Emit(OpCodes.And);
            }
            else
            {
                context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Word);
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Conv_U4);
            }

            il.Emit(OpCodes.Stloc, value);
            context.EmitStoreDataRegister(instruction.Register, value, size);
            context.EmitSetPendingLogic(value, size);
            context.EmitAddCycles(4);
        }

        private static void EmitV2Swap(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            il.Emit(OpCodes.Ldloc, context.DataRegisters[instruction.Register]);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Shr_Un);
            EmitLoadUIntConstant(il, 0xFFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Stloc, context.DataRegisters[instruction.Register]);
            context.EmitSetPendingLogic(value, M68kOperandSize.Long);
            context.EmitAddCycles(4);
        }

        private static void EmitV2Exchange(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var left = il.DeclareLocal(typeof(uint));
            var right = il.DeclareLocal(typeof(uint));
            var leftRegisters = instruction.Variant == 1 ? context.AddressRegisters : context.DataRegisters;
            var rightRegisters = instruction.Variant == 0 ? context.DataRegisters : context.AddressRegisters;
            il.Emit(OpCodes.Ldloc, leftRegisters[instruction.Register]);
            il.Emit(OpCodes.Stloc, left);
            il.Emit(OpCodes.Ldloc, rightRegisters[instruction.QuickValue]);
            il.Emit(OpCodes.Stloc, right);
            il.Emit(OpCodes.Ldloc, right);
            il.Emit(OpCodes.Stloc, leftRegisters[instruction.Register]);
            il.Emit(OpCodes.Ldloc, left);
            il.Emit(OpCodes.Stloc, rightRegisters[instruction.QuickValue]);
            context.EmitAddCycles(6);
        }

        private static void EmitV2ShiftRegister(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var count = il.DeclareLocal(typeof(int));
            var value = il.DeclareLocal(typeof(uint));
            var packed = il.DeclareLocal(typeof(ulong));
            context.EmitMaterializePendingFlags();
            if ((instruction.Variant & 8) != 0)
            {
                context.EmitLoadDataRegister(instruction.QuickValue, M68kOperandSize.Long);
                il.Emit(OpCodes.Ldc_I4, 63);
                il.Emit(OpCodes.And);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            }

            il.Emit(OpCodes.Stloc, count);
            context.EmitLoadDataRegister(instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Ldc_I4, instruction.Variant);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Size);
            il.Emit(OpCodes.Call, ShiftV2RegisterMethod);
            il.Emit(OpCodes.Stloc, packed);
            il.Emit(OpCodes.Ldloc, packed);
            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Stloc, value);
            il.Emit(OpCodes.Ldloc, packed);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Shr_Un);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, context.StatusRegister);
            context.EmitStoreDataRegister(instruction.Register, value, instruction.Size);
            il.Emit(OpCodes.Ldloc, context.Cycles);
            il.Emit(OpCodes.Ldc_I8, 6L);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldc_I8, 2L);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, context.Cycles);
        }

        private static void EmitV2BitOperation(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            bool immediateBit)
        {
            var value = il.DeclareLocal(typeof(uint));
            var bit = il.DeclareLocal(typeof(int));
            var mask = il.DeclareLocal(typeof(uint));
            var statusNotZero = il.DefineLabel();
            var memoryTarget = instruction.Destination.Kind != M68kJitEaKind.DataRegister;
            var address = memoryTarget ? il.DeclareLocal(typeof(uint)) : null;
            context.EmitMaterializePendingFlags();
            if (memoryTarget)
            {
                EmitV2ResolveMemoryReadAddress(il, context, instruction.Destination, instruction.Size);
                il.Emit(OpCodes.Stloc, address!);
                EmitV2ReadMemoryValueFromAddress(il, context, address!, instruction.Size);
            }
            else
            {
                context.EmitLoadDataRegister(instruction.Destination.Register, M68kOperandSize.Long);
            }

            il.Emit(OpCodes.Stloc, value);
            if (immediateBit)
            {
                il.Emit(OpCodes.Ldc_I4, instruction.QuickValue & (memoryTarget ? 7 : 31));
            }
            else
            {
                context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Long);
                il.Emit(OpCodes.Ldc_I4, memoryTarget ? 7 : 31);
                il.Emit(OpCodes.And);
            }

            il.Emit(OpCodes.Stloc, bit);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, bit);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Stloc, mask);
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)~M68kCpuState.Zero));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, context.StatusRegister);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldloc, mask);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brtrue, statusNotZero);
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(OpCodes.Ldc_I4, M68kCpuState.Zero);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Stloc, context.StatusRegister);
            il.MarkLabel(statusNotZero);
            switch (instruction.Variant)
            {
                case 1:
                    il.Emit(OpCodes.Ldloc, value);
                    il.Emit(OpCodes.Ldloc, mask);
                    il.Emit(OpCodes.Xor);
                    il.Emit(OpCodes.Stloc, value);
                    if (memoryTarget)
                    {
                        EmitV2StoreMemoryValue(il, context, address!, value, instruction.Size);
                    }
                    else
                    {
                        context.EmitStoreDataRegister(instruction.Destination.Register, value, M68kOperandSize.Long);
                    }

                    break;
                case 2:
                    il.Emit(OpCodes.Ldloc, value);
                    il.Emit(OpCodes.Ldloc, mask);
                    il.Emit(OpCodes.Not);
                    il.Emit(OpCodes.And);
                    il.Emit(OpCodes.Stloc, value);
                    if (memoryTarget)
                    {
                        EmitV2StoreMemoryValue(il, context, address!, value, instruction.Size);
                    }
                    else
                    {
                        context.EmitStoreDataRegister(instruction.Destination.Register, value, M68kOperandSize.Long);
                    }

                    break;
                case 3:
                    il.Emit(OpCodes.Ldloc, value);
                    il.Emit(OpCodes.Ldloc, mask);
                    il.Emit(OpCodes.Or);
                    il.Emit(OpCodes.Stloc, value);
                    if (memoryTarget)
                    {
                        EmitV2StoreMemoryValue(il, context, address!, value, instruction.Size);
                    }
                    else
                    {
                        context.EmitStoreDataRegister(instruction.Destination.Register, value, M68kOperandSize.Long);
                    }

                    break;
            }

            context.EmitAddCycles(memoryTarget ? 14 : immediateBit ? 10 : 8);
        }

        private static void EmitV2StatusImmediate(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label exit)
        {
            context.EmitStoreState(recordLazyWriteback: false);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Operation);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.Source.Immediate));
            il.Emit(OpCodes.Call, ExecuteStatusImmediateForV2BatchMethod);
            context.EmitReloadState();
            if (instruction.Operation is M68kJitOperation.OriToSr or
                M68kJitOperation.AndiToSr or
                M68kJitOperation.EoriToSr or
                M68kJitOperation.MoveToSr)
            {
                il.Emit(OpCodes.Br, exit);
            }
        }

        private static void EmitV2Pea(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            context.EmitStoreState(recordLazyWriteback: false);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Source.Kind);
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Register);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.Source.ExtensionAddress));
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Extension0);
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Extension1);
            il.Emit(OpCodes.Call, ExecutePeaForV2BatchMethod);
            context.EmitReloadState();
        }

        private static void EmitV2Movem(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label exit)
        {
            context.EmitStoreState(recordLazyWriteback: false);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Source.Kind);
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Register);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.Source.ExtensionAddress));
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Extension0);
            il.Emit(OpCodes.Ldc_I4, instruction.Source.Extension1);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Size);
            il.Emit(OpCodes.Ldc_I4, instruction.RegisterMask);
            il.Emit(OpCodes.Ldc_I4, instruction.Variant);
            il.Emit(OpCodes.Call, ExecuteCompiledMovemForV2BatchMethod);
            il.Emit(OpCodes.Brfalse, exit);
            context.EmitReloadState();
        }

        private static void EmitV2Multiply(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            bool signed)
        {
            var source = il.DeclareLocal(typeof(uint));
            var destination = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            EmitV2LoadSourceValue(il, context, instruction.Source, M68kOperandSize.Word);
            EmitLoadUIntConstant(il, 0xFFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, source);
            context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Word);
            EmitLoadUIntConstant(il, 0xFFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, destination);
            if (signed)
            {
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Ldloc, source);
                il.Emit(OpCodes.Conv_I2);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, destination);
                il.Emit(OpCodes.Ldloc, source);
            }

            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Stloc, result);
            context.EmitStoreDataRegister(instruction.Register, result, M68kOperandSize.Long);
            context.EmitSetPendingLogic(result, M68kOperandSize.Long);
            context.EmitAddCycles(70);
        }

        private static void EmitV2Divide(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label exit,
            bool signed)
        {
            var divisor = il.DeclareLocal(typeof(uint));
            var dividend = il.DeclareLocal(typeof(uint));
            var value = il.DeclareLocal(typeof(uint));
            var packed = il.DeclareLocal(typeof(ulong));
            var nonZero = il.DefineLabel();
            context.EmitMaterializePendingFlags();
            EmitV2LoadSourceValue(il, context, instruction.Source, M68kOperandSize.Word);
            EmitLoadUIntConstant(il, 0xFFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, divisor);
            il.Emit(OpCodes.Ldloc, divisor);
            il.Emit(OpCodes.Brtrue, nonZero);

            context.EmitStoreState(recordLazyWriteback: false);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ExecuteV2DivideByZeroMethod);
            context.EmitReloadState();
            il.Emit(OpCodes.Br, exit);

            il.MarkLabel(nonZero);
            context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Long);
            il.Emit(OpCodes.Stloc, dividend);
            il.Emit(OpCodes.Ldloc, dividend);
            il.Emit(OpCodes.Ldloc, divisor);
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(signed ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, DivideV2RegisterMethod);
            il.Emit(OpCodes.Stloc, packed);
            il.Emit(OpCodes.Ldloc, packed);
            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Stloc, value);
            context.EmitStoreDataRegister(instruction.Register, value, M68kOperandSize.Long);
            il.Emit(OpCodes.Ldloc, packed);
            il.Emit(OpCodes.Ldc_I4, 32);
            il.Emit(OpCodes.Shr_Un);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, context.StatusRegister);
            context.EmitAddCycles(140);
        }

        private static void EmitV2Bra(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength,
            bool recordOutOfBlockSideExit)
        {
            var target = GetBranchTarget(instruction);
            context.EmitAddCycles(10);
            context.EmitSetProgramCounter(target);
            EmitV2BranchToTarget(
                il,
                context,
                target,
                labels,
                addressToIndex,
                exit,
                root,
                codeStart,
                byteLength,
                isFallthrough: false,
                recordOutOfBlockSideExit);
        }

        private static void EmitV2Jmp(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength,
            bool recordOutOfBlockSideExit)
        {
            var target = il.DeclareLocal(typeof(uint));
            EmitV2ResolveAddress(il, context, instruction.Source);
            il.Emit(OpCodes.Stloc, target);
            il.Emit(OpCodes.Ldloc, target);
            il.Emit(OpCodes.Stloc, context.ProgramCounter);
            context.EmitAddCycles(12);
            if (recordOutOfBlockSideExit ||
                instruction.Source.Kind is not (M68kJitEaKind.AbsoluteWord or M68kJitEaKind.AbsoluteLong))
            {
                context.EmitRecordOutOfBlockSideExit(root, target, codeStart, byteLength, isFallthrough: false);
            }
            else
            {
                context.EmitMarkOutOfBlockBranchExit(root, target, codeStart, byteLength, isFallthrough: false);
            }

            il.Emit(OpCodes.Br, exit);
        }

        private static void EmitV2Jsr(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength)
        {
            var target = il.DeclareLocal(typeof(uint));
            var jumped = il.DefineLabel();
            EmitV2ResolveAddress(il, context, instruction.Source);
            il.Emit(OpCodes.Stloc, target);
            context.EmitStoreState(recordLazyWriteback: false);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, target);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldc_I4, GetV2JsrCycles(instruction));
            il.Emit(OpCodes.Call, ExecuteV2JumpToMethod);
            il.Emit(OpCodes.Brtrue, jumped);
            il.Emit(OpCodes.Ldloc, context.Executed);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, context.Executed);
            context.EmitReloadState();
            il.Emit(OpCodes.Br, exit);

            il.MarkLabel(jumped);
            context.EmitReloadState();
            context.EmitRecordOutOfBlockSideExit(root, target, codeStart, byteLength, isFallthrough: false);
            il.Emit(OpCodes.Br, exit);
        }

        private static void EmitV2Bsr(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength)
        {
            var target = GetBranchTarget(instruction);
            context.EmitStoreState(recordLazyWriteback: false);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, context.ProgramCounter);
            il.Emit(OpCodes.Call, PushLongMethod);
            context.EmitReloadState();
            context.EmitSetProgramCounter(target);
            context.EmitAddCycles(18);
            context.EmitRecordOutOfBlockSideExit(root, target, codeStart, byteLength, isFallthrough: false);
            il.Emit(OpCodes.Br, exit);
        }

        private static int GetV2JsrCycles(M68kDecodedInstruction instruction)
            => instruction.Source.Kind == M68kJitEaKind.AddressIndirect ? 16 : 18;

        private static void EmitV2Rts(
            ILGenerator il,
            V2EmitContext context,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength)
        {
            var target = il.DeclareLocal(typeof(uint));
            context.EmitStoreState(recordLazyWriteback: false);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, PullLongMethod);
            il.Emit(OpCodes.Stloc, target);
            context.EmitReloadState();
            context.EmitSetProgramCounter(target);
            context.EmitAddCycles(16);
            context.EmitRecordOutOfBlockSideExit(root, target, codeStart, byteLength, isFallthrough: false);
            il.Emit(OpCodes.Br, exit);
        }

        private static void EmitV2Bcc(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength,
            bool recordOutOfBlockSideExit)
        {
            var notTaken = il.DefineLabel();
            var target = GetBranchTarget(instruction);
            context.EmitCheckCondition(instruction.Condition);
            il.Emit(OpCodes.Brfalse, notTaken);
            context.EmitAddCycles(10);
            context.EmitSetProgramCounter(target);
            EmitV2BranchToTarget(
                il,
                context,
                target,
                labels,
                addressToIndex,
                exit,
                root,
                codeStart,
                byteLength,
                isFallthrough: false,
                recordOutOfBlockSideExit);
            il.MarkLabel(notTaken);
            context.EmitAddCycles(8);
        }

        private static void EmitV2Dbcc(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength,
            bool recordOutOfBlockSideExit)
        {
            var conditionFalse = il.DefineLabel();
            var expired = il.DefineLabel();
            var done = il.DefineLabel();
            var counter = il.DeclareLocal(typeof(uint));
            var target = GetBranchTarget(instruction);
            context.EmitCheckCondition(instruction.Condition);
            il.Emit(OpCodes.Brfalse, conditionFalse);
            context.EmitAddCycles(12);
            il.Emit(OpCodes.Br, done);

            il.MarkLabel(conditionFalse);
            context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Word);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            EmitLoadUIntConstant(il, 0xFFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, counter);
            context.EmitStoreDataRegister(instruction.Register, counter, M68kOperandSize.Word);
            il.Emit(OpCodes.Ldloc, counter);
            EmitLoadUIntConstant(il, 0xFFFF);
            il.Emit(OpCodes.Beq, expired);
            context.EmitAddCycles(10);
            context.EmitSetProgramCounter(target);
            EmitV2BranchToTarget(
                il,
                context,
                target,
                labels,
                addressToIndex,
                exit,
                root,
                codeStart,
                byteLength,
                isFallthrough: false,
                recordOutOfBlockSideExit);

            il.MarkLabel(expired);
            context.EmitAddCycles(14);
            il.MarkLabel(done);
        }

        private static void EmitV2BranchToTarget(
            ILGenerator il,
            V2EmitContext context,
            uint target,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit,
            uint root,
            uint codeStart,
            int byteLength,
            bool isFallthrough,
            bool recordOutOfBlockSideExit)
        {
            if (addressToIndex.TryGetValue(target, out var targetIndex))
            {
                il.Emit(OpCodes.Br, labels[targetIndex]);
                return;
            }

            if (recordOutOfBlockSideExit)
            {
                context.EmitRecordOutOfBlockSideExit(root, target, codeStart, byteLength, isFallthrough);
            }
            else
            {
                context.EmitMarkOutOfBlockBranchExit(root, target, codeStart, byteLength, isFallthrough);
            }

            il.Emit(OpCodes.Br, exit);
        }

        private static void EmitV2LoadSourceValue(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedEa source,
            M68kOperandSize size)
        {
            switch (source.Kind)
            {
                case M68kJitEaKind.DataRegister:
                    context.EmitLoadDataRegister(source.Register, size);
                    return;
                case M68kJitEaKind.AddressRegister:
                    context.EmitLoadAddressRegister(source.Register);
                    if (size == M68kOperandSize.Word)
                    {
                        EmitLoadUIntConstant(il, 0xFFFF);
                        il.Emit(OpCodes.And);
                    }

                    return;
                case M68kJitEaKind.Immediate:
                    EmitLoadUIntConstant(il, source.Immediate & M68kCpuState.Mask(size));
                    return;
                default:
                    if (!IsV2MemoryReadEa(source))
                    {
                        throw new InvalidOperationException("The v2 JIT attempted to load an unsupported source operand.");
                    }

                    var address = il.DeclareLocal(typeof(uint));
                    EmitV2ResolveMemoryReadAddress(il, context, source, size);
                    il.Emit(OpCodes.Stloc, address);
                    EmitV2ReadMemoryValueFromAddress(il, context, address, size);
                    return;
            }
        }

        private static void EmitV2ReadMemoryValueFromAddress(
            ILGenerator il,
            V2EmitContext context,
            LocalBuilder address,
            M68kOperandSize size)
        {
            var slowRead = il.DefineLabel();
            var done = il.DefineLabel();
            if (context.ZeroWaitProbeEnabled)
            {
                if (IsV2InlineZeroWaitMemoryEnabled())
                {
                    var fastMemory = il.DeclareLocal(typeof(byte[]));
                    var fastOffset = il.DeclareLocal(typeof(int));
                    var fastTarget = il.DeclareLocal(typeof(int));
                    var amigaBus = il.DeclareLocal(typeof(AmigaBus));
                    var fastRead = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, AmigaBusField);
                    il.Emit(OpCodes.Stloc, amigaBus);
                    il.Emit(OpCodes.Ldloc, amigaBus);
                    il.Emit(OpCodes.Brfalse, slowRead);
                    EmitSelectV2InlineZeroWaitMemory(
                        il,
                        amigaBus,
                        address,
                        GetV2MemoryByteCount(size),
                        fastMemory,
                        fastOffset,
                        fastTarget,
                        allowRom: true,
                        fastRead,
                        slowRead);
                    il.MarkLabel(fastRead);
                    EmitLoadV2ZeroWaitMemoryValue(il, fastMemory, fastOffset, size);
                    context.EmitIncrementZeroWaitRead(fastTarget);
                    il.Emit(OpCodes.Br, done);
                }
                else
                {
                    var fastValue = il.DeclareLocal(typeof(uint));
                    var amigaBus = il.DeclareLocal(typeof(AmigaBus));
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, AmigaBusField);
                    il.Emit(OpCodes.Stloc, amigaBus);
                    il.Emit(OpCodes.Ldloc, amigaBus);
                    il.Emit(OpCodes.Brfalse, slowRead);
                    il.Emit(OpCodes.Ldloc, amigaBus);
                    il.Emit(OpCodes.Ldloc, address);
                    il.Emit(OpCodes.Ldc_I4, (int)size);
                    il.Emit(OpCodes.Ldloca_S, fastValue);
                    il.Emit(OpCodes.Callvirt, TryReadJitZeroWaitMemoryMethod);
                    il.Emit(OpCodes.Brfalse, slowRead);
                    il.Emit(OpCodes.Ldloc, fastValue);
                    il.Emit(OpCodes.Br, done);
                }
            }

            il.MarkLabel(slowRead);
            if (context.FastReadOnlyFailureEnabled)
            {
                il.Emit(OpCodes.Br, context.FastReadOnlyFailureLabel);
                il.MarkLabel(done);
                return;
            }

            if (context.ZeroWaitProbeEnabled)
            {
                context.EmitIncrementZeroWaitSlowRead();
            }

            context.EmitStoreInstructionContext();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca_S, context.Cycles);
            il.Emit(OpCodes.Ldloc, address);
            il.Emit(OpCodes.Ldc_I4, (int)size);
            il.Emit(OpCodes.Call, ReadMemoryValueForV2BatchSlowRefMethod);
            il.MarkLabel(done);
        }

        private static void EmitV2StoreMemoryValue(
            ILGenerator il,
            V2EmitContext context,
            LocalBuilder address,
            LocalBuilder value,
            M68kOperandSize size)
        {
            var slowWrite = il.DefineLabel();
            var done = il.DefineLabel();
            if (context.ZeroWaitProbeEnabled)
            {
                if (IsV2InlineZeroWaitMemoryEnabled())
                {
                    var fastMemory = il.DeclareLocal(typeof(byte[]));
                    var fastOffset = il.DeclareLocal(typeof(int));
                    var fastTarget = il.DeclareLocal(typeof(int));
                    var amigaBus = il.DeclareLocal(typeof(AmigaBus));
                    var fastWrite = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, AmigaBusField);
                    il.Emit(OpCodes.Stloc, amigaBus);
                    il.Emit(OpCodes.Ldloc, amigaBus);
                    il.Emit(OpCodes.Brfalse, slowWrite);
                    EmitSelectV2InlineZeroWaitMemory(
                        il,
                        amigaBus,
                        address,
                        GetV2MemoryByteCount(size),
                        fastMemory,
                        fastOffset,
                        fastTarget,
                        allowRom: false,
                        fastWrite,
                        slowWrite);
                    il.MarkLabel(fastWrite);
                    EmitStoreV2ZeroWaitMemoryValue(il, fastMemory, fastOffset, value, size);
                    il.Emit(OpCodes.Ldloc, amigaBus);
                    il.Emit(OpCodes.Ldloc, address);
                    il.Emit(OpCodes.Ldc_I4, GetV2MemoryByteCount(size));
                    il.Emit(OpCodes.Callvirt, CompleteJitZeroWaitWriteMethod);
                    context.EmitIncrementZeroWaitWriteRealFast();
                    il.Emit(OpCodes.Br, done);
                }
                else
                {
                    var amigaBus = il.DeclareLocal(typeof(AmigaBus));
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, AmigaBusField);
                    il.Emit(OpCodes.Stloc, amigaBus);
                    il.Emit(OpCodes.Ldloc, amigaBus);
                    il.Emit(OpCodes.Brfalse, slowWrite);
                    il.Emit(OpCodes.Ldloc, amigaBus);
                    il.Emit(OpCodes.Ldloc, address);
                    il.Emit(OpCodes.Ldloc, value);
                    il.Emit(OpCodes.Ldc_I4, (int)size);
                    il.Emit(OpCodes.Callvirt, TryWriteJitZeroWaitMemoryMethod);
                    il.Emit(OpCodes.Brtrue, done);
                }
            }

            il.MarkLabel(slowWrite);
            if (context.ZeroWaitProbeEnabled)
            {
                context.EmitIncrementZeroWaitSlowWrite();
            }

            context.EmitStoreInstructionContext();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca_S, context.Cycles);
            il.Emit(OpCodes.Ldloc, address);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldc_I4, (int)size);
            il.Emit(OpCodes.Call, WriteMemoryValueForV2BatchSlowRefMethod);
            il.MarkLabel(done);
        }

        private static int GetV2MemoryByteCount(M68kOperandSize size)
        {
            return size == M68kOperandSize.Long ? 4 : size == M68kOperandSize.Word ? 2 : 1;
        }

        private static void EmitSelectV2InlineZeroWaitMemory(
            ILGenerator il,
            LocalBuilder amigaBus,
            LocalBuilder address,
            int byteCount,
            LocalBuilder memory,
            LocalBuilder offset,
            LocalBuilder targetKind,
            bool allowRom,
            Label success,
            Label fail)
        {
            var normalizedAddress = il.DeclareLocal(typeof(uint));
            var baseAddress = il.DeclareLocal(typeof(uint));
            var relativeOffset = il.DeclareLocal(typeof(uint));
            var tryRom = allowRom ? il.DefineLabel() : fail;
            il.Emit(OpCodes.Ldloc, address);
            EmitLoadUIntConstant(il, 0x00FF_FFFF);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, normalizedAddress);
            if (byteCount != 1)
            {
                il.Emit(OpCodes.Ldloc, normalizedAddress);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.And);
                il.Emit(OpCodes.Brtrue, fail);
            }

            il.Emit(OpCodes.Ldloc, amigaBus);
            il.Emit(OpCodes.Ldfld, AmigaBusRealFastRamField);
            il.Emit(OpCodes.Stloc, memory);
            il.Emit(OpCodes.Ldloc, memory);
            il.Emit(OpCodes.Brfalse, tryRom);
            il.Emit(OpCodes.Ldloc, memory);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Brfalse, tryRom);
            il.Emit(OpCodes.Ldloc, amigaBus);
            il.Emit(OpCodes.Ldfld, AmigaBusRealFastRamBaseField);
            il.Emit(OpCodes.Stloc, baseAddress);
            il.Emit(OpCodes.Ldloc, normalizedAddress);
            il.Emit(OpCodes.Ldloc, baseAddress);
            il.Emit(OpCodes.Blt_Un, tryRom);
            il.Emit(OpCodes.Ldloc, normalizedAddress);
            il.Emit(OpCodes.Ldloc, baseAddress);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, relativeOffset);
            EmitBranchIfV2InlineMemoryRangeTooSmall(il, memory, relativeOffset, byteCount, tryRom);
            il.Emit(OpCodes.Ldloc, relativeOffset);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, offset);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, targetKind);
            il.Emit(OpCodes.Br, success);

            if (allowRom)
            {
                var overlayRegion = il.DeclareLocal(AmigaBusRomOverlayRegionField.FieldType);
                var tryOverlay = il.DefineLabel();
                il.MarkLabel(tryRom);
                il.Emit(OpCodes.Ldloc, amigaBus);
                il.Emit(OpCodes.Ldfld, AmigaBusRomOverlayRegionField);
                il.Emit(OpCodes.Stloc, overlayRegion);
                il.Emit(OpCodes.Ldloc, overlayRegion);
                il.Emit(OpCodes.Brfalse, fail);
                il.Emit(OpCodes.Ldloc, overlayRegion);
                il.Emit(OpCodes.Ldfld, MappedMemoryRegionDataField);
                il.Emit(OpCodes.Stloc, memory);
                il.Emit(OpCodes.Ldloc, memory);
                il.Emit(OpCodes.Brfalse, fail);
                il.Emit(OpCodes.Ldloc, overlayRegion);
                il.Emit(OpCodes.Ldfld, MappedMemoryRegionBaseAddressField);
                il.Emit(OpCodes.Stloc, baseAddress);
                il.Emit(OpCodes.Ldloc, normalizedAddress);
                il.Emit(OpCodes.Ldloc, baseAddress);
                il.Emit(OpCodes.Blt_Un, tryOverlay);
                il.Emit(OpCodes.Ldloc, normalizedAddress);
                il.Emit(OpCodes.Ldloc, baseAddress);
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Stloc, relativeOffset);
                EmitBranchIfV2InlineMemoryRangeTooSmall(il, memory, relativeOffset, byteCount, tryOverlay);
                il.Emit(OpCodes.Ldloc, relativeOffset);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Stloc, offset);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stloc, targetKind);
                il.Emit(OpCodes.Br, success);

                il.MarkLabel(tryOverlay);
                il.Emit(OpCodes.Ldloc, amigaBus);
                il.Emit(OpCodes.Ldfld, AmigaBusRomOverlayEnabledField);
                il.Emit(OpCodes.Brfalse, fail);
                il.Emit(OpCodes.Ldloc, normalizedAddress);
                EmitLoadUIntConstant(il, 0x0008_0000);
                il.Emit(OpCodes.Bge_Un, fail);
                il.Emit(OpCodes.Ldloc, normalizedAddress);
                il.Emit(OpCodes.Ldc_I4, byteCount - 1);
                il.Emit(OpCodes.Add);
                EmitLoadUIntConstant(il, 0x0008_0000);
                il.Emit(OpCodes.Bge_Un, fail);
                il.Emit(OpCodes.Ldloc, normalizedAddress);
                il.Emit(OpCodes.Ldloc, memory);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_U4);
                il.Emit(OpCodes.Rem_Un);
                il.Emit(OpCodes.Stloc, relativeOffset);
                EmitBranchIfV2InlineMemoryRangeTooSmall(il, memory, relativeOffset, byteCount, fail);
                il.Emit(OpCodes.Ldloc, relativeOffset);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Stloc, offset);
                il.Emit(OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Stloc, targetKind);
                il.Emit(OpCodes.Br, success);
            }
        }

        private static void EmitBranchIfV2InlineMemoryRangeTooSmall(
            ILGenerator il,
            LocalBuilder memory,
            LocalBuilder offset,
            int byteCount,
            Label fail)
        {
            il.Emit(OpCodes.Ldloc, offset);
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Ldc_I8, (long)byteCount);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, memory);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Bgt_Un, fail);
        }

        private static void EmitLoadV2ZeroWaitMemoryValue(
            ILGenerator il,
            LocalBuilder memory,
            LocalBuilder offset,
            M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                EmitLoadV2ZeroWaitMemoryByte(il, memory, offset, 0);
                il.Emit(OpCodes.Conv_U4);
                return;
            }

            EmitLoadV2ZeroWaitMemoryByte(il, memory, offset, 0);
            il.Emit(OpCodes.Ldc_I4, size == M68kOperandSize.Long ? 24 : 8);
            il.Emit(OpCodes.Shl);
            EmitLoadV2ZeroWaitMemoryByte(il, memory, offset, 1);
            if (size == M68kOperandSize.Long)
            {
                il.Emit(OpCodes.Ldc_I4, 16);
                il.Emit(OpCodes.Shl);
            }

            il.Emit(OpCodes.Or);
            if (size == M68kOperandSize.Long)
            {
                EmitLoadV2ZeroWaitMemoryByte(il, memory, offset, 2);
                il.Emit(OpCodes.Ldc_I4, 8);
                il.Emit(OpCodes.Shl);
                il.Emit(OpCodes.Or);
                EmitLoadV2ZeroWaitMemoryByte(il, memory, offset, 3);
                il.Emit(OpCodes.Or);
            }

            il.Emit(OpCodes.Conv_U4);
        }

        private static void EmitStoreV2ZeroWaitMemoryValue(
            ILGenerator il,
            LocalBuilder memory,
            LocalBuilder offset,
            LocalBuilder value,
            M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                EmitStoreV2ZeroWaitMemoryByte(il, memory, offset, 0, value, 0);
                return;
            }

            if (size == M68kOperandSize.Word)
            {
                EmitStoreV2ZeroWaitMemoryByte(il, memory, offset, 0, value, 8);
                EmitStoreV2ZeroWaitMemoryByte(il, memory, offset, 1, value, 0);
                return;
            }

            EmitStoreV2ZeroWaitMemoryByte(il, memory, offset, 0, value, 24);
            EmitStoreV2ZeroWaitMemoryByte(il, memory, offset, 1, value, 16);
            EmitStoreV2ZeroWaitMemoryByte(il, memory, offset, 2, value, 8);
            EmitStoreV2ZeroWaitMemoryByte(il, memory, offset, 3, value, 0);
        }

        private static void EmitLoadV2ZeroWaitMemoryByte(
            ILGenerator il,
            LocalBuilder memory,
            LocalBuilder offset,
            int displacement)
        {
            il.Emit(OpCodes.Ldloc, memory);
            EmitLoadV2ZeroWaitMemoryOffset(il, offset, displacement);
            il.Emit(OpCodes.Ldelem_U1);
        }

        private static void EmitStoreV2ZeroWaitMemoryByte(
            ILGenerator il,
            LocalBuilder memory,
            LocalBuilder offset,
            int displacement,
            LocalBuilder value,
            int shift)
        {
            il.Emit(OpCodes.Ldloc, memory);
            EmitLoadV2ZeroWaitMemoryOffset(il, offset, displacement);
            il.Emit(OpCodes.Ldloc, value);
            if (shift != 0)
            {
                il.Emit(OpCodes.Ldc_I4, shift);
                il.Emit(OpCodes.Shr_Un);
            }

            il.Emit(OpCodes.Conv_U1);
            il.Emit(OpCodes.Stelem_I1);
        }

        private static void EmitLoadV2ZeroWaitMemoryOffset(ILGenerator il, LocalBuilder offset, int displacement)
        {
            il.Emit(OpCodes.Ldloc, offset);
            if (displacement == 0)
            {
                return;
            }

            il.Emit(OpCodes.Ldc_I4, displacement);
            il.Emit(OpCodes.Add);
        }

        private static void EmitV2ResolveMemoryReadAddress(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedEa ea,
            M68kOperandSize size)
        {
            switch (ea.Kind)
            {
                case M68kJitEaKind.AddressIndirect:
                    context.EmitLoadAddressRegister(ea.Register);
                    return;
                case M68kJitEaKind.AddressPostincrement:
                {
                    var address = il.DeclareLocal(typeof(uint));
                    var updated = il.DeclareLocal(typeof(uint));
                    context.EmitLoadAddressRegister(ea.Register);
                    il.Emit(OpCodes.Stloc, address);
                    il.Emit(OpCodes.Ldloc, address);
                    EmitLoadUIntConstant(il, AddressIncrement(ea.Register, size));
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, updated);
                    context.EmitStoreAddressRegister(ea.Register, updated);
                    il.Emit(OpCodes.Ldloc, address);
                    return;
                }
                case M68kJitEaKind.AddressPredecrement:
                {
                    var address = il.DeclareLocal(typeof(uint));
                    context.EmitLoadAddressRegister(ea.Register);
                    EmitLoadUIntConstant(il, AddressIncrement(ea.Register, size));
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Stloc, address);
                    context.EmitStoreAddressRegister(ea.Register, address);
                    il.Emit(OpCodes.Ldloc, address);
                    return;
                }
                case M68kJitEaKind.AddressDisplacement:
                    context.EmitLoadAddressRegister(ea.Register);
                    il.Emit(OpCodes.Ldc_I4, (int)(short)ea.Extension0);
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AddressIndex:
                    context.EmitLoadAddressRegister(ea.Register);
                    EmitV2ResolveIndex(il, context, ea.Extension0);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)(sbyte)(ea.Extension0 & 0xFF)));
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AbsoluteWord:
                    EmitLoadUIntConstant(il, unchecked((uint)(short)ea.Extension0));
                    return;
                case M68kJitEaKind.AbsoluteLong:
                    EmitLoadUIntConstant(il, ((uint)ea.Extension0 << 16) | ea.Extension1);
                    return;
                case M68kJitEaKind.PcDisplacement:
                    EmitLoadUIntConstant(il, unchecked((uint)(ea.ExtensionAddress + (short)ea.Extension0)));
                    return;
                case M68kJitEaKind.PcIndex:
                    EmitLoadUIntConstant(il, ea.ExtensionAddress);
                    EmitV2ResolveIndex(il, context, ea.Extension0);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)(sbyte)(ea.Extension0 & 0xFF)));
                    il.Emit(OpCodes.Add);
                    return;
                default:
                    throw new InvalidOperationException("The v2 JIT attempted to resolve an unsupported memory-read operand.");
            }
        }

        private static void EmitV2ResolveMemoryWriteAddress(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedEa ea,
            M68kOperandSize size)
        {
            switch (ea.Kind)
            {
                case M68kJitEaKind.AddressIndirect:
                    context.EmitLoadAddressRegister(ea.Register);
                    return;
                case M68kJitEaKind.AddressPostincrement:
                {
                    var address = il.DeclareLocal(typeof(uint));
                    var updated = il.DeclareLocal(typeof(uint));
                    context.EmitLoadAddressRegister(ea.Register);
                    il.Emit(OpCodes.Stloc, address);
                    il.Emit(OpCodes.Ldloc, address);
                    EmitLoadUIntConstant(il, AddressIncrement(ea.Register, size));
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Stloc, updated);
                    context.EmitStoreAddressRegister(ea.Register, updated);
                    il.Emit(OpCodes.Ldloc, address);
                    return;
                }
                case M68kJitEaKind.AddressPredecrement:
                {
                    var address = il.DeclareLocal(typeof(uint));
                    context.EmitLoadAddressRegister(ea.Register);
                    EmitLoadUIntConstant(il, AddressIncrement(ea.Register, size));
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Stloc, address);
                    context.EmitStoreAddressRegister(ea.Register, address);
                    il.Emit(OpCodes.Ldloc, address);
                    return;
                }
                case M68kJitEaKind.AddressDisplacement:
                    context.EmitLoadAddressRegister(ea.Register);
                    il.Emit(OpCodes.Ldc_I4, (int)(short)ea.Extension0);
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AddressIndex:
                    context.EmitLoadAddressRegister(ea.Register);
                    EmitV2ResolveIndex(il, context, ea.Extension0);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)(sbyte)(ea.Extension0 & 0xFF)));
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AbsoluteWord:
                    EmitLoadUIntConstant(il, unchecked((uint)(short)ea.Extension0));
                    return;
                case M68kJitEaKind.AbsoluteLong:
                    EmitLoadUIntConstant(il, ((uint)ea.Extension0 << 16) | ea.Extension1);
                    return;
                default:
                    throw new InvalidOperationException("The v2 JIT attempted to resolve an unsupported memory-write operand.");
            }
        }

        private static void EmitV2ResolveIndex(ILGenerator il, V2EmitContext context, ushort extension)
        {
            var indexRegister = (extension >> 12) & 7;
            if ((extension & 0x8000) != 0)
            {
                context.EmitLoadAddressRegister(indexRegister);
            }
            else
            {
                context.EmitLoadDataRegister(indexRegister, M68kOperandSize.Long);
            }

            if ((extension & 0x0800) == 0)
            {
                il.Emit(OpCodes.Conv_I2);
                il.Emit(OpCodes.Conv_U4);
            }
        }

        private static void EmitV2ResolveAddress(ILGenerator il, V2EmitContext context, M68kDecodedEa ea)
        {
            switch (ea.Kind)
            {
                case M68kJitEaKind.AddressIndirect:
                    context.EmitLoadAddressRegister(ea.Register);
                    return;
                case M68kJitEaKind.AddressDisplacement:
                    context.EmitLoadAddressRegister(ea.Register);
                    il.Emit(OpCodes.Ldc_I4, (int)(short)ea.Extension0);
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AddressIndex:
                    context.EmitLoadAddressRegister(ea.Register);
                    EmitV2ResolveIndex(il, context, ea.Extension0);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)(sbyte)(ea.Extension0 & 0xFF)));
                    il.Emit(OpCodes.Add);
                    return;
                case M68kJitEaKind.AbsoluteWord:
                    EmitLoadUIntConstant(il, unchecked((uint)(short)ea.Extension0));
                    return;
                case M68kJitEaKind.AbsoluteLong:
                    EmitLoadUIntConstant(il, ((uint)ea.Extension0 << 16) | ea.Extension1);
                    return;
                case M68kJitEaKind.PcDisplacement:
                    EmitLoadUIntConstant(il, unchecked((uint)(ea.ExtensionAddress + (short)ea.Extension0)));
                    return;
                case M68kJitEaKind.PcIndex:
                    EmitLoadUIntConstant(il, ea.ExtensionAddress);
                    EmitV2ResolveIndex(il, context, ea.Extension0);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_I4, unchecked((int)(sbyte)(ea.Extension0 & 0xFF)));
                    il.Emit(OpCodes.Add);
                    return;
                default:
                    throw new InvalidOperationException("The v2 JIT attempted to resolve an unsupported address-only operand.");
            }
        }

        private static void EmitV2ArithmeticResult(
            ILGenerator il,
            LocalBuilder destination,
            LocalBuilder source,
            LocalBuilder result,
            M68kOperandSize size,
            bool add)
        {
            il.Emit(OpCodes.Ldloc, destination);
            EmitLoadUIntConstant(il, M68kCpuState.Mask(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, destination);
            il.Emit(OpCodes.Ldloc, source);
            EmitLoadUIntConstant(il, M68kCpuState.Mask(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, source);
            il.Emit(OpCodes.Ldloc, destination);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(add ? OpCodes.Add : OpCodes.Sub);
            EmitLoadUIntConstant(il, M68kCpuState.Mask(size));
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Stloc, result);
        }

        private void RecordV2CompiledTrace(V2TraceTier tier)
        {
            switch (tier)
            {
                case V2TraceTier.Tier3:
                    _counters.V2Tier3CompiledTraces++;
                    break;
                case V2TraceTier.Tier2:
                    _counters.V2Tier2CompiledTraces++;
                    break;
                case V2TraceTier.Tier1:
                    _counters.V2Tier1CompiledTraces++;
                    break;
                default:
                    _counters.V2Tier0CompiledTraces++;
                    break;
            }
        }

        private void RecordV2Rejection(V2TraceRejectionReason reason, M68kDecodedInstruction instruction = default)
        {
            _counters.V2RejectedCandidates++;
            switch (reason)
            {
                case V2TraceRejectionReason.ChipRam:
                    _counters.V2RejectedChipRam++;
                    break;
                case V2TraceRejectionReason.HostTrap:
                    _counters.V2RejectedHostTrap++;
                    break;
                case V2TraceRejectionReason.DecodeFailure:
                    _counters.V2RejectedDecode++;
                    break;
                case V2TraceRejectionReason.UnsupportedEa:
                    _counters.V2RejectedUnsupportedEa++;
                    RecordV2UnsupportedInstruction(reason, instruction);
                    break;
                case V2TraceRejectionReason.UnsupportedOperation:
                    _counters.V2RejectedUnsupportedOperation++;
                    RecordV2UnsupportedInstruction(reason, instruction);
                    break;
                case V2TraceRejectionReason.Budget:
                    _counters.V2RejectedBudget++;
                    break;
                default:
                    _counters.V2RejectedEmpty++;
                    break;
            }
        }

        private void RecordV2UnsupportedInstruction(V2TraceRejectionReason reason, M68kDecodedInstruction instruction)
        {
            if (!HasDecodedInstruction(instruction))
            {
                return;
            }

            if (reason == V2TraceRejectionReason.UnsupportedEa)
            {
                IncrementV2Diagnostic(_v2UnsupportedEaCauses, FormatV2InstructionCause(instruction));
            }
            else if (reason == V2TraceRejectionReason.UnsupportedOperation)
            {
                IncrementV2Diagnostic(_v2UnsupportedOperationCauses, FormatV2InstructionCause(instruction));
            }
        }

        private void RecordV2GraphHoleCause(uint target)
        {
            if (_amigaBus == null)
            {
                IncrementV2Diagnostic(_v2GraphHoleCauses, "no-bus");
                return;
            }

            if (!M68kDecoder.TryDecode(_amigaBus, target, out var instruction, out var reason))
            {
                var opcode = _amigaBus.ReadHostWord(target);
                IncrementV2Diagnostic(
                    _v2GraphHoleCauses,
                    "decode:" + reason + "@0x" + Normalize(target).ToString("X6") + "/0x" + opcode.ToString("X4"));
                return;
            }

            if (IsV2Instruction(instruction, out _))
            {
                IncrementV2Diagnostic(_v2GraphHoleCauses, "uncollected:" + FormatV2InstructionCause(instruction));
                return;
            }

            IncrementV2Diagnostic(_v2GraphHoleCauses, FormatV2InstructionCause(instruction));
        }

        private void RecordV2TraceHandoffBlock(string reason, uint target, bool countFailure = true)
        {
            if (string.IsNullOrEmpty(reason))
            {
                reason = "unknown";
            }

            if (_amigaBus == null)
            {
                RecordV2TraceHandoffDiagnostic(reason + ":no-bus", countFailure);
                return;
            }

            if (_amigaBus.IsChipRamAddress(target))
            {
                RecordV2TraceHandoffDiagnostic(reason + ":chip", countFailure);
                return;
            }

            if (_amigaBus.HasHostTrapStub(target))
            {
                RecordV2TraceHandoffDiagnostic(reason + ":host", countFailure);
                return;
            }

            if (!M68kDecoder.TryDecode(_amigaBus, target, out var instruction, out var decodeReason))
            {
                var opcode = _amigaBus.ReadHostWord(target);
                RecordV2TraceHandoffDiagnostic(
                    reason + ":decode:" + decodeReason + "@0x" + Normalize(target).ToString("X6") + "/0x" + opcode.ToString("X4"),
                    countFailure);
                return;
            }

            RecordV2TraceHandoffDiagnostic(
                reason + ":" + FormatV2InstructionCause(instruction),
                countFailure);
        }

        private void RecordV2TraceHandoffDiagnostic(string diagnostic, bool countFailure)
        {
            IncrementV2Diagnostic(_v2TraceHandoffBlockCauses, diagnostic);
            if (!countFailure)
            {
                return;
            }

            _counters.V2TraceHandoffFailures++;
            IncrementV2Diagnostic(_v2TraceHandoffFailureCauses, diagnostic);
        }

        private void RecordV2BranchPressureLimitCause(uint target)
        {
            if (_amigaBus == null)
            {
                IncrementV2Diagnostic(_v2BranchPressureLimitCauses, "no-bus");
                return;
            }

            if (_amigaBus.IsChipRamAddress(target))
            {
                IncrementV2Diagnostic(_v2BranchPressureLimitCauses, "chip");
                return;
            }

            if (_amigaBus.HasHostTrapStub(target))
            {
                IncrementV2Diagnostic(_v2BranchPressureLimitCauses, "host");
                return;
            }

            if (!M68kDecoder.TryDecode(_amigaBus, target, out var instruction, out var decodeReason))
            {
                var opcode = _amigaBus.ReadHostWord(target);
                IncrementV2Diagnostic(
                    _v2BranchPressureLimitCauses,
                    "decode:" + decodeReason + "@0x" + Normalize(target).ToString("X6") + "/0x" + opcode.ToString("X4"));
                return;
            }

            IncrementV2Diagnostic(_v2BranchPressureLimitCauses, FormatV2InstructionCause(instruction));
        }

        private void RecordV2BranchPressureTargetState(string state, uint target)
        {
            if (string.IsNullOrEmpty(state))
            {
                state = "unknown";
            }

            if (_amigaBus == null)
            {
                IncrementV2Diagnostic(_v2BranchPressureTargetStates, state + ":no-bus");
                return;
            }

            if (_amigaBus.IsChipRamAddress(target))
            {
                IncrementV2Diagnostic(_v2BranchPressureTargetStates, state + ":chip");
                return;
            }

            if (_amigaBus.HasHostTrapStub(target))
            {
                IncrementV2Diagnostic(_v2BranchPressureTargetStates, state + ":host");
                return;
            }

            if (!M68kDecoder.TryDecode(_amigaBus, target, out var instruction, out var decodeReason))
            {
                var opcode = _amigaBus.ReadHostWord(target);
                IncrementV2Diagnostic(
                    _v2BranchPressureTargetStates,
                    state + ":decode:" + decodeReason + "@0x" + Normalize(target).ToString("X6") + "/0x" + opcode.ToString("X4"));
                return;
            }

            IncrementV2Diagnostic(
                _v2BranchPressureTargetStates,
                state + ":" + FormatV2InstructionCause(instruction));
        }

        private static bool HasDecodedInstruction(M68kDecodedInstruction instruction)
            => instruction.Opcode != 0 || instruction.ProgramCounter != 0;

        private static void IncrementV2Diagnostic(Dictionary<string, long> counters, string key)
        {
            counters.TryGetValue(key, out var count);
            counters[key] = count + 1;
        }

        private static void RecordTraceBatchDiagnostics(
            Dictionary<string, long> lengthBuckets,
            Dictionary<string, long> wakeSources,
            int instructionCount,
            IM68kInstructionBoundary boundary)
        {
            IncrementV2Diagnostic(lengthBuckets, GetBatchLengthBucket(instructionCount));
            if (boundary is IM68kTraceBatchDiagnosticsBoundary diagnosticsBoundary)
            {
                IncrementV2Diagnostic(wakeSources, FormatTraceBatchWakeSource(diagnosticsBoundary.LastTraceBatchWakeSource));
            }
        }

        private static string GetBatchLengthBucket(int instructionCount)
        {
            if (instructionCount <= 1)
            {
                return "1";
            }

            if (instructionCount == 2)
            {
                return "2";
            }

            if (instructionCount <= 4)
            {
                return "3-4";
            }

            if (instructionCount <= 8)
            {
                return "5-8";
            }

            if (instructionCount <= 16)
            {
                return "9-16";
            }

            if (instructionCount <= 32)
            {
                return "17-32";
            }

            if (instructionCount <= 64)
            {
                return "33-64";
            }

            return "65+";
        }

        private static string FormatTraceBatchWakeSource(M68kTraceBatchWakeSource source)
        {
            return source switch
            {
                M68kTraceBatchWakeSource.TargetCycle => "target-cycle",
                M68kTraceBatchWakeSource.PendingInterrupt => "pending-interrupt",
                M68kTraceBatchWakeSource.VerticalBlank => "vblank",
                M68kTraceBatchWakeSource.HorizontalSyncTod => "hsync-tod",
                M68kTraceBatchWakeSource.CiaTimer => "cia-timer",
                M68kTraceBatchWakeSource.Disk => "disk",
                M68kTraceBatchWakeSource.Paula => "paula",
                M68kTraceBatchWakeSource.Copper => "copper",
                M68kTraceBatchWakeSource.Blitter => "blitter",
                _ => "unknown"
            };
        }

        private static string FormatBatchLengthHistogram(Dictionary<string, long> counters)
        {
            if (counters.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendBatchLengthBucket(builder, counters, "1");
            AppendBatchLengthBucket(builder, counters, "2");
            AppendBatchLengthBucket(builder, counters, "3-4");
            AppendBatchLengthBucket(builder, counters, "5-8");
            AppendBatchLengthBucket(builder, counters, "9-16");
            AppendBatchLengthBucket(builder, counters, "17-32");
            AppendBatchLengthBucket(builder, counters, "33-64");
            AppendBatchLengthBucket(builder, counters, "65+");
            return builder.ToString();
        }

        private static void AppendBatchLengthBucket(StringBuilder builder, Dictionary<string, long> counters, string bucket)
        {
            if (!counters.TryGetValue(bucket, out var count) || count == 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder.Append(bucket);
            builder.Append(':');
            builder.Append(count);
        }

        private static string FormatV2DiagnosticTop(Dictionary<string, long> counters)
        {
            if (counters.Count == 0)
            {
                return string.Empty;
            }

            var entries = new List<KeyValuePair<string, long>>(counters);
            entries.Sort((left, right) =>
            {
                var countComparison = right.Value.CompareTo(left.Value);
                return countComparison != 0
                    ? countComparison
                    : string.CompareOrdinal(left.Key, right.Key);
            });

            var builder = new StringBuilder();
            var count = Math.Min(V2DiagnosticTopCount, entries.Count);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append(SanitizeV2DiagnosticText(entries[i].Key));
                builder.Append(':');
                builder.Append(entries[i].Value);
            }

            return builder.ToString();
        }

        private static string SanitizeV2DiagnosticText(string value)
            => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static string FormatV2InstructionCause(M68kDecodedInstruction instruction)
        {
            var operation = FormatV2OperationName(instruction.Operation);
            var size = FormatV2OperandSize(instruction);
            var source = FormatV2Source(instruction);
            var destination = FormatV2Destination(instruction);
            if (source.Length > 0 && destination.Length > 0)
            {
                return operation + size + " " + source + "->" + destination;
            }

            if (source.Length > 0)
            {
                return operation + size + " " + source;
            }

            if (destination.Length > 0)
            {
                return operation + size + " " + destination;
            }

            return operation + size;
        }

        private static string FormatV2Source(M68kDecodedInstruction instruction)
        {
            if (instruction.Source.Kind != M68kJitEaKind.None)
            {
                return FormatV2Ea(instruction.Source);
            }

            return instruction.Operation switch
            {
                M68kJitOperation.Moveq => "#imm",
                M68kJitOperation.Addq or M68kJitOperation.Subq => "#q",
                M68kJitOperation.ShiftRegister => (instruction.Variant & 8) != 0 ? "Dn" : "#n",
                M68kJitOperation.OriToCcr or
                M68kJitOperation.OriToSr or
                M68kJitOperation.AndiToCcr or
                M68kJitOperation.AndiToSr or
                M68kJitOperation.EoriToCcr or
                M68kJitOperation.EoriToSr or
                M68kJitOperation.MoveToCcr or
                M68kJitOperation.MoveToSr => "#imm",
                _ => string.Empty
            };
        }

        private static string FormatV2Destination(M68kDecodedInstruction instruction)
        {
            if (instruction.Destination.Kind != M68kJitEaKind.None)
            {
                return FormatV2Ea(instruction.Destination);
            }

            return instruction.Operation switch
            {
                M68kJitOperation.Moveq or
                M68kJitOperation.Mulu or
                M68kJitOperation.Muls or
                M68kJitOperation.Divu or
                M68kJitOperation.Divs or
                M68kJitOperation.ShiftRegister => "Dn",
                M68kJitOperation.Add or M68kJitOperation.Sub when instruction.Variant == 2 => "An",
                M68kJitOperation.Cmpa => "An",
                M68kJitOperation.OriToCcr or M68kJitOperation.AndiToCcr or M68kJitOperation.EoriToCcr or M68kJitOperation.MoveToCcr => "CCR",
                M68kJitOperation.OriToSr or M68kJitOperation.AndiToSr or M68kJitOperation.EoriToSr or M68kJitOperation.MoveToSr => "SR",
                _ => string.Empty
            };
        }

        private static string FormatV2Ea(M68kDecodedEa ea)
            => ea.Kind switch
            {
                M68kJitEaKind.DataRegister => "Dn",
                M68kJitEaKind.AddressRegister => "An",
                M68kJitEaKind.AddressIndirect => "(An)",
                M68kJitEaKind.AddressPostincrement => "(An)+",
                M68kJitEaKind.AddressPredecrement => "-(An)",
                M68kJitEaKind.AddressDisplacement => "d16(An)",
                M68kJitEaKind.AddressIndex => "d8(An,Xn)",
                M68kJitEaKind.AbsoluteWord => "abs.W",
                M68kJitEaKind.AbsoluteLong => "abs.L",
                M68kJitEaKind.PcDisplacement => "d16(PC)",
                M68kJitEaKind.PcIndex => "d8(PC,Xn)",
                M68kJitEaKind.Immediate => "#imm",
                _ => string.Empty
            };

        private static string FormatV2OperandSize(M68kDecodedInstruction instruction)
            => instruction.Operation switch
            {
                M68kJitOperation.Bra or
                M68kJitOperation.Bcc or
                M68kJitOperation.Bsr or
                M68kJitOperation.Dbcc or
                M68kJitOperation.Jmp or
                M68kJitOperation.Jsr or
                M68kJitOperation.Rts or
                M68kJitOperation.OriToCcr or
                M68kJitOperation.OriToSr or
                M68kJitOperation.AndiToCcr or
                M68kJitOperation.AndiToSr or
                M68kJitOperation.EoriToCcr or
                M68kJitOperation.EoriToSr or
                M68kJitOperation.MoveToCcr or
                M68kJitOperation.MoveToSr or
                M68kJitOperation.Pea => string.Empty,
                M68kJitOperation.ExtWord => ".W",
                M68kJitOperation.ExtLong => ".L",
                _ => instruction.Size switch
                {
                    M68kOperandSize.Byte => ".B",
                    M68kOperandSize.Word => ".W",
                    M68kOperandSize.Long => ".L",
                    _ => string.Empty
                }
            };

        private static string FormatV2OperationName(M68kJitOperation operation)
            => operation switch
            {
                M68kJitOperation.Moveq => "MOVEQ",
                M68kJitOperation.Movea => "MOVEA",
                M68kJitOperation.Cmpi => "CMPI",
                M68kJitOperation.Cmpa => "CMPA",
                M68kJitOperation.Cmpm => "CMPM",
                M68kJitOperation.Addi => "ADDI",
                M68kJitOperation.Addq => "ADDQ",
                M68kJitOperation.Subi => "SUBI",
                M68kJitOperation.Subq => "SUBQ",
                M68kJitOperation.Andi => "ANDI",
                M68kJitOperation.Ori => "ORI",
                M68kJitOperation.Eori => "EORI",
                M68kJitOperation.Bcc => "Bcc",
                M68kJitOperation.Dbcc => "DBcc",
                M68kJitOperation.Bsr => "BSR",
                M68kJitOperation.Jmp => "JMP",
                M68kJitOperation.Jsr => "JSR",
                M68kJitOperation.Rts => "RTS",
                M68kJitOperation.Movem => "MOVEM",
                M68kJitOperation.ExtWord or M68kJitOperation.ExtLong => "EXT",
                M68kJitOperation.Mulu => "MULU",
                M68kJitOperation.Muls => "MULS",
                M68kJitOperation.Divu => "DIVU",
                M68kJitOperation.Divs => "DIVS",
                M68kJitOperation.ShiftRegister => "SHIFT",
                M68kJitOperation.BitImmediate => "BITI",
                M68kJitOperation.BitDynamic => "BITD",
                M68kJitOperation.OriToCcr or M68kJitOperation.OriToSr => "ORI",
                M68kJitOperation.AndiToCcr or M68kJitOperation.AndiToSr => "ANDI",
                M68kJitOperation.EoriToCcr or M68kJitOperation.EoriToSr => "EORI",
                M68kJitOperation.MoveToCcr or M68kJitOperation.MoveToSr => "MOVE",
                M68kJitOperation.Pea => "PEA",
                _ => operation.ToString().ToUpperInvariant()
            };

        private void RecordV2LazyWriteback()
        {
            _counters.V2LazyWritebacks++;
        }

        private void RecordV2ZeroWaitTraceStats(
            int readRealFast,
            int readRom,
            int readOverlay,
            int writeRealFast,
            int slowReads,
            int slowWrites)
        {
            _counters.V2ZeroWaitReadRealFast += readRealFast;
            _counters.V2ZeroWaitReadRom += readRom;
            _counters.V2ZeroWaitReadOverlay += readOverlay;
            _counters.V2ZeroWaitWriteRealFast += writeRealFast;
            _counters.V2ZeroWaitReadSlow += slowReads;
            _counters.V2ZeroWaitWriteSlow += slowWrites;
        }

        private void RecordV2OutOfBlockSideExit(uint root, uint target, uint codeStart, int byteLength, bool isFallthrough)
        {
            _counters.V2SideExits++;
            _counters.V2SideExitOutOfBlockBranch++;
            _v2TraceBranchExits.TryGetValue(root, out var exits);
            exits++;
            _v2TraceBranchExits[root] = exits;
            RecordV2TierCeilingBranchExit(root, target, exits);
            if (isFallthrough)
            {
                _counters.V2SideExitConditionalFallthrough++;
            }

            if (_amigaBus != null && _amigaBus.IsChipRamAddress(target))
            {
                _counters.V2SideExitChipRam++;
                return;
            }

            if (_amigaBus != null && _amigaBus.HasHostTrapStub(target))
            {
                _counters.V2SideExitHostTrap++;
                return;
            }

            if (target < codeStart)
            {
                _counters.V2SideExitBeforeGraph++;
                return;
            }

            if (target >= Normalize(codeStart + (uint)byteLength))
            {
                _counters.V2SideExitBeyondGraph++;
                return;
            }

            if (TryFillV2GraphHoleTarget(target))
            {
                return;
            }

            _counters.V2SideExitGraphHole++;
            RecordV2GraphHoleCause(target);
            RecordV2UnsupportedGraphHoleExit(root, target);
        }

        private bool TryFillV2GraphHoleTarget(uint target)
        {
            if (!_v2Enabled ||
                _amigaBus == null ||
                _amigaBus.IsChipRamAddress(target) ||
                _amigaBus.HasHostTrapStub(target))
            {
                return false;
            }

            if (_traces.TryGetValue(target, out var existing))
            {
                return TryValidateTraceGeneration(target, ref existing) &&
                    IsV2GraphHoleTargetChainable(existing);
            }

            if (_asyncJitEnabled)
            {
                _v2UnsupportedGraphHoleExits.TryGetValue(target, out var holePressure);
                if (TryQueueAsyncTraceCompilation(
                    target,
                    V2TraceTier.Tier0,
                    M68kJitCompileReason.GraphHole,
                    CalculateAsyncCompilePriority(
                        target,
                        V2TraceTier.Tier0,
                        M68kJitCompileReason.GraphHole,
                        holePressure,
                        holePressure,
                        hasInternalLoop: false),
                    branchPressurePromotion: false) ||
                    !_asyncJitSyncFallbackEnabled)
                {
                    return false;
                }
            }

            if (!TryCompileTrace(target, out var trace))
            {
                return false;
            }

            AddTrace(trace);
            _counters.CompiledTraces++;
            _counters.V2GraphHoleTargetCompiles++;
            return IsV2GraphHoleTargetChainable(trace);
        }

        private bool IsV2GraphHoleTargetChainable(TraceEntry trace)
        {
            return trace.V2Compiled != null &&
                !_v2DisabledRoots.Contains(trace.Root) &&
                (trace.V2PureCpuBatchEligible || _v2BusAccessEnabled);
        }

        private void RecordV2TierCeilingBranchExit(uint root, uint target, int exits)
        {
            if (exits < V2TierCeilingBranchExitLimitExits ||
                _v2DisabledRoots.Contains(root) ||
                !_traces.TryGetValue(root, out var trace) ||
                trace.V2Compiled == null ||
                CanV2RootPromoteFurther(trace))
            {
                return;
            }

            var firstLimitedTarget = _v2BranchPressureLimitedTargets.Add(GetV2HandoffCacheKey(root, target));
            RecordV2BranchPressureLimitCause(target);
            RecordV2BranchPressureTargetState(
                TryQueueV2BranchPressureTarget(root, target, exits, firstLimitedTarget),
                target);
            if (_v2BranchPressureLimitedRoots.Add(root))
            {
                _counters.V2BranchPressureLimitedRoots++;
            }
        }

        private string TryQueueV2BranchPressureTarget(uint sourceRoot, uint target, int exits, bool firstLimitedTarget)
        {
            if (!_v2Enabled)
            {
                return "disabled";
            }

            if (_amigaBus == null)
            {
                return "no-bus";
            }

            if (_amigaBus.IsChipRamAddress(target))
            {
                return "chip";
            }

            if (_amigaBus.HasHostTrapStub(target))
            {
                return "host";
            }

            target = Normalize(target);
            if (_traces.TryGetValue(target, out var existing))
            {
                if (TryValidateTraceGeneration(target, ref existing) &&
                    IsV2GraphHoleTargetChainable(existing))
                {
                    _v2BranchPressureQueuedTargets.Remove(target);
                    CacheV2HandoffTrace(sourceRoot, target, existing);
                    return "linked";
                }

                return "existing-unusable";
            }

            if (_v2BranchPressureQueuedTargets.Contains(target))
            {
                return "pending";
            }

            if (!firstLimitedTarget && (exits & (exits - 1)) != 0)
            {
                return "throttled";
            }

            if (!_asyncJitEnabled)
            {
                return "async-disabled";
            }

            if (!TryQueueAsyncTraceCompilation(
                target,
                V2TraceTier.Tier0,
                M68kJitCompileReason.BranchPressure,
                CalculateAsyncCompilePriority(
                    target,
                    V2TraceTier.Tier0,
                    M68kJitCompileReason.BranchPressure,
                    exits,
                    exits,
                    hasInternalLoop: false),
                branchPressurePromotion: false,
                out var queueResult))
            {
                return "snapshot-failed";
            }

            if (queueResult is
                M68kAsyncJitQueueResult.Queued or
                M68kAsyncJitQueueResult.DedupedPending or
                M68kAsyncJitQueueResult.DedupedCompiling)
            {
                _v2BranchPressureQueuedTargets.Add(target);
            }

            return queueResult switch
            {
                M68kAsyncJitQueueResult.Queued => "queued",
                M68kAsyncJitQueueResult.DedupedPending => "deduped-pending",
                M68kAsyncJitQueueResult.DedupedCompiling => "deduped-compiling",
                M68kAsyncJitQueueResult.Dropped => "dropped",
                _ => "queue-unknown"
            };
        }

        private void RecordV2ZeroInstructionExit(uint root)
        {
            _v2ZeroInstructionExits.TryGetValue(root, out var exits);
            exits++;
            _v2ZeroInstructionExits[root] = exits;
            if (exits < V2ZeroInstructionExitDisableExits || _v2DisabledRoots.Contains(root))
            {
                return;
            }

            DisableV2Root(root, "entry");
            _counters.V2DisabledEntryMismatchRoots++;
        }

        private bool CanV2RootPromoteFurther(TraceEntry trace)
        {
            return CanUseV2PromotionTier(trace, V2TraceTier.Tier1) ||
                CanUseV2PromotionTier(trace, V2TraceTier.Tier2) ||
                CanUseV2PromotionTier(trace, V2TraceTier.Tier3);
        }

        private bool CanUseV2PromotionTier(TraceEntry trace, V2TraceTier tier)
        {
            return tier > trace.V2Tier &&
                (tier != V2TraceTier.Tier3 || _v2Tier3Enabled) &&
                !IsV2PromotionTierBlocked(trace.Root, tier);
        }

        private void RecordV2UnsupportedGraphHoleExit(uint root, uint target)
        {
            if (!IsUnsupportedV2GraphHole(target))
            {
                return;
            }

            _v2UnsupportedGraphHoleExits.TryGetValue(root, out var exits);
            exits++;
            _v2UnsupportedGraphHoleExits[root] = exits;
            if (exits < V2UnsupportedGraphHoleDisableExits || _v2DisabledRoots.Contains(root))
            {
                return;
            }

            DisableV2Root(root, "hole");
            _counters.V2DisabledGraphHoleRoots++;
        }

        private void DisableV2Root(uint root, string reason)
        {
            root = Normalize(root);
            _v2DisabledRoots.Add(root);
            _v2DisabledRootReasons[root] = reason;
        }

        private string GetV2DisabledRootBlockReason(uint root)
        {
            return _v2DisabledRootReasons.TryGetValue(Normalize(root), out var reason) &&
                !string.IsNullOrEmpty(reason)
                    ? "disabled-" + reason
                    : "disabled";
        }

        private bool IsUnsupportedV2GraphHole(uint target)
        {
            if (_amigaBus == null ||
                _amigaBus.IsChipRamAddress(target) ||
                _amigaBus.HasHostTrapStub(target) ||
                !M68kDecoder.TryDecode(_amigaBus, target, out var instruction, out _))
            {
                return false;
            }

            if (!IsV2Instruction(instruction, out _))
            {
                return true;
            }

            return !IsV2PureCpuInstruction(instruction);
        }

        private int MaterializeV2Flags(
            int status,
            int kind,
            uint destination,
            uint source,
            uint result,
            int sizeValue,
            bool setExtend)
        {
            if (kind == (int)V2PendingFlags.None)
            {
                return status;
            }

            _counters.V2FlagMaterializations++;
            var size = (M68kOperandSize)sizeValue;
            var mask = M68kCpuState.Mask(size);
            var sign = M68kCpuState.SignBit(size);
            destination &= mask;
            source &= mask;
            result &= mask;
            status &= unchecked((int)~(setExtend ? ArithmeticFlags : LogicFlags));
            if (result == 0)
            {
                status |= M68kCpuState.Zero;
            }

            if ((result & sign) != 0)
            {
                status |= M68kCpuState.Negative;
            }

            if (kind == (int)V2PendingFlags.Logic)
            {
                return status;
            }

            bool carry;
            bool overflow;
            if (kind == (int)V2PendingFlags.Add)
            {
                var full = (ulong)destination + source;
                carry = full > mask;
                overflow = (~(destination ^ source) & (destination ^ result) & sign) != 0;
            }
            else
            {
                carry = source > destination;
                overflow = ((destination ^ source) & (destination ^ result) & sign) != 0;
            }

            if (overflow)
            {
                status |= M68kCpuState.Overflow;
            }

            if (carry)
            {
                status |= M68kCpuState.Carry;
                if (setExtend)
                {
                    status |= M68kCpuState.Extend;
                }
            }

            return status;
        }

        private bool CheckV2ConditionFromPendingFlags(
            int status,
            int kind,
            uint destination,
            uint source,
            uint result,
            int sizeValue,
            int condition)
        {
            if (kind == (int)V2PendingFlags.None)
            {
                _counters.V2BranchStatusFlagChecks++;
                return CheckV2Condition(status, condition);
            }

            _counters.V2BranchPendingFlagChecks++;
            var size = (M68kOperandSize)sizeValue;
            var mask = M68kCpuState.Mask(size);
            var sign = M68kCpuState.SignBit(size);
            destination &= mask;
            source &= mask;
            result &= mask;
            var c = false;
            var v = false;
            var z = result == 0;
            var n = (result & sign) != 0;

            if (kind == (int)V2PendingFlags.Add)
            {
                var full = (ulong)destination + source;
                c = full > mask;
                v = (~(destination ^ source) & (destination ^ result) & sign) != 0;
            }
            else if (kind == (int)V2PendingFlags.Subtract)
            {
                c = source > destination;
                v = ((destination ^ source) & (destination ^ result) & sign) != 0;
            }

            return CheckV2ConditionBits(c, z, n, v, condition);
        }

        private static ulong DivideV2Register(uint dividend, uint divisor, int status, bool signed)
        {
            if (!signed)
            {
                var quotient = dividend / divisor;
                var remainder = dividend % divisor;
                if ((quotient & 0xFFFF_0000) != 0)
                {
                    return PackV2DivideResult(dividend, status | M68kCpuState.Overflow);
                }

                var result = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                status = SetV2DivideSuccessFlags(status, quotient);
                return PackV2DivideResult(result, status);
            }

            var signedDivisor = unchecked((short)divisor);
            var signedDividend = unchecked((int)dividend);
            var signedQuotient = signedDividend / signedDivisor;
            var signedRemainder = signedDividend % signedDivisor;
            if (signedQuotient < short.MinValue || signedQuotient > short.MaxValue)
            {
                return PackV2DivideResult(dividend, status | M68kCpuState.Overflow);
            }

            var signedQuotientBits = unchecked((uint)signedQuotient);
            var signedRemainderBits = unchecked((uint)signedRemainder);
            var packedResult = ((signedRemainderBits & 0xFFFF) << 16) | (signedQuotientBits & 0xFFFF);
            status = SetV2DivideSuccessFlags(status, signedQuotientBits);
            return PackV2DivideResult(packedResult, status);
        }

        private static int SetV2DivideSuccessFlags(int status, uint quotient)
        {
            status &= unchecked((int)~LogicFlags);
            quotient &= 0xFFFF;
            if (quotient == 0)
            {
                status |= M68kCpuState.Zero;
            }

            if ((quotient & 0x8000) != 0)
            {
                status |= M68kCpuState.Negative;
            }

            return status;
        }

        private static ulong PackV2DivideResult(uint value, int status)
            => ((ulong)(uint)status << 32) | value;

        private static ulong ShiftV2Register(uint value, int status, int count, int variant, int sizeValue)
        {
            var size = (M68kOperandSize)sizeValue;
            value &= M68kCpuState.Mask(size);
            if (count == 0)
            {
                status &= unchecked((int)~LogicFlags);
                if (value == 0)
                {
                    status |= M68kCpuState.Zero;
                }

                if ((value & M68kCpuState.SignBit(size)) != 0)
                {
                    status |= M68kCpuState.Negative;
                }

                return PackV2ShiftResult(value, status);
            }

            var type = variant & 3;
            var left = (variant & 4) != 0;
            var bits = size == M68kOperandSize.Long ? 32 : size == M68kOperandSize.Word ? 16 : 8;
            var mask = M68kCpuState.Mask(size);
            var carry = false;
            var extend = (status & M68kCpuState.Extend) != 0;
            for (var i = 0; i < count; i++)
            {
                if (left)
                {
                    carry = (value & (1u << (bits - 1))) != 0;
                    value = type switch
                    {
                        2 => ((value << 1) & mask) | (extend ? 1u : 0u),
                        3 => ((value << 1) & mask) | (carry ? 1u : 0u),
                        _ => (value << 1) & mask
                    };
                }
                else
                {
                    carry = (value & 1) != 0;
                    if (type == 0)
                    {
                        var sign = value & (1u << (bits - 1));
                        value = (value >> 1) | sign;
                    }
                    else if (type == 2)
                    {
                        value = (value >> 1) | (extend ? 1u << (bits - 1) : 0u);
                    }
                    else if (type == 3)
                    {
                        value = (value >> 1) | (carry ? 1u << (bits - 1) : 0u);
                    }
                    else
                    {
                        value >>= 1;
                    }
                }

                if (type == 2)
                {
                    extend = carry;
                }
            }

            status &= unchecked((int)~(type == 3 ? LogicFlags : ArithmeticFlags));
            value &= mask;
            if (value == 0)
            {
                status |= M68kCpuState.Zero;
            }

            if ((value & M68kCpuState.SignBit(size)) != 0)
            {
                status |= M68kCpuState.Negative;
            }

            if (carry)
            {
                status |= M68kCpuState.Carry;
                if (type != 3)
                {
                    status |= M68kCpuState.Extend;
                }
            }

            return PackV2ShiftResult(value, status);
        }

        private static ulong PackV2ShiftResult(uint value, int status)
            => ((ulong)(ushort)status << 32) | value;

        private static bool CheckV2Condition(int status, int condition)
        {
            var c = (status & M68kCpuState.Carry) != 0;
            var z = (status & M68kCpuState.Zero) != 0;
            var n = (status & M68kCpuState.Negative) != 0;
            var v = (status & M68kCpuState.Overflow) != 0;
            return CheckV2ConditionBits(c, z, n, v, condition);
        }

        private static bool CheckV2ConditionBits(bool c, bool z, bool n, bool v, int condition)
        {
            return condition switch
            {
                0x0 => true,
                0x1 => false,
                0x2 => !c && !z,
                0x3 => c || z,
                0x4 => !c,
                0x5 => c,
                0x6 => !z,
                0x7 => z,
                0x8 => !v,
                0x9 => v,
                0xA => !n,
                0xB => n,
                0xC => n == v,
                0xD => n != v,
                0xE => !z && n == v,
                0xF => z || n != v,
                _ => false
            };
        }

        private static void EmitLoadUIntConstant(ILGenerator il, uint value)
        {
            il.Emit(OpCodes.Ldc_I4, unchecked((int)value));
        }

        private static MethodInfo GetInstanceMethod(string name)
        {
            return typeof(M68kJitCore).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingMethodException(typeof(M68kJitCore).FullName, name);
        }

        private bool CanEnterCompiledInstruction(
            int maxInstructions,
            int executedInstructions,
            long targetCycle,
            IM68kInstructionBoundary boundary)
        {
            if (State.Halted ||
                State.Stopped ||
                executedInstructions >= maxInstructions ||
                State.Cycles >= targetCycle)
            {
                _counters.BoundarySideExits++;
                return false;
            }

            if (!boundary.BeforeInstruction())
            {
                _counters.BoundarySideExits++;
                return false;
            }

            return true;
        }

        private bool CanEnterPureCompiledInstruction(
            int maxInstructions,
            int executedInstructions,
            long targetCycle)
        {
            return !State.Halted &&
                !State.Stopped &&
                executedInstructions < maxInstructions &&
                State.Cycles < targetCycle;
        }

        private bool BeginCompiledInstruction(
            uint programCounter,
            ushort expectedOpcode,
            uint nextProgramCounter)
        {
            programCounter = Normalize(programCounter);
            _compiledInstructionPreviousCycle = State.Cycles;
            if (State.ProgramCounter != programCounter ||
                (_amigaBus != null && _amigaBus.HasHostTrapStub(programCounter)))
            {
                _counters.HostTrapBailouts++;
                return false;
            }

            State.LastOpcode = expectedOpcode;
            State.LastInstructionProgramCounter = programCounter;
            State.ProgramCounter = Normalize(nextProgramCounter);
            _instructionFrequency.Record(expectedOpcode);
            return true;
        }

        private void FinishCompiledInstruction(IM68kInstructionBoundary boundary)
        {
            boundary.AfterInstruction(_compiledInstructionPreviousCycle, State.Cycles);
        }

        private void InvalidateSource(uint programCounter)
        {
            State.ProgramCounter = Normalize(programCounter);
            State.Cycles = _compiledInstructionPreviousCycle;
            _counters.Invalidations++;
            _counters.SelfModifiedCodeExits++;
            RemoveTrace(Normalize(programCounter));
        }

        private void InvalidateWrittenCodeRange(uint address, int byteCount)
        {
            if (byteCount <= 0 || _traces.Count == 0)
            {
                return;
            }

            address = Normalize(address);
            HashSet<uint>? rootsToRemove = null;
            var remaining = byteCount;
            var current = address;
            while (remaining > 0)
            {
                if (_traceRootsByPage.TryGetValue(GetCodePageKey(current), out var roots))
                {
                    foreach (var root in roots)
                    {
                        if (!_traces.TryGetValue(root, out var trace) ||
                            !RangesOverlap(trace.CodeStart, trace.ByteLength, address, byteCount))
                        {
                            continue;
                        }

                        rootsToRemove ??= new HashSet<uint>();
                        rootsToRemove.Add(root);
                    }
                }

                var step = Math.Min(remaining, CodeGenerationPageSize - ((int)current & (CodeGenerationPageSize - 1)));
                remaining -= step;
                current = Normalize(current + (uint)step);
            }

            if (rootsToRemove != null)
            {
                foreach (var root in rootsToRemove)
                {
                    if (RemoveTrace(root))
                    {
                        _counters.Invalidations++;
                    }
                }
            }
        }

        private bool ExecuteCompiledDecodedOperation(
            int operationValue,
            int sizeValue,
            int sourceKindValue,
            int sourceRegister,
            uint sourceExtensionAddress,
            ushort sourceExtension0,
            ushort sourceExtension1,
            uint sourceImmediate,
            int destinationKindValue,
            int destinationRegister,
            uint destinationExtensionAddress,
            ushort destinationExtension0,
            ushort destinationExtension1,
            uint destinationImmediate,
            int register,
            int quickValue,
            int condition,
            int displacement,
            int variant,
            ushort registerMask,
            uint branchBase)
        {
            var operation = (M68kJitOperation)operationValue;
            var size = (M68kOperandSize)sizeValue;
            var source = new M68kDecodedEa(
                (M68kJitEaKind)sourceKindValue,
                sourceRegister,
                sourceExtensionAddress,
                sourceExtension0,
                sourceExtension1,
                sourceImmediate);
            var destination = new M68kDecodedEa(
                (M68kJitEaKind)destinationKindValue,
                destinationRegister,
                destinationExtensionAddress,
                destinationExtension0,
                destinationExtension1,
                destinationImmediate);

            switch (operation)
            {
                case M68kJitOperation.Nop:
                    AddCycles(4);
                    return true;
                case M68kJitOperation.Moveq:
                    State.D[register] = unchecked((uint)quickValue);
                    SetLogicFlags(State.D[register], M68kOperandSize.Long);
                    AddCycles(4);
                    return true;
                case M68kJitOperation.Move:
                    return ExecuteMove(source, destination, size);
                case M68kJitOperation.Movea:
                    return ExecuteMovea(source, destination.Register, size);
                case M68kJitOperation.Lea:
                    SetAddressRegister(register, ResolveEaAddress(source, size, applySideEffects: false));
                    AddCycles(8);
                    return true;
                case M68kJitOperation.Tst:
                    SetLogicFlags(ReadEaValue(source, size), size);
                    AddCycles(size == M68kOperandSize.Long ? 12 : 8);
                    return true;
                case M68kJitOperation.Clr:
                    WriteEaValue(destination, size, 0);
                    SetLogicFlags(0, size);
                    AddCycles(size == M68kOperandSize.Long ? 12 : 8);
                    return true;
                case M68kJitOperation.Neg:
                    ExecuteNeg(destination, size);
                    return true;
                case M68kJitOperation.Not:
                    ExecuteNot(destination, size);
                    return true;
                case M68kJitOperation.Cmp:
                    _ = Subtract(State.D[register], ReadEaValue(source, size), size, setExtend: false);
                    AddCycles(size == M68kOperandSize.Long ? 12 : 8);
                    return true;
                case M68kJitOperation.Cmpi:
                    _ = Subtract(ReadEaValue(destination, size), source.Immediate, size, setExtend: false);
                    AddCycles(size == M68kOperandSize.Long ? 16 : 8);
                    return true;
                case M68kJitOperation.Cmpa:
                    ExecuteAddressArithmetic(compareOnly: true, add: false, source, register, size);
                    return true;
                case M68kJitOperation.Cmpm:
                    ExecuteCmpm(source, destination, size);
                    return true;
                case M68kJitOperation.Add:
                    return variant == 2
                        ? ExecuteAddressArithmetic(compareOnly: false, add: true, source, register, size)
                        : ExecuteBinaryArithmetic(add: true, logical: false, source, destination, register, size, registerToEa: variant != 0);
                case M68kJitOperation.Addi:
                    return ExecuteImmediateArithmetic(add: true, logicalOperation: 0, source.Immediate, destination, size);
                case M68kJitOperation.Addq:
                    return ExecuteQuickArithmetic(add: true, quickValue, destination, size);
                case M68kJitOperation.Sub:
                    return variant == 2
                        ? ExecuteAddressArithmetic(compareOnly: false, add: false, source, register, size)
                        : ExecuteBinaryArithmetic(add: false, logical: false, source, destination, register, size, registerToEa: variant != 0);
                case M68kJitOperation.Subi:
                    return ExecuteImmediateArithmetic(add: false, logicalOperation: 0, source.Immediate, destination, size);
                case M68kJitOperation.Subq:
                    return ExecuteQuickArithmetic(add: false, quickValue, destination, size);
                case M68kJitOperation.And:
                    return ExecuteBinaryLogical(0, source, destination, register, size, registerToEa: variant != 0);
                case M68kJitOperation.Andi:
                    return ExecuteImmediateArithmetic(add: false, logicalOperation: 1, source.Immediate, destination, size);
                case M68kJitOperation.Or:
                    return ExecuteBinaryLogical(1, source, destination, register, size, registerToEa: variant != 0);
                case M68kJitOperation.Ori:
                    return ExecuteImmediateArithmetic(add: false, logicalOperation: 2, source.Immediate, destination, size);
                case M68kJitOperation.Eor:
                    return ExecuteBinaryLogical(2, source, destination, register, size, registerToEa: true);
                case M68kJitOperation.Eori:
                    return ExecuteImmediateArithmetic(add: false, logicalOperation: 3, source.Immediate, destination, size);
                case M68kJitOperation.Bra:
                    State.ProgramCounter = Normalize(branchBase + unchecked((uint)displacement));
                    AddCycles(10);
                    return true;
                case M68kJitOperation.Bcc:
                    if (CheckCondition(condition))
                    {
                        State.ProgramCounter = Normalize(branchBase + unchecked((uint)displacement));
                        AddCycles(10);
                    }
                    else
                    {
                        AddCycles(8);
                    }

                    return true;
                case M68kJitOperation.Bsr:
                    PushLong(State.ProgramCounter);
                    State.ProgramCounter = Normalize(branchBase + unchecked((uint)displacement));
                    AddCycles(18);
                    return true;
                case M68kJitOperation.Dbcc:
                    return ExecuteDbcc(register, condition, branchBase, displacement);
                case M68kJitOperation.Jmp:
                    return ExecuteJump(source, link: false);
                case M68kJitOperation.Jsr:
                    return ExecuteJump(source, link: true);
                case M68kJitOperation.Rts:
                    State.ProgramCounter = PullLong();
                    AddCycles(16);
                    return true;
                case M68kJitOperation.Movem:
                    ExecuteMovem(source, size, registerMask, memoryToRegisters: variant != 0);
                    return true;
                case M68kJitOperation.ExtWord:
                    ExecuteExt(register, M68kOperandSize.Word);
                    return true;
                case M68kJitOperation.ExtLong:
                    ExecuteExt(register, M68kOperandSize.Long);
                    return true;
                case M68kJitOperation.Swap:
                    ExecuteSwap(register);
                    return true;
                case M68kJitOperation.Exg:
                    ExecuteExchange(register, quickValue, variant);
                    return true;
                case M68kJitOperation.Mulu:
                    ExecuteMultiply(source, register, signed: false);
                    return true;
                case M68kJitOperation.Muls:
                    ExecuteMultiply(source, register, signed: true);
                    return true;
                case M68kJitOperation.Divu:
                    ExecuteDivide(source, register, signed: false);
                    return true;
                case M68kJitOperation.Divs:
                    ExecuteDivide(source, register, signed: true);
                    return true;
                case M68kJitOperation.ShiftRegister:
                    ExecuteRegisterShift(register, quickValue, variant, size);
                    return true;
                case M68kJitOperation.BitImmediate:
                    ExecuteBitOperation(destination, quickValue, variant, size, immediateBit: true);
                    return true;
                case M68kJitOperation.BitDynamic:
                    ExecuteBitOperation(destination, (int)State.D[register], variant, size, immediateBit: false);
                    return true;
                case M68kJitOperation.OriToCcr:
                case M68kJitOperation.OriToSr:
                case M68kJitOperation.AndiToCcr:
                case M68kJitOperation.AndiToSr:
                case M68kJitOperation.EoriToCcr:
                case M68kJitOperation.EoriToSr:
                case M68kJitOperation.MoveToCcr:
                case M68kJitOperation.MoveToSr:
                    ExecuteCompiledStatusImmediate(operationValue, source.Immediate);
                    return true;
                case M68kJitOperation.Pea:
                    ExecuteCompiledPea(
                        (int)source.Kind,
                        source.Register,
                        source.ExtensionAddress,
                        source.Extension0,
                        source.Extension1);
                    return true;
                default:
                    State.ProgramCounter = State.LastInstructionProgramCounter;
                    State.Cycles = _compiledInstructionPreviousCycle;
                    _counters.UnsupportedOpcode++;
                    return false;
            }
        }

        private void ExecuteCompiledStatusImmediate(int operationValue, uint immediate)
        {
            var operation = (M68kJitOperation)operationValue;
            if (operation == M68kJitOperation.MoveToCcr)
            {
                SetCcr((ushort)immediate);
                AddCycles(12);
                return;
            }

            if (operation == M68kJitOperation.MoveToSr)
            {
                State.StatusRegister = (ushort)immediate;
                AddCycles(12);
                return;
            }

            var status = State.StatusRegister;
            var result = operation switch
            {
                M68kJitOperation.OriToCcr or M68kJitOperation.OriToSr => status | (ushort)immediate,
                M68kJitOperation.AndiToCcr or M68kJitOperation.AndiToSr => status & (ushort)immediate,
                M68kJitOperation.EoriToCcr or M68kJitOperation.EoriToSr => status ^ (ushort)immediate,
                _ => status
            };

            if (operation is M68kJitOperation.OriToCcr or M68kJitOperation.AndiToCcr or M68kJitOperation.EoriToCcr)
            {
                SetCcr((ushort)result);
                AddCycles(8);
                return;
            }

            if (!State.GetFlag(M68kCpuState.Supervisor))
            {
                RaiseException(8, State.LastInstructionProgramCounter, 34);
                return;
            }

            State.StatusRegister = (ushort)result;
            AddCycles(20);
        }

        private void ExecuteCompiledPea(
            int sourceKindValue,
            int sourceRegister,
            uint sourceExtensionAddress,
            ushort sourceExtension0,
            ushort sourceExtension1)
        {
            var source = new M68kDecodedEa(
                (M68kJitEaKind)sourceKindValue,
                sourceRegister,
                sourceExtensionAddress,
                sourceExtension0,
                sourceExtension1,
                0);
            PushLong(ResolveEaAddress(source, M68kOperandSize.Long, applySideEffects: false));
            AddCycles(12);
        }

        private bool ExecuteCompiledMovem(
            int eaKind,
            int eaRegister,
            uint eaExtensionAddress,
            ushort eaExtension0,
            ushort eaExtension1,
            int sizeValue,
            ushort registerMask,
            int variant)
        {
            var ea = new M68kDecodedEa(
                (M68kJitEaKind)eaKind,
                eaRegister,
                eaExtensionAddress,
                eaExtension0,
                eaExtension1,
                0);
            ExecuteMovem(ea, (M68kOperandSize)sizeValue, registerMask, memoryToRegisters: variant != 0);
            return true;
        }

        private bool ExecuteCompiledMovemForV2Batch(
            int eaKind,
            int eaRegister,
            uint eaExtensionAddress,
            ushort eaExtension0,
            ushort eaExtension1,
            int sizeValue,
            ushort registerMask,
            int variant)
        {
            var ea = new M68kDecodedEa(
                (M68kJitEaKind)eaKind,
                eaRegister,
                eaExtensionAddress,
                eaExtension0,
                eaExtension1,
                0);
            ExecuteMovemForV2Batch(ea, (M68kOperandSize)sizeValue, registerMask, memoryToRegisters: variant != 0);
            return true;
        }

        private bool ExecuteCompiledJumpTo(uint target, bool link)
        {
            if (_amigaBus != null && _amigaBus.HasHostTrapStub(target))
            {
                State.ProgramCounter = State.LastInstructionProgramCounter;
                State.Cycles = _compiledInstructionPreviousCycle;
                _counters.HostTrapBailouts++;
                return false;
            }

            if (link)
            {
                PushLong(State.ProgramCounter);
                State.ProgramCounter = Normalize(target);
                AddCycles(18);
            }
            else
            {
                State.ProgramCounter = Normalize(target);
                AddCycles(12);
            }

            return true;
        }

        private bool ExecuteV2JumpTo(uint target, bool link, int cycles)
        {
            if (_amigaBus != null && _amigaBus.HasHostTrapStub(target))
            {
                State.ProgramCounter = State.LastInstructionProgramCounter;
                _counters.HostTrapBailouts++;
                _counters.V2SideExitHostTrap++;
                return false;
            }

            if (link)
            {
                PushLong(State.ProgramCounter);
            }

            State.ProgramCounter = Normalize(target);
            AddCycles(cycles);
            return true;
        }

        private void ExecuteV2DivideByZero()
        {
            RaiseException(5, State.ProgramCounter, 38);
        }

        private bool ExecuteCompiledRegisterShift(int register, int quickValue, int variant, int sizeValue)
        {
            ExecuteRegisterShift(register, quickValue, variant, (M68kOperandSize)sizeValue);
            return true;
        }

        private bool ExecuteCompiledMultiplyDivideValue(uint sourceValue, int register, bool signed, bool divide)
        {
            if (!divide)
            {
                if (signed)
                {
                    State.D[register] = unchecked((uint)(unchecked((short)State.D[register]) * unchecked((short)sourceValue)));
                }
                else
                {
                    State.D[register] = (uint)((ushort)State.D[register] * (ushort)sourceValue);
                }

                SetLogicFlags(State.D[register], M68kOperandSize.Long);
                AddCycles(70);
                return true;
            }

            var divisor = sourceValue & 0xFFFF;
            if (divisor == 0)
            {
                RaiseException(5, State.ProgramCounter, 38);
                return true;
            }

            var dividend = State.D[register];
            if (!signed)
            {
                var quotient = dividend / divisor;
                var remainder = dividend % divisor;
                if ((quotient & 0xFFFF_0000) != 0)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                }
                else
                {
                    State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                    State.SetNegativeZero(quotient, M68kOperandSize.Word);
                    State.SetFlag(M68kCpuState.Overflow, false);
                    State.SetFlag(M68kCpuState.Carry, false);
                }

                AddCycles(140);
                return true;
            }

            var signedDivisor = unchecked((short)divisor);
            var signedDividend = unchecked((int)dividend);
            var signedQuotient = signedDividend / signedDivisor;
            var signedRemainder = signedDividend % signedDivisor;
            if (signedQuotient < short.MinValue || signedQuotient > short.MaxValue)
            {
                State.SetFlag(M68kCpuState.Overflow, true);
            }
            else
            {
                var quotient = unchecked((uint)signedQuotient);
                var remainder = unchecked((uint)signedRemainder);
                State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                State.SetNegativeZero(quotient, M68kOperandSize.Word);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddCycles(140);
            return true;
        }

        private bool ExecuteMove(M68kDecodedEa source, M68kDecodedEa destination, M68kOperandSize size)
        {
            var value = ReadEaValue(source, size);
            WriteEaValue(destination, size, value);
            SetLogicFlags(value, size);
            AddCycles(size == M68kOperandSize.Long ? 12 : 8);
            return true;
        }

        private bool ExecuteMovea(M68kDecodedEa source, int register, M68kOperandSize size)
        {
            var value = ReadEaValue(source, size);
            if (size == M68kOperandSize.Word)
            {
                value = M68kCpuState.SignExtend(value, M68kOperandSize.Word);
            }

            SetAddressRegister(register, value);
            AddCycles(size == M68kOperandSize.Long ? 12 : 8);
            return true;
        }

        private bool ExecuteAddressArithmetic(bool compareOnly, bool add, M68kDecodedEa source, int register, M68kOperandSize size)
        {
            var value = ReadEaValue(source, size);
            var extended = size == M68kOperandSize.Word ? M68kCpuState.SignExtend(value, M68kOperandSize.Word) : value;
            if (compareOnly)
            {
                _ = Subtract(State.A[register], extended, M68kOperandSize.Long, setExtend: false);
            }
            else if (add)
            {
                SetAddressRegister(register, State.A[register] + extended);
            }
            else
            {
                SetAddressRegister(register, State.A[register] - extended);
            }

            AddCycles(size == M68kOperandSize.Long ? 8 : 6);
            return true;
        }

        private void ExecuteCmpm(M68kDecodedEa source, M68kDecodedEa destination, M68kOperandSize size)
        {
            var sourceValue = ReadEaValue(source, size);
            var destinationValue = ReadEaValue(destination, size);
            _ = Subtract(destinationValue, sourceValue, size, setExtend: false);
            AddCycles(size == M68kOperandSize.Long ? 20 : 12);
        }

        private void ExecuteNeg(M68kDecodedEa destination, M68kOperandSize size)
        {
            var value = ReadEaForModify(destination, size, out var resolvedAddress, out var memory);
            var result = Subtract(0, value, size, setExtend: true);
            WriteResolvedEa(destination, size, result, resolvedAddress, memory);
            AddCycles(size == M68kOperandSize.Long ? 12 : 8);
        }

        private void ExecuteNot(M68kDecodedEa destination, M68kOperandSize size)
        {
            var value = ReadEaForModify(destination, size, out var resolvedAddress, out var memory);
            var result = (~value) & M68kCpuState.Mask(size);
            WriteResolvedEa(destination, size, result, resolvedAddress, memory);
            SetLogicFlags(result, size);
            AddCycles(size == M68kOperandSize.Long ? 12 : 8);
        }

        private bool ExecuteBinaryArithmetic(
            bool add,
            bool logical,
            M68kDecodedEa source,
            M68kDecodedEa destination,
            int register,
            M68kOperandSize size,
            bool registerToEa)
        {
            _ = logical;
            var regValue = State.D[register] & M68kCpuState.Mask(size);
            if (registerToEa)
            {
                var destinationValue = ReadEaForModify(destination, size, out var resolvedAddress, out var memory);
                var result = add
                    ? Add(destinationValue, regValue, size, setExtend: true)
                    : Subtract(destinationValue, regValue, size, setExtend: true);
                WriteResolvedEa(destination, size, result, resolvedAddress, memory);
            }
            else
            {
                var sourceValue = ReadEaValue(source, size);
                var result = add
                    ? Add(regValue, sourceValue, size, setExtend: true)
                    : Subtract(regValue, sourceValue, size, setExtend: true);
                WriteDataRegister(register, result, size);
            }

            AddCycles(size == M68kOperandSize.Long ? 12 : 8);
            return true;
        }

        private void ExecuteMultiply(M68kDecodedEa source, int register, bool signed)
        {
            if (signed)
            {
                var sourceValue = unchecked((short)ReadEaValue(source, M68kOperandSize.Word));
                State.D[register] = unchecked((uint)(unchecked((short)State.D[register]) * sourceValue));
            }
            else
            {
                var sourceValue = ReadEaValue(source, M68kOperandSize.Word) & 0xFFFF;
                State.D[register] = (uint)((ushort)State.D[register] * (ushort)sourceValue);
            }

            SetLogicFlags(State.D[register], M68kOperandSize.Long);
            AddCycles(70);
        }

        private void ExecuteDivide(M68kDecodedEa source, int register, bool signed)
        {
            var divisor = ReadEaValue(source, M68kOperandSize.Word) & 0xFFFF;
            if (divisor == 0)
            {
                RaiseException(5, State.ProgramCounter, 38);
                return;
            }

            var dividend = State.D[register];
            if (!signed)
            {
                var quotient = dividend / divisor;
                var remainder = dividend % divisor;
                if ((quotient & 0xFFFF_0000) != 0)
                {
                    State.SetFlag(M68kCpuState.Overflow, true);
                }
                else
                {
                    State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                    State.SetNegativeZero(quotient, M68kOperandSize.Word);
                    State.SetFlag(M68kCpuState.Overflow, false);
                    State.SetFlag(M68kCpuState.Carry, false);
                }

                AddCycles(140);
                return;
            }

            var signedDivisor = unchecked((short)divisor);
            var signedDividend = unchecked((int)dividend);
            var signedQuotient = signedDividend / signedDivisor;
            var signedRemainder = signedDividend % signedDivisor;
            if (signedQuotient < short.MinValue || signedQuotient > short.MaxValue)
            {
                State.SetFlag(M68kCpuState.Overflow, true);
            }
            else
            {
                var quotient = unchecked((uint)signedQuotient);
                var remainder = unchecked((uint)signedRemainder);
                State.D[register] = ((remainder & 0xFFFF) << 16) | (quotient & 0xFFFF);
                State.SetNegativeZero(quotient, M68kOperandSize.Word);
                State.SetFlag(M68kCpuState.Overflow, false);
                State.SetFlag(M68kCpuState.Carry, false);
            }

            AddCycles(140);
        }

        private bool ExecuteBinaryLogical(
            int operation,
            M68kDecodedEa source,
            M68kDecodedEa destination,
            int register,
            M68kOperandSize size,
            bool registerToEa)
        {
            var regValue = State.D[register] & M68kCpuState.Mask(size);
            if (registerToEa)
            {
                var destinationValue = ReadEaForModify(destination, size, out var resolvedAddress, out var memory);
                var result = operation switch
                {
                    0 => destinationValue & regValue,
                    1 => destinationValue | regValue,
                    _ => destinationValue ^ regValue
                };
                WriteResolvedEa(destination, size, result, resolvedAddress, memory);
                SetLogicFlags(result, size);
            }
            else
            {
                var sourceValue = ReadEaValue(source, size);
                var result = operation == 0 ? regValue & sourceValue : regValue | sourceValue;
                WriteDataRegister(register, result, size);
                SetLogicFlags(result, size);
            }

            AddCycles(size == M68kOperandSize.Long ? 12 : 8);
            return true;
        }

        private bool ExecuteImmediateArithmetic(
            bool add,
            int logicalOperation,
            uint immediate,
            M68kDecodedEa destination,
            M68kOperandSize size)
        {
            var destinationValue = ReadEaForModify(destination, size, out var resolvedAddress, out var memory);
            uint result;
            if (logicalOperation != 0)
            {
                result = logicalOperation switch
                {
                    1 => destinationValue & immediate,
                    2 => destinationValue | immediate,
                    _ => destinationValue ^ immediate
                };
                WriteResolvedEa(destination, size, result, resolvedAddress, memory);
                SetLogicFlags(result, size);
            }
            else
            {
                result = add
                    ? Add(destinationValue, immediate, size, setExtend: true)
                    : Subtract(destinationValue, immediate, size, setExtend: true);
                WriteResolvedEa(destination, size, result, resolvedAddress, memory);
            }

            AddCycles(size == M68kOperandSize.Long ? 16 : 8);
            return true;
        }

        private bool ExecuteQuickArithmetic(bool add, int value, M68kDecodedEa destination, M68kOperandSize size)
        {
            if (destination.Kind == M68kJitEaKind.AddressRegister)
            {
                var delta = M68kCpuState.SignExtend((uint)value, size);
                SetAddressRegister(destination.Register, add ? State.A[destination.Register] + delta : State.A[destination.Register] - delta);
                AddCycles(size == M68kOperandSize.Long ? 8 : 6);
                return true;
            }

            var destinationValue = ReadEaForModify(destination, size, out var resolvedAddress, out var memory);
            var result = add
                ? Add(destinationValue, (uint)value, size, setExtend: true)
                : Subtract(destinationValue, (uint)value, size, setExtend: true);
            WriteResolvedEa(destination, size, result, resolvedAddress, memory);
            AddCycles(size == M68kOperandSize.Long ? 8 : 4);
            return true;
        }

        private bool ExecuteDbcc(int register, int condition, uint branchBase, int displacement)
        {
            if (!CheckCondition(condition))
            {
                var counter = (ushort)((State.D[register] & 0xFFFF) - 1);
                State.D[register] = (State.D[register] & 0xFFFF_0000) | counter;
                if (counter != 0xFFFF)
                {
                    State.ProgramCounter = Normalize(branchBase + unchecked((uint)displacement));
                    AddCycles(10);
                }
                else
                {
                    AddCycles(14);
                }
            }
            else
            {
                AddCycles(12);
            }

            return true;
        }

        private bool ExecuteJump(M68kDecodedEa source, bool link)
        {
            var target = ResolveEaAddress(source, M68kOperandSize.Long, applySideEffects: false);
            if (_amigaBus != null && _amigaBus.HasHostTrapStub(target))
            {
                State.ProgramCounter = State.LastInstructionProgramCounter;
                State.Cycles = _compiledInstructionPreviousCycle;
                _counters.HostTrapBailouts++;
                return false;
            }

            if (link)
            {
                PushLong(State.ProgramCounter);
                State.ProgramCounter = Normalize(target);
                AddCycles(source.Kind == M68kJitEaKind.AddressIndirect ? 16 : 18);
            }
            else
            {
                State.ProgramCounter = Normalize(target);
                AddCycles(12);
            }

            return true;
        }

        private void ExecuteMovem(M68kDecodedEa ea, M68kOperandSize size, ushort registerMask, bool memoryToRegisters)
        {
            var mode = ea.Kind;
            if (!memoryToRegisters && mode == M68kJitEaKind.AddressPredecrement)
            {
                var address = State.A[ea.Register];
                for (var bit = 0; bit < 16; bit++)
                {
                    if ((registerMask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    var register = 15 - bit;
                    address -= (uint)size;
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    WriteMemoryValue(address, value, size);
                }

                SetAddressRegister(ea.Register, address);
                AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
                return;
            }

            var current = ResolveEaAddress(ea, size, applySideEffects: false);
            for (var register = 0; register < 16; register++)
            {
                if ((registerMask & (1 << register)) == 0)
                {
                    continue;
                }

                if (memoryToRegisters)
                {
                    var value = size == M68kOperandSize.Word
                        ? M68kCpuState.SignExtend(ReadMemoryValue(current, size), M68kOperandSize.Word)
                        : ReadMemoryValue(current, size);
                    if (register < 8)
                    {
                        State.D[register] = value;
                    }
                    else
                    {
                        SetAddressRegister(register - 8, value);
                    }
                }
                else
                {
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    WriteMemoryValue(current, value, size);
                }

                current += (uint)size;
            }

            if (memoryToRegisters && ea.Kind == M68kJitEaKind.AddressPostincrement)
            {
                SetAddressRegister(ea.Register, current);
            }

            AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
        }

        private void ExecuteMovemForV2Batch(M68kDecodedEa ea, M68kOperandSize size, ushort registerMask, bool memoryToRegisters)
        {
            var mode = ea.Kind;
            if (!memoryToRegisters && mode == M68kJitEaKind.AddressPredecrement)
            {
                var address = State.A[ea.Register];
                for (var bit = 0; bit < 16; bit++)
                {
                    if ((registerMask & (1 << bit)) == 0)
                    {
                        continue;
                    }

                    var register = 15 - bit;
                    address -= (uint)size;
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    WriteMemoryValueForV2Batch(address, value, size);
                }

                SetAddressRegister(ea.Register, address);
                AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
                return;
            }

            var current = ResolveEaAddress(ea, size, applySideEffects: false);
            for (var register = 0; register < 16; register++)
            {
                if ((registerMask & (1 << register)) == 0)
                {
                    continue;
                }

                if (memoryToRegisters)
                {
                    var value = size == M68kOperandSize.Word
                        ? M68kCpuState.SignExtend(ReadMemoryValueForV2Batch(current, size), M68kOperandSize.Word)
                        : ReadMemoryValueForV2Batch(current, size);
                    if (register < 8)
                    {
                        State.D[register] = value;
                    }
                    else
                    {
                        SetAddressRegister(register - 8, value);
                    }
                }
                else
                {
                    var value = register < 8 ? State.D[register] : State.A[register - 8];
                    WriteMemoryValueForV2Batch(current, value, size);
                }

                current += (uint)size;
            }

            if (memoryToRegisters && ea.Kind == M68kJitEaKind.AddressPostincrement)
            {
                SetAddressRegister(ea.Register, current);
            }

            AddCycles(8 + CountBits(registerMask) * (size == M68kOperandSize.Long ? 8 : 4));
        }

        private void ExecuteExt(int register, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Word)
            {
                var value = M68kCpuState.SignExtend(State.D[register] & 0xFF, M68kOperandSize.Byte) & 0xFFFF;
                State.D[register] = (State.D[register] & 0xFFFF_0000) | value;
                SetLogicFlags(value, M68kOperandSize.Word);
            }
            else
            {
                var value = M68kCpuState.SignExtend(State.D[register] & 0xFFFF, M68kOperandSize.Word);
                State.D[register] = value;
                SetLogicFlags(value, M68kOperandSize.Long);
            }

            AddCycles(4);
        }

        private void ExecuteSwap(int register)
        {
            var value = State.D[register];
            State.D[register] = (value << 16) | ((value >> 16) & 0xFFFF);
            SetLogicFlags(State.D[register], M68kOperandSize.Long);
            AddCycles(4);
        }

        private void ExecuteExchange(int left, int right, int variant)
        {
            switch (variant)
            {
                case 0:
                    (State.D[left], State.D[right]) = (State.D[right], State.D[left]);
                    break;
                case 1:
                {
                    var value = State.A[left];
                    SetAddressRegister(left, State.A[right]);
                    SetAddressRegister(right, value);
                    break;
                }
                default:
                {
                    var value = State.D[left];
                    State.D[left] = State.A[right];
                    SetAddressRegister(right, value);
                    break;
                }
            }

            AddCycles(6);
        }

        private void ExecuteRegisterShift(int register, int quickValue, int variant, M68kOperandSize size)
        {
            var count = (variant & 8) != 0 ? (int)(State.D[quickValue] & 63) : quickValue;
            var type = variant & 3;
            var left = (variant & 4) != 0;
            var value = State.D[register] & M68kCpuState.Mask(size);
            var shifted = Shift(value, count, size, type, left);
            WriteDataRegister(register, shifted, size);
            AddCycles(6 + (count * 2));
        }

        private void ExecuteBitOperation(M68kDecodedEa ea, int bitValue, int operation, M68kOperandSize size, bool immediateBit)
        {
            uint resolvedAddress = 0;
            var memory = false;
            var value = operation == 0
                ? ReadEaValue(ea, size)
                : ReadEaForModify(ea, size, out resolvedAddress, out memory);
            var bit = ea.Kind == M68kJitEaKind.DataRegister ? bitValue & 31 : bitValue & 7;
            var mask = 1u << bit;
            State.SetFlag(M68kCpuState.Zero, (value & mask) == 0);
            if (operation == 0)
            {
                AddCycles(ea.Kind == M68kJitEaKind.DataRegister && immediateBit ? 10 : ea.Kind == M68kJitEaKind.DataRegister ? 8 : 14);
                return;
            }

            value = operation switch
            {
                1 => value ^ mask,
                2 => value & ~mask,
                _ => value | mask
            };
            WriteResolvedEa(ea, size, value, resolvedAddress, memory);
            AddCycles(ea.Kind == M68kJitEaKind.DataRegister && immediateBit ? 10 : ea.Kind == M68kJitEaKind.DataRegister ? 8 : 14);
        }

        private uint ReadEaValue(M68kDecodedEa ea, M68kOperandSize size)
        {
            return ea.Kind switch
            {
                M68kJitEaKind.DataRegister => State.D[ea.Register] & M68kCpuState.Mask(size),
                M68kJitEaKind.AddressRegister => size == M68kOperandSize.Word ? State.A[ea.Register] & 0xFFFF : State.A[ea.Register],
                M68kJitEaKind.Immediate => ea.Immediate & M68kCpuState.Mask(size),
                _ => ReadMemoryValue(ResolveEaAddress(ea, size, applySideEffects: true), size)
            };
        }

        private uint ReadEaForModify(M68kDecodedEa ea, M68kOperandSize size, out uint resolvedAddress, out bool memory)
        {
            if (ea.Kind == M68kJitEaKind.DataRegister)
            {
                resolvedAddress = 0;
                memory = false;
                return State.D[ea.Register] & M68kCpuState.Mask(size);
            }

            if (ea.Kind == M68kJitEaKind.AddressRegister)
            {
                resolvedAddress = 0;
                memory = false;
                return size == M68kOperandSize.Word ? State.A[ea.Register] & 0xFFFF : State.A[ea.Register];
            }

            resolvedAddress = ResolveEaAddress(ea, size, applySideEffects: true);
            memory = true;
            return ReadMemoryValue(resolvedAddress, size);
        }

        private void WriteResolvedEa(M68kDecodedEa ea, M68kOperandSize size, uint value, uint resolvedAddress, bool memory)
        {
            if (memory)
            {
                WriteMemoryValue(resolvedAddress, value, size);
                return;
            }

            WriteEaValue(ea, size, value);
        }

        private void WriteEaValue(M68kDecodedEa ea, M68kOperandSize size, uint value)
        {
            value &= M68kCpuState.Mask(size);
            switch (ea.Kind)
            {
                case M68kJitEaKind.DataRegister:
                    WriteDataRegister(ea.Register, value, size);
                    return;
                case M68kJitEaKind.AddressRegister:
                    SetAddressRegister(
                        ea.Register,
                        size == M68kOperandSize.Word ? M68kCpuState.SignExtend(value, M68kOperandSize.Word) : value);
                    return;
                default:
                    WriteMemoryValue(ResolveEaAddress(ea, size, applySideEffects: true), value, size);
                    return;
            }
        }

        private uint ResolveEaAddress(M68kDecodedEa ea, M68kOperandSize size, bool applySideEffects)
        {
            return ea.Kind switch
            {
                M68kJitEaKind.AddressIndirect => State.A[ea.Register],
                M68kJitEaKind.AddressPostincrement => ResolvePostincrement(ea.Register, size, applySideEffects),
                M68kJitEaKind.AddressPredecrement => ResolvePredecrement(ea.Register, size, applySideEffects),
                M68kJitEaKind.AddressDisplacement => unchecked((uint)(State.A[ea.Register] + (short)ea.Extension0)),
                M68kJitEaKind.AddressIndex => unchecked((uint)(State.A[ea.Register] + ResolveIndex(ea.Extension0) + (sbyte)(ea.Extension0 & 0xFF))),
                M68kJitEaKind.AbsoluteWord => unchecked((uint)(short)ea.Extension0),
                M68kJitEaKind.AbsoluteLong => ((uint)ea.Extension0 << 16) | ea.Extension1,
                M68kJitEaKind.PcDisplacement => unchecked((uint)(ea.ExtensionAddress + (short)ea.Extension0)),
                M68kJitEaKind.PcIndex => unchecked((uint)(ea.ExtensionAddress + ResolveIndex(ea.Extension0) + (sbyte)(ea.Extension0 & 0xFF))),
                _ => throw new AmigaEmulationException("MC68000 JIT effective address is not memory-addressable.")
            };
        }

        private uint ResolvePostincrement(int register, M68kOperandSize size, bool applySideEffects)
        {
            var address = State.A[register];
            if (applySideEffects)
            {
                SetAddressRegister(register, address + AddressIncrement(register, size));
            }

            return address;
        }

        private uint ResolvePredecrement(int register, M68kOperandSize size, bool applySideEffects)
        {
            if (applySideEffects)
            {
                SetAddressRegister(register, State.A[register] - AddressIncrement(register, size));
            }

            return State.A[register];
        }

        private uint ResolveIndex(ushort extension)
        {
            var indexRegister = (extension >> 12) & 7;
            var value = (extension & 0x8000) != 0 ? State.A[indexRegister] : State.D[indexRegister];
            return (extension & 0x0800) == 0
                ? M68kCpuState.SignExtend(value, M68kOperandSize.Word)
                : value;
        }

        private uint ReadMemoryValue(uint address, M68kOperandSize size)
        {
            return size switch
            {
                M68kOperandSize.Byte => ReadByte(address),
                M68kOperandSize.Word => ReadWord(address),
                _ => ReadLong(address)
            };
        }

        private uint ReadMemoryValueForV2Batch(uint address, M68kOperandSize size)
        {
            if (_amigaBus != null && _amigaBus.TryReadJitZeroWaitMemory(address, size, out var value))
            {
                return value;
            }

            return ReadMemoryValueForV2BatchSlow(address, size);
        }

        private uint ReadMemoryValueForV2BatchSlow(uint address, M68kOperandSize size)
        {
            return ReadMemoryValue(address, size);
        }

        private uint ReadMemoryValueForV2BatchSlowRef(ref long cycles, uint address, M68kOperandSize size)
        {
            if (_amigaBus != null)
            {
                return _amigaBus.ReadJitSlotAwareMemory(ref cycles, address, size);
            }

            if (size == M68kOperandSize.Byte)
            {
                return _bus.ReadByte(address, ref cycles, AmigaBusAccessKind.CpuDataRead);
            }

            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException(
                    size == M68kOperandSize.Word
                        ? $"Odd MC68000 word read at 0x{address:X8}."
                        : $"Odd MC68000 long read at 0x{address:X8}.");
            }

            return size == M68kOperandSize.Word
                ? _bus.ReadWord(address, ref cycles, AmigaBusAccessKind.CpuDataRead)
                : _bus.ReadLong(address, ref cycles, AmigaBusAccessKind.CpuDataRead);
        }

        private void WriteMemoryValueForV2Batch(uint address, uint value, M68kOperandSize size)
        {
            WriteMemoryValue(address, value, size);
        }

        private void WriteMemoryValueForV2BatchSlowRef(ref long cycles, uint address, uint value, M68kOperandSize size)
        {
            if (_amigaBus != null)
            {
                _amigaBus.WriteJitSlotAwareMemory(ref cycles, address, value, size);
                return;
            }

            if (size == M68kOperandSize.Byte)
            {
                _bus.WriteByte(address, (byte)value, ref cycles, AmigaBusAccessKind.CpuDataWrite);
                return;
            }

            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException(
                    size == M68kOperandSize.Word
                        ? $"Odd MC68000 word write at 0x{address:X8}."
                        : $"Odd MC68000 long write at 0x{address:X8}.");
            }

            if (size == M68kOperandSize.Word)
            {
                _bus.WriteWord(address, (ushort)value, ref cycles, AmigaBusAccessKind.CpuDataWrite);
            }
            else
            {
                _bus.WriteLong(address, value, ref cycles, AmigaBusAccessKind.CpuDataWrite);
            }
        }

        private void WriteMemoryValue(uint address, uint value, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte)
            {
                WriteByte(address, (byte)value);
            }
            else if (size == M68kOperandSize.Word)
            {
                WriteWord(address, (ushort)value);
            }
            else
            {
                WriteLong(address, value);
            }
        }

        private byte ReadByte(uint address, AmigaBusAccessKind accessKind = AmigaBusAccessKind.CpuDataRead)
        {
            var cycle = State.Cycles;
            var value = _bus.ReadByte(address, ref cycle, accessKind);
            State.Cycles = cycle;
            return value;
        }

        private ushort ReadWord(uint address, AmigaBusAccessKind accessKind = AmigaBusAccessKind.CpuDataRead)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 word read at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            var value = _bus.ReadWord(address, ref cycle, accessKind);
            State.Cycles = cycle;
            return value;
        }

        private uint ReadLong(uint address)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 long read at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            var value = _bus.ReadLong(address, ref cycle, AmigaBusAccessKind.CpuDataRead);
            State.Cycles = cycle;
            return value;
        }

        private void WriteByte(uint address, byte value)
        {
            var cycle = State.Cycles;
            _bus.WriteByte(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
            State.Cycles = cycle;
        }

        private void WriteWord(uint address, ushort value)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 word write at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            _bus.WriteWord(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
            State.Cycles = cycle;
        }

        private void WriteLong(uint address, uint value)
        {
            if ((address & 1) != 0)
            {
                throw new AmigaEmulationException($"Odd MC68000 long write at 0x{address:X8}.");
            }

            var cycle = State.Cycles;
            _bus.WriteLong(address, value, ref cycle, AmigaBusAccessKind.CpuDataWrite);
            State.Cycles = cycle;
        }

        private void PushWord(ushort value)
        {
            State.SetActiveStackPointer(State.A[7] - 2);
            WriteWord(State.A[7], value);
        }

        private void PushLong(uint value)
        {
            State.SetActiveStackPointer(State.A[7] - 4);
            WriteLong(State.A[7], value);
        }

        private ushort PullWord()
        {
            var value = ReadWord(State.A[7]);
            State.SetActiveStackPointer(State.A[7] + 2);
            return value;
        }

        private uint PullLong()
        {
            var value = ReadLong(State.A[7]);
            State.SetActiveStackPointer(State.A[7] + 4);
            return value;
        }

        private void WriteDataRegister(int register, uint value, M68kOperandSize size)
        {
            var mask = M68kCpuState.Mask(size);
            State.D[register] = size == M68kOperandSize.Long
                ? value
                : (State.D[register] & ~mask) | (value & mask);
        }

        private void SetAddressRegister(int register, uint value)
        {
            if (register == 7)
            {
                State.SetActiveStackPointer(value);
                return;
            }

            State.A[register] = value;
        }

        private void SetCcr(ushort value)
        {
            State.StatusRegister = (ushort)((State.StatusRegister & 0xFFE0) | (value & 0x001F));
        }

        private uint Add(uint destination, uint source, M68kOperandSize size, bool setExtend)
        {
            var mask = M68kCpuState.Mask(size);
            var sign = M68kCpuState.SignBit(size);
            destination &= mask;
            source &= mask;
            var full = (ulong)destination + source;
            var result = (uint)full & mask;
            var carry = full > mask;
            var overflow = (~(destination ^ source) & (destination ^ result) & sign) != 0;
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, carry);
            if (setExtend)
            {
                State.SetFlag(M68kCpuState.Extend, carry);
            }

            return result;
        }

        private uint Subtract(uint destination, uint source, M68kOperandSize size, bool setExtend)
        {
            var mask = M68kCpuState.Mask(size);
            var sign = M68kCpuState.SignBit(size);
            destination &= mask;
            source &= mask;
            var result = (destination - source) & mask;
            var borrow = source > destination;
            var overflow = ((destination ^ source) & (destination ^ result) & sign) != 0;
            State.SetNegativeZero(result, size);
            State.SetFlag(M68kCpuState.Overflow, overflow);
            State.SetFlag(M68kCpuState.Carry, borrow);
            if (setExtend)
            {
                State.SetFlag(M68kCpuState.Extend, borrow);
            }

            return result;
        }

        private uint Shift(uint value, int count, M68kOperandSize size, int type, bool left)
        {
            value &= M68kCpuState.Mask(size);
            if (count == 0)
            {
                State.SetNegativeZero(value, size);
                State.SetFlag(M68kCpuState.Carry, false);
                State.SetFlag(M68kCpuState.Overflow, false);
                return value;
            }

            var bits = size == M68kOperandSize.Long ? 32 : size == M68kOperandSize.Word ? 16 : 8;
            var mask = M68kCpuState.Mask(size);
            var carry = false;
            var extend = State.GetFlag(M68kCpuState.Extend);
            for (var i = 0; i < count; i++)
            {
                if (left)
                {
                    carry = (value & (1u << (bits - 1))) != 0;
                    value = type switch
                    {
                        2 => ((value << 1) & mask) | (extend ? 1u : 0u),
                        3 => ((value << 1) & mask) | (carry ? 1u : 0u),
                        _ => (value << 1) & mask
                    };
                }
                else
                {
                    carry = (value & 1) != 0;
                    if (type == 0)
                    {
                        var sign = value & (1u << (bits - 1));
                        value = (value >> 1) | sign;
                    }
                    else if (type == 2)
                    {
                        value = (value >> 1) | (extend ? 1u << (bits - 1) : 0u);
                    }
                    else if (type == 3)
                    {
                        value = (value >> 1) | (carry ? 1u << (bits - 1) : 0u);
                    }
                    else
                    {
                        value >>= 1;
                    }
                }

                if (type == 2)
                {
                    extend = carry;
                }
            }

            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Carry, carry);
            if (type != 3)
            {
                State.SetFlag(M68kCpuState.Extend, carry);
            }

            State.SetFlag(M68kCpuState.Overflow, false);
            return value & mask;
        }

        private void SetLogicFlags(uint value, M68kOperandSize size)
        {
            State.SetNegativeZero(value, size);
            State.SetFlag(M68kCpuState.Overflow, false);
            State.SetFlag(M68kCpuState.Carry, false);
        }

        private bool CheckCondition(int condition)
        {
            var c = State.GetFlag(M68kCpuState.Carry);
            var v = State.GetFlag(M68kCpuState.Overflow);
            var z = State.GetFlag(M68kCpuState.Zero);
            var n = State.GetFlag(M68kCpuState.Negative);
            return condition switch
            {
                0x0 => true,
                0x1 => false,
                0x2 => !c && !z,
                0x3 => c || z,
                0x4 => !c,
                0x5 => c,
                0x6 => !z,
                0x7 => z,
                0x8 => !v,
                0x9 => v,
                0xA => !n,
                0xB => n,
                0xC => n == v,
                0xD => n != v,
                0xE => !z && n == v,
                0xF => z || n != v,
                _ => false
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddCycles(int cycles)
        {
            Debug.Assert(cycles > 0, "MC68000 cycle increments must be positive.");
            State.Cycles += cycles;
        }

        private void RaiseException(int vector, uint stackedProgramCounter, int cycles)
        {
            var savedStatusRegister = State.StatusRegister;
            State.StatusRegister |= M68kCpuState.Supervisor;
            PushLong(stackedProgramCounter);
            PushWord(savedStatusRegister);
            State.ProgramCounter = ReadLong((uint)(vector * 4));
            AddCycles(cycles);
        }

        private static uint AddressIncrement(int register, M68kOperandSize size)
        {
            if (size == M68kOperandSize.Byte && register == 7)
            {
                return 2;
            }

            return (uint)size;
        }

        private static int CountBits(int value)
        {
            var count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }

        private void AddTrace(TraceEntry trace)
        {
            RemoveTrace(trace.Root);
            RemoveV2HandoffCacheEntries(trace.Root);
            _traces[trace.Root] = trace;
            ForEachTracePage(trace, page =>
            {
                if (!_traceRootsByPage.TryGetValue(page, out var roots))
                {
                    roots = new HashSet<uint>();
                    _traceRootsByPage[page] = roots;
                }

                roots.Add(trace.Root);
            });
        }

        private bool RemoveTrace(uint root)
        {
            root = Normalize(root);
            if (!_traces.TryGetValue(root, out var trace))
            {
                return false;
            }

            _traces.Remove(root);
            _v2TraceHits.Remove(root);
            _v2TraceBranchExits.Remove(root);
            _v2UnsupportedGraphHoleExits.Remove(root);
            _v2ZeroInstructionExits.Remove(root);
            _v2BlockedPromotionTiers.Remove(root);
            _v2HandoffQueuePressures.Remove(root);
            _v2BranchPressureLimitedRoots.Remove(root);
            _v2BranchPressureQueuedTargets.Remove(root);
            _v2DisabledRoots.Remove(root);
            _v2DisabledRootReasons.Remove(root);
            RemoveV2HandoffCacheEntries(root);
            RemoveV2BranchPressureTargetEntries(root);
            ForEachTracePage(trace, page =>
            {
                if (!_traceRootsByPage.TryGetValue(page, out var roots))
                {
                    return;
                }

                roots.Remove(root);
                if (roots.Count == 0)
                {
                    _traceRootsByPage.Remove(page);
                }
            });
            return true;
        }

        private void RemoveV2HandoffCacheEntries(uint root)
        {
            if (_v2HandoffTraceCache.Count == 0)
            {
                return;
            }

            root = Normalize(root);
            List<ulong>? keysToRemove = null;
            foreach (var (key, trace) in _v2HandoffTraceCache)
            {
                var source = (uint)(key >> 24);
                var target = (uint)(key & 0x00FF_FFFF);
                if (source != root && target != root && trace.Root != root)
                {
                    continue;
                }

                keysToRemove ??= new List<ulong>();
                keysToRemove.Add(key);
            }

            if (keysToRemove == null)
            {
                return;
            }

            foreach (var key in keysToRemove)
            {
                _v2HandoffTraceCache.Remove(key);
            }
        }

        private void RemoveV2BranchPressureTargetEntries(uint root)
        {
            if (_v2BranchPressureLimitedTargets.Count == 0)
            {
                return;
            }

            root = Normalize(root);
            List<ulong>? keysToRemove = null;
            foreach (var key in _v2BranchPressureLimitedTargets)
            {
                var source = (uint)(key >> 24);
                var target = (uint)(key & 0x00FF_FFFF);
                if (source != root && target != root)
                {
                    continue;
                }

                keysToRemove ??= new List<ulong>();
                keysToRemove.Add(key);
            }

            if (keysToRemove == null)
            {
                return;
            }

            foreach (var key in keysToRemove)
            {
                _v2BranchPressureLimitedTargets.Remove(key);
            }
        }

        private static void ForEachTracePage(TraceEntry trace, Action<uint> action)
        {
            var remaining = trace.ByteLength;
            var current = trace.CodeStart;
            while (remaining > 0)
            {
                action(GetCodePageKey(current));
                var step = Math.Min(remaining, CodeGenerationPageSize - ((int)current & (CodeGenerationPageSize - 1)));
                remaining -= step;
                current = Normalize(current + (uint)step);
            }
        }

        private static uint GetCodePageKey(uint address)
            => Normalize(address) & ~(uint)(CodeGenerationPageSize - 1);

        private static bool RangesOverlap(uint leftAddress, int leftByteCount, uint rightAddress, int rightByteCount)
        {
            if (leftByteCount <= 0 || rightByteCount <= 0)
            {
                return false;
            }

            var leftStart = Normalize(leftAddress);
            var rightStart = Normalize(rightAddress);
            var leftEnd = (ulong)leftStart + (uint)leftByteCount;
            var rightEnd = (ulong)rightStart + (uint)rightByteCount;
            return (ulong)leftStart < rightEnd && (ulong)rightStart < leftEnd;
        }

        private void ClearRuntimeState()
        {
            _traces.Clear();
            _traceRootsByPage.Clear();
            _hotCounters.Clear();
            _blacklist.Clear();
            _v2TraceHits.Clear();
            _v2TraceBranchExits.Clear();
            _v2UnsupportedGraphHoleExits.Clear();
            _v2ZeroInstructionExits.Clear();
            _v2BlockedPromotionTiers.Clear();
            _v2HandoffQueuePressures.Clear();
            _v2BranchPressureLimitedRoots.Clear();
            _v2BranchPressureLimitedTargets.Clear();
            _v2BranchPressureQueuedTargets.Clear();
            _v2DisabledRoots.Clear();
            _v2DisabledRootReasons.Clear();
            _v2HandoffTraceCache.Clear();
            unchecked
            {
                _asyncCompileEpoch++;
            }

            _asyncCompiler?.Clear();
            ClearPendingV2OutOfBlockBranch();
        }

        private bool TryQueueAsyncTraceCompilation(
            uint root,
            V2TraceTier tier,
            M68kJitCompileReason reason,
            int priority,
            bool branchPressurePromotion)
            => TryQueueAsyncTraceCompilation(
                root,
                tier,
                reason,
                priority,
                branchPressurePromotion,
                out _);

        private bool TryQueueAsyncTraceCompilation(
            uint root,
            V2TraceTier tier,
            M68kJitCompileReason reason,
            int priority,
            bool branchPressurePromotion,
            out M68kAsyncJitQueueResult queueResult)
        {
            queueResult = M68kAsyncJitQueueResult.Dropped;
            if (_asyncCompiler == null)
            {
                return false;
            }

            priority = CalculateAsyncCompilePriority(
                root,
                tier,
                reason,
                priority,
                branchExits: 0,
                hasInternalLoop: false);
            var key = new M68kJitCompileKey(root, tier, M68kJitCompileKind.Trace);
            if (_asyncCompiler.TryUpdatePriority(key, priority, out queueResult, out var dedupeQueueDepth))
            {
                RecordAsyncQueueResult(queueResult);
                _counters.AsyncPriorityBumps++;
                _counters.AsyncSnapshotCapturesAvoided++;
                _counters.AsyncMaxQueueDepth = Math.Max(_counters.AsyncMaxQueueDepth, dedupeQueueDepth);
                return true;
            }

            if (_amigaBus == null ||
                !_amigaBus.TryCaptureJitCodeSnapshot(root, GetAsyncSnapshotByteCount(tier), out var snapshot))
            {
                return false;
            }

            var input = new M68kTraceCompilationInput(
                root,
                tier,
                reason,
                snapshot,
                CreateTraceCompilationOptions(root, reason));
            var request = M68kJitCompileRequest.ForTrace(
                Interlocked.Increment(ref _asyncCompileSequence),
                _asyncCompileEpoch,
                priority,
                branchPressurePromotion,
                input);
            queueResult = _asyncCompiler.Enqueue(request, out var queueDepth);
            RecordAsyncQueueResult(queueResult);
            _counters.AsyncMaxQueueDepth = Math.Max(_counters.AsyncMaxQueueDepth, queueDepth);
            return true;
        }

        private bool TryQueueAsyncV2Promotion(
            TraceEntry trace,
            V2TraceTier tier,
            bool branchPressurePromotion,
            int priority)
        {
            if (_asyncCompiler == null)
            {
                return false;
            }

            var key = new M68kJitCompileKey(trace.Root, tier, M68kJitCompileKind.Promotion);
            if (_asyncCompiler.TryUpdatePriority(key, priority, out var queueResult, out var dedupeQueueDepth))
            {
                RecordAsyncQueueResult(queueResult);
                _counters.AsyncPriorityBumps++;
                _counters.AsyncSnapshotCapturesAvoided++;
                _counters.AsyncMaxQueueDepth = Math.Max(_counters.AsyncMaxQueueDepth, dedupeQueueDepth);
                return true;
            }

            if (_amigaBus == null ||
                !_amigaBus.TryCaptureJitCodeSnapshot(trace.CodeStart, GetAsyncSnapshotByteCount(tier), out var snapshot))
            {
                return false;
            }

            var input = new M68kV2PromotionCompilationInput(
                trace.Root,
                tier,
                trace.CodeStart,
                trace.ByteLength,
                snapshot,
                CreateTraceCompilationOptions(trace.Root, M68kJitCompileReason.Promotion));
            var request = M68kJitCompileRequest.ForPromotion(
                Interlocked.Increment(ref _asyncCompileSequence),
                _asyncCompileEpoch,
                priority,
                branchPressurePromotion,
                input);
            RecordAsyncQueueResult(_asyncCompiler.Enqueue(request, out var queueDepth));
            _counters.AsyncMaxQueueDepth = Math.Max(_counters.AsyncMaxQueueDepth, queueDepth);
            return true;
        }

        private M68kTraceCompilationOptions CreateTraceCompilationOptions(uint root, M68kJitCompileReason reason)
        {
            var workerGraphExpansionEnabled = reason is not (
                M68kJitCompileReason.Handoff or
                M68kJitCompileReason.GraphHole or
                M68kJitCompileReason.BranchPressure);
            return CreateTraceCompilationOptions(root, workerGraphExpansionEnabled);
        }

        private M68kTraceCompilationOptions CreateTraceCompilationOptions(uint root, bool workerGraphExpansionEnabled)
            => new M68kTraceCompilationOptions(
                _v2Enabled && !_v2DisabledRoots.Contains(root),
                _v2Tier3Enabled,
                _v2MemoryReadEnabled,
                _v2BusAccessEnabled,
                _v2FastReadEnabled,
                _v2BusGraphEnabled || _v2BusAccessEnabled,
                workerGraphExpansionEnabled);

        private int CalculateAsyncCompilePriority(
            uint root,
            V2TraceTier tier,
            M68kJitCompileReason reason,
            int hits,
            int branchExits,
            bool hasInternalLoop)
        {
            var tierBonus = tier switch
            {
                V2TraceTier.Tier3 => 65_536,
                V2TraceTier.Tier2 => 16_384,
                V2TraceTier.Tier1 => 4_096,
                _ => 0
            };
            var reasonBonus = reason switch
            {
                M68kJitCompileReason.Handoff => 8_192,
                M68kJitCompileReason.BranchPressure => 4_096,
                M68kJitCompileReason.Promotion => 2_048,
                M68kJitCompileReason.GraphHole => 1_024,
                _ => 0
            };
            var loopBonus = hasInternalLoop ? 512 : 0;
            var blacklistPenalty = _blacklist.ContainsKey(root) ? 4_096 : 0;
            return Math.Max(0, hits + (branchExits * 4) + tierBonus + reasonBonus + loopBonus - blacklistPenalty);
        }

        private static int GetAsyncSnapshotByteCount(V2TraceTier tier)
        {
            var snapshotTier = tier switch
            {
                V2TraceTier.Tier0 or V2TraceTier.None => V2TraceTier.Tier1,
                V2TraceTier.Tier1 => V2TraceTier.Tier2,
                V2TraceTier.Tier2 => V2TraceTier.Tier3,
                _ => tier
            };
            var (_, tierBytes) = GetV2TierLimits(snapshotTier);
            return Math.Max(MaxTraceBytes, tierBytes);
        }

        private void RecordAsyncQueueResult(M68kAsyncJitQueueResult result)
        {
            switch (result)
            {
                case M68kAsyncJitQueueResult.Queued:
                    _counters.AsyncRequestsQueued++;
                    break;
                case M68kAsyncJitQueueResult.DedupedPending:
                case M68kAsyncJitQueueResult.DedupedCompiling:
                    _counters.AsyncRequestsDeduped++;
                    break;
                case M68kAsyncJitQueueResult.Dropped:
                    _counters.AsyncRequestsDropped++;
                    break;
            }
        }

        private void DrainAsyncCompileResults()
        {
            if (_asyncCompiler == null)
            {
                return;
            }

            while (_asyncCompiler.TryDequeueCompleted(out var result))
            {
                _counters.AsyncWorkerCompilesStarted++;
                _counters.AsyncWorkerCompileMilliseconds += Math.Max(0, result.CompileMilliseconds);
                if (!result.Succeeded)
                {
                    _counters.AsyncWorkerCompilesFailed++;
                    RecordAsyncCompileFailure(result);
                    continue;
                }

                _counters.AsyncWorkerCompilesCompleted++;
                if (result.Epoch != _asyncCompileEpoch)
                {
                    _counters.AsyncCompletedDiscardedSuperseded++;
                    continue;
                }

                if (result.Trace.HasValue)
                {
                    InstallAsyncTraceResult(result);
                }
                else if (result.Promotion.HasValue)
                {
                    InstallAsyncPromotionResult(result);
                }
            }
        }

        private void RecordAsyncCompileFailure(M68kJitCompileResult result)
        {
            if (result.Key.Kind == M68kJitCompileKind.Promotion)
            {
                BlockV2PromotionTier(result.Key.Root, result.Key.Tier);
                return;
            }

            if (result.Reason == M68kJitCompileReason.Handoff)
            {
                _v2HandoffQueuePressures.Remove(result.Key.Root);
                RecordV2TraceHandoffBlock("no-trace-compile-failed", result.Key.Root, countFailure: false);
            }
            else if (result.Reason == M68kJitCompileReason.BranchPressure)
            {
                _v2BranchPressureQueuedTargets.Remove(result.Key.Root);
            }

            _blacklist[result.Key.Root] = BlacklistHits;
            _counters.BlacklistCount++;
        }

        private void InstallAsyncTraceResult(M68kJitCompileResult result)
        {
            var plan = result.Trace!.Value.Plan;
            if (plan.Reason == M68kJitCompileReason.Handoff)
            {
                _v2HandoffQueuePressures.Remove(plan.Root);
            }

            if (plan.Reason == M68kJitCompileReason.BranchPressure)
            {
                _v2BranchPressureQueuedTargets.Remove(plan.Root);
            }

            if (_traces.TryGetValue(plan.Root, out var existing) &&
                !AsyncTraceResultImprovesExistingTrace(existing, result.Trace.Value.Trace))
            {
                _counters.AsyncCompletedDiscardedSuperseded++;
                if (plan.Reason == M68kJitCompileReason.Handoff)
                {
                    RecordV2TraceHandoffBlock("no-trace-superseded", plan.Root, countFailure: false);
                }

                return;
            }

            if (!TracePlanGenerationMatches(plan.CodeStart, plan.ByteLength, plan.StartGeneration, plan.EndGeneration, plan.CodeWords))
            {
                _counters.AsyncCompletedDiscardedStale++;
                if (plan.Reason == M68kJitCompileReason.Handoff)
                {
                    RecordV2TraceHandoffBlock("no-trace-stale", plan.Root, countFailure: false);
                }

                return;
            }

            AddTrace(result.Trace.Value.Trace);
            _counters.AsyncCompletedInstalled++;
            _counters.CompiledTraces++;
            RecordTracePlanCompiledCounters(plan);
            RecordAsyncTierInstall(plan.V2Trace.Tier);
            if (plan.Reason == M68kJitCompileReason.GraphHole)
            {
                _counters.V2GraphHoleTargetCompiles++;
            }
            else if (plan.Reason == M68kJitCompileReason.Handoff)
            {
                RecordV2TraceHandoffBlock("no-trace-installed", plan.Root, countFailure: false);
            }
        }

        private static bool AsyncTraceResultImprovesExistingTrace(TraceEntry existing, TraceEntry candidate)
        {
            if (candidate.V2Compiled == null)
            {
                return false;
            }

            if (existing.V2Compiled == null)
            {
                return true;
            }

            if (candidate.V2Tier > existing.V2Tier)
            {
                return true;
            }

            return candidate.V2Tier == existing.V2Tier &&
                candidate.V2InstructionCount > existing.V2InstructionCount;
        }

        private void InstallAsyncPromotionResult(M68kJitCompileResult result)
        {
            var promotion = result.Promotion!.Value;
            var plan = promotion.Plan;
            if (!_traces.TryGetValue(plan.Root, out var current) ||
                current.V2Compiled == null ||
                current.V2Tier >= plan.Tier)
            {
                _counters.AsyncCompletedDiscardedSuperseded++;
                return;
            }

            if (!TracePlanGenerationMatches(plan.CodeStart, plan.ByteLength, plan.StartGeneration, plan.EndGeneration, plan.CodeWords))
            {
                _counters.AsyncCompletedDiscardedStale++;
                return;
            }

            if (!V2PromotionExpandsTrace(current, promotion.Compiled) &&
                (!result.BranchPressurePromotion || !CanV2TierPromoteFurther(plan.Tier)))
            {
                BlockV2PromotionTier(plan.Root, plan.Tier);
                _counters.AsyncCompletedDiscardedSuperseded++;
                return;
            }

            var updated = current.WithV2(
                plan.CodeStart,
                plan.ByteLength,
                plan.StartGeneration,
                plan.EndGeneration,
                plan.CodeWords,
                promotion.Compiled.Compiled!,
                promotion.Compiled.Tier,
                promotion.Compiled.InstructionCount,
                promotion.Compiled.HasInternalLoop,
                promotion.Compiled.PureCpuBatchEligible);
            AddTrace(updated);
            _counters.AsyncCompletedInstalled++;
            _v2TraceBranchExits[plan.Root] = 0;
            _v2BlockedPromotionTiers.Remove(plan.Root);
            RecordV2CompiledTrace(plan.Tier);
            RecordAsyncTierInstall(plan.Tier);
            if (plan.V2Trace.WorkerGraphExpanded)
            {
                _counters.V2WorkerExpandedGraphs++;
                _counters.V2WorkerExpandedGraphInstructions += plan.V2Trace.InstructionCount;
                _counters.V2WorkerExpandedGraphBytes += plan.V2Trace.ByteLength;
            }

            _counters.V2TierPromotions++;
            if (result.BranchPressurePromotion)
            {
                _counters.V2TierPressurePromotions++;
            }
        }

        private bool TracePlanGenerationMatches(
            uint codeStart,
            int byteLength,
            uint startGeneration,
            uint endGeneration,
            ushort[] codeWords)
        {
            if (_amigaBus == null ||
                !_amigaBus.CodeRangeGenerationMatches(codeStart, byteLength, startGeneration, endGeneration))
            {
                return false;
            }

            var address = codeStart;
            for (var i = 0; i < codeWords.Length; i++)
            {
                if (_amigaBus.ReadHostWord(address) != codeWords[i])
                {
                    return false;
                }

                address = Normalize(address + 2);
            }

            return true;
        }

        private void RecordAsyncTierInstall(V2TraceTier tier)
        {
            switch (tier)
            {
                case V2TraceTier.Tier3:
                    _counters.AsyncTier3Installs++;
                    break;
                case V2TraceTier.Tier2:
                    _counters.AsyncTier2Installs++;
                    break;
                case V2TraceTier.Tier1:
                    _counters.AsyncTier1Installs++;
                    break;
                default:
                    _counters.AsyncTier0Installs++;
                    break;
            }
        }

        private void ClearPendingV2OutOfBlockBranch()
        {
            _v2PendingOutOfBlockBranch = false;
            _v2PendingOutOfBlockBranchRoot = 0;
            _v2PendingOutOfBlockBranchTarget = 0;
            _v2PendingOutOfBlockBranchCodeStart = 0;
            _v2PendingOutOfBlockBranchByteLength = 0;
            _v2PendingOutOfBlockBranchIsFallthrough = false;
        }

        private static uint Normalize(uint address)
        {
            return address & 0x00FF_FFFF;
        }

        private static uint GetBranchTarget(M68kDecodedInstruction instruction)
            => Normalize(instruction.BranchBase + unchecked((uint)instruction.Displacement));

        private delegate int CompiledTrace(M68kJitCore core, int maxInstructions, long targetCycle, IM68kInstructionBoundary boundary);

        private enum V2PendingFlags
        {
            None = 0,
            Logic = 1,
            Add = 2,
            Subtract = 3
        }

        private sealed class V2EmitContext
        {
            private static readonly PropertyInfo CoreStateProperty =
                typeof(M68kJitCore).GetProperty(nameof(M68kJitCore.State)) ??
                throw new MissingMemberException(typeof(M68kJitCore).FullName, nameof(M68kJitCore.State));
            private static readonly PropertyInfo DataRegistersProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.D)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.D));
            private static readonly PropertyInfo AddressRegistersProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.A)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.A));
            private static readonly PropertyInfo ProgramCounterProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.ProgramCounter)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.ProgramCounter));
            private static readonly PropertyInfo CyclesProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.Cycles)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.Cycles));
            private static readonly PropertyInfo StatusRegisterProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.StatusRegister)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.StatusRegister));
            private static readonly PropertyInfo HaltedProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.Halted)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.Halted));
            private static readonly PropertyInfo StoppedProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.Stopped)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.Stopped));
            private static readonly PropertyInfo LastOpcodeProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.LastOpcode)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.LastOpcode));
            private static readonly PropertyInfo LastInstructionProgramCounterProperty =
                typeof(M68kCpuState).GetProperty(nameof(M68kCpuState.LastInstructionProgramCounter)) ??
                throw new MissingMemberException(typeof(M68kCpuState).FullName, nameof(M68kCpuState.LastInstructionProgramCounter));
            private static readonly MethodInfo MaterializeFlags =
                typeof(M68kJitCore).GetMethod(nameof(MaterializeV2Flags), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(MaterializeV2Flags));
            private static readonly MethodInfo RecordLazyWriteback =
                typeof(M68kJitCore).GetMethod(nameof(RecordV2LazyWriteback), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(RecordV2LazyWriteback));
            private static readonly MethodInfo RecordZeroWaitTraceStats =
                typeof(M68kJitCore).GetMethod(nameof(RecordV2ZeroWaitTraceStats), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(RecordV2ZeroWaitTraceStats));
            private static readonly MethodInfo RecordOutOfBlockSideExit =
                typeof(M68kJitCore).GetMethod(
                    nameof(RecordV2OutOfBlockSideExit),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    new[] { typeof(uint), typeof(uint), typeof(uint), typeof(int), typeof(bool) },
                    modifiers: null) ??
                throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(RecordV2OutOfBlockSideExit));
            private static readonly FieldInfo PendingOutOfBlockBranchField =
                typeof(M68kJitCore).GetField(nameof(_v2PendingOutOfBlockBranch), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(M68kJitCore).FullName, nameof(_v2PendingOutOfBlockBranch));
            private static readonly FieldInfo PendingOutOfBlockBranchRootField =
                typeof(M68kJitCore).GetField(nameof(_v2PendingOutOfBlockBranchRoot), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(M68kJitCore).FullName, nameof(_v2PendingOutOfBlockBranchRoot));
            private static readonly FieldInfo PendingOutOfBlockBranchTargetField =
                typeof(M68kJitCore).GetField(nameof(_v2PendingOutOfBlockBranchTarget), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(M68kJitCore).FullName, nameof(_v2PendingOutOfBlockBranchTarget));
            private static readonly FieldInfo PendingOutOfBlockBranchCodeStartField =
                typeof(M68kJitCore).GetField(nameof(_v2PendingOutOfBlockBranchCodeStart), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(M68kJitCore).FullName, nameof(_v2PendingOutOfBlockBranchCodeStart));
            private static readonly FieldInfo PendingOutOfBlockBranchByteLengthField =
                typeof(M68kJitCore).GetField(nameof(_v2PendingOutOfBlockBranchByteLength), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(M68kJitCore).FullName, nameof(_v2PendingOutOfBlockBranchByteLength));
            private static readonly FieldInfo PendingOutOfBlockBranchIsFallthroughField =
                typeof(M68kJitCore).GetField(nameof(_v2PendingOutOfBlockBranchIsFallthrough), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingFieldException(typeof(M68kJitCore).FullName, nameof(_v2PendingOutOfBlockBranchIsFallthrough));

            private readonly ILGenerator _il;

            private V2EmitContext(ILGenerator il)
            {
                _il = il;
                State = il.DeclareLocal(typeof(M68kCpuState));
                DataRegisterArray = il.DeclareLocal(typeof(uint[]));
                AddressRegisterArray = il.DeclareLocal(typeof(uint[]));
                DataRegisters = new LocalBuilder[8];
                AddressRegisters = new LocalBuilder[8];
                for (var i = 0; i < 8; i++)
                {
                    DataRegisters[i] = il.DeclareLocal(typeof(uint));
                    AddressRegisters[i] = il.DeclareLocal(typeof(uint));
                }

                ProgramCounter = il.DeclareLocal(typeof(uint));
                Cycles = il.DeclareLocal(typeof(long));
                StatusRegister = il.DeclareLocal(typeof(int));
                PendingKind = il.DeclareLocal(typeof(int));
                PendingDestination = il.DeclareLocal(typeof(uint));
                PendingSource = il.DeclareLocal(typeof(uint));
                PendingResult = il.DeclareLocal(typeof(uint));
                PendingSize = il.DeclareLocal(typeof(int));
                PendingSetExtend = il.DeclareLocal(typeof(bool));
                Executed = il.DeclareLocal(typeof(int));
                LastOpcode = il.DeclareLocal(typeof(int));
                LastInstructionProgramCounter = il.DeclareLocal(typeof(uint));
                PreviousLastOpcode = il.DeclareLocal(typeof(int));
                PreviousLastInstructionProgramCounter = il.DeclareLocal(typeof(uint));
                ZeroWaitReadRealFast = il.DeclareLocal(typeof(int));
                ZeroWaitReadRom = il.DeclareLocal(typeof(int));
                ZeroWaitReadOverlay = il.DeclareLocal(typeof(int));
                ZeroWaitWriteRealFast = il.DeclareLocal(typeof(int));
                ZeroWaitReadSlow = il.DeclareLocal(typeof(int));
                ZeroWaitWriteSlow = il.DeclareLocal(typeof(int));
            }

            public LocalBuilder State { get; }

            public LocalBuilder DataRegisterArray { get; }

            public LocalBuilder AddressRegisterArray { get; }

            public LocalBuilder[] DataRegisters { get; }

            public LocalBuilder[] AddressRegisters { get; }

            public LocalBuilder ProgramCounter { get; }

            public LocalBuilder Cycles { get; }

            public LocalBuilder StatusRegister { get; }

            public LocalBuilder PendingKind { get; }

            public LocalBuilder PendingDestination { get; }

            public LocalBuilder PendingSource { get; }

            public LocalBuilder PendingResult { get; }

            public LocalBuilder PendingSize { get; }

            public LocalBuilder PendingSetExtend { get; }

            public LocalBuilder Executed { get; }

            public LocalBuilder LastOpcode { get; }

            public LocalBuilder LastInstructionProgramCounter { get; }

            public LocalBuilder PreviousLastOpcode { get; }

            public LocalBuilder PreviousLastInstructionProgramCounter { get; }

            public LocalBuilder ZeroWaitReadRealFast { get; }

            public LocalBuilder ZeroWaitReadRom { get; }

            public LocalBuilder ZeroWaitReadOverlay { get; }

            public LocalBuilder ZeroWaitWriteRealFast { get; }

            public LocalBuilder ZeroWaitReadSlow { get; }

            public LocalBuilder ZeroWaitWriteSlow { get; }

            public bool FastReadOnlyFailureEnabled { get; private set; }

            public Label FastReadOnlyFailureLabel { get; private set; }

            public bool ZeroWaitProbeEnabled { get; private set; } = true;

            public bool ZeroWaitStatsUsed { get; private set; }

            public static V2EmitContext Create(ILGenerator il)
                => new V2EmitContext(il);

            public void SetZeroWaitProbe(bool enabled)
            {
                ZeroWaitProbeEnabled = enabled;
            }

            public void SetFastReadOnlyFailure(bool enabled, Label failureLabel)
            {
                FastReadOnlyFailureEnabled = enabled;
                FastReadOnlyFailureLabel = failureLabel;
            }

            public void EmitLoadState(
                uint root,
                Label returnZero,
                int loadDataRegisters = 0xFF,
                int loadAddressRegisters = 0xFF)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Call, CoreStateProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, State);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, ProgramCounterProperty.GetMethod!);
                EmitLoadUIntConstant(_il, root);
                _il.Emit(OpCodes.Bne_Un, returnZero);

                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, DataRegistersProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, DataRegisterArray);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, AddressRegistersProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, AddressRegisterArray);
                for (var i = 0; i < 8; i++)
                {
                    if ((loadDataRegisters & (1 << i)) != 0)
                    {
                        EmitLoadArrayElement(DataRegisterArray, i);
                        _il.Emit(OpCodes.Stloc, DataRegisters[i]);
                    }

                    if ((loadAddressRegisters & (1 << i)) != 0)
                    {
                        EmitLoadArrayElement(AddressRegisterArray, i);
                        _il.Emit(OpCodes.Stloc, AddressRegisters[i]);
                    }
                }

                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, CyclesProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, Cycles);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, StatusRegisterProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, StatusRegister);
                EmitStoreInt(PendingKind, 0);
                EmitStoreInt(Executed, 0);
            }

            public void EmitCanContinue(Label exit)
            {
                _il.Emit(OpCodes.Ldloc, Executed);
                _il.Emit(OpCodes.Ldarg_1);
                _il.Emit(OpCodes.Bge, exit);
                _il.Emit(OpCodes.Ldloc, Cycles);
                _il.Emit(OpCodes.Ldarg_2);
                _il.Emit(OpCodes.Bge, exit);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, HaltedProperty.GetMethod!);
                _il.Emit(OpCodes.Brtrue, exit);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, StoppedProperty.GetMethod!);
                _il.Emit(OpCodes.Brtrue, exit);
            }

            public void EmitCanContinuePure(Label exit)
            {
                _il.Emit(OpCodes.Ldloc, Executed);
                _il.Emit(OpCodes.Ldarg_1);
                _il.Emit(OpCodes.Bge, exit);
                _il.Emit(OpCodes.Ldloc, Cycles);
                _il.Emit(OpCodes.Ldarg_2);
                _il.Emit(OpCodes.Bge, exit);
            }

            public void EmitReturnZeroIfNoInstructions(Label returnZero)
            {
                _il.Emit(OpCodes.Ldloc, Executed);
                _il.Emit(OpCodes.Brfalse, returnZero);
            }

            public void EmitStartInstruction(M68kDecodedInstruction instruction)
            {
                EmitLoadUIntConstant(_il, instruction.ProgramCounter);
                _il.Emit(OpCodes.Stloc, LastInstructionProgramCounter);
                _il.Emit(OpCodes.Ldc_I4, instruction.Opcode);
                _il.Emit(OpCodes.Stloc, LastOpcode);
                EmitLoadUIntConstant(_il, Normalize(instruction.ProgramCounter + (uint)instruction.Length));
                _il.Emit(OpCodes.Stloc, ProgramCounter);
                _il.Emit(OpCodes.Ldloc, Executed);
                _il.Emit(OpCodes.Ldc_I4_1);
                _il.Emit(OpCodes.Add);
                _il.Emit(OpCodes.Stloc, Executed);
            }

            public void EmitSaveFastReadFailureBookkeeping()
            {
                _il.Emit(OpCodes.Ldloc, LastOpcode);
                _il.Emit(OpCodes.Stloc, PreviousLastOpcode);
                _il.Emit(OpCodes.Ldloc, LastInstructionProgramCounter);
                _il.Emit(OpCodes.Stloc, PreviousLastInstructionProgramCounter);
            }

            public void EmitRollbackFastReadFailure(uint programCounter, Label returnZero)
            {
                _il.Emit(OpCodes.Ldloc, Executed);
                _il.Emit(OpCodes.Ldc_I4_1);
                _il.Emit(OpCodes.Sub);
                _il.Emit(OpCodes.Stloc, Executed);
                EmitLoadUIntConstant(_il, programCounter);
                _il.Emit(OpCodes.Stloc, ProgramCounter);
                _il.Emit(OpCodes.Ldloc, PreviousLastOpcode);
                _il.Emit(OpCodes.Stloc, LastOpcode);
                _il.Emit(OpCodes.Ldloc, PreviousLastInstructionProgramCounter);
                _il.Emit(OpCodes.Stloc, LastInstructionProgramCounter);
                _il.Emit(OpCodes.Ldloc, Executed);
                _il.Emit(OpCodes.Brfalse, returnZero);
            }

            public void EmitStoreInstructionContext()
            {
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, ProgramCounter);
                _il.Emit(OpCodes.Call, ProgramCounterProperty.SetMethod!);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, Cycles);
                _il.Emit(OpCodes.Call, CyclesProperty.SetMethod!);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, LastOpcode);
                _il.Emit(OpCodes.Conv_U2);
                _il.Emit(OpCodes.Call, LastOpcodeProperty.SetMethod!);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, LastInstructionProgramCounter);
                _il.Emit(OpCodes.Call, LastInstructionProgramCounterProperty.SetMethod!);
            }

            public void EmitWriteback(int dirtyDataRegisters, int dirtyAddressRegisters)
                => EmitStoreState(recordLazyWriteback: true, dirtyDataRegisters, dirtyAddressRegisters);

            public void EmitStoreState(bool recordLazyWriteback)
                => EmitStoreState(recordLazyWriteback, dirtyDataRegisters: 0xFF, dirtyAddressRegisters: 0xFF);

            private void EmitStoreState(bool recordLazyWriteback, int dirtyDataRegisters, int dirtyAddressRegisters)
            {
                EmitMaterializePendingFlags();
                for (var i = 0; i < 8; i++)
                {
                    if ((dirtyDataRegisters & (1 << i)) != 0)
                    {
                        _il.Emit(OpCodes.Ldloc, DataRegisterArray);
                        _il.Emit(OpCodes.Ldc_I4, i);
                        _il.Emit(OpCodes.Ldloc, DataRegisters[i]);
                        _il.Emit(OpCodes.Stelem_I4);
                    }

                    if ((dirtyAddressRegisters & (1 << i)) != 0)
                    {
                        _il.Emit(OpCodes.Ldloc, AddressRegisterArray);
                        _il.Emit(OpCodes.Ldc_I4, i);
                        _il.Emit(OpCodes.Ldloc, AddressRegisters[i]);
                        _il.Emit(OpCodes.Stelem_I4);
                    }
                }

                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, ProgramCounter);
                _il.Emit(OpCodes.Call, ProgramCounterProperty.SetMethod!);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, Cycles);
                _il.Emit(OpCodes.Call, CyclesProperty.SetMethod!);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, StatusRegister);
                _il.Emit(OpCodes.Conv_U2);
                _il.Emit(OpCodes.Call, StatusRegisterProperty.SetMethod!);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, LastOpcode);
                _il.Emit(OpCodes.Conv_U2);
                _il.Emit(OpCodes.Call, LastOpcodeProperty.SetMethod!);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, LastInstructionProgramCounter);
                _il.Emit(OpCodes.Call, LastInstructionProgramCounterProperty.SetMethod!);
                EmitRecordZeroWaitTraceStats();
                if (recordLazyWriteback)
                {
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Call, RecordLazyWriteback);
                }
            }

            public void EmitReloadState()
            {
                for (var i = 0; i < 8; i++)
                {
                    EmitLoadArrayElement(DataRegisterArray, i);
                    _il.Emit(OpCodes.Stloc, DataRegisters[i]);
                    EmitLoadArrayElement(AddressRegisterArray, i);
                    _il.Emit(OpCodes.Stloc, AddressRegisters[i]);
                }

                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, ProgramCounterProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, ProgramCounter);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, CyclesProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, Cycles);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, StatusRegisterProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, StatusRegister);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, LastOpcodeProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, LastOpcode);
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, LastInstructionProgramCounterProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, LastInstructionProgramCounter);
                EmitStoreInt(PendingKind, 0);
            }

            public void EmitCheckCondition(int condition)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, StatusRegister);
                _il.Emit(OpCodes.Ldloc, PendingKind);
                _il.Emit(OpCodes.Ldloc, PendingDestination);
                _il.Emit(OpCodes.Ldloc, PendingSource);
                _il.Emit(OpCodes.Ldloc, PendingResult);
                _il.Emit(OpCodes.Ldloc, PendingSize);
                _il.Emit(OpCodes.Ldc_I4, condition);
                _il.Emit(OpCodes.Call, CheckV2ConditionFromPendingFlagsMethod);
            }

            public void EmitIncrementZeroWaitRead(LocalBuilder targetKind)
            {
                ZeroWaitStatsUsed = true;
                var rom = _il.DefineLabel();
                var overlay = _il.DefineLabel();
                var done = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, targetKind);
                _il.Emit(OpCodes.Ldc_I4_1);
                _il.Emit(OpCodes.Beq, rom);
                _il.Emit(OpCodes.Ldloc, targetKind);
                _il.Emit(OpCodes.Ldc_I4_2);
                _il.Emit(OpCodes.Beq, overlay);
                EmitIncrementLocal(ZeroWaitReadRealFast);
                _il.Emit(OpCodes.Br, done);
                _il.MarkLabel(rom);
                EmitIncrementLocal(ZeroWaitReadRom);
                _il.Emit(OpCodes.Br, done);
                _il.MarkLabel(overlay);
                EmitIncrementLocal(ZeroWaitReadOverlay);
                _il.MarkLabel(done);
            }

            public void EmitIncrementZeroWaitWriteRealFast()
            {
                ZeroWaitStatsUsed = true;
                EmitIncrementLocal(ZeroWaitWriteRealFast);
            }

            public void EmitIncrementZeroWaitSlowRead()
            {
                ZeroWaitStatsUsed = true;
                EmitIncrementLocal(ZeroWaitReadSlow);
            }

            public void EmitIncrementZeroWaitSlowWrite()
            {
                ZeroWaitStatsUsed = true;
                EmitIncrementLocal(ZeroWaitWriteSlow);
            }

            public void EmitReloadCycles()
            {
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Call, CyclesProperty.GetMethod!);
                _il.Emit(OpCodes.Stloc, Cycles);
            }

            public void EmitStoreCycles()
            {
                _il.Emit(OpCodes.Ldloc, State);
                _il.Emit(OpCodes.Ldloc, Cycles);
                _il.Emit(OpCodes.Call, CyclesProperty.SetMethod!);
            }

            public void EmitLoadDataRegister(int register, M68kOperandSize size)
            {
                _il.Emit(OpCodes.Ldloc, DataRegisters[register]);
                EmitLoadUIntConstant(_il, M68kCpuState.Mask(size));
                _il.Emit(OpCodes.And);
            }

            public void EmitLoadAddressRegister(int register)
            {
                _il.Emit(OpCodes.Ldloc, AddressRegisters[register]);
            }

            public void EmitStoreDataRegister(int register, LocalBuilder value, M68kOperandSize size)
            {
                if (size == M68kOperandSize.Long)
                {
                    _il.Emit(OpCodes.Ldloc, value);
                    _il.Emit(OpCodes.Stloc, DataRegisters[register]);
                    return;
                }

                _il.Emit(OpCodes.Ldloc, DataRegisters[register]);
                EmitLoadUIntConstant(_il, ~M68kCpuState.Mask(size));
                _il.Emit(OpCodes.And);
                _il.Emit(OpCodes.Ldloc, value);
                EmitLoadUIntConstant(_il, M68kCpuState.Mask(size));
                _il.Emit(OpCodes.And);
                _il.Emit(OpCodes.Or);
                _il.Emit(OpCodes.Stloc, DataRegisters[register]);
            }

            public void EmitStoreAddressRegister(int register, LocalBuilder value)
            {
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Stloc, AddressRegisters[register]);
            }

            public void EmitSetPendingLogic(LocalBuilder result, M68kOperandSize size)
            {
                EmitStoreInt(PendingKind, (int)V2PendingFlags.Logic);
                EmitStoreUInt(PendingDestination, 0);
                EmitStoreUInt(PendingSource, 0);
                _il.Emit(OpCodes.Ldloc, result);
                _il.Emit(OpCodes.Stloc, PendingResult);
                EmitStoreInt(PendingSize, (int)size);
                EmitStoreBool(PendingSetExtend, false);
            }

            public void EmitSetPendingArithmetic(
                V2PendingFlags kind,
                LocalBuilder destination,
                LocalBuilder source,
                LocalBuilder result,
                M68kOperandSize size,
                bool setExtend)
            {
                EmitStoreInt(PendingKind, (int)kind);
                _il.Emit(OpCodes.Ldloc, destination);
                _il.Emit(OpCodes.Stloc, PendingDestination);
                _il.Emit(OpCodes.Ldloc, source);
                _il.Emit(OpCodes.Stloc, PendingSource);
                _il.Emit(OpCodes.Ldloc, result);
                _il.Emit(OpCodes.Stloc, PendingResult);
                EmitStoreInt(PendingSize, (int)size);
                EmitStoreBool(PendingSetExtend, setExtend);
            }

            public void EmitMaterializePendingFlags()
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, StatusRegister);
                _il.Emit(OpCodes.Ldloc, PendingKind);
                _il.Emit(OpCodes.Ldloc, PendingDestination);
                _il.Emit(OpCodes.Ldloc, PendingSource);
                _il.Emit(OpCodes.Ldloc, PendingResult);
                _il.Emit(OpCodes.Ldloc, PendingSize);
                _il.Emit(OpCodes.Ldloc, PendingSetExtend);
                _il.Emit(OpCodes.Call, MaterializeFlags);
                _il.Emit(OpCodes.Stloc, StatusRegister);
                EmitStoreInt(PendingKind, 0);
            }

            private void EmitRecordZeroWaitTraceStats()
            {
                if (!ZeroWaitStatsUsed)
                {
                    return;
                }

                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, ZeroWaitReadRealFast);
                _il.Emit(OpCodes.Ldloc, ZeroWaitReadRom);
                _il.Emit(OpCodes.Ldloc, ZeroWaitReadOverlay);
                _il.Emit(OpCodes.Ldloc, ZeroWaitWriteRealFast);
                _il.Emit(OpCodes.Ldloc, ZeroWaitReadSlow);
                _il.Emit(OpCodes.Ldloc, ZeroWaitWriteSlow);
                _il.Emit(OpCodes.Call, RecordZeroWaitTraceStats);
                EmitStoreInt(ZeroWaitReadRealFast, 0);
                EmitStoreInt(ZeroWaitReadRom, 0);
                EmitStoreInt(ZeroWaitReadOverlay, 0);
                EmitStoreInt(ZeroWaitWriteRealFast, 0);
                EmitStoreInt(ZeroWaitReadSlow, 0);
                EmitStoreInt(ZeroWaitWriteSlow, 0);
            }

            public void EmitAddCycles(int cycles)
            {
                _il.Emit(OpCodes.Ldloc, Cycles);
                _il.Emit(OpCodes.Ldc_I8, (long)cycles);
                _il.Emit(OpCodes.Add);
                _il.Emit(OpCodes.Stloc, Cycles);
            }

            public void EmitSetProgramCounter(uint value)
            {
                EmitLoadUIntConstant(_il, Normalize(value));
                _il.Emit(OpCodes.Stloc, ProgramCounter);
            }

            public void EmitSetProgramCounter(LocalBuilder value)
            {
                _il.Emit(OpCodes.Ldloc, value);
                _il.Emit(OpCodes.Stloc, ProgramCounter);
            }

            public void EmitRecordOutOfBlockSideExit(uint root, uint target, uint codeStart, int byteLength, bool isFallthrough)
            {
                _il.Emit(OpCodes.Ldarg_0);
                EmitLoadUIntConstant(_il, root);
                EmitLoadUIntConstant(_il, target);
                EmitLoadUIntConstant(_il, codeStart);
                _il.Emit(OpCodes.Ldc_I4, byteLength);
                _il.Emit(isFallthrough ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Call, RecordOutOfBlockSideExit);
            }

            public void EmitRecordOutOfBlockSideExit(uint root, LocalBuilder target, uint codeStart, int byteLength, bool isFallthrough)
            {
                _il.Emit(OpCodes.Ldarg_0);
                EmitLoadUIntConstant(_il, root);
                _il.Emit(OpCodes.Ldloc, target);
                EmitLoadUIntConstant(_il, codeStart);
                _il.Emit(OpCodes.Ldc_I4, byteLength);
                _il.Emit(isFallthrough ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Call, RecordOutOfBlockSideExit);
            }

            public void EmitMarkOutOfBlockBranchExit(uint root, uint target, uint codeStart, int byteLength, bool isFallthrough)
            {
                EmitMarkOutOfBlockBranchHeader(root);
                _il.Emit(OpCodes.Ldarg_0);
                EmitLoadUIntConstant(_il, target);
                _il.Emit(OpCodes.Stfld, PendingOutOfBlockBranchTargetField);
                EmitMarkOutOfBlockBranchFooter(codeStart, byteLength, isFallthrough);
            }

            public void EmitMarkOutOfBlockBranchExit(uint root, LocalBuilder target, uint codeStart, int byteLength, bool isFallthrough)
            {
                EmitMarkOutOfBlockBranchHeader(root);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, target);
                _il.Emit(OpCodes.Stfld, PendingOutOfBlockBranchTargetField);
                EmitMarkOutOfBlockBranchFooter(codeStart, byteLength, isFallthrough);
            }

            private void EmitMarkOutOfBlockBranchHeader(uint root)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4_1);
                _il.Emit(OpCodes.Stfld, PendingOutOfBlockBranchField);
                _il.Emit(OpCodes.Ldarg_0);
                EmitLoadUIntConstant(_il, root);
                _il.Emit(OpCodes.Stfld, PendingOutOfBlockBranchRootField);
            }

            private void EmitMarkOutOfBlockBranchFooter(uint codeStart, int byteLength, bool isFallthrough)
            {
                _il.Emit(OpCodes.Ldarg_0);
                EmitLoadUIntConstant(_il, codeStart);
                _il.Emit(OpCodes.Stfld, PendingOutOfBlockBranchCodeStartField);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, byteLength);
                _il.Emit(OpCodes.Stfld, PendingOutOfBlockBranchByteLengthField);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(isFallthrough ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Stfld, PendingOutOfBlockBranchIsFallthroughField);
            }

            private void EmitLoadArrayElement(LocalBuilder array, int index)
            {
                _il.Emit(OpCodes.Ldloc, array);
                _il.Emit(OpCodes.Ldc_I4, index);
                _il.Emit(OpCodes.Ldelem_U4);
            }

            private void EmitStoreInt(LocalBuilder local, int value)
            {
                _il.Emit(OpCodes.Ldc_I4, value);
                _il.Emit(OpCodes.Stloc, local);
            }

            private void EmitStoreUInt(LocalBuilder local, uint value)
            {
                EmitLoadUIntConstant(_il, value);
                _il.Emit(OpCodes.Stloc, local);
            }

            private void EmitStoreBool(LocalBuilder local, bool value)
            {
                _il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Stloc, local);
            }

            private void EmitIncrementLocal(LocalBuilder local)
            {
                _il.Emit(OpCodes.Ldloc, local);
                _il.Emit(OpCodes.Ldc_I4_1);
                _il.Emit(OpCodes.Add);
                _il.Emit(OpCodes.Stloc, local);
            }
        }

        private enum V2TraceTier
        {
            None = 0,
            Tier0 = 1,
            Tier1 = 2,
            Tier2 = 3,
            Tier3 = 4
        }

        private enum V2TraceRejectionReason
        {
            None = 0,
            Disabled,
            NoBus,
            ChipRam,
            HostTrap,
            DecodeFailure,
            UnsupportedOperation,
            UnsupportedEa,
            Budget,
            Empty
        }

        private readonly struct V2TraceCompilation
        {
            public V2TraceCompilation(
                CompiledTrace compiled,
                V2TraceTier tier,
                M68kDecodedInstruction[] instructions,
                uint codeStart,
                int byteLength,
                bool hasInternalLoop,
                bool pureCpuBatchEligible)
            {
                Compiled = compiled;
                Tier = tier;
                Instructions = instructions;
                CodeStart = codeStart;
                ByteLength = byteLength;
                HasInternalLoop = hasInternalLoop;
                PureCpuBatchEligible = pureCpuBatchEligible;
            }

            public bool IsEmpty => Compiled == null;

            public CompiledTrace? Compiled { get; }

            public V2TraceTier Tier { get; }

            public M68kDecodedInstruction[] Instructions { get; }

            public uint CodeStart { get; }

            public int ByteLength { get; }

            public int InstructionCount => Instructions?.Length ?? 0;

            public bool HasInternalLoop { get; }

            public bool PureCpuBatchEligible { get; }
        }

        private readonly struct V2TracePlan
        {
            public V2TracePlan(
                V2TraceTier tier,
                M68kDecodedInstruction[] instructions,
                uint codeStart,
                int byteLength,
                bool hasInternalLoop,
                bool pureCpuBatchEligible,
                bool fastReadOnlyBatchEligible,
                uint[] externalHandoffTargets,
                bool workerGraphExpanded = false)
            {
                Tier = tier;
                Instructions = instructions;
                CodeStart = codeStart;
                ByteLength = byteLength;
                HasInternalLoop = hasInternalLoop;
                PureCpuBatchEligible = pureCpuBatchEligible;
                FastReadOnlyBatchEligible = fastReadOnlyBatchEligible;
                ExternalHandoffTargets = externalHandoffTargets;
                WorkerGraphExpanded = workerGraphExpanded;
            }

            public bool IsEmpty => Instructions == null || Instructions.Length == 0;

            public V2TraceTier Tier { get; }

            public M68kDecodedInstruction[] Instructions { get; }

            public uint CodeStart { get; }

            public int ByteLength { get; }

            public int InstructionCount => Instructions?.Length ?? 0;

            public bool HasInternalLoop { get; }

            public bool PureCpuBatchEligible { get; }

            public bool FastReadOnlyBatchEligible { get; }

            public uint[] ExternalHandoffTargets { get; }

            public bool WorkerGraphExpanded { get; }
        }

        private enum M68kJitCompileReason
        {
            HotRoot,
            Promotion,
            GraphHole,
            Handoff,
            BranchPressure
        }

        private enum M68kJitCompileKind
        {
            Trace,
            Promotion
        }

        private enum M68kAsyncJitQueueResult
        {
            Queued,
            DedupedPending,
            DedupedCompiling,
            Dropped
        }

        private readonly struct M68kJitCompileKey : IEquatable<M68kJitCompileKey>
        {
            public M68kJitCompileKey(uint root, V2TraceTier tier, M68kJitCompileKind kind)
            {
                Root = root;
                Tier = tier;
                Kind = kind;
            }

            public uint Root { get; }

            public V2TraceTier Tier { get; }

            public M68kJitCompileKind Kind { get; }

            public bool Equals(M68kJitCompileKey other)
                => Root == other.Root && Tier == other.Tier && Kind == other.Kind;

            public override bool Equals(object? obj)
                => obj is M68kJitCompileKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(Root, Tier, Kind);
        }

        private readonly struct M68kTraceCompilationPlan
        {
            public M68kTraceCompilationPlan(
                uint root,
                V2TraceTier requestedTier,
                M68kJitCompileReason reason,
                uint codeStart,
                int byteLength,
                uint startGeneration,
                uint endGeneration,
                ushort[] codeWords,
                M68kDecodedInstruction[] traceInstructions,
                bool pureCpuBatchEligible,
                int[] directInstructionPrefixes,
                int[] helperInstructionPrefixes,
                int[] directCpuInstructionPrefixes,
                int[] directMemoryInstructionPrefixes,
                int[] specializedHelperInstructionPrefixes,
                V2TracePlan v2Trace,
                bool v2BusGraphEnabled)
            {
                Root = root;
                RequestedTier = requestedTier;
                Reason = reason;
                CodeStart = codeStart;
                ByteLength = byteLength;
                StartGeneration = startGeneration;
                EndGeneration = endGeneration;
                CodeWords = codeWords;
                TraceInstructions = traceInstructions;
                PureCpuBatchEligible = pureCpuBatchEligible;
                DirectInstructionPrefixes = directInstructionPrefixes;
                HelperInstructionPrefixes = helperInstructionPrefixes;
                DirectCpuInstructionPrefixes = directCpuInstructionPrefixes;
                DirectMemoryInstructionPrefixes = directMemoryInstructionPrefixes;
                SpecializedHelperInstructionPrefixes = specializedHelperInstructionPrefixes;
                V2Trace = v2Trace;
                V2BusGraphEnabled = v2BusGraphEnabled;
            }

            public uint Root { get; }

            public V2TraceTier RequestedTier { get; }

            public M68kJitCompileReason Reason { get; }

            public uint CodeStart { get; }

            public int ByteLength { get; }

            public uint StartGeneration { get; }

            public uint EndGeneration { get; }

            public ushort[] CodeWords { get; }

            public M68kDecodedInstruction[] TraceInstructions { get; }

            public bool PureCpuBatchEligible { get; }

            public int[] DirectInstructionPrefixes { get; }

            public int[] HelperInstructionPrefixes { get; }

            public int[] DirectCpuInstructionPrefixes { get; }

            public int[] DirectMemoryInstructionPrefixes { get; }

            public int[] SpecializedHelperInstructionPrefixes { get; }

            public V2TracePlan V2Trace { get; }

            public bool V2BusGraphEnabled { get; }
        }

        private readonly struct M68kTraceCompilationOptions
        {
            public M68kTraceCompilationOptions(
                bool v2Enabled,
                bool v2Tier3Enabled,
                bool v2MemoryReadEnabled,
                bool v2BusAccessEnabled,
                bool v2FastReadEnabled,
                bool v2BusGraphEnabled,
                bool workerGraphExpansionEnabled)
            {
                V2Enabled = v2Enabled;
                V2Tier3Enabled = v2Tier3Enabled;
                V2MemoryReadEnabled = v2MemoryReadEnabled;
                V2BusAccessEnabled = v2BusAccessEnabled;
                V2FastReadEnabled = v2FastReadEnabled;
                V2BusGraphEnabled = v2BusGraphEnabled;
                WorkerGraphExpansionEnabled = workerGraphExpansionEnabled;
            }

            public bool V2Enabled { get; }

            public bool V2Tier3Enabled { get; }

            public bool V2MemoryReadEnabled { get; }

            public bool V2BusAccessEnabled { get; }

            public bool V2FastReadEnabled { get; }

            public bool V2BusGraphEnabled { get; }

            public bool WorkerGraphExpansionEnabled { get; }
        }

        private readonly struct M68kTraceCompilationInput
        {
            public M68kTraceCompilationInput(
                uint root,
                V2TraceTier tier,
                M68kJitCompileReason reason,
                M68kJitCodeSnapshot snapshot,
                M68kTraceCompilationOptions options)
            {
                Root = root;
                Tier = tier;
                Reason = reason;
                Snapshot = snapshot;
                Options = options;
            }

            public uint Root { get; }

            public V2TraceTier Tier { get; }

            public M68kJitCompileReason Reason { get; }

            public M68kJitCodeSnapshot Snapshot { get; }

            public M68kTraceCompilationOptions Options { get; }
        }

        private readonly struct M68kV2PromotionCompilationInput
        {
            public M68kV2PromotionCompilationInput(
                uint root,
                V2TraceTier tier,
                uint currentCodeStart,
                int currentByteLength,
                M68kJitCodeSnapshot snapshot,
                M68kTraceCompilationOptions options)
            {
                Root = root;
                Tier = tier;
                CurrentCodeStart = currentCodeStart;
                CurrentByteLength = currentByteLength;
                Snapshot = snapshot;
                Options = options;
            }

            public uint Root { get; }

            public V2TraceTier Tier { get; }

            public uint CurrentCodeStart { get; }

            public int CurrentByteLength { get; }

            public M68kJitCodeSnapshot Snapshot { get; }

            public M68kTraceCompilationOptions Options { get; }
        }

        private readonly struct M68kV2PromotionCompilationPlan
        {
            public M68kV2PromotionCompilationPlan(
                uint root,
                V2TraceTier tier,
                uint codeStart,
                int byteLength,
                uint startGeneration,
                uint endGeneration,
                ushort[] codeWords,
                V2TracePlan v2Trace,
                bool v2BusGraphEnabled)
            {
                Root = root;
                Tier = tier;
                CodeStart = codeStart;
                ByteLength = byteLength;
                StartGeneration = startGeneration;
                EndGeneration = endGeneration;
                CodeWords = codeWords;
                V2Trace = v2Trace;
                V2BusGraphEnabled = v2BusGraphEnabled;
            }

            public uint Root { get; }

            public V2TraceTier Tier { get; }

            public uint CodeStart { get; }

            public int ByteLength { get; }

            public uint StartGeneration { get; }

            public uint EndGeneration { get; }

            public ushort[] CodeWords { get; }

            public V2TracePlan V2Trace { get; }

            public bool V2BusGraphEnabled { get; }
        }

        private readonly struct M68kTraceCompileSuccess
        {
            public M68kTraceCompileSuccess(M68kTraceCompilationPlan plan, TraceEntry trace)
            {
                Plan = plan;
                Trace = trace;
            }

            public M68kTraceCompilationPlan Plan { get; }

            public TraceEntry Trace { get; }
        }

        private readonly struct M68kPromotionCompileSuccess
        {
            public M68kPromotionCompileSuccess(M68kV2PromotionCompilationPlan plan, V2TraceCompilation compiled)
            {
                Plan = plan;
                Compiled = compiled;
            }

            public M68kV2PromotionCompilationPlan Plan { get; }

            public V2TraceCompilation Compiled { get; }
        }

        private readonly struct M68kJitCompileRequest
        {
            private M68kJitCompileRequest(
                int sequence,
                int epoch,
                int priority,
                bool branchPressurePromotion,
                M68kJitCompileKind kind,
                M68kJitCompileKey key,
                M68kTraceCompilationInput trace,
                M68kV2PromotionCompilationInput promotion)
            {
                Sequence = sequence;
                Epoch = epoch;
                Priority = priority;
                BranchPressurePromotion = branchPressurePromotion;
                Kind = kind;
                Key = key;
                Trace = trace;
                Promotion = promotion;
            }

            public static M68kJitCompileRequest ForTrace(
                int sequence,
                int epoch,
                int priority,
                bool branchPressurePromotion,
                M68kTraceCompilationInput trace)
                => new M68kJitCompileRequest(
                    sequence,
                    epoch,
                    priority,
                    branchPressurePromotion,
                    M68kJitCompileKind.Trace,
                    new M68kJitCompileKey(trace.Root, trace.Tier, M68kJitCompileKind.Trace),
                    trace,
                    default);

            public static M68kJitCompileRequest ForPromotion(
                int sequence,
                int epoch,
                int priority,
                bool branchPressurePromotion,
                M68kV2PromotionCompilationInput promotion)
                => new M68kJitCompileRequest(
                    sequence,
                    epoch,
                    priority,
                    branchPressurePromotion,
                    M68kJitCompileKind.Promotion,
                    new M68kJitCompileKey(promotion.Root, promotion.Tier, M68kJitCompileKind.Promotion),
                    default,
                    promotion);

            public int Sequence { get; }

            public int Epoch { get; }

            public int Priority { get; private init; }

            public bool BranchPressurePromotion { get; }

            public M68kJitCompileKind Kind { get; }

            public M68kJitCompileKey Key { get; }

            public M68kTraceCompilationInput Trace { get; }

            public M68kV2PromotionCompilationInput Promotion { get; }

            public M68kJitCompileRequest WithPriority(int priority)
                => new M68kJitCompileRequest(
                    Sequence,
                    Epoch,
                    priority,
                    BranchPressurePromotion,
                    Kind,
                    Key,
                    Trace,
                    Promotion);

            public M68kJitCompileResult Compile()
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    if (Kind == M68kJitCompileKind.Trace)
                    {
                        if (!TryCreateTraceCompilationPlanFromSnapshot(Trace, out var plan))
                        {
                            return M68kJitCompileResult.Failed(Epoch, BranchPressurePromotion, stopwatch, Key, Trace.Reason);
                        }

                        var trace = CompileTraceFromPlan(plan);
                        PrepareTraceEntry(trace);
                        return M68kJitCompileResult.ForTrace(
                            Epoch,
                            BranchPressurePromotion,
                            stopwatch,
                            new M68kTraceCompileSuccess(plan, trace));
                    }

                    if (!TryCreateV2PromotionCompilationPlanFromSnapshot(Promotion, out var promotionPlan))
                    {
                        return M68kJitCompileResult.Failed(Epoch, BranchPressurePromotion, stopwatch, Key, M68kJitCompileReason.Promotion);
                    }

                    var compiled = CompileV2FromPlan(promotionPlan.Root, promotionPlan.V2Trace, promotionPlan.V2BusGraphEnabled);
                    PrepareCompiledTrace(compiled.Compiled!);
                    return M68kJitCompileResult.ForPromotion(
                        Epoch,
                        BranchPressurePromotion,
                        stopwatch,
                        new M68kPromotionCompileSuccess(
                            promotionPlan,
                            compiled));
                }
                catch
                {
                    var reason = Kind == M68kJitCompileKind.Trace ? Trace.Reason : M68kJitCompileReason.Promotion;
                    return M68kJitCompileResult.Failed(Epoch, BranchPressurePromotion, stopwatch, Key, reason);
                }
            }
        }

        private readonly struct M68kJitCompileResult
        {
            private M68kJitCompileResult(
                int epoch,
                bool branchPressurePromotion,
                bool succeeded,
                long compileMilliseconds,
                M68kJitCompileKey key,
                M68kJitCompileReason reason,
                M68kTraceCompileSuccess? trace,
                M68kPromotionCompileSuccess? promotion)
            {
                Epoch = epoch;
                BranchPressurePromotion = branchPressurePromotion;
                Succeeded = succeeded;
                CompileMilliseconds = compileMilliseconds;
                Key = key;
                Reason = reason;
                Trace = trace;
                Promotion = promotion;
            }

            public static M68kJitCompileResult ForTrace(
                int epoch,
                bool branchPressurePromotion,
                Stopwatch stopwatch,
                M68kTraceCompileSuccess trace)
                => new M68kJitCompileResult(
                    epoch,
                    branchPressurePromotion,
                    succeeded: true,
                    stopwatch.ElapsedMilliseconds,
                    new M68kJitCompileKey(trace.Plan.Root, trace.Plan.RequestedTier, M68kJitCompileKind.Trace),
                    trace.Plan.Reason,
                    trace,
                    null);

            public static M68kJitCompileResult ForPromotion(
                int epoch,
                bool branchPressurePromotion,
                Stopwatch stopwatch,
                M68kPromotionCompileSuccess promotion)
                => new M68kJitCompileResult(
                    epoch,
                    branchPressurePromotion,
                    succeeded: true,
                    stopwatch.ElapsedMilliseconds,
                    new M68kJitCompileKey(promotion.Plan.Root, promotion.Plan.Tier, M68kJitCompileKind.Promotion),
                    M68kJitCompileReason.Promotion,
                    null,
                    promotion);

            public static M68kJitCompileResult Failed(
                int epoch,
                bool branchPressurePromotion,
                Stopwatch stopwatch,
                M68kJitCompileKey key,
                M68kJitCompileReason reason)
                => new M68kJitCompileResult(
                    epoch,
                    branchPressurePromotion,
                    succeeded: false,
                    stopwatch.ElapsedMilliseconds,
                    key,
                    reason,
                    null,
                    null);

            public int Epoch { get; }

            public bool BranchPressurePromotion { get; }

            public bool Succeeded { get; }

            public long CompileMilliseconds { get; }

            public M68kJitCompileKey Key { get; }

            public M68kJitCompileReason Reason { get; }

            public M68kTraceCompileSuccess? Trace { get; }

            public M68kPromotionCompileSuccess? Promotion { get; }
        }

        private sealed class M68kAsyncJitCompiler : IDisposable
        {
            private readonly object _gate = new object();
            private readonly int _capacity;
            private readonly List<M68kJitCompileRequest> _pending = new List<M68kJitCompileRequest>();
            private readonly HashSet<M68kJitCompileKey> _compiling = new HashSet<M68kJitCompileKey>();
            private readonly ConcurrentQueue<M68kJitCompileResult> _completed = new ConcurrentQueue<M68kJitCompileResult>();
            private readonly Thread[] _workers;
            private bool _stopping;
            private int _maxQueueDepth;

            public M68kAsyncJitCompiler(int workerCount, int capacity)
            {
                _capacity = Math.Max(1, capacity);
                _workers = new Thread[Math.Max(1, workerCount)];
                for (var i = 0; i < _workers.Length; i++)
                {
                    var worker = new Thread(WorkerLoop)
                    {
                        IsBackground = true,
                        Name = "M68k async JIT compiler " + i
                    };
                    _workers[i] = worker;
                    worker.Start();
                }
            }

            public M68kAsyncJitQueueResult Enqueue(M68kJitCompileRequest request, out int queueDepth)
            {
                lock (_gate)
                {
                    queueDepth = _pending.Count;
                    if (_stopping)
                    {
                        return M68kAsyncJitQueueResult.Dropped;
                    }

                    if (_compiling.Contains(request.Key))
                    {
                        return M68kAsyncJitQueueResult.DedupedCompiling;
                    }

                    for (var i = 0; i < _pending.Count; i++)
                    {
                        if (_pending[i].Key.Equals(request.Key))
                        {
                            if (request.Priority > _pending[i].Priority)
                            {
                                _pending[i] = _pending[i].WithPriority(request.Priority);
                            }

                            return M68kAsyncJitQueueResult.DedupedPending;
                        }
                    }

                    if (_pending.Count >= _capacity)
                    {
                        var lowestIndex = 0;
                        var lowestPriority = _pending[0].Priority;
                        for (var i = 1; i < _pending.Count; i++)
                        {
                            if (_pending[i].Priority < lowestPriority)
                            {
                                lowestIndex = i;
                                lowestPriority = _pending[i].Priority;
                            }
                        }

                        if (request.Priority <= lowestPriority)
                        {
                            return M68kAsyncJitQueueResult.Dropped;
                        }

                        _pending.RemoveAt(lowestIndex);
                    }

                    _pending.Add(request);
                    queueDepth = _pending.Count;
                    if (queueDepth > _maxQueueDepth)
                    {
                        _maxQueueDepth = queueDepth;
                    }

                    Monitor.Pulse(_gate);
                    return M68kAsyncJitQueueResult.Queued;
                }
            }

            public bool TryUpdatePriority(
                M68kJitCompileKey key,
                int priority,
                out M68kAsyncJitQueueResult result,
                out int queueDepth)
            {
                lock (_gate)
                {
                    queueDepth = _pending.Count;
                    result = M68kAsyncJitQueueResult.Dropped;
                    if (_stopping)
                    {
                        return false;
                    }

                    if (_compiling.Contains(key))
                    {
                        result = M68kAsyncJitQueueResult.DedupedCompiling;
                        return true;
                    }

                    for (var i = 0; i < _pending.Count; i++)
                    {
                        if (!_pending[i].Key.Equals(key))
                        {
                            continue;
                        }

                        if (priority > _pending[i].Priority)
                        {
                            _pending[i] = _pending[i].WithPriority(priority);
                        }

                        result = M68kAsyncJitQueueResult.DedupedPending;
                        return true;
                    }

                    return false;
                }
            }

            public bool TryDequeueCompleted(out M68kJitCompileResult result)
                => _completed.TryDequeue(out result);

            public void Clear()
            {
                lock (_gate)
                {
                    _pending.Clear();
                    Monitor.PulseAll(_gate);
                }

                while (_completed.TryDequeue(out _))
                {
                }
            }

            public int MaxQueueDepth => Volatile.Read(ref _maxQueueDepth);

            public void Dispose()
            {
                lock (_gate)
                {
                    _stopping = true;
                    _pending.Clear();
                    Monitor.PulseAll(_gate);
                }

                for (var i = 0; i < _workers.Length; i++)
                {
                    var worker = _workers[i];
                    if (worker.IsAlive)
                    {
                        worker.Join(TimeSpan.FromSeconds(1));
                    }
                }

                while (_completed.TryDequeue(out _))
                {
                }
            }

            private void WorkerLoop()
            {
                while (true)
                {
                    M68kJitCompileRequest request;
                    lock (_gate)
                    {
                        while (_pending.Count == 0 && !_stopping)
                        {
                            Monitor.Wait(_gate);
                        }

                        if (_pending.Count == 0 && _stopping)
                        {
                            return;
                        }

                        var selectedIndex = 0;
                        var selectedPriority = _pending[0].Priority;
                        for (var i = 1; i < _pending.Count; i++)
                        {
                            if (_pending[i].Priority > selectedPriority)
                            {
                                selectedIndex = i;
                                selectedPriority = _pending[i].Priority;
                            }
                        }

                        request = _pending[selectedIndex];
                        _pending.RemoveAt(selectedIndex);
                        _compiling.Add(request.Key);
                    }

                    var result = request.Compile();
                    var shouldPublish = true;
                    lock (_gate)
                    {
                        _compiling.Remove(request.Key);
                        shouldPublish = !_stopping;
                    }

                    if (shouldPublish)
                    {
                        _completed.Enqueue(result);
                    }
                }
            }
        }

        private readonly struct TraceEntry
        {
            public TraceEntry(
                uint root,
                uint codeStart,
                int byteLength,
                uint startGeneration,
                uint endGeneration,
                ushort[] codeWords,
                int[] directInstructionPrefixes,
                int[] helperInstructionPrefixes,
                int[] directCpuInstructionPrefixes,
                int[] directMemoryInstructionPrefixes,
                int[] specializedHelperInstructionPrefixes,
                CompiledTrace? compiled,
                bool pureCpuBatchEligible,
                CompiledTrace? pureCompiled,
                CompiledTrace? v2Compiled,
                V2TraceTier v2Tier,
                int v2InstructionCount,
                bool v2HasInternalLoop,
                bool v2PureCpuBatchEligible)
            {
                Root = root;
                CodeStart = codeStart;
                ByteLength = byteLength;
                StartGeneration = startGeneration;
                EndGeneration = endGeneration;
                CodeWords = codeWords;
                DirectInstructionPrefixes = directInstructionPrefixes;
                HelperInstructionPrefixes = helperInstructionPrefixes;
                DirectCpuInstructionPrefixes = directCpuInstructionPrefixes;
                DirectMemoryInstructionPrefixes = directMemoryInstructionPrefixes;
                SpecializedHelperInstructionPrefixes = specializedHelperInstructionPrefixes;
                Compiled = compiled;
                PureCpuBatchEligible = pureCpuBatchEligible;
                PureCompiled = pureCompiled;
                V2Compiled = v2Compiled;
                V2Tier = v2Tier;
                V2InstructionCount = v2InstructionCount;
                V2HasInternalLoop = v2HasInternalLoop;
                V2PureCpuBatchEligible = v2PureCpuBatchEligible;
            }

            public uint Root { get; }

            public uint CodeStart { get; }

            public int ByteLength { get; }

            public uint StartGeneration { get; }

            public uint EndGeneration { get; }

            public ushort[] CodeWords { get; }

            public int[] DirectInstructionPrefixes { get; }

            public int[] HelperInstructionPrefixes { get; }

            public int[] DirectCpuInstructionPrefixes { get; }

            public int[] DirectMemoryInstructionPrefixes { get; }

            public int[] SpecializedHelperInstructionPrefixes { get; }

            public CompiledTrace? Compiled { get; }

            public bool PureCpuBatchEligible { get; }

            public CompiledTrace? PureCompiled { get; }

            public CompiledTrace? V2Compiled { get; }

            public V2TraceTier V2Tier { get; }

            public int V2InstructionCount { get; }

            public bool V2HasInternalLoop { get; }

            public bool V2PureCpuBatchEligible { get; }

            public TraceEntry WithGenerations(uint startGeneration, uint endGeneration)
                => new TraceEntry(
                    Root,
                    CodeStart,
                    ByteLength,
                    startGeneration,
                    endGeneration,
                    CodeWords,
                    DirectInstructionPrefixes,
                    HelperInstructionPrefixes,
                    DirectCpuInstructionPrefixes,
                    DirectMemoryInstructionPrefixes,
                    SpecializedHelperInstructionPrefixes,
                    Compiled,
                    PureCpuBatchEligible,
                    PureCompiled,
                    V2Compiled,
                    V2Tier,
                    V2InstructionCount,
                    V2HasInternalLoop,
                    V2PureCpuBatchEligible);

            public TraceEntry WithV2(
                uint codeStart,
                int byteLength,
                uint startGeneration,
                uint endGeneration,
                ushort[] codeWords,
                CompiledTrace compiled,
                V2TraceTier tier,
                int instructionCount,
                bool hasInternalLoop,
                bool pureCpuBatchEligible)
                => new TraceEntry(
                    Root,
                    codeStart,
                    byteLength,
                    startGeneration,
                    endGeneration,
                    codeWords,
                    DirectInstructionPrefixes,
                    HelperInstructionPrefixes,
                    DirectCpuInstructionPrefixes,
                    DirectMemoryInstructionPrefixes,
                    SpecializedHelperInstructionPrefixes,
                    Compiled,
                    PureCpuBatchEligible,
                    PureCompiled,
                    compiled,
                    tier,
                    instructionCount,
                    hasInternalLoop,
                    pureCpuBatchEligible);
        }

        private sealed class NoOpBoundary : IM68kInstructionBoundary
        {
            public static NoOpBoundary Instance { get; } = new NoOpBoundary();

            public bool BeforeInstruction()
            {
                return true;
            }

            public void AfterInstruction(long previousCycle, long currentCycle)
            {
                _ = previousCycle;
                _ = currentCycle;
            }
        }
    }

    internal struct M68kJitCounters
    {
        public long TraceHits { get; set; }

        public long V2TraceHits { get; set; }

        public long V2RejectedCandidates { get; set; }

        public long V2RejectedChipRam { get; set; }

        public long V2RejectedHostTrap { get; set; }

        public long V2RejectedDecode { get; set; }

        public long V2RejectedUnsupportedOperation { get; set; }

        public long V2RejectedUnsupportedEa { get; set; }

        public long V2RejectedBudget { get; set; }

        public long V2RejectedEmpty { get; set; }

        public long V2DisabledGraphHoleRoots { get; set; }

        public long V2DisabledBranchExitRoots { get; set; }

        public long V2BranchPressureLimitedRoots { get; set; }

        public long V2DisabledEntryMismatchRoots { get; set; }

        public long V2GraphHoleTargetCompiles { get; set; }

        public long V2TraceHandoffAttempts { get; set; }

        public long V2TraceHandoffExecutions { get; set; }

        public long V2TraceHandoffInstructions { get; set; }

        public long V2TraceHandoffFailures { get; set; }

        public long V2TraceHandoffQueuedNotReady { get; set; }

        public long V2BusAccessBatchExecutions { get; set; }

        public long V2BusAccessBatchInstructions { get; set; }

        public long V2BusAccessBatchBoundaryCallsSaved { get; set; }

        public string? V2BusAccessBatchLengthHistogram { get; set; }

        public string? V2BusAccessBatchWakeSourceTop { get; set; }

        public string? V2UnsupportedOperationTop { get; set; }

        public string? V2UnsupportedEaTop { get; set; }

        public string? V2GraphHoleTop { get; set; }

        public string? V2TraceHandoffBlockTop { get; set; }

        public string? V2TraceHandoffFailureTop { get; set; }

        public string? V2BranchPressureLimitTop { get; set; }

        public string? V2BranchPressureTargetStateTop { get; set; }

        public long V2Tier0CompiledTraces { get; set; }

        public long V2Tier1CompiledTraces { get; set; }

        public long V2Tier2CompiledTraces { get; set; }

        public long V2Tier3CompiledTraces { get; set; }

        public long V2TierPromotions { get; set; }

        public long V2TierPressurePromotions { get; set; }

        public long V2WorkerExpandedGraphs { get; set; }

        public long V2WorkerExpandedGraphInstructions { get; set; }

        public long V2WorkerExpandedGraphBytes { get; set; }

        public long AsyncRequestsQueued { get; set; }

        public long AsyncRequestsDeduped { get; set; }

        public long AsyncRequestsDropped { get; set; }

        public long AsyncPriorityBumps { get; set; }

        public long AsyncSnapshotCapturesAvoided { get; set; }

        public long AsyncWorkerCompilesStarted { get; set; }

        public long AsyncWorkerCompilesCompleted { get; set; }

        public long AsyncWorkerCompilesFailed { get; set; }

        public long AsyncCompletedInstalled { get; set; }

        public long AsyncCompletedDiscardedStale { get; set; }

        public long AsyncCompletedDiscardedSuperseded { get; set; }

        public long AsyncTier0Installs { get; set; }

        public long AsyncTier1Installs { get; set; }

        public long AsyncTier2Installs { get; set; }

        public long AsyncTier3Installs { get; set; }

        public long AsyncMaxQueueDepth { get; set; }

        public long AsyncWorkerCompileMilliseconds { get; set; }

        public long CompiledTraces { get; set; }

        public long SideExits { get; set; }

        public long V2SideExits { get; set; }

        public long V2SideExitEntryMismatch { get; set; }

        public long V2SideExitOutOfBlockBranch { get; set; }

        public long V2SideExitBeforeGraph { get; set; }

        public long V2SideExitBeyondGraph { get; set; }

        public long V2SideExitChipRam { get; set; }

        public long V2SideExitHostTrap { get; set; }

        public long V2SideExitGraphHole { get; set; }

        public long V2SideExitConditionalFallthrough { get; set; }

        public long V2LazyWritebacks { get; set; }

        public long V2FlagMaterializations { get; set; }

        public long V2BranchPendingFlagChecks { get; set; }

        public long V2BranchStatusFlagChecks { get; set; }

        public long V2ZeroWaitReadRealFast { get; set; }

        public long V2ZeroWaitReadRom { get; set; }

        public long V2ZeroWaitReadOverlay { get; set; }

        public long V2ZeroWaitWriteRealFast { get; set; }

        public long V2ZeroWaitReadSlow { get; set; }

        public long V2ZeroWaitWriteSlow { get; set; }

        public long Invalidations { get; set; }

        public long FallbackInstructions { get; set; }

        public long BlacklistCount { get; set; }

        public long UnsupportedOpcode { get; set; }

        public long UnsupportedEa { get; set; }

        public long HostTrapBailouts { get; set; }

        public long SystemInstructionBailouts { get; set; }

        public long ExceptionInstructionBailouts { get; set; }

        public long GenerationMismatches { get; set; }

        public long BoundarySideExits { get; set; }

        public long SelfModifiedCodeExits { get; set; }

        public long StoppedFastForwards { get; set; }

        public long StoppedFastForwardCycles { get; set; }

        public long DirectIlInstructions { get; set; }

        public long HelperIlInstructions { get; set; }

        public long GenerationGuardExits { get; set; }

        public long PureTraceBatchExecutions { get; set; }

        public long PureTraceBatchInstructions { get; set; }

        public long PureTraceBatchBoundaryCallsSaved { get; set; }

        public long PureTraceBatchSideExits { get; set; }

        public string? PureTraceBatchLengthHistogram { get; set; }

        public string? PureTraceBatchWakeSourceTop { get; set; }

        public long DirectCpuIlInstructions { get; set; }

        public long DirectMemoryIlInstructions { get; set; }

        public long SpecializedHelperIlInstructions { get; set; }
    }
}
