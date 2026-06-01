using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace CopperMod.Amiga
{
    internal sealed class M68kJitCore : IM68kBatchCore
    {
        private const int CompileThreshold = 64;
        private const int MaxTraceInstructions = 64;
        private const int MaxTraceBytes = 256;
        private const int BlacklistHits = 256;

        private readonly IM68kBus _bus;
        private readonly AmigaBus? _amigaBus;
        private readonly M68kInterpreter _fallback;
        private readonly Dictionary<uint, TraceEntry> _traces = new Dictionary<uint, TraceEntry>();
        private readonly Dictionary<uint, int> _hotCounters = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> _blacklist = new Dictionary<uint, int>();
        private M68kJitCounters _counters;
        private long _compiledInstructionPreviousCycle;

        public M68kJitCore(IM68kBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _amigaBus = bus as AmigaBus;
            State = new M68kCpuState();
            _fallback = new M68kInterpreter(bus, State);
        }

        public M68kCpuState State { get; }

        public M68kJitCounters Counters => _counters;

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
            if (!_traces.TryGetValue(pc, out var trace))
            {
                return 0;
            }

            if (_amigaBus != null &&
                !_amigaBus.CodeRangeGenerationMatches(trace.Root, trace.ByteLength, trace.StartGeneration, trace.EndGeneration))
            {
                _traces.Remove(pc);
                _counters.Invalidations++;
                _counters.GenerationMismatches++;
                return 0;
            }

            _counters.TraceHits++;
            var executed = trace.Compiled(this, maxInstructions, targetCycle ?? long.MaxValue, boundary);
            if (executed == 0)
            {
                _counters.SideExits++;
            }

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

        private void ObserveHotRoot(uint pc)
        {
            if (_amigaBus == null || _traces.ContainsKey(pc))
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

            _traces[pc] = trace;
            _counters.CompiledTraces++;
        }

        private bool TryCompileTrace(uint root, out TraceEntry trace)
        {
            trace = default;
            if (_amigaBus == null)
            {
                return false;
            }

            Span<M68kDecodedInstruction> instructions = stackalloc M68kDecodedInstruction[MaxTraceInstructions];
            var count = 0;
            var pc = root;
            var byteCount = 0;
            while (count < MaxTraceInstructions && byteCount < MaxTraceBytes)
            {
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

            var compiled = Compile(root, instructions[..count]);
            trace = new TraceEntry(
                root,
                byteCount,
                _amigaBus.GetCodePageGeneration(root),
                _amigaBus.GetCodePageGeneration(Normalize(root + (uint)Math.Max(0, byteCount - 1))),
                compiled);
            return true;
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

        private static CompiledTrace Compile(uint root, ReadOnlySpan<M68kDecodedInstruction> instructions)
        {
            var method = new DynamicMethod(
                "M68kTrace_" + root.ToString("X6"),
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

            var canEnter = GetInstanceMethod(nameof(CanEnterCompiledInstruction));
            var begin = GetInstanceMethod(nameof(BeginCompiledInstruction));
            var finish = GetInstanceMethod(nameof(FinishCompiledInstruction));

            for (var i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, canEnter);
                il.Emit(OpCodes.Brfalse, returnLabels[i]);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.ProgramCounter));
                il.Emit(OpCodes.Ldc_I4, instruction.Opcode);
                il.Emit(OpCodes.Ldc_I4, instruction.ExtensionCount);
                il.Emit(OpCodes.Ldc_I4, instruction.Extension0);
                il.Emit(OpCodes.Ldc_I4, instruction.Extension1);
                il.Emit(OpCodes.Ldc_I4, instruction.Extension2);
                il.Emit(OpCodes.Ldc_I4, instruction.Extension3);
                il.Emit(OpCodes.Call, begin);
                il.Emit(OpCodes.Brfalse, returnLabels[i]);

                M68kOperationEmitter.Emit(il, instruction);
                il.Emit(OpCodes.Brfalse, returnLabels[i]);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, finish);
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

        private bool BeginCompiledInstruction(
            uint programCounter,
            ushort expectedOpcode,
            int extensionCount,
            ushort extension0,
            ushort extension1,
            ushort extension2,
            ushort extension3)
        {
            programCounter = Normalize(programCounter);
            _compiledInstructionPreviousCycle = State.Cycles;
            if (State.ProgramCounter != programCounter ||
                (_amigaBus != null && _amigaBus.HasHostCallback(programCounter)))
            {
                _counters.HostTrapBailouts++;
                return false;
            }

            var cycle = State.Cycles;
            var opcode = _bus.ReadWord(programCounter, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
            if (opcode != expectedOpcode)
            {
                InvalidateSource(programCounter);
                return false;
            }

            var cursor = Normalize(programCounter + 2);
            for (var i = 0; i < extensionCount; i++)
            {
                var expected = i switch
                {
                    0 => extension0,
                    1 => extension1,
                    2 => extension2,
                    _ => extension3
                };
                var actual = _bus.ReadWord(cursor, ref cycle, AmigaBusAccessKind.CpuInstructionFetch);
                if (actual != expected)
                {
                    InvalidateSource(programCounter);
                    return false;
                }

                cursor = Normalize(cursor + 2);
            }

            State.Cycles = cycle;
            State.LastOpcode = opcode;
            State.LastInstructionProgramCounter = programCounter;
            State.ProgramCounter = cursor;
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
            _traces.Remove(Normalize(programCounter));
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

        private void ClearRuntimeState()
        {
            _traces.Clear();
            _hotCounters.Clear();
            _blacklist.Clear();
        }

        private static uint Normalize(uint address)
        {
            return address & 0x00FF_FFFF;
        }

        private delegate int CompiledTrace(M68kJitCore core, int maxInstructions, long targetCycle, IM68kInstructionBoundary boundary);

        private readonly struct TraceEntry
        {
            public TraceEntry(uint root, int byteLength, uint startGeneration, uint endGeneration, CompiledTrace compiled)
            {
                Root = root;
                ByteLength = byteLength;
                StartGeneration = startGeneration;
                EndGeneration = endGeneration;
                Compiled = compiled;
            }

            public uint Root { get; }

            public int ByteLength { get; }

            public uint StartGeneration { get; }

            public uint EndGeneration { get; }

            public CompiledTrace Compiled { get; }
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

        public long CompiledTraces { get; set; }

        public long SideExits { get; set; }

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
    }
}
