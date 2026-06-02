using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

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
        private const int V2Tier1PromotionHits = 4096;
        private const int V2Tier2PromotionHits = 16384;
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
        private readonly M68kInstructionFrequencyMatrix _instructionFrequency = new M68kInstructionFrequencyMatrix();
        private readonly Dictionary<uint, TraceEntry> _traces = new Dictionary<uint, TraceEntry>();
        private readonly Dictionary<uint, HashSet<uint>> _traceRootsByPage = new Dictionary<uint, HashSet<uint>>();
        private readonly Dictionary<uint, int> _hotCounters = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _blacklist = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _v2TraceHits = new Dictionary<uint, int>();
        private static readonly MethodInfo CheckV2ConditionMethod =
            typeof(M68kJitCore).GetMethod(nameof(CheckV2Condition), BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(CheckV2Condition));
        private M68kJitCounters _counters;
        private long _compiledInstructionPreviousCycle;

        public M68kJitCore(IM68kBus bus)
            : this(bus, IsV2EnabledByDefault())
        {
        }

        internal M68kJitCore(IM68kBus bus, bool enableV2)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _amigaBus = bus as AmigaBus;
            _v2Enabled = enableV2;
            State = new M68kCpuState();
            _fallback = new M68kInterpreter(bus, State, _instructionFrequency);
            if (_amigaBus != null)
            {
                _amigaBus.JitEligibleMemoryWritten += InvalidateWrittenCodeRange;
            }
        }

        public M68kCpuState State { get; }

        public M68kJitCounters Counters => _counters;

        public bool InstructionFrequencyEnabled
        {
            get => _instructionFrequency.Enabled;
            set => _instructionFrequency.Enabled = value;
        }

        public M68kInstructionFrequencySnapshot CaptureInstructionFrequency()
            => _instructionFrequency.CaptureSnapshot();

        public void ResetInstructionFrequency()
            => _instructionFrequency.Reset();

        private static bool IsV2EnabledByDefault()
        {
            var value = Environment.GetEnvironmentVariable("COPPER_M68K_JIT_V2");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
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
            var instructions = 0;
            while (!State.Halted &&
                instructions < maxInstructions &&
                (!targetCycle.HasValue || State.Cycles < targetCycle.Value))
            {
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

        private int TryExecuteTrace(int maxInstructions, long? targetCycle, IM68kInstructionBoundary boundary)
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

            if (_amigaBus != null &&
                !_amigaBus.CodeRangeGenerationMatches(
                    trace.Root,
                    trace.ByteLength,
                    trace.StartGeneration,
                    trace.EndGeneration))
            {
                if (TraceCodeWordsMatch(trace))
                {
                    trace = RefreshTraceGenerations(trace);
                    _traces[pc] = trace;
                }
                else
                {
                    RemoveTrace(pc);
                    _counters.GenerationMismatches++;
                    _counters.GenerationGuardExits++;
                    return 0;
                }
            }

            var v2BatchExecuted = TryExecuteV2TraceBatch(trace, maxInstructions, targetCycle, boundary);
            if (v2BatchExecuted.HasValue)
            {
                return v2BatchExecuted.Value;
            }

            var pureBatchExecuted = TryExecutePureTraceBatch(trace, maxInstructions, targetCycle, boundary);
            if (pureBatchExecuted.HasValue)
            {
                return pureBatchExecuted.Value;
            }

            _counters.TraceHits++;
            var executed = trace.Compiled(this, maxInstructions, targetCycle ?? long.MaxValue, boundary);
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

        private int? TryExecuteV2TraceBatch(
            TraceEntry trace,
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary)
        {
            if (!targetCycle.HasValue ||
                maxInstructions <= 1 ||
                trace.V2Compiled == null ||
                _instructionFrequency.Enabled ||
                (_amigaBus != null && _amigaBus.HasHostCallback(trace.Root)) ||
                boundary is not IM68kPureCpuTraceBatchBoundary batchBoundary)
            {
                return null;
            }

            var previousCycle = State.Cycles;
            if (!batchBoundary.TryBeginPureCpuTraceBatch(State, targetCycle.Value, out var batchTargetCycle))
            {
                return null;
            }

            _counters.TraceHits++;
            _counters.V2TraceHits++;
            var executed = trace.V2Compiled(this, maxInstructions, batchTargetCycle, boundary);
            if (executed == 0)
            {
                _counters.SideExits++;
                _counters.V2SideExits++;
                _counters.V2SideExitEntryMismatch++;
                _counters.PureTraceBatchSideExits++;
                return 0;
            }

            batchBoundary.AfterPureCpuTraceBatch(previousCycle, State.Cycles, executed);
            _counters.DirectIlInstructions += executed;
            _counters.DirectCpuIlInstructions += executed;
            _counters.PureTraceBatchExecutions++;
            _counters.PureTraceBatchInstructions += executed;
            _counters.PureTraceBatchBoundaryCallsSaved += Math.Max(0, executed - 1);
            MaybePromoteV2Trace(trace, executed);
            return executed;
        }

        private int? TryExecutePureTraceBatch(
            TraceEntry trace,
            int maxInstructions,
            long? targetCycle,
            IM68kInstructionBoundary boundary)
        {
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
            if (_amigaBus == null || _traces.ContainsKey(pc) || _amigaBus.IsChipRamAddress(pc))
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
        {
            trace = default;
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

                if (!M68kDecoder.TryDecode(_amigaBus, pc, out var instruction, out var reason))
                {
                    CountBailout(reason);
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

            var traceInstructions = instructions[..count];
            var compiled = Compile(root, traceInstructions, emitBoundaryCalls: true);
            var pureCpuBatchEligible = IsPureCpuBatchEligible(traceInstructions);
            var pureCompiled = pureCpuBatchEligible
                ? Compile(root, traceInstructions, emitBoundaryCalls: false)
                : null;
            var v2Trace = _v2Enabled
                ? TryCompileV2Trace(root, V2TraceTier.Tier0, out var compiledV2Trace) ? compiledV2Trace : default
                : default;
            var guardByteLength = Math.Max(byteCount, v2Trace.ByteLength);
            var codeWords = CaptureTraceWords(root, guardByteLength);
            CaptureTraceInstructionKindPrefixes(
                traceInstructions,
                out var directInstructionPrefixes,
                out var helperInstructionPrefixes,
                out var directCpuInstructionPrefixes,
                out var directMemoryInstructionPrefixes,
                out var specializedHelperInstructionPrefixes);
            var startGeneration = _amigaBus.GetCodePageGeneration(root);
            var endGeneration = _amigaBus.GetCodePageGeneration(Normalize(root + (uint)guardByteLength - 1u));
            trace = new TraceEntry(
                root,
                guardByteLength,
                startGeneration,
                endGeneration,
                codeWords,
                directInstructionPrefixes,
                helperInstructionPrefixes,
                directCpuInstructionPrefixes,
                directMemoryInstructionPrefixes,
                specializedHelperInstructionPrefixes,
                compiled,
                pureCpuBatchEligible,
                pureCompiled,
                v2Trace.Compiled,
                v2Trace.Tier,
                v2Trace.InstructionCount,
                v2Trace.HasInternalLoop);
            return true;
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

            var address = trace.Root;
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
                _amigaBus.GetCodePageGeneration(trace.Root),
                _amigaBus.GetCodePageGeneration(Normalize(trace.Root + (uint)trace.ByteLength - 1u)));
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
            if (!_v2Enabled)
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
                out var byteLength,
                out var hasInternalLoop,
                out var rejectionReason))
            {
                RecordV2Rejection(rejectionReason);
                return false;
            }

            var compiled = CompileV2(root, tier, instructions);
            trace = new V2TraceCompilation(compiled, tier, instructions, byteLength, hasInternalLoop);
            RecordV2CompiledTrace(tier);
            return true;
        }

        private bool TryCollectV2Instructions(
            uint root,
            V2TraceTier tier,
            out M68kDecodedInstruction[] instructions,
            out int byteLength,
            out bool hasInternalLoop,
            out V2TraceRejectionReason rejectionReason)
        {
            instructions = Array.Empty<M68kDecodedInstruction>();
            byteLength = 0;
            hasInternalLoop = false;
            rejectionReason = V2TraceRejectionReason.Empty;
            if (_amigaBus == null)
            {
                rejectionReason = V2TraceRejectionReason.NoBus;
                return false;
            }

            var (maxInstructions, maxBytes) = GetV2TierLimits(tier);
            var collected = new Dictionary<uint, M68kDecodedInstruction>();
            var queued = new HashSet<uint>();
            var work = new Queue<uint>();
            var maxEnd = root;
            EnqueueV2GraphAddress(root, root, maxBytes, work, queued);
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

                    if (_amigaBus.HasHostCallback(pc))
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

                    var endRelative = Normalize(instruction.ProgramCounter + (uint)instruction.Length - root);
                    if (endRelative > maxBytes)
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
                            return false;
                        }

                        break;
                    }

                    collected.Add(pc, instruction);
                    var instructionEnd = Normalize(instruction.ProgramCounter + (uint)instruction.Length);
                    if (Normalize(instructionEnd - root) > Normalize(maxEnd - root))
                    {
                        maxEnd = instructionEnd;
                    }

                    if (IsV2BranchInstruction(instruction))
                    {
                        var target = GetBranchTarget(instruction);
                        if (IsV2GraphAddressInBudget(root, target, maxBytes))
                        {
                            hasInternalLoop |= target <= instruction.ProgramCounter;
                            EnqueueV2GraphAddress(target, root, maxBytes, work, queued);
                        }

                        if (instruction.Operation is M68kJitOperation.Bcc or M68kJitOperation.Dbcc)
                        {
                            var fallthrough = Normalize(instruction.ProgramCounter + (uint)instruction.Length);
                            EnqueueV2GraphAddress(fallthrough, root, maxBytes, work, queued);
                        }

                        break;
                    }

                    pc = Normalize(pc + (uint)instruction.Length);
                    if (!IsV2GraphAddressInBudget(root, pc, maxBytes))
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

            byteLength = (int)Normalize(maxEnd - root);
            return true;
        }

        private void MaybePromoteV2Trace(TraceEntry trace, int executed)
        {
            if (!_v2Enabled || trace.V2Compiled == null || !trace.V2HasInternalLoop || executed <= 0)
            {
                return;
            }

            _v2TraceHits.TryGetValue(trace.Root, out var hits);
            hits++;
            _v2TraceHits[trace.Root] = hits;
            var nextTier = trace.V2Tier switch
            {
                V2TraceTier.Tier0 when hits >= V2Tier1PromotionHits => V2TraceTier.Tier1,
                V2TraceTier.Tier1 when hits >= V2Tier2PromotionHits => V2TraceTier.Tier2,
                _ => V2TraceTier.None
            };
            if (nextTier == V2TraceTier.None ||
                !TryCompileV2Trace(trace.Root, nextTier, out var promoted))
            {
                return;
            }

            var byteLength = Math.Max(trace.ByteLength, promoted.ByteLength);
            var codeWords = promoted.ByteLength >= trace.ByteLength
                ? CaptureTraceWords(trace.Root, byteLength)
                : trace.CodeWords;
            var startGeneration = _amigaBus?.GetCodePageGeneration(trace.Root) ?? trace.StartGeneration;
            var endGeneration = _amigaBus?.GetCodePageGeneration(Normalize(trace.Root + (uint)byteLength - 1u)) ??
                trace.EndGeneration;
            var updated = trace.WithV2(
                byteLength,
                startGeneration,
                endGeneration,
                codeWords,
                promoted.Compiled!,
                promoted.Tier,
                promoted.InstructionCount,
                promoted.HasInternalLoop);
            AddTrace(updated);
            _v2TraceHits[trace.Root] = hits;
            _counters.V2TierPromotions++;
        }

        private static (int Instructions, int Bytes) GetV2TierLimits(V2TraceTier tier)
            => tier switch
            {
                V2TraceTier.Tier2 => (V2Tier2TraceInstructions, V2Tier2TraceBytes),
                V2TraceTier.Tier1 => (V2Tier1TraceInstructions, V2Tier1TraceBytes),
                _ => (V2Tier0TraceInstructions, V2Tier0TraceBytes)
            };

        private static void EnqueueV2GraphAddress(
            uint address,
            uint root,
            int maxBytes,
            Queue<uint> work,
            HashSet<uint> queued)
        {
            if (!IsV2GraphAddressInBudget(root, address, maxBytes) || !queued.Add(address))
            {
                return;
            }

            work.Enqueue(address);
        }

        private static bool IsV2GraphAddressInBudget(uint root, uint address, int maxBytes)
        {
            var relative = Normalize(address - root);
            return relative < maxBytes;
        }

        private static List<M68kDecodedInstruction> SortV2GraphInstructions(
            uint root,
            IEnumerable<M68kDecodedInstruction> instructions)
        {
            var sorted = new List<M68kDecodedInstruction>(instructions);
            sorted.Sort((left, right) => Normalize(left.ProgramCounter - root).CompareTo(Normalize(right.ProgramCounter - root)));
            return sorted;
        }

        private static bool IsV2Instruction(M68kDecodedInstruction instruction, out V2TraceRejectionReason rejectionReason)
        {
            rejectionReason = V2TraceRejectionReason.None;
            var supported = instruction.Operation switch
            {
                M68kJitOperation.Nop => true,
                M68kJitOperation.Moveq => true,
                M68kJitOperation.Move => instruction.Source.Kind == M68kJitEaKind.DataRegister &&
                    instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Addq or M68kJitOperation.Subq => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Tst => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Cmpi => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Cmp => instruction.Source.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.And or M68kJitOperation.Or => instruction.Source.Kind == M68kJitEaKind.DataRegister &&
                    instruction.Variant == 0,
                M68kJitOperation.Eor => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.Not or M68kJitOperation.Neg => instruction.Destination.Kind == M68kJitEaKind.DataRegister,
                M68kJitOperation.ExtWord or M68kJitOperation.ExtLong or M68kJitOperation.Swap or M68kJitOperation.Exg => true,
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
                M68kJitOperation.Addq or
                M68kJitOperation.Subq or
                M68kJitOperation.Tst or
                M68kJitOperation.Cmpi or
                M68kJitOperation.Cmp or
                M68kJitOperation.And or
                M68kJitOperation.Or or
                M68kJitOperation.Eor or
                M68kJitOperation.Not or
                M68kJitOperation.Neg => V2TraceRejectionReason.UnsupportedEa,
                _ => V2TraceRejectionReason.UnsupportedOperation
            };
            return false;
        }

        private static bool IsV2BranchInstruction(M68kDecodedInstruction instruction)
            => instruction.Operation is M68kJitOperation.Bra or M68kJitOperation.Bcc or M68kJitOperation.Dbcc;

        private static CompiledTrace CompileV2(
            uint root,
            V2TraceTier tier,
            M68kDecodedInstruction[] instructions)
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
            context.EmitLoadState(root, returnZero);

            il.Emit(OpCodes.Br, labels[0]);
            for (var i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                il.MarkLabel(labels[i]);
                context.EmitCanContinue(exit);
                context.EmitStartInstruction(instruction);
                EmitV2Instruction(il, context, instruction, labels, addressToIndex, exit);
                if (instruction.Operation is M68kJitOperation.Bcc or M68kJitOperation.Dbcc)
                {
                    var fallthrough = Normalize(instruction.ProgramCounter + (uint)instruction.Length);
                    EmitV2BranchToTarget(il, context, fallthrough, labels, addressToIndex, exit);
                }
                else if (!IsV2BranchInstruction(instruction) &&
                    (i + 1 >= instructions.Length ||
                        instructions[i + 1].ProgramCounter != Normalize(instruction.ProgramCounter + (uint)instruction.Length)))
                {
                    il.Emit(OpCodes.Br, exit);
                }
            }

            il.MarkLabel(exit);
            context.EmitWriteback();
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
            Label exit)
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
                    EmitV2MoveDataRegister(il, context, instruction);
                    return;
                case M68kJitOperation.Addq:
                    EmitV2QuickArithmetic(il, context, instruction, add: true);
                    return;
                case M68kJitOperation.Subq:
                    EmitV2QuickArithmetic(il, context, instruction, add: false);
                    return;
                case M68kJitOperation.Tst:
                    EmitV2Tst(il, context, instruction);
                    return;
                case M68kJitOperation.Cmp:
                    EmitV2CompareDataRegisters(il, context, instruction);
                    return;
                case M68kJitOperation.Cmpi:
                    EmitV2CompareImmediate(il, context, instruction);
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
                case M68kJitOperation.Bra:
                    EmitV2Bra(il, context, instruction, labels, addressToIndex, exit);
                    return;
                case M68kJitOperation.Bcc:
                    EmitV2Bcc(il, context, instruction, labels, addressToIndex, exit);
                    return;
                case M68kJitOperation.Dbcc:
                    EmitV2Dbcc(il, context, instruction, labels, addressToIndex, exit);
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

        private static void EmitV2MoveDataRegister(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Source.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, value);
            context.EmitStoreDataRegister(instruction.Destination.Register, value, instruction.Size);
            context.EmitSetPendingLogic(value, instruction.Size);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2QuickArithmetic(
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
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add);
            context.EmitStoreDataRegister(instruction.Destination.Register, result, instruction.Size);
            context.EmitSetPendingArithmetic(add ? V2PendingFlags.Add : V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: true);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 8 : 4);
        }

        private static void EmitV2Tst(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var value = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, value);
            context.EmitSetPendingLogic(value, instruction.Size);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2CompareDataRegisters(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            context.EmitLoadDataRegister(instruction.Source.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add: false);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: false);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 12 : 8);
        }

        private static void EmitV2CompareImmediate(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction)
        {
            var destination = il.DeclareLocal(typeof(uint));
            var source = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Destination.Register, instruction.Size);
            il.Emit(OpCodes.Stloc, destination);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.Source.Immediate));
            il.Emit(OpCodes.Stloc, source);
            EmitV2ArithmeticResult(il, destination, source, result, instruction.Size, add: false);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source, result, instruction.Size, setExtend: false);
            context.EmitAddCycles(instruction.Size == M68kOperandSize.Long ? 16 : 8);
        }

        private static void EmitV2Logical(ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction, int operation)
        {
            var destinationRegister = instruction.Operation == M68kJitOperation.Eor || instruction.Variant != 0
                ? instruction.Destination.Register
                : instruction.Register;
            var sourceRegister = instruction.Operation == M68kJitOperation.Eor || instruction.Variant != 0
                ? instruction.Register
                : instruction.Source.Register;
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(destinationRegister, instruction.Size);
            context.EmitLoadDataRegister(sourceRegister, instruction.Size);
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

        private static void EmitV2Bra(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit)
        {
            var target = GetBranchTarget(instruction);
            context.EmitAddCycles(10);
            context.EmitSetProgramCounter(target);
            EmitV2BranchToTarget(il, context, target, labels, addressToIndex, exit);
        }

        private static void EmitV2Bcc(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit)
        {
            var notTaken = il.DefineLabel();
            var target = GetBranchTarget(instruction);
            context.EmitMaterializePendingFlags();
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(OpCodes.Ldc_I4, instruction.Condition);
            il.Emit(OpCodes.Call, CheckV2ConditionMethod);
            il.Emit(OpCodes.Brfalse, notTaken);
            context.EmitAddCycles(10);
            context.EmitSetProgramCounter(target);
            EmitV2BranchToTarget(il, context, target, labels, addressToIndex, exit);
            il.MarkLabel(notTaken);
            context.EmitAddCycles(8);
        }

        private static void EmitV2Dbcc(
            ILGenerator il,
            V2EmitContext context,
            M68kDecodedInstruction instruction,
            Label[] labels,
            Dictionary<uint, int> addressToIndex,
            Label exit)
        {
            var conditionFalse = il.DefineLabel();
            var expired = il.DefineLabel();
            var done = il.DefineLabel();
            var counter = il.DeclareLocal(typeof(uint));
            var target = GetBranchTarget(instruction);
            context.EmitMaterializePendingFlags();
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(OpCodes.Ldc_I4, instruction.Condition);
            il.Emit(OpCodes.Call, CheckV2ConditionMethod);
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
            EmitV2BranchToTarget(il, context, target, labels, addressToIndex, exit);

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
            Label exit)
        {
            if (addressToIndex.TryGetValue(target, out var targetIndex))
            {
                il.Emit(OpCodes.Br, labels[targetIndex]);
                return;
            }

            context.EmitRecordOutOfBlockSideExit();
            il.Emit(OpCodes.Br, exit);
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

        private void RecordV2Rejection(V2TraceRejectionReason reason)
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
                    break;
                case V2TraceRejectionReason.UnsupportedOperation:
                    _counters.V2RejectedUnsupportedOperation++;
                    break;
                case V2TraceRejectionReason.Budget:
                    _counters.V2RejectedBudget++;
                    break;
                default:
                    _counters.V2RejectedEmpty++;
                    break;
            }
        }

        private void RecordV2LazyWriteback()
        {
            _counters.V2LazyWritebacks++;
        }

        private void RecordV2OutOfBlockSideExit()
        {
            _counters.V2SideExits++;
            _counters.V2SideExitOutOfBlockBranch++;
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

        private static bool CheckV2Condition(int status, int condition)
        {
            var c = (status & M68kCpuState.Carry) != 0;
            var z = (status & M68kCpuState.Zero) != 0;
            var n = (status & M68kCpuState.Negative) != 0;
            var v = (status & M68kCpuState.Overflow) != 0;
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
                (_amigaBus != null && _amigaBus.HasHostCallback(programCounter)))
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
                            !RangesOverlap(trace.Root, trace.ByteLength, address, byteCount))
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
                default:
                    State.ProgramCounter = State.LastInstructionProgramCounter;
                    State.Cycles = _compiledInstructionPreviousCycle;
                    _counters.UnsupportedOpcode++;
                    return false;
            }
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

        private bool ExecuteCompiledJumpTo(uint target, bool link)
        {
            if (_amigaBus != null && _amigaBus.HasHostCallback(target))
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
            if (_amigaBus != null && _amigaBus.HasHostCallback(target))
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

        private void AddCycles(int cycles)
        {
            State.Cycles += Math.Max(1, cycles);
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

        private static void ForEachTracePage(TraceEntry trace, Action<uint> action)
        {
            var remaining = trace.ByteLength;
            var current = trace.Root;
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
            private static readonly MethodInfo RecordOutOfBlockSideExit =
                typeof(M68kJitCore).GetMethod(nameof(RecordV2OutOfBlockSideExit), BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new MissingMethodException(typeof(M68kJitCore).FullName, nameof(RecordV2OutOfBlockSideExit));

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

            public static V2EmitContext Create(ILGenerator il)
                => new V2EmitContext(il);

            public void EmitLoadState(uint root, Label returnZero)
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

            public void EmitWriteback()
            {
                EmitMaterializePendingFlags();
                for (var i = 0; i < 8; i++)
                {
                    _il.Emit(OpCodes.Ldloc, DataRegisterArray);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    _il.Emit(OpCodes.Ldloc, DataRegisters[i]);
                    _il.Emit(OpCodes.Stelem_I4);
                    _il.Emit(OpCodes.Ldloc, AddressRegisterArray);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    _il.Emit(OpCodes.Ldloc, AddressRegisters[i]);
                    _il.Emit(OpCodes.Stelem_I4);
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
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Call, RecordLazyWriteback);
            }

            public void EmitLoadDataRegister(int register, M68kOperandSize size)
            {
                _il.Emit(OpCodes.Ldloc, DataRegisters[register]);
                EmitLoadUIntConstant(_il, M68kCpuState.Mask(size));
                _il.Emit(OpCodes.And);
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

            public void EmitRecordOutOfBlockSideExit()
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Call, RecordOutOfBlockSideExit);
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
        }

        private enum V2TraceTier
        {
            None = 0,
            Tier0 = 1,
            Tier1 = 2,
            Tier2 = 3
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
                int byteLength,
                bool hasInternalLoop)
            {
                Compiled = compiled;
                Tier = tier;
                Instructions = instructions;
                ByteLength = byteLength;
                HasInternalLoop = hasInternalLoop;
            }

            public bool IsEmpty => Compiled == null;

            public CompiledTrace? Compiled { get; }

            public V2TraceTier Tier { get; }

            public M68kDecodedInstruction[] Instructions { get; }

            public int ByteLength { get; }

            public int InstructionCount => Instructions?.Length ?? 0;

            public bool HasInternalLoop { get; }
        }

        private readonly struct TraceEntry
        {
            public TraceEntry(
                uint root,
                int byteLength,
                uint startGeneration,
                uint endGeneration,
                ushort[] codeWords,
                int[] directInstructionPrefixes,
                int[] helperInstructionPrefixes,
                int[] directCpuInstructionPrefixes,
                int[] directMemoryInstructionPrefixes,
                int[] specializedHelperInstructionPrefixes,
                CompiledTrace compiled,
                bool pureCpuBatchEligible,
                CompiledTrace? pureCompiled,
                CompiledTrace? v2Compiled,
                V2TraceTier v2Tier,
                int v2InstructionCount,
                bool v2HasInternalLoop)
            {
                Root = root;
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
            }

            public uint Root { get; }

            public int ByteLength { get; }

            public uint StartGeneration { get; }

            public uint EndGeneration { get; }

            public ushort[] CodeWords { get; }

            public int[] DirectInstructionPrefixes { get; }

            public int[] HelperInstructionPrefixes { get; }

            public int[] DirectCpuInstructionPrefixes { get; }

            public int[] DirectMemoryInstructionPrefixes { get; }

            public int[] SpecializedHelperInstructionPrefixes { get; }

            public CompiledTrace Compiled { get; }

            public bool PureCpuBatchEligible { get; }

            public CompiledTrace? PureCompiled { get; }

            public CompiledTrace? V2Compiled { get; }

            public V2TraceTier V2Tier { get; }

            public int V2InstructionCount { get; }

            public bool V2HasInternalLoop { get; }

            public TraceEntry WithGenerations(uint startGeneration, uint endGeneration)
                => new TraceEntry(
                    Root,
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
                    V2HasInternalLoop);

            public TraceEntry WithV2(
                int byteLength,
                uint startGeneration,
                uint endGeneration,
                ushort[] codeWords,
                CompiledTrace compiled,
                V2TraceTier tier,
                int instructionCount,
                bool hasInternalLoop)
                => new TraceEntry(
                    Root,
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
                    hasInternalLoop);
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

        public long V2Tier0CompiledTraces { get; set; }

        public long V2Tier1CompiledTraces { get; set; }

        public long V2Tier2CompiledTraces { get; set; }

        public long V2TierPromotions { get; set; }

        public long CompiledTraces { get; set; }

        public long SideExits { get; set; }

        public long V2SideExits { get; set; }

        public long V2SideExitEntryMismatch { get; set; }

        public long V2SideExitOutOfBlockBranch { get; set; }

        public long V2LazyWritebacks { get; set; }

        public long V2FlagMaterializations { get; set; }

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

        public long DirectCpuIlInstructions { get; set; }

        public long DirectMemoryIlInstructions { get; set; }

        public long SpecializedHelperIlInstructions { get; set; }
    }
}
