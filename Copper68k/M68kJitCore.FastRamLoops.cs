/*
 * Copyright (C) 2026 Ilkka Lehtoranta
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Copper68k
{
    internal sealed partial class M68kJitCore
    {
        private const int FastRamLoopPromotionHits = 96;
        private FastRamResume? _fastRamResume;
        private readonly bool _canPromoteFastRamLoops;
        private HashSet<uint>? _fastRamLoopEntryProbes;
        private Dictionary<uint, int>? _fastRamLoopEntries;
        private Dictionary<uint, FastRamLoop>? _fastRamLoops;

        // A promoted loop borrows the current V2 batch. Every instruction
        // still retires at the 040 zero-wait floor. The compiler eliminates
        // stores and computations that are private until that slice returns.
        private void ObserveFastRamLoopBackedge(uint from, uint target, IM68kBusAccessTraceBatchBoundary boundary)
        {
            if (_fastRamLoops?.Count >= 16 || (_fastRamLoops?.ContainsKey(target) ?? false) ||
                State.CacheControlRegister != 0 ||
                target >= from || from - target > 2048 ||
                !State.M68040Mmu.DirectIdentityAccessEnabled || _jitBus == null)
                return;

            var entries = _fastRamLoopEntries ??= new();
            if (!entries.TryGetValue(target, out var visits))
            {
                if (entries.Count >= 4096 ||
                    !_fastMemoryBus!.TryGetJitZeroWaitReadMemory(target, 2, out _, out _, out var kind) ||
                    kind != M68kJitMemoryKind.FastRam)
                    return;
            }
            if (visits >= FastRamLoopPromotionHits)
                return;
            entries[target] = ++visits;
            if (visits != FastRamLoopPromotionHits)
                return;

            TryPromoteFastRamLoop(target, new FastRamLoopCodeReader(this, _jitBus, _fastMemoryBus!), boundary);
        }

        private void ProbeFastRamLoopEntry(uint root, IM68kBusAccessTraceBatchBoundary boundary)
        {
            if (_fastRamLoops?.Count >= 16 || _jitBus == null ||
                State.CacheControlRegister != 0 || !State.M68040Mmu.DirectIdentityAccessEnabled)
                return;

            // Rarely executed fallthrough PCs can fill the first-entry
            // probe budget. Keep that budget independent of hot backedges.
            var probes = _fastRamLoopEntryProbes ??= new();
            if (!IsUncachedFastCode(root) || probes.Contains(root) ||
                (_fastRamLoops?.ContainsKey(root) ?? false) ||
                !_fastMemoryBus!.TryGetJitZeroWaitReadMemory(root, 2, out _, out _, out var kind) ||
                kind != M68kJitMemoryKind.FastRam)
                return;
            if (probes.Count >= 4096)
                probes.Clear();
            probes.Add(root);

            // A caller with a visible backedge can qualify before the
            // first iteration has compiled each of its callees separately.
            var reader = new FastRamLoopCodeReader(this, _jitBus, _fastMemoryBus);
            var pc = root;
            var calls = 0;
            for (var i = 0; i < 32 && pc - root < 256; i++)
            {
                if (!M68kDecoder.TryDecode(reader, pc, out var instruction, out _, M68kJitCpuModel.M68040))
                    return;
                switch (instruction.Operation)
                {
                    case M68kJitOperation.Bcc:
                        if (calls != 0 && GetBranchTarget(instruction) == root)
                            TryPromoteFastRamLoop(root, reader, boundary);
                        return;
                    case M68kJitOperation.Bsr:
                        calls++;
                        break;
                    case M68kJitOperation.Bra:
                    case M68kJitOperation.Rts:
                    case M68kJitOperation.Jmp:
                    case M68kJitOperation.Jsr:
                    case M68kJitOperation.Dbcc:
                        return;
                }
                pc = GetInstructionFallthrough(instruction);
            }
        }

        private bool TryPromoteFastRamLoop(
            uint target, FastRamLoopCodeReader reader, IM68kBusAccessTraceBatchBoundary boundary)
        {
            if (!TryPlanFastRamLoop(reader, target, out var plan) || plan.Count < 64)
                return false;
            try
            {
                var loop = new FastRamLoop(this, [.. plan], boundary.PreservesBusDeadlineOnEarlyReturn);
                return (_fastRamLoops ??= new()).TryAdd(target, loop);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private sealed class FastRamLoopCodeReader(M68kJitCore cpu, IM68kJitBus code, IM68kJitFastMemoryBus bus) : IM68kCodeReader
        {
            public ushort ReadHostWord(uint pc)
            {
                if (pc >= 0x01000000 ||
                    !cpu.IsM68040PhysicalAddressMapped(pc, 2, M68kBusAccessKind.CpuInstructionFetch) ||
                    !code.IsJitCodeAddress(pc, 2, M68kBusAccessKind.CpuInstructionFetch) ||
                    !bus.TryGetJitZeroWaitReadMemory(pc, 2, out _, out _, out var kind) ||
                    kind != M68kJitMemoryKind.FastRam)
                    throw M68kCodeReadException.Instance;
                return code.ReadJitCodeWord(pc);
            }
        }

        private int TryExecuteFastRamLoop(
            TraceEntry current, int remaining, long target, IM68kBusAccessTraceBatchBoundary boundary,
            out int wholeLapInstructions)
        {
            wholeLapInstructions = 0;
            if (_canPromoteFastRamLoops && remaining >= 64 && target - State.Cycles >= 64 &&
                Normalize(State.ProgramCounter) == current.Root && !_guardedBatchMustExit)
                ProbeFastRamLoopEntry(current.Root, boundary);

            if (_fastRamLoops == null || !_fastRamLoops.TryGetValue(current.Root, out var loop) ||
                Normalize(State.ProgramCounter) != loop.Root || _guardedBatchMustExit ||
                State.CacheControlRegister != 0 || (State.StatusRegister & 0xC000) != 0 ||
                !State.M68040Mmu.DirectIdentityAccessEnabled)
                return 0;

            return loop.Execute(this, remaining, target, boundary.PreservesBusDeadlineOnEarlyReturn,
                out wholeLapInstructions);
        }

        private int TryExecuteFirstEntryFastRamLoop(
            uint root, int remaining, long target, IM68kBusAccessTraceBatchBoundary boundary)
        {
            if (remaining < 64 || target - State.Cycles < 64 || _instructionFrequency.Enabled ||
                State.CacheControlRegister != 0 || (State.StatusRegister & 0xC000) != 0)
                return 0;

            ProbeFastRamLoopEntry(root, boundary);
            if (_fastRamLoops == null || !_fastRamLoops.TryGetValue(root, out var loop))
                return 0;

            var previous = State.Cycles;
            _uncachedCodeChanged = false;
            _guardedBatchMustExit = false;
            if (!boundary.TryBeginBusAccessTraceBatch(State, target, out var deadline) ||
                _guardedBatchMustExit || Normalize(State.ProgramCounter) != root ||
                !State.M68040Mmu.DirectIdentityAccessEnabled)
                return 0;

            var executed = loop.Execute(this, remaining, deadline, boundary.PreservesBusDeadlineOnEarlyReturn,
                out var wholeLapInstructions);
            if (executed == 0)
                return 0;

            CompleteFastRamLoopBatch(boundary, previous, executed, wholeLapInstructions);
            return executed;
        }

        private static bool TryPlanFastRamLoop(IM68kCodeReader code, uint entry, out List<M68kDecodedInstruction> plan)
        {
            const int instructionLimit = 1024;
            const int callDepthLimit = 8;
            plan = [];
            var pc = entry;
            Span<uint> returns = stackalloc uint[callDepthLimit];
            var depth = 0;
            while (plan.Count < instructionLimit && M68kDecoder.TryDecode(code, pc,
                out var instruction, out _, M68kJitCpuModel.M68040))
            {
                var next = Normalize(pc + (uint)instruction.Length);
                var target = Normalize(instruction.BranchBase + unchecked((uint)instruction.Displacement));
                plan.Add(instruction);
                switch (instruction.Operation)
                {
                    case M68kJitOperation.Bsr:
                        if (depth == returns.Length)
                            return false;
                        returns[depth++] = next;
                        pc = target;
                        break;
                    case M68kJitOperation.Rts:
                        if (depth == 0)
                            return false;
                        pc = returns[--depth];
                        break;
                    case M68kJitOperation.Bra:
                        if (depth == 0 && target == entry)
                            return true;
                        pc = target;
                        break;
                    case M68kJitOperation.Bcc:
                        return depth == 0 && target == entry;
                    case M68kJitOperation.Add:
                    case M68kJitOperation.Addi:
                    case M68kJitOperation.Addq:
                    case M68kJitOperation.And:
                    case M68kJitOperation.Andi:
                    case M68kJitOperation.Clr:
                    case M68kJitOperation.Cmp:
                    case M68kJitOperation.Divs:
                    case M68kJitOperation.Divu:
                    case M68kJitOperation.Eori:
                    case M68kJitOperation.Exg:
                    case M68kJitOperation.ExtLong:
                    case M68kJitOperation.ExtWord:
                    case M68kJitOperation.Lea:
                    case M68kJitOperation.Move:
                    case M68kJitOperation.Movea:
                    case M68kJitOperation.Moveq:
                    case M68kJitOperation.Muls:
                    case M68kJitOperation.Mulu:
                    case M68kJitOperation.Neg:
                    case M68kJitOperation.Nop:
                    case M68kJitOperation.Not:
                    case M68kJitOperation.Ori:
                    case M68kJitOperation.ShiftRegister:
                    case M68kJitOperation.Subi:
                    case M68kJitOperation.Swap:
                        pc = next;
                        break;
                    default:
                        return false;
                }
            }

            return false;
        }


        // The device deadline can cut through a call. Re-enter only the exact
        // compiled position after confirming that the boundary preserved CPU
        // state and the Fast RAM return words still belong to this iteration.
        private int TryResumeFastRamLoop(
            int allowance, long? target, IM68kInstructionBoundary boundary)
        {
            var resume = _fastRamResume!;
            var loop = resume.Loop!;
            resume.Loop = null;
            if (!target.HasValue || _instructionFrequency.Enabled || State.CacheControlRegister != 0 ||
                (State.StatusRegister & 0xC000) != 0 || !State.M68040Mmu.DirectIdentityAccessEnabled ||
                boundary is not IM68kBusAccessTraceBatchBoundary { PreservesBusDeadlineOnEarlyReturn: true } batch ||
                !resume.Matches(State))
                return 0;

            var previous = State.Cycles;
            _uncachedCodeChanged = false;
            _guardedBatchMustExit = false;
            if (!batch.TryBeginBusAccessTraceBatch(State, target.Value, out var deadline) ||
                _guardedBatchMustExit || State.Stopped || State.Halted || _instructionFrequency.Enabled ||
                State.CacheControlRegister != 0 || !State.M68040Mmu.DirectIdentityAccessEnabled ||
                _fallback is M68kAdvancedTimingInterpreter { HasPendingM68040MovemContinuation: true } ||
                !resume.Matches(State))
                return 0;

            var executed = loop.Resume(this, allowance, deadline, resume, out var wholeLapInstructions);
            if (executed == 0)
                return 0;

            CompleteFastRamLoopBatch(batch, previous, executed, wholeLapInstructions);
            return executed;
        }

        private void CompleteFastRamLoopBatch(IM68kBusAccessTraceBatchBoundary boundary, long previousCycle,
            int executed, int wholeLapInstructions)
        {
            _fallbackFetchSynchronized = false;
            boundary.AfterBusAccessTraceBatch(previousCycle, State.Cycles, executed);
            _counters.DirectIlInstructions += executed;
            _counters.DirectMemoryIlInstructions += executed;
            _counters.V2BusAccessBatchExecutions++;
            _counters.V2BusAccessBatchInstructions += executed;
            _counters.V2BusAccessBatchBoundaryCallsSaved += executed - 1;
            _counters.V2BoundLapInstructions += wholeLapInstructions;
            if (wholeLapInstructions != 0)
                _counters.V2BoundLapBatches++;
        }

        private sealed class FastRamResume
        {
            internal FastRamLoop? Loop;
            internal int Index;
            private readonly uint[] _registers = new uint[16];
            private readonly byte[] _stack = new byte[32];
            private int _stackLength;
            private long _cycles;
            private uint _pc;
            private uint _userStackPointer;
            private uint _interruptStackPointer;
            private uint _masterStackPointer;
            private ushort _status;

            internal void Save(FastRamLoop loop, int index, M68kCpuState state, ReadOnlySpan<byte> stack)
            {
                Index = index;
                _cycles = state.Cycles;
                _pc = state.ProgramCounter;
                _status = state.StatusRegister;
                _userStackPointer = state.UserStackPointer;
                _interruptStackPointer = state.InterruptStackPointer;
                _masterStackPointer = state.MasterStackPointer;
                state.D.AsSpan().CopyTo(_registers.AsSpan(0, 8));
                state.A.AsSpan().CopyTo(_registers.AsSpan(8, 8));
                _stackLength = stack.Length;
                stack.CopyTo(_stack);
                Loop = loop;
            }

            internal bool Matches(M68kCpuState state) =>
                state.Cycles == _cycles && state.ProgramCounter == _pc && state.StatusRegister == _status &&
                state.UserStackPointer == _userStackPointer && state.InterruptStackPointer == _interruptStackPointer &&
                state.MasterStackPointer == _masterStackPointer &&
                state.D.AsSpan().SequenceEqual(_registers.AsSpan(0, 8)) &&
                state.A.AsSpan().SequenceEqual(_registers.AsSpan(8, 8));

            internal bool MatchesStack(ReadOnlySpan<byte> stack) =>
                stack.SequenceEqual(_stack.AsSpan(0, _stackLength));
        }

        private sealed class FastRamLoop
        {
            private readonly uint[] _entryAddress;
            private readonly int _addressInputs;
            private readonly uint[] _reads;
            private readonly Func<M68kJitCore, int, long, FastRamLoopMemory, int> _trace;
            private readonly FastRamLoopMemory _memory;
            private readonly int _length;
            private FastRamFragmentPlan? _fragmentPlan;
            private FastRamFragment? _fragment;

            private readonly KeyValuePair<uint, uint>[] _codePages;
            private readonly uint _stackAddress;
            private readonly int _stackLength;
            internal uint Root { get; }

            internal FastRamLoop(M68kJitCore cpu, M68kDecodedInstruction[] plan, bool prepareFragment)
            {
                var bus = cpu._fastMemoryBus!;
                Root = plan[0].ProgramCounter;
                _entryAddress = (uint[])cpu.State.A.Clone();
                _length = plan.Length;
                var addresses = (uint[])_entryAddress.Clone();
                var constantAddressRegisters = 0;
                var addressInputs = 1 << 7;
                var constants = new uint?[8];
                var deepestStack = 0;
                var stack = new Stack<uint>();
                var readAddresses = new List<uint>();
                var readSlots = new int[_length];
                var stackOffsets = new int[_length];
                var divideConstants = new uint[_length];
                Array.Fill(readSlots, -1);
                for (var i = 0; i < _length; i++)
                {
                    var instruction = plan[i];
                    var source = instruction.Source;
                    var dest = instruction.Destination;
                    if (source.Kind == M68kJitEaKind.AddressRegister && source.Register == 7 ||
                        dest.Kind == M68kJitEaKind.AddressRegister && dest.Register == 7)
                        throw Unsupported(instruction);
                    var next = i + 1 < plan.Length ? plan[i + 1].ProgramCounter : plan[0].ProgramCounter;
                    switch (instruction.Operation)
                    {
                        case M68kJitOperation.Bsr:
                            stack.Push(Normalize(instruction.ProgramCounter + (uint)instruction.Length));
                            deepestStack = Math.Max(deepestStack, stack.Count);
                            stackOffsets[i] = -stack.Count * 4;
                            continue;
                        case M68kJitOperation.Rts:
                            if (!stack.TryPop(out var matched) || matched != next)
                                throw Unsupported(instruction);
                            continue;
                        case M68kJitOperation.Bra:
                        case M68kJitOperation.Bcc:
                            continue;
                        case M68kJitOperation.Lea:
                            if (instruction.Register == 7 || source.Kind != M68kJitEaKind.AbsoluteLong)
                                throw Unsupported(instruction);
                            addresses[instruction.Register] = ((uint)source.Extension0 << 16) | source.Extension1;
                            constantAddressRegisters |= 1 << instruction.Register;
                            continue;
                        case M68kJitOperation.Movea:
                            if (instruction.Size != M68kOperandSize.Long || dest.Register == 7 ||
                                source.Kind != M68kJitEaKind.Immediate)
                                throw Unsupported(instruction);
                            addresses[dest.Register] = source.Immediate;
                            constantAddressRegisters |= 1 << dest.Register;
                            continue;
                        case M68kJitOperation.Divu:
                        case M68kJitOperation.Divs:
                            if (source.Kind != M68kJitEaKind.DataRegister ||
                                constants[source.Register] is not uint divisor || (divisor & 0xFFFF) == 0)
                                throw Unsupported(instruction);
                            divideConstants[i] = divisor & 0xFFFF;
                            break;
                    }

                    if (source.IsMemory)
                    {
                        if (instruction.Size != M68kOperandSize.Long ||
                            instruction.Operation is not (M68kJitOperation.Move or M68kJitOperation.Cmp) ||
                            source.Register == 7)
                            throw Unsupported(instruction);
                        if (source.Kind is M68kJitEaKind.AddressIndirect or M68kJitEaKind.AddressDisplacement)
                            addressInputs |= (1 << source.Register) & ~constantAddressRegisters;
                        var address = source.Kind switch
                        {
                            M68kJitEaKind.AddressIndirect => addresses[source.Register],
                            M68kJitEaKind.AddressDisplacement => unchecked(addresses[source.Register] + (uint)(short)source.Extension0),
                            M68kJitEaKind.AbsoluteLong => ((uint)source.Extension0 << 16) | source.Extension1,
                            M68kJitEaKind.AbsoluteWord => unchecked((uint)(short)source.Extension0),
                            _ => throw Unsupported(instruction),
                        };
                        if ((address & 1) != 0 || address >= 0x01000000)
                            throw Unsupported(instruction);
                        var slot = readAddresses.IndexOf(address);
                        if (slot < 0)
                        {
                            slot = readAddresses.Count;
                            readAddresses.Add(address);
                        }
                        readSlots[i] = slot;
                    }
                    if (dest.IsMemory && instruction.Operation != M68kJitOperation.Cmp ||
                        instruction.Operation == M68kJitOperation.Move && source.IsMemory &&
                        dest.Kind != M68kJitEaKind.DataRegister)
                        throw Unsupported(instruction);

                    var writtenData = 0;
                    var writtenAddress = 0;
                    MarkV2DirtyRegisterMasks(instruction, ref writtenData, ref writtenAddress);
                    if (writtenAddress != 0)
                        throw Unsupported(instruction);
                    for (var register = 0; register < constants.Length; register++)
                    {
                        if ((writtenData & (1 << register)) != 0)
                            constants[register] = null;
                    }
                    if (instruction.Operation == M68kJitOperation.Move && instruction.Size == M68kOperandSize.Long &&
                        source.Kind == M68kJitEaKind.Immediate && dest.Kind == M68kJitEaKind.DataRegister)
                        constants[dest.Register] = source.Immediate;
                    if (instruction.Operation == M68kJitOperation.Moveq)
                        constants[instruction.Register] = unchecked((uint)instruction.QuickValue);
                }

                _addressInputs = addressInputs;
                if (stack.Count != 0 || deepestStack == 0 || !MatchesAddressInputs(addresses))
                    throw new InvalidOperationException("A memory address input is not invariant around the loop.");
                var stackSize = 4 * deepestStack;
                if (_entryAddress[7] < (uint)stackSize)
                    throw new InvalidOperationException("Stack is below the required range.");
                var stackStart = _entryAddress[7] - (uint)stackSize;
                _stackAddress = stackStart;
                _stackLength = stackSize;
                if ((stackStart & 1) != 0 || stackStart >= 0x01000000 ||
                    !bus.TryGetJitUnwatchedFastRam(stackStart, stackSize, out var stackMemory, out var stackOffset))
                    throw new InvalidOperationException("Stack writes are outside exclusive, unwatched Fast RAM.");
                foreach (var address in readAddresses)
                {
                    if (address < (ulong)stackStart + (uint)stackSize && (ulong)address + 4 > stackStart ||
                        !bus.TryGetJitZeroWaitReadMemory(address, 4, out _, out _, out var kind) ||
                        kind != M68kJitMemoryKind.FastRam)
                        throw new InvalidOperationException("A read is outside Fast RAM or aliases a stack write.");
                }
                _reads = [.. readAddresses];
                _memory = new FastRamLoopMemory(new uint[_reads.Length], stackMemory, stackOffset + stackSize);
                _codePages = CaptureCodePages(cpu._jitBus!, plan);
                _trace = CompileFastRamLoop(plan, readSlots, stackOffsets, divideConstants);
                if (prepareFragment)
                    _fragmentPlan = new(plan, readSlots, stackOffsets, divideConstants);
            }

            internal int Execute(M68kJitCore cpu, int allowance, long target, bool allowFragment,
                out int wholeLapInstructions)
            {
                wholeLapInstructions = 0;
                if (allowance < _length || target - cpu.State.Cycles < _length || !MatchesAddressInputs(cpu.State.A) ||
                    !TryBindMemory(cpu))
                    return 0;
                var executed = ExecuteFromHead(cpu, allowance, target, allowFragment, out wholeLapInstructions,
                    out var nextIndex);
                FinishSlice(cpu, executed, nextIndex);
                return executed;
            }

            internal int Resume(M68kJitCore cpu, int allowance, long target, FastRamResume resume,
                out int wholeLapInstructions)
            {
                wholeLapInstructions = 0;
                var index = resume.Index;
                var budget = Math.Min(allowance, target - cpu.State.Cycles);
                if (budget <= 0 || _fragment is null || !TryBindMemory(cpu) ||
                    !resume.MatchesStack(ActiveStack(_fragment.Exits[index - 1].StackDisplacement)))
                    return 0;

                var executed = ExecuteFragment(cpu, index, (int)Math.Min(budget, _length - index), out var nextIndex);
                if (executed != 0 && nextIndex == 0 && cpu.State.ProgramCounter == Root &&
                    MatchesAddressInputs(cpu.State.A))
                {
                    executed += ExecuteFromHead(cpu, allowance - executed, target, allowFragment: true,
                        out wholeLapInstructions, out nextIndex);
                }
                FinishSlice(cpu, executed, nextIndex);
                return executed;
            }

            private bool TryBindMemory(M68kJitCore cpu)
            {
                var bus = cpu._fastMemoryBus!;
                foreach (var pair in _codePages)
                {
                    if (cpu._jitBus!.GetJitCodePageGeneration(pair.Key) != pair.Value)
                    {
                        cpu._fastRamLoops!.Remove(Root);
                        cpu._fastRamLoopEntries?.Remove(Root);
                        cpu._fastRamLoopEntryProbes?.Remove(Root);
                        return false;
                    }
                }
                if (!cpu.IsM68040PhysicalAddressMapped(_stackAddress, _stackLength, M68kBusAccessKind.CpuDataWrite) ||
                    !bus.TryGetJitUnwatchedFastRam(_stackAddress, _stackLength, out var stack, out var offset) ||
                    !ReferenceEquals(stack, _memory.Stack) || offset + _stackLength != _memory.StackStart)
                    return false;
                for (var i = 0; i < _reads.Length; i++)
                {
                    if (!cpu.IsM68040PhysicalAddressMapped(_reads[i], 4, M68kBusAccessKind.CpuDataRead) ||
                        !bus.TryReadJitZeroWaitMemory(_reads[i], M68kOperandSize.Long, out _memory.ReadValues[i]))
                        return false;
                }
                return true;
            }

            private int ExecuteFromHead(M68kJitCore cpu, int allowance, long target, bool allowFragment,
                out int wholeLapInstructions, out int nextIndex)
            {
                nextIndex = 0;
                wholeLapInstructions = allowance >= _length && target - cpu.State.Cycles >= _length
                    ? _trace(cpu, allowance, target, _memory) : 0;
                var executed = wholeLapInstructions;
                if (allowFragment && cpu.State.ProgramCounter == Root)
                {
                    var remaining = Math.Min(allowance - executed, target - cpu.State.Cycles);
                    if (remaining > 0 && remaining < _length)
                        executed += ExecuteFragment(cpu, 0, (int)remaining, out nextIndex);
                }
                return executed;
            }

            private void FinishSlice(M68kJitCore cpu, int executed, int nextIndex)
            {
                if (executed == 0)
                    return;
                cpu.State.SetActiveStackPointer(cpu.State.A[7]);
                if (nextIndex != 0)
                {
                    var resume = cpu._fastRamResume ??= new();
                    resume.Save(this, nextIndex, cpu.State, ActiveStack(_fragment!.Exits[nextIndex - 1].StackDisplacement));
                }
                cpu._fastMemoryBus!.CompleteJitZeroWaitWrite(_stackAddress, _stackLength);
            }

            private ReadOnlySpan<byte> ActiveStack(int displacement) =>
                _memory.Stack.AsSpan(_memory.StackStart + displacement, -displacement);

            private int ExecuteFragment(M68kJitCore cpu, int start, int count, out int nextIndex)
            {
                nextIndex = 0;
                if (_fragment is null)
                {
                    if (_fragmentPlan is null)
                        return 0;
                    _fragment = CompileFastRamFragment(_fragmentPlan);
                    _fragmentPlan = null;
                }

                var end = start + count;
                var destination = _fragment.Run(cpu, start, end, _memory);
                if (destination == int.MinValue)
                    return 0;
                var exit = _fragment.Exits[end - 1];
                cpu.State.ProgramCounter = destination == 0 ? Root : exit.ProgramCounter;
                cpu.State.LastInstructionProgramCounter = exit.LastProgramCounter;
                cpu.State.LastOpcode = exit.LastOpcode;
                cpu.State.Cycles += count;
                var previousStack = start == 0 ? 0 : _fragment.Exits[start - 1].StackDisplacement;
                cpu.State.A[7] = unchecked(cpu.State.A[7] + (uint)(exit.StackDisplacement - previousStack));
                nextIndex = destination > 0 ? destination : 0;
                return count;
            }

            private static KeyValuePair<uint, uint>[] CaptureCodePages(
                IM68kJitBus bus, M68kDecodedInstruction[] plan)
            {
                var stamps = new Dictionary<uint, uint>();
                foreach (var instruction in plan)
                {
                    var first = instruction.ProgramCounter & ~255u;
                    var last = (instruction.ProgramCounter + (uint)instruction.Length - 1) & ~255u;
                    for (var page = first; page <= last; page += 256)
                    {
                        if (stamps.ContainsKey(page))
                            continue;
                        var generation = bus.GetJitCodePageGeneration(page);
                        if (generation == uint.MaxValue)
                            throw new InvalidOperationException("The executable page is unavailable.");
                        stamps.Add(page, generation);
                    }
                }
                return [.. stamps];
            }

            private bool MatchesAddressInputs(uint[] addresses)
            {
                for (var register = 0; register < _entryAddress.Length; register++)
                    if ((_addressInputs & (1 << register)) != 0 && addresses[register] != _entryAddress[register])
                        return false;
                return true;
            }

            private static uint Normalize(uint address) => address >= 0x1000_0000u ? address : address & 0x00FF_FFFFu;

            private static InvalidOperationException Unsupported(M68kDecodedInstruction instruction)
                => new($"Unproved {instruction.Operation} at {instruction.ProgramCounter:X8}.");
        }

        private sealed record FastRamLoopMemory(uint[] ReadValues, byte[] Stack, int StackStart);

        private sealed record FastRamFragmentPlan(
            M68kDecodedInstruction[] Instructions, int[] ReadSlots, int[] StackOffsets, uint[] DivideConstants);

        private readonly record struct FastRamFragmentExit(
            uint ProgramCounter, uint LastProgramCounter, ushort LastOpcode, int StackDisplacement);

        private sealed record FastRamFragment(
            Func<M68kJitCore, int, int, FastRamLoopMemory, int> Run, FastRamFragmentExit[] Exits);

        private static (int Register, int Delta)? FindFastRamLoopInduction(
            M68kDecodedInstruction[] instructions)
        {
            Span<int> deltas = stackalloc int[8];
            deltas.Clear();
            var otherReferences = 0;
            foreach (var instruction in instructions)
            {
                if (IsFastRamLoopCounterUpdate(instruction))
                {
                    deltas[instruction.Destination.Register] += instruction.Operation == M68kJitOperation.Addq
                        ? instruction.QuickValue : -instruction.QuickValue;
                    continue;
                }

                var readData = 0;
                var readAddress = 0;
                var dirtyData = 0;
                var dirtyAddress = 0;
                MarkV2LoadRegisterMasks(instruction, ref readData, ref readAddress);
                MarkV2DirtyRegisterMasks(instruction, ref dirtyData, ref dirtyAddress);
                otherReferences |= readData | dirtyData;
            }

            for (var register = 0; register < deltas.Length; register++)
            {
                var bit = 1 << register;
                if (deltas[register] != 0 && (otherReferences & bit) == 0 &&
                    FastRamLoopFlagsIndependentOfCounter(instructions, register))
                    return (register, deltas[register]);
            }
            return null;
        }

        private static bool IsFastRamLoopCounterUpdate(M68kDecodedInstruction instruction) =>
            instruction.Operation is M68kJitOperation.Addq or M68kJitOperation.Subq &&
            instruction.Size == M68kOperandSize.Long && instruction.Destination.Kind == M68kJitEaKind.DataRegister;

        private static bool FastRamLoopFlagsIndependentOfCounter(M68kDecodedInstruction[] instructions, int register)
        {
            // X is the only CCR input before the terminal branch. A full
            // overwrite makes the whole CCR independent of the counter.
            var dependsOnCounter = false;
            foreach (var instruction in instructions)
            {
                switch (instruction.Operation)
                {
                    case M68kJitOperation.Addq:
                    case M68kJitOperation.Subq:
                        if (instruction.Destination.Kind == M68kJitEaKind.DataRegister)
                            dependsOnCounter = instruction.Destination.Register == register;
                        break;
                    case M68kJitOperation.Add when instruction.Variant != 2:
                    case M68kJitOperation.Addi:
                    case M68kJitOperation.Subi:
                    case M68kJitOperation.Neg:
                        dependsOnCounter = false;
                        break;
                    case M68kJitOperation.ShiftRegister:
                        var type = instruction.Variant & 3;
                        if (type == 2 && dependsOnCounter)
                            return false;
                        if (type != 3 && (instruction.Variant & 8) == 0 && instruction.QuickValue != 0)
                            dependsOnCounter = false;
                        break;
                    case M68kJitOperation.Bcc:
                    case M68kJitOperation.Bra:
                    case M68kJitOperation.Bsr:
                    case M68kJitOperation.Rts:
                    case M68kJitOperation.Add:
                    case M68kJitOperation.And:
                    case M68kJitOperation.Andi:
                    case M68kJitOperation.Clr:
                    case M68kJitOperation.Cmp:
                    case M68kJitOperation.Divs:
                    case M68kJitOperation.Divu:
                    case M68kJitOperation.Eori:
                    case M68kJitOperation.Exg:
                    case M68kJitOperation.ExtLong:
                    case M68kJitOperation.ExtWord:
                    case M68kJitOperation.Lea:
                    case M68kJitOperation.Move:
                    case M68kJitOperation.Movea:
                    case M68kJitOperation.Moveq:
                    case M68kJitOperation.Muls:
                    case M68kJitOperation.Mulu:
                    case M68kJitOperation.Nop:
                    case M68kJitOperation.Not:
                    case M68kJitOperation.Ori:
                    case M68kJitOperation.Swap:
                        break;
                    default:
                        return false;
                }
            }
            return !dependsOnCounter;
        }

        private static Func<M68kJitCore, int, long, FastRamLoopMemory, int> CompileFastRamLoop(
            M68kDecodedInstruction[] instructions, int[] readSlots, int[] stackOffsets,
            uint[] divideConstants)
        {
            var root = instructions[0].ProgramCounter;
            var induction = FindFastRamLoopInduction(instructions);
            var method = new DynamicMethod(
                (induction.HasValue ? "M68kV2FastRamLoopInduction_" : "M68kV2FastRamLoop_") + root.ToString("X6"),
                typeof(int),
                [typeof(M68kJitCore), typeof(int), typeof(long), typeof(FastRamLoopMemory)],
                typeof(M68kJitCore).Module, skipVisibility: true);
            var il = method.GetILGenerator();
            var context = V2EmitContext.Create(il, true, 0xFF, 0xFF, 0xFF, 0xFF);
            var exit = il.DefineLabel();
            var returnZero = il.DefineLabel();
            context.EmitLoadState(root, returnZero, 0xFF, 0xFF);
            var budget = il.DeclareLocal(typeof(long));
            LocalBuilder? entryStatus = null;
            if (induction.HasValue)
            {
                entryStatus = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Ldloc, context.StatusRegister);
                il.Emit(OpCodes.Stloc, entryStatus);
            }
            var (reads, memory, stackStart, stackValue) = EmitFastRamLoopMemoryLocals(il, readSlots);

            var nextLap = il.DefineLabel();
            il.MarkLabel(nextLap);
            context.EmitGetRegisterRunBudget(budget);
            il.Emit(OpCodes.Ldloc, budget);
            il.Emit(OpCodes.Ldc_I8, (long)instructions.Length);
            il.Emit(OpCodes.Blt, exit);
            context.EmitRegisterRun = true;
            var (dead, flagNeeds) = GetFastRamLoopLiveResults(instructions);
            var targets = new Dictionary<uint, int>();
            var finalStackValues = new Dictionary<int, uint>();
            for (var i = 0; i < instructions.Length; i++)
            {
                var instruction = instructions[i];
                switch (instruction.Operation)
                {
                    case M68kJitOperation.Bsr:
                        // Only a matched return can read a pending stack
                        // store inside the proven lap. Forward its value to
                        // the return and keep the last write for each slot.
                        finalStackValues[stackOffsets[i]] = GetInstructionFallthrough(instruction);
                        continue;
                    case M68kJitOperation.Rts:
                    case M68kJitOperation.Bra:
                    case M68kJitOperation.Bcc:
                        continue;
                }
                if (dead[i])
                    continue;

                context.OmitRegisterRunArithmeticFlags = (flagNeeds[i] & 0xF) == 0;
                context.OmitRegisterRunExtend = (flagNeeds[i] & M68kCpuState.Extend) == 0;
                if (divideConstants[i] != 0)
                {
                    EmitBoundNonzeroDivide(il, context, instruction, divideConstants[i]);
                    continue;
                }
                if (readSlots[i] >= 0)
                {
                    EmitFastRamLoopRead(il, context, instruction, reads[readSlots[i]]);
                    continue;
                }
                EmitV2Instruction(il, context, instruction, [], targets, exit, root,
                    root, 0, recordOutOfBlockSideExit: false);
            }
            context.OmitRegisterRunArithmeticFlags = false;
            context.OmitRegisterRunExtend = false;
            context.EmitRegisterRun = false;
            var final = instructions[^1];
            context.EmitRetireRegisterRun(final, instructions.Length);
            if (final.Operation == M68kJitOperation.Bcc)
            {
                context.EmitCheckCondition(final.Condition);
                il.Emit(OpCodes.Brfalse, exit);
            }
            context.EmitSetProgramCounter(root);
            if (induction is { } counter)
                EmitFastRamLoopInduction(il, context, budget, entryStatus!, counter, instructions.Length, nextLap, exit);
            il.Emit(OpCodes.Br, nextLap);
            il.MarkLabel(exit);
            context.EmitReturnZeroIfNoInstructions(returnZero);
            foreach (var pair in finalStackValues)
                EmitBoundStackWrite(il, memory, stackStart, stackValue, pair.Key, pair.Value);
            context.EmitWriteback(0xFF, 0xFF);
            il.Emit(OpCodes.Ldloc, context.Executed);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(returnZero);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            var trace = method.CreateDelegate<Func<M68kJitCore, int, long, FastRamLoopMemory, int>>();
            RuntimeHelpers.PrepareDelegate(trace);
            return trace;
        }

        private static FastRamFragment CompileFastRamFragment(FastRamFragmentPlan plan)
        {
            var instructions = plan.Instructions;
            var root = instructions[0].ProgramCounter;
            var method = new DynamicMethod(
                "M68kV2FastRamFragment_" + root.ToString("X6"), typeof(int),
                [typeof(M68kJitCore), typeof(int), typeof(int), typeof(FastRamLoopMemory)],
                typeof(M68kJitCore).Module, skipVisibility: true);
            var il = method.GetILGenerator();
            var context = V2EmitContext.Create(il, true, 0xFF, 0xFF, 0xFF, 0xFF);
            var exit = il.DefineLabel();
            var writeBack = il.DefineLabel();
            var branchNotTaken = il.DefineLabel();
            var returnZero = il.DefineLabel();
            context.EmitLoadState(root, returnZero, checkEntry: false);
            var (reads, memory, stackStart, stackValue) = EmitFastRamLoopMemoryLocals(il, plan.ReadSlots);
            context.EmitRegisterRun = true;
            var targets = new Dictionary<uint, int>();
            var exits = new FastRamFragmentExit[instructions.Length];
            var labels = new Label[instructions.Length];
            var continuation = il.DeclareLocal(typeof(int));
            for (var i = 0; i < labels.Length; i++)
                labels[i] = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Switch, labels);
            il.Emit(OpCodes.Br, returnZero);
            var stackDisplacement = 0;

            // Every instruction can be the final one before a device edge.
            // Keep its writes and flags, including writes to private stack slots.
            for (var i = 0; i < exits.Length; i++)
            {
                il.MarkLabel(labels[i]);
                if (i != 0)
                {
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Beq, exit);
                }

                var instruction = instructions[i];
                switch (instruction.Operation)
                {
                    case M68kJitOperation.Bsr:
                        stackDisplacement = plan.StackOffsets[i];
                        EmitBoundStackWrite(il, memory, stackStart, stackValue, stackDisplacement,
                            GetInstructionFallthrough(instruction));
                        break;
                    case M68kJitOperation.Rts:
                        stackDisplacement += 4;
                        break;
                    case M68kJitOperation.Bra:
                        break;
                    case M68kJitOperation.Bcc:
                        context.EmitCheckCondition(instruction.Condition);
                        il.Emit(OpCodes.Brfalse, branchNotTaken);
                        break;
                    default:
                        if (plan.DivideConstants[i] != 0)
                            EmitBoundNonzeroDivide(il, context, instruction, plan.DivideConstants[i]);
                        else if (plan.ReadSlots[i] >= 0)
                            EmitFastRamLoopRead(il, context, instruction, reads[plan.ReadSlots[i]]);
                        else
                            EmitV2Instruction(il, context, instruction, [], targets, exit, root, root, 0,
                                recordOutOfBlockSideExit: false);
                        break;
                }
                var next = i + 1 < instructions.Length
                    ? instructions[i + 1].ProgramCounter : GetInstructionFallthrough(instruction);
                exits[i] = new(next, instruction.ProgramCounter, instruction.Opcode, stackDisplacement);
            }
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, continuation);
            il.Emit(OpCodes.Br, writeBack);
            il.MarkLabel(branchNotTaken);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Stloc, continuation);
            il.Emit(OpCodes.Br, writeBack);
            il.MarkLabel(exit);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stloc, continuation);
            il.MarkLabel(writeBack);
            context.EmitRegisterRun = false;
            context.EmitWriteback(0xFF, 0xFF);
            il.Emit(OpCodes.Ldloc, continuation);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(returnZero);
            il.Emit(OpCodes.Ldc_I4, int.MinValue);
            il.Emit(OpCodes.Ret);
            var trace = method.CreateDelegate<Func<M68kJitCore, int, int, FastRamLoopMemory, int>>();
            RuntimeHelpers.PrepareDelegate(trace);
            return new(trace, exits);
        }

        private static (LocalBuilder[] Reads, LocalBuilder Stack, LocalBuilder StackStart, LocalBuilder StackValue)
            EmitFastRamLoopMemoryLocals(ILGenerator il, int[] readSlots)
        {
            var memory = il.DeclareLocal(typeof(byte[]));
            var stackStart = il.DeclareLocal(typeof(int));
            var stackValue = il.DeclareLocal(typeof(uint));
            var readCount = 0;
            foreach (var slot in readSlots)
                readCount = Math.Max(readCount, slot + 1);
            var reads = new LocalBuilder[readCount];
            for (var i = 0; i < reads.Length; i++)
            {
                reads[i] = il.DeclareLocal(typeof(uint));
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, typeof(FastRamLoopMemory).GetProperty(nameof(FastRamLoopMemory.ReadValues))!.GetMethod!);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_U4);
                il.Emit(OpCodes.Stloc, reads[i]);
            }
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, typeof(FastRamLoopMemory).GetProperty(nameof(FastRamLoopMemory.Stack))!.GetMethod!);
            il.Emit(OpCodes.Stloc, memory);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, typeof(FastRamLoopMemory).GetProperty(nameof(FastRamLoopMemory.StackStart))!.GetMethod!);
            il.Emit(OpCodes.Stloc, stackStart);
            return (reads, memory, stackStart, stackValue);
        }

        private static void EmitFastRamLoopInduction(
            ILGenerator il, V2EmitContext context, LocalBuilder budget, LocalBuilder entryStatus,
            (int Register, int Delta) counter, int length, Label nextLap, Label exit)
        {
            // Check the first taken lap against its entry. Memory and return
            // addresses stay fixed within the batch. Once the rest of the CPU
            // state repeats, only the isolated counter changes on later laps.
            il.Emit(OpCodes.Ldloc, context.Executed);
            il.Emit(OpCodes.Ldc_I4, length);
            il.Emit(OpCodes.Bne_Un, nextLap);
            il.Emit(OpCodes.Ldloc, budget);
            il.Emit(OpCodes.Ldc_I8, 2L * length);
            il.Emit(OpCodes.Blt, exit);
            for (var register = 0; register < 8; register++)
            {
                if (register != counter.Register)
                    EmitCompareRegister(context.DataRegisters[register], context.DataRegisterArray, register);
                EmitCompareRegister(context.AddressRegisters[register], context.AddressRegisterArray, register);
            }
            context.EmitMaterializePendingFlags();
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(OpCodes.Ldloc, entryStatus);
            il.Emit(OpCodes.Bne_Un, nextLap);

            var extraLaps = il.DeclareLocal(typeof(int));
            var extraRetired = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, budget);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, length);
            il.Emit(OpCodes.Div);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, extraLaps);

            var inductionRegister = context.DataRegisters[counter.Register];
            il.Emit(OpCodes.Ldloc, inductionRegister);
            il.Emit(OpCodes.Ldloc, extraLaps);
            il.Emit(OpCodes.Ldc_I4, counter.Delta);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, inductionRegister);

            il.Emit(OpCodes.Ldloc, extraLaps);
            il.Emit(OpCodes.Ldc_I4, length);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Stloc, extraRetired);
            il.Emit(OpCodes.Ldloc, context.Executed);
            il.Emit(OpCodes.Ldloc, extraRetired);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, context.Executed);
            il.Emit(OpCodes.Ldloc, context.Cycles);
            il.Emit(OpCodes.Ldloc, extraRetired);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, context.Cycles);
            il.Emit(OpCodes.Br, exit);

            void EmitCompareRegister(LocalBuilder current, LocalBuilder initial, int index)
            {
                il.Emit(OpCodes.Ldloc, current);
                il.Emit(OpCodes.Ldloc, initial);
                il.Emit(OpCodes.Ldc_I4, index);
                il.Emit(OpCodes.Ldelem_U4);
                il.Emit(OpCodes.Bne_Un, nextLap);
            }
        }

        private static void EmitFastRamLoopRead(
            ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction, LocalBuilder source)
        {
            if (instruction.Operation == M68kJitOperation.Move)
            {
                context.EmitStoreDataRegister(instruction.Destination.Register, source, M68kOperandSize.Long);
                context.EmitSetPendingLogic(source, M68kOperandSize.Long);
                return;
            }

            var destination = il.DeclareLocal(typeof(uint));
            var result = il.DeclareLocal(typeof(uint));
            context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Long);
            il.Emit(OpCodes.Stloc, destination);
            EmitV2ArithmeticResult(il, destination, source, result, M68kOperandSize.Long, add: false);
            context.EmitSetPendingArithmetic(V2PendingFlags.Subtract, destination, source,
                result, M68kOperandSize.Long, setExtend: false);
        }

        private static void EmitBoundStackWrite(
            ILGenerator il, LocalBuilder memory, LocalBuilder stackStart, LocalBuilder value,
            int displacement, uint continuation)
        {
            var offset = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, stackStart);
            il.Emit(OpCodes.Ldc_I4, displacement);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, offset);
            EmitLoadUIntConstant(il, continuation);
            il.Emit(OpCodes.Stloc, value);
            EmitStoreV2ZeroWaitMemoryValue(il, memory, offset, value, M68kOperandSize.Long);
        }

        private static void EmitBoundNonzeroDivide(
            ILGenerator il, V2EmitContext context, M68kDecodedInstruction instruction, uint divisor)
        {
            var packed = il.DeclareLocal(typeof(ulong));
            var value = il.DeclareLocal(typeof(uint));
            context.EmitMaterializePendingFlags();
            context.EmitLoadDataRegister(instruction.Register, M68kOperandSize.Long);
            EmitLoadUIntConstant(il, divisor);
            il.Emit(OpCodes.Ldloc, context.StatusRegister);
            il.Emit(instruction.Operation == M68kJitOperation.Divs ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
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
        }

        private static (bool[] Dead, int[] Flags) GetFastRamLoopLiveResults(
            M68kDecodedInstruction[] instructions)
        {
            var dead = new bool[instructions.Length];
            var flagNeeds = new int[instructions.Length];

            var liveFlags = 0x1F;
            var liveData = 0xFF;
            var liveAddress = 0xFF;
            for (var i = instructions.Length - 1; i >= 0; i--)
            {
                var instruction = instructions[i];
                if (instruction.Operation is M68kJitOperation.Bra or M68kJitOperation.Bsr or M68kJitOperation.Rts)
                    continue;
                if (instruction.Operation == M68kJitOperation.Bcc)
                {
                    liveFlags |= 0xF;
                    continue;
                }

                var writtenData = 0;
                var writtenAddress = 0;
                MarkV2DirtyRegisterMasks(instruction, ref writtenData, ref writtenAddress);
                var flags = instruction.Operation switch
                {
                    M68kJitOperation.Lea => 0,
                    M68kJitOperation.Neg => 0x1F,
                    M68kJitOperation.ShiftRegister when (instruction.Variant & 3) != 3 => 0x1F,
                    _ => WrittenRegisterRunFlags(instruction),
                };
                flagNeeds[i] = liveFlags & flags;
                if ((liveData & writtenData) == 0 && (liveAddress & writtenAddress) == 0 && flagNeeds[i] == 0)
                {
                    dead[i] = true;
                    continue;
                }
                liveFlags &= ~flags;
                if (instruction.Operation is M68kJitOperation.Divs or M68kJitOperation.Divu or
                    M68kJitOperation.ShiftRegister)
                    liveFlags |= 0x1F;

                switch (instruction.Operation)
                {
                    case M68kJitOperation.Moveq:
                        liveData &= ~writtenData;
                        break;
                    case M68kJitOperation.Move when instruction.Size == M68kOperandSize.Long:
                        liveData &= ~writtenData;
                        MarkV2EaRegisterReferences(instruction.Source, ref liveData, ref liveAddress);
                        break;
                    case M68kJitOperation.Clr when instruction.Size == M68kOperandSize.Long:
                        liveData &= ~writtenData;
                        break;
                    case M68kJitOperation.Lea:
                    case M68kJitOperation.Movea:
                        liveAddress &= ~writtenAddress;
                        MarkV2EaRegisterReferences(instruction.Source, ref liveData, ref liveAddress);
                        break;
                    default:
                        MarkV2LoadRegisterMasks(instruction, ref liveData, ref liveAddress);
                        break;
                }
            }
            return (dead, flagNeeds);
        }

    }
}
